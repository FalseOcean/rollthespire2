using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunShopCauldronChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, RunState run, Player player, NGlobalUi global, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set)
    {
        string receipt = Path.Combine(output, "shop-cauldron-whetstone-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        string original = signature();
        var originalDeck = player.Deck.Cards.ToArray();
        var originalRelics = player.Relics.ToArray();
        var originalPotionSlots = player.PotionSlots.ToArray();
        var originalDiscoveredCards = player.DiscoveredCards.ToArray();
        var originalDiscoveredPotions = player.DiscoveredPotions.ToArray();
        var originalDiscoveredRelics = player.DiscoveredRelics.ToArray();
        var originalRng = player.PlayerRng.ToSerializable();
        var originalOdds = player.PlayerOdds.ToSerializable();
        var originalRunRng = run.Rng.ToSerializable();
        var originalPersonalBag = player.RelicGrabBag.ToSerializable();
        var originalSharedBag = run.SharedRelicGrabBag.ToSerializable();
        int originalGold = player.Gold;
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var originalHistory = history.Select(act => act.ToArray()).ToArray();
        var originalInventoryUi = global.RelicInventory;
        var relicInventory = new NRelicInventory();
        var fixture = new Harmony("rt2.shop.cauldron.native.visuals");
        MerchantRoom? merchant = null;
        NMerchantInventory? shop = null;
        NRewardsScreen? screen = null;
        RewardsSet? rewards = null;
        Task<bool>? purchase = null;
        bool passed = false;
        string? failure = null;
        object? evidence = null;
        object? bagSetup = null;
        var addedCards = new List<CardModel>();
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            check(ready(), reason);
        }
        try
        {
            check(player.Relics.OfType<TheCourier>().Any() && !player.Relics.OfType<Cauldron>().Any() &&
                !player.Relics.OfType<Whetstone>().Any(), "Cauldron fixture has Courier and unowned Cauldron/Whetstone");
            var potionSlots = PredictorOperationCapture.Read<List<PotionModel?>>(player, "_potionSlots");
            for (int i = 0; i < potionSlots.Count; i++) potionSlots[i] = null;
            foreach (var card in originalDeck) player.Deck.RemoveInternal(card, silent: true);
            foreach (var card in new CardModel[] { ModelDb.Card<StrikeIronclad>().ToMutable(),
                ModelDb.Card<Bash>().ToMutable(), ModelDb.Card<Anger>().ToMutable() })
            { run.AddCard(card, player); player.Deck.AddInternal(card, silent: true); addedCards.Add(card); }
            // The native effect upgrades only temporary fixture cards; cleanup
            // restores original model instances without downgrading them.
            set(global, nameof(NGlobalUi.RelicInventory), relicInventory);
            fixture.Patch(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            fixture.Patch(AccessTools.Method(typeof(NMerchantSlot), "TriggerMerchantHandToPointHere"),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            // The isolated host has no full belt-holder event wiring. Omit
            // only the cosmetic holder lookup/tween; PotionReward.OnSelect,
            // PotionCmd.TryToProcure, AddPotionInternal and all hooks still run.
            fixture.Patch(AccessTools.Method(typeof(NPotionContainer), nameof(NPotionContainer.AnimatePotion)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            var bags = PredictorOperationCapture.Read<Dictionary<RelicRarity, List<RelicModel>>>(player.RelicGrabBag, "_deques");
            var shopBag = bags[RelicRarity.Shop];
            var commonBag = bags[RelicRarity.Common];
            var unlockedCatalog = PredictorOperationCapture.Read<List<RelicModel>>(player.RelicGrabBag, "_originalRelics");
            var cauldron = unlockedCatalog.OfType<Cauldron>().SingleOrDefault();
            var whetstone = unlockedCatalog.OfType<Whetstone>().SingleOrDefault();
            bagSetup = new
            {
                expected = new[] { "CAULDRON", "WHETSTONE" },
                actualShopDeque = shopBag.Select(relic => relic.Id.Entry).ToArray(),
                actualCommonDeque = commonBag.Select(relic => relic.Id.Entry).ToArray(),
                originalUnlockedCatalog = unlockedCatalog.Select(relic => relic.Id.Entry).ToArray(),
                owned = player.Relics.Select(relic => relic.Id.Entry).ToArray(),
                cauldronInCurrentDeque = shopBag.Any(relic => relic.Id.Entry == "CAULDRON"),
                whetstoneInCurrentDeque = commonBag.Any(relic => relic.Id.Entry == "WHETSTONE"),
                cauldronAllowed = cauldron?.IsAllowed(run), whetstoneAllowed = whetstone?.IsAllowed(run),
                source = "Explicit isolated catalog-based bag setup; earlier host shops may already have consumed an unowned offer. This is not proof both relics remained in the current deque."
            };
            File.WriteAllText(Path.Combine(output, "shop-cauldron-bag-setup.json"),
                JsonSerializer.Serialize(bagSetup, new JsonSerializerOptions { WriteIndented = true }));
            check(cauldron != null && whetstone != null && cauldron.IsAllowed(run) && whetstone.IsAllowed(run) &&
                !player.Relics.Any(relic => relic.Id.Entry is "CAULDRON" or "WHETSTONE") &&
                cauldron.IsAllowedInShops && whetstone.IsAllowedInShops,
                "Cauldron/Whetstone are unowned, allowed native original unlocked-catalog shop members; actual catalog/deques recorded in shop-cauldron-bag-setup.json");
            if (cauldron == null || whetstone == null)
                throw new InvalidOperationException("CauldronFixtureCatalogMissing:see shop-cauldron-bag-setup.json");
            // This bounded native fixture needs both particular relics. Restore
            // their original unlocked-catalog entries explicitly if an earlier
            // generated merchant consumed the unowned offer; do not assume the
            // host's accumulated current deque still contains them.
            shopBag.RemoveAll(relic => relic.Id.Entry == "CAULDRON"); shopBag.Add(cauldron);
            commonBag.RemoveAll(relic => relic.Id.Entry == "WHETSTONE"); commonBag.Insert(0, whetstone);
            merchant = new MerchantRoom(); run.PushRoom(merchant); set(merchant, "_runState", run);
            run.AppendToMapPointHistory(MapPointType.Shop, RoomType.Shop, null);
            player.Gold = 999;
            var inventory = MerchantInventory.CreateForNormalMerchant(player); merchant.Inventories.Add(inventory);
            var entry = inventory.RelicEntries[2];
            check(entry.Model is Cauldron && inventory.RelicEntries.All(item => item.Model is not Whetstone),
                "normal native inventory supplies Cauldron without blacklisting Whetstone in another slot");
            commonBag.Remove(whetstone); commonBag.Add(whetstone);
            shop = new NMerchantInventory(); _neowStaticSceneNodes.Add(shop);
            set(shop, nameof(NMerchantInventory.Inventory), inventory); set(shop, nameof(NMerchantInventory.IsOpen), true);
            game.AddChild(shop); game.MoveChild(shop, 0); shop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var slot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
            shop.AddChild(slot); slot.Position = new(1390, 690); slot.Initialize(shop); slot.FillSlot(entry);
            await Frames(4);
            long entryId = PredictorLiveRunCapture.Id(entry);
            PredictorOperationSnapshot Capture() => PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            // Bounded controlled RNG-position fixture, not a natural-seed search.
            // The accepted position is then exercised by the actual native buy.
            PredictorOperationSnapshot snapshot = Capture();
            OperationPreview preview = SurfaceOperationPreview.BuildShop(snapshot, entryId);
            int positions = 0;
            bool HasWhetstone(OperationPreview value) => value.Branches.SelectMany(branch => branch.Sections)
                .Any(section => section.Source.Entry == "THE_COURIER" && section.Rows.Any(row => row.Models.Any(key => key.Entry == "WHETSTONE")));
            while (!HasWhetstone(preview) && ++positions < 64)
            {
                player.PlayerRng.Rewards.NextFloat(); snapshot = Capture();
                preview = SurfaceOperationPreview.BuildShop(snapshot, entryId);
            }
            check(positions < 64 && preview.Faults.IsEmpty && HasWhetstone(preview), "bounded controlled Rewards position gives actual Whetstone replacement");
            // Only the first purchase is affordable. The displayed Whetstone
            // effect means 'if obtained', not a promise of an affordable buy.
            player.Gold = entry.Cost; snapshot = Capture();
            string beforeForecast = signature();
            preview = SurfaceOperationPreview.BuildShop(snapshot, entryId);
            var scene = (PredictorOperationShopScene)snapshot.Scene;
            ImmutableArray<PredictorInput> inputs = [new BuyShopEntry(entryId, scene.Revision)];
            var initial = PredictorOperation.Preview(snapshot, inputs);
            var settled = PredictorOperation.Preview(snapshot, inputs.Add(new LeaveRewards()));
            var replacement = settled.ShopOffers.Single(offer => offer.EntryId == entryId);
            var section = preview.Branches.SelectMany(branch => branch.Sections).Single(item => item.Source.Entry == "WHETSTONE");
            var upgrades = section.Rows.Single().Cards;
            check(preview.Source.Entry == "CAULDRON" && preview.Faults.IsEmpty && preview.PendingReasons.IsEmpty &&
                initial.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Potion) == 5 &&
                settled.Waiting.Phase == PredictorPhase.Shop && replacement.Key?.Entry == "WHETSTONE" && !replacement.Affordable &&
                preview.Branches.SelectMany(branch => branch.Sections).Where(item => item.Source.Entry == "CAULDRON")
                    .SelectMany(item => item.Rows).Sum(row => row.Models.Length) == 5 && upgrades.Length == 2 &&
                section.EmptyReason == null && section.Label == null && section.Rows.All(row => !row.Possible),
                "Cauldron preserves five generated potions and previews two exact post-settlement Whetstone upgrades even with no gold for a second buy");
            var potion = initial.Rewards.First(reward => reward.Kind == PredictorRewardKind.Potion);
            var taking = PredictorOperation.Preview(snapshot, inputs.Add(new TakeReward(potion.Id)).Add(new LeaveRewards()));
            check(taking.Disposition == PredictorInputResult.Accepted && taking.ShopOffers.Single(offer => offer.EntryId == entryId) == replacement &&
                taking.State.Streams.SequenceEqual(settled.State.Streams) &&
                JsonSerializer.Serialize(taking.State.PersonalBag) == JsonSerializer.Serialize(settled.State.PersonalBag),
                "taking a generated Cauldron potion and skipping the rest preserves replacement and RNG");
            var noAttack = snapshot with { State = snapshot.State with { Cards = snapshot.State.Cards.Select(card =>
                snapshot.State.Deck.Contains(card.Id) ? card with { UpgradeLevel = card.MaxUpgradeLevel } : card).ToImmutableArray() } };
            var empty = SurfaceOperationPreview.BuildShop(noAttack, entryId);
            var emptySection = empty.Branches.SelectMany(branch => branch.Sections).Single(item => item.Source.Entry == "WHETSTONE");
            check(empty.Faults.IsEmpty && emptySection.Rows.IsEmpty && emptySection.EmptyReason == "WhetstoneNoUpgradeTargets",
                "fully upgraded detached deck explicitly displays no Whetstone upgrade targets");
            check(signature() == beforeForecast, "all purchase and Whetstone projections preserve the actual run/RNG");
            CheckShopWhetstoneChoicePremises(snapshot, entryId, check);
            var mouse = overlays.MouseFilter;
            try
            {
                overlays.MouseFilter = Control.MouseFilterEnum.Ignore;
                await exercise("shop-cauldron-whetstone", slot, slot.Hitbox, "CAULDRON",
                    value => value is PredictorOperationShopScene shopScene && shopScene.InventoryOpen,
                    value => value.Branches.SelectMany(branch => branch.Sections).Any(item => item.Source.Entry == "WHETSTONE" &&
                        item.Rows.Single().Cards.SequenceEqual(upgrades)),
                    (view, _) =>
                    {
                        var effect = view.GetNode<Control>("Rt2NeowSummary/Rt2OperationEffectPanel_WHETSTONE");
                        check(effect.IsVisibleInTree() && Descendants(effect).OfType<Label>().Any(label => label.Text.StartsWith("升级", StringComparison.Ordinal)),
                            "native Cauldron hover renders Whetstone's separate upgrade-effect panel");
                    }, false);
            }
            finally { overlays.MouseFilter = mouse; }
            purchase = entry.OnTryPurchaseWrapper(inventory);
            await Until(() => overlays.Peek() is NRewardsScreen || purchase.IsCompleted, "original Cauldron purchase opens its potion rewards");
            if (purchase.IsFaulted) throw new InvalidOperationException("Original Cauldron purchase failed", purchase.Exception);
            check(!purchase.IsCompleted && overlays.Peek() is NRewardsScreen, "actual Cauldron purchase suspends before restock");
            screen = (NRewardsScreen)overlays.Peek()!;
            rewards = PredictorOperationCapture.Read<RewardsSet>(screen, "_rewardsSet");
            var actualPotions = rewards.Rewards.OfType<PotionReward>().ToArray();
            check(actualPotions.Length == 5 && actualPotions.Select(reward => reward.Potion!.Id.Entry)
                .SequenceEqual(initial.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Potion).Select(reward => reward.Key.Entry)),
                "original Cauldron generates the five previewed potions in native order");
            if (player.HasOpenPotionSlots)
                check(await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(actualPotions[0]) &&
                    actualPotions[0].SuccessfullySelected && actualPotions[0].ClaimedPotion == actualPotions[0].Potion &&
                    player.Potions.Contains(actualPotions[0].Potion!),
                    "original Cauldron potion selection runs procurement/hooks and claims the same native potion before final restock");
            RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            if (GodotObject.IsInstanceValid(screen) && screen.GetParent() != null) overlays.Remove(screen);
            await Until(() => purchase.IsCompleted, "original Cauldron rewards completion resumes native restock");
            check(await purchase && entry.Model is Whetstone && entry.Cost == replacement.Cost && player.Gold == 0,
                "actual original Cauldron restock identity/price match the advance forecast");
            var beforeUpgrade = addedCards.ToDictionary(card => PredictorLiveRunCapture.Id(card), card => card.CurrentUpgradeLevel);
            // Exercise the requested hypothetical obtain directly, without a
            // second paid purchase or a second merchant replacement forecast.
            await RelicCmd.Obtain(entry.Model!, player);
            var actualUpgrades = addedCards.Where(card => card.CurrentUpgradeLevel != beforeUpgrade[PredictorLiveRunCapture.Id(card)])
                .Select(PredictorLiveRunCapture.Id).Order().ToArray();
            check(actualUpgrades.SequenceEqual(upgrades.Select(card => card.Id).Order()),
                "original Whetstone AfterObtained upgrades exactly the targets forecast from the settled purchase");
            var actualNiche = run.Rng.Niche.ToSerializable();
            check(settled.State.Streams.Single(stream => stream.Stream == PredictorStream.Niche).Calls + addedCards.Count - 1 == actualNiche.counter,
                "native Whetstone consumes the expected stable-shuffle Niche continuation");
            evidence = new { controlledRewardsAdvances = positions, expectedRestock = replacement.Key?.Entry, actualRestock = "WHETSTONE",
                replacement.Cost, potionKeys = actualPotions.Select(reward => reward.Potion!.Id.Entry),
                predictedTargets = upgrades.Select(card => new { card.Id, card.Key.Entry }), actualUpgrades,
                secondPurchaseAffordable = false, actualObtain = "RelicCmd.Obtain(restockedWhetstone,player)" };
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (rewards != null && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards))
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            if (screen != null && GodotObject.IsInstanceValid(screen) && screen.GetParent() != null) overlays.Remove(screen);
            if (purchase is { IsCompleted: false }) await Until(() => purchase.IsCompleted, "Cauldron cleanup unwinds native purchase");
            if (purchase?.IsFaulted == true) _ = purchase.Exception;
            shop?.Free(); fixture.UnpatchAll(fixture.Id);
            set(global, nameof(NGlobalUi.RelicInventory), originalInventoryUi); relicInventory.Free();
            foreach (var relic in player.Relics.Where(item => !originalRelics.Contains(item)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
            foreach (var card in addedCards) { player.Deck.RemoveInternal(card, silent: true); run.RemoveCard(card); }
            foreach (var card in originalDeck) player.Deck.AddInternal(card, silent: true);
            var slots = PredictorOperationCapture.Read<List<PotionModel?>>(player, "_potionSlots");
            slots.Clear(); slots.AddRange(originalPotionSlots);
            player.Gold = originalGold;
            set(player, nameof(Player.DiscoveredCards), originalDiscoveredCards.ToList());
            set(player, nameof(Player.DiscoveredPotions), originalDiscoveredPotions.ToList());
            set(player, nameof(Player.DiscoveredRelics), originalDiscoveredRelics.ToList());
            player.PlayerRng.LoadFromSerializable(originalRng); player.PlayerOdds.LoadFromSerializable(originalOdds);
            run.Rng.LoadFromSerializable(originalRunRng); player.RelicGrabBag.LoadFromSerializable(originalPersonalBag);
            run.SharedRelicGrabBag.LoadFromSerializable(originalSharedBag);
            if (merchant != null && ReferenceEquals(run.CurrentRoom, merchant)) run.PopCurrentRoom();
            history.Clear(); foreach (var act in originalHistory) history.Add(act.ToList());
            bool restored = signature() == original;
            if (!restored)
            {
                File.WriteAllText(Path.Combine(output, "shop-cauldron-restore-before.json"), original);
                File.WriteAllText(Path.Combine(output, "shop-cauldron-restore-after.json"), signature());
            }
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, bagSetup, evidence, restored, failure,
                source = "Controlled unowned/allowed Cauldron/Whetstone setup from the native original unlocked catalog (including explicit restoration of earlier consumed unowned offers), then bounded native Rewards-RNG fixture positioning; original normal merchant generation, actual native hover, original Cauldron purchase/five potion rewards/set completion/restock, then original Whetstone obtain on settled state. Only absent relic/merchant animations and potion-belt acquisition animation patched; actual reward selection, procurement, inventory mutation and hooks remain original. No current-deque availability, natural-seed or affordable-second-purchase claim." },
                new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "Cauldron/Whetstone fixture restores actual deck, inventory, gold, RNG, odds, bags and history");
        }
    }

    private static void CheckShopWhetstoneChoicePremises(PredictorOperationSnapshot captured, long entryId,
        Action<bool, string> check)
    {
        // These are explicitly detached context variants. Only the Cauldron
        // case above runs a native purchase; do not report these as actual
        // native Mirror/Orrery executions or a natural-seed reachability claim.
        foreach (string source in new[] { "DOLLYS_MIRROR", "ORRERY" })
        {
            var scene = (PredictorOperationShopScene)captured.Scene;
            var snapshot = captured with { Scene = scene with { Entries = scene.Entries.Select(entry =>
                entry.Id == entryId ? entry with { Key = new("RELIC", source) } : entry).ToImmutableArray() } };
            var buy = new BuyShopEntry(entryId, scene.Revision);
            var first = PredictorOperation.Preview(snapshot, buy);
            PredictorOperationResult Finish(PredictorOperationSnapshot value, PredictorOperationResult initial) => source == "DOLLYS_MIRROR"
                ? PredictorOperation.Preview(value, [buy, new ChooseObtainDeckCards([initial.Waiting.Selection.CandidateCardIds.First()])])
                : PredictorOperation.Preview(value, [buy, new LeaveRewards()]);
            var settled = Finish(snapshot, first);
            int positions = 0;
            while (settled.ShopOffers.Single(offer => offer.EntryId == entryId).Key?.Entry != "WHETSTONE" && ++positions < 64)
            {
                var rng = snapshot.State.Streams.Single(stream => stream.Stream == PredictorStream.Rewards).Restore();
                rng.NextFloat(); snapshot = snapshot with { State = snapshot.State.WithRng(PredictorStream.Rewards, rng) };
                first = PredictorOperation.Preview(snapshot, buy); settled = Finish(snapshot, first);
            }
            check(positions < 64, "bounded detached " + source + " context supplies Whetstone restock");
            var preview = SurfaceOperationPreview.BuildShop(snapshot, entryId);
            var effect = preview.Branches.SelectMany(branch => branch.Sections).Single(section => section.Source.Entry == "WHETSTONE");
            if (source == "ORRERY")
            {
                var expected = PredictorSettlementEffects.ObtainRelic(settled.State, new("RELIC", "WHETSTONE"));
                var changes = expected.Deck.Select(expected.Card).Where(card => card.UpgradeLevel != settled.State.Card(card.Id).UpgradeLevel).ToArray();
                check(preview.Faults.IsEmpty && effect.Label == "WhetstoneAfterSkippingCardRewards" &&
                    effect.Rows.Single().Cards.SequenceEqual(changes) && effect.Rows.Single().Possible == false &&
                    settled.State.Deck.SequenceEqual(snapshot.State.Deck),
                    "Orrery Whetstone effect explicitly assumes all remaining cards skipped and uses that final deck");
            }
            else
            {
                var paths = first.Waiting.Selection.CandidateCardIds.Select(target =>
                {
                    var afterCopy = PredictorOperation.Preview(snapshot, [buy, new ChooseObtainDeckCards([target])]);
                    var afterUpgrade = PredictorSettlementEffects.ObtainRelic(afterCopy.State, new("RELIC", "WHETSTONE"));
                    return afterUpgrade.Deck.Select(afterUpgrade.Card)
                        .Where(card => card.UpgradeLevel != afterCopy.State.Card(card.Id).UpgradeLevel).ToImmutableArray();
                }).ToArray();
                bool varying = paths.Skip(1).Any(cards => !cards.SequenceEqual(paths[0]));
                check(preview.Source.Entry == "THE_COURIER" && preview.Faults.IsEmpty &&
                    effect.Rows.Single().Cards.SequenceEqual(paths.SelectMany(cards => cards).Distinct()) &&
                    effect.Rows.Single().Possible == varying &&
                    effect.Label == (varying ? "WhetstoneAfterMirrorChoice" : null),
                    "Mirror Whetstone targets cover every legal copy's final deck and mark differing targets possible");
            }
        }
    }
}
