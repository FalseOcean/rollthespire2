using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Workspace;

// Latest acknowledged Workbench attempt, independent of whichever history the UI is displaying.
// Keep a bounded list of detached receipts so the feedback action never reruns a seed.
internal sealed class SearchFeedbackAttempt
{
    internal const int CandidateLimit = 64;
    private const int EvidenceLimit = 16;
    private const int TextLimit = 4096;
    private const int CandidateJsonLimit = 24 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new SearchWorkspace.ModelKeyJsonConverter(), new JsonStringEnumConverter() }
    };

    public string BatchId { get; init; } = "";
    public string LogSessionId { get; init; } = "";
    public string QueryFingerprint { get; init; } = "";
    public JsonElement? Request { get; init; }
    public SearchProgressSnapshot? LastCheckpoint { get; set; }
    public string? ExecutionPlanId { get; set; }
    public ulong? ScannedFrontierOrdinal { get; set; }
    public ulong? ContinuationOrdinal { get; set; }
    public int? PendingCount { get; set; }
    public IReadOnlyList<SearchPendingCandidate>? Pending { get; set; }
    public int ReceivedCount { get; set; }
    public List<JsonElement> FirstReceivedCandidates { get; init; } = [];

    internal static SearchFeedbackAttempt Start(SearchStartContext request, string sessionId)
    {
        var plan = request.Plan;
        var compiled = plan.Model.CompiledSearch;
        var context = compiled.Context;
        var draft = request.Draft;
        return new()
        {
            BatchId = request.BatchId, LogSessionId = sessionId,
            QueryFingerprint = compiled.SemanticFingerprint,
            Request = JsonSerializer.SerializeToElement(new
            {
                request.HistoryId, request.QueryKey, request.EnvironmentFingerprint, request.StartedAtUtc,
                plan.SnapshotFingerprint, plan.CanonicalStartSeed, request.ScanStartOrdinal,
                ResumedFromOrdinal = request.Resolution.Continuation?.NextOrdinal,
                ExplicitOrigin = request.Resolution.ExplicitOriginChangeId.Length > 0,
                plan.RunOptions, plan.Model.ProfileId, GameVersion = plan.Model.Detection.NormalizedVersion,
                context.UnlockSnapshotFingerprint, context.CatalogFingerprint,
                context.EffectSnapshotFingerprint, context.WorldSnapshotFingerprint,
                draft.Character, draft.Ascension, draft.Mode, draft.ObservationVersion,
                draft.AncientPremises, draft.Query, Context = SearchContext(request.Context),
                Players = draft.Players.Select(player => new { player.Slot, player.Character, player.UnlockSource })
            }, Json)
        };
    }

    internal void Receive(SearchCandidate candidate)
    {
        ReceivedCount++;
        if (FirstReceivedCandidates.Count >= CandidateLimit) return;
        var evaluation = candidate.Evaluation;
        var neow = candidate.Facts?.Neow;
        var party = candidate.Party;
        var receipt = new
        {
            candidate.Seed, candidate.CharacterKey, candidate.Ascension, candidate.ProfileId,
            candidate.SnapshotFingerprint, candidate.IsUnfiltered, candidate.ValidationDisposition,
            ValidationIssue = LimitText(candidate.ValidationIssue), FailureCode = LimitText(evaluation.FailureCode)
        };
        var details = JsonSerializer.SerializeToElement(new
        {
            ExecutionWarnings = Sample(candidate.ExecutionWarnings, LimitText),
            MatchedRouteIds = Sample(evaluation.MatchedRouteIds, LimitText),
            Witnesses = Sample(evaluation.Witnesses, witness => witness),
            Evidence = Sample(evaluation.Evidence, item => item),
            RouteRejections = Sample(evaluation.RouteRejections, rejection => rejection with { Reason = LimitText(rejection.Reason) }),
            ReplayEstimate = evaluation.ReplayEstimate is { } estimate ? new
            {
                estimate.Disposition, estimate.Assumptions, Issue = LimitText(estimate.Issue),
                Routes = Sample(estimate.Routes, LimitText), Evidence = Sample(estimate.Evidence, item => item)
            } : null,
            Neow = neow is null ? null : new
            {
                neow.IdentityPrecision, neow.Status, neow.IssueCode,
                Choices = Sample(neow.Choices, OpeningChoice)
            },
            Party = party is null ? null : new
            {
                party.PartyFingerprint, party.ObservationWindow, party.Players,
                Transactions = Sample(party.Transactions, transaction => new
                {
                    transaction.Slot, transaction.Option, transaction.Intent, transaction.Choices, transaction.OpeningRouteId,
                    Offers = Sample(transaction.Offers, EffectGroup), Results = Sample(transaction.Results, EffectGroup)
                })
            },
            candidate.MapSearch
        }, Json);
        // Even an unusually large modded result keeps its seed and original disposition.
        bool detailsOmitted = details.GetRawText().Length > CandidateJsonLimit;
        FirstReceivedCandidates.Add(JsonSerializer.SerializeToElement(new
        {
            Receipt = receipt, Details = detailsOmitted ? (JsonElement?)null : details, DetailsOmitted = detailsOmitted
        }, Json));
    }

    internal void Checkpoint(SearchProgressSnapshot progress, SearchContinuationSnapshot? continuation,
        ulong? scannedFrontier, string? planId)
    {
        LastCheckpoint = progress with
        {
            FailureCode = LimitText(progress.FailureCode), CleanupIssue = LimitText(progress.CleanupIssue),
            ReplayAssumptions = LimitText(progress.ReplayAssumptions),
            ExecutionWarnings = progress.ExecutionWarnings.Take(EvidenceLimit).Select(LimitText).ToArray()
        };
        ScannedFrontierOrdinal = scannedFrontier;
        ContinuationOrdinal = continuation?.NextOrdinal;
        ExecutionPlanId = planId;
        var pending = continuation?.PendingCandidates;
        PendingCount = pending?.Count;
        Pending = pending?.Take(EvidenceLimit).Select(item => item with { ExceptionText = LimitText(item.ExceptionText) }).ToArray();
    }

    internal static string LimitText(string value) => value.Length <= TextLimit ? value : value[..TextLimit] + " [truncated]";

    private static object Sample<T, TProjection>(IReadOnlyList<T> items, Func<T, TProjection> project) =>
        new { items.Count, Items = items.Take(EvidenceLimit).Select(project).ToArray() };

    internal static object? SearchContext(Persistence.SeedLibraryContext? context) => context is null ? null : new
    {
        context.GameVersion, context.Profile, context.Mode, context.Ascension, context.SelectedSlot,
        Players = context.Players.Select(player => new
        {
            player.Slot, player.Character, player.UnlockSource, player.AncientPremises,
            player.OpeningPremise, player.Selection, player.Unlocks.NumberOfRuns, player.Unlocks.UnlockedEpochs
        }).ToArray()
    };

    private static object EffectGroup(PredictedEffectGroup group) => new
    {
        group.GroupId, group.GroupOrder, group.SelectionPolicy, group.RequiredSelectionCount,
        group.SelectionSetId, group.BundleId, group.SourceRelicKey,
        Orders = Sample(group.OrderedItems, effect => new
        {
            effect.Kind, effect.TargetKey, effect.SourceKey, effect.ItemOrder, effect.Multiplicity, effect.Amount,
            effect.Precision, effect.OfferItemId, effect.Relation
        })
    };

    private static object OpeningChoice(NeowChoiceResult choice) => new
    {
        choice.SlotIndex, choice.RelicKey, choice.IdentityPrecision, choice.EffectPrecision,
        EffectGroups = Sample(choice.EffectGroups, EffectGroup),
        Bones = choice.BonesOutcome is { } bones ? new
        {
            bones.OfferedRelics, bones.OrderComparisonStatus,
            Routes = Sample(bones.OriginalRoutes, route => new
            {
                route.RouteId, route.AcquisitionOrder, route.Precision, route.RouteProjectionStatus,
                route.PlayerChoiceSelections,
                Relics = Sample(route.RelicScopedResults, result => new
                {
                    result.SourceRelicKey, result.Precision, Effects = Sample(result.EffectGroups, EffectGroup)
                })
            })
        } : null
    };
}

