using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;

internal static class PredictorOperationSceneChecks
{
    private static ModelKey Relic(string entry) => new("RELIC", entry);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Send(PredictorRun run, PredictorInput input) =>
        Check(run.Submit(run.Request!, input) == PredictorInputResult.Accepted, "Operation scene input: " + input);

    internal static void Run(PredictorRun baseline)
    {
        PopulatedRewardAndSacrifice(baseline);
        PaidAstrolabeContinuation(baseline);
        NonMerchantShopPriceAndRefill(baseline);
        RemainingRestAndOpenedTreasure(baseline);
        PotionIdentityQueries(baseline);
        ObtainedChildContinuations(baseline);
        PredictorOperationCrystalChecks.Run(baseline);
        PredictorOperationTreasurePartyChecks.Run(baseline);
        PredictorOperationLinkedRewardChecks.Run(baseline);
        PendingRemovalChoices(baseline);
        ActiveRestRewards(baseline);
        SurfaceProjections(baseline);
        CurrentAndNextActIdentities(baseline);
        Console.WriteLine("OPERATION_SCENES populated identities; sacrifice Whetstone native shuffle/RNG; paid Astrolabe child/restock; remaining rest/opened chest PASS");
    }

    private readonly record struct SortCard(PredictorCard Card) : IComparable<SortCard>
    {
        public int CompareTo(SortCard other)
        {
            int key = new ModelId(Card.Key.Category, Card.Key.Entry).CompareTo(new ModelId(other.Card.Key.Category, other.Card.Key.Entry));
            return key != 0 ? key : Card.UpgradeLevel.CompareTo(other.Card.UpgradeLevel);
        }
    }

    private static void SurfaceProjections(PredictorRun baseline)
    {
        var context = baseline.Context with { Operation = new(1, 0, 1, []) };
        var original = baseline.Working;
        long first = original.NextInstanceId;
        var card = original.Card(original.Deck[0]);
        var state = original with { GoldBudget = 10000,
            Relics = [new(first, Relic("THE_COURIER"), 1)], NextInstanceId = first + 2 };
        var entry = new PredictorShopEntry(first + 1, PredictorShopEntryKind.CharacterCard, card.Key, card, 1, false, true, 0)
        { OperationRefill = new(context.Catalog!.CharacterCards, card.Type, null) };
        var snapshot = new PredictorOperationSnapshot("surface:courier", context, state,
            new PredictorOperationShopScene([entry], true), 50, 80);
        var expected = PredictorOperation.Preview(snapshot, new BuyShopEntry(entry.Id, 0));
        var refill = expected.ShopOffers.Single(o => o.EntryId == entry.Id);
        var preview = SurfaceOperationPreview.BuildShop(snapshot, entry.Id);
        var row = preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows).Single();
        Check(row.Cards.Single() == refill.Card && row.Price == refill.Cost && row.Label == null &&
            row.Action == OperationPreviewAction.Restock && preview.Source == Relic("THE_COURIER") &&
            preview.Style?.Text == OperationTextMode.None,
            "Courier hover must show exact replacement attributes and price in the purchased slot, using D1");
        Check(!row.Cards.Any(c => c.Key == card.Key) && snapshot.State == state,
            "Courier hover repeated the public purchased card or changed captured state");

        var astrolabe = entry with { Kind = PredictorShopEntryKind.Relic, Key = Relic("ASTROLABE"), Card = null, OperationRefill = null };
        var pending = SurfaceOperationPreview.BuildShop(snapshot with { Scene = new PredictorOperationShopScene([astrolabe], true) }, entry.Id);
        Check(pending.PendingReasons.Any(p => p.StartsWith("CourierRestockAwaitingPlayerChoice:", StringComparison.Ordinal)) &&
            !pending.Branches.SelectMany(b => b.Sections).Any(s => s.Source == Relic("THE_COURIER")),
            "Paid pending relic must not mislabel the purchased item as Courier refill");

