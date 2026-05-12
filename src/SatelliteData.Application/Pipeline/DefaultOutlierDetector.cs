namespace SatelliteData.Application.Pipeline;

public interface IOutlierDetector
{
    /// <summary>对 processed 值序列打标；与 6.4.3 对齐，先实现 THRESHOLD / SIGMA。</summary>
    IReadOnlyList<byte> MarkOutliers(IReadOnlyList<double> values, string method, double? minBound, double? maxBound, double sigmaK);
}

public sealed class DefaultOutlierDetector : IOutlierDetector
{
    public IReadOnlyList<byte> MarkOutliers(
        IReadOnlyList<double> values,
        string method,
        double? minBound,
        double? maxBound,
        double sigmaK)
    {
        var n = values.Count;
        var flags = new byte[n];
        if (n == 0)
        {
            return flags;
        }

        var m = method.ToUpperInvariant();
        if (m is "THRESHOLD" or "THRESH")
        {
            for (var i = 0; i < n; i++)
            {
                var v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    flags[i] = 1;
                    continue;
                }

                if (minBound.HasValue && v < minBound.Value) flags[i] = 1;
                if (maxBound.HasValue && v > maxBound.Value) flags[i] = 1;
            }

            return flags;
        }

        if (m is "SIGMA" or "3SIGMA")
        {
            var mean = values.Where(static x => !double.IsNaN(x) && !double.IsInfinity(x)).DefaultIfEmpty(0).Average();
            var variance = n > 1
                ? values.Where(static x => !double.IsNaN(x) && !double.IsInfinity(x))
                    .Select(x => (x - mean) * (x - mean))
                    .Average()
                : 0d;
            var std = Math.Sqrt(variance);
            if (std < 1e-12)
            {
                return flags;
            }

            for (var i = 0; i < n; i++)
            {
                var v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    flags[i] = 1;
                    continue;
                }

                if (Math.Abs(v - mean) > sigmaK * std)
                {
                    flags[i] = 1;
                }
            }

            return flags;
        }

        // 其它方法一阶段占位：不打标
        return flags;
    }
}
