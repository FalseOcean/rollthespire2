using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class InRunPredictionLauncher : Button
{
    private const string EntryGroup = "rt2_run_prediction_entry";
    private IRunState _run = null!;
    private ModRuntimeSnapshot _runtime = null!;
    private double _poll;
    private bool _opening;
    private bool _enabledByPreference;
    private RunPredictionOverlay? _surface;
    private EventModel? _sessionEvent;
    private CrystalSphereAssistantPanel.Session? _session;
    private void CheckSessionEvent()
    {
        var current=(_run.CurrentRoom as EventRoom)?.LocalMutableEvent;
        if(!ReferenceEquals(current,_sessionEvent)) { _session=null;_sessionEvent=current; }
    }
    private Control? _debugInfo;

    internal static void EnsureAttached(NTopBar topBar, IRunState run, ModRuntimeSnapshot runtime)
    {
        const string name = "RolltheSpire2_RunPrediction";
        var globalUi = topBar.GetParent() as NGlobalUi;
        Control parent = globalUi is null ? topBar : globalUi;
        if (parent.GetNodeOrNull<Node>(name) is not null) return;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        var preferences = new SearchWorkspacePersistence(OS.GetUserDataDir(), runtime.Profile.ProfileId, false).Preferences;
        string language = preferences.LanguageOverride is "zh" or "en" ? preferences.LanguageOverride
            : TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        var entry = new InRunPredictionLauncher { Name = name, Text = "", _run = run, _runtime = runtime,
            _enabledByPreference = preferences.ShowInRunPredictionEntry, Visible = preferences.ShowInRunPredictionEntry,
            _debugInfo = globalUi?.DebugInfo,
            TooltipText = "",
            CustomMinimumSize = new Vector2(56, 56), Size = new Vector2(56, 56), FocusMode = FocusModeEnum.None };
        entry.AccessibilityName = JsonUiTextProvider.CreatePredictorUi13(language).Get("predictor.crystal.hover");
        AppShellHostRoot.StyleNeowsBonesLauncher(entry, runtime);
        parent.AddChild(entry);
        entry.AddToGroup(EntryGroup);
        entry.SetProcess(entry._enabledByPreference);
        entry.AlignBelowRunInformation();
        entry.Pressed += entry.OpenPrediction;
        RuntimeLog.Info($"runPredictionLauncherAttached=true;enabled={entry._enabledByPreference};allRunModes=true;placement=BelowDebugInfo;icon=NeowsBones");
    }

    internal static void ApplyPreference(SceneTree tree, bool enabled)
    {
        foreach (var node in tree.GetNodesInGroup(EntryGroup))
        {
            if (node is not InRunPredictionLauncher entry) continue;
            entry._enabledByPreference = enabled;
            entry.SetProcess(enabled);
            entry._poll = 0;
            entry._Process(0);
        }
    }

    private void AlignBelowRunInformation()
    {
        if (GetParent() is not Control parent) return;
        // Follow the actual info block, including extra multiplayer/hash lines and scaling.
        // Remain its sibling so the text block's dimming does not dim the icon.
        Position = _debugInfo is not null && IsInstanceValid(_debugInfo)
            ? parent.GetGlobalTransform().AffineInverse() * _debugInfo.GetGlobalRect().End + new Vector2(-Size.X, 10)
            : new Vector2(Math.Max(0, parent.Size.X - Size.X - 16), 190);
    }

    public override void _Process(double delta)
    {
        if (!_enabledByPreference) { Hide(); return; }
        _poll -= delta;
        if (_poll > 0) return;
        _poll = .2;
        CheckSessionEvent();
        AlignBelowRunInformation();
        bool predictionOpen = _surface is not null && IsInstanceValid(_surface);
        Visible = !predictionOpen;
        Disabled = _opening || predictionOpen ||
            NOverlayStack.Instance is null || NMapScreen.Instance?.IsOpen == true ||
            NCapstoneContainer.Instance?.InUse == true || NModalContainer.Instance?.OpenModal is not null;
    }

    private void OpenPrediction()
    {
        if (!_enabledByPreference || Disabled || _opening || NOverlayStack.Instance is not { } stack) return;
        _opening = true;
        RunPredictionOverlay? surface = null;
        try
        {
            CheckSessionEvent();
            surface = new RunPredictionOverlay();
            surface.Initialize(_runtime, _run, stack,_session);
            var openedEvent=_sessionEvent;
            surface.SessionClosed += session=>
            {
                CheckSessionEvent();
                _session=_sessionEvent!=null && ReferenceEquals(_sessionEvent,openedEvent)?session:null;
                _surface=null;
            };
            surface.GuideRequested += (snapshot, mode, solution) =>
            {
                var parent = (Control)GetParent();
                if (parent.GetNodeOrNull<Node>("CrystalGuidance") is { } oldGuide) { parent.RemoveChild(oldGuide); oldGuide.QueueFree(); }
                var guide = new CrystalSphereGuidance();
                guide.Initialize(_run, _runtime, snapshot, mode, solution);
                parent.AddChild(guide); guide.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            };
            _surface = surface;
            stack.Push(surface);
        }
        catch (Exception ex)
        {
            if (surface is not null && IsInstanceValid(surface))
            {
                if (ReferenceEquals(stack.Peek(), surface)) stack.Remove(surface);
                else surface.QueueFree();
            }
            RuntimeLog.WarnException("runPredictionOpenFailed=true", ex);
        }
        finally { _opening = false; }
    }
}

