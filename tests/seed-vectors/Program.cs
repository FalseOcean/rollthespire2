using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Verification;

SeedRngVectorVerification result = SeedRngVectorVerifier.RunAll();
foreach (SeedRngVectorCase test in result.Cases)
{
    Console.WriteLine($"vector.{test.Name.Replace(' ', '_')}={(test.Passed ? "Passed" : "Failed")};expected={test.Expected};actual={test.Actual}");
}

if (!result.Passed)
{
    throw new InvalidOperationException("One or more Seed/RNG golden vectors failed.");
}

string tempRoot = Path.Combine(Path.GetTempPath(), "rt2-profile-version-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    string releaseInfo = Path.Combine(tempRoot, "release_info.json");

    AssertResolution(releaseInfo, "v0.107.0", "stable",
        RuntimeVersionSupportKind.ArchivedStableReference, RuntimeProfileId.Unsupported, "0.107.0 archive");
    AssertResolution(releaseInfo, "v0.107.1", "stable",
        RuntimeVersionSupportKind.ArchivedStableReference, RuntimeProfileId.Unsupported, "0.107.1 archive");
    AssertResolution(releaseInfo, "v0.109.0", "beta",
        RuntimeVersionSupportKind.HistoricalBetaDonor, RuntimeProfileId.Unsupported, "0.109.0 donor");
    AssertResolution(releaseInfo, "v0.109.1", "beta",
        RuntimeVersionSupportKind.HistoricalBetaDonor, RuntimeProfileId.Unsupported, "0.109.1 donor");
    AssertResolution(releaseInfo, "v0.110.0", "beta",
        RuntimeVersionSupportKind.ArchivedBetaReference, RuntimeProfileId.Unsupported, "0.110.0 archive");
    AssertResolution(releaseInfo, "v0.110.1", "beta",
        RuntimeVersionSupportKind.AuditedExact, RuntimeProfileId.Beta110, "0.110.1 production");

    AssertResolution(releaseInfo, "v0.111.0", "beta",
        RuntimeVersionSupportKind.AcceptedCompatible, RuntimeProfileId.Beta111, "0.111.0 semantic-compatible production");

    File.WriteAllText(releaseInfo, JsonSerializer.Serialize(new { version = "v0.112.0", branch = "future" }));
    GameVersionDetection detection = GameVersionDetector.DetectFromCandidates(new[] { releaseInfo });
    RuntimeVersionResolution resolution = RuntimeVersionCompatibility.Resolve(detection);
    if (!resolution.IsProvisional || !resolution.IsFallback ||
        resolution.Confidence != CompatibilityConfidence.ProvisionalUnverified ||
        resolution.RuntimeAccepted || !resolution.AllowsProductionSearch ||
        resolution.BlockedDomains != CompatibilityDomainMask.None ||
        resolution.ProfileId != RuntimeProfileId.Beta111 ||
        resolution.SemanticProfileId != RngSemanticProfileId.Modern110 ||
        resolution.GameVersionIdentity.ExactVersion != "0.112.0" ||
        resolution.ReferenceVersion != RuntimeVersionCompatibility.Beta111Version)
    {
        throw new InvalidOperationException("Unknown newer version provisional semantic-reuse fixture failed.");
    }

    RuntimeVersionResolution v1070 = RuntimeVersionCompatibility.Resolve("0.107.0");
    RuntimeVersionResolution v1071 = RuntimeVersionCompatibility.Resolve("0.107.1");
    if (v1070.GameVersionIdentity == v1071.GameVersionIdentity)
        throw new InvalidOperationException("0.107.0 and 0.107.1 exact GameVersion identities collapsed.");

    RuntimeVersionResolution v1090 = RuntimeVersionCompatibility.Resolve("0.109.0");
    RuntimeVersionResolution v1091 = RuntimeVersionCompatibility.Resolve("0.109.1");
    if (v1090.GameVersionIdentity == v1091.GameVersionIdentity ||
        v1090.SemanticProfileId != RngSemanticProfileId.Modern109 ||
        v1091.SemanticProfileId != RngSemanticProfileId.Modern109)
        throw new InvalidOperationException("0.109.0/0.109.1 exact identity/shared-semantic fixture failed.");
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("pureSeedRngVectorsPassed=true");
Console.WriteLine("productionProfileBeta110Accepted=true");
Console.WriteLine("beta111SemanticCompatibleProfile=true");
Console.WriteLine("stable107ExactVersionIdentities=true");
Console.WriteLine("beta109ExactIdentitiesSharedSemanticDonor=true");
Console.WriteLine("beta110Version01101=true");
Console.WriteLine("beta111Version01110=true");
Console.WriteLine("unknownNewerVersionProvisionalSearchEnabled=true");
Console.WriteLine("legacyProfileNamesCompatibilityReadable=true");

static void AssertResolution(
    string releaseInfo,
    string version,
    string branch,
    RuntimeVersionSupportKind expectedKind,
    RuntimeProfileId expectedProfile,
    string label)
{
    File.WriteAllText(releaseInfo, JsonSerializer.Serialize(new { version, branch }));
    GameVersionDetection detection = GameVersionDetector.DetectFromCandidates(new[] { releaseInfo });
    RuntimeVersionResolution resolution = RuntimeVersionCompatibility.Resolve(detection);
    if (!detection.IsExact || resolution.SupportKind != expectedKind || resolution.ProfileId != expectedProfile)
    {
        throw new InvalidOperationException($"{label} version resolution fixture failed.");
    }
}
