using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class UiSurfaceChecks
{
    // Runs in the existing isolated native Neow fixture, before hover workers.
    // Mutation probes are restored immediately. This is a same-state capture
    // comparison, not an in-game frame/performance acceptance claim.
    private static void RunOperationFreshnessChecks(RunState run, string version, Node root, string output)
    {
        var player = LocalContext.GetMe(run)!;
        int checks = 0;
        void Check(bool value, string reason)
        { checks++; if (!value) throw new InvalidOperationException("OperationFreshness: " + reason); }
        string Native() => JsonSerializer.Serialize(new
        {
            Players = run.Players.Select(p => p.ToSerializable()), Rng = run.Rng.ToSerializable(),
            PersonalBag = PredictorLiveRunCapture.Bag(player.RelicGrabBag),
            SharedBag = PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
            Account = SaveManager.Instance.Progress.WongoPoints,
            EventRng = run.CurrentRoom is EventRoom room ? room.LocalMutableEvent.Rng.ToSerializable() : null
        });
        var mutations = new List<object>();
        void Mutation(string name, Action change, Action restore, bool changesResult = true)
        {
            var snapshot = PredictorOperationCapture.Capture(run, version, root);
            string original = Native();
            try
            {
                change();
                string changed = Native();
                var authoritative = PredictorOperationCapture.Capture(run, version, root);
                var timing = new PredictorOperationCapture.CaptureTiming();
                bool current = PredictorOperationCapture.IsCurrent(snapshot, run, version, root, timing);
                bool fullCurrent = authoritative.Fingerprint == snapshot.Fingerprint;
                Check(fullCurrent != changesResult && current == fullCurrent, name + " preserves the full-capture freshness decision");
                Check(changed == Native(), name + " validation does not mutate the native state");
                mutations.Add(new { name, changesResult, current, fullCurrent, timing.CatalogLevelReads, timing.CatalogLevelReuses });
            }
            finally { restore(); }
            Check(original == Native(), name + " probe restores the native fixture");
        }

        // Warm both paths before alternating their order to reduce first-use
        // and ordering bias. Every sample checks the full original fingerprint.
        var baseline = PredictorOperationCapture.Capture(run, version, root);
        string before = Native();
        PredictorOperationCapture.Capture(run, version, root);
        Check(PredictorOperationCapture.IsCurrent(baseline, run, version, root), "warm publication remains current");
        var samples = new List<object>();
        void Sample(string path, int sample)
        {
            var timing = new PredictorOperationCapture.CaptureTiming();
            long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            bool current = path == "full"
                ? PredictorOperationCapture.Capture(run, version, root, timing).Fingerprint == baseline.Fingerprint
                : PredictorOperationCapture.IsCurrent(baseline, run, version, root, timing);
            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Check(current, path + " returns exactly the original fingerprint at unchanged state");
            Check(path == "full" ? timing.CatalogLevelReads > 0 && timing.CatalogLevelReuses == 0
                : timing.CatalogLevelReads == 0 && timing.CatalogLevelReuses > 0,
                path + " performs the declared stable metadata work");
            samples.Add(new { path, sample, elapsedMs, allocatedBytes, timing.CatalogMs,
                timing.CompleteCatalogMs, timing.NativeStampMs, timing.HashMs, timing.PoolReads,
                timing.CatalogLevelReads, timing.CatalogLevelReuses });
        }
        for (int sample = 0; sample < 8; sample++)
        {
            Sample(sample % 2 == 0 ? "full" : "publication", sample);
            Sample(sample % 2 == 0 ? "publication" : "full", sample);
        }
        Check(before == Native(), "all measured captures leave the native state untouched");

        int gold = player.Gold;
        Mutation("gold", () => player.Gold = gold + 1, () => player.Gold = gold);
        int hp = player.Creature.CurrentHp;
        Mutation("hp", () => player.Creature.SetCurrentHpInternal(hp - 1), () => player.Creature.SetCurrentHpInternal(hp));
        var rng = player.PlayerRng.Transformations;
        var rngState = rng.ToSerializable();
        Mutation("transformation-rng", () => rng.NextInt(), () => rng.LoadFromSerializable(rngState));
        var sharedRng = run.Rng.Niche;
        var sharedState = sharedRng.ToSerializable();
        Mutation("shared-rng", () => sharedRng.NextInt(), () => sharedRng.LoadFromSerializable(sharedState));
        foreach (var (name, bag) in new[] { ("personal-bag", player.RelicGrabBag), ("shared-bag", run.SharedRelicGrabBag) })
        {
            var queues = PredictorOperationCapture.Read<Dictionary<RelicRarity, List<RelicModel>>>(bag, "_deques");
            var queue = queues.Values.First(q => q.Count > 0);
            var relic = queue[0];
            Mutation(name, () => queue.RemoveAt(0), () => queue.Insert(0, relic));
        }
        var deck = PredictorOperationCapture.Read<List<CardModel>>(player.Deck, "_cards");
        Mutation("ordered-deck", () => (deck[0], deck[1]) = (deck[1], deck[0]),
            () => (deck[0], deck[1]) = (deck[1], deck[0]));
        var upgradable = deck.First(c => c.IsUpgradable && c.CurrentUpgradeLevel == 0 && c.Enchantment == null);
        Mutation("deck-upgrade", () => { upgradable.UpgradeInternal(); upgradable.FinalizeUpgradeInternal(); },
            upgradable.DowngradeInternal);
        int points = SaveManager.Instance.Progress.WongoPoints;
        Mutation("account-wongo", () => SaveManager.Instance.Progress.WongoPoints = points + 1,
            () => SaveManager.Instance.Progress.WongoPoints = points);
        if (run.CurrentRoom is EventRoom eventRoom)
        {
            var eventRng = eventRoom.LocalMutableEvent.Rng;
            var eventState = eventRng.ToSerializable();
            // The already-generated Ancient offer scene does not consume its
            // event RNG. Verify unchanged authority rather than claiming every
            // observable native change is a result dependency.
            Mutation("unused-ancient-offer-event-rng", () => eventRng.NextInt(),
                () => eventRng.LoadFromSerializable(eventState), changesResult: false);
            var options = eventRoom.LocalMutableEvent.CurrentOptions;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = typeof(EventModel).GetField("_currentOptions", flags)!;
            var originalOptions = field.GetValue(eventRoom.LocalMutableEvent);
            Mutation("generated-option-order", () => field.SetValue(eventRoom.LocalMutableEvent, options.Reverse().ToList()),
                () => field.SetValue(eventRoom.LocalMutableEvent, originalOptions));
        }
        // A pool change is absent from the old NativeStamp. Publication must
        // still re-read and hash its real ordered membership, even while every
        // native canonical level definition is safely reused.
        var pool = player.Character.CardPool;
        var poolCards = pool.AllCards.ToArray();
        var cardsField = typeof(CardPoolModel).GetField("_allCards", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalCards = cardsField.GetValue(pool);
        Mutation("catalog-pool-order", () => cardsField.SetValue(pool, Enumerable.Reverse(poolCards).ToArray()),
            () => cardsField.SetValue(pool, originalCards));
        var crucible = (SilverCrucible)ModelDb.Relic<SilverCrucible>().ToMutable();
        // Allocate its stable capture identity before taking the baseline so
        // monotonically allocated test identities do not contaminate a probe.
        PredictorLiveRunCapture.Id(crucible);
        player.AddRelicInternal(crucible, silent: true);
        try
        {
            Mutation("live-relic-hook-state", () => crucible.TimesUsed = 1, () => crucible.TimesUsed = 0);
        }
        finally { player.RemoveRelicInternal(crucible, silent: true); }
        Check(before == Native(), "all mutation probes together restore the full original native fixture signature");
        Check(PredictorOperationCapture.IsCurrent(PredictorOperationCapture.Capture(run, version, root), run, version, root),
            "publication remains current after restoring all probes");
        File.WriteAllText(Path.Combine(output, "operation-freshness-result.json"), JsonSerializer.Serialize(new
        {
            passed = true, checks, samples, mutations,
            scope = "controlled existing native Neow fixture; warm same-state synchronous main-thread capture, not player frame timing",
            reuse = "only native immutable canonical card level metadata, scoped to the original snapshot",
            reread = "all current catalog/pool projections, context, native identities, RNG, bags, deck/relic state, options/selection and scene"
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"OPERATION_FRESHNESS_PASS checks={checks};mutations={mutations.Count};samples={samples.Count}");
    }
}
