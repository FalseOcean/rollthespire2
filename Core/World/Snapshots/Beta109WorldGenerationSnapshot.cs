using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.World.Snapshots;

public enum Beta109OldSeedBranchStatus
{
    NewSeedHashed,
    OldSeedParsed,
    UnsupportedOldSeed,
    Unknown
}

public enum Beta109RunSeedHashKind
{
    ModernXxHash64,
    LegacyOldHash32,
    Unknown
}

public enum WorldGameMode
{
    Singleplayer,
    Multiplayer,
    Tutorial,
    Test,
    Other,
    Unknown
}

public enum SelectedActProvenance
{
    CapturedBeginRun,
    ReconstructedActSelection,
    SyntheticFixture,
    Missing
}

public enum Beta109ActSelectionMode
{
    RandomNextItem,
    DeterministicFirst,
    Unsupported
}

public enum Beta109FixtureValidationStatus
{
    PendingRealGame,
    SyntheticOnly,
    VerifiedRealGame,
    AssumedCompatibleLatestKnown
}

public enum Beta109UpFrontPrefixAuthorityKind
{
    ReplayFromRunStart,
    ExactCheckpoint,
    Missing
}

public enum Beta109UpFrontReplayOrigin
{
    RunStartBeforeRelicInitialization,
    BeforeGenerateRooms,
    Unknown
}

public enum Beta109HookDecision
{
    Allow,
    Deny,
    Unknown
}

public enum Beta109AncientOptionSupport
{
    PureChoice,
    PureShuffleTake,
    PureBranch,
    DeckFactPredicate,
    PureInstanceProjection,
    UnsupportedHookedObtain,
    Unknown
}

public enum Beta109RoomType
{
    Event,
    Weak,
    Regular,
    Elite,
    Boss,
    Other
}

public sealed record Beta109RngStateSnapshot(
    ulong S0,
    ulong S1,
    ulong S2,
    ulong S3,
    int CallCount);

public sealed record Beta109UpFrontPrefixSnapshot(
    Beta109UpFrontPrefixAuthorityKind Kind,
    Beta109UpFrontReplayOrigin Origin,
    Beta109RngStateSnapshot? Checkpoint,
    bool PriorInputsExact,
    string EvidenceCode)
{
    public static Beta109UpFrontPrefixSnapshot Missing(string evidenceCode) => new(
        Beta109UpFrontPrefixAuthorityKind.Missing,
        Beta109UpFrontReplayOrigin.Unknown,
        null,
        false,
        evidenceCode);
}

public sealed record Beta109RelicBucketEntrySnapshot(
    ModelKey RelicKey,
    bool IsAllowedInShops,
    bool ShopEligibilityExact);

public sealed record Beta109RelicBucketSnapshot(
    string BucketId,
    IReadOnlyList<ModelKey> OrderedRelics,
    bool OrderExact)
{
    public IReadOnlyList<Beta109RelicBucketEntrySnapshot> OrderedEntries { get; init; } =
        Array.Empty<Beta109RelicBucketEntrySnapshot>();

    public bool HasExactShopEligibility =>
        OrderedEntries.Count == OrderedRelics.Count &&
        OrderedEntries.All(entry => entry.ShopEligibilityExact);
}

public sealed record Beta109EncounterEntrySnapshot(
    ModelKey EncounterKey,
    Beta109RoomType RoomType,
    bool IsWeak,
    IReadOnlyList<string> Tags,
    double Weight = 1d,
    IReadOnlyList<ModelKey>? SharesTagsWith = null,
    bool TagComparisonExact = false,
    int SourceOrdinal = -1,
    int ReferenceIdentityId = -1,
    IReadOnlyList<int>? SharesTagsWithSourceOrdinals = null,
    bool TagIdentityExact = false,
    bool ClassificationExact = false,
    bool ReferenceIdentityExact = false,
    bool IsOfficialVanilla = false)
{
    public IReadOnlyList<ModelKey> ExactTagConflicts => SharesTagsWith ?? Array.Empty<ModelKey>();
    public IReadOnlyList<int> ExactTagConflictSourceOrdinals =>
        SharesTagsWithSourceOrdinals ?? Array.Empty<int>();
}


public sealed record Beta109EventEpochSnapshot(
    string EpochId,
    IReadOnlyList<ModelKey> OrderedMemberKeys,
    bool MembershipExact,
    bool IsRevealed,
    bool RevealFactExact);

