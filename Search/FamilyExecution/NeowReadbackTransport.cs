using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Godot 同步讀回按來源容量配置 CPU 資源；小輸出先搬入有界緩衝。
// GPU 完整輸出仍保留。
internal static class NeowReadbackTransport
{
    internal const int ScratchBytes = 4096;

    internal static bool Enabled
    {
        get
        {
#if RT2_NEOW_SMALL_READBACK
            return true;
#else
            return false;
#endif
        }
    }

    internal static byte[] Read(RenderingDevice rd, Rid output, Rid scratch, uint count, out bool usedScratch) =>
        ReadCore(rd, output, scratch, count, true, out usedScratch);

    internal static byte[] ReadDirect(RenderingDevice rd, Rid output, uint count) =>
        ReadCore(rd, output, default, count, false, out _);

    private static byte[] ReadCore(RenderingDevice rd, Rid output, Rid scratch, uint count,
        bool allowScratch, out bool usedScratch)
    {
        usedScratch = false;
        if (count > NeowFamilyGpuExecutor.Capacity)
            throw new InvalidDataException("N.PayloadReadCountExceedsCapacity");
        // Godot 的 size=0 代表讀取整個緩衝，不能用來表示空讀回。
        if (count == 0) return [];
        if (!output.IsValid) throw new InvalidOperationException("N.PayloadReadOutputMissing");
        uint byteCount = checked(count * sizeof(uint));
        Rid source = output;
        if (allowScratch && byteCount <= ScratchBytes)
        {
            if (!scratch.IsValid) throw new InvalidOperationException("N.PayloadReadScratchMissing");
            if (rd.BufferCopy(output, scratch, 0, 0, byteCount) != Error.Ok)
                throw new InvalidOperationException("N.PayloadReadScratchCopyFailed");
            source = scratch;
            usedScratch = true;
        }
        // 複製與同步讀回之間不可取消；讀回完成後才可重用或釋放緩衝。
        byte[] bytes = rd.BufferGetData(source, 0, byteCount);
        // 原生讀回失敗可能回傳空陣列；短讀不能當成合法空候選集。
        if (bytes.Length != byteCount)
            throw new InvalidDataException($"N.PayloadReadLengthMismatch:expected={byteCount};actual={bytes.Length}");
        return bytes;
    }
}
