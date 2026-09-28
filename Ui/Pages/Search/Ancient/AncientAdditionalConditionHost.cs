using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal sealed partial class AncientAdditionalConditionHost : PanelContainer
{
    private readonly ICharacterPoolIconProvider _icons;
    private readonly Label _title;
    private readonly Label _warning;
    private readonly HBoxContainer _targets;
    private readonly Dictionary<ModelKey, Button> _buttons = new(ModelKeyComparer.Instance);
    private readonly Dictionary<ModelKey, Label> _badges = new(ModelKeyComparer.Instance);
    private readonly HashSet<ModelKey> _selected = new(ModelKeyComparer.Instance);
    private IReadOnlyList<ModelKey> _candidates = Array.Empty<ModelKey>();
    private bool _enabled = true;

    public AncientAdditionalConditionHost(ICharacterPoolIconProvider icons)
    {
        _icons = icons;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 8f);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 5);
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _title.AutowrapMode = TextServer.AutowrapMode.Off;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _warning = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _warning.Visible = false;
        _targets = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _targets.AddThemeConstantOverride("separation", 5);
        column.AddChild(_title);
        column.AddChild(_warning);
        column.AddChild(_targets);
        AddChild(column);
        Visible = false;
    }

    public event Action? Changed;

    public IReadOnlyList<ModelKey> SelectedTargets => _candidates
        .Where(key => _selected.Contains(key))
        .ToArray();

    public void Bind(
        IReadOnlyList<ModelKey> candidates,
        bool authorityExact,
        IGameContentNameResolver names,
        IUiTextProvider text,
        string missingIconText)
    {
        _candidates = candidates
            .Where(key => key.IsValid && key.Category == BaseGameModelKeys.Categories.Character)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        _selected.RemoveWhere(key => !_candidates.Contains(key, ModelKeyComparer.Instance));
        _title.Text = text.Get(Ui1TextKey.SearchAncientSeaGlassTargetAny);
        _title.TooltipText = _title.Text;
        _warning.Text = text.Get(Ui1TextKey.SearchAncientSeaGlassTargetAuthorityPending);
        _warning.Visible = !authorityExact;

        foreach (Node child in _targets.GetChildren())
        {
            _targets.RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();
        _badges.Clear();

        foreach (ModelKey key in _candidates)
        {
            string displayName = names.Resolve(key, GameContentKind.Character);
            IconDescriptor descriptor = _icons.Resolve(key);
            var button = new Button
            {
                CustomMinimumSize = new Vector2(38, 38),
                FocusMode = Control.FocusModeEnum.All,
                TooltipText = descriptor.IsMissing
                    ? displayName + "\n" + missingIconText
                    : displayName,
                ClipContents = true
            };
            if (descriptor.Texture is not null)
            {
                var texture = new TextureRect
                {
                    Texture = descriptor.Texture,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                texture.OffsetLeft = 3;
                texture.OffsetTop = 3;
                texture.OffsetRight = -3;
                texture.OffsetBottom = -3;
                button.AddChild(texture);
            }
            else
            {
                Label missing = Ui1Theme.Label("?", Ui1TextRole.Muted);
                missing.MouseFilter = Control.MouseFilterEnum.Ignore;
                missing.HorizontalAlignment = HorizontalAlignment.Center;
                missing.VerticalAlignment = VerticalAlignment.Center;
                missing.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                button.AddChild(missing);
            }

            Label badge = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
            badge.MouseFilter = Control.MouseFilterEnum.Ignore;
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            badge.OffsetLeft = -15;
            badge.OffsetTop = 1;
            badge.OffsetRight = -1;
            badge.OffsetBottom = 15;
            button.AddChild(badge);

            ModelKey captured = key;
            button.Pressed += () => Toggle(captured);
            _buttons[key] = button;
            _badges[key] = badge;
            _targets.AddChild(button);
        }
        Refresh();
    }

    public void SetSelectedTargets(IEnumerable<ModelKey> targets)
    {
        _selected.Clear();
        foreach (ModelKey key in targets)
        {
            if (_candidates.Contains(key, ModelKeyComparer.Instance))
            {
                _selected.Add(key);
            }
        }
        Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Refresh();
    }

    public void Clear(bool notify)
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        Refresh();
        if (notify) Changed?.Invoke();
    }

    private void Toggle(ModelKey key)
    {
        if (!_enabled) return;
        if (!_selected.Add(key)) _selected.Remove(key);
        Refresh();
        Changed?.Invoke();
    }

    private void Refresh()
    {
        foreach ((ModelKey key, Button button) in _buttons)
        {
            bool selected = _selected.Contains(key);
            button.Disabled = !_enabled;
            Ui1Theme.ApplyButton(button, selected
                ? Ui1ButtonRole.NavigationSelected
                : Ui1ButtonRole.Secondary);
            if (_badges.TryGetValue(key, out Label? badge))
            {
                badge.Text = selected ? "✓" : string.Empty;
            }
        }
    }
}
