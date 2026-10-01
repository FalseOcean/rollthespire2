using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum CrystalOffsetVerdict { Possible, Impossible, Incomplete }
internal readonly record struct CrystalOffsetReceipt(CrystalOffsetVerdict Verdict,int States)
{
    internal bool MaySupport => Verdict!=CrystalOffsetVerdict.Impossible;
}

// Frozen-board necessary conditions, independently expressed as subset DP.
// A subset fixes the draw count, so permutations with the same matched goals
// and personal bag share a state. No click path or take witness is produced.
// Potions run first; physical subscription blocks remain indivisible in each
// phase. Each CardReward can satisfy only one card goal. Different physical
// sources may satisfy the same identity, without binding the player's choice.
internal sealed class PredictorCrystalOffsetBound
{
    private readonly record struct Block(string Kind,int Calls,UInt128 Need);
    private readonly PredictorCrystalSnapshot _source;
    private readonly CrystalReachabilityTable _table;
    private readonly Block[] _potions,_core;
    private readonly int[] _potionCalls,_coreDraws;
    private readonly UInt128[] _potionNeeds,_coreNeeds;
    private readonly BigInteger[] _coreSums;
    private readonly UInt128[] _covers;
    private readonly UInt128[] _residues=new UInt128[9];
    private readonly ConcurrentDictionary<(UInt128 Need,int Left),bool> _coverMemo=[];
    private readonly ConcurrentDictionary<string,Lazy<CrystalOffsetReceipt>> _proofs=[];
    private readonly List<PredictorRelicBag> _bags=[];
    private readonly Dictionary<string,int> _bagIds=[];
    private readonly List<ImmutableArray<CrystalRewardOption>> _negativeSets=[];
    private readonly Dictionary<(int Bag,int Offset,int Calls),(int Bag,ImmutableArray<ModelKey> Keys)> _relics=[];
    private readonly object _sync=new();
    private readonly bool _admitted;
    private long _checks,_rejected,_incomplete,_cacheHits,_ticks;
    internal long Checks=>Interlocked.Read(ref _checks);
    internal long Rejected=>Interlocked.Read(ref _rejected);
    internal long Incomplete=>Interlocked.Read(ref _incomplete);
    internal long CacheHits=>Interlocked.Read(ref _cacheHits);
    internal double Seconds=>Interlocked.Read(ref _ticks)/(double)Stopwatch.Frequency;

    internal PredictorCrystalOffsetBound(PredictorCrystalSnapshot source,CrystalReachabilityTable table,bool avoidCurse)
    {
        _source=source;_table=table;
        var placed=source.Items.Select((item,index)=>(item,index)).Where(x=>x.item.X>=0 &&
            x.item.Kind!="CURSE" && x.item.Subscriptions>0 && !source.Revealed.Contains(x.index)).ToArray();
        // A large failed-placement workload retains the existing legal solver.
        // Incomplete means unknown, never a negative certificate. A newly
        // revealed curse can modify state; this arithmetic relaxation does not
        // try to reconstruct its obtain hooks.
        _admitted=placed.Length<=18 && (avoidCurse || !source.Items.Any(i=>i.Kind=="CURSE" && i.X>=0 && !i.Revealed));
        UInt128 Need(PredictorCrystalItem item)
        {
            UInt128 mask=0;
            for(int x=item.X;x<item.X+item.Width;x++) for(int y=item.Y;y<item.Y+item.Height;y++)
                if(source.Hidden[x*11+y]) mask|=(UInt128)1<<(x*11+y);
            return mask;
        }
        var blocks=placed.Select(x=>new Block(x.item.Kind,x.item.Subscriptions,Need(x.item))).ToArray();
        _potions=blocks.Where(b=>b.Kind.StartsWith("POTION_",StringComparison.Ordinal)).ToArray();
        _core=blocks.Where(b=>!b.Kind.StartsWith("POTION_",StringComparison.Ordinal)).ToArray();
        (_potionCalls,_potionNeeds)=Subsets(_admitted?_potions:[]);
        (_coreDraws,_coreNeeds)=Subsets(_admitted?_core:[]);
        _coreSums=new BigInteger[_coreDraws.Length];_coreSums[0]=1;
        for(int mask=1;mask<_coreSums.Length;mask++)
        {
            int bit=BitOperations.TrailingZeroCount((uint)mask),prior=mask & (mask-1);
            _coreSums[mask]=_coreSums[prior]|(_coreSums[prior]<<(_core[bit].Calls*CrystalReachabilityTable.Draws(_core[bit].Kind)));
        }
        _covers=Enumerable.Range(0,121).Where(i=>source.Hidden[i]).Select(i=>
            PredictorCrystalSearch.Cells(i/11,i%11,PredictorCrystalTool.Big)
                .Aggregate((UInt128)0,(bits,c)=>bits|((UInt128)1<<(c.X*11+c.Y)))).Distinct().ToArray();
        for(int i=0;i<121;i++) _residues[(i/11%3)*3+i%11%3]|=(UInt128)1<<i;
        Intern(source.State.PersonalBag);
    }

