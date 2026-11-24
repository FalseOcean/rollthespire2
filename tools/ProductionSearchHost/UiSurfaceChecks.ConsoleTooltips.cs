using System.Reflection;
using Godot;
using RolltheSpire2.Ui.InRunConsole;
using RolltheSpire2.Ui.Tooltips;

internal static partial class UiSurfaceChecks
{
    // Runs inside the existing live console fixture. Call with a currently
    // visible native content tile, after the scenario's filters have settled.
    private static async Task CheckConsoleTooltip(Node root, InRunConsolePanel console, Control tile,
        string expectedTitle, string? expectedRules, Action<bool, string> check,
        Func<int, Task> frames, Func<string, Task>? savePicture, string label)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(object owner, string name) => owner.GetType().GetField(name, members)!.GetValue(owner)!;
        var host = (AnchoredTooltipHost)Field(console, "_contentTooltipHost");
        var panel = (PanelContainer)Field(host, "_panel");
        var title = (Label)Field(host, "_title");
        var description = (Label)Field(host, "_description");
        // The offscreen window's polled pointer is the Owner's desktop pointer,
        // not PushInput's injected position. Exercise real GUI enter/exit and
        // shared layout while suspending only this unchanged pointer poll.
        bool wasProcessing = host.IsProcessing();
        host.SetProcess(false);
        try
        {
        var viewport = root.GetViewport();
        void Move(Vector2 position) => viewport.PushInput(new InputEventMouseMotion
            { Position = position, GlobalPosition = position, ButtonMask = 0 }, true);
        await frames(2);
        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        check(ReferenceEquals(host.GetParent(), console.GetParent()) && host.Scale == Vector2.One && !host.ClipContents,
            "content tooltip is an unscaled sibling of the console outside clipped tiles " + label);
        check(tile.IsVisibleInTree() && viewport.GetVisibleRect().Encloses(tile.GetGlobalRect()),
            "tooltip fixture uses a visible on-screen content tile " + label);
        Vector2 away = viewport.GetVisibleRect().End - new Vector2(2, 2);
        Move(away);
        Move(tile.GetGlobalRect().GetCenter());
        check(panel.Visible && title.Text == expectedTitle,
            "real viewport hover opens the shared content tooltip with its native title " + label);
        if (expectedRules is not null)
            check(description.Text == expectedRules,
                "tooltip displays the official rules of the bound model or instance " + label);
        await frames(3);
        check(panel.IsVisibleInTree() && panel.Modulate.A > .99f &&
            viewport.GetVisibleRect().Grow(1).Encloses(panel.GetGlobalRect()),
            $"content tooltip completes layout visibly within the viewport {label}: visible={panel.Visible},tree={panel.IsVisibleInTree()},alpha={panel.Modulate.A},rect={panel.GetGlobalRect()},mouse={viewport.GetMousePosition()},anchor={tile.GetGlobalRect()}");
        if (savePicture is not null) await savePicture(label + "-tooltip");
        Move(away);
        check(!panel.Visible, "real viewport mouse exit dismisses the content tooltip " + label);
        Move(tile.GetGlobalRect().GetCenter());
        check(panel.Visible, "content tooltip can reopen on a second real hover " + label);
        console.Hide();
        check(!panel.Visible, "console hide immediately dismisses its sibling tooltip " + label);
        console.Show();
        Move(away);
        await frames(1);
        check(!panel.Visible, "showing the console again retains no stale tooltip " + label);
        }
        finally { if (GodotObject.IsInstanceValid(host)) { host.Dismiss(); host.SetProcess(wasProcessing); } }
    }
}
