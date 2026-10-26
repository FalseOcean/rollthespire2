using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using RolltheSpire2.Search.FamilyExecution;

int assertions = 0;
List<string> passed = [];

void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

void ExpectFailure(Action action, string message)
{
    bool failed = false;
    try { action(); }
    catch { failed = true; }
    Check(failed, message);
}

byte[] Pattern(int ordinalCount, uint salt = 0)
{
    byte[] bytes = new byte[checked(ordinalCount * sizeof(uint))];
    for (int ordinal = 0; ordinal < ordinalCount; ordinal++)
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ordinal * sizeof(uint), sizeof(uint)),
            unchecked((uint)ordinal * 2654435761u ^ 0xA53C09F1u ^ salt));
    // 傳輸須保留重複值與 uint 邊界；越界與重複由正式序號驗證器拒絕。
    if (ordinalCount >= 4)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), uint.MaxValue);
    }
    return bytes;
}

void VerifyPayload(int count, bool expectedScratch)
{
    RenderingDevice rd = new();
    byte[] source = Pattern(count);
    Rid output = rd.Register(source);
    byte[] scratchBytes = Enumerable.Repeat((byte)0xCC, NeowReadbackTransport.ScratchBytes).ToArray();
    Rid scratch = rd.Register(scratchBytes);
    byte[] result = NeowReadbackTransport.Read(rd, output, scratch, checked((uint)count), out bool usedScratch);
    Check(result.AsSpan().SequenceEqual(source), $"Complete ordered payload at count={count}");
    Check(result.Length == checked(count * 4), $"Exact requested length at count={count}");
    Check(usedScratch == expectedScratch, $"Transport attribution at count={count}");
    Check(rd.PendingCopies == 0, $"No unfinished copies on successful return at count={count}");
    if (count == 0) Check(rd.Calls.Count == 0, "Zero survivors performs no native calls");
    else
    {
        TransportCall read = rd.Calls.Last();
        Check(read.Operation == "Read" && read.Source == (expectedScratch ? scratch : output), $"Correct readback resource at count={count}");
        Check(read.SourceOffset == 0 && read.Bytes == count * 4u, $"Exact readback range at count={count}");
        if (expectedScratch)
        {
            Check(rd.Calls.Count == 2 && rd.Calls[0].Operation == "Copy", $"Copy precedes readback at count={count}");
            TransportCall copy = rd.Calls[0];
            Check(copy.Source == output && copy.Destination == scratch && copy.SourceOffset == 0 &&
                copy.DestinationOffset == 0 && copy.Bytes == count * 4u, $"Complete requested copy at count={count}");
        }
        else Check(rd.Calls.Count == 1, $"Oversize fallback has no scratch-copy truncation at count={count}");
    }
    passed.Add($"CompletePayload_{count}");
}

VerifyPayload(0, false);
VerifyPayload(1, true);
VerifyPayload(4, true);
VerifyPayload(1024, true);
VerifyPayload(1025, false);
VerifyPayload(65537, false);
VerifyPayload(NeowFamilyGpuExecutor.Capacity, false);

foreach (uint count in new uint[] { 0, 1, 4, 1024, 1025 })
{
    RenderingDevice rd = new();
    byte[] source = Pattern(checked((int)count));
    Rid output = rd.Register(source);
    byte[] result = NeowReadbackTransport.ReadDirect(rd, output, count);
    Check(result.AsSpan().SequenceEqual(source), $"Matched A/B direct path preserves complete payload count={count}");
    Check(rd.Calls.Count == (count == 0 ? 0 : 1), $"Matched A/B direct path never stages a copy count={count}");
    if (count > 0)
        Check(rd.Calls[0].Operation == "Read" && rd.Calls[0].Source == output && rd.Calls[0].Bytes == count * 4,
            $"Matched A/B direct path reads complete source range count={count}");
    passed.Add($"DirectControlPayload_{count}");
}

