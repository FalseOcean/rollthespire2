using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Extensions;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Persistence;

internal static class PredictorEventChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Event runtime: " + message);
    }

    private static PredictorCatalog WithControlledLevels(PredictorCatalog catalog) => catalog with
    {
        // These checks use small synthetic cards, not ModelDb cards. Supply all
        // declared levels explicitly so missing production capture remains fatal.
        CardDefinitions = catalog.CardDefinitions.Select(d => d with
        {
            Levels = Enumerable.Range(0, d.Prototype.MaxUpgradeLevel + 1)
                .Select(level => new PredictorCardLevelMetadata(level, d.CanonicalCost,
                    d.CostsX, d.Unplayable, d.HasLocalExhaust,
                    d.Prototype.HasInnateKeyword, d.Prototype.HasRetainKeyword,
                    d.Prototype.HasEternalKeyword)).ToImmutableArray()
        }).ToImmutableArray()
    };

    internal static void RunNative(PredictorRun captured)
    {
        CanonicalFixedPoolsMatchNative();
        var run = captured;
        if (!run.Working.OpeningCompleted)
        {
            run.EnterOpening();
            var opening = run.Working.OpeningOptions.FirstOrDefault(k => k.Entry is
                "GOLDEN_PEARL" or "ARCANE_SCROLL" or "PHIAL_HOLSTER" or "SMALL_CAPSULE" or "LOST_COFFER");
            Check(opening.IsValid, "Captured Dense scenario needs an implemented Opening offer");
            Check(run.Submit(run.Request!, new ChooseOpeningRelic(opening)) == PredictorInputResult.Accepted,
                "Captured Dense Opening choice");
            if (run.Phase == PredictorPhase.Rewards)
                Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
                    "Captured Dense Opening reward completion");
        }
        Check(run.Phase == PredictorPhase.Complete && run.Working.OpeningCompleted,
            "Captured Dense scenario starts after real Opening");
        int cursor = run.Working.Acts.Single(a => a.Act == 2).NormalCursor;
        int nicheBefore = run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls;
        int checkpointsBefore = run.Checkpoints.Length;
        run.BeginEvent(new(2, 25, "captured:event:dense-vegetation"), new("EVENT", "DENSE_VEGETATION"));
        Check(run.Submit(run.Request!, new ChooseEventOption("REST")) == PredictorInputResult.Accepted &&
            run.EventNeedsHealthFact, "Captured Dense rest requests local HP premise");
        Check(run.Submit(run.Request!, new ProvideEventHealthFact(new(40, 80))) == PredictorInputResult.Accepted &&
            run.EventOptions.Select(o => o.Key).SequenceEqual(["FIGHT"]),
            "Captured Dense mimic Rest returns to mandatory Fight");
        Check(run.Submit(run.Request!, new ChooseEventOption("FIGHT")) == PredictorInputResult.Accepted &&
            run.Phase == PredictorPhase.CombatFacts && run.Working.Acts.Single(a => a.Act == 2).NormalCursor == cursor,
            "Captured Dense child combat retains normal encounter queue");
        int nicheAfterEntry = run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls;
        Check(nicheAfterEntry - nicheBefore == 4, "Captured Dense four initial enemies consume Niche once");
        Check(run.Submit(run.Request!, new ResolveCombat(new PredictorCombatFacts() { Potions = PredictorCombatPotionFacts.NoAdditional })) == PredictorInputResult.Accepted,
            "Captured Dense combat fact settlement");
        if (run.Phase == PredictorPhase.Rewards)
            Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
                "Captured Dense combat reward completion");
        Check(run.Phase == PredictorPhase.Complete && run.Checkpoints.Length == checkpointsBefore + 1 &&
            run.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.Action == "DenseVegetationMimicRestHeal") &&
            run.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.Action == "EnterEventCombat:DENSE_VEGETATION_EVENT_ENCOUNTER") &&
            run.Working.Acts.Single(a => a.Act == 2).NormalCursor == cursor &&
            run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche).Calls == nicheAfterEntry,
            "Captured Dense child combat commits parent only after rewards");
        Console.WriteLine("RUNTIME_EVENT_NATIVE_DENSE_CAPTURED Rest -> Fight -> facts -> rewards -> parent commit PASS (Owner combat facts)");
        int beforeCrystal = run.Checkpoints.Length;
        run.BeginEvent(new(2, 26, "captured:event:crystal-sphere"), new("EVENT", "CRYSTAL_SPHERE"));
        Check(run.Submit(run.Request!, new ChooseEventOption("PAYMENT_PLAN")) == PredictorInputResult.Accepted &&
            run.CrystalView is { Remaining: 6 }, "Captured Crystal Debt then six reveals");
        for (int i = 0; i < 6; i++)
        {
            var hidden = run.CrystalView!.Cells.Where(c => c.Hidden)
                .OrderBy(c => Math.Abs(c.X - 5) + Math.Abs(c.Y - 5)).First();
            Check(run.Submit(run.Request!, new RevealCrystalCell(hidden.X, hidden.Y)) == PredictorInputResult.Accepted,
                "Captured Crystal reveal " + i);
        }
        if (run.Phase == PredictorPhase.Rewards)
            Check(run.Submit(run.Request!, new LeaveRewards()) == PredictorInputResult.Accepted,
                "Captured Crystal reward settlement");
        Check(run.Phase == PredictorPhase.Complete && run.Checkpoints.Length == beforeCrystal + 1 &&
            run.Checkpoints[^1].CompletedNode!.Inputs.Count(i => i.Action == "CrystalReveal") == 6 &&
            run.Working.Deck.Any(id => run.Working.Card(id).Key.Entry == "DEBT"),
            "Captured Crystal six-click board and child reward boundary");
        Console.WriteLine("RUNTIME_EVENT_NATIVE_CRYSTAL_CAPTURED PaymentPlan -> six reveals -> rewards -> parent commit PASS (runtime source chain)");
        // Continue from the captured state with controlled combat premises for
        // two temporary card sources. No temporary card enters master Cards/Deck.
        var tempEntry = run.Working with { Relics = [], PotionSlots = [null, null] };
        var actTwo = tempEntry.Acts.Single(a => a.Act == 2);
        var nextEncounter = actTwo.NormalEncounters[actTwo.NormalCursor % actTwo.NormalEncounters.Length];
        var tempFacts = new PredictorCombatFacts(false)
        {
            CardTriggers = tempEntry.Deck.Select(tempEntry.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                .Select(c => new PredictorCardTrigger(c.Id, 0)).ToImmutableArray(),
            TemporaryCardTriggers = [new(new("CARD", "FEED"), 0, 1), new(new("CARD", "ALCHEMIZE"), 0, 1)],
            CombatCardSourceOrder = [new(TemporarySourceIndex: 0), new(TemporarySourceIndex: 1)],
            Potions = PredictorCombatPotionFacts.NoAdditional,
            GremlinGold = nextEncounter.Entry == "GREMLIN_MERC_NORMAL" ? new(false, false, null) : null
        };
        var temporary = new PredictorRun(run.Context, tempEntry);
        temporary.EnterCombatRewards(PredictorCombatKind.Monster, 2, 27, facts: tempFacts);
        Check(temporary.Phase == PredictorPhase.Rewards &&
            temporary.Working.Deck.SequenceEqual(tempEntry.Deck) &&
            temporary.Working.PotionSlots.Count(p => p != null) == 1,
            "Captured context temporary Alchemize procures a bottle without master card insertion");
        Check(temporary.Submit(temporary.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
            temporary.Checkpoints[^1].CompletedNode!.Inputs.Any(i =>
                i.CombatTemporaryCardTriggers.Length == 2 &&
                i.HealthConsequences.Any(h => h.SourceCardId == 0 && h.TemporarySourceIndex == 0 && h.MaxHpDelta == 3) &&
                i.PotionAttempts.Any(p => p.TemporarySourceIndex == 1)),
            "Temporary Feed/Alchemize facts persist only after combat rewards complete");
        var temporaryReloaded = PredictorRunStore.Deserialize(PredictorRunStore.Serialize(temporary),
            run.Context.CatalogFingerprint, run.Context.UnlockFingerprint);
        Check(temporaryReloaded.Working.PotionSlots.SequenceEqual(temporary.Working.PotionSlots) &&
            temporaryReloaded.Checkpoints[^1].CompletedNode!.Inputs.Any(i =>
                i.CombatCardSourceOrder.SequenceEqual(tempFacts.CombatCardSourceOrder)),
            "Temporary source order and generated potion survive boundary reload");
        temporaryReloaded.Rewind(0);
        Check(temporaryReloaded.Working.Deck.SequenceEqual(tempEntry.Deck) &&
            temporaryReloaded.Working.PotionSlots.All(p => p == null) &&
            temporaryReloaded.Working.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration) ==
                tempEntry.Streams.Single(s => s.Stream == PredictorStream.CombatPotionGeneration),
            "Temporary card source rewind restores inventory and combat potion RNG");
        Console.WriteLine("RUNTIME_TEMP_CARD_CAPTURED Feed/Alchemize facts -> combat rewards -> checkpoint/reload -> rewind PASS (controlled combat premises)");
        // Dummy encounters suppress their own room rewards. On victory the
        // event upgrades first, then Offers its reward plus combat ExtraRewards;
        // on timeout the same accumulated extras are discarded.
        foreach (bool timedOut in new[] { false, true })
        {
            var dummyEntry = tempEntry with { PotionSlots = [null, null] };
            var dummy = new PredictorRun(run.Context, dummyEntry);
            dummy.BeginEvent(new(2, 28, "captured:event:dummy:" + timedOut), new("EVENT", "BATTLEWORN_DUMMY"));
            Check(dummy.Submit(dummy.Request!, new ChooseEventOption("SETTING_2")) == PredictorInputResult.Accepted &&
                dummy.Phase == PredictorPhase.CombatFacts, "Captured Dummy V2 child combat entry");
            var dummyFacts = new PredictorCombatFacts(false)
            {
                CardTriggers = dummyEntry.Deck.Select(dummyEntry.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                    .Select(c => new PredictorCardTrigger(c.Id, 0)).ToImmutableArray(),
                TemporaryCardTriggers = [new(new("CARD", "FORBIDDEN_GRIMOIRE"), 0, 1)],
                CombatCardSourceOrder = [new(TemporarySourceIndex: 0)],
                Potions = PredictorCombatPotionFacts.NoAdditional
            };
            Check(dummy.Submit(dummy.Request!, new ResolveCombat(dummyFacts, timedOut)) ==
                PredictorInputResult.Accepted, "Captured Dummy V2 combat settlement");
            if (timedOut)
                Check(dummy.Phase == PredictorPhase.Complete && dummy.Rewards.IsEmpty &&
                    !dummy.Checkpoints[^1].CompletedNode!.Rewards.Any(r => r.Kind == PredictorRewardKind.RemoveCard),
                    "Dummy timeout must discard Grimoire combat extra reward");
            else
            {
                Check(dummy.Phase == PredictorPhase.Rewards &&
                    dummy.Rewards.Count(r => r.Kind == PredictorRewardKind.RemoveCard) == 1 &&
                    dummy.Checkpoints.Length == 1 &&
                    dummy.Working.Deck.Any(id => dummy.Working.Card(id).UpgradeLevel > dummyEntry.Card(id).UpgradeLevel),
                    "Dummy V2 upgrade must precede deferred Grimoire OfferCustom");
                Check(dummy.Submit(dummy.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
                    dummy.Phase == PredictorPhase.Complete &&
                    dummy.Checkpoints[^1].CompletedNode!.Rewards.Any(r => r.Kind == PredictorRewardKind.RemoveCard),
                    "Dummy custom combat extra reward commits with parent event");
            }
        }
        foreach (var source in new[]
        {
            (Setting: "SETTING_1", Card: "THE_HUNT", Reward: PredictorRewardKind.Card),
            (Setting: "SETTING_3", Card: "ROYALTIES", Reward: PredictorRewardKind.Gold)
        })
        {
            var dummy = new PredictorRun(run.Context, tempEntry);
            dummy.BeginEvent(new(2, 29, "captured:event:dummy:" + source.Setting), new("EVENT", "BATTLEWORN_DUMMY"));
            Check(dummy.Submit(dummy.Request!, new ChooseEventOption(source.Setting)) == PredictorInputResult.Accepted &&
                dummy.Phase == PredictorPhase.CombatFacts, "Captured Dummy " + source.Setting + " entry");
            var facts = new PredictorCombatFacts(false)
            {
                CardTriggers = tempEntry.Deck.Select(tempEntry.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                    .Select(c => new PredictorCardTrigger(c.Id, 0)).ToImmutableArray(),
                TemporaryCardTriggers = [new(new("CARD", source.Card), 0, 1)],
                CombatCardSourceOrder = [new(TemporarySourceIndex: 0)],
                Potions = PredictorCombatPotionFacts.NoAdditional
            };
            Check(dummy.Submit(dummy.Request!, new ResolveCombat(facts, false)) == PredictorInputResult.Accepted &&
                dummy.Phase == PredictorPhase.Rewards && dummy.Rewards.Any(r => r.Kind == source.Reward),
                "Dummy " + source.Setting + " did not append combat-created " + source.Card + " reward");
            Check(dummy.Submit(dummy.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
                dummy.Checkpoints[^1].CompletedNode!.Rewards.Any(r => r.Kind == source.Reward),
                "Dummy " + source.Setting + " extra reward did not commit");
        }
        Console.WriteLine("RUNTIME_DUMMY_EXTRA_CAPTURED V1 Hunt/V2 Grimoire/V3 Royalties appended after source effect; timeout discards extras PASS");
    }

    private static void CanonicalFixedPoolsMatchNative()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        // These native static declarations do not initialize an event or touch RunState.
        var trashType = typeof(MegaCrit.Sts2.Core.Models.Events.TrashHeap);
        var cards = (MegaCrit.Sts2.Core.Models.CardModel[])trashType.GetProperty("Cards", flags)!.GetValue(null)!;
        var relics = (MegaCrit.Sts2.Core.Models.RelicModel[])trashType.GetProperty("Relics", flags)!.GetValue(null)!;
        var fake = (MegaCrit.Sts2.Core.Models.RelicModel[])typeof(MegaCrit.Sts2.Core.Models.Events.FakeMerchant)
            .GetField("_inventoryRelics", flags)!.GetValue(null)!;
        ModelKey Key(MegaCrit.Sts2.Core.Models.AbstractModel model) => new(model.Id.Category, model.Id.Entry);
        Check(cards.Select(Key).SequenceEqual(RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards),
            "TrashHeap Grab canonical pool equals actual native static declaration order");
        Check(relics.Select(Key).SequenceEqual(RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapDiveRelics),
            "TrashHeap Dive canonical pool equals actual native static declaration order");
        Check(fake.Select(Key).SequenceEqual(RolltheSpire2.Core.Events.Beta111EventResultCatalog.FakeMerchantRelics),
            "FakeMerchant canonical pool equals actual native static inventory order, excluding combat reward rug");
        Console.WriteLine("E_FIXED_POOLS_NATIVE_PASS;grab=10;dive=5;fake=9;order=true;runStateUntouched=true");
    }

    internal static int Run()
    {
        var a = new PredictorCard(0, new("CARD", "TEST_A"), EffectCardType.Attack, 0, 1, true, 1);
        var b = new PredictorCard(0, new("CARD", "TEST_B"), EffectCardType.Skill, 0, 1, true, 1);
        var mad = new PredictorCard(0, new("CARD", "MAD_SCIENCE"), EffectCardType.Skill, 0, 1, true, 1);
        var clumsy = new PredictorCard(0, new("CARD", "CLUMSY"), EffectCardType.Curse, 0, 0, false, 1);
        var enlightenment = new PredictorCard(0, new("CARD", "ENLIGHTENMENT"), EffectCardType.Skill, 0, 1, true, 1);
        var badLuck = new PredictorCard(0, new("CARD", "BAD_LUCK"), EffectCardType.Curse, 0, 0, false, 1);
        var regret = new PredictorCard(0, new("CARD", "REGRET"), EffectCardType.Curse, 0, 0, false, 1);
        var guilty = new PredictorCard(0, new("CARD", "GUILTY"), EffectCardType.Curse, 0, 0, false, 1);
        var normality = new PredictorCard(0, new("CARD", "NORMALITY"), EffectCardType.Curse, 0, 0, false, 1);
        var sporeMind = new PredictorCard(0, new("CARD", "SPORE_MIND"), EffectCardType.Curse, 0, 0, false, 1);
        var greed = new PredictorCard(0, new("CARD", "GREED"), EffectCardType.Curse, 0, 0, false, 1);
        var catalog = new PredictorCatalog([], [])
        {
            CardDefinitions =
            [
                new(a, "Common", new("CARD_POOL", "TEST"), false, false, false),
                new(b, "Common", new("CARD_POOL", "TEST"), false, false, false),
                new(mad, "Uncommon", new("CARD_POOL", "TEST"), false, false, false),
                new(clumsy, "Curse", new("CARD_POOL", "TEST"), false, false, false),
                new(enlightenment, "Uncommon", new("CARD_POOL", "TEST"), false, false, false),
                new(badLuck, "Curse", new("CARD_POOL", "TEST"), false, false, false),
                new(regret, "Curse", new("CARD_POOL", "TEST"), false, false, false),
                new(guilty, "Curse", new("CARD_POOL", "TEST"), false, false, false),
                new(normality, "Curse", new("CARD_POOL", "TEST"), false, false, false),
                new(sporeMind, "Curse", new("CARD_POOL", "TEST"), false, false, false),
                new(greed, "Curse", new("CARD_POOL", "TEST"), false, false, false)
            ],
            TransformationPools = [new(new("CARD_POOL", "TEST"), [a.Key, b.Key])]
        };
        catalog = WithControlledLevels(catalog);
        var context = new PredictorContext("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"),
            0, "events-test-catalog", "events-test-unlocks", [], [], [], 1) { Catalog = catalog };
        var seed = PredictorSeedState.Create(context, [a, b], [], 110, 2, new(-.05f, .4f));
        var tailSeed = PredictorSeedState.Create(context, [a, b],
            [new ModelKey("RELIC", "LIZARD_TAIL")], 110, 2, new(-.05f, .4f));
        var tailFairy = new PredictorPotion(tailSeed.NextInstanceId, new("POTION", "FAIRY_IN_A_BOTTLE"));
        tailSeed = tailSeed with { PotionSlots = tailSeed.PotionSlots.SetItem(0, tailFairy),
            NextInstanceId = tailSeed.NextInstanceId + 1 };
        long tailId = tailSeed.Relics.Single().Id;
        var fairyThenTail = ImmutableArray.Create(
            new PredictorCombatPotionChange(PredictorCombatPotionAction.FairyTriggered,
                new(ExistingId: tailFairy.Id)),
            new PredictorCombatPotionChange(PredictorCombatPotionAction.LizardTailTriggered,
                RelicId: tailId));
        PredictorCombatPotions.Validate(new(false, fairyThenTail), []);
        var tailResult = PredictorCombatPotions.Apply(context, tailSeed, fairyThenTail,
            ImmutableArray.CreateBuilder<PredictorPotion>(), ImmutableArray.CreateBuilder<PredictorPotionAttempt>());
        Check(tailResult.PotionSlots[0] == null && tailResult.Relics.Single().LizardTailUsed,
            "Combat Fairy first, then exact LizardTail can trigger on later lethal event");
        bool tailBeforeFairyRejected = false;
        try
        {
            PredictorCombatPotions.Apply(context, tailSeed,
                [new(PredictorCombatPotionAction.LizardTailTriggered, RelicId: tailId)],
                ImmutableArray.CreateBuilder<PredictorPotion>(), ImmutableArray.CreateBuilder<PredictorPotionAttempt>());
        }
        catch (ArgumentException) { tailBeforeFairyRejected = true; }
        Check(tailBeforeFairyRejected, "Combat held Fairy precedes LizardTail late death hook");
        bool usedTailRejected = false;
        try
        {
            PredictorCombatPotions.Apply(context, tailResult,
                [new(PredictorCombatPotionAction.LizardTailTriggered, RelicId: tailId)],
                ImmutableArray.CreateBuilder<PredictorPotion>(), ImmutableArray.CreateBuilder<PredictorPotionAttempt>());
        }
        catch (ArgumentException) { usedTailRejected = true; }
        Check(usedTailRejected, "Combat LizardTail cannot trigger twice");
        var grimoire = new PredictorCard(0, new("CARD", "FORBIDDEN_GRIMOIRE"),
            EffectCardType.Power, 0, 1, true, 1);
        var grimoireContext = context with { Catalog = WithControlledLevels(new PredictorCatalog([], [])
        { CardDefinitions = [new(grimoire, "Ancient", new("CARD_POOL", "TEST"), false, false, false),
            new(a, "Common", new("CARD_POOL", "TEST"), false, false, false)] }) };
        var grimoireSeed = PredictorSeedState.Create(grimoireContext, [grimoire, a], [], 110, 2, new(-.05f, .4f));
        var grimoireConsequences = PredictorCombatConsequences.Apply(grimoireContext, grimoireSeed,
            PredictorCombatKind.Monster, new(false)
            { CardTriggers = [new(grimoireSeed.Deck[0], 2)], Potions = PredictorCombatPotionFacts.NoAdditional });
        Check(grimoireConsequences.Extras.Length == 2 &&
            grimoireConsequences.Extras.All(p => p.Kind == PredictorRewardKind.RemoveCard),
            "ForbiddenGrimoire two plays queue two CardRemovalRewards after combat");
        var grimoireRewards = PredictorRewardGeneration.Generate(grimoireContext,
            grimoireConsequences.State, grimoireConsequences.Extras, PredictorCombatKind.Monster);
        Check(grimoireRewards.Rewards.Length == 2 &&
            grimoireRewards.State.Streams.Single(s => s.Stream == PredictorStream.Rewards) ==
                grimoireSeed.Streams.Single(s => s.Stream == PredictorStream.Rewards),
            "CardRemovalReward Populate consumes no Rewards draw");
        var grimoireRun = new PredictorRun(grimoireContext, grimoireRewards.State);
        grimoireRun.EnterRewardNode(new(1, 3, "combat:grimoire"), grimoireRewards.Rewards);
        long removalReward = grimoireRun.Rewards[0].Id;
        Check(grimoireRun.Submit(grimoireRun.Request!, new TakeReward(removalReward)) == PredictorInputResult.Accepted &&
            grimoireRun.Phase == PredictorPhase.RewardRemoveCards &&
            grimoireRun.Project().DeckSelection!.RequiredCardCount == 1 &&
            grimoireRun.Project().CanCancelTargetSelection,
            "CardRemovalReward opens cancelable single Deck target");
        Check(grimoireRun.Submit(grimoireRun.Request!, new CancelRewardCardRemoval()) == PredictorInputResult.Accepted &&
            grimoireRun.Rewards[0].Status == PredictorRewardStatus.Open &&
            grimoireRun.Submit(grimoireRun.Request!, new TakeReward(removalReward)) == PredictorInputResult.Accepted &&
            grimoireRun.Submit(grimoireRun.Request!, new SelectRemovalCards([grimoireSeed.Deck[1]])) == PredictorInputResult.Accepted &&
            grimoireRun.Rewards[0].Status == PredictorRewardStatus.Taken &&
            !grimoireRun.Working.Deck.Contains(grimoireSeed.Deck[1]) &&
            grimoireRun.Submit(grimoireRun.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
            grimoireRun.Checkpoints[^1].CompletedNode!.Rewards[1].Status == PredictorRewardStatus.Skipped,
            "CardRemovalReward cancel keeps offer; chosen removal commits; next offer may be skipped");
        grimoireRun.Rewind(0);
        Check(grimoireRun.Working.Deck.Contains(grimoireSeed.Deck[1]) && grimoireRun.Phase == PredictorPhase.Complete,
            "CardRemovalReward completed boundary rewinds full Deck");
        var sourceKeys = new[] { "ROYALTIES", "ALCHEMIZE", "FORBIDDEN_GRIMOIRE", "THE_HUNT", "FEED", "HAND_OF_GREED" };
        var temporaryCatalog = WithControlledLevels(new PredictorCatalog([], [])
        { CardDefinitions = sourceKeys.Select(key => new PredictorCardDefinition(
            new PredictorCard(0, new("CARD", key), EffectCardType.Power, 0, 1, true, 1),
            "Rare", new("CARD_POOL", "TEST"), false, false, false)).ToImmutableArray() });
        var temporaryContext = context with { Catalog = temporaryCatalog,
            PotionPool = [new(new("POTION", "TEMP_COMMON"), PredictorPotionRarity.Common),
                new(new("POTION", "TEMP_UNCOMMON"), PredictorPotionRarity.Uncommon),
                new(new("POTION", "TEMP_RARE"), PredictorPotionRarity.Rare)] };
        var masterRoyalty = temporaryCatalog.CardDefinitions.Single(d => d.Prototype.Key.Entry == "ROYALTIES").Prototype;
        var temporarySeed = PredictorSeedState.Create(temporaryContext, [masterRoyalty], [],
            110, 2, new(-.05f, .4f));
        var temporaryFacts = new PredictorCombatFacts(false)
        {
            CardTriggers = [new(temporarySeed.Deck[0], 0)],
            TemporaryCardTriggers =
            [new(new("CARD", "ROYALTIES"), 1, 1), new(new("CARD", "ALCHEMIZE"), 0, 1),
                new(new("CARD", "FORBIDDEN_GRIMOIRE"), 0, 1), new(new("CARD", "THE_HUNT"), 0, 1)],
            CombatCardSourceOrder = [new(TemporarySourceIndex: 1), new(TemporarySourceIndex: 0),
                new(TemporarySourceIndex: 3), new(TemporarySourceIndex: 2)],
            Potions = new(false, [new(PredictorCombatPotionAction.Discard,
                new(GeneratedOrdinal: 0), BeforeTemporarySourceIndex: 2)])
        };
        var temporaryResult = PredictorCombatConsequences.Apply(temporaryContext, temporarySeed,
            PredictorCombatKind.Monster, temporaryFacts);
        Check(temporaryResult.Triggers.Single().EffectiveTriggers == 0 &&
            temporaryResult.Extras.Count(p => p.Kind == PredictorRewardKind.Gold && p.MinGold == 40) == 1 &&
            temporaryResult.Extras.Count(p => p.Kind == PredictorRewardKind.Card) == 1 &&
            temporaryResult.Extras.Count(p => p.Kind == PredictorRewardKind.RemoveCard) == 1 &&
            temporaryResult.Potions.Length == 1 && temporaryResult.PotionAttempts.Single().TemporarySourceIndex == 1 &&
            temporaryResult.State.PotionSlots.All(p => p == null) &&
            temporaryResult.State.Deck.SequenceEqual(temporarySeed.Deck),
            "Manual temporary sources replace default Deck trigger and anchor generated-potion discard before Grimoire");
        var temporaryFeed = PredictorCombatConsequences.Apply(temporaryContext, temporarySeed,
            PredictorCombatKind.Monster, new(false)
            {
                CardTriggers = [new(temporarySeed.Deck[0], 0)],
                TemporaryCardTriggers = [new(new("CARD", "FEED"), 1, 1),
                    new(new("CARD", "HAND_OF_GREED"), 1, 1)],
                CombatCardSourceOrder = [new(TemporarySourceIndex: 0), new(TemporarySourceIndex: 1)]
            });
        Check(temporaryFeed.Health.Single() == new PredictorHealthConsequence(0, 4, 0) &&
            temporaryFeed.State.GoldBudget == 135 && temporaryFeed.State.Deck.SequenceEqual(temporarySeed.Deck),
            "Temporary Feed health and HandOfGreed gold settle without creating master instances");
        bool mixedDefaultRejected = false;
        try { PredictorCombatConsequences.Apply(temporaryContext, temporarySeed, PredictorCombatKind.Monster,
            temporaryFacts with { AutomaticCardEffects = true }); }
        catch (ArgumentException) { mixedDefaultRejected = true; }
        Check(mixedDefaultRejected, "Temporary declaration must replace automatic Deck defaults");
        var run = new PredictorRun(context, seed);
        run.BeginEvent(new(1, 2, "event:1:2"), new("EVENT", "AROMA_OF_CHAOS"));
        Check(run.EventOptions.Select(o => o.Key).SequenceEqual(["LET_GO", "MAINTAIN_CONTROL"]), "initial options");
        Check(run.Submit(run.Request!, new ChooseEventOption("MAINTAIN_CONTROL")) == PredictorInputResult.Accepted, "choose upgrade");
        Check(run.Phase == PredictorPhase.EventCards && run.EventCardCandidates.SequenceEqual(seed.Deck), "card selection projection");
        Check(run.Submit(run.Request!, new SelectEventCards([seed.Deck[0]])) == PredictorInputResult.Accepted, "select upgrade");
        Check(run.Phase == PredictorPhase.Complete && run.Working.Card(seed.Deck[0]).UpgradeLevel == 1, "upgrade and finish");
        Check(run.Checkpoints.Length == 2 && run.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.Action == "EventUpgrade"), "event checkpoint");

        var transformRun = new PredictorRun(context, seed);
        transformRun.BeginEvent(new(1, 2, "event:transform"), new("EVENT", "AROMA_OF_CHAOS"));
        Check(transformRun.Submit(transformRun.Request!, new ChooseEventOption("LET_GO")) == PredictorInputResult.Accepted, "Aroma transform branch");
        Check(transformRun.Submit(transformRun.Request!, new SelectEventCards([seed.Deck[0]])) == PredictorInputResult.Accepted, "Aroma transform selection");
        Check(!transformRun.Working.Deck.Contains(seed.Deck[0]) &&
            transformRun.Working.Card(transformRun.Working.Deck[^1]).Key == b.Key, "event-local transformation result");
        Check(transformRun.Working.Streams.Single(s => s.Stream == PredictorStream.Niche) ==
            seed.Streams.Single(s => s.Stream == PredictorStream.Niche), "event transform does not spend Niche");
        transformRun.Rewind(0);
        Check(transformRun.Working.Deck.SequenceEqual(seed.Deck) && transformRun.Checkpoints.Length == 1, "event rollback restores entry");

        run.BeginEvent(new(1, 3, "event:1:3"), new("EVENT", "MORPHIC_GROVE"));
        Check(run.Submit(run.Request!, new ChooseEventOption("LONER")) == PredictorInputResult.Accepted, "health request");
        Check(run.EventNeedsHealthFact && run.EventOptions.IsEmpty, "health fact projected without stale options");
        Check(run.Submit(run.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted, "health fact accepted");
        Check(run.Phase == PredictorPhase.Complete && run.Checkpoints[^1].CompletedNode!.Inputs
            .SelectMany(i => i.EventHealthConsequences).Any(h => h.HpDelta == 5 && h.MaxHpDelta == 5), "max HP result");

        run.BeginEvent(new(1, 4, "event:1:4"), new("EVENT", "TINKER_TIME"));
        Check(run.Submit(run.Request!, new ChooseEventOption("CHOOSE_CARD_TYPE")) == PredictorInputResult.Accepted, "tinker type prompt");
        Check(run.EventOptions.Length == 2, "two types sampled");
        var nativeTinker = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "TINKER_TIME"));
        var nativeTypes = new List<string> { "ATTACK", "SKILL", "POWER" }.UnstableShuffle(nativeTinker).Take(2);
        Check(run.EventOptions.Select(o => o.Key).SequenceEqual(nativeTypes), "native TinkerTime type sampling");
        string chosenType = run.EventOptions[0].Key;
        Check(run.Submit(run.Request!, new ChooseEventOption(chosenType)) == PredictorInputResult.Accepted, "tinker rider prompt");
        Check(run.EventOptions.Length == 2, "two riders sampled");
        var nativeRiders = (chosenType switch
        {
            "ATTACK" => new List<string> { "SAPPING", "VIOLENCE", "CHOKING" },
            "SKILL" => new List<string> { "ENERGIZED", "WISDOM", "CHAOS" },
            _ => new List<string> { "EXPERTISE", "CURIOUS", "IMPROVEMENT" }
        }).UnstableShuffle(nativeTinker).Take(2);
        Check(run.EventOptions.Select(o => o.Key).SequenceEqual(nativeRiders), "native TinkerTime rider sampling");
        string chosenRider = run.EventOptions[1].Key;
        Check(run.Submit(run.Request!, new ChooseEventOption(chosenRider)) == PredictorInputResult.Accepted, "tinker rider choice");
        var created = run.Working.Card(run.Working.Deck[^1]);
        Check(created.Key.Entry == "MAD_SCIENCE" && created.TinkerTimeRider.ToString().Equals(chosenRider, StringComparison.OrdinalIgnoreCase) &&
            created.TinkerTimeType.ToString().Equals(chosenType, StringComparison.OrdinalIgnoreCase), "durable MadScience configuration");
        run.BeginEvent(new(2, 20, "event:2:20"), new("EVENT", "SYMBIOTE"));
        Check(run.EventOptions[0].Key == "APPROACH" && run.EventOptions[0].Enabled, "Symbiote legal approach");
        Check(run.Submit(run.Request!, new ChooseEventOption("APPROACH")) == PredictorInputResult.Accepted, "Symbiote approach");
        if (run.Phase == PredictorPhase.EventCards)
        {
            Check(run.EventCardCandidates.Contains(seed.Deck[0]) && !run.EventCardCandidates.Contains(seed.Deck[1]), "Corrupted attack candidates");
            Check(run.Submit(run.Request!, new SelectEventCards([seed.Deck[0]])) == PredictorInputResult.Accepted, "Symbiote enchant choice");
        }
        Check(run.Working.Card(seed.Deck[0]).Enchantment == new ModelKey("ENCHANTMENT", "CORRUPTED"), "durable Corrupted enchantment");
        run.BeginEvent(new(2, 21, "event:2:21"), new("EVENT", "WHISPERING_HOLLOW"));
        var native = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "WHISPERING_HOLLOW"));
        Check(run.EventOptions.Single(o => o.Key == "GOLD").GoldCost == 35 + native.NextInt(-9, 10), "native event-local price draw after prior events");
        var priceContext = context with { PotionPool =
            [new(new("POTION", "PRICE_COMMON"), PredictorPotionRarity.Common),
             new(new("POTION", "PRICE_UNCOMMON"), PredictorPotionRarity.Uncommon),
             new(new("POTION", "PRICE_RARE"), PredictorPotionRarity.Rare)] };
        var priceSeed = PredictorSeedState.Create(priceContext, [a, b], [], 110, 2, new(-.05f, .4f));
        var priceCorrection = new PredictorRun(priceContext, priceSeed);
        priceCorrection.BeginEvent(new(1, 2, "event:price"), new("EVENT", "WHISPERING_HOLLOW"));
        Check(priceCorrection.Submit(priceCorrection.Request!, new CorrectGoldBalance(1)) == PredictorInputResult.Accepted,
            "event budget correction");
        Check(priceCorrection.Submit(priceCorrection.Request!, new ChooseEventOption("GOLD")) == PredictorInputResult.Accepted &&
            priceCorrection.Working.GoldBudget == 0 && priceCorrection.Rewards.Length == 2,
            "Whisper declared option still pays clamped budget and offers two potions");
        var ornate = new PredictorRun(context, seed);
        ornate.BeginEvent(new(1, 2, "event:ornate"), new("EVENT", "THIS_OR_THAT"));
        Check(ornate.Submit(ornate.Request!, new ChooseEventOption("ORNATE")) == PredictorInputResult.Accepted, "Ornate branch");
        Check(ornate.Phase == PredictorPhase.Complete && ornate.Working.Relics.Any(r => r.Key.Entry == "CIRCLET") &&
            ornate.Working.Deck.Any(id => ornate.Working.Card(id).Key.Entry == "CLUMSY"), "relic before curse completed");
        var tungstenSeed = PredictorSeedState.Create(context, [a, b],
            [new ModelKey("RELIC", "TUNGSTEN_ROD")], 110, 2, new(-.05f, .4f));
        var tungstenPlain = new PredictorRun(context, tungstenSeed);
        tungstenPlain.BeginEvent(new(1, 2, "event:tungsten:plain"), new("EVENT", "THIS_OR_THAT"));
        Check(tungstenPlain.Submit(tungstenPlain.Request!, new ChooseEventOption("PLAIN")) == PredictorInputResult.Accepted &&
            tungstenPlain.Submit(tungstenPlain.Request!, new ProvideEventHealthFact(new(6, 40))) == PredictorInputResult.Accepted &&
            tungstenPlain.Working.Outcome == null && tungstenPlain.Phase == PredictorPhase.Complete &&
            tungstenPlain.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences)
                .Any(h => h.HpDelta == -5) && tungstenPlain.Working.GoldBudget > 110,
            "Tungsten Rod reduces event damage before death and allows subsequent gold gain");
        var trial = new PredictorRun(context, seed);
        trial.BeginEvent(new(1, 2, "event:trial"), new("EVENT", "TRIAL"));
        Check(trial.Submit(trial.Request!, new ChooseEventOption("REJECT")) == PredictorInputResult.Accepted, "Trial reject");
        Check(trial.Submit(trial.Request!, new ChooseEventOption("DOUBLE_DOWN")) == PredictorInputResult.Accepted &&
            trial.TrialAbandonConfirmationPending && trial.EventOptions.IsEmpty, "Trial abandon confirmation");
        Check(trial.Submit(trial.Request!, new ConfirmTrialAbandon(false)) == PredictorInputResult.Accepted &&
            trial.Phase == PredictorPhase.EventChoice && trial.EventOptions.Any(o => o.Key == "ACCEPT"), "Trial cancel returns to page");
        Check(trial.Submit(trial.Request!, new ChooseEventOption("DOUBLE_DOWN")) == PredictorInputResult.Accepted, "Trial reopen confirmation");
        Check(trial.Submit(trial.Request!, new ConfirmTrialAbandon(true)) == PredictorInputResult.Accepted &&
            trial.Working.Outcome == PredictorRunOutcome.Abandoned, "Trial confirmed abandonment");
        var zen = new PredictorRun(context, seed with { GoldBudget = 300 });
        zen.BeginEvent(new(1, 2, "event:zen"), new("EVENT", "ZEN_WEAVER"));
        Check(zen.EventOptions.Single(o => o.Key == "ARACHNID_ACUPUNCTURE").Enabled, "Zen purchase option");
        Check(zen.Submit(zen.Request!, new ChooseEventOption("ARACHNID_ACUPUNCTURE")) == PredictorInputResult.Accepted, "Zen remove branch");
        if (zen.Phase == PredictorPhase.EventCards)
            Check(zen.Submit(zen.Request!, new SelectEventCards([seed.Deck[0], seed.Deck[1]])) == PredictorInputResult.Accepted, "Zen ordered removals");
        Check(zen.Working.GoldBudget == 50 && !zen.Working.Deck.Contains(seed.Deck[0]) &&
            zen.Checkpoints[^1].CompletedNode!.Inputs.FindIndex(i => i.Action == "ZenWeaverRemove") <
            zen.Checkpoints[^1].CompletedNode!.Inputs.FindIndex(i => i.Action == "EventGoldSpent"), "Zen remove before payment");
        var courier = new PredictorRun(context, seed);
        courier.BeginEvent(new(2, 20, "event:courier"), new("EVENT", "POTION_COURIER"));
        Check(courier.Submit(courier.Request!, new ChooseEventOption("GRAB_POTIONS")) == PredictorInputResult.Accepted, "Courier fixed rewards");
        Check(courier.Rewards.Length == 3 && courier.Rewards.All(r => r.Key == new ModelKey("POTION", "FOUL_POTION")) &&
            courier.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards) == seed.Streams.Single(s => s.Stream == PredictorStream.Rewards),
            "Courier prefilled reward identities and RNG");
        Check(courier.Submit(courier.Request!, new LeaveRewards()) == PredictorInputResult.Accepted && courier.Phase == PredictorPhase.Complete,
            "Courier parent completion after rewards");
        var courierContext = context with { PotionPool = [new(new("POTION", "UNCOMMON_TEST"), PredictorPotionRarity.Uncommon)] };
        var courierSeed = PredictorSeedState.Create(courierContext, [a, b], [], 110, 2, new(-.05f, .4f));
        var ransack = new PredictorRun(courierContext, courierSeed);
        ransack.BeginEvent(new(2, 20, "event:ransack"), new("EVENT", "POTION_COURIER"));
        Check(ransack.Submit(ransack.Request!, new ChooseEventOption("RANSACK")) == PredictorInputResult.Accepted, "Courier ransack");
        Check(ransack.Rewards.Length == 1 && ransack.Rewards[0].Key.Entry == "UNCOMMON_TEST" &&
            ransack.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls ==
                courierSeed.Streams.Single(s => s.Stream == PredictorStream.Rewards).Calls + 1,
            "Courier one Rewards selection draw");
        var reflections = new PredictorRun(context, seed);
        reflections.BeginEvent(new(1, 2, "event:reflection"), new("EVENT", "REFLECTIONS"));
        Check(reflections.Submit(reflections.Request!, new ChooseEventOption("SHATTER")) == PredictorInputResult.Accepted, "Reflections shatter");
        Check(reflections.Working.Deck.Length == seed.Deck.Length * 2 + 1 &&
            reflections.Working.Deck.Take(seed.Deck.Length).SequenceEqual(seed.Deck) &&
            reflections.Working.Card(reflections.Working.Deck[^1]).Key.Entry == "BAD_LUCK", "Reflections original-order cloning and curse");
        var mirrorSeed = seed.WithCard(seed.Card(seed.Deck[0]) with { UpgradeLevel = 1 });
        var mirror = new PredictorRun(context, mirrorSeed);
        mirror.BeginEvent(new(1, 2, "event:mirror"), new("EVENT", "REFLECTIONS"));
        Check(mirror.Submit(mirror.Request!, new ChooseEventOption("TOUCH_A_MIRROR")) == PredictorInputResult.Accepted &&
            mirror.Working.Deck.SequenceEqual(seed.Deck), "Reflections touch no deck insertion");
        var mirrorActions = mirror.Checkpoints[^1].CompletedNode!.Inputs.Select(i => i.Action).ToArray();
        Check(Array.IndexOf(mirrorActions, "ReflectionsDowngrade") < Array.IndexOf(mirrorActions, "ReflectionsUpgrade"),
            "Reflections downgrade then upgrade stage");
        bool merchantCaseFound = false;
        for (ulong ordinal = 0; ordinal < 24 && !merchantCaseFound; ordinal++)
        {
            string visible = RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
            var candidateContext = context with { Seed = visible };
            var candidateSeed = PredictorSeedState.Create(candidateContext, [a, b], [], 110, 2, new(-.05f, .4f));
            var candidateRun = new PredictorRun(candidateContext, candidateSeed);
            candidateRun.BeginEvent(new(1, 2, "event:trialmerchant"), new("EVENT", "TRIAL"));
            Check(candidateRun.Submit(candidateRun.Request!, new ChooseEventOption("ACCEPT")) == PredictorInputResult.Accepted, "Trial case draw");
            if (!candidateRun.EventOptions.Any(o => o.Key == "MERCHANT_GUILTY")) continue;
            merchantCaseFound = true;
            Check(candidateRun.Submit(candidateRun.Request!, new ChooseEventOption("MERCHANT_GUILTY")) == PredictorInputResult.Accepted &&
                candidateRun.Phase == PredictorPhase.Complete && candidateRun.Working.Relics.Count(r => r.Key.Entry == "CIRCLET") == 2 &&
                candidateRun.Working.Deck.Any(id => candidateRun.Working.Card(id).Key.Entry == "REGRET"), "Trial two sequential obtains");
        }
        Check(merchantCaseFound, "Trial merchant source case found");
        var wellspring = new PredictorRun(context, seed);
        wellspring.BeginEvent(new(1, 2, "event:wellspring"), new("EVENT", "WELLSPRING"));
        Check(wellspring.Submit(wellspring.Request!, new ChooseEventOption("BATHE")) == PredictorInputResult.Accepted, "Wellspring bathe");
        if (wellspring.Phase == PredictorPhase.EventCards)
            Check(wellspring.Submit(wellspring.Request!, new SelectEventCards([seed.Deck[0]])) == PredictorInputResult.Accepted, "Wellspring card remove");
        Check(wellspring.Working.Deck.Length == seed.Deck.Length &&
            wellspring.Working.Card(wellspring.Working.Deck[^1]).Key.Entry == "GUILTY", "Wellspring removal then curse");
        var bridge = new PredictorRun(context, seed);
        bridge.BeginEvent(new(1, 8, "event:bridge"), new("EVENT", "SLIPPERY_BRIDGE"));
        long firstBridgeTarget = bridge.EventOptions.Single(o => o.Key == "OVERCOME").CardTargetId!.Value;
        Check(bridge.Submit(bridge.Request!, new ChooseEventOption("HOLD_ON")) == PredictorInputResult.Accepted &&
            bridge.EventNeedsHealthFact, "Bridge health request");
        Check(bridge.Submit(bridge.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted, "Bridge first hold");
        long secondBridgeTarget = bridge.EventOptions.Single(o => o.Key == "OVERCOME").CardTargetId!.Value;
        Check(secondBridgeTarget != firstBridgeTarget && bridge.EventOptions.Single(o => o.Key == "HOLD_ON").HealthDamage == 4,
            "Bridge target and damage progress");
        Check(bridge.Submit(bridge.Request!, new ChooseEventOption("OVERCOME")) == PredictorInputResult.Accepted &&
            !bridge.Working.Deck.Contains(secondBridgeTarget), "Bridge removal of current target");
        var tea = new PredictorRun(context, seed with { GoldBudget = 160 });
        tea.BeginEvent(new(2, 20, "event:tea"), new("EVENT", "TEA_MASTER"));
        Check(tea.EventOptions.Single(o => o.Key == "EMBER_TEA").Enabled, "Tea options from entry budget");
        Check(tea.Submit(tea.Request!, new ChooseEventOption("BONE_TEA")) == PredictorInputResult.Accepted &&
            tea.Phase == PredictorPhase.Complete && tea.Working.GoldBudget == 110 &&
            tea.Working.Relics.Any(r => r.Key.Entry == "BONE_TEA"), "Tea payment then fixed Obtain");
        tea.BeginEvent(new(2, 21, "event:aftertea"), new("EVENT", "AROMA_OF_CHAOS"));
        Check(tea.Working.Relics.Single(r => r.Key.Entry == "BONE_TEA").CombatsLeft == 1,
            "Tea counter persists into next event");
        var futureCards = (from rarity in new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare }
            from type in new[] { EffectCardType.Attack, EffectCardType.Skill, EffectCardType.Power }
            from index in Enumerable.Range(0, 4)
            select new NeowEffectCardSnapshot($"future-{rarity}-{type}-{index}",
                new("CARD", $"FUTURE_{rarity}_{type}_{index}".ToUpperInvariant()), index,
                rarity, type, false, false, false, true, true)).ToImmutableArray();
        var futureDefinitions = futureCards.Select(c => new PredictorCardDefinition(
            new PredictorCard(0, c.CardKey, c.CardType, 0, 1, true, 1), c.Rarity.ToString(),
            new("CARD_POOL", "TEST"), false, false, false)).ToImmutableArray();
        var potionContext = context with { Catalog = WithControlledLevels(catalog with
            { CardDefinitions = catalog.CardDefinitions.AddRange(futureDefinitions),
                CharacterCards = futureCards, AllCharacterCards = futureCards }), PotionPool =
            [new(new("POTION", "P_COMMON"), PredictorPotionRarity.Common),
             new(new("POTION", "P_UNCOMMON"), PredictorPotionRarity.Uncommon),
             new(new("POTION", "P_RARE"), PredictorPotionRarity.Rare),
             new(new("POTION", "P_EVENT"), PredictorPotionRarity.Event)] };
        var potionSeed = PredictorSeedState.Create(potionContext, [a, b], [], 110, 4, new(-.05f, .4f));
        potionSeed = potionSeed with { PotionSlots =
            [new(3, potionContext.PotionPool[0].Key), new(4, potionContext.PotionPool[1].Key),
             new(5, potionContext.PotionPool[2].Key), new(6, potionContext.PotionPool[3].Key)], NextInstanceId = 7 };
        var future = new PredictorRun(potionContext, potionSeed);
        future.BeginEvent(new(2, 20, "event:future"), new("EVENT", "THE_FUTURE_OF_POTIONS"));
        Check(future.EventOptions.Length == 3 && future.EventPotionsLocked, "Future potion options and lock");
        var futureNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(potionContext.Seed), 0, "THE_FUTURE_OF_POTIONS"));
        var expectedTypes = new[]
        {
            new[] { EffectCardType.Attack, EffectCardType.Skill }[futureNative.NextInt(2)],
            new[] { EffectCardType.Attack, EffectCardType.Skill, EffectCardType.Power }[futureNative.NextInt(3)],
            new[] { EffectCardType.Attack, EffectCardType.Skill, EffectCardType.Power }[futureNative.NextInt(3)]
        };
        Check(future.EventOptions.Select(o => o.CardType!.Value).SequenceEqual(expectedTypes) && future.EventOptions[0].PotionId == 3,
            "Future source-bound potion type draws");
        Check(future.Submit(future.Request!, new DiscardPotion(0, 3)) == PredictorInputResult.NotAllowed &&
            future.Working.PotionSlots[0]?.Id == 3, "Future free potion discard locked");
        Check(future.Submit(future.Request!, new ChooseEventOption("POTION:3")) == PredictorInputResult.Accepted &&
            future.Phase == PredictorPhase.Rewards && future.Working.PotionSlots[0] == null && future.Working.PotionSlots[1]?.Id == 4,
            "Future source potion discarded before reward");
        Check(future.Rewards.Length == 1 && future.Rewards[0].Cards.Length == 3 &&
            future.Rewards[0].Cards.All(c => c.Type == expectedTypes[0] && c.UpgradeLevel == 1), "Future filtered upgraded card reward");
        Check(future.Submit(future.Request!, new LeaveRewards()) == PredictorInputResult.Accepted &&
            !future.EventPotionsLocked, "Future potion lock cleanup");
        var stone = new PredictorRun(potionContext, potionSeed);
        stone.BeginEvent(new(2, 20, "event:stone"), new("EVENT", "STONE_OF_ALL_TIME"));
        var stoneNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(potionContext.Seed), 0, "STONE_OF_ALL_TIME"));
        long selectedPotion = 3 + stoneNative.NextInt(4);
        Check(stone.EventOptions.Single(o => o.Key == "LIFT").PotionId == selectedPotion && stone.EventPotionsLocked,
            "Stone native selected potion");
        Check(stone.Submit(stone.Request!, new ChooseEventOption("LIFT")) == PredictorInputResult.Accepted && stone.EventNeedsHealthFact,
            "Stone health fact request");
        Check(stone.Submit(stone.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted &&
            stone.Phase == PredictorPhase.Complete && !stone.Working.PotionSlots.Any(p => p?.Id == selectedPotion) &&
            stone.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences)
                .Any(h => h.HpDelta == 10 && h.MaxHpDelta == 10), "Stone discard without use then MaxHP");
        var stonePush = new PredictorRun(potionContext, potionSeed);
        stonePush.BeginEvent(new(2, 20, "event:stonepush"), new("EVENT", "STONE_OF_ALL_TIME"));
        Check(stonePush.Submit(stonePush.Request!, new ChooseEventOption("PUSH")) == PredictorInputResult.Accepted &&
            stonePush.EventNeedsHealthFact, "Stone push health request");
        Check(stonePush.Submit(stonePush.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted &&
            stonePush.Phase == PredictorPhase.Complete &&
            stonePush.Working.Card(stonePush.Working.Deck[0]).Enchantment == new ModelKey("ENCHANTMENT", "VIGOROUS") &&
            stonePush.Working.Card(stonePush.Working.Deck[0]).EnchantmentAmount == 8 &&
            stonePush.Working.PotionSlots.Count(p => p != null) == 4, "Stone push damage then Vigorous enchantment");
        var unrest = new PredictorRun(context, seed);
        unrest.BeginEvent(new(1, 2, "event:unrest"), new("EVENT", "UNREST_SITE"));
        Check(unrest.Submit(unrest.Request!, new ChooseEventOption("KILL")) == PredictorInputResult.Accepted && unrest.EventNeedsHealthFact,
            "Unrest health fact request");
        Check(unrest.Submit(unrest.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted &&
            unrest.Phase == PredictorPhase.Complete && unrest.Working.Relics.Any(r => r.Key.Entry == "CIRCLET") &&
            unrest.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences)
                .Any(h => h.MaxHpDelta == -8), "Unrest maxHP before front relic Obtain");
        var lethalUnrest = new PredictorRun(context, seed);
        lethalUnrest.BeginEvent(new(1, 2, "event:unrest:lethal"), new("EVENT", "UNREST_SITE"));
        Check(lethalUnrest.Submit(lethalUnrest.Request!, new ChooseEventOption("KILL")) == PredictorInputResult.Accepted &&
            lethalUnrest.Submit(lethalUnrest.Request!, new ProvideEventHealthFact(new(6, 6))) == PredictorInputResult.Accepted &&
            lethalUnrest.Working.Outcome == PredictorRunOutcome.Death &&
            lethalUnrest.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences)
                .Any(h => h.MaxHpDelta == -5 && h.HpDelta == -6) &&
            lethalUnrest.Working.Relics.IsEmpty, "Unrest LoseMaxHp lethal damage precedes relic Obtain");
        var statue = new PredictorRun(context, seed);
        statue.BeginEvent(new(2, 20, "event:statue"), new("EVENT", "SUNKEN_STATUE"));
        var statueNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "SUNKEN_STATUE"));
        int statueGold = 111 + statueNative.NextInt(-10, 11);
        Check(statue.EventOptions.Single(o => o.Key == "DIVE_INTO_WATER").GoldGain == statueGold, "Statue native gold roll");
        Check(statue.Submit(statue.Request!, new ChooseEventOption("DIVE_INTO_WATER")) == PredictorInputResult.Accepted &&
            statue.EventNeedsHealthFact, "Statue health request");
        Check(statue.Submit(statue.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted &&
            statue.Working.GoldBudget == 110 + statueGold, "Statue gold before health result");
        var statueActions = statue.Checkpoints[^1].CompletedNode!.Inputs.Select(i => i.Action).ToArray();
        Check(Array.IndexOf(statueActions, "SunkenStatueGold") < Array.IndexOf(statueActions, "EventHealth"), "Statue source order");
        var holes = new PredictorRun(context, seed);
        holes.BeginEvent(new(1, 2, "event:holes"), new("EVENT", "FIELD_OF_MAN_SIZED_HOLES"));
        Check(holes.Submit(holes.Request!, new ChooseEventOption("RESIST")) == PredictorInputResult.Accepted &&
            holes.Phase == PredictorPhase.Complete && holes.Working.Deck.Length == 1 &&
            holes.Working.Card(holes.Working.Deck[0]).Key.Entry == "NORMALITY", "Holes remove two then curse");
        var party = new PredictorRun(context, seed);
        party.BeginEvent(new(1, 2, "event:tea-party"), new("EVENT", "ROUND_TEA_PARTY"));
        Check(party.Submit(party.Request!, new ChooseEventOption("PICK_FIGHT")) == PredictorInputResult.Accepted &&
            party.EventOptions.Single().Key == "CONTINUE_FIGHT", "Tea party nested page");
        Check(party.Submit(party.Request!, new ChooseEventOption("CONTINUE_FIGHT")) == PredictorInputResult.Accepted &&
            party.EventNeedsHealthFact, "Tea party damage fact");
        Check(party.Submit(party.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted &&
            party.Working.Relics.Any(r => r.Key.Entry == "CIRCLET"), "Tea party damage then front Obtain");
        var baths = new PredictorRun(context, seed);
        baths.BeginEvent(new(1, 2, "event:baths"), new("EVENT", "ABYSSAL_BATHS"));
        Check(baths.Submit(baths.Request!, new ChooseEventOption("IMMERSE")) == PredictorInputResult.Accepted &&
            baths.EventNeedsHealthFact, "Baths health request");
        Check(baths.Submit(baths.Request!, new ProvideEventHealthFact(new(30, 40))) == PredictorInputResult.Accepted &&
            baths.EventOptions.Single(o => o.Key == "LINGER").HealthDamage == 2, "Baths first immerse");
        Check(baths.Submit(baths.Request!, new ChooseEventOption("LINGER")) == PredictorInputResult.Accepted &&
            baths.EventOptions.Single(o => o.Key == "LINGER").HealthDamage == 3, "Baths escalating linger");
        Check(baths.Submit(baths.Request!, new ChooseEventOption("EXIT_BATHS")) == PredictorInputResult.Accepted &&
            baths.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences)
                .Count(h => h.MaxHpDelta == 2) == 2, "Baths two maxHP gains before damage");
        var beacon = new PredictorRun(context, seed);
        beacon.BeginEvent(new(1, 2, "event:beacon"), new("EVENT", "DROWNING_BEACON"));
        Check(beacon.Submit(beacon.Request!, new ChooseEventOption("BOTTLE")) == PredictorInputResult.Accepted &&
            beacon.Rewards.Single().Key.Entry == "GLOWWATER_POTION" &&
            beacon.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards) == seed.Streams.Single(s => s.Stream == PredictorStream.Rewards),
            "Beacon prefilled potion without Rewards draw");
        var lethalBeacon = new PredictorRun(context, seed);
        lethalBeacon.BeginEvent(new(1, 2, "event:beacon:lethal"), new("EVENT", "DROWNING_BEACON"));
        Check(lethalBeacon.EventOptions.Single(o => o.Key == "CLIMB").MaxHpCost == 13 &&
            lethalBeacon.Submit(lethalBeacon.Request!, new ChooseEventOption("CLIMB")) == PredictorInputResult.Accepted &&
            lethalBeacon.Submit(lethalBeacon.Request!, new ProvideEventHealthFact(new(5, 5))) == PredictorInputResult.Accepted &&
            lethalBeacon.Working.Outcome == PredictorRunOutcome.Death && lethalBeacon.Working.Relics.IsEmpty,
            "Beacon low-maxHP loss kills before Fresnel Lens Obtain");
        var tablet = new PredictorRun(context, seed);
        tablet.BeginEvent(new(1, 2, "event:tablet"), new("EVENT", "TABLET_OF_TRUTH"));
        Check(tablet.Submit(tablet.Request!, new ChooseEventOption("DECIPHER")) == PredictorInputResult.Accepted &&
            tablet.EventNeedsHealthFact, "Tablet initial health fact");
        Check(tablet.Submit(tablet.Request!, new ProvideEventHealthFact(new(50, 80))) == PredictorInputResult.Accepted &&
            tablet.EventOptions.Single(o => o.Key == "DECIPHER").MaxHpCost == 6, "Tablet first maxHP price and next option");
        Check(tablet.Submit(tablet.Request!, new ChooseEventOption("DECIPHER")) == PredictorInputResult.Accepted &&
            tablet.EventOptions.Single(o => o.Key == "DECIPHER").MaxHpCost == 12, "Tablet second escalating price");
        Check(tablet.Submit(tablet.Request!, new ChooseEventOption("GIVE_UP")) == PredictorInputResult.Accepted &&
            tablet.Checkpoints[^1].CompletedNode!.Inputs.Count(i => i.Action == "TabletUpgrade") == 2 &&
            tablet.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences).Sum(h => h.MaxHpDelta) == -9,
            "Tablet upgrades and maxHP deltas persisted");
        var fairy = new PredictorPotion(seed.NextInstanceId, new("POTION", "FAIRY_IN_A_BOTTLE"));
        var lethalSeed = seed with { PotionSlots = seed.PotionSlots.SetItem(0, fairy),
            NextInstanceId = seed.NextInstanceId + 1 };
        var lethalTablet = new PredictorRun(context, lethalSeed);
        lethalTablet.BeginEvent(new(1, 2, "event:tablet:lethal"), new("EVENT", "TABLET_OF_TRUTH"));
        Check(lethalTablet.Submit(lethalTablet.Request!, new ChooseEventOption("DECIPHER")) == PredictorInputResult.Accepted &&
            lethalTablet.Submit(lethalTablet.Request!, new ProvideEventHealthFact(new(3, 3))) == PredictorInputResult.Accepted &&
            lethalTablet.Working.PotionSlots[0] == null &&
            lethalTablet.EventOptions.Single(o => o.Key == "DECIPHER").MaxHpCost == 6,
            "Tablet forced Kill consumes Fairy and resumes with reduced maxHP");
        Check(lethalTablet.Submit(lethalTablet.Request!, new ChooseEventOption("GIVE_UP")) == PredictorInputResult.Accepted &&
            lethalTablet.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.Action == "EventKillPrevented" && i.ObjectId == fairy.Id) &&
            lethalTablet.Checkpoints[^1].CompletedNode!.Inputs.Count(i => i.Action == "TabletUpgrade") == 0 &&
            lethalTablet.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences).Sum(h => h.MaxHpDelta) == -2,
            "Tablet explicit Kill prevention skips upgrade and commits maxHP loss");
        var choir = new PredictorRun(context, seed);
        choir.BeginEvent(new(1, 2, "event:choir"), new("EVENT", "LUMINOUS_CHOIR"));
        var choirNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "LUMINOUS_CHOIR"));
        Check(choir.EventOptions.Single(o => o.Key == "OFFER_TRIBUTE").GoldCost == 149 - choirNative.NextInt(0, 50),
            "Choir native tribute price");
        Check(choir.Submit(choir.Request!, new ChooseEventOption("REACH_INTO_THE_FLESH")) == PredictorInputResult.Accepted &&
            choir.Phase == PredictorPhase.Complete && choir.Working.Deck.Length == 1 &&
            choir.Working.Card(choir.Working.Deck[0]).Key.Entry == "SPORE_MIND", "Choir two removals then curse");
        var wisp = new PredictorRun(context, seed);
        wisp.BeginEvent(new(1, 2, "event:wisp"), new("EVENT", "LOST_WISP"));
        var wispNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "LOST_WISP"));
        int wispGold = 60 + wispNative.NextInt(-15, 16);
        Check(wisp.EventOptions.Single(o => o.Key == "SEARCH").GoldGain == wispGold &&
            wisp.Submit(wisp.Request!, new ChooseEventOption("SEARCH")) == PredictorInputResult.Accepted &&
            wisp.Working.GoldBudget == 110 + wispGold, "Wisp native gold and gain");
        var strike = new PredictorCard(0, new("CARD", "BASIC_STRIKE"), EffectCardType.Attack, 0, 1, true, 1);
        var defend = new PredictorCard(0, new("CARD", "BASIC_DEFEND"), EffectCardType.Skill, 0, 1, true, 1);
        var ultimateStrike = new PredictorCard(0, new("CARD", "ULTIMATE_STRIKE"), EffectCardType.Attack, 0, 1, true, 1);
        var ultimateDefend = new PredictorCard(0, new("CARD", "ULTIMATE_DEFEND"), EffectCardType.Skill, 0, 1, true, 1);
        var amalgamatorCatalog = new PredictorCatalog([], []) { CardDefinitions =
        [
            new(strike, "Basic", new("CARD_POOL", "TEST"), false, true, false),
            new(defend, "Basic", new("CARD_POOL", "TEST"), false, false, true),
            new(ultimateStrike, "Rare", new("CARD_POOL", "TEST"), false, false, false),
            new(ultimateDefend, "Rare", new("CARD_POOL", "TEST"), false, false, false)
        ] };
        amalgamatorCatalog = WithControlledLevels(amalgamatorCatalog);
        var amalgamatorContext = context with { Catalog = amalgamatorCatalog };
        var amalgamatorSeed = PredictorSeedState.Create(amalgamatorContext, [strike, strike, defend, defend], [], 110, 2, new(-.05f, .4f));
        var amalgamator = new PredictorRun(amalgamatorContext, amalgamatorSeed);
        amalgamator.BeginEvent(new(1, 2, "event:amalgamator"), new("EVENT", "AMALGAMATOR"));
        Check(amalgamator.Submit(amalgamator.Request!, new ChooseEventOption("COMBINE_STRIKES")) == PredictorInputResult.Accepted &&
            amalgamator.Phase == PredictorPhase.Complete && amalgamator.Working.Deck.Length == 3 &&
            amalgamator.Working.Card(amalgamator.Working.Deck[^1]).Key == ultimateStrike.Key,
            "Amalgamator two basic strikes replaced by UltimateStrike");
        var flower = new PredictorRun(context, seed);
        flower.BeginEvent(new(1, 2, "event:flower"), new("EVENT", "COLOSSAL_FLOWER"));
        Check(flower.Submit(flower.Request!, new ChooseEventOption("REACH_DEEPER")) == PredictorInputResult.Accepted &&
            flower.EventNeedsHealthFact, "Flower health fact request");
        Check(flower.Submit(flower.Request!, new ProvideEventHealthFact(new(40, 50))) == PredictorInputResult.Accepted &&
            flower.EventOptions.Single(o => o.Key == "REACH_DEEPER").HealthDamage == 6, "Flower first depth");
        Check(flower.Submit(flower.Request!, new ChooseEventOption("REACH_DEEPER")) == PredictorInputResult.Accepted &&
            flower.EventOptions.Single(o => o.Key == "EXTRACT_INSTEAD").GoldGain == 135, "Flower second depth");
        Check(flower.Submit(flower.Request!, new ChooseEventOption("EXTRACT_INSTEAD")) == PredictorInputResult.Accepted &&
            flower.Working.GoldBudget == 245 &&
            flower.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences).Sum(h => h.HpDelta) == -11,
            "Flower damage and prize order");
        var exterminate = new PredictorCard(0, new("CARD", "EXTERMINATE"), EffectCardType.Attack, 0, 1, true, 1);
        var squash = new PredictorCard(0, new("CARD", "SQUASH"), EffectCardType.Attack, 0, 1, true, 1);
        var bugContext = context with { Catalog = WithControlledLevels(new PredictorCatalog([], []) { CardDefinitions =
            [new(exterminate, "Uncommon", new("CARD_POOL", "TEST"), false, false, false),
             new(squash, "Uncommon", new("CARD_POOL", "TEST"), false, false, false)] }) };
        var bugSeed = PredictorSeedState.Create(bugContext, [a], [], 110, 2, new(-.05f, .4f));
        var bugslayer = new PredictorRun(bugContext, bugSeed);
        bugslayer.BeginEvent(new(1, 2, "event:bugslayer"), new("EVENT", "BUGSLAYER"));
        Check(bugslayer.Submit(bugslayer.Request!, new ChooseEventOption("EXTERMINATION")) == PredictorInputResult.Accepted &&
            bugslayer.Working.Card(bugslayer.Working.Deck[^1]).Key == exterminate.Key, "Bugslayer direct card add");
        var treasury = new PredictorRun(context, seed);
        treasury.BeginEvent(new(1, 2, "event:treasury"), new("EVENT", "SUNKEN_TREASURY"));
        var treasuryNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "SUNKEN_TREASURY"));
        int small = 60 + treasuryNative.NextInt(16) - 8;
        int large = 333 + treasuryNative.NextInt(61) - 30;
        Check(treasury.EventOptions.Single(o => o.Key == "FIRST_CHEST").GoldGain == small &&
            treasury.EventOptions.Single(o => o.Key == "SECOND_CHEST").GoldGain == large,
            "Treasury two native entry draws");
        Check(treasury.Submit(treasury.Request!, new ChooseEventOption("SECOND_CHEST")) == PredictorInputResult.Accepted &&
            treasury.Working.GoldBudget == 110 + large &&
            treasury.Working.Card(treasury.Working.Deck[^1]).Key.Entry == "GREED", "Treasury gold then curse");
        var peck = new PredictorCard(0, new("CARD", "PECK"), EffectCardType.Attack, 0, 1, true, 1);
        var toric = new PredictorCard(0, new("CARD", "TORIC_TOUGHNESS"), EffectCardType.Skill, 0, 1, true, 1);
        var woodCatalog = new PredictorCatalog([], []) { CardDefinitions =
            [new(strike, "Basic", new("CARD_POOL", "TEST"), false, true, false),
             new(defend, "Basic", new("CARD_POOL", "TEST"), false, false, true),
             new(peck, "Uncommon", new("CARD_POOL", "TEST"), false, false, false),
             new(toric, "Uncommon", new("CARD_POOL", "TEST"), false, false, false)] };
        woodCatalog = WithControlledLevels(woodCatalog);
        var woodContext = context with { Catalog = woodCatalog };
        var woodSeed = PredictorSeedState.Create(woodContext, [strike, defend], [], 110, 2, new(-.05f, .4f));
        var wood = new PredictorRun(woodContext, woodSeed);
        wood.BeginEvent(new(1, 2, "event:wood"), new("EVENT", "WOOD_CARVINGS"));
        Check(wood.EventOptions.Single(o => o.Key == "SNAKE").Enabled &&
            wood.Submit(wood.Request!, new ChooseEventOption("BIRD")) == PredictorInputResult.Accepted &&
            wood.EventCardCandidates.SequenceEqual(woodSeed.Deck), "Wood basic transform candidates");
        Check(wood.Submit(wood.Request!, new SelectEventCards([woodSeed.Deck[0]])) == PredictorInputResult.Accepted &&
            wood.Working.Card(wood.Working.Deck[^1]).Key == peck.Key &&
            wood.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.Transformations).Single().OriginalId == woodSeed.Deck[0],
            "Wood fixed Bird transformation");
        var woodSnake = new PredictorRun(woodContext, woodSeed);
        woodSnake.BeginEvent(new(1, 2, "event:wood-snake"), new("EVENT", "WOOD_CARVINGS"));
        Check(woodSnake.Submit(woodSnake.Request!, new ChooseEventOption("SNAKE")) == PredictorInputResult.Accepted &&
            woodSnake.Submit(woodSnake.Request!, new SelectEventCards([woodSeed.Deck[1]])) == PredictorInputResult.Accepted &&
            woodSnake.Working.Card(woodSeed.Deck[1]).Enchantment == new ModelKey("ENCHANTMENT", "SLITHER"),
            "Wood Snake enchantment persists");
        var ranwidRelic = new ModelKey("RELIC", "WHETSTONE");
        var ranwidCatalog = catalog with { TradableRelicTypes = [ranwidRelic] };
        var ranwidContext = context with { Catalog = ranwidCatalog };
        var ranwidSeed = PredictorSeedState.Create(ranwidContext, [a, b], [ranwidRelic], 110, 2, new(-.05f, .4f));
        var ranwidPotion = new PredictorPotion(ranwidSeed.NextInstanceId, new("POTION", "BLOOD_POTION"));
        ranwidSeed = ranwidSeed with { NextInstanceId = ranwidSeed.NextInstanceId + 1,
            PotionSlots = ranwidSeed.PotionSlots.SetItem(0, ranwidPotion) };
        var ranwid = new PredictorRun(ranwidContext, ranwidSeed);
        ranwid.BeginEvent(new(2, 2, "event:ranwid"), new("EVENT", "RANWID_THE_ELDER"));
        Check(ranwid.EventOptions.Single(o => o.Key == "POTION").PotionId == ranwidPotion.Id &&
            ranwid.EventOptions.Single(o => o.Key == "RELIC").RelicId == ranwidSeed.Relics[0].Id &&
            ranwid.EventPotionsLocked, "Ranwid entry offers and potion lock");
        Check(ranwid.Submit(ranwid.Request!, new ChooseEventOption("GOLD")) == PredictorInputResult.Accepted &&
            ranwid.Phase == PredictorPhase.Complete && ranwid.Working.GoldBudget == 10 &&
            ranwid.Working.Relics.Length == 2, "Ranwid gold before front relic Obtain");
        var traderSeed = PredictorSeedState.Create(ranwidContext, [a, b],
            Enumerable.Repeat(ranwidRelic, 5), 110, 2, new(-.05f, .4f));
        var trader = new PredictorRun(ranwidContext, traderSeed);
        trader.BeginEvent(new(2, 2, "event:trader"), new("EVENT", "RELIC_TRADER"));
        Check(trader.EventOptions.Select(o => o.Key).SequenceEqual(["TOP", "MIDDLE", "BOTTOM"]) &&
            trader.EventOptions.All(o => o.OfferedRelic == new ModelKey("RELIC", "CIRCLET")) &&
            trader.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards) !=
                traderSeed.Streams.Single(s => s.Stream == PredictorStream.Rewards),
            "RelicTrader three offers pre-pulled at entry");
        long tradedId = trader.EventOptions[1].RelicId!.Value;
        Check(trader.Submit(trader.Request!, new ChooseEventOption("MIDDLE")) == PredictorInputResult.Accepted &&
            trader.Phase == PredictorPhase.Complete && !trader.Working.Relics.Any(r => r.Id == tradedId) &&
            trader.Working.Relics.Any(r => r.Key.Entry == "CIRCLET") &&
            trader.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.RemovedRelic?.Id == tradedId),
            "RelicTrader selected instance removal and pre-offered Obtain");
        var dense = new PredictorRun(context, seed);
        dense.BeginEvent(new(2, 2, "event:dense"), new("EVENT", "DENSE_VEGETATION"));
        var denseNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "DENSE_VEGETATION"));
        int denseGold = denseNative.NextInt(61, 100);
        Check(dense.EventOptions.Single(o => o.Key == "TRUDGE_ON").GoldGain == denseGold &&
            dense.Submit(dense.Request!, new ChooseEventOption("TRUDGE_ON")) == PredictorInputResult.Accepted &&
            dense.EventNeedsHealthFact, "Dense native gold and local health request");
        Check(dense.Submit(dense.Request!, new ProvideEventHealthFact(new(30, 50))) == PredictorInputResult.Accepted &&
            dense.Phase == PredictorPhase.Complete && dense.Working.GoldBudget == 110 + denseGold &&
            dense.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences).Sum(h => h.HpDelta) == -8,
            "Dense trudge damage then gold");
        var denseRest = new PredictorRun(context, seed);
        denseRest.BeginEvent(new(2, 2, "event:dense-rest"), new("EVENT", "DENSE_VEGETATION"));
        Check(denseRest.Submit(denseRest.Request!, new ChooseEventOption("REST")) == PredictorInputResult.Accepted &&
            denseRest.EventNeedsHealthFact &&
            denseRest.Submit(denseRest.Request!, new ProvideEventHealthFact(new(25, 50))) == PredictorInputResult.Accepted &&
            denseRest.EventOptions.Select(o => o.Key).SequenceEqual(["FIGHT"]) &&
            denseRest.Working.GoldBudget == 110, "Dense mimic heal resumes mandatory Fight option");
        var selfHelp = new PredictorRun(context, seed);
        selfHelp.BeginEvent(new(1, 2, "event:self-help"), new("EVENT", "SELF_HELP_BOOK"));
        Check(selfHelp.EventOptions.Single(o => o.Key == "READ_THE_BACK").Enabled &&
            !selfHelp.EventOptions.Single(o => o.Key == "READ_ENTIRE_BOOK").Enabled &&
            selfHelp.Submit(selfHelp.Request!, new ChooseEventOption("READ_THE_BACK")) == PredictorInputResult.Accepted &&
            selfHelp.Working.Card(seed.Deck[0]).Enchantment == new ModelKey("ENCHANTMENT", "SHARP") &&
            selfHelp.Working.Card(seed.Deck[0]).EnchantmentAmount == 2,
            "SelfHelpBook eligible selection and Sharp amount");
        var whirlpool = new PredictorRun(woodContext, woodSeed);
        whirlpool.BeginEvent(new(1, 2, "event:whirlpool"), new("EVENT", "SPIRALING_WHIRLPOOL"));
        Check(whirlpool.Submit(whirlpool.Request!, new ChooseEventOption("OBSERVE")) == PredictorInputResult.Accepted &&
            whirlpool.EventCardCandidates.SequenceEqual(woodSeed.Deck), "Whirlpool Basic strike/defend selection");
        Check(whirlpool.Submit(whirlpool.Request!, new SelectEventCards([woodSeed.Deck[1]])) == PredictorInputResult.Accepted &&
            whirlpool.Working.Card(woodSeed.Deck[1]).Enchantment == new ModelKey("ENCHANTMENT", "SPIRAL"),
            "Whirlpool Spiral enchantment persists");
        var doors = new PredictorRun(context, seed);
        doors.BeginEvent(new(1, 2, "event:doors"), new("EVENT", "DOORS_OF_LIGHT_AND_DARK"));
        Check(doors.Submit(doors.Request!, new ChooseEventOption("DARK")) == PredictorInputResult.Accepted &&
            doors.EventCardCandidates.SequenceEqual(seed.Deck) &&
            doors.Submit(doors.Request!, new SelectEventCards([seed.Deck[0]])) == PredictorInputResult.Accepted &&
            doors.Working.Deck.Length == 1 && !doors.Working.Deck.Contains(seed.Deck[0]),
            "Doors Dark exact removal");
        var fakeKeys = new[] { "FAKE_ANCHOR", "FAKE_BLOOD_VIAL", "FAKE_HAPPY_FLOWER", "FAKE_LEES_WAFFLE",
            "FAKE_MANGO", "FAKE_ORICHALCUM", "FAKE_SNECKO_EYE", "FAKE_STRIKE_DUMMY", "FAKE_VENERABLE_TEA_SET" }
            .Select(entry => new ModelKey("RELIC", entry)).ToImmutableArray();
        var fakeContext = context with { Catalog = catalog with
            { ShopRelics = fakeKeys.Select(k => new PredictorShopRelicOption(k, "Event", 50, false)).ToImmutableArray() } };
        var fakeSeed = PredictorSeedState.Create(fakeContext, [a, b], [], 110, 2, new(-.05f, .4f));
        var fake = new PredictorRun(fakeContext, fakeSeed);
        fake.BeginEvent(new(2, 2, "event:fake"), new("EVENT", "FAKE_MERCHANT"));
        Check(fake.FakeMerchantStock.Length == 6 && fake.FakeMerchantStock.Select(s => s.Key).Distinct().Count() == 6 &&
            fake.EventOptions.Select(o => o.Key).SequenceEqual(["OPEN_INVENTORY", "THROW_FOUL_POTION", "LEAVE"]),
            "FakeMerchant six unique event-RNG stock and closed UI");
        Check(fake.Submit(fake.Request!, new ChooseEventOption("OPEN_INVENTORY")) == PredictorInputResult.Accepted &&
            fake.EventOptions.Count(o => o.Key.StartsWith("BUY_", StringComparison.Ordinal)) == 6 &&
            fake.Submit(fake.Request!, new ChooseEventOption("CLOSE_INVENTORY")) == PredictorInputResult.Accepted &&
            fake.Submit(fake.Request!, new ChooseEventOption("LEAVE")) == PredictorInputResult.Accepted &&
            fake.Phase == PredictorPhase.Complete, "FakeMerchant inventory open/close/leave transaction");
        var cheese = new PredictorRun(potionContext, potionSeed);
        cheese.BeginEvent(new(1, 2, "event:cheese"), new("EVENT", "ROOM_FULL_OF_CHEESE"));
        Check(cheese.Submit(cheese.Request!, new ChooseEventOption("GORGE")) == PredictorInputResult.Accepted &&
            cheese.Phase == PredictorPhase.EventCards && cheese.EventCardsRequired == 2 &&
            cheese.EventOfferedCards.Length == 8 && cheese.EventOfferedCards.All(c => !cheese.Working.Deck.Contains(c.Id)),
            "Cheese eight generated cards projected before two choices");
        var pickedCheese = cheese.EventOfferedCards.Take(2).Select(c => c.Id).ToImmutableArray();
        Check(cheese.Submit(cheese.Request!, new SelectEventCards(pickedCheese)) == PredictorInputResult.Accepted &&
            cheese.Phase == PredictorPhase.Complete && cheese.Working.Deck.Length == potionSeed.Deck.Length + 2 &&
            pickedCheese.All(id => cheese.Working.Deck.Contains(id)), "Cheese grid two picks settle into Deck");
        var scriptorium = new PredictorRun(context, seed);
        scriptorium.BeginEvent(new(1, 2, "event:scriptorium"), new("EVENT", "WATERLOGGED_SCRIPTORIUM"));
        Check(scriptorium.EventOptions.Single(o => o.Key == "PRICKLY_SPONGE").Enabled &&
            scriptorium.Submit(scriptorium.Request!, new ChooseEventOption("PRICKLY_SPONGE")) == PredictorInputResult.Accepted &&
            scriptorium.Phase == PredictorPhase.Complete && scriptorium.Working.GoldBudget == 11 &&
            seed.Deck.All(id => scriptorium.Working.Card(id).Enchantment == new ModelKey("ENCHANTMENT", "STEADY")),
            "Scriptorium Sponge pays before both Steady enchants");
        var leech = new PredictorRun(potionContext, potionSeed);
        leech.BeginEvent(new(1, 2, "event:leech"), new("EVENT", "BRAIN_LEECH"));
        Check(leech.Submit(leech.Request!, new ChooseEventOption("SHARE_KNOWLEDGE")) == PredictorInputResult.Accepted &&
            leech.EventOfferedCards.Length == 5 && leech.EventCardsRequired == 1,
            "BrainLeech five generated cards before choice");
        long leechCard = leech.EventOfferedCards[2].Id;
        Check(leech.Submit(leech.Request!, new SelectEventCards([leechCard])) == PredictorInputResult.Accepted &&
            leech.Phase == PredictorPhase.Complete && leech.Working.Deck.Contains(leechCard),
            "BrainLeech one grid pick enters Deck");
        var doll = new PredictorRun(context, seed);
        doll.BeginEvent(new(2, 2, "event:doll"), new("EVENT", "DOLL_ROOM"));
        Check(doll.Submit(doll.Request!, new ChooseEventOption("TAKE_SOME_TIME")) == PredictorInputResult.Accepted &&
            doll.EventNeedsHealthFact, "Doll local health fact request");
        Check(doll.Submit(doll.Request!, new ProvideEventHealthFact(new(40, 50))) == PredictorInputResult.Accepted &&
            doll.EventOptions.Length == 2 && doll.EventOptions.All(o => o.Key.StartsWith("DOLL_", StringComparison.Ordinal)) &&
            doll.Checkpoints.Length == 1, "Doll two offered relics after damage, no early commit");
        var grafter = new PredictorRun(context, seed);
        grafter.BeginEvent(new(1, 2, "event:grafter"), new("EVENT", "SPIRIT_GRAFTER"));
        Check(grafter.Submit(grafter.Request!, new ChooseEventOption("REJECTION")) == PredictorInputResult.Accepted &&
            grafter.EventNeedsHealthFact &&
            grafter.Submit(grafter.Request!, new ProvideEventHealthFact(new(20, 40))) == PredictorInputResult.Accepted &&
            grafter.Phase == PredictorPhase.EventCards, "SpiritGrafter local HP before upgrade choice");
        Check(grafter.Submit(grafter.Request!, new SelectEventCards([seed.Deck[0]])) == PredictorInputResult.Accepted &&
            grafter.Working.Card(seed.Deck[0]).UpgradeLevel == 1 &&
            grafter.Checkpoints[^1].CompletedNode!.Inputs.SelectMany(i => i.EventHealthConsequences).Sum(h => h.HpDelta) == -10,
            "SpiritGrafter upgrade then damage");
        var exhausted = new PredictorCard(0, new("CARD", "EXHAUST_TEST"), EffectCardType.Skill, 0, 1, true, 1)
        { HasExhaustKeyword = true };
        var graveCatalog = new PredictorCatalog([], []) { CardDefinitions =
        [new PredictorCardDefinition(exhausted, "Common", new("CARD_POOL", "TEST"), false, false, false)
        { HasLocalExhaust = true, Levels =
          [new(0, 1, false, false, true, false, false, false),
           new(1, 0, false, false, false, false, false, false)] }] };
        var graveContext = context with { Catalog = graveCatalog };
        var graveBase = new PredictorRun(graveContext,
            PredictorSeedState.Create(graveContext, [exhausted], [], 110, 2, new(-.05f, .4f)));
        graveBase.BeginEvent(new(2, 2, "event:grave:base"), new("EVENT", "GRAVE_OF_THE_FORGOTTEN"));
        Check(graveBase.EventOptions.Single(o => o.Key == "CONFRONT").Enabled,
            "Grave local Exhaust candidate at base level");
        var graveUpgraded = new PredictorRun(graveContext,
            PredictorSeedState.Create(graveContext, [exhausted with { UpgradeLevel = 1 }], [], 110, 2, new(-.05f, .4f)));
        graveUpgraded.BeginEvent(new(2, 2, "event:grave:upgraded"), new("EVENT", "GRAVE_OF_THE_FORGOTTEN"));
        Check(!graveUpgraded.EventOptions.Single(o => o.Key == "CONFRONT").Enabled &&
            PredictorCardLevelEffects.Resolve(graveContext, graveUpgraded.Working.Card(graveUpgraded.Working.Deck[0])).LocalCost == 0,
            "Grave upgraded Exhaust removal and local cost use captured level");
        var conveyor = new PredictorRun(context, seed with { GoldBudget = 160 });
        conveyor.BeginEvent(new(2, 2, "event:conveyor"), new("EVENT", "ENDLESS_CONVEYOR"));
        Check(conveyor.EventNeedsHealthFact && conveyor.EventOptions.IsEmpty,
            "Conveyor entry requires health before eligibility-dependent first roll");
        var conveyorNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "ENDLESS_CONVEYOR"));
        float conveyorRoll = conveyorNative.NextFloat() * 24f;
        string conveyorDish = conveyorRoll < 6 ? "CAVIAR" : conveyorRoll < 9 ? "SPICY_SNAPPY" :
            conveyorRoll < 12 ? "JELLY_LIVER" : conveyorRoll < 15 ? "FRIED_EEL" :
            conveyorRoll < 18 ? "SUSPICIOUS_CONDIMENT" : "CLAM_ROLL";
        Check(conveyor.Submit(conveyor.Request!, new ProvideEventHealthFact(new(40, 50))) == PredictorInputResult.Accepted &&
            conveyor.EventOptions[0].Key == "GRAB_" + conveyorDish &&
            conveyor.Submit(conveyor.Request!, new ChooseEventOption("OBSERVE_CHEF")) == PredictorInputResult.Accepted &&
            conveyor.Phase == PredictorPhase.Complete,
            "Conveyor native weighted first dish and ObserveChef terminal choice");
        var jungle = new PredictorRun(context, seed);
        jungle.BeginEvent(new(1, 2, "event:jungle"), new("EVENT", "JUNGLE_MAZE_ADVENTURE"));
        var jungleNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "JUNGLE_MAZE_ADVENTURE"));
        int soloJungleGold = (int)(150m + (decimal)jungleNative.NextFloat(-15f, 15f));
        int joinedJungleGold = (int)(50m + (decimal)jungleNative.NextFloat(-15f, 15f));
        Check(jungle.EventOptions.Single(o => o.Key == "SOLO_QUEST").GoldGain == soloJungleGold &&
            jungle.EventOptions.Single(o => o.Key == "JOIN_FORCES").GoldGain == joinedJungleGold &&
            jungle.Submit(jungle.Request!, new ChooseEventOption("JOIN_FORCES")) == PredictorInputResult.Accepted &&
            jungle.Working.GoldBudget == 110 + joinedJungleGold,
            "Jungle native float gold draws and join settlement");
        var wongoContext = context with { AccountWongoPoints = 1995 };
        var wongoSeed = PredictorSeedState.Create(wongoContext, [a with { UpgradeLevel = 1 }, b], [],
            320, 2, new(-.05f, .4f));
        var wongo = new PredictorRun(wongoContext, wongoSeed);
        wongo.BeginEvent(new(2, 2, "event:wongo"), new("EVENT", "WELCOME_TO_WONGOS"));
        Check(wongo.EventOptions.Single(o => o.Key == "FEATURED_ITEM").OfferedRelic == new ModelKey("RELIC", "CIRCLET") &&
            wongo.EventOptions.Single(o => o.Key == "MYSTERY_BOX").Enabled &&
            wongo.Submit(wongo.Request!, new ChooseEventOption("LEAVE")) == PredictorInputResult.Accepted &&
            wongo.Working.Card(wongoSeed.Deck[0]).UpgradeLevel == 0 && wongo.Working.WongoPoints == 0,
            "Wongo featured pre-pull and Leave downgrade, no points awarded");
        var wongoPurchase = new PredictorRun(wongoContext, wongoSeed);
        wongoPurchase.BeginEvent(new(2, 2, "event:wongo:purchase"), new("EVENT", "WELCOME_TO_WONGOS"));
        Check(wongoPurchase.Submit(wongoPurchase.Request!, new ChooseEventOption("BARGAIN_BIN")) == PredictorInputResult.Accepted &&
            wongoPurchase.Phase == PredictorPhase.Complete && wongoPurchase.Working.GoldBudget == 220 &&
            wongoPurchase.Working.WongoPoints == 32 &&
            wongoPurchase.Working.Relics.Select(r => r.Key.Entry)
                .SequenceEqual(["CIRCLET", "WONGO_CUSTOMER_APPRECIATION_BADGE"]) &&
            wongoPurchase.Checkpoints[^1].CompletedNode!.Inputs.Any(i => i.Action == "WongoPointsEarned"),
            "Wongo purchase obtains common relic then threshold badge before parent commit");
        var crystalCatalog = WithControlledLevels(catalog with { CardDefinitions = catalog.CardDefinitions.Add(
            new(new PredictorCard(0, new("CARD", "DEBT"), EffectCardType.Curse, 0, 0, false, 1),
                "Curse", new("CARD_POOL", "TEST"), false, false, false)) });
        var crystalContext = context with { Catalog = crystalCatalog };
        var crystalSeed = PredictorSeedState.Create(crystalContext, [a, b], [], 110, 2, new(-.05f, .4f));
        var crystal = new PredictorRun(crystalContext, crystalSeed);
        crystal.BeginEvent(new(2, 2, "event:crystal"), new("EVENT", "CRYSTAL_SPHERE"));
        var crystalNative = new Rng(Beta111Profile.Instance.DeriveEventStreamSeed(
            Beta111Profile.Instance.ComputeRootSeed(context.Seed), 0, "CRYSTAL_SPHERE"));
        int crystalPrice = 50 + crystalNative.NextInt(1, 50);
        Check(crystal.EventOptions.Single(o => o.Key == "UNCOVER_FUTURE").GoldCost == crystalPrice &&
            crystal.Submit(crystal.Request!, new ChooseEventOption("UNCOVER_FUTURE")) == PredictorInputResult.Accepted &&
            crystal.CrystalView is { Remaining: 3, Tool: PredictorCrystalTool.Big } view &&
            view.Cells.Length == 121 && view.Cells.Count(c => !c.Hidden) > 4 &&
            crystal.Working.GoldBudget == 110 - crystalPrice,
            "Crystal native price, payment, eleven-square board and default Big tool");
        Console.WriteLine("Predictor event transaction checks PASS: ordered Fairy/LizardTail inventory, Grimoire removal cancel/rewind, temporary card source ordering, Aroma rollback, Morphic health, TinkerTime, Symbiote, local RNG, ThisOrThat Obtain/Tungsten survival, Trial modal/obtains, Zen ordering, Courier rewards, Reflections, Wellspring, SlipperyBridge, TeaMaster, FuturePotion trade, Stone, Unrest low-maxHP death, SunkenStatue, Holes, TeaParty, AbyssalBaths, Beacon low-maxHP death, Tablet forced-Kill prevention, Choir, Wisp, Amalgamator, Flower, Bugslayer, Treasury, Wood, Ranwid, RelicTrader, Dense trudge/rest handoff, SelfHelpBook, Whirlpool, Doors, FakeMerchant stock UI, Cheese grid, Scriptorium, BrainLeech grid, Doll selection, Grafter, Grave per-level Exhaust, Conveyor first roll, Jungle gold, Wongo pre-pull/purchase/Badge, Crystal board entry.");
        return 0;
    }
}
