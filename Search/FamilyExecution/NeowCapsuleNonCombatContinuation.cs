using System.Numerics;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static class NeowCapsuleNonCombatContinuation
{
    internal const string Revision = ".CapsuleZeroNonCombat.20261113.v1";
    internal const string AlternativesRevision = ".CapsuleNonCombatAlternatives.20261114.v1";
    internal const string Remainder = "CapsuleNonCombatContinuation";
    internal const int GpuMetadataIndex = 57;
    internal const int GpuSmallAdvancesIndex = 0;
    internal const int GpuLargeAdvancesIndex = 1;
    internal const int GpuDualAdvancesIndex = 2;
    internal const byte Small = Beta110FastRelicCatalog.SmallCapsule;
    internal const byte Large = Beta110FastRelicCatalog.LargeCapsule;
    internal static readonly int[] Zero = [0];
    private static readonly int[][] AdvanceValues =
        [[-1], Zero, [1], [0, 1], [2], [0, 2], [1, 2], [0, 1, 2]];

    internal static (uint Small, uint Large, uint Dual) CompileSoloAlternatives(SearchModel request)
    {
        var authority = request.Authority;
        if (authority.PlayersCount != 1 || authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            !authority.IsVanilla || authority.NoRunModifiers != true || authority.UsesBestEffortModel ||
            authority.WorldAuthority?.Beta109Generation?.NoUnknownHooksOrModifiers != true ||
            authority.EffectAuthority is not { HasExactRelicBagSourcePools: true } effects)
            return (0, 0, 0);

        var catalog = effects.SharedRelicPoolSource!.Concat(effects.CharacterRelicPoolSource!)
            .Where(relic => relic.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare)
            .DistinctBy(relic => relic.RelicKey, ModelKeyComparer.Instance)
            .ToDictionary(relic => relic.RelicKey, ModelKeyComparer.Instance);
        int maximumPositive = catalog.Values.Count(relic => NativeAdvance(relic) == 1);
        // Each actual bag entry can be taken only once. Unknown hooks cannot
        // bound even one pickup, so only full source bindings work in that case.
        bool completePool = catalog.Values.All(relic => NativeAdvance(relic) >= 0);
        ModelKey[] Bound(byte source) => request.Evaluation.StructuredNeowEffects
            .Where(condition => !NeowReplayPlan.IsGroupedCapsule(condition) &&
                condition.SourceRelicKey == Beta110FastRelicCatalog.KeyOf(source) &&
                condition.Scope == NeowStructuredEffectScope.NestedRelics &&
                condition.OutputKind == NeowStructuredOutputKind.Relic &&
                condition.Kind is NeowStructuredConditionKind.ExactSingle or NeowStructuredConditionKind.ExactUnorderedPair)
            .SelectMany(condition => condition.OutputKeys).Distinct(ModelKeyComparer.Instance).ToArray();
        var smallKeys = Bound(Small);
        var largeKeys = Bound(Large);
        var groupedKeys = request.Evaluation.StructuredNeowEffects.Where(NeowReplayPlan.IsGroupedCapsule)
            .SelectMany(condition => condition.OutputKeys);
        return (Compile(smallKeys, 1), Compile(largeKeys, 2),
            Compile(smallKeys.Concat(largeKeys).Concat(groupedKeys).Distinct(ModelKeyComparer.Instance), 3));

        uint Compile(IEnumerable<ModelKey> keys, int capacity)
        {
            int known = 0, positive = 0;
            foreach (var key in keys)
            {
                int advance = catalog.TryGetValue(key, out var relic) ? NativeAdvance(relic) : -1;
                if (advance < 0) continue;
                known++;
                positive += advance;
            }
            if (known > capacity || !completePool && known != capacity) return 0;
            int maximum = Math.Min(maximumPositive, positive + capacity - known);
            uint mask = 0;
            for (int advance = positive; advance <= maximum; advance++) mask |= 1u << advance;
            return mask;
        }
    }

    internal static int NativeAdvance(NeowEffectRelicSnapshot relic)
    {
        if (!relic.NestedClassificationExact ||
            !string.Equals(relic.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(relic.SourceModId)) return -1;
        if (relic.RelicKey == BaseGameModelKeys.OrdinaryRelics.Whetstone &&
            relic.NestedEffectKind == NestedRelicEffectKind.Whetstone ||
            relic.RelicKey == BaseGameModelKeys.OrdinaryRelics.WarPaint &&
            relic.NestedEffectKind == NestedRelicEffectKind.WarPaint)
            return 1; // StableShuffle requests one child seed even with no upgradeable cards.
        if (relic.NestedEffectKind == NestedRelicEffectKind.NoTrackedImmediateEffect ||
            relic.RelicKey.Entry is "MANGO" or "OLD_COIN" or "PEAR" or "POTION_BELT" or "STRAWBERRY")
            return 0; // These native pickup overrides affect only HP, gold or potion slots.
        return -1;
    }

    internal static uint AdvanceMask(NeowReplayPlan plan, byte source)
    {
        uint mask = source switch
        {
            Small => plan.SmallCapsuleNonCombatAdvances,
            Large => plan.LargeCapsuleNonCombatAdvances,
            _ => 1
        };
        if (mask == 0 && (plan.CapsulesWithKnownZeroNonCombatAdvance & Beta110FastRelicCatalog.Bit(source)) != 0)
            return 1;
        return mask;
    }

    internal static int[] Advances(uint mask) => AdvanceValues[mask];
    internal static int[] Advances(NeowReplayPlan plan, byte source) => Advances(AdvanceMask(plan, source));

    internal static bool IsDual(byte first, byte second) =>
        first == Small && second == Large || first == Large && second == Small;

    internal static bool ReadsAfterFirst(NeowReplayPlan plan, byte second) =>
        plan.HasFinalCurseFastProjection ||
        (second is Beta110FastRelicCatalog.Kaleidoscope or Beta110FastRelicCatalog.PhialHolster &&
            plan.StructuredConditions.Any(condition => condition.SourceRelicId == second));

    internal static uint Compile(SearchModel request)
    {
        var authority = request.Authority;
        // Match the current-root bag prerequisites of the opening effect replay.
        // A runtime bag captured for a different root cannot establish this premise.
        if (authority.EffectAuthority is not { HasExactRelicBagSourcePools: true } ||
            (authority.PlayersCount > 1 &&
             (authority.WorldAuthority?.Beta109Generation is not { HasExactPartyRelicBags: true } party ||
              party.PersonalPlayerSlot != authority.PlayerSlotIndex))) return 0;

        uint sources = 0;
        foreach (var condition in request.Evaluation.StructuredNeowEffects)
        {
            if (!NeowReplayPlan.IsCapsule(condition) || NeowReplayPlan.IsGroupedCapsule(condition)) continue;
            bool small = condition.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule;
            int capacity = small ? 1 : 2;
            var kind = small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair;
            // Every Exact match must obtain all these relics from this Capsule.
            // Only the downstream RNG is projected here. Exact still checks the bag.
            if (condition.Kind == kind && condition.OutputKeys.Count == capacity &&
                condition.OutputKeys.Distinct().Count() == capacity &&
                condition.OutputKeys.All(key => HasZeroAdvance(authority, key)))
                sources |= (uint)Beta110FastRelicCatalog.Bit(small ? Beta110FastRelicCatalog.SmallCapsule : Beta110FastRelicCatalog.LargeCapsule);
        }
        return sources;
    }

    internal static bool HasZeroAdvance(RuntimeContextAuthoritySnapshot authority, ModelKey key)
    {
        if (!authority.IsVanilla || authority.NoRunModifiers != true || authority.EffectAuthority is not { } effects)
            return false;
        var snapshots = (effects.OrderedRelicBag ?? []).Concat(effects.SharedRelicPoolSource ?? [])
            .Concat(effects.CharacterRelicPoolSource ?? []).Where(relic => relic.RelicKey == key).ToArray();
        return snapshots.Length != 0 && snapshots.All(relic => relic.NestedClassificationExact &&
            (relic.NestedEffectKind == NestedRelicEffectKind.NoTrackedImmediateEffect ||
             // In 0.113 the official Old Coin pickup hook only grants gold.
             key == BaseGameModelKeys.OrdinaryRelics.OldCoin &&
             string.Equals(relic.SourceAssembly, "sts2", StringComparison.OrdinalIgnoreCase)));
    }

    internal static bool IsUnknown(NeowReplayPlan plan, byte source) =>
        plan.Authority.UsesChildShuffle &&
        AdvanceMask(plan, source) == 0;

    internal static bool NeedsRemainder(NeowReplayPlan plan)
    {
        var domains = plan.EnabledDomains;
        bool finalCurse = plan.HasFinalCurseFastProjection;
        bool kaleidoscope = (domains & Beta110FastDomain.KaleidoscopeOffers) != 0;
        bool phial = (domains & Beta110FastDomain.PhialHolsterPotions) != 0;
        if (!finalCurse && !kaleidoscope && !phial) return false;

        foreach (byte first in plan.Authority.BonesEligibleRelicIds)
        foreach (byte second in plan.Authority.BonesEligibleRelicIds)
        {
            if (first == second || (plan.First != 255 && plan.Second != 255 &&
                (first != plan.First || second != plan.Second))) continue;
            ulong pair = Beta110FastRelicCatalog.Bit(first) | Beta110FastRelicCatalog.Bit(second);
            if ((pair & plan.BonesAll) != plan.BonesAll || (pair & plan.BonesBan) != 0 ||
                (plan.BonesAny != 0 && (pair & plan.BonesAny) == 0)) continue;

            bool NeedsObservation(byte source) => BitOperations.PopCount(AdvanceMask(plan, source)) != 1;
            if (finalCurse && (IsDual(first, second) && plan.DualCapsuleNonCombatAdvances != 0
                    ? BitOperations.PopCount(plan.DualCapsuleNonCombatAdvances) != 1
                    : NeedsObservation(first) || NeedsObservation(second))) return true;
            if (NeedsObservation(first) && (kaleidoscope && second == Beta110FastRelicCatalog.Kaleidoscope ||
                phial && second == Beta110FastRelicCatalog.PhialHolster)) return true;
        }
        return false;
    }
}
