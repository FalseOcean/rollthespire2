using Godot;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private Control? _retainedToolsOverlay;
    private AppShell? _retainedTools;
    private bool _closingRetainedTools;
    private readonly Dictionary<Button, bool> _toolsBlocked = [];

    // Isolated legacy-host fixture; the production workspace exposes no entry.
    private void OpenRetainedTools()
    {
        if (_references?.HasActiveSearch == true) return;
        if (_retainedTools is null)
        {
            _retainedToolsOverlay = new Control { Name = "RetainedTools", MouseFilter = MouseFilterEnum.Stop };
            AddChild(_retainedToolsOverlay);
            _retainedToolsOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var backdrop = new ColorRect { Color = _palette.Color(_palette.Canvas), MouseFilter = MouseFilterEnum.Stop };
            _retainedToolsOverlay.AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _retainedTools = new AppShell();
            _retainedTools.InitializeRetainedTools(_runtime, _persistence!);
            _retainedTools.TopLevelCloseRequested += CloseRetainedTools;
            _retainedToolsOverlay.AddChild(_retainedTools);
        }
        _toolsBlocked.Clear();
        foreach (var button in new[] { _search, _analysis, _seeds, _encyclopedia, _status, _notes, _settings, _close })
        { _toolsBlocked[button] = button.Disabled; button.Disabled = true; }
        _content.Hide();
        _retainedToolsOverlay!.Show();
        _retainedTools.ActivateRetainedTools();
    }

    private void CloseRetainedTools()
    {
        if (_retainedToolsOverlay?.Visible != true) return;
        _retainedTools!.CleanupForTopLevelClose();
        _retainedTools.Hide();
        _closingRetainedTools = true;
        // Let the existing controller finish cancellation/cursor persistence
        // before the other Search surface can start another session.
        if (!_retainedTools.HasActiveSearch) FinishClosingRetainedTools();
    }

    private void FinishClosingRetainedTools()
    {
        _closingRetainedTools = false;
        _retainedToolsOverlay!.Hide();
        _references?.SyncSearchPreferences();
        _content.Show();
        foreach (var (button, disabled) in _toolsBlocked)
            if (IsInstanceValid(button)) button.Disabled = disabled;
        _toolsBlocked.Clear();
        _settings.GrabFocus();
    }
}
