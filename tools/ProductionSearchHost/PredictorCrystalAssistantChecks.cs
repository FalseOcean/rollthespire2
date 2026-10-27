using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void Check(bool test, string why) { if (!test) throw new InvalidOperationException("CrystalAssistant: " + why); }
    private static int CheckSavedGold(string savePath)
    {
        var save=JsonSerializer.Deserialize(File.ReadAllText(savePath),MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.SerializableRun>())!;
        var native=RunState.FromSerializable(save);var manager=RunManager.Instance;
        var stateProperty=manager.GetType().GetProperty("State",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
        var ascensionProperty=manager.GetType().GetProperty("AscensionManager",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
        var previous=stateProperty.GetValue(manager);var previousAscension=ascensionProperty.GetValue(manager);
        stateProperty.SetValue(manager,native);ascensionProperty.SetValue(manager,new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(native.AscensionLevel));
        try
        {
            var player=native.Players[0];var ev=(CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
            ev.BeginEvent(player,null,false).GetAwaiter().GetResult();
            var source=CrystalSphereLiveCapture.CaptureScene(native,player,ev,null,"0.111.0").Branches.Single(b=>b.Mode=="Six").Snapshot;
            string directory=Path.GetDirectoryName(savePath)!;ProductionSearchReplay.Write(Path.Combine(directory,"snapshot.json"),source);
            Console.WriteLine($"GOLD_SAVED_SOURCE seed={source.Context.Seed};act={source.State.Position.Act};floor={source.State.Position.Floor};items={source.Items.Length}");
            for(int i=0;i<source.Items.Length;i++) Console.WriteLine($"GOLD_ITEM {i} {source.Items[i]}");
            ImmutableArray<CrystalRewardOption> goals=[new(PredictorRewardKind.Relic,new("RELIC","VEXING_PUZZLEBOX")),
                new(PredictorRewardKind.Card,new("CARD","STOKE"),0),new(PredictorRewardKind.Card,new("CARD","FEEL_NO_PAIN"),1),
                new(PredictorRewardKind.Card,new("CARD","SHRUG_IT_OFF"),1)];
            ImmutableArray<PredictorCrystalStep> photographed=[new(9,6,PredictorCrystalTool.Big),new(6,2,PredictorCrystalTool.Big),
                new(9,3,PredictorCrystalTool.Big),new(6,5,PredictorCrystalTool.Big),new(4,9,PredictorCrystalTool.Big),new(7,0,PredictorCrystalTool.Big)];
            var photographedRun=PredictorCrystalExplorer.Replay(source,photographed);
            var photographedProof=PredictorCrystalExplorer.VerifySelection(source,new(photographed,photographedRun.Rewards),goals);
            int photographedGold=photographedRun.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
            Console.WriteLine($"GOLD_PHOTOGRAPH gold={photographedGold};valid={photographedProof!=null};order={string.Join(',',photographedRun.ExportCrystal().Revealed)}");
            Check(photographedGold==30 && photographedProof!=null,"Saved context and photographed legal guide reproduce four targets and 30 gold");
            var refined=ImproveGoldForChecks(source,new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,photographedProof,photographedGold),goals,true);
            Console.WriteLine($"GOLD_PHOTOGRAPH_REFINED gold={refined.Gold};plan={string.Join(';',refined.SelectedPlan!.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}"))}");
            Check(refined.Gold>=50,"Whole-board gold rewrite replaces a small pile with the upper-left large pile");
            var refinedRun=PredictorCrystalExplorer.Replay(source,refined.SelectedPlan.Steps);
            Check(PredictorCrystalExplorer.VerifySelection(source,new(refined.SelectedPlan.Steps,refinedRun.Rewards),goals)!=null,
                "Rewritten guide retains the exact four ordered reward takes");
            ProductionSearchReplay.Write(Path.Combine(directory,"photographed-plan.json"),photographedProof);
            ProductionSearchReplay.Write(Path.Combine(directory,"refined-plan.json"),refined.SelectedPlan);
            CheckNative(photographed,photographedRun,"photographed");
            CheckNative(refined.SelectedPlan.Steps,refinedRun,"refined");
            var cached=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true);
            cached.AddWitnesses([new(photographed,photographedRun.Rewards)]);
            Check(cached.ProjectKnown(goals).Gold==30,"Immediate cached projection does not run whole-board gold refinement");
            var cachedUpdate=cached.AdvanceGold(goals,TimeSpan.FromSeconds(5));
            Check(cachedUpdate.Gold>=50 && cached.PeekKnown(goals).Gold>=50 && cached.ProjectKnown(goals).Gold>=50,
                "Background publication upgrades and retains an already-known thirty-gold guide without a new click search");
            Console.WriteLine($"GOLD_CACHED_UPDATE gold={cachedUpdate.Gold};goldBudget=5;candidateTraversalBudget=0;complete={cachedUpdate.GoldComplete}");
            var session=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true);
            var watch=System.Diagnostics.Stopwatch.StartNew();CrystalReachabilityUpdate update;
            do { update=session.Advance(goals,TimeSpan.FromMilliseconds(150)); }
            while(update.Projection.SelectedPlan==null && !update.Complete && watch.Elapsed<TimeSpan.FromSeconds(15));
            Console.WriteLine($"GOLD_SAVED_QUERY seconds={watch.Elapsed.TotalSeconds:F3};complete={update.Complete};gold={update.Projection.Gold};found={update.Projection.SelectedPlan!=null}");
            if(update.Projection.SelectedPlan is not { } plan) return 1;
            ProductionSearchReplay.Write(Path.Combine(directory,"plan.json"),plan);
            Console.WriteLine($"GOLD_SAVED_PLAN {string.Join(';',plan.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}"))}");
            Console.WriteLine("GOLD_SAVED_PASS photographed 30 -> at least 50; exact targets; legal native clicks/rewards/RNG");
            return 0;

            void CheckNative(ImmutableArray<PredictorCrystalStep> steps,PredictorRun expected,string label)
            {
                var verificationRun=RunState.FromSerializable(save);stateProperty.SetValue(manager,verificationRun);
                var verificationPlayer=verificationRun.Players[0];var verificationEvent=(CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
                verificationEvent.BeginEvent(verificationPlayer,null,false).GetAwaiter().GetResult();
                MegaCrit.Sts2.Core.Commands.CardPileCmd.AddCurseToDeck<MegaCrit.Sts2.Core.Models.Cards.Debt>(verificationPlayer).GetAwaiter().GetResult();
                var game=new CrystalSphereMinigame(verificationPlayer,verificationEvent.Rng,6);
                var initial=CrystalSphereLiveCapture.CaptureBoard(source.Context,source.State,game);
                Check(initial.Hidden.SequenceEqual(source.Hidden) && initial.Items.SequenceEqual(source.Items) && initial.EventRng==source.EventRng,
                    label+" native minigame reproduces the frozen board");
                foreach(var step in steps)
                {
                    Check(game.cells[step.X,step.Y].IsHidden,label+" native click center is still hidden");
                    game.SetTool(step.Tool==PredictorCrystalTool.Big?CrystalSphereMinigame.CrystalSphereToolType.Big:CrystalSphereMinigame.CrystalSphereToolType.Small);
                    game.CellClicked(game.cells[step.X,step.Y]).GetAwaiter().GetResult();
                }
                var revealed=(List<CrystalSphereItem>)typeof(CrystalSphereMinigame).GetField("_revealed",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(game)!;
                var rewards=new RewardsSet(verificationPlayer).WithCustomRewards(revealed.Select(i=>i.ToReward(verificationPlayer,game.Rng)).OfType<Reward>().ToList());
                rewards.GenerateWithoutOffering().GetAwaiter().GetResult();
                var order=revealed.Select(i=>game.Items.ToList().IndexOf(i)).ToArray();
                Check(game.DivinationCount==0 && order.SequenceEqual(expected.ExportCrystal().Revealed),label+" native physical completion order and reveal count");
                var cards=rewards.Rewards.OfType<CardReward>().Select(r=>string.Join(',',r.Cards.Select(c=>$"{c.Id.Entry}:{c.CurrentUpgradeLevel}"))).ToArray();
                var predictedCards=expected.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).Select(r=>string.Join(',',r.Cards.Select(c=>$"{c.Key.Entry}:{c.UpgradeLevel}"))).ToArray();
                Check(cards.SequenceEqual(predictedCards),label+" all native card reward groups and upgrade variants");
                Check(rewards.Rewards.OfType<RelicReward>().Select(r=>r.Relic!.Id.Entry).SequenceEqual(expected.Rewards.Where(r=>r.Kind==PredictorRewardKind.Relic).Select(r=>r.Key.Entry)),
                    label+" native relic identity");
                int gold=rewards.Rewards.OfType<GoldReward>().Sum(r=>r.Amount);
                Check(gold==expected.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount),label+" native gold amount");
                var rng=game.Rng.ToSerializable();var continuation=expected.ExportCrystal().EventRng;
                Check(continuation.S0==rng.state0 && continuation.S1==rng.state1 && continuation.S2==rng.state2 && continuation.S3==rng.state3 && continuation.Calls==rng.counter,
                    label+" native full event RNG continuation");
                Console.WriteLine($"GOLD_NATIVE label={label};gold={gold};allCentersHidden=true;order={string.Join(',',order)};rngCalls={rng.counter};cards={string.Join(';',cards)}");
            }
        }
        finally { stateProperty.SetValue(manager,previous);ascensionProperty.SetValue(manager,previousAscension); }
    }
    private static void CheckOffsetSnapshot(PredictorCrystalSnapshot source)
    {
        var table=new CrystalReachabilityTable(source);var bound=new PredictorCrystalOffsetBound(source,table,true);
        ImmutableArray<CrystalRewardOption> goals=[
            new(PredictorRewardKind.Card,new("CARD","CRASH_LANDING"),0),
            new(PredictorRewardKind.Card,new("CARD","PILLAR_OF_CREATION"),1),
            new(PredictorRewardKind.Card,new("CARD","SPOILS_OF_BATTLE"),0),
            new(PredictorRewardKind.Potion,new("POTION","LUCKY_TONIC")),
            new(PredictorRewardKind.Potion,new("POTION","COLORLESS_POTION")),
            new(PredictorRewardKind.Potion,new("POTION","SKILL_POTION"))];
        var rare=new CrystalRewardOption(PredictorRewardKind.Relic,new("RELIC","MEAT_ON_THE_BONE"));
        var legacy=new PredictorCrystalExplorer(source,true,reachability:table,selected:[goals[0]],potionScenarioBound:true);
        Check(legacy.InitialMaySupport(rare),"Frozen rare conflict survives the old offset relaxation");
        Check(bound.Check([goals[0],rare],0).Verdict==CrystalOffsetVerdict.Incomplete,"Work cutoff is not a negative proof");
        var timer=System.Diagnostics.Stopwatch.StartNew();var receipt=bound.Check([goals[0],rare]);
        Console.WriteLine($"CRYSTAL_OFFSET_RARE verdict={receipt.Verdict};states={receipt.States};ms={timer.Elapsed.TotalMilliseconds:F3}");
        Check(receipt.Verdict==CrystalOffsetVerdict.Impossible,"Joint reward offsets and cover close the frozen rare conflict");
        foreach(int count in new[]{1,3,5,6})
        {
            var selected=goals.Take(count).ToImmutableArray();timer.Restart();
            var possible=bound.Check(selected);Check(possible.MaySupport,"Known six-target prefix survives exact offset DP");
            int rejected=0,unknown=0;
            foreach(var candidate in table.Candidates.Where(c=>!selected.Contains(c)))
            {
                var result=bound.Check(selected.Add(candidate));
                if(result.Verdict==CrystalOffsetVerdict.Impossible) rejected++;
                if(result.Verdict==CrystalOffsetVerdict.Incomplete) unknown++;
            }
            Console.WriteLine($"CRYSTAL_OFFSET_ROOT selected={count};ms={timer.Elapsed.TotalMilliseconds:F3};rejected={rejected};unknown={unknown};states={possible.States}");
        }
        string directory=Path.GetDirectoryName(Environment.GetEnvironmentVariable("RT2_CRYSTAL_OFFSET_SNAPSHOT"))!;
        var plan=ProductionSearchReplay.Read<PredictorCrystalSolution>(Path.Combine(directory,"six-targets-True.json"));
        var replay=PredictorCrystalExplorer.Replay(source,plan.Steps);var row=new CrystalRewardRoute(plan.Steps,replay.Rewards);
        Check(PredictorCrystalExplorer.VerifySelection(source,row,goals)!=null,"Frozen six-target witness still takes three distinct cards and offers all potions");
        foreach(bool enabled in new[]{false,true})
        {
            var session=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,jointOffsetBound:enabled);
            timer.Restart();var early=session.Advance([goals[0]],TimeSpan.Zero);
            Check(!early.ExcludedCandidates.Any(goals.Skip(1).Contains),"Early proof retains all known six-target additions");
            Console.WriteLine($"CRYSTAL_OFFSET_SESSION enabled={enabled};rootMs={timer.Elapsed.TotalMilliseconds:F3};excluded={early.ExcludedCandidates.Length};rareExcluded={early.ExcludedCandidates.Contains(rare)}");
            if(enabled) Check(early.ExcludedCandidates.Contains(rare),"Strict offset exclusion reaches existing candidate projection without click traversal");
            session.AddWitnesses([row]);var known=session.ProjectKnown(goals);
            Check(known.SelectedPlan!=null,"Existing ordered take witness is retained with offset proofs");
            known=session.ProjectKnown([goals[0]]);
            Check(!early.ExcludedCandidates.Any(known.Available.ContainsKey),"Early exclusions preserve incidental compatible rewards on the frozen witness");
        }
        Console.WriteLine($"CRYSTAL_OFFSET_SNAPSHOT_PASS checks={bound.Checks};rejected={bound.Rejected};incomplete={bound.Incomplete};cacheHits={bound.CacheHits};pureProofSeconds={bound.Seconds:F3}");
    }
    private static void CheckCandidateFirst(PredictorCrystalSnapshot source)
    {
        var rare=new CrystalRewardOption(PredictorRewardKind.Relic,new("RELIC","MEAT_ON_THE_BONE"));
        var single=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true);
        var root=single.Advance([rare],TimeSpan.Zero);
        var relics=new CrystalReachabilityTable(source).Candidates.Where(c=>c.Kind==PredictorRewardKind.Relic && c.Key.IsValid && c!=rare).ToArray();
        Check(relics.Length>0 && relics.All(root.ExcludedCandidates.Contains),"One relic callback immediately excludes other concrete relic identities");
        var twice=source with { Items=source.Items.Select(i=>i.Kind=="RELIC"?i with { Subscriptions=2 }:i).ToImmutableArray() };
        var twoRoot=new PredictorCrystalReachability(twice,true,potionScenarioBound:true).Advance([rare],TimeSpan.Zero);
        Check(relics.Any(r=>!twoRoot.ExcludedCandidates.Contains(r)),"Two relic callbacks must not inherit a one-relic-only rule");
        Console.WriteLine($"CRYSTAL_RELIC_CAPACITY_PASS singleExcluded={relics.Length};twoCallbacksRemainPossible=true");
        ImmutableArray<CrystalRewardOption> goals=[
            new(PredictorRewardKind.Card,new("CARD","CRASH_LANDING"),0),
            new(PredictorRewardKind.Card,new("CARD","PILLAR_OF_CREATION"),1),
            new(PredictorRewardKind.Card,new("CARD","SPOILS_OF_BATTLE"),0),
            new(PredictorRewardKind.Potion,new("POTION","LUCKY_TONIC")),
            new(PredictorRewardKind.Potion,new("POTION","COLORLESS_POTION")),
            new(PredictorRewardKind.Potion,new("POTION","SKILL_POTION"))];
        var completedOptions=new Dictionary<int,HashSet<CrystalRewardOption>>();
        foreach(bool individual in new[]{false,true})
        {
            var session=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,candidateFirst:individual);
            foreach(int count in new[]{1,3,5,6})
            {
                var selected=goals.Take(count).ToImmutableArray();var watch=System.Diagnostics.Stopwatch.StartNew();
                var known=session.ProjectKnown(selected);double? firstPlan=known.SelectedPlan!=null?0:null;
                CrystalReachabilityUpdate update;
                do
                {
                    update=session.Advance(selected,TimeSpan.FromMilliseconds(150));
                    if(update.Projection.SelectedPlan!=null) firstPlan??=watch.Elapsed.TotalSeconds;
                    Check(!goals.Skip(count).Any(update.ExcludedCandidates.Contains),"Reported six-target combination is not prematurely excluded");
                } while(!update.Complete && watch.Elapsed<TimeSpan.FromSeconds(count==6?10:3));
                Console.WriteLine($"CRYSTAL_CANDIDATE_FIRST enabled={individual};selected={count};firstPlan={firstPlan};wall={watch.Elapsed.TotalSeconds:F3};options={update.Projection.Available.Count};excluded={update.ExcludedCandidates.Length};complete={update.Complete};probes={update.ProbeSlices};plans={update.ProbePlans}");
                if(update.Complete)
                {
                    if(!individual) completedOptions[count]=update.Projection.Available.Keys.ToHashSet();
                    else if(completedOptions.TryGetValue(count,out var expected)) Check(expected.SetEquals(update.Projection.Available.Keys),"Both complete schedules agree on compatible candidates");
                }
                if(count==6)
                {
                    Check(update.Projection.SelectedPlan!=null,"Reported six-target combination has a verified legal plan");
                    var plan=update.Projection.SelectedPlan!;var replay=PredictorCrystalExplorer.Replay(source,plan.Steps);
                    Check(PredictorCrystalExplorer.VerifySelection(source,new(plan.Steps,replay.Rewards),goals)!=null,"Three ordered card takes preserve exact upgrade and all three open potion rewards");
                    ProductionSearchReplay.Write(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("RT2_CRYSTAL_CANDIDATE_SNAPSHOT"))!,"six-targets-"+individual+".json"),plan);
                }
            }
        }
        Console.WriteLine("CRYSTAL_CANDIDATE_FIRST_PASS captured six-target witness; concrete relic count; bounded comparison, not universal throughput");
    }
    // Opt-in frozen live-capture regression. Timings describe this board only;
    // bounded observation never asserts negative reachability.
    private static void CheckProactiveDiscovery(PredictorCrystalSnapshot source)
    {
        var rare=new CrystalRewardOption(PredictorRewardKind.Relic,new("RELIC","MEAT_ON_THE_BONE"));
        foreach(bool proactive in new[]{false,true})
        {
            var session=new PredictorCrystalReachability(source,true,workers:4,potionScenarioBound:true,targetDirected:true,proactiveDiscovery:proactive);
            var watch=System.Diagnostics.Stopwatch.StartNew();double? firstRare=null;
            CrystalReachabilityUpdate update;
            do
            {
                update=session.Advance([],TimeSpan.FromMilliseconds(150));
                if(update.Projection.Available.ContainsKey(rare)) firstRare??=watch.Elapsed.TotalSeconds;
            } while(!update.Complete && watch.Elapsed<TimeSpan.FromSeconds(6));
            Console.WriteLine($"CRYSTAL_PROACTIVE proactive={proactive};seed={source.Context.Seed};wall={watch.Elapsed.TotalSeconds:F3};firstRare={firstRare};options={update.Projection.Available.Count};complete={update.Complete};probeSlices={update.ProbeSlices};probePlans={update.ProbePlans};cards={update.Projection.Available.Keys.Count(o=>o.Kind==PredictorRewardKind.Card)}");
            if(!proactive) continue;
            Check(firstRare!=null && update.ProbePlans>0,"Proactive discovery finds captured rare relic without player targeting it");
            Check(!update.Complete,"Positive probe completion does not claim all candidates exhausted");
            var plan=update.Projection.Available[rare];
            var replay=PredictorCrystalExplorer.Replay(source,plan.Steps);
            Check(PredictorCrystalExplorer.VerifySelection(source,new(plan.Steps,replay.Rewards),[rare])!=null,"Discovered rare plan replays legal centers and real takes");
            ProductionSearchReplay.Write(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PROACTIVE_SNAPSHOT"))!,"proactive-plan.json"),plan);
            watch.Restart();
            var known=session.ProjectKnown([rare]);
            Check(known.SelectedPlan!=null,"Selected discovered target reuses its witness immediately");
            Console.WriteLine($"CRYSTAL_PROACTIVE_RETARGET seconds={watch.Elapsed.TotalSeconds:F3};options={known.Available.Count}");
            using(var stop=new CancellationTokenSource())
            {
                stop.Cancel();
                try { session.Advance([rare],TimeSpan.FromMilliseconds(150),stop.Token);Check(false,"Canceled slice throws"); }
                catch(OperationCanceledException) { }
            }
            update=session.Advance([rare],TimeSpan.FromMilliseconds(150));
            Check(update.Projection.SelectedPlan!=null,"Cancellation does not poison resumed query");
            foreach(var (candidate,proof) in update.Projection.Available)
            {
                var result=PredictorCrystalExplorer.Replay(source,proof.Steps);
                Check(PredictorCrystalExplorer.VerifySelection(source,new(proof.Steps,result.Rewards),[rare,candidate])!=null,"Retargeted candidate jointly attainable: "+candidate);
            }
        }
        Console.WriteLine("CRYSTAL_PROACTIVE_PASS frozen live source, legal positive proofs, retarget reuse and pause/resume; not a full native-suite receipt");
    }
    internal static int Run(string seed, int ascension)
    {
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PAELS_WING") is { Length:>0 } wingOutput)
            return CheckPaelsWing(wingOutput,ascension);
        bool conditionalFocus=Environment.GetEnvironmentVariable("RT2_CRYSTAL_CONDITIONAL_FOCUS")=="1";
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_RELIC_SAVE") is { Length:>0 } relicSave) return CheckSavedRelicPrefixes(relicSave);
        if (Environment.GetEnvironmentVariable("RT2_CRYSTAL_DRIFTWOOD_EXPERIMENT") is { Length: > 0 } experiment)
            return CheckDriftwoodExperiment(experiment, ascension);
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOLD_SAVE") is { Length:>0 } goldSave) return CheckSavedGold(goldSave);
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_OFFSET_SNAPSHOT") is { Length:>0 } offsetSnapshot)
        {
            CheckOffsetSnapshot(ProductionSearchReplay.Read<PredictorCrystalSnapshot>(offsetSnapshot));
            return 0;
        }
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_CANDIDATE_SNAPSHOT") is { Length:>0 } candidateSnapshot)
        {
            CheckCandidateFirst(ProductionSearchReplay.Read<PredictorCrystalSnapshot>(candidateSnapshot));
            return 0;
        }
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_PROACTIVE_SNAPSHOT") is { Length:>0 } proactiveSnapshot)
        {
            CheckProactiveDiscovery(ProductionSearchReplay.Read<PredictorCrystalSnapshot>(proactiveSnapshot));
            return 0;
        }
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var native = RunState.CreateForTest([player], seed: seed, ascensionLevel: ascension);
        native.CurrentActIndex = 1;
        player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
        native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(player.UnlockState), native.Rng.UpFront);
        var manager = RunManager.Instance;
        var stateProperty = manager.GetType().GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var previous = stateProperty.GetValue(manager); stateProperty.SetValue(manager, native);
        var ascensionProperty = manager.GetType().GetProperty("AscensionManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var previousAscension = ascensionProperty.GetValue(manager);
        ascensionProperty.SetValue(manager, new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(native.AscensionLevel));
        try
        {

            foreach (bool debt in new[] { false, true })
            {
                var branchPlayer = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
                var branchRun = RunState.CreateForTest([branchPlayer], seed: seed, ascensionLevel: ascension);
                branchRun.CurrentActIndex = 1; stateProperty.SetValue(manager, branchRun);
                branchPlayer.PopulateRelicGrabBagIfNecessary(branchRun.Rng.UpFront);
                var branchEvent = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
                branchEvent.BeginEvent(branchPlayer, null, false).GetAwaiter().GetResult();
                var expected = CrystalSphereLiveCapture.CaptureScene(branchRun, branchPlayer, branchEvent, null, "0.111.0").Branches[debt ? 1 : 0].Snapshot;
                if (debt) MegaCrit.Sts2.Core.Commands.CardPileCmd.AddCurseToDeck<MegaCrit.Sts2.Core.Models.Cards.Debt>(branchPlayer).GetAwaiter().GetResult();
                else MegaCrit.Sts2.Core.Commands.PlayerCmd.LoseGold(branchEvent.DynamicVars["UncoverFutureCost"].BaseValue,
                    branchPlayer, MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Spent).GetAwaiter().GetResult();
                var branchBoard = new CrystalSphereMinigame(branchPlayer, branchEvent.Rng, debt ? 6 : 3);
                var entered = CrystalSphereLiveCapture.CaptureScene(branchRun, branchPlayer, branchEvent, branchBoard, "0.111.0").Branches[0].Snapshot;
                Check(RolltheSpire2.Ui.Shell.CrystalSphereGuidance.Matches(expected, entered, false), "Guide accepts actual payment entry " + debt);
                CheckClickCenters(entered, branchBoard);
            }
            stateProperty.SetValue(manager, native);
            Console.WriteLine("CRYSTAL_GUIDANCE_ENTRY_PASS native paid/Debt commands -> matching board and persistent state");
            // Actual vanilla saved-player import: Original is absent while
            // the saved queues remain authoritative and refresh stays disabled.
            var loaded = Player.FromSerializable(player.ToSerializable());
            var loadedRun = RunState.CreateForTest([loaded], seed: seed, ascensionLevel: ascension);
            loadedRun.CurrentActIndex = 1;
            typeof(RunState).GetProperty("SharedRelicGrabBag")!.SetValue(loadedRun, RelicGrabBag.FromSerializable(native.SharedRelicGrabBag.ToSerializable()));
            var loadedEvent = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
            loadedEvent.BeginEvent(loaded, null, false).GetAwaiter().GetResult();
            var loadedCapture = CrystalSphereLiveCapture.CaptureScene(loadedRun, loaded, loadedEvent, null, "0.111.0");
            Check(loadedCapture.Branches.Length == 2 && !loadedCapture.Branches[0].Snapshot.State.PersonalBag.HasOriginalCatalog &&
                !loadedCapture.Branches[0].Snapshot.State.PersonalBag.RefreshAllowed, "Loaded native queues capture without a refill catalog");
            Console.WriteLine("CRYSTAL_LOADED_CAPTURE_PASS native saved player/shared bag -> both payment plans");
            var ev = (CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();
            ev.BeginEvent(player, null, false).GetAwaiter().GetResult();
            string untouched = CrystalSphereLiveCapture.SceneFingerprint(native, player, ev, null);
            var before = CrystalSphereLiveCapture.CaptureScene(native, player, ev, null, "0.111.0");
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_WORKER_LIFECYCLE")=="1")
            {
                CheckLanguageWorkerLifecycle(before.Branches.Single(b=>b.Mode=="Six").Snapshot);
                return 0;
            }
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOLD_FOCUS")=="1")
            {
                CheckExactGold(before.Branches.Single(b=>b.Mode=="Six").Snapshot);
                CheckNativeGoldBudgets(before.Branches.Single(b=>b.Mode=="Six").Snapshot);
                Console.WriteLine("CRYSTAL_GOLD_FOCUS_PASS controlled raw/legal gold scope;not-full-native-suite");
                return 0;
            }
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_ONLY")=="1")
            {
                CheckDiscreteBoundWorkloads(before.Branches.Single(b=>b.Mode=="Six").Snapshot);
                Console.WriteLine("CRYSTAL_BOUND_ONLY_DONE benchmark only; not a full native-suite receipt");
                return 0;
            }
            Check(before.Branches.Select(b => b.Snapshot.Remaining).SequenceEqual([3, 6]), "Both native payment branches captured");
            var originalGold = player.Gold;
            player.Gold = 0;
            var depletedGold = CrystalSphereLiveCapture.CaptureScene(native, player, ev, null, "0.111.0");
            Check(depletedGold.Branches.Length == 2 && depletedGold.Branches[0].Snapshot.State.GoldBudget == 0,
                "Payment stays available after entry; native LoseGold clamps balance");
            player.Gold = originalGold;
            Check(untouched == CrystalSphereLiveCapture.SceneFingerprint(native, player, ev, null), "Full capture does not mutate Player/RNG");
            var board = new CrystalSphereMinigame(player, ev.Rng, 3);
            var capture = CrystalSphereLiveCapture.CaptureScene(native, player, ev, board, "0.111.0");
            var snapshot = capture.Branches.Single().Snapshot;
            Check(snapshot.Hidden.SequenceEqual(before.Branches[0].Snapshot.Hidden) &&
                snapshot.Occupancy.SequenceEqual(before.Branches[0].Snapshot.Occupancy) &&
                snapshot.Items.SequenceEqual(before.Branches[0].Snapshot.Items) && snapshot.EventRng == before.Branches[0].Snapshot.EventRng,
                "Native initial board, subscriptions and RNG match RT2 branch generation");
            // Reveal one native cell without finishing an item: a real partial
            // board is captured; do not reconstruct it by rerolling from seed.
            var cell = board.cells.Cast<CrystalSphereCell>().First(c => c.IsHidden &&
                (c.Item == null || c.Item.Size.X * c.Item.Size.Y > 1));
            board.SetTool(CrystalSphereMinigame.CrystalSphereToolType.Small);
            board.CellClicked(cell).GetAwaiter().GetResult();
            var partial = CrystalSphereLiveCapture.CaptureScene(native, player, ev, board, "0.111.0");
            Check(partial.Fingerprint != capture.Fingerprint && partial.Branches[0].Snapshot.Remaining == 2, "Partial progress invalidates snapshot");
            var replay = PredictorRun.FromCrystal(snapshot);
            replay.Submit(replay.Request!, new SelectCrystalTool(PredictorCrystalTool.Small));
            replay.Submit(replay.Request!, new RevealCrystalCell(cell.X, cell.Y));
            Check(replay.ExportCrystal().Hidden.SequenceEqual(partial.Branches[0].Snapshot.Hidden), "Native partial click parity");
            Check(RolltheSpire2.Ui.Shell.CrystalSphereGuidance.Matches(replay.ExportCrystal(), partial.Branches[0].Snapshot, false),
                "Guide accepts actual native partial state without mutation");
            Check(!RolltheSpire2.Ui.Shell.CrystalSphereGuidance.Matches(snapshot, partial.Branches[0].Snapshot, false),
                "Guide rejects a different reveal prefix");


            // Use an actual generated board, reveal all reward cells in a
            // controlled terminal fixture, then compare vanilla two-stage
            // ToReward/GenerateWithoutOffering against the existing Runtime.
            var nativeRng = new MegaCrit.Sts2.Core.Random.Rng(new MegaCrit.Sts2.Core.Saves.SerializableRng { state0 = snapshot.EventRng.S0, state1 = snapshot.EventRng.S1, state2 = snapshot.EventRng.S2, state3 = snapshot.EventRng.S3, counter = snapshot.EventRng.Calls });
            var goodItems = board.Items.Where(i => i.IsGood).ToList();
            var plans = goodItems.Select(i => i.ToReward(player, nativeRng)).OfType<Reward>().ToList();
            var nativeRewards = new RewardsSet(player).WithCustomRewards(plans);
            nativeRewards.GenerateWithoutOffering().GetAwaiter().GetResult();
            var ordered = goodItems.Select(i => board.Items.ToList().IndexOf(i)).ToImmutableArray();
            var hidden = Enumerable.Repeat(false, 121).ToArray();
            int empty = Enumerable.Range(0, 121).First(i => snapshot.Occupancy[i] < 0); hidden[empty] = true;
            var terminal = snapshot with { Remaining = 1, Hidden = hidden.ToImmutableArray(), Revealed = ordered,
                Items = snapshot.Items.Select((i,n) => i with { Revealed = ordered.Contains(n) }).ToImmutableArray() };
            var generated = PredictorRun.FromCrystal(terminal);
            generated.Submit(generated.Request!, new SelectCrystalTool(PredictorCrystalTool.Small));
            generated.Submit(generated.Request!, new RevealCrystalCell(empty / 11, empty % 11));
            var actualCards = nativeRewards.Rewards.OfType<CardReward>().Select(r => string.Join(',', r.Cards.Select(c => c.Id.Entry))).ToArray();
            var predictedCards = generated.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).Select(r => string.Join(',', r.Cards.Select(c => c.Key.Entry))).ToArray();
            Check(actualCards.SequenceEqual(predictedCards), "Vanilla ToReward potion prefix then card Populate parity");
            CheckRewardTable(snapshot, generated);
            PredictorCrystalSnapshot CaptureHookedSource()
            {
                foreach (string key in new[] { "PRISMATIC_GEM", "DINGY_RUG", "SILKEN_TRESS", "SILVER_CRUCIBLE", "WING_CHARM" })
                    player.AddRelicInternal(ModelDb.GetById<RelicModel>(new ModelId("RELIC", key)).ToMutable(), silent: true);
                return CrystalSphereLiveCapture.CaptureScene(native, player, ev, board, "0.111.0").Branches[0].Snapshot;
            }
            if(conditionalFocus)
            {
                // Preserve the normal native setup, partial click, populated
                // rewards and the two explicit live RNG probes below. The skipped
                // middle section only simulates detached snapshots.
                ev.Rng.NextInt(2);
                player.PlayerRng.Rewards.NextInt(2);
                CheckInformationPruning(CaptureHookedSource(),phaseOnly:true,focus:true);
                Console.WriteLine("CRYSTAL_CONDITIONAL_FOCUS_PASS phase-interleaving;capturedHooks=True;bound=off;not-full-native-suite");
                return 0;
            }
            var rewardCards = generated.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).ToArray();
            var click = ImmutableArray.Create(new PredictorCrystalStep(empty / 11, empty % 11, PredictorCrystalTool.Small));
            Check(PredictorCrystalSearch.Verify(terminal, click, [rewardCards[0].Cards[0].Key, rewardCards[0].Cards[1].Key]) == null,
                "Two cards from the same three-choice reward cannot both be taken");
            Check(PredictorCrystalSearch.Verify(terminal, click, [rewardCards[0].Cards[0].Key, rewardCards[1].Cards[0].Key]) != null,
                "Two separate rewards have one executable take plan");
            foreach (var branch in before.Branches.Concat(partial.Branches))
            {
                var fixture = PredictorRun.FromCrystal(branch.Snapshot);
                var intended = new List<PredictorCrystalStep>();
                foreach (var item in branch.Snapshot.Items.Where(i => i.Kind.StartsWith("CARD_") && i.X >= 0))
                {
                    if (fixture.Phase != PredictorPhase.EventChoice) break;
                    var current = fixture.ExportCrystal();
                    var corner = new[] { (item.X, item.Y), (item.X + 1, item.Y + 1), (item.X, item.Y + 1), (item.X + 1, item.Y) }
                        .FirstOrDefault(p => current.Hidden[p.Item1 * 11 + p.Item2], (-1, -1));
                    if (corner.Item1 < 0) continue;
                    intended.Add(new(corner.Item1, corner.Item2, PredictorCrystalTool.Big));
                    fixture.Submit(fixture.Request!, new SelectCrystalTool(PredictorCrystalTool.Big));
                    fixture.Submit(fixture.Request!, new RevealCrystalCell(corner.Item1, corner.Item2));
                }
                while (fixture.Phase == PredictorPhase.EventChoice)
                {
                    var current = fixture.ExportCrystal(); int next = current.Hidden.IndexOf(true);
                    intended.Add(new(next / 11, next % 11, PredictorCrystalTool.Small));
                    fixture.Submit(fixture.Request!, new SelectCrystalTool(PredictorCrystalTool.Small));
                    fixture.Submit(fixture.Request!, new RevealCrystalCell(next / 11, next % 11));
                }
                var target = fixture.Rewards.First(r => r.Kind == PredictorRewardKind.Card).Cards[0].Key;
                var solved = PredictorCrystalSearch.Solve(branch.Snapshot, [target], TimeSpan.FromSeconds(3));
                Check(solved.Status == "Found" && PredictorCrystalSearch.Verify(branch.Snapshot, solved.Steps, [target]) != null,
                    "Actual " + branch.Mode + " board solver witness: " + solved.Status + "/" + solved.Examined);
                var groups = fixture.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).ToArray();
                if (groups.Length >= 2)
                {
                    var pair = ImmutableArray.Create(groups[0].Cards[0].Key, groups[1].Cards[0].Key);
                    var pairSolution = PredictorCrystalSearch.Solve(branch.Snapshot, pair, TimeSpan.FromSeconds(5));
                    Check(pairSolution.Status == "Found" && PredictorCrystalSearch.Verify(branch.Snapshot, pairSolution.Steps, pair) != null,
                        "Actual " + branch.Mode + " two-target witness: " + pairSolution.Status);
                    Console.WriteLine("CRYSTAL_PAIR_WITNESS " + branch.Mode + " steps=" + pairSolution.Steps.Length);
                }
                Console.WriteLine("CRYSTAL_SEARCH_WITNESS " + branch.Mode + " examined=" + solved.Examined + " steps=" + solved.Steps.Length);
            }

            var relicOnly = PredictorCrystalSearch.Solve(before.Branches[1].Snapshot, [], TimeSpan.FromSeconds(5),
                options: new(true, true));
            Check(relicOnly.Status == "Found" && PredictorCrystalSearch.Verify(before.Branches[1].Snapshot,
                relicOnly.Steps, [], options: new(true, true)) != null, "Relic-only target is actually generated without a new curse: " + relicOnly.Status + "/" + relicOnly.Examined);
            var curseIndex = snapshot.Items.FindIndex(i => i.Kind == "CURSE" && i.X >= 0);
            if (curseIndex >= 0)
            {
                var curse = snapshot.Items[curseIndex];
                var curseHidden = terminal.Hidden.ToArray(); curseHidden[empty] = false;
                int lastCurseCell = curse.X * 11 + curse.Y; curseHidden[lastCurseCell] = true;
                var forcedCurse = terminal with { Hidden = curseHidden.ToImmutableArray() };
                var curseClick = ImmutableArray.Create(new PredictorCrystalStep(curse.X, curse.Y, PredictorCrystalTool.Small));
                Check(PredictorCrystalSearch.Verify(forcedCurse, curseClick, [rewardCards[0].Cards[0].Key]) == null,
                    "Avoid curse default rejects an otherwise valid reward witness");
                Check(PredictorCrystalSearch.Verify(forcedCurse, curseClick, [rewardCards[0].Cards[0].Key], options: new(false)) != null,
                    "Disabling curse avoidance permits source replay of the curse");
                var cursedReplay = PredictorRun.FromCrystal(forcedCurse);
                cursedReplay.Submit(cursedReplay.Request!, new SelectCrystalTool(PredictorCrystalTool.Small));
                cursedReplay.Submit(cursedReplay.Request!, new RevealCrystalCell(curse.X, curse.Y));
                CheckRewardTable(forcedCurse, cursedReplay);
            }
            CheckBoundedReachability(snapshot);
            CheckInformationPruning(snapshot);
            CheckExactGold(snapshot);
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_EXPERIMENT")=="1")
                CheckDiscreteBoundWorkloads(before.Branches.Single(b=>b.Mode=="Six").Snapshot);
            Console.WriteLine("CRYSTAL_GUIDANCE_OPTIONS_PASS native progress/deviation; relic-only; avoid-curse on/off");
            foreach (var branch in before.Branches)
            {
                var session=new PredictorCrystalReachability(branch.Snapshot,true);
                var queryWatch=System.Diagnostics.Stopwatch.StartNew();
                var update=session.Advance([],TimeSpan.FromSeconds(2));
                Console.WriteLine($"CRYSTAL_QUERY_NATIVE mode={branch.Mode};selected=0;options={update.Projection.Available.Count};complete={update.Complete};wall={queryWatch.Elapsed.TotalSeconds:F2};states={update.Progress.Examined}");
                Check(update.Projection.Available.Keys.Any(o=>o.Kind==PredictorRewardKind.Card),"Conditional discovery provides witnessed native cards");
                var first=update.Projection.Available.Keys.Where(o=>o.Kind==PredictorRewardKind.Card)
                    .OrderByDescending(o=>o.Key.Entry=="BARRICADE").ThenBy(o=>o.Key.Entry,StringComparer.Ordinal).First();
                queryWatch.Restart();update=session.Advance([first],TimeSpan.FromSeconds(2));
                Check(update.Projection.SelectedPlan!=null,"Native conditional search preserves selected target");
                Console.WriteLine($"CRYSTAL_QUERY_NATIVE mode={branch.Mode};selected=1;options={update.Projection.Available.Count};complete={update.Complete};wall={queryWatch.Elapsed.TotalSeconds:F2};states={update.Progress.Examined};target={first.Key}");
                if(branch.Mode=="Six" && !update.Complete)
                {
                    update=session.Advance([first],TimeSpan.FromSeconds(8));
                    Console.WriteLine($"CRYSTAL_QUERY_CONTINUATION mode=Six;selected=1;options={update.Projection.Available.Count};complete={update.Complete};wall={queryWatch.Elapsed.TotalSeconds:F2};states={update.Progress.Examined};target={first.Key}");
                }
                if(update.Projection.Available.Keys.Where(o=>o.Kind==PredictorRewardKind.Card)
                    .OrderByDescending(o=>o.Key.Entry=="BATTLE_TRANCE").ThenBy(o=>o.Key.Entry,StringComparer.Ordinal).FirstOrDefault() is { } second)
                {
                    queryWatch.Restart();update=session.Advance([first,second],TimeSpan.FromSeconds(2));
                    Check(update.Projection.SelectedPlan!=null,"Native double target remains jointly witnessed");
                    Console.WriteLine($"CRYSTAL_QUERY_NATIVE mode={branch.Mode};selected=2;options={update.Projection.Available.Count};complete={update.Complete};wall={queryWatch.Elapsed.TotalSeconds:F2};states={update.Progress.Examined}");
                }
                var explorer = new PredictorCrystalExplorer(branch.Snapshot, true,
                    useTwoStepCache:Environment.GetEnvironmentVariable("RT2_CRYSTAL_TWO_STEP_AUDIT")=="1");
                var rows = explorer.Advance(TimeSpan.FromSeconds(1));
                var projection = PredictorCrystalExplorer.Project(branch.Snapshot, rows, []);
                Check(projection.Available.Keys.Any(o=>o.Kind==PredictorRewardKind.Card), "Native branch discovery offers verified cards");
                if(branch.Mode=="Six") Check(projection.Available.Keys.Any(o=>o.Kind==PredictorRewardKind.Relic), "Six-click discovery does not starve expensive relic covers");
                Console.WriteLine($"CRYSTAL_DISCOVERY_NATIVE mode={branch.Mode};rows={rows.Length};options={projection.Available.Count};types={string.Join(',',projection.Available.Keys.GroupBy(o=>o.Kind).Select(g=>g.Key+":"+g.Count()))};complete={explorer.Complete};states={explorer.Examined};pruned={explorer.Progress.Pruned};merged={explorer.Progress.Merged}");
                if(branch.Mode=="Six" && Environment.GetEnvironmentVariable("RT2_CRYSTAL_LONG_DISCOVERY")=="1")
                {
                    var all=ImmutableArray.CreateBuilder<CrystalRewardRoute>();all.AddRange(rows);
                    HashSet<CrystalRewardOption>? tenSeconds=null;
                    int round=0;
                    foreach(int seconds in new[]{5,4,10,10,10,10,10,10,10,10,10,10,10})
                    {
                        if(round>=3 && Environment.GetEnvironmentVariable("RT2_CRYSTAL_EXHAUST_CHECK")!="1") break;
                        if(!explorer.Complete) all.AddRange(explorer.Advance(TimeSpan.FromSeconds(seconds)));
                        var available=all.SelectMany(PredictorCrystalExplorer.Options).ToHashSet();
                        if(round++==1) tenSeconds=available;
                        Console.WriteLine($"CRYSTAL_DISCOVERY_CONTINUATION round={round};rows={all.Count};options={available.Count};states={explorer.Examined};pruned={explorer.Progress.Pruned};merged={explorer.Progress.Merged};seconds={explorer.Progress.Seconds:F2};phase={explorer.Phase};complete={explorer.Complete};memoEntries={typeof(PredictorCrystalExplorer).GetField("_frontierEntries",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(explorer)};twoStepEntries={typeof(PredictorCrystalExplorer).GetField("_twoStepEntries",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(explorer)};settlementSeconds={((long)typeof(PredictorCrystalExplorer).GetField("_settlementTicks",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(explorer)!/(double)System.Diagnostics.Stopwatch.Frequency):F2}");
                    }
                    if(explorer.Complete)
                    {
                        var final=PredictorCrystalExplorer.Project(branch.Snapshot,all.ToImmutable(),[]);
                        Check(final.Available.Count==all.SelectMany(PredictorCrystalExplorer.Options).Distinct().Count(),"Completed native index projects all witnessed options");
                        Console.WriteLine($"CRYSTAL_COMPLETE_COVERAGE tenSecondOptions={tenSeconds!.Count};allOptions={final.Available.Count};seconds={explorer.Progress.Seconds:F2};rows={all.Count}");
                    }
                }
                BenchmarkKnownPlan(branch.Snapshot, branch.Mode, false, branch.Mode == "Three" ? 1 : 2);
                if (branch.Mode == "Six") BenchmarkKnownPlan(branch.Snapshot, "SixCardRelic", true, 1);
            }
            var result = PredictorCrystalSearch.Solve(terminal, [rewardCards[0].Cards[0].Key], TimeSpan.FromSeconds(2));
            Check(result.Status == "Found", "Solver returns a verified witness");
            Check(PredictorCrystalSearch.Solve(terminal, [rewardCards[0].Cards[0].Key], TimeSpan.Zero).Status == "Incomplete", "Timeout is not unreachability");
            Check(PredictorCrystalSearch.Solve(terminal, [rewardCards[0].Cards[0].Key, rewardCards[0].Cards[1].Key],
                TimeSpan.FromSeconds(2)).Status == "Unreachable", "Exhausted mutually exclusive targets");
            string rngFingerprint = CrystalSphereLiveCapture.SceneFingerprint(native, player, ev, board);
            ev.Rng.NextInt(2);
            Check(rngFingerprint != CrystalSphereLiveCapture.SceneFingerprint(native, player, ev, board), "Event RNG alone invalidates snapshot");
            rngFingerprint = CrystalSphereLiveCapture.SceneFingerprint(native, player, ev, board);
            player.PlayerRng.Rewards.NextInt(2);
            Check(rngFingerprint != CrystalSphereLiveCapture.SceneFingerprint(native, player, ev, board), "Player RNG alone invalidates snapshot");
            // Actual captured inventory: pool modifiers and one-use reward
            // counters must be read before vanilla mutates them during Populate.
            var withRelics = CaptureHookedSource();
            CheckInformationPruning(withRelics,phaseOnly:true);
            var hookedFixture = terminal with { Context = withRelics.Context, State = withRelics.State, EventRng = withRelics.EventRng };
            var sourceRng = ev.Rng.ToSerializable();
            var hookRng = new MegaCrit.Sts2.Core.Random.Rng(sourceRng);
            var hookRewards = new RewardsSet(player).WithCustomRewards(goodItems.Select(i => i.ToReward(player, hookRng)).OfType<Reward>().ToList());
            hookRewards.GenerateWithoutOffering().GetAwaiter().GetResult();
            var hooked = PredictorRun.FromCrystal(hookedFixture);
            hooked.Submit(hooked.Request!, new SelectCrystalTool(PredictorCrystalTool.Small));
            hooked.Submit(hooked.Request!, new RevealCrystalCell(empty / 11, empty % 11));
            var nativeSignatures = hookRewards.Rewards.OfType<CardReward>().SelectMany(r => r.Cards)
                .Select(c => $"{c.Id.Entry}:{c.CurrentUpgradeLevel}:{c.Enchantment?.Id.Entry}:{c.Enchantment?.Amount}");
            var replaySignatures = hooked.Rewards.Where(r => r.Kind == PredictorRewardKind.Card).SelectMany(r => r.Cards)
                .Select(c => $"{c.Key.Entry}:{c.UpgradeLevel}:{c.Enchantment?.Entry}:{(c.Enchantment == null ? null : (decimal?)c.EnchantmentAmount)}");
            Check(nativeSignatures.SequenceEqual(replaySignatures), "Captured pool modifiers and one-use hooks match native reward cards");
            var niche = player.RunState.Rng.Niche.ToSerializable();
            CheckRewardTable(hookedFixture, hooked);
            var predictedNiche = hooked.Working.Streams.Single(s => s.Stream == PredictorStream.Niche);
            Check(predictedNiche.S0 == niche.state0 && predictedNiche.S1 == niche.state1 && predictedNiche.S2 == niche.state2 &&
                predictedNiche.S3 == niche.state3 && predictedNiche.Calls == niche.counter, "Captured reward hook Niche continuation");
            Console.WriteLine("CRYSTAL_CAPTURED_HOOKS_PASS PrismaticGem/DingyRug/SilkenTress/SilverCrucible/WingCharm identities/upgrades/enchantments/Niche");
            var cancelled = new CancellationToken(true);
            try { PredictorCrystalSearch.Solve(terminal, [rewardCards[0].Cards[0].Key], TimeSpan.FromSeconds(2), cancelled); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            Console.WriteLine("CRYSTAL_ASSISTANT_PASS full read-only native capture; 3/6 board/RNG; partial capture; two-phase native rewards; one/two target take witnesses; timeout/cancellation");
            return 0;
        }
        finally { stateProperty.SetValue(manager, previous); ascensionProperty.SetValue(manager, previousAscension); }
    }
    private static void CheckClickCenters(PredictorCrystalSnapshot source, CrystalSphereMinigame native)
    {
        var first=(from x in Enumerable.Range(0,9) from y in Enumerable.Range(0,10)
            where source.Hidden[x*11+y] && source.Hidden[(x+1)*11+y] && source.Hidden[(x+1)*11+y+1] && source.Hidden[(x+2)*11+y]
            let clear=PredictorCrystalSearch.Cells(x,y,PredictorCrystalTool.Big)
                .Concat(PredictorCrystalSearch.Cells(x+2,y,PredictorCrystalTool.Big)).Select(c=>c.X*11+c.Y).ToHashSet()
            where !source.Items.Any(item=>item.Kind=="CURSE" && item.X>=0 && !item.Revealed &&
                Enumerable.Range(item.X,item.Width).SelectMany(a=>Enumerable.Range(item.Y,item.Height).Select(b=>a*11+b))
                    .All(i=>!source.Hidden[i] || clear.Contains(i)))
            select (X:x,Y:y)).First();
        var run=PredictorRun.FromCrystal(source);
        run.Submit(run.Request!,new SelectCrystalTool(PredictorCrystalTool.Big));
        Check(run.Submit(run.Request!,new RevealCrystalCell(first.X,first.Y))==PredictorInputResult.Accepted,"Initial hidden Big center is legal");
        native.SetTool(CrystalSphereMinigame.CrystalSphereToolType.Big);
        native.CellClicked(native.cells[first.X,first.Y]).GetAwaiter().GetResult();
        foreach(var point in new[]{first,(first.X+1,first.Y),(first.X+1,first.Y+1)})
        {
            Check(!native.cells[point.Item1,point.Item2].IsHidden,"Vanilla disables same/adjacent center after Big reveal");
            int before=run.ExportCrystal().Remaining;
            Check(run.Submit(run.Request!,new RevealCrystalCell(point.Item1,point.Item2))==PredictorInputResult.Invalid &&
                run.ExportCrystal().Remaining==before,"Revealed centers cannot be used as RNG padding or consume a click");
        }
        Check(native.cells[first.X+2,first.Y].IsHidden,"A center two cells away remains hidden despite overlapping areas");
        Check(run.Submit(run.Request!,new RevealCrystalCell(first.X+2,first.Y))==PredictorInputResult.Accepted,"Overlapping Big areas with hidden centers are legal");
        native.CellClicked(native.cells[first.X+2,first.Y]).GetAwaiter().GetResult();
        Check(run.ExportCrystal().Hidden.SequenceEqual(native.cells.Cast<CrystalSphereCell>().Select(c=>c.IsHidden)),"Native overlapping reveal coverage matches Runtime");
        Console.WriteLine("CRYSTAL_CLICK_LEGALITY_PASS same/adjacent/diagonal rejected; overlapping area with hidden center accepted");
    }

    private static void CheckBoundedReachability(PredictorCrystalSnapshot source)
    {
        // Controlled small remaining board: exhaust every native-order tool/cell
        // path. This validates negative pruning independently of the optimized DFS.
        var pending = source.Items.Select((item,i)=>(item,i)).Where(x => x.item.X >= 0 &&
            x.item.Kind is "CARD_RARE" or "CARD_UNCOMMON" or "RELIC").ToArray();
        var fog = Enumerable.Repeat(false,121).ToArray();
        foreach (var x in pending) fog[x.item.X*11+x.item.Y] = true;
        foreach (int i in Enumerable.Range(0,121).Where(i=>source.Hidden[i] && source.Occupancy[i]<0).Take(2)) fog[i]=true;
        var ordered = source.Items.Select((item,i)=>(item,i)).Where(x => x.item.X>=0 && x.item.Kind!="CURSE" && !pending.Any(p=>p.i==x.i))
            .SelectMany(x=>Enumerable.Repeat(x.i,x.item.Subscriptions)).ToImmutableArray();
        var tiny = source with { Remaining=2, Hidden=fog.ToImmutableArray(), Revealed=ordered,
            Items=source.Items.Select((item,i)=>item with { Revealed=ordered.Contains(i) }).ToImmutableArray() };
        var paths = new List<ImmutableArray<PredictorCrystalStep>>();
        void Visit(ImmutableArray<PredictorCrystalStep> path)
        {
            var run = PredictorRun.FromCrystal(tiny);
            foreach(var step in path) { run.Submit(run.Request!,new SelectCrystalTool(step.Tool)); run.Submit(run.Request!,new RevealCrystalCell(step.X,step.Y)); }
            if(path.Length==2) { CheckRewardTable(tiny,run); paths.Add(path); return; }
            var current=run.ExportCrystal();
            foreach(int i in Enumerable.Range(0,121).Where(i=>current.Hidden[i]))
                foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}) Visit(path.Add(new(i/11,i%11,tool)));
        }
        Visit([]);
        var pool = source.Context.Catalog!.CharacterCards.Where(c => c.Rarity is RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Rare or
            RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon).Take(6).Select(c=>c.CardKey).ToArray();
        int queries=0;
        foreach (var target in pool)
            foreach (bool relic in new[]{false,true})
            {
                var options=new PredictorCrystalOptions(true,relic);
                bool expected=paths.Any(path=>PredictorCrystalSearch.Verify(tiny,path,[target],options:options)!=null);
                var solved=PredictorCrystalSearch.Solve(tiny,[target],TimeSpan.FromSeconds(2),options:options);
                Check(solved.Status==(expected?"Found":"Unreachable"), "Exact remaining-board reachability agrees with full enumeration");queries++;
            }

        var index = new PredictorCrystalExplorer(tiny, true);
        Check(index.Advance(TimeSpan.Zero).IsEmpty && !index.Complete, "An untouched discovery index is not complete");
        var accumulated=ImmutableArray.CreateBuilder<CrystalRewardRoute>();
        var deadline=System.Diagnostics.Stopwatch.StartNew();
        while(!index.Complete && deadline.Elapsed<TimeSpan.FromSeconds(3)) accumulated.AddRange(index.Advance(TimeSpan.FromMilliseconds(1)));
        var rows=accumulated.ToImmutable();
        Check(index.Complete, "Bounded board discovery exhausts all paths");
        var truth=paths.Select(path=>new CrystalRewardRoute(path,PredictorCrystalExplorer.Replay(tiny,path).Rewards)).ToImmutableArray();
        var candidates=truth.SelectMany(PredictorCrystalExplorer.Options).Distinct().ToArray();
        var firstTarget=candidates.First();
        var firstPlan=new PredictorCrystalExplorer(tiny,true,reachability:new CrystalReachabilityTable(tiny),selected:[firstTarget],stopAfterPlan:true);
        var firstWatch=System.Diagnostics.Stopwatch.StartNew();
        while(!firstPlan.Complete && firstWatch.Elapsed<TimeSpan.FromSeconds(2)) firstPlan.Advance(TimeSpan.FromMilliseconds(10));
        Check(firstPlan.QueryProjection.SelectedPlan!=null && !firstPlan.ExcludedCandidates.Any(option=>truth.Any(row=>
            PredictorCrystalExplorer.VerifySelection(tiny,row,[firstTarget,option])!=null)),"Finding one plan does not certify unwitnessed byproducts impossible");
        var empty=PredictorCrystalExplorer.Project(tiny,rows,[]);
        Check(candidates.ToHashSet().SetEquals(empty.Available.Keys), "Discovery lists every witnessed card/potion/relic option");
        foreach(var selected in candidates.Take(12))
        {
            var projection=PredictorCrystalExplorer.Project(tiny,rows,[selected]);
            var expected=candidates.Where(option=>option!=selected && truth.Any(row=>PredictorCrystalExplorer.VerifySelection(tiny,row,[selected,option])!=null)).ToHashSet();
            Check(expected.SetEquals(projection.Available.Keys), "Selecting a reward preserves precisely joint-reachable options");
            Check(projection.SelectedPlan!=null, "Every selectable target retains its witness");
        }
        var reset=PredictorCrystalExplorer.Project(tiny,rows,[]);
        Check(reset.Available.Keys.ToHashSet().SetEquals(empty.Available.Keys), "Deselect restores compatible options without rerolling");
        foreach(int workers in new[]{1,4})
        {
        var session=new PredictorCrystalReachability(tiny,true,potionScenarioBound:true,workers:workers,targetDirected:workers==4);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        bool rejected=false;
        try { session.Advance([],TimeSpan.FromMilliseconds(1),cancelled.Token); }
        catch(OperationCanceledException) { rejected=true; }
        Check(rejected,"Cancelled query must not be reported complete");
        CrystalReachabilityUpdate Query(ImmutableArray<CrystalRewardOption> selected)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var update=session.Advance(selected,TimeSpan.Zero);
            while(!update.Complete && timer.Elapsed<TimeSpan.FromSeconds(5))
                update=session.Advance(selected,TimeSpan.FromMilliseconds(10));
            Check(update.Complete,"Conditional query exhausts bounded board");
            var expected=candidates.Where(option=>!selected.Contains(option) && truth.Any(row=>
                PredictorCrystalExplorer.VerifySelection(tiny,row,selected.Add(option))!=null)).ToHashSet();
            Check(expected.SetEquals(update.Projection.Available.Keys),"Conditional query agrees with raw legal paths");
            return update;
        }
        Check(!session.Advance([],TimeSpan.Zero).Complete,"Lookup alone does not prove geometry reachability");
        Query([]);
        var knownTarget=candidates.First();
        var known=session.ProjectKnown([knownTarget]);
        Check(known.SelectedPlan!=null,"Retarget can reuse a known plan without constructing a search frontier");
        var knownTruth=candidates.Where(option=>option!=knownTarget && truth.Any(row=>
            PredictorCrystalExplorer.VerifySelection(tiny,row,[knownTarget,option])!=null)).ToHashSet();
        Check(known.Available.Keys.All(knownTruth.Contains),"Warm projection never preserves incompatible old candidates");
        long replayBefore=session.ProofReplays;
        var warmTimer=System.Diagnostics.Stopwatch.StartNew();
        int publications=0;
        var again=session.ProjectKnown([knownTarget],publish:_=>publications++);
        Check(again.Available.Keys.ToHashSet().SetEquals(known.Available.Keys) && session.ProofReplays==replayBefore,
            "Repeated known projection performs no new Runtime replay");
        Check(publications>0 && session.PeekKnown([knownTarget]).SelectedPlan!=null,"Cached immutable projection is immediately available");
        Console.WriteLine($"CRYSTAL_REUSE_PASS workers={workers};repeatProjectionMs={warmTimer.Elapsed.TotalMilliseconds:F3};extraReplays={session.ProofReplays-replayBefore};cacheHits={session.ProofCacheHits}");
        foreach(var selected in candidates)
        {
            var update=Query([selected]);
            Check(update.Projection.SelectedPlan!=null,"Conditional target keeps a typed replay witness");
            if(update.Projection.Available.Keys.FirstOrDefault() is { } second) Query([selected,second]);
            Query([selected]);
        }
        replayBefore=session.ProofReplays;
        var restored=session.Advance([],TimeSpan.Zero);
        Check(restored.Complete && session.ProofReplays==replayBefore,"Completed query survives cold-frontier eviction without replay");
        Query([]);
        Console.WriteLine($"CRYSTAL_CONDITIONAL_ORACLE_PASS workers={workers};options={candidates.Length};empty/select/extend/deselect/cancel=true");
        }
        var broken=new PredictorCrystalExplorer(tiny with { Context=tiny.Context with { PotionPool=[] } },true);
        for(int attempt=0;attempt<2;attempt++)
        {
            bool failed=false;
            try { broken.Advance(TimeSpan.FromSeconds(1)); } catch(InvalidOperationException) { failed=true; }
            Check(failed && !broken.Complete, "A settlement failure cannot become an exhausted discovery proof on retry");
        }
        Console.WriteLine($"CRYSTAL_DISCOVERY_PASS rows={rows.Length};options={candidates.Length};complete/exhaustive/joint/deselect=true");
        Console.WriteLine($"CRYSTAL_TABLE_BOUND_PASS paths={paths.Count};queries={queries};state/rewards/eventRng=true");
    }

    private static void CheckInformationPruning(PredictorCrystalSnapshot source, bool phaseOnly=false, bool focus=false)
    {
        PredictorCrystalSnapshot Board(int remaining, PredictorCrystalItem[] items, params (int X,int Y)[] hidden)
        {
            var fog=new bool[121]; foreach(var cell in hidden) fog[cell.X*11+cell.Y]=true;
            var occupancy=Enumerable.Repeat(-1,121).ToArray();
            for(int i=0;i<items.Length;i++)
                for(int x=items[i].X;x<items[i].X+items[i].Width;x++)
                    for(int y=items[i].Y;y<items[i].Y+items[i].Height;y++)
                    { Check(occupancy[x*11+y]<0,"Controlled items do not overlap");occupancy[x*11+y]=i; }
            return source with { Remaining=remaining,Hidden=fog.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),
                Items=items.ToImmutableArray(),Revealed=[] };
        }
        PredictorCrystalItem Item(string kind,int width,int x,int y)=>new(kind,width,width,x,y,1,false);
        if(!phaseOnly)
        {
            // Small controlled board, real generation/take logic. Three real
            // common attacks make the same identity appear in both groups;
            // Act 3 upgrade rolls can produce base and upgraded copies.
            var attacks=source.Context.Catalog!.CharacterCards.Where(c=>c.Rarity.ToString()=="Common" && c.CardType.ToString()=="Attack" && c.CanUpgrade).Take(3).ToImmutableArray();
            Check(attacks.Length==3,"Variant fixture has three real upgradable attacks");
            var variantBase=Board(2,[Item("CARD_COMMON",2,0,0),Item("CARD_COMMON",2,8,8)],(0,0),(8,8)) with {
                Context=source.Context with { Modifiers=[],Catalog=source.Context.Catalog with { CharacterCards=attacks } },
                State=source.State with { Relics=[],Position=source.State.Position with { Act=3 } }
            };
            ImmutableArray<PredictorCrystalStep> variantSteps=[new(0,0,PredictorCrystalTool.Small),new(8,8,PredictorCrystalTool.Small)];
            PredictorCrystalSnapshot variantBoard=variantBase;
            CrystalRewardRoute? variantRow=null;CrystalRewardOption? baseOption=null,upOption=null;
            for(int shift=0;shift<64 && baseOption==null;shift++)
            {
                var rng=variantBase.EventRng.Restore();rng.Advance(shift);
                variantBoard=variantBase with { EventRng=PredictorStreamState.Capture(PredictorStream.Rewards,rng) };
                var result=PredictorCrystalExplorer.Replay(variantBoard,variantSteps);
                variantRow=new(variantSteps,result.Rewards);
                var pair=PredictorCrystalExplorer.Options(variantRow).GroupBy(o=>o.Key)
                    .FirstOrDefault(g=>g.Any(o=>o.UpgradeLevel==0) && g.Any(o=>o.UpgradeLevel==1));
                if(pair!=null) { baseOption=pair.Single(o=>o.UpgradeLevel==0);upOption=pair.Single(o=>o.UpgradeLevel==1); }
            }
            Check(baseOption!=null && upOption!=null,"One real reward path offers A and A+ in separate groups");
            var variantProjection=PredictorCrystalExplorer.Project(variantBoard,[variantRow!],[]);
            Check(variantProjection.Available.ContainsKey(baseOption!) && variantProjection.Available.ContainsKey(upOption!),"Base/upgraded candidates remain separate");
            var both=PredictorCrystalExplorer.VerifySelection(variantBoard,variantRow!,[baseOption!,upOption!]);
            Check(both!=null && both.Takes.Select(t=>t.UpgradeLevel).SequenceEqual(new int?[]{0,1}) && both.Takes.Select(t=>t.RewardIndex).Distinct().Count()==2,
                "A plus A+ requires two actual card takes from distinct rewards");
            var offered=variantRow!.Rewards.First(r=>r.Kind==PredictorRewardKind.Card);
            var card=offered.Cards.First(c=>c.Key==baseOption!.Key);
            Check(PredictorCrystalExplorer.MatchCardOptions([offered with { Cards=[card with { UpgradeLevel=0 },card with { UpgradeLevel=1 }] }],
                [baseOption!,upOption!])==null,"One three-choice group never satisfies both versions");
            var variantSession=new PredictorCrystalReachability(variantBoard,true,workers:4,potionScenarioBound:true,targetDirected:true);
            variantSession.AddWitnesses([variantRow]);
            Check(variantSession.ProjectKnown([baseOption!]).SelectedPlan!.Takes[0].UpgradeLevel==0 &&
                variantSession.ProjectKnown([upOption!]).SelectedPlan!.Takes[0].UpgradeLevel==1 &&
                variantSession.PeekKnown([baseOption!]).SelectedPlan!.Takes[0].UpgradeLevel==0,"Retarget caches do not alias A with A+");
            var variantUpdate=variantSession.Advance([baseOption!],TimeSpan.FromMilliseconds(20));
            var variantTimer=System.Diagnostics.Stopwatch.StartNew();
            while(!variantUpdate.Complete && variantTimer.Elapsed<TimeSpan.FromSeconds(3)) variantUpdate=variantSession.Advance([baseOption!],TimeSpan.FromMilliseconds(20));
            Check(variantUpdate.Complete && variantUpdate.Projection.Available.ContainsKey(upOption!),"Conditional search preserves upgraded addition after selecting base card");
            var egg=new PredictorRelic(variantBoard.State.NextInstanceId,new("RELIC","MOLTEN_EGG"),0);
            var eggBoard=variantBoard with { State=variantBoard.State with { NextInstanceId=egg.Id+1,Relics=[egg] } };
            var eggRow=new CrystalRewardRoute(variantSteps,PredictorCrystalExplorer.Replay(eggBoard,variantSteps).Rewards);
            Check(PredictorCrystalExplorer.Options(eggRow).Where(o=>o.Kind==PredictorRewardKind.Card).All(o=>o.UpgradeLevel==1) &&
                PredictorCrystalExplorer.VerifySelection(eggBoard,eggRow,[baseOption!])==null &&
                PredictorCrystalExplorer.VerifySelection(eggBoard,eggRow,[upOption!])!=null,"Egg offers only upgraded variants; base target cannot use its proof");
            var silverBoard=variantBoard with { State=variantBoard.State with { NextInstanceId=egg.Id+1,Relics=[egg with {
                Key=new("RELIC","SILVER_CRUCIBLE"),RewardUpgradeUses=2 }] } };
            var silverRow=new CrystalRewardRoute(variantSteps,PredictorCrystalExplorer.Replay(silverBoard,variantSteps).Rewards);
            var silverTable=new CrystalReachabilityTable(silverBoard);
            Check(PredictorCrystalExplorer.Options(silverRow).All(silverTable.Candidates.Contains),"Variant bounds include Silver Crucible expiring between groups");
            Console.WriteLine("CRYSTAL_CARD_VARIANTS_PASS base/upgrade=true;jointDistinctGroups=true;cacheSeparated=true;conditional=true;egg=true;silverExpiry=true");

            var goldBoard=Board(6,[Item("CARD_COMMON",2,0,0),Item("CARD_UNCOMMON",2,0,4),Item("CARD_RARE",2,0,8),
                Item("POTION_COMMON",1,4,0),Item("POTION_RARE",1,4,4),Item("GOLD_SMALL",1,8,8),Item("CURSE",1,8,9)],
                (0,0),(0,4),(0,8),(4,0),(4,4),(8,8),(8,9),(10,0));
            var goldPrefix=new[]{(0,0),(0,4),(0,8),(4,0),(4,4)}.Select(p=>new PredictorCrystalStep(p.Item1,p.Item2,PredictorCrystalTool.Small)).ToImmutableArray();
            var paddedPath=goldPrefix.Add(new(10,0,PredictorCrystalTool.Small));
            var baseRun=PredictorCrystalExplorer.Replay(goldBoard,paddedPath);
            var goals=baseRun.Rewards.Where(r=>r.Kind is PredictorRewardKind.Card or PredictorRewardKind.Potion)
                .Select(r=>new CrystalRewardOption(r.Kind,r.Kind==PredictorRewardKind.Card?r.Cards[0].Key:r.Key)).ToImmutableArray();
            var baseProof=PredictorCrystalExplorer.VerifySelection(goldBoard,new(paddedPath,baseRun.Rewards),goals)!;
            Check(goals.Length==5 && baseProof!=null,"Gold regression has five compatible goals and a blank sixth click");
            var improved=ImproveGoldForChecks(goldBoard,new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,baseProof,0),goals,true);
            Check(improved.Gold==10 && improved.SelectedPlan!=null,"Gold search retains five targets and adds ten gold");
            var improvedPlan=improved.SelectedPlan!;
            var improvedRun=PredictorCrystalExplorer.Replay(goldBoard,improvedPlan.Steps);
            Check(!improvedRun.ExportCrystal().Revealed.Any(i=>goldBoard.Items[i].Kind=="CURSE") &&
                PredictorCrystalExplorer.VerifySelection(goldBoard,new(improvedPlan.Steps,improvedRun.Rewards),goals)!=null,
                "Gold finish retains all five targets and avoids adjacent curse");
            var joint=new PredictorCrystalReachability(goldBoard,true,workers:4,potionScenarioBound:true);
            joint.AddWitnesses([new(paddedPath,baseRun.Rewards)]);
            joint.ProjectKnown(goals);
            var published=joint.AdvanceGold(goals,TimeSpan.FromSeconds(5));
            Check(published.Gold==10 && joint.PeekKnown(goals).Gold==10,"Cached/UI projection contains the improved plan value");

            var fortyBoard=Board(6,[Item("CARD_COMMON",2,0,0),Item("CARD_UNCOMMON",2,0,4),Item("CARD_RARE",2,0,8),
                Item("POTION_COMMON",1,4,0),Item("POTION_RARE",1,4,4),Item("GOLD_SMALL",1,8,8),Item("GOLD_BIG",2,9,8) with { Height=1 }],
                (0,0),(0,4),(0,8),(4,0),(4,4),(8,8),(9,8),(10,8),(10,0));
            var forty=ImproveGoldForChecks(fortyBoard,new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,baseProof,0),goals,true);
            Check(forty.Gold==40 && forty.SelectedPlan!=null,"Exact gold search collects ten and thirty gold while retaining five targets");
            var fortyRun=PredictorCrystalExplorer.Replay(fortyBoard,forty.SelectedPlan!.Steps);
            Check(PredictorCrystalExplorer.VerifySelection(fortyBoard,new(forty.SelectedPlan.Steps,fortyRun.Rewards),goals)!=null,
                "Forty-gold final reveal preserves all five targets");

            // Four productive reveals leave two padding clicks. The two gold
            // piles are too far apart for one Big reveal, so last-click polish
            // alone cannot collect both.
            var tailBoard=Board(6,[Item("CARD_COMMON",2,0,0),Item("CARD_UNCOMMON",2,0,4),
                Item("POTION_COMMON",1,4,0),Item("POTION_RARE",1,4,4),
                Item("GOLD_SMALL",1,8,0),Item("GOLD_BIG",2,8,8) with { Height=1 }],
                (0,0),(0,4),(4,0),(4,4),(8,0),(8,8),(9,8),(10,3),(10,5));
            ImmutableArray<PredictorCrystalStep> tailPath=[new(0,0,PredictorCrystalTool.Small),new(0,4,PredictorCrystalTool.Small),
                new(4,0,PredictorCrystalTool.Small),new(4,4,PredictorCrystalTool.Small),
                new(10,3,PredictorCrystalTool.Small),new(10,5,PredictorCrystalTool.Small)];
            var tailRun=PredictorCrystalExplorer.Replay(tailBoard,tailPath);
            var tailGoals=tailRun.Rewards.Where(r=>r.Kind is PredictorRewardKind.Card or PredictorRewardKind.Potion)
                .Select(r=>new CrystalRewardOption(r.Kind,r.Kind==PredictorRewardKind.Card?r.Cards[0].Key:r.Key)).ToImmutableArray();
            var tailProof=PredictorCrystalExplorer.VerifySelection(tailBoard,new(tailPath,tailRun.Rewards),tailGoals)!;
            var tail=ImproveGoldForChecks(tailBoard,new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,tailProof,0),tailGoals,true);
            Check(tail.Gold==40 && tail.SelectedPlan!=null,"Two spare reveals collect separated gold piles while retaining all targets");
            var tailResult=PredictorCrystalExplorer.Replay(tailBoard,tail.SelectedPlan!.Steps);
            Check(PredictorCrystalExplorer.VerifySelection(tailBoard,new(tail.SelectedPlan.Steps,tailResult.Rewards),tailGoals)!=null,
                "Two-click gold tail preserves all targets under real reward settlement");

            // Only the bridge's Big click can finish both; native neighbor order
            // makes that gold precede the card, changing its reward RNG offset.
            var shifted=Board(1,[Item("CARD_RARE",2,4,5),Item("GOLD_SMALL",1,6,8)],(5,6),(6,7),(6,8));
            ImmutableArray<PredictorCrystalStep> cardOnly=[new(5,6,PredictorCrystalTool.Small)];
            var cardRun=PredictorCrystalExplorer.Replay(shifted,cardOnly);
            var moneyRun=PredictorCrystalExplorer.Replay(shifted,[new(6,7,PredictorCrystalTool.Big)]);
            var lost=cardRun.Rewards.Single(r=>r.Kind==PredictorRewardKind.Card).Cards.Select(c=>c.Key)
                .Except(moneyRun.Rewards.Single(r=>r.Kind==PredictorRewardKind.Card).Cards.Select(c=>c.Key)).First();
            ImmutableArray<CrystalRewardOption> lostGoal=[new(PredictorRewardKind.Card,lost)];
            var cardProof=PredictorCrystalExplorer.VerifySelection(shifted,new(cardOnly,cardRun.Rewards),lostGoal)!;
            var retained=ImproveGoldForChecks(shifted,new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,cardProof,0),lostGoal,true);
            Check(retained.Gold==0 && retained.SelectedPlan!.Steps.SequenceEqual(cardOnly),"Do not buy extra gold by losing the selected card");
            Console.WriteLine("CRYSTAL_GOLD_FINISH_PASS fiveTargets=true;blankSixth=10gold;bigSixth=40gold;twoSpare=40gold;curseAvoided=true;rngShiftRejected=true;cachedProjection=true");
        }
        string Signature(PredictorCrystalSnapshot board, ImmutableArray<int> order)=>string.Join(',',order.Select(i=>
            board.Items[i].Kind=="CURSE"?"CURSE:"+i:board.Items[i].Kind));
        // Compare complete generated payloads/continuation, not the production
        // history equivalence we are testing. Reward/card instance numbering and
        // display order may change between equivalent phase interleavings.
        string Outcome(PredictorRun run)=>JsonSerializer.Serialize(new {
            run.Working,run.ExportCrystal().EventRng,
            Rewards=run.Rewards.Select(r=>JsonSerializer.Serialize(r with {
                Id=0,ContentInstanceId=null,Cards=r.Cards.Select(c=>c with { Id=0 }).ToImmutableArray()
            })).OrderBy(s=>s,StringComparer.Ordinal).ToArray()
        });
        var phaseBoard=Board(3,[Item("POTION_COMMON",1,2,2),Item("CARD_RARE",2,5,5),Item("GOLD_SMALL",1,8,8)],
            (2,2),(5,5),(8,8),(0,3),(0,5),(0,7));
        var curseBoard=Board(3,[Item("CURSE",2,2,2) with { Subscriptions=2 },Item("CURSE",2,5,5),Item("CARD_RARE",2,8,8)],
            (2,2),(5,5),(8,8),(0,3),(0,5),(0,7));
        var partial=PredictorRun.FromCrystal(curseBoard);
        Check(partial.Submit(partial.Request!,new SelectCrystalTool(PredictorCrystalTool.Small))==PredictorInputResult.Accepted,"Partial prefix tool");
        Check(partial.Submit(partial.Request!,new RevealCrystalCell(2,2))==PredictorInputResult.Accepted,"Partial prefix curse");
        var prefixTail=Board(3,[Item("CARD_COMMON",2,2,2),Item("RELIC",4,6,6)],
            (6,6),(9,9),(0,3),(0,5),(0,7));
        prefixTail=prefixTail with { Revealed=[0],Items=prefixTail.Items.SetItem(0,prefixTail.Items[0] with { Revealed=true }) };
        var completedCards=Board(1,[Item("CARD_COMMON",2,2,2),Item("CARD_RARE",2,6,2),
            Item("POTION_COMMON",1,5,7) with { Height=3 }],(5,7),(0,5));
        completedCards=completedCards with { Revealed=[0,1],Items=completedCards.Items
            .SetItem(0,completedCards.Items[0] with { Revealed=true }).SetItem(1,completedCards.Items[1] with { Revealed=true }) };
        var potionPrefix=PredictorRun.FromCrystal(completedCards with { Remaining=2 });
        potionPrefix.Submit(potionPrefix.Request!,new SelectCrystalTool(PredictorCrystalTool.Small));
        potionPrefix.Submit(potionPrefix.Request!,new RevealCrystalCell(5,7));
        var fixtures=new[]{
            (Name:"island-separated",Board:Board(3,[Item("GOLD_SMALL",1,0,0),Item("GOLD_SMALL",1,1,1),Item("CARD_RARE",2,8,8)],
                (0,0),(1,1),(8,8),(10,0)),Avoid:true),
            (Name:"island-cross-preparation",Board:Board(4,[Item("CARD_RARE",2,2,2),Item("CARD_UNCOMMON",2,2,5)],
                (2,2),(3,3),(2,5),(3,6),(2,4)),Avoid:true),
            (Name:"island-six-interleaving",Board:Board(6,[Item("RELIC",4,3,3),Item("CARD_RARE",2,8,8)],
                (3,3),(3,6),(6,3),(6,6),(9,9),(0,0)),Avoid:true),
            (Name:"island-zero-subscription-curse",Board:Board(2,[Item("CURSE",2,2,2) with { Subscriptions=0 },
                Item("CARD_RARE",2,6,6)],(2,2),(6,6),(0,5)),Avoid:false),
            (Name:"query-prefix-tail",Board:prefixTail,Avoid:true),
            (Name:"preparation",Board:Board(4,[Item("RELIC",4,3,3)],(3,3),(3,6),(6,3),(6,6)),Avoid:true),
            (Name:"small-required",Board:Board(1,[Item("CARD_RARE",2,5,5),Item("CURSE",2,5,7)],(5,6),(5,7)),Avoid:true),
            (Name:"prefix-padding",Board:Board(2,[Item("CARD_RARE",2,6,6)],(6,6),(5,5)),Avoid:true),
            (Name:"doomed-item",Board:Board(2,[Item("RELIC",4,3,3),Item("CARD_RARE",2,9,9)],
                (3,3),(3,6),(6,3),(6,6),(9,9),(0,5)),Avoid:true),
            (Name:"ordered-partial",Board:Board(3,[Item("CARD_RARE",2,2,2),Item("CARD_UNCOMMON",2,2,5)],
                (2,3),(2,5),(2,4),(0,5)),Avoid:true),
            (Name:"curse-allowed",Board:Board(3,[Item("CARD_RARE",2,2,2),Item("CURSE",2,5,5)],
                (2,3),(5,5),(4,4),(0,5)),Avoid:false),
            (Name:"future-frontier",Board:Board(3,[Item("GOLD_SMALL",1,2,2),Item("GOLD_SMALL",1,2,5),Item("CARD_RARE",2,6,6)],
                (2,2),(2,5),(6,6),(6,7),(0,3),(0,5),(0,7)),Avoid:true),
            (Name:"frontier-curse",Board:Board(3,[Item("CARD_RARE",2,2,2),Item("CARD_UNCOMMON",2,2,5),Item("CURSE",2,5,5)],
                (2,3),(2,5),(5,5),(4,4),(0,3),(0,5),(0,7)),Avoid:true),
            (Name:"phase-interleaving",Board:phaseBoard,Avoid:true),
            (Name:"phase-common-p-conflict",Board:completedCards,Avoid:true),
            (Name:"phase-completed-potion-prefix",Board:potionPrefix.ExportCrystal(),Avoid:true),
            (Name:"phase-one-big-card-potion",Board:Board(1,[Item("CARD_RARE",2,4,4),Item("POTION_COMMON",1,6,4) with { Height=3 }],
                (4,4),(4,5),(5,4),(5,5),(6,4),(6,5),(6,6)),Avoid:true),
            (Name:"phase-same-count-different-potions",Board:Board(1,[Item("CARD_RARE",2,4,4),
                Item("POTION_COMMON",1,6,4) with { Height=3 },Item("POTION_COMMON",1,9,0) with { Height=3 }],
                (4,4),(4,5),(5,4),(5,5),(6,4),(6,5),(6,6),(9,0),(9,1),(9,2),(0,3)),Avoid:true),
            (Name:"phase-sozu",Board:phaseBoard with { State=source.State with {
                NextInstanceId=source.State.NextInstanceId+1,
                Relics=source.State.Relics.Add(new(source.State.NextInstanceId,new("RELIC","SOZU"),source.State.Position.Floor)) } },Avoid:true),
            (Name:"phase-card-subscriptions",Board:Board(3,[Item("CARD_RARE",2,2,2) with { Subscriptions=2 },
                Item("CARD_UNCOMMON",2,5,5),Item("GOLD_SMALL",1,8,8)],(2,2),(5,5),(8,8),(0,3),(0,5),(0,7)),Avoid:true),
            (Name:"phase-subscriptions",Board:Board(3,[Item("POTION_COMMON",1,2,2) with { Subscriptions=2 },
                Item("POTION_RARE",1,5,5),Item("CARD_UNCOMMON",2,8,8)],(2,2),(5,5),(8,8),(0,3),(0,5),(0,7)),Avoid:true),
            (Name:"phase-bag",Board:Board(3,[Item("POTION_COMMON",1,2,2),Item("RELIC",4,5,5) with { Subscriptions=2 },
                Item("CARD_RARE",2,9,9)],(2,2),(5,5),(9,9),(0,3),(0,5),(0,7)),Avoid:true),
            // The alternative solver's gap-backtracking counterexample shape:
            // two card blocks and three fillers, with a legal two-Big witness.
            // Verify our subset merge against actual raw manual-click outcomes.
            (Name:"offset-gap-backtracking",Board:Board(2,[Item("CARD_RARE",2,1,1),Item("CARD_UNCOMMON",2,7,7),
                Item("GOLD_SMALL",1,6,7),Item("GOLD_SMALL",1,10,0),Item("GOLD_SMALL",1,0,1)],
                (1,1),(1,2),(2,1),(2,2),(7,7),(7,8),(8,7),(8,8),(6,7),(10,0),(0,1)),Avoid:true),
            (Name:"phase-empty-bag",Board:Board(2,[Item("RELIC",4,5,5),Item("CARD_RARE",2,9,9)],
                (5,5),(9,9),(0,3),(0,5)) with { State=source.State with { PersonalBag=PredictorRelicBag.Empty(false) } },Avoid:true),
            (Name:"physical-curses",Board:curseBoard,Avoid:false),
            (Name:"captured-curse-prefix",Board:partial.ExportCrystal(),Avoid:false),
        };
        foreach(var fixture in fixtures)
        {
            if(focus && fixture.Name!="phase-interleaving") continue;
            if(phaseOnly && !fixture.Name.StartsWith("phase-",StringComparison.Ordinal)) continue;
            var board=fixture.Board; long paths=0;
            if(fixture.Name is "island-separated" or "preparation" or "small-required" or "ordered-partial" or "phase-one-big-card-potion")
            {
                var islands=new PredictorCrystalIslands(board,fixture.Avoid);
                UInt128 fog=0;for(int cell=0;cell<121;cell++) if(board.Hidden[cell]) fog|=(UInt128)1<<cell;
                int islandPaths=0;
                void VerifyMacro(ImmutableArray<PredictorCrystalStep> steps,UInt128 expectedFog,ImmutableArray<int> expectedOrder)
                {
                    // Keep the event transaction open for inspecting geometry;
                    // empty final rewards otherwise close it and dispose its RNG.
                    // Actual-budget reward truth is checked below by raw paths.
                    var run=PredictorRun.FromCrystal(board with { Remaining=board.Remaining+1 });
                    foreach(var step in steps)
                    {
                        Check(run.Submit(run.Request!,new SelectCrystalTool(step.Tool))==PredictorInputResult.Accepted,"Island tool remains legal");
                        Check(run.Submit(run.Request!,new RevealCrystalCell(step.X,step.Y))==PredictorInputResult.Accepted,"Island hidden-center input remains legal");
                    }
                    var actual=run.ExportCrystal();
                    Check(Enumerable.Range(0,121).All(cell=>actual.Hidden[cell]==((expectedFog&((UInt128)1<<cell))!=0)) &&
                        actual.Revealed.SequenceEqual(expectedOrder),"Island full-boundary geometry and callback order agree with Runtime");
                    islandPaths++;
                }
                foreach(var batch in islands.Suggestions(fog,fog,board.Remaining))
                foreach(var move in batch.Take(8))
                {
                    VerifyMacro(move.Steps,move.Fog,board.Revealed.AddRange(move.Completed));
                    if(move.Steps.Length<board.Remaining)
                        foreach(var suffix in islands.Suggestions(move.Fog,move.Fog,board.Remaining-move.Steps.Length).SelectMany(b=>b.Take(2)).Take(4))
                            VerifyMacro(move.Steps.AddRange(suffix.Steps),suffix.Fog,board.Revealed.AddRange(move.Completed).AddRange(suffix.Completed));
                }
                Check(islandPaths>0,"Controlled island suggestions are nonempty");
                if(fixture.Name=="island-separated") Check(islands.Hits>0,"Local geometry is reused across island transitions");
                Console.WriteLine($"CRYSTAL_ISLAND_MACRO_PASS case={fixture.Name};paths={islandPaths};neighbourhoods={islands.Count};builds={islands.Builds};hits={islands.Hits}");
            }
            var truth=new Dictionary<string,CrystalRewardRoute>();var rawHistories=new HashSet<string>();
            void Visit(ImmutableArray<PredictorCrystalStep> path)
            {
                var run=PredictorRun.FromCrystal(board);
                foreach(var step in path)
                {
                    Check(run.Submit(run.Request!,new SelectCrystalTool(step.Tool))==PredictorInputResult.Accepted,"Raw tool input");
                    Check(run.Submit(run.Request!,new RevealCrystalCell(step.X,step.Y))==PredictorInputResult.Accepted,"Raw click input");
                }
                if(path.Length==board.Remaining)
                {
                    paths++;
                    if(run.Phase!=PredictorPhase.Rewards) return;
                    var final=run.ExportCrystal();
                    if(fixture.Avoid && final.Revealed.Any(i=>board.Items[i].Kind=="CURSE" && !board.Revealed.Contains(i))) return;
                    rawHistories.Add(Signature(board,final.Revealed));
                    truth.TryAdd(Outcome(run),new(path,run.Rewards));return;
                }
                var current=run.ExportCrystal();
                foreach(int i in Enumerable.Range(0,121).Where(i=>current.Hidden[i]))
                    foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}) Visit(path.Add(new(i/11,i%11,tool)));
            }
            Visit([]);
            // Independent coverage mode composes EVERY fragment transition,
            // retaining preparation/blank clicks and exact physical histories.
            // Compare full Runtime outcomes, not only the union of card names.
            var coverage=new PredictorCrystalRewardNodes(board,fixture.Avoid);
            CrystalIslandCoverageUpdate Cover(ImmutableArray<CrystalRewardOption> selection,int memoLimit=100_000)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                var receipt=coverage.AdvanceCoverage(selection,TimeSpan.Zero,memoLimit:memoLimit);
                while(!receipt.Complete && timer.Elapsed<TimeSpan.FromSeconds(5))
                    receipt=coverage.AdvanceCoverage(selection,TimeSpan.FromMilliseconds(10),memoLimit:memoLimit);
                Check(receipt.Complete && receipt.States==receipt.ClosedStates,"Independent fragment coverage closes all admitted work: "+fixture.Name);
                var expectedOptions=truth.Values.SelectMany(PredictorCrystalExplorer.Options).Distinct()
                    .Where(o=>!selection.Contains(o) && truth.Values.Any(row=>PredictorCrystalExplorer.VerifySelection(board,row,selection.Add(o))!=null)).ToHashSet();
                Check(expectedOptions.SetEquals(receipt.Projection.Available.Keys),"Fragment coverage preserves joint take options: "+fixture.Name);
                return receipt;
            }
            var covered=Cover([]);
            var coveredOutcomes=coverage.CoverageRows([]).Select(row=>Outcome(PredictorCrystalExplorer.Replay(board,row.Steps))).ToHashSet();
            Check(truth.Keys.ToHashSet().SetEquals(coveredOutcomes),"Fragment composition preserves all raw reward/state/RNG outcomes: "+fixture.Name);
            var coverageTargets=covered.Projection.Available.Keys.Take(2).ToImmutableArray();
            if(!coverageTargets.IsEmpty) Cover(coverageTargets);
            // A failed lookup target is not used as a pruning assumption: the
            // full action frontier must close before this negative is certified.
            var absent=source.Context.Catalog!.CardDefinitions.Select(d=>new CrystalRewardOption(PredictorRewardKind.Card,d.Prototype.Key))
                .First(o=>!truth.Values.SelectMany(PredictorCrystalExplorer.Options).Contains(o));
            var negative=Cover([absent]);
            Check(negative.Projection.SelectedPlan==null && negative.Projection.Available.Count==0,"Covered impossible target has no fabricated plan");
            if(fixture.Name=="island-cross-preparation")
            {
                var uncached=new PredictorCrystalRewardNodes(board,fixture.Avoid);
                var stopped=uncached.AdvanceCoverage([],TimeSpan.Zero,memoLimit:0);
                Check(!stopped.Complete,"Zero-time coverage is unfinished");
                using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool caught=false;
                try { uncached.AdvanceCoverage([],TimeSpan.FromSeconds(1),cancelled.Token,memoLimit:0); } catch(OperationCanceledException) { caught=true; }
                Check(caught,"Cancelled coverage never becomes completion");
                var noMemo=uncached.AdvanceCoverage([],TimeSpan.FromSeconds(5),memoLimit:0);
                Check(noMemo.Complete && noMemo.MemoSaturated,"Memo saturation recomputes instead of losing work");
                Check(truth.Keys.ToHashSet().SetEquals(uncached.CoverageRows([]).Select(row=>Outcome(PredictorCrystalExplorer.Replay(board,row.Steps)))),
                    "No-memo closure preserves all outcomes");
            }
            if(fixture.Name=="phase-one-big-card-potion")
            {
                var failedCoverage=new PredictorCrystalRewardNodes(board with { Context=board.Context with { PotionPool=[] } },fixture.Avoid);
                for(int attempt=0;attempt<2;attempt++)
                {
                    bool failed=false;
                    try { failedCoverage.AdvanceCoverage([],TimeSpan.FromSeconds(1)); } catch(InvalidOperationException) { failed=true; }
                    Check(failed,"Coverage replay failure remains a fault on retry");
                }
            }
            Console.WriteLine($"CRYSTAL_ISLAND_COVERAGE_PASS case={fixture.Name};capturedHooks={phaseOnly};rawPaths={paths};outcomes={coveredOutcomes.Count};states={covered.States};closed={covered.ClosedStates};merged={covered.Merged};transitions={covered.Transitions};localHits={covered.LocalCacheHits};seconds={covered.Seconds:F3};complete={covered.Complete};memoSaturated={covered.MemoSaturated};scope=direct-reveal-and-supported-take");
            if(fixture.Name is "island-separated" or "preparation" or "small-required" or "ordered-partial" or "phase-one-big-card-potion")
            {
                var independent=new PredictorCrystalRewardNodes(board,fixture.Avoid);
                var suggested=independent.Advance([],TimeSpan.FromMilliseconds(500));
                var expected=truth.Values.SelectMany(PredictorCrystalExplorer.Options).ToHashSet();
                Check(suggested.Projection.Available.Keys.All(expected.Contains),"Independent reward nodes only publish raw-oracle reachable options");
                Check(suggested.Projection.Available.Count>0,"Independent reward nodes find a controlled witness");
                var target=suggested.Projection.Available.Keys.First();
                var selected=independent.Advance([target],TimeSpan.FromMilliseconds(150));
                Check(selected.Projection.SelectedPlan!=null,"Independent selection reuses its known witness");
                Check(selected.Projection.Available.Keys.All(o=>truth.Values.Any(row=>PredictorCrystalExplorer.VerifySelection(board,row,[target,o])!=null)),
                    "Independent compatible suggestions agree with raw take constraints");
                using var stop=new CancellationTokenSource();stop.Cancel();bool cancelled=false;
                try { independent.Advance([target],TimeSpan.FromMilliseconds(1),stop.Token); } catch(OperationCanceledException) { cancelled=true; }
                Check(cancelled,"Independent query honours cancellation without a completion claim");
                var resumed=independent.Advance([target],TimeSpan.FromMilliseconds(20));
                Check(selected.Projection.Available.Keys.All(resumed.Projection.Available.ContainsKey),"Independent resume retains known suggestions");
                Console.WriteLine($"CRYSTAL_REWARD_NODE_ORACLE_PASS case={fixture.Name};options={suggested.Projection.Available.Count};rawOptions={expected.Count};rewardNodes={suggested.RewardNodes};localNodes={suggested.LocalNodes};cacheHits={resumed.LocalCacheHits};drained={suggested.WorklistDrained};limited={suggested.Limited};notExhaustiveClaim=true");
            }
            if(fixture.Name=="phase-common-p-conflict")
            {
                var without=PredictorRun.PreviewCrystalRewards(board,[0,1],CancellationToken.None).Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)
                    .OrderBy(r=>board.Context.Catalog!.CardDefinitions.Single(d=>d.Prototype.Key==r.Cards[0].Key).Rarity=="Common"?0:1).ToArray();
                var withPotion=PredictorRun.PreviewCrystalRewards(board,[0,1,2],CancellationToken.None).Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)
                    .OrderBy(r=>board.Context.Catalog!.CardDefinitions.Single(d=>d.Prototype.Key==r.Cards[0].Key).Rarity=="Common"?0:1).ToArray();
                var first=new CrystalRewardOption(PredictorRewardKind.Card,without[0].Cards.Select(c=>c.Key).Except(withPotion[0].Cards.Select(c=>c.Key)).First());
                var second=new CrystalRewardOption(PredictorRewardKind.Card,withPotion[1].Cards.Select(c=>c.Key).Except(without[1].Cards.Select(c=>c.Key)).First());
                var lookup=new CrystalReachabilityTable(board);
                var oldBound=new PredictorCrystalExplorer(board,true,reachability:lookup,selected:[first]);
                var newBound=new PredictorCrystalExplorer(board,true,reachability:lookup,selected:[first],potionScenarioBound:true);
                Console.WriteLine($"CRYSTAL_COMMON_P_DIAGNOSTIC first={first.Key};second={second.Key};old={oldBound.InitialMaySupport(second)};new={newBound.InitialMaySupport(second)};c0={string.Join(',',without[0].Cards.Select(c=>c.Key))};c1={string.Join(',',withPotion[0].Cards.Select(c=>c.Key))};r6={string.Join(',',without[1].Cards.Select(c=>c.Key))};r7={string.Join(',',withPotion[1].Cards.Select(c=>c.Key))}");
                Check(oldBound.InitialMaySupport(second) && !newBound.InitialMaySupport(second),"Common potion scenario rejects the independently plausible card pair");
                Check(!truth.Values.Any(row=>PredictorCrystalExplorer.VerifySelection(board,row,[first,second])!=null),"Raw legal paths confirm common-potion conflict with real card pools");
                var earlySession=new PredictorCrystalReachability(board,true,potionScenarioBound:true,workers:4);
                var early=earlySession.Advance([first],TimeSpan.Zero);
                var variants=lookup.Candidates.Where(second.Accepts).ToArray();
                Check(!early.Complete && variants.Length>0 && variants.All(early.ExcludedCandidates.Contains),
                    "Root exclusion is published before any traversal or query completion");
                var relaxed=earlySession.Advance([],TimeSpan.Zero);
                Check(variants.Any(v=>!relaxed.ExcludedCandidates.Contains(v)),"Removing a target does not carry its negative proofs to the relaxed query");
                Console.WriteLine("CRYSTAL_EARLY_EXCLUSION_PASS zero-time root proof; selection-local; no whole-query completion");
                Console.WriteLine($"CRYSTAL_COMMON_P_CONFLICT_PASS targets={first.Key},{second.Key};capturedHooks={phaseOnly};old=true;new=false;raw=false");
            }
            if(fixture.Name=="query-prefix-tail")
            {
                var target=truth.Values.SelectMany(PredictorCrystalExplorer.Options).First(o=>o.Kind==PredictorRewardKind.Card);
                var explorer=new PredictorCrystalExplorer(board,true,exhaustiveOnly:true,useTwoStepCache:true,
                    reachability:new CrystalReachabilityTable(board),selected:[target]);
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(!explorer.Complete && timer.Elapsed<TimeSpan.FromSeconds(3)) explorer.Advance(TimeSpan.FromMilliseconds(1));
                Check(explorer.Complete && explorer.QueryProjection.Available.ContainsKey(CrystalRewardOption.AnyRelic),
                    "Query two-step cache must preserve an already-satisfied card prefix");
                Console.WriteLine("CRYSTAL_QUERY_PREFIX_TAIL_PASS exhaustive-only;query=true;two-step=true;prefix-card+relic");
            }
            foreach(bool includeEnchantments in new[]{false,true})
            {
            var queryTruth=truth.Values.Select(row=>row with {
                EnchantmentTargets=includeEnchantments && CrystalRewardOption.UsesEnchantments(board.State) }).ToArray();
            var allOptions=queryTruth.SelectMany(PredictorCrystalExplorer.Options).Distinct().ToArray();
            var selections=new[]{ImmutableArray<CrystalRewardOption>.Empty}
                .Concat(allOptions.Select(o=>ImmutableArray.Create(o)))
                .Concat((from a in Enumerable.Range(0,allOptions.Length)
                    from b in Enumerable.Range(a+1,allOptions.Length-a-1)
                    select ImmutableArray.Create(allOptions[a],allOptions[b])).Take(12));
            foreach(string bound in focus?new[]{"off"}:new[]{"off","discrete","potion","potion-fallback","potion-parallel","potion-directed","potion-islands","offset"})
            foreach(var selection in selections)
            {
                // Cold queries prevent shared positive witnesses from hiding a
                // target-specific pruning defect. Warm retargeting is checked above.
                // Observe raw rows and queries in the same target mode, covering
                // both identity/upgrade targets and exact enchantment targets.
                var querySession=new PredictorCrystalReachability(board,fixture.Avoid,discreteRewardBound:bound=="discrete",
                    potionScenarioBound:bound.StartsWith("potion"),potionScenarioLimit:bound=="potion-fallback"?0:6,workers:bound is "potion-parallel" or "potion-directed" or "potion-islands"?4:1,targetDirected:bound is "potion-directed" or "potion-islands",islandDiscovery:bound=="potion-islands",jointOffsetBound:bound=="offset",includeEnchantments:includeEnchantments);
                var queryWatch=System.Diagnostics.Stopwatch.StartNew();
                var expected=allOptions.Where(o=>!selection.Contains(o) && queryTruth.Any(row=>
                    PredictorCrystalExplorer.VerifySelection(board,row,selection.Add(o))!=null)).ToHashSet();
                var query=querySession.Advance(selection,TimeSpan.FromMilliseconds(1));
                while(!query.Complete && queryWatch.Elapsed<TimeSpan.FromSeconds(5))
                {
                    Check(!query.ExcludedCandidates.Any(expected.Contains),"Partial negative proofs never remove raw reachable options: "+fixture.Name);
                    query=querySession.Advance(selection,TimeSpan.FromMilliseconds(10));
                }
                Check(query.Complete,"Controlled conditional query completes: "+fixture.Name);
                if(!expected.SetEquals(query.Projection.Available.Keys)) Console.WriteLine("CRYSTAL_OPTION_DIFF "+System.Text.Json.JsonSerializer.Serialize(new { fixture=fixture.Name, phaseOnly, includeEnchantments, bound, selected=selection.Select(o=>o.CacheKey), missing=expected.Except(query.Projection.Available.Keys).Select(o=>o.CacheKey), extra=query.Projection.Available.Keys.Except(expected).Select(o=>o.CacheKey) }));
                Check(expected.SetEquals(query.Projection.Available.Keys),"Conditional offset pruning preserves raw reachable options: "+fixture.Name+" bound="+bound);
                Check(!query.ExcludedCandidates.Any(expected.Contains),"Completed negative proofs preserve raw reachable options: "+fixture.Name);
            }
            Console.WriteLine($"CRYSTAL_CONDITIONAL_SOURCE_PASS case={fixture.Name};capturedHooks={phaseOnly};enchantments={includeEnchantments};targets={allOptions.Length}");
            }
            foreach(bool twoStepCache in new[]{false,true})
            {
            // Keep the optional tail optimization covered too; the scarce-padding
            // regression must work even when an audit explicitly enables it.
            var explorer=new PredictorCrystalExplorer(board,fixture.Avoid,exhaustiveOnly:true,useTwoStepCache:twoStepCache);
            var rows=ImmutableArray.CreateBuilder<CrystalRewardRoute>();var watch=System.Diagnostics.Stopwatch.StartNew();
            while(!explorer.Complete && watch.Elapsed<TimeSpan.FromSeconds(5)) rows.AddRange(explorer.Advance(TimeSpan.FromMilliseconds(1)));
            Check(explorer.Complete,"Pruned controlled fixture completes: "+fixture.Name);
            var actual=rows.Select(row=>Outcome(PredictorCrystalExplorer.Replay(board,row.Steps))).ToHashSet();
            Check(truth.Keys.ToHashSet().SetEquals(actual),"Exhaustive-only preserves complete reward groups/state/RNG: "+fixture.Name);
            var expectedOptions=truth.Values.Select(row=>row with {
                EnchantmentTargets=CrystalRewardOption.UsesEnchantments(board.State) })
                .SelectMany(PredictorCrystalExplorer.Options).Where(o=>
                    truth.Values.Any(row=>PredictorCrystalExplorer.VerifySelection(board,row,[o])!=null)).ToHashSet();
            Check(expectedOptions.SetEquals(PredictorCrystalExplorer.Project(board,rows.ToImmutable(),[]).Available.Keys),
                "Pruning preserves reachable rewards: "+fixture.Name);
            if(fixture.Name=="small-required") Check(rows.Any(row=>row.Steps.Single().Tool==PredictorCrystalTool.Small),"Required Small reveal survives pruning");
            if(fixture.Name=="prefix-padding")
            {
                Check(PredictorCrystalExplorer.Replay(board,[new(5,5,PredictorCrystalTool.Small),new(6,6,PredictorCrystalTool.Big)]).Phase==PredictorPhase.Rewards,
                    "Reference blank-before-Big path is legal");
                Check(rows.Any(row=>row.Steps.Length==2),"Covered-blank outcome retains a complete canonical witness");
            }
            if(fixture.Name=="preparation") Check(rows.Any(row=>row.Steps.Length==4),"Three unrewarded preparation clicks still reach the relic");
            Console.WriteLine($"CRYSTAL_INFORMATION_PRUNING_PASS case={fixture.Name};capturedHooks={phaseOnly};twoStepCache={twoStepCache};rawPaths={paths};rawHistories={rawHistories.Count};rowAttempts={explorer.Examined};profiles={actual.Count};pruned={explorer.Progress.Pruned};merged={explorer.Progress.Merged};exhaustiveOnly=true");
            }
        }
    }

    private static void CheckDiscreteBoundWorkloads(PredictorCrystalSnapshot source,int fixture=0)
    {
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_SLICED")=="1")
        {
            var parallel=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true);
            var initialSuggestions=parallel.Advance([],TimeSpan.FromMilliseconds(150));
            var selection=ImmutableArray.Create(initialSuggestions.Projection.Available.Keys.First(o=>o.Kind==PredictorRewardKind.Card));
            var prior=parallel.Advance(selection,TimeSpan.Zero);
            using var cancellation=new CancellationTokenSource();cancellation.CancelAfter(20);
            var stopWatch=System.Diagnostics.Stopwatch.StartNew();
            var stopped=parallel.Advance(selection,TimeSpan.FromSeconds(3),cancellation.Token);
            Check(!stopped.Complete && stopped.Workers==4,"Cancelled parallel search retains unfinished proof lanes");
            double stopMs=stopWatch.Elapsed.TotalMilliseconds;
            var resumed=parallel.Advance(selection,TimeSpan.FromMilliseconds(150));
            Check(prior.Projection.Available.Keys.All(resumed.Projection.Available.ContainsKey) &&
                resumed.Progress.Examined>=stopped.Progress.Examined,"Parallel resume retains proofs and traversal progress");
            Console.WriteLine($"CRYSTAL_PARALLEL_CANCEL_RESUME_PASS workers={resumed.Workers};cancelMs={stopMs:F1};options={resumed.Projection.Available.Count}");
        }
        if(fixture==0 && Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_SNAPSHOT_PATH") is { Length:>0 } snapshotPath)
        {
            ProductionSearchReplay.Write(snapshotPath,source);
            source=ProductionSearchReplay.Read<PredictorCrystalSnapshot>(snapshotPath);
            Console.WriteLine($"CRYSTAL_BOUND_SNAPSHOT_ROUNDTRIP_PASS path={snapshotPath};bytes={new FileInfo(snapshotPath).Length};seed={source.Context.Seed}");
        }
        // Fixed frozen board and explicit targets, cold per-query, same time
        // budget. This measures Next(S), not merely finding a positive S witness.
        var table=new PredictorCrystalReachability(source,true);
        var initial=table.Advance([],TimeSpan.FromSeconds(1));
        var potion=initial.Projection.Available.Keys.Where(o=>o.Kind==PredictorRewardKind.Potion)
            .OrderBy(o=>o.Key.Entry,StringComparer.Ordinal).First();
        CrystalRewardOption Card(string entry)=>new(PredictorRewardKind.Card,new("CARD",entry));
        var a=Card("BARRICADE");var b=Card("BATTLE_TRANCE");
        if(fixture!=0)
        {
            var witnessedCards=initial.Projection.Available.Keys.Where(o=>o.Kind==PredictorRewardKind.Card)
                .OrderBy(o=>o.Key.Entry,StringComparer.Ordinal).ToArray();
            a=witnessedCards.First(o=>source.Context.Catalog!.CardDefinitions.Any(d=>d.Prototype.Key==o.Key && d.Rarity=="Rare"));
            b=witnessedCards.First(o=>o!=a);
        }
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_FOCUS")=="tail")
        {
            var tailWatch=System.Diagnostics.Stopwatch.StartNew();
            var tail=PredictorCrystalSearch.Solve(source,[a.Key,b.Key],TimeSpan.FromSeconds(5),options:new(true,true));
            if(tail.Status=="Found")
                Check(PredictorCrystalSearch.Verify(source,tail.Steps,[a.Key,b.Key],options:new(true,true))!=null,"Targeted tail witness is real");
            Console.WriteLine($"CRYSTAL_TARGETED_TAIL query=AB_relic;status={tail.Status};seconds={tailWatch.Elapsed.TotalSeconds:F3};nodes={tail.Examined};rewardRows={tail.RewardRows};geometryPruned={tail.GeometryPruned}");
            return;
        }
        var workloads=new[]{
            (Name:"empty",Targets:ImmutableArray<CrystalRewardOption>.Empty),
            (Name:"A",Targets:ImmutableArray.Create(a)),
            (Name:"AB",Targets:ImmutableArray.Create(a,b)),
            (Name:"A_relic",Targets:ImmutableArray.Create(a,CrystalRewardOption.AnyRelic)),
            (Name:"A_potion",Targets:ImmutableArray.Create(a,potion))
        };
        foreach(var workload in workloads.Where(w=>Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_FOCUS") is not { Length:>0 } focus || focus.Split(',').Contains(w.Name)))
        foreach(string bound in (Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_VARIANTS")??"off,discrete,potion").Split(','))
        {
            if(bound=="islands")
            {
                var topology=new PredictorCrystalIslands(source,true);
                Console.WriteLine($"CRYSTAL_ISLAND_TOPOLOGY fixture={fixture};islands={topology.Count};averageInfluenceCells={topology.AverageInfluenceCells:F1};boardCells=121");
            }
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var independent=bound is "reward-nodes" or "island-coverage"?new PredictorCrystalRewardNodes(source,true):null;
            CrystalRewardNodeUpdate? independentUpdate=null;
            CrystalIslandCoverageUpdate? coverageUpdate=null;
            var session=new PredictorCrystalReachability(source,true,discreteRewardBound:bound=="discrete",potionScenarioBound:bound is "potion" or "parallel" or "directed" or "islands" or "serial-directed",workers:bound is "parallel" or "directed" or "islands"?4:1,targetDirected:bound is "directed" or "islands" or "serial-directed",islandDiscovery:bound=="islands");
            CrystalReachabilityUpdate Tick(TimeSpan time)
            {
                if(independent==null) return session.Advance(workload.Targets,time);
                if(bound=="island-coverage")
                {
                    coverageUpdate=independent.AdvanceCoverage(workload.Targets,time);
                    return new(coverageUpdate.Projection,new(coverageUpdate.States,0,coverageUpdate.Merged,(int)coverageUpdate.RuntimeRows,coverageUpdate.Seconds),
                        CrystalDiscoveryPhase.FindingPlans,coverageUpdate.Complete,UnwitnessedUpper:[]);
                }
                independentUpdate=independent.Advance(workload.Targets,time);
                return new(independentUpdate.Projection,new(independentUpdate.RewardNodes,0,0,(int)independentUpdate.RuntimeRows,independentUpdate.Seconds),
                    CrystalDiscoveryPhase.FindingPlans,false,UnwitnessedUpper:[]);
            }
            CrystalReachabilityUpdate AdvanceFor(int seconds)
            {
                if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_SLICED")!="1")
                    return Tick(TimeSpan.FromSeconds(seconds));
                var timer=System.Diagnostics.Stopwatch.StartNew();CrystalReachabilityUpdate result;
                do { result=Tick(TimeSpan.FromMilliseconds(150)); }
                while(!result.Complete && independentUpdate?.WorklistDrained!=true && timer.Elapsed<TimeSpan.FromSeconds(seconds));
                return result;
            }
            var update=AdvanceFor(1);
            void Report(int budget)=>Console.WriteLine($"CRYSTAL_BOUND_EXPERIMENT fixture={fixture};query={workload.Name};bound={bound};budget={budget};wall={watch.Elapsed.TotalSeconds:F3};complete={update.Complete};options={update.Projection.Available.Count};hasPlan={update.Projection.SelectedPlan!=null};rowAttempts={update.Progress.Examined};orders={update.Progress.Orders};refineChecks={update.BoundChecks};refineRejected={update.BoundRejected};refineSeconds={update.BoundSeconds:F3};boundCacheHits={update.BoundCacheHits};rootExcluded={update.BoundRootExcluded};scenarios={update.Scenarios};scenarioCoverRejected={update.ScenarioCoverRejected};scenarioFallbacks={update.ScenarioFallbacks};directedRecipes={update.DirectedRecipes};directedNodes={update.DirectedNodes};directedWitnesses={update.DirectedWitnesses};islandBuilds={update.IslandBuilds};islandHits={update.IslandHits};rewardNodeLocal={independentUpdate?.LocalNodes};rewardNodeHits={independentUpdate?.LocalCacheHits};worklistDrained={independentUpdate?.WorklistDrained};rewardNodeLimited={independentUpdate?.Limited};targets={string.Join(',',workload.Targets.Select(t=>t.Key))};unwitnessedUpper={string.Join(',',update.UnwitnessedUpper.Select(c=>c.Kind+":"+c.Key.Entry))}");
            Report(1);
            if(!update.Complete) update=AdvanceFor(4);
            Report(5);
            if(coverageUpdate!=null) Console.WriteLine($"CRYSTAL_ISLAND_COVERAGE_BUDGET fixture={fixture};query={workload.Name};wall={watch.Elapsed.TotalSeconds:F3};options={coverageUpdate.Projection.Available.Count};states={coverageUpdate.States};closed={coverageUpdate.ClosedStates};merged={coverageUpdate.Merged};transitions={coverageUpdate.Transitions};localHits={coverageUpdate.LocalCacheHits};rows={coverageUpdate.RuntimeRows};complete={coverageUpdate.Complete};memoSaturated={coverageUpdate.MemoSaturated};notHeuristicTruncation=true");
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_LONG")=="1")
            {
                if(!update.Complete) update=AdvanceFor(15);
                Report(20);
            }
        }
        if(fixture==0 && Environment.GetEnvironmentVariable("RT2_CRYSTAL_REWARD_NODE_WARM")=="1")
        foreach(bool rewardNodes in new[]{false,true})
        {
            var original=new PredictorCrystalReachability(source,true,potionScenarioBound:true,targetDirected:true);
            var independent=new PredictorCrystalRewardNodes(source,true);
            foreach(var step in new[]{(Name:"empty",Targets:ImmutableArray<CrystalRewardOption>.Empty),
                (Name:"A",Targets:ImmutableArray.Create(a)),(Name:"AB",Targets:ImmutableArray.Create(a,b)),(Name:"back_A",Targets:ImmutableArray.Create(a))})
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();double? firstPlan=null;
                CrystalOptionProjection? projection=null;long hits=0;bool limited=false;
                do
                {
                    if(rewardNodes)
                    {
                        var receipt=independent.Advance(step.Targets,TimeSpan.FromMilliseconds(10));
                        projection=receipt.Projection;hits=receipt.LocalCacheHits;limited=receipt.Limited;
                    }
                    else projection=original.Advance(step.Targets,TimeSpan.FromMilliseconds(10)).Projection;
                    if(projection.SelectedPlan!=null && firstPlan==null) firstPlan=timer.Elapsed.TotalMilliseconds;
                } while(timer.Elapsed<TimeSpan.FromSeconds(1));
                Console.WriteLine($"CRYSTAL_REWARD_NODE_WARM engine={(rewardNodes?"independent":"serial-directed")};step={step.Name};firstPlanMs={firstPlan:F1};options={projection.Available.Count};wall={timer.Elapsed.TotalSeconds:F3};localHits={hits};limited={limited};fixedTargets=true;notUiLatency=true");
            }
        }
        if(fixture==0 && Environment.GetEnvironmentVariable("RT2_CRYSTAL_BOUND_MORE_BOARDS")=="1")
            foreach(int shift in new[]{17,71})
            {
                var rng=source.EventRng.Restore();rng.Advance(shift);
                var board=PredictorRun.CreateCrystalBranch(source.Context,source.State,
                    PredictorStreamState.Capture(PredictorStream.Rewards,rng),true,0);
                Console.WriteLine($"CRYSTAL_CONTROLLED_BOARD fixture={shift};RuntimeGenerated=true;notNativeParity=true");
                CheckDiscreteBoundWorkloads(board,shift);
            }
    }

    private static void CheckRewardTable(PredictorCrystalSnapshot source, PredictorRun actual)
    {
        var preview = PredictorRun.PreviewCrystalRewards(source, actual.ExportCrystal().Revealed, CancellationToken.None);
        Check(JsonSerializer.Serialize(preview.Working) == JsonSerializer.Serialize(actual.Working), "Reward table preserves all resulting state");
        Check(JsonSerializer.Serialize(preview.Rewards) == JsonSerializer.Serialize(actual.Rewards), "Reward table includes actual card/relic/potion/gold results");
        Check(preview.ExportCrystal().EventRng == actual.ExportCrystal().EventRng, "Reward table preserves event RNG continuation");
    }

    private static void BenchmarkKnownPlan(PredictorCrystalSnapshot source, string label, bool relic, int cardCount)
    {
        var desired = source.Items.Select((item, i) => (item, i)).Where(x => x.item.X >= 0 &&
            (x.item.Kind == "CARD_RARE" || cardCount == 2 && x.item.Kind == "CARD_UNCOMMON" || relic && x.item.Kind == "RELIC")).ToArray();
        var required = desired.SelectMany(x => Enumerable.Range(x.item.X, x.item.Width)
            .SelectMany(a => Enumerable.Range(x.item.Y, x.item.Height).Select(b => a * 11 + b))).ToHashSet();
        var replay = PredictorRun.FromCrystal(source); var witness = new List<PredictorCrystalStep>();
        while (replay.Phase == PredictorPhase.EventChoice)
        {
            var board = replay.ExportCrystal();
            var choices = (from x in Enumerable.Range(0, 11) from y in Enumerable.Range(0, 11)
                from tool in new[] { PredictorCrystalTool.Big, PredictorCrystalTool.Small }
                where board.Hidden[x * 11 + y]
                let cells = PredictorCrystalSearch.Cells(x,y,tool).Select(c => c.X * 11 + c.Y).Where(i => board.Hidden[i]).ToHashSet()
                where !board.Items.Any(item => item.Kind == "CURSE" && !item.Revealed && item.X >= 0 &&
                    Enumerable.Range(item.X,item.Width).SelectMany(a => Enumerable.Range(item.Y,item.Height).Select(b => a*11+b))
                    .All(i => !board.Hidden[i] || cells.Contains(i)))
                select (Step: new PredictorCrystalStep(x,y,tool), Gain: cells.Count(required.Contains), Count: cells.Count))
                .OrderByDescending(c => c.Gain).ThenBy(c => c.Count).ToArray();
            if (choices.Length == 0) throw new Exception("Crystal benchmark has no legal move");
            var selected = choices[0].Step; witness.Add(selected);
            replay.Submit(replay.Request!, new SelectCrystalTool(selected.Tool));
            replay.Submit(replay.Request!, new RevealCrystalCell(selected.X,selected.Y));
        }
        var groups = replay.Rewards.Where(r => r.Kind == PredictorRewardKind.Card &&
            r.Cards.Any(c => source.Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key == c.Key).Rarity == "Rare")).Take(1).ToList();
        if (cardCount == 2) groups.AddRange(replay.Rewards.Where(r => r.Kind == PredictorRewardKind.Card &&
            r.Cards.Any(c => source.Context.Catalog!.CardDefinitions.Single(d => d.Prototype.Key == c.Key).Rarity == "Uncommon")).Take(1));
        Check(groups.Count == cardCount && (!relic || replay.Rewards.Any(r => r.Kind == PredictorRewardKind.Relic)), "Benchmark actually reaches primary goals " + label);
        var targets = groups.Select(r => r.Cards[^1].Key).ToImmutableArray();
        var options = new PredictorCrystalOptions(true,relic);
        Check(PredictorCrystalSearch.Verify(source,witness.ToImmutableArray(),targets,options:options) != null, "Benchmark independent witness " + label);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = PredictorCrystalSearch.Solve(source,targets,TimeSpan.FromSeconds(3),options:options);
        Check(result.Status == "Found", "Known reachable primary workload " + label + ": " + result.Status);
        if (result.Status == "Found") Check(PredictorCrystalSearch.Verify(source,result.Steps,targets,options:options) != null, "Benchmark solver replay " + label);
        Console.WriteLine($"CRYSTAL_WORKLOAD mode={label};targets={string.Join(',',targets.Select(k=>k.Entry))};status={result.Status};ms={watch.Elapsed.TotalMilliseconds:F1};nodes={result.Examined};rewardRows={result.RewardRows};cacheHits={result.RewardCacheHits};geometryPruned={result.GeometryPruned}");
    }

}
