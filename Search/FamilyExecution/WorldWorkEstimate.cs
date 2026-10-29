namespace RolltheSpire2.Search.FamilyExecution;

internal static class WorldWorkEstimate
{
    private static (bool Variant, double Work, int Comparisons) Work(WorldFamilyReplay replay)
    {
        var p = replay.Plan;
        if (p.MaxRequiredAct == 0)
            return (true, p.ActSelectionGroups.Count(g => g.EligibleActIds.Length > 1), p.PredicateCount);
        double work = replay.BucketLengths.Sum(n => Math.Max(0, (double)n - 1)) + Math.Max(0, p.SharedAncients.Length - 1);
        // One selected variant per Act, not the sum of every possible variant.
        foreach (var act in p.Acts.Where(a => a.Act <= p.MaxRequiredAct).GroupBy(a => a.Act))
            work += act.Max(a => {
                // Materialized predicates shuffle candidates; other observations
                // still advance the same stream through the eligible event pool.
                int eventCount = a.HasEventPredicate ? a.EventCandidates.Length : a.EligibleEventCount;
                double events = Math.Max(0, eventCount - 1);
                bool stopsAtEvents = a.Act == p.MaxRequiredAct && !p.RequiresSecondBoss &&
                    !a.HasBossPredicate && !a.HasAncientIdentityPredicate;
                double queues = stopsAtEvents ? 0 : a.WeakEncounterSlots + a.RegularEncounterSlots + a.EliteEncounterSlots +
                    a.WeakEncounters.Length + a.RegularEncounters.Length + a.EliteEncounters.Length +
                    .25 * a.EncounterConflictSourceOrdinals.Length;
                return events + queues;
            });
        return (false, work, p.PredicateCount);
    }

    internal static FamilyPhysicalQuote? Gpu(WorldFamilyReplay replay, WorldFamilyGpuPlan plan, FamilyPhysicalQuoteRequest g)
    {
        var w = Work(replay);
        double ns = w.Variant ? .62 + .08 * Math.Max(0, w.Work - 1) : 1.6 + .025 * w.Work + .04 * w.Comparisons;
        return FamilyCoarsePricing.Gpu("W.World", w.Variant ? "W.VariantChoice" : "W.ProgressionQueues",
            ns, checked(plan.Capacity * plan.GroupingK), g,
            $"W.Variant_062AndProgression7To12nsEnvelopes;OwnedLoopWork={w.Work:G17};Comparisons={w.Comparisons};" +
            "ActualShuffleAndQueueLengths;OneVariantPerAct;EventStopPreserved;NoTargetOrScratchExactKey", 190);
    }

    internal static FamilyPhysicalQuote? Cpu(WorldFamilyReplay replay, int workers, FamilyPhysicalQuoteRequest g,
        int capacity = 65536)
    {
        var w = Work(replay);
        double ns = w.Variant ? 55 + 8 * Math.Max(0, w.Work - 1) : 80 + 8 * w.Work + 8 * w.Comparisons;
        if (g.CompactInput) ns += 17;
        return FamilyCoarsePricing.Cpu("W.World", w.Variant ? "W.VariantChoice" : "W.ProgressionQueues",
            ns, workers, g, $"W.CpuVariant55AndProgression2700_3750Envelopes;OwnedLoopWork={w.Work:G17};" +
            $"Comparisons={w.Comparisons};ActualShuffleAndQueueLengths;OneVariantPerAct;EventStopPreserved", capacity);
    }
}
