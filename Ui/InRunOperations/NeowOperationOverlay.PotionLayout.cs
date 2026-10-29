using Godot;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private readonly record struct PotionGroupLayoutKey(int Count, string Caption, bool Stacked, bool SingleRow, bool HasPrice, string PotionUseOrder);
    private sealed record PotionGroupLayoutCache(Rect2 Viewport, Rect2 Anchor, Rect2? NativeTip,
        Rect2[] Occupied, PotionGroupLayoutKey[] Groups, float Scale, float Factor, Rect2[]? Regions);
    // One current UI layout only, including an impossible-layout decision.
    // Rectangles and metric inputs are copied values, never native controls.
    private PotionGroupLayoutCache? _potionGroupLayoutCache;
    private float _potionGroupScale = 1f;

    // A centered reward can divide the usable screen into several regions.
    // Prefer one complete horizontal row, uniformly shrinking cards as needed.
    // Only use multiple rows if no single-row plan fits around native controls.
    // A null result explicitly means no attempted plan can fit.
    internal Rect2[]? PlanPotionGroupRegions(Rect2 viewport, Rect2 anchor,
        IReadOnlyList<Rect2> occupied, Rect2? nativeTextTip)
    {
        if (_preview?.Source.Category != "POTION" || _stacks.Count < 2 || _iconGroups.Count != 0 || _summary != null)
        { _potionGroupScale = 1f; return null; }
        for (int i = 0; i < _stacks.Count; i++) if (!_stacks[i].Stacked) { _potionGroupScale = 1f; return null; }
        // CardDisplayScale includes the previously selected factor. Cache and
        // plan against the native viewport scale, preventing cumulative shrink.
        float scale = CardDisplayScale / _potionGroupScale;
        if (PotionGroupLayoutMatches(viewport, anchor, occupied, nativeTextTip, scale))
        { _potionGroupScale = _potionGroupLayoutCache!.Factor; return _potionGroupLayoutCache.Regions; }
        return RecomputePotionGroupRegions(viewport, anchor, occupied, nativeTextTip, scale);
    }

    private static PotionGroupLayoutKey PotionGroupKey(CardStack stack) => new(stack.Cards.Length, stack.Caption,
        stack.Stacked, stack.ForceSingleRow, stack.Price.HasValue,
        stack.PotionUseOrder.IsDefaultOrEmpty ? "" : string.Join("|", stack.PotionUseOrder.Select(key => key.Serialized)));

    private bool PotionGroupLayoutMatches(Rect2 viewport, Rect2 anchor, IReadOnlyList<Rect2> occupied,
        Rect2? nativeTextTip, float scale)
    {
        var cache = _potionGroupLayoutCache;
        if (cache == null || cache.Viewport != viewport || cache.Anchor != anchor || cache.NativeTip != nativeTextTip ||
            Math.Abs(cache.Scale - scale) > .00001f || cache.Occupied.Length != occupied.Count || cache.Groups.Length != _stacks.Count) return false;
        for (int i = 0; i < occupied.Count; i++) if (cache.Occupied[i] != occupied[i]) return false;
        for (int i = 0; i < _stacks.Count; i++) if (cache.Groups[i] != PotionGroupKey(_stacks[i])) return false;
        return true;
    }

    private Rect2[]? RecomputePotionGroupRegions(Rect2 viewport, Rect2 anchor, IReadOnlyList<Rect2> occupied,
        Rect2? nativeTextTip, float scale)
    {
        var groups = _stacks.Select(PotionGroupKey).ToArray();
        var copiedOccupied = occupied.ToArray();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            _potionGroupScale = (20 - attempt) / 20f;
            var regions = TrySingleRow(scale * _potionGroupScale);
            if (regions != null) return Remember(regions);
        }
        for (int attempt = 0; attempt < 20; attempt++)
        {
            _potionGroupScale = (20 - attempt) / 20f;
            var regions = TryAtScale(scale * _potionGroupScale);
            if (regions != null) return Remember(regions);
        }
        _potionGroupScale = 1f;
        return Remember(null);

        Rect2[]? TrySingleRow(float cardScale)
        {
            var sizes = _stacks.Select(stack => GroupMetrics(stack, float.PositiveInfinity, cardScale).Size).ToArray();
            var rowSize = new Vector2(sizes.Sum(size => size.X) + GroupGap * (sizes.Length - 1),
                sizes.Max(size => size.Y));
            var safe = new Rect2(viewport.Position + new Vector2(18, 18), viewport.Size - new Vector2(36, 36));
            if (rowSize.X > safe.Size.X || rowSize.Y > safe.Size.Y) return null;
            var blockers = occupied.Append(anchor).Concat(nativeTextTip is { } tip ? [tip] : Array.Empty<Rect2>())
                .Where(rect => rect.Size.X > 0 && rect.Size.Y > 0).Select(rect => rect.Grow(12))
                .Where(rect => rect.Intersects(safe)).Distinct().ToArray();
            var xs = blockers.SelectMany(rect => new[] { rect.Position.X - rowSize.X, rect.End.X })
                .Append(anchor.GetCenter().X - rowSize.X / 2).Append(safe.Position.X).Append(safe.End.X - rowSize.X)
                .Select(x => Math.Clamp(x, safe.Position.X, safe.End.X - rowSize.X)).Distinct();
            var ys = blockers.SelectMany(rect => new[] { rect.Position.Y - rowSize.Y, rect.End.Y })
                .Append(safe.Position.Y).Append(safe.End.Y - rowSize.Y)
                .Select(y => Math.Clamp(y, safe.Position.Y, safe.End.Y - rowSize.Y)).Distinct().ToArray();
            Rect2? best = null;
            float bestScore = float.PositiveInfinity;
            foreach (float x in xs)
            foreach (float y in ys)
            {
                var row = new Rect2(new Vector2(x, y), rowSize);
                if (blockers.Any(blocker => blocker.Intersects(row))) continue;
                float score = row.GetCenter().DistanceSquaredTo(anchor.GetCenter());
                if (score >= bestScore) continue;
                best = row; bestScore = score;
            }
            if (best is not { } placement) return null;
            float left = placement.Position.X;
            return sizes.Select(size =>
            {
                var rect = new Rect2(new Vector2(left, placement.Position.Y), size);
                left += size.X + GroupGap;
                return rect;
            }).ToArray();
        }

        Rect2[]? TryAtScale(float cardScale)
        {
            var sizes = _stacks.Select(stack => GroupMetrics(stack, float.PositiveInfinity, cardScale).Size).ToArray();
            var cell = new Vector2(sizes.Max(size => size.X), sizes.Max(size => size.Y));
            var safe = new Rect2(viewport.Position + new Vector2(18, 18), viewport.Size - new Vector2(36, 36));
            if (cell.X <= 0 || cell.Y <= 0 || cell.X > safe.Size.X || cell.Y > safe.Size.Y) return null;
            var blockers = occupied.Append(anchor).Concat(nativeTextTip is { } tip ? [tip] : Array.Empty<Rect2>())
                .Where(rect => rect.Size.X > 0 && rect.Size.Y > 0).Select(rect => rect.Grow(12))
                .Where(rect => rect.Intersects(safe)).Distinct().ToArray();
            var step = cell + new Vector2(GroupGap, GroupGap);
            var xs = Origins(safe.Position.X, safe.End.X, cell.X, step.X,
                blockers.SelectMany(rect => new[] { rect.Position.X - cell.X, rect.End.X }));
            var ys = Origins(safe.Position.Y, safe.End.Y, cell.Y, step.Y,
                blockers.SelectMany(rect => new[] { rect.Position.Y - cell.Y, rect.End.Y }));
            Rect2[]? best = null;
            float bestScore = float.PositiveInfinity;
            foreach (float y in ys)
            foreach (float x in xs)
            {
                var available = new List<Rect2>();
                for (float row = y; row + cell.Y <= safe.End.Y + .01f; row += step.Y)
                for (float column = x; column + cell.X <= safe.End.X + .01f; column += step.X)
                {
                    var rect = new Rect2(new Vector2(column, row), cell);
                    if (!blockers.Any(blocker => blocker.Intersects(rect))) available.Add(rect);
                }
                if (available.Count < sizes.Length) continue;
                // Prefer nearby cells, then restore visual reading order so group
                // identities and ordinal captions never depend on hole placement.
                var selected = available.OrderBy(rect => rect.GetCenter().DistanceSquaredTo(anchor.GetCenter()))
                    .ThenBy(rect => rect.Position.Y).ThenBy(rect => rect.Position.X).Take(sizes.Length)
                    .OrderBy(rect => rect.Position.Y).ThenBy(rect => rect.Position.X).ToArray();
                float score = selected.Sum(rect => rect.GetCenter().DistanceSquaredTo(anchor.GetCenter()));
                if (score >= bestScore) continue;
                bestScore = score;
                // Reserve equal grid cells during packing; return the actual host
                // size so caption/card metrics and hit regions remain truthful.
                best = selected.Select((rect, index) => new Rect2(rect.Position, sizes[index])).ToArray();
            }
            return best;
        }

        Rect2[]? Remember(Rect2[]? regions)
        {
            _potionGroupLayoutCache = new(viewport, anchor, nativeTextTip, copiedOccupied, groups, scale, _potionGroupScale, regions);
            return regions;
        }
    }

    private static float[] Origins(float minimum, float maximum, float extent, float step, IEnumerable<float> boundaries)
    {
        float Phase(float value)
        {
            float remainder = (value - minimum) % step;
            if (remainder < 0) remainder += step;
            return minimum + remainder;
        }
        return boundaries.Append(minimum).Append(maximum - extent).Select(Phase)
            .Where(value => value + extent <= maximum + .01f).Distinct().Order().ToArray();
    }
}
