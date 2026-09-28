using Godot;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Capabilities;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal sealed partial class CapabilityStatePanel : PanelContainer
{
    private readonly Label _symbol;
    private readonly Label _title;
    private readonly Label _message;
    private Ui1CapabilityModel? _model;
    private bool _compact;

    public CapabilityStatePanel(bool compact = false)
    {
        _compact = compact;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ApplySurface();

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", compact ? 10 : 14);
        _symbol = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted);
        _symbol.CustomMinimumSize = new Vector2(compact ? 28 : 42, compact ? 28 : 42);
        _symbol.HorizontalAlignment = HorizontalAlignment.Center;
        _title = Ui1Theme.Label(string.Empty, compact ? Ui1TextRole.CardTitle : Ui1TextRole.SectionTitle);
        _message = Ui1Theme.Label(string.Empty, compact ? Ui1TextRole.Muted : Ui1TextRole.Meta, true);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", compact ? 2 : 5);
        text.AddChild(_title);
        text.AddChild(_message);
        row.AddChild(_symbol);
        row.AddChild(text);
        AddChild(row);
    }

    public void Bind(Ui1CapabilityModel model, IUiTextProvider uiText)
    {
        _model = model;
        _symbol.Text = Ui1CapabilityCatalog.BadgeSymbol(model.State);
        _title.Text = uiText.Get(model.TitleKey);
        _message.Text = uiText.Get(model.MessageKey);
        ApplyStateColors(model.State);
        TooltipText = _message.Text;
    }

    public void SetCompact(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }
        _compact = compact;
        ApplySurface();
    }

    private void ApplySurface()
    {
        Ui1CapabilityState state = _model?.State ?? Ui1CapabilityState.Unknown;
        Ui1SurfaceRole role = state switch
        {
            Ui1CapabilityState.Error => Ui1SurfaceRole.Error,
            Ui1CapabilityState.NotMigrated => Ui1SurfaceRole.NotMigrated,
            _ => Ui1SurfaceRole.Input
        };
        Ui1Theme.ApplyPanel(this, role, 4f, 1, _compact ? 9f : 14f);
    }

    private void ApplyStateColors(Ui1CapabilityState state)
    {
        Color color = state switch
        {
            Ui1CapabilityState.Supported => Ui1Theme.Palette.Success,
            Ui1CapabilityState.Partial => Ui1Theme.Palette.Partial,
            Ui1CapabilityState.Unknown => Ui1Theme.Palette.Unknown,
            Ui1CapabilityState.UnsupportedProfile => Ui1Theme.Palette.Unsupported,
            Ui1CapabilityState.Error => Ui1Theme.Palette.Error,
            _ => Ui1Theme.Palette.NotMigrated
        };
        _symbol.AddThemeColorOverride("font_color", color);
        _title.AddThemeColorOverride("font_color", state == Ui1CapabilityState.Supported
            ? Ui1Theme.Palette.TextPrimary
            : color);
        ApplySurface();
    }
}
