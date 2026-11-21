using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;
using NumericRequirements = RolltheSpire2.Search.FamilyExecution.GpuNumericRequirements;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum NeowGpuNumericalVariant { NativeFloat64, IntegerUnitFloat }

// Numeric packing adopted from N0/P1. No historical executable plan is created.
internal sealed record NeowFamilyGpuPlan(uint[] Meta, uint[] PoolMeta, uint[] Cards,
    uint[] Strike, uint[] Defend, uint[] Bones, uint[] Conditions)
{
    internal NeowGpuNumericalVariant NumericalVariant { get; init; }
    internal GpuNumericRequirements GpuNumericRequirements => NumericalVariant switch
    {
        NeowGpuNumericalVariant.NativeFloat64 => NumericRequirements.Int64 | NumericRequirements.Float64,
        NeowGpuNumericalVariant.IntegerUnitFloat => NumericRequirements.Int64,
        _ => NumericRequirements.Unspecified
    };
    internal bool HasAuthoredUpgrades { get; init; }
    internal bool DirectFirstDrawRequested { get; init; }
    internal bool BonesDrawUnobserved { get; init; }
    internal NeowPartyDirectDraw? PartyDirectDraw { get; init; }
    internal NeowEventFirstDrawGate? EventFirstDraw { get; init; }
    internal int CapsuleNicheMetaOffset { get; init; } = -1;
    internal NeowEntryIdentityMode EntryIdentityMode { get; init; }
    internal byte EntryIdentityTarget { get; init; } = 255;
    internal int EntryIdentityMetaOffset { get; init; } = -1;
    internal string EntryIdentityRevision =>
        ((EntryIdentityMode & NeowEntryIdentityMode.RejectUnavailable) != 0 ? ".EntryUnavailableReject.20261030.v1" : "") +
        ((EntryIdentityMode & NeowEntryIdentityMode.TrackSingleRequired) != 0 ? ".EntrySingleIdentityTrack.20261030.v1" : "");
    internal int JointTransformMetaOffset { get; init; } = -1;
    internal NeowModernTransformGate? ModernTransformGate { get; init; }
    internal string ModernTransformGateRevision =>
        ModernTransformGate?.PhysicalRevision ?? "";
    internal string LocalObservationRevision => Meta[NeowLocalObservationPacking.UniquePools] != 0 && !IsMultiplayer
        ? NeowLocalObservationPacking.Revision : "";
    internal bool IsMultiplayer => Meta[6] > 1;
    internal bool UsesChildShuffle => (Meta[56] & 64u) != 0;
    internal string CurseFirstDrawRevision => UsesChildShuffle && !IsMultiplayer ? ".CurseFirstDrawRne.20261119.v1" : "";
    internal bool HasLocalResults => Meta[83] != 0 || Meta[84] != 0;
    internal const string DenseRevision = "N.Neow.Gpu.LocalDonor.DenseCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string CompactRevision = "N.Neow.Gpu.LocalDonor.CompactFullDecode.CanonicalAbi1Ready.20260905.v2";
    internal const string StagedDenseRevision = "N.Neow.Gpu.LocalDonor.DensePreBonesCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string LeafyDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DenseCarry8.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyCompactRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.CompactFullDecode.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyStagedDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DensePreBonesCarry8.CanonicalAbi1Ready.20260906.v1";
    internal bool UsesStagedDense => Meta[66] != 0;
    internal const string DirectFirstDrawRevision = ".DirectFirstDraw.20261119.v1";
    internal const string DenseDirectPrefixRevision = ".DenseBatchPrefix34x4.20261120.v1";
    internal const string DirectFirstDrawVariable = "RT2_N_DIRECT_FIRST_DRAW";
    internal bool DensePrefixRequested { get; init; } = !string.Equals(
        Environment.GetEnvironmentVariable("RT2_N_DENSE_PREFIX_TABLE"), "off", StringComparison.OrdinalIgnoreCase);
    internal bool UsePartyDensePrefix => DensePrefixRequested && IsMultiplayer && !UsesStagedDense &&
        NumericalVariant == NeowGpuNumericalVariant.NativeFloat64;
    internal bool UseDirectFirstDraw => DirectFirstDrawRequested && TryCompileTerminalCurseGate(out _);
    private bool UsesOrdinarySoloMenuStage => !IsMultiplayer && UsesChildShuffle && !UsesStagedDense &&
        DirectNestedMode == 0 && !HasLocalResults && !UsesLeafyPreGate && ModernTransformGate is null &&
        JointTransformMetaOffset < 0 && !HasAuthoredUpgrades && CapsuleNicheMetaOffset < 0 &&
        Meta[NeowCapsuleRarityGate.GpuMetadataIndex] == 0;
    private bool HasBonesConstraint => (Meta[56] & 3u) != 0;

    // The position tracker evaluates the entire menu, including any constraints on the curse slot.
    internal bool CanShareTrackedPositive => UsesOrdinarySoloMenuStage && !HasBonesConstraint &&
        DirectFirstDrawRequested && (EntryIdentityMode & NeowEntryIdentityMode.TrackSingleRequired) != 0;

    internal bool TryCompileTerminalCurseGate(out NeowStaticDrawGateCompiler.Gate? gate)
    {
        gate = null;
        if (!UsesOrdinarySoloMenuStage || (HasBonesConstraint && !BonesDrawUnobserved) || Meta[39] is 0 or > 10)
            return false;

        // The existing P9 masks must resolve every packed curse ordinal.
        // This predicate has no use for the selected route slot or RNG state.
        int count = (int)Meta[39];
        uint accepted = Meta[68], rejected = Meta[69];
        uint domain = (1u << count) - 1u;
        if (accepted == 0 || rejected == 0 || (accepted & rejected) != 0 || (accepted | rejected) != domain)
            return false;
        return NeowStaticDrawGateCompiler.TryCompileRanks(count,
            i => (accepted & (1u << i)) != 0 && Meta[71 + i] != uint.MaxValue, 2, out gate, binary64: true);
    }
    private readonly bool _terminalPairEnabled = !string.Equals(Environment.GetEnvironmentVariable("RT2_N_TERMINAL_PAIR"),
        "off", StringComparison.OrdinalIgnoreCase);
    private readonly bool _pairMaskEnabled = !string.Equals(Environment.GetEnvironmentVariable("RT2_N_PAIR_MASK"),
        "off", StringComparison.OrdinalIgnoreCase);
    private bool EndsAtPairIdentity => UsesStagedDense && !IsMultiplayer && !HasLocalResults &&
        Meta[NeowCapsuleRarityGate.GpuMetadataIndex] == 0 && DirectNestedMode == 0 && JointTransformMetaOffset < 0;
    internal bool UseTerminalPair => _terminalPairEnabled && EndsAtPairIdentity;
    internal const string TerminalPairRevision = ".PairIdentityTerminal.20261119.v1";
    internal bool UsePairMask => _pairMaskEnabled && EndsAtPairIdentity && UsesChildShuffle &&
        Meta[67] == 1 && Meta[8] is >= 2 and <= 32 && Meta[25] < Meta[8] && Meta[26] < Meta[8] &&
        Meta[25] != Meta[26] && Meta[27] != Meta[28] && Meta[12] == 255 && Meta[13] == 255 &&
        !HasAuthoredUpgrades && CapsuleNicheMetaOffset < 0 && ModernTransformGate is null;
    internal const string PairMaskRevision = ".UnorderedPairMask32.20261119.v1";
    internal bool UsesLeafyPreGate => Meta[70] != 0;
    internal bool HasSingleKaleidoscopeWithLeafy { get; init; }
    // Only staged Dense already established Leafy before reaching the pair checkpoint.
    internal bool UseStagedLeafyKaleidoscope => UsesStagedDense && UsesLeafyPreGate && HasSingleKaleidoscopeWithLeafy &&
        Meta[83] == 0 && !HasAuthoredUpgrades && CapsuleNicheMetaOffset < 0 &&
        FixedPairLocalSourceMask == ((1L << Beta110FastRelicCatalog.LeafyPoultice) | (1L << Beta110FastRelicCatalog.Kaleidoscope));
    internal const string StagedLeafyKaleidoscopeRevision = ".IndependentLeafyKaleidoscope.20261121.v1";
    // Stage 1 checkpoints the proven pair and Stage 3 runs the same pair_identity
    // before local replay. The modern joint transform gate reads only its own
    // Transformations prefix; it cannot replace or change either pickup source.
    internal long FixedPairLocalSourceMask
    {
        get
        {
            if (!UsesChildShuffle || IsMultiplayer || !HasLocalResults || (Meta[56] & 2u) == 0 ||
                Meta[67] != 1 || Meta[27] >= 32 || Meta[28] >= 32 || DirectNestedMode != 0) return -1;
            long mask = (1L << (int)Meta[27]) | (1L << (int)Meta[28]);
            const long leafPair = (1L << Beta110FastRelicCatalog.LeafyPoultice) | (1L << Beta110FastRelicCatalog.NewLeaf);
            return JointTransformMetaOffset < 0 || mask == leafPair ? mask : -1;
        }
    }
    internal string FixedPairLocalSourceRevision(int stage) => FixedPairLocalSourceMask < 0 ? "" : stage switch
    {
        2 => ".FixedPairLocalSources.20261114.v1",
        3 => ".FixedPairLocalSources.Ordinary.20261114.v1",
        _ => ""
    };
    // Ordinary direct replay starts pair_identity with the authored selected
    // source and a missing second slot. No route can acquire a companion.
    internal long DirectLocalSourceMask => UsesChildShuffle && !IsMultiplayer && HasLocalResults &&
        (Meta[56] & 2u) == 0 && Meta[81] < 32 && DirectNestedMode == 0 && JointTransformMetaOffset < 0
        ? 1L << (int)Meta[81] : -1;
    internal string LocalSourceRevision(int stage) => stage == 3 && DirectLocalSourceMask >= 0
        ? ".DirectLocalSource.20261114.v1" : FixedPairLocalSourceRevision(stage);
    // Bounded N-private scheduling; explicit controls retain the same-build oracle.
    internal int DirectNestedMode { get; init; }
    internal string DirectNestedRevision(bool compact) => $"N.Neow.Gpu.DirectNested.{DirectNestedName}.{(compact ? "Compact" : UsesStagedDense ? "DensePreBones" : "DenseCarry8")}.CanonicalAbi1Ready.20260909.v1.LocalPoolAuthority.20261114.v1";
    private string DirectNestedName => DirectNestedMode switch {
        1 => "KaleidoscopeFirstReplay", 2 => "KaleidoscopeFirstContinue", 3 => "KaleidoscopeBothReplay",
        4 => "KaleidoscopeLocalReject", 5 => "NewLeafReplay", 6 => "NewLeafPredicateFirst",
        7 => "ScrollSpecialFirst", 8 => "ScrollSpecialLocal", 9 => "KaleidoscopeDirectControl",
        10 => "LostLocal", 11 => "LostRareReplay", 12 => "LostRareContinue", 13 => "LostPotionRareReplay", 14 => "LostDirectControl",
        20 => "LeadRareLocal", 21 => "LeadRareReplay", 22 => "LeadIdentityDirect",
        30 => "ArcanePredicateFirst", 31 => "ArcaneIndexFirst", 32 => "ArcaneDirectControl", 33 => "ArcaneIndexLocal",
        40 => "PhialRareReplay", 41 => "PhialRareLocal", 42 => "PhialIdentityDirect",
        100 => "BonesEarlyAcceptControl", 101 => "BonesKaleidoscopeRareLocalControl",
        102 => "BonesIndependentPairLocal",
        103 => "BonesSharedNonCombatPairLocal",
        _ => throw new InvalidOperationException("NDirectModeInvalid") };
    internal const int CurseCapacity = 1 << 22;
    internal const int PairCapacity = 1 << 16;
    internal static bool TryCreate(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue) =>
        plan.Authority.PlayersCount == 1
            ? NeowSingleplayerGpuPlan.TryCreate(plan, out gpu, out issue)
            : NeowPartyGpuPlan.TryCreate(plan, out gpu, out issue);

    // Public ordinary solo and individual party bodies only. Fused/private and
    // composite paths must not infer compatibility from the N family name.
    internal static bool TryCreateIntegerUnitFloat(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue)
    {
        gpu = null;
        if (!TryCreate(plan, out var native, out issue) || native is null) return false;
        gpu = native with { NumericalVariant = NeowGpuNumericalVariant.IntegerUnitFloat };
        return true;
    }
}
