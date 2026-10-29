using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Contracts;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Ui.Settings;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Workspace;

internal sealed partial class SearchWorkspace
{
    private const double PreferenceDebounceSeconds = 0.60;
    private UserPreferencesDocument _preferences;
    private PredictorContextDocument _predictorContext;
    private bool _preferencesDirty;
    private bool _predictorContextDirty;
    private double _preferencesDirtyAge;
    private double _predictorContextDirtyAge;
    public PredictorContextDocument PredictorContext => _predictorContext;
    public void FlushPredictorContext() => FlushDocument(ref _predictorContextDirty, ref _predictorContextDirtyAge, "predictor_context.json", _predictorContext);
    public void FlushPreferences()
    {
        InitializeFeaturePreferences();
        FlushDocument(ref _preferencesDirty, ref _preferencesDirtyAge, "user_preferences.json", _preferences);
    }
    private void MarkPreferencesDirty() { _preferencesDirty = true; _preferencesDirtyAge = 0d; }
    private void MarkPredictorContextDirty() { _predictorContextDirty = true; _predictorContextDirtyAge = 0d; }

    public UserPreferencesDocument Preferences => _preferences;

    internal void SetCrystalSphereOptions(bool? avoidCurse = null, bool? includeDriftwood = null,
        bool? includeEnchantments = null, bool? transparentMask = null)
    {
        InitializeFeaturePreferences();
        InRunFeaturePreferences.SetCrystalSphere(_directory, avoidCurse, includeDriftwood, includeEnchantments, transparentMask);
        MarkPreferencesDirty();
        FlushPreferences();
    }

    internal static int ResolveSearchWorkers(int? savedBudget, int logicalProcessors) =>
        Math.Clamp(savedBudget ?? Math.Max(1, logicalProcessors / 2), 1, 64);

    internal void SetSearchWorkerBudget(int? budget)
    {
        _preferences.SearchWorkerBudget = budget.HasValue ? Math.Clamp(budget.Value, 1, 64) : null;
        MarkPreferencesDirty(); FlushPreferences();
    }

    public void SetSkipExactValidation(bool enabled)
    {
        _preferences.SkipExactValidation = enabled;
        MarkPreferencesDirty(); FlushPreferences();
    }

    internal MapLossyOptions MapLossyPreferences =>
        new MapLossyOptions(_preferences.MapLossyEnabled, _preferences.MapLossyGpuBatchSize).Normalize();

    public void SetMapLossyEnabled(bool enabled)
    {
        _preferences.MapLossyEnabled = enabled;
        MarkPreferencesDirty(); FlushPreferences();
    }

    public void SetMapLossyGpuBatchSize(int size)
    {
        _preferences.MapLossyGpuBatchSize = new MapLossyOptions(GpuBatchSize: size).Normalize().GpuBatchSize;
        MarkPreferencesDirty(); FlushPreferences();
    }

    public void ResetMapLossyPreferences()
    {
        _preferences.MapLossyEnabled = false;
        _preferences.MapLossyGpuBatchSize = MapLossyOptions.DefaultGpuBatchSize;
        MarkPreferencesDirty(); FlushPreferences();
    }

    public bool NeedsNotesAcknowledgement(RolltheSpire2.Presentation.DeveloperNotes.DeveloperNotesChapter chapter) =>
        chapter.RequiresAcknowledgement &&
        _preferences.ReadNotesVersions.GetValueOrDefault(chapter.Id) != chapter.AcknowledgementVersion;

    public void AcknowledgeNotesChapter(RolltheSpire2.Presentation.DeveloperNotes.DeveloperNotesChapter chapter)
    {
        if (!chapter.RequiresAcknowledgement || string.IsNullOrWhiteSpace(chapter.AcknowledgementVersion)) return;
        _preferences.ReadNotesVersions[chapter.Id] = chapter.AcknowledgementVersion;
        MarkPreferencesDirty(); FlushPreferences();
    }

