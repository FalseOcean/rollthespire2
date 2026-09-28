using System.Runtime.CompilerServices;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct Beta110CombatRewardRouteEvaluation(
    Beta110CombatRewardRouteProjectionStatus Status,
    int Battle1Evaluated,
    int Battle2Evaluated,
    int Battle3Evaluated,
    string DiagnosticCode);

/// <summary>
/// Allocation-free numeric oracle for the current Search-visible normal combat
/// reward observables. All surviving candidates still enter Production Exact.
/// </summary>
internal static class Beta110CombatRewardFastStage
{
    private const int BattleCount = 6;
    private const int CardsPerReward = 3;
    private const int MaximumCardsPerBattle = 12;
    private const float PotionOddsStep = 0.1f;
    private const float InitialCardRarityOffset = -0.05f;
    private const float CardRarityOffsetCap = 0.4f;
    private const float UncommonBase = 0.37f;

    public static Beta110CombatRewardRouteEvaluation Evaluate(
        Beta110OpeningRewardState opening,
        Beta110CombatRewardFastPlan plan,
        Beta110FastEffectCatalog catalog,
        int ascension)
        => EvaluateCore(opening, plan, catalog, ascension, true);

    // C0 executes the authored/neutral model even when verification flags are
    // unavailable. Numeric failures still propagate; the legacy Keep shell is
    // deliberately not part of the Family implementation.
    internal static Beta110CombatRewardRouteEvaluation EvaluateForFamily(
        Beta110OpeningRewardState opening, Beta110CombatRewardFastPlan plan,
        Beta110FastEffectCatalog catalog, int ascension) =>
        RequireFamilyResult(EvaluateCore(opening, plan, catalog, ascension, false));

    internal static Beta110CombatRewardRouteEvaluation EvaluatePotionPrefixForFamily(
        Beta110OpeningRewardState opening, Beta110CombatRewardFastPlan plan,
        Beta110FastEffectCatalog catalog, int ascension) =>
        RequireFamilyResult(EvaluateCore(opening, plan, catalog, ascension, false, potionPrefix: true));

    private static Beta110CombatRewardRouteEvaluation RequireFamilyResult(Beta110CombatRewardRouteEvaluation result)
    {
        if (result.Status is not (Beta110CombatRewardRouteProjectionStatus.ExactProjection or
                                  Beta110CombatRewardRouteProjectionStatus.Rejected))
            throw new InvalidOperationException("C.CombatReward.NumericFailure:" + result.DiagnosticCode);
        return result;
    }

