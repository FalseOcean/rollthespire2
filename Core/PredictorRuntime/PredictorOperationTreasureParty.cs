using System.Collections.Immutable;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorOperationTreasureVote(ulong PlayerId, bool Received, int? RelicIndex);
internal sealed record PredictorOperationTreasurePartyScene(ImmutableArray<ulong> Players, ulong LocalPlayerId,
    ImmutableArray<PredictorRelic> Relics, ImmutableArray<PredictorOperationTreasureVote> Votes,
    PredictorOperationEventRng Rng, bool Opened) : PredictorOperationScene
{
    internal PredictorOperationTreasureVote? OptimisticLocalVote { get; init; }
    internal bool LocalTreasureAllowed { get; init; } = true;
    internal bool AllocationActive { get; init; } = true;
    internal override void Restore(PredictorRun run) => run.RestoreOperationTreasureParty(this);
}

internal enum PredictorOperationTreasureAllocationStatus { WaitingForVotes, Allocated, Unavailable }
internal enum PredictorOperationTreasureAwardKind { OnlyOnePlayerVoted, FoughtOver, ConsolationPrize, Skipped }
internal sealed record PredictorOperationTreasureFight(ImmutableArray<ulong> Players,
    ImmutableArray<ImmutableArray<int?>> Rounds);
internal sealed record PredictorOperationTreasureAward(int RelicIndex, PredictorRelic Relic, ulong? PlayerId,
    PredictorOperationTreasureAwardKind Kind, PredictorOperationTreasureFight? Fight = null);
internal sealed record PredictorOperationTreasureAllocation(PredictorOperationTreasureAllocationStatus Status,
    ImmutableArray<ulong> PendingPlayers, ImmutableArray<PredictorOperationTreasureAward> Awards,
    PredictorOperationEventRng RngAfter, ImmutableArray<PredictorOperationTreasureVote> ConditionalVotes,
    ImmutableArray<string> Preconditions, string? UnavailableReason = null);

internal static class PredictorOperationTreasureParty
{
    internal static void Validate(PredictorOperationTreasurePartyScene scene)
    {
        if (scene.Players.IsDefaultOrEmpty || scene.Players.Distinct().Count() != scene.Players.Length ||
            !scene.Players.Contains(scene.LocalPlayerId) || scene.Relics.IsDefault || scene.Votes.IsDefault || scene.Votes.Any(v => v == null) ||
            !scene.Votes.Select(v => v.PlayerId).SequenceEqual(scene.Players) || scene.Rng == null ||
            scene.Rng.Calls < 0 || (scene.Rng.S0 | scene.Rng.S1 | scene.Rng.S2 | scene.Rng.S3) == 0 ||
            scene.Relics.Any(r => r == null || r.Id <= 0 || r.Key.Category != "RELIC" || !r.Key.IsValid) ||
            scene.Relics.GroupBy(r => r.Id).Any(group => group.Distinct().Count() != 1) ||
            scene.Votes.Any(v => v == null || v.RelicIndex is { } i && (i < 0 || scene.AllocationActive && i >= scene.Relics.Length)) ||
            scene.OptimisticLocalVote is { } optimistic &&
            (optimistic.PlayerId != scene.LocalPlayerId || optimistic.RelicIndex is { } index && (index < 0 || scene.AllocationActive && index >= scene.Relics.Length)))
            throw new InvalidDataException("PredictorOperationTreasurePartyInvalid");
    }

