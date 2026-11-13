using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record TransformationAggregatePreparation(TransformationAggregateNumericalPlan Plan,
    TransformationAggregateGpuPlan GpuPlan, TransformationAggregateProbability Probability, FilterBodyDescription Body);

internal static class TransformationAggregateFamily
{
    internal const string Id = "T.TransformationAggregate";
    internal const string CpuRevision = "T.Cpu.InitialBasicsAggregate.20261016.v4" + NeowIdentityOfferFacts.Revision;
    internal static bool TryCreate(SearchModel model, FilterExecutionPreferences preferences,
        out FilterBodyDescription? body, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        bool accepted = TryPrepare(model, preferences, out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }
    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out TransformationAggregatePreparation? prepared, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        prepared = null;
        if (model.Evaluation.TransformationAggregate is null) return false;
        try { prepared = Prepare(model, preferences, preferGpu, numericalVariant); return true; }
        catch (InvalidOperationException error) when (error.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity")
        {
            RuntimeLog.TryBackgroundWarning("transformationFilterUnavailable=true;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed;exceptionJson=" + RuntimeLog.SafeJson(error.ToString()));
            if (RuntimeLog.SessionId.Length > 0)
                RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("TransformationFilterUnavailable", nameof(TransformationAggregateFamily),
                    "Filter.Construction", "issues.filter_incomplete", $"cause={error};predicatesRetained=true;finalValidationSkipped={preferences.SkipExactValidation};matchClaimed=false");
            return false;
        }
    }
    private static TransformationAggregatePreparation Prepare(SearchModel model, FilterExecutionPreferences preferences,
        bool preferGpu, GpuNumericalVariant numericalVariant)
    {
        var plan = new TransformationAggregateNumericalPlan(model);
        var work = TransformationAggregateWorkEstimate.Describe(plan);
        var probability = FamilyAuxiliaryEvidence.Read(Id, "InitialProbability", () => TransformationAggregateProbability.Build(plan),
            new TransformationAggregateProbability(null, null, double.NaN, double.NaN, [], "AuxiliaryProbabilityFailed"), model.SnapshotFingerprint);
        bool sharedNeow = TransformationAggregateCondition.HasSharedNeow(model.Evaluation);
        bool identityOnly = TransformationAggregateCondition.SharedNeowIdentityOnly(model.Evaluation);
        double? givenNeow = ResolveConditionalProbability(model, plan, sharedNeow);
        var gpu = new TransformationAggregateGpuPlan();
        string failure = "";
        if (preferGpu)
        {
            try { gpu = new(plan, numericalVariant); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failure = error.ToString(); }
        }
        var survival = probability.StageSurvival is { } p
            ? FamilySurvivalProjection.Resolved(Id, p, probability.Evidence) : FamilySurvivalProjection.Unresolved(Id, probability.Evidence);
        FamilySurvivalProjection Resolve(IReadOnlySet<string> passed) => !sharedNeow || !passed.Contains("N.Neow") ? survival :
            identityOnly && probability.StageSurvival is { } stageProbability
                ? FamilySurvivalProjection.Resolved(Id, probability.IdentityProbability > 0 ? stageProbability / probability.IdentityProbability : 0,
                    "T.GivenSharedNIdentity;CountOpeningOnce")
                : givenNeow is { } conditional ? FamilySurvivalProjection.Resolved(Id, conditional, "T.GivenN;ExistingSameDrawJointOverNMarginal")
                : FamilySurvivalProjection.Unresolved(Id, "T+N.AdditionalPredicateJointUnknown");
        var preparation = new PreparedFilter(Id) {
            AnalyticalCost = new(Id, gpu.Capacity, [], "T.OwnedNumericalWork"), Survival = survival,
            HasConditionalProjections = sharedNeow, SurvivalResolver = Resolve,
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)],
            Issues = failure.Length == 0 ? [] : [new("GpuPreparation", "GpuPlanConstructionFailed", failure)]
        };
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        int players = model.Authority.PlayersCount;
        string identity = model.SnapshotFingerprint;
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        bool packedRoots = profile.ProfileId == RuntimeProfileId.Beta111;
        var costs = FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null);
        FamilyPhysicalQuote? CpuRaw(FamilyPhysicalQuoteRequest geometry) => TransformationAggregateWorkEstimate.Cpu(plan, probability, work, geometry, workers);
        string cpuRevision = CpuRevision + plan.RuleRevision;
        var cpu = new FilterBodyDescription(preparation, cpuRevision, profile.ProfileId) {
            InputIdentity = identity, PlayersCount = players, CpuWorkers = workers,
            WindowCapacity = 65536, CpuChunkCapacity = CpuFilterExecution.Capacity,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, cpuRevision, workers, 65536, packedRoots, packedRoots, compact),
            Cost = new() {
                CanonicalScope = players == 1 && !plan.UsesModern113, CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeTransform(plan, null),
                CalibrationQuote = CpuRaw,
                Quote = (selected, geometry, _) => FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                    (CpuRaw(geometry) is { } raw ? costs?.Local(raw,
                        selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision, workers, geometry.CompactInput) ?? raw : null)
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile, plan.Matches, packedRoots, packedRoots)
        };
        if (!gpu.Supported) return new(plan, gpu, probability, cpu);
        var gpuPlan = gpu;
        var body = new FilterBodyDescription(preparation, gpuPlan.Revision(false), profile.ProfileId) {
            UsesGpu = true, NumericRequirements = gpuPlan.NumericRequirements,
            InputIdentity = identity, PlayersCount = players, CpuWorkers = preferences.WorkerCount,
            WindowCapacity = gpuPlan.Capacity, CpuAlternatives = [cpu],
            PerformanceProjection = compact => new(Id, gpuPlan.Revision(compact), "T.Gpu.Neutral.20260914.v1" + gpuPlan.NumericalRevision,
                1, "FamilyOwned;LocalAggregate;NoLeafPredicate" +
                (gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne ? ";IntegerRne;NoNativeFloat64PriceReuse" : ""), true),
            Cost = new() {
                CanonicalScope = players == 1 && !plan.UsesModern113, CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeTransform(plan, gpuPlan),
                MinimumComparableInput = _ => gpuPlan.Capacity,
                ObservationRevision = geometry => !geometry.PrivateInput && !geometry.PrivateOutput ? FamilyGpuReadback.TransformationPublicBoundaryRevision : "",
                PublicBoundaryToken = "MeasuredTransformationPublicBoundaryRevision=" + FamilyGpuReadback.TransformationPublicBoundaryRevision,
                PublicBoundaryMeasuredMarker = "CurrentTransformationPublicInvokeHostTransportMeasured",
                Quote = (selected, geometry, _) => {
                    var quote = FamilyCanonicalMeasuredCost.TryDenseQuote(selected, geometry) ??
                        TransformationAggregateWorkEstimate.Gpu(plan, probability, gpuPlan, work, geometry);
                    if (gpuPlan.EntryIdentityTracked && quote is not null)
                        quote = quote with { Evidence = quote.Evidence + ";EntryIdentityRetainedNumericalReference;NoNewEntryBodyMeasurement;NoAssumedIdentitySpeedRatio" };
                    return FamilyGpuReadback.Reference(selected, quote);
                }
            },
            CreateExecution = (selected, _) => new GpuFilterExecution<TransformationAggregateGpuExecutor>(selected,
                device => new(device, gpuPlan), (resource, input, token) => {
                    var output = resource.Execute(input, token, out var metrics);
                    return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.ReadbackBytes);
                }, resource => resource.Dispose(), plan.Matches, profile, resource => (resource.Device, resource.SetupMs)),
            BindPrivate = gpuPlan.NumericalVariant != GpuNumericalVariant.NativeFloat64 ? null : (device, input, output) => {
                var resource = new TransformationAggregateGpuExecutor(device, gpuPlan, input, output);
                return new(resource, resource.Device, resource.SetupMs, input, output, (batch, count, token) => {
                    FamilyCandidateSet? filtered = null; TransformationAggregateGpuMetrics metrics;
                    if (output is not null) resource.ExecutePrivateStage(batch, count, token, out metrics);
                    else filtered = input is null ? resource.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                        resource.ExecutePrivate(batch, count, token, out metrics);
                    return new(metrics.Output, filtered, metrics.DispatchSyncMs, metrics.ReadbackMs,
                        metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches);
                });
            }
        };
        return new(plan, gpuPlan, probability, body);
    }

    // Keep model-only preparation delegates outside the body factory's closure.
    private static double? ResolveConditionalProbability(SearchModel model, TransformationAggregateNumericalPlan plan, bool sharedNeow) =>
        plan.Closed && plan.Neow is not null && sharedNeow
            ? FamilyAuxiliaryEvidence.Read<double?>(Id, "ConditionalProbability", () => TransformationAggregateProbability.Conditional(
                TransformationAggregateProbability.SharedNeowJoint(model, plan),
                new NeowFamilyProjections(model, NeowReplayPlan.Compile(model, false), false, null).Survival(new HashSet<string>()).SurvivalProbability),
                null, model.SnapshotFingerprint) : null;
}
