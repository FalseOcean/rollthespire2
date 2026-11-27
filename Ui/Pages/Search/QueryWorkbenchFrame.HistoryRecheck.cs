using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    internal Task<Dictionary<string, PersistedSearchResult>> RecheckHistory(QueryHistoryEntry history, string title, CancellationToken cancel)
    {
        var seeds = history.Results.Select(r => r.Seed).Distinct(StringComparer.Ordinal).ToArray();
        var draft = RestorePreset(history.AsPreset(title), render: IsInsideTree());
        var compiled = draft.Compile(_runtime, out _);
        var context = SeedLibraryContextCapture.ForSearch(draft, _runtime);
        var profile = _runtime.Profile;
        string historyId = history.Id;
        var preparation = SearchPreparationCompiler.Compile(compiled, new(ProfileSeedGenerator.CreateProbeSeed(profile), 1, 1, 1));
        // Only frozen inputs cross the worker boundary. This batch has no scan cursor.
        return Task.Run(() =>
        {
            var results = new Dictionary<string, PersistedSearchResult>(StringComparer.Ordinal);
            foreach (var seed in seeds)
            {
                cancel.ThrowIfCancellationRequested();
                var result = new PersistedSearchResult { Seed = seed, Context = context, CharacterKey = draft.Character.Serialized,
                    Ascension = draft.Ascension, QueryFingerprint = compiled.SemanticFingerprint,
                    ValidationDisposition = preparation.Disposition, ValidationIssue = preparation.Issue };
                if (preparation.Plan is { } plan)
                {
                    try
                    {
                        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(profile, seed, out var canonical, out var issue))
                            throw new InvalidDataException(issue);
                        var exact = ProductionExactSearchEvaluator.EvaluateCandidate(plan.Model,
                            TrustedRootHashInput.FromCanonicalSeed(profile, canonical), cancel);
                        if (exact.Facts is { } facts) ProductionExactSearchEvaluator.ThrowIfPredictionFailed(facts);
                        var candidate = new SearchCandidate(canonical, plan.Model.ProfileId, draft.Character, draft.Ascension,
                            plan.SnapshotFingerprint, exact.Authority, exact.Evaluation)
                        {
                            Facts = exact.Facts, ValidationIssue = exact.IsMatch ? "" : string.IsNullOrEmpty(exact.Evaluation.FailureCode)
                                ? exact.FailureCode : exact.Evaluation.FailureCode
                        };
                        var observed = SearchResultProjection.FromCandidate(candidate, compiled.SemanticFingerprint);
                        observed.Context = SeedLibraryContextCapture.WithWitness(context, candidate);
                        result = observed;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        result.ValidationDisposition = SearchDisposition.Unknown;
                        result.ValidationIssue = ex.Message;
                        RuntimeIssueLedger.Current.Record(ex, "HistorySeedRecheck", nameof(QueryWorkbenchFrame), "ExactWorker",
                            "runtime.issue.manual_validation_failed", correlationId: $"history={historyId};seed={seed};query={compiled.SemanticFingerprint}");
                    }
                }
                results.Add(seed, result);
            }
            return results;
        }, cancel);
    }
}
