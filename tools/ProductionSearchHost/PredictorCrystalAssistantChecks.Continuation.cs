using System.Collections.Immutable;
using System.Diagnostics;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckCurrentCenterProof(PredictorCrystalSnapshot source)
    {
        var hidden=new bool[121];var occupancy=Enumerable.Repeat(-1,121).ToArray();
        for(int y=4;y<=6;y++) {hidden[4*11+y]=true;occupancy[4*11+y]=0;}
        hidden[0]=hidden[120]=true;
        var board=source with {Remaining=3,Hidden=hidden.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),
            Items=[new("POTION_COMMON",1,3,4,4,1,false)],Revealed=[]};
        var success=PredictorCrystalExplorer.Replay(board,[new(0,0,PredictorCrystalTool.Small),
            new(4,5,PredictorCrystalTool.Big),new(10,10,PredictorCrystalTool.Small)]);
        var potion=success.Rewards.Single(r=>r.Kind==PredictorRewardKind.Potion);
        ImmutableArray<CrystalRewardOption> goals=[new(PredictorRewardKind.Potion,potion.Key)];
        var table=new CrystalReachabilityTable(board,includeRerolls:true);
        var bound=new PredictorCrystalOffsetBound(board,table,true);
        ImmutableArray<PredictorCrystalStep> prefix=[new(4,5,PredictorCrystalTool.Small),new(0,0,PredictorCrystalTool.Small)];
        var snapshot=PredictorCrystalExplorer.Replay(board,prefix).ExportCrystal();
        UInt128 fog=0;for(int i=0;i<121;i++) if(snapshot.Hidden[i]) fog|=(UInt128)1<<i;
        Check(bound.CheckContinuation(goals,snapshot.Revealed,fog,1,currentCenters:false).Verdict==CrystalOffsetVerdict.Possible,
            "Initial-center relaxation can reuse the already revealed potion middle");
        Check(bound.CheckContinuation(goals,snapshot.Revealed,fog,1).Verdict==CrystalOffsetVerdict.Impossible,
            "Current-center proof rejects a missing legal finishing center");
        for(int cell=0;cell<121;cell++) if(snapshot.Hidden[cell])
            foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big})
                Check(!PredictorCrystalExplorer.Replay(board,prefix.Add(new(cell/11,cell%11,tool))).Rewards.Any(r=>r.Kind==PredictorRewardKind.Potion),
                    "Raw legal final clicks independently confirm the center-exclusion certificate");
        Console.WriteLine("CRYSTAL_CURRENT_CENTER_CERTIFICATE_PASS initialCenters=Possible;currentCenters=Impossible;rawFinalClicksAgree=true");
    }

    private static void CheckContinuationWitness(PredictorCrystalSnapshot source,ImmutableArray<PredictorCrystalStep> path,
        ImmutableArray<CrystalRewardOption> goals)
    {
        var table=new CrystalReachabilityTable(source,includeRerolls:true);
        var bound=new PredictorCrystalOffsetBound(source,table,true);
        var finalOrder=PredictorCrystalExplorer.Replay(source,path).ExportCrystal().Revealed;
        int languageWitnesses=0;
        for(int n=0;n<=path.Length;n++)
        {
            var prefix=PredictorCrystalExplorer.Replay(source,path.Take(n).ToImmutableArray()).ExportCrystal();
            UInt128 fog=0;for(int i=0;i<121;i++) if(prefix.Hidden[i]) fog|=(UInt128)1<<i;
            var result=bound.CheckContinuation(goals,prefix.Revealed,fog,path.Length-n);
            Check(result.MaySupport,"Joint continuation bound preserves native-verified reroll/take witness at every prefix");
            Check(bound.CheckContinuation(goals,prefix.Revealed,fog,path.Length-n,0).MaySupport,
                "Proof work cutoff remains unknown, never a negative");
            Check(bound.CheckContinuation(goals,prefix.Revealed,fog,path.Length-n,1).MaySupport,
                "Partial reward-combination enumeration cannot reject a witnessed continuation");
            if(n==path.Length) foreach(int limit in new[]{0,1,3_000})
                Check(bound.CheckFixedWord(goals,prefix.Revealed,limit).MaySupport,
                    "Fixed legal endpoint survives exact-word proof and work cutoffs");
            if(path.Length-n<=2)
            {
                if(prefix.Remaining>0 && Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_LANGUAGE")=="1")
                {
                    var prefixTable=new CrystalReachabilityTable(prefix,includeRerolls:true);
                    var timer=Stopwatch.StartNew();
                    var language=new PredictorCrystalOffsetBound(prefix,prefixTable,true)
                        .CompileLanguage(goals,()=>timer.Elapsed<TimeSpan.FromSeconds(5),100_000);
                    Check(language.Complete,"Controlled native continuation language compiles completely");
                    var future=finalOrder.Skip(prefix.Revealed.Length).ToArray();
                    bool Potion(int i)=>source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal);
                    bool Accept(int root,IEnumerable<int> word)
                    {
                        var active=new HashSet<int>{root};
                        foreach(int item in word.Distinct()) active=active.SelectMany(id=>language.Nodes[id].Edges.Where(e=>e.Item==item).Select(e=>e.Next)).ToHashSet();
                        return active.Any(id=>language.Nodes[id].Terminal);
                    }
                    Check(language.Roots.Any(r=>Accept(r.Potion,future.Where(Potion)) && Accept(r.Core,future.Where(i=>!Potion(i)))),
                        "Compressed language retains complete native witness phases including subscription blocks and frozen upgrade context");
                    languageWitnesses++;
                }
                foreach(int limit in new[]{0,1,100_000})
                {
                    var requirements=bound.Requirements(goals,prefix.Revealed,fog,path.Length-n,limit);
                    Check(requirements.Receipt.MaySupport,"Reverse requirement compilation preserves witnessed continuation under work cutoffs");
                    if(requirements.Receipt.Verdict==CrystalOffsetVerdict.Incomplete) continue;
                    bool Potion(int i)=>source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal);
                    var future=finalOrder.Skip(prefix.Revealed.Length).ToArray();
                    Check(requirements.Alternatives.Any(r=>future.Where(Potion).Take(r.Potions.Length).SequenceEqual(r.Potions) &&
                        future.Where(i=>!Potion(i)).Take(r.Core.Length).SequenceEqual(r.Core)),
                        "A complete reverse condition set includes a prefix of the native-verified final phase words");
                }
            }
        }
        Console.WriteLine($"CRYSTAL_CONTINUATION_WITNESS_PASS seed={source.Context.Seed};prefixes={path.Length+1};goals={goals.Length};seconds={bound.ContinuationSeconds:F4}");
        if(languageWitnesses>0) Console.WriteLine($"CRYSTAL_LANGUAGE_NATIVE_PREFIX_PASS seed={source.Context.Seed};prefixes={languageWitnesses}");
    }

    private static void CompareContinuationSearch(PredictorCrystalSnapshot source,string output,
        Action<ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode> checkNative)
    {
        var tableWatch=Stopwatch.StartNew();var table=new CrystalReachabilityTable(source,includeRerolls:true);
        double commonTableMs=tableWatch.Elapsed.TotalMilliseconds;
        var relicPath=RequiredItemPath(source,i=>i.Kind=="RELIC" || i.Kind=="CARD_COMMON");
        var relicRun=PredictorCrystalExplorer.Replay(source,relicPath);
        var relicReward=relicRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Relic);
        var cardReward=relicRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Card);
        ImmutableArray<CrystalRewardOption> selection=[new(PredictorRewardKind.Relic,relicReward.Key),new(PredictorRewardKind.Card,cardReward.Cards[0].Key,cardReward.Cards[0].UpgradeLevel)];
        var preflight=new PredictorCrystalOffsetBound(source,table,true);
        string? only=Environment.GetEnvironmentVariable("RT2_CRYSTAL_CONTINUATION_TARGET");
        var targets=table.Candidates.Where(o=>o.Kind==PredictorRewardKind.Card && !selection.Contains(o) &&
            (only==null || o.Key.Entry==only) &&
            source.Context.Catalog!.CardDefinitions.Any(c=>c.Prototype.Key==o.Key && c.Rarity=="Uncommon") &&
            preflight.Check(selection.Add(o)).MaySupport).OrderByDescending(o=>o.UpgradeLevel).Take(4).ToArray();
        Check(targets.Length>0,"Comparison includes queries not already excluded at the root");
        var receipts=new List<object>();
        bool batchCompare=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_COMPARE")=="1";
        bool contextReuse=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_CONTEXT_REUSE")=="1";
        PredictorCrystalOffsetBound? shared=null;
        bool reverse=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1";
        bool languageCompare=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_LANGUAGE")=="1";
        IEnumerable<CrystalRewardOption?> sequence=reverse?targets.AsEnumerable().Reverse():targets;
        if(contextReuse)
        {
            CheckLanguageContextIdentity(source,table,selection.Add(targets[0]),checkNative);
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_CONTEXT_GUARD_ONLY")=="1") return;
            var ordered=sequence.ToArray();
            sequence=ordered.Concat(new CrystalRewardOption?[]{ordered[0],null,ordered[0]});
        }
        string[] modes=contextReuse?["goal-language-cold","goal-language-shared"]:languageCompare?["goal-batch","goal-language"]:batchCompare?["goal-first","goal-batch"]:["reverse-single","reverse","goal-first"];
        var queries=sequence.Select(target=>(Label:"original",Target:target,Goals:target==null?selection:selection.Add(target))).ToList();
        if(contextReuse && Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_NEGATIVE_REUSE")=="1")
        {
            var fixedGoals=selection.Add(targets[0]);
            var extra=table.Candidates.First(g=>!fixedGoals.Contains(g));
            queries.Add(("permutation",targets[0],fixedGoals.Reverse().ToImmutableArray()));
            queries.Add(("add-unrelated",targets[0],fixedGoals.Add(extra)));
            queries.Add(("remove-essential",null,selection));
            queries.Add(("reselect",targets[0],fixedGoals));
        }
        int queryIndex=0;
        foreach(var query in queries)
        {
            var target=query.Target;
            queryIndex++;
            foreach(string mode in reverse?modes.AsEnumerable().Reverse():modes)
            {
                var goals=query.Goals;var watch=Stopwatch.StartNew();
                if(mode.StartsWith("goal-",StringComparison.Ordinal))
                {
                    bool firstShared=mode=="goal-language-shared" && shared==null;
                    if(mode=="goal-language-shared") shared??=new(source,table,true);
                    var answer=mode.StartsWith("goal-language",StringComparison.Ordinal)?PredictorCrystalGoalLanguage.Solve(source,table,goals,true,TimeSpan.FromSeconds(only==null?6:45),sharedBound:mode=="goal-language-shared"?shared:null):mode=="goal-batch"
                        ?PredictorCrystalGoalBatch.Solve(source,table,goals,true,TimeSpan.FromSeconds(only==null?6:45))
                        :PredictorCrystalGoalSearch.Solve(source,table,goals,true,TimeSpan.FromSeconds(only==null?6:45));
                    watch.Stop();
                    if(contextReuse) answer=answer with {Seconds=watch.Elapsed.TotalSeconds};
                    if(answer.Plan is {} witness) checkNative(witness.Steps,ReplayProof(source,witness));
                    Console.WriteLine($"CRYSTAL_GOAL_FIRST_COMPARE mode={mode};query={queryIndex};label={query.Label};firstShared={firstShared};target={target};verdict={answer.Verdict};found={answer.Plan!=null};ms={answer.Seconds*1000:F2};commonTableMs={commonTableMs:F2};states={answer.RewardStates};recipes={answer.Recipes};geometry={answer.GeometryNodes};verified={answer.VerifiedWords};paddingUnknown={answer.PaddingUnknown};batches={answer.Batches};languageNodes={answer.LanguageNodes};roots={answer.LanguageRoots};compileMs={answer.CompileSeconds*1000:F2};origin={answer.NegativeKind};hit={answer.NegativeCacheHit};goals={string.Join('|',goals)}");
                    receipts.Add(new {selection,target,goals,query.Label,mode,queryIndex,firstShared,commonTableMs,answer});continue;
                }
                var bound=new PredictorCrystalOffsetBound(source,table,true);
                var root=bound.Check(goals);
                int workers=mode=="reverse-single"?1:4;
                var lanes=Enumerable.Range(0,workers).Select(i=>new PredictorCrystalExplorer(source,true,reachability:table,selected:goals,
                    potionScenarioBound:true,targetDirected:i==0,stopAfterPlan:true,offsetBound:bound,rootPartition:i,rootPartitions:workers,
                    directedProof:true,reverseProof:true)).ToArray();
                while(watch.Elapsed<TimeSpan.FromSeconds(only==null?6:45) && !lanes.All(l=>l.Complete) && !lanes.Any(l=>l.QueryProjection.SelectedPlan!=null))
                    Parallel.ForEach(lanes,l=>l.Advance(TimeSpan.FromMilliseconds(100)));
                bool positive=lanes.Any(l=>l.QueryProjection.SelectedPlan!=null),complete=lanes.All(l=>l.Complete);
                Console.WriteLine($"CRYSTAL_CONTINUATION_COMPARE target={target};mode={mode};root={root.Verdict};positive={positive};complete={complete};ms={watch.Elapsed.TotalMilliseconds:F2};examined={lanes.Sum(l=>l.Examined)};checks={bound.ContinuationChecks};rejected={bound.ContinuationRejected};coverRejected={bound.CompletionCoverRejected};tailChecks={lanes.Sum(l=>l.TwoStepProofChecks)};tailRejected={lanes.Sum(l=>l.TwoStepProofRejected)};tailWords={lanes.Sum(l=>l.TwoStepProofWords)};hits={bound.ContinuationHits};proofSeconds={bound.ContinuationSeconds:F3}");
                receipts.Add(new {selection,target,mode,workers,commonTableMs,root,positive,complete,ms=watch.Elapsed.TotalMilliseconds,
                    examined=lanes.Sum(l=>l.Examined),tailChecks=lanes.Sum(l=>l.TwoStepProofChecks),tailRejected=lanes.Sum(l=>l.TwoStepProofRejected),tailWords=lanes.Sum(l=>l.TwoStepProofWords),tailWordHits=lanes.Sum(l=>l.TwoStepWordCacheHits),bound.ContinuationChecks,bound.ContinuationRejected,bound.CompletionCoverRejected,bound.CurrentCenterRejected,bound.ContinuationHits,bound.ContinuationSeconds,
                    reverseChecks=lanes.Sum(l=>l.ReverseChecks),reverseRejected=lanes.Sum(l=>l.ReverseRejected),reverseAlternatives=lanes.Sum(l=>l.ReverseAlternatives),reverseGeometry=lanes.Sum(l=>l.ReverseGeometryNodes),bound.RequirementBuilds,bound.RequirementIncomplete,bound.RequirementWords,bound.RequirementSeconds});
                Console.WriteLine($"CRYSTAL_REVERSE_COMPARE mode={mode};checks={lanes.Sum(l=>l.ReverseChecks)};rejected={lanes.Sum(l=>l.ReverseRejected)};alternatives={lanes.Sum(l=>l.ReverseAlternatives)};geometry={lanes.Sum(l=>l.ReverseGeometryNodes)};builds={bound.RequirementBuilds};incomplete={bound.RequirementIncomplete};words={bound.RequirementWords};compileSeconds={bound.RequirementSeconds:F3}");
            }
        }
        ProductionSearchReplay.Write(Path.Combine(output,"continuation-comparison.json"),receipts);
    }
}
