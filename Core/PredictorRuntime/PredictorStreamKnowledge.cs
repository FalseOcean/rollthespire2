namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // One mapping for captured states, pending reads and ordinary DTO masks.
    // No valid stream may silently become an unrelated None domain.
    internal static PredictorCombatDomain StreamKnowledgeDomain(PredictorStream stream) => stream switch
    {
        PredictorStream.UpFront => PredictorCombatDomain.UpFrontRng,
        PredictorStream.Rewards => PredictorCombatDomain.RewardsRng,
        PredictorStream.Transformations => PredictorCombatDomain.TransformationsRng,
        PredictorStream.Shops => PredictorCombatDomain.ShopsRng,
        PredictorStream.Niche => PredictorCombatDomain.NicheRng,
        PredictorStream.CombatPotionGeneration => PredictorCombatDomain.CombatPotionRng,
        PredictorStream.UnknownMapPoint => PredictorCombatDomain.UnknownMapPointRng,
        PredictorStream.TreasureRoomRelics => PredictorCombatDomain.TreasureRng,
        _ => throw new ArgumentOutOfRangeException(nameof(stream))
    };
}
