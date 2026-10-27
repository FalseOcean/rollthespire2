using System.Numerics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Query probability for the first six normal-combat card and potion rewards.
/// Single-player and multiplayer use the same explicitly authored opening profile;
/// unspecified Bones/Capsule held influences follow the Owner's neutral approximation.
/// The chronological reward DP retains potion/card pity, group de-duplication,
/// reward modifiers and ordered/unordered distinct-battle matching. Physical
/// filtering and Production Exact keep their own actual continuation semantics.
/// </summary>
internal static partial class CombatRewardProbabilityEstimator
{
    private const int BattleCount = 6;
    private const int CardsPerReward = 3;
    private const double InitialCardRarityOffset = -0.05d;
    private const double CardRarityOffsetCap = 0.4d;
    private const double UncommonBase = 0.37d;

    private static readonly ModelKey PrayerWheel = new(BaseGameModelKeys.Categories.Relic, "PRAYER_WHEEL");
    private static readonly ModelKey WhiteBeastStatue = new(BaseGameModelKeys.Categories.Relic, "WHITE_BEAST_STATUE");
    private static readonly ModelKey LastingCandy = new(BaseGameModelKeys.Categories.Relic, "LASTING_CANDY");
    private static readonly ModelKey AmethystAubergine = new(BaseGameModelKeys.Categories.Relic, "AMETHYST_AUBERGINE");

    private readonly record struct RewardInfluenceProfile(
        bool PrayerWheel,
        bool WhiteBeastStatue,
        bool LastingCandy,
        bool AmethystAubergine)
    {
        public int AdditionalCardRewardCount => PrayerWheel ? 1 : 0;
        public bool ForcePotionReward => WhiteBeastStatue;
        public int FixedGoldAmount => AmethystAubergine ? 15 : 0;

        public RewardInfluenceProfile Add(ModelKey relic)
        {
            if (relic == CombatRewardProbabilityEstimator.PrayerWheel) return this with { PrayerWheel = true };
            if (relic == CombatRewardProbabilityEstimator.WhiteBeastStatue) return this with { WhiteBeastStatue = true };
            if (relic == CombatRewardProbabilityEstimator.LastingCandy) return this with { LastingCandy = true };
            if (relic == CombatRewardProbabilityEstimator.AmethystAubergine) return this with { AmethystAubergine = true };
            return this;
        }

        public string Label
        {
            get
            {
                var parts = new List<string>(4);
                if (PrayerWheel) parts.Add("PrayerWheel");
                if (WhiteBeastStatue) parts.Add("WhiteBeast");
                if (LastingCandy) parts.Add("LastingCandy");
                if (AmethystAubergine) parts.Add("AmethystAubergine");
                return parts.Count == 0 ? "Neutral" : string.Join("+", parts);
            }
        }
    }

    private readonly record struct CardPoolEntry(ModelKey Key, EffectCardRarity Rarity, EffectCardType Type, int TargetIndex);

    private sealed record CardPoolModel(
        CardPoolEntry[] Entries,
        int[] TotalByRarity,
        int[] PowerByRarity,
        IReadOnlyDictionary<ModelKey, int> TargetIndexByKey,
        int TargetCount);

    private sealed record PotionPoolModel(
        int[] TotalByRarity,
        IReadOnlyDictionary<ModelKey, (int Rarity, int TargetIndex)> TargetInfoByKey,
        int TargetCount);

    private readonly record struct CardGroupState(
        int UsedCommon,
        int UsedUncommon,
        int UsedRare,
        int UsedCommonPower,
        int UsedUncommonPower,
        int UsedRarePower,
        BigInteger UsedTargetMask);

    private readonly record struct CardSequenceState(int PityStreak, BigInteger ObservedMask, CardGroupState Group);
    private readonly record struct CardBattleState(int PityStreak, BigInteger ObservedMask);
    private readonly record struct PotionOutcome(bool Generated, int TargetIndex, int NextPotionStep, double Probability);
    private readonly record struct RewardDpState(
        int CardPityStreak,
        int PotionStep,
        BigInteger AnyConditionMask,
        ulong CardAssignmentStates,
        ulong PotionAssignmentStates);

    private readonly record struct ModernCardConstraint(ModelKey Target, int? OrderedBattleOrdinal);
    private readonly record struct ModernPotionConstraint(CombatPotionRewardSlotSearchCondition Slot, int? OrderedBattleOrdinal);

    private sealed record MatcherContext(
        NormalCombatRewardSearchCondition[] Conditions,
        ModernCardConstraint[] ModernCards,
        ModernPotionConstraint[] ModernPotions,
        IReadOnlyDictionary<ModelKey, int> CardTargetIndex,
        IReadOnlyDictionary<ModelKey, int> PotionTargetIndex,
        IReadOnlyDictionary<int, int> AnyConditionBitByIndex,
        BigInteger RequiredAnyConditionMask,
        int MaximumBattleOrdinal,
        bool UsesCards,
        bool UsesPotions,
        bool UsesGold,
        int ModernCardUnorderedCount,
        int ModernPotionUnorderedCount,
        int ModernCardWindowCount,
        int ModernPotionWindowCount);

