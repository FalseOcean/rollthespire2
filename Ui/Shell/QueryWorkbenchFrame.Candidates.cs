using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class QueryWorkbenchFrame
{
    private void ValidateCandidate(SearchCandidate candidate)
    {
        if (_session is not null || _manualValidation is not null || _resultPlan is null) return;
        var plan = _resultPlan with { RunOptions = _resultPlan.RunOptions with { SkipExactValidation = false } };
        var input = TrustedRootHashInput.FromCanonicalSeed(_runtime.Profile, candidate.Seed);
        _validatingCandidate = candidate;
        _manualValidation = Task.Run(() =>
        {
            var exact = ProductionExactSearchEvaluator.Evaluate(plan, input);
            if (!exact.IsMatch) ExactRejectionDiagnostics.Log(plan, candidate.Seed, exact);
            return exact;
        });
        RenderResults();
    }

    // Completion is consumed by the existing Godot main-thread poll.
    private void PollCandidateValidation()
    {
        if (_manualValidation is not { IsCompleted: true } task) return;
        var candidate = _validatingCandidate;
        _manualValidation = null; _validatingCandidate = null;
        try
        {
            var exact = task.GetAwaiter().GetResult();
            int index = candidate is null ? -1 : _results.IndexOf(candidate);
            if (index < 0) return; // A different search replaced these results.
            if (exact.IsMatch)
            {
                var confirmed = candidate! with { IsUnverified = false, PredictionRequest = exact.Request!,
                    SnapshotFingerprint = string.Join("|", new[] { candidate!.SnapshotFingerprint,
                        exact.Authority.EffectSnapshotFingerprint, exact.Authority.WorldSnapshotFingerprint }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    Document = exact.Document!, Authority = exact.Authority, MatchEvidence = exact.Evaluation.Evidence,
                    MatchedRouteIds = exact.Evaluation.MatchedRouteIds, Witnesses = exact.Evaluation.Witnesses };
                _results[index] = confirmed;
                _persistence.AppendResult(confirmed, _activeFingerprint); _persistence.FlushAll();
                ReceiptText(_text.Get("workflow.validation_passed"));
            }
            else ReceiptText(_text.Get("workflow.validation_failed") + " " + exact.Evaluation.FailureCode +
                string.Concat(exact.Evaluation.RouteRejections.Select(r => "\n" + r.RouteId + ": " + r.Reason)));
        }
        catch (Exception ex)
        {
            RolltheSpire2.Bootstrap.RuntimeLog.Fault("manualCandidateValidationFailed=true", ex);
            ReceiptText(_text.Get("workflow.validation_failed") + " " + ex.Message);
        }
        RenderResults();
    }
}
