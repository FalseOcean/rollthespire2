using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Search.Semantics;

public sealed record FishingRodDrawRequirement(int DrawOrdinal, int PoolSize);

internal static class FishingRodDrawResearch
{
    internal const string Id = "FishingRodDrawResearch";
    internal const string UnsupportedCode = Id + ".SinglePlayerModern113Required";
    internal const string InvalidRequirementsCode = Id + ".InvalidRequirements";
    internal const string Stream = "non_combat_generation";
    private const string LegacySemanticIdentity = Id + ":Modern113:Solo:Call3=11/11:Call5=5/5";
    private static readonly ulong StreamSalt = Beta111Profile.Instance.DeriveNamedStreamSeed(0, Stream);
    internal static IReadOnlyList<FishingRodDrawRequirement> DefaultRequirements { get; } =
        Array.AsReadOnly<FishingRodDrawRequirement>([new(3, 11), new(5, 5)]);

    internal static bool AreValidRequirements(IReadOnlyList<FishingRodDrawRequirement>? rows) =>
        rows is null || rows.Count > 0 && rows.All(row => row is { DrawOrdinal: > 0, PoolSize: > 0 });

    internal static IReadOnlyList<FishingRodDrawRequirement>? NormalizeRequirements(IReadOnlyList<FishingRodDrawRequirement>? rows)
    {
        if (rows is null) return null;
        // Last-slot intervals at one draw are nested. Keep the largest pool's interval.
        var normalized = rows.GroupBy(row => row.DrawOrdinal).OrderBy(group => group.Key)
            .Select(group => new FishingRodDrawRequirement(group.Key, group.Max(row => row.PoolSize))).ToArray();
        return normalized.SequenceEqual(DefaultRequirements) ? null : Array.AsReadOnly(normalized);
    }

    internal static string SemanticIdentity(IReadOnlyList<FishingRodDrawRequirement>? rows) =>
        rows is null ? LegacySemanticIdentity : Id + ":Modern113:Solo:" + OutcomeIdentity(rows);

    internal static string OutcomeIdentity(IReadOnlyList<FishingRodDrawRequirement> rows) => string.Join(';',
        rows.Select(row => FormattableString.Invariant($"call{row.DrawOrdinal}={row.PoolSize}/{row.PoolSize}")));

    internal static bool Matches(ulong rootHash, IReadOnlyList<FishingRodDrawRequirement> rows)
    {
        var stream = new Xoshiro256StarStar(unchecked(rootHash + StreamSalt));
        foreach (var row in rows)
        {
            int gap = row.DrawOrdinal - stream.CallCount - 1;
            stream = FishingRodDrawAdvance.Advance(stream, gap);
            if (stream.NextInt(row.PoolSize) != row.PoolSize - 1) return false;
        }
        return true;
    }
}
