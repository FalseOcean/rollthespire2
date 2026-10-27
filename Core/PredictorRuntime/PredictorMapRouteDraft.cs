using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorMapStrokeMode { Draw, Erase }
internal enum PredictorMapDraftEditResult { Invalid, NoChange, Changed }
internal readonly record struct PredictorMapTravelAllowance(int WingedBootsUsesLeft = 0, bool Flight = false);

/// <summary>
/// Session-local authored point selection. Compatibility can cross unselected
/// rows, but prediction receives only an explicitly selected consecutive prefix.
/// No pointer geometry, live game state, room simulation or persisted history.
/// </summary>
internal sealed class PredictorMapRouteDraft
{
    private sealed record Fragment(ImmutableArray<MapPosition> Points, int StartRow, long Revision);
    private sealed record EditState(ImmutableArray<MapPosition> Selected, ImmutableArray<Fragment> Fragments);
    private readonly Stack<EditState> _undo = new();
    private ImmutableArray<Fragment> _fragments = [];
    private long _revision;
    private Dictionary<MapPosition, MapPredictionPoint> _points = [];
    private Dictionary<int, MapPredictionPoint[]> _rows = [];

    internal MapPrediction Map { get; private set; }
    // The last factual point, excluded from the editable future selection.
    internal MapPosition? Previous { get; private set; }
    internal PredictorMapTravelAllowance Travel { get; private set; }
    internal ImmutableArray<MapPosition> Selected { get; private set; } = [];
    internal ImmutableArray<MapPosition> ReadyPrefix { get; private set; } = [];
    internal bool IsCompatible { get; private set; }
    internal bool CanUndo => _undo.Count > 0;

    internal PredictorMapRouteDraft(MapPrediction map, MapPosition? previous = null,
        PredictorMapTravelAllowance travel = default)
    {
        Map = Snapshot(map, previous, travel);
        Previous = previous;
        Travel = travel;
        IndexMap();
        SetSelection([]);
    }

