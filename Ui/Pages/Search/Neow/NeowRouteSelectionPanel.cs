using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// Master-side route identity selection. Normal mode has one active relic;
/// Bones mode keeps up to two selected relic identities. Pickup-order semantics
/// are authored only by the detail-side Bones card.
/// </summary>
internal sealed partial class NeowRouteSelectionPanel : VBoxContainer
{
    private const float RouteTileSize = 50f;
    private const float RouteGridSeparation = 4f;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly Button _bonesButton;
    private readonly Label _bonesBadge;
    private readonly TextureRect _bonesIcon;
    private readonly Label _bonesMissingIcon;
    private readonly Label _bonesName;
    private readonly Label _bonesSubtitle;
    private readonly Label _selectionTitle;
    private readonly ScrollContainer _scroll;
    private readonly GridContainer _grid;
    private readonly List<ModelKey> _bonesSelected = new();
    private readonly Dictionary<ModelKey, NeowRouteRelicTile> _tiles = new(ModelKeyComparer.Instance);
    private IReadOnlyList<ModelKey> _routeRelics = Array.Empty<ModelKey>();
    private IGameContentNameResolver? _names;
    private IUiTextProvider? _text;
    private ModelKey? _normalSelected;
    private bool _bonesMode;
    private BonesRouteOrderMode _bonesOrderMode = BonesRouteOrderMode.AnyOrder;
    private bool _enabled = true;

    public NeowRouteSelectionPanel(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons;
        _tooltipHost = tooltipHost;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);

        _bonesButton = new Button
        {
            CustomMinimumSize = new Vector2(GridWidthForColumns(5), 82),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            FocusMode = Control.FocusModeEnum.All
        };
        _bonesButton.Pressed += ToggleBonesMode;
        _bonesButton.MouseEntered += ShowBonesTooltip;
        _bonesButton.MouseExited += () => _tooltipHost.Dismiss(_bonesButton);
        _bonesButton.TreeExiting += () => _tooltipHost.Dismiss(_bonesButton);

