using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Explicit complete R/S/W allocations for port/parity evidence. No automatic quote.
internal static class RswPhysicalExperiment
{
    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        FamilyExecutionPlan ordinary, out FamilyExecutionPlan? selected)
    {
        selected = null;
        string mode = Environment.GetEnvironmentVariable("RT2_RSW_EXPERIMENT") ?? "";
        if (mode.Length == 0) return false;
        string[] parts = mode.Split('-');
        if (parts.Length != 2 || parts[0] is not ("ordinary" or "private" or "verify") ||
            parts[1] is not ("RSW" or "RWS" or "SRW" or "SWR" or "WRS" or "WSR"))
            throw new InvalidOperationException("RSW.ExplicitModeInvalid");
        if (registered.Count != 3 || registered.OfType<RelicFamily>().SingleOrDefault() is null ||
            registered.OfType<MerchantShopColorlessFamily>().SingleOrDefault() is null ||
            registered.OfType<WorldFamily>().SingleOrDefault() is not { } w || !WorldFamilyGpuPlan.Supports(w.Replay) ||
            !RelicFamilyPlanCompiler.TryCompile(request, out var r, out _))
            throw new InvalidOperationException("RSW.RequiresOrdinaryGpuRSWCoverage");
        string order = parts[1];
        IFamilyInvocation[] allocation = parts[0] == "ordinary"
            ? order.Select(c => registered.Single(f => f.FamilyId == RswPrivateInvocation.Name(c))).ToArray()
            : [new RswPrivateInvocation(request,r!,new WorldFamilyGpuPlan(w.Replay),order,parts[0]=="verify")];
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
