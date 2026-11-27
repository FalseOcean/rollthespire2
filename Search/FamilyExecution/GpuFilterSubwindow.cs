namespace RolltheSpire2.Search.FamilyExecution;

// A public upload/readback slot owns a bounded pair of buffers. A resident
// append writes directly to the caller's checked ordinal buffer.
internal static class GpuFilterSubwindow
{
    internal static int Capacity(int ordinary, bool resident, int scratchWords = 0)
    {
        if (GpuRootStorageBudget.Current is not { } budget || budget.RootCapacity <= PrivateOrdinalBuffer.Capacity)
            return ordinary;
        int limit = resident ? budget.RootCapacity : Math.Min(1 << 22, budget.RootCapacity);
        if (scratchWords > 0)
            limit = Math.Min(limit, Math.Max(64, (32 << 20) / sizeof(uint) / scratchWords / 64 * 64));
        return Math.Max(ordinary, limit);
    }
}
