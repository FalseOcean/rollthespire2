using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static int CheckSeedSurvey(string output,int ascension,Func<string,PredictorCrystalSnapshot> create,
        Action<PredictorCrystalSnapshot,PredictorCrystalSolution> native)
    {
        const int seedRandom=20261003,choiceRandom=610032026,stageMs=2500,sliceMs=150;
        const string alphabet="0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var generator=new Random(seedRandom);
        var seeds=Enumerable.Range(0,8).Select(_=>new string(Enumerable.Range(0,12).Select(_=>alphabet[generator.Next(alphabet.Length)]).ToArray())).ToArray();
        ProductionSearchReplay.Write(Path.Combine(output,"survey-receipt.json"),new {schema=1,sourceHead="71882a0e8fe95174614831becc85a593ebf69a29",
            seedRandom,choiceRandom,seeds,ascension,character="Ironclad",act=2,reveals=6,avoidCurse=true,driftwood=true,workers=4,stageMs,sliceMs,
            cache="shared discovery-positive routes; independent type sessions with cold tables; second selection retains that type session",
            selection="stable sorted witnessed options; independent Random(choiceRandom+seedIndex*97+kindIndex); second option any concrete witnessed kind",
            focus="one gray option after final available type selection; independent Random(choiceRandom+seedIndex*97+31)",
            scope="bounded host observation, no UI end-to-end, no universal seed coverage; cutoff remains Pending"});
        using var round=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        bool failed=false;
        for(int index=0;index<seeds.Length;index++)
        {
            string seed=seeds[index];var stages=new List<object>();var seedWatch=Stopwatch.StartNew();
            try
            {
                var source=create(seed);double setupMs=seedWatch.Elapsed.TotalMilliseconds;
                ProductionSearchReplay.Write(Path.Combine(output,"source-"+seed+".json"),source);
                string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
                var discovery=NewSession(source);var baseline=Stage(discovery,[],"Discovery",null);
                var routes=baseline.Projection.Available.Values.DistinctBy(p=>string.Join(';',p.Steps)).Select(p=>
                    new CrystalRewardRoute(p.Steps,PredictorCrystalExplorer.Replay(source,p.Steps).Rewards)).ToArray();
                ProductionSearchReplay.Write(Path.Combine(output,"discovery-proofs-"+seed+".json"),baseline.Projection.Available.Select(p=>new {option=p.Key,plan=p.Value}).ToArray());
                PredictorCrystalReachability? focusSession=null;ImmutableArray<CrystalRewardOption> focusSelected=[];ImmutableArray<CrystalRewardOption> focusExcluded=[];
                var kinds=new[]{PredictorRewardKind.Card,PredictorRewardKind.Potion,PredictorRewardKind.Relic};
                for(int kindIndex=0;kindIndex<kinds.Length;kindIndex++)
                {
                    var kind=kinds[kindIndex];var random=new Random(choiceRandom+index*97+kindIndex);
                    var choices=Sorted(baseline.Projection.Available.Keys.Where(o=>o.Kind==kind && o.Key.IsValid));
                    if(choices.Length==0) { foreach(string suffix in new[]{"First","Second"}) stages.Add(new {stage=kind+suffix,status="UnavailableWithinDiscovery"});continue; }
                    var first=choices[random.Next(choices.Length)];var checkWatch=Stopwatch.StartNew();native(source,baseline.Projection.Available[first]);
                    double nativeMs=checkWatch.Elapsed.TotalMilliseconds;var reach=NewSession(source);var cacheWatch=Stopwatch.StartNew();reach.AddWitnesses(routes);
                    double cacheSeedMs=cacheWatch.Elapsed.TotalMilliseconds;ImmutableArray<CrystalRewardOption> selected=[first];
                    var firstStage=Stage(reach,selected,kind+"First",new {nativeMs,cacheSeedMs,sharedRoutes=routes.Length});
                    focusSession=reach;focusSelected=selected;focusExcluded=firstStage.Excluded;
                    choices=Sorted(firstStage.Projection.Available.Keys.Where(o=>o.Key.IsValid && !selected.Contains(o)));
                    if(choices.Length==0) { stages.Add(new {stage=kind+"Second",status="UnavailableWithinSelection",selected});continue; }
                    var second=choices[random.Next(choices.Length)];checkWatch.Restart();native(source,firstStage.Projection.Available[second]);
                    selected=selected.Add(second);var secondStage=Stage(reach,selected,kind+"Second",new {nativeMs=checkWatch.Elapsed.TotalMilliseconds});
                    focusSelected=selected;focusExcluded=secondStage.Excluded;
                }
                if(focusSession!=null)
                {
                    var gray=Sorted(Table(focusSession).Where(o=>o.Key.IsValid && !focusSelected.Contains(o) && !focusSession.PeekKnown(focusSelected).Available.ContainsKey(o) && !focusExcluded.Contains(o)));
                    if(gray.Length==0) stages.Add(new {stage="GrayFocus",status="UnavailableWithinSelection",selected=focusSelected});
                    else Focus(focusSession,focusSelected,gray[new Random(choiceRandom+index*97+31).Next(gray.Length)]);
                }
                else stages.Add(new {stage="GrayFocus",status="UnavailableWithinDiscovery"});
                Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Seed survey preserves frozen source");
                ProductionSearchReplay.Write(Path.Combine(output,"survey-"+seed+".json"),new {seed,index,setupMs,seedMs=seedWatch.Elapsed.TotalMilliseconds,stages});
                Console.WriteLine($"CRYSTAL_SEED_SURVEY seed={seed};stages={stages.Count};ms={seedWatch.Elapsed.TotalMilliseconds:F3};frozen=true");
            }
            catch(Exception ex)
            {
                failed=true;ProductionSearchReplay.Write(Path.Combine(output,"survey-"+seed+".json"),new {seed,index,stages,error=ex.ToString(),seedMs=seedWatch.Elapsed.TotalMilliseconds});
                Console.WriteLine($"CRYSTAL_SEED_SURVEY_ERROR seed={seed};error={ex}");
            }

            (CrystalOptionProjection Projection,ImmutableArray<CrystalRewardOption> Excluded) Stage(PredictorCrystalReachability reach,ImmutableArray<CrystalRewardOption> selected,string label,object? preparation)
            {
                var watch=Stopwatch.StartNew();using var cancel=CancellationTokenSource.CreateLinkedTokenSource(round.Token);cancel.CancelAfter(stageMs);
                CrystalReachabilityUpdate? update=null;int iterations=0;double? firstBatchMs=null,firstPlanMs=null,firstAdvanceMs=null;bool cutoff=false;
                var excluded=new HashSet<CrystalRewardOption>();var before=Metrics(reach);
                void Observe(CrystalOptionProjection p)
                { if(!p.Available.IsEmpty) firstBatchMs??=watch.Elapsed.TotalMilliseconds;if(p.SelectedPlan!=null) firstPlanMs??=watch.Elapsed.TotalMilliseconds; }
                try
                {
                    Observe(reach.ProjectKnown(selected,cancel.Token,Observe));
                    while(watch.ElapsedMilliseconds<stageMs)
                    {
                        iterations++;var call=Stopwatch.StartNew();
                        try { update=reach.Advance(selected,TimeSpan.FromMilliseconds(sliceMs),cancel.Token,rows=>excluded.UnionWith(rows)); }
                        finally { firstAdvanceMs??=call.Elapsed.TotalMilliseconds; }
                        excluded.UnionWith(update.ExcludedCandidates);Observe(update.Projection);
                        if(update.Complete) break;
                    }
                }
                catch(OperationCanceledException) { cutoff=true; }
                var projection=reach.PeekKnown(selected);Observe(projection);double wallMs=watch.Elapsed.TotalMilliseconds;
                var counts=Counts(reach,selected,projection,excluded);var available=Sorted(projection.Available.Keys);
                stages.Add(new {stage=label,selected,status=update?.Complete==true?"Complete":"Pending",cutoff,iterations,wallMs,overrunMs=Math.Max(0,wallMs-stageMs),
                    firstAdvanceMs,firstBatchMs,firstPlanMs,preparation,counts,available,excluded=Sorted(excluded),plan=projection.SelectedPlan,
                    progress=update?.Progress,phase=update?.Phase.ToString(),boundBefore=before,boundAfter=Metrics(reach),
                    boundChecks=update?.BoundChecks,boundRejected=update?.BoundRejected,boundSeconds=update?.BoundSeconds,boundCacheHits=update?.BoundCacheHits,
                    rootExcluded=update?.BoundRootExcluded,probeSlices=update?.ProbeSlices,probePlans=update?.ProbePlans,directedNodes=update?.DirectedNodes,directedWitnesses=update?.DirectedWitnesses,
                    metricsScope="last completed Advance plus current shared-bound counters; interrupted Advance progress unavailable"});
                Console.WriteLine($"CRYSTAL_SURVEY_STAGE seed={seed};stage={label};selected={string.Join('|',selected)};ms={wallMs:F3};complete={update?.Complete==true};iterations={iterations};counts={JsonSerializer.Serialize(counts)}");
                return(projection,excluded.ToImmutableArray());
            }
            void Focus(PredictorCrystalReachability reach,ImmutableArray<CrystalRewardOption> selected,CrystalRewardOption target)
            {
                var watch=Stopwatch.StartNew();using var cancel=CancellationTokenSource.CreateLinkedTokenSource(round.Token);cancel.CancelAfter(stageMs);
                CrystalFocusUpdate? update=null;int iterations=0;bool cutoff=false;var before=Metrics(reach);
                try { do { iterations++;update=reach.AdvanceFocused(selected,target,TimeSpan.FromMilliseconds(sliceMs),cancel.Token); }
                    while(update.Verdict==CrystalFocusVerdict.Checking && watch.ElapsedMilliseconds<stageMs); }
                catch(OperationCanceledException) { cutoff=true; }
                double wallMs=watch.Elapsed.TotalMilliseconds;
                stages.Add(new {stage="GrayFocus",selected,target,status=update?.Verdict==CrystalFocusVerdict.Reachable?"Reachable":update?.Verdict==CrystalFocusVerdict.Unreachable?"Excluded":"Pending",
                    cutoff,iterations,wallMs,overrunMs=Math.Max(0,wallMs-stageMs),progress=update?.Progress,rootProof=update?.RootProof,proofSource=update?.ProofSource,boundBefore=before,boundAfter=Metrics(reach)});
                Console.WriteLine($"CRYSTAL_SURVEY_FOCUS seed={seed};ms={wallMs:F3};verdict={update?.Verdict};iterations={iterations}");
            }
        }
        Console.WriteLine(failed?"CRYSTAL_SEED_SURVEY_FAIL":"CRYSTAL_SEED_SURVEY_PASS allSeedsRetained=true;cutoffUnknown=true;noDeployment=true");return failed?1:0;
    }
    private static PredictorCrystalReachability NewSession(PredictorCrystalSnapshot source)=>new(source,true,potionScenarioBound:true,workers:4,targetDirected:true,includeRerolls:true,goalLanguageProof:true);
    private static CrystalRewardOption[] Sorted(IEnumerable<CrystalRewardOption> rows)=>rows.OrderBy(o=>o.Kind).ThenBy(o=>o.Key.Serialized,StringComparer.Ordinal).ThenBy(o=>o.UpgradeLevel).ToArray();
    private static ImmutableArray<CrystalRewardOption> Table(PredictorCrystalReachability reach)=>(typeof(PredictorCrystalReachability).GetField("_table",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(reach) as CrystalReachabilityTable)?.Candidates??[];
    private static object Counts(PredictorCrystalReachability reach,ImmutableArray<CrystalRewardOption> selected,CrystalOptionProjection p,IEnumerable<CrystalRewardOption> excluded)
    {
        var universe=Table(reach).Where(o=>!selected.Contains(o)).ToArray();var negatives=excluded.ToHashSet();
        return new {tableReady=typeof(PredictorCrystalReachability).GetField("_table",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(reach)!=null,
            universe=universe.Length,reachable=universe.Count(p.Available.ContainsKey),excluded=universe.Count(negatives.Contains),
            pending=universe.Count(o=>!p.Available.ContainsKey(o) && !negatives.Contains(o))};
    }
    private static object? Metrics(PredictorCrystalReachability reach)
    {
        var bound=typeof(PredictorCrystalReachability).GetField("_offsetBound",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(reach) as PredictorCrystalOffsetBound;
        return bound==null?null:new {bound.Checks,bound.Rejected,bound.Incomplete,bound.CacheHits,bound.Seconds,bound.CoverSearchNodes,bound.PackingChecks,bound.PackingRejected};
    }
}
