using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

internal static class StandardMapExactEvaluator
{
    internal static SearchQueryEvaluation Evaluate(SearchModel request, string seed, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var conditions = request.CompiledSearch.NormalizedQuery.StandardMaps;
        var routes = request.CompiledSearch.NormalizedQuery.StandardMapRoutes;
        var research = request.CompiledSearch.NormalizedQuery.ElitePrefixResearch;
        if (conditions.Count == 0 && routes.Count == 0 && research is null) return SearchQueryEvaluation.Match();
        if (request.ProfileId != RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
            return SearchQueryEvaluation.Unsupported("StandardMap.Beta111Required");
        var values = new Dictionary<int,MapOutcomeSignature>();
        var maps = new Dictionary<int,GeneratedStandardMap>();
        GeneratedStandardMap Observe(int act)
        {
            if (maps.TryGetValue(act, out var map)) return map;
            // Independent source-faithful graph; never a numerical workspace snapshot.
            map = Beta111StandardMapGenerator.GenerateStandardMap(seed, StandardMapFamily.Context(act,request.Ascension,
                request.Authority.VersionResolution.SemanticProfileId)
                with { IsMultiplayer = request.Authority.PlayersCount > 1 }, token);
            maps.Add(act, map);
            return map;
        }
        bool match = StandardMapFamily.Matches(conditions,act =>
        {
            if (values.TryGetValue(act,out var value)) return value;
            value=MapBasisScalars.Evaluate(Observe(act)); values.Add(act,value); return value;
        });
        if (!match) return SearchQueryEvaluation.NoMatch("StandardMap.PredicateRejected");
        var evidence = new List<SearchMatchEvidence>();
        if (conditions.Count > 0 || routes.Count > 0)
            evidence.Add(new("StandardActMap.PreHook.Final", StreamDomain:"act_map", ConditionId:"M.StandardMap"));
        static string Positions(IEnumerable<MapPosition> path) => string.Join(">", path.Select(p => $"r{p.Row}:c{p.Column}"));
        if (research is not null)
        {
            var result = research.Mode == ElitePrefixResearchMode.FloorSeven
                ? ElitePrefixResearchEvaluator.EvaluateFloorSeven(Observe(1), token)
                : ElitePrefixResearchEvaluator.Evaluate(Observe(1), research.ExtraMonsters, research.Unknowns, token);
            if (!result.Matches) return SearchQueryEvaluation.NoMatch("ElitePrefixResearch.AllPathsRejected");
            // This certifies the universal graph predicate, not a single route witness.
            evidence.Add(new("StandardActMap.ElitePrefixResearch.AllPaths.Final", Act:1, StreamDomain:"act_map",
                ConditionId:"ElitePrefixResearch",
                OutcomeId:"allPaths=true;" + (research.Mode == ElitePrefixResearchMode.FloorSeven
                    ? "firstEliteFloor=7;prefixIcons=Monster,Unknown;counts=unrestricted;"
                    : $"fixedMonsters=1;extraMonsters={research.ExtraMonsters};unknowns={research.Unknowns};") +
                    $"visitedStates={result.VisitedStates};firstElites=" + Positions(result.FirstElites)));
        }
        foreach (var route in routes)
        {
            var result = MapRouteSequenceEvaluator.Evaluate(Observe(route.Act), route.ToSequenceQuery(), token);
            if (!result.Matches) return SearchQueryEvaluation.NoMatch("StandardMap.RouteSequenceRejected");
            // Map path identity is evidence local to this Act graph, never an opening
            // choice or a commitment to follow this path in the real game.
            evidence.Add(new("StandardActMap.RouteSequence.Final", Act:route.Act, StreamDomain:"act_map",
                ConditionId:$"M.StandardMap.RouteSequence.Act{route.Act}",
                RouteId:$"act-{route.Act}:" + Positions(result.Path),
                OutcomeId:string.Join("|", result.MatchedGroups.Select(group => "[" + Positions(group) + "]"))));
        }
        return SearchQueryEvaluation.Match(evidence);
    }
}
