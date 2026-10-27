using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.InRunOperations;

internal static partial class PotionOperationPreview
{
    internal static OperationPreview Build(PredictorOperationPotionObservation observation,
        CancellationToken token = default, OperationPotionDemand? demand = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        token.ThrowIfCancellationRequested();
        demand ??= OperationPotionDemand.All;
        var source = observation.Source;
        if (source.HeldSlot is int held && (held < 0 || held >= observation.Inventory.Length ||
            observation.Inventory[held] != source.Potion))
            throw new ArgumentException("PotionPreviewInstanceMismatch", nameof(observation));
        if (source.HeldSlot.HasValue == source.RewardId.HasValue)
            throw new ArgumentException("PotionPreviewSourceAmbiguous", nameof(observation));
        var potion = source.Potion.Key;
        if (potion.Entry != "ENTROPIC_BREW" && !PredictorOperationPotionQueries.Supports(potion)) return EmptyPotion(potion);
        bool inventorySupplement = demand.IncludeInventory && (source.RewardId.HasValue || potion.Entry == "ENTROPIC_BREW");
        if (!demand.IncludeBasicResult && !demand.IncludeOrder && !inventorySupplement) return EmptyPotion(potion);
        var notices = ImmutableArray.CreateBuilder<string>();
        var inventories = ImmutableArray.CreateBuilder<(ImmutableArray<PredictorPotion?> Slots, int SourceSlot)>();
        if (source.HeldSlot is int slot) inventories.Add((observation.Inventory, slot));
        else
        {
            notices.Add("PotionAfterAcquisition");
            int empty = observation.Inventory.FindIndex(p => p == null);
            if (empty >= 0) inventories.Add((observation.Inventory.SetItem(empty, source.Potion), empty));
            else
            {
                // Receiving an offered bottle requires freeing a real slot.
                // Preserve every possible discarded bottle, rather than
                // inventing capacity or silently choosing an inventory order.
                notices.Add("PotionAcquisitionRequiresSlot");
                if (!demand.IncludeInventory)
                {
                    // Every legal replacement has the same first-use identity:
                    // receiving the bottle leaves a full belt, then using it
                    // frees one slot. Later ordinals depend on which original
                    // bottle is discarded and belong to the disabled inventory
                    // alternatives; never choose that bottle for the player.
                    if (observation.HasSozu) notices.Add("PotionAcquisitionBlocked");
                    return demand.IncludeBasicResult && observation.Inventory.Length > 0
                        ? FirstOfferedUse(observation, notices.ToImmutable(), token) : EmptyPotion(potion);
                }
                for (int i = 0; i < observation.Inventory.Length; i++)
                    inventories.Add((observation.Inventory.SetItem(i, source.Potion), i));
            }
            if (observation.HasSozu) notices.Add("PotionAcquisitionBlocked");
        }
        if (potion.Entry == "ENTROPIC_BREW")
            return Entropic(observation, inventories.ToImmutable(), notices.ToImmutable(), token, demand);
        return CardGeneration(observation.Context, potion, inventories.ToImmutable(), notices.ToImmutable(), token, demand,
            inventorySupplement);
    }

