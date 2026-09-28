using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit bounded experiment only. No normal Planner candidate or quote is added.
internal static class NwaePhysicalExperiment
{
    internal static string Mode => Environment.GetEnvironmentVariable("RT2_NWAE_EXPERIMENT") ?? "";
    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        FamilyExecutionPlan baseline, out FamilyExecutionPlan? selected, string? explicitMode = null)
    {
        selected = null;
        string mode = explicitMode ?? Mode;
        if (mode.Length == 0) return false;
        if (explicitMode is null && PrivateOrdinalSelection.IsRequested || NcPhysicalExperiment.Mode.Length != 0 || EwPhysicalExperiment.Mode.Length != 0)
            throw new InvalidOperationException("NWAE.ConflictingSelection");
        var parts = mode.Split('-');
        if (parts.Length != 2 || parts[0] is not ("ordinary" or "private" or "verify"))
            throw new InvalidOperationException("NWAE.UnknownMode");
        string order = parts[1];
        if (order.Length < 2 || order.Length > 4 || order.Distinct().Count() != order.Length || !order.Contains('W') ||
            order.Any(c => !"NAEW".Contains(c)) || registered.Count != order.Length ||
            !order.ToHashSet().SetEquals(registered.Select(f => f.FamilyId[0])))
            throw new InvalidOperationException("NWAE.IncompleteCoverage");
        var n = registered.OfType<NeowFamily>().SingleOrDefault();
        var a = registered.OfType<AncientOptionFamily>().SingleOrDefault();
        var e = registered.OfType<EventResultFamily>().SingleOrDefault();
        var w = registered.OfType<WorldFamily>().SingleOrDefault();
        if (w is null || !WorldFamilyGpuPlan.Supports(w.Replay) || n is not null && n.GpuPlan is null ||
            a is not null && !a.Plan.GpuSupported || e is not null && !e.Plan.GpuSupported)
            throw new InvalidOperationException("NWAE.RequiresSupportedGpuFamilies");
        IFamilyInvocation[] ordered = parts[0] == "ordinary"
            ? order.Select(c => registered.Single(f => f.FamilyId[0] == c)).ToArray()
            : [new NwaePrivateInvocation(request, n?.GpuPlan, a?.Plan, e?.Plan,
                new WorldFamilyGpuPlan(w.Replay), order, parts[0] == "verify")];
        selected = FamilyPlanner.InOrder(ordered) with { PlanId = "NWAE." + mode,
            SelectionPolicyId = "NWAE.ExplicitUnpriced.20260908.v1", RankingAuthority = FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            ExpectedFamilyPipelineMsPerRoot = null, DecisionEvidence = ["ExplicitBoundedExperiment", "NormalPlan=" + baseline.PlanId],
            MissingEvidence = ["ExperimentalAllocationUnpriced"] };
        // Experiment-local model decomposition only: one conditional Ancient row per
        // act multiplies under the default independence policy. No observed ratios.
        double? aModeled = a?.Survival.SurvivalProbability;
        var branches = request.Evaluation.AncientBranchConditions;
        if (a is not null && aModeled is null && branches.Count > 0 && branches.GroupBy(b => b.Act).All(g => g.Count() == 1) &&
            branches.Select(b => b.AncientKey).Distinct().Count() == branches.Count)
        {
            double p = 1; bool complete = true;
            foreach (var branch in branches)
            {
                var estimate = AncientOptionProbabilityEstimator.EstimateConditional(SearchSelectivityInput.From(request), branch);
                if (estimate is { IsPriced: true, Probability: { } probability }) p *= probability;
                else complete = false;
            }
            if (complete) aModeled = p;
        }
        RuntimeLog.TryBackgroundInfo($"nwaeExperimentSelected=true;mode={mode};order={order};normalPlan={baseline.PlanId};" +
            $"modeledN={n?.Survival.SurvivalProbability};modeledA={aModeled};modeledE={e?.Survival.SurvivalProbability};" +
            "AProjection=ExperimentOnlyDistinctActRowsAssumedIndependent;observedSurvivalPricing=false;normalPricingChanged=false");
        return true;
    }
}
