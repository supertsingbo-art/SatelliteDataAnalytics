using System.Text.Json;

namespace SatelliteData.Infrastructure.PostgreSql;

internal static class PgJson
{
    public static string ToJson(JsonElement element) => JsonSerializer.Serialize(element);

    public static JsonElement FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
