using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal sealed class Beta109NeowEffectSnapshotAdapter : INeowEffectSnapshotAdapter
{
    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta109;

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
            expectedProfile: RuntimeProfileId.Beta109,
            capturedGameVersion: gameVersion,
            rules: new ReflectionSnapshotProfileRules(
                GeneratedCursePoolRequiresModifierFlag: true,
                OtherCharacterPoolsUseModelIdOrder: true));
}
