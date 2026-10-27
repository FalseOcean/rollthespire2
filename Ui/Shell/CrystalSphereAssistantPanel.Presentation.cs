using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.Shell;

// Owns only presentation work. Solver receipts are merged independently and
// never dropped or interpreted as negative evidence because a tile is deferred.
internal sealed partial class CrystalSphereAssistantPanel
{
    private const int UpdatesPerFrame=8, OptionTilesPerFrame=4;
    private const double UpdateSliceMilliseconds=2, OptionSliceMilliseconds=2;
    private readonly record struct OptionPresentation(bool Pending,bool Sacrifice,bool Stale,int ContentRevision);
    private readonly Dictionary<CrystalRewardOption,OptionPresentation> _requestedOptionViews=[];
    private readonly Dictionary<CrystalRewardOption,OptionPresentation> _presentedOptionViews=[];
    private readonly Dictionary<string,int> _presentedPendingCounts=[];
    private readonly Dictionary<string,int> _presentedGroupCounts=[];
    private readonly HashSet<CrystalRewardOption> _visibleOptionViews=[];
    private readonly Queue<CrystalRewardOption> _optionPresentationQueue=[];
    private readonly HashSet<CrystalRewardOption> _queuedOptionViews=[];
    private bool _presentationRequested=true, _presentationSceneValidated, _presentationInvalidated=true, _optionGroupsDirty;
    private int _contentRevision, _renderedTargetContentRevision=-1, _renderedGroupContentRevision=-1, _targetPresentationIndex;

    // Current requested controls have been applied; discovery may continue to
    // publish new evidence. This is deliberately independent of solver completion.
    internal bool IsPresentationReady => !_exited && _results!=null && !_presentationRequested && !_presentationInvalidated &&
        _updates.IsEmpty && _optionPresentationQueue.Count==0 && _targetPresentationIndex>=_selected.Length && (_snapshot==null || _stale || _presentationSceneValidated);

    private void NormalizePendingOptions()
    {
        _pendingOptions.ExceptWith(_projection.Available.Keys);
        _pendingOptions.ExceptWith(_selected);
        if(_candidatesComplete || _snapshot==null) _pendingOptions.Clear();
    }
    private void Render()
    {
        NormalizePendingOptions();
        // A request can lose or downgrade evidence between process frames. Hide
        // the old view before chrome describes the new request; the desired-state
        // pass will restore unchanged tiles and leave changed ones hidden until
        // their bounded update is applied. Progress alone does not invalidate it.
        if(_renderedTargets.IsDefault || !_renderedTargets.SequenceEqual(_selected) ||
            _renderedTargetContentRevision!=_contentRevision || _visibleOptionViews.Any(option=>
                !TryCurrentOptionView(option,out var current) || !_presentedOptionViews.TryGetValue(option,out var shown) || shown!=current))
            InvalidatePresentation();
        _presentationRequested=true;
        // Chrome is small and preserves immediate safety/status feedback. The
        // cold artwork, target controls and flowing layouts are owned by Process.
        RenderChrome();
    }
    private void InvalidatePresentation()
    {
        _presentationInvalidated=true;
        if(_results==null) return;
        _results.Hide();_targetRow.Hide();
    }
    private bool TryCurrentOptionView(CrystalRewardOption option,out OptionPresentation state)
    {
        bool pending=_pendingOptions.Contains(option),available=_projection.Available.ContainsKey(option);
        state=new(pending,!pending && available && _projection.SacrificeRelics.Contains(option.Key),_stale,_contentRevision);
        return option!=CrystalRewardOption.AnyRelic && (pending || available);
    }
    private void HideOptionView(CrystalRewardOption option,Control tile)
    {
        if(!_visibleOptionViews.Remove(option)) return;
        tile.Hide();string group=GroupOf(option);
        _presentedGroupCounts[group]=_presentedGroupCounts.GetValueOrDefault(group)-1;
        if(_presentedOptionViews[option].Pending) _presentedPendingCounts[group]=_presentedPendingCounts.GetValueOrDefault(group)-1;
        _optionGroupsDirty=true;
    }
    private void ShowOptionView(CrystalRewardOption option,Control tile,OptionPresentation state)
    {
        if(!_visibleOptionViews.Add(option)) return;
        tile.Show();string group=GroupOf(option);
        _presentedGroupCounts[group]=_presentedGroupCounts.GetValueOrDefault(group)+1;
        if(state.Pending) _presentedPendingCounts[group]=_presentedPendingCounts.GetValueOrDefault(group)+1;
        _optionGroupsDirty=true;
    }
    private static string GroupOf(CrystalRewardOption option) => option.Kind==PredictorRewardKind.Card
        ?ModelDb.GetById<CardModel>(new ModelId(option.Key.Category,option.Key.Entry)).Rarity.ToString():option.Kind.ToString();

