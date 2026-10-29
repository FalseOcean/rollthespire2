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
    // Public calls retain finite references. Computation quotes compose only the
    // ordinary and local work; this additional calculation has no private binding.
    bool IFamilyInvocation.CanBindPrivateSerial => false;
    public FamilyPhysicalQuote? QuotePhysicalWork(FamilyPhysicalQuoteRequest request)
    {
        var measured = FamilyCanonicalMeasuredCost.TryDenseQuote(this, request);
        if (measured is not null) return measured;
        if (!FamilyPhysicalQuote.AdmittedRequest(request) || request.PrivateInput || request.PrivateOutput || request.MeanInputPopulation <= 0)
            return null; // this invocation has no private binding
        double inputs = request.MeanInputPopulation, ordinaryPass = 1;
        double ordinaryMs = 0;
        FamilyPhysicalQuote? ordinaryQuote = null;
        if (_ordinary is not null)
        {
            ordinaryPass = _ordinary.Survival.SurvivalProbability is { } p && double.IsFinite(p) && p is >= 0 and <= 1 ? p : 1;
            ordinaryQuote = request.ComputationOnly
                ? FamilyPhysicalQuote.Computation(_ordinary, request, request.CostSnapshot)
                : _ordinary.QuotePhysicalWork(request);
            bool ownedFallback = ordinaryQuote is null;
            if (ordinaryQuote is null)
            {
                // A missing auxiliary child price is not zero work. Use this
                // same owner's source-work envelope, explicitly as an estimate.
                double work = Math.Max(1, _ordinary.AnalyticalCost.WorkUnitsPerInput);
                ordinaryQuote = _ordinary.ConditionPerformance.UsesGpu
                    ? FamilyCoarsePricing.Gpu(FamilyId, "InitialRooms.OrdinaryWorkEnvelope", .98 + .012 * work,
                        _ordinary.AnalyticalCost.InputCapacityPerInvocation, request,
                        "OrdinaryOwnerReferenceUnavailable;SourceWorkEnvelope;NoZeroCostSubstitution")
                    : FamilyCoarsePricing.Cpu(FamilyId, "InitialRooms.OrdinaryWorkEnvelope", 243 + work,
                        _ordinary is FamilyCpuExecution cpu ? cpu.Workers : _cpu.Workers, request,
                        "OrdinaryOwnerReferenceUnavailable;SourceWorkEnvelope;NoZeroCostSubstitution");
            }
            if (ordinaryQuote is null) return null;
            if (request.ComputationOnly && (ownedFallback || request.CostSnapshot is null))
                ordinaryQuote = FamilyCoarsePricing.LocalComputation(_ordinary, ordinaryQuote, request);
            double? value = request.ComputationOnly ? FamilyCoarsePricing.ComputationMilliseconds(ordinaryQuote, request)
                : FamilyCoarsePricing.PublicMilliseconds(ordinaryQuote, request, ordinaryPass);
            if (value is null) return null;
            ordinaryMs = value.Value;
        }
        var localGeometry = request with { CompactInput = request.CompactInput || _ordinary is not null,
            MeanInputPopulation = inputs * ordinaryPass };
        var local = InitialRoomsWorkEstimate.Local(FamilyId, Plan, localGeometry, _usesGpu, _cpu.Workers);
        var input = SearchSelectivityInput.From(_request);
        var probability = Plan.CoversTreasure ? TreasureRelicProbability.Estimate(input).Probability :
            FirstActUnknownEventProbability.Estimate(input).Probability;
        double localPass = probability is { } q && double.IsFinite(q) && q is >= 0 and <= 1 ? q : 1;
        if (local is null) return null;
        if (request.ComputationOnly) local = FamilyCoarsePricing.LocalComputation(this, local, localGeometry);
        double? localTime = request.ComputationOnly ? FamilyCoarsePricing.ComputationMilliseconds(local, localGeometry)
            : FamilyCoarsePricing.PublicMilliseconds(local, localGeometry, localPass);
        if (localTime is not { } localMs) return null;
        return new(FamilyId, request.ComputationOnly ? "InitialRooms.OwnedComputationWorkEstimate.20261028.v1"
                : "InitialRooms.OwnedPublicWorkEstimate.20261028.v1", (ordinaryMs + localMs) * 1e6 / inputs,
            _usesGpu ? InitialRoomsPlan.Capacity : _cpu.WindowCapacity, null,
            FamilyCoarsePricing.Revision + ";PriceQuality=Estimate;Model=ReusableCoarse;FamilyLocal;" +
            (request.ComputationOnly ? "ComputationOnly;NumericalAndDeviceEmission;NoSubmitOrHostReadback;NoCrossFamilyHandoffCost;"
                : "CompleteWithinFamilyPublicInvoke;") + "OrdinaryOnceThenLocalOnModeledSurvivors;" +
            $"OrdinaryPass={ordinaryPass:G17};LocalPass={localPass:G17};UnknownPass=ConservativeOne;" +
            "AlreadyCompleteOrdinaryHasNoSecondOutputCharge;NoCrossFamilyConnectionOrExactCost;" +
            "OrdinaryEvidence=" + (ordinaryQuote?.Evidence ?? "None") + ";LocalEvidence=" + local.Evidence,
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: request.ComputationOnly
                ? (_usesGpu ? "DeviceFamilyWork" : "CpuOrderedAbi1") : "CompleteCanonicalAbi1")
            { LocalCostSource = request.ComputationOnly ? "ComponentComposition" : "ReferenceEstimate", ReferencePeakThroughput = 0 };
    }
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
        Revision + (_usesGpu ? ".Gpu" : ".Cpu.P" + _cpu.Workers) + (compact ? ".CompactAbi1" : ".Dense") + ".CanonicalAbi1Ready" +
            (_usesGpu ? GpuRngRotation.Revision : ""),
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
