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
        var partialDiscovery=reach.Advance([],TimeSpan.Zero);
        int unresolved=table.Candidates.Count(candidate=>!partialDiscovery.Projection.Available.ContainsKey(candidate) &&
            !partialDiscovery.ExcludedCandidates.Contains(candidate));
        Check(unresolved>0 && !partialDiscovery.Complete,
            "A witnessed empty-target discovery remains open while any frozen candidate lacks a proof");
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
        // Every positive/negative above was compared with independent raw
        // legal clicks and reward actions. No broad traversal is needed once
        // those exact candidate questions are answered, even for empty goals.
        var provedDiscovery=reach.Advance([],TimeSpan.Zero);
        Check(yes>0 && no>0 && provedDiscovery.Projection.SelectedPlan!=null &&
            table.Candidates.All(candidate=>provedDiscovery.Projection.Available.ContainsKey(candidate) ||
                provedDiscovery.ExcludedCandidates.Contains(candidate)),
            "Raw-verified focus answers cover the complete empty-target domain with a legal witness");
        Check(provedDiscovery.Complete && provedDiscovery.Phase==CrystalDiscoveryPhase.Complete &&
            provedDiscovery.Progress.Examined==partialDiscovery.Progress.Examined,
            "All independently proved empty-target candidates close without further broad traversal");
        Check(!provedDiscovery.Projection.GoldComplete && reach.GoldProgress.Examined==0,
            "Candidate-domain completion does not certify or execute the separate gold optimum");
        var retainedDiscovery=reach.Advance([],TimeSpan.Zero);
        Check(retainedDiscovery.Complete && retainedDiscovery.ExcludedCandidates.ToHashSet().SetEquals(provedDiscovery.ExcludedCandidates),
            "Finished empty-target discovery retains its proofs on a repeated zero-work query");
        Console.WriteLine($"CRYSTAL_EMPTY_CANDIDATES_CLOSED_PASS domain={table.Candidates.Length};positive={yes};negative={no};rawOracle=true;extraBroadNodes=0;goldProof=false");
        foreach(int incompleteAttempts in new[]{0,1,2})
        {
            var tail=new PredictorCrystalReachability(board,true,workers:4,includeRerolls:true,goalLanguageProof:true);
            var seedPlan=provedDiscovery.Projection.SelectedPlan!;
            tail.AddWitnesses([new(seedPlan.Steps,PredictorCrystalExplorer.Replay(board,seedPlan.Steps).Rewards)]);
            var beforeTail=tail.Advance([],TimeSpan.Zero);
            var legacyQueries=(IDictionary)typeof(PredictorCrystalReachability).GetField("_queries",flags)!.GetValue(tail)!;
            object legacyCursor=legacyQueries.Values.Cast<object>().Single();
            var negatives=provedDiscovery.ExcludedCandidates;
            var pending=negatives[0];var inherited=negatives.RemoveAt(0);
            // Force the actual tail seam on this <=3-click raw oracle. Normal
            // production activation follows broad discovery on >3-click boards.
            typeof(PredictorCrystalReachability).GetField("_emptyLanguageExcluded",flags)!.SetValue(tail,inherited);
            typeof(PredictorCrystalReachability).GetField("_emptyLegacyProgress",flags)!.SetValue(tail,beforeTail.Progress);
            typeof(PredictorCrystalReachability).GetField("_emptyLanguageTail",flags)!.SetValue(tail,true);
            var zero=tail.Advance([],TimeSpan.Zero);
            Check(!zero.Complete && inherited.All(zero.ExcludedCandidates.Contains) && !zero.ExcludedCandidates.Contains(pending) &&
                zero.Progress.Seconds>=beforeTail.Progress.Seconds && zero.Progress.Examined>=beforeTail.Progress.Examined,
                "Zero-budget tail inherits exact scoped negatives and leaves the unresolved candidate pending");
            if(incompleteAttempts>0)
            {
                var limited=PredictorCrystalGoalLanguage.Solve(board,table,[pending],true,TimeSpan.Zero);
                Check(limited.Verdict==CrystalOffsetVerdict.Incomplete,"Zero-budget language attempt supplies no negative proof");
                var languageQueries=(IDictionary)typeof(PredictorCrystalReachability).GetField("_languageQueries",flags)!.GetValue(tail)!;
                object languageQuery=languageQueries.Values.Cast<object>().Single();
                var results=(System.Collections.Concurrent.ConcurrentQueue<(CrystalRewardOption? Target,CrystalGoalSearchResult Result,bool Refined)>)
                    languageQuery.GetType().GetField("Results",flags)!.GetValue(languageQuery)!;
                // Exercise both the finite refinement opportunity and its
                // exhausted fallback, without turning either cutoff into truth.
                for(int attempt=0;attempt<incompleteAttempts;attempt++)
                    results.Enqueue((pending,limited,attempt>0));
                var limitedTail=tail.Advance([],TimeSpan.Zero);
                Check(!limitedTail.Complete && !limitedTail.ExcludedCandidates.Contains(pending),
                    "A consumed Incomplete language receipt cannot certify candidate absence");
            }
            var tailWatch=Stopwatch.StartNew();CrystalReachabilityUpdate tailResult;
            do {tailResult=tail.Advance([],TimeSpan.FromMilliseconds(5));}
            while(!tailResult.Complete && tailWatch.Elapsed<TimeSpan.FromSeconds(10));
            Check(tailResult.Complete && ReferenceEquals(legacyCursor,legacyQueries.Values.Cast<object>().Single()),
                "Tail completion retains the original broad cursor, including after an incomplete attempt");
            foreach(var candidate in table.Candidates)
            {
                bool expected=rawCandidates.Contains($"{candidate.Key.Entry}:{candidate.UpgradeLevel}");
                Check(tailResult.Projection.Available.ContainsKey(candidate)==expected &&
                    tailResult.ExcludedCandidates.Contains(candidate)!=expected,
                    "Completed tail domain matches independent raw legal-click and reward-action truth");
            }
            Check(!tailResult.Projection.GoldComplete,"Tail candidate closure does not certify gold");
            Console.WriteLine($"CRYSTAL_EMPTY_LANGUAGE_TAIL_PASS incompleteAttempts={incompleteAttempts};rawOracle=true;legacyCursorRetained=true;milliseconds={tailWatch.Elapsed.TotalMilliseconds:F3}");
        }
        foreach(bool individual in new[]{false,true})
        {
            // Drive the existing probe schedulers on this complete raw domain.
            // The public scheduler reserves probes for >3 reveals; this tiny
            // oracle uses reflection only to supply its actual candidate order.
            var probes=new PredictorCrystalReachability(board,true,workers:4,targetDirected:true,includeRerolls:true);
            var probeUpdate=probes.Advance([],TimeSpan.Zero);
            var probeQueries=(IDictionary)typeof(PredictorCrystalReachability).GetField("_queries",flags)!.GetValue(probes)!;
            object probeQuery=probeQueries.Values.Cast<object>().Single();
            probeQuery.GetType().GetField("ProbeOrder",flags)!.SetValue(probeQuery,table.Candidates.ToArray());
            var advanceProbe=typeof(PredictorCrystalReachability).GetMethod(individual?"AdvanceCandidates":"AdvanceProbe",flags)!;
            var probeWatch=Stopwatch.StartNew();
            do
            {
                advanceProbe.Invoke(probes,[probeQuery,ImmutableArray<CrystalRewardOption>.Empty,TimeSpan.FromMilliseconds(2),CancellationToken.None]);
                probeUpdate=probes.Advance([],TimeSpan.Zero);
            } while(!probeUpdate.Complete && probeWatch.Elapsed<TimeSpan.FromSeconds(15));
            Check(probeUpdate.Complete && probeUpdate.Projection.SelectedPlan!=null,
                "Single-target probe scheduler closes the independently exhausted small domain");
            foreach(var candidate in table.Candidates)
            {
                bool expected=rawCandidates.Contains($"{candidate.Key.Entry}:{candidate.UpgradeLevel}");
                Check(probeUpdate.Projection.Available.ContainsKey(candidate)==expected &&
                    probeUpdate.ExcludedCandidates.Contains(candidate)!=expected,
                    "Probe positive/negative equals independent raw clicks and reward actions");
            }
            Check(!probeUpdate.Projection.GoldComplete,"Probe completion cannot certify the separate gold question");
            Console.WriteLine($"CRYSTAL_PROBE_ORACLE_PASS mode={(individual?"candidate":"initial")};domain={table.Candidates.Length};rawOracle=true;milliseconds={probeWatch.Elapsed.TotalMilliseconds:F3}");
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
