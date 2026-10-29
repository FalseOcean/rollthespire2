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
        var authority = exact?.Authority ?? _plan.Authority;
        var disposition = exact?.Evaluation.Disposition ?? SearchDisposition.NotEvaluatedByPolicy;
        string fingerprint = match ? string.Join("|", new[]
        {
            _plan.SnapshotFingerprint, authority.EffectSnapshotFingerprint, authority.WorldSnapshotFingerprint
        }.Where(value => !string.IsNullOrWhiteSpace(value))) : _plan.SnapshotFingerprint;
        return new(input.CanonicalSeed, _plan.ProfileId, _plan.CharacterKey, _plan.Ascension, fingerprint,
            match ? exact!.Request! : CreateUnobservedCandidateRequest(input, authority),
            match ? exact!.Document : null, authority,
            match ? exact!.Evaluation.Evidence : [], match ? exact!.Evaluation.MatchedRouteIds : [],
            match ? exact!.Evaluation.Witnesses : [])
        {
            ValidationDisposition = disposition,
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

    private SeedPredictionRequest CreateUnobservedCandidateRequest(TrustedRootHashInput input,
        RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot authority)
    {
        if (!SeedPredictionRequest.TryCreateFromRootHash(input,
                RolltheSpire2.Core.Identity.CharacterIdentity.FromKey(_plan.CharacterKey), _plan.Ascension,
                authority.PlayersCount, authority.PlayerSlotIndex, authority,
                _plan.Evaluation.AncientOptionConditions, SeedPredictionDomainSelection.None,
                SeedPredictionInputLimits.DefaultRelicSequencePreviewCount, false, out var request, out var error))
            throw new InvalidOperationException("CandidateContextRejected:" + error);
        return request!;
    }

    private async ValueTask<bool> PublishCandidateAsync(SearchCandidate output, ulong ordinal, CancellationToken cancellationToken)
    {
        int matchNumber = TryReserveMatch();
        if (matchNumber == 0) return false;
        _recovery.BeginDelivery(ordinal);
        try
        {
            if (_beforeCandidatePublish is not null)
                await _beforeCandidatePublish(matchNumber, cancellationToken).ConfigureAwait(false);
            await _candidates.Writer.WriteAsync(output, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _recovery.AbandonDelivery(ordinal);
            Interlocked.Decrement(ref _reservedMatches);
            throw;
        }
        // Count only successfully published outputs, including in diagnostic candidate mode.
        int classificationCount = output.IsUnverified ? Interlocked.Increment(ref _unverifiedCandidates)
            : Interlocked.Increment(ref _confirmedCandidates);
        int published = Interlocked.Increment(ref _matches);
        RecordPublishedCandidate(output, classificationCount);
        if (published >= _plan.TargetMatchCount)
        {
            Interlocked.Exchange(ref _targetReached, 1);
            _lifetime.Cancel();
        }
        return true;
    }

    private bool BatchHasNoFilters(SearchBatch batch) => _families.Count > 0 &&
        _executionContext.CaptureFilterIsolations(batch).Any(i => i.FamilyId == _families[0].FamilyId);
}
