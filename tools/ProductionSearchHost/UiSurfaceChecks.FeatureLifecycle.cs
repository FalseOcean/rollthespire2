using System.Text.Json;
using Godot;
using HarmonyLib;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunMap;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task CheckMapFeatureLifecycle(Node root, InRunMapPredictionPanel panel,
        Action<bool, string> check, Func<Task>? nativeGesture = null)
    {
        var workspace = new SearchWorkspace(OS.GetUserDataDir(), false);
        bool enabled = workspace.Preferences.MapPredictionEnabled;
        bool processing = panel.IsProcessing();
        var draft = panel.DisplayedDraft!;
        var selected = draft.Selected;
        var session = PredictorOperationCapture.Read<InRunMapPredictionSession>(panel, "_prediction");
        string plans = JsonSerializer.Serialize(session.Plans);
        int oldEditCalls = 0;
        async Task Frames()
        { for (int i = 0; i < 4; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        void QueueEdit() => AccessTools.Method(typeof(InRunMapPredictionPanel), "DeferInformationEdit")
            .Invoke(panel, [(Func<bool>)(() => { oldEditCalls++; return false; })]);
        try
        {
            panel.OnMapChanged(); QueueEdit();
            long revision = session.Revision;
            workspace.SetMapPredictionEnabled(false);
            check(!panel.Visible && !panel.IsProcessing() && !panel.PredictionEnabled,
                "global map disable suspends the default forecast without a second local mode");
            check(panel.AllowsNativeDrawing(new InputEventMouseButton { Pressed = true,
                ButtonIndex = MouseButton.Right, GlobalPosition = panel.GetGlobalRect().GetCenter() }),
                "disabled map prediction does not intercept native drawing, even at its former panel");
            if (nativeGesture != null) await nativeGesture();
            panel.OnMapChanged(); await Frames();
            check(session.Revision == revision && oldEditCalls == 0 &&
                panel.DisplayedDraft!.Selected.SequenceEqual(selected) && JsonSerializer.Serialize(session.Plans) == plans,
                "disable preserves route and choices while pending refresh/edit and native ink cannot compute or change them");
            workspace.SetMapPredictionEnabled(true);
            QueueEdit();
            workspace.SetMapPredictionEnabled(false); workspace.SetMapPredictionEnabled(true);
            await Frames();
            check(oldEditCalls == 0 && panel.Visible && panel.PredictionEnabled &&
                panel.FindChild("PredictionToggle", true, false) == null && panel.DisplayedDraft!.Selected.SequenceEqual(selected) &&
                JsonSerializer.Serialize(session.Plans) == plans,
                "rapid off/on invalidates earlier deferred edits and refreshes the current route with its choices intact");
        }
        finally
        {
            workspace.SetMapPredictionEnabled(enabled);
            panel.SetProcess(processing);
        }
    }
}
