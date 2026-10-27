using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

internal static partial class UiSurfaceChecks
{
    // Called from the existing native operation host with one actual captured
    // catalog/state. All constructed event pages below are detached fixtures;
    // no native run, card play, reward claim or scene is modified.
    private static void RunEventFeedbackChecks(PredictorOperationSnapshot basis, string output)
    {
        int checks = 0;
        void Check(bool value, string reason)
        { checks++; if (!value) throw new InvalidOperationException("EventFeedbackUi: " + reason); }
        var context = basis.Context with { Live = null, Crystal = null, Modifiers = [], NumberOfRuns = 5,
            SealOfGoldFixedDeduction = 0 };
        Check(!basis.State.Acts.IsDefaultOrEmpty, "captured native state supplies an actual available act queue");
        int fixtureAct = basis.State.Acts[0].Act;
        // The native host starts at Neow/floor zero. These detached fixtures
        // explicitly model an event after opening at floor ten in an available
        // captured act; changing Position alone cannot satisfy the backend's
        // OpeningCompleted invariant. This is no natural-run reachability claim.
        var state = basis.State with { Relics = [], OpeningCompleted = true, CurrentRoomIsCombat = false,
            Position = new(fixtureAct, 10, "event-feedback"),
            Deck = basis.State.Deck.Where(id => basis.State.Card(id).Key.Entry is not ("THE_HUNT" or "ROYALTIES")).ToImmutableArray() };
        var rng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards);
        PredictorOperationSnapshot Snapshot(string entry, PredictorState current, params PredictorEventOption[] options) =>
            new("event-feedback:" + entry, context, current,
                new PredictorOperationEventScene(new("EVENT", entry), rng, options.ToImmutableArray(), new(80, 80)), 80, 80);
        PredictorState AddCard(PredictorState current, string key, int upgrade = 0)
        {
            var card = context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key == new ModelKey("CARD", key)).Prototype
                with { Id = current.NextInstanceId, UpgradeLevel = upgrade };
            return current with { Cards = current.Cards.Add(card), Deck = current.Deck.Add(card.Id),
                NextInstanceId = current.NextInstanceId + 1 };
        }
        static OperationPreviewCardGroup[] Groups(OperationPreview preview) => preview.Branches.SelectMany(b => b.Sections)
            .SelectMany(s => s.CardGroups).ToArray();
        static PredictorCard[] Faces(IEnumerable<PredictorCard> cards) => cards.Select(c => c with { Id = 0 }).ToArray();
        PredictorCombatFacts Facts(PredictorState current, int hunts, bool royalties)
        {
            long? royal = current.Deck.Select(current.Card).FirstOrDefault(c => c.Key.Entry == "ROYALTIES")?.Id;
            return new(false)
            {
                CardTriggers = current.Deck.Select(current.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                    .Select(c => new PredictorCardTrigger(c.Id,
                        c.Key.Entry == "THE_HUNT" && hunts-- > 0 || c.Id == royal && royalties ? 1 : 0)).ToImmutableArray(),
                Potions = PredictorCombatPotionFacts.NoAdditional
            };
        }

        // Use three copies against Punch Off's two actual starting creatures.
        var huntState = state;
        for (int i = 0; i < 3; i++) huntState = AddCard(huntState, "THE_HUNT");
        huntState = AddCard(huntState, "ROYALTIES");
        huntState = AddCard(huntState, "ROYALTIES", 1);
        var punch = Snapshot("PUNCH_OFF", huntState, new PredictorEventOption("I_CAN_TAKE_THEM"));
        punch = punch with { Scene = ((PredictorOperationEventScene)punch.Scene) with
            { LayoutEncounter = new("ENCOUNTER", "PUNCH_OFF_EVENT_ENCOUNTER") } };
        string signature = JsonSerializer.Serialize(punch);
        Check(EventOperationPreview.CanToggleRoyalties(punch, "I_CAN_TAKE_THEM") &&
            !EventOperationPreview.CanToggleRoyalties(punch, "MISSING") &&
            !EventOperationPreview.CanToggleRoyalties(punch with { State = state }, "I_CAN_TAKE_THEM"),
            "Royalties toggle is restricted to a present card and enabled actual battle entrance");
        var conditional = EventOperationPreview.Build(punch, "I_CAN_TAKE_THEM");
        var expected = PredictorOperation.Preview(punch, [new ChooseEventOption("I_CAN_TAKE_THEM"),
            new ChooseEventOption("FIGHT"), new ResolveCombat(Facts(huntState, 2, false))]);
        var wantedHunts = expected.Rewards.Where(r => r.Kind == PredictorRewardKind.Card &&
                r.RerollRecipe is { EncounterSource: true, IsFromCombat: false })
            .OrderBy(r => r.SubscriptionOrder).Take(2).ToArray();
        var huntGroups = Groups(conditional).Where(g => g.Label?.StartsWith("EventHuntConditional:", StringComparison.Ordinal) == true).ToArray();
        Check(conditional.Faults.IsEmpty && conditional.PendingReasons.IsEmpty && conditional.Branches.Length == 1 &&
            huntGroups.Length == 2 && huntGroups.Select(g => g.Label).SequenceEqual(new[] { "EventHuntConditional:1", "EventHuntConditional:2" }) &&
            huntGroups.All(g => g.Possible) && huntGroups.Select((g, i) => Faces(g.Cards).SequenceEqual(Faces(wantedHunts[i].Cards))).All(v => v),
            "Hunt adds two ordered conditional groups from the full two-fatal settlement, capped by enemy count");
        Check(conditional.Notices.Contains("EventCombatRoyaltiesNotPlayed"), "Royalties defaults to no play");
        var played = EventOperationPreview.Build(punch, "I_CAN_TAKE_THEM", playRoyalties: true);
        var expectedPlayed = PredictorOperation.Preview(punch, [new ChooseEventOption("I_CAN_TAKE_THEM"),
            new ChooseEventOption("FIGHT"), new ResolveCombat(Facts(huntState, 2, true))]);
        var wantedPlayed = expectedPlayed.Rewards.Where(r => r.Kind == PredictorRewardKind.Card &&
                r.RerollRecipe is { EncounterSource: true, IsFromCombat: false }).OrderBy(r => r.SubscriptionOrder).Take(2).ToArray();
        var playedGroups = Groups(played).Where(g => g.Label?.StartsWith("EventHuntConditional:", StringComparison.Ordinal) == true).ToArray();
        Check(played.Faults.IsEmpty && played.Notices.Contains("EventCombatRoyaltiesPlayed") && playedGroups.Length == 2 &&
            playedGroups.Select((g, i) => Faces(g.Cards).SequenceEqual(Faces(wantedPlayed[i].Cards))).All(v => v) &&
            expectedPlayed.State.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls ==
                expected.State.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls + 1,
            "one Royalties play runs before added-reward hooks and consumes its native fixed Gold Populate draw");

        var wheelState = huntState with { Relics = [new(huntState.NextInstanceId, new("RELIC", "PRAYER_WHEEL"), 1)],
            NextInstanceId = huntState.NextInstanceId + 1 };
        var wheel = EventOperationPreview.Build(punch with { State = wheelState }, "I_CAN_TAKE_THEM");
        Check(wheel.Faults.IsEmpty && wheel.Branches.Length == 2 && wheel.Branches.SelectMany(b => b.Sections)
                .Any(s => s.Label == "EventCombatNoHunt") && wheel.Branches.SelectMany(b => b.Sections)
                .Any(s => s.Label == "EventHuntTriggers:2") && Groups(wheel).All(g => g.Possible),
            "Prayer Wheel's later population retains correlated no-Hunt versus full-Hunt outcomes");

        var doll = Snapshot("DOLL_ROOM", state, new PredictorEventOption("TAKE_SOME_TIME"));
        var dolls = EventOperationPreview.Build(doll, "TAKE_SOME_TIME");
        Check(dolls.Faults.IsEmpty && dolls.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows)
            .Count(r => r.Action == OperationPreviewAction.Choose) == 2,
            "Doll Room hidden candidates say Choose");
        var revealed = PredictorOperation.Preview(doll, new ChooseEventOption("TAKE_SOME_TIME"));
        var visible = doll with { State = revealed.State, CurrentHp = revealed.CurrentHp,
            Scene = ((PredictorOperationEventScene)doll.Scene) with { Options = revealed.Waiting.EventOptions,
                Health = new(revealed.CurrentHp, revealed.MaximumHp) } };
        Check(revealed.Waiting.EventOptions.All(option => EventOperationPreview.Build(visible, option.Key).Branches.IsEmpty),
            "actual revealed doll choices have no redundant obtain prediction");

        var bridgeCard = state.Deck.First(id => state.Card(id).Removable);
        var bridge = Snapshot("SLIPPERY_BRIDGE", state, new("OVERCOME", CardTargetId: bridgeCard), new("HOLD_ON"));
        bridge = bridge with { CurrentHp = 1000, MaximumHp = 1000, Scene = ((PredictorOperationEventScene)bridge.Scene) with
            { Health = new(1000, 1000), BridgeCard = bridgeCard } };
        var publicRemoval = EventOperationPreview.Build(bridge, "OVERCOME");
        Check(publicRemoval.Branches.IsEmpty && publicRemoval.Notices.IsEmpty,
            "Bridge public removal has no prediction or orphan notice");
        var future = EventOperationPreview.Build(bridge, "HOLD_ON");
        var proposals = future.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows)
            .SelectMany(r => r.Cards).ToArray();
        var prefix = ImmutableArray<PredictorInput>.Empty;
        var wanted = new List<long>();
        while (true)
        {
            prefix = prefix.Add(new ChooseEventOption("HOLD_ON"));
            var next = PredictorOperation.Preview(bridge, prefix);
            if (next.Waiting.EventOptions.FirstOrDefault(o => o.Key == "OVERCOME")?.CardTargetId is not { } id) break;
            wanted.Add(id);
            if (!next.Waiting.EventOptions.Any(o => o.Key == "HOLD_ON" && o.Enabled)) break;
        }
        Check(future.Faults.IsEmpty && proposals.Length > 15 && proposals.Select(c => c.Id).SequenceEqual(wanted) &&
            future.Branches.Single().Sections.Select(s => s.Label).SequenceEqual(Enumerable.Range(1, wanted.Count).Select(i => i.ToString())),
            "Bridge retains every future reroll in order, excluding only the public current proposal");
        var displayedBridgeCards = future.Branches.SelectMany(branch => NeowOperationOverlay.DisplaySections(future, branch))
            .SelectMany(section => section.CardGroups).SelectMany(group => group.Cards).ToArray();
        Check(displayedBridgeCards.SequenceEqual(proposals),
            "Bridge final display grouping preserves the complete future sequence beyond fifteen cards");
        Check(signature == JsonSerializer.Serialize(punch), "event feedback leaves captured source state and RNG unchanged");
        File.WriteAllText(Path.Combine(output, "operation-event-feedback-result.json"), JsonSerializer.Serialize(new
            { passed = true, checks, nativeCapturedBasis = true, detachedEventFixtures = true,
                controlledBattleBoundary = new { act = fixtureAct, floor = 10, openingCompleted = true }, ownerRuntimeAcceptance = false },
            new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"OPERATION_EVENT_FEEDBACK_PASS checks={checks};detached=true;cap=2;royalties=one-play");
    }

    private static async Task RunEventRoyaltiesInputChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player, NEventRoom eventNode,
        NSceneContainer layoutScenes, NOverlayStack overlays, Func<string> signature, Action<bool, string> check,
        Action<object, string, object?> set)
    {
        string receipt = Path.Combine(output, "operation-event-royalties-input-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        int checks = 0;
        void Verify(bool value, string reason) { checks++; check(value, "Royalties native input: " + reason); }
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            Verify(ready(), reason);
        }
        NeowOperationOverlay? View() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName)
            ?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        OperationPreview? Preview() => View() is { Visible: true } view
            ? PredictorOperationCapture.ReadNullable(view, "_preview") as OperationPreview : null;
        async Task Move(Vector2 position)
        {
            viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
            await Frames();
        }
        async Task Away() => await Move(new(4, viewport.Size.Y - 4));
        async Task Hover(NEventOptionButton button, bool played)
        {
            await Move(button.GetGlobalRect().GetCenter());
            await Until(() => Preview() is { Source.Entry: "PUNCH_OFF" } preview && preview.Notices.Contains(
                played ? "EventCombatRoyaltiesPlayed" : "EventCombatRoyaltiesNotPlayed"),
                "actual native hover publishes " + (played ? "played" : "not-played") + " hypothesis");
            Verify(PredictorOperationCapture.ReadNullable(hoverRoot.GetNode<NeowOperationHoverController>(
                NeowOperationHoverController.NodeName), "_owner") == button,
                "original native option owns the published hypothesis");
        }
        void Right(NEventOptionButton button, bool pressed)
        {
            var position = button.GetGlobalRect().GetCenter();
            viewport.PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Right, Pressed = pressed,
                ButtonMask = pressed ? MouseButtonMask.Right : (MouseButtonMask)0 }, true);
        }

        await Away();
        string original = signature();
        var originalRunRng = run.Rng.ToSerializable();
        var originalPlayerRng = player.PlayerRng.ToSerializable();
        var originalOdds = player.PlayerOdds.ToSerializable();
        var originalCreatureCombat = player.Creature.CombatState;
        var originalCreatureId = player.Creature.CombatId;
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var originalHistory = history.Select(act => act.ToArray()).ToArray();
        var originalNativeEvent = PredictorOperationCapture.ReadNullable(eventNode, "_event");
        var originalLayout = layoutScenes.CurrentScene;
        var originalOverlayMouseFilter = overlays.MouseFilter;
        var priorLayouts = game.GetChildren().OfType<NEventLayout>().Select(layout => (Layout: layout, layout.Visible)).ToArray();
        var synchronizer = RunManager.Instance.EventSynchronizer;
        var eventList = PredictorOperationCapture.Read<List<EventModel>>(synchronizer, "_events");
        var originalEvents = eventList.ToArray();
        var originalCanonicalEvent = PredictorOperationCapture.ReadNullable(synchronizer, "_canonicalEvent");
        var originalPage = PredictorOperationCapture.Read<uint>(synchronizer, "_pageIndex");
        var votes = PredictorOperationCapture.Read<List<uint?>>(synchronizer, "_playerVotes");
        var originalVotes = votes.ToArray();
        var pendingTasks = PredictorOperationCapture.Read<List<Task>>(synchronizer, "_pendingOptionTasks");
        var originalTasks = pendingTasks.ToArray();
        var combatSynchronizer = PredictorOperationCapture.Read<EventCombatSynchronizer>(synchronizer, "_combatSynchronizer");
        var originalCombatEvent = PredictorOperationCapture.ReadNullable(combatSynchronizer, "_canonicalEvent");
        var originalCombatLayout = combatSynchronizer.CombatStateForLayout;
        var originalMutableEncounter = combatSynchronizer.MutableEncounterForLayout;
        var addedCards = new List<CardModel>();
        EventRoom? room = null;
        EventModel? ev = null;
        NEventLayout? mounted = null;
        bool passed = false;
        string? failure = null;
        object? evidence = null;
        try
        {
            Verify(overlays.ScreenCount == 0 && overlays.Peek() == null && originalCombatLayout == null && originalCreatureCombat == null &&
                originalTasks.All(task => task.IsCompleted), "fixture starts outside combat, overlays and pending native choices");
            // The enclosing host bypasses the raw overlay's _Ready. Its empty
            // full-screen root otherwise intercepts native option mouse motion.
            overlays.MouseFilter = Control.MouseFilterEnum.Ignore;
            foreach (var prior in priorLayouts) prior.Layout.Hide();
            // These are real mutable native cards registered in the run. They
            // are added only to the isolated fixture and are never played.
            foreach (var card in new CardModel[] { ModelDb.Card<Royalties>().ToMutable(),
                ModelDb.Card<TheHunt>().ToMutable(), ModelDb.Card<TheHunt>().ToMutable() })
            { run.AddCard(card, player); player.Deck.AddInternal(card, silent: true); addedCards.Add(card); }
            while (run.TotalFloor < 7)
                run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, new ModelId("EVENT", "PUNCH_OFF"));
            room = new EventRoom(ModelDb.Event<PunchOff>()); run.PushRoom(room);
            synchronizer.BeginEvent(ModelDb.Event<PunchOff>());
            ev = synchronizer.GetLocalEvent();
            await Until(() => ev.CurrentOptions.Count == 2, "native Punch Off generates both initial options");
            // This is the native event-entry roster construction; its Niche
            // draws occur before capture, never inside the prediction.
            synchronizer.GenerateInternalCombatStateIfNecessary(ev);
            set(eventNode, "_event", ev);
            mounted = PreloadManager.Cache.GetScene("res://scenes/events/default_event_layout.tscn").Instantiate<NEventLayout>();
            set(layoutScenes, nameof(NSceneContainer.CurrentScene), mounted);
            game.AddChild(mounted); game.MoveChild(mounted, 0); mounted.SetEvent(ev);
            mounted.SetTitle(ev.Title.GetFormattedText()); ev.DynamicVars.AddTo(ev.Description!);
            mounted.SetDescription(ev.Description!.GetFormattedText()); mounted.AddOptions(ev.CurrentOptions);
            async Task SettleButtons()
            {
                foreach (var button in mounted.OptionButtons) button.EnableButton();
                await Frames(2);
                await Until(() => mounted.OptionButtons.All(button => button.IsEnabled && button.Modulate.A > .99f),
                    "original native option entry animations settle");
            }
            NEventOptionButton FightOption() => mounted.OptionButtons.Single(button =>
                PredictorOperationEventCapture.Option(ev, button.Option).Key == "I_CAN_TAKE_THEM");
            await SettleButtons();
            var options = ev.CurrentOptions;
            var first = FightOption();
            var actualOption = first.Option;
            string before = signature();
            int seen = _neowSeenCalls;
            void Unchanged(string reason) => Verify(signature() == before && !ev.IsFinished &&
                ev.CurrentOptions.Count == 2 && ev.CurrentOptions.All(option => !option.WasChosen) &&
                _neowSeenCalls == seen && ReferenceEquals(run.CurrentRoom, room), reason);
            await Hover(first, false);
            var initial = Preview()!;
            Verify(initial.Faults.IsEmpty && initial.Branches.SelectMany(branch => branch.Sections)
                .Where(section => section.Source == new ModelKey("CARD", "THE_HUNT"))
                .SelectMany(section => section.CardGroups).Count(group => group.Possible) == 2,
                "native captured two-creature battle renders both conditional Hunt groups");
            var uiText = JsonUiTextProvider.CreateUi13(UiLanguagePreferences.LanguageCode);
            var captions = Descendants(View()!).OfType<Label>().Select(label => label.Text).ToArray();
            Verify(captions.Any(text => text.Contains(uiText.Format("operation.label.huntGroup", 1), StringComparison.Ordinal)) &&
                captions.Any(text => text.Contains(uiText.Format("operation.label.huntGroup", 2), StringComparison.Ordinal)) &&
                captions.All(text => !text.Contains("EventHuntConditional:", StringComparison.Ordinal)),
                "native Hunt artwork captions display localized trigger condition and order");
            Unchanged("initial hover preserves native choices, run, RNG and seen cards");
            Right(first, true);
            await Until(() => Preview()?.Notices.Contains("EventCombatRoyaltiesPlayed") == true &&
                !ReferenceEquals(Preview(), initial), "real right press refreshes to played hypothesis");
            Right(first, false); await Frames(4);
            Verify(Preview()?.Notices.Contains("EventCombatRoyaltiesPlayed") == true,
                "right release is consumed without a second toggle");
            Unchanged("right press/release changes only the hypothesis, never selects the native option");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-royalties-native-right-on-zh.png"));
            await Away();
            Verify(View() == null, "native unfocus clears prediction surfaces");
            await Hover(first, true);
            Unchanged("leaving and returning retains this actual option's hypothesis without native mutation");

            // Same native generator/key, different real EventOption objects:
            // the weak-keyed toggle must not leak to another actual option.
            await Away();
            var regenerated = ((IReadOnlyList<MegaCrit.Sts2.Core.Events.EventOption>)AccessTools.Method(typeof(PunchOff),
                "GenerateInitialOptions").Invoke(ev, null)!).ToList();
            set(ev, "_currentOptions", regenerated); mounted.ClearOptions(); mounted.AddOptions(regenerated);
            await SettleButtons();
            var other = FightOption();
            Verify(!ReferenceEquals(other.Option, actualOption), "second original generator produces a distinct actual option identity");
            before = signature();
            await Hover(other, false);
            Unchanged("another actual option with the same event/key defaults to not played");
            await Away();
            set(ev, "_currentOptions", options); mounted.ClearOptions(); mounted.AddOptions(options);
            await SettleButtons();
            first = FightOption(); before = signature();
            Verify(ReferenceEquals(first.Option, actualOption), "recreated native control still wraps the first actual option");
            await Hover(first, true);
            Right(first, true);
            await Until(() => Preview()?.Notices.Contains("EventCombatRoyaltiesNotPlayed") == true,
                "next real right press refreshes back to not-played hypothesis");
            Right(first, false); await Frames(4);
            Verify(Preview()?.Notices.Contains("EventCombatRoyaltiesNotPlayed") == true,
                "second release preserves the refreshed off state");
            Unchanged("both toggle directions preserve the actual current event and all RNG");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-royalties-native-right-off-zh.png"));
            evidence = new { actualNativeEvent = "PUNCH_OFF", nativeRightInput = true, nativeSelectionCommitted = false,
                states = new[] { "default-off", "right-on", "leave-reenter-on", "other-option-default-off", "right-off" },
                huntGroupCount = 2, conditionalLocalizedCaptions = true };
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            await Away();
            overlays.MouseFilter = originalOverlayMouseFilter;
            mounted?.Free(); ev?.EnsureCleanup();
            combatSynchronizer.ResetState();
            set(combatSynchronizer, "_canonicalEvent", originalCombatEvent);
            set(combatSynchronizer, "MutableEncounterForLayout", originalMutableEncounter);
            set(combatSynchronizer, "CombatStateForLayout", originalCombatLayout);
            player.Creature.CombatState = originalCreatureCombat; player.Creature.CombatId = originalCreatureId;
            foreach (var card in addedCards) { player.Deck.RemoveInternal(card, silent: true); run.RemoveCard(card); }
            if (room != null && ReferenceEquals(run.CurrentRoom, room)) run.PopCurrentRoom();
            eventList.Clear(); eventList.AddRange(originalEvents);
            votes.Clear(); votes.AddRange(originalVotes); pendingTasks.Clear(); pendingTasks.AddRange(originalTasks);
            set(synchronizer, "_canonicalEvent", originalCanonicalEvent); set(synchronizer, "_pageIndex", originalPage);
            set(eventNode, "_event", originalNativeEvent);
            set(layoutScenes, nameof(NSceneContainer.CurrentScene), originalLayout is GodotObject obj && !GodotObject.IsInstanceValid(obj)
                ? null : originalLayout);
            foreach (var prior in priorLayouts) if (GodotObject.IsInstanceValid(prior.Layout)) prior.Layout.Visible = prior.Visible;
            history.Clear(); foreach (var act in originalHistory) history.Add(act.ToList());
            player.PlayerRng.LoadFromSerializable(originalPlayerRng); player.PlayerOdds.LoadFromSerializable(originalOdds);
            run.Rng.LoadFromSerializable(originalRunRng);
            bool restored = signature() == original;
            if (!restored)
            {
                File.WriteAllText(Path.Combine(output, "operation-event-royalties-restore-before.json"), original);
                File.WriteAllText(Path.Combine(output, "operation-event-royalties-restore-after.json"), signature());
            }
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, checks, restored, failure, evidence,
                scope = "Native PunchOff options/layout and native two-creature roster; controlled real Royalties/TheHunt deck cards; actual SubViewport right press/release and natural controller capture/worker/publication; no choice callback invoked by the fixture.",
                ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
            if (passed) Verify(restored, "fixture restores original native state, event ownership, scene, history and RNG");
        }
        GD.Print($"OPERATION_EVENT_ROYALTIES_INPUT_PASS checks={checks};nativeRightInput=true;restored=true");
    }
}
