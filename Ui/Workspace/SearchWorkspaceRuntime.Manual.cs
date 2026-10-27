using System.Diagnostics;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspaceRuntime
{
    internal sealed class ManualCandidateValidation(QueryHistoryEntry history, PersistedSearchResult original,
        ExactSearchExecutionRequest plan, SearchContinuationSnapshot? sourceContinuation,
        Task<ProductionExactSearchResult> task)
    {
        internal QueryHistoryEntry History { get; } = history;
        internal PersistedSearchResult Original { get; } = original;
        internal ExactSearchExecutionRequest Plan { get; } = plan;
        internal SearchContinuationSnapshot? SourceContinuation { get; } = sourceContinuation;
        internal Task<ProductionExactSearchResult> Task { get; } = task;
        internal bool ExecutionObserved { get; set; }
        internal ProductionExactSearchResult? Result { get; set; }
        internal Exception? ExecutionFailure { get; set; }
        internal bool Accepted { get; set; }
        internal Exception? Failure { get; set; }
        internal SearchCandidate? Candidate { get; set; }
        internal bool ObservationReceived { get; set; }
        internal long NextDeliveryAttemptAt { get; set; }
        internal int DeliveryAttempts { get; set; }
        internal SearchStartContext? Start { get; init; }
        internal SearchResultAction? Source { get; init; }
    }

    private readonly List<ManualCandidateValidation> _manualCandidateValidations = [];
    internal bool HasManualCandidateValidation => _manualCandidateValidations.Any(operation => !operation.Task.IsCompleted);
    internal bool HasManualCandidateDelivery(string seed) => HasManualCandidateDelivery(store.Workspace.ActiveHistoryId, seed);
    internal bool HasManualCandidateDelivery(string historyId, string seed) => _manualCandidateValidations.Any(operation =>
        operation.History.Id == historyId && operation.Original.Seed == seed);

    internal void Validate(SearchResultAction action, ModRuntimeSnapshot runtime, SceneTree? tree)
    {
        if (HasSearchInFlight) throw new InvalidOperationException("ManualValidationAlreadyRunning");
        string seed = action.Result.Seed;
        if (HasManualCandidateDelivery(action.HistoryId, seed)) throw new InvalidOperationException("ManualValidationAwaitingDelivery");
        if (action.Pending is { } pending && store.HasManualPendingObservation(action.HistoryId, pending))
        {
            bool delivered = store.DeliverManualPendingObservation(action.HistoryId, pending);
            PublishReceipt(new(seed, true, delivered, false, null, "", "", SaveOnly: true));
            Bind(tree);
            return;
        }
        store.EnsureEnvironment(SearchEnvironmentSignatureBuilder.Capture(runtime));
        if (!store.TryManualValidationSource(seed, out var history, out _, out var issue, action.HistoryId, action.Pending))
            throw new InvalidOperationException(issue);
        var frozenPlan = action.ValidationPlan ?? action.Live?.Start.Plan;
        var plan = frozenPlan is { } live ? live with { RunOptions = live.RunOptions with { SkipExactValidation = false } }
            : SearchResultProjection.RebuildSavedValidationPlan(runtime, history, action.Result);
        if (!SearchWorkspace.ProvesManualValidationSource(history, action.Result, plan.CompiledSearch))
            throw new InvalidOperationException("ManualValidationOriginalAuthorityMismatch");
        var profile = runtime.Profile;
        TrackManualCandidateValidation(history, action.Result, plan, () =>
        {
            try
            {
                var input = TrustedRootHashInput.FromCanonicalSeed(profile, seed);
                var exact = ProductionExactSearchEvaluator.Evaluate(plan, input);
                if (exact.Document is { } document) ProductionExactSearchEvaluator.ThrowIfPredictionFailed(document);
                if (exact.Evaluation.Disposition == SearchDisposition.Match && !exact.IsMatch)
                    throw new InvalidOperationException("ProductionExact.MatchPayloadMissing:" + seed);
                if (!exact.IsMatch) { try { ExactRejectionDiagnostics.Log(plan, seed, exact); } catch { } }
                return exact;
            }
            catch (Exception exception)
            {
                try { RuntimeIssueLedger.Current.Record(exception, "ManualCandidateValidationFailed", nameof(SearchWorkspaceRuntime), "ExactWorker",
                    "runtime.issue.manual_validation_failed", correlationId:
                    $"seed={seed};query={plan.CompiledSearch.SemanticFingerprint};attemptSnapshot={plan.SnapshotFingerprint}"); } catch { }
                throw;
            }
        }, tree, action);
    }

    internal ManualCandidateValidation TrackManualCandidateValidation(QueryHistoryEntry history,
        PersistedSearchResult original, ExactSearchExecutionRequest plan, Func<ProductionExactSearchResult> evaluate,
        SceneTree? tree = null, SearchResultAction? action = null)
    {
        var prepared = store.PrepareManualValidation(history, original, plan, action);
        var task = Task.Run(evaluate);
        var operation = new ManualCandidateValidation(history, prepared.Original, plan, prepared.Continuation, task)
            { Start = prepared.Start, Source = prepared.Action };
        _manualCandidateValidations.Add(operation);
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Auxiliary(() => Bind(tree), "ManualValidationFrameBinding");
        return operation;
    }

    internal void PollManualCandidateValidations() => PollManualCandidateValidations(Stopwatch.GetTimestamp());
    internal void PollManualCandidateValidations(long now)
    {
        foreach (var operation in _manualCandidateValidations.ToArray())
        {
            if (!operation.Task.IsCompleted || now < operation.NextDeliveryAttemptAt) continue;
            if (!operation.ExecutionObserved)
            {
                try
                {
                    var exact = operation.Task.GetAwaiter().GetResult();
                    if (exact.Document is { } document)
                    {
                        ProductionExactSearchEvaluator.ThrowIfPredictionFailed(document);
                        if (document.Diagnostics.Any(diagnostic => diagnostic.Code == PredictionDiagnosticCodes.Exception))
                            throw new InvalidOperationException("ProductionExact.DocumentException:" + string.Join(" | ",
                                document.Diagnostics.Where(diagnostic => diagnostic.Code == PredictionDiagnosticCodes.Exception).Select(diagnostic => diagnostic.Value)));
                    }
                    if (exact.Evaluation.Disposition == SearchDisposition.Match && !exact.IsMatch)
                        throw new InvalidOperationException("ProductionExact.MatchPayloadMissing:" + operation.Original.Seed);
                    operation.Result = exact;
                }
                catch (Exception exception) { operation.ExecutionFailure = exception; }
                operation.ExecutionObserved = true;
            }
            operation.DeliveryAttempts++;
            try { store.AcceptManualCandidateValidation(operation); }
            catch (Exception exception) { store.RecordPresentationFailure(exception, "ManualValidationHandoff", operation.Original.Seed); }
            ProductionExactSearchResult? observation = operation.Result;
            PublishReceipt(new(operation.Original.Seed, operation.ObservationReceived, operation.Accepted, observation?.IsMatch == true,
                observation?.Evaluation.Disposition, observation?.Evaluation.FailureCode ?? "", operation.Failure?.Message ?? ""));
            if (operation.Accepted) _manualCandidateValidations.Remove(operation);
            else operation.NextDeliveryAttemptAt = now + (long)Math.Ceiling(SearchWorkspace.WorkspaceDebounceSeconds * Stopwatch.Frequency);
        }
    }
    private void PublishReceipt(ManualValidationReceipt receipt)
    {
        if (_receipt == receipt) return;
        _receipt = receipt; _receiptRevision++;
    }
}