public sealed record Beta109EventCatalogAuthoritySnapshot(
    IReadOnlyList<ModelKey> OrderedSharedEventsRaw,
    bool SharedRawOrderExact,
    IReadOnlyList<Beta109EventEpochSnapshot> EpochsInFilterOrder,
    bool EpochFilterOrderExact,
    string EvidenceCode)
{
    public static Beta109EventCatalogAuthoritySnapshot Missing(string evidenceCode) => new(
        Array.Empty<ModelKey>(),
        false,
        Array.Empty<Beta109EventEpochSnapshot>(),
        false,
        evidenceCode);

    public bool EpochMembershipExact =>
        EpochFilterOrderExact &&
        EpochsInFilterOrder.Count == 3 &&
        EpochsInFilterOrder.All(epoch => epoch.MembershipExact);

    public bool EpochRevealFactsExact =>
        EpochFilterOrderExact &&
        EpochsInFilterOrder.Count == 3 &&
        EpochsInFilterOrder.All(epoch => epoch.RevealFactExact);

    public int CharacterCardPoolCount { get; init; }
    public bool CharacterCardPoolCountExact { get; init; }

    public bool HasExactFilteringAuthority =>
        SharedRawOrderExact &&
        EpochMembershipExact &&
        EpochRevealFactsExact;
}

public sealed record Beta109ActGenerationSnapshot(
    int Act,
    ModelKey ActKey,
    int WeakEncounterSlots,
    int TotalNormalRooms,
    IReadOnlyList<ModelKey> OrderedEvents,
    IReadOnlyList<Beta109EncounterEntrySnapshot> OrderedGenerateAllEncounters,
    IReadOnlyList<ModelKey> OrderedAncients,
    bool CatalogOrderExact,
    bool UnlockFilteringExact,
    bool RoomShapeExact,
    bool GenerateAllEncountersOrderExact = false,
    bool EncounterTagComparerExact = false,
    bool EncounterWeightModelExact = false,
    bool EncounterClassificationExact = false,
    bool EncounterTagIdentityExact = false,
    bool EncounterReferenceIdentityExact = false,
    bool EncounterRetryShapeExact = false,
    int EliteEncounterSlots = 15,
    bool EncounterCatalogOfficialVanilla = false,
    string EncounterAuthorityEvidenceCode = "")
{
    public IReadOnlyList<ModelKey> OrderedRawEvents { get; init; } = OrderedEvents;
    public IReadOnlyList<ModelKey> OrderedEligibleEvents { get; init; } = OrderedEvents;
    public bool RawEventCatalogOrderExact { get; init; }
    public bool EventEpochMembershipExact { get; init; }
    public bool EventEpochRevealFactsExact { get; init; }
    public bool EligibleEventOrderExact { get; init; }

    public bool EventRngConsumptionExact =>
        RawEventCatalogOrderExact &&
        EventEpochMembershipExact &&
        EventEpochRevealFactsExact &&
        EligibleEventOrderExact;

    public IReadOnlyList<Beta109EncounterEntrySnapshot> WeakEncounters =>
        OrderedGenerateAllEncounters.Where(item => item.RoomType == Beta109RoomType.Weak || item.IsWeak).ToArray();

    public IReadOnlyList<Beta109EncounterEntrySnapshot> RegularEncounters =>
        OrderedGenerateAllEncounters.Where(item => item.RoomType == Beta109RoomType.Regular && !item.IsWeak).ToArray();

    public IReadOnlyList<Beta109EncounterEntrySnapshot> EliteEncounters =>
        OrderedGenerateAllEncounters.Where(item => item.RoomType == Beta109RoomType.Elite).ToArray();

    public IReadOnlyList<ModelKey> Bosses =>
        OrderedGenerateAllEncounters.Where(item => item.RoomType == Beta109RoomType.Boss)
            .Select(item => item.EncounterKey).ToArray();

    public bool HasExactGenerationInputs =>
        ActKey.IsValid &&
        CatalogOrderExact &&
        UnlockFilteringExact &&
        RoomShapeExact &&
        GenerateAllEncountersOrderExact &&
        EncounterClassificationExact &&
        EncounterTagIdentityExact &&
        EncounterTagComparerExact &&
        EncounterReferenceIdentityExact &&
        EncounterWeightModelExact &&
        EncounterRetryShapeExact &&
        EncounterCatalogOfficialVanilla &&
        EliteEncounterSlots == 15 &&
        OrderedEvents is not null &&
        WeakEncounters.Count > 0 &&
        RegularEncounters.Count > 0 &&
        EliteEncounters.Count > 0 &&
        Bosses.Count > 0 &&
        OrderedAncients.Count > 0;
}

