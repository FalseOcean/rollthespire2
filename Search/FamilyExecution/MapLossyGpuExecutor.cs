using System.Diagnostics;
using System.Numerics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// RenderingDevice calls remain on the session GPU owner. Each in-flight slot has
// distinct input, bitmap and count config; plan transitions are immutable until
// every submitted slot has been drained.
internal sealed partial class MapLossyGpuExecutor : IDisposable
{
    private const int MaxStates = 512;
    private const int RoomTypes = 9;
    private readonly RenderingDevice _rd;
    private readonly int _capacity;
    private readonly FilterRuntimeServices? _context;
    private readonly bool _prefetch;
    private readonly Slot[] _slots;
    private Rid _shader, _pipeline, _transitions;
    private MapLossyPlan? _uploadedPlan;
    private PackedPlan? _packedPlan;
    private int _shaderWords, _configBytes = 256, _transitionBytes = MaxStates * RoomTypes * sizeof(uint);
    private bool _shaderUsesChildShuffle;
    private bool _pending, _recorded, _disposed;
    private int _queuedDispatches, _pendingDispatches;
    private sealed class Slot { internal Rid Input, Output, Config, Set; }
    internal int Capacity => _capacity;
    internal string PipelineMode { get; }
    internal long Dispatches { get; private set; }
    internal long RetainedBufferBytes => _slots.Length * (_capacity * 120L + BitmapBytes + _configBytes) + _transitionBytes;
    private int BitmapBytes => (_capacity + 31) / 32 * 4;
    // Only native checks install a probe. No environment option changes results.
    internal Action<string>? ExecutionProbe { get; set; }
    internal MapLossyGpuExecutor(RenderingDevice rd, int capacity, FilterRuntimeServices? context = null)
    {
        _rd = rd; _capacity = capacity; _context = context;
        if (capacity is not (8192 or 16384 or 32768 or 65536)) throw new ArgumentOutOfRangeException(nameof(capacity));
        string mode = (System.Environment.GetEnvironmentVariable("RT2_MAP_GPU_PIPELINE") ?? "prefetch").Trim().ToLowerInvariant();
        if (mode.Length == 0) mode = "prefetch";
        if (mode is not ("baseline" or "prefetch" or "grouped" or "combined"))
            throw new ArgumentException("MapLossy.InvalidPipelineMode:" + mode);
        PipelineMode = mode; _prefetch = mode is "prefetch" or "combined";
        _slots = Enumerable.Range(0, mode is "grouped" or "combined" ? 4 : 1).Select(_ => new Slot()).ToArray();
        try
        {
            foreach (var slot in _slots)
            {
                // At most three same-seed Acts, each with its own exact RNG prefix.
                slot.Input = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, capacity * 120, "MapLossy.Input");
                slot.Output = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, BitmapBytes, "MapLossy.Bitmap");
                slot.Config = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, _configBytes, "MapLossy.Config");
            }
            _transitions = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, _transitionBytes, "MapLossy.Transitions");
        }
        catch (Exception cause) { try { Dispose(); } catch (Exception cleanup) { cause.Data["MapLossy.CreateCleanup"] = cleanup.ToString(); } throw; }
    }

    internal FamilyCandidateSet Propose(FamilyCandidateSet input, MapLossyPredicate predicate, int ascension,
        CancellationToken token, bool poisonBitmap = false) =>
        Propose(input, new MapLossyPlan([predicate], []), ascension, token, poisonBitmap);

    internal FamilyCandidateSet Propose(FamilyCandidateSet input, MapLossyPlan plan, int ascension,
        CancellationToken token, bool poisonBitmap = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var metrics = new PhaseMeasurement(input.Count);
        long started = Stopwatch.GetTimestamp();
        using var prepareCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<PreparedChunk>? lookahead = null;
        Exception? failure = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (plan.Predicates.Any(p => p.Program.AlwaysReject) || input.Count == 0)
                return FamilyCandidateSet.FromSortedAbi1(input.Batch, []);
            if (!plan.Enabled) return input;
            long phase = Stopwatch.GetTimestamp();
            PackedPlan packed;
            try { packed = EnsurePlan(plan); }
            finally { metrics.PlanSetupTicks += Stopwatch.GetTimestamp() - phase; }
            token.ThrowIfCancellationRequested();
            ReadOnlyMemory<ulong> compact = input.IsDense ? default : input.ExportAbi1();
            var result = new List<ulong>();
            int next = 0;
            PreparedChunk PrepareNext(int first) => PrepareChunk(input, compact, first, packed.Acts, ascension,
                prepareCancellation.Token, metrics);
            if (_prefetch) { lookahead = Task.Run(() => PrepareNext(0)); next = Math.Min(_capacity, input.Count); }
            while (next < input.Count || lookahead is not null)
            {
                token.ThrowIfCancellationRequested();
                var group = new List<PreparedChunk>(_slots.Length);
                for (int index = 0; index < _slots.Length && (next < input.Count || lookahead is not null); index++)
                {
                    PreparedChunk prepared;
                    if (lookahead is not null)
                    {
                        phase = Stopwatch.GetTimestamp();
                        try { prepared = lookahead.GetAwaiter().GetResult(); }
                        finally { metrics.PreparationWaitTicks += Stopwatch.GetTimestamp() - phase; }
                        lookahead = null;
                    }
                    else { prepared = PrepareNext(next); next += prepared.Ordinals.Length; }
                    token.ThrowIfCancellationRequested();
                    if (_prefetch && next < input.Count)
                    {
                        int first = next; next += Math.Min(_capacity, input.Count - first);
                        lookahead = Task.Run(() => PrepareNext(first));
                    }
                    phase = Stopwatch.GetTimestamp();
                    try { Record(_slots[index], prepared, packed, poisonBitmap); }
                    finally { metrics.UploadRecordTicks += Stopwatch.GetTimestamp() - phase; }
                    group.Add(prepared);
                }
                // One fence covers only independent slots. Preserve selected batch
                // capacity; this groups dispatches rather than changing query geometry.
                SubmitRecorded(metrics);
                ExecutionProbe?.Invoke("Submitted");
                CompletePending(metrics);
                ExecutionProbe?.Invoke("Synchronized");
                token.ThrowIfCancellationRequested();
                phase = Stopwatch.GetTimestamp();
                try
                {
                    for (int index = 0; index < group.Count; index++)
                    {
                        byte[] bitmap = _rd.BufferGetData(_slots[index].Output, 0, (uint)BitmapBytes);
                        if (bitmap.Length != BitmapBytes) throw new InvalidDataException("MapLossy.ReadbackLengthMismatch");
                        var ordinals = group[index].Ordinals;
                        foreach (int survivor in DecodeBitmap(bitmap, ordinals.Length)) result.Add(ordinals[survivor]);
                    }
                }
                finally { metrics.ReadbackDecodeTicks += Stopwatch.GetTimestamp() - phase; }
            }
            token.ThrowIfCancellationRequested();
            phase = Stopwatch.GetTimestamp();
            try { return FamilyCandidateSet.FromSortedAbi1(input.Batch, result.ToArray()); }
            finally { metrics.ReadbackDecodeTicks += Stopwatch.GetTimestamp() - phase; }
        }
        catch (Exception cause) { failure = cause; throw; }
        finally
        {
            // Join the one bounded CPU producer even when cancellation/native work
            // fails. A failed prepare is never interpreted as an empty proposal.
            prepareCancellation.Cancel();
            Exception? cleanupFailure = null;
            try { CompletePending(metrics); }
            catch (Exception cleanup) { cleanupFailure = cleanup; }
            if (lookahead is not null)
            {
                try { lookahead.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) when (prepareCancellation.IsCancellationRequested) { }
                catch (Exception cleanup)
                { cleanupFailure = cleanupFailure is null ? cleanup : new AggregateException(cleanupFailure, cleanup); }
            }
            FamilyAuxiliaryEvidence.Read(nameof(MapLossyGpuExecutor), "PublishPhaseMetrics", () =>
            { PublishMetrics(metrics.Snapshot(Stopwatch.GetTimestamp() - started)); return true; }, false);
            if (cleanupFailure is not null)
            {
                if (failure is not null) failure.Data["MapLossy.DrainOrPrepareCleanup"] = cleanupFailure.ToString();
                else System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }

    private void Record(Slot slot, PreparedChunk prepared, PackedPlan packed, bool poisonBitmap)
    {
        // The slot owns its count. No mutable count config is shared by commands.
        uint[] config = (uint[])packed.Config.Clone(); config[0] = (uint)prepared.Ordinals.Length;
        Update(slot.Input, prepared.Data); Update(slot.Config, FamilyGpuComputeUtility.ToBytes(config));
        if (poisonBitmap) { var poison = new byte[BitmapBytes]; Array.Fill(poison, (byte)255); Update(slot.Output, poison); }
        Check(_rd.BufferClear(slot.Output, 0, (uint)BitmapBytes), "BufferClear");
        _recorded = true;
        long list = _rd.ComputeListBegin();
        Exception? recordingFailure = null;
        try
        {
            _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, slot.Set, 0);
            _rd.ComputeListDispatch(list, (uint)((prepared.Ordinals.Length + 31) / 32), 1, 1);
            _queuedDispatches++;
        }
        catch (Exception cause) { recordingFailure = cause; throw; }
        finally
        {
            try { _rd.ComputeListEnd(); }
            catch (Exception cleanup) when (recordingFailure is not null)
            { recordingFailure.Data["MapLossy.RecordEndCleanup"] = cleanup.ToString(); }
        }
    }

    private PackedPlan EnsurePlan(MapLossyPlan plan)
    {
        if (ReferenceEquals(_uploadedPlan, plan)) return _packedPlan!;
        PackedPlan packed = Pack(plan);
        int configBytes = checked(packed.Config.Length * sizeof(uint));
        int transitionBytes = checked(packed.Transitions.Length * sizeof(uint));
        bool growConfig = configBytes > _configBytes, growTransitions = transitionBytes > _transitionBytes;
        bool childShuffle = Compatibility.RuntimeProfilePolicies.UsesChildStreamShuffle(plan.RngSemantics);
        if (packed.WordCount > _shaderWords || growConfig || growTransitions || childShuffle != _shaderUsesChildShuffle)
        {
            const string marker = "#define STATE_WORDS 16";
            string source = FamilyGpuComputeUtility.LoadEmbeddedShader("MapLossyAssignment.comp.glsl");
            if (!source.Contains(marker, StringComparison.Ordinal))
                throw new InvalidDataException("MapLossy.DrawingShaderStorageMarkerMissing");
            int words = Math.Max(packed.WordCount, _shaderWords);
            source = source.Replace(marker, "#define STATE_WORDS " + words, StringComparison.Ordinal)
                .Replace("#define RT2_MAP_CHILD_SHUFFLE 0", "#define RT2_MAP_CHILD_SHUFFLE " + (childShuffle ? "1" : "0"), StringComparison.Ordinal);
            Rid shader = default, pipeline = default, transitions = growTransitions ? default : _transitions;
            var configs = _slots.Select(s => growConfig ? default(Rid) : s.Config).ToArray();
            var sets = new Rid[_slots.Length];
            try
            {
                if (growTransitions) transitions = FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, transitionBytes, "MapLossy.Transitions");
                shader = FamilyGpuComputeUtility.CompileShader(_rd, source, "MapLossyDrawingPropertiesRound1");
                pipeline = FamilyGpuComputeUtility.CreateComputePipeline(_rd, shader, "MapLossy");
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (growConfig) configs[i] = FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, configBytes, "MapLossy.Config");
                    sets[i] = FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, [_slots[i].Input, _slots[i].Output, configs[i], transitions], "MapLossy");
                }
            }
            catch (Exception cause)
            {
                var failed = new[] { shader, pipeline, growTransitions ? transitions : default }
                    .Concat(growConfig ? configs : []).Concat(sets);
                try { FreeResources(failed); }
                catch (Exception cleanup) { cause.Data["MapLossy.ProgramCreateCleanup"] = cleanup.ToString(); }
                throw;
            }
            // FreeAll releases in reverse dependency order: sets must be freed
            // before a config buffer whose destruction invalidates those sets.
            var old = new[] { _shader, _pipeline, growTransitions ? _transitions : default }
                .Concat(growConfig ? _slots.Select(s => s.Config) : []).Concat(_slots.Select(s => s.Set)).ToArray();
            (_shader, _pipeline, _transitions) = (shader, pipeline, transitions);
            for (int i = 0; i < _slots.Length; i++) { _slots[i].Set = sets[i]; _slots[i].Config = configs[i]; }
            _shaderWords = words;
            _shaderUsesChildShuffle = childShuffle;
            _configBytes = Math.Max(configBytes, _configBytes); _transitionBytes = Math.Max(transitionBytes, _transitionBytes);
            _uploadedPlan = null; _packedPlan = null;
            FreeResources(old);
        }
        // Re-upload the full active transition table after long-to-short switches.
        _uploadedPlan = null; _packedPlan = null;
        if (packed.Transitions.Length > 0) Update(_transitions, FamilyGpuComputeUtility.ToBytes(packed.Transitions));
        _packedPlan = packed; _uploadedPlan = plan;
        return packed;
    }

    private void FreeResources(IEnumerable<Rid> resources) =>
        FamilyGpuComputeUtility.FreeAll(_rd, resources.Where(rid => rid.IsValid).ToList());

    internal static int[] DecodeBitmap(byte[] data, int count)
    {
        if (count < 1 || data.Length % 4 != 0 || data.Length < (count + 31) / 32 * 4)
            throw new ArgumentException("MapLossy.InvalidBitmapShape");
        var indices = new List<int>();
        for (int word = 0; word < data.Length / 4; word++)
        {
            uint bits = BitConverter.ToUInt32(data, word * 4);
            while (bits != 0)
            {
                int bit = BitOperations.TrailingZeroCount(bits); bits &= bits - 1;
                int index = word * 32 + bit;
                if (index >= count) throw new InvalidDataException("MapLossy.StaleBitmapTail");
                indices.Add(index);
            }
        }
        return indices.ToArray();
    }
    private static void Check(Error error, string stage)
    { if (error != Error.Ok) throw new InvalidOperationException($"MapLossy.{stage}:{error}"); }
    private void Update(Rid buffer, byte[] bytes) => Check(_rd.BufferUpdate(buffer, 0, (uint)bytes.Length, bytes), "BufferUpdate");
    private void SubmitRecorded(PhaseMeasurement? metrics)
    {
        long submit = Stopwatch.GetTimestamp();
        try
        {
            _rd.Submit(); _pending = true; _recorded = false;
            _pendingDispatches = _queuedDispatches; _queuedDispatches = 0;
            if (metrics is not null) metrics.SubmissionGroups++;
        }
        finally { if (metrics is not null) metrics.SubmitTicks += Stopwatch.GetTimestamp() - submit; }
    }
    private void CompletePending(PhaseMeasurement? metrics = null)
    {
        // An exception/cancellation between grouped slots can leave recorded
        // lists awaiting submission. Drain those too before any buffer reuse.
        if (_recorded && !_pending)
            SubmitRecorded(metrics);
        if (!_pending) return;
        long phase = Stopwatch.GetTimestamp();
        try
        {
            _rd.Sync(); _pending = false;
            Dispatches += _pendingDispatches;
            if (metrics is not null) metrics.Dispatches += _pendingDispatches;
            _pendingDispatches = 0;
        }
        finally { if (metrics is not null) metrics.SyncWaitTicks += Stopwatch.GetTimestamp() - phase; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        CompletePending();
        var owned = new[] { _shader, _pipeline, _transitions }
            .Concat(_slots.SelectMany(slot => new[] { slot.Input, slot.Output, slot.Config, slot.Set })).ToArray();
        _disposed = true;
        FreeResources(owned);
    }
}
