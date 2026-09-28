namespace RolltheSpire2.Core.Authority.Runtime;

/// <summary>
/// Owner-verified gameplay-semantic baselines. A baseline means the captured facts were
/// produced on a machine/environment the Owner independently confirmed as a clean,
/// trustworthy vanilla semantic environment. It does not prove absence of arbitrary UI,
/// localization, Harmony, or other non-fingerprinted modifications in another process.
/// </summary>
internal sealed record VerifiedVanillaBaseline(
    string GameVersion,
    int FingerprintSchemaVersion,
    IReadOnlyDictionary<string, string> DomainHashes,
    string UnlockUniverseHash,
    string OverallSemanticHash,
    IReadOnlyDictionary<string, IReadOnlyList<string>> VanillaUnlockUniverse,
    string VerificationEvidence);

internal static class VerifiedVanillaBaselineRegistry
{
    // First trust anchor: Owner-confirmed Beta111 / 0.111.0 gameplay-semantic environment,
    // captured with Fingerprint Schema 1 on 2026-08-20. The baseline proves equality only
    // for Schema 1 observed RT2-relevant facts; it is not a generic process/mod detector.
    private static readonly IReadOnlyList<VerifiedVanillaBaseline> Entries = new[]
    {
        VerifiedVanillaBaselineBeta111Schema1.Value
    };

    public static IReadOnlyList<VerifiedVanillaBaseline> GetAll() => Entries;
}

