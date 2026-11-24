using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Persistence;

// Separate from SearchQuery/predictor_context.json. Committed checkpoints own
// state; an optional active route draft replays accepted inputs from that boundary.
internal static class PredictorRunStore
{
    private sealed record SaveFile(int Schema, PredictorContext Context,
        ImmutableArray<PredictorCheckpoint> Checkpoints, long Revision)
    {
        public ImmutableArray<PredictorRoute> Routes { get; init; } = [];
        public PredictorCombatDefaults CombatDefaults { get; init; } = new();
        public bool AutomaticRewardPickup { get; init; } = true;
        public PredictorNodeDraft? ActiveDraft { get; init; }
        public ImmutableArray<PredictorPlannedNode> PlannedNodes { get; init; } = [];
    }
    private static readonly JsonSerializerOptions Json = Options();

    internal static string Serialize(PredictorRun run) =>
        JsonSerializer.Serialize(CreateSave(run), Json);

    private static SaveFile CreateSave(PredictorRun run)
    {
        run.CombatDefaults.Validate();
        PredictorRun.ValidatePlannedNodes(run.PlannedNodes);
        return new(7, run.Context, run.Checkpoints, run.Revision)
        {
            Routes = run.Routes, CombatDefaults = run.CombatDefaults,
            AutomaticRewardPickup = run.AutomaticRewardPickup, ActiveDraft = run.CaptureNodeDraft(),
            PlannedNodes = run.PlannedNodes
        };
    }

    internal static PredictorRun Deserialize(string json, string expectedCatalogFingerprint, string expectedUnlockFingerprint)
    {
        var save = JsonSerializer.Deserialize<SaveFile>(json, Json) ?? throw new InvalidDataException("PredictorSaveEmpty");
        if (save.Schema is not (1 or 2 or 3 or 4 or 5 or 6 or 7) || save.Context == null || save.Context.CatalogFingerprint != expectedCatalogFingerprint ||
            save.Context.UnlockFingerprint != expectedUnlockFingerprint)
            throw new InvalidDataException("PredictorSaveAuthorityMismatch");
        if (save.Context.Crystal is not null)
            throw new InvalidDataException("PredictorSaveLocalCrystalContext");
        if (save.CombatDefaults == null || save.Schema == 1 && save.ActiveDraft != null)
            throw new InvalidDataException("PredictorSaveSettingsInvalid");
        save.CombatDefaults.Validate();
        var run = new PredictorRun(save.Context, save.Checkpoints, checked(save.Revision + 1));
        run.CombatDefaults = save.CombatDefaults;
        run.AutomaticRewardPickup = save.AutomaticRewardPickup;
        run.RestoreRoutes(save.Routes);
        if (save.ActiveDraft is { } draft) run.RestoreNodeDraft(draft);
        run.RestorePlannedNodes(save.Schema >= 3 ? save.PlannedNodes : []);
        return run;
    }

    internal static void Save(string path, PredictorRun run)
    {
        // Unsupported active nodes/inputs must fail before touching the old file.
        string serialized = Serialize(run);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, serialized);
            File.Move(temp, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static PredictorRun Load(string path, string catalogFingerprint, string unlockFingerprint) =>
        Deserialize(File.ReadAllText(path), catalogFingerprint, unlockFingerprint);

    internal static PredictorContext ReadFrozenContext(string path)
    {
        var save = JsonSerializer.Deserialize<SaveFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("PredictorSaveEmpty");
        if (save.Schema is not (1 or 2 or 3 or 4 or 5 or 6 or 7) || save.Context == null) throw new InvalidDataException("PredictorSaveAuthorityMismatch");
        if (save.Context.Crystal is not null)
            throw new InvalidDataException("PredictorSaveLocalCrystalContext");
        PredictorSeedState.ValidateContext(save.Context);
        return save.Context;
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.Converters.Add(new KeyConverter());
        options.Converters.Add(new InputConverter());
        return options;
    }

    // Only these compiled input contracts can be replayed. No runtime type name,
    // assembly loading or arbitrary PredictorInput subclass is accepted from JSON.
    private sealed class InputConverter : JsonConverter<PredictorInput>
    {
        private static readonly Type[] Types =
        [
            typeof(ChooseAncientOffer), typeof(SelectTransformationCard), typeof(ChooseEventOption),
            typeof(SelectEventCards), typeof(ProvideEventHealthFact), typeof(ConfirmTrialAbandon),
            typeof(SelectCrystalTool), typeof(RevealCrystalCell), typeof(ActivateOpeningModifier),
            typeof(SelectOpeningModifierCards), typeof(ChooseScrollBoxBundle), typeof(ChooseObtainCard),
            typeof(ChooseObtainDeckCards), typeof(ChooseObtainManyCards), typeof(SelectRestAction),
            typeof(SelectSmithCard), typeof(CancelSmithSelection), typeof(SelectCookCards),
            typeof(CancelCookSelection), typeof(ProvideRestMaxHpFact), typeof(LeaveRestSite),
            typeof(SacrificeCardReward), typeof(RerollCardReward), typeof(ProvideRoomMaximumHp),
            typeof(OpenTreasure), typeof(TakeTreasureRelic), typeof(LeaveTreasure),
            typeof(DeclareEventEligibility), typeof(ChooseEventOccurrence), typeof(ResolveCombat), typeof(ContinueVictory),
            typeof(TakeReward), typeof(EnterCardSelection), typeof(ExitCardSelection), typeof(SkipCardReward),
            typeof(LeaveRewards), typeof(SelectRemovalCards), typeof(CancelRewardCardRemoval),
            typeof(DiscardPotion), typeof(UseOutOfCombatPotion), typeof(ReplaceRewardPotion),
            typeof(CancelPotionReplacement), typeof(CorrectGoldBalance), typeof(ChooseOpeningRelic),
            typeof(BuyShopEntry), typeof(BeginShopCardRemoval), typeof(ChooseShopRemovalCard),
            typeof(CancelShopCardRemoval), typeof(OpenShopInventory), typeof(CloseShopInventory), typeof(LeaveShop)
        ];
        private static readonly IReadOnlyDictionary<string, Type> ByName = Types.ToDictionary(t => t.Name, StringComparer.Ordinal);

        public override PredictorInput Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("Type", out var discriminator) || discriminator.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !ByName.TryGetValue(discriminator.GetString()!, out var inputType))
                throw new JsonException("PredictorDraftInputTypeUnsupported");
            return (PredictorInput?)data.Deserialize(inputType, options) ?? throw new JsonException("PredictorDraftInputEmpty");
        }

        public override void Write(Utf8JsonWriter writer, PredictorInput value, JsonSerializerOptions options)
        {
            Type inputType = value.GetType();
            if (!Types.Contains(inputType))
                throw new NotSupportedException("PredictorDraftInputTypeUnsupported:" + inputType.Name);
            writer.WriteStartObject();
            writer.WriteString("Type", inputType.Name);
            writer.WritePropertyName("Data");
            JsonSerializer.Serialize(writer, value, inputType, options);
            writer.WriteEndObject();
        }
    }

    private sealed class KeyConverter : JsonConverter<ModelKey>
    {
        public override ModelKey Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            string? text = reader.GetString();
            // Empty keys occur only in reward-independent default values, not inventories.
            if (text == ":") return default;
            if (!ModelKey.TryParseExact(text, out var key)) throw new JsonException("InvalidModelKey");
            return key;
        }
        public override void Write(Utf8JsonWriter writer, ModelKey value, JsonSerializerOptions options) => writer.WriteStringValue(value.Serialized);
    }
}
