using System.Globalization;
using System.Text;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Search-only physical execution profile for the already-frozen unpinned
/// synthetic Combat Reward continuation. It does not define route semantics.
/// Production Predictor / Exact / SameRoute remain authoritative.
/// </summary>
internal enum Beta110GpuCombatRewardHotLoopVariant : byte
{
    Disabled = 0,
    SpecializedBaseline = 1,
    SpecializedDirectPool = 2,
    SpecializedRawBurn = 3,
    SpecializedProbabilityThresholds = 4,
    RetainedStructural = 5,
    FullExperimental = 6, // historical benchmark label retained
    OptimizedSingleRoute = 7
}

internal sealed record Beta110GpuCombatRewardHotLoopPlan(
    bool Enabled,
    string DisableReason,
    bool DirectCardOrdinalSelectionEligible,
    bool PotionIdentityRawBurnEligible,
    bool PotionIdentityObservableRequired,
    int MaximumBattleOrdinal,
    int PredicateCount,
    ulong[] CardRareStrictCutoffs,
    ulong[] CardUncommonStrictCutoffs,
    ulong[] PotionDropStrictCutoffs,
    ulong PotionRareInclusiveCutoff,
    ulong PotionUncommonInclusiveCutoff)
{
    public int CardStateCount => CardRareStrictCutoffs.Length;
    public int PotionDropNodeCount => PotionDropStrictCutoffs.Length;

    public static Beta110GpuCombatRewardHotLoopPlan Disabled(string reason) => new(
        false,
        reason,
        false,
        false,
        false,
        0,
        0,
        Array.Empty<ulong>(),
        Array.Empty<ulong>(),
        Array.Empty<ulong>(),
        0UL,
        0UL);
}

internal static class Beta110GpuCombatRewardHotLoopCompiler
{
    private const double DoubleUnit = 1.1102230246251565E-16;
    private const ulong NextFloatMantissaCardinality = 1UL << 53;
    private const float InitialCardRarityOffset = -0.05f;
    private const float CardRarityOffsetCap = 0.4f;
    private const float UncommonBase = 0.37f;
    private const float PotionOddsStep = 0.1f;

