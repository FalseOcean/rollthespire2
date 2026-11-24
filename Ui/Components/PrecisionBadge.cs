using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class PrecisionBadge : PanelContainer
{
    private readonly Label _label;

    public PrecisionBadge()
    {
        CustomMinimumSize = new Vector2(0, 25);
        _label = MainPanelTheme.Label(string.Empty, Ui1TextRole.Meta);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_label);
    }

    public void Bind(
        string dimension,
        string value,
        PredictionPrecision precision,
        bool notMigrated = false,
        bool compact = false)
    {
        string symbol = Symbol(precision, notMigrated);
        _label.Text = compact
            ? $"{dimension} {symbol}"
            : $"{symbol} {dimension}: {value}";
        Color color = notMigrated
            ? MainPanelTheme.Palette.TextMuted
            : precision switch
            {
                PredictionPrecision.Exact => MainPanelTheme.Palette.Success,
                PredictionPrecision.Partial => MainPanelTheme.Palette.Partial,
                PredictionPrecision.Unsupported => MainPanelTheme.Palette.Unsupported,
                PredictionPrecision.Unknown => MainPanelTheme.Palette.Unknown,
                _ => MainPanelTheme.Palette.TextSecondary
            };
        var box = MainPanelTheme.Surface(Ui1SurfaceRole.Input, 3f, 1);
        box.BorderColor = notMigrated ? MainPanelTheme.Palette.BorderSubtle : color;
        MainPanelTheme.SetMargins(box, compact ? 6f : 8f, 3f, compact ? 6f : 8f, 3f);
        AddThemeStyleboxOverride("panel", box);
        _label.AddThemeColorOverride("font_color", color);
        TooltipText = $"{dimension}: {value}";
    }

    private static string Symbol(PredictionPrecision precision, bool notMigrated) => notMigrated
        ? "◇"
        : precision switch
        {
            PredictionPrecision.Exact => "✓",
            PredictionPrecision.Partial => "◐",
            PredictionPrecision.Unsupported => "⊘",
            PredictionPrecision.Unknown => "?",
            _ => "—"
        };
}
