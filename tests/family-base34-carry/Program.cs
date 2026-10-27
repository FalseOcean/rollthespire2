using System.Buffers.Binary;
using System.Diagnostics;
using RolltheSpire2.Core.Seed;

const int SeedLength = 12;
const ulong Radix = 34;
const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

ulong spaceSize = Pow34(SeedLength);
var starts = new HashSet<ulong>
{
    0,
    1,
    32,
    33,
    34,
    35,
    (1UL << 24) - 4,
    1UL << 24,
    (1UL << 32) + 29,
    spaceSize - 8
};
for (int exponent = 1; exponent <= SeedLength; exponent++)
{
    ulong boundary = Pow34(exponent);
    if (boundary > 0) starts.Add(boundary - 1);
    if (boundary < spaceSize) starts.Add(boundary);
}

long seedMismatch = 0;
long rootMismatch = 0;
long compared = 0;
string firstMismatch = string.Empty;
foreach (ulong start in starts.Order())
    CompareRange(start, checked((int)Math.Min(257UL, spaceSize - start)));

// Large contiguous windows cover ordinary propagation and non-zero global/batch bases.
foreach (ulong start in new[] { 0UL, Pow34(6) - 131, (1UL << 32) + 17, spaceSize - 262_144 })
    CompareRange(start, 262_144);

// Explicit maximum-length carry wraps the 12-byte base34 representation.
PackedSeed maximum = EncodePacked(spaceSize - 1);
Advance(ref maximum);
if (!maximum.Bytes().SequenceEqual(new byte[SeedLength].Select((_, i) => (byte)'0')))
    throw new InvalidDataException("MaximumLengthCarryDidNotWrap");

Console.WriteLine(
    $"familyBase34CarryParity=true;candidates={compared};seedMismatch={seedMismatch};" +
    $"rootHashMismatch={rootMismatch};boundaryStarts={starts.Count};firstMismatch={firstMismatch};" +
    $"result={(seedMismatch == 0 && rootMismatch == 0 ? "Pass" : "Fail")}");
if (seedMismatch != 0 || rootMismatch != 0) return 1;

// CPU-only diagnostic: useful for catching accidental arithmetic regressions. GPU A/B remains runtime evidence.
const int benchmarkCandidates = 2_000_000;
ulong benchmarkStart = Pow34(7) - 123;
var timer = Stopwatch.StartNew();
ulong baselineSink = 0;
for (int index = 0; index < benchmarkCandidates; index++)
    baselineSink ^= XxHash64.Hash(FullDecode(benchmarkStart + (ulong)index), 0);
timer.Stop();
double baselineMs = timer.Elapsed.TotalMilliseconds;

timer.Restart();
ulong carrySink = 0;
for (int group = 0; group < benchmarkCandidates; group += 8)
{
    PackedSeed seed = EncodePacked(benchmarkStart + (ulong)group);
    int count = Math.Min(8, benchmarkCandidates - group);
    for (int lane = 0; lane < count; lane++)
    {
        carrySink ^= XxHash64.Hash(seed.Bytes(), 0);
        if (lane + 1 < count) Advance(ref seed);
    }
}
timer.Stop();
double carryMs = timer.Elapsed.TotalMilliseconds;
if (baselineSink != carrySink) throw new InvalidDataException("BenchmarkChecksumMismatch");
Console.WriteLine(
    $"familyBase34CarryCpuDiagnostic=true;candidates={benchmarkCandidates};baselineMs={baselineMs:F3};" +
    $"carryMs={carryMs:F3};baselineCandidatesPerSecond={benchmarkCandidates * 1000d / baselineMs:F3};" +
    $"carryCandidatesPerSecond={benchmarkCandidates * 1000d / carryMs:F3};" +
    $"relativeUplift={(baselineMs / carryMs - 1d) * 100d:F3}%;gpuEvidenceRequired=true");
return 0;

void CompareRange(ulong start, int count)
{
    PackedSeed carried = EncodePacked(start);
    for (int offset = 0; offset < count; offset++)
    {
        ulong ordinal = start + (ulong)offset;
        byte[] baseline = FullDecode(ordinal);
        byte[] actual = carried.Bytes();
        if (!baseline.AsSpan().SequenceEqual(actual))
        {
            seedMismatch++;
            if (firstMismatch.Length == 0) firstMismatch = $"Seed@{ordinal}";
        }
        ulong expectedHash = XxHash64.Hash(baseline, 0);
        ulong actualHash = XxHash64.Hash(actual, 0);
        if (expectedHash != actualHash)
        {
            rootMismatch++;
            if (firstMismatch.Length == 0) firstMismatch = $"RootHash@{ordinal}";
        }
        compared++;
        if (offset + 1 < count) Advance(ref carried);
    }
}

byte[] FullDecode(ulong ordinal)
{
    var bytes = new byte[SeedLength];
    for (int index = SeedLength - 1; index >= 0; index--)
    {
        bytes[index] = (byte)Alphabet[(int)(ordinal % Radix)];
        ordinal /= Radix;
    }
    return bytes;
}

PackedSeed EncodePacked(ulong ordinal)
{
    byte[] bytes = FullDecode(ordinal);
    return new PackedSeed(
        BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)));
}

void Advance(ref PackedSeed seed)
{
    for (int index = SeedLength - 1; index >= 0; index--)
    {
        byte current = seed.Get(index);
        int digit = Alphabet.IndexOf((char)current);
        if (digit < 0) throw new InvalidDataException($"InvalidPackedSeedByte:{current}");
        if (digit + 1 < Alphabet.Length)
        {
            seed.Set(index, (byte)Alphabet[digit + 1]);
            return;
        }
        seed.Set(index, (byte)Alphabet[0]);
    }
}

ulong Pow34(int exponent)
{
    ulong value = 1;
    for (int index = 0; index < exponent; index++) value = checked(value * Radix);
    return value;
}

struct PackedSeed(ulong first8, uint tail4)
{
    public ulong First8 = first8;
    public uint Tail4 = tail4;

    public readonly byte Get(int index) => index < 8
        ? (byte)(First8 >> (index * 8))
        : (byte)(Tail4 >> ((index - 8) * 8));

    public void Set(int index, byte value)
    {
        if (index < 8)
        {
            int shift = index * 8;
            First8 = (First8 & ~(0xffUL << shift)) | ((ulong)value << shift);
        }
        else
        {
            int shift = (index - 8) * 8;
            Tail4 = (Tail4 & ~(0xffU << shift)) | ((uint)value << shift);
        }
    }

    public readonly byte[] Bytes()
    {
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, First8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), Tail4);
        return bytes;
    }
}
