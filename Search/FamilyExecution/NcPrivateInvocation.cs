using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NcPrivateInvocation
{
    internal static FilterBodyDescription Prepare(SearchModel model, CombatRewardGpuPlan c, NeowFamilyGpuPlan n, string mode,
        IReadOnlyList<string> remainders)
    {
        uint tag = (uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(model.SnapshotFingerprint, 0);
        string id = mode.EndsWith("-nc", StringComparison.Ordinal) ? "N.Neow" : "C.CombatReward";
        string revision = (mode.EndsWith("-nc", StringComparison.Ordinal) ? "N.Neow.Composite.C.CombatReward." : "C.CombatReward.Composite.N.Neow.") + "PrivateOrdinal.CanonicalAbi1Ready.20260908.v2.C=" + c.Revision(mode.EndsWith("-nc", StringComparison.Ordinal));
        var preparation = new PreparedFilter(id) {
            Coverage = ["C.CombatReward", "N.Neow"],
            Remainders = remainders,
            AnalyticalCost = new(id, PrivateOrdinalBuffer.Capacity, [], "NcPrivateInvocation.ExistingWholeKernelUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(id, "NcPrivateInvocation.JointUnpriced")
        };
        return new(preparation, revision, model.ProfileId) {
            UsesGpu = true, InputModes = FilterInputModes.Dense, InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount, WindowCapacity = PrivateOrdinalBuffer.Capacity,
            NumericRequirements = GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64,
            PerformanceProjection = _ => new(id, revision, "ExplicitNeutral", 1, "ExistingExplicitNumericalUnit", true),
            CreateExecution = (body, _) => new GpuExperimentExecution<NcPrivateGpuExecutor>(body,
                device => new(device, c, n, tag, mode.StartsWith("verify-", StringComparison.Ordinal), mode.EndsWith("-nc", StringComparison.Ordinal)), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var ms);
                    return new(output, ms);
                }, resource => resource.Dispose(), resource => (resource.Device, resource.SetupMs), mode.StartsWith("verify-", StringComparison.Ordinal))
        };
    }
}
