using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Templates;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Algorithms;

public sealed class AlgorithmPackageUploadService(
    IAlgorithmPackageRepository packageRepository,
    IObjectStorageService objectStorage,
    ILogger<AlgorithmPackageUploadService> logger)
{
    private static readonly HashSet<string> NameBlacklist =
    [
        "预测", "定制", "去噪", "寿命", "业务", "整星", "综测"
    ];

    public async Task<Guid> UploadZipAsync(Stream zipStream, CancellationToken cancellationToken)
    {
        using var buf = new MemoryStream();
        await zipStream.CopyToAsync(buf, cancellationToken).ConfigureAwait(false);
        var bytes = buf.ToArray();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new TemplateGovernanceException(TemplateErrorCodes.AlgorithmPackageNotFound, "ZIP 中缺少 manifest.json");
        await using var manifestStream = manifestEntry.Open();
        using var doc = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
        var root = doc.RootElement.Clone();
        ValidateManifest(root);

        var code = root.GetProperty("algorithmCode").GetString()!.Trim();
        var name = root.GetProperty("algorithmName").GetString()!.Trim();
        foreach (var bad in NameBlacklist)
        {
            if (name.Contains(bad, StringComparison.Ordinal))
            {
                throw new TemplateGovernanceException(
                    TemplateErrorCodes.AlgorithmPackageNameRejected,
                    $"算法名称命中黑名单关键字：{bad}");
            }
        }

        var version = root.GetProperty("version").GetString()!.Trim();
        var existing = await packageRepository.GetByCodeAndVersionAsync(code, version, cancellationToken);
        if (existing is not null)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmPackageDuplicateVersion,
                "同一 algorithm_code + version 已存在");
        }

        var packageId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var bucket = "algorithm-packages";
        var key = $"{code}/{version}/package.zip";
        await objectStorage.PutAsync(bucket, key, bytes, cancellationToken).ConfigureAwait(false);

        var runtimeStr = root.GetProperty("runtime").GetString()!.ToUpperInvariant();
        var runtime = runtimeStr switch
        {
            "PYTHON" => AlgorithmRuntime.Python,
            "JS" => AlgorithmRuntime.Js,
            _ => AlgorithmRuntime.Builtin
        };

        var categoryStr = root.GetProperty("category").GetString()!.ToLowerInvariant();
        var category = categoryStr switch
        {
            "source" => AlgorithmCategory.Source,
            "stats" => AlgorithmCategory.Stats,
            "spectrum" => AlgorithmCategory.Spectrum,
            "align" => AlgorithmCategory.Align,
            "cluster" => AlgorithmCategory.Cluster,
            "compare" => AlgorithmCategory.Compare,
            "output" => AlgorithmCategory.Output,
            _ => AlgorithmCategory.Stats
        };

        var inputs = root.TryGetProperty("inputs", out var inp) ? inp.Clone() : JsonDocument.Parse("[]").RootElement.Clone();
        var outputs = root.TryGetProperty("outputs", out var outp) ? outp.Clone() : JsonDocument.Parse("[]").RootElement.Clone();
        var @params = root.TryGetProperty("params", out var pm) ? pm.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
        var resources = root.TryGetProperty("resources", out var res)
            ? res.Clone()
            : JsonDocument.Parse("{\"cpu\":1,\"memoryMb\":512,\"timeoutSeconds\":300}").RootElement.Clone();

        var entrypoint = root.TryGetProperty("entrypoint", out var ep) ? ep.GetString() ?? "main.py" : "main.py";
        var now = DateTimeOffset.UtcNow;
        var package = new AlgorithmPackage(
            packageId,
            code,
            name,
            version,
            runtime,
            category,
            AlgorithmPackageStatus.Draft,
            inputs,
            outputs,
            @params,
            resources,
            Description: null,
            LastError: null,
            UploadedBy: null,
            CreatedAt: now,
            UpdatedAt: now,
            PublishedAt: null,
            ObjectId: objectId,
            Entrypoint: entrypoint,
            ManifestJson: root);

        await packageRepository.SaveAsync(package, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Uploaded algorithm package draft {PackageId} {Code} {Version}", packageId, code, version);
        return packageId;
    }

    private static void ValidateManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new TemplateGovernanceException(
                TemplateErrorCodes.AlgorithmPackageManifestInvalid,
                "manifest 必须是 JSON 对象");
        }

        foreach (var prop in new[] { "algorithmCode", "algorithmName", "category", "version", "runtime" })
        {
            if (!root.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(p.GetString()))
            {
                throw new TemplateGovernanceException(
                    TemplateErrorCodes.AlgorithmPackageManifestInvalid,
                    $"manifest 缺少或无效字段：{prop}");
            }
        }
    }
}
