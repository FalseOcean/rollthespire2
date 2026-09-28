using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

// Numeric packing adopted from N0/P1. No historical executable plan is created.
internal sealed record NeowFamilyGpuPlan(uint[] Meta, uint[] PoolMeta, uint[] Cards,
    uint[] Strike, uint[] Defend, uint[] Bones, uint[] Conditions)
{
    internal bool HasAuthoredUpgrades { get; init; }
    internal bool IsMultiplayer => Meta[6] > 1;
    internal bool HasLocalResults => Meta[83] != 0 || Meta[84] != 0;
    internal const string DenseRevision = "N.Neow.Gpu.LocalDonor.DenseCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string CompactRevision = "N.Neow.Gpu.LocalDonor.CompactFullDecode.CanonicalAbi1Ready.20260905.v2";
    internal const string StagedDenseRevision = "N.Neow.Gpu.LocalDonor.DensePreBonesCarry8.CanonicalAbi1Ready.20260905.v2";
    internal const string LeafyDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DenseCarry8.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyCompactRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.CompactFullDecode.CanonicalAbi1Ready.20260906.v1";
    internal const string LeafyStagedDenseRevision = "N.Neow.Gpu.LocalDonor.LeafyPreGate.DensePreBonesCarry8.CanonicalAbi1Ready.20260906.v1";
    internal bool UsesStagedDense => Meta[66] != 0;
    internal bool UsesLeafyPreGate => Meta[70] != 0;
    // Bounded N-private scheduling; explicit controls retain the same-build oracle.
    internal int DirectNestedMode { get; init; }
    internal string DirectNestedRevision(bool compact) => $"N.Neow.Gpu.DirectNested.{DirectNestedName}.{(compact ? "Compact" : UsesStagedDense ? "DensePreBones" : "DenseCarry8")}.CanonicalAbi1Ready.20260909.v1";
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
        _ => throw new InvalidOperationException("NDirectModeInvalid") };
    internal const int CurseCapacity = 1 << 22;
    internal const int PairCapacity = 1 << 16;
    internal static bool TryCreate(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue) =>
        plan.Authority.PlayersCount == 1
            ? NeowSingleplayerGpuPlan.TryCreate(plan, out gpu, out issue)
            : NeowPartyGpuPlan.TryCreate(plan, out gpu, out issue);
}
