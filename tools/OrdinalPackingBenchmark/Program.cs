using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Search.FamilyExecution;

// 直接測正式打包器；耗時只代表 CPU 序列化，不代表搜尋吞吐。
int assertions = 0;
Check(Array.Empty<ulong>());
Check(new ulong[] { 0 });
Check(new ulong[] { 0, 1, 255, 256, 65535, 65536, 0x12345678, uint.MaxValue });
ulong[] sliceBacking = [ulong.MaxValue, 0, 7, 0x87654321, uint.MaxValue, ulong.MaxValue];
Check(sliceBacking.AsMemory(1, 4));
if (BitConverter.IsLittleEndian)
{
    Require(FamilyGpuOrdinalPacking.ToBytes([0x12345678]).AsSpan().SequenceEqual(new byte[] { 0x78, 0x56, 0x34, 0x12 }),
        "Known little-endian uint storage value");
}
foreach (ulong invalid in new[] { (ulong)uint.MaxValue + 1, ulong.MaxValue })
{
    Require(ThrowsOverflow(() => Original(new ulong[] { 0, invalid, 1 })), "Original rejects ordinal overflow");
    Require(ThrowsOverflow(() => FamilyGpuOrdinalPacking.ToBytes(new ulong[] { 0, invalid, 1 })), "Packer rejects ordinal overflow");
}
var random = new Random(0x525432);
for (int trial = 0; trial < 200; trial++)
{
    var values = new ulong[random.Next(0, 8193)];
    for (int i = 0; i < values.Length; i++) values[i] = (ulong)random.NextInt64(0, (long)uint.MaxValue + 1);
    Check(values);
}
if (args.Contains("--full-capacity"))
{
    var values = new ulong[1 << 24];
    for (int i = 0; i < values.Length; i++) values[i] = (ulong)i;
    Check(values);
}

var measurements = new List<object>();
foreach ((int count, int iterations) in new[] { (1024, 128), (65536, 16), (1048576, 4) })
{
    var values = new ulong[count];
    for (int i = 0; i < values.Length; i++) values[i] = (ulong)i;
    ReadOnlyMemory<ulong> input = values;
    Check(input);
    // 先暖機，減少分層 JIT 對量測的影響。
    for (int i = 0; i < 64; i++)
    {
        Consume(Original(input));
        Consume(FamilyGpuOrdinalPacking.ToBytes(input.Span));
    }
    var original = new List<Measurement>();
    var direct = new List<Measurement>();
    for (int round = 0; round < 5; round++)
    {
        if ((round & 1) == 0)
        {
            original.Add(Measure(Original, input, iterations));
            direct.Add(Measure(m => FamilyGpuOrdinalPacking.ToBytes(m.Span), input, iterations));
        }
        else
        {
            direct.Add(Measure(m => FamilyGpuOrdinalPacking.ToBytes(m.Span), input, iterations));
            original.Add(Measure(Original, input, iterations));
        }
    }
    measurements.Add(new
    {
        CandidateCount = count,
        IterationsPerRound = iterations,
        Rounds = original.Count,
        Original = original,
        Direct = direct,
        OriginalMedianMillisecondsPerCall = Median(original.Select(m => m.MillisecondsPerCall)),
        DirectMedianMillisecondsPerCall = Median(direct.Select(m => m.MillisecondsPerCall)),
        OriginalMedianAllocatedBytesPerCall = Median(original.Select(m => m.AllocatedBytesPerCall)),
        DirectMedianAllocatedBytesPerCall = Median(direct.Select(m => m.AllocatedBytesPerCall)),
    });
}
var result = new
{
    Schema = "rt2-ordinal-packing-microbenchmark-v1",
    TimestampUtc = DateTimeOffset.UtcNow,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    IsLittleEndian = BitConverter.IsLittleEndian,
    AssertionsPassed = assertions,
    FullCapacityCase = args.Contains("--full-capacity"),
    Scope = "CPU compact-ordinal serialization only; no GPU, seed evaluation, or end-to-end speedup measured.",
    Measurements = measurements,
};
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
string? output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..];
if (output is not null) File.WriteAllText(output, json);
Console.WriteLine(json);

void Check(ReadOnlyMemory<ulong> values)
{
    byte[] expected = Original(values);
    byte[] actual = FamilyGpuOrdinalPacking.ToBytes(values.Span);
    Require(actual.Length == checked(values.Length * sizeof(uint)), "Payload byte count");
    Require(expected.AsSpan().SequenceEqual(actual), "Production payload matches original conversion");
}

void Require(bool passed, string check)
{
    if (!passed) throw new InvalidOperationException("FAILED: " + check);
    assertions++;
}

static byte[] Original(ReadOnlyMemory<ulong> ordinals)
{
    // 保留原上傳轉換，包含最後一次 BlockCopy。
    uint[] values = ordinals.ToArray().Select(x => checked((uint)x)).ToArray();
    byte[] bytes = new byte[checked(values.Length * sizeof(uint))];
    Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
    return bytes;
}

static bool ThrowsOverflow(Action action)
{
    try { action(); return false; }
    catch (OverflowException) { return true; }
}

static Measurement Measure(Func<ReadOnlyMemory<ulong>, byte[]> convert, ReadOnlyMemory<ulong> input, int iterations)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long before = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < iterations; i++) Consume(convert(input));
    double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    return new Measurement(elapsed / iterations, (double)allocated / iterations);
}

static void Consume(byte[] bytes) => GC.KeepAlive(bytes);
static double Median(IEnumerable<double> values)
{
    double[] sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}
internal sealed record Measurement(double MillisecondsPerCall, double AllocatedBytesPerCall);
