using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.PredictionSessions;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class CrystalSphereAssistantPanel
{
    internal CrystalPredictionSession Calculation { get; private set; } = new();
    private double _poll;
    private bool _exited;
    private string _emptyStatusKey="refresh_first";
    private Exception? _captureFault;

    internal CrystalPredictionSession.Checkpoint? SuspendSession() => Calculation.SuspendSession();
    internal void RestoreSession(CrystalPredictionSession.Checkpoint checkpoint)
    {
        InvalidatePresentation();Calculation.RestoreSession(checkpoint);
        _branch.Clear();
        foreach(var branch in Calculation.Snapshot!.Branches)
            _branch.AddItem(T(branch.Mode)+(branch.Mode=="Remaining"?" · "+string.Format(T("remaining"),branch.Snapshot.Remaining):""));
        _branch.Select(checkpoint.Branch);_avoidCurse.SetPressedNoSignal(checkpoint.AvoidCurse);
        _includeDriftwood.SetPressedNoSignal(checkpoint.IncludeDriftwood);
        _includeEnchantments.SetPressedNoSignal(checkpoint.IncludeEnchantments);
        _presentationSceneValidated=false;Render();
    }
    internal void OpenSession()
    {
        if(Calculation.Snapshot==null) { Refresh();return; }
        _presentationSceneValidated=EnsureCurrentScene() || Calculation.Stale;
        Render();
    }
    internal void Refresh()
    {
        try { ShowSnapshot(CrystalSphereLiveCapture.Capture(_run,GetTree().Root,_runtime.Detection.DisplayVersion)); }
        catch(InvalidOperationException ex)
        {
            bool expected=ex.Message is "CrystalLocalPlayerMissing" or "CrystalPlayerMismatch" or "CrystalNotCurrentScene" or "CrystalSceneBusy" or "CrystalSceneFinished" or "CrystalSnapshotChanged";
            _emptyStatusKey=expected?ex.Message:"unavailable";_captureFault=ex;if(Calculation.Snapshot!=null) Invalidate();
            if(!expected) RuntimeLog.WarnException("crystalCaptureUnavailable=true",ex);Render();
        }
        catch(Exception ex) { _emptyStatusKey="unavailable";_captureFault=ex;if(Calculation.Snapshot!=null) Invalidate();RuntimeLog.WarnException("crystalCaptureFailed=true",ex);Render(); }
    }
    internal void ShowSnapshot(CrystalSphereLiveSnapshot snapshot, bool startDiscovery=true)
    {
        InvalidatePresentation();_captureFault=null;_branch.Clear();
        foreach(var branch in snapshot.Branches) _branch.AddItem(T(branch.Mode)+(branch.Mode=="Remaining"?" · "+string.Format(T("remaining"),branch.Snapshot.Remaining):""));
        _branch.Select(0);Calculation.Begin(snapshot,0,_avoidCurse.ButtonPressed,_includeDriftwood.ButtonPressed,_includeEnchantments.ButtonPressed,startDiscovery);
        _presentationSceneValidated=false;Render();
    }
    private void BeginBranch() => BeginBranch(true);
    private void BeginBranch(bool start)
    {
        if(Calculation.Snapshot==null) return;
        InvalidatePresentation();Calculation.Begin(Calculation.Snapshot,_branch.Selected,_avoidCurse.ButtonPressed,
            _includeDriftwood.ButtonPressed,_includeEnchantments.ButtonPressed,start);
        _presentationSceneValidated=false;Render();
    }
    private void StartDiscovery() { Calculation.StartDiscovery();Render(); }
    internal void SelectOption(CrystalRewardOption option)
    {
        if(Calculation.Stale) return;
        var before=Calculation.Selected;Calculation.SelectOption(option);
        if(before!=Calculation.Selected) InvalidatePresentation();
        Render();
    }
    internal void ApplyUpdate(CrystalPredictionSession.Update update, bool render=true)
    { Calculation.ApplyUpdate(update);if(render) Render(); }
    private void UsePlan()
    {
        if(Calculation.Snapshot==null || Calculation.Projection.SelectedPlan is not { } plan) return;
        try
        {
            if(!EnsureCurrentScene()) return;
            GuideRequested?.Invoke(Calculation.Snapshot,Mode,plan);
        }
        catch(Exception ex) { RuntimeLog.WarnException("crystalGuideStartFailed=true",ex);Invalidate(); }
    }
    // Entering the previewed payment branch is an expected transition. Keep
    // its exact frozen solver/cache, while removing the no-longer-legal branch.
    internal bool AcceptEnteredBoard(CrystalSphereLiveSnapshot current)
    {
        if(Calculation.Snapshot==null || Mode is not ("Three" or "Six") || current.Seed!=Calculation.Snapshot.Seed ||
            current.Branches.Length!=1 || current.Branches[0].Mode!="Remaining") return false;
        var expected=Source;var actual=current.Branches[0].Snapshot;
        if(expected.Context.Character!=actual.Context.Character || expected.State.Position!=actual.State.Position ||
            expected.Context.Crystal?.PlayerNetId!=actual.Context.Crystal?.PlayerNetId ||
            !CrystalSphereGuidance.Matches(expected,actual,false,RequiresNiche())) return false;
        Calculation.AcceptEnteredBoard(Calculation.Snapshot with { Fingerprint=current.Fingerprint,Branches=[("Remaining",expected)] });
        _branch.Clear();_branch.AddItem(T("Remaining")+" · "+string.Format(T("remaining"),expected.Remaining));_branch.Select(0);
        Render();return true;
    }
    private bool EnsureCurrentScene()
    {
        if(Calculation.Snapshot==null) return false;
        try
        {
            if(CrystalSphereLiveCapture.Fingerprint(_run,GetTree().Root)==Calculation.Snapshot.Fingerprint)
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
    private void Invalidate() { InvalidatePresentation();Calculation.Invalidate();Render(); }
    private bool RequiresNiche()=>Calculation.Selected.Any(g=>g.Enchantment!=null) && CrystalRewardOption.UsesNicheEnchantments(Source.State);
    public override void _Notification(int what)
    {
        if(what!=NotificationVisibilityChanged || !IsInsideTree()) return;
        _presentationSceneValidated=false;
        if(!IsVisibleInTree()) return;
        // Revalidate before any retained answer is shown, even when its worker
        // completed while hidden. Busy native animations defer presentation.
        _poll=.4;
        _presentationSceneValidated=Calculation.Snapshot==null || Calculation.Stale || EnsureCurrentScene() || Calculation.Stale;
        SetPresentationVisibility();
        Render();
    }
    public override void _Process(double delta)
    {
        bool visible=IsVisibleInTree();
        if(visible && Calculation.Snapshot!=null && !Calculation.Stale)
        {
            _poll-=delta;
            if(!_presentationSceneValidated || _poll<=0)
            {
                _poll=.4;_presentationSceneValidated=EnsureCurrentScene() || Calculation.Stale;
                SetPresentationVisibility();
            }
        }
        if(Calculation.Advance(delta,visible && _presentationSceneValidated,UpdatesPerFrame,UpdateSliceMilliseconds)) Render();
        if(visible && (Calculation.Snapshot==null || Calculation.Stale || _presentationSceneValidated)) ApplyPresentationSlice();
    }
    public override void _ExitTree()
    {
        RolltheSpire2.Ui.Tooltips.SearchTooltip.DetachScope(this);
        _exited=true;Calculation.Dispose();ClearPresentationWork();
    }
}
