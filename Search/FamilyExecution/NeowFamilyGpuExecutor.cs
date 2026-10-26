using System.Diagnostics;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct NeowFamilyGpuMetrics(int Processed, int Survivors, uint Groups, long ReadbackBytes,
    double CanonicalMs, double UploadMs, double DispatchSyncMs, double ReadbackMs, double CanonicalizeMs,
    int Dispatches, uint CurseSurvivors, uint PairSurvivors,
    uint NrPairPass, uint ArcanePass, uint CapsuleRarityPass, uint HeavyEntered, double PayloadReadbackMs)
{
    // 主機 API 耗時，不是 GPU 核心時間戳。
    internal double SubmitMs { get; init; }
    internal double SyncMs { get; init; }
    internal double HeaderReadCallMs { get; init; }
    internal double PayloadReadCallMs { get; init; }
    internal int HeaderReadCalls { get; init; }
    internal int PayloadReadCalls { get; init; }
    internal int SmallPayloadReadCalls { get; init; }
    internal int DirectPayloadReadCalls { get; init; }
    internal long PayloadReadSourceCapacityBytes { get; init; }
    internal double CombinedReadCallMs { get; init; }
    internal int CombinedReadCalls { get; init; }
    internal int CombinedPayloadBatches { get; init; }
    internal long LogicalPayloadBytes { get; init; }
    internal int MediumPayloadReadCalls { get; init; }
    internal double MediumReadCallMs { get; init; }
    internal long MediumReadSourceCapacityBytes { get; init; }
    // 記錄合併緩衝容量與群組歸屬；共用耗時按窗口分攤。
    internal long CombinedReadSourceCapacityBytes { get; init; }
    internal bool BatchMergeWindow { get; init; }
    internal int BatchMergeGroupSize { get; init; }
}

