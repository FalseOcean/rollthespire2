using RolltheSpire2.Ui.Workspace;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Context;
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
    // EventRoom exists during asset preload, before GetLocalEvent's indexed
    // list is populated. Read only an already bound local event in that window.
    internal static bool TryGetCurrentEvent(IRunState run, out EventModel? current)
    {
        current = null;
        if (run.CurrentRoom is null) return false;
        if (run.CurrentRoom is not EventRoom room) return true;
        if (LocalContext.NetId is not { } localId || RunManager.Instance.EventSynchronizer is not { } synchronizer)
            return false;
        for (int slot = 0; slot < run.Players.Count; slot++)
        {
            var player = run.Players[slot];
            if (player.NetId != localId) continue;
            if (slot >= synchronizer.Events.Count) return false;
            var candidate = synchronizer.Events[slot];
            if (!ReferenceEquals(candidate.Owner, player) || candidate.Id != room.CanonicalEvent.Id) return false;
            current = candidate;
            return true;
        }
        return false;
    }
    private Control? _debugInfo;

    internal static void EnsureAttached(NTopBar topBar, IRunState run, ModRuntimeSnapshot runtime)
    {
        const string name = "RolltheSpire2_RunPrediction";
        var globalUi = topBar.GetParent() as NGlobalUi;
        Control parent = globalUi is null ? topBar : globalUi;
        if (parent.GetNodeOrNull<Node>(name) is InRunPredictionLauncher existing)
        {
            if (!ReferenceEquals(existing._run, run) || !ReferenceEquals(existing._runtime, runtime))
            {
                existing.DiscardSurface();
                existing._run = run;
                existing._runtime = runtime;
                existing._poll = 0;
            }
            return;
        }
        if (parent.GetNodeOrNull<Node>(name) is not null) return;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        var preferences = SearchWorkspace.Open(OS.GetUserDataDir()).Preferences;
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
        bool sceneReady = TryGetCurrentEvent(_run, out var currentEvent);
        if (_surface is not null && (!IsInstanceValid(_surface) || !sceneReady ||
            !_surface.Matches(_run, _runtime, NOverlayStack.Instance, currentEvent))) DiscardSurface();
        AlignBelowRunInformation();
        bool predictionOpen = _surface?.IsOpen == true;
        Visible = !predictionOpen;
        Disabled = !sceneReady || _opening || predictionOpen ||
            NOverlayStack.Instance is null || NMapScreen.Instance?.IsOpen == true ||
            NCapstoneContainer.Instance?.InUse == true || NModalContainer.Instance?.OpenModal is not null;
    }

    private void OpenPrediction()
    {
        if (!_enabledByPreference || Disabled || _opening || NOverlayStack.Instance is not { } stack) return;
        _opening = true;
        try
        {
            if (!TryGetCurrentEvent(_run, out var currentEvent)) return;
            if (_surface is not null && (!IsInstanceValid(_surface) ||
                !_surface.Matches(_run, _runtime, stack, currentEvent))) DiscardSurface();
            if (_surface is null)
            {
                _surface = new RunPredictionOverlay();
                _surface.Initialize(_runtime, _run, stack, currentEvent);
                _surface.GuideRequested += (snapshot, mode, solution) =>
                {
                    var parent = (Control)GetParent();
                    if (parent.GetNodeOrNull<Node>("CrystalGuidance") is { } oldGuide) { parent.RemoveChild(oldGuide); oldGuide.QueueFree(); }
                    var guide = new CrystalSphereGuidance();
                    guide.Initialize(_run, _runtime, snapshot, mode, solution);
                    parent.AddChild(guide); guide.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                };
            }
            _surface.Open();
        }
        catch (Exception ex)
        {
            DiscardSurface();
            RuntimeLog.WarnException("runPredictionOpenFailed=true", ex);
        }
        finally { _opening = false; }
    }

    private void DiscardSurface()
    {
        var surface = _surface;
        _surface = null;
        if (surface is not null && IsInstanceValid(surface)) surface.Retire();
    }

    public override void _ExitTree() => DiscardSurface();
}

