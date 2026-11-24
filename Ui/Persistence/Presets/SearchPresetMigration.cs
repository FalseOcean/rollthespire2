using RolltheSpire2.Ui.Workspace;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Persistence;

internal sealed class SearchPresetMigrationException(string issue, IReadOnlyList<SearchPresetUnresolvedReference> references)
    : Exception(issue)
{
    internal IReadOnlyList<SearchPresetUnresolvedReference> References { get; } = references;
}

// All knowledge of historical preset payloads is consumed here. Business code
// receives a current template or an opaque unresolved asset, never a legacy draft.
internal static class SearchPresetMigration
{
    internal static WorkbenchSearchDraft ReadIntent(SearchPresetStore.PersistedSearchPreset asset, ModelKey character,
        JsonSerializerOptions json)
    {
        if (asset.Ascension is < SeedPredictionInputLimits.MinimumAscension or > SeedPredictionInputLimits.MaximumAscension)
            throw new InvalidDataException("PresetContextInvalid");
        bool hasWorkbench = Present(asset.WorkbenchSnapshot);
        bool hasQuery = Present(asset.QuerySnapshot);
        bool hasDraft = Present(asset.Draft);
        if ((hasWorkbench ? 1 : 0) + (hasQuery ? 1 : 0) + (hasDraft ? 1 : 0) != 1)
            throw new InvalidDataException(hasWorkbench || hasQuery || hasDraft ? "PresetMultipleQueryPayloads" : "PresetQuerySnapshotMissing");
        WorkbenchSearchDraft intent;
        if (hasWorkbench)
        {
            if (asset.SchemaVersion < 3) throw new InvalidDataException("PresetWorkbenchSchemaMismatch");
            var strict = new JsonSerializerOptions(json)
            {
                RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            };
            intent = JsonSerializer.Deserialize<WorkbenchSearchDraft>(asset.WorkbenchSnapshot, strict)
                ?? throw new InvalidDataException("PresetWorkbenchShapeUnresolved");
            SearchPresetCompatibilityResolver.ValidateWorkbenchShape(intent);
            if (intent.Character != character || intent.Ascension != asset.Ascension)
                throw new InvalidDataException("PresetWorkbenchContextMismatch");
        }
        else
        {
            if (asset.SchemaVersion >= 4) throw new InvalidDataException("PresetCurrentIntentRequired");
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(type =>
            {
                if (type.Type != typeof(SearchDraft)) return;
                // Retired physical run controls are explicitly outside preset intent.
                foreach (string name in new[] { "StartSeed", "ScanCount", "TargetMatchCount", "WorkerCount" })
                {
                    JsonPropertyInfo property = type.CreateJsonPropertyInfo(typeof(JsonElement), name);
                    property.Set = (_, _) => { }; type.Properties.Add(property);
                }
            });
            var strict = new JsonSerializerOptions(json) { TypeInfoResolver = resolver };
            var draft = JsonSerializer.Deserialize<SearchDraft>(hasQuery ? asset.QuerySnapshot : asset.Draft, strict)
                ?? throw new InvalidDataException("PresetQueryShapeUnresolved");
            SearchPresetOriginalInput.Validate(draft, character);
            intent = WorkbenchQueryCompiler.CreatePresetIntent(draft, character, asset.Ascension);
            intent = intent with { Query = MigrateLegacyPresetQuery(intent.Query) };
        }
        intent = intent.ToPresetIntent();
        SearchPresetCompatibilityResolver.ValidateWorkbenchShape(intent);
        return intent;
    }

    private static bool Present(JsonElement value) => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static RolltheSpire2.Search.Semantics.SearchQuery MigrateLegacyPresetQuery(RolltheSpire2.Search.Semantics.SearchQuery query)
    {
        // Both predicates read position one of the same pristine Shop relic lane
        // in ProductionQueryValidator. Only this exact legacy shape is migrated;
        // arbitrary Any/All/Ban ranges keep their original compatibility boundary.
        var shop = query.RelicSequenceConstraints.Where(c => c.Lane == Core.Relics.RelicSequenceKind.Shop).ToArray();
        if (shop is [var first] && first.RangeValue == 1 &&
            first.RangeMode is Search.Contracts.SearchSequenceRangeMode.FirstN or Search.Contracts.SearchSequenceRangeMode.ExactSlot &&
            first.Keys.Any.Count == 1 && first.Keys.All.Count == 0 && first.Keys.Ban.Count == 0 &&
            query.RelicShopSequenceConditions.Count == 0)
            return query with
            {
                RelicSequenceConstraints = query.RelicSequenceConstraints.Where(c => c.Lane != Core.Relics.RelicSequenceKind.Shop).ToArray(),
                RelicShopSequenceConditions = [new(1, Search.Contracts.CombatRewardSequenceOrderMode.Ordered, [first.Keys.Any[0]])]
            };
        return query;
    }
}
