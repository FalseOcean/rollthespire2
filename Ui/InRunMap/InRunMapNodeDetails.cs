using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Core.PredictorRuntime;
using System.Collections.Immutable;

namespace RolltheSpire2.Ui.InRunMap;

// Display data only. The adapter supplies all knowledge, available choices,
// localized labels and opaque action IDs; the view never predicts or settles.
internal sealed record InRunMapDetailObject(ModelKey Key, GameContentKind Kind,
    int UpgradeLevel = 0, string? DisplayName = null)
{
    internal PredictorCard? CardSnapshot { get; init; }
}
internal sealed record InRunMapDetailChoice(string Id, string Label,
    InRunMapDetailObject? Object = null, bool Enabled = true, bool Selected = false)
{
    internal OperationPreview? Preview { get; init; }
    internal Func<OperationPreview?>? PreviewFactory { get; init; }
    internal string? PreviewKey { get; init; }
    internal int? Price { get; init; }
    internal string? Status { get; init; }
}
internal sealed record InRunMapDetailSection(string Title, string Text,
    IReadOnlyList<InRunMapDetailObject> Objects, IReadOnlyList<InRunMapDetailChoice> Choices)
{
    internal bool RewardActionsRow { get; init; }
    internal int CardColumns { get; init; }
    internal InRunMapDetailChoice? HeaderChoice { get; init; }
    internal InRunMapNodeEditorData? EmbeddedEditor { get; init; }
    internal bool CollapsedByDefault { get; init; }
    internal string? FoldoutId { get; init; }
    internal bool BranchRow { get; init; }
    internal string? StableId { get; init; }
    internal OperationPreview? OutcomePreview { get; init; }
    internal bool CombatPanel { get; init; }
    internal bool GainReward { get; init; }
    internal bool ItemFlow { get; init; }
    internal bool PlannedActionsRow { get; init; }
    internal bool LargeCardResults { get; init; }
}
internal sealed record InRunMapNodeDetailData(string NodeId, string Title, string Subtitle,
    IReadOnlyList<string> Information, IReadOnlyList<InRunMapDetailSection> Sections,
    IReadOnlyList<InRunMapDetailChoice> EditorActions, InRunMapDetailObject? HeaderObject = null,
    InRunMapNodeEditorData? Premises = null, string PremisesLabel = "")
{
    internal string? DiagnosticsLabel { get; init; }
    internal Func<bool>? IsCurrent { get; init; }
    internal string? PrerequisiteSummary { get; init; }
}
internal sealed record InRunMapDetailNumber(string Id, string Label, double Value,
    double Minimum, double Maximum, double Step = 1, bool Enabled = true);
internal sealed record InRunMapDetailToggle(string Id, string Label, bool Value, bool Enabled = true);
internal sealed record InRunMapDetailSelectOption(string Id, string Label, bool Enabled = true);
internal sealed record InRunMapDetailSelect(string Id, string Label, string? Value,
    IReadOnlyList<InRunMapDetailSelectOption> Options, bool Enabled = true);
internal sealed record InRunMapDetailFieldValue(double? Number = null, bool? Toggle = null, string? Selected = null);
internal sealed record InRunMapNodeEditorData(string NodeId, string Title,
    IReadOnlyList<InRunMapDetailSection> Sections, IReadOnlyList<InRunMapDetailNumber> Numbers,
    IReadOnlyList<InRunMapDetailToggle> Toggles, IReadOnlyList<InRunMapDetailSelect> Selections, string CloseLabel)
{
    internal long ScrollToTopRequest { get; init; }
}

internal sealed partial class InRunMapNodeDetails : Control
{
    private const float Gap = 10, Margin = 12;
    private const float ShopOfferWidth = 200, ShopOfferGap = 8;
    private const int ShopOfferColumns = 3;
    private readonly IGameIconResolver _icons;
    private IGameContentNameResolver _names;
    private readonly WorkspacePalette _palette = WorkspacePalette.Canonical;
    private readonly Control _hover;
    private readonly PanelContainer _surface, _editor;
    private readonly NinePatchRect _frame, _frameShadow;
    private readonly ColorRect _editorShield;
    private InRunMapNodeDetailData? _data;
    private string? _editorNode;
    private Rect2 _nodeGlobal, _viewportGlobal;
    private long _hoverGeneration, _editorGeneration;
    private long _editorScrollRequest;
    private bool _drawingSuppressed;
    private bool _premisesExpanded;
    private readonly HashSet<string> _expandedSections = [];
    private bool? _hoverLeft;
    private float? _hoverNaturalWidth;
    private ScrollContainer? _hoverScroll;
    private ScrollContainer? _editorScroll;
    private VBoxContainer? _hoverBody;
    private VBoxContainer? _hoverRoot;
    private ScrollContainer? _prerequisiteScroll;
    private VBoxContainer? _prerequisiteBody;
    private ulong _hoverOutsideSince;
    private string? _dismissedNode;
    private Rect2 _dismissedRegion;
    private readonly List<HBoxContainer> _rewardRows = [];
    private readonly List<(Control Host, NeowOperationOverlay Preview)> _embeddedPreviews = [];
    private readonly List<PopupMenu> _hoverPopups = [];
    private readonly NeowOperationOverlay _optionPreview = new() { Name = "MapEventOptionPreview", Visible = false, ExternallyFramed = true, MapCardPresentation = true, MapSummaryOnly = true };
    private readonly NeowOperationOverlay _optionCardPreview = new() { Name = "MapEventOptionCards", Visible = false, ExternallyFramed = true, MapCardPresentation = true };
    private OperationPreview? _renderedOptionPreview;
    private Control? _previewAnchor;
    private string? _previewOptionKey;
    private readonly Dictionary<string, (Control Anchor, Func<OperationPreview?> Preview)> _previewChoices = [];
    private readonly ScrollContainer _previewViewport;
    private readonly NinePatchRect _previewFrame, _previewShadow;
    private readonly Control _previewContent;
    private readonly ScrollContainer _previewCardsViewport;
    private readonly Control _previewCardsContent;
    private (Control Anchor, Func<OperationPreview?> Resolve, string Key, long Generation, ulong Since)? _pendingPreview;

