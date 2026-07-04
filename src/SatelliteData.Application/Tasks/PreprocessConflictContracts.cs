using System.Text.Json.Serialization;

namespace SatelliteData.Application.Tasks;

public sealed record PreprocessConflictDetailDto(
    [property: JsonPropertyName("param_id")] string ParamId,
    [property: JsonPropertyName("param_label")] string? ParamLabel,
    [property: JsonPropertyName("conflict_status")] string ConflictStatus,
    [property: JsonPropertyName("conflict_run_id")] Guid ConflictRunId,
    [property: JsonPropertyName("conflict_job_id")] string? ConflictJobId,
    [property: JsonPropertyName("conflict_filter_template_id")] Guid ConflictFilterTemplateId,
    [property: JsonPropertyName("conflict_filter_template_version")] int ConflictFilterTemplateVersion,
    [property: JsonPropertyName("conflict_filter_template_name")] string? ConflictFilterTemplateName);

public sealed record PreprocessConflictPayloadDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("conflicts")] IReadOnlyList<PreprocessConflictDetailDto> Conflicts,
    [property: JsonPropertyName("on_active_conflict")] string? OnActiveConflict,
    [property: JsonPropertyName("on_committed_conflict")] string? OnCommittedConflict);
