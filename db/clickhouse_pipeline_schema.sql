-- 高品质明细与算法结果（详细设计 §4.2.1–4.2.3）
-- 在 ClickHouse 中执行（clickhouse-client 或 HTTP DDL）。

CREATE TABLE IF NOT EXISTS hq_param_point
(
    tasook_no LowCardinality(String),
    satellite_no LowCardinality(String),
    test_batch_id LowCardinality(String),
    param_id LowCardinality(String),
    ts DateTime64(3, 'UTC'),
    raw_value Nullable(Float64),
    processed_value Nullable(Float64),
    is_outlier UInt8,
    version UInt64,
    ingested_at DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = ReplacingMergeTree(version)
PARTITION BY (toYYYYMM(ts), tasook_no, satellite_no)
ORDER BY (tasook_no, satellite_no, test_batch_id, param_id, ts);

CREATE TABLE IF NOT EXISTS algo_result
(
    run_id UUID,
    node_id String,
    algorithm_code LowCardinality(String),
    tasook_no LowCardinality(String),
    satellite_no LowCardinality(String),
    test_batch_id LowCardinality(String),
    window_start DateTime64(3, 'UTC'),
    window_end DateTime64(3, 'UTC'),
    metric_name LowCardinality(String),
    metric_value Float64,
    detail_json String,
    created_at DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = MergeTree
PARTITION BY toYYYYMM(window_start)
ORDER BY (run_id, node_id, metric_name);

CREATE TABLE IF NOT EXISTS algo_cluster_labels
(
    run_id UUID,
    node_id String,
    tasook_no LowCardinality(String),
    satellite_no LowCardinality(String),
    test_batch_id LowCardinality(String),
    ts DateTime64(3, 'UTC'),
    sample_id String,
    cluster_id Int32,
    is_noise UInt8,
    distance_to_center Float64,
    created_at DateTime64(3, 'UTC') DEFAULT now64(3)
)
ENGINE = MergeTree
PARTITION BY (toYYYYMM(ts), tasook_no, satellite_no)
ORDER BY (run_id, node_id, cluster_id, ts);
