using RolltheSpire2.Compatibility;

namespace RolltheSpire2.Core.Prediction;

/// <summary>
/// Internal proof that a Beta110 analysis may begin from an already-computed RootHash.
/// A bound visible seed is optional. When supplied it is canonicalized and verified
/// against RootHash before the production core is entered.
/// </summary>
internal readonly record struct TrustedRootHashInput
{
    private TrustedRootHashInput(ulong rootHash, string canonicalSeed, bool hasCanonicalSeed)
    {
        RootHash = rootHash;
        CanonicalSeed = canonicalSeed;
        HasCanonicalSeed = hasCanonicalSeed;
    }

    public ulong RootHash { get; }
    public string CanonicalSeed { get; }
    public bool HasCanonicalSeed { get; }

    public string SeedIdentity => HasCanonicalSeed
        ? CanonicalSeed
        : $"root-hash:{RootHash:x16}";

    public static TrustedRootHashInput RootOnly(ulong rootHash) =>
        new(rootHash, string.Empty, hasCanonicalSeed: false);

    public static bool TryBindCanonicalSeed(
        IRuntimeProfile profile,
        ulong rootHash,
        string rawSeed,
        out TrustedRootHashInput input,
        out string issue)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.TryCanonicalizeSeed(rawSeed, out string canonicalSeed, out issue))
        {
            input = default;
            return false;
        }
        if (profile.ComputeRootSeed(canonicalSeed) != rootHash)
        {
            input = default;
            issue = "TrustedRootHashDoesNotMatchCanonicalSeed";
            return false;
        }

        input = new TrustedRootHashInput(rootHash, canonicalSeed, hasCanonicalSeed: true);
        issue = string.Empty;
        return true;
    }

    public static TrustedRootHashInput FromCanonicalSeed(
        IRuntimeProfile profile,
        string canonicalSeed)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSeed);
        return new TrustedRootHashInput(
            profile.ComputeRootSeed(canonicalSeed),
            canonicalSeed,
            hasCanonicalSeed: true);
    }
}
