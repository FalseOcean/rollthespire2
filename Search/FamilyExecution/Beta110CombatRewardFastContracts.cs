using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal enum Beta110CombatRewardRouteProjectionStatus : byte
{
    NotApplicable,
    ExactProjection,
    ConservativeKeep,
    Rejected
}

[Flags]
internal enum Beta110CombatRewardInfluenceFlags : ushort
{
    None = 0,
    ForcePotionReward = 1 << 0,
    PrayerWheelExtraReward = 1 << 1,
    LastingCandyPowerCard = 1 << 2,
    AmethystAubergineFixedGold = 1 << 3,
    DeterministicUpgradeOnly = 1 << 4,
    DeterministicEnchantmentOnly = 1 << 5,
    UnknownRewardImpact = 1 << 6,
    UnknownRewardsContinuation = 1 << 7
}

internal readonly record struct Beta110CombatRewardFastPredicate(
    byte BattleOrdinal,
    ushort[] CardAny,
    ushort[] CardAll,
    ushort[] CardBan,
    NormalCombatPotionRequirement PotionRequirement,
    ushort[] PotionAny,
    ushort[] PotionAll,
    ushort[] PotionBan,
    int MinimumGold,
    int MaximumGold,
    bool HasMinimumGold,
    bool HasMaximumGold)
{
    public bool IsAnyBattle => BattleOrdinal == 0;
    public bool HasCardPredicate => CardAny.Length > 0 || CardAll.Length > 0 || CardBan.Length > 0;
    public bool HasPotionDropPredicate => PotionRequirement != NormalCombatPotionRequirement.Any;
    public bool HasPotionIdentityPredicate => PotionAny.Length > 0 || PotionAll.Length > 0 || PotionBan.Length > 0;
    public bool HasGoldPredicate => HasMinimumGold || HasMaximumGold;
}

internal sealed record Beta110CombatRewardFastPlan(
    bool Enabled,
    CombatRewardFastRoutePolicy RoutePolicy,
    Beta110CombatRewardExplicitContext ExplicitContext,
    Beta110CombatRewardOpeningConsumptionProjection OpeningConsumption,
    string DisableReason,
    Beta110CombatRewardFastPredicate[] Predicates,
    int MaximumBattleOrdinal,
    int CardPredicateCount,
    int PotionDropPredicateCount,
    int PotionIdentityPredicateCount,
    int GoldPredicateCount,
    bool CardPoolAuthorityExact,
    bool PotionPoolAuthorityExact,
    string Fingerprint)
{
    public byte CardAssignmentWindow { get; init; }
    public ushort[] CardAssignmentTargets { get; init; } = [];
    public byte PotionAssignmentWindow { get; init; }
    public byte[] PotionAssignmentRequirements { get; init; } = [];
    public ushort[] PotionAssignmentTargets { get; init; } = [];
    public bool HasDistinctBattleAssignments => CardAssignmentTargets.Length > 0 || PotionAssignmentTargets.Length > 0;
    public int PredicateCount => Predicates.Length;
    public bool UsesSyntheticUnperturbedContinuation =>
        RoutePolicy == CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed;
    public bool RequiresRealOpeningRoute =>
        RoutePolicy == CombatRewardFastRoutePolicy.PinnedRealRoute;

    public static Beta110CombatRewardFastPlan Disabled(string reason, string fingerprint = "") => new(
        false,
        CombatRewardFastRoutePolicy.NotApplicable,
        Beta110CombatRewardExplicitContext.Empty(RuntimeProfileId.Beta110),
        Beta110CombatRewardOpeningConsumptionProjection.Empty,
        reason ?? string.Empty,
        Array.Empty<Beta110CombatRewardFastPredicate>(),
        0, 0, 0, 0, 0,
        false,
        false,
        fingerprint ?? string.Empty);
}

/// <summary>
/// Numeric, route-local bridge from one completed Neow/Bones acquisition route
/// into the first three normal-combat rewards. It intentionally stores only the
/// continuation and reward-domain influence state required by the current Search
/// contract; it is not a Player, Deck, RunState or SeedPredictionDocument.
/// </summary>
internal struct Beta110OpeningRewardState
{
    public uint RouteBit;
    public Beta110FastRng Rewards;
    public float PotionOdds;
    public float CardRarityOffset;
    public Beta110CombatRewardInfluenceFlags InfluenceFlags;
    public byte AdditionalCardRewardCount;
    public byte LastingCandyCounter;
    public short FixedGoldAmount;
    public bool RewardsContinuationAuthorityExact;
    public bool RewardInfluenceAuthorityExact;

    public readonly bool ExactProjectionAuthority =>
        RewardsContinuationAuthorityExact &&
        RewardInfluenceAuthorityExact &&
        (InfluenceFlags & (Beta110CombatRewardInfluenceFlags.UnknownRewardImpact |
                           Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation)) == 0;

    public readonly bool EquivalentTo(in Beta110OpeningRewardState other)
    {
        if (!ExactProjectionAuthority || !other.ExactProjectionAuthority)
            return false;
        UpFrontRngCheckpoint left = Rewards.CaptureCheckpoint();
        UpFrontRngCheckpoint right = other.Rewards.CaptureCheckpoint();
        return left == right &&
               PotionOdds.Equals(other.PotionOdds) &&
               CardRarityOffset.Equals(other.CardRarityOffset) &&
               InfluenceFlags == other.InfluenceFlags &&
               AdditionalCardRewardCount == other.AdditionalCardRewardCount &&
               LastingCandyCounter == other.LastingCandyCounter &&
               FixedGoldAmount == other.FixedGoldAmount;
    }
}
