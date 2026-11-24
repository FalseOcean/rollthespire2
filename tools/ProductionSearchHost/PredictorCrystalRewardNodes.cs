using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalRewardNodeUpdate(CrystalOptionProjection Projection,bool WorklistDrained,
    long RewardNodes,long LocalNodes,long LocalCacheHits,long RuntimeRows,double Seconds,bool Limited);

// Independent experimental search: global nodes are physical reward completions.
// Inside a goal island, residual masks recursively divide the remaining work;
// cached local transitions are independent of target cards/RNG offsets. This does
// NOT call Explorer.Moves, TargetDirected, Exhaustive or its macro-island beam.
// Advance uses restricted goal-directed preparation: drained work is NOT an
// exhaustive-unreachability claim. The separate Coverage partial composes all
// legal fragment/blank actions and owns its own scoped completion receipt.
// One session has one caller/worker. Only the frozen source is shareable; query
// iterators, fragment tables and retained witnesses are mutable session state.
internal sealed partial class PredictorCrystalRewardNodes
{
    private sealed record Node(UInt128 Fog,ImmutableArray<int> Order,ImmutableArray<PredictorCrystalStep> Path,UInt128 Centers);
    private sealed record Move(PredictorCrystalStep Step,int[] Cells,UInt128 Mask,UInt128 Center);
    private sealed record LocalStep(Move Move,UInt128 Removed,ImmutableArray<int> Completed,ImmutableArray<int> Physical);
    private sealed class Query(ImmutableArray<CrystalRewardOption> selected)
    {
        internal readonly ImmutableArray<CrystalRewardOption> Selected=selected;
        internal readonly Dictionary<CrystalRewardOption,PredictorCrystalSolution> Available=[];
        internal PredictorCrystalSolution? Plan;
        internal int Gold,Seen;
        internal double Seconds;
        internal long RewardNodes,LocalNodes,RuntimeRows,Serial;
        internal bool Drained,Limited;
        internal readonly PriorityQueue<Node,(int,long)> Pending=new();
        internal readonly HashSet<(UInt128,string,int,UInt128)> Visited=[];
        internal readonly Dictionary<(UInt128,string,int),List<UInt128>> Frontiers=[];
        internal readonly HashSet<string> Outcomes=[];
        internal IEnumerator<bool>? Cursor;
    }
    private readonly PredictorCrystalSnapshot _source;
    private readonly bool _avoidCurse;
    private readonly UInt128[] _items;
    private readonly Move[] _moves;
    private readonly Dictionary<UInt128,(Move[] Moves,UInt128 Influence)> _pieces=[];
    private readonly UInt128 _initialFog,_occupied;
    private readonly bool _paddingPlentiful;
    private readonly Dictionary<(UInt128 Part,UInt128 LocalFog),ImmutableArray<LocalStep>> _local=[];
    private readonly Dictionary<string,Query> _queries=[];
    private readonly Dictionary<(UInt128,int),bool> _coverBounds=[];
    private readonly List<CrystalRewardRoute> _witnesses=[];
    private readonly HashSet<string> _witnessPaths=[];
    private CrystalReachabilityTable? _table;
    private long _cacheHits;

