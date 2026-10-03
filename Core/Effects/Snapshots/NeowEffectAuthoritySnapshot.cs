using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects.Snapshots;

public enum EffectCardRarity
{
    Basic,
    Common,
    Uncommon,
    Rare,
    Curse,
    Ancient,
    Status,
    Special
}

public enum EffectCardType
{
    Attack,
    Skill,
    Power,
    Curse,
    Status,
    Other
}

public enum EffectRelicRarity
{
    Common,
    Uncommon,
    Rare,
    Shop,
    Other
}

public enum EffectPotionRarity
{
    Common,
    Uncommon,
    Rare,
    Other
}

public enum NestedRelicEffectKind
{
    NoTrackedImmediateEffect,
    Whetstone,
    WarPaint,
    UnknownHook
}

public enum NeowEffectAuthoritySource
{
    RuntimeSnapshot,
    FixtureAuthority,
    AuditedStaticAuthority,
    Missing
}

public sealed record NeowEffectCardSnapshot(
    string InstanceId,
    ModelKey CardKey,
    int PoolOrder,
    EffectCardRarity Rarity,
    EffectCardType CardType,
    bool IsBasic,
    bool IsStrike,
    bool IsDefend,
    bool CanUpgrade,
    bool CanRemove,
    int UpgradeLevel = 0,
    int MaxUpgradeLevel = 1,
    ModelKey? UpgradeTargetKey = null,
    string PoolId = "character",
    string? SourceAssembly = null,
    string? SourceModId = null,
    bool? CanBeGeneratedByModifiers = null,
    bool IsMultiplayerOnly = false,
    bool? CanBeGeneratedInCombat = null,
    bool IsUnlockedInCapturedPool = true,
    bool? IsDiscovered = null,
    bool EligibleForPostCombatRewardByPoolMembership = true,
    string EligibilityAuthority = "runtime-unlocked-pool-membership",
    RuntimeProfileId CatalogProfileId = RuntimeProfileId.Unsupported,
    bool BehaviorMetadataExact = true);

public sealed record NeowEffectPotionSnapshot(
    ModelKey PotionKey,
    int PoolOrder,
    EffectPotionRarity Rarity,
    bool? AllowedOutOfCombat = null,
    string? SourceAssembly = null,
    string? SourceModId = null,
    bool IsMultiplayerOnly = false);

public sealed record NeowEffectRelicSnapshot(
    ModelKey RelicKey,
    int BagOrder,
    EffectRelicRarity Rarity,
    NestedRelicEffectKind NestedEffectKind,
    bool NestedClassificationExact = true,
    string? SourceAssembly = null,
    string? SourceModId = null,
    bool IsMultiplayerOnly = false,
    string RarityCode = "",
    bool IsAllowedInShops = false,
    bool ShopEligibilityExact = false);

public sealed record CharacterCardPoolSnapshot(
    ModelKey CharacterKey,
    string PoolId,
    int PoolOrder,
    IReadOnlyList<NeowEffectCardSnapshot> Cards);

