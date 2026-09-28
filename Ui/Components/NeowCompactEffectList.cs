using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Player-facing compact rendering of already-structured Neow effect facts.
/// Results are grouped by player-readable result type while preserving the source
/// predictor's group/order semantics. Kaleidoscope can explicitly expose its two
/// real offer groups without falling back to report/debug narration.
/// </summary>
internal sealed partial class NeowCompactEffectList : VBoxContainer
{
    private enum ResultType
    {
        Card,
        Potion,
        Relic,
        Other
    }

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly float _iconSize;
    private readonly Ui1TextRole _textRole;
    private readonly string _cardsLabel;
    private readonly string _potionsLabel;
    private readonly string _relicsLabel;
    private readonly string _resultsLabel;
    private readonly string _groupShortFormat;
    private const int MiniCardGap = 6;

    public NeowCompactEffectList(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost,
        float iconSize,
        Ui1TextRole textRole,
        string cardsLabel,
        string potionsLabel,
        string relicsLabel,
        string resultsLabel,
        string groupShortFormat)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        _iconSize = iconSize;
        _textRole = textRole;
        _cardsLabel = cardsLabel;
        _potionsLabel = potionsLabel;
        _relicsLabel = relicsLabel;
        _resultsLabel = resultsLabel;
        _groupShortFormat = groupShortFormat;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 7);
        Visible = false;
    }

    public void Bind(
        IReadOnlyList<PredictedEffectGroupViewModel> groups,
        string missingIconTooltip,
        bool showGroupLabels = false)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        PredictedEffectGroupViewModel[] visibleGroups = groups
            .OrderBy(group => group.GroupOrder)
            .Where(group => group.ShowInNormalMode)
            .Where(group => group.OrderedItems.Any(item => item.ShowInNormalMode))
            .ToArray();
        Visible = visibleGroups.Length > 0;
        if (!Visible)
        {
            return;
        }

        for (int index = 0; index < visibleGroups.Length; index++)
        {
            AddChild(BuildGroup(
                visibleGroups[index],
                index,
                missingIconTooltip,
                showGroupLabels));
        }
    }

    private Control BuildGroup(
        PredictedEffectGroupViewModel group,
        int visibleGroupIndex,
        string missingIconTooltip,
        bool showGroupLabel)
    {
        PredictedEffectItemViewModel[] items = group.OrderedItems
            .OrderBy(item => item.ItemOrder)
            .Where(item => item.ShowInNormalMode)
            .ToArray();

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        column.AddThemeConstantOverride("separation", 4);

        if (showGroupLabel)
        {
            Label groupLabel = Ui1Theme.Label(
                string.Format(_groupShortFormat, visibleGroupIndex + 1),
                Ui1TextRole.Muted,
                wrap: false);
            groupLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            groupLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            column.AddChild(groupLabel);
        }

        Label groupHeading = Ui1Theme.Label(
            string.IsNullOrWhiteSpace(group.DisplayLabel)
                ? string.Format(_groupShortFormat, visibleGroupIndex + 1)
                : group.DisplayLabel,
            Ui1TextRole.Muted,
            wrap: false);
        groupHeading.MouseFilter = Control.MouseFilterEnum.Ignore;
        groupHeading.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        column.AddChild(groupHeading);

        var resultRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        resultRow.AddThemeConstantOverride("separation", MiniCardGap);
        foreach (PredictedEffectItemViewModel item in items)
        {
            resultRow.AddChild(BuildItem(item, missingIconTooltip));
        }
        column.AddChild(resultRow);

        return column;
    }

    private Control BuildItem(PredictedEffectItemViewModel item, string missingIconTooltip)
    {
        string visibleText = string.IsNullOrWhiteSpace(item.CompactDisplayText)
            ? item.DisplayText
            : item.CompactDisplayText;

        if (item.TargetContent is { } target)
        {
            return BuildAnchoredContentIdentity(target, visibleText, missingIconTooltip);
        }

        // Text-only predicted facts are atomic. The parent typed row owns layout;
        // proprietary/game object names are never character-wrapped here.
        Label text = Ui1Theme.Label(visibleText, _textRole, wrap: false);
        text.CustomMinimumSize = new Vector2(0f, _iconSize);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.ClipText = true;
        text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        text.MouseFilter = Control.MouseFilterEnum.Ignore;
        return text;
    }

    private Control BuildAnchoredContentIdentity(
        GameContentDisplayViewModel target,
        string visibleText,
        string missingIconTooltip)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            SizeFlagsStretchRatio = 1f,
            Alignment = BoxContainer.AlignmentMode.Begin
        };
        row.AddThemeConstantOverride("separation", 6);

        IconDescriptor descriptor = _icons.Resolve(target.ModelKey, target.ContentKind, IconVariant.Small);
        var iconHost = new Control
        {
            CustomMinimumSize = new Vector2(_iconSize, _iconSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        var texture = new TextureRect
        {
            Texture = descriptor.Texture,
            Visible = !descriptor.IsMissing && descriptor.Texture is not null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        texture.OffsetLeft = 3f;
        texture.OffsetTop = 3f;
        texture.OffsetRight = -3f;
        texture.OffsetBottom = -3f;

        Label missing = Ui1Theme.Label(texture.Visible ? string.Empty : "?", Ui1TextRole.Muted, wrap: false);
        missing.HorizontalAlignment = HorizontalAlignment.Center;
        missing.VerticalAlignment = VerticalAlignment.Center;
        missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        missing.TooltipText = texture.Visible ? string.Empty : missingIconTooltip;
        missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        iconHost.AddChild(texture);
        iconHost.AddChild(missing);

        iconHost.MouseEntered += () =>
            _tooltipHost.ShowFor(iconHost, target.ModelKey, target.ContentKind, target.DisplayName);
        iconHost.MouseExited += () => _tooltipHost.Dismiss(iconHost);
        iconHost.TreeExiting += () => _tooltipHost.Dismiss(iconHost);

        Label name = Ui1Theme.Label(visibleText, _textRole, wrap: false);
        name.CustomMinimumSize = new Vector2(64f, _iconSize);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(iconHost);
        row.AddChild(name);
        return row;
    }

    private sealed partial class ResponsiveSummaryCardRow : HBoxContainer
    {
        private readonly int _gap;
        private readonly List<PredictorMiniCard> _cards = new();

        public ResponsiveSummaryCardRow(int gap)
        {
            _gap = gap;
            AddThemeConstantOverride("separation", gap);
            Resized += RefreshSizing;
        }

        public void AddResult(Control result)
        {
            AddChild(result);
            if (result is PredictorMiniCard card)
            {
                _cards.Add(card);
            }
            RefreshSizing();
        }

        private void RefreshSizing()
        {
            if (_cards.Count == 0 || Size.X <= 1f)
            {
                return;
            }

            // Neow summary rows share Combat Reward's responsive card geometry.
            // One/two-card rows use a standard three-card slot as their reference,
            // while three/four-card rows consume the actual row width.
            int layoutCount = Math.Max(3, _cards.Count);
            float gaps = _gap * Math.Max(0, layoutCount - 1);
            float perCard = Math.Max(
                PredictorMiniCard.MinimumResponsiveWidth,
                (Size.X - gaps) / layoutCount);
            foreach (PredictorMiniCard card in _cards)
            {
                card.SetResponsiveWidth(perCard);
            }
        }
    }

    private static ResultType Classify(PredictedEffectItemViewModel item) => item.TargetContent?.ContentKind switch
    {
        GameContentKind.Card => ResultType.Card,
        GameContentKind.Potion => ResultType.Potion,
        GameContentKind.Relic => ResultType.Relic,
        _ => ResultType.Other
    };

    private static int TypeOrder(ResultType type) => type switch
    {
        ResultType.Card => 0,
        ResultType.Potion => 1,
        ResultType.Relic => 2,
        _ => 3
    };

    private string TypeLabel(ResultType type) => type switch
    {
        ResultType.Card => _cardsLabel,
        ResultType.Potion => _potionsLabel,
        ResultType.Relic => _relicsLabel,
        _ => _resultsLabel
    };
}
