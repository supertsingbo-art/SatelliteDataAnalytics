namespace SatelliteData.Infrastructure.HttpClients;

public sealed class AssetProviderOptions
{
    public string MassDataApiBaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>卫星测试流程规划服务根地址（调用 <c>/api/testplan/*</c>）。配置键仍为 SatelliteAssetApiBaseUrl 以兼容既有部署。</summary>
    public string SatelliteAssetApiBaseUrl { get; init; } = "http://localhost:5001";

    public string MinioBaseUrl { get; init; } = "http://localhost:9000";

    public string DefaultDbStage { get; init; } = "DEV";
}
