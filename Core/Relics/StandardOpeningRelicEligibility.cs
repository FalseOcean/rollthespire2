using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Relics;

internal static class StandardOpeningRelicEligibility
{
    internal static readonly ModelKey LastingCandy = new("RELIC", "LASTING_CANDY");

    // Lasting Candy's 0.113 opening eligibility depends on the roster.
    // Raw bag order is independent of the eligibility check.
    internal static bool? AllowsLastingCandy(RuntimeContextAuthoritySnapshot authority) =>
        authority.PlayersCount == 1
            ? AllowsLastingCandy(authority.Character.CharacterKey, authority.SupportEvidence.Unlock.NumberOfRuns)
            : InParty(authority.WorldAuthority?.Beta109Generation, authority.PlayerSlotIndex,
                authority.Character.CharacterKey, authority.SupportEvidence.Unlock.NumberOfRuns);

    internal static bool? AllowsLastingCandy(WorldAuthoritySnapshot world)
    {
        if (world.Beta109Generation is not { } generation) return null;
        // The shared maximum provides no first-run fact for an individual seat.
        return generation.IsMultiplayer
            ? InParty(generation, generation.PersonalPlayerSlot, generation.CharacterKey, null)
            : AllowsLastingCandy(generation.CharacterKey, world.EncounterNumberOfRuns);
    }

    private static bool? AllowsLastingCandy(ModelKey character, int? runs) =>
        character == BaseGameModelKeys.Characters.Ironclad ? runs.HasValue ? runs > 0 : null : true;

    private static bool? InParty(Beta109WorldGenerationSnapshot? generation, int slot, ModelKey character, int? runs)
    {
        if (generation?.PartyHasFirstRunIronclad is bool any) return !any;
        bool? own = AllowsLastingCandy(character, runs);
        if (own == false) return false;
        return generation is { HasExactPartyRoster: true } && generation.LobbyPlayers.All(p =>
            p.Slot == slot || p.CharacterKey != BaseGameModelKeys.Characters.Ironclad) ? own : null;
    }
}
