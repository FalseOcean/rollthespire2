using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Shell;

internal static partial class UiSurfaceChecks
{
    // Reuses a native Crystal fixture and its verified option projection. The
    // caller owns the live scene; this check never starts a solver or changes it.
    internal static void RunCrystalLifecycleChecks(Node host,IRunState run,ModRuntimeSnapshot runtime,
        CrystalSphereLiveSnapshot snapshot,CrystalOptionProjection evidence,Action<bool,string> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Field(object instance,string name)=>instance.GetType().GetField(name,flags)!.GetValue(instance);
        void Set(object instance,string name,object? value)=>instance.GetType().GetField(name,flags)!.SetValue(instance,value);
        void Slice(CrystalSphereAssistantPanel panel)=>panel.GetType().GetMethod("ApplyPresentationSlice",flags)!.Invoke(panel,null);
        bool PresentationSettled(CrystalSphereAssistantPanel panel)=>!(bool)Field(panel,"_presentationRequested")! &&
            !(bool)Field(panel,"_presentationInvalidated")! &&
            ((Queue<CrystalRewardOption>)Field(panel,"_optionPresentationQueue")!).Count==0 &&
            (int)Field(panel,"_targetPresentationIndex")!>=((ImmutableArray<CrystalRewardOption>)Field(panel,"_selected")!).Length;
        var parent=new Control();host.AddChild(parent);parent.Hide();
        var panel=new CrystalSphereAssistantPanel();panel.Initialize(run,runtime);parent.AddChild(panel);panel.SetProcess(false);
        try
        {
            panel.ShowSnapshot(snapshot,startDiscovery:false);
            // The frozen input and witness were supplied by the caller. Clear
            // the index to isolate UI selection from authorizing worker execution.
            Set(panel,"_reachability",null);
            var queue=(ConcurrentQueue<CrystalSphereAssistantPanel.Update>)Field(panel,"_updates")!;
            for(int i=1;i<=25;i++) queue.Enqueue(new([],Progress:new(i,0,0,0,i)));
            panel._Process(0);
            check(queue.Count>=17 && queue.Count<25,"Crystal bounds receipt consumption per frame without dropping the backlog");
            var suspended=panel.SuspendSession()!;
            check(ReferenceEquals(suspended.Updates,queue) && queue.Count>=17,"Crystal close preserves unapplied receipts instead of draining them");
            panel.RestoreSession(suspended);
            for(int i=0;i<25 && !queue.IsEmpty;i++) panel._Process(0);
            check(queue.IsEmpty && ((CrystalDiscoveryProgress)Field(panel,"_progress")!).Examined==25,
                "Crystal hidden consumer eventually applies every receipt in order");
            panel.ApplyUpdate(new([],Projection:evidence));
            var tiles=(Dictionary<CrystalRewardOption,Control>)Field(panel,"_optionTiles")!;
            panel._Process(0);
            check(tiles.Count==0 && !panel.IsPresentationReady,"Crystal hidden evidence does not construct artwork or report a presented page");

            // Exercise the presentation slice directly on the main thread so
            // this focused check also works in the existing dummy renderer host.
            // This bypasses visibility only in this structural check. Do not
            // inject a successful scene check: the supplied snapshot may not be
            // this host's actual board, so IsPresentationReady must stay false.
            Slice(panel);
            check(tiles.Count<=4 && (!evidence.Available.Keys.Any(o=>o!=CrystalRewardOption.AnyRelic) || tiles.Count>0),"Crystal cold tile construction is a bounded slice");
            var deferred=evidence.Available.Keys.Where(o=>o!=CrystalRewardOption.AnyRelic && !tiles.ContainsKey(o)).ToArray();
            if(deferred.Length>0 && tiles.Keys.FirstOrDefault() is { } chosen && tiles.Count>0)
            {
                panel.SelectOption(chosen);
                var pending=(HashSet<CrystalRewardOption>)Field(panel,"_pendingOptions")!;
                check(deferred.All(pending.Contains),"Crystal selecting an early tile retains candidates whose controls have not yet been built");
                // Restore the original witness set to check reconciliation and
                // stable control identity independently of the new selection.
                panel.RestoreSession(suspended with { Projection=evidence,Updates=new(),Worker=null,Resume=false });
                Set(panel,"_reachability",null);
            }
            for(int i=0;i<evidence.Available.Count+10 && !PresentationSettled(panel);i++) Slice(panel);
            check(PresentationSettled(panel) && !panel.IsPresentationReady &&
                tiles.Count==evidence.Available.Keys.Count(o=>o!=CrystalRewardOption.AnyRelic),
                "Crystal controls settle independently while actual board readiness remains unvalidated");
            var resultContainer=(Control)Field(panel,"_results")!;
            if(tiles.Keys.FirstOrDefault() is { } earlyChoice)
            {
                panel.SelectOption(earlyChoice);
                check((bool)Field(panel,"_presentationInvalidated")! && !resultContainer.Visible,
                    "Crystal selection immediately invalidates old confirmed artwork before bounded restyling");
                Slice(panel);
                var restyling=(Queue<CrystalRewardOption>)Field(panel,"_optionPresentationQueue")!;
                check(restyling.Where(tiles.ContainsKey).All(option=>!tiles[option].Visible),
                    "Crystal tiles awaiting a changed pending or removed state are hidden before the queue settles");
                if(evidence.Available.Keys.Count(o=>o!=CrystalRewardOption.AnyRelic)>4)
                    check(restyling.Count>0,"Crystal invalidation assertion observes a genuinely unsettled restyle queue");
                panel.RestoreSession(suspended with { Projection=evidence,Updates=new(),Worker=null,Resume=false });
                Set(panel,"_reachability",null);
                for(int i=0;i<evidence.Available.Count+10 && !PresentationSettled(panel);i++) Slice(panel);
            }
            panel.ShowSnapshot(snapshot,startDiscovery:false);Set(panel,"_reachability",null);
            check((bool)Field(panel,"_presentationInvalidated")! && !resultContainer.Visible && !panel.IsPresentationReady,
                "Crystal refreshed query immediately hides old artwork without certifying the supplied board");
            Slice(panel);
            check(tiles.Values.All(tile=>!tile.Visible),"Crystal removed tiles remain hidden while refreshed-query removals drain");
            panel.RestoreSession(suspended with { Projection=evidence,Updates=new(),Worker=null,Resume=false });
            Set(panel,"_reachability",null);
            for(int i=0;i<evidence.Available.Count+10 && !PresentationSettled(panel);i++) Slice(panel);
            var identities=tiles.ToDictionary(p=>p.Key,p=>p.Value);
            panel.ApplyUpdate(new([],Progress:new(26,0,0,0,26),Projection:evidence));
            check(!(bool)Field(panel,"_presentationInvalidated")! && identities.All(p=>tiles[p.Key].Visible),
                "Crystal harmless same-request progress does not invalidate confirmed tile presentation");
            Slice(panel);
            check(identities.All(p=>tiles.TryGetValue(p.Key,out var tile) && ReferenceEquals(tile,p.Value)),
                "Crystal progress and repeated evidence preserve tile controls and visual order");
            panel.Hide();panel.ApplyLanguage((Field(panel,"_language") as string)=="en"?"zh":"en");panel._Process(0);
            check(identities.All(p=>tiles.TryGetValue(p.Key,out var tile) && ReferenceEquals(tile,p.Value)),
                "Crystal hidden language changes defer expensive controls while retaining evidence");
            check(ReferenceEquals(((CrystalOptionProjection)Field(panel,"_projection")!).SelectedPlan,evidence.SelectedPlan),
                "Crystal presentation lifecycle preserves the verified selected plan");
        }
        finally
        {
            parent.Free();
            check(((ConcurrentQueue<CrystalSphereAssistantPanel.Update>)Field(panel,"_updates")!).IsEmpty &&
                ((Dictionary<CrystalRewardOption,Control>)Field(panel,"_optionTiles")!).Count==0,
                "Crystal retirement releases panel queues and control references");
        }
    }
}
