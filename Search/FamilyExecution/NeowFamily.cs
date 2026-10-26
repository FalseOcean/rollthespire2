using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamily : IFamilyInvocation
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
    private readonly GpuCostSamples _costSamples = new();
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks => _costSamples.Peaks;
    void IFamilyInvocation.LogExecutionEvidence() => _costSamples.WriteSummary();
    private readonly NeowReplayPlan _plan;
    private readonly Func<ulong, bool> _cpuMatcher;
    private readonly NeowFamilyProjections _models;
    private readonly FamilyCpuExecution _physical;
    private readonly bool _conditional;
    private readonly ExactSearchExecutionRequest _request;
    private readonly NeowTransformationComposite? _transformations;
    private readonly double? _transformationJoint, _givenTransformations;
    private readonly NeowFamilyGpuPlan? _gpuPlan;
    internal NeowFamilyGpuPlan? GpuPlan => _composite is null && _transformations is null ? _gpuPlan : null;
    internal NeowReplayPlan PricingReplayPlan => _plan;
    internal NeowCapsuleComposite? PricingComposite => _composite;
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest request) => _transformations is null
        ? NeowPhysicalPricing.Quote(this, request) : NeowTransformationPricing.Joint(_transformations, request);
    bool IFamilyInvocation.CanBindPrivateSerial => _gpuPlan is not null && _composite is null && _transformations is null;
    FamilyPrivateGpuExecution IFamilyInvocation.BindPrivateSerial(Godot.RenderingDevice rd,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
    {
        if (_gpuPlan is null || _composite is not null || _transformations is not null) throw new InvalidOperationException("N.PrivateSerialNotAdmitted");
        var gpu = new NeowFamilyGpuExecutor(rd, _gpuPlan, 42, privateInput: input, privateOutput: output);
        return new(gpu, gpu.Device, gpu.SetupMs, input, output, (batch, count, token) =>
        {
            FamilyCandidateSet? result = null; NeowFamilyGpuMetrics m;
            if (output is not null) gpu.ExecutePrivateStage(batch, count, token, out m);
            else result = input is null ? gpu.Execute(FamilyCandidateSet.Dense(batch), token, out m) : gpu.ExecutePrivate(batch, count, token, out m);
            return new(m.Survivors, result, m.DispatchSyncMs, m.ReadbackMs, m.ReadbackBytes, m.CanonicalMs, m.Dispatches);
        });
    }
    private readonly NeowCapsuleComposite? _composite;
    private NeowFamilyGpuExecutor? _gpu;
    private bool _disabled, _compact, _parity;
    private int _batches, _steady;
    private long _inputs, _outputs, _liveInputs;
    private double _ms, _liveMs, _peak;
    private double _uploadMs, _dispatchMs, _readbackMs, _canonicalizeMs;
    // 只累計已完成呼叫，不逐種子寫日誌。
    private double _submitCallMs, _syncCallMs, _headerReadCallMs, _payloadReadCallMs;
    private long _submitSyncCalls, _headerReadCalls, _payloadReadCalls;
    private long _smallPayloadReadCalls, _directPayloadReadCalls, _payloadReadSourceCapacityBytes;
    private double _combinedReadCallMs;
    private long _combinedReadCalls, _combinedPayloadBatches, _logicalPayloadBytes, _requestedReadbackBytes;
    private long _combinedReadSourceCapacityBytes;
    // 合併批次的群組記帳。
    private long _batchMergeGroups, _batchMergeWindows;
    private int _batchMergeMaxGroupSize;
    // 中型候選的第二級有界讀回緩衝。
    private double _mediumReadCallMs;
    private long _mediumPayloadReadCalls, _mediumReadSourceCapacityBytes;
    private NeowCandidateDigest? _candidateDigest;
    private long _fullBatchInputs, _fullBatchOutputs;
    private long _bonesEntryPass, _bonesPairPass;
    private double _fullBatchDispatchMs, _fullBatchCanonicalMs;
    private long _nrPairPass, _arcanePass, _rarityPass, _heavyEntered;
    private int _zeroSurvivorBatches, _payloadReadbackCount;
    private double _payloadReadbackMs;
    internal NeowFamily(ExactSearchExecutionRequest request, NeowReplayPlan plan, bool preferGpu = true, NeowCapsuleComposite? composite = null,
        NeowTransformationComposite? transformations = null)
    {
        _request = request; _plan = plan; _transformations = transformations;
        var neowMatcher = NeowFamilyReplay.Bind(plan);
        _cpuMatcher = transformations is null ? neowMatcher : root => neowMatcher(root) && transformations.Numerical.Matches(root);
        string physicalIssue = "CpuReferenceSelected";
        if (preferGpu) NeowFamilyGpuPlan.TryCreate(plan, out _gpuPlan, out physicalIssue);
        if (transformations is not null)
        {
            if (!preferGpu || composite is not null) throw new InvalidOperationException("NJointTransformPhysicalConflict");
            _gpuPlan = transformations.Gpu;
        }
        if (composite is not null && _gpuPlan is null) throw new InvalidOperationException("NrRequiresGpuPhysical");
        _composite = composite;
        _models = new(request, plan, false, null, _gpuPlan is null ? FamilyCpuExecution.Capacity : NeowFamilyGpuExecutor.Capacity);
        if (Search.Semantics.TransformationAggregateCondition.HasSharedNeow(request.Evaluation))
        {
            var numerical = transformations?.Numerical ?? new TransformationAggregateNumericalPlan(request);
            _transformationJoint = plan.ExactOnly.Length == 0 ? TransformationAggregateProbability.SharedNeowJoint(request, numerical) : null;
            _givenTransformations = TransformationAggregateProbability.Conditional(_transformationJoint,
                TransformationAggregateProbability.Build(numerical).StageSurvival);
        }
        _physical = new(request, FamilyId, "N.Neow.Cpu.LocalReplay.20260905.v1");
        _conditional = NeowReplayPlan.HasCapsule(request);
        if (plan.AuthoredUpgrades is { } upgrades)
            RuntimeLog.TryBackgroundInfo($"nAuthoredUpgradeContinuation=true;scope=AuthoredOnly;hiddenWwpReplay=false;constants={string.Join(',', upgrades.Advances)};unknownConstant=-1;unknownRoute={(plan.CapsuleUpgradeUpperBound >= 0 ? "BoundedDrawUnion" : "IdentityOnlyExactRemainder")};choicePolicy=AuthoredTakeOtherwiseSkip");
        RuntimeLog.TryBackgroundInfo($"nFamilyReady=true;coverage={string.Join(',', Coverage)};physical={(_composite is not null ? "NrTargetRankComposite" : _gpuPlan is null ? "CpuLocalReplay" : "GpuLocalDonor")};physicalIssue={physicalIssue};capsuleBagScratch=false;capsuleSemanticOwner=R;boundedBonesNr={_composite?.UsesBonesCheckpoint == true};exactOnly={string.Join(',', plan.ExactOnly)}");
    }
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
        return new(FamilyId, revision, "N.Neutral.20260905.v1", 1, "NoAcceptedWithinPathCurve");
    }
    public FamilyPerformanceObservation CapturePerformanceObservation() => _gpuPlan is null ? _physical.Observation() :
        new(ResolveConditionPerformance(_compact), _gpu?.Device ?? "not-created", _gpu?.SetupMs ?? 0, _peak, _peak,
            _batches, _steady, _inputs, _outputs, _ms, !_disabled && _parity && _steady > 0,
            _disabled ? "PhysicalFailureOrRecovery" : "CanonicalAbi1Ready;FirstBatchExcluded;CpuReferenceParity=" + _parity);
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _gpuPlan is null ? _physical.Live() :
        _disabled || _batches < 2 ? null : new(FamilyId, ResolveConditionPerformance(_compact).PhysicalImplementationRevision,
            Interlocked.Read(ref _liveInputs), Volatile.Read(ref _liveMs), _batches - 1);
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext executionContext, FamilyObservationWindow observationWindow,
        FamilyCandidateSet input, CancellationToken cancellationToken)
    {
        if (observationWindow.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
            throw new InvalidOperationException("NFamilyObservationContextMismatch");
        cancellationToken.ThrowIfCancellationRequested();
        if (_gpuPlan is null) return _physical.Execute(observationWindow, input, cancellationToken, _cpuMatcher);
        if (_disabled && _composite?.UsesBonesCheckpoint == true)
            throw new InvalidOperationException("NrBonesPhysicalFaulted");
        if (_disabled) throw new InvalidOperationException("N.SelectedInvocationFaulted");
        if (input.Count == 0) return input;
        _compact = !input.IsDense;
        try
        {
            await EnsureGpuAsync(executionContext, cancellationToken).ConfigureAwait(false);
            var result = await executionContext.ExecutePrimaryGpuAsync(_ => {
                var survivors = _gpu!.Execute(input, cancellationToken, out var metrics); return (survivors, metrics);
            }, cancellationToken).ConfigureAwait(false);
            RecordCompletedBatch(input, result.survivors, result.metrics, executionContext);
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

    private async ValueTask EnsureGpuAsync(FamilyExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (_gpu is not null) return;
        _gpu = await executionContext.ExecutePrimaryGpuAsync(rd => new NeowFamilyGpuExecutor(rd, _gpuPlan!,
            (uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(_request.SnapshotFingerprint, 0), _composite), cancellationToken).ConfigureAwait(false);
    }

    // 單窗口與合併路徑共用驗證、摘要及記帳。
    private void RecordCompletedBatch(FamilyCandidateSet input, FamilyCandidateSet survivors,
        in NeowFamilyGpuMetrics result, FamilyExecutionContext executionContext)
    {
        if (!_parity)
        {
            var profile = RuntimeProfileRegistry.Select(_request.Detection);
            ulong[] referenceSurvivors = survivors.ExportAbi1().ToArray();
            foreach (ulong ordinal in input.EnumerateLogicalOrdinals().Take(256))
            {
                ulong root = profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, input.Batch.GlobalCandidate(ordinal)));
                if ((_cpuMatcher(root) && (_composite?.Reference.Matches(root) ?? true)) != (Array.BinarySearch(referenceSurvivors, ordinal) >= 0))
                    throw new InvalidDataException("NFamilyCpuReferenceParityMismatch");
            }
            _parity = true;
        }
        _costSamples.Observe(this, new(!input.IsDense, false, input.Count), input.Count, result.DispatchSyncMs, outputs: survivors.Count, canonicalMs: result.CanonicalMs, readbackMs: result.ReadbackMs, readbackBytes: result.ReadbackBytes);
        // 配對測試比較完整中間候選流，空批次亦納入摘要。
        (_candidateDigest ??= new()).Append(input.Batch.BatchBase, input.Batch.BatchCandidateCount,
            survivors.ExportAbi1().Span, result.CurseSurvivors, result.PairSurvivors);
        _batches++; _inputs += input.Count; _outputs += survivors.Count; _ms += result.CanonicalMs;
        _bonesEntryPass += result.CurseSurvivors; _bonesPairPass += result.PairSurvivors;
        _uploadMs += result.UploadMs; _dispatchMs += result.DispatchSyncMs;
        _readbackMs += result.ReadbackMs; _canonicalizeMs += result.CanonicalizeMs;
        _submitCallMs += result.SubmitMs; _syncCallMs += result.SyncMs;
        _headerReadCallMs += result.HeaderReadCallMs; _payloadReadCallMs += result.PayloadReadCallMs;
        _submitSyncCalls += result.BatchMergeWindow
            ? result.CombinedReadCalls
            : (result.Groups > 0 ? 1 : 0);
        _headerReadCalls += result.HeaderReadCalls; _payloadReadCalls += result.PayloadReadCalls;
        _smallPayloadReadCalls += result.SmallPayloadReadCalls;
        _directPayloadReadCalls += result.DirectPayloadReadCalls;
        _payloadReadSourceCapacityBytes += result.PayloadReadSourceCapacityBytes;
        _combinedReadCallMs += result.CombinedReadCallMs;
        _combinedReadCalls += result.CombinedReadCalls;
        _combinedPayloadBatches += result.CombinedPayloadBatches;
        _combinedReadSourceCapacityBytes += result.CombinedReadSourceCapacityBytes;
        _mediumPayloadReadCalls += result.MediumPayloadReadCalls;
        _mediumReadCallMs += result.MediumReadCallMs;
        _mediumReadSourceCapacityBytes += result.MediumReadSourceCapacityBytes;
        _logicalPayloadBytes += result.LogicalPayloadBytes;
        _requestedReadbackBytes += result.ReadbackBytes;
        if (result.BatchMergeWindow)
        {
            _batchMergeWindows++;
            _batchMergeMaxGroupSize = Math.Max(_batchMergeMaxGroupSize, result.BatchMergeGroupSize);
            if (result.CombinedReadCalls > 0) _batchMergeGroups++;
        }
        if (input.Count == NeowFamilyGpuExecutor.Capacity)
        {
            _fullBatchInputs += input.Count; _fullBatchOutputs += survivors.Count;
            _fullBatchDispatchMs += result.DispatchSyncMs; _fullBatchCanonicalMs += result.CanonicalMs;
        }
        if (_composite?.UsesBonesArcane == true)
        {
            _nrPairPass += result.NrPairPass; _arcanePass += result.ArcanePass;
            _rarityPass += result.CapsuleRarityPass; _heavyEntered += result.HeavyEntered;
            if (survivors.Count == 0) _zeroSurvivorBatches++; else _payloadReadbackCount++;
            _payloadReadbackMs += result.PayloadReadbackMs;
            if (RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled)
                RuntimeLog.TryBackgroundDetail($"boundedBonesCapsuleArcaneNr=True;batchBase={input.Batch.BatchBase};pairPass={result.NrPairPass};arcanePass={result.ArcanePass};capsuleRarityPass={result.CapsuleRarityPass};heavyEntered={result.HeavyEntered};nrOutput={survivors.Count};zeroSurvivorBatches={_zeroSurvivorBatches};payloadReadbackCount={_payloadReadbackCount};payloadReadbackMs={result.PayloadReadbackMs}");
        }
        if (_batches > 1)
        {
            Interlocked.Add(ref _liveInputs, input.Count); _liveMs += result.CanonicalMs;
            if (input.Count >= (_compact ? NeowFamilyGpuExecutor.Capacity / 4 : NeowFamilyGpuExecutor.Capacity))
            { _steady++; _peak = Math.Max(_peak, input.Count * 1000d / result.CanonicalMs); }
        }
        if (RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled)
            RuntimeLog.TryBackgroundDetail($"nFamilyBatch=true;physicalRevision={ResolveConditionPerformance(_compact).PhysicalImplementationRevision};batchBase={input.Batch.BatchBase};input={input.Count};processed={result.Processed};survivors={survivors.Count};groups={result.Groups};dispatches={result.Dispatches};curseSurvivors={result.CurseSurvivors};pairSurvivors={result.PairSurvivors};uploadMs={result.UploadMs};dispatchSyncMs={result.DispatchSyncMs};readbackMs={result.ReadbackMs};readbackBytes={result.ReadbackBytes};canonicalizeMs={result.CanonicalizeMs};canonicalAbi1ReadyMs={result.CanonicalMs};firstDrawAcceptMask={_gpuPlan!.Meta[68]};firstDrawRejectMask={_gpuPlan.Meta[69]};trackedBones={_gpuPlan.Meta[67]};boundedBonesNr={_composite?.UsesBonesCheckpoint == true};leafyPreGate={_gpuPlan.UsesLeafyPreGate};parity={_parity};workspaceBytes={_gpu!.ResidentBytes};scratchBytes={_gpu.PayloadReadbackScratchBytes};ownerThread={executionContext.GpuOwnerThreadId};batchMerge={result.BatchMergeWindow}");
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpuPlan is null) { _physical.WriteSummary(); return; }
        try
        {
            if (_gpu is not null) await context.ExecutePrimaryGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (_candidateDigest is { } digest)
            {
                _candidateDigest = null;
                try
                {
                    RuntimeLog.TryBackgroundInfo($"nFamilyCandidateDigest=true;schema=NeowCandidateDigest.v1;algorithm=SHA256;batches={_batches};input={_inputs};output={_outputs};digest={digest.GetCurrentHex()};scope=CompletedNeowCallsNotCommittedCursorSubset");
                }
                finally { digest.Dispose(); }
            }
        }
        if (_composite?.UsesBonesArcane == true)
            RuntimeLog.TryBackgroundInfo($"boundedBonesCapsuleArcaneNr=True;summary=True;batches={_batches};pairPass={_nrPairPass};arcanePass={_arcanePass};capsuleRarityPass={_rarityPass};heavyEntered={_heavyEntered};nrOutput={_outputs};zeroSurvivorBatches={_zeroSurvivorBatches};payloadReadbackCount={_payloadReadbackCount};payloadReadbackMs={_payloadReadbackMs}");
        RuntimeLog.TryBackgroundInfo($"nFamilySummary=true;gpu={_gpuPlan is not null};batches={_batches};input={_inputs};survivors={_outputs};canonicalMs={_ms};parity={_parity};recovery=False;faulted={_disabled};" +
            $"uploadMs={_uploadMs};dispatchSyncMs={_dispatchMs};readbackMs={_readbackMs};canonicalizeMs={_canonicalizeMs};" +
            $"fullBatchInputs={_fullBatchInputs};fullBatchOutputs={_fullBatchOutputs};fullBatchDispatchSyncMs={_fullBatchDispatchMs};fullBatchCanonicalMs={_fullBatchCanonicalMs};partialBatchInputs={_inputs-_fullBatchInputs};constructorSetupIncluded=false;firstBatchIncluded=true;" +
            $"bonesEntryPass={_bonesEntryPass};bonesPairPass={_bonesPairPass};bonesCheckpointCounters=StagedOnly");
        RuntimeLog.TryBackgroundInfo(FormattableString.Invariant(
            $"nFamilyPhaseSummary=true;schema=NeowHostPhaseTiming.v5;clock=StopwatchHost;gpuKernelTimestamp=false;batches={_batches};submitCalls={_submitSyncCalls};syncCalls={_submitSyncCalls};submitMs={_submitCallMs};syncMs={_syncCallMs};commandRecordingAndOtherHostMs={_dispatchMs-_submitCallMs-_syncCallMs};headerReadCalls={_headerReadCalls};headerReadApiMs={_headerReadCallMs};payloadReadCalls={_payloadReadCalls};payloadReadTransportMs={_payloadReadCallMs};combinedReadCalls={_combinedReadCalls};combinedReadApiMs={_combinedReadCallMs};combinedPayloadBatches={_combinedPayloadBatches};requestedReadbackBytes={_requestedReadbackBytes};logicalPayloadBytes={_logicalPayloadBytes};smallReadbackBuildEnabled={NeowReadbackTransport.Enabled};combinedReadbackBuildEnabled={NeowCombinedReadbackTransport.Enabled};smallPayloadReadCalls={_smallPayloadReadCalls};directPayloadReadCalls={_directPayloadReadCalls};payloadReadSourceCapacityBytes={_payloadReadSourceCapacityBytes};combinedReadSourceCapacityBytes={_combinedReadSourceCapacityBytes};mediumReadbackBuildEnabled={NeowMediumReadbackTransport.Enabled};mediumPayloadReadCalls={_mediumPayloadReadCalls};mediumReadApiMs={_mediumReadCallMs};mediumReadSourceCapacityBytes={_mediumReadSourceCapacityBytes};batchMergeBuildEnabled={FamilyBatchGrouping.Enabled};batchMergeGroupSize={FamilyBatchGrouping.GroupSize};batchMergeGroups={_batchMergeGroups};batchMergeWindows={_batchMergeWindows};batchMergeMaxGroupSize={_batchMergeMaxGroupSize};capacityMetric=LogicalSourceBufferSizesNotTransferredBytes;conversionValidationAndOtherReadbackMs={_readbackMs-_headerReadCallMs-_payloadReadCallMs-_combinedReadCallMs};scope=CompletedNeowPublicInvocationCalls;timingsAlsoIncludedInExistingSummary=true"));
    }
    internal static bool TryCreate(ExactSearchExecutionRequest request, out IFamilyInvocation? family)
    {
        family = null;
        if (!request.Evaluation.HasNeowConstraints) return false;
        try
        {
            var plan = NeowReplayPlan.Compile(request, false);
            family = new NeowFamily(request, plan);
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.TryBackgroundWarning("nFamilyUnavailable=true;recovery=ExactOnly;legacyFallback=false;reason=" + ex.Message);
            return false;
        }
    }
}
