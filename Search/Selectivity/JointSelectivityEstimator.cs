using System.Globalization;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Query-wide probability authority. This is deliberately separate from the
/// physical Planner DAG. Logical SharedConstraints are normalized inside their
/// domain block first; remaining cross-domain blocks follow the current policy:
/// AssumedIndependent unless a registered structural/conditional relation exists.
/// </summary>
internal static class JointSelectivityEstimator
{
    private sealed record WorldBranchEstimate(
        ModelKey ActKey,
        double Prior,
        double? EventProbability,
        bool EventEligibilityKnown,
        bool EventEligible,
        double? BossProbability,
        bool BossEligibilityKnown,
        bool BossEligible,
        double? ConditionalJoint,
        bool Pruned,
        string Reason,
        IReadOnlyList<JointSelectivityComponent> Components);

    public static JointSelectivityResult EstimateQuery(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CompiledSearch.Context.Party is not null)
            return PartyQueryProbability.Estimate(plan.CompiledSearch);
        if (plan.CompiledSearch.Query.TransformationAggregate is not null)
            return TransformationAggregateQueryProbability.Compose(plan, EstimateQuery);
        ProbabilitySemanticView semantic = ProbabilitySemanticProjection.From(plan);
        JointSelectivityResult result = EstimateQueryCore(plan, semantic);

        string[] diagnostics = semantic.DerivedDiagnostics
            .Concat(new[]
            {
                "ProbabilityUsesNormalizedSearchQuery=true",
                "SemanticProjectionSource=" + semantic.SemanticSource,
                "NormalizationStatus=" + semantic.NormalizationStatus,
                "NormalizationBlock=" + (semantic.NormalizationBlocks.Count == 0 ? "None" : string.Join(",", semantic.NormalizationBlocks)),
                "RouteSource=ResolvedRouteSemantics:" + semantic.Routes.CombatReward.Kind,
                "SemanticDagNotProbabilityDag=true"
            })
            .ToArray();

