using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// Shared fixed-height horizontal result-slot chrome for Neow Search.
/// The selected ModelKey remains the only business state; texture, text and
/// clear-button placement are main-thread presentation details.
/// </summary>
internal abstract partial class SearchHorizontalResultSlot : VBoxContainer, INeowResultSlot
{
    internal const float SlotWidth = 196f;
    internal const float SlotHeight = 64f;
    internal const float IconSize = 40f;
    internal const float ClearButtonSize = 24f;
    private const float ClearInset = 8f;

    private readonly IGameIconResolver _icons;
    private readonly Action<RelicPickerRequest> _openPicker;
    private readonly Label _slotLabel;
    private readonly Control _slotHost;
    private readonly Control _iconHost;
    private readonly Button _body;
    private readonly TextureRect _texture;
    private readonly Label _missing;
    private readonly Label _name;
    private readonly Button _clear;
    private HBoxContainer? _inlineLabelRow;
    private IReadOnlyList<ModelKey> _candidates = Array.Empty<ModelKey>();
    private IReadOnlyDictionary<ModelKey, RelicPickerCategory> _categories =
        new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance);
    private HashSet<ModelKey> _disabledKeys = new(ModelKeyComparer.Instance);
    private IGameContentNameResolver? _names;
    private GameContentKind _kind;
    private IconVariant _variant;
    private string _emptyText = string.Empty;
    private string _pickerTitle = string.Empty;
    private ModelKey? _selectedKey;
    private CardPickerContext? _cardPickerContext;
    private bool _enabled = true;

    protected SearchHorizontalResultSlot(
        IGameIconResolver icons,
        Action<RelicPickerRequest> openPicker)
    {
        _icons = icons;
        _openPicker = openPicker;
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        AddThemeConstantOverride("separation", 4);

        _slotLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _slotLabel.Visible = false;
        AddChild(_slotLabel);

        _slotHost = new Control
        {
            CustomMinimumSize = new Vector2(SlotWidth, SlotHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            ClipContents = true
        };

        _body = new Button
        {
            FocusMode = Control.FocusModeEnum.All,
            ClipContents = true
        };
        _body.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Ui1Theme.ApplyButton(_body, Ui1ButtonRole.Secondary);
        _body.Pressed += OpenPicker;
        _slotHost.AddChild(_body);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        // The clear button overlays the upper-right corner. Reserve only its
        // actual footprint plus a small gap so selected names retain room.
        margin.AddThemeConstantOverride("margin_right", 34);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 8);

        _iconHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ClipContents = true
        };
        _texture = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        _texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _missing = Ui1Theme.Label("＋", Ui1TextRole.Accent);
        _missing.MouseFilter = Control.MouseFilterEnum.Ignore;
        _missing.HorizontalAlignment = HorizontalAlignment.Center;
        _missing.VerticalAlignment = VerticalAlignment.Center;
        _missing.AddThemeFontSizeOverride("font_size", 24);
        _missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _iconHost.AddChild(_texture);
        _iconHost.AddChild(_missing);

        _name = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _name.MouseFilter = Control.MouseFilterEnum.Ignore;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _name.ClipText = true;
        row.AddChild(_iconHost);
        row.AddChild(_name);
        margin.AddChild(row);
        _slotHost.AddChild(margin);

        _clear = new Button
        {
            Text = "×",
            Visible = false,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(ClearButtonSize, ClearButtonSize),
            ZIndex = 3
        };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Secondary);
        _clear.AddThemeFontSizeOverride("font_size", 14);
        _clear.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _clear.OffsetLeft = -(ClearInset + ClearButtonSize);
        _clear.OffsetTop = ClearInset;
        _clear.OffsetRight = -ClearInset;
        _clear.OffsetBottom = ClearInset + ClearButtonSize;
        _clear.Pressed += () => Clear(notify: true);
        _slotHost.AddChild(_clear);

        AddChild(_slotHost);
        RefreshVisual();
    }

    public Control View => this;
    public event Action? Changed;
    public ModelKey? SelectedKey => _selectedKey;
    public bool IsEmpty => !_selectedKey.HasValue;

    public void Configure(
        string label,
        IReadOnlyList<ModelKey> candidates,
        GameContentKind kind,
        IconVariant variant,
        IGameContentNameResolver names,
        string emptyText,
        string tooltip,
        IReadOnlyDictionary<ModelKey, RelicPickerCategory>? categories = null,
        string pickerTitle = "",
        CardPickerContext? cardPickerContext = null)
    {
        _slotLabel.Text = label;
        _slotLabel.Visible = !string.IsNullOrWhiteSpace(label);
        _candidates = candidates;
        _kind = kind;
        _variant = variant;
        _names = names;
        _emptyText = emptyText;
        _pickerTitle = pickerTitle;
        _cardPickerContext = cardPickerContext;
        _categories = categories ?? new Dictionary<ModelKey, RelicPickerCategory>(ModelKeyComparer.Instance);
        TooltipText = tooltip;
        RefreshVisual();
    }

    public void SetDisabledKeys(IEnumerable<ModelKey> keys) =>
        _disabledKeys = new HashSet<ModelKey>(keys, ModelKeyComparer.Instance);

    public void Clear(bool notify)
    {
        if (!_selectedKey.HasValue) return;
        _selectedKey = null;
        RefreshVisual();
        if (notify) Changed?.Invoke();
    }

    public void Select(ModelKey? key, bool notify)
    {
        ModelKey? normalized = key is { IsValid: true } ? key : null;
        if (_selectedKey == normalized)
        {
            RefreshVisual();
            return;
        }
        _selectedKey = normalized;
        RefreshVisual();
        if (notify) Changed?.Invoke();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _body.Disabled = !enabled;
        _clear.Disabled = !enabled;
    }

    /// <summary>Allow a page-local result slot to fill its card column without changing the shared default geometry.</summary>
    public void UseExpandedWidth(float minimumWidth = SlotWidth)
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _slotHost.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _slotHost.CustomMinimumSize = new Vector2(minimumWidth, SlotHeight);
    }

    /// <summary>Places the slot's localized label beside the result control.</summary>
    public void UseInlineLabel(float labelMinimumWidth = 100f)
    {
        if (_inlineLabelRow is not null) return;
        RemoveChild(_slotLabel);
        RemoveChild(_slotHost);
        _inlineLabelRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        _inlineLabelRow.AddThemeConstantOverride("separation", 6);
        _slotLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _slotLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _slotLabel.CustomMinimumSize = new Vector2(labelMinimumWidth, SlotHeight);
        _slotLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _slotLabel.ClipText = true;
        _slotLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _slotLabel.VerticalAlignment = VerticalAlignment.Center;
        _inlineLabelRow.AddChild(_slotLabel);
        _inlineLabelRow.AddChild(_slotHost);
        AddChild(_inlineLabelRow);
    }

    public void UseCompactInlineWidth(float width = SlotWidth)
    {
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _slotHost.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _slotHost.CustomMinimumSize = new Vector2(width, SlotHeight);
    }

    /// <summary>Compact square presentation for icon-only relic queues.</summary>
    public void UseCompactIconOnly(float size = 58f, float iconSize = 42f)
    {
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _slotHost.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        _slotHost.CustomMinimumSize = new Vector2(size, size);
        _iconHost.CustomMinimumSize = new Vector2(iconSize, iconSize);
        _name.Visible = false;
        Control? margin = _slotHost.GetChildOrNull<Control>(1);
        if (margin is MarginContainer compactMargin)
        {
            compactMargin.AddThemeConstantOverride("margin_left", 4);
            compactMargin.AddThemeConstantOverride("margin_top", 4);
            compactMargin.AddThemeConstantOverride("margin_right", 4);
            compactMargin.AddThemeConstantOverride("margin_bottom", 4);
        }
    }

    /// <summary>Page-local larger presentation for wide Combat Reward card slots.</summary>
    public void UseLargeVisual(float slotHeight = 88f, float iconSize = 60f)
    {
        _slotHost.CustomMinimumSize = new Vector2(SlotWidth, slotHeight);
        _iconHost.CustomMinimumSize = new Vector2(iconSize, iconSize);
    }

    public void GrabSlotFocus() => _body.GrabFocus();

    private void OpenPicker()
    {
        if (!_enabled || _names is null || _candidates.Count == 0) return;
        Dictionary<ModelKey, RelicPickerCategory> categories = _candidates
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToDictionary(
                key => key,
                key => _categories.TryGetValue(key, out RelicPickerCategory category)
                    ? category
                    : RelicPickerCategory.Other,
                ModelKeyComparer.Instance);
        _openPicker(new RelicPickerRequest(
            string.IsNullOrWhiteSpace(_pickerTitle) ? _emptyText : _pickerTitle,
            _candidates,
            _selectedKey,
            _disabledKeys,
            categories,
            AllowClear: true,
            _kind,
            _kind == GameContentKind.Card ? IconVariant.CardPickerLarge : _variant,
            key => Select(key, notify: true))
        {
            CardContext = _kind == GameContentKind.Card ? _cardPickerContext : null
        });
    }

    private void RefreshVisual()
    {
        if (!_selectedKey.HasValue || _names is null)
        {
            _texture.Texture = null;
            _texture.Visible = false;
            _missing.Text = "＋";
            _missing.Visible = true;
            _name.Text = _emptyText;
            _name.TooltipText = _emptyText;
            _body.TooltipText = string.IsNullOrWhiteSpace(TooltipText) ? _emptyText : TooltipText;
            _clear.Visible = false;
            return;
        }

        ModelKey key = _selectedKey.Value;
        IconDescriptor descriptor = _icons.Resolve(key, _kind, _variant);
        string displayName = _names.Resolve(key, _kind);
        _texture.Texture = descriptor.Texture;
        _texture.Visible = descriptor.Texture is not null;
        _missing.Text = descriptor.Texture is null ? "?" : string.Empty;
        _missing.Visible = descriptor.Texture is null;
        _name.Text = displayName;
        _name.TooltipText = displayName;
        _body.TooltipText = displayName;
        _clear.Visible = true;
    }
}
