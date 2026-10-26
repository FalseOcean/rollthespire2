using System.Runtime.InteropServices;

namespace RolltheSpire2.Search.FamilyExecution;

// 將批次內 ABI1 序號打包為 GPU 的 uint 緩衝。
internal static class FamilyGpuOrdinalPacking
{
    internal static byte[] ToBytes(ReadOnlySpan<ulong> ordinals)
    {
        var bytes = new byte[checked(ordinals.Length * sizeof(uint))];
        // 沿用原生 uint 排列，省去中間 ulong[] 與 uint[]。
        Span<uint> packed = MemoryMarshal.Cast<byte, uint>(bytes);
        for (int i = 0; i < ordinals.Length; i++)
            packed[i] = checked((uint)ordinals[i]);
        return bytes;
    }
}
