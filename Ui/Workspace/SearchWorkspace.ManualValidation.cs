using RolltheSpire2.Ui.Persistence;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Core.Authority;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    internal bool TryManualValidationSource(string seed, out QueryHistoryEntry history,
        out PersistedSearchResult result, out string issue, string? historyId = null, SearchPendingCandidate? pending = null)
    {
        history = (historyId is null ? ActiveHistory : _workspace.QueryHistory.FirstOrDefault(record => record.Id == historyId))!;
        var recoveries = (history?.ManualValidationCandidates ?? []).Where(recovery => recovery.Original.Seed == seed &&
            (pending is null || recovery.Continuation.PendingCandidates.Any(candidate => SameManualPending(candidate, pending)))).ToArray();
        result = (recoveries.Length == 1 ? recoveries[0].Original :
            history?.Results.FirstOrDefault(row => row.Seed == seed) ??
            (history?.Id == _workspace.ActiveHistoryId ? DisplayResults.FirstOrDefault(row => row.Seed == seed) : null))!;
        var environment = history?.Draft is not null ? EnvironmentComparison(history) : EnvironmentComparison(null);
        issue = "";
        if (recoveries.Length > 1) issue = "ManualValidationPendingSourceAmbiguous";
        else if (environment.Status != SearchEnvironmentComparisonStatus.Same)
            issue = "ManualValidationOriginalEnvironment" + environment.Status + ":" + string.Join(";", environment.Reasons);
        else if (history is null || history.Draft is null || history.IdentityVersion != 2 || !history.EnvironmentKnown ||
            history.EnvironmentFingerprint != QueryHistoryIdentity.EnvironmentKey(history.Draft, _environment.Fingerprint))
            issue = "ManualValidationOriginalEnvironmentUnavailable";
        else if (result is null || string.IsNullOrWhiteSpace(result.QueryFingerprint) || history.Draft is null ||
            (result.Context ?? history.Context) is null)
            issue = "ManualValidationOriginalContextUnavailable";
        else if (pending is not null && (pending.Stage != "ManualCandidateCheck" ||
            !PendingForHistory(history).Any(candidate => SameManualPending(candidate, pending)) ||
            result.QueryFingerprint != pending.QueryFingerprint))
            issue = "ManualValidationOriginalPendingMismatch";
        else if (recoveries.SingleOrDefault()?.ReceivedObservation is not null)
            issue = "ManualValidationObservationAwaitingDelivery";
        return issue.Length == 0;
    }

    private static bool SameManualPending(SearchPendingCandidate left, SearchPendingCandidate right) =>
        left.Ordinal == right.Ordinal && left.Seed == right.Seed && left.Stage == right.Stage && left.QueryFingerprint == right.QueryFingerprint &&
        left.SnapshotFingerprint == right.SnapshotFingerprint;

    internal SearchContinuationSnapshot? ManualValidationContinuation(QueryHistoryEntry history, string seed,
        string semanticFingerprint)
    {
        history.ManualValidationSources ??= [];
        var recovery = history.ManualValidationCandidates.Where(item => item.Original.Seed == seed &&
            item.Original.QueryFingerprint == semanticFingerprint).ToArray();
        if (recovery.Length > 1) throw new InvalidOperationException("ManualValidationPendingSnapshotAmbiguous");
        if (recovery.Length == 1) return recovery[0].Continuation;
        var source = history.ManualValidationSources.FirstOrDefault(identity => identity.Version is 1 or 2 &&
            identity.SemanticFingerprint == semanticFingerprint && identity.QueryKey == history.QueryKey);
        var matches = history.RetainedContinuations.Concat(history.Continuation is { } current ? [current] : [])
            .Where(snapshot => snapshot.SemanticFingerprint == semanticFingerprint || source is not null &&
                history.ManualValidationSources.Any(identity => identity.Version == source.Version && identity.QueryKey == source.QueryKey &&
                    identity.SemanticFingerprint == snapshot.SemanticFingerprint && identity.SnapshotFingerprint == snapshot.SnapshotFingerprint &&
                    identity.PayloadSignature == source.PayloadSignature && (identity.Version == 2 || identity.Authorities.SequenceEqual(source.Authorities)))).ToArray();
        var pending = matches.Where(snapshot => snapshot.PendingCandidates.Any(candidate => candidate.Seed == seed &&
            candidate.Stage == "ManualCandidateCheck")).ToArray();
        if (pending.Length > 1) throw new InvalidOperationException("ManualValidationPendingSnapshotAmbiguous");
        if (pending.SingleOrDefault() is not { } legacy) return null;
        var obligation = legacy.PendingCandidates.Single(candidate => candidate.Seed == seed && candidate.Stage == "ManualCandidateCheck");
        return legacy with { NextOrdinal = obligation.Ordinal, PendingCandidates = [obligation], PublishedOrdinals = [] };
    }

    internal static bool ProvesManualValidationSource(QueryHistoryEntry history, PersistedSearchResult original, CompiledSearch compiled,
        CombatRewardRoutePolicyContract? policy = null)
    {
        var sources = (history.ManualValidationSources ?? []).Where(identity =>
            identity.SemanticFingerprint == original.QueryFingerprint && identity.QueryKey == history.QueryKey).ToArray();
        return sources.Length == 0 ? compiled.SemanticFingerprint == original.QueryFingerprint :
            sources.Any(identity => identity.Proves(compiled, history.QueryKey, policy));
    }

    internal (PersistedSearchResult Original, SearchContinuationSnapshot? Continuation, SearchStartContext Start, SearchResultAction Action)
        PrepareManualValidation(QueryHistoryEntry history, PersistedSearchResult original,
        ExactSearchExecutionRequest plan, SearchResultAction? action)
    {
        if (Runtime.HasSearchInFlight) throw new InvalidOperationException("ManualValidationAlreadyRunning");
        if (Runtime.HasManualCandidateDelivery(history.Id, original.Seed)) throw new InvalidOperationException("ManualValidationAwaitingDelivery");
        if (!ProvesManualValidationSource(history, original, plan.CompiledSearch, plan.CombatRewardRoutePolicy))
            throw new InvalidOperationException("ManualValidationOriginalAuthorityMismatch");
        if (!TryManualValidationSource(original.Seed, out var selected, out _, out var sourceIssue, history.Id) || selected.Id != history.Id)
            throw new InvalidOperationException(sourceIssue.Length > 0 ? sourceIssue : "ManualValidationOriginalHistoryMismatch");
        var source = ManualValidationContinuation(history, original.Seed, original.QueryFingerprint);
        if (source?.PendingCandidates.Any(candidate => candidate.Seed == original.Seed) != true) source = null;
        var frozen = System.Text.Json.JsonSerializer.Deserialize<PersistedSearchResult>(
            System.Text.Json.JsonSerializer.Serialize(original, _json), _json)!;
        var identity = ManualValidationSourceIdentity.Capture(plan, history.QueryKey);
        history.ManualValidationSources ??= [];
        PruneManualValidationSources(history);
        history.ManualValidationSources.RemoveAll(sourceIdentity => sourceIdentity.SemanticFingerprint == identity.SemanticFingerprint &&
            sourceIdentity.SnapshotFingerprint == identity.SnapshotFingerprint);
        history.ManualValidationSources.Add(identity);
        MarkWorkspaceDirty();
        action ??= ResultAction(frozen, history.Id);
        var start = new SearchStartContext(action.Draft ?? FreezeDraft(history.Draft), plan, _environment.Snapshot(), history.QueryKey,
            history.EnvironmentFingerprint, history.Id, action.BatchId, DateTimeOffset.UtcNow,
            frozen.Context ?? history.Context, new(plan, source, ""));
        if (!_processBatches.ContainsKey(history.Id))
            _processBatches[history.Id] = new(action.BatchId, history.Id, start.Draft, start.Context, history.Results);
        return (frozen, source, start, action with { ValidationPlan = plan });
    }

    internal void AcceptManualCandidateValidation(SearchWorkspaceRuntime.ManualCandidateValidation operation)
    {
        if (operation.Accepted || !operation.ExecutionObserved) return;
        if (operation.Failure is not null)
        {
            FlushWorkspace();
            operation.Accepted = !HasUnsavedSearchState;
            return;
        }
        if (operation.ObservationReceived)
        {
            FinishManualCandidateDelivery(operation);
            return;
        }
        var plan = operation.Plan;
        var original = operation.Original;
        var history = operation.History;
        // A new check keeps its genuine execution identity. An explicitly bound
        // older pending obligation retains its own identity until this check is
        // received under the same query, environment and authority.
        var origin = operation.SourceContinuation;
        string originSemantic = origin?.SemanticFingerprint ?? plan.CompiledSearch.SemanticFingerprint;
        string originSnapshot = origin?.SnapshotFingerprint ?? plan.SnapshotFingerprint;
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(RuntimeProfileRegistry.Select(plan.Detection), original.Seed,
            out var originalOrdinal, out _, out var seedIssue)) throw new InvalidOperationException("ManualValidationSeedIdentity:" + seedIssue);
        var continuation = history.ManualValidationCandidates.FirstOrDefault(recovery => recovery.Original.Seed == original.Seed &&
            recovery.Continuation.SemanticFingerprint == originSemantic && recovery.Continuation.SnapshotFingerprint == originSnapshot)?.Continuation
            ?? origin ?? new(plan.CompiledSearch.SemanticFingerprint, plan.SnapshotFingerprint, originalOrdinal, [], [])
                { ContinuationFingerprint = SearchContinuationIdentity.For(plan) };
        ProductionExactSearchResult exact;
        try
        {
            if (operation.ExecutionFailure is { } failure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            exact = operation.Result ?? throw new InvalidOperationException("ManualValidationObservationUnavailable");
        }
        catch (Exception exception)
        {
            if (!VisibleSeedCandidateCodec.TryParseOrdinal(RuntimeProfileRegistry.Select(plan.Detection), original.Seed,
                out var ordinal, out _, out var issue))
                throw new InvalidOperationException("ManualValidationSeedIdentity:" + issue, exception);
            var previous = continuation.PendingCandidates.FirstOrDefault(candidate => candidate.Ordinal == ordinal);
            string detail = $"attemptQuery={plan.CompiledSearch.SemanticFingerprint}; attemptSnapshot={plan.SnapshotFingerprint}; " +
                $"attemptContinuation={SearchContinuationIdentity.For(plan)}; " +
                $"originQuery={originSemantic}; originSnapshot={originSnapshot}\n" + exception;
            if (previous is not null) detail = previous.ExceptionText + "\n\n" + detail;
            var pending = new SearchPendingCandidate(ordinal, original.Seed, "ManualCandidateCheck",
                "ManualCandidateCheckFailed:" + exception.GetType().Name, detail,
                originSemantic, originSnapshot,
                previous?.FirstFailedAtUtc ?? DateTimeOffset.UtcNow, (previous?.Attempts ?? 0) + 1)
                { ContinuationFingerprint = continuation.ContinuationFingerprint };
            StoreManualRecovery(history, original, continuation with { PendingCandidates = continuation.PendingCandidates
                .Where(candidate => candidate.Ordinal != ordinal).Append(pending).OrderBy(candidate => candidate.Ordinal).ToArray() });
            if (operation.Source is { } action)
                _pendingResultActions[(history.Id, pending.QueryFingerprint, pending.SnapshotFingerprint, pending.Seed)] = action with { Pending = pending };
            operation.Failure = exception;
            operation.ObservationReceived = true;
            PruneManualValidationSources(history);
            try { Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "ManualCandidateCheckFailed", nameof(SearchWorkspace),
                "ManualCandidateCheck", "runtime.issue.manual_validation_failed", correlationId:
                $"seed={original.Seed};ordinal={ordinal};query={pending.QueryFingerprint};originSnapshot={originSnapshot};attemptSnapshot={plan.SnapshotFingerprint};pending=true"); } catch { }
            FlushWorkspace();
            operation.Accepted = !HasUnsavedSearchState;
            return;
        }

        var updated = FreezeResult(original);
        updated.ValidationDisposition = exact.Evaluation.Disposition;
        updated.QueryFingerprint = plan.CompiledSearch.SemanticFingerprint;
        updated.ValidationIssue = exact.Evaluation.FailureCode + string.Concat(exact.Evaluation.RouteRejections.Select(rejection =>
            $"\nroute={rejection.RouteId};disposition={rejection.Disposition};reason={rejection.Reason}"));
        updated.IsUnverified = !exact.IsMatch;
        updated.IsUnfiltered = false;
        updated.SupportScope = exact.Authority.SupportScope;
        updated.Party = null;
        updated.WitnessOpeningRouteId = "";
        updated.TreasureSources = [];
        updated.SourceAssociation = new(history.QueryKey, exact.IsMatch && history.EnvironmentKnown && history.IdentityVersion == 2
            ? SeedQueryAssociationKind.VerifiedFromSearch : SeedQueryAssociationKind.UnverifiedFromSearch,
            history.EnvironmentFingerprint, history.Id);
        if (exact.IsMatch)
        {
            var candidate = new SearchCandidate(original.Seed, plan.ProfileId, plan.CharacterKey, plan.Ascension,
                string.Join("|", new[] { plan.SnapshotFingerprint, exact.Authority.EffectSnapshotFingerprint,
                    exact.Authority.WorldSnapshotFingerprint }.Where(value => !string.IsNullOrWhiteSpace(value))),
                exact.Request!, exact.Document!, exact.Authority, exact.Evaluation.Evidence,
                exact.Evaluation.MatchedRouteIds, exact.Evaluation.Witnesses)
                { ValidationDisposition = SearchDisposition.Match, ExecutionWarnings = original.ExecutionWarnings };
            operation.Candidate = candidate;
            updated.ValidationIssue = "";
            updated.Party = exact.Document!.Party;
            var projection = SearchResultProjection.FromCandidate(candidate, updated.QueryFingerprint);
            updated.WitnessOpeningRouteId = projection.WitnessOpeningRouteId;
            updated.TreasureSources = projection.TreasureSources;
            if ((original.Context ?? history.Context) is { } context)
            {
                try { updated.Context = SeedLibraryContextCapture.WithWitness(context, candidate); }
                catch (Exception exception)
                {
                    RecordPresentationFailure(exception, "ManualValidationResultContext", original.Seed);
                    updated.ExecutionWarnings = updated.ExecutionWarnings.Append(exception.ToString()).ToArray();
                }
            }
        }
        if (operation.SourceContinuation is { } pendingSource)
        {
            StoreManualRecovery(history, original, pendingSource);
            int recoveryIndex = history.ManualValidationCandidates.FindIndex(recovery => recovery.Original.Seed == original.Seed &&
                recovery.Continuation.SemanticFingerprint == pendingSource.SemanticFingerprint &&
                recovery.Continuation.SnapshotFingerprint == pendingSource.SnapshotFingerprint);
            history.ManualValidationCandidates[recoveryIndex] = history.ManualValidationCandidates[recoveryIndex] with
                { ReceivedObservation = updated };
        }
        ReceiveManualObservation(history, updated);
        if (_processBatches.TryGetValue(history.Id, out var batch))
        {
            if (operation.Candidate is { } candidate) RecordLiveResult(candidate, batch, operation.Start);
            else batch.LiveResults.Remove(updated.Seed);
        }
        operation.ObservationReceived = true;
        // First commit the real observation while the original obligation still
        // exists. A failed save must leave both the new in-memory result and the
        // original pending recoverable; receiving it never reruns Exact.
        FinishManualCandidateDelivery(operation);
    }

    private void FinishManualCandidateDelivery(SearchWorkspaceRuntime.ManualCandidateValidation operation)
    {
        operation.Accepted = FinishManualPendingDelivery(operation.History, operation.SourceContinuation, operation.Original.Seed);
    }

    internal bool HasManualPendingObservation(string historyId, SearchPendingCandidate pending) =>
        _workspace.QueryHistory.FirstOrDefault(history => history.Id == historyId)?.ManualValidationCandidates.Any(recovery =>
            recovery.ReceivedObservation is not null && recovery.Continuation.PendingCandidates.Any(candidate => SameManualPending(candidate, pending))) == true;

    internal bool DeliverManualPendingObservation(string historyId, SearchPendingCandidate pending)
    {
        var history = _workspace.QueryHistory.SingleOrDefault(record => record.Id == historyId)
            ?? throw new InvalidOperationException("ManualValidationOriginalHistoryUnavailable");
        if (Runtime.HasManualCandidateDelivery(historyId, pending.Seed)) throw new InvalidOperationException("ManualValidationAwaitingDelivery");
        var recovery = history.ManualValidationCandidates.SingleOrDefault(item =>
            item.Continuation.PendingCandidates.Any(candidate => SameManualPending(candidate, pending)))
            ?? throw new InvalidOperationException("ManualValidationOriginalPendingMismatch");
        var observation = recovery.ReceivedObservation ?? throw new InvalidOperationException("ManualValidationObservationUnavailable");
        if (observation.Seed != recovery.Original.Seed || observation.Seed != pending.Seed || observation.ValidationDisposition is null ||
            observation.SourceAssociation is not { } association || association.SearchRecordId != history.Id || association.QueryKey != history.QueryKey)
            throw new InvalidOperationException("ManualValidationObservationIdentityMismatch");
        // This is the original completed observation, even if today's selected
        // query/environment is different. No new correctness claim is made.
        ReceiveManualObservation(history, observation);
        return FinishManualPendingDelivery(history, recovery.Continuation, pending.Seed);
    }

    private void ReceiveManualObservation(QueryHistoryEntry history, PersistedSearchResult observation)
    {
        if (!_workspace.QueryHistory.Any(record => record.Id == history.Id)) _workspace.QueryHistory.Add(history);
        if (ActiveHistory?.Id == history.Id) RecordHistoryResult(observation);
        else
        {
            if (_processBatches.TryGetValue(history.Id, out var batch)) Accumulate(batch.Results, observation);
            Accumulate(history.Results, observation);
            if (history.Results.Count > ResultsPerQuery) history.Results.RemoveAt(0);
            ResultsRevision++; MarkWorkspaceDirty();
        }
    }

    private bool FinishManualPendingDelivery(QueryHistoryEntry history, SearchContinuationSnapshot? source, string seed)
    {
        FlushWorkspace();
        if (HasUnsavedSearchState) return false;
        if (source is null) return true;
        var recoveries = history.ManualValidationCandidates.ToArray();
        var normal = history.Continuation;
        var retained = history.RetainedContinuations.ToArray();
        RemoveManualPending(history, source, seed);
        FlushWorkspace();
        if (HasUnsavedSearchState)
        {
            history.ManualValidationCandidates = recoveries.ToList();
            history.Continuation = normal;
            history.RetainedContinuations = retained.ToList();
            if (ActiveHistory?.Id == history.Id) _workspace.ResultContinuation = normal;
            ResultsRevision++; MarkWorkspaceDirty();
            return false;
        }
        return true;
    }

    private void PruneManualValidationSources(QueryHistoryEntry history)
    {
        var needed = history.Results.Select(result => result.QueryFingerprint)
            .Concat(PendingForHistory(history).Select(pending => pending.QueryFingerprint)).ToHashSet(StringComparer.Ordinal);
        history.ManualValidationSources.RemoveAll(identity => !needed.Contains(identity.SemanticFingerprint));
        MarkWorkspaceDirty();
    }

    private void StoreManualRecovery(QueryHistoryEntry history, PersistedSearchResult original, SearchContinuationSnapshot continuation)
    {
        if (!_workspace.QueryHistory.Any(record => record.Id == history.Id)) _workspace.QueryHistory.Add(history);
        var pending = continuation.PendingCandidates.Single(candidate => candidate.Seed == original.Seed);
        var single = continuation with { NextOrdinal = pending.Ordinal, PendingCandidates = [pending], PublishedOrdinals = [] };
        var frozen = FreezeResult(original);
        frozen.Context ??= history.Context is { } context ? SeedLibraryStore.CloneContext(context) : null;
        // Migrate only the matching old manual obligation. Other scan work,
        // received publications and its cursor stay owned by the normal snapshot.
        RemoveManualPending(history, single, original.Seed);
        history.ManualValidationCandidates.Add(new(frozen, single));
        ResultsRevision++; MarkWorkspaceDirty();
    }

    private void RemoveManualPending(QueryHistoryEntry history, SearchContinuationSnapshot source, string seed)
    {
        bool Matches(SearchContinuationSnapshot snapshot) => snapshot.SemanticFingerprint == source.SemanticFingerprint &&
            snapshot.SnapshotFingerprint == source.SnapshotFingerprint;
        SearchContinuationSnapshot Clean(SearchContinuationSnapshot snapshot) => !Matches(snapshot) ? snapshot : snapshot with
        {
            PendingCandidates = snapshot.PendingCandidates.Where(candidate => candidate.Seed != seed ||
                candidate.Stage != "ManualCandidateCheck").ToArray()
        };
        _pendingResultActions.Remove((history.Id, source.SemanticFingerprint, source.SnapshotFingerprint, seed));
        history.ManualValidationCandidates.RemoveAll(recovery => recovery.Original.Seed == seed && Matches(recovery.Continuation));
        if (history.Continuation is { } normal) history.Continuation = Clean(normal);
        history.RetainedContinuations = history.RetainedContinuations.Select(Clean).ToList();
        if (ActiveHistory?.Id == history.Id) _workspace.ResultContinuation = history.Continuation;
        ResultsRevision++; MarkWorkspaceDirty();
    }
}
