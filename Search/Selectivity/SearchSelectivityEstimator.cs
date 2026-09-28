using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Selectivity;

internal sealed record RelicLaneProbabilityAuthority(
    IReadOnlyDictionary<RelicSequenceKind, ModelKey[]> LanePools);

/// <summary>
/// P5 V1 shared, authority-driven selectivity service. It prices only shapes for
/// which the current runtime snapshot or accepted joint evidence is sufficient.
/// Unsupported execution never implies Unpriced, and incomplete probability
/// authority never becomes an invented number.
/// </summary>
internal static class SearchSelectivityEstimator
{
    public static SearchSelectivityEstimate EstimateStage(
        SearchSelectivityInput plan,
        SearchSelectivityDomain domain,
        string? queryShape = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return domain switch
        {
            SearchSelectivityDomain.Neow => EstimateNeow(plan),
            SearchSelectivityDomain.Relic => EstimateRelic(plan),
            SearchSelectivityDomain.AncientOption => EstimateAncientOption(plan),
            SearchSelectivityDomain.WorldEvent => EstimateWorld(plan),
            SearchSelectivityDomain.CombatReward => EstimateCombatReward(plan),
            _ => SearchSelectivityEstimate.Unpriced("P5.UnknownDomain", "Unknown selectivity domain.")
        };
    }