    internal static Beta110GpuCombatRewardHotLoopPlan Compile(
        int ascension, Beta110CombatRewardFastPlan reward, Beta110FastEffectCatalog catalog,
        Beta110GpuCombatRewardRoutePolicy routePolicy)
    {
        if (routePolicy != Beta110GpuCombatRewardRoutePolicy.UnpinnedAssumeUnperturbed)
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("PinnedOrLegacyRouteUsesAcceptedP10APath");
        if (!reward.Enabled)
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("NoCombatRewardConditions");
        if (reward.OpeningConsumption.HasReplay)
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("QueryLiteralOpeningConsumptionUsesGenericSyntheticEvaluator");
        if (reward.ExplicitContext.ChangesCurrentFastObservables)
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("ExplicitRewardContextUsesGenericSyntheticEvaluator");
        if (!reward.CardPoolAuthorityExact || !reward.PotionPoolAuthorityExact)
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("CombatRewardPoolAuthorityIncomplete");
        if (reward.GoldPredicateCount != 0)
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("GoldPredicateUsesGenericGpu");
        // The specialized loop fails native parity for existential potion identities
        // (six-battle source witness); generic streaming matches the CPU replay.
        // Keep the query on GPU through that evaluator, without an authoring gate.
        if (reward.Predicates.Any(p => p.IsAnyBattle && p.HasPotionIdentityPredicate))
            return Beta110GpuCombatRewardHotLoopPlan.Disabled("UnorderedPotionIdentityUsesGenericGpu");

        int maxBattle = Math.Clamp(reward.MaximumBattleOrdinal, 1, 6);
        bool directCard = HasSafeThreeCardOrdinalSelection(catalog.CombatRewardCardPool);
        bool potionPoolsComplete = catalog.CombatRewardPotionPool.HasEveryRarity;
        bool potionIdentityRequired = reward.PotionIdentityPredicateCount != 0;

        // This shortcut is admitted only when authored opening replay is empty and
        // the explicit Reward context does not change the current card/potion/gold
        // observables. Card rarity state can therefore advance at most three times
        // per battle before a rare reset on the current hot-loop surface.
        int cardStateCount = checked(maxBattle * 3 + 1);
        var rareCutoffs = new ulong[cardStateCount];
        var uncommonCutoffs = new ulong[cardStateCount];
        float baseRare = ascension >= 7 ? 0.0149f : 0.03f;
        float growth = ascension >= 7 ? 0.005f : 0.01f;
        float offset = InitialCardRarityOffset;
        for (int state = 0; state < cardStateCount; state++)
        {
            float rareThreshold = baseRare + offset;
            float uncommonThreshold = rareThreshold + UncommonBase;
            rareCutoffs[state] = FindNextFloatCutoff(rareThreshold, inclusive: false);
            uncommonCutoffs[state] = FindNextFloatCutoff(uncommonThreshold, inclusive: false);
            offset = Math.Min(CardRarityOffsetCap, offset + growth);
        }

        // Exact float32 potion-odds states are path-dependent because the game
        // mutates a float by +/-0.1f. Pack the tiny complete binary decision tree
        // for at most the first six battles, preserving those exact float bits.
        int potionNodeCount = (1 << maxBattle) - 1;
        var potionCutoffs = new ulong[potionNodeCount];
        var potionOddsByNode = new float[potionNodeCount];
        potionOddsByNode[0] = 0.4f;
        int internalNodeLimit = (1 << (maxBattle - 1)) - 1;
        for (int node = 0; node < potionNodeCount; node++)
        {
            float odds = potionOddsByNode[node];
            potionCutoffs[node] = FindNextFloatCutoff(odds, inclusive: false);
            if (node < internalNodeLimit)
            {
                int dropChild = node * 2 + 1;
                int missChild = dropChild + 1;
                potionOddsByNode[dropChild] = odds - PotionOddsStep;
                potionOddsByNode[missChild] = odds + PotionOddsStep;
            }
        }

        return new Beta110GpuCombatRewardHotLoopPlan(
            Enabled: true,
            DisableReason: string.Empty,
            DirectCardOrdinalSelectionEligible: directCard,
            PotionIdentityRawBurnEligible: potionPoolsComplete && !potionIdentityRequired,
            PotionIdentityObservableRequired: potionIdentityRequired,
            MaximumBattleOrdinal: maxBattle,
            PredicateCount: reward.PredicateCount,
            CardRareStrictCutoffs: rareCutoffs,
            CardUncommonStrictCutoffs: uncommonCutoffs,
            PotionDropStrictCutoffs: potionCutoffs,
            PotionRareInclusiveCutoff: FindNextFloatCutoff(0.1f, inclusive: true),
            PotionUncommonInclusiveCutoff: FindNextFloatCutoff(0.35f, inclusive: true));
    }

