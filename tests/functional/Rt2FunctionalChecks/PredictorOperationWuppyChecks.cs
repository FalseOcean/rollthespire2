using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class PredictorOperationEventChecks
{
    // The original live log has Future, Lantern Key and Repy failures while
    // holding Wuppy. These fixtures retain those actual entry/choice chains;
    // the independent game-source witness is Modern113 Wuppy/CombatState.
    private static void CheckWuppyBoundaries(PredictorContext context, PredictorState state, PredictorStreamState rng)
    {
        var wuppy = new ModelKey("RELIC", "WUPPY");
        long relicId = state.NextInstanceId, potionId = relicId + 1;
        var held = state with { Relics = [new(relicId, wuppy, 1)],
            NextInstanceId = potionId + 1, PotionSlots = [new(potionId, new("POTION", "FOUL_POTION")), null] };
        PredictorOperationSnapshot Snapshot(PredictorState current, PredictorOperationEventScene scene) =>
            new("wuppy:" + scene.Event.Entry, context, current, scene, 60, 80);
        var future = new PredictorOperationEventScene(new("EVENT", "THE_FUTURE_OF_POTIONS"), rng,
            [new("POTION:" + potionId, PotionId: potionId, CardType: EffectCardType.Skill)], new(60, 80))
        {
            FuturePotionTypes = ImmutableDictionary<long, EffectCardType>.Empty.Add(potionId, EffectCardType.Skill),
            FuturePotionRarities = ImmutableDictionary<long, EffectCardRarity>.Empty.Add(potionId, EffectCardRarity.Common)
        };
        var plain = held with { Relics = [] };
        var result = PredictorOperation.Preview(Snapshot(held, future), new ChooseEventOption("POTION:" + potionId));
        var without = PredictorOperation.Preview(Snapshot(plain, future), new ChooseEventOption("POTION:" + potionId));
        Require(result.Disposition == PredictorInputResult.Accepted && result.Rewards.Single().Cards.Length == 3 &&
            result.Rewards.Single().Cards.SequenceEqual(without.Rewards.Single().Cards) &&
            result.State.Streams.SequenceEqual(without.State.Streams), "Wuppy changed already-generated Future offers/RNG");
        var preview = EventOperationPreview.Build(Snapshot(held, future), "POTION:" + potionId);
        Require(preview.Faults.IsEmpty && preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).Any(),
            "Wuppy still blocks Future's original hover forecast");
        // An opaque implementation gap is a protocol contrast, not a claim
        // that this invented identity is reachable in the game. The audited
        // held-item scope must not admit arbitrary unknown hook continuations.
        var unsupported = held with { Relics = [new(relicId, new("RELIC", "UNIMPLEMENTED_NATIVE_RELIC"), 1)] };
        var unsupportedSnapshot = Snapshot(unsupported, future);
        bool blockedUnknown = false;
        try
        {
            PredictorOperation.Preview(unsupportedSnapshot, new ChooseEventOption("POTION:" + potionId));
        }
        catch (NotImplementedException error) when (error.Message.Contains("UNIMPLEMENTED_NATIVE_RELIC", StringComparison.Ordinal))
        { blockedUnknown = true; }
        Require(blockedUnknown, "Audited held-item support silently authorized unimplemented inventory hooks");

        var lantern = new PredictorOperationEventScene(new("EVENT", "THE_LANTERN_KEY"), rng,
            [new("KEEP_THE_KEY"), new("RETURN_THE_KEY")], new(60, 80));
        var fight = PredictorOperation.Preview(Snapshot(held, lantern),
            [new ChooseEventOption("KEEP_THE_KEY"), new ChooseEventOption("FIGHT")]);
        var plainFight = PredictorOperation.Preview(Snapshot(plain, lantern),
            [new ChooseEventOption("KEEP_THE_KEY"), new ChooseEventOption("FIGHT")]);
        Require(fight.Disposition == PredictorInputResult.Accepted && fight.Waiting.Phase == PredictorPhase.CombatFacts &&
            fight.State.Streams.SequenceEqual(plainFight.State.Streams), "Wuppy blocked fight preparation or invented a pet HP RNG draw");
        var fightPreview = EventOperationPreview.Build(Snapshot(held, lantern), "KEEP_THE_KEY");
        Require(fightPreview.Faults.IsEmpty && fightPreview.Branches.Any(), "Wuppy still blocks Lantern's original hover forecast");

        var keyed = PredictorObtainSources.AddCard(context, held, new ModelKey("CARD", "LANTERN_KEY"));
        var repy = new PredictorOperationEventScene(new("EVENT", "WAR_HISTORIAN_REPY"), rng,
            [new("INITIAL_UNLOCK_CHEST")], new(60, 80));
        var chest = PredictorOperation.Preview(Snapshot(keyed, repy), new ChooseEventOption("INITIAL_UNLOCK_CHEST"));
        Require(chest.Disposition == PredictorInputResult.Accepted && chest.Rewards.Any(r => r.Kind == PredictorRewardKind.Relic),
            "Wuppy still blocks Repy's chest reward publication");
        Require(EventOperationPreview.Build(Snapshot(keyed, repy), "INITIAL_UNLOCK_CHEST").Faults.IsEmpty,
            "Wuppy still blocks Repy's original hover forecast");

        long shopId = held.NextInstanceId;
        var shop = new PredictorOperationSnapshot("wuppy:shop", context,
            held with { GoldBudget = 10000, NextInstanceId = shopId + 1 },
            new PredictorOperationShopScene([new(shopId, PredictorShopEntryKind.Relic, new("RELIC", "ORRERY"),
                null, 1, false, true, 0)], true), 60, 80);
        var purchase = SurfaceOperationPreview.BuildShop(shop, shopId);
        Require(purchase.Faults.IsEmpty && purchase.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).Count() == 5,
            "Wuppy still blocks the shared offer publisher through Orrery purchase");
        var rest = new PredictorOperationSnapshot("wuppy:rest", context, held with
        {
            Relics = held.Relics.Add(new(held.NextInstanceId, new("RELIC", "DREAM_CATCHER"), 1)),
            NextInstanceId = held.NextInstanceId + 1
        }, new PredictorOperationRestScene([PredictorRestAction.Heal]), 60, 80);
        var healing = SurfaceOperationPreview.BuildRest(rest, PredictorRestAction.Heal);
        Require(healing.Faults.IsEmpty && healing.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).Any(),
            "Wuppy still blocks the shared offer publisher through rest rewards");

        // Fixed gold really depends on Ectoplasm. Wuppy is independent, while
        // this existing modifier must continue to suppress the receipt.
        var gold = PredictorOperation.Preview(Snapshot(held, lantern), new ChooseEventOption("RETURN_THE_KEY"));
        var blockedGold = PredictorOperation.Preview(Snapshot(held with
        {
            Relics = held.Relics.Add(new(held.NextInstanceId, new("RELIC", "ECTOPLASM"), 1)),
            NextInstanceId = held.NextInstanceId + 1
        }, lantern), new ChooseEventOption("RETURN_THE_KEY"));
        Require(gold.State.GoldBudget == held.GoldBudget + 100 && blockedGold.State.GoldBudget == held.GoldBudget,
            "Local Wuppy support weakened actual gold settlement dependencies");
        bool unsupportedRoute = false;
        try { PredictorSettlementEffects.RequireImplementedRelic(wuppy, context: context with { Operation = null }); }
        catch (NotImplementedException) { unsupportedRoute = true; }
        Require(unsupportedRoute, "Wuppy's unimplemented future-route Restore option was silently admitted");
        Require(held.Relics.Length == 1 && held.Relics[0].Key == wuppy && held.PotionSlots[0]?.Id == potionId,
            "Wuppy forecast mutated captured source inventory");
        // Same inventory consumer, other native paths: Thermae's Bath Towel /
        // Bath Robes, Shrine of the Warlord's Mighty Wings, and evolving Byrdpip
        // into Byrdine at a rest site. Acquisition effects have already finished.
        foreach (string entry in new[] { "BATH_TOWEL", "BATH_ROBES", "BYRDINE", "MIGHTY_WINGS" })
        {
            var otherHeld = held with { Relics = [new(relicId, new("RELIC", entry), 1)] };
            var offers = PredictorOperation.Preview(Snapshot(otherHeld, future), new ChooseEventOption("POTION:" + potionId));
            var combat = PredictorOperation.Preview(Snapshot(otherHeld, lantern),
                [new ChooseEventOption("KEEP_THE_KEY"), new ChooseEventOption("FIGHT")]);
            var claim = PredictorOperation.Preview(Snapshot(otherHeld, future),
                [new ChooseEventOption("POTION:" + potionId), new EnterCardSelection(offers.Rewards.Single().Id),
                    new TakeReward(offers.Rewards.Single().Id, offers.Rewards.Single().Cards[0].Id)]);
            Require(offers.Rewards.Single().Cards.SequenceEqual(without.Rewards.Single().Cards) &&
                combat.Waiting.Phase == PredictorPhase.CombatFacts && combat.State.Streams.SequenceEqual(plainFight.State.Streams) &&
                claim.Disposition == PredictorInputResult.Accepted,
                entry + " held inventory still blocks unrelated offer/receipt/combat preparation");
            Require(EventOperationPreview.Build(Snapshot(otherHeld, future), "POTION:" + potionId).Faults.IsEmpty &&
                EventOperationPreview.Build(Snapshot(otherHeld, lantern), "KEEP_THE_KEY").Faults.IsEmpty,
                entry + " original hover facade still fails");
        }
        foreach (string entry in new[] { "WUPPY", "BATH_TOWEL", "BATH_ROBES", "BYRDINE", "MIGHTY_WINGS" })
        {
            bool unsupportedAcquisition = false;
            try { PredictorSettlementEffects.AddRelic(plain, new("RELIC", entry), context: context); }
            catch (NotImplementedException) { unsupportedAcquisition = true; }
            bool combatOnly = entry is "BATH_TOWEL" or "MIGHTY_WINGS";
            Require(unsupportedAcquisition != combatOnly, entry + " acquisition must match its native noncombat hooks");
        }
        Console.WriteLine("OPERATION_WUPPY 5 reachable held items; Future offers/receipt + Lantern combat preparation + Repy chest + Ectoplasm/acquisition contrasts PASS");
    }
}
