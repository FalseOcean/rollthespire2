using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.Rewards;

public readonly record struct CardRarityEvaluation(
    EffectCardRarity Rarity, float RareThreshold, float UncommonThreshold);

/// <summary>
/// CardRarityOdds.RollWithoutChangingFutureOdds and Roll's future-offset update.
/// The consumer owns the RNG draw and decides whether its source reads or changes
/// the offset. Boss encounters evaluate at zero offset but still update future odds.
/// </summary>
public static class CardRarityRules
{
    public const float InitialOffset = -0.05f;

    public static CardRarityEvaluation EvaluateWithOffset(
        float roll, CardBaseOddsType type, int ascension, float offset)
    {
        CardBaseOddsThresholds odds = CardBaseOddsPolicyEvaluator.GetBaseOdds(type, ascension);
        float rare = odds.Rare + offset;
        float uncommon = rare + odds.Uncommon;
        return new(roll < rare ? EffectCardRarity.Rare :
            roll < uncommon ? EffectCardRarity.Uncommon : EffectCardRarity.Common,
            rare, uncommon);
    }

    public static float AdvanceOffset(float currentOffset, EffectCardRarity rolledRarity, int ascension) =>
        rolledRarity == EffectCardRarity.Rare
            ? InitialOffset
            : Math.Min(0.4f, currentOffset + (ascension >= 7 ? 0.005f : 0.01f));
}