        foreach (bool hasCourier in new[] { false, true })
        {
            var orreryEntry = astrolabe with { Key = Relic("ORRERY") };
            var orrerySnapshot = snapshot with { State = state with { Relics = hasCourier ? state.Relics : [] },
                Scene = new PredictorOperationShopScene([orreryEntry], true) };
            var orrery = SurfaceOperationPreview.BuildShop(orrerySnapshot, entry.Id);
            var rewardSections = orrery.Branches.SelectMany(b => b.Sections).Where(s => s.Source == Relic("ORRERY")).ToArray();
            Check(orrery.Faults.IsEmpty && rewardSections.SelectMany(s => s.CardGroups).Count() == 5 &&
                rewardSections.SelectMany(s => s.CardGroups).All(g => g.Cards.Length == 3) &&
                rewardSections.All(s => OperationDisplayPolicy.Resolve(orrery, s) is
                    { Text: OperationTextMode.GroupRows, Cards: OperationCardLayout.Groups }),
                "Shop Orrery must retain five A3 reward groups with or without Courier");
            var courierSections = orrery.Branches.SelectMany(b => b.Sections).Where(s => s.Source == Relic("THE_COURIER")).ToArray();
            Check(courierSections.Length == (hasCourier ? 1 : 0) && orrery.Source == Relic("ORRERY") &&
                !orrery.PendingReasons.Any(p => p.StartsWith("CourierRestockAwaitingPlayerChoice:", StringComparison.Ordinal)),
                "Orrery hover retains its originating effect and only Courier adds the ordinary-completion replacement");
            if (hasCourier)
            {
                var completed = PredictorOperation.Preview(orrerySnapshot, [new BuyShopEntry(entry.Id, 0), new LeaveRewards()]);
                var replacement = completed.ShopOffers.Single(offer => offer.EntryId == entry.Id);
                var restock = courierSections.Single().Rows.Single();
                Check(completed.Disposition == PredictorInputResult.Accepted && completed.Waiting.Phase == PredictorPhase.Shop &&
                    replacement.Stocked && restock.Action == OperationPreviewAction.Restock &&
                    restock.Models.SequenceEqual([replacement.Key!.Value]) && restock.Cards.IsEmpty &&
                    restock.Price == replacement.Cost && restock.Label == null && courierSections.Single().Label == null &&
                    orrerySnapshot.State == state,
                    "Orrery advance restock must match actual detached completion identity/price without extra alternatives or captured-state mutation");
            }
        }
        Check(pending.Branches.SelectMany(b => b.Sections).Where(s => s.Source == Relic("ASTROLABE"))
            .All(s => OperationDisplayPolicy.Resolve(pending, s) is
                { Text: OperationTextMode.None, Cards: OperationCardLayout.Groups }),
            "Shop Astrolabe must retain its source D3 layout");

        var pricedEntry = astrolabe with { Key = Relic("ORRERY"), BaseCost = 100 };
        var priced = snapshot with
        {
            State = state with { Relics = [new(first, Relic("THE_COURIER"), 1),
                new(first + 2, Relic("MEMBERSHIP_CARD"), 1)], NextInstanceId = first + 3 },
            Scene = new PredictorOperationShopScene([pricedEntry], true)
        };
        int quoted = PredictorOperation.Preview(priced, ImmutableArray<PredictorInput>.Empty)
            .ShopOffers.Single().Cost;
        Check(quoted == 40, "operation merchant quote includes Courier and Membership Card discounts once");
        foreach (int gold in new[] { 0, quoted - 1, quoted })
        {
            var current = priced with { State = priced.State with { GoldBudget = gold } };
            var purchase = SurfaceOperationPreview.BuildShop(current, pricedEntry.Id);
            Check(purchase.Notices.Contains("Purchase:InsufficientGold") == (gold < quoted),
                "shop insufficient-gold notice uses the final quote, including exact-price affordability");
            Check(current.State.GoldBudget == gold && (gold >= quoted || purchase.Branches.IsEmpty),
                "unaffordable shop hover preserves state and does not invent purchase results");
        }
        var unstocked = priced with { State = priced.State with { GoldBudget = 0 },
            Scene = new PredictorOperationShopScene([pricedEntry with { Stocked = false }], true) };
        Check(!SurfaceOperationPreview.BuildShop(unstocked, pricedEntry.Id).Notices.Contains("Purchase:InsufficientGold"),
            "an unavailable stock entry is not relabelled as an insufficient-gold purchase");

        var restState = original with { Relics = [new(first, Relic("DREAM_CATCHER"), 1),
            new(first + 1, Relic("TINY_MAILBOX"), 1)], NextInstanceId = first + 2 };
        var rest = new PredictorOperationSnapshot("surface:heal", context, restState,
            new PredictorOperationRestScene([PredictorRestAction.Heal]), 50, 80);
        var healed = PredictorOperation.Preview(rest, new SelectRestAction(PredictorRestAction.Heal));
        var healingPreview = SurfaceOperationPreview.BuildRest(rest, PredictorRestAction.Heal);
        var sections = healingPreview.Branches.Single().Sections;
        Check(sections.Single(s => s.Source.Entry == "DREAM_CATCHER").CardGroups.Single().Cards
                .SequenceEqual(healed.Rewards.Single(r => r.Kind == PredictorRewardKind.Card).Cards) &&
            sections.Single(s => s.Source.Entry == "DREAM_CATCHER").Style?.Text == OperationTextMode.None &&
            sections.Where(s => s.Source.Entry == "TINY_MAILBOX").SelectMany(s => s.Rows).SelectMany(r => r.Models)
                .SequenceEqual(healed.Rewards.Where(r => r.Kind == PredictorRewardKind.Potion).Select(r => r.Key)) &&
            sections.Single(s => s.Source.Entry == "TINY_MAILBOX").Rows.Length == 1 &&
            sections.Single(s => s.Source.Entry == "TINY_MAILBOX").Style?.Text == OperationTextMode.GroupRows &&
            healingPreview.Source == Relic("TINY_MAILBOX"),
            "Heal hover lost actual ordered cards/potions or independent D1/A1 display styles");
        var dreamOnly = SurfaceOperationPreview.BuildRest(rest with { State = restState with { Relics = [restState.Relics[0]] } },
            PredictorRestAction.Heal);
        Check(dreamOnly.Style?.Text == OperationTextMode.None && dreamOnly.Branches.Single().Sections.Length == 1,
            "Dream Catcher alone must retain its D1 art without a text summary");

