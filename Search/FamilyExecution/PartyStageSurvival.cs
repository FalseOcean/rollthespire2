using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Project only the predicates physically checked by the shared party stage.
// In particular M and personal A options never reduce W's input population.
internal static class PartyStageSurvival
{
    internal static FamilySurvivalProjection Project(ExactSearchExecutionRequest request, string familyId)
    {
        SearchQuery source = request.CompiledSearch.NormalizedQuery;
        SearchQuery projected = familyId switch
        {
            "N.Neow" => SearchQuery.Empty with
            {
                Players = source.Players.Select(player => player with
                {
                    SelectedOption = null,
                    Results = [],
                    Conditions = SearchQuery.Empty with
                    {
                        OpeningRoute = player.Conditions.OpeningRoute,
                        LegacyNeow = SearchQuery.Empty.LegacyNeow with
                        {
                            NeowRelics = player.Conditions.LegacyNeow.NeowRelics
                        },
                        LegacyWorld = SearchQuery.Empty.LegacyWorld with
                        {
                            AncientOptionFilters = player.Conditions.LegacyWorld.AncientOptionFilters
                                .Where(filter => filter.Act == 1).ToArray()
                        }
                    }
                }).ToArray()
            },
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
