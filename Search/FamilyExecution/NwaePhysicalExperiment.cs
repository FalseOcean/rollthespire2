using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit bounded experiment only. No normal Planner candidate or quote is added.
internal static class NwaePhysicalExperiment
{
    internal static string Mode => Environment.GetEnvironmentVariable("RT2_NWAE_EXPERIMENT") ?? "";
    internal static bool TrySelect(PreparedSearch request, IReadOnlyList<FilterBodyDescription> registered,
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
        var model = request.Model;
        var preferences = FilterExecutionPreferences.From(request.RunOptions);
        var nBody = registered.SingleOrDefault(f => f.FamilyId == "N.Neow");
        var aBody = registered.SingleOrDefault(f => f.FamilyId == "A.AncientOption");
        var eBody = registered.SingleOrDefault(f => f.FamilyId == "E.EventResult");
        var wBody = registered.SingleOrDefault(f => f.FamilyId == "W.World");
        NeowFamilyGpuPlan? n = null;
        NeowReplayPlan? neowReplay = null;
        AncientOptionPreparation? a = null;
        EventResultPreparation? e = null;
        if (wBody?.UsesGpu != true || nBody is not null && (!nBody.UsesGpu ||
            !NeowFamilyGpuPlan.TryCreate(neowReplay = NeowReplayPlan.Compile(model, false), out n, out _) || n is null) ||
            aBody is not null && (!aBody.UsesGpu || !AncientOptionFamily.TryPrepare(model, preferences, out a) || !a!.Plan.GpuSupported) ||
            eBody is not null && (!eBody.UsesGpu || !EventResultFamily.TryPrepare(model, preferences, out e) || !e!.Plan.GpuSupported))
            throw new InvalidOperationException("NWAE.RequiresSupportedGpuFamilies");
        var replay = new WorldFamilyReplay(model);
        if (!WorldFamilyGpuPlan.Supports(replay)) throw new InvalidOperationException("NWAE.RequiresSupportedGpuFamilies");
        FilterBodyDescription[] ordered = parts[0] == "ordinary"
            ? order.Select(c => registered.Single(f => f.FamilyId[0] == c)).ToArray()
            : [NwaePrivateInvocation.Prepare(model, n, a?.Plan, e?.Plan, new WorldFamilyGpuPlan(replay), order, parts[0] == "verify",
                neowReplay?.FilterRemainders ?? [])];
        selected = FamilyPlanner.InOrder(ordered) with { PlanId = "NWAE." + mode,
            SelectionPolicyId = "NWAE.ExplicitUnpriced.20260908.v1", RankingAuthority = FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            ExpectedFamilyPipelineMsPerRoot = null, DecisionEvidence = ["ExplicitBoundedExperiment", "NormalPlan=" + baseline.PlanId],
            MissingEvidence = ["ExperimentalAllocationUnpriced"] };
        // Experiment-local model decomposition only: one conditional Ancient row per
        // act multiplies under the default independence policy. No observed ratios.
        double? aModeled = aBody?.Survival.SurvivalProbability;
        var branches = model.Evaluation.AncientBranchConditions;
        if (aBody is not null && aModeled is null && branches.Count > 0 && branches.GroupBy(b => b.Act).All(g => g.Count() == 1) &&
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
            $"modeledN={nBody?.Survival.SurvivalProbability};modeledA={aModeled};modeledE={eBody?.Survival.SurvivalProbability};" +
            "AProjection=ExperimentOnlyDistinctActRowsAssumedIndependent;observedSurvivalPricing=false;normalPricingChanged=false");
        return true;
    }
}
