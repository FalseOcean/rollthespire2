namespace RolltheSpire2.Compatibility;

public enum GameReleaseChannel { Stable, Beta }

public enum RuntimeVersionSupportKind
{
    AuditedExact,
    AcceptedCompatible,
    SupportedAfterValidation,
    LatestKnownFallback,
    KnownIncompatible,
    ArchivedStableReference,
    HistoricalBetaDonor,
    ArchivedBetaReference,
    Unsupported,
    ProvisionalVersionedRules
}

/// <summary>
/// One exact game-version compatibility decision. GameVersionIdentity identifies
/// the detected official build; ProfileId selects the current runtime adapter;
/// SemanticProfileId selects RNG/generation semantics. ReferenceVersion is the
/// source of those rules. RulesAdaptedThroughVersion names them for players by
/// the latest game release to which this rule set has been adapted.
/// </summary>
public sealed record RuntimeVersionResolution(
    GameVersionIdentity GameVersionIdentity,
    RuntimeProfileId ProfileId,
    RngSemanticProfileId SemanticProfileId,
    CompatibilityConfidence Confidence,
    CompatibilityDomainMask BlockedDomains,
    RuntimeVersionSupportKind SupportKind,
    string DetectedVersion,
    string ReferenceVersion,
    string EvidenceCode)
{
    public string RulesAdaptedThroughVersion { get; init; } = string.Empty;
    // Set on the game branches targeted by this RT2 release. Older standalone
    // adapters may have a rule version without a current release target.
    public GameReleaseChannel? AdaptedReleaseChannel { get; init; }
    public bool IsReleaseSupported { get; init; }

    public bool IsAuditedExact => SupportKind == RuntimeVersionSupportKind.AuditedExact;
    public bool IsAcceptedCompatible => SupportKind == RuntimeVersionSupportKind.AcceptedCompatible;
    public bool IsAuditedCompatible => Confidence == CompatibilityConfidence.AuditedCompatible;
    public bool IsPendingValidation => SupportKind == RuntimeVersionSupportKind.SupportedAfterValidation;
    public bool IsProvisional => Confidence == CompatibilityConfidence.ProvisionalUnverified;
    public bool IsFallback => IsProvisional; // legacy compatibility name
    public bool IsKnownIncompatible => Confidence == CompatibilityConfidence.KnownIncompatible;
    public bool RuntimeAccepted => IsAuditedCompatible;
    public bool AuditedExact => IsAuditedExact;
    public bool RequiresCompatibilityWarning => IsProvisional || IsPendingValidation || IsKnownIncompatible;
    public bool IsGloballyIncompatible =>
        IsKnownIncompatible &&
        (Blocks(CompatibilityDomainMask.RuntimeBinding) ||
         (BlockedDomains & CompatibilityDomainMask.AllSearchDomains) == CompatibilityDomainMask.AllSearchDomains);
    public bool IsArchivedReference => SupportKind is
        RuntimeVersionSupportKind.ArchivedStableReference or
        RuntimeVersionSupportKind.ArchivedBetaReference;
    public bool IsHistoricalDonor => SupportKind == RuntimeVersionSupportKind.HistoricalBetaDonor;
    public bool IsHistoricalObsoleteBeta => IsHistoricalDonor || IsArchivedReference;

    /// <summary>
    /// Provisional versions are intentionally usable. Critical known incompatibilities
    /// and unsupported versions are not. A bounded known incompatibility may still enter
    /// the compiler so only requests touching its blocked domain fail closed.
    /// </summary>
    public bool IsUsable =>
        ProfileId is RuntimeProfileId.Beta110 or RuntimeProfileId.Beta111 &&
        (Confidence is CompatibilityConfidence.AuditedCompatible or CompatibilityConfidence.ProvisionalUnverified ||
         (IsKnownIncompatible && !IsGloballyIncompatible));

    public bool AllowsProductionSearch => IsUsable && !IsGloballyIncompatible;

    public bool Blocks(CompatibilityDomainMask requiredDomains) =>
        (BlockedDomains & requiredDomains) != CompatibilityDomainMask.None;

    public static RuntimeVersionResolution Unsupported(string detectedVersion, string evidenceCode) => new(
        GameVersionIdentity.From(detectedVersion),
        RuntimeProfileId.Unsupported,
        RngSemanticProfileId.Unsupported,
        CompatibilityConfidence.Unsupported,
        CompatibilityDomainMask.All,
        RuntimeVersionSupportKind.Unsupported,
        detectedVersion ?? string.Empty,
        string.Empty,
        evidenceCode);

    public static RuntimeVersionResolution ArchivedStable(string detectedVersion, RngSemanticProfileId semanticProfile) => new(
        GameVersionIdentity.From(detectedVersion),
        RuntimeProfileId.Unsupported,
        semanticProfile,
        CompatibilityConfidence.Unsupported,
        CompatibilityDomainMask.All,
        RuntimeVersionSupportKind.ArchivedStableReference,
        detectedVersion ?? string.Empty,
        detectedVersion ?? string.Empty,
        "archived-stable-reference:" + (detectedVersion ?? string.Empty));

    public static RuntimeVersionResolution HistoricalBetaDonor(string detectedVersion) => new(
        GameVersionIdentity.From(detectedVersion),
        RuntimeProfileId.Unsupported,
        RngSemanticProfileId.Modern109,
        CompatibilityConfidence.Unsupported,
        CompatibilityDomainMask.All,
        RuntimeVersionSupportKind.HistoricalBetaDonor,
        detectedVersion ?? string.Empty,
        detectedVersion ?? string.Empty,
        "historical-beta-donor:" + (detectedVersion ?? string.Empty));

    public static RuntimeVersionResolution ArchivedBeta(string detectedVersion, RngSemanticProfileId semanticProfile) => new(
        GameVersionIdentity.From(detectedVersion),
        RuntimeProfileId.Unsupported,
        semanticProfile,
        CompatibilityConfidence.Unsupported,
        CompatibilityDomainMask.All,
        RuntimeVersionSupportKind.ArchivedBetaReference,
        detectedVersion ?? string.Empty,
        detectedVersion ?? string.Empty,
        "archived-beta-reference:" + (detectedVersion ?? string.Empty));

    /// <summary>Policy-test/diagnostic constructor; it does not register a fake shipped version.</summary>
    public static RuntimeVersionResolution KnownIncompatible(
        GameVersionIdentity exactVersion,
        RuntimeProfileId attemptedProfile,
        RngSemanticProfileId attemptedSemanticProfile,
        CompatibilityDomainMask blockedDomains,
        string evidenceCode) => new(
        exactVersion,
        attemptedProfile,
        attemptedSemanticProfile,
        CompatibilityConfidence.KnownIncompatible,
        blockedDomains == CompatibilityDomainMask.None ? CompatibilityDomainMask.All : blockedDomains,
        RuntimeVersionSupportKind.KnownIncompatible,
        exactVersion.ExactVersion,
        string.Empty,
        evidenceCode);
}