internal static class RuntimeAuthorityInterpreter
{
    public static SemanticEnvironmentStatus InterpretHistoricalFingerprint(
        string gameVersion,
        int fingerprintSchemaVersion,
        string overallSemanticHash,
        out string matchedBaselineGameVersion)
    {
        matchedBaselineGameVersion = string.Empty;
        if (fingerprintSchemaVersion <= 0 || string.IsNullOrWhiteSpace(overallSemanticHash))
            return SemanticEnvironmentStatus.Unknown;

        VerifiedVanillaBaseline[] schemaBaselines = VerifiedVanillaBaselineRegistry.GetAll()
            .Where(item => item.FingerprintSchemaVersion == fingerprintSchemaVersion)
            .ToArray();
        if (schemaBaselines.Length == 0) return SemanticEnvironmentStatus.Unverified;

        VerifiedVanillaBaseline? exact = schemaBaselines.FirstOrDefault(item =>
            string.Equals(item.OverallSemanticHash, overallSemanticHash, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            matchedBaselineGameVersion = exact.GameVersion;
            return string.Equals(exact.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase)
                ? SemanticEnvironmentStatus.VerifiedVanillaMatch
                : SemanticEnvironmentStatus.KnownBaselineMatchButGameVersionUnverified;
        }

        return SemanticEnvironmentStatus.SemanticMismatch;
    }

    public static RuntimeAuthorityInterpretation Interpret(RuntimeAuthoritySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        VerifiedVanillaBaseline[] schemaBaselines = VerifiedVanillaBaselineRegistry.GetAll()
            .Where(item => item.FingerprintSchemaVersion == snapshot.FingerprintSchemaVersion)
            .ToArray();
        if (!snapshot.Fingerprint.Complete || string.IsNullOrWhiteSpace(snapshot.Fingerprint.OverallSemanticHash))
        {
            return RuntimeAuthorityInterpretation.Unverified("RuntimeFingerprintIncomplete");
        }
        if (schemaBaselines.Length == 0)
        {
            return RuntimeAuthorityInterpretation.Unverified("NoVerifiedBaselineForFingerprintSchema");
        }

        VerifiedVanillaBaseline? exactSemanticMatch = schemaBaselines.FirstOrDefault(item =>
            string.Equals(
                item.OverallSemanticHash,
                snapshot.Fingerprint.OverallSemanticHash,
                StringComparison.OrdinalIgnoreCase));
        VerifiedVanillaBaseline? sameVersion = schemaBaselines.FirstOrDefault(item =>
            string.Equals(item.GameVersion, snapshot.GameVersion, StringComparison.OrdinalIgnoreCase));

        SemanticEnvironmentStatus environmentStatus;
        string matchedVersion = string.Empty;
        string reason;
        if (exactSemanticMatch is not null)
        {
            matchedVersion = exactSemanticMatch.GameVersion;
            if (string.Equals(exactSemanticMatch.GameVersion, snapshot.GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                environmentStatus = SemanticEnvironmentStatus.VerifiedVanillaMatch;
                reason = "RuntimeSemanticFingerprintMatchesVerifiedSameVersionBaseline";
            }
            else
            {
                environmentStatus = SemanticEnvironmentStatus.KnownBaselineMatchButGameVersionUnverified;
                reason = "RuntimeSemanticFingerprintMatchesVerifiedDifferentVersionBaseline";
            }
        }
        else
        {
            environmentStatus = SemanticEnvironmentStatus.SemanticMismatch;
            reason = sameVersion is not null
                ? "RuntimeSemanticFingerprintDiffersFromVerifiedSameVersionBaseline"
                : "UnverifiedGameVersionDiffersFromAllKnownVerifiedBaselines";
        }

        // Vanilla unlock coverage uses an Owner-verified baseline universe, never the
        // current runtime all-unlocks universe. This prevents mod-added unlockables from
        // changing the meaning of "vanilla full unlock".
        VerifiedVanillaBaseline? unlockBaseline = sameVersion ?? exactSemanticMatch;
        (VanillaUnlockOverallStatus overallUnlock, IReadOnlyList<VanillaUnlockDomainCoverage> coverage) =
            BuildVanillaUnlockCoverage(snapshot, unlockBaseline);

        // Domain-level MATCH/MISMATCH is meaningful only when we have a concrete
        // comparison baseline: same-version when available, otherwise the exact
        // cross-version semantic match. We deliberately do not pick an arbitrary old
        // baseline for an unknown-version mismatch.
        VerifiedVanillaBaseline? comparisonBaseline = sameVersion ?? exactSemanticMatch;
        IReadOnlyList<RuntimeAuthorityDomainComparison> comparisons =
            BuildDomainComparisons(snapshot, comparisonBaseline);

        return new RuntimeAuthorityInterpretation(
            environmentStatus,
            matchedVersion,
            comparisonBaseline?.GameVersion ?? string.Empty,
            reason,
            overallUnlock,
            coverage,
            comparisons,
            BaselineSchemaCompatible: true,
            SameGameVersionBaselineKnown: sameVersion is not null,
            DiffersFromKnownBaselines: exactSemanticMatch is null);
    }

    private static IReadOnlyList<RuntimeAuthorityDomainComparison> BuildDomainComparisons(
        RuntimeAuthoritySnapshot snapshot,
        VerifiedVanillaBaseline? baseline)
    {
        if (baseline is null) return Array.Empty<RuntimeAuthorityDomainComparison>();
        var output = new List<RuntimeAuthorityDomainComparison>();
        foreach (string domain in RuntimeAuthorityDomains.Schema1SemanticDomains)
        {
            string runtimeHash = snapshot.Fingerprint.GetDomainHash(domain);
            baseline.DomainHashes.TryGetValue(domain, out string? baselineHash);
            baselineHash ??= string.Empty;
            RuntimeAuthorityDomainComparisonStatus status =
                string.IsNullOrWhiteSpace(runtimeHash) || string.IsNullOrWhiteSpace(baselineHash)
                    ? RuntimeAuthorityDomainComparisonStatus.Unavailable
                    : string.Equals(runtimeHash, baselineHash, StringComparison.OrdinalIgnoreCase)
                        ? RuntimeAuthorityDomainComparisonStatus.Match
                        : RuntimeAuthorityDomainComparisonStatus.Mismatch;
            output.Add(new RuntimeAuthorityDomainComparison(domain, status, runtimeHash, baselineHash));
        }
        return output;
    }

    private static (VanillaUnlockOverallStatus, IReadOnlyList<VanillaUnlockDomainCoverage>) BuildVanillaUnlockCoverage(
        RuntimeAuthoritySnapshot snapshot,
        VerifiedVanillaBaseline? baseline)
    {
        if (baseline is null || baseline.VanillaUnlockUniverse.Count == 0)
            return (VanillaUnlockOverallStatus.Unknown, Array.Empty<VanillaUnlockDomainCoverage>());

        var output = new List<VanillaUnlockDomainCoverage>();
        bool allComplete = true;
        bool allFull = true;
        foreach ((string domain, IReadOnlyList<string> vanillaUniverse) in baseline.VanillaUnlockUniverse
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            RuntimeCurrentUnlockDomainSnapshot? current = snapshot.FindCurrentUnlockDomain(domain);
            if (current is null || !current.StateComplete)
            {
                output.Add(new VanillaUnlockDomainCoverage(domain, 0, vanillaUniverse.Count, false));
                allComplete = false;
                allFull = false;
                continue;
            }

            var currentSet = new HashSet<string>(current.CurrentUnlocked, StringComparer.Ordinal);
            int unlocked = vanillaUniverse.Count(currentSet.Contains);
            bool complete = true;
            output.Add(new VanillaUnlockDomainCoverage(domain, unlocked, vanillaUniverse.Count, complete));
            allFull &= unlocked == vanillaUniverse.Count;
        }

        if (!allComplete) return (VanillaUnlockOverallStatus.Unknown, output);
        return (allFull ? VanillaUnlockOverallStatus.Full : VanillaUnlockOverallStatus.Partial, output);
    }
}
