using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// One parameterized complete allocation over whole registered Families. No inner
// Family stages, arbitrary graph, root/RNG state, or mutable continuation edges.
internal sealed class AutomaticPrivateSerialInvocation : IFamilyInvocation
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
    private Gpu? _gpu;
    private bool _faulted, _disposed;
    private int _batches;
    private long _inputs, _outputs;
    private double _ms;
    internal string Order { get; }
    internal AutomaticPrivateSerialInvocation(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> orderedFamilies, bool verify = false)
    {
        _request = request; _families = orderedFamilies.ToArray(); _verify = verify;
        if (_families.Length is < 2 or > 7 || _families.Select(f => f.FamilyId).Distinct().Count() != _families.Length ||
            _families.Any(f => !f.CanBindPrivateSerial || f.Coverage.Count != 1 || f.Coverage[0] != f.FamilyId))
            throw new ArgumentException("AutomaticPrivateSerial.WholeFamilyApplicability");
        Order = string.Concat(_families.Select(f => f.FamilyId[0]));
    }
    public string FamilyId => _families[0].FamilyId;
    public IReadOnlyList<string> Coverage => _families.Select(f => f.FamilyId).ToArray();
    public FamilyAnalyticalCostProjection AnalyticalCost => new(FamilyId, PrivateOrdinalBuffer.Capacity, [], "AutomaticPrivateSerial.CompleteAllocation");
    public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(FamilyId, "CompletePlanOwnsStagePopulationProjection");
    public FamilyConditionPerformanceProjection ConditionPerformance => new(FamilyId,
        "AutomaticPrivateSerial." + Order + ".CanonicalAbi1Ready.20260910.v1", "AutomaticPrivateSerial.Neutral", 1,
        "WholeFamilyOrdinalComposition", usesGpu: true);
    void IFamilyInvocation.LogExecutionEvidence() { if(_gpu is not null) foreach(var sample in _gpu.Samples) sample.WriteSummary(); }
    IEnumerable<GpuLocalPeak> IFamilyInvocation.CostPeaks => _gpu?.Samples.SelectMany(x => x.Peaks) ?? [];
    public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, _gpu?.Device ?? "",
        _gpu?.SetupMs ?? 0, 0, 0, _batches, 0, _inputs, _outputs, _ms, false,
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
            var run = await context.ExecuteGpuAsync(_ => _gpu.Execute(input, Order, _verify, token), token).ConfigureAwait(false);
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
        if (_gpu is not null)
            try { await context.ExecuteGpuAsync(_ => { _gpu.Dispose(); return true; }, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex); }
        if (_verify)
            foreach (var family in _families)
                try { await family.DisposeAsync(context).ConfigureAwait(false); }
                catch (Exception ex) { failures.Add(ex); }
        Bootstrap.RuntimeLog.TryBackgroundInfo($"automaticPrivateSerialSummary=true;order={Order};batches={_batches};input={_inputs};output={_outputs};canonicalMs={_ms};faulted={_faulted};cleanupFaults={failures.Count};verification={_verify};recovery=None");
        if (failures.Count > 0) throw new AggregateException("AutomaticPrivateSerial.CleanupFailed", failures);
    }

    private sealed class Gpu : IDisposable
    {
        private readonly PrivateOrdinalBuffer[] _buffers;
        private readonly FamilyPrivateGpuExecution[] _executors;
        private readonly IFamilyInvocation[] _families;
        internal readonly GpuCostSamples[] Samples;
        private bool _disposed;
        internal string Device { get; }
        internal double SetupMs { get; }
        internal Gpu(RenderingDevice rd, IFamilyInvocation[] families)
        {
            _families = families; Samples = families.Select(_ => new GpuCostSamples()).ToArray();
            _buffers = new PrivateOrdinalBuffer[Math.Min(2, families.Length - 1)];
            _executors = new FamilyPrivateGpuExecution[families.Length];
            var watch = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < _buffers.Length; i++) _buffers[i] = new(rd);
                for (int i = 0; i < families.Length; i++)
                {
                    long stageStarted=Stopwatch.GetTimestamp();
                    _executors[i] = families[i].BindPrivateSerial(rd,
                        i == 0 ? null : _buffers[(i - 1) % 2], i == families.Length - 1 ? null : _buffers[i % 2]);
                    RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=PrivateFamilyBound;family={families[i].FamilyId};ownerThreadId={System.Environment.CurrentManagedThreadId};elapsedMs={Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds:F4}");
                }
                Device = rd.GetDeviceName(); SetupMs = watch.Elapsed.TotalMilliseconds;
            }
            catch (Exception initialization)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("AutomaticPrivateSerial.InitializationAndCleanup", initialization, cleanup); }
                throw;
            }
        }
        internal (FamilyCandidateSet Output, double Ms, FamilyCandidateSet[] Snapshots) Execute(
            FamilyCandidateSet input, string order, bool verify, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _buffers[0].CheckPopulation(input.Batch, input.Count);
            int count = input.Count;
            var snapshots = verify ? new FamilyCandidateSet[_executors.Length] : [];
            FamilyCandidateSet? final = null;
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < _executors.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int before = count;
                if (count == 0)
                {
                    Samples[i].Observe(_families[i], new(i != 0, i != _executors.Length - 1, 0, i != 0), 0, 0, verify);
                    final = FamilyCandidateSet.FromSortedAbi1(input.Batch, []);
                    if (verify) snapshots[i] = final;
                    if (Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"automaticPrivateSerialStage=true;order={order};stage={order[i]};input=0;output=0;dispatches=0;dispatchSyncMs=0;reportedReadbackMs=0;readbackBytes=0;canonicalMs=0;zeroSkipped=true;intermediatePayloadBytes=0;verification={verify}");
                    continue;
                }
                var result = _executors[i].Execute(input.Batch, count, token);
                Samples[i].Observe(_families[i], new(i != 0, i != _executors.Length - 1, before, i != 0), before, result.DispatchSyncMs, verify, result.Count, result.CanonicalMs, result.ReportedReadbackMs, result.ReadbackBytes);
                count = result.Count; final = result.PublicOutput;
                if (Bootstrap.RuntimeLog.DetailEnabled) Bootstrap.RuntimeLog.TryBackgroundDetail($"automaticPrivateSerialStage=true;order={order};stage={order[i]};input={before};output={count};dispatches={result.Dispatches};dispatchSyncMs={result.DispatchSyncMs};reportedReadbackMs={result.ReportedReadbackMs};readbackBytes={result.ReadbackBytes};canonicalMs={result.CanonicalMs};boundary={(i == _executors.Length - 1 ? "CanonicalAbi1Ready" : "PrivateOrdinalCountReady")};intermediatePayloadBytes=0;verification={verify}");
                if (verify)
                {
                    timer.Stop();
                    snapshots[i] = final ?? _buffers[i % 2].ReadForVerification(input.Batch, count);
                    timer.Start();
                }
            }
            timer.Stop();
            final ??= FamilyCandidateSet.FromSortedAbi1(input.Batch, []);
            return (final, timer.Elapsed.TotalMilliseconds, snapshots);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PrivateOrdinalBuffer.DisposeAll(_executors.AsEnumerable().Reverse().Cast<IDisposable?>().Concat(_buffers).ToArray());
        }
    }
}
