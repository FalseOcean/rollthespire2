using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

// The ordinal is observed at the real nested Obtain call, never inferred from inventory.
internal sealed record PredictorOperationNestedObtainScene(PredictorOperationScene Parent,
    long RelicId, ModelKey Source, PredictorObtainReturn Destination, long SourceId,
    int ChildrenStarted) : PredictorOperationScene
{
    internal PredictorRestAction? RestAction { get; init; }
    internal override void Restore(PredictorRun run) => run.RestoreOperationNestedObtain(this);
}

internal sealed partial class PredictorRun
{
    internal void RestoreOperationNestedObtain(PredictorOperationNestedObtainScene scene)
    {
        if (scene.Parent == null || !Working.Relics.Any(r => r.Id == scene.RelicId && r.Key == scene.Source) ||
            scene.Source.Entry is not ("LARGE_CAPSULE" or "TOUCH_OF_OROBAS") ||
            scene.ChildrenStarted < 1 || scene.ChildrenStarted > (scene.Source.Entry == "LARGE_CAPSULE" ? 2 : 1))
            throw new InvalidDataException("OperationNestedObtainObservationInvalid");
        if (scene.Parent is PredictorOperationShopScene shop)
            RestoreOperationShop(shop, allowUnboundPurchase: true);
        else scene.Parent.Restore(this);
        if (scene.Destination == PredictorObtainReturn.Rest)
        {
            if (scene.RestAction is not (PredictorRestAction.Dig or PredictorRestAction.Hatch))
                throw new InvalidDataException("OperationNestedRestActionMissing");
            _restPendingObtain = scene.RestAction;
        }
        if (scene.Destination == PredictorObtainReturn.Treasure) _treasureObtaining = true;
        if (scene.Destination == PredictorObtainReturn.RewardAlternative)
        {
            _sacrificePendingRewards = _sacrificePendingRewards.Add(scene.SourceId);
            _cardSelection = null;
        }
        if (scene.Source.Entry == "LARGE_CAPSULE")
            _obtainFrames = _obtainFrames.Add(new(scene.Source, scene.Destination, scene.SourceId,
                Rewards, Phase, scene.ChildrenStarted, true));
        else
            _touchPending = new(scene.Destination, scene.SourceId);
    }

    internal bool HasObservedOperationParent(PredictorObtainReturn destination) => destination switch
    {
        PredictorObtainReturn.InternalLargeCapsule => _obtainFrames.LastOrDefault()?.Relic.Entry == "LARGE_CAPSULE",
        PredictorObtainReturn.InternalTouch => _touchPending != null,
        _ => true
    };
}
