using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;

namespace RolltheSpire2.Core.Prediction;

/// <summary>
/// The immutable display document projected from bounded seed facts. <see cref="OverallStatus"/>
/// is intentionally the document/request viability plus the Neow product-facing
/// aggregate; it is not a summary of every optional World, Relic, or Reward
/// section. Exact consumes the source facts and their domain statuses directly.
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

    // Display projection is one-way: calculations and Exact consume Facts directly.
    public static SeedPredictionDocument FromFacts(SeedPredictionFacts facts) =>
        Profiles.SeedPredictionDocumentFactory.FromFacts(facts);

}
