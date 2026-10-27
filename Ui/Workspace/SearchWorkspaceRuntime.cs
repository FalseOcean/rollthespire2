using System.Diagnostics;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Workspace;

internal sealed record SearchWorkspaceRuntimeSnapshot(bool Receiving, bool ManualExecuting, bool ManualDeliveryPending,
    int CleanupCount, long RunRevision, SearchProgressSnapshot? Progress, FamilySearchEtaProjectionV1? Eta,
    SearchDiagnosticSummarySnapshot? Diagnostics, long ReceiptRevision, ManualValidationReceipt? Receipt);

internal sealed record ManualValidationReceipt(string Seed, bool ObservationReceived, bool Accepted,
    bool IsMatch, SearchDisposition? Disposition, string FailureCode, string FailureMessage, bool SaveOnly = false);

// One main-thread owner survives views. Open/construction is passive; only work
// attaches the shared SceneTree receiver, which also coordinates storage ticks.
internal sealed partial class SearchWorkspaceRuntime(SearchWorkspace store)
{
    private sealed class Execution(IProductionSearchSession session, string fingerprint, SearchResultBatch? batch, QueryHistoryEntry? history)
    {
        internal IProductionSearchSession Session { get; } = session;
        internal string Fingerprint { get; } = fingerprint;
        internal SearchResultBatch? Batch { get; } = batch;
        internal QueryHistoryEntry? History { get; } = history;
        internal SearchProgressSnapshot? Progress;
        internal double ElapsedSeconds;
        internal Queue<SearchCandidate> Inbox { get; } = new();
        internal bool Wrapped;
    }
    private sealed record Cleanup(Task Task, string HistoryId, string BatchId, SearchProgressSnapshot? Progress);
    private Execution? _execution;
    private readonly List<Cleanup> _cleanup = [];
    private SceneTree? _tree;
    private long _lastFrame;
    private bool _ticking;
    private long _runRevision, _receiptRevision;
    private SearchResultBatch? _lastBatch;
    private FamilySearchEtaProjectionV1? _eta;
    private SearchDiagnosticSummarySnapshot? _diagnostics;
    private ManualValidationReceipt? _receipt;
    internal bool Receiving => _execution is not null;
    internal bool HasSearchInFlight => Receiving || HasManualCandidateValidation;
    internal bool IsPolling => _tree is not null;
    internal int CleanupCount => _cleanup.Count;
    private bool HasWork => Receiving || _manualCandidateValidations.Count > 0 || _cleanup.Count > 0 || store.HasPendingWorkspaceCommit;
    internal SearchWorkspaceRuntimeSnapshot Snapshot => new(Receiving, HasManualCandidateValidation,
        _manualCandidateValidations.Any(operation => operation.Task.IsCompleted), _cleanup.Count, _runRevision,
        _execution?.Progress ?? store.Workspace.ResultProgress, ReferenceEquals(_lastBatch, store.ResultBatch) ? _eta : null,
        ReferenceEquals(_lastBatch, store.ResultBatch) ? _diagnostics : null, _receiptRevision, _receipt);

