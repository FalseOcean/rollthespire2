using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal static partial class PotionOperationForecast
{
    private static OperationForecast BuildCore(PredictorOperationPotionObservation observation,
        CancellationToken token = default, OperationPotionForecastScope? demand = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        token.ThrowIfCancellationRequested();
        demand ??= OperationPotionForecastScope.All;
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

    private sealed record PotionCardOutcome(PredictorOperationPotionQueryResult Result,
        List<ImmutableArray<ModelKey>> Orders);

    private static OperationForecast CardGeneration(PredictorContext context, ModelKey potion,
        ImmutableArray<(ImmutableArray<PredictorPotion?> Slots, int SourceSlot)> inventories,
        ImmutableArray<string> notices, CancellationToken token, OperationPotionForecastScope demand, bool inventorySupplement)
    {
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var faults = ImmutableArray.CreateBuilder<OperationForecastFault>();
        int examined = 0;
        bool truncated = false;
        var groups = ImmutableArray.CreateBuilder<OperationOutcomeChoices>();
        var orders = new List<ImmutableArray<ModelKey>>();
        var orderKeys = new HashSet<string>(StringComparer.Ordinal);
        void AddOrder(ImmutableArray<ModelKey> order)
        {
            if (orderKeys.Add(string.Join("|", order.Select(key => key.Serialized)))) orders.Add(order);
        }
        if (!inventories.IsEmpty) AddOrder([potion]);
        if (demand.IncludeOrder)
            foreach (var inventory in inventories)
            {
                token.ThrowIfCancellationRequested();
                var available = inventory.Slots.Where((p, slot) => slot != inventory.SourceSlot && p != null &&
                    PredictorOperationPotionQueries.Supports(p.Key)).Select(p => p!.Key)
                    .OrderBy(key => key.Serialized, StringComparer.Ordinal).ToImmutableArray();
                // Every prefix belongs to one concrete held/acquired inventory.
                // Do not combine bottles across different pickup replacements.
                for (int first = 0; first < available.Length; first++)
                {
                    AddOrder([available[first], potion]);
                    for (int second = 0; second < available.Length; second++)
                        if (first != second) AddOrder([available[first], available[second], potion]);
                }
            }
        for (int depth = 1; depth <= (demand.IncludeOrder ? 3 : 1); depth++)
        {
            var outcomes = new List<PotionCardOutcome>();
            foreach (var order in orders.Where(order => order.Length == depth))
            {
                token.ThrowIfCancellationRequested();
                if (context.Operation?.CombatCardRng is not { } initial)
                {
                    foreach (var missing in PredictorOperationPotionQueries.Query(context, potion).MissingInputs) pending.Add(missing);
                    break;
                }
                var cursor = initial;
                bool ready = true;
                foreach (var predecessor in order.Take(order.Length - 1))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var query = PredictorOperationPotionQueries.Query(AtCursor(context, cursor), predecessor);
                        foreach (var missing in query.MissingInputs) pending.Add(missing);
                        if (query.RngAfter is { } after) cursor = after;
                        else { ready = false; break; }
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { faults.Add(new("PotionUsePredecessor:" + predecessor.Entry, [], error)); ready = false; break; }
                }
                if (!ready) continue;
                examined++;
                try
                {
                    var query = PredictorOperationPotionQueries.Query(AtCursor(context, cursor), potion);
                    foreach (var missing in query.MissingInputs) pending.Add(missing);
                    if (!query.IdentitiesComputed) continue;
                    var outcome = outcomes.FirstOrDefault(other => SamePotionResult(other.Result, query));
                    if (outcome == null) outcomes.Add(new(query, [order]));
                    else outcome.Orders.Add(order);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { faults.Add(new("PotionUseOrdinal:" + depth, [], error)); }
            }
            // Apply the limit after merging complete results, and admit all of
            // a depth together. A matching card face never merges continuations:
            // every longer order above was replayed from the original cursor.
            if (groups.Count + outcomes.Count > 8) { truncated = true; break; }
            foreach (var outcome in outcomes)
            {
                var query = outcome.Result;
                var cards = query.Cards.OrderBy(c => c.Group).ThenBy(c => c.Ordinal).Select(c => c.Card).ToImmutableArray();
                int select = query.RequiresCardChoice ? 1 : cards.Length;
                groups.Add(new(groups.Count, cards, select, select, false, false)
                    { SequenceOrdinal = depth, IconPaths = OperationIconPaths.Compress(outcome.Orders, token) });
            }
        }
        bool grouped = groups.Any(group => group.SequenceOrdinal > 1);
        for (int i = 0; i < groups.Count; i++)
            groups[i] = groups[i] with { Kind = grouped ? "PotionUseStep:" + groups[i].SequenceOrdinal : null,
                SequenceOrdinal = grouped ? groups[i].SequenceOrdinal : null };
        var section = new OperationOutcomeSource(potion, [], groups.ToImmutable())
        {
            Kind = grouped ? "PotionUseOrdinal" : "PotionCombatUse",
            FeatureId = grouped || !demand.IncludeBasicResult && demand.IncludeOrder ? "extra:potion-order" :
                inventorySupplement ? "extra:potion-inventory" : PotionFeature(potion)
        };
        return new(potion, [], [], false,
            [new([], groups.Count == 0 ? [] : [section], pending.ToImmutableArray(), examined,
                !truncated && pending.Count == 0 && faults.Count == 0)], [])
        { Notices = notices, Faults = faults.ToImmutable() };
    }

    private static bool SamePotionResult(PredictorOperationPotionQueryResult left,
        PredictorOperationPotionQueryResult right) => left.Potion == right.Potion &&
        left.TargetPlayerId == right.TargetPlayerId && left.RequiresCardChoice == right.RequiresCardChoice &&
        left.Preconditions.SequenceEqual(right.Preconditions) && left.Cards.Length == right.Cards.Length &&
        left.Cards.OrderBy(card => card.Group).ThenBy(card => card.Ordinal)
            .Zip(right.Cards.OrderBy(card => card.Group).ThenBy(card => card.Ordinal)).All(pair =>
                (pair.First with { Card = pair.First.Card with { Id = 0 } }) ==
                (pair.Second with { Card = pair.Second.Card with { Id = 0 } }));

    private static PredictorContext AtCursor(PredictorContext context, PredictorOperationEventRng cursor) =>
        context with { Operation = context.Operation! with { CombatCardRng = cursor } };

    private static OperationForecast Entropic(PredictorOperationPotionObservation observation,
        ImmutableArray<(ImmutableArray<PredictorPotion?> Slots, int SourceSlot)> inventories,
        ImmutableArray<string> notices, CancellationToken token, OperationPotionForecastScope demand)
    {
        var potion = observation.Source.Potion.Key;
        var results = new SortedDictionary<int, List<(ImmutableArray<ModelKey> Models, int Obtained)>>();
        var faults = ImmutableArray.CreateBuilder<OperationForecastFault>();
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
                int brews = demand.IncludeOrder ? Math.Min(2, inventory.Slots.Count(p => p?.Key == potion)) : 1;
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
            new OperationOutcome(OperationOutcomeAction.GeneratePotion, result.Models, [], pair.Value.Count > 1 || pair.Key > minimumBrews)
            { Kind = sequence ? $"EntropicUseStep:{pair.Key}:{result.Models.Length}:{result.Obtained}" :
                $"EntropicCurrentSlots:{result.Models.Length}:{result.Obtained}" })).ToImmutableArray();
        var featureId = sequence || !demand.IncludeBasicResult && demand.IncludeOrder ? "extra:potion-order" :
            demand.IncludeInventory ? "extra:potion-inventory" : PotionFeature(potion);
        return new(potion, [], [], false,
            [new([], rows.IsEmpty ? [] : [new(potion, rows, []) { FeatureId = featureId }], [], inventories.Length, faults.Count == 0)], [])
        { Notices = notices, Faults = faults.ToImmutable() };
    }

    private static OperationForecast FirstOfferedUse(PredictorOperationPotionObservation observation,
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
            var row = new OperationOutcome(OperationOutcomeAction.GeneratePotion, [model], [], false)
            { Kind = "EntropicCurrentSlots:1:" + (observation.HasSozu ? 0 : 1) };
            var section = new OperationOutcomeSource(potion, [row], []) { FeatureId = PotionFeature(potion) };
            return new(potion, [], [], false, [new([], [section], [], 1, true)], [])
            { Activity = OperationForecastActivity.PotionUse, Notices = notices };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return EmptyPotion(potion) with
            { Notices = notices, Faults = [new(potion.Entry == "ENTROPIC_BREW" ? "EntropicCurrentSlots" : "PotionUseOrdinal:1", [], error)] };
        }
    }
}
