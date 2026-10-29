using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class UiSurfaceChecks
{
    private static async Task RunManualFamilyOrderUi(Node root, string output, WorkspaceShell shell,
        SearchWorkspace persistence, QueryWorkbenchFrame frame)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(string name) => typeof(QueryWorkbenchFrame).GetField(name, flags)!.GetValue(frame)!;
        void Call(string name, params object?[] values) => typeof(QueryWorkbenchFrame).GetMethod(name, flags)!.Invoke(frame, values);
        void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ManualOrderUi: " + why); }
        shell.Open(); await AwaitPageReady(root, shell);
        frame.SetProcess(false); Call("SuspendProbabilityPreview");
        string corpus = ProjectSettings.GlobalizePath("res://Corpus/owner-nwae-v1");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(corpus, "context.json"));
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus, "NWAE_01.json"));
        var compiled = SearchCompiler.Compile(workload.Query, context.Context);
        var automatic = WorkbenchProbabilityPreview.Compute(compiled, 3, 1, cpuOnly: true);
        Require(automatic.OrderIssue.Length == 0 && automatic.FamilyOrder.Count >= 2, "current compiler provides real automatic units");
        Call("ApplyExecutionOrderPreview", automatic);
        var mode = (OptionButton)Field("_executionOrderMode");
        Require(frame.CaptureDraft().ManualFamilyOrder is null && mode.Selected == 0,
            "automatic preview does not author a manual request");
        mode.Select(1); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Require(frame.CaptureDraft().ManualFamilyOrder!.SequenceEqual(automatic.FamilyOrder), "manual begins at actual automatic order");
        var down = frame.FindChild("MoveOrderDown0", true, false) as Button;
        Require(down is { Disabled: false }, "ordinary units can move down");
        down!.EmitSignal(Button.SignalName.Pressed);
        var authored = frame.CaptureDraft().ManualFamilyOrder!.ToArray();
        Require(authored[0] == automatic.FamilyOrder[1] && authored[1] == automatic.FamilyOrder[0],
            "actual move button changes the captured request preference");
        var manual = WorkbenchProbabilityPreview.Compute(compiled, 3, 1, cpuOnly: true, authored);
        Require(manual.OrderIssue.Length == 0 && manual.FamilyOrder.SequenceEqual(authored), "preview preserves the authored execution order");
        Call("ApplyExecutionOrderPreview", manual);
        var draft = frame.CaptureDraft(); persistence.SaveWorkbench(draft);
        Require(persistence.LoadWorkbench()!.ManualFamilyOrder!.SequenceEqual(authored), "existing draft persistence retains order");
        var frozen = persistence.LoadWorkbench()!;
        frame.RestoreDraft(frozen, render: false);
        Require(frame.CaptureDraft().ManualFamilyOrder!.SequenceEqual(authored), "draft restore retains manual mode and relative order");
        mode.Select(0); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
        Require(frame.CaptureDraft().ManualFamilyOrder is null, "automatic switch clears only manual ordering");

        foreach (string language in new[] { "zh", "en" })
        {
            persistence.SetLanguageOverride(language);
            typeof(WorkspaceShell).GetMethod("RefreshLanguage", flags)!.Invoke(shell, null);
            frame.Refresh(language, JsonUiTextProvider.CreateUi13(language)); frame.SetProcess(false); Call("SuspendProbabilityPreview");
            Call("ApplyExecutionOrderPreview", automatic);
            mode = (OptionButton)Field("_executionOrderMode");
            mode.Select(1); mode.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            for (int frameAt = 0; frameAt < 3; frameAt++)
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var start = (Button)Field("_start");
            var list = (ScrollContainer)Field("_executionOrderScroll");
            Require(start.IsVisibleInTree() && start.GetGlobalRect().Position.Y >= list.GetGlobalRect().End.Y &&
                list.Size.Y <= 134, "bounded order list leaves Start visible/" + language);
            var orderRow = (HBoxContainer)frame.FindChild("ExecutionOrderUnit0", true, false)!;
            var unitTitle = (Label)orderRow.GetChild(0);
            var moveDown = (Button)orderRow.GetChild(2);
            Require(unitTitle.IsVisibleInTree() && unitTitle.Size.X >= 80 && unitTitle.Size.Y >= 20 &&
                moveDown.GetGlobalRect().End.X <= list.GetGlobalRect().End.X,
                "family title has a visible text area and move buttons fit/" + language);
            var live = (GridContainer)Field("_liveReadings");
            Require(live.GetParent() == (Control)Field("_runtimeSurface") && live.Columns == 3,
                "live readings belong to the central runtime column");
            using var image = root.GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(output, "manual-order-" + language + ".png"));
            typeof(QueryWorkbenchFrame).GetField("_showResults", flags)!.SetValue(frame, true);
            Call("LockForRun", false); Call("UpdateRuntimeVisibility");
            await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Require(live.IsVisibleInTree() && live.GetGlobalRect().Position.X < start.GetGlobalRect().Position.X,
                "live readings are visible in the middle results area");
            using var runtimeImage = root.GetViewport().GetTexture().GetImage();
            runtimeImage.SavePng(Path.Combine(output, "manual-order-runtime-" + language + ".png"));
            typeof(QueryWorkbenchFrame).GetField("_showResults", flags)!.SetValue(frame, false);
            Call("LockForRun", false);
        }
        File.WriteAllText(Path.Combine(output, "manual-order-ui.json"), JsonSerializer.Serialize(new {
            passed = true, scope = "real automatic-plan preview;native mode+move callbacks;draft save/restore;central live-reading ownership;zh/en layout",
            searchStarted = false, numericalGpuChecks = false, ownerRuntimeAcceptance = "pending"
        }));
        GD.Print("MANUAL_ORDER_UI_PASS");
    }
}
