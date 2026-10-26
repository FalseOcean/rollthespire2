using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// 合併讀回 32 位元組標頭與候選前綴；先驗證原標頭，超出前綴則讀回完整輸出。
internal static class NeowCombinedReadbackTransport
{
    internal const int HeaderBytes = 32;
    internal const int PrefixCapacity = 1024;
    internal const int BankBytes = HeaderBytes + PrefixCapacity * sizeof(uint);

    internal static bool Enabled
    {
        get
        {
#if RT2_NEOW_COMBINED_READBACK
            return true;
#else
            return false;
#endif
        }
    }

    internal static uint ReadSize(uint inputCount)
    {
        if (inputCount > NeowFamilyGpuExecutor.Capacity)
            throw new InvalidDataException("N.CombinedReadInputExceedsCapacity");
        return checked((uint)HeaderBytes + Math.Min(inputCount, (uint)PrefixCapacity) * sizeof(uint));
    }

    // 放在 ComputeListEnd 與 Submit/Sync 之間；兩次複製之間不可取消。
    internal static void QueueCopies(RenderingDevice rd, Rid header, Rid output, Rid bank, uint inputCount)
    {
        uint prefixBytes = ReadSize(inputCount) - HeaderBytes;
        if (!header.IsValid || !bank.IsValid || (prefixBytes != 0 && !output.IsValid))
            throw new InvalidOperationException("N.CombinedReadBufferMissing");
        if (rd.BufferCopy(header, bank, 0, 0, HeaderBytes) != Error.Ok)
            throw new InvalidOperationException("N.CombinedReadHeaderCopyFailed");
        if (prefixBytes != 0 && rd.BufferCopy(output, bank, 0, HeaderBytes, prefixBytes) != Error.Ok)
            throw new InvalidOperationException("N.CombinedReadPrefixCopyFailed");
    }

    internal static byte[] ReadBank(RenderingDevice rd, Rid bank, uint inputCount)
    {
        uint readSize = ReadSize(inputCount);
        if (!bank.IsValid) throw new InvalidOperationException("N.CombinedReadBankMissing");
        byte[] bytes = rd.BufferGetData(bank, 0, readSize);
        if (bytes.Length != readSize)
            throw new InvalidDataException($"N.CombinedReadLengthMismatch:expected={readSize};actual={bytes.Length}");
        return bytes;
    }

    internal static bool TryExtractPayload(byte[] bankBytes, uint inputCount, uint outputCount, out byte[] payload)
    {
        payload = [];
        uint expectedBytes = ReadSize(inputCount);
        ArgumentNullException.ThrowIfNull(bankBytes);
        if (bankBytes.Length != expectedBytes)
            throw new InvalidDataException($"N.CombinedReadLengthMismatch:expected={expectedBytes};actual={bankBytes.Length}");
        if (outputCount > inputCount)
            throw new InvalidDataException("N.CombinedReadOutputExceedsInput");
        if (outputCount > PrefixCapacity) return false;
        if (outputCount != 0)
            payload = bankBytes.AsSpan(HeaderBytes, checked((int)outputCount * sizeof(uint))).ToArray();
        return true;
    }
}
