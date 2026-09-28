using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction;

[Flags]
public enum SeedPredictionDomainSelection
{
    None = 0,
    Neow = 1 << 0,
    World = 1 << 1,
    RelicSequence = 1 << 2,
    NormalCombatRewards = 1 << 3,
    All = Neow | World | RelicSequence | NormalCombatRewards
}

public enum SeedPredictionRequestError
{
    None,
    MissingSeed,
    MissingCharacter,
    AscensionOutOfRange,
    PlayersCountOutOfRange,
    PlayerSlotOutOfRange,
    MissingAuthority,
    AuthorityContextMismatch,
    MissingSnapshotFingerprint,
    RelicSequencePreviewCountOutOfRange
}

public sealed class SeedPredictionRequest
{
    private SeedPredictionRequest(
        PredictionRequestId requestId,
        string originalSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot authority,
        AncientOptionConditionProfile ancientOptionConditions,
        SeedPredictionDomainSelection domains,
        int relicSequencePreviewCount,
        bool includeDiagnostics,
        bool enableComplexBonesDeckInteractions,
        TrustedRootHashInput? trustedRootHashInput,
        NormalCombatRewardProjectionRequest normalCombatRewardProjectionRequest)
    {
        RequestId = requestId;
        OriginalSeed = originalSeed;
        Character = character;
        Ascension = ascension;
        PlayersCount = playersCount;
        PlayerSlotIndex = playerSlotIndex;
        Authority = authority;
        AncientOptionConditions = ancientOptionConditions;
        Domains = domains;
        RelicSequencePreviewCount = relicSequencePreviewCount;
        IncludeDiagnostics = includeDiagnostics;
        EnableComplexBonesDeckInteractions = enableComplexBonesDeckInteractions;
        TrustedRootHashInput = trustedRootHashInput;
        CombatRewardProjectionRequest = normalCombatRewardProjectionRequest;
    }

    public PredictionRequestId RequestId { get; }
    public string OriginalSeed { get; }
    public CharacterIdentity Character { get; }
    public int Ascension { get; }
    public int PlayersCount { get; }
    public int PlayerSlotIndex { get; }
    public RuntimeContextAuthoritySnapshot Authority { get; }
    public AncientOptionConditionProfile AncientOptionConditions { get; }
    public SeedPredictionDomainSelection Domains { get; }
    public int RelicSequencePreviewCount { get; }
    public bool IncludeDiagnostics { get; }
    public bool EnableComplexBonesDeckInteractions { get; }
    internal TrustedRootHashInput? TrustedRootHashInput { get; }
    internal NormalCombatRewardProjectionRequest CombatRewardProjectionRequest { get; }

    public SeedPredictionRequest WithComplexBonesDeckInteractions(bool enabled) => new(
        RequestId,
        OriginalSeed,
        Character,
        Ascension,
        PlayersCount,
        PlayerSlotIndex,
        Authority,
        AncientOptionConditions,
        Domains,
        RelicSequencePreviewCount,
        IncludeDiagnostics,
        enabled,
        TrustedRootHashInput,
        CombatRewardProjectionRequest);

    public bool Includes(SeedPredictionDomainSelection domain) => (Domains & domain) == domain;

    public static bool TryCreate(
        string? originalSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot? authority,
        bool includeDiagnostics,
        out SeedPredictionRequest? request,
        out SeedPredictionRequestError error) =>
        TryCreate(
            originalSeed,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            authority,
            AncientOptionConditionProfile.BroadDefault,
            SeedPredictionDomainSelection.All,
            SeedPredictionInputLimits.DefaultRelicSequencePreviewCount,
            includeDiagnostics,
            out request,
            out error);

    public static bool TryCreate(
        string? originalSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot? authority,
        AncientOptionConditionProfile? ancientOptionConditions,
        bool includeDiagnostics,
        out SeedPredictionRequest? request,
        out SeedPredictionRequestError error) =>
        TryCreate(
            originalSeed,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            authority,
            ancientOptionConditions,
            SeedPredictionDomainSelection.All,
            SeedPredictionInputLimits.DefaultRelicSequencePreviewCount,
            includeDiagnostics,
            out request,
            out error);

    public static bool TryCreate(
        string? originalSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot? authority,
        AncientOptionConditionProfile? ancientOptionConditions,
        SeedPredictionDomainSelection domains,
        bool includeDiagnostics,
        out SeedPredictionRequest? request,
        out SeedPredictionRequestError error) =>
        TryCreate(
            originalSeed,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            authority,
            ancientOptionConditions,
            domains,
            SeedPredictionInputLimits.DefaultRelicSequencePreviewCount,
            includeDiagnostics,
            out request,
            out error);

