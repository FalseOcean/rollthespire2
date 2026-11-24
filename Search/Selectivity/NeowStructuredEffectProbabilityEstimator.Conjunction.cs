using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Selectivity;

internal static partial class NeowStructuredEffectProbabilityEstimator
{
    // Repeated predicates observe the same output, not independent redraws. This
    // is local probability normalization; Query and Exact retain their predicates.
    private static bool TryConjoinOutputConditions(NeowStructuredEffectSearchCondition[] input,
        out NeowStructuredEffectSearchCondition[] output)
    {
        var result = new List<NeowStructuredEffectSearchCondition>();
        foreach (var group in input.DistinctBy(c => JsonSerializer.Serialize(c))
                     .GroupBy(c => (c.SourceRelicKey, c.Scope, c.OutputKind)))
        {
            var rows = group.ToArray(); var first = rows[0];
            if (rows.Length == 1 || rows.Any(NeowReplayPlan.IsCapsule))
            { result.AddRange(rows); continue; }
            var requirements = rows.SelectMany(c => c.OutputKeys.GroupBy(k => k)
                    .Select(g => (Key: g.Key, Count: g.Count())))
                .GroupBy(x => x.Key).SelectMany(g => Enumerable.Repeat(g.Key, g.Max(x => x.Count))).ToArray();
            if (first.SourceRelicKey == BaseGameModelKeys.Relics.Kaleidoscope)
            {
                if (requirements.Length > 2) { output = []; return false; }
                var ordered = rows.Where(c => c.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder).ToArray();
                if (ordered.Length > 0)
                {
                    var slots = ordered[0].KaleidoscopePositionalSlots;
                    if (ordered.Any(c => !c.KaleidoscopePositionalSlots.SequenceEqual(slots)) ||
                        !SameMultiset(requirements, slots.Where(k => k.HasValue).Select(k => k!.Value)))
                    { output = []; return false; }
                    result.Add(ordered[0] with { OutputKeys = requirements });
                }
                else result.Add(first with { OutputKeys = requirements });
            }
            else if (first.SourceRelicKey == BaseGameModelKeys.Relics.ScrollBoxes)
            {
                bool special = rows.Any(c => c.Kind == NeowStructuredConditionKind.SpecialOffer);
                if (special && rows.Any(c => c.Kind != NeowStructuredConditionKind.SpecialOffer) ||
                    !special && (requirements.Length > 3 || requirements.Distinct().Count() != requirements.Length))
                { output = []; return false; }
                result.Add(special ? rows.First(c => c.Kind == NeowStructuredConditionKind.SpecialOffer) : first with { OutputKeys = requirements });
            }
            else
            {
                int capacity = first.Kind == NeowStructuredConditionKind.ExactUnorderedPair ||
                    first.Scope == NeowStructuredEffectScope.BonesOfferedRelics ? 2 : 1;
                if (requirements.Length > capacity) { output = []; return false; }
                result.Add(first with { OutputKeys = requirements });
            }
        }
        output = result.ToArray(); return true;
    }

    private static bool SameMultiset(IEnumerable<ModelKey> a, IEnumerable<ModelKey> b) =>
        a.OrderBy(k => k.Serialized, StringComparer.Ordinal).SequenceEqual(b.OrderBy(k => k.Serialized, StringComparer.Ordinal));
}
