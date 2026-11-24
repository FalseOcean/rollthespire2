using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed record RuntimeEffectSnapshotCaptureResult(
    NeowEffectAuthoritySnapshot? EffectAuthority,
    bool? AllCharacterCardPoolsUnlocked,
    int? UnlockedCommonCards,
    int? UnlockedUncommonCards,
    string UnlockFingerprint,
    SourceAuthority UnlockAuthority,
    SnapshotCompleteness UnlockCompleteness,
    IdentityResolutionStatus CharacterResolution,
    IReadOnlyList<PredictionWarningCode> WarningCodes,
    string DiagnosticSummary)
{
    public static RuntimeEffectSnapshotCaptureResult Missing(
        RuntimeProfileId profileId,
        string diagnostic,
        params PredictionWarningCode[] warnings)
    {
        PredictionWarningCode[] warningCodes = warnings.Length == 0
            ? new[] { PredictionWarningCode.RuntimeEffectSnapshotMissing }
            : warnings.Distinct().ToArray();
        var authority = new NeowEffectAuthoritySnapshot(
            OrderedDeck: null,
            CharacterRewardPool: null,
            ColorlessRewardPool: null,
            OtherCharacterPools: null,
            TransformPool: null,
            PotionPool: null,
            OrderedRelicBag: null,
            GeneratedCursePool: null,
            BonesEligibleRelics: null,
            CharacterStrikeKey: null,
            CharacterDefendKey: null,
            ClawKey: null,
            CardRewardPoolsExact: false,
            DeckExact: false,
            TransformPoolsExact: false,
            PotionPoolExact: false,
            RelicBagExact: false,
            CursePoolExact: false,
            BonesEligibilityExact: false,
            CardRewardHooksNoOpExact: false,
            PotionProcurementHooksNoOpExact: false,
            NestedRelicHooksExact: false,
            CurrentGold: null,
            CurrentPotionCount: null,
            PotionCapacity: null,
            SourceAuthority: SourceAuthority.Unknown,
            Completeness: SnapshotCompleteness.Missing,
            AuthoritySource: NeowEffectAuthoritySource.Missing,
            CapturedProfileId: profileId,
            SnapshotFingerprint: $"missing:{profileId}:{diagnostic}",
            CapturedAtUtc: DateTimeOffset.UtcNow,
            WarningCodes: warningCodes,
            CaptureDiagnosticCode: diagnostic);
        return new RuntimeEffectSnapshotCaptureResult(
            authority,
            null,
            null,
            null,
            string.Empty,
            SourceAuthority.Unknown,
            SnapshotCompleteness.Missing,
            IdentityResolutionStatus.Unknown,
            warningCodes,
            diagnostic);
    }

}

internal interface INeowEffectSnapshotAdapter
{
    RuntimeProfileId ProfileId { get; }

    RuntimeEffectSnapshotCaptureResult Capture(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        int playersCount,
        int playerSlotIndex);
}
