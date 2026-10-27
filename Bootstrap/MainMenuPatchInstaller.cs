using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using RolltheSpire2.Ui;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Bootstrap;

internal static class MainMenuPatchInstaller
{
    private static readonly string[] MainMenuTypeNames =
    {
        "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu",
        "MegaCrit.Sts2.Core.Nodes.Screens.NMainMenu",
        "MegaCrit.Sts2.Core.Nodes.MainMenu.NMainMenu"
    };

    public static void Install(Harmony harmony, Func<Type, string, Type[], MethodInfo?>? mapTargetResolver = null)
    {
        InstallFeature(harmony, "CrystalSphereClickObservation", () =>
            RolltheSpire2.Infrastructure.Snapshots.CrystalSphereLiveCapture.Install(harmony));
        InstallFeature(harmony, "CrystalSphereMaskAppearance", () => CrystalSphereMaskAppearance.Install(harmony));
        InstallFeature(harmony, "InRunMapPrediction", () => InRunMapPredictionPatch.Install(harmony, mapTargetResolver));
        InstallFeature(harmony, "InRunConsolePresence", () => InRunConsolePresencePatch.Install(harmony));
        InstallFeature(harmony, "RunTopBar", () => harmony.Patch(
            RequiredMethod(typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.NTopBar), "Initialize", [typeof(MegaCrit.Sts2.Core.Runs.IRunState)]),
            postfix: new HarmonyMethod(RequiredMethod(typeof(MainMenuPatchInstaller), nameof(RunTopBarPostfix)))));
        InstallFeature(harmony, "MainMenu", () => InstallMenu(harmony));
    }

    private static void InstallMenu(Harmony harmony)
    {
        MethodInfo readyPostfix = RequiredMethod(typeof(MainMenuPatchInstaller), nameof(Postfix));
        MethodInfo submenuPostfix = RequiredMethod(typeof(MainMenuPatchInstaller), nameof(SubmenuStackChangedPostfix));

        foreach (string typeName in MainMenuTypeNames)
        {
            Type? type = AccessTools.TypeByName(typeName);
            if (type is null || !typeof(Control).IsAssignableFrom(type))
            {
                continue;
            }

            MethodInfo? ready = AccessTools.Method(type, "_Ready", Type.EmptyTypes);
            if (ready is null)
            {
                continue;
            }

            // Visibility synchronization is part of the menu launcher, so resolve
            // it before installing either consumer hook.
            MethodInfo submenuChanged = RequiredMethod(type, "OnSubmenuStackChanged", Type.EmptyTypes);
            harmony.Patch(ready, postfix: new HarmonyMethod(readyPostfix));
            RuntimeLog.Info($"minimalUiPatchTarget={type.FullName}._Ready");

            harmony.Patch(submenuChanged, postfix: new HarmonyMethod(submenuPostfix));
            RuntimeLog.Info($"mainMenuVisibilityPatchTarget={type.FullName}.OnSubmenuStackChanged");
            return;
        }

        throw new MissingMethodException($"No supported main-menu _Ready() target was found: {string.Join(",", MainMenuTypeNames)}.");
    }

    internal static MethodInfo RequiredMethod(Type type, string name, Type[]? parameters = null) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName,
            parameters is null ? name : $"{name}({string.Join(",", parameters.Select(t => t.FullName))})");

    // These startup groups install only prefixes/postfixes. Roll back newly added
    // callbacks, retaining every callback already installed by this owner.
    internal static bool InstallFeature(Harmony harmony, string feature, Action install)
    {
        var before = OwnedCallbacks(harmony.Id);
        try
        {
            install();
            string targets = RuntimeLog.SafeJson(OwnedCallbacks(harmony.Id).Except(before)
                .Select(patch => $"{patch.Target.DeclaringType?.FullName}.{patch.Target} <- {patch.Callback.DeclaringType?.FullName}.{patch.Callback}"));
            RuntimeLog.Info($"startupPatchInstalled=true;feature={feature};owner={harmony.Id};targetsJson={targets}");
            return true;
        }
        catch (Exception error)
        {
            RuntimeLog.Fault($"startupFault=true;phase=PatchInstallation;feature={feature};owner={harmony.Id}", error);
            bool rollbackComplete = true;
            foreach (var (target, callback) in OwnedCallbacks(harmony.Id).Except(before))
            {
                try { harmony.Unpatch(target, callback); }
                catch (Exception cleanupError)
                {
                    rollbackComplete = false;
                    RuntimeLog.Fault($"startupPatchRollbackFailed=true;feature={feature};owner={harmony.Id};target={target};callback={callback}", cleanupError);
                }
            }
            RuntimeLog.Info($"startupPatchUnavailable=true;feature={feature};owner={harmony.Id};rollbackComplete={rollbackComplete.ToString().ToLowerInvariant()}");
            return false;
        }
    }

    private static HashSet<(MethodBase Target, MethodInfo Callback)> OwnedCallbacks(string owner)
    {
        var callbacks = new HashSet<(MethodBase, MethodInfo)>();
        foreach (MethodBase target in Harmony.GetAllPatchedMethods())
        {
            if (Harmony.GetPatchInfo(target) is not { } patches) continue;
            foreach (var patch in patches.Prefixes.Concat(patches.Postfixes))
                if (patch.owner == owner) callbacks.Add((target, patch.PatchMethod));
        }
        return callbacks;
    }

    private static void Postfix(object __instance)
    {
        if (__instance is NMainMenu mainMenu)
        {
            // Main-menu _Ready is the first source-audited Godot-main-thread boundary
            // where the log pump and launcher can be attached. The launcher owns
            // sliced candidate observation; actual entries establish fresh authority.
            try
            {
                RuntimeLog.BindMainThreadPump();
                // The launcher owns sliced menu preparation. Actual RT2/in-run
                // entry validates fresh native facts before any UI consumer.
            }
            catch (Exception ex)
            {
                RuntimeLog.WarnException("runtimeLogPumpBindFailed=true;failSoft=true", ex);
            }
            MinimalPanelInjector.EnsureAttached(mainMenu, ModRuntime.Snapshot);
        }
    }

    private static void SubmenuStackChangedPostfix(object __instance)
    {
        if (__instance is NMainMenu mainMenu)
        {
            AppShellHost.SyncMainMenuSurfaceVisibility(mainMenu);
        }
    }

    private static void RunTopBarPostfix(MegaCrit.Sts2.Core.Nodes.CommonUi.NTopBar __instance,
        MegaCrit.Sts2.Core.Runs.IRunState runState)
    {
        try { InRunPredictionLauncher.EnsureAttached(__instance, runState, ModRuntime.Snapshot); }
        catch (Exception ex) { RuntimeLog.WarnException("runPredictionLauncherAttachFailed=true", ex); }
        try { RolltheSpire2.Ui.InRunConsole.InRunConsoleLauncher.EnsureAttached(__instance, runState, ModRuntime.Snapshot); }
        catch (Exception ex) { RuntimeLog.WarnException("inRunConsoleAttachFailed=true", ex); }
    }
}
