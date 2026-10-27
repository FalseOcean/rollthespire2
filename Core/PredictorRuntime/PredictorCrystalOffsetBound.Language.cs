using System.Collections.Immutable;
using System.Numerics;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// A query-local finite language over completed physical items. This is an
// algorithm experiment, not a Runtime effect graph or a replacement authority.
internal sealed record CrystalLanguageNode(bool Terminal,UInt128 Required,UInt128 Future,
    ImmutableArray<(int Item,int Next)> Edges,ImmutableArray<UInt128> Completions);
internal sealed record CrystalRewardLanguage(ImmutableArray<CrystalLanguageNode> Nodes,
    ImmutableArray<(int Potion,int Core)> Roots,bool Complete,int States,int Pruned=0);

internal sealed partial class PredictorCrystalOffsetBound
{
    // The former string summary described a multiset, in ordinal kind/hit
    // order. Apply maintains that same canonical order at each card addition;
    // memo lookups can retain the immutable array and compare its exact values.
    // Hash collisions never merge slots, and length is not bounded by this key.
    private readonly struct LanguageSlots : IEquatable<LanguageSlots>
    {
        private readonly ImmutableArray<RerollSlot> _slots;
        private readonly int _hash;
        internal LanguageSlots(ImmutableArray<RerollSlot> slots)
        {
            _slots=slots;int hash=0;
            foreach(var slot in slots)
                hash=unchecked(hash*31+HashCode.Combine(StringComparer.Ordinal.GetHashCode(slot.Kind),slot.InitialHits));
            _hash=hash;
        }
        public bool Equals(LanguageSlots other)=>_slots==other._slots || _slots.AsSpan().SequenceEqual(other._slots.AsSpan());
        public override bool Equals(object? other)=>other is LanguageSlots slots && Equals(slots);
        public override int GetHashCode()=>_hash;
    }

    private static ImmutableArray<RerollSlot> AddLanguageSlot(ImmutableArray<RerollSlot> slots,RerollSlot slot)
    {
        int index=slots.Length;
        while(index>0)
        {
            var prior=slots[index-1];int kind=StringComparer.Ordinal.Compare(prior.Kind,slot.Kind);
            if(kind<0 || (kind==0 && prior.InitialHits<=slot.InitialHits)) break;
            index--;
        }
        return slots.Insert(index,slot);
    }

    // Compiler-only fixed-left queries on this frozen Bound. CompileLanguage
    // holds _sync for every access; geometry's varying-left _coverMemo is separate.
    // Each need is a union of the at-most-18 admitted physical block masks, so
    // the retained key domain is finite (at most 2^18), independent of goals.
    private readonly Dictionary<UInt128,bool> _compilerCoverResults=[];

