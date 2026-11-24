using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
namespace RolltheSpire2.Search.FamilyExecution;
internal sealed partial class NeowFilterExecution : IFilterExecution, IDenseFamilyBatchGrouping
{
    public FilterBodyDescription Body { get; }
    public bool ExecutionFaulted => _disabled;
    public (string Device,double SetupMs) ResourceInfo => (_gpu?.Device ?? "",(_gpu?.SetupMs ?? 0)+(_pairGpu?.SetupMs ?? 0));
    private readonly NeowReplayPlan _plan;
    private readonly NeowFamilyGpuPlan _gpuPlan;
    private readonly NeowCapsuleComposite? _composite;
    private readonly NeowTransformationComposite? _transformations;
    private readonly NeowGpuOptimizations _gpuOptimizations;
    private readonly bool _skipExactValidation;
    private readonly IRuntimeProfile _profile;
    private readonly Func<ulong,bool> _cpuMatcher;
    private readonly NeowCapsuleSparseRefinementRun? _sparseRefinement;
    private string FamilyId => Body.FamilyId;
    private FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => Body.ResolveConditionPerformance(compact);
    private IReadOnlyList<string> Coverage => Body.Coverage;
    internal NeowFilterExecution(FilterBodyDescription body,NeowReplayPlan plan,NeowFamilyGpuPlan gpu,
        NeowCapsuleComposite? composite,NeowTransformationComposite? transformations,NeowGpuOptimizations optimizations,
        bool skipExactValidation,IRuntimeProfile profile,NeowCapsuleSparseRefinement? sparse=null)
    {Body=body;_plan=plan;_gpuPlan=gpu;_composite=composite;_transformations=transformations;_gpuOptimizations=optimizations;_skipExactValidation=skipExactValidation;_profile=profile;var match=NeowFamilyReplay.Bind(plan);_cpuMatcher=transformations is null ? match : root=>match(root)&&transformations.Numerical.Matches(root);_sparseRefinement=sparse is null ? null : new(sparse,body.InputIdentity);}
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
    public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices executionContext, FamilyCandidateSet input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disabled && _composite?.UsesBonesCheckpoint == true)
            throw new InvalidOperationException("NrBonesPhysicalFaulted");
        if (_disabled) throw new InvalidOperationException("N.SelectedInvocationFaulted");
        if (input.Count == 0) return input;
        _compact = !input.IsDense;
        try
        {
            if (_gpu is null) _gpu = await executionContext.ExecuteGpuAsync(rd => new NeowFamilyGpuExecutor(rd, _gpuPlan,
                (uint)RolltheSpire2.Core.Seed.XxHash64.HashUtf8(Body.InputIdentity, 0), _composite, optimizations: _gpuOptimizations), cancellationToken).ConfigureAwait(false);
            var result = await executionContext.ExecuteGpuAsync(_ => {
                var survivors = _gpu.Execute(input, cancellationToken, out var metrics); return (survivors, metrics);
            }, cancellationToken).ConfigureAwait(false);
            ValidateCpuParity(input, result.survivors);
            var survivors=RefinePublic(executionContext,result.survivors,cancellationToken,out double refineMs);
            ObserveGpuResult(executionContext, input, survivors, result.metrics with {CanonicalMs=result.metrics.CanonicalMs+refineMs});
            return survivors;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _disabled = true;
            RuntimeLog.TryBackgroundWarning($"nFamilyPhysicalFault=true;reason={ex.Message};recovery=None");
            throw;
        }
    }
    public async ValueTask DisposeAsync(FilterRuntimeServices context)
    {
        _sparseRefinement?.Report();
        if (_gpu is not null || _pairGpu is not null)
            await context.ExecuteGpuAsync(_ => { DisposePair(_pairGpu, _gpu); return true; }, CancellationToken.None).ConfigureAwait(false);
        if (_composite?.UsesBonesArcane == true)
            RuntimeLog.TryBackgroundInfo($"boundedBonesCapsuleArcaneNr=True;summary=True;batches={_batches};pairPass={_nrPairPass};arcanePass={_arcanePass};capsuleRarityPass={_rarityPass};heavyEntered={_heavyEntered};nrOutput={_outputs};zeroSurvivorBatches={_zeroSurvivorBatches};payloadReadbackCount={_payloadReadbackCount};payloadReadbackMs={_payloadReadbackMs}");
        RuntimeLog.TryBackgroundInfo($"nFamilySummary=true;gpu={_gpuPlan is not null};batches={_batches};input={_inputs};survivors={_outputs};canonicalMs={_ms};parity={_parity};recovery=False;faulted={_disabled};" +
            $"uploadMs={_uploadMs};dispatchSyncMs={_dispatchMs};readbackMs={_readbackMs};canonicalizeMs={_canonicalizeMs};" +
            $"fullBatchInputs={_fullBatchInputs};fullBatchOutputs={_fullBatchOutputs};fullBatchDispatchSyncMs={_fullBatchDispatchMs};fullBatchCanonicalMs={_fullBatchCanonicalMs};partialBatchInputs={_inputs-_fullBatchInputs};constructorSetupIncluded=false;firstBatchIncluded=true;" +
            $"bonesEntryPass={_bonesEntryPass};bonesPairPass={_bonesPairPass};bonesCheckpointCounters=StagedOnly;densePairGroups={_densePairGroups};densePairCalibrationSamples=Excluded");
    }
    private FamilyCandidateSet RefinePublic(FilterRuntimeServices services,FamilyCandidateSet input,CancellationToken token,out double milliseconds)
    {
        milliseconds=0;
        return _sparseRefinement is null ? input : _sparseRefinement.Filter(services,input,_profile,token,out milliseconds);
    }
}
