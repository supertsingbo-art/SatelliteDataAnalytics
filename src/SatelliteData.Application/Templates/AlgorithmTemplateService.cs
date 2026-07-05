using System.Text.Json;
using SatelliteData.Application.Assets;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

public sealed class AlgorithmTemplateService(
    IAlgorithmTemplateRepository templateRepository,
    AlgorithmTemplateValidator validator,
    ITaskRunRepository taskRuns,
    TaskOrchestrator taskOrchestrator,
    TaskRunLifecycleService taskRunLifecycleService)
{
    public async Task<PagedResult<AlgorithmTemplateView>> ListAsync(
        AlgorithmTemplateListRequest request,
        CancellationToken cancellationToken)
    {
        var pageNo = request.PageNo <= 0 ? 1 : request.PageNo;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);

        var all = await templateRepository.GetAllAsync(cancellationToken);
        var representatives = all
            .GroupBy(t => t.TemplateId)
            .Select(grp =>
                grp.FirstOrDefault(t => t.Status == TemplateStatus.Published)
                ?? grp.OrderByDescending(t => t.Version).First())
            .ToList();

        if (request.Status.HasValue)
        {
            representatives = representatives.Where(t => t.Status == request.Status.Value).ToList();
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword!;
            representatives = representatives
                .Where(t => t.TemplateName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var ordered = representatives.OrderByDescending(t => t.UpdatedAt).ToArray();
        var paged = ordered.Skip((pageNo - 1) * pageSize).Take(pageSize).ToArray();

        return new PagedResult<AlgorithmTemplateView>(
            pageNo, pageSize, ordered.Length,
            paged.Select(ToView).ToArray());
    }

    public async Task<IReadOnlyCollection<AlgorithmTemplateView>> GetVersionsAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var versions = await templateRepository.GetByTemplateIdAsync(templateId, cancellationToken);
        if (versions.Count == 0)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板不存在");
        }
        return versions.OrderByDescending(t => t.Version).Select(ToView).ToArray();
    }

    public async Task<AlgorithmTemplateDetail> GetVersionDetailAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        var template = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");
        return new AlgorithmTemplateDetail(ToView(template), template.ReactFlowJson, template.ConfigJson);
    }

    public async Task<AlgorithmTemplateDetail> CreateAsync(
        CreateAlgorithmTemplateRequest request,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request.ReactFlowJson, request.ConfigJson, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var template = new AlgorithmTemplate(
            TemplateId: Guid.NewGuid(),
            Version: 1,
            TemplateName: request.TemplateName.Trim(),
            Status: TemplateStatus.Draft,
            ReactFlowJson: request.ReactFlowJson,
            ConfigJson: request.ConfigJson,
            NodeCount: validation.NodeCount,
            Description: request.Description,
            CreatedBy: operatorId,
            CreatedAt: now,
            UpdatedBy: operatorId,
            UpdatedAt: now,
            PublishedAt: null);

        await templateRepository.SaveAsync(template, cancellationToken);
        return new AlgorithmTemplateDetail(ToView(template), template.ReactFlowJson, template.ConfigJson);
    }

    public async Task<AlgorithmTemplateDetail> UpdateAsync(
        Guid templateId,
        int version,
        UpdateAlgorithmTemplateRequest request,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");

        if (existing.Status != TemplateStatus.Draft)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateNotEditable,
                "仅 Draft 状态版本可直接编辑；请先克隆为新版本");
        }

        var validation = await validator.ValidateAsync(request.ReactFlowJson, request.ConfigJson, cancellationToken);

        var updated = existing with
        {
            TemplateName = request.TemplateName.Trim(),
            ReactFlowJson = request.ReactFlowJson,
            ConfigJson = request.ConfigJson,
            NodeCount = validation.NodeCount,
            Description = request.Description,
            UpdatedBy = operatorId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await templateRepository.SaveAsync(updated, cancellationToken);
        return new AlgorithmTemplateDetail(ToView(updated), updated.ReactFlowJson, updated.ConfigJson);
    }

    public async Task<AlgorithmTemplateValidationResult> ValidateAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");

        return await validator.ValidateAsync(existing.ReactFlowJson, existing.ConfigJson, cancellationToken);
    }

    public async Task<AlgorithmTemplateView> PublishAsync(
        Guid templateId,
        int version,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");

        if (existing.Status != TemplateStatus.Draft)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateInvalidState,
                "只有 Draft 状态可以发布");
        }

        var validation = await validator.ValidateAsync(existing.ReactFlowJson, existing.ConfigJson, cancellationToken);
        if (!validation.Valid)
        {
            var firstIssue = validation.Issues.FirstOrDefault();
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateDagInvalid,
                "DAG 校验未通过：" + (firstIssue?.Message ?? "未知错误"));
        }

        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            Status = TemplateStatus.Published,
            PublishedAt = now,
            UpdatedBy = operatorId,
            UpdatedAt = now
        };
        await templateRepository.SaveAsync(updated, cancellationToken);
        return ToView(updated);
    }

    public async Task<AlgorithmTemplateView> ArchiveAsync(
        Guid templateId,
        int version,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");

        if (existing.Status == TemplateStatus.Archived)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateInvalidState,
                "已归档版本不能再次归档");
        }

        var updated = existing with
        {
            Status = TemplateStatus.Archived,
            UpdatedBy = operatorId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await templateRepository.SaveAsync(updated, cancellationToken);
        return ToView(updated);
    }

    public async Task<AlgorithmTemplateDetail> CloneAsync(
        Guid templateId,
        int? sourceVersion,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        AlgorithmTemplate source;
        if (sourceVersion.HasValue)
        {
            source = await templateRepository.GetVersionAsync(templateId, sourceVersion.Value, cancellationToken)
                ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "源版本不存在");
        }
        else
        {
            var versions = await templateRepository.GetByTemplateIdAsync(templateId, cancellationToken);
            source = versions.OrderByDescending(t => t.Version).FirstOrDefault()
                ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "模板无可克隆版本");
        }

        var now = DateTimeOffset.UtcNow;
        var clone = source with
        {
            TemplateId = Guid.NewGuid(),
            Version = 1,
            TemplateName = BuildCloneTemplateName(source.TemplateName),
            Status = TemplateStatus.Draft,
            CreatedBy = operatorId,
            CreatedAt = now,
            UpdatedBy = operatorId,
            UpdatedAt = now,
            PublishedAt = null
        };
        await templateRepository.SaveAsync(clone, cancellationToken);
        return new AlgorithmTemplateDetail(ToView(clone), clone.ReactFlowJson, clone.ConfigJson);
    }

    public async Task<AlgorithmTemplateDeleteImpact> GetDeleteImpactAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var versions = await templateRepository.GetByTemplateIdAsync(templateId, cancellationToken);
        if (versions.Count == 0)
        {
            throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板不存在");
        }

        var taskRunsOfTemplate = await taskRuns.ListByAlgorithmTemplateIdAsync(templateId, cancellationToken);
        var latestVersion = versions.OrderByDescending(v => v.Version).First();
        var runningCount = taskRunsOfTemplate.Count(r => r.Status is TaskRunStatus.Queued or TaskRunStatus.Running);

        return new AlgorithmTemplateDeleteImpact(
            templateId,
            latestVersion.TemplateName,
            versions.Count,
            taskRunsOfTemplate.Count,
            runningCount,
            taskRunsOfTemplate.Select(r => r.RunId).ToArray());
    }

    public async Task DeleteTemplateAsync(
        Guid templateId,
        bool cascade,
        CancellationToken cancellationToken)
    {
        var impact = await GetDeleteImpactAsync(templateId, cancellationToken);
        if (impact.HasReferences && !cascade)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateInvalidState,
                $"模板存在引用：任务 {impact.TaskRunCount} 个（运行中/排队 {impact.RunningTaskRunCount} 个）。请确认级联删除。");
        }

        if (impact.HasReferences)
        {
            var runs = await taskRuns.ListByAlgorithmTemplateIdAsync(templateId, cancellationToken);
            foreach (var run in runs.Where(r => r.Status is TaskRunStatus.Queued or TaskRunStatus.Running))
            {
                try
                {
                    await taskOrchestrator.CancelAsync(run.RunId, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskValidationException ex) when (
                    ex.ErrorCode is TaskErrorCodes.NotCancellable or TaskErrorCodes.NotFound)
                {
                    // 任务状态在并发更新，按最终删除流程兜底。
                }
            }

            foreach (var run in runs)
            {
                await taskRunLifecycleService.DeleteRunForceAsync(run.RunId, cancellationToken).ConfigureAwait(false);
            }
        }

        await templateRepository.DeleteAllByTemplateIdAsync(templateId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        Guid templateId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");

        if (existing.Status != TemplateStatus.Draft)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateInvalidState,
                "仅 Draft 状态版本可删除；已发布版本只能归档");
        }

        await templateRepository.DeleteAsync(templateId, version, cancellationToken);
    }

    /// <summary>测试运行：校验 DAG 后创建 <c>PIPELINE</c> 任务（TRIAL 触发），默认筛选模板见 <see cref="PipelineDevIds"/>。</summary>
    public async Task<AlgorithmTemplateTrialRunResponse> TrialRunAsync(
        Guid templateId,
        int version,
        AlgorithmTemplateTrialRunRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await templateRepository.GetVersionAsync(templateId, version, cancellationToken)
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmTemplateNotFound, "算法模板版本不存在");

        var validation = await validator.ValidateAsync(existing.ReactFlowJson, existing.ConfigJson, cancellationToken);
        if (!validation.Valid)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmTemplateDagInvalid,
                "DAG 校验未通过，无法测试运行");
        }

        var cmd = new PipelineCreateCommand(
            request.TasookNo,
            request.SatelliteNo,
            request.TestBatchId,
            request.WindowStart,
            request.WindowEnd,
            FilterTemplateId: null,
            FilterTemplateVersion: null,
            templateId,
            version,
            IdempotencyKey: null,
            TaskTriggerType.Trial);

        var result = await taskOrchestrator.CreatePipelineAsync(cmd, createdBy: null, cancellationToken);
        return new AlgorithmTemplateTrialRunResponse(
            RunId: result.RunId,
            Status: result.Status.ToString(),
            Message: $"已创建 PIPELINE 测试任务（trigger=TRIAL），目标 {request.TasookNo}/{request.SatelliteNo}，runId={result.RunId}");
    }

    private static AlgorithmTemplateView ToView(AlgorithmTemplate template)
    {
        return new AlgorithmTemplateView(
            template.TemplateId,
            template.Version,
            template.TemplateName,
            template.Status,
            template.NodeCount,
            template.Description,
            template.CreatedAt,
            template.UpdatedAt,
            template.PublishedAt);
    }

    private static string BuildCloneTemplateName(string sourceName)
    {
        var name = sourceName.Trim();
        if (name.Length == 0)
        {
            return "未命名模板 (副本)";
        }

        return name.EndsWith("(副本)", StringComparison.Ordinal) ? $"{name}-{DateTime.UtcNow:HHmmss}" : $"{name} (副本)";
    }
}