    internal PredictorMapDraftEditResult ApplyStroke(IEnumerable<MapPosition> hits, PredictorMapStrokeMode mode)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (mode is not (PredictorMapStrokeMode.Draw or PredictorMapStrokeMode.Erase))
            return PredictorMapDraftEditResult.Invalid;
        var candidate = Selected.ToHashSet();
        foreach (var point in hits)
        {
            if (mode == PredictorMapStrokeMode.Erase) { candidate.Remove(point); continue; }
            // Drawing across the already traversed map cannot edit real facts.
            if (Previous is { } previous && point.Row <= previous.Row) continue;
            if (!_points.ContainsKey(point)) return PredictorMapDraftEditResult.Invalid;
            candidate.Add(point);
        }
        var ordered = Order(candidate);
        if (ordered.SequenceEqual(Selected)) return PredictorMapDraftEditResult.NoChange;
        // Validate the whole gesture before accepting any point or undo entry.
        if (mode == PredictorMapStrokeMode.Draw && !Compatible(ordered))
            return PredictorMapDraftEditResult.Invalid;
        _undo.Push(new(Selected, _fragments));
        _fragments = mode == PredictorMapStrokeMode.Erase ? RetainFragments(ordered.ToHashSet()) :
            [new(ordered, ordered[0].Row, ++_revision)];
        SetSelection(ordered);
        return PredictorMapDraftEditResult.Changed;
    }

    /// <summary>
    /// Merge one newly recognized native gesture, never accumulated drawings.
    /// Shared points split competing intent into sections: lower section starts
    /// win, with the latest gesture winning ties. Only authored points survive;
    /// reachability across a gap does not author its intervening route.
    /// </summary>
    internal PredictorMapDraftEditResult ApplyNativeStroke(IEnumerable<MapPosition> hits, PredictorMapStrokeMode mode)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (mode == PredictorMapStrokeMode.Erase)
        {
            var erased = Selected.Except(hits).ToImmutableArray();
            if (erased.SequenceEqual(Selected)) return PredictorMapDraftEditResult.NoChange;
            _undo.Clear();
            _fragments = RetainFragments(erased.ToHashSet());
            SetSelection(erased);
            return PredictorMapDraftEditResult.Changed;
        }
        if (mode != PredictorMapStrokeMode.Draw) return PredictorMapDraftEditResult.Invalid;
        var stroke = Order(hits.Where(p => Previous is not { } previous || p.Row > previous.Row).Distinct());
        if (stroke.IsEmpty) return PredictorMapDraftEditResult.NoChange;
        // Invalid or ambiguous new input has no authority over existing intent.
        if (!Compatible(stroke)) return PredictorMapDraftEditResult.Invalid;

        var incoming = new Fragment(stroke, stroke[0].Row, _revision + 1);
        var shared = Selected.Intersect(stroke).ToHashSet();
        var sections = _fragments.Append(incoming).SelectMany(f => SplitAtSharedPoints(f, shared))
            .OrderBy(f => f.StartRow).ThenByDescending(f => f.Revision);
        var accepted = new HashSet<MapPosition>();
        var fragments = ImmutableArray.CreateBuilder<Fragment>();
        foreach (var section in sections)
        {
            var points = ImmutableArray.CreateBuilder<MapPosition>();
            foreach (var point in section.Points)
            {
                // Keep one owner per coordinate, bounding saved provenance by
                // the map's point count even after many overlapping gestures.
                if (accepted.Contains(point)) continue;
                accepted.Add(point);
                if (Compatible(Order(accepted))) points.Add(point);
                else accepted.Remove(point);
            }
            if (points.Count > 0) fragments.Add(section with { Points = points.ToImmutable() });
        }
        var ordered = Order(accepted);
        if (ordered.SequenceEqual(Selected)) return PredictorMapDraftEditResult.NoChange;
        _undo.Clear();
        _fragments = fragments.ToImmutable();
        _revision++;
        SetSelection(ordered);
        return PredictorMapDraftEditResult.Changed;
    }

    private static IEnumerable<Fragment> SplitAtSharedPoints(Fragment fragment, HashSet<MapPosition> shared)
    {
        int first = 0, startRow = fragment.StartRow;
        for (int index = 0; index < fragment.Points.Length; index++)
        {
            if (!shared.Contains(fragment.Points[index])) continue;
            if (index > first)
                yield return new(fragment.Points[first..(index + 1)], startRow, fragment.Revision);
            first = index;
            startRow = fragment.Points[index].Row;
        }
        yield return new(fragment.Points[first..], startRow, fragment.Revision);
    }

    private ImmutableArray<Fragment> RetainFragments(HashSet<MapPosition> retained) => _fragments
        .Select(f => f with { Points = f.Points.Where(retained.Contains).ToImmutableArray() })
        .Where(f => !f.Points.IsEmpty).ToImmutableArray();

    internal PredictorMapDraftEditResult Clear()
    {
        if (Selected.IsEmpty) return PredictorMapDraftEditResult.NoChange;
        _undo.Push(new(Selected, _fragments));
        _fragments = [];
        SetSelection([]);
        return PredictorMapDraftEditResult.Changed;
    }

    /// <summary>Native clear establishes an empty, non-undoable session boundary.</summary>
    internal PredictorMapDraftEditResult Reset()
    {
        bool changed = !Selected.IsEmpty;
        _undo.Clear();
        _fragments = [];
        SetSelection([]);
        return changed ? PredictorMapDraftEditResult.Changed : PredictorMapDraftEditResult.NoChange;
    }

    internal bool Undo()
    {
        if (!_undo.TryPop(out var state)) return false;
        _fragments = state.Fragments;
        SetSelection(state.Selected);
        return true;
    }

    /// <summary>
    /// New actual-progress boundary: forget edit history, remove past or absent
    /// coordinates, and retain all other authored intent. A replacement can make
    /// that intent incompatible; the caller can display this until explicit edits.
    /// Call at a changed factual boundary, not for ordinary observation/reopening.
    /// </summary>
    internal ImmutableArray<MapPosition> Rebase(MapPrediction map, MapPosition? previous,
        PredictorMapTravelAllowance travel = default)
    {
        var snapshot = Snapshot(map, previous, travel);
        bool sameAct = snapshot.Seed == Map.Seed && snapshot.Act == Map.Act && snapshot.ActIndex == Map.ActIndex;
        var present = snapshot.Points.Select(p => p.Position).ToHashSet();
        var retained = Selected.Where(p => sameAct && present.Contains(p) &&
            (previous == null || p.Row > previous.Value.Row)).ToImmutableArray();
        var removed = Selected.Except(retained).ToImmutableArray();
        Map = snapshot;
        Previous = previous;
        Travel = travel;
        IndexMap();
        _undo.Clear();
        _fragments = RetainFragments(retained.ToHashSet()).Select(f =>
            f with { StartRow = Math.Max(f.StartRow, previous is { } boundary ? boundary.Row + 1 : 0) })
            .ToImmutableArray();
        SetSelection(retained);
        return removed;
    }

    private void IndexMap()
    {
        _points = Map.Points.ToDictionary(p => p.Position);
        _rows = Map.Points.GroupBy(p => p.Position.Row).ToDictionary(g => g.Key, g => g.ToArray());
    }

    private void SetSelection(ImmutableArray<MapPosition> selected)
    {
        Selected = selected;
        IsCompatible = Compatible(selected);
        if (!IsCompatible) { ReadyPrefix = []; return; }
        int row = Previous is { } previous ? previous.Row + 1 : 0;
        var prefix = ImmutableArray.CreateBuilder<MapPosition>();
        foreach (var point in selected)
        {
            if (point.Row != row) break;
            prefix.Add(point);
            row++;
        }
        ReadyPrefix = prefix.ToImmutable();
    }

    private bool Compatible(ImmutableArray<MapPosition> selected)
    {
        if (selected.Any(p => !_points.ContainsKey(p) || Previous is { } previous && p.Row <= previous.Row) ||
            selected.GroupBy(p => p.Row).Any(g => g.Count() > 1)) return false;
        var required = selected.ToDictionary(p => p.Row);
        int lastRequiredRow = selected.IsEmpty ? -1 : selected[^1].Row;
        var costs = new Dictionary<MapPosition, int>();
        const int unreachable = int.MaxValue / 2;
        int RemainingCost(MapPosition position)
        {
            if (costs.TryGetValue(position, out int saved)) return saved;
            var point = _points[position];
            if (required.TryGetValue(position.Row, out var chosen) && chosen != position)
                return costs[position] = unreachable;
            if (point.Type == MapPointType.Boss && point.Children.Count == 0)
                return costs[position] = position.Row >= lastRequiredRow ? 0 : unreachable;
            int best = unreachable;
            if (_rows.TryGetValue(position.Row + 1, out var next))
                foreach (var target in next)
                {
                    // Mirrors PredictorRun.IsLegalRoute's future travel rules.
                    // All non-edges consume a Boots use; Flight permits them even
                    // after Boots are exhausted, so it has zero admission cost.
                    bool edge = point.Children.Contains(target.Position);
                    if (!edge && !Travel.Flight && Travel.WingedBootsUsesLeft == 0) continue;
                    int cost = RemainingCost(target.Position);
                    if (cost < unreachable) best = Math.Min(best, cost + (!edge && !Travel.Flight ? 1 : 0));
                }
            return costs[position] = best;
        }
        int minimum = Previous is { } start ? RemainingCost(start) : RemainingCost(_rows[0][0].Position);
        return minimum < unreachable && minimum <= Travel.WingedBootsUsesLeft;
    }

    private static ImmutableArray<MapPosition> Order(IEnumerable<MapPosition> positions) =>
        positions.OrderBy(p => p.Row).ThenBy(p => p.Column).ToImmutableArray();

    private static MapPrediction Snapshot(MapPrediction map, MapPosition? previous, PredictorMapTravelAllowance travel)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (travel.WingedBootsUsesLeft is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(travel));
        var points = map.Points.Select(p => p with { Children = Array.AsReadOnly(p.Children.ToArray()) }).ToArray();
        var positions = points.Select(p => p.Position).ToHashSet();
        if (positions.Count != points.Length || points.Count(p => p.Position.Row == 0) != 1 ||
            points.Any(p => p.Position.Row < 0 || p.Children.Any(c => !positions.Contains(c) || c.Row <= p.Position.Row)) ||
            previous is { } start && !positions.Contains(start))
            throw new ArgumentException("Invalid draft map or factual starting point.", nameof(map));
        return map with { Points = Array.AsReadOnly(points) };
    }
}
