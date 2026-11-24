using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using Environment = System.Environment;
using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;

internal static partial class PredictorCrystalAssistantChecks
{
    // Frozen inputs contain no paths/witnesses. Only production publications
    // supply selectable options; the exact subsequently chosen operations are
    // recorded independently of solver results for replay against another DLL.
    private sealed record InteractionCase(string Name, string Seed, string Character, string Inventory,
        int ActIndex, int WingCounter, int PickerSeed, int DesiredSelections, string SnapshotFile,
        bool IncludeEnchantments = true, bool AvoidCurse = true);
    private sealed record InteractionManifest(int Schema, string GeneratedUtc, bool RandomSeeds,
        ImmutableArray<string> Seeds, ImmutableArray<InteractionCase> Cases, bool DiagnosticOverrides = false);
    private sealed record InteractionAction(string Kind, ImmutableArray<CrystalRewardOption> Selected,
        CrystalRewardOption? Target = null, bool DeferCompletion = false);
    private sealed record InteractionSequence(int Schema, string Case, int DesiredSelections,
        ImmutableArray<InteractionAction> Actions, string GenerationStatus);
    private sealed record InteractionLanguageCost(ImmutableArray<string> Selected, string? Target, string Verdict,
        int RewardStates, long GeometryNodes, double CompileSeconds, double Seconds, string NegativeKind);
    private sealed record InteractionUiResult(object Receipt, InteractionSequence Sequence, bool Passed, bool WorkersJoined);

