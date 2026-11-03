using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Search.Contracts;

public enum SearchDisposition
{
    Match,
    NoMatch,
    Unknown,
    NotEvaluatedByPolicy,
    Unsupported,
    Cancelled
}

public sealed record SearchMatchEvidence(
    string Code,
    ModelKey? RelatedKey = null,
    string? RouteId = null,
    IReadOnlyList<ModelKey>? AcquisitionOrder = null,
    EvidenceCode EvidenceCode = default,
    string? ChoicePolicyId = null,
    string? OutcomeId = null,
    IReadOnlyList<ModelKey>? AutomaticEffectKeys = null,
    RuntimeProfileId? ProfileId = null,
    int? Act = null,
    string? StreamDomain = null,
    SourceAuthority? Authority = null,
    string? AuthorityFingerprint = null,
    IReadOnlyList<ModelKey>? OrderedOptionKeys = null,
    int? Ordinal = null,
    string? ConditionId = null);

/// <summary>
/// Stable proof that all route-related conditions were satisfied by one legal
/// opening route. Global seed conditions may add evidence, but never manufacture
/// or widen OpeningRouteId.
/// </summary>
public sealed record SearchMatchWitness(
    string Seed,
    string OpeningRouteId,
    IReadOnlyList<ModelKey> AcquisitionOrder,
    IReadOnlyList<string> MatchedConditions,
    string RewardContinuationFingerprint,
    int? MatchedBattleOrdinal,
    IReadOnlyList<EvidenceCode> EvidenceCodes)
{
    public string RewardRouteGroupId { get; init; } = string.Empty;
    public string ChoicePolicyId { get; init; } = string.Empty;
    public string OutcomeId { get; init; } = string.Empty;
}

/// <summary>
/// Search/Exact disposition plus evidence and route witness. FailureCode is a
/// developer-facing technical reason for this Search evaluation; it is not an
/// PredictionWarning, Planner fallback, GPU recovery result, capability status, or
/// player-facing compatibility message.
/// </summary>
public sealed record SearchQueryEvaluation(
    SearchDisposition Disposition,
    IReadOnlyList<SearchMatchEvidence> Evidence,
    IReadOnlyList<string> MatchedRouteIds,
    IReadOnlyList<SearchMatchWitness> Witnesses,
    string FailureCode = "")
{
    // Diagnostic only: preserve the first failed predicate for each attempted opening.
    public IReadOnlyList<SearchRouteRejection> RouteRejections { get; init; } = Array.Empty<SearchRouteRejection>();
    public static SearchQueryEvaluation Match(
        IReadOnlyList<SearchMatchEvidence>? evidence = null,
        IReadOnlyList<string>? routeIds = null,
        IReadOnlyList<SearchMatchWitness>? witnesses = null) =>
        new(
            SearchDisposition.Match,
            evidence ?? Array.Empty<SearchMatchEvidence>(),
            routeIds ?? Array.Empty<string>(),
            witnesses ?? Array.Empty<SearchMatchWitness>());

    public static SearchQueryEvaluation NoMatch(string code) =>
        new(SearchDisposition.NoMatch, Array.Empty<SearchMatchEvidence>(), Array.Empty<string>(), Array.Empty<SearchMatchWitness>(), code);

    public static SearchQueryEvaluation Unknown(string code) =>
        new(SearchDisposition.Unknown, Array.Empty<SearchMatchEvidence>(), Array.Empty<string>(), Array.Empty<SearchMatchWitness>(), code);

    public static SearchQueryEvaluation NotEvaluatedByPolicy(string code) =>
        new(SearchDisposition.NotEvaluatedByPolicy, Array.Empty<SearchMatchEvidence>(), Array.Empty<string>(), Array.Empty<SearchMatchWitness>(), code);

    public static SearchQueryEvaluation Unsupported(string code) =>
        new(SearchDisposition.Unsupported, Array.Empty<SearchMatchEvidence>(), Array.Empty<string>(), Array.Empty<SearchMatchWitness>(), code);
}

public sealed record SearchRouteRejection(string RouteId, IReadOnlyList<ModelKey> AcquisitionOrder,
    SearchDisposition Disposition, string Reason);

/// <summary>A delivered model disposition and its evidence, independent of any analysis view.</summary>
public sealed record SearchCandidate(
    string Seed,
    RuntimeProfileId ProfileId,
    ModelKey CharacterKey,
    int Ascension,
    string SnapshotFingerprint,
    RuntimeContextAuthoritySnapshot Authority,
    SearchQueryEvaluation Evaluation)
{
    public SeedPredictionFacts? Facts { get; init; }
    public SearchDisposition ValidationDisposition => Evaluation.Disposition;
    public string ValidationIssue { get; init; } = "";
    public IReadOnlyList<SearchMatchEvidence> MatchEvidence => Evaluation.Evidence;
    public IReadOnlyList<string> MatchedRouteIds => Evaluation.MatchedRouteIds;
    public IReadOnlyList<SearchMatchWitness> Witnesses => Evaluation.Witnesses;
    public PartySeedInformation? Party => Facts?.Party;
    public MapSearchProvenance? MapSearch { get; init; }
    public IReadOnlyList<string> ExecutionWarnings { get; init; } = [];
    public RequestSupportAssessment SupportScope => Authority.SupportScope;
    public bool IsUnverified => ValidationDisposition != SearchDisposition.Match;
    public bool IsUnfiltered { get; init; }
    public SearchMatchWitness? PrimaryWitness => Witnesses.FirstOrDefault();
}
