using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation;

namespace RolltheSpire2.Ui.InRunOperations;

internal static class EventOperationPreview
{
    internal static OperationPreview Build(PredictorOperationSnapshot snapshot, string optionKey,
        CancellationToken token = default, bool playRoyalties = false, bool includeContinuation = true,
        bool includeCombat = true)
    {
        var scene = RequireEvent(snapshot);
        if (!scene.Options.Any(o => o.Key == optionKey && o.Enabled))
            throw new ArgumentException("EventPreviewOptionNotInSnapshot", nameof(optionKey));
        return MayDisplay(scene, optionKey)
            ? OperationForecastProjection.Project(EventOperationForecast.Build(snapshot, optionKey, token,
                playRoyalties, includeContinuation, includeCombat))
            : Empty(scene, optionKey);
    }

    internal static OperationPreview BuildSelected(PredictorOperationSnapshot snapshot, ImmutableArray<long> cardIds,
        CancellationToken token = default, bool includeContinuation = true, bool includeCombat = true)
    {
        var scene = RequireEvent(snapshot);
        if (!scene.AwaitingCards || cardIds.IsDefault || cardIds.Length != scene.CardsRequired ||
            cardIds.Distinct().Count() != cardIds.Length || cardIds.Any(id => !scene.CardCandidates.Contains(id)))
            throw new ArgumentException("EventPreviewSelectionNotInSnapshot", nameof(cardIds));
        return MayDisplay(scene, scene.Branch ?? "")
            ? OperationForecastProjection.Project(EventOperationForecast.BuildSelected(snapshot, cardIds, token,
                includeContinuation, includeCombat))
            : Empty(scene, scene.Branch ?? "");
    }

    internal static bool CanToggleRoyalties(PredictorOperationSnapshot snapshot, string optionKey) =>
        snapshot.Scene is PredictorOperationEventScene scene && scene.Options.Any(o => o.Key == optionKey && o.Enabled) &&
        snapshot.State.Deck.Select(snapshot.State.Card).Any(c => c.Key.Entry == "ROYALTIES") &&
        OperationPreviewEligibility.RoyaltiesBattleOption(scene.Event.Entry, optionKey);

    private static PredictorOperationEventScene RequireEvent(PredictorOperationSnapshot snapshot) =>
        snapshot.Scene as PredictorOperationEventScene ?? throw new ArgumentException("EventPreviewRequiresActualEventScene");

    private static bool MayDisplay(PredictorOperationEventScene scene, string option) =>
        OperationPreviewEligibility.EventMayDisplay(scene.Event.Entry, option, scene.TinkerType != null,
            scene.TabletCount, scene.Options.Any(o => o.Key == option && o.OfferedRelic != null));

    private static OperationPreview Empty(PredictorOperationEventScene scene, string option) =>
        new(scene.Event, [], [], false, [], []) { OptionKey = option };
}
