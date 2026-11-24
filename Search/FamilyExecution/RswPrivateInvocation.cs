using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class RswPrivateInvocation
{
    internal static string Name(char value) => value switch { 'R' => "R.Relic", 'S' => "S.MerchantShopColorless", 'W' => "W.World", _ => throw new InvalidOperationException("RSW.Coverage") };
    internal static FilterBodyDescription Prepare(SearchModel model, string identity, MerchantShopColorlessGpuPreparation s, RelicFamilyPlan r, WorldFamilyGpuPlan w, string order, bool verify)
    {
        string id = Name(order[0]);
        string revision = "RSW." + order + ".PrivateSerial.CanonicalAbi1Ready.20260908.v1" + GpuRngRotation.Revision;
        var preparation = new PreparedFilter(id) {
            Coverage = order.Select(Name).ToArray(),
            AnalyticalCost = new(id, PrivateOrdinalBuffer.Capacity, [], "RswPrivateInvocation.ExistingWholeKernelUnpriced"),
            Survival = FamilySurvivalProjection.Unresolved(id, "RswPrivateInvocation.JointUnpriced")
        };
        return new(preparation, revision, model.ProfileId) {
            UsesGpu = true, InputModes = FilterInputModes.Dense, InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount, WindowCapacity = PrivateOrdinalBuffer.Capacity,
            NumericRequirements = GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64,
            PerformanceProjection = _ => new(id, revision, "ExplicitNeutral", 1, "ExistingExplicitNumericalUnit", true),
            CreateExecution = (body, _) => new GpuExperimentExecution<RswPrivateGpuExecutor>(body,
                device => new(device, identity, s, r, w, order, verify), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var ms);
                    return new(output, ms);
                }, resource => resource.Dispose(), resource => (resource.Device, resource.SetupMs), verify)
        };
    }
}
