using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

// Guaranteed opening acquisitions only. Alternative Any identities and ordinary
// future Relic queues cannot become held reward modifiers in Product probability.
internal static class ExplicitOpeningRelicFacts
{
    // The Unknown-room model shares C's declared opening premise. Unspecified
    // pickups do not contribute; alternative offers are not held possessions.
    internal static bool HasDeclaredJuzuForUnknownRooms(SearchQuery query)
    {
        var juzu = new ModelKey("RELIC", "JUZU_BRACELET");
        return EnumerateGuaranteed(query).Contains(juzu) || query.Players.Any(player =>
            EnumerateGuaranteed(player.Conditions).Contains(juzu));
    }

    internal static ModelKey[] EnumerateGuaranteed(SearchQuery query) => EnumerateGuaranteed(
        query.OpeningRoute, query.OpeningRouteRelicRequirement, query.StructuredOpeningEffects, query.LegacyNeow);

    internal static ModelKey[] EnumerateGuaranteed(NeowRouteSearchCondition? openingRoute,
        OpeningRouteRelicRequirement? routeRelics, IEnumerable<NeowStructuredEffectSearchCondition> structured,
        LegacyNeowSemanticConstraints legacy)
    {
        if (openingRoute is not { IsValid: true } route) return [];
        var rows = structured.Where(row => !row.IsEmpty).ToArray();
        return Stable(EnumerateGuaranteedRouteGrants(openingRoute, routeRelics, rows, legacy)
            .Concat(DeclaredCapsuleContents(route, rows, legacy)));
    }

    internal static ModelKey[] DeclaredCapsuleContents(SearchQuery query) =>
        DeclaredCapsuleContents(query.OpeningRoute, query.StructuredOpeningEffects, query.LegacyNeow);

    private static ModelKey[] DeclaredCapsuleContents(NeowRouteSearchCondition? route,
        IEnumerable<NeowStructuredEffectSearchCondition> rows, LegacyNeowSemanticConstraints legacy)
    {
        var keys = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        bool bones = route?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones;
        bool direct = route is not null && IsCapsule(route.RouteRelicKey);
        // A guaranteed nested acquisition does not identify which Capsule
        // supplied it. It is a held fact, never a new route-grant opcode.
        if (bones || direct || route is null)
        {
            foreach (var row in rows.Where(row =>
                row.Scope == NeowStructuredEffectScope.NestedRelics && row.OutputKind == NeowStructuredOutputKind.Relic &&
                (IsCapsule(row.SourceRelicKey) && (bones || route is null || row.SourceRelicKey == route.RouteRelicKey) ||
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

    // Given one assignment of named pickups to initial positions, express a
    // remaining-queue row in coordinates of the original permutation solver.
    internal static IReadOnlyList<RelicSequenceSearchCondition>? InitialQueueConditions(
        IReadOnlyList<RelicSequenceSearchCondition> conditions, int laneSize,
        IEnumerable<(int Position, ModelKey Key)> namedPositions)
    {
        var removed = namedPositions.Distinct().OrderBy(item => item.Position).ToArray();
        if (removed.Length == 0 || conditions.Count == 0) return conditions;
        var keys = removed.Select(item => item.Key).ToHashSet();
        var result = new List<RelicSequenceSearchCondition>();
        foreach (var row in conditions)
        {
            var any = row.Keys.Any.Where(key => !keys.Contains(key)).ToArray();
            if (row.Keys.All.Any(keys.Contains) || row.Keys.Any.Count > 0 && any.Length == 0 ||
                row.RangeMode == SearchSequenceRangeMode.ExactSlot && row.RangeValue > laneSize - keys.Count)
                return null;
            int raw = row.RangeValue;
            foreach (var item in removed)
                if (item.Position <= raw) raw++;
            result.Add(row with { RangeValue = Math.Min(raw, laneSize), Keys = row.Keys with
                { Any = any, Ban = row.Keys.Ban.Where(key => !keys.Contains(key)).ToArray() } });
        }
        return result;
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
