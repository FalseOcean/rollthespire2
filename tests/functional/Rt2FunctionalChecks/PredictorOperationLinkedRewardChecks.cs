using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperationLinkedRewardChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("OPERATION_LINKED " + message); }

    internal static void Run(PredictorRun baseline)
    {
        long groupId = baseline.Working.NextInstanceId, goldId = groupId + 1, relicRewardId = groupId + 2,
            contentId = groupId + 3, independentId = groupId + 4;
        var state = baseline.Working with { NextInstanceId = groupId + 5, Relics = [], OpeningCompleted = true,
            Position = new(1, 3, "operation:linked"), Visits = [] };
        var context = baseline.Context with { Operation = new(1, 0, 1, []) };
        var gold = new PredictorReward(goldId, PredictorRewardKind.Gold, PredictorRewardOrigin.Event, default, [], GoldAmount: 10);
        var relic = new PredictorReward(relicRewardId, PredictorRewardKind.Relic, PredictorRewardOrigin.Event,
            new("RELIC", "NEW_LEAF"), [], ContentInstanceId: contentId);
        var independent = new PredictorReward(independentId, PredictorRewardKind.Gold, PredictorRewardOrigin.Event, default, [], GoldAmount: 7);
        var scene = new PredictorOperationRewardsScene([gold, relic, independent])
        { LinkedGroups = [new(groupId, [goldId, relicRewardId])] };
        var snapshot = new PredictorOperationSnapshot("linked", context, state, scene, 60, 80);
        var paid = PredictorOperation.Preview(snapshot, new TakeReward(goldId));
        Check(paid.State.GoldBudget == state.GoldBudget + 10 && paid.Rewards.Select(r => r.Id).SequenceEqual([goldId, relicRewardId, independentId]) &&
            paid.Rewards[0].Status == PredictorRewardStatus.Taken && paid.Rewards[1].Status == PredictorRewardStatus.Skipped &&
            paid.Rewards[2].Status == PredictorRewardStatus.Open, "selected linked gold failed to retire only its sibling");
        var begun = PredictorOperation.Preview(snapshot, new TakeReward(relicRewardId));
        Check(begun.Waiting.Phase == PredictorPhase.TransformCards && begun.Rewards[0].Status == PredictorRewardStatus.Open,
            "linked sibling retired before relic child choice completed");
        var complete = PredictorOperation.Preview(snapshot, [new TakeReward(relicRewardId), new SelectTransformationCard(begun.Waiting.Selection.CandidateCardIds[0])]);
        Check(complete.Rewards[0].Status == PredictorRewardStatus.Skipped && complete.Rewards[1].Status == PredictorRewardStatus.Taken &&
            complete.Rewards[2].Status == PredictorRewardStatus.Open && complete.State.GoldBudget == state.GoldBudget,
            "relic child completion did not retire linked sibling at its real completion boundary");

        long cardId = state.NextInstanceId;
        var card = state.Card(state.Deck[0]) with { Id = cardId };
        var cardReward = new PredictorReward(relicRewardId, PredictorRewardKind.Card, PredictorRewardOrigin.Event, default, [card]);
        var cardSnapshot = snapshot with { State = state with { NextInstanceId = cardId + 1 },
            Scene = scene with { Rewards = [gold, cardReward, independent] } };
        var declined = PredictorOperation.Preview(cardSnapshot, [new EnterCardSelection(relicRewardId), new SkipCardReward()]);
        Check(declined.Rewards[0].Status == PredictorRewardStatus.Open && declined.Rewards[1].Status == PredictorRewardStatus.Open &&
            declined.State.Deck.SequenceEqual(state.Deck), "declining a linked card offer counted as successful selection");
        Check(state.Relics.IsEmpty && scene.Rewards.All(r => r.Status == PredictorRewardStatus.Open), "linked query mutated source offers");
        Console.WriteLine("OPERATION_LINKED copied identity/order; success-only alternative retirement; child completion; card decline preserves choice PASS");
    }
}
