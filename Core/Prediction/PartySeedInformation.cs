using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Prediction;

public sealed record PlayerInitialOffers(int Slot, ModelKey Character, IReadOnlyList<ModelKey> Offers)
{
    public string UnlockSource { get; init; } = "CapturedProfile";
}
public sealed record PartySeedInformation(string Seed, ulong Root, string Version, int Ascension,
    string PartyFingerprint, IReadOnlyList<ModelKey> Acts, IReadOnlyList<BossPredictionResult> Bosses,
    IReadOnlyList<PlayerInitialOffers> Players)
{
    public IReadOnlyList<Core.Effects.PartyNeowResult> Transactions { get; init; } = [];
    public bool IncludesFamilyInformation { get; init; }
    public string ObservationWindow => Transactions.Count == 0 ? OrderedPartyAuthority.ObservationVersion : Core.Effects.PartyNeowAdmission.ObservationVersion;
    public string GameMode => "Multiplayer";
    public string SupportedDomain => "Beta111 Standard; no run modifiers; vanilla characters; explicit per-slot unlock premises; " +
        (IncludesFamilyInformation ? "fixed player-order opening; personal and shared family information premises; no T or route prediction" :
         Transactions.Count == 0 ? "initial generation before any Neow pickup" : "declared participants completed N transactions in fixed slot order; all other players wait; no continuation");

    public void RequireSameObservation(PartySeedInformation other)
    {
        if (PartyFingerprint != other.PartyFingerprint || Version != other.Version || Ascension != other.Ascension)
            throw new InvalidOperationException("Party.StaleAuthority");
        if (System.Text.Json.JsonSerializer.Serialize(Transactions) != System.Text.Json.JsonSerializer.Serialize(other.Transactions))
            throw new InvalidOperationException("Party.N.WitnessMismatch");
        if (Root != other.Root || Seed != other.Seed || !Acts.SequenceEqual(other.Acts) ||
            !Bosses.Select(b => (b.Act, b.Ordinal, b.BossKey)).SequenceEqual(other.Bosses.Select(b => (b.Act, b.Ordinal, b.BossKey))) ||
            Players.Count != other.Players.Count || Players.Where((p, i) => p.Slot != other.Players[i].Slot ||
                p.Character != other.Players[i].Character || p.UnlockSource != other.Players[i].UnlockSource || !p.Offers.SequenceEqual(other.Players[i].Offers)).Any())
            throw new InvalidOperationException("Party.FilterInformationMismatch");
    }

    public static PartySeedInformation Project(string seed, OrderedPartyAuthority party)
    {
        var profile = Beta111Profile.Instance;
        if (!profile.TryCanonicalizeSeed(seed, out var canonical, out var issue)) throw new ArgumentException(issue);
        ulong root = profile.ComputeRootSeed(canonical);
        var owner = party.Players[0];
        var world = Beta109WorldPredictionProvider.Predict(canonical, owner.Ascension, 0,
            owner.Character.CharacterKey, party.World, AncientOptionConditionProfile.BroadDefault);
        if (world.BossStatus != SeedDomainEvaluationStatus.Evaluated || world.Bosses.Any(b => !b.SearchIdentityReplayable))
            throw new InvalidOperationException("Party.WorldProjectionIncomplete:" + world.BossIssueCode);
        var generation = Beta109WorldSnapshotProjector.ProjectForSeed(party.World.Beta109Generation!, canonical);
        var players = party.Players.Select(p =>
        {
            var offers = ModernNeowIdentityPredictor.PredictModernCore(root, p, profile, p.IsBeta111NeowIdentityAuthorityExact);
            if (!offers.ExactAuthority || offers.UnknownEligibilityAffectedPool) throw new InvalidOperationException("Party.NeowAuthorityIncomplete");
            return new PlayerInitialOffers(p.PlayerSlotIndex, p.Character.CharacterKey, offers.RelicKeys) { UnlockSource = party.UnlockSources[p.PlayerSlotIndex] };
        }).ToArray();
        return new(canonical, root, owner.GameVersion, owner.Ascension, party.Fingerprint, generation.SelectedActs, world.Bosses, players);
    }
}
