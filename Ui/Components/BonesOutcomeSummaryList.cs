using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Ordinary Bones presentation. It consumes the pre-grouped immutable view model
/// and never decides order impact or recomputes route equivalence.
/// </summary>
internal sealed partial class BonesOutcomeSummaryList : VBoxContainer
{
    private readonly IGameIconResolver _icons;
    private readonly Label _summary;

    public BonesOutcomeSummaryList(IGameIconResolver icons)
    {
        _icons = icons;
        AddThemeConstantOverride("separation", 8);
        _summary = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _summary.Visible = false;
        AddChild(_summary);
        Visible = false;
    }

    public void Bind(BonesOutcomeViewModel? model, string missingIconTooltip, bool showAll = false)
    {
        for (int i = GetChildCount() - 1; i >= 1; i--)
        {
            GetChild(i).QueueFree();
        }

        Visible = model is not null && model.OutcomeGroups.Count > 0;
        _summary.Visible = Visible;
        if (!Visible || model is null)
        {
            return;
        }

        _summary.Text = $"{model.ResultsCountLabel} · {model.OrderComparisonLabel}";
        _summary.AddThemeColorOverride("font_color", OrderComparisonColor(model.OrderComparisonStatus));

        foreach (BonesOutcomeGroupViewModel group in model.OutcomeGroups)
        {
            AddChild(CreateGroup(group, missingIconTooltip, showAll));
        }
    }

    private Control CreateGroup(BonesOutcomeGroupViewModel group, string missingIconTooltip, bool showAll)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(
            panel,
            group.RouteProjectionStatus == RouteProjectionStatus.Exact
                ? Ui1SurfaceRole.CardElevated
                : Ui1SurfaceRole.Warning,
            4f,
            1,
            10f);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);

        Label title = Ui1Theme.Label(group.Title, Ui1TextRole.CardTitle, true);
        column.AddChild(title);

        Label impact = Ui1Theme.Label(group.RouteProjectionLabel, Ui1TextRole.Meta, true);
        impact.AddThemeColorOverride("font_color", RouteProjectionColor(group.RouteProjectionStatus));
        column.AddChild(impact);

        if (group.AcquisitionOrderLabels.Count > 1)
        {
            Label orders = Ui1Theme.Label(string.Join("  /  ", group.AcquisitionOrderLabels), Ui1TextRole.Muted, true);
            column.AddChild(orders);
        }

        foreach (BonesRelicScopedResultViewModel relic in group.RelicResults)
        {
            column.AddChild(CreateRelicResult(relic, missingIconTooltip, showAll));
        }

        var continuation = new PredictedEffectSummaryList(_icons);
        continuation.Bind(group.SharedContinuationGroups, missingIconTooltip, showAll);
        column.AddChild(continuation);

        if (group.RouteWarningLabels.Count > 0)
        {
            Label reason = Ui1Theme.Label("△ " + group.RouteWarningLabels[0], Ui1TextRole.Warning, true);
            column.AddChild(reason);
        }

        panel.AddChild(column);
        return panel;
    }

    private Control CreateRelicResult(BonesRelicScopedResultViewModel relic, string missingIconTooltip, bool showAll)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);

        IconDescriptor descriptor = _icons.Resolve(
            relic.RelicDisplay.ModelKey,
            relic.RelicDisplay.ContentKind,
            IconVariant.Small);
        var icon = new IconWithLabel(34f, Ui1TextRole.Meta);
        icon.Bind(
            descriptor,
            relic.RelicDisplay.DisplayName,
            relic.RelicDisplay.Tooltip,
            missingIconTooltip);
        column.AddChild(icon);

        if (relic.EffectGroups.Count == 0)
        {
            return column;
        }

        var effects = new PredictedEffectSummaryList(_icons);
        effects.Bind(relic.EffectGroups, missingIconTooltip, showAll);
        column.AddChild(effects);
        return column;
    }

    private static Color OrderComparisonColor(OrderComparisonStatus status) => status switch
    {
        OrderComparisonStatus.ProvenIndependent => Ui1Theme.Palette.Success,
        OrderComparisonStatus.ProvenSensitive => Ui1Theme.Palette.Partial,
        _ => Ui1Theme.Palette.Unknown
    };

    private static Color RouteProjectionColor(RouteProjectionStatus status) => status switch
    {
        RouteProjectionStatus.Exact => Ui1Theme.Palette.Success,
        RouteProjectionStatus.Partial or RouteProjectionStatus.NotEvaluatedByPolicy => Ui1Theme.Palette.Partial,
        _ => Ui1Theme.Palette.Unknown
    };
}
