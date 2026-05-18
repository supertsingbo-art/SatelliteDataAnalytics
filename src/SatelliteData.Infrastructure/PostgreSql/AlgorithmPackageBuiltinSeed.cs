using System.Text.Json;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

/// <summary>6.5.4.5 预置 BUILTIN 算法包（内存与 PostgreSQL 共用）。</summary>
internal static class AlgorithmPackageBuiltinSeed
{
    public static IReadOnlyList<AlgorithmPackage> CreatePackages()
    {
        var now = DateTimeOffset.UtcNow;
        var seeds = new (string Code, string DisplayName, AlgorithmCategory Cat, string ParamsSchema, string Description)[]
        {
            ("max", "最大值", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{}}", "单遍扫描求最大 processed_value（O(n)）"),
            ("min", "最小值", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{}}", "单遍扫描求最小 processed_value（O(n)）"),
            ("mean", "平均值", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{}}", "单遍累加除以个数（O(n)）"),
            ("variance", "方差", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{\"ddof\":{\"type\":\"integer\",\"default\":1}}}", "Welford 在线方差（O(n)）"),
            ("stddev", "标准差", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{\"ddof\":{\"type\":\"integer\",\"default\":1}}}", "sqrt(variance)"),
            ("envelope", "包络线", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{\"windowSeconds\":{\"type\":\"integer\"},\"mode\":{\"enum\":[\"minmax\",\"hilbert\"],\"default\":\"minmax\"}}}", "滑动窗口 max/min 包络（O(n·w)）"),
            ("rms", "均方根值", AlgorithmCategory.Stats, "{\"type\":\"object\",\"properties\":{}}", "sqrt(mean(x²))（O(n)）"),
            ("fft", "快速傅里叶变换", AlgorithmCategory.Spectrum, "{\"type\":\"object\",\"properties\":{\"sampleRate\":{\"type\":\"integer\"},\"window\":{\"enum\":[\"hann\",\"hamming\",\"rect\"],\"default\":\"hann\"}}}", "MathNet.Numerics 离散 FT；要求等间距采样（O(n log n)）"),
            ("psd", "功率谱密度", AlgorithmCategory.Spectrum, "{\"type\":\"object\",\"properties\":{\"nperseg\":{\"type\":\"integer\"},\"overlap\":{\"type\":\"number\",\"default\":0.5}}}", "Welch 法（O(n log n)）"),
            ("dominant_freq", "主频提取", AlgorithmCategory.Spectrum, "{\"type\":\"object\",\"properties\":{\"topK\":{\"type\":\"integer\",\"default\":1}}}", "输入 Spectrum，取幅值最大的频率（O(m)）"),
            ("threshold_judge", "阈值判定", AlgorithmCategory.Output, "{\"type\":\"object\",\"properties\":{\"min\":{\"type\":\"number\"},\"max\":{\"type\":\"number\"}}}", "对每个值应用 [min, max] 判定，写 algo_result.detail_json（O(n)）"),
            ("three_sigma_judge", "3σ 判定", AlgorithmCategory.Output, "{\"type\":\"object\",\"properties\":{\"k\":{\"type\":\"number\",\"default\":3.0}}}", "计算窗口内 mean/std，超过 k·σ 标记异常（O(n)）"),
        };

        var inputsSchema = JsonDocument.Parse("{\"series\":\"TimeSeries\"}").RootElement.Clone();
        var outputsSchema = JsonDocument.Parse("{\"value\":\"Scalar\"}").RootElement.Clone();
        var resources = JsonDocument.Parse("{\"cpu\":1,\"memory\":\"1024MB\",\"timeoutSeconds\":600}").RootElement.Clone();
        var emptyManifest = JsonDocument.Parse("{}").RootElement.Clone();

        var list = new List<AlgorithmPackage>(seeds.Length);
        foreach (var seed in seeds)
        {
            var paramsSchema = JsonDocument.Parse(seed.ParamsSchema).RootElement.Clone();
            var packageId = Guid.NewGuid();
            list.Add(new AlgorithmPackage(
                PackageId: packageId,
                AlgorithmCode: seed.Code,
                DisplayName: seed.DisplayName,
                Version: "1.0.0",
                Runtime: AlgorithmRuntime.Builtin,
                Category: seed.Cat,
                Status: AlgorithmPackageStatus.Published,
                InputsSchemaJson: inputsSchema.Clone(),
                OutputsSchemaJson: outputsSchema.Clone(),
                ParamsSchemaJson: paramsSchema,
                ResourcesJson: resources.Clone(),
                Description: seed.Description,
                LastError: null,
                UploadedBy: null,
                CreatedAt: now,
                UpdatedAt: now,
                PublishedAt: now,
                ObjectId: packageId,
                Entrypoint: "__builtin__",
                ManifestJson: emptyManifest.Clone()));
        }

        return list;
    }
}
