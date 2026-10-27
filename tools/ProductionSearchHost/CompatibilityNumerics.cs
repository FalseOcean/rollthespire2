using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Godot;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static partial class CompatibilityChecks
{
    private sealed record NumericInput(ulong Raw, uint Bound, string Group);
    private sealed record NumericReadback(uint[] Words, string Device, string Vendor);

    internal static async Task CheckGpuNumericsAsync(string output)
    {
        const int randomCount = 65536;
        const int randomSeed = 20261025;
        var inputs = BuildNumericInputs(randomCount, randomSeed);
        var inputWords = new uint[checked(inputs.Count * 3)];
        var expected = new uint[checked(inputs.Count * 2)];
        const double unit = 1.1102230246251565e-16;
        for (int i = 0; i < inputs.Count; i++)
        {
            NumericInput row = inputs[i];
            inputWords[i * 3] = (uint)row.Raw;
            inputWords[i * 3 + 1] = (uint)(row.Raw >> 32);
            inputWords[i * 3 + 2] = row.Bound;
            // Keep the authoritative binary64 multiply and final conversions
            // separate. No candidate integer helper participates in this oracle.
            double sample = (row.Raw >> 11) * unit;
            double scaled = sample * row.Bound;
            expected[i * 2] = checked((uint)scaled);
            expected[i * 2 + 1] = BitConverter.SingleToUInt32Bits((float)sample);
        }

        string nextInt = GpuIntegerNumerics.NextIntSource;
        string unitFloat = GpuIntegerNumerics.UnitFloatSource;
        string source = NumericShaderSource(nextInt, unitFloat);
        NumericReadback actual;
        await using (var context = new FamilyExecutionContext(requiresGpu: true))
            actual = await context.ExecuteGpuAsync(rd => ExecuteNumericProbe(rd, source, inputWords), CancellationToken.None);

        int integerFailures = 0, floatFailures = 0;
        var mismatches = new List<object>();
        for (int i = 0; i < inputs.Count; i++)
        {
            bool integerMatch = expected[i * 2] == actual.Words[i * 2];
            bool floatMatch = expected[i * 2 + 1] == actual.Words[i * 2 + 1];
            if (!integerMatch) integerFailures++;
            if (!floatMatch) floatFailures++;
            if ((!integerMatch || !floatMatch) && mismatches.Count < 16)
                mismatches.Add(new { Index = i, inputs[i].Group, RawHex = inputs[i].Raw.ToString("X16"),
                    inputs[i].Bound, ExpectedNextInt = expected[i * 2], ActualNextInt = actual.Words[i * 2],
                    ExpectedFloatBits = expected[i * 2 + 1].ToString("X8"), ActualFloatBits = actual.Words[i * 2 + 1].ToString("X8") });
        }

        byte[] inputBytes = FamilyGpuComputeUtility.ToBytes(inputWords);
        byte[] expectedBytes = FamilyGpuComputeUtility.ToBytes(expected);
        byte[] actualBytes = FamilyGpuComputeUtility.ToBytes(actual.Words);
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "gpu-integer-numerics.input.bin"), inputBytes);
        File.WriteAllBytes(Path.Combine(output, "gpu-integer-numerics.expected.bin"), expectedBytes);
        File.WriteAllBytes(Path.Combine(output, "gpu-integer-numerics.actual.bin"), actualBytes);
        File.WriteAllText(Path.Combine(output, "gpu-integer-numerics.glsl"), source);
        uint historical = (uint)(((UInt128)(inputs[0].Raw >> 11) * inputs[0].Bound) >> 53);
        bool counterexamplePassed = historical == 1 && expected[0] == 2 && actual.Words[0] == 2;
        bool passed = integerFailures == 0 && floatFailures == 0 && counterexamplePassed;
        WriteEvidence(Path.Combine(output, "gpu-integer-numerics.json"), new
        {
            Passed = passed, InputCount = inputs.Count, NextIntChecks = inputs.Count, UnitFloatChecks = inputs.Count,
            NextIntFailures = integerFailures, UnitFloatFailures = floatFailures,
            Groups = inputs.GroupBy(row => row.Group).ToDictionary(group => group.Key, group => group.Count()),
            SmallBoundChecks = inputs.Count(row => row.Bound <= 2048),
            FullUnsignedBoundChecks = inputs.Count(row => row.Bound > int.MaxValue),
            RandomCount = randomCount, RandomSeed = randomSeed, RandomRuntime = System.Environment.Version.ToString(),
            ActualDevice = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(),
            LocalRenderingDevice = actual.Device, LocalVendor = actual.Vendor,
            Initialization = FamilyDeviceProfileFoundation.Initialization.Capture(),
            SourceSha256 = NumericHash(Encoding.UTF8.GetBytes(source)),
            NextIntHelperSha256 = NumericHash(Encoding.UTF8.GetBytes(nextInt)),
            UnitFloatHelperSha256 = NumericHash(Encoding.UTF8.GetBytes(unitFloat)),
            InputSha256 = NumericHash(inputBytes), ExpectedSha256 = NumericHash(expectedBytes), ActualSha256 = NumericHash(actualBytes),
            InputLayout = "Little-endian uint32 triples: raw low word, raw high word, positive bound",
            OutputLayout = "Little-endian uint32 pairs: NextInt result, unit NextFloat binary32 bits",
            Oracle = "C# binary64 sample=(raw>>11)*2^-53; NextInt=truncate(sample*bound); unit float=(float)sample",
            HistoricalShortcutCounterexample = new { Mantissa = inputs[0].Raw >> 11, inputs[0].Bound,
                HistoricalShortcut = historical, ExpectedRne = expected[0], ActualRne = actual.Words[0], Passed = counterexamplePassed },
            Mismatches = mismatches, TargetMetalExecutionClaimed = false, ThroughputClaimed = false,
            RngStateParityClaimed = false, Scope = "Production raw-word primitives; existing complete Family checks own RNG state/call consumption"
        });
        if (!counterexamplePassed)
            throw new InvalidDataException("CompatibilityNumerics.HistoricalShortcutCounterexampleMissing");
        if (!passed) throw new InvalidDataException($"CompatibilityNumerics.GpuMismatch:nextInt={integerFailures};unitFloat={floatFailures};see=gpu-integer-numerics.json");
        GD.Print($"COMPATIBILITY_GPU_NUMERICS_PASS inputs={inputs.Count};nextInt={inputs.Count};unitFloat={inputs.Count};" +
            $"actualSha256={NumericHash(actualBytes)};device={actual.Device};targetMetalClaimed=false;throughputClaimed=false");
    }

    private static List<NumericInput> BuildNumericInputs(int randomCount, int seed)
    {
        const ulong limit = 1UL << 53;
        var rows = new List<NumericInput>();
        var seen = new HashSet<(ulong Raw, uint Bound)>();
        void AddRaw(ulong raw, uint bound, string group)
        {
            if (bound == 0) throw new InvalidDataException("CompatibilityNumerics.ZeroBoundOutsideAuditedDomain");
            if (seen.Add((raw, bound))) rows.Add(new(raw, bound, group));
        }
        void AddMantissa(long mantissa, uint bound, string group)
        {
            if (mantissa < 0 || (ulong)mantissa >= limit) return;
            foreach (uint suffix in new uint[] { 0, 1, 2047 }) AddRaw(((ulong)mantissa << 11) | suffix, bound, group);
        }

        // Keep this at index zero: floor(m*b/2^53)=1 but binary64 RNE yields 2.
        AddRaw(6004799503160661UL << 11, 3, "HistoricalShortcutCounterexample");
        uint[] bounds = [1, 2, 3, 7, 17, 31, 127, 1023, 2047, 2048, 2049,
            4095, 65535, 65536, 65537, 0x7fffffffu, 0x80000000u, 0xfffffffeu, uint.MaxValue];
        foreach (uint bound in bounds)
        {
            foreach (long m in new long[] { 0, 1, 2, (long)limit - 2, (long)limit - 1 })
                AddMantissa(m, bound, "IntegerEndpoints");
            for (int bit = 0; bit < 32; bit++)
            {
                ulong power = 1UL << bit;
                foreach (ulong q in new[] { power - 1, power, power + 1, (ulong)bound - 1 })
                {
                    if (q == 0 || q >= bound) continue;
                    long near = (long)(((UInt128)q << 53) / bound);
                    for (int delta = -3; delta <= 3; delta++) AddMantissa(near + delta, bound, "IntegerTransitionAdjacent");
                    // Exact halfway products where the integer raw-mantissa
                    // lattice can reach one, plus both adjacent mantissas.
                    if (q <= 1) continue;
                    UInt128 halfwayProduct = ((UInt128)q << 53) - ((UInt128)1 << BitOperations.Log2((uint)(q - 1)));
                    if (halfwayProduct % bound != 0) continue;
                    long tie = (long)(halfwayProduct / bound);
                    for (int delta = -1; delta <= 1; delta++) AddMantissa(tie + delta, bound, "IntegerRoundingTieAdjacent");
                }
            }
        }

        for (int bit = 0; bit < 53; bit++)
        {
            long power = 1L << bit;
            foreach (long m in new[] { power - 1, power, power + 1 }) AddMantissa(m, 3, "FloatExponentAdjacent");
            if (bit <= 23) continue;
            int shift = bit - 23;
            ulong halfway = 1UL << (shift - 1), mask = (1UL << shift) - 1;
            foreach (ulong significand in new ulong[] { 0x800000, 0x800001, 0xaaaaaa, 0xaaaaab, 0xfffffe, 0xffffff })
            foreach (ulong remainder in new[] { 0UL, halfway - 1, halfway, Math.Min(mask, halfway + 1), mask })
                AddMantissa((long)((significand << shift) | remainder), 3, "FloatRoundingTieAdjacent");
        }
        long toOne = (long)limit - (1L << 28);
        for (int delta = -2; delta <= 2; delta++) AddMantissa(toOne + delta, uint.MaxValue, "FloatRoundToOneAdjacent");

        var random = new Random(seed);
        byte[] bytes = new byte[12];
        for (int i = 0; i < randomCount; i++)
        {
            random.NextBytes(bytes);
            uint bound = BitConverter.ToUInt32(bytes, 8);
            AddRaw(BitConverter.ToUInt64(bytes, 0), bound == 0 ? 1u : bound, "DeterministicRandomRawAndBound");
        }
        if (rows.Count is < 50000 or > 200000) throw new InvalidDataException("CompatibilityNumerics.CorpusSizeOutsideBound");
        return rows;
    }

    private static string NumericShaderSource(string nextInt, string unitFloat) => """
        #version 450
        layout(local_size_x=64) in;
        layout(set=0,binding=0,std430) readonly buffer Inputs {uint v[];} input_words;
        layout(set=0,binding=1,std430) buffer Outputs {uint v[];} output_words;
        """ + "\n" + nextInt + "\n" + unitFloat + "\n" + """
        void main(){
            uint i=gl_GlobalInvocationID.x;
            if(i>=uint(input_words.v.length())/3u)return;
            uint low=input_words.v[i*3u],high=input_words.v[i*3u+1u],bound=input_words.v[i*3u+2u];
            output_words.v[i*2u]=rt2_next_int_rne_raw32(low,high,bound);
            output_words.v[i*2u+1u]=floatBitsToUint(rt2_next_float_from_words_u32(uvec2(low,high)));
        }
        """;

    private static NumericReadback ExecuteNumericProbe(RenderingDevice rd, string source, uint[] inputWords)
    {
        int count = inputWords.Length / 3;
        var owned = new List<Rid>();
        Rid Add(Rid rid) { owned.Add(rid); return rid; }
        NumericReadback result;
        try
        {
            Rid input = Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytes(inputWords), "CompatibilityNumerics.Input"));
            var sentinel = new uint[checked(count * 2)];
            Array.Fill(sentinel, 0xdeadbeefu); // Outside the unit-float bit range; every row must be written.
            Rid output = Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytes(sentinel), "CompatibilityNumerics.Output"));
            Rid shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "CompatibilityIntegerNumerics"));
            Rid pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "CompatibilityIntegerNumerics"));
            Rid uniforms = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, [input, output], "CompatibilityIntegerNumerics"));
            long list = rd.ComputeListBegin();
            rd.ComputeListBindComputePipeline(list, pipeline);
            rd.ComputeListBindUniformSet(list, uniforms, 0);
            rd.ComputeListDispatch(list, checked((uint)((count + 63) / 64)), 1, 1);
            rd.ComputeListEnd();
            FamilyGpuComputeUtility.SubmitAndSync(rd, "CompatibilityIntegerNumerics");
            uint[] words = FamilyGpuComputeUtility.FromUInt32Bytes(rd.BufferGetData(output));
            if (words.Length != count * 2) throw new InvalidDataException("CompatibilityNumerics.ReadbackLengthMismatch");
            result = new(words, rd.GetDeviceName(), rd.GetDeviceVendorName());
        }
        catch (Exception failure)
        {
            FamilyGpuComputeUtility.CleanupAfterFailure(failure, () => FamilyGpuComputeUtility.FreeAll(rd, owned), "CompatibilityNumerics");
            throw;
        }
        FamilyGpuComputeUtility.FreeAll(rd, owned);
        return result;
    }

    private static string NumericHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
