using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class PartyQueryProbability
{
    private static JointSelectivityResult EstimatePersonalOffers(CompiledSearch compiled, PlayerOfferQuery player, SearchQuery query)
    {
        // Act 1's visible Ancient options are the same three initial Neow offers.
        // Preserve each Any clause: (A or B) AND (C or D) is not one union.
        var filters = new[] { query.LegacyNeow.NeowRelics, player.Offers }
            .Concat(player.Conditions.LegacyWorld.AncientOptionFilters.Where(f => f.Act == 1).Select(f => f.Keys)).ToArray();
        var all = filters.SelectMany(f => f.All).Distinct().ToArray();
        var banned = filters.SelectMany(f => f.Ban).ToHashSet();
        JointSelectivityResult Impossible() => JointSelectivityResult.Exact(0, JointSelectivityCombinationMethod.ExactImpossible,
            "Probability.Party.InitialOfferConflict", "Initial offer clauses cannot hold together.", "P=0");
        if (all.Length > 3 || all.Any(banned.Contains)) return Impossible();
        var groups = new List<ModelKey[]>();
        foreach (var filter in filters.Where(f => f.Any.Count > 0 && !f.Any.Any(all.Contains)))
        {
            var group = filter.Any.Where(k => !banned.Contains(k)).Distinct().ToArray();
            if (group.Length == 0) return Impossible();
            if (groups.Any(g => g.All(group.Contains))) continue;
            groups.RemoveAll(g => group.All(g.Contains));
            groups.Add(group);
        }
        JointSelectivityResult Evaluate(IReadOnlyList<ModelKey> any, IEnumerable<ModelKey> ban)
        {
            var local = query with { LegacyNeow = query.LegacyNeow with { NeowRelics = new(any, all, ban.Distinct().ToArray()) } };
            var input = SearchSelectivityInput.From(SearchCompiler.CompilePlayer(local, compiled.PlayerSearches[player.Slot].Context));
            return JointSelectivityEstimator.EstimateQuery(input);
        }
        if (groups.Count <= 1) return Evaluate(groups.Count == 1 ? groups[0] : [], banned);

        // Finite inclusion/exclusion calls the whole personal model in each term,
        // retaining its route/result/relic dependencies rather than dividing marginals.
        double sum = 0, compensation = 0;
        var assumptions = new HashSet<string>();
        var missing = new HashSet<string>();
        JointSelectivityResult? baseline = null;
        void Visit(int index, HashSet<ModelKey> excluded, int sign)
        {
            if (all.Any(excluded.Contains)) return;
            if (index < groups.Count)
            {
                Visit(index + 1, excluded, sign);
                var next = new HashSet<ModelKey>(excluded); next.UnionWith(groups[index]);
                Visit(index + 1, next, -sign);
                return;
            }
            var result = Evaluate([], excluded);
            baseline ??= result;
            assumptions.UnionWith(result.Assumptions);
            if (result.Probability is not { } p)
            { missing.UnionWith(result.UnknownComponents.DefaultIfEmpty(result.EvidenceCode)); return; }
            double term = sign * p - compensation, updated = sum + term;
            compensation = (updated - sum) - term; sum = updated;
        }
        Visit(0, banned, 1);
        if (missing.Count > 0) return JointSelectivityResult.Partial("Probability.Party.InitialOfferConjunctionPartial",
            "A personal conditional term lacks its probability authority.", "Inclusion/exclusion of complete personal-query terms",
            [], missing.ToArray(), assumptions: assumptions.ToArray());
        return JointSelectivityResult.Exact(Math.Clamp(sum, 0, 1), JointSelectivityCombinationMethod.FiniteMixture,
            "Probability.Party.InitialOfferConjunction", "Every initial offer Any clause is required on the same three offers.",
            "Sum (-1)^|S| P(personal query and none of the Any groups in S)",
            components: baseline?.KnownComponents, assumptions: assumptions.ToArray())
            with { Confidence = baseline?.Confidence ?? SearchSelectivityConfidence.Medium };
    }

}
