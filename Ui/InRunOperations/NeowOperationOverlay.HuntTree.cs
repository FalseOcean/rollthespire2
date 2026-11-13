using System.Collections.Immutable;
using Godot;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private const float HuntEdgeGap = 28, HuntLaneGap = 18, HuntEndHeight = 30;
    private const int MaxDivergentHuntTriggers = 2;

    private sealed class HuntTreeRender(OperationHuntTree tree, int branch, string caption)
    {
        internal readonly OperationHuntTree Tree = tree;
        internal readonly int Branch = branch;
        internal readonly string Caption = caption;
        internal readonly Dictionary<int, int> Stacks = [];
        internal readonly Dictionary<int, Line2D> Edges = [];
        internal readonly Dictionary<int, Label> Ends = [];
        internal Control Host = null!, Root = null!;
        internal Label? Heading;
        internal Vector2 RootPosition, Size;
        internal float PlanScale = float.NaN;
        internal string? PlanLanguage;
        internal StackLayout[] LocalLayouts = [];
        internal Vector2 LocalRoot, LocalSize;
        internal readonly Dictionary<int, (Vector2 Start, Vector2 Finish)> EdgePositions = [];
    }

    private readonly List<HuntTreeRender> _huntTrees = [];
    internal bool HasHuntTree => _huntTrees.Count > 0;

    internal static bool CanRenderHuntTree(OperationPreview preview, OperationPreviewBranch branch,
        bool mapPresentation) => !mapPresentation && branch.HuntOutcomes.Length > 1 &&
        OperationDisplayPolicy.Resolve(preview).Text is not (OperationTextMode.Complex or OperationTextMode.CardRows) &&
        branch.HuntOutcomes.SelectMany(outcome => outcome.Sections).Where(section => !section.CardGroups.IsEmpty)
            .All(section => OperationDisplayPolicy.CompactEventCardNames(preview, section, true, false));

    private bool AddHuntTree(OperationPreview preview, OperationPreviewBranch branch, int branchIndex, string branchName,
        out bool limited)
    {
        limited = false;
        if (!CanRenderHuntTree(preview, branch, MapCardPresentation)) return false;
        var tree = OperationHuntTree.Build(branch);
        if (tree.Nodes.Length == 1) return false;
        bool diverges = tree.Nodes.Any(node => node.Children.Length > 1);
        if (diverges && branch.HuntOutcomes.Any(outcome => outcome.Triggers > MaxDivergentHuntTriggers))
        {
            // Keep complete paths for the displayed counts, including late offers on a two-trigger path.
            tree = OperationHuntTree.Build(branch with { HuntOutcomes = branch.HuntOutcomes
                .Where(outcome => outcome.Triggers <= MaxDivergentHuntTriggers).ToImmutableArray() });
            limited = true;
            if (tree.Nodes.Length == 1) return false;
        }
        // The existing minimum-count sequence is already compact for a single
        // linear prefix. The graph adds information at actual divergences.
        if (!diverges &&
            !branch.Sections.Any(section => section.Label is "EventCombatNoHunt" ||
                section.Label?.StartsWith("EventHuntTriggers:", StringComparison.Ordinal) == true)) return false;
        var render = new HuntTreeRender(tree, branchIndex, preview.Branches.Length > 1 ? branchName : "");
        _huntTrees.Add(render);
        foreach (var node in tree.Nodes.Skip(1))
        {
            var parent = tree.Nodes[node.ParentId];
            string caption = parent.Children.Length > 1 || !parent.EndingTriggerCounts.IsEmpty
                ? UiText.Format("operation.hunt.counts", HuntCounts(node.TriggerCounts)) : "";
            if (node.Source!.Source != preview.Source && (parent.Source == null || parent.Source.Source != node.Source.Source))
                caption = JoinCaption(caption, NameOf(node.Source.Source));
            string authored = LocalizedLabel(node.Source.Source, node.CardGroup!.Label);
            caption = JoinCaption(caption, authored);
            int stackIndex = _stacks.Count;
            AddCardGroup(node.CardGroup.Cards, caption, branchIndex, true, false, false,
                earliestFirst: node.CardGroup.EarliestFirst);
            var stack = _stacks[stackIndex];
            stack.HuntTree = render;
            stack.HuntNodeId = node.Id;
            render.Stacks.Add(node.Id, stackIndex);
        }
        return true;
    }

    private static bool AboveHuntDisplayLimit(OperationPreviewSection section)
    {
        const string prefix = "EventHuntTriggers:";
        return section.Label is { } label && label.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(label.AsSpan(prefix.Length), out int count) && count > MaxDivergentHuntTriggers;
    }

    private static string HuntCounts(ImmutableArray<int> counts)
    {
        if (counts.Length > 1 && counts[^1] - counts[0] == counts.Length - 1) return counts[0] + "–" + counts[^1];
        string separator = RolltheSpire2.Ui.Settings.UiLanguagePreferences.LanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "、" : ", ";
        return string.Join(separator, counts);
    }

    private static void HuntMetadata(Control host, HuntTreeRender render, OperationHuntTreeNode node)
    {
        host.SetMeta("rt2_hunt_id", node.Id);
        host.SetMeta("rt2_hunt_parent", node.ParentId);
        host.SetMeta("rt2_hunt_branch", render.Branch);
        host.SetMeta("rt2_hunt_counts", string.Join(',', node.TriggerCounts));
        host.SetMeta("rt2_hunt_ends", string.Join(',', node.EndingTriggerCounts));
        if (node.Source == null || node.CardGroup == null) return;
        host.SetMeta("rt2_hunt_source", node.Source.Source.Serialized);
        host.SetMeta("rt2_hunt_minimum", node.CardGroup.MinimumSelect);
        host.SetMeta("rt2_hunt_maximum", node.CardGroup.MaximumSelect);
        host.SetMeta("rt2_hunt_canskip", node.CardGroup.CanSkip);
        host.SetMeta("rt2_hunt_possible", node.CardGroup.Possible);
    }

    private void BuildHuntTrees()
    {
        foreach (var render in _huntTrees)
        {
            render.Host = new Control { Name = "Rt2HuntTree_" + render.Branch, MouseFilter = MouseFilterEnum.Ignore };
            _cardsArea!.AddChild(render.Host);
            render.Root = new Control { Name = "Rt2HuntTreeRoot", MouseFilter = MouseFilterEnum.Ignore };
            render.Host.AddChild(render.Root);
            HuntMetadata(render.Root, render, render.Tree.Nodes[0]);
            if (render.Caption.Length > 0)
            {
                render.Heading = ArtLabel(render.Caption);
                render.Heading.Name = "Rt2HuntTreeHeading";
                render.Host.AddChild(render.Heading);
            }
            foreach (var node in render.Tree.Nodes)
            {
                if (!node.EndingTriggerCounts.IsEmpty && (node.Id == 0 || !node.Children.IsEmpty))
                {
                    var end = ArtLabel(UiText.Format("operation.hunt.end", HuntCounts(node.EndingTriggerCounts)));
                    end.Name = "Rt2HuntTreeEnd_" + node.Id;
                    render.Host.AddChild(end);
                    render.Ends.Add(node.Id, end);
                    _visibleCardRegions.Add(end);
                }
                if (node.Id == 0) continue;
                // A common first offer needs no connector from an invisible
                // root. Every actual branch has an explicit, visible edge.
                if (node.ParentId == 0 && render.Tree.Nodes[0].Children.Length == 1 &&
                    render.Tree.Nodes[0].EndingTriggerCounts.IsEmpty) continue;
                var edge = new Line2D { Name = "Rt2HuntTreeEdge_" + node.Id, Width = 2,
                    DefaultColor = MutedInk, Antialiased = true, ZIndex = -1 };
                edge.SetMeta("rt2_hunt_parent", node.ParentId);
                edge.SetMeta("rt2_hunt_id", node.Id);
                edge.SetMeta("rt2_hunt_counts", string.Join(',', node.TriggerCounts));
                render.Host.AddChild(edge);
                render.Edges.Add(node.Id, edge);
            }
        }
    }

    private Vector2 PlanHuntTrees(StackLayout[] layouts, float top, float scale, float usedWidth)
    {
        float end = top;
        foreach (var render in _huntTrees)
        {
            EnsureHuntPlan(render, scale);
            foreach (var pair in render.Stacks)
            {
                var local = render.LocalLayouts[pair.Key];
                layouts[pair.Value] = local with { Position = local.Position + new Vector2(0, end) };
            }
            render.RootPosition = render.LocalRoot + new Vector2(0, end);
            render.Size = new(render.LocalSize.X, end + render.LocalSize.Y);
            usedWidth = Math.Max(usedWidth, render.LocalSize.X);
            end += render.LocalSize.Y + GroupGap;
        }
        return new(usedWidth, _huntTrees.Count == 0 ? top : end - GroupGap);
    }

    private void EnsureHuntPlan(HuntTreeRender render, float scale)
    {
        string language = RolltheSpire2.Ui.Settings.UiLanguagePreferences.LanguageCode;
        if (render.PlanScale == scale && render.PlanLanguage == language) return;
        render.PlanScale = scale; render.PlanLanguage = language;
        var nodes = render.Tree.Nodes;
        var depth = new int[nodes.Length];
        var sizes = new Vector2[nodes.Length];
        var columnWidths = new List<float>();
        render.LocalLayouts = new StackLayout[nodes.Length];
        var metrics = new (Vector2 Size, float Scale, int Columns)[nodes.Length];
        foreach (var node in nodes.Skip(1))
        {
            depth[node.Id] = node.ParentId == 0 ? 0 : depth[node.ParentId] + 1;
            while (columnWidths.Count <= depth[node.Id]) columnWidths.Add(0);
            var stack = _stacks[render.Stacks[node.Id]];
            metrics[node.Id] = GroupMetrics(stack, float.PositiveInfinity, scale);
            sizes[node.Id] = metrics[node.Id].Size;
            columnWidths[depth[node.Id]] = Math.Max(columnWidths[depth[node.Id]], sizes[node.Id].X);
        }
        float rootWidth = !nodes[0].EndingTriggerCounts.IsEmpty ? 190 : nodes[0].Children.Length > 1 ? HuntEdgeGap : 0;
        var columnX = new float[columnWidths.Count];
        for (int index = 0; index < columnX.Length; index++)
            columnX[index] = index == 0 ? rootWidth : columnX[index - 1] + columnWidths[index - 1] + HuntEdgeGap;
        float heading = render.Caption.Length == 0 ? 0 : 30;

        // Lay out the deepest route first. Other complete routes may share its
        // row's unused columns: a short zero-trigger leaf must not reserve a
        // whole row above a two-row subtree. Descendants remain contiguous in
        // traversal order, so separate subtrees do not braid their connectors.
        var longest = new int[nodes.Length];
        for (int id = nodes.Length - 1; id >= 0; id--)
            longest[id] = nodes[id].Children.IsEmpty ? 0 : nodes[id].Children.Max(child => longest[child]) + 1;
        var lane = Enumerable.Repeat(-1, nodes.Length).ToArray();
        var occupied = new List<HashSet<int>>();
        AssignRoutes(0, []);
        var laneHeights = new float[occupied.Count];
        foreach (var node in nodes.Skip(1))
        {
            laneHeights[lane[node.Id]] = Math.Max(laneHeights[lane[node.Id]],
                sizes[node.Id].Y + (render.Ends.ContainsKey(node.Id) ? HuntEndHeight : 0));
        }
        var laneY = new float[laneHeights.Length];
        for (int index = 0; index < laneY.Length; index++)
            laneY[index] = index == 0 ? heading : laneY[index - 1] + laneHeights[index - 1] + HuntLaneGap;
        foreach (var node in nodes.Skip(1))
        {
            var stack = _stacks[render.Stacks[node.Id]];
            float footer = render.Ends.ContainsKey(node.Id) ? HuntEndHeight : 0;
            render.LocalLayouts[node.Id] = new(new(columnX[depth[node.Id]], laneY[lane[node.Id]] +
                (laneHeights[lane[node.Id]] - sizes[node.Id].Y - footer) / 2), sizes[node.Id],
                metrics[node.Id].Scale, metrics[node.Id].Columns, StackHeaderHeight(stack, sizes[node.Id].X));
        }
        float height = laneY.Length == 0 ? HuntEndHeight : laneY[^1] + laneHeights[^1];
        // A shared parent can sit between its children if its own column has
        // that space. Keep the packed row when another leaf occupies it.
        for (int id = nodes.Length - 1; id > 0; id--)
        {
            if (nodes[id].Children.IsEmpty) continue;
            var local = render.LocalLayouts[id];
            float center = (nodes[id].Children.Min(CardCenter) + nodes[id].Children.Max(CardCenter)) / 2;
            float top = Math.Clamp(center - local.Header - (local.Size.Y - local.Header) / 2, heading,
                Math.Max(heading, height - local.Size.Y - (render.Ends.ContainsKey(id) ? HuntEndHeight : 0)));
            var proposed = local with { Position = new(local.Position.X, top) };
            var bounds = NodeBounds(id, proposed).Grow(HuntLaneGap / 2);
            if (nodes.Skip(1).Any(other => other.Id != id && depth[other.Id] == depth[id] &&
                bounds.Intersects(NodeBounds(other.Id, render.LocalLayouts[other.Id])))) continue;
            render.LocalLayouts[id] = proposed;
        }
        float rootY = nodes[0].Children.IsEmpty ? heading + HuntEndHeight / 2 :
            (nodes[0].Children.Min(CardCenter) + nodes[0].Children.Max(CardCenter)) / 2;
        render.LocalRoot = new(Math.Max(0, rootWidth - HuntEdgeGap), rootY);
        float width = columnX.Length == 0 ? 190 : columnX[^1] + columnWidths[^1];
        render.LocalSize = new(width, height);

        float CardCenter(int id)
        {
            var layout = render.LocalLayouts[id];
            return layout.Position.Y + layout.Header + (layout.Size.Y - layout.Header) / 2;
        }
        Rect2 NodeBounds(int id, StackLayout layout) => new(layout.Position,
            layout.Size + new Vector2(0, render.Ends.ContainsKey(id) ? HuntEndHeight : 0));
        void AssignRoutes(int id, List<int> path)
        {
            if (id != 0) path = [.. path, id];
            foreach (int child in nodes[id].Children.OrderByDescending(child => longest[child])) AssignRoutes(child, path);
            if (nodes[id].EndingTriggerCounts.IsEmpty) return;
            var added = path.Where(node => lane[node] < 0).ToArray();
            if (added.Length == 0) return;
            int chosen = occupied.FindIndex(columns => added.All(node => !columns.Contains(depth[node])));
            if (chosen < 0) { chosen = occupied.Count; occupied.Add([]); }
            foreach (int node in added) { lane[node] = chosen; occupied[chosen].Add(depth[node]); }
        }
    }

    private void LayoutHuntTrees(DockLayout dock)
    {
        foreach (var render in _huntTrees)
        {
            render.Host.Size = render.Size;
            render.Root.Position = render.RootPosition;
            if (render.Heading is { } heading)
            {
                float firstY = render.Stacks.Values.Min(index => dock.Stacks[index].Position.Y);
                heading.Position = new(0, Math.Max(0, firstY - 30));
                SizeWrappedLabel(heading, new(render.Size.X, 28));
            }
            foreach (var pair in render.Ends)
            {
                var size = pair.Key == 0 ? new Vector2(180, HuntEndHeight) :
                    new Vector2(dock.Stacks[render.Stacks[pair.Key]].Size.X, HuntEndHeight);
                pair.Value.Position = pair.Key == 0 ? new Vector2(0, render.RootPosition.Y - HuntEndHeight / 2) :
                    dock.Stacks[render.Stacks[pair.Key]].Position + new Vector2(0, dock.Stacks[render.Stacks[pair.Key]].Size.Y);
                SizeWrappedLabel(pair.Value, size);
            }
            foreach (var pair in render.Edges)
            {
                var node = render.Tree.Nodes[pair.Key];
                var target = dock.Stacks[render.Stacks[node.Id]];
                var (start, finish) = HuntEdgeEndpoints(render, node, dock);
                if (!render.EdgePositions.TryGetValue(pair.Key, out var previous) || previous.Start != start || previous.Finish != finish)
                {
                    float middle = (start.X + finish.X) / 2;
                    pair.Value.Points = [start, new(middle, start.Y), new(middle, finish.Y), finish];
                    render.EdgePositions[pair.Key] = (start, finish);
                }
            }
        }
    }
    private static float HuntCaptionHeight(string caption, float width) => caption.Length == 0 ? 0 :
        Mathf.Ceil(RolltheSpire2.Ui.Theme.MainPanelTheme.Regular.GetMultilineStringSize(caption,
            HorizontalAlignment.Left, Math.Max(1, width), 18, -1,
            TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive).Y) + 10;

    private static void SizeWrappedLabel(Label label, Vector2 size)
    {
        // A fresh wrapping Label may retain the minimum height from its old
        // zero-width line layout. Resolve the new width before setting height.
        label.Size = new(size.X, label.Size.Y);
        _ = label.GetMinimumSize();
        label.Size = size;
    }

    private static (Vector2 Start, Vector2 Finish) HuntEdgeEndpoints(HuntTreeRender render,
        OperationHuntTreeNode node, DockLayout dock)
    {
        var target = dock.Stacks[render.Stacks[node.Id]];
        Vector2 finish = target.Position + new Vector2(-6, target.Header + (target.Size.Y - target.Header) / 2);
        Vector2 start;
        if (node.ParentId == 0)
            start = render.RootPosition + new Vector2(render.Ends.ContainsKey(0) ? HuntEdgeGap - 6 : 0, 0);
        else
        {
            var parent = dock.Stacks[render.Stacks[node.ParentId]];
            start = parent.Position + new Vector2(parent.Size.X + 6, parent.Header + (parent.Size.Y - parent.Header) / 2);
        }
        return (start, finish);
    }

    internal NeowHoverAreas? PlanNativeHuntAreas(Rect2 viewport, Rect2 anchor, IReadOnlyList<Rect2> occupied,
        Rect2? nativeTip)
    {
        if (!HasHuntTree || MapCardPresentation) return null;
        var safe = new Rect2(viewport.Position + new Vector2(18, 18), viewport.Size - new Vector2(36, 36));
        var dock = PlanDock(float.PositiveInfinity, CardDisplayScale);
        if (dock.Size.X > safe.Size.X || dock.Size.Y > safe.Size.Y) return null;
        var column = NeowHoverPlacement.PlanRightHuntColumn(viewport, anchor, occupied, SummaryDesiredSize, nativeTip);
        var blockers = occupied.Append(anchor).Append(column.Summary)
            .Where(region => region.HasArea()).ToList();
        if (nativeTip is { } tip) blockers.Add(new(tip.Position + column.NativeTipShift, tip.Size));
        var pieces = new List<Rect2>();
        pieces.AddRange(dock.Stacks.Select(layout => new Rect2(layout.Position, layout.Size)));
        for (int index = 0; index < _iconGroups.Count; index++)
            pieces.Add(new(dock.IconPositions[index], IconSize(_iconGroups[index], float.PositiveInfinity)));
        foreach (var render in _huntTrees)
        {
            foreach (var ending in render.Ends)
                pieces.Add(ending.Key == 0 ? new(new Vector2(0, render.RootPosition.Y - HuntEndHeight / 2), new Vector2(180, HuntEndHeight)) :
                    new(dock.Stacks[render.Stacks[ending.Key]].Position + new Vector2(0, dock.Stacks[render.Stacks[ending.Key]].Size.Y),
                        new Vector2(dock.Stacks[render.Stacks[ending.Key]].Size.X, HuntEndHeight)));
            if (render.Caption.Length > 0)
                pieces.Add(new(new Vector2(0, render.Size.Y - render.LocalSize.Y), new Vector2(render.Size.X, 30)));
            foreach (var id in render.Edges.Keys)
            {
                var (start, finish) = HuntEdgeEndpoints(render, render.Tree.Nodes[id], dock);
                float middle = (start.X + finish.X) / 2;
                pieces.Add(Segment(start, new(middle, start.Y)));
                pieces.Add(Segment(new(middle, start.Y), new(middle, finish.Y)));
                pieces.Add(Segment(new(middle, finish.Y), finish));
            }
        }
        float belowTop = occupied.Where(region => region.Position.Y <= safe.Position.Y && region.Size.Y < viewport.Size.Y / 4)
            .Select(region => region.End.Y + 12).DefaultIfEmpty(safe.Position.Y).Max();
        // One event-specific candidate: long route above the option, short
        // branches to its left, text in the opposite edge column. Its empty
        // lower-right corner may contain the native option; every drawn piece
        // and connector must still clear that option and the explanation.
        var position = new Vector2(safe.Position.X, Math.Max(safe.Position.Y, belowTop));
        if (!safe.Encloses(new Rect2(position, dock.Size))) return null;
        if (pieces.Any(piece => blockers.Any(blocker => new Rect2(piece.Position + position, piece.Size).Intersects(blocker.Grow(12)))))
            return null;
        return new(column.Summary, new(position, dock.Size), column.NativeTipShift);

        static Rect2 Segment(Vector2 a, Vector2 b) => new Rect2(new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y))).Grow(2);
    }

}
