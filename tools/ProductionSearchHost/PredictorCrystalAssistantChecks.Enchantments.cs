using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static string EnchantedIdentity(PredictorCard card)=>
        $"{card.Key.Entry}:{card.UpgradeLevel}:{card.Enchantment?.Entry}:{card.EnchantmentAmount}";

    // Fixed-endpoint experiment. This does not add enchantment goals to the
    // production candidate key, compile a global language or publish UI options.
    private static int CheckEnchantmentExperiment(string output,Func<string,string,PredictorCrystalSnapshot> create,
        Action<PredictorCrystalSnapshot,ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode,string,bool> native)
    {
        string[] relics=["WING_CHARM","FRESNEL_LENS","GLITTER"];
        IEnumerable<string[]> Orders(string[] available)=>available.Length==0?[[]]:
            available.SelectMany(first=>Orders(available.Where(x=>x!=first).ToArray()).Select(tail=>new[]{first}.Concat(tail).ToArray()));
        var orders=Enumerable.Range(0,8).SelectMany(mask=>Orders(relics.Where((_,i)=>(mask&(1<<i))!=0).ToArray()))
            .Select(o=>string.Join('+',o)).ToArray();
        string[] seeds=["000000000000","9TW7Y8N6663H"];
        ProductionSearchReplay.Write(Path.Combine(output,"enchantment-receipt.json"),new {seeds,orders,act=3,driftwood=true,
            scope="Controlled inventory orders, including counterfactual acquisition orders; fixed legal board endpoints and all reroll subsets; not global reachability"});
        var receipts=new List<object>();
        foreach(string seed in seeds) foreach(string order in orders)
        {
            var source=create(seed,order);string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
            var path=CardPath(source);var root=PredictorCrystalExplorer.Replay(source,path);
            Check(root.Rewards.Count(r=>r.Kind==PredictorRewardKind.Card)==3,"Enchantment fixture reveals exactly three card instances");
            var watch=Stopwatch.StartNew();var prefixes=PredictorCrystalRerolls.Prefixes(root).ToArray();
            Check(prefixes.Length==16,"Three single-use rerolls have sixteen ordered subsets");
            foreach(var node in prefixes) native(source,path,node,order,false);
            var taken=prefixes.Single(n=>n.Actions.OfType<RerollCardReward>().Count()==3 &&
                n.Actions.OfType<RerollCardReward>().Select(r=>r.RewardId).SequenceEqual(root.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).Select(r=>r.Id)));
            var takeRun=taken.Run.ForkCrystalRewards();var takeActions=taken.Actions;
            foreach(var reward in takeRun.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).ToArray())
            {
                ImmutableArray<PredictorInput> inputs=[new EnterCardSelection(reward.Id),new TakeReward(reward.Id,reward.Cards[0].Id)];
                foreach(var input in inputs) Check(takeRun.Submit(takeRun.Request!,input)==PredictorInputResult.Accepted,"Exact enchanted take remains legal");
                takeActions=takeActions.AddRange(inputs);
            }
            native(source,path,new(takeRun,takeActions),order,true);
            double nativeMs=watch.Elapsed.TotalMilliseconds;
            var slots=prefixes.Select(n=>n.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)
                .Select(r=>r.Cards.Select(EnchantedIdentity).ToHashSet()).ToArray()).ToArray();
            var universe=slots.SelectMany(s=>s.SelectMany(g=>g)).ToHashSet();
            int collapsed=universe.GroupBy(v=>string.Join(':',v.Split(':').Take(2))).Count(g=>g.Count()>1);
            var suffixes=prefixes.Select(n=>new {rerolls=n.Actions.OfType<RerollCardReward>().Select(r=>r.RewardId).ToArray(),
                eventCalls=n.Run.ExportCrystal().EventRng.Calls, nicheCalls=Niche(n.Run.Working).Calls,
                groups=n.Run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).Select(r=>r.Cards.Select(EnchantedIdentity).ToArray()).ToArray()}).ToArray();
            object? rawReceipt=null;
            if(seed==seeds[0] && order is "FRESNEL_LENS+WING_CHARM" or "WING_CHARM+FRESNEL_LENS+GLITTER" or "GLITTER+WING_CHARM")
            {
                watch.Restart();var raw=CheckNormalization(root,enchanted:true);
                var chosen=takeRun.Working.Deck.Where(id=>!root.Working.Deck.Contains(id)).Select(takeRun.Working.Card).Select(EnchantedIdentity).ToArray();
                var queries=new List<object>();
                for(int count=0;count<=3;count++)
                {
                    var selected=chosen.Take(count).ToArray();
                    var additions=universe.Where(x=>!selected.Contains(x) && slots.Any(s=>Supports(s,[..selected,x]))).ToHashSet();
                    var truth=universe.Where(x=>!selected.Contains(x) && raw.Any(row=>ContainsAll(row,selected.Append(x)))).ToHashSet();
                    Check(additions.SetEquals(truth),"Exact enchantment conditional candidates equal raw Enter/Exit/Reroll/Take oracle");
                    queries.Add(new {selected,possible=additions.Order().ToArray(),impossible=universe.Except(selected).Except(additions).Order().ToArray()});
                }
                rawReceipt=new {outcomes=raw.Count,ms=watch.Elapsed.TotalMilliseconds,queries};
            }
            var layerReceipt=CheckEnchantmentLayers(source,root,prefixes);
            Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Enchantment experiment preserves source");
            var receipt=new {seed,order,nativeMs,path,sourceNiche=Niche(source.State),initialNiche=Niche(root.Working),
                maxNicheDelta=prefixes.Max(n=>Niche(n.Run.Working).Calls)-Niche(source.State).Calls,
                exactVariants=universe.Count,identityLevelWithMultipleEnchantments=collapsed,suffixes,layerReceipt,rawReceipt};
            receipts.Add(receipt);
            ProductionSearchReplay.Write(Path.Combine(output,$"enchantment-{seed}-{(order==""?"none":order)}.json"),receipt);
            Console.WriteLine($"CRYSTAL_ENCHANTMENT_CASE seed={seed};order={order};nativePrefixes=16;deckExact=true;variants={universe.Count};collapsed={collapsed};initialNiche={Niche(root.Working).Calls-Niche(source.State).Calls};maxNiche={prefixes.Max(n=>Niche(n.Run.Working).Calls)-Niche(source.State).Calls};nativeMs={nativeMs:F2}");
        }
        ProductionSearchReplay.Write(Path.Combine(output,"enchantment-summary.json"),receipts);
        Console.WriteLine("CRYSTAL_ENCHANTMENT_EXPERIMENT_PASS cases=32;nativePrefixes=512;nativeTakes=32;fixedEndpointOnly=true;productionUnchanged=true");return 0;
    }

    private static PredictorStreamState Niche(PredictorState state)=>state.Streams.Single(s=>s.Stream==PredictorStream.Niche);
    private static bool ContainsAll(string row,IEnumerable<string> goals)
    {
        var remaining=row.Split('|').ToList();
        return goals.All(goal=>remaining.Remove(goal));
    }
    private static bool Supports(HashSet<string>[] slots,string[] goals)
    {
        bool Match(int index,int used)=>index==goals.Length || Enumerable.Range(0,slots.Length)
            .Any(i=>(used&(1<<i))==0 && slots[i].Contains(goals[index]) && Match(index+1,used|(1<<i)));
        return Match(0,0);
    }
    private static object CheckEnchantmentLayers(PredictorCrystalSnapshot source,PredictorRun root,CrystalSuffixNode[] prefixes)
    {
        var watch=Stopwatch.StartNew();var table=new CrystalReachabilityTable(source,includeRerolls:true);
        var rows=new Dictionary<(string Kind,int Offset,int Layer),(string[] Cards,int Consumed)>();
        var kinds=new[]{"CARD_COMMON","CARD_UNCOMMON","CARD_RARE"};
        foreach(string kind in kinds) for(int offset=0;offset+6<=table.MaximumDraws;offset++) for(int layer=0;layer<6;layer++)
        {
            var rng=source.EventRng.Restore();rng.Advance(offset);var niche=Niche(source.State).Restore();niche.Advance(layer);
            var state=source.State.WithRng(PredictorStream.Niche,niche);
            var cards=PredictorRewardGeneration.GenerateCards(source.Context,ref state,new(PredictorRewardKind.Card,CardCount:3,
                Odds:PredictorCardOdds.Uniform,Rarity:Enum.Parse<EffectCardRarity>(kind[5..],true),UseEventRng:true),true,rng);
            int consumed=Niche(state).Calls-niche.CallCount;
            Check(rng.CallCount-source.EventRng.Calls==offset+6 && consumed is 0 or 1,"Layer table records six event draws and zero/one Wing draw");
            rows.Add((kind,offset,layer),(cards.Select(EnchantedIdentity).ToArray(),consumed));
        }
        int transitions=0;
        foreach(var prefix in prefixes)
        {
            var run=root.ForkCrystalRewards();
            foreach(var action in prefix.Actions)
            {
                var before=run.Working;int offset=run.ExportCrystal().EventRng.Calls-source.EventRng.Calls;
                int layer=Niche(before).Calls-Niche(source.State).Calls;
                var reward=action is RerollCardReward reroll?run.Rewards.Single(r=>r.Id==reroll.RewardId):null;
                Check(run.Submit(run.Request!,action)==PredictorInputResult.Accepted,"Layer continuation input legal");
                if(reward==null) {Check(Niche(before)==Niche(run.Working),"Enter/Exit does not consume Niche");continue;}
                var row=rows[("CARD_"+reward.RerollRecipe!.Rarity!.Value.ToString().ToUpperInvariant(),offset,layer)];
                Check(row.Cards.SequenceEqual(run.Rewards.Single(r=>r.Id==reward.Id).Cards.Select(EnchantedIdentity)) &&
                    Niche(run.Working).Calls-Niche(before).Calls==row.Consumed,"Niche-layer table agrees with actual ordered reroll continuation");transitions++;
            }
        }
        var varying=rows.GroupBy(p=>(p.Key.Kind,p.Key.Offset)).Where(g=>g.Select(p=>string.Join('|',p.Value.Cards)).Distinct().Count()>1).ToArray();
        return new {rows=rows.Count,ms=watch.Elapsed.TotalMilliseconds,zeroDrawRows=rows.Count(p=>p.Value.Consumed==0),
            varyingOffsets=varying.Length,transitions,example=varying.Take(1).SelectMany(g=>g.Select(p=>new {p.Key.Kind,p.Key.Offset,p.Key.Layer,p.Value.Cards,p.Value.Consumed})).ToArray(),
            scope="Layer rows are optimistic coordinates, not independently reachable board positions; actual suffix transitions checked"};
    }
}
