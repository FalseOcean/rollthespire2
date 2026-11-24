namespace RolltheSpire2.Compatibility;

/// <summary>
/// Exact official game-version identity. This is deliberately independent from
/// RuntimeProfileId/RngSemanticProfileId: two exact patch versions may share one
/// semantic implementation only after source audit says they do.
/// </summary>
public readonly record struct GameVersionIdentity(string ExactVersion)
{
    public bool IsExact => !string.IsNullOrWhiteSpace(ExactVersion);
    [Obsolete("Use IsExact. Audit/compatibility confidence is a separate dimension.")]
    public bool IsKnown => IsExact;
    public override string ToString() => IsExact ? ExactVersion : "unknown";

    public static GameVersionIdentity From(string? version)
    {
        string normalized = GameVersionDetector.NormalizeVersion(version ?? string.Empty);
        return IsExactVersion(normalized)
            ? new GameVersionIdentity(normalized)
            : Unknown;
    }

    public static bool IsExactVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length is 3 or 4 && parts.All(part => int.TryParse(part, out int parsed) && parsed >= 0);
    }

    public static GameVersionIdentity Unknown { get; } = new(string.Empty);

    public static GameVersionIdentity V0_107_0 { get; } = new("0.107.0");
    public static GameVersionIdentity V0_107_1 { get; } = new("0.107.1");
    public static GameVersionIdentity V0_109_0 { get; } = new("0.109.0");
    public static GameVersionIdentity V0_109_1 { get; } = new("0.109.1");
    public static GameVersionIdentity V0_110_0 { get; } = new("0.110.0");
    public static GameVersionIdentity V0_110_1 { get; } = new("0.110.1");
    public static GameVersionIdentity V0_111_0 { get; } = new("0.111.0");
}

public enum CompatibilityConfidence : byte
{
    AuditedCompatible = 0,
    ProvisionalUnverified = 1,
    KnownIncompatible = 2,
    Unsupported = 3
}

[Flags]
public enum CompatibilityDomainMask : ushort
{
    None = 0,
    Neow = 1 << 0,
    RelicSequence = 1 << 1,
    WorldEventAncient = 1 << 2,
    CombatReward = 1 << 3,
    RuntimeBinding = 1 << 4,
    AllSearchDomains = Neow | RelicSequence | WorldEventAncient | CombatReward,
    All = AllSearchDomains | RuntimeBinding
}
