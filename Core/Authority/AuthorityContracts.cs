namespace RolltheSpire2.Core.Authority;

public enum SourceAuthority
{
    OfficialRuntimeExact,
    AuditedStaticExact,
    ModdedRuntimeBestEffort,
    UserDeclaredAssumption,
    Incomplete,
    Ambiguous,
    Unknown
}

public enum SnapshotCompleteness
{
    Complete,
    Partial,
    Missing
}

public enum IdentityResolutionStatus
{
    Exact,
    Ambiguous,
    Unknown
}

public static class SourceAuthorityRules
{
    public static bool SupportsExactIdentity(SourceAuthority authority) => authority is
        SourceAuthority.OfficialRuntimeExact or SourceAuthority.AuditedStaticExact;
}
