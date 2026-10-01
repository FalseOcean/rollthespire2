using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

// Snapshot-local, positive-only geometry suggestions. An island is an overlapping
// neighbourhood, not an independent board component: every click updates the full
// fog and every incidental completion is retained. No RNG or negative proof lives here.
internal sealed class PredictorCrystalIslands
{
    internal sealed record Transition(UInt128 Fog,ImmutableArray<int> Completed,
        ImmutableArray<PredictorCrystalStep> Steps,UInt128 Centers,int Gain);
    private sealed record Move(PredictorCrystalStep Step,int[] Cells,UInt128 Mask,UInt128 Center);
    private readonly PredictorCrystalSnapshot _source;
    private readonly bool _avoidCurse;
    private readonly UInt128[] _items;
    private readonly Move[] _moves;
    private readonly UInt128[] _islands;
    private readonly Dictionary<UInt128,(Move[] Moves,UInt128 Influence)> _neighbourhoods=[];
    private readonly ConcurrentDictionary<(UInt128 Fog,UInt128 Island,int Depth),Lazy<ImmutableArray<Transition>>> _cache=[];
    private long _hits,_builds;
    internal long Hits=>Interlocked.Read(ref _hits);
    internal long Builds=>Interlocked.Read(ref _builds);
    internal int Count=>_islands.Length;
    internal double AverageInfluenceCells=>_neighbourhoods.Count==0?0:_neighbourhoods.Values.Average(n=>Bits(n.Influence));
    internal PredictorCrystalIslands(PredictorCrystalSnapshot source,bool avoidCurse)
    {
        _source=source;_avoidCurse=avoidCurse;
        _items=source.Items.Select(item=>item.X<0?(UInt128)0:
            (from x in Enumerable.Range(item.X,item.Width) from y in Enumerable.Range(item.Y,item.Height)
             select (UInt128)1<<(x*11+y)).Aggregate((UInt128)0,(a,b)=>a|b)).ToArray();
        _moves=(from cell in Enumerable.Range(0,121) where source.Hidden[cell]
            from tool in new[]{PredictorCrystalTool.Small,PredictorCrystalTool.Big}
            let cells=PredictorCrystalSearch.Cells(cell/11,cell%11,tool).Select(c=>c.X*11+c.Y).ToArray()
            select new Move(new(cell/11,cell%11,tool),cells,cells.Aggregate((UInt128)0,(a,b)=>a|((UInt128)1<<b)),(UInt128)1<<cell)).ToArray();
        // A local clique of co-touched rewards, not the transitive union of all
        // neighbours of a large item (which can swallow most of a dense board).
        var groups=_moves.Where(m=>m.Step.Tool==PredictorCrystalTool.Big).Select(m=>
            _items.Where((mask,i)=>(mask&m.Mask)!=0 && source.Items[i].Kind!="CURSE")
                .Aggregate((UInt128)0,(a,b)=>a|b)).Where(mask=>mask!=0).Distinct().ToArray();
        _islands=groups.Where(mask=>!groups.Any(other=>other!=mask && (other&mask)==mask)).ToArray();
        foreach(var island in _islands)
        {
            var moves=_moves.Where(m=>(m.Mask&island)!=0).ToArray();
            UInt128 footprint=moves.Aggregate((UInt128)0,(a,m)=>a|m.Mask);
            // Include whole boundary items: cells outside the click footprint
            // may decide whether an incidental reward completes. Remote cells
            // outside this influence cannot change these local transitions.
            UInt128 influence=_items.Where(mask=>(mask&footprint)!=0).Aggregate(footprint,(a,b)=>a|b);
            _neighbourhoods.Add(island,(moves,influence));
        }
    }
    private static int Bits(UInt128 mask)=>BitOperations.PopCount((ulong)mask)+BitOperations.PopCount((ulong)(mask>>64));
    internal IEnumerable<ImmutableArray<Transition>> Suggestions(UInt128 fog,UInt128 wanted,int remaining)
    {
        int depth=Math.Min(2,remaining);if(depth==0) yield break;
        foreach(var island in _islands.Where(i=>(i&fog&wanted)!=0).OrderByDescending(i=>Bits(i&fog&wanted)))
        {
            UInt128 localFog=fog&_neighbourhoods[island].Influence;
            var key=(localFog,island,depth);
            if(!_cache.TryGetValue(key,out var found))
            {
                // This is a suggestion quota, never an impossibility decision.
                if(_cache.Count>=512) continue;
                found=_cache.GetOrAdd(key,new Lazy<ImmutableArray<Transition>>(()=>Build(localFog,island,depth)));
            }
            else Interlocked.Increment(ref _hits);
            yield return found.Value.Select(t=>t with { Fog=fog & ~(localFog^t.Fog) }).ToImmutableArray();
        }
    }
    private ImmutableArray<Transition> Build(UInt128 fog,UInt128 island,int depth)
    {
        Interlocked.Increment(ref _builds);
        Transition? Click(Transition prior,Move move)
        {
            if((prior.Fog&move.Center)==0 || (prior.Fog&move.Mask&island)==0) return null;
            UInt128 next=prior.Fog;var completed=prior.Completed;
            foreach(int cell in move.Cells)
            {
                UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                next &= ~bit;int item=_source.Occupancy[cell];
                if(item<0 || (next&_items[item])!=0) continue;
                if(_avoidCurse && _source.Items[item].Kind=="CURSE") return null;
                completed=completed.AddRange(Enumerable.Repeat(item,_source.Items[item].Subscriptions));
            }
            return new(next,completed,prior.Steps.Add(move.Step),prior.Centers|move.Center,Bits((fog^next)&island));
        }
        Move[] LocalMoves(UInt128 current)=>_neighbourhoods[island].Moves.Where(m=>(current&m.Center)!=0 && (current&m.Mask&island)!=0)
            .GroupBy(m=>m.Step.Tool).SelectMany(g=>g.OrderByDescending(m=>Bits(current&m.Mask&island)).Take(g.Key==PredictorCrystalTool.Big?32:16)).ToArray();
        var initial=new Transition(fog,[],[],0,0);
        var first=LocalMoves(fog).Select(m=>Click(initial,m)).OfType<Transition>().ToArray();
        var all=new List<Transition>(first);
        if(depth>1) foreach(var prefix in first)
            foreach(var move in LocalMoves(prefix.Fog)) if(Click(prefix,move) is { } next) all.Add(next);
        // Preserve distinct reward orders and several boundary geometries per
        // order. Partial preparation is included, allowing later island revisits.
        var groups=all.GroupBy(t=>string.Join(',',t.Completed))
            .Select(g=>g.GroupBy(t=>t.Steps.Length).SelectMany(length=>length.OrderByDescending(t=>t.Gain)
                .DistinctBy(t=>(t.Fog,t.Centers)).Take(6)).ToArray()).ToArray();
        // Round-robin reward orders rather than letting large multi-reward
        // completions crowd out a needed single reward or preparation-only move.
        return Enumerable.Range(0,12).SelectMany(n=>groups.Where(g=>n<g.Length).Select(g=>g[n]))
            .Take(256).ToImmutableArray();
    }
}
