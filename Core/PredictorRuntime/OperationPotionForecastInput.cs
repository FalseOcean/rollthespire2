using System.Collections.Immutable;
namespace RolltheSpire2.Core.PredictorRuntime;

// A reward is an offered native bottle, never a current inventory slot.
internal sealed record PredictorOperationPotionSource(PredictorPotion Potion, int? HeldSlot = null, long? RewardId = null);
internal sealed record PredictorOperationPotionObservation(PredictorContext Context, PredictorOperationPotionSource Source,
    ImmutableArray<PredictorPotion?> Inventory, PredictorStreamState PotionGenerationRng,
    bool InCombat, bool HasSozu, string Stamp);
