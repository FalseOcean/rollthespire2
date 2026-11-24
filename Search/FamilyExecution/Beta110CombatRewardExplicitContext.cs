using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Fast/Search-only projection of Reward-affecting relics acquired at legal opening
/// positions that the player explicitly authored: the selected direct Neow relic,
/// explicit Bones grants, and explicit Capsule nested outputs. Naming a relic in an
/// unrelated Relic-sequence condition is never enough to make it a Combat Reward modifier.
/// Latent Capsule/Bones outcomes remain excluded; Production Exact and SameRoute Witness
/// continue to reconstruct the real route.
/// </summary>
internal sealed record Beta110CombatRewardExplicitContext(
    ModelKey[] SourceRelicKeys,
    Beta110CombatRewardInfluenceFlags InfluenceFlags,
    byte AdditionalCardRewardCount,
    short FixedGoldAmount,
    string Fingerprint)
{
    public static Beta110CombatRewardExplicitContext Empty(RuntimeProfileId profileId) => new(
        Array.Empty<ModelKey>(),
        Beta110CombatRewardInfluenceFlags.None,
        0,
        0,
        OpeningCombatRewardImpactAdapterRegistry.BuildImpactFingerprint(
            profileId,
            Array.Empty<RewardImpactSourceKey>()));

    public bool IsEmpty => SourceRelicKeys.Length == 0;

    /// <summary>
    /// The current Combat Reward Fast observable surface is card identity, potion
    /// drop/identity and gold. Upgrade/enchantment-only modifiers do not alter those
    /// observables and therefore do not invalidate the optimized neutral hot loop.
    /// </summary>
    public bool ChangesCurrentFastObservables =>
        AdditionalCardRewardCount != 0 ||
        FixedGoldAmount != 0 ||
        (InfluenceFlags & (Beta110CombatRewardInfluenceFlags.ForcePotionReward |
                           Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward |
                           Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard |
                           Beta110CombatRewardInfluenceFlags.AmethystAubergineFixedGold |
                           Beta110CombatRewardInfluenceFlags.UnknownRewardImpact |
                           Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation)) != 0;

    public void ApplyTo(ref Beta110OpeningRewardState state)
    {
        state.InfluenceFlags |= InfluenceFlags;
        state.AdditionalCardRewardCount = checked((byte)Math.Min(
            byte.MaxValue,
            state.AdditionalCardRewardCount + AdditionalCardRewardCount));
        state.FixedGoldAmount = checked((short)Math.Clamp(
            state.FixedGoldAmount + FixedGoldAmount,
            0,
            short.MaxValue));
    }
}

internal static class Beta110CombatRewardExplicitContextProjector
{

    public static Beta110CombatRewardExplicitContext Project(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        RuntimeProfileId profileId = compiled.Context.ProfileId;
        // Shared guaranteed acquisitions retain source position semantics. This
        // includes source-ambiguous required Capsule outputs as held facts, but
        // never guesses multi-Any or future relic-queue observations.
        ModelKey[] sources = ExplicitOpeningRelicFacts.EnumerateGuaranteed(compiled.NormalizedQuery)
            .Where(key => OpeningCombatRewardImpactAdapterRegistry.TryResolve(profileId, key, out _))
            .ToArray();

        Beta110CombatRewardInfluenceFlags flags = Beta110CombatRewardInfluenceFlags.None;
        int additionalRewards = 0;
        int fixedGold = 0;
        foreach (ModelKey source in sources)
        {
            if (!OpeningCombatRewardImpactAdapterRegistry.TryResolve(
                    profileId,
                    source,
                    out OpeningCombatRewardImpactAdapterDescriptor descriptor))
            {
                continue;
            }

            foreach (OpeningCombatRewardImpactOperation operation in descriptor.Operations)
            {
                switch (operation.Kind)
                {
                    case OpeningCombatRewardImpactOperationKind.ForcePotionReward:
                        flags |= Beta110CombatRewardInfluenceFlags.ForcePotionReward;
                        break;
                    case OpeningCombatRewardImpactOperationKind.AddCardReward:
                        flags |= Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward;
                        additionalRewards = Math.Min(byte.MaxValue, additionalRewards + Math.Max(0, operation.Amount));
                        break;
                    case OpeningCombatRewardImpactOperationKind.AddFixedGoldReward:
                        flags |= Beta110CombatRewardInfluenceFlags.AmethystAubergineFixedGold;
                        fixedGold = Math.Min(short.MaxValue, fixedGold + Math.Max(0, operation.Amount));
                        break;
                    case OpeningCombatRewardImpactOperationKind.AddPowerCardEveryOtherCombat:
                        flags |= Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard;
                        break;
                    case OpeningCombatRewardImpactOperationKind.ForceUpgradeCardType:
                    case OpeningCombatRewardImpactOperationKind.UpgradeNextCardRewards:
                        flags |= Beta110CombatRewardInfluenceFlags.DeterministicUpgradeOnly;
                        break;
                    case OpeningCombatRewardImpactOperationKind.EnchantFirstCardRewardWithGlam:
                        flags |= Beta110CombatRewardInfluenceFlags.DeterministicEnchantmentOnly;
                        break;
                    default:
                        flags |= Beta110CombatRewardInfluenceFlags.UnknownRewardImpact;
                        break;
                }
            }
        }

        RewardImpactSourceKey[] impactSources = sources
            .Select(key => new RewardImpactSourceKey(RewardImpactSourceKind.Relic, key))
            .ToArray();
        return new Beta110CombatRewardExplicitContext(
            sources,
            flags,
            checked((byte)additionalRewards),
            checked((short)fixedGold),
            OpeningCombatRewardImpactAdapterRegistry.BuildImpactFingerprint(profileId, impactSources));
    }

}
