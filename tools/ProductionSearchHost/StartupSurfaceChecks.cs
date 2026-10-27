using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;

// A bounded check in the existing Host, not a second bootstrap implementation.
internal static class StartupSurfaceChecks
{
    internal static async Task RunAsync(Node root, string output, string? baseline)
    {
        var assembly = typeof(ModEntry).Assembly;
        var entry = assembly.GetTypes().Single(t => t.GetCustomAttribute<ModInitializerAttribute>() is not null);
        var loader = typeof(ModManager).GetMethod("CallModInitializer", BindingFlags.Static | BindingFlags.NonPublic)!;
        if (!(bool)loader.Invoke(null, [entry])!) throw new InvalidOperationException("ActualModLoaderEntryFailed");
        // A capture patch under this owner cannot stand in for the real menu,
        // TopBar or complete map observer/consumer group.
        const string startupOwner = "RolltheSpire2.MainMenu";
        CheckIndependentPatches(startupOwner);
        CheckMapPatches(startupOwner);
        CheckMissingMapIsolation();
        using var entryNode = new ModEntry();
        if (!GodotObject.IsInstanceValid(entryNode)) throw new InvalidOperationException("GodotEntryBindingFailed");

        string[] resources = assembly.GetManifestResourceNames();
        if (resources.Any(name => name.EndsWith(".ContentNames.fallback.json", StringComparison.Ordinal)))
            throw new InvalidOperationException("RetiredContentNameResourcePresent");
        if (!string.IsNullOrEmpty(baseline))
        {
            var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baseline))!;
            if (resources.Length != before.Keys.Count(name => MigratedResourceName(name) is not null))
                throw new InvalidOperationException("ResourceCountChanged");
            foreach (var (oldName, hash) in before)
            {
                // Comparison with the historical artifact, not a runtime compatibility alias.
                string? name = MigratedResourceName(oldName);
                if (name is null) continue;
                using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("MissingResource:" + name);
                if (Convert.ToHexString(SHA256.HashData(stream)) != hash) throw new InvalidOperationException("ResourceBytesChanged:" + name);
            }
        }
        object New(string name, params object[] args) => Activator.CreateInstance(assembly.GetType("RolltheSpire2." + name, true)!, args)!;
        object Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name)!.Invoke(value, args)!;
        var provider = New("Ui.Persistence.BuiltInSearchPresetProvider");
        var presets = ((System.Collections.IEnumerable)Call(provider, "GetPresets")).Cast<object>().ToArray();
        string[] expectedIds = resources.Where(n => n.StartsWith("RolltheSpire2.OfficialPresets.", StringComparison.Ordinal))
            .Select(n => "official:" + System.IO.Path.GetFileNameWithoutExtension(n["RolltheSpire2.OfficialPresets.".Length..])).Order().ToArray();
        string[] ids = presets.Select(p => (string)p.GetType().GetProperty("Id")!.GetValue(p)!).Order().ToArray();
        if (!ids.SequenceEqual(expectedIds)) throw new InvalidOperationException("OfficialPresetIdentityChanged");

        var canvas = new Control { Size = new Vector2(1600, 900) };
        root.AddChild(canvas);
        var search = new RolltheSpire2.Ui.Shell.WorkspaceShell();
        // The calling native UI host initializes 0.111.0 catalogs. As in the
        // existing workspace fixtures, give rendering an explicit test context;
        // the actual loader and installed patches were checked independently.
        var detection = new GameVersionDetection("0.111.0", "0.111.0", "beta", "StartupSurfaceFixture", true, "");
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance,
            Detection = detection, Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        search.InitializeWithPersistence(runtime, Path.Combine(output, "startup-state"));
        canvas.AddChild(search);
        try
        {
            for (int i = 0; i < 4; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            root.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(output, "search.png"));
            File.WriteAllText(System.IO.Path.Combine(output, "startup.json"), JsonSerializer.Serialize(new { entry = entry.FullName, resources = resources.Length, officialIds = ids, loader = "PASS", menuPatch = "PASS", topBarPatch = "PASS", mapPatches = "PASS", missingMapIsolation = "PASS", patchRollback = "PASS", workspace = "PASS" }));
            GD.Print($"StartupSurface=PASS;loaderEntry={entry.FullName};godotBinding=PASS;menuPatch=PASS;topBarPatch=PASS;mapPatches=PASS;missingMapIsolation=PASS;patchRollback=PASS;resources={resources.Length};officialIds=PASS;Workspace=PASS");
        }
        finally { canvas.QueueFree(); }
    }

    private static void CheckIndependentPatches(string owner)
    {
        RequirePatch(owner, typeof(MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame), "CellClicked",
            [typeof(MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereCell)], typeof(CrystalSphereLiveCapture), "ObserveClick");
        RequirePatch(owner, typeof(MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere.NCrystalSphereMask), "_Ready",
            Type.EmptyTypes, typeof(RolltheSpire2.Ui.Shell.CrystalSphereMaskAppearance), "ReadyPostfix");
        RequirePatch(owner, typeof(NMainMenu), "_Ready", Type.EmptyTypes, typeof(MainMenuPatchInstaller), "Postfix");
        RequirePatch(owner, typeof(NMainMenu), "OnSubmenuStackChanged", Type.EmptyTypes, typeof(MainMenuPatchInstaller), "SubmenuStackChangedPostfix");
        RequirePatch(owner, typeof(NTopBar), "Initialize", [typeof(IRunState)], typeof(MainMenuPatchInstaller), "RunTopBarPostfix");
        RequirePatch(owner, typeof(NOverlayStack), "HideOverlays", Type.EmptyTypes, typeof(InRunConsolePresencePatch), "RestoreConsole");
        RequirePatch(owner, typeof(NOverlayStack), "Push", [typeof(IOverlayScreen)], typeof(InRunConsolePresencePatch), "RestoreConsole");
        RequirePatch(owner, typeof(ActiveScreenContext), "GetCurrentScreen", Type.EmptyTypes, typeof(InRunConsolePresencePatch), "CurrentScreen");
    }

    private static void CheckMapPatches(string owner)
    {
        RequirePatch(owner, typeof(RunManager), "EnterMapCoord", [typeof(MapCoord)], typeof(PredictorLiveRunCapture), "BeforeNativeNodeEntry", prefix: true);
        RequirePatch(owner, typeof(RunManager), "EnterMapCoord", [typeof(MapCoord)], typeof(PredictorLiveRunCapture), "AfterNativeNodeEntry");
        RequirePatch(owner, typeof(NMapScreen), "Initialize", [typeof(RunState)], typeof(InRunMapPredictionPatch), "Initialized");
        RequirePatch(owner, typeof(NMapScreen), "SetMap", [typeof(ActMap), typeof(ulong), typeof(bool)], typeof(InRunMapPredictionPatch), "MapChanged");
        RequirePatch(owner, typeof(NMapScreen), "Open", [typeof(bool)], typeof(InRunMapPredictionPatch), "Opened");
        RequirePatch(owner, typeof(NMapScreen), "Close", [typeof(bool)], typeof(InRunMapPredictionPatch), "Closed");
        RequirePatch(owner, typeof(NMapScreen), "ProcessMouseDrawingEvent", [typeof(InputEvent)], typeof(InRunMapPredictionPatch), "DrawingDispatch", prefix: true);
        RequirePatch(owner, typeof(NMapDrawings), "BeginLineLocal", [typeof(Vector2), typeof(DrawingMode?)], typeof(InRunMapPredictionPatch), "LineBegan");
        RequirePatch(owner, typeof(NMapDrawings), "UpdateCurrentLinePositionLocal", [typeof(Vector2)], typeof(InRunMapPredictionPatch), "LineSampled");
        RequirePatch(owner, typeof(NMapDrawings), "StopLineLocal", Type.EmptyTypes, typeof(InRunMapPredictionPatch), "LineEnded");
        RequirePatch(owner, typeof(NMapDrawings), "ClearDrawnLinesLocal", Type.EmptyTypes, typeof(InRunMapPredictionPatch), "LinesCleared");
    }

    private static void RequirePatch(string owner, Type targetType, string name, Type[] parameters,
        Type callbackType, string callbackName, bool prefix = false)
    {
        MethodInfo target = MainMenuPatchInstaller.RequiredMethod(targetType, name, parameters);
        MethodInfo callback = MainMenuPatchInstaller.RequiredMethod(callbackType, callbackName);
        if (!HasPatch(owner, target, callback, prefix))
            throw new InvalidOperationException($"StartupPatchMissing:owner={owner};target={targetType.FullName}.{target};kind={(prefix ? "prefix" : "postfix")};callback={callback}");
    }

    private static bool HasPatch(string owner, MethodInfo target, MethodInfo callback, bool prefix = false)
    {
        var patches = Harmony.GetPatchInfo(target);
        return patches is not null && (prefix ? patches.Prefixes : patches.Postfixes)
            .Any(patch => patch.owner == owner && patch.PatchMethod == callback);
    }

    private static void CheckMissingMapIsolation()
    {
        var fixture = new Harmony("RolltheSpire2.StartupSurface.MissingMap");
        try
        {
            int missingCalls = 0;
            MainMenuPatchInstaller.Install(fixture, (type, name, parameters) =>
            {
                if (type == typeof(NMapDrawings) && name == "ClearDrawnLinesLocal")
                {
                    missingCalls++;
                    return null;
                }
                return AccessTools.Method(type, name, parameters);
            });
            if (missingCalls != 1) throw new InvalidOperationException("ControlledMissingMapTargetNotExercised");
            CheckIndependentPatches(fixture.Id);
            // Even the entry observer and earlier map methods must remain absent.
            foreach (var target in Harmony.GetAllPatchedMethods())
                if (target.DeclaringType is { } type && (type == typeof(RunManager) || type == typeof(NMapScreen) || type == typeof(NMapDrawings)) &&
                    Harmony.GetPatchInfo(target)?.Owners.Contains(fixture.Id) == true)
                    throw new InvalidOperationException("MissingMapTargetLeftPartialFeature:" + target);

            // Exercise an actual installation failure after a callback was added.
            MethodInfo close = MainMenuPatchInstaller.RequiredMethod(typeof(NMapScreen), "Close", [typeof(bool)]);
            MethodInfo probe = MainMenuPatchInstaller.RequiredMethod(typeof(StartupSurfaceChecks), nameof(RollbackProbe));
            if (MainMenuPatchInstaller.InstallFeature(fixture, "ControlledRollback", () =>
                {
                    fixture.Patch(close, postfix: new HarmonyMethod(probe));
                    throw new InvalidOperationException("ControlledStartupInstallationFailure");
                }))
                throw new InvalidOperationException("FailedStartupFeatureReportedSuccess");
            if (HasPatch(fixture.Id, close, probe)) throw new InvalidOperationException("StartupFeatureRollbackLeftCallback");
            CheckIndependentPatches(fixture.Id);
        }
        finally
        {
            // Fixture cleanup is restricted to this owner; retain the actual Mod.
            foreach (MethodBase target in Harmony.GetAllPatchedMethods().ToArray())
                fixture.Unpatch(target, HarmonyPatchType.All, fixture.Id);
        }
        CheckIndependentPatches("RolltheSpire2.MainMenu");
        CheckMapPatches("RolltheSpire2.MainMenu");
    }

    private static void RollbackProbe() { }

    private static string? MigratedResourceName(string oldName)
    {
        // Only the baseline reader knows historical physical names. Product loaders use the current layout.
        string name = oldName.Replace("RolltheSpire2.Rewrite.", "RolltheSpire2.");
        foreach (string language in new[] { "zh", "en" })
        {
            // These two name tables were explicitly retired; all other baseline
            // resources still participate in the count and byte comparison.
            if (name == $"RolltheSpire2.Localization.game_content.{language}.json" ||
                name == $"RolltheSpire2.Texts.{language}.ContentNames.fallback.json") return null;
            foreach (var (group, path) in new[]
            {
                ("rt2_ui_legacy", "UI.legacy"), ("rt2_ui_v13", "UI.workbench")
            })
                if (name == $"RolltheSpire2.Localization.{group}.{language}.json")
                    return $"RolltheSpire2.Texts.{language}.{path}.json";
            const string encyclopediaPrefix = "RolltheSpire2.Encyclopedia.";
            string articleSuffix = $"_{language}.md";
            if (name.StartsWith(encyclopediaPrefix, StringComparison.Ordinal) && name.EndsWith(articleSuffix, StringComparison.Ordinal))
                return $"RolltheSpire2.Texts.{language}.Encyclopedia.{name[encyclopediaPrefix.Length..^articleSuffix.Length]}.md";
            const string notesPrefix = "RolltheSpire2.DeveloperNotes.";
            string chapterSuffix = language == "zh" ? ".zh-CN.json" : ".en-US.json";
            if (name.StartsWith(notesPrefix, StringComparison.Ordinal) && name.EndsWith(chapterSuffix, StringComparison.Ordinal))
                return $"RolltheSpire2.Texts.{language}.DeveloperNotes.{name[notesPrefix.Length..^chapterSuffix.Length]}.json";
        }
        return name == "RolltheSpire2.DeveloperNotes.index.json"
            ? "RolltheSpire2.Infrastructure.DeveloperNotes.index.json" : name;
    }
}
