using System.Runtime.CompilerServices;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Search.FamilyExecution;
namespace RolltheSpire2.Search.FamilyExecution;

// Numeric operators adopted from Beta110UnifiedLossyCandidateKernel. Capsule
// consumes only its local Rewards draws here. No bag or relic state is accepted.
internal static class NeowLocalOperators
{
    internal static bool IsPositiveAllowed(byte id, byte curse, Beta110FastNeowAuthority authority)
    {
        if (id == Beta110FastRelicCatalog.MassiveScroll && authority.PlayersCount <= 1) return false;
        if (id == Beta110FastRelicCatalog.Kaleidoscope && !authority.AllCharacterCardPoolsUnlocked) return false;
        if (id == Beta110FastRelicCatalog.ScrollBoxes && !authority.ScrollBoxesAllowed) return false;
        if (id == Beta110FastRelicCatalog.WingedBoots && authority.PlayersCount != 1) return false;
        if (curse == Beta110FastRelicCatalog.CursedPearl && id == Beta110FastRelicCatalog.GoldenPearl) return false;
        if (curse == Beta110FastRelicCatalog.HeftyTablet && id == Beta110FastRelicCatalog.ArcaneScroll) return false;
        if (curse == Beta110FastRelicCatalog.LeafyPoultice && id == Beta110FastRelicCatalog.NewLeaf) return false;
        if (curse == Beta110FastRelicCatalog.PrecariousShears && id == Beta110FastRelicCatalog.PreciseScissors) return false;
        // Preserve the audited top-level Neow three-choice pool eligibility rule.
        // This is not the Bones acquisition potion-capacity policy: once Sacrifice
        // and Lost Coffer/Phial Holster are already offered inside Bones, Fast does
        // not create a potion-slot failure branch and treats Sacrifice as a
        // non-blocking identity companion.
        if (curse == Beta110FastRelicCatalog.NeowsSacrifice &&
            (id == Beta110FastRelicCatalog.PhialHolster || id == Beta110FastRelicCatalog.LostCoffer)) return false;
        return true;
    }