/// <summary>
/// Immutable pure DTO captured on the main thread by a separately audited adapter.
/// Missing collections remain null/Unknown instead of silently becoming empty.
/// </summary>
public sealed record NeowEffectAuthoritySnapshot(
    IReadOnlyList<NeowEffectCardSnapshot>? OrderedDeck,
    IReadOnlyList<NeowEffectCardSnapshot>? CharacterRewardPool,
    IReadOnlyList<NeowEffectCardSnapshot>? ColorlessRewardPool,
    IReadOnlyList<CharacterCardPoolSnapshot>? OtherCharacterPools,
    IReadOnlyList<NeowEffectCardSnapshot>? TransformPool,
    IReadOnlyList<NeowEffectPotionSnapshot>? PotionPool,
    IReadOnlyList<NeowEffectRelicSnapshot>? OrderedRelicBag,
    IReadOnlyList<ModelKey>? GeneratedCursePool,
    IReadOnlyList<ModelKey>? BonesEligibleRelics,
    ModelKey? CharacterStrikeKey,
    ModelKey? CharacterDefendKey,
    ModelKey? ClawKey,
    bool CardRewardPoolsExact,
    bool DeckExact,
    bool TransformPoolsExact,
    bool PotionPoolExact,
    bool RelicBagExact,
    bool CursePoolExact,
    bool BonesEligibilityExact,
    bool CardRewardHooksNoOpExact,
    bool PotionProcurementHooksNoOpExact,
    bool NestedRelicHooksExact,
    int? CurrentGold,
    int? CurrentPotionCount,
    int? PotionCapacity,
    int BranchBudget = 64,
    SourceAuthority SourceAuthority = SourceAuthority.Unknown,
    SnapshotCompleteness Completeness = SnapshotCompleteness.Missing,
    NeowEffectAuthoritySource AuthoritySource = NeowEffectAuthoritySource.Missing,
    RuntimeProfileId CapturedProfileId = RuntimeProfileId.Unsupported,
    string CatalogFingerprint = "",
    string UnlockFingerprint = "",
    string DeckFingerprint = "",
    string RelicBagFingerprint = "",
    string PotionPoolFingerprint = "",
    string SnapshotFingerprint = "",
    DateTimeOffset? CapturedAtUtc = null,
    IReadOnlyList<PredictionWarningCode>? WarningCodes = null,
    string CaptureDiagnosticCode = "",
    bool CharacterRewardPoolExact = false,
    bool ColorlessRewardPoolExact = false,
    bool OtherCharacterPoolsExact = false,
    bool CharacterRewardHooksNoOpExact = false,
    bool ColorlessRewardHooksNoOpExact = false,
    bool OtherCharacterRewardHooksNoOpExact = false,
    IReadOnlyList<NeowEffectRelicSnapshot>? SharedRelicPoolSource = null,
    IReadOnlyList<NeowEffectRelicSnapshot>? CharacterRelicPoolSource = null,
    bool RelicBagSourcePoolsExact = false,
    bool CharacterRewardIdentityPoolExact = false,
    IReadOnlyList<ModelKey>? UnlockedCharacterCardPoolKeys = null,
    bool EventResultStaticCatalogExact = false,
    bool EventColorfulCharacterPoolsExact = false,
    IReadOnlyList<NeowEffectCardSnapshot>? MerchantColorlessOrderedPool = null,
    bool MerchantColorlessOrderedPoolExact = false,
    bool MerchantColorlessPoolIdentityHooksExact = false,
    bool MerchantColorlessRarityIdentityHooksExact = false,
    bool MerchantInitialInventoryShapeExact = false)
{
    /// <summary>
    /// Immutable runtime-capture foundation. This proves that copied catalog inputs exist
    /// for the accepted profile; it deliberately does not claim that unknown Mod behavior
    /// is vanilla-equivalent. Domain-specific exactness must be checked separately.
    /// </summary>
    public bool HasCapturedIdentityFoundation =>
        AuthoritySource != NeowEffectAuthoritySource.Missing &&
        CapturedProfileId != RuntimeProfileId.Unsupported &&
        !string.IsNullOrWhiteSpace(CatalogFingerprint);

    // Owner policy: mod content uses the current model on captured inputs.
    // Historical HasExact* consumers are model-admission predicates in this mode;
    // raw provenance/Exact flags remain unchanged and do not certify mod mechanics.
    public bool UsesBestEffortModel => CapturedProfileId == RuntimeProfileId.Beta111 &&
        (SourceAuthority == SourceAuthority.ModdedRuntimeBestEffort ||
         (CharacterRewardPool?.Concat(ColorlessRewardPool ?? []).Concat(OtherCharacterPools?.SelectMany(p => p.Cards) ?? [])
             .Any(c => !string.IsNullOrWhiteSpace(c.SourceModId) ||
                 !string.IsNullOrWhiteSpace(c.SourceAssembly) && c.SourceAssembly != "sts2") ?? false));

    public bool HasExactFoundation =>
        HasCapturedIdentityFoundation &&
        (SourceAuthorityRules.SupportsExactIdentity(SourceAuthority) || UsesBestEffortModel);

    public bool HasExactDeck => HasExactFoundation && (DeckExact || UsesBestEffortModel) && OrderedDeck is not null;

    /// <summary>
    /// Exact copied membership/order/rarity for identity-only generated-card projections.
    /// It does not authorize custom card behavior or normal reward hooks.
    /// </summary>
    public bool HasExactCharacterRewardIdentityPool =>
        HasCapturedIdentityFoundation && CharacterRewardIdentityPoolExact && CharacterRewardPool is not null;

    public bool HasExactCharacterRewardPool =>
        HasExactFoundation && (CharacterRewardPoolExact && CharacterRewardHooksNoOpExact || UsesBestEffortModel) && CharacterRewardPool is not null;

    public bool HasExactColorlessRewardPool =>
        HasExactFoundation && (ColorlessRewardPoolExact && ColorlessRewardHooksNoOpExact || UsesBestEffortModel) && ColorlessRewardPool is not null;

    public bool HasExactOtherCharacterPools =>
        HasExactFoundation && (OtherCharacterPoolsExact && OtherCharacterRewardHooksNoOpExact || UsesBestEffortModel) && OtherCharacterPools is not null;

    public bool HasExactCardPools =>
        HasExactCharacterRewardPool && HasExactColorlessRewardPool && HasExactOtherCharacterPools;
    public bool HasExactTransformPool => HasExactFoundation && (TransformPoolsExact || UsesBestEffortModel) && TransformPool is not null;
    public bool HasExactPotions => HasExactFoundation && (PotionPoolExact || UsesBestEffortModel) && PotionPool is not null;
    // Relic identity/order can remain exact with opaque Mod relic behavior: unknown
    // nested hooks stay route-local and are not promoted to exact semantics.
    public bool HasExactRelicBag => HasCapturedIdentityFoundation && RelicBagExact && OrderedRelicBag is not null;
    public bool HasExactRelicBagSourcePools =>
        HasCapturedIdentityFoundation &&
        RelicBagSourcePoolsExact &&
        SharedRelicPoolSource is not null &&
        CharacterRelicPoolSource is not null;
    public bool HasExactBonesPools => HasExactFoundation && BonesEligibilityExact && CursePoolExact && BonesEligibleRelics is not null && GeneratedCursePool is not null;

    public bool HasExactMerchantColorlessV1Inputs =>
        HasCapturedIdentityFoundation &&
        CapturedProfileId == RuntimeProfileId.Beta111 &&
        MerchantColorlessOrderedPoolExact &&
        MerchantColorlessPoolIdentityHooksExact &&
        MerchantColorlessRarityIdentityHooksExact &&
        MerchantInitialInventoryShapeExact &&
        MerchantColorlessOrderedPool is not null;

    public bool HasExactColorfulEventResultInputs =>
        HasCapturedIdentityFoundation &&
        CapturedProfileId == RuntimeProfileId.Beta111 &&
        EventResultStaticCatalogExact &&
        EventColorfulCharacterPoolsExact &&
        UnlockedCharacterCardPoolKeys is not null;
}

