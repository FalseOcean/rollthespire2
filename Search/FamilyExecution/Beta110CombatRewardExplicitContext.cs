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
        SearchQuery query = compiled.NormalizedQuery;
        RuntimeProfileId profileId = compiled.Context.ProfileId;
        var candidates = new List<ModelKey>();

        // Reward influence follows legal player-authored opening acquisition positions,
        // not arbitrary mentions of a relic elsewhere in the Query. A direct Neow route
        // may itself be a supported Reward modifier (for example Silken Tress / Silver
        // Crucible). On a Bones route, only explicitly authored Bones grants are acquired
        // sources. This never consults RelicSequenceConditions, so a queue requirement such
        // as "Rare #1 = Prayer Wheel" cannot leak into C.
        if (query.OpeningRoute is { IsValid: true } route)
        {
            if (route.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones)
            {
                candidates.Add(route.RouteRelicKey);
            }
            else
            {
                if (query.OpeningRouteRelicRequirement is { IsEmpty: false } routeRelics &&
                    routeRelics.ParentRouteRelicKey == BaseGameModelKeys.Relics.NeowsBones)
                {
                    candidates.AddRange(routeRelics.RequiredRelicKeys);
                }

                candidates.AddRange(query.LegacyNeow.RequiredBonesAcquisitionOrder);
                candidates.AddRange(query.LegacyNeow.RequiredBonesCombination);

                // A structured effect authored under Bones necessarily names the child
                // relic whose effect is being requested, so that source is also an explicit
                // grant even when the separate route-relic requirement is absent.
                candidates.AddRange(query.StructuredOpeningEffects
                    .Where(condition => !condition.IsEmpty &&
                                        condition.SourceRelicKey.IsValid &&
                                        condition.SourceRelicKey != BaseGameModelKeys.Relics.NeowsBones)
                    .Select(condition => condition.SourceRelicKey));
            }
        }

        // Capsule nested Reward modifiers are a deeper authored acquisition position.
        // Prayer Wheel / Lasting Candy enter C only through this explicit nested output;
        // an unrelated relic-sequence condition remains invisible here.
        candidates.AddRange(query.StructuredOpeningEffects
            .Where(condition => !condition.IsEmpty &&
                                (IsCapsule(condition.SourceRelicKey) ||
                                 (condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
                                  condition.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset)) &&
                                condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                                condition.OutputKind == NeowStructuredOutputKind.Relic)
            .SelectMany(condition => condition.OutputKeys));

        LegacyNeowSemanticConstraints legacy = query.LegacyNeow;
        if (HasExplicitCapsuleSource(query))
        {
            // Legacy CapsuleContainedRelics is a single normalized Capsule-output
            // surface. All and singleton Any are deterministic authored nested outcomes;
            // multi-Any remains alternatives and must not be guessed into a context.
            AddGuaranteedKeys(candidates, legacy.CapsuleContainedRelics);
        }

        ModelKey[] sources = candidates
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .Where(key => OpeningCombatRewardImpactAdapterRegistry.TryResolve(profileId, key, out _))
            .OrderBy(key => key.Serialized, StringComparer.Ordinal)
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

    private static bool HasExplicitCapsuleSource(SearchQuery query)
    {
        if (query.OpeningRoute is { IsValid: true } route && IsCapsule(route.RouteRelicKey))
            return true;

        if (query.OpeningRouteRelicRequirement is { IsEmpty: false } routeRelics &&
            routeRelics.RequiredRelicKeys.Any(IsCapsule))
            return true;

        LegacyNeowSemanticConstraints legacy = query.LegacyNeow;
        if (legacy.RequiredBonesCombination.Any(IsCapsule) ||
            legacy.RequiredBonesAcquisitionOrder.Any(IsCapsule))
            return true;

        return query.StructuredOpeningEffects.Any(condition =>
            !condition.IsEmpty && IsCapsule(condition.SourceRelicKey));
    }

    private static bool IsCapsule(ModelKey key) =>
        key == BaseGameModelKeys.Relics.SmallCapsule || key == BaseGameModelKeys.Relics.LargeCapsule;

    private static void AddGuaranteedKeys(ICollection<ModelKey> target, ModelKeySetFilter filter)
    {
        foreach (ModelKey key in filter.All)
            target.Add(key);
        if (filter.Any.Count == 1)
            target.Add(filter.Any[0]);
    }
}
