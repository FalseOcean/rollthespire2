using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Producers supply only complete, reachable orders for one result at one depth.
// This formats those facts; it never infers execution or cursor equivalence.
internal static class OperationIconPaths
{
    internal static ImmutableArray<OperationIconPath> Compress(List<ImmutableArray<ModelKey>> orders,
        CancellationToken token)
    {
        var paths = ImmutableArray.CreateBuilder<OperationIconPath>();
        var covered = new HashSet<string>();
        string Key(IEnumerable<ModelKey> order) => string.Join("/", order.Select(k => k.Serialized));
        var available = orders.Select(order => Key(order)).ToHashSet();
        foreach (var order in orders)
        {
            token.ThrowIfCancellationRequested();
            if (covered.Contains(Key(order))) continue;
            var segments = order.Select(icon => ImmutableArray.Create(icon)).ToImmutableArray();
            // Prove every distinct Cartesian expansion with this exact prefix
            // and suffix. The final operation remains the hovered target.
            for (int width = order.Length - 1; width >= 2; width--)
                for (int start = 0; start + width < segments.Length; start++)
                {
                    var merged = segments.Skip(start).Take(width).SelectMany(s => s).ToImmutableArray();
                    if (merged.Distinct().Count() < 2) continue;
                    var proposed = segments.Take(start).Append(merged).Concat(segments.Skip(start + width)).ToImmutableArray();
                    if (ExpandSegments(proposed, token).All(p => available.Contains(Key(p))))
                    { segments = proposed; break; }
                }
            foreach (var expanded in ExpandSegments(segments, token)) covered.Add(Key(expanded));
            paths.Add(new(segments));
        }
        return paths.ToImmutable();
    }

    private static IEnumerable<ImmutableArray<ModelKey>> ExpandSegments(ImmutableArray<ImmutableArray<ModelKey>> segments,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (segments.IsEmpty) { yield return []; yield break; }
        foreach (var permutation in IconPermutations(segments[0], token))
            foreach (var tail in ExpandSegments(segments.RemoveAt(0), token))
                yield return permutation.AddRange(tail);
    }

    private static IEnumerable<ImmutableArray<ModelKey>> IconPermutations(ImmutableArray<ModelKey> icons,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (icons.IsEmpty) { yield return []; yield break; }
        foreach (var icon in icons.Distinct())
            foreach (var tail in IconPermutations(icons.Remove(icon), token))
                yield return tail.Insert(0, icon);
    }
}
