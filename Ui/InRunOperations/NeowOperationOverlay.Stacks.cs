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
    private sealed class CardStack(ImmutableArray<PredictorCard> cards, string caption, int branch, bool stacked, bool spread, bool highlighted, bool earliestFirst, bool forceSingleRow, bool dimmed, bool compact, int? price, bool singleStack)
    {
        public readonly ImmutableArray<PredictorCard> Cards = cards;
        public string Caption = caption;
        public readonly int? Price = price;
        public Control? PriceView;
        public readonly int Branch = branch;
        public bool Stacked = stacked;
        public readonly bool Spread = spread;
        public readonly bool Highlighted = highlighted, EarliestFirst = earliestFirst, ForceSingleRow = forceSingleRow;
        public readonly bool Dimmed = dimmed;
        public readonly bool SingleStack = singleStack;
        public readonly float Gap = compact ? OrdinalGap : GroupGap;
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
        bool dimmed = false, bool compact = false, int? price = null)
    {
        if (MapCardPresentation && MapSummaryOnly) return;
        if (MapCardPresentation)
        {
            cards = cards.Where(MapUsesCardFace).ToImmutableArray();
            if (cards.IsEmpty) return;
        }
        _stacks.Add(new(cards, (possible ? UiText.Get("operation.possiblePrefix") : "") + caption, branch,
            MapCardPresentation || stacked, !MapCardPresentation && spread, highlighted,
            earliestFirst, MapCardPresentation || forceSingleRow, dimmed, compact, price, MapCardPresentation));
    }

    private void AddIconGroup(ImmutableArray<ModelKey> models, string caption, bool possible) =>
        _iconGroups.Add(new(models, (possible ? UiText.Get("operation.possiblePrefix") : "") + caption));

    private static float CaptionHeight(string caption, float width) => caption.Length == 0 ? 0 :
        Math.Max(1, Mathf.Ceil(MainPanelTheme.Regular.GetStringSize(caption, fontSize: 18).X / Math.Max(1, width))) * 26 + 4;

    private static (Vector2 Size, float Scale, int Columns) GroupMetrics(CardStack stack, float width, float scale)
    {
        int count = stack.Cards.Length;
        float footer = stack.Price.HasValue ? 40 : 0;
        if (stack.Stacked)
        {
            int layers = stack.SingleStack ? count : Math.Min(count, StackLayers);
            float columnWidth = (CardWidth + (layers - 1) * 45) * scale;
            int groups = (count + layers - 1) / layers;
            int stackColumns = float.IsPositiveInfinity(width) ? groups : Math.Clamp((int)((width + GroupGap + .1f) / (columnWidth + GroupGap)), 1, groups);
            int stackRows = (groups + stackColumns - 1) / stackColumns;
            float measuredWidth = stackColumns * columnWidth + (stackColumns - 1) * GroupGap;
            float columnHeight = (CardHeight + (layers - 1) * 45) * scale;
            return (new(measuredWidth, CaptionHeight(stack.Caption, measuredWidth) + stackRows * columnHeight + (stackRows - 1) * GroupGap + footer), scale, stackColumns);
        }
        float cardWidth = CardWidth * scale, gap = CardGap * scale;
        int columns = stack.ForceSingleRow || float.IsPositiveInfinity(width) ? count : Math.Clamp((int)((width + gap + .1f) / (cardWidth + gap)), 1, count);
        float fitScale = scale;
        int rows = (count + columns - 1) / columns;
        float gridWidth = columns * CardWidth * fitScale + (columns - 1) * CardGap * fitScale;
        return (new(gridWidth, CaptionHeight(stack.Caption, gridWidth) + rows * CardHeight * fitScale +
            (rows - 1) * CardGap * fitScale + footer), fitScale, columns);
    }

    // Used by placement before assigning the dock rectangle. Spread is wrapped
    // according to available width; map-owned views apply their count policy.
    public Vector2 CardsSizeForWidth(float width) => MeasureDock(width, CardDisplayScale);

    private Vector2 MeasureDock(float width, float scale)
    {
        if (_stacks.Count == 0 && _iconGroups.Count == 0) return Vector2.Zero;
        width = MapCardPresentation || _stacks.Count > 0 && _stacks.All(stack => stack.ForceSingleRow) && _iconGroups.Count == 0
            ? float.PositiveInfinity : Math.Max(1, width);
        float x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        foreach (var stack in _stacks) Place(GroupMetrics(stack, width, scale).Size, stack.Gap);
        foreach (var icons in _iconGroups) Place(IconSize(icons, width), GroupGap);
        return new(usedWidth, y + rowHeight);
        void Place(Vector2 size, float gap)
        {
            if (x > 0 && x + size.X > width + .1f) { x = 0; y += rowHeight + GroupGap; rowHeight = 0; }
            usedWidth = Math.Max(usedWidth, x + size.X); rowHeight = Math.Max(rowHeight, size.Y);
            x += size.X + gap;
        }
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
        for (int i = 0; i < _stacks.Count; i++)
        {
            var stack = _stacks[i];
            stack.Host = new Control { Name = "Rt2NeowCardStack_" + i, MouseFilter = MouseFilterEnum.Ignore };
            stack.Host.SetMeta("rt2_stacked", stack.Stacked);
            stack.Host.SetMeta("rt2_spread", stack.Spread);
            stack.Host.SetMeta("rt2_earliest_first", stack.EarliestFirst);
            stack.Host.SetMeta("rt2_highlighted", stack.Highlighted);
            stack.Host.SetMeta("rt2_single_row", stack.ForceSingleRow);
            _cardsArea.AddChild(stack.Host); _visibleCardRegions.Add(stack.Host);
            stack.Label = ArtLabel(stack.Caption); stack.Host.AddChild(stack.Label);
            if (stack.Highlighted) stack.Label.AddThemeColorOverride("font_color", GoldInk);
            foreach (var snapshot in stack.Cards)
            {
                var tile = NativeCard(snapshot, 1);
                stack.Tiles.Add(tile); stack.Host.AddChild(tile);
            }
            if (stack.Price is { } price)
            {
                stack.PriceView = PriceDisplay(price);
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
        if (texture != null) return new TextureRect { Texture = texture, Size = new(size, size),
            CustomMinimumSize = new(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
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
        float x = 0, y = 0, rowHeight = 0;
        int stackIndex = 0;
        foreach (var stack in _stacks)
        {
            var (size, cardScale, columns) = GroupMetrics(stack, availableWidth, scale);
            stack.Host.Position = _potionGroupRegions is { } regions
                ? regions[stackIndex].Position - _cardsBounds.Position : NextPosition(size, stack.Gap);
            stack.Host.Size = size;
            stackIndex++;
            float caption = CaptionHeight(stack.Caption, size.X);
            stack.Label.Size = new(size.X, caption);
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
                priceView.Position = new((size.X - priceSize.X) / 2, size.Y - 34);
                priceView.Size = priceSize;
            }
        }
        foreach (var group in _iconGroups)
        {
            var size = IconSize(group, availableWidth);
            group.Host.Position = NextPosition(size, GroupGap); group.Host.Size = size;
            float caption = CaptionHeight(group.Caption, size.X);
            group.Label.Size = new(size.X, caption);
            int columns = float.IsPositiveInfinity(availableWidth) ? Math.Max(1, group.Models.Length) : Math.Max(1, (int)(availableWidth / 84));
            for (int i = 0; i < group.Icons.Count; i++) group.Icons[i].Position = new(i % columns * 84, caption + i / columns * 84);
        }
        Vector2 NextPosition(Vector2 size, float gap)
        {
            if (x > 0 && x + size.X > availableWidth + .1f) { x = 0; y += rowHeight + GroupGap; rowHeight = 0; }
            var position = new Vector2(x, y);
            rowHeight = Math.Max(rowHeight, size.Y); x += size.X + gap;
            return position;
        }
    }
}
