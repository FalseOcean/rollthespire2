using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class ScopedTransformationGeneralPlan
{
    private enum Observation { Target, Rare, NonRareTarget }
    private sealed class Counter(IReadOnlyList<string> sources, Observation observation, ModelKey? key)
    {
        internal readonly IReadOnlyList<string> Sources = sources;
        internal readonly Observation Kind = observation;
        internal readonly ModelKey? Key = key;
        internal int Minimum;
        internal uint Maximum = uint.MaxValue;
        internal bool Observes(Card card) => Kind switch
        {
            Observation.Rare => card.Rare == true,
            Observation.Target => card.Key == Key,
            _ => card.Rare == false && card.Key == Key
        };
    }
    private uint[][] _packed = [];
    internal int Draws { get; private set; }
    internal int StreamCount => _streams.Count;
    internal double CounterUpdates { get; private set; }
    internal bool NeedsAllDraws { get; private set; }
    internal uint[][] PackGpu() => _packed;

    private string? CompileCounters()
    {
        var counters = new List<Counter>();
        var byKey = new Dictionary<(string Scope, Observation Kind, ModelKey? Key), Counter>();
        var remainders = new List<(IReadOnlyList<string> Sources, HashSet<ModelKey> Targets)>();
        foreach (var row in _requirements)
        {
            string scope = string.Join('|', row.Sources.Order(StringComparer.Ordinal));
            Counter Get(Observation kind, ModelKey? key)
            {
                var identity = (scope, kind, key);
                if (byKey.TryGetValue(identity, out var previous)) return previous;
                var counter = new Counter(row.Sources, kind, key);
                counters.Add(counter);
                return byKey[identity] = counter;
            }
            if (row.MinimumRareCount > 0)
            {
                var rare = Get(Observation.Rare, null);
                rare.Minimum = Math.Max(rare.Minimum, row.MinimumRareCount);
            }
            bool remainder = row.Predicate == TransformationAggregatePredicate.ContainsMultisetAndRemainingRare;
            if (remainder) remainders.Add((row.Sources, row.TargetMultiset.ToHashSet()));
            foreach (var group in row.TargetMultiset.GroupBy(key => key))
            {
                int required = group.Count();
                var target = Get(Observation.Target, group.Key);
                target.Minimum = Math.Max(target.Minimum, required);
                // All copies satisfy the identity minimum. Only non-Rare copies
                // must fit into the selected target slots.
                if (remainder)
                {
                    var nonRare = Get(Observation.NonRareTarget, group.Key);
                    nonRare.Maximum = Math.Min(nonRare.Maximum, (uint)required);
                }
            }
        }
        var meta = new List<uint> { (uint)counters.Count, (uint)_streams.Count, (uint)counters.Sum(c => c.Minimum) };
        foreach (var counter in counters) meta.AddRange([(uint)counter.Minimum, counter.Maximum]);
        var slotRules = new List<uint>();
        var updates = new List<uint>();
        var ruleOffsets = new Dictionary<string, uint>(StringComparer.Ordinal);
        NeedsAllDraws = remainders.Count != 0;
        foreach (var stream in _streams)
        {
            meta.AddRange([(uint)stream.Offset, (uint)(stream.Offset >> 32), (uint)stream.Blocks.Count,
                (uint)stream.PrefixRange, (uint)stream.RequiredPlusOne]);
            if (stream.PrefixRange != 0) Draws++;
            foreach (var block in stream.Blocks)
            {
                if (block.Pool.Length == 0) return (block.Source ?? "N") + ".EmptyDrawPool";
                Draws += block.Count;
                meta.AddRange([(uint)slotRules.Count, (uint)block.Pool.Length, (uint)block.Count]);
                var relevant = counters.Select((c, i) => (Counter: c, Id: (uint)i))
                    .Where(item => block.Source is not null && item.Counter.Sources.Contains(block.Source)).ToArray();
                var remainderRows = remainders.Where(r => block.Source is not null && r.Sources.Contains(block.Source)).ToArray();
                bool readsRarity = remainderRows.Length != 0 || relevant.Any(item => item.Counter.Kind == Observation.Rare);
                foreach (var card in block.Pool)
                {
                    if (readsRarity && card.Rare is null) return block.Source + ".CardRarityUnknown";
                    if (card.Rare == false && remainderRows.Any(row => !row.Targets.Contains(card.Key)))
                    {
                        slotRules.Add(uint.MaxValue);
                        continue;
                    }
                    var increments = relevant.Where(item => item.Counter.Observes(card)).Select(item => item.Id).ToArray();
                    string signature = string.Join(',', increments);
                    if (!ruleOffsets.TryGetValue(signature, out uint offset))
                    {
                        offset = (uint)updates.Count;
                        updates.Add((uint)increments.Length);
                        updates.AddRange(increments);
                        ruleOffsets.Add(signature, offset);
                    }
                    slotRules.Add(offset);
                    CounterUpdates += (double)block.Count * increments.Length / block.Pool.Length;
                }
            }
        }
        uint slotCount = (uint)slotRules.Count;
        var table = slotRules.Select(pointer => pointer == uint.MaxValue ? pointer : slotCount + pointer).Concat(updates).ToArray();
        _packed = [meta.ToArray(), table];
        return null;
    }
}
