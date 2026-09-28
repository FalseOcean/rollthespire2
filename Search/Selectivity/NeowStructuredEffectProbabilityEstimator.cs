using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Exact probability authority for structured Neow outputs. The top-level route
/// identity is priced once, then child output predicates are evaluated conditionally
/// from the immutable Effect Authority. Bones-nested child sources normalize the
/// Bones grant identity as the parent fact instead of repaying the nested relic.
/// Direct Capsule routes delegate to the shared initial RelicGrabBag probability
/// authority. Scroll Boxes uses its audited two-bundle exact combinatorial model.
/// </summary>
internal static partial class NeowStructuredEffectProbabilityEstimator
{
    private readonly record struct CardState(
        int Common, int Uncommon, int Rare,
        int TargetCommon, int TargetUncommon, int TargetRare,
        int RemainingDraws,
        bool Hit);

    private readonly record struct PotionState(
        int CommonOther, int UncommonOther, int RareOther,
        int RemainingTargetMask,
        int RemainingDraws);

    internal static SearchSelectivityEstimate EstimateBonesQuery(SearchSelectivityInput plan)
    {
        if (plan.Authority.EffectAuthority is not { HasExactFoundation: true } authority)
            return SearchSelectivityEstimate.Unpriced("Probability.NeowStructured.EffectAuthorityMissing", "Opening pools unavailable.");
        var rows = ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects.Where(c => !c.IsEmpty).ToArray();
        if (!TryConjoinOutputConditions(rows, out rows))
            return SearchSelectivityEstimate.Exact(0, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.NeowStructured.ContradictoryOutputCommitments", "No joint output commitment.");
        return EstimateBonesNestedStructured(plan, authority, rows);
    }

    public static SearchSelectivityEstimate Estimate(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        NeowStructuredEffectSearchCondition[] conditions = ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects
            .Where(condition => !condition.IsEmpty)
            .ToArray();
        if (conditions.Length == 0)
            return SearchSelectivityEstimate.Unpriced("Probability.NeowStructured.NoPredicate", "No structured Neow output predicate to price.");
        if (ProbabilitySemanticProjection.From(plan).NumericalFilter.NeowRoute is not { IsValid: true } route)
            return SearchSelectivityEstimate.Unpriced("Probability.NeowStructured.RouteMissing", "Structured Neow outputs require the selected parent route.");
        bool bonesRoute = route.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones;
        if (!bonesRoute && conditions.Any(condition => condition.SourceRelicKey != route.RouteRelicKey))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowStructured.SourceOutsideSelectedRoute",
                "At least one structured output belongs to a source that is not the selected direct Neow route.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        NeowEffectAuthoritySnapshot? authority = plan.Authority.EffectAuthority;
        if (authority is null || !authority.HasExactFoundation)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowStructured.EffectAuthorityMissing",
                "Structured output probability requires the immutable runtime Effect Authority snapshot.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        if (!TryConjoinOutputConditions(conditions, out conditions))
            return SearchSelectivityEstimate.Exact(0, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.NeowStructured.ContradictoryOutputCommitments", "One source cannot satisfy all output/choice commitments together.");

        if (bonesRoute)
        {
            // Every structured predicate under Neow's Bones belongs to the same
            // normalized route block. This is important even for a Bones-owned
            // FinalCurse predicate: RequiredBonesCombination/AcquisitionOrder must
            // still be paid once when the user also constrains the two offered relics.
            // Nested effects may advance RNG cursors, but cursor advancement alone is
            // not a probability dependence under the current policy. True shared
            // semantic state is normalized separately (Capsule -> InitialRelicGrabBag).
            return EstimateBonesNestedStructured(plan, authority, conditions);
        }

        if (route.RouteRelicKey is var source &&
            (source == BaseGameModelKeys.Relics.SmallCapsule || source == BaseGameModelKeys.Relics.LargeCapsule))
        {
            // When an explicit Relic-sequence domain is also active, the Joint
            // authority must consume the integrated Capsule×Relic SharedConstraint
            // block so the same initial grab-bag facts are not paid twice.
            if (CapsuleRelicProbabilityEstimator.HasOverlappingRelicSequenceConstraint(plan))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.NeowStructured.CapsuleRelicSharedConstraintOwnedByJoint",
                    "Direct Capsule nested-relic output shares the runtime-consumed Common/Uncommon/Rare initial RelicGrabBag lanes with overlapping Relic Queue constraints; query-wide authority owns the integrated price. Shop-only constraints do not enter this block.",
                    SearchSelectivityConfidence.High,
                    SearchSelectivityMethod.WithoutReplacement,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.StructuralDependence,
                    new[] { "SharedConstraint=Capsule×RelicQueue.CUR", "ShopRuntimeLaneConsumedByCapsule=false", "UseJointAuthority=CapsuleRelicProbabilityEstimator" });
            }
            return CapsuleRelicProbabilityEstimator.Estimate(plan, includeExplicitRelicSequence: false);
        }

        if (!TryTopLevelRouteProbability(plan, route.RouteRelicKey, out double sourceProbability, out string sourceEvidence))
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowStructured.ParentIdentityAuthorityMissing",
                "The parent Neow route identity cannot be priced from the current runtime eligible Curse/Positive authority.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }
        if (sourceProbability == 0d)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent,
                "Probability.NeowStructured.ParentImpossible",
                "The selected parent Neow route is absent from the runtime eligible offer space.");
        }

        double conditional = 1d;
        var derivations = new List<string>();
        foreach (NeowStructuredEffectSearchCondition condition in conditions)
        {
            if (!TryEstimateCondition(plan, authority, condition, out double p, out string detail, out string issue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.NeowStructured.ConditionUnpriced:" + issue,
                    detail,
                    SearchSelectivityConfidence.High,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent,
                    new[] { "ParentSource=" + route.RouteRelicKey.Serialized });
            }
            conditional *= p;
            derivations.Add(condition.Scope + ":" + p.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "[" + detail + "]");
            if (conditional == 0d) break;
        }

        double probability = Math.Clamp(sourceProbability * conditional, 0d, 1d);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.RouteDependent,
            "Probability.Authority.NeowStructuredParentConditional",
            $"Parent Neow offer is paid once, then structured child predicates are evaluated conditionally. Parent={sourceProbability:G17}; children={conditional:G17}; {sourceEvidence}.",
            new[]
            {
                "P(Query)=P(parent route offered)×P(structured outputs|parent).",
                "Multiple child components under one source do not repay the parent identity probability.",
                "Separate child RNG draws use AssumedIndependent unless an explicit shared-pool/without-replacement model is applied inside the child strategy.",
                "Children=" + string.Join(";", derivations)
            });
    }

    private static SearchSelectivityEstimate EstimateBonesNestedStructured(
        SearchSelectivityInput plan,
        NeowEffectAuthoritySnapshot authority,
        IReadOnlyList<NeowStructuredEffectSearchCondition> conditions,
        bool includeExplicitRelicSequence = false)
    {
        var filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        if (!ModernNeowIdentityPredictor.TryGetEligibleCursePool(plan.Authority, out IReadOnlyList<ModelKey> cursePool) ||
            !authority.HasExactBonesPools ||
            authority.BonesEligibleRelics is null)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowStructured.BonesAuthorityMissing",
                "Bones nested structured pricing requires exact runtime Neow curse and Bones eligible-relic pools.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        ModelKey[] nestedSources = conditions
            .Select(condition => condition.SourceRelicKey)
            .Where(key => key.IsValid && key != BaseGameModelKeys.Relics.NeowsBones)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (nestedSources.Length > 2)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.NeowStructured.BonesTooManyNestedSources",
                "Neow's Bones grants exactly two distinct relic identities, but structured predicates require more than two nested sources.");
        }

        ModelKey[] requiredGrants = ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesCombination
            .Where(key => key.IsValid)
            .Concat(ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder.Where(key => key.IsValid))
            .Concat(nestedSources)
            .Concat(conditions.Where(c => c.Scope == NeowStructuredEffectScope.BonesOfferedRelics).SelectMany(c => c.OutputKeys))
            .Concat(filter.RequireSmallCapsule ? new[] { BaseGameModelKeys.Relics.SmallCapsule } : [])
            .Concat(filter.RequireLargeCapsule ? new[] { BaseGameModelKeys.Relics.LargeCapsule } : [])
            .Concat(conditions.Any(NeowReplayPlan.IsGroupedCapsule) ? new[]{BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule} : Array.Empty<ModelKey>())
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (requiredGrants.Length > 2)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.NeowStructured.BonesTooManyRequiredGrants",
                "Neow's Bones grants exactly two distinct relic identities, but the normalized query requires more than two.");
        }

        ModelKey[] bonesPool = authority.BonesEligibleRelics
            .Where(key => key.IsValid && key != BaseGameModelKeys.Relics.NeowsBones)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (bonesPool.Length < 2 || requiredGrants.Any(target => !bonesPool.Contains(target, ModelKeyComparer.Instance)))
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.NeowStructured.BonesNestedSourceImpossible",
                "At least one normalized Bones grant/source identity is absent from the runtime eligible Bones relic pool.");
        }
        if (!cursePool.Contains(BaseGameModelKeys.Relics.NeowsBones, ModelKeyComparer.Instance))
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.NeowStructured.NeowsBonesNotEligible",
                "Neow's Bones is absent from the runtime eligible curse pool.");
        }

        if (!TryTopLevelRouteProbability(plan, BaseGameModelKeys.Relics.NeowsBones, out double parentProbability, out _))
            return SearchSelectivityEstimate.Unpriced("Probability.NeowStructured.OfferJointUnavailable", "Complete opening offer authority missing.");

        var derivations = new List<string>();
        NeowStructuredEffectSearchCondition[] capsuleConditions = conditions
            .Where(NeowReplayPlan.IsCapsule)
            .ToArray();
        bool capsulePredicates = capsuleConditions.Length != 0 || !filter.CapsuleContainedRelics.IsEmpty ||
            filter.RequireWhetstone || filter.RequireWarPaint || includeExplicitRelicSequence;
        NeowStructuredEffectSearchCondition[] ordinaryConditions = conditions
            .Where(condition => !NeowReplayPlan.IsCapsule(condition) && condition.Scope is not
                (NeowStructuredEffectScope.BonesOfferedRelics or NeowStructuredEffectScope.FinalCurse))
            .ToArray();

        // Keep Bones-owned route-invariant predicates (currently Final Curse)
        // outside the Offered/Reverse route-alternative union. Otherwise an
        // unpinned route-sensitive sibling pair would incorrectly give the same
        // Final Curse predicate two independent chances even when neither sibling
        // changes the Niche continuation.
        double routeNestedChildProbability = 1d;
        double routeInvariantChildProbability = 1d;
        var finalTargets = conditions.Where(c => c.Scope == NeowStructuredEffectScope.FinalCurse).SelectMany(c => c.OutputKeys)
            .Concat(filter.RequiredFinalCurse is { } final ? new[] { final } : []).Distinct().ToArray();
        if (finalTargets.Length > 0 || filter.BannedFinalCurses.Count > 0)
        {
            if (!authority.CursePoolExact || authority.GeneratedCursePool is not { Count: > 0 } finalPool)
                return SearchSelectivityEstimate.Unpriced("Probability.NeowStructured.BonesFinalCursePoolMissing", "Generated curse pool is unavailable.");
            routeInvariantChildProbability = finalTargets.Length > 1 ? 0 : (double)finalPool.Count(k =>
                (finalTargets.Length == 0 || k == finalTargets[0]) && !filter.BannedFinalCurses.Contains(k)) / finalPool.Count;
        }
        foreach (NeowStructuredEffectSearchCondition condition in ordinaryConditions)
        {
            if (!TryEstimateCondition(plan, authority, condition, out double p, out string detail, out string issue))
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.NeowStructured.BonesNestedConditionUnpriced:" + issue,
                    detail,
                    SearchSelectivityConfidence.High,
                    SearchSelectivityMethod.ConditionalChain,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.RouteDependent,
                    new[]
                    {
                        "Parent=NeowsBones",
                        "NestedSources=" + string.Join(',', nestedSources.Select(source => source.Serialized))
                    });
            }

            if (condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones)
                routeInvariantChildProbability *= p;
            else
                routeNestedChildProbability *= p;

            derivations.Add(condition.SourceRelicKey.Entry + "/" + condition.Scope + ":" + p.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "[" + detail + "]");
            if (routeNestedChildProbability == 0d || routeInvariantChildProbability == 0d) break;
        }
        double ordinaryChildProbability = routeNestedChildProbability * routeInvariantChildProbability;

        double grantAndCapsuleProbability;
        bool PairAccepted(ModelKey[] pair) => requiredGrants.All(target => pair.Contains(target, ModelKeyComparer.Instance)) &&
            Semantics.PartyInitialQuery.Matches(filter.BonesRelics, pair);
        int legalPairCount = 0;
        for (int first = 0; first < bonesPool.Length - 1; first++)
        for (int second = first + 1; second < bonesPool.Length; second++)
            if (PairAccepted([bonesPool[first], bonesPool[second]])) legalPairCount++;
        double grantProbabilityForDiagnostics = legalPairCount * 2d / (bonesPool.Length * (bonesPool.Length - 1d));

        if (ordinaryChildProbability == 0d)
        {
            grantAndCapsuleProbability = grantProbabilityForDiagnostics;
        }
        else if (!capsulePredicates)
        {
            grantAndCapsuleProbability = grantProbabilityForDiagnostics;
        }
        else
        {
            // Capsule output probability can depend on the *other* Bones grant even
            // when that second grant is not explicitly filtered: if it is the sibling
            // Capsule, the player-selectable Offered/Reverse routes expose different
            // rarity-roll ordinals and different positions of the same RelicGrabBag.
            // Therefore enumerate the finite unordered Bones pair space instead of
            // factoring P(required grant identities) from P(Capsule outputs).
            double unorderedPairMass = 2d / (bonesPool.Length * (bonesPool.Length - 1d));
            double sum = 0d;
            int acceptedPairs = 0;
            var pairDetails = new List<string>();
            for (int first = 0; first < bonesPool.Length - 1; first++)
            for (int second = first + 1; second < bonesPool.Length; second++)
            {
                ModelKey[] pair = { bonesPool[first], bonesPool[second] };
                if (!PairAccepted(pair))
                    continue;

                acceptedPairs++;
                double capsuleP;
                string capsuleDetail, capsuleIssue;
                bool capsulePriced = plan.Authority.PlayersCount > 1
                    ? CapsuleRelicProbabilityEstimator.TryEstimateOpeningCapsules(plan,
                        pair.Where(k => k == BaseGameModelKeys.Relics.SmallCapsule || k == BaseGameModelKeys.Relics.LargeCapsule).ToArray(),
                        includeExplicitRelicSequence, out capsuleP, out capsuleDetail, out capsuleIssue)
                    : CapsuleRelicProbabilityEstimator.TryEstimateBonesNestedCapsules(plan, capsuleConditions,
                        includeExplicitRelicSequence, pair, out capsuleP, out capsuleDetail, out capsuleIssue);
                if (!capsulePriced)
                {
                    return SearchSelectivityEstimate.Unpriced(
                        "Probability.NeowStructured.BonesNestedCapsule:" + capsuleIssue,
                        capsuleDetail,
                        SearchSelectivityConfidence.High,
                        SearchSelectivityMethod.WithoutReplacement,
                        SearchSelectivityCoverage.PartialRequestedConjunction,
                        SearchSelectivityDependencyClass.StructuralDependence,
                        new[]
                        {
                            "Parent=NeowsBones",
                            "SharedConstraint=BonesNestedCapsule×InitialRelicGrabBag",
                            "BonesPair=" + string.Join("+", pair.Select(key => key.Serialized))
                        });
                }

                sum += unorderedPairMass * capsuleP;
                if (pairDetails.Count < 12)
                {
                    pairDetails.Add(
                        pair[0].Entry + "+" + pair[1].Entry +
                        "→" + capsuleP.ToString("G8", System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            grantAndCapsuleProbability = Math.Clamp(sum, 0d, 1d);
            derivations.Add(
                "CapsuleBonesPairMixture:" +
                grantAndCapsuleProbability.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) +
                $"[eligiblePairs={acceptedPairs};pairMass={unorderedPairMass:G17};sample={string.Join(',', pairDetails)}]");
        }

        bool bonesRouteUnionApplied = false;
        double routeUnionConditional = 0d;
        double fixedRouteConditional = 0d;
        string routeUnionDetail = string.Empty;
        double probability;

        if (TryPriceUnpinnedBonesRouteUnion(
                plan,
                includeExplicitRelicSequence,
                requiredGrants,
                nestedSources,
                grantProbabilityForDiagnostics,
                grantAndCapsuleProbability,
                routeNestedChildProbability,
                out fixedRouteConditional,
                out routeUnionConditional,
                out routeUnionDetail))
        {
            bonesRouteUnionApplied = true;
            probability = Math.Clamp(
                parentProbability *
                grantProbabilityForDiagnostics *
                routeInvariantChildProbability *
                routeUnionConditional,
                0d,
                1d);
        }
        else
        {
            probability = Math.Clamp(parentProbability * ordinaryChildProbability * grantAndCapsuleProbability, 0d, 1d);
        }

        return SearchSelectivityEstimate.Exact(
            probability,
            bonesRouteUnionApplied
                ? SearchSelectivityMethod.NamedStreamIndependenceModel
                : capsuleConditions.Length != 0
                    ? SearchSelectivityMethod.WithoutReplacement
                    : SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            bonesRouteUnionApplied
                ? SearchSelectivityDependencyClass.RouteDependent
                : capsuleConditions.Length != 0
                    ? SearchSelectivityDependencyClass.StructuralDependence
                    : SearchSelectivityDependencyClass.RouteDependent,
            bonesRouteUnionApplied
                ? "Probability.Authority.BonesUnpinnedRouteUnion"
                : capsuleConditions.Length != 0
                    ? "Probability.Authority.BonesNestedStructuredWithCapsuleSharedBag"
                    : "Probability.Authority.BonesNestedStructuredConditional",
            bonesRouteUnionApplied
                ? $"Neow's Bones is paid once; the exact two-grant identity pair is paid once; two player-selectable acquisition routes share an audited RNG stream and are unioned instead of being collapsed to one route. Bones={parentProbability:G17};grants={grantProbabilityForDiagnostics:G17};fixedRouteConditional={fixedRouteConditional:G17};routeUnion={routeUnionConditional:G17};routeInvariantChildren={routeInvariantChildProbability:G17};{routeUnionDetail}"
                : capsuleConditions.Length != 0
                    ? $"Neow's Bones is paid once; the unordered two-grant space is enumerated because an unspecified sibling grant may itself be the other Capsule. Bones={parentProbability:G17};grant+Capsule={grantAndCapsuleProbability:G17};ordinaryChildren={ordinaryChildProbability:G17}."
                    : $"Neow's Bones is paid once; required nested source identities are normalized into the two-grant without-replacement fact; child structured outputs are then priced. Bones={parentProbability:G17};grants={grantProbabilityForDiagnostics:G17};children={ordinaryChildProbability:G17}.",
            new[]
            {
                bonesRouteUnionApplied
                    ? "P(Query)=P(Bones)×P(exact unordered pair|Bones)×P(route-invariant children)×P(OfferedRoute satisfies ∪ ReverseRoute satisfies)."
                    : capsuleConditions.Length != 0
                        ? "P(Query)=P(Bones)×Σ_{legal unordered Bones pairs}P(pair|Bones)×P(Capsule constraints|pair)×P(other structured outputs)."
                        : "P(Query)=P(Bones)×P(required Bones grants|Bones)×P(child structured outputs|required grants).",
                "Nested source identities are SharedFacts with RequiredBonesCombination/RequiredBonesAcquisitionOrder and are never paid twice.",
                bonesRouteUnionApplied
                    ? "For an unpinned exact two-source pair with a shared audited RNG stream, the current Probability policy treats the two route-satisfaction events as AssumedIndependent alternatives and uses 1-(1-q)^2. This is a route union, not a random 1/2 factor on a pinned route."
                    : "Different non-Capsule nested effects are AssumedIndependent when they only advance RNG and do not alter each other's eligible pool or semantic state.",
                "Bones-owned FinalCurse is priced from the exact runtime GeneratedCursePool and is kept outside this Rewards-route union when route-local siblings do not share Niche.",
                "Capsule nested effects share InitialRelicGrabBag. Two-Capsule route alternatives remain owned by CapsuleRelicProbabilityEstimator and are not unioned again here.",
                "Pinned Bones acquisition order changes route semantics but does not add a random 1/2 identity factor.",
                "NestedSources=" + string.Join(',', nestedSources.Select(source => source.Serialized)),
                "Children=" + string.Join(";", derivations)
            }) with {
                PricingClass = bonesRouteUnionApplied ? SearchSelectivityPricingClass.ModeledPriced : SearchSelectivityPricingClass.ExactPriced,
                Confidence = bonesRouteUnionApplied ? SearchSelectivityConfidence.Medium : SearchSelectivityConfidence.High
            };
    }

    [Flags]
    private enum BonesProbabilityRngStream : byte
    {
        None = 0,
        Rewards = 1 << 0,
        Niche = 1 << 1,
        Transformations = 1 << 2,
        CombatPotionGeneration = 1 << 3
    }

    private static bool TryPriceUnpinnedBonesRouteUnion(
        SearchSelectivityInput plan,
        bool includeExplicitRelicSequence,
        IReadOnlyList<ModelKey> requiredGrants,
        IReadOnlyList<ModelKey> nestedSources,
        double grantProbability,
        double grantAndCapsuleProbability,
        double routeNestedChildProbability,
        out double fixedRouteConditional,
        out double routeUnionConditional,
        out string detail)
    {
        fixedRouteConditional = 0d;
        routeUnionConditional = 0d;
        detail = string.Empty;

        // Explicit Relic Queue conditions share the same initial RelicGrabBag and
        // are normalized by the separate Neow×Relic joint authority. Do not fold
        // those shared facts into this route-alternative approximation.
        if (includeExplicitRelicSequence)
            return false;

        ModelKey[] pinnedOrder = ProbabilitySemanticProjection.From(plan).NumericalFilter.RequiredBonesAcquisitionOrder
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (pinnedOrder.Length != 0)
            return false;

        // Route union is safe to model only when the two granted identities are
        // fixed by the canonical Query. One or both relics may own a concrete
        // structured predicate; an unobserved companion can still shift Rewards
        // before the observed effect and therefore make Offered/Reverse differ.
        if (requiredGrants.Count != 2 || nestedSources.Count == 0 ||
            requiredGrants.Distinct(ModelKeyComparer.Instance).Count() != 2 ||
            nestedSources.Distinct(ModelKeyComparer.Instance).Count() != nestedSources.Count ||
            !nestedSources.All(source => requiredGrants.Contains(source, ModelKeyComparer.Instance)) ||
            grantProbability <= 0d)
        {
            return false;
        }

        ModelKey first = requiredGrants[0];
        ModelKey second = requiredGrants[1];
        BonesProbabilityRngStream sharedStreams =
            GetBonesProbabilityRngStreams(first) & GetBonesProbabilityRngStreams(second);
        if ((sharedStreams & BonesProbabilityRngStream.Rewards) == 0)
            return false;

        int requiredCapsuleCount = requiredGrants.Count(IsCapsuleSource);
        if (requiredCapsuleCount == 2)
        {
            // Two Capsules already enumerate Offered/Reverse route scenarios inside
            // CapsuleRelicProbabilityEstimator. Applying another union here would
            // double-pay the same player route alternatives.
            return false;
        }

        int constrainedCapsuleCount = nestedSources.Count(IsCapsuleSource);
        double capsuleConditional = constrainedCapsuleCount == 0
            ? 1d
            : Math.Clamp(grantAndCapsuleProbability / grantProbability, 0d, 1d);
        fixedRouteConditional = Math.Clamp(routeNestedChildProbability * capsuleConditional, 0d, 1d);

        // The two acquisition routes are player-selectable alternatives over the
        // same offered pair. They are not a random coin flip. Under the project's
        // existing AssumedIndependent probability policy for otherwise-unmodeled
        // RNG relations, inclusion/exclusion is 2q-q² rather than silently using q
        // for both AnyOrder and ExactOrder. For rare strict predicates this tends to
        // the 2:1 behavior observed in Beta111 runtime validation.
        routeUnionConditional = Math.Clamp(
            1d - (1d - fixedRouteConditional) * (1d - fixedRouteConditional),
            0d,
            1d);
        detail =
            $"SharedStreams={sharedStreams};routeUnionModel=1-(1-q)^2;capsuleConditional={capsuleConditional:G17}";
        return true;
    }

    private static bool IsCapsuleSource(ModelKey source) =>
        source == BaseGameModelKeys.Relics.SmallCapsule ||
        source == BaseGameModelKeys.Relics.LargeCapsule;

    private static BonesProbabilityRngStream GetBonesProbabilityRngStreams(ModelKey source)
    {
        if (source == BaseGameModelKeys.Relics.SmallCapsule ||
            source == BaseGameModelKeys.Relics.LargeCapsule ||
            source == BaseGameModelKeys.Relics.ScrollBoxes ||
            source == BaseGameModelKeys.Relics.ArcaneScroll ||
            source == BaseGameModelKeys.Relics.HeftyTablet ||
            source == BaseGameModelKeys.Relics.LeadPaperweight ||
            source == BaseGameModelKeys.Relics.LostCoffer ||
            source == BaseGameModelKeys.Relics.MassiveScroll)
        {
            return BonesProbabilityRngStream.Rewards;
        }

        if (source == BaseGameModelKeys.Relics.Kaleidoscope)
            return BonesProbabilityRngStream.Rewards | BonesProbabilityRngStream.Niche;
        if (source == BaseGameModelKeys.Relics.NewLeaf)
            return BonesProbabilityRngStream.Niche;
        if (source == BaseGameModelKeys.Relics.LeafyPoultice)
            return BonesProbabilityRngStream.Transformations;
        if (source == BaseGameModelKeys.Relics.PhialHolster)
            return BonesProbabilityRngStream.CombatPotionGeneration;

        return BonesProbabilityRngStream.None;
    }

    internal static SearchSelectivityEstimate EstimateWithRelicSharedConstraints(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ProbabilitySemanticProjection.From(plan).NumericalFilter.NeowRoute is not { IsValid: true } route)
            return SearchSelectivityEstimate.Unpriced("Probability.NeowRelicShared.RouteMissing", "A selected Neow route is required.");

        if (route.RouteRelicKey == BaseGameModelKeys.Relics.SmallCapsule ||
            route.RouteRelicKey == BaseGameModelKeys.Relics.LargeCapsule)
            return CapsuleRelicProbabilityEstimator.Estimate(plan, includeExplicitRelicSequence: true);

        if (route.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones)
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.NotCapsuleShape",
                "Only direct Capsule or Bones-nested Capsule queries own a Neow×Relic shared-bag probability block.");

        NeowStructuredEffectSearchCondition[] conditions = ProbabilitySemanticProjection.From(plan).NumericalFilter.StructuredNeowEffects
            .Where(condition => !condition.IsEmpty)
            .ToArray();
        if (plan.Authority.PlayersCount <= 1 && !conditions.Any(NeowReplayPlan.IsCapsule))
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.NoNestedCapsule",
                "Bones query has no nested Capsule structured predicate requiring RelicGrabBag normalization.");

        NeowEffectAuthoritySnapshot? authority = plan.Authority.EffectAuthority;
        if (authority is null || !authority.HasExactFoundation)
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.EffectAuthorityMissing",
                "Bones nested Capsule shared pricing requires immutable Effect Authority.");

        return EstimateBonesNestedStructured(plan, authority, conditions, includeExplicitRelicSequence: true);
    }

    /// <summary>
    /// Exact physical-survival authority for a downstream Relic gate after an
    /// ExactEquivalent Neow Capsule/Bones-Capsule gate has already passed.
    /// The numerator is the existing normalized joint P(N ∩ R); the denominator
    /// is the same Neow query without paying explicit Relic Queue facts again.
    /// No post-Capsule queue remapping or fixed consumed-prefix offset is introduced.
    /// </summary>
    internal static SearchSelectivityEstimate EstimateRelicConditionalAfterNeow(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        SearchSelectivityEstimate joint = EstimateWithRelicSharedConstraints(plan);
        if (joint.PricingClass != SearchSelectivityPricingClass.ExactPriced || !joint.Probability.HasValue)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.ConditionalJointUnavailable",
                "The normalized Capsule×InitialRelicGrabBag joint probability is not exactly priced.",
                joint.Confidence,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                joint.Assumptions);
        }

        if (ProbabilitySemanticProjection.From(plan).NumericalFilter.NeowRoute is not { IsValid: true } route)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.ConditionalRouteMissing",
                "A selected Capsule/Bones route is required for upstream-conditioned Relic survival.");
        }

        SearchSelectivityEstimate neow = route.RouteRelicKey switch
        {
            var key when key == BaseGameModelKeys.Relics.SmallCapsule || key == BaseGameModelKeys.Relics.LargeCapsule =>
                CapsuleRelicProbabilityEstimator.Estimate(plan, includeExplicitRelicSequence: false),
            var key when key == BaseGameModelKeys.Relics.NeowsBones => Estimate(plan),
            _ => SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.ConditionalNotCapsuleShape",
                "Only direct Capsule or Bones-nested Capsule shared-bag queries have this conditional authority.")
        };
        if (neow.PricingClass != SearchSelectivityPricingClass.ExactPriced || !neow.Probability.HasValue)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.ConditionalNeowUnavailable",
                "The exact upstream Neow Capsule/Bones-Capsule probability is unavailable.",
                neow.Confidence,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                neow.Assumptions);
        }

        double jointProbability = joint.Probability.Value;
        double neowProbability = neow.Probability.Value;
        if (neowProbability <= 0d)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.Authority.CapsuleRelicConditionalAfterImpossibleNeow",
                "The upstream Neow shared-bag predicate is impossible, so no candidate can reach the downstream Relic gate.",
                new[] { "ConditionalPopulationEmpty=true", "InitialRelicGrabBagCoordinate=true" },
                conditionedOnDomains: new[] { SearchSelectivityDomain.Neow });
        }
        if (jointProbability > neowProbability + 1e-12)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.NeowRelicShared.ConditionalInvariantViolation",
                $"Normalized joint probability {jointProbability:G17} exceeds its upstream Neow parent {neowProbability:G17}; refusing to fabricate conditional survival.",
                SearchSelectivityConfidence.High,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        double conditional = Math.Clamp(jointProbability / neowProbability, 0d, 1d);
        return SearchSelectivityEstimate.Exact(
            conditional,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Authority.CapsuleRelicConditionalAfterNeow",
            $"Downstream Relic survival is the exact shared-bag conditional P(N∩R)/P(N). joint={jointProbability:G17};neow={neowProbability:G17};conditional={conditional:G17}.",
            new[]
            {
                "CanonicalCoordinate=InitialRelicGrabBag",
                "CapsuleFactsAlreadyPaidByUpstreamNeow=true",
                "ExplicitRelicQueueIsNotPostCapsuleRemapped=true",
                "NoRouteStateCarryOrFixedConsumedOffset=true"
            },
            conditionedOnDomains: new[] { SearchSelectivityDomain.Neow });
    }

    private static bool TryEstimateCondition(
        SearchSelectivityInput plan,
        NeowEffectAuthoritySnapshot authority,
        NeowStructuredEffectSearchCondition condition,
        out double probability,
        out string detail,
        out string issue)
    {
        probability = 0d;
        detail = string.Empty;
        issue = string.Empty;
        ModelKey[] targets = condition.OutputKeys.Where(key => key.IsValid).ToArray();

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle &&
            condition.Scope == NeowStructuredEffectScope.FinalCurse &&
            condition.OutputKind == NeowStructuredOutputKind.Curse &&
            targets.Length == 1)
        {
            if (!authority.HasExactFoundation || !authority.CursePoolExact ||
                authority.GeneratedCursePool is not { Count: > 0 } cursePool)
                return FailCondition("BonesFinalCursePoolMissing", out detail, out issue);

            int hit = cursePool.Count(key => key == targets[0]);
            probability = hit / (double)cursePool.Count;
            detail = $"Neow's Bones final curse is one uniform Niche draw from the runtime GeneratedCursePool;pool={cursePool.Count};targetMass={hit}.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes)
        {
            return TryEstimateScrollBoxesCondition(plan, authority, condition, targets, out probability, out detail, out issue);
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.MassiveScroll &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && targets.Length == 1)
        {
            if (plan.Authority.PlayersCount <= 1)
            {
                detail = "Massive Scroll requires a multiplayer opening.";
                return true;
            }
            if (!authority.HasExactCharacterRewardPool || !authority.HasExactColorlessRewardPool ||
                authority.CharacterRewardPool is null || authority.ColorlessRewardPool is null)
                return FailCondition("MassiveScrollMultiplayerPoolMissing", out detail, out issue);
            var pool = authority.CharacterRewardPool.Concat(authority.ColorlessRewardPool)
                .Where(card => card.IsMultiplayerOnly).DistinctBy(card => card.CardKey).ToArray();
            probability = CardOfferContains(pool, plan, targets, 3, forcedRarity: null);
            detail = $"Massive Scroll target in three no-repeat regular-rarity offers;capturedMultiplayerPool={pool.Length}.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.ArcaneScroll &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && targets.Length == 1)
        {
            if (authority.HasExactCharacterRewardPool != true || authority.CharacterRewardPool is null)
                return FailCondition("ArcaneScrollCharacterPoolMissing", out detail, out issue);
            probability = CardOfferContains(authority.CharacterRewardPool, plan, new[] { targets[0] }, 1, EffectCardRarity.Rare);
            detail = "ArcaneScroll forced-Rare single card over runtime CharacterRewardPool.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.HeftyTablet &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && targets.Length == 1)
        {
            if (authority.HasExactCharacterRewardPool != true || authority.CharacterRewardPool is null)
                return FailCondition("HeftyTabletCharacterPoolMissing", out detail, out issue);
            probability = CardOfferContains(authority.CharacterRewardPool, plan, new[] { targets[0] }, 3, EffectCardRarity.Rare);
            detail = "HeftyTablet target appears in three forced-Rare distinct card draws.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LeadPaperweight &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && targets.Length == 1)
        {
            if (authority.HasExactColorlessRewardPool != true || authority.ColorlessRewardPool is null)
                return FailCondition("LeadPaperweightColorlessPoolMissing", out detail, out issue);
            probability = CardOfferContains(authority.ColorlessRewardPool, plan, new[] { targets[0] }, 2, forcedRarity: null);
            detail = "Lead Paperweight target appears in two distinct regular-rarity Colorless draws.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LostCoffer &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && targets.Length == 1)
        {
            if (condition.OutputKind == NeowStructuredOutputKind.Card)
            {
                if (authority.HasExactCharacterRewardPool != true || authority.CharacterRewardPool is null)
                    return FailCondition("LostCofferCharacterPoolMissing", out detail, out issue);
                probability = CardOfferContains(authority.CharacterRewardPool, plan, new[] { targets[0] }, 3, forcedRarity: null);
                detail = "Lost Coffer card target appears in its three-card distinct regular-rarity offer.";
                return true;
            }
            if (condition.OutputKind == NeowStructuredOutputKind.Potion)
            {
                if (authority.HasExactPotions != true || authority.PotionPool is null)
                    return FailCondition("LostCofferPotionPoolMissing", out detail, out issue);
                if (HasDuplicatePotionIdentity(authority.PotionPool))
                    return FailCondition("LostCofferDuplicatePotionIdentityRequiresInstanceModel", out detail, out issue);
                probability = PotionContainment(authority.PotionPool, new[] { targets[0] }, 1);
                detail = "Lost Coffer single potion draw from runtime rarity pool.";
                return true;
            }
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.PhialHolster &&
            condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
            targets.Length is 1 or 2)
        {
            if (authority.HasExactPotions != true || authority.PotionPool is null)
                return FailCondition("PhialHolsterPotionPoolMissing", out detail, out issue);
            if (HasDuplicatePotionIdentity(authority.PotionPool))
                return FailCondition("PhialHolsterDuplicatePotionIdentityRequiresInstanceModel", out detail, out issue);
            probability = PotionContainment(authority.PotionPool, targets, 2);
            detail = "Phial Holster unordered containment over two no-repeat potion draws.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.NewLeaf &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && targets.Length == 1)
        {
            if (authority.HasExactDeck != true || authority.HasExactTransformPool != true ||
                authority.OrderedDeck is null || authority.TransformPool is null)
                return FailCondition("NewLeafTransformAuthorityMissing", out detail, out issue);
            NeowEffectCardSnapshot? source = NewLeafNormalizedSourcePolicy.Select(authority.OrderedDeck, authority.CharacterStrikeKey);
            if (source is null)
            {
                probability = 0d;
                detail = "New Leaf normalized first-Basic-Strike source is absent.";
                return true;
            }
            NeowEffectCardSnapshot[] candidates = NewLeafNormalizedSourcePolicy.BuildTransformCandidates(authority.TransformPool, source);
            if (candidates.Length == 0) return FailCondition("NewLeafTransformPoolEmpty", out detail, out issue);
            int hit = candidates.Count(card => card.CardKey == targets[0]);
            probability = hit / (double)candidates.Length;
            detail = $"New Leaf normalized transform candidates={candidates.Length};targetMass={hit}.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.LeafyPoultice &&
            condition.Kind == NeowStructuredConditionKind.ExactUnorderedPair &&
            targets.Length is 1 or 2)
        {
            if (authority.HasExactDeck != true || authority.HasExactTransformPool != true ||
                authority.OrderedDeck is null || authority.TransformPool is null)
                return FailCondition("LeafyPoulticeTransformAuthorityMissing", out detail, out issue);
            NeowEffectCardSnapshot? strike = authority.OrderedDeck.FirstOrDefault(card => card.IsBasic && card.IsStrike);
            NeowEffectCardSnapshot? defend = authority.OrderedDeck.FirstOrDefault(card => card.IsBasic && card.IsDefend);
            if (strike is null || defend is null)
                return FailCondition("LeafyPoulticeBasicTargetsMissing", out detail, out issue);
            NeowEffectCardSnapshot[] strikePool = TransformCandidates(authority.TransformPool, strike);
            NeowEffectCardSnapshot[] defendPool = TransformCandidates(authority.TransformPool, defend);
            if (strikePool.Length == 0 || defendPool.Length == 0)
                return FailCondition("LeafyPoulticeTransformPoolEmpty", out detail, out issue);
            probability = TransformPairContainment(strikePool, defendPool, targets);
            detail = $"Leafy Poultice two ordered source transforms with unordered target containment;strikePool={strikePool.Length};defendPool={defendPool.Length}.";
            return true;
        }

        if (condition.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope &&
            condition.Kind == NeowStructuredConditionKind.IndependentOfferGroupTargets &&
            targets.Length is 1 or 2)
        {
            if (authority.HasExactOtherCharacterPools != true || authority.OtherCharacterPools is null || authority.OtherCharacterPools.Count < 3)
                return FailCondition("KaleidoscopeOtherCharacterPoolsMissing", out detail, out issue);

            if (condition.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder)
            {
                ModelKey?[] slots = condition.KaleidoscopePositionalSlots.Count == 2
                    ? condition.KaleidoscopePositionalSlots.ToArray()
                    : condition.OutputKeys.Take(2).Select(key => (ModelKey?)key)
                        .Concat(Enumerable.Repeat<ModelKey?>(null, Math.Max(0, 2 - condition.OutputKeys.Count)))
                        .Take(2).ToArray();
                ModelKey[] concrete = slots.Where(key => key.HasValue).Select(key => key!.Value).ToArray();
                if (concrete.Length == 0)
                    return FailCondition("KaleidoscopeExactOrderHasNoConcreteTarget", out detail, out issue);
                if (!TryKaleidoscopeGroupStats(plan, authority.OtherCharacterPools, concrete, out double orderedA, out double orderedB, out _, out string orderedIssue))
                    return FailCondition(orderedIssue, out detail, out issue);
                probability = concrete.Length == 1
                    ? orderedA
                    : orderedA * orderedB;
                detail = concrete.Length == 1
                    ? $"Kaleidoscope ordered sparse position target;group-specific hit={orderedA:G17};slots={string.Join(">", slots.Select(key => key?.Serialized ?? "*"))}."
                    : $"Kaleidoscope ordered positions are independent regenerated groups;group1={orderedA:G17};group2={orderedB:G17}.";
                return true;
            }

            if (!TryKaleidoscopeGroupStats(plan, authority.OtherCharacterPools, targets, out double pFirst, out double pSecond, out double pBoth, out string statsIssue))
                return FailCondition(statsIssue, out detail, out issue);
            probability = targets.Length == 1
                ? 1d - (1d - pFirst) * (1d - pFirst)
                : targets[0] == targets[1]
                    ? pFirst * pFirst
                    : Math.Clamp(2d * pFirst * pSecond - pBoth * pBoth, 0d, 1d);
            detail = targets.Length == 1
                ? $"Kaleidoscope target in either of two regenerated groups;perGroup={pFirst:G17}."
                : $"Kaleidoscope two targets must occupy different offer groups;perGroupA={pFirst:G17};B={pSecond:G17};both={pBoth:G17}.";
            return true;
        }

        return FailCondition("UnsupportedStructuredCondition:" + condition.SourceRelicKey.Entry + ":" + condition.Kind + ":" + condition.Scope, out detail, out issue);
    }

    private static bool TryEstimateScrollBoxesCondition(
        SearchSelectivityInput plan,
        NeowEffectAuthoritySnapshot authority,
        NeowStructuredEffectSearchCondition condition,
        IReadOnlyList<ModelKey> targets,
        out double probability,
        out string detail,
        out string issue)
    {
        probability = 0d;
        detail = string.Empty;
        issue = string.Empty;

        if (authority.HasExactCharacterRewardPool != true || authority.CharacterRewardPool is null)
            return FailCondition("ScrollBoxesCharacterPoolMissing", out detail, out issue);

        bool defectRule = plan.CharacterKey == BaseGameModelKeys.Characters.Defect;
        if (condition.Kind == NeowStructuredConditionKind.SpecialOffer &&
            condition.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw)
        {
            probability = defectRule ? 1d - 0.99d * 0.99d : 0d;
            detail = defectRule
                ? "Scroll Boxes Triple Claw: two independent per-bundle 1% special rolls; at least one special bundle."
                : "Scroll Boxes Triple Claw is unavailable outside Defect under the current audited runtime rule.";
            return true;
        }

        if (condition.Kind != NeowStructuredConditionKind.StructuredCardComposition ||
            condition.OutputKind != NeowStructuredOutputKind.Card ||
            targets.Count is < 1 or > 3)
        {
            return FailCondition("ScrollBoxesStructuredShapeUnsupported", out detail, out issue);
        }

        NeowEffectCardSnapshot[] common = authority.CharacterRewardPool
            .Where(card => card.Rarity == EffectCardRarity.Common)
            .ToArray();
        NeowEffectCardSnapshot[] uncommon = authority.CharacterRewardPool
            .Where(card => card.Rarity == EffectCardRarity.Uncommon)
            .ToArray();
        if (common.Select(card => card.CardKey).Distinct(ModelKeyComparer.Instance).Count() != common.Length ||
            uncommon.Select(card => card.CardKey).Distinct(ModelKeyComparer.Instance).Count() != uncommon.Length)
        {
            return FailCondition("ScrollBoxesDuplicateRuntimeCardIdentity", out detail, out issue);
        }

        if (common.Length < 4 || uncommon.Length < 2)
            return FailCondition("ScrollBoxesRuntimePoolTooSmallForAuditedTwoBundleShape", out detail, out issue);

        var rarityByKey = authority.CharacterRewardPool
            .GroupBy(card => card.CardKey, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.First().Rarity, ModelKeyComparer.Instance);

        int targetCommon = 0;
        int targetUncommon = 0;
        foreach (ModelKey target in targets)
        {
            if (!rarityByKey.TryGetValue(target, out EffectCardRarity rarity))
            {
                probability = 0d;
                detail = "Requested Scroll Boxes card is absent from the runtime CharacterRewardPool.";
                return true;
            }
            if (rarity == EffectCardRarity.Common) targetCommon++;
            else if (rarity == EffectCardRarity.Uncommon) targetUncommon++;
            else
            {
                probability = 0d;
                detail = "Scroll Boxes normal bundles contain exactly two Common and one Uncommon card; requested target rarity is impossible.";
                return true;
            }
        }
        if (targetCommon > 2 || targetUncommon > 1)
        {
            probability = 0d;
            detail = "Requested Scroll Boxes target subset cannot fit one normal 2-Common+1-Uncommon bundle.";
            return true;
        }

        double commonMass = targetCommon switch
        {
            0 => 1d,
            1 => 2d / common.Length,
            2 => 2d / (common.Length * (common.Length - 1d)),
            _ => 0d
        };
        double uncommonMass = targetUncommon switch
        {
            0 => 1d,
            1 => 1d / uncommon.Length,
            _ => 0d
        };
        double oneNormalBundle = commonMass * uncommonMass;

        double normalOpportunityMass = defectRule ? 2d * 0.99d : 2d;
        probability = Math.Clamp(normalOpportunityMass * oneNormalBundle, 0d, 1d);
        detail =
            $"Scroll Boxes exact two-bundle containment;CommonPool={common.Length};UncommonPool={uncommon.Length};targetC={targetCommon};targetU={targetUncommon};DefectSpecialRule={defectRule};crossBundleNoRepeat=true.";
        return true;
    }

    private static bool FailCondition(string code, out string detail, out string issue)
    {
        issue = code;
        detail = "Structured Neow condition requires a mechanism-specific probability authority that is not completed in this batch: " + code;
        return false;
    }

    internal static bool TryTopLevelRouteProbability(
        SearchSelectivityInput plan,
        ModelKey source,
        out double probability,
        out string evidence)
    {
        var offers = ProbabilitySemanticProjection.From(plan).NumericalFilter.NeowRelics;
        return ModernNeowIdentityPredictor.TryEstimateOfferProbability(plan.Authority, (a,b,c) =>
        {
            bool Has(ModelKey key) => key == a || key == b || key == c;
            return (!source.IsValid || Has(source)) && (offers.Any.Count == 0 || offers.Any.Any(Has)) &&
                offers.All.All(Has) && !offers.Ban.Any(Has);
        }, out probability, out evidence);
    }

    private static double CardOfferContains(
        IReadOnlyList<NeowEffectCardSnapshot> source,
        SearchSelectivityInput plan,
        IReadOnlyCollection<ModelKey> targetKeys,
        int draws,
        EffectCardRarity? forcedRarity)
    {
        NeowEffectCardSnapshot[] pool = source
            .Where(card => card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
            .ToArray();
        var targets = new HashSet<ModelKey>(targetKeys, ModelKeyComparer.Instance);
        int c = pool.Count(card => card.Rarity == EffectCardRarity.Common);
        int u = pool.Count(card => card.Rarity == EffectCardRarity.Uncommon);
        int r = pool.Count(card => card.Rarity == EffectCardRarity.Rare);
        int tc = pool.Count(card => card.Rarity == EffectCardRarity.Common && targets.Contains(card.CardKey));
        int tu = pool.Count(card => card.Rarity == EffectCardRarity.Uncommon && targets.Contains(card.CardKey));
        int tr = pool.Count(card => card.Rarity == EffectCardRarity.Rare && targets.Contains(card.CardKey));
        CardBaseOddsPolicy policy = RuntimeProfilePolicies.BaseOddsPolicy(plan.ProfileId);
        var memo = new Dictionary<CardState, double>();

        double Solve(CardState state)
        {
            if (state.RemainingDraws == 0) return state.Hit ? 1d : 0d;
            if (memo.TryGetValue(state, out double cached)) return cached;
            double result = 0d;
            if (forcedRarity.HasValue)
            {
                result = DrawForRolledRarity(state, forcedRarity.Value, 1d);
            }
            else
            {
                foreach ((EffectCardRarity rarity, double mass) in CardRarityMasses(plan.Ascension, policy))
                    result += DrawForRolledRarity(state, rarity, mass);
            }
            result = Math.Clamp(result, 0d, 1d);
            memo[state] = result;
            return result;
        }

        double DrawForRolledRarity(CardState state, EffectCardRarity rolled, double rollMass)
        {
            EffectCardRarity selected = SelectFallbackRarity(rolled, state.Common, state.Uncommon, state.Rare);
            int total = Count(state, selected);
            if (total <= 0) return 0d;
            int target = TargetCount(state, selected);
            int other = total - target;
            double value = 0d;
            if (target > 0)
                value += target / (double)total * Solve(Decrement(state, selected, target: true) with { Hit = true });
            if (other > 0)
                value += other / (double)total * Solve(Decrement(state, selected, target: false));
            return rollMass * value;
        }

        return Solve(new CardState(c, u, r, tc, tu, tr, draws, false));
    }

    private static IEnumerable<(EffectCardRarity Rarity, double Mass)> CardRarityMasses(
        int ascension,
        CardBaseOddsPolicy policy)
    {
        CardBaseOddsThresholds odds = CardBaseOddsPolicyEvaluator.GetBaseOdds(CardBaseOddsType.Regular, ascension);
        double rare = odds.Rare;
        double uncommonUpper = policy == CardBaseOddsPolicy.Beta110Cumulative
            ? odds.Rare + odds.Uncommon
            : odds.Uncommon;
        double uncommon = Math.Max(0d, uncommonUpper - rare);
        double common = Math.Max(0d, 1d - uncommonUpper);
        yield return (EffectCardRarity.Rare, rare);
        yield return (EffectCardRarity.Uncommon, uncommon);
        yield return (EffectCardRarity.Common, common);
    }

    private static EffectCardRarity SelectFallbackRarity(
        EffectCardRarity rolled,
        int common,
        int uncommon,
        int rare)
    {
        EffectCardRarity[] order = rolled switch
        {
            EffectCardRarity.Common => new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare },
            EffectCardRarity.Uncommon => new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare, EffectCardRarity.Common },
            _ => new[] { EffectCardRarity.Rare, EffectCardRarity.Common, EffectCardRarity.Uncommon }
        };
        foreach (EffectCardRarity rarity in order)
        {
            if (rarity switch
                {
                    EffectCardRarity.Common => common > 0,
                    EffectCardRarity.Uncommon => uncommon > 0,
                    EffectCardRarity.Rare => rare > 0,
                    _ => false
                })
                return rarity;
        }
        return EffectCardRarity.Special;
    }

    private static int Count(CardState state, EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => state.Common,
        EffectCardRarity.Uncommon => state.Uncommon,
        EffectCardRarity.Rare => state.Rare,
        _ => 0
    };

    private static int TargetCount(CardState state, EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => state.TargetCommon,
        EffectCardRarity.Uncommon => state.TargetUncommon,
        EffectCardRarity.Rare => state.TargetRare,
        _ => 0
    };

    private static CardState Decrement(CardState state, EffectCardRarity rarity, bool target) => rarity switch
    {
        EffectCardRarity.Common => state with
        {
            Common = state.Common - 1,
            TargetCommon = state.TargetCommon - (target ? 1 : 0),
            RemainingDraws = state.RemainingDraws - 1
        },
        EffectCardRarity.Uncommon => state with
        {
            Uncommon = state.Uncommon - 1,
            TargetUncommon = state.TargetUncommon - (target ? 1 : 0),
            RemainingDraws = state.RemainingDraws - 1
        },
        EffectCardRarity.Rare => state with
        {
            Rare = state.Rare - 1,
            TargetRare = state.TargetRare - (target ? 1 : 0),
            RemainingDraws = state.RemainingDraws - 1
        },
        _ => state with { RemainingDraws = 0 }
    };

    private static double PotionContainment(
        IReadOnlyList<NeowEffectPotionSnapshot> source,
        IReadOnlyList<ModelKey> required,
        int draws)
    {
        if (required.Count == 0) return 1d;
        if (required.Count > draws) return 0d;
        if (required.Distinct(ModelKeyComparer.Instance).Count() != required.Count) return 0d;
        var targetIndex = required.Select((key, index) => (key, index))
            .ToDictionary(item => item.key, item => item.index, ModelKeyComparer.Instance);
        var targetRarity = Enumerable.Repeat(EffectPotionRarity.Other, required.Count).ToArray();
        int cOther = 0, uOther = 0, rOther = 0;
        foreach (NeowEffectPotionSnapshot potion in source)
        {
            if (targetIndex.TryGetValue(potion.PotionKey, out int index))
                targetRarity[index] = potion.Rarity;
            else
            {
                if (potion.Rarity == EffectPotionRarity.Common) cOther++;
                else if (potion.Rarity == EffectPotionRarity.Uncommon) uOther++;
                else if (potion.Rarity == EffectPotionRarity.Rare) rOther++;
            }
        }
        if (required.Select((_, index) => targetRarity[index]).Any(rarity => rarity is not (EffectPotionRarity.Common or EffectPotionRarity.Uncommon or EffectPotionRarity.Rare)))
            return 0d;

        int fullMask = (1 << required.Count) - 1;
        var memo = new Dictionary<PotionState, double>();
        double Solve(PotionState state)
        {
            if (state.RemainingDraws == 0) return state.RemainingTargetMask == 0 ? 1d : 0d;
            if (memo.TryGetValue(state, out double cached)) return cached;
            double result = 0d;
            foreach ((EffectPotionRarity rarity, double mass) in PotionRarityMasses())
            {
                int other = rarity switch
                {
                    EffectPotionRarity.Common => state.CommonOther,
                    EffectPotionRarity.Uncommon => state.UncommonOther,
                    EffectPotionRarity.Rare => state.RareOther,
                    _ => 0
                };
                int targetCount = 0;
                for (int index = 0; index < required.Count; index++)
                    if ((state.RemainingTargetMask & (1 << index)) != 0 && targetRarity[index] == rarity) targetCount++;
                int total = other + targetCount;
                if (total == 0) continue; // CreatePotion returns null; this root does not produce a comparable complete output.
                if (other > 0)
                {
                    PotionState next = rarity switch
                    {
                        EffectPotionRarity.Common => state with { CommonOther = state.CommonOther - 1, RemainingDraws = state.RemainingDraws - 1 },
                        EffectPotionRarity.Uncommon => state with { UncommonOther = state.UncommonOther - 1, RemainingDraws = state.RemainingDraws - 1 },
                        _ => state with { RareOther = state.RareOther - 1, RemainingDraws = state.RemainingDraws - 1 }
                    };
                    result += mass * other / total * Solve(next);
                }
                for (int index = 0; index < required.Count; index++)
                {
                    int bit = 1 << index;
                    if ((state.RemainingTargetMask & bit) == 0 || targetRarity[index] != rarity) continue;
                    result += mass * (1d / total) * Solve(state with
                    {
                        RemainingTargetMask = state.RemainingTargetMask & ~bit,
                        RemainingDraws = state.RemainingDraws - 1
                    });
                }
            }
            result = Math.Clamp(result, 0d, 1d);
            memo[state] = result;
            return result;
        }
        return Solve(new PotionState(cOther, uOther, rOther, fullMask, draws));
    }

    private static bool HasDuplicatePotionIdentity(IReadOnlyList<NeowEffectPotionSnapshot> source) =>
        source.GroupBy(item => item.PotionKey, ModelKeyComparer.Instance).Any(group => group.Count() > 1);

    private static IEnumerable<(EffectPotionRarity Rarity, double Mass)> PotionRarityMasses()
    {
        yield return (EffectPotionRarity.Rare, 0.10d);
        yield return (EffectPotionRarity.Uncommon, 0.25d);
        yield return (EffectPotionRarity.Common, 0.65d);
    }

    private static NeowEffectCardSnapshot[] TransformCandidates(
        IReadOnlyList<NeowEffectCardSnapshot> transformPool,
        NeowEffectCardSnapshot source) => transformPool
        .Where(card => string.Equals(card.PoolId, source.PoolId, StringComparison.Ordinal))
        .Where(card => card.CardKey != source.CardKey)
        .Where(card => card.Rarity is not EffectCardRarity.Basic and not EffectCardRarity.Ancient)
        .OrderBy(card => card.PoolOrder)
        .ToArray();

    private static double TransformPairContainment(
        IReadOnlyList<NeowEffectCardSnapshot> firstPool,
        IReadOnlyList<NeowEffectCardSnapshot> secondPool,
        IReadOnlyList<ModelKey> required)
    {
        double P(IReadOnlyList<NeowEffectCardSnapshot> pool, ModelKey key) =>
            pool.Count(card => card.CardKey == key) / (double)pool.Count;
        if (required.Count == 1)
        {
            double a = P(firstPool, required[0]);
            double b = P(secondPool, required[0]);
            return Math.Clamp(1d - (1d - a) * (1d - b), 0d, 1d);
        }
        ModelKey left = required[0];
        ModelKey right = required[1];
        if (left == right)
            return P(firstPool, left) * P(secondPool, left);
        return Math.Clamp(
            P(firstPool, left) * P(secondPool, right) +
            P(firstPool, right) * P(secondPool, left),
            0d, 1d);
    }

    private static bool TryKaleidoscopeGroupStats(
        SearchSelectivityInput plan,
        IReadOnlyList<CharacterCardPoolSnapshot> sourcePools,
        IReadOnlyList<ModelKey> targets,
        out double pFirst,
        out double pSecond,
        out double pBoth,
        out string issue)
    {
        pFirst = pSecond = pBoth = 0d;
        issue = string.Empty;
        CharacterCardPoolSnapshot[] pools = sourcePools.OrderBy(pool => pool.PoolOrder).ToArray();
        int n = pools.Length;
        if (n < 3)
        {
            issue = "KaleidoscopeRequiresAtLeastThreeOtherCharacterPools";
            return false;
        }
        long combos = 0;
        double sumFirst = 0d, sumSecond = 0d, sumBoth = 0d;
        for (int i = 0; i < n - 2; i++)
        for (int j = i + 1; j < n - 1; j++)
        for (int k = j + 1; k < n; k++)
        {
            CharacterCardPoolSnapshot[] selected = { pools[i], pools[j], pools[k] };
            double missFirst = 1d;
            double missSecond = 1d;
            double neither = 1d;
            foreach (CharacterCardPoolSnapshot pool in selected)
            {
                double a = CardOfferContains(pool.Cards, plan, new[] { targets[0] }, 1, forcedRarity: null);
                double b = targets.Count == 1 ? a : CardOfferContains(pool.Cards, plan, new[] { targets[1] }, 1, forcedRarity: null);
                double ab = targets.Count == 1 ? a : CardOfferContains(pool.Cards, plan, targets.Distinct(ModelKeyComparer.Instance).ToArray(), 1, forcedRarity: null);
                missFirst *= 1d - a;
                missSecond *= 1d - b;
                neither *= 1d - ab;
            }
            double aGroup = 1d - missFirst;
            double bGroup = 1d - missSecond;
            double union = 1d - neither;
            double bothGroup = Math.Clamp(aGroup + bGroup - union, 0d, 1d);
            sumFirst += aGroup;
            sumSecond += bGroup;
            sumBoth += bothGroup;
            combos++;
        }
        if (combos == 0)
        {
            issue = "KaleidoscopeCombinationEnumerationEmpty";
            return false;
        }
        pFirst = sumFirst / combos;
        pSecond = sumSecond / combos;
        pBoth = sumBoth / combos;
        return true;
    }
}
