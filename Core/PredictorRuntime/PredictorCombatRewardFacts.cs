using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// Budget correction is separate. These are encounter reward facts, not a gold
// transaction log or a reconstruction of the unmodelled combat balance.
internal sealed record PredictorGremlinGoldFacts(bool FatGremlinEscaped, bool GoldWasStolen,
    int? RecoveredGold)
{
    // An explicit prediction disposition. Legacy numeric rewards keep their
    // existing additive semantics unless the caller selects this closed loop.
    public bool RecoverAllStolenGold { get; init; }
    // Null preserves pre-ledger declarations from old saved runs. New numeric
    // declarations account for this loss before offering their recovery reward.
    public int? StolenGoldLoss { get; init; }
}
internal enum PredictorCombatExtraRewardSource { Hunt, StolenCard, Heist }
internal sealed record PredictorCombatExtraRewardRef(PredictorCombatExtraRewardSource Source, int Index);

internal static class PredictorCombatRewardFacts
{
    internal static bool NeedsGremlinFacts(PredictorEncounterDefinition encounter) =>
        encounter.Key.Entry == "GREMLIN_MERC_NORMAL";

    internal static bool ValidGremlin(PredictorEncounterDefinition encounter, PredictorGremlinGoldFacts? facts) =>
        NeedsGremlinFacts(encounter)
            ? facts != null && facts.RecoveredGold is not < 0 &&
                !(facts.FatGremlinEscaped && facts.RecoveredGold != null) &&
                !(facts.RecoveredGold > 0 && !facts.GoldWasStolen) &&
                !(facts.RecoverAllStolenGold && (facts.FatGremlinEscaped || !facts.GoldWasStolen ||
                    facts.RecoveredGold != null || facts.StolenGoldLoss != null)) &&
                (facts.StolenGoldLoss == null || facts.GoldWasStolen && facts.StolenGoldLoss is >= 0)
            : facts == null;

    internal static bool HasHeistReward(PredictorGremlinGoldFacts? facts) =>
        facts?.RecoveredGold != null || facts?.RecoverAllStolenGold == true;

    internal static bool CanBalanceRecoveredGold(PredictorContext? context, PredictorState state) =>
        RecoveredGoldBalanceBlockers(context, state).IsEmpty;

    internal static ImmutableArray<string> RecoveredGoldBalanceBlockers(PredictorContext? context, PredictorState state)
    {
        var blockers = ImmutableArray.CreateBuilder<string>();
        // TakeGold applies Bowler/Ectoplasm and invokes DragonFruit; Midas
        // doubles original GoldReward payloads. Seal clamps a separate deduction
        // before rewards are taken. None can be erased as a net-zero gain/loss.
        foreach (var entry in state.Relics.Where(r => !r.Melted && r.Key.Entry is
            "BOWLER_HAT" or "ECTOPLASM" or "DRAGON_FRUIT" or "SEAL_OF_GOLD").Select(r => r.Key.Entry).Distinct())
            blockers.Add(entry);
        if (context == null) blockers.Add("CONTEXT_UNAVAILABLE");
        else if (context.HasRunModifier("MIDAS")) blockers.Add("MIDAS");
        return blockers.ToImmutable();
    }

    internal static float GoldProportion(PredictorGremlinGoldFacts facts) =>
        facts.FatGremlinEscaped ? facts.GoldWasStolen ? 0f : .5f : 1f;

