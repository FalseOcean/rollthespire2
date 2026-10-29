using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// One parameterized complete allocation over whole registered Families. No inner
// Family stages, arbitrary graph, root/RNG state, or mutable continuation edges.
internal sealed partial class AutomaticPrivateSerialInvocation : IFamilyInvocation
{
    internal static bool TrySelectExplicit(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, out FamilyExecutionPlan? selected)
    {
        selected = null;
        string mode = System.Environment.GetEnvironmentVariable("RT2_PRIVATE_SERIAL_EXPERIMENT") ?? "";
        if (mode.Length == 0) return false;
        if (NwaePhysicalExperiment.Mode.Length != 0 || NcPhysicalExperiment.Mode.Length != 0 || EwPhysicalExperiment.Mode.Length != 0 ||
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("RT2_RSW_EXPERIMENT")))
            throw new InvalidOperationException("AutomaticPrivateSerial.ConflictingControl");
        var parts = mode.Split('-');
        if (parts.Length != 2 || parts[0] is not ("ordinary" or "private" or "verify") ||
            parts[1].Length != registered.Count || parts[1].Distinct().Count() != registered.Count ||
            !parts[1].ToHashSet().SetEquals(registered.Select(f => f.FamilyId[0])))
            throw new InvalidOperationException("AutomaticPrivateSerial.ExplicitCompleteOrderRequired");
        var ordered = parts[1].Select(c => registered.Single(f => f.FamilyId[0] == c)).ToArray();
        selected = FamilyPlanner.InOrder(parts[0] == "ordinary" ? ordered :
            [new AutomaticPrivateSerialInvocation(request, ordered, parts[0] == "verify")]) with
        {
            PlanId = "AutomaticPrivateSerial." + mode,
            SelectionPolicyId = "AutomaticPrivateSerial.ExplicitUnpriced.20260910.v1",
            RankingAuthority = FamilyPlannerRankingAuthority.UnpricedPhysicalTrial,
            ExpectedFamilyPipelineMsPerRoot = null,
            DecisionEvidence = ["ExplicitWholeFamilyControl;NoSameRunRepricing"],
            MissingEvidence = ["ExplicitControlNotAutomaticPrice"]
        };
        return true;
    }

    private readonly ExactSearchExecutionRequest _request;
    private readonly IFamilyInvocation[] _families;
    private readonly bool _verify;
    private readonly SmallCandidateExactPolicy? _smallCandidateExact;
    private Gpu? _gpu;
    private bool _faulted, _disposed;
    private int _batches;
    private long _inputs, _outputs;
    private double _ms;
    internal string Order { get; }
    internal IReadOnlyList<IFamilyInvocation> PhysicalFamilies => _families;
    // Estimation follows the already selected public units. This metadata is
    // never consulted by execution, binding, filtering or Exact handoff.
    internal IReadOnlyList<FamilyExecutionPlanStage> EstimationStages { get; }
    internal AutomaticPrivateSerialInvocation(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> orderedFamilies, bool verify = false, bool enableSmallCandidateExact = false,
        IReadOnlyList<FamilyExecutionPlanStage>? estimationStages = null)
    {
        _request = request; _families = orderedFamilies.ToArray(); _verify = verify;
        EstimationStages = estimationStages?.ToArray() ?? [];
        _smallCandidateExact = enableSmallCandidateExact && !verify && !request.RunOptions.SkipExactValidation
            ? new SmallCandidateExactPolicy() : null;
        if (_families.Length is < 2 or > 8 || _families.Select(f => f.FamilyId).Distinct().Count() != _families.Length ||
            _families.Any(f => !f.CanBindPrivateSerial || f.Coverage.Count != 1 || f.Coverage[0] != f.FamilyId))
            throw new ArgumentException("AutomaticPrivateSerial.WholeFamilyApplicability");
        Order = string.Concat(_families.Select(f => f.FamilyId[0]));
    }
    bool IFamilyInvocation.ExecutionFaulted => _faulted || _families.Any(family => family.ExecutionFaulted);
    GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements => _families.Aggregate(GpuNumericRequirements.None, (requirements, family) => requirements | family.GpuNumericRequirements);
    public string FamilyId => _families[0].FamilyId;
    public IReadOnlyList<string> Coverage => _families.Select(f => f.FamilyId).ToArray();
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, PrivateOrdinalBuffer.Capacity, [], "AutomaticPrivateSerial.CompleteAllocation");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId, "CompletePlanOwnsStagePopulationProjection");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,
        "AutomaticPrivateSerial." + Order + ".CanonicalAbi1Ready.20260910.v1" + GpuRngRotation.Revision, "AutomaticPrivateSerial.Neutral", 1,
        "WholeFamilyOrdinalComposition", usesGpu: true);
    void IFamilyInvocation.LogExecutionEvidence()
    {
        foreach (var sample in (_gpu?.Samples ?? []).Concat(_pairGpu?.Samples ?? [])) sample.WriteSummary();
    }
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks =>
        (_gpu?.Samples ?? []).Concat(_pairGpu?.Samples ?? []).SelectMany(x => x.Peaks);
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, _gpu?.Device ?? "",
        (_gpu?.SetupMs ?? 0) + (_pairGpu?.SetupMs ?? 0), 0, 0, _batches, 0, _inputs, _outputs, _ms, false,
        _faulted ? "PhysicalFailure" : "AutomaticPrivateSerial.NoStageCapabilityCalibration");
    public async ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context,
        FamilyObservationWindow window, FamilyCandidateSet input, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted || _request.SnapshotFingerprint != window.ExactRequest.SnapshotFingerprint || !input.IsDense)
            throw new InvalidOperationException("AutomaticPrivateSerial.InvocationInvariant");
        try
        {
            _gpu ??= await context.ExecuteGpuAsync(rd => new Gpu(rd, _families), token).ConfigureAwait(false);
            var run = await context.ExecuteGpuAsync(_ => _gpu.Execute(input, Order, _verify, _smallCandidateExact, token), token).ConfigureAwait(false);
            Volatile.Write(ref _lastDensePrefixCount, _gpu.LastDensePrefixCount);
            if (_verify)
            {
                var expected = input;
                for (int i = 0; i < _families.Length; i++)
                {
                    expected = await _families[i].InvokeAsync(context, window, expected, token).ConfigureAwait(false);
                    if (!expected.EnumerateLogicalOrdinals().SequenceEqual(run.Snapshots[i].EnumerateLogicalOrdinals()))
                        throw new InvalidDataException("AutomaticPrivateSerial.FullPopulationMismatch:" + _families[i].FamilyId);
                }
            }
            _batches++; _inputs += input.Count; _outputs += run.Output.Count; _ms += run.Ms;
            return run.Output;
        }
        catch (OperationCanceledException) { throw; }
        catch { _faulted = true; throw; }
    }
    public async ValueTask DisposeAsync(FamilyExecutionContext context)
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> failures = [];
        if (_gpu is not null || _pairGpu is not null)
            try { await context.ExecuteGpuAsync(_ => { PrivateOrdinalBuffer.DisposeAll(_pairGpu, _gpu); return true; }, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex); }
        if (_verify)
            foreach (var family in _families)
                try { await family.DisposeAsync(context).ConfigureAwait(false); }
                catch (Exception ex) { failures.Add(ex); }
        FamilyAuxiliaryEvidence.Read(nameof(AutomaticPrivateSerialInvocation), "WriteSummary", () =>
        {
            Bootstrap.RuntimeLog.TryBackgroundInfo($"automaticPrivateSerialSummary=true;order={Order};batches={_batches};input={_inputs};output={_outputs};canonicalMs={_ms};faulted={_faulted};cleanupFaults={failures.Count};verification={_verify};recovery=None;" +
                (_smallCandidateExact?.GetSummary() ?? "smallCandidateExactEnabled=false") +
                $";densePairGroups={_prefixGroups};densePairDefaultPreference={FamilyDenseBatchGroupingPolicy.DefaultPreference};lastDensePrefixCount={Volatile.Read(ref _lastDensePrefixCount)}");
            return true;
        }, false, _request.SnapshotFingerprint);
        if (failures.Count > 0) throw new AggregateException("AutomaticPrivateSerial.CleanupFailed", failures);
    }

    private sealed partial class Gpu : IDisposable
    {
        private readonly RenderingDevice _rd;
        private readonly PrivateOrdinalBuffer?[] _buffers;
        private readonly FamilyPrivateGpuExecution?[] _executors;
        private readonly IFamilyInvocation[] _families;
        private readonly NeowFamilyGpuExecutor? _denseScratchDonor;
        internal readonly GpuCostSamples[] Samples;
        internal int LastDensePrefixCount { get; private set; } = -1;
        private bool _disposed;
        internal string Device { get; }
        internal double SetupMs { get; private set; }
        internal Gpu(RenderingDevice rd, IFamilyInvocation[] families, NeowFamilyGpuExecutor? denseScratchDonor = null)
        {
            _denseScratchDonor = denseScratchDonor;
            _rd = rd;
            _families = families; Samples = families.Select(_ => new GpuCostSamples()).ToArray();
            _buffers = new PrivateOrdinalBuffer?[Math.Min(2, families.Length - 1)];
            _executors = new FamilyPrivateGpuExecution?[families.Length];
            Device = rd.GetDeviceName();
        }
        internal (FamilyCandidateSet Output, double Ms, FamilyCandidateSet[] Snapshots) Execute(
            FamilyCandidateSet input, string order, bool verify, SmallCandidateExactPolicy? smallCandidateExact,
            CancellationToken token, FamilyPrivateGpuOutcome? preparedPrefix = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (input.Batch.BatchCandidateCount > PrivateOrdinalBuffer.Capacity)
                throw new InvalidDataException("AutomaticPrivateSerial.InputOutsideCapacity");
            int count = input.Count;
            var snapshots = verify ? new FamilyCandidateSet[_executors.Length] : [];
            FamilyCandidateSet? final = input, smallOutput = null;
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < _executors.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int before = count;
                if (count == 0 || smallCandidateExact is not null && SmallCandidateExactPolicy.ShouldHandoff(count))
                {
                    final = count == 0 ? FamilyCandidateSet.FromSortedAbi1(input.Batch, []) :
                        i == 0 ? FamilyCandidateSet.FromSortedAbi1(input.Batch, input.EnumerateLogicalOrdinals().ToArray()) :
                        smallOutput ?? _buffers[(i - 1) % 2]!.ReadAbi1ForExact(input.Batch, count);
                    token.ThrowIfCancellationRequested();
                    smallCandidateExact?.Observe(i == 0 ? "Dense" :
                        string.Join(',', _families.Take(i).SelectMany(family => family.Coverage)),
                        _families.Skip(i).SelectMany(family => family.Coverage).ToArray(), count);
                    if (verify) Array.Fill(snapshots, final, i, snapshots.Length - i);
                    break;
                }
                if (_families[i].ExecutionFaulted)
                    throw new InvalidOperationException("PhysicalRecoveryRejected:" + _families[i].FamilyId);
                timer.Stop();
                var executor = EnsureExecutor(i);
                timer.Start();
                var result = i == 0 && preparedPrefix is { } prefix
                    ? executor.CompletePrepared(input.Batch, count, prefix) : executor.Execute(input.Batch, count, token);
                if (i != 0 || preparedPrefix is null)
                    Samples[i].Observe(_families[i], new(i != 0, i != _executors.Length - 1, before, i != 0), before, result.DispatchSyncMs, verify, result.Count, result.CanonicalMs, result.ReportedReadbackMs, result.ReadbackBytes);
                count = result.Count; final = result.PublicOutput; smallOutput = result.SmallOutput;
                if (i == 0) LastDensePrefixCount = result.Count;
                if (Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"automaticPrivateSerialStage=true;order={order};stage={order[i]};input={before};output={count};dispatches={result.Dispatches};dispatchSyncMs={result.DispatchSyncMs};reportedReadbackMs={result.ReportedReadbackMs};readbackBytes={result.ReadbackBytes};canonicalMs={result.CanonicalMs};boundary={(i == _executors.Length - 1 ? "CanonicalAbi1Ready" : "PrivateOrdinalCountReady")};smallOutputAbi1Ready={result.SmallOutput is not null};intermediatePayloadBytes=0;verification={verify}");
                if (verify)
                {
                    timer.Stop();
                    snapshots[i] = final ?? _buffers[i % 2]!.ReadForVerification(input.Batch, count);
                    timer.Start();
                }
            }
            timer.Stop();
            if (final is null) throw new InvalidDataException("AutomaticPrivateSerial.TerminalAbi1Missing");
            return (final, timer.Elapsed.TotalMilliseconds, snapshots);
        }

        internal FamilyPrivateGpuExecution EnsureExecutor(int index)
        {
            if (_executors[index] is { } existing) return existing;
            long started = Stopwatch.GetTimestamp();
            try
            {
                PrivateOrdinalBuffer? incoming = index == 0 ? null : (_buffers[(index - 1) % 2]
                    ?? throw new InvalidDataException("AutomaticPrivateSerial.InputBufferMissing"));
                PrivateOrdinalBuffer? outgoing = index == _families.Length - 1 ? null :
                    (_buffers[index % 2] ??= new PrivateOrdinalBuffer(_rd));
                var executor = index == 0 && _denseScratchDonor is not null && _families[index] is NeowFamily neow
                    ? neow.BindNeowPrivate(_rd, incoming, outgoing, _denseScratchDonor)
                    : _families[index].BindPrivateSerial(_rd, incoming, outgoing);
                _executors[index] = executor;
                RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=PrivateFamilyBound;family={_families[index].FamilyId};ownerThreadId={System.Environment.CurrentManagedThreadId};elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F4}");
                return executor;
            }
            finally { SetupMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PrivateOrdinalBuffer.DisposeAll(_executors.AsEnumerable().Reverse().Cast<IDisposable?>().Concat(_buffers).ToArray());
        }
    }
}
