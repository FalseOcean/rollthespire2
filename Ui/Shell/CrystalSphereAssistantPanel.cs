using System.Collections.Concurrent;
using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class CrystalSphereAssistantPanel : VBoxContainer
{
    private IRunState _run = null!;
    private ModRuntimeSnapshot _runtime = null!;
    private IUiTextProvider _text = null!;
    private IGameContentNameResolver _names = null!;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("crystal-options");
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
    private bool _exited, _running, _complete, _dirty, _projecting, _switching;
    private string? _error;
    internal enum DiscoveryStop { TimeLimit, UserPaused, Exhausted, Failed }
    private DiscoveryStop? _stopReason;
    private CrystalDiscoveryPhase _phase;
    private CrystalDiscoveryProgress _progress;
    private string _language = "zh";
    private Label _status = null!, _summary = null!, _speed = null!, _elapsed = null!;
    private readonly List<(Label Control,string Key)> _labels=[];
    private readonly List<(Button Control,string Key)> _buttons=[];
    private VBoxContainer _targetRow = null!;
    private VBoxContainer _results = null!;
    private ImmutableArray<CrystalRewardOption> _renderedTargets;
    private readonly Dictionary<CrystalRewardOption,Control> _optionTiles=[];
    private readonly Dictionary<string,(Label Count,HFlowContainer Flow)> _groups=[];
    private readonly OptionButton _branch = new();
    private readonly CheckButton _avoidCurse = new() { ButtonPressed = true };
    private readonly CheckButton _showGuidance = new();
    private readonly CheckButton _transparentMask = new();
    private Button _continue = null!, _stop = null!, _use = null!;
    internal sealed record Update(ImmutableArray<CrystalRewardRoute> Rows, DiscoveryStop? Stop=null,
        CrystalDiscoveryPhase Phase=CrystalDiscoveryPhase.FindingPlans, Exception? Error=null, CrystalDiscoveryProgress? Progress=null,
        CrystalOptionProjection? Projection=null,ImmutableArray<CrystalRewardOption> ExcludedCandidates=default);
    private ConcurrentQueue<Update> _updates = new();
    internal event Action<CrystalSphereLiveSnapshot,string,PredictorCrystalSolution>? GuideRequested;
    private string T(string key) => _text.Get("predictor.crystal."+key);
    private PredictorCrystalSnapshot Source => _snapshot!.Branches[_branch.Selected].Snapshot;
    private string Mode => _snapshot!.Branches[_branch.Selected].Mode;
    internal void ApplyLanguage(string language)
    {
        if(_language==language) return;
        _language=language;_text=JsonUiTextProvider.CreatePredictorUi13(language);
        _names=RuntimeGameContentNameResolver.Create(language);
        foreach(var (control,key) in _labels) control.Text=T(key);
        foreach(var (control,key) in _buttons) control.Text=T(key);
        _avoidCurse.Text=T("avoid_curse");_avoidCurse.TooltipText=T("avoid_curse_hint");
        _showGuidance.Text=T("show_guidance");
        _transparentMask.Text=T("transparent_mask");
        if(_snapshot!=null) for(int i=0;i<_snapshot.Branches.Length;i++)
        {
            var branch=_snapshot.Branches[i];_branch.SetItemText(i,T(branch.Mode)+(branch.Mode=="Remaining"?" · "+string.Format(T("remaining"),branch.Snapshot.Remaining):""));
        }
        foreach(var tile in _optionTiles.Values) { tile.GetParent().RemoveChild(tile);tile.QueueFree(); }
        _optionTiles.Clear();_renderedTargets=default;
        Render();
    }
    internal void Initialize(IRunState run, ModRuntimeSnapshot runtime)
    {
        _run=run; _runtime=runtime;
        Theme=new Godot.Theme { DefaultFontSize=20 };
        Theme.SetColor("font_color","Label",new Color("e2e7ec"));
        Theme.SetColor("font_shadow_color","Label",Colors.Transparent);
        Theme.SetStylebox("normal","Label",new StyleBoxEmpty());
        var preferences=new SearchWorkspacePersistence(OS.GetUserDataDir(),runtime.Profile.ProfileId,false).Preferences;
        _language=preferences.LanguageOverride is "zh" or "en" ? preferences.LanguageOverride :
            TranslationServer.GetLocale().StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"zh":"en";
        _text=JsonUiTextProvider.CreatePredictorUi13(_language); _names=RuntimeGameContentNameResolver.Create(_language);
        AddThemeConstantOverride("separation",12);
        var layout=new HBoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill };layout.AddThemeConstantOverride("separation",20);AddChild(layout);
        VBoxContainer Column(float width,bool expand=false)
        {
            var column=new VBoxContainer { CustomMinimumSize=new(width,0),SizeFlagsHorizontal=expand?SizeFlags.ExpandFill:SizeFlags.Fill };
            column.AddThemeConstantOverride("separation",14);layout.AddChild(column);return column;
        }
        var left=Column(248);
        Label Text(Node parent,string key,int size=20)
        {
            var label=new Label { Text=T(key),AutowrapMode=TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size",size);parent.AddChild(label);_labels.Add((label,key));return label;
        }
        Button Action(Node parent,string key,System.Action click)
        {
            var button=Button(parent,T(key),click);button.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            _buttons.Add((button,key));return button;
        }
        Text(left,"scene_title",24);
        Text(left,"choose_count",18);StyleChoice(_branch);_branch.CustomMinimumSize=new(248,48);left.AddChild(_branch);
        _branch.ItemSelected += _=> BeginBranch();
        _avoidCurse.Text=T("avoid_curse");_avoidCurse.TooltipText=T("avoid_curse_hint");left.AddChild(_avoidCurse);
        _avoidCurse.Toggled += _=>{ if(_snapshot!=null) BeginBranch(); };
        _showGuidance.Text=T("show_guidance");_showGuidance.ButtonPressed=CrystalSphereGuidance.DisplayEnabled;
        _showGuidance.Toggled += enabled=>CrystalSphereGuidance.DisplayEnabled=enabled;left.AddChild(_showGuidance);
        _transparentMask.Text=T("transparent_mask");_transparentMask.ButtonPressed=CrystalSphereMaskAppearance.Enabled;
        _transparentMask.Toggled += CrystalSphereMaskAppearance.SetEnabled;left.AddChild(_transparentMask);
        Action(left,"refresh",Refresh);
        left.AddChild(new HSeparator());Text(left,"selected_targets",20);
        var targetScroll=new ScrollContainer { SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };left.AddChild(targetScroll);
        _targetRow=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };targetScroll.AddChild(_targetRow);
        _summary=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart };_summary.AddThemeFontSizeOverride("font_size",17);left.AddChild(_summary);
        _use=Action(left,"guide_start",UsePlan);
        layout.AddChild(new VSeparator());
        var center=Column(0,true);
        var heading=new HBoxContainer();center.AddChild(heading);
        var headingText=Text(heading,"available_rewards",24);headingText.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        Text(center,"choose_hint",17);
        var scroll=new ScrollContainer { SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };center.AddChild(scroll);
        _results=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };_results.AddThemeConstantOverride("separation",18);scroll.AddChild(_results);
        foreach(string group in new[]{"Relic","Rare","Uncommon","Common","Potion"})
        {
            var column=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };_results.AddChild(column);
            var count=new Label();column.AddChild(count);
            var flow=new HFlowContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };column.AddChild(flow);
            _groups.Add(group,(count,flow));
        }
        layout.AddChild(new VSeparator());
        var right=Column(200);
        Text(right,"search_status",22);
        _status=new Label { Text=T("refresh_first"),AutowrapMode=TextServer.AutowrapMode.WordSmart };_status.AddThemeFontSizeOverride("font_size",18);right.AddChild(_status);
        Text(right,"search_speed",18);
        _speed=new Label { Text="—",AutowrapMode=TextServer.AutowrapMode.WordSmart };right.AddChild(_speed);
        Text(right,"elapsed",18);
        _elapsed=new Label { Text="—" };right.AddChild(_elapsed);
        right.AddChild(new Control { SizeFlagsVertical=SizeFlags.ExpandFill });
        Text(right,"search_hint",17);
        _continue=Action(right,"continue_discovery",StartDiscovery);
        _stop=Action(right,"pause_discovery",()=>_cancel?.Cancel());
        Render();
    }
    private static Button Button(Node parent,string text,Action clicked)
    {
        var button=WorkspacePalette.Canonical.Button(text);button.CustomMinimumSize=new(112,48);
        parent.AddChild(button);button.Pressed+=clicked;return button;
    }
    private static void StyleChoice(OptionButton choice)
    {
        var p=WorkspacePalette.Canonical;choice.CustomMinimumSize=new(250,48);choice.FitToLongestItem=false;
        foreach(var state in new[]{"normal","hover","pressed","disabled","focus"})
            choice.AddThemeStyleboxOverride(state,p.Box(state=="hover"?p.Hover:p.Surface,p.Line,1));
        choice.AddThemeFontSizeOverride("font_size",20);
        choice.AddThemeColorOverride("font_color",p.Color(p.Text));choice.GetPopup().AddThemeFontSizeOverride("font_size",20);
    }
    private static void Clear(Node parent) { foreach(Node child in parent.GetChildren()) { parent.RemoveChild(child);child.QueueFree(); } }
    private void ResetWork()
    {
        _generation++;_selectionRevision++;_cancel?.Cancel();_cancel?.Dispose();_cancel=null;
        _projectCancel?.Cancel();_projectCancel?.Dispose();_projectCancel=null;
        _worker=null;_updates=new();_rows.Clear();_selected=[];_pendingOptions.Clear();
        _projection=new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
        _reachability=null;_running=_complete=_dirty=_projecting=_switching=false;_error=null;_stopReason=null;_phase=CrystalDiscoveryPhase.FindingPlans;_progress=default;
    }
    internal void Refresh()
    {
        ResetWork();_snapshot=null;
        try { ShowSnapshot(CrystalSphereLiveCapture.Capture(_run,GetTree().Root,_runtime.Detection.DisplayVersion)); }
        catch(InvalidOperationException ex)
        {
            bool expected=ex.Message is "CrystalSoloOnly" or "CrystalNotCurrentScene" or "CrystalSceneBusy" or "CrystalSceneFinished" or "CrystalSnapshotChanged";
            _status.Text=T(expected?ex.Message:"unavailable");if(!expected) RuntimeLog.WarnException("crystalCaptureUnavailable=true",ex);Render();
        }
        catch(Exception ex) { _status.Text=T("unavailable");RuntimeLog.WarnException("crystalCaptureFailed=true",ex);Render(); }
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
            targetDirected:Source.Remaining>3);Render();if(start) StartDiscovery();
    }
    private void StartDiscovery()
    {
        if(_reachability==null || _snapshot==null || _complete || _worker is { IsCompleted:false }) return;
        var reachability=_reachability;var selected=_selected;var queue=_updates;bool reproject=_switching;
        _cancel?.Dispose();_cancel=new();var token=_cancel.Token;_running=true;_error=null;_stopReason=null;Render();
        _worker=Task.Run(()=>{
            try
            {
                if(reproject)
                {
                    reachability.ProjectKnown(selected,token,known=>queue.Enqueue(new([],Projection:known)));
                    token.ThrowIfCancellationRequested();
                }
                CrystalReachabilityUpdate? update=null;
                while(!token.IsCancellationRequested && update?.Complete!=true)
                {
                    update=reachability.Advance(selected,TimeSpan.FromMilliseconds(150),token);
                    queue.Enqueue(new([],Phase:update.Phase,Progress:update.Progress,Projection:update.Projection,ExcludedCandidates:update.ExcludedCandidates));
                }
                queue.Enqueue(new([],update?.Complete==true?DiscoveryStop.Exhausted:DiscoveryStop.UserPaused,update?.Phase??CrystalDiscoveryPhase.FindingPlans,
                    Progress:update?.Progress,Projection:update?.Projection));
            }
            catch(OperationCanceledException) { queue.Enqueue(new([],DiscoveryStop.UserPaused)); }
            catch(Exception ex) { queue.Enqueue(new([],DiscoveryStop.Failed,Error:ex)); }
        });
    }
    internal void ApplyRows(ImmutableArray<CrystalRewardRoute> rows, bool complete=false)
    { _rows.AddRange(rows);_reachability?.AddWitnesses(rows);_complete|=complete;_dirty=true; }
    internal async Task RebuildProjection()
    {
        if(_snapshot==null || _projecting) return;
        long generation=_generation,revision=_selectionRevision;
        var source=Source;var rows=_rows.ToImmutableArray();var selected=_selected;
        _projectCancel?.Dispose();_projectCancel=new();var token=_projectCancel.Token;_projecting=true;_dirty=false;Render();
        try
        {
            var result=await Task.Run(()=>PredictorCrystalExplorer.Project(source,rows,selected,token),token);
            if(_exited || generation!=_generation || revision!=_selectionRevision || token.IsCancellationRequested) return;
            _switching=false;_projection=result;
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(generation==_generation && revision==_selectionRevision) _error=T("projection_failed");
            RuntimeLog.WarnException("crystalProjectionFailed=true",ex);
        }
        finally { if(!_exited && generation==_generation && revision==_selectionRevision) { _projecting=false;Render(); } }
    }
    internal void SelectOption(CrystalRewardOption option)
    {
        PredictorCrystalSolution? selectedProof=null;
        if(_selected.Contains(option)) _selected=_selected.Remove(option);
        else if(_projection.Available.TryGetValue(option,out selectedProof)) _selected=_selected.Add(option);
        else return;
        // Old workers keep their old queue. Each selection owns an independent
        // frontier, while the frozen-board session reuses tables and witnesses.
        _cancel?.Cancel();_worker=null;_updates=new();_running=_complete=false;
        _stopReason=null;_error=null;_progress=default;_phase=CrystalDiscoveryPhase.FindingPlans;
        _selectionRevision++;_projectCancel?.Cancel();_projecting=false;
        // This option already carries a verified plan for the enlarged selection.
        // It remains usable while compatible additions are reprojected off-thread.
        _switching=true;
        _pendingOptions.Clear();_pendingOptions.UnionWith(_optionTiles.Keys.Where(o=>!_selected.Contains(o)));
        var cached=_reachability?.PeekKnown(_selected);
        _projection=cached==null?new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,selectedProof,0)
            :cached with { SelectedPlan=cached.SelectedPlan??selectedProof };
        _dirty=_rows.Count>0;_projectionDelay=0;Render();
        if(_dirty) _=RebuildProjection();
        StartDiscovery();
    }
    private void UsePlan()
    {
        if(_snapshot==null || _projection.SelectedPlan is not { } plan || _selected.IsEmpty) return;
        try
        {
            if(CrystalSphereLiveCapture.Fingerprint(_run,GetTree().Root)!=_snapshot.Fingerprint) { Invalidate();return; }
            GuideRequested?.Invoke(_snapshot,Mode,plan);
        }
        catch(Exception ex) { RuntimeLog.WarnException("crystalGuideStartFailed=true",ex);Invalidate(); }
    }
    private void Invalidate() { ResetWork();_snapshot=null;_status.Text=T("discovery_stale");Render(); }
    private static GameContentKind ContentKind(CrystalRewardOption o)=>o.Kind switch {
        PredictorRewardKind.Card=>GameContentKind.Card,PredictorRewardKind.Potion=>GameContentKind.Potion,_=>GameContentKind.Relic };
    private string NameOf(CrystalRewardOption option)=>(option.Key.IsValid?_names.Resolve(option.Key,ContentKind(option)):T("any_relic"))+
        (option.Kind==PredictorRewardKind.Card && option.UpgradeLevel is >0?"+"+(option.UpgradeLevel>1?option.UpgradeLevel.ToString():""):"");
    private Control Tile(CrystalRewardOption option,bool selected)
    {
        if(selected || !option.Key.IsValid)
        {
            var button=WorkspacePalette.Canonical.Button(NameOf(option)+(selected?" ×":""));
            button.CustomMinimumSize=new(240,44);button.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            button.Pressed+=()=>SelectOption(option);return button;
        }
        var tile=new WorkspaceResultTile(WorkspacePalette.Canonical,_icons,_names,JsonUiTextProvider.CreateUi13(_language),
            option.Key,ContentKind(option),()=>SelectOption(option),()=>SelectOption(option),selected,NameOf(option));
        if(option.Kind==PredictorRewardKind.Card && option.UpgradeLevel is >0)
        {
            // Keep the variant visible even when a long localized name truncates.
            var badge=new Label { Text="+"+(option.UpgradeLevel>1?option.UpgradeLevel.ToString():""),
                Position=new(WorkspaceResultTile.TileWidth-38,WorkspaceResultTile.TileHeight-30),Size=new(30,26),
                HorizontalAlignment=HorizontalAlignment.Right,MouseFilter=MouseFilterEnum.Ignore };
            badge.AddThemeFontSizeOverride("font_size",24);
            badge.AddThemeColorOverride("font_color",new Color("8fff98"));
            badge.AddThemeColorOverride("font_outline_color",Colors.Black);
            badge.AddThemeConstantOverride("outline_size",5);tile.AddChild(badge);
        }
        return tile;
    }
    private void Render()
    {
        if(_results==null) return;
        _branch.Disabled=_snapshot==null;
        _continue.Disabled=_snapshot==null || _running || _complete;
        _stop.Disabled=!_running;
        _use.Disabled=_selected.IsEmpty || _projection.SelectedPlan==null;
        // Progress updates must not destroy a button between mouse-down and
        // mouse-up. Existing options retain both identity and visual order.
        if(_renderedTargets.IsDefault || !_renderedTargets.SequenceEqual(_selected))
        {
            Clear(_targetRow);_renderedTargets=_selected;
            if(_selected.IsEmpty) _targetRow.AddChild(new Label { Text=T("none_selected") });
            else foreach(var option in _selected) _targetRow.AddChild(Tile(option,true));
        }
        _pendingOptions.ExceptWith(_projection.Available.Keys);
        _pendingOptions.ExceptWith(_selected);
        if(_complete || _snapshot==null) _pendingOptions.Clear();
        foreach(var option in _optionTiles.Keys.Where(o=>!_projection.Available.ContainsKey(o) && !_pendingOptions.Contains(o)).ToArray())
        {
            var tile=_optionTiles[option];tile.GetParent().RemoveChild(tile);tile.QueueFree();_optionTiles.Remove(option);
        }
        string GroupOf(CrystalRewardOption o)=>o.Kind==PredictorRewardKind.Card
            ?ModelDb.GetById<CardModel>(new ModelId(o.Key.Category,o.Key.Entry)).Rarity.ToString():o.Kind.ToString();
        foreach(var option in _projection.Available.Keys.Where(o=>o!=CrystalRewardOption.AnyRelic && !_optionTiles.ContainsKey(o)).OrderBy(NameOf,StringComparer.CurrentCulture))
        {
            var tile=Tile(option,false);_groups[GroupOf(option)].Flow.AddChild(tile);_optionTiles.Add(option,tile);
        }
        foreach(var (option,tile) in _optionTiles)
        {
            bool pending=_pendingOptions.Contains(option);
            tile.Modulate=pending?new Color(1,1,1,.45f):Colors.White;
            var button=tile as Button??tile.GetChildren().OfType<Button>().FirstOrDefault();
            if(button!=null) { button.Disabled=pending;button.TooltipText=NameOf(option)+(pending?"\n"+T("candidate_pending"):""); }
        }
        foreach(var (group,controls) in _groups)
        {
            int count=controls.Flow.GetChildCount();
            int pending=_pendingOptions.Count(o=>o!=CrystalRewardOption.AnyRelic && GroupOf(o)==group);
            controls.Count.Text=pending==0?T("group_"+group)+" · "+count:
                string.Format(T("group_pending"),T("group_"+group),count-pending,pending);
            ((Control)controls.Flow.GetParent()).Visible=count>0;
        }
        _summary.Text=_selected.IsEmpty?"":_projection.SelectedPlan==null?T("checking_joint"):_switching?T("selection_ready"):
            string.Format(T("joint_ready"),_selected.Length,_projection.Gold);
        _summary.TooltipText=T("plan_scope");
        if(_snapshot==null) return;
        _status.Text=_error??T(_complete ? (_projecting || _dirty ? "discovery_finalizing" : "discovery_exhausted") :
            _running ? (_phase==CrystalDiscoveryPhase.Exhaustive ? "discovery_exhausting" : "discovery_finding") :
            _stopReason switch { DiscoveryStop.TimeLimit=>"discovery_time_limit", DiscoveryStop.UserPaused=>"discovery_user_paused",
                DiscoveryStop.Failed=>"discovery_failed", _=>"discovery_ready" });
        _speed.Text=_progress.Seconds>0?string.Format(T("speed_value"),_progress.Examined/_progress.Seconds):"—";
        _elapsed.Text=string.Format(T("elapsed_value"),_progress.Seconds);
        _status.TooltipText=T("discovery_scope")+(_progress.Examined>0?"\n"+string.Format(T("discovery_progress"),_progress.Examined,
            _progress.Pruned+_progress.Merged,_progress.Orders,_progress.Seconds):"");
    }

    internal void ApplyUpdate(Update update, bool render=true)
    {
        // Each selection owns a fresh queue. Only explicit same-query negative
        // proofs remove grey candidates; timeout/absence alone removes nothing.
        if(!update.ExcludedCandidates.IsDefaultOrEmpty) _pendingOptions.ExceptWith(update.ExcludedCandidates);
        if(update.Rows.Length>0) ApplyRows(update.Rows);
        if(update.Projection is { } projection)
        {
            _projectCancel?.Cancel();_projecting=false;_dirty=false;_switching=false;
            // Same-revision partial publications may arrive before the first
            // slice. Never erase already verified options or the clicked plan.
            _projection=new(_projection.Available.SetItems(projection.Available),
                projection.SelectedPlan??_projection.SelectedPlan,Math.Max(_projection.Gold,projection.Gold));
        }
        _phase=update.Phase;
        if(update.Progress is { } progress) _progress=progress;
        if(update.Stop is { } reason) { _running=false;_stopReason=reason;_complete=reason==DiscoveryStop.Exhausted; }
        if(update.Error!=null) { _error=T("discovery_failed");RuntimeLog.WarnException("crystalDiscoveryFailed=true",update.Error); }
        if(render) Render();
    }
    public override void _Process(double delta)
    {
        bool changed=false;
        while(_updates.TryDequeue(out var update)) { ApplyUpdate(update,false);changed=true; }
        _projectionDelay-=delta;
        if(_dirty && !_projecting && _projectionDelay<=0) { _projectionDelay=.5;_=RebuildProjection(); }
        else if(changed) Render();
        if(_snapshot==null) return;
        _poll-=delta;if(_poll>0)return;_poll=.4;
        try { if(CrystalSphereLiveCapture.Fingerprint(_run,GetTree().Root)!=_snapshot.Fingerprint) Invalidate(); }
        catch { Invalidate(); }
    }
    public override void _ExitTree()
    {
        _exited=true;_generation++;_selectionRevision++;_cancel?.Cancel();_cancel?.Dispose();
        _projectCancel?.Cancel();_projectCancel?.Dispose();
        // The launcher may retain the freed overlay wrapper until reopened.
        // Release the potentially large discovery index immediately on close.
        _rows.Clear();_snapshot=null;_reachability=null;_worker=null;_updates=new();_selected=[];
        _projection=new(ImmutableDictionary<CrystalRewardOption,PredictorCrystalSolution>.Empty,null,0);
    }
}
