using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.Rewards;

/// <summary>
/// Shared eligibility and counter facts for card-reward listeners. Consumers own
/// card instances, cloning, enchantment metadata and the ordered after-hook commit.
/// </summary>
public static class CardRewardRelicRules
{
    public const int CandyPowerCardCount = 1;
    public const int SilkenTressRewardCount = 1;
    public const int SilverCrucibleRewardLimit = 3;

    public static EffectCardType? EggCardType(string relicEntry) => relicEntry switch
    {
        "MOLTEN_EGG" => EffectCardType.Attack,
        "TOXIC_EGG" => EffectCardType.Skill,
        "FROZEN_EGG" => EffectCardType.Power,
        _ => null
    };

    public static bool EggCanUpgrade(EffectCardType cardType, bool canUpgrade, string relicEntry) =>
        canUpgrade && EggCardType(relicEntry) == cardType;

    public static bool SilkenTressApplies(bool isCardReward, bool isUsed) => isCardReward && !isUsed;

    public static bool SilverCrucibleApplies(bool isCardReward, int timesUsed, int limit = SilverCrucibleRewardLimit) =>
        isCardReward && timesUsed < limit;

    public static int SilverCrucibleUsesAfterModification(int timesUsed, int limit = SilverCrucibleRewardLimit) =>
        timesUsed < limit ? timesUsed + 1 : timesUsed;

    public static bool CandyRequiresIsFromCombat(LastingCandyCounterPolicy policy) =>
        policy == LastingCandyCounterPolicy.RewardOfferingOdd;

    public static bool CandyCounterIsTriggering(LastingCandyCounterPolicy policy, int counter) =>
        counter > 0 && (policy == LastingCandyCounterPolicy.RewardOfferingOdd ? counter % 2 == 1 : counter % 2 == 0);

    public static int CandyExtraCardCount(LastingCandyCounterPolicy policy, int counter,
        bool isCardReward, bool encounterSource, bool isFromCombat)
    {
        return isCardReward && encounterSource && CandyCounterIsTriggering(policy, counter) &&
            (!CandyRequiresIsFromCombat(policy) || isFromCombat) ? CandyPowerCardCount : 0;
    }

    public static int CandyCounterBeforeReward(LastingCandyCounterPolicy policy, int counter) =>
        policy == LastingCandyCounterPolicy.CombatEndEven ? counter + 1 : counter;

    public static int CandyCounterBeforeOffering(LastingCandyCounterPolicy policy, int counter, bool hasCardReward) =>
        policy == LastingCandyCounterPolicy.RewardOfferingOdd && hasCardReward ? counter + 1 : counter;
}
