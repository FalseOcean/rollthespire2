using RolltheSpire2.Ui.Persistence;
using System.Text.Json;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    private readonly Dictionary<string, SearchResultBatch> _processBatches = new(StringComparer.Ordinal);
    private SearchResultBatch? _resultBatch;
    private readonly Dictionary<(string History, string Query, string Snapshot, string Seed), SearchResultAction> _pendingResultActions = [];
    internal SearchResultBatch? ResultBatch => _resultBatch;

    private WorkbenchSearchDraft FreezeDraft(WorkbenchSearchDraft draft) =>
        JsonSerializer.Deserialize<WorkbenchSearchDraft>(JsonSerializer.Serialize(draft.WithoutCapturedAuthority(), _json), _json)
        ?? throw new InvalidDataException("SearchResultDraftFreezeFailed");

    private PersistedSearchResult FreezeResult(PersistedSearchResult result) =>
        JsonSerializer.Deserialize<PersistedSearchResult>(JsonSerializer.Serialize(result, _json), _json)
        ?? throw new InvalidDataException("SearchResultFreezeFailed");

    internal PreparedSearchResultWorkspace PrepareSearchStart(WorkbenchSearchDraft draft,
        ExactSearchExecutionRequest plan, SeedLibraryContext? context)
    {
        var frozen = FreezeDraft(draft);
        var resolution = ResolveSearchStart(frozen, plan);
        var prepared = PrepareResultWorkspace(resolution.Plan.CompiledSearch.SemanticFingerprint, frozen, context,
            resolution.Plan.RunOptions.SkipExactValidation);
        var history = prepared.History;
        prepared.Batch.Start = new(prepared.Workspace.ResultDraft!, resolution.Plan, _environment.Snapshot(),
            history?.QueryKey ?? QueryHistoryIdentity.Key(frozen), history?.EnvironmentFingerprint ?? "",
            history?.Id ?? "", prepared.Workspace.ResultBatchId, prepared.StartedAtUtc,
            prepared.Workspace.ResultContext, resolution);
        return prepared;
    }

    private SearchResultBatch PrepareBatch(string batchId, QueryHistoryEntry? history,
        WorkbenchSearchDraft? draft, SeedLibraryContext? context, IEnumerable<PersistedSearchResult> results)
    {
        _processBatches.TryGetValue(history?.Id ?? batchId, out var previous);
        var batch = new SearchResultBatch(batchId, history?.Id ?? "", draft, context, previous?.Results ?? results);
        if (previous is not null)
            foreach (var pair in previous.LiveResults) batch.LiveResults.Add(pair.Key, pair.Value);
        return batch;
    }

    // History owns accumulated rows, progress and continuation. The document's
    // active fields are a compatibility projection; frozen execution metadata
    // (batch, start time, query fingerprint, draft/context and skip policy) is
    // established separately when preparing a start or selecting a saved query.
    private static void ProjectActiveHistory(SearchWorkspaceDocument document, QueryHistoryEntry? history,
        bool newExecution = false)
    {
        document.ActiveHistoryId = history?.Id ?? "";
        document.Results = history?.Results.ToList() ?? [];
        document.ResultProgress = newExecution ? null : history?.Progress;
        document.ResultContinuation = history?.Continuation;
    }

    private void InstallBatch(SearchResultBatch batch)
    {
        _resultBatch = batch;
        _processBatches[batch.HistoryId.Length > 0 ? batch.HistoryId : batch.Id] = batch;
    }

    internal SearchResultAction ResultAction(PersistedSearchResult result, string? historyId = null,
        SearchPendingCandidate? pending = null)
    {
        string? sourceId = historyId ?? result.SourceAssociation?.SearchRecordId;
        var history = string.IsNullOrEmpty(sourceId) ? ActiveHistory : _workspace.QueryHistory.FirstOrDefault(record => record.Id == sourceId);
        var batch = history is not null ? _processBatches.GetValueOrDefault(history.Id) :
            string.IsNullOrEmpty(sourceId) ? _resultBatch : _processBatches.GetValueOrDefault(sourceId);
        var live = batch?.LiveResults.GetValueOrDefault(result.Seed);
        if (live is not null && (!ReferenceEquals(live.Observation, result) || live.Candidate.IsUnverified != result.IsUnverified ||
            live.Candidate.ValidationDisposition != result.EffectiveValidationDisposition ||
            live.Start.Plan.CompiledSearch.SemanticFingerprint != result.QueryFingerprint)) live = null;
        var frozen = FreezeResult(result);
        var context = frozen.Context ?? (live?.Start.Context is { } original ? SeedLibraryStore.CloneContext(original) : null);
        frozen.Context = context;
        var association = frozen.SourceAssociation ?? (history is null ? null : AssociationFor(history, frozen));
        frozen.SourceAssociation = association;
        return new(live?.Start.BatchId ?? "", association?.SearchRecordId ?? history?.Id ?? sourceId ?? "", frozen,
            (live?.Start.Draft ?? batch?.Draft ?? history?.Draft) is { } draft ? FreezeDraft(draft) : null,
            context, association, SeedLibraryResultProvenance.From(frozen, association), live, pending);
    }

    internal SearchResultAction? PendingResultAction(QueryHistoryEntry history, SearchPendingCandidate pending)
    {
        if (_pendingResultActions.TryGetValue((history.Id, pending.QueryFingerprint, pending.SnapshotFingerprint, pending.Seed), out var action))
            return action with { Pending = pending };
        var recovery = history.ManualValidationCandidates.FirstOrDefault(item =>
            item.Original.Seed == pending.Seed && item.Continuation.PendingCandidates.Any(candidate => SameManualPending(candidate, pending)));
        var original = recovery?.Original ?? history.Results.FirstOrDefault(result => result.Seed == pending.Seed) ??
            _processBatches.GetValueOrDefault(history.Id)?.Results.FirstOrDefault(result => result.Seed == pending.Seed);
        return original is null ? null : ResultAction(original, history.Id, pending);
    }

    private void RecordLiveResult(SearchCandidate candidate, SearchResultBatch? batch = null, SearchStartContext? start = null)
    {
        batch ??= _resultBatch;
        start ??= batch?.Start;
        if (batch is null || start is null) return;
        var row = batch.Results.FirstOrDefault(result => result.Seed == candidate.Seed);
        // Display de-duplication can retain an earlier confirmed observation.
        // In that case its captured authority and plan must remain paired with it.
        if (row is null || row.IsUnverified != candidate.IsUnverified || row.QueryFingerprint != start.Plan.CompiledSearch.SemanticFingerprint ||
            row.EffectiveValidationDisposition != candidate.ValidationDisposition) return;
        batch.LiveResults[candidate.Seed] = new(candidate, start, row);
    }
}
