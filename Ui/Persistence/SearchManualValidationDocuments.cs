using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Core.Authority;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

internal sealed record ManualValidationAuthorityIdentity(int Slot, string UnlockFingerprint, string CatalogFingerprint,
    string EffectFingerprint, string WorldFingerprint);

// A failed explicit check owns its original result and a single-seed obligation,
// independently of the display quota and the ordinary scanning cursor.
internal sealed record ManualValidationRecovery(PersistedSearchResult Original, SearchContinuationSnapshot Continuation)
{
    // An already completed Exact observation is delivery work, never a reason
    // to run Exact again after a restart or display-quota eviction.
    public PersistedSearchResult? ReceivedObservation { get; init; }
}

// Hashes and scalar provenance only. No execution plan, pools or captured
// authority payload are persisted in the query history.
internal sealed record ManualValidationSourceIdentity(int Version, string SemanticFingerprint,
    string SnapshotFingerprint, string QueryKey, string PayloadSignature,
    IReadOnlyList<ManualValidationAuthorityIdentity> Authorities)
{
    internal static ManualValidationSourceIdentity Capture(ExactSearchExecutionRequest plan, string queryKey) =>
        new(2, plan.CompiledSearch.SemanticFingerprint, plan.SnapshotFingerprint, queryKey,
            SearchContinuationIdentity.For(plan), AuthorityIdentities(plan.CompiledSearch));

    internal bool Proves(CompiledSearch compiled, string queryKey, CombatRewardRoutePolicyContract? policy = null) =>
        QueryKey == queryKey && (Version == 2 ?
            PayloadSignature == (policy is null ? SearchContinuationIdentity.For(compiled) : SearchContinuationIdentity.For(compiled, policy)) : Version == 1 &&
            Authorities.SequenceEqual(AuthorityIdentities(compiled)) && PayloadSignature == Signature(compiled));

    private static ManualValidationAuthorityIdentity[] AuthorityIdentities(CompiledSearch compiled) =>
        (compiled.Context.Party?.Players ?? [compiled.Context.Authority]).Select(authority => new ManualValidationAuthorityIdentity(
            authority.PlayerSlotIndex, authority.UnlockSnapshotFingerprint, authority.CatalogFingerprint,
            authority.EffectSnapshotFingerprint, authority.WorldSnapshotFingerprint)).ToArray();

    private static RuntimeContextAuthoritySnapshot WithoutCaptureTimes(RuntimeContextAuthoritySnapshot authority) =>
        authority.WithEffectAuthority(authority.EffectAuthority is { } effects ? effects with { CapturedAtUtc = null } : null)
            .WithWorldAuthority(authority.WorldAuthority is { } world ? world with { CapturedAtUtc = null } : null);

    private static string Signature(CompiledSearch compiled)
    {
        // These two typed fields record observation time, never a model input.
        // Every other authority/scenario field and all ordered pools participate.
        static MorphicGroveScenario? Scenario(MorphicGroveScenario? scenario) => scenario is null ? null :
            new(WithoutCaptureTimes(scenario.Authority), scenario.Premises, scenario.Targets);
        static SearchQuery Query(SearchQuery query) => query with
        {
            EventResultConditions = query.EventResultConditions.Select(condition => condition with
                { MorphicGroveScenario = Scenario(condition.MorphicGroveScenario) }).ToArray(),
            TransformationAggregate = query.TransformationAggregate is { } aggregate ? aggregate with
                { EventScenario = Scenario(aggregate.EventScenario) } : null,
            Players = query.Players.Select(player => player with { Conditions = Query(player.Conditions) }).ToArray()
        };
        var context = compiled.Context with
        {
            Authority = WithoutCaptureTimes(compiled.Context.Authority),
            Party = compiled.Context.Party is { } party ? new OrderedPartyAuthority(party.Players.Select(WithoutCaptureTimes).ToArray(),
                party.UnlockProvenance, party.World with { CapturedAtUtc = null }, party.UnlockSources) : null
        };
        static string Canonical(JsonNode? node) => node switch
        {
            JsonObject obj => "{" + string.Join(",", obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + Canonical(pair.Value))) + "}",
            JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
            _ => node?.ToJsonString() ?? "null"
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(JsonSerializer.SerializeToNode(
            new { Query = Query(compiled.NormalizedQuery), Context = context })))));
    }
}
