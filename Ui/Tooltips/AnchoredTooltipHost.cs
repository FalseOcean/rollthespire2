using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Tooltips;

internal sealed partial class AnchoredTooltipHost : Control
{
    private const float AnchorGap = 16f;
    private const float ViewportMargin = 8f;
    private const float DetailedTooltipWidth = 380f;
    private const float ShortTextTooltipWidth = 180f;
    private const float TooltipPanelMargin = 12f;

    private readonly IRelicTooltipResolver _relicResolver;
    private readonly IPotionTooltipResolver _potionResolver;
    private readonly ICardTooltipResolver _cardResolver;
    private readonly PanelContainer _panel;
    private readonly Label _title;
    private readonly Label _metadata;
    private readonly MarginContainer _rulesMargin;
    private readonly MarginContainer _customMargin;
    private readonly Label _description;
    private Control? _anchor;
    private int _showGeneration;
    private float _pendingPreferredWidth = DetailedTooltipWidth;

    public AnchoredTooltipHost(
        IRelicTooltipResolver relicResolver,
        IPotionTooltipResolver potionResolver,
        ICardTooltipResolver cardResolver)
    {
        _relicResolver = relicResolver ?? throw new ArgumentNullException(nameof(relicResolver));
        _potionResolver = potionResolver ?? throw new ArgumentNullException(nameof(potionResolver));
        _cardResolver = cardResolver ?? throw new ArgumentNullException(nameof(cardResolver));
        Name = "AnchoredContentTooltipHost";
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = false;
        ZIndex = UiZLayers.Tooltip;

        _panel = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(DetailedTooltipWidth, 0f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        // This is a manually positioned child of the full-rect tooltip host. Keep
        // its anchors explicitly at top-left so no parent/previous layout state can
        // stretch the tooltip to the viewport height.
        _panel.AnchorLeft = 0f;
        _panel.AnchorTop = 0f;
        _panel.AnchorRight = 0f;
        _panel.AnchorBottom = 0f;
        Ui1Theme.ApplyPanel(_panel, Ui1SurfaceRole.CardElevated, 4f, 1, TooltipPanelMargin);

        var content = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 4);

        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.CardTitle, true);
        _title.MouseFilter = MouseFilterEnum.Ignore;
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _metadata = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _metadata.MouseFilter = MouseFilterEnum.Ignore;
        _metadata.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _metadata.Visible = false;

