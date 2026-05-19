-- 任务编排、元数据、算法包、Webhook 投递（详细设计 §4.1.8–4.1.16、§4.1.13、§4.1.15）
-- Hangfire 表由 Hangfire.PostgreSql 在运行时自动创建，勿手工维护 hangfire schema。

CREATE TABLE IF NOT EXISTS object_index (
    object_id uuid PRIMARY KEY,
    bucket varchar(128) NOT NULL,
    object_key text NOT NULL,
    object_version varchar(128),
    checksum varchar(128),
    content_type varchar(128),
    file_size bigint,
    ref_run_id uuid,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS task_run (
    run_id uuid PRIMARY KEY,
    parent_run_id uuid,
    job_id varchar(128) UNIQUE,
    job_type varchar(32) NOT NULL,
    trigger_type varchar(32) NOT NULL,
    status varchar(32) NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    tasook_no varchar(64) NOT NULL,
    satellite_no varchar(64) NOT NULL,
    -- 展示用阶段名（可为 test_batch_cache.test_batch_name 或「自定义时间段」），非外键；处理以 window_start/end 为准
    test_batch_name varchar(256),
    window_start timestamptz,
    window_end timestamptz,
    filter_template_id uuid,
    filter_template_version int,
    algorithm_template_id uuid,
    algorithm_template_version int,
    report_template_id uuid,
    report_template_version int,
    progress_percent numeric(5,2) NOT NULL DEFAULT 0,
    current_step varchar(128),
    start_time timestamptz,
    end_time timestamptz,
    timeout_flag boolean NOT NULL DEFAULT false,
    error_code varchar(64),
    error_msg text,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (idempotency_key)
);

CREATE INDEX IF NOT EXISTS idx_task_run_status_created ON task_run(status, created_at);
CREATE INDEX IF NOT EXISTS idx_task_run_satellite ON task_run(tasook_no, satellite_no);

CREATE TABLE IF NOT EXISTS task_event (
    event_id uuid PRIMARY KEY,
    run_id uuid NOT NULL,
    event_type varchar(64) NOT NULL,
    event_status varchar(32) NOT NULL,
    payload_json jsonb,
    error_code varchar(64),
    error_msg text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_task_event_run ON task_event(run_id, created_at);

CREATE TABLE IF NOT EXISTS task_compensation (
    compensation_id uuid PRIMARY KEY,
    run_id uuid NOT NULL,
    compensation_type varchar(64) NOT NULL,
    status varchar(32) NOT NULL,
    retry_count int NOT NULL DEFAULT 0,
    next_retry_at timestamptz,
    payload_json jsonb NOT NULL,
    last_error text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS hq_param_metadata (
    metadata_id uuid PRIMARY KEY,
    run_id uuid NOT NULL,
    tasook_no varchar(64) NOT NULL,
    satellite_no varchar(64) NOT NULL,
    test_batch_id varchar(128) NOT NULL,
    param_id varchar(128) NOT NULL,
    window_start timestamptz NOT NULL,
    window_end timestamptz NOT NULL,
    filter_template_id uuid NOT NULL,
    filter_template_version int NOT NULL,
    outlier_method varchar(64),
    outlier_reason_pattern text,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_hq_meta_run ON hq_param_metadata(run_id);

CREATE TABLE IF NOT EXISTS algorithm_package (
    package_id uuid PRIMARY KEY,
    algorithm_code varchar(128) NOT NULL,
    algorithm_name varchar(256) NOT NULL,
    algorithm_category varchar(32) NOT NULL,
    version varchar(64) NOT NULL,
    runtime varchar(32) NOT NULL,
    entrypoint varchar(256) NOT NULL,
    object_id uuid NOT NULL,
    manifest_json jsonb NOT NULL,
    inputs_schema_json jsonb NOT NULL,
    outputs_schema_json jsonb NOT NULL,
    params_schema_json jsonb,
    resources_json jsonb NOT NULL,
    status varchar(32) NOT NULL,
    last_error text,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid,
    updated_at timestamptz NOT NULL DEFAULT now(),
    approved_by uuid,
    approved_at timestamptz,
    published_at timestamptz,
    CONSTRAINT uk_algorithm_package_code_version UNIQUE (algorithm_code, version),
    CONSTRAINT ck_algorithm_package_status CHECK (status IN ('Draft','SandboxValidating','Published','Rejected','Archived')),
    CONSTRAINT ck_algorithm_package_runtime CHECK (runtime IN ('BUILTIN','PYTHON','JS')),
    CONSTRAINT ck_algorithm_package_category CHECK (algorithm_category IN ('source','stats','spectrum','align','cluster','compare','output'))
);

CREATE INDEX IF NOT EXISTS idx_algorithm_package_code ON algorithm_package(algorithm_code);
CREATE INDEX IF NOT EXISTS idx_algorithm_package_status ON algorithm_package(status);
CREATE INDEX IF NOT EXISTS idx_algorithm_package_category ON algorithm_package(algorithm_category);

CREATE TABLE IF NOT EXISTS client_callbacks (
    callback_id uuid PRIMARY KEY,
    client_id uuid,
    callback_name varchar(256) NOT NULL,
    callback_url text NOT NULL,
    secret_ref varchar(256) NOT NULL,
    event_types jsonb NOT NULL DEFAULT '["job.succeeded","job.failed","job.timeout"]'::jsonb,
    max_retry_count int NOT NULL DEFAULT 5,
    enabled boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS callback_deliveries (
    delivery_id uuid PRIMARY KEY,
    event_id varchar(128) NOT NULL UNIQUE,
    callback_id uuid NOT NULL REFERENCES client_callbacks(callback_id),
    run_id uuid,
    event_type varchar(64) NOT NULL,
    payload_json jsonb NOT NULL,
    status varchar(32) NOT NULL,
    retry_count int NOT NULL DEFAULT 0,
    next_retry_at timestamptz,
    response_status int,
    response_body text,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_callback_deliveries_run ON callback_deliveries(run_id);

-- 内置算法包种子（§6.5.4.5）：object_id 与 object_index 对齐；重复执行脚本时用 ON CONFLICT 跳过
INSERT INTO object_index (object_id, bucket, object_key, content_type, file_size, created_at)
VALUES
    ('00000000-0000-0000-0001-000000000001'::uuid, 'builtin', 'algorithm/max/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000002'::uuid, 'builtin', 'algorithm/min/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000003'::uuid, 'builtin', 'algorithm/mean/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000004'::uuid, 'builtin', 'algorithm/variance/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000005'::uuid, 'builtin', 'algorithm/stddev/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000006'::uuid, 'builtin', 'algorithm/envelope/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000007'::uuid, 'builtin', 'algorithm/rms/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000008'::uuid, 'builtin', 'algorithm/fft/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-000000000009'::uuid, 'builtin', 'algorithm/psd/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-00000000000a'::uuid, 'builtin', 'algorithm/dominant_freq/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-00000000000b'::uuid, 'builtin', 'algorithm/threshold_judge/1.0.0', 'application/json', 0, now()),
    ('00000000-0000-0000-0001-00000000000c'::uuid, 'builtin', 'algorithm/three_sigma_judge/1.0.0', 'application/json', 0, now())
ON CONFLICT (object_id) DO NOTHING;

INSERT INTO algorithm_package (
    package_id, algorithm_code, algorithm_name, algorithm_category, version, runtime, entrypoint, object_id,
    manifest_json, inputs_schema_json, outputs_schema_json, params_schema_json, resources_json, status, created_at, updated_at, published_at
) VALUES
    ('10000000-0000-0000-0001-000000000001'::uuid, 'max', '最大值', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000001'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"value":"Scalar"}', '{}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000002'::uuid, 'min', '最小值', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000002'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"value":"Scalar"}', '{}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000003'::uuid, 'mean', '平均值', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000003'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"value":"Scalar"}', '{}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000004'::uuid, 'variance', '方差', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000004'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"value":"Scalar"}', '{"ddof":1}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000005'::uuid, 'stddev', '标准差', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000005'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"value":"Scalar"}', '{"ddof":1}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000006'::uuid, 'envelope', '包络线', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000006'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"envelope":"Series"}', '{"windowSeconds":5,"mode":"minmax"}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000007'::uuid, 'rms', '均方根值', 'stats', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000007'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"value":"Scalar"}', '{}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000008'::uuid, 'fft', '快速傅里叶变换', 'spectrum', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000008'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"spectrum":"Spectrum"}', '{"sampleRate":1,"window":"hann"}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-000000000009'::uuid, 'psd', '功率谱密度', 'spectrum', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-000000000009'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"spectrum":"Spectrum"}', '{"nperseg":256,"overlap":0.5}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-00000000000a'::uuid, 'dominant_freq', '主频提取', 'spectrum', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-00000000000a'::uuid,
        '{}', '{"spectrum":"Spectrum"}', '{"value":"Scalar"}', '{"topK":1}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-00000000000b'::uuid, 'threshold_judge', '阈值判定', 'output', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-00000000000b'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"result":"algo_result"}', '{}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now()),
    ('10000000-0000-0000-0001-00000000000c'::uuid, 'three_sigma_judge', '3σ判定', 'output', '1.0.0', 'BUILTIN', '__builtin__', '00000000-0000-0000-0001-00000000000c'::uuid,
        '{}', '{"series":"TimeSeries"}', '{"result":"algo_result"}', '{"k":3}', '{"cpu":1,"memoryMb":512,"timeoutSeconds":600}', 'Published', now(), now(), now())
ON CONFLICT (package_id) DO NOTHING;

INSERT INTO client_callbacks (callback_id, client_id, callback_name, callback_url, secret_ref, event_types, max_retry_count, enabled, created_at)
VALUES (
    '20000000-0000-0000-0000-000000000001'::uuid,
    NULL,
    'dev-null',
    'https://httpbin.org/post',
    'dev-secret',
    '["job.succeeded","job.failed","job.timeout"]'::jsonb,
    3,
    false,
    now()
)
ON CONFLICT (callback_id) DO NOTHING;
