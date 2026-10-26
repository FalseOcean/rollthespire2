using System.Globalization;
using System.Text;
using System.Text.Json;
using RolltheSpire2.Search.Runtime;

// 直接測正式輔助類別，不載入 Godot 或啟動搜尋。
int checks = 0;
string? original = Environment.GetEnvironmentVariable(SearchBenchmarkRunLimits.ScanCountVariable, EnvironmentVariableTarget.Process);
CultureInfo originalCulture = CultureInfo.CurrentCulture;
string requestPath = Path.Combine(AppContext.BaseDirectory, "benchmark-request-check-" + Guid.NewGuid().ToString("N") + ".txt");
try
{
    // 切換地區設定後，範圍語法仍須一致。
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    foreach (string? raw in new string?[] { null, "" })
    {
        Set(raw);
        var limits = SearchBenchmarkRunLimits.Resolve(30);
        Require(limits == (long.MaxValue, 30, false), "Unset/empty preserves ordinary limits");
    }
    foreach (long value in new long[] { 1, 175842000896, 1L << 40 })
    {
        Set(value.ToString(CultureInfo.InvariantCulture));
        var limits = SearchBenchmarkRunLimits.Resolve(30);
        Require(limits == (value, int.MaxValue, true), "Explicit finite range overrides only run bounds");
    }
    foreach (string raw in new[]
    {
        "-1", "0", "1099511627777", "9223372036854775808", "18446744073709551615",
        " ", "\t", "\r\n", " 1", "1 ", "+1", "1.0", "1,000", "1_000", "1e3", "abc"
    })
    {
        Set(raw);
        bool rejected = false;
        try { _ = SearchBenchmarkRunLimits.Resolve(30); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Invalid process setting fails closed: " + JsonSerializer.Serialize(raw));
    }
    Set(null);
    Require(SearchBenchmarkRunLimits.Resolve(7) == (long.MaxValue, 7, false), "Original target is preserved when disabled");
    Require(SearchBenchmarkRunLimits.Resolve(7, requestPath) == (long.MaxValue, 7, false), "Missing request file preserves ordinary limits");
    File.WriteAllText(requestPath, "175842000896", new UTF8Encoding(false));
    Require(SearchBenchmarkRunLimits.Resolve(30, requestPath) == (175842000896, int.MaxValue, true), "Valid UTF8 request file enables a bounded run");
    Set("");
    Require(SearchBenchmarkRunLimits.Resolve(30, requestPath) == (175842000896, int.MaxValue, true), "Empty process setting permits the request file");
    foreach (string raw in new[] { "0", "invalid", "", "175842000896\n", " 1" })
    {
        Set(null);
        File.WriteAllText(requestPath, raw, new UTF8Encoding(false));
        bool rejected = false;
        try { _ = SearchBenchmarkRunLimits.Resolve(30, requestPath); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Present invalid request file fails closed: " + JsonSerializer.Serialize(raw));
    }
    File.WriteAllText(requestPath, "invalid", new UTF8Encoding(false));
    Set("1");
    Require(SearchBenchmarkRunLimits.Resolve(30, requestPath) == (1, int.MaxValue, true), "Valid process setting takes precedence over an invalid request file");
    File.WriteAllText(requestPath, "175842000896", new UTF8Encoding(false));
    Set("0");
    bool invalidEnvironmentRejected = false;
    try { _ = SearchBenchmarkRunLimits.Resolve(30, requestPath); }
    catch (InvalidOperationException) { invalidEnvironmentRejected = true; }
    Require(invalidEnvironmentRejected, "Invalid process setting cannot fall back to a valid request file");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Schema = "rt2-benchmark-run-limit-checks-v1",
        ChecksPassed = checks,
        Scope = "Actual process-environment and UTF8 request-file helper; no Godot, search, GPU or deployment.",
        MaximumScanCount = SearchBenchmarkRunLimits.MaximumScanCount,
    }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    try { File.Delete(requestPath); }
    finally
    {
        Set(original);
        CultureInfo.CurrentCulture = originalCulture;
    }
}

void Set(string? value) => Environment.SetEnvironmentVariable(SearchBenchmarkRunLimits.ScanCountVariable, value, EnvironmentVariableTarget.Process);
void Require(bool success, string description)
{
    if (!success) throw new InvalidOperationException("FAILED: " + description);
    checks++;
}
