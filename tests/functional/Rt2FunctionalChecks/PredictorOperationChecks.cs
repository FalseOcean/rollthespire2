using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperationChecks
{
    private static void Check(bool value, string description)
    { if (!value) throw new InvalidOperationException("OPERATION " + description); }

    internal static void Run(PredictorRun baseline)
    {
        var context = baseline.Context with { Live = null, Crystal = null, Operation = new(1, 0, 1, []) };
        var initial = baseline.Working with { Position = new(1, 3, "operation:test") };
        long rewardId = initial.NextInstanceId, relicId = rewardId + 1;
        var state = initial with { NextInstanceId = relicId + 1 };
        var reward = new PredictorReward(rewardId, PredictorRewardKind.Relic, PredictorRewardOrigin.Combat,
            new("RELIC", "LEES_WAFFLE"), [], ContentInstanceId: relicId);
        var snapshot = new PredictorOperationSnapshot("health-fixture", context, state,
            new PredictorOperationRewardsScene([reward]), 12, 80);
        var healed = PredictorOperation.Preview(snapshot, new TakeReward(rewardId));
        Check(healed.Disposition == PredictorInputResult.Accepted && healed.CurrentHp == 87 && healed.MaximumHp == 87 &&
            healed.Rewards.Single().Status == PredictorRewardStatus.Taken && healed.Waiting.Phase == PredictorPhase.Rewards,
            "captured HP must automatically resolve Waffle obtain and resume reward");
        Check(snapshot.State == state && snapshot.CurrentHp == 12 && state.Relics.All(r => r.Id != relicId), "preview mutated snapshot");
        var again = PredictorOperation.Preview(snapshot, new TakeReward(rewardId));
        Check(again.State.Relics.SequenceEqual(healed.State.Relics) && again.State.Streams.SequenceEqual(healed.State.Streams), "repeat preview changed result");

        var opaque = new ModelKey("RELIC", "OPERATION_EXTERNAL_RELIC");
        var modSnapshot = snapshot with { Context = context with { Operation = new(1, 0, 1, [opaque]) },
            Scene = new PredictorOperationRewardsScene([reward with { Key = opaque }]) };
        var opaqueResult = PredictorOperation.Preview(modSnapshot, new TakeReward(rewardId));
        Check(opaqueResult.Disposition == PredictorInputResult.Accepted && opaqueResult.State.Relics.Any(r => r.Key == opaque) &&
            opaqueResult.State.Streams.SequenceEqual(state.Streams) && opaqueResult.CurrentHp == 12,
            "operation-only opaque relic must retain identity with zero extra effects");
        bool strictRejected = false;
        try { PredictorSettlementEffects.RequireImplementedRelic(opaque); }
        catch (NotImplementedException) { strictRejected = true; }
        Check(strictRejected, "operation policy leaked into strict default");

        var rest = snapshot with { Scene = new PredictorOperationRestScene([PredictorRestAction.Heal]),
            State = state with { Relics = [] }, CurrentHp = 10, MaximumHp = 80 };
        var rested = PredictorOperation.Preview(rest, new SelectRestAction(PredictorRestAction.Heal));
        Check(rested.CurrentHp == 34 && rested.MaximumHp == 80, "rest fixed health consequence not folded");

        var eventRng = state.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var targets = state.Deck.Where(id => state.Card(id).Removable).Take(3).ToImmutableArray();
        var picker = new PredictorOperationEventScene(new("EVENT", "MORPHIC_GROVE"), eventRng, [], new(80, 80))
        { Branch = "GROUP", AwaitingCards = true, CardsRequired = 2, CardCandidates = targets };
        var combinationSnapshot = snapshot with { State = state with { Relics = [] }, Scene = picker, CurrentHp = 80 };
        var space = PredictorOperationCombinations.Describe(combinationSnapshot, [], ordered: true);
        var combinations = PredictorOperationCombinations.Enumerate(combinationSnapshot, [], ordered: true).ToArray();
        Check(space.Count == 6 && combinations.Length == 6 && combinations.All(c => c.Targets.Distinct().Count() == 2 &&
            c.Result.Disposition == PredictorInputResult.Accepted), "ordered target combinations incomplete");
        foreach (var combination in combinations)
        {
            var single = PredictorOperation.Preview(combinationSnapshot, new SelectEventCards(combination.Targets));
            Check(single.State.Cards.SequenceEqual(combination.Result.State.Cards) &&
                single.State.Streams.SequenceEqual(combination.Result.State.Streams), "combination differs from explicit choice");
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool cancelled = false;
        try { _ = PredictorOperation.Preview(snapshot, new TakeReward(rewardId), cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && snapshot.State == state, "cancel must preserve captured input");
        Console.WriteLine("OPERATION pure repeat/cancel; automatic HP; scoped opaque policy; ordered combinations PASS");
    }
}
