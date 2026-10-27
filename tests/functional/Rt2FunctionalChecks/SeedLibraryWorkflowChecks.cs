using RolltheSpire2.Ui.Workspace;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Diagnostics;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Tests.FastSearch;

// Runs under the existing native host; fixed Predictor contexts, no Search scan.
internal static class SeedLibraryWorkflowChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("NativeSeedLibraryWorkflow");
        string directory = Path.Combine(Path.GetTempPath(), "RolltheSpire2-seed-workflow", Guid.NewGuid().ToString("N"));
        Run(runtime, directory, PresetWorkbenchChecks.BuildDraft(runtime, 1), PresetWorkbenchChecks.BuildDraft(runtime, 4));
        return 0;
    }

    private static void Run(ModRuntimeSnapshot runtime, string directory, WorkbenchSearchDraft solo, WorkbenchSearchDraft party)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("SEED_LIBRARY_WORKFLOW " + message);
        }
        var json = SeedLibraryStore.CreateJsonOptions();
        var store = new SeedLibraryStore(directory, []);
        var selectedContext = SeedLibraryContextCapture.ForSearch(party, runtime);
        Check(selectedContext.Players.All(p => p.OpeningPremise.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.GoldenPearl),
            "Search capture retains each declared opening premise");
        // This fixed seed is not claimed to match the filter fixture. Use the
        // neutral opening context for native reopen; witness transport is checked below.
        var searchContext = SeedLibraryContextCapture.ForSearch(party with { Query = party.Query with
            { Players = party.Query.Players.Select(p => p with { Conditions = p.Conditions with { OpeningRoute = null } }).ToArray() } }, runtime);
        Check(searchContext.Mode == WorldGameMode.Multiplayer && searchContext.Players.Count == party.Players.Count &&
            searchContext.Players.Select(p => (p.Slot, p.Character, p.UnlockSource))
                .SequenceEqual(party.Players.Select(p => (p.Slot, p.Character, p.UnlockSource))),
            "Search capture retains ordered table and source");
        Check(searchContext.Players.All(p => p.OpeningPremise.AncientBranches.Count == 0 &&
            p.OpeningPremise.CombatCardRewards is null && p.OpeningPremise.EventResultConditions.Count == 0 &&
            p.OpeningPremise.RelicSequenceConstraints.Count == 0 && p.OpeningPremise.StandardMaps.Count == 0),
            "Library context excludes Search predicates");
        int originalRuns = searchContext.Players[1].Unlocks.NumberOfRuns;
        party.Players[1].RequireUnlocks().NumberOfRuns++;
        Check(searchContext.Players[1].Unlocks.NumberOfRuns == originalRuns, "Search capture isolates mutable unlock DTOs");
        party.Players[1].RequireUnlocks().NumberOfRuns--;
        var entry = store.Save("000000000000", searchContext with { SelectedSlot = 1 }, "中文备注 🌱");
        var reopened = new SeedLibraryStore(directory, []).GetAll().Single();
        Check(reopened.Note == "中文备注 🌱" && reopened.Context!.SelectedSlot == 1, "Library reopen preserves note and selected seat");
        Check(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(searchContext.Players, json),
            JsonSerializer.SerializeToNode(reopened.Context!.Players, json)), "Library retains the captured table context");

        var icons = new ReflectionGameIconResolver("native-seed-library-check");
        using var characterIcons = new NativeCharacterPoolIconProvider(icons);
        var tooltip = new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(), new RuntimePotionTooltipResolver(), new RuntimeCardTooltipResolver());
        var page = new AnalysisPage(runtime, icons, characterIcons,
            [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent, BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Regent], tooltip);
        page.ApplyLocalization(JsonUiTextProvider.CreatePredictorUi13("en"), RuntimeGameContentNameResolver.Create("en"));
        var persistence = new SearchWorkspace(Path.Combine(directory, "predictor"), false);
        var controller = new AnalysisPageController(page, runtime, new RuntimePredictionSettings(),
            new NullRuntimePredictionDiagnosticSink(), persistence,
            (_, _, _) => { });

        controller.OpenSeedLibraryEntry(reopened);
        var actual = controller.CaptureSeedLibraryContext();
        Check(actual.Mode == WorldGameMode.Multiplayer && actual.SelectedSlot == 1 && actual.Ascension == searchContext.Ascension &&
            actual.Players.Select(p => (p.Slot, p.Character, p.UnlockSource))
                .SequenceEqual(searchContext.Players.Select(p => (p.Slot, p.Character, p.UnlockSource))),
            "Predictor opens complete roster and selected seat");
        Check(page.LastRequest!.PlayerSlotIndex == 1 && page.LastRequest.PlayersCount == party.Players.Count &&
            page.LastRequest.Character.CharacterKey == party.Players[1].Character, "Predictor request uses saved seat");
        Check(actual.Players.Select(p => p.AncientPremises).SequenceEqual(searchContext.Players.Select(p => p.AncientPremises)),
            "Predictor retains each player's Ancient premises");
        Check(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(actual.Players.Select(p => p.Unlocks), json),
            JsonSerializer.SerializeToNode(searchContext.Players.Select(p => p.Unlocks), json)), "Predictor recapture retains unlock values");
        Check(store.TryFind(reopened.Seed, actual, out var partyFavorite) && partyFavorite.Id == reopened.Id,
            "Opening and recapturing a neutral party finds the existing favorite");

        var twoPlayer = store.Save(reopened.Seed, searchContext with { Players = searchContext.Players.Take(2).ToArray(), SelectedSlot = 1 }, "2P");
        controller.OpenSeedLibraryEntry(twoPlayer);
        Check(controller.CaptureSeedLibraryContext().Players.Count == 2 && page.LastRequest!.PlayersCount == 2 &&
            page.LastRequest.PlayerSlotIndex == 1, "Opening a two-player favorite replaces the full four-player roster");

        var soloContext = SeedLibraryContextCapture.ForSearch(solo with { Query = solo.Query with { OpeningRoute = null } }, runtime);
        var soloEntry = store.Save("000000000000", soloContext, "solo");
        controller.OpenSeedLibraryEntry(soloEntry);
        var soloCaptured = controller.CaptureSeedLibraryContext();
        Check(soloCaptured.Mode == WorldGameMode.Singleplayer && soloCaptured.Players.Count == 1 &&
            page.LastRequest!.PlayersCount == 1 && page.LastRequest.PlayerSlotIndex == 0 &&
            page.LastRequest.Character.CharacterKey == solo.Character,
            "Opening solo clears previous party predictor context");
        Check(soloCaptured.Players[0].UnlockSource == soloContext.Players[0].UnlockSource &&
            JsonNode.DeepEquals(JsonSerializer.SerializeToNode(soloCaptured.Players[0].Unlocks, json),
                JsonSerializer.SerializeToNode(soloContext.Players[0].Unlocks, json)), "Solo predictor uses saved unlock context");
        Check(store.TryFind(soloEntry.Seed, soloCaptured, out var soloFavorite) && soloFavorite.Id == soloEntry.Id,
            "Opening and recapturing a default solo finds the existing favorite");
        page.SetSeedText("invalid!", commit: false);
        bool visibleInputRejected = false;
        try { controller.CaptureSeedLibraryContext(); }
        catch (InvalidOperationException) { visibleInputRejected = true; }
        Check(visibleInputRejected && !page.CanFavoriteVisibleSeed, "Invalid visible input cannot favorite the previous valid prediction");
        page.SetSeedText(soloEntry.Seed, commit: true);
        var candidate = new SearchCandidate("000000000000", runtime.Profile.ProfileId, solo.Character, solo.Ascension, "fixture",
            page.LastRequest!, page.LastDocument!, page.LastRequest!.Authority, [], [],
            [new("000000000000", "choice.2.fixture-route", [], [], "", null, [])]);
        var witnessed = SeedLibraryContextCapture.WithWitness(soloContext, candidate);
        Check(witnessed.Players[0].Selection is { ChoiceSlot: 2, Route: "fixture-route", Explicit: true },
            "Search result transport extracts one explicit solo choice and route");
        CheckIncompleteInformationPresentation(page, page.LastRequest!, page.LastDocument!, Check);
        if (soloCaptured.Players[0].Selection is { ChoiceSlot: > 0, Route.Length: > 0 } validSelection)
        {
            var actualWitness = SeedLibraryContextCapture.WithWitness(soloContext, candidate with
            { Witnesses = [new(candidate.Seed, $"choice.{validSelection.ChoiceSlot}.{validSelection.Route}", [], [], "", null, [])] });
            var witnessedEntry = store.Save(candidate.Seed, actualWitness, "native witnessed opening");
            controller.OpenSeedLibraryEntry(witnessedEntry);
            Check(store.TryFind(candidate.Seed, controller.CaptureSeedLibraryContext(), out var witnessedFavorite) &&
                witnessedFavorite.Id == witnessedEntry.Id, "Native witnessed opening finds the existing favorite after reopen");
        }
        else Console.WriteLine("SEED_LIBRARY_WORKFLOW nativeWitnessRoundtrip=unavailable:no-concrete-native-selection");
        var partyDocument = new SeedPredictionDocument
        {
            Context = page.LastDocument!.Context, PredictorId = "transport-fixture", OriginalSeed = candidate.Seed, CanonicalSeed = candidate.Seed,
            Sections = [], Warnings = [], Diagnostics = [], OverallStatus = SeedPredictionOverallStatus.Completed,
            Party = new(candidate.Seed, 0, runtime.Detection.NormalizedVersion, party.Ascension, "transport-fixture", [], [],
                party.Players.Select(p => new PlayerInitialOffers(p.Slot, p.Character,
                    [BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.GoldenPearl]) { UnlockSource = p.UnlockSource }).ToArray())
            {
                Transactions = [new(1, BaseGameModelKeys.Relics.GoldenPearl, PartyNeowIntent.PremiseOnly, [], [], [])
                    { OpeningRouteId = "party-route" }]
            }
        };
        var partyWitnessed = SeedLibraryContextCapture.WithWitness(searchContext, candidate with { Document = partyDocument });
        Check(partyWitnessed.Players[1].Selection is { ChoiceSlot: 2, Route: "party-route", Explicit: false } &&
            partyWitnessed.Players.Where(p => p.Slot != 1).All(p => p.Selection is null),
            "Automatic witness stays on its slot without declaring a pickup");
        var declaredContext = searchContext with { Players = searchContext.Players.Select(p => p.Slot == 1
            ? p with { OpeningPremise = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) } } : p).ToArray() };
        Check(SeedLibraryContextCapture.WithWitness(declaredContext, candidate with { Document = partyDocument }).Players[1].Selection!.Explicit,
            "Declared pickup premise retains its explicit witness");
        bool rosterRejected = false;
        try { SeedLibraryContextCapture.WithWitness(searchContext with { Players = searchContext.Players.Select(p => p.Slot == 1
            ? p with { Character = BaseGameModelKeys.Characters.Necrobinder } : p).ToArray() }, candidate with { Document = partyDocument }); }
        catch (InvalidOperationException) { rosterRejected = true; }
        Check(rosterRejected, "Same-count mismatched roster cannot borrow result witness");
        bool staleRejected = false;
        try { controller.OpenSeedLibraryEntry(entry with { Context = entry.Context! with { GameVersion = "future" } }); }
        catch (InvalidOperationException) { staleRejected = true; }
        Check(staleRejected && controller.CaptureSeedLibraryContext().Mode == WorldGameMode.Singleplayer,
            "Stale library version rejects before switching predictor mode");
        CheckSearchOriginRoundtrips(page, controller, runtime, solo, Path.Combine(directory, "search-origin"), Check);
        CheckManualPendingQuota(runtime, solo, Path.Combine(directory, "manual-pending-quota"), Check);
        CheckPredictorPartyConfiguration(page, controller, persistence, Path.Combine(directory, "predictor"), runtime, party, Check);
        page.Free(); tooltip.Free();
        Console.WriteLine($"SEED_LIBRARY_WORKFLOW checks={checks};failures=0;seedScans=0");
    }

    // Store/compile lifecycle coverage needs no Godot controls or simulated clicks.
    private static void CheckManualPendingQuota(ModRuntimeSnapshot runtime, WorkbenchSearchDraft solo,
        string directory, Action<bool, string> check)
    {
        string Seed(ulong ordinal) => Beta110SeedCodec.FormatOrdinal(ordinal);
        var draft = new WorkbenchSearchDraft(solo.Character, solo.Ascension, SearchQuery.Empty, solo.AncientPremises);
        var compiled = draft.Compile(runtime, out var authority);
        var context = SeedLibraryContextCapture.ForSearch(draft, runtime);
        var plan = ExactSearchExecutionRequestFactory.Compile(compiled, new(Seed(0), 1, 1, 1)).Plan
            ?? throw new InvalidOperationException("Manual quota fixture Exact admission failed");
        var store = new SearchWorkspace(directory, false);
        store.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(runtime));
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace(compiled.SemanticFingerprint, draft, context, skipExactValidation: true));
        void Add(ulong ordinal) => store.AppendResult(new(Seed(ordinal), plan.ProfileId, plan.CharacterKey, plan.Ascension,
            plan.SnapshotFingerprint, null!, null, authority, [], [], [])
            { ValidationDisposition = SearchDisposition.NotEvaluatedByPolicy, ValidationIssue = "quota-original-unchecked" }, compiled.SemanticFingerprint);
        Add(0);
        check(store.TryManualValidationSource(Seed(0), out var history, out var original, out _), "Quota fixture original source available");
        string historyId = history.Id;
        var scanPending = new SearchPendingCandidate(500, Seed(500), "ExactValidation", "scan-original-cause", "scan-original-detail",
            compiled.SemanticFingerprint, "ordinary-scanning-snapshot", DateTimeOffset.UtcNow);
        history.Continuation = new(compiled.SemanticFingerprint, "ordinary-scanning-snapshot", 900, [scanPending], [42]);
        string normal = JsonSerializer.Serialize(history.Continuation);
        var failure = new IOException("quota-first-original-cause");
        var first = store.Runtime.TrackManualCandidateValidation(history, original, plan, () => throw failure);
        try { first.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); } catch (IOException) { }
        store.Runtime.PollManualCandidateValidations();
        check(first.Accepted && history.ManualValidationCandidates.Single().Continuation is { NextOrdinal: 0, PublishedOrdinals.Count: 0 } &&
            JsonSerializer.Serialize(history.Continuation) == normal,
            "Manual fault owns a single-seed snapshot without borrowing the scan cursor, publication set or other pending work");
        var firstPending = SearchWorkspace.PendingForHistory(history).Single(p => p.Stage == "ManualCandidateCheck");
        for (ulong ordinal = 1; ordinal <= SearchWorkspace.ResultsPerQuery; ordinal++) Add(ordinal);
        store.FlushAll();
        store = new(directory, false);
        history = store.Workspace.QueryHistory.Single(record => record.Id == historyId);
        var pending = SearchWorkspace.PendingForHistory(history).Single(p => p.Stage == "ManualCandidateCheck");
        check(store.DisplayResults.Count == SearchWorkspace.ResultsPerQuery &&
            store.DisplayResults.All(result => result.Seed != Seed(0)) && history.Results.All(result => result.Seed != Seed(0)) &&
            pending == firstPending && store.TryManualValidationSource(Seed(0), out _, out original, out _, historyId, pending),
            "After quota eviction and restart pending independently restores the original seed, cause and author context");

        var metadata = history.ManualValidationCandidates;
        history.ManualValidationCandidates = [];
        history.RetainedContinuations.Add(metadata.Single().Continuation);
        check(!store.TryManualValidationSource(Seed(0), out _, out _, out var missing, historyId, pending) && missing.Length > 0,
            "Old quota-evicted pending without saved source metadata refuses recovery rather than guessing an original result");
        history.RetainedContinuations.Clear(); history.ManualValidationCandidates = metadata;

        int changedAscension = solo.Ascension == 0 ? 1 : solo.Ascension - 1;
        store.BindPreparedResultWorkspace(store.PrepareResultWorkspace("different-active-query", draft with { Ascension = changedAscension }, context with { Ascension = changedAscension }));
        check(store.TryManualValidationSource(Seed(0), out history, out original, out _, historyId, pending) && store.DisplayResults.Count == 0,
            "Pending-list identity selects its original history even when another query is active");
        var retryPlan = RolltheSpire2.Ui.Workspace.SearchResultProjection.RebuildSavedValidationPlan(runtime, history, original);
        var secondFailure = new IOException("quota-explicit-retry-cause");
        var again = store.Runtime.TrackManualCandidateValidation(history, original, retryPlan, () => throw secondFailure);
        try { again.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(); } catch (IOException) { }
        store.Runtime.PollManualCandidateValidations();
        pending = SearchWorkspace.PendingForHistory(history).Single(p => p.Stage == "ManualCandidateCheck");
        check(again.Accepted && pending.Attempts == 2 && pending.FirstFailedAtUtc == firstPending.FirstFailedAtUtc &&
            pending.ExceptionText.Contains(failure.ToString()) && pending.ExceptionText.Contains(secondFailure.ToString()) &&
            pending.SnapshotFingerprint == firstPending.SnapshotFingerprint && JsonSerializer.Serialize(history.Continuation) == normal && store.DisplayResults.Count == 0,
            "Another explicit failure preserves both causes and the original obligation without affecting active-query results or ordinary scanning");
        store = new(directory, false);
        check(store.TryManualValidationSource(Seed(0), out history, out original, out _, historyId, pending), "Second failure remains recoverable after another restart");
        retryPlan = RolltheSpire2.Ui.Workspace.SearchResultProjection.RebuildSavedValidationPlan(runtime, history, original);
        if (!SeedPredictionRequest.TryCreateFromRootHash(TrustedRootHashInput.FromCanonicalSeed(runtime.Profile, Seed(0)),
            CharacterIdentity.FromKey(plan.CharacterKey), plan.Ascension, 1, 0, retryPlan.Authority, plan.Evaluation.AncientOptionConditions,
            SeedPredictionDomainSelection.None, 1, false, out var request, out var requestIssue))
            throw new InvalidOperationException("Manual quota success request: " + requestIssue);
        var document = new SeedPredictionDocument { Context = SeedPredictionDocument.CreateContext(request!, runtime.Detection.DisplayVersion, plan.ProfileId),
            PredictorId = "ManualQuotaLifecycleFixture", OriginalSeed = Seed(0), CanonicalSeed = Seed(0), Sections = [], Warnings = [], Diagnostics = [],
            OverallStatus = SeedPredictionOverallStatus.Completed };
        int exactCalls = 0;
        var success = store.Runtime.TrackManualCandidateValidation(history, original, retryPlan, () =>
        {
            Interlocked.Increment(ref exactCalls);
            return new(request, document, retryPlan.Authority, SearchQueryEvaluation.Match([new("ManualQuotaFixture")], ["quota-fixture"]), "");
        });
        success.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        string workspaceFile = Path.Combine(directory, "RolltheSpire2", "state", "search_workspace.json");
        long now = Stopwatch.GetTimestamp();
        using (var locked = new FileStream(workspaceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.Runtime.PollManualCandidateValidations(now);
            for (int i = 0; i < 20; i++) store.Runtime.PollManualCandidateValidations(now);
            check(success.ObservationReceived && !success.Accepted && exactCalls == 1 && success.DeliveryAttempts == 1 &&
                history.ManualValidationCandidates.Single().Continuation.PendingCandidates.Single() == pending && store.DisplayResults.Count == 0,
                "A successful Exact with a failed save retains pending plus the actual observation and never reruns Exact during delivery retries");
            var disk = new SearchWorkspace(directory, false);
            check(disk.PendingHistories.Single(record => record.Id == historyId).ManualValidationCandidates.Single().Original.Seed == Seed(0),
                "A failed delivery cannot erase the disk's independent pending source");
        }
        // Freeze the real receipt and original obligation in the first commit,
        // then simulate a restart before the second (pending-removal) commit.
        store.FlushAll();
        store = new(directory, false);
        history = store.Workspace.QueryHistory.Single(record => record.Id == historyId);
        check(store.HasManualPendingObservation(historyId, pending) &&
            !store.TryManualValidationSource(Seed(0), out _, out _, out var deliveryIssue, historyId, pending) &&
            deliveryIssue == "ManualValidationObservationAwaitingDelivery",
            "A durable received observation survives restart independently and prohibits repeating Exact");
        using (var locked = new FileStream(workspaceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(!store.DeliverManualPendingObservation(historyId, pending) && exactCalls == 1 &&
                store.HasManualPendingObservation(historyId, pending),
                "Restarted save-only retry retains the original observation when storage still fails");
        bool delivered = store.DeliverManualPendingObservation(historyId, pending);
        store = new(directory, false);
        history = store.Workspace.QueryHistory.Single(record => record.Id == historyId);
        check(delivered && exactCalls == 1 && history.ManualValidationCandidates.Count == 0 &&
            SearchWorkspace.PendingForHistory(history).Single() == scanPending &&
            history.Results.Single(result => result.Seed == Seed(0)).EffectiveValidationDisposition == SearchDisposition.Match &&
            store.DisplayResults.Count == 0 && JsonSerializer.Serialize(history.Continuation) == normal,
            "Durable Match resolves only its manual obligation, retaining ordinary pending/cursor and isolating the active query");
    }

    private static void CheckSearchOriginRoundtrips(AnalysisPage page, AnalysisPageController controller,
        ModRuntimeSnapshot runtime, WorkbenchSearchDraft solo, string directory, Action<bool, string> check)
    {
        const string seed = "N9U3WB5KM4ZX"; // Existing B-SourceCapsules-positive fixture.
        var neutral = SeedLibraryContextCapture.ForSearch(solo with { Query = SearchQuery.Empty }, runtime);
        controller.OpenPredictionContext(seed, neutral);
        var request = page.LastRequest!;
        var document = page.LastDocument!;
        var bones = document.Sections.SelectMany(s => s.NeowChoices).Single(c => c.RelicKey == BaseGameModelKeys.Relics.NeowsBones);
        var ordered = bones.BonesOutcome!.OriginalRoutes.Last();
        var query = solo.Query with { OpeningRoute = new(bones.RelicKey), OpeningRouteRelicRequirement =
            new(bones.RelicKey, ordered.AcquisitionOrder, BonesRouteOrderMode.ExactOrder), StructuredOpeningEffects = [] };
        var store = new SeedLibraryStore(directory, []);
        string originalQuery = JsonSerializer.Serialize(query);
        Godot.Label Notice() => (Godot.Label)page.FindChild("UnverifiedSearchOrigin", true, false);
        bool OriginRowHidden() => !Notice().Visible && string.IsNullOrEmpty(Notice().Text);
        void AssertOpening(string route, int? choice, string where) => check(
            page.PreferredOpeningRouteId == route && page.PreferredOpeningChoiceSlotIndex == choice, where);
        foreach (var disposition in new[] { SearchDisposition.NotEvaluatedByPolicy, SearchDisposition.Unknown })
        {
            var draft = solo with { Query = query };
            var context = SeedLibraryContextCapture.ForSearch(draft, runtime);
            check(context.Players[0].Selection is null && context.Players[0].OpeningPremise.OpeningRouteRelicRequirement!.OrderMode == BonesRouteOrderMode.ExactOrder &&
                context.Players[0].OpeningPremise.StandardMaps.Count == 0 && JsonSerializer.Serialize(draft.Query) == originalQuery,
                "Real ForSearch retains authored solo order without fabricating a selection or changing the original query");
            var candidate = new SearchCandidate(seed, runtime.Profile.ProfileId, solo.Character, solo.Ascension, "origin-fixture",
                request, null, request.Authority, [], [], []) { ValidationDisposition = disposition,
                ValidationIssue = "origin-cause:" + disposition, IsUnfiltered = disposition == SearchDisposition.NotEvaluatedByPolicy };
            var association = new SeedQueryAssociation("original-query", SeedQueryAssociationKind.UnverifiedFromSearch, "original-environment", "record-" + disposition);
            controller.ClearSeedLibraryOverrides();
            controller.ImportSearchOpening(candidate, draft, context, association);
            page.SetPredictorContext(solo.Character, solo.Ascension, 1, 0, solo.AncientPremises, "", null, "", false);
            page.SetSeedText(seed, commit: true);
            controller.AnalyzeCurrentDraft();
            var expected = AnalysisPageController.ResolveSearchOpening(page.LastDocument!, query);
            check(expected.Available, "Native fixed Bones acquisition order is available");
            AssertOpening(expected.Route, expected.ChoiceSlot, "Direct unverified navigation uses the authored Bones order");
            var captured = controller.CaptureSeedLibraryContext();
            var origin = controller.CaptureResultOrigin(captured);
            check(origin.Association == association && origin.Provenance is { AppliesToCurrentContext: true } &&
                origin.Provenance.ValidationDisposition == disposition && origin.Provenance.ValidationIssue == candidate.ValidationIssue &&
                origin.Provenance.SourceEnvironmentFingerprint == association.EnvironmentFingerprint &&
                captured.Players[0].Selection?.Explicit == false && candidate.Witnesses.Count == 0,
                "Search to Analysis capture retains the actual source and keeps authored selection separate from witness evidence");
            var favorite = store.Save(seed, captured, "via Analysis", association: origin.Association, resultProvenance: origin.Provenance);
            var reopened = new SeedLibraryStore(directory, []).GetAll().Single(e => e.Id == favorite.Id);
            controller.OpenSeedLibraryEntry(reopened);
            AssertOpening(expected.Route, expected.ChoiceSlot, "Analysis favorite reopening restores the same Bones order");
            check(controller.CaptureResultOrigin(controller.CaptureSeedLibraryContext()).Provenance?.ValidationDisposition == disposition &&
                OriginRowHidden(),
                "Favorite reopening keeps the original disposition while the removed Analysis origin row stays hidden");
            var persisted = new PersistedSearchResult { Seed = seed, Context = context, SourceAssociation = association,
                ValidationDisposition = disposition, ValidationIssue = candidate.ValidationIssue, IsUnfiltered = candidate.IsUnfiltered };
            controller.OpenSavedSearchResult(persisted);
            AssertOpening(expected.Route, expected.ChoiceSlot, "Saved-history reopening resolves the same authored order from ForSearch");
            var historyOrigin = controller.CaptureResultOrigin(controller.CaptureSeedLibraryContext());
            check(historyOrigin.Association == association && historyOrigin.Provenance?.SearchRecordId == association.SearchRecordId,
                "Saved history transports its actual record rather than the active Search record");
            page.SetSeedText("000000000000", commit: true);
            controller.AnalyzeCurrentDraft();
            var altered = controller.CaptureResultOrigin(controller.CaptureSeedLibraryContext());
            check(altered.Association is null && altered.Provenance is { AppliesToCurrentContext: false, SourceSeed: seed } &&
                altered.Provenance.ValidationDisposition == disposition,
                "Changing the seed retains history without associating the new prediction with the original query evidence");
            controller.OpenSavedSearchResult(persisted);
            page.SetPredictorContext(solo.Character, solo.Ascension == 0 ? 1 : solo.Ascension - 1, 1, 0, solo.AncientPremises, "", null, "", false);
            controller.AnalyzeCurrentDraft();
            altered = controller.CaptureResultOrigin(controller.CaptureSeedLibraryContext());
            check(altered.Association is null && altered.Provenance?.AppliesToCurrentContext == false && OriginRowHidden(),
                "Changing configuration separates historical validation from the current prediction");

            var missingDraft = draft with { Query = query with { OpeningRouteRelicRequirement = new(bones.RelicKey,
                [BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.LeafyPoultice], BonesRouteOrderMode.ExactOrder) } };
            var missingContext = SeedLibraryContextCapture.ForSearch(missingDraft, runtime);
            controller.ClearSeedLibraryOverrides();
            controller.ImportSearchOpening(candidate, missingDraft, missingContext, association);
            page.SetPredictorContext(solo.Character, solo.Ascension, 1, 0, solo.AncientPremises, "", null, "", false);
            page.SetSeedText(seed, commit: true); controller.AnalyzeCurrentDraft();
            check(!AnalysisPageController.ResolveSearchOpening(page.LastDocument!, missingDraft.Query).Available && OriginRowHidden(),
                "Missing authored Bones order remains unavailable while its Analysis origin row stays hidden");
            var missingCaptured = controller.CaptureSeedLibraryContext();
            var missingOrigin = controller.CaptureResultOrigin(missingCaptured);
            var missingFavorite = store.Save(seed, missingCaptured, "missing authored order", association: missingOrigin.Association, resultProvenance: missingOrigin.Provenance);
            controller.OpenSeedLibraryEntry(new SeedLibraryStore(directory, []).GetAll().Single(e => e.Id == missingFavorite.Id));
            check(OriginRowHidden() && controller.CaptureSeedLibraryContext().Players[0].OpeningPremise.OpeningRouteRelicRequirement!
                .RequiredRelicKeys.SequenceEqual(missingDraft.Query.OpeningRouteRelicRequirement!.RequiredRelicKeys),
                "Favorite reopening retains the missing authored requirement without an Analysis origin row");
            controller.OpenSavedSearchResult(new PersistedSearchResult { Seed = seed, Context = missingContext, SourceAssociation = association,
                ValidationDisposition = disposition, ValidationIssue = candidate.ValidationIssue });
            check(OriginRowHidden() && !AnalysisPageController.ResolveSearchOpening(page.LastDocument!, missingDraft.Query).Available,
                "History reopening preserves the unavailable authored opening without showing the removed origin row");
        }
        controller.OpenPredictionContext(seed, neutral);
        check(controller.CaptureResultOrigin(controller.CaptureSeedLibraryContext()).Provenance is null &&
            controller.CaptureSeedLibraryContext().Players[0].OpeningPremise.OpeningRoute is null,
            "Old neutral context has no invented opening premise or search source");
    }

    private static void CheckIncompleteInformationPresentation(AnalysisPage page, SeedPredictionRequest request,
        SeedPredictionDocument document, Action<bool, string> check)
    {
        var unknown = new SeedPredictionDocument { Context = document.Context, PredictorId = document.PredictorId,
            OriginalSeed = document.OriginalSeed, CanonicalSeed = document.CanonicalSeed, Warnings = document.Warnings,
            Diagnostics = document.Diagnostics, Party = document.Party, OverallStatus = SeedPredictionOverallStatus.CompletedWithWarnings,
            Sections = document.Sections.Select(section => section.Kind switch
            {
                PredictionSectionKind.NeowIdentity => section with { NeowChoices = [], DomainStatus = Core.World.SeedDomainEvaluationStatus.Unknown, IssueCode = "Party.NeowAuthorityIncomplete" },
                PredictionSectionKind.BossIdentity => section with { Bosses = [], DomainStatus = Core.World.SeedDomainEvaluationStatus.Unsupported, IssueCode = "Party.BossModelUnsupported" },
                _ => section
            }).ToArray() };
        var text = JsonUiTextProvider.CreateUi13("zh");
        var view = Presentation.Ui1.SeedAnalysisPresentationBuilder.Build(unknown, text, RuntimeGameContentNameResolver.Create("zh"), false);
        check(view.NeowChoices.Count == 0 && view.NeowIdentityStatus == Core.World.SeedDomainEvaluationStatus.Unknown &&
            view.NeowIdentityIssueCode == "Party.NeowAuthorityIncomplete" && view.BossDomain.Status == Core.World.SeedDomainEvaluationStatus.Unsupported &&
            view.BossDomain.IssueCode == "Party.BossModelUnsupported", "Empty incomplete offers/bosses retain actual status and cause in presentation");
        var preferred = page.PreferredOpeningRouteId;
        var choice = page.PreferredOpeningChoiceSlotIndex;
        page.ShowDocument(request, unknown);
        check(page.LastDocument == unknown && page.PreferredOpeningRouteId == preferred && page.PreferredOpeningChoiceSlotIndex == choice,
            "An unknown opening projection cannot erase the player's frozen choice/route");
        page.ShowDocument(request, document);
    }

    private static void CheckPredictorPartyConfiguration(AnalysisPage page, AnalysisPageController controller,
        SearchWorkspace persistence, string directory, ModRuntimeSnapshot runtime, WorkbenchSearchDraft party, Action<bool, string> check)
    {
        object? Call(object target, string method, params object[] args) => target.GetType()
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, args);
        const string seed = "SKDTNY1YQEPG";
        var draft = party with { Query = SearchQuery.Empty with { Players = party.Players.Select(p =>
            new PlayerOfferQuery(p.Slot, ModelKeySetFilter.Empty)).ToArray() } };
        controller.ClearSeedLibraryOverrides();
        controller.SetPartyDraft(draft, seed);
        var first = page.LastDocument!.Sections.SelectMany(s => s.NeowChoices)
            .Single(c => c.SlotIndex == page.PreferredOpeningChoiceSlotIndex);
        var before = first.OpeningRewardContinuations!.Routes.Single(r => r.Route.RouteId == page.PreferredOpeningRouteId);
        check(before.NicheState is { CallCount: > 0 }, "Fixed default opening consumes shared RNG");
        Call(controller, "SelectPartyPlayer", 1);
        void CheckPrefix(Core.Rewards.OpeningRewardContinuation prefix)
        {
            var request = page.LastRequest!;
            var expected = PartyNeowProjection.ProjectChoices(request, runtime.Profile.ComputeRootSeed(seed),
                prefix.NicheState!.Restore(), prefix.CombatPotionGenerationState!.Restore(), true,
                PartyInitialQuery.CapsuleEffectPremise(SearchQuery.Empty));
            var actual = request.PartyOpeningChoices!;
            check(actual.Select(c => c.RelicKey).SequenceEqual(expected.Select(c => c.RelicKey)) &&
                actual.Zip(expected).All(pair => JsonSerializer.Serialize(pair.First.EffectGroups) == JsonSerializer.Serialize(pair.Second.EffectGroups)),
                "P2 uses the complete P1 shared prefix rather than fresh streams");
        }
        CheckPrefix(before);
        Call(controller, "SelectPartyPlayer", 0);
        var alternative = page.LastDocument!.Sections.SelectMany(s => s.NeowChoices)
            .First(c => c.SlotIndex != first.SlotIndex && c.OpeningRewardContinuations?.Routes.Count > 0);
        var next = alternative.OpeningRewardContinuations!.Routes[0];
        Call(page, "ApplyOpeningSelection", alternative.RelicKey, next.Route.RouteId);
        Call(controller, "SelectPartyPlayer", 1);
        CheckPrefix(next);
        var savedChoice = controller.CaptureSeedLibraryContext().Players[0].Selection;
        Call(controller, "SelectPartyMode", false);
        check(!persistence.PredictorContext.PartyActive && persistence.PredictorContext.Party is { SelectedSlot: 0 } remembered &&
            remembered.Players[0].Selection == savedChoice,
            "Inactive party memory retains its roster and explicit choices with selected slot zero");
        Call(controller, "SelectPartyMode", true);
        check(controller.CaptureSeedLibraryContext().Players[0].Selection == savedChoice,
            "Solo/party toggle retains each player's selected opening");
        persistence.FlushPredictorContext();
        var stored = new SearchWorkspace(directory, false);
        check(stored.PredictorContext.PartyActive && stored.PredictorContext.Party?.Players.Count == 4 &&
            stored.PredictorContext.PartySeed == seed && stored.PredictorContext.Party.Players[0].Selection == savedChoice,
            "Predictor persistence retains full roster and selected N separately from Search");
        Call(controller, "SelectPartyMode", false);
        controller.RestorePartyConfiguration(stored.PredictorContext, true);
        controller.AnalyzeCurrentDraft();
        check(page.LastRequest!.PlayersCount == 4 && controller.CaptureSeedLibraryContext().Players[0].Selection == savedChoice,
            "Persisted party restores with its opening choices");
        var older = new PredictorContextDocument { Party = stored.PredictorContext.Party! with { GameVersion = "older-runtime" },
            PartySeed = seed, PartyActive = true };
        controller.RestorePartyConfiguration(older, true);
        var restored = (WorkbenchSearchDraft)controller.GetType().GetField("_partyDraft",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
        var selections = (System.Collections.IDictionary)controller.GetType().GetField("_partySelections",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(controller)!;
        check(restored.Players.Select(p => p.Character).SequenceEqual(party.Players.Select(p => p.Character)) &&
            restored.Players.Select(p => p.UnlockSource).SequenceEqual(party.Players.Select(p => p.UnlockSource)) && selections.Count == 0,
            "Older predictor memory restores its roster and unlock provenance while discarding stale opening selections");
        controller.RestorePartyConfiguration(stored.PredictorContext, true);
        controller.AnalyzeCurrentDraft();
        ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root.GetChildren().Single(n => n.GetType().Name == "Runner").AddChild(page);
        check(page.IsInsideTree(), "Party dialog checks run in the native scene tree");
        Call(controller, "OpenPartyConfiguration");
        var popup = (Godot.PopupPanel)page.GetChildren().Single(n => n is Godot.PopupPanel);
        var count = (Godot.OptionButton)popup.FindChild("PartyCount", true, false);
        count.Select(0); count.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, 0L);
        page.ClosePartyConfiguration();
        check(controller.CaptureSeedLibraryContext().Players.Count == 4, "Cancel leaves roster unchanged");
        Call(controller, "OpenPartyConfiguration");
        popup = page.GetChildren().OfType<Godot.PopupPanel>().Last();
        ((Godot.Button)popup.FindChild("ReadPartyLobby", true, false)).EmitSignal(Godot.Button.SignalName.Pressed);
        count = (Godot.OptionButton)popup.FindChild("PartyCount", true, false);
        check(count.GetSelectedId() == 4, "Absent lobby does not erase the staged roster");
        count.Select(0); count.EmitSignal(Godot.OptionButton.SignalName.ItemSelected, 0L);
        ((Godot.Button)popup.FindChild("PartyFullUnlocks", true, false)).EmitSignal(Godot.Button.SignalName.Pressed);
        ((Godot.Button)popup.FindChild("ApplyPartyConfig", true, false)).EmitSignal(Godot.Button.SignalName.Pressed);
        check(page.LastRequest!.PlayersCount == 2 && controller.CaptureSeedLibraryContext().Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"),
            "Applying offline config changes player count and full-unlock authority");
    }
}
