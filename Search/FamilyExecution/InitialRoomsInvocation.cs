using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// An additional numerical calculation owned by R or W. The legacy calculation
// stays independent; only predicates that actually ran enter passed Coverage.
internal sealed class InitialRoomsInvocation : IFamilyInvocation
{
    internal const string TreasureCoverage = "R.Relic.InitialTreasure";
    internal const string UnknownCoverage = "W.World.InitialUnknown";
    private readonly ExactSearchExecutionRequest _request;
    private readonly IFamilyInvocation? _ordinary;
    private readonly ExactSearchExecutionRequest _ordinaryRequest;
    private readonly bool _usesGpu;
    private readonly FamilyCpuExecution _cpu;
    private InitialRoomsGpuExecutor? _gpu;
    private bool _parity, _faulted, _compact;
    private int _batches;
    private long _inputs, _outputs;
    private double _milliseconds;
    internal InitialRoomsPlan Plan { get; }
    internal IFamilyInvocation? Ordinary => _ordinary;
    public string FamilyId { get; }
    public IReadOnlyList<string> Coverage { get; }
    internal ExactSearchExecutionRequest PricingRequest => _request;
    internal IFamilyInvocation? PricingOrdinary => _ordinary;
    internal ExactSearchExecutionRequest PricingOrdinaryRequest => _ordinaryRequest;

    internal InitialRoomsInvocation(ExactSearchExecutionRequest request, string familyId,
        InitialRoomsPlan plan, IFamilyInvocation? ordinary, bool preferGpu, int? workers = null,
        ExactSearchExecutionRequest? ordinaryRequest = null)
    {
        if (familyId is not ("R.Relic" or "W.World") || ordinary is not null && ordinary.FamilyId != familyId ||
            familyId == "R.Relic" && (!plan.CoversTreasure || plan.CoversUnknownPrefix) ||
            familyId == "W.World" && (!plan.CoversUnknownPrefix || plan.CoversTreasure))
            throw new ArgumentException("InitialRooms.OwnerProjectionMismatch", nameof(plan));
        _request = request; FamilyId = familyId; Plan = plan; _usesGpu = preferGpu;
        _ordinary = !preferGpu && ordinary is { ConditionPerformance.UsesGpu: true }
            ? FamilyCpuExecution.ConfiguredRealizations(ordinary).FirstOrDefault()
                ?? throw new InvalidOperationException("InitialRooms.OrdinaryCpuRealizationUnavailable:" + familyId)
            : ordinary;
        _ordinaryRequest = ordinaryRequest ?? request;
        Coverage = (_ordinary?.Coverage ?? []).Append(familyId == "R.Relic" ? TreasureCoverage : UnknownCoverage)
            .Distinct(StringComparer.Ordinal).ToArray();
        _cpu = new(request, FamilyId, Revision + ".Cpu", workers: workers ?? FamilyCpuExecution.ConfiguredWorkers(request),
            numericalRoots: true, windowCapacity: 65536);
    }

