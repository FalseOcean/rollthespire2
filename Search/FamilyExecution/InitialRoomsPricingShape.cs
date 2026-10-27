using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record InitialRoomsTargetShape(int Slot, ModelKey Key, int IdentityGroup,
    int RarityLane, bool PresentInSharedBag, bool MatchesExhaustedCirclet);

// Immutable source geometry only. Neither hardware coefficients nor observed
// survivors/timings participate in this collector/planning descriptor.
internal sealed record InitialRoomsPricingShape(string BodyRevision, string Scope, int Players,
    int UnknownPrefixCount, bool TreasureCovered, int TreasureStart, int TreasureCount,
    TreasureRelicOrderMode? TreasureOrder, IReadOnlyList<int> EffectiveRarityCounts,
    IReadOnlyList<int> SharedBucketCounts, IReadOnlyList<int> SharedBucketLanes,
    int LastRequiredShuffleBucket, int SharedShuffleDraws, IReadOnlyList<InitialRoomsTargetShape> Targets,
    string TreasureRemainder)
{
    internal string Key => FormattableString.Invariant($"{BodyRevision};Scope={Scope};Players={Players};Unknown={UnknownPrefixCount};Treasure={TreasureCovered};Window={TreasureStart}+{TreasureCount};Order={TreasureOrder};Bags={string.Join(',', EffectiveRarityCounts)};Shared={string.Join(',', SharedBucketCounts.Zip(SharedBucketLanes).Select(bucket => $"{bucket.Second}:{bucket.First}"))};Last={LastRequiredShuffleBucket};Shuffle={SharedShuffleDraws};Targets={string.Join(',', Targets.Select(t => $"{t.Slot}:{t.RarityLane}:G{t.IdentityGroup}:C{t.MatchesExhaustedCirclet}"))}");
}

internal sealed partial class InitialRoomsPlan
{
    internal InitialRoomsPricingShape DescribePricingShape()
    {
        var groups = new Dictionary<ModelKey, int>();
        var targets = new List<InitialRoomsTargetShape>();
        for (int slot = 0; slot < (_treasure?.Count ?? 0); slot++)
        {
            if (_treasure!.Slots[slot] is not { } key) continue;
            if (!groups.TryGetValue(key, out int group)) groups.Add(key, group = groups.Count);
            targets.Add(new(slot, key, group, _targetLanes[slot], _targetPositions[slot] >= 0, _circlet[slot]));
        }
        bool party = _request.CompiledSearch.Context.Party is not null;
        return new(ActualRevision, party ? "Party.SharedOrdinaryUnknown.Uncommitted" : "Solo.InitialRooms.Uncommitted",
            _request.Authority.PlayersCount, UnknownPrefixCount, CoversTreasure,
            _treasure?.StartOrdinal ?? 0, _treasure?.Count ?? 0, _treasure?.OrderMode,
            Array.AsReadOnly(_effectiveLengths.ToArray()), Array.AsReadOnly(_lengths.ToArray()),
            Array.AsReadOnly(_bucketLanes.ToArray()), _lastBucket,
            _lengths.Take(_lastBucket + 1).Sum(length => Math.Max(0, length - 1)),
            targets.AsReadOnly(), TreasureRemainder);
    }
}
