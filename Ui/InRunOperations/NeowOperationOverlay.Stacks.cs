using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private const float CardWidth = 350, CardHeight = 470, CardGap = 18, GroupGap = 22;
    private const float OrdinalGap = 8;
    // One viewport-level size for every option. Placement may wrap groups,
    // but never changes card scale according to the hovered option's height.
    internal float? CardScaleOverride { get; set; }
    internal float CardDisplayScale => _potionGroupScale *
        (CardScaleOverride ?? OperationNativeCardDisplay.ScaleForViewport(GetViewportRect().Size));
    private const int StackLayers = 6;
    private sealed class CardStack(ImmutableArray<PredictorCard> cards, string caption, int branch, bool stacked, bool spread, bool highlighted, bool earliestFirst, bool forceSingleRow, bool dimmed, bool compact, int? price, bool singleStack, ImmutableArray<OperationIconPath> iconPaths)
    {
        public readonly ImmutableArray<PredictorCard> Cards = cards;
        public string Caption = caption;
        public readonly int? Price = price;
        public Control? PriceView;
        public readonly ImmutableArray<OperationIconPath> IconPaths = iconPaths;
        public Control? IconPathsView;
        public IconPathPresentation? PathPresentation;
        public string? PathSignature;
        public float CaptionWidth = -1, CaptionHeightValue;
        public string? CaptionLanguage;
        public readonly int Branch = branch;
        public bool Stacked = stacked;
        public readonly bool Spread = spread;
        public readonly bool Highlighted = highlighted, EarliestFirst = earliestFirst, ForceSingleRow = forceSingleRow;
        public readonly bool Dimmed = dimmed;
        public readonly bool SingleStack = singleStack;
        public readonly float Gap = compact ? OrdinalGap : GroupGap;
        public HuntTreeRender? HuntTree;
        public int HuntNodeId;
        public Control Host = null!;
        public Label Label = null!;
        public readonly List<Control> Tiles = [];
    }
    private sealed class IconGroup(ImmutableArray<ModelKey> models, string caption)
    {
        public readonly ImmutableArray<ModelKey> Models = models;
        public string Caption = caption;
        public Control Host = null!;
        public Label Label = null!;
        public readonly List<Control> Icons = [];
    }
    private readonly List<CardStack> _stacks = [];
    private readonly List<IconGroup> _iconGroups = [];

    private void AddCardGroup(ImmutableArray<PredictorCard> cards, string caption, int branch,
        bool stacked, bool spread, bool possible, bool highlighted = false, bool earliestFirst = false, bool forceSingleRow = false,
        bool dimmed = false, bool compact = false, int? price = null, ImmutableArray<OperationIconPath> iconPaths = default)
    {
        if (MapCardPresentation && MapSummaryOnly) return;
        if (MapCardPresentation)
        {
            cards = cards.Where(MapUsesCardFace).ToImmutableArray();
            if (cards.IsEmpty) return;
        }
        _stacks.Add(new(cards, (possible ? UiText.Get("operation.possiblePrefix") : "") + caption, branch,
            MapCardPresentation || stacked, !MapCardPresentation && spread, highlighted,
            earliestFirst, MapCardPresentation || forceSingleRow, dimmed, compact, price, MapCardPresentation, iconPaths));
    }

    private void AddIconGroup(ImmutableArray<ModelKey> models, string caption, bool possible) =>
        _iconGroups.Add(new(models, (possible ? UiText.Get("operation.possiblePrefix") : "") + caption));

    private static float CaptionHeight(string caption, float width) => caption.Length == 0 ? 0 :
        Math.Max(1, Mathf.Ceil(MainPanelTheme.Regular.GetStringSize(caption, fontSize: 18).X / Math.Max(1, width))) * 26 + 4;

    private float StackHeaderHeight(CardStack stack, float width)
    {
        if (stack.CaptionWidth != width || stack.CaptionLanguage != RolltheSpire2.Ui.Settings.UiLanguagePreferences.LanguageCode)
        {
            stack.CaptionWidth = width;
            stack.CaptionLanguage = RolltheSpire2.Ui.Settings.UiLanguagePreferences.LanguageCode;
            stack.CaptionHeightValue = stack.HuntTree != null ? HuntCaptionHeight(stack.Caption, width) :
                CaptionHeight(stack.Caption, stack.Price.HasValue ? Math.Max(1, width - 12) : width) +
                (stack.Price.HasValue && stack.Caption.Length > 0 ? 6 : 0);
        }
        return IconPathsSize(stack).Y + stack.CaptionHeightValue;
    }

    private (Vector2 Size, float Scale, int Columns) GroupMetrics(CardStack stack, float width, float scale)
    {
        int count = stack.Cards.Length;
        float footer = stack.Price.HasValue ? 46 : 0;
        if (stack.Stacked)
        {
            int layers = stack.SingleStack ? count : Math.Min(count, StackLayers);
            float columnWidth = (CardWidth + (layers - 1) * 45) * scale;
            int groups = (count + layers - 1) / layers;
            int stackColumns = float.IsPositiveInfinity(width) ? groups : Math.Clamp((int)((width + GroupGap + .1f) / (columnWidth + GroupGap)), 1, groups);
            int stackRows = (groups + stackColumns - 1) / stackColumns;
            float measuredWidth = Math.Max(IconPathsSize(stack).X, stackColumns * columnWidth + (stackColumns - 1) * GroupGap);
            float columnHeight = (CardHeight + (layers - 1) * 45) * scale;
            return (new(measuredWidth, StackHeaderHeight(stack, measuredWidth) + stackRows * columnHeight + (stackRows - 1) * GroupGap + footer), scale, stackColumns);
        }
        float cardWidth = CardWidth * scale, gap = CardGap * scale;
        int columns = stack.ForceSingleRow || float.IsPositiveInfinity(width) ? count : Math.Clamp((int)((width + gap + .1f) / (cardWidth + gap)), 1, count);
        float fitScale = scale;
        int rows = (count + columns - 1) / columns;
        float gridWidth = Math.Max(IconPathsSize(stack).X, columns * CardWidth * fitScale + (columns - 1) * CardGap * fitScale);
        return (new(gridWidth, StackHeaderHeight(stack, gridWidth) + rows * CardHeight * fitScale +
            (rows - 1) * CardGap * fitScale + footer), fitScale, columns);
    }

    // Used by placement before assigning the dock rectangle. Spread is wrapped
    // according to available width; map-owned views apply their count policy.
    public Vector2 CardsSizeForWidth(float width) => MeasureDock(width, CardDisplayScale);

    private readonly record struct StackLayout(Vector2 Position, Vector2 Size, float Scale, int Columns, float Header);
    private sealed record DockLayout(StackLayout[] Stacks, Vector2[] IconPositions, Vector2 Size);

    private Vector2 MeasureDock(float width, float scale) => PlanDock(width, scale).Size;

    // Measure and place from the same rows. Every card group in one visual
    // row reserves that row's tallest header, including wrapped captions and
    // route alternatives. A new row starts with its own header requirement.
    private DockLayout PlanDock(float width, float scale)
    {
        width = MapCardPresentation || _stacks.Count > 0 && _stacks.All(stack => stack.ForceSingleRow) && _iconGroups.Count == 0
            ? float.PositiveInfinity : Math.Max(1, width);
        var stacks = new StackLayout[_stacks.Count];
        var iconPositions = new Vector2[_iconGroups.Count];
        var rowStacks = new List<int>();
        var rowIcons = new List<(int Index, Vector2 Size)>();
        float x = 0, y = 0, usedWidth = 0;
        for (int index = 0; index < _stacks.Count; index++)
        {
            var stack = _stacks[index];
            if (stack.HuntTree != null) continue;
            var metrics = GroupMetrics(stack, width, scale);
            if (x > 0 && x + metrics.Size.X > width + .1f) FinishRow();
            stacks[index] = new(new(x, y), metrics.Size, metrics.Scale, metrics.Columns, StackHeaderHeight(stack, metrics.Size.X));
            rowStacks.Add(index);
            usedWidth = Math.Max(usedWidth, x + metrics.Size.X);
            x += metrics.Size.X + stack.Gap;
        }
        for (int index = 0; index < _iconGroups.Count; index++)
        {
            var size = IconSize(_iconGroups[index], width);
            if (x > 0 && x + size.X > width + .1f) FinishRow();
            iconPositions[index] = new(x, y);
            rowIcons.Add((index, size));
            usedWidth = Math.Max(usedWidth, x + size.X);
            x += size.X + GroupGap;
        }
        float height = FinishRow();
        var dockSize = PlanHuntTrees(stacks, height > 0 && _huntTrees.Count > 0 ? height + GroupGap : height, scale, usedWidth);
        return new(stacks, iconPositions, dockSize);

        float FinishRow()
        {
            if (rowStacks.Count == 0 && rowIcons.Count == 0) return y;
            float header = rowStacks.Count == 0 ? 0 : rowStacks.Max(index => stacks[index].Header);
            float rowHeight = rowIcons.Count == 0 ? 0 : rowIcons.Max(icon => icon.Size.Y);
            foreach (int index in rowStacks)
            {
                var entry = stacks[index];
                var size = new Vector2(entry.Size.X, entry.Size.Y + header - entry.Header);
                stacks[index] = entry with { Size = size, Header = header };
                rowHeight = Math.Max(rowHeight, size.Y);
            }
            float end = y + rowHeight;
            y = end + GroupGap; x = 0;
            rowStacks.Clear(); rowIcons.Clear();
            return end;
        }
    }

    private Vector2[] NaturalGroupSizes(float scale) => _stacks.Select(stack =>
        GroupMetrics(stack, float.PositiveInfinity, scale).Size).ToArray();

    private Vector2[] AlignGroupSizes(Vector2[] sizes, IEnumerable<int> row)
    {
        var indices = row.ToArray();
        float header = indices.Max(index => StackHeaderHeight(_stacks[index], sizes[index].X));
        foreach (int index in indices)
            sizes[index] = new(sizes[index].X, sizes[index].Y + header - StackHeaderHeight(_stacks[index], sizes[index].X));
        return sizes;
    }

    private static Vector2 IconSize(IconGroup group, float width) =>
        new(Math.Min(width, group.Models.Length * 84), CaptionHeight(group.Caption, Math.Min(width, group.Models.Length * 84)) + 84 *
            Math.Max(1, (int)Math.Ceiling(group.Models.Length / Math.Max(1f, MathF.Floor(width / 84)))));

    private void BuildStacks()
    {
        if (_stacks.Count == 0 && _iconGroups.Count == 0) return;
        SetMapCardCount(MapCardCount);
        _cardsArea = new Control { Name = "Rt2NeowCardDock", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_cardsArea);
        BuildHuntTrees();
        for (int i = 0; i < _stacks.Count; i++)
        {
            var stack = _stacks[i];
            stack.Host = new Control { Name = "Rt2NeowCardStack_" + i, MouseFilter = MouseFilterEnum.Ignore };
            stack.Host.SetMeta("rt2_stacked", stack.Stacked);
            stack.Host.SetMeta("rt2_spread", stack.Spread);
            stack.Host.SetMeta("rt2_earliest_first", stack.EarliestFirst);
            stack.Host.SetMeta("rt2_highlighted", stack.Highlighted);
            stack.Host.SetMeta("rt2_single_row", stack.ForceSingleRow);
            if (stack.HuntTree is { } tree)
            {
                stack.Host.Name = "Rt2HuntTreeNode_" + stack.HuntNodeId;
                tree.Host.AddChild(stack.Host);
                HuntMetadata(stack.Host, tree, tree.Tree.Nodes[stack.HuntNodeId]);
            }
            else _cardsArea.AddChild(stack.Host);
            _visibleCardRegions.Add(stack.Host);
            if (!stack.IconPaths.IsDefaultOrEmpty)
            {
                stack.IconPathsView = IconPathsStrip(stack);
                stack.Host.AddChild(stack.IconPathsView);
            }
            stack.Label = ArtLabel(stack.Caption); stack.Host.AddChild(stack.Label);
            if (stack.Price.HasValue) stack.Label.AddThemeStyleboxOverride("normal", AnnotationStyle());
            if (stack.Highlighted) stack.Label.AddThemeColorOverride("font_color", GoldInk);
            foreach (var snapshot in stack.Cards)
            {
                var tile = NativeCard(snapshot, 1);
                stack.Tiles.Add(tile); stack.Host.AddChild(tile);
            }
            if (stack.Price is { } price)
            {
                stack.PriceView = PriceBadge(price);
                stack.Host.AddChild(stack.PriceView);
            }
        }
        for (int i = 0; i < _iconGroups.Count; i++)
        {
            var group = _iconGroups[i];
            group.Host = new Control { Name = "Rt2OperationIconGroup_" + i, MouseFilter = MouseFilterEnum.Ignore };
            _cardsArea.AddChild(group.Host); _visibleCardRegions.Add(group.Host);
            group.Label = ArtLabel(group.Caption); group.Host.AddChild(group.Label);
            foreach (var key in group.Models)
            {
                var icon = ModelIcon(key, 72);
                icon.SetMeta("rt2_model", key.Serialized);
                group.Icons.Add(icon); group.Host.AddChild(icon);
            }
        }
    }

    private static Label ArtLabel(string caption)
    {
        var label = Text(caption, 18);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeColorOverride("font_color", MutedInk);
        label.Visible = caption.Length > 0;
        return label;
    }

    private Control ModelIcon(ModelKey key, int size)
    {
        var kind = key.Category == "POTION" ? GameContentKind.Potion : GameContentKind.Relic;
        var texture = Icons.Resolve(key, kind, IconVariant.RelicLarge).Texture;
        // Disable the texture's native minimum before assigning the requested
        // rectangle. Manually positioned children have no Container resize pass
        // to repair a Size that was clamped to the source sprite's dimensions.
        if (texture != null) return new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = texture, CustomMinimumSize = new(size, size),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore,
            Size = new(size, size) };
        var fallback = Text(NameOf(key), 18); fallback.Size = new(size, size);
        fallback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return fallback;
    }

    private void LayoutStacks()
    {
        if (_cardsArea == null) return;
        _cardsArea.Position = _cardsBounds.Position; _cardsArea.Size = _cardsBounds.Size;
        _cardsArea.Visible = _cardsBounds.Size.X > 0 && _cardsBounds.Size.Y > 0;
        if (!_cardsArea.Visible) return;
        float availableWidth = MapCardPresentation || _potionGroupRegions != null || _stacks.Count > 0 && _stacks.All(stack => stack.ForceSingleRow) && _iconGroups.Count == 0
            ? float.PositiveInfinity : _cardsBounds.Size.X;
        float scale = CardDisplayScale;
        var dock = PlanDock(availableWidth, scale);
        // Event battles can have several correlated reward rows beside the summary.
        // Placement may grant less height than those rows need at native size.
        if (!MapCardPresentation && _preview?.Notices.Contains("EventCombatStableContextNoExtraActions") == true)
        {
            float fit = Math.Min(1, Math.Min(_cardsBounds.Size.X / Math.Max(1, dock.Size.X),
                _cardsBounds.Size.Y / Math.Max(1, dock.Size.Y)));
            _cardsArea.Scale = Vector2.One * fit;
        }
        int stackIndex = 0;
        foreach (var stack in _stacks)
        {
            var layout = dock.Stacks[stackIndex];
            Vector2 size = layout.Size;
            float cardScale = layout.Scale;
            int columns = layout.Columns;
            float caption = layout.Header;
            if (_potionGroupRegions is { } regions)
            {
                var natural = GroupMetrics(stack, float.PositiveInfinity, scale);
                size = regions[stackIndex].Size;
                cardScale = natural.Scale; columns = natural.Columns;
                caption = StackHeaderHeight(stack, natural.Size.X) + size.Y - natural.Size.Y;
                stack.Host.Position = regions[stackIndex].Position - _cardsBounds.Position;
            }
            else stack.Host.Position = layout.Position;
            stack.Host.Size = size;
            stack.Host.SetMeta("rt2_header_height", caption);
            stackIndex++;
            float orderHeight = 0;
            if (stack.IconPathsView is { } pathsView)
            {
                var pathSize = IconPathsSize(stack);
                // A shared row header aligns the cards. Keep a shorter path
                // beside its own stack rather than floating at the tallest
                // neighbour's first line; its existing trailing gap stays six px.
                float pathTop = Math.Max(0, caption - pathSize.Y - CaptionHeight(stack.Caption, size.X));
                pathsView.Position = new(0, pathTop);
                pathsView.Size = pathSize;
                orderHeight = pathTop + pathSize.Y;
            }
            stack.Label.Position = new(0, orderHeight);
            if (stack.Price.HasValue)
                SizeWrappedLabel(stack.Label, new(Math.Min(size.X, MainPanelTheme.Regular.GetStringSize(stack.Caption, fontSize: 18).X + 12),
                    Math.Max(0, caption - orderHeight - 4)));
            else if (stack.HuntTree != null) SizeWrappedLabel(stack.Label, new(size.X, caption - orderHeight));
            else stack.Label.Size = new(size.X, caption - orderHeight);
            int layers = stack.SingleStack ? stack.Cards.Length : Math.Min(stack.Cards.Length, StackLayers);
            for (int i = 0; i < stack.Tiles.Count; i++)
            {
                // Acquisition bundles keep the native last-card-front ordering.
                // Transform candidates instead expose their earliest source at
                // lower-left, with later candidates layered behind to upper-right.
                int group = i / layers, inGroup = i % layers;
                int overlaps = Math.Min(layers, stack.Cards.Length - group * layers) - 1;
                int depth = stack.EarliestFirst ? overlaps - inGroup : inGroup;
                stack.Tiles[i].ZIndex = stack.Stacked ? depth + 1 : 0;
                stack.Tiles[i].Position = stack.Stacked
                    ? new((group % columns) * ((CardWidth + (layers - 1) * 45) * cardScale + GroupGap) + (overlaps - depth) * 45 * cardScale,
                        caption + (group / columns) * ((CardHeight + (layers - 1) * 45) * cardScale + GroupGap) + depth * 45 * cardScale)
                    : new((i % columns) * (CardWidth + CardGap) * cardScale,
                        caption + (i / columns) * (CardHeight + CardGap) * cardScale);
                stack.Tiles[i].Scale = Vector2.One * cardScale;
                float brightness = !stack.Stacked || overlaps == 0 ? 1 : .55f + .45f * depth / overlaps;
                if (stack.Dimmed) brightness *= .55f;
                stack.Tiles[i].Modulate = new(brightness, brightness, brightness);
            }
            if (stack.PriceView is { } priceView)
            {
                var priceSize = priceView.GetCombinedMinimumSize();
                priceView.Position = new((size.X - priceSize.X) / 2, size.Y - priceSize.Y);
                priceView.Size = priceSize;
            }
        }
        LayoutHuntTrees(dock);
        for (int iconIndex = 0; iconIndex < _iconGroups.Count; iconIndex++)
        {
            var group = _iconGroups[iconIndex];
            var size = IconSize(group, availableWidth);
            group.Host.Position = dock.IconPositions[iconIndex]; group.Host.Size = size;
            float caption = CaptionHeight(group.Caption, size.X);
            group.Label.Size = new(size.X, caption);
            int columns = float.IsPositiveInfinity(availableWidth) ? Math.Max(1, group.Models.Length) : Math.Max(1, (int)(availableWidth / 84));
            for (int i = 0; i < group.Icons.Count; i++) group.Icons[i].Position = new(i % columns * 84, caption + i / columns * 84);
        }
    }
}
