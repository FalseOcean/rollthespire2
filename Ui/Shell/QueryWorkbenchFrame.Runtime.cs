using Godot;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private Control? _runtimeSurface;
    private readonly Label?[] _runValues = new Label?[4];
    private SearchProgressSnapshot? _lastProgress;
    private FamilySearchEtaProjectionV1? _runEta;
    private readonly SearchScanningSpeed _scanningSpeed = new();
    private double? _currentScanningSpeed;
    private SearchEtaQuickView? _predictedRunEta, _displayedRunEta;

    private void BeginRuntimeProgress(FamilySearchEtaProjectionV1 eta, SearchProgressSnapshot progress)
    {
        _runEta = eta;
        _predictedRunEta = SearchEtaPresentationBuilder.Build(eta);
        _scanningSpeed.Reset();
        ObserveRuntimeProgress(progress);
    }

    private void ObserveRuntimeProgress(SearchProgressSnapshot progress)
    {
        _lastProgress = progress;
        double? rolling = _scanningSpeed.Observe(progress.ElapsedSeconds, progress.ScannedCount);
        _currentScanningSpeed = SearchScanningSpeed.Terminal(progress, rolling);
        // This is presentation over the frozen session quote. Cold startup, stalls
        // without a positive rate, and terminal states use the predicted view.
        _displayedRunEta = _predictedRunEta is { } predicted
            ? SearchEtaPresentationBuilder.Live(predicted, progress, _currentScanningSpeed)
            : null;
    }

    private void BuildRuntimeSurface()
    {
        var runtimeColumn = new VBoxContainer { Name="SearchRuntime", Position=new(CenterLeft+8,CenterTop),
            Size=new(CenterWidth-16,DockTop-CenterTop-12),Visible=_showResults };
        runtimeColumn.AddThemeConstantOverride("separation", 16);
        _runtimeSurface = runtimeColumn;
        AddChild(_runtimeSurface);
        string[] keys=["workflow.current_speed","workflow.scanned","workflow.elapsed","workflow.first_eta"];
        var metrics = new HBoxContainer(); metrics.AddThemeConstantOverride("separation", 10); runtimeColumn.AddChild(metrics);
        for(int i=0;i<keys.Length;i++)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface)); metrics.AddChild(panel);
            var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 6); panel.AddChild(column);
            var title=_p.Label(_text.Get(keys[i]),14,true);
            title.ClipText = true; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            title.TooltipText = title.Text; title.MouseFilter = MouseFilterEnum.Pass; column.AddChild(title);
            var value=_p.Label("—",23); value.CustomMinimumSize = new(0, 36);
            value.ClipText = true; value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            value.MouseFilter = MouseFilterEnum.Pass; column.AddChild(value); _runValues[i]=value;
        }
        _resultSurface=new ScrollContainer {SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        _runtimeSurface.AddChild(_resultSurface);
        _resultRows=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};
        _resultRows.AddThemeConstantOverride("separation",10);_resultSurface.AddChild(_resultRows);
        // Planner diagnostics remain available in logs; omit the advanced UI for now.
        UpdateRuntimeStats();
    }
    private void UpdateRuntimeStats()
    {
        var progress=_lastProgress;
        if(_showResults) {
            PresentExpectations(_displayedRunEta?.FirstResultMeanMs,_runEta?.AcceptedResultProbability,EtaRate(_runEta));
        }
        string[] values=[Rate(_currentScanningSpeed),progress is null?"0":CompactNumber(progress.ScannedCount),
            Duration(progress is null?0:progress.ElapsedSeconds*1000),Duration(_displayedRunEta?.FirstResultMeanMs)];
        for(int i=0;i<values.Length;i++) if(_runValues[i] is { } label && GodotObject.IsInstanceValid(label))
        { label.Text=values[i]; label.TooltipText=values[i]; }
    }
}
