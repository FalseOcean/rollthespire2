using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class EventResultFamily
{
    private static FilterBodyDescription PrepareCpu(PreparedFilter preparation, EventResultFamilyPlan plan,
        EventResultCpuPlan? compiled, FilterPricingAuthority authority, IRuntimeProfile profile, string identity, int workers)
    {
        string revision = compiled is not null ? "E.EventResult.Cpu.CompiledColor.20261015.v3" :
            plan.HasNewWhitelist ? "E.EventResult.Cpu.Whitelist.20261016.v4" :
            plan.HasMorphic ? "E.EventResult.Cpu.GroupInitialBasicsContains.20261016.v4" : "E.EventResult.Cpu.EventLocal.20261016.v4";
        revision += plan.Authority.UsesChildShuffle ? ".Modern113" : "";
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? Raw(FamilyPhysicalQuoteRequest geometry) => (workers == 1 && !plan.Authority.UsesChildShuffle
            ? compiled is null ? EventResultPhysicalPricing.QuoteCpu(authority, plan, geometry) :
                EventResultPhysicalPricing.QuoteCpuCompiledColor(authority, plan, compiled, geometry) : null) ??
            EventResultWorkEstimate.Cpu(plan, workers, geometry, compiled);
        return new(preparation, revision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = authority.PlayersCount,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact),
            Cost = new() {
                PartyChild = new(WorkUnits: 1, AllowsHistoricalEnvelope: !plan.Authority.UsesChildShuffle),
                CanonicalScope = authority.PlayersCount == 1 && !plan.Authority.UsesChildShuffle,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeEvent(plan, compiled?.AllowedRemovalMask),
                CalibrationQuote = Raw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (Raw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile,
                compiled is null ? CreateMatcher(plan) : compiled.Matches, numericalRoots)
        };
    }
}
