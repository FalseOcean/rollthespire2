using System.Buffers.Binary;
using Godot;
using RolltheSpire2.Search.FamilyExecution;

internal static class CombinedReadbackCases
{
    internal static void Run(Action<bool, string> check, Action<Action, string> expectFailure, List<string> passed)
    {
        byte[] Header(uint input, uint output)
        {
            uint[] words = [0x4e464d52, 1, output, 0, input, 0x12345678, 123, 17];
            byte[] result = new byte[32];
            for (int i = 0; i < words.Length; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4, 4), words[i]);
            return result;
        }

        byte[] Source(uint input, uint salt)
        {
            byte[] result = new byte[checked((int)input * 4)];
            for (int i = 0; i < result.Length / 4; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(i * 4, 4),
                    unchecked((uint)i * 2654435761u ^ salt ^ 0xA53C09F1u));
            return result;
        }

        foreach ((uint input, uint output) in new (uint, uint)[]
        {
            (0, 0), (1, 0), (1, 1), (1024, 0), (1024, 1), (1024, 1024),
            (1025, 1), (1025, 1024), (1025, 1025),
            (NeowFamilyGpuExecutor.Capacity, NeowFamilyGpuExecutor.Capacity)
        })
        {
            RenderingDevice rd = new();
            byte[] headerBytes = Header(input, output);
            byte[] sourceBytes = Source(input, input + output);
            byte[] residentBank = Enumerable.Repeat((byte)0xD3, NeowCombinedReadbackTransport.BankBytes).ToArray();
            Rid header = rd.Register(headerBytes);
            Rid source = input == 0 ? default : rd.Register(sourceBytes);
            Rid bank = rd.Register(residentBank);

            NeowCombinedReadbackTransport.QueueCopies(rd, header, source, bank, input);
            check(residentBank.All(value => value == 0xD3), $"Combined copies are deferred input={input},output={output}");
            check(rd.PendingCopies == (input == 0 ? 1 : 2), $"Both combined transfers remain queued input={input},output={output}");
            check(rd.Calls.Count == (input == 0 ? 1 : 2) && rd.Calls.All(call => call.Operation == "Copy"),
                "Queueing does not hide a CPU read or synchronization");
            TransportCall headerCopy = rd.Calls[0];
            check(headerCopy.Source == header && headerCopy.Destination == bank && headerCopy.SourceOffset == 0 &&
                headerCopy.DestinationOffset == 0 && headerCopy.Bytes == 32, "Complete fixed header copy is queued first");
            if (input != 0)
            {
                TransportCall prefixCopy = rd.Calls[1];
                check(prefixCopy.Source == source && prefixCopy.Destination == bank && prefixCopy.SourceOffset == 0 &&
                    prefixCopy.DestinationOffset == 32 && prefixCopy.Bytes == Math.Min(input, 1024u) * 4,
                    "Bounded output prefix copy occupies a disjoint bank range");
            }
            byte[] bytes = NeowCombinedReadbackTransport.ReadBank(rd, bank, input);
            uint expectedSize = 32 + Math.Min(input, 1024u) * 4;
            check(bytes.Length == expectedSize && rd.Calls.Last().Bytes == expectedSize && expectedSize != 0,
                "Combined read requests exactly the bounded nonzero range");
            check(rd.PendingCopies == 0, "Synchronous combined read completes both dependent bank copies");
            check(bytes.AsSpan(0, 32).SequenceEqual(headerBytes), "Combined bank preserves all header words exactly");
            check(bytes.AsSpan(32).SequenceEqual(sourceBytes.AsSpan(0, checked((int)(expectedSize - 32)))),
                "Combined bank contains complete requested prefix without overlap or dropped dependent copies");
            bool extracted = NeowCombinedReadbackTransport.TryExtractPayload(bytes, input, output, out byte[] payload);
            check(extracted == (output <= 1024), "Combined prefix availability agrees with validated survivor count");
            if (!extracted)
            {
                check(payload.Length == 0, "Oversized prefix never becomes a truncated candidate payload");
                payload = NeowReadbackTransport.ReadDirect(rd, source, output);
                check(rd.Calls.Last().Source == source && rd.Calls.Last().Bytes == output * 4,
                    "Oversized output falls back to the full source range");
            }
            check(payload.AsSpan().SequenceEqual(sourceBytes.AsSpan(0, checked((int)output * 4))),
                "Combined or fallback payload preserves the complete ordered result bytes");
            check(payload.Length == checked((int)output * 4), "Unwritten/stale prefix tail is never returned as candidates");
            passed.Add($"CombinedCompletePayload_{input}_{output}");
        }

        {
            RenderingDevice rd = new();
            byte[] sourceBytes = Source(1024, 3);
            byte[] headerBytes = Header(1024, 1024);
            Rid source = rd.Register(sourceBytes);
            Rid header = rd.Register(headerBytes);
            Rid bank = rd.Register(Enumerable.Repeat((byte)0xF7, NeowCombinedReadbackTransport.BankBytes).ToArray());
            List<(byte[] Result, byte[] Snapshot)> previous = [];
            foreach ((uint input, uint output) in new (uint, uint)[] { (1024, 9), (1024, 0), (17, 1), (1, 1), (1024, 2) })
            {
                Header(input, output).CopyTo(headerBytes, 0);
                // 只覆寫本批候選，其餘保留舊資料，驗證不會混入上一批。
                Source(output, input + output).CopyTo(sourceBytes, 0);
                NeowCombinedReadbackTransport.QueueCopies(rd, header, source, bank, input);
                byte[] bytes = NeowCombinedReadbackTransport.ReadBank(rd, bank, input);
                check(NeowCombinedReadbackTransport.TryExtractPayload(bytes, input, output, out byte[] payload), "Sparse reused bank yields complete prefix");
                check(bytes.AsSpan(0, 32).SequenceEqual(headerBytes), "Reused bank header is fresh");
                check(payload.AsSpan().SequenceEqual(sourceBytes.AsSpan(0, checked((int)output * 4))), "Reused bank ignores stale/unwritten tail");
                foreach ((byte[] result, byte[] snapshot) in previous)
                    check(result.AsSpan().SequenceEqual(snapshot), "Later combined reuse does not mutate previous payload arrays");
                previous.Add((payload, payload.ToArray()));
            }
            passed.Add("CombinedReuse_StaleUnwrittenTailAndPriorArrays");
        }

