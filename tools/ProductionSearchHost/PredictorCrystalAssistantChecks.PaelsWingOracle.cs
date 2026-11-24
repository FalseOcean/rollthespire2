using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private sealed record WingRawState(PredictorRun Run,ImmutableDictionary<long,PredictorCard> CardEvidence);

    private static void CheckWingModeledGiftScope(PredictorCrystalSnapshot source)
    {
        string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
        ImmutableArray<ModelKey> keys=[new("RELIC","ANCHOR"),new("RELIC","STRAWBERRY"),new("RELIC","VAJRA")];
        var unknown=new ModelKey("RELIC","CRYSTAL_UNKNOWN_GIFT");var special=new ModelKey("RELIC","ROYAL_STAMP");
        var context=source.Context with {Crystal=source.Context.Crystal! with {
            EligibleRelics=source.Context.Crystal!.EligibleRelics.Union(keys).Add(unknown).Add(special),
            UnmodeledSources=[new("CARD","CRYSTAL_UNRELATED_CATALOG_CARD")]}};
        var bag=new PredictorRelicBag(false,keys.Select(k=>new PredictorBagEntry(k,"Common")).ToImmutableArray(),
            [new("Common",keys)],[]);
        var state=source.State with {PersonalBag=bag};var pulledState=state;
        var expected=PredictorRewardGeneration.PullRelic(context,ref pulledState);
        var narrow=PredictorCrystalWing.ModeledGiftAvailability(context,state,1);
        var wide=PredictorCrystalWing.GiftAvailability(context,state,1);
        Check(narrow.SetEquals([expected]) && wide.IsSupersetOf(narrow) && wide.Count>narrow.Count,
            "Unrelated catalog unknown permits only the query-scoped first gift refinement");
        int wing=state.Relics.FindIndex(r=>r.Key.Entry=="PAELS_WING");
        var twoSource=source with {Context=context,State=state with {Relics=state.Relics.SetItem(wing,state.Relics[wing] with {RewardsSacrificed=1})}};
        var twoPath=CardPath(twoSource);var twoEntry=PredictorCrystalExplorer.Replay(twoSource,twoPath);
        var slots=twoEntry.Rewards.Select((reward,index)=>(reward,index)).Where(p=>p.reward.Kind==PredictorRewardKind.Card).Select(p=>(p.index,true)).ToArray();
        Check(slots.Length==3,"Two-gift scope has three actual card reward slots");
        var twoActual=ManualWingPlan(twoSource,twoPath,slots);
        var two=PredictorCrystalWing.ModeledGiftAvailability(context,twoEntry.Working,2);
        Check(two.SetEquals(twoActual.Plan.SacrificeRelics) && two.Count<PredictorCrystalWing.GiftAvailability(context,twoEntry.Working,2).Count,
            "Modeled two-gift set is strict and equals actual ordered sacrifices");
        var takeSource=source with {Context=context,State=state};
        var entry=PredictorCrystalExplorer.Replay(takeSource,CardPath(takeSource));
        var offered=entry.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0];
        var taken=PredictorCrystalExplorer.TakeCards(entry,[CrystalRewardOption.ForCard(offered,false)]);
        Check(taken!=null,"Scoped first gift check performs an actual typed card take");
        var before=PredictorCrystalWing.ModeledGiftAvailability(context,entry.Working,1);
        var after=PredictorCrystalWing.ModeledGiftAvailability(context,taken!.Value.Run.Working,1);
        Check(after.SetEquals(before) && before.Count==1 &&
            JsonSerializer.Serialize(entry.Working.PersonalBag,ProductionSearchReplay.JsonOptions)==JsonSerializer.Serialize(taken.Value.Run.Working.PersonalBag,ProductionSearchReplay.JsonOptions) &&
            entry.Working.Streams.Single(s=>s.Stream==PredictorStream.Rewards)==taken.Value.Run.Working.Streams.Single(s=>s.Stream==PredictorStream.Rewards),
            "Actual card take preserves exact first gift, Rewards stream and relic bag");
        var tail=state with {PersonalBag=bag with {Buckets=[new("Common",keys.Add(unknown))]}};
        var bagOnlyContext=context with {Crystal=context.Crystal! with {UnmodeledSources=[]}};
        var tailNarrow=PredictorCrystalWing.ModeledGiftAvailability(bagOnlyContext,tail,2);
        Check(tailNarrow.SetEquals(PredictorCrystalWing.ModeledGiftAvailability(context,state,2)) &&
            PredictorCrystalWing.GiftAvailability(bagOnlyContext,tail,2).SetEquals(PredictorCrystalWing.GiftUpper(bagOnlyContext,tail,2)) &&
            tailNarrow.Count<PredictorCrystalWing.GiftAvailability(bagOnlyContext,tail,2).Count,
            "Unvisited unknown bag tail permits strict query evidence while general availability retains its whole-bag guard");
        ImmutableArray<PredictorBagBucket> AllBuckets(ImmutableArray<ModelKey> entries)=>
            [new("Common",entries),new("Uncommon",entries),new("Rare",entries)];
        foreach(var (guarded,count) in new[]{
            (state with {PersonalBag=bag with {Buckets=AllBuckets([unknown,..keys])}},1),
            (state with {PersonalBag=bag with {Buckets=AllBuckets([special,..keys])}},1),
            (state with {PersonalBag=bag with {Buckets=AllBuckets([keys[0],unknown,keys[1]])}},2),
            (state with {PersonalBag=bag with {Buckets=AllBuckets([keys[0],special,keys[1]])}},2),
            (state with {PersonalBag=bag with {RefreshAllowed=true,HasOriginalCatalog=false}},1)})
            Check(PredictorCrystalWing.ModeledGiftAvailability(context,guarded,count).SetEquals(PredictorCrystalWing.GiftUpper(context,guarded,count)),
                "Actual unknown/special gift or missing refresh metadata returns the original full-scope upper, including after a known prefix");
        Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Modeled gift scope check preserves source");
        Console.WriteLine("CRYSTAL_WING_MODELED_GIFT_SCOPE_PASS unrelatedCatalog=true;unvisitedUnknownTail=true;unknownGiftGuard=true;specialGiftGuard=true;partialPrefixUpper=true;refreshGuard=true;twoGiftsExact=true;takeExactFirst=true;premise=base-properties-only-no-custom-hooks");
    }

    private static ImmutableArray<ImmutableArray<CrystalRewardOption>> CheckPaelsWingSmallOracle(PredictorCrystalSnapshot source,string output,
        bool includeBoardRelic=true,string? label=null,Action<int,int>? observePhysicalUpgrades=null,bool includeThirdCard=false,
        bool includeCurse=false,bool avoidCurse=true,int curseSubscriptions=1)
    {
        // A late, partially uncovered board: each remaining item has one hidden
        // cell. All other cells of its native-sized footprint are uncovered.
        // Distant cells ensure both tools remain legal without merging items.
        var items=ImmutableArray.Create(new PredictorCrystalItem("CARD_COMMON",2,2,0,0,1,false),
            new PredictorCrystalItem("CARD_UNCOMMON",2,2,0,9,1,false),new PredictorCrystalItem("RELIC",4,4,7,0,1,false));
        if(includeThirdCard) items=items.Add(new("CARD_RARE",2,2,7,7,1,false));
        if(includeCurse) items=items.Add(new("CURSE",2,2,3,3,curseSubscriptions,false));
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
        var curseFacts=new Dictionary<string,(string Fact,int Curses)>();int cursePaths=0,curseTransparentComparisons=0;
        int initialDoubts=board.State.Deck.Select(board.State.Card).Count(c=>c.Key.Entry=="DOUBT");
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
            // Empty/Curse-only settlement may close the event and release its
            // RNG. A spare reveal observes geometry only; rewards use real run.
            var current=path.Length==board.Remaining
                ?PredictorCrystalExplorer.Replay(board with {Remaining=board.Remaining+1},path).ExportCrystal():run.ExportCrystal();
            int curses=current.Items.Count(i=>i.Kind=="CURSE" && i.Revealed);
            if(avoidCurse && curses>0) return;
            if(path.Length==board.Remaining)
            {
                paths++;if(run.Phase!=PredictorPhase.Rewards) return;
                if(includeCurse)
                {
                    Check(run.Working.Deck.Select(run.Working.Card).Count(c=>c.Key.Entry=="DOUBT")-initialDoubts==curses,
                        "One physical Curse adds one Doubt regardless of subscription count");
                    if(curses>0) cursePaths++;
                    string word=string.Join(',',current.Revealed.Where(i=>current.Items[i].Kind!="CURSE"));
                    string fact=JsonSerializer.Serialize(new {EventRng=run.ExportCrystal().EventRng,
                        Rewards=run.Working.Streams.Single(s=>s.Stream==PredictorStream.Rewards),
                        Niche=run.Working.Streams.Single(s=>s.Stream==PredictorStream.Niche),run.Working.PersonalBag,
                        Offers=run.Rewards.Select(r=>new {r.Kind,r.Key,r.GoldAmount,Cards=r.Cards.Select(c=>new {c.Key,c.UpgradeLevel,c.Enchantment,c.EnchantmentAmount})})},ProductionSearchReplay.JsonOptions);
                    if(curseFacts.TryGetValue(word,out var prior))
                    {Check(prior.Fact==fact,"Curse changes Deck but not reward words, RNG, Niche, bag or offers");if(prior.Curses!=curses) curseTransparentComparisons++;}
                    else curseFacts.Add(word,(fact,curses));
                }
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
        var reach=new PredictorCrystalReachability(board,avoidCurse,includeRerolls:false,includeEnchantments:true,workers:1);
        var directBound=new PredictorCrystalOffsetBound(board,table,avoidCurse);
        bool memoProfile=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_MEMO_PROFILE")=="1";
        long memoWordHits=0,memoAbstractSuppressed=0;
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
            var direct=PredictorCrystalGoalLanguage.Solve(board,table,goals,avoidCurse,TimeSpan.FromSeconds(5),sharedBound:directBound,rootCardSlots:true,profileCompilation:memoProfile);
            if(direct.GeometryMemoProfile is {} memo)
            {memoWordHits+=memo.WordHits;memoAbstractSuppressed+=memo.AbstractMemoSuppressedByWordHits;}
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
        if(includeCurse && !avoidCurse) Check(cursePaths>0 && curseTransparentComparisons>0,"Allow-Curse raw paths cover both physical Curse histories for equal reward words");
        Check(frozen==JsonSerializer.Serialize(board,ProductionSearchReplay.JsonOptions),"Raw Wing oracle and focus preserve source snapshot purity");
        int counter=source.State.Relics.SingleOrDefault(r=>r.Key.Entry=="PAELS_WING")?.RewardsSacrificed??-1;
        ProductionSearchReplay.Write(Path.Combine(output,$"paels-wing-small-{label??counter.ToString()}.json"),new {counter,label,includeBoardRelic,includeCurse,avoidCurse,curseSubscriptions,cursePaths,curseTransparentComparisons,paths,endpoints=endpoints.Count,states,positives,negatives,
            giftEndpoints,narrowedGiftEndpoints,looseGiftCandidates,prefixGiftCandidates,
            observedPhysicalUpgradeEdges,changedInsertions,
            scope="Complete legal click/tool graph on controlled partially revealed native-size geometry; complete raw Enter/Exit/Take/Sacrifice graph; native generation parity is established separately",receipts});
        Console.WriteLine($"CRYSTAL_WING_SMALL_ORACLE counter={counter};paths={paths};endpoints={endpoints.Count};states={states};positive={positives};negative={negatives};giftEndpoints={giftEndpoints};narrowedGiftEndpoints={narrowedGiftEndpoints};looseGiftCandidates={looseGiftCandidates};prefixGiftCandidates={prefixGiftCandidates};complete=true");
        if(memoProfile) Console.WriteLine($"CRYSTAL_LANGUAGE_MEMO_DEPENDENCY raw=wing;label={label};wordHits={memoWordHits};abstractSuppressed={memoAbstractSuppressed}");
        if(includeCurse) Console.WriteLine($"CRYSTAL_CURSE_RAW_PASS avoid={avoidCurse};subscriptions={curseSubscriptions};cursePaths={cursePaths};transparentComparisons={curseTransparentComparisons};sameBoundTruth=true;oneDoubtPerPhysical=true");
        observePhysicalUpgrades?.Invoke(observedPhysicalUpgradeEdges,changedInsertions);
        return outcomes.ToImmutableArray();
    }
}
