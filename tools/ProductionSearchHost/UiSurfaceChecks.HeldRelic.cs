using System.Diagnostics;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunHeldRelicHoverChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player, NGlobalUi global,
        Action<object, string, object?> set)
    {
        var args = OS.GetCmdlineUserArgs();
        string gamePack = Path.GetFullPath(args[Array.IndexOf(args, "--game-pck") + 1]);
        var detection = GameVersionDetector.DetectFromCandidates([Path.Combine(Path.GetDirectoryName(gamePack)!, "release_info.json")]);
        var uiText = JsonUiTextProvider.CreateUi13("zh");
        string receipt = Path.Combine(output, "held-relic-hover-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        int checks = 0;
        void Check(bool condition, string reason)
        { checks++; if (!condition) throw new InvalidOperationException("HeldRelicHoverUi: " + reason); }
        Check(detection.IsExact && Version.Parse(detection.NormalizedVersion) >= new Version(0, 113),
            "provided game PCK has modern release metadata for the native NonCombatGeneration oracle");
        var runtimeProperty = typeof(ModRuntime).GetProperty(nameof(ModRuntime.Snapshot))!;
        var originalRuntime = ModRuntime.Snapshot;
        var nativeRng = player.PlayerRng.NonCombatGeneration;
        var originalRng = nativeRng.ToSerializable();
        var originalInventory = global.RelicInventory;
        var originalCapstone = global.CapstoneContainer;
        var originalDeck = player.Deck.Cards.ToArray();
        var addedCards = new List<CardModel>();
        var orobas = new EventRoom(ModelDb.Event<Orobas>());
        var capstone = new NCapstoneContainer();
        var deckScreen = new NDeckViewScreen();
        var turnField = AccessTools.Field(typeof(CombatManager), "_turnState");
        var originalTurn = turnField.GetValue(CombatManager.Instance);
        var room = new CombatRoom(ModelDb.AllEncounters.First(encounter => encounter.RoomType == RoomType.Monster).ToMutable(), run);
        var originalRoom = run.CurrentRoom;
        var hammer = (WarHammer)ModelDb.Relic<WarHammer>().ToMutable();
        var rod = (FishingRod)ModelDb.Relic<FishingRod>().ToMutable();
        var tooth = (PaelsTooth)ModelDb.Relic<PaelsTooth>().ToMutable();
        var storedCards = PredictorOperationCapture.Read<List<SerializableCard>>(tooth, "_serializableCards");
        void RefreshToothCounter() => AccessTools.Method(typeof(PaelsTooth), "UpdateCardList").Invoke(tooth, null);
        RelicModel[] added = [hammer, rod, tooth];
        var inventory = new NRelicInventory { Name = "HeldRelicInventory", MouseFilter = Control.MouseFilterEnum.Ignore };
        var cases = new List<object>();
        string? failure = null;
        bool passed = false;
        string Signature() => JsonSerializer.Serialize(new
        {
            room = run.CurrentRoom?.RoomType, players = run.Players.Select(p => p.ToSerializable()), rng = run.Rng.ToSerializable(),
            personalBag = PredictorLiveRunCapture.Bag(player.RelicGrabBag), sharedBag = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag)
        });
        string initial = Signature();
        object? Field(object instance, string name) => PredictorOperationCapture.ReadNullable(instance, name);
        NeowOperationHoverController? Controller() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => Controller()?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        NRelicInventoryHolder Holder(RelicModel relic) => inventory.RelicNodes.Single(holder => ReferenceEquals(holder.Relic.Model, relic));
        NHoverTipSet? NativeTip(NRelicInventoryHolder holder) => hoverRoot.GetChildren().OfType<NHoverTipSet>()
            .FirstOrDefault(tip => ReferenceEquals(Field(tip, "_owner"), holder));
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var timer = Stopwatch.StartNew();
            while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            Check(ready(), reason);
        }
        void Move(Vector2 point) => viewport.PushInput(new InputEventMouseMotion
        { Position = point, GlobalPosition = point }, true);
        async Task Leave()
        {
            Move(new(4, viewport.Size.Y - 4));
            Check(View() == null && (Controller() is not { } controller || Field(controller, "_owner") == null),
                "native relic unfocus clears the result immediately");
            await Frames();
        }
        async Task Hover(RelicModel relic)
        {
            await Leave();
            var holder = Holder(relic);
            Move(holder.GetGlobalRect().GetCenter());
            Check(Controller() is { } controller && ReferenceEquals(Field(controller, "_owner"), holder) && NativeTip(holder) != null,
                relic.Id.Entry + " real inventory holder and native tooltip share the pointer owner");
        }
        async Task Verify(string name, RelicModel relic, OperationPreviewAction action, string[] labels,
            string[] localized, CardModel[] expected, bool compareIds, bool focus = true)
        {
            string before = Signature();
            if (focus) await Hover(relic);
            int seen = _neowSeenCalls;
            await Until(() => View() is { Visible: true, Modulate.A: > .99f }, name + " native focus publishes without forced controller updates");
            var view = View()!;
            var preview = (OperationPreview)Field(view, "_preview")!;
            var rows = preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows).ToArray();
            var cards = rows.SelectMany(row => row.Cards).ToArray();
            Check(preview.Source == PredictorLiveRunCapture.Key(relic) && preview.Faults.IsEmpty &&
                rows.All(row => row.Action == action && !row.ShowCards) && rows.Select(row => row.Label).SequenceEqual(labels),
                name + " shows the correct source, timing and action in text rows");
            Check(cards.Select(card => (card.Key, card.UpgradeLevel)).SequenceEqual(expected.Select(card =>
                    (PredictorLiveRunCapture.Key(card), card.CurrentUpgradeLevel + (card.IsUpgradable ? 1 : 0)))) &&
                (!compareIds || cards.Select(card => card.Id).SequenceEqual(expected.Select(PredictorLiveRunCapture.Id))),
                name + " card identity, order and upgrade match native inputs using cloned RNG");
            var text = Descendants(view).OfType<Label>().Select(label => label.Text).ToArray();
            Check(localized.All(label => text.Contains(label)) && text.Contains(relic.Title.GetFormattedText()) &&
                cards.All(card => text.Any(line => line.EndsWith(" · " + OperationNativeCardDisplay.CardName(card), StringComparison.Ordinal))) &&
                !Descendants(view).OfType<MegaCrit.Sts2.Core.Nodes.Cards.NCard>().Any(),
                name + " displays Chinese timing and separate upgraded card names as text");
            var nativeText = NativeTip(Holder(relic))!.GetNode<Control>("textHoverTipContainer").GetGlobalRect();
            Check(view.VisibleRegions.Count > 0 && view.VisibleRegions.All(region =>
                viewport.GetVisibleRect().Grow(.5f).Encloses(region) && !region.Intersects(nativeText) &&
                !inventory.RelicNodes.Any(holder => region.Intersects(holder.GetGlobalRect()))),
                name + " keeps the night-blue panel beside the original tooltip and outside the relic bar");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string screenshot = "held-relic-" + name + "-zh.png";
            Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, screenshot)) == Error.Ok, name + " native viewport screenshot saved");
            Check(before == Signature() && seen == _neowSeenCalls, name + " leaves the live run and RNG unchanged");
            cases.Add(new { name, screenshot, source = relic.Id.Entry, text, cardIds = cards.Select(card => card.Key.Serialized).ToArray() });
        }
        try
        {
            runtimeProperty.SetValue(null, runtime with { Detection = detection });
            foreach (var relic in added) player.AddRelicInternal(relic, silent: true);
            rod.CombatsSeen = 1;
            storedCards.AddRange([ModelDb.Card<Anger>().ToMutable().ToSerializable(),
                ModelDb.Card<ShrugItOff>().ToMutable().ToSerializable(), ModelDb.Card<IronWave>().ToMutable().ToSerializable()]);
            RefreshToothCounter();
            // The original run scene places its native relic inventory at (12, 82).
            game.AddChild(inventory);
            inventory.SetProcessInput(false); // The host has no game-wide debug hide-top-bar action.
            inventory.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            inventory.OffsetLeft = 12; inventory.OffsetTop = 82; inventory.OffsetRight = -84; inventory.OffsetBottom = -478;
            inventory.AddThemeConstantOverride("h_separation", 0); inventory.AddThemeConstantOverride("v_separation", 0);
            set(global, nameof(NGlobalUi.RelicInventory), inventory);
            var addHolder = AccessTools.Method(typeof(NRelicInventory), "Add");
            foreach (var relic in player.Relics) addHolder.Invoke(inventory, [relic, true, -1]);
            await Frames();
            Check(LocManager.Instance.Language == "zhs", "fixture uses the native Chinese locale");

            CardModel[] Upgradable() => player.Deck.Cards.Where(card => card.IsUpgradable).ToArray();
            CardModel FishingChoice() => new Rng(nativeRng.ToSerializable()).NextItem(Upgradable())!;
            CardModel[] ReturnOrder()
            {
                var remaining = tooth.SerializableCards.ToList();
                var first = new Rng(player.PlayerRng.Rewards.ToSerializable()).NextItem(remaining)!;
                remaining.Remove(first);
                return (remaining.Count == 1 ? new[] { first, remaining[0] } : [first]).Select(CardModel.FromSerializable).ToArray();
            }

            await Verify("war-hammer", hammer, OperationPreviewAction.UpgradeCard, ["HeldRelicEliteVictory"],
                [uiText.Get("operation.heldRelic.eliteVictory")], Upgradable().ToList().StableShuffle(new Rng(nativeRng.ToSerializable()))
                    .Take(hammer.DynamicVars.Cards.IntValue).ToArray(), compareIds: true);
            await Verify("fishing-rod-wait", rod, OperationPreviewAction.UpgradeCard, ["HeldRelicFishingWait:2"],
                [uiText.Format("operation.heldRelic.fishingWait", 2)], [FishingChoice()], compareIds: true);
            var previousView = View();
            var firstFishing = FishingChoice();
            for (int i = 0; i < 16 && ReferenceEquals(firstFishing, FishingChoice()); i++) nativeRng.NextInt();
            Check(!ReferenceEquals(firstFishing, FishingChoice()), "cloned cursor probe changes the fishing target while pointer stays put");
            await Until(() => !ReferenceEquals(View(), previousView), "changed native RNG replaces the still-hovered prediction");
            await Verify("fishing-rod-refresh", rod, OperationPreviewAction.UpgradeCard, ["HeldRelicFishingWait:2"],
                [uiText.Format("operation.heldRelic.fishingWait", 2)], [FishingChoice()], compareIds: true, focus: false);
            await Leave();
            nativeRng.LoadFromSerializable(originalRng);

            rod.CombatsSeen = 2;
            run.PushRoom(room);
            object turn = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(turnField.FieldType);
            set(turn, "IsInProgress", true);
            turnField.SetValue(CombatManager.Instance, turn);
            await Verify("fishing-rod-this-victory", rod, OperationPreviewAction.UpgradeCard, ["HeldRelicThisVictory"],
                [uiText.Get("operation.heldRelic.thisVictory")], [FishingChoice()], compareIds: true);
            turnField.SetValue(CombatManager.Instance, originalTurn);
            await Until(() => View() == null, "combat settlement transition dismisses a still-hovered forecast");
            run.PopCurrentRoom();

            await Verify("paels-tooth-three", tooth, OperationPreviewAction.ReturnCard, ["HeldRelicNextReturn"],
                [uiText.Get("operation.heldRelic.nextReturn")], ReturnOrder(), compareIds: false);
            await Leave();
            storedCards.RemoveAt(2);
            RefreshToothCounter();
            await Verify("paels-tooth-two", tooth, OperationPreviewAction.ReturnCard, ["HeldRelicNextReturn", "HeldRelicLastReturn"],
                [uiText.Get("operation.heldRelic.nextReturn"), uiText.Get("operation.heldRelic.lastReturn")], ReturnOrder(), compareIds: false);
            await Leave();

            run.PushRoom(orobas);
            var dagger = ModelDb.Card<DingyDagger>().ToMutable();
            for (int i = 0; i < 8; i++) { dagger.UpgradeInternal(); dagger.FinalizeUpgradeInternal(); }
            var guilty = (Guilty)ModelDb.Card<Guilty>().ToMutable();
            guilty.CombatsSeen = 2;
            foreach (var card in player.Deck.Cards.ToArray()) player.Deck.RemoveInternal(card, silent: true);
            foreach (var card in new[] { dagger, guilty })
            {
                run.AddCard(card, player); player.Deck.AddInternal(card, silent: true); addedCards.Add(card);
            }
            set(global, nameof(NGlobalUi.CapstoneContainer), capstone);
            set(capstone, nameof(NCapstoneContainer.CurrentCapstoneScreen), deckScreen);
            await Verify("fishing-rod-orobas-dagger", rod, OperationPreviewAction.UpgradeCard, ["HeldRelicFishingWait:1"],
                [uiText.Format("operation.heldRelic.fishingWait", 1)], [FishingChoice()], compareIds: true);
            var daggerPreview = ((OperationPreview)Field(View()!, "_preview")!).Branches.Single().Sections.Single().Rows.Single().Cards.Single();
            var nativeUpgrade = (DingyDagger)CardModel.FromSerializable(dagger.ToSerializable());
            nativeUpgrade.UpgradeInternal(); nativeUpgrade.FinalizeUpgradeInternal();
            Check(daggerPreview.PermanentCostOverride == null && OperationNativeCardDisplay.CardName(daggerPreview) == nativeUpgrade.Title,
                "high-level dagger retains canonical cost and the game's upgraded title");
            await Leave();
            set(capstone, nameof(NCapstoneContainer.CurrentCapstoneScreen), null);
            set(global, nameof(NGlobalUi.CapstoneContainer), originalCapstone);
            player.Deck.RemoveInternal(dagger, silent: true);
            await Hover(rod);
            await Until(() => View() is { Visible: true, Modulate.A: > .99f }, "fishing rod with no eligible card still publishes a panel");
            var noTarget = (OperationPreview)Field(View()!, "_preview")!;
            Check(noTarget.Branches.Single().Sections.Single().EmptyReason == "HeldRelicNoUpgradeTarget" &&
                Descendants(View()!).OfType<Label>().Any(label => label.Text == uiText.Get("operation.heldRelic.noUpgradeTarget")),
                "empty fishing pool shows its actual reason");
            await Leave();

            var guiltySnapshot = PredictorLiveRunCapture.Card(guilty,
                new PredictorCatalog([], []) { CardDefinitions = [CrystalSphereCatalogCapture.CaptureCardDefinition(
                    ModelDb.Card<Guilty>(), PredictorSeedCapture.CaptureLevels)] });
            string beforeCardFaces = Signature();
            var previewFaces = new HBoxContainer { Position = new(830, 180), MouseFilter = Control.MouseFilterEnum.Ignore };
            hoverRoot.AddChild(previewFaces);
            try
            {
                foreach (var snapshot in new[] { daggerPreview, guiltySnapshot })
                    previewFaces.AddChild(OperationNativeCardDisplay.Create(snapshot, .72f));
                await Frames();
                var cards = Descendants(previewFaces).OfType<NCard>().ToArray();
                Check(cards.Length == 2 && cards.All(card => card.IsVisibleInTree() && card.Model!.Portrait != null) &&
                    cards[0].GetNode<MegaLabel>("%TitleLabel").Text == nativeUpgrade.Title &&
                    cards[0].Model!.DynamicVars.Damage.BaseValue == nativeUpgrade.DynamicVars.Damage.BaseValue &&
                    cards[1].Model is Guilty { CombatsSeen: 2 } curse && curse.DynamicVars["Combats"].IntValue == 3,
                    "native dagger and Guilty faces show the upgraded art/title and recorded card values");
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-dagger-and-guilty-zh.png")) == Error.Ok,
                    "native card faces screenshot saved");
                Check(beforeCardFaces == Signature(), "card faces never modify their source cards or RNG");
            }
            finally { previewFaces.Free(); }
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            inventory.Free();
            set(global, nameof(NGlobalUi.RelicInventory), originalInventory);
            foreach (var relic in added.Where(player.Relics.Contains)) player.RemoveRelicInternal(relic, silent: true);
            if (ReferenceEquals(run.CurrentRoom, room) || ReferenceEquals(run.CurrentRoom, orobas)) run.PopCurrentRoom();
            set(global, nameof(NGlobalUi.CapstoneContainer), originalCapstone);
            deckScreen.Free(); capstone.Free();
            foreach (var card in addedCards)
            {
                if (player.Deck.Cards.Contains(card)) player.Deck.RemoveInternal(card, silent: true);
                run.RemoveCard(card);
            }
            if (addedCards.Count > 0)
                foreach (var card in originalDeck) player.Deck.AddInternal(card, silent: true);
            turnField.SetValue(CombatManager.Instance, originalTurn);
            nativeRng.LoadFromSerializable(originalRng);
            runtimeProperty.SetValue(null, originalRuntime);
            bool restored = initial == Signature() && ReferenceEquals(run.CurrentRoom, originalRoom);
            checks++;
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, failure, restored, checks,
                version = detection.NormalizedVersion, cases, controlledCombatPhase = true, nativeCombatHooksExecuted = false,
                ownerRuntimeAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
            if (!restored) throw new InvalidOperationException("HeldRelicHoverUi: fixture did not restore original native state and RNG");
        }
        GD.Print($"HELD_RELIC_HOVER_UI_PASS checks={checks};cases={cases.Count};nativeFocus=true;cloneOracle=true");
    }
}