    public static bool TryCreate(
        string? originalSeed,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot? authority,
        AncientOptionConditionProfile? ancientOptionConditions,
        SeedPredictionDomainSelection domains,
        int relicSequencePreviewCount,
        bool includeDiagnostics,
        out SeedPredictionRequest? request,
        out SeedPredictionRequestError error)
    {
        string seed = originalSeed?.Trim() ?? string.Empty;
        if (seed.Length == 0)
        {
            request = null;
            error = SeedPredictionRequestError.MissingSeed;
            return false;
        }

        if (!character.IsValid)
        {
            request = null;
            error = SeedPredictionRequestError.MissingCharacter;
            return false;
        }

        if (ascension is < SeedPredictionInputLimits.MinimumAscension or > SeedPredictionInputLimits.MaximumAscension)
        {
            request = null;
            error = SeedPredictionRequestError.AscensionOutOfRange;
            return false;
        }

        if (playersCount is < SeedPredictionInputLimits.MinimumPlayers or > SeedPredictionInputLimits.MaximumPlayers)
        {
            request = null;
            error = SeedPredictionRequestError.PlayersCountOutOfRange;
            return false;
        }

        if (playerSlotIndex < 0 || playerSlotIndex >= playersCount)
        {
            request = null;
            error = SeedPredictionRequestError.PlayerSlotOutOfRange;
            return false;
        }


        if (relicSequencePreviewCount is < 1 or > SeedPredictionInputLimits.MaximumRelicSequencePreviewCount)
        {
            request = null;
            error = SeedPredictionRequestError.RelicSequencePreviewCountOutOfRange;
            return false;
        }

        if (authority is null)
        {
            request = null;
            error = SeedPredictionRequestError.MissingAuthority;
            return false;
        }

        if (authority.Character != character ||
            authority.Ascension != ascension ||
            authority.PlayersCount != playersCount ||
            authority.PlayerSlotIndex != playerSlotIndex)
        {
            request = null;
            error = SeedPredictionRequestError.AuthorityContextMismatch;
            return false;
        }

        if (string.IsNullOrWhiteSpace(authority.UnlockSnapshotFingerprint) ||
            string.IsNullOrWhiteSpace(authority.CatalogFingerprint))
        {
            request = null;
            error = SeedPredictionRequestError.MissingSnapshotFingerprint;
            return false;
        }

        request = new SeedPredictionRequest(
            PredictionRequestId.Create(),
            seed,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            authority,
            ancientOptionConditions ?? AncientOptionConditionProfile.BroadDefault,
            domains == SeedPredictionDomainSelection.None ? SeedPredictionDomainSelection.Neow : domains,
            relicSequencePreviewCount,
            includeDiagnostics,
            enableComplexBonesDeckInteractions: false,
            trustedRootHashInput: null,
            NormalCombatRewardProjectionRequest.RichAnalysis);
        error = SeedPredictionRequestError.None;
        return true;
    }

    internal static bool TryCreateFromRootHash(
        TrustedRootHashInput trustedRootHashInput,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot? authority,
        AncientOptionConditionProfile? ancientOptionConditions,
        SeedPredictionDomainSelection domains,
        int relicSequencePreviewCount,
        bool includeDiagnostics,
        out SeedPredictionRequest? request,
        out SeedPredictionRequestError error) =>
        TryCreateFromRootHash(
            trustedRootHashInput,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            authority,
            ancientOptionConditions,
            domains,
            relicSequencePreviewCount,
            includeDiagnostics,
            NormalCombatRewardProjectionRequest.RichAnalysis,
            out request,
            out error);

    internal static bool TryCreateFromRootHash(
        TrustedRootHashInput trustedRootHashInput,
        CharacterIdentity character,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        RuntimeContextAuthoritySnapshot? authority,
        AncientOptionConditionProfile? ancientOptionConditions,
        SeedPredictionDomainSelection domains,
        int relicSequencePreviewCount,
        bool includeDiagnostics,
        NormalCombatRewardProjectionRequest normalCombatRewardProjectionRequest,
        out SeedPredictionRequest? request,
        out SeedPredictionRequestError error)
    {
        if (!character.IsValid)
        {
            request = null;
            error = SeedPredictionRequestError.MissingCharacter;
            return false;
        }
        if (ascension is < SeedPredictionInputLimits.MinimumAscension or > SeedPredictionInputLimits.MaximumAscension)
        {
            request = null;
            error = SeedPredictionRequestError.AscensionOutOfRange;
            return false;
        }
        if (playersCount is < SeedPredictionInputLimits.MinimumPlayers or > SeedPredictionInputLimits.MaximumPlayers)
        {
            request = null;
            error = SeedPredictionRequestError.PlayersCountOutOfRange;
            return false;
        }
        if (playerSlotIndex < 0 || playerSlotIndex >= playersCount)
        {
            request = null;
            error = SeedPredictionRequestError.PlayerSlotOutOfRange;
            return false;
        }
        if (relicSequencePreviewCount is < 1 or > SeedPredictionInputLimits.MaximumRelicSequencePreviewCount)
        {
            request = null;
            error = SeedPredictionRequestError.RelicSequencePreviewCountOutOfRange;
            return false;
        }
        if (authority is null)
        {
            request = null;
            error = SeedPredictionRequestError.MissingAuthority;
            return false;
        }
        if (authority.Character != character ||
            authority.Ascension != ascension ||
            authority.PlayersCount != playersCount ||
            authority.PlayerSlotIndex != playerSlotIndex)
        {
            request = null;
            error = SeedPredictionRequestError.AuthorityContextMismatch;
            return false;
        }
        if (string.IsNullOrWhiteSpace(authority.UnlockSnapshotFingerprint) ||
            string.IsNullOrWhiteSpace(authority.CatalogFingerprint))
        {
            request = null;
            error = SeedPredictionRequestError.MissingSnapshotFingerprint;
            return false;
        }

        request = new SeedPredictionRequest(
            PredictionRequestId.Create(),
            trustedRootHashInput.SeedIdentity,
            character,
            ascension,
            playersCount,
            playerSlotIndex,
            authority,
            ancientOptionConditions ?? AncientOptionConditionProfile.BroadDefault,
            domains == SeedPredictionDomainSelection.None ? SeedPredictionDomainSelection.Neow : domains,
            relicSequencePreviewCount,
            includeDiagnostics,
            enableComplexBonesDeckInteractions: false,
            trustedRootHashInput,
            normalCombatRewardProjectionRequest);
        error = SeedPredictionRequestError.None;
        return true;
    }
}
