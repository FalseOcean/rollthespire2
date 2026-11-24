using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
    private static readonly ConditionalWeakTable<SearchModel, Lazy<string>> ModelIdentities = new();

    private static Lazy<string> IdentityFor(SearchModel model) => ModelIdentities.GetValue(model, static source =>
        new(() => For(source.CompiledSearch, source.CombatRewardRoutePolicy)));

    // The captured authority is immutable. Build the model identity alongside
    // allocation, then use the same validated value (or failure) in recovery.
    internal static void BeginPreparation(PreparedSearch plan)
    {
        var identity = IdentityFor(plan.Model);
        if (identity.IsValueCreated) return;
        _ = Task.Run(() =>
        {
            try { _ = identity.Value; }
            catch (Exception error) { Bootstrap.RuntimeLog.WarnException("continuationIdentityPreparationFailed=true", error); }
        });
    }

    internal static string For(PreparedSearch plan)
    {
        string model = IdentityFor(plan.Model).Value;
        // Preserve legacy full-search identities; an opt-in lossy frontier must
        // never be resumed as an exhaustively checked full-search frontier.
        return plan.MapLossy.Enabled ? model + ":MapLossy:" + plan.MapLossy.Identity : model;
    }

    internal static string For(CompiledSearch compiled) =>
        For(compiled, SearchPreparationCompiler.BuildCombatRewardRoutePolicy(compiled));

    internal static string For(CompiledSearch compiled, CombatRewardRoutePolicyContract policy)
    {
        var context = compiled.Context;
        var payload = new JsonObject
        {
            ["Query"] = QueryPayload(compiled.NormalizedQuery),
            [nameof(context.ProfileId)] = JsonSerializer.SerializeToNode(context.ProfileId),
            [nameof(context.CharacterKey)] = JsonSerializer.SerializeToNode(context.CharacterKey),
            [nameof(context.Ascension)] = context.Ascension,
            ["Authority"] = RuntimePredictionInputIdentity.AuthorityPayload(context.Authority),
            [nameof(context.EvaluationAssumptions)] = JsonSerializer.SerializeToNode(context.EvaluationAssumptions),
            ["Party"] = context.Party is { } party ? new JsonObject
            {
                ["Players"] = new JsonArray(party.Players.Select(RuntimePredictionInputIdentity.AuthorityPayload).ToArray()),
                [nameof(party.UnlockProvenance)] = JsonSerializer.SerializeToNode(party.UnlockProvenance),
                [nameof(party.UnlockSources)] = JsonSerializer.SerializeToNode(party.UnlockSources),
                ["World"] = RuntimePredictionInputIdentity.AuthorityPayload(
                    context.Authority.WithWorldAuthority(party.World))?["WorldAuthority"]?.DeepClone()
            } : null,
            [nameof(compiled.ResolvedRouteSemantics)] = JsonSerializer.SerializeToNode(compiled.ResolvedRouteSemantics),
            [nameof(compiled.RequiresComplexBonesDeckInteractionEvaluation)] = compiled.RequiresComplexBonesDeckInteractionEvaluation,
            ["Policy"] = JsonSerializer.SerializeToNode(policy)
        };
        var bytes = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(bytes);
        WriteCanonical(writer, payload);
        writer.Flush();
        string identity = "v1:" + Convert.ToHexString(SHA256.HashData(bytes.WrittenSpan));
        return SearchCompiler.UsesRevisedTreasureClaims(context, compiled.NormalizedQuery)
            ? identity + TreasureRelicSearchCondition.ClaimSequenceRevision : identity;
    }

    private static JsonNode? QueryPayload(SearchQuery query)
    {
        var node = JsonSerializer.SerializeToNode(query)!;
        // This field was added after v1 resume identities shipped. Its absent
        // historical value is an empty condition list; retain every authored row.
        if (query.MerchantRelicConditions.Count == 0) node.AsObject().Remove(nameof(SearchQuery.MerchantRelicConditions));
        if (query.EncounterQueues.Count == 0) node.AsObject().Remove(nameof(SearchQuery.EncounterQueues));
        for (int i = 0; i < query.EventResultConditions.Count; i++)
            if (query.EventResultConditions[i].MorphicGroveScenario is { } scenario)
                node["EventResultConditions"]![i]!["MorphicGroveScenario"]!["Authority"] =
                    RuntimePredictionInputIdentity.AuthorityPayload(scenario.Authority);
        if (query.TransformationAggregate is { } aggregate)
        {
            var transform = node[nameof(SearchQuery.TransformationAggregate)]!.AsObject();
            transform.Remove(nameof(TransformationAggregateCondition.UsesAncient));
            transform.Remove(nameof(TransformationAggregateCondition.UsesExtendedRequirements));
            transform.Remove(nameof(TransformationAggregateCondition.AncientOpportunityCount));
            // The old material-count gate could skip seeds that match the same
            // saved request under its declared roll count. Rescan those windows.
            if (aggregate.UsesAncient)
                transform["AncientRollPremiseRevision"] = TransformationAggregateCondition.AncientRollPremiseRevision;
            if (!aggregate.UsesExtendedRequirements)
            {
                // Preserve the v1 identity of aggregate searches saved before source scopes.
                transform.Remove(nameof(TransformationAggregateCondition.AncientSource));
                transform.Remove(nameof(TransformationAggregateCondition.AncientAct));
                transform.Remove(nameof(TransformationAggregateCondition.PandoraCountOverride));
                transform.Remove(nameof(TransformationAggregateCondition.PandoraAutomaticCount));
                transform.Remove(nameof(TransformationAggregateCondition.ResultRequirements));
            }
            if (aggregate.EventScenario is { } eventScenario)
                transform[nameof(TransformationAggregateCondition.EventScenario)]!["Authority"] =
                    RuntimePredictionInputIdentity.AuthorityPayload(eventScenario.Authority);
        }
        for (int i = 0; i < query.Players.Count; i++)
            node["Players"]![i]!["Conditions"] = QueryPayload(query.Players[i].Conditions);
        return node;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var item in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(item.Key);
                    WriteCanonical(writer, item.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case null: writer.WriteNullValue(); break;
            default: node.WriteTo(writer); break;
        }
    }

    internal static bool Matches(SearchContinuationSnapshot continuation, PreparedSearch plan,
        string fingerprint) => continuation.ContinuationFingerprint.Length > 0
            ? continuation.ContinuationFingerprint == fingerprint
            : continuation.SemanticFingerprint == plan.Model.CompiledSearch.SemanticFingerprint &&
                continuation.SnapshotFingerprint == plan.SnapshotFingerprint;

    internal static bool SameSource(SearchContinuationSnapshot first, SearchContinuationSnapshot second) =>
        first.ContinuationFingerprint.Length > 0 && second.ContinuationFingerprint.Length > 0
            ? first.ContinuationFingerprint == second.ContinuationFingerprint
            : first.SemanticFingerprint == second.SemanticFingerprint && first.SnapshotFingerprint == second.SnapshotFingerprint;
}
