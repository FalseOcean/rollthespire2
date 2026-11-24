using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    internal static PredictorOperationObtainScene CaptureObtainChild(Player player, Node? root, PredictorCatalog catalog,
        PredictorOperationScene parent, PredictorObtainReturn destination, long sourceId,
        NativeObtainOrigin? origin = null, PredictorOperationRewardsScene? childRewards = null,
        PredictorRestAction? restAction = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        origin ??= ObservedObtainOrigin(player, root)
            ?? throw new NotSupportedException("OperationObtainParentObservationMissing");
        var ancestry = new List<NativeObtainOrigin>();
        for (var current = origin; current != null; current = current.Parent) ancestry.Add(current);
        ancestry.Reverse();
        for (int i = 0; i < ancestry.Count - 1; i++)
        {
            var ancestor = ancestry[i];
            var child = ancestry[i + 1];
            string kind = Key(ancestor.Relic).Entry;
            if (kind is "LARGE_CAPSULE" or "TOUCH_OF_OROBAS")
            {
                int maximum = kind == "LARGE_CAPSULE" ? 2 : 1;
                if (child.ChildOrdinal < 1 || child.ChildOrdinal != ancestor.ChildrenStarted || ancestor.ChildrenStarted > maximum)
                    throw new InvalidOperationException("OperationNestedObtainObservedStageInvalid:" + kind);
                parent = new PredictorOperationNestedObtainScene(parent, Id(ancestor.Relic), Key(ancestor.Relic),
                    destination, sourceId, ancestor.ChildrenStarted) { RestAction = restAction };
                destination = kind == "LARGE_CAPSULE" ? PredictorObtainReturn.InternalLargeCapsule : PredictorObtainReturn.InternalTouch;
                continue;
            }
            var set = NativeRewardSets((MegaCrit.Sts2.Core.Runs.RunState)player.RunState, player, root).SingleOrDefault(s =>
                ObservedRewardSetObtainSource(s) is { } source && ReferenceEquals(source.Relic, ancestor.Relic));
            if (set == null || child.ParentReward == null || !NativeContainsReward(set, child.ParentReward))
                throw new NotSupportedException("OperationNestedObtainParentStageMissing:" + Key(ancestor.Relic).Serialized);
            parent = new PredictorOperationObtainScene(parent, destination, sourceId, Id(ancestor.Relic), Key(ancestor.Relic),
                PredictorOperationObtainChildKind.Rewards)
            {
                RewardScene = CaptureRewardsSet(set, player, catalog), Mandatory = kind == "NEOWS_BONES", RestAction = restAction
            };
            destination = child.ParentReward is MegaCrit.Sts2.Core.Rewards.CardReward
                ? PredictorObtainReturn.RewardAlternative : PredictorObtainReturn.Reward;
            sourceId = Id(child.ParentReward);
        }
        var relic = origin.Relic;
        PredictorOperationObtainScene Scene(PredictorOperationObtainChildKind kind) =>
            new(parent, destination, sourceId, Id(relic), Key(relic), kind) { RestAction = restAction };
        if (childRewards != null)
            return Scene(PredictorOperationObtainChildKind.Rewards) with
                { RewardScene = childRewards, Mandatory = Key(relic).Entry == "NEOWS_BONES" };
        var grids = Nodes<NCardGridSelectionScreen>(root).Where(n => n.IsVisibleInTree()).ToArray();
        var choices = Nodes<NChooseACardSelectionScreen>(root).Where(n => n.IsVisibleInTree()).ToArray();
        var bundles = Nodes<NChooseABundleSelectionScreen>(root).Where(n => n.IsVisibleInTree()).ToArray();
        if (grids.Length + choices.Length + bundles.Length != 1)
            throw new InvalidOperationException("OperationObtainSelectionAmbiguous");
        if (grids.FirstOrDefault() is { } grid)
        {
            var cards = Read<IReadOnlyList<CardModel>>(grid, "_cards");
            if (Key(relic).Entry == "NEW_LEAF")
                return Scene(PredictorOperationObtainChildKind.NewLeaf) with
                    { NewLeafCandidates = cards.Select(Id).ToImmutableArray() };
            if (Key(relic).Entry == "SEA_GLASS")
                return Scene(PredictorOperationObtainChildKind.ChooseManyCards) with
                    { Cards = cards.Select(c => Card(c, catalog)).ToImmutableArray() };
            return Scene(PredictorOperationObtainChildKind.Deck) with { DeckChoice = CaptureDeckObtain(player, root) };
        }
        if (choices.FirstOrDefault() is { } choice)
            return Scene(PredictorOperationObtainChildKind.ChooseCard) with
                { Cards = Read<IReadOnlyList<CardModel>>(choice, "_cards").Select(c => Card(c, catalog)).ToImmutableArray() };
        return Scene(PredictorOperationObtainChildKind.Bundle) with
        {
            Bundles = Read<IReadOnlyList<IReadOnlyList<CardModel>>>(bundles[0], "_bundles")
                .Select(b => b.Select(c => Card(c, catalog)).ToImmutableArray()).ToImmutableArray()
        };
    }

    internal static NativeObtainOrigin OutermostObtainOrigin(NativeObtainOrigin origin)
    {
        while (origin.Parent != null) origin = origin.Parent;
        return origin;
    }
}
