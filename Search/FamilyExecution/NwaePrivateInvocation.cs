using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NwaePrivateInvocation
{
    internal static string Name(char value) => value switch { 'N' => "N.Neow", 'A' => "A.AncientOption", 'E' => "E.EventResult", 'W' => "W.World", _ => throw new InvalidOperationException("NWAE.Coverage") };
    internal static FilterBodyDescription Prepare(SearchModel model, NeowFamilyGpuPlan? n, AncientOptionFamilyPlan? a, EventResultFamilyPlan? e, WorldFamilyGpuPlan w, string order, bool verify,
        IReadOnlyList<string> remainders)
    {
        string id = Name(order[0]);
        string revision = "NWAE." + order + ".PrivateSerial.CanonicalAbi1Ready.20260908.v1" + GpuRngRotation.Revision;
        var preparation = new PreparedFilter(id) {
            Coverage = order.Select(Name).ToArray(),
            Remainders = remainders,
            AnalyticalCost = new(id, PrivateOrdinalBuffer.Capacity, [], "NwaePrivateInvocation.ExistingWholeKernelUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(id, "NwaePrivateInvocation.JointUnpriced")
        };
        return new(preparation, revision, model.ProfileId) {
            UsesGpu = true, InputModes = FilterInputModes.Dense, InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount, WindowCapacity = PrivateOrdinalBuffer.Capacity,
            NumericRequirements = GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64,
            PerformanceProjection = _ => new(id, revision, "ExplicitNeutral", 1, "ExistingExplicitNumericalUnit", true),
            CreateExecution = (body, _) => new GpuExperimentExecution<NwaePrivateGpuExecutor>(body,
                device => new(device, n, a, e, w, order, verify), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var ms);
                    return new(output, ms);
                }, resource => resource.Dispose(), resource => (resource.Device, resource.SetupMs), verify)
        };
    }
}
