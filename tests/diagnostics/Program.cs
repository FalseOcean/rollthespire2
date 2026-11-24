using System.Text.Json;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Infrastructure.Diagnostics;

string root = Path.Combine(Path.GetTempPath(), "rt2-diagnostic-fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string disabledDir = Path.Combine(root, "disabled");
    await using (var logger = new RuntimePredictionDiagnosticLogger(disabledDir, enabled: false))
    {
        bool accepted = logger.TryWrite(Event(RuntimePredictionDiagnosticLevel.Info));
        await Task.Delay(75);
        Console.WriteLine($"diagnosticDisabledRejectsNormalEvent={!accepted}");
    }
    Console.WriteLine($"diagnosticDisabledCreatesNoFile={!Directory.Exists(disabledDir) || !Directory.EnumerateFiles(disabledDir).Any()}");

    string forceDir = Path.Combine(root, "force");
    string forcedPath;
    await using (var logger = new RuntimePredictionDiagnosticLogger(forceDir, enabled: false))
    {
        bool accepted = logger.TryWrite(Event(RuntimePredictionDiagnosticLevel.Trace), force: true);
        await WaitUntilAsync(() => !string.IsNullOrWhiteSpace(logger.LatestLogPath));
        forcedPath = logger.LatestLogPath;
        Console.WriteLine($"diagnosticForcedTraceAccepted={accepted}");
    }
    string[] lines = File.ReadAllLines(forcedPath);
    using JsonDocument parsed = JsonDocument.Parse(lines.Single());
    Console.WriteLine($"diagnosticJsonLineValid={parsed.RootElement.GetProperty("component").GetString() == "Fixture"}");

    string infoDir = Path.Combine(root, "info");
    await using (var logger = new RuntimePredictionDiagnosticLogger(infoDir, enabled: true, RuntimePredictionDiagnosticLevel.Info))
    {
        bool infoAccepted = logger.TryWrite(Event(RuntimePredictionDiagnosticLevel.Info));
        bool traceAccepted = logger.TryWrite(Event(RuntimePredictionDiagnosticLevel.Trace));
        await WaitUntilAsync(() => !string.IsNullOrWhiteSpace(logger.LatestLogPath));
        Console.WriteLine($"diagnosticInfoAccepted={infoAccepted}");
        Console.WriteLine($"diagnosticTraceFilteredAtInfo={!traceAccepted}");
    }

    string blocker = Path.Combine(root, "not-a-directory");
    File.WriteAllText(blocker, "block");
    await using (var logger = new RuntimePredictionDiagnosticLogger(blocker, enabled: true))
    {
        bool callReturned = logger.TryWrite(Event(RuntimePredictionDiagnosticLevel.Info));
        await WaitUntilAsync(() => !string.IsNullOrWhiteSpace(logger.LastFailureCode));
        Console.WriteLine($"diagnosticWriteFailureDoesNotThrow={callReturned}");
        Console.WriteLine($"diagnosticWriteFailureRecorded={!string.IsNullOrWhiteSpace(logger.LastFailureCode)}");
    }
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static RuntimePredictionDiagnosticEvent Event(RuntimePredictionDiagnosticLevel level) =>
    RuntimePredictionDiagnosticEvent.Create(
        "fixture-request",
        "Beta109",
        "65E17ZMFQVRF",
        level,
        "Fixture",
        "Writer",
        RuntimePredictionDiagnosticStatus.Succeeded,
        details: new Dictionary<string, object?> { ["value"] = 1 });

static async Task WaitUntilAsync(Func<bool> predicate)
{
    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
    while (!predicate() && DateTime.UtcNow < deadline)
    {
        await Task.Delay(25);
    }
    if (!predicate()) throw new TimeoutException("Diagnostic fixture timed out.");
}
