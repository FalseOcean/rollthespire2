using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit, unpriced Y1 experiment. Never selected by normal Production pricing.
internal static class EwPhysicalExperiment
{
    internal static string Mode => Environment.GetEnvironmentVariable("RT2_EW_EXPERIMENT") ?? "";
    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        FamilyExecutionPlan ordinary, out FamilyExecutionPlan? selected, string? explicitMode = null)
    {
        selected = null;
        string mode = explicitMode ?? Mode;
        if (mode.Length == 0) return false;
        if (mode is not ("ordinary-ew" or "ordinary-we" or "private-ordinal" or "verify-ordinal" or "ordinary-ecw" or "private-ecw" or "verify-ecw")) throw new InvalidOperationException("EW.UnknownMode.RootRetentionParked:" + mode);
        bool ecw = mode.EndsWith("-ecw",StringComparison.Ordinal);
        var combat = ecw ? registered.OfType<CombatRewardFamily>().SingleOrDefault() : null;
        if (registered.Count != (ecw ? 3 : 2) || (ecw && combat?.GpuPlan is null) || registered.OfType<EventResultFamily>().SingleOrDefault() is not { } e ||
            registered.OfType<WorldFamily>().SingleOrDefault() is not { } w ||
            !e.Plan.GpuSupported || !WorldFamilyGpuPlan.Supports(w.Replay))
            throw new InvalidOperationException("Y1.RequiresExactlyGpuEWCoverage");
        IFamilyInvocation[] ordered = mode.StartsWith("private-",StringComparison.Ordinal) || mode.StartsWith("verify-",StringComparison.Ordinal)
            ? ecw ? [new EcwPrivateClosureProbe(request,e.Plan,combat!.GpuPlan!,new WorldFamilyGpuPlan(w.Replay),mode=="verify-ecw")]
                  : [new EwPrivateInvocation(request, e.Plan, new WorldFamilyGpuPlan(w.Replay), mode)]
            : ecw ? [e,combat!,w] : mode == "ordinary-ew" ? [e,w] : [w,e];
        var stages = ordered.Select((f, i) =>
        {
            var condition = f.ResolveConditionPerformance(i != 0);
            var survival = f.Survival.SurvivalProbability;
            return new FamilyExecutionPlanStage(f, f.Coverage, i != 0, i == 0 ? 1 : double.NaN,
                survival, survival.HasValue ? 1-survival : null, null, condition,
                GpuCostCalibration.ResolveReference(condition), null, null, null, null, i);
        }).ToArray();
        selected = new("Y1." + mode, "Y1.ExplicitUnpricedAllocation.20260908", FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            stages, null, ["ExplicitOwnerExperiment", "NormalSelectedPlan=" + ordinary.PlanId], ["ExperimentalQuoteUnavailable"]);
        RuntimeLog.TryBackgroundInfo($"ewExperimentSelected=true;mode={mode};ordinaryPlan={ordinary.PlanId};actual={string.Join('>', ordered.Select(f=>f.FamilyId))};pricingChanged=false");
        return true;
    }
}