public sealed record Beta109ActSelectionCandidateSnapshot(
    int SourceActIndex,
    int CandidateOrdinal,
    ModelKey ActKey,
    bool IsDefault,
    bool IsUnlocked,
    bool DiscoveredInSingleplayer,
    bool DeterministicFirstEligible,
    bool PredicateExact,
    string EvidenceCode);

public sealed record Beta109ActSelectionGroupSnapshot(
    int Act,
    IReadOnlyList<ModelKey> EligibleActsInSourceOrder,
    bool EligibilityAndOrderExact,
    Beta109ActSelectionMode SelectionMode = Beta109ActSelectionMode.RandomNextItem,
    IReadOnlyList<Beta109ActSelectionCandidateSnapshot>? CandidatesInSourceOrder = null)
{
    public IReadOnlyList<Beta109ActSelectionCandidateSnapshot> Candidates =>
        CandidatesInSourceOrder ?? Array.Empty<Beta109ActSelectionCandidateSnapshot>();
}

public sealed record Beta109LobbyPlayerSnapshot(
    int Slot,
    ModelKey CharacterKey,
    bool IsRandomCharacter,
    bool Exact);

public sealed record Beta109NamedOptionPoolSnapshot(
    string PoolId,
    IReadOnlyList<ModelKey> OrderedOptions,
    bool OrderExact,
    bool InstanceProjectionExact = true)
{
    public int SourceOrdinal { get; init; } = -1;
    public string FilterPredicateId { get; init; } = string.Empty;
    public bool FilterResultExact { get; init; }
    public bool IsDynamicProjection { get; init; }
}

public sealed record Beta109DeckFactSnapshot(
    int BasicStrikeCount,
    int RemovableCardCount,
    int GoopyEnchantableCount,
    int SwiftEnchantableCount,
    int InstinctEnchantableCount,
    bool Exact);

public sealed record Beta109AncientOptionCatalogSnapshot(
    ModelKey AncientKey,
    string EventIdEntry,
    Beta109AncientOptionSupport Capability,
    IReadOnlyList<Beta109NamedOptionPoolSnapshot> Pools,
    IReadOnlyDictionary<string, string> VariantData,
    bool CatalogExact,
    bool MutableInstanceProjectionExact,
    string CatalogFingerprint)
{
    public IReadOnlyList<ModelKey> Pool(string poolId) => Pools
        .FirstOrDefault(pool => string.Equals(pool.PoolId, poolId, StringComparison.Ordinal))?
        .OrderedOptions ?? Array.Empty<ModelKey>();

    public IReadOnlyList<Beta109NamedOptionPoolSnapshot> PoolsWithPrefix(string prefix) => Pools
        .Where(pool => pool.PoolId.StartsWith(prefix, StringComparison.Ordinal))
        .OrderBy(pool => pool.SourceOrdinal < 0 ? int.MaxValue : pool.SourceOrdinal)
        .ThenBy(pool => pool.PoolId, StringComparer.Ordinal)
        .ToArray();
}

public sealed record Beta109AncientEventContextSnapshot(
    int Act,
    ModelKey AncientKey,
    string EventIdEntry,
    int PlayerSlot,
    bool IsShared,
    int CurrentActIndex,
    ModelKey CharacterKey,
    IReadOnlyList<ModelKey> UnlockedCharacters,
    Beta109DeckFactSnapshot DeckFacts,
    IReadOnlyDictionary<string, bool> BooleanFacts,
    IReadOnlyDictionary<string, int> IntegerFacts,
    Beta109HookDecision HookDecision,
    bool EventContextExact,
    bool DynamicFactsExact,
    bool ModifierFactsExact,
    Beta109AncientOptionCatalogSnapshot? Catalog,
    string ContextFingerprint)
{
    public string RuntimeTypeName { get; init; } = string.Empty;
    public bool UnlockedCharacterSourceOrderExact { get; init; }
    public bool CandidateFromSharedAncientAllocation { get; init; }
    public bool EventIsSharedAuthorityExact { get; init; }
    public string EventIsSharedAuthorityEvidence { get; init; } = string.Empty;
    public ulong EventRngRoot { get; init; }
    public bool EventRngRootExact { get; init; }
    public string EventRngFormulaVersion { get; init; } = string.Empty;
    public string ProjectionPolicy { get; init; } = string.Empty;
    public string AuthorityEvidenceCode { get; init; } = string.Empty;
}

