using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class AncientOptionFamily
{
    private static FilterBodyDescription PrepareCpu(PreparedFilter preparation, AncientOptionFamilyPlan plan,
        AncientOptionPricingInput pricing, IRuntimeProfile profile, string identity, int workers)
    {
        const string revision = "A.AncientOption.Cpu.EventLocal.20261016.v3";
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? Raw(FamilyPhysicalQuoteRequest geometry) => AncientOptionPhysicalPricing.QuoteCpu(pricing, plan, geometry, workers);
        return new(preparation, revision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = pricing.Authority.PlayersCount,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact),
            Cost = new() {
                PartyChild = new(WorkUnits: 1),
                CanonicalScope = pricing.Authority.PlayersCount == 1,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeAncient(pricing.Evaluation, plan),
                CalibrationQuote = Raw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (Raw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile, CreateMatcher(plan), numericalRoots)
        };
    }
}
