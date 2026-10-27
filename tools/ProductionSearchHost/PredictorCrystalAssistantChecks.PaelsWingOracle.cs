using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private sealed record WingRawState(PredictorRun Run,ImmutableDictionary<long,PredictorCard> CardEvidence);

    private static ImmutableArray<ImmutableArray<CrystalRewardOption>> CheckPaelsWingSmallOracle(PredictorCrystalSnapshot source,string output,
        bool includeBoardRelic=true,string? label=null,Action<int,int>? observePhysicalUpgrades=null)
    {
        // A late, partially uncovered board: each remaining item has one hidden
        // cell. All other cells of its native-sized footprint are uncovered.
        // Distant cells ensure both tools remain legal without merging items.
        var items=ImmutableArray.Create(new PredictorCrystalItem("CARD_COMMON",2,2,0,0,1,false),
            new PredictorCrystalItem("CARD_UNCOMMON",2,2,0,9,1,false),new PredictorCrystalItem("RELIC",4,4,7,0,1,false));
        if(!includeBoardRelic) items=items.Where(i=>i.Kind!="RELIC").ToImmutableArray();
        var hidden=new bool[121];var occupancy=Enumerable.Repeat(-1,121).ToArray();
        for(int i=0;i<items.Length;i++)
        {
            var item=items[i];for(int x=item.X;x<item.X+item.Width;x++) for(int y=item.Y;y<item.Y+item.Height;y++) occupancy[x*11+y]=i;
            hidden[item.X*11+item.Y]=true;
        }
        hidden[10*11+10]=true;
        // Three initially hidden empty centers guarantee prefix padding within
        // the original three-click budget. This is an initial-prefix invariant;
        // a Big click may clear several of these cells later in the route.
        hidden[4*11+5]=true;hidden[5*11+5]=true;
        var board=source with {Remaining=3,Items=items,Revealed=[],Hidden=hidden.ToImmutableArray(),Occupancy=occupancy.ToImmutableArray()};
        string frozen=JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions);
        var outcomes=new List<ImmutableArray<CrystalRewardOption>>();var endpoints=new HashSet<string>();long paths=0,states=0;
        int giftEndpoints=0,narrowedGiftEndpoints=0,looseGiftCandidates=0,prefixGiftCandidates=0;
        int observedPhysicalUpgradeEdges=0,changedInsertions=0;
        void Suffix(PredictorRun entry)
        {
            var giftUpper=PredictorCrystalWing.Active(entry.Working)?PredictorCrystalWing.GiftAvailability(entry.Context,entry.Working,
                (entry.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)+PredictorCrystalWing.Parity(entry.Working))/2):null;
            if(giftUpper is {Count:>0})
            {
                int count=(entry.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)+PredictorCrystalWing.Parity(entry.Working))/2;
                var loose=PredictorCrystalWing.GiftUpper(entry.Context,entry.Working,count);
                giftEndpoints++;looseGiftCandidates+=loose.Count;prefixGiftCandidates+=giftUpper.Count;
                if(giftUpper.Count<loose.Count) narrowedGiftEndpoints++;
            }
            var queue=new Queue<WingRawState>();queue.Enqueue(new(entry.ForkCrystalRewards(),ImmutableDictionary<long,PredictorCard>.Empty));var seen=new HashSet<string>();
            while(queue.TryDequeue(out var node))
            {
                var run=node.Run;
                // Request IDs/traces are intentionally excluded: this is the
                // raw semantic graph, not the production suffix normalization.
                string key=JsonSerializer.Serialize(new {run.Working,run.Rewards,run.CrystalSelectedReward,node.CardEvidence},ProductionSearchReplay.JsonOptions);
                if(!seen.Add(key)) continue;
                if(++states>100_000) throw new InvalidOperationException("Wing raw oracle state cutoff: result remains unknown");
                if(giftUpper!=null)
                    Check(run.Working.Relics.Where(r=>!entry.Working.Relics.Any(prior=>prior.Id==r.Id)).All(r=>giftUpper.Contains(r.Key)),
                        "Endpoint gift-prefix upper set preserves every gift reached by the complete raw suffix oracle");
                outcomes.Add(WingObtainedOptions(entry,run,CrystalRewardOption.UsesEnchantments(board.State),node.CardEvidence));
                var inputs=new List<PredictorInput>();
                if(run.CrystalSelectedReward is { } active)
                {
                    var reward=run.Rewards.Single(r=>r.Id==active);inputs.Add(new ExitCardSelection());
                    inputs.AddRange(reward.Cards.Select(c=>new TakeReward(active,c.Id)));
                    foreach(var alternative in run.CardRewardAlternatives.Where(a=>a.Key=="SACRIFICE"))
                        inputs.Add(new SacrificeCardReward(active,alternative.RelicId));
                }
                else inputs.AddRange(run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card && r.Status==PredictorRewardStatus.Open)
                    .Select(r=>new EnterCardSelection(r.Id)));
                foreach(var input in inputs)
                {
                    var copy=run.ForkCrystalRewards();
                    Check(copy.Submit(copy.Request!,input)==PredictorInputResult.Accepted,"Wing raw oracle follows actual legal reward inputs");
                    Check(copy.Phase==PredictorPhase.Rewards,"Controlled vanilla Wing oracle does not suspend on an unimplemented child interaction");
                    var evidence=node.CardEvidence.ToBuilder();
                    if(input is TakeReward take)
                    {
                        int rewardIndex=run.Rewards.FindIndex(r=>r.Id==take.RewardId);
                        int offerIndex=run.Rewards[rewardIndex].Cards.FindIndex(c=>c.Id==take.CardId);
                        // Independent of the production helper: bind the Take's
                        // group/position to its ORIGINAL generated offer. New
                        // Eggs may replace/upgrade the live offered instance;
                        // physical insertion and later upgrades stay separate.
                        var offered=entry.Rewards[rewardIndex].Cards[offerIndex];
                        foreach(long id in copy.Working.Deck.Where(id=>!run.Working.Deck.Contains(id)))
                        {evidence[id]=offered;if(copy.Working.Card(id).UpgradeLevel!=offered.UpgradeLevel) changedInsertions++;}
                    }
                    observedPhysicalUpgradeEdges+=run.Working.Deck.Where(copy.Working.Deck.Contains)
                        .Count(id=>run.Working.Card(id).UpgradeLevel!=copy.Working.Card(id).UpgradeLevel);
                    queue.Enqueue(new(copy,evidence.ToImmutable()));
                }
            }
        }
        void Clicks(ImmutableArray<PredictorCrystalStep> path)
        {
            var run=PredictorCrystalExplorer.Replay(board,path);
            if(path.Length==board.Remaining)
            {
                paths++;if(run.Phase!=PredictorPhase.Rewards) return;
                string endpoint=JsonSerializer.Serialize(new {run.Working,run.Rewards,Event=run.ExportCrystal().EventRng},ProductionSearchReplay.JsonOptions);
                if(endpoints.Add(endpoint)) Suffix(run);
                return;
            }
            var state=run.ExportCrystal();
            for(int cell=0;cell<121;cell++) if(state.Hidden[cell])
                foreach(var tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big})
                    Clicks(path.Add(new(cell/11,cell%11,tool)));
        }
        Clicks([]);
        var universe=outcomes.SelectMany(o=>o).Distinct().ToArray();
        var table=new CrystalReachabilityTable(board,includeRerolls:false,includeEnchantments:true);
        Check(universe.All(table.Candidates.Contains),"Production Wing candidate universe contains every complete raw-oracle result");
        var firstCard=universe.First(c=>c.Kind==PredictorRewardKind.Card);
        var firstRelic=universe.First(c=>c.Kind==PredictorRewardKind.Relic);
        List<ImmutableArray<CrystalRewardOption>> selections=[[],[firstCard],[firstRelic]];
        var pair=outcomes.FirstOrDefault(row=>row.Count(o=>o.Kind==PredictorRewardKind.Relic)>1);
        if(!pair.IsDefaultOrEmpty) selections.Add(pair.Where(o=>o.Kind==PredictorRewardKind.Relic).Take(2).ToImmutableArray());
        var receipts=new List<object>();int positives=0,negatives=0;
        var reach=new PredictorCrystalReachability(board,true,includeRerolls:false,includeEnchantments:true,workers:1);
        var directBound=new PredictorCrystalOffsetBound(board,table,true);
        foreach(var selected in selections)
        foreach(var target in table.Candidates.Where(c=>!selected.Contains(c)))
        {
            var goals=selected.Add(target);
            bool expected=outcomes.Any(row=>goals.All(g=>g.Kind==PredictorRewardKind.Relic && !g.Key.IsValid
                ?row.Any(o=>o.Kind==PredictorRewardKind.Relic):row.Any(g.Accepts)));
            var timer=Stopwatch.StartNew();CrystalFocusUpdate update;
            do { update=reach.AdvanceFocused(selected,target,TimeSpan.FromMilliseconds(5)); }
            while(update.Verdict==CrystalFocusVerdict.Checking && timer.Elapsed<TimeSpan.FromSeconds(10));
            Check(update.Verdict!=CrystalFocusVerdict.Checking,"Small Wing focus must complete; a cutoff supplies no negative evidence");
            Check((update.Verdict==CrystalFocusVerdict.Reachable)==expected,
                $"Wing complete raw oracle and production focus agree: {string.Join(',',goals.Select(g=>g.CacheKey))}");
            var direct=PredictorCrystalGoalLanguage.Solve(board,table,goals,true,TimeSpan.FromSeconds(5),sharedBound:directBound,rootCardSlots:true);
            Check(direct.Verdict!=CrystalOffsetVerdict.Incomplete,"Small direct goal-language query must close; cutoff cannot become a negative");
            Check((direct.Verdict==CrystalOffsetVerdict.Possible)==expected,
                $"Wing direct goal language and complete raw suffix truth agree: {string.Join(',',goals.Select(g=>g.CacheKey))}");
            if(expected)
            {
                positives++;
                Check(update.Projection.Available.TryGetValue(target,out var plan) &&
                    PredictorCrystalExplorer.VerifySelection(board,new(plan.Steps,PredictorCrystalExplorer.Replay(board,plan.Steps).Rewards),goals)!=null,
                    "Wing positive focused result retains a typed complete witness");
            }
            else negatives++;
            receipts.Add(new {selected,target,expected,update.Verdict,ms=timer.Elapsed.TotalMilliseconds,
                directVerdict=direct.Verdict,direct.Seconds,direct.GeometryNodes,direct.VerifiedWords,direct.NegativeKind});
        }
        Check(positives>0 && negatives>0,"Complete Wing oracle exercises actual positive and negative combinations");
        Check(frozen==JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions),"Raw Wing oracle and focus preserve source snapshot purity");
        int counter=source.State.Relics.SingleOrDefault(r=>r.Key.Entry=="PAELS_WING")?.RewardsSacrificed??-1;
        ProductionSearchReplay.Write(Path.Combine(output,$"paels-wing-small-{label??counter.ToString()}.json"),new {counter,label,includeBoardRelic,paths,endpoints=endpoints.Count,states,positives,negatives,
            giftEndpoints,narrowedGiftEndpoints,looseGiftCandidates,prefixGiftCandidates,
            observedPhysicalUpgradeEdges,changedInsertions,
            scope="Complete legal click/tool graph on controlled partially revealed native-size geometry; complete raw Enter/Exit/Take/Sacrifice graph; native generation parity is established separately",receipts});
        Console.WriteLine($"CRYSTAL_WING_SMALL_ORACLE counter={counter};paths={paths};endpoints={endpoints.Count};states={states};positive={positives};negative={negatives};giftEndpoints={giftEndpoints};narrowedGiftEndpoints={narrowedGiftEndpoints};looseGiftCandidates={looseGiftCandidates};prefixGiftCandidates={prefixGiftCandidates};complete=true");
        observePhysicalUpgrades?.Invoke(observedPhysicalUpgradeEdges,changedInsertions);
        return outcomes.ToImmutableArray();
    }
}
