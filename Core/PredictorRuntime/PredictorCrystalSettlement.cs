using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// Local Crystal witnesses share detached state and card-obtain rules with the
// route runtime, but never promise complete relic/potion settlement or travel.
internal sealed partial class PredictorRun
{
    private PredictorInputResult SubmitLocalCrystal(PredictorRequest request, PredictorInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request == null || Phase == PredictorPhase.Complete || Request != request) return PredictorInputResult.Stale;
        if (TrySubmitCrystal(input, cancellationToken) is { } result) return result;
        if (Phase != PredictorPhase.Rewards) return PredictorInputResult.Invalid;
        if (input is ExitCardSelection)
        {
            if (_cardSelection is not { } current) return PredictorInputResult.Invalid;
            int i = OpenReward(current);
            if (i < 0) return PredictorInputResult.Invalid;
            Rewards = Rewards.SetItem(i, Rewards[i] with { CardSelectionEntered = false, RelicSubscriptionActive = false });
            _cardSelection = null;
            Accept(new("ExitCardSelection", current, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is RerollCardReward reroll) return SubmitCrystalReroll(reroll);
        if (input is SacrificeCardReward sacrifice) return SubmitCrystalSacrifice(sacrifice, cancellationToken);
        if (input is EnterCardSelection enter)
        {
            int i = OpenReward(enter.RewardId);
            if (i < 0 || Rewards[i].Kind != PredictorRewardKind.Card || _cardSelection != null) return PredictorInputResult.Invalid;
            _ = GetCardRewardAlternatives(enter.RewardId);
            _cardSelection = enter.RewardId;
            Rewards = Rewards.SetItem(i, Rewards[i] with { CardSelectionEntered = true });
            Accept(new("EnterCardSelection", enter.RewardId, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is not TakeReward take) return PredictorInputResult.Invalid;
        int index = OpenReward(take.RewardId);
        if (index < 0 || (_cardSelection != null && _cardSelection != take.RewardId)) return PredictorInputResult.Invalid;
        var selected = Rewards[index];
        if (selected.Kind != PredictorRewardKind.Card && take.CardId != null) return PredictorInputResult.Invalid;
        // The solver verifies card taking; relics/potions are reward targets,
        // not a promise to simulate their subsequent obtain/choice effects.
        if (selected.Kind != PredictorRewardKind.Card) return PredictorInputResult.Invalid;
        var card = selected.Cards.FirstOrDefault(c => c.Id == take.CardId);
        if (card == null || !selected.CardSelectionEntered) return PredictorInputResult.Invalid;
        if (!Working.Cards.Any(c => c.Id == card.Id)) Working = Working with { Cards = Working.Cards.Add(card) };
        Working = PredictorObtainSources.AddExistingCard(Context, Working, card);
        MarkTaken(selected.Id);
        _cardSelection = null;
        Working.Validate();
        Accept(new("TakeReward", selected.Id, take.CardId is long cardId ? [cardId] : [], null, null));
        return PredictorInputResult.Accepted;
    }

    private PredictorInputResult SubmitCrystalSacrifice(SacrificeCardReward input, CancellationToken token)
    {
        if (_cardSelection != input.RewardId) return PredictorInputResult.Invalid;
        int index = OpenReward(input.RewardId);
        if (index < 0 || Rewards[index].Kind != PredictorRewardKind.Card || !Rewards[index].CardSelectionEntered)
            return PredictorInputResult.Invalid;
        // Preserve the native alternative-count fault, including Driftwood + Wing.
        if (!GetCardRewardAlternatives(input.RewardId).Any(a => a.Key == "SACRIFICE" && a.RelicId == input.WingRelicId))
            return PredictorInputResult.Invalid;
        int wingIndex = Working.Relics.FindIndex(r => r.Id == input.WingRelicId && !r.Melted && r.Key.Entry == "PAELS_WING");
        if (wingIndex < 0) return PredictorInputResult.Invalid;
        int count = checked(Working.Relics[wingIndex].RewardsSacrificed + 1);
        var state = Working;
        ModelKey? gift = count % 2 == 0 ? PredictorRewardGeneration.PullRelic(Context, ref state) : null;
        state = state with { Relics = state.Relics.SetItem(wingIndex, state.Relics[wingIndex] with { RewardsSacrificed = count }) };
        Working = state;
        Accept(new("PaelsWingSacrifice", input.WingRelicId, [input.RewardId], null, null));
        if (gift is { } key)
        {
            _sacrificePendingRewards = _sacrificePendingRewards.Add(input.RewardId);
            _cardSelection = null;
            if (!BeginObtainRelic(key, null, PredictorObtainReturn.RewardAlternative, input.RewardId, token))
                return PredictorInputResult.Accepted;
        }
        ResumeSacrificeCardReward(input.RewardId);
        Working.Validate();
        return PredictorInputResult.Accepted;
    }

}
