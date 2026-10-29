using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed record OperationPotionDemand(bool IncludeBasicResult = true, bool IncludeOrder = true,
    bool IncludeInventory = true)
{
    internal static readonly OperationPotionDemand All = new();
}

internal static partial class PotionOperationPreview
{
    internal static OperationPreview Build(PredictorContext context, ModelKey potion, CancellationToken token = default,
        OperationPotionDemand? demand = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();
        demand ??= OperationPotionDemand.All;
        if (!demand.IncludeBasicResult) return EmptyPotion(potion);
        var query = PredictorOperationPotionQueries.Query(context, potion);
        token.ThrowIfCancellationRequested();
        var cards = query.Cards.OrderBy(c => c.Group).ThenBy(c => c.Ordinal).Select(c => c.Card).ToImmutableArray();
        ImmutableArray<OperationPreviewSection> sections = cards.IsEmpty ? [] :
            [new(potion, [], [new(0, cards, query.RequiresCardChoice ? 1 : cards.Length,
                query.RequiresCardChoice ? 1 : cards.Length, false, false) { PotionUseOrder = [potion] }])
                { Label = "PotionCombatUse", FeatureId = PotionFeature(potion) }];
        return new(potion, [], [], false,
            [new([], sections, query.MissingInputs, 1, query.IdentitiesComputed)], [])
        {
            // The target capture is independent of room/scene availability.
            // No combat insertion, native potion use or lifecycle participates.
            Style = new(OperationTextMode.None, OperationCardLayout.Parallel)
        };
    }

    internal static OperationPreview Build(PredictorOperationSnapshot snapshot, ModelKey potion, int slot,
        CancellationToken token = default, OperationPotionDemand? demand = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        demand ??= OperationPotionDemand.All;
        if (slot < 0 || slot >= snapshot.State.PotionSlots.Length ||
            snapshot.State.PotionSlots[slot] is not { } actual || actual.Key != potion)
            throw new ArgumentException("PotionPreviewInstanceMismatch", nameof(slot));
        if (PredictorOperationPotionQueries.Supports(potion))
            return Build(snapshot.Context, potion, token, demand);
        if (potion.Entry != "ENTROPIC_BREW") return new(potion, [], [], false, [], []);
        if (!demand.IncludeBasicResult && !demand.IncludeInventory) return EmptyPotion(potion);

        // The legacy inventory supplement explicitly empties other detached
        // slots. Without it, even this context-only overload uses the actual
        // captured inventory; turning a supplement off never invents capacity.
        var hypothetical = !demand.IncludeInventory ? snapshot.State : snapshot.State with
        {
            PotionSlots = snapshot.State.PotionSlots.Select((p, i) => i == slot ? p : null).ToImmutableArray()
        };
        PredictorInput input = new UseOutOfCombatPotion(slot, actual.Id);
        try
        {
            var result = PredictorSettlementEffects.UseEntropicBrew(hypothetical, snapshot.Context.PotionPool, slot);
            token.ThrowIfCancellationRequested();
            var generated = result.Generated.Select(p => p.Key).ToImmutableArray();
            int obtained = result.State.PotionSlots.Count(p => p != null &&
                !snapshot.State.PotionSlots.Any(original => original?.Id == p.Id));
            ImmutableArray<OperationPreviewRow> rows = generated.IsEmpty ? [] :
                [new(OperationPreviewAction.GeneratePotion, generated, [], false)
                    { Label = demand.IncludeInventory
                        ? obtained == generated.Length ? "EntropicOtherSlotsEmpty" : $"EntropicOtherSlotsEmpty:{generated.Length}:{obtained}"
                        : $"EntropicCurrentSlots:{generated.Length}:{obtained}" }];
            return new(potion, [], [], false,
                [new([], rows.IsEmpty ? [] : [new(potion, rows, [])
                    { FeatureId = demand.IncludeInventory ? "extra:potion-inventory" : PotionFeature(potion) }], [], 1, true)], [])
            {
                Style = OperationDisplayPolicy.A1
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new(potion, [], [], false, [], [])
            { Faults = [new(demand.IncludeInventory ? "EntropicBrewEmptyOtherSlots" : "EntropicCurrentSlots", [input], error)] };
        }
    }

    private static string PotionFeature(ModelKey potion) => "potion:" + potion.Entry;
    private static OperationPreview EmptyPotion(ModelKey potion) => new(potion, [], [], false, [], []);
}