    private static async Task RunInteractionLatencyAsync(string output, CharacterModel watcher)
    {
        string? enchantmentsOverride = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_ENCHANTMENTS");
        bool rendered = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_RENDERED") == "1";
        if (rendered)
        {
            Check(DisplayServer.GetName() != "headless", "Rendered interaction requires an actual renderer");
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(CardModel).Assembly);
        }
        Check(enchantmentsOverride is null or "0" or "1", "INTERACTION_ENCHANTMENTS accepts 0 or 1");
        bool includeEnchantments = enchantmentsOverride != "0";
        if (Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_SNAPSHOT") is { Length: > 0 } snapshotFile)
        {
            Check(!rendered, "Rendered interaction requires a native manifest fixture; use USE_CAPTURE");
            var source = ProductionSearchReplay.Read<PredictorCrystalSnapshot>(Path.GetFullPath(snapshotFile));
            string? sequenceFile = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_SEQUENCE");
            var frozenSequence = string.IsNullOrWhiteSpace(sequenceFile) ? null :
                ProductionSearchReplay.Read<InteractionSequence>(Path.GetFullPath(sequenceFile));
            var fixture = new InteractionCase("snapshot-" + source.Context.Character.Entry + "-" + source.Remaining,
                source.Context.Seed, source.Context.Character.Entry, string.Join('+', source.State.Relics.Select(r => r.Key.Entry)),
                0, 0, int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_PICKER_SEED") ?? "42"),
                int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_SELECTIONS") ?? InteractionSelectionUpperBound(source).ToString()), snapshotFile, includeEnchantments);
            var receipt = RunInteractionBoard(output, fixture, source, frozenSequence, out bool snapshotPassed);
            ProductionSearchReplay.Write(Path.Combine(output, "interaction-latency.json"), new {
                schema = 1, status = "complete", singleSnapshot = true, passed = snapshotPassed, receipt,
                productMvid = typeof(PredictorCrystalSnapshot).Module.ModuleVersionId, renderedE2E = false });
            return;
        }
        string? replayDirectory = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_REPLAY");
        string? captureDirectory = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_USE_CAPTURE");
        bool replay = !string.IsNullOrWhiteSpace(replayDirectory);
        Check(!replay || string.IsNullOrWhiteSpace(captureDirectory), "Use either REPLAY or USE_CAPTURE");
        bool useCapture = !string.IsNullOrWhiteSpace(captureDirectory);
        string input = replay ? Path.GetFullPath(replayDirectory!) : useCapture ? Path.GetFullPath(captureDirectory!) : output;
        int count = int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_COUNT") ?? "10");
        Check(count is >= 1 and <= 10, "Interaction count is 1..10; final assessment requires ten");
        string? caseFilter = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_CASE");
        string? specifiedSeed = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_SEED");
        string? specifiedInventory = Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_INVENTORY");
        var manifestPath = Path.Combine(input, "interaction-manifest.json");
        InteractionManifest manifest;
        if (replay || useCapture)
        {
            manifest = ProductionSearchReplay.Read<InteractionManifest>(manifestPath);
            Check(manifest.Schema == 1, "Replay preserves the original seed manifest");
            if (useCapture)
            {
                // The new output remains a self-contained future A/B input.
                manifest = manifest with { DiagnosticOverrides = manifest.DiagnosticOverrides || enchantmentsOverride != null,
                    Cases = manifest.Cases.Select(c => enchantmentsOverride == null ? c : c with { IncludeEnchantments = includeEnchantments }).ToImmutableArray() };
                ProductionSearchReplay.Write(Path.Combine(output, "interaction-manifest.json"), manifest);
                foreach (var fixture in manifest.Cases)
                    File.Copy(Path.Combine(input, fixture.SnapshotFile), Path.Combine(output, fixture.SnapshotFile));
            }
        }
        else
        {
            Check(!File.Exists(manifestPath), "Interaction generation requires a fresh output directory");
            var seeds = new HashSet<string>(StringComparer.Ordinal);
            if (specifiedSeed is { Length: > 0 })
            {
                Check(count == 1, "An explicitly supplied diagnostic seed requires INTERACTION_COUNT=1");
                seeds.Add(specifiedSeed);
            }
            while (seeds.Count < count) seeds.Add(Convert.ToHexString(RandomNumberGenerator.GetBytes(6)));
            string[] inventories = ["", "DRIFTWOOD", "PAELS_WING", "FRESNEL_LENS+WING_CHARM",
                "DRIFTWOOD+WING_CHARM+FRESNEL_LENS", "PAELS_WING+FRESNEL_LENS+GLITTER",
                "DRIFTWOOD+FRESNEL_LENS", "PAELS_WING+WING_CHARM", "GLITTER+WING_CHARM", "DRIFTWOOD+FRESNEL_LENS+WING_CHARM"];
            var cases = ImmutableArray.CreateBuilder<InteractionCase>();
            int index = 0;
            foreach (string seed in seeds)
            {
                string character = index % 2 == 0 ? "IRONCLAD" : watcher.Id.Entry;
                foreach (string mode in new[] { "Three", "Six" })
                {
                    string name = $"seed-{index:D2}-{character}-{mode}";
                    int pickerSeed = RandomNumberGenerator.GetInt32(int.MaxValue);
                    // The reveal budget is not a target-count bound. Derive
                    // the count from this branch's captured callback instances
                    // below, before any solving or measured interaction.
                    int desired = 0;
                    string inventory = specifiedInventory ?? inventories[index];
                    cases.Add(new(name, seed, character, inventory, inventory.Contains("FRESNEL") ||
                        inventory.Contains("WING_CHARM") ? 2 : index % 3,
                        index % 3, pickerSeed, desired, name + "-snapshot.json", includeEnchantments, AvoidCurse: index % 2 == 0));
                }
                index++;
            }
            manifest = new(1, DateTimeOffset.UtcNow.ToString("O"), specifiedSeed == null, seeds.ToImmutableArray(), cases.ToImmutable(),
                specifiedSeed != null || specifiedInventory != null || enchantmentsOverride != null);
            ProductionSearchReplay.Write(manifestPath, manifest);
            // Capture all native boards before any solving. A/B reuses exactly
            // these snapshots, not a regenerated approximation of their RNGs.
            foreach (var group in manifest.Cases.GroupBy(c => c.Seed))
            {
                var fixture = group.First();
                var character = fixture.Character == "IRONCLAD" ? ModelDb.Character<Ironclad>() : watcher;
                var player = Player.CreateForNewRun(character, UnlockState.all, 1); player.Gold = 200;
                var run = RunState.CreateForTest([player], seed: fixture.Seed, ascensionLevel: 10);
                run.CurrentActIndex = fixture.ActIndex;
                const BindingFlags binding = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var state = typeof(RunManager).GetProperty("State", binding)!;
                var ascension = typeof(RunManager).GetProperty("AscensionManager", binding)!;
                var oldRun = state.GetValue(RunManager.Instance); var oldAscension = ascension.GetValue(RunManager.Instance);
                var oldLocal = LocalContext.NetId;
                try
                {
                    state.SetValue(RunManager.Instance, run);
                    ascension.SetValue(RunManager.Instance, new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(10));
                    LocalContext.NetId = player.NetId;
                    foreach (string relic in fixture.Inventory.Split('+', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var model = ModelDb.GetById<RelicModel>(new("RELIC", relic)).ToMutable();
                        if (model is PaelsWing wing) wing.RewardsSacrificed = fixture.WingCounter;
                        player.AddRelicInternal(model, silent: true);
                    }
                    Check(!(player.Relics.Any(r => r is PaelsWing) && player.Relics.Any(r => r is Driftwood)),
                        "Interaction fixtures do not combine Wing and Driftwood");
                    player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
                    run.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), run.Rng.UpFront);
                    var ev = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
                    await ev.BeginEvent(player, null, false);
                    string fingerprint = CrystalSphereLiveCapture.SceneFingerprint(run, player, ev, null);
                    var capture = CrystalSphereLiveCapture.CaptureScene(run, player, ev, null, "0.111.0");
                    foreach (var branch in capture.Branches)
                    {
                        var item = group.Single(c => c.Name.EndsWith("-" + branch.Mode, StringComparison.Ordinal));
                        ProductionSearchReplay.Write(Path.Combine(output, item.SnapshotFile), branch.Snapshot);
                        int upper = InteractionSelectionUpperBound(branch.Snapshot);
                        int desired = item.Name.StartsWith("seed-00-", StringComparison.Ordinal) ? upper :
                            upper > 0 ? RandomNumberGenerator.GetInt32(1, upper + 1) : 0;
                        manifest = manifest with { Cases = manifest.Cases.Select(c => c.Name == item.Name ? c with { DesiredSelections = desired } : c).ToImmutableArray() };
                    }
                    Check(fingerprint == CrystalSphereLiveCapture.SceneFingerprint(run, player, ev, null),
                        "Interaction capture preserves the isolated native state");
                }
                finally
                {
                    state.SetValue(RunManager.Instance, oldRun); ascension.SetValue(RunManager.Instance, oldAscension);
                    LocalContext.NetId = oldLocal;
                }
            }
            ProductionSearchReplay.Write(manifestPath, manifest);
        }
        if (Environment.GetEnvironmentVariable("RT2_CRYSTAL_INTERACTION_CAPTURE_ONLY") == "1")
        {
            ProductionSearchReplay.Write(Path.Combine(output, "interaction-latency.json"), new {
                schema = 1, status = "capture-only", manifest, solverRun = false, finalAssessment = false, renderedE2E = false });
            Console.WriteLine($"CRYSTAL_INTERACTION_CAPTURE seeds={manifest.Seeds.Length};boards={manifest.Cases.Length};solverRun=false");
            return;
        }
        var receipts = new List<object>();
        int passed = 0, renderedPassed = 0, tested = 0;
        var selectedCases = manifest.Cases.Where(c => caseFilter == null || c.Name.Contains(caseFilter, StringComparison.Ordinal)).ToArray();
        foreach (var fixture in selectedCases)
        {
            var source = ProductionSearchReplay.Read<PredictorCrystalSnapshot>(Path.Combine(input, fixture.SnapshotFile));
            var frozen = JsonSerializer.Serialize(source, ProductionSearchReplay.JsonOptions);
            string sequenceFile = Path.Combine(input, fixture.Name + "-sequence.json");
            if (rendered && replay && File.Exists(Path.Combine(input, fixture.Name + "-ui-sequence.json")))
                sequenceFile = Path.Combine(input, fixture.Name + "-ui-sequence.json");
            var sequence = replay ? ProductionSearchReplay.Read<InteractionSequence>(sequenceFile) : null;
            InteractionUiResult? ui = null;
            if (rendered)
            {
                // The real panel generates choices when no full sequence exists;
                // its partial solver diagnostic never limits UI coverage.
                ui = await RunRenderedInteractionBoardAsync(output, fixture, source, sequence, watcher);
                Check(ui.WorkersJoined, "Rendered fixture retained its failure receipt and stops timing after an unjoined worker; later boards must not run under resource contamination");
                sequence = ui.Sequence;
                if (ui.Passed) renderedPassed++;
                ProductionSearchReplay.Write(Path.Combine(output, fixture.Name + "-sequence.json"), sequence);
            }
            var receipt = RunInteractionBoard(output, fixture, source, sequence, out bool complete);
            Check(frozen == JsonSerializer.Serialize(source, ProductionSearchReplay.JsonOptions), "Interaction solving preserves frozen source");
            receipts.Add(new { solver = receipt, rendered = ui?.Receipt }); tested++; if (complete) passed++;
            ProductionSearchReplay.Write(Path.Combine(output, "interaction-latency.json"), new {
                schema = 1, status = tested == selectedCases.Length ? "complete" : "running", replay, input,
                productMvid = typeof(PredictorCrystalSnapshot).Module.ModuleVersionId, runtimeVersion = Environment.Version.ToString(),
                randomSeeds = manifest.Seeds, tested, passed, finalAssessment = !manifest.DiagnosticOverrides && manifest.RandomSeeds && manifest.Seeds.Length == 10 && caseFilter == null,
                solverAssessmentPassed = !manifest.DiagnosticOverrides && manifest.RandomSeeds && manifest.Seeds.Length == 10 && caseFilter == null && tested == 20 && passed == 20,
                rendered, renderedPassed, renderedE2EAssessmentPassed = rendered && !manifest.DiagnosticOverrides && manifest.RandomSeeds &&
                    manifest.Seeds.Length == 10 && caseFilter == null && tested == 20 && renderedPassed == 20,
                scope = "Solver and actual rendered Crystal panel are measured separately against the same snapshot and operations. Rendered measurements include normal Process/FramePostDraw, candidate/Gold display and cancellation cleanup. Native fixture reconstruction/snapshot parity, package startup and receipt IO are outside timing. No launcher-opening or Owner natural-play acceptance claim.", receipts });
        }
        Console.WriteLine($"CRYSTAL_INTERACTION_SUMMARY seeds={manifest.Seeds.Length};boards={tested};passed={passed};solverAssessmentPassed={!manifest.DiagnosticOverrides && manifest.RandomSeeds && manifest.Seeds.Length == 10 && caseFilter == null && tested == 20 && passed == 20};renderedE2E={rendered};renderedPassed={renderedPassed}");
    }

