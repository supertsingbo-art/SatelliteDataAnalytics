using Microsoft.Extensions.Options;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.Mongo;

public sealed class MongoConditionHistoryProvider(
    IMongoPkgSeriesReader mongoPkgReader,
    IMongoInstructionSeriesReader mongoInstructionReader,
    IOptions<PipelineOptions> pipelineOptions) : IConditionHistoryProvider
{
    private readonly PipelineOptions _pipelineOptions = pipelineOptions.Value;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>>> QueryParameterSeriesAsync(
        string mongoUri,
        string mongoDb,
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
        string mongoUri,
        string mongoDb,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<InstructionHistoryLookup> lookups,
        CancellationToken cancellationToken)
    {
        if (lookups.Count == 0)
        {
            return Array.Empty<InstructionHistoryPoint>();
        }

        return await mongoInstructionReader.ReadHistoryAsync(
            mongoUri,
            mongoDb,
            _pipelineOptions.MongoInstructionCollection,
            windowStart,
            windowEnd,
            lookups,
            cancellationToken);
    }
}
