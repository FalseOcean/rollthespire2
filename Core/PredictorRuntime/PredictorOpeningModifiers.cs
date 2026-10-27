using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record ActivateOpeningModifier(int ModifierOrdinal) : PredictorInput;
internal sealed record SelectOpeningModifierCards(ImmutableArray<long> CardIds) : PredictorInput;

internal sealed partial class PredictorRun
{
    private int _openingModifierOrdinal;
    private int _draftPicks;
    internal ImmutableArray<PredictorCard> OpeningModifierCards { get; private set; } = [];
    internal int OpeningModifierCardsRequired { get; private set; }
    internal int? OpeningModifierOrdinal => Phase is PredictorPhase.OpeningModifier or PredictorPhase.OpeningModifierCards ? _openingModifierOrdinal : null;
    private static bool HasOpeningAction(PredictorRunModifier m) => m.Key.Entry is "ALL_STAR" or "DRAFT" or "INSANITY" or "SEALED_DECK" or "SPECIALIZED";

    private void BeginModifierOpening()
    {
        _openingModifierOrdinal = -1;
        ContinueModifierOpening();
    }

    private void ContinueModifierOpening()
    {
        OpeningModifierCards = []; OpeningModifierCardsRequired = 0;
        do { _openingModifierOrdinal++; }
        while (_openingModifierOrdinal < Context.Modifiers.Length && !HasOpeningAction(Context.Modifiers[_openingModifierOrdinal]));
        if (_openingModifierOrdinal == Context.Modifiers.Length)
        {
            if (_pendingFirstActHooks) { _pendingFirstActHooks = false; AfterRuntimeActEntered(); }
            Working = Working with { OpeningCompleted = true };
            CompleteNode();
        }
        else { Phase = PredictorPhase.OpeningModifier; _requestId++; }
    }

    private PredictorInputResult SubmitModifierOpening(PredictorInput input)
    {
        string source = Context.Modifiers[_openingModifierOrdinal].Key.Entry;
        if (Phase == PredictorPhase.OpeningModifierCards)
        {
            if (input is not SelectOpeningModifierCards select || select.CardIds.IsDefault ||
                select.CardIds.Length != OpeningModifierCardsRequired || select.CardIds.Distinct().Count() != select.CardIds.Length ||
                select.CardIds.Any(id => !OpeningModifierCards.Any(c => c.Id == id))) return PredictorInputResult.Invalid;
            foreach (long id in select.CardIds)
            {
                var card = OpeningModifierCards.Single(c => c.Id == id);
                Working = Working with { Cards = Working.Cards.Add(card) };
                Working = PredictorObtainSources.AddExistingCard(Context, Working, card);
            }
            Accept(new("OpeningModifierSelect:" + source, _openingModifierOrdinal, select.CardIds, null, null));
            if (source == "DRAFT" && ++_draftPicks < 10) GenerateModifierGrid(source);
            else { RemovePandorasBoxFromBags(); ContinueModifierOpening(); }
            return PredictorInputResult.Accepted;
        }
        if (input is not ActivateOpeningModifier action || action.ModifierOrdinal != _openingModifierOrdinal) return PredictorInputResult.Invalid;
        Accept(new("OpeningModifier:" + source, _openingModifierOrdinal, [], null, null));
        if (source is "DRAFT" or "SEALED_DECK")
        { _draftPicks = 0; GenerateModifierGrid(source); return PredictorInputResult.Accepted; }
        var state = Working;
        int count = source == "INSANITY" ? 30 : source == "ALL_STAR" ? 5 : 1;
        for (int i = 0; i < count; i++)
        {
            var cards = PredictorRewardGeneration.GenerateCards(Context, ref state,
                new(PredictorRewardKind.Card, CardCount: 1, Odds: PredictorCardOdds.Uniform, Colorless: source == "ALL_STAR",
                    NoUpgradeRoll: true, NoCardPoolModifications: source == "ALL_STAR", NoRarityModification: true), isCardReward: false);
            if (source == "SPECIALIZED")
                for (int copy = 0; copy < 5; copy++) state = PredictorObtainSources.CloneCardToDeck(Context, state, cards[0] with { FloorAdded = state.Position.Floor });
            else state = PredictorObtainSources.AddCard(Context, state, cards[0]);
        }
        Working = state;
        if (source == "INSANITY") RemovePandorasBoxFromBags();
        ContinueModifierOpening();
        return PredictorInputResult.Accepted;
    }

    private void GenerateModifierGrid(string source)
    {
        var state = Working;
        var cards = PredictorRewardGeneration.GenerateCards(Context, ref state,
            new(PredictorRewardKind.Card, CardCount: source == "DRAFT" ? 3 : 30, NoUpgradeRoll: true,
                ForceRarityOddsChange: source == "SEALED_DECK"), isCardReward: true);
        long nextId = state.NextInstanceId;
        OpeningModifierCards = cards.Select(c => c with { Id = nextId++, FloorAdded = state.Position.Floor }).ToImmutableArray();
        state = state with { NextInstanceId = nextId };
        Working = state;
        OpeningModifierCardsRequired = source == "DRAFT" ? 1 : 10;
        Phase = PredictorPhase.OpeningModifierCards; _requestId++;
    }

    private void RemovePandorasBoxFromBags()
    {
        var key = new ModelKey("RELIC", "PANDORAS_BOX");
        Working = Working with { PersonalBag = Working.PersonalBag.Remove(key), SharedBag = Working.SharedBag.Remove(key) };
    }

    private void AfterRuntimeActEntered()
    {
        foreach (var modifier in Context.Modifiers)
        {
            if (modifier.Key.Entry != "CURSED_RUN") continue;
            var pool = Context.Catalog!.ModifierCurseCards;
            if (pool.IsDefaultOrEmpty) throw new InvalidOperationException("PredictorModifierCursePoolMissing");
            (int selected, var state) = Working.Draw(PredictorStream.Niche, 0, pool.Length);
            Working = PredictorObtainSources.AddCard(Context, state, pool[selected]);
            Accept(new("CursedRunActEntered", Working.Deck[^1], [], null, null));
        }
    }
}
