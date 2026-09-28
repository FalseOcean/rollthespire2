using System.Collections.Concurrent;
using Godot;

namespace RolltheSpire2.Bootstrap;

// Default = Production evidence; Detail = execution trace. Godot is discovery only.
internal static class RuntimeLog
{
    private const string ConsolePrefix = "[RolltheSpire2]";
    public static readonly bool DetailEnabled = System.Environment.GetEnvironmentVariable("RT2_REWRITE_DETAIL_LOG") == "1";
    private static readonly AsyncLocal<string?> Correlation = new();
    private static readonly AsyncLocal<bool> Preview = new();
    private static readonly ConcurrentQueue<(bool Error, string Message)> Discovery = new();
    private static int _mainThread, _fileFailureReported;
    private static bool _hostConsole, _pumpAttached;
    public static string SessionId => Correlation.Value ?? "";
    public static string LogDirectory => OperationalFileLog.LogDirectory;
    public static string CurrentLogPath => OperationalFileLog.CurrentLogPath;
    public static string LastFailureCode => OperationalFileLog.LastFailureCode;
    public static void InitializeOnMainThread(bool hostConsole = false)
    {
        _mainThread = System.Environment.CurrentManagedThreadId; _hostConsole = hostConsole;
        BindMainThreadPump();
        OperationalFileLog.Write("INFO", "operationalLogSchema=2;default=ProductionEvidence;detail=ExecutionTrace;godot=Discovery;retentionFiles=10");
        CheckFileFailure(); PumpOnMainThread();
    }
    internal static void BindMainThreadPump()
    {
        if (_pumpAttached || System.Environment.CurrentManagedThreadId != _mainThread) return;
        try { if (Engine.GetMainLoop() is SceneTree tree) { tree.ProcessFrame += PumpOnMainThread; _pumpAttached = true; } } catch { }
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
    public static IDisposable SearchScope(string id)
    { string? previous = Correlation.Value; Correlation.Value = id; return new Restore(() => Correlation.Value = previous); }
    public static IDisposable PlanningScope()
    { bool previous = Preview.Value; Preview.Value = SessionId.Length == 0; return new Restore(() => Preview.Value = previous); }
    public static void Info(string message) => Write("INFO", message);
    public static void Ui(string message) => Detail(message);
    public static void Warn(string message) { Write("WARN", message); GlobalSummary(message, false); }
    public static void Error(string message) { Write("ERROR", message); GlobalSummary(message, true); }
    public static void Detail(string message) { if (DetailEnabled) Write("DETAIL", message); }
    public static bool TryBackgroundDetail(string message) => !DetailEnabled || Write("DETAIL", message);
    public static bool TryBackgroundInfo(string message) => Write("INFO", message);
    public static bool TryBackgroundWarning(string message) => Write("WARN", message);
    public static string SafeJson(object? value)
    { try { return System.Text.Json.JsonSerializer.Serialize(value); } catch(Exception ex) { return "{\"loggingSerializationFailure\":\"" + ex.GetType().Name + "\"}"; } }
    public static void FaultEvidence(string context, string exceptionText, string summary)
    { try { Write("ERROR", context + ";exceptionJson=" + SafeJson(exceptionText)); OperationalFileLog.Flush(); GlobalSummary(summary, true); } catch { } }
    public static void Fault(string context, Exception exception)
    {
        try { FaultEvidence(context, exception.ToString(), (context.StartsWith("searchFault", StringComparison.Ordinal) ? "Search faulted: " : "RT2 fault: ") + exception.GetType().Name + ": " + exception.Message.Split('\n')[0]); } catch { }
    }
    public static void WarnException(string context, Exception exception)
    { try { Write("WARN", context + ";exceptionJson=" + SafeJson(exception.ToString())); GlobalSummary(context + ";error=" + exception.GetType().Name); } catch { } }
    public static void GlobalSummary(string message, bool error = false, bool startup = false)
    {
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        string shortMessage = message.Length > 240 ? message[..240] : message;
        if (startup && System.Environment.CurrentManagedThreadId == _mainThread)
        { try { GD.Print(ConsolePrefix + " " + shortMessage + "; Full diagnostics: " + CurrentLogPath); } catch { } return; }
        Discovery.Enqueue((error, shortMessage));
        if (System.Environment.CurrentManagedThreadId == _mainThread) PumpOnMainThread();
    }
    public static void PumpOnMainThread()
    {
        if (System.Environment.CurrentManagedThreadId != _mainThread) return;
        CheckFileFailure();
        while (Discovery.TryDequeue(out var item))
        {
            string text = ConsolePrefix + " " + item.Message + "; Full diagnostics: " + CurrentLogPath;
            try { if (item.Error) GD.PrintErr("[ERROR] " + text); else GD.PushWarning(text); } catch { }
        }
        OperationalFileLog.FlushIfDue();
    }
    private static void CheckFileFailure()
    {
        if (LastFailureCode.Length > 0 && Interlocked.Exchange(ref _fileFailureReported, 1) == 0)
            Discovery.Enqueue((true, "Operational log unavailable: " + LastFailureCode));
    }
    private static bool Write(string level, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            if (Preview.Value && level == "INFO") { if (!DetailEnabled) return true; level = "DETAIL"; }
            string normalized = message.Trim();
            if (SessionId.Length > 0)
            {
                int jsonAt = normalized.IndexOf(";workloadJson=", StringComparison.Ordinal);
                normalized = jsonAt < 0 ? normalized + ";logSession=" + SessionId : normalized.Insert(jsonAt, ";logSession=" + SessionId);
            }
            if (Preview.Value) normalized += ";logScope=Preview";
            bool written = OperationalFileLog.TryWriteInitialized(level, normalized);
            if (_hostConsole) { if (level is "ERROR" or "WARN") Console.Error.WriteLine($"{ConsolePrefix} [{level}] {normalized}"); else Console.WriteLine($"{ConsolePrefix} [{level}] {normalized}"); }
            CheckFileFailure();
            if (System.Environment.CurrentManagedThreadId == _mainThread && !written) PumpOnMainThread();
            return written;
        }
        catch { return false; }
    }
}
