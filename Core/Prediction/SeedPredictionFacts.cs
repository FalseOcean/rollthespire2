using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction;

// Input readiness is independent of any requested domain's model disposition.
public enum SeedPredictionInputStatus { Ready, InvalidRequest, Unsupported, Unknown }

public sealed record NeowSeedObservation(
    IReadOnlyList<NeowChoiceResult> Choices, PredictionPrecision IdentityPrecision,
    SeedDomainEvaluationStatus Status, string IssueCode = "", bool HasIdentityAuthorityWarning = false);

/// <summary>
/// Bounded facts computed from one seed and explicit captured authority. A null
/// domain was not observed; a requested unavailable domain retains its own status
/// and cause. These values carry no display section, Search session or device state.
/// </summary>
public sealed record SeedPredictionFacts
{
    public required PredictionContext Context { get; init; }
    public required string PredictorId { get; init; }
    public required string OriginalSeed { get; init; }
    public required string CanonicalSeed { get; init; }
    public SeedPredictionInputStatus InputStatus { get; init; } = SeedPredictionInputStatus.Ready;
    public string InputIssue { get; init; } = "";
    public NeowSeedObservation? Neow { get; init; }
    public WorldPredictionResult? World { get; init; }
    public string WorldAuthorityFingerprint { get; init; } = "";
    public RelicSequencePredictionResult? Relics { get; init; }
    public NormalCombatRewardSequencePredictionResult? CombatRewards { get; init; }
    public PartySeedInformation? Party { get; init; }
    public IReadOnlyList<PredictionWarning> Warnings { get; init; } = [];
    public IReadOnlyList<PredictionDiagnostic> Diagnostics { get; init; } = [];
    public string DetectedGameVersion => Context.GameVersion;
    public RuntimeProfileId ProfileId => Context.ProfileId;

    internal static SeedPredictionFacts Invalid(string detectedVersion, RuntimeProfileId profileId,
        string predictorId, SeedPredictionRequest request, string technicalIssue) => new()
    {
        Context = PredictionContext.FromRequest(request, detectedVersion, profileId),
        PredictorId = predictorId, OriginalSeed = request.OriginalSeed, CanonicalSeed = "",
        InputStatus = SeedPredictionInputStatus.InvalidRequest, InputIssue = technicalIssue,
        Warnings = [new(PredictionWarningCode.InvalidSeed)],
        Diagnostics = request.IncludeDiagnostics ? [
            new(PredictionDiagnosticCodes.RequestId, request.RequestId.Serialized),
            new(PredictionDiagnosticCodes.PlayerSlot, request.PlayerSlotIndex.ToString()),
            new(PredictionDiagnosticCodes.UnlockSnapshotFingerprint, request.Authority.UnlockSnapshotFingerprint),
            new(PredictionDiagnosticCodes.CatalogFingerprint, request.Authority.CatalogFingerprint),
            new(PredictionDiagnosticCodes.ValidationIssue, technicalIssue)] : []
    };
}

public sealed record SeedPredictionRevision(SeedPredictionRequest Request, SeedPredictionFacts Facts);
