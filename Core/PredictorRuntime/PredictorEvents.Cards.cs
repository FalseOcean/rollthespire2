using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private PredictorCardRewardNicheScope? _eventOfferedCardNicheScope;

    private PredictorReward EventCardOffer() => new(_eventOfferedCards[0].Id, PredictorRewardKind.Card,
        PredictorRewardOrigin.Event, _eventKey!.Value, _eventOfferedCards)
        { CanSkip = false, CardRewardNicheScope = _eventOfferedCardNicheScope };
    private PredictorInputResult SubmitEventCardsOption(ChooseEventOption choice,
        PredictorEventOption option, CancellationToken token)
    {
        switch (_eventKey!.Value.Entry, choice.OptionKey)
        {
            case ("THIS_OR_THAT", "PLAIN"):
                ApplyEventHealth(-6, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                Working = PredictorSettlementEffects.TakeGold(Working, _eventGoldAmount);
                Accept(new("ThisOrThatGold", 0, [], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("THIS_OR_THAT", "ORNATE"):
                BeginEventFrontRelic(4, token);
                return PredictorInputResult.Accepted;
            case ("UNREST_SITE", "REST"):
                ApplyEventFullHeal();
                AddTrialCurse("POOR_SLEEP");
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("UNREST_SITE", "KILL"):
                ApplyEventHealth(0, -8);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                BeginEventFrontRelic(5, token);
                return PredictorInputResult.Accepted;
            case ("SAPPHIRE_SEED", "EAT"):
                ApplyEventHealth(9, 0);
                BeginEventCardSelection(Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToImmutableArray(), 1, token);
                return PredictorInputResult.Accepted;
            case ("SAPPHIRE_SEED", "PLANT"):
                BeginEventCardSelection(SownCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("ZEN_WEAVER", "BREATHING_TECHNIQUES"):
                SpendEventGold(50);
                var enlightenment = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key.Entry == "ENLIGHTENMENT").Prototype;
                PredictorSettlementEffects.RequireImplementedCard(enlightenment, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, enlightenment);
                Working = PredictorObtainSources.AddCard(Context, Working, enlightenment);
                Accept(new("ZenWeaverCards", 0, Working.Deck.TakeLast(2).ToImmutableArray(), null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("ZEN_WEAVER", "EMOTIONAL_AWARENESS"):
            case ("ZEN_WEAVER", "ARACHNID_ACUPUNCTURE"):
                _eventGoldAmount = choice.OptionKey == "EMOTIONAL_AWARENESS" ? 125 : 250;
                var removable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (removable.IsEmpty)
                {
                    SpendEventGold(_eventGoldAmount);
                    FinishEvent();
                }
                else BeginEventCardSelection(removable, Math.Min(choice.OptionKey == "EMOTIONAL_AWARENESS" ? 1 : 2, removable.Length), token);
                return PredictorInputResult.Accepted;
            case ("REFLECTIONS", "TOUCH_A_MIRROR"):
                var upgraded = Working.Deck.Where(id => Working.Card(id).UpgradeLevel > 0).ToList();
                for (int i = 0; i < 2 && upgraded.Count > 0; i++)
                {
                    int drawn = _eventRng!.NextInt(upgraded.Count);
                    long id = upgraded[drawn];
                    upgraded.RemoveAt(drawn);
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        Working.Card(id) with { UpgradeLevel = 0 }));
                    Accept(new("ReflectionsDowngrade", id, [], null, null));
                }
                var upgradableNow = Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToList();
                for (int i = 0; i < 4 && upgradableNow.Count > 0; i++)
                {
                    int drawn = _eventRng!.NextInt(upgradableNow.Count);
                    long id = upgradableNow[drawn];
                    upgradableNow.RemoveAt(drawn);
                    var card = Working.Card(id);
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        card with { UpgradeLevel = card.UpgradeLevel + 1 }));
                    Accept(new("ReflectionsUpgrade", id, [], null, null));
                }
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("REFLECTIONS", "SHATTER"):
                var originals = Working.Deck.ToArray();
                foreach (long originalId in originals)
                {
                    var copy = Working.Card(originalId);
                    PredictorSettlementEffects.RequireImplementedCard(copy, context: Context);
                    Working = PredictorObtainSources.CloneCardToDeck(Context, Working, copy);
                    Accept(new("ReflectionsClone", originalId, [Working.Deck[^1]], null, null));
                }
                AddTrialCurse("BAD_LUCK");
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("WELLSPRING", "BOTTLE"):
                if (Context.PotionPool.IsEmpty) { FinishEvent(); return PredictorInputResult.Accepted; }
                var bottle = Working.Draw(PredictorStream.Rewards, 0, Context.PotionPool.Length);
                Working = bottle.State;
                OfferEventRewardPlans([new(PredictorRewardKind.Potion, PrefilledPotionKey: Context.PotionPool[bottle.Value].Key)], token);
                return PredictorInputResult.Accepted;
            case ("WELLSPRING", "BATHE"):
                var bathable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (bathable.IsEmpty)
                {
                    AddTrialCurse("GUILTY");
                    FinishEvent();
                }
                else BeginEventCardSelection(bathable, 1, token);
                return PredictorInputResult.Accepted;
            case ("FIELD_OF_MAN_SIZED_HOLES", "RESIST"):
                var holesRemovable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (holesRemovable.IsEmpty) { AddTrialCurse("NORMALITY"); FinishEvent(); }
                else BeginEventCardSelection(holesRemovable, Math.Min(2, holesRemovable.Length), token);
                return PredictorInputResult.Accepted;
            case ("FIELD_OF_MAN_SIZED_HOLES", "ENTER_YOUR_HOLE"):
                BeginEventCardSelection(SownCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH"):
                var choirRemovable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (choirRemovable.IsEmpty) { AddTrialCurse("SPORE_MIND"); FinishEvent(); }
                else BeginEventCardSelection(choirRemovable, Math.Min(2, choirRemovable.Length), token);
                return PredictorInputResult.Accepted;
            case ("LUMINOUS_CHOIR", "OFFER_TRIBUTE"):
                SpendEventGold(_eventGoldAmount);
                BeginEventFrontRelic(13, token);
                return PredictorInputResult.Accepted;
            case ("AMALGAMATOR", "COMBINE_STRIKES"):
            case ("AMALGAMATOR", "COMBINE_DEFENDS"):
                bool strikes = choice.OptionKey == "COMBINE_STRIKES";
                var basics = Working.Deck.Where(id =>
                {
                    var card = Working.Card(id);
                    var definition = PredictorCardChanges.Definition(Context, card.Key);
                    return card.Removable && definition.Rarity == "Basic" &&
                        (strikes ? definition.IsStrike : definition.IsDefend);
                }).ToImmutableArray();
                if (basics.IsEmpty)
                {
                    AddAmalgamatedCard(strikes);
                    FinishEvent();
                }
                else BeginEventCardSelection(basics, Math.Min(2, basics.Length), token);
                return PredictorInputResult.Accepted;
            case ("WOOD_CARVINGS", "BIRD"):
            case ("WOOD_CARVINGS", "TORUS"):
                BeginEventCardSelection(WoodBasicCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("WOOD_CARVINGS", "SNAKE"):
                BeginEventCardSelection(WoodSnakeCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("SELF_HELP_BOOK", "READ_THE_BACK"):
            case ("SELF_HELP_BOOK", "READ_PASSAGE"):
            case ("SELF_HELP_BOOK", "READ_ENTIRE_BOOK"):
                BeginEventCardSelection(SelfHelpBookCandidates(choice.OptionKey), 1, token);
                return PredictorInputResult.Accepted;
            case ("SELF_HELP_BOOK", "NO_OPTIONS"):
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("SPIRALING_WHIRLPOOL", "OBSERVE"):
                BeginEventCardSelection(SpiralingWhirlpoolCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("SPIRALING_WHIRLPOOL", "DRINK"):
                if (_eventHealth == null && _preview.Event.ProjectHealthIndependently)
                    _preview.Event.UnknownHealingAmount = true;
                else ApplyEventHealth((int)(_eventHealth!.MaxHp * .33m), 0);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("DOORS_OF_LIGHT_AND_DARK", "LIGHT"):
                var lightCards = Working.Deck.Select(Working.Card)
                    .Where(c => c.UpgradeLevel < c.MaxUpgradeLevel)
                    .OrderBy(c => c.Key.Entry, StringComparer.Ordinal)
                    .ThenBy(c => c.UpgradeLevel).ToList();
                _eventRng!.UnstableShuffle(lightCards);
                var lightIds = lightCards.Take(2).Select(c => c.Id).ToImmutableArray();
                foreach (long id in lightIds)
                {
                    var card = Working.Card(id);
                    Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                        card with { UpgradeLevel = card.UpgradeLevel + 1 }));
                }
                Accept(new("DoorsLightUpgrade", 0, lightIds, null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("DOORS_OF_LIGHT_AND_DARK", "DARK"):
                BeginEventCardSelection(Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray(), 1, token);
                return PredictorInputResult.Accepted;
            case ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT"):
                AddTrialCurse("DECAY");
                BeginEventCardSelection(GraveSoulCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("GRAVE_OF_THE_FORGOTTEN", "ACCEPT"):
                if (BeginObtainRelic(new("RELIC", "FORGOTTEN_SOUL"), null, PredictorObtainReturn.Event, 23, token))
                    ResumeEventAfterRelicObtain(23);
                return PredictorInputResult.Accepted;
            case ("ROOM_FULL_OF_CHEESE", "GORGE"):
                BeginCheeseGorge();
                return PredictorInputResult.Accepted;
            case ("ROOM_FULL_OF_CHEESE", "SEARCH"):
                ApplyEventHealth(-14, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                if (BeginObtainRelic(new("RELIC", "CHOSEN_CHEESE"), null, PredictorObtainReturn.Event, 24, token))
                    ResumeEventAfterRelicObtain(24);
                return PredictorInputResult.Accepted;
            case ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK"):
                ApplyEventHealth(6, 6);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL"):
            case ("WATERLOGGED_SCRIPTORIUM", "PRICKLY_SPONGE"):
                SpendEventGold(choice.OptionKey == "TENTACLE_QUILL" ? 55 : 99);
                var steady = Working.Deck.Where(id => PredictorObtainSources.CanEnchant(Context, Working.Card(id)))
                    .ToImmutableArray();
                if (steady.IsEmpty) FinishEvent();
                else BeginEventCardSelection(steady, Math.Min(choice.OptionKey == "TENTACLE_QUILL" ? 1 : 2, steady.Length), token);
                return PredictorInputResult.Accepted;
            case ("THE_LEGENDS_WERE_TRUE", "NAB_THE_MAP"):
                var mapCardKey = new ModelKey("CARD", "SPOILS_MAP");
                PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, mapCardKey).Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, mapCardKey);
                Accept(new("LegendsSpoilsMap", Working.Deck[^1], [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT"):
                ApplyEventHealth(-8, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                var availablePotions = Context.PotionPool;
                if (availablePotions.IsEmpty) { FinishEvent(); return PredictorInputResult.Accepted; }
                var potionDraw = Working.Draw(PredictorStream.Rewards, 0, availablePotions.Length);
                Working = potionDraw.State;
                OfferEventRewardPlans([new(PredictorRewardKind.Potion,
                    PrefilledPotionKey: availablePotions[potionDraw.Value].Key)], token);
                return PredictorInputResult.Accepted;
            case ("BRAIN_LEECH", "SHARE_KNOWLEDGE"):
                BeginBrainLeechGrid();
                return PredictorInputResult.Accepted;
            case ("BRAIN_LEECH", "RIP"):
                ApplyEventHealth(-5, 0);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                OfferEventRewardPlans([new(PredictorRewardKind.Card, CardCount: 3,
                    Colorless: true, NoUpgradeRoll: true, NoCardPoolModifications: true)], token);
                return PredictorInputResult.Accepted;
            case ("INFESTED_AUTOMATON", "STUDY"):
            case ("INFESTED_AUTOMATON", "TOUCH_CORE"):
                AddInfestedAutomatonCard(choice.OptionKey == "STUDY");
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("SPIRIT_GRAFTER", "LET_IT_IN"):
                ApplyEventHealth(25, 0);
                var metamorphosis = new ModelKey("CARD", "METAMORPHOSIS");
                PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, metamorphosis).Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, metamorphosis);
                Accept(new("SpiritGrafterMetamorphosis", Working.Deck[^1], [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("SPIRIT_GRAFTER", "REJECTION"):
                var grafterUpgrades = Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel)
                    .ToImmutableArray();
                if (grafterUpgrades.IsEmpty)
                {
                    ApplyEventHealth(-10, 0);
                    if (Phase != PredictorPhase.Complete) FinishEvent();
                }
                else BeginEventCardSelection(grafterUpgrades, 1, token);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> ThisOrThatOptions()
    {
        _eventGoldAmount = 41 + _eventRng!.NextInt(28);
        return [new("PLAIN", GoldGain: _eventGoldAmount), new("ORNATE")];
    }

    private ImmutableArray<PredictorEventOption> ZenWeaverOptions() =>
    [
        new("BREATHING_TECHNIQUES", GoldCost: 50),
        new("EMOTIONAL_AWARENESS", Enabled: Working.GoldBudget >= 125, GoldCost: 125),
        new("ARACHNID_ACUPUNCTURE", Enabled: Working.GoldBudget >= 250, GoldCost: 250)
    ];

    private ImmutableArray<PredictorEventOption> SelfHelpBookOptions()
    {
        var back = SelfHelpBookCandidates("READ_THE_BACK");
        var passage = SelfHelpBookCandidates("READ_PASSAGE");
        var entire = SelfHelpBookCandidates("READ_ENTIRE_BOOK");
        if (back.IsEmpty && passage.IsEmpty && entire.IsEmpty) return [new("NO_OPTIONS")];
        return [new("READ_THE_BACK", !back.IsEmpty), new("READ_PASSAGE", !passage.IsEmpty),
            new("READ_ENTIRE_BOOK", !entire.IsEmpty)];
    }

    private ImmutableArray<long> SelfHelpBookCandidates(string option) => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        if (!PredictorObtainSources.CanEnchant(Context, card)) return false;
        return option switch
        {
            "READ_THE_BACK" => card.Type == EffectCardType.Attack,
            "READ_PASSAGE" => card.Type == EffectCardType.Skill &&
                PredictorObtainSources.GainsBlockForFresnel(Context, card),
            "READ_ENTIRE_BOOK" => card.Type == EffectCardType.Power,
            _ => false
        };
    }).ToImmutableArray();

    private ImmutableArray<long> SpiralingWhirlpoolCandidates() => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        var definition = PredictorCardChanges.Definition(Context, card.Key);
        return definition.Rarity == "Basic" && (definition.IsStrike || definition.IsDefend) &&
            PredictorObtainSources.CanEnchant(Context, card);
    }).ToImmutableArray();

    private ImmutableArray<long> GraveSoulCandidates() => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        return PredictorCardLevelEffects.Resolve(Context, card).LocalExhaust &&
            PredictorObtainSources.CanEnchant(Context, card);
    }).ToImmutableArray();

    private void BeginCheeseGorge()
    {
        var state = Working;
        var plan = new PredictorRewardPlan(PredictorRewardKind.Card, CardCount: 8,
            Odds: PredictorCardOdds.Uniform, Rarity: EffectCardRarity.Common,
            NoUpgradeRoll: true, NoCardPoolModifications: true);
        var cards = PredictorRewardGeneration.GenerateCards(Context, ref state, plan, isCardReward: false, out var nicheScope);
        var offered = ImmutableArray.CreateBuilder<PredictorCard>(cards.Length);
        foreach (var card in cards)
        {
            offered.Add(card with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor });
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        Working = state;
        _eventOfferedCards = offered.ToImmutable();
        _eventOfferedCardNicheScope = nicheScope;
        _preview.Partial?.ObserveRewardSourceIds(EventCardOffer());
        _eventCardsRequired = 2;
        _eventCardCandidates = _eventOfferedCards.Select(c => c.Id).ToImmutableArray();
        EventOptions = [];
        Phase = PredictorPhase.EventCards;
        _requestId++;
    }

    private void BeginBrainLeechGrid()
    {
        var state = Working;
        var plan = new PredictorRewardPlan(PredictorRewardKind.Card, CardCount: 5,
            Odds: PredictorCardOdds.Regular, NoUpgradeRoll: true, NoCardPoolModifications: true);
        var cards = PredictorRewardGeneration.GenerateCards(Context, ref state, plan, isCardReward: false, out var nicheScope);
        var offered = ImmutableArray.CreateBuilder<PredictorCard>(cards.Length);
        foreach (var card in cards)
        {
            offered.Add(card with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor });
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        Working = state;
        _eventOfferedCards = offered.ToImmutable();
        _eventOfferedCardNicheScope = nicheScope;
        _preview.Partial?.ObserveRewardSourceIds(EventCardOffer());
        _eventCardsRequired = 1;
        _eventCardCandidates = _eventOfferedCards.Select(c => c.Id).ToImmutableArray();
        EventOptions = [];
        Phase = PredictorPhase.EventCards;
        _requestId++;
    }

    private void AddInfestedAutomatonCard(bool power)
    {
        var catalog = Context.Catalog!;
        var pool = catalog.CharacterCards.Where(c =>
        {
            var definition = PredictorCardChanges.Definition(Context, c.CardKey);
            return power ? c.CardType == EffectCardType.Power : definition.CanonicalCost == 0 && !definition.CostsX;
        }).Select(c => c.CardKey).ToImmutableArray();
        if (pool.IsEmpty) return;
        var state = Working;
        var plan = new PredictorRewardPlan(PredictorRewardKind.Card, CardCount: 1,
            Odds: PredictorCardOdds.Regular, NoUpgradeRoll: true,
            CardPoolOverride: pool, NoCardPoolModifications: !power);
        var card = PredictorRewardGeneration.GenerateCards(Context, ref state, plan, isCardReward: false)[0];
        PredictorSettlementEffects.RequireImplementedCard(card, context: Context);
        Working = PredictorObtainSources.AddCard(Context, state, card);
        Accept(new(power ? "AutomatonStudyCard" : "AutomatonCoreCard", Working.Deck[^1], [], null, null));
    }

    private ImmutableArray<long> WoodBasicCandidates() => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        var definition = PredictorCardChanges.Definition(Context, card.Key);
        return card.Removable && definition.Rarity == "Basic" && !definition.IsQuest;
    }).ToImmutableArray();

    private ImmutableArray<long> WoodSnakeCandidates() => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        var definition = PredictorCardChanges.Definition(Context, card.Key);
        return PredictorObtainSources.CanEnchant(Context, card) && !definition.CostsX;
    }).ToImmutableArray();

    private ImmutableArray<long> SownCandidates() => Working.Deck.Where(id =>
    {
        var card = Working.Card(id);
        return card.Type is EffectCardType.Attack or EffectCardType.Skill or EffectCardType.Power &&
            card.Enchantment == null && !PredictorCardChanges.Definition(Context, card.Key).Unplayable;
    }).ToImmutableArray();

    private void AddAmalgamatedCard(bool strikes)
    {
        var key = strikes ? "ULTIMATE_STRIKE" : "ULTIMATE_DEFEND";
        var prototype = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key.Entry == key).Prototype;
        PredictorSettlementEffects.RequireImplementedCard(prototype, context: Context);
        Working = PredictorObtainSources.AddCard(Context, Working, prototype);
        Accept(new("AmalgamatorCard", Working.Deck[^1], [], null, null));
    }

    private ImmutableArray<PredictorEventOption> LuminousChoirOptions()
    {
        _eventGoldAmount = 149 - _eventRng!.NextInt(50);
        return [new("REACH_INTO_THE_FLESH"),
            new("OFFER_TRIBUTE", Enabled: Working.GoldBudget >= _eventGoldAmount, GoldCost: _eventGoldAmount)];
    }

}