        foreach (uint invalid in new[] { checked((uint)NeowFamilyGpuExecutor.Capacity + 1), uint.MaxValue })
        {
            RenderingDevice rd = new();
            Rid header = rd.Register(Header(1, 1));
            Rid output = rd.Register(Source(1, 0));
            Rid bank = rd.Register(new byte[NeowCombinedReadbackTransport.BankBytes]);
            expectFailure(() => NeowCombinedReadbackTransport.ReadSize(invalid), "Combined invalid input size is rejected");
            expectFailure(() => NeowCombinedReadbackTransport.QueueCopies(rd, header, output, bank, invalid), "Combined invalid input rejects queueing");
            expectFailure(() => NeowCombinedReadbackTransport.ReadBank(rd, bank, invalid), "Combined invalid input rejects readback");
            expectFailure(() => NeowCombinedReadbackTransport.TryExtractPayload([], invalid, 0, out _), "Combined invalid input rejects extraction");
            check(rd.Calls.Count == 0, "Invalid combined input has no native calls");
            passed.Add($"CombinedInputBounds_{invalid}");
        }

        foreach (string missing in new[] { "header", "output", "bank" })
        {
            RenderingDevice rd = new();
            Rid header = rd.Register(Header(1, 1));
            Rid output = rd.Register(Source(1, 0));
            Rid bank = rd.Register(new byte[NeowCombinedReadbackTransport.BankBytes]);
            expectFailure(() => NeowCombinedReadbackTransport.QueueCopies(rd,
                missing == "header" ? default : header, missing == "output" ? default : output,
                missing == "bank" ? default : bank, 1), "Missing required combined RID fails before queueing");
            check(rd.Calls.Count == 0, "Missing required RID produces no partial native work");
            passed.Add($"CombinedMissingRid_{missing}");
        }
        {
            RenderingDevice rd = new();
            expectFailure(() => NeowCombinedReadbackTransport.ReadBank(rd, default, 0), "Missing bank rejected even for header-only read");
            check(rd.Calls.Count == 0, "Missing read bank produces no native calls");
            passed.Add("CombinedMissingReadBank");
        }

        foreach (int failingCopy in new[] { 1, 2 })
        {
            RenderingDevice rd = new();
            if (failingCopy == 2) rd.ScriptedCopyErrors.Enqueue(Error.Ok);
            rd.ScriptedCopyErrors.Enqueue(Error.Failed);
            Rid header = rd.Register(Header(1, 1));
            Rid output = rd.Register(Source(1, 0));
            Rid bank = rd.Register(new byte[NeowCombinedReadbackTransport.BankBytes]);
            expectFailure(() => NeowCombinedReadbackTransport.QueueCopies(rd, header, output, bank, 1), "Any combined copy error fails the batch");
            check(rd.Calls.Count == failingCopy && rd.Calls.All(call => call.Operation == "Copy"), "Copy error never consumes bank contents");
            check(rd.PendingCopies == failingCopy - 1, "Second-copy failure leaves first queued transfer for caller failure cleanup");
            passed.Add($"CombinedCopyFailure_{failingCopy}");
        }

        foreach (uint input in new uint[] { 0, 1, 1024, 1025 })
        {
            int expected = checked((int)NeowCombinedReadbackTransport.ReadSize(input));
            foreach (int badLength in new[] { 0, expected - 1, expected + 1 })
            {
                RenderingDevice rd = new() { NextReadResultLength = badLength };
                Rid bank = rd.Register(new byte[NeowCombinedReadbackTransport.BankBytes]);
                expectFailure(() => NeowCombinedReadbackTransport.ReadBank(rd, bank, input), "Malformed combined native length fails closed");
                expectFailure(() => NeowCombinedReadbackTransport.TryExtractPayload(new byte[badLength], input, 0, out _), "Malformed combined bank cannot extract an empty success");
            }
            passed.Add($"CombinedReadLengthBounds_{input}");
        }

        foreach ((uint input, uint output) in new (uint, uint)[] { (0, 1), (1, 2), (1024, 1025), (1025, uint.MaxValue) })
        {
            byte[] bank = new byte[NeowCombinedReadbackTransport.ReadSize(input)];
            expectFailure(() => NeowCombinedReadbackTransport.TryExtractPayload(bank, input, output, out _), "Output count larger than input cannot be classified as fallback or empty success");
            passed.Add($"CombinedOutputBounds_{input}_{output}");
        }
        {
            expectFailure(() => NeowCombinedReadbackTransport.TryExtractPayload(null!, 0, 0, out _), "Null combined bank fails closed");
            RenderingDevice rd = new();
            Rid bank = rd.Register(new byte[NeowCombinedReadbackTransport.BankBytes]);
            IOException injected = new("Injected combined native readback error");
            rd.NextReadException = injected;
            Exception? observed = null;
            try { NeowCombinedReadbackTransport.ReadBank(rd, bank, 1); }
            catch (Exception exception) { observed = exception; }
            check(ReferenceEquals(observed, injected), "Combined native readback exception propagates unchanged");
            passed.Add("CombinedNullAndReadException");
        }
    }
}
