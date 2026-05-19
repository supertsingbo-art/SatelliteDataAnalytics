namespace SatelliteData.Application.Pipeline;

/// <summary>从海量 Mongo 库按 pkg 集合（pkg{prm_sys_id}）读取参数时序。</summary>
public interface IMongoPkgSeriesReader
{
    Task<IReadOnlyList<RawSeriesPoint>> ReadSeriesAsync(
        string mongoUri,
        string databaseName,
        int prmSysId,
        int paraId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken);
}
