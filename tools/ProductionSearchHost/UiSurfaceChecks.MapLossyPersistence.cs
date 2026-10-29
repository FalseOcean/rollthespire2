using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunMapLossyPersistenceChecksAsync(Node root, string output, ModRuntimeSnapshot runtime)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, members)!.GetValue(owner)!;
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("MAP_LOSSY_PERSISTENCE " + why); }
        var host = new Control { Size = new(1600, 1000) }; root.AddChild(host);
        var shell = new WorkspaceShell();
        string state = Path.Combine(output, "map-lossy-persistence", Guid.NewGuid().ToString("N"));
        shell.InitializeWithPersistence(runtime, state); host.AddChild(shell);
        try
        {
            shell.Open(); await AwaitPageReady(root, shell);
            RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime);
            var frame = Field<QueryWorkbenchFrame>(shell, "_references");
            var store = Field<SearchWorkspace>(shell, "_persistence");
            var initial = frame.CaptureDraft();
            var draft = initial with { Query = SearchQuery.Empty };
            var compiled = draft.Compile(runtime, out _);
            var context = SeedLibraryContextCapture.ForSearch(draft, runtime);
            var environment = SearchEnvironmentSignatureBuilder.Capture(runtime);
            Check(environment.CaptureComplete, "real environment capture supports continuation checks: " + environment.CaptureIssue);
            store.EnsureEnvironment(environment);
            ExactSearchExecutionRequest Plan(string seed = "000000000000") =>
                ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 128, 1, 1, MapLossy: store.MapLossyPreferences)).Plan!;
            Check(!store.Preferences.MapLossyEnabled, "new workspace defaults to complete map filtering");
            var full = Plan();
            store.SetMapLossyEnabled(true); store.SetMapLossyGpuBatchSize(32768);
            var on = Plan();
            var prepared = store.PrepareSearchStart(draft, on, context);
            store.BindPreparedResultWorkspace(prepared);
            store.ResetMapLossyPreferences();
            Check(!full.MapLossy.Enabled && prepared.Batch.Start!.Plan.MapLossy is { Enabled: true, GpuBatchSize: 32768 },
                "prepared execution freezes policy independently of later preference edits");
            Check(SearchContinuationIdentity.For(full) != SearchContinuationIdentity.For(on), "full and lossy continuation sources differ");
            Check(QueryHistoryIdentity.Key(draft) == prepared.History!.QueryKey, "execution policy does not alter authored query identity");

            // Persistence fixture supplies an observed execution receipt; numerical
            // candidate generation is covered by the separate backend checks.
            var scan = new MapSearchProvenance(on.MapLossy, true, "Cpu", MapLossyOptions.CurrentStrategy, "CpuSelectedOrGpuUnavailable",
                "Act1.Drawing,Scope1.GuaranteedMonster,Scope0.ReachableMaxRest", ["AdditionalCountsFullFinalOnly"], 0, 64, "persistence-attempt")
                { ObservedProposalCalls = 1, ObservedProposalInputs = 64 };
            var continuation = new SearchContinuationSnapshot(compiled.SemanticFingerprint, on.SnapshotFingerprint, 64, [], [])
                { ContinuationFingerprint = SearchContinuationIdentity.For(on), UnreceivedOrdinals = [], MapSearch = scan };
            var progress = new SearchProgressSnapshot(SearchRunState.Cancelled, 64, 0, 128, 1, 1, 64,
                SearchDisposition.NoMatch, "", true);
            string oldIdentity = SearchContinuationIdentity.For(compiled, on.CombatRewardRoutePolicy) +
                ":MapLossy:AssignmentRound1.Drawing5.Multiword.20261028.v2:batch=32768";
            var oldScan = scan with { Strategy = "AssignmentRound1.Drawing5.Multiword.20261028.v2", AttemptFingerprint = "old-er-attempt", EndOrdinalExclusive = 32 };
            var oldPending = new SearchPendingCandidate(31, "00000000000V", "Exact", "OldExecutionFault", "original old fault",
                compiled.SemanticFingerprint, "old-er-attempt", DateTimeOffset.UtcNow) { ContinuationFingerprint = oldIdentity };
            store.SaveCheckpoint(progress, continuation with { SnapshotFingerprint = "old-er-attempt", NextOrdinal = 32,
                ContinuationFingerprint = oldIdentity, MapSearch = oldScan, PendingCandidates = [oldPending] }, 32);
            Check(store.ContinuationFor(draft, on) is null, "new property/drawing strategy cannot resume the previous drawing-only frontier");
            store.SaveCheckpoint(progress, continuation, 64);
            Check(store.ContinuationFor(draft, full) is null, "switching off never resumes a lossy frontier as full coverage");
            Check(store.ContinuationFor(draft, on)?.NextOrdinal == 64, "same lossy policy resumes its own frontier");

            var exact = ProductionExactSearchEvaluator.Evaluate(full,
                TrustedRootHashInput.FromCanonicalSeed(runtime.Profile, full.CanonicalStartSeed));
            Check(exact.IsMatch && exact.Request is not null && exact.Document is not null, "result projection fixture has actual Production Exact observation");
            var candidate = new SearchCandidate(full.CanonicalStartSeed, full.ProfileId, full.CharacterKey, full.Ascension,
                full.SnapshotFingerprint, exact.Request!, exact.Document!, exact.Authority, exact.Evaluation.Evidence,
                exact.Evaluation.MatchedRouteIds, exact.Evaluation.Witnesses) { MapSearch = scan };
            store.AppendResult(candidate, compiled.SemanticFingerprint);
            store.FlushAll();
            var restored = new SearchWorkspace(state, false);
            var history = restored.Workspace.QueryHistory.Single();
            var result = history.Results.Single();
            var currentScan = history.MapScans.Single(item => item.AttemptFingerprint == scan.AttemptFingerprint);
            Check(currentScan.EndOrdinalExclusive == 64 && currentScan.RequestedOptions.GpuBatchSize == 32768,
                "checkpoint roundtrip preserves scanned interval and frozen batch");
            Check(history.MapScans.Single(item => item.AttemptFingerprint == "old-er-attempt").Strategy == "AssignmentRound1.Drawing5.Multiword.20261028.v2" &&
                history.RetainedContinuations.Any(item => item.ContinuationFingerprint == oldIdentity &&
                    item.PendingCandidates.Single().ExceptionText == "original old fault"), "upgrade preserves old interval, strategy and pending original cause");
            Check(currentScan.Scope == scan.Scope && currentScan.FullFinalRemainders.SequenceEqual(scan.FullFinalRemainders),
                "property/drawing scope and explicit Final remainders survive the checkpoint roundtrip");
            Check(result.MapSearch?.AttemptFingerprint == scan.AttemptFingerprint && !restored.Preferences.MapLossyEnabled,
                "result keeps original lossy provenance after preferences return to complete filtering");
            Check(restored.ContinuationFor(draft, on)?.MapSearch?.EndOrdinalExclusive == 64 && restored.ContinuationFor(draft, full) is null,
                "reload preserves on/off continuation isolation without inventing a full rescan");
            var provenance = SeedLibraryResultProvenance.From(result, restored.ResultAssociation(result.Seed, history));
            var library = new SeedLibraryStore(state, Array.Empty<SeedLibraryEntry>());
            library.Save(result.Seed, context, "", resultProvenance: provenance);
            var bookmarked = new SeedLibraryStore(state, Array.Empty<SeedLibraryEntry>()).GetAll().Single();
            Check(bookmarked.ResultProvenance?.MapSearch?.AttemptFingerprint == scan.AttemptFingerprint &&
                bookmarked.ResultProvenance.MapSearch.EndOrdinalExclusive == 64, "favorite roundtrip preserves source scan provenance");
            var options = SeedLibraryStore.CreateJsonOptions();
            Check(JsonSerializer.Deserialize<PersistedSearchResult>("{\"Seed\":\"000000000000\"}", options)!.MapSearch is null,
                "legacy result retains unknown map scan provenance");
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreateUi13(language);
                string applied = MapSearchProvenanceDisplay.Describe(scan, text);
                string unsupported = MapSearchProvenanceDisplay.Describe(scan with { Applied = false, Reason = "OnlyPositiveEliteRestGroupsSupported" }, text);
                Check(applied.Contains("64") && !applied.Contains("map.scan.") && !unsupported.Contains("OnlyPositiveEliteRestGroupsSupported"),
                    "localized applied/unsupported status exposes actual interval " + language);
            }
            File.WriteAllText(Path.Combine(output, "map-lossy-persistence.json"), JsonSerializer.Serialize(new { Passed = checks,
                PolicyFrozen = true, ContinuationIsolation = true, HistoryAndFavoriteRoundtrip = true, NumericalExecution = "SeparateBackendChecks" }));
            GD.Print($"MAP_LOSSY_PERSISTENCE_PASS checks={checks};freeze=true;continuation=on-off-isolated;history=favorite-roundtrip");
        }
        finally { host.Free(); }
    }
}
