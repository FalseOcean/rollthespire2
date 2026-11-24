using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Workspace;

internal static class SearchFeedbackChecks
{
    internal static int Run()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "search-feedback", Guid.NewGuid().ToString("N"));
        var store = new SearchWorkspace(directory, initializeSearchCursor: false);
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(
            Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/v1/context.json")).Context;
        var compiled = SearchCompiler.Compile(SearchQuery.Empty, context);
        var plan = SearchPreparationCompiler.Compile(compiled, new("000000000010", 100, 70, 1)).Plan!;
        var draft = new WorkbenchSearchDraft(context.CharacterKey, context.Ascension, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault);
        var prepared = store.PrepareSearchStart(draft, plan, null);
        var start = prepared.Batch.Start!;
        var order = new[] { BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.HeftyTablet };
        var evaluation = SearchQueryEvaluation.Unknown("OriginalEvaluationReason") with
        {
            RouteRejections = [new("actual-route", order, SearchDisposition.Unknown, "OriginalRouteReason")]
        };
        var candidates = Enumerable.Range(0, 70).Select(index => new SearchCandidate(
            Beta110SeedCodec.FormatOrdinal((ulong)(index + 100)), plan.Model.ProfileId, context.CharacterKey,
            context.Ascension, "snapshot-at-receipt", context.Authority, evaluation)
            { ValidationIssue = "OriginalValidationIssue" }).ToArray();
        var session = new CompletedSearch(candidates);
        using (RuntimeLog.SearchScope("test-search-log-session"))
            store.Runtime.InstallStartedSession(session, compiled.SemanticFingerprint, prepared);
        using (var active = Read(store))
            Require(active.RootElement.GetProperty("LatestAttemptIsExecuting").GetBoolean(), "actual receiving execution is labelled");
        store.Runtime.Tick();

        // Select an unrelated saved observation. It must not borrow the active attempt's start, query or evidence.
        var other = store.PrepareResultWorkspace("older-fingerprint", draft with { Ascension = context.Ascension == 0 ? 1 : 0 });
        store.BindPreparedResultWorkspace(other);
        store.AppendResult(candidates[0], "older-fingerprint", other.History, other.Batch);
        store.FlushAll();
        store = new SearchWorkspace(directory, initializeSearchCursor: false);
        using var persisted = Read(store);
        var document = persisted.RootElement;
        var attempt = document.GetProperty("LatestAttempt");
        var request = attempt.GetProperty("Request");
        Require(!document.GetProperty("LatestAttemptIsExecuting").GetBoolean() &&
            attempt.GetProperty("BatchId").GetString() == prepared.Batch.Id &&
            attempt.GetProperty("LogSessionId").GetString() == "test-search-log-session" &&
            attempt.GetProperty("QueryFingerprint").GetString() == compiled.SemanticFingerprint &&
            request.GetProperty("CanonicalStartSeed").GetString() == start.Plan.CanonicalStartSeed &&
            request.GetProperty("ScanStartOrdinal").GetUInt64() == start.ScanStartOrdinal &&
            attempt.GetProperty("LastCheckpoint").GetProperty("UnverifiedCandidateCount").GetInt32() == 70,
            "reopened feedback preserves actual batch and checkpoint independently of selection");
        var rows = attempt.GetProperty("FirstReceivedCandidates");
        Require(attempt.GetProperty("ReceivedCount").GetInt32() == 70 && rows.GetArrayLength() == SearchFeedbackAttempt.CandidateLimit,
            "bounded receipts retain all 30 seeds in the reported workshop case");
        for (int index = 0; index < rows.GetArrayLength(); index++)
        {
            var receipt = rows[index].GetProperty("Receipt");
            Require(receipt.GetProperty("Seed").GetString() == candidates[index].Seed &&
                receipt.GetProperty("ValidationDisposition").GetString() == "Unknown" &&
                receipt.GetProperty("ValidationIssue").GetString() == "OriginalValidationIssue" &&
                receipt.GetProperty("FailureCode").GetString() == "OriginalEvaluationReason",
                "each received seed retains its disposition and original cause");
        }
        var rejection = rows[0].GetProperty("Details").GetProperty("RouteRejections").GetProperty("Items")[0];
        Require(rejection.GetProperty("Reason").GetString() == "OriginalRouteReason" &&
            rejection.GetProperty("AcquisitionOrder").EnumerateArray().Select(value => value.GetString()).SequenceEqual(order.Select(key => key.Serialized)),
            "route rejection retains observed model acquisition order");
        var history = document.GetProperty("SelectedStoredHistory");
        Require(history.GetProperty("LastStoredRows").GetArrayLength() == 1 &&
            history.GetProperty("LastStoredRows")[0].GetProperty("QueryFingerprint").GetString() == "older-fingerprint" &&
            !history.TryGetProperty("CanonicalStartSeed", out _), "selected stored rows retain independent provenance");
        Console.WriteLine("SEARCH_FEEDBACK_PASS bounded=true persisted=true routes=true");
        return 0;
    }

    private static JsonDocument Read(SearchWorkspace store) => JsonDocument.Parse(store.CaptureSearchFeedbackJson()!);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Search feedback: " + message);
    }

    private sealed class CompletedSearch(IEnumerable<SearchCandidate> candidates) : IProductionSearchSession
    {
        private readonly Queue<SearchCandidate> _remaining = new(candidates);
        public Task Completion => Task.CompletedTask;
        public void Cancel() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public bool TryReadCandidate(out SearchCandidate? candidate) => _remaining.TryDequeue(out candidate);
        public SearchDiagnosticSummarySnapshot GetDiagnosticSummary() => null!;
        public ulong GetSafeNextOrdinal() => 200;
        public bool TryGetSafeNextOrdinal(out ulong nextOrdinal) { nextOrdinal = GetSafeNextOrdinal(); return true; }
        public SearchProgressSnapshot GetProgress() =>
            new(SearchRunState.Completed, 100, 0, 100, 70, 1, 100, SearchDisposition.Unknown, "", false)
            { UnverifiedCandidateCount = 70 };
    }
}
