using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit, unpriced Y1 experiment. Never selected by normal Production pricing.
internal static class EwPhysicalExperiment
{
    internal static string Mode => Environment.GetEnvironmentVariable("RT2_EW_EXPERIMENT") ?? "";
    internal static bool TrySelect(PreparedSearch request, IReadOnlyList<FilterBodyDescription> registered,
        FamilyExecutionPlan ordinary, out FamilyExecutionPlan? selected, string? explicitMode = null)
    {
        selected = null;
        string mode = explicitMode ?? Mode;
        if (mode.Length == 0) return false;
        if (mode is not ("ordinary-ew" or "ordinary-we" or "private-ordinal" or "verify-ordinal" or "ordinary-ecw" or "private-ecw" or "verify-ecw")) throw new InvalidOperationException("EW.UnknownMode.RootRetentionParked:" + mode);
        bool ecw = mode.EndsWith("-ecw",StringComparison.Ordinal);
        var model = request.Model;
        var preferences = FilterExecutionPreferences.From(request.RunOptions);
        var eventBody = registered.SingleOrDefault(f => f.FamilyId == "E.EventResult");
        var worldBody = registered.SingleOrDefault(f => f.FamilyId == "W.World");
        var combatBody = registered.SingleOrDefault(f => f.FamilyId == "C.CombatReward");
        CombatRewardPreparation? combat = null;
        if (registered.Count != (ecw ? 3 : 2) || eventBody?.UsesGpu != true || worldBody?.UsesGpu != true ||
            !EventResultFamily.TryPrepare(model, preferences, out var events) || !events!.Plan.GpuSupported ||
            (ecw && (combatBody?.UsesGpu != true || !CombatRewardFamily.TryPrepare(model, preferences, out combat) || combat!.GpuPlan is null)))
            throw new InvalidOperationException("Y1.RequiresExactlyGpuEWCoverage");
        var replay = new WorldFamilyReplay(model);
        if (!WorldFamilyGpuPlan.Supports(replay)) throw new InvalidOperationException("Y1.RequiresExactlyGpuEWCoverage");
        var world = new WorldFamilyGpuPlan(replay);
        FilterBodyDescription[] ordered = mode.StartsWith("private-", StringComparison.Ordinal) || mode.StartsWith("verify-", StringComparison.Ordinal)
            ? ecw ? [EcwPrivateClosureProbe.Prepare(model, events.Plan, combat!.GpuPlan!, world, mode == "verify-ecw",
                        combat.Body.Preparation.Remainders)]
                  : [EwPrivateInvocation.Prepare(model, events.Plan, world, mode)]
            : ecw ? [eventBody, combatBody!, worldBody] : mode == "ordinary-ew" ? [eventBody, worldBody] : [worldBody, eventBody];
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
