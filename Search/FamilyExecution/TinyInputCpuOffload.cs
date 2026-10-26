namespace RolltheSpire2.Search.FamilyExecution;

// 可選的少量候選 CPU 分流，直接使用 family 既有的參考實作，減少 GPU 往返。
// 預設關閉，由 Rt2TinyInputCpuOffload 啟用。
internal static class TinyInputCpuOffload
{
#if RT2_TINY_INPUT_CPU_OFFLOAD
    internal const bool Compiled = true;
    internal const int ThresholdInputs = 32;
#else
    internal const bool Compiled = false;
    internal const int ThresholdInputs = 0;
#endif

    internal static bool ShouldOffload(FamilyCandidateSet input) =>
        Compiled && !input.IsDense && input.Count > 0 && input.Count <= ThresholdInputs;
}
