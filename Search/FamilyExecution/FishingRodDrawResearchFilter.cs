using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class FishingRodDrawResearchFilter
{
    internal static FilterBodyDescription Prepare(SearchModel model, FilterExecutionPreferences preferences)
    {
        const string id = FishingRodDrawResearch.Id;
        const string revision = id + ".Cpu.Modern113.v2";
        const int capacity = 65536;
        var rows = model.CompiledSearch.NormalizedQuery.FishingRodDrawRequirements ?? FishingRodDrawResearch.DefaultRequirements;
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        var preparation = new PreparedFilter(id)
        {
            Survival = FamilySurvivalProjection.Unresolved(id, "FishingRodDrawResearch.JointSurvivalUnmeasured")
        };
        return new(preparation, revision, model.ProfileId)
        {
            InputIdentity = model.SnapshotFingerprint,
            WindowCapacity = capacity,
            CpuChunkCapacity = CpuFilterExecution.Capacity,
            CpuWorkers = workers,
            PerformanceProjection = compact => CpuFilterExecution.Describe(id, revision,
                workers, capacity, numericalRoots: true, packedRoots: false, compact: compact),
            CreateExecution = (body, _) => new CpuFilterExecution(body, Beta111Profile.Instance,
                root => FishingRodDrawResearch.Matches(root, rows), numericalRoots: true)
        };
    }
}
