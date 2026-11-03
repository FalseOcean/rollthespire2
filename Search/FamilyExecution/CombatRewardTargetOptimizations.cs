using System.Numerics;
using System.Text;

namespace RolltheSpire2.Search.FamilyExecution;

// C-local adaptation of target-aware/lazy work from shion0528 (Egoistic今宵)'s resource.17
// ideas. This is new C code, not an import of its N implementation. James Zhou's
// previously integrated execution/transport foundation remains separately owned.
internal static class CombatRewardTargetOptimizations
{
    internal static (bool RareOnly, bool Assignment) Capture() =>
        Environment.GetEnvironmentVariable("RT2_C_TARGET_EXPERIMENT") switch {
            null or "" or "default" => (true, true),
            "off" => (false, false),
            "rare-only" => (true, false),
            "assignment" => (false, true),
            "all" => (true, true),
            var mode => throw new InvalidOperationException("C.UnknownTargetExperiment:" + mode)
        };

    internal static Beta110GpuCombatRewardHotLoopPlan Apply(Beta110GpuCombatRewardHotLoopPlan hot,
        Beta110CombatRewardFastPlan plan, Beta110FastEffectCatalog catalog, bool rareOnly, bool assignment)
    {
        if (!hot.Enabled || !hot.DirectCardOrdinalSelectionEligible) return hot;
        var pool = catalog.CombatRewardCardPool;
        // Direct selection already proves >=3 entries in each unique, disjoint
        // rarity pool. Invalid IDs must also be excluded: elision/early return
        // must never hide the later numeric fault of a selected invalid card.
        if (pool.Common.Concat(pool.Uncommon).Concat(pool.Rare).Contains(Beta110FastDenseId.Invalid)) return hot;
        var targets = plan.Predicates.SelectMany(p => p.CardAny.Concat(p.CardAll).Concat(p.CardBan))
            .Concat(plan.CardAssignmentTargets).ToArray();
        bool rareEligible = rareOnly && targets.Length > 0 && targets.All(id => pool.Rare.Contains(id));
        // Returning early skips a suffix, so prove that every possible remaining
        // card/potion branch is fault-free on this hot three-card surface. Opening
        // replay has already executed before any gate and retains its own faults.
        bool assignmentEligible = assignment && plan.HasDistinctBattleAssignments &&
            plan.CardAssignmentTargets.Length <= 6 && plan.PotionAssignmentTargets.Length <= 6 &&
            catalog.CombatRewardPotionPool.HasEveryRarity;
        return hot with {
            RareOnlyIdentityElisionEnabled = rareEligible,
            AssignmentEarlyRejectionEnabled = assignmentEligible,
            AssignmentFeasibilitySource = assignmentEligible ? CompileAssignmentFeasibility(plan, hot.MaximumBattleOrdinal) : ""
        };
    }

    // Each set bit denotes an already reachable assignment subset. One future
    // battle can add at most one target; this is an optimistic capacity bound,
    // independent of identities/probabilities. Existing DP still owns matching.
    internal static ulong FeasibleAssignmentStates(int count, int remaining)
    {
        if (count is < 0 or > 6 || remaining < 0) throw new ArgumentOutOfRangeException();
        ulong allowed = 0;
        for (int subset = 0; subset < (1 << count); subset++)
            if (BitOperations.PopCount((uint)subset) + remaining >= count) allowed |= 1UL << subset;
        return allowed;
    }

    private static string CompileAssignmentFeasibility(Beta110CombatRewardFastPlan plan, int horizon)
    {
        var source = new StringBuilder("bool cr_hot_assignment_feasible(uint64_t cards,uint64_t potions,uint battle){switch(battle){\n");
        for (int battle = 1; battle <= horizon; battle++)
        {
            ulong cards = FeasibleAssignmentStates(plan.CardAssignmentTargets.Length, Math.Max(0, plan.CardAssignmentWindow - battle));
            ulong potions = FeasibleAssignmentStates(plan.PotionAssignmentTargets.Length, Math.Max(0, plan.PotionAssignmentWindow - battle));
            source.Append("case ").Append(battle).Append("u:return (cards&uint64_t(0x").Append(cards.ToString("X"))
                .Append("ul))!=uint64_t(0u)&&(potions&uint64_t(0x").Append(potions.ToString("X"))
                .Append("ul))!=uint64_t(0u);\n");
        }
        return source.Append("}return true;}\n").ToString();
    }
}
