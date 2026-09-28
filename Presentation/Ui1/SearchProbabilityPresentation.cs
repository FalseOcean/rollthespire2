using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

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
    Shop = 8
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

        bool capsuleRelicShared = joint.DependencyGraph.Nodes.Any(node =>
            string.Equals(node.Id, "shared:relicbag-cur", StringComparison.Ordinal)) ||
            joint.KnownComponents.Any(component => string.Equals(component.Id, "shared:capsule-relic-cur", StringComparison.Ordinal));
        bool hasCurRelic = filter.RelicSequenceConditions.Any(condition =>
            !condition.IsEmpty && condition.Lane is RelicSequenceKind.Common or RelicSequenceKind.Uncommon or RelicSequenceKind.Rare);
        bool hasTypedShopRelic = filter.RelicShopSequenceConditions.Any(condition => !condition.IsEmpty);
        bool hasLegacyShopRelic = filter.RelicSequenceConditions.Any(condition =>
            !condition.IsEmpty && condition.Lane == RelicSequenceKind.Shop);

        if (capsuleRelicShared)
        {
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.CapsuleRelicJoint,
                FindComponentProbability(joint, "shared:capsule-relic-cur")));
        }
        else
        {
            if (filter.HasNeowConstraints)
            {
                rows.Add(new SearchProbabilityQuickViewRow(
                    SearchProbabilityRowKind.Neow,
                    FindDomainProbability(joint, SearchSelectivityDomain.Neow)));
            }

            if (hasCurRelic)
            {
                double? relicProbability = FindComponentProbability(joint, "domain:relic-cur");
                if (!relicProbability.HasValue && !hasTypedShopRelic)
                    relicProbability = FindDomainProbability(joint, SearchSelectivityDomain.Relic);
                rows.Add(new SearchProbabilityQuickViewRow(SearchProbabilityRowKind.Relic, relicProbability));
            }
        }

        bool hasAncient = filter.AncientBranchConditions.Any(branch => branch.IsValid) ||
                          filter.AncientIdentityFilters.Any(item => !item.IsEmpty);
        if (hasAncient)
        {
            JointSelectivityResult? ancient = AncientProbabilityEstimator.Estimate(input);
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.Ancient,
                ancient?.JointlyPriced == true ? ancient.Probability : null));
        }

        bool hasBossEvent = filter.BossFilters.Any(item => !item.IsEmpty) ||
                            filter.BossOrdinalFilters.Any(item => !item.IsEmpty) ||
                            filter.EventSequenceConditions.Any(item => !item.IsEmpty);
        if (hasBossEvent)
        {
            JointSelectivityResult? world = WorldProbabilityEstimator.Estimate(input);
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.WorldEvent,
                world?.JointlyPriced == true ? world.Probability : null));
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
        bool hasShopRelic = hasTypedShopRelic || hasLegacyShopRelic;
        if (hasShopRelic || hasShopColorless)
        {
            double? relicShopProbability = hasTypedShopRelic
                ? FindComponentProbability(joint, "remainder:relic-shop")
                : hasLegacyShopRelic && !hasCurRelic
                    ? FindDomainProbability(joint, SearchSelectivityDomain.Relic)
                    : null;
            double? colorlessProbability = FindComponentProbability(joint, "shop-colorless:conditional");
            double? shopProbability = hasShopRelic && hasShopColorless
                ? FindComponentProbability(joint, "presentation:shop")
                : hasShopRelic
                    ? relicShopProbability
                    : colorlessProbability;
            rows.Add(new SearchProbabilityQuickViewRow(
                SearchProbabilityRowKind.Shop,
                shopProbability));
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
