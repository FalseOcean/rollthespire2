using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Query-literal replay contract for RNG mechanics that occur before normal Combat Reward.
/// It is intentionally not a reconstruction of the real Neow route: only opening mechanics
/// explicitly authored by the player are replayed. Unspecified Bones grants / Capsule nested
/// relics are ordinary no-special-effect placeholders. Production Exact remains responsible
/// for reconstructing the real route and rejecting incompatible candidates.
/// </summary>
internal sealed record Beta110CombatRewardOpeningConsumptionProjection(
    bool ReplayBonesOffer,
    byte[] OrderedRelicIds,
    string Fingerprint)
{
    public bool HasReplay => ReplayBonesOffer || OrderedRelicIds.Length > 0;

    public static Beta110CombatRewardOpeningConsumptionProjection Empty { get; } = new(
        false,
        Array.Empty<byte>(),
        FingerprintOf(false, Array.Empty<byte>()));

    internal static string FingerprintOf(bool replayBonesOffer, IReadOnlyList<byte> relicIds) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"combat-reward-opening-consumption-v1|bones={replayBonesOffer}|relics={string.Join(',', relicIds)}")))
            .ToLowerInvariant();
}

internal static class Beta110CombatRewardOpeningConsumptionProjector
{

    public static Beta110CombatRewardOpeningConsumptionProjection Project(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        LegacySearchCriteriaProjection legacy = LegacySearchCriteriaAdapter.Project(compiled);
        return Project(compiled.NormalizedQuery, legacy.Filter);
    }

    private static Beta110CombatRewardOpeningConsumptionProjection Project(
        SearchQuery query,
        NeowSearchFilter filter)
    {
        if (query.OpeningRoute is not { IsValid: true } route)
            return ProjectLegacyFallback(filter);

        if (!Beta110FastRelicCatalog.TryGetId(route.RouteRelicKey, out byte routeId))
            return Beta110CombatRewardOpeningConsumptionProjection.Empty;

        if (route.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones)
        {
            byte[] direct = { routeId };
            return new Beta110CombatRewardOpeningConsumptionProjection(
                false,
                direct,
                Beta110CombatRewardOpeningConsumptionProjection.FingerprintOf(false, direct));
        }

        // Bones itself consumes Rewards RNG while producing its two offered grants.
        // The grants are *not* reconstructed here. Only authored child mechanics are
        // replayed; missing children are ordinary placeholders by product policy.
        var childKeys = new List<ModelKey>(2);
        if (query.OpeningRouteRelicRequirement is { IsEmpty: false } requirement &&
            requirement.ParentRouteRelicKey == BaseGameModelKeys.Relics.NeowsBones)
        {
            childKeys.AddRange(requirement.RequiredRelicKeys);
        }

        if (childKeys.Count == 0 && filter.RequiredBonesAcquisitionOrder.Count > 0)
            childKeys.AddRange(filter.RequiredBonesAcquisitionOrder);
        if (childKeys.Count == 0 && filter.RequiredBonesCombination.Count > 0)
            childKeys.AddRange(filter.RequiredBonesCombination);

        // A typed structured condition can only execute if its source relic is present.
        // Treat that source as an authored Bones child when the parent route is Bones.
        foreach (NeowStructuredEffectSearchCondition condition in query.StructuredOpeningEffects)
        {
            if (condition.IsEmpty || !condition.SourceRelicKey.IsValid ||
                condition.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones)
                continue;
            if (!childKeys.Contains(condition.SourceRelicKey, ModelKeyComparer.Instance))
                childKeys.Add(condition.SourceRelicKey);
        }

        byte[] children = childKeys
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .Take(2)
            .Select(key => Beta110FastRelicCatalog.TryGetId(key, out byte id) ? id : Beta110FastRelicCatalog.InvalidId)
            .Where(id => id != Beta110FastRelicCatalog.InvalidId)
            .ToArray();

        return new Beta110CombatRewardOpeningConsumptionProjection(
            true,
            children,
            Beta110CombatRewardOpeningConsumptionProjection.FingerprintOf(true, children));
    }

    private static Beta110CombatRewardOpeningConsumptionProjection ProjectLegacyFallback(NeowSearchFilter filter)
    {
        bool bones = filter.RequireNeowsBones;
        var relicIds = new List<byte>(2);

        if (bones)
        {
            IEnumerable<ModelKey> keys = filter.RequiredBonesAcquisitionOrder.Count > 0
                ? filter.RequiredBonesAcquisitionOrder
                : filter.RequiredBonesCombination;
            foreach (ModelKey key in keys.Take(2))
            {
                if (Beta110FastRelicCatalog.TryGetId(key, out byte id)) relicIds.Add(id);
            }
        }
        else if (filter.RequireLargeCapsule)
        {
            relicIds.Add(Beta110FastRelicCatalog.LargeCapsule);
        }
        else if (filter.RequireSmallCapsule)
        {
            relicIds.Add(Beta110FastRelicCatalog.SmallCapsule);
        }

        if (!bones && relicIds.Count == 0)
            return Beta110CombatRewardOpeningConsumptionProjection.Empty;

        byte[] ids = relicIds.ToArray();
        return new Beta110CombatRewardOpeningConsumptionProjection(
            bones,
            ids,
            Beta110CombatRewardOpeningConsumptionProjection.FingerprintOf(bones, ids));
    }
}
