using System.Diagnostics;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Reuses the already mounted normal merchant, original slot scenes and
    // controller. Controlled changes affect this isolated fixture only and are
    // restored; admission/prediction itself must leave each state untouched.
    private static async Task RunOperationAdmissionChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        MerchantInventory inventory, NMerchantInventory shop, NMerchantCard cardSlot,
        Func<string> signature, Action<bool, string> check)
    {
        string receipt = Path.Combine(output, "operation-admission-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        var evidence = new List<object>();
        var courier = player.Relics.OfType<TheCourier>().Single();
        bool originalMelted = courier.IsMelted;
        var originalDeck = player.Deck.Cards.ToArray();
        var originalPlayerRng = player.PlayerRng.ToSerializable();
        var relicEntries = PredictorOperationCapture.Read<List<MerchantRelicEntry>>(inventory, "_relicEntries");
        var originalRelicEntry = relicEntries[0];
        string original = signature();
        NMerchantPotion? potionSlot = null;
        NMerchantRelic? relicSlot = null;
        NeowOperationHoverController? Controller() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => Controller()?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Move(Vector2 point)
        {
            viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Frames();
        }
        async Task Leave()
        {
            await Move(new(4, viewport.Size.Y - 4));
            check(View() == null, "admission fixture native unfocus removes any prior prediction");
        }
        async Task Until(Func<bool> ready, string reason)
        {
            var timer = Stopwatch.StartNew();
            while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(8)) await Frames(1);
            check(ready(), reason);
        }
        void Admit(NMerchantSlot slot, bool expected, string reason)
        {
            string before = signature();
            bool admitted = OperationSurfaceHoverCapture.TryShop(slot, out var capturedRun, out _, out _);
            check(admitted == expected && (!admitted || ReferenceEquals(capturedRun, run)), reason);
            check(signature() == before, reason + " leaves native state and RNG unchanged");
            evidence.Add(new { kind = "shop", item = slot.Entry.GetType().Name, admitted, reason });
        }
        async Task RejectHover(NMerchantSlot slot, Control hitbox, string reason)
        {
            await Leave();
            string before = signature();
            await Move(hitbox.GetGlobalRect().GetCenter());
            check(View() == null && (Controller() == null ||
                PredictorOperationCapture.ReadNullable(Controller()!, "_owner") == null &&
                PredictorOperationCapture.ReadNullable(Controller()!, "_work") == null), reason);
            check(signature() == before, reason + " leaves native state and RNG unchanged");
            await Leave();
        }
        try
        {
            await Leave();
            potionSlot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_potion.tscn").Instantiate<NMerchantPotion>();
            shop.AddChild(potionSlot); potionSlot.Position = new(1400, 760);
            potionSlot.Initialize(shop); potionSlot.FillSlot(inventory.PotionEntries[0]);
            relicSlot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
            shop.AddChild(relicSlot); relicSlot.Position = new(1390, 690); relicSlot.Initialize(shop);
            void Offer(RelicModel model)
            {
                var entry = new MerchantRelicEntry(model, player);
                relicEntries[0] = entry; relicSlot.FillSlot(entry);
            }
            Offer(ModelDb.Relic<Anchor>().ToMutable());
            await Frames(4);
            courier.IsMelted = true;
            Admit(cardSlot, false, "ordinary card without active Courier has no hidden purchase output");
            Admit(potionSlot, false, "ordinary potion without active Courier has no hidden purchase output");
            Admit(relicSlot, false, "passive native Anchor without Courier has no hidden obtain output");
            await RejectHover(cardSlot, cardSlot.GetNode<Control>("%Hitbox"), "rejected card native hover starts no worker or capture");
            await RejectHover(potionSlot, potionSlot.Hitbox, "rejected potion native hover starts no worker or capture");
            await RejectHover(relicSlot, relicSlot.Hitbox, "rejected passive relic native hover starts no worker or capture");
            foreach (var model in new RelicModel[] { ModelDb.Relic<Strawberry>(), ModelDb.Relic<Pear>(), ModelDb.Relic<Mango>(),
                ModelDb.Relic<NutritiousOyster>(), ModelDb.Relic<OldCoin>(), ModelDb.Relic<GoldenPearl>(),
                ModelDb.Relic<SignetRing>(), ModelDb.Relic<PotionBelt>(), ModelDb.Relic<PumpkinCandle>() })
                check(!OperationSurfaceHoverCapture.MayDisplayRelicObtain(model), model.Id.Entry + " public native override has no projection output");
            foreach (var model in new RelicModel[] { ModelDb.Relic<Strawberry>(), ModelDb.Relic<OldCoin>(), ModelDb.Relic<PotionBelt>() })
            {
                Offer(model.ToMutable());
                Admit(relicSlot, false, model.Id.Entry + " public obtain stays out without Courier");
                courier.IsMelted = false;
                Admit(relicSlot, true, model.Id.Entry + " public obtain preserves active Courier restock");
                courier.IsMelted = true;
            }
            foreach (var model in new RelicModel[] { ModelDb.Relic<Whetstone>(), ModelDb.Relic<WarPaint>(),
                ModelDb.Relic<Orrery>(), ModelDb.Relic<Cauldron>(), ModelDb.Relic<NewLeaf>(), ModelDb.Relic<NeowsBones>() })
            {
                check(OperationSurfaceHoverCapture.MayDisplayRelicObtain(model), model.Id.Entry + " retains its native obtain projection");
                Offer(model.ToMutable()); Admit(relicSlot, true, model.Id.Entry + " remains eligible without Courier");
            }
            foreach (var model in new RelicModel[] { ModelDb.Relic<FrozenEgg>(), ModelDb.Relic<WingCharm>(),
                ModelDb.Relic<SilverCrucible>(), ModelDb.Relic<SilkenTress>(), ModelDb.Relic<FresnelLens>() })
                check(OperationSurfaceHoverCapture.MayDisplayRelicObtain(model), model.Id.Entry + " retains existing-card-reward notification effects");
            // Native ordinary inventories exclude Courier, but a supplied actual
            // offer would activate its refill before this purchase finishes.
            Offer(ModelDb.Relic<TheCourier>().ToMutable());
            Admit(relicSlot, true, "buying Courier itself preserves this purchase's possible refill");
            Offer(ModelDb.Relic<Anchor>().ToMutable());
            courier.IsMelted = false;
            Admit(cardSlot, true, "active Courier preserves native card purchase restock");
            Admit(potionSlot, true, "active Courier preserves native potion purchase restock");
            Admit(relicSlot, true, "active Courier preserves passive relic purchase restock");

            // A real Whetstone worker can currently have no targets. Its empty
            // result avoids publication capture but must keep observing a deck
            // that can later gain an eligible target, without another focus.
            await Leave(); courier.IsMelted = true;
            Offer(ModelDb.Relic<Whetstone>().ToMutable());
            var attack = originalDeck.First(c => c.Type == CardType.Attack && c.IsUpgradable);
            var other = originalDeck.First(c => c.Type != CardType.Attack);
            player.Deck.Clear(silent: true); player.Deck.AddInternal(other, silent: true);
            string emptyState = signature();
            await Move(relicSlot.Hitbox.GetGlobalRect().GetCenter());
            await Until(() => Controller() is { } controller &&
                ReferenceEquals(PredictorOperationCapture.ReadNullable(controller, "_owner"), relicSlot) &&
                PredictorOperationCapture.ReadNullable(controller, "_snapshot") is PredictorOperationSnapshot &&
                PredictorOperationCapture.ReadNullable(controller, "_work") == null,
                "actual Whetstone native hover completes its empty detached worker");
            var timing = PredictorOperationCapture.Read<NeowOperationHoverController.HoverTiming>(Controller()!, "_timing");
            check(View() == null && timing.WorkerMs > 0 && timing.Publication.AllocatedBytes == 0 && timing.PublishCaptureMs == 0,
                "empty actual worker leaves no view and performs no publication capture");
            check(signature() == emptyState, "empty actual worker leaves its native deck, inventory and RNG unchanged");
            int polls = timing.PollCount;
            player.Deck.AddInternal(attack, silent: true);
            string changedState = signature();
            await Until(() => View() is { Visible: true } view &&
                PredictorOperationCapture.ReadNullable(view, "_preview") is OperationPreview preview &&
                preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                    .Any(row => row.Action == OperationPreviewAction.UpgradeCard &&
                        row.Cards.Any(card => card.Id == PredictorLiveRunCapture.Id(attack))),
                "same native Whetstone hover observes added attack and automatically publishes its upgrade");
            check(signature() == changedState, "automatic Whetstone recovery leaves changed native state and RNG unchanged");
            evidence.Add(new { kind = "empty-worker-recovery", timing.WorkerMs, timing.PublishCaptureMs,
                publicationAllocatedBytes = timing.Publication.AllocatedBytes, initialPolls = polls,
                cardId = PredictorLiveRunCapture.Id(attack), recoveredWithoutRefocus = true });
            await Leave();
            player.Deck.Clear(silent: true);
            foreach (var card in originalDeck) player.Deck.AddInternal(card, silent: true);
            courier.IsMelted = originalMelted; relicEntries[0] = originalRelicEntry;
            player.PlayerRng.LoadFromSerializable(originalPlayerRng);

            var basis = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            var publicCases = new (EventModel Event, string Option)[] {
                (ModelDb.Event<MorphicGrove>(), "LONER"), (ModelDb.Event<DenseVegetation>(), "TRUDGE_ON"),
                (ModelDb.Event<SlipperyBridge>(), "OVERCOME"), (ModelDb.Event<Trial>(), "ACCEPT"),
                (ModelDb.Event<TabletOfTruth>(), "SMASH") };
            void EmptyEvent(EventModel ev, PredictorEventOption option, bool tinker = false, int tablet = 0)
            {
                check(!OperationSurfaceHoverCapture.EventMayDisplay(ev, option), ev.Id.Entry + ":" + option.Key + " native lightweight admission rejects public output");
                var scene = new PredictorOperationEventScene(new("EVENT", ev.Id.Entry), basis.State.Streams[0],
                    [option], new(basis.CurrentHp, basis.MaximumHp))
                { TinkerType = tinker ? EffectCardType.Attack : null, TabletCount = tablet };
                var preview = EventOperationPreview.Build(basis with { Scene = scene }, option.Key);
                check(!NeowOperationOverlay.HasVisibleContent(preview) && preview.Faults.IsEmpty,
                    ev.Id.Entry + ":" + option.Key + " shared worker policy preserves its empty projection");
                evidence.Add(new { kind = "event", ev.Id.Entry, option.Key, tinker, tablet, admitted = false });
            }
            foreach (var item in publicCases) EmptyEvent(item.Event, new(item.Option));
            var tinker = ModelDb.Event<TinkerTime>().ToMutable();
            AccessTools.Property(typeof(EventModel), nameof(EventModel.Description)).SetValue(tinker,
                new LocString("events", "TINKER_TIME.pages.CHOOSE_RIDER.description"));
            EmptyEvent(tinker, new("SAPPING"), tinker: true);
            var tablet = ModelDb.Event<TabletOfTruth>().ToMutable();
            AccessTools.Field(typeof(TabletOfTruth), "_decipherCount").SetValue(tablet, 4);
            EmptyEvent(tablet, new("DECIPHER"), tablet: 4);
            EmptyEvent(ModelDb.Event<DollRoom>(), new("DOLL_BING_BONG", OfferedRelic: new("RELIC", "BING_BONG")));
            foreach (string verdict in new[] { "MERCHANT_GUILTY", "NONDESCRIPT_GUILTY", "NONDESCRIPT_INNOCENT" })
                check(OperationSurfaceHoverCapture.EventMayDisplay(ModelDb.Event<Trial>(), new(verdict)), "Trial actual verdict remains eligible: " + verdict);
            check(OperationSurfaceHoverCapture.EventMayDisplay(ModelDb.Event<MorphicGrove>(), new("GROUP")) &&
                OperationSurfaceHoverCapture.EventMayDisplay(ModelDb.Event<SlipperyBridge>(), new("HOLD_ON")) &&
                OperationSurfaceHoverCapture.EventMayDisplay(ModelDb.Event<DollRoom>(), new("EXAMINE")),
                "hidden event transformations, future bridge removals and unrevealed Doll choices remain eligible");
            AccessTools.Field(typeof(TabletOfTruth), "_decipherCount").SetValue(tablet, 3);
            check(OperationSurfaceHoverCapture.EventMayDisplay(tablet, new("DECIPHER")), "fourth Tablet decipher remains eligible");
            check(OperationPreviewEligibility.RoyaltiesBattleOption("DENSE_VEGETATION", "REST") &&
                OperationPreviewEligibility.RoyaltiesBattleOption("BATTLEWORN_DUMMY", "SETTING_2") &&
                !OperationPreviewEligibility.RoyaltiesBattleOption("DENSE_VEGETATION", "TRUDGE_ON") &&
                !OperationPreviewEligibility.RoyaltiesBattleOption("MORPHIC_GROVE", "GROUP"),
                "Royalties shared predicate retains only actual combat entrances");
            check(signature() == original, "admission checks restore their entire native fixture and leave all prediction calls pure");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = true, nativeAdmission = true,
                actualEmptyWorkerRecovery = true, ownerRuntimeAcceptance = false, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (Controller() is { } controller && PredictorOperationCapture.ReadNullable(controller, "_owner") is Control owner)
                NeowOperationHoverController.Leave(owner);
            courier.IsMelted = originalMelted;
            player.Deck.Clear(silent: true);
            foreach (var card in originalDeck) player.Deck.AddInternal(card, silent: true);
            relicEntries[0] = originalRelicEntry;
            player.PlayerRng.LoadFromSerializable(originalPlayerRng);
            potionSlot?.Free(); relicSlot?.Free();
        }
    }
}
