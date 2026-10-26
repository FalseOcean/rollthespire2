namespace RolltheSpire2.Search.FamilyExecution;

// 將工作記入共用 epoch；記錄階段不得 Submit/Sync，完成同步後才收取結果。
// 結果、排序、ABI1、摘要與逐批記帳均沿用原規則。
internal interface IFamilyEpochInvocation
{
    // 目前計畫是否支援共用 epoch。
    bool EpochEligible { get; }

    // 確保執行器已在 epoch 裝置建立。
    ValueTask EnsureEpochExecutorAsync(FamilyExecutionContext context, CancellationToken cancellationToken);

    // 第一階段先驗證、上傳並排入讀回複製；此時不可開啟計算清單。
    void PrepareEpoch(FamilyEpoch epoch, IReadOnlyList<FamilyObservationWindow> windows,
        IReadOnlyList<FamilyCandidateSet> inputs, CancellationToken cancellationToken);

    // 第二階段將 Dispatch 記入已開啟的共用清單。
    void RecordEpochDispatches(FamilyEpoch epoch);

    // epoch 同步完成後收取逐窗口結果；共用耗時按原記帳欄位分攤。
    IReadOnlyList<FamilyCandidateSet> CompleteEpoch(FamilyExecutionContext context,
        double sharedSubmitMs, double sharedSyncMs);

    // epoch 中止時盡量清理已記錄的工作。
    void AbortEpoch();
}
