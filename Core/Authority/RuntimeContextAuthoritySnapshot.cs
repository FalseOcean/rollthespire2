using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Authority;

public enum PredictionGameModeAuthority
{
    Unknown,
    ExplicitRequest,
    RuntimeValidation
}

/// <summary>
/// Immutable authority for one character/ascension/player context, shared by
/// Filter, Predictor and standalone replay. Distinct from the global runtime
/// catalog/unlock snapshot in Core.Authority.Runtime.
/// </summary>
public sealed class RuntimeContextAuthoritySnapshot
{
    public RuntimeContextAuthoritySnapshot(
        CharacterIdentity character,
        int ascension,
        int playerSlotIndex,
        int playersCount,
        bool? noRunModifiers,
        bool? vanillaNeowCatalogExact,
        bool? allCharacterCardPoolsUnlocked,
        int? unlockedCommonCards,
        int? unlockedUncommonCards,
        string gameVersion,
        RuntimeProfileId profileId,
        bool isVanilla,
        string? sourceModId,
        string? sourceAssembly,
        SourceAuthority sourceAuthority,
        SnapshotCompleteness completeness,
        IdentityResolutionStatus resolutionStatus,
        NeowEffectAuthoritySnapshot? effectAuthority = null,
        WorldAuthoritySnapshot? worldAuthority = null,
        WorldGameMode predictionGameMode = WorldGameMode.Unknown,
        PredictionGameModeAuthority predictionGameModeAuthority = PredictionGameModeAuthority.Unknown)
    {
        Character = character;
        Ascension = ascension;
        PlayerSlotIndex = playerSlotIndex;
        PlayersCount = playersCount;
        NoRunModifiers = noRunModifiers;
        VanillaNeowCatalogExact = vanillaNeowCatalogExact;
        AllCharacterCardPoolsUnlocked = allCharacterCardPoolsUnlocked;
        UnlockedCommonCards = unlockedCommonCards;
        UnlockedUncommonCards = unlockedUncommonCards;
        GameVersion = gameVersion ?? string.Empty;
        ProfileId = profileId;
        IsVanilla = isVanilla;
        SourceModId = string.IsNullOrWhiteSpace(sourceModId) ? null : sourceModId;
        SourceAssembly = string.IsNullOrWhiteSpace(sourceAssembly) ? null : sourceAssembly;
        SourceAuthority = sourceAuthority;
        Completeness = completeness;
        ResolutionStatus = resolutionStatus;
        EffectAuthority = effectAuthority;
        WorldAuthority = worldAuthority;
        PredictionGameMode = predictionGameMode;
        PredictionGameModeAuthority = predictionGameModeAuthority;
        UnlockSnapshotFingerprint = SnapshotFingerprint.BuildUnlockSnapshot(
            character,
            ascension,
            playerSlotIndex,
            playersCount,
            allCharacterCardPoolsUnlocked,
            unlockedCommonCards,
            unlockedUncommonCards,
            sourceAuthority,
            completeness);
        CatalogFingerprint = SnapshotFingerprint.BuildCatalog(
            profileId,
            GameVersion,
            vanillaNeowCatalogExact,
            isVanilla,
            SourceModId,
            SourceAssembly);
    }

    public CharacterIdentity Character { get; }
    public int Ascension { get; }
    public int PlayerSlotIndex { get; }
    public int PlayersCount { get; }
    public bool? NoRunModifiers { get; }
    public bool? VanillaNeowCatalogExact { get; }
    public bool? AllCharacterCardPoolsUnlocked { get; }
    public int? UnlockedCommonCards { get; }
    public int? UnlockedUncommonCards { get; }
    public string GameVersion { get; }
    public GameVersionIdentity ExactGameVersionIdentity => RolltheSpire2.Compatibility.GameVersionIdentity.From(GameVersion);
    public RuntimeProfileId ProfileId { get; }
    public bool IsVanilla { get; }
    public bool UsesBestEffortModel => ProfileId == RuntimeProfileId.Beta111 &&
        (!IsVanilla || EffectAuthority?.UsesBestEffortModel == true);
    public bool CanUseCurrentModel => IsVanilla || UsesBestEffortModel;
    public string? SourceModId { get; }
    public string? SourceAssembly { get; }
    public SourceAuthority SourceAuthority { get; }
    public SnapshotCompleteness Completeness { get; }
    public IdentityResolutionStatus ResolutionStatus { get; }
    public NeowEffectAuthoritySnapshot? EffectAuthority { get; }
    public WorldAuthoritySnapshot? WorldAuthority { get; }
    public WorldGameMode PredictionGameMode { get; }
    public PredictionGameModeAuthority PredictionGameModeAuthority { get; }
    public string UnlockSnapshotFingerprint { get; }
    public string CatalogFingerprint { get; }
    public string EffectSnapshotFingerprint => EffectAuthority?.SnapshotFingerprint ?? string.Empty;
    public string WorldSnapshotFingerprint => WorldAuthority?.SnapshotFingerprint ?? string.Empty;

