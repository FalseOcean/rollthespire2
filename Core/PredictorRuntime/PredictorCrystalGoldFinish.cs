using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

// Positive-only improvement: first collect extra tail gold, then optionally
// re-realize the same phase words with different gold piles throughout the
// board. Small/Big gold both consume one draw per callback. Neither bounded
// pass proves globally optimal gold or changes candidate completeness.
internal static class PredictorCrystalGoldFinish
{
    private static PredictorCrystalSolution? Verify(PredictorCrystalSnapshot source,
        ImmutableArray<PredictorCrystalStep> path,PredictorRun run,ImmutableArray<CrystalRewardOption> selected,bool includeRerolls)
    {
        var row=new CrystalRewardRoute(path,run.Rewards) { EnchantmentTargets=CrystalRewardOption.UsesEnchantments(source.State) };
        if(includeRerolls && PredictorSettlementEffects.Has(source.State,"DRIFTWOOD"))
            row=PredictorCrystalExplorer.WithRerolls(row,run);
        return PredictorCrystalExplorer.VerifySelection(source,row,selected);
    }
    private sealed record Move(PredictorCrystalStep Step,int[] Cells,UInt128 Mask);
    private sealed record State(UInt128 Fog,ImmutableArray<int> Order,ImmutableArray<PredictorCrystalStep> Steps);
    internal static CrystalOptionProjection Improve(PredictorCrystalSnapshot source,
        CrystalOptionProjection projection,ImmutableArray<CrystalRewardOption> selected,bool avoidCurse,bool reshape=true,bool includeRerolls=false)
    {
        if(projection.SelectedPlan is not { } plan || plan.Steps.Length!=source.Remaining || plan.Steps.IsEmpty) return projection;
        var original=PredictorCrystalExplorer.Replay(source,plan.Steps);
        if(original.Phase!=PredictorPhase.Rewards) return projection;
        int Gold(PredictorRun run)=>run.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
        int bestGold=Gold(original);var best=plan;
        var masks=source.Items.Select(i=>i.X<0?(UInt128)0:
            (from x in Enumerable.Range(i.X,i.Width) from y in Enumerable.Range(i.Y,i.Height)
             select (UInt128)1<<(x*11+y)).Aggregate((UInt128)0,(a,b)=>a|b)).ToArray();
        UInt128 fog=0;for(int i=0;i<121;i++) if(source.Hidden[i]) fog|=(UInt128)1<<i;
        int cut=0;
        for(int n=0;n<plan.Steps.Length;n++)
        {
            var step=plan.Steps[n];UInt128 prior=fog;
            foreach(var cell in PredictorCrystalSearch.Cells(step.X,step.Y,step.Tool)) fog &= ~((UInt128)1<<(cell.X*11+cell.Y));
            if(Enumerable.Range(0,masks.Length).Any(i=>(prior&masks[i])!=0 && (fog&masks[i])==0 &&
                !source.Items[i].Kind.StartsWith("GOLD_",StringComparison.Ordinal))) cut=n+1;
        }
        cut=Math.Min(cut,plan.Steps.Length-1);
        var prefix=plan.Steps.Take(cut).ToImmutableArray();
        var before=PredictorCrystalExplorer.Replay(source,prefix).ExportCrystal();
        fog=0;for(int i=0;i<121;i++) if(before.Hidden[i]) fog|=(UInt128)1<<i;
        UInt128 initialFog=fog,occupied=masks.Aggregate((UInt128)0,(a,b)=>a|b);
        UInt128 goldMask=Enumerable.Range(0,masks.Length).Where(i=>source.Items[i].Kind.StartsWith("GOLD_",StringComparison.Ordinal))
            .Aggregate((UInt128)0,(a,i)=>a|masks[i]);
        var moves=(from center in Enumerable.Range(0,121) where before.Hidden[center]
            from tool in new[]{PredictorCrystalTool.Big,PredictorCrystalTool.Small}
            let cells=PredictorCrystalSearch.Cells(center/11,center%11,tool).Select(c=>c.X*11+c.Y).ToArray()
            select new Move(new(center/11,center%11,tool),cells,cells.Aggregate((UInt128)0,(a,c)=>a|((UInt128)1<<c))))
            .OrderByDescending(m=>Bits(m.Mask&goldMask&fog)).ToArray();
        var seen=new HashSet<(UInt128,string,int)>();var outcomes=new HashSet<string>();
        var stack=new Stack<State>();stack.Push(new(fog,before.Revealed,[]));
        var watch=Stopwatch.StartNew();
        while(stack.TryPop(out var node) && seen.Count<16_384 && watch.Elapsed<TimeSpan.FromMilliseconds(75))
        {
            string history=string.Join(',',node.Order);
            if(!seen.Add((node.Fog,history,node.Steps.Length))) continue;
            if(node.Order.Skip(before.Revealed.Length).Any(i=>source.Items[i].Kind.StartsWith("GOLD_",StringComparison.Ordinal)) &&
                !outcomes.Contains(history) && Pad(node) is { } path)
            {
                var candidate=PredictorCrystalExplorer.Replay(source,path);outcomes.Add(history);
                if(candidate.Phase==PredictorPhase.Rewards && Gold(candidate)>bestGold)
                {
                    var proof=Verify(source,path,candidate,selected,includeRerolls);
                    if(proof!=null) { best=proof;bestGold=Gold(candidate); }
                }
            }
            if(node.Steps.Length==before.Remaining) continue;
            for(int n=moves.Length-1;n>=0;n--)
            {
                var move=moves[n];UInt128 center=(UInt128)1<<(move.Step.X*11+move.Step.Y);
                if((node.Fog&center)==0 || (node.Fog&move.Mask&goldMask)==0) continue;
                UInt128 next=node.Fog;var order=node.Order;bool curse=false;
                foreach(int cell in move.Cells)
                {
                    UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                    next &= ~bit;int item=before.Occupancy[cell];
                    if(item<0 || (next&masks[item])!=0) continue;
                    if(avoidCurse && source.Items[item].Kind=="CURSE") { curse=true;break; }
                    order=order.AddRange(Enumerable.Repeat(item,source.Items[item].Subscriptions));
                }
                if(!curse) stack.Push(new(next,order,node.Steps.Add(move.Step)));
            }
        }
        var improved=projection with { SelectedPlan=best,Gold=bestGold };
        return reshape?Reshape(source,improved,selected,avoidCurse,masks,includeRerolls):improved;

        ImmutableArray<PredictorCrystalStep>? Pad(State node)
        {
            UInt128 rest=node.Fog;var steps=node.Steps;
            while(steps.Length<before.Remaining)
            {
                int cell=-1;
                for(int i=0;i<121;i++) if((rest&((UInt128)1<<i))!=0)
                {
                    int item=before.Occupancy[i];
                    if(item<0 || Bits(rest&masks[item])>1) { cell=i;break; }
                }
                if(cell<0) break;
                rest &= ~((UInt128)1<<cell);steps=steps.Add(new(cell/11,cell%11,PredictorCrystalTool.Small));
            }
            if(steps.Length==before.Remaining) return prefix.AddRange(steps);
            // Safe padding can precede a Big reveal that covers the blank cells.
            UInt128 allowed=initialFog&~occupied;
            foreach(var mask in masks)
            {
                UInt128 need=node.Fog&mask;
                if(need!=0) allowed|=(initialFog&mask)&~(need&(~need+1));
            }
            foreach(var step in node.Steps) allowed &= ~((UInt128)1<<(step.X*11+step.Y));
            var pad=ImmutableArray.CreateBuilder<PredictorCrystalStep>();
            for(int i=0;i<121 && pad.Count+node.Steps.Length<before.Remaining;i++)
                if((allowed&((UInt128)1<<i))!=0) pad.Add(new(i/11,i%11,PredictorCrystalTool.Small));
            return pad.Count+node.Steps.Length==before.Remaining?prefix.AddRange(pad).AddRange(node.Steps):null;
        }
    }

