using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorOperationLinkedRewardGroup(long GroupId, ImmutableArray<long> RewardIds);

internal sealed partial class PredictorRun
{
    private ImmutableArray<PredictorOperationLinkedRewardGroup> _operationLinkedGroups = [];
    private readonly HashSet<long> _operationSeenTakenRewards = [];
    internal ImmutableArray<PredictorOperationLinkedRewardGroup> OperationLinkedGroups => _operationLinkedGroups;

    private IEnumerable<PredictorReward> OperationVisibleAndSuspendedRewards() => Rewards
        .Concat(_obtainFrames.SelectMany(f => f.ParentRewards)).Concat(CompletedChildRewards);

    private void RestoreOperationLinkedGroups(ImmutableArray<PredictorOperationLinkedRewardGroup> groups)
    {
        if (groups.IsDefault || groups.Any(g => g == null || g.GroupId <= 0 || g.RewardIds.IsDefault ||
            g.RewardIds.Distinct().Count() != g.RewardIds.Length || g.RewardIds.Any(id => id <= 0 || id == g.GroupId)) ||
            groups.Select(g => g.GroupId).Distinct().Count() != groups.Length)
            throw new InvalidDataException("PredictorOperationLinkedGroupsInvalid");
        var combined = _operationLinkedGroups;
        foreach (var group in groups)
        {
            var existing = combined.FirstOrDefault(g => g.GroupId == group.GroupId);
            if (existing != null && !existing.RewardIds.SequenceEqual(group.RewardIds))
                throw new InvalidDataException("PredictorOperationLinkedGroupIdentityConflict");
            if (existing == null) combined = combined.Add(group);
        }
        var leaves = OperationVisibleAndSuspendedRewards().Select(r => r.Id).ToHashSet();
        foreach (var group in combined)
        {
            var visiting = new HashSet<long>();
            void Visit(long id)
            {
                var child = combined.FirstOrDefault(g => g.GroupId == id);
                if (child == null)
                {
                    if (!leaves.Contains(id)) throw new InvalidDataException("PredictorOperationLinkedRewardMissing");
                    return;
                }
                if (!visiting.Add(id)) throw new InvalidDataException("PredictorOperationLinkedRewardCycle");
                foreach (long member in child.RewardIds) Visit(member);
                visiting.Remove(id);
            }
            Visit(group.GroupId);
        }
        _operationLinkedGroups = combined;
        _operationSeenTakenRewards.UnionWith(OperationVisibleAndSuspendedRewards().Where(r => r.Status == PredictorRewardStatus.Taken).Select(r => r.Id));
    }

    // Called only by the operation facade. Ordinary route settlement retains
    // its existing reward handling. A picker opening, reroll or cancellation
    // does not acquire a linked alternative; a completed obtain does.
    internal void CompleteOperationLinkedRewards(CancellationToken token)
    {
        if (_operationLinkedGroups.IsEmpty) return;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var newlyTaken = OperationVisibleAndSuspendedRewards().Where(r => r.Status == PredictorRewardStatus.Taken &&
                _operationSeenTakenRewards.Add(r.Id)).Select(r => r.Id).ToHashSet();
            if (newlyTaken.Count == 0) return;
            var retire = new HashSet<long>();
            foreach (var group in _operationLinkedGroups)
            {
                var members = new HashSet<long>();
                void Leaves(long id)
                {
                    var nested = _operationLinkedGroups.FirstOrDefault(g => g.GroupId == id);
                    if (nested == null) members.Add(id);
                    else foreach (long member in nested.RewardIds) Leaves(member);
                }
                Leaves(group.GroupId);
                if (members.Overlaps(newlyTaken)) retire.UnionWith(members.Except(newlyTaken));
            }
            if (retire.Count == 0) continue;
            var retired = new HashSet<long>();
            ImmutableArray<PredictorReward> Apply(ImmutableArray<PredictorReward> rewards) => rewards.Select(r =>
            {
                if (!retire.Contains(r.Id) || r.Status != PredictorRewardStatus.Open) return r;
                retired.Add(r.Id);
                return r with { Status = PredictorRewardStatus.Skipped, CardSelectionEntered = false, RelicSubscriptionActive = false };
            }).ToImmutableArray();
            Rewards = Apply(Rewards);
            for (int i = 0; i < _obtainFrames.Length; i++)
                _obtainFrames = _obtainFrames.SetItem(i, _obtainFrames[i] with { ParentRewards = Apply(_obtainFrames[i].ParentRewards) });
            CompletedChildRewards = Apply(CompletedChildRewards);
            if (_cardSelection is { } selected && retired.Contains(selected)) _cardSelection = null;
            if (retired.Count == 0) continue;
            Accept(new("LinkedRewardAlternativesSkipped", 0, retired.Order().ToImmutableArray(), null, null));
            if (Phase == PredictorPhase.Rewards && HasObtainChild) ContinueObtainChildIfComplete(token);
            DrainObtainCompletions(token);
        }
    }
}
