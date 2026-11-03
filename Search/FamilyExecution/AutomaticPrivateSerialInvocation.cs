using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class AutomaticPrivateSerialInvocation
{
    internal static bool TrySelectExplicit(PreparedSearch request,
        IReadOnlyList<FilterBodyDescription> registered, out FamilyExecutionPlan? selected)
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
            [Prepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), ordered, parts[0] == "verify")]) with {
            PlanId = "AutomaticPrivateSerial." + mode, SelectionPolicyId = "AutomaticPrivateSerial.ExplicitUnpriced.20260910.v1",
            RankingAuthority = FamilyPlannerRankingAuthority.UnpricedPhysicalTrial, ExpectedFamilyPipelineMsPerRoot = null,
            DecisionEvidence = ["ExplicitWholeFamilyControl;NoSameRunRepricing"], MissingEvidence = ["ExplicitControlNotAutomaticPrice"]
        };
        return true;
    }

    internal static FilterBodyDescription Prepare(SearchModel model, FilterExecutionPreferences preferences,
        IReadOnlyList<FilterBodyDescription> orderedFamilies, bool verify = false, bool enableSmallCandidateExact = false,
        IReadOnlyList<FamilyExecutionPlanStage>? estimationStages = null, IReadOnlyList<string>? followingCoverage = null)
    {
        var families = orderedFamilies.ToArray();
        if (families.Length is < 2 or > 8 || families.Select(f => f.FamilyId).Distinct().Count() != families.Length ||
            families.Any(f => !f.CanBindPrivateSerial || f.Coverage.Count != 1 || f.Coverage[0] != f.FamilyId))
            throw new ArgumentException("AutomaticPrivateSerial.WholeFamilyApplicability");
        string id = families[0].FamilyId, order = string.Concat(families.Select(f => f.FamilyId[0]));
        bool smallExact = enableSmallCandidateExact && !verify && !preferences.SkipExactValidation;
        var following = followingCoverage?.ToArray() ?? [];
        string revision = "AutomaticPrivateSerial." + order + ".CanonicalAbi1Ready.20260910.v1" + GpuRngRotation.Revision;
        var preparation = new PreparedFilter(id) {
            Coverage = families.Select(f => f.FamilyId).ToArray(),
            Obligations = families.SelectMany(f => f.Preparation.EffectiveObligations).ToArray(),
            AnalyticalCost = new(id, PrivateOrdinalBuffer.Capacity, [], "AutomaticPrivateSerial.WholeFamilySegment"),
            Survival = FamilySurvivalProjection.Unresolved(id, "CompletePlanOwnsStagePopulationProjection")
        };
        return new(preparation, revision, model.ProfileId) {
            UsesGpu = true, InputModes = FilterInputModes.Dense, InputIdentity = model.SnapshotFingerprint,
            PlayersCount = model.Authority.PlayersCount, CpuWorkers = preferences.WorkerCount,
            WindowCapacity = PrivateOrdinalBuffer.Capacity,
            NumericRequirements = families.Aggregate(GpuNumericRequirements.None, (requirements, family) => requirements | family.NumericRequirements),
            PhysicalBodies = families, IsPrivateTransport = true, EstimationStages = estimationStages?.ToArray() ?? [],
            RequiresBonesCapsuleDiagnostics = families.Any(f => f.RequiresBonesCapsuleDiagnostics),
            PerformanceProjection = _ => new(id, revision, "AutomaticPrivateSerial.Neutral", 1, "WholeFamilyOrdinalComposition", true),
            CreateExecution = (body, services) => new Runtime(body, families, verify, smallExact, following, services)
        };
    }

    private sealed partial class Runtime : IFilterExecution, IDenseFamilyBatchGrouping
    {
        public FilterBodyDescription Body { get; }
        public bool ExecutionFaulted => _faulted || (_references?.Any(f => f.ExecutionFaulted) ?? false);
        public (string Device, double SetupMs) ResourceInfo => (_gpu?.Device ?? "", (_gpu?.SetupMs ?? 0) + (_pairGpu?.SetupMs ?? 0));
        private readonly FilterBodyDescription[] _families;
        private readonly IFilterExecution[]? _references;
        private readonly bool _verify;
        private readonly SmallCandidateExactPolicy? _smallCandidateExact;
        private readonly string[] FollowingCoverage;
        private Gpu? _gpu;
        private bool _faulted, _disposed;
        private int _batches;
        private long _inputs, _outputs;
        private double _ms;
        private string Order => string.Concat(_families.Select(f => f.FamilyId[0]));
        internal Runtime(FilterBodyDescription body, FilterBodyDescription[] families, bool verify,
            bool smallExact, string[] followingCoverage, FilterRuntimeServices services)
        {
            Body = body; _families = families; _verify = verify; FollowingCoverage = followingCoverage;
            _smallCandidateExact = smallExact ? new SmallCandidateExactPolicy() : null;
            if (verify) _references = families.Select(f =>
                (f.CreateExecution ?? throw new InvalidOperationException("AutomaticPrivateSerial.ReferenceFactoryMissing"))(f, services)).ToArray();
        }
        public async ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted || !input.IsDense) throw new InvalidOperationException("AutomaticPrivateSerial.InvocationInvariant");
            try {
                _gpu ??= await services.ExecuteGpuAsync(rd => new Gpu(rd, _families, services.Costs), token).ConfigureAwait(false);
                var run = await services.ExecuteGpuAsync(_ => _gpu.Execute(input, Order, _verify, _smallCandidateExact, FollowingCoverage, token), token).ConfigureAwait(false);
                Volatile.Write(ref _lastDensePrefixCount, _gpu.LastDensePrefixCount);
                if (_verify) {
                    var expected = input;
                    for (int i = 0; i < _references!.Length; i++) {
                        expected = await _references[i].FilterAsync(services, expected, token).ConfigureAwait(false);
                        if (!expected.EnumerateLogicalOrdinals().SequenceEqual(run.Snapshots[i].EnumerateLogicalOrdinals()))
                            throw new InvalidDataException("AutomaticPrivateSerial.FullPopulationMismatch:" + _families[i].FamilyId);
                    }
                }
                _batches++; _inputs += input.Count; _outputs += run.Output.Count; _ms += run.Ms;
                // The transport wrapper has no single-stage calibration scope;
                // real child measurements are published separately by Gpu.Execute.
                services.Costs.ObserveCompletedGroup(Body, !input.IsDense, input.Count, run.Output.Count,
                    1, run.Ms, includeLive: _batches > 1);
                return run.Output;
            }
            catch (OperationCanceledException) { throw; }
            catch { _faulted = true; throw; }
        }
        public async ValueTask DisposeAsync(FilterRuntimeServices services)
        {
            if (_disposed) return;
            _disposed = true;
            var failures = new List<Exception>();
            if (_gpu is not null || _pairGpu is not null)
                try { await services.ExecuteGpuAsync(_ => { PrivateOrdinalBuffer.DisposeAll(_pairGpu, _gpu); return true; }, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            foreach (var reference in _references ?? [])
                try { await reference.DisposeAsync(services).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            RuntimeLog.TryBackgroundInfo($"automaticPrivateSerialSummary=true;order={Order};followingCoverage={string.Join(',', FollowingCoverage)};batches={_batches};input={_inputs};output={_outputs};canonicalMs={_ms};faulted={_faulted};cleanupFaults={failures.Count};verification={_verify};recovery=None;" +
                (_smallCandidateExact?.GetSummary() ?? "smallCandidateExactEnabled=false") + $";densePairGroups={_prefixGroups};densePairDefaultPreference={FamilyDenseBatchGroupingPolicy.DefaultPreference};lastDensePrefixCount={Volatile.Read(ref _lastDensePrefixCount)}");
            if (failures.Count > 0) throw new AggregateException("AutomaticPrivateSerial.CleanupFailed", failures);
        }
    private sealed partial class Gpu : IDisposable
    {
        private readonly RenderingDevice _rd;
        private readonly PrivateOrdinalBuffer?[] _buffers;
        private readonly FamilyPrivateGpuExecution?[] _executors;
        private readonly FilterBodyDescription[] _families;
        private readonly IPrivateGpuScratchLease? _denseScratchDonor;
        private readonly FilterCostObserver _observer;
        internal int LastDensePrefixCount { get; private set; } = -1;
        private bool _disposed;
        internal string Device { get; }
        internal double SetupMs { get; private set; }
        internal Gpu(RenderingDevice rd, FilterBodyDescription[] families, FilterCostObserver observer, IPrivateGpuScratchLease? denseScratchDonor = null)
        {
            _denseScratchDonor = denseScratchDonor;
            _rd = rd;
            _families = families; _observer = observer;
            _buffers = new PrivateOrdinalBuffer?[Math.Min(2, families.Length - 1)];
            _executors = new FamilyPrivateGpuExecution?[families.Length];
            Device = rd.GetDeviceName();
        }
        internal (FamilyCandidateSet Output, double Ms, FamilyCandidateSet[] Snapshots) Execute(
            FamilyCandidateSet input, string order, bool verify, SmallCandidateExactPolicy? smallCandidateExact,
            IReadOnlyList<string> followingCoverage, CancellationToken token, FamilyPrivateGpuOutcome? preparedPrefix = null)
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
                        [.._families.Skip(i).SelectMany(family => family.Coverage), ..followingCoverage], count);
                    if (verify) Array.Fill(snapshots, final, i, snapshots.Length - i);
                    break;
                }
                timer.Stop();
                var executor = EnsureExecutor(i);
                timer.Start();
                var result = i == 0 && preparedPrefix is { } prefix
                    ? executor.CompletePrepared(input.Batch, count, prefix) : executor.Execute(input.Batch, count, token);
                if (i != 0 || preparedPrefix is null)
                    _observer.ObserveGpuCompleted(_families[i], new(i != 0, i != _executors.Length - 1, before, i != 0), before, result.Count, result.DispatchSyncMs, result.CanonicalMs, result.ReportedReadbackMs, result.ReadbackBytes, verify);
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
                var executor = index == 0 && _denseScratchDonor is not null
                    ? (_families[index].BindPrivateShared ?? throw new InvalidOperationException("AutomaticPrivateSerial.SharedScratchBindingUnavailable"))(_rd, incoming, outgoing, _denseScratchDonor)
                    : _families[index].BindPrivate!(_rd, incoming, outgoing);
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
}
