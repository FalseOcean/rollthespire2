namespace RolltheSpire2.Core.PredictorRuntime;

// An execution limit of the synchronous map preview, not a limit on valid
// combat declarations. Full authored simulation keeps its existing contract.
internal sealed class PredictorDrawingWorkLimitException(long operations) : Exception("PredictorDrawingCombatWorkLimit")
{
    internal const int MaximumOperations = 4096;
    internal long Operations { get; } = operations;
}
