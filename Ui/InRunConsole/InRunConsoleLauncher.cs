using RolltheSpire2.Ui.Workspace;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsoleLauncher : Button
{
    private const string EntryGroup = "rt2_inrun_console_entry";
    internal const string EntryLayerName = "RolltheSpire2_InRunConsoleLayer";
    private IRunState _run = null!;
    private ModRuntimeSnapshot _runtime = null!;
    private bool _enabled;
    private double _vertical = 50, _scale = 100;
    private InRunConsoleOverlay? _surface;
    private double _poll;

    internal static void EnsureAttached(NTopBar topBar, IRunState run, ModRuntimeSnapshot runtime)
    {
        Control parent = topBar.GetParent() is NGlobalUi global ? global : topBar;
        const string name = "RolltheSpire2_InRunConsole";
        if (parent.GetNodeOrNull<Node>(EntryLayerName) is not null) return;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        var preferences = new SearchWorkspace(OS.GetUserDataDir(), false).Preferences;
        var entry = new InRunConsoleLauncher
        {
            Name = name, Text = "RT2\n›", _run = run, _runtime = runtime,
            _enabled = preferences.ShowInRunConsole, _vertical = preferences.InRunConsoleVerticalPercent,
            _scale = preferences.InRunConsoleScalePercent, FocusMode = FocusModeEnum.None,
            TooltipText = "", AccessibilityName = "RT2 Console"
        };
        WorkspacePalette.MainPanel.ApplyButton(entry);
        entry.AddThemeFontSizeOverride("font_size", 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var style = (StyleBoxFlat)entry.GetThemeStylebox(state).Duplicate();
            style.ContentMarginLeft = style.ContentMarginRight = 4;
            entry.AddThemeStyleboxOverride(state, style);
        }
        // A separate canvas controls both draw order and Godot mouse picking;
        // ZIndex alone would still leave the later native map/capstone first.
        var layer = new CanvasLayer { Name = EntryLayerName, Layer = 1 };
        parent.AddChild(layer);
        layer.AddChild(entry);
        entry.AddToGroup(EntryGroup);
        entry.Pressed += entry.Open;
        entry.Align();
        entry.RefreshAvailability();
    }

    internal static void ApplyPreferences(SceneTree tree, bool enabled, double verticalPercent, double scalePercent)
    {
        foreach (var node in tree.GetNodesInGroup(EntryGroup))
        {
            if (node is not InRunConsoleLauncher entry) continue;
            entry._enabled = enabled;
            entry._vertical = verticalPercent;
            entry._scale = scalePercent;
            bool eligible = ConsoleActions.CanUseConsole;
            if (!enabled || !eligible) entry._surface?.Close();
            entry._surface?.ApplyPlacement(verticalPercent, scalePercent);
            entry.Align();
            entry.RefreshAvailability();
        }
    }

    private void Align()
    {
        Vector2 viewport = GetViewportRect().Size;
        float scale = (float)Math.Clamp(_scale / 100, .5, 2);
        Scale = Vector2.One * scale;
        Size = CustomMinimumSize = new(40, 70);
        Position = new(0, Math.Clamp((viewport.Y - Size.Y * scale) * (float)Math.Clamp(_vertical / 100, 0, 1), 0, Math.Max(0, viewport.Y - Size.Y * scale)));
    }

    public override void _EnterTree() => ActiveScreenContext.Instance.Updated += RefreshAvailability;
    public override void _ExitTree() => ActiveScreenContext.Instance.Updated -= RefreshAvailability;

    internal static bool IsCoveredByPriorityScreen => NModalContainer.Instance?.OpenModal is not null ||
        NGame.Instance?.FeedbackScreen?.Visible == true || NGame.Instance?.InspectCardScreen?.Visible == true ||
        NGame.Instance?.InspectRelicScreen?.Visible == true || NGame.Instance?.LogoAnimation is not null;

    private void RefreshAvailability()
    {
        bool eligible = ConsoleActions.CanUseConsole;
        if (!eligible) _surface?.Close();
        bool open = _surface is not null && IsInstanceValid(_surface);
        bool covered = IsCoveredByPriorityScreen;
        Visible = _enabled && eligible && !open && !covered;
        Disabled = !eligible || open || NOverlayStack.Instance is null || covered;
    }

    public override void _Process(double delta)
    {
        _poll -= delta;
        if (_poll > 0) return;
        _poll = .2;
        Align();
        RefreshAvailability();
    }

    private void Open()
    {
        RefreshAvailability();
        if (!_enabled || !ConsoleActions.CanUseConsole || Disabled || _surface is not null || NOverlayStack.Instance is not { } stack) return;
        var surface = new InRunConsoleOverlay();
        try
        {
            surface.Initialize(_run, _runtime, stack, _vertical, _scale);
            surface.Closed += () => { _surface = null; RefreshAvailability(); };
            _surface = surface;
            stack.Push(surface);
            Hide();
        }
        catch (Exception ex)
        {
            _surface = null;
            if (IsInstanceValid(surface))
            {
                if (ReferenceEquals(stack.Peek(), surface)) stack.Remove(surface);
                else surface.QueueFree();
            }
            RuntimeLog.WarnException("inRunConsoleOpenFailed=true", ex);
        }
    }
}

