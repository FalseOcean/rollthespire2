using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorCombatPotionAction { Use, Discard, FairyTriggered, AdditionalAlchemize, LizardTailTriggered }
internal sealed record PredictorPotionReference(long? ExistingId = null, int? GeneratedOrdinal = null);
// Placement is relative to a known effective source instance, not a turn/action
// log. A null BeforeCardId means after the displayed automatic card effects.
internal sealed record PredictorCombatPotionChange(PredictorCombatPotionAction Action,
    PredictorPotionReference? Potion = null, long? BeforeCardId = null, int BeforeOccurrence = 0,
    long? RelicId = null, int? BeforeTemporarySourceIndex = null);
internal sealed record PredictorCombatPotionFacts(bool NoAdditionalPotionChange,
    ImmutableArray<PredictorCombatPotionChange> Changes)
{
    internal static PredictorCombatPotionFacts NoAdditional => new(true, []);
}

internal static class PredictorCombatPotions
{
    internal static void Validate(PredictorCombatPotionFacts facts, ImmutableArray<PredictorCardTrigger> triggers,
        ImmutableArray<PredictorTemporaryCardTrigger> temporaryTriggers = default)
    {
        if (temporaryTriggers.IsDefault) temporaryTriggers = [];
        if (facts.Changes.IsDefault || facts.NoAdditionalPotionChange && !facts.Changes.IsEmpty ||
            !facts.NoAdditionalPotionChange && facts.Changes.IsEmpty || facts.Changes.Any(c => c == null ||
            !Enum.IsDefined(c.Action) || c.BeforeOccurrence < 0 ||
            (c.Action == PredictorCombatPotionAction.LizardTailTriggered) != (c.RelicId != null) ||
            c.Action == PredictorCombatPotionAction.LizardTailTriggered && c.Potion != null ||
            c.BeforeCardId != null && c.BeforeTemporarySourceIndex != null ||
            c.BeforeCardId == null && c.BeforeTemporarySourceIndex == null && c.BeforeOccurrence != 0 ||
            c.BeforeCardId is { } id &&
                !triggers.Any(t => t.InstanceId == id && c.BeforeOccurrence < t.EffectiveTriggers) ||
            c.BeforeTemporarySourceIndex is { } sourceIndex &&
                (sourceIndex < 0 || sourceIndex >= temporaryTriggers.Length ||
                    c.BeforeOccurrence >= temporaryTriggers[sourceIndex].EffectiveTriggers)))
            throw new ArgumentException("PredictorCombatPotionFactsInvalid");
    }

    internal static PredictorState Apply(PredictorContext context, PredictorState state,
        IEnumerable<PredictorCombatPotionChange> changes, ImmutableArray<PredictorPotion>.Builder generated,
        ImmutableArray<PredictorPotionAttempt>.Builder attempts)
    {
        foreach (var change in changes)
        {
            if (change.Action == PredictorCombatPotionAction.LizardTailTriggered)
            {
                int relicIndex = state.Relics.FindIndex(r => r.Id == change.RelicId && !r.Melted &&
                    r.Key.Entry == "LIZARD_TAIL" && !r.LizardTailUsed);
                if (relicIndex < 0) throw new ArgumentException("PredictorLizardTailNotActiveUnused");
                // Fairy uses ShouldDie; Tail uses ShouldDieLate. A held Fairy
                // therefore prevents this same lethal event before Tail runs.
                if (state.PotionSlots.Any(p => p?.Key.Entry == "FAIRY_IN_A_BOTTLE"))
                    throw new ArgumentException("PredictorFairyPrecedesLizardTail");
                state = state with { Relics = state.Relics.SetItem(relicIndex,
                    state.Relics[relicIndex] with { LizardTailUsed = true }) };
                continue;
            }
            if (change.Action == PredictorCombatPotionAction.AdditionalAlchemize)
            {
                if (change.Potion != null) throw new ArgumentException("PredictorAlchemizeDoesNotSelectResult");
                var key = PredictorRewardGeneration.GeneratePotions(context, ref state, 1, PredictorStream.CombatPotionGeneration, inCombat: true)[0];
                var result = PredictorPotionEffects.Procure(state, key, 0, "DeclaredAlchemize");
                state = result.State; generated.Add(result.Attempt.Potion); attempts.Add(result.Attempt);
                continue;
            }
            var reference = change.Potion ?? throw new ArgumentException("PredictorCombatPotionInstanceMissing");
            if (reference.ExistingId.HasValue == reference.GeneratedOrdinal.HasValue)
                throw new ArgumentException("PredictorCombatPotionReferenceAmbiguous");
            long id = reference.ExistingId ?? (reference.GeneratedOrdinal is int ordinal && ordinal >= 0 && ordinal < generated.Count
                ? generated[ordinal].Id : throw new ArgumentException("PredictorGeneratedPotionOrdinalUnavailable"));
            int slot = state.PotionSlots.FindIndex(p => p?.Id == id);
            if (slot < 0) throw new ArgumentException("PredictorCombatPotionNotHeld");
            var potion = state.PotionSlots[slot]!;
            if (change.Action == PredictorCombatPotionAction.FairyTriggered && potion.Key.Entry != "FAIRY_IN_A_BOTTLE" ||
                change.Action == PredictorCombatPotionAction.Use && potion.Key.Entry == "FAIRY_IN_A_BOTTLE")
                throw new ArgumentException("PredictorPotionUseKindInvalid");
            if (change.Action == PredictorCombatPotionAction.Use && potion.Key.Entry == "ENTROPIC_BREW")
            {
                var result = PredictorSettlementEffects.UseEntropicBrew(state, context.PotionPool, slot);
                state = result.State; generated.AddRange(result.Generated);
            }
            else state = state with { PotionSlots = state.PotionSlots.SetItem(slot, null) };
            // Combat buffs, damage/targets, draw, and temporary card upgrades do
            // not become master Deck mutations. Persistent consequences created
            // by those plays belong to their separately declared source facts.
        }
        return state;
    }
}
