using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Ui.Components;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private long _observedManualReceiptRevision;
    private void ValidateCandidate(SearchResultAction action)
    {
        if (_persistence.Runtime.HasSearchInFlight) return;
        try { _persistence.Runtime.Validate(action, _runtime, IsInsideTree() ? GetTree() : null); }
        catch (Exception exception)
        {
            _persistence.RecordPresentationFailure(exception, "ManualValidationSetup", action.Result.Seed);
            ReceiptText(_text.Get("workflow.validation_failed") + (_language == "zh"
                ? " 原结果与待办已保留；仅能在原查询和兼容环境下检查。\n"
                : " Original results and pending work are retained; validation requires the original query and a compatible environment.\n") + exception.Message);
        }
        TryAuxiliary(RenderResults, "ManualValidationPresentation");
    }
    private void PollCandidateValidation()
    {
        var snapshot = _persistence.Runtime.Snapshot;
        if (_observedManualReceiptRevision == snapshot.ReceiptRevision || snapshot.Receipt is not { } receipt) return;
        _observedManualReceiptRevision = snapshot.ReceiptRevision;
        if (receipt.SaveOnly)
            ReceiptText(receipt.Accepted ? (_language == "zh" ? "已保存原校验结果；未重复校验。" : "The original validation result was saved without rechecking.")
                : _text.Get("workflow.unsaved_search"));
        else if (!receipt.ObservationReceived) ReceiptText(_text.Get("workflow.unsaved_search"));
        else if (receipt.FailureMessage.Length > 0) ReceiptText(_text.Get("workflow.validation_failed") + " " + receipt.FailureMessage);
        else ReceiptText(receipt.IsMatch ? _text.Get("workflow.validation_passed") :
            _text.Get(SearchResultValidationDisplay.StatusKey(receipt.Disposition ?? SearchDisposition.NotEvaluatedByPolicy)) + "\n" + receipt.FailureCode);
        if (!receipt.Accepted && receipt.ObservationReceived && !receipt.SaveOnly)
            ReceiptText((_status?.Text ?? "") + "\n" + _text.Get("workflow.unsaved_search"));
        TryAuxiliary(RenderResults, "ManualValidationPresentation");
    }

}
