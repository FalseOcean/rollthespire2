using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    private sealed record ContinuationProof(CrystalOffsetReceipt Receipt,ImmutableArray<UInt128> RequiredCells);
    private readonly ConcurrentDictionary<string,Lazy<ContinuationProof>> _continuations=[];
    private long _continuationChecks,_continuationRejected,_continuationHits,_continuationTicks;
    internal long ContinuationChecks=>Interlocked.Read(ref _continuationChecks);
    internal long ContinuationRejected=>Interlocked.Read(ref _continuationRejected);
    internal long ContinuationHits=>Interlocked.Read(ref _continuationHits);
    internal double ContinuationSeconds=>Interlocked.Read(ref _continuationTicks)/(double)Stopwatch.Frequency;
    private long _completionCoverRejected;
    internal long CompletionCoverRejected=>Interlocked.Read(ref _completionCoverRejected);

    // No future physical rewards: only the given two phase words and the
    // remaining reroll suffix. Unknown/cutoff never certifies absence.
    internal CrystalOffsetReceipt CheckFixedWord(ImmutableArray<CrystalRewardOption> goals,
        ImmutableArray<int> revealed,int stateLimit=3_000)
    {
        if(!_admitted || !_table.IncludesRerolls || goals.Length>16) return new(CrystalOffsetVerdict.Incomplete,0);
        var ordered=goals.Distinct().OrderBy(g=>g.Kind).ThenBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
        string prefix=string.Join(',',revealed.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)))+"/"+
            string.Join(',',revealed.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)));
        string key=$"fixed:{stateLimit}:{prefix}:"+string.Join('|',ordered.Select(g=>g.CacheKey));
        if(_proofs.TryGetValue(key,out var known)) return known.Value;
        if(_proofs.Count>=16_384) return new(CrystalOffsetVerdict.Incomplete,0);
        return _proofs.GetOrAdd(key,_=>new Lazy<CrystalOffsetReceipt>(()=>{
            lock(_sync) return SolveRerolls(ordered,stateLimit,revealed,0,0);
        },LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // Board clicks have not generated rewards yet: retain the original RNG and
    // bag, fix the completed phase words and remaining eligible physical items.
    // Reward-domain collection uses the relaxed initial cover; final application
    // also restricts centers to current fog. Neither admits an unknown as UNSAT.
    internal CrystalOffsetReceipt CheckContinuation(ImmutableArray<CrystalRewardOption> goals,
        ImmutableArray<int> revealed,UInt128 fog,int left,int stateLimit=3_000,bool completionCover=true,bool currentCenters=true)
    {
        if(!_admitted || !_table.IncludesRerolls || goals.Length>16) return new(CrystalOffsetVerdict.Incomplete,0);
        Interlocked.Increment(ref _continuationChecks);
        var ordered=goals.Distinct().OrderBy(g=>g.Kind).ThenBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
        // Geometry variants share one reward-level proof. Exclude a future item
        // only when even relaxed cover cannot finish it in the remaining clicks.
        // The cache holds reward-domain alternatives. Joint remaining coverage
        // is checked against the caller's actual fog only after full enumeration.
        int potions=0,core=0;
        for(int i=0;i<_potions.Length;i++) if(!revealed.Contains(_potions[i].Item) && CanCover(_potions[i].Need&fog,left)) potions|=1<<i;
        for(int i=0;i<_core.Length;i++) if(!revealed.Contains(_core[i].Item) && CanCover(_core[i].Need&fog,left)) core|=1<<i;
        string prefix=string.Join(',',revealed.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)))+"/"+
            string.Join(',',revealed.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)));
        string key=$"{completionCover}:{stateLimit}:{potions}:{core}:{prefix}:"+string.Join('|',ordered.Select(g=>g.CacheKey));
        if(_continuations.TryGetValue(key,out var known)) {Interlocked.Increment(ref _continuationHits);return Apply(known.Value);}
        if(_continuations.Count>=4096) return new(CrystalOffsetVerdict.Incomplete,0);
        var pending=new Lazy<ContinuationProof>(()=>{
            long start=Stopwatch.GetTimestamp();CrystalOffsetReceipt receipt;
            List<UInt128>? required=completionCover?[]:null;
            lock(_sync) receipt=SolveRerolls(ordered,stateLimit,revealed,potions,core,required);
            Interlocked.Add(ref _continuationTicks,Stopwatch.GetTimestamp()-start);
            if(receipt.Verdict==CrystalOffsetVerdict.Impossible) Interlocked.Increment(ref _continuationRejected);
            return new(receipt,required?.ToImmutableArray()??default);
        },LazyThreadSafetyMode.ExecutionAndPublication);
        var proof=_continuations.GetOrAdd(key,pending);
        if(!ReferenceEquals(proof,pending)) Interlocked.Increment(ref _continuationHits);
        return Apply(proof.Value);

        CrystalOffsetReceipt Apply(ContinuationProof proof)
        {
            // Only a COMPLETE reward-domain enumeration can exclude all
            // alternatives. A partial list is never used to prove absence.
            if(proof.Receipt.Verdict!=CrystalOffsetVerdict.Possible || proof.RequiredCells.IsDefault) return proof.Receipt;
            if(proof.RequiredCells.Any(need=>CanCover(need&fog,left) && (!currentCenters || CanCoverFromCenters(need&fog,fog,left)))) return proof.Receipt;
            if(currentCenters && proof.RequiredCells.Any(need=>CanCover(need&fog,left))) Interlocked.Increment(ref _currentCenterRejected);
            Interlocked.Increment(ref _completionCoverRejected);
            return new(CrystalOffsetVerdict.Impossible,proof.Receipt.States);
        }
    }
}
