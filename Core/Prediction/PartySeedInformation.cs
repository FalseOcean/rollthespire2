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
    public bool ObservationRequested { get; init; } = true;
    public string UnlockSource { get; init; } = "CapturedProfile";
    public PredictionPrecision IdentityPrecision { get; init; } = PredictionPrecision.Exact;
    public string IssueCode { get; init; } = string.Empty;
}
public sealed record PartySeedInformation(string Seed, ulong Root, string Version, int Ascension,
    string PartyFingerprint, IReadOnlyList<ModelKey> Acts, IReadOnlyList<BossPredictionResult> Bosses,
    IReadOnlyList<PlayerInitialOffers> Players)
{
    public bool ActObservationRequested { get; init; } = true;
    public bool BossObservationRequested { get; init; } = true;
    public PredictionPrecision ActIdentityPrecision { get; init; } = PredictionPrecision.Exact;
    public SeedDomainEvaluationStatus BossStatus { get; init; } = SeedDomainEvaluationStatus.Evaluated;
    public string BossIssueCode { get; init; } = string.Empty;
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
        if (Root != other.Root || Seed != other.Seed || ActObservationRequested != other.ActObservationRequested ||
            BossObservationRequested != other.BossObservationRequested || ActIdentityPrecision != other.ActIdentityPrecision ||
            BossStatus != other.BossStatus || BossIssueCode != other.BossIssueCode || !Acts.SequenceEqual(other.Acts) ||
            !Bosses.Select(b => (b.Act, b.Ordinal, b.BossKey)).SequenceEqual(other.Bosses.Select(b => (b.Act, b.Ordinal, b.BossKey))) ||
            Players.Count != other.Players.Count || Players.Where((p, i) => p.Slot != other.Players[i].Slot ||
                p.Character != other.Players[i].Character || p.UnlockSource != other.Players[i].UnlockSource ||
                p.ObservationRequested != other.Players[i].ObservationRequested ||
                p.IdentityPrecision != other.Players[i].IdentityPrecision || p.IssueCode != other.Players[i].IssueCode || !p.Offers.SequenceEqual(other.Players[i].Offers)).Any())
            throw new InvalidOperationException("Party.FilterInformationMismatch");
    }

    public static PartySeedInformation Project(string seed, OrderedPartyAuthority party) =>
        Project(seed, party, includeActs: true, includeBosses: true,
            party.Players.Select(p => p.PlayerSlotIndex).ToHashSet());

    internal static PartySeedInformation Project(string seed, OrderedPartyAuthority party,
        bool includeActs, bool includeBosses, IReadOnlySet<int> neowSlots)
    {
        var profile = Beta111Profile.Instance;
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(profile, seed, out var canonical, out var issue)) throw new ArgumentException(issue);
        ulong root = profile.ComputeRootSeed(canonical);
        var owner = party.Players[0];
        // Bosses retain their complete up_front replay, including the preceding
        // event/encounter shuffles and Ancient identity draws. Acts alone do not.
        var world = includeBosses ? Beta109WorldPredictionProvider.Predict(canonical, owner.Ascension, 0,
            owner.Character.CharacterKey, party.World, AncientOptionConditionProfile.BroadDefault) : null;
        includeActs |= includeBosses;
        var generation = includeActs
            ? Beta109WorldSnapshotProjector.ProjectForSeed(party.World.Beta109Generation!, canonical) : null;
        var players = party.Players.Select(p =>
        {
            if (!neowSlots.Contains(p.PlayerSlotIndex))
                return new PlayerInitialOffers(p.PlayerSlotIndex, p.Character.CharacterKey, [])
                {
                    ObservationRequested = false,
                    UnlockSource = party.UnlockSources[p.PlayerSlotIndex],
                    IdentityPrecision = PredictionPrecision.DescriptionOnly,
                    IssueCode = "Party.NeowObservationNotRequested"
                };
            if (!p.HasExactModernNeowIdentityInputs)
                return new PlayerInitialOffers(p.PlayerSlotIndex, p.Character.CharacterKey, [])
                {
                    UnlockSource = party.UnlockSources[p.PlayerSlotIndex],
                    IdentityPrecision = p.VanillaNeowCatalogExact == false || p.NoRunModifiers == false
                        ? PredictionPrecision.Unsupported : PredictionPrecision.Unknown,
                    IssueCode = p.VanillaNeowCatalogExact == false || p.NoRunModifiers == false
                        ? "Party.NeowModelUnsupported" : "Party.NeowAuthorityIncomplete"
                };
            var offers = ModernNeowIdentityPredictor.PredictModernCore(root, p, profile, p.IsBeta111NeowIdentityAuthorityExact);
            bool exact = offers.ExactAuthority && !offers.UnknownEligibilityAffectedPool;
            return new PlayerInitialOffers(p.PlayerSlotIndex, p.Character.CharacterKey, offers.RelicKeys)
            {
                UnlockSource = party.UnlockSources[p.PlayerSlotIndex],
                IdentityPrecision = exact ? PredictionPrecision.Exact : PredictionPrecision.Unknown,
                IssueCode = exact ? string.Empty : "Party.NeowAuthorityIncomplete"
            };
        }).ToArray();
        return new(canonical, root, owner.GameVersion, owner.Ascension, party.Fingerprint,
            generation?.SelectedActs ?? [], world?.Bosses ?? [], players)
        {
            ActObservationRequested = includeActs,
            BossObservationRequested = includeBosses,
            ActIdentityPrecision = generation is null ? PredictionPrecision.DescriptionOnly
                : generation.SelectedActsExact ? PredictionPrecision.Exact : PredictionPrecision.Unknown,
            BossStatus = world is null ? SeedDomainEvaluationStatus.Unknown
                : world.BossStatus == SeedDomainEvaluationStatus.Evaluated && world.Bosses.Any(b => !b.SearchIdentityReplayable)
                ? SeedDomainEvaluationStatus.Unknown : world.BossStatus,
            BossIssueCode = world is null ? "Party.BossObservationNotRequested"
                : string.IsNullOrWhiteSpace(world.BossIssueCode) && world.Bosses.Any(b => !b.SearchIdentityReplayable)
                ? "Party.BossIdentityNotReplayable" : world.BossIssueCode
        };
    }
}
