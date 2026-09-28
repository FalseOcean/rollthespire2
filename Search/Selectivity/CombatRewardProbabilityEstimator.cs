using System.Numerics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Product-projected probability authority for the first six normal-combat card and potion rewards.
/// Legacy any-battle rows retain their three-battle window.
/// It prices the portion of real seed space that is compatible with the current
/// Query-literal Search projection; it is not a complete latent-game-world rarity model.
/// The chronological DP mirrors the accepted Beta110 reward-generation topology:
/// potion pity, card-rarity pity, per-card-reward no-duplicate selection, Prayer Wheel,
/// White Beast Statue, Lasting Candy and deterministic opening fixed-gold effects.
///
/// In the inherited solo model, latent nested outcomes for an explicit Capsule are used only to
/// measure projection compatibility. Unsupported implicit Reward-influence branches are
/// excluded from searchable probability instead of being re-evaluated under their own
/// latent Reward contexts. Query-authored opening RNG consumption is a deterministic part
/// of the projected C continuation and is replayed by the Fast evaluator; it is not a
/// probability compatibility penalty. Bones-unspecified children are not expanded into
/// hidden Capsule routes in that solo model. Multiplayer instead integrates the actual
/// held reward effects of Capsule draws and unspecified Bones companions, conditioned
/// on authored opening/bag predicates; unavailable effects retain Unknown probability.
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

    private sealed record RewardInfluenceMass(RewardInfluenceProfile Profile, double Mass, string Evidence);

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

    private readonly record struct CapsuleDrawPosition(int Lane, int Ordinal);
    private readonly record struct CapsuleRarityChoice(int PreferredLane, double Probability, string Label);
    private sealed record CapsuleGroup(ModelKey Source, int DrawCount, IReadOnlyList<NeowStructuredEffectSearchCondition> Constraints);

    private enum CombatRewardProbabilityMode : byte
    {
        ProductProjected = 0,
        PhysicalFastGate = 1
    }

    public static SearchSelectivityEstimate Estimate(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return EstimateCore(
            plan,
            Beta110CombatRewardExplicitContextProjector.Project(plan.CompiledSearch),
            CombatRewardProbabilityMode.ProductProjected);
    }

    /// <summary>
    /// Analytic survival of the concrete query-literal C Fast gate. Unlike Product
    /// Probability this does not multiply projection-compatibility mass, because the
    /// physical C Action cannot observe hidden Capsule outcomes and therefore really
    /// does keep/reject solely from its synthetic ExplicitRewardContext projection.
    /// </summary>
    public static SearchSelectivityEstimate EstimateExplicitFastProjection(SearchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EstimateCore(
            SearchSelectivityInput.From(request),
            Beta110CombatRewardExplicitContextProjector.Project(request.CompiledSearch),
            CombatRewardProbabilityMode.PhysicalFastGate);
    }

    // Bounded C2 bridge. The legacy "PhysicalFastGate" name is insufficient:
    // its modern unordered matcher still uses distinct-battle assignment.
    internal static SearchSelectivityEstimate EstimateFamilySieve(
        ExactSearchExecutionRequest request, Beta110CombatRewardFastPlan fast)
    {
        int? prefixDraws = FixedAuthoredPrefixDrawCount(request, fast);
        bool neutralPrefix = prefixDraws.HasValue;
        var query = request.CompiledSearch.NormalizedQuery;
        var cardSequence = query.CombatCardRewards;
        bool repeatedExistential = !fast.HasDistinctBattleAssignments && (neutralPrefix || !fast.OpeningConsumption.HasReplay) &&
            fast.PredicateCount is >= 1 and <= 6 && fast.Predicates.All(p => p.IsAnyBattle &&
                p.CardAny.Length == 1 && p.CardAll.Length == 0 && p.CardBan.Length == 0 &&
                !p.HasPotionDropPredicate && !p.HasPotionIdentityPredicate && !p.HasGoldPredicate) &&
            fast.Predicates.Select(p => p.CardAny[0]).Distinct().Count() == 1 &&
            cardSequence is { OrderMode: CombatRewardSequenceOrderMode.Unordered } &&
            cardSequence.Slots.Where(k => k.HasValue).Select(k => k!.Value).Distinct().Count() == 1 &&
            query.LegacyCombatRewardConstraints.Count == 0;
        string? issue = fast.OpeningConsumption.HasReplay && !neutralPrefix ? "AuthoredPrefixDistribution" :
            fast.Predicates.Any(p => p.IsAnyBattle) && !repeatedExistential && !fast.HasDistinctBattleAssignments ? "UnorderedFastExistentialsNotDistinctAssignment" :
            fast.PredicateCount > 6 ? "BoundedMatcherCapacity" : null;
        int domains = (fast.CardPredicateCount > 0 ? 1 : 0) +
            (fast.PotionDropPredicateCount + fast.PotionIdentityPredicateCount > 0 ? 1 : 0) +
            (fast.GoldPredicateCount > 0 ? 1 : 0);
        int targetCount = fast.Predicates.SelectMany(p => p.CardAny.Concat(p.CardAll).Concat(p.CardBan)
            .Concat(p.PotionAny).Concat(p.PotionAll).Concat(p.PotionBan)).Distinct().Count();
        if (targetCount > 6) issue = "BoundedTargetDpCapacity";
        if (domains > 1) issue = "SharedRewardsJointDistribution";
        if ((fast.ExplicitContext.InfluenceFlags & (Beta110CombatRewardInfluenceFlags.UnknownRewardImpact |
             Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation)) != 0)
            issue = "UnknownImpactIsNotFamilyKeep";
        if (issue is not null)
            return SearchSelectivityEstimate.Unpriced("C2.SieveUnknown." + issue, issue);
        // Ordered, single-domain predicates have the same matcher in C and this
        // chronological DP. No latent compatibility multiplier or route union.
        var input = SearchSelectivityInput.From(request);
        if (repeatedExistential)
        {
            // C's identical existential rows collapse to one observation over
            // all simulated battles. This is a probability-only matcher
            // projection, NOT a change to authored Query/Exact multiplicity.
            ModelKey target = cardSequence!.Slots.First(k => k.HasValue)!.Value;
            var matcherQuery = query with { CombatCardRewards = new(fast.MaximumBattleOrdinal,
                CombatRewardSequenceOrderMode.Unordered, Enumerable.Range(0, fast.MaximumBattleOrdinal).Select(i => i == 0 ? (ModelKey?)target : null).ToArray()) };
            input = SearchSelectivityInput.From(SearchCompiler.Compile(matcherQuery, request.CompiledSearch.Context));
        }
        var result = EstimateCore(input, fast.ExplicitContext, CombatRewardProbabilityMode.PhysicalFastGate);
        return result with { EvidenceCode = (neutralPrefix ? $"C.FixedAuthoredPrefixDraws={prefixDraws};" : "") +
            (repeatedExistential ? "C.IdenticalExistentialWindowUnion;" : "") + result.EvidenceCode };
    }

    // Mirrors the donor's zero-consumption branch and its single forced-Rare
    // SelectUnused(empty) call: exactly one NextInt for a nonempty pool. Their
    // order cannot change the ending Rewards state. The ordinary uniform-draw
    // probability model therefore needs no two-route union or learned offset.
    // More involved authored generators retain Unknown. These three known
    // reward modifiers run after the fixed prefix; EstimateCore already models
    // their card groups, potion decision and Candy counter. They are not neutral.
    internal static bool HasModeledRewardImpact(Beta110CombatRewardExplicitContext context) =>
        context.AdditionalCardRewardCount <= 1 && context.FixedGoldAmount == 0 &&
        (context.InfluenceFlags & ~(Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward |
            Beta110CombatRewardInfluenceFlags.ForcePotionReward |
            Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard |
            Beta110CombatRewardInfluenceFlags.DeterministicUpgradeOnly |
            Beta110CombatRewardInfluenceFlags.DeterministicEnchantmentOnly)) == 0;

    internal static int? FixedAuthoredPrefixDrawCount(ExactSearchExecutionRequest request, Beta110CombatRewardFastPlan fast)
    {
        // Query-literal C replays exactly three rarity rolls for fixed Small+Large,
        // independent of pickup order. It does not execute the latent bag outputs.
        // Subsequent reward draws use the existing disjoint-draw independence model.
        if (request.ProfileId == RuntimeProfileId.Beta111 && request.Authority.CanUseCurrentModel &&
            request.Authority.PlayersCount == 1 && request.Authority.NoRunModifiers == true &&
            fast.OpeningConsumption.ReplayBonesOffer && fast.OpeningConsumption.OrderedRelicIds.Length == 2 &&
            fast.OpeningConsumption.OrderedRelicIds.ToHashSet().SetEquals([Beta110FastRelicCatalog.SmallCapsule, Beta110FastRelicCatalog.LargeCapsule]) &&
            HasModeledRewardImpact(fast.ExplicitContext) &&
            request.Evaluation.StructuredNeowEffects.All(c => c.Scope == NeowStructuredEffectScope.FinalCurse || NeowReplayPlan.IsCapsule(c)))
            return 3;
        if (!(request.ProfileId == RuntimeProfileId.Beta111 && request.Authority.CanUseCurrentModel && request.Authority.NoRunModifiers == true &&
        fast.OpeningConsumption.HasReplay && fast.OpeningConsumption.OrderedRelicIds.Length is 1 or 2 &&
        fast.OpeningConsumption.OrderedRelicIds.All(id => id <= Beta110FastRelicCatalog.Pomander && id is not (
            Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule or
            Beta110FastRelicCatalog.HeftyTablet or
            Beta110FastRelicCatalog.LeadPaperweight or Beta110FastRelicCatalog.LostCoffer or
            Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.ScrollBoxes)) &&
        request.Evaluation.StructuredNeowEffects.All(c => c.Scope == NeowStructuredEffectScope.FinalCurse))) return null;
        int draws = fast.OpeningConsumption.OrderedRelicIds.Count(id => id == Beta110FastRelicCatalog.ArcaneScroll);
        if (draws > 1 || draws == 1 && (request.Authority.EffectAuthority?.HasExactCharacterRewardPool != true ||
            !request.Authority.EffectAuthority.CharacterRewardPool!.Any(c => c.Rarity == EffectCardRarity.Rare))) return null;
        return draws;
    }

    private static SearchSelectivityEstimate EstimateCore(
        SearchSelectivityInput plan,
        Beta110CombatRewardExplicitContext explicitContext,
        CombatRewardProbabilityMode mode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(explicitContext);
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

        if (plan.Authority.PlayersCount > 1 && mode == CombatRewardProbabilityMode.ProductProjected)
            return EstimateParty(plan, semantic, matcher, cards, potions);

        if ((explicitContext.InfluenceFlags &
             (Beta110CombatRewardInfluenceFlags.UnknownRewardImpact |
              Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation)) != 0)
        {
            if (mode == CombatRewardProbabilityMode.PhysicalFastGate)
            {
                return SearchSelectivityEstimate.Exact(
                    1d,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.ExactRequestedConjunction,
                    SearchSelectivityDependencyClass.AssumedIndependent,
                    "PhysicalEffectiveness.Analytical.CombatRewardExplicitQueryProjection.ConservativeKeep",
                    "ExplicitRewardContext contains an unsupported Reward influence; current Fast projection conservatively keeps every candidate and leaves truth to Production Exact/Witness.",
                    new[] { "UnknownExplicitRewardInfluence=ConservativeKeep;Survival=1" });
            }

            return SearchSelectivityEstimate.Unpriced(
                "Probability.CombatReward.ProductProjection.UnknownExplicitInfluence",
                "Searchable probability is unpriced because the authored Reward context contains an unsupported influence.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        Beta110CombatRewardOpeningConsumptionProjection openingConsumption =
            Beta110CombatRewardOpeningConsumptionProjector.Project(plan.CompiledSearch);
        RewardInfluenceProfile projectedProfile = ProfileFromExplicitFastContext(explicitContext);
        double rewardSurvival = EstimateForProfile(plan.Ascension, cards, potions, matcher, projectedProfile);
        double projectionCompatibility = 1d;
        string influenceDetail;
        IReadOnlyList<SearchSelectivityDomain> conditionedDomains;
        SearchSelectivityDependencyClass dependencyClass;

        if (mode == CombatRewardProbabilityMode.ProductProjected)
        {
            // Owner policy: unspecified Bones grants do not add latent reward influences.
            // Keep authored influences below; this is a probability approximation only.
            if (!TryResolveInfluenceDistribution(plan, semantic, matcher, projectedProfile, out IReadOnlyList<RewardInfluenceMass> realProfiles,
                    out influenceDetail, out string influenceIssue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.CombatReward.ProductProjection:" + influenceIssue,
                    influenceDetail,
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent,
                    new[]
                    {
                        "ProbabilityModel=ProductProjectedSearchableMass",
                        "UnsupportedImplicitRewardInfluenceIsExcludedRatherThanIntegrated",
                        "BonesUnspecifiedChildrenAreNotExpandedIntoLatentCapsuleRoutes"
                    });
            }

            projectionCompatibility = Math.Clamp(
                realProfiles.Where(item => item.Profile == projectedProfile).Sum(item => item.Mass),
                0d,
                1d);

            bool conditionedOnNeow = semantic.Routes.CombatReward.Kind == ResolvedCombatRewardRouteKind.PinnedOpeningRoute;
            bool conditionedOnRelic = conditionedOnNeow &&
                                      CapsuleRelicProbabilityEstimator.HasOverlappingRelicSequenceConstraint(plan) &&
                                      HasExplicitCapsuleRoute(semantic);
            conditionedDomains = conditionedOnRelic
                ? new[] { SearchSelectivityDomain.Neow, SearchSelectivityDomain.Relic }
                : conditionedOnNeow
                    ? new[] { SearchSelectivityDomain.Neow }
                    : Array.Empty<SearchSelectivityDomain>();
            dependencyClass = conditionedOnNeow
                ? SearchSelectivityDependencyClass.RouteDependent
                : SearchSelectivityDependencyClass.AssumedIndependent;
        }
        else
        {
            influenceDetail = "PhysicalFastGate;ExplicitRewardContext=" +
                              (explicitContext.SourceRelicKeys.Length == 0
                                  ? "{}"
                                  : "{" + string.Join(',', explicitContext.SourceRelicKeys.Select(key => key.Serialized)) + "}") +
                              ";ProjectionCompatibilityNotAppliedToPhysicalGate=true";
            conditionedDomains = Array.Empty<SearchSelectivityDomain>();
            dependencyClass = SearchSelectivityDependencyClass.AssumedIndependent;
        }

        double probability = Math.Clamp(projectionCompatibility * rewardSurvival, 0d, 1d);
        string evidenceCode = mode == CombatRewardProbabilityMode.ProductProjected
            ? "Probability.Authority.CombatRewardProductProjected"
            : "PhysicalEffectiveness.Analytical.CombatRewardExplicitQueryProjection";
        string notes =
            $"Beta110 first-three normal-combat Reward projection; context={projectedProfile.Label};" +
            $"openingReplay={openingConsumption.Fingerprint};openingReplayRequired={openingConsumption.HasReplay};" +
            $"compatibility={projectionCompatibility:G17};rewardSurvival={rewardSurvival:G17};" +
            $"searchable={probability:G17};Influence={influenceDetail}";

        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            dependencyClass,
            evidenceCode,
            notes,
            mode == CombatRewardProbabilityMode.ProductProjected
                ? new[]
                {
                    "ProbabilityModel=ProductProjectedSearchableMass",
                    "PlayerAuthoredQueryDefinesProjection=true",
                    "ProjectionCompatibilityMultipliesProjectedRewardSurvival=true",
                    "QueryLiteralOpeningConsumptionReplayIsDeterministic=true",
                    "ProjectedRewardSurvivalUsesSameOpeningReplayContract=true",
                    "OpeningReplayFingerprint=" + openingConsumption.Fingerprint,
                    "ProjectionCompatibilityDoesNotCompensateForRngContinuation=true",
                    "UnsupportedImplicitRewardInfluenceBranchesExcluded=true",
                    "BonesUnspecifiedChildrenNotExpanded=true",
                    "UnspecifiedBonesRewardImpactIgnored=OwnerAcceptedApproximation",
                    "UnspecifiedCapsuleRewardImpactIgnored=OwnerAcceptedApproximation",
                    "ProductionExactAndSameRouteWitnessRemainRealRouteTruth=true"
                }
                : new[]
                {
                    "PhysicalFastGateOnly=true",
                    "QueryLiteralExplicitInfluenceOnly=true",
                    "QueryLiteralOpeningConsumptionReplayIsDeterministic=true",
                    "OpeningReplayFingerprint=" + openingConsumption.Fingerprint,
                    "ProjectionCompatibilityNotApplied=true",
                    "ProductionExactAndSameRouteWitnessRemainRealRouteTruth=true"
                },
            conditionedOnDomains: conditionedDomains);
    }

    private static RewardInfluenceProfile ProfileFromExplicitFastContext(
        Beta110CombatRewardExplicitContext context) => new(
        PrayerWheel: (context.InfluenceFlags & Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward) != 0 ||
                     context.AdditionalCardRewardCount > 0,
        WhiteBeastStatue: (context.InfluenceFlags & Beta110CombatRewardInfluenceFlags.ForcePotionReward) != 0,
        LastingCandy: (context.InfluenceFlags & Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard) != 0,
        AmethystAubergine: (context.InfluenceFlags & Beta110CombatRewardInfluenceFlags.AmethystAubergineFixedGold) != 0 ||
                           context.FixedGoldAmount > 0);

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
                constraintIndex => MatchesCardTarget(cardMask, unorderedCards[constraintIndex].Target, matcher.CardTargetIndex));
        }
        ModernPotionConstraint[] unorderedPotions = matcher.ModernPotions.Where(item => !item.OrderedBattleOrdinal.HasValue).ToArray();
        if (unorderedPotions.Length > 0 && battleOrdinal <= matcher.ModernPotionWindowCount)
        {
            potionAssignmentStates = AdvanceAssignmentStates(
                existingPotionAssignmentStates,
                unorderedPotions.Length,
                constraintIndex => MatchesPotionConstraint(potion, unorderedPotions[constraintIndex].Slot, matcher.PotionTargetIndex));
        }
        return true;
    }

    private static ulong AdvanceAssignmentStates(ulong states, int requirementCount, Func<int, bool> matches)
    {
        ulong next = states;
        int maxSubset = 1 << requirementCount;
        for (int subset = 0; subset < maxSubset; subset++)
        {
            if ((states & (1UL << subset)) == 0) continue;
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

    private static bool TryResolveInfluenceDistribution(
        SearchSelectivityInput plan,
        ProbabilitySemanticView semantic,
        MatcherContext matcher,
        RewardInfluenceProfile authoredProfile,
        out IReadOnlyList<RewardInfluenceMass> distribution,
        out string detail,
        out string issue)
    {
        distribution = Array.Empty<RewardInfluenceMass>();
        detail = string.Empty;
        issue = string.Empty;

        if (semantic.Routes.CombatReward.Kind == ResolvedCombatRewardRouteKind.NeutralNonPerturbingContinuation)
        {
            distribution = new[] { new RewardInfluenceMass(default, 1d, "UnpinnedNeutralRewardRoute") };
            detail = "ResolvedRouteSemantics=NeutralNonPerturbingContinuation;latentNeowRoutesNotEnumerated";
            return true;
        }
        if (semantic.Routes.CombatReward.Kind != ResolvedCombatRewardRouteKind.PinnedOpeningRoute ||
            semantic.NormalizedQuery.OpeningRoute is not { IsValid: true } route ||
            semantic.Routes.CombatReward.PinnedOpeningRouteKey != route.RouteRelicKey)
        {
            issue = "ResolvedCombatRewardRouteNotPriceable:" + semantic.Routes.CombatReward.Kind;
            detail = "Combat Reward probability follows ResolvedRouteSemantics and does not infer a route from the legacy execution contract.";
            return false;
        }

        RewardInfluenceProfile deterministic = default;
        if (IsRelevantImpactRelic(route.RouteRelicKey, matcher))
        {
            if (!HasExplicitRewardInfluenceRelation(semantic, route.RouteRelicKey))
            {
                issue = "CanonicalStateInfluenceMissing:" + route.RouteRelicKey.Serialized;
                detail = "An explicitly selected deterministic Reward-influence relic must be registered by Canonical StateInfluence.";
                return false;
            }
            deterministic = deterministic.Add(route.RouteRelicKey);
        }

        if (route.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones && !IsCapsule(route.RouteRelicKey))
        {
            distribution = new[] { new RewardInfluenceMass(deterministic, 1d, "PinnedDirectRoute") };
            detail = "PinnedDirectRoute:" + deterministic.Label;
            return true;
        }

        ModelKey[] explicitBonesRelics = ExplicitBonesRelics(semantic.NumericalFilter);
        foreach (ModelKey relic in explicitBonesRelics)
        {
            if (!IsRelevantImpactRelic(relic, matcher)) continue;
            if (!HasExplicitRewardInfluenceRelation(semantic, relic))
            {
                issue = "CanonicalStateInfluenceMissing:" + relic.Serialized;
                detail = "An explicitly constrained deterministic Reward-influence relic must be registered by Canonical StateInfluence.";
                return false;
            }
            deterministic = deterministic.Add(relic);
        }

        ModelKey[] capsuleSources;
        if (IsCapsule(route.RouteRelicKey))
        {
            capsuleSources = new[] { route.RouteRelicKey };
        }
        else
        {
            capsuleSources = explicitBonesRelics.Where(IsCapsule).Distinct(ModelKeyComparer.Instance).ToArray();
        }

        // Product route policy boundary: Bones alone does not authorize probability
        // to speculate that an unspecified grant may itself be a Capsule or another
        // reward-affecting relic. Explicit Capsule results are handled below.
        if (capsuleSources.Length == 0)
        {
            distribution = new[] { new RewardInfluenceMass(deterministic, 1d, "PinnedRouteNoExplicitCapsule") };
            detail = "PinnedRouteNoExplicitCapsule:" + deterministic.Label;
            return true;
        }
        if (capsuleSources.Length > 2 || capsuleSources.Distinct(ModelKeyComparer.Instance).Count() != capsuleSources.Length)
        {
            issue = "CapsuleSourceMultiplicityUnsupported";
            detail = "The selected opening route contains an invalid Capsule source multiplicity.";
            return false;
        }

        // Owner policy: empty Capsule outputs do not contribute latent influences
        // to P(C). The canonical explicit context already includes authored nested
        // targets (including grouped targets). R still prices those targets and bag
        // constraints separately; this must not change actual replay or Exact.
        distribution = new[] { new RewardInfluenceMass(authoredProfile, 1d, "AuthoredCapsuleInfluencesOnly") };
        detail = "AuthoredCapsuleInfluencesOnly:" + authoredProfile.Label + ";UnspecifiedOutputsIgnored=true";
        return true;
    }

    internal static bool TryEstimateCapsuleBagConjunction(SearchSelectivityInput plan,
        IReadOnlyList<ModelKey> capsuleSources, bool includeExplicitRelicSequence,
        out double probability, out string detail, out string issue)
    {
        var matcher = new MatcherContext([], [], [], new Dictionary<ModelKey, int>(),
            new Dictionary<ModelKey, int>(), new Dictionary<int, int>(), BigInteger.Zero,
            0, false, false, false, 0, 0, 0, 0);
        return TryBuildCapsuleInfluenceDistribution(plan, matcher, default, capsuleSources,
            out _, out detail, out issue, out probability, includeExplicitRelicSequence);
    }

    private static bool TryBuildCapsuleInfluenceDistribution(
        SearchSelectivityInput plan,
        MatcherContext matcher,
        RewardInfluenceProfile deterministic,
        IReadOnlyList<ModelKey> capsuleSources,
        out IReadOnlyList<RewardInfluenceMass> distribution,
        out string detail,
        out string issue,
        out double acceptedMass,
        bool includeExplicitRelicSequence = true)
    {
        acceptedMass = 0;
        distribution = Array.Empty<RewardInfluenceMass>();
        detail = string.Empty;
        issue = string.Empty;

        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out string poolIssue))
        {
            issue = "RuntimeRelicBagMissing:" + poolIssue;
            detail = "Capsule projection-compatibility pricing requires exact runtime player RelicGrabBag authority.";
            return false;
        }
        if (!TryBuildPlayerLanes(pool, denseByKey, out ModelKey[][] lanes, out string laneIssue))
        {
            issue = "RuntimeRelicLaneMissing:" + laneIssue;
            detail = "Capsule projection-compatibility pricing could not reconstruct the runtime Common/Uncommon/Rare lanes.";
            return false;
        }

        NeowStructuredEffectSearchCondition[] capsuleConstraints = ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects
            .Where(condition => !condition.IsEmpty && NeowReplayPlan.IsCapsule(condition) &&
                                condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                                condition.OutputKind == NeowStructuredOutputKind.Relic)
            .ToArray();
        var groupsBySource = new Dictionary<ModelKey, CapsuleGroup>(ModelKeyComparer.Instance);
        foreach (ModelKey source in capsuleSources)
        {
            int drawCount = source == BaseGameModelKeys.Relics.LargeCapsule ? 2 : 1;
            groupsBySource[source] = new CapsuleGroup(source, drawCount, capsuleConstraints.Where(item => item.SourceRelicKey == source).ToArray());
        }
        if (capsuleConstraints.Any(condition => !NeowReplayPlan.IsGroupedCapsule(condition) && !groupsBySource.ContainsKey(condition.SourceRelicKey)))
        {
            detail = "A nested Capsule predicate references a Capsule identity that is not explicitly present on the selected opening route.";
            return true;
        }
        var sourceFilter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        if (sourceFilter.RequireSmallCapsule && !groupsBySource.ContainsKey(BaseGameModelKeys.Relics.SmallCapsule) ||
            sourceFilter.RequireLargeCapsule && !groupsBySource.ContainsKey(BaseGameModelKeys.Relics.LargeCapsule) ||
            capsuleConstraints.Any(NeowReplayPlan.IsGroupedCapsule) && groupsBySource.Count != 2)
        { detail = "RequiredCapsuleSourceAbsent"; return true; }

        CapsuleGroup[][] routeGroupings;
        if (groupsBySource.Count <= 1)
        {
            routeGroupings = [groupsBySource.Values.ToArray()];
        }
        else
        {
            ModelKey[] pinnedOrder = ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder
                .Where(groupsBySource.ContainsKey)
                .Distinct(ModelKeyComparer.Instance)
                .ToArray();
            routeGroupings = pinnedOrder.Length == 2
                ? new[] { pinnedOrder.Select(key => groupsBySource[key]).ToArray() }
                : new[]
                {
                    new[] { groupsBySource[BaseGameModelKeys.Relics.LargeCapsule], groupsBySource[BaseGameModelKeys.Relics.SmallCapsule] },
                    new[] { groupsBySource[BaseGameModelKeys.Relics.SmallCapsule], groupsBySource[BaseGameModelKeys.Relics.LargeCapsule] }
                };
        }

        int totalDraws = groupsBySource.Values.Sum(group => group.DrawCount);
        if (totalDraws is < 0 or > 3)
        {
            issue = "CapsuleDrawCountUnsupported:" + totalDraws;
            detail = "Current opening Capsule topology supports one direct Capsule or the Bones Large+Small three-draw combination.";
            return false;
        }

        var distinguished = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (ModelKey key in RelevantImpactRelics(matcher))
            if (plan.Authority.PlayersCount == 1 ||
                matcher.UsesCards && (key == PrayerWheel || key == LastingCandy) ||
                matcher.UsesPotions && key == WhiteBeastStatue || matcher.UsesGold && key == AmethystAubergine) distinguished.Add(key);
        foreach (NeowStructuredEffectSearchCondition constraint in capsuleConstraints)
            foreach (ModelKey key in constraint.OutputKeys.Where(key => key.IsValid)) distinguished.Add(key);
        var capsuleFilter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        foreach (var key in capsuleFilter.CapsuleContainedRelics.All) distinguished.Add(key);
        if (capsuleFilter.RequireWhetstone) distinguished.Add(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        if (capsuleFilter.RequireWarPaint) distinguished.Add(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        var unmodeled = new HashSet<ModelKey>();
        if (capsuleSources.Count > 0 && plan.Authority.PlayersCount > 1)
        {
            var authored = PartyInitialQuery.CapsuleEffectPremise(plan.CompiledSearch.NormalizedQuery).Values.SelectMany(k => k).ToHashSet();
            foreach (var key in lanes.SelectMany(l => l))
            {
                RewardInfluenceProfile held = default;
                bool unsupportedObtain = authored.Contains(key) &&
                    (!RolltheSpire2.Core.Rewards.VanillaRelicRewardEffects.TryGet(plan.ProfileId, key, out var effect) ||
                     (effect.OnObtainCapabilities & (RolltheSpire2.Core.Rewards.RelicOnObtainRewardEffects.ChangesCardRewardPool |
                        RolltheSpire2.Core.Rewards.RelicOnObtainRewardEffects.ChangesPotionRewardPool |
                        RolltheSpire2.Core.Rewards.RelicOnObtainRewardEffects.ChangesPotionRewardState |
                        RolltheSpire2.Core.Rewards.RelicOnObtainRewardEffects.NestedRelicObtain |
                        RolltheSpire2.Core.Rewards.RelicOnObtainRewardEffects.Unknown)) != 0);
                if ((matcher.UsesCards || matcher.UsesPotions || matcher.UsesGold) &&
                    !TryPartyHeldProfile(plan, key, matcher, ref held) || unsupportedObtain)
                { unmodeled.Add(key); }
            }
        }
        // All and structured identities need individual categories. Any/Ban and
        // unsupported-hook sets stay aggregate sequence constraints; their size
        // must not turn a legal three-draw observation into an arbitrary budget failure.
        var requiredIdentities = capsuleConstraints.SelectMany(c => c.OutputKeys)
            .Concat(capsuleFilter.CapsuleContainedRelics.All).ToHashSet();
        if (capsuleFilter.RequireWhetstone) requiredIdentities.Add(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        if (capsuleFilter.RequireWarPaint) requiredIdentities.Add(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        if (requiredIdentities.Count > totalDraws)
        { detail = "CapsuleRequiredIdentitiesExceedDrawCount"; return true; }
        distinguished.IntersectWith(lanes.SelectMany(l => l));

        var laneByKey = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
        for (int lane = 0; lane < 3; lane++)
            foreach (ModelKey key in lanes[lane]) laneByKey[key] = lane;

        var profileMass = new Dictionary<RewardInfluenceProfile, double>();
        int acceptedScenarioCount = 0;
        foreach ((CapsuleRarityChoice[] rolls, double rollMass, string _) in EnumerateCapsuleRarityPatterns(totalDraws))
        {
            CapsuleDrawPosition?[] positions = ProjectCapsulePositions(rolls, lanes.Take(3).Select(items => items.Length).ToArray());
            foreach (ModelKey?[] assignment in EnumerateCompressedAssignments(positions, lanes, distinguished, laneByKey))
            {
                if (!RouteGroupingAccepts(routeGroupings, assignment) ||
                    capsuleConstraints.Where(NeowReplayPlan.IsGroupedCapsule).Any(c => !MatchesCapsuleConstraint(assignment, c))) continue;
                var actualKeys = assignment.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
                if (!capsuleFilter.CapsuleContainedRelics.All.All(actualKeys.Contains) ||
                    capsuleFilter.RequireWhetstone && !actualKeys.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone) ||
                    capsuleFilter.RequireWarPaint && !actualKeys.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint)) continue;
                double BagMass(IReadOnlyCollection<ModelKey> extraBan, out string solveIssue)
                {
                    ModelKey[] bans = capsuleFilter.CapsuleContainedRelics.Ban.Concat(extraBan).Distinct().ToArray();
                    var explicitRelics = includeExplicitRelicSequence ? capsuleFilter.RelicSequenceConditions : [];
                    double all = SolveCompressedAssignmentBagMass(lanes, explicitRelics, positions, assignment,
                        distinguished, out solveIssue, bans);
                    if (double.IsNaN(all) || all <= 0 || capsuleFilter.CapsuleContainedRelics.Any.Count == 0) return all;
                    double none = SolveCompressedAssignmentBagMass(lanes, explicitRelics, positions, assignment,
                        distinguished, out solveIssue, bans.Concat(capsuleFilter.CapsuleContainedRelics.Any).Distinct().ToArray());
                    return double.IsNaN(none) ? none : Math.Max(0, all - none);
                }
                double bagMass = BagMass([], out string solveIssue);
                if (double.IsNaN(bagMass))
                {
                    issue = "CapsuleBagSolver:" + solveIssue;
                    detail = "Capsule projection-compatibility pricing could not normalize the shared initial RelicGrabBag constraint.";
                    return false;
                }
                if (bagMass <= 0d) continue;
                if (unmodeled.Count > 0)
                {
                    double knownMass = BagMass(unmodeled, out solveIssue);
                    if (double.IsNaN(knownMass)) { issue = "CapsuleBagSolver:" + solveIssue; return false; }
                    if (bagMass - knownMass > 1e-14)
                    { issue = "UnmodeledHeldOrAuthoredPoolChangePositiveMass"; return false; }
                }
                double mass = rollMass * bagMass;
                acceptedMass += mass;
                acceptedScenarioCount++;
                RewardInfluenceProfile profile = deterministic;
                foreach (ModelKey? key in assignment)
                    if (key.HasValue && IsRelevantImpactRelic(key.Value, matcher)) profile = profile.Add(key.Value);
                AddMass(profileMass, profile, mass);
            }
        }

        if (acceptedMass <= 0d)
        {
            distribution = new[] { new RewardInfluenceMass(deterministic, 0d, "CapsuleConstraintImpossible") };
            detail = "ExplicitCapsuleRoute;conditionalProfileMass=0;CapsuleNestedOrRelicQueueConstraintImpossible";
            return true;
        }

        double normalizationMass = acceptedMass;
        distribution = profileMass
            .Select(pair => new RewardInfluenceMass(pair.Key, pair.Value / normalizationMass, "ConditionalOnSelectedCapsuleRoute"))
            .OrderBy(item => item.Profile.Label, StringComparer.Ordinal)
            .ToArray();
        detail = $"ExplicitCapsuleRoute;draws={totalDraws};acceptedScenarios={acceptedScenarioCount};conditionalMass={acceptedMass:G17};profiles={string.Join(',', distribution.Select(item => item.Profile.Label + '=' + item.Mass.ToString("G8", System.Globalization.CultureInfo.InvariantCulture)))}";
        return true;
    }

    private static IEnumerable<ModelKey?[]> EnumerateCompressedAssignments(
        IReadOnlyList<CapsuleDrawPosition?> positions,
        IReadOnlyList<ModelKey[]> lanes,
        IReadOnlySet<ModelKey> distinguished,
        IReadOnlyDictionary<ModelKey, int> laneByKey)
    {
        var buffer = new ModelKey?[positions.Count];
        var used = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        foreach (ModelKey?[] result in Walk(0)) yield return result;

        IEnumerable<ModelKey?[]> Walk(int index)
        {
            if (index == positions.Count)
            {
                yield return (ModelKey?[])buffer.Clone();
                yield break;
            }
            if (positions[index] is not { } position)
            {
                buffer[index] = BaseGameModelKeys.OrdinaryRelics.Circlet;
                foreach (ModelKey?[] result in Walk(index + 1)) yield return result;
                buffer[index] = null;
                yield break;
            }
            int lane = position.Lane;
            ModelKey[] candidates = distinguished.Where(key => laneByKey.TryGetValue(key, out int keyLane) && keyLane == lane && !used.Contains(key)).ToArray();
            foreach (ModelKey key in candidates)
            {
                buffer[index] = key;
                used.Add(key);
                foreach (ModelKey?[] result in Walk(index + 1)) yield return result;
                used.Remove(key);
            }

            int distinguishedInLane = distinguished.Count(key => laneByKey.TryGetValue(key, out int keyLane) && keyLane == lane);
            int alreadyUsedOtherSlots = 0;
            for (int prior = 0; prior < index; prior++)
                if (positions[prior] is { } previous && previous.Lane == lane && !buffer[prior].HasValue) alreadyUsedOtherSlots++;
            int otherAvailable = lanes[lane].Length - distinguishedInLane - alreadyUsedOtherSlots;
            if (otherAvailable > 0)
            {
                buffer[index] = null;
                foreach (ModelKey?[] result in Walk(index + 1)) yield return result;
            }
            buffer[index] = null;
        }
    }

    private static double SolveCompressedAssignmentBagMass(
        IReadOnlyList<ModelKey[]> lanes,
        IReadOnlyList<RelicSequenceSearchCondition> explicitRelicConditions,
        IReadOnlyList<CapsuleDrawPosition?> positions,
        IReadOnlyList<ModelKey?> assignment,
        IReadOnlySet<ModelKey> distinguished,
        out string issue,
        IReadOnlyCollection<ModelKey>? globalBans = null)
    {
        issue = string.Empty;
        globalBans ??= [];
        for (int draw = 0; draw < positions.Count; draw++)
            if (positions[draw] is null && globalBans.Contains(BaseGameModelKeys.OrdinaryRelics.Circlet)) return 0;
        double probability = 1d;
        for (int lane = 0; lane < 3; lane++)
        {
            var constraints = new List<FiniteSequenceProbabilitySolver.Constraint>();
            constraints.AddRange(explicitRelicConditions
                .Where(condition => LaneIndex(condition.Lane) == lane)
                .Select(condition => new FiniteSequenceProbabilitySolver.Constraint(condition.RangeMode, condition.RangeValue, condition.Keys)));

            ModelKey[] distinguishedLane = distinguished.Where(key => lanes[lane].Contains(key, ModelKeyComparer.Instance)).ToArray();
            for (int draw = 0; draw < positions.Count; draw++)
            {
                if (positions[draw] is not { } position || position.Lane != lane) continue;
                int ordinal = position.Ordinal;
                ModelKeySetFilter filter = assignment[draw].HasValue
                    ? new ModelKeySetFilter(new[] { assignment[draw]!.Value }, [], globalBans.ToArray())
                    : new ModelKeySetFilter([], [], distinguishedLane.Concat(globalBans).Distinct().ToArray());
                constraints.Add(new FiniteSequenceProbabilitySolver.Constraint(SearchSequenceRangeMode.ExactSlot, ordinal, filter));
            }

            if (constraints.Count == 0) continue;
            FiniteSequenceProbabilitySolver.Item[] items = lanes[lane].Select(key => new FiniteSequenceProbabilitySolver.Item(key)).ToArray();
            if (!FiniteSequenceProbabilitySolver.TrySolve(items, constraints, out FiniteSequenceProbabilitySolver.Result result, out issue))
                return double.NaN;
            probability *= result.Probability;
            if (probability == 0d) return 0d;
        }
        return Math.Clamp(probability, 0d, 1d);
    }

    private static bool RouteGroupingAccepts(IReadOnlyList<CapsuleGroup[]> groupings, IReadOnlyList<ModelKey?> assignment)
    {
        foreach (CapsuleGroup[] grouping in groupings)
        {
            int offset = 0;
            bool accepted = true;
            foreach (CapsuleGroup group in grouping)
            {
                ModelKey?[] outputs = assignment.Skip(offset).Take(group.DrawCount).ToArray();
                offset += group.DrawCount;
                if (group.Constraints.Any(c => !MatchesCapsuleConstraint(outputs, c)))
                {
                    accepted = false;
                    break;
                }
            }
            if (accepted) return true;
        }
        return false;
    }

    private static bool MatchesCapsuleConstraint(IReadOnlyList<ModelKey?> outputs, NeowStructuredEffectSearchCondition constraint)
    {
        ModelKey[] actual = outputs.Where(key => key.HasValue).Select(key => key.GetValueOrDefault()).ToArray();
        if (NeowReplayPlan.IsGroupedCapsule(constraint))
            return outputs.Count == 3 && constraint.OutputKeys.GroupBy(k=>k).All(g=>actual.Count(k=>k==g.Key)>=g.Count());
        if (constraint.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule)
        {
            return constraint.Kind == NeowStructuredConditionKind.ExactSingle &&
                   constraint.OutputKeys.Count == 1 &&
                   actual.Length == 1 && actual[0] == constraint.OutputKeys[0];
        }
        if (constraint.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule)
        {
            return constraint.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
                   outputs.Count == 2 &&
                   constraint.OutputKeys.GroupBy(k => k).All(g => actual.Count(k => k == g.Key) >= g.Count());
        }
        return false;
    }

    private static IEnumerable<(CapsuleRarityChoice[] Rolls, double Mass, string Label)> EnumerateCapsuleRarityPatterns(int drawCount)
    {
        CapsuleRarityChoice[] choices =
        {
            new(0, 0.50d, "C"),
            new(1, 0.33d, "U"),
            new(2, 0.17d, "R")
        };
        var buffer = new CapsuleRarityChoice[drawCount];
        foreach ((CapsuleRarityChoice[] Rolls, double Mass, string Label) result in Walk(0, 1d, string.Empty)) yield return result;

        IEnumerable<(CapsuleRarityChoice[] Rolls, double Mass, string Label)> Walk(int index, double mass, string label)
        {
            if (index == drawCount)
            {
                yield return ((CapsuleRarityChoice[])buffer.Clone(), mass, label);
                yield break;
            }
            foreach (CapsuleRarityChoice choice in choices)
            {
                buffer[index] = choice;
                foreach ((CapsuleRarityChoice[] Rolls, double Mass, string Label) result in Walk(index + 1, mass * choice.Probability, label + choice.Label))
                    yield return result;
            }
        }
    }

    private static CapsuleDrawPosition?[] ProjectCapsulePositions(IReadOnlyList<CapsuleRarityChoice> rolls, int[] initialCounts)
    {
        int[] remaining = (int[])initialCounts.Clone();
        int[] consumed = new int[remaining.Length];
        var output = new CapsuleDrawPosition?[rolls.Count];
        for (int draw = 0; draw < rolls.Count; draw++)
        {
            int selectedLane = -1;
            for (int lane = rolls[draw].PreferredLane; lane < 3; lane++)
            {
                if (remaining[lane] <= 0) continue;
                selectedLane = lane;
                break;
            }
            if (selectedLane < 0) continue;
            consumed[selectedLane]++;
            remaining[selectedLane]--;
            output[draw] = new CapsuleDrawPosition(selectedLane, consumed[selectedLane]);
        }
        return output;
    }

    private static bool TryBuildPlayerLanes(
        CompiledRelicPoolSnapshot pool,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        out ModelKey[][] lanes,
        out string issue)
    {
        lanes = new ModelKey[3][];
        issue = string.Empty;
        var keyByDense = new Dictionary<ushort, ModelKey>();
        foreach ((ModelKey key, ushort dense) in denseByKey)
        {
            if (!keyByDense.TryAdd(dense, key) && keyByDense[dense] != key)
            {
                issue = "DenseIdentityAmbiguous:" + dense;
                return false;
            }
        }
        for (int lane = 0; lane < 3; lane++)
        {
            if (lane >= pool.PlayerLaneBucketIndexes.Length)
            {
                issue = "PlayerLaneIndexMissing:" + lane;
                return false;
            }
            short bucket = pool.PlayerLaneBucketIndexes[lane];
            if (bucket < 0 || bucket >= pool.BucketCount)
            {
                issue = "PlayerLaneMissing:" + lane;
                return false;
            }
            int offset = pool.BucketOffsets[bucket];
            int length = pool.BucketLengths[bucket];
            if (offset < 0 || length < 0 || offset + length > pool.DenseRelicIds.Length)
            {
                issue = "PlayerLaneBoundsInvalid:" + lane;
                return false;
            }
            var keys = new List<ModelKey>(length);
            for (int index = 0; index < length; index++)
            {
                ushort dense = pool.DenseRelicIds[offset + index];
                if (!keyByDense.TryGetValue(dense, out ModelKey key))
                {
                    issue = "DenseIdentityMissing:" + dense;
                    return false;
                }
                keys.Add(key);
            }
            if (keys.Distinct(ModelKeyComparer.Instance).Count() != keys.Count)
            {
                issue = "DuplicateIdentityInPlayerLane:" + lane;
                return false;
            }
            lanes[lane] = keys.ToArray();
        }
        return true;
    }

    private static ModelKey[] ExplicitBonesRelics(NeowSearchFilter filter)
    {
        return filter.RequiredBonesCombination
            .Concat(filter.RequiredBonesAcquisitionOrder)
            .Concat(filter.StructuredNeowEffects
                .Where(condition => !condition.IsEmpty && condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
                                    condition.Scope == NeowStructuredEffectScope.BonesOfferedRelics &&
                                    condition.OutputKind == NeowStructuredOutputKind.Relic)
                .SelectMany(condition => condition.OutputKeys))
            .Concat(filter.StructuredNeowEffects
                .Where(condition => !condition.IsEmpty && IsCapsule(condition.SourceRelicKey))
                .Select(condition => condition.SourceRelicKey))
            .Where(key => key.IsValid && key != BaseGameModelKeys.Relics.NeowsBones)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
    }

    private static IEnumerable<ModelKey> RelevantImpactRelics(MatcherContext matcher)
    {
        _ = matcher;
        // Projection compatibility is stricter than predicate-local relevance: any
        // known latent Reward modifier can make the real continuation diverge from the
        // query-literal synthetic context, even when the current predicate does not
        // directly observe that modifier's primary output family.
        yield return PrayerWheel;
        yield return WhiteBeastStatue;
        yield return LastingCandy;
        yield return AmethystAubergine;
    }

    private static bool IsRelevantImpactRelic(ModelKey relic, MatcherContext matcher)
    {
        _ = matcher;
        return relic == PrayerWheel || relic == WhiteBeastStatue ||
               relic == LastingCandy || relic == AmethystAubergine;
    }

    private static bool HasExplicitCapsuleRoute(ProbabilitySemanticView semantic)
    {
        if (semantic.NormalizedQuery.OpeningRoute is not { IsValid: true } route) return false;
        if (IsCapsule(route.RouteRelicKey)) return true;
        if (route.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones) return false;
        return ExplicitBonesRelics(semantic.NumericalFilter).Any(IsCapsule);
    }

    private static bool HasExplicitRewardInfluenceRelation(ProbabilitySemanticView semantic, ModelKey relic) =>
        semantic.Relations.Any(relation =>
            relation.Kind == SemanticRelationKind.StateInfluence &&
            relation.Source.Kind == SemanticFactKind.RewardInfluenceRelic &&
            relation.Source.Key == relic &&
            relation.Target.Kind == SemanticFactKind.CombatReward);

    private static bool IsCapsule(ModelKey key) =>
        key == BaseGameModelKeys.Relics.SmallCapsule || key == BaseGameModelKeys.Relics.LargeCapsule;

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

    private static int LaneIndex(RelicSequenceKind kind) => kind switch
    {
        RelicSequenceKind.Common => 0,
        RelicSequenceKind.Uncommon => 1,
        RelicSequenceKind.Rare => 2,
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
