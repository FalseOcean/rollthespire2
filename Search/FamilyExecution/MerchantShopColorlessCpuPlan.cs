using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// S-owned exact CPU realization for slot/sequence predicates. Compile immutable pools and
// observation calls once. No prediction documents/traces or old FastPlan owner.
internal sealed class MerchantShopColorlessCpuPlan
{
    private static readonly ulong ShopsHash = XxHash64.Hash("shops"u8, 0);
    private readonly record struct Predicate(int Call, int PoolLength, bool[] AcceptedIndexes);
    private readonly Predicate[] _predicates;
    private readonly int _slot;
    private readonly record struct Observation(int Call, int Merchant, bool Uncommon, int PoolLength,
        bool[]? FixedAcceptedIndexes, ushort[]? CanonicalIndexes);
    private readonly record struct RequiredMultiplicity(ushort Target, int Count);
    private readonly record struct Sequence(int Count, bool Uncommon, ushort[] Targets,
        RequiredMultiplicity[] Required, int RequiredCount);
    private readonly Observation[] _observations = [];
    private readonly Sequence[] _sequences = [];

    private MerchantShopColorlessCpuPlan(Observation[] observations, Sequence[] sequences, int slot)
    { _predicates = []; _observations = observations; _sequences = sequences; _slot = slot; }

    private MerchantShopColorlessCpuPlan(Predicate[] predicates, int slot)
    { _predicates = predicates; _slot = slot; }

    internal static MerchantShopColorlessCpuPlan? TryCompile(ExactSearchExecutionRequest request)
    {
        var e = request.Evaluation;
        var authority = Beta111MerchantColorlessAuthority.From(request.Authority);
        if (!authority.HasExactV1Inputs) return null;
        if (e.MerchantColorlessSequenceConditions.Count != 0) return CompileSequences(request, authority);
        if (e.MerchantColorlessConditions.Count == 0) return null;
        var uncommon = authority.UncommonPool.ToArray();
        var rare = authority.RarePool.ToArray();
        var predicates = new List<(int Call, ModelKey Target, ModelKey[] Pool)>();
        foreach (var c in e.MerchantColorlessConditions)
        {
            if (c.MerchantOrdinal is < 1 or > 5) return null;
            bool u = c.Slot == MerchantColorlessSlot.Uncommon;
            if (!u && c.Slot != MerchantColorlessSlot.Rare) return null;
            predicates.Add(new((c.MerchantOrdinal - 1) * Beta111NormalMerchantShopsContinuation.CallsPerMerchant +
                (u ? Beta111NormalMerchantShopsContinuation.UncommonCallWithinMerchant : Beta111NormalMerchantShopsContinuation.RareCallWithinMerchant),
                c.TargetCardKey, u ? uncommon : rare));
        }
        return new(predicates.GroupBy(p => p.Call).OrderBy(g => g.Key).Select(g =>
        {
            var pool = g.First().Pool;
            return new Predicate(g.Key, pool.Length, CompileAcceptedIndexes(pool, g.Select(p => p.Target).Distinct().ToArray()));
        }).ToArray(), authority.PlayerSlotIndex);
    }

    internal bool Matches(ulong root)
    {
        if (_observations.Length != 0) return MatchesSequences(root);
        var rng = new Beta110FastRng(unchecked(root + (ulong)_slot + ShopsHash));
        int call = 0;
        foreach (var p in _predicates)
        {
            while (++call < p.Call) _ = rng.NextDouble();
            if (!p.AcceptedIndexes[rng.NextInt(p.PoolLength)]) return false;
        }
        // Later calls cannot change an already-observed slot. S exports ordinals,
        // not continuation state; another Family independently replays its RNG.
        return true;
    }

    private static bool[] CompileAcceptedIndexes(ModelKey[] pool, ModelKey[] targets)
    {
        var accepted = new bool[pool.Length];
        for (int index = 0; index < pool.Length; index++)
        {
            bool matches = true;
            foreach (var target in targets) matches &= pool[index] == target;
            // Every duplicate source entry retains its own draw index. Missing
            // targets and conflicting same-call requirements accept no index.
            accepted[index] = matches;
        }
        return accepted;
    }

