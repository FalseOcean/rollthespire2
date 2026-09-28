using System.Diagnostics;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct NeowFamilyGpuMetrics(int Processed, int Survivors, uint Groups, long ReadbackBytes,
    double CanonicalMs, double UploadMs, double DispatchSyncMs, double ReadbackMs, double CanonicalizeMs,
    int Dispatches, uint CurseSurvivors, uint PairSurvivors,
    uint NrPairPass, uint ArcanePass, uint CapsuleRarityPass, uint HeavyEntered, double PayloadReadbackMs);

// Session-resident resources on the existing Family GPU owner. Host transport,
// header validation and canonicalization follow the accepted R executor donor.
internal sealed class NeowFamilyGpuExecutor : IDisposable
{
    internal const int Capacity = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
    // Full-capacity Compact transport prevents high-survival upstream N/S from
    // becoming an accidental Exact-only gate. Both workspaces remain bounded.
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = new();
    private readonly Rid _meta, _header, _input, _output, _projectionMetadata;
    private Rid _pipeline, _uniforms;
    private readonly Rid[] _buffers;
    private Rid[]? _stagedBuffers;
    private readonly NeowFamilyGpuPlan _plan;
    private readonly NeowCapsuleComposite? _composite;
    private readonly string? _partySource;
    private readonly int _capsulePhysicalMode;
    private readonly List<(Rid Pipeline, Rid Uniforms)> _stages = new();
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private readonly uint _tag;
    private readonly bool _bonesArcane;
    private readonly int _bonesKMode;
    internal uint[] BonesKCounts { get; private set; } = [];
    private readonly bool _privateInput;
    private readonly bool _privateOutput;
    private bool _disposed, _initializationFaulted;
    private readonly bool _tracePrivate = NcPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    internal double SetupMs { get; private set; }
    internal string Device { get; }
    internal const long WorkspaceBytes = (long)Capacity * sizeof(uint) * 2;
    internal long ResidentBytes => WorkspaceBytes + (_stagedBuffers is null ? 0 :
        (long)NeowFamilyGpuPlan.CurseCapacity * 12 + (long)NeowFamilyGpuPlan.PairCapacity * 48);
    internal static string ShaderSource(int stage = 3, bool capsuleComposite = false, bool leafyPreGate = false,
        bool bonesCapsuleComposite = false, bool bonesArcaneComposite = false, int directNestedMode = 0, int capsulePhysicalMode = 0, int bonesKMode = 0, bool authoredUpgrades = false,
        bool localResults = true, bool multiplayer = false) => FamilyGpuComputeUtility.LoadEmbeddedShader(multiplayer ? "NeowParty.comp.glsl" : "NeowSingleplayer.comp.glsl")
        .Replace("__RT2_LOCAL_RESULTS__", localResults ? "1" : "0", StringComparison.Ordinal)
        .Replace("__RT2_AUTHORED_UPGRADES__", authoredUpgrades ? "1" : "0", StringComparison.Ordinal)
        .Replace("__RT2_BONES_K_MODE__", bonesKMode.ToString(), StringComparison.Ordinal)
        .Replace("/*__RT2_BONES_K__*/", bonesKMode == 0 ? "" : FamilyGpuComputeUtility.LoadEmbeddedShader("BonesKSameRoute.glsl")
            .Replace("/*__RT2_RFULL_CHECKPOINT_MATCHER__*/", FamilyGpuComputeUtility.LoadEmbeddedShader("RelicFullCapsule.glsl"), StringComparison.Ordinal), StringComparison.Ordinal)
        .Replace("__RT2_CAPSULE_PHYSICAL__", capsulePhysicalMode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("__RT2_N_DIRECT_NESTED__", directNestedMode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("__RT2_NR_CAPSULE__", bonesCapsuleComposite || bonesArcaneComposite ? "2" : capsuleComposite ? "1" : "0", StringComparison.Ordinal)
        .Replace("__RT2_N_LEAFY_PRE_GATE__", leafyPreGate ? "1" : "0", StringComparison.Ordinal)
        .Replace("/*__RT2_CAPSULE_COMPOSITE__*/", bonesArcaneComposite ? BonesCapsuleShader(true) : bonesCapsuleComposite ? BonesCapsuleShader() : capsuleComposite ? CapsuleShader() : "", StringComparison.Ordinal)
        .Replace("__RT2_N_STAGE__", stage.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("/*__RT2_NEOW_LOCAL_DONOR__*/", FamilyGpuComputeUtility.LoadEmbeddedShader(
            multiplayer ? "NeowPartyDonor.glsl" : "NeowSingleplayerDonor.glsl"), StringComparison.Ordinal);
    private static string BonesCapsuleShader(bool arcane = false) => FamilyGpuComputeUtility.LoadEmbeddedShader(
        arcane ? "NeowBonesArcaneCapsuleComposite.glsl" : "NeowBonesCapsuleComposite.glsl")
        .Replace("/*__RT2_RFULL_CHECKPOINT_MATCHER__*/", FamilyGpuComputeUtility.LoadEmbeddedShader("RelicFullCapsule.glsl"), StringComparison.Ordinal);
    private static string CapsuleShader()
    {
        ulong hash = RolltheSpire2.Core.Seed.XxHash64.Hash("up_front"u8, 0);
        return FamilyGpuComputeUtility.LoadEmbeddedShader("NeowCapsuleComposite.glsl")
            .Replace("__RT2_UP_FRONT_LOW__", ((uint)hash).ToString("x8"), StringComparison.Ordinal)
            .Replace("__RT2_UP_FRONT_HIGH__", ((uint)(hash >> 32)).ToString("x8"), StringComparison.Ordinal);
    }
    internal NeowFamilyGpuExecutor(RenderingDevice rd, NeowFamilyGpuPlan plan, uint tag, NeowCapsuleComposite? composite = null, PrivateOrdinalBuffer? privateInput = null, PrivateOrdinalBuffer? privateOutput = null, int capsulePhysicalMode = 0, int bonesKMode = 0, uint[]? bonesKMetadata = null, NeowFamilyGpuPlan[]? partyPlans = null)
    {
        string? partySource = null;
        if (partyPlans is not null)
        {
            if (composite is not null || privateInput is not null || privateOutput is not null || bonesKMode != 0)
                throw new InvalidOperationException("PartyN.FusedTransportScope");
            (plan, partySource) = NeowPartyGpuPacking.Pack(partyPlans);
        }
        if (capsulePhysicalMode != 0 && (capsulePhysicalMode is < 1 or > 6 || composite is null || composite.UsesBonesCheckpoint || plan.UsesStagedDense))
            throw new InvalidOperationException("DirectCapsule.PhysicalScope");
        if (bonesKMode is < 0 or > 2 || (bonesKMode != 0 && (composite is not null || bonesKMode == 1 && privateOutput is null || bonesKMode == 2 && bonesKMetadata is null)))
            throw new InvalidOperationException("BonesK.SameRouteScope");
        _bonesKMode = bonesKMode;
        _rd = rd; _tag = tag; _bonesArcane = composite?.UsesBonesArcane == true; _privateInput = privateInput is not null;
        _privateOutput = privateOutput is not null;
        _plan = plan; _composite = composite; _partySource = partySource; _capsulePhysicalMode = capsulePhysicalMode;
        if (_privateInput && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("N.PrivateBuffersAlias");
        var timer = Stopwatch.StartNew();
        Rid Buffer(uint[] values, string name) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(values), name));
        try
        {
            _meta = Buffer(new uint[8], "N.Batch"); _header = Buffer(Header(), "N.Header");
            _input = privateInput?.Buffer ?? Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * sizeof(uint), "N.Input"));
            _output = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * sizeof(uint), "N.Output"));
            _buffers = [_meta, Buffer(plan.Meta, "N.Plan"), Buffer(plan.PoolMeta, "N.Pools"), Buffer(plan.Cards, "N.Cards"), Buffer(plan.Strike, "N.Strike"),
                Buffer(plan.Defend, "N.Defend"), _header, _output, Buffer(plan.Bones, "N.Bones"), Buffer(plan.Conditions, "N.Conditions"), _input];
            if (bonesKMode == 2 || composite is not null)
                _projectionMetadata = Buffer(bonesKMetadata ?? composite!.Metadata, "N.CapsuleProjection");
            Device = rd.GetDeviceName();
            // Private bindings have a fixed role. Build only that role while the
            // chain's shader-reuse scope is active. Public input is known at Execute.
            if (_privateInput || _privateOutput)
                EnsureModules(plan.UsesStagedDense && !_privateInput);
            SetupMs = timer.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(ex, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), "N.Constructor");
            throw;
        }
    }
    private Rid Add(Rid rid) { _owned.Add(rid); return rid; }

    private string ModuleSource(int stage)
    {
        string source = stage == 3 && _partySource is not null ? _partySource : ShaderSource(stage,
            capsuleComposite: stage == 3 && _composite is not null,
            leafyPreGate: _plan.UsesLeafyPreGate, bonesCapsuleComposite: _composite?.UsesBonesCheckpoint == true,
            bonesArcaneComposite: _bonesArcane, directNestedMode: _composite is null ? _plan.DirectNestedMode : 0,
            capsulePhysicalMode: stage == 3 ? _capsulePhysicalMode : 0, bonesKMode: _bonesKMode,
            authoredUpgrades: _plan.HasAuthoredUpgrades,
            localResults: _plan.HasLocalResults || _composite is not null || _bonesKMode != 0, multiplayer: _plan.IsMultiplayer);
        if (_privateInput)
        {
            const string ordinalRead = "uint ordinal=batch.values[4u]==0u ? first+lane : input_ordinals.values[first+lane];";
            if (!source.Contains(ordinalRead, StringComparison.Ordinal)) throw new InvalidOperationException("N.PrivateInputSeamChanged");
            source = source.Replace(ordinalRead, ordinalRead + "\nif(ordinal>=batch.values[2u]) { atomicExchange(header.values[3u],1u); continue; }", StringComparison.Ordinal);
        }
        return source;
    }

    private void EnsureModules(bool staged)
    {
        if (_initializationFaulted) throw new InvalidOperationException("NFamilyGpuInitializationFaulted");
        if (staged ? _stages.Count == 3 : _pipeline.IsValid) return;
        var timer = Stopwatch.StartNew();
        string name = staged ? "NeowFamilyPreBones0" : "NeowFamilyLocalDonor";
        bool checkpointMetadata = _composite?.UsesBonesCheckpoint == true || _bonesKMode == 2;
        try
        {
            if (staged)
            {
                // Allocate checkpoints only when this executor actually receives Dense.
                _stagedBuffers = [.._buffers,
                    Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(_rd, NeowFamilyGpuPlan.CurseCapacity * 12, "N.CurseSurvivors")),
                    Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(_rd, NeowFamilyGpuPlan.PairCapacity * 48, "N.PairSurvivors"))];
                if (checkpointMetadata) _stagedBuffers = [.._stagedBuffers, _projectionMetadata];
                for (int stage = 0; stage < 3; stage++)
                {
                    name = "NeowFamilyPreBones" + stage;
                    Rid shader = Add(FamilyGpuComputeUtility.CompileShader(_rd, ModuleSource(stage), name));
                    Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(_rd, shader, name));
                    if (!_rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("NFamilyStagePipelineInvalid:" + name);
                    _stages.Add((pipeline, Add(FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, _stagedBuffers, name))));
                }
            }
            else
            {
                Rid[] buffers = _buffers;
                if (checkpointMetadata)
                    buffers = _stagedBuffers ?? [..buffers,
                        Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(_rd, 4, "N.UnusedCurseBinding")),
                        Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(_rd, 4, "N.UnusedPairBinding")), _projectionMetadata];
                else if (_composite is not null) buffers = [..buffers, _projectionMetadata];
                Rid shader = Add(FamilyGpuComputeUtility.CompileShader(_rd, ModuleSource(3), name));
                Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(_rd, shader, name));
                if (!_rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("NFamilyGpuPipelineInvalid:" + name);
                Rid uniforms = Add(FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, buffers, name));
                _pipeline = pipeline; _uniforms = uniforms;
            }
        }
        catch (Exception ex)
        {
            _initializationFaulted = true;
            var failure = new InvalidOperationException("NFamilyGpuModuleInitializationFailed:" + name + ":" + ex.Message, ex);
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(_rd, _owned), "N.Module:" + name);
            throw failure;
        }
        finally { SetupMs += timer.Elapsed.TotalMilliseconds; }
    }
    private uint[] Header() => _bonesKMode != 0 ? [0x4e464d52, 1, 0, 0, 0, _tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] : _bonesArcane ? [0x4e464d52, 1, 0, 0, 0, _tag, 0, 0, 0, 0, 0, 0]
        : [0x4e464d52, 1, 0, 0, 0, _tag, 0, 0];
    private void AssertOwner()
    {
        if (_owner != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("NFamilyGpuOwnerMismatch");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    internal FamilyCandidateSet Execute(FamilyCandidateSet input, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        if (_privateInput || _privateOutput) throw new InvalidOperationException("N.PrivateTransportRequiresPrivateEntry");
        return ExecuteCore(input, input.Count, token, out metrics)!;
    }

    internal FamilyCandidateSet ExecutePrivate(SearchBatch batch, int count, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        if (!_privateInput || _privateOutput || count < 0 || count > batch.BatchCandidateCount) throw new InvalidDataException("N.PrivateCountInvalid");
        return ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics)!;
    }
    internal int ExecutePrivateOutput(FamilyCandidateSet input, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        if (!_privateOutput || _privateInput || !input.IsDense) throw new InvalidOperationException("N.PrivateOutputDenseOnly");
        ExecuteCore(input,input.Count,token,out metrics);
        return metrics.Survivors;
    }
    internal int ExecutePrivateStage(SearchBatch batch, int count, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        if (!_privateOutput || (!_privateInput && count != batch.BatchCandidateCount) || count < 0 || count > batch.BatchCandidateCount || batch.BatchCandidateCount > Capacity)
            throw new InvalidDataException("N.PrivateStageBounds");
        ExecuteCore(FamilyCandidateSet.Dense(batch), count, token, out metrics);
        return metrics.Survivors;
    }
    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input, int inputCount, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        AssertOwner(); token.ThrowIfCancellationRequested();
        if (inputCount > Capacity) throw new InvalidOperationException("NFamilyGpuCapacityExceeded");
        bool dense = input.IsDense && !_privateInput;
        bool staged = dense && _plan.UsesStagedDense;
        if (_initializationFaulted) throw new InvalidOperationException("NFamilyGpuInitializationFaulted");
        // Setup is reported separately from batch upload/dispatch costs, including
        // a later transition between public Compact and Dense input.
        if (inputCount > 0) EnsureModules(staged);
        var timer = Stopwatch.StartNew();
        FamilyGpuComputeUtility.Update(_rd, _meta, [(uint)input.Batch.BatchBase, (uint)(input.Batch.BatchBase >> 32),
            (uint)input.Batch.BatchCandidateCount, (uint)inputCount, dense ? 0u : 1u, Capacity,
            NeowFamilyGpuPlan.CurseCapacity, NeowFamilyGpuPlan.PairCapacity], "NFamilyBatch");
        FamilyGpuComputeUtility.Update(_rd, _header, Header(), "NFamilyHeader");
        if (!input.IsDense && !_privateInput) FamilyGpuComputeUtility.Update(_rd, _input,
            input.ExportAbi1().ToArray().Select(x => checked((uint)x)).ToArray(), "NFamilyCompact");
        double uploadedMs = timer.Elapsed.TotalMilliseconds;
        uint groups = (uint)((inputCount + 511) / 512);
        if (groups > 0)
        {
            if (!staged && !_pipeline.IsValid) throw new InvalidOperationException("N.UnboundCompactModule");
            long list = _rd.ComputeListBegin();
            if (staged)
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    if (stage > 0) _rd.ComputeListAddBarrier(list);
                    _rd.ComputeListBindComputePipeline(list, _stages[stage].Pipeline);
                    _rd.ComputeListBindUniformSet(list, _stages[stage].Uniforms, 0);
                    _rd.ComputeListDispatch(list, stage == 0 ? groups : (uint)((stage == 1 ? NeowFamilyGpuPlan.CurseCapacity : NeowFamilyGpuPlan.PairCapacity) / 512), 1, 1);
                }
            }
            else
            {
                _rd.ComputeListBindComputePipeline(list, _pipeline); _rd.ComputeListBindUniformSet(list, _uniforms, 0);
                _rd.ComputeListDispatch(list, groups, 1, 1);
            }
            _rd.ComputeListEnd();
            FamilyGpuComputeUtility.SubmitAndSync(_rd, "N.Neow");
        }
        token.ThrowIfCancellationRequested();
        double syncedMs = timer.Elapsed.TotalMilliseconds;
        uint[] header = FamilyGpuComputeUtility.FromUInt32Bytes(_rd.BufferGetData(_header));
        if (header.Length != (_bonesKMode != 0 ? 16 : _bonesArcane ? 12 : 8) || header[0] != 0x4e464d52 || header[1] != 1 || header[5] != _tag ||
            header[3] != 0 || header[4] != inputCount || header[2] > header[4] ||
            (staged && (header[6] > NeowFamilyGpuPlan.CurseCapacity || header[7] > NeowFamilyGpuPlan.PairCapacity || header[7] > header[6])))
            throw new InvalidDataException("NFamilyGpuHeaderInvalid");
        if (_bonesKMode != 0) {
            BonesKCounts = header[8..16];
            ulong nPass=(ulong)header[8]+header[9]+header[10];
            if(nPass>(ulong)inputCount || (staged && nPass>header[7]) ||
                (_bonesKMode==1 && nPass!=header[2]) ||
                (_bonesKMode==2 && (header[11]>(ulong)header[8]+header[10] || header[12]>(ulong)header[9]+header[10] ||
                    header[13]>header[11] || header[14]>header[12] || header[15]!=header[2] || header[2]>(ulong)header[13]+header[14])))
                throw new InvalidDataException("BonesK.SameRouteCountersInvalid");
        }
        uint count = header[2];
        if (_privateOutput)
        {
            double readyMs=timer.Elapsed.TotalMilliseconds;
            metrics=new(inputCount,checked((int)count),groups,header.Length*4,readyMs,uploadedMs,syncedMs-uploadedMs,
                readyMs-syncedMs,0,groups==0?0:staged?3:1,header[6],header[7],0,0,0,0,0);
            return null;
        }
        if (_bonesArcane && (header[8] > input.Count || header[9] > header[8] || header[10] > header[9] ||
            header[11] < header[10] || header[11] > 2UL * header[10] || count > header[10]))
            throw new InvalidDataException("NrBonesArcaneCountersInvalid");
        double payloadStartMs = _bonesArcane ? timer.Elapsed.TotalMilliseconds : 0;
        uint[] packed = count == 0 ? [] : FamilyGpuComputeUtility.FromUInt32Bytes(_rd.BufferGetData(_output, 0, count * sizeof(uint)));
        double readMs = timer.Elapsed.TotalMilliseconds;
        ulong[] ordinals = packed.Select(x => (ulong)x).ToArray();
        double widenEnd = timer.Elapsed.TotalMilliseconds;
        if (ordinals.Length > 1) Array.Sort(ordinals);
        double sortEnd = timer.Elapsed.TotalMilliseconds;
        var result = FamilyCandidateSet.FromSortedAbi1(input.Batch, ordinals);
        double canonicalMs = timer.Elapsed.TotalMilliseconds;
        metrics = new((int)header[4], ordinals.Length, groups, header.Length * sizeof(uint) + count * sizeof(uint), canonicalMs,
            uploadedMs, syncedMs - uploadedMs, readMs - syncedMs, canonicalMs - readMs,
            groups == 0 ? 0 : staged ? 3 : 1, header[6], header[7], _bonesArcane ? header[8] : 0, _bonesArcane ? header[9] : 0,
            _bonesArcane ? header[10] : 0, _bonesArcane ? header[11] : 0, _bonesArcane && count > 0 ? readMs - payloadStartMs : 0);
        if (_tracePrivate)
            Bootstrap.RuntimeLog.TryBackgroundDetail($"ncStageTiming=true;stage=N;input={inputCount};output={result.Count};dispatches={metrics.Dispatches};uploadMs={uploadedMs};dispatchSyncMs={metrics.DispatchSyncMs};readbackMs={metrics.ReadbackMs};readbackBytes={metrics.ReadbackBytes};widenMs={widenEnd-readMs};sortMs={sortEnd-widenEnd};validationMs={canonicalMs-sortEnd};canonicalMs={canonicalMs}");
        return result;
    }
    public void Dispose()
    {
        AssertOwner(); _disposed = true;
        FamilyGpuComputeUtility.FreeAll(_rd, _owned);
    }
}
