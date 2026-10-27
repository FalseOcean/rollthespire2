using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

// Guaranteed opening acquisitions only. Alternative Any identities and ordinary
// future Relic queues cannot become held reward modifiers in Product probability.
internal static class ExplicitOpeningRelicFacts
{
    internal static ModelKey[] EnumerateGuaranteed(SearchQuery query) => EnumerateGuaranteed(
        query.OpeningRoute, query.OpeningRouteRelicRequirement, query.StructuredOpeningEffects, query.LegacyNeow);

    internal static ModelKey[] EnumerateGuaranteed(NeowRouteSearchCondition? openingRoute,
        OpeningRouteRelicRequirement? routeRelics, IEnumerable<NeowStructuredEffectSearchCondition> structured,
        LegacyNeowSemanticConstraints legacy)
    {
        if (openingRoute is not { IsValid: true } route) return [];
        var rows = structured.Where(row => !row.IsEmpty).ToArray();
        var keys = EnumerateGuaranteedRouteGrants(openingRoute, routeRelics, rows, legacy)
            .ToHashSet(ModelKeyComparer.Instance);
        bool bones = route.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones;
        var capsuleSources = keys.Where(IsCapsule).ToHashSet(ModelKeyComparer.Instance);
        // A guaranteed nested acquisition does not identify which Capsule
        // supplied it. It is a held fact, never a new route-grant opcode.
        if (bones || capsuleSources.Count > 0)
        {
            foreach (var row in rows.Where(row =>
                row.Scope == NeowStructuredEffectScope.NestedRelics && row.OutputKind == NeowStructuredOutputKind.Relic &&
                (IsCapsule(row.SourceRelicKey) && capsuleSources.Contains(row.SourceRelicKey) ||
                    bones && row.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset)))
                keys.UnionWith(row.OutputKeys);
            AddGuaranteed(keys, legacy.CapsuleContainedRelics);
            if (legacy.RequireWhetstone) keys.Add(BaseGameModelKeys.OrdinaryRelics.Whetstone);
            if (legacy.RequireWarPaint) keys.Add(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        }
        return Stable(keys);
    }

    // Immediate direct/Bones acquisitions only, without an execution order.
    // Consumers that replay RNG must preserve authored order separately and
    // accept only the opcodes they implement; nested outputs stay out of here.
    internal static ModelKey[] EnumerateGuaranteedRouteGrants(SearchQuery query) => EnumerateGuaranteedRouteGrants(
        query.OpeningRoute, query.OpeningRouteRelicRequirement, query.StructuredOpeningEffects, query.LegacyNeow);

    internal static ModelKey[] EnumerateGuaranteedRouteGrants(NeowRouteSearchCondition? openingRoute,
        OpeningRouteRelicRequirement? routeRelics, IEnumerable<NeowStructuredEffectSearchCondition> structured,
        LegacyNeowSemanticConstraints legacy)
    {
        if (openingRoute is not { IsValid: true } route) return [];
        var keys = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        bool bones = route.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones;
        if (!bones) keys.Add(route.RouteRelicKey);
        else
        {
            if (routeRelics is { IsEmpty: false } required &&
                required.ParentRouteRelicKey == BaseGameModelKeys.Relics.NeowsBones)
                keys.UnionWith(required.RequiredRelicKeys);
            keys.UnionWith(legacy.RequiredBonesCombination);
            keys.UnionWith(legacy.RequiredBonesAcquisitionOrder);
            AddGuaranteed(keys, legacy.BonesRelics);
            foreach (var row in structured.Where(row => !row.IsEmpty))
            {
                if (row.SourceRelicKey != BaseGameModelKeys.Relics.NeowsBones) keys.Add(row.SourceRelicKey);
                else if (row.Scope == NeowStructuredEffectScope.BonesOfferedRelics && row.OutputKind == NeowStructuredOutputKind.Relic)
                    keys.UnionWith(row.OutputKeys);
                else if (row.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset)
                {
                    keys.Add(BaseGameModelKeys.Relics.SmallCapsule);
                    keys.Add(BaseGameModelKeys.Relics.LargeCapsule);
                }
            }
            if (legacy.RequireSmallCapsule) keys.Add(BaseGameModelKeys.Relics.SmallCapsule);
            if (legacy.RequireLargeCapsule) keys.Add(BaseGameModelKeys.Relics.LargeCapsule);
        }
        return Stable(keys);
    }

    private static void AddGuaranteed(HashSet<ModelKey> keys, ModelKeySetFilter filter)
    {
        keys.UnionWith(filter.All);
        if (filter.Any.Count == 1) keys.Add(filter.Any[0]);
    }

    private static ModelKey[] Stable(IEnumerable<ModelKey> keys) =>
        keys.Where(key => key.IsValid && key != BaseGameModelKeys.Relics.NeowsBones)
            .OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();

    private static bool IsCapsule(ModelKey key) =>
        key == BaseGameModelKeys.Relics.SmallCapsule || key == BaseGameModelKeys.Relics.LargeCapsule;
}