    internal static bool ExecuteRelic(
        byte relicId,
        bool isActive,
        NeowReplayPlan plan,
        Beta110FastEffectCatalog catalog,
        bool needsRewards,
        bool needsNiche,
        bool needsTransformations,
        bool needsCombatPotionGeneration,
        ref Beta110FastRng rewards,
        ref Beta110FastRng niche,
        ref Beta110FastRng transformations,
        ref Beta110FastRng combatPotionGeneration,
        Span<byte> matched)
    {
        if (!isActive)
            return true;

        if (relicId == Beta110FastRelicCatalog.LargeCapsule || relicId == Beta110FastRelicCatalog.SmallCapsule)
        {
            if (needsRewards) {
                rewards.NextFloat();
                if (relicId == Beta110FastRelicCatalog.LargeCapsule) rewards.NextFloat();
            }
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.ArcaneScroll)
        {
            if (!needsRewards) return true;
            if (!catalog.CharacterRewardAuthorityExact) return false;
            ushort card = SelectUnused(catalog.CharacterRewardPool.Rare, ReadOnlySpan<ushort>.Empty, ref rewards);
            if (card == Beta110FastDenseId.Invalid) return false;
            MatchSingleOfferCondition(relicId, Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard,
                plan.StructuredConditions, card, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.HeftyTablet)
        {
            if (!needsRewards) return true;
            if (!catalog.CharacterRewardAuthorityExact || catalog.CharacterRewardPool.Rare.Length < 3) return false;
            Span<ushort> offer = stackalloc ushort[3];
            if (!GenerateForcedRareOffer(catalog.CharacterRewardPool, ref rewards, offer)) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.HeftyTabletRareOffer,
                plan.StructuredConditions, offer, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.LeadPaperweight)
        {
            if (!needsRewards) return true;
            if (!catalog.ColorlessRewardAuthorityExact) return false;
            Span<ushort> offer = stackalloc ushort[2];
            if (!GenerateNormalCardOffer(catalog.ColorlessRewardPool, plan.Authority.Ascension, ref rewards, offer)) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.LeadPaperweightColorlessOffer,
                plan.StructuredConditions, offer, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.LostCoffer)
        {
            if (!needsRewards) return true;
            if (!catalog.CharacterRewardAuthorityExact || !catalog.PotionAuthorityExact) return false;
            Span<ushort> cards = stackalloc ushort[3];
            if (!GenerateNormalCardOffer(catalog.CharacterRewardPool, plan.Authority.Ascension, ref rewards, cards)) return false;
            ushort potion = RollPotion(catalog.PotionPool, ReadOnlySpan<ushort>.Empty, ref rewards);
            if (potion == Beta110FastDenseId.Invalid) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.LostCofferCardOffer,
                plan.StructuredConditions, cards, matched);
            MatchSingleOfferCondition(relicId, Beta110FastStructuredConditionKind.LostCofferPotion,
                plan.StructuredConditions, potion, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.Kaleidoscope)
        {
            if (!needsNiche && !needsRewards) return true;
            bool hasCondition = HasStructuredConditionForRelic(plan.StructuredConditions, relicId);
            if (!needsRewards)
            {
                int poolCount = catalog.OtherCharacterPools.Length;
                if (!catalog.OtherCharacterCardAuthorityExact || poolCount < 3) return false;
                niche.ConsumeUnstableShuffle(poolCount);
                niche.ConsumeUnstableShuffle(poolCount);
                return true;
            }
            Span<ushort> offers = stackalloc ushort[6];
            offers.Fill(Beta110FastDenseId.Invalid);
            if (!GenerateKaleidoscopeOffers(plan.Authority.Ascension, catalog, ref rewards, ref niche, offers)) return false;
            if (hasCondition) MatchKaleidoscopeConditions(plan.StructuredConditions, offers, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.ScrollBoxes)
        {
            if (!needsRewards) return true;
            Span<ushort> cards = stackalloc ushort[6];
            cards.Fill(Beta110FastDenseId.Invalid);
            Span<byte> bundleSizes = stackalloc byte[2];
            Span<byte> clawBundles = stackalloc byte[2];
            bundleSizes.Clear();
            clawBundles.Clear();
            if (!GenerateScrollBoxes(
                    catalog,
                    plan.Authority.UsesDefectScrollBoxesRule,
                    ref rewards,
                    cards,
                    bundleSizes,
                    clawBundles)) return false;
            MatchScrollBoxesConditions(plan.StructuredConditions, cards, bundleSizes, clawBundles, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.PhialHolster)
        {
            if (!needsCombatPotionGeneration) return true;
            if (!catalog.PotionAuthorityExact) return false;
            Span<ushort> potions = stackalloc ushort[2];
            potions[0] = RollPotion(catalog.PotionPool, ReadOnlySpan<ushort>.Empty, ref combatPotionGeneration);
            if (potions[0] == Beta110FastDenseId.Invalid) return false;
            potions[1] = RollPotion(catalog.PotionPool, potions[..1], ref combatPotionGeneration);
            if (potions[1] == Beta110FastDenseId.Invalid) return false;
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.PhialHolsterPotions,
                plan.StructuredConditions, potions, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.LeafyPoultice)
        {
            if (!needsTransformations) return true;
            if (!catalog.LeafyTransformAuthorityExact ||
                catalog.LeafyStrikeTransformPool.Length == 0 || catalog.LeafyDefendTransformPool.Length == 0) return false;
            Span<ushort> transforms = stackalloc ushort[2];
            transforms[0] = catalog.LeafyStrikeTransformPool[transformations.NextInt(catalog.LeafyStrikeTransformPool.Length)];
            transforms[1] = catalog.LeafyDefendTransformPool[transformations.NextInt(catalog.LeafyDefendTransformPool.Length)];
            MatchOfferConditions(relicId, Beta110FastStructuredConditionKind.LeafyPoulticeTransforms,
                plan.StructuredConditions, transforms, matched);
            return true;
        }

        if (relicId == Beta110FastRelicCatalog.NewLeaf)
        {
            if (!needsNiche) return true;
            if (!catalog.NewLeafTransformAuthorityExact || catalog.NewLeafTransformPool.Length == 0) return false;
            ushort replacement = catalog.NewLeafTransformPool[niche.NextInt(catalog.NewLeafTransformPool.Length)];
            if ((plan.EnabledDomains & Beta110FastDomain.NewLeafTransform) != 0)
            {
                MatchSingleOfferCondition(relicId, Beta110FastStructuredConditionKind.NewLeafTransform,
                    plan.StructuredConditions, replacement, matched);
            }
            return true;
        }

        // Pure player-choice/deck mutation relics and deterministic no-RNG
        // effects are deliberate continuation no-ops in the Fast RNG surface.
        return true;
    }

    internal static bool RequiredStructuredSourcesPresent(
        ReadOnlySpan<Beta110FastStructuredCondition> conditions,
        byte firstRelic,
        byte secondRelic)
    {
        for (int index = 0; index < conditions.Length; index++)
        {
            byte source = conditions[index].SourceRelicId;
            if (source != firstRelic && source != secondRelic)
                return false;
        }
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

    private static void MatchSingleOfferCondition(
        byte relicId,
        Beta110FastStructuredConditionKind kind,
        ReadOnlySpan<Beta110FastStructuredCondition> conditions,
        ushort output,
        Span<byte> matched)
    {
        for (int index = 0; index < conditions.Length; index++)
        {
            Beta110FastStructuredCondition condition = conditions[index];
            if (condition.SourceRelicId == relicId && condition.Kind == kind &&
                condition.TargetCount == 1 && condition.Target0 == output)
            {
                matched[index] = 1;
            }
        }
    }

    private static void MatchOfferConditions(
        byte relicId,
        Beta110FastStructuredConditionKind kind,
        ReadOnlySpan<Beta110FastStructuredCondition> conditions,
        ReadOnlySpan<ushort> outputs,
        Span<byte> matched)
    {
        for (int index = 0; index < conditions.Length; index++)
        {
            Beta110FastStructuredCondition condition = conditions[index];
            if (condition.SourceRelicId != relicId || condition.Kind != kind) continue;
            if (ContainsTargets(outputs, condition)) matched[index] = 1;
        }
    }

    private static bool GenerateKaleidoscopeOffers(
        int ascension,
        Beta110FastEffectCatalog catalog,
        ref Beta110FastRng rewards,
        ref Beta110FastRng niche,
        Span<ushort> offers)
    {
        int poolCount = catalog.OtherCharacterPools.Length;
        if (!catalog.OtherCharacterCardAuthorityExact || poolCount < 3 || offers.Length < 6) return false;
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
        Span<byte> clawBundles)
    {
        if (!catalog.CharacterCardAuthorityExact) return false;
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

    private static void MatchKaleidoscopeConditions(
        ReadOnlySpan<Beta110FastStructuredCondition> conditions,
        ReadOnlySpan<ushort> offers,
        Span<byte> matched)
    {
        ReadOnlySpan<ushort> first = offers[..3];
        ReadOnlySpan<ushort> second = offers.Slice(3, 3);
        for (int index = 0; index < conditions.Length; index++)
        {
            Beta110FastStructuredCondition condition = conditions[index];
            if (condition.SourceRelicId != Beta110FastRelicCatalog.Kaleidoscope ||
                condition.Kind != Beta110FastStructuredConditionKind.KaleidoscopeIndependentOfferTargets)
                continue;
            bool match = condition.TargetCount == 1
                ? Contains(first, condition.Target0) || Contains(second, condition.Target0)
                : (Contains(first, condition.Target0) && Contains(second, condition.Target1)) ||
                  (Contains(first, condition.Target1) && Contains(second, condition.Target0));
            if (match) matched[index] = 1;
        }
    }

    private static void MatchScrollBoxesConditions(
        ReadOnlySpan<Beta110FastStructuredCondition> conditions,
        ReadOnlySpan<ushort> cards,
        ReadOnlySpan<byte> bundleSizes,
        ReadOnlySpan<byte> clawBundles,
        Span<byte> matched)
    {
        for (int index = 0; index < conditions.Length; index++)
        {
            Beta110FastStructuredCondition condition = conditions[index];
            if (condition.SourceRelicId != Beta110FastRelicCatalog.ScrollBoxes) continue;
            for (int bundle = 0; bundle < 2; bundle++)
            {
                if (condition.Kind == Beta110FastStructuredConditionKind.ScrollBoxesTripleClaw)
                {
                    if (clawBundles[bundle] != 0) matched[index] = 1;
                    continue;
                }
                if (condition.Kind != Beta110FastStructuredConditionKind.ScrollBoxesCardComposition ||
                    bundleSizes[bundle] != 3)
                    continue;
                if (ContainsTargets(cards.Slice(bundle * 3, 3), condition)) matched[index] = 1;
            }
        }
    }

    private static bool ContainsTargets(
        ReadOnlySpan<ushort> actual,
        Beta110FastStructuredCondition condition)
    {
        Span<byte> consumed = stackalloc byte[actual.Length];
        consumed.Clear();
        for (int targetIndex = 0; targetIndex < condition.TargetCount; targetIndex++)
        {
            ushort target = condition.TargetAt(targetIndex);
            bool found = false;
            for (int actualIndex = 0; actualIndex < actual.Length; actualIndex++)
            {
                if (consumed[actualIndex] != 0 || actual[actualIndex] != target) continue;
                consumed[actualIndex] = 1;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static bool HasStructuredConditionForRelic(
        ReadOnlySpan<Beta110FastStructuredCondition> conditions,
        byte relicId)
    {
        foreach (Beta110FastStructuredCondition condition in conditions)
        {
            if (condition.SourceRelicId == relicId) return true;
        }
        return false;
    }

    private static bool Contains(ReadOnlySpan<ushort> values, ushort target)
    {
        foreach (ushort value in values)
        {
            if (value == target) return true;
        }
        return false;
    }

}
