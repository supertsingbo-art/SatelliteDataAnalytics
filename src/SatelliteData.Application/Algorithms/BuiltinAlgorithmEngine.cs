using System.Numerics;
using System.Text.Json;
using MathNet.Numerics.IntegralTransforms;

namespace SatelliteData.Application.Algorithms;

public sealed class SpectrumData
{
    public SpectrumData(double[] frequencies, double[] magnitudes)
    {
        Frequencies = frequencies;
        Magnitudes = magnitudes;
    }

    public double[] Frequencies { get; }
    public double[] Magnitudes { get; }
}

public sealed class NodeOutput
{
    public List<(DateTimeOffset Ts, double V)>? Series { get; init; }
    public double? Scalar { get; init; }
    public SpectrumData? Spectrum { get; init; }
}

/// <summary>6.5.2.2 一阶段 BUILTIN 数学实现（与预处理离群打标语义分离）。</summary>
public static class BuiltinAlgorithmEngine
{
    public static NodeOutput? Execute(string algorithmCode, NodeOutput? input, JsonElement nodeData)
    {
        var code = algorithmCode.Trim().ToLowerInvariant();
        var series = input?.Series;
        var values = series?.Select(x => x.V).ToList() ?? new List<double>();

        return code switch
        {
            "max" => ScalarOut(BuiltinStats.Max(values)),
            "min" => ScalarOut(BuiltinStats.Min(values)),
            "mean" => ScalarOut(BuiltinStats.Mean(values)),
            "variance" => ScalarOut(BuiltinStats.Variance(values, ReadDdof(nodeData))),
            "stddev" => ScalarOut(BuiltinStats.StdDev(values, ReadDdof(nodeData))),
            "rms" => ScalarOut(BuiltinStats.Rms(values)),
            "envelope" => SeriesOut(BuiltinStats.Envelope(series, ReadInt(nodeData, "windowSeconds", 5), ReadStringEnum(nodeData, "mode", "minmax"))),
            "fft" => SpectrumOut(BuiltinSpectrum.Fft(series, ReadInt(nodeData, "sampleRate", 1), ReadStringEnum(nodeData, "window", "hann"))),
            "psd" => SpectrumOut(BuiltinSpectrum.Psd(series, ReadInt(nodeData, "nperseg", 256), ReadDouble(nodeData, "overlap", 0.5))),
            "dominant_freq" => ScalarOut(BuiltinSpectrum.DominantFreq(input?.Spectrum, ReadInt(nodeData, "topK", 1))),
            "threshold_judge" => SeriesOut(BuiltinOutput.ThresholdJudge(series, ReadDoubleOrNull(nodeData, "min"), ReadDoubleOrNull(nodeData, "max"))),
            "three_sigma_judge" => SeriesOut(BuiltinOutput.ThreeSigmaJudge(series, ReadDouble(nodeData, "k", 3))),
            _ => null
        };
    }

    private static NodeOutput ScalarOut(double? v) =>
        v.HasValue ? new NodeOutput { Scalar = v.Value } : new NodeOutput { Scalar = null };

    private static NodeOutput SeriesOut(List<(DateTimeOffset Ts, double V)>? s) =>
        s is null ? new NodeOutput() : new NodeOutput { Series = s };

    private static NodeOutput SpectrumOut(SpectrumData? sp) =>
        sp is null ? new NodeOutput() : new NodeOutput { Spectrum = sp };

    private static int ReadDdof(JsonElement nodeData) => ReadInt(nodeData, "ddof", 1);

    private static int ReadInt(JsonElement nodeData, string name, int dft)
    {
        if (!TryParams(nodeData, out var p)) return dft;
        return p.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var v) ? v : dft;
    }

    private static double ReadDouble(JsonElement nodeData, string name, double dft)
    {
        if (!TryParams(nodeData, out var p)) return dft;
        return p.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out var v) ? v : dft;
    }

    private static double? ReadDoubleOrNull(JsonElement nodeData, string name)
    {
        if (!TryParams(nodeData, out var p)) return null;
        if (!p.TryGetProperty(name, out var x) || x.ValueKind == JsonValueKind.Null) return null;
        return x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out var v) ? v : null;
    }

    private static string ReadStringEnum(JsonElement nodeData, string name, string dft)
    {
        if (!TryParams(nodeData, out var p)) return dft;
        return p.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? (x.GetString() ?? dft) : dft;
    }

    private static bool TryParams(JsonElement nodeData, out JsonElement p)
    {
        p = default;
        if (nodeData.ValueKind != JsonValueKind.Object) return false;
        return nodeData.TryGetProperty("params", out p) && p.ValueKind == JsonValueKind.Object;
    }
}

internal static class BuiltinStats
{
    public static double? Max(IReadOnlyList<double> v) => v.Count == 0 ? null : v.Max();

