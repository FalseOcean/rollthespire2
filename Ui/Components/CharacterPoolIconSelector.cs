using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Reusable ModelKey-based selector for the native Card Library character-pool
/// icons. Missing or invalid visual authority never removes or renames a
/// character choice.
/// </summary>
internal sealed partial class CharacterPoolIconSelector : HBoxContainer
{
    private const float ButtonSize = 42f;
    private const float ButtonSeparation = 6f;
    private const float IconInset = 3f;
    private const float BadgeSize = 15f;

    private static readonly HashSet<ModelKey> InvalidTextureDiagnostics =
        new(ModelKeyComparer.Instance);

    private readonly IReadOnlyList<ModelKey> _keys;
    private readonly ICharacterPoolIconProvider _icons;
    private readonly Dictionary<ModelKey, Button> _buttons = new(ModelKeyComparer.Instance);
    private readonly Dictionary<ModelKey, Label> _selectionBadges = new(ModelKeyComparer.Instance);
    private ModelKey _selectedKey;

    public CharacterPoolIconSelector(
        IReadOnlyList<ModelKey> keys,
        ICharacterPoolIconProvider icons)
    {
        _keys = keys;
        _icons = icons;
        AddThemeConstantOverride("separation", (int)ButtonSeparation);
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        CustomMinimumSize = new Vector2(CalculateWidth(_keys.Count), ButtonSize);
    }

    public event Action<ModelKey>? SelectionChanged;

    public bool RequiresHorizontalOverflow(int visibleCharacterLimit) =>
        visibleCharacterLimit > 0 && _keys.Count > visibleCharacterLimit;

    public float PreferredViewportWidth(int visibleCharacterLimit)
    {
        int visible = visibleCharacterLimit <= 0
            ? _keys.Count
            : Math.Min(_keys.Count, visibleCharacterLimit);
        return CalculateWidth(visible);
    }

    private static float CalculateWidth(int count) => count <= 0
        ? 0f
        : count * ButtonSize + Math.Max(0, count - 1) * ButtonSeparation;

    public ModelKey SelectedKey => _selectedKey;
    public IReadOnlyList<ModelKey> Keys => _keys;

    public void SetEnabled(bool enabled)
    {
        foreach (Button button in _buttons.Values)
            button.Disabled = !enabled;
    }

    public bool Contains(ModelKey key) => _keys.Contains(key, ModelKeyComparer.Instance);

    public ModelKey ResolveAvailableSelection(ModelKey preferred)
    {
        if (preferred.IsValid && Contains(preferred)) return preferred;
        if (Contains(BaseGameModelKeys.Characters.Silent)) return BaseGameModelKeys.Characters.Silent;
        return _keys.FirstOrDefault(key => key.IsValid);
    }

    public void Build(
        ModelKey selectedKey,
        IGameContentNameResolver names,
        string missingIconTooltip)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();
        _selectionBadges.Clear();
        _selectedKey = ResolveAvailableSelection(selectedKey);

        foreach (ModelKey key in _keys)
        {
            string displayName = names.Resolve(key, GameContentKind.Character);
            IconDescriptor descriptor = _icons.Resolve(key);
            Texture2D? texture = TryGetUsableTexture(key, descriptor.Texture);
            bool missingVisual = descriptor.IsMissing || texture is null;
            var button = new Button
            {
                Text = string.Empty,
                CustomMinimumSize = new Vector2(ButtonSize, ButtonSize),
                TooltipText = missingVisual
                    ? $"{displayName}\n{missingIconTooltip}"
                    : displayName,
                ToggleMode = true,
                ButtonPressed = key == _selectedKey,
                FocusMode = Control.FocusModeEnum.All,
                ClipContents = true
            };

            if (texture is null || !TryAddTextureVisual(button, key, texture))
            {
                button.TooltipText = $"{displayName}\n{missingIconTooltip}";
                AddFallbackVisual(button);
            }

            var selectedBadge = Ui1Theme.Label("✓", Ui1TextRole.Accent);
            selectedBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
            selectedBadge.HorizontalAlignment = HorizontalAlignment.Center;
            selectedBadge.VerticalAlignment = VerticalAlignment.Center;
            selectedBadge.AnchorLeft = 1f;
            selectedBadge.AnchorRight = 1f;
            selectedBadge.AnchorTop = 0f;
            selectedBadge.AnchorBottom = 0f;
            selectedBadge.OffsetLeft = -(BadgeSize + 1f);
            selectedBadge.OffsetTop = 1f;
            selectedBadge.OffsetRight = -1f;
            selectedBadge.OffsetBottom = BadgeSize + 1f;
            selectedBadge.Visible = key == _selectedKey;
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
        ModelKey resolved = ResolveAvailableSelection(key);
        if (!resolved.IsValid) return;
        _selectedKey = resolved;
        foreach ((ModelKey candidate, Button button) in _buttons)
        {
            button.ButtonPressed = candidate == resolved;
        }
        RefreshStyles();
        if (notify)
        {
            SelectionChanged?.Invoke(resolved);
        }
    }

    private static Texture2D? TryGetUsableTexture(ModelKey key, Texture2D? texture)
    {
        if (texture is null)
        {
            return null;
        }

        try
        {
            if (GodotObject.IsInstanceValid(texture))
            {
                return texture;
            }
        }
        catch (ObjectDisposedException)
        {
            // Fall through to the existing question-mark visual.
        }

        LogInvalidTextureOnce(key);
        return null;
    }

    private static bool TryAddTextureVisual(Button button, ModelKey key, Texture2D texture)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(texture))
            {
                LogInvalidTextureOnce(key);
                return false;
            }

            var textureRect = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            textureRect.Texture = texture;
            textureRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            textureRect.OffsetLeft = IconInset;
            textureRect.OffsetTop = IconInset;
            textureRect.OffsetRight = -IconInset;
            textureRect.OffsetBottom = -IconInset;
            button.AddChild(textureRect);
            return true;
        }
        catch (ObjectDisposedException)
        {
            LogInvalidTextureOnce(key);
            return false;
        }
    }

    private static void AddFallbackVisual(Button button)
    {
        var fallback = Ui1Theme.Label("?", Ui1TextRole.Muted);
        fallback.HorizontalAlignment = HorizontalAlignment.Center;
        fallback.VerticalAlignment = VerticalAlignment.Center;
        fallback.MouseFilter = Control.MouseFilterEnum.Ignore;
        fallback.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(fallback);
    }

    private static void LogInvalidTextureOnce(ModelKey key)
    {
        if (!InvalidTextureDiagnostics.Add(key))
        {
            return;
        }

        RuntimeLog.Detail(
            $"characterPoolIconInvalidFallback=true;modelKey={key.Serialized};evidence=disposed-or-invalid-texture;");
    }

    private void RefreshStyles()
    {
        foreach ((ModelKey key, Button button) in _buttons)
        {
            bool selected = key == _selectedKey;
            Ui1Theme.ApplyButton(
                button,
                selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
            if (_selectionBadges.TryGetValue(key, out Label? badge) && badge is not null)
            {
                badge.Visible = selected;
            }
        }
    }
}
