using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// New physical revisions own these quotes. Reference evidence is the completed
// 20261001 donor windows/matrix, NOT Local peak, old T prices or live survivors.
// Deliberately narrower than numerical support; an absent quote is not a gate.
internal static class NeowTransformationPricing
{
    private const string Evidence = "NT.OwnedWork.20261001.v1;Reference=RTX4060Laptop_D3D12;Source=T_LEAFY_PREGATE_PERFORMANCE_20260914#20261001;CompletedDispatchWindows;NoPeakFit;Model=ConservativeCoarse";

    private static bool Reference(TransformationAggregateNumericalPlan p, FamilyPhysicalQuoteRequest g) =>
        p.Closed && FamilyPhysicalQuote.AdmittedRequest(g) && FamilyPhysicalQuote.HasReferenceBackend() &&
        p.Neow is { DirectNestedVanilla111: true } n && n.Authority.PlayersCount == 1 &&
        n.Authority.AllCharacterCardPoolsUnlocked && n.Authority.Ascension == 10 &&
        n.Authority.BonesEligibleRelicIds.Length == 28 && n.Authority.EligibleCurseRelicIds.Length == 10;

    internal static FamilyPhysicalQuote? Joint(NeowTransformationComposite p, FamilyPhysicalQuoteRequest g)
    {
        var c = p.Numerical.Condition;
        if (!Reference(p.Numerical, g) || g.CompactInput || g.PrivateInput || g.PrivateOutput ||
            !p.Gpu.UsesStagedDense || c.Predicate == TransformationAggregatePredicate.RareCountAtLeast ||
            c.TargetMultiset.Count != 3 || p.Probability.ExpectedGpuDraws > 1.25)
            return null;
        // 2 x 10,468,982,784 roots: 1517.3125/1491.7664 ms dispatch.
        // Remove the common .15ms x 624 control allowance, round up to .14ns.
        // Observed module construction was 2.5–2.8s; keep it out of throughput.
        return new("N.Neow", "NT.Joint.Named3.20261001.v1", .14, NeowFamilyGpuExecutor.Capacity, 2800,
            Evidence + ";NAndTCoverage;DenseOnly;NamedThreeSlots;HostTransportOutside;Setup=2800ms",
            PublicTransportClass: "LargeResidentAppend32");
    }

    internal static FamilyPhysicalQuote? Transform(TransformationAggregateNumericalPlan p,
        TransformationAggregateProbability probability, TransformationAggregateGpuPlan physical, FamilyPhysicalQuoteRequest g)
    {
        if (!Reference(p, g) || !physical.FullTarget || physical.Capacity != TransformationAggregateGpuPlan.FullTargetCapacity) return null;
        var c = p.Condition;
        bool rare = c.Predicate == TransformationAggregatePredicate.RareCountAtLeast;
        bool measuredShape = rare ? c.OpportunityCount == 7 : c.OpportunityCount is 3 or 7;
        if (!measuredShape || !rare && probability.ExpectedGpuDraws > 1.25) return null;
        string shape = $"T.BonesTracked.Carry8.{(rare ? "AllRare" : "FullNamed")}.O{c.OpportunityCount}.20261001.v1";
        if (g.CompactInput)
        {
            if (g.MeanInputPopulation > 16387) return null;
            // The sparse native matrix measured completed windows, not a reliable
            // per-input slope. Charge the work as a fixed envelope (.40/.30ms,
            // including the coordinator's .15ms submit allowance), never as free.
            return new(TransformationAggregateFamily.Id, shape + ".SparseWindow", 0,
                TransformationAggregateGpuPlan.FullTargetCapacity, 250,
                Evidence + ";Compact<=16387;MatrixWindowEnvelope;NumericalWorkInFixedWindow;NoDenseToCompactSlope") {
                FixedWindowMilliseconds = g.PrivateOutput ? .15 : .25
            };
        }
        // Full completed-window numerical/emission bands after common submit
        // subtraction: named3 .162/.163, named7 .189/.192, rare7 .371/.372 ns.
        double ns = rare ? .38 : c.OpportunityCount == 3 ? .17 : .20;
        return new(TransformationAggregateFamily.Id, shape, ns,
            TransformationAggregateGpuPlan.FullTargetCapacity, 250,
            Evidence + ";DenseFullWindows;HostEdgesOutside;OtherOpportunityShapesUnpriced");
    }
}
