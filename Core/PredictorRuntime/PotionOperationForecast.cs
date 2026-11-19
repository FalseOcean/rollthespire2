using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record OperationPotionForecastScope(bool IncludeBasicResult = true, bool IncludeOrder = true,
    bool IncludeInventory = true)
{
    internal static readonly OperationPotionForecastScope All = new();
}

internal static partial class PotionOperationForecast
{
    internal static OperationForecast Build(PredictorContext context,ModelKey potion,CancellationToken token=default,
        OperationPotionForecastScope? scope=null) => BuildCore(context,potion,token,scope) with {Activity=OperationForecastActivity.PotionUse};
    internal static OperationForecast Build(PredictorOperationSnapshot snapshot,ModelKey potion,int slot,CancellationToken token=default,
        OperationPotionForecastScope? scope=null) => BuildCore(snapshot,potion,slot,token,scope) with {Activity=OperationForecastActivity.PotionUse,Fingerprint=snapshot.Fingerprint};
    internal static OperationForecast Build(PredictorOperationPotionObservation observation,CancellationToken token=default,
        OperationPotionForecastScope? scope=null) => BuildCore(observation,token,scope) with {Activity=OperationForecastActivity.PotionUse,Fingerprint=observation.Stamp};
    private static OperationForecast BuildCore(PredictorContext context, ModelKey potion, CancellationToken token = default,
        OperationPotionForecastScope? demand = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        token.ThrowIfCancellationRequested();
        demand ??= OperationPotionForecastScope.All;
        if (!demand.IncludeBasicResult) return EmptyPotion(potion);
        var query = PredictorOperationPotionQueries.Query(context, potion);
        token.ThrowIfCancellationRequested();
        var cards = query.Cards.OrderBy(c => c.Group).ThenBy(c => c.Ordinal).Select(c => c.Card).ToImmutableArray();
        ImmutableArray<OperationOutcomeSource> sections = cards.IsEmpty ? [] :
            [new(potion, [], [new(0, cards, query.RequiresCardChoice ? 1 : cards.Length,
                query.RequiresCardChoice ? 1 : cards.Length, false, false) { IconPaths = [new([[potion]])] }])
                { Kind = "PotionCombatUse", FeatureId = PotionFeature(potion) }];
        return new(potion, [], [], false,
            [new([], sections, query.MissingInputs, 1, query.IdentitiesComputed)], [])
        {
            // The target capture is independent of room/scene availability.
            // No combat insertion, native potion use or lifecycle participates.
            Activity = OperationForecastActivity.PotionUse
        };
    }

    private static OperationForecast BuildCore(PredictorOperationSnapshot snapshot, ModelKey potion, int slot,
        CancellationToken token = default, OperationPotionForecastScope? demand = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        demand ??= OperationPotionForecastScope.All;
        if (slot < 0 || slot >= snapshot.State.PotionSlots.Length ||
            snapshot.State.PotionSlots[slot] is not { } actual || actual.Key != potion)
            throw new ArgumentException("PotionPreviewInstanceMismatch", nameof(slot));
        if (PredictorOperationPotionQueries.Supports(potion))
            return Build(snapshot.Context, potion, token, demand);
        if (potion.Entry != "ENTROPIC_BREW") return new(potion, [], [], false, [], []);
        if (!demand.IncludeBasicResult && !demand.IncludeInventory && !demand.IncludeOrder) return EmptyPotion(potion);
        try
        {
            var stream = snapshot.Context.NonCombatStream(PredictorStream.CombatPotionGeneration);
            var cursor = snapshot.State.Streams.Single(s => s.Stream == stream);
            return BuildCore(new PredictorOperationPotionObservation(snapshot.Context, new(actual, slot), snapshot.State.PotionSlots,
                cursor, false, PredictorSettlementEffects.Has(snapshot.State, "SOZU"), snapshot.Fingerprint), token, demand);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return EmptyPotion(potion) with
            { Faults = [new("EntropicCurrentSlots", [new UseOutOfCombatPotion(slot, actual.Id)], error)] };
        }
    }

    private static string PotionFeature(ModelKey potion) => "potion:" + potion.Entry;
    private static OperationForecast EmptyPotion(ModelKey potion) => new(potion, [], [], false, [], []);
}
