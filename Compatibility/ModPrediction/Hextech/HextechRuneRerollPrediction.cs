using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal enum HextechRuneRerollOutcome { Replaced, Unchanged, NoiseRestored, Unavailable, Unknown }
internal sealed record HextechRuneRerollCandidate(ModelKey Key, int Weight, int NativeValue = 0);
internal sealed record HextechRuneRerollSnapshot(string Fingerprint, ModelKey Source, int Slot,
    bool Enemy, bool Multiplayer, ImmutableArray<ModelKey> CurrentPanel, bool Golden, bool Noise,
    bool CanReroll, string Seed, int Act, int Floor, int Stage, string PlayerKey, int Ordinal,
    int Rarity, int CurrentEnemyValue, ImmutableArray<HextechRuneRerollCandidate> Candidates,
    PredictorStreamState? Niche, string? Issue)
{
    internal HextechRuneRerollState? FullState { get; init; }
}
internal sealed record HextechRuneRerollResult(string Fingerprint, int Slot, bool Enemy,
    bool Multiplayer, ImmutableArray<ModelKey> CurrentPanel, ImmutableArray<ModelKey> NextPanel,
    HextechRuneRerollOutcome Outcome, string? Issue);

internal sealed record HextechRuneRerollOption(ModelKey Key, int Rarity, bool Upgrade,
    bool Guaranteed, int NativeValue = -1);
internal sealed record HextechRuneRerollPoolCandidate(HextechRuneRerollOption Option, int TagWeight, bool IsCharacter);
internal sealed record HextechRuneRerollPool(int Rarity,
    ImmutableArray<HextechRuneRerollPoolCandidate> Candidates, string? Issue = null);
internal sealed record HextechRuneRerollState(ImmutableArray<HextechRuneRerollOption> Panel,
    ImmutableArray<int> Counts, int Limit, ImmutableHashSet<ModelKey> Seen,
    int CharacterWeight, bool HasCharacterPool, ImmutableArray<HextechRuneRerollPool> Pools,
    PredictorStreamState? Niche, int Ordinal, bool Golden, int GoldenRarity, int? NoiseSlot,
    bool CanAct, int? StageRarity, bool Chaos);
internal sealed record HextechRuneRerollStep(HextechRuneRerollState State, HextechRuneRerollResult Result, bool Legal);
internal sealed record HextechRuneRerollPrepared(HextechRuneRerollState State, int Rarity,
    ImmutableArray<HextechRuneRerollPoolCandidate> Pool, ImmutableArray<HextechRuneRerollCandidate> Candidates, string? Issue);

// The same detached transition owns both next-click and routed prediction.
internal static class HextechRuneRerollPrediction
{
    internal static HextechRuneRerollResult Predict(HextechRuneRerollSnapshot snapshot) =>
        Step(snapshot, snapshot.FullState ?? throw new InvalidOperationException("HextechRuneRerollFullStateMissing"), snapshot.Slot).Result;

    internal static bool IsLegal(HextechRuneRerollSnapshot snapshot, HextechRuneRerollState state, int slot) =>
        state.CanAct && slot >= 0 && slot < state.Panel.Length && (!snapshot.Enemy || state.Panel[slot].NativeValue >= 0) &&
        (!snapshot.Enemy && state.NoiseSlot == slot || state.Limit == -1 || state.Counts[slot] < state.Limit);

