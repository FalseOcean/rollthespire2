using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

// Detached reward continuation. These operations never touch live game objects.
internal sealed partial class PredictorRun
{
    internal long? CrystalSelectedReward => _cardSelection;

    internal PredictorRun ForkCrystalRewards()
    {
        if (Phase != PredictorPhase.Rewards) throw new InvalidOperationException("CrystalRewardForkPhase");
        var copy = FromCrystal(ExportCrystal());
        copy.Phase = Phase; copy.Rewards = Rewards; copy.GenerationStages = GenerationStages;
        copy._cardSelection = _cardSelection; copy._inputs = _inputs; copy._requestId = _requestId;
        return copy;
    }

    private PredictorInputResult SubmitCrystalReroll(RerollCardReward input)
    {
        if (_cardSelection != input.RewardId) return PredictorInputResult.Invalid;
        int i = OpenReward(input.RewardId);
        if (i < 0) return PredictorInputResult.Invalid;
        var reward = Rewards[i];
        if (reward.Kind != PredictorRewardKind.Card || !reward.CardSelectionEntered || !reward.CanReroll)
            return PredictorInputResult.NotAllowed;
        var recipe = reward.RerollRecipe ?? throw new InvalidOperationException("CrystalRerollRecipeMissing");
        var state = Working;
        var eventRng = _eventRng!.Clone();
        var generated = PredictorRewardGeneration.GenerateCards(Context, ref state, recipe,
            reward.RerollIsCardReward, recipe.UseEventRng ? eventRng : null);
        var cards = ImmutableArray.CreateBuilder<PredictorCard>();
        foreach (var card in generated)
        {
            PredictorSettlementEffects.RequireImplementedCard(card, Context.Crystal != null);
            cards.Add(card with { Id = state.NextInstanceId, Removed = false });
            state = state with { NextInstanceId = checked(state.NextInstanceId + 1) };
        }
        state.Validate();
        Working = state; if (recipe.UseEventRng) _eventRng = eventRng;
        Rewards = Rewards.SetItem(i, reward with { Cards = cards.ToImmutable(), CanReroll = false, HasRerolled = true });
        Accept(new("RerollCardReward", input.RewardId, reward.Cards.Select(c => c.Id).ToImmutableArray(), null, null));
        return PredictorInputResult.Accepted;
    }
}

internal sealed record CrystalSuffixNode(PredictorRun Run, ImmutableArray<PredictorInput> Actions);

// Exact ordered subsets of the AVAILABLE reward instances at one fixed endpoint.
// Takes are checked afterwards. Crystal's supported card-insertion hooks do not
// alter the fixed-rarity pools, event/Niche streams or later reward modifications.
// This normalization concerns acquired card identities/levels; it does not move
// native operations or claim every interleaving has an identical complete state.
internal static class PredictorCrystalRerolls
{
    internal static IEnumerable<CrystalSuffixNode> Prefixes(PredictorRun entry, CancellationToken token = default)
    {
        if (entry.CrystalSelectedReward != null) throw new InvalidOperationException("CrystalSuffixRequiresClosedSelector");
        var stack = new Stack<CrystalSuffixNode>(); stack.Push(new(entry.ForkCrystalRewards(), []));
        while (stack.TryPop(out var node))
        {
            token.ThrowIfCancellationRequested(); yield return node;
            foreach (var reward in node.Run.Rewards.Where(r => r.Kind == PredictorRewardKind.Card && r.Status == PredictorRewardStatus.Open && r.CanReroll))
            {
                var run = node.Run.ForkCrystalRewards();
                ImmutableArray<PredictorInput> actions = [new EnterCardSelection(reward.Id), new RerollCardReward(reward.Id), new ExitCardSelection()];
                foreach (var action in actions)
                    if (run.Submit(run.Request!, action) != PredictorInputResult.Accepted)
                        throw new InvalidOperationException("CrystalSuffixTransitionRejected");
                stack.Push(new(run, node.Actions.AddRange(actions)));
            }
        }
    }

    internal static CrystalSuffixNode? Take(CrystalSuffixNode node, ImmutableArray<CrystalRewardOption> goals)
    {
        // Research witnesses are card-only; never silently drop a requested goal.
        if (goals.Any(g => g.Kind != PredictorRewardKind.Card)) throw new ArgumentException("CrystalSuffixCardGoalsOnly", nameof(goals));
        var result=PredictorCrystalExplorer.TakeCards(node.Run,goals);
        if(result==null) return null;
        var run=node.Run.ForkCrystalRewards();var actions=node.Actions;
        foreach(var take in result.Value.Takes)
        {
            var reward=run.Rewards[take.RewardIndex];var card=reward.Cards.First(take.MatchesCard);
            ImmutableArray<PredictorInput> inputs=[new EnterCardSelection(reward.Id),new TakeReward(reward.Id,card.Id)];
            foreach(var action in inputs) if(run.Submit(run.Request!,action)!=PredictorInputResult.Accepted) return null;
            actions=actions.AddRange(inputs);
        }
        return new(run, actions);
    }
}