// Keep this room's controls in one SceneTree: detaching would invoke panel
// _ExitTree and dispose its frozen session. Only the native stack entry is new.
internal sealed partial class RunPredictionOverlay : Control
{
    internal event Action<CrystalSphereLiveSnapshot, string, RolltheSpire2.Core.PredictorRuntime.PredictorCrystalSolution>? GuideRequested;
    private CrystalSphereAssistantPanel? _panel;
    private WorkspaceShell _shell = null!;
    private NOverlayStack _stack = null!;
    private IRunState _run = null!;
    private ModRuntimeSnapshot _runtime = null!;
    private AbstractRoom? _room;
    private EventModel? _event;
    private int _act, _floor;
    private RunPredictionOverlayStackEntry? _entry;
    private CrystalSphereAssistantPanel.Session? _session;
    private bool _retired, _openedBefore;
    private bool _hotkeysBlocked, _backPressed;
    private long _openedAt;
    private double _initializeMilliseconds;
    private bool _isCrystalRoom;
    public Control? DefaultFocusedControl => _shell?.CloseButton;
    internal bool IsOpen => _entry is not null;

    internal void Initialize(ModRuntimeSnapshot runtime, IRunState run, NOverlayStack stack, EventModel? currentEvent)
    {
        _openedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _run = run; _runtime = runtime; _room = run.CurrentRoom; _event = currentEvent;
        _act = run.CurrentActIndex; _floor = run.ActFloor;
        _isCrystalRoom = run.CurrentRoom is EventRoom room && room.CanonicalEvent.Id.Entry == "CRYSTAL_SPHERE";
        Name = "RolltheSpire2_RunPredictionOverlay";
        _stack = stack;
        Hide();
        ProcessMode = ProcessModeEnum.Disabled;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0, 0, 0, .65f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (_isCrystalRoom)
        {
            // The player can start a run before ever opening RT2. Crystal also
            // exposes status/library consumers, so its entry owns fresh Current.
            RefreshRuntimeAuthorityAtEntry(runtime);
            _panel = new CrystalSphereAssistantPanel();
            _panel.Initialize(run, runtime);
        }
        _shell = new WorkspaceShell(); _shell.InitializeRunScene(runtime, run, _panel); AddChild(_shell);
        _shell.TopLevelCloseRequested += Close;
        _shell.CurrentPredictionCreated += AttachCurrentPrediction;
        if (_panel is not null) AttachCurrentPrediction(_panel);
        _initializeMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(_openedAt).TotalMilliseconds;
    }

    private void AttachCurrentPrediction(CrystalSphereAssistantPanel panel)
    {
        _panel = panel;
        panel.GuideRequested += (snapshot, mode, solution) => { GuideRequested?.Invoke(snapshot, mode, solution); Close(); };
    }

    internal bool Matches(IRunState run, ModRuntimeSnapshot runtime, NOverlayStack? stack, EventModel? currentEvent) =>
        !_retired && !IsQueuedForDeletion() && ReferenceEquals(_run, run) && ReferenceEquals(_runtime, runtime) &&
        ReferenceEquals(_stack, stack) && ReferenceEquals(_room, run.CurrentRoom) && ReferenceEquals(_event, currentEvent) &&
        _act == run.CurrentActIndex && _floor == run.ActFloor;

    internal void Open()
    {
        if (_retired || IsOpen) return;
        if (_openedBefore) _openedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        if (GetParent() is null) _stack.AddChild(this);
        _entry = new RunPredictionOverlayStackEntry(this);
        _stack.Push(_entry);
    }