    private void QueueOptionView(CrystalRewardOption option)
    {
        if(_queuedOptionViews.Add(option)) _optionPresentationQueue.Enqueue(option);
    }
    private void CaptureRequestedOptionViews()
    {
        var desired=_projection.Available.Keys.Concat(_pendingOptions).Where(o=>o!=CrystalRewardOption.AnyRelic).ToHashSet();
        foreach(var option in _requestedOptionViews.Keys.Where(o=>!desired.Contains(o)).ToArray())
        { _requestedOptionViews.Remove(option);QueueOptionView(option); }
        // Keep existing tile identity/order across progress receipts. Only new or
        // actually changed representations enter the work queue; updating a
        // proof or a progress counter alone performs no tile/layout work.
        var added=new List<CrystalRewardOption>();
        foreach(var option in desired)
        {
            TryCurrentOptionView(option,out var state);
            if(_requestedOptionViews.TryGetValue(option,out var previous) && previous==state) continue;
            bool newView=!_requestedOptionViews.ContainsKey(option);
            _requestedOptionViews[option]=state;
            if(newView) added.Add(option);else QueueOptionView(option);
        }
        foreach(var option in added.OrderBy(NameOf,StringComparer.CurrentCulture)) QueueOptionView(option);
        foreach(var (option,tile) in _optionTiles)
        {
            if(_requestedOptionViews.TryGetValue(option,out var requested) &&
                _presentedOptionViews.TryGetValue(option,out var shown) && shown==requested) ShowOptionView(option,tile,shown);
            else HideOptionView(option,tile);
        }
        _presentationInvalidated=false;
    }
    private void ApplyPresentationSlice()
    {
        if(_exited || _results==null) return;
        if(_presentationRequested)
        {
            CaptureRequestedOptionViews();
            _presentationRequested=false;
        }
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        if(_renderedTargets.IsDefault || !_renderedTargets.SequenceEqual(_selected) || _renderedTargetContentRevision!=_contentRevision)
        {
            Clear(_targetRow);_renderedTargets=_selected;_renderedTargetContentRevision=_contentRevision;_targetPresentationIndex=0;
            if(_selected.IsEmpty) _targetRow.AddChild(new Label { Text=T("none_selected"),
                AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=SizeFlags.ExpandFill });
        }
        int applied=0;
        while(_targetPresentationIndex<_selected.Length && applied<OptionTilesPerFrame &&
            (applied==0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds<OptionSliceMilliseconds))
        { _targetRow.AddChild(Tile(_selected[_targetPresentationIndex++],true));applied++; }
        foreach(var button in _targetRow.GetChildren().OfType<Button>()) button.Disabled=_stale;
        int examined=applied;
        while(examined<OptionTilesPerFrame && _optionPresentationQueue.Count>0 &&
            (applied==0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds<OptionSliceMilliseconds))
        {
            var option=_optionPresentationQueue.Dequeue();_queuedOptionViews.Remove(option);examined++;
            if(!_requestedOptionViews.TryGetValue(option,out var requested))
            {
                if(_optionTiles.Remove(option,out var removed))
                { HideOptionView(option,removed);removed.GetParent().RemoveChild(removed);removed.QueueFree(); }
                _presentedOptionViews.Remove(option);applied++;continue;
            }
            bool previouslyPresented=_presentedOptionViews.TryGetValue(option,out var previous);
            if(previouslyPresented && previous==requested)
            { if(_optionTiles.TryGetValue(option,out var unchanged)) ShowOptionView(option,unchanged,requested);continue; }
            if(_optionTiles.TryGetValue(option,out var tile) && previous.ContentRevision!=requested.ContentRevision)
            { tile.GetParent().RemoveChild(tile);tile.QueueFree();_optionTiles.Remove(option);tile=null; }
            if(tile==null)
            {
                tile=Tile(option,false);tile.Hide();_groups[GroupOf(option)].Flow.AddChild(tile);_optionTiles.Add(option,tile);
            }
            tile.Modulate=requested.Pending || requested.Stale?new Color(1,1,1,.45f):Colors.White;
            if(tile.GetNodeOrNull<TextureRect>("CrystalSacrificeBadge") is { } wing) wing.Visible=requested.Sacrifice;
            var button=tile as Button??tile.GetChildren().OfType<Button>().FirstOrDefault();
            if(button!=null) { button.Disabled=requested.Stale;button.TooltipText=NameOf(option)+(requested.Pending?"\n"+T("focus_hint"):""); }
            _presentedOptionViews[option]=requested;ShowOptionView(option,tile,requested);applied++;
        }
        if(_optionGroupsDirty || _renderedGroupContentRevision!=_contentRevision)
        {
            _optionGroupsDirty=false;_renderedGroupContentRevision=_contentRevision;
            foreach(var (group,controls) in _groups)
            {
                int count=_presentedGroupCounts.GetValueOrDefault(group);
                int pending=_presentedPendingCounts.GetValueOrDefault(group);
                controls.Count.Text=pending==0?T("group_"+group)+" · "+count:
                    string.Format(T("group_pending"),T("group_"+group),count-pending,pending);
                ((Control)controls.Flow.GetParent()).Visible=count>0;
            }
        }
        SetPresentationVisibility();
    }
    private void SetPresentationVisibility()
    {
        if(_results==null) return;
        bool allowed=_snapshot==null || _stale || _presentationSceneValidated;
        _results.Visible=allowed && !_presentationInvalidated;
        _targetRow.Visible=allowed && !_presentationInvalidated;
        _rewardScroll.Visible=allowed && !_presentationInvalidated && _visibleOptionViews.Count>0;
        _emptyState.Visible=!_rewardScroll.Visible;
        if(!allowed) _use.Disabled=true;
    }
    private void ClearPresentationWork()
    {
        _requestedOptionViews.Clear();_presentedOptionViews.Clear();_presentedPendingCounts.Clear();_presentedGroupCounts.Clear();
        _visibleOptionViews.Clear();_queuedOptionViews.Clear();_optionPresentationQueue.Clear();
        _optionTiles.Clear();_renderedTargets=default;_targetPresentationIndex=0;_presentationRequested=false;
        _presentationSceneValidated=false;_presentationInvalidated=true;_optionGroupsDirty=false;
    }
}
