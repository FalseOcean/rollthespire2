using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Godot;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct NeowFamilyGpuMetrics(int Processed, int Survivors, uint Groups, long ReadbackBytes,
    double CanonicalMs, double UploadMs, double DispatchSyncMs, double ReadbackMs, double CanonicalizeMs,
    int Dispatches, uint CurseSurvivors, uint PairSurvivors,
    uint NrPairPass, uint ArcanePass, uint CapsuleRarityPass, uint HeavyEntered, double PayloadReadbackMs)
{
    internal FamilyCandidateSet? SmallOutput { get; init; }
}

// Only constructed after shared GPU completion and successful left collection.
// The right slot remains failed; this carries the bounded left outcome so its
// normal terminal handoff can finish without advancing across the failed root.
internal sealed class NeowDensePairRightFailure : Exception
{
    internal FamilyCandidateSet? Left { get; }
    internal NeowFamilyGpuMetrics LeftMetrics { get; }
    internal double SharedMs { get; }
    internal SearchBatch RightBatch { get; }

    internal NeowDensePairRightFailure(FamilyCandidateSet? left, NeowFamilyGpuMetrics leftMetrics,
        double sharedMs, SearchBatch rightBatch, Exception failure)
        : base($"N.DensePairRightCollectFailure:batch={rightBatch.BatchBase};count={rightBatch.BatchCandidateCount}", failure)
    {
        Left=left; LeftMetrics=leftMetrics; SharedMs=sharedMs; RightBatch=rightBatch;
    }
}

