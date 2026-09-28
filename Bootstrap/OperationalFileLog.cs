using System.Globalization;
using Godot;

namespace RolltheSpire2.Bootstrap;

/// <summary>
/// Human-readable operational log for normal troubleshooting.
/// This is intentionally separate from the opt-in JSONL prediction diagnostic trace.
/// Default records Production evidence; opt-in Detail adds execution trace to the same stream.
/// Logging is best-effort and must never affect game or prediction behavior.
/// </summary>
internal static class OperationalFileLog
{
    private const int MaxRetainedLogs = 10;
    private static readonly object Sync = new();
    private static bool _initialized;
    private static StreamWriter? _writer;
    private static long _lastFlush;
    static OperationalFileLog() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Close();
    public static void Flush()
    {
        lock (Sync) { try { _writer?.Flush(); _lastFlush = System.Environment.TickCount64; }
            catch (Exception ex) { _lastFailureCode = "OperationalLogFlushFailed:" + ex.GetType().Name; } }
    }
    public static void FlushIfDue()
    { lock (Sync) { if (System.Environment.TickCount64 - _lastFlush >= 1000) Flush(); } }
    private static void Close()
    { lock (Sync) { try { _writer?.Dispose(); } catch { } _writer = null; } }

    private static string _currentLogPath = string.Empty;
    private static string _lastFailureCode = string.Empty;

    public static string LogDirectory
    {
        get
        {
            try
            {
                return Path.Combine(OS.GetUserDataDir(), "RolltheSpire2", "logs");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "RolltheSpire2", "logs");
            }
        }
    }

    public static string CurrentLogPath
    {
        get
        {
            lock (Sync)
            {
                return _currentLogPath;
            }
        }
    }

    public static string LastFailureCode
    {
        get
        {
            lock (Sync)
            {
                return _lastFailureCode;
            }
        }
    }

    public static void Write(string level, string message) =>
        _ = TryWrite(level, message);

    public static bool TryWrite(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return true;

        lock (Sync)
        {
            EnsureInitializedNoThrow();
            return WriteLineNoThrow(
                string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant(),
                message.Trim());
        }
    }

    public static bool TryWriteInitialized(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return true;

        lock (Sync)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(_currentLogPath)) return false;
            return WriteLineNoThrow(
                string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant(),
                message.Trim());
        }
    }

    private static void EnsureInitializedNoThrow()
    {
        if (_initialized) return;

        try
        {
            string directory = LogDirectory;
            Directory.CreateDirectory(directory);
            string timestamp = DateTimeOffset.Now.ToString(
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture);
            _currentLogPath = Path.Combine(directory, $"rt2_{timestamp}.log");
            _writer = new StreamWriter(new FileStream(_currentLogPath, FileMode.Append, System.IO.FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false), 16384);
            _writer.WriteLine($"# RolltheSpire2 log started {DateTimeOffset.Now:O}");
            _writer.Flush();
            _initialized = true;
            PruneOldLogsNoThrow(directory);
            _ = WriteLineNoThrow("INFO", "RT2 readable file log enabled.");
        }
        catch (Exception ex)
        {
            _currentLogPath = string.Empty;
            _lastFailureCode = "OperationalLogInitializationFailed:" + ex.GetType().Name;
            _initialized = true;
        }
    }

    private static bool WriteLineNoThrow(string level, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentLogPath)) return false;

            string line =
                $"{DateTimeOffset.Now:HH:mm:ss.fff} [{level}] {message}{System.Environment.NewLine}";
            if (_writer is null) return false;
            _writer.Write(line);
            if (level is "WARN" or "ERROR" || System.Environment.TickCount64 - _lastFlush >= 1000)
            { _writer.Flush(); _lastFlush = System.Environment.TickCount64; }
            return true;
        }
        catch (Exception ex)
        {
            _lastFailureCode = "OperationalLogWriteFailed:" + ex.GetType().Name;
            return false;
        }
    }

    private static void PruneOldLogsNoThrow(string directory)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(directory)
                .GetFiles("rt2_*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            foreach (FileInfo file in files.Skip(MaxRetainedLogs))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Retention is best-effort only.
                }
            }
        }
        catch (Exception ex)
        {
            _lastFailureCode = "OperationalLogRetentionFailed:" + ex.GetType().Name;
        }
    }
}
