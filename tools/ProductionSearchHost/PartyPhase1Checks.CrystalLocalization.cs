using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class PartyPhase1Checks
{
    private static void CheckCrystalLocalization(Node host,ModRuntimeSnapshot runtime,RunState outsideRun,
        RunState crystalRun,EventModel liveEvent,CrystalSphereLiveSnapshot capture,
        PredictorCrystalSolution solution,Action<bool,string> check)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Field(object instance,string name)=>instance.GetType().GetField(name,flags)!.GetValue(instance);
        void Call(object instance,string name,params object?[] args)=>instance.GetType().GetMethod(name,flags)!.Invoke(instance,args);
        string Text(string language,string key)=>JsonUiTextProvider.CreatePredictorUi13(language).Get("predictor.crystal."+key);
        var workspace=SearchWorkspace.Open(OS.GetUserDataDir());
        string previousLanguage=workspace.Preferences.LanguageOverride;
        string previousLocale=TranslationServer.GetLocale();
        var previousLocal=LocalContext.NetId;
        var syncProperty=typeof(RunManager).GetProperty(nameof(RunManager.EventSynchronizer))!;
        var previousSync=syncProperty.GetValue(RunManager.Instance);
        var panel=new CrystalSphereAssistantPanel();
        var guide=new CrystalSphereGuidance();
        var net=new NetSingleplayerGameService();
        using var sync=new EventSynchronizer(new RunLocationTargetedMessageBuffer(net),net,crystalRun,crystalRun,
            crystalRun.Players[0].NetId,crystalRun.Rng.Seed);
        try
        {
            workspace.SetLanguageOverride("en");TranslationServer.SetLocale("zh");
            panel.Initialize(outsideRun,runtime);host.AddChild(panel);panel.SetProcess(false);
            var status=(Label)Field(panel,"_status")!;
            var empty=(Label)Field(panel,"_emptyState")!;
            panel.ApplyLanguage("zh");
            check(status.Text==Text("zh","refresh_first") && empty.Text==status.Text,
                "Crystal existing no-snapshot status and empty surface refresh together");
            panel.Refresh();
            string captureKey=(string)Field(panel,"_emptyStatusKey")!;
            var captureFault=Field(panel,"_fault");
            check(captureFault is Exception && captureKey!="refresh_first","Crystal unavailable fixture exercises a capture failure");
            panel.ApplyLanguage("en");
            check(status.Text==Text("en",captureKey) && empty.Text==status.Text &&
                ReferenceEquals(captureFault,Field(panel,"_fault")) && Field(panel,"_snapshot")==null,
                "Crystal capture failure retranslates without recapture or losing its cause");

            panel.ShowSnapshot(capture,startDiscovery:false);
            var option=new CrystalRewardOption(PredictorRewardKind.Card,solution.Takes[0].Card);
            var projection=new CrystalOptionProjection(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty.Add(option,solution),solution,13);
            panel.ApplyUpdate(new([],Projection:projection,Progress:new(7,2,1,3,.5)));
            // Retargeting normally starts a worker; install a saved selection to
            // isolate language refresh from all search and scene observation.
            var saved=panel.SuspendSession()!;
            panel.RestoreSession(saved with { Selected=[option],Pending=[CrystalRewardOption.AnyRelic] });
            foreach(string key in new[]{"discovery_failed","projection_failed"})
            {
                var fault=new InvalidOperationException("CrystalLocalizationRawFault:"+key);
                if(key=="discovery_failed") panel.ApplyUpdate(new([],CrystalSphereAssistantPanel.DiscoveryStop.Failed,Error:fault));
                else
                {
                    typeof(CrystalSphereAssistantPanel).GetField("_errorKey",flags)!.SetValue(panel,key);
                    typeof(CrystalSphereAssistantPanel).GetField("_fault",flags)!.SetValue(panel,fault);
                }
                var before=panel.SuspendSession()!;
                foreach(string language in new[]{"zh","en"})
                {
                    panel.ApplyLanguage(language);
                    var after=panel.SuspendSession()!;
                    check(status.Text==Text(language,key) && after.ErrorKey==key && ReferenceEquals(after.Fault,fault),
                        "Crystal existing "+key+" retranslates and retains the raw fault");
                    check(ReferenceEquals(before.Reachability,after.Reachability) && ReferenceEquals(before.Snapshot,after.Snapshot) &&
                        before.Selected.SequenceEqual(after.Selected) && before.Pending.SequenceEqual(after.Pending) &&
                        ReferenceEquals(before.Projection,after.Projection) && before.Progress==after.Progress &&
                        ReferenceEquals(before.Worker,after.Worker) && ReferenceEquals(before.Updates,after.Updates),
                        "Crystal language refresh preserves targets, snapshot, solver, progress and worker queue");
                }
                panel.RestoreSession(panel.SuspendSession()!);
                check(status.Text==Text("en",key),"Crystal reopened failure uses the current provider");
            }

            // Supply the real native event to Initialize without starting a
            // native room, payment or reveal. The existing verified witness is
            // replayed only once during initialization, just as in production.
            LocalContext.NetId=crystalRun.Players[0].NetId;
            ((List<EventModel>)typeof(EventSynchronizer).GetField("_events",flags)!.GetValue(sync)!).Add(liveEvent);
            syncProperty.SetValue(RunManager.Instance,sync);
            crystalRun.PushRoom(new EventRoom(ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>()));
            string Signature()=>JsonSerializer.Serialize(new { rng=crystalRun.Rng.ToSerializable(),
                players=crystalRun.Players.Select(p=>new { rng=p.PlayerRng.ToSerializable(),cards=p.Deck.Cards.Select(c=>c.Id.ToString()) }),
                eventRng=liveEvent.Rng.ToSerializable() });
            string signature=Signature();
            guide.Initialize(crystalRun,runtime,capture,capture.Branches[0].Mode,solution);
            host.AddChild(guide);guide.SetProcess(false);
            var label=(Label)Field(guide,"_label")!;
            var prefixes=Field(guide,"_prefixes");
            check(label.Text==string.Format(Text("en","guide_wait"),Text("en",capture.Branches[0].Mode)),
                "Crystal guide uses the live RT2 override when the game locale differs");
            // A stopped guide returns before any board observation, and a
            // covered guide returns before polling. Both must still retranslate.
            Call(guide,"Stop");guide.Hide();
            foreach(string language in new[]{"zh","en"})
            {
                workspace.SetLanguageOverride(language);TranslationServer.SetLocale(language=="en"?"zh":"en");
                guide._Process(0);
                check(label.Text==Text(language,"guide_stale") && (bool)Field(guide,"_stopped")! &&
                    ReferenceEquals(prefixes,Field(guide,"_prefixes")) && ReferenceEquals(solution,Field(guide,"_solution")),
                    "Crystal hidden/stopped guide refreshes using effective UI language and preserves its witness");
                guide.Show();guide._Process(0);
                check(label.Text==Text(language,"guide_stale") && Signature()==signature,
                    "Crystal reopened guide retains language and does not mutate native player/event RNG or deck");
                guide.Hide();
            }
            // Exercise a dynamic tool argument too; stored arguments must be
            // formatted against the newly selected provider.
            typeof(CrystalSphereGuidance).GetField("_started",flags)!.SetValue(guide,true);
            typeof(CrystalSphereGuidance).GetField("_stopped",flags)!.SetValue(guide,false);
            typeof(CrystalSphereGuidance).GetField("_progress",flags)!.SetValue(guide,1);
            typeof(CrystalSphereGuidance).GetField("_poll",flags)!.SetValue(guide,10d);
            Func<object[]> arguments=()=>[1,solution.Steps.Length,
                ((IUiTextProvider)Field(guide,"_text")!).Get("predictor.crystal."+solution.Steps[0].Tool)];
            Call(guide,"ShowMessage","guide_step",arguments);
            foreach(string language in new[]{"zh","en"})
            {
                workspace.SetLanguageOverride(language);guide.Hide();guide._Process(0);guide.Show();
                check(label.Text==string.Format(Text(language,"guide_step"),1,solution.Steps.Length,Text(language,solution.Steps[0].Tool.ToString())) &&
                    (int)Field(guide,"_progress")! == 1 && (bool)Field(guide,"_started")! && !(bool)Field(guide,"_stopped")! &&
                    ReferenceEquals(prefixes,Field(guide,"_prefixes")) && Signature()==signature,
                    "Crystal started guide preserves progress while hidden/reopened and retranslates its tool argument");
            }
            GD.Print("CRYSTAL_LOCALIZATION_PASS empty=true;captureFailure=true;discoveryAndProjectionError=true;stateRetained=true;hiddenReopenedGuidance=true;effectiveLanguage=true;nativeStateUnchanged=true");
        }
        finally
        {
            syncProperty.SetValue(RunManager.Instance,previousSync);LocalContext.NetId=previousLocal;
            workspace.SetLanguageOverride(previousLanguage);TranslationServer.SetLocale(previousLocale);
            if(panel.GetParent()!=null) panel.GetParent().RemoveChild(panel);panel.Free();
            if(guide.GetParent()!=null) guide.GetParent().RemoveChild(guide);guide.Free();
        }
    }
}