/// <summary>
/// Exact game-version resolver. Exact patch identities are registered separately
/// from RNG semantic profiles. Unknown newer versions provisionally reuse the
/// latest known semantic implementation with an explicit warning; only known
/// semantic/API breaks are fail-closed.
/// </summary>
public static class RuntimeVersionCompatibility
{
    public const string Stable107Version0 = "0.107.0";
    public const string Stable107Version = "0.107.1";
    public const string Beta109Version0 = "0.109.0";
    public const string Beta109Version1 = "0.109.1";
    public const string Beta110PriorVersion = "0.110.0";
    public const string Beta110Version = "0.110.1";
    public const string Beta111Version = "0.111.0";
    public const string Stable111Version = "0.111.1";
    public const string Beta113Version = "0.113.0";
    public const string SupportedStableVersion = Stable111Version;
    public const string SupportedBetaVersion = "0.113.1";

    public const string LatestAuditedVersion = Beta111Version;
    public const string LatestProductionCompatibleVersion = SupportedBetaVersion;
    public const string LatestSourceAuditedVersion = Beta111Version;
    public const string LatestSemanticProfileBaseVersion = Beta110Version;
    public const string LatestKnownProfileVersion = Beta113Version;

    // Newest rule origin first. A patch can extend a release line without
    // introducing new mechanics. Keep the evidence for the original build.
    private static readonly RuntimeVersionResolution[] MaintainedRuleReferences =
    [
        new(new(Beta113Version), RuntimeProfileId.Beta111, RngSemanticProfileId.Modern113,
            CompatibilityConfidence.ProvisionalUnverified, CompatibilityDomainMask.None,
            RuntimeVersionSupportKind.ProvisionalVersionedRules, Beta113Version, Beta113Version,
            "provisional-versioned-rng:" + Beta113Version)
        { RulesAdaptedThroughVersion = SupportedBetaVersion, AdaptedReleaseChannel = GameReleaseChannel.Beta },
        new(new(Beta111Version), RuntimeProfileId.Beta111, RngSemanticProfileId.Modern110,
            CompatibilityConfidence.AuditedCompatible, CompatibilityDomainMask.None,
            RuntimeVersionSupportKind.AcceptedCompatible, Beta111Version, Beta111Version,
            "semantic-compatible-patch:source-audited+compile-load+authority-smoke:" + Beta111Version)
        { RulesAdaptedThroughVersion = SupportedStableVersion, AdaptedReleaseChannel = GameReleaseChannel.Stable }
    ];
    private static readonly RuntimeVersionResolution[] SupportedReleases =
        Array.ConvertAll(MaintainedRuleReferences, AdaptedRelease);

