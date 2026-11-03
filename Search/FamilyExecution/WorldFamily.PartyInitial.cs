using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class WorldFamily
{
    internal static class PartyInitial
    {
        // Conservative pass-through: Exact reconstructs shared W once. This
        // description never substitutes solo inputs for uncaptured party state.
        internal static FilterBodyDescription Prepare(SearchModel model, FilterExecutionPreferences preferences)
        {
            const string revision = "W.World.PartyInitial.Cpu.v1";
            int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
            var profile = RuntimeProfileRegistry.Select(model.Detection);
            return new(new PreparedFilter(Id)
            {
                AnalyticalCost = new(Id, CpuFilterExecution.Capacity, [], "PartyInitialCostUncalibrated"),
                Survival = FamilySurvivalProjection.Unresolved(Id, "PartyCorrelationUnknown")
            }, revision, model.ProfileId)
            {
                WindowCapacity = CpuFilterExecution.Capacity, CpuWorkers = workers,
                PlayersCount = model.Authority.PlayersCount, InputIdentity = model.SnapshotFingerprint,
                PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers,
                    CpuFilterExecution.Capacity, true, false, compact),
                CreateExecution = (self, _) => new CpuFilterExecution(self, profile, root => true, numericalRoots: true)
            };
        }
    }
}
