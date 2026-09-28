using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class Beta110GpuCombatRewardConstants
{
    public const int PlanAbiVersion = 10;
    public const int ShaderAbiVersion = 7;
    public const uint SyntheticCandidatePassFlag = 0x80000000u;
    public const uint RealRouteMaskBits = 0x0000001fu;
    public const int WorkgroupSize = 64;
    public const int RecoveryBatchSeedCount = 1 << 18; // accepted generic/pinned batch
    public const int MaximumOptimizedRecoveryBatchSeedCount = 1 << 20;
    public const int ParitySeedCount = 4_096;
    public const int CapsuleWorkCapacity = 1 << 18;
    public const int CapsuleWorkStrideUInts = 4;
    public const int HeaderUIntCount = 16;
    public const int CompactOutputCountHeaderIndex = 9;
    public const int CompactOutputOverflowHeaderIndex = 10;
    public const int PredicateStrideUInts = 16;
    public const int PoolFixedUIntCount = 48;
    public const int OrdinaryDescriptorStrideUInts = 4;
    public const int TopCapabilityStrideUInts = 2;
    public const string MainShaderResourceSuffix = "Beta110GpuCombatRewardMainR1.comp.glsl";
    public const string CapsuleShaderResourceSuffix = "Beta110GpuCombatRewardCapsuleR2.comp.glsl";
    public const string CommonShaderResourceSuffix = "Beta110GpuCombatRewardCommon.glsl";
    public const string StreamingMainShaderResourceSuffix = "Beta110GpuCombatRewardMainStreamingP10A.comp.glsl";
    public const string StreamingCapsuleShaderResourceSuffix = "Beta110GpuCombatRewardCapsuleStreamingP10A.comp.glsl";
    public const string StreamingSupplementResourceSuffix = "Beta110GpuCombatRewardStreamingP10A.glsl";
}

internal enum Beta110GpuCombatRewardRoutePolicy : byte
{
    LegacyAllRealRoutesDiagnostic = 0,
    UnpinnedAssumeUnperturbed = 1,
    PinnedRealRoute = 2
}

internal static class CombatRewardGpuPacking
{
    private static readonly ulong NeowEventHash = XxHash64.Hash("NEOW"u8, 0UL);
    private static readonly ulong RewardsStreamHash = XxHash64.Hash("rewards"u8, 0UL);
    private static readonly ulong NicheStreamHash = XxHash64.Hash("niche"u8, 0UL);
    private static readonly ulong UpFrontStreamHash = XxHash64.Hash("up_front"u8, 0UL);

