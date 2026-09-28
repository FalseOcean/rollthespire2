using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;

namespace RolltheSpire2.Core.Verification;

public sealed record SeedRngVectorCase(string Name, bool Passed, string Expected, string Actual);

public sealed record SeedRngVectorVerification(IReadOnlyList<SeedRngVectorCase> Cases)
{
    public bool Passed => Cases.Count > 0 && Cases.All(test => test.Passed);
    public static SeedRngVectorVerification NotRun { get; } = new(Array.Empty<SeedRngVectorCase>());
}

public static class SeedRngVectorVerifier
{
    public static SeedRngVectorVerification RunAll()
    {
        var cases = new List<SeedRngVectorCase>();
        Stable107Profile legacy = Stable107Profile.Instance;
        Beta109Profile modern = Beta109Profile.Instance;
        Beta110Profile beta110 = Beta110Profile.Instance;

        Add(cases, "legacy canonicalization", SeedRngVectorCatalog.LegacySeed,
            Canonicalize(legacy, " H0T0Z0S23C "));
        AddHex(cases, "legacy root hash", SeedRngVectorCatalog.LegacyRoot,
            legacy.ComputeRootSeed(SeedRngVectorCatalog.LegacySeed));
        AddHex(cases, "legacy up_front stream", SeedRngVectorCatalog.LegacyUpFrontSeed,
            legacy.DeriveNamedStreamSeed(SeedRngVectorCatalog.LegacyRoot, "up_front"));
        AddSequence(cases, "legacy Xoshiro outputs", SeedRngVectorCatalog.LegacyRootOutputs,
            Draw(SeedRngVectorCatalog.LegacyRoot, SeedRngVectorCatalog.LegacyRootOutputs.Length));

        Add(cases, "modern canonicalization", "010101010101", Canonicalize(modern, " OIOIOIOIOIOI "));
        AddHex(cases, "xxHash64 empty", 0xEF46DB3751D8E999UL, XxHash64.HashUtf8(string.Empty, 0UL));
        AddHex(cases, "modern root hash", SeedRngVectorCatalog.ModernRoot,
            modern.ComputeRootSeed(SeedRngVectorCatalog.ModernSeed));
        AddHex(cases, "modern NEOW event stream", SeedRngVectorCatalog.ModernEventSeed,
            modern.DeriveEventStreamSeed(SeedRngVectorCatalog.ModernRoot, 0, "NEOW"));
        AddHex(cases, "modern up_front stream", SeedRngVectorCatalog.ModernUpFrontSeed,
            modern.DeriveNamedStreamSeed(SeedRngVectorCatalog.ModernRoot, "up_front"));
        Add(cases, "beta110 canonicalization parity", SeedRngVectorCatalog.ModernSeed,
            Canonicalize(beta110, SeedRngVectorCatalog.ModernSeed));
        AddHex(cases, "beta110 root hash parity", SeedRngVectorCatalog.ModernRoot,
            beta110.ComputeRootSeed(SeedRngVectorCatalog.ModernSeed));
        AddHex(cases, "beta110 NEOW event stream parity", SeedRngVectorCatalog.ModernEventSeed,
            beta110.DeriveEventStreamSeed(SeedRngVectorCatalog.ModernRoot, 0, "NEOW"));
        AddHex(cases, "beta110 up_front stream parity", SeedRngVectorCatalog.ModernUpFrontSeed,
            beta110.DeriveNamedStreamSeed(SeedRngVectorCatalog.ModernRoot, "up_front"));

        var eventRng = new Xoshiro256StarStar(SeedRngVectorCatalog.ModernEventSeed);
        (ulong s0, ulong s1, ulong s2, ulong s3) = eventRng.State;
        AddSequence(cases, "modern event initial state", SeedRngVectorCatalog.ModernEventInitialState,
            new[] { s0, s1, s2, s3 });
        AddSequence(cases, "modern event Xoshiro outputs", SeedRngVectorCatalog.ModernEventOutputs,
            Draw(eventRng, SeedRngVectorCatalog.ModernEventOutputs.Length));

        bool invalidRejected = !modern.TryCanonicalizeSeed("12345678901!", out _, out _);
        cases.Add(new SeedRngVectorCase("modern invalid alphabet fail-closed", invalidRejected, "rejected", invalidRejected ? "rejected" : "accepted"));
        return new SeedRngVectorVerification(cases);
    }

    private static string Canonicalize(IRuntimeProfile profile, string raw)
    {
        return profile.TryCanonicalizeSeed(raw, out string canonical, out string issue)
            ? canonical
            : "ERROR:" + issue;
    }

    private static ulong[] Draw(ulong seed, int count) => Draw(new Xoshiro256StarStar(seed), count);

    private static ulong[] Draw(Xoshiro256StarStar rng, int count)
    {
        var values = new ulong[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = rng.NextUInt64();
        }

        return values;
    }

    private static void Add(ICollection<SeedRngVectorCase> cases, string name, string expected, string actual) =>
        cases.Add(new SeedRngVectorCase(name, string.Equals(expected, actual, StringComparison.Ordinal), expected, actual));

    private static void AddHex(ICollection<SeedRngVectorCase> cases, string name, ulong expected, ulong actual) =>
        Add(cases, name, $"0x{expected:X16}", $"0x{actual:X16}");

    private static void AddSequence(ICollection<SeedRngVectorCase> cases, string name, IReadOnlyList<ulong> expected, IReadOnlyList<ulong> actual)
    {
        string expectedText = string.Join(",", expected.Select(value => value.ToString("X16")));
        string actualText = string.Join(",", actual.Select(value => value.ToString("X16")));
        Add(cases, name, expectedText, actualText);
    }
}
