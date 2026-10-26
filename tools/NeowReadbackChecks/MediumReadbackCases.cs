using System.Buffers.Binary;
using Godot;
using RolltheSpire2.Search.FamilyExecution;

internal static class MediumReadbackCases
{
    internal static void Run(Action<bool, string> check, Action<Action, string> expectFailure, List<string> passed)
    {
        byte[] Pattern(int count, uint salt = 0)
        {
            byte[] bytes = new byte[checked(count * sizeof(uint))];
            for (int ordinal = 0; ordinal < count; ordinal++)
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(ordinal * 4, 4),
                    unchecked((uint)ordinal * 2654435761u ^ 0xA53C09F1u ^ salt));
            return bytes;
        }

        // 僅超過小型前綴且未超過中型容量時暫存，兩端路徑保持原樣。
        foreach ((uint count, bool staged) in new (uint, bool)[]
        {
            (0, false), (1, false), (1024, false), (1025, true), (4096, true),
            (NeowMediumReadbackTransport.CandidateCapacity, true),
            (NeowMediumReadbackTransport.CandidateCapacity + 1, false),
            ((uint)NeowFamilyGpuExecutor.Capacity, false)
        })
        {
            check(NeowMediumReadbackTransport.CanStage(count) == staged,
                $"Medium staging selection count={count} staged={staged}");
            passed.Add($"MediumSelection_{count}_{staged}");
        }

        foreach (uint count in new uint[] { 1025, 1026, 4096, 8192, 65536 })
        {
            RenderingDevice rd = new();
            byte[] source = Pattern(1024 * 64 + 1, count);
            Rid output = rd.Register(source);
            byte[] bankBytes = Enumerable.Repeat((byte)0xCC, NeowMediumReadbackTransport.BankBytes).ToArray();
            Rid bank = rd.Register(bankBytes);

            byte[] result = NeowMediumReadbackTransport.Read(rd, output, bank, count);
            int requested = checked((int)count * sizeof(uint));
            check(result.Length == requested && result.AsSpan().SequenceEqual(source.AsSpan(0, requested)),
                $"Medium payload preserves complete ordered bytes count={count}");
            check(rd.Calls.Count == 2, $"Medium path issues exactly one copy and one read count={count}");
            TransportCall copy = rd.Calls[0];
            check(copy.Operation == "Copy" && copy.Source == output && copy.Destination == bank &&
                copy.SourceOffset == 0 && copy.DestinationOffset == 0 && copy.Bytes == count * 4,
                $"Medium copy requests exact validated payload range count={count}");
            TransportCall read = rd.Calls[1];
            check(read.Operation == "Read" && read.Source == bank && read.SourceOffset == 0 && read.Bytes == count * 4,
                $"Medium synchronous read uses the bounded bank count={count}");
            check(rd.PendingCopies == 0, $"Medium copy completes before return count={count}");
            // 有效數量以外是舊資料，不能當作候選傳回。
            check(bankBytes.AsSpan(requested).ToArray().All(value => value == 0xCC),
                $"Medium bank tail beyond validated count is untouched count={count}");
            passed.Add($"MediumCompletePayload_{count}");
        }

        {
            RenderingDevice rd = new();
            byte[] source = Pattern(65537, 7);
            Rid output = rd.Register(source);
            Rid bank = rd.Register(Enumerable.Repeat((byte)0xF7, NeowMediumReadbackTransport.BankBytes).ToArray());
            List<(byte[] Result, byte[] Snapshot)> previous = [];
            foreach ((uint count, uint salt) in new (uint, uint)[]
            {
                (1025, 1), (65536, 2), (2048, 3), (1025, 4), (65536, 5)
            })
            {
                Pattern(checked((int)count), salt).CopyTo(source, 0);
                byte[] result = NeowMediumReadbackTransport.Read(rd, output, bank, count);
                check(result.AsSpan().SequenceEqual(source.AsSpan(0, checked((int)count * 4))),
                    $"Reused medium bank is fresh for count={count} salt={salt}");
                check(rd.PendingCopies == 0, $"Reused medium bank completed before return count={count}");
                foreach ((byte[] prior, byte[] snapshot) in previous)
                    check(prior.AsSpan().SequenceEqual(snapshot),
                        "Later medium reuse does not mutate previously returned arrays");
                previous.Add((result, result.ToArray()));
            }
            passed.Add("MediumReuse_DecreasingAndGrowingPayloads");
        }