    // The three gameplay sources append rewards while combat is in progress.
    // Royalties/Grimoire are added later by AfterCombatEnd and cannot be moved
    // before them by a user-supplied ordering. Automatic Hunt order is the
    // approved entry-deck order, not a claim about simulated card plays.
    internal static ImmutableArray<PredictorRewardPlan> Order(
        ImmutableArray<PredictorRewardPlan> cardConsequences,
        IReadOnlyList<PredictorRewardPlan> stolen,
        PredictorGremlinGoldFacts? gremlin,
        ImmutableArray<PredictorCombatExtraRewardRef> order)
    {
        var entries = new List<(PredictorCombatExtraRewardRef Ref, PredictorRewardPlan Plan)>();
        int index = 0;
        foreach (var plan in cardConsequences.Where(p => p.Kind == PredictorRewardKind.Card))
            entries.Add((new(PredictorCombatExtraRewardSource.Hunt, index++), plan));
        for (int i = 0; i < stolen.Count; i++)
            entries.Add((new(PredictorCombatExtraRewardSource.StolenCard, i), stolen[i]));
        if (gremlin?.RecoveredGold is { } amount)
            entries.Add((new(PredictorCombatExtraRewardSource.Heist, 0),
                new(PredictorRewardKind.Gold, amount, amount, PrefilledGold: true, WasGoldStolenBack: true)));
        else if (gremlin?.RecoverAllStolenGold == true)
            entries.Add((new(PredictorCombatExtraRewardSource.Heist, 0),
                new(PredictorRewardKind.Gold, PrefilledGold: true, WasGoldStolenBack: true,
                    GoldAmountUnspecified: true, GoldNetRestored: true)));
        if (order.IsDefault) throw new ArgumentException("PredictorCombatRewardOrderInvalid");
        if (order.IsEmpty)
        {
            if (entries.Select(e => e.Ref.Source).Distinct().Count() > 1)
                throw new ArgumentException("PredictorCombatRewardOrderRequired");
        }
        else
        {
            if (order.Length != entries.Count || order.Any(r => r == null) || order.Distinct().Count() != order.Length ||
                order.Any(r => !entries.Any(e => e.Ref == r)))
                throw new ArgumentException("PredictorCombatRewardOrderInvalid");
            entries = order.Select(r => entries.Single(e => e.Ref == r)).ToList();
        }
        return entries.Select(e => e.Plan).Concat(cardConsequences.Where(p => p.Kind != PredictorRewardKind.Card)).ToImmutableArray();
    }
}

internal sealed partial class PredictorRun
{
    internal (ImmutableArray<PredictorPotion> Potions, ImmutableArray<PredictorPotionAttempt> Attempts,
        ImmutableArray<PredictorHealthConsequence> Health) PreviewCombatConsequences(PredictorCombatFacts facts)
    {
        var entry = _eventCombatEntry ?? _combatEntry;
        if (Phase != PredictorPhase.CombatFacts || entry == null)
            throw new InvalidOperationException("PredictorNotAtCombatBoundary");
        var result = PredictorCombatConsequences.Apply(Context, Working, entry.Kind, facts);
        return (result.Potions, result.PotionAttempts, result.Health);
    }

    // Once the player has supplied source outcomes, UI asks Runtime for the
    // exact reward identities to order. This evaluates copied state only; it
    // neither consumes the working streams nor accepts a combat fact twice.
    internal ImmutableArray<PredictorCombatExtraRewardRef> DescribeCombatRewardOrder(PredictorCombatFacts facts)
    {
        var entry = _eventCombatEntry ?? _combatEntry;
        if (Phase != PredictorPhase.CombatFacts || entry == null)
            throw new InvalidOperationException("PredictorNotAtCombatBoundary");
        var consequences = PredictorCombatConsequences.Apply(Context, Working, entry.Kind, facts);
        if (facts.GremlinGold?.StolenGoldLoss is { } loss && loss > consequences.State.GoldBudget)
            throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.StolenGoldLossExceedsBudget,
                "PredictorStolenGoldLossExceedsBudget");
        var result = ImmutableArray.CreateBuilder<PredictorCombatExtraRewardRef>();
        int count = consequences.Extras.Count(p => p.Kind == PredictorRewardKind.Card);
        for (int i = 0; i < count; i++) result.Add(new(PredictorCombatExtraRewardSource.Hunt, i));
        count = facts.StolenCards?.Cards.Count(c => c.ReturnedAsReward) ?? 0;
        for (int i = 0; i < count; i++) result.Add(new(PredictorCombatExtraRewardSource.StolenCard, i));
        if (PredictorCombatRewardFacts.HasHeistReward(facts.GremlinGold)) result.Add(new(PredictorCombatExtraRewardSource.Heist, 0));
        return result.ToImmutable();
    }
}
