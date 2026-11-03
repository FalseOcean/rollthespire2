using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record MerchantShopColorlessPreparedBody(MerchantShopColorlessPreparation Inputs, FilterBodyDescription Body);

// S prepares its pristine Shops replay once. Selected execution handles own the
// partitioned CPU loop or the existing GPU resources and compaction algorithm.
internal static partial class MerchantShopColorlessFamily
{
    internal const string Id = "S.MerchantShopColorless";
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true)
    {
        bool accepted = TryPrepare(model, preferences, out var result, preferGpu);
        body = result?.Body;
        return accepted;
    }

    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out MerchantShopColorlessPreparedBody? result, bool preferGpu = true)
    {
        result = null;
        if (!model.Evaluation.RequiresMerchantColorlessDomain) return false;
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var authority = Beta111MerchantColorlessAuthority.From(model.Authority);
        if (profile.ProfileId != RuntimeProfileId.Beta111 || !authority.HasExactV1Inputs || authority.PlayersCount > 4) return false;
        int maximum = model.Evaluation.MerchantColorlessConditions.Select(c => c.MerchantOrdinal)
            .Concat(model.Evaluation.MerchantColorlessSequenceConditions.Select(c => c.Count)).DefaultIfEmpty(0).Max();
        if (maximum is < 1 or > Beta111NormalMerchantColorlessSequenceProjector.MerchantCount) return false;
        var inputs = MerchantShopColorlessPreparation.Prepare(model, preferGpu);
        var fullCost = MerchantShopColorlessAnalyticalCost.Project(inputs.Evaluation);
        var survival = MerchantShopColorlessSurvival.Project(model);
        var decision = MerchantShopColorlessCompactionPolicy.Select(survival);
        var expected = MerchantShopColorlessExpectedFilteringCost.Project(inputs.Evaluation, fullCost, authority, survival);
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = fullCost, Survival = survival, ExpectedFilteringCost = expected,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)],
            Issues = inputs.GpuPreparationFailure.Length == 0 ? [] :
                [new("GpuPreparation", "GpuPlanConstructionFailed", inputs.GpuPreparationFailure)]
        };
        string identity = model.SnapshotFingerprint;
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        var cpu = PrepareCpu(preparation, inputs, decision, profile, identity, workers);
        if (inputs.GpuPlan is not { } gpuPlan) { result = new(inputs, cpu); return true; }
        string suffix = gpuPlan.PerformanceSuffix;
        string denseRevision = (decision.Path == MerchantShopColorlessCompactionPath.StableOrderedCompaction
            ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessStableDensePerformance
            : FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicDensePerformance) + suffix;
        var dense = MerchantShopColorlessConditionPerformance.Project(inputs.Evaluation, denseRevision);
        var compact = MerchantShopColorlessConditionPerformance.Project(inputs.Evaluation,
            FamilyPhysicalImplementationRevisions.MerchantShopColorlessCompactPerformance + suffix);
        var body = new FilterBodyDescription(preparation, denseRevision, profile.ProfileId) {
            UsesGpu = true, NumericRequirements = GpuNumericRequirements.Int64,
            InputIdentity = identity, PlayersCount = authority.PlayersCount, CpuWorkers = preferences.WorkerCount,
            WindowCapacity = MerchantShopColorlessGpuExecutor.Capacity, PreferredRootWindowSize = MerchantShopColorlessGpuExecutor.Capacity, CpuAlternatives = [cpu],
            PerformanceProjection = isCompact => isCompact ? compact : dense,
            Cost = new() {
                MinimumComparableInput = compactInput => compactInput ? FamilyPerformanceEvidenceEligibility.MinimumDurableCompactInput(MerchantShopColorlessGpuExecutor.CompactInputCapacity) : MerchantShopColorlessGpuExecutor.Capacity,
                PartyChild = new(ShopPrefixChanged: suffix.Length > 0),
                CanonicalScope = authority.PlayersCount == 1,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeShop(inputs.Evaluation, authority),
                PrivatePhysicalRevision = privateInput => (privateInput
                    ? FamilyPhysicalImplementationRevisions.MerchantShopColorlessCompactPerformance
                    : FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicDensePerformance) + suffix,
                PrivateTransportPath = "AtomicAppendHostSort",
                PrivateBodyRevision = FamilyPhysicalImplementationRevisions.MerchantShopColorlessAtomicAppendHostSort + suffix,
                Quote = (selected, geometry, _) => FamilyGpuReadback.Reference(selected,
                    FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                        MerchantShopColorlessPhysicalPricing.Quote(inputs, decision, geometry, expected))
            },
            CreateExecution = (selected, _) => new GpuFilterExecution<MerchantShopColorlessGpuExecutor>(selected,
                device => MerchantShopColorlessGpuExecutor.Create(device, gpuPlan, decision.Path),
                (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalAbi1ReadyMs, metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs,
                        metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), inputs.Reference, profile, resource => (resource.DeviceName, resource.SetupMs), 4096),
            BindPrivate = (device, input, output) => {
                var path = input is null && output is null ? decision.Path : MerchantShopColorlessCompactionPath.AtomicAppendHostSort;
                var resource = MerchantShopColorlessGpuExecutor.Create(device, gpuPlan, path, input, output);
                return new(resource, resource.DeviceName, resource.SetupMs, input, output, (batch, count, token) => {
                    FamilyCandidateSet? filtered = null; MerchantShopColorlessGpuBatchMetrics metrics;
                    if (output is not null) resource.ExecutePrivateStage(batch, count, token, out metrics);
                    else filtered = input is null ? resource.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                        resource.ExecutePrivate(batch, count, token, out metrics);
                    return new(metrics.Survivors, filtered, metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs,
                        metrics.ReadbackMs, metrics.ReadbackBytes, metrics.CanonicalAbi1ReadyMs, metrics.Dispatches);
                });
            }
        };
        result = new(inputs, body);
        return true;
    }
}
