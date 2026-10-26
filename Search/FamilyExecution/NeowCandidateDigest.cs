using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RolltheSpire2.Search.FamilyExecution;

// 依序摘要已完成 Neow 呼叫的完整候選集，包含尚未提交游標的批次。
internal sealed class NeowCandidateDigest : IDisposable
{
    internal const string DomainMarker = "RolltheSpire2.NeowCandidateDigest.v1\0";
    private static readonly byte[] DomainBytes = Encoding.ASCII.GetBytes(DomainMarker);
    private readonly IncrementalHash _hash;
    private bool _disposed;

    internal NeowCandidateDigest()
    {
        // 支援的 Windows 執行環境為小端序，直接保留指定的 u64 位元組流。
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("N.CandidateDigestRequiresLittleEndian");
        _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _hash.AppendData(DomainBytes);
    }

    internal void Append(ulong batchBase, int batchCandidateCount, ReadOnlySpan<ulong> sortedRelativeOrdinals,
        uint curseSurvivors, uint pairSurvivors)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (batchCandidateCount < 0)
            throw new ArgumentOutOfRangeException(nameof(batchCandidateCount));
        if (sortedRelativeOrdinals.Length > batchCandidateCount)
            throw new ArgumentException("N.CandidateDigestSurvivorsExceedWindow", nameof(sortedRelativeOrdinals));

        // 無填充：基址 u64，窗口／候選數／詛咒／配對各為 u32。
        Span<byte> header = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(header, batchBase);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], checked((uint)batchCandidateCount));
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], checked((uint)sortedRelativeOrdinals.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], curseSurvivors);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], pairSurvivors);
        _hash.AppendData(header);
        // 整批提交 span，避免逐序號雜湊與複製。
        _hash.AppendData(MemoryMarshal.AsBytes(sortedRelativeOrdinals));
    }

    internal string GetCurrentHex()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Convert.ToHexString(_hash.GetCurrentHash());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hash.Dispose();
    }
}