internal sealed partial class SearchWorkspace
{
    internal void InstallSearchFeedback(SearchFeedbackAttempt attempt)
    {
        _workspace.LatestSearchFeedback = attempt;
        MarkWorkspaceDirty();
    }

    internal string? CaptureSearchFeedbackJson()
    {
        var attempt = _workspace.LatestSearchFeedback;
        // Saved display rows can be from several attempts. Their own source and legacy
        // precision survive, but a selected history never supplies missing session details.
        var selected = _workspace.ResultDraft is not null && (_workspace.ActiveHistoryId.Length > 0 || _workspace.Results.Count > 0) &&
                       (attempt is null || attempt.BatchId != _workspace.ResultBatchId)
            ? new
            {
                _workspace.ActiveHistoryId, DisplayBatchId = _workspace.ResultBatchId,
                Request = new
                {
                    _workspace.ResultDraft.Character, _workspace.ResultDraft.Ascension, _workspace.ResultDraft.Mode,
                    _workspace.ResultDraft.AncientPremises, _workspace.ResultDraft.WithoutCapturedAuthority().Query,
                    Players = _workspace.ResultDraft.Players.Select(player => new { player.Slot, player.Character, player.UnlockSource })
                },
                Context = SearchFeedbackAttempt.SearchContext(_workspace.ResultContext), Count = _workspace.Results.Count,
                LastStoredRows = _workspace.Results.TakeLast(SearchFeedbackAttempt.CandidateLimit).Select(row => new
                {
                    row.Seed, row.CharacterKey, row.Ascension, row.QueryFingerprint, row.SourceAssociation,
                    row.IsUnverified, row.ValidationDisposition, row.IsUnfiltered,
                    ValidationIssue = SearchFeedbackAttempt.LimitText(row.ValidationIssue),
                    row.WitnessOpeningRouteId
                }).ToArray()
            }
            : null;
        if (attempt is null && selected is null) return null;
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            LatestAttempt = attempt,
            LatestAttemptIsExecuting = attempt is not null && _runtime?.IsReceivingSearchFeedback(attempt) == true,
            SelectedStoredHistory = selected
        }, SearchFeedbackAttempt.Json);
    }
}
