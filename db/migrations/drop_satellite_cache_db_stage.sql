-- 移除 satellite_cache.db_stage（海量 Web API v2 主键为 taskNo + satNo，不再使用 dbStage）
ALTER TABLE satellite_cache DROP COLUMN IF EXISTS db_stage;
