namespace RolltheSpire2.Search.FamilyExecution;

// Optional local numeric data. The base condition rows and pool buffers remain
// available for continuations and unqualified captures.
internal static class NeowLocalObservationPacking
{
    internal const int KaleidoscopeOffset = 14;
    internal const int UniquePools = 15;
    internal const uint Character = 1;
    internal const uint Colorless = 2;
    internal const uint Potion = 4;
    internal const uint Transforms = 8;
    internal const uint Kaleidoscope = 16;
    internal const uint BurnCharacter = 32;
    internal const uint BurnColorless = 64;
    internal const uint BurnPotion = 128;
    internal const uint BurnScroll = 256;
    internal const uint BurnLostPotion = 512;
    internal const uint Arcane = 1024;
    internal const uint Massive = 2048;
    internal const uint BurnKaleidoscope = 4096;
    internal const string Revision = ".LocalObservations.20261114.v1";

    internal static uint[] Apply(NeowReplayPlan plan, ReadOnlySpan<byte> sources, uint[] meta, uint[] conditions)
    {
        string? observation = Environment.GetEnvironmentVariable("RT2_N_OBSERVE_LOCAL");
        if (!plan.Authority.UsesChildShuffle || !plan.AuditedModern113IdentityReplay ||
            string.Equals(observation, "off", StringComparison.OrdinalIgnoreCase))
            return conditions;
        var c = plan.Authority.EffectCatalog;
        if (plan.NeedsRewards && (sources.Contains(Beta110FastRelicCatalog.HeftyTablet) || sources.Contains(Beta110FastRelicCatalog.ScrollBoxes) || sources.Contains(Beta110FastRelicCatalog.LostCoffer)) &&
            Unique(c.CharacterRewardPool.Common, c.CharacterRewardPool.Uncommon, c.CharacterRewardPool.Rare))
        {
            meta[UniquePools] |= Character;
            if (c.CharacterRewardPool.TotalCount >= 3) meta[UniquePools] |= BurnCharacter;
            if (c.CharacterRewardPool.Common.Length >= 4 && c.CharacterRewardPool.Uncommon.Length >= 2)
                meta[UniquePools] |= BurnScroll;
        }
        if (plan.NeedsRewards && sources.Contains(Beta110FastRelicCatalog.LeadPaperweight) &&
            Unique(c.ColorlessRewardPool.Common, c.ColorlessRewardPool.Uncommon, c.ColorlessRewardPool.Rare))
        {
            meta[UniquePools] |= Colorless;
            if (c.ColorlessRewardPool.TotalCount >= 2) meta[UniquePools] |= BurnColorless;
        }
        if ((sources.Contains(Beta110FastRelicCatalog.PhialHolster) && plan.NeedsPotions || sources.Contains(Beta110FastRelicCatalog.LostCoffer) && plan.NeedsRewards) &&
            Unique(c.PotionPool.Common, c.PotionPool.Uncommon, c.PotionPool.Rare))
        {
            meta[UniquePools] |= Potion;
            if (c.PotionPool.HasAtLeastPerRarity(2)) meta[UniquePools] |= BurnPotion;
            if (c.PotionPool.HasAtLeastPerRarity(1)) meta[UniquePools] |= BurnLostPotion;
        }
        if (plan.NeedsTransformations && (sources.Contains(Beta110FastRelicCatalog.LeafyPoultice) || sources.Contains(Beta110FastRelicCatalog.NewLeaf)))
            meta[UniquePools] |= Transforms;
        if (plan.NeedsRewards && sources.Contains(Beta110FastRelicCatalog.ArcaneScroll) && c.CharacterRewardPool.Rare.Length > 0 &&
            !c.CharacterRewardPool.Rare.Contains(Beta110FastDenseId.Invalid))
            meta[UniquePools] |= Arcane;

        int k = Beta110FastRelicCatalog.Kaleidoscope * 5;
        if (!sources.Contains(Beta110FastRelicCatalog.Kaleidoscope) || !plan.NeedsRewards || c.OtherCharacterPools.Length < 3 ||
            c.OtherCharacterPools.Any(pool => pool.TotalCount == 0)) return conditions;
        if ((conditions[k + 4] & 0x80000000u) == 0)
        {
            meta[UniquePools] |= BurnKaleidoscope;
            return conditions;
        }
        // The direct rank table raised cold setup cost without a repeatable
        // warm gain. Keep it available for measured comparisons.
        if (!plan.Bones && !string.Equals(observation, "on", StringComparison.OrdinalIgnoreCase)) return conditions;
        uint target0 = conditions[k + 1], target1 = conditions[k + 2];
        var ranks = new List<uint>(c.OtherCharacterPools.Length * 9);
        foreach (var pool in c.OtherCharacterPools)
        {
            ushort[][] lanes = [pool.Common, pool.Uncommon, pool.Rare];
            if (!Unique(lanes) || pool.TotalCount == 0) return conditions;
            for (int rarity = 0; rarity < 3; rarity++)
            {
                // Same rarity fallbacks as rarity_slice, preserving source order.
                var effective = lanes[rarity];
                for (int i = 1; i <= 2 && effective.Length == 0; i++) effective = lanes[(rarity + i) % 3];
                static uint Rank(ushort[] lane, uint target) => target > ushort.MaxValue
                    ? uint.MaxValue : unchecked((uint)Array.IndexOf(lane, (ushort)target));
                ranks.AddRange([(uint)effective.Length, Rank(effective, target0), Rank(effective, target1)]);
            }
        }
        meta[KaleidoscopeOffset] = (uint)conditions.Length;
        meta[UniquePools] |= Kaleidoscope;
        return [..conditions, ..ranks];
    }

    internal static bool Unique(params ushort[][] lanes)
    {
        var ids = new HashSet<ushort>();
        return lanes.All(lane => lane.All(id => id != Beta110FastDenseId.Invalid && ids.Add(id)));
    }
}
