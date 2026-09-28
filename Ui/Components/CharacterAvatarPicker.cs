using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

internal enum CharacterAvatarPickerDensity
{
    Standard,
    Compact
}

internal sealed partial class CharacterAvatarPicker : HBoxContainer
{
    private readonly IReadOnlyList<ModelKey> _keys;
    private readonly CharacterAvatarPickerDensity _density;
    private readonly Dictionary<ModelKey, Button> _buttons = new();
    private readonly Dictionary<ModelKey, Label> _selectionBadges = new();
    private ModelKey _selectedKey;

    public CharacterAvatarPicker(
        IReadOnlyList<ModelKey> keys,
        CharacterAvatarPickerDensity density = CharacterAvatarPickerDensity.Standard)
    {
        _keys = keys;
        _density = density;
        AddThemeConstantOverride("separation", density == CharacterAvatarPickerDensity.Compact ? 6 : 9);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
    }

    public event Action<ModelKey>? SelectionChanged;

    public ModelKey SelectedKey => _selectedKey;

    public void Build(
        ModelKey selectedKey,
        IGameContentNameResolver names,
        IGameIconResolver icons,
        string missingIconTooltip)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();
        _selectionBadges.Clear();
        _selectedKey = selectedKey;

        float portraitSize = _density == CharacterAvatarPickerDensity.Compact ? 46f : 58f;
        float portraitInset = _density == CharacterAvatarPickerDensity.Compact ? 3f : 5f;

        foreach (ModelKey key in _keys)
        {
            string displayName = names.Resolve(key, GameContentKind.Character);
            IconDescriptor descriptor = icons.Resolve(key, GameContentKind.Character, IconVariant.CharacterPortrait);
            var button = new Button
            {
                Text = string.Empty,
                CustomMinimumSize = new Vector2(portraitSize, portraitSize),
                TooltipText = descriptor.IsMissing ? $"{displayName}\n{missingIconTooltip}" : displayName,
                ToggleMode = true,
                ButtonPressed = key == selectedKey,
                FocusMode = Control.FocusModeEnum.All,
                ClipContents = true
            };

            if (descriptor.Texture is not null)
            {
                var texture = new TextureRect
                {
                    Texture = descriptor.Texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                texture.OffsetLeft = portraitInset;
                texture.OffsetTop = portraitInset;
                texture.OffsetRight = -portraitInset;
                texture.OffsetBottom = -portraitInset;
                button.AddChild(texture);
            }
            else
            {
                var fallback = Ui1Theme.Label(ShortName(displayName), Ui1TextRole.CardTitle);
                fallback.HorizontalAlignment = HorizontalAlignment.Center;
                fallback.VerticalAlignment = VerticalAlignment.Center;
                fallback.MouseFilter = Control.MouseFilterEnum.Ignore;
                fallback.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                button.AddChild(fallback);
            }

            var selectedBadge = Ui1Theme.Label("✓", Ui1TextRole.Accent);
            selectedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
            selectedBadge.HorizontalAlignment = HorizontalAlignment.Center;
            selectedBadge.VerticalAlignment = VerticalAlignment.Center;
            selectedBadge.AnchorLeft = 1f;
            selectedBadge.AnchorRight = 1f;
            selectedBadge.AnchorTop = 0f;
            selectedBadge.AnchorBottom = 0f;
            float badgeSize = _density == CharacterAvatarPickerDensity.Compact ? 16f : 20f;
            selectedBadge.OffsetLeft = -(badgeSize + 1f);
            selectedBadge.OffsetTop = 1f;
            selectedBadge.OffsetRight = -1f;
            selectedBadge.OffsetBottom = badgeSize + 1f;
            selectedBadge.Visible = key == selectedKey;
            button.AddChild(selectedBadge);

            ModelKey captured = key;
            button.Pressed += () => Select(captured, notify: true);
            _buttons[key] = button;
            _selectionBadges[key] = selectedBadge;
            AddChild(button);
        }
        RefreshStyles();
    }

    public void Select(ModelKey key, bool notify)
    {
        if (!_buttons.ContainsKey(key))
        {
            _selectedKey = key;
            RefreshStyles();
            return;
        }

        _selectedKey = key;
        foreach ((ModelKey candidate, Button button) in _buttons)
        {
            button.ButtonPressed = candidate == key;
        }
        RefreshStyles();
        if (notify)
        {
            SelectionChanged?.Invoke(key);
        }
    }

    private void RefreshStyles()
    {
        foreach ((ModelKey key, Button button) in _buttons)
        {
            bool selected = key == _selectedKey;
            Ui1Theme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
            if (_selectionBadges.TryGetValue(key, out Label? badge) && badge is not null)
            {
                badge.Visible = selected;
            }
        }
    }

    private static string ShortName(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= 2 ? trimmed : trimmed[..2];
    }
}