    private static int InteractionSelectionUpperBound(PredictorCrystalSnapshot source)
    {
        static bool Selectable(string kind) => kind == "RELIC" || kind.StartsWith("CARD_", StringComparison.Ordinal) ||
            kind.StartsWith("POTION_", StringComparison.Ordinal);
        // Revealed entries are callbacks, while each future physical item owns
        // its captured subscription multiplicity. Gold/curse are not target
        // options. Wing trades card instances for at most as many gifts, so it
        // cannot increase this conservative count. Duplicate identities and
        // incompatible geometry may lower the attainable count naturally.
        return checked(source.Revealed.Count(i => Selectable(source.Items[i].Kind)) +
            source.Items.Select((item, index) => (item, index)).Where(p => p.item.X >= 0 &&
                !source.Revealed.Contains(p.index) && Selectable(p.item.Kind)).Sum(p => p.item.Subscriptions));
    }

    private static async Task<InteractionUiResult> RunRenderedInteractionBoardAsync(string output, InteractionCase fixture,
        PredictorCrystalSnapshot source, InteractionSequence? frozenSequence, CharacterModel watcher)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var tree = (SceneTree)Engine.GetMainLoop(); var host = tree.Root;
        var character = fixture.Character == "IRONCLAD" ? ModelDb.Character<Ironclad>() : watcher;
        var player = Player.CreateForNewRun(character, UnlockState.all, 1); player.Gold = 200;
        var run = RunState.CreateForTest([player], seed: fixture.Seed, ascensionLevel: 10); run.CurrentActIndex = fixture.ActIndex;
        var stateProperty = typeof(RunManager).GetProperty("State", flags)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", flags)!;
        var syncProperty = typeof(RunManager).GetProperty(nameof(RunManager.EventSynchronizer))!;
        var oldRun = stateProperty.GetValue(RunManager.Instance); var oldAscension = ascensionProperty.GetValue(RunManager.Instance);
        var oldSync = syncProperty.GetValue(RunManager.Instance); var oldLocal = LocalContext.NetId;
        CrystalSphereAssistantPanel? panel = null;
        EventSynchronizer? sync = null;
        bool roomPushed = false;
        var actions = new List<InteractionAction>(); var stages = new List<object>();
        var workers = new HashSet<Task>();
        var languageCosts = new ConcurrentQueue<InteractionLanguageCost>();
        PredictorCrystalReachability? reachability = null;
        var watch = new Stopwatch();
        int budgetMs = source.Remaining > 3 ? 60_000 : 20_000, dailyMs = source.Remaining > 3 ? 30_000 : 10_000;
        int renderedFrames = 0, clicks = 0, pendingFocusClicks = 0, focusAlreadyResolved = 0, largestSelection = 0;
        int removals = 0, reselections = 0;
        var actualSelectionCounts = new HashSet<int> { 0 };
        var actualSelectedKinds = new HashSet<string>(StringComparer.Ordinal);
        string status = "running"; Exception? failure = null; double cleanupMs = 0;
        bool sequenceComplete = false, finalCandidatesComplete = false, finalGoldComplete = false;
        int requestedSelections = frozenSequence?.DesiredSelections ?? fixture.DesiredSelections;
        bool desiredSelectionsReached = false, naturalSelectionLimitReached = false;
        bool nativeSnapshotParity = false, workersJoined = true;
        object? terminalState = null;
        string? fingerprint = null;
        T Field<T>(string name) => (T)typeof(CrystalSphereAssistantPanel).GetField(name, flags)!.GetValue(panel)!;
        ImmutableArray<CrystalRewardOption> Selected() => Field<ImmutableArray<CrystalRewardOption>>("_selected");
        CrystalOptionProjection Projection() => Field<CrystalOptionProjection>("_projection");
        Dictionary<CrystalRewardOption, Control> Tiles() => Field<Dictionary<CrystalRewardOption, Control>>("_optionTiles");
        void RememberWorker()
        {
            if (panel != null && typeof(CrystalSphereAssistantPanel).GetField("_worker", flags)!.GetValue(panel) is Task worker)
                workers.Add(worker);
        }
        void CheckCurrent()
        {
            if (watch.Elapsed.TotalMilliseconds >= budgetMs) throw new OperationCanceledException("Cumulative rendered interaction budget exhausted");
            if (Field<bool>("_stale")) throw new InvalidOperationException("Rendered interaction native scene became stale");
            if (typeof(CrystalSphereAssistantPanel).GetField("_fault", flags)!.GetValue(panel) is Exception error)
                throw new InvalidOperationException("Rendered interaction production panel failed", error);
        }
        async Task Frame()
        {
            CheckCurrent(); RememberWorker();
            await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            // The ordinary engine calls _Process and its bounded queue consumer.
            // Observations occur only after the resulting frame was drawn.
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            renderedFrames++; RememberWorker(); CheckCurrent();
        }
        async Task Wait(Func<bool> condition)
        {
            while (!condition()) await Frame();
        }
        // Match the production presentation domain, which intentionally omits
        // the auxiliary generic-relic fact while retaining it in solver truth.
        bool VisibleCandidate(CrystalRewardOption option) => option != CrystalRewardOption.AnyRelic;
        bool TileReady(CrystalRewardOption option) => VisibleCandidate(option) && Tiles().TryGetValue(option, out var tile) &&
            tile.IsVisibleInTree() && tile.Size.X > 0 && tile.Size.Y > 0 &&
            (tile as Button ?? tile.GetChildren().OfType<Button>().FirstOrDefault()) is { Disabled: false };
        async Task<bool> PressOption(CrystalRewardOption option, bool selected, bool pendingOnly = false)
        {
            Button button;
            if (selected)
            {
                await Wait(() => panel!.IsPresentationReady && Field<VBoxContainer>("_targetRow").IsVisibleInTree());
                int index = Selected().IndexOf(option);
                Check(index >= 0, "Rendered remove target is actually selected");
                button = Field<VBoxContainer>("_targetRow").GetChildren().OfType<Button>().ElementAt(index);
            }
            else
            {
                await Wait(() => TileReady(option));
                var tile = Tiles()[option];
                Field<ScrollContainer>("_rewardScroll").EnsureControlVisible(tile);
                await Frame();
                await Wait(() => TileReady(option) || pendingOnly && !Field<HashSet<CrystalRewardOption>>("_pendingOptions").Contains(option));
                if (pendingOnly && !Field<HashSet<CrystalRewardOption>>("_pendingOptions").Contains(option)) return false;
                button = Tiles()[option] as Button ?? Tiles()[option].GetChildren().OfType<Button>().First();
            }
            Check(button.IsVisibleInTree() && !button.Disabled, "Rendered action uses a visible enabled production button");
            if (pendingOnly && (!Field<HashSet<CrystalRewardOption>>("_pendingOptions").Contains(option) || Projection().Available.ContainsKey(option)))
                return false;
            RememberWorker(); button.EmitSignal(Button.SignalName.Pressed); clicks++; RememberWorker();
            return true;
        }
        async Task CloseSelection()
        {
            await Wait(() => Field<bool>("_complete") && Field<bool>("_candidatesComplete") && Projection().GoldComplete &&
                !Field<bool>("_projecting") && !Field<bool>("_dirty") && !Field<bool>("_switching") && panel!.IsPresentationReady);
            await Frame();
            var projection = Projection(); var selected = Selected();
            var table = typeof(PredictorCrystalReachability).GetField("_table", flags)!.GetValue(reachability) as CrystalReachabilityTable;
            Check(table != null && Field<HashSet<CrystalRewardOption>>("_pendingOptions").Count == 0,
                "Rendered completion has the actual candidate domain and no unresolved UI candidate");
            Check(projection.SelectedPlan != null && projection.GoldUpperBound == projection.Gold,
                "Rendered completion retains a whole-selection witness and certified Gold optimum");
            var summary = Field<Label>("_summary"); var statusLabel = Field<Label>("_status");
            var text = JsonUiTextProvider.CreatePredictorUi13("en");
            Check(summary.IsVisibleInTree() && summary.Size.Y > 0 && summary.Text.Contains(text.Get("predictor.crystal.gold_optimal"), StringComparison.Ordinal),
                "Actual rendered Gold summary states optimality");
            Check(statusLabel.IsVisibleInTree() && statusLabel.Text == text.Get("predictor.crystal.discovery_exhausted"),
                "Actual rendered candidate status states completed discovery");
            Check(projection.Available.Keys.Where(c => VisibleCandidate(c) && !selected.Contains(c)).All(TileReady),
                "Every user-visible published available candidate has its completed actual enabled tile");
            var selectedButtons = Field<VBoxContainer>("_targetRow").GetChildren().OfType<Button>().ToArray();
            Check(selected.IsEmpty || selectedButtons.Length == selected.Length && selectedButtons.All(b => b.IsVisibleInTree() && !b.Disabled),
                "Every selected target has its actual enabled rendered removal button");
            finalCandidatesComplete = true; finalGoldComplete = true;
        }
        void Record(InteractionAction action, double start, string? observation = null)
        {
            var projection = Projection(); var selected = Selected();
            var table = typeof(PredictorCrystalReachability).GetField("_table", flags)!.GetValue(reachability) as CrystalReachabilityTable;
            stages.Add(new { action.Kind, action.Target, selected, observation, ms = watch.Elapsed.TotalMilliseconds - start,
                cumulativeMs = watch.Elapsed.TotalMilliseconds, candidatesComplete = Field<bool>("_candidatesComplete"),
                projection.GoldComplete, projection.Gold, projection.GoldUpperBound, panel!.IsPresentationReady,
                candidateDomainReady = table != null, candidateCount = table?.Candidates.Count(c => !selected.Contains(c)),
                availableKeys = projection.Available.Keys.Select(c => c.CacheKey).Order().ToArray(),
                auxiliaryHiddenAvailableKeys = projection.Available.Keys.Where(c => !VisibleCandidate(c)).Select(c => c.CacheKey).Order().ToArray(),
                pendingUiKeys = Field<HashSet<CrystalRewardOption>>("_pendingOptions").Select(c => c.CacheKey).Order().ToArray(),
                summary = Field<Label>("_summary").Text, status = Field<Label>("_status").Text, renderedFrames });
        }
        async Task Execute(InteractionAction action)
        {
            double start = watch.Elapsed.TotalMilliseconds;
            CheckCurrent(); finalCandidatesComplete = finalGoldComplete = false;
            if (action.Kind == "settle")
            {
                Check(action.Selected.SequenceEqual(Selected()), "Rendered settle retains the exact recorded selection");
                if (typeof(CrystalSphereAssistantPanel).GetField("_focusTarget", flags)!.GetValue(panel) != null)
                {
                    var back = Field<Button>("_allCandidates");
                    Check(back.IsVisibleInTree() && !back.Disabled, "Return to all candidates uses its real control");
                    RememberWorker(); back.EmitSignal(Button.SignalName.Pressed); clicks++; RememberWorker();
                }
                await CloseSelection(); Record(action, start); return;
            }
            if (action.Kind == "focus")
            {
                Check(action.Selected.SequenceEqual(Selected()) && action.Target != null, "Rendered focus preserves the exact selected set");
                await Wait(() => Projection().Available.ContainsKey(action.Target!) || TileReady(action.Target!) || Field<bool>("_candidatesComplete"));
                if (Projection().Available.ContainsKey(action.Target!) || Field<bool>("_candidatesComplete") && !TileReady(action.Target!))
                {
                    focusAlreadyResolved++; Record(action, start, "Frozen focus target already resolved by normal UI scheduling; no artificial grey tile or extra selection");
                    return;
                }
                Check(Field<HashSet<CrystalRewardOption>>("_pendingOptions").Contains(action.Target!), "Actual focus button is currently pending");
                if (!await PressOption(action.Target!, false, pendingOnly: true))
                {
                    focusAlreadyResolved++; Record(action, start, "Focus resolved during normal scroll/presentation frames before click"); return;
                }
                pendingFocusClicks++;
                await Wait(() => Field<CrystalFocusVerdict>("_focusVerdict") != CrystalFocusVerdict.Checking && panel!.IsPresentationReady);
                Check(Selected().SequenceEqual(action.Selected), "Grey focus click never silently selects the target");
                await Frame(); Record(action, start); return;
            }
            Check(action.Target != null, "Rendered selection action has its original target");
            if (action.Kind is "add" or "reselect")
            {
                await Wait(() => Projection().Available.ContainsKey(action.Target!) && TileReady(action.Target!));
                Check(action.Selected.SequenceEqual(Selected().Add(action.Target!)), "Rendered addition preserves every prior target and its order");
                await PressOption(action.Target!, false);
            }
            else if (action.Kind == "remove")
            {
                Check(action.Selected.SequenceEqual(Selected().Remove(action.Target!)), "Rendered removal preserves every other target");
                await PressOption(action.Target!, true);
            }
            else throw new InvalidOperationException("Unknown rendered interaction action: " + action.Kind);
            Check(Selected().SequenceEqual(action.Selected), "Actual production click applies the frozen selection");
            largestSelection = Math.Max(largestSelection, Selected().Length);
            actualSelectionCounts.Add(Selected().Length);
            if (action.Kind is "add" or "reselect") actualSelectedKinds.Add(action.Target!.Kind.ToString());
            if (action.Kind == "remove") removals++;
            if (action.Kind == "reselect") reselections++;
            if (action.DeferCompletion) await Frame(); else await CloseSelection();
            Record(action, start);
        }
        try
        {
            stateProperty.SetValue(RunManager.Instance, run);
            ascensionProperty.SetValue(RunManager.Instance, new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(10));
            LocalContext.NetId = player.NetId;
            foreach (string relic in fixture.Inventory.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var model = ModelDb.GetById<RelicModel>(new("RELIC", relic)).ToMutable();
                if (model is PaelsWing wing) wing.RewardsSacrificed = fixture.WingCounter;
                player.AddRelicInternal(model, silent: true);
            }
            player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
            run.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), run.Rng.UpFront);
            var ev = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable(); await ev.BeginEvent(player, null, false);
            var capture = CrystalSphereLiveCapture.CaptureScene(run, player, ev, null, "0.111.0");
            var branch = capture.Branches.Single(b => b.Snapshot.Remaining == source.Remaining);
            Check(JsonSerializer.Serialize(branch.Snapshot, ProductionSearchReplay.JsonOptions) == JsonSerializer.Serialize(source, ProductionSearchReplay.JsonOptions),
                "Rendered native fixture reproduces the entire frozen snapshot before timing");
            nativeSnapshotParity = true;
            var net = new NetSingleplayerGameService();
            sync = new EventSynchronizer(new RunLocationTargetedMessageBuffer(net), net, run, run, player.NetId, run.Rng.Seed);
            ((List<EventModel>)typeof(EventSynchronizer).GetField("_events", flags)!.GetValue(sync)!).Add(ev);
            syncProperty.SetValue(RunManager.Instance, sync);
            run.PushRoom(new EventRoom(ModelDb.Event<CrystalSphere>())); roomPushed = true;
            fingerprint = CrystalSphereLiveCapture.Fingerprint(run, host);
            Check(fingerprint == capture.Fingerprint, "Rendered panel binds the actual unchanged current player/event");
            var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
                Detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "CrystalInteraction", true, "") };
            watch.Start();
            panel = new CrystalSphereAssistantPanel { Size = new Vector2(1600, 900) };
            workersJoined = false;
            panel.Initialize(run, runtime); panel.ApplyLanguage("en");
            Field<CheckButton>("_avoidCurse").SetPressedNoSignal(fixture.AvoidCurse);
            Field<CheckButton>("_includeDriftwood").SetPressedNoSignal(true);
            Field<CheckButton>("_includeEnchantments").SetPressedNoSignal(fixture.IncludeEnchantments);
            host.Size = new Vector2I(1600, 900); host.AddChild(panel);
            panel.ShowSnapshot(capture with { Branches = [branch] }, startDiscovery: true);
            reachability = Field<PredictorCrystalReachability>("_reachability");
            reachability.LanguageResultObserved = (selection, target, result) => languageCosts.Enqueue(new(
                selection.Select(g => g.CacheKey).ToImmutableArray(), target?.CacheKey, result.Verdict.ToString(),
                result.RewardStates, result.GeometryNodes, result.CompileSeconds, result.Seconds, result.NegativeKind.ToString()));
            await Wait(() => Projection().Available.Keys.Any(TileReady));
            Record(new("cold-first-visible-option", []), 0);
            if (frozenSequence != null)
            {
                foreach (var action in frozenSequence.Actions) { actions.Add(action); await Execute(action); }
                sequenceComplete = frozenSequence.GenerationStatus == "complete" && actions.Any(a => a.Kind == "add");
            }
            else
            {
                var picker = new Random(fixture.PickerSeed);
                for (int index = 0; index < requestedSelections; index++)
                {
                    var selected = Selected();
                    var options = Projection().Available.Keys.Where(c => !selected.Contains(c) && c.Key.IsValid && TileReady(c)).ToArray();
                    if (options.Length == 0)
                    {
                        Check(finalCandidatesComplete && finalGoldComplete && Field<bool>("_complete") && panel!.IsPresentationReady &&
                            !Projection().Available.Keys.Any(c => !selected.Contains(c) && c.Key.IsValid),
                            "Natural target-count stop requires a completed domain and no remaining selectable target");
                        naturalSelectionLimitReached = true;
                        Record(new("natural-selection-limit", selected), watch.Elapsed.TotalMilliseconds,
                            "No compatible selectable target remains after complete candidate and Gold proof");
                        break;
                    }
                    var kind = new[] { PredictorRewardKind.Card, PredictorRewardKind.Relic, PredictorRewardKind.Potion }
                        [(actions.Count + Math.Abs(fixture.PickerSeed % 3)) % 3];
                    var preferred = options.Where(c => c.Kind == kind).ToArray();
                    var pool = (preferred.Length > 0 ? preferred : options).OrderBy(c => c.CacheKey, StringComparer.Ordinal).ToArray();
                    var next = pool[picker.Next(pool.Length)];
                    var add = new InteractionAction("add", selected.Add(next), next, DeferCompletion: index == 0);
                    actions.Add(add); await Execute(add);
                    if (index == 0)
                    {
                        await Wait(() => Field<HashSet<CrystalRewardOption>>("_pendingOptions").Any(TileReady) ||
                            Field<bool>("_candidatesComplete") && panel!.IsPresentationReady);
                        var pending = Field<HashSet<CrystalRewardOption>>("_pendingOptions").Where(TileReady).OrderBy(c => c.CacheKey, StringComparer.Ordinal).ToArray();
                        if (pending.Length > 0)
                        {
                            var focus = new InteractionAction("focus", Selected(), pending[picker.Next(pending.Length)]);
                            actions.Add(focus); await Execute(focus);
                        }
                        var settle = new InteractionAction("settle", Selected()); actions.Add(settle); await Execute(settle);
                        var remove = new InteractionAction("remove", Selected().Remove(next), next); actions.Add(remove); await Execute(remove);
                        var reselect = new InteractionAction("reselect", Selected().Add(next), next); actions.Add(reselect); await Execute(reselect);
                    }
                }
                sequenceComplete = actions.Any(a => a.Kind == "add");
            }
            desiredSelectionsReached = largestSelection >= requestedSelections;
            naturalSelectionLimitReached |= !desiredSelectionsReached && finalCandidatesComplete && finalGoldComplete &&
                !Projection().Available.Keys.Any(c => !Selected().Contains(c) && c.Key.IsValid);
            sequenceComplete &= desiredSelectionsReached || naturalSelectionLimitReached;
            Check(CrystalSphereLiveCapture.Fingerprint(run, host) == fingerprint, "Rendered interaction leaves native run/player/event unchanged");
            status = sequenceComplete && finalCandidatesComplete && finalGoldComplete ? "complete" : "sequence-incomplete";
        }
        catch (OperationCanceledException error) { status = "cumulative-budget-exhausted"; failure = error; }
        catch (Exception error) { status = "failed"; failure = error; }
        finally
        {
            double cleanupStart = watch.Elapsed.TotalMilliseconds;
            try
            {
                if (panel != null)
                {
                    var closingProjection = Projection();
                    terminalState = new { selected = Selected(), closingProjection.Gold, closingProjection.GoldUpperBound,
                        closingProjection.GoldComplete, selectedWitness = closingProjection.SelectedPlan != null,
                        availableKeys = closingProjection.Available.Keys.Select(c => c.CacheKey).Order().ToArray(),
                        candidatesComplete = Field<bool>("_candidatesComplete"), complete = Field<bool>("_complete"),
                        pendingUiKeys = Field<HashSet<CrystalRewardOption>>("_pendingOptions").Select(c => c.CacheKey).Order().ToArray(),
                        focusTarget = typeof(CrystalSphereAssistantPanel).GetField("_focusTarget", flags)!.GetValue(panel),
                        focusVerdict = Field<CrystalFocusVerdict>("_focusVerdict"), panel.IsPresentationReady };
                    RememberWorker(); panel.SuspendSession();
                    var joins = Task.WhenAll(workers.Select(async worker => { try { await worker; } catch (OperationCanceledException) { } }));
                    Check(await Task.WhenAny(joins, Task.Delay(5000)) == joins, "All actual panel workers join after close");
                    await joins;
                    if (reachability != null) await Task.Run(() => JoinOverallWorkers(reachability));
                    workersJoined = true;
                    panel.Free(); panel = null;
                    await host.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    renderedFrames++;
                }
                if (fingerprint != null) Check(CrystalSphereLiveCapture.Fingerprint(run, host) == fingerprint,
                    "Rendered cancellation and panel close preserve the native scene");
            }
            catch (Exception error) { status = "failed-worker-cleanup"; failure = failure == null ? error : new AggregateException(failure, error); }
            finally
            {
                if (panel != null) panel.Free();
                cleanupMs = watch.Elapsed.TotalMilliseconds - cleanupStart; watch.Stop();
                if (roomPushed) run.PopCurrentRoom();
                syncProperty.SetValue(RunManager.Instance, oldSync); sync?.Dispose();
                stateProperty.SetValue(RunManager.Instance, oldRun); ascensionProperty.SetValue(RunManager.Instance, oldAscension);
                LocalContext.NetId = oldLocal;
            }
        }
        var sequence = new InteractionSequence(1, fixture.Name, requestedSelections, actions.ToImmutableArray(), status);
        ProductionSearchReplay.Write(Path.Combine(output, fixture.Name + "-ui-sequence.json"), sequence);
        bool passed = status == "complete" && watch.Elapsed.TotalMilliseconds <= budgetMs;
        var receipt = new { fixture, status, passed, totalMs = watch.Elapsed.TotalMilliseconds, budgetMs, dailyMs,
            dailyTargetPassed = passed && watch.Elapsed.TotalMilliseconds <= dailyMs, cleanupMs, renderedFrames,
            clicks, pendingFocusClicks, focusAlreadyResolved, largestSelection, sequenceComplete,
            requestedSelections, selectionUpperBound = InteractionSelectionUpperBound(source), desiredSelectionsReached, naturalSelectionLimitReached,
            selectionCounts = actualSelectionCounts.Order().ToArray(), selectedKinds = actualSelectedKinds.Order().ToArray(),
            removals, reselections,
            finalCandidatesComplete, finalGoldComplete, stages, languageCosts = languageCosts.ToArray(),
            terminalState,
            actualWorker = reachability != null, manualDrain = false, manualProcess = false, nativeSnapshotParityBeforeTiming = nativeSnapshotParity,
            workersJoined,
            nativeCurrentScene = fingerprint != null, error = failure?.ToString(),
            scope = "Actual Crystal panel from Initialize/ShowSnapshot through normal Process/FramePostDraw and close/join; same native snapshot and exact recorded operations; launcher opening excluded" };
        ProductionSearchReplay.Write(Path.Combine(output, fixture.Name + "-ui-latency.json"), receipt);
        Console.WriteLine($"CRYSTAL_INTERACTION_UI case={fixture.Name};ms={watch.Elapsed.TotalMilliseconds:F3};pass={passed};frames={renderedFrames};clicks={clicks};pendingFocusClicks={pendingFocusClicks};alreadyResolvedFocus={focusAlreadyResolved};status={status};actualWorker={reachability != null};manualDrain=false");
        return new(receipt, sequence, passed, workersJoined);
    }

    private static object RunInteractionBoard(string output, InteractionCase fixture, PredictorCrystalSnapshot source,
        InteractionSequence? frozenSequence, out bool passed)
    {
        bool replay = frozenSequence != null;
        int budgetMs = source.Remaining > 3 ? 60_000 : 20_000;
        int dailyMs = source.Remaining > 3 ? 30_000 : 10_000;
        var actions = new List<InteractionAction>();
        var stages = new List<object>();
        var stageLog = new List<string>();
        var picker = new Random(fixture.PickerSeed);
        var watch = Stopwatch.StartNew();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMilliseconds(budgetMs));
        var selectionCancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var session = new PredictorCrystalReachability(source, fixture.AvoidCurse, potionScenarioBound: true,
            workers: source.Remaining > 3 ? Math.Clamp(Environment.ProcessorCount - 1, 1, 4) : 1,
            targetDirected: source.Remaining > 3, includeRerolls: true, goalLanguageProof: true, includeEnchantments: fixture.IncludeEnchantments);
        var languageCosts = new ConcurrentQueue<InteractionLanguageCost>();
        int observedLanguage = 0;
        session.LanguageResultObserved = (selection, target, result) => languageCosts.Enqueue(new(
            selection.Select(g => g.CacheKey).ToImmutableArray(), target?.CacheKey, result.Verdict.ToString(),
            result.RewardStates, result.GeometryNodes, result.CompileSeconds, result.Seconds, result.NegativeKind.ToString()));
        var tableField = typeof(PredictorCrystalReachability).GetField("_table", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ImmutableArray<CrystalRewardOption> selected = [];
        var projection = session.PeekKnown(selected);
        var excluded = new HashSet<CrystalRewardOption>();
        bool candidatesComplete = false, focusAttempted = false, focusResolved = false, sequenceComplete = false;
        double candidateMs = 0, goldMs = 0, focusMs = 0, cleanupMs = 0;
        string status = "running";
        Exception? failure = null;
        int largestSelection = 0;
        var actualSelectionCounts = new HashSet<int> { 0 };
        var actualSelectedKinds = new HashSet<string>(StringComparer.Ordinal);
        int requestedSelections = frozenSequence?.DesiredSelections ?? fixture.DesiredSelections;
        bool desiredSelectionsReached = false, naturalSelectionLimitReached = false;
        ImmutableArray<CrystalRewardOption> Candidates() => (tableField.GetValue(session) as CrystalReachabilityTable)?.Candidates
            .Where(c => !selected.Contains(c)).ToImmutableArray() ?? [];
        TimeSpan Slice(int milliseconds) => TimeSpan.FromMilliseconds(Math.Max(0, Math.Min(milliseconds, budgetMs - watch.Elapsed.TotalMilliseconds)));
        void Record(string kind, CrystalRewardOption? target, double start)
        {
            var candidates = Candidates();
            var pending = candidates.Where(c => !projection.Available.ContainsKey(c) && !excluded.Contains(c)).ToArray();
            Check(!excluded.Any(projection.Available.ContainsKey), "Interaction candidate positive/exclusion consistency");
            Check(!candidatesComplete || tableField.GetValue(session) != null && pending.Length == 0,
                "Complete interaction stage has a real production domain and resolves every candidate");
            var costs = languageCosts.ToArray().Skip(observedLanguage).ToArray();
            observedLanguage += costs.Length;
            stages.Add(new { kind, selected, target, ms = watch.Elapsed.TotalMilliseconds - start,
                cumulativeMs = watch.Elapsed.TotalMilliseconds, candidatesComplete, projection.GoldComplete, projection.Gold, projection.GoldUpperBound,
                selectedWitness = projection.SelectedPlan != null, candidateCount = candidates.Length,
                candidateDomainReady = tableField.GetValue(session) != null,
                availableKeys = candidates.Where(projection.Available.ContainsKey).Select(c => c.CacheKey).Order().ToArray(),
                excludedKeys = excluded.Select(c => c.CacheKey).Order().ToArray(), pendingKeys = pending.Select(c => c.CacheKey).Order().ToArray(),
                language = new { queries = costs.Length, rewardStates = costs.Sum(c => c.RewardStates), geometryNodes = costs.Sum(c => c.GeometryNodes),
                    compileSeconds = costs.Sum(c => c.CompileSeconds), seconds = costs.Sum(c => c.Seconds), costs } });
            stageLog.Add($"CRYSTAL_INTERACTION_STAGE case={fixture.Name};action={kind};selected={selected.Length};cumulativeMs={watch.Elapsed.TotalMilliseconds:F3};pending={pending.Length};complete={candidatesComplete};goldComplete={projection.GoldComplete}");
        }
        void CandidateSlice()
        {
            double start = watch.Elapsed.TotalMilliseconds;
            var update = session.Advance(selected, Slice(150), selectionCancel.Token, values => excluded.UnionWith(values));
            candidateMs += watch.Elapsed.TotalMilliseconds - start;
            candidatesComplete = update.Complete; excluded.UnionWith(update.ExcludedCandidates);
            projection = session.PeekKnown(selected);
        }
        void CloseSelection()
        {
            while (watch.Elapsed.TotalMilliseconds < budgetMs && (!candidatesComplete || !projection.GoldComplete))
            {
                if (!projection.GoldComplete)
                {
                    double start = watch.Elapsed.TotalMilliseconds;
                    session.AdvanceGold(selected, Slice(75), selectionCancel.Token);
                    goldMs += watch.Elapsed.TotalMilliseconds - start; projection = session.PeekKnown(selected);
                }
                if (!candidatesComplete && watch.Elapsed.TotalMilliseconds < budgetMs) CandidateSlice();
            }
            if (!candidatesComplete || !projection.GoldComplete) throw new OperationCanceledException("Cumulative interaction budget exhausted");
            Check(projection.SelectedPlan != null, "Natural selected options retain a whole-selection witness");
            Check(projection.GoldUpperBound == projection.Gold, "Completed gold optimum closes its bound");
        }
        CrystalRewardOption? PickAvailable()
        {
            var options = projection.Available.Keys.Where(c => !selected.Contains(c) && c.Key.IsValid).ToArray();
            var kinds = new[] { PredictorRewardKind.Card, PredictorRewardKind.Relic, PredictorRewardKind.Potion };
            var preferred = kinds[(actions.Count + Math.Abs(fixture.PickerSeed % 3)) % kinds.Length];
            var sameKind = options.Where(c => c.Kind == preferred).OrderBy(c => c.CacheKey, StringComparer.Ordinal).ToArray();
            var pool = sameKind.Length > 0 ? sameKind : options.OrderBy(c => c.CacheKey, StringComparer.Ordinal).ToArray();
            return pool.Length == 0 ? null : pool[picker.Next(pool.Length)];
        }
        void Execute(InteractionAction action)
        {
            double start = watch.Elapsed.TotalMilliseconds;
            if (action.Kind == "settle")
            {
                Check(action.Selected.SequenceEqual(selected), "Settle keeps the recorded selected set");
                CloseSelection(); Record(action.Kind, null, start); return;
            }
            if (action.Kind == "focus")
            {
                Check(action.Selected.SequenceEqual(selected), "Focus keeps the recorded selected set");
                Check(action.Target != null && Candidates().Contains(action.Target), "Recorded focus belongs to the production candidate domain");
                if (!replay) Check(!projection.Available.ContainsKey(action.Target!) && !excluded.Contains(action.Target!),
                    "Generated focus starts on a currently pending production candidate");
                focusAttempted = true;
                CrystalFocusUpdate result;
                do
                {
                    double sliceStart = watch.Elapsed.TotalMilliseconds;
                    result = session.AdvanceFocused(selected, action.Target!, Slice(150), selectionCancel.Token);
                    focusMs += watch.Elapsed.TotalMilliseconds - sliceStart;
                    projection = session.PeekKnown(selected);
                } while (result.Verdict == CrystalFocusVerdict.Checking && watch.Elapsed.TotalMilliseconds < budgetMs);
                focusResolved = result.Verdict != CrystalFocusVerdict.Checking;
                if (result.Verdict == CrystalFocusVerdict.Unreachable) excluded.Add(action.Target!);
                Record(action.Kind, action.Target, start);
                if (!focusResolved) throw new OperationCanceledException("Pending focus exhausted cumulative budget");
                return;
            }
            if (action.Kind == "add" || action.Kind == "reselect")
            {
                Check(action.Target != null && projection.Available.ContainsKey(action.Target) && !selected.Contains(action.Target),
                    "Only a production-published reachable option can be selected");
                Check(action.Selected.SequenceEqual(selected.Add(action.Target!)), "Recorded addition preserves the exact prior selection and order");
            }
            else if (action.Kind == "remove")
                Check(action.Target != null && selected.Contains(action.Target) && action.Selected.SequenceEqual(selected.Remove(action.Target)),
                    "Recorded removal preserves all other authored targets");
            else throw new InvalidOperationException("Unknown interaction action: " + action.Kind);
            selectionCancel.Cancel(); selectionCancel.Dispose();
            selectionCancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            selected = action.Selected;
            largestSelection = Math.Max(largestSelection, selected.Length);
            actualSelectionCounts.Add(selected.Length);
            if (action.Kind is "add" or "reselect") actualSelectedKinds.Add(action.Target!.Kind.ToString());
            candidatesComplete = false; excluded.Clear();
            var knownOptions = Candidates();
            excluded.UnionWith(session.RootCardExclusions(selected, knownOptions));
            projection = session.ProjectKnown(selected, selectionCancel.Token);
            if (action.DeferCompletion) CandidateSlice();
            else CloseSelection();
            Record(action.Kind, action.Target, start);
        }
        try
        {
            double coldStart = watch.Elapsed.TotalMilliseconds;
            // Empty discovery yields at its first published option. In
            // particular it cannot consume the whole allowance proving an
            // unconstrained board before the first user choice is permitted.
            while (projection.Available.Count == 0 && watch.Elapsed.TotalMilliseconds < budgetMs) CandidateSlice();
            Record("cold-first-option", null, coldStart);
            if (projection.Available.Count == 0) throw new OperationCanceledException("No selectable production option before cumulative cutoff");
            if (replay)
            {
                foreach (var action in frozenSequence!.Actions)
                {
                    // A faster/slower A/B implementation must publish the
                    // same first option before applying the frozen click.
                    if (action.Kind is "add" or "reselect")
                        while (!projection.Available.ContainsKey(action.Target!) && !candidatesComplete && watch.Elapsed.TotalMilliseconds < budgetMs)
                            CandidateSlice();
                    actions.Add(action); Execute(action);
                }
                sequenceComplete = frozenSequence.GenerationStatus == "complete";
            }
            else
            {
                for (int index = 0; index < requestedSelections; index++)
                {
                    var next = PickAvailable();
                    // Exhausted compatible additions is an actual selectable
                    // set limit. Record it rather than fabricating a target.
                    if (next == null)
                    {
                        Check(candidatesComplete && projection.GoldComplete && tableField.GetValue(session) != null,
                            "Natural solver target-count stop requires the entire candidate domain and Gold proof");
                        naturalSelectionLimitReached = true;
                        Record("natural-selection-limit", null, watch.Elapsed.TotalMilliseconds);
                        break;
                    }
                    var add = new InteractionAction("add", selected.Add(next), next, DeferCompletion: index == 0);
                    actions.Add(add); Execute(add);
                    if (index == 0)
                    {
                        // Choose the first published option before asking a
                        // pending focus. Thus a difficult unconstrained focus
                        // cannot prevent the first actual selection.
                        var pending = Candidates().Where(c => !projection.Available.ContainsKey(c) && !excluded.Contains(c))
                            .OrderBy(c => c.CacheKey, StringComparer.Ordinal).ToArray();
                        if (pending.Length > 0)
                        {
                            var focus = new InteractionAction("focus", selected, pending[picker.Next(pending.Length)]);
                            actions.Add(focus); Execute(focus);
                        }
                        var settle = new InteractionAction("settle", selected);
                        actions.Add(settle); Execute(settle);
                        var remove = new InteractionAction("remove", selected.Remove(next), next);
                        actions.Add(remove); Execute(remove);
                        var reselect = new InteractionAction("reselect", selected.Add(next), next);
                        actions.Add(reselect); Execute(reselect);
                    }
                }
                sequenceComplete = actions.Any(a => a.Kind == "add");
            }
            desiredSelectionsReached = largestSelection >= requestedSelections;
            naturalSelectionLimitReached |= !desiredSelectionsReached && candidatesComplete && projection.GoldComplete &&
                tableField.GetValue(session) != null && !projection.Available.Keys.Any(c => !selected.Contains(c) && c.Key.IsValid);
            sequenceComplete &= desiredSelectionsReached || naturalSelectionLimitReached;
            status = sequenceComplete ? "complete" : "sequence-incomplete";
        }
        catch (OperationCanceledException error) { status = "cumulative-budget-exhausted"; failure = error; }
        catch (Exception error) { status = "failed"; failure = error; }
        finally
        {
            lifetime.Cancel(); selectionCancel.Cancel();
            double cleanupStart = watch.Elapsed.TotalMilliseconds;
            try { JoinOverallWorkers(session); }
            catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); status = "failed-worker-cleanup"; }
            cleanupMs = watch.Elapsed.TotalMilliseconds - cleanupStart;
            selectionCancel.Dispose(); watch.Stop();
        }
        double totalMs = watch.Elapsed.TotalMilliseconds;
        passed = status == "complete" && sequenceComplete && candidatesComplete && projection.GoldComplete && totalMs <= budgetMs;
        if (!replay) ProductionSearchReplay.Write(Path.Combine(output, fixture.Name + "-sequence.json"),
            new InteractionSequence(1, fixture.Name, requestedSelections, actions.ToImmutableArray(), status));
        var candidates = Candidates();
        foreach (string line in stageLog) Console.WriteLine(line);
        Console.WriteLine($"CRYSTAL_INTERACTION_BOARD case={fixture.Name};ms={totalMs:F3};budgetMs={budgetMs};pass={passed};maxSelected={largestSelection};desired={fixture.DesiredSelections};focus={focusAttempted};status={status};renderedE2E=false");
        return new { fixture, replay, status, passed, totalMs, budgetMs, dailyMs, dailyTargetPassed = passed && totalMs <= dailyMs,
            candidateMs, goldMs, focusMs, cleanupMs, largestSelection, requestedSelections, selectionUpperBound = InteractionSelectionUpperBound(source),
            desiredSelectionsReached, naturalSelectionLimitReached,
            selectionCounts = actualSelectionCounts.Order().ToArray(), selectedKinds = actualSelectedKinds.Order().ToArray(),
            focusAttempted, focusResolved, actions, stages, languageCosts = languageCosts.ToArray(), candidatesComplete, projection.GoldComplete,
            candidateCount = candidates.Length, pendingKeys = candidates.Where(c => !projection.Available.ContainsKey(c) && !excluded.Contains(c)).Select(c => c.CacheKey).Order().ToArray(),
            error = failure?.ToString(), renderedE2E = false, injectedWitnesses = 0 };
    }
}
