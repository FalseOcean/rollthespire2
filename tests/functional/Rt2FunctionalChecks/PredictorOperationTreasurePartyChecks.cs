using System.Collections.Immutable;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorOperationTreasurePartyChecks
{
    private sealed class Party(IReadOnlyList<Player> players) : IPlayerCollection
    {
        public IReadOnlyList<Player> Players => players;
        public int GetPlayerSlotIndex(Player player) => players.ToList().IndexOf(player);
        public Player? GetPlayer(ulong netId) => players.FirstOrDefault(p => p.NetId == netId);
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("OPERATION_TREASURE_PARTY " + message); }

    internal static void Run(PredictorRun baseline)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var original = baseline.Working.Streams.Single(s => s.Stream == PredictorStream.TreasureRoomRelics);
        var copiedRng = original.Restore();
        bool observedTie = false, observedConsolation = false, observedSkipped = false;
        for (int count = 2; count <= 4; count++)
        {
            var players = Enumerable.Range(0, count).Select(i => Player.CreateForNewRun<Ironclad>(UnlockState.all, (ulong)(i + 1))).ToArray();
            var models = new RelicModel[] { ModelDb.Relic<Whetstone>(), ModelDb.Relic<WarPaint>(), ModelDb.Relic<OldCoin>(), ModelDb.Relic<Anchor>() }
                .Take(count).ToList();
            var relics = models.Select((r, i) => new PredictorRelic(baseline.Working.NextInstanceId + i,
                new ModelKey(r.Id.Category, r.Id.Entry), 1)).ToImmutableArray();
            var scenarios = new[]
            {
                Enumerable.Repeat<int?>(0, count).ToArray(),
                Enumerable.Range(0, count).Select(i => (int?)i).ToArray(),
                Enumerable.Range(0, count).Select(i => i == 1 ? null : (int?)0).ToArray(),
                Enumerable.Repeat<int?>(null, count).ToArray()
            };
            for (int offset = 0; offset < 24; offset++)
            {
                if (offset == 23)
                {
                    // Exhausted native bags can return the same canonical
                    // fallback reference for several distinct offered slots.
                    models = Enumerable.Repeat<RelicModel>(ModelDb.Relic<Circlet>(), count).ToList();
                    relics = models.Select(r => new PredictorRelic(baseline.Working.NextInstanceId,
                        new ModelKey(r.Id.Category, r.Id.Entry), 1)).ToImmutableArray();
                }
                copiedRng.NextInt(37);
                var state = copiedRng.State;
                var cursor = new PredictorOperationEventRng(state.S0, state.S1, state.S2, state.S3, copiedRng.CallCount);
                foreach (var votes in scenarios)
                {
                    var scene = new PredictorOperationTreasurePartyScene(players.Select(p => p.NetId).ToImmutableArray(), players[0].NetId,
                        relics, players.Select((p, i) => new PredictorOperationTreasureVote(p.NetId, true, votes[i])).ToImmutableArray(), cursor, true);
                    var predicted = PredictorOperationTreasureParty.Query(scene, votes[0]);
                    var nativeRng = new Rng(new SerializableRng { state0 = cursor.S0, state1 = cursor.S1, state2 = cursor.S2,
                        state3 = cursor.S3, counter = cursor.Calls });
                    // AwardRelics only reads the supplied player collection,
                    // relics/votes and RNG. No native run or lifecycle is used.
                    var native = new TreasureRoomRelicSynchronizer(new Party(players), players[0].NetId, null!, null!, nativeRng);
                    typeof(TreasureRoomRelicSynchronizer).GetField("_currentRelics", members)!.SetValue(native, models);
                    var nativeVotes = (List<TreasureRoomRelicSynchronizer.PlayerVote>)typeof(TreasureRoomRelicSynchronizer)
                        .GetField("_votes", members)!.GetValue(native)!;
                    nativeVotes.AddRange(votes.Select(v => new TreasureRoomRelicSynchronizer.PlayerVote { index = v, voteReceived = true }));
                    List<RelicPickingResult>? results = null;
                    native.RelicsAwarded += value => results = value;
                    typeof(TreasureRoomRelicSynchronizer).GetMethod("AwardRelics", members)!.Invoke(native, null);
                    Check(predicted.Status == PredictorOperationTreasureAllocationStatus.Allocated && results != null &&
                        predicted.Awards.Length == results.Count && predicted.Awards.Select(a => a.RelicIndex).Distinct().Count() == relics.Length,
                        "native award count/status or repeated-slot quantity differs");
                    for (int i = 0; i < results!.Count; i++)
                    {
                        var expected = results[i]; var actual = predicted.Awards[i];
                        Check(actual.Kind.ToString() == expected.type.ToString() && actual.PlayerId == expected.player?.NetId &&
                            actual.Relic.Key == new ModelKey(expected.relic.Id.Category, expected.relic.Id.Entry) &&
                            (models.Distinct().Count() != models.Count || actual.RelicIndex == models.IndexOf(expected.relic)), "ordered native award differs");
                        if (expected.fight is { } fight)
                        {
                            Check(actual.Fight != null && actual.Fight.Players.SequenceEqual(fight.playersInvolved.Select(p => p.NetId)) &&
                                actual.Fight.Rounds.Length == fight.rounds.Count, "RPS player/round structure differs");
                            for (int round = 0; round < fight.rounds.Count; round++)
                                Check(actual.Fight!.Rounds[round].SequenceEqual(fight.rounds[round].moves.Select(m => m.HasValue ? (int?)m.Value : null)),
                                    "native RPS transcript differs");
                            observedTie |= fight.rounds.Any(r => r.moves.OfType<RelicPickingFightMove>().Distinct().Count() != 2);
                        }
                    }
                    var after = nativeRng.ToSerializable();
                    Check(predicted.RngAfter == new PredictorOperationEventRng(after.state0, after.state1, after.state2, after.state3, after.counter),
                        "native full allocation RNG differs");
                    observedConsolation |= predicted.Awards.Any(a => a.Kind == PredictorOperationTreasureAwardKind.ConsolationPrize);
                    observedSkipped |= predicted.Awards.Any(a => a.Kind == PredictorOperationTreasureAwardKind.Skipped);
                }
            }
            var pendingScene = new PredictorOperationTreasurePartyScene(players.Select(p => p.NetId).ToImmutableArray(), players[0].NetId,
                relics, players.Select(p => new PredictorOperationTreasureVote(p.NetId, false, null)).ToImmutableArray(),
                new(original.S0, original.S1, original.S2, original.S3, original.Calls), false)
            { OptimisticLocalVote = new(players[0].NetId, true, 1) };
            var pending = PredictorOperationTreasureParty.Query(pendingScene, 0);
            Check(pending.PendingPlayers.SequenceEqual(players.Skip(1).Select(p => p.NetId)) && pending.Awards.IsEmpty &&
                pending.RngAfter == pendingScene.Rng && pendingScene.Votes.All(v => !v.Received),
                "unknown peer vote was invented or optimistic vote became received");
            var conditions = players.Skip(1).ToImmutableDictionary(p => p.NetId, _ => (int?)0);
            var conditional = PredictorOperationTreasureParty.Query(pendingScene, 0, conditions);
            Check(conditional.Status == PredictorOperationTreasureAllocationStatus.Allocated && conditional.ConditionalVotes.Length == count - 1 &&
                pendingScene.Votes.All(v => !v.Received), "conditional votes changed actual received votes");
        }
        Check(observedTie && observedConsolation && observedSkipped, "fixtures did not exercise ties/consolation/skips");
        Check(baseline.Working.Streams.Single(s => s.Stream == PredictorStream.TreasureRoomRelics) == original,
            "allocation query changed captured source RNG");
        Console.WriteLine("OPERATION_TREASURE_PARTY native AwardRelics parity 2/3/4 players: ties/transcripts/consolation/skips/full RNG; unknown/conditional votes preserved PASS");
    }
}
