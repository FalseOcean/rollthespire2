using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Authority;

/// <summary>Immutable initial-observation authority. No acquisition or execution state.</summary>
public sealed class OrderedPartyAuthority
{
    public const string ObservationVersion = "Party.InitialBeforeNeowPickup.WActBoss.NOffers.v1";
    public IReadOnlyList<RuntimeContextAuthoritySnapshot> Players { get; }
    public IReadOnlyList<string> UnlockProvenance { get; }
    public IReadOnlyList<string> UnlockSources { get; }
    public WorldAuthoritySnapshot World { get; }
    public string Fingerprint { get; }
    public OrderedPartyAuthority(IReadOnlyList<RuntimeContextAuthoritySnapshot> players,
        IReadOnlyList<string> unlockProvenance, WorldAuthoritySnapshot world, IReadOnlyList<string>? unlockSources = null)
    {
        if (players.Count < 2 || unlockProvenance.Count != players.Count)
            throw new ArgumentException("Party.MissingMembers");
        var sources = unlockSources?.ToArray() ?? Enumerable.Repeat("CapturedProfile", players.Count).ToArray();
        if (sources.Length != players.Count || sources.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Party.InvalidUnlockSources");
        UnlockSources = Array.AsReadOnly(sources);
        var copy = players.ToArray();
        for (int slot = 0; slot < copy.Length; slot++)
        {
            var p = copy[slot];
            if (p.PlayerSlotIndex != slot || p.PlayersCount != copy.Length ||
                p.ProfileId != RuntimeProfileId.Beta111 || p.GameVersion != copy[0].GameVersion ||
                p.Ascension != copy[0].Ascension || p.PredictionGameMode != WorldGameMode.Multiplayer ||
                p.Ascension is < 0 or > 10 || !p.Character.IsValid || string.IsNullOrWhiteSpace(unlockProvenance[slot]) ||
                !p.IsBeta111NeowIdentityAuthorityExact)
                throw new ArgumentException($"Party.InvalidSlotAuthority:P{slot + 1}");
        }
        if (world.Beta109Generation is not { HasExactFixedParty: true } generation ||
            generation.PlayerCount != copy.Length || generation.Ascension != copy[0].Ascension ||
            generation.LobbyPlayers.Where((p, i) => p.CharacterKey != copy[i].Character.CharacterKey).Any())
            throw new ArgumentException("Party.InvalidSharedWorldAuthority");
        Players = Array.AsReadOnly(copy); UnlockProvenance = Array.AsReadOnly(unlockProvenance.ToArray()); World = world;
        Fingerprint = Hash(ObservationVersion + "|" + copy[0].GameVersion + "|Multiplayer|" + copy[0].Ascension + "|" +
            world.SnapshotFingerprint + "|" + string.Join("|", copy.Select((p, i) =>
                $"{i}:{p.Character.CharacterKey.Serialized}:{unlockProvenance[i]}:{sources[i]}:{p.CatalogFingerprint}:{p.UnlockSnapshotFingerprint}")));
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
