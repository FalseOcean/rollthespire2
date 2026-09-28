using System.Runtime.CompilerServices;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// C-owned query-literal opening Rewards consumption. Reuses the established numeric
/// draws and pool rules; no complete Neow executor or route planner is constructed.
/// </summary>
internal static class CombatRewardOpeningReplay
{

    internal static bool TryReplayQueryLiteralRelicRewardsConsumption(
        byte relicId, Beta110FastEffectCatalog catalog, int ascension, bool usesDefectScrollBoxesRule,
        ref Beta110FastRng rewards, ref Beta110FastRng niche, bool requireAuthority = true, bool multiplayer = false,
        bool nicheKnown = true)
    {
        // Capsule nested identity is deliberately ignored here. The authored
        // Capsule itself consumes one Rewards rarity roll per contained relic;
        // nested relic semantics are supplied only by ExplicitRewardContext.
        if (relicId == Beta110FastRelicCatalog.LargeCapsule)
        {
            _ = rewards.NextFloat();
            _ = rewards.NextFloat();
            return true;
        }
        if (relicId == Beta110FastRelicCatalog.SmallCapsule)
        {
            _ = rewards.NextFloat();
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.MassiveScroll && multiplayer)
        {
            Span<ushort> offer = stackalloc ushort[3];
            return GenerateNormalCardOffer(catalog.MultiplayerRewardPool, ascension, ref rewards, offer);
        }

        if (relicId == Beta110FastRelicCatalog.ArcaneScroll)
        {
            if (requireAuthority && !catalog.CharacterRewardAuthorityExact) return false;
            return SelectUnused(catalog.CharacterRewardPool.Rare, ReadOnlySpan<ushort>.Empty, ref rewards) !=
                   Beta110FastDenseId.Invalid;
        }
        if (relicId == Beta110FastRelicCatalog.HeftyTablet)
        {
            if ((requireAuthority && !catalog.CharacterRewardAuthorityExact) || catalog.CharacterRewardPool.Rare.Length < 3) return false;
            Span<ushort> offer = stackalloc ushort[3];
            return GenerateForcedRareOffer(catalog.CharacterRewardPool, ref rewards, offer);
        }
        if (relicId == Beta110FastRelicCatalog.LeadPaperweight)
        {
            if (requireAuthority && !catalog.ColorlessRewardAuthorityExact) return false;
            Span<ushort> offer = stackalloc ushort[2];
            return GenerateNormalCardOffer(catalog.ColorlessRewardPool, ascension, ref rewards, offer);
        }
        if (relicId == Beta110FastRelicCatalog.LostCoffer)
        {
            if (requireAuthority && (!catalog.CharacterRewardAuthorityExact || !catalog.PotionAuthorityExact)) return false;
            Span<ushort> cards = stackalloc ushort[3];
            if (!GenerateNormalCardOffer(catalog.CharacterRewardPool, ascension, ref rewards, cards))
                return false;
            return RollPotion(catalog.PotionPool, ReadOnlySpan<ushort>.Empty, ref rewards) !=
                   Beta110FastDenseId.Invalid;
        }
        if (relicId == Beta110FastRelicCatalog.Kaleidoscope)
        {
            if ((!requireAuthority || catalog.OtherCharacterCardAuthorityExact) && catalog.OtherCharacterPools.Length >= 3 &&
                catalog.OtherCharacterPools.All(p => p.TotalCount > 0))
            {
                // Each selected pool generates one card with a fresh exclusion set:
                // rarity + successful identity + upgrade, even with rarity fallback.
                // The six-card Rewards continuation is independent of the Niche permutation.
                for (int group = 0; group < 2; group++)
                {
                    if (nicheKnown) niche.ConsumeUnstableShuffle(catalog.OtherCharacterPools.Length);
                    for (int draw = 0; draw < 9; draw++) _ = rewards.NextDouble();
                }
                return true;
            }
            if (!nicheKnown) return false;
            Span<ushort> offers = stackalloc ushort[6];
            offers.Fill(Beta110FastDenseId.Invalid);
            return GenerateKaleidoscopeOffers(ascension, catalog, ref rewards, ref niche, offers, requireAuthority);
        }
        if (relicId == Beta110FastRelicCatalog.ScrollBoxes)
        {
            Span<ushort> cards = stackalloc ushort[6];
            Span<byte> bundleSizes = stackalloc byte[2];
            Span<byte> clawBundles = stackalloc byte[2];
            cards.Fill(Beta110FastDenseId.Invalid);
            bundleSizes.Clear();
            clawBundles.Clear();
            return GenerateScrollBoxes(
                catalog, usesDefectScrollBoxesRule, ref rewards, cards, bundleSizes, clawBundles, requireAuthority);
        }
        if (relicId == Beta110FastRelicCatalog.NewLeaf && nicheKnown) _ = niche.NextDouble();

        // Relics whose opening behavior uses only other RNG streams, player choice,
        // deck mutation, or no RNG do not change the Rewards continuation consumed by C.
        return true;
    }

