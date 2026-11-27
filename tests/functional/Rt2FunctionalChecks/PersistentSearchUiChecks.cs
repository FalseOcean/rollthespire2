using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Tests.FastSearch;

internal static class PersistentSearchUiChecks
{
    internal static int Run()
    {
        var runtime = NativeWorkbenchCheckEnvironment.Initialize("PersistentSearch");
        var scene = new Control { Size = new(1600, 900) };
        ((SceneTree)Engine.GetMainLoop()).CurrentScene.AddChild(scene);
        var store = SearchWorkspace.Open(OS.GetUserDataDir());
        var menu = new WorkspaceShell();
        menu.Initialize(runtime);
        scene.AddChild(menu);
        menu.Open();
        AccessTools.Field(typeof(WorkspaceShell), "_applyingPreparedPage").SetValue(menu, true);
        ((Button)AccessTools.Field(typeof(WorkspaceShell), "_settings").GetValue(menu)!).EmitSignal(Button.SignalName.Pressed);
        ((Button)menu.FindChild("SettingsCategory2", owned: false)).EmitSignal(BaseButton.SignalName.Pressed);
        var toggle = (CheckButton)menu.FindChild("SettingsPersistentSearch", owned: false);
        toggle.ButtonPressed = true;
        if (!store.Preferences.PersistentSearch) throw new Exception("Settings toggle did not enable persistent search");
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = RunState.CreateForTest([player], seed: "000000000000");
        var draft = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Ironclad, 10,
            SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) },
            AncientOptionConditionProfile.BroadDefault);
        bool checkGpu = System.Environment.GetEnvironmentVariable("RT2_PERSISTENT_SEARCH_GPU_CHECK") == "1";
        store.Preferences.SearchMode = checkGpu ? "AUTO" : "CPU";
        RolltheSpire2.Search.Runtime.ProductionSearchSession Session()
        {
            var active = AccessTools.Field(store.Runtime.GetType(), "_execution").GetValue(store.Runtime)!;
            return (RolltheSpire2.Search.Runtime.ProductionSearchSession)AccessTools.Property(active.GetType(), "Session").GetValue(active)!;
        }
        void WaitFor(Func<bool> condition)
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && wait.Elapsed.TotalSeconds < 35)
            {
                store.Runtime.Tick(.01);
                Thread.Sleep(10);
            }
            if (!condition()) throw new Exception("Search timed out: " + store.Runtime.SessionProgress);
        }
        WorkspaceShell OpenRunSearch()
        {
            var shell = new WorkspaceShell();
            shell.InitializeRunScene(runtime, run);
            scene.AddChild(shell);
            shell.Open();
            if (shell.FindChild("RunSearchMonitor", owned: false) is not Control page || !page.IsVisibleInTree())
                throw new Exception("Icon entry did not return to the background search");
            return shell;
        }
        try
        {
            store.Runtime.Start(draft, runtime, 100000, 2, menu.GetTree());
            menu.CleanupForTopLevelClose();
            var session = Session();
            if (!session.PlayingBudget) throw new Exception("Closing the menu did not reduce the search budget");
            var overlay = OpenRunSearch();
            WaitFor(() => store.Runtime.SessionProgress?.ScannedCount > 0);
            if (checkGpu && !session.ExecutionPlan.ExecutionOrderUnits.Any(body => body.UsesGpu))
                throw new Exception("GPU check selected a CPU plan: " + session.ExecutionPlan.PlanId);
            overlay.CleanupForTopLevelClose();
            overlay.Free();
            menu.Open();
            if (session.PlayingBudget || !ReferenceEquals(session, Session()) || !store.Runtime.Receiving || menu.FindChild("SearchRuntime", owned: false) is not Control view || !view.IsVisibleInTree())
                throw new Exception("Menu failed to reconnect to the live results view");
            menu.CleanupForTopLevelClose();
            overlay = OpenRunSearch();
            var stop = (Button)AccessTools.Field(typeof(WorkspaceShell), "_runSearchStop").GetValue(overlay)!;
            stop.EmitSignal(Button.SignalName.Pressed);
            WaitFor(() => !store.Runtime.Receiving);
            if (store.Runtime.SessionProgress?.State != SearchRunState.Cancelled) throw new Exception("In-run stop failed");
            overlay.Free();

            store.Runtime.Start(draft, runtime, 1, 1, menu.GetTree());
            menu.CleanupForTopLevelClose();
            WaitFor(() => !store.Runtime.Receiving);
            overlay = OpenRunSearch();
            var rows = (VBoxContainer)AccessTools.Field(typeof(WorkspaceShell), "_runSearchRows").GetValue(overlay)!;
            if (store.Runtime.SessionProgress?.State != SearchRunState.Completed || store.Runtime.SessionResults.Count == 0 ||
                rows.GetChildCount() != store.Runtime.SessionResults.Count) throw new Exception("Unattended results missing");
            overlay.Free();
            Console.WriteLine("PERSISTENT_SEARCH_UI_PASS settings, closed-panel progress, menu reattach, room reattach, stop, unattended result rows");
        }
        finally
        {
            store.Runtime.Cancel();
            store.SetPersistentSearch(false);
            scene.Free();
        }
        return 0;
    }
}
