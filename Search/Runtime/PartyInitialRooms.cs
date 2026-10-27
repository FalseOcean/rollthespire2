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
    private static readonly ConditionalWeakTable<ExactSearchExecutionRequest, Lazy<ExactSearchExecutionRequest>> IndependentProjections = new();
    internal readonly record struct PrefixRequirement(int? Slot, int Count)
    {
        internal string ConditionId => Slot is int slot ? $"P{slot + 1}.FirstActUnknownEventPrefix" : "FirstActUnknownEventPrefix";
    }
    internal static PrefixRequirement[] Prefixes(ExactSearchExecutionRequest request)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        return (query.FirstActUnknownEventPrefixCount > 0 ? new[] { new PrefixRequirement(null, query.FirstActUnknownEventPrefixCount) } : [])
            .Concat(query.Players.Where(player => player.Conditions.FirstActUnknownEventPrefixCount > 0)
                .Select(player => new PrefixRequirement(player.Slot, player.Conditions.FirstActUnknownEventPrefixCount))).ToArray();
    }
    internal static bool HasTreasure(ExactSearchExecutionRequest request)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        return query.TreasureRelics is { IsEmpty: false } || query.Players.Any(player => player.Conditions.TreasureRelics is { IsEmpty: false });
    }
    internal static bool HasConditions(ExactSearchExecutionRequest request)
    {
        var query = request.CompiledSearch.NormalizedQuery;
        return query.FirstActUnknownEventPrefixCount > 0 || query.TreasureRelics is { IsEmpty: false } ||
            query.Players.Any(static player => player.Conditions.FirstActUnknownEventPrefixCount > 0 ||
                player.Conditions.TreasureRelics is { IsEmpty: false });
    }

    internal static bool TryOrdinaryUnknown(ExactSearchExecutionRequest request, out int prefix, out string cause)
    {
        prefix = Prefixes(request).Select(requirement => requirement.Count).DefaultIfEmpty(0).Max();
        var party = request.CompiledSearch.Context.Party;
        if (prefix == 0) { cause = "FirstActUnknownEventPrefix.NotRequested"; return false; }
        if (request.ProfileId != RuntimeProfileId.Beta111 || party is null || party.Players.Count is < 2 or > 4)
        { cause = "FirstActUnknownEventPrefix.PartyProfileUnsupported"; return false; }
        if (party.Players.Any(owner => !owner.IsBeta111Context || !owner.CanUseCurrentModel) ||
            party.World.Beta109Generation is not { HasExactPartyRoster: true, NoUnknownHooksOrModifiers: true })
        { cause = "FirstActUnknownEventPrefix.PartyOrdinaryAuthorityUnavailable"; return false; }
        var query = request.CompiledSearch.NormalizedQuery;
        if (query.Players.Any(player => player.SelectedOption is not null || player.Results.Count > 0) ||
            request.CompiledSearch.PlayerSearches.Any(compiled => ProductionQueryValidator.HasOpeningInitialRoomConditions(
                CompiledSearchEvaluationProjector.Project(compiled).Evaluation with { FirstActUnknownEventPrefixCount = 1 })))
        { cause = "FirstActUnknownEventPrefix.PartyOpeningInterventionUnresolved"; return false; }
        cause = ""; return true;
    }

    // Only an internal execution projection. The caller retains the original
    // request, fingerprint and all obligations for delivery and final combination.
    internal static ExactSearchExecutionRequest IndependentDomains(ExactSearchExecutionRequest request)
        => IndependentProjections.GetValue(request, static key => new Lazy<ExactSearchExecutionRequest>(
            () => CompileIndependentDomains(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static ExactSearchExecutionRequest CompileIndependentDomains(ExactSearchExecutionRequest request)
    {
        SearchQuery Strip(SearchQuery query) => query with { TreasureRelics = null, FirstActUnknownEventPrefixCount = 0 };
        var query = Strip(request.CompiledSearch.NormalizedQuery) with {
            Players = request.CompiledSearch.NormalizedQuery.Players.Select(player => player with { Conditions = Strip(player.Conditions) }).ToArray() };
        var projection = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, request.CompiledSearch.Context), request.RunOptions);
        return projection.Plan is { } plan ? plan with { PartyOpeningChoices = request.PartyOpeningChoices } :
            throw new InvalidOperationException("Party.InitialRoomsIndependentProjection:" + projection.Issue);
    }

    internal static SearchQueryEvaluation Evaluate(ExactSearchExecutionRequest request, ulong root)
    {
        var requirements = Prefixes(request);
        SearchQueryEvaluation unknown = SearchQueryEvaluation.Match();
        if (requirements.Length > 0)
        {
            if (!TryOrdinaryUnknown(request, out int count, out string cause)) unknown = cause == "FirstActUnknownEventPrefix.PartyProfileUnsupported"
                ? SearchQueryEvaluation.Unsupported(cause) : SearchQueryEvaluation.Unknown(cause);
            else
            {
                var sequence = OrdinaryUnknownRoomPredictor.ForAct1Filter(root, count, false);
                var failed = sequence.Steps.FirstOrDefault(step => step.Room != UnknownRoomCategory.Event);
                var evidence = requirements.Where(requirement => failed is null || requirement.Count >= failed.Ordinal).Select(requirement =>
                    new SearchMatchEvidence(requirement.Slot is int slot ? $"P{slot + 1}.FirstActUnknownEventPrefixMatched" : "FirstActUnknownEventPrefixMatched",
                        EvidenceCode: new(failed is null ? "search.initial-rooms.standard-multiplayer-shared" : "search.initial-rooms.shared-non-event"),
                        ProfileId: request.ProfileId, Act: 1, StreamDomain: "unknown_map_point",
                        AuthorityFingerprint: request.CompiledSearch.Context.Party!.Fingerprint,
                        Ordinal: failed?.Ordinal ?? requirement.Count, ConditionId: requirement.ConditionId)).ToArray();
                if (failed is null) unknown = SearchQueryEvaluation.Match(evidence);
                else unknown = SearchQueryEvaluation.NoMatch("FirstActUnknownEventPrefix.NonEventEncounter") with {
                    Evidence = evidence.Select(item => item with { Code = item.Code.Replace("Matched", "NonEventEncounter", StringComparison.Ordinal) }).ToArray() };
            }
        }
        // A shared Unknown mismatch can reject independently of the disabled
        // chest requirement; a partial success cannot confirm the conjunction.
        return ProductionExactSearchEvaluator.CombineIndependentEvaluations(unknown, HasTreasure(request)
            ? SearchQueryEvaluation.Unsupported("InitialRooms.SinglePlayerOnly") : SearchQueryEvaluation.Match());
    }
}