        var bonesMargin = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        bonesMargin.AddThemeConstantOverride("margin_left", 10);
        bonesMargin.AddThemeConstantOverride("margin_top", 8);
        bonesMargin.AddThemeConstantOverride("margin_right", 10);
        bonesMargin.AddThemeConstantOverride("margin_bottom", 8);
        bonesMargin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var bonesRow = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        bonesRow.AddThemeConstantOverride("separation", 9);
        var iconHost = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(54, 54)
        };
        _bonesIcon = new TextureRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(54, 54),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        _bonesIcon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _bonesMissingIcon = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted);
        _bonesMissingIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bonesMissingIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _bonesMissingIcon.VerticalAlignment = VerticalAlignment.Center;
        _bonesMissingIcon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        iconHost.AddChild(_bonesIcon);
        iconHost.AddChild(_bonesMissingIcon);

        var bonesTitles = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        bonesTitles.AddThemeConstantOverride("separation", 2);
        _bonesName = Ui1Theme.Label(string.Empty, Ui1TextRole.CardTitle, true);
        _bonesName.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bonesSubtitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _bonesSubtitle.MouseFilter = Control.MouseFilterEnum.Ignore;
        bonesTitles.AddChild(_bonesName);
        bonesTitles.AddChild(_bonesSubtitle);

        _bonesBadge = Ui1Theme.Label(string.Empty, Ui1TextRole.Accent);
        _bonesBadge.MouseFilter = Control.MouseFilterEnum.Ignore;
        _bonesBadge.VerticalAlignment = VerticalAlignment.Center;
        bonesRow.AddChild(iconHost);
        bonesRow.AddChild(bonesTitles);
        bonesRow.AddChild(_bonesBadge);
        bonesMargin.AddChild(bonesRow);
        _bonesButton.AddChild(bonesMargin);
        AddChild(_bonesButton);

        _selectionTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle, true);
        AddChild(_selectionTitle);

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 300)
        };
        _grid = new GridContainer
        {
            Columns = 5,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(GridWidthForColumns(5), 0)
        };
        _grid.AddThemeConstantOverride("h_separation", (int)RouteGridSeparation);
        _grid.AddThemeConstantOverride("v_separation", (int)RouteGridSeparation);
        _scroll.AddChild(_grid);
        _scroll.Resized += UpdateGridColumns;
        AddChild(_scroll);
        RefreshVisualState();
    }

    public event Action? Changed;

    public bool BonesMode => _bonesMode;
    public ModelKey? NormalRouteRelic => _bonesMode ? null : _normalSelected;
    public ModelKey? ActiveRouteRelic => _bonesMode
        ? BaseGameModelKeys.Relics.NeowsBones
        : _normalSelected;
    public IReadOnlyList<ModelKey> BonesRelics => _bonesSelected.ToArray();
    public BonesRouteOrderMode BonesOrderMode => _bonesOrderMode;

    public void Bind(
        IReadOnlyList<ModelKey> routeRelics,
        IGameContentNameResolver names,
        IUiTextProvider text)
    {
        _names = names;
        _text = text;
        _routeRelics = routeRelics
            .Where(key => key.IsValid && key != BaseGameModelKeys.Relics.NeowsBones)
            .Distinct(ModelKeyComparer.Instance)
            .OrderBy(key => names.Resolve(key, GameContentKind.Relic), StringComparer.CurrentCulture)
            .ToArray();

        if (_normalSelected.HasValue && !_routeRelics.Contains(_normalSelected.Value, ModelKeyComparer.Instance))
        {
            _normalSelected = null;
        }
        _bonesSelected.RemoveAll(key => !_routeRelics.Contains(key, ModelKeyComparer.Instance));
        while (_bonesSelected.Count > 2) _bonesSelected.RemoveAt(_bonesSelected.Count - 1);

        string bonesName = names.Resolve(BaseGameModelKeys.Relics.NeowsBones, GameContentKind.Relic);
        _bonesName.Text = bonesName;
        _bonesButton.TooltipText = string.Empty;
        _bonesSubtitle.Text = text.Get(Ui1TextKey.SearchNeowBonesCombinationRoute);
        IconDescriptor icon = _icons.Resolve(BaseGameModelKeys.Relics.NeowsBones, GameContentKind.Relic, IconVariant.RelicLarge);
        _bonesIcon.Texture = icon.Texture;
        _bonesIcon.Visible = icon.Texture is not null;
        _bonesMissingIcon.Text = icon.Texture is null ? "?" : string.Empty;
        RebuildGrid();
        RefreshVisualState();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _bonesButton.Disabled = !enabled;
        RefreshTileStates();
    }

    public void SetCompact(bool compact) => UpdateGridColumns();

    public void RestoreSelection(
        ModelKey? routeRelic,
        IReadOnlyList<ModelKey>? bonesRelics,
        BonesRouteOrderMode bonesOrderMode,
        bool notify)
    {
        ModelKey[] normalizedBones = (bonesRelics ?? Array.Empty<ModelKey>())
            .Where(key => key.IsValid && _routeRelics.Contains(key, ModelKeyComparer.Instance))
            .Distinct(ModelKeyComparer.Instance)
            .Take(2)
            .ToArray();
        bool bones = routeRelic == BaseGameModelKeys.Relics.NeowsBones;
        _bonesMode = bones;
        _bonesOrderMode = bones && normalizedBones.Length == 2
            ? bonesOrderMode
            : BonesRouteOrderMode.AnyOrder;
        _bonesSelected.Clear();
        _normalSelected = null;
        if (bones)
        {
            _bonesSelected.AddRange(normalizedBones);
        }
        else if (routeRelic is { IsValid: true } route && _routeRelics.Contains(route, ModelKeyComparer.Instance))
        {
            _normalSelected = route;
        }
        RefreshVisualState();
        if (notify) Changed?.Invoke();
    }

    public void ClearAllSelections(bool notify)
    {
        bool changed = _bonesMode || _normalSelected.HasValue || _bonesSelected.Count > 0;
        _bonesMode = false;
        _bonesOrderMode = BonesRouteOrderMode.AnyOrder;
        _normalSelected = null;
        _bonesSelected.Clear();
        RefreshVisualState();
        if (changed && notify) Changed?.Invoke();
    }

    public void GrabSelectionFocus()
    {
        if (_bonesMode)
        {
            _bonesButton.GrabFocus();
            return;
        }
        if (_normalSelected.HasValue && _tiles.TryGetValue(_normalSelected.Value, out NeowRouteRelicTile? tile))
        {
            tile.GrabFocus();
            return;
        }
        _bonesButton.GrabFocus();
    }

    public void SetBonesOrderMode(BonesRouteOrderMode mode, bool notify)
    {
        if (!_enabled || !_bonesMode || _bonesOrderMode == mode) return;
        if (mode == BonesRouteOrderMode.ExactOrder && _bonesSelected.Count != 2) return;
        _bonesOrderMode = mode;
        RefreshVisualState();
        if (notify) Changed?.Invoke();
    }

    public void SwapBonesOrder(bool notify)
    {
        if (!_enabled || !_bonesMode || _bonesOrderMode != BonesRouteOrderMode.ExactOrder || _bonesSelected.Count != 2) return;
        (_bonesSelected[0], _bonesSelected[1]) = (_bonesSelected[1], _bonesSelected[0]);
        RefreshVisualState();
        if (notify) Changed?.Invoke();
    }

    private void ToggleBonesMode()
    {
        if (!_enabled) return;
        _bonesMode = !_bonesMode;
        // Entering or leaving Bones never silently reactivates a normal route.
        _normalSelected = null;
        RefreshVisualState();
        Changed?.Invoke();
    }

    private void OnRelicPressed(ModelKey key)
    {
        if (!_enabled) return;
        if (_bonesMode)
        {
            int index = _bonesSelected.FindIndex(existing => existing == key);
            if (index >= 0)
            {
                _bonesSelected.RemoveAt(index);
                if (_bonesSelected.Count < 2)
                    _bonesOrderMode = BonesRouteOrderMode.AnyOrder;
            }
            else if (_bonesSelected.Count < 2)
            {
                _bonesSelected.Add(key);
            }
        }
        else
        {
            _normalSelected = _normalSelected == key ? null : key;
        }
        RefreshVisualState();
        Changed?.Invoke();
    }

    private void ShowBonesTooltip()
    {
        string fallbackTitle = _bonesName.Text;
        _tooltipHost.ShowFor(
            _bonesButton,
            BaseGameModelKeys.Relics.NeowsBones,
            fallbackTitle);
    }

    private void RebuildGrid()
    {
        _tooltipHost.Dismiss();
        foreach (Node child in _grid.GetChildren())
        {
            _grid.RemoveChild(child);
            child.QueueFree();
        }
        _tiles.Clear();
        if (_names is null) return;
        foreach (ModelKey key in _routeRelics)
        {
            var tile = new NeowRouteRelicTile(key, _icons, _names, _tooltipHost);
            tile.Pressed += () => OnRelicPressed(key);
            _tiles[key] = tile;
            _grid.AddChild(tile);
        }
        RefreshTileStates();
    }


    private void UpdateGridColumns()
    {
        float availableWidth = _scroll.Size.X > 0f ? _scroll.Size.X : Size.X;
        int columns = availableWidth >= GridWidthForColumns(5)
            ? 5
            : availableWidth >= GridWidthForColumns(4)
                ? 4
                : 3;
        float alignedWidth = GridWidthForColumns(columns);
        if (_grid.Columns != columns) _grid.Columns = columns;
        _grid.CustomMinimumSize = new Vector2(alignedWidth, 0);
        _bonesButton.CustomMinimumSize = new Vector2(alignedWidth, 82);
    }

    private static float GridWidthForColumns(int columns) =>
        columns * RouteTileSize + Math.Max(0, columns - 1) * RouteGridSeparation;

    private void RefreshVisualState()
    {
        _bonesBadge.Text = _bonesMode ? "✓" : string.Empty;
        Ui1Theme.ApplyButton(_bonesButton, _bonesMode
            ? Ui1ButtonRole.NavigationSelected
            : Ui1ButtonRole.Secondary);
        if (_text is not null)
        {
            _selectionTitle.Text = _bonesMode
                ? string.Format(_text.Get(Ui1TextKey.SearchNeowBonesSelectionCount), _bonesSelected.Count)
                : _text.Get(Ui1TextKey.SearchNeowNormalSelectionTitle);
        }
        RefreshTileStates();
    }

    private void RefreshTileStates()
    {
        bool pairFull = _bonesMode && _bonesSelected.Count >= 2;
        foreach ((ModelKey key, NeowRouteRelicTile tile) in _tiles)
        {
            bool selected = _bonesMode
                ? _bonesSelected.Contains(key, ModelKeyComparer.Instance)
                : _normalSelected == key;
            bool disabled = !_enabled || pairFull && !selected;
            string badge = selected ? "✓" : string.Empty;
            tile.SetState(selected, disabled, badge);
        }
    }
}
