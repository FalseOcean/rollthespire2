using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class AncientOptionWorkEstimate
{
    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest request, AncientOptionFamilyPlan plan,
        FamilyPhysicalQuoteRequest geometry, bool cpu, int workers = 1)
    {
        double ns = 0;
        foreach (var p in plan.OptionPlans)
        {
            double baseline = cpu ? p.Generator switch {
                WorldFastAncientOptionGeneratorKind.Darv => 275,
                WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx or WorldFastAncientOptionGeneratorKind.Vakuu => 195,
                _ => 160
            } : p.Generator switch {
                WorldFastAncientOptionGeneratorKind.Darv => 1.8,
                WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx => 1.35,
                WorldFastAncientOptionGeneratorKind.Tezcatara => .8,
                _ => 1.02
            };
            int referenceScratch = p.Generator switch {
                WorldFastAncientOptionGeneratorKind.Darv => 9,
                WorldFastAncientOptionGeneratorKind.Nonupeipe or WorldFastAncientOptionGeneratorKind.Tanx => 10,
                WorldFastAncientOptionGeneratorKind.Pael => 7,
                WorldFastAncientOptionGeneratorKind.Vakuu => 4,
                WorldFastAncientOptionGeneratorKind.Orobas => 2,
                _ => 3
            };
            // Source count changes can reuse the generator class without an exact
            // old pool-vector match. Summation is a conservative OR/AND envelope.
            double workScale = Math.Max(.5, (double)Math.Max(1, p.MaxScratchCount) / referenceScratch);
            ns += baseline * workScale;
            ns += p.Pools.Sum(pool => Math.Max(0, pool.Values.Length - 1)) * (cpu ? .8 : .003);
            ns += p.OtherUnlockedCharacters.Length * (cpu ? 3 : .02);
        }
        int probes = request.Evaluation.AncientBranchConditions.Sum(r => r.OptionAny.Count + r.SeaGlassTargetAny.Count) +
            request.Evaluation.AncientOptionFilters.Sum(f => f.Keys.Any.Count + f.Keys.All.Count + f.Keys.Ban.Count) +
            request.Evaluation.AncientSeaGlassTargetFilters.Sum(f => f.Keys.Any.Count + f.Keys.All.Count + f.Keys.Ban.Count);
        int referenceRows = plan.PricingReferenceRows;
        if (referenceRows > 0) ns += referenceRows * (cpu ? 640 : 4);
        if (plan.OptionPlans.Count == 0 && referenceRows == 0) ns = cpu ? 30 : .15;
        ns += probes * (cpu ? 5 : .03);
        string basis = FormattableString.Invariant($"A.AncientGenerators.20260910-FamilyCostClosure.20260913;Generators={plan.OptionPlans.Count};ReferenceRows={referenceRows};Scratch={plan.ScratchWords};Probes={probes};RuntimePoolCounts;GeneratorSumEnvelope;NoAssumedBranchReach;NumericalVariant={plan.NumericalVariant};IntegerUsesOperationEstimateNotNativeMeasurement");
        return cpu ? FamilyCoarsePricing.Cpu("A.AncientOption", referenceRows > 0 ? "A.ReferenceRows" : "A.GeneratorWork", ns, workers, geometry, basis) :
            FamilyCoarsePricing.Gpu("A.AncientOption", "A.GeneratorWork", ns, plan.Capacity, geometry, basis, 160);
    }
}
