namespace RolltheSpire2.Core.Rewards;

public enum CombatRewardRoomKind { Other, Monster, Elite, Boss }

/// <summary>A reward addition recipe, before population or instance allocation.</summary>
public readonly record struct AddedCardReward(
    int RewardCount, int CardCount, CardBaseOddsType Odds, bool EncounterSource, bool IsFromCombat);

public readonly record struct LavaRockReward(int RelicCount, bool HasTriggeredAfterModification);
public readonly record struct WongoReward(int RelicCount, bool GaveRelicAfterModifyingRewards);

/// <summary>
/// Vanilla reward-list triggers. Callers retain inventory order, creation and
/// population phases. ForRoom selects encounter odds; it does not set IsFromCombat.
/// </summary>
public static class CombatRewardRelicRules
{
    public const int DefaultCardOptionCount = 3;

    public static AddedCardReward PrayerWheel(CombatRewardRoomKind room) =>
        room == CombatRewardRoomKind.Monster
            ? new(1, DefaultCardOptionCount, CardBaseOddsType.Regular, true, false)
            : default;

    public static AddedCardReward WhiteStar(CombatRewardRoomKind room) =>
        room == CombatRewardRoomKind.Elite
            ? new(1, DefaultCardOptionCount, CardBaseOddsType.Boss, true, false)
            : default;

    public static int BlackStarRelics(CombatRewardRoomKind room) => room == CombatRewardRoomKind.Elite ? 1 : 0;

    // Native LavaRock commits HasTriggered in TryModifyRewards, before the
    // additions populate. This is deliberately distinct from Wongo's after hook.
    public static LavaRockReward LavaRock(CombatRewardRoomKind room, int act, bool hasTriggered) =>
        room == CombatRewardRoomKind.Boss && act == 1 && !hasTriggered
            ? new(2, true)
            : new(0, hasTriggered);

    public static int AmethystGold(CombatRewardRoomKind room, bool finalAct) =>
        room == CombatRewardRoomKind.Other || room == CombatRewardRoomKind.Boss && finalAct ? 0 : 15;

    // The counter advances at combat end. Successful TryModifyRewards only
    // schedules this state change; AfterModifyingRewards commits it later.
    public static WongoReward Wongo(CombatRewardRoomKind room, int combatsFinished, bool gaveRelic) =>
        room != CombatRewardRoomKind.Other && combatsFinished >= 5 && !gaveRelic
            ? new(3, true)
            : new(0, gaveRelic);
}
