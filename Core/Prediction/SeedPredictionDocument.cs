using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;

namespace RolltheSpire2.Core.Prediction;

/// <summary>
/// The immutable Predictor document for one request. <see cref="OverallStatus"/>
/// is intentionally the document/request viability plus the Neow product-facing
/// aggregate; it is not a summary of every optional World, Relic, or Reward
/// section. Search Exact checks the sections and domain status that its Query
/// actually consumes.
/// </summary>
public sealed class SeedPredictionDocument
{
    public PartySeedInformation? Party { get; init; }
    public required PredictionContext Context { get; init; }
    // Stable wire name and identifier values preserve existing evidence/readers.
    [System.Text.Json.Serialization.JsonPropertyName("AnalyzerId")]
    public required string PredictorId { get; init; }
    public required string OriginalSeed { get; init; }
    public required string CanonicalSeed { get; init; }
    public required IReadOnlyList<PredictionSection> Sections { get; init; }
    public required IReadOnlyList<PredictionWarning> Warnings { get; init; }
    public required IReadOnlyList<PredictionDiagnostic> Diagnostics { get; init; }
    /// <summary>
    /// Preserved compatibility field for document/request viability and the
    /// Neow product-facing aggregate. This value does not represent an
    /// all-domain World/Relic/Reward aggregate.
    /// </summary>
    public required SeedPredictionOverallStatus OverallStatus { get; init; }

    /// <summary>
    /// Aggregate product-facing projection precision. This dimension is not
    /// downgraded merely because unrelated future/full relic semantics remain
    /// incomplete.
    /// </summary>
    public PredictionPrecision ProductRelevantProjectionPrecision { get; init; } = PredictionPrecision.Unknown;

    public ProductRelevantProjectionStatus ProductRelevantProjectionStatus { get; init; } =
        ProductRelevantProjectionStatus.Unknown;

    public FullEffectSemanticsCompleteness FullEffectSemanticsCompleteness { get; init; } =
        FullEffectSemanticsCompleteness.Unknown;

    public string DetectedGameVersion => Context.GameVersion;
    public RuntimeProfileId ProfileId => Context.ProfileId;

    public static SeedPredictionDocument Invalid(
        string detectedVersion,
        RuntimeProfileId profileId,
        string analyzerId,
        SeedPredictionRequest request,
        string technicalIssue) => new()
    {
        Context = CreateContext(request, detectedVersion, profileId),
        PredictorId = analyzerId,
        OriginalSeed = request.OriginalSeed,
        CanonicalSeed = string.Empty,
        Sections = Array.Empty<PredictionSection>(),
        Warnings = new[] { new PredictionWarning(PredictionWarningCode.InvalidSeed) },
        Diagnostics = request.IncludeDiagnostics
            ? new[]
            {
                new PredictionDiagnostic(PredictionDiagnosticCodes.RequestId, request.RequestId.Serialized),
                new PredictionDiagnostic(PredictionDiagnosticCodes.PlayerSlot, request.PlayerSlotIndex.ToString()),
                new PredictionDiagnostic(PredictionDiagnosticCodes.UnlockSnapshotFingerprint, request.Authority.UnlockSnapshotFingerprint),
                new PredictionDiagnostic(PredictionDiagnosticCodes.CatalogFingerprint, request.Authority.CatalogFingerprint),
                new PredictionDiagnostic(PredictionDiagnosticCodes.ValidationIssue, technicalIssue)
            }
            : Array.Empty<PredictionDiagnostic>(),
        OverallStatus = SeedPredictionOverallStatus.InvalidRequest,
        ProductRelevantProjectionPrecision = PredictionPrecision.Unknown,
        ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Unknown,
        FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Unknown
    };

    public static PredictionContext CreateContext(
        SeedPredictionRequest request,
        string detectedVersion,
        RuntimeProfileId profileId) => new(
        request.RequestId,
        profileId,
        detectedVersion,
        request.Character,
        request.Ascension,
        request.PlayersCount,
        request.PlayerSlotIndex,
        request.Authority.UnlockSnapshotFingerprint,
        request.Authority.CatalogFingerprint,
        request.Authority.SourceAuthority,
        request.Authority.Completeness,
        request.Authority.ResolutionStatus,
        request.Authority.IsVanilla,
        request.Authority.SourceModId,
        request.Authority.SourceAssembly);
}
