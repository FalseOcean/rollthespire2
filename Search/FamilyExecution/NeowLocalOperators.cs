using System.Runtime.CompilerServices;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Search.FamilyExecution;
namespace RolltheSpire2.Search.FamilyExecution;

// Numeric operators adopted from Beta110UnifiedLossyCandidateKernel. Capsule
// consumes only its local Rewards draws here. No bag or relic state is accepted.
internal static class NeowLocalOperators
{
    internal static bool IsPositiveAllowed(byte id, byte curse, Beta110FastNeowAuthority authority) =>
        authority.PlayersCount == 1 ? NeowSingleplayerOperators.IsPositiveAllowed(id, curse, authority)
            : NeowPartyOperators.IsPositiveAllowed(id, curse, authority);

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
        return plan.Authority.PlayersCount == 1
            ? NeowSingleplayerOperators.ExecuteRelic(relicId, isActive, plan, catalog, needsRewards, needsNiche,
                needsTransformations, needsCombatPotionGeneration, ref rewards, ref niche, ref transformations, ref combatPotionGeneration, matched)
            : NeowPartyOperators.ExecuteRelic(relicId, isActive, plan, catalog, needsRewards, needsNiche,
                needsTransformations, needsCombatPotionGeneration, ref rewards, ref niche, ref transformations, ref combatPotionGeneration, matched);
    }

    // Shared bounded numerical donors; pool authority remains compiled by N.
    internal static void DrawLeafyTransforms(Beta110FastEffectCatalog catalog, ref Beta110FastRng rng, Span<ushort> output)
    {
        output[0] = catalog.LeafyStrikeTransformPool[rng.NextInt(catalog.LeafyStrikeTransformPool.Length)];
        output[1] = catalog.LeafyDefendTransformPool[rng.NextInt(catalog.LeafyDefendTransformPool.Length)];
    }
    internal static ushort DrawNewLeafTransform(Beta110FastEffectCatalog catalog, ref Beta110FastRng rng) =>
        catalog.NewLeafTransformPool[rng.NextInt(catalog.NewLeafTransformPool.Length)];

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

    internal static bool GenerateForcedRareOffer(
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

    internal static bool GenerateNormalCardOffer(
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

    internal static ushort RollPotion(
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

    internal static void MatchSingleOfferCondition(
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

    internal static void MatchOfferConditions(
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

    internal static bool GenerateKaleidoscopeOffers(
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

    internal static bool GenerateScrollBoxes(
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

    internal static ushort SelectUnused(
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

    internal static void MatchKaleidoscopeConditions(
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
            bool match = condition.OrderedKaleidoscope
                ? (condition.KaleidoscopeFirstTarget == Beta110FastDenseId.Invalid || Contains(first, condition.KaleidoscopeFirstTarget)) &&
                  (condition.KaleidoscopeSecondTarget == Beta110FastDenseId.Invalid || Contains(second, condition.KaleidoscopeSecondTarget))
                : condition.TargetCount == 1
                ? Contains(first, condition.Target0) || Contains(second, condition.Target0)
                : (Contains(first, condition.Target0) && Contains(second, condition.Target1)) ||
                  (Contains(first, condition.Target1) && Contains(second, condition.Target0));
            if (match) matched[index] = 1;
        }
    }

    internal static void MatchScrollBoxesConditions(
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

    internal static bool HasStructuredConditionForRelic(
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