    public void SetShowInRunPredictionEntry(bool enabled)
    {
        if (_preferences.ShowInRunPredictionEntry == enabled) return;
        _preferences.ShowInRunPredictionEntry = enabled;
        MarkPreferencesDirty();
        FlushPreferences();
    }

    public void SetInRunConsole(bool enabled, double verticalPercent, double scalePercent)
    {
        _preferences.ShowInRunConsole = enabled;
        _preferences.InRunConsoleVerticalPercent = double.IsFinite(verticalPercent) ? Math.Clamp(verticalPercent, 10, 90) : 50;
        _preferences.InRunConsoleScalePercent = double.IsFinite(scalePercent) ? Math.Clamp(scalePercent, 75, 125) : 100;
        MarkPreferencesDirty(); FlushPreferences();
    }

    internal void SetWorkbenchSearchMode(string mode)
    {
        _preferences.SearchMode = mode == "CPU" ? "CPU" : "Auto";
        MarkPreferencesDirty(); FlushPreferences();
    }

    internal void SaveWorkbenchPreferences(IReadOnlyDictionary<string, bool> flags, string page)
    {
        _preferences.WorkbenchFlags = new(flags);
        _preferences.WorkbenchPage = page;
        MarkPreferencesDirty();
        FlushPreferences();
    }

    internal void InitializeWorkbenchCursor() => EnsureCursorInitialized();

    public void SetLanguageOverride(string language)
    {
        InitializeFeaturePreferences();
        _preferences.LanguageOverride = language is "en" or "zh" ? language : string.Empty;
        InRunFeaturePreferences.SetLanguage(_directory, _preferences.LanguageOverride);
        MarkPreferencesDirty();
        FlushPreferences();
    }

    internal static string NormalizeFontChoice(string? choice) => choice is "game-serif" or "game-sans" ? choice : "game-serif";

    internal void SetFontChoice(string choice)
    {
        _preferences.FontChoice = NormalizeFontChoice(choice);
        MarkPreferencesDirty(); FlushPreferences();
    }

    public void SetActInformationIdentityGuideExpanded(bool expanded)
    {
        if (_preferences.ActInformationIdentityGuideExpanded == expanded) return;
        _preferences.ActInformationIdentityGuideExpanded = expanded;
        MarkPreferencesDirty();
        FlushPreferences();
    }

    public void SetActInformationMapGuideExpanded(bool expanded)
    {
        if (_preferences.ActInformationMapGuideExpanded == expanded) return;
        _preferences.ActInformationMapGuideExpanded = expanded;
        _preferences.ActInformationGuideExpanded = expanded;
        MarkPreferencesDirty();
        FlushPreferences();
    }
    public void ApplyPreferencesToRuntimeSettings(RuntimePredictionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.RelicComputeBackendPreference = _preferences.SearchMode switch
        {
            "CPU" => Beta110RelicComputeBackendPreference.Cpu,
            _ => Beta110RelicComputeBackendPreference.Auto
        };
        settings.NormalizeWorkshopSearchModePreference();
        settings.SearchWorkerCount = ResolveSearchWorkers(_preferences.SearchWorkerBudget, System.Environment.ProcessorCount);
    }

    public void CapturePreferences(string languageCode, Vector2 logicalSize, RuntimePredictionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string normalizedLanguage = string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
        Beta110RelicComputeBackendPreference mode = settings.NormalizeWorkshopSearchModePreference();
        string storedMode = mode == Beta110RelicComputeBackendPreference.Cpu ? "CPU" : "Auto";
        float width = Math.Max(0f, logicalSize.X);
        float height = Math.Max(0f, logicalSize.Y);
        if (string.Equals(_preferences.Language, normalizedLanguage, StringComparison.Ordinal) &&
            Math.Abs(_preferences.PanelWidth - width) < 0.5f &&
            Math.Abs(_preferences.PanelHeight - height) < 0.5f &&
            string.Equals(_preferences.SearchMode, storedMode, StringComparison.Ordinal) &&
            _preferences.SearchWorkerBudget == settings.SearchWorkerCount)
        {
            return;
        }
        _preferences.Language = normalizedLanguage;
        _preferences.PanelWidth = width;
        _preferences.PanelHeight = height;
        _preferences.SearchMode = storedMode;
        _preferences.SearchWorkerBudget = settings.SearchWorkerCount;
        MarkPreferencesDirty();
    }

