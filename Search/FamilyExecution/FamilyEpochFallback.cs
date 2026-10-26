namespace RolltheSpire2.Search.FamilyExecution;

internal static class FamilyEpochFallback
{
    // 只有提交前拒絕合併，才可改用逐窗路徑；執行錯誤照常上拋。
    internal static async Task<T> RunAsync<T>(Func<Task<T>> epoch, Func<Task<T>> perWindow)
    {
        try
        {
            return await epoch().ConfigureAwait(false);
        }
        catch (FamilyBatchNotSupportedException)
        {
            return await perWindow().ConfigureAwait(false);
        }
    }
}
