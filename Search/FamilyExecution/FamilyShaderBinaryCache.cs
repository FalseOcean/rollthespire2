using Godot;

namespace RolltheSpire2.Search.FamilyExecution;

// Only immutable driver bytecode crosses search/device lifetimes. Each owner
// still creates and frees its own RIDs. The pipeline UUID includes device and
// driver compatibility; this process-local cache never persists across updates.
internal static class FamilyShaderBinaryCache
{
    private const int MaximumEntries = 32;
    private const long MaximumBytes = 32L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<Key, LinkedListNode<Entry>> Entries = [];
    private static readonly LinkedList<Entry> Recent = [];
    private static long _bytes;
    internal readonly record struct Key(string Driver, string Device, string PipelineUuid, string Source);
    private sealed record Entry(Key Key, byte[] Binary, long Bytes);

    internal static Key? Identity(RenderingDevice device, string source)
    {
        if (string.Equals(System.Environment.GetEnvironmentVariable("RT2_SHADER_BINARY_CACHE"), "off", StringComparison.OrdinalIgnoreCase))
            return null;
        string uuid = device.GetDevicePipelineCacheUuid();
        // Without a compatibility identity, keep normal engine creation.
        if (string.IsNullOrWhiteSpace(uuid)) return null;
        return new(RenderingServer.GetCurrentRenderingDriverName(), device.GetDeviceName(), uuid, source);
    }

    internal static bool TryGet(Key key, out byte[] binary)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var node)) { binary = []; return false; }
            Recent.Remove(node); Recent.AddLast(node);
            binary = node.Value.Binary;
            return true;
        }
    }

    internal static void Add(Key key, byte[] binary)
    {
        long size = binary.LongLength + (long)key.Source.Length * sizeof(char);
        if (binary.Length == 0 || size > MaximumBytes) return;
        lock (Gate)
        {
            RemoveCore(key);
            while (Entries.Count >= MaximumEntries || _bytes + size > MaximumBytes)
                RemoveCore(Recent.First!.Value.Key);
            Entries.Add(key, Recent.AddLast(new Entry(key, binary, size)));
            _bytes += size;
        }
    }

    internal static void Remove(Key key) { lock (Gate) RemoveCore(key); }
    private static void RemoveCore(Key key)
    {
        if (!Entries.Remove(key, out var node)) return;
        Recent.Remove(node); _bytes -= node.Value.Bytes;
    }
}
