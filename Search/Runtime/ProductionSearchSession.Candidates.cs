using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Search.Runtime;

public sealed partial class ProductionSearchSession
{
    private SearchCandidate CreateCandidate(TrustedRootHashInput input, SearchBatch batch,
        ProductionExactSearchResult? exact = null)
    {
        bool match = exact?.IsMatch == true;
        var authority = exact?.Authority ?? _plan.Model.Authority;
        var disposition = exact?.Evaluation.Disposition ?? SearchDisposition.NotEvaluatedByPolicy;
        string fingerprint = match ? string.Join("|", new[]
        {
            _plan.SnapshotFingerprint, authority.EffectSnapshotFingerprint, authority.WorldSnapshotFingerprint
        }.Where(value => !string.IsNullOrWhiteSpace(value))) : _plan.SnapshotFingerprint;
        var evaluation = exact?.Evaluation ?? SearchQueryEvaluation.NotEvaluatedByPolicy("CandidateValidationSkipped");
        return new(input.CanonicalSeed, _plan.Model.ProfileId, _plan.Model.CharacterKey, _plan.Model.Ascension, fingerprint,
            authority, evaluation)
        {
            Facts = exact?.Facts,
            ValidationIssue = exact is null ? "CandidateValidationSkipped" : match ? "" : ValidationIssue(exact),
            IsUnfiltered = exact is null && (ExecutionPlan.AllConditionsUnfiltered || BatchHasNoFilters(batch)),
            ExecutionWarnings = BatchExecutionWarnings(batch),
            MapSearch = CaptureMapSearch(batch.BatchBase, checked(batch.BatchBase + (ulong)batch.BatchCandidateCount))
        };
    }

    private static string ValidationIssue(ProductionExactSearchResult exact)
    {
        var evaluation = exact.Evaluation;
        string issue = string.IsNullOrWhiteSpace(evaluation.FailureCode) ? exact.FailureCode : evaluation.FailureCode;
        if (string.IsNullOrWhiteSpace(issue)) issue = "Validation." + evaluation.Disposition;
        if (evaluation.RouteRejections.Count > 0)
            issue += "\n" + string.Join("\n", evaluation.RouteRejections.Select(r =>
                $"route={r.RouteId};disposition={r.Disposition};reason={r.Reason}"));
        foreach (var scope in evaluation.Evidence.Where(e => e.Code == "Party.ChildEvaluationScope").Take(4))
            issue += $"\nsource=P{scope.Ordinal};character={scope.RelatedKey}";
        return issue;
    }

    private async ValueTask<bool> PublishCandidateAsync(TrustedRootHashInput input, SearchBatch batch,
        ulong ordinal, CancellationToken cancellationToken, ProductionExactSearchResult? exact = null,
        bool modeledNonHitSample = false)
    {
        int matchNumber = modeledNonHitSample ? 0 : TryReserveMatch();
        if ((!modeledNonHitSample && matchNumber == 0) || !_recovery.BeginDelivery(ordinal, modeledNonHitSample)) return false;
        SearchCandidate output;
        try
        {
            output = CreateCandidate(input, batch, exact);
            if (_beforeCandidatePublish is not null && !modeledNonHitSample)
                await _beforeCandidatePublish(matchNumber, cancellationToken).ConfigureAwait(false);
            await _candidates.Writer.WriteAsync(output, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _recovery.AbandonDelivery(ordinal);
            if (!modeledNonHitSample) Interlocked.Decrement(ref _reservedMatches);
            throw;
        }
        // Count only successfully published outputs, including in diagnostic candidate mode.
        int classificationCount = output.IsUnverified ? Interlocked.Increment(ref _unverifiedCandidates)
            : Interlocked.Increment(ref _confirmedCandidates);
        Interlocked.Increment(ref _matches);
        if (modeledNonHitSample) Interlocked.Increment(ref _modeledNonHitSampleCount);
        RecordPublishedCandidate(output, classificationCount);
        if (!modeledNonHitSample && Interlocked.Increment(ref _targetCandidates) >= _plan.TargetMatchCount)
        {
            Interlocked.Exchange(ref _targetReached, 1);
            _lifetime.Cancel();
        }
        return true;
    }

    private bool BatchHasNoFilters(SearchBatch batch) => _families.Count > 0 &&
        _executionContext.CaptureFilterIsolations(batch).Any(i => i.FamilyId == _families[0].Body.FamilyId);
}
