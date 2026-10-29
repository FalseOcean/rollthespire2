using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// T-owned work only. Reuses old T/independent Bones-T numerical anchors;
// no N+T joint-call timing, observed survivors or query probability fitting.
internal static class TransformationAggregateWorkEstimate
{
    internal readonly record struct Work(int Draws, int Streams, int PrefixDraws, int Targets, int BonesPool);

    internal static Work Describe(TransformationAggregateNumericalPlan p)
    {
        var groups = p.DrawGroups;
        return new(groups.Sum(g => g.Pools.Length), groups.Length, groups.Count(g => g.Prefix != 0),
            p.Condition.Predicate == TransformationAggregatePredicate.RareCountAtLeast ? 0 : p.Condition.TargetMultiset.Count,
            p.Neow?.Authority.BonesEligibleRelicIds.Length ?? 0);
    }

    private static double Reach(TransformationAggregateProbability p) =>
        double.IsFinite(p.IdentityProbability) && p.IdentityProbability is >= 0 and <= 1 ? p.IdentityProbability : 1;

    internal static FamilyPhysicalQuote? Cpu(TransformationAggregateNumericalPlan plan, TransformationAggregateProbability probability,
        Work work, FamilyPhysicalQuoteRequest g, int workers)
    {
        var c = plan.Condition;
        double opening = c.UsesNeow ? 65 : 0;
        if (c.IsBones) opening += 3d * work.BonesPool;
        // Missing auxiliary reach retains full work. A direct index is not a
        // linear walk through its source pool.
        double ns = 25 + opening + (c.TrialNondescript ? 20 : 0) + Reach(probability) *
            (4d * work.Draws + 4d * work.Streams + 2d * work.Targets) + (g.CompactInput ? 17 : 0);
        return FamilyCoarsePricing.Cpu(TransformationAggregateFamily.Id, "T.StreamingAggregate", ns, workers, g,
            Basis(work) + ";CpuAnchors=T.OwnedWork.20260914.v1;IdentityReachOrFullBound;NoPoolSizeMultiplierForDirectIndex");
    }

    internal static FamilyPhysicalQuote? Gpu(TransformationAggregateNumericalPlan plan, TransformationAggregateProbability probability,
        TransformationAggregateGpuPlan physical, Work work, FamilyPhysicalQuoteRequest g)
    {
        if (!physical.Supported) return null;
        var c = plan.Condition;
        double draws = double.IsFinite(probability.ExpectedGpuDraws) && probability.ExpectedGpuDraws >= 0
            ? Math.Min(work.Draws, probability.ExpectedGpuDraws) : work.Draws;
        string kind;
        double ns;
        if (physical.BonesOptimized)
        {
            kind = physical.FullTarget ? "BonesTracked.FullTarget" : "BonesTracked.OpeningFirst";
            // Independent T donor Dense named3/named7/rare7: .17/.20/.38ns.
            // Compact uses its own full ordinal reconstruction envelope.
            ns = (g.CompactInput ? .9 : .14) + .008 * Math.Max(0, work.Draws - 3) +
                (c.Predicate == TransformationAggregatePredicate.RareCountAtLeast ? .24 : .03) +
                .003 * Math.Max(0, work.BonesPool - 28);
            if (!physical.FullTarget) ns += .05 * draws;
        }
        else if (physical.FullTarget)
        {
            kind = "FullTarget";
            ns = (g.CompactInput ? .70 : .16) + .18 * Math.Max(0, draws - 1);
        }
        else
        {
            kind = "Generic";
            double opening = c.Opening switch {
                TransformationOpening.None => 0,
                TransformationOpening.LeafyPoultice => .10,
                TransformationOpening.NewLeaf => 1.10,
                _ => .063 * work.BonesPool
            };
            double activeWarp = 1 - Math.Pow(1 - Reach(probability), 32);
            double emission = probability.StageSurvival is >= 0 and <= 1 ? probability.StageSurvival.Value : 1;
            ns = .30 + opening + .05 * work.PrefixDraws + .22 * activeWarp * draws + .015 * work.Targets + .25 * emission;
        }
        // Do not invent a measured speed factor for the opt-in prefix body.
        // Its exact curves still take priority at the family entry point.
        if (physical.PrefixReuse) kind += ".Prefix";
        double integerFactor = physical.NumericalVariant == GpuNumericalVariant.IntegerRne ? 1.5 : 1;
        ns *= integerFactor;
        return FamilyCoarsePricing.Gpu(TransformationAggregateFamily.Id, "T." + kind, ns, physical.Capacity, g,
            Basis(work) + $";GpuAnchors=T.OwnedWork.20260914.v1+IndependentT.BonesTracked.20261001;NumericalVariant={physical.NumericalVariant};" +
            $"IntegerArithmeticFactorEstimated={integerFactor};NoAssumedPrefixSpeedRatio;UnknownDrawReachUsesFullWork;DeviceEmissionIncluded;NoHostTransportOrJointFit",
            setupMilliseconds: physical.BonesOptimized ? 250 : 200);
    }

    private static string Basis(Work w) =>
        $"T.WorkEstimate.20261028.v1;OwnCoverageOnly;Draws={w.Draws};Streams={w.Streams};PrefixDraws={w.PrefixDraws};Targets={w.Targets};BonesPool={w.BonesPool};NoNewMeasurement";
}