        _rulesMargin = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };
        _rulesMargin.AddThemeConstantOverride("margin_top", 6);

        _description = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        _description.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        _description.MouseFilter = MouseFilterEnum.Ignore;
        _description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rulesMargin.AddChild(_description);

        content.AddChild(_title);
        content.AddChild(_metadata);
        content.AddChild(_rulesMargin);
        _customMargin = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };
        _customMargin.AddThemeConstantOverride("margin_top", 6);
        content.AddChild(_customMargin);
        _panel.AddChild(content);
        AddChild(_panel);
    }

    /// <summary>Compatibility overload for existing relic-only callers.</summary>
    public void ShowFor(Control anchor, ModelKey relicKey, string fallbackTitle) =>
        ShowFor(anchor, relicKey, GameContentKind.Relic, fallbackTitle);

    public void ShowFor(
        Control anchor,
        ModelKey contentKey,
        GameContentKind contentKind,
        string fallbackTitle)
    {
        if (!IsUsableAnchor(anchor))
        {
            Dismiss();
            return;
        }

        string title = string.IsNullOrWhiteSpace(fallbackTitle)
            ? contentKey.Entry
            : fallbackTitle;

        try
        {
            switch (contentKind)
            {
                case GameContentKind.Relic:
                {
                    RelicTooltipSnapshot snapshot = _relicResolver.Resolve(contentKey, title);
                    ShowResolved(anchor, snapshot.Title, string.Empty,
                        snapshot.HasOfficialDescription ? snapshot.Description : string.Empty);
                    return;
                }
                case GameContentKind.Potion:
                {
                    PotionTooltipSnapshot snapshot = _potionResolver.Resolve(contentKey, title);
                    ShowResolved(anchor, snapshot.Title, string.Empty,
                        snapshot.HasOfficialDescription ? snapshot.Description : string.Empty);
                    return;
                }
                case GameContentKind.Card:
                {
                    ShowResolvedCard(anchor, _cardResolver.Resolve(contentKey, title));
                    return;
                }
            }
        }
        catch
        {
            // Picker tooltips are presentation-only. Runtime text failures never
            // affect candidate visibility, selection or search semantics.
        }

        ShowResolved(anchor, title, string.Empty, string.Empty);
    }

    public void ShowRelicWithCondition(Control anchor, ModelKey relicKey, string fallbackTitle,
        string conditionTitle, string condition)
    {
        if (!IsUsableAnchor(anchor)) { Dismiss(); return; }
        string title = string.IsNullOrWhiteSpace(fallbackTitle) ? relicKey.Entry : fallbackTitle;
        string description = string.Empty;
        try
        {
            RelicTooltipSnapshot snapshot = _relicResolver.Resolve(relicKey, title);
            title = snapshot.Title;
            if (snapshot.HasOfficialDescription) description = snapshot.Description;
        }
        catch { /* The premise remains visible if runtime relic text is unavailable. */ }
        string premise = string.IsNullOrWhiteSpace(conditionTitle) ? condition : $"{conditionTitle}\n{condition}";
        ShowResolved(anchor, title, string.Empty,
            string.IsNullOrWhiteSpace(description) ? premise : $"{description}\n\n{premise}");
    }

    public void ShowCardFor(
        Control anchor,
        ModelKey cardKey,
        string fallbackTitle)
    {
        if (!IsUsableAnchor(anchor))
        {
            Dismiss();
            return;
        }

        CardTooltipSnapshot snapshot;
        try
        {
            snapshot = _cardResolver.Resolve(cardKey, fallbackTitle);
        }
        catch
        {
            snapshot = new CardTooltipSnapshot(
                cardKey,
                fallbackTitle,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                "CardTooltipFallback;KeywordHoverTipRejected",
                string.Empty);
        }

        ShowResolvedCard(anchor, snapshot);
    }

    public void ShowText(Control anchor, string title, string description = "") =>
        ShowResolved(anchor, title, string.Empty, description, ShortTextTooltipWidth);

    /// <summary>
    /// Shows the project-standard anchored tooltip with a title, a short metadata/section line,
    /// and a player-facing description. This is presentation-only and does not affect selection state.
    /// </summary>
    public void ShowStructuredText(
        Control anchor,
        string title,
        string metadata,
        string description) =>
        ShowResolved(anchor, title, metadata, description, DetailedTooltipWidth);

    public void ShowStructuredContent(
        Control anchor,
        string title,
        string metadata,
        string description,
        Control content)
    {
        ShowResolved(anchor, title, metadata, description, DetailedTooltipWidth, content);
    }

    private void ShowResolvedCard(Control anchor, CardTooltipSnapshot snapshot)
    {
        string metadata = string.Join(
            " · ",
            new[]
            {
                snapshot.PoolOrCharacterName,
                snapshot.RarityName,
                snapshot.CardTypeName
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        ShowResolved(anchor, snapshot.DisplayName, metadata, snapshot.RulesText);
    }

    private void ShowResolved(
        Control anchor,
        string title,
        string metadata,
        string description,
        float preferredWidth = DetailedTooltipWidth,
        Control? customContent = null)
    {
        if (!IsUsableAnchor(anchor))
        {
            Dismiss();
            return;
        }

        _anchor = anchor;
        _pendingPreferredWidth = preferredWidth;
        int generation = ++_showGeneration;
        ApplyStableWrapWidth(preferredWidth);
        _title.Text = title;
        _metadata.Text = metadata;
        _metadata.Visible = !string.IsNullOrWhiteSpace(metadata);
        _description.Text = description;
        _rulesMargin.Visible = !string.IsNullOrWhiteSpace(description);
        foreach (Node child in _customMargin.GetChildren())
        {
            _customMargin.RemoveChild(child);
            child.QueueFree();
        }
        if (customContent is not null)
        {
            _customMargin.AddChild(customContent);
            _customMargin.Visible = true;
        }
        else
        {
            _customMargin.Visible = false;
        }

        // Autowrapped Label minimum height is width-dependent. Calling ResetSize()
        // before the child labels have a concrete width can measure them at an
        // effectively zero-width layout and cache a viewport-sized/tall minimum.
        // Instead, pin the wrap width first, show transparently, and give the two
        // nested Containers time to lay out at that width. Only then do we read the
        // final combined minimum height. Never inherit a previous tooltip Y size.
        _panel.Visible = true;
        _panel.Modulate = new Color(1f, 1f, 1f, 0f);
        _panel.Size = new Vector2(preferredWidth, 1f);
        Callable.From(() => PrepareFirstShowLayout(generation)).CallDeferred();
    }

    private void ApplyStableWrapWidth(float preferredWidth)
    {
        float contentWidth = Math.Max(1f, preferredWidth - TooltipPanelMargin * 2f);
        _panel.CustomMinimumSize = new Vector2(preferredWidth, 0f);

        // These labels all autowrap. Giving them the real inner width up front makes
        // their minimum-height calculation deterministic even before the first
        // visible layout pass. The VBox/PanelContainer still owns the final height.
        _title.CustomMinimumSize = new Vector2(contentWidth, 0f);
        _metadata.CustomMinimumSize = new Vector2(contentWidth, 0f);
        _description.CustomMinimumSize = new Vector2(contentWidth, 0f);
    }

    private void PrepareFirstShowLayout(int generation)
    {
        if (generation != _showGeneration || _anchor is null || !_panel.Visible || !IsUsableAnchor(_anchor))
        {
            return;
        }

        ApplyStableWrapWidth(_pendingPreferredWidth);
        _panel.Size = new Vector2(_pendingPreferredWidth, 1f);
        Callable.From(() => PresentAfterFirstShowLayout(generation)).CallDeferred();
    }

    private void PresentAfterFirstShowLayout(int generation)
    {
        if (generation != _showGeneration || _anchor is null || !_panel.Visible || !IsUsableAnchor(_anchor))
        {
            return;
        }

        Vector2 minimum = _panel.GetCombinedMinimumSize();
        _panel.Size = new Vector2(
            _customMargin.Visible ? Math.Max(_pendingPreferredWidth, minimum.X) : _pendingPreferredWidth,
            Math.Max(1f, minimum.Y));
        RepositionCurrent();
        _panel.Modulate = Colors.White;
    }

    private static bool IsUsableAnchor(Control anchor) =>
        GodotObject.IsInstanceValid(anchor) && anchor.IsInsideTree();

    public void Dismiss(Control? owner = null)
    {
        if (owner is not null && !ReferenceEquals(owner, _anchor))
        {
            return;
        }

        _showGeneration++;
        _panel.Visible = false;
        _panel.Modulate = Colors.White;
        // Do not retain a bad first-layout height across hover sessions.
        _panel.Size = new Vector2(_pendingPreferredWidth, 1f);
        _anchor = null;
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible)
        {
            return;
        }

        if (!IsAnchorStillHovered())
        {
            Dismiss();
            return;
        }

        RepositionCurrent();
    }

    public override void _ExitTree()
    {
        Dismiss();
    }

    private bool IsAnchorStillHovered()
    {
        if (_anchor is null ||
            !GodotObject.IsInstanceValid(_anchor) ||
            !_anchor.IsInsideTree() ||
            !_anchor.IsVisibleInTree())
        {
            return false;
        }

        Vector2 pointer = GetViewport().GetMousePosition();
        if (!_anchor.GetGlobalRect().HasPoint(pointer))
        {
            return false;
        }

        Node? current = _anchor.GetParent();
        while (current is Control control)
        {
            if (control.ClipContents && !control.GetGlobalRect().HasPoint(pointer))
            {
                return false;
            }
            current = current.GetParent();
        }

        return true;
    }

    private void RepositionCurrent()
    {
        if (_anchor is null || !_panel.Visible || !GodotObject.IsInstanceValid(_anchor))
        {
            return;
        }

        Rect2 anchorRect = _anchor.GetGlobalRect();
        Rect2 viewportRect = GetViewport().GetVisibleRect();
        Rect2 hostRect = GetGlobalRect();
        Vector2 tooltipSize = _panel.Size;
        if (tooltipSize.X <= 0f || tooltipSize.Y <= 0f)
        {
            tooltipSize = _panel.GetCombinedMinimumSize();
            _panel.Size = tooltipSize;
        }

        // The workspace shell clips its children even when the game viewport is
        // wider. Keep the tooltip within this host, not merely within the viewport.
        float visibleLeft = Math.Max(viewportRect.Position.X, hostRect.Position.X);
        float visibleTop = Math.Max(viewportRect.Position.Y, hostRect.Position.Y);
        float visibleRight = Math.Min(viewportRect.End.X, hostRect.End.X);
        float visibleBottom = Math.Min(viewportRect.End.Y, hostRect.End.Y);
        float rightX = anchorRect.End.X + AnchorGap;
        float leftX = anchorRect.Position.X - AnchorGap - tooltipSize.X;
        float x = rightX + tooltipSize.X <= visibleRight - ViewportMargin
            ? rightX
            : leftX;
        float y = anchorRect.Position.Y + (anchorRect.Size.Y - tooltipSize.Y) * 0.5f;

        float minX = visibleLeft + ViewportMargin;
        float minY = visibleTop + ViewportMargin;
        float maxX = Math.Max(minX, visibleRight - tooltipSize.X - ViewportMargin);
        float maxY = Math.Max(minY, visibleBottom - tooltipSize.Y - ViewportMargin);
        _panel.GlobalPosition = new Vector2(
            Math.Clamp(x, minX, maxX),
            Math.Clamp(y, minY, maxY));
    }
}
