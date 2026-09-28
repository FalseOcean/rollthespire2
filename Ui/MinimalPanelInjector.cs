using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui;

/// <summary>
/// Preserves the accepted main-menu entry injection boundary while delegating
/// the actual RT2 top-level lifetime to the game's NSubmenuStack.
/// </summary>
internal static class MinimalPanelInjector
{
    public static void EnsureAttached(NMainMenu mainMenu, ModRuntimeSnapshot snapshot) =>
        AppShellHost.EnsureAttached(mainMenu, snapshot);
}
