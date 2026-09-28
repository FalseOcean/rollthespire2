using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Semantics;
namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class CapsuleRelicFamily
{
    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            var route = NeowReplayPlan.Compile(_request,true);
            var plan = CapsuleRelicCpuPlan.TryCompile(_request,route);
            var band = _request.Evaluation.RequiredFinalCurse is not null || _request.Evaluation.BannedFinalCurses.Count != 0 ||
                _request.Evaluation.StructuredNeowEffects.Any(c=>!c.IsEmpty && !NeowReplayPlan.IsCapsule(c)) ? null : plan?.DirectPricingBand;
            foreach(int workers in (band == "LargeUR" ? new[]{1,8} : new[]{1}).Where(w=>w<=_request.WorkerCount))
            {
                var cpu = new FamilyCpuExecution(_request,this,plan is null ? "R.Relic.Cpu.CapsuleAndSequenceReplay.20260905.v1" : "R.Relic.Cpu.CapsuleTargets.20260912.v1",
                    plan is null ? _replay.Matches : plan.Matches,workers,g =>
                    band is null || Math.Abs(g.MeanInputPopulation - (band=="SmallC" ? 65536d/17 : 65536d/10)) > .01 ? null :
                    FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"R.Direct"+band,workers,
                        band=="SmallC"?"573.474-604.364":workers==1?"238.782-258.854":"94.418-105.237",compact:true));
                cpu.CalibrationMeanInput = band is null ? null : band=="SmallC" ? 65536d/17 : 65536d/10;
                cpu.RequiredPassedCoverage = new HashSet<string>{"N.Neow"};
                yield return cpu;
            }
        }
    }
}
