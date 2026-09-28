using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Compact rendering of one already-presented effect item. It does not infer
/// content kind, icon identity, selection policy, or precision.
/// </summary>
internal sealed partial class CompactPredictionItem : PanelContainer
{
    public CompactPredictionItem(
        PredictedEffectItemViewModel item,
        IGameIconResolver icons,
        string missingIconTooltip,
        bool showFullText)
    {
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        CustomMinimumSize = new Vector2(150f, 44f);
        TooltipText = item.Tooltip;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 6f);

        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        string visibleText = showFullText ? item.DisplayText : item.CompactDisplayText;
        if (item.TargetContent is { } targetContent)
        {
            IconDescriptor descriptor = icons.Resolve(
                targetContent.ModelKey,
                targetContent.ContentKind,
                IconVariant.Small);
            var icon = new IconWithLabel(34f, Ui1TextRole.Meta)
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            icon.Bind(descriptor, visibleText, item.Tooltip, missingIconTooltip);
            row.AddChild(icon);
        }
        else
        {
            Label text = Ui1Theme.Label(visibleText, Ui1TextRole.Meta, true);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
        }

        if (item.Precision != PredictionPrecision.Exact)
        {
            Label precision = Ui1Theme.Label(
                PrecisionSymbol(item.Precision),
                Ui1TextRole.Meta);
            precision.HorizontalAlignment = HorizontalAlignment.Right;
            precision.AddThemeColorOverride("font_color", PrecisionColor(item.Precision));
            precision.TooltipText = item.PrecisionLabel;
            row.AddChild(precision);
        }

        AddChild(row);
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
