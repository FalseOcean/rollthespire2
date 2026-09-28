using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Authority.Runtime;

/// <summary>
/// Stable schema for the RT2-relevant runtime semantic fingerprint. A schema bump means
/// RT2 changed what it considers fingerprint authority; it does not imply the game changed.
/// </summary>
internal static class RuntimeAuthorityFingerprintSchema
{
    public const int Current = 1;
}

internal static class RuntimeAuthorityDomains
{
    public const string Characters = "Characters";
    public const string Cards = "Cards";
    public const string Relics = "Relics";
    public const string Ancients = "Ancients";
    public const string Potions = "Potions";
    public const string Events = "Events";
    public const string Bosses = "Bosses";

    public static readonly IReadOnlyList<string> Schema1SemanticDomains = new[]
    {
        Characters,
        Cards,
        Relics,
        Ancients,
        Potions,
        Events,
        Bosses
    };

    public static readonly IReadOnlyList<string> Schema1UnlockDomains = new[]
    {
        Characters,
        Cards,
        Relics,
        Ancients,
        Potions
    };
}

/// <summary>
/// One canonical semantic item. StableIdentity is always a ModelKey serialization.
/// Fields are copied primitive strings in a schema-defined order; no game/runtime object
/// is retained after the main-thread capture boundary.
/// </summary>
internal sealed record RuntimeSemanticItem(
    string StableIdentity,
    IReadOnlyList<string> Fields);

internal sealed record RuntimeSemanticDomainSnapshot(
    string Domain,
    IReadOnlyList<RuntimeSemanticItem> Items,
    bool AuthorityComplete,
    string EvidenceCode)
{
    public int Count => Items.Count;
}

/// <summary>
/// Runtime all-unlocks universe and current player unlock state are captured separately.
/// RuntimeUnlockAuthority may include mod-provided content; vanilla unlock coverage is
/// computed only against a VerifiedVanillaBaseline's own unlock universe.
/// </summary>
internal sealed record RuntimeUnlockAuthorityDomainSnapshot(
    string Domain,
    IReadOnlyList<string> UnlockUniverse,
    bool AuthorityComplete,
    string EvidenceCode)
{
    public int Count => UnlockUniverse.Count;
}

internal sealed record RuntimeCurrentUnlockDomainSnapshot(
    string Domain,
    IReadOnlyList<string> CurrentUnlocked,
    bool StateComplete,
    string EvidenceCode)
{
    public int Count => CurrentUnlocked.Count;
}

internal sealed record RuntimeSemanticFingerprintBundle(
    int SchemaVersion,
    IReadOnlyDictionary<string, string> DomainHashes,
    string UnlockUniverseHash,
    string OverallSemanticHash,
    bool Complete,
    string EvidenceCode)
{
    public string GetDomainHash(string domain) =>
        DomainHashes.TryGetValue(domain, out string? value) ? value : string.Empty;
}

internal sealed record RuntimeAuthoritySnapshot(
    string GameVersion,
    int FingerprintSchemaVersion,
    IReadOnlyList<RuntimeSemanticDomainSnapshot> SemanticUniverse,
    IReadOnlyList<RuntimeUnlockAuthorityDomainSnapshot> UnlockAuthority,
    IReadOnlyList<RuntimeCurrentUnlockDomainSnapshot> CurrentUnlockState,
    RuntimeSemanticFingerprintBundle Fingerprint,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<string> CaptureIssues)
{
    public static RuntimeAuthoritySnapshot Unavailable(string gameVersion, string issue) => new(
        gameVersion ?? string.Empty,
        RuntimeAuthorityFingerprintSchema.Current,
        Array.Empty<RuntimeSemanticDomainSnapshot>(),
        Array.Empty<RuntimeUnlockAuthorityDomainSnapshot>(),
        Array.Empty<RuntimeCurrentUnlockDomainSnapshot>(),
        new RuntimeSemanticFingerprintBundle(
            RuntimeAuthorityFingerprintSchema.Current,
            new Dictionary<string, string>(StringComparer.Ordinal),
            string.Empty,
            string.Empty,
            false,
            issue ?? "RuntimeAuthorityUnavailable"),
        DateTimeOffset.UtcNow,
        new[] { issue ?? "RuntimeAuthorityUnavailable" });

    public RuntimeSemanticDomainSnapshot? FindSemanticDomain(string domain) =>
        SemanticUniverse.FirstOrDefault(item => string.Equals(item.Domain, domain, StringComparison.Ordinal));

    public RuntimeUnlockAuthorityDomainSnapshot? FindUnlockAuthorityDomain(string domain) =>
        UnlockAuthority.FirstOrDefault(item => string.Equals(item.Domain, domain, StringComparison.Ordinal));

    public RuntimeCurrentUnlockDomainSnapshot? FindCurrentUnlockDomain(string domain) =>
        CurrentUnlockState.FirstOrDefault(item => string.Equals(item.Domain, domain, StringComparison.Ordinal));
}

internal enum SemanticEnvironmentStatus : byte
{
    Unknown = 0,
    VerifiedVanillaMatch = 1,
    KnownBaselineMatchButGameVersionUnverified = 2,
    SemanticMismatch = 3,
    Unverified = 4
}

internal enum VanillaUnlockOverallStatus : byte
{
    Unknown = 0,
    Full = 1,
    Partial = 2
}

internal sealed record VanillaUnlockDomainCoverage(
    string Domain,
    int Unlocked,
    int VanillaUniverse,
    bool Complete)
{
    public bool IsFull => Complete && VanillaUniverse >= 0 && Unlocked >= VanillaUniverse;
}

internal enum RuntimeAuthorityDomainComparisonStatus : byte
{
    Unavailable = 0,
    Match = 1,
    Mismatch = 2
}

internal sealed record RuntimeAuthorityDomainComparison(
    string Domain,
    RuntimeAuthorityDomainComparisonStatus Status,
    string RuntimeHash,
    string BaselineHash);

internal sealed record RuntimeAuthorityInterpretation(
    SemanticEnvironmentStatus EnvironmentStatus,
    string MatchedBaselineGameVersion,
    string ComparisonBaselineGameVersion,
    string EnvironmentReason,
    VanillaUnlockOverallStatus VanillaUnlockStatus,
    IReadOnlyList<VanillaUnlockDomainCoverage> VanillaUnlockCoverage,
    IReadOnlyList<RuntimeAuthorityDomainComparison> DomainComparisons,
    bool BaselineSchemaCompatible,
    bool SameGameVersionBaselineKnown,
    bool DiffersFromKnownBaselines)
{
    public static RuntimeAuthorityInterpretation Unverified(string reason) => new(
        SemanticEnvironmentStatus.Unverified,
        string.Empty,
        string.Empty,
        reason,
        VanillaUnlockOverallStatus.Unknown,
        Array.Empty<VanillaUnlockDomainCoverage>(),
        Array.Empty<RuntimeAuthorityDomainComparison>(),
        false,
        false,
        false);
}

internal sealed record RuntimeAuthorityEnvironmentSnapshot(
    RuntimeAuthoritySnapshot Authority,
    RuntimeAuthorityInterpretation Interpretation,
    string BaselineCandidatePath)
{
    public static RuntimeAuthorityEnvironmentSnapshot Unavailable(string gameVersion, string issue) => new(
        RuntimeAuthoritySnapshot.Unavailable(gameVersion, issue),
        RuntimeAuthorityInterpretation.Unverified(issue),
        string.Empty);
}