        foreach (uint invalid in new[]
        {
            checked((uint)NeowFamilyGpuExecutor.Capacity + 1), uint.MaxValue,
            (uint)NeowMediumReadbackTransport.CandidateCapacity + 1
        })
        {
            RenderingDevice rd = new();
            Rid output = rd.Register(Pattern(1));
            Rid bank = rd.Register(new byte[NeowMediumReadbackTransport.BankBytes]);
            expectFailure(() => NeowMediumReadbackTransport.Read(rd, output, bank, invalid),
                $"Medium invalid count {invalid} fails closed");
            check(rd.Calls.Count == 0, $"Medium invalid count {invalid} has no native calls");
            passed.Add($"MediumCountBounds_{invalid}");
        }

        {
            RenderingDevice rd = new();
            byte[] result = NeowMediumReadbackTransport.Read(rd, default, default, 0);
            check(result.Length == 0 && rd.Calls.Count == 0, "Medium zero survivors needs no resource and issues no native calls");
            passed.Add("MediumEmptyPayload_NoResources");
        }

        foreach (string missing in new[] { "output", "bank" })
        {
            RenderingDevice rd = new();
            Rid output = rd.Register(Pattern(1025));
            Rid bank = rd.Register(new byte[NeowMediumReadbackTransport.BankBytes]);
            expectFailure(() => NeowMediumReadbackTransport.Read(rd,
                missing == "output" ? default : output,
                missing == "bank" ? default : bank, 1025), $"Medium missing {missing} fails before any native call");
            check(rd.Calls.Count == 0, $"Medium missing {missing} produces no partial native work");
            passed.Add($"MediumMissingRid_{missing}");
        }

        {
            RenderingDevice rd = new() { NextCopyError = Error.Failed };
            Rid output = rd.Register(Pattern(1025));
            Rid bank = rd.Register(new byte[NeowMediumReadbackTransport.BankBytes]);
            expectFailure(() => NeowMediumReadbackTransport.Read(rd, output, bank, 1025),
                "Medium copy failure does not read stale bank contents");
            check(rd.Calls.Count == 1 && rd.Calls[0].Operation == "Copy" && rd.PendingCopies == 0,
                "Medium copy failure stops before readback");
            passed.Add("MediumCopyFailure_NoStalePayload");
        }

        {
            RenderingDevice rd = new();
            Rid output = rd.Register(Pattern(1025));
            Rid bank = rd.Register(new byte[NeowMediumReadbackTransport.BankBytes]);
            IOException injected = new("Injected medium native readback error");
            rd.NextReadException = injected;
            Exception? observed = null;
            try { NeowMediumReadbackTransport.Read(rd, output, bank, 1025); }
            catch (Exception error) { observed = error; }
            check(ReferenceEquals(observed, injected), "Medium native readback exception propagates unchanged");
            passed.Add("MediumReadException");
        }

        foreach (int badLength in new[] { 0, checked(1025 * 4) - 1 })
        {
            RenderingDevice rd = new() { NextReadResultLength = badLength };
            Rid output = rd.Register(Pattern(1025));
            Rid bank = rd.Register(new byte[NeowMediumReadbackTransport.BankBytes]);
            expectFailure(() => NeowMediumReadbackTransport.Read(rd, output, bank, 1025),
                $"Medium malformed native length {badLength} fails closed");
            check(rd.Calls.Count == 2 && rd.Calls[1].Operation == "Read" && rd.PendingCopies == 0,
                "Medium malformed length is rejected after the copy was completed");
            passed.Add($"MediumMalformedReadbackLength_{badLength}");
        }

        {
            // 中型區維持 256 KiB，避免同步讀回配置 64 MiB CPU 資源。
            check(NeowMediumReadbackTransport.BankBytes == 262144, "Medium bank stays at 256 KiB");
            check(NeowMediumReadbackTransport.BankBytes < NeowFamilyGpuExecutor.Capacity * sizeof(uint) / 128,
                "Medium bank is far below the dense output source capacity");
            passed.Add("MediumBankCapacityBound");
        }
    }
}
