using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class NeowStructuredEffectProbabilityEstimator
{
    internal static SearchSelectivityEstimate EstimatePartyOpening(SearchSelectivityInput plan)
    {
        var filter = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        if (filter.NeowRoute is { IsValid: true } route)
            return EstimatePinnedPartyOpening(plan, route.RouteRelicKey);
        // The current editor always binds result predicates to a selected source.
        // A completely unselected N only observes its initial offers and remains
        // neutral for later players. Historical raw unbound-result queries are
        // retained by Exact but are outside the Owner's UI-reachable goal.
        return HasOpeningResults(filter)
            ? SearchSelectivityEstimate.Unpriced("Probability.Neow.UnboundResultsOutsideUiScope",
                "Current result editing binds a parent route; this legacy raw shape is outside the goal.")
            : OfferProbability(plan, default);
    }

    private static bool HasOpeningResults(NeowSearchFilter f) =>
        f.RequireNeowsBones || !f.BonesRelics.IsEmpty || f.RequiredBonesCombination.Count > 0 ||
        f.RequiredBonesAcquisitionOrder.Count > 0 || f.RequireSmallCapsule || f.RequireLargeCapsule ||
        !f.CapsuleContainedRelics.IsEmpty || f.RequireWhetstone || f.RequireWarPaint ||
        f.RequiredFinalCurse.HasValue || f.BannedFinalCurses.Count > 0 ||
        f.StructuredNeowEffects.Any(c => !c.IsEmpty) || f.EffectOutputConditions.Any(c => !c.IsEmpty) ||
        f.Preset != NeowSearchPreset.None;

    private static SearchSelectivityEstimate EstimatePinnedPartyOpening(SearchSelectivityInput plan, ModelKey source)
    {
        var f = ProbabilitySemanticProjection.From(plan).NumericalFilter;
        bool bones = source == BaseGameModelKeys.Relics.NeowsBones;
        bool small = source == BaseGameModelKeys.Relics.SmallCapsule;
        bool large = source == BaseGameModelKeys.Relics.LargeCapsule;
        bool needsBones = f.RequireNeowsBones || !f.BonesRelics.IsEmpty || f.RequiredBonesCombination.Count > 0 ||
            f.RequiredBonesAcquisitionOrder.Count > 0 || f.RequiredFinalCurse.HasValue || f.BannedFinalCurses.Count > 0 ||
            f.Preset != NeowSearchPreset.None;
        bool needsCapsule = !f.CapsuleContainedRelics.IsEmpty || f.RequireWhetstone || f.RequireWarPaint;
        if (!bones && (needsBones || f.RequireSmallCapsule && !small || f.RequireLargeCapsule && !large ||
            needsCapsule && !small && !large ||
            f.StructuredNeowEffects.Any(c => !c.IsEmpty && c.SourceRelicKey != source) ||
            f.EffectOutputConditions.Any(c => !c.IsEmpty && c.SourceRelicKey != source)))
            return OpeningImpossible("Selected route cannot produce all requested opening sources.");
        if (f.EffectOutputConditions.Any(c => !c.IsEmpty))
            return SearchSelectivityEstimate.Unpriced("Probability.Neow.InternalObjectiveOutputOutsideUiScope",
                "The legacy research-only objective-output contract is outside the current UI-reachable probability goal.");
        if (f.Preset != NeowSearchPreset.None)
            return SearchSelectivityEstimate.Unpriced("Probability.Neow.PartyAutomaticUpgradePreset", "Automatic upgrades after deck changes require a deck-conditioned outcome model.");
        if (bones) return EstimateBonesQuery(plan);
        if (small || large)
        {
            if (!TryTopLevelRouteProbability(plan, source, out double parent, out _))
                return SearchSelectivityEstimate.Unpriced("Probability.Neow.PartyOfferAuthorityMissing", "Opening offer pools unavailable.");
            if (parent == 0) return OpeningImpossible("Selected Capsule is not offered.");
            if (!CapsuleRelicProbabilityEstimator.TryEstimateOpeningCapsules(plan, [source], false,
                    out double child, out string detail, out string issue))
                return SearchSelectivityEstimate.Unpriced("Probability.Neow.PartyCapsule:" + issue, detail);
            return SearchSelectivityEstimate.Exact(parent * child, SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.Neow.PartyCapsule", detail);
        }
        return f.StructuredNeowEffects.Any(c => !c.IsEmpty) ? Estimate(plan) : OfferProbability(plan, source);
    }

    private static SearchSelectivityEstimate OfferProbability(SearchSelectivityInput plan, ModelKey source) =>
        TryTopLevelRouteProbability(plan, source, out double probability, out string detail)
            ? SearchSelectivityEstimate.Exact(probability, SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.Neow.CompleteOffer", detail)
            : SearchSelectivityEstimate.Unpriced("Probability.Neow.OfferJointUnavailable", "Opening offer authority missing.");

    private static SearchSelectivityEstimate OpeningImpossible(string detail) =>
        SearchSelectivityEstimate.Exact(0, SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction, SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Neow.PartyOpeningImpossible", detail);
}
