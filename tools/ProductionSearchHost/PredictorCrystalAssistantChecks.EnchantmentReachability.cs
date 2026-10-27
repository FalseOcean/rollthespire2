using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static bool EnchantmentChecksEnabled=>Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_EXPERIMENT")=="1" ||
        Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_REACHABILITY")=="1";

    // Explicit opt-in: small complete action domains establish negatives. The
    // native boards below establish positives only, using original legal paths.
    private static int CheckEnchantmentReachability(string output,Func<string,string,PredictorCrystalSnapshot> create,
        Action<PredictorCrystalSnapshot,ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode,string,bool> native)
    {
        const string ordered="FRESNEL_LENS+WING_CHARM+GLITTER";
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_TOGGLE")=="1")
        {
            var toggleSource=create("000000000000",ordered);
            var disabledTable=new CrystalReachabilityTable(toggleSource,includeRerolls:true,includeEnchantments:false);
            Check(!disabledTable.IncludesEnchantments && disabledTable.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card).All(c=>c.Enchantment==null),
                "Disabled enchantment search retains card identities and upgrade levels as wildcard candidates");
            var lazy=typeof(CrystalReachabilityTable).GetField("_enchantmentRows",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(disabledTable)!;
            Check(!(bool)lazy.GetType().GetProperty("IsValueCreated")!.GetValue(lazy)!,"Disabled search does not eagerly build Niche/enchantment layers");
            var enabledTable=new CrystalReachabilityTable(toggleSource,includeRerolls:true,includeEnchantments:true);
            Check(enabledTable.IncludesEnchantments && enabledTable.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card).All(c=>c.Enchantment!=null),
                "Enabled search publishes exact enchantment variants");
            var toggleReceipts=new List<object>();
            foreach(bool enabled in new[]{false,true})
                toggleReceipts.Add(new {enabled,oracle=CheckEnchantmentRawBoard(toggleSource,"ordered-pair",goalLanguageProof:true,includeEnchantments:enabled)});
            ProductionSearchReplay.Write(Path.Combine(output,"enchantment-toggle.json"),toggleReceipts);
            Console.WriteLine("CRYSTAL_ENCHANTMENT_TOGGLE_PASS enabledAndDisabled=true;rawProjection=true;sourcePreserved=true");
            return 0;
        }
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_CONTROLS")=="1")
            return CheckEnchantmentControlBoards(output,create);
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_RANDOM_RELIC_SURVEY")=="1")
            return CheckRandomRelicInventories(output,create,native);
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_EXTREME_RELIC_FOCUS")=="1")
            return CheckExtremeRelicInventories(output,create,native);
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_QUERY_SURVEY")=="1")
            return CheckEnchantmentProductQuerySurvey(output,create,ordered);
        var source=create("000000000000",ordered);
        var original=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
        var observed=PredictorCrystalExplorer.Replay(source,CardPath(source));
        CheckEnchantmentTargetContract(observed.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0]);
        var receipts=new List<object>();
        foreach(string shape in new[]{"single","repeat","ordered-pair"})
            receipts.Add(CheckEnchantmentRawBoard(source,shape));
        Check(original==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Bounded enchantment oracle preserves frozen native source");
        ProductionSearchReplay.Write(Path.Combine(output,"enchantment-reachability-raw.json"),receipts);
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_RAW_ONLY")=="1")
        {
            Console.WriteLine("CRYSTAL_ENCHANTMENT_REACHABILITY_RAW_PASS completeSmallDomains=true;nativeWholeBoardQueries=false");
            return 0;
        }

        var naturalReceipts=new List<object>();int jointQueries=0;
        foreach(string seed in new[]{"000000000000","9TW7Y8N6663H"})
        foreach(string order in new[]{ordered,"WING_CHARM+FRESNEL_LENS+GLITTER","GLITTER+WING_CHARM"})
        {
            var board=create(seed,order);string frozen=JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions);
            var path=CardPath(board);var root=PredictorCrystalExplorer.Replay(board,path);
            var prefixes=PredictorCrystalRerolls.Prefixes(root).ToArray();
            var exact=prefixes.SelectMany(p=>p.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)
                .SelectMany(r=>r.Cards.Select(c=>CrystalRewardOption.ForCard(c,true)))).Distinct().ToArray();
            int collapsed=exact.GroupBy(o=>(o.Key,o.UpgradeLevel)).Count(g=>g.Select(o=>o.Enchantment).Distinct().Count()>1);
            // These are counterfactual ordered inventories, not acquisition claims.
            // All six original fixtures are checked at a legal path; only two
            // receive whole-board joint language queries.
            var rerolled=prefixes.OrderByDescending(p=>p.Actions.Length).First();
            var picks=rerolled.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).Take(2)
                .Select(r=>CrystalRewardOption.ForCard(r.Cards[0],true)).ToImmutableArray();
            var taken=PredictorCrystalRerolls.Take(rerolled,picks);
            Check(taken!=null,"Observed exact enchanted pair has a typed take witness");
            Check(EnchantmentObtainedSupports(root,taken!.Run,picks),"Exact enchanted pair survives actual obtain hooks");
            native(board,path,taken,order,true);
            object? query=null;
            if(order==ordered)
            {
                var preparation=Stopwatch.StartNew();var table=new CrystalReachabilityTable(board,includeRerolls:true);
                double tableMs=preparation.Elapsed.TotalMilliseconds;
                var bound=new PredictorCrystalOffsetBound(board,table,true);
                var answer=PredictorCrystalGoalLanguage.Solve(board,table,picks,true,TimeSpan.FromSeconds(10),sharedBound:bound);
                Check(answer.Plan!=null,"Whole-board language finds a jointly witnessed exact enchantment pair");
                var replayed=ReplayProof(board,answer.Plan!);
                Check(EnchantmentObtainedSupports(PredictorCrystalExplorer.Replay(board,answer.Plan!.Steps),replayed.Run,picks),
                    "Whole-board exact pair replay obtains requested enchantments and amounts");
                native(board,answer.Plan!.Steps,replayed,order,true);jointQueries++;
                query=new {selected=picks,tableMs,answer,scope="Whole-board first positive, no candidate completeness or gold optimum claim"};
                Console.WriteLine($"CRYSTAL_ENCHANTMENT_JOINT seed={seed};goals={picks.Length};tableMs={tableMs:F3};queryMs={answer.Seconds*1000:F3};verdict={answer.Verdict};nativeExact=true;wholeBoardNegativeClaim=false");
            }
            Check(frozen==JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions),"Native enchantment joint check preserves source");
            naturalReceipts.Add(new {seed,order,path,exactVariants=exact.Length,identityLevelWithMultipleEnchantments=collapsed,
                selected=picks,query,scope="Original native fixture with known legal CardPath; counterfactual ordered inventory"});
        }
        ProductionSearchReplay.Write(Path.Combine(output,"enchantment-reachability-native.json"),naturalReceipts);
        Console.WriteLine($"CRYSTAL_ENCHANTMENT_REACHABILITY_PASS completeSmallDomains=3;nativeFixtures=6;wholeBoardJointPositives={jointQueries};nativeExact=true;noWholeBoardNegativeClaim=true");
        return 0;
    }

    private static int CheckEnchantmentProductQuerySurvey(string output,Func<string,string,PredictorCrystalSnapshot> create,string order)
    {
        var receipts=new List<object>();
        bool diagnose=Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_QUERY_DIAGNOSTICS")=="1";
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        // Diagnostics read the actual production table, avoiding a second cold
        // construction before measurement. No solver state is modified here.
        var tableField=typeof(PredictorCrystalReachability).GetField("_table",fields)!;
        var activeField=typeof(PredictorCrystalReachability).GetField("_activeLanguage",fields)!;
        foreach(string seed in new[]{"000000000000","9TW7Y8N6663H"})
        {
            var source=create(seed,order);string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
            var preparation=Stopwatch.StartNew();var path=CardPath(source);var root=PredictorCrystalExplorer.Replay(source,path);
            var row=PredictorCrystalExplorer.WithRerolls(new(path,root.Rewards){EnchantmentTargets=true},root);
            var session=new PredictorCrystalReachability(source,true,includeRerolls:true,goalLanguageProof:true);
            bool compilationProfile=Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_COMPILATION_PROFILE")=="1";
            typeof(PredictorCrystalReachability).GetProperty("ProfileLanguageCompilation",fields)?.SetValue(session,compilationProfile);
            Check(!compilationProfile || typeof(PredictorCrystalReachability).GetProperty("ProfileLanguageCompilation",fields)!=null,
                "Explicit compilation profile requires its diagnostic Core seam");
            var costs=new ConcurrentQueue<EnchantmentQueryCost>();
            if(diagnose) session.LanguageResultObserved=(selection,target,result)=>costs.Enqueue(new(
                result.Seconds*1000,selection.Select(g=>g.CacheKey).ToImmutableArray(),target?.CacheKey,result));
            session.AddWitnesses([row]);double knownRowPreparationMs=preparation.Elapsed.TotalMilliseconds;
            ImmutableArray<CrystalRewardOption> targets=seed=="000000000000"
                ?[new(PredictorRewardKind.Card,new("CARD","INFERNAL_BLADE"),1,new(new("ENCHANTMENT","GLAM"),1)),
                  new(PredictorRewardKind.Card,new("CARD","SETUP_STRIKE"),1,new(new("ENCHANTMENT","GLAM"),1))]
                :[new(PredictorRewardKind.Card,new("CARD","SECOND_WIND"),0,new(new("ENCHANTMENT","NIMBLE"),2)),
                  new(PredictorRewardKind.Card,new("CARD","PERFECTED_STRIKE"),0,new(new("ENCHANTMENT","SWIFT"),1))];
            foreach(int count in new[]{1,2})
            {
                var selected=targets.Take(count).ToImmutableArray();var cacheTimer=Stopwatch.StartNew();
                var known=session.ProjectKnown(selected);double knownProjectionMs=cacheTimer.Elapsed.TotalMilliseconds;
                Check(known.SelectedPlan!=null,"Survey uses the previously native-confirmed exact target and known CardPath");
                costs.Clear();long proofReplaysBefore=session.ProofReplays;
                var boundBefore=EnchantmentBoundCounters(session);
                using var stop=new CancellationTokenSource();var timer=Stopwatch.StartNew();
                var update=session.Advance(selected,TimeSpan.FromMilliseconds(100),stop.Token);
                double firstAdvanceMs=timer.Elapsed.TotalMilliseconds;
                while(!update.Complete && timer.Elapsed<TimeSpan.FromSeconds(10))
                {
                    var remaining=TimeSpan.FromSeconds(10)-timer.Elapsed;
                    update=session.Advance(selected,remaining<TimeSpan.FromMilliseconds(100)?remaining:TimeSpan.FromMilliseconds(100),stop.Token);
                }
                double queryMs=timer.Elapsed.TotalMilliseconds;
                var table=(CrystalReachabilityTable)tableField.GetValue(session)!;
                var candidates=table.Candidates.Where(c=>!selected.Contains(c)).ToArray();
                var pending=candidates.Where(c=>!update.Projection.Available.ContainsKey(c) && !update.ExcludedCandidates.Contains(c)).ToArray();
                Check(update.Projection.SelectedPlan!=null,"Survey retains native-confirmed selected witness throughout product query");
                Check(!update.Complete || pending.Length==0,"Complete product query leaves no unresolved actual table candidate");
                // A timed-out queue must stop before the next measured question.
                // Retargeting itself has the same cancellation behavior in UI.
                var active=activeField.GetValue(session);
                var worker=active?.GetType().GetField("Worker",fields)?.GetValue(active) as Task;
                stop.Cancel();
                if(worker!=null)
                    try {Check(worker.Wait(TimeSpan.FromSeconds(5)),"Survey worker exits after cancellation");}
                    catch(AggregateException e) when(e.InnerExceptions.All(x=>x is OperationCanceledException)) { }
                var solveCosts=costs.ToArray();
                var focus=typeof(PredictorCrystalReachability).GetField("_focus",fields)!.GetValue(session);
                double fallbackSeconds=focus==null?0:(double)focus.GetType().GetField("Seconds",fields)!.GetValue(focus)!;
                var boundAfter=EnchantmentBoundCounters(session);
                var receipt=new {seed,order,selected,goalKeys=selected.Select(g=>g.CacheKey).ToArray(),coldTable=count==1,
                    knownRowPreparationMs,knownProjectionMs,knownPositive=known.Available.Count,firstAdvanceMs,queryMs,
                    queryIncludesColdTableAndBound=count==1,compilationProfile,complete=update.Complete,positive=update.Projection.Available.Count,
                    negative=update.ExcludedCandidates.Length,pending=pending.Length,pendingKeys=pending.Select(g=>g.CacheKey).ToArray(),
                    selectedWitness=update.Projection.SelectedPlan!=null,update.Progress,update.ProbeSlices,
                    solveMs=solveCosts.Sum(c=>c.Milliseconds),solveCompileMs=solveCosts.Sum(c=>c.Result.CompileSeconds*1000),solveCosts,
                    fallbackSeconds,proofReplays=session.ProofReplays-proofReplaysBefore,
                    coverSearchNodes=boundAfter.CoverSearchNodes-boundBefore.CoverSearchNodes,
                    packingChecks=boundAfter.PackingChecks-boundBefore.PackingChecks,
                    packingRejected=boundAfter.PackingRejected-boundBefore.PackingRejected,
                    scope="Production candidate query after explicitly preseeded legal CardPath and all reroll variants; not cold UI first discovery; 10s per question"};
                receipts.Add(receipt);
                ProductionSearchReplay.Write(Path.Combine(output,"enchantment-product-query-survey.json"),receipts);
                Console.WriteLine($"CRYSTAL_ENCHANTMENT_PRODUCT_QUERY seed={seed};selected={count};coldTable={count==1};knownRowPrepMs={knownRowPreparationMs:F3};knownProjectionMs={knownProjectionMs:F3};knownPositive={known.Available.Count};firstAdvanceMs={firstAdvanceMs:F3};queryMs={queryMs:F3};complete={update.Complete};positive={update.Projection.Available.Count};negative={update.ExcludedCandidates.Length};pending={pending.Length};selectedWitness={update.Projection.SelectedPlan!=null};goalKeys={string.Join('|',selected.Select(g=>g.CacheKey))};knownPathSeeded=true");
            }
            Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Product query survey preserves frozen source");
        }
        Console.WriteLine("CRYSTAL_ENCHANTMENT_PRODUCT_QUERY_SURVEY_PASS questions=4;knownLegalPathSeeded=true;incompleteIsReported=true");
        return 0;
    }

    private sealed record EnchantmentQueryCost(double Milliseconds,ImmutableArray<string> GoalKeys,string? TargetKey,
        CrystalGoalSearchResult Result);

    private static (long CoverSearchNodes,long PackingChecks,long PackingRejected) EnchantmentBoundCounters(PredictorCrystalReachability session)
    {
        var bound=(PredictorCrystalOffsetBound?)typeof(PredictorCrystalReachability)
            .GetField("_offsetBound",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session);
        return bound==null?(0,0,0):(bound.CoverSearchNodes,bound.PackingChecks,bound.PackingRejected);
    }

    private static int CheckEnchantmentControlBoards(string output,Func<string,string,PredictorCrystalSnapshot> create)
    {
        var receipts=new List<object>();
        bool language=Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_CONTROL_LANGUAGE")=="1";
        string? requested=Environment.GetEnvironmentVariable("RT2_CRYSTAL_ENCHANTMENT_CONTROL_CASE");
        foreach(var (name,order,shape,scope) in new[]{
            ("fresnel","FRESNEL_LENS","single","No Wing; exact-none and deterministic Nimble targets"),
            ("suppressed-wing","GLITTER+WING_CHARM","repeat","Repeated subscriptions; Glitter suppresses Wing Niche draws"),
            ("ordinary","","single","Ordinary inventory; published candidates retain wildcard enchantments")})
        {
            if(requested!=null && requested!=name) continue;
            var source=create("000000000000",order);
            var root=PredictorCrystalExplorer.Replay(source,CardPath(source));
            Check(PredictorCrystalRerolls.Prefixes(root).All(p=>Niche(p.Run.Working)==Niche(source.State)),
                "Deterministic control inventory has no Niche continuation draws: "+order);
            receipts.Add(new {name,order,scope,language,oracle=CheckEnchantmentRawBoard(source,shape,language)});
            ProductionSearchReplay.Write(Path.Combine(output,"enchantment-control-boards.json"),receipts);
        }
        ProductionSearchReplay.Write(Path.Combine(output,"enchantment-control-boards.json"),receipts);
        Console.WriteLine($"CRYSTAL_ENCHANTMENT_CONTROLS_PASS cases={receipts.Count};language={language};original589Unchanged=true;separateControls=true");
        return 0;
    }

    private static void CheckEnchantmentTargetContract(PredictorCard prototype)
    {
        var plain=prototype with {Enchantment=null,EnchantmentAmount=0};
        var named=plain with {Enchantment=new ModelKey("ENCHANTMENT","SWIFT"),EnchantmentAmount=1};
        var amount=named with {EnchantmentAmount=2};
        var other=named with {Enchantment=new ModelKey("ENCHANTMENT","GLAM")};
        var wildcard=CrystalRewardOption.ForCard(plain,false);
        var none=CrystalRewardOption.ForCard(plain,true);
        var swift=CrystalRewardOption.ForCard(named,true);
        var two=CrystalRewardOption.ForCard(amount,true);
        var glam=CrystalRewardOption.ForCard(other,true);
        CrystalRewardOption[] targets=[wildcard,none,swift,two,glam];
        Check(targets.Select(t=>t.CacheKey).Distinct().Count()==targets.Length,"Wildcard, exact-none, named enchantment and amount have distinct cache keys");
        foreach(var card in new[]{plain,named,amount,other})
        {
            var exact=CrystalRewardOption.ForCard(card,true);
            Check(wildcard.MatchesCard(card) && wildcard.Accepts(exact),"Legacy plain target accepts every enchantment");
            foreach(var target in targets.Skip(1))
            {
                bool expected=target==exact;
                Check(target.MatchesCard(card)==expected && target.Accepts(exact)==expected,"Exact target distinguishes enchantment identity and amount");
                Check(!target.MatchesCard(card with {UpgradeLevel=card.UpgradeLevel+1}),"Exact enchantment also retains upgrade-level identity");
            }
        }
        Check(none.Enchantment is {Key:null,Amount:0},"Exact-none is explicit and canonical");
        Console.WriteLine("CRYSTAL_ENCHANTMENT_TARGET_CONTRACT_PASS wildcard/exact-none/named/amount;cacheKeysDistinct=true;sameCardLevelVariants=true");
    }

    private static object CheckEnchantmentRawBoard(PredictorCrystalSnapshot source,string shape,bool goalLanguageProof=false,bool includeEnchantments=true)
    {
        PredictorCrystalItem[] specs=shape=="ordered-pair"
            ?[new("CARD_COMMON",2,2,1,3,1,false),new("CARD_UNCOMMON",2,2,3,3,1,false)]
            :[new("CARD_COMMON",2,2,2,2,shape=="repeat"?2:1,false)];
        var hidden=new bool[121];var occupancy=Enumerable.Repeat(-1,121).ToArray();
        for(int i=0;i<specs.Length;i++)
            for(int x=specs[i].X;x<specs[i].X+specs[i].Width;x++) for(int y=specs[i].Y;y<specs[i].Y+specs[i].Height;y++)
            {hidden[x*11+y]=true;occupancy[x*11+y]=i;}
        if(shape=="ordered-pair") foreach(int x in new[]{1,4}) foreach(int y in new[]{3,4}) hidden[x*11+y]=false;
        hidden[0]=hidden[120]=true;
        var board=source with {Remaining=2,Hidden=hidden.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),Items=specs.ToImmutableArray(),Revealed=[]};
        string frozen=JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions);
        var rows=new Dictionary<string,CrystalRewardRoute>();long paths=0;
        void Visit(ImmutableArray<PredictorCrystalStep> steps)
        {
            var run=PredictorCrystalExplorer.Replay(board,steps);
            if(steps.Length==board.Remaining)
            {
                paths++;Check(paths<=20_000,"Bounded raw click oracle stays within complete-work cap");
                if(run.Phase!=PredictorPhase.Rewards) return;
                string key=JsonSerializer.Serialize(new {run.Working,run.Rewards,Rng=run.ExportCrystal().EventRng},ProductionSearchReplay.JsonOptions);
                rows.TryAdd(key,new(steps,run.Rewards){EnchantmentTargets=true});return;
            }
            var current=run.ExportCrystal();
            for(int cell=0;cell<121;cell++) if(current.Hidden[cell])
                foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}) Visit(steps.Add(new(cell/11,cell%11,tool)));
        }
        Visit([]);
        var truth=new HashSet<string>();
        foreach(var row in rows.Values)
            truth.UnionWith(CheckNormalization(PredictorCrystalExplorer.Replay(board,row.Steps),enchanted:true));
        var obtained=truth.Select(ParseEnchantmentOutcome).ToArray();
        bool exactCandidates=includeEnchantments && CrystalRewardOption.UsesEnchantments(board.State);
        var options=obtained.SelectMany(o=>o).Select(o=>exactCandidates?o:o with {Enchantment=null})
            .Distinct().OrderBy(o=>o.CacheKey,StringComparer.Ordinal).ToArray();
        Check(options.Length>0,"Small board oracle observes acquired exact variants");
        bool RawSupports(ImmutableArray<CrystalRewardOption> goals)=>obtained.Any(o=>EnchantmentMultisetSupports(o,goals));
        int variants=options.GroupBy(o=>(o.Key,o.UpgradeLevel)).Count(g=>g.Select(o=>o.Enchantment).Distinct().Count()>1);
        var prepare=Stopwatch.StartNew();var table=new CrystalReachabilityTable(board,includeRerolls:true,includeEnchantments:includeEnchantments);
        double tableMs=prepare.Elapsed.TotalMilliseconds;
        Check(options.All(table.Candidates.Contains),"Exact lookup candidate domain retains every raw obtained variant: "+shape);
        Check(table.Candidates.Where(o=>o.Kind==PredictorRewardKind.Card).All(o=>(o.Enchantment!=null)==exactCandidates),
            "Published enchantment specificity follows current inventory");
        var bound=new PredictorCrystalOffsetBound(board,table,true);int questions=0,positive=0,negative=0;
        var queryTimer=Stopwatch.StartNew();
        void Compare(ImmutableArray<CrystalRewardOption> goals)
        {
            bool expected=RawSupports(goals);
            var answer=PredictorCrystalGoalLanguage.Solve(board,table,goals,true,TimeSpan.FromSeconds(5),sharedBound:bound);
            Check(answer.Verdict!=CrystalOffsetVerdict.Incomplete,"Bounded exact language query closes: "+shape+" "+string.Join('|',goals));
            Check((answer.Plan!=null)==expected && (answer.Verdict==CrystalOffsetVerdict.Possible)==expected,
                "Language exact enchantment truth equals full raw clicks x Enter/Exit/Reroll/Take: "+shape+" "+string.Join('|',goals));
            if(answer.Plan is {} plan)
            {
                var replayed=ReplayProof(board,plan);
                Check(EnchantmentObtainedSupports(PredictorCrystalExplorer.Replay(board,plan.Steps),replayed.Run,goals),"Positive exact plan replays requested acquired variants");
            }
            questions++;if(expected) positive++;else negative++;
        }
        foreach(var option in options) Compare([option]);
        for(int a=0;a<options.Length;a++) for(int b=a;b<options.Length;b++) Compare([options[a],options[b]]);
        // Warm the same frozen bound with incompatible constraints first. A
        // later wildcard or same-key alternative must not inherit their negatives.
        foreach(var group in options.GroupBy(o=>(o.Key,o.UpgradeLevel)))
        {
            var exact=group.First();var wildcard=exact with {Enchantment=null};
            var none=exact with {Enchantment=new CrystalCardEnchantment(null,0)};
            var wrong=exact with {Enchantment=new CrystalCardEnchantment(new ModelKey("ENCHANTMENT","SWIFT"),999)};
            Compare([wrong]);Compare([none]);Compare([wildcard]);Compare([exact]);
        }
        double queryMs=queryTimer.Elapsed.TotalMilliseconds;
        Check(PredictorCrystalGoalLanguage.Solve(board,table,[options[0]],true,TimeSpan.Zero,sharedBound:bound).Verdict==CrystalOffsetVerdict.Incomplete,
            "Zero-budget exact query remains unknown");
        Check(PredictorCrystalGoalLanguage.Solve(board,table,[options[0]],true,TimeSpan.FromSeconds(5),stateLimit:0,sharedBound:bound).Verdict==CrystalOffsetVerdict.Incomplete,
            "Reward-state cutoff cannot certify absence of a reachable exact variant");
        var session=new PredictorCrystalReachability(board,true,includeRerolls:true,workers:1,goalLanguageProof:goalLanguageProof,includeEnchantments:includeEnchantments);
        // Full candidate closure is compared with raw truth; no table absence
        // serves as an independent oracle.
        CrystalReachabilityUpdate Close(ImmutableArray<CrystalRewardOption> selected)
        {
            var timer=Stopwatch.StartNew();var update=session.Advance(selected,TimeSpan.Zero);
            while(!update.Complete && timer.Elapsed<TimeSpan.FromSeconds(30)) update=session.Advance(selected,TimeSpan.FromMilliseconds(30));
            Check(update.Complete,"Small exact candidate frontier completes: "+shape);
            var expected=options.Where(o=>!selected.Contains(o) && RawSupports(selected.Add(o))).ToHashSet();
            Check(expected.SetEquals(update.Projection.Available.Keys),"Complete exact candidate projection equals raw joint truth: "+shape);
            Check(update.ExcludedCandidates.All(o=>!RawSupports(selected.Add(o))),"Every published exact exclusion has raw negative truth");
            bool expectedSelected=RawSupports(selected),actualSelected=update.Projection.SelectedPlan!=null;
            if(actualSelected!=expectedSelected)
                Console.WriteLine($"CRYSTAL_ENCHANTMENT_CONTROL_MISMATCH shape={shape};language={goalLanguageProof};selected={string.Join('|',selected.Select(g=>g.CacheKey))};expectedSelected={expectedSelected};actualSelected={actualSelected};complete={update.Complete};rawOutcomes={truth.Count}");
            Check(actualSelected==expectedSelected,"Selected exact plan retains full raw truth: "+string.Join('|',selected.Select(g=>g.CacheKey)));
            return update;
        }
        Close([]);var first=options[0];Close([first]);
        var pairOutcome=obtained.FirstOrDefault(o=>o.Distinct().Count()>=2);
        ImmutableArray<CrystalRewardOption> joint=pairOutcome.IsDefault?[first]:pairOutcome.Distinct().Take(2).ToImmutableArray();
        using(var canceled=new CancellationTokenSource())
        {
            canceled.Cancel();bool thrown=false;
            try {session.Advance(joint,TimeSpan.FromMilliseconds(30),canceled.Token);}
            catch(OperationCanceledException) {thrown=true;}
            Check(thrown,"Canceled exact query observes cancellation");
        }
        Close(joint);Close([joint[0]]);Close([]);Close(joint);
        var wildcardFirst=first with {Enchantment=null};
        var exactNone=first with {Enchantment=new CrystalCardEnchantment(null,0)};
        var wrongAmount=first with {Enchantment=new CrystalCardEnchantment(new ModelKey("ENCHANTMENT","SWIFT"),999)};
        Close([wrongAmount]);Close([exactNone]);Close([wildcardFirst]);Close([first]);
        foreach(var goals in new[]{joint,[joint[0]],ImmutableArray<CrystalRewardOption>.Empty})
        {
            var projection=session.ProjectKnown(goals);long replays=session.ProofReplays;
            var repeated=session.ProjectKnown(goals);
            Check(session.ProofReplays==replays && projection.Available.Keys.ToHashSet().SetEquals(repeated.Available.Keys),"Repeated exact retarget reuses proof cache");
        }
        Check(frozen==JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions),"Complete raw enchantment oracle does not mutate source");
        Console.WriteLine($"CRYSTAL_ENCHANTMENT_RAW_BOARD_PASS shape={shape};rawPaths={paths};endpoints={rows.Count};outcomes={truth.Count};exactVariants={options.Length};sameCardLevelVariants={variants};queries={questions};positive={positive};negative={negative};tableMs={tableMs:F3};queryMs={queryMs:F3};projectionEquality=true;cancelAppendReselect=true");
        return new {shape,goalLanguageProof,exactCandidates,rawPaths=paths,endpoints=rows.Count,outcomes=truth.Count,options,sameCardLevelVariants=variants,questions,positive,negative,
            tableMs,queryMs,scope="Complete artificial small geometry, real generation and raw typed reward actions; no native full-board negative claim"};
    }

    private static ImmutableArray<CrystalRewardOption> ParseEnchantmentOutcome(string outcome)=>outcome.Split('|',StringSplitOptions.RemoveEmptyEntries)
        .Select(identity=>{var p=identity.Split(':');return new CrystalRewardOption(PredictorRewardKind.Card,new ModelKey("CARD",p[0]),int.Parse(p[1]),
            new CrystalCardEnchantment(p[2].Length==0?null:new ModelKey("ENCHANTMENT",p[2]),int.Parse(p[3])));}).ToImmutableArray();

    private static bool EnchantmentMultisetSupports(ImmutableArray<CrystalRewardOption> cards,ImmutableArray<CrystalRewardOption> goals)
    {
        bool Match(int index,int used)=>index==goals.Length || Enumerable.Range(0,cards.Length)
            .Any(i=>(used&(1<<i))==0 && goals[index].Accepts(cards[i]) && Match(index+1,used|(1<<i)));
        return Match(0,0);
    }
    private static bool EnchantmentObtainedSupports(PredictorRun root,PredictorRun final,ImmutableArray<CrystalRewardOption> goals)=>
        EnchantmentMultisetSupports(final.Working.Deck.Where(id=>!root.Working.Deck.Contains(id)).Select(final.Working.Card)
            .Select(c=>CrystalRewardOption.ForCard(c,true)).ToImmutableArray(),goals);

}
