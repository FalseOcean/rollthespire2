namespace RolltheSpire2.Compatibility;

public interface IRuntimeProfile
{
    RuntimeProfileId ProfileId { get; }
    int SeedLength { get; }
    string SeedAlphabet { get; }
    RuntimeCapability Capability { get; }
    bool SupportsSeedRng { get; }

    bool TryCanonicalizeSeed(string rawSeed, out string canonicalSeed, out string issue);
    ulong ComputeRootSeed(string canonicalSeed);
    ulong DeriveNamedStreamSeed(ulong rootSeed, string streamName);
    ulong DeriveEventStreamSeed(ulong rootSeed, int playerSlot, string eventEntry);
}