        long rewardId = first + 1, shownId = first + 2;
        var shown = card with { Id = shownId };
        var sacrificeState = original with { Relics = [new(first, Relic("PAELS_WING"), 1)],
            NextInstanceId = shownId + 1, PersonalBag = PredictorRelicBag.Empty(false) };
        var reward = new PredictorReward(rewardId, PredictorRewardKind.Card, PredictorRewardOrigin.Combat,
            default, [shown], CardSelectionEntered: true) { CanReroll = true, RerollRecipe = new(PredictorRewardKind.Card) };
        var rewardSnapshot = new PredictorOperationSnapshot("surface:alternative", context, sacrificeState,
            new PredictorOperationRewardsScene([reward], rewardId), 50, 80);
        var firstSacrifice = SurfaceOperationPreview.BuildRewardAlternative(rewardSnapshot, rewardId, "SACRIFICE", first);
        Check(firstSacrifice.Branches.IsEmpty && firstSacrifice.ContainedRelics.IsEmpty,
            "Odd sacrifice hover invented a generated relic");
        var evenSnapshot = rewardSnapshot with { State = sacrificeState with
            { Relics = [sacrificeState.Relics[0] with { RewardsSacrificed = 1 }] } };
        var secondSacrifice = SurfaceOperationPreview.BuildRewardAlternative(evenSnapshot, rewardId, "SACRIFICE", first);
        Check(secondSacrifice.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows).Any(r => r.Models.Contains(Relic("CIRCLET"))),
            "Even sacrifice hover lost the actual generated relic");
        // Reroll and Wing simultaneously exceed the native two-alternative
        // limit; use the actual single-feature premise for the reroll fixture.
        var rerollSnapshot = rewardSnapshot with { State = sacrificeState with { Relics = [] } };
        var rerolled = PredictorOperation.Preview(rerollSnapshot, new RerollCardReward(rewardId));
        var rerollPreview = SurfaceOperationPreview.BuildRewardAlternative(rerollSnapshot, rewardId, "REROLL", null);
        Check(rerollPreview.Branches.Single().Sections.Single().CardGroups.Single().Cards
            .SequenceEqual(rerolled.Rewards.Single(r => r.Id == rewardId).Cards) && rerollPreview.Style?.Text == OperationTextMode.None,
            "Reroll hover must use new exact candidate attributes and D1");
        Check(baseline.Working == original && rewardSnapshot.State == sacrificeState,
            "Surface projection mutated original snapshots");
        Console.WriteLine("OPERATION_SURFACES Courier replacement/pending payment; heal mixed styles; sacrifice parity/reroll identities PASS");
    }

    private static void PopulatedRewardAndSacrifice(PredictorRun baseline)
    {
        var original = baseline.Working;
        long wingId = original.NextInstanceId;
        long rewardId = wingId + 1;
        var shownCard = original.Card(original.Deck[0]) with { Id = wingId + 2 };
        var state = original with
        {
            Relics = [new(wingId, Relic("PAELS_WING"), 1) { RewardsSacrificed = 1 }],
            NextInstanceId = wingId + 3,
            PersonalBag = new(false, [new(Relic("WHETSTONE"), "Common")],
                [new("Common", [Relic("WHETSTONE")])], [])
        };
        // A controlled common-rarity draw isolates the obtain chain, while the
        // target shuffle below uses the native RNG and native stable ordering.
        var rewardRng = state.Streams.Single(s => s.Stream == PredictorStream.Rewards).Restore();
        for (int i = 0; rewardRng.Clone().NextFloat() >= .5f; i++)
        {
            Check(i < 64, "Controlled common reward cursor unavailable");
            rewardRng.NextFloat();
        }
        state = state.WithRng(PredictorStream.Rewards, rewardRng);
        var niche = state.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var nativeNiche = Native(niche);
        var expected = state.Deck.Select(state.Card).Where(c => c.Type == EffectCardType.Attack && c.UpgradeLevel < c.MaxUpgradeLevel)
            .Select(c => new SortCard(c)).ToList();
        expected.StableShuffle(nativeNiche);
        var reward = new PredictorReward(rewardId, PredictorRewardKind.Card, PredictorRewardOrigin.Combat,
            default, [shownCard], CardSelectionEntered: true)
        { CanReroll = false, RerollRecipe = new(PredictorRewardKind.Card) };
        var run = new PredictorRun(baseline.Context, state);
        new PredictorOperationRewardsScene([reward], rewardId).Restore(run);
        Check(run.Rewards[0] == reward && run.Rewards[0].Cards[0].Id == shownCard.Id &&
            run.Working == state && run.Selection.CardRewardId == rewardId,
            "Import regenerated populated reward or consumed state");
        Send(run, new SacrificeCardReward(rewardId, wingId));
        Check(run.Working.Relics.Single(r => r.Id == wingId).RewardsSacrificed == 2 &&
            run.Working.Relics.Count(r => r.Key == Relic("WHETSTONE")) == 1 &&
            run.Rewards[0].Status == PredictorRewardStatus.Taken && run.Working.Deck.SequenceEqual(state.Deck),
            "Imported sacrifice did not complete its exact source/obtain chain");
        var upgraded = run.Working.Deck.Where(id => run.Working.Card(id).UpgradeLevel != state.Card(id).UpgradeLevel).Order();
        Check(upgraded.SequenceEqual(expected.Take(2).Select(c => c.Card.Id).Order()), "Sacrifice Whetstone master targets differ from native shuffle");
        var predictedNiche = run.Working.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var actual = nativeNiche.ToSerializable();
        Check(predictedNiche.S0 == actual.state0 && predictedNiche.S1 == actual.state1 &&
            predictedNiche.S2 == actual.state2 && predictedNiche.S3 == actual.state3 &&
            predictedNiche.Calls == actual.counter, "Sacrifice Whetstone RNG continuation differs");
        Check(original.Relics == baseline.Working.Relics && original.Streams == baseline.Working.Streams,
            "Operation changed its captured source");
    }

    private static Rng Native(PredictorStreamState state) => new(new SerializableRng
    { state0 = state.S0, state1 = state.S1, state2 = state.S2, state3 = state.S3, counter = state.Calls });

    private static void PaidAstrolabeContinuation(PredictorRun baseline)
    {
        long courierId = baseline.Working.NextInstanceId;
        long entryId = courierId + 1;
        var state = baseline.Working with
        {
            GoldBudget = 500, Relics = [new(courierId, Relic("THE_COURIER"), 1)], NextInstanceId = entryId + 1,
            OpeningCompleted = true
        };
        var entry = new PredictorShopEntry(entryId, PredictorShopEntryKind.Relic, Relic("ASTROLABE"), null, 100, false, true, 0);
        var direct = new PredictorRun(baseline.Context, state);
        new PredictorOperationShopScene([entry], true).Restore(direct);
        Send(direct, new BuyShopEntry(entryId, 0));
        Check(direct.Phase == PredictorPhase.ObtainDeckCards && direct.Working.GoldBudget == 420 &&
            direct.ShopEntries[0] == entry, "Direct shop did not pause after payment and before restock");
        var paidState = direct.Working;
        var choice = new PredictorOperationDeckObtain(paidState.Relics.Single(r => r.Key == Relic("ASTROLABE")).Id,
            Relic("ASTROLABE"), PredictorObtainDeckOperation.Astrolabe, 3, direct.Selection.CandidateCardIds);
        var imported = new PredictorRun(baseline.Context, paidState);
        new PredictorOperationShopScene([entry], true, PendingRelic: new(entryId, 500, false, choice)).Restore(imported);
        Check(imported.Working == paidState && imported.Selection.CandidateCardIds.SequenceEqual(direct.Selection.CandidateCardIds),
            "Paid child import charged, acquired or generated again");
        var targets = new ChooseObtainDeckCards(choice.CandidateIds.Take(3).ToImmutableArray());
        Send(direct, targets);
        Send(imported, targets);
        Check(imported.Phase == PredictorPhase.Shop && imported.Working.GoldBudget == 420 &&
            imported.Working.Relics.Count(r => r.Key == Relic("ASTROLABE")) == 1 && imported.ShopInventoryRevision == 1,
            "Paid child completion duplicated purchase or failed restock");
        Check(imported.Working.Cards.SequenceEqual(direct.Working.Cards) &&
            imported.Working.Deck.SequenceEqual(direct.Working.Deck) &&
            imported.Working.Streams.SequenceEqual(direct.Working.Streams) && imported.ShopEntries.SequenceEqual(direct.ShopEntries),
            "Restored paid purchase differs from uninterrupted completion");
    }

    private static void RemainingRestAndOpenedTreasure(PredictorRun baseline)
    {
        var rest = new PredictorRun(baseline.Context, baseline.Working);
        new PredictorOperationRestScene([PredictorRestAction.Smith]).Restore(rest);
        Check(rest.RestOptions.SequenceEqual([PredictorRestAction.Smith]) && rest.Working == baseline.Working,
            "Rest import recreated a spent action or room-entry effect");
        var treasure = new PredictorRun(baseline.Context, baseline.Working);
        new PredictorOperationTreasureScene(null, true, true).Restore(treasure);
        Check(treasure.Phase == PredictorPhase.TreasureRelic && treasure.Working == baseline.Working,
            "Opened chest import regenerated relic/gold");
        Check(treasure.Submit(treasure.Request!, new OpenTreasure()) == PredictorInputResult.Invalid,
            "Opened chest accepted another opening");
    }

    private static void NonMerchantShopPriceAndRefill(PredictorRun baseline)
    {
        long firstId = baseline.Working.NextInstanceId;
        long entryId = firstId + 2;
        var state = baseline.Working with { GoldBudget = 500,
            Relics = [new(firstId, Relic("THE_COURIER"), 1), new(firstId + 1, Relic("MEMBERSHIP_CARD"), 1)],
            NextInstanceId = entryId + 1 };
        var run = new PredictorRun(baseline.Context with { Operation = new(1, 0, 1, []) }, state);
        new PredictorOperationShopScene([new(entryId, PredictorShopEntryKind.Relic, Relic("FAKE_ANCHOR"), null, 100, false, true, 0)], true)
            { IsMerchantRoom = false }.Restore(run);
        Check(run.ShopOffers.Single().Cost == 100, "Non-merchant inventory applied MerchantRoom discount hooks");
        Send(run, new BuyShopEntry(entryId, 0));
        Check(run.Working.GoldBudget == 400 && !run.ShopEntries.Single().Stocked &&
            run.Working.Streams.Single(s => s.Stream == PredictorStream.Rewards) == state.Streams.Single(s => s.Stream == PredictorStream.Rewards),
            "Non-merchant purchase applied Courier refill or wrong payment");
    }

    private static void PendingRemovalChoices(PredictorRun baseline)
    {
        long entryId = baseline.Working.NextInstanceId;
        var state = baseline.Working with { GoldBudget = 40, Relics = [], NextInstanceId = entryId + 1 };
        long target = state.Deck.First(id => state.Card(id).Removable);
        var context = baseline.Context with { Operation = new(1, 0, 1, []) };
        var shop = new PredictorOperationShopScene([new(entryId, PredictorShopEntryKind.CardRemoval, null, null, 75, false, true, 0)], true)
        { PendingRemoval = new(entryId, 75, [target], false) };
        var snapshot = new PredictorOperationSnapshot("removal:quoted", context, state, shop, 60, 80);
        var unchanged = PredictorOperation.Preview(snapshot, new CancelShopCardRemoval());
        Check(unchanged.Disposition == PredictorInputResult.Invalid && unchanged.State == state &&
            unchanged.Waiting.Selection.CandidateCardIds.SequenceEqual([target]) && !unchanged.Waiting.Cancelable,
            "pending merchant removal lost actual cancellation/candidate constraints");
        var removed = PredictorOperation.Preview(snapshot, new ChooseShopRemovalCard(target));
        Check(removed.Disposition == PredictorInputResult.Accepted && removed.State.GoldBudget == 0 &&
            !removed.State.Deck.Contains(target) && removed.State.CardShopRemovalsUsed == state.CardShopRemovalsUsed + 1,
            "quoted current removal rechecked affordability or paid/removed incorrectly");
        var reward = new PredictorReward(entryId, PredictorRewardKind.RemoveCard, PredictorRewardOrigin.Event, default, []);
        var rewards = new PredictorOperationRewardsScene([reward]) { PendingRemoval = new(entryId, [target], 1, 1, false) };
        var rewardSnapshot = snapshot with { Scene = rewards };
        var complete = PredictorOperation.Preview(rewardSnapshot, new SelectRemovalCards([target]));
        Check(complete.Disposition == PredictorInputResult.Accepted && complete.Rewards.Single().Status == PredictorRewardStatus.Taken &&
            complete.State.GoldBudget == 40 && !complete.State.Deck.Contains(target), "current removal reward lost source completion or charged gold");
    }

    private static void ActiveRestRewards(PredictorRun baseline)
    {
        long tentId = baseline.Working.NextInstanceId, rewardId = tentId + 1;
        var state = baseline.Working with { Relics = [new(tentId, Relic("MINIATURE_TENT"), 1)], NextInstanceId = rewardId + 1 };
        var reward = new PredictorReward(rewardId, PredictorRewardKind.Gold, PredictorRewardOrigin.Event, default, [], GoldAmount: 7);
        var parent = new PredictorOperationRestScene([PredictorRestAction.Heal, PredictorRestAction.Smith]);
        var snapshot = new PredictorOperationSnapshot("rest:already-healed", baseline.Context with { Operation = new(1, 0, 1, []) }, state,
            new PredictorOperationRestRewardsScene(parent, new([reward])), 70, 80);
        var restored = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty);
        Check(restored.State == state && restored.CurrentHp == 70 && restored.Rewards.Single() == reward,
            "active rest reward import replayed healing or replaced generated reward");
        var completed = PredictorOperation.Preview(snapshot, new TakeReward(rewardId));
        Check(completed.Disposition == PredictorInputResult.Accepted && completed.Waiting.Phase == PredictorPhase.RestSite &&
            completed.CurrentHp == 70 && completed.State.GoldBudget == state.GoldBudget + 7 &&
            completed.Waiting.RestOffers.Select(o => o.Action).SequenceEqual([PredictorRestAction.Smith]) &&
            completed.CompletedChildRewards.Single().Id == rewardId && completed.CompletedChildRewards.Single().Status == PredictorRewardStatus.Taken,
            "active rest rewards failed to resume remaining options without healing again");
    }

    private static void CurrentAndNextActIdentities(PredictorRun baseline)
    {
        var actualActs = baseline.Working.Acts;
        Check(actualActs.Length >= 3, "OP10 fixture requires the baseline's actual generated acts");
        var bosses = actualActs.SelectMany(a => a.Bosses).Distinct().Take(2).ToImmutableArray();
        Check(bosses.Length == 2, "OP10 fixture requires two actual generated boss identities");
        var originalStreams = baseline.Working.Streams;
        var context = baseline.Context with { Operation = new(1, 0, 1, []) };
        for (int cursor = 0; cursor <= 2; cursor++)
        {
            // Retain actual generated model identities, controlling only the
            // already-observed double-boss queue and its consumption cursor.
            var current = actualActs[0] with { Bosses = bosses, BossCursor = cursor };
            var next = actualActs[1];
            var state = baseline.Working with { Acts = [current, next, actualActs[2]],
                Position = new(current.Act, 7, "operation:act-information") };
            var snapshot = new PredictorOperationSnapshot("act-information:" + cursor, context, state,
                new PredictorOperationTreasureScene(null, true, true), 60, 80);
            var information = PredictorOperationInformation.CurrentAndNextAct(snapshot);
            Check(information.Length == 2 && information[0].Act == current.Act && information[0].ActKey == current.Key &&
                information[0].Ancient == current.Ancient && !information[0].IsNextAct &&
                information[0].RemainingBosses.SequenceEqual(bosses.Skip(cursor)) &&
                information[1].Act == next.Act && information[1].ActKey == next.Key && information[1].Ancient == next.Ancient &&
                information[1].IsNextAct && information[1].RemainingBosses.SequenceEqual(next.Bosses.Skip(next.BossCursor)) &&
                information.All(a => a.Provenance == "ObservedGeneratedRunIdentity"),
                "OP10 lost actual current/next identities or double-boss cursor " + cursor);
            string otherSeed = context.Seed == "000000000001" ? "000000000002" : "000000000001";
            var reseeded = PredictorOperationInformation.CurrentAndNextAct(snapshot with { Context = context with { Seed = otherSeed } });
            Check(reseeded.Length == information.Length && reseeded.Zip(information).All(pair =>
                pair.First.Act == pair.Second.Act && pair.First.ActKey == pair.Second.ActKey && pair.First.Ancient == pair.Second.Ancient &&
                pair.First.IsNextAct == pair.Second.IsNextAct && pair.First.RemainingBosses.SequenceEqual(pair.Second.RemainingBosses)) &&
                snapshot.State == state && state.Streams.SequenceEqual(originalStreams),
                "OP10 regenerated identities from seed or consumed captured RNG");
        }
        var last = actualActs[^1] with { Bosses = bosses, BossCursor = 2 };
        var finalState = baseline.Working with { Acts = actualActs.SetItem(actualActs.Length - 1, last),
            Position = new(last.Act, 50, "operation:last-act-information") };
        var finalSnapshot = new PredictorOperationSnapshot("act-information:last", context, finalState,
            new PredictorOperationTreasureScene(null, true, true), 60, 80);
        var final = PredictorOperationInformation.CurrentAndNextAct(finalSnapshot);
        Check(final.Length == 1 && final[0].ActKey == last.Key && final[0].Ancient == last.Ancient &&
            !final[0].IsNextAct && final[0].RemainingBosses.IsEmpty && finalState.Streams.SequenceEqual(originalStreams) &&
            baseline.Working.Acts == actualActs && baseline.Working.Streams == originalStreams,
            "OP10 invented a next act/boss or mutated the original generated state");
        Console.WriteLine("OPERATION_INFORMATION actual generated act/Ancient/boss identities; double-boss cursors 0/1/2; final act; seed independence/full RNG purity PASS");
    }

    private static void PotionIdentityQueries(PredictorRun baseline)
    {
        var catalog = baseline.Context.Catalog!;
        CardModel Model(ModelKey key) => ModelDb.GetById<CardModel>(new(key.Category, key.Entry));
        var character = catalog.CharacterCards.Select(c => Model(c.CardKey)).ToImmutableArray();
        var colorless = catalog.ColorlessCards.Select(c => Model(c.CardKey)).ToImmutableArray();
        PredictorOperationPotionCard Copy(CardModel card)
        {
            var key = new ModelKey(card.Id.Category, card.Id.Entry);
            var prototype = catalog.CardDefinitions.Single(d => d.Prototype.Key == key).Prototype;
            return new(prototype, card.Rarity.ToString(), card.CanBeGeneratedInCombat, card.MultiplayerConstraint.ToString());
        }
        var before = baseline.Working;
        var source = before.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var cursor = new PredictorOperationEventRng(source.S0, source.S1, source.S2, source.S3, source.Calls);
        foreach (int players in new[] { 1, 2 })
        foreach (string potion in new[] { "ATTACK_POTION", "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION", "COSMIC_CONCOCTION", "OROBIC_ACID" })
        {
            var context = baseline.Context with { Operation = new(1, 0, players, [])
            { CombatCardRng = cursor, CombatCardPools = new(character.Select(Copy).ToImmutableArray(), colorless.Select(Copy).ToImmutableArray()) } };
            var actual = PredictorOperationPotionQueries.Query(context, new("POTION", potion));
            var native = Native(source);
            var expected = new List<CardModel>();
            void Generate(IEnumerable<CardModel> pool, MegaCrit.Sts2.Core.Entities.Cards.CardType? type, int count)
            {
                var candidates = pool.Where(c => type == null || c.Type == type)
                    .Where(c => players > 1
                        ? c.MultiplayerConstraint != MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.SingleplayerOnly
                        : c.MultiplayerConstraint != MegaCrit.Sts2.Core.Entities.Cards.CardMultiplayerConstraint.MultiplayerOnly);
                expected.AddRange(CardFactory.FilterForCombat(candidates).TakeRandom(count, native));
            }
            if (potion == "OROBIC_ACID")
            {
                Generate(character, MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack, 1);
                Generate(character, MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill, 1);
                Generate(character, MegaCrit.Sts2.Core.Entities.Cards.CardType.Power, 1);
            }
            else Generate(potion is "COLORLESS_POTION" or "COSMIC_CONCOCTION" ? colorless : character,
                potion switch
                {
                    "ATTACK_POTION" => MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack,
                    "SKILL_POTION" => MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill,
                    "POWER_POTION" => MegaCrit.Sts2.Core.Entities.Cards.CardType.Power,
                    _ => null
                }, 3);
            Check(actual.IdentitiesComputed && actual.Cards.Select(c => c.Card.Key).SequenceEqual(expected.Select(c => new ModelKey(c.Id.Category, c.Id.Entry))),
                "Potion query identities differ from native FilterForCombat/TakeRandom: " + potion);
            var rng = native.ToSerializable();
            Check(actual.RngAfter == new PredictorOperationEventRng(rng.state0, rng.state1, rng.state2, rng.state3, rng.counter),
                "Potion query RNG differs from native full-pool shuffle: " + potion);
            Check(!actual.CanUseOutOfCombat && actual.Preconditions.Contains("NoInterveningCombatCardGenerationBeforeUse") &&
                actual.Cards.All(c => c.Card.Id == 0), "Potion identity query claimed use or real instances");
            if (potion == "COSMIC_CONCOCTION")
            {
                var upgraded = expected.Select(c =>
                {
                    var copy = c.ToMutable();
                    if (copy.IsUpgradable) { copy.UpgradeInternal(); copy.FinalizeUpgradeInternal(); }
                    return copy.CurrentUpgradeLevel;
                });
                Check(actual.Cards.Select(c => c.Card.UpgradeLevel).SequenceEqual(upgraded), "Cosmic query lost native upgraded attributes");
            }
        }
        var missing = PredictorOperationPotionQueries.Query(baseline.Context, new("POTION", "ATTACK_POTION"));
        Check(!missing.IdentitiesComputed && missing.MissingInputs.Contains("CombatCardGenerationStateMissing") && missing.Cards.IsEmpty,
            "Missing combat-card cursor invented potion results");
        Check(baseline.Working == before, "Potion query mutated captured inventory/RNG");
    }

    private static void ObtainedChildContinuations(PredictorRun baseline)
    {
        foreach (var sample in new[]
        {
            ("EMPTY_CAGE", PredictorOperationObtainChildKind.Deck),
            ("NEW_LEAF", PredictorOperationObtainChildKind.NewLeaf),
            ("HEFTY_TABLET", PredictorOperationObtainChildKind.ChooseCard),
            ("SEA_GLASS", PredictorOperationObtainChildKind.ChooseManyCards),
            ("SCROLL_BOXES", PredictorOperationObtainChildKind.Bundle),
            ("ORRERY", PredictorOperationObtainChildKind.Rewards)
        })
        {
            long rewardId = baseline.Working.NextInstanceId;
            long relicId = rewardId + 1;
            var state = baseline.Working with { NextInstanceId = relicId + 1 };
            var reward = new PredictorReward(rewardId, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat,
                Relic(sample.Item1), [], ContentInstanceId: relicId);
            var parent = new PredictorOperationRewardsScene([reward]);
            var direct = new PredictorRun(baseline.Context, state);
            parent.Restore(direct);
            var prepared = sample.Item1 == "SEA_GLASS"
                ? new PredictorRelic(relicId, reward.Key, 1) { CharacterTarget = baseline.Context.Catalog!.CharacterPools[0].Character }
                : null;
            Check(!direct.BeginObtainRelic(reward.Key, relicId, PredictorObtainReturn.Reward, rewardId, prepared: prepared),
                "Obtain fixture unexpectedly has no pending choice: " + sample.Item1);
            var child = new PredictorOperationObtainScene(parent, PredictorObtainReturn.Reward, rewardId,
                relicId, reward.Key, sample.Item2)
            {
                DeckChoice = sample.Item2 == PredictorOperationObtainChildKind.Deck
                    ? new(relicId, reward.Key, PredictorObtainDeckOperation.Remove, direct.Selection.RequiredCardCount, direct.Selection.CandidateCardIds) : null,
                NewLeafCandidates = sample.Item2 == PredictorOperationObtainChildKind.NewLeaf ? direct.Selection.CandidateCardIds : [],
                Cards = sample.Item2 == PredictorOperationObtainChildKind.ChooseCard ? direct.ObtainCardChoices :
                    sample.Item2 == PredictorOperationObtainChildKind.ChooseManyCards ? direct.ObtainManyCardChoices : [],
                Bundles = direct.ScrollBoxBundles,
                RewardScene = sample.Item2 == PredictorOperationObtainChildKind.Rewards ? new(direct.Rewards) : null
            };
            var pending = direct.Working;
            var generatedIds = child.Cards.Concat(child.Bundles.SelectMany(b => b)).Select(c => c.Id).ToImmutableHashSet();
            // Real captures do not register native picker-owned candidates in
            // the deck state. Import must register those exact copied IDs.
            var nativeEquivalent = pending with { Cards = pending.Cards.Where(c => !generatedIds.Contains(c.Id)).ToImmutableArray() };
            var imported = new PredictorRun(baseline.Context, nativeEquivalent);
            child.Restore(imported);
            Check(imported.Working.Cards.OrderBy(c => c.Id).SequenceEqual(pending.Cards.OrderBy(c => c.Id)) && imported.Phase == direct.Phase &&
                imported.Working.Relics.Count(r => r.Id == relicId) == 1,
                "Obtained child import replayed state: " + sample.Item1);
            PredictorInput input = sample.Item2 switch
            {
                PredictorOperationObtainChildKind.Deck => new ChooseObtainDeckCards(direct.Selection.CandidateCardIds.Take(direct.Selection.RequiredCardCount).ToImmutableArray()),
                PredictorOperationObtainChildKind.NewLeaf => new SelectTransformationCard(direct.Selection.CandidateCardIds[0]),
                PredictorOperationObtainChildKind.ChooseCard => new ChooseObtainCard(direct.ObtainCardChoices[0].Id),
                PredictorOperationObtainChildKind.ChooseManyCards => new ChooseObtainManyCards(direct.ObtainManyCardChoices.Take(2).Select(c => c.Id).ToImmutableArray()),
                PredictorOperationObtainChildKind.Bundle => new ChooseScrollBoxBundle(0),
                _ => new LeaveRewards()
            };
            Send(direct, input); Send(imported, input);
            Check(imported.Phase == direct.Phase && imported.Rewards.Single(r => r.Id == rewardId).Status == PredictorRewardStatus.Taken &&
                imported.Working.Deck.SequenceEqual(direct.Working.Deck) && imported.Working.Cards.OrderBy(c => c.Id).SequenceEqual(direct.Working.Cards.OrderBy(c => c.Id)) &&
                imported.Working.Relics.SequenceEqual(direct.Working.Relics) && imported.Working.Streams.SequenceEqual(direct.Working.Streams),
                "Obtained child did not match uninterrupted source completion: " + sample.Item1);
        }
    }
}
