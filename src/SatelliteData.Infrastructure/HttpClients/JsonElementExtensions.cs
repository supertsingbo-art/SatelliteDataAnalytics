using System.Text.Json;

namespace SatelliteData.Infrastructure.HttpClients;

internal static class JsonElementExtensions
{
    public static string? GetStringOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetRawText();
                }
            }
        }

        return null;
    }

    public static int? GetIntOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static double? GetDoubleOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static bool? GetBoolOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var number))
            {
                return number != 0;
            }

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static IReadOnlyCollection<JsonElement> GetArrayItems(this JsonElement root, params string[] possibleArrayNames)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(Clone).ToArray();
        }

        foreach (var name in possibleArrayNames)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out var array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                return array.EnumerateArray().Select(Clone).ToArray();
            }
        }

        return Array.Empty<JsonElement>();
    }

    public static JsonElement Clone(this JsonElement element)
    {
        return JsonDocument.Parse(element.GetRawText()).RootElement.Clone();
    }
}
