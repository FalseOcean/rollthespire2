namespace RolltheSpire2.Compatibility;

/// <summary>
/// Stable serialized profile identity. New production code uses official channel
/// names. The obsolete aliases preserve compatibility with previously persisted
/// string enum values such as "Modern110".
/// </summary>
public enum RuntimeProfileId
{
    Stable107 = 0,
    Beta109 = 1,
    Beta110 = 2,
    Unsupported = 3,
    Beta111 = 4,

    [Obsolete("Use Stable107. This alias exists only for compatibility reads.")]
    Legacy107 = Stable107,

    [Obsolete("Use Beta109. This alias exists only for compatibility reads.")]
    Modern109 = Beta109,

    [Obsolete("Use Beta110. This alias exists only for compatibility reads.")]
    Modern110 = Beta110
}
