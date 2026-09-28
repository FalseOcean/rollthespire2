using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

/// <summary>
/// Game-owned top-level Surface adapter for RolltheSpire2.
///
/// Important Beta111 contract:
/// - derived NSubmenu must not call base._Ready();
/// - RT2 intentionally overrides ConnectSignals() and does not call the base
///   implementation, because the base NBackButton wiring calls _stack.Pop()
///   directly and would bypass RT2's guarded single-close authority.
/// </summary>
internal sealed partial class Rt2TopLevelSubmenu : NSubmenu
{
    private AppShell? _shell;
    private Button? _backPointer;
    private bool _initialized;
    private bool _closeRequested;

    protected override Control? InitialFocusedControl => _backPointer;

    public event Action? TopLevelClosed;

    public void Initialize(ModRuntimeSnapshot snapshot)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Name = "RolltheSpire2_TopLevelSubmenu";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _shell = new AppShell
        {
            Visible = false
        };
        _shell.Initialize(snapshot);
        AddChild(_shell);

        _backPointer = new Button
        {
            Name = "RolltheSpire2_TopLevelBack",
            Text = "←",
            TooltipText = "Back",
            CustomMinimumSize = new Vector2(44f, 44f),
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All,
            AnchorLeft = 0f,
            AnchorTop = 1f,
            AnchorRight = 0f,
            AnchorBottom = 1f,
            OffsetLeft = 8f,
            OffsetTop = -52f,
            OffsetRight = 52f,
            OffsetBottom = -8f,
            ZIndex = UiZLayers.ShellSurface + 1
        };
        Ui1Theme.ApplyButton(_backPointer, Ui1ButtonRole.Secondary);
        AddChild(_backPointer);
    }

    public override void _Ready()
    {
        if (!_initialized || _shell is null || _backPointer is null)
        {
            throw new InvalidOperationException("RT2 top-level submenu must be initialized before entering the SceneTree.");
        }

        // Beta111 NSubmenu explicitly forbids base._Ready() for derived types.
        // Call the virtual signal contract directly instead.
        ConnectSignals();
        RefreshBackPresentation();
    }

    protected override void ConnectSignals()
    {
        if (_shell is null || _backPointer is null)
        {
            return;
        }

        // Intentionally do NOT call base.ConnectSignals(). Beta111's base
        // implementation requires an NBackButton named "BackButton" and wires
        // Released directly to _stack.Pop(), which has no duplicate/reentrancy
        // guard. Both pointer Back and Header X must converge on the RT2 guard.
        _backPointer.Pressed += () => RequestTopLevelClose("back-pointer");
        _shell.TopLevelCloseRequested += () => RequestTopLevelClose("header-x");
        VisibilityChanged += HandleVisibilityChanged;
    }

    public override void OnSubmenuOpened()
    {
        _closeRequested = false;
        if (_backPointer is not null)
        {
            _backPointer.Disabled = false;
        }

        RefreshBackPresentation();
        _shell?.Open();
        RuntimeLog.Info("rt2TopLevelOpened=true;closeAuthority=GuardedRequestTopLevelClose");
    }

    public override void OnSubmenuClosed()
    {
        // Pop already removed RT2 from the game stack before this callback.
        // This method is cleanup-only and must never call Pop again.
        if (_backPointer is not null)
        {
            _backPointer.Disabled = true;
        }

        _shell?.CleanupForTopLevelClose();
        base.OnSubmenuClosed();
        TopLevelClosed?.Invoke();
        RuntimeLog.Info("rt2TopLevelClosed=true;cleanupOnly=true;popRequested=false");
    }

    public bool RequestTopLevelClose(string source)
    {
        if (_closeRequested)
        {
            RuntimeLog.Info($"rt2TopLevelCloseSuppressed=true;reason=duplicate;source={source}");
            return false;
        }

        if (_stack is null || !ReferenceEquals(_stack.Peek(), this))
        {
            RuntimeLog.Info($"rt2TopLevelCloseSuppressed=true;reason=not-current-top;source={source}");
            return false;
        }

        _closeRequested = true;
        if (_backPointer is not null)
        {
            _backPointer.Disabled = true;
        }

        RuntimeLog.Info($"rt2TopLevelCloseRequested=true;source={source};guardAccepted=true");
        _stack.Pop();
        return true;
    }

    private void HandleVisibilityChanged()
    {
        if (Visible)
        {
            OnSubmenuShown();
        }
        else
        {
            OnSubmenuHidden();
        }
    }

    protected override void OnSubmenuShown()
    {
        if (_backPointer is not null)
        {
            _backPointer.Disabled = _closeRequested;
        }
        DefaultFocusedControl?.GrabFocus();
    }

    protected override void OnSubmenuHidden()
    {
        Control? focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner is not null && IsAncestorOf(focusOwner))
        {
            _lastFocusedControl = focusOwner;
        }
        if (_backPointer is not null)
        {
            _backPointer.Disabled = true;
        }
    }

    private void RefreshBackPresentation()
    {
        if (_backPointer is null)
        {
            return;
        }

        string languageCode = TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh"
            : "en";
        string back = languageCode == "zh" ? "返回" : "Back";
        _backPointer.Text = "←";
        _backPointer.TooltipText = back;
    }
}
