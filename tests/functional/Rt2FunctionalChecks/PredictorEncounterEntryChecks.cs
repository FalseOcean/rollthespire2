using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorEncounterEntryChecks
{
    internal static void Run(PredictorRun captured)
    {
        ulong root = Beta111Profile.Instance.ComputeRootSeed(captured.Context.Seed);
        var field = typeof(EncounterModel).GetField("_rng", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Native encounter RNG field missing");
        var stranglerCounts = new HashSet<int>();
        foreach (var canonical in ModelDb.AllEncounters)
        {
            var key = new ModelKey(canonical.Id.Category, canonical.Id.Entry);
            var method = canonical.GetType().GetMethod("GenerateMonsters", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Native encounter generator missing: " + key);
            var floors = key.Entry == "SLITHERING_STRANGLER_NORMAL" ? Enumerable.Range(1, 25) : new[] { 1, 12, 25 };
            foreach (int totalFloor in floors)
            {
                var instance = (EncounterModel)canonical.ToMutable();
                ulong localSeed = unchecked(root + (ulong)totalFloor +
                    StringHelper.GetDeterministicHashCode(key.Entry));
                field.SetValue(instance, new Rng(localSeed));
                var generated = method.Invoke(instance, null) as IEnumerable
                    ?? throw new InvalidOperationException("Native encounter roster missing: " + key);
                int nativeCount = generated.Cast<object>().Count();
                if (key.Entry == "SLITHERING_STRANGLER_NORMAL") stranglerCounts.Add(nativeCount);
                int predictedCount = PredictorEncounterEntry.InitialEnemyCount(key, root, totalFloor);
                if (nativeCount != predictedCount)
                    throw new InvalidOperationException($"Encounter initial count mismatch: {key}, floor {totalFloor}, native {nativeCount}, predictor {predictedCount}");
            }
        }
        if (!stranglerCounts.SetEquals(new[] { 2, 3 }))
            throw new InvalidOperationException("Encounter variable roster cases not covered");
        foreach (int count in new[] { 0, 1, 2, 3, 4, 5, 10 })
        {
            var before = captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche);
            var native = new Rng(new SerializableRng { counter = before.Calls,
                state0 = before.S0, state1 = before.S1, state2 = before.S2, state3 = before.S3 });
            for (int index = 0; index < count; index++) _ = native.NextInt(1);
            var actual = PredictorEncounterEntry.ConsumeInitialEnemyHp(captured.Working, count)
                .Streams.Single(s => s.Stream == PredictorStream.Niche);
            var expected = native.ToSerializable();
            if ((actual.Calls, actual.S0, actual.S1, actual.S2, actual.S3) !=
                (expected.counter, expected.state0, expected.state1, expected.state2, expected.state3))
                throw new InvalidOperationException("Encounter initial Niche continuation mismatch: " + count);
        }
        var niche = captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var nativeEnemy = new Rng(new SerializableRng { counter = niche.Calls,
            state0 = niche.S0, state1 = niche.S1, state2 = niche.S2, state3 = niche.S3 });
        var egg = (ToughEgg)ModelDb.Monster<ToughEgg>().ToMutable();
        var spawned = new Creature(egg, CombatSide.Enemy, null);
        spawned.SetUniqueMonsterHpValue(Array.Empty<Creature>(), nativeEnemy);
        var derived = PredictorCombatEnemyFacts.Apply(captured.Working,
            [new(PredictorEnemyCreationSource.LaidEggs, 1)], default);
        CompareEnemyNiche(derived, nativeEnemy, "Created enemy HP Niche continuation");
        // ToughEgg.Hatch consumes one separate Niche bounded HP draw on the
        // existing creature, without calling CreateCreature again.
        _ = nativeEnemy.NextInt(egg.HatchlingMinHp, egg.HatchlingMaxHp + 1);
        derived = PredictorCombatEnemyFacts.Apply(derived,
            [new(PredictorEnemyCreationSource.HatchedEggs, 1)], default);
        CompareEnemyNiche(derived, nativeEnemy, "ToughEgg hatch Niche continuation");
    }

    private static void CompareEnemyNiche(PredictorState state, Rng native, string source)
    {
        var actual = state.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var expected = native.ToSerializable();
        if ((actual.Calls, actual.S0, actual.S1, actual.S2, actual.S3) !=
            (expected.counter, expected.state0, expected.state1, expected.state2, expected.state3))
            throw new InvalidOperationException(source);
    }
}
