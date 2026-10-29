using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// One independent short observation. ABI1 carries ordinals only; W reconstructs
// its own selected content and retains the full Event/Boss branch relationships.
internal sealed class VariantOpeningFamily : IFamilyInvocation
{
    internal const string Id = "V.VariantOpening";
    private const string CpuRevision = "V.VariantOpening.Cpu.20261028.v1";
    private readonly ExactSearchExecutionRequest _request;
    private readonly FamilyCpuExecution _cpu;
    private readonly VariantOpeningGpuPlan? _gpuPlan;
    private readonly VariantOpeningWorldSurvival _survival;
    private readonly double? _unknownProbability;
    private readonly GpuCostSamples _costSamples = new();
    private WorldFamilyGpuExecutor? _gpu;
    private bool _parity, _faulted, _compact;
    private int _batches;
    private long _inputs, _outputs;
    private double _milliseconds;
    internal VariantOpeningPlan Plan { get; }
    internal VariantOpeningGpuPlan? GpuPlan => _gpuPlan;
    internal ExactSearchExecutionRequest PricingRequest => _request;

    private VariantOpeningFamily(ExactSearchExecutionRequest request, VariantOpeningPlan plan,
        bool preferGpu, GpuNumericalVariant numericalVariant)
    {
        _request = request; Plan = plan;
        _cpu = new(request, Id, CpuRevision, FamilyCpuExecution.ConfiguredWorkers(request), 65536,
            numericalRoots: request.ProfileId == RuntimeProfileId.Beta111);
        _unknownProbability = plan.UnknownPrefixCount == 0 ? 1 : plan.UnknownPrefixCount is >= 1 and <= 3
            ? FirstActUnknownEventProbability.ConditionalPrefix(plan.UnknownPrefixCount, false) : null;
        _survival = FamilyAuxiliaryEvidence.Read(Id, "WorldConditionalProjection",
            () => VariantOpeningWorldSurvival.Create(request, plan.CheckedPreparation,
                WorldFamilyReplay.HasConditions(request) ? WorldFamilyReplay.Compile(request) : null),
            new VariantOpeningWorldSurvival(plan.CoversVariants ? null : 1, null, null,
                !plan.CoversVariants, "V.AuxiliaryWorldProjectionFailed"), request.SnapshotFingerprint);
        if (preferGpu) _gpuPlan = new(plan, numericalVariant);
        RuntimeLog.TryBackgroundInfo($"variantOpeningFamilyReady=true;family={Id};variantGroups={plan.Groups.Length};" +
            $"selectionDraws={plan.SelectionDraws};unknownPrefix={plan.UnknownPrefixCount};unknownFirst={plan.UnknownFirst};" +
            $"variantUnavailableReason={plan.VariantUnavailableReason};gpu={_gpuPlan is not null};coverage=ActualNecessaryChecks;recovery=None");
    }

    internal static bool TryCreate(ExactSearchExecutionRequest request, out IFamilyInvocation? family,
        bool preferGpu = true, GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        family = null;
        var preparation = request.VariantOpening ?? VariantOpeningPreparation.Prepare(request.CompiledSearch);
        if (!preparation.HasConditions || VariantOpeningPlan.TryCompile(request, preparation, numericalVariant) is not { } plan)
            return false;
        family = new VariantOpeningFamily(request, plan, preferGpu, numericalVariant);
        return true;
    }

