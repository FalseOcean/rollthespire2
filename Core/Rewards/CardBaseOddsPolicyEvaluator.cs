using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Rewards;

/// <summary>CardRarityOdds.RollWithBaseOdds odds type.</summary>
public enum CardBaseOddsType
{
    Regular,
    Elite,
    Shop,
    Uniform
}

public readonly record struct CardBaseOddsThresholds(float Rare, float Uncommon);

/// <summary>
/// Pure profile-specific replay of CardRarityOdds.RollWithBaseOdds. The caller
/// consumes exactly one RNG float; threshold evaluation itself is deterministic.
/// This is not the stateful encounter-reward pity algorithm.
/// </summary>
public static class CardBaseOddsPolicyEvaluator
{
    public static EffectCardRarity Roll(
        Xoshiro256StarStar rng,
        CardBaseOddsType type,
        int ascension,
        CardBaseOddsPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(rng);
        float roll = rng.NextFloat();
        return Evaluate(roll, type, ascension, policy);
    }

    public static EffectCardRarity Evaluate(
        float roll,
        CardBaseOddsType type,
        int ascension,
        CardBaseOddsPolicy policy)
    {
        if (roll < 0f || roll >= 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(roll));
        }

        CardBaseOddsThresholds odds = GetBaseOdds(type, ascension);
        if (roll < odds.Rare)
        {
            return EffectCardRarity.Rare;
        }

        float uncommonUpperBound = policy switch
        {
            CardBaseOddsPolicy.LegacyAndBeta109NonCumulative => odds.Uncommon,
            CardBaseOddsPolicy.Beta110Cumulative => odds.Rare + odds.Uncommon,
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
        return roll < uncommonUpperBound
            ? EffectCardRarity.Uncommon
            : EffectCardRarity.Common;
    }

    public static CardBaseOddsThresholds GetBaseOdds(CardBaseOddsType type, int ascension)
    {
        bool scarcity = ascension >= 7;
        return type switch
        {
            // Preserve the accepted rarity-parameter mapping already used by the
            // stateful normal-combat predictor: A0-A6 3%, A7+ 1.49%.
            CardBaseOddsType.Regular => new CardBaseOddsThresholds(
                scarcity ? 0.0149f : 0.03f,
                0.37f),
            CardBaseOddsType.Elite => new CardBaseOddsThresholds(
                scarcity ? 0.10f : 0.05f,
                0.40f),
            CardBaseOddsType.Shop => new CardBaseOddsThresholds(
                scarcity ? 0.09f : 0.045f,
                0.37f),
            CardBaseOddsType.Uniform => new CardBaseOddsThresholds(0.33f, 0.33f),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
