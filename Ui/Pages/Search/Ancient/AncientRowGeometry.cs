namespace RolltheSpire2.Ui.Pages.Search.Ancient;

/// <summary>
/// Shared geometry for the Ancient card layout. Option grids use at most five
/// columns: ten-option rows render 5x2 while the twelve-option Act 2 Darv wraps
/// naturally as 5+5+2 without dictating the surrounding Act-column width.
/// </summary>
internal static class AncientRowGeometry
{
    public const float HeaderHeight = 44f;
    public const float PortraitSize = 38f;
    public const float OptionCellSize = 41f;
    public const float OptionCellInset = 2f;
    public const int OptionGap = 4;
    public const int CardPadding = 8;
    public const int CardGap = 8;
    public const int ActGap = 10;
    public const int StandardOptionColumns = 5;
    public const float SelectedBadgeSize = 15f;
}