    public string FamilyId => Id;
    bool IFamilyInvocation.ExecutionFaulted => _faulted || ((IFamilyInvocation)_cpu).ExecutionFaulted;
    GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements => _gpuPlan?.NumericRequirements ?? GpuNumericRequirements.None;
    public bool HasConditionalProjections => true;
    public FamilySurvivalProjection Survival => ResolveSurvival(new HashSet<string>(StringComparer.Ordinal));
    public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage) =>
        FamilyAuxiliaryEvidence.Read(Id, "ConditionalSurvival", () => _survival.ResolveVariant(passedCoverage, _unknownProbability),
            FamilySurvivalProjection.Unresolved(Id, "V.ActualSieveSurvivalUnavailable"), _request.SnapshotFingerprint);
    public FamilyAnalyticalCostProjection AnalyticalCost => new(Id,
        _gpuPlan?.Capacity ?? _cpu.WindowCapacity, [], "V.ShortSelectionAndOrdinaryUnknown;PhysicalWorkEstimateAvailable");
    public FamilyExpectedFilteringCostProjection? ExpectedFilteringCost => null;
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest geometry) =>
        Quote(geometry, _gpuPlan is not null, _cpu.Workers);

    private FamilyPhysicalQuote? Quote(FamilyPhysicalQuoteRequest geometry, bool gpu, int workers)
    {
        // One root hash and one device/CPU output. The second independent short
        // predicate adds only its own draws/comparisons, never another call floor.
        double draws = Plan.SelectionDraws, comparisons = Plan.VariantComparisons;
        double ns = Plan.CoversVariants
            ? (gpu ? .62 + .08 * Math.Max(0, draws - 1) + .02 * comparisons : 55 + 8 * Math.Max(0, draws - 1) + 2 * comparisons)
            : (gpu ? .12 : 30);
        ns += (gpu ? .04 : 12) * Plan.UnknownPrefixCount;
        if (!gpu && geometry.CompactInput) ns += 17;
        string evidence = $"V.ShortWork;Variant062nsCpu55nsAndInitialUnknownExistingEnvelopes;" +
            $"SelectionDraws={draws};VariantComparisons={comparisons};UnknownPrefix={Plan.UnknownPrefixCount};" +
            $"UnknownFirst={Plan.UnknownFirst};OneHashAndOutput;NoWorldProgression;NoNewMeasurement;NumericalVariant={_gpuPlan?.NumericalVariant}";
        return gpu ? FamilyCoarsePricing.Gpu(Id, "V.ShortSelectionUnknown", ns,
            VariantOpeningGpuPlan.WindowCapacity * 4, geometry, evidence) :
            FamilyCoarsePricing.Cpu(Id, "V.ShortSelectionUnknown", ns, workers, geometry, evidence);
    }

    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            foreach (int workers in FamilyCpuExecution.WorkerConfigurations(_request))
                yield return new FamilyCpuExecution(_request, this, CpuRevision, Plan.Matches, workers,
                    geometry => Quote(geometry, false, workers));
        }
    }
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compactAbi1Input) => _gpuPlan is null
        ? _cpu.Condition(compactAbi1Input)
        : new(Id, _gpuPlan.Revision(compactAbi1Input), "V.Neutral.20261028.v1", 1,
            "ActualNecessaryVariantAndAdmittedOrdinaryUnknown;CanonicalAbi1Ready;EstimateOnly", usesGpu: true);
    public FamilyPerformanceObservation CapturePerformanceObservation() => _gpuPlan is null ? _cpu.Observation() :
        new(ResolveConditionPerformance(_compact), _gpu?.Device ?? "", _gpu?.SetupMs ?? 0, 0, 0,
            _batches, 0, _inputs, _outputs, _milliseconds, false,
            $"V.NoAcceptedBodyMeasurement;CpuReferenceParity={_parity};Faulted={_faulted}");
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => _gpuPlan is null ? _cpu.Live() : null;
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks => _costSamples.Peaks;
    void IFamilyInvocation.LogExecutionEvidence() => _costSamples.WriteSummary();

    bool IFamilyInvocation.CanBindPrivateSerial => _gpuPlan is { NumericalVariant: GpuNumericalVariant.NativeFloat64 };
    FamilyPrivateGpuExecution IFamilyInvocation.BindPrivateSerial(Godot.RenderingDevice device,
        PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
    {
        if (_gpuPlan is not { NumericalVariant: GpuNumericalVariant.NativeFloat64 })
            throw new InvalidOperationException("V.PrivateSerialNotAdmitted");
        var gpu = new WorldFamilyGpuExecutor(device, _gpuPlan, input?.Buffer, output);
        return new(gpu, gpu.Device, gpu.SetupMs, input, output, (batch, count, token) =>
        {
            FamilyCandidateSet? result = null; WorldFamilyGpuMetrics metrics;
            if (output is not null) gpu.ExecutePrivateStage(batch, count, token, out metrics);
            else result = input is null ? gpu.Execute(FamilyCandidateSet.Dense(batch), token, out metrics) :
                gpu.ExecutePrivate(batch, count, token, out metrics);
            return new(metrics.Output, result, metrics.DispatchSyncMs, metrics.HeaderReadbackMs,
                metrics.HeaderReadBytes + metrics.PayloadReadBytes, metrics.CanonicalMs, metrics.Dispatches);
        });
    }

    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow observation,
        FamilyCandidateSet input, CancellationToken token)
    {
        if (_gpuPlan is null) return _cpu.Execute(observation, input, token, Plan.Matches, context);
        token.ThrowIfCancellationRequested();
        if (_faulted) throw new InvalidOperationException("V.InvocationFaulted");
        if (observation.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
            throw new InvalidOperationException("V.ObservationMismatch");
        if (input.Count == 0) return input;
        _compact = !input.IsDense;
        try
        {
            _gpu ??= await context.ExecuteGpuAsync(device => new WorldFamilyGpuExecutor(device, _gpuPlan), token).ConfigureAwait(false);
            if (!_parity)
            {
                var sample = FamilyCandidateSet.FromSortedAbi1(input.Batch, input.EnumerateLogicalOrdinals().Take(4096).ToArray());
                var actual = await context.ExecuteGpuAsync(device => _gpu.Execute(sample, token, out _), token).ConfigureAwait(false);
                var profile = RuntimeProfileRegistry.Select(_request.Detection);
                var expected = sample.EnumerateLogicalOrdinals().Where(ordinal => Plan.Matches(profile.ComputeRootSeed(
                    VisibleSeedCandidateCodec.FormatOrdinal(profile, sample.Batch.GlobalCandidate(ordinal)))));
                if (!actual.EnumerateLogicalOrdinals().SequenceEqual(expected)) throw new InvalidDataException("V.GpuCpuParityMismatch");
                _parity = true;
            }
            var result = await context.ExecuteGpuAsync(_ =>
            {
                var output = _gpu.Execute(input, token, out var metrics); return (output, metrics);
            }, token).ConfigureAwait(false);
            var m = result.metrics;
            _costSamples.Observe(this, new(!input.IsDense, false, input.Count), input.Count, m.DispatchSyncMs,
                outputs: m.Output, canonicalMs: m.CanonicalMs, readbackMs: m.HeaderReadbackMs,
                readbackBytes: m.HeaderReadBytes + m.PayloadReadBytes);
            _batches++; _inputs += m.Input; _outputs += m.Output; _milliseconds += m.CanonicalMs;
            return result.output;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            _faulted = true;
            RuntimeLog.TryBackgroundWarning("variantOpeningGpuFault=true;family=" + Id + ";recovery=None;exception=" + RuntimeLog.SafeJson(error.ToString()));
            throw;
        }
    }

    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_gpuPlan is null) { _cpu.WriteSummary(); return; }
        if (_gpu is not null)
            try { await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) { context.RecordGpuCleanupFailure(error, Id, "VariantOpening.Dispose", _request.SnapshotFingerprint); }
    }
}
