using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal sealed record AncientRowUiState(
    int Act,
    ModelKey AncientKey,
    bool IsActive,
    IReadOnlyList<ModelKey> SelectedOptions,
    IReadOnlyList<ModelKey> SeaGlassTargets);

/// <summary>
/// One player-facing Ancient card. Identity selection lives in the card header;
/// this Ancient's option targets occupy the full card width below it.
/// </summary>
internal sealed partial class AncientConditionRow : PanelContainer
{
    private readonly AncientRowDefinition _definition;
    private readonly AncientIdentityButton _identity;
    private readonly Dictionary<ModelKey, AncientOptionIconButton> _optionButtons = new(ModelKeyComparer.Instance);
    private readonly HashSet<ModelKey> _selectedOptions = new(ModelKeyComparer.Instance);
    private readonly AncientAdditionalConditionHost _additional;
    private readonly Label _pending;
    private Control? _optionsViewport;
    private bool _active;
    private bool _expanded;
    private bool _enabled = true;

    public AncientConditionRow(
        AncientRowDefinition definition,
        IReadOnlyList<ModelKey> seaGlassTargets,
        bool seaGlassTargetAuthorityExact,
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterIcons,
        IGameContentNameResolver names,
        IUiTextProvider text,
        AnchoredTooltipHost tooltipHost,
        string missingIconText)
    {
        _definition = definition;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, AncientRowGeometry.CardPadding);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        content.AddThemeConstantOverride("separation", 6);

        _identity = new AncientIdentityButton(
            definition.AncientKey,
            icons,
            names,
            missingIconText);
        _identity.Pressed += ToggleIdentity;
        content.AddChild(_identity);

        int columns = ResolveOptionColumns(definition);
        if (definition.Options.Count > 0)
        {
            int optionRows = (definition.Options.Count + columns - 1) / columns;
            float optionHeight = optionRows * AncientRowGeometry.OptionCellSize +
                                 Math.Max(0, optionRows - 1) * AncientRowGeometry.OptionGap;
            _optionsViewport = new Control
            {
                CustomMinimumSize = new Vector2(0, optionHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
                ClipContents = true,
                Visible = false
            };
            var optionsCenter = new CenterContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipContents = true
            };
            optionsCenter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var options = new GridContainer
            {
                Columns = columns,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            options.AddThemeConstantOverride("h_separation", AncientRowGeometry.OptionGap);
            options.AddThemeConstantOverride("v_separation", AncientRowGeometry.OptionGap);
            foreach (AncientOptionCandidate candidate in definition.Options)
            {
                var button = new AncientOptionIconButton(
                    candidate,
                    icons,
                    names,
                    tooltipHost,
                    missingIconText);
                ModelKey captured = candidate.OptionKey;
                button.Pressed += () => ToggleOption(captured);
                _optionButtons[captured] = button;
                options.AddChild(button);
            }
            optionsCenter.AddChild(options);
            _optionsViewport.AddChild(optionsCenter);
            content.AddChild(_optionsViewport);
        }

        _pending = Ui1Theme.Label(text.Get(Ui1TextKey.SearchAncientCatalogPending), Ui1TextRole.Warning, true);
        _pending.Visible = definition.Options.Count == 0;
        _pending.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pending.TooltipText = definition.EvidenceCode;
        if (_pending.Visible)
        {
            content.AddChild(_pending);
        }

        _additional = new AncientAdditionalConditionHost(characterIcons);
        _additional.Bind(
            seaGlassTargets,
            seaGlassTargetAuthorityExact,
            names,
            text,
            missingIconText);
        _additional.Changed += () => Changed?.Invoke();
        content.AddChild(_additional);

        AddChild(content);
        Refresh();
    }

    public event Action? Changed;

    public int Act => _definition.Act;
    public ModelKey AncientKey => _definition.AncientKey;
    public bool IsActive => _active;
    public int EnabledConditionCount => _active ? 1 : 0;

    public AncientRowUiState CaptureState() => new(
        _definition.Act,
        _definition.AncientKey,
        _active,
        _definition.Options
            .Select(option => option.OptionKey)
            .Where(key => _selectedOptions.Contains(key))
            .ToArray(),
        _additional.SelectedTargets);

    public void RestoreState(AncientRowUiState state)
    {
        if (state.Act != Act || state.AncientKey != AncientKey) return;
        _active = state.IsActive && _definition.IdentityAvailable;
        _selectedOptions.Clear();
        foreach (ModelKey key in state.SelectedOptions)
        {
            if (_definition.Options.Any(option => option.OptionKey == key))
            {
                _selectedOptions.Add(key);
            }
        }
        _additional.SetSelectedTargets(state.SeaGlassTargets);
        // Identity activation and option reveal are one player-facing state.
        // Rebuild/catalog/localization restores must therefore reopen an active
        // Ancient instead of leaving its already-selected options hidden.
        _expanded = _active;
        Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _additional.SetEnabled(enabled);
        Refresh();
    }

    public void Clear(bool notify)
    {
        bool changed = _active || _selectedOptions.Count > 0 || _additional.SelectedTargets.Count > 0;
        _active = false;
        _expanded = false;
        _selectedOptions.Clear();
        _additional.Clear(notify: false);
        Refresh();
        if (changed && notify) Changed?.Invoke();
    }

    private void ToggleIdentity()
    {
        if (!_enabled || !_definition.IdentityAvailable) return;
        _active = !_active;
        _expanded = _active;
        Refresh();
        Changed?.Invoke();
    }

    private void ToggleOption(ModelKey optionKey)
    {
        if (!_enabled) return;
        AncientOptionCandidate? candidate = _definition.Options.FirstOrDefault(option => option.OptionKey == optionKey);
        if (candidate is null || !candidate.IsAvailable) return;

        bool selecting = !_selectedOptions.Contains(optionKey);
        if (selecting)
        {
            _selectedOptions.Add(optionKey);
            _active = true;
        }
        else
        {
            _selectedOptions.Remove(optionKey);
        }
        Refresh();
        Changed?.Invoke();
    }

    private void Refresh()
    {
        _identity.SetState(_active, _enabled && _definition.IdentityAvailable);
        _identity.SetExpanded(_expanded);
        if (_optionsViewport is not null)
        {
            _optionsViewport.Visible = _expanded;
        }
        foreach ((ModelKey key, AncientOptionIconButton button) in _optionButtons)
        {
            AncientOptionCandidate candidate = _definition.Options.First(option => option.OptionKey == key);
            button.SetState(
                _selectedOptions.Contains(key),
                _enabled && candidate.IsAvailable);
        }
        bool seaGlassSelected = _definition.Options.Any(option =>
            option.AdditionalConditionKind == AncientAdditionalConditionKind.SeaGlassCharacterTarget &&
            _selectedOptions.Contains(option.OptionKey));
        _additional.Visible = seaGlassSelected;
    }

    private static int ResolveOptionColumns(AncientRowDefinition definition)
    {
        if (definition.Options.Count == 0) return 1;
        return Math.Max(1, Math.Min(AncientRowGeometry.StandardOptionColumns, definition.Options.Count));
    }
}
