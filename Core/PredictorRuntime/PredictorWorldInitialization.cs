using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorRuntimeWorld(ImmutableArray<PredictorActState> Acts,
    Beta109RngStateSnapshot FinalUpFrontState, bool StartedWithNeow);

internal static class PredictorWorldInitialization
{
    // A Standard run without NeowEpoch begins at a Monster point. The source
    // NextItem(empty) returns null without advancing RNG; the Search world
    // authority deliberately requires a nonempty Ancient pool. This Runtime
    // replay uses the same numerical donors with the actual empty pool.
    internal static PredictorRuntimeWorld Generate(PredictorContext context, WorldAuthoritySnapshot world)
    {
        bool startedWithNeow = context.RevealedEpochs.Contains("NEOW_EPOCH");
        var source = world.Beta109Generation ?? throw new InvalidOperationException("PredictorRuntimeWorldSnapshotMissing");
        var snapshot = Beta109WorldSnapshotProjector.ProjectForSeed(source, context.Seed);
        var missing = new List<string>();
        if (snapshot.Profile != RuntimeProfileId.Beta111) missing.Add("Profile");
        if (!snapshot.DirectSourceAudited) missing.Add("DirectSourceAudited");
        if (!snapshot.IsVanilla) missing.Add("IsVanilla");
        if (snapshot.GameMode != WorldGameMode.Singleplayer || !snapshot.ModeFactsExact) missing.Add("SingleplayerModeFacts");
        if (!snapshot.TestModeIsOff) missing.Add("TestModeIsOff");
        if (!snapshot.SelectedActsExact || snapshot.SelectedActs.Count != 3) missing.Add("SelectedActs");
        if (!snapshot.HasExactUpFrontPrefix) missing.Add("UpFrontPrefix");
        if (!snapshot.RunSeedRootExact) missing.Add("RunSeedRoot");
        if (!snapshot.EventAuthority.HasExactFilteringAuthority) missing.Add("EventFiltering");
        if (!snapshot.SharedEventCatalogExact) missing.Add("SharedEventCatalog");
        if (!snapshot.SharedAncientCatalogExact || !snapshot.AllSharedAncientCatalogExact) missing.Add("SharedAncientCatalog");
        if (!snapshot.UnlockFactsExact) missing.Add("UnlockFacts");
        if (!snapshot.WorldGenerationHooksExact) missing.Add("WorldGenerationHooks");
        if (snapshot.PlayerCount != 1) missing.Add("PlayerCount");
        if (missing.Count != 0)
            throw new InvalidOperationException("PredictorRuntimeWorldAuthorityIncomplete:" + string.Join(',', missing));
        var acts = snapshot.SelectedActs.Select(key => snapshot.OrderedActCatalog.SingleOrDefault(a => a.ActKey == key)
            ?? throw new InvalidOperationException("PredictorRuntimeActCatalogMissing:" + key)).ToArray();
        for (int i = 0; i < acts.Length; i++)
        {
            var a = acts[i];
            // Empty Act-1 Ancient is the only allowed deviation from the
            // Search-oriented ordinary-generation contract.
            bool exact = a.ActKey.IsValid && a.CatalogOrderExact && a.UnlockFilteringExact &&
                a.RoomShapeExact && a.GenerateAllEncountersOrderExact && a.EncounterClassificationExact &&
                a.EncounterTagIdentityExact && a.EncounterTagComparerExact &&
                a.EncounterReferenceIdentityExact && a.EncounterWeightModelExact &&
                a.EncounterRetryShapeExact && a.EncounterCatalogOfficialVanilla &&
                a.EliteEncounterSlots == 15 && a.EventRngConsumptionExact &&
                a.WeakEncounters.Count > 0 && a.RegularEncounters.Count > 0 &&
                a.EliteEncounters.Count > 0 && a.Bosses.Count > 0 &&
                (a.OrderedAncients.Count > 0 || i == 0 && !startedWithNeow);
            if (!exact) throw new InvalidOperationException("PredictorRuntimeActAuthorityIncomplete:" + a.ActKey + ":" + a.EncounterAuthorityEvidenceCode);
        }

        Beta109WorldRng rng = Beta109WorldPredictionProvider.CreateUpFront(snapshot);
        var shared = Beta109WorldPredictionProvider.AssignSharedAncients(snapshot.SelectedActs,
            snapshot.OrderedActCatalog, snapshot.SharedAncients, rng);
        var output = ImmutableArray.CreateBuilder<PredictorActState>(acts.Length);
        foreach (var act in acts)
        {
            var ancientPool = act.OrderedAncients.ToList();
            if (shared.TryGetValue(act.Act, out var assigned)) ancientPool.AddRange(assigned);
            if (!EventPoolSequenceProjector.TryPrepare(act.Act, act.ActKey, act.EventRngConsumptionExact,
                    act.OrderedRawEvents, snapshot.EventAuthority.OrderedSharedEventsRaw,
                    snapshot.EventAuthority, act.OrderedEligibleEvents, out var prepared, out string issue) || prepared is null)
                throw new InvalidOperationException("PredictorRuntimeEventAuthorityIncomplete:" + act.ActKey + ":" + issue);
            int eventStart = rng.CallCount;
            rng.UnstableShuffle(prepared.Candidates, $"act{act.Act}:event-shuffle");
            var immutableEventContext = new EventImmutableEligibilityContext(snapshot.GameMode, true, 1, true,
                snapshot.EventAuthority.CharacterCardPoolCount,
                snapshot.EventAuthority.CharacterCardPoolCountExact);
            var eventProjection = EventPoolSequenceProjector.Complete(snapshot.Profile, prepared,
                new EventEffectiveCandidateContext(ancientPool.Count > 0 ? 1 : 0, true,
                    ancientPool.Count > 0 ? "AncientOpening" : "NoAncientMonsterOpening", immutableEventContext),
                world.SourceAuthority, world.Completeness, eventStart, rng.CallCount,
                "predictor.runtime.event-source-shuffle");
            var events = eventProjection.RawEntries.Select(e => e.EventKey).ToArray();

            var normalHistory = new List<Beta109EncounterEntrySnapshot>();
            Beta109WorldPredictionProvider.ConsumeEncounterQueue(act.WeakEncounters, act.WeakEncounterSlots,
                rng, normalHistory, $"act{act.Act}:weak");
            Beta109WorldPredictionProvider.ConsumeEncounterQueue(act.RegularEncounters,
                Math.Max(0, act.TotalNormalRooms - act.WeakEncounterSlots), rng, normalHistory,
                $"act{act.Act}:regular");
            var eliteHistory = new List<Beta109EncounterEntrySnapshot>();
            Beta109WorldPredictionProvider.ConsumeEncounterQueue(act.EliteEncounters, act.EliteEncounterSlots,
                rng, eliteHistory, $"act{act.Act}:elite");
            ModelKey drawnBoss = rng.NextModelKey(act.Bosses, $"act{act.Act}:boss:first");
            ModelKey ancient = ancientPool.Count == 0 ? default :
                rng.NextModelKey(ancientPool, $"act{act.Act}:ancient:identity");

            var normal = normalHistory.Select(e => e.EncounterKey).ToArray();
            var elite = eliteHistory.Select(e => e.EncounterKey).ToArray();
            if (context.NumberOfRuns == 0 && act.ActKey.Entry == "OVERGROWTH")
            {
                normal = Beta109EncounterSequenceProjection.ApplyFirstRunOrder(normal, elite: false);
                elite = Beta109EncounterSequenceProjection.ApplyFirstRunOrder(elite, elite: true);
                SwapToFront(events, "BYRDONIS_NEST", 0);
                SwapToFront(events, "SAPPHIRE_SEED", 1);
            }
            ModelKey boss = FirstUnseenBoss(act.ActKey.Entry, context.SeenEncounters) ?? drawnBoss;
            var bosses = new List<ModelKey> { boss };
            if (act.Act == acts.Length && context.Ascension >= 10)
            {
                var secondPool = act.Bosses.Where(k => k != boss).ToArray();
                bosses.Add(rng.NextModelKey(secondPool, $"act{act.Act}:boss:second-a10"));
            }
            output.Add(new PredictorActState(act.Act, act.ActKey, ancient, bosses.ToImmutableArray(),
                normal.ToImmutableArray(), elite.ToImmutableArray(), events.ToImmutableArray()));
        }
        return new(output.ToImmutable(), rng.CaptureState(), startedWithNeow);
    }

