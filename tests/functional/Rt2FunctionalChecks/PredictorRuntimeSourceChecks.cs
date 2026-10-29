using System.Collections.Immutable;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Runtime;

internal static class PredictorRuntimeSourceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static ModelKey K(string entry) => new("RELIC", entry);
    private static void Select(PredictorRun run, PredictorInput input) => Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Input: " + input);
    internal static int Run(bool routesOnly = false, string? domain = null)
    {
        if (Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") != "1")
            throw new InvalidOperationException("Use existing AuthoredEventChecks native runner");
        TestMode.IsOn = true;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null, MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        var root = new DirectoryInfo(Environment.CurrentDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "RolltheSpire2.csproj"))) root = root.Parent;
        var authority = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(root!.FullName,
            "tools/ProductionSearchHost/Corpus/v1/context.json")).Context.Authority;
        var discoveredActs = authority.WorldAuthority!.Beta109Generation!.ActSelectionGroups
            .SelectMany(group => group.Candidates).Where(candidate => candidate.DiscoveredInSingleplayer)
            .Select(candidate => candidate.ActKey).Distinct().ToImmutableArray();
        var context = new PredictorContext("000000000000", "0.111.0", authority.Character.CharacterKey, authority.Ascension,
            authority.CatalogFingerprint, authority.UnlockSnapshotFingerprint, MegaCrit.Sts2.Core.Timeline.EpochModel.AllEpochIds.ToImmutableArray(),
            ModelDb.AllEncounters.Select(e => new ModelKey(e.Id.Category, e.Id.Entry)).ToImmutableArray(), discoveredActs, 1) { AccountWongoPoints = 0 };
        // Route initialization must stay separate from a live Crystal minigame
        // capture. The existing ProductionSearchHost Crystal checks remain its
        // local-mode suite; these checks always begin from the seed boundary.
        Check(context.Crystal is null, "Seed route fixture cannot carry local Crystal capture");
        if (domain is not null)
        {
            switch (domain)
            {
                case "initialization": FiveCharacterInitialization(context); break;
                case "live-capture": PredictorLiveCaptureChecks.Run(PredictorSeedCapture.CreateRun(context, authority)); break;
                case "feature-preferences": PredictorLiveCaptureChecks.Run(PredictorSeedCapture.CreateRun(context, authority), featurePreferencesOnly: true); break;
                case "live-policy": PredictorRouteLivePolicyChecks.Run(PredictorSeedCapture.CreateRun(context, authority)); break;
                case "repy": PredictorDrawingEventChecks.RepyReadsOnlyKeyMembership(PredictorSeedCapture.CreateRun(context, authority)); break;
                case "tablet": PredictorRouteEventChecks.TabletHealthScope(PredictorSeedCapture.CreateRun(context, authority)); break;
                case "drawing":
                    PredictorRouteKnowledgeChecks.RunLiveRestDefaults(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorDrawingEventChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorDrawRewardContinuationChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "drawing-boundaries":
                    var drawingBoundary = PredictorSeedCapture.CreateRun(context, authority);
                    PredictorRouteLivePolicyChecks.RunPandoraBoundaries(drawingBoundary);
                    PredictorRouteKnowledgeChecks.RunTargetBoundaries(drawingBoundary);
                    PredictorDrawingEventChecks.RunFaultBoundaries(drawingBoundary);
                    break;
                case "drawing-overblocks":
                    var overblockBoundary = PredictorSeedCapture.CreateRun(context, authority);
                    PredictorDrawingEventChecks.RunOverblockBoundaries(overblockBoundary);
                    PredictorRouteKnowledgeChecks.RunHatchOverblockBoundaries(overblockBoundary);
                    PredictorRouteLivePolicyChecks.RunObtainOverblockBoundaries(overblockBoundary);
                    break;
                case "card-operations":
                    var cardOperations = PredictorSeedCapture.CreateRun(context, authority);
                    PredictorCardRewardNicheKnowledgeChecks.Run(cardOperations);
                    PredictorDrawRewardContinuationChecks.RunCardOperations(cardOperations);
                    PredictorRouteEventChecks.RunPartialCardOperations(cardOperations);
                    break;
                case "dependency-boundaries":
                    var dependencyBoundary = PredictorSeedCapture.CreateRun(context, authority);
                    PredictorDrawRewardContinuationChecks.RunDependencyBoundaries(dependencyBoundary);
                    PredictorRouteLivePolicyChecks.RunDependencyScopeBoundaries(dependencyBoundary);
                    PredictorRouteShopChecks.RunDependencyScopeBoundaries(dependencyBoundary);
                    PredictorRouteEventChecks.RunDefaultDependencyFixes(dependencyBoundary);
                    break;
                case "combat-facts":
                    PredictorCombatRewardFactsChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorCombatKnowledgeChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorRouteKnowledgeChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorEncounterEntryChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "shop": PredictorShopChecks.Run(context, authority); break;
                case "operation":
                    PredictorOperationChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorOperationSceneChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorOperationEventChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorOperationMendChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorOperationCaptureChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "neow-operation":
                    NeowOperationPreviewChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "route-choices":
                    PredictorRouteShopChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorShopReadRecordingChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorRouteAncientChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorAncientTraceChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "route-events":
                    PredictorEventOccurrenceChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorRouteEventChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorEventTraceChecks.RunNative(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "route-journey":
                    PredictorRestTreasureReadRecordingChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorRouteTreasureChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorRouteEditChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    PredictorRouteJourneyChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
                    break;
                case "obtain": PredictorObtainChecks.Run(PredictorSeedCapture.CreateRun(context, authority)); break;
                case "map": PredictorMapChecks.Run(PredictorSeedCapture.CreateRun(context, authority)); break;
                case "events": PredictorEventChecks.RunNative(PredictorSeedCapture.CreateRun(context with { Seed = "00000000000M" }, authority)); break;
                default: throw new ArgumentException("Unknown Predictor source check domain: " + domain, nameof(domain));
            }
            Console.WriteLine("RUNTIME_SOURCE_DOMAIN_PASS " + domain + ";native=true;gpu=false;localCrystal=false");
            return 0;
        }
        var found = new HashSet<string>();
        if (routesOnly) return RouteContinuations(context, authority);
        FiveCharacterInitialization(context);
        foreach (int ordinal in Enumerable.Range(0, 300))
        {
            string seed = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(RolltheSpire2.Compatibility.Beta111Profile.Instance, (ulong)ordinal);
            var run = PredictorSeedCapture.CreateRun(context with { Seed = seed }, authority);
            var option = run.Working.OpeningOptions.FirstOrDefault(k => k.Entry is "GOLDEN_PEARL" or "LOST_COFFER" or "ARCANE_SCROLL" or "PHIAL_HOLSTER" or "SMALL_CAPSULE" && !found.Contains(k.Entry));
            if (!option.IsValid || found.Contains(option.Entry)) continue;
            found.Add(option.Entry);
            var initial = run.Working;
            Check(initial.Deck.Length == ModelDb.Character<Ironclad>().StartingDeck.Count() + 1 && initial.PotionSlots.Length == 2, "A10 seed inventory");
            Check(initial.Acts.Length == 3 && initial.Acts[2].Bosses.Length == 2, "World continuation");
            run.EnterOpening(); Select(run, new ChooseOpeningRelic(option));
            if (run.Phase != PredictorPhase.Complete) Select(run, new LeaveRewards());
            Check(run.Working.OpeningCompleted && run.Working.Relics.Last().Key == option, "Opening did not retain obtained identity");
            Check(option.Entry != "GOLDEN_PEARL" || run.Working.GoldBudget == initial.GoldBudget + 150, "GoldenPearl actual budget");
            Check(option.Entry != "ARCANE_SCROLL" || run.Working.Deck.Length == initial.Deck.Length + 1, "Arcane immediate card");
            Check(option.Entry != "PHIAL_HOLSTER" || run.Working.PotionSlots.Count(p => p != null) == 2, "Phial direct batch");
            int cardsBefore = run.Working.Deck.Length;
            for (int battle = 0; battle < 3; battle++)
            {
                run.EnterCombatRewards(PredictorCombatKind.Monster, 1, battle + 2);
                var cards = run.Rewards.Single(r => r.Kind == PredictorRewardKind.Card);
                Select(run, new EnterCardSelection(cards.Id));
                Select(run, new TakeReward(cards.Id, cards.Cards[0].Id));
                Select(run, new LeaveRewards());
                run = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint);
            }
            Check(run.Working.Deck.Length == cardsBefore + 3 && run.Working.CombatCount == 3 && run.Working.Acts[0].NormalCursor == 3, "Cross-node inventory/queue/checkpoint");
            run.Rewind(1);
            Check(run.Working.CombatCount == 0 && run.Working.OpeningCompleted, "Rewind first combat preserved Opening");
            Console.WriteLine($"RUNTIME_SOURCE_CHAIN {seed} {option.Entry} -> 3 Monster rewards -> save/reload each -> rewind PASS");
            if (found.Count == 5) break;
        }
        Check(found.Count == 5, "Did not cover each implemented real Opening source");
        NativeRewardPipeline(context, authority);
        PredictorPressureChecks.Run(context, authority);
        ModifierOpeningChecks(context, authority);
        CombatPremiseChecks(context, authority);
        ContinuedNodes(context, authority);
        NativeTransformationAndPotions(context, authority);
        PredictorShopChecks.Run(context, authority);
        PredictorObtainChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
        PredictorAncientChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
        PredictorCombatRewardFactsChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
        PredictorEncounterEntryChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
        PredictorMapChecks.Run(PredictorSeedCapture.CreateRun(context, authority));
        PredictorEventChecks.RunNative(PredictorSeedCapture.CreateRun(context with { Seed = "00000000000M" }, authority));
        var remainingRelics = new List<string>();
        foreach (var relic in ModelDb.AllRelics)
        {
            try { PredictorSettlementEffects.RequireImplementedRelic(new(relic.Id.Category, relic.Id.Entry)); }
            catch (NotImplementedException) { remainingRelics.Add(relic.Id.Entry); }
        }
        Console.WriteLine("RUNTIME_RELIC_IMPLEMENTATION_REMAINDER " + string.Join(',', remainingRelics.Order()));
        Console.WriteLine("RUNTIME_SOURCE_PASS openingSources=7 nativeRewardShapes=10 plus transform/potion/rest transitions; no whole-route/UI claim");
        return 0;
    }

    private static void FiveCharacterInitialization(PredictorContext template)
    {
        var declaredUnlocks = new UnlockState(template.RevealedEpochs,
            template.SeenEncounters.Select(k => new ModelId(k.Category, k.Entry)), template.NumberOfRuns);
        RequestSupportCaptureChecks(template);
        foreach (var key in new[]
        {
            BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
            BaseGameModelKeys.Characters.Regent, BaseGameModelKeys.Characters.Necrobinder,
            BaseGameModelKeys.Characters.Defect
        })
        {
            var identity = CharacterIdentity.FromKey(key);
            bool priorTestMode = TestMode.IsOn;
            RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority;
            try
            {
                // A real seed run uses the ordinary world generator. Native
                // host test mode changes boss/room setup and is not its input.
                TestMode.IsOn = false;
                authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                    RolltheSpire2.Compatibility.Beta111Profile.Instance, template.Seed, identity,
                    template.Ascension, template.GameVersion,
                    predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
                    predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
                    explicitUnlockState: declaredUnlocks,
                    explicitDiscoveredActs: template.DiscoveredActs);
            }
            finally { TestMode.IsOn = priorTestMode; }
            Check(authority.EffectAuthority?.CharacterRewardPool != null &&
                authority.WorldAuthority?.Beta109Generation != null,
                "Per-character runtime authority incomplete: " + key);
            var context = template with { Character = key, CatalogFingerprint = authority.CatalogFingerprint,
                UnlockFingerprint = authority.UnlockSnapshotFingerprint };
            PredictorRun run;
            try
            {
                TestMode.IsOn = false;
                run = PredictorSeedCapture.CreateRun(context);
            }
            finally { TestMode.IsOn = priorTestMode; }
            if (key == BaseGameModelKeys.Characters.Ironclad)
                PredictionEntryContext(context, authority, declaredUnlocks);
            if (key == BaseGameModelKeys.Characters.Ironclad && template.DiscoveredActs.Length > 0)
            {
                try
                {
                    PredictorSeedCapture.CreateRun(context with { DiscoveredActs = [] }, authority);
                    throw new InvalidOperationException("Mismatched discovered acts accepted");
                }
                catch (InvalidOperationException ex) when (ex.Message == "PredictorDiscoveredActsAuthorityMismatch") { }
            }
            var character = ModelDb.GetById<CharacterModel>(new ModelId(key.Category, key.Entry));
            Check(run.Working.Deck.Length == character.StartingDeck.Count() + 1 &&
                run.Working.Relics.Select(r => r.Key).SequenceEqual(character.StartingRelics.Select(r =>
                    new ModelKey(r.Id.Category, r.Id.Entry))) && run.Working.OpeningOptions.Length == 3,
                "Per-character seed inventory/Opening mismatch: " + key);
            run.EnterOpening();
            for (int step = 0; step < 40 && run.Phase != PredictorPhase.Complete; step++)
                Select(run, RouteInput(run, new HashSet<string>(), new HashSet<string>()));
            Check(run.Phase == PredictorPhase.Complete && run.Working.OpeningCompleted,
                "Per-character Opening did not settle: " + key);
            Console.WriteLine($"RUNTIME_CHARACTER_START {key.Entry} starter={character.StartingRelics.First().Id.Entry} opening={run.Working.OpeningOptions[0].Entry} PASS");
        }
    }

    private static void RequestSupportCaptureChecks(PredictorContext template)
    {
        var identity = CharacterIdentity.FromKey(BaseGameModelKeys.Characters.Ironclad);
        RuntimeContextAuthoritySnapshot Capture(UnlockState unlocks, IReadOnlyList<ModelKey> acts,
            RolltheSpire2.Compatibility.IRuntimeProfile? profile = null) => RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                profile ?? RolltheSpire2.Compatibility.Beta111Profile.Instance, template.Seed, identity,
                template.Ascension, template.GameVersion,
                predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer,
                predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest,
                explicitUnlockState: unlocks, explicitDiscoveredActs: acts);
        var full = Capture(UnlockState.all, template.DiscoveredActs);
        Check(full.SupportEvidence.Unlock.Source == RequestFactSource.ExplicitRequest && full.SupportEvidence.Unlock.Complete &&
            Vanilla111EpochBaseline.EpochIds.All(full.SupportEvidence.Unlock.RevealedEpochs.Contains),
            "Explicit all unlock Epoch facts and source were not captured from the actual request");
        Check(full.SupportEvidence.Discovery.Source == RequestFactSource.ExplicitRequest &&
            full.SupportEvidence.Discovery.Acts.SequenceEqual(template.DiscoveredActs),
            "Explicit discovery must retain the exact supplied snapshot");
        var partialUnlocks = new UnlockState(Vanilla111EpochBaseline.EpochIds.Remove("EVENT3_EPOCH"), [], 1);
        var partial = Capture(partialUnlocks, []);
        Check(partial.SupportEvidence.Unlock.Source == RequestFactSource.ExplicitRequest &&
            !partial.SupportEvidence.Unlock.RevealedEpochs.Contains("EVENT3_EPOCH") &&
            partial.SupportScope.Reasons.Contains("RequestEpochNotUnlocked:EVENT3_EPOCH") &&
            partial.AllCharacterCardPoolsUnlocked == true,
            "Full character pools cannot substitute actual request Epoch unlock state");
        Check(partial.SupportEvidence.Discovery.Source == RequestFactSource.ExplicitRequest &&
            partial.SupportEvidence.Discovery.Acts.IsEmpty && partial.SupportEvidence.Unlock.SeenEncounters.IsEmpty,
            "Explicit empty discovery and seen encounters cannot silently substitute account/all state");
        var undiscovered = Capture(new UnlockState(Vanilla111EpochBaseline.EpochIds, [], 1), []);
        Check(undiscovered.SupportScope.State == full.SupportScope.State &&
            !undiscovered.SupportScope.Reasons.Any(r => r.Contains("Discovery", StringComparison.Ordinal)),
            "Partial discovery/seen encounters do not alter full Epoch baseline membership");
        var beta110 = Capture(UnlockState.all, template.DiscoveredActs, RolltheSpire2.Compatibility.Beta110Profile.Instance);
        Check(beta110.EffectAuthority?.CapturedProfileId == RolltheSpire2.Compatibility.RuntimeProfileId.Beta110,
            "Explicit unlock capture must retain requested Beta110 donor profile rather than hardcoding Beta111");
        Console.WriteLine("RUNTIME_REQUEST_SUPPORT_CAPTURE PASS explicitAllEpochs=true partialEpochs=true discoveryIndependent=true profileDonor=true");
    }

    private static void PredictionEntryContext(PredictorContext context,
        RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority, UnlockState declaredUnlocks)
    {
        var serialized = declaredUnlocks.ToSerializable();
        Check(RolltheSpire2.Core.Prediction.SeedPredictionRequest.TryCreate(context.Seed,
            authority.Character, context.Ascension, 1, 0, authority,
            RolltheSpire2.Core.World.AncientOptionConditionProfile.BroadDefault, false,
            out var request, out var issue), "Prediction entry request: " + issue);
        var save = MegaCrit.Sts2.Core.Saves.SaveManager.Instance;
        var oldProgress = save.Progress;
        bool oldTestMode = TestMode.IsOn;
        try
        {
            save.Progress = new MegaCrit.Sts2.Core.Saves.ProgressState();
            var account = save.GenerateUnlockStateFromProgress().ToSerializable();
            Check(!account.UnlockedEpochs.SequenceEqual(serialized.UnlockedEpochs),
                "Prediction entry fixture needs different account and declared unlock facts");
            TestMode.IsOn = false;
            var run = PredictorSeedCapture.CreateFromPrediction(request!, serialized);
            Check(run.Context.RevealedEpochs.SequenceEqual(serialized.UnlockedEpochs) &&
                run.Context.SeenEncounters.SequenceEqual(serialized.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry))) &&
                run.Context.NumberOfRuns == serialized.NumberOfRuns && run.Context.Crystal is null,
                "Prediction entry replaced declared unlock facts with account defaults or a Crystal local context");

            var partyAuthority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                RolltheSpire2.Compatibility.Beta111Profile.Instance, context.Seed, authority.Character,
                context.Ascension, context.GameVersion, 2, 1,
                predictionGameMode: RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer,
                predictionGameModeAuthority: RolltheSpire2.Core.Authority.PredictionGameModeAuthority.ExplicitRequest,
                explicitUnlockState: declaredUnlocks, explicitDiscoveredActs: context.DiscoveredActs);
            Check(RolltheSpire2.Core.Prediction.SeedPredictionRequest.TryCreate(context.Seed,
                partyAuthority.Character, context.Ascension, 2, 1, partyAuthority,
                RolltheSpire2.Core.World.AncientOptionConditionProfile.BroadDefault, false,
                out var partyRequest, out issue), "Party prediction entry request: " + issue);
            try
            {
                PredictorSeedCapture.CreateFromPrediction(partyRequest!, serialized);
                throw new InvalidOperationException("Prediction entry silently projected multiplayer to a solo route");
            }
            catch (InvalidOperationException ex) when (ex.Message == "PredictorRouteRequiresSingleplayer") { }
            Console.WriteLine("RUNTIME_PREDICTION_ENTRY_PASS declared unlocks differ from account; current request retained; full party route rejected; local Crystal checked separately");
        }
        finally { save.Progress = oldProgress; TestMode.IsOn = oldTestMode; }
    }

    private static int RouteContinuations(PredictorContext context, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        var gaps = new HashSet<string>();
        for (int ordinal = 0; ordinal < 64; ordinal++)
        {
            string seed = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(RolltheSpire2.Compatibility.Beta111Profile.Instance, (ulong)ordinal);
            var run = PredictorSeedCapture.CreateRun(context with { Seed = seed }, authority);
            var routeMetrics = new[] { RolltheSpire2.Core.Prediction.Maps.MapPointType.Shop,
                RolltheSpire2.Core.Prediction.Maps.MapPointType.RestSite,
                RolltheSpire2.Core.Prediction.Maps.MapPointType.Elite };
            foreach (int act in new[] { 1, 2, 3 })
                run.SetRoute(act, new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(run.RouteMap(act),
                    routeMetrics[act - 1]).At(0).ToImmutableArray());
            bool restored = false, rewound = false;
            var shopBuys = new HashSet<string>();
            var rested = new HashSet<string>();
            try
            {
                for (int step = 0; step < 500 && run.Working.Outcome == null; step++)
                {
                    if (run.Phase == PredictorPhase.Complete)
                    {
                        if (!restored && run.Working.Visits.Length >= 5)
                        {
                            try { run = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint); }
                            catch (ArgumentException ex)
                            {
                                throw new InvalidOperationException($"Route reload invalid seed={seed} node={run.Working.Position} visits={run.Working.Visits.Length} maps={string.Join(';', run.Working.Maps.Select(m => $"{m.Act}:{m.Visited.Length}"))} routes={string.Join(';', run.Routes.Select(r => $"{r.Act}:{r.Points.Length}"))}", ex);
                            }
                            Check(run.Routes.Length == 3 && run.Working.Maps.Length == 1, "Route save/reload lost geometry or commitment");
                            restored = true;
                        }
                        if (!rewound && run.Working.Visits.Length >= 8)
                        {
                            int oldVisits = run.Working.Visits.Length;
                            run.Rewind(run.Checkpoints.Length - 2);
                            Check(run.Working.Visits.Length == oldVisits - 1, "Route rewind did not restore entry history");
                            rewound = true;
                        }
                        foreach (int act in run.Project().RouteActsNeedingReplan)
                        {
                            var map = run.RouteMap(act);
                            var visited = run.Working.Maps.FirstOrDefault(m => m.Act == act)?.Visited ?? [];
                            run.SetRoute(act, ReplanFromVisited(map, visited));
                            Console.WriteLine($"RUNTIME_ROUTE_REPLAN seed={seed} act={act} visited={visited.Length}");
                        }
                        run.AdvanceRoute();
                        continue;
                    }
                    PredictorInput input = RouteInput(run, shopBuys, rested);
                    Select(run, input);
                }
                if (run.Working.Outcome != PredictorRunOutcome.Victory)
                {
                    gaps.Add($"seed={seed} route ended {run.Working.Outcome?.ToString() ?? run.Phase.ToString()} at {run.Working.Position} after {run.Working.Visits.Length} visits");
                    continue;
                }
                Check(run.Working.Maps.Length == 3 && run.Working.Visits.Any(v => v.Act == 3 && v.Room == PredictorRoomKind.Boss) &&
                    restored && rewound, "Missing continuous route/cross-act/restore evidence");
                var requiredRooms = new[] { PredictorRoomKind.Shop, PredictorRoomKind.RestSite, PredictorRoomKind.Treasure,
                    PredictorRoomKind.Event, PredictorRoomKind.Ancient, PredictorRoomKind.Elite, PredictorRoomKind.Boss };
                var missingRooms = requiredRooms.Where(kind => !run.Working.Visits.Any(v => v.Room == kind)).ToArray();
                if (missingRooms.Length > 0)
                {
                    gaps.Add("seed=" + seed + " missing rooms=" + string.Join(',', missingRooms));
                    continue;
                }
                var final = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint);
                Check(final.Working.Outcome == PredictorRunOutcome.Victory &&
                    System.Text.Json.JsonSerializer.Serialize(final.Working.Visits) == System.Text.Json.JsonSerializer.Serialize(run.Working.Visits),
                    "Terminal route save mismatch");
                Console.WriteLine($"RUNTIME_ROUTE_CHAIN {seed} visits={run.Working.Visits.Length} combats={run.Working.CombatCount} rooms={string.Join(',', run.Working.Visits.Select(v => v.Room).Distinct())} Act1->Act3 save/reload+rewind PASS; Owner-controlled combat/health facts");
                foreach (var gap in gaps) Console.WriteLine("ROUTE_PROBE_CHOICE_GAP " + gap);
                return 0;
            }
            catch (NotImplementedException ex) { gaps.Add(ex.Message); }
        }
        throw new InvalidOperationException("No complete route yet: " + string.Join("; ", gaps));
    }

    private static ImmutableArray<RolltheSpire2.Core.Prediction.Maps.MapPosition> ReplanFromVisited(
        RolltheSpire2.Core.Prediction.Maps.MapPrediction map,
        ImmutableArray<RolltheSpire2.Core.Prediction.Maps.MapPosition> visited)
    {
        if (visited.IsEmpty) return new RolltheSpire2.Core.Prediction.Maps.MapRouteSet(map).At(0).ToImmutableArray();
        var byPosition = map.Points.ToDictionary(p => p.Position);
        var path = visited.ToList();
        bool Extend(RolltheSpire2.Core.Prediction.Maps.MapPosition position)
        {
            var point = byPosition[position];
            if (point.Type == RolltheSpire2.Core.Prediction.Maps.MapPointType.Boss) return true;
            foreach (var child in point.Children.OrderBy(p => p.Row).ThenBy(p => p.Column))
            {
                path.Add(child);
                if (Extend(child)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
        if (!Extend(visited[^1])) throw new InvalidOperationException("Route visited prefix has no Boss continuation");
        return path.ToImmutableArray();
    }

    private static PredictorInput RouteInput(PredictorRun run, HashSet<string> shopBuys, HashSet<string> rested)
    {
        var view = run.Project();
        string visit = $"{view.Position.Act}:{view.Position.Floor}:{view.Position.NodeId}";
        if (view.NeedsAbandonConfirmation) return new ConfirmTrialAbandon(false);
        if (view.Phase == PredictorPhase.CombatFacts)
        {
            // Owner-controlled scenario: the fight is won, no extra enemies or
            // potion use, no stolen cards, and no qualifying damage. Facts are
            // supplied explicitly at each battle boundary.
            int huntCount = view.DefaultCombatCardInstances.Count(id => run.Working.Card(id).Key.Entry == "THE_HUNT");
            var extraOrder = ImmutableArray.CreateBuilder<PredictorCombatExtraRewardRef>();
            if (view.NeedsGremlinGoldFacts && huntCount > 0)
            {
                for (int i = 0; i < huntCount; i++) extraOrder.Add(new(PredictorCombatExtraRewardSource.Hunt, i));
                extraOrder.Add(new(PredictorCombatExtraRewardSource.Heist, 0));
            }
            return new ResolveCombat(new PredictorCombatFacts
            {
                Potions = PredictorCombatPotionFacts.NoAdditional,
                EnemyCreations = view.RequiredEnemyCreationFacts.Select(s => new PredictorEnemyCreationFact(s, 0)).ToImmutableArray(),
                StolenCards = view.NeedsStolenCardFacts ? new([]) : null,
                FirstPlayerTurnOccurred = view.NeedsFirstPlayerTurnFact ? true : null,
                VictoryHealth = view.NeedsVictoryHealthFact ? new(80, 100) : null,
                GremlinGold = view.NeedsGremlinGoldFacts ? new(false, false, 0) : null,
                ExtraRewardOrder = extraOrder.ToImmutable()
            }, EventRanOutOfTime: view.NeedsEventTimeoutFact ? false : null);
        }
        if (view.Phase == PredictorPhase.Rewards)
        {
            var open = view.Rewards.FirstOrDefault(r => r.Reward.Status == PredictorRewardStatus.Open &&
                (r.Reward.Kind != PredictorRewardKind.Card || r.Reward.Cards.Length > 0) &&
                (r.Reward.Kind != PredictorRewardKind.Potion ||
                    view.Inventory.PotionSlots.Any(p => p == null) &&
                    !view.Inventory.Relics.Any(relic => !relic.Melted && relic.Key.Entry == "SOZU")) &&
                (r.Reward.Kind != PredictorRewardKind.RemoveCard || view.Inventory.Deck.Any(c => c.Removable)));
            if (open != null)
            {
                if (open.CanOpen) return new EnterCardSelection(open.Reward.Id);
                if (open.CanTake) return new TakeReward(open.Reward.Id,
                    open.Reward.Kind == PredictorRewardKind.Card ? open.Reward.Cards[0].Id : null);
            }
            if (view.CanLeave) return new LeaveRewards();
            throw new NotImplementedException("Route has mandatory reward without legal take: " + view.Position);
        }
        if (view.Phase == PredictorPhase.Shop)
        {
            if (!view.ShopRemovalCandidates.IsEmpty) return new ChooseShopRemovalCard(view.ShopRemovalCandidates[0]);
            if (!view.ShopInventoryOpen && !shopBuys.Contains(visit)) return new OpenShopInventory();
            if (view.ShopInventoryOpen && shopBuys.Add(visit))
            {
                var candidate = view.ShopOffers.Where(o => o.Stocked && o.Affordable && o.Selectable &&
                    o.Kind != PredictorShopEntryKind.CardRemoval)
                    .OrderBy(o => o.Kind == PredictorShopEntryKind.Relic ? 0 : 1).ThenBy(o => o.Cost).FirstOrDefault();
                if (candidate != null) return new BuyShopEntry(candidate.EntryId, view.ShopRevision);
            }
            if (view.ShopInventoryOpen) return new CloseShopInventory();
            return new LeaveShop();
        }
        if (view.Phase == PredictorPhase.RestSite)
        {
            if (rested.Add(visit))
            {
                var preferred = (rested.Count % 3) switch
                {
                    1 => PredictorRestAction.Heal,
                    2 => PredictorRestAction.Smith,
                    _ => PredictorRestAction.Dig
                };
                var priority = new[] { preferred, PredictorRestAction.Smith,
                    PredictorRestAction.Heal, PredictorRestAction.Cook, PredictorRestAction.Lift };
                var action = priority.FirstOrDefault(a => view.RestOffers.Any(o => o.Action == a && o.Enabled));
                return new SelectRestAction(action);
            }
            return new LeaveRestSite();
        }
        if (view.Phase == PredictorPhase.EventChoice)
        {
            if (view.NeedsHealthFact) return new ProvideEventHealthFact(new(80, 100));
            var option = view.EventChoices.FirstOrDefault(o => o.Enabled &&
                !o.Key.Equals("LEAVE", StringComparison.OrdinalIgnoreCase)) ?? view.EventChoices.FirstOrDefault(o => o.Enabled);
            return option == null ? throw new NotImplementedException("Route event has no enabled choice: " + view.Position)
                : new ChooseEventOption(option.Key);
        }
        if (view.Phase == PredictorPhase.AncientChoice)
        {
            var offer = view.AncientChoices.FirstOrDefault(o => o.Selectable);
            return offer == null ? throw new NotImplementedException("Route Ancient has no selectable offer: " + view.Position)
                : new ChooseAncientOffer(offer.Id);
        }
        return view.Phase switch
        {
            PredictorPhase.OpeningChoice => new ChooseOpeningRelic(view.OpeningChoices[0]),
            PredictorPhase.Victory => new ContinueVictory(),
            PredictorPhase.ReplacePotion => new CancelPotionReplacement(),
            PredictorPhase.RewardRemoveCards => new SelectRemovalCards(view.DeckSelection!.CandidateCardIds.Take(1).ToImmutableArray()),
            PredictorPhase.TreasureClosed => new OpenTreasure(),
            PredictorPhase.TreasureRelic => view.TreasureRelic is { } treasure
                ? new TakeTreasureRelic(treasure.Id) : new LeaveTreasure(),
            PredictorPhase.SmithCards => new SelectSmithCard(view.DeckSelection!.CandidateCardIds[0]),
            PredictorPhase.RestCookCards => new SelectCookCards(view.DeckSelection!.CandidateCardIds.Take(2).ToImmutableArray()),
            PredictorPhase.RestHealFact => new ProvideRestMaxHpFact(100),
            PredictorPhase.RoomEntryHealth => new ProvideRoomMaximumHp(100),
            PredictorPhase.ObtainHealth => new ProvideEventHealthFact(new(80, 100)),
            PredictorPhase.ObtainChooseCard => new ChooseObtainCard(view.ObtainCardChoices.Length > 0 ? view.ObtainCardChoices[0].Id : null),
            PredictorPhase.ObtainChooseManyCards => new ChooseObtainManyCards(view.ObtainManyCardChoices.Take(1).Select(c => c.Id).ToImmutableArray()),
            PredictorPhase.ObtainDeckCards => new ChooseObtainDeckCards(view.DeckSelection!.CandidateCardIds.Take(
                Math.Min(view.DeckSelection.MaximumCardCount, view.DeckSelection.CandidateCardIds.Length)).ToImmutableArray()),
            PredictorPhase.ChooseBundle => new ChooseScrollBoxBundle(0),
            PredictorPhase.EventEligibility => new DeclareEventEligibility(view.EventEligibilityCandidate!.Value,
                view.EventEligibilityCandidate.Value.Entry is "MORPHIC_GROVE" or "AROMA_OF_CHAOS" or "WELLSPRING"),
            PredictorPhase.EventCards => new SelectEventCards(view.EventCardCandidates.Take(view.EventCardsRequired).ToImmutableArray()),
            PredictorPhase.TransformCards => new SelectTransformationCard(view.DeckSelection!.CandidateCardIds[0]),
            _ => throw new NotImplementedException("Route choice phase: " + view.Phase)
        };
    }

    private static void NativeRewardPipeline(PredictorContext context, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? saved = stateProperty.GetValue(manager);
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        object? savedAscension = ascensionProperty.GetValue(manager);
        try
        {
            var cases = new (string Name, string[] Relics, bool Elite)[]
            {
                ("baseline", [], false), ("wheel", ["PRAYER_WHEEL"], false), ("candy", ["LASTING_CANDY"], false),
                ("wheel+candy", ["PRAYER_WHEEL","LASTING_CANDY"], false),
                ("elite-stars-ticket-prismatic-rug", ["PRISMATIC_GEM","DINGY_RUG","WHITE_STAR","BLACK_STAR","WONGOS_MYSTERY_TICKET"], true),
                ("eggs", ["MOLTEN_EGG","TOXIC_EGG","FROZEN_EGG"], false),
                ("crucible-wheel", ["SILVER_CRUCIBLE","PRAYER_WHEEL"], false),
                ("silken-wheel", ["SILKEN_TRESS","PRAYER_WHEEL"], false),
                ("midas", ["AMETHYST_AUBERGINE"], false),
                ("vintage-wheel", ["PRAYER_WHEEL"], false)
            };
            foreach (var scenario in cases)
            {
                var pure = PredictorSeedCapture.CreateRun(context, authority);
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                ModifierModel[] modifiers = scenario.Name switch
                {
                    "midas" => [ModelDb.Modifier<MegaCrit.Sts2.Core.Models.Modifiers.Midas>().ToMutable()],
                    "vintage-wheel" => [ModelDb.Modifier<MegaCrit.Sts2.Core.Models.Modifiers.Vintage>().ToMutable()],
                    _ => []
                };
                var native = RunState.CreateForTest([player], acts: pure.Working.Acts.Select(a => ModelDb.GetById<ActModel>(new ModelId(a.Key.Category, a.Key.Entry))).ToArray(), modifiers: modifiers, seed: context.Seed, ascensionLevel: context.Ascension);
                stateProperty.SetValue(manager, native);
                ascensionProperty.SetValue(manager, new AscensionManager(context.Ascension));
                native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(UnlockState.all), native.Rng.UpFront);
                player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
                if (scenario.Name == "baseline")
                {
                    manager.GenerateRooms();
                    var worldCursor = native.Rng.UpFront.ToSerializable();
                    var pureCursor = pure.Working.Streams.Single(s => s.Stream == PredictorStream.UpFront);
                    Check((pureCursor.S0,pureCursor.S1,pureCursor.S2,pureCursor.S3,pureCursor.Calls) ==
                        (worldCursor.state0,worldCursor.state1,worldCursor.state2,worldCursor.state3,worldCursor.counter), "Native initialization world/bag UpFront");
                    Console.WriteLine("NATIVE_INITIALIZATION shared+personal bag -> GenerateRooms -> final UpFront PASS");
                }
                var boundary = pure.Working with { OpeningCompleted = true };
                foreach (string entry in scenario.Relics)
                {
                    player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC", entry)).ToMutable(), silent: true);
                    boundary = PredictorSettlementEffects.ObtainRelic(boundary, K(entry));
                    // Native comparison starts after these declared obtains.
                    player.RelicGrabBag.Remove(ModelDb.GetById<RelicModel>(new ModelId("RELIC", entry)));
                    native.SharedRelicGrabBag.Remove(ModelDb.GetById<RelicModel>(new ModelId("RELIC", entry)));
                }
                if (scenario.Elite)
                {
                    ((WongosMysteryTicket)player.Relics.Single(r => r is WongosMysteryTicket)).CombatsFinished = 2;
                    boundary = boundary with { Relics = boundary.Relics.Select(r => r.Key.Entry == "WONGOS_MYSTERY_TICKET" ? r with { CombatsFinished = 2 } : r).ToImmutableArray() };
                }
                pure = new PredictorRun(pure.Context with { Modifiers = modifiers.Select(m => new PredictorRunModifier(new(m.Id.Category, m.Id.Entry))).ToImmutableArray() }, boundary);
                for (int battle = 0; battle < 3; battle++)
                {
                    ModelKey encounter = scenario.Elite ? pure.Working.Acts[0].EliteEncounters[pure.Working.Acts[0].EliteCursor] : pure.Working.Acts[0].NormalEncounters[pure.Working.Acts[0].NormalCursor];
                    var room = new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.GetById<EncounterModel>(new ModelId(encounter.Category, encounter.Entry)).ToMutable(), native);
                    foreach (var ticket in player.Relics.OfType<WongosMysteryTicket>()) ticket.AfterCombatEnd(room).GetAwaiter().GetResult();
                    var rewardSet = new RewardsSet(player).WithRewardsFromRoom(room);
                    rewardSet.GenerateWithoutOffering().GetAwaiter().GetResult();
                    pure.EnterCombatRewards(scenario.Elite ? PredictorCombatKind.Elite : PredictorCombatKind.Monster, 1, battle + 2);
                    var actualCards = pure.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).SelectMany(r => r.Cards).ToArray();
                    var expectedCards = rewardSet.Rewards.OfType<CardReward>().SelectMany(r => r.Cards).ToArray();
                    Check(actualCards.Select(c => c.Key.Entry).SequenceEqual(expectedCards.Select(c => c.Id.Entry)), "Native CardReward identities: " + scenario.Name +
                        " RT2=" + string.Join(',', actualCards.Select(c => c.Key.Entry)) + " native=" + string.Join(',', expectedCards.Select(c => c.Id.Entry)) +
                        " pools=" + string.Join(',', UnlockState.all.CharacterCardPools.Select(p => p.Id.Entry)));
                    Check(actualCards.Select(c => c.UpgradeLevel).SequenceEqual(expectedCards.Select(c => c.CurrentUpgradeLevel)), "Native upgrades");
                    Check(pure.Rewards.Where(r => r.Kind == PredictorRewardKind.Gold).Select(r => r.GoldAmount).SequenceEqual(rewardSet.Rewards.OfType<GoldReward>().Select(r => r.Amount)), "Native gold range/order");
                    Check(pure.Rewards.Where(r => r.Kind == PredictorRewardKind.Potion).Select(r => r.Key.Entry).SequenceEqual(rewardSet.Rewards.OfType<PotionReward>().Select(r => r.Potion!.Id.Entry)), "Native potion identity");
                    Check(pure.Rewards.Where(r => r.Kind == PredictorRewardKind.Relic).Select(r => r.Key.Entry).SequenceEqual(rewardSet.Rewards.OfType<RelicReward>().Select(r => r.Relic!.Id.Entry)), "Native relic generation/bag");
                    Check(actualCards.Select(c => c.Enchantment?.Entry).SequenceEqual(expectedCards.Select(c => c.Enchantment?.Id.Entry)), "Native enchantment");
                    var rng = pure.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards);
                    var expected = player.PlayerRng.Rewards.ToSerializable();
                    Check((rng.S0,rng.S1,rng.S2,rng.S3,rng.Calls) == (expected.state0,expected.state1,expected.state2,expected.state3,expected.counter), "Native reward continuation");
                    Check(pure.Working.Odds.CardRarityOffset == player.PlayerOdds.CardRarity.CurrentValue && pure.Working.Odds.PotionDropChance == player.PlayerOdds.PotionReward.CurrentValue, "Native pity/odds state");
                    foreach (var relic in player.Relics) relic.BeforeCombatRewardOffered(rewardSet, room).GetAwaiter().GetResult();
                    Select(pure, new LeaveRewards());
                }
                Console.WriteLine($"NATIVE_REWARD_PHASES {scenario.Name} three consecutive nodes identities/RNG/pity/bag PASS");
            }
            foreach (string entry in new[] { "MOLTEN_EGG", "TOXIC_EGG", "FROZEN_EGG", "SILVER_CRUCIBLE", "SILKEN_TRESS" })
            {
                var seedRun = PredictorSeedCapture.CreateRun(context, authority);
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                var native = RunState.CreateForTest([player], seed: context.Seed, ascensionLevel: context.Ascension);
                stateProperty.SetValue(manager, native);
                ascensionProperty.SetValue(manager, new AscensionManager(context.Ascension));
                var encounter = seedRun.Working.Acts[0].NormalEncounters[0];
                var room = new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.GetById<EncounterModel>(new ModelId(encounter.Category, encounter.Entry)).ToMutable(), native);
                var generated = new RewardsSet(player).WithRewardsFromRoom(room);
                generated.GenerateWithoutOffering().GetAwaiter().GetResult();
                var cardReward = generated.Rewards.OfType<CardReward>().Single();
                var prototypes = cardReward.Cards.Select(c => new PredictorCard(0, new("CARD", c.Id.Entry),
                    Enum.Parse<RolltheSpire2.Core.Effects.Snapshots.EffectCardType>(c.Type.ToString()), c.CurrentUpgradeLevel, 1, true, 1)).ToImmutableArray();
                var pure = new PredictorRun(seedRun.Context, seedRun.Working);
                pure.EnterRewardNode(new(1, 2, "open-reward"), [
                    new(0, PredictorRewardKind.Card, PredictorRewardOrigin.Combat, default, prototypes),
                    new(0, PredictorRewardKind.Card, PredictorRewardOrigin.Combat, default, prototypes, ManuallySetCards: true),
                    new(0, PredictorRewardKind.Card, PredictorRewardOrigin.Combat, default, prototypes),
                    new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, K(entry), [])]);
                var exited = pure.Rewards[2];
                Select(pure, new EnterCardSelection(exited.Id)); Select(pure, new ExitCardSelection());
                var beforeRng = player.PlayerRng.Rewards.ToSerializable();
                var relic = ModelDb.GetById<RelicModel>(new ModelId("RELIC", entry)).ToMutable();
                player.AddRelicInternal(relic); // Actual registered OnRelicObtained; no private callback simulation.
                Select(pure, new TakeReward(pure.Rewards[3].Id));
                var actual = pure.Rewards[0].Cards;
                Check(actual.Select(c => c.UpgradeLevel).SequenceEqual(cardReward.Cards.Select(c => c.CurrentUpgradeLevel)) &&
                    actual.Select(c => c.Enchantment?.Entry).SequenceEqual(cardReward.Cards.Select(c => c.Enchantment?.Id.Entry)), "Native open reward notification: " + entry);
                Check(pure.Rewards[1].Cards.Select(c => (c.UpgradeLevel,c.Enchantment)).SequenceEqual(prototypes.Select(c => (c.UpgradeLevel,c.Enchantment))) &&
                    pure.Rewards[2].Cards.SequenceEqual(exited.Cards), "Fixed/exited rewards must not subscribe");
                Check(entry != "SILVER_CRUCIBLE" || ((SilverCrucible)relic).TimesUsed == 0 && pure.Working.Relics.Last().RewardUpgradeUses == 0, "Notification consumed crucible counter");
                Check(entry != "SILKEN_TRESS" || !relic.IsUsedUp && !pure.Working.Relics.Last().SilkenUsed, "Notification consumed silken counter");
                var afterRng = player.PlayerRng.Rewards.ToSerializable();
                Check((beforeRng.state0,beforeRng.state1,beforeRng.state2,beforeRng.state3,beforeRng.counter) ==
                    (afterRng.state0,afterRng.state1,afterRng.state2,afterRng.state3,afterRng.counter), "Notification rerolled rewards");
            }
            Console.WriteLine("NATIVE_OPEN_REWARD eggs/crucible/silken notification identities and counters PASS; fixed/exited behavior checked in RT2 settlement");
        }
        finally { stateProperty.SetValue(manager, saved); ascensionProperty.SetValue(manager, savedAscension); }
    }

    private static void ModifierOpeningChecks(PredictorContext context, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        var terminal = PredictorSeedCapture.CreateRun(context with { Modifiers = [new(new("MODIFIER", "TERMINAL"))] }, authority);
        var terminalMap = terminal.RouteMap(1);
        var route = ImmutableArray.CreateBuilder<RolltheSpire2.Core.Prediction.Maps.MapPosition>();
        var point = terminalMap.Points.OrderBy(p => p.Position.Row).First();
        while (true)
        {
            route.Add(point.Position);
            if (point.Children.Count == 0) break;
            point = terminalMap.Points.Single(p => p.Position == point.Children[0]);
        }
        terminal.SetRoute(1, route.ToImmutable());
        terminal.AdvanceRoute();
        Check(terminal.Phase == PredictorPhase.RoomEntryHealth && terminal.Checkpoints.Length == 1,
            "Terminal must keep automatic Opening entry uncommitted until required max-HP fact");
        var beforeAnswer = terminal.Working;
        Check(terminal.Submit(terminal.Request!, new ProvideRoomMaximumHp(0)) == PredictorInputResult.Invalid &&
            ReferenceEquals(terminal.Working, beforeAnswer), "Invalid Terminal fact changed working state");
        var interrupted = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(terminal), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(interrupted.Phase == PredictorPhase.RoomEntryHealth, "Resume must re-enter unfinished Terminal node from entry");
        Select(terminal, new ProvideRoomMaximumHp(80));
        Check(terminal.Phase == PredictorPhase.Complete && terminal.Checkpoints[^1].CompletedNode!.HealthEffects.Single().MaxHpDelta == -1,
            "Terminal entry completion retains source consequence");
        terminal.Rewind(0);
        terminal.AdvanceRoute();
        Select(terminal, new ProvideRoomMaximumHp(1));
        Check(terminal.Checkpoints[^1].CompletedNode!.HealthEffects.IsEmpty, "Terminal leaves valid max-HP one state unchanged");
        foreach (string modifier in new[] { "DRAFT", "SEALED_DECK", "INSANITY", "ALL_STAR", "SPECIALIZED" })
        {
            var run = PredictorSeedCapture.CreateRun(context with { Modifiers = [new(new("MODIFIER", modifier))] }, authority);
            int initial = run.Working.Deck.Length;
            Check(initial == (modifier is "DRAFT" or "SEALED_DECK" or "INSANITY" ? 1 : 11), "Modifier clear precedes A5 curse");
            var boundary = run.Working;
            run.EnterOpening();
            Check(run.Project().OpeningModifierOrdinal == 0 && run.Working.OpeningOptions.IsEmpty, "Modifier Opening contract");
            Select(run, new ActivateOpeningModifier(0));
            int picks = 0;
            while (run.Phase == PredictorPhase.OpeningModifierCards)
            {
                var oldRequest = run.Request!;
                var ids = run.OpeningModifierCards.Take(run.OpeningModifierCardsRequired).Select(c => c.Id).ToImmutableArray();
                Select(run, new SelectOpeningModifierCards(ids));
                Check(run.Submit(oldRequest, new SelectOpeningModifierCards(ids)) == PredictorInputResult.Stale, "Modifier grid stale resubmit");
                picks++;
            }
            Check(run.Phase == PredictorPhase.Complete && run.Working.OpeningCompleted, "Modifier Opening completion");
            Check(run.Working.Deck.Length == initial + (modifier == "INSANITY" ? 30 : modifier is "DRAFT" or "SEALED_DECK" ? 10 : 5), "Modifier concrete instance count");
            Check(modifier != "DRAFT" || picks == 10, "Draft ten independent offers");
            var restored = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint);
            Check(restored.Working.Deck.SequenceEqual(run.Working.Deck) && restored.Context.Modifiers.SequenceEqual(run.Context.Modifiers), "Modifier persistence");
            restored.Rewind(0);
            Check(restored.Working.Deck.SequenceEqual(boundary.Deck), "Modifier rewind entry");
        }
        Console.WriteLine("RUNTIME_MODIFIER_OPENING Draft/Sealed/Insanity/AllStar/Specialized actual seed initialization + choices + save/rewind PASS (controlled Runtime behavior)");
    }

    private static void ContinuedNodes(PredictorContext context, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        var found = new HashSet<string>();
        for (int ordinal = 0; ordinal < 300 && found.Count < 2; ordinal++)
        {
            string seed = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(RolltheSpire2.Compatibility.Beta111Profile.Instance, (ulong)ordinal);
            var run = PredictorSeedCapture.CreateRun(context with { Seed = seed }, authority);
            var source = run.Working.OpeningOptions.FirstOrDefault(k => (k.Entry is "NEW_LEAF" or "NEOWS_TALISMAN") && !found.Contains(k.Entry));
            if (!source.IsValid) continue;
            found.Add(source.Entry);
            var original = run.Working;
            run.EnterOpening(); Select(run, new ChooseOpeningRelic(source));
            if (source.Entry == "NEW_LEAF")
            {
                var request = run.Request!;
                long target = run.Selection.CandidateCardIds[0];
                Check(run.Submit(request, new SelectTransformationCard(long.MaxValue)) == PredictorInputResult.Invalid && run.Request == request, "Invalid transform target changed state");
                Select(run, new SelectTransformationCard(target));
                Check(!run.Working.Deck.Contains(target) && run.Working.Card(target).Removed && run.Working.Deck[^1] >= original.NextInstanceId, "Transform must remove original and append new instance");
                Check(run.Submit(request, new SelectTransformationCard(target)) == PredictorInputResult.Stale, "Repeated transform request accepted");
                Check(run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls == 1 && run.Working.Streams.Single(s => s.Stream == PredictorStream.Transformations).Calls == 0, "NewLeaf wrong RNG owner");
            }
            else Check(run.Working.Deck.Count(id => run.Working.Card(id).UpgradeLevel > 0) == 2, "Talisman last basic pair");
            Check(run.Phase == PredictorPhase.Complete, "Opening did not settle");
            run.EnterRestSite(1, 2);
            Select(run, new SelectRestAction(PredictorRestAction.Smith));
            Select(run, new CancelSmithSelection());
            Check(run.RestOptions.Contains(PredictorRestAction.Smith), "Smith cancel consumed option");
            Select(run, new SelectRestAction(PredictorRestAction.Smith));
            long smith = run.Selection.CandidateCardIds[0];
            int before = run.Working.Card(smith).UpgradeLevel;
            Select(run, new SelectSmithCard(smith));
            Check(run.Working.Card(smith).UpgradeLevel == before + 1 && run.RestOptions.IsEmpty, "Smith state/action settlement");
            Select(run, new LeaveRestSite());
            run = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(run), context.CatalogFingerprint, context.UnlockFingerprint);
            run.EnterCombatRewards(PredictorCombatKind.Monster, 1, 3);
            Select(run, new LeaveRewards());
            run.Rewind(1);
            Check(run.Working.OpeningCompleted && run.Working.CombatCount == 0 && run.Working.Card(smith).UpgradeLevel == before, "Rewind rest must preserve Opening and discard downstream");
            Console.WriteLine($"RUNTIME_CONTINUED {seed} {source.Entry} -> Smith cancel/reselect -> save/reload -> combat -> rewind Rest PASS");
        }
        Check(found.Count == 2, "New Opening sources not exercised");
        var seedRun = PredictorSeedCapture.CreateRun(context, authority);
        var state = PredictorSettlementEffects.ObtainRelic(seedRun.Working with { OpeningCompleted = true }, K("MINIATURE_TENT"));
        state = PredictorSettlementEffects.ObtainRelic(state, K("GIRYA"));
        var rest = new PredictorRun(seedRun.Context, state);
        rest.EnterRestSite(1, 2);
        Select(rest, new SelectRestAction(PredictorRestAction.Lift));
        Check(rest.RestOptions.Length == 2 && !rest.RestOptions.Contains(PredictorRestAction.Lift), "Tent must remove only used action");
        Select(rest, new SelectRestAction(PredictorRestAction.Heal));
        Check(rest.RestOptions.SequenceEqual(new[] { PredictorRestAction.Smith }), "Rest options regenerated after Heal");
        Select(rest, new LeaveRestSite());
        rest = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(rest), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(rest.Working.Relics.Single(r => r.Key.Entry == "GIRYA").TimesLifted == 1, "Lift counter missing checkpoint");
        Console.WriteLine("RUNTIME_REST Tent remaining actions + Lift counter + explicit Leave + reload PASS (controlled behavior)");
    }

    private static void NativeTransformationAndPotions(PredictorContext context, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? saved = stateProperty.GetValue(manager);
        try
        {
            foreach (bool eggs in new[] { false, true })
            {
                var run = PredictorSeedCapture.CreateRun(context, authority);
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                var native = RunState.CreateForTest([player], seed: context.Seed);
                stateProperty.SetValue(manager, native);
                player.Deck.AddInternal(native.CreateCard(ModelDb.GetById<CardModel>(new("CARD", "ASCENDERS_BANE")), player));
                var state = run.Working;
                if (eggs)
                    foreach (string key in new[] { "MOLTEN_EGG", "TOXIC_EGG", "FROZEN_EGG" })
                    {
                        player.AddRelicInternal(ModelDb.GetById<RelicModel>(new("RELIC", key)).ToMutable());
                        state = PredictorSettlementEffects.ObtainRelic(state, K(key));
                    }
                var originals = new[] { player.Deck.Cards[4], player.Deck.Cards[0] };
                var ids = ImmutableArray.Create(state.Deck[4], state.Deck[0]);
                MegaCrit.Sts2.Core.Commands.CardCmd.Transform(originals.Select(c => new MegaCrit.Sts2.Core.Entities.Cards.CardTransformation(c)), native.Rng.Niche).GetAwaiter().GetResult();
                var transformed = PredictorCardChanges.Transform(run.Context, state, ids, PredictorStream.Niche);
                Check(transformed.State.Deck.Select(id => transformed.State.Card(id).Key.Entry).SequenceEqual(player.Deck.Cards.Select(c => c.Id.Entry)), "Native transform deck append/index order");
                Check(transformed.State.Deck.Select(id => transformed.State.Card(id).UpgradeLevel).SequenceEqual(player.Deck.Cards.Select(c => c.CurrentUpgradeLevel)), "Native transform add-to-deck egg modifications");
                var a = transformed.State.Streams.Single(s => s.Stream == PredictorStream.Niche); var b = native.Rng.Niche.ToSerializable();
                Check((a.S0,a.S1,a.S2,a.S3,a.Calls) == (b.state0,b.state1,b.state2,b.state3,b.counter), "Native transform continuation");
            }
            foreach (bool sozu in new[] { false, true })
            {
                var run = PredictorSeedCapture.CreateRun(context, authority);
                var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                var native = RunState.CreateForTest([player], seed: context.Seed);
                stateProperty.SetValue(manager, native);
                typeof(Player).GetMethod("SetMaxPotionCountInternal", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(player, [run.Working.PotionSlots.Length]);
                var state = run.Working with { OpeningCompleted = true };
                foreach (string entry in (sozu ? new[] { "SOZU", "PETRIFIED_TOAD", "DELICATE_FROND" } : new[] { "PETRIFIED_TOAD", "DELICATE_FROND" }))
                {
                    player.AddRelicInternal(ModelDb.GetById<RelicModel>(new("RELIC", entry)).ToMutable());
                    state = PredictorSettlementEffects.ObtainRelic(state, K(entry));
                }
                player.Relics.OfType<DelicateFrond>().Single().BeforeCombatStart().GetAwaiter().GetResult();
                player.Relics.OfType<PetrifiedToad>().Single().BeforeCombatStartLate().GetAwaiter().GetResult();
                var entryResult = PredictorPotionEffects.BeforeCombat(run.Context, state, default);
                Check(entryResult.State.PotionSlots.Select(p => p?.Key.Entry).SequenceEqual(player.PotionSlots.Select(p => p?.Id.Entry)), "Native entry potion slots");
                Check(entryResult.Attempts[^1].Potion.Key.Entry == "POTION_SHAPED_ROCK" && entryResult.Attempts[^1].Result != PredictorInputResult.Accepted, "Late Toad ran before Frond");
                state = entryResult.State;
                if (!sozu)
                {
                    player.PotionSlots[0]!.Discard();
                    state = state with { PotionSlots = state.PotionSlots.SetItem(0, null) };
                }
                var coffer = ModelDb.Relic<AlchemicalCoffer>().ToMutable(); player.AddRelicInternal(coffer);
                coffer.AfterObtained().GetAwaiter().GetResult();
                var cofferRun = new PredictorRun(run.Context, state);
                cofferRun.EnterRewardNode(new(1, 2, "coffer-obtain"), [new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Event, K("ALCHEMICAL_COFFER"), [])]);
                Select(cofferRun, new TakeReward(cofferRun.Rewards[0].Id));
                Check(cofferRun.Working.PotionSlots.Select(p => p?.Key.Entry).SequenceEqual(player.PotionSlots.Select(p => p?.Id.Entry)), "Native Coffer slots/capacity");
                var a = cofferRun.Working.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration); var b = native.Rng.CombatPotionGeneration.ToSerializable();
                Check((a.S0,a.S1,a.S2,a.S3,a.Calls) == (b.state0,b.state1,b.state2,b.state3,b.counter), "Native entrance+Coffer potion stream");
                Select(cofferRun, new LeaveRewards());
                var cofferReload = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(cofferRun), context.CatalogFingerprint, context.UnlockFingerprint);
                Check(cofferReload.Checkpoints[^1].CompletedNode!.Inputs.Single(i => i.Action == "AlchemicalCoffer").PotionAttempts.Length == 4, "Coffer attempts missing checkpoint");
                var chain = new PredictorRun(run.Context, state);
                chain.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2);
                Select(chain, new LeaveRewards());
                chain = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(chain), context.CatalogFingerprint, context.UnlockFingerprint);
                chain.EnterRestSite(1, 3);
                var bottle = chain.Working.PotionSlots.FirstOrDefault(p => p != null);
                if (bottle != null) Select(chain, new DiscardPotion(chain.Working.PotionSlots.FindIndex(p => p?.Id == bottle.Id), bottle.Id));
                Select(chain, new LeaveRestSite());
                chain.EnterCombatRewards(PredictorCombatKind.Monster, 1, 4);
                Select(chain, new LeaveRewards());
                Check(chain.Checkpoints[^1].CompletedNode!.Inputs.First(i => i.Action == "CombatBoundary").PotionAttempts.Length == 2, "Next battle entrance missing persisted/discarded slot continuation");
            }
            Console.WriteLine("NATIVE_TRANSITIONS CardCmd.Transform batch/deck/eggs/Niche; Frond -> late Toad -> Coffer slots/RNG with Sozu; next battle refill PASS");
        }
        finally { stateProperty.SetValue(manager, saved); }
    }

    private static void CombatPremiseChecks(PredictorContext context, RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        var seedRun = PredictorSeedCapture.CreateRun(context, authority);
        var state = seedRun.Working with { OpeningCompleted = true };
        var ids = new List<long>();
        foreach (var entry in new[] { "THE_HUNT", "THE_HUNT", "ROYALTIES", "ROYALTIES", "GENETIC_ALGORITHM", "THE_SCYTHE", "HAND_OF_GREED", "FEED", "ALCHEMIZE" })
        {
            var native = ModelDb.GetById<CardModel>(new ModelId("CARD", entry));
            var card = new PredictorCard(state.NextInstanceId, new("CARD", entry), Enum.Parse<RolltheSpire2.Core.Effects.Snapshots.EffectCardType>(native.Type.ToString()),
                ids.Count == 3 ? 1 : 0, 1, true, 1);
            ids.Add(card.Id);
            state = state with { NextInstanceId = state.NextInstanceId + 1, Cards = state.Cards.Add(card), Deck = state.Deck.Add(card.Id) };
        }
        var automatic = new PredictorRun(seedRun.Context, state);
        automatic.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2);
        Check(automatic.Rewards.Count(r => r.Kind == PredictorRewardKind.Card) == 3, "Two Hunt instances must add two rewards");
        Check(automatic.Rewards.Any(r => r.Kind == PredictorRewardKind.Gold && r.GoldAmount == 70), "Royalties same-owner aggregate 30+40");
        Check(automatic.Working.Card(ids[4]).IncreasedBlock == 3 && automatic.Working.Card(ids[5]).IncreasedDamage == 5, "Entry master growth once");
        Check(automatic.Working.GoldBudget == state.GoldBudget + 20, "HandOfGreed before reward settlement");
        Select(automatic, new LeaveRewards());
        var record = automatic.Checkpoints[^1].CompletedNode!.Inputs[0];
        Check(record.CombatCardTriggers.Length == 9 && record.CombatCardTriggers.All(t => t.EffectiveTriggers == 1), "Default once per instance");
        Check(record.HealthConsequences.Single().MaxHpDelta == 3 && record.GeneratedPotions.Length == 1, "Feed/Alchemize consequences retained");
        var reloaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(automatic), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(reloaded.Working.Card(ids[4]).IncreasedBlock == 3, "Growth checkpoint");
        reloaded.EnterCombatRewards(PredictorCombatKind.Monster, 1, 3);
        Check(reloaded.Working.Card(ids[4]).IncreasedBlock == 6 && reloaded.Working.Card(ids[5]).IncreasedDamage == 10, "Growth across two battles");
        var manual = new PredictorRun(seedRun.Context, state);
        var facts = new PredictorCombatFacts(false) { CardTriggers = ids.Select(id => new PredictorCardTrigger(id, 0)).ToImmutableArray() };
        manual.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: facts);
        Check(manual.Rewards.Count(r => r.Kind == PredictorRewardKind.Card) == 1 && manual.Rewards.All(r => r.GoldAmount != 70), "Manual zero must replace automatic rewards");
        Check(manual.Working.GoldBudget == state.GoldBudget && manual.Working.Card(ids[4]).IncreasedBlock == 0, "Manual zero must replace growth/income");
        Check(manual.Working.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration).Calls == 0, "Manual zero still generated Alchemize");
        var invalid = new PredictorRun(seedRun.Context, state);
        try { invalid.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: new(false)); throw new Exception("Incomplete manual facts accepted"); }
        catch (ArgumentException) { Check(ReferenceEquals(invalid.Working, state) && invalid.Phase == PredictorPhase.Complete, "Invalid facts partially advanced node"); }
        var potionState = state with { PotionSlots = [new(state.NextInstanceId, new("POTION", "ENTROPIC_BREW")),
            new(state.NextInstanceId + 1, new("POTION", "FAIRY_IN_A_BOTTLE"))], NextInstanceId = state.NextInstanceId + 2 };
        long tailId = potionState.NextInstanceId;
        potionState = potionState with { NextInstanceId = tailId + 1,
            Relics = potionState.Relics.Add(new(tailId, new("RELIC", "LIZARD_TAIL"), 1)) };
        var potionFacts = new PredictorCombatFacts(false)
        {
            CardTriggers = ids.Select(id => new PredictorCardTrigger(id, 0)).ToImmutableArray(),
            Potions = new(false, [new(PredictorCombatPotionAction.FairyTriggered, new(potionState.PotionSlots[1]!.Id)),
                new(PredictorCombatPotionAction.LizardTailTriggered, RelicId: tailId),
                new(PredictorCombatPotionAction.Use, new(potionState.PotionSlots[0]!.Id)),
                new(PredictorCombatPotionAction.Discard, new(GeneratedOrdinal: 0)),
                new(PredictorCombatPotionAction.AdditionalAlchemize)])
        };
        var withFacts = new PredictorRun(seedRun.Context, potionState);
        withFacts.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2, facts: potionFacts);
        Check(withFacts.Working.PotionSlots.All(p => p != null) &&
            withFacts.Working.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration).Calls == 6,
            "Fairy consumption -> Entropic out-of-combat pool fill -> discard generated instance -> Alchemize in-combat generation");
        Select(withFacts, new LeaveRewards());
        var potionReload = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(withFacts), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(potionReload.Working.PotionSlots.SequenceEqual(withFacts.Working.PotionSlots) &&
            potionReload.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.CombatPotionFacts?.Changes.Length == 5) && potionReload.Working.Relics.Single(r => r.Id == tailId).LizardTailUsed, "Combat inventory facts/slots/Tail checkpoint");
        // Concrete persistent relic consequences at their source phases.
        var boundary = seedRun.Working with { OpeningCompleted = true };
        long teaId = boundary.NextInstanceId;
        var teaState = boundary with { NextInstanceId = teaId + 1,
            Relics = boundary.Relics.Add(new(teaId, new("RELIC", "BONE_TEA"), 1) { CombatsLeft = 1 }) };
        foreach (bool firstTurn in new[] { false, true })
        {
            var tea = new PredictorRun(seedRun.Context, teaState);
            tea.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
                facts: new() { FirstPlayerTurnOccurred = firstTurn });
            Check(tea.Working.Relics.Single(r => r.Id == teaId).CombatsLeft == (firstTurn ? 0 : 1) &&
                tea.Working.Deck.All(id => tea.Working.Card(id).UpgradeLevel == teaState.Card(id).UpgradeLevel),
                "BoneTea consumes only actual first owner turn, without master upgrade");
            Select(tea, new LeaveRewards());
            var restoredTea = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(tea), context.CatalogFingerprint, context.UnlockFingerprint);
            Check(restoredTea.Working.Relics.Single(r => r.Id == teaId).CombatsLeft == (firstTurn ? 0 : 1), "BoneTea boundary counter reload");
        }
        long meatId = boundary.NextInstanceId;
        var meatState = boundary with { NextInstanceId = meatId + 2, Relics = boundary.Relics
            .Add(new(meatId, new("RELIC", "MEAT_ON_THE_BONE"), 1))
            .Add(new(meatId + 1, new("RELIC", "MEAT_ON_THE_BONE"), 1)) };
        foreach (int hp in new[] { 20, 40, 41 })
        {
            var meat = new PredictorRun(seedRun.Context, meatState);
            meat.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
                facts: new() { VictoryHealth = new(hp, 80) });
            Check(meat.Working.CurrentHealthEffects.Count(h => h.SourceKey.Entry == "MEAT_ON_THE_BONE") ==
                (hp == 20 ? 2 : hp == 40 ? 1 : 0), "Meat threshold uses preceding early heals");
            Select(meat, new LeaveRewards());
            Check(meat.Checkpoints[^1].CompletedNode!.HealthEffects.Any(h => h.SourceKey.Entry == "MEAT_ON_THE_BONE") == (hp <= 40) &&
                meat.Working.CurrentHealthEffects.IsEmpty, "Nominal health effects commit to node history, not persistent HP");
        }
        long swordId = boundary.NextInstanceId;
        var swordState = boundary with { NextInstanceId = swordId + 1, Relics = boundary.Relics
            .Insert(0, new(swordId, new("RELIC", "SWORD_OF_STONE"), 1) { SwordElitesDefeated = 4 }) };
        var sword = new PredictorRun(seedRun.Context, swordState);
        sword.EnterCombatRewards(PredictorCombatKind.Elite, 1, 2);
        Check(sword.Working.Relics[0].Key.Entry == "SWORD_OF_JADE" && sword.Working.Relics[0].Id != swordId &&
            sword.Working.Relics.Skip(1).Select(r => r.Id).SequenceEqual(boundary.Relics.Select(r => r.Id)),
            "Fifth elite replaces Sword at same inventory position with a fresh instance");
        Select(sword, new LeaveRewards());
        var savedSword = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(sword), context.CatalogFingerprint, context.UnlockFingerprint);
        Check(savedSword.Working.Relics[0].Key.Entry == "SWORD_OF_JADE", "Sword replacement persists");
        savedSword.Rewind(0);
        Check(savedSword.Working.Relics[0].Id == swordId && savedSword.Working.Relics[0].SwordElitesDefeated == 4,
            "Sword rewind restores original instance and counter");
        var hopperState = seedRun.Working with { OpeningCompleted = true,
            Acts = seedRun.Working.Acts.SetItem(0, seedRun.Working.Acts[0] with
            { NormalEncounters = [new("ENCOUNTER", "THIEVING_HOPPER_WEAK")], NormalCursor = 0 }) };
        long stolenId = hopperState.Deck[0];
        foreach (bool takeBack in new[] { false, true })
        {
            var hopper = new PredictorRun(seedRun.Context, hopperState);
            hopper.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
                facts: new() { StolenCards = new([new(stolenId, true)]) });
            var returned = hopper.Rewards.Single(r => r.Kind == PredictorRewardKind.SpecialCard);
            Check(returned.Cards.Single().Id == stolenId && !hopper.Working.Deck.Contains(stolenId),
                "Swipe return must register original instance without automatically taking it");
            if (takeBack) Select(hopper, new TakeReward(returned.Id));
            Select(hopper, new LeaveRewards());
            var saved = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(hopper), context.CatalogFingerprint, context.UnlockFingerprint);
            Check(saved.Working.Deck.Contains(stolenId) == takeBack && saved.Working.Cards.Count(c => c.Id == stolenId) == 1,
                "Swipe SpecialCard take/skip preserves exact master instance across reload");
            saved.Rewind(0);
            Check(saved.Working.Deck.SequenceEqual(hopperState.Deck), "Swipe rewind restores pre-theft deck");
        }
        var lost = new PredictorRun(seedRun.Context, hopperState);
        lost.EnterCombatRewards(PredictorCombatKind.Monster, 1, 2,
            facts: new() { StolenCards = new([new(stolenId, false)]) });
        Check(!lost.Working.Deck.Contains(stolenId) && lost.Working.Card(stolenId).Removed &&
            lost.Rewards.All(r => r.Kind != PredictorRewardKind.SpecialCard), "Escaped Hopper does not return stolen card");
        var dynamicSources = seedRun.Context.Catalog!.Encounters.Single(e => e.Key.Entry == "OVICOPTER_NORMAL");
        Check(!PredictorCombatEnemyFacts.Valid(dynamicSources, []) && PredictorCombatEnemyFacts.Valid(dynamicSources,
            dynamicSources.EnemyCreationSources.Select(s => new PredictorEnemyCreationFact(s, 0)).ToImmutableArray()),
            "Dynamic creation sources require explicit zero rather than omitted facts");
        Console.WriteLine("RUNTIME_COMBAT_PREMISE entry instances -> Hunt/Royalties/growth/Gold/health/Alchemize -> checkpoint -> next battle; manual replacement PASS (product premise, not simulated combat parity)");
    }
}
