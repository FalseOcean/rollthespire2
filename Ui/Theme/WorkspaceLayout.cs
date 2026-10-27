namespace RolltheSpire2.Ui.Theme;

/// <summary>Shared logical column geometry for the Search and Analysis workspaces.</summary>
internal static class WorkspaceLayout
{
    public const float CanvasWidth = 1536;
    public const float CanvasHeight = 772;
    public const float LeftRailWidth = 224;
    public const float RightRailWidth = 304;
    public const float RailGap = 24;
    public const float CenterLeft = LeftRailWidth + RailGap;
    public const float RightRailLeft = CanvasWidth - RightRailWidth;
    public const float CenterWidth = RightRailLeft - RailGap - CenterLeft;
    public const int ColumnInset = 8;
    public const float HeaderHeight = 44;
    public const float DividerTop = -12;
    public const float DividerHeight = 816;
}
