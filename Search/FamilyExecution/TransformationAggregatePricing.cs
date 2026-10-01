using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class TransformationAggregatePricing
{
    private const string Evidence = "T.OwnedWork.20260914.v1;Reference=i9-13900HX_P1+RTX4060Laptop_D3D12;Source=TRANSFORMATION_COMPOSITE_NUMERICAL_CLOSURE_20260914;BoundedMeasuredWorkModel;NoNPlusEPrices;NoObservedSurvivalFit";

    internal static FamilyPhysicalQuote? Cpu(TransformationAggregateNumericalPlan plan, TransformationAggregateProbability probability, FamilyPhysicalQuoteRequest g)
    {
        if (!plan.Closed || !FamilyPhysicalQuote.AdmittedRequest(g) || g.PrivateInput || g.PrivateOutput) return null;
        var c = plan.Condition;
        // CPU v1 observes the entire opening offer (and Bones pair when requested)
        // before computing every selected output. GPU early rejection is NOT priced here.
        double opening = c.UsesNeow ? 65 : 0;
        if (c.Opening is TransformationOpening.BonesLeafyNewLeaf or TransformationOpening.BonesLeafyOther)
            opening += 3 * plan.Neow!.Authority.BonesEligibleRelicIds.Length;
        double reached = probability.IdentityProbability;
        double ns = 25 + opening + (c.TrialNondescript ? 20 : 0) + reached * (4 * c.OpportunityCount + 4 * plan.DrawGroups.Length +
            (c.Predicate != TransformationAggregatePredicate.RareCountAtLeast ? 2 * c.TargetMultiset.Count : 0));
        if (g.CompactInput) ns += 17; // canonical compact hash / index traversal
        return new(TransformationAggregateFamily.Id, Shape(plan, "Cpu"), ns, 65536, 0,
            Evidence + MixedEvidence(c) + ";ClosedNewSources=SymbioteTrialBonesLeafyCompanion;TrialCaseDrawChargedOnce;CpuCanonicalIncludesHashOrderedAbi1;AdditionalExecutorSetup=0;GlobalCpuCalibrationEligible",
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1") { FixedWindowMilliseconds = .002 };
    }

    internal static FamilyPhysicalQuote? Gpu(TransformationAggregateNumericalPlan plan, TransformationAggregateProbability probability,
        TransformationAggregateGpuPlan physical, FamilyPhysicalQuoteRequest g)
    {
        if (!plan.Closed || !FamilyPhysicalQuote.AdmittedRequest(g) || !FamilyPhysicalQuote.HasReferenceBackend()) return null;
        var c = plan.Condition;
        if (physical.BonesOptimized) return NeowTransformationPricing.Transform(plan, probability, physical, g);
        if (physical.FullTarget)
        {
            // Bounded full-slot donor: dense Carry8 hashing; every unused-target
            // miss rejects. Compact still decodes each incoming ordinal. No hit
            // probability or same-run population fitting enters this quote.
            double nsFull = (g.CompactInput ? .70 : .16) + .18 * Math.Max(0, probability.ExpectedGpuDraws - 1);
            return new(TransformationAggregateFamily.Id, Shape(plan,"Gpu")+".FullTargetCarry8",nsFull,
                TransformationAggregateGpuPlan.Experiment=="full1m"?TransformationAggregateGpuPlan.WindowCapacity:TransformationAggregateGpuPlan.FullTargetCapacity,200,
                "T.FullTarget.OwnedWork.20260914.v1;BoundedMeasured;RTX4060Laptop_D3D12;DenseCarry8;CompactFullDecode;ExpectedTargetDraws;NoObservedSurvivalFit");
        }
        double opening = c.Opening switch {
            TransformationOpening.None => 0,
            TransformationOpening.LeafyPoultice => .10,
            TransformationOpening.NewLeaf => 1.10,
            _ => .063 * plan.Neow!.Authority.BonesEligibleRelicIds.Length
        };
        // A warp with any surviving lane executes the bounded transform loop.
        // This coarse correction avoids claiming per-thread early rejects remove
        // proportional wall time on SIMT hardware. Draw reach comes from the model.
        double activeWarp = 1 - Math.Pow(1 - probability.IdentityProbability, 32);
        double ns = .30 + opening + (c.TrialNondescript ? .05 : 0) + .22 * activeWarp * probability.ExpectedGpuDraws +
            .015 * c.TargetMultiset.Count + .25 * (probability.StageSurvival ?? 0);
        return new(TransformationAggregateFamily.Id, Shape(plan, "Gpu"), ns, TransformationAggregateGpuPlan.WindowCapacity, 200,
            Evidence + MixedEvidence(c) + ";ClosedNewSources=SymbioteTrialBonesLeafyCompanion;TrialCaseDrawChargedOnce;GPU=DispatchNumericalIncludingEmission;SubmitHeaderReadbackQuotedOutside;ModuleSetupBounded=200ms;ExpectedEarlyRejectDraws=" +
            probability.ExpectedGpuDraws.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string MixedEvidence(TransformationAggregateCondition c) => c.RequiresRareRemainder
        ? ";RemainingRareQuote=ExistingWorkModelExtrapolation;NotSeparatelyCalibrated" : "";

    private static string Shape(TransformationAggregateNumericalPlan plan, string device) =>
        $"T.{device}.InitialBasicsAggregate.{plan.Condition.Opening}.O{plan.Condition.OpportunityCount}.G{plan.DrawGroups.Length}.{plan.Condition.Predicate}.K{plan.Condition.MinimumRareCount}.M{plan.Condition.TargetMultiset.Count}.v1";
}
