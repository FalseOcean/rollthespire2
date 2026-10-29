using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

internal static class StandardMapExactEvaluator
{
    internal static SearchQueryEvaluation Evaluate(ExactSearchExecutionRequest request, string seed, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var conditions = request.CompiledSearch.NormalizedQuery.StandardMaps;
        var routes = request.CompiledSearch.NormalizedQuery.StandardMapRoutes;
        if (conditions.Count == 0 && routes.Count == 0) return SearchQueryEvaluation.Match();
        if (request.ProfileId != RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
            return SearchQueryEvaluation.Unsupported("StandardMap.Beta111Required");
        var values = new Dictionary<int,MapOutcomeSignature>();
        var maps = new Dictionary<int,GeneratedStandardMap>();
        GeneratedStandardMap Observe(int act)
        {
            if (maps.TryGetValue(act, out var map)) return map;
            // Independent source-faithful graph; never a numerical workspace snapshot.
            map = Beta111StandardMapGenerator.GenerateStandardMap(seed, StandardMapFamily.Context(act,request.Ascension)
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
        var evidence = new List<SearchMatchEvidence>
        {
            new("StandardActMap.PreHook.Final", StreamDomain:"act_map", ConditionId:"M.StandardMap")
        };
        static string Positions(IEnumerable<MapPosition> path) => string.Join(">", path.Select(p => $"r{p.Row}:c{p.Column}"));
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
