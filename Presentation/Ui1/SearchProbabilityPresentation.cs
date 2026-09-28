using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Presentation.Ui1;

internal enum SearchProbabilityQuickViewStatus : byte
{
    Complete = 0,
    Partial = 1,
    Unavailable = 2,
    Impossible = 3
}

internal enum SearchProbabilityRowKind : byte
{
    Neow = 1,
    Relic = 2,
    CapsuleRelicJoint = 3,
    WorldEvent = 4,
    Ancient = 5,
    CombatReward = 6,
    EventResult = 7,
    Shop = 8,
    TransformationAggregate = 9
}

internal sealed record SearchProbabilityQuickViewRow(
    SearchProbabilityRowKind Kind,
    double? Probability,
    ProbabilityExplanation? Explanation = null);

internal sealed record SearchProbabilityQuickView(
    SearchProbabilityQuickViewStatus Status,
    double? TotalProbability,
    IReadOnlyList<SearchProbabilityQuickViewRow> Rows,
    int UnknownComponentCount,
    SearchImpossibilityProof? ImpossibilityProof = null)
{
    public static SearchProbabilityQuickView Unavailable { get; } = new(
        SearchProbabilityQuickViewStatus.Unavailable,
        null,
        Array.Empty<SearchProbabilityQuickViewRow>(),
        0);
}

/// <summary>
/// Player-facing Search probability presentation. This layer owns no probability
/// mathematics and never invokes the physical/cost Planner. It projects the
/// existing Joint/Domain selectivity authorities into a compact product view.
/// </summary>
internal static class SearchProbabilityPresentationBuilder
{
    public static SearchProbabilityQuickView Build(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        SearchSelectivityInput input = SearchSelectivityInput.From(compiled);
        JointSelectivityResult joint = JointSelectivityEstimator.EstimateQuery(input);
        return Build(compiled, joint);
    }

    internal static SearchProbabilityQuickView Build(CompiledSearch compiled, JointSelectivityResult joint)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(joint);

        SearchSelectivityInput input = SearchSelectivityInput.From(compiled);
        NeowSearchFilter filter = input.Filter;
        SearchFeasibilityResult feasibility = SearchFeasibilityAnalyzer.Analyze(compiled);
        var rows = new List<SearchProbabilityQuickViewRow>(6);

        var resolved = CompiledSearchEvaluationProjector.Project(compiled).Evaluation;
        bool sharedOpeningIdentity = TransformationAggregateCondition.SharedNeowIdentityOnly(resolved);
        TransformationAggregateProbability? aggregateAttribution = null;
        if(sharedOpeningIdentity) {
            var request=new ExactSearchExecutionRequest(compiled,new("000000000000",1,1,1),"000000000000",1,
                resolved,input.CombatRewardRoutePolicy,compiled.SemanticFingerprint);
            aggregateAttribution=TransformationAggregateProbability.Build(new(request));
        }
        bool capsuleRelicShared = joint.DependencyGraph.Nodes.Any(node =>
            string.Equals(node.Id, "shared:relicbag-cur", StringComparison.Ordinal)) ||
            joint.KnownComponents.Any(component => string.Equals(component.Id, "shared:capsule-relic-cur", StringComparison.Ordinal));
        bool hasBagRelic = filter.RelicSequenceConditions.Any(condition =>
            !condition.IsEmpty && condition.Lane != RelicSequenceKind.Shop);
        bool hasTypedShopRelic = filter.RelicShopSequenceConditions.Any(condition => !condition.IsEmpty);
        bool hasLegacyShopRelic = filter.RelicSequenceConditions.Any(condition =>
            !condition.IsEmpty && condition.Lane == RelicSequenceKind.Shop);
        bool hasShopRelic = hasTypedShopRelic || hasLegacyShopRelic;
        double? shopRelicProbability = FindComponentProbability(joint, "remainder:relic-shop");

