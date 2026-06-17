using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.Mongo;

public sealed class MongoConditionHistoryProvider(
    MongoConnectionPool mongoPool,
    IMongoPkgSeriesReader mongoPkgReader,
    IMongoInstructionSeriesReader mongoInstructionReader,
    IOptions<PipelineOptions> pipelineOptions) : IConditionHistoryProvider
{
    private readonly PipelineOptions _pipelineOptions = pipelineOptions.Value;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>>> QueryParameterSeriesAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<ParameterHistoryLookup> lookups,
        CancellationToken cancellationToken)
    {
        var result = lookups.ToDictionary(
            x => x.ParamId,
            _ => (IReadOnlyList<RawSeriesPoint>)Array.Empty<RawSeriesPoint>(),
            StringComparer.Ordinal);
        if (lookups.Count == 0)
        {
            return result;
        }

        var (mongoUri, mongoDb) = await ResolveMongoAsync(tasookNo, satelliteNo, cancellationToken);
        foreach (var lookup in lookups)
        {
            var series = await mongoPkgReader.ReadSeriesAsync(
                mongoUri,
                mongoDb,
                lookup.PrmSysId,
                lookup.ParaId,
                windowStart,
                windowEnd,
                cancellationToken);
            result[lookup.ParamId] = series;
        }

        return result;
    }

    public async Task<IReadOnlyList<InstructionHistoryPoint>> QueryInstructionHistoryAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<InstructionHistoryLookup> lookups,
        CancellationToken cancellationToken)
    {
        if (lookups.Count == 0)
        {
            return Array.Empty<InstructionHistoryPoint>();
        }

        var (mongoUri, mongoDb) = await ResolveMongoAsync(tasookNo, satelliteNo, cancellationToken);
        return await mongoInstructionReader.ReadHistoryAsync(
            mongoUri,
            mongoDb,
            _pipelineOptions.MongoInstructionCollection,
            windowStart,
            windowEnd,
            lookups,
            cancellationToken);
    }

    private async Task<(string MongoUri, string MongoDb)> ResolveMongoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var mongoInfo = await mongoPool.GetConnectionInfoAsync(tasookNo, satelliteNo, cancellationToken);
        var mongoDb = string.IsNullOrWhiteSpace(mongoInfo.DbName) ? "test" : mongoInfo.DbName;
        return (mongoInfo.MongoUri, mongoDb);
    }
}