    public static double? Min(IReadOnlyList<double> v) => v.Count == 0 ? null : v.Min();

    public static double? Mean(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return null;
        return v.Average();
    }

    public static double? Variance(IReadOnlyList<double> v, int ddof)
    {
        if (v.Count <= ddof) return null;
        var mean = v.Average();
        var s = v.Sum(x => (x - mean) * (x - mean));
        return s / (v.Count - ddof);
    }

    public static double? StdDev(IReadOnlyList<double> v, int ddof)
    {
        var var = Variance(v, ddof);
        return var.HasValue ? Math.Sqrt(var.Value) : null;
    }

    public static double? Rms(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return null;
        return Math.Sqrt(v.Sum(x => x * x) / v.Count);
    }

    public static List<(DateTimeOffset Ts, double V)>? Envelope(
        List<(DateTimeOffset Ts, double V)>? series,
        int windowSeconds,
        string mode)
    {
        if (series is null || series.Count == 0) return series;
        _ = mode;
        var half = TimeSpan.FromSeconds(Math.Max(1, windowSeconds) / 2d);
        var list = new List<(DateTimeOffset, double)>();
        foreach (var (ts, val) in series)
        {
            var win = series.Where(p => p.Ts >= ts - half && p.Ts <= ts + half).Select(p => p.V).ToArray();
            list.Add((ts, win.Length == 0 ? val : (win.Max() + win.Min()) / 2d));
        }

        return list;
    }
}

internal static class BuiltinSpectrum
{
    public static SpectrumData? Fft(List<(DateTimeOffset Ts, double V)>? series, int sampleRate, string window)
    {
        _ = window;
        if (series is null || series.Count < 2) return null;
        var dt = (series[1].Ts - series[0].Ts).TotalSeconds;
        if (dt <= 0 || double.IsNaN(dt)) dt = 1d / Math.Max(1, sampleRate);

        var y = series.Select(s => new Complex(s.V, 0)).ToArray();
        var n = NextPow2(y.Length);
        Array.Resize(ref y, n);
        Fourier.Forward(y, FourierOptions.Matlab);

        var freqs = new double[n / 2];
        var mags = new double[n / 2];
        for (var i = 0; i < n / 2; i++)
        {
            freqs[i] = i / (dt * n);
            mags[i] = y[i].Magnitude;
        }

        return new SpectrumData(freqs, mags);
    }

    public static SpectrumData? Psd(List<(DateTimeOffset Ts, double V)>? series, int nperseg, double overlap)
    {
        var fft = Fft(series, 1, "hann");
        if (fft is null) return null;
        var scale = 1d / Math.Max(1, nperseg * (1d - overlap));
        var mags = fft.Magnitudes.Select(m => m * m * scale).ToArray();
        return new SpectrumData(fft.Frequencies, mags);
    }

    public static double? DominantFreq(SpectrumData? sp, int topK)
    {
        _ = topK;
        if (sp is null || sp.Magnitudes.Length == 0) return null;
        var idx = 0;
        var best = 0d;
        for (var i = 0; i < sp.Magnitudes.Length; i++)
        {
            if (sp.Magnitudes[i] > best)
            {
                best = sp.Magnitudes[i];
                idx = i;
            }
        }

        return best <= 0 ? null : sp.Frequencies[idx];
    }

    private static int NextPow2(int x)
    {
        var p = 1;
        while (p < x) p <<= 1;
        return p;
    }
}

internal static class BuiltinOutput
{
    public static List<(DateTimeOffset Ts, double V)>? ThresholdJudge(
        List<(DateTimeOffset Ts, double V)>? series,
        double? min,
        double? max)
    {
        if (series is null) return null;
        var list = new List<(DateTimeOffset Ts, double V)>();
        foreach (var (ts, v) in series)
        {
            byte ok = 1;
            if (min.HasValue && v < min.Value) ok = 0;
            if (max.HasValue && v > max.Value) ok = 0;
            list.Add((ts, ok));
        }

        return list;
    }

    public static List<(DateTimeOffset Ts, double V)>? ThreeSigmaJudge(List<(DateTimeOffset Ts, double V)>? series, double k)
    {
        if (series is null || series.Count < 2) return series;
        var vals = series.Select(s => s.V).ToList();
        var mean = vals.Average();
        var std = Math.Sqrt(vals.Sum(x => (x - mean) * (x - mean)) / (vals.Count - 1));
        if (std < 1e-12) return series.Select(s => (s.Ts, 1d)).ToList();
        var list = new List<(DateTimeOffset Ts, double V)>();
        foreach (var (ts, v) in series)
        {
            var ok = Math.Abs(v - mean) <= k * std ? 1d : 0d;
            list.Add((ts, ok));
        }

        return list;
    }
}