    private static OperationPreview CardGeneration(PredictorContext context, ModelKey potion,
        ImmutableArray<(ImmutableArray<PredictorPotion?> Slots, int SourceSlot)> inventories,
        ImmutableArray<string> notices, CancellationToken token, OperationPotionDemand demand, bool inventorySupplement)
    {
        var alternatives = new SortedDictionary<int, List<PredictorOperationPotionQueryResult>>();
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        int minimumBottles = inventories.IsEmpty ? 0 : inventories.Min(inventory => inventory.Slots.Count(p =>
            p != null && PredictorOperationPotionQueries.Supports(p.Key)));
        int examined = 0;
        foreach (var inventory in inventories.Take(demand.IncludeOrder ? inventories.Length : 1))
        {
            token.ThrowIfCancellationRequested();
            var others = demand.IncludeOrder ? inventory.Slots.Where((p, i) => i != inventory.SourceSlot && p != null &&
                PredictorOperationPotionQueries.Supports(p.Key)).Select(p => p!).ToArray() : [];
            var prefixes = Enumerable.Range(0, others.Length + 1)
                .Select(_ => new HashSet<PredictorOperationEventRng>()).ToArray();
            if (context.Operation?.CombatCardRng is not { } initial)
            {
                foreach (var missing in PredictorOperationPotionQueries.Query(context, potion).MissingInputs) pending.Add(missing);
                continue;
            }
            prefixes[0].Add(initial);
            for (int i = 0; i < others.Length; i++)
            {
                // Each captured bottle is either before or after the hovered
                // bottle. Full-pool shuffles have fixed call counts, so the
                // predecessor order cannot change the resulting RNG cursor.
                for (int count = i; count >= 0; count--)
                    foreach (var cursor in prefixes[count].ToArray())
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var query = PredictorOperationPotionQueries.Query(AtCursor(context, cursor), others[i].Key);
                            foreach (var missing in query.MissingInputs) pending.Add(missing);
                            if (query.RngAfter is { } after) prefixes[count + 1].Add(after);
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        { faults.Add(new("PotionUsePredecessor:" + others[i].Key.Entry, [], error)); }
                    }
            }
            for (int count = 0; count < prefixes.Length; count++)
                foreach (var cursor in prefixes[count].OrderBy(p => p.Calls))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var query = PredictorOperationPotionQueries.Query(AtCursor(context, cursor), potion);
                        examined++;
                        foreach (var missing in query.MissingInputs) pending.Add(missing);
                        if (!query.IdentitiesComputed) continue;
                        if (!alternatives.TryGetValue(count + 1, out var results)) alternatives[count + 1] = results = [];
                        if (!results.Any(other => SameCards(other, query))) results.Add(query);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { faults.Add(new("PotionUseOrdinal:" + (count + 1), [], error)); }
                }
        }
        bool grouped = alternatives.Keys.Any(ordinal => ordinal > 1);
        var style = new OperationDisplayStyle(OperationTextMode.None,
            grouped ? OperationCardLayout.Groups : OperationCardLayout.Parallel);
        var groups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
        foreach (var (ordinal, results) in alternatives)
            foreach (var query in results)
            {
                var cards = query.Cards.OrderBy(c => c.Group).ThenBy(c => c.Ordinal).Select(c => c.Card).ToImmutableArray();
                int select = query.RequiresCardChoice ? 1 : cards.Length;
                groups.Add(new(groups.Count, cards, select, select, false, results.Count > 1 || ordinal > minimumBottles)
                { Label = grouped ? "PotionUseStep:" + ordinal : null, EarliestFirst = grouped });
            }
        var section = new OperationPreviewSection(potion, [], groups.ToImmutable())
        {
            Label = grouped ? "PotionUseOrdinal" : "PotionCombatUse", Style = style,
            FeatureId = grouped || !demand.IncludeBasicResult && demand.IncludeOrder ? "extra:potion-order" :
                inventorySupplement ? "extra:potion-inventory" : PotionFeature(potion)
        };
        return new(potion, [], [], false,
            [new([], groups.Count == 0 ? [] : [section], pending.ToImmutableArray(), examined, pending.Count == 0 && faults.Count == 0)], [])
        { Style = style, Notices = notices, Faults = faults.ToImmutable() };
    }

    private static PredictorContext AtCursor(PredictorContext context, PredictorOperationEventRng cursor) =>
        context with { Operation = context.Operation! with { CombatCardRng = cursor } };

    private static bool SameCards(PredictorOperationPotionQueryResult a, PredictorOperationPotionQueryResult b) =>
        a.Cards.Select(c => (c.Card.Key, c.Card.UpgradeLevel)).SequenceEqual(
            b.Cards.Select(c => (c.Card.Key, c.Card.UpgradeLevel)));

    private static OperationPreview Entropic(PredictorOperationPotionObservation observation,
        ImmutableArray<(ImmutableArray<PredictorPotion?> Slots, int SourceSlot)> inventories,
        ImmutableArray<string> notices, CancellationToken token, OperationPotionDemand demand)
    {
        var potion = observation.Source.Potion.Key;
        var results = new SortedDictionary<int, List<(ImmutableArray<ModelKey> Models, int Obtained)>>();
        var faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        int minimumBrews = inventories.IsEmpty ? 0 : inventories.Min(inventory => inventory.Slots.Count(p => p?.Key == potion));
        foreach (var inventory in inventories.Take(demand.IncludeOrder ? inventories.Length : 1))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var pool = observation.Context.PotionPool;
                if (pool.IsDefaultOrEmpty) throw new InvalidOperationException("PredictorPotionPoolNotInitialized");
                var rng = observation.CombatPotionRng.Restore();
                int empty = inventory.Slots.Count(p => p == null);
                int brews = demand.IncludeOrder ? inventory.Slots.Count(p => p?.Key == potion) : 1;
                for (int ordinal = 1; ordinal <= brews; ordinal++)
                {
                    token.ThrowIfCancellationRequested();
                    // These identical recipes make this the hovered bottle's
                    // result when it is used at this ordinal: the preceding
                    // uses belong to other original bottles, not to repeated
                    // use of the selected instance.
                    // Only bottles in the original inventory participate.
                    // Newly generated brews never extend this continuation.
                    empty++;
                    int attempts = observation.HasSozu ? 1 : empty;
                    var generated = ImmutableArray.CreateBuilder<ModelKey>();
                    for (int i = 0; i < attempts; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        generated.Add(PredictorSettlementEffects.GenerateEntropicPotion(pool, rng));
                    }
                    var models = generated.ToImmutable();
                    int obtained = observation.HasSozu ? 0 : models.Length;
                    empty -= obtained;
                    if (!results.TryGetValue(ordinal, out var alternatives)) results[ordinal] = alternatives = [];
                    if (!alternatives.Any(result => result.Obtained == obtained && result.Models.SequenceEqual(models)))
                        alternatives.Add((models, obtained));
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { faults.Add(new("EntropicCurrentSlots", [], error)); }
        }
        bool sequence = results.Count > 1;
        var rows = results.SelectMany(pair => pair.Value.Select(result =>
            new OperationPreviewRow(OperationPreviewAction.GeneratePotion, result.Models, [], pair.Value.Count > 1 || pair.Key > minimumBrews)
            { Label = sequence ? $"EntropicUseStep:{pair.Key}:{result.Models.Length}:{result.Obtained}" :
                $"EntropicCurrentSlots:{result.Models.Length}:{result.Obtained}" })).ToImmutableArray();
        var style = new OperationDisplayStyle(OperationTextMode.None, OperationCardLayout.Parallel);
        var featureId = sequence || !demand.IncludeBasicResult && demand.IncludeOrder ? "extra:potion-order" :
            demand.IncludeInventory ? "extra:potion-inventory" : PotionFeature(potion);
        return new(potion, [], [], false,
            [new([], rows.IsEmpty ? [] : [new(potion, rows, []) { FeatureId = featureId }], [], inventories.Length, faults.Count == 0)], [])
        { Style = style, Notices = notices, Faults = faults.ToImmutable() };
    }

    private static OperationPreview FirstOfferedUse(PredictorOperationPotionObservation observation,
        ImmutableArray<string> notices, CancellationToken token)
    {
        var potion = observation.Source.Potion.Key;
        try
        {
            token.ThrowIfCancellationRequested();
            if (potion.Entry != "ENTROPIC_BREW")
                return Build(observation.Context, potion, token) with { Notices = notices };
            var model = PredictorSettlementEffects.GenerateEntropicPotion(observation.Context.PotionPool,
                observation.CombatPotionRng.Restore());
            var row = new OperationPreviewRow(OperationPreviewAction.GeneratePotion, [model], [], false)
            { Label = "EntropicCurrentSlots:1:" + (observation.HasSozu ? 0 : 1) };
            var section = new OperationPreviewSection(potion, [row], []) { FeatureId = PotionFeature(potion) };
            return new(potion, [], [], false, [new([], [section], [], 1, true)], [])
            { Style = new(OperationTextMode.None, OperationCardLayout.Parallel), Notices = notices };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return EmptyPotion(potion) with
            { Notices = notices, Faults = [new(potion.Entry == "ENTROPIC_BREW" ? "EntropicCurrentSlots" : "PotionUseOrdinal:1", [], error)] };
        }
    }
}