        if (capsuleRelicShared)
        {
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.CapsuleRelicJoint,
                FindComponentProbability(joint, "shared:capsule-relic-cur") * (hasShopRelic ? shopRelicProbability : 1d)));
        }
        else
        {
            bool openingOwnedByT = sharedOpeningIdentity;
            if(openingOwnedByT && aggregateAttribution is { } opening)
                rows.Add(new SearchProbabilityQuickViewRow(SearchProbabilityRowKind.Neow,opening.IdentityProbability));
            if (filter.HasNeowConstraints && !openingOwnedByT)
            {
                rows.Add(new SearchProbabilityQuickViewRow(
                    SearchProbabilityRowKind.Neow,
                    FindDomainProbability(joint, SearchSelectivityDomain.Neow) ?? SearchSelectivityEstimator.EstimateStage(input, SearchSelectivityDomain.Neow).Probability));
            }

            if (hasBagRelic || hasShopRelic)
            {
                // Shop relics belong to R, regardless of the editor page. Regroup
                // the existing distinct-lane factors without changing query probability.
                double? relicProbability = hasTypedShopRelic
                    ? (hasBagRelic ? FindComponentProbability(joint, "domain:relic-cur") : 1d) * shopRelicProbability
                    : FindDomainProbability(joint, SearchSelectivityDomain.Relic) ??
                        SearchSelectivityEstimator.EstimateStage(input, SearchSelectivityDomain.Relic).Probability;
                rows.Add(new SearchProbabilityQuickViewRow(SearchProbabilityRowKind.Relic, relicProbability));
            }
        }

        // Family attribution follows resolved predicates, not the page that authored them.
        // W owns Ancient identity; A owns the parent-conditioned offer/result predicate.
        bool hasAncientIdentity = resolved.AncientBranchConditions.Any(branch => branch.IsValid) ||
                                  resolved.AncientIdentityFilters.Any(item => !item.IsEmpty);
        bool hasAncientOptions = resolved.AncientBranchConditions.Any(branch => branch.OptionAny.Count > 0 || branch.SeaGlassTargetAny.Count > 0) ||
                                 resolved.AncientOptionFilters.Any(item => !item.IsEmpty) ||
                                 resolved.AncientSeaGlassTargetFilters.Any(item => !item.IsEmpty);
        double? identityProbability = null;
        if (hasAncientIdentity)
        {
            var identity = AncientIdentityProbabilityEstimator.Estimate(input);
            if (identity.IsPriced) identityProbability = identity.Probability;
        }
        if (hasAncientOptions)
        {
            var ancient = AncientProbabilityEstimator.Estimate(input);
            // P(options | accepted identities) = P(identity AND options) / P(identity).
            // This retains the authority's OR mixtures and cross-Act shared assignment;
            // multiplying individual branch option marginals would be incorrect.
            double? conditional = resolved.AncientBranchConditions.Any(branch => branch.IsValid) && ancient?.JointlyPriced == true && ancient.Probability is { } combined &&
                                  identityProbability is > 0 && combined <= identityProbability.Value + 1e-12
                ? Math.Clamp(combined / identityProbability.Value, 0, 1) : null;
            rows.Add(new SearchProbabilityQuickViewRow(SearchProbabilityRowKind.Ancient, conditional));
        }

        bool hasBossEvent = resolved.BossFilters.Any(item => !item.IsEmpty) ||
                            resolved.BossOrdinalFilters.Any(item => !item.IsEmpty) ||
                            resolved.EventSequenceConditions.Any(item => !item.IsEmpty) || compiled.NormalizedQuery.VariantBossBranches.Count > 0;
        if (hasBossEvent || hasAncientIdentity)
        {
            double? worldProbability;
            if (WorldVariantQueryProbability.Needed(input))
            {
                // The flat World authority cannot see authored Variant branches.
                // Reuse the query mixture on W's predicates so the selected Variant
                // prior and its Boss/Event/Ancient identities are counted together.
                var query = compiled.NormalizedQuery;
                var worldQuery = SearchQuery.Empty with
                {
                    VariantBossBranches = query.VariantBossBranches,
                    EventSequenceConstraints = query.EventSequenceConstraints,
                    AncientBranches = query.AncientBranches.Select(branch => branch with
                    {
                        OptionAny = [],
                        SeaGlassTargetAny = []
                    }).ToArray(),
                    LegacyWorld = query.LegacyWorld with
                    {
                        AncientOptionFilters = [],
                        AncientSeaGlassTargetFilters = []
                    }
                };
                worldProbability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(
                    SearchCompiler.Compile(worldQuery, compiled.Context))).Probability;
            }
            else
            {
                var world = hasBossEvent ? WorldProbabilityEstimator.Estimate(input) : null;
                worldProbability = hasBossEvent ? world?.JointlyPriced == true ? world.Probability : null : 1d;
                if (hasAncientIdentity)
                    worldProbability = identityProbability == 0 || worldProbability == 0 ? 0 :
                        identityProbability.HasValue && worldProbability.HasValue ? identityProbability.Value * worldProbability.Value : null;
            }
            rows.Add(new SearchProbabilityQuickViewRow(SearchProbabilityRowKind.WorldEvent, worldProbability));
        }

        EventResultSearchCondition[] eventResults = filter.EventResultConditions
            .Where(condition => condition.IsValid)
            .ToArray();
        if (eventResults.Length > 0)
        {
            SearchSelectivityEstimate? eventBlock = EventResultProbabilityEstimator.EstimateBlock(input, eventResults, out _);
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.EventResult,
                eventBlock?.IsPriced == true ? eventBlock.Probability : null));
        }

        bool hasShopColorless = filter.MerchantColorlessConditions.Any(condition => condition.IsValid) ||
                                filter.MerchantColorlessSequenceConditions.Any(condition => !condition.IsEmpty);
        if (hasShopColorless)
        {
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.Shop,
                FindComponentProbability(joint, "shop-colorless:conditional")));
        }

        if (filter.RequiresNormalCombatRewardDomain)
        {
            SearchSelectivityEstimate estimate =
                SearchSelectivityEstimator.EstimateStage(input, SearchSelectivityDomain.CombatReward);
            ProbabilityExplanation? explanation = estimate.Explanations.FirstOrDefault(item =>
                item.Kind == ProbabilityExplanationKind.NecessaryImplicitEnabler &&
                item.TargetDomain == SearchSelectivityDomain.CombatReward);
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.CombatReward,
                estimate.Probability,
                explanation));
        }

        if (compiled.NormalizedQuery.TransformationAggregate is not null)
            rows.Add(new(SearchProbabilityRowKind.TransformationAggregate,
                aggregateAttribution is {IdentityProbability: >0, QueryHitProbability: { } aggregateP}
                    ? aggregateP/aggregateAttribution.IdentityProbability
                    : aggregateAttribution is not null ? null : FindComponentProbability(joint, "transformation-aggregate")));

        SearchProbabilityQuickViewStatus status = feasibility.IsImpossible
            ? SearchProbabilityQuickViewStatus.Impossible
            : joint.JointlyPriced
                ? SearchProbabilityQuickViewStatus.Complete
                : joint.PartiallyPriced
                    ? SearchProbabilityQuickViewStatus.Partial
                    : SearchProbabilityQuickViewStatus.Unavailable;

        return new SearchProbabilityQuickView(
            status,
            status == SearchProbabilityQuickViewStatus.Complete ? joint.Probability : null,
            rows,
            joint.UnknownComponents.Count,
            feasibility.IsImpossible ? feasibility.Proof : null);
    }
    private static double? FindComponentProbability(JointSelectivityResult joint, string componentId) =>
        joint.KnownComponents
            .FirstOrDefault(component => string.Equals(component.Id, componentId, StringComparison.Ordinal))
            ?.Probability;

    private static double? FindDomainProbability(JointSelectivityResult joint, SearchSelectivityDomain domain)
    {
        JointSelectivityComponent? component = joint.KnownComponents.FirstOrDefault(item =>
            string.Equals(item.Id, "domain:" + domain, StringComparison.Ordinal) ||
            (string.Equals(item.Id, "condition", StringComparison.Ordinal) &&
             string.Equals(item.Condition, domain.ToString(), StringComparison.Ordinal)));
        return component?.Probability;
    }


}
