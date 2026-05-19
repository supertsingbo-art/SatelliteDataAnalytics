using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Pipeline;

/// <summary>
/// 根据任务入参或有效时间窗，从 <c>test_batch_cache</c> 解析 Mongo / 元数据使用的 <c>test_batch_id</c>。
/// </summary>
public static class TestBatchWindowResolver
{
    public static string ResolveBatchId(
        string? testBatchId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<TestBatchCache> testBatches)
    {
        if (!string.IsNullOrWhiteSpace(testBatchId))
        {
            return testBatchId.Trim();
        }

        var matched = TryMatchByWindow(windowStart, windowEnd, testBatches);
        if (matched is not null)
        {
            return matched.TestBatchId;
        }

        if (testBatches.Count > 0)
        {
            return testBatches
                .OrderByDescending(b => b.StartTs)
                .First()
                .TestBatchId;
        }

        return "default";
    }

    public static TestBatchCache? TryMatchByWindow(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<TestBatchCache> testBatches)
    {
        TestBatchCache? best = null;
        var bestScore = double.MinValue;

        foreach (var batch in testBatches)
        {
            var overlapStart = windowStart > batch.StartTs ? windowStart : batch.StartTs;
            var overlapEnd = windowEnd < batch.EndTs ? windowEnd : batch.EndTs;
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var overlapSeconds = (overlapEnd - overlapStart).TotalSeconds;
            var batchSeconds = Math.Max((batch.EndTs - batch.StartTs).TotalSeconds, 1);
            var score = overlapSeconds / batchSeconds;
            if (score > bestScore)
            {
                bestScore = score;
                best = batch;
            }
        }

        return best;
    }
}
