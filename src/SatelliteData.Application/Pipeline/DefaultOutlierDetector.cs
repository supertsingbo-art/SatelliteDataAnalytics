namespace SatelliteData.Application.Pipeline;

public interface IOutlierDetector
{
    /// <summary>对 processed 值序列打标；配置来自筛选模板 <c>targetParams[].outlier</c>。</summary>
    IReadOnlyList<byte> MarkOutliers(IReadOnlyList<double> values, OutlierDetectionOptions options);
}

public sealed class DefaultOutlierDetector : IOutlierDetector
{
    private const double MadScale = 0.6745;
    private const double HampelMadScale = 1.4826;
    private const double DefaultIqrMultiplier = 1.5;

    public IReadOnlyList<byte> MarkOutliers(IReadOnlyList<double> values, OutlierDetectionOptions options)
    {
        var n = values.Count;
        var flags = new byte[n];
        if (n == 0)
        {
            return flags;
        }

        return options.Method switch
        {
            "THRESHOLD" or "THRESH" => MarkThreshold(values, flags, options.Min, options.Max),
            "SIGMA" or "3SIGMA" => MarkSigmaGlobal(values, flags, options.SigmaK),
            "IQR" => MarkIqrGlobal(values, flags, DefaultIqrMultiplier),
            "MAD" => MarkMadGlobal(values, flags, options.SigmaK),
            "HAMPEL" => MarkHampel(values, flags, options.WindowSize, options.SigmaK),
            _ => flags
        };
    }

    private static IReadOnlyList<byte> MarkThreshold(
        IReadOnlyList<double> values,
        byte[] flags,
        double? minBound,
        double? maxBound)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (IsInvalid(values[i]))
            {
                flags[i] = 1;
                continue;
            }

            var v = values[i];
            if (minBound.HasValue && v < minBound.Value)
            {
                flags[i] = 1;
            }

            if (maxBound.HasValue && v > maxBound.Value)
            {
                flags[i] = 1;
            }
        }

        return flags;
    }

    private static IReadOnlyList<byte> MarkSigmaGlobal(IReadOnlyList<double> values, byte[] flags, double sigmaK)
    {
        var valid = ValidValues(values);
        if (valid.Count == 0)
        {
            return flags;
        }

        var mean = valid.Average();
        var std = StdDev(valid, mean);
        if (std < 1e-12)
        {
            return flags;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (IsInvalid(values[i]))
            {
                flags[i] = 1;
                continue;
            }

            if (Math.Abs(values[i] - mean) > sigmaK * std)
            {
                flags[i] = 1;
            }
        }

        return flags;
    }

    private static IReadOnlyList<byte> MarkIqrGlobal(IReadOnlyList<double> values, byte[] flags, double multiplier)
    {
        var valid = ValidValues(values);
        if (valid.Count < 4)
        {
            return flags;
        }

        valid.Sort();
        var q1 = Percentile(valid, 0.25);
        var q3 = Percentile(valid, 0.75);
        var iqr = q3 - q1;
        if (iqr < 1e-12)
        {
            return flags;
        }

        var lower = q1 - multiplier * iqr;
        var upper = q3 + multiplier * iqr;

        for (var i = 0; i < values.Count; i++)
        {
            if (IsInvalid(values[i]))
            {
                flags[i] = 1;
                continue;
            }

            var v = values[i];
            if (v < lower || v > upper)
            {
                flags[i] = 1;
            }
        }

        return flags;
    }

    private static IReadOnlyList<byte> MarkMadGlobal(IReadOnlyList<double> values, byte[] flags, double threshold)
    {
        var valid = ValidValues(values);
        if (valid.Count == 0)
        {
            return flags;
        }

        var median = Median(valid);
        var deviations = valid.Select(v => Math.Abs(v - median)).ToList();
        var mad = Median(deviations);
        if (mad < 1e-12)
        {
            return flags;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (IsInvalid(values[i]))
            {
                flags[i] = 1;
                continue;
            }

            var score = Math.Abs(MadScale * (values[i] - median) / mad);
            if (score > threshold)
            {
                flags[i] = 1;
            }
        }

        return flags;
    }

    private static IReadOnlyList<byte> MarkHampel(
        IReadOnlyList<double> values,
        byte[] flags,
        int windowSize,
        double threshold)
    {
        var half = Math.Max(1, windowSize / 2);
        for (var i = 0; i < values.Count; i++)
        {
            if (IsInvalid(values[i]))
            {
                flags[i] = 1;
                continue;
            }

            var start = Math.Max(0, i - half);
            var end = Math.Min(values.Count - 1, i + half);
            var window = new List<double>();
            for (var j = start; j <= end; j++)
            {
                if (!IsInvalid(values[j]))
                {
                    window.Add(values[j]);
                }
            }

            if (window.Count < 3)
            {
                continue;
            }

            var med = Median(window);
            var absDev = window.Select(v => Math.Abs(v - med)).ToList();
            var mad = Median(absDev);
            if (mad < 1e-12)
            {
                continue;
            }

            if (Math.Abs(values[i] - med) > threshold * HampelMadScale * mad)
            {
                flags[i] = 1;
            }
        }

        return flags;
    }

    private static List<double> ValidValues(IReadOnlyList<double> values) =>
        values.Where(static v => !IsInvalid(v)).ToList();

    private static bool IsInvalid(double v) => double.IsNaN(v) || double.IsInfinity(v);

    private static double StdDev(IReadOnlyList<double> values, double mean)
    {
        if (values.Count <= 1)
        {
            return 0d;
        }

        return Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
    }

    private static double Median(List<double> sortedOrUnsorted)
    {
        var list = sortedOrUnsorted.OrderBy(static x => x).ToList();
        var n = list.Count;
        if (n == 0)
        {
            return 0d;
        }

        var mid = n / 2;
        return n % 2 == 1
            ? list[mid]
            : (list[mid - 1] + list[mid]) / 2d;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi)
        {
            return sorted[lo];
        }

        var weight = rank - lo;
        return sorted[lo] * (1 - weight) + sorted[hi] * weight;
    }
}