    internal static uint[] BuildPlanMeta(int playerSlot, int players, int ascension,
        bool allUnlocked, bool scrollAllowed, bool defect, int bonesPoolCount,
        Beta110CombatRewardFastPlan reward, uint tag, Beta110FastEffectCatalog catalog,
        Beta110GpuCombatRewardRoutePolicy routePolicy, byte pinnedRelicId,
        byte requestedBonesFirstId, byte requestedBonesSecondId, Beta110GpuCombatRewardHotLoopPlan hotLoop,
        int? precedingNicheDraws = 0, int[]? capsuleNicheAdvances = null,
        bool kaleidoscopeCountOnly = false, bool conservativeOpening = false,
        bool actualBonesPair = false, byte[]? bonesPool = null, bool dynamicCapsuleNicheUnknown = false,
        bool capsuleHeldReplay = false)
    {
        const int fixedHeader = 61;
        int cardTableUInts = hotLoop.Enabled ? hotLoop.CardStateCount * 4 : 0;
        int potionDropUInts = hotLoop.Enabled ? hotLoop.PotionDropNodeCount * 2 : 0;
        int potionRarityUInts = hotLoop.Enabled ? 4 : 0;
        int bonesOffset = fixedHeader + cardTableUInts + potionDropUInts + potionRarityUInts;
        int cardAssignmentOffset = bonesOffset + (bonesPool?.Length ?? 0);
        int potionAssignmentOffset = cardAssignmentOffset + reward.CardAssignmentTargets.Length;
        var meta = new uint[potionAssignmentOffset + reward.PotionAssignmentTargets.Length * 2];
        meta[0] = Beta110GpuCombatRewardConstants.PlanAbiVersion;
        meta[1] = tag;
        meta[2] = checked((uint)playerSlot);
        meta[3] = checked((uint)players);
        meta[4] = checked((uint)ascension);
        meta[5] = checked((uint)reward.MaximumBattleOrdinal);
        meta[6] = (allUnlocked ? 1u : 0u) |
                  (scrollAllowed ? 2u : 0u) |
                  (defect ? 4u : 0u);
        meta[7] = checked((uint)bonesPoolCount);
        meta[8] = checked((uint)catalog.OrdinaryRelics.Length);
        meta[9] = checked((uint)catalog.PlayerRelicBuckets.Length);
        meta[10] = checked((uint)catalog.SharedRelicConsumeShuffleLengths.Length);
        meta[11] = checked((uint)reward.Predicates.Length);
        WriteU64(meta, 12, NeowEventHash);
        WriteU64(meta, 14, RewardsStreamHash);
        WriteU64(meta, 16, NicheStreamHash);
        WriteU64(meta, 18, UpFrontStreamHash);
        meta[20] = checked((uint)Beta110GpuCombatRewardConstants.CapsuleWorkCapacity);
        meta[21] = checked((uint)catalog.OtherCharacterPools.Length);
        meta[22] = (uint)routePolicy;
        meta[23] = pinnedRelicId;
        meta[24] = requestedBonesFirstId != Beta110FastRelicCatalog.InvalidId &&
                   requestedBonesSecondId != Beta110FastRelicCatalog.InvalidId ? 1u : 0u;
        meta[25] = requestedBonesFirstId;
        meta[26] = requestedBonesSecondId;
        // 27/28 are reserved for bounded route-policy diagnostic instrumentation;
        // production keeps both at zero. The backend may clone this immutable
        // plan metadata for a short diagnostic run and set [27]=enabled,[28]=sample count.
        meta[27] = 0u;
        meta[28] = 0u;

        // P10 single-route hot-loop metadata. These are physical Fast execution
        // tables only; they do not carry route identity or Production evidence.
        uint hotFlags = 0u;
        if (hotLoop.Enabled) hotFlags |= 1u;
        if (hotLoop.DirectCardOrdinalSelectionEligible) hotFlags |= 2u;
        if (hotLoop.PotionIdentityRawBurnEligible) hotFlags |= 4u;
        if (hotLoop.PotionIdentityObservableRequired) hotFlags |= 8u;
        meta[29] = hotFlags;
        meta[30] = checked((uint)fixedHeader);
        meta[31] = checked((uint)hotLoop.CardStateCount);
        int potionDropOffset = fixedHeader + cardTableUInts;
        meta[32] = checked((uint)potionDropOffset);
        meta[33] = checked((uint)hotLoop.PotionDropNodeCount);
        // 37-39 carry Fast/Search-only query-literal Reward context. They are
        // never route evidence and Production Exact/Witness do not consume them.
        meta[37] = (uint)(ushort)reward.ExplicitContext.InfluenceFlags;
        meta[38] = reward.ExplicitContext.AdditionalCardRewardCount;
        meta[39] = unchecked((uint)(ushort)reward.ExplicitContext.FixedGoldAmount);
        // 40-43 carry the authored opening RNG-consumption replay. This is not
        // Neow result state: it only tells C which player-authored mechanics must
        // be replayed before Reward generation. MP partial Bones instead recovers
        // the actual root pair via [51..53]; Capsule latent obtains stay neutral.
        meta[40] = reward.OpeningConsumption.ReplayBonesOffer ? 1u : 0u;
        meta[41] = checked((uint)Math.Min(2, reward.OpeningConsumption.OrderedRelicIds.Length));
        meta[42] = reward.OpeningConsumption.OrderedRelicIds.Length > 0
            ? reward.OpeningConsumption.OrderedRelicIds[0]
            : Beta110FastRelicCatalog.InvalidId;
        meta[43] = reward.OpeningConsumption.OrderedRelicIds.Length > 1
            ? reward.OpeningConsumption.OrderedRelicIds[1]
            : Beta110FastRelicCatalog.InvalidId;
        // Immutable proven P1..P4 opening prefix; no per-candidate state or ABI1 payload.
        meta[44] = precedingNicheDraws.HasValue ? checked((uint)precedingNicheDraws.Value) : uint.MaxValue;
        for (int index = 0; index < 4; index++) meta[45 + index] = unchecked((uint)(capsuleNicheAdvances?[index] ?? 0));
        meta[49] = kaleidoscopeCountOnly ? 1u : 0u;
        meta[50] = conservativeOpening ? 1u : 0u;
        meta[51] = actualBonesPair ? 1u : 0u;
        meta[52] = checked((uint)bonesOffset);
        meta[53] = dynamicCapsuleNicheUnknown ? 1u : 0u;
        meta[54] = capsuleHeldReplay ? 1u : 0u;
        meta[55] = reward.CardAssignmentWindow;
        meta[56] = checked((uint)reward.CardAssignmentTargets.Length);
        meta[57] = checked((uint)cardAssignmentOffset);
        meta[58] = reward.PotionAssignmentWindow;
        meta[59] = checked((uint)reward.PotionAssignmentTargets.Length);
        meta[60] = checked((uint)potionAssignmentOffset);
        for (int index = 0; index < reward.CardAssignmentTargets.Length; index++)
            meta[cardAssignmentOffset + index] = reward.CardAssignmentTargets[index];
        for (int index = 0; index < reward.PotionAssignmentTargets.Length; index++)
        {
            meta[potionAssignmentOffset + index * 2] = reward.PotionAssignmentRequirements[index];
            meta[potionAssignmentOffset + index * 2 + 1] = reward.PotionAssignmentTargets[index];
        }
        if (bonesPool is not null)
            for (int index = 0; index < bonesPool.Length; index++) meta[bonesOffset + index] = bonesPool[index];
        int potionRarityOffset = potionDropOffset + potionDropUInts;
        meta[34] = checked((uint)potionRarityOffset);
        meta[35] = checked((uint)hotLoop.MaximumBattleOrdinal);
        meta[36] = checked((uint)hotLoop.PredicateCount);

        int cursor = fixedHeader;
        if (hotLoop.Enabled)
        {
            for (int state = 0; state < hotLoop.CardStateCount; state++)
            {
                WriteU64(meta, cursor, hotLoop.CardRareStrictCutoffs[state]); cursor += 2;
                WriteU64(meta, cursor, hotLoop.CardUncommonStrictCutoffs[state]); cursor += 2;
            }
            for (int node = 0; node < hotLoop.PotionDropNodeCount; node++)
            {
                WriteU64(meta, cursor, hotLoop.PotionDropStrictCutoffs[node]); cursor += 2;
            }
            WriteU64(meta, cursor, hotLoop.PotionRareInclusiveCutoff); cursor += 2;
            WriteU64(meta, cursor, hotLoop.PotionUncommonInclusiveCutoff); cursor += 2;
        }
        return meta;
    }

