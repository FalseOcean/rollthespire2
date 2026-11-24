using System.Runtime.CompilerServices;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Infrastructure.Diagnostics;

internal enum RuntimeIssueSeverity { Warning, Error }

// Copied evidence only. No Exception, game object, task or UI control is retained.
internal sealed record RuntimeIssue(
    Guid Id, string Code, string Source, string Stage, RuntimeIssueSeverity Severity,
    string MessageKey, string CorrelationId, DateTimeOffset FirstOccurredUtc,
    DateTimeOffset LastOccurredUtc, long OccurrenceCount, string ExceptionDetail)
{
    public bool HasException { get; init; }
}

/// <summary>
/// Process-local, bounded issue evidence. It records failures/degradations without
/// acquiring authority to cancel, recover, retry or alter a result. UI reads snapshots
/// on its own thread; background producers never invoke a UI callback.
/// </summary>
internal sealed class RuntimeIssueLedger
{
    public static RuntimeIssueLedger Current { get; } = new();
    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly Action<RuntimeIssue, bool> _write;
    private readonly LinkedList<RuntimeIssue> _recent = new();
    private readonly Dictionary<IssueKey, LinkedListNode<RuntimeIssue>> _groups = new();
    // Weak identity markers do not keep exception graphs alive after task completion.
    private readonly ConditionalWeakTable<Exception, Seen> _seen = new();
    private long _revision;
    private sealed class Seen { }
    private sealed record IssueKey(string Code, string Source, string Stage, RuntimeIssueSeverity Severity,
        string MessageKey, string CorrelationId, bool HasException, string Detail);

    internal RuntimeIssueLedger(int capacity = 64, Action<RuntimeIssue, bool>? write = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _write = write ?? WriteOperationalLog;
    }

    public long Revision { get { lock (_sync) return _revision; } }
    public IReadOnlyList<RuntimeIssue> Snapshot()
    {
        lock (_sync) return _recent.Reverse().ToArray();
    }

    // A repeated observation of the same thrown instance is not a new occurrence.
    // Independent failures with identical evidence increment the retained count.
    public RuntimeIssue? Record(Exception exception, string code, string source, string stage,
        string messageKey, RuntimeIssueSeverity severity = RuntimeIssueSeverity.Error, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            if (_seen.TryGetValue(exception, out _)) return null;
            _seen.Add(exception, new Seen());
            return Add(code, source, stage, messageKey, Detail(exception), true, severity, correlationId);
        }
    }

    public RuntimeIssue RecordDegradation(string code, string source, string stage, string messageKey,
        string detail, string? correlationId = null)
    {
        return RecordEvidence(code, source, stage, messageKey, detail, RuntimeIssueSeverity.Warning, correlationId);
    }

    // Already copied failure evidence (for example a prediction document's stack)
    // needs no synthetic Exception. Severity is explicit and has no control effect.
    public RuntimeIssue RecordEvidence(string code, string source, string stage, string messageKey,
        string detail, RuntimeIssueSeverity severity = RuntimeIssueSeverity.Error, string? correlationId = null)
    {
        lock (_sync) return Add(code, source, stage, messageKey, detail, false, severity, correlationId);
    }

    // Called under the lock so count, order, revision and the first full log agree.
    // The sink is worker-safe and must never be a UI/event callback.
    private RuntimeIssue Add(string code, string source, string stage, string messageKey, string detail,
        bool hasException, RuntimeIssueSeverity severity, string? correlationId)
    {
        var key = new IssueKey(code ?? "", source ?? "", stage ?? "", severity, messageKey ?? "",
            correlationId ?? RuntimeLog.SessionId, hasException, detail ?? "");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool first = !_groups.TryGetValue(key, out var node);
        RuntimeIssue issue;
        if (first)
        {
            issue = new(Guid.NewGuid(), key.Code, key.Source, key.Stage, severity, key.MessageKey,
                key.CorrelationId, now, now, 1, key.Detail) { HasException = hasException };
            node = _recent.AddLast(issue);
            _groups.Add(key, node);
            if (_groups.Count > _capacity)
            {
                RuntimeIssue old = _recent.First!.Value;
                _groups.Remove(new(old.Code, old.Source, old.Stage, old.Severity, old.MessageKey,
                    old.CorrelationId, old.HasException, old.ExceptionDetail));
                _recent.RemoveFirst();
            }
        }
        else
        {
            issue = node!.Value with { LastOccurredUtc = now,
                OccurrenceCount = node.Value.OccurrenceCount == long.MaxValue ? long.MaxValue : node.Value.OccurrenceCount + 1 };
            node.Value = issue;
            _recent.Remove(node); _recent.AddLast(node);
        }
        if (_revision < long.MaxValue) _revision++;
        try { _write(issue, first); } catch { /* Recording must not replace the original failure. */ }
        return issue;
    }

    private static string Detail(Exception exception)
    {
        try { return exception.ToString(); }
        catch { return exception.GetType().FullName + ":ExceptionDetailUnavailable"; }
    }

    private static void WriteOperationalLog(RuntimeIssue issue, bool first)
    {
        string context = $"runtimeIssue=true;id={issue.Id:N};code={issue.Code};source={issue.Source};stage={issue.Stage};" +
            $"severity={issue.Severity};correlation={issue.CorrelationId};occurrences={issue.OccurrenceCount};messageKey={issue.MessageKey}";
        if (!first) { RuntimeLog.TryBackgroundInfo(context + ";repeated=true"); return; }
        if (issue.Severity == RuntimeIssueSeverity.Error)
            RuntimeLog.FaultEvidence(context, issue.ExceptionDetail, "RT2 runtime issue: " + issue.Code);
        else
            RuntimeLog.Warn(context + ";detailJson=" + RuntimeLog.SafeJson(issue.ExceptionDetail));
    }
}
