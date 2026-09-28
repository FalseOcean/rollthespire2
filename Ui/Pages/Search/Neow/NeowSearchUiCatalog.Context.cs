using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

internal sealed partial record NeowSearchUiCatalog
{
    public static IReadOnlyList<ModelKey> AllRuntimeIdentities() => ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().AllPossibleOptions
        .Where(o => o.Relic is not null).Select(o => new ModelKey("RELIC", o.Relic!.Id.Entry))
        .Distinct().OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray();

    public static IReadOnlySet<ModelKey> CaptureModeAllowedIdentities(ModelKey character, int ascension, int count)
    {
        var model = ModelDb.GetById<CharacterModel>(new ModelId("CHARACTER", character.Entry));
        var players = Enumerable.Range(0, count).Select(i => Player.CreateForNewRun(model, UnlockState.none, (ulong)(i + 1))).ToArray();
        var run = RunState.CreateForTest(players, ascensionLevel: ascension, seed: "000000000000");
        // Only the run-mode gate here: do not infer unlock eligibility from UnlockState.none.
        return AllRuntimeIdentities().Where(k => ModelDb.GetById<RelicModel>(new ModelId(k.Category, k.Entry)).IsAllowed(run)).ToHashSet();
    }

    // Main-thread UI capture only. Never attach these temporary players/run to RunManager.
    // The other temporary players supply population count for IsAllowed; no outcomes are simulated.
    public static NeowSearchUiCatalog CaptureForPicker(ModRuntimeSnapshot runtime, ModelKey character,
        int ascension, int players, int seat, UnlockState unlocks)
    {
        string seed = new(runtime.Profile.SeedAlphabet[0], runtime.Profile.SeedLength);
        var captured = ReflectionNeowEffectSnapshotAdapter.Capture(runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, runtime.Profile.ProfileId,
            runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        if (captured.EffectAuthority is null) throw new InvalidOperationException("Picker candidate capture unavailable");
        var catalog = FromAuthority(runtime.Profile.ProfileId, character, captured.EffectAuthority);
        var model = ModelDb.GetById<CharacterModel>(new ModelId("CHARACTER", character.Entry));
        var temporaryPlayers = Enumerable.Range(0, players)
            .Select(i => Player.CreateForNewRun(model, unlocks, (ulong)(i + 1))).ToArray();
        var run = RunState.CreateForTest(temporaryPlayers, ascensionLevel: ascension, seed: seed);
        var owner = temporaryPlayers[seat];
        var allowed = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().AllPossibleOptions.Where(o => o.Relic is not null && o.Relic.IsAllowedAtNeow(owner))
            .Select(o => new ModelKey("RELIC", o.Relic!.Id.Entry)).Distinct().ToArray();
        var relics = catalog.OrdinaryRelics.Where(k => ModelDb.GetById<RelicModel>(new ModelId(k.Category, k.Entry)).IsAllowed(run)).ToArray();
        return catalog with { RouteRelics = allowed, OrdinaryRelics = relics,
            BonesNeowRelics = allowed.Where(k => k != BaseGameModelKeys.Relics.NeowsBones).ToArray() };
    }
}
