using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunShopMirrorChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, RunState run, Player player, MerchantInventory inventory,
        NMerchantInventory shop, Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        NGlobalUi global, Action<object, string, object?> set)
    {
        string receipt = Path.Combine(output, "shop-mirror-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        string original = signature();
        var originalDeck = player.Deck.Cards.ToArray();
        var originalRelics = player.Relics.ToArray();
        int originalGold = player.Gold;
        var originalRng = player.PlayerRng.ToSerializable();
        var originalRunRng = run.Rng.ToSerializable();
        var originalPersonalBag = player.RelicGrabBag.ToSerializable();
        var originalSharedBag = run.SharedRelicGrabBag.ToSerializable();
        var originalInventoryUi = global.RelicInventory;
        NRelicInventory? relicInventory = null;
        var relicEntries = PredictorOperationCapture.Read<List<MerchantRelicEntry>>(inventory, "_relicEntries");
        var originalShopEntry = relicEntries[2];
        var courier = player.Relics.OfType<TheCourier>().Single();
        int courierIndex = player.Relics.ToList().IndexOf(courier);
        NMerchantRelic? slot = null;
        var variants = new List<object>();
        object? actual = null;
        bool passed = false;
        string? failure = null;
        var visualFixture = new Harmony("rt2.shop.mirror.native.visuals");
        try
        {
            check(!player.Relics.OfType<DollysMirror>().Any() && player.RunState.CurrentMapPointHistoryEntry == null,
                "Mirror fixture has an unowned offer and no map history to pollute with its controlled purchase");
            // Normal merchants have a Shop-rarity third relic slot. Reorder only
            // a legitimately available Shop bag member, then let the original
            // rarity constructor pull it through its real eligibility filter.
            // This controls a legal bag permutation, not a natural seed claim.
            MerchantRelicEntry mirror;
            if (originalShopEntry.Model is DollysMirror)
                mirror = new MerchantRelicEntry(ModelDb.Relic<DollysMirror>().ToMutable(), player);
            else
            {
                var bags = PredictorOperationCapture.Read<Dictionary<RelicRarity, List<RelicModel>>>(player.RelicGrabBag, "_deques");
                var shopBag = bags[RelicRarity.Shop];
                var canonicalMirror = shopBag.OfType<DollysMirror>().Single();
                check(canonicalMirror.IsAllowedInShops && canonicalMirror.IsAllowed(run),
                    "Mirror is an available legal Shop-rarity relic in the actual player's bag");
                shopBag.Remove(canonicalMirror); shopBag.Add(canonicalMirror);
                mirror = new MerchantRelicEntry(RelicRarity.Shop, player);
                check(mirror.Model is DollysMirror, "original Shop-rarity constructor pulls Mirror from its legal bag");
            }
            relicEntries[2] = mirror;
            slot = PreloadManager.Cache.GetScene("res://scenes/merchant/merchant_relic.tscn").Instantiate<NMerchantRelic>();
            shop.AddChild(slot); slot.Position = new(1390, 690); slot.Initialize(shop); slot.FillSlot(mirror);
            for (int frame = 0; frame < 4; frame++)
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            CardModel AddTarget(CardModel card)
            { run.AddCard(card, player); player.Deck.AddInternal(card, silent: true); return card; }
            CardModel[] targets = [AddTarget(ModelDb.Card<DefendIronclad>().ToMutable()),
                AddTarget(ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
                    .First(card => card.Type != CardType.Quest).ToMutable()), AddTarget(ModelDb.Card<Injury>().ToMutable())];
            long entryId = PredictorLiveRunCapture.Id(mirror);
            PredictorOperationSnapshot Capture() => PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
            var snapshot = Capture();
            var scene = (PredictorOperationShopScene)snapshot.Scene;
            var buy = new BuyShopEntry(entryId, scene.Revision);
            var waiting = PredictorOperation.Preview(snapshot, buy);
            check(waiting.Disposition == PredictorInputResult.Accepted && waiting.Waiting.Phase == PredictorPhase.ObtainDeckCards,
                "real merchant capture suspends Mirror at its unresolved copy selection");
            PredictorShopOffer? expected = null;
            string? expectedDependencies = null;
            foreach (var target in targets)
            {
                long cardId = PredictorLiveRunCapture.Id(target);
                check(waiting.Waiting.Selection.CandidateCardIds.Contains(cardId), "ordinary/colorless/curse targets are legal native Mirror candidates");
                var result = PredictorOperation.Preview(snapshot, [buy, new ChooseObtainDeckCards([cardId])]);
                var replacement = result.ShopOffers.Single(offer => offer.EntryId == entryId);
                string dependencies = JsonSerializer.Serialize(new { replacement, result.State.PersonalBag, result.State.SharedBag,
                    streams = result.State.Streams.Where(stream => stream.Stream is PredictorStream.Rewards or PredictorStream.Shops) });
                check(result.Disposition == PredictorInputResult.Accepted && result.Waiting.Phase == PredictorPhase.Shop && replacement.Stocked,
                    "each legal Mirror copy resumes the real predictor merchant transaction");
                expected ??= replacement; expectedDependencies ??= dependencies;
                check(dependencies == expectedDependencies, "ordinary/colorless/curse copies have identical restock, price, Rewards/Shops RNG and bags");
                variants.Add(new { target = target.Id.Entry, type = target.Type.ToString(), replacement = replacement.Key?.Entry,
                    replacement.Cost, dependenciesSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dependencies))) });
            }
            var expectedOffer = expected!;
            bool IsRestockOnly(OperationPreview preview) => preview.PendingReasons.IsEmpty && preview.Notices.IsEmpty &&
                preview.Branches.SelectMany(branch => branch.PendingReasons).Any() == false &&
                preview.Branches.SelectMany(branch => branch.Sections).Count(section => section.Source.Entry == "THE_COURIER") == 1 &&
                preview.Branches.SelectMany(branch => branch.Sections).All(section => section.Source.Entry == "THE_COURIER" ||
                    expectedOffer.Key?.Entry == "WHETSTONE" && section.Source.Entry == "WHETSTONE") &&
                preview.Branches.SelectMany(branch => branch.Sections).Where(section => section.Source.Entry == "THE_COURIER").All(section =>
                    section.CardGroups.IsEmpty && section.Rows.Length == 1 && section.Rows[0].Cards.IsEmpty &&
                    section.Rows[0].Action == OperationPreviewAction.Restock &&
                    section.Rows[0].Models.SequenceEqual([expectedOffer.Key!.Value]) && section.Rows[0].Price == expectedOffer.Cost);
            await exercise("shop-mirror-courier", slot, slot.Hitbox, "THE_COURIER",
                captured => captured is PredictorOperationShopScene s && s.Entries.Any(entry => entry.Id == entryId && entry.Key?.Entry == "DOLLYS_MIRROR"),
                IsRestockOnly, (view, _) =>
                {
                    var labels = Descendants(view).OfType<Label>().Select(label => label.Text).ToArray();
                    check((expectedOffer.Key?.Entry == "WHETSTONE" || !Descendants(view).OfType<MegaCrit.Sts2.Core.Nodes.Cards.NCard>().Any()) &&
                        labels.Any(text => text == expectedOffer.Cost.ToString()) &&
                        labels.Any(text => text == "补货") && !labels.Any(text => text == "获得") &&
                        !labels.Any(text => text.Contains("尚未确定", StringComparison.Ordinal)),
                        "Mirror native hover names the priced Courier row as restock without copy cards or generic awaiting-choice text");
                }, false);
            // The non-Courier projection keeps its ordinary copy preview; the
            // conditional shortcut must neither promise restock nor consume RNG.
            // Removing/re-adding a native relic permanently sets its
            // HasBeenRemovedFromState hook flag. Use a detached absence variant
            // so this negative case cannot disable Courier in later native work.
            var withoutCourier = snapshot with { State = snapshot.State with {
                Relics = snapshot.State.Relics.Where(relic => relic.Key.Entry != "THE_COURIER").ToImmutableArray() } };
            string beforeWithoutCourier = signature();
            var ordinary = SurfaceOperationPreview.BuildShop(withoutCourier, entryId);
            var normalRelic = RelicOperationPreview.Build(withoutCourier,
                [new BuyShopEntry(entryId, ((PredictorOperationShopScene)withoutCourier.Scene).Revision)], new("RELIC", "DOLLYS_MIRROR"));
            check(JsonSerializer.Serialize(ordinary) == JsonSerializer.Serialize(normalRelic) && signature() == beforeWithoutCourier &&
                ordinary.Branches.SelectMany(branch => branch.Sections).All(section => section.Source.Entry != "THE_COURIER"),
                "without Courier Mirror preserves its existing relic preview and does not invent restock");
            player.Gold = mirror.Cost - 1;
            var unaffordableSnapshot = Capture();
            var unaffordableScene = (PredictorOperationShopScene)unaffordableSnapshot.Scene;
            var unaffordableResult = PredictorOperation.Preview(unaffordableSnapshot,
                new BuyShopEntry(entryId, unaffordableScene.Revision));
            var currentQuote = unaffordableResult.ShopOffers.Single(offer => offer.EntryId == entryId);
            check(!courier.HasBeenRemovedFromState && !mirror.EnoughGold && !currentQuote.Affordable && currentQuote.Cost == mirror.Cost,
                "Mirror insufficient-gold fixture uses the same active-Courier native/model quote;gold=" + player.Gold +
                ";nativeCost=" + mirror.Cost + ";modelCost=" + currentQuote.Cost + ";disposition=" + unaffordableResult.Disposition);
            await exercise("shop-mirror-insufficient-gold", slot, slot.Hitbox, "DOLLYS_MIRROR",
                captured => captured is PredictorOperationShopScene,
                preview => preview.Notices.SequenceEqual(["Purchase:InsufficientGold"]) && preview.Branches.IsEmpty,
                (view, _) => check(Descendants(view).OfType<Label>().Any(label => label.Text == "金币不足"),
                    "unaffordable Mirror retains the native-hover insufficient-gold explanation"), false);
            player.Gold = originalGold;
            // Only visual animation is skipped here. Payment, the original
            // AfterObtained/clone/deck-add hooks and original restock all run.
            relicInventory = new NRelicInventory();
            set(global, nameof(NGlobalUi.RelicInventory), relicInventory);
            visualFixture.Patch(AccessTools.Method(typeof(NRelicInventory), nameof(NRelicInventory.AnimateRelic)),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            // The mounted slot omits the full merchant's Spine hand. Retain
            // its successful-purchase visual refresh, omitting only hand motion.
            visualFixture.Patch(AccessTools.Method(typeof(NMerchantSlot), "TriggerMerchantHandToPointHere"),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            // Keep TestMode off: successful native slot refresh creates its new
            // NRelic, which TestMode deliberately suppresses. Skip only card
            // motion/preview; the original deck-add hooks still execute.
            visualFixture.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.GetTweenForCardsChangingPiles),
                    [typeof(IEnumerable<CardPileAddResult>), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            foreach (var previewMethod in typeof(CardCmd).GetMethods().Where(method => method.Name == nameof(CardCmd.PreviewCardPileAdd)))
                visualFixture.Patch(previewMethod,
                    prefix: new HarmonyMethod(typeof(UiSurfaceChecks), nameof(InRunMapSkipUnrelatedUiSetup)));
            int beforeDeck = player.Deck.Cards.Count;
            int beforeGold = player.Gold;
            int cost = mirror.Cost;
            var purchasedMirror = mirror.Model!;
            var selector = new MirrorNativeSelector(targets[2]);
            using (CardSelectCmd.UseSelector(selector))
            {
                var purchase = mirror.OnTryPurchaseWrapper(inventory);
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (!purchase.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5))
                    await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                check(purchase.IsCompleted, "original Mirror purchase completes its selected copy and merchant restock");
                check(await purchase && selector.Calls == 1 && player.Relics.Contains(purchasedMirror) &&
                    player.Gold == beforeGold - cost && player.Deck.Cards.Count == beforeDeck + 1 &&
                    player.Deck.Cards.Count(card => card.Id == targets[2].Id) == originalDeck.Count(card => card.Id == targets[2].Id) + 2,
                    "original merchant wrapper pays once, obtains Mirror and copies the chosen curse once");
            }
            var after = Capture();
            check(mirror.Model?.Id.Entry == expectedOffer.Key?.Entry && mirror.Cost == expectedOffer.Cost,
                "pre-purchase Mirror hover restock identity and price equal the completed original game purchase");
            check(JsonSerializer.Serialize(after.State.Streams.Where(stream => stream.Stream is PredictorStream.Rewards or PredictorStream.Shops)) ==
                JsonSerializer.Serialize(PredictorOperation.Preview(snapshot, [buy,
                    new ChooseObtainDeckCards([PredictorLiveRunCapture.Id(targets[2])])]).State.Streams
                    .Where(stream => stream.Stream is PredictorStream.Rewards or PredictorStream.Shops)),
                "completed native Mirror purchase agrees with detached Rewards/Shops continuation");
            actual = new { selectedCard = targets[2].Id.Entry, replacement = mirror.Model?.Id.Entry, mirror.Cost,
                goldSpent = beforeGold - player.Gold, cardsAdded = player.Deck.Cards.Count - beforeDeck, selector.Calls };
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            visualFixture.UnpatchAll(visualFixture.Id);
            set(global, nameof(NGlobalUi.RelicInventory), originalInventoryUi); relicInventory?.Free();
            slot?.Free(); relicEntries[2] = originalShopEntry;
            foreach (var card in player.Deck.Cards.Where(card => !originalDeck.Contains(card)).ToArray())
            { player.Deck.RemoveInternal(card, silent: true); run.RemoveCard(card); }
            foreach (var relic in player.Relics.Where(relic => !originalRelics.Contains(relic)).ToArray())
                player.RemoveRelicInternal(relic, silent: true);
            if (!player.Relics.Contains(courier)) player.AddRelicInternal(courier, courierIndex, silent: true);
            player.Gold = originalGold;
            player.PlayerRng.LoadFromSerializable(originalRng); run.Rng.LoadFromSerializable(originalRunRng);
            player.RelicGrabBag.LoadFromSerializable(originalPersonalBag); run.SharedRelicGrabBag.LoadFromSerializable(originalSharedBag);
            bool restored = signature() == original;
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed = passed && restored, variants, actual, restored, failure,
                source = "Controlled legal Shop-rarity bag permutation; original MerchantRelicEntry rarity constructor/native slot/native hover and OnTryPurchaseWrapper. Ordinary/colorless/curse targets share one detached baseline; one original purchase chooses the curse. Only card motion/preview, relic pickup animation and absent merchant-hand motion are patched; settlement and native slot refresh remain original. No natural-seed reachability search or Owner runtime acceptance." },
                new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "Mirror fixture restores prior player, deck, relics, gold, RNG, bags and merchant slot");
        }
    }

    private sealed class MirrorNativeSelector(CardModel target) : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        internal int Calls { get; private set; }
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Calls++;
            if (minSelect != 1 || maxSelect != 1 || !options.Contains(target))
                throw new InvalidOperationException("MirrorNativeSelector: original legal one-card choice missing");
            return Task.FromResult<IEnumerable<CardModel>>([target]);
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<CardRewardAlternative> alternatives) => throw new NotSupportedException("Mirror is a deck-copy selector");
    }
}
