using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

internal static class QueryKeySetPredicate
{

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
