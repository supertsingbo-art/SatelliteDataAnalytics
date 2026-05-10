namespace SatelliteData.Infrastructure.HttpClients;

public sealed class AssetProviderOptions
{
    public string MassDataApiBaseUrl { get; init; } = "http://localhost:5000";

    public string SatelliteAssetApiBaseUrl { get; init; } = "http://localhost:5001";

    public string MinioBaseUrl { get; init; } = "http://localhost:9000";

    public string DefaultDbStage { get; init; } = "DEV";
}
