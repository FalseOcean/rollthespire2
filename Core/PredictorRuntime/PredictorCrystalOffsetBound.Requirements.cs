using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record CrystalRewardRequirement(ImmutableArray<int> Potions,ImmutableArray<int> Core);
internal sealed record CrystalRequirementProof(CrystalOffsetReceipt Receipt,ImmutableArray<CrystalRewardRequirement> Alternatives);

internal sealed partial class PredictorCrystalOffsetBound
{
    internal CrystalOffsetReceipt VisitTerminalWords(ImmutableArray<CrystalRewardOption> goals,
        Func<CrystalRewardRequirement,bool> visitor,Func<bool> keepGoing,int stateLimit)
    {
        if(!_admitted || goals.Length>16) return new(CrystalOffsetVerdict.Incomplete,0);
        var ordered=goals.Distinct().OrderBy(g=>g.Kind).ThenBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
        lock(_sync) return SolveRerolls(ordered,stateLimit,terminalVisitor:visitor,keepGoing:keepGoing);
    }
    internal bool MayCover(UInt128 need,int left)=>CanCover(need,left);

    private readonly ConcurrentDictionary<string,Lazy<CrystalRequirementProof>> _requirements=[];
    private long _requirementTicks,_requirementBuilds,_requirementIncomplete,_requirementWords;
    internal double RequirementSeconds=>Interlocked.Read(ref _requirementTicks)/(double)Stopwatch.Frequency;
    internal long RequirementBuilds=>Interlocked.Read(ref _requirementBuilds);
    internal long RequirementIncomplete=>Interlocked.Read(ref _requirementIncomplete);
    internal long RequirementWords=>Interlocked.Read(ref _requirementWords);

    // Compile necessary future phase-prefix alternatives, not click paths.
    // Different physical orders must not disappear through reward-state merging.
    internal CrystalRequirementProof Requirements(ImmutableArray<CrystalRewardOption> goals,
        ImmutableArray<int> revealed,UInt128 fog,int left,int stateLimit=100_000)
    {
        CrystalRequirementProof Unknown()=>new(new(CrystalOffsetVerdict.Incomplete,0),[]);
        if(!_admitted || !_table.IncludesRerolls || goals.Length>16) return Unknown();
        var ordered=goals.Distinct().OrderBy(g=>g.Kind).ThenBy(g=>g.Key.Serialized,StringComparer.Ordinal).ThenBy(g=>g.UpgradeLevel).ThenBy(g=>g.CacheKey,StringComparer.Ordinal).ToImmutableArray();
        int potions=0,core=0;
        for(int i=0;i<_potions.Length;i++) if(!revealed.Contains(_potions[i].Item) && CanCover(_potions[i].Need&fog,left)) potions|=1<<i;
        for(int i=0;i<_core.Length;i++) if(!revealed.Contains(_core[i].Item) && CanCover(_core[i].Need&fog,left)) core|=1<<i;
        string prefix=string.Join(',',revealed.Where(i=>_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)))+"/"+
            string.Join(',',revealed.Where(i=>!_source.Items[i].Kind.StartsWith("POTION_",StringComparison.Ordinal)));
        string key=$"{stateLimit}:{potions}:{core}:{prefix}:"+string.Join('|',ordered.Select(g=>g.CacheKey));
        if(_requirements.TryGetValue(key,out var known)) return known.Value;
        if(_requirements.Count>=4096) return Unknown();
        return _requirements.GetOrAdd(key,_=>new Lazy<CrystalRequirementProof>(()=>{
            long start=Stopwatch.GetTimestamp();var words=new List<CrystalRewardRequirement>();CrystalOffsetReceipt receipt;
            lock(_sync) receipt=SolveRerolls(ordered,stateLimit,revealed,potions,core,requirements:words);
            Interlocked.Add(ref _requirementTicks,Stopwatch.GetTimestamp()-start);
            Interlocked.Increment(ref _requirementBuilds);Interlocked.Add(ref _requirementWords,words.Count);
            if(receipt.Verdict==CrystalOffsetVerdict.Incomplete) Interlocked.Increment(ref _requirementIncomplete);
            // A partial collection cannot exclude any geometry.
            return new(receipt,receipt.Verdict==CrystalOffsetVerdict.Incomplete?[]:words.ToImmutableArray());
        },LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}
