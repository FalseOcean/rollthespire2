using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static bool _transformConfirmationInjectDisplayFailure;
    private static bool _transformConfirmationInjectSourceFailure;
    private static void TransformConfirmationModelFixture()
    {
        if (_transformConfirmationInjectDisplayFailure)
            throw new InvalidOperationException("TransformConfirmationInjectedDisplayFailure");
    }

    private static void TransformConfirmationSourceFixture()
    {
        if (_transformConfirmationInjectSourceFailure)
            throw new InvalidOperationException("TransformConfirmationInjectedSourceDiscoveryFailure");
    }

    // A real suspended reward-obtain and native transform confirmation, mounted
    // in the existing isolated UI host. No true transformation is confirmed.
    private static async Task RunTransformConfirmationChecks(Node root, string output, ModRuntimeSnapshot runtime,
        RunState run, Player player)
    {
        int checks = 0, demoCalls = 0, disabledDemoCalls = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException("TransformConfirmationUi: " + message); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string message)
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && wait.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            Check(ready(), message);
        }
        string RunSignature() => JsonSerializer.Serialize(new
        {
            players = run.Players.Select(value => value.ToSerializable()), rng = run.Rng.ToSerializable(),
            shared = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag), personal = PredictorLiveRunCapture.Bag(player.RelicGrabBag)
        });
        static string Face(CardModel model) => JsonSerializer.Serialize(model.ToSerializable());
        var receipt = Path.Combine(output, "transform-confirmation-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        var overlays = NOverlayStack.Instance!;
        var reward = new RelicReward(ModelDb.Relic<Astrolabe>().ToMutable(), player);
        var rewards = new RewardsSet(player).WithCustomRewards([reward]); await rewards.GenerateWithoutOffering();
        _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(rewards);
        var rewardScreen = NRewardsScreen.ShowScreen(rewards, false, run); await Frames(4);
        var selection = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
        await Until(() => overlays.Peek() is NDeckTransformSelectScreen || selection.IsCompleted,
            "real Astrolabe reward opens its native transform picker");
        Check(!selection.IsCompleted && overlays.Peek() is NDeckTransformSelectScreen,
            "real obtain is suspended before any actual transformation");
        var screen = (NDeckTransformSelectScreen)overlays.Peek()!;
        await Frames(8);
        var native = PredictorOperationCapture.Read<NTransformPreview>(screen, "_transformPreview");
        var selected = PredictorOperationCapture.Read<HashSet<CardModel>>(screen, "_selectedCards");
        var candidates = PredictorOperationCapture.Read<IReadOnlyList<CardModel>>(screen, "_cards");
        var originals = candidates.DistinctBy(card => card.Id).Concat(candidates).Distinct().Take(3).ToArray();
        Check(originals.Length == 3, "fixture has three actual selectable originals");
        var hoverCases = new List<object>();
        await HoverOriginal(originals[0], "astrolabe-none-selected", 1);
        await HoverOriginal(originals[0], "astrolabe-same-card-warm", 1);
        await HoverOriginal(originals[1], "astrolabe-switch-b", 1);
        await HoverOriginal(originals[0], "astrolabe-return-a", 1);
        var moltenEgg = ModelDb.Relic<MoltenEgg>().ToMutable();
        player.AddRelicInternal(moltenEgg, silent: true);
        // Two actual fixture selections bound the complex branch to three
        // complete tuples while retaining its full publication capture guard.
        foreach (var card in originals.Take(2))
            AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [card]);
        try { await HoverOriginal(originals[2], "astrolabe-molten-egg-full-capture", 3, direct: false); }
        finally
        {
            AccessTools.Method(typeof(NDeckTransformSelectScreen), "CancelSelection").Invoke(screen, [null]);
            player.RemoveRelicInternal(moltenEgg, silent: true);
        }
        var beforeSelection = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, NGame.Instance, out string beforeStamp);
        Check(beforeStamp == PredictorOperationCapture.ObservationStamp(run, beforeSelection.Context.Catalog!, NGame.Instance),
            "capture observation stamp equals an independent fresh native observation");
        var capturedCatalog = beforeSelection.Context.Catalog!;
        Check(capturedCatalog.CharacterCards.Select(card => card.CardKey).SequenceEqual(player.Character.CardPool
                .GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint).Select(PredictorLiveRunCapture.Key)) &&
            capturedCatalog.ColorlessCards.Select(card => card.CardKey).SequenceEqual(ModelDb.CardPool<ColorlessCardPool>()
                .GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint).Select(PredictorLiveRunCapture.Key)) &&
            capturedCatalog.TransformationPools.All(pool => pool.Cards.SequenceEqual(ModelDb.GetById<CardPoolModel>(
                new(pool.Key.Category, pool.Key.Entry)).GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint)
                .Select(PredictorLiveRunCapture.Key))),
            "same-capture pool copies retain independent native membership and order for reward and transformation pools");
        string beforeDirect = PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen, true)!;
        Check(beforeDirect == PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
            "direct Astrolabe initial signature reuses captured values without changing its current signature");
        var selectionBaseline = PredictorOperationCapture.CaptureSelection(run, runtime.Detection.NormalizedVersion,
            screen, NGame.Instance, out _, out bool selectionDirect);
        var reuseTiming = new PredictorOperationCapture.CaptureTiming();
        var selectionAgain = PredictorOperationCapture.CaptureSelection(run, runtime.Detection.NormalizedVersion,
            screen, NGame.Instance, out _, out bool reusedDirect, reuseTiming);
        Check(selectionDirect && reusedDirect && ReferenceEquals(selectionBaseline, selectionAgain) && reuseTiming.SnapshotReuses == 1 &&
            reuseTiming.CatalogLevelReads == 0 && reuseTiming.DirectPoolReads > 0 && reuseTiming.ContentObservationReads > 0,
            "unchanged selector reuses frozen input only after rereading actual ordered pools, content identities, source and dynamic state");
        string beforeMutation = RunSignature();
        var originalRng = run.Rng.Niche.ToSerializable();
        var changedRng = new MegaCrit.Sts2.Core.Random.Rng(originalRng); changedRng.NextInt();
        try
        {
            // Explicit mutation injection only on this isolated host fixture;
            // the native live stream is restored before another process frame.
            run.Rng.Niche.LoadFromSerializable(changedRng.ToSerializable());
            Check(beforeDirect != PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
                "direct Astrolabe signature rejects an intervening native RNG change");
            Check(!PredictorOperationCapture.IsCurrent(beforeSelection, run, runtime.Detection.NormalizedVersion, NGame.Instance),
                "complete publication capture also rejects an intervening native RNG change");
            var changedTiming = new PredictorOperationCapture.CaptureTiming();
            var changedSelection = PredictorOperationCapture.CaptureSelection(run, runtime.Detection.NormalizedVersion,
                screen, NGame.Instance, out _, out _, changedTiming);
            Check(!ReferenceEquals(changedSelection, selectionBaseline) && changedTiming.SnapshotReuses == 0 && changedTiming.CatalogLevelReuses > 0,
                "real RNG change rebuilds current selector inputs while reusing only immutable level definitions");
        }
        finally { run.Rng.Niche.LoadFromSerializable(originalRng); }
        var moved = player.Deck.Cards[0];
        player.Deck.RemoveInternal(moved, true); player.Deck.AddInternal(moved, silent: true);
        try
        {
            Check(beforeDirect != PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
                "direct Astrolabe signature rejects an intervening native deck-order change");
            Check(!PredictorOperationCapture.IsCurrent(beforeSelection, run, runtime.Detection.NormalizedVersion, NGame.Instance),
                "complete publication capture also rejects an intervening native deck-order change");
        }
        finally { player.Deck.RemoveInternal(moved, true); player.Deck.AddInternal(moved, 0, true); }
        Check(RunSignature() == beforeMutation && beforeDirect == PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
            "isolated native RNG/deck mutation fixtures restore the complete original run state before continuing");
        var poolCardsField = AccessTools.Field(typeof(CardPoolModel), "_allCards");
        var nativePool = player.Character.CardPool;
        var originalPoolCards = poolCardsField.GetValue(nativePool);
        try
        {
            poolCardsField.SetValue(nativePool, nativePool.AllCards.Reverse().ToArray());
            Check(beforeDirect != PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
                "direct selector observation rejects actual ordered transformation-pool change");
            var changedTiming = new PredictorOperationCapture.CaptureTiming();
            var changedPool = PredictorOperationCapture.CaptureSelection(run, runtime.Detection.NormalizedVersion,
                screen, NGame.Instance, out _, out _, changedTiming);
            Check(changedTiming.SnapshotReuses == 0 && changedTiming.CatalogLevelReuses > 0 &&
                !changedPool.Context.Catalog!.TransformationPools.Single(pool => pool.Key == PredictorLiveRunCapture.Key(nativePool)).Cards
                    .SequenceEqual(beforeSelection.Context.Catalog!.TransformationPools.Single(pool => pool.Key == PredictorLiveRunCapture.Key(nativePool)).Cards),
                "pool mutation triggers complete current membership projection without re-reading canonical levels");
        }
        finally { poolCardsField.SetValue(nativePool, originalPoolCards); }
        Check(RunSignature() == beforeMutation && beforeDirect == PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
            "pool mutation probe restores the native run and exact original direct observation");
        var sources = AccessTools.Field(typeof(PredictorOperationCapture), "SelectionSources").GetValue(null)!;
        object?[] sourceArguments = [screen, null];
        Check((bool)sources.GetType().GetMethod("TryGetValue")!.Invoke(sources, sourceArguments)!,
            "source identity probe uses the actual observed selector source");
        var sourceObservation = sourceArguments[1]!;
        var sourceTaskField = AccessTools.Field(sourceObservation.GetType(), "Task");
        var nativeSourceTask = sourceTaskField.GetValue(sourceObservation);
        try
        {
            sourceTaskField.SetValue(sourceObservation, new TaskCompletionSource().Task);
            Check(beforeDirect != PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
                "local observation rejects an actual pending source-task identity change");
            var sourceTiming = new PredictorOperationCapture.CaptureTiming();
            PredictorOperationCapture.CaptureSelection(run, runtime.Detection.NormalizedVersion,
                screen, NGame.Instance, out _, out _, sourceTiming);
            Check(sourceTiming.SnapshotReuses == 0 && sourceTiming.CatalogLevelReuses > 0,
                "source task change recaptures selector input while retaining only stable canonical definitions");
        }
        finally { sourceTaskField.SetValue(sourceObservation, nativeSourceTask); }
        Check(beforeDirect == PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen),
            "source identity probe restores the actual suspended native source");
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [originals[0]]);
        await Frames(3);
        var selectedSnapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, NGame.Instance);
        string? selectedStamp = PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen);
        Check(selectedStamp != beforeDirect && selectedStamp == PredictorOperationCapture.DirectTransformObservationStamp(selectedSnapshot, run, screen, true),
            "direct Astrolabe native reselection invalidates the old signature and matches a new independent full capture");
        await HoverOriginal(originals[1], "astrolabe-one-selected", 2);
        await HoverOriginal(originals[0], "astrolabe-selected-original", 1);
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [originals[0]]);
        await Frames(3);
        Check(selected.Count == 0, "native hover checks restore the original empty selection without confirming transform");
        var delegateField = AccessTools.Field(typeof(NDeckTransformSelectScreen), "_cardToTransformation");
        var originalDelegate = delegateField.GetValue(screen);
        delegateField.SetValue(screen, (Func<CardModel, CardTransformation>)(_ =>
        { demoCalls++; throw new InvalidOperationException("TransformConfirmationDemoDelegateMustNotExecute"); }));
        var snapshots = new List<object>();
        var fixture = new Harmony("rt2.transform.confirmation.failure.fixture");
        fixture.Patch(AccessTools.Method(typeof(OperationPreviewCardModel), nameof(OperationPreviewCardModel.Create)),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(TransformConfirmationModelFixture)));
        fixture.Patch(AccessTools.Method(typeof(TransformConfirmationPreview), nameof(TransformConfirmationPreview.IsSupported)),
            prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(TransformConfirmationSourceFixture)));
        try
        {
            await Confirm(originals, "initial");
            var featurePreferences = new RolltheSpire2.Ui.Workspace.SearchWorkspace(OS.GetUserDataDir(), false);
            string featureBefore = RunSignature();
            var selectedBefore = selected.ToArray();
            var forecastFaces = PredictorOperationCapture.Read<Control>(native, "_after").GetChildren()
                .OfType<NPreviewCardHolder>().Select(holder => Face(holder.CardModel!)).ToArray();
            var enabledDemoDelegate = delegateField.GetValue(screen);
            delegateField.SetValue(screen, (Func<CardModel, CardTransformation>)(card =>
            { disabledDemoCalls++; return new(card, card); }));
            try
            {
                featurePreferences.SetOperationPredictionEnabled(false); await Frames(3);
                Check(disabledDemoCalls == selectedBefore.Length && selected.SequenceEqual(selectedBefore) &&
                    !selection.IsCompleted && PredictorOperationCapture.Read<Control>(native, "_after").GetChildren()
                        .OfType<NPreviewCardHolder>().Select(holder => holder.CardModel).SequenceEqual(selectedBefore),
                    "feature disable restores the native display delegate without changing or confirming the selected cards");
                delegateField.SetValue(screen, enabledDemoDelegate);
                featurePreferences.SetOperationPredictionEnabled(true); await Frames(3);
                Check(demoCalls == 0 && disabledDemoCalls == selectedBefore.Length &&
                    PredictorOperationCapture.Read<Control>(native, "_after").GetChildren().OfType<NPreviewCardHolder>()
                        .Select(holder => Face(holder.CardModel!)).SequenceEqual(forecastFaces) &&
                    RunSignature() == featureBefore && selected.SequenceEqual(selectedBefore) && !selection.IsCompleted,
                    "feature re-enable rebuilds the current exact confirmation faces without native demo, real transformation or RNG writes");
            }
            finally
            {
                delegateField.SetValue(screen, enabledDemoDelegate);
                featurePreferences.SetOperationPredictionEnabled(true);
            }
            AccessTools.Method(typeof(NDeckTransformSelectScreen), "CancelSelection").Invoke(screen, [null]);
            await Frames(3);
            Check(selected.Count == 0 && !PredictorOperationCapture.Read<Control>(screen, "_previewContainer").Visible,
                "native cancel returns to an empty selection");
            await Confirm(Enumerable.Reverse(originals).ToArray(), "reselected-reverse");
            Check(demoCalls == 0, "native demo callback is never enumerated by either Initialize");
            AccessTools.Method(typeof(NDeckTransformSelectScreen), "CancelSelection").Invoke(screen, [null]);
            await Frames(3);
            string oldUiLanguage = featurePreferences.Preferences.LanguageOverride;
            string oldNativeLanguage = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language;
            try
            {
                foreach (string language in new[] { "en", "zh" })
                {
                    featurePreferences.SetLanguageOverride(language);
                    MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language == "en" ? "zhs" : "eng");
                    string failureBefore = RunSignature();
                    _transformConfirmationInjectDisplayFailure = true;
                    try
                    {
                        foreach (var card in originals)
                            AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [card]);
                    }
                    finally { _transformConfirmationInjectDisplayFailure = false; }
                    await Frames(3);
                    var failedAfter = PredictorOperationCapture.Read<Control>(native, "_after");
                    Check(failedAfter.GetChildren().OfType<Label>().Any(label => label.Text ==
                            (language == "en" ? "Forecast unavailable" : "预测结果暂不可用")) &&
                        !failedAfter.GetChildren().OfType<NPreviewCardHolder>().Any() && demoCalls == 0 &&
                        selected.SetEquals(originals) && !selection.IsCompleted &&
                        PredictorOperationCapture.ReadNullable(native, "_cancelTokenSource") == null && RunSignature() == failureBefore,
                        "actual native selection failure follows RT2 override with opposite game language, preserving selected identities and RNG " + language);
                    AccessTools.Method(typeof(NDeckTransformSelectScreen), "CancelSelection").Invoke(screen, [null]);
                    await Frames(3);
                }
            }
            finally
            {
                featurePreferences.SetLanguageOverride(oldUiLanguage);
                MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(oldNativeLanguage);
            }
            string sourceFailureBefore = RunSignature();
            _transformConfirmationInjectSourceFailure = true;
            try
            {
                foreach (var card in originals)
                    AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [card]);
            }
            finally { _transformConfirmationInjectSourceFailure = false; }
            await Frames(3);
            var sourceFailedAfter = PredictorOperationCapture.Read<Control>(native, "_after");
            Check(PredictorOperationCapture.Read<Control>(screen, "_previewContainer").Visible &&
                sourceFailedAfter.GetChildren().OfType<Label>().Any(label => label.Text.Contains("不可用") || label.Text.Contains("unavailable")) &&
                !sourceFailedAfter.GetChildren().OfType<NPreviewCardHolder>().Any() && demoCalls == 0 &&
                PredictorOperationCapture.ReadNullable(native, "_cancelTokenSource") == null && RunSignature() == sourceFailureBefore,
                "source discovery failure clearly reports unavailable without enumerating demo or starting cycling/RNG");
            AccessTools.Method(typeof(NDeckTransformSelectScreen), "CancelSelection").Invoke(screen, [null]);
            await Frames(3);
            Check(!selection.IsCompleted && PredictorOperationCapture.SelectionIsPending(screen),
                "both stable forecasts leave the real obtain and RNG pending");
        }
        finally
        {
            _transformConfirmationInjectDisplayFailure = false;
            _transformConfirmationInjectSourceFailure = false;
            fixture.UnpatchAll(fixture.Id);
            delegateField.SetValue(screen, originalDelegate);
        }
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "CloseSelection").Invoke(screen, [null]);
        await Until(() => selection.IsCompleted, "ordinary empty-selection cleanup unwinds real Astrolabe obtain");
        Check(selection.IsCompletedSuccessfully, "native cleanup succeeds without committing a transformation");
        Check(PredictorOperationCapture.DirectTransformObservationStamp(beforeSelection, run, screen) == null,
            "closed native selector and completed source cannot reuse its prior observation");
        overlays.Remove(rewardScreen);
        player.RemoveRelicInternal(reward.Relic!, silent: true);
        var classCards = player.Character.CardPool.AllCards.Where(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .Take(40).ToArray();
        var colorlessCards = ModelDb.CardPool<ColorlessCardPool>().AllCards.Where(card => card.Rarity is CardRarity.Uncommon or CardRarity.Rare)
            .Take(20).ToArray();
        foreach (var prototype in classCards.Concat(colorlessCards))
        {
            if (player.Deck.Cards.Count >= 60) break;
            var card = prototype.ToMutable(); run.AddCard(card, player); player.Deck.AddInternal(card);
        }
        Check(player.Deck.Cards.Count == 60 && player.Deck.Cards.Any(card => card.Pool is ColorlessCardPool),
            "Darv hover fixture has a large sixty-card mixed character/colorless deck");
        var ancientRoom = new EventRoom(ModelDb.Event<Darv>()); run.PushRoom(ancientRoom);
        RunManager.Instance.EventSynchronizer.BeginEvent(ModelDb.Event<Darv>());
        var ancient = (Darv)RunManager.Instance.EventSynchronizer.GetLocalEvent();
        await Until(() => ancient.CurrentOptions.Count > 0, "real Darv event initializes its native options");
        var ancientOption = ancient.AllPossibleOptions.Single(option => option.Relic?.Id.Entry == "ASTROLABE");
        AccessTools.Field(typeof(EventModel), "_currentOptions").SetValue(ancient,
            new List<MegaCrit.Sts2.Core.Events.EventOption> { ancientOption });
        var ancientObtain = ancientOption.Chosen();
        await Until(() => overlays.Peek() is NDeckTransformSelectScreen || ancientObtain.IsCompleted,
            "real Darv Astrolabe option suspends at the native picker");
        Check(!ancientObtain.IsCompleted, "Darv obtain remains suspended before prediction");
        screen = (NDeckTransformSelectScreen)overlays.Peek()!;
        await Frames(8);
        selected = PredictorOperationCapture.Read<HashSet<CardModel>>(screen, "_selectedCards");
        candidates = PredictorOperationCapture.Read<IReadOnlyList<CardModel>>(screen, "_cards");
        await HoverOriginal(candidates[0], "astrolabe-darv-none-selected", 1);
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [candidates[0]]);
        await Frames(3);
        await HoverOriginal(candidates[1], "astrolabe-darv-one-selected", 2);
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [candidates[0]]);
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "CloseSelection").Invoke(screen, [null]);
        await Until(() => ancientObtain.IsCompleted, "Darv native empty-selection cleanup completes without true transform");
        Check(ancientObtain.IsCompletedSuccessfully, "Darv native cleanup succeeds");
        ancient.EnsureCleanup(); run.PopCurrentRoom();
        File.WriteAllText(receipt, JsonSerializer.Serialize(new
        {
            passed = true, checks, demoCalls, disabledDemoCalls, nativeInitializeHarmonyEntry = true,
            stableExactSelectedResults = true, reselectionRecomputed = true, runAndRngUnchanged = true,
            injectedDisplayFailureDisclosedWithoutDemo = true,
            injectedSourceDiscoveryFailureDisclosedWithoutDemo = true,
            snapshots, hoverCases, ownerRuntimeAcceptance = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"TRANSFORM_CONFIRMATION_UI_PASS checks={checks};demoCalls={demoCalls};nativeInitialize=true;reselection=true");

        async Task HoverOriginal(CardModel original, string name, int highlightedOrdinal, bool direct = true)
        {
            var viewport = (SubViewport)screen.GetViewport();
            var hoverRoot = NGame.Instance!.HoverTipsContainer!;
            var holder = Descendants(screen).OfType<NGridCardHolder>().First(value =>
                ReferenceEquals(value.CardModel, original) && value.IsVisibleInTree() &&
                screen.GetViewportRect().Encloses(value.Hitbox.GetGlobalRect()));
            var point = holder.Hitbox.GetGlobalRect().GetCenter();
            viewport.PushInput(new InputEventMouseMotion { Position = new(4, viewport.Size.Y - 4),
                GlobalPosition = new(4, viewport.Size.Y - 4) }, true);
            await Frames(3);
            string before = RunSignature();
            var selectedBefore = selected.ToArray();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frames(3);
            var controller = hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
            Check(controller != null && ReferenceEquals(PredictorOperationCapture.ReadNullable(controller, "_owner"), holder),
                name + " real mouse focus admits its original native holder");
            NeowOperationOverlay? View() => controller!.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
            var captured = PredictorOperationCapture.ReadNullable(controller!, "_snapshot") as PredictorOperationSnapshot;
            var observedWork = PredictorOperationCapture.ReadNullable(controller!, "_work") as Task<OperationPreview>;
            double? captureObservedMs = captured != null ? elapsed.Elapsed.TotalMilliseconds : null;
            while (elapsed.Elapsed < TimeSpan.FromSeconds(name.Contains("darv", StringComparison.Ordinal) ? 5 : 30) &&
                View() is not { Visible: true })
            {
                captured ??= PredictorOperationCapture.ReadNullable(controller!, "_snapshot") as PredictorOperationSnapshot;
                observedWork ??= PredictorOperationCapture.ReadNullable(controller!, "_work") as Task<OperationPreview>;
                if (captured != null) captureObservedMs ??= elapsed.Elapsed.TotalMilliseconds;
                await Frames(1);
            }
            File.WriteAllText(Path.Combine(output, name + "-progress.json"), JsonSerializer.Serialize(new
            {
                name, deckCount = player.Deck.Cards.Count, focusOwner = PredictorOperationCapture.ReadNullable(controller!, "_owner")?.GetType().Name,
                captureObservedMs, scene = captured?.Scene.GetType().Name, workerStatus = observedWork?.Status.ToString(),
                elapsedMs = elapsed.Elapsed.TotalMilliseconds, visible = View() is { Visible: true },
                pathsExamined = observedWork is { IsCompletedSuccessfully: true } ? observedWork.Result.Branches.Sum(branch => branch.PathsExamined) : (int?)null
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (View() is not { Visible: true })
            {
                viewport.PushInput(new InputEventMouseMotion { Position = new(4, viewport.Size.Y - 4),
                    GlobalPosition = new(4, viewport.Size.Y - 4) }, true);
                await Frames(3);
                for (int i = 0; i < 120 && observedWork is { IsCompleted: false }; i++) await Frames(1);
                File.WriteAllText(Path.Combine(output, name + "-cancel.json"), JsonSerializer.Serialize(new
                {
                    name, workerStatus = observedWork?.Status.ToString(),
                    runAndRngUnchanged = RunSignature() == before, selectedUnchanged = selected.SequenceEqual(selectedBefore)
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Check(View() is { Visible: true }, name + " real focus/capture/worker/publication shows a visible result;owner=" +
                PredictorOperationCapture.ReadNullable(controller!, "_owner")?.GetType().Name + ";snapshot=" +
                (PredictorOperationCapture.ReadNullable(controller!, "_snapshot") as PredictorOperationSnapshot)?.Scene.GetType().Name +
                ";work=" + (PredictorOperationCapture.ReadNullable(controller!, "_work") as Task)?.Status);
            var view = View()!;
            double publicationMs = elapsed.Elapsed.TotalMilliseconds;
            var preview = PredictorOperationCapture.Read<OperationPreview>(view, "_preview");
            var timing = PredictorOperationCapture.Read<NeowOperationHoverController.HoverTiming>(controller!, "_timing");
            if (name is "astrolabe-same-card-warm" or "astrolabe-switch-b" or "astrolabe-return-a")
                Check(timing.Capture.SnapshotReuses == 1 && timing.Capture.CatalogLevelReads == 0 && timing.Capture.DirectPoolReads > 0,
                    name + " leaving and switching native holders reuses selector inputs after current pool/dynamic observation");
            Check(timing.InitialObservationMs == 0 && timing.PublishCaptureMs > 0,
                name + " reuses its captured observation and validates before publication");
            Check(direct
                    ? ReferenceEquals(PredictorOperationCapture.ReadNullable(controller!, "_directTransformSelector"), screen)
                    : PredictorOperationCapture.ReadNullable(controller!, "_directTransformSelector") == null && timing.Publication.CatalogMs > 0,
                name + " publication uses its required direct or complete capture guard");
            if (name == "astrolabe-none-selected" || name == "astrolabe-darv-none-selected")
            {
                await Until(() => timing.PollCount > 0, name + " unchanged hover performs its native freshness poll");
                Check(ReferenceEquals(View(), view), name + " unchanged freshness poll preserves the published native cards");
            }
            var groups = preview.Branches.SelectMany(branch => branch.Sections).Where(section => section.Source.Entry == "ASTROLABE")
                .SelectMany(section => section.CardGroups).ToArray();
            Check(preview.Faults.IsEmpty && groups.Length == 3 && groups.All(group => !group.Cards.IsEmpty && group.ForceSingleRow) &&
                groups.Single(group => group.Highlighted).Label == highlightedOrdinal.ToString(),
                name + " native worker presents all three flat transform ordinals with the actual current ordinal highlighted");
            var ordinalStacks = Descendants(view).OfType<Control>().Where(node => node.HasMeta("rt2_single_row") &&
                node.GetMeta("rt2_single_row").AsBool()).OrderBy(node => node.GlobalPosition.X).ToArray();
            Check(ordinalStacks.Length == 3 && ordinalStacks.All(stack => stack.GetChildren().OfType<Control>()
                .Where(tile => tile.GetChildren().OfType<NCard>().Any()).All(tile =>
                    tile.Modulate.IsEqualApprox(stack.GetMeta("rt2_highlighted").AsBool() ? Colors.White : new Color(.55f, .55f, .55f)))) &&
                Descendants(view).OfType<NCard>().All(card => !card.GetNode<Control>("%Highlight").Visible),
                name + " current ordinal retains normal whole-card brightness and all other ordinal cards are dimmed without native highlight");
            Check(Enumerable.Range(1, ordinalStacks.Length - 1).All(index =>
                Math.Abs(ordinalStacks[index].GlobalPosition.X - ordinalStacks[index - 1].GetGlobalRect().End.X - 8) < .2f),
                name + " adjacent native transform ordinals use the compact eight-pixel gap");
            if (name.Contains("darv", StringComparison.Ordinal))
                Check(preview.Branches.Sum(branch => branch.PathsExamined) == 3,
                    name + " direct mixed-pool Astrolabe uses exactly three complete representative inputs");
            Check(Descendants(view).OfType<NCard>().Count() >= 3 && view.VisibleRegions.Count > 0 &&
                RunSignature() == before && selected.SequenceEqual(selectedBefore),
                name + " native hover draws its result without changing real selection, RunState or RNG");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, name + "-zh.png"));
            hoverCases.Add(new { name, selected = selectedBefore.Select(PredictorLiveRunCapture.Id),
                hovered = PredictorLiveRunCapture.Id(original), groups = groups.Length, highlightedOrdinal,
                deckCount = player.Deck.Cards.Count, captureObservedMs,
                publicationValidation = direct ? "DirectAstrolabeDynamicStamp" : "FullCapture",
                pathsExamined = preview.Branches.Sum(branch => branch.PathsExamined), predictionMs = publicationMs,
                timing = new { timing.CaptureMs, timing.InitialObservationMs, timing.WorkerMs, timing.PublishCaptureMs,
                    capture = new { timing.Capture.NativeStampMs, timing.Capture.CatalogMs, timing.Capture.CompleteCatalogMs,
                        timing.Capture.HashMs, timing.Capture.AllocatedBytes,
                        timing.Capture.PoolReads, timing.Capture.PoolReuses, timing.Capture.PoolProjectionReuses,
                        timing.Capture.CatalogLevelReads, timing.Capture.CatalogLevelReuses, timing.Capture.SnapshotReuses,
                        timing.Capture.DirectPoolReads, timing.Capture.ContentObservationReads },
                    publication = new { timing.Publication.NativeStampMs, timing.Publication.CatalogMs, timing.Publication.CompleteCatalogMs,
                        timing.Publication.HashMs, timing.Publication.AllocatedBytes,
                        timing.Publication.PoolReads, timing.Publication.PoolReuses, timing.Publication.PoolProjectionReuses },
                    timing.RenderMs, timing.PlacementMs, timing.PollObservationMs, timing.PollCount,
                    view.NativeCardCreateMilliseconds, view.NativeCardReadyMilliseconds, view.NativeCardCount } });
            viewport.PushInput(new InputEventMouseMotion { Position = new(4, viewport.Size.Y - 4),
                GlobalPosition = new(4, viewport.Size.Y - 4) }, true);
            await Frames(3);
        }

        async Task Confirm(CardModel[] cards, string name)
        {
            string before = RunSignature();
            var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, NGame.Instance);
            var ids = cards.Select(PredictorLiveRunCapture.Id).ToImmutableArray();
            var forecast = TransformSelectionForecast.Build(snapshot, ids);
            string detachedBefore = JsonSerializer.Serialize(new { snapshot.State, snapshot.Context, snapshot.SelectedCardIds });
            foreach (var card in cards)
                AccessTools.Method(typeof(NDeckTransformSelectScreen), "OnCardClicked").Invoke(screen, [card]);
            await Frames(3);
            Check(PredictorOperationCapture.Read<Control>(screen, "_previewContainer").Visible &&
                selected.Select(PredictorLiveRunCapture.Id).SequenceEqual(ids),
                name + " real click path opens native confirmation in actual selected order");
            var beforeHost = PredictorOperationCapture.Read<Control>(native, "_before");
            var afterHost = PredictorOperationCapture.Read<Control>(native, "_after");
            var beforeCards = beforeHost.GetChildren().OfType<NPreviewCardHolder>().ToArray();
            var afterCards = afterHost.GetChildren().OfType<NPreviewCardHolder>().ToArray();
            Check(beforeCards.Select(holder => holder.CardModel).SequenceEqual(cards) && afterCards.Length == cards.Length,
                name + " native original-to-result rows have exactly one face per selected card");
            Check(afterCards.Select((holder, index) => holder.CardModel is { RunState: null, CombatState: null } model &&
                Face(model) == Face(OperationPreviewCardModel.Create(forecast[index].Card))).All(value => value),
                name + " right-hand faces match exact final upgraded forecast in selected order");
            Check(afterCards.All(holder => holder.CardModel!.CurrentUpgradeLevel > 0) &&
                PredictorOperationCapture.ReadNullable(native, "_cancelTokenSource") == null && demoCalls == 0,
                name + " Astrolabe upgrades are visible and no native random-cycling worker starts");
            var faces = afterCards.Select(holder => Face(holder.CardModel!)).ToArray();
            await Frames(30);
            Check(afterCards.Select(holder => Face(holder.CardModel!)).SequenceEqual(faces) && RunSignature() == before &&
                JsonSerializer.Serialize(new { snapshot.State, snapshot.Context, snapshot.SelectedCardIds }) == detachedBefore,
                name + " faces remain stable across frames with real run/RNG and detached snapshot unchanged");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            screen.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, "transform-confirmation-" + name + "-zh.png"));
            snapshots.Add(new { name, selected = ids, forecast = forecast.Select(item => new
                { item.OriginalId, card = item.Card.Key.Serialized, item.Card.UpgradeLevel }) });
        }
    }
}
