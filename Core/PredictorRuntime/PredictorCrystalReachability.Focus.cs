using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum CrystalFocusVerdict { Checking, Reachable, Unreachable }
internal sealed record CrystalFocusUpdate(CrystalRewardOption Target, CrystalFocusVerdict Verdict,
    CrystalOptionProjection Projection, CrystalDiscoveryProgress Progress,CrystalOffsetReceipt? RootProof=null,
    string? ProofSource=null);

internal sealed partial class PredictorCrystalReachability
{
    private sealed class FocusQuery(string key, CrystalRewardOption target, PredictorCrystalExplorer[] lanes,CrystalOffsetReceipt rootProof)
    {
        internal readonly string Key=key;
        internal readonly CrystalRewardOption Target=target;
        internal readonly PredictorCrystalExplorer[] Lanes=lanes;
        internal readonly CrystalOffsetReceipt RootProof=rootProof;
        internal double Seconds;
    }
    // Keep the explicitly requested frontier through pauses/reopening. Moving
    // focus to a different question may release it; no timed background eviction.
    private FocusQuery? _focus;
    private readonly Dictionary<string,HashSet<CrystalRewardOption>> _focusedExcluded=[];

    internal CrystalFocusUpdate AdvanceFocused(ImmutableArray<CrystalRewardOption> selected,CrystalRewardOption target,
        TimeSpan budget,CancellationToken token=default)
    {
        if(!goalLanguageProof) return AdvanceFocusedLegacy(selected,target,budget,token);
        var update=AdvanceLanguage(selected,budget,token,focus:target);
        var verdict=update.Projection.Available.ContainsKey(target)?CrystalFocusVerdict.Reachable:
            update.ExcludedCandidates.Contains(target)?CrystalFocusVerdict.Unreachable:CrystalFocusVerdict.Checking;
        return new(target,verdict,update.Projection,update.Progress,ProofSource:verdict==CrystalFocusVerdict.Unreachable?"goal-language-or-continuation":null);
    }
    private CrystalFocusUpdate AdvanceFocusedLegacy(ImmutableArray<CrystalRewardOption> selected,CrystalRewardOption target,
        TimeSpan budget,CancellationToken token=default)
    {
        lock(_sync)
        {
            token.ThrowIfCancellationRequested();
            if(workers is <1 or >4) throw new ArgumentOutOfRangeException(nameof(workers));
            string key=SelectionKey(selected);
            var projection=PeekKnown(selected);
            if(ConflictsWith(selected.Add(target)))
            {
                if(projection.Available.ContainsKey(target)) throw new InvalidOperationException("CrystalConflictingCandidateProofs");
                return new(target,CrystalFocusVerdict.Unreachable,projection,default,ProofSource:"learned-conflict");
            }
            if(projection.Available.ContainsKey(target)) return new(target,CrystalFocusVerdict.Reachable,projection,default);
            if(_focusedExcluded.TryGetValue(key,out var negatives) && negatives.Contains(target))
                return new(target,CrystalFocusVerdict.Unreachable,projection,default);
            var watch=Stopwatch.StartNew();
            _table??=new(source,token,includeRerolls:includeRerolls,relicOffsetBound:relicOffsetBound,includeEnchantments:includeEnchantments);
            _offsetBound??=new(source,_table,avoidCurse,rootCardSlots:rootCardSlots,packingCover:packingCover);
            if(_focus is not { } focus || focus.Key!=key || focus.Target!=target)
            {
                if(selected.Contains(target) || !_table.Candidates.Contains(target)) throw new ArgumentException("CrystalFocusTargetInvalid");
                var goals=selected.Add(target);
                var rootProof=_offsetBound.Check(goals);
                if(rootProof.Verdict==CrystalOffsetVerdict.Impossible)
                {
                    // A complete relaxed root denial already answers the query;
                    // no click frontiers or failed ordered-take inference needed.
                    string origin=rootProof.RootCardSlotsRejected?"root-card-slots":"root-offset";
                    RememberConflict(rootProof.RootCardSlotsRejected
                        ?goals.Where(g=>g.Kind==PredictorRewardKind.Card).ToImmutableArray():goals,origin);
                    _focus=null;
                    return new(target,CrystalFocusVerdict.Unreachable,projection,
                        new(0,0,0,0,watch.Elapsed.TotalSeconds),rootProof,origin);
                }
                _focus=focus=new(key,target,Enumerable.Range(0,workers).Select(i=>new PredictorCrystalExplorer(source,avoidCurse,
                    reachability:_table,selected:goals,potionScenarioBound:true,potionScenarioLimit:potionScenarioLimit,
                    targetDirected:i==0,verifySelection:VerifyCached,stopAfterPlan:true,offsetBound:_offsetBound,
                    rootPartition:i,rootPartitions:workers,directedProof:true,reverseProof:true)).ToArray(),rootProof);
            }
            var batches=new ImmutableArray<CrystalRewardRoute>[focus.Lanes.Length];
            // No broad discovery, unrelated candidate probes or gold refinement
            // runs here. Every worker answers this same selected+target question.
            Parallel.For(0,focus.Lanes.Length,new ParallelOptions { MaxDegreeOfParallelism=workers },i=>
                batches[i]=focus.Lanes[i].Advance(budget,token));
            foreach(var row in batches.SelectMany(b=>b))
                if(_paths.Add(string.Join(';',row.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}")))) _witnesses.Add(row);
            PublishWitnesses();
            var plan=focus.Lanes.Select(l=>l.QueryProjection.SelectedPlan).FirstOrDefault(p=>p!=null);
            var verdict=plan!=null?CrystalFocusVerdict.Reachable:
                focus.Lanes.All(l=>l.Complete)?CrystalFocusVerdict.Unreachable:CrystalFocusVerdict.Checking;
            if(plan!=null)
            {
                // Do not replace the existing selected-only plan/gold value.
                var positive=new CrystalOptionProjection(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty.Add(target,plan),null,0);
                projection=_published.AddOrUpdate(key,positive,(_,prior)=>prior with { Available=prior.Available.SetItem(target,plan) });
            }
            if(verdict==CrystalFocusVerdict.Unreachable)
            {
                if(!_focusedExcluded.TryGetValue(key,out negatives)) _focusedExcluded[key]=negatives=[];
                negatives.Add(target);
                // Exhaustive take failure is valid for this ordered query only.
                // Root/offset certificates above retain their stronger subset scope.
            }
            focus.Seconds+=watch.Elapsed.TotalSeconds;
            var progress=new CrystalDiscoveryProgress(focus.Lanes.Sum(l=>l.Progress.Examined),focus.Lanes.Sum(l=>l.Progress.Pruned),
                focus.Lanes.Sum(l=>l.Progress.Merged),focus.Lanes.Sum(l=>l.Progress.Orders),focus.Seconds);
            return new(target,verdict,projection,progress,focus.RootProof,
                verdict==CrystalFocusVerdict.Unreachable?(focus.RootProof.Verdict==CrystalOffsetVerdict.Impossible?"root-offset":"exhaustive-search"):null);
        }
    }
}
