using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NcPhysicalExperiment
{
    internal static string Mode => Environment.GetEnvironmentVariable("RT2_NC_EXPERIMENT") ?? "";
    internal static bool TrySelect(ExactSearchExecutionRequest request,IReadOnlyList<IFamilyInvocation> registered,
        FamilyExecutionPlan ordinary,out FamilyExecutionPlan? selected, string? explicitMode = null)
    {
        selected=null;string mode=explicitMode ?? Mode;if(mode.Length==0)return false;
        if(EwPhysicalExperiment.Mode.Length!=0)throw new InvalidOperationException("Y5.ConflictingExperiments");
        if(mode is not ("ordinary" or "private-ordinal" or "verify-ordinal" or "ordinary-nc" or "ordinary-cn" or "private-nc" or "verify-nc"))throw new InvalidOperationException("Y5.UnknownMode");
        if(registered.Count!=2||registered.OfType<CombatRewardFamily>().SingleOrDefault()?.GpuPlan is not {} c ||
            registered.OfType<NeowFamily>().SingleOrDefault()?.GpuPlan is not {} n)
            throw new InvalidOperationException("Y5.RequiresGpuNCOnly");
        // Preserve the original Y5 natural-order modes; rare-NC modes explicitly compare complete allocations.
        if(mode is "ordinary" or "private-ordinal" or "verify-ordinal" && ordinary.OrderedFamilies[0].FamilyId!="C.CombatReward")throw new InvalidOperationException("Y5.ExpectedNaturalCFirst");
        if(mode=="ordinary")selected=ordinary;
        else if(mode is "ordinary-nc" or "ordinary-cn")
        {
            var first=mode=="ordinary-nc"?"N.Neow":"C.CombatReward";
            var ordered=registered.OrderBy(f=>f.FamilyId==first?0:1).ToArray();
            var stages=ordered.Select((f,i)=>
            {
                var condition=f.ResolveConditionPerformance(i!=0);
                return new FamilyExecutionPlanStage(f,f.Coverage,i!=0,i==0?1:double.NaN,null,null,null,condition,
                    GpuCostCalibration.ResolveReference(condition),null,null,null,null,i);
            }).ToArray();
            selected=new("RareNC."+mode,"RareNC.ExplicitUnpriced.20260908",FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
                stages,null,["ExplicitOwnerExperiment"],["ExperimentalQuoteUnavailable"]);
        }
        else
        {
            var f=new NcPrivateInvocation(request,c,n,mode);
            var condition=f.ConditionPerformance;
            var stage=new FamilyExecutionPlanStage(f,f.Coverage,false,1,null,null,null,condition,
                GpuCostCalibration.ResolveReference(condition),null,null,null,null,0);
            selected=new("Y5."+mode,"Y5.ExplicitUnpriced.20260908",FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
                [stage],null,["ExplicitOwnerExperiment"],["ExperimentalQuoteUnavailable"]);
        }
        Bootstrap.RuntimeLog.TryBackgroundInfo($"ncExperimentSelected=true;mode={mode};ordinaryPlan={ordinary.PlanId};actual={(mode.EndsWith("-nc",StringComparison.Ordinal)?"N.Neow>C.CombatReward":"C.CombatReward>N.Neow")};pricingChanged=false");
        return true;
    }
}