    internal CrystalRewardLanguage CompileLanguage(ImmutableArray<CrystalRewardOption> requested,
        Func<bool> keepGoing,int stateLimit,Func<CrystalRewardLanguage,bool>? visitScenario=null,CompilationTimer? profile=null,
        int minimumGold=0)
    {
        if(minimumGold<0) throw new ArgumentOutOfRangeException(nameof(minimumGold));
        if(!_admitted || requested.Length>16) return new([],[],false,0);
        lock(_sync) return Build();

        CrystalRewardLanguage Build()
        {
            bool CompilerCanCover(UInt128 need)
            {
                using var measurement=profile?.MeasureCanCover();
                bool result;
                if(_compilerCoverResults.TryGetValue(need,out result))
                {if(profile!=null) profile.CanCoverCacheHits++;}
                else {result=CanCover(need,_source.Remaining);_compilerCoverResults.Add(need,result);}
                if(profile!=null) profile.CanCoverCacheEntries=_compilerCoverResults.Count;
                if(!result && profile!=null) profile.CanCoverRejected++;
                return result;
            }
            var goals=requested.Distinct().OrderBy(g=>g.Kind).ThenBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel)
                .ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
            bool exactEnchantments=goals.Any(g=>g.Kind==PredictorRewardKind.Card && g.Enchantment!=null);
            int all=(1<<goals.Length)-1,states=0,pruned=0;bool limited=false;
            int cards=Enumerable.Range(0,goals.Length).Where(g=>goals[g].Kind==PredictorRewardKind.Card).Aggregate(0,(m,g)=>m|(1<<g));
            int potions=Enumerable.Range(0,goals.Length).Where(g=>goals[g].Kind==PredictorRewardKind.Potion).Aggregate(0,(m,g)=>m|(1<<g));
            int relicGoals=Enumerable.Range(0,goals.Length).Where(g=>goals[g].Kind==PredictorRewardKind.Relic).Aggregate(0,(m,g)=>m|(1<<g));
            int cardCount=BitOperations.PopCount((uint)cards);
            bool Admit() {if(states>=stateLimit || !keepGoing()) {limited=true;return false;} states++;return true;}
            var nodes=new List<CrystalLanguageNode>{new(true,0,0,[],[0])};
            var roots=new List<(int Potion,int Core)>();
            var needs=_potions.Concat(_core).ToDictionary(b=>b.Item,b=>b.Need);
            int Node(bool terminal,List<(int Item,int Next)> edges)
            {
                using var measurement=profile?.MeasureNode();
                if(!terminal && edges.Count==0) return -1;
                UInt128 required=terminal?0:UInt128.MaxValue,future=0;
                foreach(var edge in edges)
                {required &= needs[edge.Item]|nodes[edge.Next].Required;future |= needs[edge.Item]|nodes[edge.Next].Future;}
                var completions=new List<UInt128>();bool overflow=false;
                if(terminal) completions.Add(0);
                else foreach(var edge in edges)
                {
                    foreach(var rest in nodes[edge.Next].Completions)
                    {
                        UInt128 cells=needs[edge.Item]|rest;
                        if(completions.Any(c=>(c&cells)==c)) continue;
                        completions.RemoveAll(c=>(c&cells)==cells);completions.Add(cells);
                        if(completions.Count>128) {overflow=true;break;}
                    }
                    if(overflow) break;
                }
                // Retaining only the first 128 alternatives could falsely
                // exclude the omitted ones. Overflow instead weakens to their
                // common necessary cells, including every possible suffix.
                int id=nodes.Count;nodes.Add(new(terminal,required,future,edges.Distinct().ToImmutableArray(),
                    overflow?[required]:completions.ToImmutableArray()));return id;
            }
            var hits=new Dictionary<(string Kind,int Offset),int>();
            int CardHits(string kind,int offset)
            {
                if(hits.TryGetValue((kind,offset),out int known)) return known;
                int hit=0;for(int g=0;g<goals.Length;g++)
                    if((cards&(1<<g))!=0 && _table.CardAt(kind,goals[g],offset,offset)) hit|=1<<g;
                hits.Add((kind,offset),hit);return hit;
            }
            var exactHits=new Dictionary<(string Kind,int Offset,int Niche,int Groups),(int Hits,int Niche,int Groups)>();
            (int Hits,int Niche,int Groups) CardStep(string kind,int offset,int niche,int groups)
            {
                // Ordinary predicates preserve the existing optimistic key/level
                // table and do not generate or visit any Niche layers.
                if(!exactEnchantments) return (CardHits(kind,offset),0,0);
                var key=(kind,offset,niche,groups);
                if(exactHits.TryGetValue(key,out var known)) return known;
                if(!_table.TryCardRow(kind,offset,niche,groups,out var row))
                {limited=true;return (0,niche,groups);}
                int hit=0;for(int g=0;g<goals.Length;g++)
                    if((cards&(1<<g))!=0 && row.Cards.Any(goals[g].MatchesCard)) hit|=1<<g;
                var next=(hit,niche+row.NicheConsumed,_table.CanonicalCardGroups(groups+1));
                exactHits.Add(key,next);return next;
            }
            IEnumerable<int> NonCard(string kind,int offset,int matched,ModelKey relic=default)
            {
                int hit=0;for(int g=0;g<goals.Length;g++)
                {
                    if((matched&(1<<g))!=0) continue;
                    bool yes=goals[g].Kind switch {
                        PredictorRewardKind.Potion=>kind.StartsWith("POTION_",StringComparison.Ordinal) && _table.PotionAt(kind,goals[g].Key,offset,offset),
                        PredictorRewardKind.Relic=>kind=="RELIC" && (!goals[g].Key.IsValid || goals[g].Key==relic),_=>false};
                    if(yes) hit|=1<<g;
                }
                if(hit==0 || kind=="RELIC") {yield return matched|hit;yield break;}
                for(int g=0;g<goals.Length;g++) if((hit&(1<<g))!=0) yield return matched|(1<<g);
            }
            LanguageSlots Summary(ImmutableArray<RerollSlot> slots)
            {
                using var measurement=profile?.MeasureSummary();
                return new(slots);
            }
            var terminalMemo=new Dictionary<(int Offset,int Niche,int Groups,LanguageSlots Slots,int Matched,int Bag),bool>();
            bool Finish(int offset,int niche,int groups,ImmutableArray<RerollSlot> input,int matched,int bag)
            {
                using var measurement=profile?.MeasureFinish();
                if(_table.IncludesSacrifices)
                {
                    var missing=goals.Where((g,n)=>g.Kind==PredictorRewardKind.Relic && (matched&(1<<n))==0).ToArray();
                    int gifts=missing.Where(g=>g.Key.IsValid).Select(g=>g.Key).Distinct().Count();
                    if(gifts==0 && missing.Length>0) gifts=1;
                    int sacrifices=Math.Max(0,2*gifts-PredictorCrystalWing.Parity(_source.State));
                    if(input.Length<cardCount+sacrifices) return false;
                    var upper=WingGiftAvailability(bag,
                        (input.Length-cardCount+PredictorCrystalWing.Parity(_source.State))/2);
                    if(missing.Any(g=>g.Key.IsValid && !upper.Contains(g.Key))) return false;
                }
                if(cards==0) return true;
                if(input.Length<BitOperations.PopCount((uint)cards)) return false;
                if(input.Length>16) {limited=true;return false;}
                var key=(offset,niche,groups,Summary(input),_table.IncludesSacrifices?matched:0,_table.IncludesSacrifices?bag:0);if(terminalMemo.TryGetValue(key,out bool known))
                {if(profile!=null) profile.FinishCacheHits++;return known;}
                var slots=input.ToArray();
                var visited=new HashSet<string>();
                bool Matches(int[] offers)
                {
                    var owners=Enumerable.Repeat(-1,offers.Length).ToArray();
                    bool Assign(int goal,bool[] seen)
                    {
                        for(int i=0;i<offers.Length;i++) if(!seen[i] && (offers[i]&(1<<goal))!=0)
                        {seen[i]=true;if(owners[i]<0 || Assign(owners[i],seen)) {owners[i]=goal;return true;}}
                        return false;
                    }
                    return Enumerable.Range(0,goals.Length).Where(g=>(cards&(1<<g))!=0).All(g=>Assign(g,new bool[offers.Length]));
                }
                bool Suffix(int used,int currentNiche,int currentGroups,int[] offers)
                {
                    if(profile!=null) profile.SuffixCalls++;
                    if(Matches(offers)) return true;
                    string suffixKey=exactEnchantments?$"{used}:{currentNiche}:{currentGroups}:"+string.Join(',',offers):used+":"+string.Join(',',offers);
                    if(limited || !visited.Add(suffixKey) || !Admit()) return false;
                    int nextOffset=offset+6*BitOperations.PopCount((uint)used);
                    for(int i=0;i<slots.Length;i++) if((used&(1<<i))==0)
                    {
                        var step=CardStep(slots[i].Kind,nextOffset,currentNiche,currentGroups);
                        var next=(int[])offers.Clone();next[i]=step.Hits;
                        if(Suffix(used|(1<<i),step.Niche,step.Groups,next)) return true;
                    }
                    return false;
                }
                var offers=slots.Select(s=>s.InitialHits).ToArray();bool result=_table.IncludesRerolls?Suffix(0,niche,groups,offers):Matches(offers);
                if(!limited) terminalMemo.Add(key,result);return result;
            }
            IEnumerable<(int Matched,int Bag,int Niche,int Groups,ImmutableArray<RerollSlot> Cards)> Apply(string kind,int calls,int offset,int matched,int bag,int niche,int groups,ImmutableArray<RerollSlot> slots)
            {
                if(kind.StartsWith("CARD_",StringComparison.Ordinal))
                {
                    for(int n=0;n<calls;n++)
                    {
                        var step=CardStep(kind,offset+6*n,niche,groups);slots=AddLanguageSlot(slots,new(kind,step.Hits));
                        niche=step.Niche;groups=step.Groups;
                    }
                    yield return (matched,bag,niche,groups,slots);yield break;
                }
                var matches=new HashSet<int>{matched};int nextBag=bag;ImmutableArray<ModelKey> keys=[];
                if(kind=="RELIC") (nextBag,keys)=Pull(bag,offset,calls);
                for(int n=0;n<calls;n++) matches=matches.SelectMany(m=>NonCard(kind,offset+n,m,kind=="RELIC"?keys[n]:default)).ToHashSet();
                foreach(int m in matches) yield return (m,nextBag,niche,groups,slots);
            }
            var prefixPotions=_source.Revealed.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)).ToArray();
            var prefixCore=_source.Revealed.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal) && _source.Items[i].Kind!="CURSE").ToArray();
            int goldMultiplier=1;
            foreach(var modifier in _source.Context.Modifiers.Where(m=>m.Key.Entry=="MIDAS")) goldMultiplier=checked(goldMultiplier*2);
            int Gold(string kind)=>checked(goldMultiplier*(kind switch {"GOLD_SMALL"=>10,"GOLD_BIG"=>30,_=>0}));
            int prefixGold=prefixCore.Sum(i=>Gold(_source.Items[i].Kind));
            var goldSums=new int[minimumGold>0?_coreDraws.Length:0];
            for(int mask=1;mask<goldSums.Length;mask++)
            {
                int bit=BitOperations.TrailingZeroCount((uint)mask);var block=_core[bit];
                goldSums[mask]=checked(goldSums[mask&(mask-1)]+Gold(block.Kind)*block.Calls);
            }
            var prefixMatches=new HashSet<int>{0};int prefixPotion=0;
            foreach(int i in prefixPotions) {prefixMatches=prefixMatches.SelectMany(m=>NonCard(_source.Items[i].Kind,prefixPotion,m)).ToHashSet();prefixPotion++;}
            int prefixDraws=prefixCore.Sum(i=>CrystalReachabilityTable.Draws(_source.Items[i].Kind));
            var remainingCards=new int[_coreDraws.Length];var remainingRelics=new int[_coreDraws.Length];
            for(int mask=1;mask<remainingCards.Length;mask++)
            {
                int bit=BitOperations.TrailingZeroCount((uint)mask);var block=_core[bit];
                remainingCards[mask]=remainingCards[mask&(mask-1)]+(block.Kind.StartsWith("CARD_",StringComparison.Ordinal)?block.Calls:0);
                remainingRelics[mask]=remainingRelics[mask&(mask-1)]+(block.Kind=="RELIC"?block.Calls:0);
            }
            int relicBlocks=Enumerable.Range(0,_core.Length).Where(i=>_core[i].Kind=="RELIC").Aggregate(0,(m,i)=>m|(1<<i));
            // Draws accepts only the three supported CARD_ kinds. Subsets has
            // validated hidden blocks, and prefixDraws above validates every
            // revealed callback, so this actual-source kind set fits an int.
            var cardKinds=_core.Select(b=>b.Kind).Concat(prefixCore.Select(i=>_source.Items[i].Kind))
                .Where(k=>k.StartsWith("CARD_",StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            var cardKindIds=cardKinds.Select((kind,index)=>(kind,index)).ToDictionary(k=>k.kind,k=>k.index,StringComparer.Ordinal);
            var cardTargets=cardKinds.Select(kind=>goals.Select(g=>g.Kind==PredictorRewardKind.Card?_table.CardOffsets(kind,g):BigInteger.Zero).ToArray()).ToArray();
            var coreCardKinds=_core.Select(b=>cardKindIds.TryGetValue(b.Kind,out int kind)?kind:-1).ToArray();
            // Bag, Niche and prior groups remain in Core's exact state; this
            // necessary offset test already unions over those possibilities.
            // Slot kind multiplicity matters here only through total slot count.
            var futureMemo=new Dictionary<(int Remaining,int Offset,int Slots,int Initial,int Kinds),bool>();
            var wingQuotas=new Dictionary<(int Bag,int Gifts,int Missing),int>();
            bool WingQuota(int remaining,int matched,int bag,int available,UInt128 completedNeed)
            {
                int missing=relicGoals&~matched;
                var missingGoals=goals.Where((g,n)=>(missing&(1<<n))!=0).ToArray();
                int missingCount=missingGoals.Where(g=>g.Key.IsValid).Select(g=>g.Key).Distinct().Count();
                if(missingCount==0 && missing!=0) missingCount=1;
                // Every remaining board callback may satisfy one identity.
                // Reserve sacrifices only for the remainder; both origins stay
                // admitted until the last board-relic decision fixes the bag.
                int gifts=Math.Max(0,missingCount-remainingRelics[remaining]);
                if(remainingRelics[remaining]==0 && missing!=0)
                {
                    int maximum=(available-cardCount+PredictorCrystalWing.Parity(_source.State))/2;
                    var key=(bag,maximum,missing);
                    if(!wingQuotas.TryGetValue(key,out gifts))
                    {
                        gifts=int.MaxValue;
                        for(int n=missingCount;n<=maximum;n++)
                        {
                            var upper=WingGiftAvailability(bag,n);
                            if(missingGoals.All(g=>!g.Key.IsValid || upper.Contains(g.Key))) {gifts=n;break;}
                        }
                        wingQuotas.Add(key,gifts);
                    }
                    if(gifts==int.MaxValue) return false;
                }
                int required=checked(cardCount+Math.Max(0,2*gifts-PredictorCrystalWing.Parity(_source.State)));
                if(available<required) return false;
                UInt128 mandatory=0;
                for(int bits=remaining;bits!=0;bits &= bits-1)
                {
                    var block=_core[BitOperations.TrailingZeroCount((uint)bits)];
                    if(block.Kind.StartsWith("CARD_",StringComparison.Ordinal) && available-block.Calls<required)
                        mandatory|=block.Need;
                }
                return mandatory==0 || CompilerCanCover(completedNeed|mandatory);
            }
            bool CanStillMatch(int remaining,int offset,int matched,int bag,ImmutableArray<RerollSlot> slots,UInt128 completedNeed)
            {
                using var measurement=profile?.MeasureCanStillMatch();
                int available=slots.Length+remainingCards[remaining];
                if(available<cardCount) return false;
                if(_table.IncludesSacrifices && !WingQuota(remaining,matched,bag,available,completedNeed)) return false;
                if(!_table.IncludesSacrifices && (relicGoals&~matched)!=0 && (remaining&relicBlocks)==0) return false;
                int initial=0;foreach(var slot in slots) initial|=slot.InitialHits;
                if((initial&cards)==cards) return true;
                int kinds=0;foreach(var slot in slots) kinds|=1<<cardKindIds[slot.Kind];
                var key=(remaining,offset,slots.Length,initial,kinds);
                if(futureMemo.TryGetValue(key,out bool known))
                {if(profile!=null) profile.CanStillMatchCacheHits++;return known;}
                BigInteger ends=_coreSums[remaining]<<offset,rerolls=0;
                if(_table.IncludesRerolls) for(int n=0;n<available;n++) rerolls |= ends<<(6*n);
                bool result=true;
                for(int g=0;g<goals.Length;g++)
                {
                    if((cards&(1<<g))==0 || (initial&(1<<g))!=0) continue;
                    bool possible=false;
                    for(int bits=kinds;bits!=0 && !possible;bits &= bits-1)
                        possible=!(cardTargets[BitOperations.TrailingZeroCount((uint)bits)][g]&rerolls).IsZero;
                    for(int bits=remaining;bits!=0 && !possible;bits &= bits-1)
                    {
                        int bit=BitOperations.TrailingZeroCount((uint)bits);var block=_core[bit];
                        int kind=coreCardKinds[bit];if(kind<0) continue;
                        var target=cardTargets[kind][g];
                        if(!(target&rerolls).IsZero) {possible=true;break;}
                        var starts=_coreSums[remaining&~(1<<bit)]<<offset;
                        for(int n=0;n<block.Calls && !possible;n++) possible=!(target&(starts<<(6*n))).IsZero;
                    }
                    // Offset/subset and reroll-slot choices are intentionally
                    // independent here. This only rejects an empty overestimate.
                    if(!possible) {result=false;break;}
                }
                futureMemo.Add(key,result);
                if(profile!=null) profile.CanStillMatchCacheEntries=futureMemo.Count;
                return result;
            }
            for(int potionMask=0;potionMask<_potionCalls.Length && !limited;potionMask++)
            {
                UInt128 need=_potionNeeds[potionMask];if(!CompilerCanCover(need)) continue;
                int finalPotions=prefixPotion+_potionCalls[potionMask];
                var potionMemo=new Dictionary<(int Used,int Matched),int>();
                int Potion(int used,int matched)
                {
                    if(used==potionMask) return (matched&potions)==potions?0:-1;
                    var key=(used,matched);if(potionMemo.TryGetValue(key,out int known)) return known;
                    if(limited || !Admit()) return -1;
                    var edges=new List<(int Item,int Next)>();
                    for(int remaining=potionMask&~used;remaining!=0;remaining &= remaining-1)
                    {
                        int bit=BitOperations.TrailingZeroCount((uint)remaining);var block=_potions[bit];
                        var matches=new HashSet<int>{matched};int offset=prefixPotion+_potionCalls[used];
                        for(int n=0;n<block.Calls;n++) matches=matches.SelectMany(m=>NonCard(block.Kind,offset+n,m)).ToHashSet();
                        foreach(int m in matches) {int next=Potion(used|(1<<bit),m);if(next>=0) edges.Add((block.Item,next));}
                    }
                    int id=Node(false,edges);potionMemo.Add(key,id);return id;
                }
                var potionRoots=prefixMatches.Select(m=>Potion(0,m)).Where(id=>id>=0).Distinct().ToArray();
                if(potionRoots.Length==0 || limited) continue;
                var coreMemo=new Dictionary<(int Used,int Matched,int Bag,int Niche,int Groups,LanguageSlots Cards),int>();
                int Core(int used,int matched,int bag,int niche,int groups,ImmutableArray<RerollSlot> slots)
                {
                    if(profile!=null) profile.CoreCalls++;
                    var key=(used,matched,bag,niche,groups,Summary(slots));if(coreMemo.TryGetValue(key,out int known))
                    {if(profile!=null) profile.CoreCacheHits++;return known;}
                    if(limited || !Admit()) return -1;
                    if(minimumGold>0)
                    {
                        int remainingGold=(_coreDraws.Length-1)&~used;
                        int maximumGold=checked(prefixGold+goldSums[used]+goldSums[remainingGold]);
                        if(maximumGold<minimumGold) {pruned++;coreMemo.Add(key,-1);return -1;}
                        // A pile whose omission would miss the threshold is a
                        // necessary geometric obligation for every accepting word.
                        // This is query-local and never becomes a target-only fact.
                        UInt128 mandatoryGold=0;
                        for(int bits=remainingGold;bits!=0;bits &= bits-1)
                        {
                            int bit=BitOperations.TrailingZeroCount((uint)bits);var block=_core[bit];
                            if(maximumGold-Gold(block.Kind)*block.Calls<minimumGold) mandatoryGold |= block.Need;
                        }
                        if(!CompilerCanCover(need|_coreNeeds[used]|mandatoryGold)) {pruned++;coreMemo.Add(key,-1);return -1;}
                    }
                    int offset=finalPotions+prefixDraws+_coreDraws[used];
                    if(!CanStillMatch((_coreDraws.Length-1)&~used,offset,matched,bag,slots,need|_coreNeeds[used]))
                    {pruned++;coreMemo.Add(key,-1);return -1;}
                    bool terminal=(minimumGold==0 || checked(prefixGold+goldSums[used])>=minimumGold) &&
                        (matched|cards|(_table.IncludesSacrifices?relicGoals:0))==all && Finish(offset,niche,groups,slots,matched,bag);
                    var edges=new List<(int Item,int Next)>();
                    for(int remaining=(_coreDraws.Length-1)&~used;remaining!=0 && !limited;remaining &= remaining-1)
                    {
                        int bit=BitOperations.TrailingZeroCount((uint)remaining),next=used|(1<<bit);var block=_core[bit];
                        if(!CompilerCanCover(need|_coreNeeds[next])) continue;
                        foreach(var r in Apply(block.Kind,block.Calls,offset,matched,bag,niche,groups,slots))
                        {int target=Core(next,r.Matched,r.Bag,r.Niche,r.Groups,r.Cards);if(target>=0) edges.Add((block.Item,target));}
                    }
                    int id=Node(terminal,edges);coreMemo.Add(key,id);return id;
                }
                var current=new List<(int Matched,int Bag,int Niche,int Groups,ImmutableArray<RerollSlot> Cards)>{(potions,0,0,0,[])};int prefixOffset=finalPotions;
                foreach(int i in prefixCore)
                {string kind=_source.Items[i].Kind;current=current.SelectMany(s=>Apply(kind,1,prefixOffset,s.Matched,s.Bag,s.Niche,s.Groups,s.Cards)).ToList();prefixOffset+=CrystalReachabilityTable.Draws(kind);}
                foreach(var start in current)
                {
                    int core=Core(0,start.Matched,start.Bag,start.Niche,start.Groups,start.Cards);
                    if(core>=0) foreach(int potion in potionRoots) roots.Add((potion,core));
                }
                // Query existence incrementally; compiling other potion sets
                // is unnecessary after a replayed positive. Early exit leaves
                // the language incomplete, so it cannot certify a negative.
                if(visitScenario!=null && visitScenario(new(nodes.ToImmutableArray(),roots.Distinct().ToImmutableArray(),false,states,pruned)))
                    return new(nodes.ToImmutableArray(),roots.Distinct().ToImmutableArray(),false,states,pruned);
            }
            return new(nodes.ToImmutableArray(),roots.Distinct().ToImmutableArray(),!limited,states,pruned);
        }
    }
}
