using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using RolltheSpire2.Core.PredictorRuntime;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckFocusedQueries(PredictorCrystalSnapshot board,CrystalRewardOption target,HashSet<string> rawCandidates)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var reach=new PredictorCrystalReachability(board,true,workers:4,includeRerolls:true);
        var first=reach.AdvanceFocused([],target,TimeSpan.Zero);
        Check(first.Verdict==CrystalFocusVerdict.Checking,"Zero traversal budget cannot reject an unresolved reachable focus");
        object cursor=typeof(PredictorCrystalReachability).GetField("_focus",flags)!.GetValue(reach)!;
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool canceled=false;
            try { reach.AdvanceFocused([],target,TimeSpan.FromMilliseconds(1),cancel.Token); }
            catch(OperationCanceledException) { canceled=true; }
            Check(canceled && ReferenceEquals(cursor,typeof(PredictorCrystalReachability).GetField("_focus",flags)!.GetValue(reach)),
                "Cancel leaves the focused frontier intact and publishes no negative");
        }
        CrystalFocusUpdate Close(ImmutableArray<CrystalRewardOption> selected,CrystalRewardOption goal)
        {
            var watch=Stopwatch.StartNew();CrystalFocusUpdate result;
            do { result=reach.AdvanceFocused(selected,goal,TimeSpan.FromMilliseconds(2)); }
            while(result.Verdict==CrystalFocusVerdict.Checking && watch.Elapsed<TimeSpan.FromSeconds(15));
            Check(result.Verdict!=CrystalFocusVerdict.Checking,"Controlled focused query reaches a verdict");
            return result;
        }
        var found=Close([],target);
        Check(found.Verdict==CrystalFocusVerdict.Reachable && ReferenceEquals(cursor,
            typeof(PredictorCrystalReachability).GetField("_focus",flags)!.GetValue(reach)),"Resumed focus reuses its four frontiers and finds reroll-only target");
        ReplayProof(board,found.Projection.Available[target]);
        var table=new CrystalReachabilityTable(board,includeRerolls:true);
        int yes=0,no=0;
        foreach(var option in table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card))
        {
            bool expected=rawCandidates.Contains($"{option.Key.Entry}:{option.UpgradeLevel}");
            Check((Close([],option).Verdict==CrystalFocusVerdict.Reachable)==expected,"Focused answer matches raw click × suffix oracle");
            // Independently force exhaustive-only lanes with no offset proof, so
            // the directed recipe/root bounds cannot conceal a missing partition.
            var lanes=Enumerable.Range(0,4).Select(i=>new PredictorCrystalExplorer(board,true,reachability:table,
                selected:[option],stopAfterPlan:true,exhaustiveOnly:true,rootPartition:i,rootPartitions:4)).ToArray();
            foreach(var lane in lanes)
            {
                var timer=Stopwatch.StartNew();
                while(!lane.Complete && timer.Elapsed<TimeSpan.FromSeconds(15)) lane.Advance(TimeSpan.FromMilliseconds(5));
                Check(lane.Complete,"Small exhaustive root partition closes");
            }
            Check(lanes.Any(l=>l.QueryProjection.SelectedPlan!=null)==expected,"Union of exhaustive root partitions matches raw oracle");
            if(expected) yes++;else no++;
        }
        var other=table.Candidates.First(o=>o!=target && rawCandidates.Contains($"{o.Key.Entry}:{o.UpgradeLevel}"));
        Check(Close([target],other).Verdict==CrystalFocusVerdict.Unreachable,"One card reward cannot supply two different cards");
        object failedCursor=typeof(PredictorCrystalReachability).GetField("_focus",flags)!.GetValue(reach)!;
        var filler=table.Candidates.First(o=>o!=target && o!=other);
        var stronger=reach.AdvanceFocused([target,filler],other,TimeSpan.Zero);
        Check(reach.IsCertifiedConflict([target,filler,other]) && !reach.IsCertifiedConflict([other]),
            "Lock-free UI proof view excludes stronger constraints and preserves weaker ones");
        Check(stronger.Verdict==CrystalFocusVerdict.Unreachable && reach.ConflictCount>0 &&
            ReferenceEquals(failedCursor,typeof(PredictorCrystalReachability).GetField("_focus",flags)!.GetValue(reach)),
            "Stronger selection reuses a certified conflict without creating or advancing a frontier");
        Check(Close([],other).Verdict==CrystalFocusVerdict.Reachable,"Focused negative does not leak into a weaker selected set");
        var ordinary=reach.Advance([target],TimeSpan.Zero);
        Check(ordinary.ExcludedCandidates.Contains(other),"Returning to broad discovery retains focused negative certificate");
        Console.WriteLine($"CRYSTAL_FOCUS_ORACLE_PASS reachable={yes};unreachable={no};workers=4;rawClickSuffixEquality=true;pauseResume=true;negativeScope=true");
    }

    private static void CheckProbeRetention(PredictorCrystalSnapshot source)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        foreach(bool individual in new[]{false,true})
        {
            var reach=new PredictorCrystalReachability(source,true,workers:4,targetDirected:true,includeRerolls:true);
            reach.Advance([],TimeSpan.Zero);
            var queries=(IDictionary)typeof(PredictorCrystalReachability).GetField("_queries",flags)!.GetValue(reach)!;
            object query=queries.Values.Cast<object>().Single();
            var probes=(IDictionary)query.GetType().GetField("Probes",flags)!.GetValue(query)!;
            var method=typeof(PredictorCrystalReachability).GetMethod(individual?"AdvanceCandidates":"AdvanceProbe",flags)!;
            int capacity=individual?8:4,rotations=0;
            for(int round=0;round<40;round++)
            {
                var before=probes.Keys.Cast<object>().ToDictionary(key=>key,key=>probes[key]!);
                foreach(var probe in before.Values) probe.GetType().GetField("TurnTime",flags)!.SetValue(probe,TimeSpan.FromSeconds(2));
                method.Invoke(reach,[query,ImmutableArray<CrystalRewardOption>.Empty,TimeSpan.Zero,CancellationToken.None]);
                foreach(var (key,probe) in before)
                {
                    var explorer=(PredictorCrystalExplorer)probe.GetType().GetField("Explorer",flags)!.GetValue(probe)!;
                    if(!explorer.Complete) Check(ReferenceEquals(probes[key],probe),"Timed rotation must retain every unfinished resident cursor");
                }
                Check(probes.Count<=capacity,"Retained candidate frontiers remain memory bounded");
                if(probes.Count==capacity) rotations++;
            }
            Check(rotations>4,"Controlled scheduler exercises repeated turns at full resident capacity");
            Console.WriteLine($"CRYSTAL_PROBE_RETENTION_PASS mode={(individual?"selected-plus-candidate":"initial")};capacity={capacity};fullRounds={rotations};unfinishedEvictions=0");
        }
    }
}
