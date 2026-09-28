using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Renders one immutable ordered effect group as a compact horizontal offer
/// strip. It preserves original order and never merges, sorts, or deduplicates.
/// </summary>
internal sealed partial class HorizontalOfferGroup : VBoxContainer
{
    public HorizontalOfferGroup(
        PredictedEffectGroupViewModel group,
        IGameIconResolver icons,
        string missingIconTooltip,
        bool showAll)
    {
        AddThemeConstantOverride("separation", 5);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        PredictedEffectItemViewModel[] visibleItems = group.OrderedItems
            .OrderBy(item => item.ItemOrder)
            .Where(item => showAll || item.ShowInNormalMode)
            .ToArray();
        bool compactNormalSummary = !showAll &&
                                    group.NormalViewDetailLevel == EffectPresentationDetailLevel.CompactSummary &&
                                    !string.IsNullOrWhiteSpace(group.CompactSummaryText);

        if (visibleItems.Length == 0 && !compactNormalSummary)
        {
            Visible = false;
            return;
        }

        string headerText = BuildHeader(group);
        if (!string.IsNullOrWhiteSpace(headerText))
        {
            Label header = Ui1Theme.Label(headerText, Ui1TextRole.Meta, true);
            header.AddThemeColorOverride("font_color", PrecisionColor(group.Precision));
            AddChild(header);
        }

        if (compactNormalSummary)
        {
            AddChild(Ui1Theme.Label(group.CompactSummaryText, Ui1TextRole.Meta, true));
            return;
        }

        bool hasHierarchy = visibleItems.Any(item =>
            !string.IsNullOrWhiteSpace(item.ParentEffectNodeId) ||
            item.Relation != PredictedEffectRelation.Root);
        if (hasHierarchy)
        {
            var hierarchy = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            hierarchy.AddThemeConstantOverride("separation", 5);
            foreach (PredictedEffectItemViewModel item in visibleItems)
            {
                int depth = item.Relation switch
                {
                    PredictedEffectRelation.NestedRelic => 0,
                    PredictedEffectRelation.NestedAutomaticEffect => 1,
                    PredictedEffectRelation.AffectedTarget => 2,
                    _ => 0
                };
                var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                margin.AddThemeConstantOverride("margin_left", depth * 22);
                var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", 5);
                if (depth > 0)
                {
                    row.AddChild(Ui1Theme.Label("↳", Ui1TextRole.Muted));
                }
                row.AddChild(new CompactPredictionItem(
                    item,
                    icons,
                    missingIconTooltip,
                    showFullText: showAll));
                margin.AddChild(row);
                hierarchy.AddChild(margin);
            }
            AddChild(hierarchy);
        }
        else
        {
            var flow = new HFlowContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            flow.AddThemeConstantOverride("h_separation", 8);
            flow.AddThemeConstantOverride("v_separation", 8);

            foreach (PredictedEffectItemViewModel item in visibleItems)
            {
                flow.AddChild(new CompactPredictionItem(
                    item,
                    icons,
                    missingIconTooltip,
                    showFullText: showAll));
            }
            AddChild(flow);
        }
    }

    private static string BuildHeader(PredictedEffectGroupViewModel group)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(group.DisplayLabel))
        {
            parts.Add(group.DisplayLabel);
        }
        if (group.SelectionPolicy != EffectSelectionPolicy.NoPlayerChoice)
        {
            parts.Add(group.SelectionPolicyLabel);
        }
        parts.Add($"{PrecisionSymbol(group.Precision)} {group.PrecisionLabel}");
        return string.Join(" · ", parts);
    }

    private static string PrecisionSymbol(PredictionPrecision precision) => precision switch
    {
        PredictionPrecision.Exact => "✓",
        PredictionPrecision.Partial => "◐",
        PredictionPrecision.DescriptionOnly => "◇",
        PredictionPrecision.Unsupported => "⊘",
        _ => "?"
    };

    private static Color PrecisionColor(PredictionPrecision precision) => precision switch
    {
        PredictionPrecision.Exact => Ui1Theme.Palette.Success,
        PredictionPrecision.Partial => Ui1Theme.Palette.Partial,
        PredictionPrecision.Unsupported => Ui1Theme.Palette.Unsupported,
        PredictionPrecision.Unknown => Ui1Theme.Palette.Unknown,
        _ => Ui1Theme.Palette.TextSecondary
    };
}
