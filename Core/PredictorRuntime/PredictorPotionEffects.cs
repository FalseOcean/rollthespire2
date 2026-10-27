using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorPotionAttempt(long SourceId, string Stage, PredictorPotion Potion, int Slot, PredictorInputResult Result)
{
    public int? TemporarySourceIndex { get; init; }
}
internal sealed record PredictorPotionHealthEffect(long PotionId, decimal HealMaxHpFraction, int MaxHpDelta);

internal static class PredictorPotionEffects
{
    internal static (PredictorState State, PredictorPotionAttempt Attempt) Procure(PredictorState state, ModelKey key,
        long sourceId, string stage, int slot = -1)
    {
        var potion = new PredictorPotion(state.NextInstanceId, key);
        state = state with { NextInstanceId = checked(state.NextInstanceId + 1) };
        int target = slot < 0 ? state.PotionSlots.FindIndex(p => p == null) : slot;
        if (PredictorSettlementEffects.Has(state, "SOZU")) return (state, new(sourceId, stage, potion, target, PredictorInputResult.NotAllowed));
        if (target < 0 || target >= state.PotionSlots.Length || state.PotionSlots[target] != null)
            return (state, new(sourceId, stage, potion, target, PredictorInputResult.TooFull));
        state = state with { PotionSlots = state.PotionSlots.SetItem(target, potion) };
        return (state, new(sourceId, stage, potion, target, PredictorInputResult.Accepted));
    }

    internal static (PredictorState State, ImmutableArray<PredictorPotionAttempt> Attempts) BeforeCombat(
        PredictorContext context, PredictorState state, CancellationToken token)
    {
        var attempts = ImmutableArray.CreateBuilder<PredictorPotionAttempt>();
        // Complete normal entry callbacks before the late callback, irrespective
        // of whether the toad's instance occurs earlier in the inventory.
        foreach (var relic in state.Relics.Where(r => !r.Melted && r.Key.Entry == "DELICATE_FROND"))
            while (state.PotionSlots.Any(p => p == null))
            {
                token.ThrowIfCancellationRequested();
                var key = PredictorRewardGeneration.GeneratePotions(context, ref state, 1, PredictorStream.CombatPotionGeneration)[0];
                var result = Procure(state, key, relic.Id, "BeforeCombatStart");
                state = result.State;
                attempts.Add(result.Attempt);
                if (result.Attempt.Result != PredictorInputResult.Accepted) break;
            }
        foreach (var relic in state.Relics.Where(r => !r.Melted && r.Key.Entry == "PETRIFIED_TOAD"))
        {
            token.ThrowIfCancellationRequested();
            var result = Procure(state, new("POTION", "POTION_SHAPED_ROCK"), relic.Id, "BeforeCombatStartLate");
            state = result.State;
            attempts.Add(result.Attempt);
        }
        return (state, attempts.ToImmutable());
    }

    internal static (PredictorState State, ImmutableArray<PredictorPotionAttempt> Attempts) Coffer(PredictorContext context,
        PredictorState state, long sourceId)
    {
        int originalCapacity = state.PotionSlots.Length;
        state = state with { PotionSlots = state.PotionSlots.AddRange(new PredictorPotion?[4]) };
        var keys = PredictorRewardGeneration.GeneratePotions(context, ref state, 4, PredictorStream.CombatPotionGeneration);
        var attempts = ImmutableArray.CreateBuilder<PredictorPotionAttempt>();
        for (int i = 0; i < keys.Length; i++)
        {
            var result = Procure(state, keys[i], sourceId, "AfterObtained", originalCapacity + i);
            state = result.State;
            attempts.Add(result.Attempt);
        }
        return (state, attempts.ToImmutable());
    }
}
