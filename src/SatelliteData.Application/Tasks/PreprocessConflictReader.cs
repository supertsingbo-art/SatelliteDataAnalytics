using System.Text.Json;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

/// <summary>从 task.failed 事件 payload 读取 PRE_006 结构化冲突明细。</summary>
public sealed class PreprocessConflictReader(ITaskEventRepository taskEvents)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<PreprocessConflictDetailDto>?> TryGetConflictDetailsAsync(
        TaskRun run,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(run.ErrorCode, "PRE_006", StringComparison.Ordinal))
        {
            return null;
        }

        var evt = await taskEvents.GetLatestFailedEventAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        if (evt?.PayloadJson is null or "")
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<PreprocessConflictPayloadDto>(evt.PayloadJson, JsonOptions);
            return payload?.Conflicts is { Count: > 0 } conflicts ? conflicts : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