    // Bounded beam discovery, never an impossibility/optimality certificate.
    // Keep potion/core callback words separately, normalize only gold size,
    // and let different legal reveal centers realize those same RNG offsets.
    // This can trade a small incidental pile for a large one in the prefix;
    // locking the old prefix would make that improvement undiscoverable.
    private static CrystalOptionProjection Reshape(PredictorCrystalSnapshot source,CrystalOptionProjection projection,
        ImmutableArray<CrystalRewardOption> selected,bool avoidCurse,UInt128[] masks,bool includeRerolls)
    {
        var original=PredictorCrystalExplorer.Replay(source,projection.SelectedPlan!.Steps).ExportCrystal();
        bool Potion(string kind)=>kind.StartsWith("POTION_",StringComparison.Ordinal);
        string Token(string kind)=>kind.StartsWith("GOLD_",StringComparison.Ordinal)?"GOLD":kind;
        var core=original.Revealed.Select(i=>source.Items[i].Kind).Where(k=>!Potion(k)).Select(Token).ToArray();
        var potions=original.Revealed.Select(i=>source.Items[i].Kind).Where(Potion).ToArray();
        int Gold(ImmutableArray<int> order)=>order.Sum(i=>source.Items[i].Kind switch { "GOLD_SMALL"=>10,"GOLD_BIG"=>30,_=>0 });
        int fixedGold=Gold(source.Revealed),goldCalls=core.Count(k=>k=="GOLD")-source.Revealed.Count(i=>Token(source.Items[i].Kind)=="GOLD");
        int upper=fixedGold+source.Items.Select((item,i)=>(item,i)).Where(p=>p.item.X>=0 &&
            !source.Revealed.Contains(p.i) && p.item.Kind.StartsWith("GOLD_",StringComparison.Ordinal))
            .SelectMany(p=>Enumerable.Repeat(p.item.Kind=="GOLD_BIG"?30:10,p.item.Subscriptions)).OrderDescending().Take(goldCalls).Sum();
        if(upper<=projection.Gold) return projection;
        UInt128 fog=0;for(int i=0;i<121;i++) if(source.Hidden[i]) fog|=(UInt128)1<<i;
        UInt128 initialFog=fog,required=original.Revealed.Where(i=>!source.Items[i].Kind.StartsWith("GOLD_",StringComparison.Ordinal))
            .Aggregate((UInt128)0,(bits,i)=>bits|masks[i]);
        UInt128 goldMask=Enumerable.Range(0,masks.Length).Where(i=>source.Items[i].Kind.StartsWith("GOLD_",StringComparison.Ordinal))
            .Aggregate((UInt128)0,(bits,i)=>bits|masks[i]);
        var residues=new UInt128[9];for(int i=0;i<121;i++) residues[(i/11%3)*3+i%11%3]|=(UInt128)1<<i;
        var moves=(from cell in Enumerable.Range(0,121) where source.Hidden[cell]
            from tool in new[]{PredictorCrystalTool.Big,PredictorCrystalTool.Small}
            let cells=PredictorCrystalSearch.Cells(cell/11,cell%11,tool).Select(c=>c.X*11+c.Y).ToArray()
            select new Move(new(cell/11,cell%11,tool),cells,cells.Aggregate((UInt128)0,(bits,c)=>bits|((UInt128)1<<c)))).ToArray();
        bool Compatible(ImmutableArray<int> order,out bool complete)
        {
            int c=0,p=0;complete=false;
            foreach(int i in order)
            {
                string kind=source.Items[i].Kind;
                if(Potion(kind)) { if(p>=potions.Length || kind!=potions[p++]) return false; }
                else if(c>=core.Length || Token(kind)!=core[c++]) return false;
            }
            complete=c==core.Length && p==potions.Length;return true;
        }
        bool Coverable(UInt128 need,UInt128 liveFog,int left)=>need==0 || left>0 && Bits(need)<=9*left &&
            !residues.Any(r=>Bits(need&r)>left) && (left!=1 || moves.Any(m=>(liveFog&((UInt128)1<<(m.Step.X*11+m.Step.Y)))!=0 && (need&m.Mask)==need));
        ImmutableArray<PredictorCrystalStep>? Pad(State node)
        {
            var steps=node.Steps;UInt128 rest=node.Fog;
            for(int i=0;i<121 && steps.Length<source.Remaining;i++)
                if((rest&((UInt128)1<<i))!=0 && (source.Occupancy[i]<0 || Bits(rest&masks[source.Occupancy[i]])>1))
                { rest &= ~((UInt128)1<<i);steps=steps.Add(new(i/11,i%11,PredictorCrystalTool.Small)); }
            if(steps.Length==source.Remaining) return steps;
            // Padding before productive Big reveals may use blank cells later
            // covered by them, but never one of their actual click centers.
            UInt128 available=initialFog & ~masks.Aggregate((UInt128)0,(bits,m)=>bits|m);
            foreach(var step in node.Steps) available &= ~((UInt128)1<<(step.X*11+step.Y));
            var prefix=ImmutableArray.CreateBuilder<PredictorCrystalStep>();
            for(int i=0;i<121 && prefix.Count+node.Steps.Length<source.Remaining;i++)
                if((available&((UInt128)1<<i))!=0) prefix.Add(new(i/11,i%11,PredictorCrystalTool.Small));
            return prefix.Count+node.Steps.Length==source.Remaining?prefix.ToImmutable().AddRange(node.Steps):null;
        }
        var layer=new List<State> { new(fog,source.Revealed,[]) };var seen=new HashSet<(UInt128,string,int)>();
        var outcomes=new HashSet<string>();var watch=Stopwatch.StartNew();
        var best=projection;
        for(int depth=0;depth<=source.Remaining && layer.Count>0 && watch.ElapsedMilliseconds<200 && seen.Count<32_768;depth++)
        {
            var nextLayer=new List<State>();
            foreach(var node in layer)
            {
                if(watch.ElapsedMilliseconds>=200 || seen.Count>=32_768) break;
                if(Compatible(node.Order,out bool complete) && complete && Gold(node.Order)>best.Gold &&
                    outcomes.Add(string.Join(',',node.Order)) && Pad(node) is { } path)
                {
                    var result=PredictorCrystalExplorer.Replay(source,path);
                    int amount=result.Rewards.Where(r=>r.Kind==PredictorRewardKind.Gold).Sum(r=>r.GoldAmount);
                    if(amount>best.Gold && Verify(source,path,result,selected,includeRerolls) is { } proof)
                        best=best with { SelectedPlan=proof,Gold=amount };
                    if(best.Gold>=upper) return best;
                }
                if(depth==source.Remaining) continue;
                foreach(var move in moves)
                {
                    if((node.Fog&((UInt128)1<<(move.Step.X*11+move.Step.Y)))==0 || (node.Fog&move.Mask&(required|goldMask))==0) continue;
                    UInt128 next=node.Fog;var order=node.Order;bool curse=false;
                    foreach(int cell in move.Cells)
                    {
                        UInt128 bit=(UInt128)1<<cell;if((next&bit)==0) continue;
                        next &= ~bit;int item=source.Occupancy[cell];
                        if(item<0 || (next&masks[item])!=0 || order.Contains(item)) continue;
                        if(avoidCurse && source.Items[item].Kind=="CURSE") { curse=true;break; }
                        order=order.AddRange(Enumerable.Repeat(item,source.Items[item].Subscriptions));
                    }
                    if(curse || !Compatible(order,out _) || !Coverable(next&required,next,source.Remaining-depth-1)) continue;
                    if(seen.Add((next,string.Join(',',order),depth+1))) nextLayer.Add(new(next,order,node.Steps.Add(move.Step)));
                }
            }
            layer=nextLayer.OrderByDescending(n=>Gold(n.Order)*1000+Bits(initialFog&required & ~n.Fog)*100+n.Order.Length*10)
                .Take(192).ToList();
        }
        return best;
    }
    private static int Bits(UInt128 value)=>BitOperations.PopCount((ulong)value)+BitOperations.PopCount((ulong)(value>>64));
}