    private static bool GenerateForcedRareOffer(
        Beta110FastCardPool pool,
        ref Beta110FastRng rewards,
        Span<ushort> output)
    {
        if (pool.Rare.Length < output.Length) return false;
        Span<ushort> used = stackalloc ushort[output.Length];
        int usedCount = 0;
        for (int index = 0; index < output.Length; index++)
        {
            ushort selected = SelectUnused(pool.Rare, used[..usedCount], ref rewards);
            if (selected == Beta110FastDenseId.Invalid) return false;
            output[index] = selected;
            used[usedCount++] = selected;
        }
        return true;
    }

    private static bool GenerateNormalCardOffer(
        Beta110FastCardPool pool,
        int ascension,
        ref Beta110FastRng rewards,
        Span<ushort> output)
    {
        Span<ushort> used = stackalloc ushort[output.Length];
        int usedCount = 0;
        for (int index = 0; index < output.Length; index++)
        {
            ushort selected = RollCard(pool, ascension, used[..usedCount], ref rewards);
            if (selected == Beta110FastDenseId.Invalid) return false;
            output[index] = selected;
            used[usedCount++] = selected;
        }
        return true;
    }

    private static ushort RollPotion(
        Beta110FastPotionPool pool,
        ReadOnlySpan<ushort> used,
        ref Beta110FastRng rng)
    {
        float roll = rng.NextFloat();
        ReadOnlySpan<ushort> requested = roll <= 0.1f
            ? pool.Rare
            : roll <= 0.35f ? pool.Uncommon : pool.Common;
        return SelectUnused(requested, used, ref rng);
    }

    private static bool GenerateKaleidoscopeOffers(
        int ascension,
        Beta110FastEffectCatalog catalog,
        ref Beta110FastRng rewards,
        ref Beta110FastRng niche,
        Span<ushort> offers,
        bool requireAuthority = true)
    {
        int poolCount = catalog.OtherCharacterPools.Length;
        if ((requireAuthority && !catalog.OtherCharacterCardAuthorityExact) || poolCount < 3 || offers.Length < 6) return false;
        Span<ushort> poolOrder = stackalloc ushort[poolCount];
        for (ushort index = 0; index < poolOrder.Length; index++) poolOrder[index] = index;

        for (int group = 0; group < 2; group++)
        {
            for (ushort index = 0; index < poolOrder.Length; index++) poolOrder[index] = index;
            niche.UnstableShuffle(poolOrder);
            for (int item = 0; item < 3; item++)
            {
                ushort poolIndex = poolOrder[item];
                if (poolIndex >= catalog.OtherCharacterPools.Length) return false;
                ushort card = RollCard(catalog.OtherCharacterPools[poolIndex], ascension, ref rewards);
                if (card == Beta110FastDenseId.Invalid) return false;
                offers[group * 3 + item] = card;
            }
        }
        return true;
    }

    private static ushort RollCard(
        Beta110FastCardPool pool,
        int ascension,
        ref Beta110FastRng rewards) =>
        RollCard(pool, ascension, ReadOnlySpan<ushort>.Empty, ref rewards);