    public static string QueryShape(SearchSelectivityInput plan, SearchSelectivityDomain domain)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return domain switch
        {
            SearchSelectivityDomain.Neow => IsBonesGrantShape(ProbabilitySemanticProjection.From(plan).NumericalFilter)
                ? "Neow.BonesGrantConditionalChain"
                : IsSimpleRequiredCurseIdentity(ProbabilitySemanticProjection.From(plan).NumericalFilter, out _)
                    ? "Neow.RequiredCurseIdentity"
                    : IsSingleLooseNeowIdentity(ProbabilitySemanticProjection.From(plan).NumericalFilter)
                        ? "Neow.SingleLooseIdentity"
                        : "Neow.Default",
            SearchSelectivityDomain.Relic => IsSameLaneTwoTargetFirstTwo(ProbabilitySemanticProjection.From(plan).NumericalFilter)
                ? "Relic.WithoutReplacementPair"
                : "Relic.PositiveSequence",
            SearchSelectivityDomain.AncientOption => AncientShape(ProbabilitySemanticProjection.From(plan).NumericalFilter),
            SearchSelectivityDomain.WorldEvent => IsAct3TwoBossOrdinalShape(ProbabilitySemanticProjection.From(plan).NumericalFilter)
                ? "World.Act3BossPairWithoutReplacement"
                : IsP3BEventPairShape(ProbabilitySemanticProjection.From(plan).NumericalFilter)
                    ? "World.EventPair"
                    : HasAncientActualShape(ProbabilitySemanticProjection.From(plan).NumericalFilter)
                        ? "World.ActualAncient"
                        : "World.Default",
            SearchSelectivityDomain.CombatReward => CombatRewardShape(ProbabilitySemanticProjection.From(plan).NumericalFilter),
            _ => "*"
        };
    }

    internal static SearchSelectivityEstimate EstimateAct3BossPair(
        SearchSelectivityInput plan) => EstimateBossOrdinalPair(plan);

    internal static SearchSelectivityEstimate EstimateSharedAncientAssignment(
        SearchSelectivityInput plan,
        int act,
        ModelKey sharedAncientKey)
    {
        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null ||
            !generation.ActSelectionAuthorityExact ||
            !generation.SharedAncientCatalogExact ||
            !generation.AllSharedAncientCatalogExact ||
            !generation.DirectSourceAudited ||
            (!generation.NoUnknownHooksOrModifiers && !plan.Authority.UsesBestEffortModel) ||
            !generation.SharedAncients.Contains(sharedAncientKey))
        {
            return SearchSelectivityEstimate.Unpriced(
                "P5.Ancient.SharedAssignment.AuthorityMissing",
                "Shared Ancient assignment requires exact act-selection and shared-pool authority.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.StructuralAssignment,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        Beta109ActSelectionGroupSnapshot[] orderedGroups = generation.ActSelectionGroups
            .OrderBy(group => group.Act)
            .ToArray();
        int selectedIndex = Array.FindIndex(orderedGroups, group => group.Act == act);
        if (selectedIndex < 1 ||
            orderedGroups.Take(selectedIndex + 1).Any(group =>
                !group.EligibilityAndOrderExact || group.EligibleActsInSourceOrder.Count == 0))
        {
            return SearchSelectivityEstimate.Unpriced(
                "P5.Ancient.SharedAssignment.ActNotSelected",
                "Requested Act does not have a shared-Ancient assignment draw in this authority snapshot.");
        }

        // Each assignment draw chooses a uniform prefix count in [0, remaining].
        // For any particular remaining Ancient, P(selected in this prefix)=1/2.
        // It must survive every earlier assignment draw, then be selected here.
        double probability = Math.Pow(0.5d, selectedIndex);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.StructuralAssignment,
            SearchSelectivityCoverage.PartialRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            "P5.Authority.SharedAncientUniformPrefixAssignment",
            $"Shared Ancient assignment to Act {act}; act-selection group index {selectedIndex} gives (1/2)^{selectedIndex}.",
            new[]
            {
                "Prices assignment membership only; it does not prove actual Ancient identity or option output.",
                "Shared Ancient source is shuffled before the uniform-prefix assignment chain."
            });
    }

    private static string CombatRewardShape(NeowSearchFilter filter)
    {
        NormalCombatRewardSearchCondition[] conditions = filter.NormalCombatRewardConditions
            .Where(condition => !condition.IsEmpty)
            .ToArray();
        if (conditions.Length == 1)
        {
            NormalCombatRewardSearchCondition condition = conditions[0];
            bool hasCards = !condition.Cards.IsEmpty;
            bool hasPotion = condition.PotionRequirement != NormalCombatPotionRequirement.Any || !condition.Potions.IsEmpty;
            bool hasGold = condition.MinimumGold.HasValue || condition.MaximumGold.HasValue;
            if (condition.BattleOrdinal == 1 && hasCards && !hasPotion && !hasGold)
                return "CombatReward.MinimalCard";
            if (condition.BattleOrdinal == 1 && hasCards && hasPotion && !hasGold)
                return "CombatReward.CardPotion";
            if (condition.BattleOrdinal == 0 && hasCards && hasPotion && !hasGold)
                return "CombatReward.FirstThree";
        }
        return "CombatReward.Default";
    }

    private static SearchSelectivityEstimate EstimateNeow(SearchSelectivityInput plan)
    {
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        if (plan.Authority.PlayersCount > 1)
            return NeowStructuredEffectProbabilityEstimator.EstimatePartyOpening(plan);
        if (filter.StructuredNeowEffects.Any(condition => !condition.IsEmpty))
            return NeowStructuredEffectProbabilityEstimator.Estimate(plan);

        if (HasNestedOrDeckCoupledNeowCompound(filter))
        {
            return SearchSelectivityEstimate.Unpriced(
                "P5.Neow.NestedCompound.Unpriced",
                "Nested Capsule/deck-coupled continuation is intentionally not assigned a synthetic probability.",
                SearchSelectivityConfidence.Unknown,
                SearchSelectivityMethod.Unknown,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent,
                new[] { "Fast remains deckless; Product Exact remains final authority." });
        }

        if (IsBonesGrantShape(filter))
            return EstimateBonesConditionalChain(plan);

        if (IsSimpleRequiredCurseIdentity(filter, out ModelKey requiredCurse))
        {
            if (!ModernNeowIdentityPredictor.TryGetEligibleCursePool(plan.Authority, out IReadOnlyList<ModelKey> cursePool))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "P5.Neow.RequiredCurse.AuthorityMissing",
                    "Exact eligible Neow curse-pool membership is unavailable.");
            }
            if (!cursePool.Contains(requiredCurse))
            {
                return SearchSelectivityEstimate.Exact(
                    0d,
                    SearchSelectivityMethod.AuthorityPoolMembership,
                    SearchSelectivityCoverage.ExactRequestedPredicate,
                    SearchSelectivityDependencyClass.AssumedIndependent,
                    "P5.Authority.NeowRequiredCurseImpossible",
                    "The required unpinned Neow curse identity is not in the exact eligible curse pool.");
            }
            return SearchSelectivityEstimate.Exact(
                1d / cursePool.Count,
                SearchSelectivityMethod.AuthorityPoolMembership,
                SearchSelectivityCoverage.ExactRequestedPredicate,
                SearchSelectivityDependencyClass.AssumedIndependent,
                "P5.Authority.NeowRequiredCurseIdentity",
                $"Unpinned Neow offers contain exactly one uniformly selected eligible curse; requested identity over {cursePool.Count} eligible curses.",
                new[]
                {
                    "This prices only the top-level Neow identity filter; it does not pin a route.",
                    "CrossDomainRelation=AssumedIndependentUnlessStructuralDependencyRegistered"
                });
        }

        if (IsSingleLooseNeowIdentity(filter) || !filter.NeowRelics.IsEmpty &&
            filter.NeowRoute is null && filter.StructuredNeowEffects.Count == 0 && filter.EffectOutputConditions.Count == 0 &&
            !filter.RequireNeowsBones && !filter.RequireSmallCapsule && !filter.RequireLargeCapsule &&
            !filter.RequireWhetstone && !filter.RequireWarPaint && filter.RequiredFinalCurse is null && filter.BannedFinalCurses.Count == 0)
        {
            if (!NeowStructuredEffectProbabilityEstimator.TryTopLevelRouteProbability(plan, filter.NeowRoute?.RouteRelicKey ?? default,
                out double probability, out string evidence))
                return SearchSelectivityEstimate.Unpriced("Probability.Neow.OfferJointUnavailable", "Opening offer authority missing.");
            return SearchSelectivityEstimate.Exact(probability, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.Neow.CompleteOffer", evidence);
        }

        return SearchSelectivityEstimate.Unpriced(
            "P5.Neow.ShapeUnpriced",
            "Structured/route-coupled Neow shape is outside the first authority-backed P5 pricing set.",
            SearchSelectivityConfidence.Low,
            SearchSelectivityMethod.Unknown,
            SearchSelectivityCoverage.PartialRequestedConjunction,
            SearchSelectivityDependencyClass.RouteDependent);
    }

    private static SearchSelectivityEstimate EstimateBonesConditionalChain(SearchSelectivityInput plan)
    {
        NeowEffectAuthoritySnapshot? effects = plan.Authority.EffectAuthority;
        if (!ModernNeowIdentityPredictor.TryGetEligibleCursePool(plan.Authority, out IReadOnlyList<ModelKey> cursePool) ||
            effects is null || !effects.HasExactBonesPools || effects.BonesEligibleRelics is null)
        {
            return SearchSelectivityEstimate.Unpriced(
                "P5.Neow.Bones.AuthorityMissing",
                "Bones conditional-chain pricing requires exact Neow curse and Bones eligible-relic pools.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        IReadOnlyList<ModelKey> targets = ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder.Count != 0
            ? ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder
            : ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesCombination;
        ModelKey[] distinctTargets = targets.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
        if (distinctTargets.Length is < 1 or > 2)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Neow.Bones.TargetArityUnsupported",
                "Exact Bones pricing currently covers one specified grant or two distinct specified grants.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        ModelKey[] pool = effects.BonesEligibleRelics
            .Where(key => key != BaseGameModelKeys.Relics.NeowsBones)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (pool.Length < 2 || distinctTargets.Any(target => !pool.Contains(target)))
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent,
                "P5.Authority.BonesGrantImpossible",
                "At least one requested Bones grant identity is absent from the exact eligible pool.");
        }
        if (!cursePool.Contains(BaseGameModelKeys.Relics.NeowsBones))
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent,
                "P5.Authority.NeowsBonesNotEligible",
                "Neow's Bones is absent from the exact eligible curse pool.");
        }

        if (!NeowStructuredEffectProbabilityEstimator.TryTopLevelRouteProbability(plan, BaseGameModelKeys.Relics.NeowsBones,
            out double routeProbability, out _))
            return SearchSelectivityEstimate.Unpriced("Probability.Neow.Bones.OfferJointUnavailable", "Opening offer authority missing.");
        double grantProbability = distinctTargets.Length == 1
            ? 2d / pool.Length
            : WithoutReplacementUnorderedPairProbability(pool.Length, 2);
        double probability = routeProbability * grantProbability;
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.RouteDependent,
            distinctTargets.Length == 1 ? "Probability.Authority.BonesSingleGrantConditionalChain" : "P5.Authority.BonesConditionalChain",
            distinctTargets.Length == 1
                ? $"Neow's Bones identity 1/{cursePool.Count} followed by one requested identity appearing among two distinct grants from {pool.Length}: 2/{pool.Length}."
                : $"Neow's Bones identity 1/{cursePool.Count} followed by two distinct grants from {pool.Length} without replacement; Offered/Reverse are player routes, not another random 1/2 factor.",
            new[]
            {
                "Both Offered and Reverse acquisition orders are first-class legal routes over the same two offered relics.",
                "No cross-route probability factor is invented."
            });
    }

    private static SearchSelectivityEstimate EstimateRelic(SearchSelectivityInput plan) =>
        EstimateRelicAndShopConditions(plan);

    private static SearchSelectivityEstimate EstimateRelicAndShopConditions(SearchSelectivityInput plan)
    {
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        RelicSequenceSearchCondition[] ordinary = filter.RelicSequenceConditions
            .Where(item => !item.IsEmpty)
            .ToArray();
        RelicShopSequenceSearchCondition[] shop = filter.RelicShopSequenceConditions
            .Where(item => !item.IsEmpty)
            .ToArray();
        if (shop.Length == 0) return EstimateRelicConditions(plan, ordinary);

        if (!TryGetRelicLaneProbabilityAuthority(plan, out RelicLaneProbabilityAuthority? authority, out string issue) ||
            authority is null)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Relic.AuthorityMissing:" + issue,
                "Exact runtime Relic lane authority is unavailable for the joint ordinary/typed Shop projection.");
        }

        double probability = 1d;
        var derivations = new List<string>();
        RelicSequenceKind[] lanes = ordinary.Select(item => item.Lane)
            .Concat(shop.Select(_ => RelicSequenceKind.Shop))
            .Distinct()
            .OrderBy(item => item)
            .ToArray();
        foreach (RelicSequenceKind lane in lanes)
        {
            if (!authority.LanePools.TryGetValue(lane, out ModelKey[]? pool))
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.LaneMissing:" + lane,
                    "Requested Relic lane is unavailable from the immutable runtime authority.");

            RelicSequenceSearchCondition[] laneOrdinary = ordinary.Where(item => item.Lane == lane).ToArray();
            int maxExact = laneOrdinary.Where(item => item.RangeMode == SearchSequenceRangeMode.ExactSlot)
                .Select(item => item.RangeValue).DefaultIfEmpty(0).Max();
            if (maxExact > pool.Length)
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.ExactSlotOutsideCapturedLane",
                    $"Exact Relic slot {maxExact} exceeds the captured {lane} lane length {pool.Length}.");

            var constraints = laneOrdinary.Select(condition => new FiniteSequenceProbabilitySolver.Constraint(
                condition.RangeMode, condition.RangeValue, condition.Keys)).ToList();
            bool impossibleTypedMultiplicity = false;
            if (lane == RelicSequenceKind.Shop)
            {
                foreach (RelicShopSequenceSearchCondition typed in shop)
                {
                    if (!TryAppendTypedShopConstraints(typed, constraints, out string typedIssue))
                    {
                        if (typedIssue == "RelicShopDuplicateTargetImpossible")
                        {
                            impossibleTypedMultiplicity = true;
                            break;
                        }
                        return SearchSelectivityEstimate.Unpriced(
                            "Probability.Relic.ShopSequenceSolver:" + typedIssue,
                            "Typed Shop sequence cannot be represented by the bounded joint finite-permutation solver.");
                    }
                }
            }

            double laneProbability;
            string normalization;
            if (impossibleTypedMultiplicity)
            {
                laneProbability = 0d;
                normalization = "DuplicateTypedShopTargetImpossible";
            }
            else if (!FiniteSequenceProbabilitySolver.TrySolve(
                         pool.Select(key => new FiniteSequenceProbabilitySolver.Item(key)).ToArray(),
                         constraints,
                         out FiniteSequenceProbabilitySolver.Result solved,
                         out string solveIssue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.JointSequenceSolver:" + solveIssue,
                    "Relic lane requires a relation outside the current bounded finite-permutation solver.");
            }
            else
            {
                laneProbability = solved.Probability;
                normalization = solved.Normalization;
            }

            probability *= laneProbability;
            derivations.Add($"{lane}={laneProbability:G17}[{normalization}]");
            if (probability == 0d) break;
        }

        return SearchSelectivityEstimate.Exact(
            Math.Clamp(probability, 0d, 1d),
            SearchSelectivityMethod.WithoutReplacement,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            lanes.Length > 1 ? SearchSelectivityDependencyClass.AssumedIndependent : SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Authority.RelicFiniteSequenceJointShop",
            "Ordinary Shop predicates and typed Shop sequences are normalized jointly over one cleaned permutation: " +
            string.Join("; ", derivations),
            new[]
            {
                "RelicShopLaneDistinctFromCommonUncommonRare=true",
                "OrdinaryAndTypedShopObserveSameCleanedPermutation=true",
                "NoWithinShopIndependenceMultiplication=true",
                "DistinctLaneCombinationPolicy=AssumedIndependentUnlessStructuralDependencyRegistered"
            });
    }

    internal static bool TryGetRelicLaneProbabilityAuthority(
        SearchSelectivityInput plan,
        out RelicLaneProbabilityAuthority? authority,
        out string issue)
    {
        authority = null;
        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out issue))
            return false;

        var keyByDense = new Dictionary<ushort, ModelKey>();
        foreach ((ModelKey key, ushort dense) in denseByKey)
        {
            if (!keyByDense.TryAdd(dense, key) && keyByDense[dense] != key)
            {
                issue = "RelicDenseIdentityAmbiguous";
                return false;
            }
        }

        var lanes = new Dictionary<RelicSequenceKind, ModelKey[]>();
        foreach (RelicSequenceKind lane in Enum.GetValues<RelicSequenceKind>())
        {
            if (!TryGetLanePool(pool, lane, out ushort[] denseEntries)) continue;
            var keys = new ModelKey[denseEntries.Length];
            for (int index = 0; index < denseEntries.Length; index++)
            {
                if (!keyByDense.TryGetValue(denseEntries[index], out keys[index]))
                {
                    issue = "RelicDenseIdentityMissing:" + denseEntries[index];
                    return false;
                }
            }
            lanes[lane] = keys;
        }

        authority = new RelicLaneProbabilityAuthority(lanes);
        issue = string.Empty;
        return true;
    }

    internal static bool TryAppendTypedShopConstraints(
        RelicShopSequenceSearchCondition condition,
        ICollection<FiniteSequenceProbabilitySolver.Constraint> output,
        out string issue)
    {
        issue = string.Empty;
        if (condition.Count is < 1 or > 5 || condition.Slots.Count < condition.Count ||
            condition.OrderMode is not (CombatRewardSequenceOrderMode.Ordered or CombatRewardSequenceOrderMode.Unordered))
        {
            issue = "ShopSequenceShapeUnsupported";
            return false;
        }

        ModelKey?[] slots = condition.Slots.Take(condition.Count).ToArray();
        if (slots.Any(target => target.HasValue && !target.Value.IsValid))
        {
            issue = "ShopSequenceInvalidTarget";
            return false;
        }

        ModelKey[] targets = slots.Where(target => target.HasValue).Select(target => target!.Value).ToArray();
        if (condition.OrderMode == CombatRewardSequenceOrderMode.Unordered)
        {
            if (targets.Distinct(ModelKeyComparer.Instance).Count() != targets.Length)
            {
                issue = "RelicShopDuplicateTargetImpossible";
                return false;
            }
            if (targets.Length != 0)
                output.Add(new FiniteSequenceProbabilitySolver.Constraint(
                    SearchSequenceRangeMode.FirstN,
                    condition.Count,
                    new ModelKeySetFilter([], targets, [])));
            return true;
        }

        for (int index = 0; index < slots.Length; index++)
            if (slots[index] is ModelKey target)
                output.Add(new FiniteSequenceProbabilitySolver.Constraint(
                    SearchSequenceRangeMode.ExactSlot,
                    index + 1,
                    new ModelKeySetFilter([], [target], [])));
        return true;
    }

    internal static SearchSelectivityEstimate EstimateRelicShopConditions(
        SearchSelectivityInput plan,
        RelicShopSequenceSearchCondition condition)
    {
        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out string issue))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Relic.ShopAuthorityMissing:" + issue,
                "Exact cleaned IsAllowedInShops Relic pool authority is unavailable.");
        }
        if (!TryGetLanePool(pool, RelicSequenceKind.Shop, out ushort[] denseEntries))
            return SearchSelectivityEstimate.Unpriced("Probability.Relic.ShopLaneMissing", "Cleaned runtime Shop Relic lane is unavailable.");

        var reverse = new Dictionary<ushort, ModelKey>();
        foreach ((ModelKey key, ushort dense) in denseByKey)
        {
            if (!reverse.TryAdd(dense, key) && reverse[dense] != key)
                return SearchSelectivityEstimate.Unpriced("Probability.Relic.ShopDenseIdentityAmbiguous", "Shop Relic dense identity map is not one-to-one.");
        }
        ModelKey[] poolKeys = denseEntries
            .Where(reverse.ContainsKey)
            .Select(dense => reverse[dense])
            .ToArray();
        if (poolKeys.Length != denseEntries.Length)
            return SearchSelectivityEstimate.Unpriced("Probability.Relic.ShopDenseIdentityMissing", "A cleaned Shop Relic entry cannot be resolved to a ModelKey.");
        if (!ShopSequenceProbabilityEstimator.TrySolveRelic(poolKeys, condition, out double probability, out string detail))
            return SearchSelectivityEstimate.Unpriced("Probability.Relic.ShopSequenceSolver:" + detail, "Shop Relic finite-sequence probability authority is unavailable.");
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.WithoutReplacement,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Authority.RelicShopCleanedFiniteSequence",
            $"Exact no-replacement probability over the cleaned IsAllowedInShops pool: {detail}.",
            new[]
            {
                "RelicShopUsesCleanedIsAllowedInShopsPool=true",
                "RelicShopDuplicateTargetProbability=0",
                "OrderedNonEmptySlotsBindExactMerchantOrdinal=true",
                "OrderedEmptySlotsArePositionalWildcards=true",
                "UnorderedTargetsAreMembershipWithinFirstN=true",
                "ProbabilityOnlySearchExactWitnessUnaffected=true"
            });
    }

    internal static SearchSelectivityEstimate EstimateMerchantColorlessConditions(SearchSelectivityInput plan)
    {
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        MerchantColorlessSequenceSearchCondition[] conditions = filter.MerchantColorlessSequenceConditions
            .Where(item => !item.IsEmpty)
            .ToArray();
        var slots = filter.MerchantColorlessConditions.Where(c => c.IsValid).ToArray();
        if (conditions.Length == 0 && slots.Length == 0)
            return SearchSelectivityEstimate.Unpriced("Probability.MerchantColorless.NoSequencePredicate", "No canonical Merchant Colorless sequence predicate is present.");

        Beta111MerchantColorlessAuthority authority = Beta111MerchantColorlessAuthority.From(plan.Authority);
        if (!authority.HasExactV1Inputs)
            return SearchSelectivityEstimate.Unpriced("Probability.MerchantColorless.AuthorityMissing", "Exact Beta111 runtime-authoritative Uncommon/Rare Merchant pools are unavailable.");

        var terms = new List<(MerchantColorlessSlot Slot, double Probability, string Detail)>();
        foreach (var lane in conditions.Select(c => c.Slot).Concat(slots.Select(c => c.Slot)).Distinct())
        {
            IReadOnlyList<ModelKey> pool = lane == MerchantColorlessSlot.Uncommon ? authority.UncommonPool : authority.RarePool;
            if (!ShopSequenceProbabilityEstimator.TrySolveColorlessConjunction(pool, slots.Where(c => c.Slot == lane).ToArray(),
                conditions.Where(c => c.Slot == lane).ToArray(), out double probability, out string detail))
                return SearchSelectivityEstimate.Unpriced("Probability.MerchantColorless.SequenceSolver:" + detail, "Merchant Colorless finite replacement probability authority is unavailable.");
            terms.Add((lane, probability, detail));
        }
        double total = terms.Aggregate(1d, (value, item) => value * item.Probability);
        return SearchSelectivityEstimate.Exact(
            total,
            terms.Count > 1 ? SearchSelectivityMethod.NamedStreamIndependenceModel : SearchSelectivityMethod.AuthorityPoolMembership,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            terms.Count > 1 ? SearchSelectivityDependencyClass.AssumedIndependent : SearchSelectivityDependencyClass.StructuralDependence,
            terms.Count > 1 ? "Probability.Authority.MerchantColorlessURAssumedIndependent" : "Probability.Authority.MerchantColorlessSequenceFiniteDP",
            "Exact finite DP over the first N Normal Merchant U/R slots: " + string.Join("; ", terms.Select(item => item.Slot + "=" + item.Probability.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "[" + item.Detail + "]")),
            new[]
            {
                "MerchantColorlessUsesRuntimeAuthoritativeOrderedPool=true",
                "MerchantColorlessAcrossShopsAllowsRepeatedCard=true",
                "OrderedNonEmptySlotsBindExactMerchantOrdinal=true",
                "OrderedEmptySlotsArePositionalWildcards=true",
                "UnorderedTargetsAreMembershipWithinFirstN=true",
                "MerchantColorlessURCrossLaneRelation=AssumedIndependentUnlessStructuralDependencyRegistered",
                "ProbabilityOnlySearchExactWitnessUnaffected=true"
            });
    }

    internal static SearchSelectivityEstimate EstimateRelicConditions(
        SearchSelectivityInput plan,
        IReadOnlyList<RelicSequenceSearchCondition> sourceConditions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceConditions);
        RelicSequenceSearchCondition[] conditions = sourceConditions
            .Where(item => !item.IsEmpty)
            .ToArray();
        if (conditions.Length == 0)
            return SearchSelectivityEstimate.Unpriced("P5.Relic.NoPredicate", "No Relic predicate to price.");
        if (!RelicPoolCompilation.TryCompileAuthorityPoolForSelectivity(
                plan.ProfileId,
                plan.Authority,
                out CompiledRelicPoolSnapshot pool,
                out IReadOnlyDictionary<ModelKey, ushort> denseByKey,
                out string issue))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.Relic.AuthorityMissing:" + issue,
                "Exact runtime Relic pool authority is unavailable.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.AuthorityPoolMembership,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        var keyByDense = new Dictionary<ushort, ModelKey>();
        foreach ((ModelKey key, ushort dense) in denseByKey)
        {
            if (!keyByDense.TryAdd(dense, key) && keyByDense[dense] != key)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.DenseIdentityAmbiguous",
                    "Runtime dense Relic identity map is not one-to-one.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.AuthorityPoolMembership,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence);
            }
        }

        double probability = 1d;
        var derivations = new List<string>();
        foreach (IGrouping<RelicSequenceKind, RelicSequenceSearchCondition> laneGroup in conditions.GroupBy(item => item.Lane))
        {
            if (!TryGetLanePool(pool, laneGroup.Key, out ushort[] laneDense))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.LaneMissing:" + laneGroup.Key,
                    "Requested Relic lane is unavailable from the immutable runtime authority.");
            }

            var items = new List<FiniteSequenceProbabilitySolver.Item>(laneDense.Length);
            foreach (ushort dense in laneDense)
            {
                if (!keyByDense.TryGetValue(dense, out ModelKey key))
                {
                    return SearchSelectivityEstimate.Unpriced(
                        "Probability.Relic.DenseIdentityMissing:" + dense,
                        "A runtime Relic lane entry cannot be resolved back to its ModelKey.",
                        SearchSelectivityConfidence.Low,
                        SearchSelectivityMethod.AuthorityPoolMembership,
                        SearchSelectivityCoverage.PartialRequestedConjunction,
                        SearchSelectivityDependencyClass.StructuralDependence);
                }
                items.Add(new FiniteSequenceProbabilitySolver.Item(key));
            }

            // Relic evaluator treats an unavailable exact slot as Unknown rather than
            // NoMatch. Keep that boundary fail-visible instead of claiming P=0.
            int maxExact = laneGroup.Where(item => item.RangeMode == SearchSequenceRangeMode.ExactSlot)
                .Select(item => item.RangeValue).DefaultIfEmpty(0).Max();
            if (maxExact > items.Count)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.ExactSlotOutsideCapturedLane",
                    $"Exact Relic slot {maxExact} exceeds the captured {laneGroup.Key} lane length {items.Count}; Product evaluator classifies this as unavailable/Unknown.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.WithoutReplacement,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence);
            }

            FiniteSequenceProbabilitySolver.Constraint[] sequenceConstraints = laneGroup
                .Select(condition => new FiniteSequenceProbabilitySolver.Constraint(
                    condition.RangeMode,
                    condition.RangeValue,
                    condition.Keys))
                .ToArray();
            if (!FiniteSequenceProbabilitySolver.TrySolve(
                    items, sequenceConstraints, out FiniteSequenceProbabilitySolver.Result solved, out string solveIssue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.Relic.SequenceSolver:" + solveIssue,
                    "Relic lane requires a sequence relation outside the current exact finite-sequence solver.",
                    SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.WithoutReplacement,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence);
            }

            probability *= solved.Probability;
            derivations.Add($"{laneGroup.Key}={solved.Probability:G17}[{solved.Normalization}]");
            if (probability == 0d) break;
        }

        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.WithoutReplacement,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            conditions.Select(item => item.Lane).Distinct().Count() > 1
                ? SearchSelectivityDependencyClass.AssumedIndependent
                : SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Authority.RelicFiniteSequence",
            "Exact finite-permutation probability over runtime-authoritative Relic lanes: " + string.Join("; ", derivations),
            new[]
            {
                "All constraints within one rarity lane are normalized jointly; duplicate positional facts are not multiplied twice.",
                "Distinct rarity/shop lanes use AssumedIndependent unless a registered shared constraint (for example Capsule queue consumption) applies.",
                "Shop is priced over the cleaned IsAllowedInShops subset. Reversing a uniform shuffled order preserves a uniform permutation over eligible Shop identities."
            });
    }

    private static SearchSelectivityEstimate EstimateAncientOption(SearchSelectivityInput plan)
    {
        AncientSearchBranchCondition[] optionBranches = ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientBranchConditions
            .Where(item => item.IsValid && (item.OptionAny.Count != 0 || item.SeaGlassTargetAny.Count != 0 ||
                ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientOptionFilters.Any(f => f.Act == item.Act && f.Keys.All.Count > 0)))
            .ToArray();
        if (optionBranches.Length == 0)
            return SearchSelectivityEstimate.Unpriced("Probability.AncientOption.NoBranch", "No parent-scoped Ancient OptionAny predicate to price.");
        if (optionBranches.Length != 1)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.QueryLevelParentScopedMixture",
                "Multiple parent Ancient branches are priced by AncientProbabilityEstimator as one OR mixture; the physical AncientOption stage does not fabricate a global option marginal.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                new[] { "UseQueryLevelAuthority=AncientProbabilityEstimator" });
        }

        return AncientOptionProbabilityEstimator.EstimateConditionalAll(plan, optionBranches[0],
            ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientOptionFilters.Where(f => f.Act == optionBranches[0].Act).SelectMany(f => f.Keys.All).Distinct().ToArray());
    }

    private static SearchSelectivityEstimate EstimateWorld(SearchSelectivityInput plan)
    {
        bool hasAncientIdentity = ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientBranchConditions.Any(branch => branch.IsValid) ||
                                  ProbabilitySemanticProjection.From(plan).NumericalFilter.AncientIdentityFilters.Any(item => !item.IsEmpty);
        bool hasBossOrEvent = ProbabilitySemanticProjection.From(plan).NumericalFilter.BossFilters.Any(item => !item.IsEmpty) ||
                              ProbabilitySemanticProjection.From(plan).NumericalFilter.BossOrdinalFilters.Any(item => !item.IsEmpty) ||
                              ProbabilitySemanticProjection.From(plan).NumericalFilter.EventSequenceConditions.Any(item => !item.IsEmpty);
        if (hasAncientIdentity && !hasBossOrEvent)
        {
            JointSelectivityResult? ancient = AncientProbabilityEstimator.Estimate(plan);
            if (ancient is { JointlyPriced: true, Probability: not null })
            {
                return SearchSelectivityEstimate.Exact(
                    ancient.Probability.Value,
                    SearchSelectivityMethod.StructuralAssignment,
                    SearchSelectivityCoverage.ExactRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    ancient.EvidenceCode,
                    ancient.Notes + " " + ancient.Derivation,
                    ancient.Assumptions);
            }
            if (ancient is not null)
            {
                return SearchSelectivityEstimate.Unpriced(
                    ancient.EvidenceCode,
                    ancient.Notes,
                    ancient.PartiallyPriced ? SearchSelectivityConfidence.High : SearchSelectivityConfidence.Low,
                    SearchSelectivityMethod.StructuralAssignment,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    ancient.Assumptions);
            }
        }
        if (!hasAncientIdentity)
        {
            JointSelectivityResult? worldAuthority = WorldProbabilityEstimator.Estimate(plan);
            if (worldAuthority is { JointlyPriced: true, Probability: not null })
            {
                return SearchSelectivityEstimate.Exact(
                    worldAuthority.Probability.Value,
                    worldAuthority.Method == JointSelectivityCombinationMethod.WithoutReplacementJoint
                        ? SearchSelectivityMethod.WithoutReplacement
                        : SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.ExactRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    worldAuthority.EvidenceCode,
                    worldAuthority.Notes + " " + worldAuthority.Derivation,
                    worldAuthority.Assumptions);
            }
            if (worldAuthority is { PartiallyPriced: true })
            {
                return SearchSelectivityEstimate.Unpriced(
                    worldAuthority.EvidenceCode,
                    worldAuthority.Notes,
                    SearchSelectivityConfidence.High,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    worldAuthority.Assumptions);
            }
        }
        else if (hasBossOrEvent)
        {
            // Ancient identity/option generation is not Variant-conditioned by the
            // current immutable Runtime Authority. Boss/Event are normalized through
            // Act Variant inside WorldProbabilityEstimator, while Ancient keeps its own
            // shared-assignment/parent-option block. Under the 2026-08-11 probability
            // policy, stream advancement alone is not a structural dependency, so the
            // two complete logical blocks are combined as AssumedIndependent.
            JointSelectivityResult? ancient = AncientProbabilityEstimator.Estimate(plan);
            JointSelectivityResult? bossEvent = WorldProbabilityEstimator.Estimate(plan);
            if (ancient is { ExactlyImpossible: true } || bossEvent is { ExactlyImpossible: true })
            {
                return SearchSelectivityEstimate.Exact(
                    0d,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.ExactRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    "Probability.World.AncientOrBossEventImpossible",
                    "Ancient or Boss/Event normalized block is authority-proven impossible.");
            }
            if (ancient is { JointlyPriced: true, Probability: not null } &&
                bossEvent is { JointlyPriced: true, Probability: not null })
            {
                double probability = Math.Clamp(ancient.Probability.Value * bossEvent.Probability.Value, 0d, 1d);
                return SearchSelectivityEstimate.Exact(
                    probability,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.ExactRequestedConjunction,
                    SearchSelectivityDependencyClass.AssumedIndependent,
                    "Probability.Authority.AncientTimesVariantBossEvent",
                    $"Ancient block and Variant-normalized Boss/Event block are both exact and have no registered shared semantic state; P={ancient.Probability.Value:G17}×{bossEvent.Probability.Value:G17}={probability:G17}.",
                    new[]
                    {
                        "AncientBlock includes shared-Ancient assignment and parent-scoped OptionAny.",
                        "BossEventBlock pays Act Variant prior exactly once.",
                        "CrossBlockRelation=AssumedIndependentUnlessStructuralDependencyRegistered",
                        "RNG stream advancement alone is not treated as probability dependence."
                    });
            }
            return SearchSelectivityEstimate.Unpriced(
                "Probability.World.AncientOrBossEventAuthorityIncomplete",
                "Ancient and Boss/Event are separate probability blocks under the current model, but at least one block lacks complete runtime authority.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.AssumedIndependent,
                new[]
                {
                    "CrossBlockRelation=AssumedIndependentUnlessStructuralDependencyRegistered",
                    "Missing block authority is not replaced with a fixed constant."
                });
        }

        SearchSelectivityEstimate bossPair = EstimateBossOrdinalPair(plan);
        if (bossPair.IsPriced) return bossPair;

        if (IsP3BEventPairShape(ProbabilitySemanticProjection.From(plan).NumericalFilter))
        {
            return SearchSelectivityEstimate.Heuristic(
                0.00068789317d,
                SearchSelectivityConfidence.Medium,
                SearchSelectivityMethod.RuntimeObservedConditional,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.SharedContinuation,
                "P3B.Accepted.EventPairConditional",
                "Accepted P3-B Act1+Act2 event-pair survival observed after the single-loose Neow producer; not reconstructed by multiplying event marginals.",
                new[]
                {
                    "Device-independent semantic estimate, but fixture-specific accepted runtime observation.",
                    "Relic and World/Event share up_front continuation, so an extra Relic prefix invalidates this conditional estimate unless separately modeled."
                },
                conditionedOnDomains: new[] { SearchSelectivityDomain.Neow });
        }
        if (HasAncientActualShape(ProbabilitySemanticProjection.From(plan).NumericalFilter))
        {
            return SearchSelectivityEstimate.Heuristic(
                0.291539245987d,
                SearchSelectivityConfidence.Medium,
                SearchSelectivityMethod.RuntimeObservedConditional,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.SharedContinuation,
                "P3B.Accepted.PaelActualAncientConditional",
                "Accepted P3-B Actual Ancient survival conditional on the Pael event-local option PreGate.",
                new[] { "Conditional on the AncientOption PreGate; not an unconditional Ancient identity rarity." },
                conditionedOnDomains: new[] { SearchSelectivityDomain.AncientOption });
        }

        return SearchSelectivityEstimate.Unpriced(
            "P5.World.JointUnpriced",
            "Arbitrary Boss/Ancient/Event conjunctions share world continuation and are not blindly multiplied.",
            SearchSelectivityConfidence.Low,
            SearchSelectivityMethod.Unknown,
            SearchSelectivityCoverage.PartialRequestedConjunction,
            SearchSelectivityDependencyClass.SharedContinuation);
    }

    private static SearchSelectivityEstimate EstimateBossOrdinalPair(SearchSelectivityInput plan)
    {
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        if (!IsAct3TwoBossOrdinalShape(filter))
            return SearchSelectivityEstimate.Unpriced("P5.World.BossPair.NotShape", "Not the two-ordinal Boss regression shape.");
        if (plan.Ascension < 10)
            return SearchSelectivityEstimate.Exact(0d, SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "P5.Authority.Act3SecondBossA10Gate", "Second final-act Boss does not occur below Ascension 10.");

        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null ||
            !TryResolveActAuthorityForSelectivity(generation, 3, out Beta109ActGenerationSnapshot? act, out string actResolution) ||
            act is null)
        {
            return SearchSelectivityEstimate.Unpriced(
                "P5.World.BossPair.AuthorityMissing",
                "Seed-independent Act 3 selection authority cannot resolve one exact Boss pool for query-wide pricing.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        ActOrdinalModelKeySetFilter first = filter.BossOrdinalFilters.Single(item => item.Act == 3 && item.Ordinal == 1);
        ActOrdinalModelKeySetFilter second = filter.BossOrdinalFilters.Single(item => item.Act == 3 && item.Ordinal == 2);
        if (!TrySimpleIdentitySet(first.Keys, act.Bosses, out HashSet<ModelKey> firstSet) ||
            !TrySimpleIdentitySet(second.Keys, act.Bosses, out HashSet<ModelKey> secondSet))
        {
            return SearchSelectivityEstimate.Unpriced(
                "P5.World.BossPair.CompoundKeys",
                "Boss pair pricing requires simple positive identity sets without bans.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        int n = act.Bosses.Count;
        if (n < 2)
            return SearchSelectivityEstimate.Exact(0d, SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "P5.Authority.Act3BossPoolTooSmall", "Fewer than two Boss identities are available.");
        int overlap = firstSet.Intersect(secondSet, ModelKeyComparer.Instance).Count();
        double allowedOrderedPairs = (double)firstSet.Count * secondSet.Count - overlap;
        double probability = allowedOrderedPairs / (n * (n - 1d));
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.WithoutReplacement,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            "P5.Authority.Act3TwoBossesWithoutReplacement",
            $"Act3 Boss #2 excludes Boss #1: allowed ordered pairs {allowedOrderedPairs:0}/{n * (n - 1)}. For two distinct singleton targets with n=3 this is 1/6, not 1/9.",
            new[]
            {
                "Second final-act Boss pool explicitly excludes the first Boss.",
                "Act authority resolution=" + actResolution
            });
    }

    private static SearchSelectivityEstimate EstimateCombatReward(SearchSelectivityInput plan)
    {
        if (!ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiresNormalCombatRewardDomain)
            return SearchSelectivityEstimate.Unpriced("P5.CombatReward.NoPredicate", "No Combat Reward predicate to price.");
        return CombatRewardProbabilityEstimator.Estimate(plan);
    }

    private static bool TryGetLanePool(
        CompiledRelicPoolSnapshot pool,
        RelicSequenceKind lane,
        out ushort[] entries)
    {
        int laneIndex = lane switch
        {
            RelicSequenceKind.Common => 0,
            RelicSequenceKind.Uncommon => 1,
            RelicSequenceKind.Rare => 2,
            RelicSequenceKind.Shop => 3,
            _ => -1
        };
        if (laneIndex < 0 || laneIndex >= pool.PlayerLaneBucketIndexes.Length)
        {
            entries = Array.Empty<ushort>();
            return false;
        }
        int bucket = pool.PlayerLaneBucketIndexes[laneIndex];
        if (bucket < 0 || bucket >= pool.BucketLengths.Length)
        {
            entries = Array.Empty<ushort>();
            return false;
        }
        int offset = pool.BucketOffsets[bucket];
        int count = pool.BucketLengths[bucket];
        if (offset < 0 || count < 0 || offset + count > pool.DenseRelicIds.Length ||
            offset + count > pool.EntryFlags.Length)
        {
            entries = Array.Empty<ushort>();
            return false;
        }

        if (lane != RelicSequenceKind.Shop)
        {
            entries = pool.DenseRelicIds.AsSpan(offset, count).ToArray();
            return true;
        }

        // Production Shop sequence is read from the shuffled tail while skipping
        // entries whose captured IsAllowedInShops=false. A uniform permutation,
        // restricted to eligible identities and then reversed, is still a uniform
        // permutation of that eligible subset. Therefore the cleaned player-visible
        // Shop queue can use the same finite-sequence authority as the forward lanes.
        var eligible = new List<ushort>(count);
        for (int index = 0; index < count; index++)
        {
            int entryIndex = offset + index;
            if ((pool.EntryFlags[entryIndex] & (byte)Beta110RelicEntryFlags.AllowedInShops) == 0) continue;
            eligible.Add(pool.DenseRelicIds[entryIndex]);
        }
        entries = eligible.ToArray();
        return true;
    }

    private static bool TryPositiveTargetSet(
        ModelKeySetFilter keys,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        IReadOnlyCollection<ushort> laneEntries,
        out HashSet<ushort> targets)
    {
        targets = new HashSet<ushort>();
        if (keys.Ban.Count != 0 || (keys.Any.Count != 0 && keys.All.Count != 0)) return false;
        if (keys.Any.Count == 0 && keys.All.Distinct(ModelKeyComparer.Instance).Count() > 1) return false;
        IReadOnlyList<ModelKey> source = keys.Any.Count != 0 ? keys.Any : keys.All;
        if (source.Count == 0) return false;
        return TryResolveDenseTargets(source, denseByKey, laneEntries, out targets);
    }

    private static bool TryResolveDenseTargets(
        IReadOnlyList<ModelKey> keys,
        IReadOnlyDictionary<ModelKey, ushort> denseByKey,
        IReadOnlyCollection<ushort> laneEntries,
        out HashSet<ushort> targets)
    {
        var lane = laneEntries.ToHashSet();
        targets = new HashSet<ushort>();
        foreach (ModelKey key in keys.Distinct(ModelKeyComparer.Instance))
        {
            if (!denseByKey.TryGetValue(key, out ushort id) || !lane.Contains(id))
                return false;
            targets.Add(id);
        }
        return targets.Count != 0;
    }

    private static bool TrySimpleIdentitySet(
        ModelKeySetFilter filter,
        IReadOnlyList<ModelKey> authorityPool,
        out HashSet<ModelKey> targets)
    {
        targets = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        if (filter.Ban.Count != 0 || (filter.Any.Count != 0 && filter.All.Count != 0)) return false;
        if (filter.Any.Count == 0 && filter.All.Distinct(ModelKeyComparer.Instance).Count() > 1) return false;
        IReadOnlyList<ModelKey> source = filter.Any.Count != 0 ? filter.Any : filter.All;
        if (source.Count == 0) return false;
        var pool = new HashSet<ModelKey>(authorityPool, ModelKeyComparer.Instance);
        foreach (ModelKey key in source.Distinct(ModelKeyComparer.Instance))
        {
            if (!pool.Contains(key)) return false;
            targets.Add(key);
        }
        return targets.Count != 0;
    }

    internal static double WithoutReplacementUnorderedPairProbability(int poolSize, int targetCount)
    {
        if (poolSize < 2 || targetCount != 2 || targetCount > poolSize) return 0d;
        return 2d / (poolSize * (poolSize - 1d));
    }

    private static Beta109AncientEventContextSnapshot? FindAncientContext(
        SearchSelectivityInput plan,
        AncientSearchBranchCondition branch)
    {
        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        return generation?.AncientEventContexts.FirstOrDefault(context =>
            context.Act == branch.Act && context.AncientKey == branch.AncientKey &&
            (context.PlayerSlot == plan.Authority.PlayerSlotIndex || context.IsShared));
    }

    private static bool AncientCatalogUsable(
        Beta109AncientEventContextSnapshot context,
        Beta109AncientOptionCatalogSnapshot catalog) =>
        context.EventContextExact &&
        context.ModifierFactsExact &&
        catalog.CatalogExact &&
        catalog.Pools.All(pool =>
            pool.SourceOrdinal >= 0 && pool.OrderExact && pool.FilterResultExact);

    private static IReadOnlyList<ModelKey> BuildPaelPool3(
        SearchSelectivityInput plan,
        Beta109AncientOptionCatalogSnapshot catalog)
    {
        var pool = catalog.Pool("pael.pool3.base").ToList();
        if (ProbabilitySemanticProjection.From(plan).Context.EvaluationAssumptions
            .AncientEligibilityAssumptions.PaelAllowLegionNoEventPet)
        {
            pool.AddRange(catalog.Pool("pael.pool3.no-event-pet"));
        }
        return pool;
    }

    internal static bool TryResolveActAuthorityForSelectivity(
        Beta109WorldGenerationSnapshot generation,
        int act,
        out Beta109ActGenerationSnapshot? snapshot,
        out string resolution)
    {
        ArgumentNullException.ThrowIfNull(generation);
        snapshot = null;

        // Per-seed Production projection may already materialize SelectedActs.
        // Query-wide P5 planning normally sees the seed-independent runtime
        // snapshot where SelectedActs is intentionally empty. Exact V1 pricing
        // may use that snapshot only when Act-selection authority resolves this
        // Act to one unique catalog entry; otherwise the joint Act-selection
        // mixture remains explicitly Unpriced.
        if (generation.SelectedActsExact && generation.SelectedActs.Count > 0)
        {
            snapshot = FindSelectedAct(generation, act);
            if (snapshot is not null && snapshot.HasExactGenerationInputs)
            {
                resolution = "MaterializedSelectedActs";
                return true;
            }
        }

        if (!generation.ActSelectionAuthorityExact)
        {
            resolution = "ActSelectionAuthorityInexact";
            return false;
        }

        Beta109ActSelectionGroupSnapshot[] groups = generation.ActSelectionGroups
            .Where(group => group.Act == act)
            .ToArray();
        if (groups.Length != 1 || !groups[0].EligibilityAndOrderExact)
        {
            resolution = groups.Length == 1
                ? "ActSelectionGroupInexact"
                : "ActSelectionGroupMissingOrAmbiguous";
            return false;
        }

        IReadOnlyList<ModelKey> eligible = groups[0].EligibleActsInSourceOrder;
        if (eligible.Count != 1 || !eligible[0].IsValid)
        {
            resolution = "MultipleEligibleActsRequireJointActSelectionModel";
            return false;
        }

        snapshot = generation.OrderedActCatalog.FirstOrDefault(item => item.ActKey == eligible[0]);
        if (snapshot is null || snapshot.Act != act || !snapshot.HasExactGenerationInputs)
        {
            snapshot = null;
            resolution = "UniqueActCatalogAuthorityIncomplete";
            return false;
        }

        resolution = "UniqueSeedIndependentActSelectionAuthority";
        return true;
    }

    private static Beta109ActGenerationSnapshot? FindSelectedAct(
        Beta109WorldGenerationSnapshot generation,
        int act)
    {
        foreach (ModelKey selectedKey in generation.SelectedActs)
        {
            Beta109ActGenerationSnapshot? snapshot = generation.OrderedActCatalog
                .FirstOrDefault(item => item.ActKey == selectedKey);
            if (snapshot?.Act == act) return snapshot;
        }
        return null;
    }

    private static bool IsSimpleRequiredCurseIdentity(NeowSearchFilter filter, out ModelKey required)
    {
        required = default;
        ModelKey[] all = filter.NeowRelics.All.Distinct(ModelKeyComparer.Instance).ToArray();
        if (filter.NeowRoute is not null || filter.NeowRelics.Any.Count != 0 || filter.NeowRelics.Ban.Count != 0 || all.Length != 1)
            return false;
        if (filter.StructuredNeowEffects.Count != 0 || filter.EffectOutputConditions.Count != 0 ||
            filter.RequireNeowsBones || !filter.BonesRelics.IsEmpty || filter.RequiredBonesCombination.Count != 0 ||
            filter.RequiredBonesAcquisitionOrder.Count != 0 || filter.RequireSmallCapsule || filter.RequireLargeCapsule ||
            !filter.CapsuleContainedRelics.IsEmpty || filter.RequireWhetstone || filter.RequireWarPaint ||
            filter.RequiredFinalCurse.HasValue || filter.BannedFinalCurses.Count != 0 || filter.Preset != NeowSearchPreset.None)
            return false;
        required = all[0];
        return required.IsValid;
    }

    private static bool IsSingleLooseNeowIdentity(NeowSearchFilter filter) =>
        filter.NeowRoute is { IsValid: true } &&
        filter.StructuredNeowEffects.Count == 0 &&
        filter.EffectOutputConditions.Count == 0 &&
        !filter.RequireNeowsBones &&
        filter.RequiredBonesCombination.Count == 0 &&
        filter.RequiredBonesAcquisitionOrder.Count == 0 &&
        !filter.RequireSmallCapsule && !filter.RequireLargeCapsule &&
        !filter.RequireWhetstone && !filter.RequireWarPaint &&
        filter.CapsuleContainedRelics.IsEmpty &&
        !filter.RequiredFinalCurse.HasValue && filter.BannedFinalCurses.Count == 0;

    private static bool IsBonesGrantShape(NeowSearchFilter filter)
    {
        int combination = filter.RequiredBonesCombination.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).Count();
        int order = filter.RequiredBonesAcquisitionOrder.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).Count();
        int targetCount = order != 0 ? order : combination;
        bool bonesIdentity = filter.RequireNeowsBones ||
            filter.NeowRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones ||
            targetCount is 1 or 2;
        return bonesIdentity && targetCount is 1 or 2 &&
               filter.StructuredNeowEffects.Count == 0 &&
               filter.EffectOutputConditions.Count == 0 &&
               !filter.RequireSmallCapsule && !filter.RequireLargeCapsule &&
               filter.CapsuleContainedRelics.IsEmpty &&
               !filter.RequireWhetstone && !filter.RequireWarPaint &&
               !filter.RequiredFinalCurse.HasValue && filter.BannedFinalCurses.Count == 0;
    }

    private static bool HasNestedOrDeckCoupledNeowCompound(NeowSearchFilter filter) =>
        filter.RequireSmallCapsule || filter.RequireLargeCapsule ||
        !filter.CapsuleContainedRelics.IsEmpty ||
        filter.RequireWhetstone || filter.RequireWarPaint ||
        filter.StructuredNeowEffects.Any(condition =>
            condition.Scope == NeowStructuredEffectScope.NestedRelics ||
            condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule ||
            condition.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);

    private static bool IsSameLaneTwoTargetFirstTwo(NeowSearchFilter filter) =>
        filter.RelicSequenceConditions.Count == 1 &&
        filter.RelicSequenceConditions[0].RangeMode == SearchSequenceRangeMode.FirstN &&
        filter.RelicSequenceConditions[0].RangeValue == 2 &&
        filter.RelicSequenceConditions[0].Keys.Any.Count == 0 &&
        filter.RelicSequenceConditions[0].Keys.All.Distinct(ModelKeyComparer.Instance).Count() == 2 &&
        filter.RelicSequenceConditions[0].Keys.Ban.Count == 0;

    private static string AncientShape(NeowSearchFilter filter)
    {
        AncientSearchBranchCondition? branch = filter.AncientBranchConditions
            .FirstOrDefault(item => item.IsValid && item.OptionAny.Count != 0);
        if (branch is null) return "AncientOption.Default";
        string key = branch.AncientKey.Serialized;
        if (key.EndsWith(":PAEL", StringComparison.Ordinal)) return "AncientOption.Pael";
        if (key.EndsWith(":VAKUU", StringComparison.Ordinal)) return "AncientOption.Vakuu";
        if (key.EndsWith(":DARV", StringComparison.Ordinal)) return "AncientOption.DarvAssignment";
        return "AncientOption.Default";
    }

    private static bool HasAncientActualShape(NeowSearchFilter filter) =>
        filter.AncientBranchConditions.Any(branch => branch.IsValid && branch.OptionAny.Count != 0);

    private static bool IsAct3TwoBossOrdinalShape(NeowSearchFilter filter)
    {
        if (filter.BossFilters.Any(item => !item.IsEmpty) ||
            filter.AncientBranchConditions.Any(item => item.IsValid) ||
            filter.AncientIdentityFilters.Any(item => !item.IsEmpty) ||
            filter.AncientOptionFilters.Any(item => !item.IsEmpty) ||
            filter.AncientSeaGlassTargetFilters.Any(item => !item.IsEmpty) ||
            filter.EventSequenceConditions.Any(item => !item.IsEmpty))
            return false;
        ActOrdinalModelKeySetFilter[] active = filter.BossOrdinalFilters.Where(item => !item.IsEmpty).ToArray();
        return active.Length == 2 &&
               active.All(item => item.Act == 3) &&
               active.Any(item => item.Ordinal == 1) &&
               active.Any(item => item.Ordinal == 2);
    }

    private static bool IsP3BEventPairShape(NeowSearchFilter filter)
    {
        if (filter.EventSequenceConditions.Count != 2) return false;
        if (filter.BossFilters.Any(item => !item.IsEmpty) || filter.BossOrdinalFilters.Any(item => !item.IsEmpty)) return false;
        foreach (EventSequenceSearchCondition condition in filter.EventSequenceConditions)
        {
            int positiveIdentityCount = condition.Keys.Any.Count + condition.Keys.All.Count;
            if (condition.RangeMode != SearchSequenceRangeMode.ExactSlot || condition.RangeValue != 1 ||
                positiveIdentityCount != 1 || condition.Keys.Ban.Count != 0)
                return false;
        }
        return true;
    }
}
