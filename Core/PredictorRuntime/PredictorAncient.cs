using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorAncientOffer(long Id, ModelKey Relic, bool Selectable,
    ModelKey? CharacterTarget, long? StarterCardId, long? StarterRelicId, ModelKey? PreparedCard, string? Variant)
{
    // Existing authored/captured complete offers retain their original contract.
    // A provider's unknown target is distinct from an established absent target.
    public bool CharacterTargetKnown { get; init; } = true;
    public bool CharacterTargetSourceOrderKnown { get; init; } = true;
    public string? CharacterTargetIssueCode { get; init; }
}
internal sealed record ChooseAncientOffer(long OfferId) : PredictorInput;

internal sealed partial class PredictorRun
{
    internal ImmutableArray<PredictorAncientOffer> AncientOffers { get; private set; } = [];
    internal ModelKey? CurrentAncient { get; private set; }

    private void EnterAncient(PredictorPosition position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var authority = Context.AncientAuthority ?? throw new InvalidOperationException("PredictorAncientAuthorityMissing");
        var catalog = Context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var ancient = Working.Acts.Single(a => a.Act == position.Act).Ancient;
        var cards = Working.Deck.Select(Working.Card).ToArray();
        var starterCard = cards.FirstOrDefault(c => c.Key.Entry is "BASH" or "NEUTRALIZE" or "UNLEASH" or "FALLING_STAR" or "DUALCAST");
        var starterRelic = Working.Relics.FirstOrDefault(r => catalog.StarterRelics.Contains(r.Key));
        var conditions = AncientConditions(Context, Working);
        // The captured donor is metadata, not an earlier player's dynamic facts.
        // This runtime currently starts with no run modifiers. Live inventory
        // predicates above replace the Filter author's declaration profile.
        var contexts = authority.AncientEventContexts.Select(c => c.Act == position.Act && c.AncientKey == ancient
            ? c with { HookDecision = Beta109HookDecision.Allow } : c).ToArray();
        var trace = _preview.Partial?.BeginAncientGeneration(this, position, ancient, conditions,
            authority with { AncientEventContexts = contexts });
        var generated = Beta109AncientOptionProvider.PredictEventLocal(authority with { AncientEventContexts = contexts },
            position.Act, ancient, 0, Context.Character, conditions);
        if (generated.Options.Count == 0 || generated.Options.Any(o => o.OptionPrecision != PredictionPrecision.Exact))
            throw new InvalidOperationException("PredictorAncientGenerationIncomplete:" + generated.IssueCode);
        var eventContext = contexts.FirstOrDefault(c => c.Act == position.Act && c.AncientKey == ancient && c.PlayerSlot == 0)
            ?? contexts.FirstOrDefault(c => c.Act == position.Act && c.AncientKey == ancient && c.IsShared);
        var state = Working with { Position = position };
        var offers = ImmutableArray.CreateBuilder<PredictorAncientOffer>();
        foreach (var option in generated.Options)
        {
            ModelKey? preparedCard = null;
            if (option.OptionKey.Entry == "DUSTY_TOME")
            {
                if (catalog.DustyTomeCards.IsEmpty) throw new InvalidOperationException("PredictorDustyTomePoolMissing");
                (int index, state) = state.Draw(PredictorStream.Rewards, 0, catalog.DustyTomeCards.Length);
                preparedCard = catalog.DustyTomeCards[index];
            }
            offers.Add(new(state.NextInstanceId, option.OptionKey, option.IsSelectable,
                option.CharacterTarget?.CharacterKey,
                option.OptionKey.Entry == "ARCHAIC_TOOTH" ? starterCard?.Id : null,
                option.OptionKey.Entry == "TOUCH_OF_OROBAS" ? starterRelic?.Id : null,
                preparedCard, option.VariantId)
            {
                CharacterTargetKnown = option.CharacterTarget?.IsKnown != false,
                CharacterTargetSourceOrderKnown = option.CharacterTarget?.IsKnown != false || eventContext?.UnlockedCharacterSourceOrderExact == true,
                CharacterTargetIssueCode = option.CharacterTarget?.IsKnown == false ? option.CharacterTarget.IssueCode : null
            });
            state = state with { NextInstanceId = state.NextInstanceId + 1 };
        }
        Working = state;
        AncientOffers = offers.ToImmutable(); CurrentAncient = ancient;
        Rewards = [];
        Accept(new("EnterAncient:" + ancient.Entry, 0, [], null, null));
        Phase = PredictorPhase.AncientChoice;
        _requestId++;
        if (trace != null) _preview.Partial!.CompleteAncientGeneration(this, trace, generated);
    }