    public RuntimeContextAuthoritySnapshot WithEffectAuthority(NeowEffectAuthoritySnapshot? effectAuthority) => new(
        Character,
        Ascension,
        PlayerSlotIndex,
        PlayersCount,
        NoRunModifiers,
        VanillaNeowCatalogExact,
        AllCharacterCardPoolsUnlocked,
        UnlockedCommonCards,
        UnlockedUncommonCards,
        GameVersion,
        ProfileId,
        IsVanilla,
        SourceModId,
        SourceAssembly,
        SourceAuthority,
        Completeness,
        ResolutionStatus,
        effectAuthority,
        WorldAuthority,
        PredictionGameMode,
        PredictionGameModeAuthority);

    public RuntimeContextAuthoritySnapshot WithWorldAuthority(WorldAuthoritySnapshot? worldAuthority) => new(
        Character,
        Ascension,
        PlayerSlotIndex,
        PlayersCount,
        NoRunModifiers,
        VanillaNeowCatalogExact,
        AllCharacterCardPoolsUnlocked,
        UnlockedCommonCards,
        UnlockedUncommonCards,
        GameVersion,
        ProfileId,
        IsVanilla,
        SourceModId,
        SourceAssembly,
        SourceAuthority,
        Completeness,
        ResolutionStatus,
        EffectAuthority,
        worldAuthority,
        PredictionGameMode,
        PredictionGameModeAuthority);

    public bool HasCoreNoModifierCatalogAuthority =>
        (ResolutionStatus == IdentityResolutionStatus.Exact || UsesBestEffortModel) &&
        NoRunModifiers == true &&
        VanillaNeowCatalogExact == true;


    /// <summary>
    /// Exact authority for the Modern Neow identity generator only. Character origin is
    /// intentionally not part of this proof: the live Neow option catalog and the copied
    /// eligibility inputs are the relevant facts.
    /// </summary>
    public bool HasExactModernNeowIdentityInputs =>
        NoRunModifiers == true &&
        VanillaNeowCatalogExact == true &&
        AllCharacterCardPoolsUnlocked.HasValue &&
        IsScrollBoxesAllowed.HasValue;

    public bool IsBeta110NeowIdentityAuthorityExact =>
        IsBeta110Context &&
        HasExactModernNeowIdentityInputs &&
        Beta110ValidationAuthority.NeowAccepted &&
        VersionResolution.RuntimeAccepted;

    public bool IsBeta111NeowIdentityAuthorityExact =>
        IsBeta111Context &&
        HasExactModernNeowIdentityInputs &&
        Beta111ValidationAuthority.RuntimeAccepted &&
        VersionResolution.AllowsProductionSearch &&
        !VersionResolution.Blocks(CompatibilityDomainMask.Neow);

    public RuntimeVersionResolution VersionResolution =>
        RuntimeVersionCompatibility.Resolve(GameVersion);

    public bool IsCompatibilityFallback => VersionResolution.IsFallback;

    public bool IsStable107Context =>
        ProfileId == RuntimeProfileId.Stable107 &&
        RuntimeVersionCompatibility.IsStable107(GameVersion);

    /// <summary>
    /// Historical donor context. This can authorize direct fixture/audit replay, but
    /// does not make Beta109 selectable through the production RuntimeProfileRegistry.
    /// </summary>
    public bool IsBeta109Context =>
        ProfileId == RuntimeProfileId.Beta109 &&
        (RuntimeVersionCompatibility.IsBeta109_0(GameVersion) || RuntimeVersionCompatibility.IsBeta109_1(GameVersion));

    public bool IsAuditedBeta109Context =>
        ProfileId == RuntimeProfileId.Beta109 &&
        (RuntimeVersionCompatibility.IsBeta109_0(GameVersion) || RuntimeVersionCompatibility.IsBeta109_1(GameVersion));

    public bool IsBeta110Context =>
        ProfileId == RuntimeProfileId.Beta110 &&
        RuntimeVersionCompatibility.UsesBeta110Profile(GameVersion);

    public bool IsAuditedBeta110Context =>
        ProfileId == RuntimeProfileId.Beta110 &&
        RuntimeVersionCompatibility.IsBeta110(GameVersion);

    public bool IsBeta111Context =>
        ProfileId == RuntimeProfileId.Beta111 &&
        RuntimeVersionCompatibility.UsesBeta111Profile(GameVersion);

    public bool IsAuditedBeta111SourceContext =>
        ProfileId == RuntimeProfileId.Beta111 &&
        RuntimeVersionCompatibility.IsBeta111(GameVersion);

    public bool CanAnalyzeStable107 =>
        IsStable107Context &&
        HasCoreNoModifierCatalogAuthority &&
        PlayersCount == 1 &&
        PlayerSlotIndex == 0 &&
        AllCharacterCardPoolsUnlocked == true &&
        UnlockedCommonCards >= 4 &&
        UnlockedUncommonCards >= 2;

