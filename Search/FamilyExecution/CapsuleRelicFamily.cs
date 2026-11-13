using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record CapsuleRelicPreparation(NeowReplayPlan Route, CapsuleRelicReplay Replay,
    RelicFullGpuPlan? GpuPlan, FilterBodyDescription Body);

// R owns the shared initial bag and its source-local Capsule observations. These
// immutable replays are numerical inputs; N never owns their bag state.
internal static partial class CapsuleRelicFamily
{
    private const string Id = "R.Relic";
    private static readonly IReadOnlySet<string> EmptyCoverage = new HashSet<string>(StringComparer.Ordinal);
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }
    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out CapsuleRelicPreparation? prepared, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        prepared = null;
        if (!NeowReplayPlan.HasCapsule(model) || model.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113)
            return false; // Production Exact owns 0.113 Capsule predicates until the combined R body replays the new opening.
        try
        {
            var route = NeowReplayPlan.Compile(model, true);
            bool openingPrefix = RelicFullGpuPlan.CaptureOpeningPrefixSelection();
            string issue = route.ContinuationIssue(capsule: true);
            if (issue.Length > 0 && openingPrefix && model.Authority.PlayersCount == 1 &&
                RelicOpeningPrefixPlan.TryCompile(route, out _, out _)) issue = "";
            if (issue.Length > 0)
            {
                if (RuntimeLog.SessionId.Length > 0)
                    RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("CapsuleFilterUnavailable", nameof(CapsuleRelicFamily),
                        "Filter.Construction", "issues.filter_incomplete", $"cause={issue};predicatesRetained=true;finalValidationSkipped={preferences.SkipExactValidation};matchClaimed=false");
                return false;
            }
            if (!RelicFamilyPlanCompiler.TryCompilePool(model, out _, out _, out _, out string poolIssue))
            {
                RuntimeLog.TryBackgroundWarning("rCapsuleFamilyUnavailable=true;cause=" + poolIssue + ";predicatesRetained=true;replacementCompleteness=NotClaimed");
                return false;
            }
            prepared = Prepare(model, preferences, route, preferGpu, numericalVariant, openingPrefix);
            return true;
        }
        catch (InvalidOperationException error) when (error.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity" or
            "RFamilyUnboundCapsuleRoute_ExactOnly" or "RFamilyCapsulePredicateUnsupported_ExactOnly")
        {
            RuntimeLog.TryBackgroundWarning("rCapsuleFamilyUnavailable=true;predicatesRetained=true;replacementCompleteness=NotClaimed;exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
            return false;
        }
    }
    private static CapsuleRelicPreparation Prepare(SearchModel model, FilterExecutionPreferences preferences,
        NeowReplayPlan route, bool preferGpu, GpuNumericalVariant numericalVariant, bool openingPrefix)
    {
        var replay = new CapsuleRelicReplay(model, route);
        var projections = new NeowFamilyProjections(model, route, true, replay.Pool);
        RelicFullGpuPlan? gpu = null;
        string issue = "CpuReferenceSelected", failure = "";
        if (preferGpu)
        {
            try { RelicFullGpuPlan.TryCreate(model, route, out gpu, out issue, numericalVariant, openingPrefix); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { issue = "GpuPlanConstructionFailed"; failure = error.ToString(); }
        }
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = projections.Full, Survival = projections.Survival(EmptyCoverage),
            ExpectedFilteringCost = projections.Expected(EmptyCoverage), HasConditionalProjections = true,
            SurvivalResolver = projections.Survival, ExpectedCostResolver = projections.Expected,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)],
            Issues = failure.Length == 0 ? [] : [new("GpuPreparation", issue, failure)]
        };
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        var evaluation = RelicReferencePlan.ProjectEvaluation(model);
        var pricingAuthority = FilterPricingAuthority.From(model);
        var conditions = model.Evaluation.StructuredNeowEffects.ToArray();
        string identity = model.SnapshotFingerprint;
        var cpu = PrepareCpu(model, preparation, replay, route, gpu, evaluation, conditions, profile, identity,
            Math.Clamp(preferences.WorkerCount, 1, 32));
        if (gpu is null) return new(route, replay, null, cpu);
        var gpuPlan = gpu;
        var body = new FilterBodyDescription(preparation, gpuPlan.Revision(false), profile.ProfileId) {
            UsesGpu = true, NumericRequirements = gpuPlan.NumericRequirements,
            InputIdentity = identity, PlayersCount = model.Authority.PlayersCount, CpuWorkers = preferences.WorkerCount,
            WindowCapacity = RelicFamilyGpuExecutor.Capacity, CpuAlternatives = [cpu],
            PerformanceProjection = compact => new(Id, gpuPlan.Revision(compact),
                "Rfull.ConditionPerformance.Neutral.CanonicalBoundary.20260906.v4" + gpuPlan.NumericalRevision, 1,
                "NoAcceptedWithinPathCurve;timingBoundary=CanonicalAbi1Ready;independentRfullIdentity=true" +
                (gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne
                    ? ";OperationEstimateAvailable;NoIntegerBodyMeasurement;HistoricalGlobalNextIntPreserved" : "")),
            Cost = new() {
                PartyChild = new(AllowsHistoricalEnvelope: gpuPlan.NumericalVariant != GpuNumericalVariant.IntegerRne,
                    NumericalRevision: gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ".IntegerRne.20261025.v1" : "",
                    CapacityRevision: gpuPlan.BagCapacityRevision),
                CanonicalScope = pricingAuthority.PlayersCount == 1,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeCapsule(route, replay.Pool, gpuPlan, conditions),
                MinimumComparableInput = compact => compact ? RelicFamilyGpuExecutor.CompactInputCapacity / 4 : RelicFamilyGpuExecutor.Capacity,
                Quote = (selected, geometry, _) => FamilyGpuReadback.Reference(selected,
                    FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    RelicPhysicalPricing.QuoteSourceCapsules(pricingAuthority, gpuPlan, geometry) ??
                    RelicWorkEstimate.Capsule(evaluation, route, replay.Pool, gpuPlan, geometry, cpu: false))
            },
            CreateExecution = (selected, _) => new GpuFilterExecution<RelicFamilyGpuExecutor>(selected,
                device => RelicFamilyGpuExecutor.Create(device, identity, gpuPlan), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalAbi1ReadyMs, metrics.CommandMs + metrics.SubmitMs + metrics.SyncMs,
                        metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), replay.Matches, profile, resource => (resource.DeviceName, resource.SetupMs), 256,
                faultCleanupStage: "CapsuleGpuFault.Dispose"),
            BindPrivate = gpuPlan.NumericalVariant != GpuNumericalVariant.NativeFloat64 ? null :
                (device, input, output) => RelicFamilyGpuExecutor.Create(device, identity, gpuPlan, input, false, output).BindPrivateSerial()
        };
        return new(route, replay, gpuPlan, body);
    }
}
