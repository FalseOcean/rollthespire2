using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class RelicFamily
{
    private static FilterBodyDescription PrepareCpu(PreparedFilter preparation, RelicFamilyPlan plan,
        RelicReferencePlan reference, FilterPricingAuthority authority, IRuntimeProfile profile, string identity, int workers)
    {
        var ordinary = RelicCpuPlan.TryCompile(plan);
        var shop = RelicShopCpuPlan.TryCompile(plan);
        bool referencePool = authority.PlayersCount == 1 && plan.Pool.BucketLengths.SequenceEqual(new[] { 30, 25, 35, 25, 1, 2, 32, 26, 38, 26 });
        bool common = referencePool && ordinary is not null && plan.Predicates is [{ Lane: 0, RangeMode: 0, RangeValue: 3, AnyCount: 0, AllCount: 3, BanCount: 0 }] &&
            plan.LastRequiredBucket == 7 && plan.TrackedInitialPositions.Length == 3;
        bool threeShop = referencePool && shop is not null && plan.Predicates.Length == 0 &&
            plan.ShopPredicates is [{ Count: 3, OrderMode: 0 }] && plan.ShopPredicates[0].TargetIds.Distinct().Count() == 3;
        string revision = shop is not null ? "R.Relic.Cpu.ShopBackPrefix.20261016.v2" : ordinary is not null
            ? "R.Relic.Cpu.TrackedPositions.20261016.v3" : "R.Relic.Cpu.Reference.20260912.v1";
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        FamilyConditionPerformanceProjection Condition(bool compact) => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact);
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? Raw(FamilyPhysicalQuoteRequest geometry) =>
            (common && workers == 1 ? FamilyCpuReferenceCost.Quote(authority, geometry, Id, "R.CommonFirst3All3", 1, "254.912-257.253",
                cpuPhysicalRevision: Condition(geometry.CompactInput).PhysicalImplementationRevision, cpuWindowCapacity: 65536) :
             threeShop && workers is 1 or 8 ? FamilyCpuReferenceCost.Quote(authority, geometry, Id, "R.ShopOrdered3", workers,
                workers == 1 ? "232.418-232.635" : "52.627-53.505", cpuPhysicalRevision: Condition(geometry.CompactInput).PhysicalImplementationRevision,
                cpuWindowCapacity: 65536) : null) ??
            RelicWorkEstimate.Ordinary(plan, geometry, preparation.ExpectedFilteringCost, cpu: true, workers: workers,
                referenceMatcher: ordinary is null && shop is null);
        return new(preparation, revision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = authority.PlayersCount,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
            PerformanceProjection = Condition,
            Cost = new() {
                CanonicalScope = authority.PlayersCount == 1, CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeRelic(plan),
                CalibrationQuote = Raw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (Raw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile,
                shop is not null ? shop.Matches : ordinary is not null ? ordinary.Matches : CreateReferenceMatcher(reference), numericalRoots)
        };
    }
}
