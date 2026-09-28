using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Rewards;

[Flags]
public enum OpeningRewardSupportFlags
{
    None = 0,
    ConsumesRewardsRng = 1 << 0,
    ChangesRewardGenerationContext = 1 << 1,
    ChangesCardRewardPool = 1 << 2,
    ChangesPotionRewardPool = 1 << 3,
    ChangesPotionDropState = 1 << 4,
    IntroducesUnknownRewardHook = 1 << 5,
    KnownSpecialRewardImpactNotImplemented = 1 << 6,
    ConditionalRewardImpactNotModeled = 1 << 7,
    NestedOpeningRewardImpactNotImplemented = 1 << 8,
    VanillaCapabilityNotAudited = 1 << 9,
    UnknownModRewardImpact = 1 << 10,
    SupportedSpecialRewardImpact = 1 << 11
}

public enum OpeningRewardRouteKind
{
    DirectNeowChoice,
    BonesAcquisitionOrder
}

public sealed record RewardsRngStateSnapshot(
    ulong S0,
    ulong S1,
    ulong S2,
    ulong S3,
    int CallCount)
{
    public static RewardsRngStateSnapshot Capture(Xoshiro256StarStar rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        (ulong s0, ulong s1, ulong s2, ulong s3) = rng.State;
        return new RewardsRngStateSnapshot(s0, s1, s2, s3, rng.CallCount);
    }

    public Xoshiro256StarStar Restore() =>
        Xoshiro256StarStar.FromState(S0, S1, S2, S3, CallCount);

    public string Fingerprint => FingerprintParts(
        "opening-rewards-rng-state-v1",
        S0.ToString("X16"),
        S1.ToString("X16"),
        S2.ToString("X16"),
        S3.ToString("X16"),
        CallCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string FingerprintParts(params string[] values)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join("|", values));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}

public sealed record OpeningRewardRouteDescriptor(
    string RouteId,
    int RouteOrder,
    OpeningRewardRouteKind RouteKind,
    ModelKey RootRelicKey,
    IReadOnlyList<ModelKey> AcquisitionOrder,
    PredictionPrecision Precision,
    EvidenceCode EvidenceCode);

/// <summary>
/// Immutable bridge from one completed Neow/Bones opening route into later
/// PlayerRng.Rewards consumers. The full copied xoshiro state is production
/// authority; RewardsDrawCount is diagnostic evidence only.
/// </summary>
public sealed record OpeningRewardContinuation(
    OpeningRewardRouteDescriptor Route,
    RewardsRngStateSnapshot? RewardsRngState,
    int RewardsDrawCount,
    float PotionRewardState,
    OpeningRewardSupportFlags Capabilities,
    string RewardContextFingerprint,
    string ContinuationFingerprint,
    PredictionPrecision Precision,
    SourceAuthority Authority,
    SnapshotCompleteness Completeness,
    IReadOnlyList<EvidenceCode> EvidenceCodes,
    IReadOnlyList<string> UnknownReasonCodes)
{
    internal RewardsRngStateSnapshot? NicheState { get; init; }
    internal RewardsRngStateSnapshot? CombatPotionGenerationState { get; init; }
    public IReadOnlyList<RewardImpactSourceKey> ActiveRewardImpactSources { get; init; } =
        Array.Empty<RewardImpactSourceKey>();

    public string RewardImpactFingerprint { get; init; } = string.Empty;

    public bool CanProjectRewards =>
        RewardsRngState is not null &&
        Precision is PredictionPrecision.Exact or PredictionPrecision.Partial or PredictionPrecision.DescriptionOnly &&
        UnknownReasonCodes.Count == 0 &&
        (Capabilities & OpeningRewardSupportFlags.IntroducesUnknownRewardHook) == 0 &&
        (Capabilities & OpeningRewardSupportFlags.ChangesPotionDropState) == 0;
}


public enum OpeningRewardNeutrality
{
    ProvablyNeutral,
    Perturbing,
    Unknown
}

public enum NormalCombatRewardProjectionScope
{
    RichAnalysis,
    SearchExact
}

