using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed record BossMapVariantUiState(
    int Act,
    ModelKey ActKey,
    bool IsVariantActive,
    IReadOnlyList<ModelKey> FirstBosses,
    IReadOnlyList<ModelKey> SecondBosses);

internal sealed partial class BossMapVariantEditor : VBoxContainer
{
    private readonly BossMapVariantDefinition _definition;
    private readonly bool _hasSelectableVariant;
    private readonly bool _showSecondBoss;
    private readonly BossMapVariantIdentityButton? _variantButton;
    private readonly Dictionary<ModelKey, BossIconButton> _firstButtons = new(ModelKeyComparer.Instance);
    private readonly Dictionary<ModelKey, BossIconButton> _secondButtons = new(ModelKeyComparer.Instance);
    private readonly HashSet<ModelKey> _selectedFirst = new(ModelKeyComparer.Instance);
    private readonly HashSet<ModelKey> _selectedSecond = new(ModelKeyComparer.Instance);
    private bool _active;
    private bool _enabled = true;

    public BossMapVariantEditor(
        BossMapVariantDefinition definition,
        bool hasSelectableVariant,
        bool showSecondBoss,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        IUiTextProvider text,
        string missingIconText)
    {
        _definition = definition;
        _hasSelectableVariant = hasSelectableVariant;
        _showSecondBoss = showSecondBoss;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 2);

        if (hasSelectableVariant)
        {
            _variantButton = new BossMapVariantIdentityButton(definition.ActKey, names);
            _variantButton.Pressed += ToggleVariant;
        }

