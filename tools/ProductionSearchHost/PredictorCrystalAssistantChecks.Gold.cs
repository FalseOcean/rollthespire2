using System.Collections.Immutable;
using System.Diagnostics;
using RolltheSpire2.Core.PredictorRuntime;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckNativeGoldBudgets(PredictorCrystalSnapshot source)
    {
        var card=source.Items.First(item=>item.X>=0 && item.Kind.StartsWith("CARD_",StringComparison.Ordinal));
        var path=(from x in Enumerable.Range(card.X,card.Width) from y in Enumerable.Range(card.Y,card.Height)
            where source.Hidden[x*11+y] select new PredictorCrystalStep(x,y,PredictorCrystalTool.Small)).ToImmutableArray();
        var partial=PredictorCrystalExplorer.Replay(source,path).ExportCrystal();
        foreach(int cell in Enumerable.Range(0,121).Where(i=>partial.Hidden[i] && partial.Occupancy[i]<0))
        { if(path.Length==source.Remaining) break;path=path.Add(new(cell/11,cell%11,PredictorCrystalTool.Small)); }
        Check(path.Length==source.Remaining,"Native budget fixture has a legal complete card witness with blank padding");
        var replay=PredictorCrystalExplorer.Replay(source,path);
        var workloads=new List<(string Name,ImmutableArray<CrystalRewardOption> Targets,CrystalOptionProjection? Known,int Budget)>
            { ("empty",[],null,5) };
        int choice=0;
        foreach(var offered in replay.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards.Take(3))
        {
            var target=CrystalRewardOption.ForCard(offered,false);
            var proof=PredictorCrystalExplorer.VerifySelection(source,new(path,replay.Rewards),[target])!;
            Check(proof!=null,"Native selected budget fixture uses an exact real card take");
            workloads.Add((choice++==0?"selected-one":"selected-one-choice"+choice,[target],new(
                ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,proof,0),10));
        }
        foreach(var workload in workloads)
        {
            var watch=Stopwatch.StartNew();
            var search=new PredictorCrystalGoldSearch(source,workload.Targets,true);
            if(!workload.Targets.IsEmpty)
                search.UseRewardTable(new CrystalReachabilityTable(source,includeRerolls:false,includeEnchantments:false));
            double setupSeconds=watch.Elapsed.TotalSeconds;
            var optimization=Stopwatch.StartNew();var result=search.Advance(TimeSpan.Zero,workload.Known);
            while(!result.GoldComplete && optimization.Elapsed<TimeSpan.FromSeconds(workload.Budget))
                result=search.Advance(TimeSpan.FromMilliseconds(75));
            Check(result.GoldUpperBound>=result.Gold,"Native bounded gold receipt has a sound ordering of incumbent and upper bound");
            if(result.SelectedPlan is { } found)
            {
                var actual=PredictorCrystalExplorer.Replay(source,found.Steps);
                Check(actual.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount)==result.Gold &&
                    (workload.Targets.IsEmpty || PredictorCrystalExplorer.VerifySelection(source,new(found.Steps,actual.Rewards),workload.Targets)!=null),
                    "Native board bounded gold guide is legal and preserves its selected targets");
            }
            Console.WriteLine($"CRYSTAL_GOLD_NATIVE_BUDGET case={workload.Name};target={string.Join('|',workload.Targets.Select(t=>t.Key.Entry))};seed={source.Context.Seed};remaining={source.Remaining};seconds={watch.Elapsed.TotalSeconds:F3};setupSeconds={setupSeconds:F3};optimizationSeconds={optimization.Elapsed.TotalSeconds:F3};rewardTable={!workload.Targets.IsEmpty};nodes={search.Progress.Examined};gold={result.Gold};upper={result.GoldUpperBound};complete={result.GoldComplete};witness={result.SelectedPlan!=null};scope=captured-native-board-bounded-predictor-replay");
        }
    }
    private static CrystalOptionProjection ImproveGoldForChecks(PredictorCrystalSnapshot source,CrystalOptionProjection known,
        ImmutableArray<CrystalRewardOption> selected,bool avoidCurse)
    {
        var search=new PredictorCrystalGoldSearch(source,selected,avoidCurse);
        return search.Advance(TimeSpan.FromSeconds(5),known);
    }
    // Tiny legal boards use the real typed reveal and reward/take inputs. The
    // oracle enumerates every currently hidden center and both tools, without
    // borrowing optimizer masks, bounds, callback memoization or reward tables.
    private static void CheckExactGold(PredictorCrystalSnapshot source)
    {
        PredictorCrystalSnapshot Board(int remaining, PredictorCrystalItem[] items, params (int X,int Y)[] hidden)
        {
            var fog=new bool[121];foreach(var cell in hidden) fog[cell.X*11+cell.Y]=true;
            var occupancy=Enumerable.Repeat(-1,121).ToArray();
            for(int i=0;i<items.Length;i++)
                if(items[i].X>=0)
                    for(int x=items[i].X;x<items[i].X+items[i].Width;x++)
                        for(int y=items[i].Y;y<items[i].Y+items[i].Height;y++)
                        { Check(occupancy[x*11+y]<0,"Gold fixture items do not overlap");occupancy[x*11+y]=i; }
            return source with { Remaining=remaining,Hidden=fog.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),
                Items=items.ToImmutableArray(),Revealed=[],State=source.State with { Relics=[],Position=source.State.Position with { Act=1 } } };
        }
        PredictorCrystalItem Item(string kind,int x,int y,int width=1,int height=1,int subscriptions=1)=>
            new(kind,width,height,x,y,subscriptions,false);
        int Gold(PredictorRun run)=>run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
        PredictorRun RawReplay(PredictorCrystalSnapshot board,ImmutableArray<PredictorCrystalStep> steps)
        {
            var run=PredictorRun.FromCrystal(board);
            foreach(var step in steps)
            {
                Check(run.Phase==PredictorPhase.EventChoice && run.ExportCrystal().Hidden[step.X*11+step.Y],
                    "Gold oracle and returned guide use currently hidden centers");
                Check(run.Submit(run.Request!,new SelectCrystalTool(step.Tool))==PredictorInputResult.Accepted &&
                    run.Submit(run.Request!,new RevealCrystalCell(step.X,step.Y))==PredictorInputResult.Accepted,
                    "Gold oracle typed inputs are accepted");
            }
            return run;
        }
        List<CrystalRewardRoute> Oracle(PredictorCrystalSnapshot board,bool avoid)
        {
            var rows=new List<CrystalRewardRoute>();
            void Visit(ImmutableArray<PredictorCrystalStep> path)
            {
                var run=RawReplay(board,path);
                // A genuinely empty endpoint closes the event and disposes its
                // RNG. One spare reveal keeps geometry observable independently.
                var current=(path.Length==board.Remaining?RawReplay(board with { Remaining=board.Remaining+1 },path):run).ExportCrystal();
                if(avoid && current.Items.Select((item,i)=>(item,i)).Any(p=>p.item.Kind=="CURSE" && p.item.Revealed && !board.Items[p.i].Revealed)) return;
                if(path.Length==board.Remaining)
                {
                    Check(path.Length==board.Remaining,"Gold oracle consumes the exact remaining reveal budget");
                    rows.Add(new(path,run.Rewards));return;
                }
                foreach(int cell in Enumerable.Range(0,121).Where(i=>current.Hidden[i]))
                    foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big})
                        Visit(path.Add(new(cell/11,cell%11,tool)));
            }
            Visit([]);return rows;
        }
        bool Compatible(PredictorCrystalSnapshot board,CrystalRewardRoute row,ImmutableArray<CrystalRewardOption> targets)=>
            targets.IsEmpty || PredictorCrystalExplorer.VerifySelection(board,row,targets)!=null;
        CrystalOptionProjection Finish(PredictorCrystalGoldSearch search,CrystalOptionProjection? known=null)
        {
            var watch=Stopwatch.StartNew();var result=search.Advance(TimeSpan.Zero,known);
            while(!result.GoldComplete && watch.Elapsed<TimeSpan.FromSeconds(5))
                result=search.Advance(TimeSpan.FromMilliseconds(10));
            Check(result.GoldComplete,"Tiny exact gold frontier closes within the focused check budget");return result;
        }
        CrystalOptionProjection Compare(string name,PredictorCrystalSnapshot board,bool avoid,ImmutableArray<CrystalRewardOption> targets,
            CrystalOptionProjection? known=null)
        {
            var raw=Oracle(board,avoid);var compatible=raw.Where(row=>Compatible(board,row,targets)).ToArray();
            int expected=compatible.Length==0?0:compatible.Max(row=>row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount));
            var search=new PredictorCrystalGoldSearch(board,targets,avoid);
            var early=search.Advance(TimeSpan.Zero,known);
            Check(early.GoldUpperBound is { } bound && bound>=expected,"Gold upper bound covers all independently exhaustive legal paths: "+name);
            if(early.SelectedPlan==null && raw.Count>0) Check(!early.GoldComplete,"A zero-work stop cannot claim a witnessed optimum: "+name);
            var result=Finish(search);
            Check(result.Gold==expected && (result.SelectedPlan!=null)==(compatible.Length>0),"Exact maximum and witness match raw legal paths: "+name);
            Check(result.GoldUpperBound==expected,"Closed exact frontier publishes a tight bound: "+name);
            if(result.SelectedPlan is { } plan)
            {
                Check(plan.Steps.Length==board.Remaining,"Gold witness consumes the exact remaining budget: "+name);
                var run=RawReplay(board,plan.Steps);
                Check(Gold(run)==expected && Compatible(board,new(plan.Steps,run.Rewards),targets),"Gold witness preserves every exact selected target: "+name);
                var geometry=RawReplay(board with { Remaining=board.Remaining+1 },plan.Steps).ExportCrystal();
                Check(!avoid || !geometry.Items.Select((item,i)=>(item,i)).Any(p=>p.item.Kind=="CURSE" && p.item.Revealed && !board.Items[p.i].Revealed),
                    "Gold witness respects new-curse avoidance: "+name);
            }
            Console.WriteLine($"CRYSTAL_GOLD_ORACLE_PASS case={name};rawPaths={raw.Count};compatible={compatible.Length};gold={expected};complete={result.GoldComplete};upper={result.GoldUpperBound}");
            return result;
        }
        void CompareGoldLanguage(string name,PredictorCrystalSnapshot board,ImmutableArray<CrystalRewardOption> targets,params int[] thresholds)
        {
            var raw=Oracle(board,true);var compatible=raw.Where(row=>Compatible(board,row,targets)).ToArray();
            var table=new CrystalReachabilityTable(board);
            var owner=new PredictorCrystalOffsetBound(board,table,true);
            foreach(int minimum in thresholds)
            {
                bool expected=compatible.Any(row=>row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount)>=minimum);
                var result=PredictorCrystalGoalLanguage.Solve(board,table,targets,true,TimeSpan.FromSeconds(5),sharedBound:owner,minimumGold:minimum);
                Check(result.Verdict!=CrystalOffsetVerdict.Incomplete && (result.Plan!=null)==expected &&
                    (result.Verdict==CrystalOffsetVerdict.Possible)==expected,"Gold threshold language agrees with independent raw paths: "+name+"/"+minimum);
                if(result.Plan is { } plan)
                {
                    var run=RawReplay(board,plan.Steps);
                    Check(Gold(run)>=minimum && Compatible(board,new(plan.Steps,run.Rewards),targets),
                        "Gold threshold language proof retains actual gold and ordered targets: "+name);
                }
                // Threshold-specific absence cannot retire the original targets
                // or contaminate a weaker reward threshold on the frozen owner.
                if(minimum>0 && !expected)
                {
                    Check(owner.KnownLanguageNegative(targets)==CrystalLanguageNegativeKind.None,
                        "Gold-threshold negative never becomes a target-only negative: "+name);
                    var zero=PredictorCrystalGoalLanguage.Solve(board,table,targets,true,TimeSpan.FromSeconds(5),sharedBound:owner);
                    Check((zero.Plan!=null)==(compatible.Length>0) && !zero.NegativeCacheHit,
                        "Removing a gold threshold restores the original supported target query: "+name);
                }
                Console.WriteLine($"CRYSTAL_GOLD_LANGUAGE_ORACLE_PASS case={name};minimum={minimum};rawPaths={raw.Count};compatible={compatible.Length};verdict={result.Verdict};cacheHit={result.NegativeCacheHit}");
            }
            var fresh=new PredictorCrystalOffsetBound(board,table,true);
            int positive=thresholds.FirstOrDefault(n=>n>0);
            var stopped=PredictorCrystalGoalLanguage.Solve(board,table,targets,true,TimeSpan.Zero,sharedBound:fresh,minimumGold:positive);
            Check(stopped.Verdict==CrystalOffsetVerdict.Incomplete && !stopped.NegativeCacheHit,
                "Zero-time threshold compilation remains unknown: "+name);
            var limited=PredictorCrystalGoalLanguage.Solve(board,table,targets,true,TimeSpan.FromSeconds(1),stateLimit:0,sharedBound:fresh,minimumGold:positive);
            Check(limited.Verdict==CrystalOffsetVerdict.Incomplete && fresh.KnownLanguageNegative(targets)==CrystalLanguageNegativeKind.None,
                "Threshold state cutoff cannot acquire target-negative authority: "+name);
        }

        var blank=Board(2,[],(0,0),(0,1),(10,10));
        var blankPlan=Compare("zero-target-zero-gold",blank,true,[]).SelectedPlan!;
        var blankGuide=PredictorCrystalRewardGuide.BuildRerollGuide(blank,blankPlan);
        Check(blankGuide.Actions.IsEmpty && blankGuide.Frames.IsEmpty,"Zero-gold legal board plan has an empty executable reward guide");
        bool malformedSuffix=false;
        try { PredictorCrystalRewardGuide.BuildRerollGuide(blank,blankPlan with { RerollRewardIndices=[0] }); }
        catch(InvalidOperationException) { malformedSuffix=true; }
        Check(malformedSuffix,"An empty reward picker cannot accept a fabricated reroll suffix");
        var curse=Board(1,[Item("GOLD_SMALL",5,5),Item("CURSE",5,6),Item("GOLD_BIG",6,5)],(5,5),(5,6),(6,5));
        Check(Compare("curse-avoided-small-required",curse,true,[]).Gold==30,"Avoidance retains independent larger gold");
        Check(Compare("curse-permitted-big",curse,false,[]).Gold==40,"Permitted curse changes the legal maximum");
        var partial=Board(1,[Item("GOLD_BIG",4,4,2),Item("GOLD_SMALL",9,9)],(4,4),(5,4),(9,9));
        var incomplete=RawReplay(partial,[new(4,4,PredictorCrystalTool.Small)]);
        Check(Gold(incomplete)==0 && !RawReplay(partial with { Remaining=2 },[new(4,4,PredictorCrystalTool.Small)]).ExportCrystal().Items[0].Revealed,
            "Partially uncovered gold earns no callback");
        Compare("incomplete-gold-item",partial,true,[]);
        var centers=Board(2,[Item("GOLD_BIG",0,0,1,2)],(0,0),(0,1));
        var centersResult=Compare("hidden-center-exact-budget",centers,true,[]);
        Check(centersResult.SelectedPlan!.Steps[0].Tool==PredictorCrystalTool.Small,"Big first would destroy all legal second centers");
        Check(Compare("repeated-gold-subscriptions",Board(1,[Item("GOLD_BIG",5,5,2,1,2)],(5,5),(6,5)),true,[]).Gold==60,
            "Gold callbacks honor repeated subscriptions");
        Compare("zero-subscription",Board(1,[Item("GOLD_BIG",5,5,2,1,0)],(5,5),(6,5)),true,[]);
        Compare("unplaced-gold",Board(1,[Item("GOLD_BIG",-1,-1,2)],(5,5)),true,[]);
        var midas=partial with { Context=partial.Context with { Modifiers=[new(new("MODIFIER","MIDAS"))] } };
        Check(Compare("midas-gold-modifier",midas,true,[]).Gold==60,"Late Midas reward modifier participates in exact gold and its bound");
        Compare("no-legal-complete-guide",Board(3,[],(0,0),(0,1)),true,[]);

        var continuation=Board(2,[Item("GOLD_SMALL",0,0,1,1,2) with { Revealed=true },Item("GOLD_BIG",8,8,2)],(8,8),(9,8),(10,0)) with { Revealed=[0,0] };
        var continuedRng=continuation.EventRng.Restore();continuedRng.Advance(7);
        continuation=continuation with { EventRng=PredictorStreamState.Capture(PredictorStream.Rewards,continuedRng) };
        Check(Compare("already-revealed-continuation",continuation,true,[]).Gold==50,"Continuation counts existing callbacks once and adds remaining gold");
        var initialCurse=Board(1,[Item("CURSE",0,0) with { Revealed=true },Item("GOLD_SMALL",8,8)],(8,8)) with { Revealed=[0] };
        Compare("existing-curse-is-not-new",initialCurse,true,[]);

        // The richer optimum changes the first productive click AND the reward
        // callback prefix. Tail polishing / same-word substitution cannot prove it.
        var prefixBase=Board(2,[Item("CARD_COMMON",0,0,2,2),Item("GOLD_BIG",0,2),Item("GOLD_SMALL",8,8)],(0,0),(0,1),(0,2),(8,8));
        ImmutableArray<PredictorCrystalStep> low=[new(0,0,PredictorCrystalTool.Big),new(8,8,PredictorCrystalTool.Small)];
        ImmutableArray<PredictorCrystalStep> high=[new(0,1,PredictorCrystalTool.Big),new(8,8,PredictorCrystalTool.Small)];
        var prefix=prefixBase;CrystalRewardOption? target=null;
        for(int shift=0;shift<64 && target==null;shift++)
        {
            var rng=prefixBase.EventRng.Restore();rng.Advance(shift);
            prefix=prefixBase with { EventRng=PredictorStreamState.Capture(PredictorStream.Rewards,rng) };
            var first=RawReplay(prefix,low);var richer=RawReplay(prefix,high);
            target=PredictorCrystalExplorer.Options(new(low,first.Rewards)).FirstOrDefault(o=>o.Kind==PredictorRewardKind.Card &&
                PredictorCrystalExplorer.VerifySelection(prefix,new(high,richer.Rewards),[o])!=null);
        }
        Check(target!=null,"Bounded fixture has one exact real card shared across distinct callback prefixes");
        var lowRun=RawReplay(prefix,low);var lowProof=PredictorCrystalExplorer.VerifySelection(prefix,new(low,lowRun.Rewards),[target!])!;
        var known=new CrystalOptionProjection(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,lowProof,10);
        var prefixResult=Compare("target-compatible-different-prefix",prefix,true,[target!],known);
        Check(prefixResult.Gold==40 && prefixResult.SelectedPlan!.Steps[0]!=low[0],"Exact global maximum replaces the earlier target witness prefix");
        CompareGoldLanguage("target-changed-prefix",prefix,[target!],0,10,30,40,41);
        foreach(var option in Oracle(prefix,true).SelectMany(PredictorCrystalExplorer.Options).Distinct().Where(o=>o.Kind==PredictorRewardKind.Card))
            Compare("callback-sensitive-card-"+option.Key.Entry,prefix,true,[option]);
        var doubled=prefix with { Items=prefix.Items.SetItem(0,prefix.Items[0] with { Subscriptions=2 }) };
        var doubleRun=RawReplay(doubled,high);
        var doubleTargets=doubleRun.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).Select(r=>CrystalRewardOption.ForCard(r.Cards[0],false)).ToImmutableArray();
        Check(doubleTargets.Length==2,"Repeated card callbacks create two actual take slots");
        Compare("repeated-card-callback-takes",doubled,true,doubleTargets);
        CompareGoldLanguage("repeated-card-take-slots",doubled,doubleTargets,0,10,30,40,41);
        var repeatedGold=prefix with { Items=prefix.Items.SetItem(1,prefix.Items[1] with { Subscriptions=2 }) };
        var repeatedRun=RawReplay(repeatedGold,high);
        var repeatedTarget=CrystalRewardOption.ForCard(repeatedRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0],false);
        CompareGoldLanguage("repeated-gold-callback-threshold",repeatedGold,[repeatedTarget],0,30,60,70,71);
        var fixedGold=prefix with { Items=prefix.Items.Add(Item("GOLD_SMALL",10,10,1,1,2) with { Revealed=true }),
            Occupancy=prefix.Occupancy.SetItem(120,3),Revealed=[3,3] };
        var fixedRun=RawReplay(fixedGold,high);
        var fixedTarget=CrystalRewardOption.ForCard(fixedRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0],false);
        CompareGoldLanguage("fixed-revealed-gold-threshold",fixedGold,[fixedTarget],0,20,50,60,61);
        var midasPrefix=prefix with { Context=prefix.Context with { Modifiers=[new(new("MODIFIER","MIDAS"))] } };
        var midasRun=RawReplay(midasPrefix,high);
        var midasTarget=CrystalRewardOption.ForCard(midasRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0],false);
        CompareGoldLanguage("midas-target-gold-threshold",midasPrefix,[midasTarget],0,20,60,80,81);
        var separated=Board(1,[Item("CARD_COMMON",0,0,2,2),Item("GOLD_BIG",8,8,2)],(0,0),(8,8),(9,8));
        var separateRun=RawReplay(separated,[new(0,0,PredictorCrystalTool.Small)]);
        var separateTarget=CrystalRewardOption.ForCard(separateRun.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0],false);
        CompareGoldLanguage("gold-and-target-joint-cover-impossible",separated,[separateTarget],0,10,30);
        var shifted=Board(1,[Item("CARD_RARE",4,5,2,2),Item("GOLD_SMALL",6,8)],(5,6),(6,7),(6,8));
        var shiftedBase=shifted;CrystalRewardOption? lostGoal=null;
        for(int shift=0;shift<64 && lostGoal==null;shift++)
        {
            var rng=shiftedBase.EventRng.Restore();rng.Advance(shift);
            shifted=shiftedBase with { EventRng=PredictorStreamState.Capture(PredictorStream.Rewards,rng) };
            var noGold=RawReplay(shifted,[new(5,6,PredictorCrystalTool.Small)]);
            var money=RawReplay(shifted,[new(6,7,PredictorCrystalTool.Big)]);
            lostGoal=PredictorCrystalExplorer.Options(new([],noGold.Rewards)).FirstOrDefault(o=>o.Kind==PredictorRewardKind.Card &&
                !PredictorCrystalExplorer.Options(new([],money.Rewards)).Any(o.Accepts));
        }
        Check(lostGoal!=null,"Gold-first callback shifts a real rare-card target out of the maximum-gold offer");
        CompareGoldLanguage("gold-callback-target-conflict",shifted,[lostGoal!],0,10,11);

        var paused=new PredictorCrystalGoldSearch(prefix,[target!],true);
        var cutoff=paused.Advance(TimeSpan.Zero,known);
        Check(cutoff.SelectedPlan!=null && cutoff.Gold==10 && !cutoff.GoldComplete && cutoff.GoldUpperBound>=40,
            "Cutoff retains a positive guide without treating its gold as optimum");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();bool caught=false;
            try { paused.Advance(TimeSpan.FromSeconds(1),token:cancelled.Token); } catch(OperationCanceledException) { caught=true; }
            Check(caught,"Gold cancellation remains explicit");
        }
        Check(Finish(paused).Gold==40,"Gold frontier resumes after cancellation without losing work or its bound");
        var session=new PredictorCrystalReachability(prefix,true);
        session.AddWitnesses([new(low,lowRun.Rewards)]);
        session.ProjectKnown([target!]);
        var candidates=session.Advance([target!],TimeSpan.Zero);
        var sessionCutoff=session.AdvanceGold([target!],TimeSpan.Zero);
        Check(!sessionCutoff.GoldComplete && sessionCutoff.Gold==10,"Candidate witness and gold proof completion remain separate");
        var sessionWatch=Stopwatch.StartNew();var published=sessionCutoff;
        while((!published.GoldComplete || !candidates.Complete) && sessionWatch.Elapsed<TimeSpan.FromSeconds(5))
        {
            if(!published.GoldComplete) published=session.AdvanceGold([target!],TimeSpan.FromMilliseconds(10));
            if(!candidates.Complete) candidates=session.Advance([target!],TimeSpan.FromMilliseconds(10));
        }
        Check(published.GoldComplete && published.Gold==40 && session.PeekKnown([target!]).GoldComplete &&
            session.ProjectKnown([target!]).Gold==40,"Reachability publishes and retains the exact gold receipt");
        Check(candidates.Complete && session.ProjectKnown([target!]).GoldComplete && session.GoldProgress.Examined>0,
            "Production candidate-table handoff and sliced gold optimization both close without erasing the receipt");
        Console.WriteLine($"CRYSTAL_GOLD_SESSION_PASS candidatesComplete={candidates.Complete};goldComplete={published.GoldComplete};gold={published.Gold};seconds={sessionWatch.Elapsed.TotalSeconds:F3};goldNodes={session.GoldProgress.Examined}");
        var rawPrefix=Oracle(prefix,true);
        var belowAllGold=rawPrefix.SelectMany(PredictorCrystalExplorer.Options).Distinct().Where(o=>o.Kind==PredictorRewardKind.Card)
            .Select(o=>(Option:o,Rows:rawPrefix.Where(row=>Compatible(prefix,row,[o])).ToArray()))
            .Where(p=>p.Rows.Length>0)
            .Select(p=>(p.Option,p.Rows,Gold:p.Rows.Max(row=>row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount))))
            .FirstOrDefault(p=>p.Gold==30);
        Check(belowAllGold.Option!=null,"Raw callback-sensitive fixture has a target with maximum thirty below physical forty");
        var thirtyRow=belowAllGold.Rows.First(row=>row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount)==30);
        var belowSession=new PredictorCrystalReachability(prefix,true);
        belowSession.AddWitnesses([thirtyRow]);belowSession.ProjectKnown([belowAllGold.Option!]);
        belowSession.Advance([belowAllGold.Option!],TimeSpan.Zero);
        var below=belowSession.AdvanceGold([belowAllGold.Option!],TimeSpan.Zero);
        Check(!below.GoldComplete && below.Gold==30 && below.GoldUpperBound==40,
            "Seeded target maximum begins below a genuinely loose forty-gold bound");
        var belowWatch=Stopwatch.StartNew();
        while(!below.GoldComplete && belowWatch.Elapsed<TimeSpan.FromSeconds(5))
            below=belowSession.AdvanceGold([belowAllGold.Option!],TimeSpan.FromMilliseconds(10));
        Check(below.GoldComplete && below.Gold==30 && below.GoldUpperBound==30,
            "Async threshold absence closes the lower target-compatible optimum and tightens its bound");
        var goldQueries=(Dictionary<string,PredictorCrystalGoldSearch>)typeof(PredictorCrystalReachability)
            .GetField("_goldQueries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(belowSession)!;
        int tightened=(int)typeof(PredictorCrystalGoldSearch).GetField("_upper",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
            .GetValue(goldQueries.Values.Single())!;
        Check(tightened==30,"Threshold lane consumed its completed negative instead of merely exhausting raw geometry");
        var relaxed=belowSession.Advance([],TimeSpan.Zero);
        var focus=belowSession.AdvanceFocused([],belowAllGold.Option!,TimeSpan.FromSeconds(1));
        var reselected=belowSession.Advance([belowAllGold.Option!],TimeSpan.Zero);
        Check(!relaxed.ExcludedCandidates.Contains(belowAllGold.Option!) && focus.Verdict==CrystalFocusVerdict.Reachable &&
            reselected.Projection.SelectedPlan!=null && reselected.Projection.GoldComplete && reselected.Projection.Gold==30,
            "Threshold negative leaves target-only focus possible and retargeting retains its exact receipt");
        Console.WriteLine($"CRYSTAL_GOLD_NEGATIVE_SESSION_PASS target={belowAllGold.Option!.Key.Entry};initialUpper=40;gold=30;upper={below.GoldUpperBound};complete={below.GoldComplete};targetOnly={focus.Verdict};seconds={belowWatch.Elapsed.TotalSeconds:F3}");
        var noTargets=session.AdvanceGold([],TimeSpan.Zero);
        Check(!noTargets.GoldComplete || noTargets.Gold==40,"A target change cannot inherit a wrong gold proof");
        Console.WriteLine("CRYSTAL_GOLD_EXACT_PASS rawHiddenCentersAndBothTools=true;incompleteItems=true;curse=true;subscriptions=true;continuation=true;changedPrefix=true;cutoffAndResume=true;zeroTargetGuide=true");
    }
}