        return result with { ProbabilitySemanticDiagnostics = diagnostics };
    }

    private static JointSelectivityResult EstimateQueryCore(
        SearchSelectivityInput plan,
        ProbabilitySemanticView semantic)
    {
        SearchFeasibilityResult feasibility = SearchFeasibilityAnalyzer.Analyze(plan.CompiledSearch);
        if (feasibility.IsImpossible)
        {
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.Semantic.FeasibilityProofImpossible",
                "Canonical semantics plus immutable authority proved the satisfying outcome set empty before probability pricing.",
                "SearchFeasibilityAnalyzer=Impossible ⇒ P(Query)=0",
                assumptions: new[]
                {
                    "ProbabilityConsumesFeasibilityProof=true",
                    "ProbabilityIsNotFeasibilityAuthority=true"
                },
                dependencyCoverage: "CompleteAuthoritativeImpossibleProof");
        }

        if (semantic.NormalizationStatus == QueryNormalizationStatus.Impossible)
        {
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.Semantic.NormalizationImpossible",
                "SearchCompiler normalization proved the query impossible; Probability does not rerun domain estimators to rediscover the contradiction.",
                "CompiledSearchNormalizationStatus=Impossible ⇒ P(Query)=0",
                assumptions: new[]
                {
                    "ProbabilitySemanticSource=CompiledSearch",
                    "SemanticImpossibleDominatesProbabilityPricing=true"
                },
                dependencyCoverage: "CompleteSemanticImpossibleProof");
        }

        if (plan.CompiledSearch.NormalizedQuery.StandardMaps.Count > 0)
            return StandardMapProbabilityEstimator.Compose(plan, EstimateQuery);

        if (WorldVariantQueryProbability.Needed(plan))
            return WorldVariantQueryProbability.Compose(plan, EstimateQuery);

        if (!semantic.NumericalProjectionUsable)
        {
            return JointSelectivityResult.Unpriced(
                "Probability.Semantic.NumericalProjectionNotReusable",
                "The normalized SearchQuery cannot be represented by the existing numerical estimator contracts without semantic loss.",
                "Probability remains CompiledSearch-derived and fails closed instead of pricing a lossy/unsupported compatibility projection.",
                new[] { "NumericalProjectionFidelity=" + semantic.NumericalProjectionFidelity },
                assumptions: new[] { "AllowedNumericalProjectionFidelity=Exact|EquivalentUnderCurrentRuntime" });
        }

        EventResultSearchCondition[] eventResults = semantic.NumericalFilter.EventResultConditions
            .Where(condition => condition.IsValid)
            .ToArray();
        MerchantColorlessSlotCondition[] merchantColorless = semantic.NumericalFilter.MerchantColorlessConditions
            .Where(condition => condition.IsValid)
            .ToArray();
        MerchantColorlessSequenceSearchCondition[] merchantColorlessSequences = semantic.NumericalFilter.MerchantColorlessSequenceConditions
            .Where(condition => !condition.IsEmpty)
            .ToArray();

        SearchSelectivityDomain[] activeDomains = semantic.ActiveDomains().ToArray();

        if (merchantColorlessSequences.Length > 0 || merchantColorless.Length > 0)
        {
            SearchSelectivityEstimate shopColorless = SearchSelectivityEstimator.EstimateMerchantColorlessConditions(plan);
            if (!shopColorless.IsPriced || !shopColorless.Probability.HasValue)
            {
                return JointSelectivityResult.Unpriced(
                    shopColorless.EvidenceCode,
                    shopColorless.Notes,
                    "Merchant Colorless sequence probability remains Unknown because runtime pool/shape authority or the requested local conjunction is unavailable.",
                    new[] { "MerchantColorless:" + shopColorless.EvidenceCode },
                    assumptions: shopColorless.Assumptions);
            }
            ProbabilitySemanticView withoutShopColorless = semantic with
            {
                NumericalFilter = semantic.NumericalFilter with
                {
                    MerchantColorlessSequenceConditions = Array.Empty<MerchantColorlessSequenceSearchCondition>(),
                    MerchantColorlessConditions = Array.Empty<MerchantColorlessSlotCondition>()
                }
            };
            JointSelectivityResult baseResult = EstimateQueryCore(plan, withoutShopColorless);
            return ComposeShopColorlessBlock(baseResult, shopColorless);
        }

        if (eventResults.Length > 0)
        {
            SearchSelectivityEstimate? eventBlock = TryEstimateEventResultBlock(
                plan,
                eventResults,
                out string eventIssue);
            if (eventBlock is null)
            {
                return JointSelectivityResult.Unpriced(
                    "Probability.EventResult.UnsupportedConjunction",
                    "The selected Event Result conjunction contains a repeated local group without a registered joint model.",
                    "Probability fails closed for the unsupported local conjunction; Search/Exact/Witness remain unchanged.",
                    new[] { eventIssue },
                    assumptions: new[] { "EventResultLocalJointAuthorityRequired=true" });
            }

            // Reuse the complete existing estimator for every non-EventResult
            // domain, then add the known conditional result block once. This keeps
            // Event occurrence, Neow, World, and other domains on their existing
            // pricing path while making the cross-domain assumption explicit.
            ProbabilitySemanticView withoutEventResults = semantic with
            {
                NumericalFilter = semantic.NumericalFilter with
                {
                    EventResultConditions = Array.Empty<EventResultSearchCondition>()
                }
            };
            JointSelectivityResult baseResult = EstimateQueryCore(plan, withoutEventResults);
            return ComposeEventResultBlock(baseResult, eventBlock);
        }

        if (activeDomains.Length == 0)
        {
            return JointSelectivityResult.Exact(
                1d,
                JointSelectivityCombinationMethod.SingleAuthorityTerm,
                "Joint.NoPredicates",
                "An unconstrained query matches every root.",
                "Root query contains no selectivity-bearing predicates.");
        }

        // Direct Capsule and Bones-nested Capsule outputs constrain the same
        // initial player RelicGrabBag observed by the explicit Relic-sequence domain.
        // Normalize that SharedConstraint before any cross-domain product so the
        // same queue facts are paid exactly once.
        if (activeDomains.Contains(SearchSelectivityDomain.Neow) &&
            activeDomains.Contains(SearchSelectivityDomain.Relic) &&
            CapsuleRelicProbabilityEstimator.HasCapsuleRelicSharedConstraint(plan))
        {
            return ComposeCapsuleRelicSharedBlock(plan, activeDomains);
        }

        // Modern Shop Relic conditions are a typed lane contract, not the legacy
        // RelicSequenceKind.Shop projection. Normalize the typed Shop lane into an
        // explicit remainder component before generic domain composition so both
        // Probability presentation and diagnostics consume the same authority term.
        if (activeDomains.Contains(SearchSelectivityDomain.Relic) &&
            semantic.NumericalFilter.RelicShopSequenceConditions.Any(condition => !condition.IsEmpty))
        {
            return ComposeTypedRelicShopBlock(plan, activeDomains);
        }

        if (activeDomains.Length == 1 && activeDomains[0] == SearchSelectivityDomain.WorldEvent)
        {
            bool hasAncientIdentity = semantic.NumericalFilter.AncientBranchConditions.Any(branch => branch.IsValid) ||
                                      semantic.NumericalFilter.AncientIdentityFilters.Any(item => !item.IsEmpty);
            if (!hasAncientIdentity)
            {
                JointSelectivityResult? world = WorldProbabilityEstimator.Estimate(plan);
                if (world is not null) return world;
            }
        }

        if (activeDomains.Length == 1)
        {
            SearchSelectivityDomain domain = activeDomains[0];
            SearchSelectivityEstimate stage = SearchSelectivityEstimator.EstimateStage(plan, domain);
            return FromSingleStage(stage, domain);
        }

        var stageEstimates = activeDomains
            .Select(domain => (Domain: domain, Estimate: SearchSelectivityEstimator.EstimateStage(plan, domain)))
            .ToArray();

        // A provable impossible factor makes the entire conjunction impossible even if
        // another factor is still unpriced. This keeps Impossible distinct from Unknown.
        var impossibleCandidates = stageEstimates
            .Where(item => item.Estimate.Probability == 0d && item.Estimate.PricingClass == SearchSelectivityPricingClass.ExactPriced)
            .ToArray();
        if (impossibleCandidates.Length != 0)
        {
            var impossibleFactor = impossibleCandidates[0];
            var component = ToComponent(impossibleFactor.Domain, impossibleFactor.Estimate, "Exact impossible factor");
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                "Joint.StructuralImpossibleFactor",
                "One normalized domain constraint is authority-proven impossible, so the full conjunction has probability zero.",
                $"P({impossibleFactor.Domain})=0 ⇒ P(Query)=0",
                BuildFlatDomainGraph(stageEstimates),
                new[] { component },
                assumptions: new[] { "Impossible dominates unresolved independent/conditional factors." },
                dependencyCoverage: "CompleteImpossibleProof");
        }

        if (CanUseCurrentProbabilityComposition(stageEstimates, out string compositionIssue))
        {
            bool hasConditional = stageEstimates.Any(item => item.Estimate.ConditionedOnDomains.Count != 0);
            double probability = stageEstimates.Aggregate(1d, (value, item) => value * item.Estimate.Probability!.Value);
            var components = stageEstimates.Select(item => ToComponent(
                item.Domain,
                item.Estimate,
                item.Estimate.ConditionedOnDomains.Count == 0
                    ? "AssumedIndependentUnlessStructuralDependencyRegistered"
                    : "ConditionalOn=" + string.Join(',', item.Estimate.ConditionedOnDomains))).ToArray();

            var graph = BuildFlatDomainGraph(stageEstimates, useCurrentPolicy: true);
            return JointSelectivityResult.Exact(
                probability,
                hasConditional ? JointSelectivityCombinationMethod.ConditionalChain : JointSelectivityCombinationMethod.IndependentProduct,
                hasConditional ? "Joint.ConditionalThenAssumedIndependent" : "Joint.AssumedIndependentProduct",
                hasConditional
                    ? "Every domain factor is priced; registered conditional factors are evaluated as P(child|parent), then remaining blocks use the current AssumedIndependent policy."
                    : "Every domain factor is priced and no registered structural/conditional relation blocks product composition; current policy treats the blocks as AssumedIndependent.",
                "P(Query)=" + string.Join(" × ", stageEstimates.Select(item => F(item.Estimate.Probability))) + "=" + F(probability),
                graph,
                components,
                assumptions: new[]
                {
                    "CrossDomainIndependencePolicy=AssumedIndependentUnlessStructuralDependencyRegistered",
                    "AuditedIndependent remains a stronger provenance when explicit source authority exists.",
                    "Known Conditional/SharedConstraint relations must be normalized before this product step.",
                    "CompositionCheck=" + compositionIssue
                },
                dependencyCoverage: hasConditional ? "CompleteConditionalPlusAssumedIndependence" : "CompleteAssumedIndependence");
        }

        var known = stageEstimates
            .Where(item => item.Estimate.Probability.HasValue)
            .Select(item => ToComponent(item.Domain, item.Estimate, "Known factor; full query remains incomplete."))
            .ToArray();
        string[] unknown = stageEstimates
            .Where(item => !item.Estimate.IsPriced || !item.Estimate.Probability.HasValue)
            .Select(item => item.Domain + ":" + item.Estimate.EvidenceCode)
            .Append("Composition:" + compositionIssue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return JointSelectivityResult.Partial(
            "Joint.CrossDomain.PartialCoverage",
            "At least one domain probability or registered conditional/shared-constraint relation is still unpriced.",
            "Known factors are exposed for diagnostics; final P(Query) remains Unknown and PlanningSurvivalUpperBound remains 1.0.",
            known,
            unknown,
            BuildFlatDomainGraph(stageEstimates, useCurrentPolicy: true),
            assumptions: new[]
            {
                "CrossDomainIndependencePolicy=AssumedIndependentUnlessStructuralDependencyRegistered",
                "Unknown is retained only for missing domain/conditional/shared-constraint authority, not merely for unaudited RNG stream independence.",
                "RepresentativeSeedForQueryProbability=false"
            });
    }

    private static SearchSelectivityEstimate? TryEstimateEventResultBlock(
        SearchSelectivityInput plan,
        IReadOnlyList<EventResultSearchCondition> conditions,
        out string issue)
        => EventResultProbabilityEstimator.EstimateBlock(plan, conditions, out issue);

    private static JointSelectivityResult ComposeEventResultBlock(
        JointSelectivityResult baseResult,
        SearchSelectivityEstimate eventBlock)
    {
        JointSelectivityComponent eventComponent = new(
            "event-result:conditional",
            "EventResult:" + eventBlock.EvidenceCode,
            "ConditionalEventLocalResult",
            eventBlock.Probability,
            eventBlock.EvidenceCode,
            eventBlock.Method == SearchSelectivityMethod.NamedStreamIndependenceModel
                ? JointSelectivityCombinationMethod.IndependentProduct
                : JointSelectivityCombinationMethod.SingleAuthorityTerm,
            eventBlock.DependencyClass,
            true,
            eventBlock.Probability.GetValueOrDefault() > 0d,
            "Conditional Event Result block; combined with other domains only under the Probability assumed-independent policy.",
            eventBlock.Assumptions);

        var components = baseResult.KnownComponents.Concat(new[] { eventComponent }).ToArray();
        var assumptions = baseResult.Assumptions
            .Concat(eventBlock.Assumptions)
            .Concat(new[]
            {
                "EventResultCrossDomainRelation=AssumedIndependentUnlessStructuralDependencyRegistered",
                "EventResultProbabilityOnly=true",
                "SearchExactWitnessAuthorityUnaffected=true"
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var graphNodes = baseResult.DependencyGraph.Nodes.ToList();
        var graphEdges = baseResult.DependencyGraph.Edges.ToList();
        if (!graphNodes.Any(node => node.Id == "event-result:conditional"))
        {
            graphNodes.Add(new SelectivityDependencyNode(
                "event-result:conditional",
                SelectivityDependencyNodeKind.ObservableCondition,
                "Conditional Event Result",
                eventBlock.Probability,
                eventBlock.EvidenceCode,
                eventBlock.DependencyClass,
                eventBlock.Assumptions));
        }
        if (graphNodes.Any(node => node.Id == "query"))
        {
            graphEdges.Add(new SelectivityDependencyEdge(
                "event-result:conditional",
                "query",
                SelectivityDependencyEdgeKind.AssumedIndependent,
                eventBlock.EvidenceCode,
                "No registered Event occurrence/cross-domain structural dependency; current Probability policy permits product composition."));
        }
        SelectivityDependencyGraph graph = new(graphNodes, graphEdges);

        if (baseResult.ExactlyImpossible || baseResult.Probability == 0d || eventBlock.Probability == 0d)
        {
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.EventResult.CrossDomainImpossible",
                "An authority-proven impossible base factor or Event Result factor makes the full conjunction impossible.",
                "P(Query)=P(Base)×P(EventResult)=0",
                graph,
                components,
                assumptions: assumptions,
                dependencyCoverage: "CompleteWithEventResultAssumedIndependence");
        }

        if (baseResult.Priced && baseResult.Probability.HasValue)
        {
            double probability = baseResult.Probability.Value * eventBlock.Probability!.Value;
            return JointSelectivityResult.Exact(
                probability,
                JointSelectivityCombinationMethod.IndependentProduct,
                "Probability.EventResult.CrossDomainAssumedIndependent",
                "Every base domain factor is priced, and the known conditional Event Result block is composed with it using the existing assumed-independent Probability policy.",
                "P(Query)=P(Base)×P(EventResult)=" + F(baseResult.Probability) + "×" + F(eventBlock.Probability) + "=" + F(probability),
                graph,
                components,
                assumptions: assumptions,
                dependencyCoverage: "CompleteWithEventResultAssumedIndependence");
        }

        return JointSelectivityResult.Partial(
            "Probability.EventResult.BasePartial",
            "The Event Result block is known, but another domain or registered relation remains unpriced.",
            "Event Result probability is retained as a known component; no unsupported product is fabricated for the unresolved base query.",
            components,
            baseResult.UnknownComponents.Concat(new[] { "BaseQuery:" + baseResult.EvidenceCode }).Distinct(StringComparer.Ordinal).ToArray(),
            graph,
            assumptions: assumptions,
            dependencyCoverage: "EventResultKnown_BasePartial");
    }

    private static JointSelectivityResult ComposeShopColorlessBlock(
        JointSelectivityResult baseResult,
        SearchSelectivityEstimate shopBlock)
    {
        JointSelectivityComponent component = new(
            "shop-colorless:conditional",
            "MerchantColorless:U/R",
            "NormalMerchantShops",
            shopBlock.Probability,
            shopBlock.EvidenceCode,
            shopBlock.Method == SearchSelectivityMethod.NamedStreamIndependenceModel
                ? JointSelectivityCombinationMethod.IndependentProduct
                : JointSelectivityCombinationMethod.SingleAuthorityTerm,
            shopBlock.DependencyClass,
            true,
            shopBlock.Probability.GetValueOrDefault() > 0d,
            "Known Normal Merchant U/R queue factor; composed with other priced domains under Probability policy only.",
            shopBlock.Assumptions);
        var components = baseResult.KnownComponents.Concat(new[] { component }).ToList();
        JointSelectivityComponent? relicShopComponent = baseResult.KnownComponents.FirstOrDefault(item =>
            string.Equals(item.Id, "remainder:relic-shop", StringComparison.Ordinal));
        if (relicShopComponent?.Probability is double relicShopProbability && shopBlock.Probability is double colorlessProbability)
        {
            double shopAggregateProbability = Math.Clamp(relicShopProbability * colorlessProbability, 0d, 1d);
            components.Add(new JointSelectivityComponent(
                "presentation:shop",
                "Shop",
                "PresentationAggregateOnly",
                shopAggregateProbability,
                "Probability.Shop.PresentationAggregate",
                JointSelectivityCombinationMethod.IndependentProduct,
                SearchSelectivityDependencyClass.AssumedIndependent,
                true,
                shopAggregateProbability > 0d,
                "Presentation aggregate over already-authoritative Shop Relic and Shop Colorless components; not an additional query factor.",
                new[]
                {
                    "PresentationAggregateOnly=true",
                    "RelicShopComponent=remainder:relic-shop",
                    "ColorlessShopComponent=shop-colorless:conditional",
                    "AdditionalProbabilityFactor=false"
                }));
        }
        else if (relicShopComponent is null && shopBlock.Probability is double colorlessOnlyProbability)
        {
            components.Add(new JointSelectivityComponent(
                "presentation:shop",
                "Shop",
                "PresentationAggregateOnly",
                colorlessOnlyProbability,
                "Probability.Shop.PresentationAggregate",
                JointSelectivityCombinationMethod.SingleAuthorityTerm,
                shopBlock.DependencyClass,
                true,
                colorlessOnlyProbability > 0d,
                "Presentation aggregate aliases the single authoritative Shop Colorless component; not an additional query factor.",
                new[]
                {
                    "PresentationAggregateOnly=true",
                    "ColorlessShopComponent=shop-colorless:conditional",
                    "AdditionalProbabilityFactor=false"
                }));
        }
        var assumptions = baseResult.Assumptions
            .Concat(shopBlock.Assumptions)
            .Concat(new[]
            {
                "MerchantColorlessCrossDomainRelation=AssumedIndependentUnlessStructuralDependencyRegistered",
                "MerchantColorlessProbabilityOnly=true",
                "SearchExactWitnessAuthorityUnaffected=true"
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nodes = baseResult.DependencyGraph.Nodes.ToList();
        var edges = baseResult.DependencyGraph.Edges.ToList();
        if (!nodes.Any(node => node.Id == "shop-colorless:conditional"))
        {
            nodes.Add(new SelectivityDependencyNode(
                "shop-colorless:conditional",
                SelectivityDependencyNodeKind.ObservableCondition,
                "Normal Merchant Colorless U/R",
                shopBlock.Probability,
                shopBlock.EvidenceCode,
                shopBlock.DependencyClass,
                shopBlock.Assumptions));
        }
        if (nodes.Any(node => node.Id == "query"))
        {
            edges.Add(new SelectivityDependencyEdge(
                "shop-colorless:conditional",
                "query",
                SelectivityDependencyEdgeKind.AssumedIndependent,
                shopBlock.EvidenceCode,
                "No registered Shop Colorless cross-domain dependency; current Probability policy permits product composition."));
        }
        SelectivityDependencyGraph graph = new(nodes, edges);

        if (baseResult.ExactlyImpossible || baseResult.Probability == 0d || shopBlock.Probability == 0d)
        {
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                "Probability.MerchantColorless.CrossDomainImpossible",
                "An authority-proven impossible base factor or Shop Colorless factor makes the full conjunction impossible.",
                "P(Query)=P(Base)×P(MerchantColorless)=0",
                graph,
                components,
                assumptions: assumptions,
                dependencyCoverage: "CompleteWithMerchantColorlessAssumedIndependence");
        }
        if (baseResult.Priced && baseResult.Probability.HasValue)
        {
            double probability = baseResult.Probability.Value * shopBlock.Probability!.Value;
            return JointSelectivityResult.Exact(
                probability,
                JointSelectivityCombinationMethod.IndependentProduct,
                "Probability.MerchantColorless.CrossDomainAssumedIndependent",
                "Every base domain factor and the known Shop Colorless U/R factor are priced; the existing assumed-independent policy combines them.",
                "P(Query)=P(Base)×P(MerchantColorless)=" + F(baseResult.Probability) + "×" + F(shopBlock.Probability) + "=" + F(probability),
                graph,
                components,
                assumptions: assumptions,
                dependencyCoverage: "CompleteWithMerchantColorlessAssumedIndependence");
        }
        return JointSelectivityResult.Partial(
            "Probability.MerchantColorless.BasePartial",
            "Shop Colorless U/R probability is known, but another domain or registered relation remains unpriced.",
            "The Shop Colorless factor is preserved without fabricating a product for an unresolved base query.",
            components,
            baseResult.UnknownComponents.Concat(new[] { "BaseQuery:" + baseResult.EvidenceCode }).Distinct(StringComparer.Ordinal).ToArray(),
            graph,
            assumptions: assumptions,
            dependencyCoverage: "MerchantColorlessKnown_BasePartial");
    }


    private static JointSelectivityResult ComposeTypedRelicShopBlock(
        SearchSelectivityInput plan,
        IReadOnlyList<SearchSelectivityDomain> activeDomains)
    {
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        RelicSequenceSearchCondition[] legacyShop = filter.RelicSequenceConditions
            .Where(condition => !condition.IsEmpty && condition.Lane == RelicSequenceKind.Shop)
            .ToArray();
        RelicSequenceSearchCondition[] curConditions = filter.RelicSequenceConditions
            .Where(condition => !condition.IsEmpty && condition.Lane != RelicSequenceKind.Shop)
            .ToArray();
        RelicShopSequenceSearchCondition[] typedShop = filter.RelicShopSequenceConditions
            .Where(condition => !condition.IsEmpty)
            .ToArray();

        // Keep the pre-existing domain-level estimator as the query-total authority.
        // The typed split below exists so Presentation/Diagnostics can consume the same
        // authoritative subcomponents without re-running a legacy Shop projection.
        var stageEstimates = activeDomains
            .Select(domain => (Domain: domain, Estimate: SearchSelectivityEstimator.EstimateStage(plan, domain)))
            .ToArray();
        SearchSelectivityEstimate relicAggregate = stageEstimates
            .Single(item => item.Domain == SearchSelectivityDomain.Relic)
            .Estimate;

        SearchSelectivityEstimate shop = typedShop.Length == 1 && legacyShop.Length == 0
            ? SearchSelectivityEstimator.EstimateRelicShopConditions(plan, typedShop[0])
            : SearchSelectivityEstimate.Unpriced(
                "Probability.Relic.ShopSequenceJointShapeUnsupported",
                legacyShop.Length != 0
                    ? "Modern typed Shop Relic sequence conditions cannot be combined with the superseded legacy Shop lane projection."
                    : "Multiple typed Shop Relic sequence conditions target the same no-replacement lane; Probability does not multiply or silently merge them.");
        SearchSelectivityEstimate? cur = curConditions.Length == 0
            ? null
            : SearchSelectivityEstimator.EstimateRelicConditions(plan, curConditions);

        JointSelectivityComponent? curComponent = cur is null
            ? null
            : new JointSelectivityComponent(
                "domain:relic-cur",
                "RelicQueue.CUR",
                "InitialPlayerRelicGrabBag.CUR",
                cur.Probability,
                cur.EvidenceCode,
                MapMethod(cur.Method),
                cur.DependencyClass,
                cur.Probability.HasValue,
                cur.Probability.GetValueOrDefault() > 0d,
                "Non-Shop Relic lanes remain one authority term; typed Shop is priced separately.",
                cur.Assumptions);
        JointSelectivityComponent shopComponent = new(
            "remainder:relic-shop",
            "RelicQueue.Shop",
            "InitialPlayerRelicGrabBag.Shop",
            shop.Probability,
            shop.EvidenceCode,
            MapMethod(shop.Method),
            shop.DependencyClass == SearchSelectivityDependencyClass.StructuralDependence
                ? SearchSelectivityDependencyClass.AssumedIndependent
                : shop.DependencyClass,
            shop.Probability.HasValue,
            shop.Probability.GetValueOrDefault() > 0d,
            "Typed Shop Relic lane is a distinct runtime-lane remainder.",
            shop.Assumptions.Concat(new[]
            {
                "RelicShopUsesTypedContract=true",
                "RelicShopLaneDistinctFromCommonUncommonRare=true"
            }).ToArray());

        var splitComponents = new List<JointSelectivityComponent>();
        if (curComponent is not null && cur?.Probability.HasValue == true) splitComponents.Add(curComponent);
        if (shop.Probability.HasValue) splitComponents.Add(shopComponent);
        splitComponents.AddRange(stageEstimates
            .Where(item => item.Domain != SearchSelectivityDomain.Relic && item.Estimate.Probability.HasValue)
            .Select(item => ToComponent(
                item.Domain,
                item.Estimate,
                item.Estimate.ConditionedOnDomains.Count == 0
                    ? "AssumedIndependentFromTypedShopRelic"
                    : "ConditionalOn=" + string.Join(',', item.Estimate.ConditionedOnDomains))));

        var nodes = new List<SelectivityDependencyNode>
        {
            new("query", SelectivityDependencyNodeKind.QueryRoot, "Query", null, "QuerySemanticModel",
                SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>()),
            new("remainder:relic-shop", SelectivityDependencyNodeKind.ObservableCondition, "Relic Queue Shop predicates",
                shop.Probability, shop.EvidenceCode, SearchSelectivityDependencyClass.AssumedIndependent, shop.Assumptions)
        };
        var edges = new List<SelectivityDependencyEdge>
        {
            new("remainder:relic-shop", "query", SelectivityDependencyEdgeKind.AssumedIndependent, shop.EvidenceCode,
                "Typed Shop Relic is a distinct runtime lane and is not folded into Common/Uncommon/Rare probability facts.")
        };
        if (cur is not null)
        {
            nodes.Add(new SelectivityDependencyNode(
                "domain:relic-cur", SelectivityDependencyNodeKind.ObservableCondition, "Relic Queue C/U/R predicates",
                cur.Probability, cur.EvidenceCode, cur.DependencyClass, cur.Assumptions));
            edges.Add(new SelectivityDependencyEdge(
                "domain:relic-cur", "query", SelectivityDependencyEdgeKind.AssumedIndependent, cur.EvidenceCode,
                "Common/Uncommon/Rare and typed Shop lanes are distinct runtime lanes under the existing Probability policy."));
        }
        foreach ((SearchSelectivityDomain domain, SearchSelectivityEstimate estimate) in stageEstimates.Where(item => item.Domain != SearchSelectivityDomain.Relic))
        {
            string id = "domain:" + domain;
            nodes.Add(new SelectivityDependencyNode(id, SelectivityDependencyNodeKind.ObservableCondition, domain.ToString(),
                estimate.Probability, estimate.EvidenceCode, estimate.DependencyClass, estimate.Assumptions));
            edges.Add(new SelectivityDependencyEdge(id, "query", MapEdge(estimate.DependencyClass), estimate.EvidenceCode,
                estimate.ConditionedOnDomains.Count == 0
                    ? "No registered structural dependency with typed Shop Relic."
                    : estimate.Notes));
        }
        var graph = new SelectivityDependencyGraph(nodes, edges);

        var impossibleCandidates = stageEstimates
            .Where(item => item.Estimate.Probability == 0d && item.Estimate.PricingClass == SearchSelectivityPricingClass.ExactPriced)
            .ToArray();
        if (impossibleCandidates.Length != 0)
        {
            var impossibleFactor = impossibleCandidates[0];
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                "Joint.StructuralImpossibleFactor",
                "One normalized domain constraint is authority-proven impossible, so the full conjunction has probability zero.",
                $"P({impossibleFactor.Domain})=0 ⇒ P(Query)=0",
                graph,
                splitComponents,
                assumptions: new[]
                {
                    "Impossible dominates unresolved independent/conditional factors.",
                    "RelicShopUsesTypedContract=true"
                },
                dependencyCoverage: "CompleteImpossibleProofWithTypedRelicShopProjection");
        }

        // A query whose only active numerical domain is Relic can still contain
        // two typed logical lane factors: C/U/R and Shop. Do not require a second
        // SearchSelectivityDomain merely to price this already-normalized Relic block.
        // This is especially important for Shop-Relic-only queries, where the typed
        // Shop estimator is itself the complete base-query authority.
        if (stageEstimates.Length == 1 && stageEstimates[0].Domain == SearchSelectivityDomain.Relic)
        {
            var relicComponents = new List<JointSelectivityComponent>();
            if (curComponent is not null && cur?.Probability.HasValue == true) relicComponents.Add(curComponent);
            if (shop.Probability.HasValue) relicComponents.Add(shopComponent);

            if (shop.IsPriced && shop.Probability.HasValue && (cur is null || cur.IsPriced && cur.Probability.HasValue))
            {
                double relicProbability = shop.Probability.Value;
                if (cur?.Probability is double curProbability) relicProbability *= curProbability;
                relicProbability = Math.Clamp(relicProbability, 0d, 1d);
                return JointSelectivityResult.Exact(
                    relicProbability,
                    cur is null ? JointSelectivityCombinationMethod.SingleAuthorityTerm : JointSelectivityCombinationMethod.IndependentProduct,
                    cur is null ? "Joint.TypedRelicShop.SingleDomain" : "Joint.TypedRelicShop.SingleDomainLaneProduct",
                    cur is null
                        ? "The typed Shop Relic condition is the only active probability factor and is fully priced by its typed lane estimator."
                        : "C/U/R and typed Shop are distinct Relic runtime lanes and are composed once under the existing distinct-lane Probability policy.",
                    cur is null
                        ? "P(Query)=P(Relic.Shop)=" + F(relicProbability)
                        : "P(Query)=P(Relic.CUR)×P(Relic.Shop)=" + F(cur!.Probability) + "×" + F(shop.Probability) + "=" + F(relicProbability),
                    graph,
                    relicComponents,
                    assumptions: new[]
                    {
                        "RelicShopUsesTypedContract=true",
                        "RelicShopLaneDistinctFromCommonUncommonRare=true",
                        "SingleSearchDomainDoesNotImplySingleLogicalProbabilityFactor=true",
                        "PresentationDoesNotReestimateShop=true"
                    },
                    dependencyCoverage: cur is null ? "CompleteTypedRelicShopSingleDomain" : "CompleteTypedRelicCURPlusShopSingleDomain");
            }

            string[] relicUnknown = new[]
            {
                !shop.IsPriced || !shop.Probability.HasValue ? "Relic.Shop:" + shop.EvidenceCode : string.Empty,
                cur is not null && (!cur.IsPriced || !cur.Probability.HasValue) ? "Relic.CUR:" + cur.EvidenceCode : string.Empty
            }.Where(value => value.Length != 0).ToArray();
            return JointSelectivityResult.Partial(
                "Joint.TypedRelicShop.SingleDomainPartial",
                "The typed Relic lane block is recognized, but at least one requested Relic lane factor is unpriced.",
                "Known C/U/R or Shop lane components are retained; no presentation-layer multiplication is used.",
                relicComponents,
                relicUnknown,
                graph,
                assumptions: new[]
                {
                    "RelicShopUsesTypedContract=true",
                    "RelicShopLaneDistinctFromCommonUncommonRare=true",
                    "PresentationDoesNotReestimateShop=true"
                },
                dependencyCoverage: "TypedRelicSingleDomainPartial");
        }

        if (!CanUseCurrentProbabilityComposition(stageEstimates, out string compositionIssue))
        {
            string[] unknown = stageEstimates
                .Where(item => !item.Estimate.IsPriced || !item.Estimate.Probability.HasValue)
                .Select(item => item.Domain + ":" + item.Estimate.EvidenceCode)
                .Append("Composition:" + compositionIssue)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return JointSelectivityResult.Partial(
                "Joint.TypedRelicShop.Partial",
                "Typed Shop Relic is exposed as its own component, but the existing domain composition policy cannot fully price the query.",
                "Known typed Shop/C/U/R/domain factors are retained; P(Query) remains Unknown.",
                splitComponents,
                unknown,
                graph,
                assumptions: new[]
                {
                    "RelicShopUsesTypedContract=true",
                    "RelicShopLaneDistinctFromCommonUncommonRare=true",
                    "CrossDomainIndependencePolicy=AssumedIndependentUnlessStructuralDependencyRegistered",
                    "CompositionCheck=" + compositionIssue
                },
                dependencyCoverage: "TypedRelicShopProjection_Partial");
        }

        bool hasConditional = stageEstimates.Any(item => item.Estimate.ConditionedOnDomains.Count != 0);
        double probability = stageEstimates.Aggregate(1d, (value, item) => value * item.Estimate.Probability!.Value);
        probability = Math.Clamp(probability, 0d, 1d);
        return JointSelectivityResult.Exact(
            probability,
            hasConditional ? JointSelectivityCombinationMethod.ConditionalChain :
                stageEstimates.Length == 1 ? MapMethod(relicAggregate.Method) : JointSelectivityCombinationMethod.IndependentProduct,
            "Joint.TypedRelicShop.Normalized",
            "The existing domain-level Probability result remains total authority; typed Shop Relic is exposed as an independent remainder component for Presentation/Diagnostics.",
            "P(Query)=" + string.Join(" × ", stageEstimates.Select(item => F(item.Estimate.Probability))) + "=" + F(probability),
            graph,
            splitComponents,
            assumptions: new[]
            {
                "RelicShopUsesTypedContract=true",
                "RelicShopLaneDistinctFromCommonUncommonRare=true",
                "CrossDomainIndependencePolicy=AssumedIndependentUnlessStructuralDependencyRegistered",
                "PresentationDoesNotReestimateShop=true"
            },
            dependencyCoverage: "CompleteTypedRelicShopProjection");
    }

    private static JointSelectivityResult ComposeCapsuleRelicSharedBlock(
        SearchSelectivityInput plan,
        IReadOnlyList<SearchSelectivityDomain> activeDomains)
    {
        SearchSelectivityEstimate shared = NeowStructuredEffectProbabilityEstimator.EstimateWithRelicSharedConstraints(plan);
        RelicShopSequenceSearchCondition[] shopConditions = CapsuleRelicProbabilityEstimator.IndependentShopRelicSequenceConditions(plan);
        SearchSelectivityEstimate? shop = shopConditions.Length switch
        {
            0 => null,
            1 => SearchSelectivityEstimator.EstimateRelicShopConditions(plan, shopConditions[0]),
            _ => SearchSelectivityEstimate.Unpriced(
                "Probability.Relic.ShopSequenceJointShapeUnsupported",
                "Multiple typed Shop Relic sequence conditions target the same no-replacement lane; Probability does not multiply or silently merge them.")
        };

        SearchSelectivityDomain[] remainingDomains = activeDomains
            .Where(domain => domain is not SearchSelectivityDomain.Neow and not SearchSelectivityDomain.Relic)
            .ToArray();
        var remaining = remainingDomains
            .Select(domain => (Domain: domain, Estimate: SearchSelectivityEstimator.EstimateStage(plan, domain)))
            .ToArray();

        var sharedComponent = new JointSelectivityComponent(
            "shared:capsule-relic-cur",
            "Neow.Capsule/BonesCapsule × RelicQueue.CUR",
            "InitialPlayerRelicGrabBag.CUR",
            shared.Probability,
            shared.EvidenceCode,
            JointSelectivityCombinationMethod.WithoutReplacementJoint,
            SearchSelectivityDependencyClass.StructuralDependence,
            shared.Probability.HasValue,
            shared.Probability.GetValueOrDefault() > 0d,
            "SharedConstraintNormalizedBeforeProduct",
            shared.Assumptions);

        JointSelectivityComponent? shopComponent = shop is null
            ? null
            : new JointSelectivityComponent(
                "remainder:relic-shop",
                "RelicQueue.Shop",
                "InitialPlayerRelicGrabBag.Shop",
                shop.Probability,
                shop.EvidenceCode,
                JointSelectivityCombinationMethod.WithoutReplacementJoint,
                shop.DependencyClass == SearchSelectivityDependencyClass.StructuralDependence
                    ? SearchSelectivityDependencyClass.AssumedIndependent
                    : shop.DependencyClass,
                shop.Probability.HasValue,
                shop.Probability.GetValueOrDefault() > 0d,
                "IndependentShopRuntimeLaneRemainder",
                shop.Assumptions.Concat(new[]
                {
                    "CapsuleDoesNotRuntimeConsumeShopLane=true",
                    "ShopRemainderCombinedByDistinctLaneIndependencePolicy=true"
                }).ToArray());

        var graphNodes = new List<SelectivityDependencyNode>
        {
            new("query", SelectivityDependencyNodeKind.QueryRoot, "Query", null, "QuerySemanticModel",
                SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>()),
            new("shared:relicbag-cur", SelectivityDependencyNodeKind.FinitePool, "Initial Player RelicGrabBag C/U/R lanes", null,
                "RuntimeAuthority.RelicGrabBag.CUR", SearchSelectivityDependencyClass.StructuralDependence,
                new[] { "Capsule and Common/Uncommon/Rare Relic Sequence constraints share these runtime-consumed lanes." }),
            new("shared:capsule", SelectivityDependencyNodeKind.ObservableCondition, "Capsule nested Relic", null,
                shared.EvidenceCode, SearchSelectivityDependencyClass.StructuralDependence, shared.Assumptions),
            new("shared:relicquery-cur", SelectivityDependencyNodeKind.ObservableCondition, "Relic Queue C/U/R predicates", null,
                shared.EvidenceCode, SearchSelectivityDependencyClass.StructuralDependence, shared.Assumptions)
        };
        var graphEdges = new List<SelectivityDependencyEdge>
        {
            new("shared:relicbag-cur", "shared:capsule", SelectivityDependencyEdgeKind.WithoutReplacement,
                "ProductionSource.RelicGrabBag.CUR", "Capsule pulls constrain the same runtime-consumed rarity-specific initial bag lanes."),
            new("shared:relicbag-cur", "shared:relicquery-cur", SelectivityDependencyEdgeKind.WithoutReplacement,
                "ProductionSource.RelicGrabBag.CUR", "Common/Uncommon/Rare Relic Queue predicates observe the same initial lanes."),
            new("shared:capsule", "query", SelectivityDependencyEdgeKind.SharedParent,
                shared.EvidenceCode, "Capsule and overlapping C/U/R Relic constraints are normalized as one shared probability block."),
            new("shared:relicquery-cur", "query", SelectivityDependencyEdgeKind.SharedParent,
                shared.EvidenceCode, "The shared C/U/R initial bag facts are not paid a second time.")
        };

        if (shop is not null)
        {
            graphNodes.Add(new SelectivityDependencyNode(
                "remainder:relic-shop",
                SelectivityDependencyNodeKind.ObservableCondition,
                "Relic Queue Shop predicates",
                shop.Probability,
                shop.EvidenceCode,
                SearchSelectivityDependencyClass.AssumedIndependent,
                shop.Assumptions));
            graphEdges.Add(new SelectivityDependencyEdge(
                "remainder:relic-shop",
                "query",
                SelectivityDependencyEdgeKind.AssumedIndependent,
                shop.EvidenceCode,
                "Capsule does not runtime-consume the Shop lane; Shop is an independent Relic remainder under the current distinct-lane policy."));
        }

        foreach ((SearchSelectivityDomain domain, SearchSelectivityEstimate estimate) in remaining)
        {
            string id = "domain:" + domain;
            graphNodes.Add(new SelectivityDependencyNode(
                id,
                SelectivityDependencyNodeKind.ObservableCondition,
                domain.ToString(),
                estimate.Probability,
                estimate.EvidenceCode,
                estimate.DependencyClass,
                estimate.Assumptions));
            if (estimate.ConditionedOnDomains.Count != 0)
            {
                foreach (SearchSelectivityDomain parent in estimate.ConditionedOnDomains)
                {
                    string parentId = parent is SearchSelectivityDomain.Neow or SearchSelectivityDomain.Relic
                        ? "shared:capsule"
                        : "domain:" + parent;
                    graphEdges.Add(new SelectivityDependencyEdge(
                        parentId, id, SelectivityDependencyEdgeKind.ConditionalOn,
                        estimate.EvidenceCode, $"{domain} is evaluated conditionally on {parent}."));
                }
                graphEdges.Add(new SelectivityDependencyEdge(id, "query", SelectivityDependencyEdgeKind.ConditionalOn,
                    estimate.EvidenceCode, estimate.Notes));
            }
            else
            {
                graphEdges.Add(new SelectivityDependencyEdge(id, "query", SelectivityDependencyEdgeKind.AssumedIndependent,
                    estimate.EvidenceCode, "No additional registered structural dependency with the normalized Capsule×Relic C/U/R block."));
            }
        }
        var graph = new SelectivityDependencyGraph(graphNodes, graphEdges);

        if (shared.IsPriced && shared.Probability == 0d)
        {
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                shared.EvidenceCode,
                "Capsule and explicit Common/Uncommon/Rare Relic Queue constraints conflict on the same runtime initial grab-bag facts.",
                "P(Capsule×RelicQueue.CUR)=0 ⇒ P(Query)=0",
                graph,
                new[] { sharedComponent },
                assumptions: shared.Assumptions,
                dependencyCoverage: "CompleteSharedConstraintImpossible");
        }

        if (shop is { IsPriced: true, Probability: 0d })
        {
            var components = new List<JointSelectivityComponent>();
            if (shared.Probability.HasValue) components.Add(sharedComponent);
            if (shopComponent is not null) components.Add(shopComponent);
            return JointSelectivityResult.Exact(
                0d,
                JointSelectivityCombinationMethod.ExactImpossible,
                shop.EvidenceCode,
                "The independent Shop Relic Queue remainder is authority-proven impossible.",
                "P(Relic.Shop)=0 ⇒ P(Query)=0",
                graph,
                components,
                assumptions: shop.Assumptions,
                dependencyCoverage: "CompleteShopRemainderImpossible");
        }

        if (!shared.IsPriced || !shared.Probability.HasValue)
        {
            var knownRemaining = new List<JointSelectivityComponent>();
            if (shopComponent is not null && shop?.Probability.HasValue == true) knownRemaining.Add(shopComponent);
            knownRemaining.AddRange(remaining
                .Where(item => item.Estimate.Probability.HasValue)
                .Select(item => ToComponent(item.Domain, item.Estimate, "Known remainder outside unresolved Capsule×Relic C/U/R SharedConstraint.")));
            return JointSelectivityResult.Partial(
                "Joint.CapsuleRelic.SharedConstraintPartial",
                "The query contains a recognized Capsule×Relic C/U/R shared bag relation, but its normalized probability is not fully priceable.",
                "Shared C/U/R constraint recognized; Shop remains a separate remainder and naive Neow×full-Relic marginal multiplication is forbidden.",
                knownRemaining,
                new[] { "CapsuleRelic.CUR:" + shared.EvidenceCode },
                graph,
                assumptions: shared.Assumptions.Concat(new[] { "NaiveFullRelicMarginalMultiplicationUsed=false" }).ToArray(),
                confidence: shared.Confidence,
                dependencyCoverage: "SharedConstraintRecognized_Partial");
        }

        if (shop is not null && (!shop.IsPriced || !shop.Probability.HasValue))
        {
            var known = new List<JointSelectivityComponent> { sharedComponent };
            known.AddRange(remaining
                .Where(item => item.Estimate.Probability.HasValue)
                .Select(item => ToComponent(item.Domain, item.Estimate, "Known remainder outside unresolved Shop Relic component.")));
            return JointSelectivityResult.Partial(
                "Joint.CapsuleRelic.ShopRemainderPartial",
                "Capsule×C/U/R is exactly normalized, but the independent Shop Relic remainder is unpriced.",
                "P(Query)=P(Capsule×RelicQueue.CUR)×P(Relic.Shop)×…; Shop is Unknown.",
                known,
                new[] { "Relic.Shop:" + shop.EvidenceCode },
                graph,
                assumptions: shared.Assumptions.Concat(shop.Assumptions).Concat(new[]
                {
                    "CapsuleDoesNotRuntimeConsumeShopLane=true",
                    "NaiveFullRelicMarginalMultiplicationUsed=false"
                }).ToArray(),
                confidence: shop.Confidence,
                dependencyCoverage: "SharedConstraintComplete_ShopRemainderPartial");
        }

        // The normalized block consumes logical Neow and the overlapping C/U/R
        // portion of the Relic domain. Shop, when present, is already represented
        // as its own independent remainder above.
        foreach ((SearchSelectivityDomain domain, SearchSelectivityEstimate estimate) in remaining)
        {
            if (!estimate.IsPriced || !estimate.Probability.HasValue)
            {
                var known = new List<JointSelectivityComponent> { sharedComponent };
                if (shopComponent is not null) known.Add(shopComponent);
                known.AddRange(remaining
                    .Where(item => item.Estimate.Probability.HasValue)
                    .Select(item => ToComponent(item.Domain, item.Estimate, "Known remainder; final query incomplete.")));
                string[] unknown = remaining
                    .Where(item => !item.Estimate.IsPriced || !item.Estimate.Probability.HasValue)
                    .Select(item => item.Domain + ":" + item.Estimate.EvidenceCode)
                    .ToArray();
                return JointSelectivityResult.Partial(
                    "Joint.CapsuleRelic.WithRemainderPartial",
                    "Capsule×Relic C/U/R is exactly normalized, but at least one remaining domain is unpriced.",
                    "Known shared and Shop components are preserved; final P(Query) remains Unknown.",
                    known,
                    unknown,
                    graph,
                    assumptions: shared.Assumptions.Concat(new[] { "NaiveFullRelicMarginalMultiplicationUsed=false" }).ToArray(),
                    confidence: SearchSelectivityConfidence.High,
                    dependencyCoverage: "SharedConstraintComplete_RemainderPartial");
            }
            foreach (SearchSelectivityDomain parent in estimate.ConditionedOnDomains)
            {
                bool parentPresent = parent is SearchSelectivityDomain.Neow or SearchSelectivityDomain.Relic ||
                                     remainingDomains.Contains(parent);
                if (!parentPresent)
                {
                    return JointSelectivityResult.Partial(
                        "Joint.CapsuleRelic.ConditionalParentMissing",
                        $"Remaining domain {domain} requires missing conditional parent {parent}.",
                        "Conditional composition is withheld.",
                        shopComponent is null ? new[] { sharedComponent } : new[] { sharedComponent, shopComponent! },
                        new[] { $"ConditionalParentMissing:{domain}<-{parent}" },
                        graph,
                        assumptions: shared.Assumptions);
                }
            }
        }

        double probability = shared.Probability.Value;
        if (shop is not null) probability *= shop.Probability!.Value;
        foreach (var item in remaining) probability *= item.Estimate.Probability!.Value;
        probability = Math.Clamp(probability, 0d, 1d);

        var finalComponents = new List<JointSelectivityComponent> { sharedComponent };
        if (shopComponent is not null) finalComponents.Add(shopComponent);
        finalComponents.AddRange(remaining.Select(item => ToComponent(
            item.Domain,
            item.Estimate,
            item.Estimate.ConditionedOnDomains.Count == 0
                ? "AssumedIndependentFromNormalizedSharedBlock"
                : "ConditionalOn=" + string.Join(',', item.Estimate.ConditionedOnDomains))));

        bool hasConditional = remaining.Any(item => item.Estimate.ConditionedOnDomains.Count != 0);
        string derivation = "P(Query)=P(Capsule×RelicQueue.CUR)" +
                            (shop is null ? string.Empty : "×P(Relic.Shop)") +
                            string.Concat(remaining.Select(item => "×P(" + item.Domain + ")")) +
                            "=" + F(probability);
        return JointSelectivityResult.Exact(
            probability,
            hasConditional ? JointSelectivityCombinationMethod.ConditionalChain :
                shop is null && remaining.Length == 0 ? JointSelectivityCombinationMethod.WithoutReplacementJoint : JointSelectivityCombinationMethod.IndependentProduct,
            "Joint.CapsuleRelic.LaneAwareSharedConstraintNormalized",
            shop is null && remaining.Length == 0
                ? "Capsule and overlapping C/U/R Relic Queue constraints are priced as one shared runtime-consumption block."
                : "Capsule×C/U/R is normalized first; Shop is an independent Relic remainder and other domains follow registered Conditional relations or the current AssumedIndependent policy.",
            derivation,
            graph,
            finalComponents,
            assumptions: shared.Assumptions.Concat(shop?.Assumptions ?? Array.Empty<string>()).Concat(new[]
            {
                "CapsuleRuntimeConsumptionLanes=Common|Uncommon|Rare",
                "ShopRuntimeLaneConsumedByCapsule=false",
                "NaiveFullRelicMarginalMultiplicationUsed=false",
                "CrossDomainIndependencePolicy=AssumedIndependentUnlessStructuralDependencyRegistered"
            }).ToArray(),
            dependencyCoverage: shop is null && remaining.Length == 0
                ? "CompleteSharedConstraint.CUR"
                : "CompleteSharedConstraint.CURPlusShopAndRemainder");
    }

    private static JointSelectivityComponent ToComponent(
        SearchSelectivityDomain domain,
        SearchSelectivityEstimate estimate,
        string reason) => new(
        "domain:" + domain,
        domain.ToString(),
        "QueryRoot",
        estimate.Probability,
        estimate.EvidenceCode,
        estimate.ConditionedOnDomains.Count != 0 ? JointSelectivityCombinationMethod.ConditionalChain : MapMethod(estimate.Method),
        estimate.ConditionedOnDomains.Count == 0 && estimate.DependencyClass != SearchSelectivityDependencyClass.ProvenIndependent
            ? SearchSelectivityDependencyClass.AssumedIndependent
            : estimate.DependencyClass,
        true,
        estimate.Probability.GetValueOrDefault() > 0d,
        reason,
        estimate.Assumptions);

    /// <summary>
    /// Exact finite-mixture World model for the first supported query-wide family:
    /// Act-selection variant + first effective Event + simple first-Boss predicate.
    /// Shared up_front continuation is never multiplied unless one branch term is
    /// deterministic (0/1), which makes the conjunction algebraically exact.
    /// </summary>
    private static JointSelectivityResult? EstimateWorldInternalJoint(SearchSelectivityInput plan)
    {
        NeowSearchFilter filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        EventSequenceSearchCondition[] events = filter.EventSequenceConditions.Where(item => !item.IsEmpty).ToArray();
        ActModelKeySetFilter[] bosses = filter.BossFilters.Where(item => !item.IsEmpty).ToArray();
        ActOrdinalModelKeySetFilter[] bossOrdinals = filter.BossOrdinalFilters.Where(item => !item.IsEmpty).ToArray();

        // Preserve the already-audited Act3 two-Boss without-replacement model.
        if (events.Length == 0 && bosses.Length == 0 && bossOrdinals.Length == 2)
        {
            SearchSelectivityEstimate existing = SearchSelectivityEstimator.EstimateAct3BossPair(plan);
            if (existing.IsPriced && existing.Probability.HasValue)
                return FromSingleStage(existing, SearchSelectivityDomain.WorldEvent);
        }

        if (events.Length > 1 || bosses.Length > 1 || bossOrdinals.Length != 0)
            return null;
        if (events.Length == 0 && bosses.Length == 0)
            return null;

        int act = events.Length == 1 ? events[0].Act : bosses[0].Act;
        if (events.Any(item => item.Act != act) || bosses.Any(item => item.Act != act))
            return null;
        if (act is < 1 or > 3)
            return null;

        EventSequenceSearchCondition? eventCondition = events.SingleOrDefault();
        ActModelKeySetFilter? bossCondition = bosses.SingleOrDefault();
        ModelKey eventTarget = default;
        var bossTargets = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        if (eventCondition is not null && !TryGetSinglePositiveEventTarget(eventCondition, out eventTarget))
            return null;
        if (bossCondition is not null && !TryGetSimplePositiveBossTargets(bossCondition.Keys, out bossTargets))
            return null;

        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null || !generation.ActSelectionAuthorityExact)
        {
            return JointSelectivityResult.Unpriced(
                "Joint.World.ActSelectionAuthorityMissing",
                "World finite mixture requires exact act-selection authority.",
                "Act-selection branch priors are unavailable; mixture is not normalized or guessed.",
                new[] { "ActVariantPrior" });
        }

        Beta109ActSelectionGroupSnapshot? group = generation.ActSelectionGroups.SingleOrDefault(item => item.Act == act);
        if (group is null || !group.EligibilityAndOrderExact || group.EligibleActsInSourceOrder.Count == 0)
        {
            return JointSelectivityResult.Unpriced(
                "Joint.World.ActSelectionGroupMissing",
                "Requested Act has no exact eligible-variant selection group.",
                "Finite mixture cannot be constructed.",
                new[] { "ActVariantGroup" });
        }

        IReadOnlyList<(ModelKey Key, double Prior)> priors = ResolveVariantPriors(generation, group);
        double priorSum = priors.Sum(item => item.Prior);
        if (Math.Abs(priorSum - 1d) > 1e-12)
        {
            return JointSelectivityResult.Unpriced(
                "Joint.World.MixturePriorNotNormalized",
                "Exact variant priors do not sum to one.",
                $"Σ branch prior={F(priorSum)}; automatic renormalization is forbidden.",
                new[] { "FiniteMixturePrior" });
        }

        var graphNodes = new List<SelectivityDependencyNode>
        {
            new("query", SelectivityDependencyNodeKind.QueryRoot, $"Act{act} Query", null, "QuerySemanticModel", SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>()),
            new("variant", SelectivityDependencyNodeKind.LatentParent, $"Act{act} Variant", null, "ActSelectionAuthority", SearchSelectivityDependencyClass.StructuralDependence, Array.Empty<string>())
        };
        var graphEdges = new List<SelectivityDependencyEdge>
        {
            new("variant", "query", SelectivityDependencyEdgeKind.SharedParent, "ActSelectionAuthority", "Observable conditions are evaluated under the selected Act variant.")
        };
        if (eventCondition is not null)
        {
            graphNodes.Add(new("event", SelectivityDependencyNodeKind.ObservableCondition, "Event " + eventTarget.Serialized, null, "EventPoolSequenceProjector", SearchSelectivityDependencyClass.SharedContinuation, Array.Empty<string>()));
            graphEdges.Add(new("variant", "event", SelectivityDependencyEdgeKind.ConditionalOn, "ActEventCatalog", "Event eligibility and candidate multiplicity are variant-conditional."));
        }
        if (bossCondition is not null)
        {
            graphNodes.Add(new("boss", SelectivityDependencyNodeKind.ObservableCondition, "Boss target set", null, "ActBossPoolAuthority", SearchSelectivityDependencyClass.SharedContinuation, Array.Empty<string>()));
            graphEdges.Add(new("variant", "boss", SelectivityDependencyEdgeKind.ConditionalOn, "ActBossPool", "Boss pool is variant-conditional."));
        }
        if (eventCondition is not null && bossCondition is not null)
        {
            graphEdges.Add(new("event", "boss", SelectivityDependencyEdgeKind.SharedContinuation, "up_front", "Event shuffle and Boss draw share the world up_front continuation; no product is permitted without mechanism authority."));
        }

        var branches = new List<JointSelectivityBranchTrace>();
        var allKnownComponents = new List<JointSelectivityComponent>();
        var unknownComponents = new List<string>();
        double final = 0d;
        bool allBranchesKnown = true;

        foreach ((ModelKey actKey, double prior) in priors)
        {
            Beta109ActGenerationSnapshot? variant = generation.OrderedActCatalog.FirstOrDefault(item => item.ActKey == actKey && item.Act == act);
            if (variant is null || !variant.HasExactGenerationInputs)
            {
                allBranchesKnown = false;
                unknownComponents.Add("ActVariantAuthorityIncomplete:" + actKey.Serialized);
                branches.Add(new JointSelectivityBranchTrace(
                    "variant:" + actKey.Serialized,
                    actKey.Serialized,
                    prior,
                    true,
                    null,
                    false,
                    "VariantAuthorityIncomplete",
                    Array.Empty<JointSelectivityComponent>(),
                    Array.Empty<string>()));
                continue;
            }

            var components = new List<JointSelectivityComponent>();
            double? eventP = null;
            bool eventKnown = eventCondition is null;
            bool eventEligible = eventCondition is null;
            if (eventCondition is not null)
            {
                if (TryEstimateFirstEffectiveEventProbability(generation, variant, eventCondition, eventTarget, out double p, out string authority, out string reason))
                {
                    eventP = p;
                    eventKnown = true;
                    eventEligible = p > 0d;
                    components.Add(new JointSelectivityComponent(
                        "event:" + actKey.Serialized,
                        "Event=" + eventTarget.Serialized,
                        actKey.Serialized,
                        p,
                        authority,
                        JointSelectivityCombinationMethod.ConditionalEligibility,
                        SearchSelectivityDependencyClass.SharedContinuation,
                        true,
                        eventEligible,
                        reason,
                        new[] { "ExactSlot=1 means first static-effective ordinary event after opening Ancient cursor offset." }));
                }
                else
                {
                    components.Add(new JointSelectivityComponent(
                        "event:" + actKey.Serialized,
                        "Event=" + eventTarget.Serialized,
                        actKey.Serialized,
                        null,
                        authority,
                        JointSelectivityCombinationMethod.Unknown,
                        SearchSelectivityDependencyClass.SharedContinuation,
                        false,
                        false,
                        reason,
                        Array.Empty<string>()));
                }
            }

            double? bossP = null;
            bool bossKnown = bossCondition is null;
            bool bossEligible = bossCondition is null;
            if (bossCondition is not null)
            {
                if (TryEstimateSimpleFirstBossProbability(plan, variant, bossTargets, out double p, out string reason))
                {
                    bossP = p;
                    bossKnown = true;
                    bossEligible = p > 0d;
                    components.Add(new JointSelectivityComponent(
                        "boss:" + actKey.Serialized,
                        "Boss∈{" + string.Join(',', bossTargets.Select(item => item.Serialized)) + "}",
                        actKey.Serialized,
                        p,
                        "RuntimeAuthority.ActBossPool",
                        JointSelectivityCombinationMethod.ConditionalEligibility,
                        SearchSelectivityDependencyClass.SharedContinuation,
                        true,
                        bossEligible,
                        reason,
                        new[] { "Single first-Boss draw only; Act3 A10 two-Boss act-level set is not generalized here." }));
                }
                else
                {
                    components.Add(new JointSelectivityComponent(
                        "boss:" + actKey.Serialized,
                        "Boss target set",
                        actKey.Serialized,
                        null,
                        "RuntimeAuthority.ActBossPool",
                        JointSelectivityCombinationMethod.Unknown,
                        SearchSelectivityDependencyClass.SharedContinuation,
                        false,
                        false,
                        reason,
                        Array.Empty<string>()));
                }
            }

            double? conditional = CombineBranch(eventP, eventKnown, bossP, bossKnown, out bool pruned, out string branchReason);
            if (!conditional.HasValue)
            {
                allBranchesKnown = false;
                unknownComponents.Add("SharedContinuation:" + actKey.Serialized + ":EventBossConditionalJoint");
            }
            else
            {
                final += prior * conditional.Value;
            }
            allKnownComponents.AddRange(components.Where(item => item.Probability.HasValue));
            branches.Add(new JointSelectivityBranchTrace(
                "variant:" + actKey.Serialized,
                actKey.Serialized,
                prior,
                true,
                conditional,
                pruned,
                branchReason,
                components,
                Array.Empty<string>()));
        }

        string derivation = BuildMixtureDerivation(act, branches, allBranchesKnown ? final : null);
        var graph = new SelectivityDependencyGraph(graphNodes, graphEdges);
        if (allBranchesKnown)
        {
            return JointSelectivityResult.Exact(
                final,
                JointSelectivityCombinationMethod.FiniteMixture,
                "Joint.World.ActVariantFiniteMixture",
                "Exact finite mixture over source-authoritative Act variants. Branch-local SharedContinuation is multiplied only when algebraically resolved by a deterministic 0/1 term.",
                derivation,
                graph,
                allKnownComponents,
                branches,
                new[]
                {
                    "Variant priors come from exact ActSelectionMode authority.",
                    "No representative seed is used for query-wide probability.",
                    "NaiveMarginalMultiplicationUsed=false"
                },
                "CompleteFiniteMixture");
        }

        return JointSelectivityResult.Partial(
            "Joint.World.ActVariantPartialMixture",
            "Act-variant branches and marginals are known, but at least one branch requires an unresolved Event/Boss SharedContinuation joint.",
            derivation,
            allKnownComponents,
            unknownComponents,
            graph,
            branches,
            new[]
            {
                "Known branch priors and marginals are preserved.",
                "Unknown branch conditional joints are not filled with marginal products.",
                "PlanningSurvivalUpperBound=1.0"
            },
            SearchSelectivityConfidence.High,
            "FiniteMixtureBranchesKnown_JointContinuationPartial");
    }

    private static double? CombineBranch(
        double? eventProbability,
        bool eventKnown,
        double? bossProbability,
        bool bossKnown,
        out bool pruned,
        out string reason)
    {
        pruned = false;
        if (!eventKnown || !bossKnown)
        {
            reason = "BranchComponentUnpriced";
            return null;
        }

        bool hasEvent = eventProbability.HasValue;
        bool hasBoss = bossProbability.HasValue;
        if (!hasEvent && !hasBoss)
        {
            reason = "NoBranchPredicate";
            return 1d;
        }
        if (hasEvent && !hasBoss)
        {
            pruned = eventProbability!.Value == 0d;
            reason = pruned ? "EventEligibilityImpossible" : "EventOnly";
            return eventProbability.Value;
        }
        if (!hasEvent && hasBoss)
        {
            pruned = bossProbability!.Value == 0d;
            reason = pruned ? "BossEligibilityImpossible" : "BossOnly";
            return bossProbability.Value;
        }

        double e = eventProbability!.Value;
        double b = bossProbability!.Value;
        if (e == 0d)
        {
            pruned = true;
            reason = "EventEligibilityImpossible";
            return 0d;
        }
        if (b == 0d)
        {
            pruned = true;
            reason = "BossEligibilityImpossible";
            return 0d;
        }
        if (e == 1d)
        {
            reason = "EventDeterministicTrue_BossConditionalRetained";
            return b;
        }
        if (b == 1d)
        {
            reason = "BossDeterministicTrue_EventConditionalRetained";
            return e;
        }

        reason = "SharedContinuationConditionalJointUnpriced_NoNaiveProduct";
        return null;
    }

    private static IReadOnlyList<(ModelKey Key, double Prior)> ResolveVariantPriors(
        Beta109WorldGenerationSnapshot generation,
        Beta109ActSelectionGroupSnapshot group)
    {
        // BeginRunLocally performs the normal Act 1 draw and then applies the explicit
        // overgrowth/underdocks override. Query-wide probability must model the effective
        // selected Act, not the discarded pre-override draw.
        if (group.Act == 1)
        {
            if (!generation.Act1OverrideExact)
                return Array.Empty<(ModelKey, double)>();
            if (generation.Act1OverrideResolvedKey is ModelKey overrideKey && overrideKey.IsValid)
                return new[] { (overrideKey, 1d) };
        }

        if (group.SelectionMode == Beta109ActSelectionMode.DeterministicFirst)
            return new[] { (group.EligibleActsInSourceOrder[0], 1d) };
        if (group.SelectionMode != Beta109ActSelectionMode.RandomNextItem)
            return Array.Empty<(ModelKey, double)>();

        int count = group.EligibleActsInSourceOrder.Count;
        if (count <= 0) return Array.Empty<(ModelKey, double)>();
        return group.EligibleActsInSourceOrder
            .GroupBy(key => key, ModelKeyComparer.Instance)
            .Select(items => (items.Key, items.Count() / (double)count))
            .ToArray();
    }

    private static bool TryEstimateFirstEffectiveEventProbability(
        Beta109WorldGenerationSnapshot generation,
        Beta109ActGenerationSnapshot variant,
        EventSequenceSearchCondition condition,
        ModelKey target,
        out double probability,
        out string authority,
        out string reason)
    {
        probability = 0d;
        authority = "EventPoolSequenceProjector";
        reason = string.Empty;
        if (condition.RangeMode != SearchSequenceRangeMode.ExactSlot || condition.RangeValue != 1)
        {
            reason = "OnlyExactSlot1Supported";
            return false;
        }
        if (!EventPoolSequenceProjector.TryPrepare(
                variant.Act,
                variant.ActKey,
                variant.EventRngConsumptionExact,
                variant.OrderedRawEvents,
                generation.EventAuthority.OrderedSharedEventsRaw,
                generation.EventAuthority,
                variant.OrderedEligibleEvents,
                out EventPoolSequenceProjector.PreparedAct? prepared,
                out string issue) || prepared is null)
        {
            reason = "EventAuthority:" + issue;
            return false;
        }

        int n = prepared.Candidates.Count;
        if (n <= EventPoolSequenceProjector.NormalNeowOpeningAncientEventCursorOffset)
        {
            probability = 0d;
            reason = "NoOrdinaryEventAfterOpeningCursor";
            return true;
        }

        int eligible = 0;
        int targetEligible = 0;
        foreach (EventPoolSequenceProjector.Candidate candidate in prepared.Candidates)
        {
            EventStaticEligibilityResult staticRule = EventStaticEligibilityCatalog.Evaluate(
                candidate.EventKey,
                variant.Act,
                generation.Profile,
                candidate.Source);
            if (staticRule.ShouldReject) continue;
            eligible++;
            bool sourceMatches = !condition.Source.HasValue || condition.Source.Value == candidate.Source;
            if (sourceMatches && candidate.EventKey == target) targetEligible++;
        }

        if (targetEligible == 0 || eligible == 0)
        {
            probability = 0d;
            reason = "EventEligibilityImpossible";
            return true;
        }

        // The first raw shuffled candidate is reserved for the opening Ancient cursor.
        // With >=2 statically eligible occurrences, symmetry leaves the first remaining
        // eligible occurrence uniform among those occurrences. With exactly one eligible
        // occurrence it succeeds only when that sole occurrence was not the skipped first.
        probability = eligible == 1
            ? targetEligible * ((n - 1d) / n)
            : targetEligible / (double)eligible;
        probability = Math.Clamp(probability, 0d, 1d);
        reason = $"PreparedCandidates={n};StaticEligibleOccurrences={eligible};TargetEligibleOccurrences={targetEligible};OpeningCursorOffset=1";
        return true;
    }

    private static bool TryEstimateSimpleFirstBossProbability(
        SearchSelectivityInput plan,
        Beta109ActGenerationSnapshot variant,
        HashSet<ModelKey> targets,
        out double probability,
        out string reason)
    {
        probability = 0d;
        reason = string.Empty;
        // At Ascension 10, Act3 act-level BossFilters are evaluated against the two-Boss
        // pair. That structural shape already has a separate ordinal without-replacement
        // authority and is intentionally not collapsed to first-Boss membership here.
        if (plan.Ascension >= 10 && variant.Act == 3)
        {
            reason = "Act3A10ActLevelBossSetRequiresTwoBossJoint";
            return false;
        }
        ModelKey[] pool = variant.Bosses.Where(key => key.IsValid).ToArray();
        if (pool.Length == 0)
        {
            reason = "BossPoolEmpty";
            return false;
        }
        int allowed = pool.Count(targets.Contains);
        probability = allowed / (double)pool.Length;
        reason = $"AllowedBosses={allowed};BossPool={pool.Length}";
        return true;
    }

    private static bool TryGetSinglePositiveEventTarget(EventSequenceSearchCondition condition, out ModelKey target)
    {
        target = default;
        if (condition.Keys.Ban.Count != 0 || (condition.Keys.Any.Count != 0 && condition.Keys.All.Count != 0)) return false;
        IReadOnlyList<ModelKey> source = condition.Keys.Any.Count != 0 ? condition.Keys.Any : condition.Keys.All;
        ModelKey[] distinct = source.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).ToArray();
        if (distinct.Length != 1) return false;
        target = distinct[0];
        return true;
    }

    private static bool TryGetSimplePositiveBossTargets(ModelKeySetFilter keys, out HashSet<ModelKey> targets)
    {
        targets = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        if (keys.Ban.Count != 0 || (keys.Any.Count != 0 && keys.All.Count != 0)) return false;
        IReadOnlyList<ModelKey> source = keys.Any.Count != 0 ? keys.Any : keys.All;
        if (keys.All.Count > 1) return false; // one first Boss cannot satisfy multiple All identities
        foreach (ModelKey key in source.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance)) targets.Add(key);
        return targets.Count != 0;
    }

    private static JointSelectivityResult FromSingleStage(SearchSelectivityEstimate estimate, SearchSelectivityDomain domain)
    {
        SelectivityDependencyGraph graph;
        if (estimate.DependencyClass == SearchSelectivityDependencyClass.RouteDependent)
        {
            graph = new SelectivityDependencyGraph(
                new[]
                {
                    new SelectivityDependencyNode("query", SelectivityDependencyNodeKind.QueryRoot, "Query", null, "QuerySemanticModel", estimate.DependencyClass, Array.Empty<string>()),
                    new SelectivityDependencyNode("route", SelectivityDependencyNodeKind.RouteState, "Query Route", null, "QueryRouteSemantics", SearchSelectivityDependencyClass.RouteDependent, Array.Empty<string>()),
                    new SelectivityDependencyNode("condition", SelectivityDependencyNodeKind.ObservableCondition, domain.ToString(), estimate.Probability, estimate.EvidenceCode, estimate.DependencyClass, estimate.Assumptions)
                },
                new[]
                {
                    new SelectivityDependencyEdge("route", "condition", SelectivityDependencyEdgeKind.RouteDependent, "QueryRouteSemantics", "Probability term is conditional on the actual legal/pinned route semantics."),
                    new SelectivityDependencyEdge("condition", "query", SelectivityDependencyEdgeKind.ConditionalOn, estimate.EvidenceCode, estimate.Notes)
                });
        }
        else
        {
            graph = new SelectivityDependencyGraph(
                new[]
                {
                    new SelectivityDependencyNode("query", SelectivityDependencyNodeKind.QueryRoot, "Query", null, "QuerySemanticModel", estimate.DependencyClass, Array.Empty<string>()),
                    new SelectivityDependencyNode("condition", SelectivityDependencyNodeKind.ObservableCondition, domain.ToString(), estimate.Probability, estimate.EvidenceCode, estimate.DependencyClass, estimate.Assumptions)
                },
                new[] { new SelectivityDependencyEdge("condition", "query", MapEdge(estimate.DependencyClass), estimate.EvidenceCode, estimate.Notes) });
        }
        if (estimate.IsPriced && estimate.Probability.HasValue)
        {
            return JointSelectivityResult.Exact(
                estimate.Probability.Value,
                MapMethod(estimate.Method),
                estimate.EvidenceCode,
                estimate.Notes,
                $"Single authority term: P({domain})={F(estimate.Probability)}",
                graph,
                new[]
                {
                    new JointSelectivityComponent("condition", domain.ToString(), "QueryRoot", estimate.Probability, estimate.EvidenceCode,
                        MapMethod(estimate.Method), estimate.DependencyClass, true, estimate.Probability.Value > 0d, string.Empty, estimate.Assumptions)
                },
                assumptions: estimate.Assumptions,
                dependencyCoverage: "SingleDomainComplete") with { Confidence = estimate.Confidence };
        }
        if (estimate.Probability.HasValue)
        {
            return JointSelectivityResult.Partial(
                estimate.EvidenceCode,
                estimate.Notes,
                "Stage exposes a probability term but is not accepted as a complete query-wide price.",
                new[]
                {
                    new JointSelectivityComponent("condition", domain.ToString(), "QueryRoot", estimate.Probability, estimate.EvidenceCode,
                        MapMethod(estimate.Method), estimate.DependencyClass, true, estimate.Probability.Value > 0d, string.Empty, estimate.Assumptions)
                },
                new[] { "CompletePricingAuthorityMissing" },
                graph,
                assumptions: estimate.Assumptions);
        }
        return JointSelectivityResult.Unpriced(
            estimate.EvidenceCode,
            estimate.Notes,
            "Single-domain probability is unpriced; PlanningSurvivalUpperBound=1.0.",
            new[] { domain + ":" + estimate.DependencyClass },
            graph,
            estimate.Assumptions);
    }

    private static bool CanUseCurrentProbabilityComposition(
        (SearchSelectivityDomain Domain, SearchSelectivityEstimate Estimate)[] items,
        out string reason)
    {
        reason = string.Empty;
        if (items.Length < 2)
        {
            reason = "NeedAtLeastTwoFactors";
            return false;
        }
        var active = items.Select(item => item.Domain).ToHashSet();
        foreach ((SearchSelectivityDomain domain, SearchSelectivityEstimate estimate) in items)
        {
            if (!estimate.IsPriced || !estimate.Probability.HasValue)
            {
                reason = "UnpricedFactor:" + domain + ":" + estimate.EvidenceCode;
                return false;
            }
            foreach (SearchSelectivityDomain requiredParent in estimate.ConditionedOnDomains)
            {
                if (!active.Contains(requiredParent))
                {
                    reason = "ConditionalParentMissing:" + domain + "<-" + requiredParent;
                    return false;
                }
            }
        }

        reason = items.Any(item => item.Estimate.ConditionedOnDomains.Count != 0)
            ? "RegisteredConditionalRelationsSatisfied;RemainingBlocksAssumedIndependent"
            : "NoRegisteredStructuralOrConditionalCrossDomainDependency";
        return true;
    }

    private static SelectivityDependencyGraph BuildFlatDomainGraph(
        (SearchSelectivityDomain Domain, SearchSelectivityEstimate Estimate)[] items,
        bool useCurrentPolicy = false)
    {
        var nodes = new List<SelectivityDependencyNode>
        {
            new("query", SelectivityDependencyNodeKind.QueryRoot, "Query", null, "QuerySemanticModel",
                useCurrentPolicy ? SearchSelectivityDependencyClass.AssumedIndependent : SearchSelectivityDependencyClass.UnknownDependence,
                Array.Empty<string>())
        };
        var edges = new List<SelectivityDependencyEdge>();
        foreach ((SearchSelectivityDomain domain, SearchSelectivityEstimate estimate) in items)
        {
            string id = "domain:" + domain;
            SearchSelectivityDependencyClass displayedClass = estimate.DependencyClass;
            if (useCurrentPolicy && estimate.ConditionedOnDomains.Count == 0 && displayedClass != SearchSelectivityDependencyClass.ProvenIndependent)
                displayedClass = SearchSelectivityDependencyClass.AssumedIndependent;
            nodes.Add(new SelectivityDependencyNode(
                id,
                SelectivityDependencyNodeKind.ObservableCondition,
                domain.ToString(),
                estimate.Probability,
                estimate.EvidenceCode,
                displayedClass,
                estimate.Assumptions));

            if (estimate.ConditionedOnDomains.Count != 0)
            {
                foreach (SearchSelectivityDomain parent in estimate.ConditionedOnDomains)
                {
                    string parentId = "domain:" + parent;
                    edges.Add(new SelectivityDependencyEdge(parentId, id, SelectivityDependencyEdgeKind.ConditionalOn,
                        estimate.EvidenceCode, $"{domain} is priced conditionally on {parent}."));
                }
                edges.Add(new SelectivityDependencyEdge(id, "query", SelectivityDependencyEdgeKind.ConditionalOn,
                    estimate.EvidenceCode, estimate.Notes));
            }
            else
            {
                SelectivityDependencyEdgeKind edge = useCurrentPolicy && estimate.DependencyClass != SearchSelectivityDependencyClass.ProvenIndependent
                    ? SelectivityDependencyEdgeKind.AssumedIndependent
                    : MapEdge(estimate.DependencyClass);
                edges.Add(new SelectivityDependencyEdge(id, "query", edge, estimate.EvidenceCode,
                    useCurrentPolicy && edge == SelectivityDependencyEdgeKind.AssumedIndependent
                        ? "No registered structural/conditional cross-domain dependency; current policy permits product composition. " + estimate.Notes
                        : estimate.Notes));
            }
        }
        return new SelectivityDependencyGraph(nodes, edges);
    }



    private static JointSelectivityCombinationMethod MapMethod(SearchSelectivityMethod method) => method switch
    {
        SearchSelectivityMethod.WithoutReplacement => JointSelectivityCombinationMethod.WithoutReplacementJoint,
        SearchSelectivityMethod.ConditionalChain => JointSelectivityCombinationMethod.ConditionalChain,
        SearchSelectivityMethod.AuthorityPoolMembership => JointSelectivityCombinationMethod.SingleAuthorityTerm,
        SearchSelectivityMethod.StructuralAssignment => JointSelectivityCombinationMethod.ConditionalChain,
        SearchSelectivityMethod.NamedStreamIndependenceModel => JointSelectivityCombinationMethod.IndependentProduct,
        _ => JointSelectivityCombinationMethod.SingleAuthorityTerm
    };

    private static SelectivityDependencyEdgeKind MapEdge(SearchSelectivityDependencyClass dependency) => dependency switch
    {
        SearchSelectivityDependencyClass.ProvenIndependent => SelectivityDependencyEdgeKind.Independent,
        SearchSelectivityDependencyClass.StructuralDependence => SelectivityDependencyEdgeKind.WithoutReplacement,
        SearchSelectivityDependencyClass.SharedContinuation => SelectivityDependencyEdgeKind.SharedContinuation,
        SearchSelectivityDependencyClass.RouteDependent => SelectivityDependencyEdgeKind.RouteDependent,
        SearchSelectivityDependencyClass.AssumedIndependent => SelectivityDependencyEdgeKind.AssumedIndependent,
        _ => SelectivityDependencyEdgeKind.Unknown
    };

    private static string BuildMixtureDerivation(int act, IReadOnlyList<JointSelectivityBranchTrace> branches, double? final)
    {
        string terms = string.Join(" + ", branches.Select(branch =>
            F(branch.PriorProbability) + "×" + (branch.ConditionalProbability.HasValue ? F(branch.ConditionalProbability) : "Unknown")));
        return $"Act{act} finite mixture: P(Query)={terms}=" + (final.HasValue ? F(final) : "Unknown") + "; Σ prior=" + F(branches.Sum(branch => branch.PriorProbability));
    }

    private static string F(double? value) => value.HasValue
        ? value.Value.ToString("G17", CultureInfo.InvariantCulture)
        : "Unknown";
}
