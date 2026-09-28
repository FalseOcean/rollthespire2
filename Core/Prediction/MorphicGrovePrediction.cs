using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Events;

namespace RolltheSpire2.Core.Prediction;

/// <summary>
/// Captured entry facts for an already occurring Morphic Grove. This is not the
/// starting deck or proof of a path to the event. No mutable game objects escape.
/// </summary>
public sealed class MorphicGroveScenario
{
    private string? _fingerprint;
    // Immutable content identity is reused by Query compilation and result evidence.
    // It is derived, not a new serialized authority/schema field.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => _fingerprint ??= Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this))));
    public MorphicGroveScenario(RuntimeContextAuthoritySnapshot authority, MorphicGrovePremises premises,
        IReadOnlyList<MorphicGroveTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(premises);
        ArgumentNullException.ThrowIfNull(targets);
        Authority = authority;
        Premises = premises;
        Targets = Array.AsReadOnly(targets.Select(t => t with {
            OrderedSourceCandidates = t.OrderedSourceCandidates is null ? null : Array.AsReadOnly(t.OrderedSourceCandidates.ToArray())
        }).ToArray());
    }
    public RuntimeContextAuthoritySnapshot Authority { get; }
    public MorphicGrovePremises Premises { get; }
    public IReadOnlyList<MorphicGroveTarget> Targets { get; }
}

/// <summary>Conditional observations only; precision never proves event occurrence or a full final RunState.</summary>
public sealed record MorphicGrovePrediction(
    string CanonicalSeed, ulong? RootHash, RuntimeContextAuthoritySnapshot Authority,
    MorphicGrovePremises Premises, MorphicGroveCommitment? Commitment,
    PredictionPrecision Precision, MorphicGroveProjection? Projection, string Evidence);

internal static class MorphicGrovePredictor
{
    internal static void ValidateAuthority(GameVersionDetection detection, RuntimeContextAuthoritySnapshot a,
        MorphicGroveScenario scenario)
    {
        var captured = scenario.Authority;
        if (a.GameVersion != detection.NormalizedVersion || captured.GameVersion != a.GameVersion ||
            captured.ProfileId != a.ProfileId || captured.Character.CharacterKey != a.Character.CharacterKey ||
            captured.Ascension != a.Ascension || captured.PlayersCount != a.PlayersCount ||
            captured.PlayerSlotIndex != a.PlayerSlotIndex || captured.CatalogFingerprint != a.CatalogFingerprint ||
            captured.UnlockSnapshotFingerprint != a.UnlockSnapshotFingerprint)
            throw new ArgumentException("MorphicGrove.ScenarioAuthorityContextMismatch");
    }
    internal static MorphicGrovePrediction Predict(GameVersionDetection detection, SeedPredictionRequest request,
        MorphicGroveScenario scenario, MorphicGroveCommitment? commitment)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scenario);
        var a = request.Authority;
        MorphicGrovePrediction Missing(PredictionPrecision precision, string reason) =>
            new("", null, a, scenario.Premises, commitment, precision, null, reason);
        if (RuntimeProfileRegistry.Resolve(detection).ProfileId != RuntimeProfileId.Beta111 || a.ProfileId != RuntimeProfileId.Beta111)
            return Missing(PredictionPrecision.Unsupported, "MorphicGrove.Beta111ModelRequired");
        ValidateAuthority(detection, a, scenario);
        if (string.IsNullOrWhiteSpace(scenario.Premises.EventOccurrenceBasis))
            return Missing(PredictionPrecision.Unknown, "MorphicGrove.EventOccurrencePremiseRequired");
        // Vanilla offers Group or Loner, neither is an effect-free Skip. Missing
        // commitment cannot silently choose Loner or execute Group.
        if (commitment is null)
            return Missing(PredictionPrecision.Unknown, "MorphicGrove.MandatoryChoiceNotAuthored");
        TrustedRootHashInput root;
        if (request.TrustedRootHashInput is { } trusted) root = trusted;
        else
        {
            if (!Beta111Profile.Instance.TryCanonicalizeSeed(request.OriginalSeed, out var canonicalSeed, out var issue))
                throw new ArgumentException("MorphicGrove.InvalidSeed:" + issue);
            root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, canonicalSeed);
        }
        // No catch-and-Unknown: malformed captured facts and implementation faults propagate.
        var projection = Beta111MorphicGroveProjector.Project(root.RootHash, commitment, scenario.Premises, scenario.Targets);
        var precision = projection.Transforms.All(t => t.FinalPrecision == PredictionPrecision.Exact)
            ? PredictionPrecision.Exact : projection.Transforms.Any(t => t.RawPrecision == PredictionPrecision.Exact)
                ? PredictionPrecision.Partial : PredictionPrecision.Unknown;
        return new(root.CanonicalSeed, root.RootHash, a, scenario.Premises, commitment, precision, projection,
            "MorphicGrove.Group.ConditionalOnCapturedEntry;OccurrenceNotProven;FullRunStateNotClaimed");
    }
}
