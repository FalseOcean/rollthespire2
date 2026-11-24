using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class PredictorOperationMendCapture
{
    internal static ImmutableArray<PredictorOperationMendTarget> CaptureTargets(RunState run, Player actor)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!run.Players.Contains(actor)) throw new InvalidOperationException("OperationMendActorNotInRun");
        return run.Players.Where(p => p != actor).Select(p => new PredictorOperationMendTarget(
            p.NetId, p.Creature.CurrentHp, p.Creature.MaxHp,
            p.Relics.Count(r => !r.IsMelted && r.Id.Entry == "REGAL_PILLOW"),
            p.Relics.Count(r => !r.IsMelted && r.Id.Entry == "STONE_HUMIDIFIER"))
            { ActiveForHooks = p.IsActiveForHooks }).ToImmutableArray();
    }
}