    internal static uint[] BuildPoolMeta(Beta110FastEffectCatalog catalog, out uint[] denseIds,
        IReadOnlySet<ushort>? explicitHeldIds = null)
    {
        int otherCount = catalog.OtherCharacterPools.Length;
        int bucketCount = catalog.PlayerRelicBuckets.Length;
        int ordinaryCount = catalog.OrdinaryRelics.Length;
        int otherBase = Beta110GpuCombatRewardConstants.PoolFixedUIntCount;
        int bucketBase = otherBase + otherCount * 6;
        int ordinaryBase = bucketBase + bucketCount * 2;
        int multiplayerBase = ordinaryBase + ordinaryCount * Beta110GpuCombatRewardConstants.OrdinaryDescriptorStrideUInts;
        var meta = new uint[multiplayerBase + 6];
        var values = new List<uint>(4096);

        void Append(IEnumerable<ushort> source, int offset)
        {
            ushort[] array = source.ToArray();
            meta[offset] = checked((uint)values.Count);
            meta[offset + 1] = checked((uint)array.Length);
            foreach (ushort value in array) values.Add(value);
        }
        void AppendCardPool(Beta110FastCardPool pool, int offset)
        {
            Append(pool.Common, offset);
            Append(pool.Uncommon, offset + 2);
            Append(pool.Rare, offset + 4);
        }

        AppendCardPool(catalog.CharacterRewardPool, 0);
        AppendCardPool(catalog.ColorlessRewardPool, 6);
        Append(catalog.PotionPool.Common, 12);
        Append(catalog.PotionPool.Uncommon, 14);
        Append(catalog.PotionPool.Rare, 16);
        Append(catalog.PotionPool.AllAllowed, 18);
        AppendCardPool(catalog.CombatRewardCardPool, 20);
        AppendCardPool(catalog.CombatRewardPowerPool, 26);
        Append(catalog.CombatRewardPotionPool.Common, 32);
        Append(catalog.CombatRewardPotionPool.Uncommon, 34);
        Append(catalog.CombatRewardPotionPool.Rare, 36);
        Append(catalog.CombatRewardPotionPool.AllAllowed, 38);
        meta[40] = checked((uint)otherCount);
        meta[41] = checked((uint)values.Count);
        meta[42] = checked((uint)catalog.SharedRelicConsumeShuffleLengths.Length);
        foreach (int length in catalog.SharedRelicConsumeShuffleLengths) values.Add(checked((uint)length));
        meta[43] = checked((uint)bucketCount);
        meta[44] = checked((uint)ordinaryCount);
        meta[45] = checked((uint)bucketBase);
        meta[46] = checked((uint)ordinaryBase);
        meta[47] = checked((uint)multiplayerBase);
        AppendCardPool(catalog.MultiplayerRewardPool, multiplayerBase);

        for (int index = 0; index < otherCount; index++)
            AppendCardPool(catalog.OtherCharacterPools[index], otherBase + index * 6);

        for (int index = 0; index < bucketCount; index++)
        {
            Beta110FastRelicBucket bucket = catalog.PlayerRelicBuckets[index];
            meta[bucketBase + index * 2] = checked((uint)values.Count);
            meta[bucketBase + index * 2 + 1] = checked((uint)bucket.RelicIndexes.Length);
            foreach (ushort relicIndex in bucket.RelicIndexes) values.Add(relicIndex);
        }

        for (int index = 0; index < ordinaryCount; index++)
        {
            Beta110FastOrdinaryRelic relic = catalog.OrdinaryRelics[index];
            int offset = ordinaryBase + index * Beta110GpuCombatRewardConstants.OrdinaryDescriptorStrideUInts;
            meta[offset] = relic.DenseId;
            // GPU rarity encoding is 1=Common, 2=Uncommon, 3=Rare, 4=Shop.
            // EffectRelicRarity is zero-based in C#, so the packed ABI must shift it.
            meta[offset + 1] = checked((uint)relic.Rarity + 1u);
            meta[offset + 2] = PackCapability(relic.RewardCapability) |
                (explicitHeldIds?.Contains(relic.DenseId) == true ? 4u : 0u);
            meta[offset + 3] = unchecked((uint)(ushort)relic.RewardCapability.FixedGoldAmount);
        }

        denseIds = values.ToArray();
        return meta;
    }