// Session-resident resources on the existing Family GPU owner. Host transport,
// header validation and canonicalization follow the accepted R executor donor.
internal sealed partial class NeowFamilyGpuExecutor : IDisposable
{
    internal const int Capacity = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
    // Full-capacity Compact transport prevents high-survival upstream N/S from
    // becoming an accidental Exact-only gate. Both workspaces remain bounded.
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = new();
    private readonly Rid _meta, _header, _input, _output, _projectionMetadata, _readbackScratch, _readbackBank, _readbackMediumBank;
    private Rid _pipeline, _uniforms, _singleShader;
    private readonly Rid[] _buffers;
    private Rid[]? _stagedBuffers;
    private readonly NeowFamilyGpuPlan _plan;
    private readonly NeowCapsuleComposite? _composite;
    private readonly string? _partySource;
    private readonly int _capsulePhysicalMode;
    private readonly List<(Rid Shader, Rid Pipeline, Rid Uniforms)> _stages = new();
    private readonly int _owner = System.Environment.CurrentManagedThreadId;
    private readonly uint _tag;
    private readonly bool _bonesArcane;
    private readonly int _bonesKMode;
    internal uint[] BonesKCounts { get; private set; } = [];
    private readonly bool _privateInput;
    private readonly bool _privateOutput;
    private readonly bool _useSmallReadback;
    private readonly bool _useCombinedReadback;
    private readonly bool _useMediumReadback;
    private bool _disposed, _initializationFaulted;
    private readonly bool _tracePrivate = NcPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    internal double SetupMs { get; private set; }
    internal string Device { get; }
    internal const long WorkspaceBytes = (long)Capacity * sizeof(uint) * 2;
    internal int PayloadReadbackScratchBytes => (_readbackBank.IsValid ? NeowCombinedReadbackTransport.BankBytes : 0)
        + (_readbackMediumBank.IsValid ? NeowMediumReadbackTransport.BankBytes : 0)
        + (_readbackScratch.IsValid ? NeowReadbackTransport.ScratchBytes : 0);
    internal long ResidentBytes => WorkspaceBytes + PayloadReadbackScratchBytes + (_stagedBuffers is null ? 0 :
        (long)NeowFamilyGpuPlan.CurseCapacity * 12 + (long)NeowFamilyGpuPlan.PairCapacity * 48);
    internal static string ShaderSource(int stage = 3, bool capsuleComposite = false, bool leafyPreGate = false,
        bool bonesCapsuleComposite = false, bool bonesArcaneComposite = false, int directNestedMode = 0, int capsulePhysicalMode = 0, int bonesKMode = 0, bool authoredUpgrades = false,
        bool localResults = true, bool multiplayer = false, int jointTransformMetaOffset = -1) => FamilyGpuComputeUtility.LoadEmbeddedShader(multiplayer ? "NeowParty.comp.glsl" : "NeowSingleplayer.comp.glsl")
        .Replace("__RT2_N_TRANSFORM_META_OFFSET__", jointTransformMetaOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("/*__RT2_N_JOINT_TRANSFORMS__*/", jointTransformMetaOffset < 0 ? "" : FamilyGpuComputeUtility.LoadEmbeddedShader("NeowJointTransforms.glsl"), StringComparison.Ordinal)
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
        _useSmallReadback = NeowReadbackTransport.Enabled && !_privateInput && !_privateOutput && !plan.IsMultiplayer && partySource is null;
        _useCombinedReadback = _useSmallReadback && NeowCombinedReadbackTransport.Enabled && !_bonesArcane && _bonesKMode == 0;
        // 超出合併前綴的公開輸出使用第二級緩衝；只隨合併讀回啟用。
        _useMediumReadback = _useCombinedReadback && NeowMediumReadbackTransport.Enabled;
        if (_privateInput && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("N.PrivateBuffersAlias");
        var timer = Stopwatch.StartNew();
        Rid Buffer(uint[] values, string name) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(values), name));
        try
        {
            _meta = Buffer(new uint[8], "N.Batch"); _header = Buffer(Header(), "N.Header");
            _input = privateInput?.Buffer ?? Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * sizeof(uint), "N.Input"));
            _output = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, Capacity * sizeof(uint), "N.Output"));
            if (_useCombinedReadback)
            {
                _readbackBank = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, NeowCombinedReadbackTransport.BankBytes, "N.CombinedReadbackBank"));
                if (_useMediumReadback)
                    _readbackMediumBank = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, NeowMediumReadbackTransport.BankBytes, "N.MediumReadbackBank"));
            }
            else if (_useSmallReadback)
                _readbackScratch = Add(FamilyGpuComputeUtility.CreateZeroedStorageBuffer(rd, NeowReadbackTransport.ScratchBytes, "N.PayloadReadbackScratch"));
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
            localResults: _plan.HasLocalResults || _composite is not null || _bonesKMode != 0, multiplayer: _plan.IsMultiplayer,
            jointTransformMetaOffset: _plan.JointTransformMetaOffset);
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
                    _stages.Add((shader, pipeline, Add(FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, _stagedBuffers, name))));
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
                _pipeline = pipeline; _uniforms = uniforms; _singleShader = shader;
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
        if (!input.IsDense && !_privateInput) FamilyGpuComputeUtility.UpdateBytes(_rd, _input,
            FamilyGpuOrdinalPacking.ToBytes(input.ExportAbi1().Span), "NFamilyCompact");
        double uploadedMs = timer.Elapsed.TotalMilliseconds;
        uint groups = (uint)((inputCount + 511) / 512);
        bool combinedRead = _useCombinedReadback && inputCount > 0;
        double submitMs = 0, syncMs = 0;
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
            // 清單結束後再排入複製，由 RD 追蹤依賴；複製至 Sync 之間不可取消。
            if (combinedRead)
                NeowCombinedReadbackTransport.QueueCopies(_rd, _header, _output, _readbackBank, checked((uint)inputCount));
            // 合併模式的複製包含在本次提交。
            (submitMs, syncMs) = FamilyGpuComputeUtility.SubmitAndSync(_rd, "N.Neow");
        }
        token.ThrowIfCancellationRequested();
        double syncedMs = timer.Elapsed.TotalMilliseconds;
        // 讀回 API 與轉換、驗證分開計時。
        byte[]? combinedBytes = null;
        byte[] headerBytes;
        double headerReadCallMs = 0, combinedReadCallMs = 0;
        if (combinedRead)
        {
            long combinedReadStarted = Stopwatch.GetTimestamp();
            combinedBytes = NeowCombinedReadbackTransport.ReadBank(_rd, _readbackBank, checked((uint)inputCount));
            combinedReadCallMs = Stopwatch.GetElapsedTime(combinedReadStarted).TotalMilliseconds;
            headerBytes = combinedBytes.AsSpan(0, NeowCombinedReadbackTransport.HeaderBytes).ToArray();
        }
        else
        {
            long headerReadStarted = Stopwatch.GetTimestamp();
            headerBytes = _rd.BufferGetData(_header);
            headerReadCallMs = Stopwatch.GetElapsedTime(headerReadStarted).TotalMilliseconds;
        }
        uint[] header = FamilyGpuComputeUtility.FromUInt32Bytes(headerBytes);
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
                readyMs-syncedMs,0,groups==0?0:staged?3:1,header[6],header[7],0,0,0,0,0)
            {
                SubmitMs=submitMs, SyncMs=syncMs, HeaderReadCallMs=headerReadCallMs, HeaderReadCalls=1
            };
            return null;
        }
        if (_bonesArcane && (header[8] > input.Count || header[9] > header[8] || header[10] > header[9] ||
            header[11] < header[10] || header[11] > 2UL * header[10] || count > header[10]))
            throw new InvalidDataException("NrBonesArcaneCountersInvalid");
        double payloadStartMs = _bonesArcane ? timer.Elapsed.TotalMilliseconds : 0;
        uint[] packed = [];
        double payloadReadCallMs = 0, mediumReadCallMs = 0;
        bool usedSmallReadback = false;
        bool usedMediumReadback = false;
        bool payloadFromCombined = false;
        if (count > 0)
        {
            byte[] payloadBytes;
            if (combinedBytes is not null && NeowCombinedReadbackTransport.TryExtractPayload(
                    combinedBytes, checked((uint)inputCount), count, out byte[] prefixPayload))
            {
                // 只讀取已驗證的有效筆數；未使用前綴可能殘留舊資料。
                payloadBytes = prefixPayload;
                payloadFromCombined = true;
            }
            else
            {
                long payloadReadStarted = Stopwatch.GetTimestamp();
                if (_useMediumReadback && NeowMediumReadbackTransport.CanStage(count))
                {
                    // 先將有效輸出搬入有界緩衝，避免同步讀回按 64 MiB 來源建立臨時資源。
                    long mediumReadStarted = Stopwatch.GetTimestamp();
                    payloadBytes = NeowMediumReadbackTransport.Read(_rd, _output, _readbackMediumBank, count);
                    usedMediumReadback = true;
                    mediumReadCallMs = Stopwatch.GetElapsedTime(mediumReadStarted).TotalMilliseconds;
                }
                else
                {
                    payloadBytes = _useSmallReadback && !combinedRead
                        ? NeowReadbackTransport.Read(_rd, _output, _readbackScratch, count, out usedSmallReadback)
                        : NeowReadbackTransport.ReadDirect(_rd, _output, count);
                }
                payloadReadCallMs = Stopwatch.GetElapsedTime(payloadReadStarted).TotalMilliseconds;
            }
            packed = FamilyGpuComputeUtility.FromUInt32Bytes(payloadBytes);
        }
        double readMs = timer.Elapsed.TotalMilliseconds;
        // Solo high-output public boundaries spend far more time comparison-sorting
        // widened ordinals than dispatching. Sort the complete packed permutation
        // first; the usual ABI1 validator still owns bounds and duplicate checks.
        // Party transport and small outputs retain their existing materialization.
        bool packedRadix = !_plan.IsMultiplayer && _partySource is null && packed.Length >= 65536;
        double sortMs = 0;
        if (packedRadix)
        {
            SortLargePackedOrdinals(packed);
            sortMs = timer.Elapsed.TotalMilliseconds - readMs;
        }
        double widenStart = timer.Elapsed.TotalMilliseconds;
        ulong[] ordinals;
        if (packedRadix)
        {
            ordinals = new ulong[packed.Length];
            for (int i = 0; i < packed.Length; i++) ordinals[i] = packed[i];
        }
        else ordinals = packed.Select(x => (ulong)x).ToArray();
        double widenMs = timer.Elapsed.TotalMilliseconds - widenStart;
        double sortStart = timer.Elapsed.TotalMilliseconds;
        if (!packedRadix && ordinals.Length > 1) Array.Sort(ordinals);
        sortMs += timer.Elapsed.TotalMilliseconds - sortStart;
        double validationStart = timer.Elapsed.TotalMilliseconds;
        var result = FamilyCandidateSet.FromSortedAbi1(input.Batch, ordinals);
        double canonicalMs = timer.Elapsed.TotalMilliseconds;
        int payloadReadCalls = count > 0 && !payloadFromCombined ? 1 : 0;
        long readbackBytes = (combinedBytes?.Length ?? headerBytes.Length) + (payloadReadCalls > 0 ? (long)count * sizeof(uint) : 0);
        metrics = new((int)header[4], ordinals.Length, groups, readbackBytes, canonicalMs,
            uploadedMs, syncedMs - uploadedMs, readMs - syncedMs, canonicalMs - readMs,
            groups == 0 ? 0 : staged ? 3 : 1, header[6], header[7], _bonesArcane ? header[8] : 0, _bonesArcane ? header[9] : 0,
            _bonesArcane ? header[10] : 0, _bonesArcane ? header[11] : 0, _bonesArcane && count > 0 ? readMs - payloadStartMs : 0)
        {
            SubmitMs=submitMs, SyncMs=syncMs, HeaderReadCallMs=headerReadCallMs,
            PayloadReadCallMs=payloadReadCallMs, HeaderReadCalls=combinedRead?0:1, PayloadReadCalls=payloadReadCalls,
            SmallPayloadReadCalls=usedSmallReadback?1:0, DirectPayloadReadCalls=payloadReadCalls>0&&!usedSmallReadback&&!usedMediumReadback?1:0,
            PayloadReadSourceCapacityBytes=payloadReadCalls==0?0:usedSmallReadback?NeowReadbackTransport.ScratchBytes:usedMediumReadback?NeowMediumReadbackTransport.BankBytes:(long)Capacity*sizeof(uint),
            CombinedReadCallMs=combinedReadCallMs, CombinedReadCalls=combinedRead?1:0,
            CombinedPayloadBatches=payloadFromCombined?1:0, LogicalPayloadBytes=(long)count*sizeof(uint),
            MediumPayloadReadCalls=usedMediumReadback?1:0, MediumReadCallMs=mediumReadCallMs,
            MediumReadSourceCapacityBytes=usedMediumReadback?NeowMediumReadbackTransport.BankBytes:0,
            CombinedReadSourceCapacityBytes=combinedRead?NeowCombinedReadbackTransport.BankBytes:0
        };
        if (_tracePrivate)
            Bootstrap.RuntimeLog.TryBackgroundDetail($"ncStageTiming=true;stage=N;input={inputCount};output={result.Count};dispatches={metrics.Dispatches};uploadMs={uploadedMs};dispatchSyncMs={metrics.DispatchSyncMs};readbackMs={metrics.ReadbackMs};readbackBytes={metrics.ReadbackBytes};widenMs={widenMs};sortMs={sortMs};validationMs={canonicalMs-validationStart};canonicalMs={canonicalMs};packedRadix={packedRadix}");
        return result;
    }

    // Four stable byte passes preserve every uint, including malformed values;
    // no masking to the current root-window width, filtering, or deduplication.
    internal static void SortLargePackedOrdinals(uint[] packed)
    {
        var scratch = new uint[packed.Length];
        uint[] source = packed, destination = scratch;
        Span<int> offsets = stackalloc int[256];
        for (int shift = 0; shift < 32; shift += 8)
        {
            offsets.Clear();
            foreach (uint value in source) offsets[(int)((value >> shift) & 255)]++;
            int next = 0;
            for (int bucket = 0; bucket < offsets.Length; bucket++)
            {
                int length = offsets[bucket]; offsets[bucket] = next; next += length;
            }
            foreach (uint value in source) destination[offsets[(int)((value >> shift) & 255)]++] = value;
            (source, destination) = (destination, source);
        }
    }
    public void Dispose()
    {
        AssertOwner(); _disposed = true;
        FamilyGpuComputeUtility.FreeAll(_rd, _owned);
    }
}
