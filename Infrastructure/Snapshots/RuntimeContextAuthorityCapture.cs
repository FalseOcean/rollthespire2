using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Main-thread boundary. It produces immutable primitives/identities only and never
/// retains Godot controls, game Model objects, RunState, Player, or real RNG.
/// </summary>
public static class RuntimeContextAuthorityCapture
{
    private static int _neowCatalogWarning;
    /// <summary>
    /// Production main-thread capture. Identity catalog authority and runtime effect inputs
    /// are recorded independently; missing runtime data never falls back to fixture or all-unlocks data.
    /// </summary>
    public static RuntimeContextAuthoritySnapshot CaptureRuntimeReadOnly(
        IRuntimeProfile profile,
        string rawSeed,
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        int playersCount = 1,
        int playerSlotIndex = 0,
        WorldGameMode predictionGameMode = WorldGameMode.Unknown,
        PredictionGameModeAuthority predictionGameModeAuthority = PredictionGameModeAuthority.Unknown,
        object? explicitUnlockState = null,
        IReadOnlyList<ModelKey>? orderedCharacters = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RuntimeEffectSnapshotCaptureResult captured = explicitUnlockState is not null
            ? ReflectionNeowEffectSnapshotAdapter.Capture(profile, rawSeed, character, ascension, playersCount,
                playerSlotIndex, RuntimeProfileId.Beta111, gameVersion, new(true, true), explicitUnlockState)
            : NeowEffectRuntimeSnapshotCapture.Capture(
            profile,
            rawSeed,
            character,
            ascension,
            gameVersion,
            playersCount,
            playerSlotIndex);
        var worldAuthority = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(
            profile,
            rawSeed,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            noRunModifiers: true,
            captured.EffectAuthority,
            predictionGameMode,
            predictionGameModeAuthority,
            gameVersion, explicitUnlockState: explicitUnlockState, orderedCharacters: orderedCharacters);

        bool vanilla = character.IsKnownVanilla;
        bool? runtimeNeowCatalogExact = ReflectionNeowEffectSnapshotAdapter.CaptureVanillaNeowCatalogExact(out string neowCatalogEvidence);
        if (runtimeNeowCatalogExact != true && Interlocked.Exchange(ref _neowCatalogWarning,1)==0)
            Bootstrap.RuntimeLog.TryBackgroundWarning($"runtimeNeowCatalogAuthority={runtimeNeowCatalogExact?.ToString() ?? "Unknown"};evidence={neowCatalogEvidence};scope=Neow;noInventedCatalog=true");
        bool? vanillaNeowCatalogExact = runtimeNeowCatalogExact;
        bool unlockCaptured = !string.IsNullOrWhiteSpace(captured.UnlockFingerprint);
        bool exactRuntimeCharacterAuthority =
            vanilla &&
            unlockCaptured &&
            captured.CharacterResolution == IdentityResolutionStatus.Exact &&
            SourceAuthorityRules.SupportsExactIdentity(captured.UnlockAuthority);
        SourceAuthority authority = exactRuntimeCharacterAuthority
            ? SourceAuthority.OfficialRuntimeExact
            : vanilla
                ? SourceAuthority.Incomplete
                : captured.UnlockAuthority == SourceAuthority.Unknown
                    ? SourceAuthority.ModdedRuntimeBestEffort
                    : captured.UnlockAuthority;
        SnapshotCompleteness completeness = unlockCaptured
            ? captured.UnlockCompleteness
            : SnapshotCompleteness.Missing;
        IdentityResolutionStatus resolution = captured.CharacterResolution;

        return new RuntimeContextAuthoritySnapshot(
            character,
            ascension,
            playerSlotIndex,
            playersCount,
            noRunModifiers: true,
            vanillaNeowCatalogExact: vanillaNeowCatalogExact,
            allCharacterCardPoolsUnlocked: captured.AllCharacterCardPoolsUnlocked,
            unlockedCommonCards: captured.UnlockedCommonCards,
            unlockedUncommonCards: captured.UnlockedUncommonCards,
            gameVersion,
            profile.ProfileId,
            isVanilla: vanilla,
            sourceModId: vanilla ? null : "runtime-character",
            sourceAssembly: vanilla ? "sts2" : null,
            authority,
            completeness,
            resolution,
            captured.EffectAuthority,
            worldAuthority,
            predictionGameMode,
            predictionGameModeAuthority);
    }

