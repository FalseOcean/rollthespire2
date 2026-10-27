using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private ImmutableArray<long> _traderOwned = [];
    private ImmutableArray<ModelKey> _traderOffers = [];
    private ImmutableArray<PredictorFakeMerchantStock> _fakeStock = [];
    private bool _fakeInventoryOpen;
    private ModelKey? _wongoFeatured;
    private int _wongoPointsEarned;
    private bool _repySecondPending;

    private PredictorInputResult SubmitEventInventoryOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("TEA_MASTER", "BONE_TEA"):
            case ("TEA_MASTER", "EMBER_TEA"):
            case ("TEA_MASTER", "TEA_OF_DISCOURTESY"):
                if (choice.OptionKey == "BONE_TEA") SpendEventGold(50);
                if (choice.OptionKey == "EMBER_TEA") SpendEventGold(150);
                long teaSource = choice.OptionKey switch { "BONE_TEA" => 6, "EMBER_TEA" => 7, _ => 8 };
                var tea = new ModelKey("RELIC", choice.OptionKey);
                if (BeginObtainRelic(tea, null, PredictorObtainReturn.Event, teaSource, token))
                    ResumeEventAfterRelicObtain(teaSource);
                return PredictorInputResult.Accepted;
            case ("SUNKEN_STATUE", "GRAB_SWORD"):
                if (BeginObtainRelic(new("RELIC", "SWORD_OF_STONE"), null, PredictorObtainReturn.Event, 9, token))
                    ResumeEventAfterRelicObtain(9);
                return PredictorInputResult.Accepted;
            case ("SUNKEN_STATUE", "DIVE_INTO_WATER"):
                Working = PredictorSettlementEffects.TakeGold(Working, _eventGoldAmount);
                Accept(new("SunkenStatueGold", 0, [], null, Working.GoldBudget));
                ApplyEventHealth(-7, 0);
                if (Phase != PredictorPhase.Complete) FinishEvent();
                return PredictorInputResult.Accepted;
            case ("ROUND_TEA_PARTY", "PICK_FIGHT"):
                EventOptions = [new("CONTINUE_FIGHT", HealthDamage: 11)];
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("ROUND_TEA_PARTY", "CONTINUE_FIGHT"):
                ApplyEventHealth(-11, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                BeginEventFrontRelic(10, token);
                return PredictorInputResult.Accepted;
            case ("ROUND_TEA_PARTY", "ENJOY_TEA"):
                if (BeginObtainRelic(new("RELIC", "ROYAL_POISON"), null, PredictorObtainReturn.Event, 11, token))
                    ResumeEventAfterRelicObtain(11);
                return PredictorInputResult.Accepted;
            case ("DROWNING_BEACON", "BOTTLE"):
                OfferEventRewardPlans([new(PredictorRewardKind.Potion,
                    PrefilledPotionKey: new ModelKey("POTION", "GLOWWATER_POTION"))], token);
                return PredictorInputResult.Accepted;
            case ("DROWNING_BEACON", "CLIMB"):
                ApplyEventHealth(0, -13);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                if (BeginObtainRelic(new("RELIC", "FRESNEL_LENS"), null, PredictorObtainReturn.Event, 12, token))
                    ResumeEventAfterRelicObtain(12);
                return PredictorInputResult.Accepted;
            case ("LOST_WISP", "CLAIM"):
                AddTrialCurse("DECAY");
                if (BeginObtainRelic(new("RELIC", "LOST_WISP"), null, PredictorObtainReturn.Event, 14, token))
                    ResumeEventAfterRelicObtain(14);
                return PredictorInputResult.Accepted;
            case ("LOST_WISP", "SEARCH"):
                Working = PredictorSettlementEffects.TakeGold(Working, _eventGoldAmount);
                Accept(new("LostWispGold", 0, [], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("BUGSLAYER", "EXTERMINATION"):
            case ("BUGSLAYER", "SQUASH"):
                var bugCardKey = choice.OptionKey == "EXTERMINATION" ? "EXTERMINATE" : "SQUASH";
                var bugCard = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key.Entry == bugCardKey).Prototype;
                PredictorSettlementEffects.RequireImplementedCard(bugCard, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, bugCard);
                Accept(new("BugslayerCard", Working.Deck[^1], [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("RELIC_TRADER", "TOP"):
            case ("RELIC_TRADER", "MIDDLE"):
            case ("RELIC_TRADER", "BOTTOM"):
                int tradeIndex = choice.OptionKey == "TOP" ? 0 : choice.OptionKey == "MIDDLE" ? 1 : 2;
                if (Context.Operation is not null) RemoveRelicInstance(_traderOwned[tradeIndex]);
                else RemoveRelic(_traderOwned[tradeIndex]);
                if (BeginObtainRelic(_traderOffers[tradeIndex], null, PredictorObtainReturn.Event, 20, token))
                    ResumeEventAfterRelicObtain(20);
                return PredictorInputResult.Accepted;
            case ("RELIC_TRADER", "DONE"):
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("FAKE_MERCHANT", "OPEN_INVENTORY"):
                _fakeInventoryOpen = true;
                EventOptions = FakeMerchantOptions();
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("FAKE_MERCHANT", "CLOSE_INVENTORY"):
                _fakeInventoryOpen = false;
                EventOptions = FakeMerchantOptions();
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("FAKE_MERCHANT", "LEAVE"):
                foreach (var remaining in _fakeStock.Where(s => s.Stocked))
                    Accept(new("FakeMerchantUnbought:" + remaining.Key.Entry, remaining.Slot, [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("FAKE_MERCHANT", "THROW_FOUL_POTION"):
                long foulId = EventPotionToken(option);
                int foulSlot = EventPotionSlots.FindIndex(p => p?.Id == foulId && p.Key.Entry == "FOUL_POTION");
                return foulSlot < 0 ? PredictorInputResult.Invalid :
                    ApplyFoulPotionInEvent(foulSlot, foulId, token);
            case ("FAKE_MERCHANT", _) when choice.OptionKey.StartsWith("BUY_", StringComparison.Ordinal):
                int slot = int.Parse(choice.OptionKey.AsSpan(4));
                var stock = _fakeStock.Single(s => s.Slot == slot);
                if (!stock.Stocked || stock.Price > Working.GoldBudget) return PredictorInputResult.Invalid;
                SpendEventGold(stock.Price);
                _fakeStock = _fakeStock.SetItem(slot, stock with { Stocked = false });
                Accept(new("FakeMerchantPurchase", slot, [], null, Working.GoldBudget));
                if (BeginObtainRelic(stock.Key, null, PredictorObtainReturn.Event, 22, token))
                    ResumeEventAfterRelicObtain(22);
                return PredictorInputResult.Accepted;
            case ("HUNGRY_FOR_MUSHROOMS", "BIG_MUSHROOM"):
            case ("HUNGRY_FOR_MUSHROOMS", "FRAGRANT_MUSHROOM"):
                long mushroomSource = choice.OptionKey == "BIG_MUSHROOM" ? 25 : 26;
                if (BeginObtainRelic(new("RELIC", choice.OptionKey), null, PredictorObtainReturn.Event,
                    mushroomSource, token)) ResumeEventAfterRelicObtain(mushroomSource);
                return PredictorInputResult.Accepted;
            case ("BYRDONIS_NEST", "EAT"):
                ApplyEventHealth(7, 7);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("BYRDONIS_NEST", "TAKE"):
                var eggKey = new ModelKey("CARD", "BYRDONIS_EGG");
                PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, eggKey).Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, eggKey);
                Accept(new("ByrdonisEgg", Working.Deck[^1], [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("DOLL_ROOM", "RANDOM"):
                var randomDoll = new[] { "DAUGHTER_OF_THE_WIND", "MR_STRUGGLES", "BING_BONG" };
                BeginDollObtain(randomDoll[_eventRng!.NextInt(randomDoll.Length)], token);
                return PredictorInputResult.Accepted;
            case ("DOLL_ROOM", "TAKE_SOME_TIME"):
            case ("DOLL_ROOM", "EXAMINE"):
                ApplyEventHealth(choice.OptionKey == "TAKE_SOME_TIME" ? -5 : -15, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                var dollChoices = new List<string> { "BING_BONG", "DAUGHTER_OF_THE_WIND", "MR_STRUGGLES" };
                _eventRng!.UnstableShuffle(dollChoices);
                EventOptions = dollChoices.Take(choice.OptionKey == "TAKE_SOME_TIME" ? 2 : 3)
                    .Select(k => new PredictorEventOption("DOLL_" + k, OfferedRelic: new("RELIC", k)))
                    .ToImmutableArray();
                _requestId++;
                return PredictorInputResult.Accepted;
            case ("DOLL_ROOM", _) when choice.OptionKey.StartsWith("DOLL_", StringComparison.Ordinal):
                BeginDollObtain(choice.OptionKey[5..], token);
                return PredictorInputResult.Accepted;
            case ("WELCOME_TO_WONGOS", "BARGAIN_BIN"):
                SpendEventGold(100);
                _wongoPointsEarned = 32;
                if (BeginObtainRelic(PullWongoRelic("Common"), null, PredictorObtainReturn.Event, 28, token))
                    ResumeEventAfterRelicObtain(28);
                return PredictorInputResult.Accepted;
            case ("WELCOME_TO_WONGOS", "FEATURED_ITEM"):
                SpendEventGold(200);
                _wongoPointsEarned = 16;
                if (BeginObtainRelic(_wongoFeatured!.Value, null, PredictorObtainReturn.Event, 29, token))
                    ResumeEventAfterRelicObtain(29);
                return PredictorInputResult.Accepted;
            case ("WELCOME_TO_WONGOS", "MYSTERY_BOX"):
                SpendEventGold(300);
                _wongoPointsEarned = 8;
                if (BeginObtainRelic(new("RELIC", "WONGOS_MYSTERY_TICKET"), null,
                    PredictorObtainReturn.Event, 30, token)) ResumeEventAfterRelicObtain(30);
                return PredictorInputResult.Accepted;
            case ("WELCOME_TO_WONGOS", "LEAVE"):
                var upgradedWongo = Working.Deck.Where(id => Working.Card(id).UpgradeLevel > 0).ToArray();
                if (upgradedWongo.Length > 0)
                {
                    long id = upgradedWongo[_eventRng!.NextInt(upgradedWongo.Length)];
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        Working.Card(id) with { UpgradeLevel = 0 }));
                    Accept(new("WongoLeaveDowngrade", id, [], null, null));
                }
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CAGE"):
            case ("WAR_HISTORIAN_REPY", "INITIAL_UNLOCK_CHEST"):
                bool repySinglePlayer = Context.Operation?.PlayerCount is not > 1;
                RemoveRepyLanternKeys(firstOnly: repySinglePlayer);
                _repySecondPending = repySinglePlayer && Working.Deck.Any(id => Working.Card(id).Key.Entry == "LANTERN_KEY");
                if (choice.OptionKey == "INITIAL_UNLOCK_CAGE") BeginRepyCage(32, token);
                else BeginRepyChest(token);
                return PredictorInputResult.Accepted;
            case ("WAR_HISTORIAN_REPY", "SECOND_UNLOCK_CAGE"):
            case ("WAR_HISTORIAN_REPY", "SECOND_UNLOCK_CHEST"):
                RemoveRepyLanternKeys(firstOnly: false);
                _repySecondPending = false;
                if (choice.OptionKey == "SECOND_UNLOCK_CAGE") BeginRepyCage(33, token);
                else BeginRepyChest(token);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> TeaMasterOptions() =>
    [
        new("BONE_TEA", Enabled: Working.GoldBudget >= 50, GoldCost: 50),
        new("EMBER_TEA", Enabled: Working.GoldBudget >= 150, GoldCost: 150),
        new("TEA_OF_DISCOURTESY")
    ];

    private ImmutableArray<PredictorEventOption> SunkenStatueOptions()
    {
        _eventGoldAmount = 101 + _eventRng!.NextInt(21);
        return [new("GRAB_SWORD"), new("DIVE_INTO_WATER", GoldGain: _eventGoldAmount, HealthDamage: 7)];
    }

    private ImmutableArray<PredictorEventOption> LostWispOptions()
    {
        _eventGoldAmount = 45 + _eventRng!.NextInt(31);
        return [new("CLAIM"), new("SEARCH", GoldGain: _eventGoldAmount)];
    }

    private ImmutableArray<PredictorEventOption> RelicTraderOptions()
    {
        var owned = Working.Relics.Where(IsRelicTradable).OrderBy(r => r.Key.Entry, StringComparer.Ordinal).ToList();
        _eventRng!.UnstableShuffle(owned);
        _traderOwned = owned.Take(3).Select(r => r.Id).ToImmutableArray();
        if (_traderOwned.IsEmpty) return [new("DONE")];
        var offers = ImmutableArray.CreateBuilder<ModelKey>(3);
        for (int i = 0; i < 3; i++)
        {
            var state = Working;
            offers.Add(PredictorRewardGeneration.PullRelic(Context, ref state));
            Working = state;
            Accept(new("RelicTraderOfferPull", i, [], null, null));
        }
        _traderOffers = offers.ToImmutable();
        var names = new[] { "TOP", "MIDDLE", "BOTTOM" };
        return Enumerable.Range(0, _traderOwned.Length)
            .Select(i => new PredictorEventOption(names[i], RelicId: _traderOwned[i], OfferedRelic: _traderOffers[i]))
            .ToImmutableArray();
    }

    private ImmutableArray<PredictorEventOption> FakeMerchantOptions(bool initializing = false)
    {
        if (initializing)
        {
            // Shuffle a local copy; the versioned source-order catalog is shared.
            var pool = RolltheSpire2.Core.Events.Beta111EventResultCatalog.FakeMerchantRelics.ToList();
            _eventRng!.UnstableShuffle(pool);
            var stock = ImmutableArray.CreateBuilder<PredictorFakeMerchantStock>(6);
            for (int slot = 0; slot < 6; slot++)
            {
                var (price, state) = PriceMerchantRelic(Working, pool[slot]);
                Working = state;
                stock.Add(new(slot, pool[slot], price, true));
            }
            _fakeStock = stock.ToImmutable();
        }
        if (!_fakeInventoryOpen)
        {
            var foul = EventPotionSlots.FirstOrDefault(p => p?.Key.Entry == "FOUL_POTION");
            return [new("OPEN_INVENTORY"), foul == null ? new("THROW_FOUL_POTION", false) :
                EventPotionOption("THROW_FOUL_POTION", foul), new("LEAVE")];
        }
        return _fakeStock.Where(s => s.Stocked)
            .Select(s => new PredictorEventOption("BUY_" + s.Slot,
                Enabled: s.Price <= Working.GoldBudget, GoldCost: s.Price, OfferedRelic: s.Key))
            .Append(new("CLOSE_INVENTORY")).ToImmutableArray();
    }

    internal PredictorInputResult ApplyFoulPotionInEvent(int slot, long potionId, CancellationToken token)
    {
        if (!CanUseFoulPotionInEvent || slot < 0 || slot >= EventPotionSlots.Length ||
            EventPotionSlots[slot] is not { } potion || potion.Id != potionId ||
            potion.Key.Entry != "FOUL_POTION") return PredictorInputResult.Invalid;
        SetEventPotionSlot(slot, null);
        Accept(new("UseFoulPotionAtFakeMerchant", potionId, [], null, null));
        var fakeExtras = ImmutableArray.CreateBuilder<PredictorRewardPlan>();
        fakeExtras.Add(new(PredictorRewardKind.Relic,
            PrefilledRelicKey: new("RELIC", "FAKE_MERCHANTS_RUG")));
        foreach (var item in _fakeStock.Where(s => s.Stocked))
            fakeExtras.Add(new(PredictorRewardKind.Relic, PrefilledRelicKey: item.Key));
        BeginEventCombat(new("ENCOUNTER", "FAKE_MERCHANT_EVENT_ENCOUNTER"),
            fakeExtras.ToImmutable(), false, token);
        return PredictorInputResult.Accepted;
    }

    private void BeginDollObtain(string entry, CancellationToken token)
    {
        if (BeginObtainRelic(new("RELIC", entry), null, PredictorObtainReturn.Event, 27, token))
            ResumeEventAfterRelicObtain(27);
    }

    private void RemoveRepyLanternKeys(bool firstOnly)
    {
        var lanterns = Working.Deck.Where(id => Working.Card(id).Key.Entry == "LANTERN_KEY")
            .Take(firstOnly ? 1 : int.MaxValue).ToImmutableArray();
        foreach (long id in lanterns) Accept(new("RepyCompletedQuest", id, [], null, null));
        RemoveEventCards(lanterns);
        Accept(new("RepyRemovedLanternKeys", 0, lanterns, null, null));
    }

    private void BeginRepyCage(long sourceId, CancellationToken token)
    {
        Accept(new("RepyFreed", 0, [], null, null));
        if (BeginObtainRelic(new("RELIC", "HISTORY_COURSE"), null, PredictorObtainReturn.Event,
            sourceId, token)) ResumeEventAfterRelicObtain(sourceId);
    }

    private void BeginRepyChest(CancellationToken token) => OfferEventRewardPlans(
        [new(PredictorRewardKind.Potion), new(PredictorRewardKind.Potion),
            new(PredictorRewardKind.Relic), new(PredictorRewardKind.Relic)], token);

    private ModelKey PullWongoRelic(string rarity)
    {
        var snapshot = Working;
        var catalog = Context.Catalog!;
        var pull = Working.PersonalBag.Pull(rarity, false,
            key => PredictorRewardGeneration.RelicAllowed(Context, snapshot, key),
            key => catalog.ShopRelics.Any(r => r.Key == key && r.IsAllowedInShops));
        var key = pull.Key ?? new ModelKey("RELIC", "CIRCLET");
        Working = Working with { PersonalBag = pull.Bag, SharedBag = Working.SharedBag.Remove(key) };
        Accept(new("WongoRelicPull:" + rarity, 0, [], null, null));
        return key;
    }

    private ImmutableArray<PredictorEventOption> WongoOptions()
    {
        if (Context.AccountWongoPoints is null) throw new InvalidOperationException("PredictorWongoAccountPointsMissing");
        _wongoFeatured = PullWongoRelic("Rare");
        return [new("BARGAIN_BIN", Working.GoldBudget >= 100, GoldCost: 100),
            new("FEATURED_ITEM", Working.GoldBudget >= 200, GoldCost: 200, OfferedRelic: _wongoFeatured),
            new("MYSTERY_BOX", Working.GoldBudget >= 300, GoldCost: 300), new("LEAVE")];
    }

}