    internal static RuntimeVersionResolution AdaptedRelease(RuntimeVersionResolution rules)
    {
        string target = rules.RulesAdaptedThroughVersion;
        return rules with
        {
            GameVersionIdentity = new(target),
            DetectedVersion = target,
            IsReleaseSupported = true,
            Confidence = CompatibilityConfidence.AuditedCompatible,
            SupportKind = RuntimeVersionSupportKind.AcceptedCompatible,
            EvidenceCode = $"release-adapted:{target}->rules:{rules.ReferenceVersion}:{rules.SemanticProfileId}"
        };
    }

    public static bool IsStable107(string? normalizedGameVersion) =>
        string.Equals(normalizedGameVersion, Stable107Version, StringComparison.Ordinal);

    public static bool IsStable107_0(string? normalizedGameVersion) =>
        string.Equals(normalizedGameVersion, Stable107Version0, StringComparison.Ordinal);

    public static bool IsBeta109_0(string? normalizedGameVersion) =>
        string.Equals(normalizedGameVersion, Beta109Version0, StringComparison.Ordinal);

    public static bool IsBeta109_1(string? normalizedGameVersion) =>
        string.Equals(normalizedGameVersion, Beta109Version1, StringComparison.Ordinal);

    [Obsolete("Use exact IsBeta109_0 / IsBeta109_1 or Resolve; patch-family matching is not a compatibility audit.")]
    public static bool IsBeta109(string? normalizedGameVersion) => IsBeta109_0(normalizedGameVersion) || IsBeta109_1(normalizedGameVersion);

    public static bool IsBeta110(string? normalizedGameVersion) =>
        string.Equals(normalizedGameVersion, Beta110Version, StringComparison.Ordinal);

    public static bool IsBeta111(string? normalizedGameVersion) =>
        string.Equals(normalizedGameVersion, Beta111Version, StringComparison.Ordinal);

    public static bool UsesAccepted111Rules(string? version) =>
        version is Beta111Version or Stable111Version;

