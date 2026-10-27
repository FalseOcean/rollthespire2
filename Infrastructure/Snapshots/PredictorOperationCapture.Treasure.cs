using System.Collections.Immutable;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private static PredictorOperationScene CaptureTreasure(RunState run, Player player, PredictorCatalog catalog, Node? root)
    {
        var sync = RunManager.Instance.TreasureRoomRelicSynchronizer;
        var node = Nodes<NTreasureRoom>(root).LastOrDefault()
            ?? throw new InvalidOperationException("OperationTreasurePresentationMissing");
        bool opened = Read<bool>(node, "_hasChestBeenOpened");
        var origin = ObservedObtainOrigin(player, root);
        var set = CurrentNativeRewards(run, player, root);
        // Distribution has already assigned the relic and native Obtain has
        // already added it. There is no second offered relic to take here.
        var parent = new PredictorOperationTreasureScene(null, false, true);
        if (origin != null)
            return CaptureObtainChild(player, root, catalog, parent, PredictorObtainReturn.Treasure,
                Id(OutermostObtainOrigin(origin).Relic), origin);
        if (set != null && ObservedRewardSetObtainSource(set) is { } rewardOrigin)
            return CaptureObtainChild(player, root, catalog, parent, PredictorObtainReturn.Treasure,
                Id(OutermostObtainOrigin(rewardOrigin).Relic), rewardOrigin, CaptureRewardsSet(set, player, catalog));
        if (set != null) throw new NotSupportedException("OperationTreasureOpeningRewardContinuationMissing");
        if (!opened && IsTreasureOpening(node)) throw new InvalidOperationException("OperationTreasureOpening");
        if (run.Players.Count > 1)
        {
            var votes = Read<List<TreasureRoomRelicSynchronizer.PlayerVote>>(sync, "_votes");
            var optimistic = ReadNullable(sync, "_predictedVote") as TreasureRoomRelicSynchronizer.PlayerVote;
            var rng = Read<MegaCrit.Sts2.Core.Random.Rng>(sync, "_rng").ToSerializable();
            bool allowed = player.Relics.OfType<SilverCrucible>().Where(r => !r.IsMelted).All(r => r.TreasureRoomsEntered > 1);
            return new PredictorOperationTreasurePartyScene(run.Players.Select(p => p.NetId).ToImmutableArray(), player.NetId,
                (sync.CurrentRelics ?? []).Select(r => Relic(r, catalog, Id)).ToImmutableArray(),
                run.Players.Select((p, i) => new PredictorOperationTreasureVote(p.NetId,
                    i < votes.Count && votes[i].voteReceived, i < votes.Count ? votes[i].index : null)).ToImmutableArray(),
                new(rng.state0, rng.state1, rng.state2, rng.state3, rng.counter), opened)
            {
                AllocationActive = sync.CurrentRelics != null, LocalTreasureAllowed = allowed,
                OptimisticLocalVote = optimistic == null ? null : new(player.NetId, optimistic.voteReceived, optimistic.index)
            };
        }
        if (sync.CurrentRelics?.Count > 1) throw new InvalidOperationException("OperationSingleplayerTreasureRelicCountInvalid");
        bool skipped = Read<bool>(sync, "_singleplayerSkipped");
        var relic = skipped ? null : sync.CurrentRelics?.FirstOrDefault();
        if (sync.CurrentRelics == null && !opened) throw new InvalidOperationException("OperationTreasureGenerationInProgress");
        return new PredictorOperationTreasureScene(relic == null ? null : Relic(relic, catalog, Id), relic != null, opened);
    }
}
