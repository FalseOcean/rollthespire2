using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Search.FamilyExecution;

// The ordinary N route still checks each source and the rest of the opening.
// This gate uses the same rank function and pools to reject roots before Bones.
internal sealed record NeowModernTransformGate(bool LeafyFirst, bool NewLeafFirst, int LeafyTargets, bool HasNewLeafTarget)
{
    internal const string Revision = ".SharedTransformGate.20261114.v1";
    internal string PhysicalRevision => LeafyTargets == 2 && HasNewLeafTarget ? Revision : ".SharedTransformPartialGate.20261114.v1";

    internal static NeowModernTransformGate? TryCreate(NeowReplayPlan plan, RuntimeContextAuthoritySnapshot authority)
    {
        const byte leafy = Beta110FastRelicCatalog.LeafyPoultice;
        const byte newLeaf = Beta110FastRelicCatalog.NewLeaf;
        if (!HasSharedPools(plan, authority)) return null;
        var leafyConditions = plan.StructuredConditions.Where(c => c.SourceRelicId == leafy &&
            c.Kind == Beta110FastStructuredConditionKind.LeafyPoulticeTransforms && c.TargetCount is 1 or 2).ToArray();
        var newLeafConditions = plan.StructuredConditions.Where(c => c.SourceRelicId == newLeaf &&
            c.Kind == Beta110FastStructuredConditionKind.NewLeafTransform && c.TargetCount == 1).ToArray();
        if (leafyConditions.Length > 1 || newLeafConditions.Length > 1 ||
            leafyConditions.Length + newLeafConditions.Length != plan.StructuredConditions.Length || plan.StructuredConditions.Length == 0)
            return null;

        int leafyTargets = leafyConditions.Length == 0 ? 0 : leafyConditions[0].TargetCount;
        bool hasNewLeafTarget = newLeafConditions.Length == 1;
        if (plan.First == Beta110FastRelicCatalog.InvalidId && plan.Second == Beta110FastRelicCatalog.InvalidId)
            return new(true, true, leafyTargets, hasNewLeafTarget);
        if (plan.First == leafy && plan.Second == newLeaf) return new(true, false, leafyTargets, hasNewLeafTarget);
        if (plan.First == newLeaf && plan.Second == leafy) return new(false, true, leafyTargets, hasNewLeafTarget);
        return null;
    }

