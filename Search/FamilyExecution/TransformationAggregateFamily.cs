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
        if (model.Evaluation.TransformationAggregate is { UsesExtendedRequirements: true })
        {
            body = PrepareScoped(model, preferences, preferGpu, numericalVariant);
            return true;
        }
        bool accepted = TryPrepare(model, preferences, out var prepared, preferGpu, numericalVariant);
        body = prepared?.Body;
        return accepted;
    }
    internal static bool TryPrepare(SearchModel model, FilterExecutionPreferences preferences,
        out TransformationAggregatePreparation? prepared, bool preferGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        prepared = null;
        if (model.Evaluation.TransformationAggregate is null or { UsesExtendedRequirements: true }) return false;
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

    // GPU bodies here evaluate only their seed-local numerical obligation.
    private static FilterBodyDescription PrepareScoped(SearchModel model, FilterExecutionPreferences preferences,
        bool preferGpu, GpuNumericalVariant numericalVariant)
    {
        var numerical = TransformationAncientNumericalPlan.TryCreate(model);
        TransformationAggregateGpuPlan? gpu = null;
        FilterPreparationIssue[] issues = [];
        ScopedTransformationCountPlan? scopedCount = null;
        if (preferGpu)
        {
            try
            {
                if (numerical is not null) gpu = new(numerical, numericalVariant);
                else if ((scopedCount = ScopedTransformationCountPlan.TryCreate(model)) is not null)
                    gpu = new(scopedCount, numericalVariant);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { issues = [new("GpuPreparation", "GpuPlanConstructionFailed", error.ToString())]; }
        }
        string revision = numerical is null ? "T.Cpu.ScopedTransformations.20261124.v2" :
            "T.Cpu.AncientInitialBasics.20261117.v1";
        var profile = RuntimeProfileRegistry.Select(model.Detection);
        int workers = Math.Clamp(preferences.WorkerCount, 1, 32);
        bool packed = profile.ProfileId == RuntimeProfileId.Beta111;
        int window = numerical is null ? 1024 : 65536;
        var eventCheck = numerical is null ? Runtime.TransformationAggregateValidator.PrepareIndependentEventCheck(model) : null;
        if (eventCheck is not null) revision += ".EventScopeFirst.20261121.v1";
        Func<ulong, bool> matches = numerical is null ? root => MatchesScoped(model, root, eventCheck) : numerical.Matches;
        var estimate = numerical is null ? null : FamilyAuxiliaryEvidence.Read<TransformationAncientNumericalPlan.WorkEstimate?>(
            Id, "AncientFixedPoolEstimate", () => numerical.EstimateWork(), null, model.SnapshotFingerprint);
        var costs = estimate.HasValue ? FamilyAuxiliaryEvidence.Read<CpuCostSnapshot?>(
            Id, "CpuCostSnapshot", CpuCostCalibration.Capture, null) : null;
        FamilyPhysicalQuote? CpuRaw(FamilyPhysicalQuoteRequest geometry)
        {
            if (estimate is not { } work) return null;
            // Rounded single-worker coefficients for the pre-hashed numerical loop.
            // Root identity and transport are outside this arithmetic anchor.
            return FamilyCoarsePricing.Cpu(Id, "T.AncientFixedPool", 4 + 8 * work.ExpectedDraws, workers, geometry,
                FormattableString.Invariant($"{work.Evidence};ExpectedDraws={work.ExpectedDraws:G9};P1PrehashedNumericAnchor.20261121;NoHostTransport"),
                measuredWorkerScale: TransformationAncientNumericalPlan.WorkerScale(workers, geometry.MeanInputPopulation));
        }
        var prepared = new PreparedFilter(Id) {
            AnalyticalCost = new(Id, window, [], "ScopedTransformations.Unmeasured"),
            Survival = estimate is { Survival: { } pass } estimated
                ? FamilySurvivalProjection.Resolved(Id, pass, estimated.Evidence)
                : FamilySurvivalProjection.Unresolved(Id, estimate?.Evidence ?? "ScopedTransformations.JointProbabilityUnavailable"),
            Obligations = [new(Id, model.Authority.PlayerSlotIndex)], Issues = issues
        };
        var cpu = new FilterBodyDescription(prepared, revision, profile.ProfileId) {
            InputIdentity = model.SnapshotFingerprint, PlayersCount = model.Authority.PlayersCount,
            CpuWorkers = workers, WindowCapacity = window, MaximumRootWindowSize = 65536,
            MaximumEntryRootWindowSize = numerical is null ? 1024 : null,
            CpuChunkCapacity = numerical is null ? window : CpuFilterExecution.Capacity,
            PerformanceProjection = compact => CpuFilterExecution.Describe(Id, revision, workers, window, packed, packed, compact),
            Cost = new() {
                CanonicalScope = false, CalibrationQuote = CpuRaw, UsesBoundedPrefixPopulation = estimate.HasValue,
                Quote = (selected, geometry, _) => CpuRaw(geometry) is { } raw
                    ? costs?.Local(raw, selected.ResolveConditionPerformance(geometry.CompactInput).PhysicalImplementationRevision,
                        workers, geometry.CompactInput) ?? raw : null
            },
            CreateExecution = (selected, _) => new CpuFilterExecution(selected, profile, matches, packed, packed,
                minimumPartitionInputs: numerical is null ? 32 : 1024)
        };
        if (gpu is not { Supported: true } gpuPlan) return cpu;
        Func<ulong, bool> gpuReference = scopedCount is null ? matches : scopedCount.Matches;
        var scopedWork = scopedCount is null ? null : FamilyAuxiliaryEvidence.Read<ScopedTransformationCountPlan.WorkEstimate?>(
            Id, "ScopedStreamCountEstimate", scopedCount.EstimateWork, null, model.SnapshotFingerprint);
        var gpuPreparation = scopedWork is { } scoped ? prepared with
        {
            Survival = FamilySurvivalProjection.Resolved(Id, scoped.Survival, ScopedTransformationCountPlan.WorkEstimate.Evidence)
        } : prepared;
        return new(gpuPreparation, gpuPlan.Revision(false), profile.ProfileId) {
            UsesGpu = true, NumericRequirements = gpuPlan.NumericRequirements,
            InputIdentity = model.SnapshotFingerprint, PlayersCount = model.Authority.PlayersCount, CpuWorkers = workers,
            WindowCapacity = gpuPlan.Capacity, CpuAlternatives = [cpu],
            PerformanceProjection = compact => new(Id, gpuPlan.Revision(compact), (gpuPlan.ScopedCount ? "T.Gpu.ScopedStreamCount" : "T.Gpu.AncientFixedPool") + gpuPlan.NumericalRevision,
                1, gpuPlan.ScopedCount ? "OrderedSourceStreams;ScopedCount;ExactOwnsJointRoute" :
                    "FamilyOwned;OneAncientFixedPool;ExactOwnsAncientOfferAndFinalDisposition", true),
            Cost = new() {
                CanonicalScope = false, PrivateCanonicalScope = false,
                MinimumComparableInput = _ => gpuPlan.Capacity,
                Quote = (_, geometry, _) => TransformationAncientGpuQuote(gpuPlan, estimate, geometry, scopedWork)
            },
            CreateExecution = (selected, _) => CreateGpuExecution(selected, gpuPlan, gpuReference, profile),
            BindPrivate = gpuPlan.NumericalVariant == GpuNumericalVariant.NativeFloat64
                ? (device, input, output) => BindPrivateGpu(gpuPlan, device, input, output) : null
        };
    }

    private static bool MatchesScoped(SearchModel model, ulong root,
        Func<ulong, List<Runtime.TransformationAggregateValidator.Output>?>? eventCheck)
    {
        var events = eventCheck?.Invoke(root);
        if (eventCheck is not null && events is null) return false;
        // A captured bag belongs to its capture seed. Replay the same copied
        // per-root authority boundary used by final validation before pruning.
        if (!Runtime.SearchAuthorityProjector.TryProjectForRootHash(model, root, $"root:{root:X16}",
                out var authority, out _, out _)) return true;
        return Runtime.TransformationAggregateValidator.Evaluate(model, root, authority, events).Disposition != SearchDisposition.NoMatch;
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
            CreateExecution = (selected, _) => CreateGpuExecution(selected, gpuPlan, plan.Matches, profile),
            BindPrivate = gpuPlan.NumericalVariant == GpuNumericalVariant.NativeFloat64
                ? (device, input, output) => BindPrivateGpu(gpuPlan, device, input, output) : null
        };
        return new(plan, gpuPlan, probability, body);
    }

    private static IFilterExecution CreateGpuExecution(FilterBodyDescription selected, TransformationAggregateGpuPlan gpuPlan,
        Func<ulong, bool> matches, IRuntimeProfile profile) => new GpuFilterExecution<TransformationAggregateGpuExecutor>(selected,
        device => new(device, gpuPlan), (resource, input, token) => {
            var output = resource.Execute(input, token, out var metrics);
            return new(output, metrics.CanonicalMs, metrics.DispatchSyncMs, metrics.ReadbackMs, metrics.ReadbackBytes);
        }, resource => resource.Dispose(), matches, profile, resource => (resource.Device, resource.SetupMs));

    private static FamilyPrivateGpuExecution BindPrivateGpu(TransformationAggregateGpuPlan gpuPlan, Godot.RenderingDevice device,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
    {
        var resource = new TransformationAggregateGpuExecutor(device, gpuPlan, input, output);
        return new(resource, resource.Device, resource.SetupMs, input, output, (batch, count, token) => {
            FamilyCandidateSet? filtered = null;
            TransformationAggregateGpuMetrics metrics;
            if (output is not null) resource.ExecutePrivateStage(batch, count, token, out metrics);
            else filtered = input is null ? resource.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                resource.ExecutePrivate(batch, count, token, out metrics);
            return new(metrics.Output, filtered, metrics.DispatchSyncMs, metrics.ReadbackMs,
                metrics.ReadbackBytes, metrics.CanonicalMs, metrics.Dispatches);
        });
    }

    private static FamilyPhysicalQuote? TransformationAncientGpuQuote(TransformationAggregateGpuPlan gpuPlan,
        TransformationAncientNumericalPlan.WorkEstimate? estimate, FamilyPhysicalQuoteRequest geometry,
        ScopedTransformationCountPlan.WorkEstimate? scoped = null)
    {
        if (gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne && (geometry.PrivateInput || geometry.PrivateOutput)) return null;
        // Rounded coefficients from independent fixed-pool Dense dispatch
        // slopes. Constant per-dispatch header work cancels at the two input
        // populations. Compact input has no fitted coefficients of its own.
        var (fixedWork, drawWork, emitWork) = gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne
            ? (0.09, 0.035, 0.45) : (0.12, 0.06, 0.22);
        if (scoped is { } count)
        {
            // Rounded dense D3D12 dispatch slopes for scoped draw blocks.
            // FP64 retains the single-pool emission anchor because the new
            // samples cannot isolate that term from the number of draws.
            var (streamNs, drawNs, emitNs) = gpuPlan.NumericalVariant == GpuNumericalVariant.IntegerRne
                ? (0.08, 0.028, 0.16) : (0.04, 0.065, emitWork);
            double ns = streamNs * Math.Max(1, count.Streams) + drawNs * count.Draws + emitNs * count.Survival;
            return FamilyCoarsePricing.Gpu(Id, "T.ScopedStreamCount", ns, gpuPlan.Capacity, geometry,
                FormattableString.Invariant($"{ScopedTransformationCountPlan.WorkEstimate.Evidence};ExpectedDraws={count.Draws:G9};ExpectedStreamStarts={count.Streams:G9};ExpectedEmission={count.Survival:G9};NumericalVariant={gpuPlan.NumericalVariant};Reference=T.ScopedStreamCount.DenseDispatchSlope.20261124;InputAnchor=196608..1048576;StreamStartNs={streamNs:G3};DrawNs={drawNs:G3};EmitNs={emitNs:G3};NoPayloadTransport") +
                (geometry.CompactInput ? ";CompactUsesDenseReference" : ""));
        }
        if (estimate is not { } work) return null;
        double emission = work.Survival ?? 1;
        double nanoseconds = fixedWork + drawWork * work.ExpectedDraws + emitWork * emission;
        return FamilyCoarsePricing.Gpu(Id, "T.AncientFixedPool", nanoseconds, gpuPlan.Capacity, geometry,
            FormattableString.Invariant($"{work.Evidence};ExpectedDraws={work.ExpectedDraws:G9};ExpectedEmission={emission:G9};NumericalVariant={gpuPlan.NumericalVariant};Anchor=T.AncientFixedPool.DenseDispatchSlope.20261121;InputAnchor=196608..1048576;EightIndependentShapes;NoPayloadTransport") +
            (geometry.CompactInput ? ";CompactUsesDenseReference;CompactNumericSlopeUnmeasured" : ""));
    }

    // Keep model-only preparation delegates outside the body factory's closure.
    private static double? ResolveConditionalProbability(SearchModel model, TransformationAggregateNumericalPlan plan, bool sharedNeow) =>
        plan.Closed && plan.Neow is not null && sharedNeow
            ? FamilyAuxiliaryEvidence.Read<double?>(Id, "ConditionalProbability", () => TransformationAggregateProbability.Conditional(
                TransformationAggregateProbability.SharedNeowJoint(model, plan),
                new NeowFamilyProjections(model, NeowReplayPlan.Compile(model, false), false, null).Survival(new HashSet<string>()).SurvivalProbability),
                null, model.SnapshotFingerprint) : null;
}