internal sealed partial class InRunConsoleOverlay : Control, IOverlayScreen
{
    private NOverlayStack _stack = null!;
    private InRunConsolePanel _panel = null!;
    private bool _closed, _hotkeysBlocked, _backPressed;
    private ColorRect _shade = null!;
    private CanvasLayer _layer = null!;
    private Control _surfaceRoot = null!;
    private double _vertical, _scale;
    internal event Action? Closed;
    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => false;
    internal bool CanTakeCoveredScreenPriority => !_closed && IsInsideTree() && Visible &&
        ReferenceEquals(_stack.Peek(), this) && ConsoleActions.CanUseConsole;
    public Control? DefaultFocusedControl => _panel.CloseButton;

    internal void Initialize(IRunState run, ModRuntimeSnapshot runtime, NOverlayStack stack, double vertical, double scale)
    {
        Name = "RolltheSpire2_InRunConsoleOverlay";
        _stack = stack;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _layer = new CanvasLayer { Name = "ConsoleLayer", Layer = 1, Visible = false };
        AddChild(_layer);
        _surfaceRoot = new Control { Name = "Surface", MouseFilter = MouseFilterEnum.Stop };
        _layer.AddChild(_surfaceRoot);
        _surfaceRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _shade = new ColorRect { Color = new(0, 0, 0, .15f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _surfaceRoot.AddChild(_shade); _shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new InRunConsolePanel();
        _panel.Initialize(run, runtime);
        _surfaceRoot.AddChild(_panel);
        _panel.AttachContentTooltips(_surfaceRoot);
        _panel.CloseRequested += Close;
        _panel.LayoutChanged += LayoutPanel;
        ApplyPlacement(vertical, scale);
        Resized += LayoutPanel;
    }

    internal void ApplyPlacement(double vertical, double scale)
    { _vertical = vertical; _scale = scale; LayoutPanel(); }

    private void LayoutPanel()
    {
        if (_panel is null || !IsInsideTree()) return;
        Vector2 viewport = Size;
        if (viewport.X <= 1 || viewport.Y <= 1) viewport = GetViewportRect().Size;
        Vector2 desired = _panel.DesiredSize;
        float scale = Math.Min((float)Math.Clamp(_scale / 100, .5, 2), Math.Min((viewport.X - 12) / 1204, (viewport.Y - 16) / desired.Y));
        scale = Math.Max(.1f, scale);
        _panel.Scale = Vector2.One * scale;
        _panel.Size = desired;
        _panel.Position = new(6, Math.Max(8, (viewport.Y - desired.Y * scale) * (float)Math.Clamp(_vertical / 100, 0, 1)));
        _shade.Visible = _panel.ContentOpen;
    }

    public override void _Ready() { SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); LayoutPanel(); }
    public override void _EnterTree() => ActiveScreenContext.Instance.Updated += SyncPriorityVisibility;
    public override void _Process(double delta)
    {
        if (!_closed && !ConsoleActions.CanUseConsole) Close();
        else SyncPriorityVisibility();
    }
    public void AfterOverlayOpened() { }
    public void AfterOverlayClosed()
    {
        if (_closed) return;
        _closed = true;
        _panel.DismissContentTooltip();
        _panel.CaptureConsoleState();
        ReleaseHotkeys(); Closed?.Invoke(); QueueFree();
    }
    public void AfterOverlayShown()
    {
        if (_closed) return;
        Show();
        SyncPriorityVisibility();
    }
    public void AfterOverlayHidden() { _panel.DismissContentTooltip(); ReleaseHotkeys(); Hide(); _layer.Hide(); }
    internal static void RestoreTopAboveCoveredScreen(NOverlayStack stack)
    {
        if (stack.Peek() is not InRunConsoleOverlay console || console._closed) return;
        console.AfterOverlayShown();
        // Push updates native focus while the covered overlay is hidden. Refresh
        // again after restoring it so the stack's focus contract matches routing.
        ActiveScreenContext.Instance.Update();
    }
    private void SyncPriorityVisibility()
    {
        bool active = CanTakeCoveredScreenPriority && ActiveScreenContext.Instance.IsCurrent(this);
        _layer.Visible = active;
        _surfaceRoot.Visible = active;
        if (!active) { _panel.DismissContentTooltip(); ReleaseHotkeys(); return; }
        if (!_hotkeysBlocked && NHotkeyManager.Instance is { } hotkeys)
        { hotkeys.AddBlockingScreen(this); _hotkeysBlocked = true; }
    }
    public override void _ExitTree()
    {
        ActiveScreenContext.Instance.Updated -= SyncPriorityVisibility;
        ReleaseHotkeys();
    }
    private void ReleaseHotkeys()
    {
        _backPressed = false;
        if (_hotkeysBlocked) NHotkeyManager.Instance?.RemoveBlockingScreen(this);
        _hotkeysBlocked = false;
    }
    public override void _Input(InputEvent input)
    {
        if (_closed || !_layer.Visible || !IsVisibleInTree() || !ActiveScreenContext.Instance.IsCurrent(this)) return;
        if (input is not InputEventKey { Keycode: Key.Escape } && !input.IsAction(MegaInput.pauseAndBack)) return;
        GetViewport().SetInputAsHandled();
        if (input.IsEcho()) return;
        if (input.IsPressed()) _backPressed = true;
        else if (_backPressed) { _backPressed = false; if (!_panel.HandleBack()) Close(); }
    }
    internal void Close()
    { if (!_closed) _stack.Remove(this); }
}
