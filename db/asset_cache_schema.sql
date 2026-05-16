-- 资产中心缓存表（与 PgAssetCacheRepository 首次启动 DDL 对齐；可手工在 PostgreSQL 中执行）
-- 详见 appsettings.json 中 ConnectionStrings:Postgres 与 AssetCache:UsePostgreSql

CREATE TABLE IF NOT EXISTS satellite_cache (
    tasook_no varchar(64) NOT NULL,
    tasook_name varchar(256),
    satellite_no varchar(64) NOT NULL,
    satellite_name varchar(256) NOT NULL,
    satellite_type varchar(128),
    db_stage varchar(64),
    mongo_uri text,
    mongo_db_name varchar(256),
    mongo_auth_ref varchar(256),
    source_version varchar(128),
    last_synced_at timestamptz NOT NULL,
    cached_parameter_count int NOT NULL DEFAULT 0,
    cached_command_count int NOT NULL DEFAULT 0,
    raw_json jsonb NOT NULL,
    PRIMARY KEY (tasook_no, satellite_no)
);

CREATE TABLE IF NOT EXISTS param_cache (
    tasook_no varchar(64) NOT NULL,
    satellite_no varchar(64) NOT NULL,
    param_id varchar(128) NOT NULL,
    param_name varchar(256) NOT NULL,
    unit varchar(64),
    value_type varchar(32),
    value_min double precision,
    value_max double precision,
    source_version varchar(128),
    last_synced_at timestamptz NOT NULL,
    raw_json jsonb NOT NULL,
    PRIMARY KEY (tasook_no, satellite_no, param_id)
);

CREATE TABLE IF NOT EXISTS command_cache (
    tasook_no varchar(64) NOT NULL,
    satellite_no varchar(64) NOT NULL,
    command_id varchar(128) NOT NULL,
    command_name varchar(256) NOT NULL,
    source_version varchar(128),
    last_synced_at timestamptz NOT NULL,
    raw_json jsonb NOT NULL,
    PRIMARY KEY (tasook_no, satellite_no, command_id)
);

CREATE TABLE IF NOT EXISTS test_batch_cache (
    tasook_no varchar(64) NOT NULL,
    satellite_no varchar(64) NOT NULL,
    test_batch_id varchar(128) NOT NULL,
    scenario varchar(256),
    start_ts timestamptz NOT NULL,
    end_ts timestamptz NOT NULL,
    source_version varchar(128),
    last_synced_at timestamptz NOT NULL,
    raw_json jsonb NOT NULL,
    PRIMARY KEY (tasook_no, satellite_no, test_batch_id)
);

ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS cached_parameter_count integer NOT NULL DEFAULT 0;
ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS cached_command_count integer NOT NULL DEFAULT 0;
ALTER TABLE satellite_cache ADD COLUMN IF NOT EXISTS tasook_name varchar(256);
