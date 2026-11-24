namespace RolltheSpire2.Core.Verification;

/// <summary>
/// Golden values imported before the Rewrite R0 implementation was created.
/// Provenance is documented in tests/seed-vectors/VECTOR_SOURCES.md.
/// </summary>
public static class SeedRngVectorCatalog
{
    public const string LegacySeed = "H0T0Z0S23C";
    public const ulong LegacyRoot = 0x00000000BF7EE117UL;
    public const ulong LegacyUpFrontSeed = 0x0000000075E62272UL;
    public static readonly ulong[] LegacyRootOutputs =
    {
        0x9D0229E3535C9D10UL,
        0xEE3788F9D48C3795UL,
        0xF33397A4B2727627UL,
        0x1F8757F632173B59UL
    };

    public const string ModernSeed = "123456789012";
    public const ulong ModernRoot = 0xB5CEE4C943D43C7EUL;
    public const ulong ModernEventSeed = 0xCF67818397849AEBUL;
    public const ulong ModernUpFrontSeed = 0xD059602A1DF87AFEUL;
    public static readonly ulong[] ModernEventInitialState =
    {
        0xD0AB234A6134A22AUL,
        0x594AD9931D697A6CUL,
        0x7537F04C0A9C1268UL,
        0xA1335B384CC6DF26UL
    };
    public static readonly ulong[] ModernEventOutputs =
    {
        0x141F6E15C5428157UL,
        0xCFEE22F008450F38UL,
        0xB8DB23163BFD9196UL,
        0xA035218EDE9BFCAFUL
    };
}
