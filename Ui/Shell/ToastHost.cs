using Godot;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class ToastHost : PanelContainer
{
    private readonly Label _message;
    private readonly Godot.Timer _timer;

    public ToastHost()
    {
        Visible = false;
        MouseFilter = Control.MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(220, 44);
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.CardElevated, 4f, 1, 10f);
        _message = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_message);
        _timer = new Godot.Timer { OneShot = true, WaitTime = 2.2 };
        _timer.Timeout += () => Visible = false;
        AddChild(_timer);
    }

    public void ShowToast(string text)
    {
        _message.Text = text;
        Visible = true;
        _timer.Start();
    }
}
