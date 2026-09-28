using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Capabilities;

internal enum Ui1CapabilityState
{
    Supported,
    NotMigrated,
    UnsupportedProfile,
    Partial,
    Unknown,
    Error
}

internal sealed record Ui1CapabilityModel(
    Ui1CapabilityState State,
    string TitleKey,
    string MessageKey,
    bool AllowsInteraction = false);

/// <summary>
/// Explicit product-surface capability declaration for this build. Pages consume this
/// catalog instead of reflecting over analyzers or guessing from missing data.
/// </summary>
internal static class Ui1CapabilityCatalog
{
    private static readonly Ui1CapabilityModel SupportedAnalysis = new(
        Ui1CapabilityState.Supported,
        Ui1TextKey.CapabilitySupported,
        Ui1TextKey.CapabilityAnalysisSupported,
        AllowsInteraction: true);

    private static readonly Ui1CapabilityModel SupportedSearch = new(
        Ui1CapabilityState.Supported,
        Ui1TextKey.CapabilitySupported,
        Ui1TextKey.CapabilitySearchSupported,
        AllowsInteraction: true);

    private static readonly Ui1CapabilityModel NotMigratedEvents = new(
        Ui1CapabilityState.NotMigrated,
        Ui1TextKey.CapabilityNotMigrated,
        Ui1TextKey.CapabilityEventCatalogNotMigrated);

    private static readonly Ui1CapabilityModel UnknownSaveStatus = new(
        Ui1CapabilityState.Unknown,
        Ui1TextKey.CapabilityUnknown,
        Ui1TextKey.CapabilitySaveSnapshotUnavailable);

    private static readonly Ui1CapabilityModel PartialAdvanced = new(
        Ui1CapabilityState.Partial,
        Ui1TextKey.CapabilityPartial,
        Ui1TextKey.CapabilityAdvancedSurfaceOnly,
        AllowsInteraction: true);

    private static readonly Ui1CapabilityModel SupportedDeveloperNotes = new(
        Ui1CapabilityState.Supported,
        Ui1TextKey.CapabilitySupported,
        Ui1TextKey.CapabilitySettingsSupported,
        AllowsInteraction: true);

    private static readonly Ui1CapabilityModel SupportedSettings = new(
        Ui1CapabilityState.Supported,
        Ui1TextKey.CapabilitySupported,
        Ui1TextKey.CapabilitySettingsSupported,
        AllowsInteraction: true);

    private static readonly Ui1CapabilityModel NotMigratedAnalysisModule = new(
        Ui1CapabilityState.NotMigrated,
        Ui1TextKey.CapabilityNotMigrated,
        Ui1TextKey.CapabilityAnalysisModuleNotMigrated);

    public static Ui1CapabilityModel ForPage(AppPageKey pageKey) => pageKey switch
    {
        AppPageKey.Analysis => SupportedAnalysis,
        AppPageKey.Search => SupportedSearch,
        AppPageKey.Events => NotMigratedEvents,
        AppPageKey.SaveStatus => UnknownSaveStatus,
        AppPageKey.Advanced => PartialAdvanced,
        AppPageKey.DeveloperNotes => SupportedDeveloperNotes,
        AppPageKey.Settings => SupportedSettings,
        _ => new Ui1CapabilityModel(Ui1CapabilityState.Error, Ui1TextKey.CapabilityError, Ui1TextKey.CapabilityUnexpected)
    };

    public static Ui1CapabilityModel NeowIdentityModule { get; } = new(
        Ui1CapabilityState.Supported,
        Ui1TextKey.CapabilitySupported,
        Ui1TextKey.CapabilityNeowIdentitySupported,
        AllowsInteraction: true);

    public static Ui1CapabilityModel WorldSeedDomainModule { get; } = new(
        Ui1CapabilityState.Partial,
        Ui1TextKey.CapabilityPartial,
        Ui1TextKey.CapabilityWorldAuthorityPartial,
        AllowsInteraction: true);

    public static Ui1CapabilityModel EventSequenceModule { get; } = new(
        Ui1CapabilityState.Partial,
        Ui1TextKey.CapabilityPartial,
        Ui1TextKey.CapabilityEventSequenceSourceCandidate,
        AllowsInteraction: true);

    public static Ui1CapabilityModel RelicSequenceModule { get; } = new(
        Ui1CapabilityState.Partial,
        Ui1TextKey.CapabilityPartial,
        Ui1TextKey.CapabilityRelicSequenceSourceCandidate,
        AllowsInteraction: true);

    public static Ui1CapabilityModel NormalCombatRewardSequenceModule { get; } = new(
        Ui1CapabilityState.Partial,
        Ui1TextKey.CapabilityPartial,
        Ui1TextKey.CapabilityNormalCombatRewardSourceCandidate,
        AllowsInteraction: true);

    public static Ui1CapabilityModel FutureAnalysisModule => NotMigratedAnalysisModule;

    public static Ui1CapabilityModel SearchBackend => SupportedSearch;

    public static Ui1CapabilityModel EventCatalog => NotMigratedEvents;

    public static Ui1CapabilityModel SaveSnapshot => UnknownSaveStatus;

    public static Ui1CapabilityModel AdvancedFeature => new(
        Ui1CapabilityState.NotMigrated,
        Ui1TextKey.CapabilityNotMigrated,
        Ui1TextKey.CapabilityAdvancedFeatureNotMigrated);

    public static string BadgeSymbol(Ui1CapabilityState state) => state switch
    {
        Ui1CapabilityState.Supported => "✓",
        Ui1CapabilityState.Partial => "◐",
        Ui1CapabilityState.Unknown => "?",
        Ui1CapabilityState.UnsupportedProfile => "⊘",
        Ui1CapabilityState.Error => "!",
        _ => "🚧"
    };
}
