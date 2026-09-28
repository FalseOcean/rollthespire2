using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class RelicFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            var ordinary = RelicCpuPlan.TryCompile(_plan);
            bool referencePool = _request.Authority.PlayersCount == 1 &&
                _plan.Pool.BucketLengths.SequenceEqual(new[]{30,25,35,25,1,2,32,26,38,26});
            var shop = RelicShopCpuPlan.TryCompile(_plan);
            bool common = referencePool && ordinary is not null && _plan.Predicates is [{Lane:0,RangeMode:0,RangeValue:3,AnyCount:0,AllCount:3,BanCount:0}] &&
                _plan.LastRequiredBucket == 7 && _plan.TrackedInitialPositions.Length == 3;
            bool threeShop = referencePool && shop is not null && _plan.Predicates.Length == 0 && _plan.ShopPredicates is [{Count:3,OrderMode:0}] && _plan.ShopPredicates[0].TargetIds.Distinct().Count()==3;
            bool ordinaryWorkers = referencePool && ordinary is not null && _plan.Predicates.Length <= 5 && _plan.TrackedInitialPositions.Length <= 12;
            bool shopWorkers = referencePool && shop is not null && _plan.Predicates.Length <= 5 && _plan.ShopPredicates.Length <= 2 &&
                _plan.TrackedInitialPositions.Length <= 12 && Math.Max(_plan.PositiveDepthByLane[3], _plan.ExclusionDepthByLane[3]) <= 5;
            Func<ulong,bool> matcher = shop is not null ? shop.Matches : ordinary is not null ? ordinary.Matches : MatchesCpuReference;
            foreach(int workers in (ordinaryWorkers || shopWorkers ? new[]{1,2,4,8} : threeShop ? new[]{1,8} : new[]{1}).Where(w=>w<=_request.WorkerCount))
                yield return new FamilyCpuExecution(_request,this,shop is not null ? "R.Relic.Cpu.ShopBackPrefix.20260912.v1" : ordinary is not null ? "R.Relic.Cpu.TrackedPositions.20260912.v1" : "R.Relic.Cpu.Reference.20260912.v1",
                    matcher,workers,g => (common && workers == 1 ? FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"R.CommonFirst3All3",1,"498.773-510.142") :
                    threeShop && workers is 1 or 8 ? FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"R.ShopOrdered3",workers,workers==1?"232.418-232.635":"52.627-53.505") : null) ??
                    (workers == 1 && ordinary is not null ? RelicPhysicalPricing.QuoteOrdinaryModel(_request,_plan,_expectedFilteringCost,g,cpu:true) : null) ??
                    (workers == 1 && shop is not null ? RelicPhysicalPricing.QuoteShopModel(_request,_plan,g,cpu:true) : null));
        }
    }
}
