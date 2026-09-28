namespace RolltheSpire2.Ui.Theme;

/// <summary>
/// Small relative Z-index bands for the single AppShell CanvasItem hierarchy.
/// Values intentionally stay far below Godot's CanvasItem limit; parent/child
/// structure, not large magic numbers, establishes the visual ordering.
/// </summary>
internal static class UiZLayers
{
    public const int BaseContent = 0;
    public const int ShellHost = 10;
    public const int ShellChrome = 10;
    public const int ShellSurface = 20;
    public const int DrawerOverlay = 20;
    public const int WorkspaceOverlay = 24;
    public const int TransactionModal = 29;
    public const int PickerModal = 30;
    public const int ConfirmationModal = 31;
    public const int Tooltip = 40;
    public const int ToastModal = 50;

    public const int HighestAssigned = ToastModal;
}