    private static Beta110CombatRewardRouteEvaluation EvaluateCore(
        Beta110OpeningRewardState opening, Beta110CombatRewardFastPlan plan,
        Beta110FastEffectCatalog catalog, int ascension, bool requireAuthority, bool potionPrefix = false)
    {
        if (!plan.Enabled)
            return new(Beta110CombatRewardRouteProjectionStatus.NotApplicable, 0, 0, 0, string.Empty);
        if (requireAuthority && !opening.ExactProjectionAuthority)
            return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep, 0, 0, 0,
                "OpeningRewardStateAuthorityIncomplete");
        if (requireAuthority && (!plan.CardPoolAuthorityExact || !plan.PotionPoolAuthorityExact))
            return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep, 0, 0, 0,
                "CombatRewardPoolAuthorityIncomplete");

        Beta110FastRng rewards = opening.Rewards;
        float potionOdds = opening.PotionOdds;
        float cardOffset = opening.CardRarityOffset;
        byte lastingCandyCounter = opening.LastingCandyCounter;

        Span<ushort> battleCards = stackalloc ushort[BattleCount * MaximumCardsPerBattle];
        Span<byte> battleCardCounts = stackalloc byte[BattleCount];
        Span<ushort> battlePotions = stackalloc ushort[BattleCount];
        Span<byte> battlePotionDropped = stackalloc byte[BattleCount];
        Span<int> battleGold = stackalloc int[BattleCount];
        battleCards.Fill(Beta110FastDenseId.Invalid);
        battlePotions.Fill(Beta110FastDenseId.Invalid);
        battleCardCounts.Clear();
        battlePotionDropped.Clear();
        battleGold.Clear();

        int maxBattle = Math.Clamp(plan.MaximumBattleOrdinal, 1, BattleCount);
        int battle1 = 0, battle2 = 0, battle3 = 0;
        for (int battleOrdinal = 1; battleOrdinal <= maxBattle; battleOrdinal++)
        {
            int battleIndex = battleOrdinal - 1;
            bool potionDropped;
            if ((opening.InfluenceFlags & Beta110CombatRewardInfluenceFlags.ForcePotionReward) != 0)
            {
                // Beta110 White Beast Statue returns before both the decision draw
                // and pity-state mutation.
                potionDropped = true;
            }
            else
            {
                potionDropped = rewards.NextFloat() < potionOdds;
                potionOdds += potionDropped ? -PotionOddsStep : PotionOddsStep;
            }
            battlePotionDropped[battleIndex] = potionDropped ? (byte)1 : (byte)0;

            int minGold = ascension >= 3 ? 7 : 10;
            int maxGoldInclusive = ascension >= 3 ? 15 : 20;
            int gold = minGold + rewards.NextInt(maxGoldInclusive - minGold + 1);
            gold += opening.FixedGoldAmount;
            battleGold[battleIndex] = gold;

            if (potionDropped)
            {
                byte rarity = RollPotionRarity(rewards.NextFloat());
                ReadOnlySpan<ushort> pool = PotionPool(catalog.CombatRewardPotionPool, rarity);
                if (pool.IsEmpty)
                    return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep,
                        battle1, battle2, battle3, "CombatRewardPotionRarityPoolEmpty");
                battlePotions[battleIndex] = pool[rewards.NextInt(pool.Length)];
            }

            if (potionPrefix)
            {
                // CPU binding admits only fixed, card-free predicates. The
                // potion/gold observation is already complete at this seam.
                foreach (var predicate in plan.Predicates)
                    if (predicate.BattleOrdinal == battleOrdinal &&
                        !MatchesPredicate(predicate, ReadOnlySpan<ushort>.Empty, potionDropped,
                            battlePotions[battleIndex], gold))
                        return PrefixResult(Beta110CombatRewardRouteProjectionStatus.Rejected, battleOrdinal);
                if (battleOrdinal == maxBattle)
                    return PrefixResult(Beta110CombatRewardRouteProjectionStatus.ExactProjection, battleOrdinal);
                // Earlier card draws remain a real Rewards continuation
                // prerequisite for later potions. Reuse the complete body below.
            }

            Span<ushort> output = battleCards.Slice(battleIndex * MaximumCardsPerBattle, MaximumCardsPerBattle);
            int outputCount = 0;
            if (!GenerateCardReward(
                    ref rewards,
                    catalog,
                    ascension,
                    requiredCardType: 0,
                    ref cardOffset,
                    output,
                    ref outputCount))
            {
                return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep,
                    battle1, battle2, battle3, "CombatRewardPrimaryCardPoolExhausted");
            }

            if ((opening.InfluenceFlags & Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard) != 0 &&
                lastingCandyCounter % 2 == 1)
            {
                if (!GenerateSingleCard(
                        ref rewards,
                        catalog,
                        ascension,
                        catalog.CombatRewardPowerPool,
                        ref cardOffset,
                        output[..outputCount],
                        out ushort power, baseOdds: true))
                {
                    return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep,
                        battle1, battle2, battle3, "CombatRewardLastingCandyPowerPoolExhausted");
                }
                if (outputCount >= output.Length)
                    return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep,
                        battle1, battle2, battle3, "CombatRewardCardScratchCapacityExceeded");
                if (power != Beta110FastDenseId.Invalid) output[outputCount++] = power;
            }

            for (int reward = 0; reward < opening.AdditionalCardRewardCount; reward++)
            {
                if (!GenerateCardReward(
                        ref rewards,
                        catalog,
                        ascension,
                        requiredCardType: 0,
                        ref cardOffset,
                        output,
                        ref outputCount))
                {
                    return new(Beta110CombatRewardRouteProjectionStatus.ConservativeKeep,
                        battle1, battle2, battle3, "CombatRewardAdditionalCardPoolExhausted");
                }
            }

            battleCardCounts[battleIndex] = checked((byte)outputCount);
            if ((opening.InfluenceFlags & Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard) != 0)
                lastingCandyCounter++;

            if (battleOrdinal == 1) battle1++;
            else if (battleOrdinal == 2) battle2++;
            else if (battleOrdinal == 3) battle3++;
        }

        if (plan.HasDistinctBattleAssignments)
        {
            ulong cardStates = 1, potionStates = 1;
            for (int battle = 0; battle < maxBattle; battle++)
            {
                if (battle < plan.CardAssignmentWindow)
                {
                    uint matches = 0;
                    var cards = battleCards.Slice(battle * MaximumCardsPerBattle, battleCardCounts[battle]);
                    for (int target = 0; target < plan.CardAssignmentTargets.Length; target++)
                        if (cards.Contains(plan.CardAssignmentTargets[target])) matches |= 1u << target;
                    cardStates = AdvanceAssignmentStates(cardStates, matches, plan.CardAssignmentTargets.Length);
                }
                if (battle < plan.PotionAssignmentWindow)
                {
                    uint matches = 0;
                    for (int target = 0; target < plan.PotionAssignmentTargets.Length; target++)
                    {
                        bool match = (CombatPotionSlotRequirement)plan.PotionAssignmentRequirements[target] switch
                        {
                            CombatPotionSlotRequirement.NoDrop => battlePotionDropped[battle] == 0,
                            CombatPotionSlotRequirement.DropAny => battlePotionDropped[battle] != 0,
                            CombatPotionSlotRequirement.DropSpecific => battlePotionDropped[battle] != 0 &&
                                battlePotions[battle] == plan.PotionAssignmentTargets[target],
                            _ => true
                        };
                        if (match) matches |= 1u << target;
                    }
                    potionStates = AdvanceAssignmentStates(potionStates, matches, plan.PotionAssignmentTargets.Length);
                }
            }
            if ((cardStates & (1UL << ((1 << plan.CardAssignmentTargets.Length) - 1))) == 0 ||
                (potionStates & (1UL << ((1 << plan.PotionAssignmentTargets.Length) - 1))) == 0)
                return new(Beta110CombatRewardRouteProjectionStatus.Rejected, battle1, battle2, battle3, "DistinctBattleAssignmentRejected");
        }

        foreach (Beta110CombatRewardFastPredicate predicate in plan.Predicates)
        {
            bool matched = false;
            int firstBattle = predicate.IsAnyBattle ? 1 : predicate.BattleOrdinal;
            int lastBattle = predicate.IsAnyBattle ? maxBattle : predicate.BattleOrdinal;
            for (int battleOrdinal = firstBattle; battleOrdinal <= lastBattle; battleOrdinal++)
            {
                if (battleOrdinal > maxBattle) continue;
                int index = battleOrdinal - 1;
                ReadOnlySpan<ushort> cards = battleCards.Slice(
                    index * MaximumCardsPerBattle,
                    battleCardCounts[index]);
                if (MatchesPredicate(
                        predicate,
                        cards,
                        battlePotionDropped[index] != 0,
                        battlePotions[index],
                        battleGold[index]))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                return new(Beta110CombatRewardRouteProjectionStatus.Rejected,
                    battle1, battle2, battle3, "CombatRewardPredicateRejected");
            }
        }

        return new(Beta110CombatRewardRouteProjectionStatus.ExactProjection,
            battle1, battle2, battle3, string.Empty);
    }

    private static Beta110CombatRewardRouteEvaluation PrefixResult(Beta110CombatRewardRouteProjectionStatus status, int observedBattle) =>
        new(status, observedBattle >= 1 ? 1 : 0, observedBattle >= 2 ? 1 : 0, observedBattle >= 3 ? 1 : 0,
            "CpuPotionPrefix");

    private static ulong AdvanceAssignmentStates(ulong states, uint matches, int count)
    {
        ulong next = states;
        for (int subset = 0; subset < (1 << count); subset++)
        {
            if ((states & (1UL << subset)) == 0) continue;
            for (int target = 0; target < count; target++)
                if ((matches & (1u << target)) != 0 && (subset & (1 << target)) == 0)
                    next |= 1UL << (subset | (1 << target));
        }
        return next;
    }

    private static bool GenerateCardReward(
        ref Beta110FastRng rewards,
        Beta110FastEffectCatalog catalog,
        int ascension,
        byte requiredCardType,
        ref float cardOffset,
        Span<ushort> output,
        ref int outputCount)
    {
        Span<ushort> selected = stackalloc ushort[CardsPerReward + 1];
        int selectedCount = 0;
        for (int ordinal = 0; ordinal < CardsPerReward; ordinal++)
        {
            if (!GenerateSingleCard(
                    ref rewards,
                    catalog,
                    ascension,
                    requiredCardType == 0 ? catalog.CombatRewardCardPool : catalog.CombatRewardPowerPool,
                    ref cardOffset,
                    selected[..selectedCount],
                    out ushort card))
                return false;
            if (outputCount >= output.Length) return false;
            selected[selectedCount++] = card;
            output[outputCount++] = card;
        }
        return true;
    }

    private static bool GenerateSingleCard(
        ref Beta110FastRng rewards,
        Beta110FastEffectCatalog catalog,
        int ascension,
        Beta110FastCardPool pool,
        ref float cardOffset,
        ReadOnlySpan<ushort> selected,
        out ushort card, bool baseOdds = false)
    {
        if (baseOdds)
        {
            // Vanilla Candy retries without exclusions only when all Powers are used.
            if (pool.Common.Length + pool.Uncommon.Length + pool.Rare.Length == 0)
            { card = Beta110FastDenseId.Invalid; return true; }
            if (CountAvailable(pool.Common, selected) + CountAvailable(pool.Uncommon, selected) + CountAvailable(pool.Rare, selected) == 0)
                selected = ReadOnlySpan<ushort>.Empty;
        }
        float roll = rewards.NextFloat();
        byte rolledRarity = baseOdds ? RollBaseCardRarity(roll, ascension) : RollCardRarity(roll, ascension, ref cardOffset);
        byte rarity = rolledRarity;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ReadOnlySpan<ushort> candidates = CardPool(pool, rarity);
            int available = CountAvailable(candidates, selected);
            if (available > 0)
            {
                int pick = rewards.NextInt(available);
                card = SelectAvailable(candidates, selected, pick);
                _ = rewards.NextFloat(); // natural upgrade roll is always consumed
                return card != Beta110FastDenseId.Invalid;
            }
            rarity = rarity switch
            {
                1 => 2,
                2 => 3,
                _ => 1
            };
        }
        card = Beta110FastDenseId.Invalid;
        return false;
    }

    private static byte RollBaseCardRarity(float value, int ascension)
    {
        float rare = ascension >= 7 ? .0149f : .03f;
        return value < rare ? (byte)3 : value < rare + UncommonBase ? (byte)2 : (byte)1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte RollCardRarity(float value, int ascension, ref float offset)
    {
        float baseRare = ascension >= 7 ? 0.0149f : 0.03f;
        float rareThreshold = baseRare + offset;
        float uncommonThreshold = rareThreshold + UncommonBase;
        if (value < rareThreshold)
        {
            offset = InitialCardRarityOffset;
            return 3;
        }
        float growth = ascension >= 7 ? 0.005f : 0.01f;
        offset = Math.Min(CardRarityOffsetCap, offset + growth);
        return value < uncommonThreshold ? (byte)2 : (byte)1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte RollPotionRarity(float value) => value switch
    {
        <= 0.1f => 3,
        <= 0.35f => 2,
        _ => 1
    };

    private static bool MatchesPredicate(
        Beta110CombatRewardFastPredicate predicate,
        ReadOnlySpan<ushort> cards,
        bool potionDropped,
        ushort potion,
        int gold)
    {
        if (!MatchesSet(cards, predicate.CardAny, predicate.CardAll, predicate.CardBan)) return false;
        if (predicate.PotionRequirement == NormalCombatPotionRequirement.MustDrop && !potionDropped) return false;
        if (predicate.PotionRequirement == NormalCombatPotionRequirement.MustNotDrop && potionDropped) return false;
        if (predicate.HasPotionIdentityPredicate)
        {
            if (!potionDropped || potion == Beta110FastDenseId.Invalid) return false;
            Span<ushort> single = stackalloc ushort[1];
            single[0] = potion;
            if (!MatchesSet(single, predicate.PotionAny, predicate.PotionAll, predicate.PotionBan)) return false;
        }
        if (predicate.HasMinimumGold && gold < predicate.MinimumGold) return false;
        if (predicate.HasMaximumGold && gold > predicate.MaximumGold) return false;
        return true;
    }

    private static bool MatchesSet(
        ReadOnlySpan<ushort> actual,
        ReadOnlySpan<ushort> any,
        ReadOnlySpan<ushort> all,
        ReadOnlySpan<ushort> ban)
    {
        if (!any.IsEmpty)
        {
            bool found = false;
            for (int i = 0; i < any.Length && !found; i++) found = actual.Contains(any[i]);
            if (!found) return false;
        }
        for (int i = 0; i < all.Length; i++) if (!actual.Contains(all[i])) return false;
        for (int i = 0; i < ban.Length; i++) if (actual.Contains(ban[i])) return false;
        return true;
    }

    private static int CountAvailable(ReadOnlySpan<ushort> pool, ReadOnlySpan<ushort> selected)
    {
        int count = 0;
        for (int i = 0; i < pool.Length; i++) if (!selected.Contains(pool[i])) count++;
        return count;
    }

    private static ushort SelectAvailable(ReadOnlySpan<ushort> pool, ReadOnlySpan<ushort> selected, int ordinal)
    {
        for (int i = 0; i < pool.Length; i++)
        {
            if (selected.Contains(pool[i])) continue;
            if (ordinal-- == 0) return pool[i];
        }
        return Beta110FastDenseId.Invalid;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<ushort> CardPool(Beta110FastCardPool pool, byte rarity) => rarity switch
    {
        1 => pool.Common,
        2 => pool.Uncommon,
        3 => pool.Rare,
        _ => ReadOnlySpan<ushort>.Empty
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<ushort> PotionPool(Beta110FastPotionPool pool, byte rarity) => rarity switch
    {
        1 => pool.Common,
        2 => pool.Uncommon,
        3 => pool.Rare,
        _ => ReadOnlySpan<ushort>.Empty
    };
}
