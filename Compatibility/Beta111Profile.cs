namespace RolltheSpire2.Compatibility;

/// <summary>
/// STS2 Beta 0.111.0 compatibility profile. The 0.111 source audit found the
/// visible-seed, xxHash64, SplitMix64, xoshiro256** and named-stream derivation
/// mechanisms semantically unchanged from Beta110. This wrapper deliberately
/// reuses those primitives while retaining an independent runtime profile identity.
/// </summary>
public sealed class Beta111Profile : IRuntimeProfile
{
    public static Beta111Profile Instance { get; } = new();

    private Beta111Profile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta111;
    public int SeedLength => Beta110Profile.Instance.SeedLength;
    public string SeedAlphabet => Beta110Profile.Instance.SeedAlphabet;
    public RuntimeCapability Capability => RuntimeCapability.SeedAnalysisTypedNeowEffects;
    public bool SupportsSeedRng => true;

    public bool TryCanonicalizeSeed(string rawSeed, out string canonicalSeed, out string issue) =>
        Beta110Profile.Instance.TryCanonicalizeSeed(rawSeed, out canonicalSeed, out issue);

    public ulong ComputeRootSeed(string canonicalSeed) =>
        Beta110Profile.Instance.ComputeRootSeed(canonicalSeed);

    public ulong DeriveNamedStreamSeed(ulong rootSeed, string streamName) =>
        Beta110Profile.Instance.DeriveNamedStreamSeed(rootSeed, streamName);

    public ulong DeriveEventStreamSeed(ulong rootSeed, int playerSlot, string eventEntry) =>
        Beta110Profile.Instance.DeriveEventStreamSeed(rootSeed, playerSlot, eventEntry);
}
