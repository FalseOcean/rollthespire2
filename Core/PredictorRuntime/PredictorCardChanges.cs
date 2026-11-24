using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorTransformation(long OriginalId, long ReplacementId);
internal sealed record SelectTransformationCard(long CardId) : PredictorInput;

internal static class PredictorCardChanges
{
    internal static PredictorCardDefinition Definition(PredictorContext context, ModelKey key) =>
        (context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing")).CardDefinitions.Single(d => d.Prototype.Key == key);

    internal static ImmutableArray<long> TransformCandidates(PredictorContext context, PredictorState state) =>
        state.Deck.Where(id => state.Card(id).Removable && !Definition(context, state.Card(id).Key).IsQuest).ToImmutableArray();

    internal static (PredictorState State, PredictorTransformation Change) TransformTo(
        PredictorContext context, PredictorState state, long originalId, ModelKey key) =>
        TransformTo(context, state, originalId, Definition(context, key).Prototype);

    internal static (PredictorState State, PredictorTransformation Change) TransformTo(
        PredictorContext context, PredictorState state, long originalId, PredictorCard replacement)
    {
        if (!state.Deck.Contains(originalId)) throw new ArgumentException("PredictorTransformInstanceMissing");
        replacement = replacement with { Id = state.NextInstanceId };
        state = state with { NextInstanceId = state.NextInstanceId + 1, Deck = state.Deck.Remove(originalId) };
        int insertion = state.Deck.Length;
        state = AddReplacement(context, state, originalId, replacement);
        return (state, new(originalId, state.Deck[insertion]));
    }

    internal static (PredictorState State, ImmutableArray<PredictorTransformation> Changes) TransformBatch(
        PredictorContext context, PredictorState entry, ImmutableArray<long> targets, PredictorStream stream, CancellationToken token = default)
    {
        ValidateTargets(entry, targets);
        var pools = new ReplacementPools(context);
        var rng = entry.Streams.Single(s => s.Stream == stream).Restore();
        var state = entry;
        var replacements = new List<(int Index, long OriginalId, PredictorCard Replacement)>();
        foreach (long id in targets)
        {
            token.ThrowIfCancellationRequested();
            var replacement = DrawReplacement(context, state, id, rng, pools, upgradeBeforeAdd: false);
            replacements.Add((state.Deck.IndexOf(id), id, replacement));
            state = state with { NextInstanceId = state.NextInstanceId + 1, Deck = state.Deck.Remove(id) };
        }
        state = state.WithRng(stream, rng);
        // Vanilla records each index AFTER earlier removals, then List.Sort by
        // that index. Replacements append to Deck; originals are never re-keyed.
        replacements.Sort((a, b) => a.Index.CompareTo(b.Index));
        var changes = ImmutableArray.CreateBuilder<PredictorTransformation>();
        foreach (var item in replacements)
        {
            token.ThrowIfCancellationRequested();
            int nextDeckIndex = state.Deck.Length;
            state = AddReplacement(context, state, item.OriginalId, item.Replacement);
            changes.Add(new(item.OriginalId, state.Deck[nextDeckIndex]));
        }
        state.Validate();
        return (state, changes.ToImmutable());
    }

    internal static (PredictorState State, ImmutableArray<PredictorTransformation> Changes, Xoshiro256StarStar Rng) TransformInOrder(
        PredictorContext context, PredictorState entry, ImmutableArray<long> targets, Xoshiro256StarStar sourceRng,
        CancellationToken token, PredictorStream? stateStream = null, bool upgradeBeforeAdd = false)
    {
        ValidateTargets(entry, targets);
        var rng = sourceRng.Clone();
        var pools = new ReplacementPools(context);
        var state = entry;
        var changes = ImmutableArray.CreateBuilder<PredictorTransformation>(targets.Length);
        // Astrolabe and the sequential event transforms finish deck insertion
        // and its hooks before drawing the next selected card.
        foreach (long id in targets)
        {
            token.ThrowIfCancellationRequested();
            var replacement = DrawReplacement(context, state, id, rng, pools, upgradeBeforeAdd);
            state = state with { NextInstanceId = state.NextInstanceId + 1, Deck = state.Deck.Remove(id) };
            if (stateStream is { } stream) state = state.WithRng(stream, rng);
            int insertion = state.Deck.Length;
            state = AddReplacement(context, state, id, replacement);
            changes.Add(new(id, state.Deck[insertion]));
        }
        state.Validate();
        return (state, changes.MoveToImmutable(), rng);
    }

    private static void ValidateTargets(PredictorState state, ImmutableArray<long> targets)
    {
        if (targets.IsDefault || targets.Distinct().Count() != targets.Length ||
            targets.Any(id => !state.Deck.Contains(id) || !state.Card(id).Removable))
            throw new ArgumentException("PredictorTransformTargetsInvalid");
    }

    private static PredictorCard DrawReplacement(PredictorContext context, PredictorState state, long originalId,
        Xoshiro256StarStar rng, ReplacementPools pools, bool upgradeBeforeAdd)
    {
        var pool = pools.For(state.Card(originalId).Key);
        if (pool.Length == 0) throw new InvalidOperationException("PredictorTransformPoolDepleted");
        return PrepareReplacement(context, state, pool[rng.NextInt(pool.Length)], state.NextInstanceId, upgradeBeforeAdd);
    }

    private sealed class ReplacementPools
    {
        private readonly PredictorCatalog _catalog;
        private readonly Dictionary<ModelKey, PredictorCardDefinition> _definitions;
        private readonly Dictionary<ModelKey, PredictorCardDefinition[]> _pools = [];

        internal ReplacementPools(PredictorContext context)
        {
            _catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
            _definitions = _catalog.CardDefinitions.ToDictionary(d => d.Prototype.Key);
        }

        internal PredictorCardDefinition[] For(ModelKey original)
        {
            if (!_pools.TryGetValue(original, out var pool))
                _pools.Add(original, pool = ReplacementPool(_catalog, _definitions, original).Select(key => _definitions[key]).ToArray());
            return pool;
        }
    }

    // Each legal instance can occupy every displayed slot when the required
    // number of distinct instances exists. The draw and card face for that
    // slot do not depend on the other selections. These IDs are display-only.
    internal static ImmutableArray<ImmutableArray<PredictorCard>> ProjectReplacementSlots(PredictorContext context,
        PredictorState state, ImmutableArray<long> candidates, int slots, Xoshiro256StarStar startingRng,
        bool upgradeBeforeAdd, CancellationToken token, out int examined)
    {
        examined = 0;
        if (slots < 1 || slots > candidates.Length) return default;
        var pools = new ReplacementPools(context);
        var inputs = candidates.Select(id => state.Card(id).Key).Distinct().Select(key =>
        {
            token.ThrowIfCancellationRequested();
            return pools.For(key);
        }).ToArray();
        if (inputs.Any(pool => pool.Length == 0)) return default;

        var rng = startingRng.Clone();
        var groups = ImmutableArray.CreateBuilder<ImmutableArray<PredictorCard>>(slots);
        for (int slot = 0; slot < slots; slot++)
        {
            double draw = rng.NextDouble();
            var cards = ImmutableArray.CreateBuilder<PredictorCard>();
            var seen = new HashSet<PredictorCard>();
            foreach (var pool in inputs)
            {
                token.ThrowIfCancellationRequested();
                var replacement = PrepareReplacement(context, state, pool[(int)(draw * pool.Length)],
                    state.NextInstanceId + slot, upgradeBeforeAdd);
                var added = PredictorObtainSources.PrepareAddedCard(context, state, replacement);
                if (seen.Add(added)) cards.Add(added);
            }
            groups.Add(cards.ToImmutable());
        }
        examined = inputs.Length * slots;
        return groups.MoveToImmutable();
    }

    internal static ModelKey[] ReplacementPool(PredictorCatalog catalog,
        IReadOnlyDictionary<ModelKey, PredictorCardDefinition> definitions, ModelKey original)
    {
        var definition = definitions[original];
        return catalog.TransformationPools.Single(p => p.Key == definition.TransformationPool).Cards
            .Where(key => key != original && (definition.Rarity is "Curse" or "Status" ||
                definitions[key].Rarity is "Common" or "Uncommon" or "Rare")).ToArray();
    }

    private static PredictorCard PrepareReplacement(PredictorContext context, PredictorState state,
        PredictorCardDefinition definition, long replacementId, bool upgradeBeforeAdd)
    {
        var replacement = definition.Prototype with { Id = replacementId, FloorAdded = state.Position.Floor };
        if (upgradeBeforeAdd && replacement.UpgradeLevel < replacement.MaxUpgradeLevel)
            replacement = PredictorCardLevelEffects.Reconcile(context, replacement with { UpgradeLevel = replacement.UpgradeLevel + 1 });
        PredictorSettlementEffects.RequireImplementedCard(replacement, context: context);
        return replacement;
    }

    private static PredictorState AddReplacement(PredictorContext context, PredictorState state, long originalId, PredictorCard replacement)
    {
        state = state.WithCard(state.Card(originalId) with { Removed = true, SpoilsCoord = null });
        return PredictorObtainSources.AddExistingCard(context, state with { Cards = state.Cards.Add(replacement) }, replacement, isTransformation: true);
    }

    internal static PredictorState NeowsTalisman(PredictorContext context, PredictorState state)
    {
        var basics = state.Deck.Select(state.Card).Where(c => Definition(context, c.Key).Rarity == "Basic").ToArray();
        foreach (var card in new[] { basics.LastOrDefault(c => Definition(context, c.Key).IsStrike), basics.LastOrDefault(c => Definition(context, c.Key).IsDefend) })
            if (card != null && card.UpgradeLevel < card.MaxUpgradeLevel) state = state.WithCard(PredictorCardLevelEffects.Reconcile(context, card with { UpgradeLevel = card.UpgradeLevel + 1 }));
        return state;
    }
}

internal sealed partial class PredictorRun
{
    private void BeginNewLeaf(long? parentReward, CancellationToken token)
    {
        _parentReward = parentReward;
        var candidates = OperationTransformCandidates();
        Phase = PredictorPhase.TransformCards;
        _requestId++;
        if (candidates.Length <= 1)
        {
            if (candidates.Length == 1) ApplyNewLeaf(candidates[0], token);
            else FinishNewLeaf();
        }
    }

    private PredictorInputResult ApplyNewLeaf(long id, CancellationToken token)
    {
        if (!OperationTransformCandidates().Contains(id)) return PredictorInputResult.Invalid;
        var result = PredictorCardChanges.TransformBatch(Context, Working, [id], Context.RelicTransformStream, token);
        token.ThrowIfCancellationRequested();
        Working = result.State;
        Accept(new("NewLeafTransform", id, result.Changes.Select(c => c.ReplacementId).ToImmutableArray(), null, null)
        { Transformations = result.Changes });
        FinishNewLeaf();
        return PredictorInputResult.Accepted;
    }

    private void FinishNewLeaf()
    {
        if (_parentReward is long parent) MarkTaken(parent);
        _parentReward = null;
        Phase = PredictorPhase.Rewards;
        _requestId++;
        TryResumeNewLeafObtain();
        if (IsOpeningNode && Rewards.Length == 0 && !HasObtainChild) Submit(Request!, new LeaveRewards());
    }
}
