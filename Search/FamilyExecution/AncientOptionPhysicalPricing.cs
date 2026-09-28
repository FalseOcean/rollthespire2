using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Compiled numerical generator geometry, not an Ancient/option target lookup.
internal static class AncientOptionPhysicalPricing
{
    internal static bool IsSingleOptionWork(AncientOptionFamilyPlan plan) =>
        plan.SingleOptionPlan is { Generator: WorldFastAncientOptionGeneratorKind.Tezcatara,
            MaxScratchCount: 3 } && plan.ScratchWords == 3;

    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest request,
        AncientOptionFamilyPlan plan, FamilyPhysicalQuoteRequest geometry) =>
        QuoteSeaGlass(request, plan, geometry) ?? QuoteMeasured(request, plan, geometry) ?? QuoteGeneratorEnvelope(request, plan, geometry, false);

    private static FamilyPhysicalQuote? QuoteSeaGlass(ExactSearchExecutionRequest request,
        AncientOptionFamilyPlan plan, FamilyPhysicalQuoteRequest geometry)
    {
        if (!plan.GpuSupported || !FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            !FamilyPhysicalQuote.HasReferenceBackend() || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 ||
            request.Evaluation.AncientBranchConditions is not [{ SeaGlassTargetAny.Count: > 0 } row] ||
            plan.SingleOptionPlan is not { Generator: WorldFastAncientOptionGeneratorKind.Orobas,
                SeaGlassTargetAuthorityExact: true } p || !MeasuredPools(p)) return null;
        // Reuse the Orobas generator envelope plus bounded target comparisons.
        // This is a conservative derived quote, not a measured SeaGlass speed.
        double ns = 1.02 + .03 * (row.OptionAny.Count + row.SeaGlassTargetAny.Count) + .02 * p.OtherUnlockedCharacters.Length;
        return new("A.AncientOption", $"A.OrobasSeaGlass.20260913.v1.Characters{p.OtherUnlockedCharacters.Length}.Targets{row.SeaGlassTargetAny.Count}",
            ns, plan.Capacity, 160,
            "Model=ConservativeCoarse;OrobasGeneratorEnvelopePlusTargetWork;RuntimeCharacterPool;NumericalDeviceEmission;NoObservedSurvival");
    }

    private static FamilyPhysicalQuote? QuoteMeasured(ExactSearchExecutionRequest request,
        AncientOptionFamilyPlan plan, FamilyPhysicalQuoteRequest geometry)
    {
        if (!plan.GpuSupported || !FamilyPhysicalQuote.AdmittedRequest(geometry) ||
            !FamilyPhysicalQuote.HasReferenceBackend() || request.ProfileId != Compatibility.RuntimeProfileId.Beta111 ||
            !request.Authority.CanUseCurrentModel || request.Authority.PlayersCount != 1 ||
            request.Authority.AllCharacterCardPoolsUnlocked != true) return null;
        var rows = request.Evaluation.AncientBranchConditions;
        if (rows.Any(r => r.OptionAny.Count != 1 || r.SeaGlassTargetAny.Count != 0)) return null;
        bool single = rows.Count == 1 && plan.SingleOptionPlan is not null;
        bool two = rows.Count == 2 && plan.ScratchWords == 9 &&
            rows.Select(r => r.Act).Distinct().Count() == 2 &&
            rows.Select(r => r.AncientKey).Distinct().Count() == 2 &&
            plan.OptionPlans.Select(p => p.Generator).Order().SequenceEqual(new[] {
                WorldFastAncientOptionGeneratorKind.Darv, WorldFastAncientOptionGeneratorKind.Tezcatara }.Order());
        if (!single && !two) return null;
        if (!plan.OptionPlans.All(MeasuredPools)) return null;
        // Generator + compiled pool/scratch geometry; target identities never price
        // the work. Hook/SeaGlass and unmeasured pool layouts retain Unknown.
        double? ns = single ? plan.SingleOptionPlan switch {
            { Generator: WorldFastAncientOptionGeneratorKind.Tezcatara, MaxScratchCount: 3 } => geometry.CompactInput ? 1.0 : .8,
            { Generator: WorldFastAncientOptionGeneratorKind.Pael, MaxScratchCount: 7 } => .90,
            { Generator: WorldFastAncientOptionGeneratorKind.Orobas, MaxScratchCount: 2 } => .93,
            { Generator: WorldFastAncientOptionGeneratorKind.Darv, MaxScratchCount: 9 or 11 } => geometry.CompactInput ? 1.68 : 1.80,
            { Generator: WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx, MaxScratchCount: 10 } => 1.35,
            { Generator: WorldFastAncientOptionGeneratorKind.Vakuu, MaxScratchCount: 4 } => 1.02,
            _ => null
        } : geometry.CompactInput ? 1.6 : 1.8;
        if (ns is null) return null;
        return new("A.AncientOption", "A.EventLocal." + plan.PricingSignature, ns.Value,
            plan.Capacity, 160, "FamilyMatrix.20260910.AncientGenerators;NumericalDeviceEmission;NoPublicMaterialization");
    }

    private static bool MeasuredPools(WorldFastAncientOptionPlan p) => p.Generator switch {
            WorldFastAncientOptionGeneratorKind.Tezcatara => p.Pools.Length == 4 &&
                p.Pools.Select((pool, index) => pool.Values.Length is >= 1 &&
                    pool.Values.Length <= new[] { 2, 1, 3, 4 }[index]).All(inside => inside),
            WorldFastAncientOptionGeneratorKind.Pael => p.Pools.Select(x => x.Values.Length).SequenceEqual(new[] { 3, 1, 1, 1, 1, 2, 1 }),
            WorldFastAncientOptionGeneratorKind.Orobas => p.Pools.Select(x => x.Values.Length).SequenceEqual(new[] { 3, 3, 4, 1, 1 }),
            WorldFastAncientOptionGeneratorKind.Darv => p.Pools.Length is 10 or 12 && p.Pools.All(x => x.Values.Length == 1),
            WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx => p.Pools.Select(x => x.Values.Length).SequenceEqual(new[] { 9, 1 }),
            WorldFastAncientOptionGeneratorKind.Vakuu => p.Pools.Select(x => x.Values.Length).SequenceEqual(new[] { 3, 3, 4 }),
            _ => false
        };

    internal static FamilyPhysicalQuote? QuoteCpu(ExactSearchExecutionRequest request,
        AncientOptionFamilyPlan plan, FamilyPhysicalQuoteRequest g) =>
        QuoteCpuMeasured(request, plan, g) ?? QuoteGeneratorEnvelope(request, plan, g, true);

    private static FamilyPhysicalQuote? QuoteGeneratorEnvelope(ExactSearchExecutionRequest request,
        AncientOptionFamilyPlan plan, FamilyPhysicalQuoteRequest g, bool cpu)
    {
        var allFilters = request.Evaluation.AncientOptionFilters.Where(f => !f.IsEmpty).ToArray();
        if (!plan.GpuSupported || !FamilyPhysicalQuote.AdmittedRequest(g) ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            plan.OptionPlans.Count is < 1 or > 4 || !plan.OptionPlans.All(MeasuredPools) ||
            request.Evaluation.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty) ||
            allFilters.Any(f => f.Keys.Any.Count > 0 || f.Keys.Ban.Count > 0 || f.Keys.All.Count is < 1 or > 3 ||
                request.Evaluation.AncientBranchConditions.Count(r => r.Act == f.Act) != 1) ||
            request.Evaluation.AncientBranchConditions.Any(r => r.SeaGlassTargetAny.Count != 0 ||
                r.OptionAny.Count > 4 || r.OptionAny.Count == 0 && !allFilters.Any(f => f.Act == r.Act))) return null;
        if (cpu ? g.CompactInput || g.PrivateInput || g.PrivateOutput || g.MeanInputPopulation < 4096 :
            !FamilyPhysicalQuote.HasReferenceBackend()) return null;
        // Reuse measured generator envelopes without pretending to know branch
        // reach. Summation bounds both same-act OR and cross-act early rejection.
        double ns = plan.OptionPlans.Sum(p => cpu ? p.Generator switch {
            WorldFastAncientOptionGeneratorKind.Darv => 275d,
            WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx or WorldFastAncientOptionGeneratorKind.Vakuu => 195d,
            _ => 160d } : p.Generator switch {
            WorldFastAncientOptionGeneratorKind.Darv => 1.8,
            WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx => 1.35,
            _ => 1.02 });
        ns += (request.Evaluation.AncientBranchConditions.Sum(r => Math.Max(0, r.OptionAny.Count - 1)) +
            allFilters.Sum(f => Math.Max(0, f.Keys.All.Count - 1))) * (cpu ? 5 : .03);
        return new("A.AncientOption", "A.GeneratorEnvelope.20260913.v1" + (cpu ? ".P1" : ".Gpu"), ns,
            cpu ? 65536 : plan.Capacity, cpu ? null : 160,
            "PricingHoleSweep.20260923;Model=ConservativeCoarse;KnownGeneratorPoolSum;BranchCount1..4;ModernAllOffered1..3;NoAssumedBranchReach;" +
            (cpu ? "CpuCanonicalAbi1" : "NumericalDeviceEmission;HostEdgesExcluded"),
            OutputElementBytes: cpu ? 8 : 4, OutputAlreadyOrdered: cpu, PublicTransportClass: cpu ? "CpuOrderedAbi1" : "Counted32")
        { FixedWindowMilliseconds = cpu ? 0 : 1 };
    }

    private static FamilyPhysicalQuote? QuoteCpuMeasured(ExactSearchExecutionRequest request,
        AncientOptionFamilyPlan plan, FamilyPhysicalQuoteRequest g)
    {
        if (!plan.GpuSupported || !FamilyPhysicalQuote.AdmittedRequest(g) || g.CompactInput ||
            g.PrivateInput || g.PrivateOutput || g.MeanInputPopulation < 4096 ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || !request.Authority.CanUseCurrentModel ||
            request.Authority.PlayersCount != 1 || request.Authority.AllCharacterCardPoolsUnlocked != true ||
            request.Evaluation.AncientBranchConditions is not [{ OptionAny.Count: 1, SeaGlassTargetAny.Count: 0 }] ||
            plan.SingleOptionPlan is not { } p || !MeasuredPools(p)) return null;
        // Three bounded CPU generator classes. No generic multiplication by GPU
        // coefficients, target identity, or unobserved multi-act reach.
        double? ns = p.Generator switch
        {
            WorldFastAncientOptionGeneratorKind.Tezcatara or WorldFastAncientOptionGeneratorKind.Pael or
                WorldFastAncientOptionGeneratorKind.Orobas => 160,
            WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx or
                WorldFastAncientOptionGeneratorKind.Vakuu => 195,
            WorldFastAncientOptionGeneratorKind.Darv => 275,
            _ => null
        };
        return ns is null ? null : new("A.AncientOption", "A.GeneratorClass.20260913.v1.P1." + p.Generator,
            ns.Value, 65536, null, "FamilyCostClosure.20260913;Model=BoundedCoarse;SingleGenerator;" +
            "Workers=1;MinInput=4096;CpuCanonicalAbi1;metric=ns/ActualStageInput",
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1");
    }

    // Same distinct-event conditional projection formerly embedded in the private
    // comparator. Each row is hypothetical; occurrence/identity remains W-owned.
    internal static FamilySurvivalProjection ResolveSurvival(ExactSearchExecutionRequest request)
    {
        var e = request.Evaluation;
        var rows = e.AncientBranchConditions.Where(r => r.OptionAny.Count > 0 || r.SeaGlassTargetAny.Count > 0 ||
            e.AncientOptionFilters.Any(f => f.Act == r.Act && f.Keys.All.Count > 0)).ToArray();
        if (rows.Length == 0 || rows.Select(r => r.Act).Distinct().Count() != rows.Length ||
            rows.Select(r => r.AncientKey).Distinct().Count() != rows.Length ||
            e.AncientOptionFilters.Any(f => !f.IsEmpty && (f.Keys.Any.Count > 0 || f.Keys.Ban.Count > 0 ||
                e.AncientBranchConditions.Count(b => b.Act == f.Act) != 1)) || e.AncientSeaGlassTargetFilters.Any(f => !f.IsEmpty))
            return FamilySurvivalProjection.Unresolved("A.AncientOption", "A.JointGeneratorProjectionUnknown");
        double product = 1;
        foreach (var row in rows)
        {
            var estimate = AncientOptionProbabilityEstimator.EstimateConditionalAll(SearchSelectivityInput.From(request), row,
                e.AncientOptionFilters.Where(f => f.Act == row.Act).SelectMany(f => f.Keys.All).Distinct().ToArray());
            if (estimate is not { IsPriced: true, Probability: { } p })
                return FamilySurvivalProjection.Unresolved("A.AncientOption", "A.ConditionalProjectionUnknown");
            product *= p;
        }
        return FamilySurvivalProjection.Resolved("A.AncientOption", product,
            "A.DistinctActEventLocalConditionalProduct;ModeledNotObserved");
    }
}
