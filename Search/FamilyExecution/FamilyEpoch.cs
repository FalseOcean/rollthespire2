using System.Diagnostics;
using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// 多個 family 共用同一命令清單與一次 Submit/Sync，結果仍逐批記帳。
internal sealed class FamilyEpoch
{
    private readonly struct CopyCommand(Rid source, Rid destination, uint sourceOffset,
        uint destinationOffset, uint bytes)
    {
        internal readonly Rid Source = source;
        internal readonly Rid Destination = destination;
        internal readonly uint SourceOffset = sourceOffset;
        internal readonly uint DestinationOffset = destinationOffset;
        internal readonly uint Bytes = bytes;
    }

    private readonly List<CopyCommand> _copies = new();
    private long _list;
    private bool _open;
    private bool _submitted;
    private bool _completed;

    internal FamilyEpoch(RenderingDevice device)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
    }

    internal RenderingDevice Device { get; }
    internal int FamilyCount { get; private set; }

    // 所有 BufferUpdate 必須先完成；Godot 不允許在活動清單內更新緩衝。
    internal void BeginList()
    {
        if (_open) throw new InvalidOperationException("EpochListAlreadyOpen");
        _list = Device.ComputeListBegin();
        _open = true;
    }

    internal long List => _open ? _list : throw new InvalidOperationException("EpochListNotOpen");

    internal void Barrier()
    {
        if (!_open) throw new InvalidOperationException("EpochListNotOpen");
        Device.ComputeListAddBarrier(_list);
    }

    internal void QueueCopy(Rid source, Rid destination, uint sourceOffset, uint destinationOffset, uint bytes) =>
        _copies.Add(new CopyCommand(source, destination, sourceOffset, destinationOffset, bytes));

    internal void MarkFamily() => FamilyCount++;

    // 先結束清單並排入複製，再統一 Submit/Sync；回傳的是主機 API 耗時。
    internal (double SubmitMs, double SyncMs) SubmitAndSync(string label)
    {
        if (_completed) throw new InvalidOperationException("EpochAlreadyCompleted");
        if (!_open) BeginList();
        Device.ComputeListEnd();
        _open = false;
        foreach (CopyCommand copy in _copies)
        {
            if (Device.BufferCopy(copy.Source, copy.Destination, copy.SourceOffset,
                    copy.DestinationOffset, copy.Bytes) != Error.Ok)
                throw new InvalidOperationException("EpochCopyFailed:" + label);
        }
        long submitStarted = Stopwatch.GetTimestamp();
        Device.Submit();
        _submitted = true;
        double submitMs = Stopwatch.GetElapsedTime(submitStarted).TotalMilliseconds;
        long syncStarted = Stopwatch.GetTimestamp();
        Device.Sync();
        double syncMs = Stopwatch.GetElapsedTime(syncStarted).TotalMilliseconds;
        _completed = true;
        return (submitMs, syncMs);
    }

    // 中途失敗時盡量關閉清單，並等候已提交的工作，才可回收資源。
    internal void TryAbort()
    {
        try
        {
            if (_open) { Device.ComputeListEnd(); _open = false; }
        }
        catch { /* 裝置可能已故障，交由工作階段釋放。 */ }
        if (!_submitted || _completed) return;
        try { Device.Sync(); }
        catch { /* 僅盡力清理。 */ }
        _completed = true;
    }
}
