using RolltheSpire2.Ui.Persistence;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    // Shells in the menu and a run must not overwrite one another's final cursor.
    // Access and polling are confined to the Godot main thread.
    private static readonly Dictionary<string, WeakReference<SearchWorkspace>> OpenStores =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    internal static SearchWorkspace Open(string directory)
    {
        string key = Path.GetFullPath(directory);
        if (OpenStores.TryGetValue(key, out var reference) && reference.TryGetTarget(out var existing)) return existing;
        var store = new SearchWorkspace(directory, initializeSearchCursor: false);
        OpenStores[key] = new(store);
        return store;
    }

    private SearchWorkspaceRuntime? _runtime;
    internal SearchWorkspaceRuntime Runtime => _runtime ??= new(this);

    internal void SaveCheckpoint(SearchProgressSnapshot progress, SearchContinuationSnapshot? continuation,
        ulong? safeNext, bool wrap = false, QueryHistoryEntry? targetHistory = null,
        SearchResultBatch? targetBatch = null, double elapsedDelta = 0)
    {
        if (targetBatch is null) SaveResultProgress(progress);
        else
        {
            if (targetHistory is { } history)
            {
                if (!_workspace.QueryHistory.Any(record => record.Id == history.Id)) _workspace.QueryHistory.Add(history);
                history.ElapsedSeconds += elapsedDelta; history.Progress = progress;
            }
            if (ReferenceEquals(targetBatch, _resultBatch)) _workspace.ResultProgress = progress;
            MarkWorkspaceDirty();
        }
        if (continuation is not null) SetResultContinuation(continuation, targetHistory, targetBatch);
        if (safeNext is { } next && next < Beta110SeedCodec.SpaceSize) ObserveSafeNextCursor(next);
        else if (wrap) CommitEndOfSpaceWrap();
    }

    internal void RecordCleanupFailure(string historyId, string batchId, SearchProgressSnapshot? previous, Exception exception)
    {
        RecordPresentationFailure(exception, "SessionDispose;history=" + historyId + ";batch=" + batchId);
        if (previous is null) { FlushWorkspace(); return; }
        var updated = previous with { CleanupIssue = previous.CleanupIssue + "\n" + exception };
        var history = _workspace.QueryHistory.FirstOrDefault(record => record.Id == historyId);
        if (history is not null && ReferenceEquals(history.Progress, previous)) history.Progress = updated;
        if (ReferenceEquals(_workspace.ResultProgress, previous)) _workspace.ResultProgress = updated;
        MarkWorkspaceDirty(); FlushWorkspace();
    }

    private void SetResultContinuation(SearchContinuationSnapshot continuation, QueryHistoryEntry? targetHistory = null, SearchResultBatch? targetBatch = null)
    {
        var history = targetBatch is not null ? targetHistory : ActiveHistory;
        var previous = history?.Continuation ?? _workspace.ResultContinuation;
        if (targetBatch is null || ReferenceEquals(targetBatch, _resultBatch)) _workspace.ResultContinuation = continuation;
        if (history is not null)
        {
            if (history.Continuation is { } old && !SearchContinuationIdentity.SameSource(old, continuation) &&
                (old.PendingCandidates.Count > 0 || old.PublishedOrdinals.Count > 0 || old.UnreceivedOrdinals?.Count > 0))
                history.RetainedContinuations.Add(old);
            history.RetainedContinuations.RemoveAll(old => SearchContinuationIdentity.SameSource(old, continuation));
            history.Continuation = continuation;
        }
        if (previous is null || (!ReferenceEquals(previous.PendingCandidates, continuation.PendingCandidates) &&
            !previous.PendingCandidates.SequenceEqual(continuation.PendingCandidates))) ResultsRevision++;
        MarkWorkspaceDirty();
    }

    internal sealed record SearchStartResolution(ExactSearchExecutionRequest Plan,
        SearchContinuationSnapshot? Continuation, string ExplicitOriginChangeId,
        SearchContinuationSnapshot? InitialContinuation = null);

    internal SearchStartResolution ResolveSearchStart(global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft draft, ExactSearchExecutionRequest plan)
    {
        var continuation = ContinuationFor(draft, plan);
        string explicitOrigin = _cursor.PendingOriginChangeId ?? "";
        string start = explicitOrigin.Length > 0 ? CurrentPersistentOriginSeed :
            continuation is not null && continuation.NextOrdinal < Beta110SeedCodec.SpaceSize
                ? Beta110SeedCodec.FormatOrdinal(continuation.NextOrdinal) : plan.CanonicalStartSeed;
        if (plan.CanonicalStartSeed != start)
        {
            // A query resumes its own durable frontier unless the player has
            // explicitly requested a new start. Its pending/delivery ledger is
            // still passed to the session independently of ordinary scanning.
            var execution = ExactSearchExecutionRequestFactory.Compile(plan.CompiledSearch,
                plan.RunOptions with { StartSeed = start });
            if (!execution.Success || execution.Plan is null) throw new InvalidOperationException(execution.Issue);
            plan = execution.Plan;
        }
        SearchContinuationSnapshot? initial = null;
        if (explicitOrigin.Length > 0)
        {
            // Persist the new scan's entrance together with consuming the setting,
            // even if the worker has not produced its first checkpoint yet.
            initial = new(plan.CompiledSearch.SemanticFingerprint, plan.SnapshotFingerprint,
                _cursor.PersistentOrigin, continuation?.PendingCandidates ?? [], continuation?.PublishedOrdinals ?? [])
            {
                ContinuationFingerprint = SearchContinuationIdentity.For(plan),
                UnreceivedOrdinals = continuation?.UnreceivedOrdinals ?? []
            };
        }
        return new(plan, continuation, explicitOrigin, initial);
    }

    internal SearchContinuationSnapshot? ContinuationFor(global::RolltheSpire2.Ui.Workspace.WorkbenchSearchDraft draft,
        ExactSearchExecutionRequest plan)
    {
        if (!_environment.CaptureComplete) return null;
        var history = FindCurrentQuery(draft);
        if (history is null) return null;
        string fingerprint = SearchContinuationIdentity.For(plan);
        return (history.Continuation is { } current ? new[] { current } : [])
            .Concat(history.RetainedContinuations)
            .FirstOrDefault(continuation => SearchContinuationIdentity.Matches(continuation, plan, fingerprint));
    }

    internal IReadOnlyList<SearchPendingCandidate> DisplayPendingCandidates => ActiveHistory is { } history
        ? (history.Continuation?.PendingCandidates ?? [])
            .Concat(history.ManualValidationCandidates.SelectMany(recovery => recovery.Continuation.PendingCandidates))
            .DistinctBy(pending => (pending.QueryFingerprint, pending.SnapshotFingerprint, pending.Seed)).ToArray()
        : _workspace.ResultContinuation?.PendingCandidates ?? [];
    internal IReadOnlyList<QueryHistoryEntry> PendingHistories => _workspace.QueryHistory
        .Where(history => PendingForHistory(history).Count > 0).OrderByDescending(history => history.LastUsedAtUtc).ToArray();
    internal static IReadOnlyList<SearchPendingCandidate> PendingForHistory(QueryHistoryEntry history) =>
        history.RetainedContinuations.Concat(history.Continuation is { } current ? [current] : [])
            .Concat(history.ManualValidationCandidates.Select(recovery => recovery.Continuation))
            .SelectMany(continuation => continuation.PendingCandidates)
            .DistinctBy(pending => (pending.QueryFingerprint, pending.SnapshotFingerprint, pending.Seed)).ToArray();

}
