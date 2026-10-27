namespace RolltheSpire2.Core.PredictorRuntime;

// These are the existing combat-plan binding/shape failures that route previews
// may leave pending. Diagnostic wording does not decide whether execution failed.
internal enum PredictorCombatPlanBindingReason
{
    NodeOverridesInvalid,
    FactsMustReplaceAllRelevantInstances,
    TemporaryCardFactsInvalid,
    CardSourceOrderInvalid,
    StolenCardFactsInvalid,
    RelicFactsInvalid,
    GremlinGoldFactsInvalid,
    StolenGoldLossExceedsBudget
}

internal sealed class PredictorCombatPlanBindingException(
    PredictorCombatPlanBindingReason reason, string message, Exception? innerException = null)
    : ArgumentException(message, innerException)
{
    internal PredictorCombatPlanBindingReason Reason { get; } = reason;

    // Keep the allowance explicit: a future reason is not automatically a
    // recoverable old plan, and a plain same-text ArgumentException is not one.
    internal static bool IsPendingPlanBinding(ArgumentException exception) => exception is PredictorCombatPlanBindingException
    {
        Reason: PredictorCombatPlanBindingReason.NodeOverridesInvalid or
            PredictorCombatPlanBindingReason.FactsMustReplaceAllRelevantInstances or
            PredictorCombatPlanBindingReason.TemporaryCardFactsInvalid or
            PredictorCombatPlanBindingReason.CardSourceOrderInvalid or
            PredictorCombatPlanBindingReason.StolenCardFactsInvalid or
            PredictorCombatPlanBindingReason.RelicFactsInvalid or
            PredictorCombatPlanBindingReason.GremlinGoldFactsInvalid or
            PredictorCombatPlanBindingReason.StolenGoldLossExceedsBudget
    };
}
