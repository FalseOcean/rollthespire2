using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using RolltheSpire2.Ui.InRunConsole;

namespace RolltheSpire2.Bootstrap;

// Keep native stack ownership and teardown. Only the RT2 test console can remain
// above map/deck screens; later overlay choices and native modal priorities win.
internal static class InRunConsolePresencePatch
{
    internal static void Install(Harmony harmony)
    {
        var hide = MainMenuPatchInstaller.RequiredMethod(typeof(NOverlayStack), nameof(NOverlayStack.HideOverlays), Type.EmptyTypes);
        var push = MainMenuPatchInstaller.RequiredMethod(typeof(NOverlayStack), nameof(NOverlayStack.Push), [typeof(IOverlayScreen)]);
        var screen = MainMenuPatchInstaller.RequiredMethod(typeof(ActiveScreenContext), nameof(ActiveScreenContext.GetCurrentScreen), Type.EmptyTypes);
        var restore = new HarmonyMethod(MainMenuPatchInstaller.RequiredMethod(typeof(InRunConsolePresencePatch), nameof(RestoreConsole)));
        var current = new HarmonyMethod(MainMenuPatchInstaller.RequiredMethod(typeof(InRunConsolePresencePatch), nameof(CurrentScreen)));
        harmony.Patch(hide, postfix: restore);
        harmony.Patch(push, postfix: restore);
        harmony.Patch(screen, postfix: current);
    }

    private static void RestoreConsole(NOverlayStack __instance) =>
        InRunConsoleOverlay.RestoreTopAboveCoveredScreen(__instance);

    private static void CurrentScreen(ref IScreenContext? __result)
    {
        if (NOverlayStack.Instance?.Peek() is not InRunConsoleOverlay { CanTakeCoveredScreenPriority: true } console)
            return;
        if (__result is NMapScreen ||
            __result is not null && ReferenceEquals(__result, NCapstoneContainer.Instance?.CurrentCapstoneScreen))
            __result = console;
    }
}
