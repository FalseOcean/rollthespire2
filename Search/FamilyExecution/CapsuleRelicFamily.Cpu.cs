using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class CapsuleRelicFamily
{
    private static FilterBodyDescription PrepareCpu(SearchModel model, PreparedFilter preparation,
        CapsuleRelicReplay replay, NeowReplayPlan route, RelicFullGpuPlan? gpu,
        ExactSearchEvaluationProjection evaluation, IReadOnlyList<NeowStructuredEffectSearchCondition> conditions,
        IRuntimeProfile profile, string identity, int workers)
    {
        var plan = CapsuleRelicCpuPlan.TryCompile(model, route);
        bool ordinarySequence = evaluation.RelicSequenceConditions.Any(c => !c.IsEmpty);
        string revision = plan is null ? "R.Relic.Cpu.CapsuleAndSequenceReplay.20260905.v1" : route.Bones
            ? ordinarySequence ? "R.Relic.Cpu.BonesCapsuleTargetsWithOrdinary.20261016.v4" : "R.Relic.Cpu.BonesCapsuleTargets.20261016.v3"
            : ordinarySequence ? "R.Relic.Cpu.CapsuleTargetsWithOrdinary.20261016.v3" : "R.Relic.Cpu.CapsuleTargets.20261013.v2";
        var band = model.Evaluation.RequiredFinalCurse is not null || model.Evaluation.BannedFinalCurses.Count != 0 ||
            conditions.Any(c => !c.IsEmpty && !NeowReplayPlan.IsCapsule(c)) ? null : plan?.DirectPricingBand;
        double? meanInput = band is null ? null : band == "SmallC" ? 65536d / 17 : 65536d / 10;
        int players = model.Authority.PlayersCount;
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? Raw(FamilyPhysicalQuoteRequest geometry) => RelicWorkEstimate.Capsule(evaluation, route, replay.Pool,
            gpu, geometry, cpu: true, workers: workers, compiledMatcher: plan is not null);
        return new(preparation, revision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = players,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact),
            Cost = new() {
                CalibrationRequiredPassedCoverage = meanInput.HasValue ? new HashSet<string>(["N.Neow"], StringComparer.Ordinal) : null,
                CanonicalScope = players == 1,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeCapsule(route, replay.Pool, gpu, conditions), CalibrationQuote = Raw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (Raw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile,
                plan is null ? replay.Matches : plan.Matches, numericalRoots) { CalibrationMeanInput = meanInput }
        };
    }
}
