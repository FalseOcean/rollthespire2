namespace RolltheSpire2.Search.FamilyExecution;

// 可選的雙 RenderingDevice 流水線；Neow 與落後一組的下游工作分開執行。
// 預設關閉，啟用仍須通過裝置探測與篩選鏈檢查。
internal static class FamilyPipelineModel
{
#if RT2_NEOW_PIPELINE
    internal static readonly bool Compiled = true;
#else
    internal static readonly bool Compiled = false;
#endif

#if RT2_NEOW_EPOCH_BATCH
    internal static readonly bool EpochCompiled = true;
#else
    internal static readonly bool EpochCompiled = false;
#endif
}