    internal void Retire()
    {
        if (_retired) return;
        _retired = true;
        if (_entry is { Registered: true } entry && IsInstanceValid(_stack)) _stack.Remove(entry);
        else { _entry?.QueueFree(); _entry = null; }
        ReleaseHotkeys();
        Hide();
        ProcessMode = ProcessModeEnum.Disabled;
        QueueFree();
    }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    internal void AfterOverlayOpened()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool retainedSession = _session is not null;
        if (_isCrystalRoom && _openedBefore) RefreshRuntimeAuthorityAtEntry(_runtime);
        if (_session is not null) { _panel!.RestoreSession(_session); _session = null; }
        // Validate while the parent is still hidden, before old answers can draw.
        if (_isCrystalRoom) _panel!.OpenSession();
        _shell.Open();
        ProcessMode = ProcessModeEnum.Inherit;
        RuntimeLog.Detail(FormattableString.Invariant($"panelOpenTiming=true;kind=in-run;crystal={_isCrystalRoom};surfaceReused={_openedBefore};retainedCrystalSession={retainedSession};initializeMs={(_openedBefore ? 0 : _initializeMilliseconds):F3};openMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3};totalMs={System.Diagnostics.Stopwatch.GetElapsedTime(_openedAt).TotalMilliseconds:F3};phase=after-open-before-frame"));
        _openedBefore = true;
    }
    internal void AfterOverlayClosed()
    {
        if (_entry is null) return;
        _entry = null;
        ReleaseHotkeys();
        _session = _panel?.SuspendSession();
        _shell.CleanupForTopLevelClose();
        Hide();
        ProcessMode = ProcessModeEnum.Disabled;
    }
    internal void AfterOverlayShown()
    {
        if (_retired || !IsOpen) return;
        // Native Push owns the logical stack/backstop. Keep the retained scene
        // immediately above its current entry without detaching any controls.
        _stack.MoveChild(this, _stack.GetChildCount() - 1);
        Show();
        if (!_hotkeysBlocked && NHotkeyManager.Instance is { } hotkeys)
        { hotkeys.AddBlockingScreen(this); _hotkeysBlocked = true; }
    }
    internal void AfterOverlayHidden() { ReleaseHotkeys(); Hide(); }
    private static void RefreshRuntimeAuthorityAtEntry(ModRuntimeSnapshot runtime)
    {
        try { RuntimeAuthorityEnvironment.CaptureOnMainThread(runtime); }
        catch (Exception ex)
        {
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "RuntimeAuthorityCaptureFailed", nameof(RunPredictionOverlay), "CrystalEntryEnvironment", "issues.capture_failed");
            RuntimeLog.WarnException("crystalEntryEnvironmentFailed=true;independentSceneCaptureContinues=true", ex);
        }
    }
    public override void _ExitTree() => ReleaseHotkeys();
    private void ReleaseHotkeys()
    {
        _backPressed = false;
        if (_hotkeysBlocked) NHotkeyManager.Instance?.RemoveBlockingScreen(this);
        _hotkeysBlocked = false;
    }
    public override void _Input(InputEvent input)
    {
        if (_entry is null || !IsVisibleInTree() || !ActiveScreenContext.Instance.IsCurrent(_entry)) return;
        if (input is not InputEventKey { Keycode: Key.Escape } && !input.IsAction(MegaInput.pauseAndBack)) return;
        GetViewport().SetInputAsHandled();
        if (input.IsEcho()) return;
        if (input.IsPressed()) _backPressed = true;
        else if (_backPressed) { _backPressed = false; _shell.HandleBack(); }
    }
    private void Close()
    {
        if (_entry is { } entry && ReferenceEquals(_stack.Peek(), entry)) _stack.Remove(entry);
    }
}

// NOverlayStack.Push always adds its screen as a child; Remove never detaches
// it. A disposable entry preserves native stack/focus ownership on each open.
internal sealed partial class RunPredictionOverlayStackEntry : Control, IOverlayScreen
{
    private readonly RunPredictionOverlay _surface;
    internal bool Registered { get; private set; }
    public NetScreenType ScreenType => NetScreenType.None;
    public bool UseSharedBackstop => true;
    public Control? DefaultFocusedControl => _surface.DefaultFocusedControl;

    internal RunPredictionOverlayStackEntry(RunPredictionOverlay surface)
    {
        _surface = surface;
        Name = "RolltheSpire2_RunPredictionStackEntry";
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
    }

    public void AfterOverlayOpened() { Registered = true; _surface.AfterOverlayOpened(); }
    public void AfterOverlayClosed()
    {
        if (!Registered) return;
        Registered = false;
        _surface.AfterOverlayClosed();
        QueueFree();
    }
    public void AfterOverlayShown() => _surface.AfterOverlayShown();
    public void AfterOverlayHidden() => _surface.AfterOverlayHidden();
}
