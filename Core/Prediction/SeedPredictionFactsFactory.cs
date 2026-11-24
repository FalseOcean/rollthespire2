using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction;

internal static class SeedPredictionFactsFactory
{
    internal static SeedPredictionFacts Create(
        SeedPredictionRequest request, GameVersionDetection detection, RuntimeProfileId profileId,
        string predictorId, string canonicalSeed, IReadOnlyList<NeowChoiceResult> choices,
        IReadOnlyList<PredictionWarning> warnings, IReadOnlyList<PredictionDiagnostic> diagnostics,
        SeedPredictionInputStatus inputStatus, WorldPredictionResult? worldPrediction = null,
        RelicSequencePredictionResult? relicSequencePrediction = null,
        NormalCombatRewardSequencePredictionResult? normalCombatRewardPrediction = null,
        SeedDomainEvaluationStatus? neowStatus = null, string neowIssueCode = "",
        PredictionPrecision identityPrecision = PredictionPrecision.Unknown,
        bool identityAuthorityWarning = false, string inputIssue = "") => new()
    {
        Context = PredictionContext.FromRequest(request, detection.DisplayVersion, profileId),
        PredictorId = predictorId,
        OriginalSeed = request.OriginalSeed,
        CanonicalSeed = canonicalSeed,
        InputStatus = inputStatus,
        InputIssue = inputIssue,
        Neow = choices.Count > 0 || neowStatus.HasValue
            ? new(choices, identityPrecision, neowStatus ?? SeedDomainEvaluationStatus.Evaluated,
                neowIssueCode, identityAuthorityWarning) : null,
        World = worldPrediction,
        WorldAuthorityFingerprint = request.Authority.WorldSnapshotFingerprint,
        Relics = relicSequencePrediction,
        CombatRewards = normalCombatRewardPrediction,
        Warnings = warnings,
        Diagnostics = request.IncludeDiagnostics
            ? ContextDiagnostics(request).Concat(diagnostics)
                .Concat(worldPrediction?.Diagnostics ?? [])
                .Concat(worldPrediction?.EventPoolSequencePrediction?.Diagnostics ?? [])
                .Concat(relicSequencePrediction?.Diagnostics ?? [])
                .Concat(normalCombatRewardPrediction?.Diagnostics ?? []).ToArray()
            : warnings.Any(w => w.Code == PredictionWarningCode.AnalysisFailed)
                ? diagnostics.Where(d => d.Code == PredictionDiagnosticCodes.Exception).ToArray() : []
    };

    private static IReadOnlyList<PredictionDiagnostic> ContextDiagnostics(SeedPredictionRequest request)
    {
        var diagnostics = new List<PredictionDiagnostic>
        {
            new(PredictionDiagnosticCodes.RequestId, request.RequestId.Serialized),
            new(PredictionDiagnosticCodes.PlayerSlot, request.PlayerSlotIndex.ToString()),
            new(PredictionDiagnosticCodes.UnlockSnapshotFingerprint, request.Authority.UnlockSnapshotFingerprint),
            new(PredictionDiagnosticCodes.CatalogFingerprint, request.Authority.CatalogFingerprint)
        };
        if (request.Authority.EffectAuthority is { } effects)
        {
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectAuthoritySource, effects.AuthoritySource.ToString()));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectSnapshotFingerprint, effects.SnapshotFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectDeckFingerprint, effects.DeckFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectRelicBagFingerprint, effects.RelicBagFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectPotionPoolFingerprint, effects.PotionPoolFingerprint));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectSnapshotCompleteness, effects.Completeness.ToString()));
            diagnostics.Add(new(PredictionDiagnosticCodes.EffectSnapshotCapturedAt,
                effects.CapturedAtUtc?.ToString("O") ?? "not-provided"));
            diagnostics.Add(new(
                PredictionDiagnosticCodes.EffectSnapshotWarnings,
                effects.WarningCodes is { Count: > 0 }
                    ? string.Join(",", effects.WarningCodes.Distinct())
                    : "none"));
            diagnostics.Add(new(
                PredictionDiagnosticCodes.EffectSnapshotCaptureDiagnostic,
                string.IsNullOrWhiteSpace(effects.CaptureDiagnosticCode)
                    ? "not-provided"
                    : effects.CaptureDiagnosticCode));
        }
        if (request.Authority.WorldAuthority is { } world)
        {
            diagnostics.Add(new("world-snapshot-fingerprint", world.SnapshotFingerprint));
            diagnostics.Add(new("world-catalog-fingerprint", world.CatalogFingerprint));
            diagnostics.Add(new("world-snapshot-completeness", world.Completeness.ToString()));
            diagnostics.Add(new("world-snapshot-authority", world.SourceAuthority.ToString()));
            diagnostics.Add(new("world-snapshot-capture-diagnostic", world.CaptureDiagnosticCode));
        }
        return diagnostics;
    }

}
