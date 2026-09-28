using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Compatibility;

/// <summary>
/// STS2 Beta 0.110.1 production profile. Seed/RNG primitives are intentionally shared
/// with the historical Beta109 donor; profile-specific semantic differences are
/// selected through RuntimeProfilePolicies.
/// </summary>
public sealed class Beta110Profile : IRuntimeProfile
{
    public static Beta110Profile Instance { get; } = new();

    private Beta110Profile() { }

    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta110;
    public int SeedLength => 12;
    public string SeedAlphabet => ProfileSeedRules.Alphabet;
    public RuntimeCapability Capability => RuntimeCapability.SeedAnalysisTypedNeowEffects;
    public bool SupportsSeedRng => true;

    public bool TryCanonicalizeSeed(string rawSeed, out string canonicalSeed, out string issue) =>
        ProfileSeedRules.TryCanonicalize(rawSeed, SeedLength, out canonicalSeed, out issue);

    public ulong ComputeRootSeed(string canonicalSeed) =>
        XxHash64.HashUtf8(canonicalSeed, 0UL);

    public ulong DeriveNamedStreamSeed(ulong rootSeed, string streamName) =>
        unchecked(rootSeed + XxHash64.HashUtf8(streamName ?? string.Empty, 0UL));

    public ulong DeriveEventStreamSeed(ulong rootSeed, int playerSlot, string eventEntry) =>
        unchecked(rootSeed + (ulong)playerSlot + XxHash64.HashUtf8(eventEntry ?? string.Empty, 0UL));
}