    public void SetLastPage(string pageKey)
    {
        string normalized = pageKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(_preferences.LastPage, normalized, StringComparison.Ordinal))
        {
            return;
        }
        _preferences.LastPage = normalized;
        MarkPreferencesDirty();
    }

    public void SetShowOfficialPresets(bool show)
    {
        if (_preferences.ShowOfficialPresets == show)
            return;
        _preferences.ShowOfficialPresets = show;
        MarkPreferencesDirty();
    }

    public void SavePredictorContext(
        string seed,
        ModelKey characterKey,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        bool? allCharacterCardPoolsUnlocked,
        AncientOptionConditionProfile conditions,
        string preferredOpeningRouteId,
        int? preferredOpeningChoiceSlotIndex,
        string preferredRewardRouteGroupId)
    {
        _predictorContext.Seed = seed?.Trim() ?? string.Empty;
        _predictorContext.CharacterKey = characterKey.Serialized;
        _predictorContext.Ascension = Math.Clamp(ascension, SeedPredictionInputLimits.MinimumAscension, SeedPredictionInputLimits.MaximumAscension);
        _predictorContext.PlayersCount = Math.Clamp(playersCount, SeedPredictionInputLimits.MinimumPlayers, SeedPredictionInputLimits.MaximumPlayers);
        _predictorContext.PlayerSlotIndex = Math.Clamp(playerSlotIndex, 0, Math.Max(0, _predictorContext.PlayersCount - 1));
        // Last-known Predictor display context only. A subsequent analysis still
        // captures fresh immutable Runtime Authority before prediction.
        _predictorContext.AllCharacterCardPoolsUnlocked = allCharacterCardPoolsUnlocked;
        _predictorContext.TezcataraHasBasicStrike = conditions.TezcataraHasBasicStrike;
        _predictorContext.NonupeipeSwiftEnchantableAtLeast4 = conditions.NonupeipeSwiftEnchantableAtLeast4;
        _predictorContext.TanxInstinctEnchantableAtLeast3 = conditions.TanxInstinctEnchantableAtLeast3;
        _predictorContext.PaelGoopyDefendCardsAtLeast3 = conditions.PaelGoopyDefendCardsAtLeast3;
        _predictorContext.PaelAllowLegionNoEventPet = conditions.PaelAllowLegionNoEventPet;
        _predictorContext.PaelRemovableCardsAtLeast5 = conditions.PaelRemovableCardsAtLeast5;
        _predictorContext.OrobasArchaicToothConditionMet = conditions.OrobasArchaicToothConditionMet;
        _predictorContext.OrobasTouchOfOrobasConditionMet = conditions.OrobasTouchOfOrobasConditionMet;
        _predictorContext.DarvAllowPandorasBoxRelicSet = conditions.DarvAllowPandorasBoxRelicSet;
        _predictorContext.PreferredOpeningRouteId = preferredOpeningRouteId?.Trim() ?? string.Empty;
        _predictorContext.PreferredOpeningChoiceSlotIndex = preferredOpeningChoiceSlotIndex ?? -1;
        _predictorContext.PreferredRewardRouteGroupId = preferredRewardRouteGroupId?.Trim() ?? string.Empty;
        MarkPredictorContextDirty();
    }

    internal void SavePredictorParty(SeedLibraryContext party, string seed, bool active)
    {
        _predictorContext.Party = party;
        _predictorContext.PartySeed = seed;
        _predictorContext.PartyActive = active;
        MarkPredictorContextDirty();
    }

    private void NormalizePreferences()
    {
        _preferences.Appearance = null;
        _preferences.FontChoice = NormalizeFontChoice(_preferences.FontChoice);
        _preferences.SchemaVersion = SchemaVersion;
        _preferences.Language = string.Equals(_preferences.Language, "zh", StringComparison.OrdinalIgnoreCase) ? "zh" :
            string.Equals(_preferences.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : string.Empty;
        _preferences.SearchMode = string.Equals(_preferences.SearchMode, "CPU", StringComparison.OrdinalIgnoreCase) ? "CPU" : "Auto";
        _preferences.MapLossyGpuBatchSize = MapLossyPreferences.GpuBatchSize;
        _preferences.WorkbenchFlags ??= new();
        _preferences.InRunConsoleVerticalPercent = double.IsFinite(_preferences.InRunConsoleVerticalPercent)
            ? Math.Clamp(_preferences.InRunConsoleVerticalPercent, 10, 90) : 50;
        _preferences.InRunConsoleScalePercent = double.IsFinite(_preferences.InRunConsoleScalePercent)
            ? Math.Clamp(_preferences.InRunConsoleScalePercent, 75, 125) : 100;
        _preferences.ReadNotesVersions ??= new();
        // The old global button only acknowledged release notes, not positioning.
        if (!string.IsNullOrWhiteSpace(_preferences.ReadReleaseNotesVersion))
            _preferences.ReadNotesVersions.TryAdd("release", _preferences.ReadReleaseNotesVersion);
        if (_preferences.SearchWorkerBudget is { } budget)
            _preferences.SearchWorkerBudget = ResolveSearchWorkers(budget, System.Environment.ProcessorCount);
        _preferences.LastPage = string.IsNullOrWhiteSpace(_preferences.LastPage)
            ? "Analysis"
            : _preferences.LastPage.Trim();
    }

    private void ClearLegacyAppearance()
    {
        const string fileName = "user_preferences.json";
        if (_unreadableDocuments.Contains(fileName)) return;
        string path = Path.Combine(_directory, fileName);
        if (!File.Exists(path)) return;
        try
        {
            // Retire only this field, preserving all other JSON, including unknown
            // preferences which are not represented by the current typed document.
            var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidDataException("PersistenceDocumentNull");
            string[] fields = document.Select(field => field.Key)
                .Where(key => key.Equals("Appearance", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (fields.Length == 0) return;
            foreach (string field in fields) document.Remove(field);
            SearchPersistenceFile.WriteAtomic(path, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document, _json));
        }
        catch (Exception exception) { RememberDocumentFailure(fileName, exception); }
    }

    private void NormalizePredictorContext()
    {
        _predictorContext.SchemaVersion = SchemaVersion;
        _predictorContext.Ascension = Math.Clamp(_predictorContext.Ascension, SeedPredictionInputLimits.MinimumAscension, SeedPredictionInputLimits.MaximumAscension);
        _predictorContext.PlayersCount = Math.Clamp(_predictorContext.PlayersCount, SeedPredictionInputLimits.MinimumPlayers, SeedPredictionInputLimits.MaximumPlayers);
        _predictorContext.PlayerSlotIndex = Math.Clamp(_predictorContext.PlayerSlotIndex, 0, Math.Max(0, _predictorContext.PlayersCount - 1));
        _predictorContext.Seed ??= string.Empty;
        _predictorContext.PreferredOpeningRouteId ??= string.Empty;
        if (_predictorContext.PreferredOpeningChoiceSlotIndex < -1)
            _predictorContext.PreferredOpeningChoiceSlotIndex = -1;
        _predictorContext.PreferredRewardRouteGroupId ??= string.Empty;
    }
}