    internal PredictorCrystalRewardNodes(PredictorCrystalSnapshot source,bool avoidCurse)
    {
        _source=source;_avoidCurse=avoidCurse;
        _items=source.Items.Select(item=>item.X<0?(UInt128)0:
            (from x in Enumerable.Range(item.X,item.Width) from y in Enumerable.Range(item.Y,item.Height)
             select (UInt128)1<<(x*11+y)).Aggregate((UInt128)0,(a,b)=>a|b)).ToArray();
        _occupied=_items.Aggregate((UInt128)0,(a,b)=>a|b);
        for(int cell=0;cell<121;cell++) if(source.Hidden[cell]) _initialFog|=(UInt128)1<<cell;
        _paddingPlentiful=Bits(_initialFog&~_occupied)>=source.Remaining;
        _moves=(from cell in Enumerable.Range(0,121) where source.Hidden[cell]
            from tool in new[]{PredictorCrystalTool.Big,PredictorCrystalTool.Small}
            let cells=PredictorCrystalSearch.Cells(cell/11,cell%11,tool).Select(c=>c.X*11+c.Y).ToArray()
            select new Move(new(cell/11,cell%11,tool),cells,cells.Aggregate((UInt128)0,(a,b)=>a|((UInt128)1<<b)),(UInt128)1<<cell)).ToArray();

    }
    private static int Bits(UInt128 n)=>BitOperations.PopCount((ulong)n)+BitOperations.PopCount((ulong)(n>>64));
    private string Word(ImmutableArray<int> order)=>string.Join(',',order.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)).Select(i=>_source.Items[i].Kind))+"|"+
        string.Join(',',order.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)).Select(i=>_source.Items[i].Kind))+"|"+
        string.Join(',',order.Where(i=>_source.Items[i].Kind=="CURSE").Distinct().Order());
    private static string Selection(ImmutableArray<CrystalRewardOption> selected)=>string.Join('|',selected
        .Select(o=>$"{o.Kind}:{o.Key.Serialized}:{o.UpgradeLevel?.ToString()??"*"}"));

    internal CrystalRewardNodeUpdate Advance(ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var watch=Stopwatch.StartNew();
        _table??=new(_source,token);
        string key=Selection(selected);
        if(!_queries.TryGetValue(key,out var query))
        {
            if(_queries.Count>=4) { var oldest=_queries.First();oldest.Value.Cursor?.Dispose();_queries.Remove(oldest.Key); }
            query=new(selected);_queries.Add(key,query);
            var initial=new Node(_initialFog,_source.Revealed,[],0);
            Enqueue(query,initial);query.Cursor=Search(query).GetEnumerator();
        }
        while(query.Seen<_witnesses.Count && watch.Elapsed<budget)
        {
            if(token.IsCancellationRequested) break;
            Consider(query,_witnesses[query.Seen++]);
        }
        while(!query.Drained && !token.IsCancellationRequested && watch.Elapsed<budget)
            if(!query.Cursor!.MoveNext()) query.Drained=true;
        query.Seconds+=watch.Elapsed.TotalSeconds;
        return new(new(query.Available.ToImmutableDictionary(),query.Plan,query.Gold),query.Drained,
            query.RewardNodes,query.LocalNodes,_cacheHits,query.RuntimeRows,query.Seconds,query.Limited);
    }

    // Residual parts are recalculated after each local reveal; separate pieces
    // have separate work options. Cross-part Big clicks are retained once, and
    // boundary items stay in the state, so no independent-cost sum is assumed.
    private static IEnumerable<UInt128> Parts(UInt128 need)
    {
        while(need!=0)
        {
            UInt128 part=need & (~need+1),front=part;need &= ~part;
            while(front!=0)
            {
                UInt128 next=0;
                for(int cell=0;cell<121;cell++) if((front&((UInt128)1<<cell))!=0)
                {
                    int x=cell/11,y=cell%11;
                    foreach(var (a,b) in new[]{(x-1,y),(x+1,y),(x,y-1),(x,y+1)})
                        if(a is >=0 and <11 && b is >=0 and <11) next|=((UInt128)1<<(a*11+b))&need;
                }
                need &= ~next;part|=next;front=next;
            }
            yield return part;
        }
    }
    private ImmutableArray<LocalStep> Local(int goal,UInt128 fog)
    {
        var combined=ImmutableArray.CreateBuilder<LocalStep>();var seen=new HashSet<Move>();
        foreach(var part in Parts(fog&_items[goal]).OrderBy(Bits))
            foreach(var step in Fragment(part,fog)) if(seen.Add(step.Move)) combined.Add(step);
        return combined.ToImmutable();
    }
    private ImmutableArray<LocalStep> Fragment(UInt128 part,UInt128 fog)
    {
        if(!_pieces.TryGetValue(part,out var piece))
        {
            var moves=_moves.Where(m=>(m.Mask&part)!=0).OrderByDescending(m=>Bits(m.Mask&part)).ToArray();
            UInt128 footprint=moves.Aggregate((UInt128)0,(a,m)=>a|m.Mask);
            UInt128 influence=_items.Where(item=>(item&footprint)!=0).Aggregate(footprint,(a,b)=>a|b);
            piece=(moves,influence);if(_pieces.Count<2048) _pieces.Add(part,piece);
        }
        // A residual subpiece owns its own transition table. Unchanged remote
        // pieces do not invalidate it, including pieces of the same goal island.
        UInt128 localFog=fog&piece.Influence;var key=(part,localFog);
        if(_local.TryGetValue(key,out var cached)) { _cacheHits++;return cached; }
        var result=ImmutableArray.CreateBuilder<LocalStep>();var seen=new HashSet<(UInt128,UInt128)>();
        foreach(var move in piece.Moves)
        {
            if((localFog&move.Center)==0) continue;
            UInt128 next=localFog;var completed=ImmutableArray.CreateBuilder<int>();
            var physical=ImmutableArray.CreateBuilder<int>();bool curse=false;
            foreach(int cell in move.Cells)
            {
                UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                next &= ~bit;int item=_source.Occupancy[cell];
                if(item<0 || (next&_items[item])!=0) continue;
                if(_avoidCurse && _source.Items[item].Kind=="CURSE") { curse=true;break; }
                physical.Add(item);
                for(int n=0;n<_source.Items[item].Subscriptions;n++) completed.Add(item);
            }
            if(!curse && seen.Add((localFog^next,move.Center))) result.Add(new(move,localFog^next,completed.ToImmutable(),physical.ToImmutable()));
        }
        var steps=result.ToImmutable();if(_local.Count<4096) _local.Add(key,steps);
        return steps;
    }

    private IEnumerable<Node?> CompleteNext(Node start,int goal,Query query)
    {
        var stack=new Stack<Node>();stack.Push(start);
        var visited=new HashSet<(UInt128,int,UInt128)> { (start.Fog,_source.Remaining-start.Path.Length,_paddingPlentiful?0:start.Centers) };
        while(stack.TryPop(out var node))
        {
            yield return null;query.LocalNodes++;
            int left=_source.Remaining-node.Path.Length;
            if(left==0) continue;
            // The local job works toward one physical reward, without committing
            // to clearing the whole island. An incidental completion returns a
            // global reward node immediately, carrying all unfinished progress.
            var steps=Local(goal,node.Fog);
            for(int i=steps.Length-1;i>=0;i--)
            {
                var step=steps[i];var next=new Node(node.Fog&~step.Removed,node.Order.AddRange(step.Completed),
                    node.Path.Add(step.Move.Step),node.Centers|step.Move.Center);
                if(!step.Completed.IsEmpty) yield return next;
                else if(left>1 && visited.Add((next.Fog,left-1,_paddingPlentiful?0:next.Centers))) stack.Push(next);
            }
        }
    }
    private IEnumerable<Node?> Expand(Node node,Query query)
    {
        var cursors=Enumerable.Range(0,_items.Length).Where(i=>(node.Fog&_items[i])!=0 &&
            !(_avoidCurse && _source.Items[i].Kind=="CURSE"))
            .OrderBy(i=>_source.Items[i].Kind.StartsWith("CARD_",StringComparison.Ordinal)?0:1)
            .Select(i=>CompleteNext(node,i,query).GetEnumerator()).ToArray();
        var active=Enumerable.Repeat(true,cursors.Length).ToArray();
        try
        {
            while(active.Any(a=>a)) for(int i=0;i<cursors.Length;i++)
                if(active[i]) { if(cursors[i].MoveNext()) yield return cursors[i].Current;else active[i]=false; }
        }
        finally { foreach(var cursor in cursors) cursor.Dispose(); }
    }
    private IEnumerable<bool> Search(Query query)
    {
        var jobs=new Queue<IEnumerator<Node?>>();int tick=0;
        try
        {
            while(query.Pending.Count>0 || jobs.Count>0)
            {
                if(query.Pending.Count>0 && (jobs.Count==0 || ++tick%4==0))
                {
                    var node=query.Pending.Dequeue();
                    if(_paddingPlentiful && query.Frontiers.TryGetValue((node.Fog&_occupied,Word(node.Order),node.Path.Length),out var variants) &&
                        variants.Any(other=>other!=node.Fog && (other&node.Fog)==node.Fog)) { yield return true;continue; }
                    query.RewardNodes++;Publish(query,node);
                    if(node.Path.Length<_source.Remaining)
                    {
                        if(jobs.Count>=4096) { jobs.Dequeue().Dispose();query.Limited=true; }
                        jobs.Enqueue(Expand(node,query).GetEnumerator());
                    }
                    yield return true;
                }
                if(jobs.TryDequeue(out var job))
                {
                    if(job.MoveNext()) { if(job.Current is { } next) Enqueue(query,next);jobs.Enqueue(job); }
                    else job.Dispose();
                    yield return true;
                }
            }
        }
        finally { foreach(var job in jobs) job.Dispose(); }
    }
    private void Enqueue(Query query,Node node)
    {
        if(!MaySupport(node,query.Selected,false)) return;
        if(query.Pending.Count>=20000 || query.Visited.Count>=100000) { query.Limited=true;return; }
        string word=Word(node.Order);
        if(query.Visited.Contains((node.Fog,word,node.Path.Length,_paddingPlentiful?0:node.Centers))) return;
        if(_paddingPlentiful)
        {
            // With the same occupied progress, reward phases and click cost,
            // retaining more blank centers admits every continuation of the
            // smaller fog. Ample initial blank cells also guarantee padding.
            var key=(node.Fog&_occupied,word,node.Path.Length);
            if(!query.Frontiers.TryGetValue(key,out var variants)) query.Frontiers.Add(key,variants=[]);
            if(variants.Any(other=>(other&node.Fog)==node.Fog)) return;
            variants.RemoveAll(other=>(other&node.Fog)==other);variants.Add(node.Fog);
        }
        query.Visited.Add((node.Fog,word,node.Path.Length,_paddingPlentiful?0:node.Centers));
        int priority=node.Path.Length*4-(MaySupport(node,query.Selected,true)?40:0)-
            node.Order.Count(i=>_source.Items[i].Kind.StartsWith("CARD_",StringComparison.Ordinal))*3;
        query.Pending.Enqueue(node,(priority,query.Serial++));
    }
    private bool MaySupport(Node node,ImmutableArray<CrystalRewardOption> goals,bool settled)
    {
        if(goals.IsEmpty) return true;
        int left=_source.Remaining-node.Path.Length;
        var slots=new List<(string Kind,int Low,int High,UInt128 Need)>();int p=0,core=0;
        bool Potion(int i)=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal);
        int extraPotions=settled?0:Enumerable.Range(0,_items.Length).Where(i=>(node.Fog&_items[i])!=0 && Potion(i)).Sum(i=>_source.Items[i].Subscriptions);
        int potions=node.Order.Count(Potion);
        foreach(int i in node.Order)
        {
            string kind=_source.Items[i].Kind;
            if(Potion(i)) { slots.Add((kind,p,p,0));p++; }
            else { slots.Add((kind,core+potions,core+potions+extraPotions,0));core+=CrystalReachabilityTable.Draws(kind); }
        }
        if(!settled && node.Path.Length<_source.Remaining)
            foreach(int i in Enumerable.Range(0,_items.Length).Where(i=>(node.Fog&_items[i])!=0 && MayCover(node.Fog&_items[i],left)))
                for(int n=0;n<_source.Items[i].Subscriptions;n++)
                    slots.Add((_source.Items[i].Kind,Potion(i)?potions:core+potions,
                        Potion(i)?potions+extraPotions-1:_table!.MaximumDraws-6,node.Fog&_items[i]));
        bool Edge(int g,int s)=>goals[g].Kind switch {
            PredictorRewardKind.Card=>_table!.CardAt(slots[s].Kind,goals[g],slots[s].Low,slots[s].High),
            PredictorRewardKind.Potion=>_table!.PotionAt(slots[s].Kind,goals[g].Key,slots[s].Low,slots[s].High),
            PredictorRewardKind.Relic=>slots[s].Kind=="RELIC",_=>false };
        var owners=Enumerable.Repeat(-1,slots.Count).ToArray();
        bool Assign(int goal,bool[] seen)
        {
            for(int s=0;s<slots.Count;s++) if(!seen[s] && Edge(goal,s))
            { seen[s]=true;if(owners[s]<0 || Assign(owners[s],seen)) { owners[s]=goal;return true; } }
            return false;
        }
        UInt128 required=0;
        for(int g=0;g<goals.Length;g++)
        {
            UInt128 intersection=UInt128.MaxValue;bool any=false;
            for(int s=0;s<slots.Count;s++) if(Edge(g,s)) { intersection &= slots[s].Need;any=true; }
            if(!any) return false;required |= intersection;
            if(goals[g].Kind==PredictorRewardKind.Relic) { if(!slots.Any(s=>s.Kind=="RELIC")) return false; }
            else if(!Assign(g,new bool[slots.Count])) return false;
        }
        return MayCover(required,left);
    }
    private bool MayCover(UInt128 need,int left)
    {
        if(need==0) return true;
        if(left==0 || Bits(need)>left*9) return false;
        var key=(need,left);if(_coverBounds.TryGetValue(key,out bool cached)) return cached;
        int lower=0;
        while(need!=0)
        {
            if(++lower>left) break;
            int cell=(ulong)need!=0?BitOperations.TrailingZeroCount((ulong)need):64+BitOperations.TrailingZeroCount((ulong)(need>>64));
            int x=cell/11,y=cell%11;
            for(int a=Math.Max(0,x-2);a<=Math.Min(10,x+2);a++)
                for(int b=Math.Max(0,y-2);b<=Math.Min(10,y+2);b++) need &= ~((UInt128)1<<(a*11+b));
        }
        // Pairwise separated cells require different reveals. This bound grants
        // arbitrary centers and ignores ordering, so it cannot prove a witness.
        bool possible=lower<=left;if(_coverBounds.Count<50000) _coverBounds.Add(key,possible);return possible;
    }
    private ImmutableArray<PredictorCrystalStep>? Finish(Node node)
    {
        UInt128 fog=node.Fog;var path=node.Path;
        while(path.Length<_source.Remaining)
        {
            int next=-1;
            for(int cell=0;cell<121;cell++) if((fog&((UInt128)1<<cell))!=0)
            {
                int item=_source.Occupancy[cell];
                if(item<0 || Bits(fog&_items[item])>1) { next=cell;break; }
            }
            if(next<0) break;
            fog &= ~((UInt128)1<<next);path=path.Add(new(next/11,next%11,PredictorCrystalTool.Small));
        }
        if(path.Length==_source.Remaining) return path;
        // Same audited padding semantics, applied independently of Explorer search.
        UInt128 allowed=_initialFog&~_occupied;
        foreach(var item in _items)
        {
            UInt128 need=node.Fog&item;
            if(need!=0) allowed|=(_initialFog&item)&~(need&(~need+1));
        }
        allowed &= ~node.Centers;var prefix=ImmutableArray.CreateBuilder<PredictorCrystalStep>();
        for(int cell=0;cell<121 && prefix.Count+node.Path.Length<_source.Remaining;cell++)
            if((allowed&((UInt128)1<<cell))!=0) prefix.Add(new(cell/11,cell%11,PredictorCrystalTool.Small));
        return prefix.Count+node.Path.Length==_source.Remaining?prefix.ToImmutable().AddRange(node.Path):null;
    }
    private void Publish(Query query,Node node)
    {
        if(!MaySupport(node,query.Selected,true)) return;
        string signature=Word(node.Order);if(query.Outcomes.Contains(signature)) return;
        var path=Finish(node);if(path==null) return;
        var run=PredictorCrystalExplorer.Replay(_source,path.Value);query.RuntimeRows++;
        query.Outcomes.Add(signature);if(run.Phase!=PredictorPhase.Rewards) return;
        if(!run.ExportCrystal().Revealed.SequenceEqual(node.Order)) throw new InvalidOperationException("CrystalRewardNodePaddingChangedOrder");
        var row=new CrystalRewardRoute(path.Value,run.Rewards);
        if(_witnessPaths.Add(string.Join(';',row.Steps))) _witnesses.Add(row);
        Consider(query,row);
    }
    private void Consider(Query query,CrystalRewardRoute row)
    {
        var options=PredictorCrystalExplorer.Options(row).ToArray();
        if(query.Selected.Any(o=>!options.Any(o.Accepts))) return;
        var additions=options.Where(o=>!query.Selected.Contains(o) && !query.Available.ContainsKey(o)).ToArray();
        if(query.Plan!=null && additions.Length==0) return;
        var proof=PredictorCrystalExplorer.VerifySelection(_source,row,query.Selected);if(proof==null) return;
        if(query.Plan==null) { query.Plan=proof;query.Gold=row.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount); }
        foreach(var option in additions)
        {
            var extended=PredictorCrystalExplorer.VerifySelection(_source,row,query.Selected.Add(option));
            if(extended!=null) query.Available.Add(option,extended);
        }
    }
}
