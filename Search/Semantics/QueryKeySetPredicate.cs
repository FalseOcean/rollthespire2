using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

internal static class QueryKeySetPredicate
{

    internal static SearchDisposition MatchVisibleAncientOptions(AncientOptionsEvaluationStatus status,
        IReadOnlyList<AncientOptionPredictionResult> options, ModelKeySetFilter filter)
    {
        bool partial = status == AncientOptionsEvaluationStatus.EvaluatedPartial;
        if (!partial && status is not (AncientOptionsEvaluationStatus.EvaluatedNonEmpty or
            AncientOptionsEvaluationStatus.EvaluatedKnownEmpty or AncientOptionsEvaluationStatus.EvaluatedLocked))
            return SearchDisposition.Unknown;
        var visible = options.Where(o => o.IsVisible).ToArray();
        var known = visible.Where(o => o.OptionPrecision == PredictionPrecision.Exact).Select(o => o.OptionKey).ToHashSet();
        var uncertain = visible.Where(o => o.OptionPrecision != PredictionPrecision.Exact).ToArray();
        bool Possible(ModelKey key) => known.Contains(key) || partial && options.Count < 3 ||
            uncertain.Any(o => o.PossibleOptionKeys?.Contains(key) != false);
        if (filter.Ban.Any(known.Contains) || filter.All.Any(key => !Possible(key)) ||
            filter.Any.Count > 0 && !filter.Any.Any(Possible)) return SearchDisposition.NoMatch;
        return filter.All.All(known.Contains) && (filter.Any.Count == 0 || filter.Any.Any(known.Contains)) &&
            !filter.Ban.Any(Possible) ? SearchDisposition.Match : SearchDisposition.Unknown;
    }

    internal static SearchDisposition MatchActBosses(IReadOnlyList<BossPredictionResult> bosses, int act,
        int ascension, ModelKeySetFilter filter)
    {
        var usable = bosses.Where(b => b.Act == act &&
            (b.IdentityPrecision == PredictionPrecision.Exact || b.SearchIdentityReplayable)).ToArray();
        var keys = usable.Select(b => b.BossKey).ToHashSet();
        if (filter.Ban.Any(keys.Contains)) return SearchDisposition.NoMatch;
        bool matches = MatchesKeySet(keys, filter);
        bool complete = usable.Any(b => b.Ordinal == 1) &&
            (act != 3 || ascension < 10 || usable.Any(b => b.Ordinal == 2));
        if (complete) return matches ? SearchDisposition.Match : SearchDisposition.NoMatch;
        return matches && filter.Ban.Count == 0 ? SearchDisposition.Match : SearchDisposition.Unknown;
    }

    internal static bool MatchesKeySet(IEnumerable<ModelKey> source, ModelKeySetFilter filter)
    {
        var set = new HashSet<ModelKey>(source, ModelKeyComparer.Instance);
        if (filter.Any.Count > 0 && !filter.Any.Any(set.Contains))
        {
            return false;
        }
        if (filter.All.Any(required => !set.Contains(required)))
        {
            return false;
        }
        if (filter.Ban.Any(set.Contains))
        {
            return false;
        }
        return true;
    }
}
