using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Project only the predicates physically checked by the shared party stage.
// In particular M and personal A options never reduce W's input population.
internal static class PartyStageSurvival
{
    internal static FamilySurvivalProjection ProjectNeow(SearchModel request, IReadOnlyList<NeowReplayPlan> checkedPredicates)
    {
        const string family = "N.Neow";
        var source = request.CompiledSearch.NormalizedQuery;
        var players = source.Players.Select(player => player with
        {
            SelectedOption = null,
            Results = [],
            Conditions = SearchQuery.Empty with
            {
                OpeningRoute = player.Conditions.OpeningRoute,
                NeowOfferWhitelist = player.Conditions.NeowOfferWhitelist,
                LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = player.Conditions.LegacyNeow.NeowRelics },
                LegacyWorld = SearchQuery.Empty.LegacyWorld with
                {
                    AncientOptionFilters = player.Conditions.LegacyWorld.AncientOptionFilters
                        .Where(filter => filter.Act == 1).ToArray()
                }
            }
        }).ToArray();
        var offers = SearchQuery.Empty with { Players = players };
        if (checkedPredicates.Count > 0 && TryProjectDirectLeafy(request, family, offers, checkedPredicates) is { } leafy)
            return leafy;
        var gate = Estimate(request, family, offers);
        return gate.SurvivalProbability is double p ? FamilySurvivalProjection.Resolved(family, p,
            "PartyN.OfferGateUpperEnvelope;ResultArrivalUnionsAndExactRemaindersNotPaid;" + gate.Evidence) :
            FamilySurvivalProjection.Unresolved(family, "PartyN.OfferProbabilityUnavailable;" + gate.Evidence);
    }

    private static FamilySurvivalProjection? TryProjectDirectLeafy(SearchModel request, string family,
        SearchQuery offers, IReadOnlyList<NeowReplayPlan> predicates)
    {
        // Use each seat's retained source row and direct route. Distinct seats
        // own separate Transformations streams. The party probability model
        // applies its existing cross-player independence approximation.
        var checkedRows = new Dictionary<int, (NeowRouteSearchCondition Route, NeowStructuredEffectSearchCondition Row)>();
        foreach (var predicate in predicates)
        {
            if (predicate is not { Bones: false, Selected: Beta110FastRelicCatalog.LeafyPoultice,
                EnabledDomains: Beta110FastDomain.LeafyPoulticeTransforms, SharedArrivals.Length: 0,
                StructuredConditions: [{ Kind: Beta110FastStructuredConditionKind.LeafyPoulticeTransforms, TargetCount: 1 or 2 }],
                Filter: { NeowRoute: { } route, StructuredNeowEffects: [var row] } } ||
                !checkedRows.TryAdd(predicate.Authority.PlayerSlotIndex, (route, row))) return null;
        }
        var projected = offers with { Players = offers.Players.Select(player =>
            checkedRows.TryGetValue(player.Slot, out var own) ? player with
            {
                Conditions = player.Conditions with { OpeningRoute = own.Route, StructuredOpeningEffects = [own.Row] }
            } : player).ToArray() };
        var estimate = FamilyAuxiliaryEvidence.Read<FamilySurvivalProjection?>(family, "DirectLeafySurvival",
            () => Estimate(request, family, projected), null, request.SnapshotFingerprint);
        if (estimate?.SurvivalProbability is not double survival) return null;
        string evidence = predicates.Count == 1 && predicates[0].StructuredConditions[0].TargetCount == 2
            ? "PartyN.DirectLeafyPair" : "PartyN.DirectLeafy";
        return FamilySurvivalProjection.Resolved(family, survival,
            $"{evidence};CheckedLeafySeats={checkedRows.Count};" + estimate.Evidence);
    }

    internal static FamilySurvivalProjection Project(SearchModel request, string familyId)
    {
        SearchQuery source = request.CompiledSearch.NormalizedQuery;
        SearchQuery projected = familyId switch
        {
            "W.World" => SearchQuery.Empty with
            {
                Players = EmptyPlayers(source),
                VariantBossBranches = source.VariantBossBranches,
                EventSequenceConstraints = source.EventSequenceConstraints,
                LegacyWorld = SearchQuery.Empty.LegacyWorld with
                {
                    BossFilters = source.LegacyWorld.BossFilters,
                    BossOrdinalFilters = source.LegacyWorld.BossOrdinalFilters,
                    AncientIdentityFilters = source.LegacyWorld.AncientIdentityFilters
                }
            },
            "E.EventResult" => SearchQuery.Empty with
            {
                Players = source.Players.Select(player => player with
                {
                    Offers = ModelKeySetFilter.Empty,
                    Conditions = SearchQuery.Empty with
                    {
                        EventResultConditions = player.Conditions.EventResultConditions
                    }
                }).ToArray()
            },
            _ => throw new ArgumentOutOfRangeException(nameof(familyId))
        };
        return Estimate(request, familyId, projected);
    }

    private static FamilySurvivalProjection Estimate(SearchModel request, string familyId, SearchQuery projected)
    {
        CompiledSearch compiled = PartyInitialQuery.Compile(projected, request.CompiledSearch.Context);
        JointSelectivityResult estimate = PartyQueryProbability.Estimate(compiled);
        return estimate.Probability is double p && estimate.JointlyPriced
            ? FamilySurvivalProjection.Resolved(familyId, p,
                "PartyPhysicalPredicateProjection;" + estimate.EvidenceCode)
            : FamilySurvivalProjection.Unresolved(familyId,
                "PartyPhysicalPredicateProbabilityUnavailable;" + estimate.EvidenceCode);
    }

    private static PlayerOfferQuery[] EmptyPlayers(SearchQuery source) => source.Players
        .Select(player => player with
        {
            Offers = ModelKeySetFilter.Empty,
            Conditions = SearchQuery.Empty,
            SelectedOption = null,
            Results = []
        }).ToArray();
}
