using System.Collections.Concurrent;
using System.Numerics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    private static readonly UInt128[] CenterCovers=Enumerable.Range(0,121).Select(i=>
        PredictorCrystalSearch.Cells(i/11,i%11,PredictorCrystalTool.Big)
            .Aggregate((UInt128)0,(mask,c)=>mask|((UInt128)1<<(c.X*11+c.Y)))).ToArray();
    private readonly ConcurrentDictionary<(UInt128 Need,UInt128 Centers,int Left),bool> _centerCoverMemo=[];
    private long _currentCenterRejected;
    internal long CurrentCenterRejected=>Interlocked.Read(ref _currentCenterRejected);

    // Every future legal center is hidden now. Keep that restriction, but still
    // allow these centers to be reused in any order. Big dominates Small only
    // in this relaxed coverage question, never in the actual click search.
    private bool CanCoverFromCenters(UInt128 need,UInt128 centers,int left)
    {
        if(need==0) return true;
        if(left<=0) return false;
        UInt128 relevant=0;
        for(UInt128 cells=need;cells!=0;cells &= cells-1) relevant |= CenterCovers[LowBitIndex(cells)];
        centers &= relevant;
        var key=(need,centers,left);
        if(_centerCoverMemo.TryGetValue(key,out bool known)) return known;
        // Capacity limits relax the proof, not the legal search domain.
        if(_centerCoverMemo.Count>=100_000) return true;
        var maximal=new List<UInt128>();
        for(UInt128 remaining=centers;remaining!=0;remaining &= remaining-1)
        {
            UInt128 gain=CenterCovers[LowBitIndex(remaining)]&need;
            if(maximal.Any(m=>(gain&m)==gain)) continue;
            maximal.RemoveAll(m=>(gain&m)==m);maximal.Add(gain);
        }
        bool result;
        if(maximal.Any(g=>g==need)) result=true;
        else if(left==1 || maximal.Count==0 || PopCount(need)>left*maximal.Max(PopCount)) result=false;
        else
        {
            UInt128 cell=0;int best=int.MaxValue;
            for(UInt128 remaining=need;remaining!=0;remaining &= remaining-1)
            {
                UInt128 bit=remaining & (~remaining+1);int count=maximal.Count(g=>(g&bit)!=0);
                if(count<best) {cell=bit;best=count;if(best<=1) break;}
            }
            result=maximal.Where(g=>(g&cell)!=0).Any(g=>CanCoverFromCenters(need&~g,centers,left-1));
        }
        _centerCoverMemo.TryAdd(key,result);return result;
    }

    private static int LowBitIndex(UInt128 bits)=>(ulong)bits!=0
        ?BitOperations.TrailingZeroCount((ulong)bits):64+BitOperations.TrailingZeroCount((ulong)(bits>>64));
}
