using Godot;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private long _observedResultsRevision = -1;
    private Button? _savedResultsEntry;
    private string? _observedResultBatchId;
    internal int ResultCount => _persistence.DisplayResults.Count;
    internal event Action? ResultsChanged;
    internal event Action<PersistedSearchResult>? OpenSavedResultRequested;

    private bool SyncSavedResults()
    {
        if (_session is not null || _manualValidation is not null ||
            _observedResultsRevision == _persistence.ResultsRevision) return false;
        var saved = _persistence.Workspace;
        if (_observedResultsRevision < 0 || _observedResultBatchId != saved.ResultBatchId)
        {
            _results.Clear();
            _resultPlan = null;
            _lastDiagnostics = null;
            _lastProgress = saved.ResultProgress;
            _runEta = null; _predictedRunEta = null; _displayedRunEta = null;
            _currentScanningSpeed = null; _scanningSpeed.Reset();
            ResultDraft = saved.ResultDraft;
            _runningPartyDraft = saved.ResultDraft is { Players.Count: > 0 } party ? party : null;
            _resultSeedContext = saved.ResultContext;
            _activeFingerprint = saved.QueryFingerprint;
        }
        _observedResultBatchId = saved.ResultBatchId;
        _observedResultsRevision = _persistence.ResultsRevision;
        UpdateResultNavigation();
        return true;
    }

    private Button FavoriteSavedResultButton(PersistedSearchResult result)
    {
        var button = _p.Button(_language == "zh" ? "收藏" : "Favorite");
        button.CustomMinimumSize = new(72, 32); button.AddThemeFontSizeOverride("font_size", 14);
        button.Disabled = result.Context is null || result.IsUnverified;
        if (button.Disabled) button.TooltipText = _text.Get(result.Context is null
            ? "workflow.results.missing_context" : "workflow.results.validation_unavailable");
        button.Pressed += () =>
        {
            try { FavoriteSeedRequested?.Invoke(result.Seed, result.Context!, _persistence.ResultAssociation(result.Seed)); }
            catch (Exception ex) { ReceiptText(ex.Message); }
        };
        return button;
    }
}
