namespace RolltheSpire2.Compatibility;

public sealed class UnsupportedProfile : IRuntimeProfile
{
    public static UnsupportedProfile Instance { get; } = new();

    private UnsupportedProfile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Unsupported;
    public int SeedLength => 0;
    public string SeedAlphabet => string.Empty;
    public RuntimeCapability Capability => RuntimeCapability.DisabledUnsupportedVersion;
    public bool SupportsSeedRng => false;

    public bool TryCanonicalizeSeed(string rawSeed, out string canonicalSeed, out string issue)
    {
        canonicalSeed = string.Empty;
        issue = "The detected game version does not have an audited runtime profile.";
        return false;
    }

    public ulong ComputeRootSeed(string canonicalSeed) => throw Disabled();
    public ulong DeriveNamedStreamSeed(ulong rootSeed, string streamName) => throw Disabled();
    public ulong DeriveEventStreamSeed(ulong rootSeed, int playerSlot, string eventEntry) => throw Disabled();

    private static InvalidOperationException Disabled() =>
        new("Unsupported profile: Seed/RNG execution is disabled fail-closed.");
}
