namespace RolltheSpire2.Search.FamilyExecution;

internal static class SharedTransformationDrawRanks
{
    // One uniform mantissa selects a slot in each route's ordered pool. A rank
    // belongs to exactly one intersection, including when pool lengths differ.
    internal static Dictionary<(T First, T Second), ulong> CountPairs<T>(
        IReadOnlyList<T> first, IReadOnlyList<T> second) where T : notnull
    {
        if (first.Count == 0 || second.Count == 0)
            throw new ArgumentException("Both transformation draws require a nonempty pool.");
        var result = new Dictionary<(T First, T Second), ulong>();
        ulong start = 0;
        // Equal lengths have the same boundaries at every slot. This includes
        // a fixed/equivalent route and distinct pools observed at the same rank.
        if (first.Count == second.Count)
        {
            for (int rank = 0; rank < first.Count; rank++)
            {
                ulong end = NeowStaticDrawGateCompiler.Binary64Boundary(rank + 1, first.Count);
                var pair = (first[rank], second[rank]);
                result[pair] = result.GetValueOrDefault(pair) + (end - start);
                start = end;
            }
            return result;
        }
        int i = 0, j = 0;
        ulong firstEnd = NeowStaticDrawGateCompiler.Binary64Boundary(1, first.Count);
        ulong secondEnd = NeowStaticDrawGateCompiler.Binary64Boundary(1, second.Count);
        while (i < first.Count && j < second.Count)
        {
            ulong end = Math.Min(firstEnd, secondEnd);
            var pair = (first[i], second[j]);
            result[pair] = result.GetValueOrDefault(pair) + (end - start);
            start = end;
            if (end == firstEnd)
            {
                i++;
                if (i < first.Count) firstEnd = NeowStaticDrawGateCompiler.Binary64Boundary(i + 1, first.Count);
            }
            if (end == secondEnd)
            {
                j++;
                if (j < second.Count) secondEnd = NeowStaticDrawGateCompiler.Binary64Boundary(j + 1, second.Count);
            }
        }
        return result;
    }
}