// Use the game's normal overlay ownership so rewards underneath are restored on close.
internal sealed partial class RunPredictionOverlay : Control, IOverlayScreen
{
    internal event Action<CrystalSphereLiveSnapshot, string, RolltheSpire2.Core.PredictorRuntime.PredictorCrystalSolution>? GuideRequested;
    internal event Action<CrystalSphereAssistantPanel.Session?>? SessionClosed;
    private CrystalSphereAssistantPanel _panel = null!;
    private WorkspaceShell _shell = null!;
    private NOverlayStack _stack = null!;
    private bool _closed;
    private bool _hotkeysBlocked, _backPressed;
    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _shell?.CloseButton;

    internal void Initialize(ModRuntimeSnapshot runtime, IRunState run, NOverlayStack stack,CrystalSphereAssistantPanel.Session? session=null)
    {
        Name = "RolltheSpire2_RunPredictionOverlay";
        _stack = stack;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0, 0, 0, .65f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new CrystalSphereAssistantPanel();
        _panel.Initialize(run, runtime);
        if(session!=null) _panel.RestoreSession(session);
        _shell = new WorkspaceShell(); _shell.InitializeCrystalScene(runtime, _panel,run); AddChild(_shell);
        _shell.TopLevelCloseRequested += Close;
        _panel.GuideRequested += (snapshot, mode, solution) => { GuideRequested?.Invoke(snapshot, mode, solution); Close(); };
    }
    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    public void AfterOverlayOpened()
    {
        _shell.Open();
        _panel.OpenSession();
    }
    public void AfterOverlayClosed()
    {
        if (_closed) return;
        _closed = true; ReleaseHotkeys();SessionClosed?.Invoke(_panel.SuspendSession()); _shell.CleanupForTopLevelClose(); QueueFree();
    }
    public void AfterOverlayShown()
    {
        if (_closed) return;
        Show();
        if (!_hotkeysBlocked && NHotkeyManager.Instance is { } hotkeys)
        { hotkeys.AddBlockingScreen(this); _hotkeysBlocked = true; }
    }
    public void AfterOverlayHidden() { ReleaseHotkeys(); Hide(); }
    public override void _ExitTree() => ReleaseHotkeys();
    private void ReleaseHotkeys()
    {
        _backPressed = false;
        if (_hotkeysBlocked) NHotkeyManager.Instance?.RemoveBlockingScreen(this);
        _hotkeysBlocked = false;
    }
    public override void _Input(InputEvent input)
    {
        if (_closed || !IsVisibleInTree() || !ActiveScreenContext.Instance.IsCurrent(this)) return;
        if (input is not InputEventKey { Keycode: Key.Escape } && !input.IsAction(MegaInput.pauseAndBack)) return;
        GetViewport().SetInputAsHandled();
        if (input.IsEcho()) return;
        if (input.IsPressed()) _backPressed = true;
        else if (_backPressed) { _backPressed = false; _shell.HandleBack(); }
    }
    private void Close()
    {
        if (!_closed && ReferenceEquals(_stack.Peek(), this)) _stack.Remove(this);
    }
}