{
    RenderingDevice rd = new();
    byte[] source = Pattern(1024, 1);
    Rid output = rd.Register(source);
    Rid scratch = rd.Register(new byte[NeowReadbackTransport.ScratchBytes]);
    byte[] firstResult = NeowReadbackTransport.Read(rd, output, scratch, 1024, out _);
    byte[] firstSnapshot = firstResult.ToArray();
    foreach (int count in new[] { 1024, 17, 1, 0, 512, 2 })
    {
        byte[] next = Pattern(1024, checked((uint)(count + 19)));
        next.CopyTo(source, 0);
        byte[] result = NeowReadbackTransport.Read(rd, output, scratch, checked((uint)count), out _);
        Check(result.AsSpan().SequenceEqual(next.AsSpan(0, count * 4)), $"Reused scratch freshness and length count={count}");
        Check(rd.PendingCopies == 0, $"Reused scratch completed before return count={count}");
        Check(firstResult.AsSpan().SequenceEqual(firstSnapshot), $"Prior returned array remains independent count={count}");
    }
    passed.Add("ScratchReuse_DecreasingAndGrowingPayloads");
}

{
    RenderingDevice rd = new();
    Rid output = rd.Register(Pattern(1025));
    byte[] result = NeowReadbackTransport.Read(rd, output, default, 1025, out bool usedScratch);
    Check(result.AsSpan().SequenceEqual(Pattern(1025)) && !usedScratch, "Oversized payload does not require scratch");
    Check(rd.Calls.Count == 1 && rd.Calls[0].Source == output, "No invalid scratch use on fallback");
    passed.Add("OversizeFallback_WithoutScratch");
}

{
    RenderingDevice rd = new();
    byte[] result = NeowReadbackTransport.Read(rd, default, default, 0, out bool usedScratch);
    Check(result.Length == 0 && !usedScratch && rd.Calls.Count == 0, "Zero survivors needs no resource and issues no native calls");
    passed.Add("EmptyPayload_NoResources");
}

{
    RenderingDevice rd = new();
    Rid output = rd.Register(Pattern(1));
    ExpectFailure(() => NeowReadbackTransport.Read(rd, output, default, 1, out _), "Missing scratch fails closed");
    Check(rd.Calls.Count == 0, "Missing scratch rejected before any native calls");
    passed.Add("MissingScratch_FailClosed");
}

foreach (uint invalid in new[] { checked((uint)NeowFamilyGpuExecutor.Capacity + 1), uint.MaxValue })
{
    RenderingDevice rd = new();
    Rid output = rd.Register(Pattern(1));
    Rid scratch = rd.Register(new byte[NeowReadbackTransport.ScratchBytes]);
    ExpectFailure(() => NeowReadbackTransport.Read(rd, output, scratch, invalid, out _), $"Invalid count {invalid} fails closed");
    Check(rd.Calls.Count == 0, $"Invalid count {invalid} has no GPU-side effects");
    ExpectFailure(() => NeowReadbackTransport.ReadDirect(rd, output, invalid), $"Invalid direct count {invalid} fails closed");
    Check(rd.Calls.Count == 0, $"Invalid direct count {invalid} has no GPU-side effects");
    passed.Add($"CountBounds_{invalid}");
}

{
    RenderingDevice rd = new();
    Rid scratch = rd.Register(new byte[NeowReadbackTransport.ScratchBytes]);
    ExpectFailure(() => NeowReadbackTransport.Read(rd, default, scratch, 1, out _), "Missing output fails closed before scratch copy");
    ExpectFailure(() => NeowReadbackTransport.ReadDirect(rd, default, 1), "Missing direct output fails closed");
    Check(rd.Calls.Count == 0, "Missing output causes no native calls in either mode");
    passed.Add("MissingOutput_FailClosed");
}

{
    RenderingDevice rd = new() { NextCopyError = Error.Failed };
    Rid output = rd.Register(Pattern(1));
    Rid scratch = rd.Register(new byte[NeowReadbackTransport.ScratchBytes]);
    ExpectFailure(() => NeowReadbackTransport.Read(rd, output, scratch, 1, out _), "Copy failure does not return stale scratch");
    Check(rd.Calls.Count == 1 && rd.Calls[0].Operation == "Copy" && rd.PendingCopies == 0, "Copy failure stops before readback");
    passed.Add("CopyFailure_NoStalePayload");
}

foreach (uint count in new uint[] { 1, 1025 })
{
    RenderingDevice rd = new();
    Rid output = rd.Register(Pattern(checked((int)count)));
    Rid scratch = rd.Register(new byte[NeowReadbackTransport.ScratchBytes]);
    var injected = new IOException("Injected native readback failure");
    rd.NextReadException = injected;
    Exception? observed = null;
    try { NeowReadbackTransport.Read(rd, output, scratch, count, out _); }
    catch (Exception error) { observed = error; }
    Check(ReferenceEquals(observed, injected), $"Native readback exception propagates count={count}");
    passed.Add($"ReadbackFailure_{count}");
}