    private static uint PackCapability(Beta110FastRewardRelicCapability capability) =>
        (capability.ContinuationSupported ? 1u : 0u) |
        (capability.InfluenceSupported ? 2u : 0u) |
        ((uint)capability.InfluenceFlags << 8) |
        ((uint)capability.AdditionalCardRewardCount << 24);

    internal static uint[] BuildPredicates(
        Beta110CombatRewardFastPlan plan,
        out uint[] targets)
    {
        var meta = new uint[plan.Predicates.Length * Beta110GpuCombatRewardConstants.PredicateStrideUInts];
        var values = new List<uint>();
        for (int index = 0; index < plan.Predicates.Length; index++)
        {
            Beta110CombatRewardFastPredicate predicate = plan.Predicates[index];
            int offset = index * Beta110GpuCombatRewardConstants.PredicateStrideUInts;
            meta[offset] = predicate.BattleOrdinal;
            meta[offset + 1] = (uint)predicate.PotionRequirement;
            Append(predicate.CardAny, offset + 2);
            Append(predicate.CardAll, offset + 4);
            Append(predicate.CardBan, offset + 6);
            Append(predicate.PotionAny, offset + 8);
            Append(predicate.PotionAll, offset + 10);
            Append(predicate.PotionBan, offset + 12);
            meta[offset + 14] = unchecked((uint)(predicate.HasMinimumGold ? predicate.MinimumGold : int.MinValue));
            meta[offset + 15] = unchecked((uint)(predicate.HasMaximumGold ? predicate.MaximumGold : int.MaxValue));
        }
        targets = values.ToArray();
        return meta;

        void Append(ushort[] source, int offset)
        {
            meta[offset] = checked((uint)values.Count);
            meta[offset + 1] = checked((uint)source.Length);
            foreach (ushort value in source) values.Add(value);
        }
    }

    private static void WriteU64(uint[] output, int offset, ulong value)
    {
        output[offset] = unchecked((uint)value);
        output[offset + 1] = unchecked((uint)(value >> 32));
    }
}
