using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NeowCapsuleRarityGate
{
    internal const string Revision = ".SmallCapsuleRare.20261114.v2";
    internal const int GpuMetadataIndex = 29;

    internal static bool CanApply(SearchModel request, NeowReplayPlan plan)
    {
        var authority = request.Authority;
        if (!plan.AuditedModern113IdentityReplay || !authority.IsVanilla ||
            authority.UsesBestEffortModel || authority.NoRunModifiers != true ||
            authority.PlayersCount != 1 || authority.PlayerSlotIndex != 0 ||
            authority.WorldAuthority is not { SourceAuthority: SourceAuthority.OfficialRuntimeExact or SourceAuthority.AuditedStaticExact } ||
            authority.EffectAuthority is not { HasExactRelicBagSourcePools: true } effects)
            return false;

        var targets = request.Evaluation.StructuredNeowEffects.Where(condition =>
            NeowReplayPlan.IsCapsule(condition) &&
            condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule &&
            condition.Kind == NeowStructuredConditionKind.ExactSingle && condition.OutputKeys.Count == 1)
            .Select(condition => condition.OutputKeys[0]).ToArray();
        if (targets.Length == 0) return false;

        const byte small = Beta110FastRelicCatalog.SmallCapsule;
        const byte leafy = Beta110FastRelicCatalog.LeafyPoultice;
        ulong fixedPair = Beta110FastRelicCatalog.Bit(small) | Beta110FastRelicCatalog.Bit(leafy);
        // In this pair Bones uses NonCombatGeneration and Leafy uses Transformations
        // without pulling from the relic bag. Either pickup order reaches the same first Rewards draw.
        if (plan.Bones
            ? plan.BonesAll != fixedPair && (plan.First, plan.Second) is not ((small, leafy) or (leafy, small))
            : plan.Selected != small)
            return false;

        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var denseByKey, out var lanes, out _) ||
            pool.RngSemantics != RngSemanticProfileId.Modern113)
            return false;

        // Check membership against both the compiled personal lanes and the exact
        // model's immutable sources. Lasting Candy is the only vanilla opening
        // bag exclusion. Exclude it from the lower bound regardless of account state.
        var personalSource = effects.SharedRelicPoolSource!.Concat(effects.CharacterRelicPoolSource!)
            .Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or
                EffectRelicRarity.Rare or EffectRelicRarity.Shop)
            .DistinctBy(relic => relic.RelicKey, ModelKeyComparer.Instance)
            .ToDictionary(relic => relic.RelicKey, ModelKeyComparer.Instance);
        // Only the first pull's rarity is observed, before any pickup hook.
        // Every required native Rare identity supplies this necessary condition;
        // its later effects and exact identity remain owned by ordinary replay.
        return targets.Any(key => EligibleInLane(key, EffectRelicRarity.Rare, 2)) &&
            HasEligibleRelic(EffectRelicRarity.Common, 0) &&
            HasEligibleRelic(EffectRelicRarity.Uncommon, 1);

        bool HasEligibleRelic(EffectRelicRarity rarity, int lane) =>
            personalSource.Keys.Any(key => EligibleInLane(key, rarity, lane));

        bool EligibleInLane(ModelKey key, EffectRelicRarity rarity, int lane) =>
            key != StandardOpeningRelicEligibility.LastingCandy &&
            personalSource.TryGetValue(key, out var relic) && relic.Rarity == rarity &&
            string.IsNullOrWhiteSpace(relic.SourceModId) &&
            string.Equals(relic.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) &&
            denseByKey.TryGetValue(key, out ushort id) && lanes[lane].ContainsKey(id);
    }

    internal static bool Passes(Beta110FastRng rewards) => rewards.NextFloat() >= 0.83f;

    internal static Func<ulong, bool> Refine(NeowReplayPlan plan, Func<ulong, bool> matcher) =>
        plan.HasCapsuleRarityGate
            ? root => matcher(root) && Passes(new Beta110FastRng(unchecked(root + NeowFamilyReplay.RewardsHash)))
            : matcher;
}
