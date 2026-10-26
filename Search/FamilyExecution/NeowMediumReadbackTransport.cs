using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// 中型輸出先複製到 256 KiB 緩衝，避免同步讀回按 64 MiB 來源配置臨時資源。
// 只搬有效候選，原標頭、完整 ABI1 與排序驗證維持原規則。
internal static class NeowMediumReadbackTransport
{
    // 最多 65,536 個候選，讀回來源限制為 256 KiB。
    internal const int CandidateCapacity = 65536;
    internal const int BankBytes = CandidateCapacity * sizeof(uint);

    internal static bool Enabled
    {
        get
        {
#if RT2_NEOW_MEDIUM_READBACK
            return true;
#else
            return false;
#endif
        }
    }

    // 合併前綴放不下時才使用此緩衝。
    internal static bool CanStage(uint count) =>
        count > NeowCombinedReadbackTransport.PrefixCapacity && count <= CandidateCapacity;

    internal static byte[] Read(RenderingDevice rd, Rid output, Rid bank, uint count)
    {
        if (count > NeowFamilyGpuExecutor.Capacity)
            throw new InvalidDataException("N.MediumReadCountExceedsCapacity");
        // Godot 的 size=0 代表讀取整個緩衝，不能用來表示空讀回。
        if (count == 0) return [];
        if (count > CandidateCapacity)
            throw new InvalidDataException("N.MediumReadCountExceedsBank");
        if (!output.IsValid) throw new InvalidOperationException("N.MediumReadOutputMissing");
        if (!bank.IsValid) throw new InvalidOperationException("N.MediumReadBankMissing");
        uint byteCount = checked(count * sizeof(uint));
        if (byteCount > BankBytes)
            throw new InvalidDataException("N.MediumReadCountExceedsBank");
        if (rd.BufferCopy(output, bank, 0, 0, byteCount) != Error.Ok)
            throw new InvalidOperationException("N.MediumReadCopyFailed");
        // 複製與同步讀回之間不可取消；讀回完成後才可重用或釋放緩衝。
        byte[] bytes = rd.BufferGetData(bank, 0, byteCount);
        // 原生讀回失敗可能回傳空陣列；短讀不能當成合法空候選集。
        if (bytes.Length != byteCount)
            throw new InvalidDataException($"N.MediumReadLengthMismatch:expected={byteCount};actual={bytes.Length}");
        return bytes;
    }
}
