using System.Numerics;
using RolltheSpire2.Core.Rng;

const int CandidateCount = 131_072;
const int MerchantCount = 5;
const uint UncommonPoolCount = 37;
const uint RarePoolCount = 11;
const ulong ShopsHash = 0x9E3779B97F4A7C15UL;
const ulong PlayerSlot = 2UL;

long observationMismatch = 0;
long stateMismatch = 0;
long checkpointComparisons = 0;
string firstMismatch = string.Empty;

for (ulong candidate = 0; candidate < CandidateCount; candidate++)
{
    ulong source = candidate ^ 0xD1B54A32D192ED03UL;
    ulong root = SplitMix64.Next(ref source);
    var before = new ShaderRng(unchecked(root + PlayerSlot + ShopsHash));
    var after = new ShaderRng(unchecked(root + PlayerSlot + ShopsHash));

    for (int merchant = 1; merchant <= MerchantCount; merchant++)
    {
        for (int call = 0; call < 12; call++)
        {
            _ = before.NextDouble();
            after.Consume();
        }

        uint beforeUncommon = before.NextIntHistorical(UncommonPoolCount);
        uint afterUncommon = after.NextIntHistorical(UncommonPoolCount);
        _ = before.NextDouble();
        after.Consume();
        uint beforeRare = before.NextIntHistorical(RarePoolCount);
        uint afterRare = after.NextIntHistorical(RarePoolCount);

        for (int call = 0; call < 13; call++)
        {
            _ = before.NextDouble();
            after.Consume();
        }

        if (beforeUncommon != afterUncommon || beforeRare != afterRare)
        {
            observationMismatch++;
            RecordFirst($"candidate={candidate};merchant={merchant};observation");
        }

        checkpointComparisons++;
        if (before.State != after.State || before.CallCount != after.CallCount)
        {
            stateMismatch++;
            RecordFirst($"candidate={candidate};merchant={merchant};state");
        }
    }

    ulong beforeContinuation = before.NextUInt64();
    ulong afterContinuation = after.NextUInt64();
    if (beforeContinuation != afterContinuation || before.State != after.State)
    {
        stateMismatch++;
        RecordFirst($"candidate={candidate};continuation");
    }
}

Console.WriteLine(
    $"familySConsumeOnlyParity=true;candidates={CandidateCount};merchants={MerchantCount};" +
    $"checkpointComparisons={checkpointComparisons};observationMismatch={observationMismatch};" +
    $"rngStateMismatch={stateMismatch};firstMismatch={firstMismatch};" +
    $"result={(observationMismatch == 0 && stateMismatch == 0 ? "Pass" : "Fail")}");

return observationMismatch == 0 && stateMismatch == 0 ? 0 : 1;

void RecordFirst(string value)
{
    if (firstMismatch.Length == 0) firstMismatch = value;
}

internal sealed class ShaderRng
{
    private const double DoubleUnit = 1.1102230246251565E-16;
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    internal ShaderRng(ulong seed)
    {
        ulong state = seed;
        _s0 = SplitMix64.Next(ref state);
        _s1 = SplitMix64.Next(ref state);
        _s2 = SplitMix64.Next(ref state);
        _s3 = SplitMix64.Next(ref state);
    }

    internal int CallCount { get; private set; }
    internal (ulong S0, ulong S1, ulong S2, ulong S3) State => (_s0, _s1, _s2, _s3);

    internal ulong NextUInt64()
    {
        CallCount++;
        ulong result = BitOperations.RotateLeft(_s1 * 5UL, 7) * 9UL;
        AdvanceState();
        return result;
    }

    internal double NextDouble() => (NextUInt64() >> 11) * DoubleUnit;

    internal uint NextIntHistorical(uint bound)
    {
        ulong mantissa = NextUInt64() >> 11;
        return (uint)(((UInt128)mantissa * bound) >> 53);
    }

    internal void Consume()
    {
        CallCount++;
        AdvanceState();
    }

    private void AdvanceState()
    {
        ulong temporary = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= temporary;
        _s3 = BitOperations.RotateLeft(_s3, 45);
    }
}
