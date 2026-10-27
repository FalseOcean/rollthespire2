using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorObtainChecks
{
    internal static void Run(PredictorRun captured)
    {
        var context = captured.Context;
        if (context.OpeningEligibleRelics.Length < 3 || context.Catalog?.UnlockedCurseCards.Length < 1)
            throw new InvalidOperationException("Obtain authority pools missing");
        var entry = captured.Working;
        var rewardStream = entry.Streams.Single(s => s.Stream == PredictorStream.Rewards);
        var nativeRewards = Native(rewardStream);
        var eligible = context.OpeningEligibleRelics.Where(k => k.Entry != "NEOWS_BONES").ToList();
        eligible.UnstableShuffle(nativeRewards);
        var bones = PredictorObtainSources.Bones(context, entry, default);
        if (!bones.Rewards.Select(r => r.Key).SequenceEqual(eligible.Take(2)))
            throw new InvalidOperationException("Bones native Rewards shuffle mismatch");
        Compare(bones.State.Streams.Single(s => s.Stream == PredictorStream.Rewards), nativeRewards,
            "Bones Rewards continuation");

        var nicheStream = bones.State.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var nativeNiche = Native(nicheStream);
        var curses = context.Catalog!.UnlockedCurseCards.ToList();
        var selected = nativeNiche.NextItem(curses);
        var cursed = PredictorObtainSources.BonesCurse(context, bones.State);
        if (cursed.Deck.Length != bones.State.Deck.Length + 1 || cursed.Card(cursed.Deck[^1]).Key != selected)
            throw new InvalidOperationException("Bones native final curse mismatch");
        Compare(cursed.Streams.Single(s => s.Stream == PredictorStream.Niche), nativeNiche,
            "Bones Niche continuation");
        SeaGlassGeneration(captured);
        ScrollBoxesGeneration(captured);
        KaleidoscopePoolOrder(captured);
        OptionalObtainProjection(captured);
        FrameUnwind(captured);
        LeafyLocalHealth(captured);
        LateCardRewardRelics(captured);
        LavaLampLateBoundary(captured);
        StableUpgradeSources(captured);
        RoyalStampCandidateOrder(captured);
        LuckyFyshCloneGold(captured);
        BookOfFiveRingsDeckEntry(captured);
        DarkstoneCurseDeckEntry(captured);
        TouchStarterReplacement(captured);
        ClawsMaulEnchantEligibility(captured);
        ToyBoxLifecycle(captured);
        CombatRelicDeckUpgrades(captured);
        Console.WriteLine("NATIVE_OBTAIN Bones shuffle/curse, SeaGlass/ScrollBoxes identities, Kaleidoscope pools/RNG; nested unwind, Leafy local death, WingCharm Niche, stable upgrade, Claws Maul enchant and local LavaLamp boundary PASS");
    }

    private static void OptionalObtainProjection(PredictorRun captured)
    {
        foreach (string key in new[] { "GNARLED_HAMMER", "KIFUDA" })
        {
            var run = new PredictorRun(captured.Context, captured.Working with { OpeningCompleted = true });
            run.EnterRewardNode(new(1, 2, "optional-obtain"),
                [new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Event, new("RELIC", key), [])]);
            long reward = run.Rewards.Single().Id;
            Select(run, new TakeReward(reward));
            if (run.Phase != PredictorPhase.ObtainDeckCards || run.Project().DeckSelection!.MinimumCardCount != 0)
                throw new InvalidOperationException("Optional obtain projected a forced minimum");
            var deck = run.Working.Deck.Select(run.Working.Card).ToArray();
            Select(run, new ChooseObtainDeckCards([]));
            if (run.Rewards.Single(r => r.Id == reward).Status != PredictorRewardStatus.Taken ||
                !run.Working.Deck.Select(run.Working.Card).SequenceEqual(deck))
                throw new InvalidOperationException("Empty optional obtain did not complete unchanged Deck");
        }
        Console.WriteLine("RUNTIME_OPTIONAL_OBTAIN_PROJECTION GnarledHammer/Kifuda min0 -> explicit zero-card confirm -> parent Taken PASS");
    }

    private static void LateCardRewardRelics(PredictorRun captured)
    {
        var cards = captured.Working.Deck.Select(captured.Working.Card).ToList();
        var eligible = Enumerable.Range(0, cards.Count)
            .Where(i => PredictorObtainSources.CanEnchant(captured.Context, cards[i])).ToArray();
        if (eligible.Length == 0) throw new InvalidOperationException("WingCharm comparison has no eligible cards");
        var native = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche));
        int chosen = eligible[native.NextInt(eligible.Length)];
        var wing = PredictorCardRewardRelicHooks.ApplyLate(captured.Context, captured.Working,
            cards.ToList(), new("RELIC", "WING_CHARM"), cloneOnChange: false);
        if (wing.Cards.Where(c => c.Enchantment?.Entry == "SWIFT").Count() != 1 ||
            wing.Cards[chosen].Enchantment?.Entry != "SWIFT")
            throw new InvalidOperationException("WingCharm late eligible option mismatch");
        Compare(wing.State.Streams.Single(s => s.Stream == PredictorStream.Niche), native,
            "WingCharm late Niche continuation");
        var fresnel = PredictorCardRewardRelicHooks.ApplyLate(captured.Context, captured.Working,
            cards.ToList(), new("RELIC", "FRESNEL_LENS"), cloneOnChange: false);
        for (int i = 0; i < cards.Count; i++)
        {
            bool expected = PredictorObtainSources.GainsBlockForFresnel(captured.Context, cards[i]) &&
                PredictorObtainSources.CanEnchant(captured.Context, cards[i]);
            if ((fresnel.Cards[i].Enchantment?.Entry == "NIMBLE") != expected)
                throw new InvalidOperationException("Fresnel late option mismatch");
        }
        var glitter = PredictorCardRewardRelicHooks.ApplyLate(captured.Context, captured.Working,
            cards.ToList(), new("RELIC", "GLITTER"), cloneOnChange: false);
        for (int i = 0; i < cards.Count; i++)
            if ((glitter.Cards[i].Enchantment?.Entry == "GLAM") !=
                PredictorObtainSources.CanEnchant(captured.Context, cards[i]))
                throw new InvalidOperationException("Glitter late option mismatch");
    }

    private static void LavaLampLateBoundary(PredictorRun captured)
    {
        var cards = captured.Working.Deck.Select(captured.Working.Card).ToList();
        long id = captured.Working.NextInstanceId;
        var lamp = new PredictorRelic(id, new("RELIC", "LAVA_LAMP"), captured.Working.Position.Floor);
        var state = captured.Working with { CurrentRoomIsCombat = true,
            Relics = captured.Working.Relics.Add(lamp), NextInstanceId = id + 1 };
        var clean = PredictorCardRewardRelicHooks.ApplyLate(captured.Context, state,
            cards.ToList(), lamp.Key, cloneOnChange: false, relicId: id);
        for (int i = 0; i < cards.Count; i++)
        {
            int expected = cards[i].UpgradeLevel < cards[i].MaxUpgradeLevel ? cards[i].UpgradeLevel + 1 : cards[i].UpgradeLevel;
            if (clean.Cards[i].UpgradeLevel != expected) throw new InvalidOperationException("LavaLamp clean upgrade mismatch");
        }
        var damaged = state with { Relics = state.Relics.SetItem(state.Relics.Length - 1, lamp with { TookDamageThisCombat = true }) };
        if (!PredictorCardRewardRelicHooks.ApplyLate(captured.Context, damaged,
            cards.ToList(), lamp.Key, cloneOnChange: false, relicId: id).Cards.SequenceEqual(cards))
            throw new InvalidOperationException("LavaLamp damage boundary mismatch");
        if (!PredictorCardRewardRelicHooks.ApplyLate(captured.Context, state with { CurrentRoomIsCombat = false },
            cards.ToList(), lamp.Key, cloneOnChange: false, relicId: id).Cards.SequenceEqual(cards))
            throw new InvalidOperationException("LavaLamp room boundary mismatch");
    }

    private static void StableUpgradeSources(PredictorRun captured)
    {
        var expected = captured.Working.Deck.Select(captured.Working.Card)
            .Where(c => c.UpgradeLevel < c.MaxUpgradeLevel)
            .OrderBy(c => c.Key.Category, StringComparer.Ordinal)
            .ThenBy(c => c.Key.Entry, StringComparer.Ordinal)
            .ThenBy(c => c.UpgradeLevel).ToList();
        var native = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche));
        expected.UnstableShuffle(native);
        var actual = PredictorObtainSources.FragrantMushroom(captured.Context, captured.Working, 6);
        if (!actual.CardIds.SequenceEqual(expected.Take(6).Select(c => c.Id)))
            throw new InvalidOperationException("SandCastle stable upgrade order mismatch");
        Compare(actual.State.Streams.Single(s => s.Stream == PredictorStream.Niche), native,
            "SandCastle stable Niche continuation");
    }

    private static void RoyalStampCandidateOrder(PredictorRun captured)
    {
        var eligible = captured.Working.Deck.Where(id =>
            PredictorObtainSources.CanEnchant(captured.Context, captured.Working.Card(id)) &&
            captured.Working.Card(id).Type is EffectCardType.Attack or EffectCardType.Skill).ToImmutableArray();
        var native = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche));
        var expected = eligible.ToList();
        expected.UnstableShuffle(native);
        var actual = PredictorObtainSources.RoyalStampCandidates(captured.Working, eligible);
        if (!actual.Candidates.SequenceEqual(expected))
            throw new InvalidOperationException("RoyalStamp native Niche candidate order mismatch");
        Compare(actual.State.Streams.Single(s => s.Stream == PredictorStream.Niche), native,
            "RoyalStamp candidate Niche continuation");
    }

    private static void LuckyFyshCloneGold(PredictorRun captured)
    {
        var source = captured.Working.Card(captured.Working.Deck[0]);
        long next = captured.Working.NextInstanceId;
        var state = captured.Working with
        {
            Relics = ImmutableArray.Create(new PredictorRelic(next, new("RELIC", "BING_BONG"), 1))
                .Add(new(next + 1, new("RELIC", "LUCKY_FYSH"), 1))
                .Add(new(next + 2, new("RELIC", "DRAGON_FRUIT"), 1)),
            NextInstanceId = next + 3
        };
        var added = PredictorObtainSources.CloneCardToDeck(captured.Context, state, source);
        if (added.Deck.Length != state.Deck.Length + 2 || added.GoldBudget != state.GoldBudget + 30 ||
            added.CurrentHealthEffects.Length != state.CurrentHealthEffects.Length + 2 ||
            added.CurrentHealthEffects.Skip(state.CurrentHealthEffects.Length).Any(e =>
                e.SourceKey.Entry != "DRAGON_FRUIT" || e.MaxHpDelta != 1 || e.HealRequested != 1))
            throw new InvalidOperationException("LuckyFysh/BingBong/DragonFruit card-add chain mismatch");
        long bingCloneId = added.Deck[^1];
        var bing = added.Relics.Single(r => r.Key.Entry == "BING_BONG");
        if (!bing.BingBongCardsToSkip.Contains(bingCloneId))
            throw new InvalidOperationException("BingBong cloned-card skip was not retained");
        var removed = added with { Deck = added.Deck.Remove(bingCloneId) };
        var returned = PredictorObtainSources.AddExistingCard(captured.Context, removed, removed.Card(bingCloneId));
        if (returned.Deck.Length != added.Deck.Length || returned.GoldBudget != added.GoldBudget + 15 ||
            returned.Relics.Single(r => r.Key.Entry == "BING_BONG").BingBongCardsToSkip.Contains(bingCloneId))
            throw new InvalidOperationException("BingBong stolen clone re-entry copied twice or retained skip");

        var hoarderContext = captured.Context with { Modifiers = captured.Context.Modifiers.Add(
            new PredictorRunModifier(new("MODIFIER", "HOARDER"))) };
        var hoarderEntry = captured.Working with { Relics = [], HoarderCardsToSkip = [] };
        var hoarded = PredictorObtainSources.CloneCardToDeck(hoarderContext, hoarderEntry, source);
        if (hoarded.Deck.Length != hoarderEntry.Deck.Length + 3 || hoarded.HoarderCardsToSkip.Length != 2)
            throw new InvalidOperationException("Hoarder original plus two clones mismatch");
        long hoarderCloneId = hoarded.Deck[^1];
        var hoarderRemoved = hoarded with { Deck = hoarded.Deck.Remove(hoarderCloneId) };
        var hoarderReturned = PredictorObtainSources.AddExistingCard(hoarderContext, hoarderRemoved,
            hoarderRemoved.Card(hoarderCloneId));
        if (hoarderReturned.Deck.Length != hoarded.Deck.Length ||
            hoarderReturned.HoarderCardsToSkip.Contains(hoarderCloneId))
            throw new InvalidOperationException("Hoarder cloned-card re-entry copied twice or retained skip");
    }

    private static void BookOfFiveRingsDeckEntry(PredictorRun captured)
    {
        var source = captured.Working.Card(captured.Working.Deck[0]);
        var book = new PredictorRelic(captured.Working.NextInstanceId,
            new("RELIC", "BOOK_OF_FIVE_RINGS"), 1) { BookOfFiveRingsCardsAdded = 4 };
        var state = captured.Working with { Relics = [book],
            NextInstanceId = captured.Working.NextInstanceId + 1 };
        var added = PredictorObtainSources.CloneCardToDeck(captured.Context, state, source);
        if (added.Relics[0].BookOfFiveRingsCardsAdded != 5 ||
            added.CurrentHealthEffects.Length != state.CurrentHealthEffects.Length + 1 ||
            added.CurrentHealthEffects[^1] is not { MaxHpDelta: 0, HealRequested: 20 } effect ||
            effect.SourceKey.Entry != "BOOK_OF_FIVE_RINGS")
            throw new InvalidOperationException("BookOfFiveRings fifth Deck entry heal mismatch");
    }

    private static void DarkstoneCurseDeckEntry(PredictorRun captured)
    {
        var curse = captured.Context.Catalog!.UnlockedCurseCards[0];
        long next = captured.Working.NextInstanceId;
        var state = captured.Working with { Relics = [
            new PredictorRelic(next, new("RELIC", "DARKSTONE_PERIAPT"), 1),
            new PredictorRelic(next + 1, new("RELIC", "BING_BONG"), 1)],
            NextInstanceId = next + 2 };
        var added = PredictorObtainSources.AddCard(captured.Context, state, curse);
        var health = added.CurrentHealthEffects.Skip(state.CurrentHealthEffects.Length).ToArray();
        if (added.Deck.Length != state.Deck.Length + 2 || health.Length != 2 ||
            health.Any(e => e.SourceKey.Entry != "DARKSTONE_PERIAPT" || e.MaxHpDelta != 6 || e.HealRequested != 6))
            throw new InvalidOperationException("DarkstonePeriapt curse clone max-HP chain mismatch");
    }

    private static void TouchStarterReplacement(PredictorRun captured)
    {
        foreach (var (starter, upgrade) in new[]
        {
            ("BURNING_BLOOD", "BLACK_BLOOD"), ("RING_OF_THE_SNAKE", "RING_OF_THE_DRAKE"),
            ("DIVINE_RIGHT", "DIVINE_DESTINY"), ("BOUND_PHYLACTERY", "PHYLACTERY_UNBOUND"),
            ("CRACKED_CORE", "INFUSED_CORE")
        })
        {
            long id = captured.Working.NextInstanceId;
            var state = captured.Working with { Relics = [new PredictorRelic(id, new("RELIC", starter), 1)],
                NextInstanceId = id + 1 };
            var run = new PredictorRun(captured.Context, state);
            if (!run.BeginObtainRelic(new("RELIC", "TOUCH_OF_OROBAS"), null,
                PredictorObtainReturn.Ancient, 77))
                throw new InvalidOperationException("Touch starter replacement unexpectedly deferred: " + starter);
            if (run.Working.Relics.Length != 2 || run.Working.Relics[0].Key.Entry != upgrade ||
                run.Working.Relics[1].Key.Entry != "TOUCH_OF_OROBAS")
                throw new InvalidOperationException("Touch starter inventory replacement mismatch: " + starter);
        }
    }

    private static void ClawsMaulEnchantEligibility(PredictorRun captured)
    {
        var original = captured.Working.Card(captured.Working.Deck[0]);
        foreach (var enchantment in ModelDb.DebugEnchantments.Where(e =>
            e.GetType().Namespace == "MegaCrit.Sts2.Core.Models.Enchantments" && e.Id.Entry != "DEPRECATED_ENCHANTMENT"))
        {
            var nativeMaul = ModelDb.Card<Maul>().ToMutable();
            bool expected = enchantment.CanEnchant(nativeMaul);
            var proposed = original with { Enchantment = new("ENCHANTMENT", enchantment.Id.Entry), EnchantmentAmount = 1 };
            var actual = PredictorObtainSources.MaulFromOriginal(captured.Context, proposed);
            if ((actual.Enchantment?.Entry == enchantment.Id.Entry) != expected)
                throw new InvalidOperationException("Claws Maul enchant compatibility mismatch: " + enchantment.Id.Entry);
        }
    }

    private static void ToyBoxLifecycle(PredictorRun captured)
    {
        var manager = RunManager.Instance;
        var stateProperty = typeof(RunManager).GetProperty("State", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!;
        object? saved = stateProperty.GetValue(manager);
        var ascensionProperty = typeof(RunManager).GetProperty("AscensionManager", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
        object? savedAscension = ascensionProperty.GetValue(manager);
        try
        {
            var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
            var native = RunState.CreateForTest([player], acts: captured.Working.Acts.Select(a =>
                ModelDb.GetById<ActModel>(new(a.Key.Category, a.Key.Entry))).ToArray(),
                seed: captured.Context.Seed, ascensionLevel: captured.Context.Ascension);
            stateProperty.SetValue(manager, native);
            ascensionProperty.SetValue(manager, new AscensionManager(captured.Context.Ascension));
            native.SharedRelicGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(UnlockState.all), native.Rng.UpFront);
            player.PopulateRelicGrabBagIfNecessary(native.Rng.UpFront);
            var nativeToy = ModelDb.Relic<ToyBox>().ToMutable();
            player.AddRelicInternal(nativeToy, silent: true);
            player.RelicGrabBag.Remove(nativeToy);
            native.SharedRelicGrabBag.Remove(nativeToy);
            var actual = new PredictorRun(captured.Context, captured.Working);
            if (actual.BeginObtainRelic(new("RELIC", "TOY_BOX"), null, PredictorObtainReturn.Ancient, 910))
                throw new InvalidOperationException("ToyBox did not suspend for five relic rewards");
            var expected = Enumerable.Range(0, 5).Select(_ => RelicFactory.PullNextRelicFromFront(player).Id.Entry).ToArray();
            if (!actual.Rewards.Select(r => r.Key.Entry).SequenceEqual(expected) ||
                actual.Rewards.Length != 5 || actual.Rewards.Any(r => !r.IsWaxRelic))
                throw new InvalidOperationException("ToyBox native five-front offers/wax metadata mismatch");
            Compare(actual.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards), player.PlayerRng.Rewards,
                "ToyBox five pulls Rewards continuation");

            var toyState = captured.Working with { OpeningCompleted = true, Relics = captured.Working.Relics.Add(
                new(captured.Working.NextInstanceId, new("RELIC", "TOY_BOX"), 1)),
                NextInstanceId = captured.Working.NextInstanceId + 1 };
            var pure = new PredictorRun(captured.Context, toyState);
            pure.EnterRewardNode(new(1, 2, "toybox-wax-take"),
                [new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Event,
                    new("RELIC", "CIRCLET"), []) { IsWaxRelic = true }]);
            Select(pure, new TakeReward(pure.Rewards[0].Id));
            var wax = pure.Working.Relics.Single(r => r.Key.Entry == "CIRCLET");
            if (!wax.IsWax) throw new InvalidOperationException("ToyBox wax flag lost on shared Obtain");
            Select(pure, new LeaveRewards());

            var nativeWax = ModelDb.Relic<Circlet>().ToMutable();
            nativeWax.IsWax = true;
            player.AddRelicInternal(nativeWax, silent: true);
            for (int battle = 0; battle < 3; battle++)
            {
                var encounter = pure.Working.Acts[0].NormalEncounters[pure.Working.Acts[0].NormalCursor];
                var room = new MegaCrit.Sts2.Core.Rooms.CombatRoom(ModelDb.GetById<EncounterModel>(
                    new(encounter.Category, encounter.Entry)).ToMutable(), native);
                nativeToy.AfterCombatEnd(room).GetAwaiter().GetResult();
                pure.EnterCombatRewards(PredictorCombatKind.Monster, 1, battle + 3);
                if (pure.Working.Relics.Single(r => r.Key.Entry == "TOY_BOX").ToyBoxCombatsSeen != battle + 1 ||
                    pure.Working.Relics.Single(r => r.Id == wax.Id).Melted != nativeWax.IsMelted)
                    throw new InvalidOperationException("ToyBox three-combat wax melt mismatch: " + battle);
                Select(pure, new LeaveRewards());
            }
        }
        finally
        {
            stateProperty.SetValue(manager, saved);
            ascensionProperty.SetValue(manager, savedAscension);
        }
    }

    private static void CombatRelicDeckUpgrades(PredictorRun captured)
    {
        var eligible = captured.Working.Deck.Select(captured.Working.Card)
            .Where(c => c.UpgradeLevel < c.MaxUpgradeLevel).ToList();
        var nativeWar = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche));
        eligible.Sort((a, b) =>
        {
            int category = string.Compare(a.Key.Category, b.Key.Category, StringComparison.Ordinal);
            if (category != 0) return category;
            int key = string.Compare(a.Key.Entry, b.Key.Entry, StringComparison.Ordinal);
            return key != 0 ? key : a.UpgradeLevel.CompareTo(b.UpgradeLevel);
        });
        eligible.UnstableShuffle(nativeWar);
        var war = PredictorCombatRelicUpgrade.WarHammerVictory(captured.Context, captured.Working);
        if (!war.CardIds.SequenceEqual(eligible.Take(4).Select(c => c.Id)))
            throw new InvalidOperationException("WarHammer native elite shuffle selection mismatch");
        Compare(war.State.Streams.Single(s => s.Stream == PredictorStream.Niche), nativeWar,
            "WarHammer elite Niche continuation");

        var candidates = captured.Working.Deck.Where(id =>
            captured.Working.Card(id).UpgradeLevel < captured.Working.Card(id).MaxUpgradeLevel).ToArray();
        var nativeFish = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche));
        long? expected = candidates.Length == 0 ? null : candidates[nativeFish.NextInt(candidates.Length)];
        long fishId = captured.Working.NextInstanceId;
        var fishing = captured.Working with { Relics = captured.Working.Relics.Add(
            new(fishId, new("RELIC", "FISHING_ROD"), 1) { FishingRodCombatsSeen = 2 }) };
        var fish = PredictorCombatRelicUpgrade.FishingRodMonster(captured.Context, fishing, fishId);
        ImmutableArray<long> expectedFish = expected is long selected ? [selected] : [];
        if (!fish.CardIds.SequenceEqual(expectedFish) ||
            fish.State.Relics[^1].FishingRodCombatsSeen != 3)
            throw new InvalidOperationException("FishingRod third Monster upgrade mismatch");
        Compare(fish.State.Streams.Single(s => s.Stream == PredictorStream.Niche), nativeFish,
            "FishingRod third Monster Niche continuation");
    }

    private static void SeaGlassGeneration(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog!;
        var pool = catalog.CharacterPools.First(p => p.Character != captured.Context.Character);
        var definitions = catalog.CardDefinitions.ToDictionary(d => d.Prototype.Key);
        var native = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards));
        var expected = new List<ModelKey>();
        foreach (string rarity in new[] { "Common", "Uncommon", "Rare" })
        {
            var available = pool.Cards.Where(k => definitions[k].Rarity == rarity).ToList();
            for (int n = 0; n < 5; n++)
            {
                var key = native.NextItem(available);
                expected.Add(key);
                available.Remove(key);
            }
        }
        var actual = PredictorObtainSources.SeaGlass(captured.Context, captured.Working, pool.Character);
        if (!actual.Cards.Select(c => c.Key).SequenceEqual(expected))
            throw new InvalidOperationException("SeaGlass native candidate identities mismatch");
        Compare(actual.State.Streams.Single(s => s.Stream == PredictorStream.Rewards), native,
            "SeaGlass Rewards continuation");
    }

    private static void ScrollBoxesGeneration(PredictorRun captured)
    {
        if (captured.Context.Character.Entry == "DEFECT") return; // Claw branch has its own native roll contract.
        var catalog = captured.Context.Catalog!;
        var commons = catalog.CharacterCards.Where(c => c.IsUnlockedInCapturedPool &&
            c.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common).Select(c => c.CardKey).ToList();
        var uncommons = catalog.CharacterCards.Where(c => c.IsUnlockedInCapturedPool &&
            c.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon).Select(c => c.CardKey).ToList();
        var native = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards));
        var used = new HashSet<ModelKey>();
        var expected = new List<ModelKey>();
        for (int bundle = 0; bundle < 2; bundle++)
        {
            var available = commons.Where(k => !used.Contains(k)).ToList();
            for (int n = 0; n < 2; n++)
            {
                var key = native.NextItem(available);
                expected.Add(key); used.Add(key); available.Remove(key);
            }
            var other = uncommons.Where(k => !used.Contains(k)).ToList();
            var uncommon = native.NextItem(other);
            expected.Add(uncommon); used.Add(uncommon);
        }
        var generated = PredictorObtainSources.ScrollBoxes(captured.Context, captured.Working);
        if (!generated.Bundles.SelectMany(b => b).Select(c => c.Key).SequenceEqual(expected))
            throw new InvalidOperationException("ScrollBoxes native bundle identities mismatch");
        Compare(generated.State.Streams.Single(s => s.Stream == PredictorStream.Rewards), native,
            "ScrollBoxes Rewards continuation");
    }

    private static void KaleidoscopePoolOrder(PredictorRun captured)
    {
        var catalog = captured.Context.Catalog!;
        var own = catalog.CharacterCards.First().PoolId;
        var pools = catalog.AllCharacterCards.Where(c => c.IsUnlockedInCapturedPool && c.PoolId != own)
            .Select(c => c.PoolId).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (pools.Length < 3) return; // Ineligible for native Neow offer in this captured unlock state.
        var native = Native(captured.Working.Streams.Single(s => s.Stream == PredictorStream.Niche));
        var expected = new List<string>();
        for (int n = 0; n < 2; n++)
        {
            var order = pools.ToList();
            order.UnstableShuffle(native);
            expected.AddRange(order.Take(3));
        }
        var actual = PredictorObtainSources.Kaleidoscope(captured.Context, captured.Working, default);
        var poolByCard = catalog.AllCharacterCards.ToDictionary(c => c.CardKey, c => c.PoolId);
        if (!actual.Rewards.SelectMany(r => r.Cards).Select(c => poolByCard[c.Key]).SequenceEqual(expected))
            throw new InvalidOperationException("Kaleidoscope other-pool order mismatch");
        Compare(actual.State.Streams.Single(s => s.Stream == PredictorStream.Niche), native,
            "Kaleidoscope Niche continuation");
        var withWing = PredictorSettlementEffects.ObtainRelic(captured.Working, new("RELIC", "WING_CHARM"));
        withWing = PredictorSettlementEffects.ObtainRelic(withWing, new("RELIC", "DRIFTWOOD"));
        var modified = PredictorObtainSources.Kaleidoscope(captured.Context, withWing, default);
        if (modified.Rewards.SelectMany(r => r.Cards).Any(c => c.Enchantment?.Entry != "SWIFT") ||
            modified.Rewards.Any(r => !r.ManuallySetCards || !r.CanReroll || r.RelicSubscriptionActive || r.RerollIsCardReward ||
                r.RerollRecipe is not { NoUpgradeRoll: true } recipe || recipe.CardPoolOverride.IsDefault || !recipe.CardPoolOverride.IsEmpty))
            throw new InvalidOperationException("Kaleidoscope per-single-card hooks/manual reward reroll contract missing");
        var run = new PredictorRun(captured.Context, withWing with { OpeningCompleted = true });
        run.EnterRewardNode(new(1, 2, "kaleidoscope-lifecycle"),
            [new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Event, new("RELIC", "KALEIDOSCOPE"), [])]);
        Select(run, new TakeReward(run.Rewards.Single().Id));
        if (run.Rewards.Length != 2 || run.Rewards.Any(r => r.RelicSubscriptionActive || !r.CanReroll || !r.ManuallySetCards))
            throw new InvalidOperationException("Kaleidoscope child publication restored an absent subscription");
    }

    private static void FrameUnwind(PredictorRun captured)
    {
        var candidates = new[] { new ModelKey("RELIC", "GOLDEN_PEARL"), new ModelKey("RELIC", "OLD_COIN"),
            new ModelKey("RELIC", "NUTRITIOUS_OYSTER") }.ToImmutableArray();
        var context = captured.Context with { OpeningEligibleRelics = candidates };
        var entry = captured.Working with { OpeningCompleted = true };
        var run = new PredictorRun(context, entry);
        run.EnterRewardNode(new(1, 2, "obtain-frame-check"),
            [new(0, PredictorRewardKind.Relic, PredictorRewardOrigin.Event, new("RELIC", "NEOWS_BONES"), [])]);
        long parentId = run.Rewards[0].Id;
        Select(run, new TakeReward(parentId));
        if (run.Rewards.Length != 2 || run.Rewards.Any(r => !r.Mandatory))
            throw new InvalidOperationException("Bones mandatory child offers missing");
        if (run.Submit(run.Request!, new LeaveRewards()) != PredictorInputResult.Invalid)
            throw new InvalidOperationException("Bones permitted child skip");
        var offered = run.Rewards.Select(r => r.Key).ToArray();
        long second = run.Rewards[1].Id, first = run.Rewards[0].Id;
        Select(run, new TakeReward(second));
        if (run.Rewards.Single(r => r.Id == second).Status != PredictorRewardStatus.Taken)
            throw new InvalidOperationException("Bones second child not settled");
        Select(run, new TakeReward(first));
        if (run.Rewards.Single(r => r.Id == parentId).Status != PredictorRewardStatus.Taken ||
            run.CompletedChildRewards.Length != 2 ||
            !run.Working.Relics.TakeLast(2).Select(r => r.Key).SequenceEqual(offered.AsEnumerable().Reverse()))
            throw new InvalidOperationException("Bones ordered child unwind or parent return differs");
        Select(run, new LeaveRewards());
        if (run.Phase != PredictorPhase.Complete || run.Checkpoints[^1].CompletedNode?.Rewards.Length != 3)
            throw new InvalidOperationException("Bones child offers absent from completed node history");
    }

    private static void LeafyLocalHealth(PredictorRun captured)
    {
        var run = new PredictorRun(captured.Context, captured.Working);
        if (run.BeginObtainRelic(new("RELIC", "LEAFY_POULTICE"), null,
            PredictorObtainReturn.Ancient, 1))
            throw new InvalidOperationException("Leafy did not pause for local health premise");
        Select(run, new ProvideEventHealthFact(new(10, 10)));
        if (run.Working.Outcome != PredictorRunOutcome.Death || run.Working.Deck.Length != captured.Working.Deck.Length)
            throw new InvalidOperationException("Leafy low-max lethal branch transformed after death");
        long next = captured.Working.NextInstanceId;
        var rods = captured.Working with { Relics = captured.Working.Relics
            .Add(new(next, new("RELIC", "TUNGSTEN_ROD"), 1))
            .Add(new(next + 1, new("RELIC", "TUNGSTEN_ROD"), 1)), NextInstanceId = next + 2 };
        var protectedRun = new PredictorRun(captured.Context, rods);
        if (protectedRun.BeginObtainRelic(new("RELIC", "LEAFY_POULTICE"), null,
            PredictorObtainReturn.Ancient, 2))
            throw new InvalidOperationException("Protected Leafy did not request local health premise");
        Select(protectedRun, new ProvideEventHealthFact(new(11, 11)));
        if (protectedRun.Working.Outcome == PredictorRunOutcome.Death)
            throw new InvalidOperationException("TungstenRod did not prevent Leafy raw max-HP damage death");
    }

    private static void Select(PredictorRun run, PredictorInput input)
    {
        if (run.Submit(run.Request!, input) != PredictorInputResult.Accepted)
            throw new InvalidOperationException("Obtain input rejected: " + input);
    }

    private static Rng Native(PredictorStreamState state) => new(new SerializableRng
    { counter = state.Calls, state0 = state.S0, state1 = state.S1, state2 = state.S2, state3 = state.S3 });

    private static void Compare(PredictorStreamState state, Rng native, string source)
    {
        var expected = native.ToSerializable();
        if ((state.Calls, state.S0, state.S1, state.S2, state.S3) !=
            (expected.counter, expected.state0, expected.state1, expected.state2, expected.state3))
            throw new InvalidOperationException(source);
    }
}
