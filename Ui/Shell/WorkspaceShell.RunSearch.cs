using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Components;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private VBoxContainer? _runSearchPage;
    private Label _runSearchStatus = null!;
    private Button _runSearchStop = null!;
    private VBoxContainer _runSearchRows = null!;
    private IUiTextProvider? _runSearchText;
    private string? _runSearchLanguage;
    private long _runSearchRevision = -1;

    private void RefreshRunSearch()
    {
        var store = _persistence!;
        if (_runSearchPage is null)
        {
            _runSearchPage = new VBoxContainer { Name = "RunSearchMonitor" };
            _content.AddChild(_runSearchPage);
            _runSearchPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _runSearchPage.AddThemeConstantOverride("separation", 12);
            var header = new HBoxContainer();
            _runSearchPage.AddChild(header);
            _runSearchStatus = _palette.Label("", 20);
            _runSearchStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _runSearchStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            header.AddChild(_runSearchStatus);
            _runSearchStop = _palette.Button("");
            _runSearchStop.Pressed += () => store.Runtime.Cancel();
            header.AddChild(_runSearchStop);
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            _runSearchPage.AddChild(scroll);
            _runSearchRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddChild(_runSearchRows);
        }

        bool languageChanged = _runSearchLanguage != _languageCode;
        if (languageChanged)
        {
            _runSearchText = JsonUiTextProvider.CreateUi13(_languageCode);
            _runSearchLanguage = _languageCode;
        }
        var text = _runSearchText!;
        var runtime = store.Runtime;
        runtime.MarkBackgroundResultsViewed();
        var progress = runtime.SessionProgress;
        string state = runtime.Receiving ? "workflow.run_search.running" : progress?.State switch
        {
            SearchRunState.Completed => "integration.completed",
            SearchRunState.Faulted => "integration.failed",
            null or SearchRunState.Idle => "workflow.run_search.empty",
            _ => "integration.stopped"
        };
        _runSearchStatus.Text = text.Get(state);
        if (progress is not null)
        {
            _runSearchStatus.Text += $"   {text.Get("workflow.scanned")} {progress.ScannedCount:N0}   " +
                text.Format("workflow.mixed_results_count", progress.MatchCount, progress.UnverifiedCandidateCount);
            if (!string.IsNullOrEmpty(progress.FailureCode)) _runSearchStatus.Text += "\n" + progress.FailureCode;
        }
        _runSearchStop.Text = text.Get("integration.stop");
        _runSearchStop.Visible = runtime.Receiving;
        _runSearchStop.Disabled = progress?.CancellationRequested == true;
        if (!languageChanged && _runSearchRevision == store.ResultsRevision) return;
        _runSearchRevision = store.ResultsRevision;
        foreach (var child in _runSearchRows.GetChildren())
        {
            _runSearchRows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var result in runtime.SessionResults)
        {
            var row = new HBoxContainer();
            var seed = result.Seed;
            var label = _palette.Label(seed + " · " + text.Get(SearchResultValidationDisplay.StatusKey(result)), 18);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            var copy = _palette.Button(text.Get("integration.copy"));
            copy.Pressed += () => DisplayServer.ClipboardSet(seed);
            row.AddChild(copy);
            _runSearchRows.AddChild(row);
        }
    }
}
