using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit complete R/S/W allocations for port/parity evidence. No automatic quote.
internal static class RswPhysicalExperiment
{
    internal static bool TrySelect(PreparedSearch request, IReadOnlyList<FilterBodyDescription> registered,
        FamilyExecutionPlan ordinary, out FamilyExecutionPlan? selected)
    {
        selected = null;
        string mode = Environment.GetEnvironmentVariable("RT2_RSW_EXPERIMENT") ?? "";
        if (mode.Length == 0) return false;
        string[] parts = mode.Split('-');
        if (parts.Length != 2 || parts[0] is not ("ordinary" or "private" or "verify") ||
            parts[1] is not ("RSW" or "RWS" or "SRW" or "SWR" or "WRS" or "WSR"))
            throw new InvalidOperationException("RSW.ExplicitModeInvalid");
        var model = request.Model;
        if (registered.Count != 3 || !registered.Any(f => f.FamilyId == "R.Relic" && f.UsesGpu && f.Coverage.Count == 1) ||
            !registered.Any(f => f.FamilyId == "S.MerchantShopColorless" && f.UsesGpu) ||
            !registered.Any(f => f.FamilyId == "W.World" && f.UsesGpu) || NeowReplayPlan.HasCapsule(model) ||
            !RelicFamilyPlanCompiler.TryCompile(model, out var r, out _))
            throw new InvalidOperationException("RSW.RequiresOrdinaryGpuRSWCoverage");
        var replay = new WorldFamilyReplay(model);
        if (!WorldFamilyGpuPlan.Supports(replay)) throw new InvalidOperationException("RSW.RequiresOrdinaryGpuRSWCoverage");
        var shops = MerchantShopColorlessPreparation.Prepare(model, true).GpuPlan ?? throw new InvalidOperationException("RSW.ShopPreparationUnavailable");
        string order = parts[1];
        FilterBodyDescription[] allocation = parts[0] == "ordinary"
            ? order.Select(c => registered.Single(f => f.FamilyId == RswPrivateInvocation.Name(c))).ToArray()
            : [RswPrivateInvocation.Prepare(model,model.SnapshotFingerprint,shops,r!,new WorldFamilyGpuPlan(replay),order,parts[0]=="verify")];
        var stages = allocation.Select((f,i) =>
        {
            var condition = f.ResolveConditionPerformance(i != 0);
            return new FamilyExecutionPlanStage(f,f.Coverage,i!=0,i==0?1:double.NaN,null,null,null,condition,
                GpuCostCalibration.ResolveReference(condition),null,null,null,null,i);
        }).ToArray();
        selected = new("RSW."+mode,"RSW.Connectivity.20260910.v1",FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            stages,null,["ExplicitConnectivityProbe","Ordinary="+ordinary.PlanId],["Unpriced"]);
        Bootstrap.RuntimeLog.TryBackgroundInfo($"rswSelected=true;mode={mode};order={order};normalPricingChanged=false");
        return true;
    }
}
