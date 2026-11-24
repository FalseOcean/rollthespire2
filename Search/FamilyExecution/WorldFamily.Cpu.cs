using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class WorldFamily
{
    private static FilterBodyDescription PrepareCpu(PreparedFilter prepared, WorldFamilyReplay replay,
        WorldFamilyGpuPlan? gpu, IRuntimeProfile profile, FilterPricingAuthority authority, string identity, int workers)
    {
        bool numericalRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        string revision = CpuRevision + (replay.Plan.Acts.Any(a => a.HasEncounterPredicate) ? ".EncounterQueues.20261105.v1" : "");
        if (replay.Plan.UsesChildShuffle) revision += ".Modern113";
        return new(prepared, revision, profile.ProfileId)
        {
            WindowCapacity = 65536, CpuWorkers = workers, PlayersCount = authority.PlayersCount, InputIdentity = identity,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, 65536, numericalRoots, false, compact),
            Cost = new()
            {
                CanonicalScope = true,
                PartyChild = new(1 + Math.Max(0, replay.Plan.MaxRequiredAct - 2) * .5 + replay.Plan.Acts.Sum(a => a.EventPredicates.Length) * .25),
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeWorld(replay, null),
                Quote = (self, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(self, geometry) ??
                    (workers == 1 ? WorldPhysicalPricing.QuoteCpu(authority, replay, gpu, geometry) : null) ??
                    WorldWorkEstimate.Cpu(replay, workers, geometry),
                CalibrationQuote = geometry => (workers == 1 ? WorldPhysicalPricing.QuoteCpu(authority, replay, gpu, geometry) : null) ??
                    WorldWorkEstimate.Cpu(replay, workers, geometry)
            },
            CreateExecution = (self, _) => new CpuFilterExecution(self, profile, replay.Matches, numericalRoots)
        };
    }
}