    private static void SwapToFront(ModelKey[] entries, string entry, int target)
    {
        var key = new ModelKey("EVENT", entry);
        int old = Array.IndexOf(entries, key);
        if (old >= 0) (entries[target], entries[old]) = (entries[old], entries[target]);
        else entries[target] = key;
    }

    private static ModelKey? FirstUnseenBoss(string act, ImmutableArray<ModelKey> seen)
    {
        string[] order = act switch
        {
            "OVERGROWTH" => ["VANTOM_BOSS", "CEREMONIAL_BEAST_BOSS", "THE_KIN_BOSS"],
            "UNDERDOCKS" => ["WATERFALL_GIANT_BOSS", "SOUL_FYSH_BOSS", "LAGAVULIN_MATRIARCH_BOSS"],
            "HIVE" => ["THE_INSATIABLE_BOSS", "KNOWLEDGE_DEMON_BOSS", "KAISER_CRAB_BOSS"],
            "GLORY" => ["QUEEN_BOSS", "TEST_SUBJECT_BOSS", "AEONGLASS_BOSS"],
            _ => throw new InvalidOperationException("PredictorFirstRunBossDiscoveryActUnknown:" + act)
        };
        foreach (string entry in order)
        {
            var key = new ModelKey("ENCOUNTER", entry);
            if (!seen.Contains(key)) return key;
        }
        return null;
    }
}
