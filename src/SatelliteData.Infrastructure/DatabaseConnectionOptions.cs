namespace SatelliteData.Infrastructure;

public sealed class DatabaseConnectionOptions
{
    public const string SectionName = "ConnectionStrings";

    public string ClickHouse { get; init; } =
        "Host=localhost;Port=8123;Username=default;Password=1234;";

    public string Postgres { get; init; } =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=demo_db";
}