    internal static string BuildShaderDefines(Beta110GpuCombatRewardHotLoopPlan hot,
        Beta110GpuCombatRewardRoutePolicy routePolicy, Beta110GpuCombatRewardHotLoopVariant variant)
    {
        if (!hot.Enabled || routePolicy != Beta110GpuCombatRewardRoutePolicy.UnpinnedAssumeUnperturbed)
            variant = Beta110GpuCombatRewardHotLoopVariant.Disabled;

        bool enabled = variant != Beta110GpuCombatRewardHotLoopVariant.Disabled;
        bool directPool = enabled && hot.DirectCardOrdinalSelectionEligible &&
                          (variant is Beta110GpuCombatRewardHotLoopVariant.SpecializedDirectPool or
                                      Beta110GpuCombatRewardHotLoopVariant.RetainedStructural or
                                      Beta110GpuCombatRewardHotLoopVariant.FullExperimental or
                                      Beta110GpuCombatRewardHotLoopVariant.OptimizedSingleRoute);
        bool rawBurn = enabled &&
                       (variant is Beta110GpuCombatRewardHotLoopVariant.SpecializedRawBurn or
                                   Beta110GpuCombatRewardHotLoopVariant.RetainedStructural or
                                   Beta110GpuCombatRewardHotLoopVariant.FullExperimental or
                                   Beta110GpuCombatRewardHotLoopVariant.OptimizedSingleRoute);
        bool thresholds = enabled &&
                          (variant is Beta110GpuCombatRewardHotLoopVariant.SpecializedProbabilityThresholds or
                                      Beta110GpuCombatRewardHotLoopVariant.FullExperimental or
                                      Beta110GpuCombatRewardHotLoopVariant.OptimizedSingleRoute);
        bool potionIdentityRawBurn = rawBurn && hot.PotionIdentityRawBurnEligible;

        var sb = new StringBuilder(384);
        sb.Append("#define RT2_CR_HOT_ENABLED ").Append(enabled ? "1\n" : "0\n");
        sb.Append("#define RT2_CR_HOT_DIRECT_POOL ").Append(directPool ? "1\n" : "0\n");
        sb.Append("#define RT2_CR_HOT_RAW_BURN ").Append(rawBurn ? "1\n" : "0\n");
        sb.Append("#define RT2_CR_HOT_INTEGER_PROB ").Append(thresholds ? "1\n" : "0\n");
        sb.Append("#define RT2_CR_HOT_POTION_ID_RAW_BURN ").Append(potionIdentityRawBurn ? "1\n" : "0\n");
        sb.Append("#define RT2_CR_HOT_POTION_ID_REQUIRED ").Append(hot.PotionIdentityObservableRequired ? "1\n" : "0\n");
        sb.Append("#define RT2_CR_HOT_MAX_BATTLE ").Append(Math.Max(1, hot.MaximumBattleOrdinal).ToString(CultureInfo.InvariantCulture)).Append("u\n");
        sb.Append("#define RT2_CR_HOT_PREDICATE_COUNT ").Append(Math.Max(0, hot.PredicateCount).ToString(CultureInfo.InvariantCulture)).Append("u\n");
        return sb.ToString();
    }

    private static bool HasSafeThreeCardOrdinalSelection(Beta110FastCardPool pool)
    {
        if (pool.Common.Length < 3 || pool.Uncommon.Length < 3 || pool.Rare.Length < 3)
            return false;
        if (!Unique(pool.Common) || !Unique(pool.Uncommon) || !Unique(pool.Rare))
            return false;
        var all = new HashSet<ushort>();
        return AddAll(all, pool.Common) && AddAll(all, pool.Uncommon) && AddAll(all, pool.Rare);
    }

    private static bool Unique(ReadOnlySpan<ushort> values)
    {
        var seen = new HashSet<ushort>();
        for (int i = 0; i < values.Length; i++)
            if (!seen.Add(values[i])) return false;
        return true;
    }

    private static bool AddAll(HashSet<ushort> seen, ReadOnlySpan<ushort> values)
    {
        for (int i = 0; i < values.Length; i++)
            if (!seen.Add(values[i])) return false;
        return true;
    }

    /// <summary>
    /// Returns the first 53-bit NextDouble mantissa whose subsequent binary32
    /// cast no longer satisfies either value &lt; threshold or value &lt;= threshold.
    /// Comparing mantissa &lt; cutoff is therefore exactly equivalent to the CPU
    /// NextFloat comparison, including IEEE-754 round-to-nearest-even boundaries.
    /// </summary>
    internal static ulong FindNextFloatCutoff(float threshold, bool inclusive)
    {
        ulong low = 0UL;
        ulong high = NextFloatMantissaCardinality;
        while (low < high)
        {
            ulong mid = low + ((high - low) >> 1);
            float value = (float)(mid * DoubleUnit);
            bool passes = inclusive ? value <= threshold : value < threshold;
            if (passes) low = mid + 1UL;
            else high = mid;
        }
        return low;
    }
}
