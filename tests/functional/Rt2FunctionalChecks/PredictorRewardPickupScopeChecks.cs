using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorRewardPickupScopeChecks
{
    internal static void Run(PredictorContext context, PredictorState state)
    {
        static PredictorReward Reward(long id, PredictorRewardKind kind, PredictorRewardOrigin origin,
            string entry = "") => new(id, kind, origin,
                entry.Length == 0 ? default : new ModelKey(kind == PredictorRewardKind.Potion ? "POTION" : "RELIC", entry), []);
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        // A rest node can inherit this flag from a preceding combat. It must
        // never give combat defaults authority over rest/obtain child rewards.
        var afterCombat = state with { CurrentRoomIsCombat = true };
        foreach (var origin in new[] { PredictorRewardOrigin.Event, PredictorRewardOrigin.SmallCapsule,
            PredictorRewardOrigin.BonesInternal })
        {
            ImmutableArray<PredictorReward> rewards = [
                Reward(100, PredictorRewardKind.Gold, origin) with { GoldAmount = 15 },
                Reward(101, PredictorRewardKind.Potion, origin, "ENERGY_POTION"),
                Reward(102, PredictorRewardKind.Relic, origin, "BOWLER_HAT"),
                Reward(103, PredictorRewardKind.Relic, origin, "VAJRA")];
            var plan = PredictorRewardPickupPolicy.Plan(afterCombat, rewards);
            Check(plan.NextInput == null && plan.Pending.Length == rewards.Length &&
                plan.Pending.All(p => p.Reason == PredictorRewardPickupPendingReason.PlayerChoice),
                $"Non-combat {origin} rewards require manual pickup even after combat");
        }

        ImmutableArray<PredictorReward> mixed = [
            Reward(200, PredictorRewardKind.Relic, PredictorRewardOrigin.Event, "BOWLER_HAT"),
            Reward(201, PredictorRewardKind.Gold, PredictorRewardOrigin.Combat) with { GoldAmount = 15 },
            Reward(202, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, "VAJRA")];
        var mixedPlan = PredictorRewardPickupPolicy.Plan(afterCombat, mixed);
        Check(mixedPlan.NextInput is TakeReward { RewardId: 202 } &&
            mixedPlan.Pending is [{ RewardId: 200, Reason: PredictorRewardPickupPendingReason.PlayerChoice }],
            "Manual reward provenance does not stop an independent combat pickup");
        var projected = PredictorRewardPickupPolicy.Plan(afterCombat, mixed, includeOrdinaryNonCombatRelics: true);
        Check(projected.NextInput is TakeReward { RewardId: 200 },
            "Explicit projection scope can claim a supported ordinary non-combat relic");
        var choiceRelic = Reward(204, PredictorRewardKind.Relic, PredictorRewardOrigin.Event, "DOLLYS_MIRROR");
        Check(PredictorRewardPickupPolicy.Plan(afterCombat, [choiceRelic], includeOrdinaryNonCombatRelics: true)
            is { NextInput: null, Pending: [{ Reason: PredictorRewardPickupPendingReason.PlayerChoice }] },
            "Broader ordinary pickup does not invent a deck choice for a child obtain");

        var eventPotion = Reward(205, PredictorRewardKind.Potion, PredictorRewardOrigin.Event, "ENERGY_POTION");
        Check(PredictorRewardPickupPolicy.Plan(afterCombat, [eventPotion], includeNonCombatPotions: true)
            .NextInput is TakeReward { RewardId: 205 }, "Explicit forecast potion default fills an available slot across reward origins");
        var full = state with { PotionSlots = state.PotionSlots.Select((_, slot) =>
            (PredictorPotion?)new PredictorPotion(state.NextInstanceId + slot, new("POTION", "ENERGY_POTION"))).ToImmutableArray() };
        var independentGold = Reward(206, PredictorRewardKind.Gold, PredictorRewardOrigin.Combat) with { GoldAmount = 15 };
        var beforeStreams = full.Streams;
        Check(PredictorRewardPickupPolicy.IsDefaultPotionOmission(full, eventPotion) &&
            PredictorRewardPickupPolicy.Plan(full, [eventPotion, independentGold], includeNonCombatPotions: true)
                .NextInput is TakeReward { RewardId: 206 } && full.Streams == beforeStreams,
            "Full inventory omits default potion pickup without changing RNG or blocking independent gold");
        var sozu = PredictorSettlementEffects.ObtainRelic(state, new("RELIC", "SOZU"));
        Check(PredictorRewardPickupPolicy.IsDefaultPotionOmission(sozu, eventPotion) &&
            PredictorRewardPickupPolicy.Plan(sozu, [eventPotion], includeNonCombatPotions: true)
                is { NextInput: null, Pending: [{ Reason: PredictorRewardPickupPendingReason.PotionNotAllowed }] },
            "Forecast potion default preserves Sozu procurement denial even with an empty slot");

        ImmutableArray<PredictorReward> combat = [
            Reward(300, PredictorRewardKind.Potion, PredictorRewardOrigin.Combat, "ENERGY_POTION"),
            Reward(301, PredictorRewardKind.Gold, PredictorRewardOrigin.Combat) with { GoldAmount = 15 },
            Reward(302, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, "VAJRA"),
            Reward(303, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat, "BOWLER_HAT")];
        foreach (long expected in new long[] { 303, 302, 301, 300 })
        {
            Check(PredictorRewardPickupPolicy.Plan(state, combat).NextInput is TakeReward take &&
                take.RewardId == expected, "Combat pickup order is benefit, ordinary, gold, then potion");
            combat = combat.Select(r => r.Id == expected ? r with { Status = PredictorRewardStatus.Taken } : r)
                .ToImmutableArray();
        }

        // Mandatory pickup legality is separate from the recommendation. The
        // player can take a held reward and its fixed obtain effect still runs.
        var mandatory = new PredictorRun(context, state);
        mandatory.EnterRewardNode(new(1, 3, "pickup-scope"),
            [Reward(0, PredictorRewardKind.Relic, PredictorRewardOrigin.BonesInternal, "VAJRA")]);
        Check(PredictorRewardPickupPolicy.Plan(mandatory).NextInput == null && mandatory.Rewards[0].Mandatory,
            "Mandatory non-combat rewards still require manual pickup");
        Check(mandatory.Submit(mandatory.Request!, new LeaveRewards()) == PredictorInputResult.Invalid,
            "Manual pickup scope preserves mandatory reward settlement legality");
        Check(mandatory.Submit(mandatory.Request!, new TakeReward(mandatory.Rewards[0].Id)) == PredictorInputResult.Accepted &&
            mandatory.Rewards[0].Status == PredictorRewardStatus.Taken &&
            mandatory.Working.Relics.Any(r => r.Key.Entry == "VAJRA"),
            "Manual mandatory pickup applies its fixed obtain effect");
    }
}