    private string Revision => FamilyId + (Plan.CoversTreasure ? ".InitialTreasure" : ".InitialUnknown") +
        (_ordinary is null ? ".Only" : ".WithOrdinary") + ".Public.20261024.v1" +
        (_usesGpu && Plan.NumericalVariant != GpuNumericalVariant.NativeFloat64 ? ".IntegerRne.20261025.v1" : "");
    bool IFamilyInvocation.ExecutionFaulted => _faulted || (_ordinary?.ExecutionFaulted ?? false);
    GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements =>
        (_usesGpu ? Plan.NumericRequirements : GpuNumericRequirements.None) |
        (_ordinary?.GpuNumericRequirements ?? GpuNumericRequirements.None);
    // These complete calls use their own finite public references. R/W private paths stay
    // available only when this additional calculation is absent.
    bool IFamilyInvocation.CanBindPrivateSerial => false;
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest request) => FamilyCanonicalMeasuredCost.TryDenseQuote(this, request);
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, InitialRoomsPlan.Capacity, [], "InitialRoomsAnalyticalWorkUnmodeled");
    public FamilySurvivalProjection Survival => ResolveSurvival(new HashSet<string>(StringComparer.Ordinal));
    public bool HasConditionalProjections => _ordinary?.HasConditionalProjections == true;
    public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage)
    {
        var input = SearchSelectivityInput.From(_request);
        var initial = Plan.CoversTreasure
            ? TreasureRelicProbability.Estimate(input)
            : FirstActUnknownEventProbability.Estimate(input);
        if (initial.Probability is not double local)
            return FamilySurvivalProjection.Unresolved(FamilyId, "InitialRooms.BaseProbabilityUnavailable;" + initial.EvidenceCode);
        double ordinary = 1;
        if (_ordinary is not null)
        {
            var before = _ordinary.ResolveSurvival(passedCoverage);
            if (before.SurvivalProbability is not double value)
                return FamilySurvivalProjection.Unresolved(FamilyId, "InitialRooms.OrdinaryProbabilityUnavailable;" + before.Evidence);
            ordinary = value;
        }
        return FamilySurvivalProjection.Resolved(FamilyId, ordinary * local,
            "InitialRooms.DeclaredConditions;OrdinaryAndInitialConditionsAssumedIndependent;" + initial.EvidenceCode);
    }
    public FamilyExpectedFilteringCostProjection? ExpectedFilteringCost => null;
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact) => new(FamilyId,
        Revision + (_usesGpu ? ".Gpu" : ".Cpu.P" + _cpu.Workers) + (compact ? ".CompactAbi1" : ".Dense") + ".CanonicalAbi1Ready",
        "InitialRooms.Neutral.20261024.v1", 1, "CompletePublicCall;FiniteReferenceWhenAvailable;ProductionExactFinal", usesGpu: _usesGpu);

    IEnumerable<IFamilyInvocation> IFamilyInvocation.CpuRealizations
    {
        get
        {
            if (!_usesGpu) { yield return this; yield break; }
            if (_ordinary is null)
            {
                yield return new InitialRoomsInvocation(_request, FamilyId, Plan, null, false,
                    FamilyCpuExecution.ConfiguredWorkers(_request));
                yield break;
            }
            foreach (IFamilyInvocation cpu in FamilyCpuExecution.ConfiguredRealizations(_ordinary))
                yield return new InitialRoomsInvocation(_request, FamilyId, Plan, cpu, false,
                    cpu is FamilyCpuExecution physical ? physical.Workers : FamilyCpuExecution.ConfiguredWorkers(_request), _ordinaryRequest);
        }
    }

    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ResolveConditionPerformance(_compact),
        _usesGpu ? _gpu?.Device ?? "" : "CPU", _gpu?.SetupMs ?? 0, 0, 0, _batches, 0, _inputs, _outputs, _milliseconds,
        false, "CompletePublicCall;ReferenceParity=" + _parity);
    // Child observations cannot be advertised as a measurement of this call.
    public FamilyLivePerformanceSnapshot? CaptureLivePerformanceSnapshot() => null;

    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow observation,
        FamilyCandidateSet input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (((IFamilyInvocation)this).ExecutionFaulted) throw new InvalidOperationException(FamilyId + ".InitialRooms.InvocationFaulted");
        if (observation.ExactRequest.SnapshotFingerprint != _request.SnapshotFingerprint)
            throw new InvalidOperationException(FamilyId + ".InitialRooms.ObservationMismatch");
        _compact = !input.IsDense;
        var watch = Stopwatch.StartNew();
        try
        {
            FamilyCandidateSet current = _ordinary is null ? input : await _ordinary.InvokeAsync(context,
                new FamilyObservationWindow(observation.Batch, _ordinaryRequest), input, token).ConfigureAwait(false);
            FamilyCandidateSet output;
            if (!_usesGpu) output = _cpu.Execute(observation, current, token, Plan.Matches, context);
            else if (current.Count == 0) output = current;
            else
            {
                _gpu ??= await context.ExecuteGpuAsync(rd => new InitialRoomsGpuExecutor(rd, Plan), token).ConfigureAwait(false);
                if (!_parity)
                {
                    var sample = FamilyCandidateSet.FromSortedAbi1(current.Batch, current.EnumerateLogicalOrdinals().Take(256).ToArray());
                    var actual = await context.ExecuteGpuAsync(rd => _gpu.Execute(sample, token, out _), token).ConfigureAwait(false);
                    var profile = RuntimeProfileRegistry.Select(_request.Detection);
                    var expected = sample.EnumerateLogicalOrdinals().Where(i => Plan.Reference(profile.ComputeRootSeed(
                        VisibleSeedCandidateCodec.FormatOrdinal(profile, current.Batch.GlobalCandidate(i)))));
                    if (!actual.EnumerateLogicalOrdinals().SequenceEqual(expected))
                        throw new InvalidDataException(FamilyId + ".InitialRooms.ReferenceParityMismatch");
                    _parity = true;
                }
                output = await context.ExecuteGpuAsync(rd => _gpu.Execute(current, token, out _), token).ConfigureAwait(false);
            }
            _batches++; _inputs += input.Count; _outputs += output.Count; _milliseconds += watch.Elapsed.TotalMilliseconds;
            return output;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            _faulted = true;
            RuntimeLog.TryBackgroundWarning("initialRoomsOwnerFault=true;family=" + FamilyId + ";recovery=None;exception=" + RuntimeLog.SafeJson(error.ToString()));
            throw;
        }
    }

    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        try
        {
            if (_gpu is not null) await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) { context.RecordGpuCleanupFailure(error, FamilyId, "InitialRooms.Dispose", _request.SnapshotFingerprint); }
        if (!_usesGpu) _cpu.WriteSummary();
        if (_ordinary is not null) await _ordinary.DisposeAsync(context).ConfigureAwait(false);
    }
}
