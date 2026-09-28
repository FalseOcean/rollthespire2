using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World.Beta109;

internal static class Beta109EncounterSequenceProjection
{
    // Vanilla 0.111 Overgrowth.ApplyActDiscoveryOrderModifications. This applies
    // to the completed queue, consumes no RNG and does not modify replay history.
    internal static ModelKey[] ApplyFirstRunOrder(IEnumerable<ModelKey> source, bool elite)
    {
        var items = source.ToArray();
        string[] prefix = elite ? ["BYRDONIS_ELITE", "PHROG_PARASITE_ELITE"] :
            ["NIBBITS_WEAK", "SLIMES_WEAK", "SHRINKER_BEETLE_WEAK", "INKLETS_NORMAL", "MAWLER_NORMAL", "RUBY_RAIDERS_NORMAL", "NIBBITS_NORMAL"];
        for (int i = 0; i < Math.Min(items.Length, prefix.Length); i++)
        {
            var key = new ModelKey("ENCOUNTER", prefix[i]);
            int existing = Array.IndexOf(items, key);
            if (existing >= 0) (items[i], items[existing]) = (items[existing], items[i]);
            else items[i] = key;
        }
        return items;
    }

    internal static ActEncounterSequenceResult Build(Beta109ActGenerationSnapshot act,
        IReadOnlyList<Beta109EncounterEntrySnapshot> normal, IReadOnlyList<Beta109EncounterEntrySnapshot> elite,
        Beta109WorldGenerationSnapshot snapshot, WorldAuthoritySnapshot world)
    {
        ModelKey[] normalKeys = normal.Select(e => e.EncounterKey).ToArray(), eliteKeys = elite.Select(e => e.EncounterKey).ToArray();
        bool overgrowth = act.ActKey.Entry == "OVERGROWTH";
        bool needsDiscoveryFact = overgrowth && (!snapshot.TestModeFactExact || snapshot.TestModeIsOff);
        if (needsDiscoveryFact && world.EncounterNumberOfRuns is null)
            return new(act.Act, act.ActKey, [], [], PredictionPrecision.Unknown, "EncounterDiscoveryContextMissing");
        if (needsDiscoveryFact && world.EncounterNumberOfRuns == 0)
        {
            if (snapshot.Profile != Compatibility.RuntimeProfileId.Beta111 || !snapshot.TestModeFactExact || !snapshot.ModeFactsExact || snapshot.GameMode != WorldGameMode.Singleplayer)
                return new(act.Act, act.ActKey, [], [], PredictionPrecision.Unknown, "EncounterDiscoveryContextMissing");
            normalKeys = ApplyFirstRunOrder(normalKeys, false);
            eliteKeys = ApplyFirstRunOrder(eliteKeys, true);
        }
        var precision = snapshot.HasExactReplayInputs && act.HasExactGenerationInputs && snapshot.UpFrontPrefix.PriorInputsExact &&
            SourceAuthorityRules.SupportsExactIdentity(world.SourceAuthority) && world.Completeness == SnapshotCompleteness.Complete
            ? PredictionPrecision.Exact : PredictionPrecision.Partial;
        return new(act.Act, act.ActKey,
            normalKeys.Select((key, i) => new EncounterSequenceEntryResult(i + 1, key)).ToArray(),
            eliteKeys.Select((key, i) => new EncounterSequenceEntryResult(i + 1, key)).ToArray(), precision, "");
    }
}
