using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static int CheckSavedRelicPrefixes(string savePath)
    {
        var save=JsonSerializer.Deserialize(File.ReadAllText(savePath),MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.SerializableRun>())!;
        var native=RunState.FromSerializable(save);var manager=RunManager.Instance;
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var stateProperty=manager.GetType().GetProperty("State",flags)!;
        var ascensionProperty=manager.GetType().GetProperty("AscensionManager",flags)!;
        var previous=stateProperty.GetValue(manager);var previousAscension=ascensionProperty.GetValue(manager);
        stateProperty.SetValue(manager,native);ascensionProperty.SetValue(manager,new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(native.AscensionLevel));
        try
        {
            var player=native.Players[0];var ev=(CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
            ev.BeginEvent(player,null,false).GetAwaiter().GetResult();
            var source=CrystalSphereLiveCapture.CaptureScene(native,player,ev,null,"0.111.0").Branches.Single(b=>b.Mode=="Six").Snapshot;
            string output=Path.Combine(Path.GetDirectoryName(savePath)!,Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1"?"reverse":"normal");
            Directory.CreateDirectory(output);ProductionSearchReplay.Write(Path.Combine(output,"snapshot.json"),source);
            Check(source.Context.Seed=="R93J1VRL814T","Owner relic case seed");
            CrystalRewardOption Relic(string id)=>new(PredictorRewardKind.Relic,new("RELIC",id));
            CrystalRewardOption Card(string id,int level=0)=>new(PredictorRewardKind.Card,new("CARD",id),level);
            var relic=Relic("CHANDELIER");var stoke=Card("STOKE");
            var cases=new (string Name,ImmutableArray<CrystalRewardOption> Selected,CrystalRewardOption Target,bool Rerolls)[] {
                ("owner-negative",[relic,stoke],Card("FEEL_NO_PAIN"),true),
                ("owner-positive-no-reroll",[relic],Card("BREAKTHROUGH",1),false),
                ("card-control",[stoke],Card("FEEL_NO_PAIN"),true)
            };
            var receipts=new List<object>();
            foreach(var test in cases)
            {
                CrystalFocusVerdict? prior=null;
                foreach(bool enabled in Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1"?new[]{true,false}:new[]{false,true})
                {
                    var session=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,
                        includeRerolls:test.Rerolls,relicOffsetBound:enabled);
                    var watch=Stopwatch.StartNew();CrystalFocusUpdate result;
                    do {result=session.AdvanceFocused(test.Selected,test.Target,TimeSpan.FromMilliseconds(100));}
                    while(result.Verdict==CrystalFocusVerdict.Checking && watch.Elapsed<TimeSpan.FromSeconds(90));
                    double ms=watch.Elapsed.TotalMilliseconds;
                    Check(result.Verdict!=CrystalFocusVerdict.Checking,"Relic prefix comparison completes: "+test.Name);
                    if(prior!=null) Check(prior==result.Verdict,"Relic prefix enabled/disabled verdict: "+test.Name);
                    prior=result.Verdict;
                    if(test.Name=="owner-negative") Check(result.Verdict==CrystalFocusVerdict.Unreachable,"Captured Owner negative reproduced");
                    if(test.Name=="owner-positive-no-reroll") Check(result.Verdict==CrystalFocusVerdict.Reachable,"Captured Owner positive reproduced");
                    if(result.Projection.Available.TryGetValue(test.Target,out var plan))
                    {
                        var replay=PredictorCrystalExplorer.Replay(source,plan.Steps);
                        Check(PredictorCrystalExplorer.VerifySelection(source,new(plan.Steps,replay.Rewards),test.Selected.Add(test.Target))!=null,"Returned legal plan replays");
                    }
                    var focus=typeof(PredictorCrystalReachability).GetField("_focus",flags)!.GetValue(session);
                    var lanes=focus==null?[]:(PredictorCrystalExplorer[])focus.GetType().GetField("Lanes",flags)!.GetValue(focus)!;
                    long checks=lanes.Sum(l=>l.RelicOffsetChecks),rejected=lanes.Sum(l=>l.RelicOffsetRejected),hits=lanes.Sum(l=>l.RelicOffsetCacheHits);
                    receipts.Add(new {test.Name,test.Selected,test.Target,test.Rerolls,enabled,ms,checks,rejected,hits,result.Verdict,result.Progress,result.RootProof,
                        plans=result.Projection.Available.Select(p=>new {p.Key,p.Value}).ToArray()});
                    Console.WriteLine($"CRYSTAL_RELIC_PREFIX name={test.Name};enabled={enabled};verdict={result.Verdict};ms={ms:F3};examined={result.Progress.Examined};checks={checks};rejected={rejected};hits={hits}");
                }
            }
            ProductionSearchReplay.Write(Path.Combine(output,"relic-prefix.json"),receipts);
            Console.WriteLine("CRYSTAL_RELIC_PREFIX_PASS saved-native-capture;paired-focus;typed-positive-replay;not-full-native-oracle");
            return 0;
        }
        finally {stateProperty.SetValue(manager,previous);ascensionProperty.SetValue(manager,previousAscension);}
    }
}
