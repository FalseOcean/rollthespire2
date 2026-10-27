using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodeDetails
{
    private Control? _combatPanel;
    private PanelContainer? _combatSurface;
    private NinePatchRect? _combatFrame, _combatShadow;
    private ScrollContainer? _combatScroll;
    private VBoxContainer? _combatBody;
    private VBoxContainer? _combatRoot;
    private float CombatSurfaceChrome => _combatRoot == null ? 58 : 24 +
        _combatRoot.GetChildren().OfType<Control>().Where(child => child != _combatScroll && child.Visible)
            .Sum(child => child.GetCombinedMinimumSize().Y + _combatRoot.GetThemeConstant("separation"));
    private float CombatBodyMinimum => Math.Max(40, 36 + (_combatScroll?.GetHScrollBar().GetCombinedMinimumSize().Y ?? 0));
    private bool HasCombatSurface => _combatPanel?.Visible == true;
    private bool CombatSurfaceContains(Vector2 point) => HasCombatSurface && _combatSurface!.GetGlobalRect().HasPoint(point);

    private void BuildCombatSurface(InRunMapNodeDetailData data, IReadOnlyList<InRunMapDetailSection> sections, long generation)
    {
        if (_combatPanel != null)
        { RemoveChild(_combatPanel); _combatPanel.QueueFree(); }
        _combatPanel = null; _combatSurface = null; _combatScroll = null; _combatBody = null; _combatRoot = null;
        if (sections.Count == 0) return;
        _combatPanel = new Control { Name = "EventCombatPanel", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_combatPanel);
        _combatShadow = NativeNightSurface.Backdrop(shadow: true); _combatPanel.AddChild(_combatShadow);
        _combatFrame = NativeNightSurface.Backdrop(); _combatPanel.AddChild(_combatFrame);
        _combatSurface = Surface("EventCombatSurface"); _combatPanel.AddChild(_combatSurface);
        _combatSurface.AddThemeStyleboxOverride("panel", new StyleBoxEmpty {
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 12, ContentMarginBottom = 12 });
        var root = _combatRoot = Column(_combatSurface, 8);
        root.AddChild(Text(JsonUiTextProvider.CreatePredictorUi13(UiLanguagePreferences.LanguageCode).Get("predictor.map.event_combat"), 18));
        _combatScroll = new ScrollContainer { Name = "EventCombatScroll", MouseFilter = MouseFilterEnum.Stop,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_combatScroll); _combatBody = Column(_combatScroll, 8);
        BuildSections(_combatBody, data.NodeId, sections.Select(section => section with { CombatPanel = false }).ToArray(), generation, editor: false);
    }

    private void ApplyCombatSurfaceLayout(Rect2 area)
    {
        if (!HasCombatSurface || _combatBody == null || _combatScroll == null) return;
        _combatScroll.CustomMinimumSize = new(0, Math.Max(CombatBodyMinimum, area.Size.Y - CombatSurfaceChrome));
        _combatPanel!.Position = area.Position; _combatPanel.Size = area.Size;
        _combatSurface!.Position = Vector2.Zero; _combatSurface.Size = _combatPanel.Size;
        _combatFrame!.Size = _combatPanel.Size; _combatShadow!.Size = _combatPanel.Size; _combatShadow.Position = new(5, 7);
    }
}
