using System.Collections.Concurrent;
using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.Shell;

// Owns frozen search/session state, pure workers and explicit guidance admission.
internal sealed partial class CrystalSphereAssistantPanel
{
    private CrystalSphereLiveSnapshot? _snapshot;
    private PredictorCrystalReachability? _reachability;
    private readonly List<CrystalRewardRoute> _rows = [];
    private ImmutableArray<CrystalRewardOption> _selected = [];
    private readonly HashSet<CrystalRewardOption> _pendingOptions=[];
    private CrystalOptionProjection _projection = new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
    private CancellationTokenSource? _cancel, _projectCancel;
    private Task? _worker;
    private long _generation, _selectionRevision;
    private double _poll, _projectionDelay;
    private bool _exited, _running, _complete, _candidatesComplete, _dirty, _projecting, _switching;
    private bool _stale, _resumeOnOpen, _startWhenUpdatesDrained;
    private CrystalRewardOption? _focusTarget;
    private CrystalFocusVerdict _focusVerdict;
    private string _emptyStatusKey = "refresh_first";
    private string? _errorKey;
    private Exception? _fault;
    internal enum DiscoveryStop { TimeLimit, UserPaused, Exhausted, Failed }
    private DiscoveryStop? _stopReason;
    private CrystalDiscoveryPhase _phase;
    private CrystalDiscoveryProgress _progress;
    private CrystalDiscoveryProgress _goldProgress;
    internal sealed record Update(ImmutableArray<CrystalRewardRoute> Rows, DiscoveryStop? Stop=null,
        CrystalDiscoveryPhase Phase=CrystalDiscoveryPhase.FindingPlans, Exception? Error=null, CrystalDiscoveryProgress? Progress=null,
        CrystalOptionProjection? Projection=null,ImmutableArray<CrystalRewardOption> ExcludedCandidates=default,CrystalFocusUpdate? Focus=null,
        bool? CandidatesComplete=null,CrystalDiscoveryProgress? GoldProgress=null);
    private ConcurrentQueue<Update> _updates = new();
    // Explicit close cancels work and freezes this event's query. The retained
    // room UI restores it before checking the actual scene again on reopening.
    internal sealed record Session(CrystalSphereLiveSnapshot Snapshot,int Branch,bool AvoidCurse,bool IncludeDriftwood,bool IncludeEnchantments,
        PredictorCrystalReachability? Reachability,ImmutableArray<CrystalRewardRoute> Rows,
        ImmutableArray<CrystalRewardOption> Selected,CrystalOptionProjection Projection,
        ImmutableArray<CrystalRewardOption> Pending,CrystalDiscoveryPhase Phase,CrystalDiscoveryProgress Progress,
        bool Complete,bool Stale,bool Resume,bool Switching,DiscoveryStop? StopReason,string? ErrorKey,Exception? Fault,Task? Worker,ConcurrentQueue<Update> Updates,
        CrystalRewardOption? FocusTarget,CrystalFocusVerdict FocusVerdict,bool CandidatesComplete,CrystalDiscoveryProgress GoldProgress);
    internal Session? SuspendSession()
    {
        if(_snapshot==null) return null;
        bool resume=(_startWhenUpdatesDrained || (_running && _cancel?.IsCancellationRequested!=true)) && !_complete && !_stale;
        _cancel?.Cancel();_projectCancel?.Cancel();
        // The session carries the unconsumed queue as well as its applied state.
        // Closing must not synchronously merge an arbitrary worker backlog.
        _startWhenUpdatesDrained=false;
        return new(_snapshot,_branch.Selected,_avoidCurse.ButtonPressed,_includeDriftwood.ButtonPressed,_includeEnchantments.ButtonPressed,_reachability,_rows.ToImmutableArray(),
            _selected,_projection,_pendingOptions.ToImmutableArray(),_phase,_progress,_complete,_stale,
            resume && !_complete && _errorKey==null && (_focusTarget==null || _focusVerdict==CrystalFocusVerdict.Checking),
            _switching,_stopReason,_errorKey,_fault,_worker,_updates,_focusTarget,_focusVerdict,_candidatesComplete,_goldProgress);
    }
    internal void RestoreSession(Session session)
    {
        ResetWork();_snapshot=session.Snapshot;
        _branch.Clear();foreach(var branch in _snapshot.Branches)
            _branch.AddItem(T(branch.Mode)+(branch.Mode=="Remaining"?" · "+string.Format(T("remaining"),branch.Snapshot.Remaining):""));
        _branch.Select(session.Branch);_avoidCurse.SetPressedNoSignal(session.AvoidCurse);
        _includeDriftwood.SetPressedNoSignal(session.IncludeDriftwood);
        _includeEnchantments.SetPressedNoSignal(session.IncludeEnchantments);
        _reachability=session.Reachability;_rows.AddRange(session.Rows);_selected=session.Selected;_projection=session.Projection;
        _pendingOptions.UnionWith(session.Pending);_phase=session.Phase;_progress=session.Progress;
        _complete=session.Complete;_stale=session.Stale;_resumeOnOpen=session.Resume;_switching=session.Switching;
        _candidatesComplete=session.CandidatesComplete;_goldProgress=session.GoldProgress;
        _stopReason=session.StopReason;_errorKey=session.ErrorKey;_fault=session.Fault;
        _focusTarget=session.FocusTarget;_focusVerdict=session.FocusVerdict;
        _worker=session.Worker;_updates=session.Updates;_running=_worker is { IsCompleted:false };
        Render();
    }
    internal void OpenSession()
    {
        if(_snapshot==null) { Refresh();return; }
        _presentationSceneValidated=EnsureCurrentScene() || _stale;
        Render();
    }
    private void ResetWork()
    {
        InvalidatePresentation();
        _generation++;_selectionRevision++;_cancel?.Cancel();_cancel?.Dispose();_cancel=null;
        _projectCancel?.Cancel();_projectCancel?.Dispose();_projectCancel=null;
        _worker=null;_updates=new();_rows.Clear();_selected=[];_pendingOptions.Clear();
        _projection=new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
        _reachability=null;_running=_complete=_candidatesComplete=_dirty=_projecting=_switching=false;_errorKey=null;_fault=null;_stopReason=null;_phase=CrystalDiscoveryPhase.FindingPlans;_progress=_goldProgress=default;
        _stale=_resumeOnOpen=_startWhenUpdatesDrained=false;
        _presentationSceneValidated=false;
        _focusTarget=null;_focusVerdict=CrystalFocusVerdict.Checking;
    }
    internal void Refresh()
    {
        try { ShowSnapshot(CrystalSphereLiveCapture.Capture(_run,GetTree().Root,_runtime.Detection.DisplayVersion)); }
        catch(InvalidOperationException ex)
        {
            bool expected=ex.Message is "CrystalLocalPlayerMissing" or "CrystalPlayerMismatch" or "CrystalNotCurrentScene" or "CrystalSceneBusy" or "CrystalSceneFinished" or "CrystalSnapshotChanged";
            _emptyStatusKey=expected?ex.Message:"unavailable";_fault=ex;if(_snapshot!=null) Invalidate();
            if(!expected) RuntimeLog.WarnException("crystalCaptureUnavailable=true",ex);Render();
        }
        catch(Exception ex) { _emptyStatusKey="unavailable";_fault=ex;if(_snapshot!=null) Invalidate();RuntimeLog.WarnException("crystalCaptureFailed=true",ex);Render(); }
    }
    internal void ShowSnapshot(CrystalSphereLiveSnapshot snapshot, bool startDiscovery=true)
    {
        ResetWork();_snapshot=snapshot;_branch.Clear();
        foreach(var branch in snapshot.Branches) _branch.AddItem(T(branch.Mode)+(branch.Mode=="Remaining"?" · "+string.Format(T("remaining"),branch.Snapshot.Remaining):""));
        _branch.Select(0);BeginBranch(startDiscovery);
    }
    private void BeginBranch() => BeginBranch(true);
    private void BeginBranch(bool start)
    {
        ResetWork();if(_snapshot==null) return;
        _reachability=new(Source,_avoidCurse.ButtonPressed,potionScenarioBound:true,
            workers:Source.Remaining>3?Math.Clamp(System.Environment.ProcessorCount-1,1,4):1,
            targetDirected:Source.Remaining>3,includeRerolls:_includeDriftwood.ButtonPressed,goalLanguageProof:true,
            includeEnchantments:_includeEnchantments.ButtonPressed)
        {
            GoldFailureObserved=static (exception,scope)=>RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(
                exception,"CrystalGoldOptimizationFailed",nameof(CrystalSphereAssistantPanel),
                "GoldThresholdWorker:"+scope,"runtime.issue.crystal_discovery_failed"),
            LanguageFailureObserved=static (exception,scope)=>RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(
                exception,"CrystalLanguageWorkerFailed",nameof(CrystalSphereAssistantPanel),
                "LanguageWorker","runtime.issue.crystal_discovery_failed",correlationId:scope)
        };
        Render();if(start) StartDiscovery();
    }
    private void StartDiscovery()
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
        _cancel?.Dispose();_cancel=new();var token=_cancel.Token;_running=true;_errorKey=null;_fault=null;_stopReason=null;Render();
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
                    "CrystalDiscoveryFailed", nameof(CrystalSphereAssistantPanel), "DiscoveryWorker", "runtime.issue.crystal_discovery_failed");
                queue.Enqueue(new([],DiscoveryStop.Failed,Error:ex));
            }
        });
    }
    internal void ApplyRows(ImmutableArray<CrystalRewardRoute> rows, bool complete=false)
    { _rows.AddRange(rows);_reachability?.AddWitnesses(rows);_candidatesComplete|=complete;_complete=_candidatesComplete && _projection.GoldComplete;_dirty=true; }
    internal async Task RebuildProjection()
    {
        if(_snapshot==null || _projecting) return;
        long generation=_generation,revision=_selectionRevision;
        var source=Source;var rows=_rows.ToImmutableArray();var selected=_selected;bool includeEnchantments=_includeEnchantments.ButtonPressed;
        _projectCancel?.Dispose();_projectCancel=new();var token=_projectCancel.Token;_projecting=true;_dirty=false;Render();
        try
        {
            var result=await Task.Run(()=>PredictorCrystalExplorer.Project(source,rows,selected,token,includeEnchantments:includeEnchantments),token);
            if(_exited || generation!=_generation || revision!=_selectionRevision || token.IsCancellationRequested) return;
            _switching=false;_projection=MergeProjection(_projection,result);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception ex)
        {
            if(generation==_generation && revision==_selectionRevision) { _errorKey="projection_failed";_fault=ex; }
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "CrystalProjectionFailed", nameof(CrystalSphereAssistantPanel), "ProjectionWorker", "runtime.issue.crystal_projection_failed");
        }
        finally { if(!_exited && generation==_generation && revision==_selectionRevision) { _projecting=false;Render(); } }
    }
    internal void SelectOption(CrystalRewardOption option)
    {
        if(_stale) return;
        PredictorCrystalSolution? selectedProof=null;
        if(_selected.Contains(option)) _selected=_selected.Remove(option);
        else if(_projection.Available.TryGetValue(option,out selectedProof)) _selected=_selected.Add(option);
        else { if(_pendingOptions.Contains(option)) FocusOption(option);return; }
        InvalidatePresentation();_focusTarget=null;
        // Old workers keep their old queue. Each selection owns an independent
        // frontier, while the frozen-board session reuses tables and witnesses.
        _cancel?.Cancel();_worker=null;_updates=new();_running=_complete=_candidatesComplete=_startWhenUpdatesDrained=false;
        _stopReason=null;_errorKey=null;_fault=null;_progress=_goldProgress=default;_phase=CrystalDiscoveryPhase.FindingPlans;
        _selectionRevision++;_projectCancel?.Cancel();_projecting=false;
        // This option already carries a verified plan for the enlarged selection.
        // It remains usable while compatible additions are reprojected off-thread.
        _switching=true;
        var knownOptions=_projection.Available.Keys.Concat(_pendingOptions).Where(o=>!_selected.Contains(o)).ToArray();
        _pendingOptions.Clear();_pendingOptions.UnionWith(knownOptions);
        if(_reachability!=null) _pendingOptions.RemoveWhere(o=>_reachability.IsCertifiedConflict(_selected.Add(o)));
        var cached=_reachability?.PeekKnown(_selected);
        _projection=cached==null?new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,selectedProof,0)
            :cached with { SelectedPlan=cached.SelectedPlan??selectedProof };
        _dirty=_rows.Count>0;_projectionDelay=0;Render();
        if(_dirty) _=RebuildProjection();
        StartDiscovery();
    }
    private void SwitchSearchQueue()
    {
        _cancel?.Cancel();_worker=null;_updates=new();_running=_complete=_startWhenUpdatesDrained=false;
        _stopReason=null;_errorKey=null;_fault=null;_progress=default;
        _selectionRevision++;_projectCancel?.Cancel();_projecting=_dirty=_switching=false;
    }
    private void FocusOption(CrystalRewardOption option)
    {
        SwitchSearchQueue();_focusTarget=option;_focusVerdict=CrystalFocusVerdict.Checking;
        RuntimeLog.Info($"crystalFocus=start;seed={Source.Context.Seed};remaining={Source.Remaining};avoidCurse={_avoidCurse.ButtonPressed};driftwood={_includeDriftwood.ButtonPressed};selected={string.Join(',',_selected)};target={option}");
        Render();StartDiscovery();
    }
    private void UsePlan()
    {
        if(_snapshot==null || _projection.SelectedPlan is not { } plan) return;
        try
        {
            if(!EnsureCurrentScene()) return;
            GuideRequested?.Invoke(_snapshot,Mode,plan);
        }
        catch(Exception ex) { RuntimeLog.WarnException("crystalGuideStartFailed=true",ex);Invalidate(); }
    }
    // Entering the previewed payment branch is an expected transition. Keep
    // its exact frozen solver/cache, while removing the no-longer-legal branch.
    internal bool AcceptEnteredBoard(CrystalSphereLiveSnapshot current)
    {
        if(_snapshot==null || Mode is not ("Three" or "Six") || current.Seed!=_snapshot.Seed ||
            current.Branches.Length!=1 || current.Branches[0].Mode!="Remaining") return false;
        var expected=Source;var actual=current.Branches[0].Snapshot;
        if(expected.Context.Character!=actual.Context.Character || expected.State.Position!=actual.State.Position ||
            expected.Context.Crystal?.PlayerNetId!=actual.Context.Crystal?.PlayerNetId ||
            !CrystalSphereGuidance.Matches(expected,actual,false,RequiresNiche())) return false;
        _snapshot=_snapshot with { Fingerprint=current.Fingerprint,Branches=[("Remaining",expected)] };
        _branch.Clear();_branch.AddItem(T("Remaining")+" · "+string.Format(T("remaining"),expected.Remaining));_branch.Select(0);
        Render();return true;
    }
    private bool EnsureCurrentScene()
    {
        if(_snapshot==null) return false;
        try
        {
            if(CrystalSphereLiveCapture.Fingerprint(_run,GetTree().Root)==_snapshot.Fingerprint)
            {
                // Ordinary identity/upgrade queries retain their local snapshot
                // policy. Exact enchantment plans also depend on captured Niche.
                if(!RequiresNiche() || Source.State.Streams.Single(s=>s.Stream==PredictorStream.Niche)==
                    CrystalSphereLiveCapture.RngState(PredictorStream.Niche,_run.Rng.Niche)) return true;
                Invalidate();return false;
            }
            if(AcceptEnteredBoard(CrystalSphereLiveCapture.Capture(_run,GetTree().Root,_runtime.Detection.DisplayVersion))) return true;
        }
        // Native payment/reveal animations may temporarily lack a capturable
        // board. Wait for the stable state instead of retiring the plan.
        catch(InvalidOperationException ex) when(ex.Message=="CrystalSceneBusy") { return false; }
        catch { }
        Invalidate();return false;
    }
    private void Invalidate()
    {
        // Keep the previous plan readable after its board has advanced. Explicit
        // Refresh captures a new problem; old positives are never re-certified.
        InvalidatePresentation();_stale=true;_resumeOnOpen=false;_cancel?.Cancel();_projectCancel?.Cancel();Render();
    }
    private bool RequiresNiche()=>_selected.Any(g=>g.Enchantment!=null) && CrystalRewardOption.UsesNicheEnchantments(Source.State);
    internal void ApplyUpdate(Update update, bool render=true)
    {
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
        _phase=update.Phase;
        if(update.Progress is { } progress) _progress=progress;
        if(update.GoldProgress is { } goldProgress) _goldProgress=goldProgress;
        if(update.CandidatesComplete is { } candidatesComplete) _candidatesComplete=candidatesComplete;
        if(update.Stop is { } reason)
        { _running=false;_stopReason=reason;_complete=reason==DiscoveryStop.Exhausted && _candidatesComplete && _projection.GoldComplete; }
        if(update.Error!=null) { _errorKey="discovery_failed";_fault=update.Error; }
        NormalizePendingOptions();
        if(render) Render();
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
    public override void _Notification(int what)
    {
        if(what!=NotificationVisibilityChanged || !IsInsideTree()) return;
        _presentationSceneValidated=false;
        if(!IsVisibleInTree()) return;
        // Revalidate before any retained answer is shown, even when its worker
        // completed while hidden. Busy native animations defer presentation.
        _poll=.4;
        _presentationSceneValidated=_snapshot==null || _stale || EnsureCurrentScene() || _stale;
        SetPresentationVisibility();
        Render();
    }
    public override void _Process(double delta)
    {
        bool visible=IsVisibleInTree();
        if(visible && _snapshot!=null && !_stale)
        {
            _poll-=delta;
            if(!_presentationSceneValidated || _poll<=0)
            {
                _poll=.4;_presentationSceneValidated=EnsureCurrentScene() || _stale;
                SetPresentationVisibility();
            }
        }
        bool changed=ConsumeUpdates();
        if(_updates.IsEmpty && _worker is not { IsCompleted:false } &&
            (_startWhenUpdatesDrained || (_resumeOnOpen && visible && _presentationSceneValidated)))
        {
            bool explicitStart=_startWhenUpdatesDrained;
            _resumeOnOpen=false;_startWhenUpdatesDrained=false;
            // A canceled worker may finish a focused proof after suspension.
            // Consume its entire receipt sequence before deciding to resume.
            if(explicitStart || _focusTarget==null || _focusVerdict==CrystalFocusVerdict.Checking) StartDiscovery();
        }
        _projectionDelay-=delta;
        if(!_stale && _dirty && !_projecting && _projectionDelay<=0) { _projectionDelay=.5;_=RebuildProjection(); }
        else if(changed) Render();
        if(visible && (_snapshot==null || _stale || _presentationSceneValidated)) ApplyPresentationSlice();
    }
    private bool ConsumeUpdates()
    {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        bool changed=false;
        for(int count=0;count<UpdatesPerFrame && _updates.TryDequeue(out var update);count++)
        {
            ApplyUpdate(update,false);changed=true;
            if(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds>=UpdateSliceMilliseconds) break;
        }
        return changed;
    }
    public override void _ExitTree()
    {
        RolltheSpire2.Ui.Tooltips.SearchTooltip.DetachScope(this);
        _exited=true;_generation++;_selectionRevision++;_cancel?.Cancel();_cancel?.Dispose();
        _projectCancel?.Cancel();_projectCancel?.Dispose();
        // Only the launcher-owned event session may retain the detached index.
        // The freed Godot panel must release its own references and queues.
        ClearPresentationWork();
        _pendingOptions.Clear();_rows.Clear();_snapshot=null;_reachability=null;_worker=null;_updates=new();_selected=[];
        _projection=new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
    }
}
