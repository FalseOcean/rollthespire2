using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private void TryAuxiliary(Action action, string stage)
    { try { action(); _persistence.ResolvePresentationFailure(stage); } catch (Exception exception) { AuxiliaryFailure(exception, stage); } }
    private void AuxiliaryFailure(Exception exception, string stage)
    {
        _persistence.RecordPresentationFailure(exception, stage);
        // Store the issue even if the label itself is no longer renderable.
        try { PresentSearchStatus(_lastProgress); } catch { }
    }
    private void PresentSearchStatus(SearchProgressSnapshot? progress)
    {
        if (_status is null) return;
        var lines = new List<string>();
        if (progress is not null)
        {
            if (!_persistence.Runtime.Receiving)
                lines.Add(_text.Get(progress.State == SearchRunState.Faulted ? "integration.failed" :
                    progress.State == SearchRunState.Cancelled ? "integration.stopped" : "integration.completed"));
            if (!string.IsNullOrEmpty(progress.FailureCode)) lines.Add(
                progress.FailureCode.StartsWith("SearchRecoveryCapacityReached:", StringComparison.Ordinal)
                    ? _text.Get("workflow.recovery_capacity") : progress.FailureCode);
            if (!string.IsNullOrEmpty(progress.CleanupIssue)) lines.Add(_text.Get("workflow.cleanup_issue"));
        }
        int pending = _persistence.DisplayPendingCandidates.Count;
        if (pending > 0) lines.Add(_text.Format("workflow.pending_count", pending));
        int historical = _persistence.PendingHistories.Sum(history => global::RolltheSpire2.Ui.Workspace.SearchWorkspace.PendingForHistory(history).Count) - pending;
        if (historical > 0) lines.Add(_text.Format("workflow.pending_historical_count", historical));
        if (_persistence.HasUnsavedSearchState) lines.Add(_text.Get("workflow.unsaved_search"));
        if (progress?.ExecutionWarnings.Count > 0) lines.Add(_text.Get("workflow.execution_warning"));
        if (_persistence.Workspace.AuxiliaryIssues.LastOrDefault() is { } auxiliaryIssue)
            lines.Add(_text.Get("workflow.auxiliary_issue") + " " + auxiliaryIssue.Split('\n')[0].TrimEnd('\r'));
        _status.Text = string.Join("\n", lines);
    }
    private void AddFailureDetails(VBoxContainer parent, string caption, string details)
    {
        var toggle = _p.Button(caption + " · " + _text.Get("workflow.failure_details"));
        toggle.AddThemeFontSizeOverride("font_size", 14);
        var body = _p.Label(details, 14, true);
        body.Visible = false; body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        toggle.Pressed += () => body.Visible = !body.Visible;
        parent.AddChild(toggle); parent.AddChild(body);
    }
    private void RenderPendingCandidates(VBoxContainer parent)
    {
        // Pending execution faults are separate evidence, never results or target credit.
        if (_persistence.Workspace.ResultProgress is { } progress)
        {
            if (progress.ExecutionWarnings.Count > 0)
                AddFailureDetails(parent, _text.Get("workflow.execution_warning"), string.Join("\n", progress.ExecutionWarnings));
            if (!string.IsNullOrEmpty(progress.CleanupIssue))
                AddFailureDetails(parent, _text.Get("workflow.cleanup_issue"), progress.CleanupIssue);
        }
        if (_persistence.Workspace.AuxiliaryIssues.Count > 0)
            AddFailureDetails(parent, _text.Get("workflow.auxiliary_issue"), string.Join("\n\n", _persistence.Workspace.AuxiliaryIssues));
        var histories = _persistence.PendingHistories;
        if (histories.Count == 0 && _persistence.DisplayPendingCandidates.Count == 0) return;
        var title = _p.Label(_text.Get("workflow.pending_heading"), 18);
        parent.AddChild(title);
        if (histories.Count > 0)
            foreach (var history in histories)
                foreach (var pending in global::RolltheSpire2.Ui.Workspace.SearchWorkspace.PendingForHistory(history))
                    AddPending(pending, history.QueryKey + " / " + history.EnvironmentFingerprint, history);
        else foreach (var pending in _persistence.DisplayPendingCandidates) AddPending(pending, "", null);
        void AddPending(SearchPendingCandidate pending, string history, Persistence.QueryHistoryEntry? owner)
        {
            AddFailureDetails(parent, pending.Seed + " · " + pending.Stage + " · " + pending.FailureCode,
                history + "\n" + pending.ExceptionText + "\nquery=" + pending.QueryFingerprint +
                "\nsnapshot=" + pending.SnapshotFingerprint + "\nfirstFailed=" + pending.FirstFailedAtUtc.ToString("O") +
                "; attempts=" + pending.Attempts);
            if (pending.Stage != "ManualCandidateCheck" || owner is null) return;
            bool delivery = _persistence.HasManualPendingObservation(owner.Id, pending);
            var retry = _p.Button(delivery ? (_language == "zh" ? "重试保存" : "Retry save") : _text.Get("workflow.validate"));
            retry.Name = "RetryManualPending_" + pending.Ordinal;
            retry.AddThemeFontSizeOverride("font_size", 14);
            retry.Disabled = _persistence.Runtime.Receiving || _persistence.Runtime.HasManualCandidateValidation ||
                _persistence.Runtime.HasSearchInFlight || _persistence.Runtime.HasManualCandidateDelivery(owner.Id, pending.Seed);
            var action = _persistence.PendingResultAction(owner, pending);
            retry.Disabled |= action is null;
            if (action is not null) retry.Pressed += () => ValidateCandidate(action);
            parent.AddChild(retry);
        }
    }
}
