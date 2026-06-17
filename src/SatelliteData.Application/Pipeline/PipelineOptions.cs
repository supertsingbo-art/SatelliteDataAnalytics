namespace SatelliteData.Application.Pipeline;

public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Hangfire 存储：Memory（仅同进程 Api+Worker）、PostgreSql。</summary>
    public string Storage { get; init; } = "Memory";

    /// <summary>在 Api 进程内启动 Hangfire Server（开发默认 true）。生产应 false 并由 Workers 承载。</summary>
    public bool RunWorkerInApi { get; init; } = true;

    /// <summary>任务元数据使用 PostgreSQL。</summary>
    public bool UsePostgreSqlTaskStore { get; init; }

    /// <summary>已废弃：不再注入与参数无关的合成时序，无 Mongo 数据时应任务失败。</summary>
    public bool SyntheticMongoWhenEmpty { get; init; }

    public string MongoRawCollection { get; init; } = "parameter_raw";

    /// <summary>指令历史 Mongo 集合名（默认与海量/AIRTP 一致）。</summary>
    public string MongoInstructionCollection { get; init; } = "IndicatorCollection";

    public int ClickHouseBatchSize { get; init; } = 100_000;
}
