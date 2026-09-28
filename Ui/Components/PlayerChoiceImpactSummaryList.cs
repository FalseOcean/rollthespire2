using Godot;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Dedicated presentation for player-choice impact. It consumes already
/// deduplicated product outcomes and never enumerates raw card instances,
/// computes downstream effects, or decides Search match semantics.
/// </summary>
internal sealed partial class PlayerChoiceImpactSummaryList : VBoxContainer
{
    private readonly IGameIconResolver _icons;

    public PlayerChoiceImpactSummaryList(IGameIconResolver icons)
    {
        _icons = icons;
        AddThemeConstantOverride("separation", 8);
        Visible = false;
    }

    public void Bind(PlayerChoiceImpactViewModel? model, string missingIconTooltip)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        Visible = model is not null && model.RouteSets.Count > 0;
        if (!Visible || model is null)
        {
            return;
        }

        Label title = Ui1Theme.Label(model.Title, Ui1TextRole.SectionTitle, true);
        AddChild(title);

        foreach (PlayerChoiceRouteSetViewModel routeSet in model.RouteSets)
        {
            AddChild(CreateRouteSet(routeSet, missingIconTooltip));
        }
    }

    private Control CreateRouteSet(PlayerChoiceRouteSetViewModel routeSet, string missingIconTooltip)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.CardElevated, 4f, 1, 10f);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 7);
        column.AddChild(Ui1Theme.Label(routeSet.AcquisitionOrderLabel, Ui1TextRole.CardTitle, true));

        for (int index = 0; index < routeSet.DistinctOutcomes.Count; index++)
        {
            PlayerChoiceOutcomeViewModel outcome = routeSet.DistinctOutcomes[index];
            var outcomePanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(outcomePanel, Ui1SurfaceRole.Card, 3f, 1, 8f);
            var outcomeColumn = new VBoxContainer();
            outcomeColumn.AddThemeConstantOverride("separation", 5);
            var choices = new PredictedEffectSummaryList(_icons);
            choices.Bind(outcome.ChoiceGroups, missingIconTooltip, showAll: true);
            outcomeColumn.AddChild(choices);

            var automatic = new PredictedEffectSummaryList(_icons);
            automatic.Bind(outcome.AutomaticEffectGroups, missingIconTooltip, showAll: true);
            outcomeColumn.AddChild(automatic);

            if (outcome.FinalCurse is { } finalCurse)
            {
                IconDescriptor descriptor = _icons.Resolve(
                    finalCurse.Content.ModelKey,
                    finalCurse.Content.ContentKind,
                    IconVariant.Small);
                var curseIcon = new IconWithLabel(34f, Ui1TextRole.Meta);
                curseIcon.Bind(
                    descriptor,
                    finalCurse.Content.DisplayName,
                    finalCurse.Content.Tooltip,
                    missingIconTooltip);
                outcomeColumn.AddChild(curseIcon);
            }

            outcomePanel.AddChild(outcomeColumn);
            column.AddChild(outcomePanel);
        }

        panel.AddChild(column);
        return panel;
    }
}
