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
    internal bool HasLocalResults => Meta[83] != 0 || Meta[84] != 0;
    internal const string DenseRevision = "N.Neow.Gpu.LocalDonor.DenseCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string CompactRevision = "N.Neow.Gpu.LocalDonor.CompactFullDecode.CanonicalAbi1Ready.20260905.v2";
    internal const string StagedDenseRevision = "N.Neow.Gpu.LocalDonor.DensePreBonesCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string LeafyDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DenseCarry8.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyCompactRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.CompactFullDecode.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyStagedDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DensePreBonesCarry8.CanonicalAbi1Ready.20260906.v1";
    internal bool UsesStagedDense => Meta[66] != 0;
    internal bool UsesLeafyPreGate => Meta[70] != 0;
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
