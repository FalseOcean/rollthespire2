using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Relics;

/// <summary>Pure ordinary treasure replay and a separate initial-order reference.</summary>
public static class TreasureRelicPredictor
{
    private static readonly RelicSequenceKind[] Kinds =
        [RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare];

    public static TreasureRelicPredictionResult Predict(
        IRuntimeProfile profile,
        ulong rootHash,
        RelicSequencePredictionResult initialSequences,
        IReadOnlyList<ModelKey> acquiredOpeningRelics,
        int count = TreasureRelicPredictionResult.MaximumCount)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(initialSequences);
        ArgumentNullException.ThrowIfNull(acquiredOpeningRelics);
        if (count is < 1 or > TreasureRelicPredictionResult.MaximumCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (acquiredOpeningRelics.Any(key => !key.IsValid || key.Category != "RELIC"))
            throw new ArgumentException("Opening acquisitions must be exact relic identities.", nameof(acquiredOpeningRelics));
        return Replay(profile, rootHash, initialSequences, acquiredOpeningRelics, count,
            "treasure-relic.ordinary-singleplayer-first-two-before-floor41");
    }

    public static InitialTreasureRelicPreviewResult PredictInitialOrderPreview(
        IRuntimeProfile profile,
        ulong rootHash,
        RelicSequencePredictionResult initialSequences)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(initialSequences);
        var result = Replay(profile, rootHash, initialSequences, [], InitialTreasureRelicPreviewResult.PreviewCount,
            "treasure-relic.initial-order-nine-singleplayer-before-floor41-reference");
        return new(result.ProfileId, result.Status, result.Entries, result.Precision, result.Authority,
            result.Completeness, result.IssueCode, result.RngCallCount, result.EvidenceCode);
    }

    private static TreasureRelicPredictionResult Replay(
        IRuntimeProfile profile,
        ulong rootHash,
        RelicSequencePredictionResult initialSequences,
        IReadOnlyList<ModelKey> acquiredOpeningRelics,
        int count,
        string evidenceCode)
    {
        if (!profile.SupportsSeedRng || !RuntimeProfilePolicies.IsModernCore(profile.ProfileId))
            return Unavailable(profile.ProfileId, "TreasureRelicProfileUnsupported", unsupported: true);
        if (initialSequences.ProfileId != profile.ProfileId)
            return Unavailable(profile.ProfileId, "TreasureRelicSequenceProfileMismatch");
        if (initialSequences.Status != SeedDomainEvaluationStatus.Evaluated)
            return Unavailable(profile.ProfileId, "TreasureRelicInitialSequencesUnavailable");

        var bags = new List<ModelKey>[Kinds.Length];
        bool exact = true;
        for (int index = 0; index < Kinds.Length; index++)
        {
            var matches = initialSequences.TreasureRoomLanes.Where(lane => lane.Kind == Kinds[index]).ToArray();
            if (matches.Length != 1)
                return Unavailable(profile.ProfileId, "TreasureRelicSharedLaneMissingOrAmbiguous:" + Kinds[index]);
            var lane = matches[0];
            if (lane.Precision is not (PredictionPrecision.Exact or PredictionPrecision.Partial) ||
                lane.RarityCode != Kinds[index].ToString() ||
                lane.PullDirection != RelicSequencePullDirection.Front ||
                lane.TotalCount != lane.FullEntries.Count ||
                lane.FullEntries.Where((entry, position) => entry.Position != position + 1 ||
                    !entry.RelicKey.IsValid || entry.RelicKey.Category != "RELIC" ||
                    entry.Precision is not (PredictionPrecision.Exact or PredictionPrecision.Partial)).Any())
                return Unavailable(profile.ProfileId, "TreasureRelicSharedLaneIncomplete:" + Kinds[index]);
            exact &= lane.Precision == PredictionPrecision.Exact &&
                lane.FullEntries.All(entry => entry.Precision == PredictionPrecision.Exact) &&
                lane.Completeness == SnapshotCompleteness.Complete &&
                SourceAuthorityRules.SupportsExactIdentity(lane.Authority);
            // Native Remove removes every matching identity. Native IsAllowed
            // removes Massive Scroll in single-player; all other vanilla shared
            // bag restrictions allow this explicit non-tutorial, pre-floor-41 scope.
            bags[index] = lane.FullEntries.Select(entry => entry.RelicKey)
                .Where(key => !acquiredOpeningRelics.Contains(key) && key.Entry != "MASSIVE_SCROLL").ToList();
        }

        PredictionPrecision precision = exact ? PredictionPrecision.Exact : PredictionPrecision.Partial;
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(rootHash, TreasureRelicPredictionResult.RngStream));
        bool silverCrucible = acquiredOpeningRelics.Contains(new ModelKey("RELIC", "SILVER_CRUCIBLE"));
        var output = new TreasureRelicEntryResult[count];
        for (int position = 0; position < count; position++)
        {
            if (silverCrucible && position == 0)
            {
                output[position] = new(position + 1, null, string.Empty, string.Empty,
                    TreasureRelicSource.SilverCrucibleSuppression, precision, "treasure-relic.silver-crucible.first-encounter");
                continue;
            }
            float roll = rng.NextFloat();
            int rolledLane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
            int actualLane = rolledLane;
            while (actualLane < bags.Length && bags[actualLane].Count == 0) actualLane++;
            if (actualLane == bags.Length)
            {
                output[position] = new(position + 1, new ModelKey("RELIC", "CIRCLET"), Kinds[rolledLane].ToString(),
                    "None", TreasureRelicSource.ExhaustedBagCirclet, precision, "treasure-relic.exhausted-bag-circlet");
                continue;
            }
            ModelKey relic = bags[actualLane][0];
            bags[actualLane].RemoveAt(0);
            output[position] = new(position + 1, relic, Kinds[rolledLane].ToString(), Kinds[actualLane].ToString(),
                TreasureRelicSource.SharedBag, precision, "treasure-relic.shared-bag.front");
        }
        return new(profile.ProfileId, SeedDomainEvaluationStatus.Evaluated, Array.AsReadOnly(output), precision,
            exact ? initialSequences.Authority : SourceAuthority.Incomplete,
            exact ? SnapshotCompleteness.Complete : SnapshotCompleteness.Partial,
            string.Empty, rng.CallCount, evidenceCode);
    }

    private static TreasureRelicPredictionResult Unavailable(RuntimeProfileId profileId, string issue, bool unsupported = false) =>
        new(profileId, unsupported ? SeedDomainEvaluationStatus.Unsupported : SeedDomainEvaluationStatus.Unknown,
            Array.Empty<TreasureRelicEntryResult>(), unsupported ? PredictionPrecision.Unsupported : PredictionPrecision.Unknown,
            SourceAuthority.Unknown, SnapshotCompleteness.Missing, issue, 0, "treasure-relic.unavailable");
}
