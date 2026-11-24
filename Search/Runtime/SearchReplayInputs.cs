using System.Runtime.CompilerServices;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

// A conditional replay belongs only to the final candidate check. Keep the original
// compiled input for filtering, continuation identity and authoritative validation.
internal sealed record SearchReplayInputs(SearchModel? Model, string Assumptions)
{
    private static readonly ConditionalWeakTable<SearchModel, SearchReplayInputs> Cache = new();
    internal static SearchReplayInputs For(SearchModel model) => Cache.GetValue(model, Prepare);

    private static SearchReplayInputs Prepare(SearchModel model)
    {
        var context = model.CompiledSearch.Context;
        var reasons = new List<string>();
        RuntimeContextAuthoritySnapshot ForAuthority(RuntimeContextAuthoritySnapshot source,
            params (ExactSearchEvaluationProjection Evaluation, SearchQuery Query)[] requests)
        {
            var domains = requests.Aggregate(SeedPredictionDomainSelection.None,
                (combined, request) => combined | RequiredDomains(request.Evaluation, request.Query));
            if (!SeedModelAssumptions.TryCreate(source, out var modeled, out var assumptions, domains)) return source;
            var applicable = assumptions.SelectMany(pair => pair.Value.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Where(reason => requests.Any(request => (RequiredDomains(request.Evaluation, request.Query) & pair.Key) != 0 &&
                    UsesRule(request.Evaluation, reason)))).Distinct(StringComparer.Ordinal).ToArray();
            if (applicable.Length == 0) return source;
            reasons.AddRange(applicable);
            return modeled;
        }

        var shared = (model.Evaluation, model.CompiledSearch.NormalizedQuery);
        CompiledSearch compiled;
        if (context.Party is { } party)
        {
            var members = party.Players.Select((source, index) =>
            {
                var child = model.CompiledSearch.PlayerSearches.ElementAtOrDefault(index);
                return child is null ? ForAuthority(source, shared) : ForAuthority(source, shared,
                    (CompiledSearchEvaluationProjector.Project(child).Evaluation, child.NormalizedQuery));
            }).ToArray();
            if (reasons.Count == 0) return new(null, "");
            var modeledParty = new OrderedPartyAuthority(members, party.UnlockProvenance,
                members[0].WorldAuthority ?? party.World, party.UnlockSources);
            compiled = SearchCompiler.Compile(model.CompiledSearch.Query,
                context with { Party = modeledParty, Authority = members[0] });
        }
        else
        {
            var modeled = ForAuthority(context.Authority, shared);
            if (reasons.Count == 0) return new(null, "");
            compiled = model.CompiledSearch with { Context = context with { Authority = modeled } };
        }
        return new(model with { CompiledSearch = compiled, Evaluation = CompiledSearchEvaluationProjector.Project(compiled).Evaluation,
                RewardPoolPreparation = null, VariantOpening = null },
            string.Join(" | ", reasons.Distinct(StringComparer.Ordinal)));
    }

    private static bool UsesRule(ExactSearchEvaluationProjection evaluation, string reason)
    {
        if (reason == "NoAdditionalEventRemovals") return evaluation.RequiresEventSequenceDomain;
        const string generator = "StandardAncientOptionGenerator:";
        if (!reason.StartsWith(generator, StringComparison.Ordinal)) return true;
        string entry = reason[generator.Length..];
        return evaluation.AncientOptionFilters.Count != 0 || evaluation.AncientSeaGlassTargetFilters.Count != 0 ||
            evaluation.TransformationAggregate is { UsesAncient: true } ||
            evaluation.AncientBranchConditions.Any(row => row.AncientKey.Entry == entry &&
                (row.OptionAny.Count != 0 || row.SeaGlassTargetAny.Count != 0));
    }

    private static SeedPredictionDomainSelection RequiredDomains(ExactSearchEvaluationProjection evaluation, SearchQuery query)
    {
        var domains = evaluation.CheapPredictionDomains;
        if (evaluation.RequiresNormalCombatRewardDomain) domains |= SeedPredictionDomainSelection.NormalCombatRewards;
        if (query.MerchantRelicConditions.Count > 0 || SearchAuthorityProjector.RequiresPerSeedRelicBagProjection(evaluation))
            domains |= SeedPredictionDomainSelection.RelicSequence;
        if (evaluation.TransformationAggregate is { UsesAncient: true }) domains |= SeedPredictionDomainSelection.World;
        if (evaluation.RequiresMerchantColorlessDomain) domains |= SeedPredictionDomainSelection.MerchantColorless;
        return domains;
    }
}
