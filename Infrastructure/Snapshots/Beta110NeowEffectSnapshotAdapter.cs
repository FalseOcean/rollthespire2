using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Beta 0.110.1 main-thread runtime authority capture. The reflection implementation is
/// shared with the Beta109 donor, while profile, audit and catalog fingerprints
/// remain Beta110-specific.
/// </summary>
internal sealed class Beta110NeowEffectSnapshotAdapter : INeowEffectSnapshotAdapter
{
    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta110;

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
            expectedProfile: RuntimeProfileId.Beta110,
            capturedGameVersion: gameVersion,
            rules: new ReflectionSnapshotProfileRules(
                GeneratedCursePoolRequiresModifierFlag: true,
                OtherCharacterPoolsUseModelIdOrder: true));
}
