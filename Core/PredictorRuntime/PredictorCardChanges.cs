using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorTransformation(long OriginalId, long ReplacementId);
internal sealed record SelectTransformationCard(long CardId) : PredictorInput;

internal static class PredictorCardChanges
{
    internal static PredictorCardDefinition Definition(PredictorContext context, ModelKey key) =>
        (context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing")).CardDefinitions.Single(d => d.Prototype.Key == key);

    internal static ImmutableArray<long> TransformCandidates(PredictorContext context, PredictorState state) =>
        state.Deck.Where(id => state.Card(id).Removable && !Definition(context, state.Card(id).Key).IsQuest).ToImmutableArray();

    internal static PredictorState TransformTo(PredictorContext context, PredictorState state, long originalId, ModelKey key)
    {
        if (!state.Deck.Contains(originalId)) throw new ArgumentException("PredictorTransformInstanceMissing");
        var replacement = Definition(context, key).Prototype;
        state = state.WithCard(state.Card(originalId) with { Removed = true, SpoilsCoord = null }) with { Deck = state.Deck.Remove(originalId) };
        return PredictorObtainSources.AddCard(context, state, replacement);
    }

    internal static (PredictorState State, ImmutableArray<PredictorTransformation> Changes) Transform(
        PredictorContext context, PredictorState entry, ImmutableArray<long> targets, PredictorStream stream, CancellationToken token = default,
        bool upgradeReplacementBeforeAdd = false)
    {
        if (targets.IsDefault || targets.Distinct().Count() != targets.Length ||
            targets.Any(id => !entry.Deck.Contains(id) || !entry.Card(id).Removable))
            throw new ArgumentException("PredictorTransformTargetsInvalid");
        var catalog = context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
        var definitions = catalog.CardDefinitions.ToDictionary(d => d.Prototype.Key);
        var state = entry;
        var replacements = new List<(int Index, long OriginalId, PredictorCard Replacement)>();
        foreach (long id in targets)
        {
            token.ThrowIfCancellationRequested();
            var original = state.Card(id);
            var definition = definitions[original.Key];
            var pool = catalog.TransformationPools.Single(p => p.Key == definition.TransformationPool).Cards
                .Where(k => k != original.Key && (definition.Rarity is "Curse" or "Status" || definitions[k].Rarity is "Common" or "Uncommon" or "Rare")).ToArray();
            if (pool.Length == 0) throw new InvalidOperationException("PredictorTransformPoolDepleted");
            var draw = state.Draw(stream, 0, pool.Length);
            state = draw.State;
            var replacement = definitions[pool[draw.Value]].Prototype with { Id = state.NextInstanceId, FloorAdded = state.Position.Floor };
            if (upgradeReplacementBeforeAdd && replacement.UpgradeLevel < replacement.MaxUpgradeLevel)
                replacement = PredictorCardLevelEffects.Reconcile(context, replacement with { UpgradeLevel = replacement.UpgradeLevel + 1 });
            PredictorSettlementEffects.RequireImplementedCard(replacement, context: context);
            replacements.Add((state.Deck.IndexOf(id), id, replacement));
            state = state with { NextInstanceId = state.NextInstanceId + 1, Deck = state.Deck.Remove(id) };
        }
        // Vanilla records each index AFTER earlier removals, then List.Sort by
        // that index. Replacements append to Deck; originals are never re-keyed.
        replacements.Sort((a, b) => a.Index.CompareTo(b.Index));
        var changes = ImmutableArray.CreateBuilder<PredictorTransformation>();
        foreach (var item in replacements)
        {
            token.ThrowIfCancellationRequested();
            var card = item.Replacement;
            state = state.WithCard(state.Card(item.OriginalId) with { Removed = true, SpoilsCoord = null });
            state = state with { Cards = state.Cards.Add(card) };
            int nextDeckIndex = state.Deck.Length;
            state = PredictorObtainSources.AddExistingCard(context, state, card);
            changes.Add(new(item.OriginalId, state.Deck[nextDeckIndex]));
        }
        state.Validate();
        return (state, changes.ToImmutable());
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
        var result = PredictorCardChanges.Transform(Context, Working, [id], PredictorStream.Niche, token);
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