    private PredictorInputResult SubmitAncient(ChooseAncientOffer input, CancellationToken token)
    {
        var offer = AncientOffers.FirstOrDefault(o => o.Id == input.OfferId);
        if (offer == null || !offer.Selectable) return PredictorInputResult.Invalid;
        // Reject only this unresolved setup before Accept/obtain/replay writes.
        // In particular, Sea Glass must never fall back to a guessed card pool.
        if (!offer.CharacterTargetKnown) return PredictorInputResult.Invalid;
        var prepared = new PredictorRelic(0, offer.Relic, Working.Position.Floor)
        {
            CharacterTarget = offer.CharacterTarget, StarterCardId = offer.StarterCardId,
            StarterRelicId = offer.StarterRelicId, PreparedCard = offer.PreparedCard, VariantId = offer.Variant
        };
        _preview.Partial?.RecordAncientSelection(this, input, offer, prepared);
        Accept(new("ChooseAncient:" + offer.Relic.Entry, offer.Id, [], null, null));
        if (BeginObtainRelic(offer.Relic, null, PredictorObtainReturn.Ancient, offer.Id, token, prepared)) CompleteNode();
        return PredictorInputResult.Accepted;
    }

    private void ResetAncient() { AncientOffers = []; CurrentAncient = null; }

    private static PredictorCombatPendingCause AncientCharacterTargetCause(string node, PredictorAncientOffer offer)
    {
        string code = string.IsNullOrWhiteSpace(offer.CharacterTargetIssueCode)
            ? "AncientCharacterTargetNotObserved" : offer.CharacterTargetIssueCode;
        bool unavailable = code == "NoOtherUnlockedCharacterCandidate" && offer.CharacterTargetSourceOrderKnown;
        return PredictorRouteCauseEvidence.CreateExplicit(code, node, PredictorCombatDomain.None,
            unavailable ? PredictorPendingReasonKind.KnownUnavailable : PredictorPendingReasonKind.MissingObservation,
            unavailable
                ? "Beta109AncientOptionProvider.GenerateOrobas: the established unlocked-character list contains no other character target."
                : "Beta109AncientOptionProvider.GenerateOrobas: captured unlocked-character membership/source-order authority does not establish the selected target.",
            unavailable ? [] : ["ancient.unlockedCharacters.sourceOrder"], occurrenceKey: "sea-glass-target");
    }

    internal static AncientOptionConditionProfile AncientConditions(PredictorContext context, PredictorState state)
    {
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var cards = state.Deck.Select(state.Card).ToArray();
        var definitions = catalog.CardDefinitions.ToDictionary(d => d.Prototype.Key);
        bool Enchantable(PredictorCard card) => PredictorObtainSources.CanEnchant(context, card);
        return new(
            cards.Any(c => definitions[c.Key].IsStrike && definitions[c.Key].Rarity == "Basic"),
            cards.Count(Enchantable) >= 4,
            cards.Count(c => c.Type == EffectCardType.Attack && Enchantable(c)) >= 3,
            cards.Count(c => definitions[c.Key].IsDefend && Enchantable(c)) >= 3,
            !state.Relics.Any(r => catalog.EventPetRelics.Contains(r.Key)) && !cards.Any(c => c.Key.Entry == "BYRDONIS_EGG"),
            cards.Count(c => !PredictorCardLevelEffects.Resolve(context, c).Eternal) >= 5,
            cards.Any(c => c.Key.Entry is "BASH" or "NEUTRALIZE" or "UNLEASH" or "FALLING_STAR" or "DUALCAST"),
            state.Relics.Any(r => catalog.StarterRelics.Contains(r.Key)),
            !context.Modifiers.Any(m => m.Key.Entry is "DRAFT" or "INSANITY" or "SEALED_DECK"));
    }
}
