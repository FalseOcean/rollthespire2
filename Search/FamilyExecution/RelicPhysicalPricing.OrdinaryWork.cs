using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class RelicPhysicalPricing
{
    // Ordinary tracked-position body only. The existing finite-sequence ledger
    // owns bucket reach; these independently measured CPU/GPU coefficients map
    // that R-owned work to reference time. Shop and Capsule are different bodies.
    internal static FamilyPhysicalQuote? QuoteOrdinaryModel(ExactSearchExecutionRequest request,
        RelicFamilyPlan plan, FamilyExpectedFilteringCostProjection expected,
        FamilyPhysicalQuoteRequest geometry, bool cpu)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            plan.AlwaysReject || !expected.IsResolved || plan.ShopPredicates.Length != 0 ||
            plan.Predicates.Length is < 1 or > 5 || plan.Predicates.Any(p => p.Lane > 2) ||
            plan.TrackedInitialPositions.Length is < 1 or > 12 ||
            !plan.Pool.BucketLengths.SequenceEqual(new[] {30,25,35,25,1,2,32,26,38,26})) return null;
        if (cpu ? geometry.PrivateInput || geometry.PrivateOutput || geometry.CompactInput ||
                  geometry.MeanInputPopulation < 4096
                : !FamilyPhysicalQuote.HasReferenceBackend() || geometry.MeanInputPopulation < 262144)
            return null;

        double shuffle = 0, tracked = 0, predicates = 0;
        foreach (var segment in expected.Segments)
        {
            if (segment.Operation == FamilyAnalyticalOperation.HistoricalNextInt) shuffle += segment.ExpectedContribution;
            if (segment.Operation == FamilyAnalyticalOperation.TrackedPositionUpdateAttempt) tracked += segment.ExpectedContribution;
            if (segment.Operation == FamilyAnalyticalOperation.PredicateProbe) predicates += segment.ExpectedContribution;
        }
        // Bounded interpolation region, including a second-lane rejection and
        // high-output Ban holdout. No arbitrary-target or tiny-input extrapolation.
        if (shuffle is < 143 or > 205 || tracked is < 25 or > 400 || predicates > 16 ||
            expected.FinalSurvival is not >= 0 or > .7) return null;
        bool coarse = tracked > 150 || predicates > 5 || plan.TrackedInitialPositions.Length > 6 || plan.Predicates.Length > 2;
        if (coarse && !cpu)
        {
            // With more tracked positions, scalar rejection reach underprices
            // active GPU groups. Reuse the same loop coefficients with full-path
            // work as a conservative envelope, not another fitted target table.
            shuffle = expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.HistoricalNextInt).Sum(s => s.FullWorkUnits);
            tracked = expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.TrackedPositionUpdateAttempt).Sum(s => s.FullWorkUnits);
            if (tracked > 400) return null;
        }
        double ns = cpu ? 243 + .93 * shuffle + 1.31 * tracked :
            geometry.CompactInput ? 1.55 + .00085 * shuffle + .01175 * tracked :
                .98 + .00254 * shuffle + .0101 * tracked;
        if (coarse) ns += Math.Max(0, predicates - 5) * (cpu ? 3 : .02);
        // GPU reference fit excludes the existing .2ms submit/control edge.
        // CPU measurements already own hash/partition/ordered output; no second edge.
        return new("R.Relic", "R.OrdinaryWork.20260913.v1." + (cpu ? "P1" : geometry.CompactInput ? "Compact" : "Dense"),
            ns, cpu ? 65536 : RelicFamilyGpuExecutor.Capacity, cpu ? null : 390,
            "FamilyCostClosure.20260913;Model=BoundedCoarse;metric=ns/ActualStageInput;Work=ExpectedShuffle+TrackedUpdates;" +
            "MeasuredSpecializationFirst;Reference=CurrentMain;ObservedSurvivalUsed=false;" +
            (coarse ? "ConservativeCoarse;BoundedTrackedLoopExtension;" : "ValidatedWorkRegion;") +
            (cpu ? "Boundary=CpuCanonicalAbi1;Workers=1;MinInput=4096" : "Boundary=NumericalDeviceEmission;MinInput=262144;SubmitAndHostTransportExcluded"),
            OutputElementBytes: cpu ? 8 : 4, OutputAlreadyOrdered: cpu,
            PublicTransportClass: cpu ? "CpuOrderedAbi1" : "Counted32");
    }
}
