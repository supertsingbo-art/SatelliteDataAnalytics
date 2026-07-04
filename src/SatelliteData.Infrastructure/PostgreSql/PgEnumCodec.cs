using SatelliteData.Domain.Templates;

namespace SatelliteData.Infrastructure.PostgreSql;

internal static class PgEnumCodec
{
    public static string ToDb(TemplateStatus status) => status.ToString();

    public static TemplateStatus ParseTemplateStatus(string value) =>
        Enum.Parse<TemplateStatus>(value, ignoreCase: true);

    public static string ToDb(AlgorithmRuntime runtime) => runtime switch
    {
        AlgorithmRuntime.Builtin => "BUILTIN",
        AlgorithmRuntime.Python => "PYTHON",
        AlgorithmRuntime.Js => "JS",
        _ => throw new ArgumentOutOfRangeException(nameof(runtime))
    };

    public static AlgorithmRuntime ParseRuntime(string value) => value.ToUpperInvariant() switch
    {
        "BUILTIN" => AlgorithmRuntime.Builtin,
        "PYTHON" => AlgorithmRuntime.Python,
        "JS" => AlgorithmRuntime.Js,
        _ => Enum.Parse<AlgorithmRuntime>(value, ignoreCase: true)
    };

    public static string ToDb(AlgorithmCategory category) => category switch
    {
        AlgorithmCategory.Source => "source",
        AlgorithmCategory.Stats => "stats",
        AlgorithmCategory.Spectrum => "spectrum",
        AlgorithmCategory.Align => "align",
        AlgorithmCategory.Cluster => "cluster",
        AlgorithmCategory.Compare => "compare",
        AlgorithmCategory.Output => "output",
        AlgorithmCategory.DataOutput => "dataoutput",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    public static AlgorithmCategory ParseCategory(string value) => value.ToLowerInvariant() switch
    {
        "source" => AlgorithmCategory.Source,
        "stats" => AlgorithmCategory.Stats,
        "spectrum" => AlgorithmCategory.Spectrum,
        "align" => AlgorithmCategory.Align,
        "cluster" => AlgorithmCategory.Cluster,
        "compare" => AlgorithmCategory.Compare,
        "output" => AlgorithmCategory.Output,
        "dataoutput" => AlgorithmCategory.DataOutput,
        _ => Enum.Parse<AlgorithmCategory>(value, ignoreCase: true)
    };

    public static string ToDb(AlgorithmPackageStatus status) => status.ToString();

    public static AlgorithmPackageStatus ParsePackageStatus(string value) =>
        Enum.Parse<AlgorithmPackageStatus>(value, ignoreCase: true);
}
