namespace Godot;

// 只模擬傳輸 API 的順序與資料完整性，不能代表原生 GPU 正確性或效能。
public readonly record struct Rid(ulong Id)
{
    public bool IsValid => Id != 0;
}

public enum Error { Ok, Failed, InvalidParameter }

public sealed class RenderingDevice
{
    private readonly Dictionary<Rid, byte[]> _buffers = [];
    private readonly Queue<Action> _pendingCopies = [];
    private ulong _nextId = 1;
    public readonly List<TransportCall> Calls = [];
    public Error NextCopyError { get; set; } = Error.Ok;
    public Queue<Error> ScriptedCopyErrors { get; } = [];
    public Exception? NextReadException { get; set; }
    public int? NextReadResultLength { get; set; }
    public int PendingCopies => _pendingCopies.Count;

    public Rid Register(byte[] bytes)
    {
        Rid id = new(_nextId++);
        _buffers.Add(id, bytes);
        return id;
    }

    public Error BufferCopy(Rid source, Rid destination, uint sourceOffset, uint destinationOffset, uint size)
    {
        Calls.Add(new("Copy", source, destination, sourceOffset, destinationOffset, size));
        Error forcedError = ScriptedCopyErrors.TryDequeue(out Error scripted) ? scripted : NextCopyError;
        NextCopyError = Error.Ok;
        if (forcedError != Error.Ok) return forcedError;
        if (!_buffers.TryGetValue(source, out byte[]? sourceBytes) ||
            !_buffers.TryGetValue(destination, out byte[]? destinationBytes) ||
            (ulong)sourceOffset + size > (ulong)sourceBytes.Length ||
            (ulong)destinationOffset + size > (ulong)destinationBytes.Length)
            return Error.InvalidParameter;

        // BufferCopy 只排入工作，不能立即改動目的資料。
        _pendingCopies.Enqueue(() => sourceBytes.AsSpan(checked((int)sourceOffset), checked((int)size))
            .CopyTo(destinationBytes.AsSpan(checked((int)destinationOffset), checked((int)size))));
        return Error.Ok;
    }

    public byte[] BufferGetData(Rid buffer, uint offset = 0, uint size = 0)
    {
        Calls.Add(new("Read", buffer, default, offset, 0, size));
        if (NextReadException is Exception failure)
        {
            NextReadException = null;
            throw failure;
        }
        if (!_buffers.TryGetValue(buffer, out byte[]? bytes)) throw new IOException("Invalid readback buffer");
        if (size == 0) size = checked((uint)bytes.Length);
        if ((ulong)offset + size > (ulong)bytes.Length) throw new IOException("Readback out of range");
        // 同步讀回須等傳輸完成，才填入 CPU 陣列。
        while (_pendingCopies.TryDequeue(out Action? copy)) copy();
        if (NextReadResultLength is int injectedLength)
        {
            NextReadResultLength = null;
            byte[] injected = new byte[injectedLength];
            bytes.AsSpan(checked((int)offset), Math.Min(checked((int)size), injectedLength)).CopyTo(injected);
            return injected;
        }
        return bytes.AsSpan(checked((int)offset), checked((int)size)).ToArray();
    }
}

public readonly record struct TransportCall(string Operation, Rid Source, Rid Destination,
    uint SourceOffset, uint DestinationOffset, uint Bytes);
