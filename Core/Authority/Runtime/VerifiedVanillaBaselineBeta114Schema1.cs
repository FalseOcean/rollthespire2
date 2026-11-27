namespace RolltheSpire2.Core.Authority.Runtime;

internal static class VerifiedVanillaBaselineBeta114Schema1
{
    internal static readonly VerifiedVanillaBaseline Value = FromCapture();

    private static VerifiedVanillaBaseline FromCapture()
    {
        // The 0.114.0 capture matches 0.113.1 in every domain except Cards.
        var previous = VerifiedVanillaBaselineBeta113Schema1.Value;
        const string cards = RuntimeAuthorityDomains.Cards;
        var unlocks = previous.VanillaUnlockUniverse[cards]
            .Where(id => id != "CARD:RAPID_SHOT").Append("CARD:OPTIMISM").Append("CARD:TWIN_SHOT")
            .Order(StringComparer.Ordinal).ToArray();
        return previous with
        {
            GameVersion = "0.114.0",
            DomainHashes = new Dictionary<string, string>(previous.DomainHashes, StringComparer.Ordinal)
            {
                [cards] = "bfc7e54851a9a55d95b8f68d3d59370003822f0e5d2c48977871d7ce276ac9c2"
            },
            VanillaUnlockUniverse = new Dictionary<string, IReadOnlyList<string>>(previous.VanillaUnlockUniverse, StringComparer.Ordinal)
            {
                [cards] = unlocks
            },
            UnlockUniverseHash = "cefe9a3437f47b4566cdc99d60d348235f524660d91d9b39d6eea6e31dc13f33",
            OverallSemanticHash = "ba569f656bb9a09467d4740e1002c628287fa8ea1c07e9a9ceee892d37ffdb3c",
            VerificationEvidence = "Vanilla114Schema1:a7b9a9c0:2026-11-25:candidate-sha256=53aa1f91ef961940e82e11120a65204ef9dd8f8a2b5dc1b11073f8e69f5bdc23"
        };
    }
}