    // The explicit local vote may be a skip (null). Unknown peer votes remain
    // pending unless the caller supplies those individual conditional premises.
    // This computes awards only: actual local pickup has its own observed order.
    internal static PredictorOperationTreasureAllocation Query(PredictorOperationTreasurePartyScene scene,
        int? localVote, ImmutableDictionary<ulong, int?>? conditionalVotes = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Validate(scene);
        conditionalVotes ??= ImmutableDictionary<ulong, int?>.Empty;
        ImmutableArray<string> preconditions = ["CapturedAllocationCursorStillApplies",
            "SpecifiedRemainingVotesAreReceivedBeforeAllocation",
            "AllocationOnly_LocalPickupOrderAndObtainEffectsAreSeparate"];
        if (!scene.AllocationActive)
            return new(PredictorOperationTreasureAllocationStatus.Unavailable, [], [], scene.Rng, [], preconditions,
                "NoActiveRelicAllocation");
        bool ValidIndex(int? index) => index == null || index >= 0 && index < scene.Relics.Length;
        if (!ValidIndex(localVote) || conditionalVotes.Any(v => v.Key == scene.LocalPlayerId || !scene.Players.Contains(v.Key) || !ValidIndex(v.Value)))
            throw new ArgumentException("PredictorOperationTreasureVoteInvalid");
        var actualLocal = scene.Votes.Single(v => v.PlayerId == scene.LocalPlayerId);
        if (actualLocal.Received && actualLocal.RelicIndex != localVote)
            throw new ArgumentException("PredictorOperationTreasureReceivedVoteCannotChange");
        var effective = scene.Votes.ToArray();
        var hypotheses = ImmutableArray.CreateBuilder<PredictorOperationTreasureVote>();
        for (int i = 0; i < effective.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var vote = effective[i];
            if (vote.PlayerId == scene.LocalPlayerId)
                effective[i] = new(vote.PlayerId, true, localVote);
            else if (conditionalVotes.TryGetValue(vote.PlayerId, out var value))
            {
                if (vote.Received && vote.RelicIndex != value)
                    throw new ArgumentException("PredictorOperationTreasureReceivedVoteCannotChange");
                effective[i] = new(vote.PlayerId, true, value);
                if (!vote.Received) hypotheses.Add(effective[i]);
            }
        }
        var pending = effective.Where(v => !v.Received).Select(v => v.PlayerId).ToImmutableArray();
        if (!pending.IsEmpty)
            return new(PredictorOperationTreasureAllocationStatus.WaitingForVotes, pending, [], scene.Rng,
                hypotheses.ToImmutable(), preconditions);
        var rng = Xoshiro256StarStar.FromState(scene.Rng.S0, scene.Rng.S1, scene.Rng.S2, scene.Rng.S3, scene.Rng.Calls);
        var awards = ImmutableArray.CreateBuilder<PredictorOperationTreasureAward>();
        var unclaimed = new List<int>();
        for (int relicIndex = 0; relicIndex < scene.Relics.Length; relicIndex++)
        {
            token.ThrowIfCancellationRequested();
            var voters = effective.Where(v => v.RelicIndex == relicIndex).Select(v => v.PlayerId).ToImmutableArray();
            if (voters.IsEmpty) { unclaimed.Add(relicIndex); continue; }
            if (voters.Length == 1)
            {
                awards.Add(new(relicIndex, scene.Relics[relicIndex], voters[0], PredictorOperationTreasureAwardKind.OnlyOnePlayerVoted));
                continue;
            }
            var remaining = voters.ToHashSet();
            var rounds = ImmutableArray.CreateBuilder<ImmutableArray<int?>>();
            while (remaining.Count > 1)
            {
                token.ThrowIfCancellationRequested();
                var moves = voters.Select(p => remaining.Contains(p) ? (int?)rng.NextInt(3) : null).ToImmutableArray();
                rounds.Add(moves);
                var distinct = moves.OfType<int>().Distinct().ToArray();
                if (distinct.Length != 2) continue;
                int loser = (distinct[0] + 1) % 3 == distinct[1] ? distinct[0] : distinct[1];
                for (int i = 0; i < voters.Length; i++) if (moves[i] == loser) remaining.Remove(voters[i]);
            }
            awards.Add(new(relicIndex, scene.Relics[relicIndex], remaining.Single(), PredictorOperationTreasureAwardKind.FoughtOver,
                new(voters, rounds.ToImmutable())));
        }
        var losers = effective.Where(v => v.RelicIndex != null && !awards.Any(a => a.PlayerId == v.PlayerId)).Select(v => v.PlayerId).ToArray();
        unclaimed.Sort((left, right) =>
        {
            var a = scene.Relics[left].Key; var b = scene.Relics[right].Key;
            int category = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
            return category != 0 ? category : string.Compare(a.Entry, b.Entry, StringComparison.Ordinal);
        });
        rng.UnstableShuffle(unclaimed);
        for (int i = 0; i < unclaimed.Count; i++)
            awards.Add(new(unclaimed[i], scene.Relics[unclaimed[i]], i < losers.Length ? losers[i] : null,
                i < losers.Length ? PredictorOperationTreasureAwardKind.ConsolationPrize : PredictorOperationTreasureAwardKind.Skipped));
        var after = rng.State;
        return new(PredictorOperationTreasureAllocationStatus.Allocated, [], awards.ToImmutable(),
            new(after.S0, after.S1, after.S2, after.S3, rng.CallCount), hypotheses.ToImmutable(), preconditions);
    }
}

internal sealed partial class PredictorRun
{
    private PredictorOperationTreasurePartyScene? _operationTreasureParty;
    internal void RestoreOperationTreasureParty(PredictorOperationTreasurePartyScene scene)
    {
        RequireEmptyOperationScene();
        PredictorOperationTreasureParty.Validate(scene);
        if (Context.Operation is not { } operation || operation.PlayerId != scene.LocalPlayerId ||
            operation.PlayerCount != scene.Players.Length || scene.Players[operation.PlayerSlot] != scene.LocalPlayerId ||
            scene.Relics.Any(r => r.Id >= Working.NextInstanceId))
            throw new InvalidDataException("PredictorOperationTreasurePartyContextMismatch");
        _operationTreasureParty = scene;
        _treasureAllowed = scene.LocalTreasureAllowed;
        _treasureObtaining = false;
        TreasureRelic = null;
        Phase = scene.Opened ? PredictorPhase.TreasureRelic : PredictorPhase.TreasureClosed;
        _requestId++;
    }

    internal PredictorOperationTreasurePartyScene? ExportOperationTreasureParty() => _operationTreasureParty is not { } party ? null :
        party with { Opened = Phase != PredictorPhase.TreasureClosed };
}
