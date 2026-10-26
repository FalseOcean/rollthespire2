namespace RolltheSpire2.Search.FamilyExecution;

// 可選的 K 批合併，只改 GPU 排程；排序、游標與 Exact 仍逐批處理。
// 未啟用時 K=1；啟用後預設 K=4，可另選 K=2 或 K=8。
internal static class FamilyBatchGrouping
{
#if RT2_NEOW_BATCH_MERGE_SIZE_2
    internal const int GroupSize = 2;
#elif RT2_NEOW_BATCH_MERGE_SIZE_8
    internal const int GroupSize = 8;
#elif RT2_NEOW_BATCH_MERGE
    internal const int GroupSize = 4;
#else
    internal const int GroupSize = 1;
#endif

    internal static bool Enabled => GroupSize > 1;

    // Relic 合併預設關閉；配對測試的同步收益不足以抵銷額外命令與複製成本。
    internal static bool RelicEnabled =>
#if RT2_BATCH_MERGE_RELIC
        Enabled;
#else
        false;
#endif
}

// 僅限提交前拒絕合併時使用，讓上層退回逐窗口執行；實際執行故障必須拋出。
internal sealed class FamilyBatchNotSupportedException(string message, Exception? inner = null)
    : Exception(message, inner);

// 多個獨立窗口共用一次提交，輸入與結果按索引對應，保留逐窗口語義。
// 只合併相同實作路徑；群組失敗不交付部分結果。取消點只在提交前與完成後。
internal interface IFamilyBatchInvocation
{
    int BatchGroupCapacity { get; }

    ValueTask<IReadOnlyList<FamilyCandidateSet>> InvokeBatchAsync(
        FamilyExecutionContext executionContext,
        IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> inputs,
        CancellationToken cancellationToken);
}