    internal static HextechRuneRerollStep Step(HextechRuneRerollSnapshot snapshot, HextechRuneRerollState state,
        int slot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var before = state.Panel.Select(option => option.Key).ToImmutableArray();
        HextechRuneRerollStep Result(HextechRuneRerollState next, HextechRuneRerollOutcome outcome,
            string? issue = null, bool legal = true) => new(next,
                new(snapshot.Fingerprint, slot, snapshot.Enemy, snapshot.Multiplayer, before,
                    outcome == HextechRuneRerollOutcome.Unknown ? [] : next.Panel.Select(option => option.Key).ToImmutableArray(), outcome, issue), legal);
        if (!IsLegal(snapshot, state, slot)) return Result(state, HextechRuneRerollOutcome.Unavailable, legal: false);
        if (!snapshot.Enemy && state.NoiseSlot == slot)
            return Result(state with { NoiseSlot = null }, HextechRuneRerollOutcome.NoiseRestored);
        var prepared = Prepare(snapshot, state, slot, token);
        var next = prepared.State;
        if (prepared.Issue != null) return Result(next, HextechRuneRerollOutcome.Unknown, prepared.Issue);
        if (prepared.Candidates.IsEmpty) return Result(next, HextechRuneRerollOutcome.Unchanged);
        if (!snapshot.Enemy && state.Chaos) return Result(next, HextechRuneRerollOutcome.Unknown, "HextechRuneRerollChaosUnknown");
        int index;
        if (snapshot.Enemy)
        {
            string pool = string.Join(",", prepared.Candidates.Select(c => c.NativeValue.ToString()).OrderBy(x => x, StringComparer.Ordinal));
            index = StableIndex(prepared.Candidates.Length, snapshot.Seed, "|act:", snapshot.Act.ToString(),
                "|floor:", snapshot.Floor.ToString(), "|", "enemy-hex-reroll", "|", snapshot.Stage.ToString(),
                "|", prepared.Rarity.ToString(), "|", state.Panel[slot].NativeValue.ToString(), "|", state.Counts[slot].ToString(), "|", pool);
        }
        else
        {
            int total = prepared.Candidates.Sum(c => c.Weight);
            int roll;
            if (snapshot.Multiplayer)
            {
                var parts = new List<string> { snapshot.Seed, "|act:", snapshot.Stage.ToString(), "|player:", snapshot.PlayerKey,
                    "|rarity:", prepared.Rarity.ToString(), "|slot:", slot.ToString(), "|ordinal:", state.Ordinal.ToString() };
                foreach (var candidate in prepared.Candidates)
                { parts.Add("|pool:"); parts.Add(candidate.Key.Entry); parts.Add(":"); parts.Add(candidate.Weight.ToString()); }
                roll = StableIndex(total, parts.ToArray());
            }
            else
            {
                var niche = (state.Niche ?? throw new InvalidOperationException("HextechRuneRerollNicheMissing")).Restore();
                roll = niche.NextInt(total);
                next = next with { Niche = PredictorStreamState.Capture(PredictorStream.Niche, niche) };
            }
            index = 0;
            while (index < prepared.Candidates.Length - 1 && roll >= prepared.Candidates[index].Weight)
            { roll -= prepared.Candidates[index].Weight; index++; }
        }
        var selected = prepared.Pool[index];
        next = next with { Seen = next.Seen.Add(selected.Option.Key) };
        // The enemy helper records the selected kind even if the last fallback
        // produced the same kind. Only a changed screen consumes a reroll use.
        if (selected.Option.Key == state.Panel[slot].Key) return Result(next, HextechRuneRerollOutcome.Unchanged);
        next = next with { Panel = next.Panel.SetItem(slot, selected.Option), Counts = next.Counts.SetItem(slot, checked(next.Counts[slot] + 1)) };
        if (!snapshot.Enemy)
            next = next with { Ordinal = checked(next.Ordinal + 1), Golden = false,
                CharacterWeight = !state.HasCharacterPool ? state.CharacterWeight : selected.IsCharacter
                    ? Math.Max(0, state.CharacterWeight - 10) : checked(state.CharacterWeight + 10) };
        return Result(next, HextechRuneRerollOutcome.Replaced);
    }

    internal static HextechRuneRerollPrepared Prepare(HextechRuneRerollSnapshot snapshot, HextechRuneRerollState state,
        int slot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        int rarity = snapshot.Enemy ? snapshot.Rarity : state.Golden ? state.GoldenRarity :
            state.Panel[slot].Guaranteed && state.StageRarity is { } stage ? stage : state.Panel[slot].Rarity;
        var source = state.Pools.FirstOrDefault(pool => pool.Rarity == rarity)
            ?? throw new InvalidOperationException("HextechRuneRerollPoolNotCaptured:" + rarity);
        if (source.Issue != null) return new(state, rarity, [], [], source.Issue);
        var pool = source.Candidates;
        if (snapshot.Enemy)
        {
            state = state with { Seen = state.Seen.Add(state.Panel[slot].Key) };
            var current = state.Panel[slot].Key;
            var others = state.Panel.Where((_, i) => i != slot).Select(option => option.Key).ToHashSet();
            var filtered = pool.Where(c => c.Option.Key != current && !state.Seen.Contains(c.Option.Key) && !others.Contains(c.Option.Key)).ToImmutableArray();
            if (filtered.IsEmpty) filtered = pool.Where(c => c.Option.Key != current && !state.Seen.Contains(c.Option.Key)).ToImmutableArray();
            if (filtered.IsEmpty) filtered = pool.Where(c => c.Option.Key != current).ToImmutableArray();
            if (!filtered.IsEmpty) pool = filtered;
        }
        else
        {
            var current = state.Panel.Select(option => option.Key).ToHashSet();
            bool upgrade = state.Panel.Where((_, i) => i != slot).Any(option => option.Upgrade);
            var eligible = pool.Where(c => !current.Contains(c.Option.Key) && (!upgrade || !c.Option.Upgrade));
            var filtered = eligible.Where(c => !state.Seen.Contains(c.Option.Key)).ToImmutableArray();
            if (filtered.IsEmpty && !state.Seen.IsEmpty)
            { state = state with { Seen = ImmutableHashSet<ModelKey>.Empty }; filtered = eligible.ToImmutableArray(); }
            pool = snapshot.Multiplayer ? filtered.OrderBy(c => c.Option.Key.Entry, StringComparer.Ordinal).ToImmutableArray() : filtered;
        }
        var weights = pool.Select(c => snapshot.Enemy ? 1 : checked(c.TagWeight * (c.IsCharacter ? state.CharacterWeight : 100))).ToArray();
        if (weights.Sum() <= 0) weights = pool.Select(c => c.TagWeight).ToArray();
        return new(state, rarity, pool, pool.Select((c, i) => new HextechRuneRerollCandidate(c.Option.Key, weights[i], c.Option.NativeValue)).ToImmutableArray(), null);
    }

    private static int StableIndex(int count, params string[] parts)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (string part in parts) foreach (char c in part) { hash ^= c; hash *= 1099511628211UL; }
            hash ^= hash >> 33; hash *= 18397679294719823053UL;
            hash ^= hash >> 33; hash *= 14181476777654086739UL; hash ^= hash >> 33;
            return (int)(hash % (ulong)count);
        }
    }
}
