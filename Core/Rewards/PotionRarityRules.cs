using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.Rewards;

/// <summary>PotionFactory rarity selection. Both boundaries are inclusive.</summary>
public static class PotionRarityRules
{
    public static EffectPotionRarity Evaluate(float roll) => roll switch
    {
        <= 0.1f => EffectPotionRarity.Rare,
        <= 0.35f => EffectPotionRarity.Uncommon,
        _ => EffectPotionRarity.Common
    };
}
