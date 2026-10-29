using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Ui.Theme;
namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private sealed record FamilyEntry(string Id,string Title,double? Probability,string Speed,string Detail,string Source = "",bool JointlyAttributed = false);
    private readonly List<FamilyEntry> _familyEntries=[];
    private VBoxContainer? _familyRows, _estimateContent;
    private GridContainer? _liveReadings;
    private Label? _liveReadingsHeading;
    private string FamilyTitle(string id)=>id == "N/R" ? _text.Get("workflow.family.nr_joint") : _text.Get("workflow.family."+id.ToLowerInvariant());
    private static string DashboardRarity(double? probability)=>Rarity(probability);
    private readonly Label?[] _expectationValues = new Label?[3];
    private Label? _expectationNote, _familyHeading;
    private string _expectationContext = "";

    private void BuildSearchSidebar()
    {
        var sidebar = new VBoxContainer { Name = "SearchInformationRail", Position = new(RightRailLeft + ColumnInset, 0),
            Size = new(RightRailWidth - ColumnInset * 2, CanvasHeight) };
        sidebar.AddThemeConstantOverride("separation", 14);
        AddChild(sidebar);
        sidebar.AddChild(_p.Label(_text.Get("layout.settings"), 17, heading: true));
        var controls = new HBoxContainer { Name = "SearchSettingsFields" }; controls.AddThemeConstantOverride("separation", 12); sidebar.AddChild(controls);
        VBoxContainer Field(string title)
        {
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 6); controls.AddChild(column);
            column.AddChild(_p.Label(title, 15, true)); return column;
        }
        var target = Field(_persistence.Preferences.SkipExactValidation ? _text.Get("workflow.candidate_target") : (_language == "zh" ? "结果数量" : "Results"));
        _targetCount = new SpinBox { MinValue = 1, MaxValue = 1000, Value = _target,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 38) };
        _targetCount.AddThemeFontSizeOverride("font_size", 17);
        SearchTooltip.Set(_targetCount, _text.Get("workflow.result_target_hint"));
        SearchTooltip.Set(_targetCount.GetLineEdit(), _text.Get("workflow.result_target_hint"));
        _targetCount.ValueChanged += v => { _target = (int)v; _analysisKey = ""; }; target.AddChild(_targetCount);
        var plan = Field(_text.Get("workflow.plan")); plan.SizeFlagsStretchRatio = 1.4f;
        _planSelector = ContextOptions(0, 0, 144); _planSelector.Reparent(plan, false);
        _planSelector.SizeFlagsHorizontal = SizeFlags.ExpandFill; _planSelector.CustomMinimumSize = new(0, 38);
        _planSelector.AddItem(_text.Get("workflow.plan.auto")); _planSelector.AddItem(_text.Get("workflow.plan.cpu"));
        _planSelector.Select(_persistence.Preferences.SearchMode == "CPU" ? 1 : 0);
        _planSelector.ItemSelected += index => { _persistence.SetWorkbenchSearchMode(index == 1 ? "CPU" : "Auto"); _analysisKey = ""; };
        BuildExecutionOrder(sidebar);
        _start = _p.Button(_text.Get("query.action.start"), primary: true); _start.CustomMinimumSize = new(0, 46);
        _start.Name = "StartSearch";
        // The main action uses a blue surface; gold remains authored-condition ink.
        _start.AddThemeStyleboxOverride("normal", _p.Box(_p.SelectedSurface, _p.Line, 1));
        _start.Pressed += () => { if (_persistence.Runtime.Receiving) _persistence.Runtime.Cancel(); else StartSearch(); }; sidebar.AddChild(_start);
        _savedResultsEntry = _p.CompactButton("", 44, 18);
        _savedResultsEntry.Name = "ViewLastSearchResults";
        _p.SetActive(_savedResultsEntry, false, horizontalPadding: 8);
        _savedResultsEntry.Pressed += ShowLastResults;
        sidebar.AddChild(_savedResultsEntry);
        if (_persistence.Preferences.SkipExactValidation)
        {
            var notice = _p.Label(_text.Get("workflow.candidate_mode"), 15, true);
            notice.AutowrapMode = TextServer.AutowrapMode.WordSmart; sidebar.AddChild(notice);
        }

        var scroll = new ScrollContainer { Name = "SearchInformationScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        sidebar.AddChild(scroll);
        var content = new VBoxContainer { Name = "SearchEstimateContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 16); scroll.AddChild(content); _estimateContent = content;
        content.AddChild(new RolltheSpire2.Ui.Components.GpuInitializationPanel(_language, () => HasActiveSearch, compact: true));
        content.AddChild(_p.Label(_text.Get("layout.estimates"), 17, heading: true));
        var statistics = new PanelContainer { Name = "SearchEstimateStatistics" };
        var statisticsStyle = _p.ContentBox(0);
        statisticsStyle.ContentMarginLeft = statisticsStyle.ContentMarginRight = 12;
        statisticsStyle.ContentMarginTop = statisticsStyle.ContentMarginBottom = 12;
        statistics.AddThemeStyleboxOverride("panel", statisticsStyle); content.AddChild(statistics);
        var values = new VBoxContainer(); values.AddThemeConstantOverride("separation", 10); statistics.AddChild(values);
        var secondaryValues = new HBoxContainer(); secondaryValues.AddThemeConstantOverride("separation", 12);
        string[] keys = ["workflow.total_rarity", "workflow.expected_speed", "workflow.eta"];
        for (int i = 0; i < keys.Length; i++)
        {
            var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 3);
            if (i == 0) values.AddChild(row); else secondaryValues.AddChild(row);
            var caption = _p.Label(_text.Get(keys[i]), 14, true);
            caption.SizeFlagsHorizontal = SizeFlags.ExpandFill; caption.VerticalAlignment = VerticalAlignment.Center;
            caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            SearchTooltip.Set(caption, _text.Get(keys[i] + "_hint")); caption.MouseFilter = MouseFilterEnum.Pass; row.AddChild(caption);
            var value = _p.Label("—", i == 0 ? 26 : 18);
            value.Name = "SearchEstimateValue" + i;
            value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            value.MouseFilter = MouseFilterEnum.Pass; row.AddChild(value); _expectationValues[i] = value;
        }
        values.AddChild(secondaryValues);
        _expectationNote = _p.Label("", 14, true); _expectationNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _expectationNote.SizeFlagsHorizontal = SizeFlags.ExpandFill; content.AddChild(_expectationNote);
        _familyHeading = _p.Label(_text.Get("workflow.family_estimates"), 17);
        content.AddChild(_familyHeading);
        _familyRows=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};
        _familyRows.AddThemeConstantOverride("separation",14);content.AddChild(_familyRows);
        PresentExpectations(null, null, null);
        RenderFamilyDashboard();
        UpdateResultNavigation();
    }

    private void PresentExpectations(double? time, double? probability, double? rate)
    {
        if (_showResults ? _persistence.ResultBatch?.Start?.Plan.RunOptions.SkipExactValidation == true : _persistence.Preferences.SkipExactValidation)
        { time = null; rate = null; }
        string[] values = [probability is null ? (_language == "zh" ? "未知" : "Unknown") : Rarity(probability), Rate(rate), Duration(time)];
        for (int i = 0; i < values.Length; i++)
            if (_expectationValues[i] is { } label) { label.Text = values[i]; SearchTooltip.Set(label, values[i]); }
        if (_expectationNote is not null)
        { _expectationNote.Text = _expectationContext; _expectationNote.Visible = _expectationContext.Length > 0; }
    }
    private void RenderFamilyDashboard()
    {
        if(_familyRows is null) return;
        foreach(var child in _familyRows.GetChildren()){_familyRows.RemoveChild(child);child.QueueFree();}
        if(_draftCompileIssue.Length>0) {
            var issue=_p.Label(_draftCompileIssue,14,true);issue.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            issue.SizeFlagsHorizontal=SizeFlags.ExpandFill;_familyRows.AddChild(issue);
        }
        if (_familyEntries.Count == 0 && _draftCompileIssue.Length == 0)
        {
            var empty = _p.Label(_probabilityPending
                ? (_language == "zh" ? "正在更新条件估算…" : "Updating condition estimates…")
                : _text.Get("workflow.conditions.empty"), 15, true);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; _familyRows.AddChild(empty);
        }
        foreach(var entry in _familyEntries.OrderBy(e=>"NACERTMVWS".IndexOf(e.Id[0])))
        {
            var row = new VBoxContainer
            {
                Name = "ConditionEstimate_" + entry.Id.Replace('/', '_'),
                CustomMinimumSize = new(0, 30),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore
            };
            row.AddThemeConstantOverride("separation", 4);
            _familyRows.AddChild(row);
            var title = _p.Label(entry.Title, 17);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(title);
            // One reading line: probability is primary, the measured backend rate secondary.
            // Both can wrap within their own width; neither steals width from the family name.
            var reading = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            reading.AddThemeConstantOverride("separation", 12); row.AddChild(reading);
            bool undisplayed = entry.JointlyAttributed && entry.Probability is null;
            var rarity = _p.Label(undisplayed ? (_language == "zh" ? "未单列" : "Not itemized") : entry.Probability is null
                ? (_language == "zh" ? "未知" : "Unknown") : DashboardRarity(entry.Probability), 19);
            rarity.Name = "FamilyRarity";
            rarity.VerticalAlignment = VerticalAlignment.Center;
            rarity.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rarity.SizeFlagsStretchRatio = 1.35f;
            rarity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rarity.MouseFilter = MouseFilterEnum.Ignore; reading.AddChild(rarity);
            var speed = _p.Label(entry.Speed, 14, true);
            speed.Name = "FamilyCheckSpeed";
            speed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            speed.VerticalAlignment = VerticalAlignment.Center;
            speed.HorizontalAlignment = HorizontalAlignment.Right;
            speed.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            speed.MouseFilter = MouseFilterEnum.Ignore; reading.AddChild(speed);
            // These families can combine several editing sources. The other titles
            // already identify their source without repeating it on every row.
            if (entry.Source.Length > 0 && (entry.Id is "W" or "R" or "T" or "N/R" || entry.JointlyAttributed))
            {
                var source = _p.Label(entry.Source, 14, true);
                source.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                source.MouseFilter = MouseFilterEnum.Ignore; row.AddChild(source);
            }
        }
    }
}