public enum NormalCombatRewardRouteSelectionMode
{
    AllRealRoutes,
    PinnedRealRoute,
    ProvablyNeutralRealRoutes
}

public sealed record NormalCombatRewardProjectionRequest(
    NormalCombatRewardProjectionScope Scope,
    NormalCombatRewardRouteSelectionMode RouteSelectionMode,
    ModelKey? PinnedRootRelicKey = null,
    IReadOnlyList<ModelKey>? RequiredAcquisitionOrder = null)
{
    public int BattleCount { get; init; } = 3;

    public static NormalCombatRewardProjectionRequest RichAnalysis { get; } = new(
        NormalCombatRewardProjectionScope.RichAnalysis,
        NormalCombatRewardRouteSelectionMode.AllRealRoutes);
}

public static class OpeningRewardNeutralityClassifier
{
    private const float DefaultPotionRewardState = 0.4f;

    public static OpeningRewardNeutrality Classify(OpeningRewardContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        if (continuation.Precision != PredictionPrecision.Exact ||
            continuation.RewardsRngState is null ||
            continuation.UnknownReasonCodes.Count != 0 ||
            !SourceAuthorityRules.SupportsExactIdentity(continuation.Authority) ||
            continuation.Completeness != SnapshotCompleteness.Complete)
        {
            return OpeningRewardNeutrality.Unknown;
        }

        const OpeningRewardSupportFlags unknownCapabilities =
            OpeningRewardSupportFlags.IntroducesUnknownRewardHook |
            OpeningRewardSupportFlags.KnownSpecialRewardImpactNotImplemented |
            OpeningRewardSupportFlags.ConditionalRewardImpactNotModeled |
            OpeningRewardSupportFlags.NestedOpeningRewardImpactNotImplemented |
            OpeningRewardSupportFlags.VanillaCapabilityNotAudited |
            OpeningRewardSupportFlags.UnknownModRewardImpact;
        if ((continuation.Capabilities & unknownCapabilities) != 0)
            return OpeningRewardNeutrality.Unknown;

        // RewardsRngState is the production authority. RewardsDrawCount is only
        // diagnostic evidence, so disagreement between the two fails closed.
        if (continuation.RewardsDrawCount != continuation.RewardsRngState.CallCount)
            return OpeningRewardNeutrality.Unknown;

        if (continuation.RewardsRngState.CallCount != 0 ||
            !continuation.PotionRewardState.Equals(DefaultPotionRewardState) ||
            continuation.ActiveRewardImpactSources.Count != 0 ||
            continuation.Capabilities != OpeningRewardSupportFlags.None)
        {
            return OpeningRewardNeutrality.Perturbing;
        }

        return OpeningRewardNeutrality.ProvablyNeutral;
    }
}

public sealed record OpeningRewardContinuationAnalysis(
    IReadOnlyList<OpeningRewardContinuation> Routes,
    PredictionPrecision Precision,
    string IssueCode,
    EvidenceCode EvidenceCode)
{
    public static OpeningRewardContinuationAnalysis Unknown(
        ModelKey rootRelicKey,
        string issueCode,
        EvidenceCode evidenceCode) => new(
        new[]
        {
            new OpeningRewardContinuation(
                new OpeningRewardRouteDescriptor(
                    $"opening:{rootRelicKey.Serialized}:unknown",
                    0,
                    rootRelicKey == BaseGameModelKeys.Relics.NeowsBones
                        ? OpeningRewardRouteKind.BonesAcquisitionOrder
                        : OpeningRewardRouteKind.DirectNeowChoice,
                    rootRelicKey,
                    rootRelicKey.IsValid ? new[] { rootRelicKey } : Array.Empty<ModelKey>(),
                    PredictionPrecision.Unknown,
                    evidenceCode),
                null,
                0,
                0.4f,
                OpeningRewardSupportFlags.IntroducesUnknownRewardHook,
                string.Empty,
                string.Empty,
                PredictionPrecision.Unknown,
                SourceAuthority.Unknown,
                SnapshotCompleteness.Missing,
                new[] { evidenceCode },
                new[] { issueCode })
        },
        PredictionPrecision.Unknown,
        issueCode,
        evidenceCode);
}
