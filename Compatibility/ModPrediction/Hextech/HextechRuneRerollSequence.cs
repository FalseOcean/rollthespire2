using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

// Result is representative only: all Orders share the displayed final hovered
// identity/outcome, while other slots and all continuation states may differ.
internal sealed record HextechRuneRerollSequenceGroup(int Depth, HextechRuneRerollResult Result,
    ImmutableArray<ImmutableArray<int>> Orders);
internal sealed record HextechRuneRerollSequenceResult(ImmutableArray<HextechRuneRerollSequenceGroup> Groups,
    bool Truncated, bool Unknown);

internal static class HextechRuneRerollSequence
{
    private sealed record Prefix(HextechRuneRerollState State, ImmutableArray<int> Order);
    internal static HextechRuneRerollSequenceResult Predict(HextechRuneRerollSnapshot snapshot,
        CancellationToken token = default, int maxDepth = 5, int maxRows = 8)
    {
        token.ThrowIfCancellationRequested();
        var initial = snapshot.FullState ?? throw new InvalidOperationException("HextechRuneRerollFullStateMissing");
        var groups = ImmutableArray.CreateBuilder<HextechRuneRerollSequenceGroup>();
        var prefixes = new List<Prefix> { new(initial, []) };
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            token.ThrowIfCancellationRequested();
            var outcomes = new List<(ImmutableArray<int> Order, HextechRuneRerollResult Result)>();
            bool unknown = false;
            foreach (var prefix in prefixes)
            {
                token.ThrowIfCancellationRequested();
                if (!HextechRuneRerollPrediction.IsLegal(snapshot, prefix.State, snapshot.Slot)) continue;
                var step = HextechRuneRerollPrediction.Step(snapshot, prefix.State, snapshot.Slot, token);
                outcomes.Add((prefix.Order.Add(snapshot.Slot), step.Result));
                unknown |= step.Result.Outcome == HextechRuneRerollOutcome.Unknown;
            }
            if (outcomes.Count == 0) return new(groups.ToImmutable(), false, false);
            var level = outcomes.GroupBy(value => (Target: value.Result.NextPanel.IsEmpty ? (ModelKey?)null : value.Result.NextPanel[snapshot.Slot],
                    value.Result.Outcome, value.Result.Issue))
                .Select(group => new HextechRuneRerollSequenceGroup(depth, group.First().Result, group.Select(value => value.Order).ToImmutableArray())).ToArray();
            if (groups.Count + level.Length > maxRows) return new(groups.ToImmutable(), true, unknown);
            groups.AddRange(level);
            if (unknown) return new(groups.ToImmutable(), false, true);
            var following = new List<Prefix>();
            bool prefixUnknown = false;
            foreach (var prefix in prefixes)
            for (int slot = 0; slot < prefix.State.Panel.Length; slot++)
            {
                token.ThrowIfCancellationRequested();
                if (!HextechRuneRerollPrediction.IsLegal(snapshot, prefix.State, slot)) continue;
                var step = HextechRuneRerollPrediction.Step(snapshot, prefix.State, slot, token);
                // An unknown prefix cannot establish whether a following click
                // is legal. Keep earlier complete depths and stop this expansion.
                if (step.Result.Outcome == HextechRuneRerollOutcome.Unknown)
                { prefixUnknown = true; continue; }
                following.Add(new(step.State, prefix.Order.Add(slot)));
            }
            if (depth == maxDepth)
                return new(groups.ToImmutable(), following.Any(prefix =>
                    HextechRuneRerollPrediction.IsLegal(snapshot, prefix.State, snapshot.Slot)), prefixUnknown);
            if (prefixUnknown) return new(groups.ToImmutable(), false, true);
            prefixes = following;
            if (prefixes.Count == 0) break;
        }
        return new(groups.ToImmutable(), false, false);
    }
}
