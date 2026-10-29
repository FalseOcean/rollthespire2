using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Workspace;

internal static class SearchQueryProjection
{
    internal static int CountConditions(SearchQuery q) =>
        (q.OpeningRoute is null ? 0 : 1) + (q.OpeningRouteRelicRequirement is null ? 0 : 1) + q.StructuredOpeningEffects.Count +
        q.RelicSequenceConstraints.Count + (q.TreasureRelics is { } treasure ? treasure.Slots.Take(treasure.Count).Count(k => k.HasValue) : 0) +
        (q.FirstActUnknownEventPrefixCount > 0 ? 1 : 0) + (q.CombatCardRewards is null ? 0 : 1) + (q.CombatPotionRewards is null ? 0 : 1) +
        (q.CombatCardExclusion is null ? 0 : 1) +
        q.AncientBranches.Count + q.LegacyNeow.NeowRelics.Any.Count + q.LegacyNeow.NeowRelics.All.Count +
        q.LegacyWorld.AncientIdentityFilters.Count + q.LegacyWorld.AncientOptionFilters.Count + q.EventResultConditions.Count +
        q.EventSequenceConstraints.Count + q.VariantBossBranches.Count + q.LegacyWorld.BossOrdinalFilters.Count + q.StandardMaps.Count + q.StandardMapRoutes.Count +
        q.MerchantColorlessSequenceConditions.Count + q.RelicShopSequenceConditions.Count + (q.TransformationAggregate is null ? 0 : 1) +
        q.Players.Sum(p => CountConditions(p.Conditions) + (p.Offers.IsEmpty ? 0 : 1));

    internal static QueryHistoryEntry? Find(IEnumerable<QueryHistoryEntry> records, string key, string environment,
        SearchEnvironmentSignature signature, bool known) => records.FirstOrDefault(record =>
            record.QueryKey == key && record.EnvironmentFingerprint == environment &&
            (known ? SearchEnvironmentIdentity.Compare(record.EnvironmentSignature, signature).Status == SearchEnvironmentComparisonStatus.Same
                : !record.EnvironmentKnown));

    internal static QueryHistoryEntry Create(WorkbenchSearchDraft draft, SeedLibraryContext? context,
        SearchEnvironmentSignature signature, string batchId, IEnumerable<QueryHistoryEntry> records)
    {
        string key = QueryHistoryIdentity.Key(draft);
        bool known = SearchEnvironmentIdentity.IsComparable(signature);
        string environment = known ? QueryHistoryIdentity.EnvironmentKey(draft, signature.Fingerprint) : "unknown:" + batchId;
        return new QueryHistoryEntry
        {
            Id = QueryHistoryIdentity.AvailableRecordId(key, environment, records), QueryKey = key, IdentityVersion = 2,
            EnvironmentKnown = known, EnvironmentFingerprint = environment,
            RuntimeEnvironmentFingerprint = known ? signature.Fingerprint : "",
            EnvironmentSignature = known ? signature.Snapshot() : null, Draft = draft.ToPresetIntent(), Context = context
        };
    }
}
