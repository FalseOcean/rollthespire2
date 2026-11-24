using System.Collections.Immutable;
using System.Diagnostics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.World.Beta109;

internal static class CrystalSeedGoalChecks
{
    private const RngSemanticProfileId Profile = RngSemanticProfileId.Modern113;
    private static readonly CrystalSphereSeedTargets AllCards =
        new(CrystalSphereSeedCards.Common | CrystalSphereSeedCards.Uncommon | CrystalSphereSeedCards.Rare, null);
    private static readonly CrystalSphereSeedTargets RelicAndRare =
        new(CrystalSphereSeedCards.Rare, EffectRelicRarity.Rare);

    internal static int Run()
    {
        var (context, state) = CreateReplayFixture();
        bool checkedThreeCards = false, checkedRelicAndCard = false, checkedShiftedRelic = false;
        int retryBoards = 0, relicMatches = 0, relicUnknown = 0;
        long relicTicks = 0;
        for (ulong root = 0; root < 64; root++)
        {
            int slot = (int)(root % 2);
            var originalRng = EventRng(root, slot);
            var afterMenu = PredictorStreamState.Capture(PredictorStream.Rewards, originalRng);
            var original = LegacyBoard(originalRng);
            if (!original.PlacedAll) retryBoards++;
            var paid = PredictorRun.CreateCrystalBranch(context, state, afterMenu, false, 51);
            var debt = PredictorRun.CreateCrystalBranch(context, state, afterMenu, true, 51);
            foreach (var branch in new[] { paid, debt })
                Check(branch.PlacedAllItems == original.PlacedAll && branch.Hidden.SequenceEqual(original.Hidden) &&
                    branch.Occupancy.SequenceEqual(original.Occupancy) && branch.Items.SequenceEqual(original.Items) &&
                    branch.EventRng == PredictorStreamState.Capture(PredictorStream.Rewards, originalRng),
                    $"Shared layout drift at root {root} slot {slot}");
            Check(paid.Remaining == 3 && debt.Remaining == 6, "Original branch budgets changed");
            Check(CrystalSphereSeedSearch.Evaluate(root, slot, Profile, 3, RelicAndRare).Verdict == CrystalSphereSeedVerdict.NoMatch,
                "Three reveals returned a 4x4 relic");

            var three = CrystalSphereSeedSearch.Evaluate(root, slot, Profile, 3, AllCards);
            if (three.Witness is { } cards)
            {
                Verify(paid, cards, ["Common", "Uncommon", "Rare"], null);
                checkedThreeCards = true;
            }
            long start = Stopwatch.GetTimestamp();
            var six = CrystalSphereSeedSearch.Evaluate(root, slot, Profile, 6, RelicAndRare);
            relicTicks += Stopwatch.GetTimestamp() - start;
            if (six.Verdict == CrystalSphereSeedVerdict.Unknown) relicUnknown++;
            if (six.Witness is { } relicAndCard)
            {
                checkedShiftedRelic |= Verify(debt, relicAndCard, ["Rare"], EffectRelicRarity.Rare);
                checkedRelicAndCard = true;
                relicMatches++;
            }
        }
        Check(checkedThreeCards && checkedRelicAndCard && checkedShiftedRelic,
            $"Missing coverage for same-board multi-card or a shifted relic roll ({checkedThreeCards}/{checkedRelicAndCard}/{checkedShiftedRelic})");
        CheckRepeatedRelic();

        var capped = CrystalSphereSeedSearch.Evaluate(0, 0, Profile, 6, new(0, EffectRelicRarity.Rare), stateLimit: 0);
        Check(capped.Verdict == CrystalSphereSeedVerdict.Unknown && capped.Witness == null && capped.Reason != null,
            "Work cap was promoted to a confirmed disposition");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { CrystalSphereSeedSearch.Evaluate(0, 0, Profile, 6, RelicAndRare, cancelled.Token); }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancelled.Token)
        {
            Console.WriteLine($"CRYSTAL_SEED_GOALS 64 legacy/branch layouts ({retryBoards} retry boards), repeated relic, same-board card/relic replay, shifted roll, cutoff and cancellation PASS");
            Console.WriteLine($"CRYSTAL_SEED_RELIC_SAMPLE rare+rare card, six reveals, 64 roots: match={relicMatches} noMatch={64-relicMatches-relicUnknown} unknown={relicUnknown} solveMs={relicTicks * 1000d / Stopwatch.Frequency:F1}");
            return 0;
        }
        throw new InvalidOperationException("Crystal search ignored cancellation");
    }

    private static void CheckRepeatedRelic()
    {
        UInt128 cells = PredictorCrystalBoard.Rectangle(4, 4, 4, 4);
        var required = CrystalSphereSeedTargets.WithRelicRarities(CrystalSphereSeedCards.None,
            [EffectRelicRarity.Common, EffectRelicRarity.Uncommon]);
        var single = new PredictorCrystalBoard([new(PredictorCrystalItemKind.Relic, 4, 4, 4, 4, cells, 1)], true);
        var repeated = single with
        {
            Items = [single.Items[0] with { Subscriptions = 10 }],
            PlacedAllItems = false
        };
        var rng = new Xoshiro256StarStar(0);
        Check(new CrystalSphereSeedSolver(single, rng, 6, required, default, 24_000).Solve().Verdict == CrystalSphereSeedVerdict.NoMatch,
            "A single roll satisfied two distinct rarity requests");
        var multiple = new CrystalSphereSeedSolver(repeated, rng, 6, required, default, 24_000).Solve();
        var rolls = multiple.Witness?.Rewards.Single().RelicRolls ?? [];
        Check(multiple.Verdict == CrystalSphereSeedVerdict.Match && rolls.Length == 10 &&
              rolls.Contains(EffectRelicRarity.Common) && rolls.Contains(EffectRelicRarity.Uncommon),
            "One repeatedly subscribed relic must be able to generate both requested rolls");
    }

    private static bool Verify(PredictorCrystalSnapshot source, CrystalSphereSeedWitness witness,
        string[] requiredCardRarities, EffectRelicRarity? requiredRoll)
    {
        Check(witness.Steps.Length == source.Remaining &&
              witness.EventCallsAfterPlacement == source.EventRng.Calls, "Witness used a different entry state");
        var replay = PredictorCrystalExplorer.Replay(source, witness.Steps);
        var completed = replay.ExportCrystal();
        Check(completed.Remaining == 0 &&
              completed.Revealed.SequenceEqual(witness.Rewards.SelectMany(item => Enumerable.Repeat(item.ItemIndex, item.Copies))),
            "Detached click settlement disagreed with witness reveal order or multiplicity");
        var actualCardRarities = replay.Rewards.Where(reward => reward.Kind == PredictorRewardKind.Card)
            .SelectMany(reward => reward.Cards).Select(card => PredictorCardChanges.Definition(replay.Context, card.Key).Rarity);
        Check(requiredCardRarities.All(actualCardRarities.Contains), "One complete play did not generate every requested card rarity");

        // The detached settlement owns actual offers and may fall back in the bag.
        // Cross-check the raw roll at its native position before applying that bag.
        var rng = source.EventRng.Restore();
        int potions = completed.Revealed.Count(index => source.Items[index].Kind is "POTION_COMMON" or "POTION_RARE");
        for (int i = 0; i < potions; i++) rng.NextInt(1);
        var rolls = new List<EffectRelicRarity>();
        foreach (int index in completed.Revealed)
            switch (source.Items[index].Kind)
            {
                case "RELIC": rolls.Add(Roll(rng.NextFloat())); break;
                case "CARD_COMMON" or "CARD_UNCOMMON" or "CARD_RARE":
                    for (int i = 0; i < 3; i++) { rng.NextInt(1); rng.NextFloat(); }
                    break;
                case "GOLD_SMALL" or "GOLD_BIG": rng.NextInt(1); break;
            }
        Check(completed.EventRng == PredictorStreamState.Capture(PredictorStream.Rewards, rng) &&
              rolls.SequenceEqual(witness.Rewards.SelectMany(reward => reward.RelicRolls)) &&
              replay.Rewards.Count(reward => reward.Kind == PredictorRewardKind.Relic) == rolls.Count &&
              (requiredRoll == null || rolls.Contains(requiredRoll.Value)),
            "Relic roll or final RNG differs from detached settlement and ordered reward replay");
        return requiredRoll != null && Roll(source.EventRng.Restore().NextFloat()) != requiredRoll;
    }

    private static EffectRelicRarity Roll(float value) => value < .5f ? EffectRelicRarity.Common
        : value < .83f ? EffectRelicRarity.Uncommon : EffectRelicRarity.Rare;

    internal static Xoshiro256StarStar EventRng(ulong root, int slot)
    {
        var rng = new Xoshiro256StarStar(Beta109WorldRng.DeriveEventLocalSeed(root, slot, false, "CRYSTAL_SPHERE"));
        rng.NextInt(49);
        return rng;
    }

    // Row-major reference for the original detached assistant, including failed
    // cumulative attempts that leave prior placements and repeated subscriptions.
    private static (bool PlacedAll, bool[] Hidden, int[] Occupancy, List<PredictorCrystalItem> Items) LegacyBoard(Xoshiro256StarStar rng)
    {
        var hidden = Enumerable.Repeat(true, 121).ToArray();
        var clear = new[] { (0, 0), (10, 0), (10, 10), (0, 10) }.AsEnumerable();
        for (int round = 0; round < 2; round++)
            clear = clear.SelectMany(cell => new[] { cell, (cell.Item1 - 1, cell.Item2), (cell.Item1 + 1, cell.Item2),
                (cell.Item1, cell.Item2 - 1), (cell.Item1, cell.Item2 + 1) })
                .Where(cell => cell.Item1 is >= 0 and < 11 && cell.Item2 is >= 0 and < 11).ToArray();
        foreach (var (x, y) in clear) hidden[x * 11 + y] = false;
        var occupancy = Enumerable.Repeat(-1, 121).ToArray();
        (string Name, int Width, int Height)[] shapes =
        [
            ("RELIC", 4, 4), ("POTION_COMMON", 1, 3), ("POTION_COMMON", 1, 3),
            ("POTION_RARE", 2, 2), ("CARD_COMMON", 2, 2), ("CARD_UNCOMMON", 2, 2),
            ("CARD_RARE", 2, 2), ("CURSE", 2, 2),
            ("GOLD_SMALL", 1, 1), ("GOLD_SMALL", 1, 1), ("GOLD_SMALL", 1, 1), ("GOLD_SMALL", 1, 1),
            ("GOLD_SMALL", 1, 1), ("GOLD_BIG", 2, 1), ("GOLD_BIG", 2, 1)
        ];
        var items = new List<PredictorCrystalItem>();
        bool placedAll = false;
        for (int attempt = 0; attempt < 10 && !placedAll; attempt++)
        {
            placedAll = true;
            foreach (var shape in shapes)
            {
                int x = -1, y = -1;
                if (placedAll)
                {
                    var candidates = new List<(int X, int Y)>();
                    for (int i = 0; i + shape.Width <= 11; i++)
                        for (int j = 0; j + shape.Height <= 11; j++)
                            if (Cells(i, j, shape.Width, shape.Height).All(cell => hidden[cell] && occupancy[cell] < 0))
                                candidates.Add((i, j));
                    placedAll = candidates.Count != 0;
                    if (placedAll)
                    {
                        (x, y) = candidates[rng.NextInt(candidates.Count)];
                        foreach (int cell in Cells(x, y, shape.Width, shape.Height)) occupancy[cell] = items.Count;
                    }
                }
                items.Add(new(shape.Name, shape.Width, shape.Height, x, y, 0, false));
            }
            for (int i = 0; i < items.Count; i++) items[i] = items[i] with { Subscriptions = items[i].Subscriptions + 1 };
        }
        return (placedAll, hidden, occupancy, items);

        static IEnumerable<int> Cells(int x, int y, int width, int height)
        {
            for (int i = x; i < x + width; i++)
                for (int j = y; j < y + height; j++) yield return i * 11 + j;
        }
    }

    internal static (PredictorContext Context, PredictorState State) CreateReplayFixture(int cardsPerRarity = 3)
    {
        var cards = new List<NeowEffectCardSnapshot>();
        var definitions = new List<PredictorCardDefinition>();
        foreach (var rarity in new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare })
            for (int i = 0; i < cardsPerRarity; i++)
            {
                var key = new ModelKey("CARD", $"CRYSTAL_{rarity.ToString().ToUpperInvariant()}_{i}");
                cards.Add(new(key.Entry, key, cards.Count, rarity, EffectCardType.Skill, false, false, false, true, true));
                definitions.Add(new(new(0, key, EffectCardType.Skill, 0, 1, true, 0), rarity.ToString(), default, false, false, false)
                {
                    Levels = [new(0, 1, false, false, false, false, false, false), new(1, 1, false, false, false, false, false, false)]
                });
            }
        foreach (string curse in new[] { "DEBT", "DOUBT" })
            definitions.Add(new(new(0, new("CARD", curse), EffectCardType.Curse, 0, 0, true, 0), "Curse", default, false, false, false)
            {
                Levels = [new(0, 0, false, true, false, false, false, false)]
            });
        var context = new PredictorContext("000000000000", "0.113.1", BaseGameModelKeys.Characters.Ironclad, 10,
            "crystal-seed-fixture", "unlocks", [], [], [], 0)
        {
            Crystal = new(1, 0, 1, ImmutableHashSet<ModelKey>.Empty),
            Catalog = new(cards.ToImmutableArray(), []) { CardDefinitions = definitions.ToImmutableArray() },
            PotionPool = [new(new("POTION", "BLOCK_POTION"), PredictorPotionRarity.Common),
                new(new("POTION", "DUPLICATION_POTION"), PredictorPotionRarity.Rare)]
        };
        var streams = Enum.GetValues<PredictorStream>().Select(stream =>
            PredictorStreamState.Capture(stream, new Xoshiro256StarStar((ulong)stream + 1))).ToImmutableArray();
        return (context, new PredictorState(1, new(2, 20, "crystal-seed-fixture"), 150, [], [], [], [],
            streams, new(0f, 0f), PredictorRelicBag.Empty(false), PredictorRelicBag.Empty(false)));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