{
    RenderingDevice rd = new();
    Rid output = rd.Register(Pattern(1));
    var injected = new IOException("Injected direct native readback failure");
    rd.NextReadException = injected;
    Exception? observed = null;
    try { NeowReadbackTransport.ReadDirect(rd, output, 1); }
    catch (Exception error) { observed = error; }
    Check(ReferenceEquals(observed, injected), "Matched A/B direct control propagates readback exception");
    passed.Add("DirectControlReadbackFailure");
}

foreach (uint count in new uint[] { 1, 1025 })
{
    foreach (int badLength in new[] { 0, checked((int)(count * 4) - 1), checked((int)(count * 4) + 4) })
    {
        RenderingDevice rd = new() { NextReadResultLength = badLength };
        Rid output = rd.Register(Pattern(checked((int)count)));
        Rid scratch = rd.Register(new byte[NeowReadbackTransport.ScratchBytes]);
        ExpectFailure(() => NeowReadbackTransport.Read(rd, output, scratch, count, out _),
            $"Empty/short/oversized native return fails closed count={count}, length={badLength}");
        Check(rd.Calls.Last().Operation == "Read" && rd.PendingCopies == 0,
            $"Native return length rejected after completed read count={count}, length={badLength}");
        RenderingDevice direct = new() { NextReadResultLength = badLength };
        Rid directOutput = direct.Register(Pattern(checked((int)count)));
        ExpectFailure(() => NeowReadbackTransport.ReadDirect(direct, directOutput, count),
            $"Direct control malformed return fails closed count={count}, length={badLength}");
        Check(direct.Calls.Count == 1 && direct.Calls[0].Operation == "Read", "Direct malformed-return test exercises control API");
        passed.Add($"MalformedReadbackLength_{count}_{badLength}");
    }
}

// 預期位元組與雜湊由 Python struct.pack 獨立建立，未使用受測編碼器。
const string DomainOnlyHash = "956592D3991ADF3FA635439F6A8AE90562C5C53A697595FD3BEB722A55688824";
const string EmptyBatchHash = "A7BE1B96361C1D8AB50451DC8EC9BF64693AAFEDE8B8CA356A55583405A5148C";
const string GoldenHash = "F151EFA562A7C3BEAF28E6193420E4ADB239990B33E83BB569D5572E8850F5BB";
const ulong GoldenBase = 0x0123456789ABCDEFUL;
ulong[] goldenOrdinals = [0, 7, 1023];
ulong[] goldenInputSnapshot = goldenOrdinals.ToArray();

{
    using NeowCandidateDigest digest = new();
    Check(digest.GetCurrentHex() == DomainOnlyHash, "Digest version domain matches independent SHA256 golden");
    Check(digest.GetCurrentHex() == digest.GetCurrentHex(), "Empty digest reads are non-destructive");
    digest.Append(GoldenBase, 4096, goldenOrdinals, 1234, 321);
    string first = digest.GetCurrentHex();
    Check(first == digest.GetCurrentHex(), "Current hash read does not consume first completed batch");
    digest.Append(17, 5, [], 2, 0);
    Check(digest.GetCurrentHex() == GoldenHash, "Ordered complete digest matches independent two-batch golden");
    Check(digest.GetCurrentHex() == GoldenHash, "Repeated final hash read is stable");
    Check(goldenOrdinals.AsSpan().SequenceEqual(goldenInputSnapshot), "Digest preserves caller's ordinal bytes and order");
    passed.Add("Digest_IndependentGoldenAndNonDestructiveReads");
}

{
    using NeowCandidateDigest digest = new();
    digest.Append(17, 5, [], 2, 0);
    Check(digest.GetCurrentHex() == EmptyBatchHash, "Empty completed batch has deterministic independent golden");
    Check(digest.GetCurrentHex() != DomainOnlyHash, "Empty completed batches are recorded, not omitted");
    passed.Add("Digest_EmptyBatchIncluded");
}

string MutatedDigest(ulong batchBase, int window, ulong[] ordinals, uint curse, uint pair, bool appendEmpty = true)
{
    using NeowCandidateDigest digest = new();
    digest.Append(batchBase, window, ordinals, curse, pair);
    if (appendEmpty) digest.Append(17, 5, [], 2, 0);
    return digest.GetCurrentHex();
}

