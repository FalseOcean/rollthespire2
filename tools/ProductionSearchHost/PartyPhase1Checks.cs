using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Ui.Pages.Search.Editors;
using Godot;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Presentation.Localization;

internal static partial class PartyPhase1Checks
{
    internal static async Task RunAsync(Node host, string output, string pck)
    {
        Directory.CreateDirectory(output);
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidOperationException) { checks++; return; } throw new Exception("Expected rejection: " + name); }
        var dll = Path.Combine(Path.GetDirectoryName(pck)!, "data_sts2_windows_x86_64", "sts2.dll");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) == "0861BFA1DF347538D932F22D580E75420F08082792EB914E53B4882764ACDBE9", "pinned Beta111");
        Check(typeof(RunState).Module.ModuleVersionId == Guid.Parse("73b63ee0-6c0a-47bb-b0d1-b21f6d94222e"), "loaded Beta111 MVID");
        Check(ProjectSettings.LoadResourcePack(pck, false), "resources");
        TestMode.IsOn = true; LocalContext.NetId = null;
        MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        await MegaCrit.Sts2.Core.Modding.ModManager.Initialize(new MegaCrit.Sts2.Core.Modding.ModManagerFileIo(), null, null);
        ModelDb.Init(typeof(AbstractModel).Assembly.GetTypes().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && t.IsSubclassOf(typeof(AbstractModel))).ToArray());
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
        SaveManager.MockInstanceForTesting(new SaveManager(new MockGodotFileIo("user://party-phase1-memory"), true));
        SaveManager.Instance.InitSettingsDataForTest(); SaveManager.Instance.InitPrefsDataForTest(); SaveManager.Instance.InitProfileId(1); SaveManager.Instance.InitProgressData();
        SaveManager.Instance.SettingsSave.Language = "eng"; MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        var detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "PartyPhase1", true, "");
        if (OS.GetCmdlineUserArgs().Contains("--compatibility-party-checks"))
        {
            await CompatibilityChecks.CheckExtendedPartyFamiliesAsync(detection, output);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-cpu-corpus"))
        {
            ExportCpuCorpus(output, detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--run-entry"))
        {
            if (System.Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOLD_FOCUS")=="1" ||
                System.Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_WORKER_LIFECYCLE")=="1" ||
                System.Environment.GetEnvironmentVariable("RT2_CRYSTAL_DRIFTWOOD_EXPERIMENT") is { Length: > 0 })
            {
                if(PredictorCrystalAssistantChecks.Run("000000000000", 10)!=0)
                    throw new InvalidOperationException("Crystal experiment incomplete or failed; inspect its JSON receipts and verdict marker");
                return;
            }
            await CheckRunEntry(host, detection, output);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-kaleidoscope-order"))
        {
            CheckKaleidoscopeOrder(host, output, detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-neow-authoring"))
        {
            await CheckNeowAuthoring(host, detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-bones-pool"))
        {
            await CheckBonesPool(detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-ancient-event-coverage"))
        {
            await CheckAncientEventCoverage(detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-probability"))
        {
            RunProbability(detection);
            if (OS.GetCmdlineUserArgs().Contains("--party-c-probability-ui"))
                await CaptureCombatPotionUi(host, output, detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-neow-batch"))
        {
            await RunNeowBatchComparison(detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-neow-gpu"))
        {
            await RunNeowGpu(detection);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--party-family-checks"))
        {
            await RunFamilies(host, output, detection);
            return;
        }
        var evidence = new List<object>();
        WorkbenchSearchDraft? uiDraft = null;
        foreach (int count in new[] { 2, 3, 4 })
        foreach (string seed in new[] { "000000000040", "0000000002MY" })
        foreach (string unlockCase in count == 2 ? new[] { "all", "mixed-unlocks", "unseen-bosses" } : new[] { "all" })
        {
            var characters = BaseGameModelKeys.Characters.All.Skip(count == 4 && seed == "0000000002MY" ? 1 : 0).Take(count).ToArray();
            var unlocks = characters.Select(_ => UnlockState.all.ToSerializable()).ToArray();
            if (unlockCase == "mixed-unlocks") { unlocks[0] = UnlockState.none.ToSerializable(); unlocks[0].NumberOfRuns = 1; }
            if (unlockCase == "unseen-bosses") foreach (var u in unlocks) u.EncountersSeen.Clear();
            var runtimeUnlocks = unlocks.Select(UnlockState.FromSerializable).ToArray();
            var union = new UnlockState(runtimeUnlocks);
            TestMode.IsOn = false;
            var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed, characters, unlocks, 10, "0.111.0");
            Console.WriteLine($"PARTY_WORLD_FIXTURE players={count};seed={seed};unlocks={unlockCase}");
            var observation = PartySeedInformation.Project(seed, party);
            TestMode.IsOn = true;
            var players = characters.Select((key, slot) => Player.CreateForNewRun(ModelDb.AllCharacters.Single(c => c.Id.Entry == key.Entry), runtimeUnlocks[slot], (ulong)(1000 + slot * 17))).ToArray();
            var acts = ActModel.GetRandomList(new Rng(StringHelper.GetDeterministicHashCode(seed), "act_selection"), union, true).Select(a => a.ToMutable()).ToArray();
            var run = RunState.CreateForNewRun(players, acts, [], GameMode.Standard, 10, seed);
            RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), shouldSave: false);
            try
            {
                TestMode.IsOn = false; RunManager.Instance.GenerateRooms(); TestMode.IsOn = true;
                Check(observation.Acts.Select(k => k.Entry).SequenceEqual(run.Acts.Select(a => a.Id.Entry)), "party Acts");
                Check(observation.Bosses.Where(b => b.Ordinal == 1).Select(b => b.BossKey.Entry).SequenceEqual(run.Acts.Select(a => a.BossEncounter.Id.Entry)), "party Bosses");
                Check(observation.Bosses.Single(b => b.Ordinal == 2).BossKey.Entry == run.Acts.Last().SecondBossEncounter!.Id.Entry, "A10 second Boss after discovery replacement");
                string relicsBefore = string.Join("|", players.SelectMany(p => p.Relics.Select(r => r.Id.ToString())));
                int nicheBefore = run.Rng.Niche.ToSerializable().counter;
                int upFrontBefore = run.Rng.UpFront.ToSerializable().counter;
                foreach (var player in players)
                {
                    var neow = ModelDb.Event<Neow>().ToMutable();
                    await neow.BeginEvent(player, null, false);
                    var offers = neow.CurrentOptions.Select(o => o.Relic!.Id.Entry).ToArray();
                    Check(offers.SequenceEqual(observation.Players[run.GetPlayerSlotIndex(player)].Offers.Select(k => k.Entry)), "per-slot real Neow offers");
                }
                Check(relicsBefore == string.Join("|", players.SelectMany(p => p.Relics.Select(r => r.Id.ToString()))) &&
                    nicheBefore == run.Rng.Niche.ToSerializable().counter && upFrontBefore == run.Rng.UpFront.ToSerializable().counter,
                    "offer observation has no pickup/shared continuation");
            }
            finally { RunManager.Instance.CleanUp(); TestMode.IsOn = false; }
            var query = SearchQuery.Empty with
            {
                Players = observation.Players.Select(p => new PlayerOfferQuery(p.Slot, new([], [p.Offers[0]], []))).ToArray(),
                VariantBossBranches = [new(1, observation.Acts[0], new([observation.Bosses[0].BossKey], [], []), ModelKeySetFilter.Empty, false)]
            };
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, characters[0], 10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var compiled = SearchCompiler.Compile(query, context);
            var restoredContext = JsonSerializer.Deserialize<SearchContext>(JsonSerializer.Serialize(context, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
            Check(SearchCompiler.Compile(query, restoredContext).SemanticFingerprint == compiled.SemanticFingerprint, "party context replay JSON roundtrip");
            var request = ExactSearchExecutionRequestFactory.Compile(compiled, new(seed, 1, 1, 1));
            Check(request.Success, "Production compile:" + request.Issue);
            await using var session = FamilyExecutionCoordinator.Start(request.Plan!, gpuAvailable: false);
            await session.Completion;
            Check(session.TryReadCandidate(out var candidate) && candidate?.Document?.Party is not null, "Production result:" + session.GetProgress().FailureCode);
            Check(JsonSerializer.Serialize(candidate!.Document!.Party) == JsonSerializer.Serialize(observation), "Filter/Information parity");
            Check(session.EtaProjection.AcceptedResultProbability == RolltheSpire2.Search.Selectivity.JointSelectivityEstimator
                .EstimateQuery(RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(compiled)).Probability, "whole-table probability authority");
            Check(session.FamilyIds.Count(f => f == "W.World") == 1, "W once per root");
            var duplicate = query with { VariantBossBranches = [query.VariantBossBranches[0], query.VariantBossBranches[0]] };
            Check(SearchCompiler.Compile(duplicate, context).NormalizedQuery.VariantBossBranches.Count == 1, "shared deduplication");
            Reject(() => SearchCompiler.Compile(query with { Players = query.Players.Reverse().ToArray() }, context), "reordered slots");
            Reject(() => SearchCompiler.Compile(query with { Players = query.Players.Select(p => p with { Slot = 0 }).ToArray() }, context), "duplicate slots");
            Reject(() => SearchCompiler.Compile(query with { OpeningRoute = new(BaseGameModelKeys.Relics.NewLeaf) }, context), "pickup not offers");
            Reject(() => observation.RequireSameObservation(observation with { PartyFingerprint = "stale" }), "stale party");
            Reject(() => observation.RequireSameObservation(observation with { Version = "0.110.1" }), "stale version");
            var differentBoss = observation.Bosses.First(b => b.Act == 2).BossKey;
            Check(differentBoss != observation.Bosses[0].BossKey, "Boss contradiction fixture has distinct identities");
            var alternatives = query with { VariantBossBranches = [query.VariantBossBranches[0], query.VariantBossBranches[0] with
                { FirstBoss = new([differentBoss], [], []) }] };
            Check(PartyInitialQuery.MatchesWorld(alternatives, observation), "same-source Boss alternatives preserve matching branch");
            var alternativesPlan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(alternatives, context), new(seed, 1, 1, 1));
            Check(alternativesPlan.Success && ProductionExactSearchEvaluator.Evaluate(alternativesPlan.Plan!,
                TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch,
                "same-source Boss alternatives compile and reach Exact as OR");
            var conflict = query with { VariantBossBranches = [query.VariantBossBranches[0] with
                { FirstBoss = new([], [observation.Bosses[0].BossKey, differentBoss], []) }] };
            Check(!PartyInitialQuery.MatchesWorld(conflict, observation), "single Boss slot cannot satisfy two required identities");
            Check(SearchCompiler.Compile(conflict, context).Status == QueryNormalizationStatus.Impossible, "conflict compile fail-fast");
            var ordinalQuery = query with { VariantBossBranches = [], LegacyWorld = LegacyWorldSemanticConstraints.Empty with
                { BossOrdinalFilters = [new(3, 2, new([observation.Bosses.Single(b => b.Ordinal == 2).BossKey], [], []))] } };
            Check(PartyInitialQuery.MatchesWorld(ordinalQuery, observation), "explicit Boss ordinal projection");
            var ordinalPlan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(ordinalQuery, context), new(seed, 1, 1, 1));
            Check(ordinalPlan.Success && FamilyExecutionCoordinator.CreateRegisteredFamilies(ordinalPlan.Plan!).Count(f => f.FamilyId == "W.World") == 1, "ordinal shared W compile");
            var lost = compiled with { Normalization = compiled.Normalization with { NormalizedQuery = compiled.NormalizedQuery with
                { Players = query.Players.Select(p => p with { Offers = ModelKeySetFilter.Empty }).ToArray() } } };
            Check(!ExactSearchExecutionRequestFactory.Compile(lost, new(seed, 1, 1, 1)).Success, "omitted compiled predicate rejected");
            var onlyWorld = query with { Players = query.Players.Select(p => p with { Offers = ModelKeySetFilter.Empty }).ToArray() };
            var onlyNeow = query with { VariantBossBranches = [] };
            foreach (var shape in new[] { onlyWorld, onlyNeow, query with { Players = query.Players.Select(p => p.Slot == 0 ? p with { Offers = ModelKeySetFilter.Empty } : p).ToArray() } })
            {
                var shapePlan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(shape, context), new(seed, 1, 1, 1));
                Check(shapePlan.Success, "W/N-only compile");
                Check(TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, observation.Root, seed, out var input, out _), "root binding");
                Check(ProductionExactSearchEvaluator.Evaluate(shapePlan.Plan!, input).IsMatch, "W/N-only and unauthored member Exact");
            }
            var draft = new WorkbenchSearchDraft(characters[0], 10, query, AncientOptionConditionProfile.BroadDefault)
            { Players = characters.Select((c, i) => new WorkbenchPlayerDraft(i, c, unlocks[i], "NativeOracleCapturedUnlock")).ToArray(),
                Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer, GameVersion = "0.111.0", Profile = RuntimeProfileId.Beta111 };
            var persistence = new SearchWorkspace(Path.Combine(output, $"persistence-{count}"), false);
            persistence.SaveWorkbench(draft);
            var restored = persistence.LoadWorkbench()!;
            Check(restored.Version == 4 && restored.Players.Count == count && JsonSerializer.Serialize(restored.Query) == JsonSerializer.Serialize(query), "party JSON roundtrip");
            var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance, Detection = detection, Compatibility = RuntimeProfileRegistry.Resolve(detection) };
            Check(restored.Compile(runtime, out _).SemanticFingerprint == draft.Compile(runtime, out _).SemanticFingerprint, "full party fingerprint roundtrip");
            if (count == 2 && unlockCase == "all") uiDraft = draft;
            evidence.Add(new { count, seed, unlockCase, observation, progress = session.GetProgress() });
        }
        var runtimeUi = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance, Detection = detection, Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        // Structural check beyond the UI product range: capture/compile/JSON/fingerprint, no >4P game execution.
        var five = BaseGameModelKeys.Characters.All.Take(5).ToArray();
        var fiveDraft = new WorkbenchSearchDraft(five[0], 10, SearchQuery.Empty with
        { Players = five.Select((c, i) => new PlayerOfferQuery(i, ModelKeySetFilter.Empty)).ToArray() }, AncientOptionConditionProfile.BroadDefault)
        { Players = five.Select((c, i) => new WorkbenchPlayerDraft(i, c, UnlockState.all.ToSerializable(), "StructuralOnly")).ToArray(),
            Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer, GameVersion = "0.111.0", Profile = RuntimeProfileId.Beta111 };
        var fiveStore = new SearchWorkspace(Path.Combine(output, "structural-five"), false);
        fiveStore.SaveWorkbench(fiveDraft);
        var fiveRestored = fiveStore.LoadWorkbench()!;
        Check(fiveRestored.Players.Select(p => p.Slot).SequenceEqual(Enumerable.Range(0, 5)), "five-slot JSON ordering");
        Check(fiveRestored.Compile(runtimeUi, out _).SemanticFingerprint == fiveDraft.Compile(runtimeUi, out _).SemanticFingerprint, "five-slot fingerprint");
        var fiveAuthority = fiveRestored.Compile(runtimeUi, out _).Context.Party!;
        var fiveInformation = PartySeedInformation.Project("000000000040", fiveAuthority);
        Check(fiveInformation.Players.Select(p => p.Slot).SequenceEqual(Enumerable.Range(0, 5)), "five-slot information section ordering (pure projection only)");
        Check(TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, fiveInformation.Root, fiveInformation.Seed, out var fiveInput, out _), "five-slot root metadata");
        Check(SeedPredictionRequest.ForPartyInitialInformation(fiveInput, fiveAuthority, false).PlayersCount == 5, "no four-player legacy result cap");
        Reject(() => (fiveRestored with { GameVersion = "0.110.1" }).Compile(runtimeUi, out _), "stale persisted version");
        Reject(() => (fiveRestored with { Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Singleplayer }).Compile(runtimeUi, out _), "inconsistent persisted mode");
        var oldSp = new WorkbenchSearchDraft(five[0], 10, SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault) { Version = 1 };
        fiveStore.SaveWorkbench(oldSp);
        Check(fiveStore.LoadWorkbench() is { Version: 3, Players.Count: 0 }, "legacy SP schema upgrade");

        // Actual Shell/Workbench authoring, persistence, Start signal, session, result button and information surface.
        T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
        void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
        string uiRoot = Path.Combine(output, "workbench-e2e");
        var uiStore = new SearchWorkspace(uiRoot);

        VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, "0000000002MY", out var ordinal, out _, out _);
        var cursor = Field<SearchCursorDocument>(uiStore, "_cursor"); cursor.NextCursor = ordinal; cursor.PersistentOrigin = ordinal; cursor.Initialized = true;
        uiStore.ObserveSafeNextCursor(ordinal); uiStore.FlushAll();
        var canvas = new Control { Size = new(1600, 900) }; host.AddChild(canvas);
        var shell = new WorkspaceShell(); canvas.AddChild(shell); shell.InitializeWithPersistence(runtimeUi, uiRoot);
        var frame = Field<QueryWorkbenchFrame>(shell, "_references");
        var references = frame;
        Set(frame, "_target", 1);
        Set(frame, "_multiplayer", true);
        var defaultDraft = frame.CaptureDraft();
        Check(defaultDraft.Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"), "offline default full-unlock premise");
        var defaultCompiled = defaultDraft.Compile(runtimeUi, out _);
        Check(defaultCompiled.Context.Party!.Players.Count == 2, "offline full-unlock compile");
        var defaultInfo = PartySeedInformation.Project("0000000002MY", defaultCompiled.Context.Party!);
        Check(defaultInfo.Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"), "information retains assumed premise");
        var capturedEquivalent = defaultDraft with { Players = defaultDraft.Players.Select(p => p with { UnlockSource = "CapturedLobbySlot" }).ToArray() };
        Check(capturedEquivalent.Compile(runtimeUi, out _).SemanticFingerprint != defaultCompiled.SemanticFingerprint, "source premise participates in fingerprint");
        liveDefaultRoundtrip();
        void liveDefaultRoundtrip()
        {
            uiStore.SaveWorkbench(defaultDraft);
            frame.RestoreDraft(uiStore.LoadWorkbench()!);
            Check(frame.CaptureDraft().Compile(runtimeUi, out _).SemanticFingerprint == defaultCompiled.SemanticFingerprint, "offline default persisted premise roundtrip");
        }
        typeof(QueryWorkbenchFrame).GetMethod("ReadLobbyUnlocks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(frame, null);
        Reject(() => frame.CaptureDraft(), "failed explicit lobby read does not silently assume full unlocks");
        typeof(QueryWorkbenchFrame).GetMethod("UseFullyUnlockedParty", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(frame, null);
        Check(frame.CaptureDraft().Players.All(p => p.UnlockSource == "AssumedFullyUnlocked"), "explicit reset to full unlocks");
        uiDraft = uiDraft! with { Players = uiDraft.Players.Select(p => p with { UnlockSource = "AssumedFullyUnlocked" }).ToArray() };
        frame.RestoreDraft(uiDraft); frame.Refresh("en", JsonUiTextProvider.CreateUi13("en"));
        var liveStore = Field<SearchWorkspace>(frame, "_persistence");
        var liveCursor = Field<SearchCursorDocument>(liveStore, "_cursor"); liveCursor.NextCursor = ordinal; liveCursor.Initialized = true;
        Check(frame.CaptureDraft().Players.Count == 2, "Workbench party capture");
        string beforeSeat = frame.CaptureDraft().Compile(runtimeUi, out _).SemanticFingerprint;
        Field<Dictionary<int, Button>>(frame, "_seatButtons")[1].EmitSignal(Button.SignalName.Pressed);
        Check(frame.CaptureDraft().Compile(runtimeUi, out _).SemanticFingerprint == beforeSeat, "UI seat navigation preserves slot authority");
        Field<Dictionary<int, Button>>(frame, "_seatButtons")[0].EmitSignal(Button.SignalName.Pressed);
        var neowEditor = Field<NeowSearchEditor>(frame, "_neowEditor");
        var selectSource = typeof(NeowSearchEditor).GetMethod("OnSourceRelicPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        selectSource.Invoke(neowEditor, [uiDraft!.Query.Players[0].Offers.All[0]]);
        Check(frame.CaptureDraft().Query.Players[0].Conditions.OpeningRoute is not null, "shared N editor authors personal pickup");
        selectSource.Invoke(neowEditor, [uiDraft.Query.Players[0].Offers.All[0]]);
        Check(frame.CaptureDraft().Compile(runtimeUi, out _).SemanticFingerprint == beforeSeat, "shared N editor restores personal predicates");
        var start = Field<Button>(frame, "_start"); Check(!start.Disabled, "Start enabled"); start.EmitSignal(Button.SignalName.Pressed);
        Check(liveStore.Runtime.Receiving, "Workbench Start installs execution in the shared owner");
        while (liveStore.Runtime.Receiving) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        frame._Process(1);
        var results = liveStore.DisplayResults.Select(row => liveStore.ResultAction(row)).ToArray();
        Check(results.Length == 1 && results[0].Live?.Candidate.Document?.Party is not null, "Workbench Production result");
        var resultRows = Field<VBoxContainer>(frame, "_resultRows");
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var analyzeText = Field<RolltheSpire2.Presentation.Localization.IUiTextProvider>(frame, "_text").Get("integration.analyze");
        var open = resultRows.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == analyzeText);
        open.EmitSignal(Button.SignalName.Pressed);
        var information = Field<RolltheSpire2.Ui.Pages.Analysis.AnalysisPage>(shell, "_predictor");
        Check(information.LastDocument?.CanonicalSeed == results[0].Result.Seed && information.LastRequest?.PlayersCount == 2,
            "Filter button opens existing information page with party context");
        Set(frame, "_partyAscension", 9); open.EmitSignal(Button.SignalName.Pressed);
        Check(information.LastRequest?.Ascension == uiDraft!.Ascension, "saved result retains its original authority");
        Set(frame, "_partyAscension", 10); Set(frame, "_target", 1000);
        frame.Refresh("en", JsonUiTextProvider.CreateUi13("en"));
        var cancelButton = Field<Button>(frame, "_start"); cancelButton.EmitSignal(Button.SignalName.Pressed);
        Check(liveStore.Runtime.Receiving, "Workbench restart installs execution in the shared owner");
        cancelButton.EmitSignal(Button.SignalName.Pressed);
        while (liveStore.Runtime.Receiving) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        frame._Process(1);
        Check(liveStore.Runtime.Snapshot.Progress?.State == SearchRunState.Cancelled, "Workbench Stop cancels real session");
        canvas.QueueFree();
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        File.WriteAllText(Path.Combine(output, "party-phase1.json"), JsonSerializer.Serialize(new { checks, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PARTY_PHASE1_PASS checks={checks}");
    }
}
