namespace RolltheSpire2.Compatibility;

/// <summary>
/// Beta111 is an A-class Semantic-Compatible Patch. The accepted 2026-08-14 source
/// diff found no RNG-core/generation/continuation/authority-schema semantic change.
/// Owner runtime evidence then confirmed 0.111.0 compile/load, RNG vectors 16/16,
/// MainMenu/reflection binding, live authority capture, and the Salvo/Splash rarity
/// swap under the Beta111 catalog identity. Full-domain Exact owner re-acceptance is
/// intentionally not required for this compatibility class.
/// </summary>
public static class Beta111ValidationAuthority
{
    public const string CompatibilityClass = "A.SemanticCompatiblePatch";
    public const string ImplementationStatus = "AcceptedCompatible";
    public const string RuntimeSupportStatus = "Supported";

    public const bool SourceAuditAccepted = true;
    public const bool CompileLoadAccepted = true;
    public const bool AuthoritySmokeAccepted = true;
    public const bool RuntimeSmokeAccepted = true;
    public const bool RuntimeAccepted = true;
    public const bool FullDomainExactOwnerAcceptanceRequired = false;

    public const bool RngCoreSourceAccepted = true;
    public const bool NeowSourceAccepted = true;
    public const bool RelicSourceAccepted = true;
    public const bool WorldAncientSourceAccepted = true;
    public const bool CardCatalogSourceAccepted = true;
    public const bool NormalCombatRewardSourceAccepted = true;
    public const bool PotionSourceAccepted = true;
    public const bool MainMenuBindingSourceAccepted = true;
}
