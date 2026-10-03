using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalReachability
{
    // Optional host diagnostics; no observer is installed by production UI.
    internal Action<ImmutableArray<CrystalRewardOption>,CrystalRewardOption?,CrystalGoalSearchResult>? LanguageResultObserved { get; set; }
    internal bool ProfileLanguageCompilation { get; set; }
    private sealed class LanguageQuery(ImmutableArray<CrystalRewardOption> selected,ImmutableArray<CrystalRewardOption> candidates)
    {
        internal readonly ImmutableArray<CrystalRewardOption> Selected=selected,Candidates=candidates;
        internal readonly HashSet<CrystalRewardOption> Attempted=[],Excluded=[];
        internal readonly ConcurrentQueue<(CrystalRewardOption? Target,CrystalGoalSearchResult Result)> Results=new();
        internal Task? Worker;
        internal CancellationTokenSource? Cancel;
        internal bool SelectedAttempted,SelectedImpossible;
        internal double Seconds;
        internal long Work,Pruned,Queries,Positives;
    }
    private readonly Dictionary<string,LanguageQuery> _languageQueries=[];
    private LanguageQuery? _activeLanguage;

    private void StopLanguageWorker()
    {
        _activeLanguage?.Cancel?.Cancel();_activeLanguage=null;
    }

    // One queue per ordered selection: CompileLanguage owns its shared Bound
    // lock, so concurrent candidate tasks would serialize anyway. A UI slice
    // observes work; it never restarts compilation merely because 150 ms ended.
    private CrystalReachabilityUpdate AdvanceLanguage(ImmutableArray<CrystalRewardOption> selected,TimeSpan budget,
        CancellationToken token,Action<ImmutableArray<CrystalRewardOption>>? publishExcluded=null,CrystalRewardOption? focus=null)
    {
        lock(_sync)
        {
            token.ThrowIfCancellationRequested();var watch=Stopwatch.StartNew();
            _table??=new(source,token,includeRerolls:includeRerolls,relicOffsetBound:relicOffsetBound,includeEnchantments:includeEnchantments);
            _offsetBound??=new(source,_table,avoidCurse,rootCardSlots:rootCardSlots,packingCover:packingCover);
            if(focus!=null && (selected.Contains(focus) || !_table.Candidates.Contains(focus))) throw new ArgumentException("CrystalFocusTargetInvalid");
            string selectionKey=SelectionKey(selected),key=focus==null?selectionKey:"focus:"+SelectionKey(selected.Add(focus));
            if(focus==null && _finished.TryGetValue(selectionKey,out var finished)) {StopLanguageWorker();return finished;}
            if(!_languageQueries.TryGetValue(key,out var query))
            {
                ImmutableArray<CrystalRewardOption> candidates=focus==null?ProbeOrder(_table.Candidates.Where(c=>!selected.Contains(c)).ToImmutableArray()).ToImmutableArray():[focus];
                // AnyRelic is hidden in the picker but remains part of the
                // existing candidate domain and must receive a conclusion too.
                if(focus==null && _table.Candidates.Contains(CrystalRewardOption.AnyRelic) && !selected.Contains(CrystalRewardOption.AnyRelic))
                    candidates=candidates.Add(CrystalRewardOption.AnyRelic);
                query=new(selected,candidates);_languageQueries.Add(key,query);
            }
            if(_activeLanguage!=query)
            {
                _activeLanguage?.Cancel?.Cancel();_activeLanguage=query;
            }
            var projection=PeekKnown(selected);
            bool changed=false;
            void Drain()
            {
                foreach(var owner in _languageQueries.Values)
                while(owner.Results.TryDequeue(out var item))
                {
                    var result=item.Result;owner.Queries++;owner.Work+=result.RewardStates+result.GeometryNodes;owner.Pruned+=result.LanguagePruned;
                    var goals=item.Target==null?owner.Selected:owner.Selected.Add(item.Target);
                    if(item.Target==null) owner.SelectedAttempted=true;else owner.Attempted.Add(item.Target);
                    if(result.Verdict==CrystalOffsetVerdict.Impossible)
                    {
                        // Replay-order failures are exact ordered-query facts;
                        // only reward/geometry absence propagates to supersets.
                        if(result.NegativeKind is CrystalLanguageNegativeKind.RewardDomain or CrystalLanguageNegativeKind.Geometry)
                            RememberConflict(goals,"language-"+result.NegativeKind);
                        if(item.Target==null) owner.SelectedImpossible=true;
                        else
                        {
                            owner.Excluded.Add(item.Target);
                            if(!_focusedExcluded.TryGetValue(SelectionKey(owner.Selected),out var negatives)) _focusedExcluded[SelectionKey(owner.Selected)]=negatives=[];
                            negatives.Add(item.Target);
                        }
                        changed=true;
                    }
                    else if(result.Plan is {} plan)
                    {
                        var run=PredictorCrystalExplorer.Replay(source,plan.Steps);var row=new CrystalRewardRoute(plan.Steps,run.Rewards) { EnchantmentTargets=CrystalRewardOption.UsesEnchantments(source.State) };
                        if(includeRerolls) row=PredictorCrystalExplorer.WithRerolls(row,run);
                        if(_paths.Add(string.Join(';',plan.Steps.Select(s=>$"{s.X},{s.Y},{s.Tool}")))) _witnesses.Add(row);
                        owner.Positives++;changed=true;
                    }
                }
                PublishWitnesses();
                if(_projectedRows.GetValueOrDefault(selectionKey)<_witnesses.Count)
                    projection=ProjectKnown(selected,token);
                int priorExcluded=query.Excluded.Count;
                query.Excluded.UnionWith(query.Candidates.Where(c=>ConflictsWith(selected.Add(c))));
                if(_focusedExcluded.TryGetValue(selectionKey,out var priorFocus)) query.Excluded.UnionWith(priorFocus.Where(query.Candidates.Contains));
                changed|=query.Excluded.Count!=priorExcluded;
                if(query.SelectedImpossible) query.Excluded.UnionWith(query.Candidates);
                if((query.SelectedImpossible && projection.SelectedPlan!=null) || query.Excluded.Any(projection.Available.ContainsKey)) throw new InvalidOperationException("CrystalConflictingCandidateProofs");
            }
            Drain();
            if(query.Worker?.IsCompleted==true)
            {
                try {query.Worker.GetAwaiter().GetResult();} catch(OperationCanceledException) { }
                finally {query.Worker=null;query.Cancel?.Dispose();query.Cancel=null;}
                Drain();
            }
            var remaining=query.Candidates.Where(c=>!query.Excluded.Contains(c) && !projection.Available.ContainsKey(c)).ToImmutableArray();
            if(query.Worker==null && budget>TimeSpan.Zero &&
                ((!query.SelectedAttempted && projection.SelectedPlan==null) || remaining.Any(c=>!query.Attempted.Contains(c))))
            {
                var targets=remaining.Where(c=>!query.Attempted.Contains(c)).ToImmutableArray();
                bool needSelected=!query.SelectedAttempted && projection.SelectedPlan==null;
                query.Cancel=CancellationTokenSource.CreateLinkedTokenSource(token);var workerToken=query.Cancel.Token;
                query.Worker=Task.Run(()=>
                {
                    bool Solve(CrystalRewardOption? target)
                    {
                        var goals=target==null?selected:selected.Add(target);
                        var result=PredictorCrystalGoalLanguage.Solve(source,_table,goals,avoidCurse,TimeSpan.FromSeconds(2),
                            token:workerToken,sharedBound:_offsetBound,rootCardSlots:rootCardSlots,profileCompilation:ProfileLanguageCompilation);
                        LanguageResultObserved?.Invoke(selected,target,result);
                        query.Results.Enqueue((target,result));return result.Verdict==CrystalOffsetVerdict.Impossible;
                    }
                    if(needSelected && Solve(null)) return;
                    foreach(var target in targets)
                    {
                        workerToken.ThrowIfCancellationRequested();
                        if(!PeekKnown(selected).Available.ContainsKey(target)) Solve(target);
                    }
                },workerToken);
            }
            if(query.Worker!=null)
            {
                var wait=budget-watch.Elapsed;
                if(wait>TimeSpan.Zero) Task.WaitAny([query.Worker],Math.Max(1,(int)Math.Min(int.MaxValue,wait.TotalMilliseconds)),token);
                Drain();
            }
            else if(budget>watch.Elapsed && remaining.Length>0)
            {
                // A work limit is not a negative. Preserve the original legal
                // search frontier for the first still-unresolved ordered query.
                var target=remaining[0];var fallback=AdvanceFocusedLegacy(selected,target,budget-watch.Elapsed,token);
                projection=fallback.Projection;
                if(fallback.Verdict==CrystalFocusVerdict.Unreachable) changed|=query.Excluded.Add(target);
                query.Work=Math.Max(query.Work,fallback.Progress.Examined);
            }
            else if(budget>watch.Elapsed && projection.SelectedPlan==null && !query.SelectedImpossible)
            {
                var fallback=AdvanceLegacy(selected,budget-watch.Elapsed,token,publishExcluded);
                projection=fallback.Projection;query.Excluded.UnionWith(fallback.ExcludedCandidates);
                if(fallback.Complete && projection.SelectedPlan==null) query.SelectedImpossible=true;
            }
            bool complete=(projection.SelectedPlan!=null || query.SelectedImpossible) &&
                query.Candidates.All(c=>projection.Available.ContainsKey(c) || query.Excluded.Contains(c));
            if(complete) StopLanguageWorker();
            if(complete && projection.SelectedPlan!=null) projection=PublishKnown(selectionKey,projection,selected,reshape:true);
            query.Seconds+=watch.Elapsed.TotalSeconds;
            var excluded=query.Excluded.ToImmutableArray();if(changed && !excluded.IsEmpty) publishExcluded?.Invoke(excluded);
            var update=new CrystalReachabilityUpdate(projection,new(query.Work,query.Pruned,0,(int)query.Queries,query.Seconds),
                complete?CrystalDiscoveryPhase.Complete:CrystalDiscoveryPhase.Exhaustive,complete,
                Workers:1,ProbeSlices:query.Queries,ProbePlans:query.Positives,ExcludedCandidates:excluded);
            if(focus==null && complete) _finished[selectionKey]=update;
            return update;
        }
    }
}
