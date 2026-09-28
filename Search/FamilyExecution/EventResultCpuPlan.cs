using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>E-private compiled color membership; all remaining operators use the existing numerical donor.</summary>
internal sealed class EventResultCpuPlan(EventResultFamilyPlan plan, uint allowedRemovalMask,
    EventResultSearchCondition[] remaining)
{
    internal uint AllowedRemovalMask => allowedRemovalMask;
    internal EventResultSearchCondition[] Remaining => remaining;

    internal static EventResultCpuPlan? TryCompile(ExactSearchExecutionRequest request, EventResultFamilyPlan plan)
    {
        if (plan.HasMorphic || !request.Authority.CanUseCurrentModel || !plan.GpuSupported ||
            !plan.Authority.ColorfulPoolAuthorityExact ||
            !plan.Conditions.Any(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor)) return null;
        var candidates = Beta111EventResultCatalog.ColorfulCharacterOrder.Where(c =>
            c != plan.Authority.OwnerCharacterKey && plan.Authority.UnlockedCharacterCardPoolKeys.Contains(c)).ToArray();
        // Current fully-unlocked vanilla removes one of four candidates. Other
        // pool geometries keep the existing numerical/reference implementation.
        if (candidates.Length != 4) return null;
        uint mask = 15;
        foreach (var c in plan.Conditions.Where(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor))
        {
            int index = Array.IndexOf(candidates, c.TargetKey);
            if (index < 0) { mask = 0; break; }
            mask &= ~(1u << index);
        }
        return new(plan, mask, plan.Conditions.Where(c => c.Kind != EventResultConditionKind.ColorfulPhilosophersOfferedColor).ToArray());
    }

    internal bool Matches(ulong root)
    {
        var rng = EventResultNumericPredicate.ColorfulRng(root, plan.PlayerSlot);
        if ((allowedRemovalMask & (1u << rng.NextInt(4))) == 0) return false;
        return remaining.Length == 0 || !EventResultNumericPredicate.RejectsEventResultForFamily(
            root, remaining, plan.Authority, plan.PlayerSlot);
    }
}