    private static MerchantShopColorlessCpuPlan? CompileSequences(ExactSearchExecutionRequest request, Beta111MerchantColorlessAuthority authority)
    {
        var fixedSlots = request.Evaluation.MerchantColorlessConditions.ToList();
        var unordered = new List<Sequence>();
        var u = authority.UncommonPool.ToArray();
        var r = authority.RarePool.ToArray();
        if (u.Length >= ushort.MaxValue || r.Length >= ushort.MaxValue) return null;
        var canonicalU = CompileCanonicalIndexes(u);
        var canonicalR = CompileCanonicalIndexes(r);
        foreach (var c in request.Evaluation.MerchantColorlessSequenceConditions)
        {
            if (c.Count is < 1 or > 5 || c.Slots.Count != c.Count ||
                c.Slot is not (MerchantColorlessSlot.Uncommon or MerchantColorlessSlot.Rare)) return null;
            if (c.OrderMode == CombatRewardSequenceOrderMode.Ordered)
            {
                for (int i = 0; i < c.Count; i++)
                    if (c.Slots[i] is { } key) fixedSlots.Add(new(i + 1, c.Slot, key));
            }
            else if (c.OrderMode == CombatRewardSequenceOrderMode.Unordered)
            {
                var pool = c.Slot == MerchantColorlessSlot.Uncommon ? u : r;
                var targets = new ushort[c.Count];
                for (int i = 0; i < targets.Length; i++)
                {
                    if (c.Slots[i] is not { } key) { targets[i] = ushort.MaxValue; continue; }
                    int index = Array.IndexOf(pool, key);
                    // Missing target is a semantic impossibility, not a wildcard.
                    targets[i] = index < 0 ? (ushort)(ushort.MaxValue - 1) : checked((ushort)index);
                }
                var required = targets.Where(target => target != ushort.MaxValue).GroupBy(target => target)
                    .Select(group => new RequiredMultiplicity(group.Key, group.Count())).ToArray();
                unordered.Add(new(c.Count, c.Slot == MerchantColorlessSlot.Uncommon, targets,
                    required, required.Sum(requirement => requirement.Count)));
            }
            else return null;
        }
        if (fixedSlots.Any(c => c.MerchantOrdinal is < 1 or > 5 ||
            c.Slot is not (MerchantColorlessSlot.Uncommon or MerchantColorlessSlot.Rare))) return null;
        var observations = new List<Observation>();
        for (int merchant = 1; merchant <= 5; merchant++)
        foreach (bool uncommon in new[]{true, false})
        {
            var slot = uncommon ? MerchantColorlessSlot.Uncommon : MerchantColorlessSlot.Rare;
            var targets = fixedSlots.Where(c => c.MerchantOrdinal == merchant && c.Slot == slot).Select(c => c.TargetCardKey).Distinct().ToArray();
            if (targets.Length == 0 && !unordered.Any(c => c.Uncommon == uncommon && c.Count >= merchant)) continue;
            int call = (merchant - 1) * Beta111NormalMerchantShopsContinuation.CallsPerMerchant +
                (uncommon ? Beta111NormalMerchantShopsContinuation.UncommonCallWithinMerchant : Beta111NormalMerchantShopsContinuation.RareCallWithinMerchant);
            var pool = uncommon ? u : r;
            observations.Add(new(call, merchant, uncommon, pool.Length,
                targets.Length == 0 ? null : CompileAcceptedIndexes(pool, targets), uncommon ? canonicalU : canonicalR));
        }
        // Entirely wildcard ordered conditions need no observation, but still use
        // a compiled numerical realization; returning true is exact here.
        return new(observations.ToArray(), unordered.ToArray(), authority.PlayerSlotIndex);
    }

    private static ushort[]? CompileCanonicalIndexes(ModelKey[] pool)
    {
        var firstIndexes = new Dictionary<ModelKey, ushort>(ModelKeyComparer.Instance);
        ushort[]? canonical = null;
        for (int index = 0; index < pool.Length; index++)
        {
            ushort actual = checked((ushort)index);
            if (!firstIndexes.TryGetValue(pool[index], out ushort first))
            {
                firstIndexes.Add(pool[index], actual);
                first = actual;
            }
            else if (canonical is null)
            {
                canonical = new ushort[pool.Length];
                for (int previous = 0; previous < index; previous++) canonical[previous] = checked((ushort)previous);
            }
            if (canonical is not null) canonical[index] = first;
        }
        // Unique pools retain the original identity representation without an
        // extra lookup. Duplicate entries keep their original draw weights.
        return canonical;
    }

    private bool MatchesSequences(ulong root)
    {
        var rng = new Beta110FastRng(unchecked(root + (ulong)_slot + ShopsHash));
        Span<ushort> uncommon = stackalloc ushort[5];
        Span<ushort> rare = stackalloc ushort[5];
        uncommon.Fill(ushort.MaxValue); rare.Fill(ushort.MaxValue);
        int call = 0;
        foreach (var p in _observations)
        {
            while (++call < p.Call) _ = rng.NextDouble();
            ushort actual = checked((ushort)rng.NextInt(p.PoolLength));
            var lane = p.Uncommon ? uncommon : rare;
            // Sequence targets use the first index for their ModelKey. Keep the
            // raw draw index for fixed-slot acceptance, but observe its identity.
            lane[p.Merchant - 1] = p.CanonicalIndexes is { } canonical ? canonical[actual] : actual;
            if (p.FixedAcceptedIndexes is { } accepted && !accepted[actual]) return false;
            foreach (var sequence in _sequences)
            {
                if (sequence.Uncommon != p.Uncommon) continue;
                if (sequence.Count == p.Merchant)
                {
                    if (!ShopSequenceSemantics.MatchesDense(lane, sequence.Count, false, sequence.Targets, ushort.MaxValue)) return false;
                }
                else if (sequence.Count > p.Merchant && !CanCompleteSequence(lane[..p.Merchant], sequence)) return false;
            }
        }
        return true;
    }

    private static bool CanCompleteSequence(ReadOnlySpan<ushort> observed, Sequence sequence)
    {
        int remaining = sequence.Count - observed.Length;
        // Even a completely unmet requirement fits while this holds; avoid a
        // prefix scan for wildcard-heavy sequences that cannot yet be rejected.
        if (remaining >= sequence.RequiredCount) return true;
        int missing = 0;
        foreach (var requirement in sequence.Required)
        {
            int available = 0;
            foreach (ushort actual in observed) if (actual == requirement.Target) available++;
            missing += Math.Max(0, requirement.Count - available);
            // Each remaining merchant supplies at most one index for this lane.
            // A proven deficit rejects only this sequence's candidate; separate
            // sequence horizons and fixed-slot conjunctions remain unchanged.
            if (missing > remaining) return false;
        }
        return true;
    }
}
