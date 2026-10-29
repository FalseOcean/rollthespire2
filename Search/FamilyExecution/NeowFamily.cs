using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Compatibility;
using NumericRequirements = RolltheSpire2.Search.FamilyExecution.GpuNumericRequirements;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamily : IFamilyInvocation, IDenseFamilyBatchGrouping
{
    internal const string PartyOrderedKRevision = ".PartyOrderedKFirstReject.20261016.v1";
    internal delegate bool GpuPlanFactory(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue);
    internal bool GpuPreparationFailed { get; }
    internal string GpuPreparationFailureDetail { get; } = "";
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
    private readonly GpuCostSamples _costSamples = new();
    private readonly NeowGpuOptimizations _gpuOptimizations = NeowGpuOptimizations.Capture();
    private NeowGpuOptimizations OptimizationsFor(bool compact) => _gpuPlan is null ? default : _gpuOptimizations.Applicable(_gpuPlan, compact,
        _composite is null && _transformations is null && _gpuPlan.DirectNestedMode == 0 && _gpuPlan.JointTransformMetaOffset < 0);
    internal NeowGpuOptimizations WorkEstimateOptimizations(bool compact) => OptimizationsFor(compact);
    internal string PricingOptimizationShape(bool compact) => _gpuPlan is null ? "" : OptimizationsFor(compact).DescribeWork(_gpuPlan, compact);
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks => _costSamples.Peaks;
    void IFamilyInvocation.LogExecutionEvidence() => _costSamples.WriteSummary();
    private readonly NeowReplayPlan _plan;
    private readonly NeowWorkEstimate.Work _workEstimate;
    private readonly Func<ulong, bool> _cpuMatcher;
    private readonly NeowFamilyProjections _models;
    private readonly FamilyCpuExecution _physical;
    private readonly bool _conditional;
    private readonly ExactSearchExecutionRequest _request;
    internal ExactSearchExecutionRequest PricingRequest => _request;
    private readonly NeowTransformationComposite? _transformations;
    private readonly double? _transformationJoint, _givenTransformations;
    private readonly NeowFamilyGpuPlan? _gpuPlan;
    internal NeowFamilyGpuPlan? GpuPlan => _composite is null && _transformations is null ? _gpuPlan : null;
    internal NeowReplayPlan PricingReplayPlan => _plan;
    internal NeowCapsuleComposite? PricingComposite => _composite;
    internal bool UsesCompatibilityGpu => _gpuPlan?.NumericalVariant == NeowGpuNumericalVariant.IntegerUnitFloat;
    GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements => _gpuPlan is null ? NumericRequirements.None :
        _gpuPlan.GpuNumericRequirements | (_composite is not null || _transformations is not null ? NumericRequirements.Float64 : NumericRequirements.None);
    internal bool UsesSmallPrivateOutput => _composite is null && _transformations is null && NeowFamilyGpuExecutor.SupportsSmallOutput(_gpuPlan);
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest request)
    {
        // Exact finite evidence keeps its original identity. A new ordinary body
        // may instead consume a clearly estimated N-only work reference. Joint
        // owners retain their own existing boundary, never an ordinary N price.
        var quote = FamilyCanonicalMeasuredCost.TryDenseQuote(this, request);
        if (quote is null)
        {
            if (Coverage.Count != 1)
                quote = _transformations is null ? NeowPhysicalPricing.Quote(this, request) :
                    NeowTransformationPricing.Joint(_transformations, request);
            else if (_gpuPlan is null)
                quote = NeowWorkEstimate.Cpu(this, _workEstimate, request, 1, capacity: FamilyCpuExecution.Capacity);
            else
                quote = (UsesCompatibilityGpu || OptimizationsFor(request.CompactInput).Revision.Length != 0 ? null :
                    NeowPhysicalPricing.Quote(this, request)) ?? NeowWorkEstimate.Gpu(this, _workEstimate, request);
        }
        return FamilyGpuReadback.Reference(this, request.PrivateOutput && UsesSmallPrivateOutput && quote is not null
            ? quote with { Evidence = quote.Evidence + ";RetainedNumericalEstimate;PrivateSmallHeader8TransportUnmeasured" } : quote);
    }
    bool IFamilyInvocation.CanBindPrivateSerial => _gpuPlan is { NumericalVariant: NeowGpuNumericalVariant.NativeFloat64 } && _composite is null && _transformations is null;
    FamilyPrivateGpuExecution IFamilyInvocation.BindPrivateSerial(Godot.RenderingDevice rd,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output) => BindNeowPrivate(rd, input, output);
    internal FamilyPrivateGpuExecution BindNeowPrivate(Godot.RenderingDevice rd,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output, NeowFamilyGpuExecutor? sharedDenseScratch = null)
    {
        if (_gpuPlan is not { NumericalVariant: NeowGpuNumericalVariant.NativeFloat64 } || _composite is not null || _transformations is not null) throw new InvalidOperationException("N.PrivateSerialNotAdmitted");
        var gpu = new NeowFamilyGpuExecutor(rd, _gpuPlan, 42, privateInput: input, privateOutput: output, sharedDenseScratch: sharedDenseScratch, optimizations: _gpuOptimizations);
        return new(gpu, gpu.Device, gpu.SetupMs, input, output, (batch, count, token) =>
        {
            FamilyCandidateSet? result = null; NeowFamilyGpuMetrics m;
            if (output is not null) gpu.ExecutePrivateStage(batch, count, token, out m);
            else result = input is null ? gpu.Execute(FamilyCandidateSet.Dense(batch), token, out m) : gpu.ExecutePrivate(batch, count, token, out m);
            return new(m.Survivors, result, m.DispatchSyncMs, m.ReadbackMs, m.ReadbackBytes, m.CanonicalMs, m.Dispatches) { SmallOutput = m.SmallOutput };
        });
    }
    private readonly NeowCapsuleComposite? _composite;
    private NeowFamilyGpuExecutor? _gpu;
    private bool _disabled, _compact, _parity;
    private int _batches, _steady;
    private long _inputs, _outputs, _liveInputs;
    private double _ms, _liveMs, _peak;
    private double _uploadMs, _dispatchMs, _readbackMs, _canonicalizeMs;
    private long _fullBatchInputs, _fullBatchOutputs;
    private long _bonesEntryPass, _bonesPairPass;
    private double _fullBatchDispatchMs, _fullBatchCanonicalMs;
    private long _nrPairPass, _arcanePass, _rarityPass, _heavyEntered;
    private int _zeroSurvivorBatches, _payloadReadbackCount;
    private double _payloadReadbackMs;
    internal NeowFamily(ExactSearchExecutionRequest request, NeowReplayPlan plan, bool preferGpu = true, NeowCapsuleComposite? composite = null,
        NeowTransformationComposite? transformations = null, GpuPlanFactory? gpuPlanFactory = null)
    {
        _request = request; _plan = plan; _transformations = transformations;
        _workEstimate = NeowWorkEstimate.Describe(plan);
        var neowMatcher = NeowFamilyReplay.Bind(plan);
        _cpuMatcher = transformations is null ? neowMatcher : root => neowMatcher(root) && transformations.Numerical.Matches(root);
        string physicalIssue = "CpuReferenceSelected";
        if (preferGpu && transformations is null)
        {
            try { (gpuPlanFactory ?? NeowFamilyGpuPlan.TryCreate)(plan, out _gpuPlan, out physicalIssue); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _gpuPlan = null; GpuPreparationFailed = true; physicalIssue = "GpuPlanConstructionFailed";
                GpuPreparationFailureDetail = ex.ToString();
                RuntimeLog.TryBackgroundWarning($"nGpuPreparationFailed=true;slot={plan.Authority.PlayerSlotIndex};selected=CpuLocalReplay;scope=N.Neow;exactOnly={string.Join(',', plan.ExactOnly)};exceptionJson=" + RuntimeLog.SafeJson(ex.ToString()));
            }
        }
        if (transformations is not null)
        {
            if (!preferGpu || composite is not null) throw new InvalidOperationException("NJointTransformPhysicalConflict");
            _gpuPlan = transformations.Gpu;
        }
        if (composite is not null && _gpuPlan is null) throw new InvalidOperationException("NrRequiresGpuPhysical");
        if (_gpuPlan is { NumericalVariant: not NeowGpuNumericalVariant.NativeFloat64 } &&
            (composite is not null || transformations is not null || _gpuPlan.JointTransformMetaOffset >= 0))
            throw new InvalidOperationException("N.IntegerUnitFloat.OrdinaryInvocationOnly");
        _composite = composite;
        _models = new(request, plan, false, null, _gpuPlan is null ? FamilyCpuExecution.Capacity : NeowFamilyGpuExecutor.Capacity);
        if (Search.Semantics.TransformationAggregateCondition.HasSharedNeow(request.Evaluation))
        {
            var numerical = transformations?.Numerical ?? new TransformationAggregateNumericalPlan(request);
            _transformationJoint = plan.ExactOnly.Length == 0 ? TransformationAggregateProbability.SharedNeowJoint(request, numerical) : null;
            _givenTransformations = TransformationAggregateProbability.Conditional(_transformationJoint,
                TransformationAggregateProbability.Build(numerical).StageSurvival);
        }
        _physical = new(request, FamilyId, "N.Neow.Cpu.LocalReplay.20261015.v3");
        _conditional = NeowReplayPlan.HasCapsule(request);
        if (plan.AuthoredUpgrades is { } upgrades)
            RuntimeLog.TryBackgroundInfo($"nAuthoredUpgradeContinuation=true;scope=AuthoredOnly;hiddenWwpReplay=false;constants={string.Join(',', upgrades.Advances)};unknownConstant=-1;unknownRoute={(plan.CapsuleUpgradeUpperBound >= 0 ? "BoundedDrawUnion" : "IdentityOnlyExactRemainder")};choicePolicy=AuthoredTakeOtherwiseSkip");
        if (_gpuOptimizations != default) RuntimeLog.TryBackgroundInfo($"nOptimizationsRequested=true;requested={_gpuOptimizations.Revision};dense={OptimizationsFor(false).Revision};compact={OptimizationsFor(true).Revision};productionDefaults=true;quote=MatchingCurrentBodyReferenceOrEstimatedOwnWork");
        RuntimeLog.TryBackgroundInfo($"nFamilyReady=true;coverage={string.Join(',', Coverage)};physical={(_composite is not null ? "NrTargetRankComposite" : _gpuPlan is null ? "CpuLocalReplay" : "GpuLocalDonor")};physicalIssue={physicalIssue};capsuleBagScratch=false;capsuleSemanticOwner=R;boundedBonesNr={_composite?.UsesBonesCheckpoint == true};exactOnly={string.Join(',', plan.ExactOnly)}");
    }
    bool IFamilyInvocation.ExecutionFaulted => _disabled;
    public string FamilyId => "N.Neow";
    public IReadOnlyList<string> Coverage => _transformations is not null ? [FamilyId, TransformationAggregateFamily.Id] :
        _composite is null ? [FamilyId] : [FamilyId, "R.Relic"];
    public FamilyAnalyticalCostProjection AnalyticalCost => _composite is null ? _models.Full : new(FamilyId,
        NeowFamilyGpuExecutor.Capacity, _models.Full.Terms.Concat(_composite.Projection.Full.Terms),
        "CompositeConservativeFullEnvelope_NPlusR;ExpectedTargetRankReachUnpriced");
    public FamilySurvivalProjection Survival => ResolveSurvival(Empty);
    public bool HasConditionalProjections => _conditional || Search.Semantics.TransformationAggregateCondition.HasSharedNeow(_request.Evaluation);
    public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage)
    {
        if (_transformations is not null) return _transformationJoint is { } joint
            ? FamilySurvivalProjection.Resolved(FamilyId, joint, "NJointAggregate;ExistingSameDrawNAndTModel;IdentityOnce")
            : FamilySurvivalProjection.Unresolved(FamilyId, "NJointAggregate.JointModelUnavailable");
        if (Search.Semantics.TransformationAggregateCondition.HasSharedNeow(_request.Evaluation) && passedCoverage.Contains(TransformationAggregateFamily.Id))
            return Search.Semantics.TransformationAggregateCondition.SharedNeowIdentityOnly(_request.Evaluation)
                ? FamilySurvivalProjection.Resolved(FamilyId, 1, "SharedNIdentityAlreadyPassedByT")
                : _givenTransformations is { } conditional
                    ? FamilySurvivalProjection.Resolved(FamilyId, conditional, "N.GivenT;ExistingSameDrawJointOverTMarginal")
                    : FamilySurvivalProjection.Unresolved(FamilyId, "T+N.AdditionalPredicateJointUnknown");
        if (_composite is null) return _models.Survival(passedCoverage);
        double? n = _models.Survival(Empty).SurvivalProbability;
        double? r = _composite.Projection.Survival(new HashSet<string> { FamilyId }).SurvivalProbability;
        return n.HasValue && r.HasValue ? FamilySurvivalProjection.Resolved(FamilyId, n.Value*r.Value, "CompositeCoverage=N.Neow,R.Relic;RConditionalOnN")
            : FamilySurvivalProjection.Unresolved(FamilyId, "CompositeJointSurvivalUnavailable");
    }
    public FamilyExpectedFilteringCostProjection ExpectedFilteringCost => ResolveExpectedFilteringCost(Empty);
    public FamilyExpectedFilteringCostProjection ResolveExpectedFilteringCost(IReadOnlySet<string> passedCoverage) => _transformations is not null
        ? new(FamilyId, _models.Full.WorkUnitsPerInput, null, _transformationJoint, 1, [], "NJointAggregate.NoPrimitiveLedger;UseOwnedPhysicalQuote") : _composite is null
        ? _models.Expected(passedCoverage) : new(FamilyId, _models.Full.WorkUnitsPerInput + _composite.Projection.Full.WorkUnitsPerInput,
            null, ResolveSurvival(passedCoverage).SurvivalProbability, 1, [], "CompositeTargetRankReachLedgerNotYetPriced");
    internal int PreferredExecutionWindowSize => _gpuPlan is null ? FamilyCpuExecution.Capacity : NeowFamilyGpuExecutor.Capacity;
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compactAbi1Input)
    {
        if (_gpuPlan is null) return _physical.Condition(compactAbi1Input);
        string revision = _transformations is not null
            ? NeowTransformationComposite.Revision + (compactAbi1Input ? ".Compact" : _gpuPlan.UsesStagedDense ? ".StagedDense" : ".Dense")
            : _composite is not null
            ? _composite.Revision(compactAbi1Input, _gpuPlan.UsesStagedDense)
            : _gpuPlan.DirectNestedMode != 0 ? _gpuPlan.DirectNestedRevision(compactAbi1Input)
            : _gpuPlan.UsesLeafyPreGate
                ? compactAbi1Input ? NeowFamilyGpuPlan.LeafyCompactRevision :
                    _gpuPlan.UsesStagedDense ? NeowFamilyGpuPlan.LeafyStagedDenseRevision : NeowFamilyGpuPlan.LeafyDenseRevision
                : compactAbi1Input ? NeowFamilyGpuPlan.CompactRevision :
                    _gpuPlan.UsesStagedDense ? NeowFamilyGpuPlan.StagedDenseRevision : NeowFamilyGpuPlan.DenseRevision;
        if (!_gpuPlan.IsMultiplayer) revision += ".PackedPublicOrdinals.20260930.v1";
        if (!_gpuPlan.IsMultiplayer && (_gpuPlan.Meta[82] & 9u) != 0)
            revision += ".ZeroUsedDirectIndex.20260930.v1";
        if (!_gpuPlan.IsMultiplayer && _plan.StructuredConditions.Any(c => c.SourceRelicId == Beta110FastRelicCatalog.Kaleidoscope))
            revision += ".KaleidoscopeFirstGroupReject.20260930.v1";
        if (_gpuPlan.IsMultiplayer && _plan.StructuredConditions.Any(c => c.OrderedKaleidoscope))
            revision += PartyOrderedKRevision;
        if (UsesCompatibilityGpu) revision += ".IntegerUnitFloat.Rne.20261025.v1";
        revision += OptimizationsFor(compactAbi1Input).Revision;
        revision += GpuRngRotation.Revision;
        return new(FamilyId, revision, "N.Neutral.20260905.v1", 1, UsesCompatibilityGpu
            ? "NoAcceptedWithinPathCurve;GpuNumericalVariant=IntegerUnitFloat;PhysicalQuoteUnavailable=UnmeasuredVariant"
            : "NoAcceptedWithinPathCurve");
    }
    public FamilyPerformanceObservation CapturePerformanceObservation() => _gpuPlan is null ? _physical.Observation() :
        new(ResolveConditionPerformance(_compact), _gpu?.Device ?? "not-created", (_gpu?.SetupMs ?? 0) + (_pairGpu?.SetupMs ?? 0), _peak, _peak,
            _batches, _steady, _inputs, _outputs, _ms, !_disabled && _parity && _steady > 0,
            _disabled ? "PhysicalFailureOrRecovery" : "CanonicalAbi1Ready;FirstBatchExcluded;DensePairLocalCalibrationExcluded;CpuReferenceParity=" + _parity);
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _gpuPlan is null ? _physical.Live() :
        _disabled || _liveBatches == 0 ? null : new(FamilyId, ResolveConditionPerformance(_compact).PhysicalImplementationRevision,
            Interlocked.Read(ref _liveInputs), Volatile.Read(ref _liveMs), _liveBatches);
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext executionContext, FamilyObservationWindow observationWindow,
        FamilyCandidateSet input, CancellationToken cancellationToken)
    {
        if (observationWindow.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
            throw new InvalidOperationException("NFamilyObservationContextMismatch");
        cancellationToken.ThrowIfCancellationRequested();
        if (_gpuPlan is null) return _physical.Execute(observationWindow, input, cancellationToken, _cpuMatcher, executionContext);
        if (_disabled && _composite?.UsesBonesCheckpoint == true)
            throw new InvalidOperationException("NrBonesPhysicalFaulted");
        if (_disabled) throw new InvalidOperationException("N.SelectedInvocationFaulted");
        if (input.Count == 0) return input;
        _compact = !input.IsDense;
        try
        {
            if (_gpu is null) _gpu = await executionContext.ExecuteGpuAsync(rd => new NeowFamilyGpuExecutor(rd, _gpuPlan,
                (uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(_request.SnapshotFingerprint, 0), _composite, optimizations: _gpuOptimizations), cancellationToken).ConfigureAwait(false);
            var result = await executionContext.ExecuteGpuAsync(_ => {
                var survivors = _gpu.Execute(input, cancellationToken, out var metrics); return (survivors, metrics);
            }, cancellationToken).ConfigureAwait(false);
            ValidateCpuParity(input, result.survivors);
            ObserveGpuResult(executionContext, input, result.survivors, result.metrics);
            return result.survivors;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _disabled = true;
            RuntimeLog.TryBackgroundWarning($"nFamilyPhysicalFault=true;reason={ex.Message};recovery=None");
            throw;
        }
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpuPlan is null) { _physical.WriteSummary(); return; }
        if (_gpu is not null || _pairGpu is not null)
            await context.ExecuteGpuAsync(_ => { DisposePair(_pairGpu, _gpu); return true; }, CancellationToken.None).ConfigureAwait(false);
        if (_composite?.UsesBonesArcane == true)
            RuntimeLog.TryBackgroundInfo($"boundedBonesCapsuleArcaneNr=True;summary=True;batches={_batches};pairPass={_nrPairPass};arcanePass={_arcanePass};capsuleRarityPass={_rarityPass};heavyEntered={_heavyEntered};nrOutput={_outputs};zeroSurvivorBatches={_zeroSurvivorBatches};payloadReadbackCount={_payloadReadbackCount};payloadReadbackMs={_payloadReadbackMs}");
        RuntimeLog.TryBackgroundInfo($"nFamilySummary=true;gpu={_gpuPlan is not null};batches={_batches};input={_inputs};survivors={_outputs};canonicalMs={_ms};parity={_parity};recovery=False;faulted={_disabled};" +
            $"uploadMs={_uploadMs};dispatchSyncMs={_dispatchMs};readbackMs={_readbackMs};canonicalizeMs={_canonicalizeMs};" +
            $"fullBatchInputs={_fullBatchInputs};fullBatchOutputs={_fullBatchOutputs};fullBatchDispatchSyncMs={_fullBatchDispatchMs};fullBatchCanonicalMs={_fullBatchCanonicalMs};partialBatchInputs={_inputs-_fullBatchInputs};constructorSetupIncluded=false;firstBatchIncluded=true;" +
            $"bonesEntryPass={_bonesEntryPass};bonesPairPass={_bonesPairPass};bonesCheckpointCounters=StagedOnly;densePairGroups={_densePairGroups};densePairCalibrationSamples=Excluded");
    }
    internal static bool TryCreate(ExactSearchExecutionRequest request, out IFamilyInvocation? family, bool preferGpu = true,
        GpuPlanFactory? gpuPlanFactory = null)
    {
        family = null;
        if (!request.Evaluation.HasNeowConstraints) return false;
        NeowReplayPlan plan;
        try
        {
            plan = NeowReplayPlan.Compile(request, false);
        }
        catch (InvalidOperationException ex) when (ex.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity" ||
            ex.Message.StartsWith("DonorContinuationAuthorityUnavailable:", StringComparison.Ordinal))
        {
            RuntimeLog.TryBackgroundWarning("nFamilyUnavailable=true;phase=CpuReplayCompilation;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed;reason=" + ex.Message + ";exceptionJson=" + RuntimeLog.SafeJson(ex.ToString()));
            return false;
        }
        string continuationIssue = plan.StructuredConditions.Length == 0 && !plan.HasFinalCurseFastProjection ? "" : plan.ContinuationIssue();
        if (continuationIssue.Length > 0)
        {
            if (RuntimeLog.SessionId.Length > 0) Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("NeowFilterUnavailable", nameof(NeowFamily),
                "Filter.Construction", "issues.filter_incomplete", $"cause={continuationIssue};predicatesRetained=true;finalValidationSkipped={request.RunOptions.SkipExactValidation};matchClaimed=false");
            return false;
        }
        family = new NeowFamily(request, plan, preferGpu, gpuPlanFactory: gpuPlanFactory);
        return true;
    }
    internal static bool TryCreateParty(ExactSearchExecutionRequest request, out IFamilyInvocation? family, bool preferGpu,
        GpuPlanFactory? gpuPlanFactory = null)
    {
        family = null;
        if (!request.CompiledSearch.NormalizedQuery.Players.Any(PartyInitial.HasConditions)) return false;
        try { family = new PartyInitial(request, preferGpu, gpuPlanFactory: gpuPlanFactory); return true; }
        catch (InvalidOperationException ex) when (ex.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity" ||
            ex.Message.StartsWith("DonorContinuationAuthorityUnavailable:", StringComparison.Ordinal))
        {
            RuntimeLog.TryBackgroundWarning("partyNeowFamilyUnavailable=true;phase=CpuReplayCompilation;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed;exceptionJson=" + RuntimeLog.SafeJson(ex.ToString()));
            return false;
        }
    }
}
