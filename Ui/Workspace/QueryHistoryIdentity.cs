using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Search.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Workspace;

internal static class QueryHistoryIdentity
{
    // Only unordered predicates are sorted. Player order, pickup order, slots,
    // and other ordered observations are deliberately left intact.
    private static readonly HashSet<string> Sets = new(StringComparer.Ordinal)
    {
        "Any", "All", "Ban", "OptionAny", "SeaGlassTargetAny", "EventSequenceConstraints",
        "EventResultConditions", "VariantBossBranches", "AncientBranches", "RelicSequenceConstraints",
        "RelicShopSequenceConditions", "MerchantColorlessSequenceConditions", "StandardMaps",
        "BossFilters", "BossOrdinalFilters", "AncientIdentityFilters", "AncientOptionFilters",
        "AncientSeaGlassTargetFilters", "StructuredOpeningEffects", "unlocked_epochs", "encounters_seen", "CardKeys"
    };

    internal static string Key(WorkbenchSearchDraft draft)
    {
        var clean = draft.ToPresetIntent();
        var query = clean.Query;
        if (query.Players.Count > 0)
        {
            try { query = PartyInitialQuery.BindSharedConditions(query); }
            // Saved presets may contain conflicting conditions. Keep their raw
            // identity; normalization failure here must never discard user intent.
            catch (ArgumentException) { }
        }
        if (query.StandardMapRoutes.All(route => route is { IsValid: true }) &&
            query.StandardMapRoutes.Select(route => route.Act).Distinct().Count() == query.StandardMapRoutes.Count)
            query = query with { StandardMapRoutes = query.StandardMapRoutes.Select(route => route.Normalize()).OrderBy(route => route.Act).ToArray() };
        return Hash(new { IntentVersion = 2, clean.Character, clean.Ascension, clean.Mode, Query = query, clean.AncientPremises,
            Players = clean.Players.Select(p => new { p.Slot, p.Character, UnlockPolicy = p.UnlockSource }).ToArray() });
    }

    internal static string EnvironmentKey(WorkbenchSearchDraft draft, string runtimeFingerprint) => Hash(new
    {
        EnvironmentVersion = 2, Runtime = runtimeFingerprint,
        Players = draft.Players.Select(p => new { p.Slot, Unlocks = p.Unlocks is not { } unlocks ? null :
            new MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState {
                UnlockedEpochs = unlocks.UnlockedEpochs, EncountersSeen = unlocks.EncountersSeen,
                NumberOfRuns = unlocks.NumberOfRuns == 0 ? 0 : 1
            } }).ToArray()
    });
    internal static string RecordId(string queryKey, string environmentFingerprint) => Hash(new { Query = queryKey, Environment = environmentFingerprint });
    internal static string AvailableRecordId(string queryKey, string environment, IEnumerable<QueryHistoryEntry> records)
    {
        string original = RecordId(queryKey, environment), id = original;
        int suffix = 0;
        while (records.Any(record => record.Id == id)) id = original + ":environment-v2:" + ++suffix;
        return id;
    }

    private static string Hash(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, SearchPresetStore.CreateJsonOptions());
        string canonical = Canonical(node, "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Canonical(JsonNode? node, string name)
    {
        if (node is JsonObject obj)
            return "{" + string.Join(",", obj.Where(p => p.Key != "StandardMapRoutes" || p.Value is not JsonArray { Count: 0 })
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value, p.Key))) + "}";
        if (node is JsonArray array)
        {
            IEnumerable<string> items = array.Select(item => Canonical(item, ""));
            if (Sets.Contains(name)) items = items.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            return "[" + string.Join(",", items) + "]";
        }
        return node?.ToJsonString() ?? "null";
    }
}
