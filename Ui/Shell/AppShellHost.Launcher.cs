using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class AppShellHostRoot
{
    private Vector2? _launcherMouseDown;
    private Vector2 _launcherStart;
    private bool _draggingLauncher;
    private bool _suppressLauncherClick;

    private static string FormatLauncherVersion(RuntimeVersionResolution rules, IUiTextProvider text)
    {
        string version = $"RT2 v{typeof(ModRuntime).Assembly.GetName().Version?.ToString(3)}";
        if (!rules.IsUsable || rules.AdaptedReleaseChannel is not { } channel ||
            string.IsNullOrEmpty(rules.RulesAdaptedThroughVersion)) return version;
        string label = text.Format(channel == GameReleaseChannel.Stable ? "shell.rules.stable" : "shell.rules.beta",
            rules.RulesAdaptedThroughVersion);
        return version + "\n" + label;
    }

    private bool CanToggleMainMenuLauncher() =>
        _mainMenuSurfaceVisible && !_pushScheduled && _menuWorkspace is not null &&
        _mainMenu is not null && GodotObject.IsInstanceValid(_mainMenu) && _mainMenu.IsVisibleInTree() &&
        !_mainMenu.SubmenuStack.SubmenusOpen && !_mainMenu.PatchNotesScreen.IsOpen &&
        NModalContainer.Instance?.OpenModal is null && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit);

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _launcherMouseDown.HasValue)
        {
            EndLauncherGesture();
            RestoreLauncherPosition();
        }
    }

    private void RefreshLauncherVisibility()
    {
        if (_launcher is null) return;
        bool hiddenOnMainMenu = _launcher.GetParent() == this && _menuWorkspace?.Preferences.MainMenuLauncherHidden == true;
        bool show = _mainMenuSurfaceVisible && !_pushScheduled && !hiddenOnMainMenu;
        if (!show) EndLauncherGesture();
        _launcher.Visible = show;
        _launcher.Disabled = !show;
    }

    private void BeginLauncherGesture(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down ||
            _launcher is null || _launcher.Disabled) return;
        _launcherMouseDown = _launcher.GetTransform() * down.Position;
        _launcherStart = _launcher.Position;
        _draggingLauncher = _suppressLauncherClick = false;
    }

    private void TrackLauncherDrag(InputEvent input)
    {
        if (_launcherMouseDown is not { } start || _launcher?.GetParentControl() is not { } parent) return;
        bool released = input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false };
        if ((!released && input is not InputEventMouseMotion) || input is not InputEventMouse mouse) return;
        var point = parent.GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
        var delta = point - start;
        if (delta.LengthSquared() >= 36) _draggingLauncher = true;
        if (_draggingLauncher) _launcher.Position = ConstrainLauncher(_launcherStart + delta, parent);
        if (!released) return;
        if (_draggingLauncher)
        {
            _menuWorkspace?.SaveMainMenuLauncherPosition(_launcher.Position.X, _launcher.Position.Y);
            // BaseButton dispatches its release after _Input in this viewport event.
            _suppressLauncherClick = true;
            Callable.From(() => _suppressLauncherClick = false).CallDeferred();
        }
        _launcherMouseDown = null;
        _draggingLauncher = false;
    }

    private void EndLauncherGesture()
    {
        _launcherMouseDown = null;
        _draggingLauncher = _suppressLauncherClick = false;
    }

    private void HandleLauncherPressed()
    {
        if (!_suppressLauncherClick) ShowShell();
        _suppressLauncherClick = false;
    }

    private void RestoreLauncherPosition()
    {
        if (_launcher?.GetParentControl() is not { } parent || _launcherMouseDown.HasValue) return;
        var prefs = _menuWorkspace?.Preferences;
        var saved = new Vector2(prefs?.MainMenuLauncherX ?? 28, prefs?.MainMenuLauncherY ?? 96);
        _launcher.Position = ConstrainLauncher(saved, parent);
    }

    private Vector2 ConstrainLauncher(Vector2 position, Control parent)
    {
        var versionEdge = _launcherVersion is { } label ? label.Position + label.Size : Vector2.Zero;
        var extent = _launcher!.Size.Max(versionEdge);
        return position.Clamp(Vector2.Zero, (parent.Size - extent).Max(Vector2.Zero));
    }
}
