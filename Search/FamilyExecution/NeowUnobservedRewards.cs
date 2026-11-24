namespace RolltheSpire2.Search.FamilyExecution;

// Reuse the source-owned successful-draw proof. Unobserved card identities
// never leave this operator; only the original route-local RNG arrivals do.
internal static class NeowUnobservedRewards
{
    internal const string Revision = ".UnobservedRewardsConsumption.20261114.v1";
    internal const int Unavailable = int.MinValue;

    internal static int[] Compile(NeowReplayPlan plan)
    {
        if (!plan.Bones || !plan.NeedsRewards || !plan.AuditedModern113IdentityReplay ||
            plan.Authority.PlayersCount != 1 ||
            string.Equals(Environment.GetEnvironmentVariable("RT2_N_UNOBSERVED_REWARDS"), "off", StringComparison.OrdinalIgnoreCase))
            return [];
        int[] rules = Enumerable.Repeat(Unavailable, 30).ToArray();
        foreach (byte source in new byte[] {
            Beta110FastRelicCatalog.ArcaneScroll, Beta110FastRelicCatalog.HeftyTablet,
            Beta110FastRelicCatalog.LeadPaperweight, Beta110FastRelicCatalog.LostCoffer,
            Beta110FastRelicCatalog.Kaleidoscope, Beta110FastRelicCatalog.ScrollBoxes })
        {
            if (plan.StructuredConditions.Any(condition => condition.SourceRelicId == source)) continue;
            if (RelicOpeningPrefixPlan.TryProveCompanionRewardsDraws(plan, source, out int draws))
                rules[source] = draws;
        }
        return rules.Any(rule => rule != Unavailable) ? rules : [];
    }

    internal static bool TryAdvance(byte source, NeowReplayPlan plan,
        ref Beta110FastRng rewards, ref Beta110FastRng nonCombat)
    {
        if (source >= plan.UnobservedRewardsDraws.Length || plan.UnobservedRewardsDraws[source] == Unavailable)
            return false;
        foreach (var condition in plan.StructuredConditions)
            if (condition.SourceRelicId == source) return false;
        RelicOpeningPrefixPlan.AdvanceObservedRewards(plan.UnobservedRewardsDraws[source], ref rewards);
        if (source == Beta110FastRelicCatalog.Kaleidoscope)
        {
            nonCombat.ConsumeShuffleSeed();
            nonCombat.ConsumeShuffleSeed();
        }
        return true;
    }
}