    /// <summary>
    /// Attaches a normal-flow, main-thread Beta109 world DTO capture to an
    /// existing analysis authority. This method never reads or advances game RNG.
    /// </summary>
    public static RuntimeContextAuthoritySnapshot AttachModernWorldCapture(
        RuntimeContextAuthoritySnapshot authority,
        Beta109WorldGenerationSnapshot generation,
        SourceAuthority worldSourceAuthority,
        SnapshotCompleteness worldCompleteness,
        DateTimeOffset capturedAtUtc,
        string diagnosticCode)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!RuntimeProfilePolicies.IsModernCore(authority.ProfileId) ||
            generation.Profile != authority.ProfileId)
            throw new InvalidOperationException("ModernWorldCaptureAuthorityProfileMismatch");
        if (authority.Character.CharacterKey != generation.CharacterKey ||
            authority.Ascension != generation.Ascension ||
            authority.PlayersCount != generation.PlayerCount)
        {
            throw new InvalidOperationException("ModernWorldCaptureAuthorityContextMismatch");
        }

        WorldAuthoritySnapshot world = Beta109WorldRuntimeCaptureSealer.Seal(
            generation,
            worldSourceAuthority,
            worldCompleteness,
            capturedAtUtc,
            diagnosticCode);
        return authority.WithWorldAuthority(world);
    }

    [Obsolete("Use AttachModernWorldCapture. Beta109 is a historical donor only.")]
    public static RuntimeContextAuthoritySnapshot AttachBeta109WorldCapture(
        RuntimeContextAuthoritySnapshot authority,
        Beta109WorldGenerationSnapshot generation,
        SourceAuthority worldSourceAuthority,
        SnapshotCompleteness worldCompleteness,
        DateTimeOffset capturedAtUtc,
        string diagnosticCode) => AttachModernWorldCapture(
            authority,
            generation,
            worldSourceAuthority,
            worldCompleteness,
            capturedAtUtc,
            diagnosticCode);

    public static RuntimeContextAuthoritySnapshot CaptureAuditedVanillaAllUnlocks(
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        RuntimeProfileId profileId,
        int playersCount = 1,
        int playerSlotIndex = 0) =>
        new(
            character,
            ascension,
            playerSlotIndex,
            playersCount,
            noRunModifiers: true,
            vanillaNeowCatalogExact: true,
            allCharacterCardPoolsUnlocked: true,
            unlockedCommonCards: 4,
            unlockedUncommonCards: 2,
            gameVersion,
            profileId,
            isVanilla: true,
            sourceModId: null,
            sourceAssembly: "sts2",
            SourceAuthority.AuditedStaticExact,
            SnapshotCompleteness.Complete,
            IdentityResolutionStatus.Exact);

    public static RuntimeContextAuthoritySnapshot CaptureModdedRuntimeBestEffort(
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        RuntimeProfileId profileId,
        string sourceModId,
        string? sourceAssembly = null,
        int playersCount = 1,
        int playerSlotIndex = 0) =>
        new(
            character,
            ascension,
            playerSlotIndex,
            playersCount,
            noRunModifiers: true,
            vanillaNeowCatalogExact: true,
            allCharacterCardPoolsUnlocked: null,
            unlockedCommonCards: null,
            unlockedUncommonCards: null,
            gameVersion,
            profileId,
            isVanilla: false,
            sourceModId,
            sourceAssembly,
            SourceAuthority.ModdedRuntimeBestEffort,
            SnapshotCompleteness.Partial,
            IdentityResolutionStatus.Exact);

    public static RuntimeContextAuthoritySnapshot CaptureUnknown(
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        RuntimeProfileId profileId,
        int playersCount = 1,
        int playerSlotIndex = 0) =>
        new(
            character,
            ascension,
            playerSlotIndex,
            playersCount,
            noRunModifiers: null,
            vanillaNeowCatalogExact: null,
            allCharacterCardPoolsUnlocked: null,
            unlockedCommonCards: null,
            unlockedUncommonCards: null,
            gameVersion,
            profileId,
            isVanilla: character.IsKnownVanilla,
            sourceModId: null,
            sourceAssembly: null,
            SourceAuthority.Unknown,
            SnapshotCompleteness.Missing,
            IdentityResolutionStatus.Unknown);
    public static RuntimeContextAuthoritySnapshot CaptureAmbiguous(
        CharacterIdentity character,
        int ascension,
        string gameVersion,
        RuntimeProfileId profileId,
        string? sourceModId = null,
        string? sourceAssembly = null,
        int playersCount = 1,
        int playerSlotIndex = 0) =>
        new(
            character,
            ascension,
            playerSlotIndex,
            playersCount,
            noRunModifiers: true,
            vanillaNeowCatalogExact: true,
            allCharacterCardPoolsUnlocked: null,
            unlockedCommonCards: null,
            unlockedUncommonCards: null,
            gameVersion,
            profileId,
            isVanilla: false,
            sourceModId,
            sourceAssembly,
            SourceAuthority.Ambiguous,
            SnapshotCompleteness.Partial,
            IdentityResolutionStatus.Ambiguous);

}
