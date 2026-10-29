using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// R-local operation summaries. Runtime pools supply loop lengths; target names
// and measured-key equality are not an estimate admission rule. The coefficients
// are retained 20260913 shuffle/tracked/Shop references, not new body timings.
internal static class RelicWorkEstimate
{
    internal static FamilyPhysicalQuote? Ordinary(ExactSearchExecutionRequest request, RelicFamilyPlan plan,
        FamilyPhysicalQuoteRequest geometry, FamilyExpectedFilteringCostProjection? expected,
        bool cpu, int workers = 1, bool referenceMatcher = false)
    {
        double shuffle = 0, tracked = 0;
        int last = Math.Min(plan.LastRequiredBucket, plan.Pool.BucketCount - 1);
        for (int bucket = 0; bucket <= last; bucket++)
        {
            double steps = Math.Max(0, plan.Pool.BucketLengths[bucket] - 1);
            shuffle += steps;
            int lane = plan.Pool.BucketKinds[bucket] - 1;
            if (plan.Pool.BucketScopes[bucket] == 1 && lane >= 0 && lane < 3)
                tracked += steps * plan.TrackedCountsByLane[lane];
        }
        double probes = plan.Predicates.Sum(p => (double)p.AnyCount + p.AllCount + p.BanCount);
        double shopDepth = plan.ShopPredicates.Select(p => (double)p.Count)
            .Concat(plan.Predicates.Where(p => p.Lane == 3).Select(p => (double)p.RangeValue)).DefaultIfEmpty(0).Max();
        double shopLength = Enumerable.Range(0, plan.Pool.BucketCount)
            .Where(i => plan.Pool.BucketScopes[i] == 1 && plan.Pool.BucketKinds[i] == 4)
            .Select(i => (double)plan.Pool.BucketLengths[i]).DefaultIfEmpty(0).Max();
        double eligible = Enumerable.Range(0, plan.Pool.BucketCount)
            .Where(i => plan.Pool.BucketScopes[i] == 1 && plan.Pool.BucketKinds[i] == 4)
            .Sum(i => Enumerable.Range(plan.Pool.BucketOffsets[i], plan.Pool.BucketLengths[i])
                .Count(at => (plan.Pool.EntryFlags[at] & 1) != 0));
        // Rejected/ineligible entries still consume their existing partial draw.
        double shopDraws = Math.Min(shopLength, shopDepth * shopLength / Math.Max(1, eligible));
        probes += plan.ShopPredicates.Sum(p => p.OrderMode == 0 ? (double)p.Count :
            (double)p.Count * p.TargetIds.Count(id => id != ushort.MaxValue));
        if (cpu && expected is { IsResolved: true } && shopDepth == 0)
        {
            shuffle = Math.Min(shuffle, expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.HistoricalNextInt).Sum(s => s.ExpectedContribution));
            tracked = Math.Min(tracked, expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.TrackedPositionUpdateAttempt).Sum(s => s.ExpectedContribution));
        }
        double ns = cpu ? 243 + .93 * shuffle + 1.31 * tracked + 15 * shopDraws + 2 * probes :
            (geometry.CompactInput ? 1.55 + .00085 * shuffle + .01175 * tracked : .98 + .00254 * shuffle + .0101 * tracked) +
            .05 * shopDraws + .02 * probes;
        if (plan.AlwaysReject) ns = cpu ? 22 : .12;
        if (referenceMatcher) ns *= 2;
        string workClass = shopDepth == 0 ? "R.Tracked" : plan.Predicates.Any(p => p.Lane < 3) ? "R.TrackedShop" : "R.ShopPrefix";
        string basis = FormattableString.Invariant($"R.FamilyCostClosure.20260913;Shuffle={shuffle:G9};TrackedUpdates={tracked:G9};ShopDraws={shopDraws:G9};Probes={probes:G9};RuntimeBucketLengths;OneSharedTraversal;FullPathGpuEnvelope;ReferenceMatcher={referenceMatcher};AlwaysReject={plan.AlwaysReject}");
        if (cpu) return FamilyCoarsePricing.Cpu("R.Relic", workClass, ns, workers, geometry, basis);
        var quote = FamilyCoarsePricing.Gpu("R.Relic", workClass, ns, RelicFamilyGpuExecutor.Capacity, geometry, basis, 390);
        // Retain the existing compact underfill envelope separately from the
        // per-input loop estimate. Submission/materialization remain downstream.
        return quote is not null && geometry.CompactInput && geometry.MeanInputPopulation is > 0 and < 262144
            ? quote with { FixedWindowMilliseconds = .25, Evidence = quote.Evidence + ";CompactMatrix.20260919;UnderfillEnvelopeMs=0.25;NotNewMeasurement" }
            : quote;
    }

    internal static FamilyPhysicalQuote? Capsule(ExactSearchExecutionRequest request, NeowReplayPlan route,
        RelicFamilyPool pool, RelicFullGpuPlan? gpuPlan, FamilyPhysicalQuoteRequest geometry,
        bool cpu, int workers = 1, bool compiledMatcher = true)
    {
        var conditions = route.Filter.StructuredNeowEffects.Where(NeowReplayPlan.IsCapsule).ToArray();
        int targets = Math.Max(1, conditions.SelectMany(c => c.OutputKeys).Distinct().Count());
        double shuffle = pool.BucketLengths.Sum(n => (double)Math.Max(0, n - 1));
        double tracked = Enumerable.Range(0, pool.BucketCount).Where(i => pool.BucketScopes[i] == 1 && pool.BucketKinds[i] is >= 1 and <= 3)
            .Sum(i => (double)Math.Max(0, pool.BucketLengths[i] - 1)) * targets / 3;
        bool sequence = request.Evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty) ||
            request.Evaluation.RelicShopSequenceConditions.Any(c => !c.IsEmpty);
        int sourceCount = route.Bones ? 2 : 1;
        bool large = route.Selected == Beta110FastRelicCatalog.LargeCapsule ||
            conditions.Any(c => c.SourceRelicKey.Entry == "LARGE_CAPSULE");
        bool fullBag = gpuPlan?.GenericReplay == true || !compiledMatcher;
        var companion = cpu && !compiledMatcher ? NeowWorkEstimate.CapsuleCompanion(route) : default;
        double ns;
        if (cpu)
        {
            // Old direct SmallC / LargeUR anchors were conditional input samples.
            // Reuse their scale as a declared estimate, never their admission or
            // measured status for an unconditional, changed or different target.
            double entry = route.Bones ? 268 : large ? 130 : 268;
            ns = entry * Math.Max(.25, shuffle / 201) + Math.Max(0, targets - (large ? 2 : 1)) * 8;
            if (sequence) ns += 80 + .93 * shuffle;
            if (fullBag) ns += 120 + pool.BucketLengths.Sum(n => (double)n);
            ns += companion.Routes * (12 * companion.Draws + .3 * companion.PoolVisits);
        }
        else
        {
            ns = (route.Bones || !large ? 1.6 : .28) +
                (geometry.CompactInput ? 1.55 + .00085 * shuffle + .01175 * tracked : .98 + .00254 * shuffle + .0101 * tracked);
            if (sequence) ns += .00254 * shuffle + .02 * request.Evaluation.RelicSequenceConditions.Count;
            if (fullBag) ns += .008 * pool.BucketLengths.Sum(n => (double)n);
        }
        string basis = FormattableString.Invariant($"R.CapsuleFoundation.20260913;CpuDirectSmallC268-LargeUR130.20261013;SourceCount={sourceCount};Bones={route.Bones};Large={large};Targets={targets};Shuffle={shuffle:G9};TrackedUpdates={tracked:G9};FullBag={fullBag};WithSequence={sequence};CompanionReplayDraws={companion.Draws:G9};CompanionPoolVisits={companion.PoolVisits:G9};CompanionRoutes={companion.Routes};ReferenceCompanionOnly;NoAdditionalNFilterCharge;NumericalVariant={gpuPlan?.NumericalVariant};SameOperationCoefficientsNotVariantMeasurement;NoSourceSurvivalSelector;NotConditionalAnchorMeasurement");
        return cpu ? FamilyCoarsePricing.Cpu("R.Relic", fullBag ? "R.CapsuleReference" : "R.CapsuleTracked", ns, workers, geometry, basis) :
            FamilyCoarsePricing.Gpu("R.Relic", fullBag ? "R.CapsuleFullBag" : "R.CapsuleTracked", ns,
                RelicFamilyGpuExecutor.Capacity, geometry, basis, 750);
    }
}
