using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Infrastructure.Snapshots;
using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Settings;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal static class HextechShopForgeOperationPreview
{
    internal const string FeatureId = "shop:hextech-forge";

    internal static OperationPreview Build(HextechShopForgeSnapshot snapshot, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = HextechShopForgePrediction.Predict(snapshot);
        token.ThrowIfCancellationRequested();
        if (result.Options.IsEmpty)
            return new(snapshot.Source, [], [], false, [], []) { Notices = ["UiText:operation.hextechForge.noOptions"] };
        var row = result.DirectGrant is { } granted
            ? new OperationPreviewRow(OperationPreviewAction.ObtainRelic, [granted], [], false)
            : new OperationPreviewRow(OperationPreviewAction.Choose, result.Options, [], false);
        var section = new OperationPreviewSection(snapshot.Source, [row], [])
        {
            FeatureId = FeatureId, Category = OperationPredictionCategory.RelicEffect,
            Label = "UiText:operation.hextechForge.purchase1"
        };
        var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
        sections.Add(section);
        foreach (var upgrade in result.Upgrades)
        {
            token.ThrowIfCancellationRequested();
            var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
            var confirmed = upgrade.Targets.Where(target => target.After != null)
                .Select(target => target.After!).ToImmutableArray();
            if (!confirmed.IsEmpty)
                rows.Add(new(OperationPreviewAction.UpgradeCard, [], confirmed, false)
                { Label = upgrade.GrantCount > 1 && upgrade.Issue == null
                    ? "UiText:operation.hextechForge.withDoubleVision" : null });
            var unverified = upgrade.Targets.Where(target => target.After == null)
                .Select(target => target.Before).ToImmutableArray();
            if (!unverified.IsEmpty)
                rows.Add(new(OperationPreviewAction.UpgradeCard, [], unverified, false)
                { ShowCards = false, Label = UpgradeIssueLabel(upgrade.Issue) });
            sections.Add(new(upgrade.Forge, rows.ToImmutable(), [])
            {
                FeatureId = FeatureId, Category = OperationPredictionCategory.RelicEffect,
                Label = result.DirectGrant == null ? "UiText:operation.hextechForge.ifChosen" : null,
                EmptyReason = upgrade.Targets.IsEmpty && upgrade.Issue == null ? "UiText:operation.noUpgradeTargets" :
                    upgrade.Issue != null && unverified.IsEmpty ? UpgradeIssueLabel(upgrade.Issue) : null
            });
        }
        for (int index = 1; index < result.Purchases.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var purchase = result.Purchases[index];
            var futureRow = purchase.DirectGrant is { } direct
                ? new OperationPreviewRow(OperationPreviewAction.ObtainRelic, [direct], [], false)
                : new OperationPreviewRow(OperationPreviewAction.Choose, purchase.Options, [], false);
            sections.Add(new(snapshot.Source, [futureRow], [])
            {
                FeatureId = FeatureId, Category = OperationPredictionCategory.RelicEffect,
                Label = "UiText:operation.hextechForge.purchase" + (index + 1)
            });
        }
        var notices = ImmutableArray.CreateBuilder<string>();
        if (result.Purchases.Length > 1) notices.Add("UiText:operation.hextechForge.continuousPurchases");
        if (result.ContinuationIssue is { } issue)
            notices.Add(issue switch
            {
                "HextechForgeChoiceDivergence" => "UiText:operation.hextechForge.choiceDivergence",
                "HextechForgeNoMoreOptions" => "UiText:operation.hextechForge.noMoreOptions",
                _ => "UiText:operation.hextechForge.continuationUnsupported"
            });
        return new(snapshot.Source, [], [], false,
            [new([], sections.ToImmutable(), [], 1, result.Upgrades.All(upgrade => upgrade.Issue == null))], [])
        { Style = OperationDisplayPolicy.A1, Notices = notices.ToImmutable() };
    }

    private static string UpgradeIssueLabel(string? issue) => issue switch
    {
        "HextechForgeUpgradeCaptureUnavailable" => "UiText:operation.hextechForge.upgradeCaptureUnavailable",
        "HextechForgeUpgradeCardNotAudited" => "UiText:operation.hextechForge.upgradeNotSupported",
        "HextechForgeUpgradeCardUnavailable" => "UiText:operation.hextechForge.upgradeResultUnavailable",
        "HextechForgeUpgradeContinuationUnknown" => "UiText:operation.hextechForge.upgradeContinuationUnknown",
        _ => "UiText:operation.hextechForge.upgradeUnverified"
    };
}
