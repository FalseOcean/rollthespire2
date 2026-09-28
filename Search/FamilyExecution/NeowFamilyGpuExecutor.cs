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
    private readonly Rid _meta, _header, _input, _output, _pipeline, _uniforms;
    private readonly List<(Rid Pipeline, Rid Uniforms)> _stages = new();
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private readonly uint _tag;
    private readonly bool _bonesArcane;
    private readonly int _bonesKMode;
    internal uint[] BonesKCounts { get; private set; } = [];
    private readonly bool _privateInput;
    private readonly bool _privateOutput;
    private bool _disposed;
    private readonly bool _tracePrivate = NcPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    internal double SetupMs { get; }
    internal string Device { get; }
    internal const long WorkspaceBytes = (long)Capacity * sizeof(uint) * 2;
    internal long ResidentBytes => WorkspaceBytes + (_stages.Count == 0 ? 0 :
        (long)NeowFamilyGpuPlan.CurseCapacity * 12 + (long)NeowFamilyGpuPlan.PairCapacity * 48);
    internal static string ShaderSource(int stage = 3, bool capsuleComposite = false, bool leafyPreGate = false,
        bool bonesCapsuleComposite = false, bool bonesArcaneComposite = false, int directNestedMode = 0, int capsulePhysicalMode = 0, int bonesKMode = 0, bool authoredUpgrades = false) => FamilyGpuComputeUtility.LoadEmbeddedShader("NeowFamily.comp.glsl")
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
        .Replace("/*__RT2_NEOW_LOCAL_DONOR__*/", FamilyGpuComputeUtility.LoadEmbeddedShader("NeowFamilyDonor.glsl"), StringComparison.Ordinal);
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
    internal NeowFamilyGpuExecutor(RenderingDevice rd, NeowFamilyGpuPlan plan, uint tag, NeowCapsuleComposite? composite = null, PrivateOrdinalBuffer? privateInput = null, PrivateOrdinalBuffer? privateOutput = null, int capsulePhysicalMode = 0, int bonesKMode = 0, uint[]? bonesKMetadata = null)
    {
        if (capsulePhysicalMode != 0 && (capsulePhysicalMode is < 1 or > 6 || composite is null || composite.UsesBonesCheckpoint || plan.UsesStagedDense))
            throw new InvalidOperationException("DirectCapsule.PhysicalScope");
        if (bonesKMode is < 0 or > 2 || (bonesKMode != 0 && (composite is not null || bonesKMode == 1 && privateOutput is null || bonesKMode == 2 && bonesKMetadata is null)))
            throw new InvalidOperationException("BonesK.SameRouteScope");
        _bonesKMode = bonesKMode;
        _rd = rd; _tag = tag; _bonesArcane = composite?.UsesBonesArcane == true; _privateInput = privateInput is not null;
        _privateOutput = privateOutput is not null;
        if (_privateInput && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("N.PrivateBuffersAlias");
        var timer = Stopwatch.StartNew();
        Rid Add(Rid rid) { _owned.Add(rid); return rid; }
        Rid Buffer(uint[] values) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(values)));
        try
        {
            _meta = Buffer(new uint[8]); _header = Buffer(Header());
            _input = privateInput?.Buffer ?? Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * sizeof(uint)));
            _output = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * sizeof(uint)));
            Rid[] buffers = [_meta, Buffer(plan.Meta), Buffer(plan.PoolMeta), Buffer(plan.Cards), Buffer(plan.Strike),
                Buffer(plan.Defend), _header, _output, Buffer(plan.Bones), Buffer(plan.Conditions), _input];
            bool bonesComposite = composite?.UsesBonesCheckpoint == true;
            bool checkpointMetadata = bonesComposite || bonesKMode == 2;
            if (checkpointMetadata)
            {
                // 11/12 retain N's checkpoint meanings; R metadata is binding 13.
                // Tiny-pool fused paths need only placeholders for the two gaps.
                buffers = [..buffers,
                    Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, plan.UsesStagedDense ? NeowFamilyGpuPlan.CurseCapacity * 12 : 4)),
                    Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, plan.UsesStagedDense ? NeowFamilyGpuPlan.PairCapacity * 48 : 4)),
                    Buffer(bonesKMetadata ?? composite!.Metadata)];
            }
            else if (composite is not null) buffers = [..buffers, Buffer(composite.Metadata)];
            string source = ShaderSource(capsuleComposite: composite is not null,
                leafyPreGate: plan.UsesLeafyPreGate, bonesCapsuleComposite: bonesComposite, bonesArcaneComposite: _bonesArcane,
                directNestedMode: composite is null ? plan.DirectNestedMode : 0, capsulePhysicalMode: capsulePhysicalMode, bonesKMode: bonesKMode, authoredUpgrades: plan.HasAuthoredUpgrades);
            if (_privateInput)
            {
                const string ordinalRead = "uint ordinal=batch.values[4u]==0u ? first+lane : input_ordinals.values[first+lane];";
                if (!source.Contains(ordinalRead, StringComparison.Ordinal)) throw new InvalidOperationException("N.PrivateInputSeamChanged");
                source = source.Replace(ordinalRead, ordinalRead + "\nif(ordinal>=batch.values[2u]) { atomicExchange(header.values[3u],1u); continue; }", StringComparison.Ordinal);
            }
            // A private producer accepts Dense only; a private consumer is
            // Compact only. Do not build modules these bound roles cannot use.
            // Public transport retains both modes and its original modules.
            bool stagedDenseOnly = plan.UsesStagedDense && !_privateInput && _privateOutput;
            if (!stagedDenseOnly)
            {
                Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "NeowFamilyLocalDonor"));
                _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader));
                if (!rd.ComputePipelineIsValid(_pipeline)) throw new InvalidOperationException("NFamilyGpuPipelineInvalid");
                _uniforms = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, buffers));
            }
            else Bootstrap.RuntimeLog.TryBackgroundInfo("searchStartup=true;phase=ModuleNotRequired;name=NeowFamilyLocalDonor;binding=PrivateStagedDense;reason=NoCompactEntry;sourceSha256=" +
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))));
            if (plan.UsesStagedDense && !_privateInput)
            {
                Rid[] stagedBuffers = checkpointMetadata ? buffers : [..buffers,
                    Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, NeowFamilyGpuPlan.CurseCapacity * 12)),
                    Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, NeowFamilyGpuPlan.PairCapacity * 48))];
                for (int stage = 0; stage < 3; stage++)
                {
                    Rid stageShader = Add(FamilyGpuComputeUtility.CompileShader(rd, ShaderSource(stage,
                        leafyPreGate: plan.UsesLeafyPreGate, bonesCapsuleComposite: bonesComposite, bonesArcaneComposite: _bonesArcane,
                        directNestedMode: composite is null ? plan.DirectNestedMode : 0, bonesKMode: bonesKMode, authoredUpgrades: plan.HasAuthoredUpgrades), "NeowFamilyPreBones" + stage));
                    Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, stageShader));
                    if (!rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("NFamilyStagePipelineInvalid");
                    _stages.Add((pipeline, Add(FamilyGpuComputeUtility.CreateUniformSet(rd, stageShader, stagedBuffers))));
                }
            }
            Device = rd.GetDeviceName(); SetupMs = timer.Elapsed.TotalMilliseconds;
        }
        catch { FamilyGpuComputeUtility.FreeAll(rd, _owned, _privateInput || _privateOutput); throw; }
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
        var timer = Stopwatch.StartNew();
        FamilyGpuComputeUtility.Update(_rd, _meta, [(uint)input.Batch.BatchBase, (uint)(input.Batch.BatchBase >> 32),
            (uint)input.Batch.BatchCandidateCount, (uint)inputCount, dense ? 0u : 1u, Capacity,
            NeowFamilyGpuPlan.CurseCapacity, NeowFamilyGpuPlan.PairCapacity], "NFamilyBatch");
        FamilyGpuComputeUtility.Update(_rd, _header, Header(), "NFamilyHeader");
        if (!input.IsDense && !_privateInput) FamilyGpuComputeUtility.Update(_rd, _input,
            input.ExportAbi1().ToArray().Select(x => checked((uint)x)).ToArray(), "NFamilyCompact");
        double uploadedMs = timer.Elapsed.TotalMilliseconds;
        uint groups = (uint)((inputCount + 511) / 512);
        bool staged = dense && _stages.Count == 3;
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
            _rd.ComputeListEnd(); FamilyGpuExecutionOwner.ObserveFirstSubmit(); _rd.Submit(); _rd.Sync();
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
                readyMs-syncedMs,0,staged?3:1,header[6],header[7],0,0,0,0,0);
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
            staged ? 3 : 1, header[6], header[7], _bonesArcane ? header[8] : 0, _bonesArcane ? header[9] : 0,
            _bonesArcane ? header[10] : 0, _bonesArcane ? header[11] : 0, _bonesArcane && count > 0 ? readMs - payloadStartMs : 0);
        if (_tracePrivate)
            Bootstrap.RuntimeLog.TryBackgroundDetail($"ncStageTiming=true;stage=N;input={inputCount};output={result.Count};dispatches={metrics.Dispatches};uploadMs={uploadedMs};dispatchSyncMs={metrics.DispatchSyncMs};readbackMs={metrics.ReadbackMs};readbackBytes={metrics.ReadbackBytes};widenMs={widenEnd-readMs};sortMs={sortEnd-widenEnd};validationMs={canonicalMs-sortEnd};canonicalMs={canonicalMs}");
        return result;
    }
    public void Dispose()
    {
        AssertOwner(); _disposed = true;
        FamilyGpuComputeUtility.FreeAll(_rd, _owned, _privateInput || _privateOutput);
    }
}