        if (showSecondBoss)
        {
            AddChild(BuildDoubleBossMatrix(icons, names, text, missingIconText));
        }
        else
        {
            AddChild(BuildSingleBossLine(
                ordinal: 1,
                labelText: definition.Act == 3
                    ? text.Get(Ui1TextKey.SearchBossMapFirstBoss)
                    : text.Get(Ui1TextKey.SearchBossMapBoss),
                icons,
                names,
                text,
                missingIconText));
        }
        Refresh();
    }

    public event Action? Changed;

    public int Act => _definition.Act;
    public ModelKey ActKey => _definition.ActKey;
    public int EnabledConditionCount => _hasSelectableVariant
        ? (_active ? 1 : 0)
        : (_selectedFirst.Count > 0 ? 1 : 0) + (_showSecondBoss && _selectedSecond.Count > 0 ? 1 : 0);

    public BossMapVariantUiState CaptureState() => new(
        Act,
        ActKey,
        _active,
        _definition.Bosses.Where(_selectedFirst.Contains).ToArray(),
        _definition.Bosses.Where(_selectedSecond.Contains).ToArray());

    public BossMapVariantSearchDraft BuildDraft() => new(
        Act,
        ActKey,
        _hasSelectableVariant,
        _active,
        _definition.Bosses,
        _definition.Bosses.Where(_selectedFirst.Contains).ToArray(),
        _definition.Bosses.Where(_selectedSecond.Contains).ToArray());

    public void RestoreState(BossMapVariantUiState state)
    {
        if (state.Act != Act || state.ActKey != ActKey)
        {
            return;
        }

        _active = state.IsVariantActive && _definition.IdentityAvailable;
        RestoreSelection(_selectedFirst, state.FirstBosses);
        RestoreSelection(_selectedSecond, state.SecondBosses);
        Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Refresh();
    }

    public void Clear(bool notify)
    {
        bool changed = _active || _selectedFirst.Count > 0 || _selectedSecond.Count > 0;
        _active = false;
        _selectedFirst.Clear();
        _selectedSecond.Clear();
        Refresh();
        if (changed && notify)
        {
            Changed?.Invoke();
        }
    }

    public void FocusFirstBoss()
    {
        BossIconButton? button = _firstButtons.Values.FirstOrDefault();
        button?.GrabFocus();
    }

    private Control BuildSingleBossLine(
        int ordinal,
        string labelText,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        IUiTextProvider text,
        string missingIconText)
    {
        var line = CreateFullLine(BossMapRowGeometry.CellHeight);
        line.AddChild(BuildVariantCell(BossMapRowGeometry.CellHeight, names, text));
        line.AddChild(BuildVerticalDivider(BossMapRowGeometry.CellHeight));
        line.AddChild(BuildBossContentLine(ordinal, labelText, icons, names, missingIconText));
        return line;
    }

    private Control BuildDoubleBossMatrix(
        IGameIconResolver icons,
        IGameContentNameResolver names,
        IUiTextProvider text,
        string missingIconText)
    {
        var matrix = CreateFullLine(BossMapRowGeometry.DoubleRowHeight);
        matrix.AddChild(BuildVariantCell(BossMapRowGeometry.DoubleRowHeight, names, text));
        matrix.AddChild(BuildVerticalDivider(BossMapRowGeometry.DoubleRowHeight));

        var bossRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0, BossMapRowGeometry.DoubleRowHeight),
            ClipContents = true
        };
        bossRows.AddThemeConstantOverride("separation", 0);
        bossRows.AddChild(BuildBossContentLine(
            1,
            text.Get(Ui1TextKey.SearchBossMapFirstBoss),
            icons,
            names,
            missingIconText));

        var separator = new HSeparator
        {
            CustomMinimumSize = new Vector2(0, BossMapRowGeometry.InterRowSeparatorHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplySeparator(separator);
        bossRows.AddChild(separator);

        bossRows.AddChild(BuildBossContentLine(
            2,
            text.Get(Ui1TextKey.SearchBossMapSecondBoss),
            icons,
            names,
            missingIconText));
        matrix.AddChild(bossRows);
        return matrix;
    }

    private static HBoxContainer CreateFullLine(float height)
    {
        var line = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0, height),
            ClipContents = true
        };
        line.AddThemeConstantOverride("separation", 8);
        return line;
    }

    private Control BuildVariantCell(
        float height,
        IGameContentNameResolver names,
        IUiTextProvider text)
    {
        var variantCell = new Control
        {
            CustomMinimumSize = new Vector2(BossMapRowGeometry.VariantWidth, height),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            ClipContents = true
        };
        if (_hasSelectableVariant && _variantButton is not null)
        {
            variantCell.AddChild(_variantButton);
            _variantButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        else
        {
            Control readOnly = BuildReadOnlyVariantCell(names, text);
            variantCell.AddChild(readOnly);
            readOnly.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        return variantCell;
    }

    private static VSeparator BuildVerticalDivider(float height)
    {
        var divider = new VSeparator
        {
            CustomMinimumSize = new Vector2(1, height),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplySeparator(divider);
        return divider;
    }

    private Control BuildBossContentLine(
        int ordinal,
        string labelText,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        string missingIconText)
    {
        var line = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(0, BossMapRowGeometry.CellHeight),
            ClipContents = true
        };
        line.AddThemeConstantOverride("separation", 8);

        var ordinalLabel = Ui1Theme.Label(labelText, Ui1TextRole.Meta, true);
        ordinalLabel.CustomMinimumSize = new Vector2(BossMapRowGeometry.OrdinalLabelWidth, BossMapRowGeometry.CellHeight);
        ordinalLabel.VerticalAlignment = VerticalAlignment.Center;
        ordinalLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        ordinalLabel.ClipText = true;
        ordinalLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        line.AddChild(ordinalLabel);

        var bosses = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, BossMapRowGeometry.CellHeight),
            ClipContents = true
        };
        bosses.AddThemeConstantOverride("separation", 3);
        foreach (ModelKey bossKey in _definition.Bosses)
        {
            var button = new BossIconButton(bossKey, icons, names, missingIconText);
            ModelKey captured = bossKey;
            button.Pressed += () => ToggleBoss(ordinal, captured);
            if (ordinal == 1)
            {
                _firstButtons[captured] = button;
            }
            else
            {
                _secondButtons[captured] = button;
            }
            bosses.AddChild(button);
        }
        line.AddChild(bosses);
        return line;
    }

    private Control BuildReadOnlyVariantCell(
        IGameContentNameResolver names,
        IUiTextProvider text)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", (int)BossMapRowGeometry.CellInset);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", (int)BossMapRowGeometry.CellInset);

        var centered = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        row.AddThemeConstantOverride("separation", 6);

        string displayName = names.Resolve(_definition.ActKey, GameContentKind.Act);
        Label name = Ui1Theme.Label(displayName, Ui1TextRole.Body, true);
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        // A clipped Label reports an almost-zero minimum width to HBoxContainer.
        // Reserve explicit width so the fixed marker cannot squeeze the actual
        // map-variant name out of the read-only Act 2/3 identity cell.
        name.CustomMinimumSize = new Vector2(88, 0);
        name.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.AutowrapMode = TextServer.AutowrapMode.Off;
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.TooltipText = displayName;

        Label fixedMarker = Ui1Theme.Label(text.Get(Ui1TextKey.SearchBossMapFixedVariant), Ui1TextRole.Muted);
        fixedMarker.MouseFilter = Control.MouseFilterEnum.Ignore;
        fixedMarker.VerticalAlignment = VerticalAlignment.Center;
        fixedMarker.AutowrapMode = TextServer.AutowrapMode.Off;

        row.AddChild(name);
        row.AddChild(fixedMarker);
        centered.AddChild(row);
        margin.AddChild(centered);
        return margin;
    }

    private void ToggleVariant()
    {
        if (!_enabled || !_definition.IdentityAvailable)
        {
            return;
        }
        _active = !_active;
        Refresh();
        Changed?.Invoke();
    }

    private void ToggleBoss(int ordinal, ModelKey bossKey)
    {
        if (!_enabled || !_definition.IdentityAvailable)
        {
            return;
        }

        HashSet<ModelKey> selected = ordinal == 1 ? _selectedFirst : _selectedSecond;
        bool selecting = !selected.Contains(bossKey);
        if (selecting)
        {
            selected.Add(bossKey);
            if (_hasSelectableVariant)
            {
                _active = true;
            }
        }
        else
        {
            selected.Remove(bossKey);
        }
        Refresh();
        Changed?.Invoke();
    }

    private void RestoreSelection(HashSet<ModelKey> target, IReadOnlyList<ModelKey> source)
    {
        target.Clear();
        foreach (ModelKey key in source)
        {
            if (_definition.Bosses.Contains(key, ModelKeyComparer.Instance))
            {
                target.Add(key);
            }
        }
    }

    private void Refresh()
    {
        _variantButton?.SetState(_active, _enabled && _definition.IdentityAvailable);
        foreach ((ModelKey key, BossIconButton button) in _firstButtons)
        {
            button.SetState(_selectedFirst.Contains(key), _enabled && _definition.IdentityAvailable);
        }
        foreach ((ModelKey key, BossIconButton button) in _secondButtons)
        {
            button.SetState(_selectedSecond.Contains(key), _enabled && _definition.IdentityAvailable);
        }
    }
}
