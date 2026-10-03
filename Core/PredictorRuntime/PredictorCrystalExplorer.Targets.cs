using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalExplorer
{
    private sealed record RewardRecipe(ImmutableArray<int> Core,ImmutableArray<int> Potions,UInt128 Need);
    internal long DirectedRecipes { get; private set; }
    internal long DirectedNodes { get; private set; }
    internal long DirectedWitnesses { get; private set; }

    // Positive discovery only. Plan physical callback blocks at target-compatible
    // RNG offsets, then realize their two ordered phases with actual legal clicks.
    // Quotas/beam width limit suggestions, never establish a negative result;
    // the original full traversal still runs after this pass.
    private IEnumerable<CrystalRewardRoute?> TargetDirected(Position initial)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        bool WithinBudget()=>timer.ElapsedMilliseconds<300;
        // Necessary geometry only: selected cells separated by >2 on either
        // axis cannot share one Big reveal. Avoid an expensive exact relaxed
        // cover search while merely trying to suggest a positive recipe.
        bool RecipeCoverable(UInt128 need,int left)
        {
            if(need==0) return true;
            if(left<=0 || Bits(need)>9*left) return false;
            if(left==1) return FinishingBigCenters(need)!=0;
            int lower=0;
            while(need!=0)
            {
                if(++lower>left) return false;
                int cell=FirstBit(need),x=cell/11,y=cell%11;
                for(int a=Math.Max(0,x-2);a<=Math.Min(10,x+2);a++)
                    for(int b=Math.Max(0,y-2);b<=Math.Min(10,y+2);b++) need &= ~((UInt128)1 << (a*11+b));
            }
            return true;
        }
        bool Potion(int i)=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal);
        bool Core(int i)=>!Potion(i) && _source.Items[i].Kind!="CURSE";
        var prefixCore=initial.Order.Where(Core).ToImmutableArray();
        var prefixPotions=initial.Order.Where(Potion).ToImmutableArray();
        var future=Enumerable.Range(0,_items.Length).Where(i=>_source.Items[i].X>=0 &&
            _source.Items[i].Subscriptions>0 && (_items[i]&initial.Fog)!=0).ToArray();
        var futureCore=future.Where(Core).ToArray();
        var futurePotions=future.Where(Potion).ToArray();
        bool SlotMatches(CrystalRewardOption target,string kind,int minimum,int maximum)=>target.Kind switch {
            PredictorRewardKind.Card=>_reachability!.CardAt(kind,target,minimum,maximum),
            PredictorRewardKind.Potion=>_reachability!.PotionAt(kind,target.Key,minimum,maximum),
            // Relic identity still needs bags/RNG replay; this is an optimistic
            // recipe suggestion and cannot publish a specific relic on its own.
            PredictorRewardKind.Relic=>kind=="RELIC",_=>false };
        bool SourceMatches(CrystalRewardOption target,int item)=>target.Kind switch {
            PredictorRewardKind.Card=>_reachability!.CardAt(_source.Items[item].Kind,target,0,_reachability.MaximumDraws),
            PredictorRewardKind.Potion=>_reachability!.PotionAt(_source.Items[item].Kind,target.Key,0,_reachability.MaximumDraws),
            PredictorRewardKind.Relic=>_source.Items[item].Kind=="RELIC",_=>false };
        // Group interchangeable suggestions by their possible physical sources.
        // A found row may prove many suggestions; subsequent recipes prefer the
        // still-unwitnessed ones rather than repeating the same easy candidate.
        var groups=_queryCandidates.Where(c=>!_proven.ContainsKey(c)).GroupBy(c=>
            c.Kind+":"+string.Join(',',Enumerable.Range(0,_items.Length).Where(i=>SourceMatches(c,i))))
            .Select(g=>g.ToImmutableArray()).ToArray();
        var cursors=groups.Select(g=>Recipes(g).GetEnumerator()).ToArray();
        var active=Enumerable.Repeat(true,cursors.Length).ToArray();
        var visitedRecipes=new HashSet<string>();
        int attempts=0;
        try
        {
            while(active.Any(a=>a) && attempts<64 && WithinBudget())
                for(int g=0;g<cursors.Length && attempts<64;g++)
                {
                    if(!active[g]) continue;
                    if(groups[g].All(_proven.ContainsKey) || !cursors[g].MoveNext()) { active[g]=false;continue; }
                    var recipe=cursors[g].Current;
                    timer.Stop();yield return null;timer.Start(); // exclude paused time
                    if(!WithinBudget()) yield break;
                    if(recipe==null) continue;
                    string key=string.Join(',',recipe.Potions)+"|"+string.Join(',',recipe.Core);
                    if(!visitedRecipes.Add(key)) continue;
                    attempts++;DirectedRecipes++;
                    var layers=Enumerable.Range(0,_source.Remaining+1).Select(_=>new List<Position>()).ToArray();
                    layers[0].Add(initial);
                    bool Prefix(ImmutableArray<int> actual,ImmutableArray<int> expected)=>
                        actual.Length<=expected.Length && actual.SequenceEqual(expected.Take(actual.Length));
                    bool CoreCompatible(ImmutableArray<int> actual)=>Prefix(actual,recipe.Core) || actual.Take(recipe.Core.Length).SequenceEqual(recipe.Core);
                    // Incidental core callbacks do not shift initial offers,
                    // but DO shift the endpoint from which rerolls start. An
                    // expanded-domain recipe therefore fixes the whole word.
                    bool Compatible(Position node)=>(_reachability!.IncludesRerolls
                        ?Prefix(node.Order.Where(Core).ToImmutableArray(),recipe.Core)
                        :CoreCompatible(node.Order.Where(Core).ToImmutableArray())) &&
                        Prefix(node.Order.Where(Potion).ToImmutableArray(),recipe.Potions) &&
                        RecipeCoverable(node.Fog & recipe.Need,_source.Remaining-node.Path.Length);
                    for(int depth=0;depth<=_source.Remaining;depth++)
                    {
                        var beam=layers[depth].GroupBy(n=>(n.Fog,n.RewardKey))
                            .Select(gr=>gr.First()).OrderBy(n=>Bits(n.Fog & recipe.Need)).ThenByDescending(n=>n.Order.Length).Take(6).ToArray();
                        foreach(var node in beam)
                        {
                            DirectedNodes++;timer.Stop();yield return null;timer.Start();
                            if(!WithinBudget()) yield break;
                            if(node.Order.Where(Core).Take(recipe.Core.Length).SequenceEqual(recipe.Core) && node.Order.Where(Potion).SequenceEqual(recipe.Potions))
                            {
                                var row=Row(node);
                                if(row!=null) { DirectedWitnesses++;timer.Stop();yield return row;timer.Start(); }
                                continue;
                            }
                            // The recipe already fixes target-compatible draws.
                            // Re-running the broad query bound for every click
                            // spends time proving unrelated negatives. Only this
                            // positive heuristic skips it; Row still replays truth.
                            if(depth>=_source.Remaining) continue;
                            if(_islands==null || depth==_source.Remaining-1) layers[depth+1].AddRange(Moves(node,Compatible,checkQuery:false));
                            else foreach(var batch in _islands.Suggestions(node.Fog,recipe.Need,_source.Remaining-depth))
                            {
                                timer.Stop();yield return null;timer.Start();if(!WithinBudget()) yield break;
                                foreach(var move in batch)
                                {
                                    var order=node.Order.AddRange(move.Completed);var rewardKey=node.RewardKey;
                                    // A physical source contributes one contiguous callback block.
                                    foreach(int item in move.Completed.Distinct()) rewardKey=AppendItem(rewardKey,item);
                                    var next=new Position(move.Fog,order,node.Path.AddRange(move.Steps),node.Score+move.Gain,
                                        node.Centers|move.Centers,string.Join(',',order),rewardKey);
                                    if(Compatible(next)) layers[next.Path.Length].Add(next);
                                }
                            }
                        }
                    }
                }
        }
        finally { foreach(var cursor in cursors) cursor.Dispose(); }

        IEnumerable<RewardRecipe?> Recipes(ImmutableArray<CrystalRewardOption> additions)
        {
            var goals=_selected.Select(s=>ImmutableArray.Create(s)).Append(additions).ToArray();
            int totalNodes=0;
            // Exact identities in each phase are cheap lookup operations. Match
            // distinct card/potion callback slots, not a union of offer contents.
            int Matched(ImmutableArray<int> core,ImmutableArray<int> potions,bool includeFuture=false)
            {
                var slots=new List<(string Kind,int Minimum,int Maximum)>();int offset=potions.Length;
                for(int p=0;p<potions.Length;p++) slots.Add((_source.Items[potions[p]].Kind,p,p));
                foreach(int i in core) { string kind=_source.Items[i].Kind;slots.Add((kind,offset,offset));offset+=CrystalReachabilityTable.Draws(kind); }
                int maximumEnd=offset;
                if(includeFuture)
                {
                    var remaining=futureCore.Where(i=>!core.Contains(i)).ToArray();
                    int draws=remaining.Sum(i=>CrystalReachabilityTable.Draws(_source.Items[i].Kind)*_source.Items[i].Subscriptions);
                    maximumEnd+=draws;
                    foreach(int i in remaining)
                    {
                        var item=_source.Items[i];int step=CrystalReachabilityTable.Draws(item.Kind);
                        for(int n=0;n<item.Subscriptions;n++)
                            slots.Add((item.Kind,offset+n*step,offset+draws-(item.Subscriptions-n)*step));
                    }
                }
                var cardSlots=Enumerable.Range(0,slots.Count).Where(s=>slots[s].Kind.StartsWith("CARD_",StringComparison.Ordinal)).ToArray();
                int end=offset;
                int Count(bool relaxedSuffix)
                {
                    var owners=Enumerable.Repeat(-1,slots.Count).ToArray();
                    bool Edge(int goal,int slot)=>goals[goal].Any(t=>(goal<goals.Length-1 || !_proven.ContainsKey(t)) &&
                        (SlotMatches(t,slots[slot].Kind,slots[slot].Minimum,slots[slot].Maximum) || relaxedSuffix &&
                        t.Kind==PredictorRewardKind.Card && SlotMatches(t,slots[slot].Kind,end,
                            maximumEnd+6*Math.Max(0,cardSlots.Length-1))));
                    bool Assign(int goal,bool[] seen)
                    {
                        for(int s=0;s<slots.Count;s++) if(!seen[s] && Edge(goal,s))
                        { seen[s]=true;if(owners[s]<0 || Assign(owners[s],seen)) { owners[s]=goal;return true; } }
                        return false;
                    }
                    int count=0;
                    for(int g=0;g<goals.Length;g++)
                    {
                        // Any-relic and a particular relic may refer to one reward.
                        // Actual coexistence/identity is checked by VerifySelection.
                        if(goals[g].All(t=>t.Kind==PredictorRewardKind.Relic))
                        { if(slots.Any(s=>s.Kind=="RELIC")) count++; }
                        else if(Assign(g,new bool[slots.Count])) count++;
                    }
                    return count;
                }
                if(!_reachability!.IncludesRerolls) return Count(false);
                // Future slots are an optimistic recipe bound. A complete word
                // instead binds each reward instance to its initial offers OR
                // one ordered suffix offset. This remains positive discovery;
                // quotas and table-only matches never establish Impossible.
                if(includeFuture || cardSlots.Length>6) return Count(true);
                int best=Count(false);
                void Suffix(int used,int depth)
                {
                    if(best==goals.Length || !WithinBudget()) return;
                    for(int n=0;n<cardSlots.Length;n++) if((used&(1<<n))==0)
                    {
                        int i=cardSlots[n];var previous=slots[i];slots[i]=(previous.Kind,end+6*depth,end+6*depth);
                        best=Math.Max(best,Count(false));Suffix(used|(1<<n),depth+1);slots[i]=previous;
                        if(best==goals.Length) return;
                    }
                }
                Suffix(0,0);return best;
            }
            ImmutableArray<int> Append(ImmutableArray<int> order,int i)=>order.AddRange(Enumerable.Repeat(i,_source.Items[i].Subscriptions));
            IEnumerable<ImmutableArray<int>> PotionOrders(ImmutableArray<int> chosen,int count)
            {
                if(chosen.Length==count) { yield return chosen;yield break; }
                foreach(int i in futurePotions)
                    if(!chosen.Contains(i)) foreach(var order in PotionOrders(chosen.Add(i),count)) yield return order;
            }
            for(int count=0;count<=futurePotions.Length && totalNodes<1600;count++)
                foreach(var chosen in PotionOrders([],count))
                {
                    if(totalNodes>=1600 || additions.All(_proven.ContainsKey)) yield break;
                    var potions=prefixPotions;UInt128 need=0;
                    foreach(int i in chosen) { potions=Append(potions,i);need |= _items[i]&initial.Fog; }
                    yield return null;
                    if(!RecipeCoverable(need,_source.Remaining)) continue;
                    int phaseNodes=0;
                    foreach(var recipe in Search(prefixCore,need)) yield return recipe;

                    IEnumerable<RewardRecipe?> Search(ImmutableArray<int> core,UInt128 required)
                    {
                        if(phaseNodes++>=160 || totalNodes++>=1600 || additions.All(_proven.ContainsKey)) yield break;
                        yield return null;
                        if(Matched(core,potions)==goals.Length) { yield return new(core,potions,required);yield break; }
                        // A committed source at the wrong offset cannot be used
                        // again. Do not spend this recipe's discovery quota after
                        // even optimistic remaining callback slots cannot match.
                        if(Matched(core,potions,includeFuture:true)<goals.Length) yield break;
                        var choices=new List<(ImmutableArray<int> Order,UInt128 Need,int Score,int Draws)>();
                        foreach(int i in futureCore)
                        {
                            if(core.Contains(i)) continue;
                            UInt128 combined=required | (_items[i]&initial.Fog);
                            if(!RecipeCoverable(combined,_source.Remaining)) continue;
                            var next=Append(core,i);
                            choices.Add((next,combined,Matched(next,potions),CrystalReachabilityTable.Draws(_source.Items[i].Kind)));
                        }
                        foreach(var choice in choices.OrderByDescending(c=>c.Score).ThenBy(c=>c.Draws).ThenBy(c=>Bits(c.Need)))
                            foreach(var recipe in Search(choice.Order,choice.Need)) yield return recipe;
                    }
                }
        }
    }
}
