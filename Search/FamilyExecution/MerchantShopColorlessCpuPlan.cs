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
    private readonly record struct Predicate(int Call, ModelKey Target, ModelKey[] Pool);
    private readonly Predicate[] _predicates;
    private readonly int _slot;
    private readonly record struct Observation(int Call, int Merchant, bool Uncommon, ModelKey[] Pool, ModelKey[] FixedTargets);
    private readonly record struct Sequence(int Count, bool Uncommon, ushort[] Targets);
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
        var predicates = new List<Predicate>();
        foreach (var c in e.MerchantColorlessConditions)
        {
            if (c.MerchantOrdinal is < 1 or > 5) return null;
            bool u = c.Slot == MerchantColorlessSlot.Uncommon;
            if (!u && c.Slot != MerchantColorlessSlot.Rare) return null;
            predicates.Add(new((c.MerchantOrdinal - 1) * Beta111NormalMerchantShopsContinuation.CallsPerMerchant +
                (u ? Beta111NormalMerchantShopsContinuation.UncommonCallWithinMerchant : Beta111NormalMerchantShopsContinuation.RareCallWithinMerchant),
                c.TargetCardKey, u ? uncommon : rare));
        }
        return new(predicates.OrderBy(p => p.Call).ToArray(), authority.PlayerSlotIndex);
    }

    internal bool Matches(ulong root)
    {
        if (_observations.Length != 0) return MatchesSequences(root);
        var rng = new Beta110FastRng(unchecked(root + (ulong)_slot + ShopsHash));
        int call = 0;
        ModelKey actual = default;
        foreach (var p in _predicates)
        {
            if (call != p.Call)
            {
                while (++call < p.Call) _ = rng.NextDouble();
                actual = p.Pool[rng.NextInt(p.Pool.Length)];
            }
            if (actual != p.Target) return false;
        }
        // Later calls cannot change an already-observed slot. S exports ordinals,
        // not continuation state; another Family independently replays its RNG.
        return true;
    }

    private static MerchantShopColorlessCpuPlan? CompileSequences(ExactSearchExecutionRequest request, Beta111MerchantColorlessAuthority authority)
    {
        var fixedSlots = request.Evaluation.MerchantColorlessConditions.ToList();
        var unordered = new List<Sequence>();
        var u = authority.UncommonPool.ToArray();
        var r = authority.RarePool.ToArray();
        if (u.Length >= ushort.MaxValue || r.Length >= ushort.MaxValue) return null;
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
                unordered.Add(new(c.Count, c.Slot == MerchantColorlessSlot.Uncommon, targets));
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
            observations.Add(new(call, merchant, uncommon, uncommon ? u : r, targets));
        }
        // Entirely wildcard ordered conditions need no observation, but still use
        // a compiled numerical realization; returning true is exact here.
        return new(observations.ToArray(), unordered.ToArray(), authority.PlayerSlotIndex);
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
            ushort actual = checked((ushort)rng.NextInt(p.Pool.Length));
            var lane = p.Uncommon ? uncommon : rare;
            lane[p.Merchant - 1] = actual;
            foreach (var target in p.FixedTargets) if (p.Pool[actual] != target) return false;
            foreach (var sequence in _sequences)
                if (sequence.Uncommon == p.Uncommon && sequence.Count == p.Merchant &&
                    !ShopSequenceSemantics.MatchesDense(lane, sequence.Count, false, sequence.Targets, ushort.MaxValue)) return false;
        }
        return true;
    }
}
