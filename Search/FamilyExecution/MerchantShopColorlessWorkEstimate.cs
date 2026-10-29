using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class MerchantShopColorlessWorkEstimate
{
    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest request,
        MerchantShopColorlessCompactionDecision decision, FamilyExpectedFilteringCostProjection expected,
        FamilyPhysicalQuoteRequest geometry, bool cpu, int workers = 1, bool compiledMatcher = true)
    {
        var e = request.Evaluation;
        int horizon = e.MerchantColorlessConditions.Select(c => c.MerchantOrdinal)
            .Concat(e.MerchantColorlessSequenceConditions.Select(c => c.Count)).DefaultIfEmpty(0).Max();
        double calls = horizon * Beta111NormalMerchantShopsContinuation.CallsPerMerchant;
        double observations = horizon * 2d;
        double probes = e.MerchantColorlessConditions.Count + e.MerchantColorlessSequenceConditions.Sum(c =>
            c.OrderMode == CombatRewardSequenceOrderMode.Ordered ? (double)c.Count : c.Count * (double)c.Slots.Count(k => k.HasValue));
        double visits = horizon;
        if (expected.IsResolved)
        {
            calls = expected.Segments.Where(s => s.Operation is FamilyAnalyticalOperation.RngAdvance or FamilyAnalyticalOperation.HistoricalNextInt).Sum(s => s.ExpectedContribution);
            observations = expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.ObservationWrite).Sum(s => s.ExpectedContribution);
            probes = expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.PredicateProbe).Sum(s => s.ExpectedContribution);
            visits = expected.Segments.Where(s => s.Operation == FamilyAnalyticalOperation.RngAdvance && s.Name.StartsWith("S.MerchantReplay[", StringComparison.Ordinal))
                .Sum(s => 1 - Math.Pow(1 - s.ReachProbability, 32));
        }
        bool stable = !geometry.CompactInput && !geometry.PrivateInput && !geometry.PrivateOutput &&
            decision.Path == MerchantShopColorlessCompactionPath.StableOrderedCompaction;
        double ns = cpu ? 40 + .85 * calls + 4 * observations + 1.5 * probes :
            .1 + .12 * visits + (geometry.CompactInput ? .3 : 0) + (stable ? .06 : 0) + .003 * probes;
        if (!compiledMatcher) ns *= 4;
        string basis = FormattableString.Invariant($"S.FamilyCostClosure.20260913;Calls={calls:G9};Observations={observations:G9};Probes={probes:G9};GroupVisits32={visits:G9};Horizon={horizon};RuntimePoolMembershipViaExistingProjection;SharedMerchantTraversal;CompiledMatcher={compiledMatcher};NoObservedSurvival;NoFixedPoolCountAdmission");
        if (cpu) return FamilyCoarsePricing.Cpu("S.MerchantShopColorless", e.MerchantColorlessSequenceConditions.Count == 0 ? "S.Slots" : "S.Sequences", ns, workers, geometry, basis);
        int capacity = !geometry.PrivateInput && !geometry.PrivateOutput ? MerchantShopColorlessGpuExecutor.SurvivorCapacity : MerchantShopColorlessGpuExecutor.Capacity;
        return FamilyCoarsePricing.Gpu("S.MerchantShopColorless", stable ? "S.Stable" : "S.Atomic", ns, capacity, geometry, basis,
            100, geometry.PrivateOutput ? 4 : 8, stable);
    }
}