public sealed record Beta109WorldGenerationSnapshot(
    RuntimeProfileId Profile,
    string OriginalSeed,
    string CanonicalSeed,
    ulong ActSelectionRoot,
    bool ActSelectionRootExact,
    ulong RunSeedRoot,
    bool RunSeedRootExact,
    Beta109RunSeedHashKind RunSeedHashKind,
    Beta109OldSeedBranchStatus OldSeedBranchStatus,
    IReadOnlyList<ModelKey> SelectedActs,
    SelectedActProvenance SelectedActProvenance,
    bool SelectedActsExact,
    IReadOnlyList<Beta109ActSelectionGroupSnapshot> ActSelectionGroups,
    bool ActSelectionAuthorityExact,
    bool IsMultiplayer,
    bool IsMultiplayerExact,
    bool TestModeIsOff,
    bool TestModeFactExact,
    bool TutorialBossOverrideWillApply,
    bool TutorialBossOverrideAuthorityExact,
    string Act1OverrideRaw,
    ModelKey? Act1OverrideResolvedKey,
    bool Act1OverrideExact,
    IReadOnlyList<Beta109LobbyPlayerSnapshot> LobbyPlayers,
    IReadOnlyList<ModelKey> AllCharactersInSourceOrder,
    bool RandomCharacterAuthorityExact,
    IReadOnlyList<ModelKey> UnlockedCharacters,
    bool UnlockedCharactersExact,
    ModelKey CharacterKey,
    int Ascension,
    WorldGameMode GameMode,
    bool ModeFactsExact,
    int PlayerCount,
    IReadOnlyList<Beta109ActGenerationSnapshot> OrderedActCatalog,
    IReadOnlyList<ModelKey> SharedEvents,
    bool SharedEventCatalogExact,
    IReadOnlyList<ModelKey> AllSharedAncients,
    bool AllSharedAncientCatalogExact,
    IReadOnlyList<ModelKey> SharedAncients,
    bool SharedAncientCatalogExact,
    bool UnlockFactsExact,
    IReadOnlyList<Beta109RelicBucketSnapshot> SharedRelicBuckets,
    IReadOnlyList<Beta109RelicBucketSnapshot> PlayerRelicBuckets,
    bool RelicInitializationExact,
    Beta109UpFrontPrefixSnapshot UpFrontPrefix,
    IReadOnlyList<Beta109AncientEventContextSnapshot> AncientEventContexts,
    bool NoUnknownHooksOrModifiers,
    bool IsVanilla,
    bool DirectSourceAudited,
    Beta109FixtureValidationStatus FixtureValidationStatus,
    IReadOnlyList<string> VerifiedRealGameFixtureIds,
    string CatalogFingerprint,
    string UnlockFingerprint,
    string GenerationRuleFingerprint,
    string SnapshotFingerprint,
    string CaptureDiagnosticCode)
{
    public WorldGameMode RequestedGameMode { get; init; } = WorldGameMode.Unknown;
    public bool RequestedGameModeExact { get; init; }
    public string GameModeEvidenceCode { get; init; } = string.Empty;
    public Beta109EventCatalogAuthoritySnapshot EventAuthority { get; init; } =
        Beta109EventCatalogAuthoritySnapshot.Missing("MissingEventAuthority");

    public bool SharedRelicPoolOrderExact { get; init; }
    public bool CharacterRelicPoolOrderExact { get; init; }
    /// <summary>
    /// Exactness of the audited Act/room/world-generation implementation surface.
    /// This is intentionally independent from Character provenance: a Mod Character
    /// does not by itself imply custom ActModel.GenerateRooms behavior.
    /// </summary>
    public bool WorldGenerationHooksExact { get; init; }
    public bool RelicRarityAuthorityExact { get; init; }
    public bool RelicShopEligibilityAuthorityExact { get; init; }
    public bool PlayerRelicPoolCompositionExact { get; init; }
    public string RelicAuthorityEvidenceCode { get; init; } = string.Empty;

    public bool CanReconstructSelectedActs
    {
        get
        {
            bool randomCharactersNeedCatalog = LobbyPlayers.Any(player => player.IsRandomCharacter);
            return ActSelectionRootExact &&
                   ActSelectionAuthorityExact &&
                   IsMultiplayerExact &&
                   TestModeFactExact &&
                   Act1OverrideExact &&
                   (!randomCharactersNeedCatalog || RandomCharacterAuthorityExact) &&
                   ActSelectionGroups.Count > 0 &&
                   ActSelectionGroups.All(group =>
                       group.EligibilityAndOrderExact &&
                       group.EligibleActsInSourceOrder.Count > 0 &&
                       group.SelectionMode != Beta109ActSelectionMode.Unsupported);
        }
    }

    public bool HasExactUpFrontPrefix =>
        UpFrontPrefix.PriorInputsExact &&
        (UpFrontPrefix.Kind switch
        {
            Beta109UpFrontPrefixAuthorityKind.ExactCheckpoint =>
                UpFrontPrefix.Origin == Beta109UpFrontReplayOrigin.BeforeGenerateRooms &&
                UpFrontPrefix.Checkpoint is not null,
            Beta109UpFrontPrefixAuthorityKind.ReplayFromRunStart =>
                UpFrontPrefix.Origin == Beta109UpFrontReplayOrigin.RunStartBeforeRelicInitialization &&
                RelicInitializationExact &&
                SharedRelicPoolOrderExact &&
                CharacterRelicPoolOrderExact &&
                RelicRarityAuthorityExact &&
                PlayerRelicPoolCompositionExact &&
                SharedRelicBuckets.Count > 0 &&
                PlayerRelicBuckets.Count > 0 &&
                SharedRelicBuckets.All(bucket => bucket.OrderExact) &&
                PlayerRelicBuckets.All(bucket => bucket.OrderExact),
            _ => false
        });

    /// <summary>
    /// Exact immutable inputs required by the CPU World Fast planner. Unlike
    /// HasExactReplayInputs, this authority intentionally does not require a
    /// precomputed SelectedActs result: the Fast stage reconstructs Act
    /// selection from ActSelectionRoot and ActSelectionGroups for every Seed.
    /// Production prediction keeps its stricter selected-act authority.
    /// </summary>
    public bool HasExactWorldFastPlanInputs =>
        RuntimeProfilePolicies.IsModernCore(Profile) &&
        DirectSourceAudited &&
        ActSelectionRootExact &&
        RunSeedRootExact &&
        RunSeedHashKind == Beta109RunSeedHashKind.ModernXxHash64 &&
        OldSeedBranchStatus == Beta109OldSeedBranchStatus.NewSeedHashed &&
        CanReconstructSelectedActs &&
        ModeFactsExact &&
        IsMultiplayerExact &&
        TestModeFactExact &&
        Act1OverrideExact &&
        GameMode == WorldGameMode.Singleplayer &&
        !IsMultiplayer &&
        PlayerCount == 1 &&
        LobbyPlayers.Count == 1 &&
        LobbyPlayers.All(player => player.Exact && !player.IsRandomCharacter) &&
        SharedEventCatalogExact &&
        EventAuthority.HasExactFilteringAuthority &&
        AllSharedAncientCatalogExact &&
        SharedAncientCatalogExact &&
        UnlockFactsExact &&
        HasExactUpFrontPrefix &&
        OrderedActCatalog.Count > 0 &&
        ActSelectionGroups
            .SelectMany(group => group.EligibleActsInSourceOrder)
            .Distinct(ModelKeyComparer.Instance)
            .All(key => OrderedActCatalog.Any(act => act.ActKey == key && act.HasExactGenerationInputs)) &&
        WorldGenerationHooksExact &&
        !string.IsNullOrWhiteSpace(CatalogFingerprint) &&
        !string.IsNullOrWhiteSpace(UnlockFingerprint) &&
        !string.IsNullOrWhiteSpace(GenerationRuleFingerprint) &&
        !string.IsNullOrWhiteSpace(SnapshotFingerprint);

    public bool CanReplayBossIdentityForFastSearch =>
        HasExactWorldFastPlanInputs &&
        !TutorialBossOverrideWillApply &&
        HasVerifiedBossVersionFixture;

    public bool AllowsAncientIdentityFastSearch =>
        HasExactWorldFastPlanInputs && HasVerifiedFixturePrefix("ancient-identity:");

    /// <summary>
    /// Fast-specific event-local option authority. Unlike Production replay this
    /// does not require a precomputed SelectedActs list; the World Fast stage
    /// reconstructs Act selection per seed before entering this independent leaf.
    /// </summary>
    public bool AllowsAncientOptionFastSearch(string ancientEntry) =>
        HasExactWorldFastPlanInputs &&
        HasVerifiedFixturePrefix("ancient-option:" + NormalizeFixtureEntry(ancientEntry) + ":");

    public bool HasExactReplayInputs =>
        RuntimeProfilePolicies.IsModernCore(Profile) &&
        DirectSourceAudited &&
        IsVanilla &&
        ActSelectionRootExact &&
        RunSeedRootExact &&
        RunSeedHashKind == Beta109RunSeedHashKind.ModernXxHash64 &&
        OldSeedBranchStatus == Beta109OldSeedBranchStatus.NewSeedHashed &&
        SelectedActsExact &&
        SelectedActs.Count > 0 &&
        ModeFactsExact &&
        IsMultiplayerExact &&
        TestModeFactExact &&
        Act1OverrideExact &&
        GameMode == WorldGameMode.Singleplayer &&
        !IsMultiplayer &&
        PlayerCount == 1 &&
        LobbyPlayers.Count == 1 &&
        LobbyPlayers.All(player => player.Exact && !player.IsRandomCharacter) &&
        SharedEventCatalogExact &&
        EventAuthority.HasExactFilteringAuthority &&
        AllSharedAncientCatalogExact &&
        SharedAncientCatalogExact &&
        UnlockFactsExact &&
        HasExactUpFrontPrefix &&
        OrderedActCatalog.Count > 0 &&
        SelectedActs.All(key => OrderedActCatalog.Any(act => act.ActKey == key && act.HasExactGenerationInputs)) &&
        WorldGenerationHooksExact &&
        !string.IsNullOrWhiteSpace(CatalogFingerprint) &&
        !string.IsNullOrWhiteSpace(UnlockFingerprint) &&
        !string.IsNullOrWhiteSpace(GenerationRuleFingerprint) &&
        !string.IsNullOrWhiteSpace(SnapshotFingerprint);

    /// <summary>
    /// Narrow authority used only by Orobas SeaGlass target projection. Missing
    /// character source-order authority must not invalidate unrelated world or
    /// Ancient option identities because the target draw still consumes exactly
    /// one event-local RNG call whenever another unlocked character exists.
    /// </summary>
    public bool HasExactOrobasSeaGlassTargetAuthority =>
        UnlockedCharactersExact;

    /// <summary>
    /// Structural capability required to deterministically replay the normal
    /// non-tutorial Boss identity path for Search. This deliberately excludes
    /// version/fixture confidence and the separately audited tutorial override
    /// fact: those lower confidence and produce a visible warning, but must not
    /// manufacture a zero-result search when the normal path itself is replayable.
    /// </summary>
    public bool CanReplayBossIdentityForSearch =>
        HasExactReplayInputs &&
        !TutorialBossOverrideWillApply;

    public bool HasVerifiedBossVersionFixture =>
        HasVerifiedFixturePrefix("boss:");

    public bool AllowsBossProductionExact =>
        CanReplayBossIdentityForSearch &&
        TutorialBossOverrideAuthorityExact &&
        HasVerifiedBossVersionFixture;

    public bool AllowsAncientIdentityProductionExact =>
        HasExactReplayInputs && HasVerifiedFixturePrefix("ancient-identity:");

    public bool AllowsAncientOptionProductionExact(string ancientEntry) =>
        HasExactReplayInputs &&
        HasVerifiedFixturePrefix("ancient-option:" + NormalizeFixtureEntry(ancientEntry) + ":");

    /// <summary>
    /// Overall Modern world foundation. Ancient option exactness remains
    /// generator-specific and is queried through AllowsAncientOptionProductionExact.
    /// </summary>
    public bool AllowsProductionExact =>
        AllowsBossProductionExact && AllowsAncientIdentityProductionExact;

    private bool HasVerifiedFixturePrefix(string prefix) =>
        (FixtureValidationStatus is Beta109FixtureValidationStatus.VerifiedRealGame or
            Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown) &&
        VerifiedRealGameFixtureIds.Any(id =>
            !string.IsNullOrWhiteSpace(id) &&
            id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeFixtureEntry(string entry) =>
        new(entry.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