// Session-resident resources on the existing Family GPU owner. Host transport,
// header validation and canonicalization follow the accepted R executor donor.
internal sealed partial class NeowFamilyGpuExecutor : IDisposable
{
    internal const int Capacity = FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize;
    internal const string SmallOutputRevision = ".PrivateSmallHeader8.20261026.v1";
    internal const string DensePairRevision = ".DensePairK2.SharedScratchAndModules.20261027.v2";
    internal const string PublicBoundaryRevision = ".QueuedPrefix1024Or65536.20261027.v1";
    internal static bool SupportsLeafyLazy(NeowFamilyGpuPlan? plan) =>
        plan is { UsesLeafyPreGate: true, IsMultiplayer: false };
    internal bool UsesLeafyLazy { get; }
    private readonly NeowGpuOptimizations _optimizations;
    private const uint SmallOutputMagic = 0x384e5452;
    private const int SmallOutputOffset = 12;
    private const int SmallOutputCapacity = 8;
    // Ordinary solo Leafy only; composite/BonesK counters keep their own layout.
    internal static bool SupportsSmallOutput(NeowFamilyGpuPlan? plan) =>
        plan is { UsesLeafyPreGate: true, IsMultiplayer: false, NumericalVariant: NeowGpuNumericalVariant.NativeFloat64 };
    // Full-capacity Compact transport prevents high-survival upstream N/S from
    // becoming an accidental Exact-only gate. Both workspaces remain bounded.
    private readonly RenderingDevice _rd;
    private readonly List<Rid> _owned = new();
    private readonly Rid _meta, _header, _input, _output, _projectionMetadata;
    private Rid _pipeline, _uniforms, _shader;
    private Rid _leafyPrefixPipeline, _leafyPrefixUniforms, _leafyPrefixShader;
    private readonly Rid[] _stageShaders = new Rid[5];
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
    private readonly bool _smallOutput;
    private readonly NeowFamilyGpuExecutor? _sharedDenseScratch;
    private readonly long _ordinalWorkspaceBytes;
    private Rid _readbackBank;
    private const int PublicPrefixCapacity = 65536;
    private int _publicPrefixCount = 1024;
    private bool _disposed, _initializationFaulted;
    private readonly bool _tracePrivate = NcPhysicalExperiment.Mode.Length != 0 || PrivateOrdinalSelection.IsRequested || NwaePhysicalExperiment.Mode.Length != 0;
    internal double SetupMs { get; private set; }
    internal string Device { get; }
    internal const long WorkspaceBytes = (long)Capacity * sizeof(uint) * 2;
    internal long ResidentBytes => _ordinalWorkspaceBytes + (_readbackBank.IsValid ? PublicPrefixCapacity * 4L + Header().Length * 4L : 0) +
        (_prefixHashTable.IsValid ? NeowPrefixHashCache.MaximumTableWords * 4L : 0) +
        (_stagedBuffers is null || _sharedDenseScratch is not null ? 0 :
        (long)NeowFamilyGpuPlan.CurseCapacity * 12 + (long)NeowFamilyGpuPlan.PairCapacity * 48);
    internal bool SupportsDensePair => !_privateInput && _composite is null && _bonesKMode == 0 &&
        _capsulePhysicalMode == 0 && _plan.DirectNestedMode == 0 && _plan.JointTransformMetaOffset < 0;
    internal static string ShaderSource(int stage = 3, bool capsuleComposite = false, bool leafyPreGate = false,
        bool bonesCapsuleComposite = false, bool bonesArcaneComposite = false, int directNestedMode = 0, int capsulePhysicalMode = 0, int bonesKMode = 0, bool authoredUpgrades = false,
        bool localResults = true, bool multiplayer = false, int jointTransformMetaOffset = -1,
        NeowGpuNumericalVariant numericalVariant = NeowGpuNumericalVariant.NativeFloat64,
        bool leafyLazy = false)
    {
        if (numericalVariant != NeowGpuNumericalVariant.NativeFloat64 &&
            (capsuleComposite || bonesCapsuleComposite || bonesArcaneComposite ||
             capsulePhysicalMode != 0 || bonesKMode != 0 || jointTransformMetaOffset >= 0))
            throw new InvalidOperationException("N.IntegerUnitFloat.OrdinarySourceOnly");
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader(multiplayer ? "NeowParty.comp.glsl" : "NeowSingleplayer.comp.glsl")
        .Replace("#define RT2_N_LEAFY_LAZY 0", leafyLazy && leafyPreGate && !multiplayer
            ? "#define RT2_N_LEAFY_LAZY 1" : "#define RT2_N_LEAFY_LAZY 0", StringComparison.Ordinal)
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
        return numericalVariant switch
        {
            NeowGpuNumericalVariant.NativeFloat64 => source,
            NeowGpuNumericalVariant.IntegerUnitFloat => NeowGpuNumericalSource.IntegerUnitFloat(source),
            _ => throw new InvalidOperationException("N.UnknownGpuNumericalVariant")
        };
    }
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
    internal NeowFamilyGpuExecutor(RenderingDevice rd, NeowFamilyGpuPlan plan, uint tag, NeowCapsuleComposite? composite = null, PrivateOrdinalBuffer? privateInput = null, PrivateOrdinalBuffer? privateOutput = null, int capsulePhysicalMode = 0, int bonesKMode = 0, uint[]? bonesKMetadata = null, NeowFamilyGpuPlan[]? partyPlans = null, NeowFamilyGpuExecutor? sharedDenseScratch = null, NeowGpuOptimizations? optimizations = null)
    {
        if (plan.NumericalVariant != NeowGpuNumericalVariant.NativeFloat64 &&
            (plan.JointTransformMetaOffset >= 0 || composite is not null ||
             privateInput is not null || privateOutput is not null || capsulePhysicalMode != 0 || bonesKMode != 0 || partyPlans is not null))
            throw new InvalidOperationException("N.IntegerUnitFloat.PublicOrdinaryOnly");
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
        _smallOutput = _privateOutput && composite is null && bonesKMode == 0 && partyPlans is null && SupportsSmallOutput(plan);
        _plan = plan; _composite = composite; _partySource = partySource; _capsulePhysicalMode = capsulePhysicalMode;
        _optimizations = optimizations ?? sharedDenseScratch?._optimizations ?? NeowGpuOptimizations.Capture();
        _optimizations = _optimizations.Applicable(plan, false, composite is null && bonesKMode == 0 &&
            capsulePhysicalMode == 0 && partySource is null && plan.DirectNestedMode == 0 && plan.JointTransformMetaOffset < 0);
        UsesLeafyLazy = SupportsLeafyLazy(plan) && partySource is null &&
            _optimizations.LeafyLazy;
        _curseFirstPlan = _optimizations.CurseFirst ? NeowInternalFilterOrder.Compile(plan) :
            new(false, [], true, "OptimizationOff");
        if (_privateInput && ReferenceEquals(privateInput, privateOutput)) throw new ArgumentException("N.PrivateBuffersAlias");
        if (sharedDenseScratch is not null)
        {
            sharedDenseScratch.AssertOwner();
            if (!SupportsDensePair || !sharedDenseScratch.SupportsDensePair || !ReferenceEquals(rd, sharedDenseScratch._rd) ||
                !SamePlanRole(sharedDenseScratch)) throw new ArgumentException("N.SharedDenseScratchRoleMismatch");
        }
        _sharedDenseScratch = sharedDenseScratch;
        int inputBytes = _privateOutput && !_privateInput ? sizeof(uint) : Capacity * sizeof(uint);
        _ordinalWorkspaceBytes = (privateInput is null ? inputBytes : 0L) + (privateOutput is null ? Capacity * sizeof(uint) : 0L);
        var timer = Stopwatch.StartNew();
        Rid Buffer(uint[] values, string name) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(values), name));
        try
        {
            _meta = Buffer(new uint[8], "N.Batch"); _header = Buffer(Header(), "N.Header");
            // A private-output/no-private-input executor admits Dense only, so
            // every shader chooses generated ordinals and never reads this binding.
            _input = privateInput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, inputBytes, "N.Input"));
            _output = privateOutput?.Buffer ?? Add(FamilyGpuComputeUtility.AllocateStorageBuffer(rd, Capacity * sizeof(uint), "N.Output"));
            _buffers = [_meta, Buffer(plan.Meta, "N.Plan"), Buffer(plan.PoolMeta, "N.Pools"), Buffer(plan.Cards, "N.Cards"), Buffer(plan.Strike, "N.Strike"),
                Buffer(plan.Defend, "N.Defend"), _header, _output, Buffer(plan.Bones, "N.Bones"), Buffer(plan.Conditions, "N.Conditions"), _input];
            if (bonesKMode == 2 || composite is not null)
                _projectionMetadata = Buffer(bonesKMetadata ?? composite!.Metadata, "N.CapsuleProjection");
            Device = rd.GetDeviceName();
            // Private bindings have a fixed role. Build only that role while the
            // chain's shader-reuse scope is active. Public input is known at Execute.
            if (_privateInput || _privateOutput)
                EnsureModules(plan.UsesStagedDense && !_privateInput,
                    !_privateInput && _optimizations.LeafyLocalPrefix);
            SetupMs = timer.Elapsed.TotalMilliseconds;
        }
        catch (Exception ex)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(ex, () => FamilyGpuComputeUtility.FreeAll(rd, _owned), "N.Constructor");
            throw;
        }
    }
    private Rid Add(Rid rid) { _owned.Add(rid); return rid; }
    private bool SamePlanRole(NeowFamilyGpuExecutor other) => _privateOutput == other._privateOutput &&
        _optimizations == other._optimizations &&
        UsesLeafyLazy == other.UsesLeafyLazy &&
        _smallOutput == other._smallOutput && _partySource == other._partySource &&
        _plan.NumericalVariant == other._plan.NumericalVariant && _plan.HasAuthoredUpgrades == other._plan.HasAuthoredUpgrades &&
        _plan.Meta.AsSpan().SequenceEqual(other._plan.Meta) && _plan.PoolMeta.AsSpan().SequenceEqual(other._plan.PoolMeta) &&
        _plan.Cards.AsSpan().SequenceEqual(other._plan.Cards) && _plan.Strike.AsSpan().SequenceEqual(other._plan.Strike) &&
        _plan.Defend.AsSpan().SequenceEqual(other._plan.Defend) && _plan.Bones.AsSpan().SequenceEqual(other._plan.Bones) &&
        _plan.Conditions.AsSpan().SequenceEqual(other._plan.Conditions);

    private string ModuleSource(int stage, bool leafyLocalPrefix = false)
    {
        string source = stage == 3 && _partySource is not null ? _partySource : ShaderSource(stage,
            capsuleComposite: stage == 3 && _composite is not null,
            leafyPreGate: _plan.UsesLeafyPreGate, bonesCapsuleComposite: _composite?.UsesBonesCheckpoint == true,
            bonesArcaneComposite: _bonesArcane, directNestedMode: _composite is null ? _plan.DirectNestedMode : 0,
            capsulePhysicalMode: stage == 3 ? _capsulePhysicalMode : 0, bonesKMode: _bonesKMode,
            authoredUpgrades: _plan.HasAuthoredUpgrades,
            localResults: _plan.HasLocalResults || _composite is not null || _bonesKMode != 0, multiplayer: _plan.IsMultiplayer,
            jointTransformMetaOffset: _plan.JointTransformMetaOffset, numericalVariant: _plan.NumericalVariant,
            leafyLazy: UsesLeafyLazy);
        source = ApplyOptimizations(source, stage);
        if (leafyLocalPrefix)
            source = source.Replace("#define RT2_N_LEAFY_LOCAL_PREFIX 0", "#define RT2_N_LEAFY_LOCAL_PREFIX 1", StringComparison.Ordinal);
        if (_smallOutput)
        {
            const string append = "if(slot<batch.values[5u]) output_ordinals.values[slot]=ordinal;";
            if (!source.Contains(append, StringComparison.Ordinal)) throw new InvalidOperationException("N.SmallOutputSeamChanged");
            source = source.Replace(append,
                "if(slot<batch.values[5u]) { output_ordinals.values[slot]=ordinal; if(slot<8u) header.values[12u+slot]=ordinal; }",
                StringComparison.Ordinal);
        }
        if (_privateInput)
        {
            const string ordinalRead = "uint ordinal=batch.values[4u]==0u ? first+lane : input_ordinals.values[first+lane];";
            if (!source.Contains(ordinalRead, StringComparison.Ordinal)) throw new InvalidOperationException("N.PrivateInputSeamChanged");
            source = source.Replace(ordinalRead, ordinalRead + "\nif(ordinal>=batch.values[2u]) { atomicExchange(header.values[3u],1u); continue; }", StringComparison.Ordinal);
        }
        return source;
    }

    private void EnsureModules(bool staged, bool leafyLocalPrefix = false)
    {
        if (_initializationFaulted) throw new InvalidOperationException("NFamilyGpuInitializationFaulted");
        if (staged ? _stages.Count == StagedDispatchCount :
            leafyLocalPrefix ? _leafyPrefixPipeline.IsValid : _pipeline.IsValid) return;
        var timer = Stopwatch.StartNew();
        string name = staged ? "NeowFamilyPreBones0" : leafyLocalPrefix ? "NeowFamilyLeafyLocalPrefix" : "NeowFamilyLocalDonor";
        bool checkpointMetadata = _composite?.UsesBonesCheckpoint == true || _bonesKMode == 2;
        try
        {
            if (staged)
            {
                // Allocate checkpoints only when this executor actually receives Dense.
                if (_sharedDenseScratch is not null)
                {
                    _sharedDenseScratch.AssertOwner();
                    _sharedDenseScratch.EnsureModules(true);
                    // Borrowed RIDs are never added to _owned. The pair records
                    // each complete three-stage sequence before the next slot.
                    _stagedBuffers = [.._buffers, _sharedDenseScratch._stagedBuffers![_buffers.Length],
                        _sharedDenseScratch._stagedBuffers[_buffers.Length + 1]];
                }
                else _stagedBuffers = [.._buffers,
                        Add(FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, NeowFamilyGpuPlan.CurseCapacity * 12, "N.CurseSurvivors")),
                        Add(FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, NeowFamilyGpuPlan.PairCapacity * 48, "N.PairSurvivors"))];
                if (checkpointMetadata) _stagedBuffers = [.._stagedBuffers, _projectionMetadata];
                if (_optimizations.GpuPrefix)
                    _prefixHashTable = Add(FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, NeowPrefixHashCache.MaximumTableWords * 4, "N.PrefixHash"));
                foreach (int stage in CurseFirstEnabled ? new[] { 0, 4, 1, 2 } : new[] { 0, 1, 2 })
                {
                    Rid[] stageBuffers = stage == 0 && _optimizations.GpuPrefix ? [.._stagedBuffers, _prefixHashTable] : _stagedBuffers;
                    name = "NeowFamilyPreBones" + stage;
                    if (_optimizations != default) RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundInfo($"nOptimizationModule=true;stage={stage};revision={OptimizationRevisionFor(false)};numeric={_plan.NumericalVariant};privateInput={_privateInput};privateOutput={_privateOutput};dispatches={StagedDispatchCount}");
                    // The donor outlives this Dense-only borrower. Modules are
                    // immutable and have the same plan/transport role; uniforms
                    // and pipelines stay owned by each independent root slot.
                    Rid shader = _sharedDenseScratch is null
                        ? Add(FamilyGpuComputeUtility.CompileShader(_rd, ModuleSource(stage), name))
                        : _sharedDenseScratch._stageShaders[stage];
                    if (!shader.IsValid) throw new InvalidOperationException("N.SharedDenseShaderMissing:" + stage);
                    _stageShaders[stage] = shader;
                    if (_sharedDenseScratch is not null)
                        RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=ShaderModuleReused;name={name};stage=Compute;ownerThreadId={_owner};cache=DensePairDonor;shaderOwner=EarlierRootSlot");
                    Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(_rd, shader, name));
                    if (!_rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("NFamilyStagePipelineInvalid:" + name);
                    _stages.Add((pipeline, Add(FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, stageBuffers, name))));
                }
            }
            else
            {
                if (_optimizations != default) RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundInfo($"nOptimizationModule=true;stage=3;revision={OptimizationRevisionFor(!leafyLocalPrefix)};numeric={_plan.NumericalVariant};privateInput={_privateInput};privateOutput={_privateOutput};leafyLocalPrefix={leafyLocalPrefix};dispatches=1");
                Rid[] buffers = _buffers;
                if (checkpointMetadata)
                    buffers = _stagedBuffers ?? [..buffers,
                        Add(FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, 4, "N.UnusedCurseBinding")),
                        Add(FamilyGpuComputeUtility.AllocateStorageBuffer(_rd, 4, "N.UnusedPairBinding")), _projectionMetadata];
                else if (_composite is not null) buffers = [..buffers, _projectionMetadata];
                _sharedDenseScratch?.EnsureModules(false, leafyLocalPrefix);
                Rid shader = _sharedDenseScratch is null
                    ? Add(FamilyGpuComputeUtility.CompileShader(_rd, ModuleSource(3, leafyLocalPrefix), name))
                    : leafyLocalPrefix ? _sharedDenseScratch._leafyPrefixShader : _sharedDenseScratch._shader;
                if (!shader.IsValid) throw new InvalidOperationException("N.SharedDenseShaderMissing:ordinary");
                if (leafyLocalPrefix) _leafyPrefixShader = shader;
                else _shader = shader;
                if (_sharedDenseScratch is not null)
                    RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundInfo($"searchStartup=true;phase=ShaderModuleReused;name={name};stage=Compute;ownerThreadId={_owner};cache=DensePairDonor;shaderOwner=EarlierRootSlot");
                Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(_rd, shader, name));
                if (!_rd.ComputePipelineIsValid(pipeline)) throw new InvalidOperationException("NFamilyGpuPipelineInvalid:" + name);
                Rid uniforms = Add(FamilyGpuComputeUtility.CreateUniformSet(_rd, shader, buffers, name));
                if (leafyLocalPrefix) { _leafyPrefixPipeline = pipeline; _leafyPrefixUniforms = uniforms; }
                else { _pipeline = pipeline; _uniforms = uniforms; }
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
    private uint[] Header() => _smallOutput ?
        [0x4e464d52, 1, 0, 0, 0, _tag, 0, 0, SmallOutputMagic, SmallOutputCapacity, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] : _bonesKMode != 0 ? [0x4e464d52, 1, 0, 0, 0, _tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] : _bonesArcane ? [0x4e464d52, 1, 0, 0, 0, _tag, 0, 0, 0, 0, 0, 0]
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
    // A prepared batch owns only its immutable identity and this invocation's
    // readback. GPU resources stay resident on its executor until collection.
    private sealed class Prepared
    {
        internal required FamilyCandidateSet Input;
        internal required int InputCount;
        internal required bool Staged;
        internal required uint Groups;
        internal required uint[] InitialHeader;
        internal double UploadMs;
        internal int PrefixCount;
        internal bool LeafyLocalPrefix;
        internal FamilyGpuReadback.Pending? Pending;
        internal byte[]? Readback;
        internal Exception? QueueFailure;
        internal Exception? CompletionFailure;
    }

    private Prepared Prepare(FamilyCandidateSet input, int inputCount, CancellationToken token)
    {
        AssertOwner(); token.ThrowIfCancellationRequested();
        if (inputCount < 0 || inputCount > Capacity || inputCount > input.Batch.BatchCandidateCount)
            throw new InvalidOperationException("NFamilyGpuCapacityExceeded");
        if (_initializationFaulted) throw new InvalidOperationException("NFamilyGpuInitializationFaulted");
        _sharedDenseScratch?.AssertOwner();
        if (_sharedDenseScratch?._initializationFaulted == true) throw new InvalidOperationException("N.SharedDenseScratchFaulted");
        bool dense = input.IsDense && !_privateInput, staged = dense && _plan.UsesStagedDense;
        bool leafyLocalPrefix = dense && _optimizations.LeafyLocalPrefix;
        // Complete all lazy allocation/compilation before any list is recorded.
        if (inputCount > 0) EnsureModules(staged, leafyLocalPrefix);
        if (inputCount > 0 && !staged && !(leafyLocalPrefix ? _leafyPrefixPipeline : _pipeline).IsValid)
            throw new InvalidOperationException("N.UnboundOrdinaryModule");
        if (inputCount > 0 && !_privateOutput && !_readbackBank.IsValid)
        {
            var setupTimer=Stopwatch.StartNew();
            _readbackBank=Add(FamilyGpuComputeUtility.AllocateStorageBuffer(_rd,
                checked(Header().Length*4+PublicPrefixCapacity*4),"N.PublicReadbackBank"));
            SetupMs+=setupTimer.Elapsed.TotalMilliseconds;
        }
        var timer = Stopwatch.StartNew();
        var prepared=new Prepared { Input=input, InputCount=inputCount, Staged=staged,
            Groups=(uint)((inputCount+511)/512), InitialHeader=Header(),
            PrefixCount=_privateOutput ? 0 : Math.Min(inputCount,_publicPrefixCount), LeafyLocalPrefix=leafyLocalPrefix };
        // Empty work has no device or previous-header dependency.
        if(inputCount>0)
        {
            FamilyGpuComputeUtility.Update(_rd, _meta, [(uint)input.Batch.BatchBase, (uint)(input.Batch.BatchBase >> 32),
                (uint)input.Batch.BatchCandidateCount, (uint)inputCount, dense ? 0u : 1u, Capacity,
                NeowFamilyGpuPlan.CurseCapacity, NeowFamilyGpuPlan.PairCapacity], "NFamilyBatch");
            FamilyGpuComputeUtility.Update(_rd, _header, prepared.InitialHeader, "NFamilyHeader");
            if (staged && _optimizations.GpuPrefix)
                FamilyGpuComputeUtility.Update(_rd, _prefixHashTable, NeowPrefixHashCache.BuildTable(input.Batch.BatchBase, inputCount), "N.PrefixHash");
            if (staged && CurseFirstEnabled)
                FamilyGpuComputeUtility.Update(_rd, _output, [0u], "N.FinalCurseCounter");
            if (!input.IsDense && !_privateInput) FamilyGpuComputeUtility.UpdateBytes(_rd, _input,
                FamilyGpuComputeUtility.PackAbi1UInt32(input), "NFamilyCompact");
        }
        prepared.UploadMs=timer.Elapsed.TotalMilliseconds;
        return prepared;
    }

    private void Record(long list, Prepared prepared)
    {
        if(prepared.Groups==0)return;
        if(prepared.Staged)
        {
            for(int stage=0;stage<StagedDispatchCount;stage++)
            {
                if(stage>0)_rd.ComputeListAddBarrier(list);
                _rd.ComputeListBindComputePipeline(list,_stages[stage].Pipeline);
                _rd.ComputeListBindUniformSet(list,_stages[stage].Uniforms,0);
                _rd.ComputeListDispatch(list,stage==0 ? prepared.Groups :
                    (uint)((stage < StagedDispatchCount-1 ? NeowFamilyGpuPlan.CurseCapacity : NeowFamilyGpuPlan.PairCapacity)/512),1,1);
            }
        }
        else
        {
            _rd.ComputeListBindComputePipeline(list,prepared.LeafyLocalPrefix ? _leafyPrefixPipeline : _pipeline);
            _rd.ComputeListBindUniformSet(list,prepared.LeafyLocalPrefix ? _leafyPrefixUniforms : _uniforms,0);
            _rd.ComputeListDispatch(list,prepared.Groups,1,1);
        }
    }

    private void QueueReadback(Prepared prepared)
    {
        if(prepared.Groups==0)return;
        uint headerBytes=checked((uint)prepared.InitialHeader.Length*4);
        try
        {
            if(_privateOutput)
                prepared.Pending=FamilyGpuReadback.Queue(_rd,_header,0,headerBytes,$"N.PrivateHeader:batch={prepared.Input.Batch.BatchBase}");
            else
            {
                // Both copies are ordered after compute and before this submit.
                // No prefix entry is decoded until its validated header count owns it.
                Error headerCopy=_rd.BufferCopy(_header,_readbackBank,0,0,headerBytes);
                if(headerCopy!=Error.Ok)prepared.QueueFailure=new InvalidOperationException($"N.HeaderBankCopy:batch={prepared.Input.Batch.BatchBase};error={headerCopy}");
                uint prefixBytes=checked((uint)prepared.PrefixCount*4);
                if(prefixBytes>0)
                {
                    Error prefixCopy=_rd.BufferCopy(_output,_readbackBank,0,headerBytes,prefixBytes);
                    if(prefixCopy!=Error.Ok)
                    {
                        var failure=new InvalidOperationException($"N.PrefixBankCopy:batch={prepared.Input.Batch.BatchBase};error={prefixCopy}");
                        prepared.QueueFailure=prepared.QueueFailure is null ? failure : new AggregateException(prepared.QueueFailure,failure);
                    }
                }
                prepared.Pending=FamilyGpuReadback.Queue(_rd,_readbackBank,0,headerBytes+prefixBytes,$"N.PublicBoundary:batch={prepared.Input.Batch.BatchBase}");
            }
        }
        catch(Exception failure)
        {
            // Do not abandon commands already recorded on this device.
            prepared.QueueFailure=prepared.QueueFailure is null ? failure : new AggregateException(prepared.QueueFailure,failure);
        }
    }

    private static double SubmitPrepared(NeowFamilyGpuExecutor[] executors, Prepared[] prepared,
        Action<int, object>? beforeSubmitProbe = null)
    {
        if(!prepared.Any(item=>item.Groups>0))return 0;
        RenderingDevice rd=executors[0]._rd;
        var timer=Stopwatch.StartNew();
        long list=rd.ComputeListBegin();
        Exception? recordingFailure=null;
        try
        {
            bool prior=false;
            for(int slot=0;slot<prepared.Length;slot++)
            {
                if(prepared[slot].Groups==0)continue;
                // Finish every stage of the earlier root before reusing scratch.
                if(prior)rd.ComputeListAddBarrier(list);
                try { executors[slot].Record(list,prepared[slot]); }
                catch(Exception failure) { throw new InvalidOperationException($"N.RecordFailure:slot={slot};batch={prepared[slot].Input.Batch.BatchBase}",failure); }
                prior=true;
            }
        }
        catch(Exception failure) { recordingFailure=failure; }
        try { rd.ComputeListEnd(); }
        catch(Exception failure) { recordingFailure=recordingFailure is null ? failure : new AggregateException(recordingFailure,failure); }
        if(recordingFailure is null)
            for(int slot=0;slot<prepared.Length;slot++)
            {
                executors[slot].QueueReadback(prepared[slot]);
                // Explicit native fault checks may inspect this invocation's
                // prepared slot. No process setting or normal caller enables it.
                if(beforeSubmitProbe is not null)
                    try { beforeSubmitProbe(slot,prepared[slot]); }
                    catch(Exception failure) { prepared[slot].QueueFailure=failure; }
            }
        try { FamilyGpuComputeUtility.SubmitAndSync(rd,"N.Neow"); }
        catch(Exception failure)
        {
            foreach(var executor in executors)executor._initializationFaulted=true;
            if(recordingFailure is not null)throw new AggregateException("N.RecordAndDrain",recordingFailure,failure);
            throw;
        }
        if(recordingFailure is not null)
        {
            foreach(var executor in executors)executor._initializationFaulted=true;
            ExceptionDispatchInfo.Capture(recordingFailure).Throw();
        }
        // Callback/schedule/copy failures are reported only after the whole
        // group is drained. No callback validates or publishes either slot.
        for(int slot=0;slot<prepared.Length;slot++)
        {
            Prepared item=prepared[slot];
            try
            {
                if(item.QueueFailure is not null)ExceptionDispatchInfo.Capture(item.QueueFailure).Throw();
                if(item.Pending is not null)item.Readback=item.Pending.GetData();
            }
            catch(Exception failure)
            {
                // The shared fence is complete. A slot-local copy/download
                // fault is adjudicated when that slot is collected, allowing
                // a validated earlier member to retain its original handoff.
                item.CompletionFailure=failure;
                RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundWarning($"nReadbackSlotFault=true;slot={slot};batch={item.Input.Batch.BatchBase};stage=CompletedReadback;cause={RolltheSpire2.Bootstrap.RuntimeLog.SafeJson(failure.ToString())}");
            }
        }
        return timer.Elapsed.TotalMilliseconds;
    }

    internal static (FamilyCandidateSet? Left, NeowFamilyGpuMetrics LeftMetrics,
        FamilyCandidateSet? Right, NeowFamilyGpuMetrics RightMetrics, double SharedMs) ExecuteDensePair(
        NeowFamilyGpuExecutor first, SearchBatch firstBatch, NeowFamilyGpuExecutor second, SearchBatch secondBatch, CancellationToken token,
        Action<int, object>? beforeSubmitProbe = null)
    {
        first.AssertOwner(); second.AssertOwner(); token.ThrowIfCancellationRequested();
        if(ReferenceEquals(first,second) || !ReferenceEquals(first._rd,second._rd) ||
            !first.SupportsDensePair || !second.SupportsDensePair || !first.SamePlanRole(second))
            throw new InvalidOperationException("N.DensePairScopeMismatch");
        Prepared left=first.Prepare(FamilyCandidateSet.Dense(firstBatch),firstBatch.BatchCandidateCount,token);
        Prepared right=second.Prepare(FamilyCandidateSet.Dense(secondBatch),secondBatch.BatchCandidateCount,token);
        token.ThrowIfCancellationRequested();
        // One owner/list/submit/sync; at most two complete root sequences before
        // cancellation is observed. Shared waiting belongs to this group once.
        double sharedMs=SubmitPrepared([first,second],[left,right],beforeSubmitProbe);
        token.ThrowIfCancellationRequested();
        var leftResult=first.Collect(left,0,token,out var leftMetrics);
        try
        {
            var rightResult=second.Collect(right,0,token,out var rightMetrics);
            return (leftResult,leftMetrics,rightResult,rightMetrics,sharedMs);
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception failure)
        {
            second._initializationFaulted=true;
            throw new NeowDensePairRightFailure(leftResult,leftMetrics,sharedMs,secondBatch,failure);
        }
    }

    private FamilyCandidateSet? ExecuteCore(FamilyCandidateSet input, int inputCount, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        Prepared prepared=Prepare(input,inputCount,token);
        token.ThrowIfCancellationRequested();
        double sharedMs=SubmitPrepared([this],[prepared]);
        token.ThrowIfCancellationRequested();
        return Collect(prepared,sharedMs,token,out metrics);
    }

    private FamilyCandidateSet? Collect(Prepared prepared, double dispatchSyncMs, CancellationToken token, out NeowFamilyGpuMetrics metrics)
    {
        if(prepared.CompletionFailure is not null)ExceptionDispatchInfo.Capture(prepared.CompletionFailure).Throw();
        var timer=Stopwatch.StartNew();
        FamilyCandidateSet input=prepared.Input;
        int inputCount=prepared.InputCount;
        bool staged=prepared.Staged;
        uint groups=prepared.Groups;
        ReadOnlySpan<uint> header=prepared.Readback is null ? prepared.InitialHeader :
            FamilyGpuComputeUtility.UInt32Span(prepared.Readback).Slice(0,prepared.InitialHeader.Length);
        if (header.Length != (_smallOutput ? 20 : _bonesKMode != 0 ? 16 : _bonesArcane ? 12 : 8) || header[0] != 0x4e464d52 || header[1] != 1 || header[5] != _tag ||
            header[3] != 0 || header[4] != inputCount || header[2] > header[4] ||
            (staged && (header[6] > NeowFamilyGpuPlan.CurseCapacity || header[7] > NeowFamilyGpuPlan.PairCapacity || header[7] > header[6])))
            throw new InvalidDataException($"NFamilyGpuHeaderInvalid:batch={input.Batch.BatchBase};count={inputCount}");
        if (_bonesKMode != 0) {
            BonesKCounts = header[8..16].ToArray();
            ulong nPass=(ulong)header[8]+header[9]+header[10];
            if(nPass>(ulong)inputCount || (staged && nPass>header[7]) ||
                (_bonesKMode==1 && nPass!=header[2]) ||
                (_bonesKMode==2 && (header[11]>(ulong)header[8]+header[10] || header[12]>(ulong)header[9]+header[10] ||
                    header[13]>header[11] || header[14]>header[12] || header[15]!=header[2] || header[2]>(ulong)header[13]+header[14])))
                throw new InvalidDataException("BonesK.SameRouteCountersInvalid");
        }
        uint count=header[2];
        if (_smallOutput && (header[8] != SmallOutputMagic || header[9] != SmallOutputCapacity || header[10] != 0 || header[11] != 0))
            throw new InvalidDataException("N.SmallOutputHeaderInvalid");
        long readbackBytes=prepared.Groups==0 ? 0 : (prepared.InitialHeader.Length+prepared.PrefixCount)*4L;
        int dispatches=groups==0 ? 0 : staged ? StagedDispatchCount : 1;
        if(_privateOutput)
        {
            double readbackMs=timer.Elapsed.TotalMilliseconds;
            FamilyCandidateSet? smallOutput=null;
            if(_smallOutput && count<=SmallOutputCapacity && SmallCandidateExactPolicy.ShouldHandoff(checked((int)count)))
            {
                var smallOrdinals=new ulong[count];
                for(int i=0;i<smallOrdinals.Length;i++)smallOrdinals[i]=header[SmallOutputOffset+i];
                Array.Sort(smallOrdinals);
                smallOutput=FamilyCandidateSet.FromSortedAbi1(input.Batch,smallOrdinals);
            }
            double localMs=timer.Elapsed.TotalMilliseconds;
            metrics=new(inputCount,checked((int)count),groups,readbackBytes,prepared.UploadMs+dispatchSyncMs+localMs,
                prepared.UploadMs,dispatchSyncMs,readbackMs,localMs-readbackMs,dispatches,header[6],header[7],0,0,0,0,0)
                { SmallOutput=smallOutput };
            return null;
        }
        if (_bonesArcane && (header[8] > input.Count || header[9] > header[8] || header[10] > header[9] ||
            header[11] < header[10] || header[11] > 2UL * header[10] || count > header[10]))
            throw new InvalidDataException("NrBonesArcaneCountersInvalid");
        // This controls only next invocation's transfer size. A high-output
        // boundary warms the larger tier; every result beyond either tier still
        // reads its complete tail below, independently of this observation.
        _publicPrefixCount=count>1024 ? PublicPrefixCapacity : 1024;
        double payloadStartMs=timer.Elapsed.TotalMilliseconds;
        var packed=new uint[count];
        int prefixCount=Math.Min(checked((int)count),prepared.PrefixCount);
        if(prefixCount>0)
            FamilyGpuComputeUtility.UInt32Span(prepared.Readback!).Slice(prepared.InitialHeader.Length,prefixCount).CopyTo(packed);
        if(count>prefixCount)
        {
            uint remaining=count-(uint)prefixCount;
            byte[] tail=_rd.BufferGetData(_output,checked((uint)prefixCount*4),remaining*4);
            ReadOnlySpan<uint> tailOrdinals=FamilyGpuComputeUtility.UInt32Span(tail);
            if(tailOrdinals.Length!=remaining)throw new InvalidDataException("N.PayloadCountMismatch");
            tailOrdinals.CopyTo(packed.AsSpan(prefixCount));
            readbackBytes+=remaining*4L;
        }
        double readMs=timer.Elapsed.TotalMilliseconds;
        // Preserve the full permutation (including masks/invalid values) until
        // ordinary ABI1 bounds and duplicate validation has seen every element.
        bool packedRadix=!_plan.IsMultiplayer && _partySource is null && packed.Length>=65536;
        double sortMs=0;
        if(packedRadix)
        {
            SortLargePackedOrdinals(packed);
            sortMs=timer.Elapsed.TotalMilliseconds-readMs;
        }
        double widenStart=timer.Elapsed.TotalMilliseconds;
        var ordinals=new ulong[packed.Length];
        for(int i=0;i<packed.Length;i++)ordinals[i]=packed[i];
        double widenMs=timer.Elapsed.TotalMilliseconds-widenStart;
        double sortStart=timer.Elapsed.TotalMilliseconds;
        if(!packedRadix && ordinals.Length>1)Array.Sort(ordinals);
        sortMs+=timer.Elapsed.TotalMilliseconds-sortStart;
        double validationStart=timer.Elapsed.TotalMilliseconds;
        var result=FamilyCandidateSet.FromSortedAbi1(input.Batch,ordinals);
        token.ThrowIfCancellationRequested();
        double localReadyMs=timer.Elapsed.TotalMilliseconds;
        double canonicalMs=prepared.UploadMs+dispatchSyncMs+localReadyMs;
        metrics=new(inputCount,ordinals.Length,groups,readbackBytes,canonicalMs,
            prepared.UploadMs,dispatchSyncMs,readMs,localReadyMs-readMs,dispatches,header[6],header[7],
            _bonesArcane ? header[8] : 0,_bonesArcane ? header[9] : 0,_bonesArcane ? header[10] : 0,
            _bonesArcane ? header[11] : 0,_bonesArcane && count>0 ? readMs-payloadStartMs : 0);
        if(_tracePrivate)
            Bootstrap.RuntimeLog.TryBackgroundDetail($"ncStageTiming=true;stage=N;input={inputCount};output={result.Count};dispatches={dispatches};uploadMs={prepared.UploadMs};dispatchSyncMs={dispatchSyncMs};readbackMs={readMs};readbackBytes={readbackBytes};widenMs={widenMs};sortMs={sortMs};validationMs={localReadyMs-validationStart};canonicalMs={canonicalMs};packedRadix={packedRadix}");
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