    internal event Action<string, string>? ChoiceRequested;
    internal event Action<string, string>? EditorRequested;
    internal event Action<string, string, InRunMapDetailFieldValue>? FieldChanged;
    internal event Action? HoverClosed;
    internal event Action? EditorClosed;
    internal bool HoverOpen => _hover.Visible;
    internal bool EditorOpen => _editor.Visible;
    internal string? HoverNodeId => HoverOpen ? _data?.NodeId : null;
    internal string? EditorNodeId => EditorOpen ? _editorNode : null;
    internal Rect2 HoverGlobalRegion => _hover.GetGlobalRect();
    internal bool HoverSurfaceContains(Vector2 global) => HoverOpen && (_surface.GetGlobalRect().HasPoint(global) || HoverPopupOpen ||
        CombatSurfaceContains(global) || RewardCardDockContains(global) || _optionPreview.Visible &&
        (_previewViewport.IsVisibleInTree() && _previewViewport.GetGlobalRect().HasPoint(global) ||
         _previewCardsViewport.IsVisibleInTree() && _previewCardsViewport.GetGlobalRect().HasPoint(global)));

    internal void RebindLocalization(IGameContentNameResolver names) => _names = names;

    internal InRunMapNodeDetails(IGameIconResolver icons, IGameContentNameResolver names)
    {
        _icons = icons; _names = names;
        Name = "MapNodeDetails"; MouseFilter = MouseFilterEnum.Ignore; ZIndex = 80;
        InitializeSourceMarker();
        _hover = new Control { Name = "ContinuousHoverRegion", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _hover.GuiInput += ConsumeMouse;
        AddChild(_hover);
        _frameShadow = NativeNightSurface.Backdrop(shadow: true); _hover.AddChild(_frameShadow);
        _frame = NativeNightSurface.Backdrop(); _frame.Name = "NodeNightFrame"; _hover.AddChild(_frame);
        _surface = Surface("CompactNodeHover"); _hover.AddChild(_surface);
        _surface.AddThemeStyleboxOverride("panel", new StyleBoxEmpty {
            ContentMarginLeft = 16, ContentMarginRight = 20, ContentMarginTop = 14, ContentMarginBottom = 17 });
        _previewViewport = new ScrollContainer { Name = "NodeOptionPreviewViewport", Visible = false,
            MouseFilter = MouseFilterEnum.Stop, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, ZIndex = 100 };
        _previewContent = new Control { Name = "NodeOptionPreviewContent", MouseFilter = MouseFilterEnum.Ignore };
        _previewFrame = NativeNightSurface.Backdrop(); _previewFrame.Name = "NodeOptionNightFrame"; _previewFrame.ZIndex = 100; _previewFrame.Hide();
        _previewShadow = NativeNightSurface.Backdrop(shadow: true); _previewShadow.ZIndex = 100; _previewShadow.Hide();
        // The text backing belongs to the scrolling summary, so it cannot
        // remain behind card art after a long result is scrolled away.
        _previewContent.AddChild(_previewShadow); _previewContent.AddChild(_previewFrame);
        _previewViewport.AddChild(_previewContent); _previewContent.AddChild(_optionPreview); AddChild(_previewViewport);
        _previewCardsViewport = new ScrollContainer { Name = "NodeOptionCardsViewport", Visible = false,
            MouseFilter = MouseFilterEnum.Stop, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, ClipContents = true, ZIndex = 100 };
        _previewCardsContent = new Control { Name = "NodeOptionCardsContent", MouseFilter = MouseFilterEnum.Ignore };
        _previewCardsViewport.AddChild(_previewCardsContent); _previewCardsContent.AddChild(_optionCardPreview); AddChild(_previewCardsViewport);
        _editorShield = new ColorRect { Name = "EditorInputShield", Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _editorShield.GuiInput += ConsumeMouse;
        AddChild(_editorShield);
        _editor = Surface("PersistentEditor"); _editor.Visible = false; AddChild(_editor);
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _editorShield.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    internal void ShowNode(InRunMapNodeDetailData data, Rect2 nodeGlobalRect, Rect2 viewportGlobalRect, CanvasItem? sourceVisual = null)
    {
        if (_drawingSuppressed || EditorOpen) return;
        if (data.IsCurrent?.Invoke() == false) { CloseHover(); return; }
        bool rebuild = !ReferenceEquals(_data, data) || !HoverOpen;
        bool sameNode = _data?.NodeId == data.NodeId && HoverOpen;
        if (!sameNode)
        {
            _premisesExpanded = false; _expandedSections.Clear(); _hoverLeft = null; _hoverOffset = null;
            _cardsBelow = _informationAbove = false;
            _hoverNaturalWidth = null; _hoverOutsideSince = 0; CloseOptionPreview();
        }
        _data = data; _nodeGlobal = nodeGlobalRect; _viewportGlobal = viewportGlobalRect;
        if (rebuild)
        {
            int scroll = sameNode ? _hoverScroll?.ScrollVertical ?? 0 : 0;
            BuildHover(data);
            long generation = _hoverGeneration;
            Callable.From(() =>
            {
                if (HoverOpen && generation == _hoverGeneration && _hoverScroll != null)
                    _hoverScroll.ScrollVertical = scroll;
            }).CallDeferred();
        }
        _hover.Show(); LayoutHover();
    }
    internal void UpdateAnchor(Rect2 nodeGlobalRect, Rect2 viewportGlobalRect)
    {
        _nodeGlobal = nodeGlobalRect; _viewportGlobal = viewportGlobalRect;
        if (HoverOpen) LayoutHover();
        if (EditorOpen) LayoutEditor();
    }
    internal void SetDrawingSuppressed(bool suppressed)
    {
        _drawingSuppressed = suppressed;
        if (suppressed) CloseHover();
    }
    internal void CloseHover()
    {
        if (!HoverOpen) return;
        foreach (var popup in _hoverPopups.Where(IsInstanceValid)) popup.Hide();
        CloseOptionPreview();
        HideRewardCardDock();
        _combatPanel?.Hide();
        _hover.Hide(); _sourceMarker.Hide(); _hoverGeneration++; QueueRedraw(); HoverClosed?.Invoke();
        _hoverOutsideSince = 0;
    }
    internal bool ContainsPointer(Vector2 global) =>
        HoverOpen && (_nodeGlobal.Grow(4).HasPoint(global) || HoverSurfaceContains(global) ||
            ShortConnectionContains(_nodeGlobal, _surface.GetGlobalRect(), global) ||
            AuxiliaryConnectionContains(global) || PreviewContains(global)) ||
        EditorOpen && _editor.GetGlobalRect().HasPoint(global);
    internal bool BlocksMapInput(Vector2 global) => EditorOpen || HoverSurfaceContains(global);
    internal bool CanOpenNode(string nodeId) => _dismissedNode != nodeId;
    private bool HoverPopupOpen => _hoverPopups.Any(popup => IsInstanceValid(popup) && popup.Visible);

    public override void _Process(double delta)
    {
        if (_dismissedNode != null && !_dismissedRegion.HasPoint(GetGlobalMousePosition())) _dismissedNode = null;
        if (HoverOpen)
        {
            if (_data?.IsCurrent?.Invoke() == false) { CloseHover(); return; }
            LayoutHover();
            var mouse = GetGlobalMousePosition();
            if (_pendingPreview is { } pending)
            {
                if (pending.Generation != _hoverGeneration || !IsInstanceValid(pending.Anchor) ||
                    !pending.Anchor.IsVisibleInTree() || !pending.Anchor.GetGlobalRect().HasPoint(mouse)) _pendingPreview = null;
                else if (Time.GetTicksMsec() - pending.Since >= 140)
                {
                    _pendingPreview = null;
                    if (pending.Resolve() is { } preview)
                    { ShowOptionPreview(pending.Anchor, preview); _previewOptionKey = pending.Key; }
                    else CloseOptionPreview();
                }
            }
            if (_drawingSuppressed) CloseHover();
            else if (!ContainsPointer(mouse))
            {
                if (_hoverOutsideSince == 0) _hoverOutsideSince = Time.GetTicksMsec();
                else if (Time.GetTicksMsec() - _hoverOutsideSince >= 90) CloseHover();
            }
            else
            {
                _hoverOutsideSince = 0;
                if (_optionPreview.Visible && _pendingPreview == null && !PreviewContains(mouse)) CloseOptionPreview();
            }
        }
        if (EditorOpen) LayoutEditor();
    }
    private void DismissHover()
    {
        _dismissedNode = HoverNodeId; _dismissedRegion = _nodeGlobal.Grow(8); CloseHover();
    }
    public override void _Input(InputEvent input)
    {
        if (!HoverOpen) return;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { DismissHover(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } mouse &&
            !HoverSurfaceContains(mouse.GlobalPosition) && !_nodeGlobal.HasPoint(mouse.GlobalPosition)) DismissHover();
    }
    private void ConsumeMouse(InputEvent input)
    {
        if (input is InputEventMouseButton or InputEventMouseMotion) GetViewport().SetInputAsHandled();
    }
    private Rect2 PreviewSourceRegion => HasCombatSurface && _previewAnchor != null && _combatSurface!.IsAncestorOf(_previewAnchor)
        ? _combatSurface.GetGlobalRect() : _surface.GetGlobalRect();
    private bool PreviewContains(Vector2 global) => _optionPreview.Visible &&
        (PreviewSourceRegion.HasPoint(global) ||
            _surface.GetGlobalRect().HasPoint(global) || CombatSurfaceContains(global) || AuxiliaryConnectionContains(global) ||
            _previewViewport.IsVisibleInTree() && _previewViewport.GetGlobalRect().HasPoint(global) ||
            _previewCardsViewport.IsVisibleInTree() && _previewCardsViewport.GetGlobalRect().HasPoint(global));

    // Keep only the small gap between facing edges interactive. A bounding
    // rectangle would retain unrelated map space above/below a tall panel.
    private static bool ShortConnectionContains(Rect2 source, Rect2 target, Vector2 point)
    {
        Vector2 a, b;
        if (source.End.X <= target.Position.X || target.End.X <= source.Position.X)
        {
            bool right = source.End.X <= target.Position.X;
            float top = Math.Max(source.Position.Y, target.Position.Y), bottom = Math.Min(source.End.Y, target.End.Y);
            float y = top <= bottom ? Math.Clamp(point.Y, top, bottom) :
                source.GetCenter().Y < target.GetCenter().Y ? source.End.Y : source.Position.Y;
            a = new(right ? source.End.X : source.Position.X, y);
            b = new(right ? target.Position.X : target.End.X, Math.Clamp(y, target.Position.Y, target.End.Y));
        }
        else
        {
            bool down = source.GetCenter().Y <= target.GetCenter().Y;
            float x = Math.Clamp(point.X, Math.Max(source.Position.X, target.Position.X), Math.Min(source.End.X, target.End.X));
            a = new(x, down ? source.End.Y : source.Position.Y);
            b = new(x, down ? target.Position.Y : target.End.Y);
        }
        var segment = b - a;
        if (segment.LengthSquared() > 80 * 80) return false;
        float t = segment.LengthSquared() == 0 ? 0 : Math.Clamp((point - a).Dot(segment) / segment.LengthSquared(), 0, 1);
        return point.DistanceSquaredTo(a + segment * t) <= 10 * 10;
    }
    private void CloseOptionPreview()
    {
        _optionPreview.Hide(); _previewViewport.Hide(); _previewAnchor = null; _previewOptionKey = null;
        _optionCardPreview.Hide(); _previewCardsViewport.Hide();
        _previewFrame.Hide(); _previewShadow.Hide();
        _pendingPreview = null;
        _optionPreview.CardScaleOverride = null;
        _optionCardPreview.CardScaleOverride = null;
    }
    private void ShowOptionPreview(Control anchor, OperationPreview preview)
    {
        CloseOptionPreview();
        if (_data?.IsCurrent?.Invoke() == false) { CloseHover(); return; }
        if (!NeowOperationOverlay.HasVisibleContent(preview)) return;
        _optionPreview.CardScaleOverride = RewardDockCardScale;
        _optionCardPreview.CardScaleOverride = RewardDockCardScale;
        _previewAnchor = anchor; _previewOptionKey = preview.OptionKey;
        if (!ReferenceEquals(_renderedOptionPreview, preview))
        { _optionPreview.SetPreview(preview); _optionCardPreview.SetPreview(preview); _renderedOptionPreview = preview; }
        HideRewardCardDock();
        _optionPreview.Show(); _previewViewport.Show(); _optionCardPreview.Show();
        _previewViewport.ScrollHorizontal = _previewViewport.ScrollVertical = 0;
        _previewCardsViewport.ScrollHorizontal = _previewCardsViewport.ScrollVertical = 0;
        LayoutHover();
    }
    private void LayoutOptionPreview()
    {
        if (_optionPreview.Visible) LayoutAuxiliarySurfaces(VisibleViewportLocal(), new Rect2(_hover.Position, _hover.Size));
    }
    private void ApplyOptionPreviewLayout(Rect2 summaryArea, Rect2 cardArea)
    {
        float width = summaryArea.Size.X;
        var summary = _optionPreview.SummaryDesiredSize;
        float naturalHeight = summary.Y > 0 ? summary.Y + 19 : 0;
        float height = summaryArea.Size.Y;
        float verticalBar = naturalHeight > height ? _previewViewport.GetVScrollBar().GetCombinedMinimumSize().X : 0;
        float frameWidth = Math.Max(1, width - verticalBar);
        float inner = Math.Max(1, frameWidth - 21);
        _previewContent.CustomMinimumSize = new(frameWidth, Math.Max(1, naturalHeight));
        _previewContent.Size = _previewContent.CustomMinimumSize;
        _optionPreview.SetLayout(summary.Y > 0 ? new Rect2(8, 5, inner, summary.Y) : default, default);
        _previewViewport.Position = summaryArea.Position; _previewViewport.Size = new(Math.Max(1, width), Math.Max(1, height));
        _previewViewport.Visible = summary.Y > 0;
        _previewFrame.Visible = _previewShadow.Visible = summary.Y > 0;
        _previewFrame.Position = Vector2.Zero; _previewFrame.Size = new(Math.Max(1, frameWidth - 5), summary.Y + 12);
        _previewShadow.Position = _previewFrame.Position + new Vector2(5, 7); _previewShadow.Size = _previewFrame.Size;
        var cards = _optionCardPreview.CardsDesiredSize;
        _previewCardsViewport.Visible = cards.X > 0 && cards.Y > 0;
        if (_previewCardsViewport.Visible)
        {
            _previewCardsViewport.Position = cardArea.Position; _previewCardsViewport.Size = cardArea.Size;
            _previewCardsContent.CustomMinimumSize = cards + new Vector2(16, 16);
            _previewCardsContent.Size = _previewCardsContent.CustomMinimumSize;
            _optionCardPreview.SetLayout(default, new Rect2(new(8, 8), cards));
        }
    }
    private PanelContainer Surface(string name)
    {
        var panel = new PanelContainer { Name = name, MouseFilter = MouseFilterEnum.Stop };
        var style = _palette.Box("151E32", "405578", 1);
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", style); panel.GuiInput += ConsumeMouse;
        return panel;
    }
    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren()) { node.RemoveChild(child); child.QueueFree(); }
    }
    private VBoxContainer Column(Node parent, int separation = 6)
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", separation); parent.AddChild(column); return column;
    }
    private Label Text(string text, int size = 16, bool secondary = false)
    {
        var label = _palette.Label(text, size, secondary); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        MainPanelTheme.ApplyFont(label);
        label.AddThemeColorOverride("font_color", new Color(secondary ? "b5c4d9" : "ece9df"));
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill; return label;
    }
    private void BuildHover(InRunMapNodeDetailData data)
    {
        _hoverGeneration++; long generation = _hoverGeneration;
        CloseOptionPreview();
        ClearRewardCardDock();
        _gainItems.Clear();
        ClearChildren(_surface); _hoverPopups.Clear(); _previewChoices.Clear();
        _rewardRows.Clear(); _embeddedPreviews.Clear();
        _hoverRoot = Column(_surface, 7);
        var heading = new HBoxContainer { Name = "NodeHeading", MouseFilter = MouseFilterEnum.Ignore };
        heading.AddThemeConstantOverride("separation", 7); _hoverRoot.AddChild(heading);
        if (data.HeaderObject is { } identity)
            heading.AddChild(new TextureRect { Texture = _icons.Resolve(identity.Key, identity.Kind, IconVariant.Small).Texture,
                CustomMinimumSize = new(28, 28), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        var title = Column(heading, 2); title.AddChild(Text(data.Title, 20));
        if (!string.IsNullOrWhiteSpace(data.Subtitle)) title.AddChild(Text(data.Subtitle, 14, true));
        _prerequisiteScroll = null; _prerequisiteBody = null;
        if (data.Information.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            _prerequisiteScroll = new ScrollContainer { Name = "NodePrerequisiteScroll", MouseFilter = MouseFilterEnum.Stop,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _hoverRoot.AddChild(_prerequisiteScroll); _prerequisiteBody = Column(_prerequisiteScroll, 4);
            foreach (string information in data.Information.Where(value => !string.IsNullOrWhiteSpace(value)))
                _prerequisiteBody.AddChild(Text(information, 14, true));
        }
        var sections = data.Sections.Where(section => !RedundantIdentity(section)).ToArray();
        var firstBranch = sections.FirstOrDefault(section => section.BranchRow && !section.CombatPanel);
        if (firstBranch != null) BuildSections(_hoverRoot, data.NodeId, [firstBranch], generation, editor: false);
        _hoverScroll = new ScrollContainer { Name = "NodeDetailScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            MouseFilter = MouseFilterEnum.Stop, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _hoverScroll.GetVScrollBar().ValueChanged += _ =>
        { if (generation == _hoverGeneration) CloseOptionPreview(); };
        _hoverRoot.AddChild(_hoverScroll); _hoverBody = Column(_hoverScroll, 8);
        BuildSections(_hoverBody, data.NodeId, sections.Where(section => !section.CombatPanel && !ReferenceEquals(section, firstBranch)).ToArray(), generation, editor: false);
        BuildCombatSurface(data, sections.Where(section => section.CombatPanel).ToArray(), generation);
        if (data.Premises is { } premises)
        {
            var expand = ChoiceButton((_premisesExpanded ? "▾ " : "▸ ") + data.PremisesLabel, compact: true);
            expand.Name = "PremisesExpansion"; _hoverBody.AddChild(expand);
            expand.Pressed += () =>
            {
                if (generation != _hoverGeneration || _data?.NodeId != data.NodeId) return;
                _premisesExpanded = !_premisesExpanded; BuildHover(data); LayoutHover();
            };
            if (_premisesExpanded)
            {
                var fields = Column(_hoverBody, 3);
                BuildFields(fields, premises, generation, editor: false);
                BuildSections(fields, data.NodeId, premises.Sections, generation, editor: false, compactCards: true);
            }
        }
    }
    private static bool HasCards(InRunMapDetailSection section) => section.Objects.Any(item => item.Kind == GameContentKind.Card) ||
        section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card);
    private static bool RedundantIdentity(InRunMapDetailSection section) =>
        string.IsNullOrWhiteSpace(section.Title) && string.IsNullOrWhiteSpace(section.Text) && section.Choices.Count == 0 &&
        section.Objects.Count != 0 && section.Objects.All(item => item.Kind is GameContentKind.Encounter or GameContentKind.Event or GameContentKind.Ancient);
    private void BuildSections(Node parent, string nodeId, IReadOnlyList<InRunMapDetailSection> sections, long generation, bool editor, bool compactCards = false)
    {
        var seenFoldouts = new HashSet<string>();
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            if (compactCards) section = section with { LargeCardResults = false };
            if (!editor && section.PlannedActionsRow)
            {
                var planRow = Column(parent);
                string heading = string.Join(" · ", new[] { section.Title, section.Text }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (heading.Length != 0) planRow.AddChild(Text(heading, 14, true));
                foreach (var item in section.Objects) planRow.AddChild(ObjectRow(item, compact: true));
                var actions = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                actions.AddThemeConstantOverride("h_separation", 6); actions.AddThemeConstantOverride("v_separation", 5); planRow.AddChild(actions);
                foreach (var choice in section.Choices) AddChoice(actions, nodeId, choice, generation, editorAction: false, editor: false);
                continue;
            }
            if (!editor && section.ItemFlow)
            {
                var stockColumn = Column(parent);
                if (!string.IsNullOrWhiteSpace(section.Title)) stockColumn.AddChild(Text(section.Title, 16));
                if (!string.IsNullOrWhiteSpace(section.Text)) stockColumn.AddChild(Text(section.Text, 14, true));
                var flow = new HFlowContainer { Name = "ShopOfferRows", MouseFilter = MouseFilterEnum.Ignore,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill };
                flow.AddThemeConstantOverride("h_separation", (int)ShopOfferGap); flow.AddThemeConstantOverride("v_separation", 8);
                stockColumn.AddChild(flow);
                foreach (var choice in section.Choices)
                    AddChoice(flow, nodeId, choice, generation, editorAction: false, editor: false, itemFlow: true);
                continue;
            }
            if (!editor && section.GainReward)
            {
                var gains = new List<InRunMapDetailSection> { section };
                while (sectionIndex + 1 < sections.Count && sections[sectionIndex + 1].GainReward)
                    gains.Add(sections[++sectionIndex]);
                BuildGainRewards(parent, nodeId, gains, generation, compactCards);
                continue;
            }
            if (!editor && section.BranchRow)
            {
                if (!string.IsNullOrWhiteSpace(section.Text)) parent.AddChild(Text(section.Text, 14, true));
                var scroll = new ScrollContainer { Name = "EventBranchScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                    VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Stop,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill };
                parent.AddChild(scroll);
                var row = new HBoxContainer { Name = "EventBranchRow", MouseFilter = MouseFilterEnum.Ignore };
                if (section.StableId != null) row.SetMeta("branch_stage", section.StableId);
                row.AddThemeConstantOverride("separation", 8); scroll.AddChild(row);
                foreach (var choice in section.Choices) AddChoice(row, nodeId, choice, generation,
                    editorAction: false, editor: false, branch: true);
                continue;
            }
            bool firstFoldout = section.FoldoutId == null || seenFoldouts.Add(section.FoldoutId);
            if (!editor && section.CollapsedByDefault && section.FoldoutId is { } hidden &&
                !firstFoldout && !_expandedSections.Contains(hidden)) continue;
            Node sectionParent = parent;
            if (!editor && section.CombatPanel)
            {
                var combat = Surface("EventCombatRewards"); parent.AddChild(combat); sectionParent = combat;
            }
            var column = Column(sectionParent, editor ? 6 : 5);
            if (section.StableId != null) column.SetMeta("reward_group", section.StableId);
            if (!editor && section.CollapsedByDefault && section.FoldoutId is { } foldout)
            {
                bool expanded = _expandedSections.Contains(foldout);
                if (firstFoldout)
                {
                    var toggle = ChoiceButton((expanded ? "▾ " : "▸ ") + section.Title, compact: true);
                    toggle.Name = "NodeSection_" + foldout; column.AddChild(toggle);
                    toggle.Pressed += () =>
                    {
                        if (generation != _hoverGeneration || _data?.NodeId != nodeId) return;
                        if (!_expandedSections.Remove(foldout)) _expandedSections.Add(foldout);
                        BuildHover(_data); LayoutHover();
                    };
                }
                if (!expanded) continue;
            }
            if (section.HeaderChoice is { } header)
                AddChoice(column, nodeId, header, generation, editorAction: false, editor);
            if (editor)
            {
                if (section.HeaderChoice == null && !string.IsNullOrWhiteSpace(section.Title)) column.AddChild(Text(section.Title, 17));
                if (!string.IsNullOrWhiteSpace(section.Text)) column.AddChild(Text(section.Text, 15, true));
            }
            else
            {
                string heading = string.Join(" · ", new[] { section.HeaderChoice == null && !section.CollapsedByDefault ? section.Title : "", section.Text }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (HasCards(section)) BuildCardRewardHeading(column, nodeId, section, heading, generation);
                else if (heading.Length != 0) column.AddChild(Text(heading, 15, true));
            }
            if (!editor && section.OutcomePreview is { } outcome && NeowOperationOverlay.HasVisibleContent(outcome))
            {
                if (section.LargeCardResults) RegisterOutcomeCardDock(section, outcome);
                var host = new Control { Name = "StableBranchOutcome", MouseFilter = MouseFilterEnum.Ignore,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var preview = new NeowOperationOverlay { Name = "StableOperationResult", ExternallyFramed = true,
                    MapCardPresentation = true, MapSummaryOnly = true, MapSummaryCardNames = !section.LargeCardResults,
                    CardScaleOverride = RewardDockCardScale };
                column.AddChild(host); host.AddChild(preview); preview.SetPreview(outcome);
                _embeddedPreviews.Add((host, preview));
            }
            // A clickable offer already draws its icon/name. Keep the complete
            // data payload while showing each offered object once.
            bool cardGrid = section.CardColumns > 0 || !editor && HasCards(section) || section.RewardActionsRow;
            var displayObjects = section.Objects.Where(item => !section.Choices.Any(choice => choice.Object == item)).ToArray();
            foreach (var item in displayObjects.Where(item => !cardGrid || item.Kind != GameContentKind.Card))
                column.AddChild(ObjectRow(item, compact: !editor));
            if (!editor && HasCards(section))
                BuildCardRewardRow(column, nodeId, section, displayObjects, generation);
            else if (cardGrid)
            {
                var cards = section.Choices.Where(choice => choice.Object?.Kind == GameContentKind.Card).ToArray();
                var previews = displayObjects.Where(item => item.Kind == GameContentKind.Card).ToArray();
                int count = cards.Length + previews.Length;
                if (count > 0)
                {
                    var grid = new GridContainer { Name = "CardResultsGrid", Columns = Math.Min(section.CardColumns > 0 ? section.CardColumns : 4, count),
                        SizeFlagsHorizontal = editor ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin, MouseFilter = MouseFilterEnum.Ignore };
                    grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10); column.AddChild(grid);
                    foreach (var item in previews) grid.AddChild(CompactCardRow(item, !editor));
                    foreach (var choice in cards) AddChoice(grid, nodeId, choice, generation, editorAction: false, editor, cardTile: true);
                }
                var actions = section.Choices.Where(choice => choice.Object?.Kind != GameContentKind.Card).ToArray();
                if (actions.Length != 0)
                {
                    var row = new HBoxContainer { Name = "CardRewardActions", MouseFilter = MouseFilterEnum.Ignore,
                        SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    row.AddThemeConstantOverride("separation", 4); column.AddChild(row);
                    foreach (var choice in actions) AddChoice(row, nodeId, choice, generation, editorAction: false, editor);
                }
            }
            // Plain actions are a compact row (Take/Skip); authored event options
            // with more choices retain their full-width labels and order.
            else
            {
            Node choices = column;
            if (!editor && section.Choices.Count is > 0 and <= 2 &&
                section.Choices.All(choice => choice.Object == null && choice.Label.Length <= 10))
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                row.AddThemeConstantOverride("separation", 4); column.AddChild(row); choices = row;
            }
            foreach (var choice in section.Choices) AddChoice(choices, nodeId, choice, generation, editorAction: false, editor);
            }
            if (section.EmbeddedEditor is { } embedded)
            {
                BuildFields(column, embedded, generation, editor);
                BuildSections(column, nodeId, embedded.Sections, generation, editor, compactCards);
            }
        }
    }
    private Control CompactCardRow(InRunMapDetailObject item, bool compact)
    {
        var tile = new HBoxContainer { CustomMinimumSize = new(compact ? 76 : 100, compact ? 30 : 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        tile.AddThemeConstantOverride("separation", 4);
        tile.AddChild(new TextureRect { Texture = _icons.Resolve(item.Key, item.Kind, IconVariant.Small).Texture,
            CustomMinimumSize = new(compact ? 20 : 24, compact ? 20 : 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        string name = item.DisplayName ?? _names.Resolve(item.Key, item.Kind);
        if (item.UpgradeLevel > 0) name += "+" + (item.UpgradeLevel > 1 ? item.UpgradeLevel.ToString() : "");
        var label = Text(name, compact ? 12 : 14); label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore; tile.AddChild(label);
        return tile;
    }
    private Control ObjectRow(InRunMapDetailObject item, bool compact = false)
    {
        var row = new IconWithLabel(compact ? 22 : 32, Ui1TextRole.Meta) { MouseFilter = MouseFilterEnum.Ignore };
        if (compact) row.AddThemeConstantOverride("separation", 6);
        string name = item.DisplayName ?? _names.Resolve(item.Key, item.Kind);
        if (item.Kind == GameContentKind.Card && item.UpgradeLevel > 0)
            name += "+" + (item.UpgradeLevel > 1 ? item.UpgradeLevel.ToString() : "");
        row.Bind(_icons.Resolve(item.Key, item.Kind, IconVariant.Small), name, string.Empty, string.Empty);
        row.UseClippedName(); row.TooltipText = string.Empty;
        foreach (Control child in row.GetChildren().OfType<Control>()) child.MouseFilter = MouseFilterEnum.Ignore;
        return row;
    }
    private void AddChoice(Node parent, string nodeId, InRunMapDetailChoice choice, long generation, bool editorAction, bool editor, bool cardTile = false, bool branch = false, bool inline = false, bool itemFlow = false)
    {
        var button = ChoiceButton(choice.Object == null ? choice.Label : string.Empty, choice.Selected, compact: !editor);
        button.TooltipText = string.Empty; button.Disabled = !choice.Enabled;
        button.SizeFlagsHorizontal = inline || cardTile && !editor ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill; button.SetMeta("choice_id", choice.Id);
        if (inline)
        {
            button.ClipText = false; button.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            if (choice.Object == null)
                button.CustomMinimumSize = new(button.GetThemeFont("font").GetStringSize(choice.Label,
                    fontSize: button.GetThemeFontSize("font_size")).X + 20, 34);
        }
        parent.AddChild(button);
        if (branch)
        {
            float width = Math.Clamp(MainPanelTheme.Regular.GetStringSize(choice.Label, fontSize: 16).X + 24, 120, 340);
            var label = Text(choice.Label, 16); label.MouseFilter = MouseFilterEnum.Ignore;
            button.Text = ""; button.CustomMinimumSize = new(width, Math.Max(42, Mathf.Ceil(
                MainPanelTheme.Regular.GetStringSize(choice.Label, fontSize: 16).X / (width - 24)) * 23 + 16));
            button.AddChild(label); label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            label.OffsetLeft = label.OffsetTop = 8; label.OffsetRight = label.OffsetBottom = -8;
            label.VerticalAlignment = VerticalAlignment.Center;
        }
        if (!editor && branch && choice.Selected)
            button.MouseEntered += () =>
            {
                if (generation == _hoverGeneration && HoverOpen) CloseOptionPreview();
            };
        else if (!editor && !editorAction && (choice.Preview != null || choice.PreviewFactory != null))
        {
            string key = choice.PreviewKey ?? choice.Preview?.OptionKey ?? choice.Id;
            OperationPreview? Resolve()
            {
                if (_data?.IsCurrent?.Invoke() == false) return null;
                return choice.Preview ?? choice.PreviewFactory?.Invoke();
            }
            _previewChoices[key] = (button, Resolve);
            button.MouseEntered += () =>
            {
                if (generation == _hoverGeneration && HoverOpen && IsInstanceValid(button))
                {
                    // Crossing another row on the way down to the child must
                    // not replace it under the pointer. A deliberate short
                    // hover still changes options, using the same cached data.
                    if (_optionPreview.Visible && !ReferenceEquals(button, _previewAnchor))
                    { _pendingPreview = (button, Resolve, key, generation, Time.GetTicksMsec()); return; }
                    if (Resolve() is { } preview) { ShowOptionPreview(button, preview); _previewOptionKey = key; }
                    else CloseOptionPreview();
                }
            };
        }
        if (itemFlow)
        {
            button.Text = ""; button.CustomMinimumSize = new(ShopOfferWidth, 58);
            button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            var tile = Column(button, 3); tile.MouseFilter = MouseFilterEnum.Ignore;
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; tile.AddChild(row);
            if (choice.Object is { } stock)
                row.AddChild(new TextureRect { Texture = _icons.Resolve(stock.Key, stock.Kind, IconVariant.Small).Texture,
                    CustomMinimumSize = new(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
            string name = choice.Object is { } named ? named.DisplayName ?? _names.Resolve(named.Key, named.Kind) +
                (named.UpgradeLevel > 0 ? "+" + (named.UpgradeLevel > 1 ? named.UpgradeLevel.ToString() : "") : "") : choice.Label;
            var label = Text(name, 14); label.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(label);
            if (choice.Price is { } price)
            {
                var cost = Text(price + "G", 14); cost.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
                cost.AutowrapMode = TextServer.AutowrapMode.Off;
                cost.CustomMinimumSize = new(cost.GetThemeFont("font").GetStringSize(cost.Text,
                    fontSize: cost.GetThemeFontSize("font_size")).X + 1, 20);
                cost.MouseFilter = MouseFilterEnum.Ignore; tile.AddChild(cost);
            }
            if (!string.IsNullOrWhiteSpace(choice.Status))
            { var status = Text(choice.Status, 12, true); status.MouseFilter = MouseFilterEnum.Ignore; tile.AddChild(status); }
            tile.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            tile.OffsetLeft = tile.OffsetTop = 6; tile.OffsetRight = tile.OffsetBottom = -6;
            void FitTile() => button.CustomMinimumSize = new(ShopOfferWidth, Math.Max(58, tile.GetCombinedMinimumSize().Y + 12));
            tile.MinimumSizeChanged += FitTile; FitTile();
        }
        else if (choice.Object is { } item)
        {
            var row = inline ? CardRewardItem(item) : cardTile ? CompactCardRow(item, !editor) : ObjectRow(item, compact: !editor);
            button.AddChild(row);
            if (inline) button.CustomMinimumSize = row.GetCombinedMinimumSize() + new Vector2(16, 8);
            else if (cardTile) button.CustomMinimumSize = new(editor ? 108 : 84, editor ? 48 : 42);
            if (!choice.Enabled) row.Modulate = new Color(1, 1, 1, .55f);
            row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            row.OffsetLeft = inline ? 8 : 4; row.OffsetTop = 4;
            row.OffsetRight = inline ? -8 : -4; row.OffsetBottom = -4;
        }
        button.Pressed += () => Callable.From(() =>
        {
            if (!IsInsideTree() || !IsInstanceValid(button) || button.IsQueuedForDeletion() ||
                generation != (editor ? _editorGeneration : _hoverGeneration)) return;
            if (editorAction) EditorRequested?.Invoke(nodeId, choice.Id);
            else ChoiceRequested?.Invoke(nodeId, choice.Id);
        }).CallDeferred();
    }
    private Button ChoiceButton(string label, bool selected = false, bool compact = false)
    {
        var button = _palette.CompactButton(label, compact ? 34 : 40, 16, selected);
        MainPanelTheme.ApplyFont(button);
        foreach (var (state, fill, border) in new[]
        {
            ("normal", _palette.Canvas, selected ? _palette.Selected : _palette.Line),
            ("hover", _palette.Hover, selected ? _palette.Selected : _palette.Active),
            ("pressed", _palette.Line, selected ? _palette.Selected : _palette.Active),
            ("disabled", _palette.Canvas, _palette.Line)
        })
        {
            var style = _palette.Box(fill, border, selected ? 2 : 1);
            if (compact)
            {
                style.BgColor = new Color(.035f, .07f, .13f, state == "normal" ? .16f : .65f);
                style.BorderColor = selected ? new Color("bca475") : new Color(.47f, .61f, .77f, state == "normal" ? .22f : .6f);
                style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom = 1;
            }
            style.ContentMarginLeft = style.ContentMarginRight = 8;
            style.ContentMarginTop = style.ContentMarginBottom = compact ? 2 : 4;
            button.AddThemeStyleboxOverride(state, style);
        }
        button.TooltipText = string.Empty;
        return button;
    }

    // The panel has its own compact frame; the source stays under native map ownership.
    private void LayoutHover()
    {
        if (_data == null || _hoverScroll == null || _hoverBody == null || _hoverRoot == null || !IsInsideTree()) return;
        var inverse = GetGlobalTransform().AffineInverse();
        Rect2 viewport = VisibleViewportLocal(), anchor = inverse * _nodeGlobal;
        // Actual reward rows may grow the panel before a horizontal scrollbar
        // is needed. Optional target lists never determine its natural width.
        float rewardWidth = _rewardRows.Where(row => row.HasMeta("map_comparable_reward"))
            .Select(row => row.GetCombinedMinimumSize().X).DefaultIfEmpty(0).Max() + 52;
        float shopWidth = _data.Sections.Any(section => section.ItemFlow)
            ? ShopOfferColumns * ShopOfferWidth + (ShopOfferColumns - 1) * ShopOfferGap + 52 : 460;
        _hoverNaturalWidth = Math.Max(_hoverNaturalWidth ?? shopWidth, Math.Max(rewardWidth, shopWidth));
        float sideSpace = Math.Max(anchor.Position.X - viewport.Position.X - Margin - Gap,
            viewport.End.X - Margin - anchor.End.X - Gap);
        float width = Math.Min(_hoverNaturalWidth.Value,
            Math.Max(100, Math.Min(viewport.Size.X - Margin * 2, sideSpace)));
        float inner = Math.Max(1, width - 40);
        _surface.CustomMinimumSize = new(width, 0);
        _hoverRoot.CustomMinimumSize = new(inner, 0);
        _hoverBody.CustomMinimumSize = new(inner - 12, 0);
        LayoutGainRewards(inner - 12);
        if (_prerequisiteScroll != null && _prerequisiteBody != null)
        {
            _prerequisiteBody.CustomMinimumSize = new(inner - 12, 0);
            _prerequisiteScroll.CustomMinimumSize = new(0, Math.Min(_prerequisiteBody.GetCombinedMinimumSize().Y,
                Math.Min(64, viewport.Size.Y * .12f)));
        }
        foreach (var (host, preview) in _embeddedPreviews)
        {
            var summary = preview.SummaryDesiredSize;
            host.Visible = summary.Y > 0;
            host.CustomMinimumSize = new(0, summary.Y);
            preview.SetLayout(summary.Y > 0 ? new Rect2(0, 0, inner - 12, summary.Y) : default, default);
        }
        // Only the outcome area scrolls. Heading and authored branch controls stay in place.
        float pinned = _hoverRoot.GetChildren().OfType<Control>().Where(child => child != _hoverScroll)
            .Sum(child => child.GetCombinedMinimumSize().Y + 7) + 31;
        float desiredBody = _hoverBody.GetCombinedMinimumSize().Y;
        // Resizing an already open node can leave only the minimum outcome
        // viewport. It must still expose a full action row, including room
        // for a horizontal scrollbar, rather than an unclickable 24px strip.
        float bodyMinimum = Math.Max(40, 36 + _hoverScroll.GetHScrollBar().GetCombinedMinimumSize().Y);
        _hoverScroll.CustomMinimumSize = new(0, bodyMinimum);
        // The root includes the outcome viewport's previous allocated height.
        // Treating that stretched height as a content minimum creates feedback:
        // a once-expanded panel can never shrink after its result is cleared.
        float minimumHeight = pinned + bodyMinimum;
        // Main content has a viewport-sized reading budget, independent of
        // its source's current Y and whether a secondary preview is showing.
        float mainBudget = Math.Max(minimumHeight, viewport.Size.Y * .65f);
        float height = Math.Max(minimumHeight, Math.Min(pinned + desiredBody, mainBudget));
        var origin = StableMainOrigin(viewport, anchor, width, height);
        _hoverScroll.CustomMinimumSize = new(0, Math.Max(bodyMinimum, height - pinned));
        _surface.Size = new(width, height);
        _hover.Position = origin; _hover.Size = new(width, height);
        _surface.Position = Vector2.Zero;
        _frame.Position = Vector2.Zero; _frame.Size = _hover.Size;
        _frameShadow.Position = new(5, 7); _frameShadow.Size = _hover.Size;
        LayoutAuxiliarySurfaces(viewport, new Rect2(_hover.Position, _hover.Size));
        _sourceMarker.Position = (inverse * _nodeGlobal).Position - new Vector2(4, 4);
        _sourceMarker.Size = (inverse * _nodeGlobal).Size + new Vector2(8, 8); _sourceMarker.Show();
        QueueRedraw();
    }

    internal void ShowEditor(InRunMapNodeEditorData data, Rect2 viewportGlobalRect)
    {
        bool scrollToTop = data.ScrollToTopRequest != 0 &&
            (_editorNode != data.NodeId || _editorScrollRequest != data.ScrollToTopRequest);
        int previousScroll = !scrollToTop && EditorOpen && _editorNode == data.NodeId ? _editorScroll?.ScrollVertical ?? 0 : 0;
        _editorScrollRequest = data.ScrollToTopRequest;
        CloseHover(); _editorGeneration++; long generation = _editorGeneration;
        _editorNode = data.NodeId; _viewportGlobal = viewportGlobalRect;
        ClearChildren(_editor);
        var column = Column(_editor, 10);
        var heading = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new(0, 44) }; column.AddChild(heading);
        heading.AddChild(Text(data.Title, 20));
        // CompactButton clips text and therefore contributes no text minimum
        // width. Reserve an explicit width beside the expanding title.
        var close = ChoiceButton(data.CloseLabel);
        close.Name = "EditorClose"; close.CustomMinimumSize = new(90, 40);
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        close.Pressed += CloseEditor; heading.AddChild(close);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _editorScroll = scroll;
        column.AddChild(scroll); var fields = Column(scroll, 8);
        BuildSections(fields, data.NodeId, data.Sections, generation, editor: true);
        BuildFields(fields, data, generation, editor: true);
        _editorShield.Show(); _editor.Show(); LayoutEditor();
        // Preserve ordinary same-node edits. A new explicit reason-inspection
        // request starts at its top summary once, rather than on every refresh.
        Callable.From(() =>
        {
            if (EditorOpen && generation == _editorGeneration && IsInstanceValid(scroll))
                scroll.ScrollVertical = previousScroll;
        }).CallDeferred();
    }
    private void BuildFields(Node fields, InRunMapNodeEditorData data, long generation, bool editor)
    {
        foreach (var field in data.Numbers)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; fields.AddChild(row);
            row.AddChild(Text(field.Label, editor ? 16 : 13));
            var number = new SpinBox { MinValue = field.Minimum, MaxValue = field.Maximum, Step = field.Step,
                Value = field.Value, Editable = field.Enabled, CustomMinimumSize = new(editor ? 150 : 100, editor ? 38 : 28) };
            number.AddThemeFontSizeOverride("font_size", editor ? 16 : 13);
            number.GetLineEdit().TooltipText = string.Empty;
            row.AddChild(number);
            number.ValueChanged += value => EmitField(data.NodeId, field.Id, new(Number: value), generation, editor);
        }
        foreach (var field in data.Toggles)
        {
            var toggle = new CheckButton { Text = editor ? field.Label : string.Empty,
                ButtonPressed = field.Value, Disabled = !field.Enabled };
            toggle.AddThemeFontSizeOverride("font_size", editor ? 16 : 13);
            if (editor) fields.AddChild(toggle);
            else
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; fields.AddChild(row);
                row.AddChild(Text(field.Label, 13)); row.AddChild(toggle);
            }
            toggle.Toggled += value => EmitField(data.NodeId, field.Id, new(Toggle: value), generation, editor);
        }
        foreach (var field in data.Selections)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; fields.AddChild(row);
            row.AddChild(Text(field.Label, editor ? 16 : 13));
            var selection = new OptionButton { Disabled = !field.Enabled,
                CustomMinimumSize = new(editor ? 190 : 125, editor ? 38 : 28), FitToLongestItem = false,
                ClipText = true };
            selection.AddThemeFontSizeOverride("font_size", editor ? 16 : 13);
            for (int index = 0; index < field.Options.Count; index++)
            {
                selection.AddItem(field.Options[index].Label);
                selection.SetItemDisabled(index, !field.Options[index].Enabled);
            }
            selection.Select(field.Options.ToList().FindIndex(option => option.Id == field.Value));
            row.AddChild(selection);
            if (!editor) _hoverPopups.Add(selection.GetPopup());
            selection.ItemSelected += index =>
                EmitField(data.NodeId, field.Id, new(Selected: field.Options[(int)index].Id), generation, editor);
        }
    }
    private void EmitField(string nodeId, string fieldId, InRunMapDetailFieldValue value, long generation, bool editor) =>
        Callable.From(() =>
        {
            if (IsInsideTree() && (editor ? EditorOpen && generation == _editorGeneration :
                    HoverOpen && generation == _hoverGeneration && _data?.NodeId == nodeId))
                FieldChanged?.Invoke(nodeId, fieldId, value);
        }).CallDeferred();
    internal void CloseEditor()
    {
        if (!EditorOpen) return;
        _editor.Hide(); _editorShield.Hide(); _editorNode = null; _editorScroll = null;
        _editorGeneration++; EditorClosed?.Invoke();
    }
    private void LayoutEditor()
    {
        if (!IsInsideTree()) return;
        Rect2 viewport = VisibleViewportLocal();
        _editor.Size = new(Math.Min(620, viewport.Size.X - Margin * 2), Math.Min(680, viewport.Size.Y - Margin * 2));
        _editor.Position = viewport.Position + (viewport.Size - _editor.Size) * .5f;
    }
}
