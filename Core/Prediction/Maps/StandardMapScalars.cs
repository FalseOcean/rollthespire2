namespace RolltheSpire2.Core.Prediction.Maps;

// Scalar Final DP donor; independent graph evaluation is retained for Exact/parity.
internal readonly record struct MapOutcomeSignature(ulong Bits)
{
    internal int Guarantee(int dimension) => (int)(Bits >> (dimension * 4) & 15);
    internal int Maximum(int dimension) => (int)(Bits >> (24 + dimension * 4) & 15);
    internal uint Key(int family) => (uint)(Bits & 0xffffff) |
        (family == 0 ? 0 : (uint)Maximum(family - 1) << 24);
    internal static MapOutcomeSignature Create(ReadOnlySpan<int> guarantees, ReadOnlySpan<int> maxima)
    {
        ulong bits=0;
        for(int i=0;i<6;i++) {if(guarantees[i] is <0 or >15)throw new ArgumentOutOfRangeException(nameof(guarantees));bits|=(ulong)guarantees[i]<<(4*i);}
        for(int i=0;i<5;i++) {if(maxima[i] is <0 or >15)throw new ArgumentOutOfRangeException(nameof(maxima));bits|=(ulong)maxima[i]<<(24+4*i);}
        return new(bits);
    }
    internal static uint ComponentwiseMin(uint a,uint b,int family)
    {
        uint key=0;for(int i=0;i<(family==0?6:7);i++)key|=Math.Min(a>>(i*4)&15,b>>(i*4)&15)<<(i*4);return key;
    }
}


internal static class MapBasisScalars
{
    private readonly record struct State(bool ReachesBoss,MapOutcomeSignature Signature);
    internal static MapOutcomeSignature Evaluate(GeneratedStandardMap map) => Evaluate(map.Start,map.Boss);
    internal static MapOutcomeSignature Evaluate(MapNode start,MapNode boss) => EvaluateCore(start,boss,false);
    private static MapOutcomeSignature EvaluateCore(MapNode start,MapNode boss,bool projectUnassigned)
    {
        var memo=new Dictionary<MapNode,State>();
        State Visit(MapNode node)
        {
            if(ReferenceEquals(node,boss))return new(true,default);
            if(memo.TryGetValue(node,out var cached))return cached;
            Span<int> minima=stackalloc int[6];minima.Fill(16);
            Span<int> maxima=stackalloc int[5];maxima.Clear();bool any=false;
            foreach(var child in node.Children)
            {
                if(child.FinalRow<=node.FinalRow)throw new ArgumentException("Expected increasing-row DAG");
                var next=Visit(child);if(!next.ReachesBoss)continue;any=true;
                for(int i=0;i<6;i++)minima[i]=Math.Min(minima[i],next.Signature.Guarantee(i));
                for(int i=0;i<5;i++)maxima[i]=Math.Max(maxima[i],next.Signature.Maximum(i));
            }
            if(!any)return memo[node]=new(false,default);
            if(!ReferenceEquals(node,start))
            {
                // Prediction view only: never write to the node or generator workspace.
                var observed=projectUnassigned && node.PointType==MapPointType.Unassigned ? MapPointType.Monster : node.PointType;
                int type=Index(observed);
                if(type>=0){minima[type]++;maxima[type]++;}
                minima[5]=observed==MapPointType.Monster?minima[5]+1:0;
            }
            return memo[node]=new(true,MapOutcomeSignature.Create(minima,maxima));
        }
        var result=Visit(start);if(!result.ReachesBoss)throw new ArgumentException("No legal Start to Boss path");
        return result.Signature;
    }
    internal static int Index(MapPointType type) => type switch
    {MapPointType.Monster=>0,MapPointType.Elite=>1,MapPointType.RestSite=>2,MapPointType.Shop=>3,MapPointType.Unknown=>4,_=>-1};
}