    private static (int[] Draws,UInt128[] Needs) Subsets(Block[] blocks)
    {
        int count=1<<blocks.Length;var draws=new int[count];var needs=new UInt128[count];
        for(int mask=1;mask<count;mask++)
        {
            int bit=BitOperations.TrailingZeroCount((uint)mask),prior=mask & (mask-1);
            draws[mask]=checked(draws[prior]+blocks[bit].Calls*CrystalReachabilityTable.Draws(blocks[bit].Kind));
            needs[mask]=needs[prior]|blocks[bit].Need;
        }
        return (draws,needs);
    }

    internal CrystalOffsetReceipt Check(ImmutableArray<CrystalRewardOption> goals,int stateLimit=100_000)
    {
        Interlocked.Increment(ref _checks);
        // Take order is relaxed here. The actual verifier keeps ordered takes;
        // relaxation only enlarges this necessary-condition solution set.
        var ordered=goals.Distinct().OrderBy(g=>g.Kind).ThenBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ToImmutableArray();
        if(ordered.IsEmpty) return new(CrystalOffsetVerdict.Possible,0);
        if(!_admitted || ordered.Length>16) return new(CrystalOffsetVerdict.Incomplete,0);
        // Different audit work limits must not poison the normal proof cache.
        string key=stateLimit+":"+string.Join('|',ordered.Select(g=>$"{g.Kind}:{g.Key.Serialized}:{g.UpgradeLevel?.ToString()??"*"}"));
        if(_proofs.TryGetValue(key,out var known)) { Interlocked.Increment(ref _cacheHits);return known.Value; }
        var pending=new Lazy<CrystalOffsetReceipt>(()=>
        {
            long start=Stopwatch.GetTimestamp();CrystalOffsetReceipt receipt;
            // Bag interning and transitions are pure, local to this snapshot.
            lock(_sync)
            {
                if(_negativeSets.Any(prior=>prior.All(ordered.Contains)))
                { Interlocked.Increment(ref _cacheHits);receipt=new(CrystalOffsetVerdict.Impossible,0); }
                else
                {
                    receipt=Solve(ordered,stateLimit);
                    if(receipt.Verdict==CrystalOffsetVerdict.Impossible && _negativeSets.Count<2048)
                        _negativeSets.Add(ordered);
                }
            }
            Interlocked.Add(ref _ticks,Stopwatch.GetTimestamp()-start);
            if(receipt.Verdict==CrystalOffsetVerdict.Impossible) Interlocked.Increment(ref _rejected);
            if(receipt.Verdict==CrystalOffsetVerdict.Incomplete) Interlocked.Increment(ref _incomplete);
            return receipt;
        },LazyThreadSafetyMode.ExecutionAndPublication);
        var proof=_proofs.Count<2048?_proofs.GetOrAdd(key,pending):pending;
        if(!ReferenceEquals(proof,pending)) Interlocked.Increment(ref _cacheHits);
        return proof.Value;
    }

