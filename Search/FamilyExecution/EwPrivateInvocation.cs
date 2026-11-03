using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class EwPrivateInvocation
{
    internal static FilterBodyDescription Prepare(SearchModel model, EventResultFamilyPlan e, WorldFamilyGpuPlan w, string mode)
    {
        string id = "E.EventResult";
        string revision = "E.EventResult.Composite.W.World.PrivateOrdinal.CanonicalAbi1Ready.20260908.v2" + GpuRngRotation.Revision;
        var preparation = new PreparedFilter(id) {
            Coverage = ["E.EventResult", "W.World"],
            AnalyticalCost = new(id, EwPrivateGpuExecutor.MaxRoots, [], "EwPrivateInvocation.ExistingWholeKernelUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(id, "EwPrivateInvocation.JointUnpriced")
        };
        return new(preparation, revision, model.ProfileId) {
            UsesGpu = true, InputModes = FilterInputModes.Dense, InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount, WindowCapacity = EwPrivateGpuExecutor.MaxRoots,
            NumericRequirements = GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64,
            PerformanceProjection = _ => new(id, revision, "ExplicitNeutral", 1, "ExistingExplicitNumericalUnit", true),
            CreateExecution = (body, _) => new GpuExperimentExecution<EwPrivateGpuExecutor>(body,
                device => new(device, e, w, mode.StartsWith("verify-", StringComparison.Ordinal)), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var ms);
                    return new(output, ms);
                }, resource => resource.Dispose(), resource => (resource.Device, resource.SetupMs), mode.StartsWith("verify-", StringComparison.Ordinal))
        };
    }
}
