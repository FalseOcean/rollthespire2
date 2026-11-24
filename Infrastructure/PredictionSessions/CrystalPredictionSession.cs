using System.Collections.Concurrent;
using System.Collections.Immutable;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Infrastructure.PredictionSessions;

// Room-owned detached calculation. All task publications use a revision-owned
// queue, consumed explicitly by the caller; no worker retains a Godot control.
internal sealed class CrystalPredictionSession : IDisposable
{
    private CrystalSphereLiveSnapshot? _snapshot;
    internal int Branch { get; private set; }
    internal bool AvoidCurse { get; private set; }
    internal bool IncludeDriftwood { get; private set; }
    internal bool IncludeEnchantments { get; private set; }
    internal PredictorCrystalSnapshot Source => _snapshot!.Branches[Branch].Snapshot;
    private PredictorCrystalReachability? _reachability;
    private readonly List<CrystalRewardRoute> _rows = [];
    private ImmutableArray<CrystalRewardOption> _selected = [];
    private readonly HashSet<CrystalRewardOption> _pendingOptions=[];
    private CrystalOptionProjection _projection = new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
    private CancellationTokenSource? _cancel, _projectCancel;
    private Task? _worker;
    private double _projectionDelay;
    private bool _running, _complete, _candidatesComplete, _dirty, _projecting, _switching;
    private bool _stale, _resumeOnOpen, _startWhenUpdatesDrained;
    private CrystalRewardOption? _focusTarget;
    private CrystalFocusVerdict _focusVerdict;
    private string? _errorKey;
    private Exception? _fault;
    internal enum DiscoveryStop { TimeLimit, UserPaused, Exhausted, Failed }
    private DiscoveryStop? _stopReason;
    private CrystalDiscoveryPhase _phase;
    private CrystalDiscoveryProgress _progress;
    private CrystalDiscoveryProgress _goldProgress;
    internal sealed record Update(ImmutableArray<CrystalRewardRoute> Rows, DiscoveryStop? Stop=null,
        CrystalDiscoveryPhase? Phase=null, Exception? Error=null, CrystalDiscoveryProgress? Progress=null,
        CrystalOptionProjection? Projection=null,ImmutableArray<CrystalRewardOption> ExcludedCandidates=default,CrystalFocusUpdate? Focus=null,
        bool? CandidatesComplete=null,CrystalDiscoveryProgress? GoldProgress=null,bool ProjectionFinished=false);
    private ConcurrentQueue<Update> _updates = new();
    internal CrystalSphereLiveSnapshot? Snapshot => _snapshot;
    internal PredictorCrystalReachability? Reachability => _reachability;
    internal IReadOnlyList<CrystalRewardRoute> Rows => _rows;
    internal ImmutableArray<CrystalRewardOption> Selected => _selected;
    internal IReadOnlySet<CrystalRewardOption> PendingOptions => _pendingOptions;
    internal CrystalOptionProjection Projection => _projection;
    internal bool Running => _running;
    internal bool Complete => _complete;
    internal bool CandidatesComplete => _candidatesComplete;
    internal bool Dirty => _dirty;
    internal bool Projecting => _projecting;
    internal bool Switching => _switching;
    internal bool Stale => _stale;
    internal bool ResumeOnOpen => _resumeOnOpen;
    internal bool StartWhenUpdatesDrained => _startWhenUpdatesDrained;
    internal CrystalRewardOption? FocusTarget => _focusTarget;
    internal CrystalFocusVerdict FocusVerdict => _focusVerdict;
    internal string? ErrorKey => _errorKey;
    internal Exception? Fault => _fault;
    internal DiscoveryStop? StopReason => _stopReason;
    internal CrystalDiscoveryPhase Phase => _phase;
    internal CrystalDiscoveryProgress Progress => _progress;
    internal CrystalDiscoveryProgress GoldProgress => _goldProgress;
    internal Task? Worker => _worker;
    internal ConcurrentQueue<Update> Updates => _updates;
    internal sealed record Checkpoint(CrystalSphereLiveSnapshot Snapshot,int Branch,bool AvoidCurse,bool IncludeDriftwood,bool IncludeEnchantments,
        PredictorCrystalReachability? Reachability,ImmutableArray<CrystalRewardRoute> Rows,
        ImmutableArray<CrystalRewardOption> Selected,CrystalOptionProjection Projection,
        ImmutableArray<CrystalRewardOption> Pending,CrystalDiscoveryPhase Phase,CrystalDiscoveryProgress Progress,
        bool Complete,bool Stale,bool Resume,bool Switching,DiscoveryStop? StopReason,string? ErrorKey,Exception? Fault,Task? Worker,ConcurrentQueue<Update> Updates,
        CrystalRewardOption? FocusTarget,CrystalFocusVerdict FocusVerdict,bool CandidatesComplete,CrystalDiscoveryProgress GoldProgress);
    internal Checkpoint? SuspendSession()
    {
        if(_snapshot==null) return null;
        bool resume=(_resumeOnOpen || _startWhenUpdatesDrained || (_running && _cancel?.IsCancellationRequested!=true)) && !_complete && !_stale;
        _cancel?.Cancel();_projectCancel?.Cancel();
        // The session carries the unconsumed queue as well as its applied state.
        // Closing must not synchronously merge an arbitrary worker backlog.
        _startWhenUpdatesDrained=false;
        return new(_snapshot,Branch,AvoidCurse,IncludeDriftwood,IncludeEnchantments,_reachability,_rows.ToImmutableArray(),
            _selected,_projection,_pendingOptions.ToImmutableArray(),_phase,_progress,_complete,_stale,
            resume && !_complete && _errorKey==null && (_focusTarget==null || _focusVerdict==CrystalFocusVerdict.Checking),
            _switching,_stopReason,_errorKey,_fault,_worker,_updates,_focusTarget,_focusVerdict,_candidatesComplete,_goldProgress);
    }
    internal void RestoreSession(Checkpoint session)
    {
        ResetWork();_snapshot=session.Snapshot;
        Branch=session.Branch;AvoidCurse=session.AvoidCurse;
        IncludeDriftwood=session.IncludeDriftwood;IncludeEnchantments=session.IncludeEnchantments;
        _reachability=session.Reachability;_rows.AddRange(session.Rows);_selected=session.Selected;_projection=session.Projection;
        _pendingOptions.UnionWith(session.Pending);_phase=session.Phase;_progress=session.Progress;
        _complete=session.Complete;_stale=session.Stale;_resumeOnOpen=session.Resume;_switching=session.Switching;
        _candidatesComplete=session.CandidatesComplete;_goldProgress=session.GoldProgress;
        _stopReason=session.StopReason;_errorKey=session.ErrorKey;_fault=session.Fault;
        _focusTarget=session.FocusTarget;_focusVerdict=session.FocusVerdict;
        _worker=session.Worker;_updates=session.Updates;_running=_worker is { IsCompleted:false };
        NormalizePendingOptions();
    }
    private void ResetWork()
    {
        _cancel?.Cancel();_cancel?.Dispose();_cancel=null;
        _projectCancel?.Cancel();_projectCancel?.Dispose();_projectCancel=null;
        _worker=null;_updates=new();_rows.Clear();_selected=[];_pendingOptions.Clear();
        _projection=new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
        _reachability=null;_running=_complete=_candidatesComplete=_dirty=_projecting=_switching=false;_errorKey=null;_fault=null;_stopReason=null;_phase=CrystalDiscoveryPhase.FindingPlans;_progress=_goldProgress=default;
        _stale=_resumeOnOpen=_startWhenUpdatesDrained=false;
        _focusTarget=null;_focusVerdict=CrystalFocusVerdict.Checking;
    }
    internal void Begin(CrystalSphereLiveSnapshot snapshot, int branch, bool avoidCurse,
        bool includeDriftwood, bool includeEnchantments, bool start=true)
    {
        ResetWork();_snapshot=snapshot;Branch=branch;AvoidCurse=avoidCurse;
        IncludeDriftwood=includeDriftwood;IncludeEnchantments=includeEnchantments;
        _reachability=new(Source,AvoidCurse,potionScenarioBound:true,
            workers:Source.Remaining>3?Math.Clamp(System.Environment.ProcessorCount-1,1,4):1,
            targetDirected:Source.Remaining>3,includeRerolls:IncludeDriftwood,goalLanguageProof:true,
            includeEnchantments:IncludeEnchantments)
        {
            GoldFailureObserved=static (exception,scope)=>RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(
                exception,"CrystalGoldOptimizationFailed",nameof(CrystalPredictionSession),
                "GoldThresholdWorker:"+scope,"runtime.issue.crystal_discovery_failed"),
            LanguageFailureObserved=static (exception,scope)=>RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(
                exception,"CrystalLanguageWorkerFailed",nameof(CrystalPredictionSession),
                "LanguageWorker","runtime.issue.crystal_discovery_failed",correlationId:scope)
        };
        if(start) StartDiscovery();
    }
    internal void StartDiscovery()
    {
        // A terminal receipt must be applied before reusing this query queue,
        // through the same bounded consumer used during ordinary processing.
        if(_worker is not { IsCompleted:false } && !_updates.IsEmpty)
        { _startWhenUpdatesDrained=true;return; }
        _startWhenUpdatesDrained=false;
        if(_reachability==null || _snapshot==null || _stale || _complete || _worker is { IsCompleted:false }) return;
        if(_focusTarget!=null && _focusVerdict!=CrystalFocusVerdict.Checking) _focusTarget=null;
        var focusTarget=_focusTarget;
        var reachability=_reachability;var selected=_selected;var queue=_updates;bool reproject=_switching,candidatesInitiallyComplete=_candidatesComplete;
        _cancel?.Dispose();_cancel=new();var token=_cancel.Token;_running=true;_errorKey=null;_fault=null;_stopReason=null;
        _worker=Task.Run(()=>{
            try
            {
                if(focusTarget!=null)
                {
                    CrystalFocusUpdate focus;
                    do
                    {
                        focus=reachability.AdvanceFocused(selected,focusTarget,TimeSpan.FromMilliseconds(150),token);
                        queue.Enqueue(new([],Progress:focus.Progress,Projection:focus.Projection,Focus:focus));
                    } while(!token.IsCancellationRequested && focus.Verdict==CrystalFocusVerdict.Checking);
                    queue.Enqueue(new([],DiscoveryStop.UserPaused,Focus:focus));
                    return;
                }
                if(reproject)
                {
                    reachability.ProjectKnown(selected,token,known=>queue.Enqueue(new([],Projection:known)));
                    token.ThrowIfCancellationRequested();
                }
                CrystalReachabilityUpdate? update=null;
                var gold=reachability.PeekKnown(selected);
                bool candidatesComplete=candidatesInitiallyComplete;
                while(!token.IsCancellationRequested && (!candidatesComplete || !gold.GoldComplete))
                {
                    if(!gold.GoldComplete)
                    {
                        gold=reachability.AdvanceGold(selected,TimeSpan.FromMilliseconds(75),token);
                        queue.Enqueue(new([],Projection:gold,GoldProgress:reachability.GoldProgress));
                    }
                    if(!candidatesComplete)
                    {
                        update=reachability.Advance(selected,TimeSpan.FromMilliseconds(150),token,
                            excluded=>queue.Enqueue(new([],ExcludedCandidates:excluded)));
                        candidatesComplete=update.Complete;
                        queue.Enqueue(new([],Phase:update.Phase,Progress:update.Progress,Projection:update.Projection,
                            ExcludedCandidates:update.ExcludedCandidates,CandidatesComplete:candidatesComplete));
                    }
                }
                queue.Enqueue(new([],candidatesComplete && gold.GoldComplete?DiscoveryStop.Exhausted:DiscoveryStop.UserPaused,
                    update?.Phase??CrystalDiscoveryPhase.Complete,CandidatesComplete:candidatesComplete));
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { queue.Enqueue(new([],DiscoveryStop.UserPaused)); }
            catch(Exception ex)
            {
                // A retargeted selection or closed page no longer consumes this queue.
                // Preserve the failure where it occurs, independently of its UI lifetime.
                RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                    "CrystalDiscoveryFailed", nameof(CrystalPredictionSession), "DiscoveryWorker", "runtime.issue.crystal_discovery_failed");
                queue.Enqueue(new([],DiscoveryStop.Failed,Error:ex));
            }
        });
    }
    internal void ApplyRows(ImmutableArray<CrystalRewardRoute> rows, bool complete=false)
    { _rows.AddRange(rows);_reachability?.AddWitnesses(rows);_candidatesComplete|=complete;_complete=_candidatesComplete && _projection.GoldComplete;_dirty=true; }
    internal Task RebuildProjection()
    {
        if(_snapshot==null || _projecting) return Task.CompletedTask;
        var source=Source;var rows=_rows.ToImmutableArray();var selected=_selected;
        bool includeEnchantments=IncludeEnchantments;
        _projectCancel?.Dispose();_projectCancel=new();var token=_projectCancel.Token;
        var queue=_updates;_projecting=true;_dirty=false;
        return Task.Run(()=>
        {
            try
            {
                var result=PredictorCrystalExplorer.Project(source,rows,selected,token,includeEnchantments:includeEnchantments);
                token.ThrowIfCancellationRequested();
                queue.Enqueue(new([],Projection:result,ProjectionFinished:true));
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested)
            { queue.Enqueue(new([],ProjectionFinished:true)); }
            catch(Exception ex)
            {
                RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                    "CrystalProjectionFailed",nameof(CrystalPredictionSession),"ProjectionWorker","runtime.issue.crystal_projection_failed");
                queue.Enqueue(new([],Error:ex,ProjectionFinished:true));
            }
        });
    }
    internal void SelectOption(CrystalRewardOption option)
    {
        if(_stale) return;
        PredictorCrystalSolution? selectedProof=null;
        if(_selected.Contains(option)) _selected=_selected.Remove(option);
        else if(_projection.Available.TryGetValue(option,out selectedProof)) _selected=_selected.Add(option);
        else { if(_pendingOptions.Contains(option)) FocusOption(option);return; }
        _focusTarget=null;
        // Old workers keep their old queue. Each selection owns an independent
        // frontier, while the frozen-board session reuses tables and witnesses.
        _cancel?.Cancel();_worker=null;_updates=new();_running=_complete=_candidatesComplete=_startWhenUpdatesDrained=false;
        _stopReason=null;_errorKey=null;_fault=null;_progress=_goldProgress=default;_phase=CrystalDiscoveryPhase.FindingPlans;
        _projectCancel?.Cancel();_projecting=false;
        // This option already carries a verified plan for the enlarged selection.
        // It remains usable while compatible additions are reprojected off-thread.
        _switching=true;
        var knownOptions=_projection.Available.Keys.Concat(_pendingOptions).Where(o=>!_selected.Contains(o)).ToImmutableArray();
        _pendingOptions.Clear();_pendingOptions.UnionWith(knownOptions);
        if(_reachability!=null)
            _pendingOptions.ExceptWith(_reachability.RootCardExclusions(_selected,knownOptions));
        var cached=_reachability?.PeekKnown(_selected);
        _projection=cached==null?new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,selectedProof,0)
            :cached with { SelectedPlan=cached.SelectedPlan??selectedProof };
        _dirty=_rows.Count>0;_projectionDelay=0;
        NormalizePendingOptions();
        if(_dirty) _=RebuildProjection();
        StartDiscovery();
    }
    internal void SwitchSearchQueue()
    {
        _cancel?.Cancel();_worker=null;_updates=new();_running=_complete=_startWhenUpdatesDrained=false;
        _stopReason=null;_errorKey=null;_fault=null;_progress=default;
        _projectCancel?.Cancel();_projecting=_dirty=_switching=false;
    }
    private void FocusOption(CrystalRewardOption option)
    {
        SwitchSearchQueue();_focusTarget=option;_focusVerdict=CrystalFocusVerdict.Checking;
        RuntimeLog.Info($"crystalFocus=start;seed={Source.Context.Seed};remaining={Source.Remaining};avoidCurse={AvoidCurse};driftwood={IncludeDriftwood};selected={string.Join(',',_selected)};target={option}");
        StartDiscovery();
    }
    internal void ApplyUpdate(Update update)
    {
        if(update.ProjectionFinished) _projecting=false;
        if(update.Focus is { } focus)
        {
            if(focus.Target!=_focusTarget) return;
            if(focus.Verdict!=_focusVerdict && focus.Verdict!=CrystalFocusVerdict.Checking)
                RuntimeLog.Info($"crystalFocus={focus.Verdict};target={focus.Target};seconds={focus.Progress.Seconds:F3};examined={focus.Progress.Examined};rootProof={focus.RootProof};proofSource={focus.ProofSource}");
            _focusVerdict=focus.Verdict;
            if(focus.Verdict!=CrystalFocusVerdict.Checking) _resumeOnOpen=false;
            if(focus.Verdict==CrystalFocusVerdict.Unreachable) _pendingOptions.Remove(focus.Target);
        }
        // Each selection owns a fresh queue. Only explicit same-query negative
        // proofs remove grey candidates; timeout/absence alone removes nothing.
        if(!update.ExcludedCandidates.IsDefaultOrEmpty) _pendingOptions.ExceptWith(update.ExcludedCandidates);
        if(update.Rows.Length>0) ApplyRows(update.Rows);
        if(update.Projection is { } projection)
        {
            _projectCancel?.Cancel();_projecting=false;_dirty=false;_switching=false;
            // Same-revision partial publications may arrive before the first
            // slice. Never erase already verified options or the clicked plan.
            _projection=MergeProjection(_projection,projection);
        }
        // Projection/gold receipts do not report a candidate phase transition.
        // Keep the last actual candidate stage instead of flashing FindingPlans.
        if(update.Phase is { } phase) _phase=phase;
        if(update.Progress is { } progress) _progress=progress;
        if(update.GoldProgress is { } goldProgress) _goldProgress=goldProgress;
        if(update.CandidatesComplete is { } candidatesComplete) _candidatesComplete=candidatesComplete;
        if(update.Stop is { } reason)
        { _running=false;_stopReason=reason;_complete=reason==DiscoveryStop.Exhausted && _candidatesComplete && _projection.GoldComplete; }
        if(update.Error!=null) { _errorKey=update.ProjectionFinished?"projection_failed":"discovery_failed";_fault=update.Error; }
        NormalizePendingOptions();
    }

    private static CrystalOptionProjection MergeProjection(CrystalOptionProjection prior,CrystalOptionProjection incoming)
    {
        var best=incoming.SelectedPlan!=null && (prior.SelectedPlan==null || incoming.Gold>prior.Gold)?incoming:prior;
        int? upper=prior.GoldUpperBound is { } previous && incoming.GoldUpperBound is { } next?Math.Min(previous,next):
            incoming.GoldUpperBound??prior.GoldUpperBound;
        return new(prior.Available.SetItems(incoming.Available),best.SelectedPlan,best.Gold)
        {
            GoldComplete=prior.GoldComplete || incoming.GoldComplete,
            GoldUpperBound=upper,
            SacrificeRelics=prior.SacrificeRelics.Union(incoming.SacrificeRelics)
        };
    }
    private void NormalizePendingOptions()
    {
        _pendingOptions.ExceptWith(_projection.Available.Keys);_pendingOptions.ExceptWith(_selected);
        if(_candidatesComplete || _snapshot==null) _pendingOptions.Clear();
    }

    internal void Pause() { _resumeOnOpen=_startWhenUpdatesDrained=false;_cancel?.Cancel(); }
    internal void ReturnToCandidates() { SwitchSearchQueue();_focusTarget=null; }
    internal void Suspend()
    {
        _resumeOnOpen=(_resumeOnOpen || _startWhenUpdatesDrained || (_running && _cancel?.IsCancellationRequested!=true)) &&
            !_complete && !_stale && _errorKey==null && (_focusTarget==null || _focusVerdict==CrystalFocusVerdict.Checking);
        _cancel?.Cancel();_projectCancel?.Cancel();_startWhenUpdatesDrained=false;
    }
    internal void Invalidate()
    { _stale=true;_resumeOnOpen=false;_cancel?.Cancel();_projectCancel?.Cancel(); }
    internal void AcceptEnteredBoard(CrystalSphereLiveSnapshot snapshot) { _snapshot=snapshot;Branch=0; }
    internal bool Advance(double delta, bool mayResume, int maximumUpdates, double milliseconds)
    {
        bool changed=false;long started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int count=0;count<maximumUpdates && _updates.TryDequeue(out var update);count++)
        {
            ApplyUpdate(update);changed=true;
            if(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds>=milliseconds) break;
        }
        if(_updates.IsEmpty && _worker is not { IsCompleted:false } &&
            (_startWhenUpdatesDrained || (_resumeOnOpen && mayResume)))
        {
            bool explicitStart=_startWhenUpdatesDrained;_resumeOnOpen=false;_startWhenUpdatesDrained=false;
            if(explicitStart || _focusTarget==null || _focusVerdict==CrystalFocusVerdict.Checking) StartDiscovery();
            changed=true;
        }
        _projectionDelay-=delta;
        if(!_stale && _dirty && !_projecting && _projectionDelay<=0)
        { _projectionDelay=.5;_=RebuildProjection();changed=true; }
        return changed;
    }
    public void Dispose() { ResetWork();_snapshot=null; }
}
