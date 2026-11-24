using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
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
                ApplyFixedEventHealth(choice.OptionKey);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                Working = PredictorSettlementEffects.TakeGold(Working, _eventGoldAmount);
                Accept(new("ThisOrThatGold", 0, [], null, Working.GoldBudget));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("THIS_OR_THAT", "ORNATE"):
                BeginEventFrontRelic(4, token);
                return PredictorInputResult.Accepted;
            case ("UNREST_SITE", "REST"):
                ApplyFixedEventHealth(choice.OptionKey);
                AddTrialCurse(Beta111EventOptionRules.UnrestRestCurse.Entry);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("UNREST_SITE", "KILL"):
                ApplyFixedEventHealth(choice.OptionKey);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                BeginEventFrontRelic(5, token);
                return PredictorInputResult.Accepted;
            case ("SAPPHIRE_SEED", "EAT"):
                ApplyFixedEventHealth(choice.OptionKey);
                BeginEventCardSelection(Working.Deck.Where(id => Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToImmutableArray(), 1, token);
                return PredictorInputResult.Accepted;
            case ("SAPPHIRE_SEED", "PLANT"):
                BeginEventCardSelection(SownCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("ZEN_WEAVER", "BREATHING_TECHNIQUES"):
                var breathing = Beta111EventOptionRules.ZenWeaver(choice.OptionKey);
                SpendEventGold(breathing.Cost);
                var enlightenment = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key == breathing.AddedCard).Prototype;
                PredictorSettlementEffects.RequireImplementedCard(enlightenment, context: Context);
                for (int i = 0; i < breathing.AddedCopies; i++)
                    Working = PredictorObtainSources.AddCard(Context, Working, enlightenment);
                Accept(new("ZenWeaverCards", 0, Working.Deck.TakeLast(breathing.AddedCopies).ToImmutableArray(), null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("ZEN_WEAVER", "EMOTIONAL_AWARENESS"):
            case ("ZEN_WEAVER", "ARACHNID_ACUPUNCTURE"):
                var zenRemoval = Beta111EventOptionRules.ZenWeaver(choice.OptionKey);
                _eventGoldAmount = zenRemoval.Cost.Amount;
                var removable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (removable.IsEmpty)
                {
                    SpendEventGold(zenRemoval.Cost);
                    FinishEvent();
                }
                else BeginEventCardSelection(removable, Math.Min(zenRemoval.RemovalCount, removable.Length), token);
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
                    AddTrialCurse(EventCardProductAfterSelection(choice.OptionKey).Entry);
                    FinishEvent();
                }
                else BeginEventCardSelection(bathable, 1, token);
                return PredictorInputResult.Accepted;
            case ("FIELD_OF_MAN_SIZED_HOLES", "RESIST"):
                var holesRemovable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (holesRemovable.IsEmpty) { AddTrialCurse(EventCardProductAfterSelection(choice.OptionKey).Entry); FinishEvent(); }
                else BeginEventCardSelection(holesRemovable, Math.Min(2, holesRemovable.Length), token);
                return PredictorInputResult.Accepted;
            case ("FIELD_OF_MAN_SIZED_HOLES", "ENTER_YOUR_HOLE"):
                BeginEventCardSelection(SownCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH"):
                var choirRemovable = Working.Deck.Where(id => Working.Card(id).Removable).ToImmutableArray();
                if (choirRemovable.IsEmpty) { AddTrialCurse(EventCardProductAfterSelection(choice.OptionKey).Entry); FinishEvent(); }
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
                    AddAmalgamatedCard();
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
            case ("DOORS_OF_LIGHT_AND_DARK", "LIGHT") when Context.UsesNonCombatGeneration:
                BeginEventCardSelection(Working.Deck.Where(id =>
                    Working.Card(id).UpgradeLevel < Working.Card(id).MaxUpgradeLevel).ToImmutableArray(), 1, token);
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
                AddTrialCurse(FixedEventCardProduct(choice.OptionKey).Entry);
                BeginEventCardSelection(GraveSoulCandidates(), 1, token);
                return PredictorInputResult.Accepted;
            case ("GRAVE_OF_THE_FORGOTTEN", "ACCEPT"):
                if (BeginObtainRelic(FixedEventRelicProduct(choice.OptionKey), null, PredictorObtainReturn.Event, 23, token))
                    ResumeEventAfterRelicObtain(23);
                return PredictorInputResult.Accepted;
            case ("ROOM_FULL_OF_CHEESE", "GORGE"):
                BeginCheeseGorge();
                return PredictorInputResult.Accepted;
            case ("ROOM_FULL_OF_CHEESE", "SEARCH"):
                ApplyFixedEventHealth(choice.OptionKey);
                if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
                if (BeginObtainRelic(FixedEventRelicProduct(choice.OptionKey), null, PredictorObtainReturn.Event, 24, token))
                    ResumeEventAfterRelicObtain(24);
                return PredictorInputResult.Accepted;
            case ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK"):
                ApplyFixedEventHealth(choice.OptionKey);
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL"):
            case ("WATERLOGGED_SCRIPTORIUM", "PRICKLY_SPONGE"):
                var scriptorium = Beta111EventOptionRules.Scriptorium(choice.OptionKey);
                SpendEventGold(scriptorium.Cost);
                var steady = Working.Deck.Where(id => PredictorObtainSources.CanEnchant(Context, Working.Card(id)))
                    .ToImmutableArray();
                if (steady.IsEmpty) FinishEvent();
                else BeginEventCardSelection(steady, Math.Min(scriptorium.TargetCount, steady.Length), token);
                return PredictorInputResult.Accepted;
            case ("THE_LEGENDS_WERE_TRUE", "NAB_THE_MAP"):
                var mapCardKey = FixedEventCardProduct(choice.OptionKey);
                PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, mapCardKey).Prototype, context: Context);
                Working = PredictorObtainSources.AddCard(Context, Working, mapCardKey);
                Accept(new("LegendsSpoilsMap", Working.Deck[^1], [], null, null));
                FinishEvent();
                return PredictorInputResult.Accepted;
            case ("THE_LEGENDS_WERE_TRUE", "SLOWLY_FIND_AN_EXIT"):
                ApplyFixedEventHealth(choice.OptionKey);
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
                ApplyFixedEventHealth(choice.OptionKey);
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
                ApplyFixedEventHealth(choice.OptionKey);
                var metamorphosis = FixedEventCardProduct(choice.OptionKey);
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
                    ApplyFixedEventHealth(choice.OptionKey);
                    if (Phase != PredictorPhase.Complete) FinishEvent();
                }
                else BeginEventCardSelection(grafterUpgrades, 1, token);
                return PredictorInputResult.Accepted;
        }
        throw new NotImplementedException("PredictorEventBranchPending:" + _eventKey + ":" + choice.OptionKey);
    }

    private ImmutableArray<PredictorEventOption> ThisOrThatOptions()
    {
        _eventGoldAmount = Beta111EventRules.ThisOrThatGold(_eventRng!);
        return [new("PLAIN", GoldGain: _eventGoldAmount), new("ORNATE")];
    }

    private ImmutableArray<PredictorEventOption> ZenWeaverOptions() =>
        new[] { "BREATHING_TECHNIQUES", "EMOTIONAL_AWARENESS", "ARACHNID_ACUPUNCTURE" }.Select(key =>
        {
            var choice = Beta111EventOptionRules.ZenWeaver(key);
            return new PredictorEventOption(key, Enabled: choice.Cost.CanChoose(Working.GoldBudget), GoldCost: choice.Cost.Amount);
        }).ToImmutableArray();

    private ImmutableArray<PredictorEventOption> ScriptoriumOptions() =>
        new[] { "TENTACLE_QUILL", "PRICKLY_SPONGE" }.Select(key =>
        {
            var choice = Beta111EventOptionRules.Scriptorium(key);
            return new PredictorEventOption(key, Enabled: choice.Cost.CanChoose(Working.GoldBudget), GoldCost: choice.Cost.Amount);
        }).Prepend(FixedEventHealthOption("BLOODY_INK")).ToImmutableArray();

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

    private void AddAmalgamatedCard()
    {
        var key = EventCardProductAfterSelection(_eventBranch!);
        var prototype = Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key == key).Prototype;
        PredictorSettlementEffects.RequireImplementedCard(prototype, context: Context);
        Working = PredictorObtainSources.AddCard(Context, Working, prototype);
        Accept(new("AmalgamatorCard", Working.Deck[^1], [], null, null));
    }

    private ImmutableArray<PredictorEventOption> LuminousChoirOptions()
    {
        _eventGoldAmount = Beta111EventRules.LuminousChoirGold(_eventRng!);
        return [new("REACH_INTO_THE_FLESH"),
            new("OFFER_TRIBUTE", Enabled: Working.GoldBudget >= _eventGoldAmount, GoldCost: _eventGoldAmount)];
    }

}
