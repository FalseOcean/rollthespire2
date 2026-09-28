using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Relic;

internal sealed partial class RelicSequencePickerSlot : Button
{
    private const float SlotWidth = 196f;
    private const float SlotHeight = 60f;
    private const float IconSize = 42f;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly TextureRect _icon;
    private readonly Label _missing;
    private readonly Label _name;
    private IGameContentNameResolver? _names;
    private ModelKey? _selectedKey;
    private string _emptyText = string.Empty;

    public RelicSequencePickerSlot(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons;
        _tooltipHost = tooltipHost;
        CustomMinimumSize = new Vector2(SlotWidth, SlotHeight);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
        Ui1Theme.ApplyButton(this, Ui1ButtonRole.Secondary);

        var margin = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 8);

        var iconHost = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            ClipContents = true
        };
        _icon = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _missing = Ui1Theme.Label("＋", Ui1TextRole.Accent);
        _missing.MouseFilter = MouseFilterEnum.Ignore;
        _missing.HorizontalAlignment = HorizontalAlignment.Center;
        _missing.VerticalAlignment = VerticalAlignment.Center;
        _missing.AddThemeFontSizeOverride("font_size", 23);
        _missing.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        iconHost.AddChild(_icon);
        iconHost.AddChild(_missing);

        _name = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _name.MouseFilter = MouseFilterEnum.Ignore;
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;

        row.AddChild(iconHost);
        row.AddChild(_name);
        margin.AddChild(row);
        AddChild(margin);

        MouseEntered += ShowTooltip;
        MouseExited += () => _tooltipHost.Dismiss(this);
        TreeExiting += () => _tooltipHost.Dismiss(this);
        RefreshVisual();
    }

    public event Action? SelectionChanged;

    public ModelKey? SelectedKey => _selectedKey;

    public void BindText(IGameContentNameResolver names, string emptyText)
    {
        _names = names;
        _emptyText = emptyText;
        RefreshVisual();
    }

    public void SetSelection(ModelKey? key, bool notify)
    {
        _tooltipHost.Dismiss(this);
        _selectedKey = key is { IsValid: true } ? key : null;
        RefreshVisual();
        if (notify)
        {
            SelectionChanged?.Invoke();
        }
    }

    private void RefreshVisual()
    {
        if (!_selectedKey.HasValue)
        {
            _icon.Texture = null;
            _icon.Visible = false;
            _missing.Text = "＋";
            _missing.Visible = true;
            _name.Text = _emptyText;
            return;
        }

        ModelKey key = _selectedKey.Value;
        string displayName = _names?.Resolve(key, GameContentKind.Relic) ?? key.Entry;
        IconDescriptor descriptor = _icons.Resolve(key, GameContentKind.Relic, IconVariant.Small);
        _icon.Texture = descriptor.Texture;
        _icon.Visible = descriptor.Texture is not null;
        _missing.Text = descriptor.Texture is null ? "?" : string.Empty;
        _missing.Visible = descriptor.Texture is null;
        _name.Text = displayName;
    }

    private void ShowTooltip()
    {
        if (!_selectedKey.HasValue)
        {
            return;
        }

        ModelKey key = _selectedKey.Value;
        string displayName = _names?.Resolve(key, GameContentKind.Relic) ?? key.Entry;
        _tooltipHost.ShowFor(this, key, displayName);
    }
}
