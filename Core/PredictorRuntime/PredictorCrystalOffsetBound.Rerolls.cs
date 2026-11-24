using System.Collections.Immutable;
using System.Numerics;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    private readonly record struct RerollSlot(string Kind,int InitialHits);

    // Opt-in research domain. Initial card matches are deferred until the whole
    // chosen callback word is complete. Each instance then retains its initial
    // offer OR one reroll at an ordered suffix position, never both. Cover/order
    // restrictions remain relaxed; Possible is not a click/obtain witness.
    private CrystalOffsetReceipt SolveRerolls(ImmutableArray<CrystalRewardOption> goals,int stateLimit,
        ImmutableArray<int> revealed=default,int? eligiblePotions=null,int? eligibleCore=null,List<UInt128>? completions=null,
        List<CrystalRewardRequirement>? requirements=null,
        Func<CrystalRewardRequirement,bool>? terminalVisitor=null,Func<bool>? keepGoing=null)
    {
        if(revealed.IsDefault) revealed=_source.Revealed;
        int allowedPotions=0,allowedCore=0;
        for(int i=0;i<_potions.Length;i++) if(!revealed.Contains(_potions[i].Item)) allowedPotions|=1<<i;
        for(int i=0;i<_core.Length;i++) if(!revealed.Contains(_core[i].Item)) allowedCore|=1<<i;
        // Prefix facts retain all already completed items in the global cover.
        // Individual remaining eligibility is proved against current fog/steps
        // by the caller; joint geometry is deliberately relaxed to the root.
        UInt128 prefixNeed=_potionNeeds[(_potionNeeds.Length-1)&~allowedPotions]|_coreNeeds[(_coreNeeds.Length-1)&~allowedCore];
        allowedPotions &= eligiblePotions??int.MaxValue;
        allowedCore &= eligibleCore??int.MaxValue;
        int states=0,all=(1<<goals.Length)-1,successfulWords=0;bool limited=false;
        int cardGoals=Enumerable.Range(0,goals.Length).Where(g=>goals[g].Kind==PredictorRewardKind.Card).Aggregate(0,(m,g)=>m|(1<<g));
        int potionGoals=Enumerable.Range(0,goals.Length).Where(g=>goals[g].Kind==PredictorRewardKind.Potion).Aggregate(0,(m,g)=>m|(1<<g));
        bool preserveWords=requirements!=null || terminalVisitor!=null;
        bool Admit() { if(states>=stateLimit || keepGoing?.Invoke()==false) { limited=true;return false; } states++;return true; }
        var hitMemo=new Dictionary<(string Kind,int Offset),int>();
        int CardHits(string kind,int offset)
        {
            if(hitMemo.TryGetValue((kind,offset),out int known)) return known;
            int hit=0;for(int g=0;g<goals.Length;g++)
                if((cardGoals&(1<<g))!=0 && _table.CardAt(kind,goals[g],offset,offset)) hit|=1<<g;
            hitMemo.Add((kind,offset),hit);return hit;
        }
        IEnumerable<int> NonCard(string kind,int offset,int matched,ModelKey relic=default)
        {
            int hit=0;for(int g=0;g<goals.Length;g++)
            {
                if((matched&(1<<g))!=0) continue;
                bool yes=goals[g].Kind switch {
                    PredictorRewardKind.Potion=>kind.StartsWith("POTION_",StringComparison.Ordinal) && _table.PotionAt(kind,goals[g].Key,offset,offset),
                    PredictorRewardKind.Relic=>kind=="RELIC" && (!goals[g].Key.IsValid || goals[g].Key==relic),_=>false };
                if(yes) hit|=1<<g;
            }
            if(hit==0 || kind=="RELIC") { yield return matched|hit;yield break; }
            for(int g=0;g<goals.Length;g++) if((hit&(1<<g))!=0) yield return matched|(1<<g);
        }
        static string Summary(IEnumerable<RerollSlot> slots)=>string.Join(';',slots.OrderBy(s=>s.Kind,StringComparer.Ordinal).ThenBy(s=>s.InitialHits).Select(s=>$"{s.Kind}:{s.InitialHits}"));
        var terminalMemo=new Dictionary<(int Offset,string Slots),bool>();
        bool Finish(int offset,ImmutableArray<RerollSlot> input)
        {
            if(cardGoals==0) return true;
            if(input.Length<BitOperations.PopCount((uint)cardGoals)) return false;
            if(input.Length>16) { limited=true;return false; } // work limit, not product exclusion
            var slots=input.OrderBy(s=>s.Kind,StringComparer.Ordinal).ThenBy(s=>s.InitialHits).ToArray();
            var key=(offset,Summary(slots));if(terminalMemo.TryGetValue(key,out bool cached)) return cached;
            var visited=new HashSet<string>();
            bool Matches(int[] offers)
            {
                var owners=Enumerable.Repeat(-1,offers.Length).ToArray();
                bool Assign(int goal,bool[] seen)
                {
                    for(int i=0;i<offers.Length;i++) if(!seen[i] && (offers[i]&(1<<goal))!=0)
                    { seen[i]=true;if(owners[i]<0 || Assign(owners[i],seen)) { owners[i]=goal;return true; } }
                    return false;
                }
                return Enumerable.Range(0,goals.Length).Where(g=>(cardGoals&(1<<g))!=0).All(g=>Assign(g,new bool[offers.Length]));
            }
            bool Suffix(int used,int[] offers)
            {
                if(Matches(offers)) return true;
                if(limited || !visited.Add(used+":"+string.Join(',',offers)) || !Admit()) return false;
                int nextOffset=offset+6*BitOperations.PopCount((uint)used);
                for(int i=0;i<slots.Length;i++) if((used&(1<<i))==0)
                {
                    var next=(int[])offers.Clone();next[i]=CardHits(slots[i].Kind,nextOffset);
                    if(Suffix(used|(1<<i),next)) return true;
                }
                return false;
            }
            var initialOffers=slots.Select(s=>s.InitialHits).ToArray();
            bool result=_table.IncludesRerolls?Suffix(0,initialOffers):Matches(initialOffers);
            if(!limited && terminalMemo.Count<100_000) terminalMemo.Add(key,result);
            return result;
        }
        var revealedPotions=revealed.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)).ToArray();
        var prefixCore=revealed.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal) && _source.Items[i].Kind!="CURSE").ToArray();
        var potionMatched=new HashSet<int>{0};int prefixPotion=0;
        foreach(int i in revealedPotions) { potionMatched=potionMatched.SelectMany(m=>NonCard(_source.Items[i].Kind,prefixPotion,m)).ToHashSet();prefixPotion++; }
        int prefixDraws=prefixCore.Sum(i=>CrystalReachabilityTable.Draws(_source.Items[i].Kind));
        List<int>? potionWord=preserveWords?[]:null,coreWord=preserveWords?[]:null;
        for(int potionMask=0;potionMask<_potionCalls.Length && !limited;potionMask++)
        {
            if((potionMask&~allowedPotions)!=0) continue;
            UInt128 need=prefixNeed|_potionNeeds[potionMask];if(!CanCover(need,_source.Remaining)) continue;
            int finalPotions=prefixPotion+_potionCalls[potionMask];
            var coreVisited=new HashSet<(int Used,int Matched,int Bag,string Cards)>();
            var potionVisited=new HashSet<(int Used,int Matched)>();
            bool Core(int used,int matched,int bag,ImmutableArray<RerollSlot> slots)
            {
                int offset=finalPotions+prefixDraws+_coreDraws[used];
                if((matched|cardGoals)==all && Finish(offset,slots))
                {
                    if(terminalVisitor!=null)
                    {
                        successfulWords++;
                        if(terminalVisitor(new(potionWord!.ToImmutableArray(),coreWord!.ToImmutableArray()))) return true;
                        // Exact final words cannot use prefix dominance. A later
                        // reward can change the final offers or realize a layout
                        // that cannot stop at this earlier accepting prefix.
                    }
                    else
                    {
                        if(requirements!=null)
                        {
                            successfulWords++;
                            var p=potionWord!.ToImmutableArray();var c=coreWord!.ToImmutableArray();
                            // A successful reward prefix is a necessary condition for
                            // every extension of this same branch, not a final offer
                            // promise. Geometry may finish additional rewards later.
                            if(!requirements.Any(r=>WordPrefix(r.Potions,p) && WordPrefix(r.Core,c)))
                            {
                                requirements.RemoveAll(r=>WordPrefix(p,r.Potions) && WordPrefix(c,r.Core));
                                requirements.Add(new(p,c));
                            }
                            return p.IsEmpty && c.IsEmpty;
                        }
                        if(completions==null) return true;
                        // Every relaxed rewarding endpoint contributes its required
                        // future physical cells. Keep a covering antichain: a valid
                        // subset is enough for this necessary-condition proof.
                        UInt128 required=_potionNeeds[potionMask]|_coreNeeds[used];
                        if(!completions.Any(c=>(c&required)==c))
                        {
                            completions.RemoveAll(c=>(c&required)==required);
                            completions.Add(required);
                        }
                        // Further rewards from this branch only add required cells.
                        // Their geometric obligation is dominated by this endpoint.
                        return required==0;
                    }
                }
                var coreKey=(used,matched,bag,Summary(slots));
                // Failed reward states are reusable across physical prefixes.
                // Successful states cannot be merged: each prefix may impose a
                // different necessary order on the actual board.
                if(limited || (!preserveWords?!coreVisited.Add(coreKey):coreVisited.Contains(coreKey)) || !Admit()) return false;
                int before=successfulWords;
                for(int remaining=allowedCore&~used;remaining!=0;remaining &= remaining-1)
                {
                    int bit=BitOperations.TrailingZeroCount((uint)remaining),next=used|(1<<bit);var block=_core[bit];
                    if(!CanCover(need|_coreNeeds[next],_source.Remaining)) continue;
                    if(coreWord!=null) for(int n=0;n<block.Calls;n++) coreWord.Add(block.Item);
                    foreach(var r in Apply(block.Kind,block.Calls,offset,matched,bag,slots))
                        if(Core(next,r.Matched,r.Bag,r.Cards)) return true;
                    if(coreWord!=null) coreWord.RemoveRange(coreWord.Count-block.Calls,block.Calls);
                }
                if(preserveWords && !limited && successfulWords==before) coreVisited.Add(coreKey);
                return false;
            }
            IEnumerable<(int Matched,int Bag,ImmutableArray<RerollSlot> Cards)> Apply(string kind,int calls,int offset,int matched,int bag,ImmutableArray<RerollSlot> slots)
            {
                if(kind.StartsWith("CARD_",StringComparison.Ordinal))
                {
                    for(int n=0;n<calls;n++) slots=slots.Add(new(kind,CardHits(kind,offset+6*n)));
                    yield return (matched,bag,slots);yield break;
                }
                var matches=new HashSet<int>{matched};int nextBag=bag;ImmutableArray<ModelKey> keys=[];
                if(kind=="RELIC") (nextBag,keys)=Pull(bag,offset,calls);
                for(int n=0;n<calls;n++) matches=matches.SelectMany(m=>NonCard(kind,offset+n,m,kind=="RELIC"?keys[n]:default)).ToHashSet();
                foreach(int m in matches) yield return (m,nextBag,slots);
            }
            bool BeginCore(int matched)
            {
                if((matched&potionGoals)!=potionGoals) return false;
                var current=new List<(int Matched,int Bag,ImmutableArray<RerollSlot> Cards)>{(matched,0,[])};int offset=finalPotions;
                foreach(int i in prefixCore)
                { string kind=_source.Items[i].Kind;current=current.SelectMany(s=>Apply(kind,1,offset,s.Matched,s.Bag,s.Cards)).ToList();offset+=CrystalReachabilityTable.Draws(kind); }
                return current.Any(s=>Core(0,s.Matched,s.Bag,s.Cards));
            }
            bool Potions(int used,int matched)
            {
                if(used==potionMask) return BeginCore(matched);
                if(limited || (!preserveWords?!potionVisited.Add((used,matched)):potionVisited.Contains((used,matched))) || !Admit()) return false;
                int before=successfulWords;
                for(int remaining=potionMask&~used;remaining!=0;remaining &= remaining-1)
                {
                    int bit=BitOperations.TrailingZeroCount((uint)remaining);var block=_potions[bit];
                    var matches=new HashSet<int>{matched};int offset=prefixPotion+_potionCalls[used];
                    for(int n=0;n<block.Calls;n++) matches=matches.SelectMany(m=>NonCard(block.Kind,offset+n,m)).ToHashSet();
                    if(potionWord!=null) for(int n=0;n<block.Calls;n++) potionWord.Add(block.Item);
                    if(matches.Any(m=>Potions(used|(1<<bit),m))) return true;
                    if(potionWord!=null) potionWord.RemoveRange(potionWord.Count-block.Calls,block.Calls);
                }
                if(preserveWords && !limited && successfulWords==before) potionVisited.Add((used,matched));
                return false;
            }
            if(potionMatched.Any(m=>Potions(0,m))) return new(CrystalOffsetVerdict.Possible,states);
        }
        return new(limited?CrystalOffsetVerdict.Incomplete:completions is {Count:>0} || requirements is {Count:>0}?CrystalOffsetVerdict.Possible:CrystalOffsetVerdict.Impossible,states);
    }
    private static bool WordPrefix(ImmutableArray<int> prefix,ImmutableArray<int> word)=>
        prefix.Length<=word.Length && prefix.AsSpan().SequenceEqual(word.AsSpan()[..prefix.Length]);
}
