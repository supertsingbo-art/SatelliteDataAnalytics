using System.Text.Json;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed record CreateAlgorithmTemplateRequest(
    string TemplateName,
    JsonElement ReactFlowJson,
    JsonElement ConfigJson,
    string? Description);

public sealed record UpdateAlgorithmTemplateRequest(
    string TemplateName,
    JsonElement ReactFlowJson,
    JsonElement ConfigJson,
    string? Description);

public sealed record AlgorithmTemplateView(
    Guid TemplateId,
    int Version,
    string TemplateName,
    TemplateStatus Status,
    int NodeCount,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);

public sealed record AlgorithmTemplateDetail(
    AlgorithmTemplateView View,
    JsonElement ReactFlowJson,
    JsonElement ConfigJson);

public sealed record AlgorithmTemplateListRequest(
    TemplateStatus? Status,
    string? Keyword,
    int PageNo = 1,
    int PageSize = 20);

public sealed record AlgorithmTemplateDeleteImpact(
    Guid TemplateId,
    string TemplateName,
    int VersionCount,
    int TaskRunCount,
    int RunningTaskRunCount,
    IReadOnlyList<Guid> TaskRunIds)
{
    public bool HasReferences => TaskRunCount > 0;
}

public sealed record AlgorithmTemplateValidationResult(
    bool Valid,
    int NodeCount,
    int EdgeCount,
    IReadOnlyList<AlgorithmTemplateValidationIssue> Issues);

public sealed record AlgorithmTemplateValidationIssue(
    string Code,
    string Message,
    string? NodeId);

public sealed record AlgorithmTemplateTrialRunRequest(
    string TasookNo,
    string SatelliteNo,
    string? TestBatchId,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd);

public sealed record AlgorithmTemplateTrialRunResponse(
    Guid RunId,
    string Status,
    string Message);

public interface IAlgorithmTemplateRepository
{
    Task<IReadOnlyCollection<AlgorithmTemplate>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AlgorithmTemplate>> GetByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken);

    Task<AlgorithmTemplate?> GetVersionAsync(Guid templateId, int version, CancellationToken cancellationToken);

    Task<int> GetMaxVersionAsync(Guid templateId, CancellationToken cancellationToken);

    Task SaveAsync(AlgorithmTemplate template, CancellationToken cancellationToken);

    Task DeleteAsync(Guid templateId, int version, CancellationToken cancellationToken);

    Task DeleteAllByTemplateIdAsync(Guid templateId, CancellationToken cancellationToken);
}
