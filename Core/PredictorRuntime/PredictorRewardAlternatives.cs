using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record SacrificeCardReward(long RewardId, long WingRelicId) : PredictorInput;
internal sealed record RerollCardReward(long RewardId) : PredictorInput;
internal sealed record PredictorCardRewardAlternative(long RewardId, long RelicId, string Key);

internal sealed partial class PredictorRun
{
    private ImmutableArray<long> _sacrificePendingRewards = [];
    private long? _sacrificePendingRewardId => _sacrificePendingRewards.IsEmpty ? null : _sacrificePendingRewards[^1];

    internal ImmutableArray<PredictorCardRewardAlternative> CardRewardAlternatives =>
        _cardSelection is { } rewardId ? GetCardRewardAlternatives(rewardId) : [];

    internal ImmutableArray<PredictorCardRewardAlternative> GetCardRewardAlternatives(long rewardId)
    {
        int index = OpenReward(rewardId);
        if (index < 0) return [];
        var reward = Rewards[index];
        var alternatives = ImmutableArray.CreateBuilder<PredictorCardRewardAlternative>();
        if (reward.CanSkip) alternatives.Add(new(rewardId, 0, "SKIP"));
        if (reward.CanReroll) alternatives.Add(new(rewardId, 0, "REROLL"));
        foreach (var relic in Working.Relics.Where(r => !r.Melted && r.Key.Entry == "PAELS_WING"))
            alternatives.Add(new(rewardId, relic.Id, "SACRIFICE"));
        if (alternatives.Count > 2)
            throw new InvalidOperationException("More than 2 card reward alternatives are not supported.");
        return alternatives.ToImmutable();
    }

    internal void ResetRewardAlternatives() => _sacrificePendingRewards = [];

    internal PredictorInputResult SubmitRerollCardReward(RerollCardReward input,
        CancellationToken token = default)
    {
        if (Phase != PredictorPhase.Rewards || _cardSelection != input.RewardId ||
            _sacrificePendingRewardId == input.RewardId) return PredictorInputResult.Invalid;
        int index = OpenReward(input.RewardId);
        if (index < 0) return PredictorInputResult.Invalid;
        var reward = Rewards[index];
        if (reward.Kind != PredictorRewardKind.Card || !reward.CardSelectionEntered || !reward.CanReroll)
            return PredictorInputResult.NotAllowed;
        if (reward.RerollRecipe is not { } recipe)
            throw new NotImplementedException("PredictorCardRewardRerollRecipeMissing");
        token.ThrowIfCancellationRequested();
        var state = Working;
        var generated = PredictorRewardGeneration.GenerateCards(Context, ref state, recipe, reward.RerollIsCardReward, out var nicheScope,
            recipe.UseEventRng ? _eventRng ?? throw new InvalidOperationException("PredictorEventRewardRngMissing") : null);
        var cards = ImmutableArray.CreateBuilder<PredictorCard>();
        foreach (var card in generated)
        {
            PredictorSettlementEffects.RequireImplementedCard(card, context: Context);
            cards.Add(card with { Id = state.NextInstanceId, Removed = false });
            state = state with { NextInstanceId = checked(state.NextInstanceId + 1) };
        }
        Working = state;
        Rewards = Rewards.SetItem(index, reward with { Cards = cards.ToImmutable(), CanReroll = false,
            HasRerolled = true, CardRewardNicheScope = nicheScope });
        // Reroll logs each prior option as unpicked, then Populate refreshes the
        // same selection screen with newly generated card instances.
        Accept(new("RerollCardReward", input.RewardId,
            reward.Cards.Select(c => c.Id).ToImmutableArray(), null, null));
        return PredictorInputResult.Accepted;
    }

    internal PredictorInputResult SubmitSacrificeCardReward(SacrificeCardReward input,
        CancellationToken token = default)
    {
        if (Phase != PredictorPhase.Rewards || _cardSelection != input.RewardId ||
            _sacrificePendingRewardId == input.RewardId) return PredictorInputResult.Invalid;
        int rewardIndex = OpenReward(input.RewardId);
        if (rewardIndex < 0 || Rewards[rewardIndex].Kind != PredictorRewardKind.Card ||
            !Rewards[rewardIndex].CardSelectionEntered) return PredictorInputResult.Invalid;
        int wingIndex = Working.Relics.FindIndex(r => r.Id == input.WingRelicId && !r.Melted &&
            r.Key.Entry == "PAELS_WING");
        if (wingIndex < 0) return PredictorInputResult.Invalid;
        var wing = Working.Relics[wingIndex];
        int count = checked(wing.RewardsSacrificed + 1);
        var state = Working;
        ModelKey? pulled = null;
        if (count % 2 == 0)
        {
            pulled = PredictorRewardGeneration.PullRelic(Context, ref state);
            PredictorSettlementEffects.RequireImplementedRelic(pulled.Value, context: Context);
        }
        state = state with { Relics = state.Relics.SetItem(wingIndex,
            state.Relics[wingIndex] with { RewardsSacrificed = count }) };
        Working = state;
        Accept(new("PaelsWingSacrifice", input.WingRelicId, [input.RewardId], null, null));
        if (pulled is not { } key)
        {
            ResumeSacrificeCardReward(input.RewardId);
            return PredictorInputResult.Accepted;
        }
        _sacrificePendingRewards = _sacrificePendingRewards.Add(input.RewardId);
        // The source picker is suspended while the acquired relic owns its
        // child interaction. Child card rewards may open their own picker.
        _cardSelection = null;
        if (BeginObtainRelic(key, null, PredictorObtainReturn.RewardAlternative,
            input.RewardId, token)) ResumeSacrificeCardReward(input.RewardId);
        return PredictorInputResult.Accepted;
    }

    internal void ResumeSacrificeCardReward(long rewardId)
    {
        if (_sacrificePendingRewards.Contains(rewardId) && _sacrificePendingRewardId != rewardId)
            throw new InvalidOperationException("PredictorSacrificeRewardContinuationInvalid");
        if (_cardSelection != null && _cardSelection != rewardId || OpenReward(rewardId) < 0)
            throw new InvalidOperationException("PredictorSacrificeRewardMissing");
        if (!_sacrificePendingRewards.IsEmpty && _sacrificePendingRewards[^1] == rewardId)
            _sacrificePendingRewards = _sacrificePendingRewards.RemoveAt(_sacrificePendingRewards.Length - 1);
        MarkTaken(rewardId);
        _cardSelection = null;
        Accept(new("PaelsWingSacrificeComplete", rewardId, [], null, null));
    }
}
