using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Infrastructure.Snapshots;

public static class PartyRuntimeAuthorityCapture
{
    public static OrderedPartyAuthority Capture(IRuntimeProfile profile, string seed, IReadOnlyList<ModelKey> characters,
        IReadOnlyList<SerializableUnlockState> unlocks, int ascension, string version, IReadOnlyList<string>? unlockSources = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (profile.ProfileId != RuntimeProfileId.Beta111 || characters.Count < 2 || unlocks.Count != characters.Count ||
            unlocks.Any(u => u is null) || characters.Any(k => !CharacterIdentity.FromKey(k).IsValid))
            throw new ArgumentException("Party.MissingOrUnsupportedAuthority");
        // Runtime capture resolves each supplied identity and its actual pools.
        // A registered Mod character is not missing authority merely by origin.
        var states = unlocks.Select(UnlockState.FromSerializable).ToArray();
        // Supplied profiles are retained. The UI may explicitly supply the full-unlock assumption; missing profiles never default here.
        var players = characters.Select((key, slot) => RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
            profile, seed, CharacterIdentity.FromKey(key), ascension, version, characters.Count, slot,
            WorldGameMode.Multiplayer, PredictionGameModeAuthority.ExplicitRequest, states[slot], characters)).ToArray();
        var union = new UnlockState(states);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(profile, seed, CharacterIdentity.FromKey(characters[0]),
            ascension, characters.Count, 0, true, players[0].EffectAuthority, WorldGameMode.Multiplayer,
            PredictionGameModeAuthority.ExplicitRequest, version, explicitUnlockState: union, orderedCharacters: characters);
        var generation = world.Beta109Generation ?? throw new InvalidOperationException("Party.WorldCaptureMissing");
        var bags = players.Select(p => p.WorldAuthority?.Beta109Generation?.PlayerRelicBuckets ??
            throw new InvalidOperationException("Party.PersonalBagCaptureMissing")).ToArray();
        var provenance = unlocks.Select(u => OrderedPartyAuthority.Hash(string.Join(",", u.UnlockedEpochs.Order()) + "|" +
            string.Join(",", u.EncountersSeen.Select(k => k.ToString()).Order()) + "|" + u.NumberOfRuns)).ToArray();
        // Standard initial generation applies the party union's discovery ordering after
        // the normal Boss draw. Capture the replacement identity, never change RNG state.
        var discovery = new Dictionary<string, ModelKey>();
        foreach (var act in generation.OrderedActCatalog)
        {
            var model = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.ActModel>(
                new MegaCrit.Sts2.Core.Models.ModelId(act.ActKey.Category, act.ActKey.Entry));
            var replacement = model.BossDiscoveryOrder.FirstOrDefault(b => !union.HasSeenEncounter(b));
            if (replacement is not null) discovery.Add(act.ActKey.Serialized, new(replacement.Id.Category, replacement.Id.Entry));
        }
        generation = generation with
        {
            PartyRelicBuckets = Array.AsReadOnly(bags),
            PlayerRelicBuckets = bags[0],
            PartyBossDiscoveryOverrides = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelKey>(discovery),
            PartyBossDiscoveryExact = true,
            TutorialBossOverrideAuthorityExact = true,
            TutorialBossOverrideWillApply = discovery.Count > 0,
            SnapshotFingerprint = OrderedPartyAuthority.Hash(generation.SnapshotFingerprint + "|" + string.Join("|", provenance) + "|" +
                string.Join("|", players.Select(p => p.WorldSnapshotFingerprint)) + "|" +
                string.Join("|", discovery.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value.Serialized)))
        };
        world = world with { Beta109Generation = generation, SnapshotFingerprint = generation.SnapshotFingerprint };
        if (!SourceAuthorityRules.SupportsExactIdentity(world.SourceAuthority) || world.Completeness != SnapshotCompleteness.Complete ||
            !Beta109WorldSnapshotProjector.ProjectForSeed(generation, seed).AllowsBossProductionExact)
            throw new InvalidOperationException("Party.InitialWorldAuthorityIncomplete:" + generation.CaptureDiagnosticCode);
        // Existing shared-W compiler projection reads Context.Authority.WorldAuthority.
        // Bind the separately captured union world there, retaining P1's personal N inputs.
        for (int slot = 0; slot < players.Length; slot++)
        {
            var personalWorld = players[slot].WorldAuthority!;
            var personal = personalWorld.Beta109Generation!;
            var bound = generation with
            {
                PersonalPlayerSlot = slot,
                CharacterKey = players[slot].Character.CharacterKey,
                UnlockedCharacters = personal.UnlockedCharacters,
                UnlockedCharactersExact = personal.UnlockedCharactersExact,
                PlayerRelicBuckets = bags[slot],
                AncientEventContexts = personal.AncientEventContexts,
                SnapshotFingerprint = OrderedPartyAuthority.Hash(generation.SnapshotFingerprint + "|owner=" + slot + "|" + personal.SnapshotFingerprint)
            };
            players[slot] = players[slot].WithWorldAuthority(personalWorld with
            {
                // Shared world generation was captured and verified against the party
                // unlock union above. Do not relabel it with P1's partial solo-world
                // confidence; personal pools and their exactness flags stay personal.
                ActGroups = world.ActGroups,
                SharedEvents = world.SharedEvents,
                SharedAncients = world.SharedAncients,
                CatalogOrderExact = world.CatalogOrderExact,
                SourceAuthority = world.SourceAuthority,
                Completeness = world.Completeness,
                CaptureDiagnosticCode = world.CaptureDiagnosticCode,
                Beta109Generation = bound,
                SnapshotFingerprint = bound.SnapshotFingerprint
            });
        }
        world = players[0].WorldAuthority!;
        return new OrderedPartyAuthority(players, provenance, world, unlockSources);
    }
}
