using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.Pages.Search.CombatReward;

internal sealed partial record CombatRewardSearchUiCatalog
{
    // Main-thread UI capture only. It reads the immutable reward-pool snapshot and
    // never attaches the temporary context to RunManager or advances RNG.
    public static CombatRewardSearchUiCatalog CaptureForPicker(ModRuntimeSnapshot runtime,
        ModelKey character, int ascension, int players, int seat, UnlockState unlocks)
    {
        string seed = new(runtime.Profile.SeedAlphabet[0], runtime.Profile.SeedLength);
        var captured = ReflectionNeowEffectSnapshotAdapter.Capture(runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, runtime.Profile.ProfileId,
            runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        return FromAuthority(runtime.Profile.ProfileId, captured.EffectAuthority,
            includeMultiplayerOnly: players > 1, characterKey: character);
    }
}
