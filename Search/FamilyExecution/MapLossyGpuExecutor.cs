using System.Numerics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// RenderingDevice calls are confined to the existing session GPU owner. Buffers
// persist across calls; the complete bitmap is cleared before every dispatch.
internal sealed partial class MapLossyGpuExecutor : IDisposable
{
    private const int MaxStates = 512;
    private const int RoomTypes = 9;
    private readonly RenderingDevice _rd;
    private readonly int _capacity;
    private Rid _shader, _pipeline, _input, _output, _config, _transitions, _set;
    private MapLossyPlan? _uploadedPlan;
    private PackedPlan? _packedPlan;
    private int _shaderWords, _configBytes = 256, _transitionBytes = MaxStates * RoomTypes * sizeof(uint);
    private bool _pending, _disposed;
    internal int Capacity => _capacity;
    internal long Dispatches { get; private set; }
    internal long RetainedBufferBytes => _capacity * 120L + BitmapBytes + _configBytes + _transitionBytes;
    private int BitmapBytes => (_capacity + 31) / 32 * 4;
    internal MapLossyGpuExecutor(RenderingDevice rd, int capacity)
    {
        _rd = rd; _capacity = capacity;
        if (capacity is not (8192 or 16384 or 32768 or 65536)) throw new ArgumentOutOfRangeException(nameof(capacity));
        try
        {
            // At most three same-seed Acts, each with its own exact RNG prefix.
            _input = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, capacity * 120, "MapLossy.Input");
            _output = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, BitmapBytes, "MapLossy.Bitmap");
            _config = FamilyGpuComputeUtility.AllocateStorageBuffer(rd, _configBytes, "MapLossy.Config");
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
        token.ThrowIfCancellationRequested();
        if (plan.Predicates.Any(p => p.Program.AlwaysReject) || input.Count == 0)
            return FamilyCandidateSet.FromSortedAbi1(input.Batch, []);
        if (!plan.Enabled) return input;
        PackedPlan packed = EnsurePlan(plan);
        token.ThrowIfCancellationRequested();
        ReadOnlyMemory<ulong> compact = input.IsDense ? default : input.ExportAbi1();
        var result = new List<ulong>();
        for (int first = 0; first < input.Count; first += _capacity)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(_capacity, input.Count - first);
            var ordinals = new ulong[count];
            if (input.IsDense) for (int i = 0; i < count; i++) ordinals[i] = (ulong)(first + i);
            else compact.Span.Slice(first, count).CopyTo(ordinals);
            byte[] data = Prepare(input.Batch, ordinals, packed.Acts, ascension, token);
            packed.Config[0] = (uint)count;
            try
            {
                Update(_input, data); Update(_config, FamilyGpuComputeUtility.ToBytes(packed.Config));
                if (poisonBitmap) { var poison = new byte[BitmapBytes]; Array.Fill(poison, (byte)255); Update(_output, poison); }
                Check(_rd.BufferClear(_output, 0, (uint)BitmapBytes), "BufferClear");
                long list = _rd.ComputeListBegin();
                _rd.ComputeListBindComputePipeline(list, _pipeline);
                _rd.ComputeListBindUniformSet(list, _set, 0);
                _rd.ComputeListDispatch(list, (uint)((count + 31) / 32), 1, 1); _rd.ComputeListEnd();
                // Submitted commands are drained before cancellation or disposal.
                _rd.Submit(); _pending = true; _rd.Sync(); _pending = false; Dispatches++;
                token.ThrowIfCancellationRequested();
                byte[] bitmap = _rd.BufferGetData(_output, 0, (uint)BitmapBytes);
                if (bitmap.Length != BitmapBytes) throw new InvalidDataException("MapLossy.ReadbackLengthMismatch");
                foreach (int index in DecodeBitmap(bitmap, count)) result.Add(ordinals[index]);
            }
            catch (Exception cause)
            {
                try { CompletePending(); } catch (Exception cleanup) { cause.Data["MapLossy.SyncCleanup"] = cleanup.ToString(); }
                throw;
            }
        }
        token.ThrowIfCancellationRequested();
        return FamilyCandidateSet.FromSortedAbi1(input.Batch, result.ToArray());
    }

    private PackedPlan EnsurePlan(MapLossyPlan plan)
    {
        if (ReferenceEquals(_uploadedPlan, plan)) return _packedPlan!;
        PackedPlan packed = Pack(plan);
        int configBytes = checked(packed.Config.Length * sizeof(uint));
        int transitionBytes = checked(packed.Transitions.Length * sizeof(uint));
        bool growConfig = configBytes > _configBytes, growTransitions = transitionBytes > _transitionBytes;
        if (packed.WordCount > _shaderWords || growConfig || growTransitions)
        {
            const string marker = "#define STATE_WORDS 16";
            string source = FamilyGpuComputeUtility.LoadEmbeddedShader("MapLossyAssignment.comp.glsl");
            if (!source.Contains(marker, StringComparison.Ordinal))
                throw new InvalidDataException("MapLossy.DrawingShaderStorageMarkerMissing");
            int words = Math.Max(packed.WordCount, _shaderWords);
            source = source.Replace(marker, "#define STATE_WORDS " + words, StringComparison.Ordinal);
            Rid shader = default, pipeline = default, set = default;
            Rid config = growConfig ? default : _config, transitions = growTransitions ? default : _transitions;
            try
            {
                if (growConfig) config = FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, configBytes, "MapLossy.Config");
                if (growTransitions) transitions = FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, transitionBytes, "MapLossy.Transitions");
                shader = FamilyGpuComputeUtility.CompileShader(_rd, source, "MapLossyDrawingPropertiesRound1");
                pipeline = FamilyGpuComputeUtility.CreateComputePipeline(_rd, shader, "MapLossy");
                set = FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, [_input, _output, config, transitions], "MapLossy");
            }
            catch (Exception cause)
            {
                var failed = new[] { shader, pipeline, growConfig ? config : default, growTransitions ? transitions : default, set };
                try { FreeResources(failed); }
                catch (Exception cleanup) { cause.Data["MapLossy.ProgramCreateCleanup"] = cleanup.ToString(); }
                throw;
            }
            var old = new[] { _shader, _pipeline, growConfig ? _config : default, growTransitions ? _transitions : default, _set };
            (_shader, _pipeline, _set, _config, _transitions) = (shader, pipeline, set, config, transitions);
            _shaderWords = words;
            _configBytes = Math.Max(configBytes, _configBytes); _transitionBytes = Math.Max(transitionBytes, _transitionBytes);
            _uploadedPlan = null; _packedPlan = null;
            FreeResources(old);
        }
        // Re-upload the full active transition table even after a long-to-short
        // switch. Every route DP starts empty and addresses only its own table.
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
    private void CompletePending() { if (_pending) { _rd.Sync(); _pending = false; } }
    public void Dispose()
    {
        if (_disposed) return;
        CompletePending();
        var owned = new[] { _shader, _pipeline, _input, _output, _config, _transitions, _set };
        _disposed = true;
        FreeResources(owned);
    }
}
