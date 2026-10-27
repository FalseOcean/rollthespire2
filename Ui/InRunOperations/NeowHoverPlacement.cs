using Godot;

namespace RolltheSpire2.Ui.InRunOperations;

internal readonly record struct NeowHoverAreas(Rect2 Summary, Rect2 Cards, Vector2 NativeTipShift = default);

internal static class NeowHoverPlacement
{
    internal static NeowHoverAreas Plan(Rect2 viewport, Rect2 anchor, IReadOnlyList<Rect2> options,
        IEnumerable<Rect2> occupied, Vector2 summaryDesired, Vector2 cardsDesired, bool bones,
        Func<float, Vector2>? cardsSizeForWidth = null, Rect2? nativeTextTip = null)
    {
        // The caller supplies the current owner and genuine global controls.
        // Other choices and dialogue remain legitimate display space.
        var safe = new Rect2(viewport.Position + new Vector2(18, 18),
            new(Math.Max(0, viewport.Size.X - 36), Math.Max(0, viewport.Size.Y - 36)));
        var protectedRegions = occupied.Append(anchor).Where(r => r.Size.X > 0 && r.Size.Y > 0).Distinct().ToArray();
        var summary = new Rect2(safe.Position, Vector2.Zero);
        Vector2 tipShift = Vector2.Zero;
        if (summaryDesired.X > 0 && summaryDesired.Y > 0)
        {
            var size = new Vector2(Math.Min(summaryDesired.X, safe.Size.X), Math.Min(summaryDesired.Y, safe.Size.Y));
            if (!bones && nativeTextTip is { } tip && tip.Size.Y > 0)
            {
                // Treat prediction and the existing native explanation as one
                // column. Move their presentation together, never overwrite it.
                size.Y = Math.Min(size.Y, Math.Max(1, safe.Size.Y - tip.Size.Y - 14));
                var columnSize = new Vector2(Math.Max(size.X, tip.Size.X), size.Y + 14 + tip.Size.Y);
                // A small text-width adjustment lets native explanations stay
                // beside centered options instead of forcing the entire column
                // above them. Font and native card scale remain unchanged.
                var column = FindSlot(safe, protectedRegions, width => new(
                    Math.Max(Math.Max(Math.Min(320, size.X), tip.Size.X), Math.Min(width, columnSize.X)), columnSize.Y),
                    new(anchor.Position.X - columnSize.X - 14, anchor.GetCenter().Y - columnSize.Y * .5f), true);
                summary = new(column.Position, new(Math.Min(size.X, column.Size.X),
                    Math.Min(size.Y, Math.Max(1, column.Size.Y - tip.Size.Y - 14))));
                tipShift = new Vector2(summary.Position.X, summary.End.Y + 14) - tip.Position;
            }
            else
            {
                var preferred = bones ? new Vector2(anchor.GetCenter().X - size.X * .5f, anchor.Position.Y - size.Y - 14)
                    : new Vector2(anchor.Position.X - size.X - 14, anchor.GetCenter().Y - size.Y * .5f);
                summary = FindSlot(safe, protectedRegions,
                    width => bones ? size : new(Math.Max(Math.Min(320, size.X), Math.Min(width, size.X)), size.Y),
                    preferred, preferLeft: !bones);
            }
        }
        if (nativeTextTip is { } native) protectedRegions = protectedRegions.Append(new Rect2(native.Position + tipShift, native.Size)).ToArray();
        if (cardsDesired.X <= 0 || cardsDesired.Y <= 0) return new(summary, new(safe.Position, Vector2.Zero), tipShift);
        var blockers = summary.Size.X > 0 ? protectedRegions.Append(summary) : protectedRegions;
        Vector2 CardsForWidth(float width) => cardsSizeForWidth?.Invoke(width) ??
            new(Math.Min(width, cardsDesired.X), cardsDesired.Y);
        var initial = CardsForWidth(safe.Size.X);
        var cards = FindSlot(safe, blockers, CardsForWidth,
            new(anchor.GetCenter().X - initial.X * .5f, anchor.Position.Y - initial.Y - 14), preferLeft: false);
        return new(summary, cards, tipShift);
    }

    private static Rect2 FindSlot(Rect2 region, IEnumerable<Rect2> occupied, Func<float, Vector2> desiredForWidth,
        Vector2 preferred, bool preferLeft)
    {
        var free = new List<Rect2> { region };
        foreach (var raw in occupied)
        {
            var obstacle = raw.Grow(12).Intersection(region);
            if (obstacle.Size.X <= 0 || obstacle.Size.Y <= 0) continue;
            var next = new List<Rect2>();
            foreach (var rect in free)
            {
                if (!rect.Intersects(obstacle)) { next.Add(rect); continue; }
                Add(new(rect.Position, new(rect.Size.X, obstacle.Position.Y - rect.Position.Y)));
                Add(new(new(rect.Position.X, obstacle.End.Y), new(rect.Size.X, rect.End.Y - obstacle.End.Y)));
                Add(new(rect.Position, new(obstacle.Position.X - rect.Position.X, rect.Size.Y)));
                Add(new(new(obstacle.End.X, rect.Position.Y), new(rect.End.X - obstacle.End.X, rect.Size.Y)));
                void Add(Rect2 candidate)
                { if (candidate.Size.X > 1 && candidate.Size.Y > 1) next.Add(candidate); }
            }
            free = next.Distinct().Where(r => !next.Any(other => other != r && other.Encloses(r))).ToList();
        }
        if (free.Count == 0) return new(region.Position, Vector2.Zero);
        return free.Select(rect =>
        {
            var desired = desiredForWidth(rect.Size.X);
            var size = new Vector2(Math.Min(desired.X, rect.Size.X), Math.Min(desired.Y, rect.Size.Y));
            var position = new Vector2(Math.Clamp(preferred.X, rect.Position.X, Math.Max(rect.Position.X, rect.End.X - size.X)),
                Math.Clamp(preferred.Y, rect.Position.Y, Math.Max(rect.Position.Y, rect.End.Y - size.Y)));
            float fit = Math.Min(size.X / Math.Max(1, desired.X), size.Y / Math.Max(1, desired.Y));
            float distance = position.DistanceTo(preferred);
            // A full-size neighbouring text panel wins; cards use the largest
            // usable area so their native faces need as little scaling as possible.
            float score = fit * 100000 + (preferLeft ? -distance : size.X * size.Y * .001f - distance);
            return (Rect: new Rect2(position, size), Score: score);
        }).OrderByDescending(c => c.Score).First().Rect;
    }
}
