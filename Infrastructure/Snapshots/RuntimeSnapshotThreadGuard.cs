namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Records the Godot main thread at the already accepted AppShell attachment boundary.
/// Runtime game-model capture must remain on that thread.
/// </summary>
internal static class RuntimeSnapshotThreadGuard
{
    private static int _mainThreadId;

    public static void BindCurrentThread()
    {
        int current = Environment.CurrentManagedThreadId;
        Interlocked.CompareExchange(ref _mainThreadId, current, 0);
    }

    public static bool IsBound => Volatile.Read(ref _mainThreadId) != 0;

    public static bool IsMainThread =>
        IsBound && Volatile.Read(ref _mainThreadId) == Environment.CurrentManagedThreadId;

    public static void RequireMainThread()
    {
        if (!IsMainThread)
        {
            throw new InvalidOperationException("RuntimeSnapshotCaptureRequiresGodotMainThread");
        }
    }
}
