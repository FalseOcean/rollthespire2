using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorOperationAncientScene(ModelKey Ancient,
    ImmutableArray<PredictorAncientOffer> Offers) : PredictorOperationScene
{
    internal override void Restore(PredictorRun run) => run.RestoreOperationAncient(this);
}

internal sealed partial class PredictorRun
{
    internal void RestoreOperationAncient(PredictorOperationAncientScene scene)
    {
        RequireEmptyOperationScene();
        if (Context.Operation == null || !scene.Ancient.IsValid || scene.Offers.IsDefault ||
            scene.Offers.Any(o => o == null || o.Id < 1 || o.Id >= Working.NextInstanceId ||
                !o.Relic.IsValid || o.Relic.Category != "RELIC" ||
                o.StarterCardId is { } card && !Working.Deck.Contains(card) ||
                o.StarterRelicId is { } relic && !Working.Relics.Any(r => r.Id == relic)) ||
            scene.Offers.Select(o => o.Id).Distinct().Count() != scene.Offers.Length)
            throw new InvalidDataException("PredictorOperationAncientSceneInvalid");
        CurrentAncient = scene.Ancient;
        AncientOffers = scene.Offers;
        Phase = PredictorPhase.AncientChoice;
        _requestId++;
    }
}
