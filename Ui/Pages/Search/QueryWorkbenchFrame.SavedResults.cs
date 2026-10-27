using RolltheSpire2.Ui.Shell;
using Godot;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private long _observedResultsRevision = -1;
    private Button? _savedResultsEntry;
    private string? _observedResultBatchId;
    internal int ResultCount => _persistence.DisplayResults.Count;

    private bool SyncSavedResults()
    {
        if (_persistence.Runtime.Receiving || _persistence.Runtime.HasManualCandidateValidation ||
            _observedResultsRevision == _persistence.ResultsRevision) return false;
        var saved = _persistence.Workspace;
        if (_observedResultsRevision < 0 || _observedResultBatchId != saved.ResultBatchId)
        {
            _lastProgress = saved.ResultProgress;
            _runEta = null; _predictedRunEta = null; _displayedRunEta = null;
            _currentScanningSpeed = null; _scanningSpeed.Reset();
        }
        _observedResultBatchId = saved.ResultBatchId;
        _observedResultsRevision = _persistence.ResultsRevision;
        UpdateResultNavigation();
        return true;
    }
}
