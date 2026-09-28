namespace RolltheSpire2.Compatibility;

/// <summary>
/// User-accepted Beta110 production validation state. The Beta 0.110.1 runtime is the accepted production target. Its seed/RNG/catalog
/// design retains audited 0.110.0 evidence, while later unknown versions still use
/// the explicit warning-backed fallback policy.
/// </summary>
public static class Beta110ValidationAuthority
{
    public const string ImplementationStatus = "Accepted";
    public const string RuntimeSupportStatus = "Supported";

    public const bool RuntimeAccepted = true;
    public const bool NeowAccepted = true;
    public const bool BossAncientAccepted = true;
    public const bool CardCatalogAccepted = true;
    public const bool KaleidoscopeAccepted = true;
    public const bool NormalCombatRewardAccepted = true;
    public const bool RelicEventSequenceAccepted = true;
}
