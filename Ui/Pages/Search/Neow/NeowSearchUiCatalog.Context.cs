using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

internal sealed partial record NeowSearchUiCatalog
{
    internal static NeowSearchUiCatalog FromAuthority(RuntimeContextAuthoritySnapshot authority)
    {
        var catalog = FromAuthority(authority.ProfileId, authority.Character.CharacterKey, authority.EffectAuthority);
        if (authority.ProfileId != RuntimeProfileId.Beta111 ||
            authority.CapturedNeowOffers == null && authority.VanillaNeowCatalogExact != true) return catalog;
        var preparation = authority.NeowPreparation;
        var candidates = new HashSet<ModelKey>(preparation.EligibleCurses);
        foreach (var curse in preparation.EligibleCurses)
        {
            var pool = preparation.ForCurse(curse);
            candidates.UnionWith(pool.FixedCandidates);
            foreach (var choice in pool.AppendChoices)
            {
                if (choice.TrueEligible) candidates.Add(choice.TrueCandidate);
                if (choice.FalseEligible) candidates.Add(choice.FalseCandidate);
            }
        }
        return catalog with { RouteRelics = candidates.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray(),
            RouteEligibilityComplete = authority.HasExactModernNeowIdentityInputs && authority.EffectAuthority?.BonesEligibilityExact == true };
    }

    public static IReadOnlyList<ModelKey> AllRuntimeIdentities() => ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().AllPossibleOptions
        .Where(o => o.Relic is not null).Select(o => new ModelKey("RELIC", o.Relic!.Id.Entry))
        .Distinct().OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray();

    public static IReadOnlySet<ModelKey> CaptureModeAllowedIdentities(ModelKey character, int ascension, int count)
    {
        var model = ModelDb.GetById<CharacterModel>(new ModelId("CHARACTER", character.Entry));
        var players = Enumerable.Range(0, count).Select(i => Player.CreateForNewRun(model, UnlockState.none, (ulong)(i + 1))).ToArray();
        var run = DetachedStandardRunBinding.Create(players, ascension, "000000000000");
        // Only the run-mode gate here: do not infer unlock eligibility from UnlockState.none.
        return AllRuntimeIdentities().Where(k => ModelDb.GetById<RelicModel>(new ModelId(k.Category, k.Entry)).IsAllowed(run)).ToHashSet();
    }

    // Main-thread UI capture only. Never attach these temporary players/run to RunManager.
    // The other temporary players supply population count for IsAllowed; no outcomes are simulated.
    public static NeowSearchUiCatalog CaptureForPicker(ModRuntimeSnapshot runtime, ModelKey character,
        int ascension, int players, int seat, UnlockState unlocks,
        RolltheSpire2.Core.Authority.RuntimeContextAuthoritySnapshot? sharedAuthority = null)
    {
        string seed = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(runtime.Profile);
        sharedAuthority ??= RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, runtime.Detection.DisplayVersion, players, seat,
            explicitUnlockState: unlocks);
        if (sharedAuthority.EffectAuthority is null) throw new InvalidOperationException("Picker candidate capture unavailable");
        var catalog = FromAuthority(sharedAuthority);
        var model = ModelDb.GetById<CharacterModel>(new ModelId("CHARACTER", character.Entry));
        var temporaryPlayers = Enumerable.Range(0, players)
            .Select(i => Player.CreateForNewRun(model, unlocks, (ulong)(i + 1))).ToArray();
        var run = DetachedStandardRunBinding.Create(temporaryPlayers, ascension, seed);
        var owner = temporaryPlayers[seat];
        var relics = catalog.OrdinaryRelics.Where(k => ModelDb.GetById<RelicModel>(new ModelId(k.Category, k.Entry)).IsAllowed(run)).ToArray();
        if (sharedAuthority.ProfileId == RuntimeProfileId.Beta111) return catalog with { OrdinaryRelics = relics };
        var allowed = ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>().AllPossibleOptions.Where(o => o.Relic is not null && o.Relic.IsAllowedAtNeow(owner))
            .Select(o => new ModelKey("RELIC", o.Relic!.Id.Entry)).Distinct().ToArray();
        return catalog with { RouteRelics = allowed, OrdinaryRelics = relics,
            BonesNeowRelics = allowed.Where(k => k != BaseGameModelKeys.Relics.NeowsBones).ToArray(), RouteEligibilityComplete = true };
    }
}
