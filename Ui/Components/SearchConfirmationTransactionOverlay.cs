using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Narrow Search-page confirmation transaction used by Phase A2 to keep
/// pointer/focus ownership inside the same Control hierarchy as the Page and
/// its pickers. Business meaning stays with SearchPage.
/// </summary>
internal sealed partial class SearchConfirmationTransactionOverlay : Control
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly Button _cancel;
    private readonly Button _confirm;

    public SearchConfirmationTransactionOverlay()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ZIndex = UiZLayers.ConfirmationModal;

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.66f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Cancel();
            }
        };
        AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(440f, 170f),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Drawer, 5f, 2, 18f);
        center.AddChild(panel);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 12);
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle, true);
        _message = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        root.AddChild(_title);
        root.AddChild(_message);
        root.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var footer = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _cancel = new Button { CustomMinimumSize = new Vector2(92f, 34f) };
        _confirm = new Button { CustomMinimumSize = new Vector2(104f, 34f) };
        Ui1Theme.ApplyButton(_cancel, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(_confirm, Ui1ButtonRole.Primary);
        _cancel.Pressed += Cancel;
        _confirm.Pressed += () =>
        {
            if (!Visible)
            {
                return;
            }
            Visible = false;
            Confirmed?.Invoke();
        };
        footer.AddChild(_cancel);
        footer.AddChild(_confirm);
        root.AddChild(footer);
        panel.AddChild(root);
    }

    public event Action? Confirmed;
    public event Action? Cancelled;
    public bool IsOpen => Visible;

    public void Open(string title, string message, string confirmText, string cancelText)
    {
        _title.Text = title;
        _message.Text = message;
        _confirm.Text = confirmText;
        _cancel.Text = cancelText;
        Visible = true;
        _confirm.GrabFocus();
    }

    public void Cancel()
    {
        if (!Visible)
        {
            return;
        }
        Visible = false;
        Cancelled?.Invoke();
    }
}
