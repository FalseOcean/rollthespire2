using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;

namespace RolltheSpire2.Core.PredictorRuntime;

// Live capture is a present-state premise. It never claims that an active
// room's remaining choices have been settled, or recreates its entry draws.
internal sealed record PredictorLiveContext(int OriginAct, bool CanLeaveCurrentNode,
    ImmutableArray<PredictorCombatPendingCause> PendingCauses)
{
    // Native history retains room facts across Acts, but not their map coords.
    public ImmutableArray<PredictorVisitFact> PreviousVisits { get; init; } = [];
}

internal sealed record PredictorVisitFact(MapPointType PointType, PredictorRoomKind Room,
    ImmutableArray<PredictorCombatKind> CombatKinds);

internal sealed partial class PredictorRun
{
    private IEnumerable<PredictorVisitFact> RoomHistory => (Context.Live?.PreviousVisits ?? Context.Operation?.PreviousVisits ?? [])
        .Concat(Working.Visits.Select(v => new PredictorVisitFact(v.PointType, v.Room, v.CombatKinds)));
}

internal static class PredictorLiveInitialization
{
    internal static PredictorRun FromSnapshot(PredictorContext context, PredictorState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        if (context.Crystal != null || context.Live is not { } live ||
            live.OriginAct is < 1 or > 3 || live.OriginAct != state.Position.Act ||
            live.PreviousVisits.IsDefault || live.PreviousVisits.Any(v => v == null || v.CombatKinds.IsDefault) ||
            live.PendingCauses.IsDefault || live.PendingCauses.Any(c => c == null ||
                string.IsNullOrWhiteSpace(c.Code) || string.IsNullOrWhiteSpace(c.Description)) ||
            state.Maps.IsDefaultOrEmpty || state.Maps[^1].Act != live.OriginAct ||
            state.Acts.IsDefaultOrEmpty || !state.Acts.Any(a => a.Act == live.OriginAct) ||
            !live.CanLeaveCurrentNode && !live.PendingCauses.Any(c =>
                (c.AffectedDomains & PredictorCombatDomain.Continuation) != 0))
            throw new InvalidDataException("PredictorLiveSnapshotInvalid");
        // This constructor publishes precisely the copied boundary. It does
        // not initialize world queues/bags, apply Ascension or starting obtains.
        state.Validate();
        return new(context, state);
    }
}
