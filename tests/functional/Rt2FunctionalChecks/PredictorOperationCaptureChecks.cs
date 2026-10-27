using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Multiplayer.Game;

internal static class PredictorOperationCaptureChecks
{
    private static void Check(bool condition, string why)
    { if (!condition) throw new InvalidOperationException("OPERATION_CAPTURE " + why); }
    private static string Native(RunState run) => JsonSerializer.Serialize(new
    {
        Players = run.Players.Select(p => p.ToSerializable()).ToArray(), Rng = run.Rng.ToSerializable(),
        Bag = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
        Personal = run.Players.Select(p => PredictorLiveRunCapture.Bag(p.RelicGrabBag)).ToArray()
    }, new JsonSerializerOptions { IncludeFields = true });

    internal static void Run(PredictorRun baseline)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", members)!;
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", members)!;
        var previousState = stateProperty.GetValue(manager);
        var previousAscension = ascensionProperty.GetValue(manager);
        ulong? previousLocal = LocalContext.NetId;
        try
        {
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var partner = Player.CreateForNewRun<Silent>(UnlockState.all, 2);
            var native = RunState.CreateForTest([player, partner], baseline.Working.Acts
                .Select(a => ModelDb.GetById<ActModel>(new(a.Key.Category, a.Key.Entry))).ToArray(),
                seed: baseline.Context.Seed, ascensionLevel: baseline.Context.Ascension);
            stateProperty.SetValue(manager, native);
            ascensionProperty.SetValue(manager, new AscensionManager(native.AscensionLevel));
            LocalContext.NetId = player.NetId;
            native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), native.Rng.UpFront);
            foreach (var owner in native.Players) owner.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
            manager.GenerateRooms();
            player.Gold = 350;
            var first = ModelDb.Card<StrikeIronclad>().ToMutable();
            var second = ModelDb.Card<StrikeIronclad>().ToMutable();
            native.AddCard(first, player); player.Deck.AddInternal(first);
            native.AddCard(second, player); player.Deck.AddInternal(second);
            var wing = (PaelsWing)ModelDb.Relic<PaelsWing>().ToMutable();
            wing.RewardsSacrificed = 1; player.AddRelicInternal(wing);
            var inventory = new MerchantInventory(player);
            var relicEntry = new MerchantRelicEntry(ModelDb.Relic<Whetstone>().ToMutable(), player);
            inventory.AddRelicEntry(relicEntry);
            var room = new MerchantRoom();
            room.Inventories.Add(inventory);
            typeof(MerchantRoom).GetField("_runState", members)!.SetValue(room, native);
            native.PushRoom(room);

            string before = Native(native);
            var captured = PredictorOperationCapture.Capture(native, "0.111.0");
            Check(Native(native) == before, "capture preserves native players, RNG and bags");
            Check(captured.Context.Live == null && captured.Context.Crystal == null && captured.Context.AncientAuthority == null,
                "operation capture does not require route departure or seed authority");
            Check(captured.Context.Operation is { PlayerCount: 2, PlayerSlot: 0 } op && op.PlayerId == player.NetId,
                "actual local player and multiplayer mode retained");
            var massiveNative = player.Character.CardPool.GetUnlockedCards(player.UnlockState, ((IRunState)native).CardMultiplayerConstraint)
                .Concat(ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>().GetUnlockedCards(player.UnlockState,
                    ((IRunState)native).CardMultiplayerConstraint))
                .Where(c => c.MultiplayerConstraint == MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.MultiplayerOnly)
                .Select(PredictorLiveRunCapture.Key).ToArray();
            Check(massiveNative.Length > 0 && captured.Context.Operation!.CombatCardPools!.CharacterCards
                .Concat(captured.Context.Operation.CombatCardPools.ColorlessCards).Where(c => c.MultiplayerConstraint == "MultiplayerOnly")
                .Select(c => c.Card.Key).SequenceEqual(massiveNative), "Massive Scroll receives actual ordered two-player-only pool identities");
            var shop = (PredictorOperationShopScene)captured.Scene;
            Check(shop.Entries is [{ Kind: PredictorShopEntryKind.Relic, Stocked: true }] &&
                shop.Entries[0].BaseCost == PredictorOperationCapture.Read<int>(relicEntry, "_cost"),
                "actual inventory and raw generated price copied without Cost getter");
            Check(captured.State.Relics.Single(r => r.Key.Entry == "PAELS_WING").RewardsSacrificed == 1,
                "real sacrifice counter copied");
            var nativeBones = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().AllPossibleOptions
                .Where(o => o.Relic != null && o.Relic is not NeowsBones && o.Relic.IsAllowedAtNeow(player))
                .Select(o => PredictorLiveRunCapture.Key(o.Relic!)).ToImmutableArray();
            Check(!captured.Context.OpeningEligibleRelics.IsEmpty && captured.Context.OpeningEligibleRelics.SequenceEqual(nativeBones) &&
                Native(native) == before, "Bones uses native Neow candidate order/mode eligibility without authority setup or RNG mutation");
            PredictorOperationRelicSmokeChecks.Run(captured);
            Check(PredictorOperationCapture.IsCurrent(captured, native, "0.111.0"), "same native scene remains current");
            native.Rng.Niche.NextInt(19);
            Check(!PredictorOperationCapture.IsCurrent(captured, native, "0.111.0"), "teammate/shared RNG change invalidates operation");
            var next = PredictorOperationCapture.Capture(native, "0.111.0");
            player.Gold++;
            Check(!PredictorOperationCapture.IsCurrent(next, native, "0.111.0"), "economy change invalidates operation");

            var reward = new CardReward([first, second], CardCreationSource.Other, player,
                CardCreationOptions.ForNonCombatWithDefaultOdds([player.Character.CardPool]));
            before = Native(native);
            var copied = PredictorOperationCapture.CaptureReward(reward, captured.Context.Catalog!);
            Check(Native(native) == before && copied.Cards.Length == 2 && copied.Cards[0].Id != copied.Cards[1].Id &&
                copied.Cards[0].Key == copied.Cards[1].Key && copied.ManuallySetCards,
                "already generated same-name candidates preserve ordered instance identities without repopulation");
            Check(copied.RerollRecipe is { CardCount: 2, NoUpgradeRoll: true } && !copied.RerollRecipe.CardPoolOverride.IsDefaultOrEmpty,
                "manual reward retains actual independent reroll recipe");
            var rewardScene = new PredictorOperationRewardsScene([copied]);
            string phaseStamp = PredictorOperationCapture.SceneFingerprint(rewardScene);
            Check(phaseStamp != PredictorOperationCapture.SceneFingerprint(rewardScene with
                { Rewards = [copied with { RerollRecipe = copied.RerollRecipe! with { NoUpgradeRoll = false } }] }),
                "recipe changes participate in polymorphic scene fingerprint");
            Check(phaseStamp != PredictorOperationCapture.SceneFingerprint(rewardScene with
                { EventRng = new(1, 2, 3, 4, 0) }), "internal phase/RNG fields participate in freshness");
            CheckOpaqueReward(captured, player);
            var actualPool = new List<CardModel> { ModelDb.Card<DefendIronclad>(), ModelDb.Card<StrikeIronclad>() };
            var actualCardEntry = new MerchantCardEntry(player, inventory, actualPool, MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack);
            var cardEntries = PredictorOperationCapture.Read<List<MerchantCardEntry>>(inventory, "_characterCardEntries");
            cardEntries.Add(actualCardEntry);
            try
            {
                var copiedPool = PredictorOperationCapture.CaptureShop(inventory, captured.Context.Catalog!, true);
                var refill = copiedPool.Entries.Single(e => e.Id == PredictorLiveRunCapture.Id(actualCardEntry)).OperationRefill!;
                Check(refill.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack &&
                    refill.Pool.Select(c => c.CardKey).SequenceEqual(actualPool.Select(PredictorLiveRunCapture.Key)),
                    "shop captures actual entry pool ordering and declared replacement type even when unstocked");
                actualPool.Reverse();
                Check(PredictorOperationCapture.SceneFingerprint(copiedPool) != PredictorOperationCapture.SceneFingerprint(
                    PredictorOperationCapture.CaptureShop(inventory, captured.Context.Catalog!, true)),
                    "entry refill pool changes invalidate detached scene fingerprint");
            }
            finally { cardEntries.Remove(actualCardEntry); }

            // Controlled native current-room fixture verifies the custom shop
            // entry routing; no claim that Fake Merchant normally admits MP.
            var eventSyncProperty = typeof(RunManager).GetProperty("EventSynchronizer", members)!;
            var priorEventSync = eventSyncProperty.GetValue(manager);
            var fake = (MegaCrit.Sts2.Core.Models.Events.FakeMerchant)ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.FakeMerchant>().ToMutable();
            typeof(MegaCrit.Sts2.Core.Models.Events.FakeMerchant).GetField("_inventory", members)!.SetValue(fake, inventory);
            var fakeSync = (MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer));
            typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer).GetField("_playerCollection", members)!.SetValue(fakeSync, native);
            typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer).GetField("_localPlayerId", members)!.SetValue(fakeSync, player.NetId);
            typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer).GetField("_events", members)!.SetValue(fakeSync, new List<EventModel> { fake });
            native.PushRoom(new EventRoom(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.FakeMerchant>()));
            try
            {
                eventSyncProperty.SetValue(manager, fakeSync);
                var fakeSnapshot = PredictorOperationCapture.Capture(native, "0.111.0");
                Check(fakeSnapshot.Scene is PredictorOperationShopScene { IsMerchantRoom: false } fakeShop &&
                    fakeShop.Entries[0].Id == shop.Entries[0].Id,
                    "Fake Merchant current event dispatch copies actual inventory with native room-price/refill premise");
            }
            finally { native.PopCurrentRoom(); eventSyncProperty.SetValue(manager, priorEventSync); }

            // Controlled native residue: construction/setup models the already
            // committed payment+inventory boundary; this does not claim a live
            // screen or a native coroutine was executed by the fixture.
            var cage = ModelDb.Relic<EmptyCage>().ToMutable();
            player.AddRelicInternal(cage);
            var paidEntry = new MerchantRelicEntry(cage, player);
            var paidState = PredictorOperationCapture.Capture(native, "0.111.0");
            inventory.AddRelicEntry(paidEntry);
            var ids = player.Deck.Cards.Select(PredictorLiveRunCapture.Id).Take(2).ToImmutableArray();
            var residue = PredictorOperationCapture.CaptureDeckObtainChoice(cage,
                new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 2), ids);
            Check(residue.Source.Entry == "EMPTY_CAGE" && residue.RelicId == PredictorLiveRunCapture.Id(cage) &&
                residue.CandidateIds.SequenceEqual(ids), "native suspended choice preserves acquired source and ordered actual candidates");
            var paidScene = PredictorOperationCapture.CaptureShop(inventory, captured.Context.Catalog!, false);
            paidState = paidState with { State = paidState.State with { NextInstanceId = PredictorLiveRunCapture.NextInstanceId } };
            paidScene = paidScene with { PendingRelic = new(PredictorLiveRunCapture.Id(paidEntry), player.Gold + 100, false, residue) };
            before = Native(native);
            var waiting = PredictorOperation.Preview(paidState with { Scene = paidScene }, ImmutableArray<PredictorInput>.Empty);
            Check(waiting.Waiting.Phase == PredictorPhase.ObtainDeckCards &&
                waiting.State.GoldBudget == paidState.State.GoldBudget &&
                waiting.State.Relics.Count(r => r.Key.Entry == "EMPTY_CAGE") == 1 && Native(native) == before,
                "restoring actual paid obtain residue waits without payment/add/real-state mutation");
            PredictorOperationCapture.Read<List<MerchantRelicEntry>>(inventory, "_relicEntries").Remove(paidEntry);
            PredictorOperationEventCaptureChecks.Run(native, captured.Context.Catalog!);
            SuspendedNativePurchase(native, inventory);
            FreshRewardCallback(native);
        }
        finally
        {
            stateProperty.SetValue(manager, previousState);
            ascensionProperty.SetValue(manager, previousAscension);
            LocalContext.NetId = previousLocal;
        }
        Console.WriteLine("OPERATION_CAPTURE passed native read-only, actual inventory/reward instances, mode and shared-RNG freshness checks");
    }

    private static void CheckOpaqueReward(PredictorOperationSnapshot captured, Player player)
    {
        var opaque = new ForeignRewardFixture(player);
        var linkedGold = new GoldReward(17, player);
        var independentGold = new GoldReward(19, player);
        var linked = new LinkedRewardSet([opaque, linkedGold], player);
        var set = new RewardsSet(player).WithCustomRewards([linked, independentGold]);
        string before = Native((RunState)player.RunState);
        var scene = PredictorOperationCapture.CaptureRewardsSet(set, player, captured.Context.Catalog!);
        long opaqueId = PredictorLiveRunCapture.Id(opaque);
        long linkedGoldId = PredictorLiveRunCapture.Id(linkedGold);
        long independentId = PredictorLiveRunCapture.Id(independentGold);
        Check(scene.Rewards.Select(r => r.Id).SequenceEqual(new[] { opaqueId, linkedGoldId, independentId }) &&
            scene.Rewards[0] is { Kind: PredictorRewardKind.Opaque, CanSkip: true, Status: PredictorRewardStatus.Open } copied &&
            copied.OpaqueSourceType == typeof(ForeignRewardFixture).FullName &&
            copied.OpaqueSourceAssembly == typeof(ForeignRewardFixture).Assembly.GetName().Name &&
            scene.LinkedGroups.Single().RewardIds.SequenceEqual(new[] { opaqueId, linkedGoldId }),
            "unknown external reward retains actual identity/order/status/linkage without invoking its callbacks");
        var snapshot = captured with { Scene = scene,
            State = captured.State with { NextInstanceId = PredictorLiveRunCapture.NextInstanceId } };
        var known = PredictorOperation.Preview(snapshot, new TakeReward(independentId));
        Check(known.Disposition == PredictorInputResult.Accepted && known.State.GoldBudget == captured.State.GoldBudget + 19 &&
            known.Rewards.Single(r => r.Id == opaqueId).Status == PredictorRewardStatus.Open,
            "independent known reward still predicts beside unknown external row");
        var omitted = PredictorOperation.Preview(snapshot, new TakeReward(opaqueId));
        Check(omitted.Disposition == PredictorInputResult.Accepted && omitted.State.GoldBudget == captured.State.GoldBudget &&
            omitted.Rewards.Single(r => r.Id == opaqueId).Status == PredictorRewardStatus.Taken &&
            omitted.Rewards.Single(r => r.Id == linkedGoldId).Status == PredictorRewardStatus.Skipped && Native((RunState)player.RunState) == before,
            "explicit opaque choice applies only generic selection/linkage with zero extra effects and no native mutation");
    }

    private sealed class ForeignRewardFixture(Player player) : Reward(player)
    {
        protected override RewardType RewardType => RewardType.None;
        public override int RewardsSetIndex => throw new InvalidOperationException("Opaque callback invoked");
        public override MegaCrit.Sts2.Core.Localization.LocString Description => throw new InvalidOperationException("Opaque callback invoked");
        public override bool IsPopulated => throw new InvalidOperationException("Opaque callback invoked");
        public override void Populate() => throw new InvalidOperationException("Opaque callback invoked");
        protected override Task<bool> OnSelect() => throw new InvalidOperationException("Opaque callback invoked");
        public override void MarkContentAsSeen() => throw new InvalidOperationException("Opaque callback invoked");
    }

    // Uses the existing game's TestSupport selector only to suspend its real
    // purchase/AfterObtained coroutine at a native picker task. This is a native
    // controlled fixture, not a claim of owner acceptance in the game UI.
    private static void SuspendedNativePurchase(RunState native, MerchantInventory inventory)
    {
        var player = inventory.Player;
        foreach (var prior in player.Relics.OfType<DollysMirror>().ToArray()) player.RemoveRelicInternal(prior);
        while (player.Deck.Cards.Count < 3)
        {
            var card = ModelDb.Card<DefendIronclad>().ToMutable();
            native.AddCard(card, player); player.Deck.AddInternal(card);
        }
        player.Gold = 1000;
        var entry = new MerchantRelicEntry(ModelDb.Relic<DollysMirror>().ToMutable(), player);
        inventory.AddRelicEntry(entry);
        var screen = new NDeckCardSelectScreen();
        var tree = (SceneTree)Engine.GetMainLoop();
        var harmony = new Harmony("rt2.operation.capture.fixture." + Guid.NewGuid().ToString("N"));
        Task<bool>? purchase = null;
        try
        {
            PredictorOperationCapture.Install(harmony);
            PredictorOperationEventCapture.Install(harmony);
            _suspendedFixtureScreen = screen;
            harmony.Patch(AccessTools.Method(typeof(NDeckCardSelectScreen), nameof(NDeckCardSelectScreen._Ready)),
                prefix: new(typeof(PredictorOperationCaptureChecks), nameof(SkipFixtureScreenReady)));
            var host = tree.CurrentScene ?? tree.Root.GetChildren().LastOrDefault(n => n.IsInsideTree())
                ?? throw new InvalidOperationException("OPERATION_CAPTURE fixture host missing");
            host.AddChild(screen);
            Check(screen.IsInsideTree(), "native picker attached below current host while root prepares its children");
            using var selected = CardSelectCmd.UseSelector(new SuspendedNativeSelector(screen));
            int goldBefore = player.Gold;
            purchase = entry.OnTryPurchaseWrapper(inventory);
            if (purchase.IsFaulted)
                throw new InvalidOperationException("OPERATION_CAPTURE original merchant purchase failed", purchase.Exception);
            Check(!purchase.IsCompleted && player.Relics.Any(r => ReferenceEquals(r, entry.Model)) && player.Gold < goldBefore,
                "real original merchant coroutine paid/added relic and now awaits its child picker");
            string before = Native(native);
            var snapshot = PredictorOperationCapture.Capture(native, "0.111.0", screen);
            Check(snapshot.Scene is PredictorOperationObtainScene { Parent: PredictorOperationShopScene { PendingPurchase: { } receipt },
                DeckChoice: { } deck } && receipt.BeforeGold == goldBefore && receipt.EntryId == PredictorLiveRunCapture.Id(entry) &&
                deck.RelicId == PredictorLiveRunCapture.Id(entry.Model!) && deck.Count == 1,
                "Harmony observer preserves actual pending purchase/source across original await");
            var waiting = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty);
            Check(waiting.Waiting.Phase == PredictorPhase.ObtainDeckCards && waiting.State.GoldBudget == player.Gold &&
                waiting.State.Relics.Count(r => r.Key.Entry == "DOLLYS_MIRROR") == 1 && Native(native) == before && !purchase.IsCompleted,
                "repeated pending preview leaves original task/payment/relic/picker unchanged");
        }
        finally
        {
            // Cancelling the isolated native test selector ends its awaiting
            // original call through cancellation; it cannot remove real cards
            // or execute a post-obtain purchase a second time.
            PredictorOperationCapture.Read<TaskCompletionSource<IEnumerable<CardModel>>>(screen, "_completionSource").TrySetCanceled();
            harmony.UnpatchAll(harmony.Id);
            _suspendedFixtureScreen = null;
            screen.GetParent()?.RemoveChild(screen); screen.Free();
            PredictorOperationCapture.Read<List<MerchantRelicEntry>>(inventory, "_relicEntries").Remove(entry);
            if (purchase?.IsFaulted == true) _ = purchase.Exception;
        }
    }

    // This bare native picker uses its real completion task; only prefab layout
    // initialization is skipped for this exact fixture instance.
    private static NDeckCardSelectScreen? _suspendedFixtureScreen;
    private static bool SkipFixtureScreenReady(NDeckCardSelectScreen __instance) =>
        !ReferenceEquals(__instance, _suspendedFixtureScreen);

    private static NeowsBones? _fixtureParent;
    private static RewardsSet? _fixtureParentRewards;
    private static RewardsSetSynchronizer? _fixtureParentSync;
    private static TaskCompletionSource? _fixtureParentWait;
    private static bool SuspendFixtureParent(NeowsBones __instance, ref Task __result)
    {
        if (!ReferenceEquals(__instance, _fixtureParent)) return true;
        __result = _fixtureParentSync!.BeginRewardsSet(_fixtureParentRewards!);
        return false;
    }
    private static bool SuspendFixtureRewards(RewardsSet set, ref Task __result)
    {
        if (!ReferenceEquals(set, _fixtureParentRewards)) return true;
        __result = _fixtureParentWait!.Task;
        return false;
    }

    private static void FreshRewardCallback(RunState native)
    {
        var player = LocalContext.GetMe(native)!;
        foreach (var relic in player.Relics.Where(r => r is EmptyCage or NeowsBones).ToArray()) player.RemoveRelicInternal(relic);
        var screen = new NDeckCardSelectScreen();
        var tree = (SceneTree)Engine.GetMainLoop();
        var host = tree.CurrentScene ?? tree.Root.GetChildren().Last(n => n.IsInsideTree());
        var harmony = new Harmony("rt2.operation.reward.callback.fixture." + Guid.NewGuid().ToString("N"));
        Task<RelicModel>? parent = null;
        Task<bool>? child = null;
        try
        {
            PredictorOperationCapture.Install(harmony);
            _suspendedFixtureScreen = screen;
            harmony.Patch(AccessTools.Method(typeof(NDeckCardSelectScreen), nameof(NDeckCardSelectScreen._Ready)),
                prefix: new(typeof(PredictorOperationCaptureChecks), nameof(SkipFixtureScreenReady)));
            host.AddChild(screen);
            Check(screen.IsInsideTree(), "fresh callback fixture picker attached");
            _fixtureParent = (NeowsBones)ModelDb.Relic<NeowsBones>().ToMutable();
            var reward = new RelicReward(ModelDb.Relic<EmptyCage>().ToMutable(), player);
            _fixtureParentRewards = new RewardsSet(player).WithCustomRewards([reward]);
            _fixtureParentWait = new();
            _fixtureParentSync = (RewardsSetSynchronizer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RewardsSetSynchronizer));
            harmony.Patch(AccessTools.Method(typeof(NeowsBones), nameof(NeowsBones.AfterObtained)),
                prefix: new(typeof(PredictorOperationCaptureChecks), nameof(SuspendFixtureParent)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet)),
                prefix: new(typeof(PredictorOperationCaptureChecks), nameof(SuspendFixtureRewards)) { priority = Priority.Last });
            using var selected = CardSelectCmd.UseSelector(new SuspendedNativeSelector(screen));
            var cleanCallback = ExecutionContext.Capture()!;
            parent = RelicCmd.Obtain(_fixtureParent, player);
            if (parent.IsFaulted) throw new InvalidOperationException("OPERATION_CAPTURE fixture parent failed", parent.Exception);
            Check(!parent.IsCompleted && PredictorOperationCapture.ObservedRewardSetObtainSource(_fixtureParentRewards)?.Relic == _fixtureParent,
                "controlled native parent await records its offered reward set");
            // Models the later UI callback on the SAME main thread. No outer
            // obtain AsyncLocal flows into this independent callback context.
            ExecutionContext.Run(cleanCallback, _ => child = reward.SelectUnsynchronized(), null);
            if (child?.IsFaulted == true) throw new InvalidOperationException("OPERATION_CAPTURE fresh reward callback failed", child.Exception);
            var origin = PredictorOperationCapture.ObservedObtainOrigin(player, screen)
                ?? throw new InvalidOperationException("OPERATION_CAPTURE fresh reward callback source missing");
            var observedParent = origin.Parent
                ?? throw new InvalidOperationException("OPERATION_CAPTURE fresh reward callback parent missing");
            Check(child is { IsCompleted: false } &&
                origin.Relic == reward.Relic && observedParent.Relic == _fixtureParent && origin.ParentReward == reward &&
                origin.ChildOrdinal == 1 && observedParent.ChildrenStarted == 1,
                "fresh UI reward callback rebinds exact suspended obtain parent and nested-call ordinal");
        }
        finally
        {
            PredictorOperationCapture.Read<TaskCompletionSource<IEnumerable<CardModel>>>(screen, "_completionSource").TrySetCanceled();
            _fixtureParentWait?.TrySetCanceled();
            harmony.UnpatchAll(harmony.Id);
            _suspendedFixtureScreen = null;
            screen.GetParent()?.RemoveChild(screen); screen.Free();
            _fixtureParent = null; _fixtureParentRewards = null; _fixtureParentSync = null; _fixtureParentWait = null;
            if (parent?.IsFaulted == true) _ = parent.Exception;
            if (child?.IsFaulted == true) _ = child.Exception;
        }
    }

    private sealed class SuspendedNativeSelector(NDeckCardSelectScreen screen) : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            typeof(NCardGridSelectionScreen).GetField("_cards", members)!.SetValue(screen, options.ToArray());
            typeof(NDeckCardSelectScreen).GetField("_prefs", members)!.SetValue(screen,
                new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, minSelect, maxSelect));
            return screen.CardsSelected();
        }
        public MegaCrit.Sts2.Core.TestSupport.CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives) => throw new NotSupportedException("Not a reward-selector fixture");
    }
}
