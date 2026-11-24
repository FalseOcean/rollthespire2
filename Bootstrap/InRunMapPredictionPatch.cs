using Godot;
using HarmonyLib;
using System.Reflection;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunMap;

namespace RolltheSpire2.Bootstrap;

// Native map tools, ink, networking and travel retain ownership. Prediction
// observes only newly authored local strokes, independently of display visibility.
internal static class InRunMapPredictionPatch
{
    internal static void Install(Harmony harmony, Func<Type, string, Type[], MethodInfo?>? targetResolver = null)
    {
        // Resolve the complete observer/consumer group before installing anything.
        // The resolver is local to this call, allowing the native host to exercise
        // a missing game target without changing process-wide reflection state.
        targetResolver ??= (type, name, parameters) => AccessTools.Method(type, name, parameters);
        MethodInfo Target(Type type, string method, params Type[] parameters) =>
            targetResolver(type, method, parameters) ?? throw new MissingMethodException(type.FullName,
                $"{method}({string.Join(",", parameters.Select(t => t.FullName))})");
        HarmonyMethod Callback(string name) => new(MainMenuPatchInstaller.RequiredMethod(typeof(InRunMapPredictionPatch), name));
        MethodInfo entryTarget = Target(typeof(RunManager), nameof(RunManager.EnterMapCoord), typeof(MapCoord));
        _ = MainMenuPatchInstaller.RequiredMethod(typeof(PredictorLiveRunCapture), "BeforeNativeNodeEntry");
        _ = MainMenuPatchInstaller.RequiredMethod(typeof(PredictorLiveRunCapture), "AfterNativeNodeEntry");
        (MethodInfo Target, HarmonyMethod? Prefix, HarmonyMethod? Postfix)[] patches =
        [
            (Target(typeof(NMapScreen), "Initialize", typeof(RunState)), null, Callback(nameof(Initialized))),
            (Target(typeof(NMapScreen), "SetMap", typeof(ActMap), typeof(ulong), typeof(bool)), null, Callback(nameof(MapChanged))),
            (Target(typeof(NMapScreen), "Open", typeof(bool)), null, Callback(nameof(Opened))),
            (Target(typeof(NMapScreen), "Close", typeof(bool)), null, Callback(nameof(Closed))),
            (Target(typeof(NMapScreen), "ProcessMouseDrawingEvent", typeof(InputEvent)), Callback(nameof(DrawingDispatch)), null),
            (Target(typeof(NMapDrawings), "BeginLineLocal", typeof(Vector2), typeof(DrawingMode?)), null, Callback(nameof(LineBegan))),
            (Target(typeof(NMapDrawings), "UpdateCurrentLinePositionLocal", typeof(Vector2)), null, Callback(nameof(LineSampled))),
            (Target(typeof(NMapDrawings), "StopLineLocal"), null, Callback(nameof(LineEnded))),
            (Target(typeof(NMapDrawings), "ClearDrawnLinesLocal"), null, Callback(nameof(LinesCleared)))
        ];
        PredictorLiveRunCapture.InstallNodeEntryObserver(harmony, entryTarget);
        foreach (var patch in patches)
            harmony.Patch(patch.Target, prefix: patch.Prefix, postfix: patch.Postfix);
    }

    private static void Initialized(NMapScreen __instance, RunState runState)
    {
        try { InRunMapPredictionPanel.EnsureAttached(__instance, runState, ModRuntime.Snapshot); }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapPredictionAttachFailed=true", ex); }
    }
    private static void MapChanged(NMapScreen __instance) => Surface(__instance)?.OnMapChanged();
    private static void Opened(NMapScreen __instance) => Surface(__instance)?.OnMapOpened();
    private static void Closed(NMapScreen __instance) => Surface(__instance)?.OnMapClosed();
    private static bool DrawingDispatch(NMapScreen __instance, InputEvent inputEvent)
    {
        try { return Surface(__instance)?.AllowsNativeDrawing(inputEvent) ?? true; }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapDrawingInputGuardFailed=true", ex); return true; }
    }
    private static void LineBegan(NMapDrawings __instance, Vector2 position, DrawingMode? overrideDrawingMode)
    {
        try
        {
            Surface(__instance)?.BeginNativeStroke(__instance, position,
                overrideDrawingMode ?? __instance.GetLocalDrawingMode(useOverride: false));
        }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapNativeStrokeBeginFailed=true", ex); }
    }
    private static void LineSampled(NMapDrawings __instance, Vector2 position)
    {
        try { Surface(__instance)?.SampleNativeStroke(__instance, position); }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapNativeStrokeSampleFailed=true", ex); }
    }
    private static void LineEnded(NMapDrawings __instance)
    {
        try { Surface(__instance)?.EndNativeStroke(__instance); }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapNativeStrokeEndFailed=true", ex); }
    }
    private static void LinesCleared(NMapDrawings __instance)
    {
        try { Surface(__instance)?.ClearNativePlans(); }
        catch (Exception ex) { RuntimeLog.WarnException("inRunMapNativePlansClearFailed=true", ex); }
    }
    private static InRunMapPredictionPanel? Surface(NMapDrawings drawings)
    {
        if (!GodotObject.IsInstanceValid(drawings)) return null;
        for (Node? parent = drawings.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is NMapScreen screen) return Surface(screen);
        return null;
    }
    private static InRunMapPredictionPanel? Surface(NMapScreen screen) =>
        GodotObject.IsInstanceValid(screen) ? screen.GetNodeOrNull<InRunMapPredictionPanel>(InRunMapPredictionPanel.NodeName) : null;
}
