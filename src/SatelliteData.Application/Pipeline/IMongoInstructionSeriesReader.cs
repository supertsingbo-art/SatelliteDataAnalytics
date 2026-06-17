namespace SatelliteData.Application.Pipeline;

/// <summary>从海量 Mongo 库 <c>IndicatorCollection</c> 读取指令历史（<c>ci</c>/<c>et</c>）。</summary>
public interface IMongoInstructionSeriesReader
{
    Task<IReadOnlyList<InstructionHistoryPoint>> ReadHistoryAsync(
        string mongoUri,
        string databaseName,
        string collectionName,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<InstructionHistoryLookup> lookups,
        CancellationToken cancellationToken);
}
