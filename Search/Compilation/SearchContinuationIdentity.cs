using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Compilation;

// Request/model inputs are stable across scan windows and recapture. The
// execution SnapshotFingerprint remains the identity of each actual attempt.
internal static class SearchContinuationIdentity
{
    internal static string For(ExactSearchExecutionRequest plan) =>
        For(plan.CompiledSearch, plan.CombatRewardRoutePolicy);

    internal static string For(CompiledSearch compiled) =>
        For(compiled, ExactSearchExecutionRequestFactory.BuildCombatRewardRoutePolicy(compiled));

    internal static string For(CompiledSearch compiled, CombatRewardRoutePolicyContract policy)
    {
        var context = compiled.Context;
        var payload = JsonSerializer.SerializeToNode(new
        {
            Query = QueryPayload(compiled.NormalizedQuery),
            context.ProfileId, context.CharacterKey, context.Ascension,
            Authority = RuntimePredictionInputIdentity.AuthorityPayload(context.Authority),
            context.EvaluationAssumptions,
            Party = context.Party is { } party ? new
            {
                Players = party.Players.Select(RuntimePredictionInputIdentity.AuthorityPayload).ToArray(),
                party.UnlockProvenance, party.UnlockSources,
                World = RuntimePredictionInputIdentity.AuthorityPayload(
                    context.Authority.WithWorldAuthority(party.World))?["WorldAuthority"]?.DeepClone()
            } : null,
            compiled.ResolvedRouteSemantics, compiled.RequiresComplexBonesDeckInteractionEvaluation,
            Policy = policy
        });
        return "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(payload))));
    }

    private static JsonNode? QueryPayload(SearchQuery query)
    {
        var node = JsonSerializer.SerializeToNode(query)!;
        for (int i = 0; i < query.EventResultConditions.Count; i++)
            if (query.EventResultConditions[i].MorphicGroveScenario is { } scenario)
                node["EventResultConditions"]![i]!["MorphicGroveScenario"]!["Authority"] =
                    RuntimePredictionInputIdentity.AuthorityPayload(scenario.Authority);
        if (query.TransformationAggregate?.EventScenario is { } aggregate)
            node["TransformationAggregate"]!["EventScenario"]!["Authority"] =
                RuntimePredictionInputIdentity.AuthorityPayload(aggregate.Authority);
        for (int i = 0; i < query.Players.Count; i++)
            node["Players"]![i]!["Conditions"] = QueryPayload(query.Players[i].Conditions);
        return node;
    }

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node?.ToJsonString() ?? "null"
    };

    internal static bool Matches(SearchContinuationSnapshot continuation, ExactSearchExecutionRequest plan,
        string fingerprint) => continuation.ContinuationFingerprint.Length > 0
            ? continuation.ContinuationFingerprint == fingerprint
            : continuation.SemanticFingerprint == plan.CompiledSearch.SemanticFingerprint &&
                continuation.SnapshotFingerprint == plan.SnapshotFingerprint;

    internal static bool SameSource(SearchContinuationSnapshot first, SearchContinuationSnapshot second) =>
        first.ContinuationFingerprint.Length > 0 && second.ContinuationFingerprint.Length > 0
            ? first.ContinuationFingerprint == second.ContinuationFingerprint
            : first.SemanticFingerprint == second.SemanticFingerprint && first.SnapshotFingerprint == second.SnapshotFingerprint;
}