    internal static bool HasSharedPools(NeowReplayPlan plan, RuntimeContextAuthoritySnapshot authority)
    {
        ulong pair = Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.LeafyPoultice) |
            Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.NewLeaf);
        if (authority.VersionResolution.SemanticProfileId != RngSemanticProfileId.Modern113 ||
            authority.PlayersCount != 1 || authority.PlayerSlotIndex != 0 || !authority.IsVanilla ||
            authority.NoRunModifiers != true || !authority.IsBeta111NeowIdentityAuthorityExact ||
            !plan.Authority.BonesAuthorityExact || !plan.Bones || plan.BonesAll != pair)
            return false;

        var catalog = plan.Authority.EffectCatalog;
        if (!catalog.LeafyTransformAuthorityExact || !catalog.NewLeafTransformAuthorityExact ||
            catalog.LeafyStrikeTransformPool.Length == 0 || catalog.LeafyDefendTransformPool.Length == 0 ||
            catalog.NewLeafTransformPool.Length == 0 ||
            authority.EffectAuthority is not
            {
                CapturedProfileId: RuntimeProfileId.Beta111, DeckExact: true, TransformPoolsExact: true,
                OrderedDeck: { } deck, CharacterStrikeKey: { } strike, CharacterDefendKey: { } defend
            } ||
            !RetainsSource(deck.Where(c => c.IsBasic && c.IsStrike), strike, 2) ||
            !RetainsSource(deck.Where(c => c.IsBasic && c.IsDefend), defend, 1) ||
            deck.Any(c => c.IsBasic && c.IsStrike && (c.IsDefend || c.CardType is EffectCardType.Curse or EffectCardType.Status)))
            return false;

        // Leafy picks the first Strike and Defend. New Leaf uses the normalized
        // first remaining Strike. Both orders use the same captured source pools
        // when at least two interchangeable Strikes survive the initial premise.
        return true;
    }

    private static bool RetainsSource(IEnumerable<NeowEffectCardSnapshot> candidates, ModelKey key, int minimum)
    {
        var cards = candidates.ToArray();
        return cards.Length >= minimum && cards.All(c => c.CardKey == key &&
            string.Equals(c.PoolId, cards[0].PoolId, StringComparison.Ordinal));
    }

    internal static NeowStaticDrawGateCompiler.Gate[]? CompileIntervals(NeowFamilyGpuPlan plan)
    {
        if (plan.ModernTransformGate is not { } gate) return null;
        int strikeLength = (int)plan.Meta[58];
        int defendLength = (int)plan.Meta[59];
        int newLeafOffset = (int)plan.Meta[60];
        int newLeafLength = (int)plan.Meta[61];
        uint leafyTarget0 = plan.Conditions[Beta110FastRelicCatalog.LeafyPoultice * 5 + 1];
        uint leafyTarget1 = plan.Conditions[Beta110FastRelicCatalog.LeafyPoultice * 5 + 2];
        uint newLeafTarget = plan.Conditions[Beta110FastRelicCatalog.NewLeaf * 5 + 1];
        // The Defend buffer also contains the New Leaf pool at its own offset.
        // Match all repeated ranks and keep the usual bounded interval geometry.
        return NeowStaticDrawGateCompiler.TryCompileRanks(strikeLength, rank => gate.LeafyTargets > 0 && plan.Strike[rank] == leafyTarget0, 2, out var strike0) &&
            NeowStaticDrawGateCompiler.TryCompileRanks(strikeLength, rank => gate.LeafyTargets > 1 && plan.Strike[rank] == leafyTarget1, 2, out var strike1) &&
            NeowStaticDrawGateCompiler.TryCompileRanks(defendLength, rank => gate.LeafyTargets > 0 && plan.Defend[rank] == leafyTarget0, 2, out var defend0) &&
            NeowStaticDrawGateCompiler.TryCompileRanks(defendLength, rank => gate.LeafyTargets > 1 && plan.Defend[rank] == leafyTarget1, 2, out var defend1) &&
            NeowStaticDrawGateCompiler.TryCompileRanks(newLeafLength, rank => !gate.HasNewLeafTarget || plan.Defend[newLeafOffset + rank] == newLeafTarget, 2, out var leaf)
            ? [strike0!, strike1!, defend0!, defend1!, leaf!] : null;
    }

    internal string ShaderSource(NeowFamilyGpuPlan plan, bool lazy, bool drawIntervals)
    {
        var intervals = drawIntervals ? CompileIntervals(plan) : null;
        const string poolChecks = """
            bvec2 modern_leafy_strike_targets(uint64_t m) {
                uint rank = next_int_current_shortcut_from_mantissa(m, plan_value(58u));
                uint card = leafy_strike.ids[rank];
                return bvec2(card == conditions.values[4u * 5u + 1u], card == conditions.values[4u * 5u + 2u]);
            }
            bvec2 modern_leafy_defend_targets(uint64_t m) {
                uint rank = next_int_current_shortcut_from_mantissa(m, plan_value(59u));
                uint card = leafy_defend.ids[rank];
                return bvec2(card == conditions.values[4u * 5u + 1u], card == conditions.values[4u * 5u + 2u]);
            }
            bool modern_new_leaf_target(uint64_t m) {
                uint rank = next_int_current_shortcut_from_mantissa(m, plan_value(61u));
                return leafy_defend.ids[plan_value(60u) + rank] == conditions.values[19u * 5u + 1u];
            }
            """;
        string targetChecks = intervals is null ? poolChecks : $$"""
            bvec2 modern_leafy_strike_targets(uint64_t m) {
                return bvec2({{intervals[0].GlslExpression("m")}}, {{intervals[1].GlslExpression("m")}});
            }
            bvec2 modern_leafy_defend_targets(uint64_t m) {
                return bvec2({{intervals[2].GlslExpression("m")}}, {{intervals[3].GlslExpression("m")}});
            }
            bool modern_new_leaf_target(uint64_t m) {
                return {{intervals[4].GlslExpression("m")}};
            }
            """;
        return $$"""
            #define N_MODERN_TRANSFORM_LAZY {{(lazy ? 1 : 0)}}
            {{targetChecks}}

            bool modern_leafy_pair_matches(bvec2 strike, bvec2 defend) {
                return (strike.x && defend.y) || (strike.y && defend.x);
            }

            #if N_MODERN_TRANSFORM_LAZY
            uint64_t modern_transform_mantissa(uint64_t word) {
                return (rotl64(word * uint64_t(5u), 7u) * uint64_t(9u)) >> 11u;
            }
            #endif

            bool modern_transform_pre_gate(uint64_t root) {
                const bool leafy_first = {{(LeafyFirst ? "true" : "false")}};
                const bool new_leaf_first = {{(NewLeafFirst ? "true" : "false")}};
                uint64_t seed = root + plan_u64(37u);
            #if N_MODERN_TRANSFORM_LAZY
                uint64_t mix = seed + make_u64(0x7f4a7c15u, 0x9e3779b9u);
                uint64_t s1 = splitmix_next(mix);
                uint64_t first = modern_transform_mantissa(s1);
            #else
                RngState rng = rng_initialize(seed);
                uint64_t first = next_u64(rng) >> 11u;
            #endif
                bvec2 first_strike = leafy_first ? modern_leafy_strike_targets(first) : bvec2(false);
                const uint leafy_targets = {{LeafyTargets}}u;
                const bool has_new_leaf_target = {{(HasNewLeafTarget ? "true" : "false")}};
                bool leafy_route = leafy_first && (leafy_targets < 2u || any(first_strike));
                bool new_leaf_route = new_leaf_first && (!has_new_leaf_target || modern_new_leaf_target(first));
                if (new_leaf_route && leafy_targets == 0u) return true;
                if (!leafy_route && !new_leaf_route) return false;

            #if N_MODERN_TRANSFORM_LAZY
                mix = seed;
                uint64_t s0 = splitmix_next(mix);
                mix += make_u64(0x7f4a7c15u, 0x9e3779b9u);
                uint64_t s2 = splitmix_next(mix);
                uint64_t second = modern_transform_mantissa(s0 ^ s1 ^ s2);
            #else
                uint64_t second = next_u64(rng) >> 11u;
            #endif
                leafy_route = leafy_route && (leafy_targets == 0u || (leafy_targets == 1u
                    ? first_strike.x || modern_leafy_defend_targets(second).x
                    : modern_leafy_pair_matches(first_strike, modern_leafy_defend_targets(second))));
                if (leafy_route && !has_new_leaf_target) return true;
                bvec2 second_strike = new_leaf_route ? modern_leafy_strike_targets(second) : bvec2(false);
                new_leaf_route = new_leaf_route && (leafy_targets < 2u || any(second_strike));
                if (!leafy_route && !new_leaf_route) return false;

            #if N_MODERN_TRANSFORM_LAZY
                // Two xoshiro transitions put s0 ^ s3 ^ (s1 << 17) in word 1.
                // Only this final draw needs the fourth initial SplitMix word.
                uint64_t s3 = splitmix_next(mix);
                uint64_t third = modern_transform_mantissa(s0 ^ s3 ^ (s1 << 17u));
            #else
                uint64_t third = next_u64(rng) >> 11u;
            #endif
                if (leafy_route && modern_new_leaf_target(third)) return true;
                return new_leaf_route && (leafy_targets == 1u ? second_strike.x || modern_leafy_defend_targets(third).x
                    : modern_leafy_pair_matches(second_strike, modern_leafy_defend_targets(third)));
            }
            """;
    }
}