    internal SearchFeasibilityResult Start(WorkbenchSearchDraft draft, ModRuntimeSnapshot runtime,
        int target, int workers, SceneTree? tree)
    {
        if (HasSearchInFlight) throw new InvalidOperationException("SearchAlreadyRunning");
        var compiled = draft.Compile(runtime, out _);
        var feasibility = SearchFeasibilityAnalyzer.Analyze(compiled);
        if (feasibility.IsImpossible) return feasibility;
        store.InitializeWorkbenchCursor();
        var execution = ExactSearchExecutionRequestFactory.Compile(compiled,
            new(store.CurrentNextCursorSeed, long.MaxValue, target, workers, SkipExactValidation: store.Preferences.SkipExactValidation));
        if (!execution.Success || execution.Plan is null) throw new InvalidOperationException(execution.Issue);
        Auxiliary(() => store.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(runtime)), "EnvironmentSave");
        Auxiliary(() => store.SaveWorkbench(draft), "DraftSave");
        SeedLibraryContext? context = null;
        Auxiliary(() => context = SeedLibraryContextCapture.ForSearch(draft, runtime), "ResultContext");
        var prepared = store.PrepareSearchStart(draft, execution.Plan, context);
        var start = prepared.Batch.Start!;
        var session = FamilyExecutionCoordinator.Start(start.Plan, queueCapacity: 64,
            gpuAvailable: store.Preferences.SearchMode == "CPU" ? false : null, continuation: start.Resolution.Continuation);
        // The session belongs to this owner before any optional publication,
        // diagnostics, save or frame binding can fail.
        InstallStartedSession(session, start.Plan.CompiledSearch.SemanticFingerprint, prepared, tree);
        store.ResolvePresentationFailure("QueryAdmission");
        return feasibility;
    }

    internal void InstallStartedSession(IProductionSearchSession session, string fingerprint,
        SearchWorkspace.PreparedSearchResultWorkspace? prepared = null, SceneTree? tree = null)
    {
        if (HasSearchInFlight) throw new InvalidOperationException("SearchAlreadyRunning");
        _execution = new(session, fingerprint, prepared?.Batch ?? store.ResultBatch,
            prepared?.History ?? store.Workspace.QueryHistory.FirstOrDefault(history => history.Id == store.Workspace.ActiveHistoryId));
        _runRevision++; _diagnostics = null; _eta = null;
        _lastBatch = _execution.Batch;
        if (prepared is not null) Auxiliary(() => store.BindPreparedResultWorkspace(prepared), "StartReceiverInstall");
        if (session is ProductionSearchSession production) _eta = production.EtaProjection;
        Auxiliary(() => _execution.Progress = session.GetProgress(), "StartProgress");
        Auxiliary(() => Bind(tree), "StartFrameBinding");
    }

    internal SearchDiagnosticSummarySnapshot? CaptureDiagnostics()
    {
        if (_execution is { } active) Auxiliary(() => _diagnostics = active.Session.GetDiagnosticSummary(), "SearchDiagnostics");
        return Snapshot.Diagnostics;
    }

    internal void Cancel(bool flush = true)
    {
        if (_execution is not { } active) return;
        Auxiliary(active.Session.Cancel, "CancelRequest");
        Auxiliary(() => Checkpoint(active, complete: false), "ClosingCheckpoint");
        if (flush) Auxiliary(store.FlushAll, "ClosingSave");
    }

    internal void Close(SceneTree? tree)
    {
        Cancel(flush: false);
        Auxiliary(store.FlushAll, "ClosingSave");
        Auxiliary(() => Bind(tree), "ClosingFrameBinding");
    }

    private void Bind(SceneTree? tree)
    {
        if (tree is null || _tree is not null || !HasWork) return;
        _tree = tree; _lastFrame = Stopwatch.GetTimestamp();
        tree.ProcessFrame += ProcessFrame;
    }
    private void ProcessFrame()
    {
        long now = Stopwatch.GetTimestamp();
        double delta = Math.Max(0, (now - _lastFrame) / (double)Stopwatch.Frequency);
        _lastFrame = now;
        Tick(delta, now);
    }
    internal void Tick(double delta = 0, long? now = null)
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            if (_execution is { } active) Auxiliary(() => Receive(active), "ResultHandoff");
            PollManualCandidateValidations(now ?? Stopwatch.GetTimestamp());
            foreach (var cleanup in _cleanup.ToArray())
            {
                if (!cleanup.Task.IsCompleted) continue;
                try { cleanup.Task.GetAwaiter().GetResult(); }
                catch (Exception exception) { store.RecordCleanupFailure(cleanup.HistoryId, cleanup.BatchId, cleanup.Progress, exception); }
                _cleanup.Remove(cleanup);
            }
            Auxiliary(() => store.Tick(Math.Max(0, delta)), "WorkspaceTick");
        }
        finally
        {
            _ticking = false;
            if (!HasWork && _tree is { } tree)
            { tree.ProcessFrame -= ProcessFrame; _tree = null; }
        }
    }

    private void Receive(Execution active)
    {
        // A device-creation recovery publishes a new actual CPU plan and an
        // unpriced ETA. Refresh on the existing main-thread receive boundary.
        if (active.Session is ProductionSearchSession production) _eta = production.EtaProjection;
        var session = active.Session;
        void Drain()
        {
            while (session.TryReadCandidate(out var candidate))
                if (candidate is not null) active.Inbox.Enqueue(candidate);
            while (active.Inbox.TryPeek(out var candidate))
            {
                store.AppendResult(candidate, active.Fingerprint, active.History, active.Batch);
                session.AcknowledgeCandidate(candidate.Seed);
                active.Inbox.Dequeue();
            }
        }
        Drain();
        bool complete = session.Completion.IsCompleted;
        if (complete) Drain();
        Checkpoint(active, complete);
        if (complete) Auxiliary(() => _diagnostics = session.GetDiagnosticSummary(), "FinalDiagnostics");
        if (!complete) return;
        _execution = null;
        Auxiliary(store.FlushAll, "TerminalSave");
        Task disposal;
        try { disposal = session.DisposeAsync().AsTask(); }
        catch (Exception exception) { disposal = Task.FromException(exception); }
        _cleanup.Add(new(disposal, active.Batch?.HistoryId ?? store.Workspace.ActiveHistoryId,
            active.Batch?.Id ?? store.Workspace.ResultBatchId, active.Progress));
    }

    private void Checkpoint(Execution active, bool complete)
    {
        var session = active.Session;
        var continuation = session.CaptureContinuation();
        ulong next;
        bool safe = continuation is not null ? (next = continuation.NextOrdinal) <= Beta110SeedCodec.SpaceSize : session.TryGetSafeNextOrdinal(out next);
        bool wrap = safe && next == Beta110SeedCodec.SpaceSize && complete && !active.Wrapped;
        var progress = session.GetProgress();
        store.SaveCheckpoint(progress, continuation, safe ? next : null, wrap, active.History, active.Batch,
            Math.Max(0, progress.ElapsedSeconds - active.ElapsedSeconds));
        active.ElapsedSeconds = progress.ElapsedSeconds;
        active.Progress = progress;
        if (wrap) active.Wrapped = true;
    }

    private void Auxiliary(Action action, string stage, string? seed = null)
    { try { action(); store.ResolvePresentationFailure(stage); } catch (Exception exception) { store.RecordPresentationFailure(exception, stage, seed); } }
}
