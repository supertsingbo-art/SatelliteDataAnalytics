namespace SatelliteData.Application.Pipeline;

public sealed record ParameterHistoryLookup(
    string ParamId,
    int ParaId,
    int PrmSysId);

public sealed record InstructionHistoryLookup(
    string CommandId,
    int CmdId,
    int ChannelId);

public sealed record InstructionHistoryPoint(
    string CommandId,
    int CmdId,
    int ChannelId,
    DateTimeOffset ExecuteTime);

public interface IConditionHistoryProvider
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<RawSeriesPoint>>> QueryParameterSeriesAsync(
        string mongoUri,
        string mongoDb,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<ParameterHistoryLookup> lookups,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<InstructionHistoryPoint>> QueryInstructionHistoryAsync(
        string mongoUri,
        string mongoDb,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<InstructionHistoryLookup> lookups,
        CancellationToken cancellationToken);
}
