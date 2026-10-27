using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static bool RootCardSlotsEnabled=>Environment.GetEnvironmentVariable("RT2_CRYSTAL_ROOT_CARD_SLOTS")=="1";

    private static void CheckRootCardSlotGuards(PredictorCrystalSnapshot source)
    {
        var item=new PredictorCrystalItem("CARD_COMMON",2,2,2,2,1,false);
        var hidden=new bool[121];hidden[120]=true;
        var occupancy=Enumerable.Repeat(-1,121).ToArray();
        for(int x=2;x<4;x++) for(int y=2;y<4;y++) {hidden[x*11+y]=true;occupancy[x*11+y]=0;}
        var one=source with {Items=[item],Hidden=hidden.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray(),Revealed=[],Remaining=2};
        var table=new CrystalReachabilityTable(one,includeRerolls:true);
        var cards=table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card).Take(2).ToImmutableArray();
        Check(cards.Length==2,"Root-slot fixture has two globally supported card goals");
        var bound=new PredictorCrystalOffsetBound(one,table,true);
        Check(!bound.RejectRootCardSlots([cards[0],cards[0]]),"Normalize duplicate goals before learning set conflicts");
        Check(bound.KnownLanguageNegative([cards[0]])==CrystalLanguageNegativeKind.None,"Duplicate goal does not poison singleton");
        Check(bound.RejectRootCardSlots(cards),"Two supported cards compete for one final slot, even with rerolls");
        Check(bound.KnownLanguageNegative(cards)==CrystalLanguageNegativeKind.RewardDomain,"Root denial is retained");
        var twice=one with {Items=[item with {Subscriptions=2}]};
        var twiceTable=new CrystalReachabilityTable(twice,includeRerolls:true);
        Check(!new PredictorCrystalOffsetBound(twice,twiceTable,true).RejectRootCardSlots(cards),"Two subscriptions retain two slots");
        var openHidden=hidden.ToArray();for(int x=2;x<4;x++) for(int y=2;y<4;y++) openHidden[x*11+y]=false;
        var revealed=twice with {Items=[item with {Subscriptions=2,Revealed=true}],Revealed=[0,0],Hidden=openHidden.ToImmutableArray(),Remaining=1};
        Check(!new PredictorCrystalOffsetBound(revealed,new(revealed,includeRerolls:true),true).RejectRootCardSlots(cards),
            "Already revealed callbacks count separately without multiplying subscriptions again");
        int variantPairs=0;
        foreach(var variants in table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card).GroupBy(c=>c.Key).Where(g=>g.Count()>1).Take(1))
        {
            var pair=variants.Take(2).ToImmutableArray();
            Check(new PredictorCrystalOffsetBound(one,table,true).RejectRootCardSlots(pair),"Upgrade variants still need distinct slots");
            Check(!new PredictorCrystalOffsetBound(twice,twiceTable,true).RejectRootCardSlots(pair),"Variant matching retains duplicate slots");
            variantPairs++;
        }
        var fresh=new PredictorCrystalOffsetBound(one,table,true);
        Check(PredictorCrystalGoalLanguage.Solve(one,table,cards,true,TimeSpan.Zero,sharedBound:fresh,rootCardSlots:true).Verdict==CrystalOffsetVerdict.Incomplete,
            "Cold zero time does not perform root proof");
        Check(PredictorCrystalGoalLanguage.Solve(one,table,cards,true,TimeSpan.FromSeconds(1),stateLimit:0,sharedBound:fresh,rootCardSlots:true).Verdict==CrystalOffsetVerdict.Incomplete,
            "Cold zero state budget does not perform root proof");
        Check(fresh.KnownLanguageNegative(cards)==CrystalLanguageNegativeKind.None,"Cutoffs leave no root fact");
        Check(fresh.Check(cards,0).Verdict==CrystalOffsetVerdict.Incomplete,"Production cold zero state stays unknown");
        var rootReceipt=fresh.Check(cards);
        Check(rootReceipt.Verdict==CrystalOffsetVerdict.Impossible && rootReceipt.RootCardSlotsRejected && rootReceipt.States==0,
            "Production offset check closes slot conflict without subset DP");
        Check(fresh.Check(cards,0).Verdict==CrystalOffsetVerdict.Impossible,"Completed fact can answer subsequent zero-budget query");
        Check(fresh.Check([cards[0]]).MaySupport,"Removing a necessary card does not retain the pair conflict");
        Check(!new PredictorCrystalOffsetBound(one,table,true,rootCardSlots:false).Check(cards).RootCardSlotsRejected,
            "Audit control retains the old production proof path");
        Console.WriteLine($"CRYSTAL_ROOT_SLOT_GUARDS_PASS subscriptions=2;revealedEntries=2;duplicateNormalized=true;variantPairs={variantPairs};controlledCountsNotNativePaths=true");
    }

    private static void CheckRootSlotProduction(PredictorCrystalSnapshot source,ImmutableArray<CrystalRewardOption> selected,
        Action<ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode> native,string output)
    {
        string before=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
        var table=new CrystalReachabilityTable(source,includeRerolls:true);
        var slotBound=new PredictorCrystalOffsetBound(source,table,true);
        var slotNegatives=table.Candidates.Where(c=>!selected.Contains(c) && slotBound.RejectRootCardSlots(selected.Add(c))).ToImmutableArray();
        Check(slotNegatives.Length>0,"Natural production fixture exercises root slot conflicts");
        var goal=slotNegatives.First(c=>c.Kind==PredictorRewardKind.Card);
        var cardPair=selected.Where(c=>c.Kind==PredictorRewardKind.Card).Append(goal).ToImmutableArray();
        var focus=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,includeRerolls:true);
        var firstFocus=focus.AdvanceFocused(selected,goal,TimeSpan.Zero);
        Check(firstFocus.Verdict==CrystalFocusVerdict.Unreachable && firstFocus.ProofSource=="root-card-slots" &&
            firstFocus.RootProof is {States:0,RootCardSlotsRejected:true} && firstFocus.Progress.Examined==0,
            "Focused root denial bypasses all click workers");
        Check(focus.IsCertifiedConflict(cardPair) && focus.IsCertifiedConflict(cardPair.Reverse().ToImmutableArray()),
            "Slot fact drops irrelevant relic targets and is permutation independent");
        Check(!focus.IsCertifiedConflict([goal]),"Removing a necessary goal restores eligibility");
        Check(focus.AdvanceFocused(selected,goal,TimeSpan.Zero).ProofSource=="learned-conflict","Focused repeated negative is retained");

        var receipts=new List<object>();CrystalReachabilityUpdate? baseline=null,enabled=null;
        foreach(bool root in Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1"?new[]{true,false}:new[]{false,true})
        {
            var session=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,
                includeRerolls:true,rootCardSlots:root);
            var watch=Stopwatch.StartNew();
            double? earlyMs=null;ImmutableArray<CrystalRewardOption> early=[];
            var result=session.Advance(selected,TimeSpan.Zero,publishExcluded:denials=>{earlyMs=watch.Elapsed.TotalMilliseconds;early=denials;});
            double initialMs=watch.Elapsed.TotalMilliseconds;
            if(root) Check(slotNegatives.All(early.Contains) && earlyMs<initialMs,
                "All cheap negatives are published before the expensive first projection returns");
            Check(slotNegatives.All(result.ExcludedCandidates.Contains),"Production query publishes every root slot denial");
            Check(!result.ExcludedCandidates.Any(result.Projection.Available.ContainsKey),"No available option is excluded");
            Check(result.Progress.Examined==0,"Root publication needs no click traversal");
            watch.Restart();var repeated=session.Advance(selected,TimeSpan.Zero);double repeatMs=watch.Elapsed.TotalMilliseconds;
            Check(result.ExcludedCandidates.ToHashSet().SetEquals(repeated.ExcludedCandidates),"Repeated query retains exclusions");
            if(root)
            {
                Check(session.IsCertifiedConflict(cardPair),"Broad root pass publishes the card-only conflict");
                session.Advance([],TimeSpan.Zero);
                Check(!session.IsCertifiedConflict([goal]),"Deselect does not convert a pair conflict into singleton absence");
                var reselected=session.Advance(selected,TimeSpan.Zero);
                Check(slotNegatives.All(reselected.ExcludedCandidates.Contains),"Reselect reuses complete exclusions");
                var reordered=session.Advance(selected.Reverse().ToImmutableArray(),TimeSpan.Zero);
                Check(slotNegatives.All(reordered.ExcludedCandidates.Contains),"Reordered selection retains all-orders root denials");
            }
            if(root) enabled=result;else baseline=result;
            receipts.Add(new {root,earlyMs,early,initialMs,repeatMs,result,conflicts=session.ConflictCount});
            Console.WriteLine($"CRYSTAL_ROOT_PRODUCTION_MODE seed={source.Context.Seed};root={root};earlyMs={earlyMs:F3};early={early.Length};initialMs={initialMs:F3};repeatMs={repeatMs:F3};excluded={result.ExcludedCandidates.Length};conflicts={session.ConflictCount}");
        }
        Check(baseline!=null && enabled!=null && baseline.ExcludedCandidates.ToHashSet().SetEquals(enabled.ExcludedCandidates),
            "Production first projection retains the baseline exclusion set");
        // A legal positive must still survive after cancellation and fresh selection.
        var answer=PredictorCrystalGoalLanguage.Solve(source,table,selected,true,TimeSpan.FromSeconds(5),rootCardSlots:true);
        Check(answer.Plan!=null,"Selected natural goals retain a legal witness");
        var replay=PredictorCrystalExplorer.Replay(source,answer.Plan!.Steps);
        focus.AddWitnesses([new(answer.Plan.Steps,replay.Rewards)]);
        var positive=focus.ProjectKnown([]);
        foreach(var c in selected.Where(c=>c.Kind==PredictorRewardKind.Card))
            Check(positive.Available.ContainsKey(c) && focus.AdvanceFocused([],c,TimeSpan.Zero).Verdict==CrystalFocusVerdict.Reachable,
                "Canceling the conflicting candidate restores a witnessed selected card");
        native(answer.Plan.Steps,ReplayProof(source,answer.Plan));
        Check(before==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Production proof leaves source snapshot unchanged");
        ProductionSearchReplay.Write(Path.Combine(output,"root-production-"+source.Context.Seed+".json"),new {source,selected,slotNegatives,firstFocus,receipts});
        Console.WriteLine($"CRYSTAL_ROOT_PRODUCTION seed={source.Context.Seed};candidates={table.Candidates.Length};slotNegatives={slotNegatives.Length};excluded={enabled!.ExcludedCandidates.Length};sameExclusions=true;focusSource={firstFocus.ProofSource};noClickTraversal=true;retarget=true;nativePositive=true");
    }
}
