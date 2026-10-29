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
                    RelicWorkEstimate.Capsule(_request, route, _replay.Pool, _gpuPlan, g,
                        cpu: true, workers: workers, compiledMatcher: plan is not null));
                // Retain only the historical acquisition mean as diagnostic
                // calibration metadata. The estimate is not that measured band
                // and does not require an upstream N pass to own a valid quote.
                cpu.CalibrationMeanInput = band is null ? null : band=="SmallC" ? 65536d/17 : 65536d/10;
                yield return cpu;
            }
        }
    }
}
