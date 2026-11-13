using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using System.Runtime.CompilerServices;

namespace RolltheSpire2.Search.Runtime;

// One run-global ordinary Unknown observation, with each authored source kept.
// This owns no opening choice, acquisition, route witness, or multiplayer chest.
internal static class PartyInitialRooms
{
    private static readonly ConditionalWeakTable<SearchModel, Lazy<SearchModel>> IndependentProjections = new();
    internal readonly record struct PrefixRequirement(int? Slot, int Count, UnknownRoomCategory Category = UnknownRoomCategory.Event)
    {
        internal string ConditionId => (Slot is int slot ? $"P{slot + 1}." : "") +
            (Category == UnknownRoomCategory.Monster ? "FirstActUnknownMonsterPrefix" : "FirstActUnknownEventPrefix");
    }
    internal static PrefixRequirement[] Prefixes(SearchModel request)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        return (query.FirstActUnknownEventPrefixCount > 0 ? new[] { new PrefixRequirement(null, query.FirstActUnknownEventPrefixCount) } : [])
            .Concat(query.Players.Where(player => player.Conditions.FirstActUnknownEventPrefixCount > 0)
                .Select(player => new PrefixRequirement(player.Slot, player.Conditions.FirstActUnknownEventPrefixCount)))
            .Concat(query.FirstActUnknownMonsterPrefixCount > 0 ? new[] { new PrefixRequirement(null, query.FirstActUnknownMonsterPrefixCount, UnknownRoomCategory.Monster) } : [])
            .Concat(query.Players.Where(player => player.Conditions.FirstActUnknownMonsterPrefixCount > 0)
                .Select(player => new PrefixRequirement(player.Slot, player.Conditions.FirstActUnknownMonsterPrefixCount, UnknownRoomCategory.Monster))).ToArray();
    }
    internal static bool HasTreasure(SearchModel request)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        return query.TreasureRelics is { IsEmpty: false } || query.Players.Any(player => player.Conditions.TreasureRelics is { IsEmpty: false });
    }
    internal static bool HasConditions(SearchModel request)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        return UnknownRoomPrefixRequirements.HasConditions(query) || query.TreasureRelics is { IsEmpty: false } ||
            query.Players.Any(static player =>
                player.Conditions.TreasureRelics is { IsEmpty: false });
    }

    internal static bool TryOrdinaryUnknown(SearchModel request, out int prefix, out string cause)
    {
        prefix = Prefixes(request).Select(requirement => requirement.Count).DefaultIfEmpty(0).Max();
        var party = request.CompiledSearch.Context.Party;
        if (prefix == 0) { cause = "FirstActUnknownEventPrefix.NotRequested"; return false; }
        if (request.ProfileId != RuntimeProfileId.Beta111 || party is null || party.Players.Count is < 2 or > 4)
        { cause = "FirstActUnknownEventPrefix.PartyProfileUnsupported"; return false; }
        if (party.Players.Any(owner => !owner.IsBeta111Context || !owner.CanUseCurrentModel) ||
            party.World.Beta109Generation is not { HasExactPartyRoster: true, NoUnknownHooksOrModifiers: true })
        { cause = "FirstActUnknownEventPrefix.PartyOrdinaryAuthorityUnavailable"; return false; }
        cause = ""; return true;
    }

    // Only an internal execution projection. The caller retains the original
    // request, fingerprint and all obligations for delivery and final combination.
    internal static SearchModel IndependentDomains(SearchModel request)
        => IndependentProjections.GetValue(request, static key => new Lazy<SearchModel>(
            () => CompileIndependentDomains(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static SearchModel CompileIndependentDomains(SearchModel request)
    {
        SearchQuery Strip(SearchQuery query) => query with { TreasureRelics = null, FirstActUnknownEventPrefixCount = 0, FirstActUnknownMonsterPrefixCount = 0 };
        var query = Strip(request.CompiledSearch.NormalizedQuery) with {
            Players = request.CompiledSearch.NormalizedQuery.Players.Select(player => player with { Conditions = Strip(player.Conditions) }).ToArray() };
        var compiled = SearchCompiler.Compile(query, request.CompiledSearch.Context);
        return SearchPreparationCompiler.ForPlayer(request,compiled) with
        { PartyOpeningChoices=request.PartyOpeningChoices,ExactOptions=request.ExactOptions };

    }

    internal static SearchQueryEvaluation Evaluate(SearchModel request, ulong root)
    {
        var requirements = Prefixes(request);
        SearchQueryEvaluation unknown = SearchQueryEvaluation.Match();
        if (requirements.Length > 0)
        {
            if (!TryOrdinaryUnknown(request, out int count, out string cause)) unknown = cause == "FirstActUnknownEventPrefix.PartyProfileUnsupported"
                ? SearchQueryEvaluation.Unsupported(cause) : SearchQueryEvaluation.Unknown(cause);
            else
            {
                bool juzu = ExplicitOpeningRelicFacts.HasDeclaredJuzuForUnknownRooms(request.CompiledSearch.NormalizedQuery);
                var sequence = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, juzu);
                var failed = requirements.FirstOrDefault(requirement => sequence.Steps.Take(requirement.Count).Any(step => step.Room != requirement.Category));
                bool mismatch = failed.Count > 0;
                var evidence = requirements.Select(requirement =>
                    new SearchMatchEvidence(requirement.ConditionId + "Matched",
                        EvidenceCode: new("search.initial-rooms.standard-multiplayer-shared"),
                        ProfileId: request.ProfileId, Act: 1, StreamDomain: "unknown_map_point",
                        AuthorityFingerprint: request.CompiledSearch.Context.Party!.Fingerprint,
                        Ordinal: requirement.Count, ConditionId: requirement.ConditionId)).ToArray();
                if (!mismatch) unknown = SearchQueryEvaluation.Match(evidence);
                else unknown = SearchQueryEvaluation.NoMatch(failed.ConditionId +
                    (failed.Category == UnknownRoomCategory.Monster ? ".NonMonsterEncounter" : ".NonEventEncounter"));
            }
        }
        // A shared Unknown mismatch can reject independently of the disabled
        // chest requirement; a partial success cannot confirm the conjunction.
        return ProductionExactSearchEvaluator.CombineIndependentEvaluations(unknown, HasTreasure(request)
            ? SearchQueryEvaluation.Unsupported("InitialRooms.SinglePlayerOnly") : SearchQueryEvaluation.Match());
    }
}