    private static ushort RollCard(
        Beta110FastCardPool pool,
        int ascension,
        ReadOnlySpan<ushort> used,
        ref Beta110FastRng rewards)
    {
        float rare = ascension >= 7 ? 0.0149f : 0.03f;
        float roll = rewards.NextFloat();
        byte requested = roll < rare ? (byte)3 : roll < rare + 0.37f ? (byte)2 : (byte)1;
        for (int fallbackIndex = 0; fallbackIndex < 3; fallbackIndex++)
        {
            ReadOnlySpan<ushort> candidates = SelectCardRarityPool(pool, requested, fallbackIndex);
            ushort selected = SelectUnused(candidates, used, ref rewards);
            if (selected == Beta110FastDenseId.Invalid) continue;
            _ = rewards.NextFloat(); // Game source consumes Rewards.NextFloat for the Neow upgrade roll.
            return selected;
        }
        return Beta110FastDenseId.Invalid;
    }

    private static ushort[] SelectCardRarityPool(Beta110FastCardPool pool, byte requested, int fallbackIndex)
    {
        byte rarity = requested switch
        {
            1 => fallbackIndex switch { 0 => (byte)1, 1 => (byte)2, _ => (byte)3 },
            2 => fallbackIndex switch { 0 => (byte)2, 1 => (byte)3, _ => (byte)1 },
            _ => fallbackIndex switch { 0 => (byte)3, 1 => (byte)1, _ => (byte)2 }
        };
        return rarity switch { 1 => pool.Common, 2 => pool.Uncommon, _ => pool.Rare };
    }

    private static bool GenerateScrollBoxes(
        Beta110FastEffectCatalog catalog,
        bool usesDefectRule,
        ref Beta110FastRng rewards,
        Span<ushort> cards,
        Span<byte> bundleSizes,
        Span<byte> clawBundles,
        bool requireAuthority = true)
    {
        if (requireAuthority && !catalog.CharacterCardAuthorityExact) return false;
        Span<ushort> used = stackalloc ushort[6];
        used.Fill(Beta110FastDenseId.Invalid);
        int usedCount = 0;
        for (int bundle = 0; bundle < 2; bundle++)
        {
            bool claw = usesDefectRule && rewards.NextInt(100) < 1;
            if (claw)
            {
                cards[bundle * 3] = catalog.ClawId;
                bundleSizes[bundle] = 1;
                clawBundles[bundle] = 1;
                continue;
            }

            for (int index = 0; index < 2; index++)
            {
                ushort selected = SelectUnused(catalog.CharacterRewardPool.Common, used[..usedCount], ref rewards);
                if (selected == Beta110FastDenseId.Invalid) return false;
                cards[bundle * 3 + index] = selected;
                used[usedCount++] = selected;
            }
            ushort uncommon = SelectUnused(catalog.CharacterRewardPool.Uncommon, used[..usedCount], ref rewards);
            if (uncommon == Beta110FastDenseId.Invalid) return false;
            cards[bundle * 3 + 2] = uncommon;
            used[usedCount++] = uncommon;
            bundleSizes[bundle] = 3;
        }
        return true;
    }

    private static ushort SelectUnused(
        ReadOnlySpan<ushort> source,
        ReadOnlySpan<ushort> used,
        ref Beta110FastRng rewards)
    {
        int available = 0;
        foreach (ushort candidate in source)
        {
            if (!Contains(used, candidate)) available++;
        }
        if (available == 0) return Beta110FastDenseId.Invalid;
        int selectedIndex = rewards.NextInt(available);
        foreach (ushort candidate in source)
        {
            if (Contains(used, candidate)) continue;
            if (selectedIndex-- == 0) return candidate;
        }
        return Beta110FastDenseId.Invalid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Contains(ReadOnlySpan<ushort> values, ushort target)
    {
        foreach (ushort value in values)
        {
            if (value == target) return true;
        }
        return false;
    }
}