    public static RuntimeVersionResolution Resolve(string? normalizedGameVersion)
    {
        string normalized = GameVersionDetector.NormalizeVersion(normalizedGameVersion ?? string.Empty);
        GameVersionIdentity identity = GameVersionIdentity.From(normalized);
        if (!identity.IsExact)
            return RuntimeVersionResolution.Unsupported(normalized, string.IsNullOrWhiteSpace(normalized) ? "version-unavailable" : "version-not-exact:" + normalized);

        // Historical identities stay exact even when their Production implementation is archived.
        if (string.Equals(normalized, Stable107Version0, StringComparison.Ordinal))
            return RuntimeVersionResolution.ArchivedStable(normalized, RngSemanticProfileId.Stable107Legacy);
        if (string.Equals(normalized, Stable107Version, StringComparison.Ordinal))
            return RuntimeVersionResolution.ArchivedStable(normalized, RngSemanticProfileId.Stable107Legacy);
        if (string.Equals(normalized, Beta109Version0, StringComparison.Ordinal))
            return RuntimeVersionResolution.HistoricalBetaDonor(normalized);
        if (string.Equals(normalized, Beta109Version1, StringComparison.Ordinal))
            return RuntimeVersionResolution.HistoricalBetaDonor(normalized);
        if (string.Equals(normalized, Beta110PriorVersion, StringComparison.Ordinal))
            return RuntimeVersionResolution.ArchivedBeta(normalized, RngSemanticProfileId.Modern110);

        if (string.Equals(normalized, Beta110Version, StringComparison.Ordinal))
        {
            return new RuntimeVersionResolution(
                identity,
                RuntimeProfileId.Beta110,
                RngSemanticProfileId.Modern110,
                CompatibilityConfidence.AuditedCompatible,
                CompatibilityDomainMask.None,
                RuntimeVersionSupportKind.AuditedExact,
                normalized,
                Beta110Version,
                "audited-exact:" + Beta110Version)
            { RulesAdaptedThroughVersion = Beta110Version };
        }

        foreach (var release in SupportedReleases)
            if (normalized == release.DetectedVersion) return release;
        foreach (var rules in MaintainedRuleReferences)
            if (normalized == rules.ReferenceVersion) return rules;

        if (TryParseSemanticVersion(normalized, out Version detected))
        {
            foreach (var rules in MaintainedRuleReferences)
            {
                if (!TryParseSemanticVersion(rules.ReferenceVersion, out Version reference) || detected <= reference)
                    continue;
                return rules with
                {
                    GameVersionIdentity = identity,
                    Confidence = CompatibilityConfidence.ProvisionalUnverified,
                    SupportKind = RuntimeVersionSupportKind.LatestKnownFallback,
                    DetectedVersion = normalized,
                    EvidenceCode = $"provisional-latest-known-semantic-reuse:{normalized}->{rules.ReferenceVersion};semantic={rules.SemanticProfileId}"
                };
            }
        }

        return RuntimeVersionResolution.Unsupported(normalized, "unsupported-version:" + normalized);
    }

    public static RuntimeVersionResolution Resolve(GameVersionDetection detection)
    {
        ArgumentNullException.ThrowIfNull(detection);
        return detection.IsExact
            ? Resolve(detection.NormalizedVersion)
            : RuntimeVersionResolution.Unsupported(detection.NormalizedVersion, "version-detection-inexact");
    }

    public static bool UsesProfile(string? normalizedGameVersion, RuntimeProfileId profileId) => Resolve(normalizedGameVersion).ProfileId == profileId;
    public static bool UsesBeta109Profile(string? normalizedGameVersion) => UsesProfile(normalizedGameVersion, RuntimeProfileId.Beta109);
    public static bool UsesBeta110Profile(string? normalizedGameVersion) => UsesProfile(normalizedGameVersion, RuntimeProfileId.Beta110);
    public static bool UsesBeta111Profile(string? normalizedGameVersion) => UsesProfile(normalizedGameVersion, RuntimeProfileId.Beta111);
    public static bool IsFallback(string? normalizedGameVersion) => Resolve(normalizedGameVersion).IsFallback;
    public static bool IsArchivedReference(string? normalizedGameVersion) => Resolve(normalizedGameVersion).IsArchivedReference;
    public static bool IsHistoricalDonor(string? normalizedGameVersion) => Resolve(normalizedGameVersion).IsHistoricalDonor;

    [Obsolete("Use IsHistoricalDonor or IsArchivedReference.")]
    public static bool IsHistoricalObsoleteBeta(string? normalizedGameVersion) => Resolve(normalizedGameVersion).IsHistoricalObsoleteBeta;

    private static bool TryParseSemanticVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!GameVersionIdentity.IsExactVersion(value)) return false;
        if (!Version.TryParse(value, out Version? parsed) || parsed is null) return false;
        version = parsed;
        return true;
    }
}
