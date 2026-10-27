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
            bool ordinarySequence = _request.Evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty);
            string compiledRevision = route.Bones
                ? ordinarySequence ? "R.Relic.Cpu.BonesCapsuleTargetsWithOrdinary.20261016.v4" : "R.Relic.Cpu.BonesCapsuleTargets.20261016.v3"
                : ordinarySequence ? "R.Relic.Cpu.CapsuleTargetsWithOrdinary.20261016.v3" : "R.Relic.Cpu.CapsuleTargets.20261013.v2";
            var band = _request.Evaluation.RequiredFinalCurse is not null || _request.Evaluation.BannedFinalCurses.Count != 0 ||
                _request.Evaluation.StructuredNeowEffects.Any(c=>!c.IsEmpty && !NeowReplayPlan.IsCapsule(c)) ? null : plan?.DirectPricingBand;
            foreach(int workers in FamilyCpuExecution.WorkerConfigurations(_request))
            {
                FamilyCpuExecution? cpu = null;
                cpu = new FamilyCpuExecution(_request,this,plan is null ? "R.Relic.Cpu.CapsuleAndSequenceReplay.20260905.v1" :
                    compiledRevision,
                    plan is null ? _replay.Matches : plan.Matches,workers,g =>
                    band is null || Math.Abs(g.MeanInputPopulation - (band=="SmallC" ? 65536d/17 : 65536d/10)) > .01 ? null :
                    FamilyCpuReferenceCost.Quote(_request,g,FamilyId,"R.Direct"+band,workers,
                        band=="SmallC"?"260.131-267.567":workers==1?"128.035-129.343":"53.902-55.005",compact:true,
                        cpuPhysicalRevision: cpu!.Condition(g.CompactInput).PhysicalImplementationRevision, cpuWindowCapacity: cpu.WindowCapacity));
                cpu.CalibrationMeanInput = band is null ? null : band=="SmallC" ? 65536d/17 : 65536d/10;
                cpu.RequiredPassedCoverage = new HashSet<string>{"N.Neow"};
                yield return cpu;
            }
        }
    }
}
