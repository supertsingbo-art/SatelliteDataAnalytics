namespace SatelliteData.Application.Tasks;

/// <summary>创建预处理入仓时的执行模式（API 入参）。</summary>
public enum PreprocessCreateMode
{
    Immediate,
    OnceScheduled,
    DailyRecurring
}

public static class PreprocessCreateModeParser
{
    public static bool TryParse(string? raw, out PreprocessCreateMode mode)
    {
        mode = PreprocessCreateMode.Immediate;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        switch (raw.Trim().ToUpperInvariant())
        {
            case "IMMEDIATE":
                mode = PreprocessCreateMode.Immediate;
                return true;
            case "ONCE_SCHEDULED":
                mode = PreprocessCreateMode.OnceScheduled;
                return true;
            case "DAILY_RECURRING":
                mode = PreprocessCreateMode.DailyRecurring;
                return true;
            default:
                return false;
        }
    }

    public static string ToApi(PreprocessCreateMode mode) =>
        mode switch
        {
            PreprocessCreateMode.OnceScheduled => "ONCE_SCHEDULED",
            PreprocessCreateMode.DailyRecurring => "DAILY_RECURRING",
            _ => "IMMEDIATE"
        };
}
