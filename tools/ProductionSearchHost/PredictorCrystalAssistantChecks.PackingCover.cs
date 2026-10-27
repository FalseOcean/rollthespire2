using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckPackingProduction(PredictorCrystalSnapshot source,ImmutableArray<CrystalRewardOption> selected,string output)
    {
        var receipts=new List<object>();CrystalReachabilityUpdate? prior=null;
        foreach(bool packing in Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1"?new[]{true,false}:new[]{false,true})
        {
            var session=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,
                includeRerolls:true,packingCover:packing);
            var watch=Stopwatch.StartNew();double? earlyMs=null;
            var result=session.Advance(selected,TimeSpan.Zero,publishExcluded:_=>earlyMs=watch.Elapsed.TotalMilliseconds);
            double milliseconds=watch.Elapsed.TotalMilliseconds;
            var bound=(PredictorCrystalOffsetBound)typeof(PredictorCrystalReachability).GetField("_offsetBound",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(session)!;
            if(prior!=null) Check(prior.ExcludedCandidates.ToHashSet().SetEquals(result.ExcludedCandidates) &&
                prior.Projection.Available.Keys.ToHashSet().SetEquals(result.Projection.Available.Keys) && prior.Complete==result.Complete,
                "Packing preserves the actual production initial projection");
            prior=result;
            receipts.Add(new {packing,earlyMs,milliseconds,result,bound.CoverSearchNodes,bound.PackingChecks,bound.PackingRejected});
            Console.WriteLine($"CRYSTAL_PACKING_PRODUCTION seed={source.Context.Seed};packing={packing};ms={milliseconds:F3};earlyMs={earlyMs:F3};coverNodes={bound.CoverSearchNodes};packingRejected={bound.PackingRejected};excluded={result.ExcludedCandidates.Length}");
        }
        ProductionSearchReplay.Write(Path.Combine(output,"packing-production-"+source.Context.Seed+".json"),new {source,selected,receipts});
    }

    private static void CheckPackingCoverOracle(PredictorCrystalSnapshot source)
    {
        int checks=0;long rejected=0;
        for(int shape=0;shape<4;shape++)
        {
            var hidden=Enumerable.Range(0,121).Select(i=>shape switch {
                0=>true,1=>source.Hidden[i],2=>i%4!=0,_=>i/11%2==0 || i%11%2==0 }).ToImmutableArray();
            var board=source with {Hidden=hidden};
            var bound=new PredictorCrystalOffsetBound(board,new(board,includeRerolls:true),true,packingCover:true);
            int[] cells=Enumerable.Range(0,121).Where(i=>hidden[i]).OrderBy(i=>(i*37+shape*23)%127).Take(9).ToArray();
            // Independent exact cover DP over at most nine demand cells. Every
            // initial hidden center is enumerated; no greedy/dominance/memo
            // implementation from the candidate proof is used as the oracle.
            var covers=Enumerable.Range(0,121).Where(i=>hidden[i]).Select(center=>
                cells.Select((cell,n)=>(cell,n)).Where(p=>Math.Abs(p.cell/11-center/11)<=1 && Math.Abs(p.cell%11-center%11)<=1)
                    .Aggregate(0,(mask,p)=>mask|(1<<p.n))).Distinct().ToArray();
            var minimum=new int[1<<cells.Length];
            for(int mask=1;mask<minimum.Length;mask++)
                minimum[mask]=1+covers.Where(c=>(c&mask)!=0).Min(c=>minimum[mask&~c]);
            for(int mask=0;mask<minimum.Length;mask++)
            {
                UInt128 need=0;
                for(int i=0;i<cells.Length;i++) if((mask&(1<<i))!=0) need|=(UInt128)1<<cells[i];
                for(int left=0;left<=6;left++)
                {
                    Check(bound.MayCover(need,left)==(minimum[mask]<=left),"Packing cover matches independent exact demand-subset DP");
                    checks++;
                }
            }
            rejected+=bound.PackingRejected;
        }
        Check(rejected>0,"Packing oracle exercises denials beyond the residue lower bound");
        Console.WriteLine($"CRYSTAL_PACKING_COVER_ORACLE_PASS exactComparisons={checks};packingRejected={rejected};hiddenCenterVariants=4;scope=relaxed-cover-not-click-order");
    }
}
