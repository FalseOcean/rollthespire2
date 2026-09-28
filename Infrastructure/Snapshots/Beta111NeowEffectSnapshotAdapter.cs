using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Beta 0.111.0 main-thread authority capture. Source audit found the capture
/// mechanism unchanged; the independent profile stamp/audit fingerprint prevents
/// Beta110 evidence from being silently inherited.
/// </summary>
internal sealed class Beta111NeowEffectSnapshotAdapter : INeowEffectSnapshotAdapter
{
    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta111;

    public RuntimeEffectSnapshotCaptureResult Capture(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        int playersCount,
        int playerSlotIndex) =>
        ReflectionNeowEffectSnapshotAdapter.Capture(
            profile,
            rawSeed,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            expectedProfile: RuntimeProfileId.Beta111,
            capturedGameVersion: gameVersion,
            rules: new ReflectionSnapshotProfileRules(
                GeneratedCursePoolRequiresModifierFlag: true,
                OtherCharacterPoolsUseModelIdOrder: true));
}
