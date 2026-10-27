using System.Numerics;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class TransformationAggregateNumericalPlan
{
    private const uint RareOutput = 1u << 31;
    private (ulong Hash, int Prefix, uint[][] Pools)[]? _streamingGroups;
    private Func<ulong, bool>? _streamingMatches;
    internal bool UsesStreaming => _streamingGroups is not null;

    private interface IStreamingPredicate
    {
        static abstract bool CountsRare { get; }
        static abstract bool RequiresRareRemainder { get; }
    }
    private readonly struct CountRare : IStreamingPredicate
    {
        public static bool CountsRare => true;
        public static bool RequiresRareRemainder => false;
    }
    private readonly struct ContainsTargets : IStreamingPredicate
    {
        public static bool CountsRare => false;
        public static bool RequiresRareRemainder => false;
    }
    private readonly struct TargetsAndRare : IStreamingPredicate
    {
        public static bool CountsRare => false;
        public static bool RequiresRareRemainder => true;
    }

    private void PrepareStreaming()
    {
        if (!Closed || _targets.Length > 10) return;
        var groups = DrawGroups;
        // A malformed output shape must still reach NumericalOutputCount on the
        // collector. Early rejection is safe only after this invariant is proven.
        int count = groups.Sum(static group => group.Pools.Length);
        if (count != _condition.OpportunityCount || count > 10) return;
        _streamingGroups = groups.Select(group => (
            group.Hash == TransformationsHash && _neow is { } n
                ? unchecked(group.Hash + (ulong)n.Authority.PlayerSlotIndex) : group.Hash,
            group.Prefix,
            group.Pools.Select(pool => pool.Select(id => TargetBits(id) | (_rare[id] ? RareOutput : 0u)).ToArray()).ToArray()
        )).ToArray();
        _streamingMatches = _condition.Predicate switch {
            TransformationAggregatePredicate.RareCountAtLeast => MatchesStreaming<CountRare>,
            TransformationAggregatePredicate.ContainsMultiset => MatchesStreaming<ContainsTargets>,
            TransformationAggregatePredicate.ContainsMultisetAndRemainingRare => MatchesStreaming<TargetsAndRare>,
            _ => MatchesCollected };
    }

    private bool MatchesStreaming<TPredicate>(ulong root) where TPredicate : struct, IStreamingPredicate
    {
        // Preserve identity evaluation, including its failures, before local draws.
        if (_neow is { } n &&
            !(_neowIdentity?.Matches(root) ?? _neowIdentityOnly?.Matches(root) ?? NeowFamilyReplay.Observe(root, n).IdentityPass))
            return false;
        int remaining = _condition.OpportunityCount, rare = 0;
        uint missing = (1u << _targets.Length) - 1;
        foreach (var group in _streamingGroups!)
        {
            var rng = new Beta110FastRng(unchecked(root + group.Hash));
            if (group.Prefix == 1) rng.NextInt(19);
            if (group.Prefix == 2 && rng.NextInt(3) != 2) return false;
            foreach (uint[] pool in group.Pools)
            {
                uint output = pool[rng.NextInt(pool.Length)];
                remaining--;
                if (TPredicate.CountsRare)
                {
                    if ((output & RareOutput) != 0) rare++;
                    if (rare + remaining < _condition.MinimumRareCount) return false;
                }
                else
                {
                    uint match = output & missing;
                    if (match != 0) missing &= ~(match & unchecked(0u - match));
                    else if (TPredicate.RequiresRareRemainder && (output & RareOutput) == 0) return false;
                    if (BitOperations.PopCount(missing) > remaining) return false;
                }
            }
        }
        return TPredicate.CountsRare ? rare >= _condition.MinimumRareCount : missing == 0;
    }
}
