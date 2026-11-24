using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorCard> _eventOfferedCards = [];

    private PredictorInputResult? TrySubmitEventCardSelection(PredictorInput input, CancellationToken token)
    {
        if (Phase != PredictorPhase.EventCards) return null;
        if (input is not SelectEventCards selection || selection.CardIds.IsDefault ||
            selection.CardIds.Length != _eventCardsRequired || selection.CardIds.Distinct().Count() != selection.CardIds.Length ||
            selection.CardIds.Any(id => !_eventCardCandidates.Contains(id))) return PredictorInputResult.Invalid;
        if (_eventKey?.Entry is ("ROOM_FULL_OF_CHEESE" or "BRAIN_LEECH") &&
            _eventBranch is ("GORGE" or "SHARE_KNOWLEDGE"))
        {
            var offer = EventCardOffer();
            var knowledge = _preview.Partial?.EventOfferedCardKnowledge(this);
            foreach (long id in selection.CardIds)
            {
                var card = _eventOfferedCards.Single(c => c.Id == id);
                PredictorSettlementEffects.RequireImplementedCard(card, context: Context);
                Working = ReceiveRewardCard(offer, card, knowledge);
            }
            Accept(new(_eventKey.Value.Entry == "BRAIN_LEECH" ? "BrainLeechGridCard" : "CheeseGorgeCards",
                0, selection.CardIds, null, null));
        }
        else if (_eventKey?.Entry == "DOORS_OF_LIGHT_AND_DARK" && _eventBranch == "LIGHT" && Context.UsesNonCombatGeneration)
        {
            var card = Working.Card(selection.CardIds[0]);
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, card with { UpgradeLevel = card.UpgradeLevel + 1 }));
            Accept(new("DoorsLightUpgrade", 0, selection.CardIds, null, null));
        }
        else if (_eventKey?.Entry == "DOORS_OF_LIGHT_AND_DARK" && _eventBranch == "DARK")
        {
            RemoveEventCards(selection.CardIds);
            Accept(new("DoorsDarkRemove", 0, selection.CardIds, null, null));
        }
        else if (_eventBranch is "EMOTIONAL_AWARENESS" or "ARACHNID_ACUPUNCTURE")
        {
            RemoveEventCards(selection.CardIds);
            Accept(new("ZenWeaverRemove", 0, selection.CardIds, null, null));
            SpendEventGold(PendingZenWeaverCost());
        }
        else if (_eventBranch == "BATHE")
        {
            RemoveEventCards(selection.CardIds);
            Accept(new("WellspringRemove", 0, selection.CardIds, null, null));
            AddTrialCurse(EventCardProductAfterSelection(_eventBranch!).Entry);
        }
        else if (_eventBranch == "RESIST")
        {
            RemoveEventCards(selection.CardIds);
            Accept(new("HolesRemove", 0, selection.CardIds, null, null));
            AddTrialCurse(EventCardProductAfterSelection(_eventBranch!).Entry);
        }
        else if (_eventBranch == "REACH_INTO_THE_FLESH")
        {
            RemoveEventCards(selection.CardIds);
            Accept(new("ChoirRemove", 0, selection.CardIds, null, null));
            AddTrialCurse(EventCardProductAfterSelection(_eventBranch!).Entry);
        }
        else if (_eventBranch is "COMBINE_STRIKES" or "COMBINE_DEFENDS")
        {
            RemoveEventCards(selection.CardIds);
            Accept(new("AmalgamatorRemove", 0, selection.CardIds, null, null));
            AddAmalgamatedCard();
        }
        else if (_eventKey?.Entry == "WOOD_CARVINGS" && _eventBranch is ("BIRD" or "TORUS"))
        {
            long original = selection.CardIds[0];
            var replacement = new ModelKey("CARD", _eventBranch == "BIRD" ? "PECK" : "TORIC_TOUGHNESS");
            PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, replacement).Prototype, context: Context);
            var changed = PredictorCardChanges.TransformTo(Context, Working, original, replacement);
            Working = changed.State;
            Accept(new("WoodCarvingsTransform", 0, selection.CardIds, null, null)
                { Transformations = [changed.Change] });
        }
        else if (_eventKey?.Entry == "WOOD_CARVINGS" && _eventBranch == "SNAKE")
        {
            long id = selection.CardIds[0];
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, Working.Card(id) with
                { Enchantment = new("ENCHANTMENT", "SLITHER"), EnchantmentAmount = 1 }));
            Accept(new("WoodCarvingsSlither", id, [], null, null));
        }
        else if (_eventKey?.Entry == "SELF_HELP_BOOK")
        {
            long id = selection.CardIds[0];
            string enchantment = _eventBranch switch
            {
                "READ_THE_BACK" => "SHARP", "READ_PASSAGE" => "NIMBLE", "READ_ENTIRE_BOOK" => "SWIFT",
                _ => throw new InvalidOperationException("PredictorSelfHelpBranchInvalid")
            };
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, Working.Card(id) with
                { Enchantment = new("ENCHANTMENT", enchantment), EnchantmentAmount = 2 }));
            Accept(new("SelfHelpBookEnchant", id, [], null, null));
        }
        else if (_eventKey?.Entry == "SPIRALING_WHIRLPOOL")
        {
            long id = selection.CardIds[0];
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, Working.Card(id) with
                { Enchantment = new("ENCHANTMENT", "SPIRAL"), EnchantmentAmount = 1 }));
            Accept(new("SpiralingWhirlpoolEnchant", id, [], null, null));
        }
        else if (_eventKey?.Entry == "GRAVE_OF_THE_FORGOTTEN")
        {
            long id = selection.CardIds[0];
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, Working.Card(id) with
                { Enchantment = new("ENCHANTMENT", "SOULS_POWER"), EnchantmentAmount = 1 }));
            Accept(new("GraveSoulsPowerEnchant", id, [], null, null));
        }
        else if (_eventKey?.Entry == "WATERLOGGED_SCRIPTORIUM")
        {
            var choice = Beta111EventOptionRules.Scriptorium(_eventBranch!);
            foreach (long id in selection.CardIds)
                Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, Working.Card(id) with
                    { Enchantment = choice.Enchantment, EnchantmentAmount = choice.Amount }));
            Accept(new("ScriptoriumSteadyEnchant", 0, selection.CardIds, null, null));
        }
        else if (_eventKey?.Entry == "ENDLESS_CONVEYOR" && _conveyorDish == "JELLY_LIVER")
        {
            Accept(new("ConveyorJellyTransform", 0, selection.CardIds, null, null)
                { Transformations = TransformEventCards(selection.CardIds, token) });
            AdvanceConveyor();
            return PredictorInputResult.Accepted;
        }
        else if (_eventBranch is "APPROACH" or "PLANT" or "PUSH" or "ENTER_YOUR_HOLE")
        {
            long id = selection.CardIds[0];
            var key = _eventBranch switch { "APPROACH" => new ModelKey("ENCHANTMENT", "CORRUPTED"),
                "PUSH" => new ModelKey("ENCHANTMENT", "VIGOROUS"),
                "ENTER_YOUR_HOLE" => new ModelKey("ENCHANTMENT", "PERFECT_FIT"),
                _ => new ModelKey("ENCHANTMENT", "SOWN") };
            Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context, Working.Card(id) with
                { Enchantment = key, EnchantmentAmount = _eventBranch == "PUSH" ? 8 : 1 }));
            Accept(new("EventEnchant", id, [], null, null));
            if (_eventBranch == "PUSH") _eventRng!.NextInt(100);
        }
        else if (_eventBranch is "MAINTAIN_CONTROL" or "MERCHANT_INNOCENT" or "EAT" or "REJECTION")
        {
            foreach (long id in selection.CardIds)
            {
                var card = Working.Card(id);
                Working = Working.WithCard(PredictorCardLevelEffects.Reconcile(Context,
                    card with { UpgradeLevel = card.UpgradeLevel + 1 }));
            }
            Accept(new("EventUpgrade", 0, selection.CardIds, null, null));
        }
        else
        {
            Accept(new("EventTransform", 0, selection.CardIds, null, null)
                { Transformations = TransformEventCards(selection.CardIds, token) });
        }
        if (_eventKey!.Value.Entry == "WHISPERING_HOLLOW") ApplyFixedEventHealth(_eventBranch!);
        if (Phase == PredictorPhase.Complete) return PredictorInputResult.Accepted;
        if (_eventKey.Value.Entry == "SPIRIT_GRAFTER" && _eventBranch == "REJECTION") ApplyFixedEventHealth(_eventBranch);
        if (Phase != PredictorPhase.Complete) FinishEvent();
        return PredictorInputResult.Accepted;
    }

    private ImmutableArray<PredictorTransformation> TransformEventCards(ImmutableArray<long> selected, CancellationToken token)
    {
        var reads = _preview.EventTrace?.TransformInputs(this, selected) ?? [];
        var transformed = PredictorCardChanges.TransformInOrder(Context, Working, selected, _eventRng!, token);
        Working = transformed.State;
        _eventRng = transformed.Rng;
        _preview.EventTrace?.TransformWrite(this, reads);
        return transformed.Changes;
    }

    private void BeginEventCardSelection(ImmutableArray<long> candidates, int count, CancellationToken token)
    {
        if (!_preview.Event.DeferCardTarget && candidates.Length == 0 && count == 1)
        {
            FinishEvent();
            return;
        }
        _eventCardsRequired = count;
        _eventCardCandidates = candidates;
        EventOptions = [];
        Phase = PredictorPhase.EventCards;
        _requestId++;
        if (!_preview.Event.DeferCardTarget && candidates.Length == count)
            _ = TrySubmitEvent(new SelectEventCards(candidates), token);
    }

}
