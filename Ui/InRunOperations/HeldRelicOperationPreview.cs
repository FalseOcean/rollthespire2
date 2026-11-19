using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

internal static class HeldRelicOperationPreview
{
    internal static OperationPreview Build(ModelKey source, HeldRelicOperationObservation observation)
    {
        var result = HeldRelicOperationForecast.Calculate(observation);
        if (result.Cards.IsEmpty && !result.NoUpgradeTarget) return new(source, [], [], false, [], []);

        bool returnCard = source.Entry == "PAELS_TOOTH";
        string label = returnCard ? "HeldRelicNextReturn" : result.ThisBattle ? "HeldRelicThisVictory" :
            source.Entry == "WAR_HAMMER" ? "HeldRelicEliteVictory" : "HeldRelicFishingWait:" + result.OrdinaryBattlesRemaining;
        var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
        if (!result.Cards.IsEmpty)
            rows.Add(new(returnCard ? OperationPreviewAction.ReturnCard : OperationPreviewAction.UpgradeCard,
                [], result.Cards, false) { ShowCards = false, Label = label });
        if (result.FinalReturn is { } final)
            rows.Add(new(OperationPreviewAction.ReturnCard, [], [final], false) { ShowCards = false, Label = "HeldRelicLastReturn" });
        var section = new OperationPreviewSection(source, rows.ToImmutable(), []);
        if (result.NoUpgradeTarget)
            section = section with { Label = label, EmptyReason = "HeldRelicNoUpgradeTarget" };
        return new(source, [], [], false, [new([], [section], [], 1, true)], [])
        {
            Style = new(OperationTextMode.CardRows, OperationCardLayout.Parallel)
        };
    }
}
