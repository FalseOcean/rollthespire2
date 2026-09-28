using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

internal static class StandardMapExactEvaluator
{
    internal static SearchQueryEvaluation Evaluate(ExactSearchExecutionRequest request, string seed)
    {
        var conditions = request.CompiledSearch.NormalizedQuery.StandardMaps;
        if (conditions.Count == 0) return SearchQueryEvaluation.Match();
        var values = new Dictionary<int,MapOutcomeSignature>();
        bool match = StandardMapFamily.Matches(conditions,act =>
        {
            if (values.TryGetValue(act,out var value)) return value;
            // Independent, source-faithful existing Predictor graph, not the numerical workspace.
            var map = Beta111StandardMapGenerator.GenerateStandardMap(seed,StandardMapFamily.Context(act,request.Ascension) with { IsMultiplayer = request.Authority.PlayersCount > 1 });
            value=MapBasisScalars.Evaluate(map); values.Add(act,value); return value;
        });
        return match ? SearchQueryEvaluation.Match([new("StandardActMap.PreHook.Final", StreamDomain:"act_map", ConditionId:"M.StandardMap")]) :
            SearchQueryEvaluation.NoMatch("StandardMap.PredicateRejected");
    }
}