    private CrystalOffsetReceipt Solve(ImmutableArray<CrystalRewardOption> goals,int stateLimit)
    {
        int all=(1<<goals.Length)-1,states=0;
        int potionGoals=Enumerable.Range(0,goals.Length).Where(g=>goals[g].Kind==PredictorRewardKind.Potion).Aggregate(0,(bits,g)=>bits|(1<<g));
        bool limited=false;
        bool Admit() { if(states>=stateLimit) { limited=true;return false; } states++;return true; }
        // A non-card callback may satisfy AnyRelic together with its concrete
        // identity. Card choices remain exclusive even for A versus A+.
        IEnumerable<int> Match(string kind,int offset,int matched,ModelKey relic=default)
        {
            int hit=0;
            for(int g=0;g<goals.Length;g++)
            {
                if((matched & (1<<g))!=0) continue;
                var goal=goals[g];bool yes=goal.Kind switch {
                    PredictorRewardKind.Card=>kind.StartsWith("CARD_",StringComparison.Ordinal) && _table.CardAt(kind,goal,offset,offset),
                    PredictorRewardKind.Potion=>kind.StartsWith("POTION_",StringComparison.Ordinal) && _table.PotionAt(kind,goal.Key,offset,offset),
                    PredictorRewardKind.Relic=>kind=="RELIC" && (!goal.Key.IsValid || goal.Key==relic),_=>false };
                if(yes) hit|=1<<g;
            }
            if(hit==0) { yield return matched;yield break; }
            if(kind=="RELIC") { yield return matched|hit;yield break; }
            // Skipping a matching goal is dominated by choosing it: no reward
            // is taken here, and neither RNG nor bag depends on this bitset.
            for(int g=0;g<goals.Length;g++) if((hit & (1<<g))!=0) yield return matched|(1<<g);
        }
        int prefixPotion=0;
        var potionMatched=new HashSet<int> { 0 };
        foreach(int i in _source.Revealed.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)))
        {
            string kind=_source.Items[i].Kind;
            potionMatched=potionMatched.SelectMany(m=>Match(kind,prefixPotion,m)).ToHashSet();prefixPotion++;
        }
        var prefixCore=_source.Revealed.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal) && _source.Items[i].Kind!="CURSE").ToArray();
        int prefixDraws=prefixCore.Sum(i=>CrystalReachabilityTable.Draws(_source.Items[i].Kind));
        int coreAll=_coreDraws.Length-1;
        for(int potionMask=0;potionMask<_potionCalls.Length && !limited;potionMask++)
        {
            UInt128 potionNeed=_potionNeeds[potionMask];
            if(!CanCover(potionNeed,_source.Remaining)) continue;
            int finalPotions=prefixPotion+_potionCalls[potionMask];
            var potionVisited=new HashSet<(int Used,int Matched)>();
            var coreVisited=new HashSet<(int Used,int Matched,int Bag)>();
            bool FutureCore(int used,int matched,int bag)
            {
                int remaining=coreAll & ~used,offset=finalPotions+prefixDraws+_coreDraws[used];
                for(int g=0;g<goals.Length;g++)
                {
                    if((matched & (1<<g))!=0) continue;
                    var goal=goals[g];bool possible=false;
                    if(goal.Kind==PredictorRewardKind.Potion) return false;
                    for(int bits=remaining;bits!=0 && !possible;bits &= bits-1)
                    {
                        int bit=BitOperations.TrailingZeroCount((uint)bits);var block=_core[bit];
                        var starts=_coreSums[remaining & ~(1<<bit)]<<offset;
                        if(goal.Kind==PredictorRewardKind.Card && block.Kind.StartsWith("CARD_",StringComparison.Ordinal))
                            for(int n=0;n<block.Calls && !possible;n++)
                                possible=!((starts<<(6*n))&_table.CardOffsets(block.Kind,goal)).IsZero;
                        else if(goal.Kind==PredictorRewardKind.Relic && block.Kind=="RELIC")
                        {
                            // When another uncompleted relic block may run
                            // first, its bag changes are deliberately relaxed.
                            // Exact transitions below still carry that bag.
                            if(!goal.Key.IsValid || Enumerable.Range(0,_core.Length).Any(i=>i!=bit && (remaining&(1<<i))!=0 && _core[i].Kind=="RELIC")) possible=true;
                            else for(int draw=0;starts>0 && !possible;draw++,starts>>=1)
                                if(!starts.IsEven) possible=Pull(bag,draw,block.Calls).Keys.Contains(goal.Key);
                        }
                    }
                    if(!possible) return false;
                }
                return true;
            }
            bool Core(int used,int matched,int bag)
            {
                if(matched==all) return true;
                if(limited || !coreVisited.Add((used,matched,bag))) return false;
                if(!Admit()) return false;
                if(!FutureCore(used,matched,bag)) return false;
                for(int remaining=coreAll & ~used;remaining!=0;remaining &= remaining-1)
                {
                    int bit=BitOperations.TrailingZeroCount((uint)remaining),next=used|(1<<bit);
                    if(!CanCover(potionNeed|_coreNeeds[next],_source.Remaining)) continue;
                    int offset=finalPotions+prefixDraws+_coreDraws[used];var block=_core[bit];
                    foreach(var result in Apply(block.Kind,block.Calls,offset,matched,bag))
                        if(Core(next,result.Matched,result.Bag)) return true;
                }
                return false;
            }
            IEnumerable<(int Matched,int Bag)> Apply(string kind,int calls,int offset,int matched,int bag)
            {
                var matches=new HashSet<int> { matched };int nextBag=bag;
                ImmutableArray<ModelKey> keys=[];
                if(kind=="RELIC") (nextBag,keys)=Pull(bag,offset,calls);
                for(int n=0;n<calls;n++)
                    matches=matches.SelectMany(m=>Match(kind,offset+n*CrystalReachabilityTable.Draws(kind),m,kind=="RELIC"?keys[n]:default)).ToHashSet();
                foreach(int m in matches) yield return (m,nextBag);
            }
            bool BeginCore(int matched)
            {
                if((matched & potionGoals)!=potionGoals) return false;
                var current=new HashSet<(int Matched,int Bag)> { (matched,0) };int offset=finalPotions;
                foreach(int i in prefixCore)
                {
                    string kind=_source.Items[i].Kind;
                    current=current.SelectMany(s=>Apply(kind,1,offset,s.Matched,s.Bag)).ToHashSet();offset+=CrystalReachabilityTable.Draws(kind);
                }
                return current.Any(s=>Core(0,s.Matched,s.Bag));
            }
            bool Potions(int used,int matched)
            {
                if(used==potionMask) return BeginCore(matched);
                if(limited || !potionVisited.Add((used,matched))) return false;
                if(!Admit()) return false;
                for(int remaining=potionMask & ~used;remaining!=0;remaining &= remaining-1)
                {
                    int bit=BitOperations.TrailingZeroCount((uint)remaining);var block=_potions[bit];
                    var matches=new HashSet<int> { matched };int offset=prefixPotion+_potionCalls[used];
                    for(int n=0;n<block.Calls;n++) matches=matches.SelectMany(m=>Match(block.Kind,offset+n,m)).ToHashSet();
                    if(matches.Any(m=>Potions(used|(1<<bit),m))) return true;
                }
                return false;
            }
            if(potionMatched.Any(m=>Potions(0,m))) return new(CrystalOffsetVerdict.Possible,states);
        }
        return new(limited?CrystalOffsetVerdict.Incomplete:CrystalOffsetVerdict.Impossible,states);
    }

    private int Intern(PredictorRelicBag bag)
    {
        string key=string.Join('|',bag.Buckets.Select(b=>b.Rarity+":"+string.Join(',',b.Entries.Select(k=>k.Serialized))))+
            "#"+string.Join(',',bag.MultiplayerFallback.Select(k=>k.Serialized));
        if(_bagIds.TryGetValue(key,out int id)) return id;
        id=_bags.Count;_bags.Add(bag);_bagIds.Add(key,id);return id;
    }
    private (int Bag,ImmutableArray<ModelKey> Keys) Pull(int bag,int offset,int calls)
    {
        var key=(bag,offset,calls);if(_relics.TryGetValue(key,out var known)) return known;
        var state=_source.State with { PersonalBag=_bags[bag] };var rng=_source.EventRng.Restore();rng.Advance(offset);
        long before=rng.CallCount;var keys=ImmutableArray.CreateBuilder<ModelKey>();
        for(int n=0;n<calls;n++) keys.Add(PredictorRewardGeneration.PullRelic(_source.Context,ref state,rng));
        if(rng.CallCount-before!=calls) throw new InvalidOperationException("CrystalRelicOffsetContractChanged");
        var result=(Intern(state.PersonalBag),keys.ToImmutable());_relics.Add(key,result);return result;
    }

    // Relaxed set cover: initial hidden Big centers may be reused, without
    // future center eligibility, incidental completions or order restrictions.
    // Every actual Small/Big path is admitted. A cover cutoff enlarges the
    // relaxation; it cannot turn an unknown cover into an impossibility.
    private bool CanCover(UInt128 need,int left)
    {
        if(need==0) return true;
        if(left<=0) return false;
        if(_coverMemo.TryGetValue((need,left),out bool found)) return found;
        // In a fixed (x mod 3,y mod 3) class, every distinct pair is at
        // least three cells apart on some axis. One 3x3 click covers at most
        // one such cell. This is a certificate, independent of click order.
        if(_residues.Any(r=>PopCount(need&r)>left)) { _coverMemo.TryAdd((need,left),false);return false; }
        var maximal=new List<UInt128>();
        foreach(var gain in _covers.Select(c=>c&need).Where(c=>c!=0).Distinct().OrderByDescending(PopCount))
            if(!maximal.Any(larger=>(gain&larger)==gain)) maximal.Add(gain);
        var gains=maximal.ToArray();
        if(gains.Length==0 || PopCount(need)>left*PopCount(gains[0])) return false;
        if(PopCount(need)<=left || gains[0]==need) return true;
        if(_coverMemo.Count>=100_000) return true;
        // Branch on the least-covered cell; subset moves are dominated in
        // this relaxation even though they can matter to actual reveal order.
        UInt128 cell=0;int best=int.MaxValue;
        for(UInt128 remaining=need;remaining!=0;remaining &= remaining-1)
        {
            UInt128 bit=remaining & (~remaining+1);int choices=gains.Count(g=>(g&bit)!=0);
            if(choices<best) { cell=bit;best=choices;if(best==1) break; }
        }
        bool possible=gains.Where(g=>(g&cell)!=0).Any(g=>CanCover(need & ~g,left-1));
        _coverMemo.TryAdd((need,left),possible);return possible;
    }
    private static int PopCount(UInt128 mask)=>BitOperations.PopCount((ulong)mask)+BitOperations.PopCount((ulong)(mask>>64));
}
