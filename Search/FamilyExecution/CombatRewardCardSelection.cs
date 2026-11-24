namespace RolltheSpire2.Search.FamilyExecution;

// Immutable CPU rank indexes. Every occurrence retains its original pool
// position; excluding a card identity removes all of its occurrences.
internal sealed class CombatRewardCardSelection(Beta110FastEffectCatalog catalog)
{
    internal CardPool Normal { get; } = new(catalog.CombatRewardCardPool);
    internal CardPool Power { get; } = new(catalog.CombatRewardPowerPool);

    internal sealed class CardPool(Beta110FastCardPool source)
    {
        private readonly Pool _common = new(source.Common);
        private readonly Pool _uncommon = new(source.Uncommon);
        private readonly Pool _rare = new(source.Rare);
        private static readonly Pool Empty = new([]);
        internal Pool Rarity(byte rarity) => rarity switch
        {
            1 => _common, 2 => _uncommon, 3 => _rare, _ => Empty
        };
        internal int CountAvailable(ReadOnlySpan<ushort> selected) =>
            _common.CountAvailable(selected) + _uncommon.CountAvailable(selected) + _rare.CountAvailable(selected);
    }

    internal sealed class Pool
    {
        private readonly ushort[] _source;
        private readonly (int Start, int Count)[] _ranges;
        private readonly int[] _positions;

        internal Pool(ushort[] source)
        {
            _source = source;
            int identities = 0;
            foreach (ushort id in source) identities = Math.Max(identities, (int)id + 1);
            _ranges = new (int, int)[identities];
            foreach (ushort id in source) _ranges[id].Count++;
            int offset = 0;
            for (int id = 0; id < _ranges.Length; id++)
            {
                _ranges[id].Start = offset;
                offset += _ranges[id].Count;
            }
            _positions = new int[source.Length];
            var written = new int[identities];
            for (int position = 0; position < source.Length; position++)
            {
                ushort id = source[position];
                _positions[_ranges[id].Start + written[id]++] = position;
            }
        }

        internal int CountAvailable(ReadOnlySpan<ushort> selected)
        {
            int available = _source.Length;
            for (int i = 0; i < selected.Length; i++)
            {
                ushort id = selected[i];
                if (id < _ranges.Length && !selected[..i].Contains(id)) available -= _ranges[id].Count;
            }
            return available;
        }

        internal ushort SelectAvailable(ReadOnlySpan<ushort> selected, int ordinal)
        {
            int position = ordinal;
            // The available rank maps to rank + excluded positions before it.
            // Iterate that monotone equation until all intervening exclusions
            // are accounted for, including repeated source identities.
            while ((uint)position < (uint)_source.Length)
            {
                int excluded = 0;
                for (int i = 0; i < selected.Length; i++)
                {
                    ushort id = selected[i];
                    if (id >= _ranges.Length || selected[..i].Contains(id)) continue;
                    var range = _ranges[id];
                    if (range.Count == 1)
                    {
                        if (_positions[range.Start] <= position) excluded++;
                        continue;
                    }
                    int low = 0, high = range.Count;
                    while (low < high)
                    {
                        int middle = low + (high - low) / 2;
                        if (_positions[range.Start + middle] <= position) low = middle + 1;
                        else high = middle;
                    }
                    excluded += low;
                }
                int next = ordinal + excluded;
                if (next == position) return _source[position];
                position = next;
            }
            return Beta110FastDenseId.Invalid;
        }
    }
}