    public bool IsExactStable107 =>
        CanAnalyzeStable107 &&
        Completeness == SnapshotCompleteness.Complete &&
        ResolutionStatus == IdentityResolutionStatus.Exact &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority);

    public bool CanAnalyzeBeta109 =>
        IsBeta109Context &&
        HasCoreNoModifierCatalogAuthority &&
        PlayersCount >= 1 &&
        PlayerSlotIndex >= 0 &&
        PlayerSlotIndex < PlayersCount &&
        AllCharacterCardPoolsUnlocked.HasValue &&
        UnlockedCommonCards.HasValue &&
        UnlockedUncommonCards.HasValue;

    /// <summary>
    /// Audited game-version parity authority. Compatibility fallback never satisfies
    /// this property even when the selected profile can produce a deterministic result.
    /// </summary>
    public bool IsExactBeta109 =>
        IsAuditedBeta109Context &&
        CanAnalyzeBeta109 &&
        Completeness == SnapshotCompleteness.Complete &&
        ResolutionStatus == IdentityResolutionStatus.Exact &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority);

    /// <summary>
    /// Deterministic projection authority under the selected Beta109 ruleset.
    /// In fallback mode this is Exact only relative to the reference profile and must
    /// be accompanied by the persistent unverified-version warning.
    /// </summary>
    public bool IsBeta109ProjectionAuthorityExact =>
        CanAnalyzeBeta109 &&
        Completeness == SnapshotCompleteness.Complete &&
        ResolutionStatus == IdentityResolutionStatus.Exact &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority);

    public bool CanAnalyzeBeta110 =>
        IsBeta110Context &&
        HasCoreNoModifierCatalogAuthority &&
        PlayersCount >= 1 &&
        PlayerSlotIndex >= 0 &&
        PlayerSlotIndex < PlayersCount &&
        AllCharacterCardPoolsUnlocked.HasValue &&
        UnlockedCommonCards.HasValue &&
        UnlockedUncommonCards.HasValue &&
        EffectAuthority?.CapturedProfileId == RuntimeProfileId.Beta110 &&
        !string.IsNullOrWhiteSpace(EffectAuthority.CatalogFingerprint);

    /// <summary>
    /// Runtime DTO and source-audit authority for deterministic Beta110 projection.
    /// Product acceptance is tracked separately by Beta110ValidationAuthority.
    /// </summary>
    public bool IsBeta110ProjectionAuthorityExact =>
        CanAnalyzeBeta110 &&
        Completeness == SnapshotCompleteness.Complete &&
        ResolutionStatus == IdentityResolutionStatus.Exact &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority) &&
        EffectAuthority?.HasExactFoundation == true;

    public bool CanAnalyzeBeta111 =>
        IsBeta111Context &&
        HasCoreNoModifierCatalogAuthority &&
        PlayersCount >= 1 &&
        PlayerSlotIndex >= 0 &&
        PlayerSlotIndex < PlayersCount &&
        AllCharacterCardPoolsUnlocked.HasValue &&
        UnlockedCommonCards.HasValue &&
        UnlockedUncommonCards.HasValue &&
        EffectAuthority?.CapturedProfileId == RuntimeProfileId.Beta111 &&
        !string.IsNullOrWhiteSpace(EffectAuthority.CatalogFingerprint);

    public bool IsBeta111ProjectionAuthorityExact =>
        CanAnalyzeBeta111 &&
        (UsesBestEffortModel || Completeness == SnapshotCompleteness.Complete &&
        ResolutionStatus == IdentityResolutionStatus.Exact &&
        SourceAuthorityRules.SupportsExactIdentity(SourceAuthority)) &&
        EffectAuthority?.HasExactFoundation == true;

    public bool IsProductionBetaProjectionAuthorityExact => ProfileId switch
    {
        RuntimeProfileId.Beta110 => IsBeta110ProjectionAuthorityExact,
        RuntimeProfileId.Beta111 => IsBeta111ProjectionAuthorityExact,
        _ => false
    };

    public bool IsExactBeta110 =>
        IsAuditedBeta110Context &&
        IsBeta110ProjectionAuthorityExact &&
        Beta110ValidationAuthority.RuntimeAccepted;

    public bool IsExactBeta111 =>
        IsAuditedBeta111SourceContext &&
        IsBeta111ProjectionAuthorityExact &&
        Beta111ValidationAuthority.RuntimeAccepted;

    public string RuntimeAuthorityId => RuntimeProfilePolicies.AuthorityId(ProfileId);
    public string RuntimeAuditFingerprint
    {
        get
        {
            RuntimeVersionResolution resolution = RuntimeVersionCompatibility.Resolve(GameVersion);
            return resolution.IsAuditedCompatible
                ? RuntimeProfilePolicies.AuditFingerprint(ProfileId)
                : $"unverified-game-version:{resolution.GameVersionIdentity};reuse={resolution.ReferenceVersion};semantic={resolution.SemanticProfileId}";
        }
    }
    public string RuntimeCardCatalogFingerprint => EffectAuthority?.CatalogFingerprint ?? string.Empty;

    public bool IsKaleidoscopeAllowed => AllCharacterCardPoolsUnlocked == true;

    public bool? IsScrollBoxesAllowed =>
        !UnlockedCommonCards.HasValue || !UnlockedUncommonCards.HasValue
            ? null
            : UnlockedCommonCards.Value >= 4 && UnlockedUncommonCards.Value >= 2;
}
