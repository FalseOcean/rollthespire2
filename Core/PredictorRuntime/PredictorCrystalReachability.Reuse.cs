using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalReachability
{
    // All entries belong to this frozen board/budget/curse policy. Take order is
    // part of the key: a proof for one ordered selection is not another's proof.
    private static string SelectionKey(ImmutableArray<CrystalRewardOption> selected) =>
        string.Join('|',selected.Select(s=>s.CacheKey));
    private readonly object _reuseSync=new();
    private readonly ConcurrentDictionary<string,CrystalOptionProjection> _published=new();
    private readonly ConcurrentDictionary<string,int> _projectedRows=new();
    private readonly Dictionary<string,CrystalReachabilityUpdate> _finished=[]; // _sync owned
    private readonly ConcurrentDictionary<(string Path,string Selection),Lazy<PredictorCrystalSolution?>> _takeProofs=new();
    private readonly ConditionalWeakTable<CrystalRewardRoute,string> _pathKeys=new();
    private long _useClock,_proofReplays,_proofHits;
    internal long ProofReplays=>Interlocked.Read(ref _proofReplays);
    internal long ProofCacheHits=>Interlocked.Read(ref _proofHits);

    internal CrystalOptionProjection PeekKnown(ImmutableArray<CrystalRewardOption> selected) =>
        _published.TryGetValue(SelectionKey(selected),out var result)?result:new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);

    private CrystalOptionProjection PublishKnown(string key,CrystalOptionProjection incoming,ImmutableArray<CrystalRewardOption> selected)
    {
        // Discovery publishes positives only. The retained gold frontier owns
        // its separate certificate; ordinary candidate projection cannot clear
        // that certificate or attach a new authority to a bounded improvement.
        return _published.AddOrUpdate(key,incoming,(_,prior)=>
        {
            // Both inputs claim truth for this exact frozen ordered selection.
            // An optimum/absence certificate and a contradicting typed witness
            // cannot be merged into a confident but internally inconsistent row.
            if(prior.GoldComplete && incoming.SelectedPlan!=null &&
                (prior.SelectedPlan==null || incoming.Gold>prior.Gold) ||
                incoming.GoldComplete && prior.SelectedPlan!=null &&
                (incoming.SelectedPlan==null || prior.Gold>incoming.Gold))
                throw new InvalidOperationException("CrystalGoldConflictingPublishedProof");
            return new(prior.Available.SetItems(incoming.Available),
                incoming.SelectedPlan!=null && (prior.SelectedPlan==null || incoming.Gold>prior.Gold)?incoming.SelectedPlan:prior.SelectedPlan,
                Math.Max(prior.Gold,incoming.Gold)) {
                GoldComplete=prior.GoldComplete || incoming.GoldComplete,
                SacrificeRelics=prior.SacrificeRelics.Union(incoming.SacrificeRelics),
                GoldUpperBound=prior.GoldComplete?prior.GoldUpperBound:incoming.GoldUpperBound??prior.GoldUpperBound };
        });
    }

    private PredictorCrystalSolution? VerifyCached(CrystalRewardRoute row,ImmutableArray<CrystalRewardOption> selected)
    {
        string path=_pathKeys.GetValue(row,r=>string.Join(';',r.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}")));
        var key=(path,SelectionKey(selected));
        if(_takeProofs.TryGetValue(key,out var existing)) { Interlocked.Increment(ref _proofHits);return existing.Value; }
        // Root slot contradictions cover every path and take order. Reprojection
        // must not redo skipped take checks for these now-retired candidates.
        // Do not broaden ordered replay failures into this all-path shortcut.
        if(rootCardSlots && IsRootCardConflict(selected)) return null;
        PredictorCrystalSolution? Verify()
        {
            Interlocked.Increment(ref _proofReplays);
            return PredictorCrystalExplorer.VerifySelection(source,row,selected);
        }
        // A full cache affects speed only. A null entry is a failed take on THIS
        // path, never a global unreachability fact for the target combination.
        if(_takeProofs.Count>=16_384) return Verify();
        var pending=new Lazy<PredictorCrystalSolution?>(Verify,LazyThreadSafetyMode.ExecutionAndPublication);
        var stored=_takeProofs.GetOrAdd(key,pending);
        if(!ReferenceEquals(stored,pending)) Interlocked.Increment(ref _proofHits);
        return stored.Value;
    }
}
