using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Workspace;

// Process-only state. Plans/candidates retain their captured authority here; none
// of these objects are part of the workspace JSON contract.
internal sealed record SearchStartContext(WorkbenchSearchDraft Draft, ExactSearchExecutionRequest Plan,
    SearchEnvironmentSignature Environment, string QueryKey, string EnvironmentFingerprint,
    string HistoryId, string BatchId, DateTimeOffset StartedAtUtc, SeedLibraryContext? Context,
    SearchWorkspace.SearchStartResolution Resolution);

internal sealed record SearchLiveResult(SearchCandidate Candidate, SearchStartContext Start, PersistedSearchResult Observation);

internal sealed class SearchResultBatch(string id, string historyId, WorkbenchSearchDraft? draft,
    SeedLibraryContext? context, IEnumerable<PersistedSearchResult> results)
{
    internal string Id { get; } = id;
    internal string HistoryId { get; } = historyId;
    internal WorkbenchSearchDraft? Draft { get; } = draft;
    internal SeedLibraryContext? Context { get; } = context;
    internal SearchStartContext? Start { get; set; }
    internal List<PersistedSearchResult> Results { get; } = results.ToList();
    internal Dictionary<string, SearchLiveResult> LiveResults { get; } = new(StringComparer.Ordinal);
}

// An action transports the original row and its source, rather than looking up
// today's active history, editor or batch after the button is pressed.
internal sealed record SearchResultAction(string BatchId, string HistoryId, PersistedSearchResult Result,
    WorkbenchSearchDraft? Draft, SeedLibraryContext? Context, SeedQueryAssociation? Association,
    SeedLibraryResultProvenance Provenance, SearchLiveResult? Live = null,
    SearchPendingCandidate? Pending = null, ExactSearchExecutionRequest? ValidationPlan = null);
