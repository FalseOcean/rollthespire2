using System.Globalization;
using System.Text;

namespace RolltheSpire2.Search.Runtime;

// [PROFILE] 僅明確啟用時套用固定測試範圍，平常保留原搜尋限制。
internal static class SearchBenchmarkRunLimits
{
    internal const string ScanCountVariable = "RT2_BENCHMARK_SCAN_COUNT";
    internal const long MaximumScanCount = 1L << 40;

    internal static (long ScanCount, int TargetMatchCount, bool IsActive) Resolve(int targetMatchCount, string? benchmarkRequestPath = null)
    {
        string? raw = Environment.GetEnvironmentVariable(ScanCountVariable, EnvironmentVariableTarget.Process);
        if (string.IsNullOrEmpty(raw))
        {
            if (benchmarkRequestPath is null || !File.Exists(benchmarkRequestPath))
                return (long.MaxValue, targetMatchCount, false);
            // 檔案存在就以檔案為準；讀取或解析失敗不能退回無限搜尋。
            raw = File.ReadAllText(benchmarkRequestPath, Encoding.UTF8);
        }
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long count) ||
            count is < 1 or > MaximumScanCount)
            throw new InvalidOperationException(ScanCountVariable + " must be an invariant positive integer from 1 through 1099511627776.");
        return (count, int.MaxValue, true);
    }
}
