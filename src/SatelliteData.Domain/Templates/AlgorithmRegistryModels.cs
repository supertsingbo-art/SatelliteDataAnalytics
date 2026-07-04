using System.Text.Json;

namespace SatelliteData.Domain.Templates;

/// <summary>
/// 算法运行时类型。详见 6.5.3 DAG 校验规则。
/// </summary>
public enum AlgorithmRuntime
{
    /// <summary>内置 .NET 实现。详见 6.5.2。</summary>
    Builtin = 0,

    /// <summary>用户上传的 Python 算法包。</summary>
    Python = 1,

    /// <summary>用户上传的 JavaScript 算法包。</summary>
    Js = 2
}

/// <summary>
/// 算法分类，对应 React Flow 组件库分类容器（详见 6.2.4.3）。
/// </summary>
public enum AlgorithmCategory
{
    Source = 0,
    Stats = 1,
    Spectrum = 2,
    Align = 3,
    Cluster = 4,
    Compare = 5,
    Output = 6,
    /// <summary>数据输出（与 Source 对称），如结果落库 save_result。</summary>
    DataOutput = 7
}

/// <summary>
/// 算法包状态机。详见 6.5.4 算法仓库与沙箱执行。
/// </summary>
public enum AlgorithmPackageStatus
{
    Draft = 0,
    SandboxValidating = 1,
    Published = 2,
    Rejected = 3,
    Archived = 4
}

/// <summary>
/// 算法包。同 <c>algorithm_code</c> 多版本共存；命名仅允许数学名（小写、数字、下划线，不超过 32 字符）。
/// </summary>
public sealed record AlgorithmPackage(
    Guid PackageId,
    string AlgorithmCode,
    string DisplayName,
    string Version,
    AlgorithmRuntime Runtime,
    AlgorithmCategory Category,
    AlgorithmPackageStatus Status,
    JsonElement InputsSchemaJson,
    JsonElement OutputsSchemaJson,
    JsonElement ParamsSchemaJson,
    JsonElement ResourcesJson,
    string? Description,
    string? LastError,
    Guid? UploadedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    Guid ObjectId,
    string Entrypoint,
    JsonElement ManifestJson);
