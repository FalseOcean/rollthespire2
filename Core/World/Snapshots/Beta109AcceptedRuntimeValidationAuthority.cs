using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Core.World.Snapshots;

public sealed record Beta109RuntimeValidationAuthorityDecision(
    Beta109FixtureValidationStatus Status,
    IReadOnlyList<string> FixtureIds,
    string EvidenceCode);

/// <summary>
/// Version- and domain-scoped world identity authority. Beta109 retains only the
/// directly validated historical Ancient identity/initial-option fixtures. Accepted
/// Beta110/0.110.1 runtimes additionally publish the user-accepted Boss identity
/// fixture while reusing the audited generators with AssumedCompatibleLatestKnown
/// status. IDs cannot unlock Neow, SeaGlass target metadata, or unrelated domains.
/// </summary>
public static class Beta109AcceptedRuntimeValidationAuthority
{
    public const string EvidenceCode =
        "beta109-ancient-identity-options-user-runtime-validated-v1";

    private const string ValidationId =
        "rewrite-user-runtime-validated-20260728";

    private const string Beta110BossValidationId =
        "rewrite-user-runtime-validated-beta110";

    private static readonly IReadOnlyList<string> AcceptedAncientFixtureIds = Array.AsReadOnly(new[]
    {
        "ancient-identity:" + ValidationId,
        "ancient-option:OROBAS:" + ValidationId,
        "ancient-option:PAEL:" + ValidationId,
        "ancient-option:TEZCATARA:" + ValidationId,
        "ancient-option:DARV:" + ValidationId,
        "ancient-option:NONUPEIPE:" + ValidationId,
        "ancient-option:TANX:" + ValidationId,
        "ancient-option:VAKUU:" + ValidationId
    });

    private static readonly IReadOnlyList<string> AcceptedBeta110FixtureIds = Array.AsReadOnly(
        AcceptedAncientFixtureIds
            .Concat(new[] { "boss:" + Beta110BossValidationId })
            .ToArray());

    public static Beta109RuntimeValidationAuthorityDecision Resolve(string gameVersion)
    {
        string normalized = GameVersionDetector.NormalizeVersion(gameVersion);
        if (RuntimeVersionCompatibility.IsBeta109_0(normalized) || RuntimeVersionCompatibility.IsBeta109_1(normalized))
        {
            return new Beta109RuntimeValidationAuthorityDecision(
                Beta109FixtureValidationStatus.VerifiedRealGame,
                AcceptedAncientFixtureIds,
                EvidenceCode + ":historical-donor:" + normalized);
        }

        if (RuntimeVersionCompatibility.IsBeta110(normalized) &&
            Beta110ValidationAuthority.BossAncientAccepted)
        {
            return new Beta109RuntimeValidationAuthorityDecision(
                Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown,
                AcceptedBeta110FixtureIds,
                "beta110-boss-ancient-generator-user-accepted-compatible-v2:" + normalized);
        }

        // Beta111 (0.111.0) is an audited A-class semantic-compatible patch over
        // the Modern110 world/Ancient implementation. The official 0.110.1 ->
        // 0.111.0 source audit found no Ancient deterministic semantic delta.
        // Reuse the already accepted Beta110 Boss/Ancient fixture IDs only for
        // this audited runtime identity. The separate provisional branch below
        // retains explicit unverified reference provenance.
        if (RuntimeVersionCompatibility.IsBeta111(normalized) &&
            Beta111ValidationAuthority.RuntimeAccepted &&
            Beta111ValidationAuthority.WorldAncientSourceAccepted &&
            Beta110ValidationAuthority.BossAncientAccepted)
        {
            return new Beta109RuntimeValidationAuthorityDecision(
                Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown,
                AcceptedBeta110FixtureIds,
                "beta111-world-ancient-no-semantic-delta-audited-compatible-v1:" + normalized);
        }

        var compatibility = RuntimeVersionCompatibility.Resolve(normalized);
        if (compatibility.IsProvisional && compatibility.ProfileId == RuntimeProfileId.Beta111 &&
            compatibility.AllowsProductionSearch && !compatibility.Blocks(CompatibilityDomainMask.WorldEventAncient))
        {
            // Reuse reference-generator evidence, not a claim of future-version
            // validation. All captured input/order/hook requirements still apply.
            return new Beta109RuntimeValidationAuthorityDecision(
                Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown,
                AcceptedBeta110FixtureIds,
                "unverified-world-reference-reuse:" + normalized + "->" + compatibility.ReferenceVersion);
        }

        return new Beta109RuntimeValidationAuthorityDecision(
            Beta109FixtureValidationStatus.PendingRealGame,
            Array.Empty<string>(),
            "modern-world-ancient-fixture-version-not-applicable:" +
            (string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized));
    }
}
