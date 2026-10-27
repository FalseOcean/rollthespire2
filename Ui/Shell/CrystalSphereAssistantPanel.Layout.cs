using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;
using RolltheSpire2.Infrastructure.ContentNames;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class CrystalSphereAssistantPanel
{
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
    private readonly CheckButton _includeDriftwood = new() { ButtonPressed = true };
    private readonly CheckButton _includeEnchantments = new();
    private readonly CheckButton _transparentMask = new();
    private Button _continue = null!, _stop = null!, _use = null!, _allCandidates = null!;
    private const float RewardWidth = WorkspaceResultTile.TileWidth, RewardHeight = 144;
    private const float RewardNameHeight = 52;
    private Label _searchHint = null!;
    private Label _emptyState = null!;
    private ScrollContainer _rewardScroll = null!;
    private Control _statistics = null!;

    private void BuildLayout()
    {
        var workspace = new Control { Name="CrystalAssistantWorkspace", SizeFlagsVertical=SizeFlags.ExpandFill };
        AddChild(workspace);
        VBoxContainer Column(string name, float x, float width)
        {
            var inset = new MarginContainer { Name=name+"Inset", Position=new(x,0),
                Size=new(width,WorkspaceLayout.CanvasHeight) };
            inset.AddThemeConstantOverride("margin_left",WorkspaceLayout.ColumnInset);
            inset.AddThemeConstantOverride("margin_right",WorkspaceLayout.ColumnInset);
            workspace.AddChild(inset);
            var column = new VBoxContainer { Name=name, SizeFlagsHorizontal=SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation",14); inset.AddChild(column);
            return column;
        }
        foreach(float x in new[]{WorkspaceLayout.LeftRailWidth+WorkspaceLayout.RailGap/2,
            WorkspaceLayout.RightRailLeft-WorkspaceLayout.RailGap/2})
        {
            var line=new ColorRect { Position=new(x,WorkspaceLayout.DividerTop),
                Size=new(1,WorkspaceLayout.DividerHeight),MouseFilter=MouseFilterEnum.Ignore };
            MainPanelTheme.SetBackground(line,WorkspacePalette.MainPanel.Line);workspace.AddChild(line);
        }
        Label Text(Node parent,string key,int size=18)
        {
            var label=new Label { Text=T(key),AutowrapMode=TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal=SizeFlags.ExpandFill };
            label.AddThemeFontSizeOverride("font_size",size);parent.AddChild(label);_labels.Add((label,key));return label;
        }
        Button Action(Node parent,string key,System.Action click)
        {
            var button=Button(parent,T(key),click);button.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            _buttons.Add((button,key));return button;
        }
        void Quiet(Button button)
        {
            foreach(string state in new[]{"normal","disabled"})
                button.AddThemeStyleboxOverride(state,new StyleBoxEmpty { ContentMarginLeft=4,ContentMarginRight=4,
                    ContentMarginTop=6,ContentMarginBottom=6 });
            button.AddThemeFontSizeOverride("font_size",17);
        }
        var context=Column("CrystalContext",0,WorkspaceLayout.LeftRailWidth);
        context.AddThemeConstantOverride("separation",6);
        var settingsScroll=new ScrollContainer { SizeFlagsVertical=SizeFlags.ExpandFill,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };
        context.AddChild(settingsScroll);
        var left=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation",8);settingsScroll.AddChild(left);
        Text(left,"scene_title",24);
        Text(left,"choose_count",17);StyleChoice(_branch);_branch.CustomMinimumSize=new(0,44);left.AddChild(_branch);
        _branch.ItemSelected += _=> BeginBranch();
        foreach(var option in new[] { _avoidCurse, _includeDriftwood, _includeEnchantments, _transparentMask })
        {
            Quiet(option);option.CustomMinimumSize=new(0,60);
            option.AutowrapMode=TextServer.AutowrapMode.WordSmart;left.AddChild(option);
        }
        _avoidCurse.Toggled += enabled=>{ _preferences.SetCrystalSphereOptions(avoidCurse:enabled);if(_snapshot!=null) BeginBranch(); };
        _includeDriftwood.Toggled += enabled=>{ _preferences.SetCrystalSphereOptions(includeDriftwood:enabled);if(_snapshot!=null) BeginBranch(); };
        _includeEnchantments.Toggled += enabled=>{ _preferences.SetCrystalSphereOptions(includeEnchantments:enabled);if(_snapshot!=null) BeginBranch(); };
        _transparentMask.Toggled += enabled=>{
            _preferences.SetCrystalSphereOptions(transparentMask:enabled);CrystalSphereMaskAppearance.SetEnabled(enabled);
        };
        Action(left,"refresh",Refresh);
        Text(context,"search_status",20);
        _status=new Label { Text=T("refresh_first"),AutowrapMode=TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size",17);context.AddChild(_status);
        var statistics=new VBoxContainer();statistics.AddThemeConstantOverride("separation",4);context.AddChild(statistics);_statistics=statistics;
        Text(statistics,"search_speed",15);
        _speed=new Label { Text="—",AutowrapMode=TextServer.AutowrapMode.WordSmart };statistics.AddChild(_speed);
        Text(statistics,"elapsed",15);_elapsed=new Label { Text="—" };statistics.AddChild(_elapsed);
        _continue=Action(context,"continue_discovery",StartDiscovery);
        _stop=Action(context,"pause_discovery",()=>_cancel?.Cancel());
        _allCandidates=Action(context,"focus_back",()=>{ SwitchSearchQueue();_focusTarget=null;Render();StartDiscovery(); });
        _allCandidates.Visible=false;

        var center=Column("CrystalRewardCandidates",WorkspaceLayout.CenterLeft,WorkspaceLayout.CenterWidth);
        var heading=new HBoxContainer();center.AddChild(heading);
        var title=Text(heading,"available_rewards",24);title.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var help=new VBoxContainer { Name="CrystalUsage",Visible=false };
        help.AddThemeConstantOverride("separation",8);
        var helpButton=Action(heading,"usage",()=>help.Visible=!help.Visible);helpButton.ToggleMode=true;Quiet(helpButton);
        center.AddChild(help);Text(help,"choose_hint",17);Text(help,"snapshot_premise",15);
        var scroll=new ScrollContainer { SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };center.AddChild(scroll);
        _rewardScroll=scroll;
        _emptyState=new Label { SizeFlagsVertical=SizeFlags.ExpandFill,AutowrapMode=TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        _emptyState.AddThemeFontSizeOverride("font_size",20);
        MainPanelTheme.SetColor(_emptyState,"font_color",WorkspacePalette.MainPanel.Secondary);center.AddChild(_emptyState);
        _results=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };
        _results.AddThemeConstantOverride("separation",22);scroll.AddChild(_results);
        foreach(string group in new[]{"Relic","Rare","Uncommon","Common","Potion"})
        {
            var column=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };column.AddThemeConstantOverride("separation",10);_results.AddChild(column);
            var count=new Label();count.AddThemeFontSizeOverride("font_size",18);column.AddChild(count);
            var flow=new HFlowContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation",10);flow.AddThemeConstantOverride("v_separation",12);column.AddChild(flow);
            _groups.Add(group,(count,flow));
        }
        _searchHint=Text(center,"search_hint",15);
        MainPanelTheme.SetColor(_searchHint,"font_color",WorkspacePalette.MainPanel.Secondary);

        var right=Column("CrystalTargetsAndGuide",WorkspaceLayout.RightRailLeft,WorkspaceLayout.RightRailWidth);
        Text(right,"selected_targets",24);
        var targetScroll=new ScrollContainer { SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };right.AddChild(targetScroll);
        _targetRow=new VBoxContainer { Name="CrystalSelectedTargets",SizeFlagsHorizontal=SizeFlags.ExpandFill };
        _targetRow.AddThemeConstantOverride("separation",10);targetScroll.AddChild(_targetRow);
        _summary=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart };_summary.AddThemeFontSizeOverride("font_size",17);right.AddChild(_summary);
        _use=Action(right,"guide_start",UsePlan);
        var tooltip=new AnchoredTooltipHost(new RuntimeRelicTooltipResolver(),new RuntimePotionTooltipResolver(),new RuntimeCardTooltipResolver());
        workspace.AddChild(tooltip);tooltip.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SearchTooltip.AttachScope(this,tooltip,workspace);
        ApplyOptionText();
    }
    private static Button Button(Node parent,string text,Action clicked)
    {
        var button=WorkspacePalette.MainPanel.Button(text);button.CustomMinimumSize=new(112,48);
        parent.AddChild(button);button.Pressed+=clicked;return button;
    }
    private static void StyleChoice(OptionButton choice)
    {
        var p=WorkspacePalette.MainPanel;choice.CustomMinimumSize=new(0,44);choice.FitToLongestItem=false;
        foreach(var state in new[]{"normal","hover","pressed","disabled","focus"})
            choice.AddThemeStyleboxOverride(state,p.Box(state=="hover"?p.Hover:p.Surface,p.Line,1));
        choice.AddThemeFontSizeOverride("font_size",18);
        choice.AddThemeColorOverride("font_color",p.Color(p.Text));choice.GetPopup().AddThemeFontSizeOverride("font_size",18);
    }
    private static void Clear(Node parent) { foreach(Node child in parent.GetChildren()) { parent.RemoveChild(child);child.QueueFree(); } }
    private static GameContentKind ContentKind(CrystalRewardOption o)=>o.Kind switch {
        PredictorRewardKind.Card=>GameContentKind.Card,PredictorRewardKind.Potion=>GameContentKind.Potion,_=>GameContentKind.Relic };
    private string NameOf(CrystalRewardOption option)=>(option.Key.IsValid?_names.Resolve(option.Key,ContentKind(option)):T("any_relic"))+
        (option.Kind==PredictorRewardKind.Card && option.UpgradeLevel is >0?"+"+(option.UpgradeLevel>1?option.UpgradeLevel.ToString():""):"")+
        (option.Enchantment is {} enchantment?" · "+EnchantmentName(enchantment):"");
    private string EnchantmentName(CrystalCardEnchantment enchantment)=>enchantment.Key is {} key
        ?ModelDb.GetById<EnchantmentModel>(new ModelId(key.Category,key.Entry)).Title.GetFormattedText()+" "+enchantment.Amount
        :T("unenchanted");
    private Control Tile(CrystalRewardOption option,bool selected)
    {
        if(selected || !option.Key.IsValid)
        {
            var button=WorkspacePalette.MainPanel.Button(NameOf(option)+(selected?" ×":""),selected:selected);
            button.CustomMinimumSize=new(0,60);button.SizeFlagsHorizontal=SizeFlags.ExpandFill;
            button.Alignment=HorizontalAlignment.Left;button.AddThemeFontSizeOverride("font_size",18);
            button.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            if(option.Key.IsValid)
            {
                button.Icon=_icons.Resolve(option.Key,ContentKind(option),IconVariant.Small).Texture;
                button.ExpandIcon=true;button.AddThemeConstantOverride("icon_max_width",40);
            }
            button.Pressed+=()=>SelectOption(option);return button;
        }
        var tile=new WorkspaceResultTile(WorkspacePalette.MainPanel,_icons,_names,JsonUiTextProvider.CreateUi13(_language),
            option.Key,ContentKind(option),()=>SelectOption(option),()=>SelectOption(option),selected,NameOf(option),isRequested:selected);
        var palette=WorkspacePalette.MainPanel;
        var rewardButton=tile.GetChild<Button>(0);
        tile.CustomMinimumSize=tile.Size=new(RewardWidth,RewardHeight);
        rewardButton.CustomMinimumSize=rewardButton.Size=tile.Size;
        var artwork=rewardButton.GetChildren().OfType<TextureRect>().Single();
        artwork.Position=new(8,6);artwork.Size=new(RewardWidth-16,76);
        var nameLabel=rewardButton.GetChildren().OfType<Label>().Single();
        nameLabel.Position=new(6,84);nameLabel.Size=new(RewardWidth-12,RewardNameHeight);
        nameLabel.VerticalAlignment=VerticalAlignment.Center;
        nameLabel.AddThemeFontSizeOverride("font_size",15);
        // The shared tile sets its default label bounds on Ready; this local
        // variant then restores its authored bounds after that callback.
        nameLabel.Ready+=()=>nameLabel.SetDeferred(Control.PropertyName.Size,new Vector2(RewardWidth-12,RewardNameHeight));
        foreach(var (state,fill,opacity) in new[]{("normal",palette.Surface,.3f),
            ("hover","243650",.7f),("pressed",palette.SelectedSurface,.7f),("disabled",palette.Surface,.3f)})
        {
            var outline=(StyleBoxFlat)rewardButton.GetThemeStylebox(state).Duplicate();
            outline.BgColor=palette.Color(fill);outline.SetMeta("rt2_color_bg",fill);
            outline.BorderColor=new Color(palette.Color(palette.Line),opacity);
            outline.SetMeta("rt2_color_border",palette.Line);
            outline.BorderWidthLeft=outline.BorderWidthTop=outline.BorderWidthRight=outline.BorderWidthBottom=1;
            rewardButton.AddThemeStyleboxOverride(state,outline);
        }
        if(option.Enchantment is {} enchantment)
        {
            var badge=new Label { Text=EnchantmentName(enchantment),Position=new(5,60),
                Size=new(RewardWidth-43,22),TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter=MouseFilterEnum.Ignore };
            badge.AddThemeFontSizeOverride("font_size",15);badge.AddThemeColorOverride("font_color",new Color("95e9ff"));
            badge.AddThemeColorOverride("font_outline_color",Colors.Black);badge.AddThemeConstantOverride("outline_size",5);tile.AddChild(badge);
        }
        if(option.Kind==PredictorRewardKind.Card && option.UpgradeLevel is >0)
        {
            // Keep the variant visible even when a long localized name truncates.
            var badge=new Label { Text="+"+(option.UpgradeLevel>1?option.UpgradeLevel.ToString():""),
                Position=new(RewardWidth-38,58),Size=new(30,26),
                HorizontalAlignment=HorizontalAlignment.Right,MouseFilter=MouseFilterEnum.Ignore };
            badge.AddThemeFontSizeOverride("font_size",24);
            badge.AddThemeColorOverride("font_color",new Color("8fff98"));
            badge.AddThemeColorOverride("font_outline_color",Colors.Black);
            badge.AddThemeConstantOverride("outline_size",5);tile.AddChild(badge);
        }
        if(option.Kind==PredictorRewardKind.Relic)
        {
            var wing=new TextureRect { Name="CrystalSacrificeBadge",Position=new(3,3),Size=new(24,24),
                Texture=_icons.Resolve(new("RELIC","PAELS_WING"),GameContentKind.Relic,IconVariant.Small).Texture,
                ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter=MouseFilterEnum.Ignore,ZIndex=2,Visible=false };
            tile.AddChild(wing);
        }
        return tile;
    }
    private void RenderChrome()
    {
        if(_results==null) return;
        _branch.Disabled=_snapshot==null || _stale;_avoidCurse.Disabled=_stale;
        _includeDriftwood.Disabled=_stale || _snapshot==null || !PredictorSettlementEffects.Has(Source.State,"DRIFTWOOD");
        _includeEnchantments.Disabled=_stale || _snapshot==null;
        _continue.Disabled=_snapshot==null || _stale || _running || _complete;
        _stop.Disabled=!_running;
        _continue.Visible=_snapshot!=null && !_stale && !_running && !_complete;
        _stop.Visible=_running;
        _statistics.Visible=_snapshot!=null;
        _searchHint.Visible=_snapshot!=null && !_stale && !_candidatesComplete;
        _use.Disabled=_stale || _projection.SelectedPlan==null;
        _summary.Text=_snapshot==null?"":_projection.SelectedPlan==null?T(_selected.IsEmpty?"gold_finding":"checking_joint"):
            _selected.IsEmpty?string.Format(T("gold_ready"),_projection.Gold):
            string.Format(T("joint_ready"),_selected.Length,_projection.Gold);
        if(_projection.SelectedPlan!=null)
            _summary.Text+="\n"+(_projection.GoldComplete?T("gold_optimal"):
                _projection.GoldUpperBound is { } upper?string.Format(T("gold_bound"),upper):T("gold_searching"));
        if(_projection.SelectedPlan is { RerollRewardIndices.Length: >0 } suffixPlan)
            _summary.Text+=string.Format(T("reroll_count"),suffixPlan.RerollRewardIndices.Length);
        _summary.TooltipText=T("plan_scope");
        if(_snapshot==null) { _status.Text=T(_emptyStatusKey);UpdateEmptyState();return; }
        _allCandidates.Visible=_focusTarget!=null;_allCandidates.Disabled=_stale;
        _status.Text=_stale?T("retained_plan"):_errorKey!=null?T(_errorKey):T(_complete ? (_projecting || _dirty ? "discovery_finalizing" : "discovery_exhausted") :
            _running ? (_phase==CrystalDiscoveryPhase.Exhaustive ? "discovery_exhausting" : "discovery_finding") :
            _stopReason switch { DiscoveryStop.TimeLimit=>"discovery_time_limit", DiscoveryStop.UserPaused=>"discovery_user_paused",
                DiscoveryStop.Failed=>"discovery_failed", _=>"discovery_ready" });
        if(!_stale && _errorKey==null && _focusTarget is { } target)
            _status.Text=string.Format(T(_focusVerdict switch {
                CrystalFocusVerdict.Reachable=>"focus_reachable",CrystalFocusVerdict.Unreachable=>"focus_unreachable",
                _=>_running?"focus_checking":"focus_paused" }),NameOf(target));
        else if(!_stale && _errorKey==null && _candidatesComplete && !_projection.GoldComplete)
            _status.Text=T(_running?"gold_optimizing":"gold_paused");
        double seconds=_progress.Seconds+_goldProgress.Seconds;
        _speed.Text=seconds>0?string.Format(T("speed_value"),(_progress.Examined+_goldProgress.Examined)/seconds):"—";
        _elapsed.Text=string.Format(T("elapsed_value"),seconds);
        _status.TooltipText=T("discovery_scope")+(_progress.Examined>0?"\n"+string.Format(T("discovery_progress"),_progress.Examined,
            _progress.Pruned+_progress.Merged,_progress.Orders,_progress.Seconds):"");
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        SetPresentationVisibility();
        _emptyState.Text=_status.Text;
    }

}
