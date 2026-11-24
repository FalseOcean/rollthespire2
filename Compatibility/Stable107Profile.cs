using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Compatibility;

public sealed class Stable107Profile : IRuntimeProfile
{
    public static Stable107Profile Instance { get; } = new();

    private Stable107Profile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Stable107;
    public int SeedLength => 10;
    public string SeedAlphabet => ProfileSeedRules.Alphabet;
    public RuntimeCapability Capability => RuntimeCapability.SeedAnalysisTypedNeowEffects;
    public bool SupportsSeedRng => true;

    public bool TryCanonicalizeSeed(string rawSeed, out string canonicalSeed, out string issue) =>
        ProfileSeedRules.TryCanonicalize(rawSeed, SeedLength, out canonicalSeed, out issue);

    public ulong ComputeRootSeed(string canonicalSeed) =>
        LegacyDeterministicHash32.Hash(canonicalSeed);

    public ulong DeriveNamedStreamSeed(ulong rootSeed, string streamName)
    {
        uint root = checked((uint)rootSeed);
        uint streamHash = LegacyDeterministicHash32.Hash(streamName ?? string.Empty);
        return unchecked(root + streamHash);
    }

    public ulong DeriveEventStreamSeed(ulong rootSeed, int playerSlot, string eventEntry)
    {
        uint root = checked((uint)rootSeed);
        uint eventHash = LegacyDeterministicHash32.Hash(eventEntry ?? string.Empty);
        return unchecked(root + (uint)playerSlot + eventHash);
    }
}
