using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private Func<Context, PredictorPlannedRewardChoice, bool>? EventCombatRewardWriter(Context context) =>
        context.Node.Event is { NeededInput: PredictorEventNeededInput.Combat, CombatEncounter: not null,
            Combat.MissingFacts.EventTimeout: false } && EventPlanApplies(context, context.Node.Event)
            ? (current, choice) => Set(current, EventRewardPlan(current, choice)) : null;
}
