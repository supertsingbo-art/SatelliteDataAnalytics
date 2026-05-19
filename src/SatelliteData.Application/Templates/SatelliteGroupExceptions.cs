namespace SatelliteData.Application.Templates;

/// <summary>
/// 模板治理领域错误。错误码与详细设计文档 3.2 错误码规范保持一致。
/// </summary>
public sealed class TemplateGovernanceException(string errorCode, string message)
    : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public static class TemplateErrorCodes
{
    public const string GroupNotFound = "GROUP_001";
    public const string GroupDeleteRefused = "GROUP_002";
    public const string GroupSiblingNameDuplicated = "GROUP_003";
    public const string GroupCircular = "GROUP_004";

    public const string FilterTemplateNotFound = "TPL_001";
    public const string FilterTemplateNotEditable = "TPL_002";
    public const string FilterTemplateInvalidState = "TPL_003";
    public const string FilterTemplateConfigInvalid = "TPL_004";
    public const string FilterTemplateResolveFailed = "TPL_005";
    public const string FilterTemplateNotApplicable = "TPL_006";
    public const string AssetSatelliteDisabled = "ASSET_SATELLITE_DISABLED";

    public const string AlgorithmTemplateNotFound = "ALGO_TPL_001";
    public const string AlgorithmTemplateNotEditable = "ALGO_TPL_002";
    public const string AlgorithmTemplateInvalidState = "ALGO_TPL_003";
    public const string AlgorithmTemplateDagInvalid = "ALGO_TPL_004";

    public const string AlgorithmPackageNotFound = "PKG_001";
    public const string AlgorithmPackageNotPublished = "PKG_002";
    public const string AlgorithmPackageNameRejected = "PKG_003";
    public const string AlgorithmPackageDuplicateVersion = "PKG_004";
    public const string AlgorithmPackageManifestInvalid = "PKG_005";
}
