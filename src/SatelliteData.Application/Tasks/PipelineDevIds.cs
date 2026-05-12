namespace SatelliteData.Application.Tasks;

/// <summary>默认 PIPELINE 开发用筛选/算法模板 ID（由 DevPipelineTemplateSeeder 写入）。</summary>
public static class PipelineDevIds
{
    public static readonly Guid DefaultFilterTemplateId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    public static readonly Guid DefaultAlgorithmTemplateId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
}