foreach ((string field, string hash) in new[]
{
    ("batch base", MutatedDigest(GoldenBase + 1, 4096, [0, 7, 1023], 1234, 321)),
    ("window size", MutatedDigest(GoldenBase, 4095, [0, 7, 1023], 1234, 321)),
    ("survivor count", MutatedDigest(GoldenBase, 4096, [0, 7], 1234, 321)),
    ("curse count", MutatedDigest(GoldenBase, 4096, [0, 7, 1023], 1235, 321)),
    ("pair count", MutatedDigest(GoldenBase, 4096, [0, 7, 1023], 1234, 322)),
    ("ordinal value", MutatedDigest(GoldenBase, 4096, [0, 8, 1023], 1234, 321)),
    ("ordinal order", MutatedDigest(GoldenBase, 4096, [7, 0, 1023], 1234, 321)),
    ("missing empty batch", MutatedDigest(GoldenBase, 4096, [0, 7, 1023], 1234, 321, false)),
})
{
    Check(hash != GoldenHash, $"Digest detects change in {field}");
}
{
    using NeowCandidateDigest reversed = new();
    reversed.Append(17, 5, [], 2, 0);
    reversed.Append(GoldenBase, 4096, [0, 7, 1023], 1234, 321);
    Check(reversed.GetCurrentHex() != GoldenHash, "Digest detects batch order change");
    passed.Add("Digest_AllMetadataAndOrderingMutations");
}

{
    using NeowCandidateDigest digest = new();
    string initial = digest.GetCurrentHex();
    ExpectFailure(() => digest.Append(0, -1, [], 0, 0), "Negative window fails before digest mutation");
    Check(digest.GetCurrentHex() == initial, "Negative window leaves digest unchanged");
    ExpectFailure(() => digest.Append(0, 1, [0, 1], 0, 0), "Excess survivor count fails before digest mutation");
    Check(digest.GetCurrentHex() == initial, "Excess survivor count leaves digest unchanged");
    digest.Append(0, 0, [], 0, 0);
    Check(digest.GetCurrentHex() != initial, "Zero-sized completed window is encoded deterministically");
    digest.Dispose();
    ExpectFailure(() => digest.Append(0, 0, [], 0, 0), "Disposed digest rejects append");
    ExpectFailure(() => digest.GetCurrentHex(), "Disposed digest rejects read");
    digest.Dispose();
    passed.Add("Digest_ValidationAndDisposal");
}

CombinedReadbackCases.Run(Check, ExpectFailure, passed);
MediumReadbackCases.Run(Check, ExpectFailure, passed);

var report = new
{
    Kind = "OfflineLinkedProductionTransportContractChecks",
    Assertions = assertions,
    Passed = passed,
    AllPassed = true,
    ScratchBytes = NeowReadbackTransport.ScratchBytes,
    CombinedHeaderBytes = NeowCombinedReadbackTransport.HeaderBytes,
    CombinedPrefixCapacity = NeowCombinedReadbackTransport.PrefixCapacity,
    CombinedBankBytes = NeowCombinedReadbackTransport.BankBytes,
    MediumCandidateCapacity = NeowMediumReadbackTransport.CandidateCapacity,
    MediumBankBytes = NeowMediumReadbackTransport.BankBytes,
    SmallReadbackEnabled = NeowReadbackTransport.Enabled,
    CombinedReadbackEnabled = NeowCombinedReadbackTransport.Enabled,
    MediumReadbackEnabled = NeowMediumReadbackTransport.Enabled,
    FullCapacityOrdinals = NeowFamilyGpuExecutor.Capacity,
    DigestDomainAscii = NeowCandidateDigest.DomainMarker,
    DigestDomainBytes = 37,
    DigestGoldenSha256 = GoldenHash,
    NativeGodotLoaded = false,
    GpuExecuted = false,
    Scope = "Linked production separate/combined/medium readback transport and digest helpers with queued-copy fake Godot; byte completeness, ordering, bounded banks, complete fallback, fail-closed behavior and independent digest golden. Does not establish actual GPU correctness or performance."
};
string serialized = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0) File.WriteAllText(args[0], serialized + Environment.NewLine);
Console.WriteLine(serialized);

namespace RolltheSpire2.Search.FamilyExecution
{
    // 測試只替代此常數，不參照主專案。
    internal static class NeowFamilyGpuExecutor
    {
        internal const int Capacity = 1 << 24;
    }
}