    public static SearchSelectivityEstimate Estimate(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CompiledSearch.NormalizedQuery.CombatCardExclusion is not { IsEmpty: false })
            return EstimateCore(plan);
        var positive = EstimateOptimizedConditions(plan);
        var exclusion = EstimateCardExclusion(plan);
        if (positive.Probability is not { } p || exclusion.Probability is not { } e)
            return SearchSelectivityEstimate.Unpriced("Probability.CombatReward.ExclusionPartial",
                positive.Probability.HasValue ? exclusion.EvidenceCode + ";" + exclusion.Notes : positive.EvidenceCode + ";" + positive.Notes);
        return positive with { Probability = p * e, EstimatedSurvivalRate = p * e,
            EvidenceCode = "Probability.CombatReward.PositiveAndExclusionIndependent",
            Notes = $"Positive={p:G17};Exclusion={e:G17};ExclusionRemainsExactOnly",
            Confidence = SearchSelectivityConfidence.Medium,
            Assumptions = positive.Assumptions.Concat(exclusion.Assumptions).Append(
                "PositiveAndExclusionDependenceUnproved=OwnerApprovedIndependentProduct").Distinct().ToArray() };
    }

    internal static SearchSelectivityEstimate EstimateOptimizedConditions(SearchSelectivityInput input)
    {
        var compiled = input.CompiledSearch;
        if (compiled.NormalizedQuery.CombatCardExclusion is { IsEmpty: false })
            compiled = SearchCompiler.CompilePlayer(compiled.NormalizedQuery with { CombatCardExclusion = null }, compiled.Context);
        if (!compiled.NormalizedQuery.HasCombatRewardConstraints)
            return SearchSelectivityEstimate.Exact(1, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedPredicate, SearchSelectivityDependencyClass.AssumedIndependent,
                "Probability.CombatReward.NoOptimizedPredicate", "No positive C predicate participates in the optimized sieve.",
                ["ExactOnlyExclusionNotCheckedBySieve=true"]);
        return EstimateCore(SearchSelectivityInput.From(compiled));
    }

    private static SearchSelectivityEstimate EstimateCore(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ProbabilitySemanticView semantic = ProbabilitySemanticProjection.From(plan);
        if (!semantic.NormalizedQuery.HasCombatRewardConstraints)
            return SearchSelectivityEstimate.Unpriced("Probability.CombatReward.NoPredicate", "No Combat Reward predicate to price.");
        if (!semantic.HasRelation(
                SemanticRelationKind.RouteScoped,
                SemanticFactKind.CombatReward,
                SemanticFactKind.ResolvedRewardRoute))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CombatReward.CanonicalRouteScopeMissing",
                "Combat Reward probability requires the Canonical RouteScoped relation.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent,
                new[] { "RouteSource=ResolvedRouteSemantics", "SemanticRelationAuthority=Canonical" });
        }
        if (!RuntimeProfilePolicies.UsesBeta110SharedAlgorithms(plan.ProfileId))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CombatReward.ProfileUnsupported",
                "The chronological Reward probability authority requires the Modern110 RNG semantic profile.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        NeowEffectAuthoritySnapshot? authority = plan.Authority.EffectAuthority;
        if (authority is null)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CombatReward.RuntimeAuthorityMissing",
                "Combat Reward probability requires the immutable runtime EffectAuthority snapshot.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        NormalCombatRewardSearchCondition[] conditions = semantic.NormalizedQuery.LegacyCombatRewardConstraints
            .Where(condition => !condition.IsEmpty)
            .ToArray();
        if (conditions.Length == 0 &&
            semantic.NormalizedQuery.CombatCardRewards is not { IsEmpty: false } &&
            semantic.NormalizedQuery.CombatPotionRewards is not { IsEmpty: false })
            return SearchSelectivityEstimate.Exact(1d, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedPredicate, SearchSelectivityDependencyClass.AssumedIndependent,
                "Probability.CombatReward.Empty", "No active Combat Reward condition remains after normalization.");
        if (conditions.Any(condition => condition.BattleOrdinal is < 0 or > 3))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CombatReward.BattleOrdinalUnsupported",
                "Combat Reward probability supports the Product opening window: exact battle 1-3 or BattleOrdinal=0 (any opening battle).",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        if (!TryBuildMatcherContext(
                conditions,
                semantic.NormalizedQuery.CombatCardRewards,
                semantic.NormalizedQuery.CombatPotionRewards,
                out MatcherContext matcher,
                out string matcherIssue))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.CombatReward.Matcher:" + matcherIssue,
                "Combat Reward probability could not normalize the current ordered opening-battle matcher semantics.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        CardPoolModel cards = new(
            Array.Empty<CardPoolEntry>(),
            new int[3],
            new int[3],
            matcher.CardTargetIndex,
            matcher.CardTargetIndex.Count);
        if (matcher.UsesCards)
        {
            if (!authority.HasExactCharacterRewardPool)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.CombatReward.CardPoolAuthority:MissingExactRuntimeUnlockedCharacterRewardPool",
                    "Card Reward probability requires the exact runtime-unlocked character reward pool.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent);
            }
            if (!TryBuildCardPool(authority, matcher.CardTargetIndex, plan.Authority.PlayersCount > 1, out cards, out string cardIssue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.CombatReward.CardPoolAuthority:" + cardIssue,
                    "Card Reward probability could not construct the exact runtime-unlocked character reward pool model.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent);
            }
        }

        PotionPoolModel potions = new(
            new int[3],
            new Dictionary<ModelKey, (int Rarity, int TargetIndex)>(ModelKeyComparer.Instance),
            matcher.PotionTargetIndex.Count);
        if (matcher.UsesPotions)
        {
            if (!authority.HasExactPotions)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.CombatReward.PotionPoolAuthority:MissingExactRuntimePotionPool",
                    "Potion Reward probability requires the exact runtime potion pool.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent);
            }
            if (!TryBuildPotionPool(authority, matcher.PotionTargetIndex, plan.Authority.PlayersCount > 1, out potions, out string potionIssue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.CombatReward.PotionPoolAuthority:" + potionIssue,
                    "Potion Reward probability could not construct the exact runtime potion pool model.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent);
            }
        }

        const string openingEvidence = "OpeningRngDrawOffsetsDoNotChangeMarginalModel";
        if (!TryResolveAuthoredProfile(plan, semantic, matcher, out var projectedProfile, out string influenceDetail, out string issue))
            return SearchSelectivityEstimate.Unpriced("Probability.CombatReward.ProductProjection:" + issue,
                influenceDetail, SearchSelectivityConfidence.Low, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction, SearchSelectivityDependencyClass.RouteDependent);
        bool pinned = semantic.Routes.CombatReward.Kind == ResolvedCombatRewardRouteKind.PinnedOpeningRoute;
        IReadOnlyList<SearchSelectivityDomain> conditionedDomains = pinned ? [SearchSelectivityDomain.Neow] : [];
        var dependencyClass = pinned ? SearchSelectivityDependencyClass.RouteDependent : SearchSelectivityDependencyClass.AssumedIndependent;
        double rewardSurvival = EstimateForProfile(plan.Ascension, cards, potions, matcher, projectedProfile);
        string notes = $"ChronologicalNormalCombatReward;context={projectedProfile.Label};" +
            $"{openingEvidence};" +
            $"rewardSurvival={rewardSurvival:G17};Influence={influenceDetail}";
        var estimate = SearchSelectivityEstimate.Exact(rewardSurvival, SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction, dependencyClass, "Probability.Authority.CombatRewardProductProjected", notes,
            [
                "ProbabilityModel=ExplicitAuthoredOpeningRewardProfile",
                "SingleplayerAndMultiplayerShareRewardDp=true",
                "UnspecifiedBonesRewardImpactIgnored=OwnerAcceptedApproximation",
                "UnspecifiedCapsuleRewardImpactIgnored=OwnerAcceptedApproximation",
                "InitialRelicBagConditionsRemainSeparateProbabilityFacts",
                openingEvidence,
                "ProductionExactAndSameRouteWitnessRemainRealRouteTruth=true"
            ], conditionedOnDomains: conditionedDomains);
        return plan.Authority.PlayersCount > 1
            ? estimate with { Confidence = SearchSelectivityConfidence.Medium } : estimate;
    }

    private static bool TryResolveAuthoredProfile(SearchSelectivityInput plan, ProbabilitySemanticView semantic,
        MatcherContext matcher, out RewardInfluenceProfile projected, out string detail, out string issue)
    {
        issue = string.Empty;
        projected = default;
        foreach (ModelKey source in ExplicitOpeningRelicFacts.EnumerateGuaranteed(semantic.NormalizedQuery))
        {
            bool adapter = OpeningCombatRewardImpactAdapterRegistry.TryResolve(plan.ProfileId, source, out var descriptor);
            if (adapter && descriptor.Operations.Any(operation => operation.Kind is not (
                    OpeningCombatRewardImpactOperationKind.ForcePotionReward or
                    OpeningCombatRewardImpactOperationKind.AddCardReward or
                    OpeningCombatRewardImpactOperationKind.AddFixedGoldReward or
                    OpeningCombatRewardImpactOperationKind.AddPowerCardEveryOtherCombat or
                    OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType or
                    OpeningCombatRewardImpactOperationKind.UpgradeNextCardRewards or
                    OpeningCombatRewardImpactOperationKind.EnchantFirstCardRewardWithGlam)) ||
                !adapter && !HasSupportedAuthoredHeldImpact(plan.ProfileId, source, matcher))
            {
                issue = "UnsupportedExplicitHeldInfluence:" + source.Serialized;
                detail = "An explicitly acquired relic changes the requested reward observable without a supported probability adapter.";
                return false;
            }
            projected = projected.Add(source);
        }
        RewardInfluenceProfile canonical = default;
        if (semantic.Routes.CombatReward.Kind == ResolvedCombatRewardRouteKind.NeutralNonPerturbingContinuation)
            detail = "ResolvedRouteSemantics=NeutralNonPerturbingContinuation;UnselectedOpeningNeutral";
        else if (semantic.Routes.CombatReward.Kind == ResolvedCombatRewardRouteKind.PinnedOpeningRoute &&
            semantic.NormalizedQuery.OpeningRoute is { IsValid: true } route &&
            semantic.Routes.CombatReward.PinnedOpeningRouteKey == route.RouteRelicKey)
        {
            foreach (var relation in semantic.Relations.Where(relation =>
                relation.Kind == SemanticRelationKind.StateInfluence &&
                relation.Source.Kind == SemanticFactKind.RewardInfluenceRelic &&
                relation.Target.Kind == SemanticFactKind.CombatReward))
                if (relation.Source.Key is { } key) canonical = canonical.Add(key);
            detail = "PinnedAuthoredOpening;CanonicalStateInfluence=" + canonical.Label;
        }
        else
        {
            issue = "ResolvedCombatRewardRouteNotPriceable:" + semantic.Routes.CombatReward.Kind;
            detail = "Combat Reward probability requires a matching canonical resolved opening route.";
            return false;
        }
        if (canonical == projected) return true;
        issue = "CanonicalExplicitRewardProfileMismatch";
        detail += ";Projected=" + projected.Label + ";Expected=" + canonical.Label;
        return false;
    }

    private static bool HasSupportedAuthoredHeldImpact(RuntimeProfileId profile, ModelKey source, MatcherContext matcher)
    {
        if (!VanillaRelicRewardEffects.TryGet(profile, source, out var effect)) return false;
        if (effect.IsHeldNeutralForNormalCombatReward) return true;
        var relevant = HeldNormalCombatRewardEffects.UnknownModHook;
        if (matcher.UsesCards) relevant |= HeldNormalCombatRewardEffects.ChangesCardRewardPool |
            HeldNormalCombatRewardEffects.ChangesCardRewardCount | HeldNormalCombatRewardEffects.ChangesCardRewardOptions |
            HeldNormalCombatRewardEffects.AddsRewardEntries | HeldNormalCombatRewardEffects.RemovesRewardEntries |
            HeldNormalCombatRewardEffects.ReplacesRewardEntries | HeldNormalCombatRewardEffects.AddsCardRewardAlternative;
        if (matcher.UsesPotions) relevant |= HeldNormalCombatRewardEffects.ForcesPotionReward |
            HeldNormalCombatRewardEffects.ChangesPotionDropState | HeldNormalCombatRewardEffects.ChangesPotionRewardPool;
        if (matcher.UsesGold) relevant |= HeldNormalCombatRewardEffects.ChangesGoldReward;
        return (effect.HeldCapabilities & relevant) == 0;
    }

    // Authoring-only support query. Reuses chronological generation; does not extend Search horizon.
    internal static bool CanAuthorCards(int ascension,
        IReadOnlyList<(ModelKey Key, EffectCardRarity Rarity, EffectCardType Type)> candidates,
        IReadOnlyList<ModelKey?> slots, bool unordered, bool wheel, bool candy)
    {
        if (slots.Count is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(slots));
        var targets = slots.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
        if (targets.Length == 0) return true;
        var indexes = targets.Distinct().Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        if (targets.Any(k => !candidates.Any(c => c.Key == k))) return false;
        var entries = candidates.Select(c => new CardPoolEntry(c.Key, c.Rarity, c.Type, indexes.GetValueOrDefault(c.Key, -1))).ToArray();
        var totals = new int[3]; var powers = new int[3];
        foreach (var e in entries) { int r = RarityIndex(e.Rarity); if (r < 0) continue; totals[r]++; if (e.Type == EffectCardType.Power) powers[r]++; }
        var pool = new CardPoolModel(entries, totals, powers, indexes, indexes.Count);
        var profile = new RewardInfluenceProfile(wheel, false, candy, false);
        var states = new HashSet<(int Pity, int Assigned)> { (0, 0) };
        var memo = new Dictionary<(int Battle, int Pity), IReadOnlyList<(CardBattleState State, double Probability)>>();
        int full = (1 << targets.Length) - 1;
        for (int battle = 1; battle <= slots.Count; battle++)
        {
            var next = new HashSet<(int Pity, int Assigned)>();
            foreach (var state in states)
            {
                if (!memo.TryGetValue((battle, state.Pity), out var outcomes))
                    memo[(battle, state.Pity)] = outcomes = GenerateBattleCards(pool, ascension, battle, state.Pity, profile);
                foreach (var (result, mass) in outcomes)
                {
                    if (mass <= 0) continue;
                    bool Has(ModelKey key) => (result.ObservedMask & (BigInteger.One << indexes[key])) != 0;
                    if (!unordered)
                    {
                        if (slots[battle - 1] is { } target && !Has(target)) continue;
                        next.Add((result.PityStreak, 0));
                    }
                    else
                    {
                        next.Add((result.PityStreak, state.Assigned));
                        // Match at most one requirement to this battle, as the existing C assignment donor does.
                        for (int i = 0; i < targets.Length; i++)
                            if ((state.Assigned & (1 << i)) == 0 && Has(targets[i]))
                                next.Add((result.PityStreak, state.Assigned | (1 << i)));
                    }
                }
            }
            states = next;
            if (states.Count == 0) return false;
        }
        return !unordered || states.Any(s => s.Assigned == full);
    }

    private static double EstimateForProfile(
        int ascension,
        CardPoolModel cards,
        PotionPoolModel potions,
        MatcherContext matcher,
        RewardInfluenceProfile profile)
    {
        string? key = ProfileProbabilityCacheKey(ascension, cards, potions, matcher, profile);
        if (key is not null)
        {
            lock (ProfileProbabilityCacheGate)
                if (ProfileProbabilityCache.TryGetValue(key, out double cached)) return cached;
        }

        Interlocked.Increment(ref _profileProbabilityComputations);
        double probability = EstimateForProfileUncached(ascension, cards, potions, matcher, profile);
        if (key is not null)
        {
            lock (ProfileProbabilityCacheGate)
            {
                // Concurrent misses may finish together; retain only one scalar.
                if (!ProfileProbabilityCache.ContainsKey(key))
                {
                    if (ProfileProbabilityCache.Count == ProfileProbabilityCacheCapacity)
                        ProfileProbabilityCache.Remove(ProfileProbabilityCacheOrder.Dequeue());
                    ProfileProbabilityCache.Add(key, probability);
                    ProfileProbabilityCacheOrder.Enqueue(key);
                }
            }
        }
        return probability;
    }

    private static double EstimateForProfileUncached(
        int ascension,
        CardPoolModel cards,
        PotionPoolModel potions,
        MatcherContext matcher,
        RewardInfluenceProfile profile)
    {
        var states = new Dictionary<RewardDpState, double>
        {
            [new RewardDpState(0, 4, BigInteger.Zero, 1, 1)] = 1d
        };

        var cardMemo = new Dictionary<(int Battle, int Pity, RewardInfluenceProfile Profile), IReadOnlyList<(CardBattleState State, double Probability)>>();
        for (int battle = 1; battle <= matcher.MaximumBattleOrdinal; battle++)
        {
            var next = new Dictionary<RewardDpState, double>();
            foreach ((RewardDpState state, double stateMass) in states)
            {
                IReadOnlyList<(CardBattleState State, double Probability)> cardOutcomes;
                if (!matcher.UsesCards)
                {
                    cardOutcomes = new[] { (new CardBattleState(state.CardPityStreak, BigInteger.Zero), 1d) };
                }
                else
                {
                    var cardKey = (battle, state.CardPityStreak, profile);
                    if (!cardMemo.TryGetValue(cardKey, out IReadOnlyList<(CardBattleState State, double Probability)>? cachedOutcomes))
                    {
                        cachedOutcomes = GenerateBattleCards(cards, ascension, battle, state.CardPityStreak, profile);
                        cardMemo[cardKey] = cachedOutcomes;
                    }
                    cardOutcomes = cachedOutcomes;
                }

                IReadOnlyList<PotionOutcome> potionOutcomes = matcher.UsesPotions
                    ? GeneratePotionOutcomes(potions, state.PotionStep, profile.ForcePotionReward)
                    : new[] { new PotionOutcome(false, -1, state.PotionStep, 1d) };
                IReadOnlyList<(int Gold, double Probability)> goldOutcomes = matcher.UsesGold
                    ? GenerateGoldOutcomes(ascension, profile.FixedGoldAmount)
                    : new[] { (0, 1d) };

                foreach ((CardBattleState cardState, double cardMass) in cardOutcomes)
                foreach (PotionOutcome potion in potionOutcomes)
                foreach ((int gold, double goldMass) in goldOutcomes)
                {
                    double mass = stateMass * cardMass * potion.Probability * goldMass;
                    if (mass <= 0d) continue;
                    if (!TryApplyBattleMatcher(
                            matcher,
                            battle,
                            cardState.ObservedMask,
                            potion,
                            gold,
                            state.AnyConditionMask,
                            state.CardAssignmentStates,
                            state.PotionAssignmentStates,
                            out BigInteger anyMask,
                            out ulong cardAssignmentStates,
                            out ulong potionAssignmentStates))
                    {
                        continue;
                    }
                    var key = new RewardDpState(
                        cardState.PityStreak,
                        potion.NextPotionStep,
                        anyMask,
                        cardAssignmentStates,
                        potionAssignmentStates);
                    AddMass(next, key, mass);
                }
            }
            states = next;
            if (states.Count == 0) return 0d;
        }

        double total = 0d;
        foreach ((RewardDpState state, double mass) in states)
        {
            bool legacySatisfied = (state.AnyConditionMask & matcher.RequiredAnyConditionMask) == matcher.RequiredAnyConditionMask;
            bool cardSatisfied = AssignmentComplete(state.CardAssignmentStates, matcher.ModernCardUnorderedCount);
            bool potionSatisfied = AssignmentComplete(state.PotionAssignmentStates, matcher.ModernPotionUnorderedCount);
            if (legacySatisfied && cardSatisfied && potionSatisfied)
                total += mass;
        }
        return Math.Clamp(total, 0d, 1d);
    }

    private static IReadOnlyList<(CardBattleState State, double Probability)> GenerateBattleCards(
        CardPoolModel pool,
        int ascension,
        int battleOrdinal,
        int startingPity,
        RewardInfluenceProfile profile)
    {
        var states = new Dictionary<CardBattleState, double>
        {
            [new CardBattleState(startingPity, BigInteger.Zero)] = 1d
        };

        bool candy = profile.LastingCandy && battleOrdinal % 2 == 0;
        states = GenerateCardRewardGroup(states, pool, ascension, candy);
        for (int extra = 0; extra < profile.AdditionalCardRewardCount; extra++)
            states = GenerateCardRewardGroup(states, pool, ascension, includeTrailingPowerCard: false);

        return states.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    private static Dictionary<CardBattleState, double> GenerateCardRewardGroup(
        IReadOnlyDictionary<CardBattleState, double> incoming,
        CardPoolModel pool,
        int ascension,
        bool includeTrailingPowerCard)
    {
        var sequence = new Dictionary<CardSequenceState, double>();
        foreach ((CardBattleState state, double mass) in incoming)
        {
            sequence[new CardSequenceState(state.PityStreak, state.ObservedMask, default)] = mass;
        }

        int drawCount = CardsPerReward + (includeTrailingPowerCard ? 1 : 0);
        for (int ordinal = 0; ordinal < drawCount; ordinal++)
        {
            bool requiredPower = includeTrailingPowerCard && ordinal == drawCount - 1;
            var next = new Dictionary<CardSequenceState, double>();
            foreach ((CardSequenceState state, double mass) in sequence)
            {
                if (requiredPower && pool.PowerByRarity.Sum() == 0)
                { AddMass(next, state, mass); continue; }
                CardGroupState selectionGroup = requiredPower && Enumerable.Range(0, 3).Sum(r => AvailableCount(pool, state.Group, r, true)) == 0
                    ? default : state.Group;
                var rarityBranches = requiredPower ? CandyRarityBranches(ascension, state.PityStreak) : RarityBranches(ascension, state.PityStreak);
                foreach ((EffectCardRarity rolledRarity, int nextPity, double rarityMass) in rarityBranches)
                {
                    if (rarityMass <= 0d) continue;
                    int selectedRarity = NextAvailableRarity(pool, selectionGroup, rolledRarity, requiredPower);
                    if (selectedRarity < 0) continue;
                    int available = AvailableCount(pool, selectionGroup, selectedRarity, requiredPower);
                    if (available <= 0) continue;

                    foreach ((int TargetIndex, bool IsPower, int Count) candidate in CandidateCategories(pool, selectionGroup, selectedRarity, requiredPower))
                    {
                        if (candidate.Count <= 0) continue;
                        double candidateMass = (double)candidate.Count / available;
                        CardGroupState group = ConsumeCard(selectionGroup, selectedRarity, candidate.IsPower, candidate.TargetIndex);
                        BigInteger observed = state.ObservedMask;
                        if (candidate.TargetIndex >= 0) observed |= BigInteger.One << candidate.TargetIndex;
                        AddMass(next,
                            new CardSequenceState(nextPity, observed, group),
                            mass * rarityMass * candidateMass);
                    }
                }
            }
            sequence = next;
            if (sequence.Count == 0) break;
        }

        var output = new Dictionary<CardBattleState, double>();
        foreach ((CardSequenceState state, double mass) in sequence)
            AddMass(output, new CardBattleState(state.PityStreak, state.ObservedMask), mass);
        return output;
    }

    private static IEnumerable<(EffectCardRarity Rarity, int NextPity, double Probability)> CandyRarityBranches(int ascension, int pityStreak)
    {
        double rare = ascension >= 7 ? .0149d : .03d;
        yield return (EffectCardRarity.Rare, pityStreak, rare);
        yield return (EffectCardRarity.Uncommon, pityStreak, UncommonBase);
        yield return (EffectCardRarity.Common, pityStreak, 1d - rare - UncommonBase);
    }

    private static IEnumerable<(EffectCardRarity Rarity, int NextPity, double Probability)> RarityBranches(int ascension, int pityStreak)
    {
        double growth = ascension >= 7 ? 0.005d : 0.01d;
        double baseRare = ascension >= 7 ? 0.0149d : 0.03d;
        double offset = Math.Min(CardRarityOffsetCap, InitialCardRarityOffset + growth * pityStreak);
        double rareThreshold = baseRare + offset;
        double uncommonThreshold = rareThreshold + UncommonBase;
        double rare = Clamp01(rareThreshold);
        double uncommon = Math.Max(0d, Clamp01(uncommonThreshold) - Clamp01(rareThreshold));
        double common = Math.Max(0d, 1d - Clamp01(uncommonThreshold));
        int maxPity = ascension >= 7 ? 90 : 45;
        int nextNonRare = Math.Min(maxPity, pityStreak + 1);
        if (rare > 0d) yield return (EffectCardRarity.Rare, 0, rare);
        if (uncommon > 0d) yield return (EffectCardRarity.Uncommon, nextNonRare, uncommon);
        if (common > 0d) yield return (EffectCardRarity.Common, nextNonRare, common);
    }

    private static int NextAvailableRarity(CardPoolModel pool, CardGroupState group, EffectCardRarity rolled, bool requiredPower)
    {
        EffectCardRarity current = rolled;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int rarity = RarityIndex(current);
            if (rarity >= 0 && AvailableCount(pool, group, rarity, requiredPower) > 0) return rarity;
            current = current switch
            {
                EffectCardRarity.Common => EffectCardRarity.Uncommon,
                EffectCardRarity.Uncommon => EffectCardRarity.Rare,
                _ => EffectCardRarity.Common
            };
        }
        return -1;
    }

    private static int AvailableCount(CardPoolModel pool, CardGroupState group, int rarity, bool requiredPower) =>
        requiredPower
            ? pool.PowerByRarity[rarity] - UsedPower(group, rarity)
            : pool.TotalByRarity[rarity] - UsedTotal(group, rarity);

    private static IEnumerable<(int TargetIndex, bool IsPower, int Count)> CandidateCategories(
        CardPoolModel pool,
        CardGroupState group,
        int rarity,
        bool requiredPower)
    {
        int targetCount = 0;
        int targetPowerCount = 0;
        foreach (CardPoolEntry entry in pool.Entries)
        {
            if (entry.TargetIndex < 0 || RarityIndex(entry.Rarity) != rarity) continue;
            if (requiredPower && entry.Type != EffectCardType.Power) continue;
            if ((group.UsedTargetMask & (BigInteger.One << entry.TargetIndex)) != 0) continue;
            targetCount++;
            if (entry.Type == EffectCardType.Power) targetPowerCount++;
            yield return (entry.TargetIndex, entry.Type == EffectCardType.Power, 1);
        }

        int totalAvailable = pool.TotalByRarity[rarity] - UsedTotal(group, rarity);
        int powerAvailable = pool.PowerByRarity[rarity] - UsedPower(group, rarity);
        if (requiredPower)
        {
            int otherPower = powerAvailable - targetPowerCount;
            if (otherPower > 0) yield return (-1, true, otherPower);
            yield break;
        }

        int otherPowerNormal = powerAvailable - targetPowerCount;
        int otherNonPower = totalAvailable - targetCount - otherPowerNormal;
        if (otherPowerNormal > 0) yield return (-1, true, otherPowerNormal);
        if (otherNonPower > 0) yield return (-1, false, otherNonPower);
    }

    private static CardGroupState ConsumeCard(CardGroupState group, int rarity, bool power, int targetIndex)
    {
        group = rarity switch
        {
            0 => group with { UsedCommon = group.UsedCommon + 1 },
            1 => group with { UsedUncommon = group.UsedUncommon + 1 },
            _ => group with { UsedRare = group.UsedRare + 1 }
        };
        if (power)
        {
            group = rarity switch
            {
                0 => group with { UsedCommonPower = group.UsedCommonPower + 1 },
                1 => group with { UsedUncommonPower = group.UsedUncommonPower + 1 },
                _ => group with { UsedRarePower = group.UsedRarePower + 1 }
            };
        }
        if (targetIndex >= 0) group = group with { UsedTargetMask = group.UsedTargetMask | (BigInteger.One << targetIndex) };
        return group;
    }

    private static IReadOnlyList<PotionOutcome> GeneratePotionOutcomes(PotionPoolModel pool, int potionStep, bool forced)
    {
        double dropProbability = forced ? 1d : Math.Clamp(potionStep / 10d, 0d, 1d);
        int dropStep = forced ? potionStep : Math.Max(0, potionStep - 1);
        int noDropStep = forced ? potionStep : Math.Min(10, potionStep + 1);
        var output = new List<PotionOutcome>();
        if (!forced && dropProbability < 1d)
            output.Add(new PotionOutcome(false, -1, noDropStep, 1d - dropProbability));
        if (dropProbability <= 0d) return output;

        double[] rarityMass = { 0.65d, 0.25d, 0.10d };
        for (int rarity = 0; rarity < 3; rarity++)
        {
            int total = pool.TotalByRarity[rarity];
            if (total <= 0) continue;
            int targetInRarity = 0;
            foreach (KeyValuePair<ModelKey, (int Rarity, int TargetIndex)> pair in pool.TargetInfoByKey)
            {
                (int targetRarity, int targetIndex) = pair.Value;
                if (targetRarity != rarity) continue;
                targetInRarity++;
                output.Add(new PotionOutcome(true, targetIndex, dropStep,
                    dropProbability * rarityMass[rarity] / total));
            }
            int other = total - targetInRarity;
            if (other > 0)
                output.Add(new PotionOutcome(true, -1, dropStep,
                    dropProbability * rarityMass[rarity] * other / total));
        }
        return output;
    }

    private static IReadOnlyList<(int Gold, double Probability)> GenerateGoldOutcomes(int ascension, int fixedGold)
    {
        int min = ascension >= 3 ? 7 : 10;
        int max = ascension >= 3 ? 15 : 20;
        double mass = 1d / (max - min + 1);
        return Enumerable.Range(min, max - min + 1)
            .Select(value => (value + fixedGold, mass))
            .ToArray();
    }

    private static bool TryApplyBattleMatcher(
        MatcherContext matcher,
        int battleOrdinal,
        BigInteger cardMask,
        PotionOutcome potion,
        int gold,
        BigInteger existingAnyMask,
        ulong existingCardAssignmentStates,
        ulong existingPotionAssignmentStates,
        out BigInteger anyMask,
        out ulong cardAssignmentStates,
        out ulong potionAssignmentStates)
    {
        anyMask = existingAnyMask;
        cardAssignmentStates = existingCardAssignmentStates;
        potionAssignmentStates = existingPotionAssignmentStates;
        for (int index = 0; index < matcher.Conditions.Length; index++)
        {
            NormalCombatRewardSearchCondition condition = matcher.Conditions[index];
            if (condition.BattleOrdinal == 0 && battleOrdinal > 3) continue;
            if (condition.BattleOrdinal != 0 && condition.BattleOrdinal != battleOrdinal) continue;
            bool matched = MatchesCondition(matcher, condition, cardMask, potion, gold);
            if (condition.BattleOrdinal == battleOrdinal)
            {
                if (!matched) return false;
            }
            else if (condition.BattleOrdinal == 0 && matched && matcher.AnyConditionBitByIndex.TryGetValue(index, out int bit))
            {
                anyMask |= BigInteger.One << bit;
            }
        }

        foreach (ModernCardConstraint constraint in matcher.ModernCards.Where(item => item.OrderedBattleOrdinal == battleOrdinal))
        {
            if (!MatchesCardTarget(cardMask, constraint.Target, matcher.CardTargetIndex)) return false;
        }
        foreach (ModernPotionConstraint constraint in matcher.ModernPotions.Where(item => item.OrderedBattleOrdinal == battleOrdinal))
        {
            if (!MatchesPotionConstraint(potion, constraint.Slot, matcher.PotionTargetIndex)) return false;
        }

        ModernCardConstraint[] unorderedCards = matcher.ModernCards.Where(item => !item.OrderedBattleOrdinal.HasValue).ToArray();
        if (unorderedCards.Length > 0 && battleOrdinal <= matcher.ModernCardWindowCount)
        {
            cardAssignmentStates = AdvanceAssignmentStates(
                existingCardAssignmentStates,
                unorderedCards.Length,
                Math.Max(0, matcher.ModernCardWindowCount - battleOrdinal),
                constraintIndex => MatchesCardTarget(cardMask, unorderedCards[constraintIndex].Target, matcher.CardTargetIndex));
            if (cardAssignmentStates == 0) return false;
        }
        ModernPotionConstraint[] unorderedPotions = matcher.ModernPotions.Where(item => !item.OrderedBattleOrdinal.HasValue).ToArray();
        if (unorderedPotions.Length > 0 && battleOrdinal <= matcher.ModernPotionWindowCount)
        {
            potionAssignmentStates = AdvanceAssignmentStates(
                existingPotionAssignmentStates,
                unorderedPotions.Length,
                Math.Max(0, matcher.ModernPotionWindowCount - battleOrdinal),
                constraintIndex => MatchesPotionConstraint(potion, unorderedPotions[constraintIndex].Slot, matcher.PotionTargetIndex));
            if (potionAssignmentStates == 0) return false;
        }
        return true;
    }

    private static ulong AdvanceAssignmentStates(ulong states, int requirementCount,
        int remainingBattles, Func<int, bool> matches)
    {
        ulong next = 0;
        int maxSubset = 1 << requirementCount;
        for (int subset = 0; subset < maxSubset; subset++)
        {
            if ((states & (1UL << subset)) == 0) continue;
            // One battle can fill at most one requirement, including repeated
            // targets. Keep each reachable subset only if its own window can
            // still complete it; the subsets remain alternatives, not masses.
            int assigned = BitOperations.PopCount((uint)subset);
            if (assigned + remainingBattles >= requirementCount)
                next |= 1UL << subset;
            if (assigned + 1 + remainingBattles < requirementCount) continue;
            for (int requirement = 0; requirement < requirementCount; requirement++)
            {
                if ((subset & (1 << requirement)) != 0 || !matches(requirement)) continue;
                int expanded = subset | (1 << requirement);
                next |= 1UL << expanded;
            }
        }
        return next;
    }

    private static bool AssignmentComplete(ulong states, int requirementCount)
    {
        if (requirementCount <= 0) return true;
        int fullSubset = (1 << requirementCount) - 1;
        return (states & (1UL << fullSubset)) != 0;
    }

    private static bool MatchesCardTarget(
        BigInteger cardMask,
        ModelKey target,
        IReadOnlyDictionary<ModelKey, int> indexes) =>
        HasBit(cardMask, indexes, target);

    private static bool MatchesPotionConstraint(
        PotionOutcome potion,
        CombatPotionRewardSlotSearchCondition slot,
        IReadOnlyDictionary<ModelKey, int> indexes) => slot.Requirement switch
    {
        CombatPotionSlotRequirement.Neutral => true,
        CombatPotionSlotRequirement.NoDrop => !potion.Generated,
        CombatPotionSlotRequirement.DropAny => potion.Generated,
        CombatPotionSlotRequirement.DropSpecific => potion.Generated && slot.PotionKey.HasValue &&
            indexes.TryGetValue(slot.PotionKey.Value, out int targetIndex) && targetIndex == potion.TargetIndex,
        _ => false
    };

    private static bool MatchesCondition(
        MatcherContext matcher,
        NormalCombatRewardSearchCondition condition,
        BigInteger cardMask,
        PotionOutcome potion,
        int gold)
    {
        if (!MatchesMaskFilter(cardMask, condition.Cards, matcher.CardTargetIndex)) return false;
        if (condition.PotionRequirement == NormalCombatPotionRequirement.MustDrop && !potion.Generated) return false;
        if (condition.PotionRequirement == NormalCombatPotionRequirement.MustNotDrop && potion.Generated) return false;
        if (!condition.Potions.IsEmpty)
        {
            if (!potion.Generated) return false;
            if (!MatchesSingletonFilter(potion.TargetIndex, condition.Potions, matcher.PotionTargetIndex)) return false;
        }
        if (condition.MinimumGold.HasValue && gold < condition.MinimumGold.Value) return false;
        if (condition.MaximumGold.HasValue && gold > condition.MaximumGold.Value) return false;
        return true;
    }

    private static bool MatchesMaskFilter(
        BigInteger observed,
        ModelKeySetFilter filter,
        IReadOnlyDictionary<ModelKey, int> indexes)
    {
        if (filter.Any.Count > 0 && !filter.Any.Any(key => HasBit(observed, indexes, key))) return false;
        if (filter.All.Any(key => !HasBit(observed, indexes, key))) return false;
        if (filter.Ban.Any(key => HasBit(observed, indexes, key))) return false;
        return true;
    }

    private static bool MatchesSingletonFilter(
        int selectedTargetIndex,
        ModelKeySetFilter filter,
        IReadOnlyDictionary<ModelKey, int> indexes)
    {
        bool Contains(ModelKey key) => indexes.TryGetValue(key, out int index) && index == selectedTargetIndex;
        if (filter.Any.Count > 0 && !filter.Any.Any(Contains)) return false;
        if (filter.All.Any(key => !Contains(key))) return false;
        if (filter.Ban.Any(Contains)) return false;
        return true;
    }

    private static bool HasBit(BigInteger mask, IReadOnlyDictionary<ModelKey, int> indexes, ModelKey key) =>
        indexes.TryGetValue(key, out int index) && (mask & (BigInteger.One << index)) != 0;

    private static bool TryBuildMatcherContext(
        NormalCombatRewardSearchCondition[] conditions,
        CombatCardRewardSequenceSearchCondition? cardSequence,
        CombatPotionRewardSequenceSearchCondition? potionSequence,
        out MatcherContext context,
        out string issue)
    {
        issue = string.Empty;
        var cardIndex = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        var potionIndex = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        foreach (ModelKey key in conditions.SelectMany(condition => condition.Cards.Any.Concat(condition.Cards.All).Concat(condition.Cards.Ban)))
        {
            if (key.IsValid && !cardIndex.ContainsKey(key)) cardIndex[key] = cardIndex.Count;
        }
        foreach (ModelKey key in cardSequence?.Slots.Where(key => key.HasValue).Select(key => key!.Value) ?? Array.Empty<ModelKey>())
        {
            if (key.IsValid && !cardIndex.ContainsKey(key)) cardIndex[key] = cardIndex.Count;
        }
        foreach (ModelKey key in conditions.SelectMany(condition => condition.Potions.Any.Concat(condition.Potions.All).Concat(condition.Potions.Ban)))
        {
            if (key.IsValid && !potionIndex.ContainsKey(key)) potionIndex[key] = potionIndex.Count;
        }
        foreach (ModelKey key in potionSequence?.Slots
                     .Where(slot => slot.Requirement == CombatPotionSlotRequirement.DropSpecific && slot.PotionKey.HasValue)
                     .Select(slot => slot.PotionKey!.Value) ?? Array.Empty<ModelKey>())
        {
            if (key.IsValid && !potionIndex.ContainsKey(key)) potionIndex[key] = potionIndex.Count;
        }

        ModernCardConstraint[] modernCards = cardSequence is { IsEmpty: false }
            ? cardSequence.Slots.Select((key, index) => (key, index))
                .Where(item => item.key.HasValue)
                .Select(item => new ModernCardConstraint(
                    item.key!.Value,
                    cardSequence.OrderMode == CombatRewardSequenceOrderMode.Ordered ? item.index + 1 : null))
                .ToArray()
            : Array.Empty<ModernCardConstraint>();
        ModernPotionConstraint[] modernPotions = potionSequence is { IsEmpty: false }
            ? potionSequence.Slots.Select((slot, index) => (slot, index))
                .Where(item => !item.slot.IsNeutral)
                .Select(item => new ModernPotionConstraint(
                    item.slot,
                    potionSequence.OrderMode == CombatRewardSequenceOrderMode.Ordered ? item.index + 1 : null))
                .ToArray()
            : Array.Empty<ModernPotionConstraint>();

        var anyBits = new Dictionary<int, int>();
        int nextBit = 0;
        for (int index = 0; index < conditions.Length; index++)
            if (conditions[index].BattleOrdinal == 0) anyBits[index] = nextBit++;
        BigInteger requiredAny = nextBit == 0 ? BigInteger.Zero : (BigInteger.One << nextBit) - 1;

        int legacyMaximum = conditions.Length == 0
            ? 0
            : conditions.Any(condition => condition.BattleOrdinal == 0)
                ? 3
                : Math.Clamp(conditions.Max(condition => condition.BattleOrdinal), 1, BattleCount);
        int maximumBattleOrdinal = Math.Max(
            legacyMaximum,
            Math.Max(cardSequence is { IsEmpty: false } ? cardSequence.Count : 0,
                     potionSequence is { IsEmpty: false } ? potionSequence.Count : 0));
        maximumBattleOrdinal = Math.Clamp(maximumBattleOrdinal, 1, BattleCount);

        context = new MatcherContext(
            conditions,
            modernCards,
            modernPotions,
            cardIndex,
            potionIndex,
            anyBits,
            requiredAny,
            maximumBattleOrdinal,
            conditions.Any(condition => !condition.Cards.IsEmpty) || modernCards.Length > 0,
            conditions.Any(condition => condition.PotionRequirement != NormalCombatPotionRequirement.Any || !condition.Potions.IsEmpty) || modernPotions.Length > 0,
            conditions.Any(condition => condition.MinimumGold.HasValue || condition.MaximumGold.HasValue),
            modernCards.Count(item => !item.OrderedBattleOrdinal.HasValue),
            modernPotions.Count(item => !item.OrderedBattleOrdinal.HasValue),
            cardSequence is { IsEmpty: false } ? cardSequence.Count : 0,
            potionSequence is { IsEmpty: false } ? potionSequence.Count : 0);
        return true;
    }

    private static bool TryBuildCardPool(
        NeowEffectAuthoritySnapshot authority,
        IReadOnlyDictionary<ModelKey, int> targetIndexes,
        bool multiplayer,
        out CardPoolModel model,
        out string issue)
    {
        issue = string.Empty;
        model = default!;
        NeowEffectCardSnapshot[] source = authority.CharacterRewardPool!
            .Where(card => multiplayer || !card.IsMultiplayerOnly)
            .Where(card => card.EligibleForPostCombatRewardByPoolMembership)
            .Where(card => card.IsUnlockedInCapturedPool)
            .Where(card => card.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .OrderBy(card => card.PoolOrder)
            .ToArray();
        if (source.Length == 0)
        {
            issue = "CharacterRewardPoolEmpty";
            return false;
        }
        if (source.GroupBy(card => card.CardKey, ModelKeyComparer.Instance).Any(group => group.Count() != 1))
        {
            issue = "CharacterRewardPoolDuplicateIdentity";
            return false;
        }

        var entries = new List<CardPoolEntry>(source.Length);
        int[] total = new int[3];
        int[] power = new int[3];
        foreach (NeowEffectCardSnapshot card in source)
        {
            int rarity = RarityIndex(card.Rarity);
            if (rarity < 0) continue;
            total[rarity]++;
            if (card.CardType == EffectCardType.Power) power[rarity]++;
            entries.Add(new CardPoolEntry(
                card.CardKey,
                card.Rarity,
                card.CardType,
                targetIndexes.TryGetValue(card.CardKey, out int targetIndex) ? targetIndex : -1));
        }
        // Unlike Potion identity generation, card generation has an audited rarity
        // fallback chain. A partially unlocked save may therefore have an empty
        // rarity bucket without making the Reward identity distribution unknown;
        // NextAvailableRarity below mirrors the Product fallback instead of
        // requiring every Common/Uncommon/Rare bucket to be populated.
        if (entries.Count == 0)
        {
            issue = "CharacterRewardPoolEmpty";
            return false;
        }

        model = new CardPoolModel(entries.ToArray(), total, power, targetIndexes, targetIndexes.Count);
        return true;
    }

    private static bool TryBuildPotionPool(
        NeowEffectAuthoritySnapshot authority,
        IReadOnlyDictionary<ModelKey, int> targetIndexes,
        bool multiplayer,
        out PotionPoolModel model,
        out string issue)
    {
        issue = string.Empty;
        model = default!;
        NeowEffectPotionSnapshot[] source = authority.PotionPool!
            .Where(potion => multiplayer || !potion.IsMultiplayerOnly)
            .Where(potion => potion.Rarity is EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)
            .OrderBy(potion => potion.PoolOrder)
            .ToArray();
        if (source.GroupBy(potion => potion.PotionKey, ModelKeyComparer.Instance).Any(group => group.Count() != 1))
        {
            issue = "PotionPoolDuplicateIdentity";
            return false;
        }
        int[] total = new int[3];
        var targets = new Dictionary<ModelKey, (int Rarity, int TargetIndex)>(ModelKeyComparer.Instance);
        foreach (NeowEffectPotionSnapshot potion in source)
        {
            int rarity = PotionRarityIndex(potion.Rarity);
            if (rarity < 0) continue;
            total[rarity]++;
            if (targetIndexes.TryGetValue(potion.PotionKey, out int targetIndex))
                targets[potion.PotionKey] = (rarity, targetIndex);
        }
        if (total.Any(count => count <= 0))
        {
            issue = "PotionRarityBucketEmpty";
            return false;
        }
        model = new PotionPoolModel(total, targets, targetIndexes.Count);
        return true;
    }

    private static int RarityIndex(EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => 0,
        EffectCardRarity.Uncommon => 1,
        EffectCardRarity.Rare => 2,
        _ => -1
    };

    private static int PotionRarityIndex(EffectPotionRarity rarity) => rarity switch
    {
        EffectPotionRarity.Common => 0,
        EffectPotionRarity.Uncommon => 1,
        EffectPotionRarity.Rare => 2,
        _ => -1
    };

    private static int UsedTotal(CardGroupState group, int rarity) => rarity switch
    {
        0 => group.UsedCommon,
        1 => group.UsedUncommon,
        _ => group.UsedRare
    };

    private static int UsedPower(CardGroupState group, int rarity) => rarity switch
    {
        0 => group.UsedCommonPower,
        1 => group.UsedUncommonPower,
        _ => group.UsedRarePower
    };

    private static double Clamp01(double value) => Math.Clamp(value, 0d, 1d);

    private static void AddMass<TKey>(IDictionary<TKey, double> target, TKey key, double mass) where TKey : notnull
    {
        if (mass <= 0d) return;
        target[key] = target.TryGetValue(key, out double existing) ? existing + mass : mass;
    }
}
