using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Random;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

internal static partial class UiSurfaceChecks
{
    private static void VerifyPredictorEncounterQueues(SearchContext context)
    {
        var source = context.Authority.WorldAuthority!;
        var add = typeof(ActModel).GetMethod("AddWithoutRepeatingTags", BindingFlags.Static | BindingFlags.NonPublic)!;
        int checks = 0, firstRuns = 0;
        foreach (string seed in new[] { "00000103CP3W", "000000000001", "000000000002", "000000000003", "000000000004", "000000000005" })
        {
            WorldPredictionResult Predict(int? runs) => WorldPredictionEngine.Predict(Beta111Profile.Instance, seed, 10, 0,
                context.CharacterKey, source with { EncounterNumberOfRuns = runs }, context.Authority.EffectAuthority);
            var result = Predict(1);
            var first = Predict(0);
            var unknown = Predict(null);
            if (JsonSerializer.Serialize(result.Bosses) != JsonSerializer.Serialize(first.Bosses) || JsonSerializer.Serialize(result.Ancients) != JsonSerializer.Serialize(first.Ancients))
                throw new Exception("Encounter projection changed downstream RNG");
            foreach (var sequence in result.EncounterSequences)
            {
                var act = ModelDb.GetById<ActModel>(new(sequence.ActKey.Category, sequence.ActKey.Entry));
                var catalog = source.Beta109Generation!.OrderedActCatalog.First(a => a.ActKey == sequence.ActKey);
                int before = result.EventPoolSequencePrediction!.Acts.First(a => a.Act == sequence.Act).RngCallCountAfter;
                var rng = new Rng(unchecked(Beta111Profile.Instance.ComputeRootSeed(seed) + RolltheSpire2.Core.Seed.XxHash64.HashUtf8("up_front", 0)));
                for (int i = 0; i < before; i++) rng.NextDouble();
                var normal = new List<EncounterModel>(); var elite = new List<EncounterModel>();
                void Generate(IEnumerable<EncounterModel> pool, int count, List<EncounterModel> output)
                {
                    var bag = new GrabBag<EncounterModel>();
                    for (int i = 0; i < count; i++)
                    {
                        if (bag.Count == 0) foreach (var e in pool) bag.Add(e, 1);
                        add.Invoke(null, [output, bag, rng]);
                    }
                }
                Generate(act.AllWeakEncounters, catalog.WeakEncounterSlots, normal);
                Generate(act.AllRegularEncounters, catalog.TotalNormalRooms - catalog.WeakEncounterSlots, normal);
                Generate(act.AllEliteEncounters, catalog.EliteEncounterSlots, elite);
                ModelKey Key(EncounterModel e) => new(e.Id.Category, e.Id.Entry);
                if (!sequence.Normal.Select(e => e.EncounterKey).SequenceEqual(normal.Select(Key)) || !sequence.Elite.Select(e => e.EncounterKey).SequenceEqual(elite.Select(Key)))
                    throw new Exception("Vanilla encounter sequence mismatch: " + seed + "/" + sequence.ActKey);
                int expectedCursor = result.Bosses.First(b => b.Act == sequence.Act && b.Ordinal == 1).RngCallCount - 1;
                if (rng.ToSerializable().counter != expectedCursor) throw new Exception("Vanilla encounter RNG mismatch");
                checks += normal.Count + elite.Count;
                if (sequence.ActKey.Entry != "OVERGROWTH") continue;
                var mutable = (ActModel)act.ToMutable();
                var rooms = (MegaCrit.Sts2.Core.Rooms.RoomSet)typeof(ActModel).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mutable)!;
                rooms.normalEncounters.AddRange(normal); rooms.eliteEncounters.AddRange(elite); rooms.events.AddRange(act.AllEvents);
                mutable.GetType().GetMethod("ApplyActDiscoveryOrderModifications", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(mutable, [new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0)]);
                var projected = first.EncounterSequences.First(a => a.Act == sequence.Act);
                if (!projected.Normal.Select(e => e.EncounterKey).SequenceEqual(rooms.normalEncounters.Select(Key)) || !projected.Elite.Select(e => e.EncounterKey).SequenceEqual(rooms.eliteEncounters.Select(Key)))
                    throw new Exception("First-run encounter override mismatch");
                if (unknown.EncounterSequences.First(a => a.Act == sequence.Act).Normal.Count != 0) throw new Exception("Missing discovery fact invented a queue");
                firstRuns++;
            }
        }
        if (firstRuns == 0) throw new Exception("First-run fixture not exercised");
        Godot.GD.Print($"PREDICTOR_ENCOUNTER_PARITY_PASS identities={checks};firstRunCases={firstRuns};vanillaRngCursor=true;bossAncientUnchanged=true");
    }
}
