using Godot;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodeDetails
{
    private readonly PanelContainer _sourceMarker = new()
    { Name = "NodeSourceHighlight", MouseFilter = MouseFilterEnum.Ignore, Visible = false };

    private void InitializeSourceMarker()
    {
        _sourceMarker.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Colors.Transparent, BorderColor = new Color(.80f, .72f, .49f, .65f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14
        });
        AddChild(_sourceMarker);
    }
    public override void _Draw()
    {
        if (!HoverOpen) return;
        var source = GetGlobalTransform().AffineInverse() * _nodeGlobal;
        var panel = GetGlobalTransform().AffineInverse() * _surface.GetGlobalRect();
        var from = source.GetCenter();
        var to = new Vector2(Math.Clamp(from.X, panel.Position.X, panel.End.X),
            Math.Clamp(from.Y, panel.Position.Y + 16, panel.End.Y - 16));
        if (from.DistanceTo(to) > source.Size.X * .5f + 26)
            DrawLine(from, to, new Color(.72f, .72f, .60f, .42f), 1, true);
    }

    public override void _ExitTree() => CloseHover();

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && _hover != null && !IsVisibleInTree())
            CloseHover();
    }
}
