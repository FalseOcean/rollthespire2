using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private sealed record HoldoutQuestion(int Index,int RequestedSize,ImmutableArray<CrystalRewardOption> Selected,PredictorCrystalSolution? Witness,string Construction,double TypedMs,double NativeMs);
    private sealed record HoldoutState(bool DomainPrepared,ImmutableArray<CrystalRewardOption> Upper,ImmutableArray<CrystalRewardOption> Gray,
        ImmutableArray<CrystalRewardOption> Impossible,ImmutableArray<string> NegativeSources,CrystalOptionProjection Positive);
    // This entry is deliberately dormant until the final validation is authorized; no default random seed exists.
    private static int CheckHoldout(string output,int ascension,Func<string,PredictorCrystalSnapshot> create,Action<PredictorCrystalSnapshot,PredictorCrystalSolution> native)
    {
        const int discoveryMs=24000,pathLimit=256,guidedPathLimit=64,geometryStateLimit=20000,suffixSamples=8,queryMs=10000,sliceMs=150;
        bool smoke=Environment.GetEnvironmentVariable("RT2_CRYSTAL_HOLDOUT_SMOKE")=="1";
        int master=smoke?20261003:int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_HOLDOUT_RANDOM_SEED")??throw new InvalidOperationException("Final holdout random seed required"));
        string sourceHead=Environment.GetEnvironmentVariable("RT2_CRYSTAL_HOLDOUT_SOURCE_HEAD")??throw new InvalidOperationException("Final source HEAD required");
        const string alphabet="0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var excludedSeeds="KAJE4M0MECQ6 L11S87VL6E0Y REFEW5HV6YSF TZSUCA3EM6T6 P8WZ22Z5ALNP 6KR8UVZM8LZ9 E08VAKAKL8QM 8JS4YL57A2MS 000000000000 9TW7Y8N6663H AAJ82GDRLS09 CKF62MPWQBL7".Split(' ');
        var seeds=new List<string>();if(smoke) seeds.Add(excludedSeeds[0]);
        else {var seedRandom=new Random(master);while(seeds.Count<10) { string seed=new(Enumerable.Range(0,12).Select(_=>alphabet[seedRandom.Next(alphabet.Length)]).ToArray());if(!excludedSeeds.Contains(seed) && !seeds.Contains(seed)) seeds.Add(seed); }}
        ProductionSearchReplay.Write(Path.Combine(output,"holdout-receipt.json"),new {schema=1,smoke,sourceHead,master,seeds,excludedSeeds,ascension,character="Ironclad",act=2,reveals=6,driftwood=true,avoidCurse=true,
            runtimeEvidence=RolltheSpire2.Bootstrap.AssemblyEvidence.Capture(),
            discoveryMs,pathLimit,guidedPathLimit,geometryStateLimit,suffixSamples,queryMs,sliceMs,workers=4,cache="one session per seed; discovery positives only; cold first table; subsequent selections retain naturally learned positives/negatives",
            pathSchedule="64 bounded geometry paths requiring relic+random card item+random potion item, then 192 uniform raw paths; 24s overall cap; construction cutoffs are not negative proofs",
            random="System.Random(master) seed strings; independent per-seed path/choice streams via master XOR fixed salts XOR index*97",
            targetSchedule="three stratified Card/Potion/Relic-first 1->2->3 chains plus one unique single retarget; missing strata/construction retained"});
        var inputs=new List<(string Seed,PredictorCrystalSnapshot Source,CrystalRewardRoute[] Routes,HoldoutQuestion[] Questions)>();
        for(int seedIndex=0;seedIndex<seeds.Count;seedIndex++)
        {
            string seed=seeds[seedIndex];var setup=Stopwatch.StartNew();var source=create(seed);double captureMs=setup.Elapsed.TotalMilliseconds;
            ProductionSearchReplay.Write(Path.Combine(output,"holdout-source-"+seed+".json"),source);
            var pathRandom=new Random(master^0x31574A21^(seedIndex*97));var choiceRandom=new Random(master^0x61285013^(seedIndex*97));
            var discovery=Stopwatch.StartNew();var pool=new List<(CrystalRewardRoute Row,ImmutableArray<CrystalRewardOption> Goals)>();var poolKeys=new HashSet<string>();var routes=new List<CrystalRewardRoute>();var geometryGaps=new List<object>();int paths=0;double typedTotalMs=0,nativeTotalMs=0;
            while(paths<pathLimit && discovery.ElapsedMilliseconds<discoveryMs)
            {
                paths++;ImmutableArray<PredictorCrystalStep> steps=[];var run=PredictorCrystalExplorer.Replay(source,steps);
                if(paths<=guidedPathLimit)
                {
                    var cards=source.Items.Where(i=>i.X>=0 && i.Kind.StartsWith("CARD_",StringComparison.Ordinal)).ToArray();var potions=source.Items.Where(i=>i.X>=0 && i.Kind.StartsWith("POTION_",StringComparison.Ordinal)).ToArray();
                    if(cards.Length==0 || potions.Length==0 || !source.Items.Any(i=>i.X>=0 && i.Kind=="RELIC")) {geometryGaps.Add(new {path=paths,reason="RequiredGroupUnplaced"});continue;}
                    var card=cards[pathRandom.Next(cards.Length)];var potion=potions[pathRandom.Next(potions.Length)];
                    try {steps=RequiredItemPath(source,i=>i.Kind=="RELIC" || i==card || i==potion,geometryStateLimit);run=PredictorCrystalExplorer.Replay(source,steps);}
                    catch(InvalidOperationException ex) when(ex.Message.StartsWith("Required-item construction",StringComparison.Ordinal) || ex.Message.StartsWith("No legal required-item path",StringComparison.Ordinal)) {geometryGaps.Add(new {path=paths,reason=ex.Message});continue;}
                }
                else for(int click=0;click<source.Remaining;click++)
                { var hidden=run.ExportCrystal().Hidden.Select((value,i)=>(value,i)).Where(p=>p.value).Select(p=>p.i).ToArray();if(hidden.Length==0) break;int cell=hidden[pathRandom.Next(hidden.Length)];
                    steps=steps.Add(new(cell/11,cell%11,pathRandom.Next(4)==0?PredictorCrystalTool.Small:PredictorCrystalTool.Big));run=PredictorCrystalExplorer.Replay(source,steps); }
                if(steps.Length!=source.Remaining || run.Phase!=PredictorPhase.Rewards || run.ExportCrystal().Revealed.Any(i=>source.Items[i].Kind=="CURSE" && !source.Revealed.Contains(i))) continue;
                var row=PredictorCrystalExplorer.WithRerolls(new(steps,run.Rewards),run);routes.Add(row);
                foreach(var variant in row.RerollVariants.OrderBy(_=>choiceRandom.Next()).Take(suffixSamples))
                {
                    if(discovery.ElapsedMilliseconds>=discoveryMs) break;
                    var options=variant.Rewards.Select(r=>{if(r.Kind==PredictorRewardKind.Card && !r.Cards.IsEmpty) {var card=r.Cards[choiceRandom.Next(r.Cards.Length)];return new CrystalRewardOption(r.Kind,card.Key,card.UpgradeLevel);}
                        return (r.Kind is PredictorRewardKind.Relic or PredictorRewardKind.Potion) && r.Key.IsValid?new CrystalRewardOption(r.Kind,r.Key):null;})
                        .OfType<CrystalRewardOption>().Distinct().OrderBy(_=>choiceRandom.Next()).Take(3).ToImmutableArray();
                    if(options.Length!=3 || poolKeys.Contains(HoldoutKey(options)) || Verify(row,options)==null) continue;
                    poolKeys.Add(HoldoutKey(options));pool.Add((row,options));
                }
            }
            double discoverWallMs=discovery.Elapsed.TotalMilliseconds;var questions=new List<HoldoutQuestion>();var used=new HashSet<string>();var gaps=new List<string>();
            foreach(var firstKind in new[]{PredictorRewardKind.Card,PredictorRewardKind.Potion,PredictorRewardKind.Relic})
            {
                var candidates=pool.Where(p=>p.Goals.Any(g=>g.Kind==firstKind)).OrderBy(_=>choiceRandom.Next()).ToArray();
                if(candidates.Length==0) { gaps.Add(firstKind+":NoTripletWithinDiscovery");candidates=pool.OrderBy(_=>choiceRandom.Next()).ToArray(); }
                else candidates=candidates.Concat(pool.Where(p=>!p.Goals.Any(g=>g.Kind==firstKind)).OrderBy(_=>choiceRandom.Next())).ToArray();
                ImmutableArray<CrystalRewardOption> ordered=[];CrystalRewardRoute? chosen=null;
                foreach(var candidate in candidates)
                {
                    var first=candidate.Goals.Where(g=>g.Kind==firstKind).OrderBy(_=>choiceRandom.Next()).FirstOrDefault()??candidate.Goals[choiceRandom.Next(3)];
                    var selection=ImmutableArray.Create(first).AddRange(candidate.Goals.Where(g=>g!=first).OrderBy(_=>choiceRandom.Next()));
                    if(Enumerable.Range(1,3).Any(n=>used.Contains(HoldoutKey(selection.Take(n))))) continue;
                    if(Verify(candidate.Row,selection)==null) continue;chosen=candidate.Row;ordered=selection;break;
                }
                if(chosen==null) gaps.Add(firstKind+":UniqueTripletChainUnavailable");
                else if(ordered[0].Kind!=firstKind) gaps.Add(firstKind+":StratumUnavailableAfterUniquePrefixes");
                for(int size=1;size<=3;size++) AddQuestion(size,chosen,ordered.Take(size).ToImmutableArray());
            }
            bool relicFirstCovered=questions.Where(q=>q.RequestedSize==1 && q.Selected.Length>0).Any(q=>q.Selected[0].Kind==PredictorRewardKind.Relic);
            var singles=routes.SelectMany(r=>PredictorCrystalExplorer.Options(r).Where(o=>o.Key.IsValid).Select(o=>(Row:r,Goal:o)))
                .Where(p=>!used.Contains(HoldoutKey([p.Goal]))).ToArray();
            var preferred=relicFirstCovered?singles:singles.Where(p=>p.Goal.Kind==PredictorRewardKind.Relic).ToArray();if(preferred.Length==0) preferred=singles;
            var single=preferred.OrderBy(_=>choiceRandom.Next()).FirstOrDefault(p=>Verify(p.Row,[p.Goal])!=null);
            AddQuestion(1,single.Row,single.Goal==null?[]:[single.Goal]);
            ProductionSearchReplay.Write(Path.Combine(output,"holdout-cases-"+seed+".json"),new {seed,captureMs,discoverWallMs,discoveryOverrunMs=Math.Max(0,discoverWallMs-discoveryMs),paths,guidedAttempted=Math.Min(paths,guidedPathLimit),rawAttempted=Math.Max(0,paths-guidedPathLimit),geometryGaps,routeCount=routes.Count,tripletCount=pool.Count,gaps,questions,typedTotalMs,nativeTotalMs,totalPreparationMs=setup.Elapsed.TotalMilliseconds});
            inputs.Add((seed,source,routes.ToArray(),questions.ToArray()));
            void AddQuestion(int size,CrystalRewardRoute? row,ImmutableArray<CrystalRewardOption> selected)
            {
                var check=Stopwatch.StartNew();var witness=row==null || selected.Length!=size?null:Verify(row,selected);double typedMs=check.Elapsed.TotalMilliseconds;
                double nativeMs=0;if(witness!=null) { used.Add(HoldoutKey(selected));check.Restart();native(source,witness);nativeMs=check.Elapsed.TotalMilliseconds;nativeTotalMs+=nativeMs; }
                questions.Add(new(questions.Count,size,selected,witness,witness==null?"CannotConstructWithinDiscovery":"VerifiedJointWitness",typedMs,nativeMs));
            }
            PredictorCrystalSolution? Verify(CrystalRewardRoute row,ImmutableArray<CrystalRewardOption> selected)
            {var check=Stopwatch.StartNew();try {return PredictorCrystalExplorer.VerifySelection(source,row,selected);}finally {typedTotalMs+=check.Elapsed.TotalMilliseconds;}}
        }
        // Every seed and question is frozen before any measured production query is issued.
        bool passed=inputs.All(p=>p.Questions.All(q=>q.Witness!=null));
        if(smoke) {ProductionSearchReplay.Write(Path.Combine(output,"holdout-format-smoke.json"),Export(HoldoutSnapshot(NewSession(inputs[0].Source),[],new())));
            Console.WriteLine(passed?"CRYSTAL_HOLDOUT_SMOKE_PASS developmentSeedOnly=true;noFinalSeedGeneration=true":"CRYSTAL_HOLDOUT_SMOKE_GAPS developmentSeedOnly=true;constructionPreserved=true");return passed?0:2;}
        foreach(var input in inputs)
        {
            var reach=NewSession(input.Source);string frozen=JsonSerializer.Serialize(input.Source,ProductionSearchReplay.JsonOptions);var results=new List<object>();bool cacheLoaded=false;
            foreach(var question in input.Questions)
            {
                if(question.Witness==null) { results.Add(new {question.Index,status="CannotConstructWithinDiscovery",question.RequestedSize});continue; }
                using var queryCancel=new CancellationTokenSource();var watch=Stopwatch.StartNew();string cacheLabel=cacheLoaded?"retarget: natural same-seed caches":"first valid query: cold table, discovery positives only";
                var before=Metrics(reach);var negatives=new ConcurrentDictionary<CrystalRewardOption,string>();CrystalReachabilityUpdate? update=null;int iterations=0;double? firstAdvanceMs=null,firstPlanMs=null;
                HoldoutState? deadline=null;double? deadlineObservedMs=null;string? deadlineError=null;
                using var timer=new Timer(_=>{try {deadline=HoldoutSnapshot(reach,question.Selected,negatives);deadlineObservedMs=watch.Elapsed.TotalMilliseconds;}catch(Exception ex) {deadlineError=ex.ToString();}},null,queryMs,Timeout.Infinite);
                if(!cacheLoaded) { reach.AddWitnesses(input.Routes);cacheLoaded=true; }
                void Observe(CrystalOptionProjection p) { if(p.SelectedPlan!=null) firstPlanMs??=watch.Elapsed.TotalMilliseconds; }
                Observe(reach.ProjectKnown(question.Selected,queryCancel.Token,publish:Observe));var state=HoldoutSnapshot(reach,question.Selected,negatives);
                while(watch.ElapsedMilliseconds<queryMs && (!state.DomainPrepared || state.Gray.Length>0 || state.Positive.SelectedPlan==null))
                {
                    iterations++;var call=Stopwatch.StartNew();update=reach.Advance(question.Selected,TimeSpan.FromMilliseconds(Math.Min(sliceMs,Math.Max(1,queryMs-watch.ElapsedMilliseconds))),
                        token:queryCancel.Token,publishExcluded:rows=>{foreach(var option in rows) negatives.TryAdd(option,"published-candidate-proof");});firstAdvanceMs??=call.Elapsed.TotalMilliseconds;
                    foreach(var option in update.ExcludedCandidates) negatives.TryAdd(option,"candidate-proof");Observe(update.Projection);state=HoldoutSnapshot(reach,question.Selected,negatives);
                }
                queryCancel.Cancel();double wallMs=watch.Elapsed.TotalMilliseconds;bool closed=state.DomainPrepared && state.Gray.IsEmpty && state.Positive.SelectedPlan!=null;bool onTime=closed && wallMs<=queryMs;passed&=onTime;
                Console.WriteLine($"CRYSTAL_HOLDOUT_QUERY seed={input.Seed};index={question.Index};goals={question.Selected.Length};ms={wallMs:F2};gray={state.Gray.Length};closed={closed};onTime={onTime}");
                timer.Change(Timeout.Infinite,Timeout.Infinite);timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
                results.Add(new {question.Index,chain=question.Index/3,question.RequestedSize,question.Selected,cacheLabel,status=onTime?"Pass":"Pending",wallMs,overrunMs=Math.Max(0,wallMs-queryMs),closed,onTime,lateClosure=closed && !onTime,
                    firstAdvanceMs,firstPlanMs,iterations,deadlineObservedMs,deadlineError,atDeadline=deadline==null?null:Export(deadline),final=Export(state),sessionComplete=update?.Complete,progress=update?.Progress,boundBefore=before,boundAfter=Metrics(reach),probeSlices=update?.ProbeSlices,probePlans=update?.ProbePlans,
                    deadlineScope="read-only timer observation at actual recorded timestamp; null means no observation, never zero gray",progressScope="last completed Advance; null stays unknown"});
                foreach(var proof in state.Positive.Available.OrderBy(p=>p.Key.Key.Serialized).Take(1)) ReplayProof(input.Source,proof.Value);
                ProductionSearchReplay.Write(Path.Combine(output,"holdout-results-"+input.Seed+".json"),new {input.Seed,results});
            }
            ProductionSearchReplay.Write(Path.Combine(output,"holdout-results-"+input.Seed+".json"),new {input.Seed,results});
            Check(frozen==JsonSerializer.Serialize(input.Source,ProductionSearchReplay.JsonOptions),"Holdout preserves source");
        }
        Console.WriteLine(passed?"CRYSTAL_HOLDOUT_PASS 100Queries=true;actualWallWithin10s=true":"CRYSTAL_HOLDOUT_PENDING allPlannedQueriesRetained=true;noTimeoutNegatives=true");return passed?0:2;
        object Export(HoldoutState s)=>new {s.DomainPrepared,tableUpper=s.Upper,pending=s.DomainPrepared?(int?)s.Gray.Length:null,gray=s.Gray,impossible=s.Impossible.Select((option,i)=>new {option,source=s.NegativeSources[i]}),
            possible=s.Positive.Available.Select(p=>new {option=p.Key,witness=p.Value}),selectedWitness=s.Positive.SelectedPlan};
    }
    private static string HoldoutKey(IEnumerable<CrystalRewardOption> selected)=>string.Join('|',Sorted(selected).Select(o=>$"{o.Kind}:{o.Key.Serialized}:{o.UpgradeLevel}"));
    private static HoldoutState HoldoutSnapshot(PredictorCrystalReachability reach,ImmutableArray<CrystalRewardOption> selected,ConcurrentDictionary<CrystalRewardOption,string> published)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;var table=typeof(PredictorCrystalReachability).GetField("_table",flags)!.GetValue(reach) as CrystalReachabilityTable;
        var conflicts=(Array)typeof(PredictorCrystalReachability).GetField("_readableConflicts",flags)!.GetValue(reach)!;var projection=reach.PeekKnown(selected);var negatives=new Dictionary<CrystalRewardOption,string>();
        var upper=table?.Candidates??[];
        foreach(var option in upper.Where(o=>!selected.Contains(o)))
        { var goals=selected.Add(option);foreach(var conflict in conflicts) { var needs=(ImmutableArray<CrystalRewardOption>)conflict!.GetType().GetProperty("Goals",flags)!.GetValue(conflict)!;
                if(needs.All(goals.Contains)) { negatives[option]=(string)conflict.GetType().GetProperty("Source",flags)!.GetValue(conflict)!;break; } }
            if(!negatives.ContainsKey(option) && published.TryGetValue(option,out string? source)) negatives[option]=source; }
        Check(!negatives.Keys.Any(projection.Available.ContainsKey),"Holdout positive/negative conflict");var excluded=Sorted(negatives.Keys).ToImmutableArray();
        return new(table!=null && selected.All(upper.Contains) && projection.Available.Keys.All(upper.Contains),upper,
            upper.Where(o=>!selected.Contains(o) && !projection.Available.ContainsKey(o) && !negatives.ContainsKey(o)).ToImmutableArray(),excluded,excluded.Select(o=>negatives[o]).ToImmutableArray(),projection);
    }
}
