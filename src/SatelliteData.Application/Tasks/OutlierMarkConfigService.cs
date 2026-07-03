using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Tasks;

public sealed class OutlierMarkConfigService(IOutlierMarkConfigRepository repository)
{
    public async Task<IReadOnlyList<OutlierMarkOption>> ListAsync(CancellationToken cancellationToken)
    {
        var options = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return NormalizeOrDefault(options);
    }

    public async Task<IReadOnlyList<OutlierMarkOption>> SaveAsync(
        IReadOnlyList<OutlierMarkOption> options,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(options);
        Validate(normalized);
        await repository.ReplaceAllAsync(normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    public async Task<IReadOnlyList<OutlierMarkOption>> GetEnabledOptionsAsync(CancellationToken cancellationToken)
    {
        var options = await ListAsync(cancellationToken).ConfigureAwait(false);
        return options.Where(x => x.Enabled).OrderBy(x => x.SortOrder).ToArray();
    }

    public async Task<IReadOnlyDictionary<string, OutlierMarkOption>> GetEnabledMapAsync(CancellationToken cancellationToken)
    {
        var options = await GetEnabledOptionsAsync(cancellationToken).ConfigureAwait(false);
        return options.ToDictionary(x => x.MarkCode, x => x, StringComparer.Ordinal);
    }

    private static IReadOnlyList<OutlierMarkOption> NormalizeOrDefault(IReadOnlyList<OutlierMarkOption> options)
    {
        if (options.Count == 0)
        {
            return DefaultOptions();
        }

        var normalized = Normalize(options);
        if (!TryValidate(normalized, out _))
        {
            return DefaultOptions();
        }

        return normalized;
    }

    private static IReadOnlyList<OutlierMarkOption> Normalize(IReadOnlyList<OutlierMarkOption> options)
    {
        var rows = new List<OutlierMarkOption>(options.Count);
        foreach (var option in options)
        {
            var code = (option.MarkCode ?? string.Empty).Trim().ToUpperInvariant();
            var label = (option.MarkLabel ?? string.Empty).Trim();
            rows.Add(option with
            {
                MarkCode = code,
                MarkLabel = label,
                SortOrder = Math.Max(0, option.SortOrder)
            });
        }

        return rows
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.MarkCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Validate(IReadOnlyList<OutlierMarkOption> options)
    {
        if (!TryValidate(options, out var message))
        {
            throw new InvalidOperationException(message ?? "离群标记配置不合法");
        }
    }

    private static bool TryValidate(IReadOnlyList<OutlierMarkOption> options, out string? message)
    {
        if (options.Count == 0)
        {
            message = "离群标记配置不能为空";
            return false;
        }

        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.MarkCode))
            {
                message = "标记编码不能为空";
                return false;
            }

            if (string.IsNullOrWhiteSpace(option.MarkLabel))
            {
                message = $"标记 {option.MarkCode} 的名称不能为空";
                return false;
            }

            if (!seenCodes.Add(option.MarkCode))
            {
                message = $"标记编码重复：{option.MarkCode}";
                return false;
            }
        }

        var enabled = options.Where(x => x.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            message = "至少需要启用一个离群标记项";
            return false;
        }

        var enabledOutlier = enabled.Count(x => x.IsOutlier);
        if (enabledOutlier != 1)
        {
            message = "启用项中必须且仅能有一个离群项";
            return false;
        }

        var enabledNonOutlier = enabled.Count(x => !x.IsOutlier);
        if (enabledNonOutlier < 1)
        {
            message = "启用项中至少需要一个非离群项";
            return false;
        }

        message = null;
        return true;
    }

    public static IReadOnlyList<OutlierMarkOption> DefaultOptions() =>
    [
        new OutlierMarkOption(
            MarkCode: OutlierReviewPointStatus.Confirmed,
            MarkLabel: "确认离群",
            IsOutlier: true,
            SortOrder: 0,
            Enabled: true),
        new OutlierMarkOption(
            MarkCode: OutlierReviewPointStatus.Jitter,
            MarkLabel: "标为抖动",
            IsOutlier: false,
            SortOrder: 1,
            Enabled: true)
    ];
}
