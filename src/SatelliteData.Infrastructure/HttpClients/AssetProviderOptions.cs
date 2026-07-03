namespace SatelliteData.Infrastructure.HttpClients;

public sealed class AssetProviderOptions
{
    public string MassDataApiBaseUrl { get; init; } = "http://localhost:5000";

    /// <summary>卫星测试流程规划服务根地址（调用 <c>/api/testplan/*</c>）。配置键仍为 SatelliteAssetApiBaseUrl 以兼容既有部署。</summary>
    public string SatelliteAssetApiBaseUrl { get; init; } = "http://localhost:5001";

    public string MinioBaseUrl { get; init; } = "http://localhost:9000";

    /// <summary>数据源配置种子写入 <c>data_source_config.env</c> 的默认值。</summary>
    public string DefaultEnv { get; init; } = "DEV";
}
