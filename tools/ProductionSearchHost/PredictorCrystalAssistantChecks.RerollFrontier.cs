using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckDriftwoodProductionQueries(PredictorCrystalSnapshot source,
        Action<ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode> native,string output)
    {
        var receipts=new List<object>();
        bool contextReuse=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_CONTEXT_REUSE")=="1";
        CrystalReachabilityTable? sharedTable=null;PredictorCrystalOffsetBound? sharedBound=null;
        string? replayDirectory=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_QUERY_REPLAY_DIR");
        if(replayDirectory!=null)
        {
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(replayDirectory,"production-queries-"+source.Context.Seed+".json")));
            foreach(string json in document.RootElement.EnumerateArray().Select(r=>r.GetProperty("selected")).Where(r=>r.GetArrayLength()>0).Select(r=>r.GetRawText()).Distinct())
                CompareGoals(JsonSerializer.Deserialize<ImmutableArray<CrystalRewardOption>>(json,ProductionSearchReplay.JsonOptions));
            ProductionSearchReplay.Write(Path.Combine(output,"production-queries-"+source.Context.Seed+".json"),receipts);
            return;
        }
        var reach=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,includeRerolls:true);
        ImmutableArray<CrystalRewardOption> selected=[];
        for(int depth=0;depth<3;depth++)
        {
            var timer=Stopwatch.StartNew();var known=reach.ProjectKnown(selected);double knownMs=timer.Elapsed.TotalMilliseconds;
            CrystalReachabilityUpdate? update=null;double firstMs=known.Available.IsEmpty?-1:knownMs;
            while(timer.Elapsed<TimeSpan.FromSeconds(2))
            {
                update=reach.Advance(selected,TimeSpan.FromMilliseconds(100));
                if(firstMs<0 && !update.Projection.Available.IsEmpty) firstMs=timer.Elapsed.TotalMilliseconds;
                if(update.Complete) break;
            }
            var projection=update?.Projection??known;
            double searchMs=timer.Elapsed.TotalMilliseconds;var nativeTimer=Stopwatch.StartNew();
            if(depth>0)
            {
                Check(projection.SelectedPlan!=null,"Previously witnessed Driftwood target retains a legal plan");
                var plan=projection.SelectedPlan!;
                // Exercise production publication, including reroll-aware gold refinement.
                native(plan.Steps,ReplayProof(source,plan));
                var serialized=JsonSerializer.Serialize(plan,ProductionSearchReplay.JsonOptions);
                var restored=JsonSerializer.Deserialize<PredictorCrystalSolution>(serialized,ProductionSearchReplay.JsonOptions)!;
                Check(PredictorCrystalRewardGuide.Build(source,restored).Actions.Length==plan.RerollRewardIndices.Length+plan.Takes.Length,
                    "Published plan survives snapshot serialization and guide reconstruction");
            }
            double nativeMs=depth>0?nativeTimer.Elapsed.TotalMilliseconds:0;
            if(depth>0 && Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_FIRST_EXPERIMENT")=="1")
                CompareGoals(selected);
            Console.WriteLine($"DRIFT_PRODUCTION_QUERY seed={source.Context.Seed};goals={depth};cachedMs={knownMs:F3};firstCandidatesMs={firstMs:F3};searchMs={searchMs:F3};nativeMs={nativeMs:F3};available={projection.Available.Count};complete={update?.Complete};gold={projection.Gold};rerolls={projection.SelectedPlan?.RerollRewardIndices.Length};workers=4");
            receipts.Add(new {seed=source.Context.Seed,selected,cachedMs=knownMs,firstCandidatesMs=firstMs,
                searchMs,nativeMs,available=projection.Available.Count,complete=update?.Complete,projection.Gold,plan=projection.SelectedPlan});
            var next=projection.Available.Where(p=>p.Key.Kind==PredictorRewardKind.Card)
                .OrderByDescending(p=>p.Value.RerollRewardIndices.Length).Select(p=>p.Key).FirstOrDefault();
            if(next==null) break;
            selected=selected.Add(next);
        }
        ProductionSearchReplay.Write(Path.Combine(output,"production-queries-"+source.Context.Seed+".json"),receipts);

        void CompareGoals(ImmutableArray<CrystalRewardOption> targets)
        {
            var prepared=Stopwatch.StartNew();
            var index=contextReuse?(sharedTable??=new(source,includeRerolls:true)):new CrystalReachabilityTable(source,includeRerolls:true);
            double tableMs=prepared.Elapsed.TotalMilliseconds;
            bool batchFirst=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1";
            bool language=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_LANGUAGE")=="1";
            string[] modes=contextReuse?["language-cold","language-shared"]:language?["batch","language"]:["single","batch"];
            foreach(string mode in batchFirst?modes.AsEnumerable().Reverse():modes)
            {
                var queryWatch=Stopwatch.StartNew();bool firstShared=mode=="language-shared" && sharedBound==null;
                if(mode=="language-shared") sharedBound??=new(source,index,true);
                var answer=mode.StartsWith("language",StringComparison.Ordinal)?PredictorCrystalGoalLanguage.Solve(source,index,targets,true,TimeSpan.FromSeconds(6),sharedBound:mode=="language-shared"?sharedBound:null)
                    :mode=="batch"?PredictorCrystalGoalBatch.Solve(source,index,targets,true,TimeSpan.FromSeconds(6))
                    :PredictorCrystalGoalSearch.Solve(source,index,targets,true,TimeSpan.FromSeconds(6));
                queryWatch.Stop();if(contextReuse) answer=answer with {Seconds=queryWatch.Elapsed.TotalSeconds};
                Check(answer.Verdict!=CrystalOffsetVerdict.Impossible,"Goal-first must not exclude an existing verified plan");
                if(answer.Plan is {} fresh) native(fresh.Steps,ReplayProof(source,fresh));
                Console.WriteLine($"CRYSTAL_GOAL_FIRST_SEED_QUERY mode={mode};firstShared={firstShared};seed={source.Context.Seed};goals={targets.Length};verdict={answer.Verdict};tableMs={tableMs:F3};solveMs={answer.Seconds*1000:F3};recipes={answer.Recipes};geometry={answer.GeometryNodes};batches={answer.Batches};languageNodes={answer.LanguageNodes};roots={answer.LanguageRoots};compileMs={answer.CompileSeconds*1000:F2};targets={string.Join('|',targets)};source=known-reachable-production-plan");
                receipts.Add(new {seed=source.Context.Seed,selected=targets,mode,firstShared,tableMs,answer});
            }
        }
    }
    private static void CheckLanguageContextIdentity(PredictorCrystalSnapshot source,CrystalReachabilityTable table,
        ImmutableArray<CrystalRewardOption> goals,Action<ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode> native)
    {
        var bound=new PredictorCrystalOffsetBound(source,table,true);
        int rejected=0;
        void Reject(PredictorCrystalSnapshot board,CrystalReachabilityTable index,bool avoid)
        {
            try {PredictorCrystalGoalLanguage.Solve(board,index,[],avoid,TimeSpan.Zero,sharedBound:bound);}
            catch(ArgumentException) {rejected++;return;}
            throw new InvalidOperationException("Mismatched language context was accepted");
        }
        Reject(source with {Remaining=source.Remaining-1},table,true);
        Reject(source,new CrystalReachabilityTable(source,includeRerolls:false),true);
        Reject(source,table,false);
        Check(rejected==3,"Source/table/avoid context mismatches rejected");
        Check(PredictorCrystalGoalLanguage.Solve(source,table,[],true,TimeSpan.Zero,sharedBound:bound).Verdict==CrystalOffsetVerdict.Incomplete,"Correct context preserves cutoff unknown");
        Console.WriteLine("CRYSTAL_LANGUAGE_CONTEXT_GUARD_PASS source/table/avoid identity; cutoffUnknown=true");
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_CONTEXT_GUARD_ONLY")=="1")
        {
            // This bound is separate from every measured context.
            var interrupted=new PredictorCrystalOffsetBound(source,table,true);
            var partial=PredictorCrystalGoalLanguage.Solve(source,table,goals,true,TimeSpan.FromSeconds(6),stateLimit:1,sharedBound:interrupted);
            Check(partial.Verdict==CrystalOffsetVerdict.Incomplete,"Mid-compile state limit must remain unknown");
            var complete=PredictorCrystalGoalLanguage.Solve(source,table,goals,true,TimeSpan.FromSeconds(6),sharedBound:interrupted);
            Check(complete.Verdict==CrystalOffsetVerdict.Possible && complete.Plan!=null,"Limited query must not contaminate a later query on the same bound");
            native(complete.Plan!.Steps,ReplayProof(source,complete.Plan));
            Console.WriteLine($"CRYSTAL_LANGUAGE_CONTEXT_PARTIAL_GUARD_PASS limit=1;partial={partial.Verdict};next={complete.Verdict};nativeParity=true;separateFromBenchmark=true");
        }
    }
    private static void CheckRerollFrontier(PredictorCrystalSnapshot source,ImmutableArray<PredictorCrystalStep> nativePath,
        ImmutableArray<CrystalRewardOption> observedGoals,Action<ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode> checkNative,string output)
    {
        // Raw click enumeration keeps blank/preparatory moves and hidden centers.
        // Suffix reference uses raw Enter/Exit/Reroll/Take, not the new offset DP.
        bool languageExperiment=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_LANGUAGE")=="1";
        CrystalGoalSearchResult SolveGoal(PredictorCrystalSnapshot board,CrystalReachabilityTable table,
            ImmutableArray<CrystalRewardOption> goals,TimeSpan budget,int stateLimit=100_000)=>languageExperiment
            ?PredictorCrystalGoalLanguage.Solve(board,table,goals,true,budget,stateLimit,
                sharedBound:new(board,table,true,packingCover:PackingCoverEnabled),rootCardSlots:RootCardSlotsEnabled)
            :PredictorCrystalGoalBatch.Solve(board,table,goals,true,budget,stateLimit);
        CheckCurrentCenterProof(source);
        if(PackingCoverEnabled) CheckPackingCoverOracle(source);
        if(RootCardSlotsEnabled) CheckRootCardSlotGuards(source);
        var shapes=new[]{"one","three","repeat","potion","tail","ordered-tail"};
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_RELIC_ORACLE")=="1") shapes=[..shapes,"relic-tail","revealed-relic-tail","repeat-relic-tail"];
        foreach(string shape in shapes)
        {
            var specs=shape switch {
                "one"=>new[]{new PredictorCrystalItem("CARD_COMMON",2,2,2,2,1,false)},
                "three"=>new[]{new PredictorCrystalItem("CARD_COMMON",2,2,1,3,1,false),new("CARD_UNCOMMON",2,2,5,3,1,false),new("CARD_RARE",2,2,8,7,1,false)},
                "repeat"=>new[]{new PredictorCrystalItem("CARD_COMMON",2,2,2,2,2,false)},
                "ordered-tail"=>new[]{new PredictorCrystalItem("CARD_COMMON",2,2,1,3,1,false),new("CARD_UNCOMMON",2,2,3,3,1,false),new("CARD_RARE",2,2,8,7,1,false)},
                "relic-tail" or "revealed-relic-tail" or "repeat-relic-tail"=>new[]{new PredictorCrystalItem("RELIC",4,4,1,1,shape=="repeat-relic-tail"?2:1,shape=="revealed-relic-tail"),new("CARD_COMMON",2,2,5,3,1,false),new("POTION_COMMON",1,3,8,7,1,false)},
                _=>new[]{new PredictorCrystalItem("CARD_COMMON",2,2,1,3,1,false),new("CARD_UNCOMMON",2,2,5,3,1,false),new("POTION_COMMON",1,3,8,7,1,false)} };
            var hidden=new bool[121];var occupancy=Enumerable.Repeat(-1,121).ToArray();
            for(int i=0;i<specs.Length;i++) for(int x=specs[i].X;x<specs[i].X+specs[i].Width;x++) for(int y=specs[i].Y;y<specs[i].Y+specs[i].Height;y++)
            { hidden[x*11+y]=true;occupancy[x*11+y]=i; }
            hidden[0]=hidden[120]=true;
            // Two adjacent partial groups must complete together to leave a
            // click for the distant third group; it cannot settle between them.
            if(shape=="ordered-tail") foreach(int x in new[]{1,4}) foreach(int y in new[]{3,4}) hidden[x*11+y]=false;
            if(shape.Contains("relic",StringComparison.Ordinal))
                for(int x=1;x<5;x++) for(int y=1;y<5;y++) hidden[x*11+y]=shape!="revealed-relic-tail" && x==1 && y==1;
            var board=source with { Remaining=shape.EndsWith("tail",StringComparison.Ordinal)?2:3,Hidden=hidden.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),Items=specs.ToImmutableArray(),Revealed=[] };
            if(shape=="revealed-relic-tail") board=board with { Revealed=[0],Items=board.Items.SetItem(0,board.Items[0] with {Subscriptions=3}) };
            var timer=Stopwatch.StartNew();var rows=new Dictionary<string,CrystalRewardRoute>();long paths=0;
            void Visit(ImmutableArray<PredictorCrystalStep> steps)
            {
                var run=PredictorCrystalExplorer.Replay(board,steps);
                if(steps.Length==board.Remaining)
                {
                    paths++;if(run.Phase!=PredictorPhase.Rewards) return;
                    string key=JsonSerializer.Serialize(new {run.Working,run.Rewards,Rng=run.ExportCrystal().EventRng},ProductionSearchReplay.JsonOptions);
                    rows.TryAdd(key,new(steps,run.Rewards));return;
                }
                var current=run.ExportCrystal();
                for(int cell=0;cell<121;cell++) if(current.Hidden[cell])
                    foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}) Visit(steps.Add(new(cell/11,cell%11,tool)));
            }
            Visit([]);
            var prefixTable=new CrystalReachabilityTable(board,includeRerolls:true);
            var prefixBound=new PredictorCrystalOffsetBound(board,prefixTable,true,packingCover:PackingCoverEnabled);
            int prefixChecks=0;
            var truth=new HashSet<string>();var options=new HashSet<CrystalRewardOption>();
            foreach(var row in rows.Values)
            {
                var run=PredictorCrystalExplorer.Replay(board,row.Steps);
                var raw=CheckNormalization(run);
                var nonCards=PredictorCrystalExplorer.Options(row).Where(o=>o.Kind!=PredictorRewardKind.Card).ToArray();
                if(shape.Contains("relic",StringComparison.Ordinal)) foreach(var option in nonCards) options.Add(option);
                foreach(var outcome in raw)
                {
                    var cards=outcome.Split('|',StringSplitOptions.RemoveEmptyEntries).Select(k=>
                    { var pair=k.Split(':');return new CrystalRewardOption(PredictorRewardKind.Card,new("CARD",pair[0]),int.Parse(pair[1])); }).ToArray();
                    foreach(var c in cards) options.Add(c);
                    foreach(var n in nonCards) options.Add(n);
                    truth.Add(string.Join('|',cards.Concat(nonCards).Select(GoalKey).Order()));
                    var goals=cards.Concat(nonCards).ToImmutableArray();
                    if(RootCardSlotsEnabled) Check(!prefixBound.RejectRootCardSlots(goals),"Root proof retains full raw outcome: "+shape);
                    for(int p=1;p<row.Steps.Length;p++)
                    {
                        var prefix=PredictorCrystalExplorer.Replay(board,row.Steps.Take(p).ToImmutableArray()).ExportCrystal();
                        UInt128 fog=0;for(int cell=0;cell<121;cell++) if(prefix.Hidden[cell]) fog|=(UInt128)1<<cell;
                        Check(prefixBound.CheckContinuation(goals,prefix.Revealed,fog,row.Steps.Length-p).MaySupport,
                            "Continuation certificate preserves raw suffix outcome: "+shape);prefixChecks++;
                        if(shape.Contains("relic",StringComparison.Ordinal) && !goals.IsEmpty)
                        {
                            var prefixLookup=new CrystalReachabilityTable(prefix,includeRerolls:true,relicOffsetBound:true);
                            Check(prefixLookup.HasSingleRelicOffsets==(shape!="repeat-relic-tail"),"Single-relic scope uses historical callback count");
                            var probe=new PredictorCrystalExplorer(prefix,true,reachability:prefixLookup,selected:goals.RemoveAt(goals.Length-1),stopAfterPlan:true);
                            Check(probe.InitialMaySupport(goals[^1]),"Relic offset arithmetic retains actual raw suffix, including late potion and rerolls");
                        }
                    }
                }
            }
            var table=new CrystalReachabilityTable(board,includeRerolls:true,relicOffsetBound:Environment.GetEnvironmentVariable("RT2_CRYSTAL_RELIC_ORACLE")=="1");var bound=new PredictorCrystalOffsetBound(board,table,true,packingCover:PackingCoverEnabled);
            if(shape=="three")
            {
                // Both required card groups are individually one-click finishes,
                // but no remaining click can finish them together. The old
                // reward-prefix proof admits this; the completed family rejects it.
                var allCards=rows.Values.First(r=>r.Rewards.Count(x=>x.Kind==PredictorRewardKind.Card)==3);
                var pair=allCards.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card &&
                    r.RerollRecipe!.Rarity!=RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon)
                    .Select(r=>new CrystalRewardOption(PredictorRewardKind.Card,r.Cards[0].Key,r.Cards[0].UpgradeLevel)).ToImmutableArray();
                ImmutableArray<PredictorCrystalStep> blanks=[new(0,0,PredictorCrystalTool.Small),new(10,10,PredictorCrystalTool.Small)];
                var prefix=PredictorCrystalExplorer.Replay(board,blanks).ExportCrystal();
                UInt128 fog=0;for(int cell=0;cell<121;cell++) if(prefix.Hidden[cell]) fog|=(UInt128)1<<cell;
                Check(bound.CheckContinuation(pair,prefix.Revealed,fog,1,100_000,completionCover:false).Verdict==CrystalOffsetVerdict.Possible,
                    "Controlled pair passes independent item eligibility and reward-prefix arithmetic");
                Check(bound.CheckContinuation(pair,prefix.Revealed,fog,1,100_000).Verdict==CrystalOffsetVerdict.Impossible,
                    "All successful reward combinations exceed the remaining joint cover budget");
                for(int cell=0;cell<121;cell++) if(prefix.Hidden[cell])
                    foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big})
                    {
                        var final=PredictorCrystalExplorer.Replay(board,blanks.Add(new(cell/11,cell%11,tool)));
                        Check(final.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)<2,
                            "Raw legal last-click enumeration independently rejects the two-rarity pair");
                    }
                Console.WriteLine("CRYSTAL_COMPLETION_COVER_CERTIFICATE_PASS oldPrefix=Possible;new=Impossible;rawLastClicksAgree=true");
            }
            bool RawSupports(ImmutableArray<CrystalRewardOption> goals)=>truth.Any(row=>goals.All(g=>row.Split('|').Contains(GoalKey(g))));
            if(shape=="tail")
            {
                var initialTable=new CrystalReachabilityTable(board,includeRerolls:false);
                var initialOptions=rows.Values.SelectMany(PredictorCrystalExplorer.Options).Distinct().ToArray();int questions=0;
                for(int a=0;a<initialOptions.Length;a++) for(int b=a+1;b<initialOptions.Length;b++)
                {
                    ImmutableArray<CrystalRewardOption> goals=[initialOptions[a],initialOptions[b]];
                    bool expected=rows.Values.Any(row=>PredictorCrystalExplorer.VerifySelection(board,row,goals)!=null);
                    var answer=SolveGoal(board,initialTable,goals,TimeSpan.FromSeconds(5));
                    Check(answer.Verdict!=CrystalOffsetVerdict.Incomplete && (answer.Plan!=null)==expected,"Goal-first initial-only mode agrees with raw no-reroll reward takes");
                    Check(answer.Plan==null || answer.Plan.RerollActions.IsEmpty,"Disabled Driftwood cannot appear in goal-first witness");questions++;
                }
                var limited=SolveGoal(board,table,[options.First()],TimeSpan.Zero);
                Check(limited.Verdict==CrystalOffsetVerdict.Incomplete,"Goal-first timeout remains unknown");
                Check(SolveGoal(board,table,[options.First()],TimeSpan.FromSeconds(5),stateLimit:0).Verdict==CrystalOffsetVerdict.Incomplete,"Goal-first reward-state cutoff remains unknown");
                Console.WriteLine($"CRYSTAL_GOAL_BATCH_INITIAL_ONLY_PASS language={languageExperiment};questions={questions};cutoffsRemainUnknown=true");
                if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_SUPPORT_TRANSFER")=="oracle")
                    CheckSupportTransferRaw(board,initialTable,initialOptions,
                        goals=>rows.Values.Any(row=>PredictorCrystalExplorer.VerifySelection(board,row,goals)!=null),"initial-tail");
            }
            if(shape.EndsWith("tail",StringComparison.Ordinal))
            {
              var reuseOrigins=new HashSet<CrystalLanguageNegativeKind>();
              foreach(bool reverse in new[]{false,true})
              {
                var candidates=options.ToArray();long queries=0,rejected=0,checks=0,goalStates=0,goalRecipes=0;
                for(int a=0;a<candidates.Length;a++) for(int b=a+1;b<candidates.Length;b++)
                {
                    ImmutableArray<CrystalRewardOption> goals=[candidates[a],candidates[b]];
                    var search=new PredictorCrystalExplorer(board,true,exhaustiveOnly:true,reachability:table,selected:goals,
                        stopAfterPlan:true,offsetBound:bound,twoStepProof:!reverse,reverseProof:reverse);
                    var watch=Stopwatch.StartNew();
                    while(!search.Complete && watch.Elapsed<TimeSpan.FromSeconds(5)) search.Advance(TimeSpan.FromMilliseconds(20));
                    Check(search.Complete,"Controlled two-step proof query completes");
                    Check((search.QueryProjection.SelectedPlan!=null)==RawSupports(goals),"Two-step ordered proof agrees with raw click/reroll pair oracle");
                    queries++;rejected+=reverse?search.ReverseRejected:search.TwoStepProofRejected;checks+=reverse?search.ReverseChecks:search.TwoStepProofChecks;
                    if(reverse)
                    {
                        var independent=SolveGoal(board,table,goals,TimeSpan.FromSeconds(5));
                        Check(independent.Verdict!=CrystalOffsetVerdict.Incomplete,"Independent goal-first solver closes small raw pair query");
                        Check((independent.Plan!=null)==RawSupports(goals),"Independent goal-first outcome equals raw click/reroll pair truth");
                        goalStates+=independent.RewardStates;goalRecipes+=independent.Recipes;
                        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_NEGATIVE_REUSE")=="1" && languageExperiment &&
                            independent.Verdict==CrystalOffsetVerdict.Impossible && reuseOrigins.Add(independent.NegativeKind))
                            CheckLanguageNegativeReuse(board,table,candidates,RawSupports,shape,goals);
                    }
                }
                if(shape=="ordered-tail") Check(rejected>0,"Ordered-tail fixture exercises additional joint negative certificates");
                Console.WriteLine($"CRYSTAL_TWO_STEP_PAIR_ORACLE_PASS shape={shape};reverse={reverse};queries={queries};tailChecks={checks};tailRejected={rejected};rawEquality=true");
                if(reverse) Console.WriteLine($"CRYSTAL_GOAL_BATCH_PAIR_ORACLE_PASS language={languageExperiment};shape={shape};queries={queries};rewardStates={goalStates};recipes={goalRecipes};rawEquality=true");
              }
              if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_NEGATIVE_REUSE")=="1")
                  Console.WriteLine($"CRYSTAL_LANGUAGE_NEGATIVE_ORIGINS shape={shape};observed={string.Join(',',reuseOrigins)};orderedFailureNatural=false");
              if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_SUPPORT_TRANSFER")=="oracle" && !shape.Contains("relic",StringComparison.Ordinal))
                  CheckSupportTransferRaw(board,table,options.ToArray(),RawSupports,shape);
            }
            foreach(var goal in options) Check(bound.Check([goal]).Verdict!=CrystalOffsetVerdict.Impossible,"Expanded offset DP retains every raw reachable singleton: "+shape);
            foreach(var combination in truth)
            {
                var goals=options.Where(o=>combination.Split('|').Contains(GoalKey(o))).ToImmutableArray();
                Check(bound.Check(goals).Verdict!=CrystalOffsetVerdict.Impossible,"Joint suffix DP retains raw simultaneous goals: "+shape);
            }
            var reach=new PredictorCrystalReachability(board,true,discreteRewardBound:true,jointOffsetBound:true,includeRerolls:true,
                packingCover:PackingCoverEnabled,relicOffsetBound:Environment.GetEnvironmentVariable("RT2_CRYSTAL_RELIC_ORACLE")=="1",
                goalLanguageProof:Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_DISPATCH")=="1");
            CrystalReachabilityUpdate Close(ImmutableArray<CrystalRewardOption> selected)
            {
                var watch=Stopwatch.StartNew();var result=reach.Advance(selected,TimeSpan.Zero);
                while(!result.Complete && watch.Elapsed<TimeSpan.FromSeconds(30)) result=reach.Advance(selected,TimeSpan.FromMilliseconds(30));
                Check(result.Complete,"Controlled reroll candidate frontier closes: "+shape);
                var expected=options.Where(o=>!selected.Contains(o) && RawSupports(selected.Add(o))).ToHashSet();
                Check(expected.SetEquals(result.Projection.Available.Keys),"Controlled complete candidate projection matches raw click 脳 reward actions: "+shape);
                Check(result.ExcludedCandidates.All(o=>!RawSupports(selected.Add(o))),"Every exclusion is absent from raw oracle: "+shape);
                Console.WriteLine($"DRIFT_FRONTIER_CLOSE shape={shape};goals={selected.Length};ms={watch.Elapsed.TotalMilliseconds:F3};available={result.Projection.Available.Count};excluded={result.ExcludedCandidates.Length};examined={result.Progress.Examined}");
                return result;
            }
            var completed=Close([]);
            var selected=completed.Projection.Available.Keys.First(o=>o.Kind==PredictorRewardKind.Card);
            Close([selected]);
            long prior=reach.ProofReplays;var known=reach.ProjectKnown([selected]);long afterFirst=reach.ProofReplays;reach.ProjectKnown([selected]);
            long cacheDelta=reach.ProofReplays-prior;
            if(shape.Contains("relic",StringComparison.Ordinal))
            {
                // New mixed relic fixtures also request options skipped during
                // root filtering. Measure first projection work separately;
                // subsequent identical projections must reuse its take proofs.
                Console.WriteLine($"CRYSTAL_RELIC_RETARGET shape={shape};firstReplays={afterFirst-prior};repeatReplays={reach.ProofReplays-afterFirst}");
                Check(reach.ProofReplays==afterFirst && known.SelectedPlan!=null,"Repeated relic projection reuses take cache: "+shape);
            }
            else Check(cacheDelta==0 && known.SelectedPlan!=null,"Retarget projection reuses expanded endpoint/take cache: "+shape);
            if(shape=="one")
            {
                var legacy=new CrystalReachabilityTable(board);var target=options.First(o=>!legacy.Candidates.Contains(o));
                Check(new PredictorCrystalOffsetBound(board,legacy,true).Check([target]).Verdict==CrystalOffsetVerdict.Impossible,"Old domain has its own initial-only negative");
                Check(bound.Check([target]).Verdict==CrystalOffsetVerdict.Possible,"Expanded domain does not inherit old negative");
                var found=Close([target]).Projection.SelectedPlan!;
                Check(!found.RerollActions.IsEmpty,"Research search carries actual reroll suffix in positive proof");
                Check(bound.Check([target],stateLimit:0).Verdict==CrystalOffsetVerdict.Incomplete,"Cutoff proof cache cannot poison full-domain positive");
            }
            Console.WriteLine($"DRIFT_FRONTIER_ORACLE shape={shape};rawPaths={paths};endpoints={rows.Count};rawGoalSets={truth.Count};candidates={options.Count};seconds={timer.Elapsed.TotalSeconds:F3};fullCandidateEquality=true;prefixChecks={prefixChecks};retargetReplayDelta={cacheDelta};proofCacheHits={reach.ProofCacheHits}");
        }
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_SUPPORT_TRANSFER")=="oracle") return;
        // Natural captured board, actual final-click continuation, original source
        // native check callback. This is not the artificial geometry above.
        var partial=PredictorCrystalExplorer.Replay(source,nativePath.Take(5).ToImmutableArray()).ExportCrystal();
        var tableFull=new CrystalReachabilityTable(partial,includeRerolls:true);
        var root=PredictorCrystalExplorer.Replay(source,nativePath);
        var expanded=PredictorCrystalExplorer.WithRerolls(new(nativePath,root.Rewards),root);
        var initial=PredictorCrystalExplorer.Options(new(nativePath,root.Rewards)).ToHashSet();
        var targetFull=PredictorCrystalExplorer.Options(expanded).First(o=>o.Kind==PredictorRewardKind.Card && !initial.Contains(o));
        var fullTableWatch=Stopwatch.StartNew();var fullTable=new CrystalReachabilityTable(source,includeRerolls:true);
        Console.WriteLine($"DRIFT_WHOLE_TABLE initialMaximum={fullTable.MaximumInitialDraws};expandedMaximum={fullTable.MaximumDraws};candidates={fullTable.Candidates.Length};ms={fullTableWatch.Elapsed.TotalMilliseconds:F3}");
        var sweep=new List<object>();
        var witnesses=new List<object>();
        foreach(int count in new[]{1,2,3}) foreach(string mode in new[]{"unbounded","prior","focused"})
        {
            bool bounded=mode!="unbounded";
            var goals=observedGoals.Take(count).ToImmutableArray();
            Check(PredictorCrystalExplorer.VerifySelection(source,expanded,goals)!=null,"Whole-board query has a known legal reroll witness before timing");
            var watch=Stopwatch.StartNew();var explorer=new PredictorCrystalExplorer(source,true,reachability:fullTable,selected:goals,
                potionScenarioBound:true,targetDirected:true,stopAfterPlan:true,offsetBound:bounded?new(source,fullTable,true):null,
                currentCenters:mode=="focused",directedProof:mode=="focused");
            while(!explorer.Complete && watch.Elapsed<TimeSpan.FromSeconds(10)) explorer.Advance(TimeSpan.FromMilliseconds(30));
            var plan=explorer.QueryProjection.SelectedPlan;
            double searchMs=watch.Elapsed.TotalMilliseconds;
            Check(!explorer.Complete || plan!=null,"Whole-board known reachable query cannot be certified absent");
            var nativeWatch=Stopwatch.StartNew();
            if(plan!=null)
            {
                var replayed=ReplayProof(source,plan);checkNative(plan.Steps,replayed);
                witnesses.Add(new { count,bounded,goals,plan.Steps,operations=Operations(PredictorCrystalExplorer.Replay(source,plan.Steps),replayed) });
            }
            double nativeMs=plan==null?0:nativeWatch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"DRIFT_WHOLE_FIRST_PLAN goals={count};mode={mode};bounded={bounded};searchMs={searchMs:F3};nativeMs={nativeMs:F3};found={plan!=null};candidateQuestionComplete=false;examined={explorer.Examined};pruned={explorer.Progress.Pruned};boundStatesSeconds={explorer.BoundSeconds:F3};boundRejected={explorer.BoundRejected};directedRecipes={explorer.DirectedRecipes};directedWitnesses={explorer.DirectedWitnesses}");
            sweep.Add(new { count,mode,bounded,searchMs,nativeMs,found=plan!=null,examined=explorer.Examined,
                rerolls=plan?.RerollActions.OfType<RerollCardReward>().Count(),directedRecipes=explorer.DirectedRecipes,directedWitnesses=explorer.DirectedWitnesses,
                scope="first legal plan, not candidate exhaustion or global gold optimum" });
        }
        var cache=new PredictorCrystalReachability(source,true,includeRerolls:true);
        cache.AddWitnesses([new(nativePath,root.Rewards)]); // upgrade existing board witness once
        foreach(int count in new[]{1,2,3})
        {
            var watch=Stopwatch.StartNew();var goals=observedGoals.Take(count).ToImmutableArray();var projected=cache.ProjectKnown(goals);
            Check(projected.SelectedPlan!=null,"Known legal board gains reroll suffixes before retargeting");
            long replayed=cache.ProofReplays;cache.ProjectKnown(goals);
            Check(cache.ProofReplays==replayed,"Repeated known-board query does not replay any prefix or take");
            Console.WriteLine($"DRIFT_WHOLE_CACHED_RETARGET goals={count};available={projected.Available.Count};ms={watch.Elapsed.TotalMilliseconds:F3};proofReplays={replayed};repeatReplayDelta=0;positiveOnly=true");
        }
        ProductionSearchReplay.Write(Path.Combine(output,"frontier-whole-board.json"),sweep);
        ProductionSearchReplay.Write(Path.Combine(output,"frontier-whole-witnesses.json"),witnesses);
        foreach(bool bounded in new[]{false,true})
        {
            var timer=Stopwatch.StartNew();var explorer=new PredictorCrystalExplorer(partial,true,reachability:tableFull,
                selected:[targetFull],potionScenarioBound:true,targetDirected:true,stopAfterPlan:true,
                offsetBound:bounded?new(partial,tableFull,true):null);
            while(!explorer.Complete && timer.Elapsed<TimeSpan.FromSeconds(10)) explorer.Advance(TimeSpan.FromMilliseconds(30));
            var proof=explorer.QueryProjection.SelectedPlan;
            double searchMs=timer.Elapsed.TotalMilliseconds;
            Check(proof!=null,"Natural remaining-click search discovers a reroll-domain target");
            var fullSteps=nativePath.Take(5).ToImmutableArray().AddRange(proof!.Steps);
            var nativeWatch=Stopwatch.StartNew();
            checkNative(fullSteps,ReplayProof(source,proof with { Steps=fullSteps }));
            Console.WriteLine($"DRIFT_NATURAL_FRONTIER bounded={bounded};remaining={partial.Remaining};searchMs={searchMs:F3};nativeMs={nativeWatch.Elapsed.TotalMilliseconds:F3};examined={explorer.Examined};boundRejected={explorer.BoundRejected};rerolls={proof.RerollActions.OfType<RerollCardReward>().Count()};nativeSelectionParity=true");
        }
        var goalAnswer=SolveGoal(partial,tableFull,[targetFull],TimeSpan.FromSeconds(10));
        Check(goalAnswer.Plan!=null,"Independent goal-first solver continues a real partially revealed board");
        var goalSteps=nativePath.Take(5).ToImmutableArray().AddRange(goalAnswer.Plan!.Steps);
        checkNative(goalSteps,ReplayProof(source,goalAnswer.Plan with {Steps=goalSteps}));
        Console.WriteLine($"CRYSTAL_GOAL_FIRST_CONTINUATION_PASS remaining={partial.Remaining};ms={goalAnswer.Seconds*1000:F2};nativeParity=true");
        Console.WriteLine("DRIFT_FRONTIER_EXPERIMENT_PASS opt-in candidate universe / joint suffix offset proof / front pruning / immutable endpoint reuse; not installed or UI-ready");
        string GoalKey(CrystalRewardOption o)=>$"{o.Kind}:{o.Key.Serialized}:{o.UpgradeLevel}";
    }

    private static CrystalSuffixNode ReplayProof(PredictorCrystalSnapshot source,PredictorCrystalSolution proof)
    {
        var final=PredictorCrystalExplorer.Replay(source,proof.Steps);ImmutableArray<PredictorInput> actions=[];
        foreach(int index in proof.RerollRewardIndices)
        {
            var reward=final.Rewards[index];
            ImmutableArray<PredictorInput> inputs=[new EnterCardSelection(reward.Id),new RerollCardReward(reward.Id),new ExitCardSelection()];
            foreach(var action in inputs) Check(final.Submit(final.Request!,action)==PredictorInputResult.Accepted,"Search suffix replays under entry");
            actions=actions.AddRange(inputs);
        }
        foreach(var take in proof.Takes)
        {
            var reward=final.Rewards[take.RewardIndex];var card=reward.Cards.First(take.MatchesCard);
            var input=new PredictorInput[]{new EnterCardSelection(reward.Id),new TakeReward(reward.Id,card.Id)};
            foreach(var action in input) Check(final.Submit(final.Request!,action)==PredictorInputResult.Accepted,"Search take witness replays");
            actions=actions.AddRange(input);
        }
        return new(final,actions);
    }
}
