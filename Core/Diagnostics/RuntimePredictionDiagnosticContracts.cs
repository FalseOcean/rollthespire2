using System.Collections.ObjectModel;

namespace RolltheSpire2.Core.Diagnostics;

public enum RuntimePredictionDiagnosticLevel
{
    Error = 0,
    Warning = 1,
    Info = 2,
    Trace = 3
}

public enum RuntimePredictionDiagnosticStatus
{
    Started,
    Succeeded,
    Partial,
    Failed,
    Skipped,
    Completed
}

/// <summary>
/// Immutable, serialization-ready prediction diagnostic event. Details may contain
/// only copied primitives, strings, enums, and immutable arrays/dictionaries.
/// Live game/Godot objects are forbidden.
/// </summary>
public sealed record RuntimePredictionDiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string RequestId,
    string Profile,
    string Seed,
    RuntimePredictionDiagnosticLevel Level,
    string Component,
    string Stage,
    RuntimePredictionDiagnosticStatus Status,
    string ReasonCode,
    IReadOnlyDictionary<string, object?> Details)
{
    public static RuntimePredictionDiagnosticEvent Create(
        string requestId,
        string profile,
        string seed,
        RuntimePredictionDiagnosticLevel level,
        string component,
        string stage,
        RuntimePredictionDiagnosticStatus status,
        string reasonCode = "",
        IReadOnlyDictionary<string, object?>? details = null) => new(
            DateTimeOffset.UtcNow,
            requestId ?? string.Empty,
            profile ?? string.Empty,
            seed ?? string.Empty,
            level,
            component ?? string.Empty,
            stage ?? string.Empty,
            status,
            reasonCode ?? string.Empty,
            details ?? EmptyDetails);

    private static IReadOnlyDictionary<string, object?> EmptyDetails { get; } =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

/// <summary>
/// Worker-safe sink. Implementations must never throw into prediction/search code and
/// must not require Godot, ModelDb, RunState, Player, SaveManager, or real RNG objects.
/// </summary>
public interface IRuntimePredictionDiagnosticSink
{
    bool Enabled { get; }
    RuntimePredictionDiagnosticLevel Verbosity { get; }
    string LogDirectory { get; }
    string LatestLogPath { get; }
    string LastFailureCode { get; }

    void Configure(bool enabled, RuntimePredictionDiagnosticLevel verbosity);
    bool TryWrite(RuntimePredictionDiagnosticEvent diagnosticEvent, bool force = false);
}


/// <summary>
/// Safe fallback used when the optional diagnostic writer cannot be initialized.
/// It preserves the requested directory/failure reason for UI display while never
/// writing files or affecting prediction.
/// </summary>
public sealed class NullRuntimePredictionDiagnosticSink : IRuntimePredictionDiagnosticSink
{
    public NullRuntimePredictionDiagnosticSink(string logDirectory = "", string failureCode = "")
    {
        LogDirectory = logDirectory ?? string.Empty;
        LastFailureCode = failureCode ?? string.Empty;
    }

    public bool Enabled => false;
    public RuntimePredictionDiagnosticLevel Verbosity => RuntimePredictionDiagnosticLevel.Info;
    public string LogDirectory { get; }
    public string LatestLogPath => string.Empty;
    public string LastFailureCode { get; }

    public void Configure(bool enabled, RuntimePredictionDiagnosticLevel verbosity)
    {
    }

    public bool TryWrite(RuntimePredictionDiagnosticEvent diagnosticEvent, bool force = false) => false;
}

public sealed record SearchDiagnosticSummarySnapshot(
    IReadOnlyDictionary<string, long> DispositionCounts,
    IReadOnlyDictionary<string, long> FailureReasonCounts,
    IReadOnlyDictionary<string, string> RepresentativeSeeds,
    string FirstFailureCode,
    string FirstFailureSeed);
