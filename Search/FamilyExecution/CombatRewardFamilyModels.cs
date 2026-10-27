using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// C-private projections only. No runtime fitting and no new probability authority.
internal sealed class CombatRewardFamilyModels
{
    private const string Id = "C.CombatReward";
    private readonly FamilyExpectedFilteringCostSegment[] _segments;
    internal FamilySurvivalProjection Survival { get; }
    internal FamilyAnalyticalCostProjection Analytical { get; }

    internal CombatRewardFamilyModels(ExactSearchExecutionRequest request, CombatRewardReplay replay, int capacity)
    {
        var plan = replay.Plan;
        var estimate = FamilyAuxiliaryEvidence.Read(Id, "InitialSurvival", () => CombatRewardSieveProbability.Estimate(request, replay),
            SearchSelectivityEstimate.Unpriced("AuxiliarySurvivalFailed", "Combat probability is unavailable."), request.SnapshotFingerprint);
        Survival = replay.ConservativelyKeeps
            ? FamilySurvivalProjection.Resolved(Id, 1d,
                "C.ConservativeOpeningKeepsAll;PositivePredicatesRemainExact;Cause=" + replay.ConservativeOpeningReason)
            : estimate.PricingClass == SearchSelectivityPricingClass.ExactPriced && estimate.Probability is double p
            ? FamilySurvivalProjection.Resolved(Id, p, "C.CompletePositiveFilter.OwnerFixedPremises;" + estimate.EvidenceCode)
            : FamilySurvivalProjection.Unresolved(Id, estimate.EvidenceCode + ";" + estimate.Notes);

        var catalog = replay.Catalog;
        var segments = new List<FamilyExpectedFilteringCostSegment>();
        void Add(string name, FamilyAnalyticalOperation operation, double units) =>
            segments.Add(new(name, operation, units, 1d));
        Add("Root", FamilyAnalyticalOperation.CandidateReconstruction, 1);
        // Full reference envelope: both authored routes, all combats and pool
        // scans. No unproved early-stop reach discounts, hot-loop savings or ms.
        // CountAvailable + SelectAvailable each scan at most the complete pool,
        // with at most selectedLimit identity probes per pool entry.
        static double Card(Beta110FastCardPool pool, int selectedLimit) =>
            4d + 2d * pool.TotalCount * Math.Max(1, selectedLimit);
        double prefix = plan.OpeningConsumption.ReplayBonesOffer
            ? Math.Max(0, (request.Authority.EffectAuthority?.BonesEligibleRelics?
                .Count(k => k != BaseGameModelKeys.Relics.NeowsBones) ?? 0) - 1) : 0;
        double openingCard = new[] { catalog.CharacterRewardPool, catalog.ColorlessRewardPool }
            .Concat(catalog.OtherCharacterPools).Max(pool => Card(pool, 6));
        foreach (byte relic in plan.OpeningConsumption.OrderedRelicIds)
        {
            prefix += 1; // authored dispatch, including neutral children
            prefix += relic switch
            {
                Beta110FastRelicCatalog.SmallCapsule => 1,
                Beta110FastRelicCatalog.LargeCapsule => 2,
                Beta110FastRelicCatalog.ArcaneScroll => openingCard,
                Beta110FastRelicCatalog.HeftyTablet => 3 * openingCard,
                Beta110FastRelicCatalog.LeadPaperweight => 2 * openingCard,
                Beta110FastRelicCatalog.LostCoffer => 3 * openingCard + 2 + 2 * catalog.PotionPool.TotalCount,
                Beta110FastRelicCatalog.Kaleidoscope => 6 * openingCard + 2 * Math.Max(0, catalog.OtherCharacterPools.Length - 1),
                Beta110FastRelicCatalog.ScrollBoxes => 6 * openingCard + 2,
                _ => 0
            };
        }
        Add("AuthoredPrefix.FullRoutes", FamilyAnalyticalOperation.PredicateProbe, replay.RouteCount * (2 + prefix));
        bool wbs = (plan.ExplicitContext.InfluenceFlags & Beta110CombatRewardInfluenceFlags.ForcePotionReward) != 0;
        bool candy = (plan.ExplicitContext.InfluenceFlags & Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard) != 0;
        int maximumCards = 3 * (1 + plan.ExplicitContext.AdditionalCardRewardCount) + (candy ? 1 : 0);
        for (int battle = 1; battle <= plan.MaximumBattleOrdinal; battle++)
        {
            // Potion decision (unless WBS), gold, at most one potion rarity/id,
            // and observation writes. Potion-drop probability is not guessed.
            double work = (wbs ? 0 : 1) + 1 + 2 + 3;
            work += 3 * (1 + plan.ExplicitContext.AdditionalCardRewardCount) * Card(catalog.CombatRewardCardPool, 3);
            if (candy && battle % 2 == 0) work += Card(catalog.CombatRewardPowerPool, maximumCards);
            if (candy) work += 1; // Candy counter
            Add($"Combat{battle}.FullGeneration", FamilyAnalyticalOperation.ObservationWrite, replay.RouteCount * work);
        }
        foreach (var predicate in plan.Predicates)
        {
            int battles = predicate.IsAnyBattle ? (predicate.AnyBattleWindow == 0 ? plan.MaximumBattleOrdinal : predicate.AnyBattleWindow) : 1;
            double probes = maximumCards * (predicate.CardAny.Length + predicate.CardAll.Length + predicate.CardBan.Length) +
                predicate.PotionAny.Length + predicate.PotionAll.Length + predicate.PotionBan.Length + 3;
            Add("Predicate.FullWindow", FamilyAnalyticalOperation.PredicateProbe, replay.RouteCount * battles * probes);
        }
        if (plan.CardAssignmentTargets.Length > 0)
            Add("Card.DistinctBattleAssignments", FamilyAnalyticalOperation.PredicateProbe,
                replay.RouteCount * plan.CardAssignmentWindow * plan.CardAssignmentTargets.Length * (maximumCards + 3));
        if (plan.PotionAssignmentTargets.Length > 0)
            Add("Potion.DistinctBattleAssignments", FamilyAnalyticalOperation.PredicateProbe,
                replay.RouteCount * plan.PotionAssignmentWindow * plan.PotionAssignmentTargets.Length * 4);
        _segments = segments.ToArray();
        var terms = segments.Select(s => new FamilyAnalyticalWorkTerm(s.Operation, FamilyAnalyticalWorkVariability.Query,
            0, s.FullWorkUnits, 0, 0, s.Name)).ToList();
        terms.Add(new(FamilyAnalyticalOperation.InvocationBoundary, FamilyAnalyticalWorkVariability.Constant, 1, 0, 0, 0, "C invocation"));
        terms.Add(new(FamilyAnalyticalOperation.CompactInputOrdinal, FamilyAnalyticalWorkVariability.Constant, 0, 0, 1, 0, "ABI1 ordinal"));
        terms.Add(new(FamilyAnalyticalOperation.OutputCompaction, FamilyAnalyticalWorkVariability.Path, 0, 0, 0, 1, "Surviving ordinal"));
        Analytical = new(Id, capacity, terms, "C2.FullReferenceOperationEnvelope.v1;NoReachDiscount;NotWallTime");
    }

    // Merely passing another Family does not establish shared reward observations.
    // The fixed declared marginal is unchanged by unrelated passed families;
    // execution/Exact may still conservatively retain unsupported observations.
    internal FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passed) => Survival;

    internal FamilyExpectedFilteringCostProjection Expected(IReadOnlySet<string> passed) => new(Id,
        Analytical.WorkUnitsPerInput, Analytical.WorkUnitsPerInput, ResolveSurvival(passed).SurvivalProbability,
        Analytical.CompactInputWorkUnitsPerInput, _segments,
        "C2.ConservativeFullReferenceEnvelope;AllRoutesAllBattles;Reach=1EnvelopeNotMeasuredReach;NoTimingOrEarlyStopFit");
}
