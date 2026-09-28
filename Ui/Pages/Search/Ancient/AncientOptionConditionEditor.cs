using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

/// <summary>
/// Main-thread editor for the bounded user-declared predicates used while
/// constructing Ancient option pools. The compact icon selector deliberately
/// uses a different visual language from Ancient search-target selection:
/// bright = expected eligible, dim = expected ineligible.
/// </summary>
internal sealed partial class AncientOptionConditionEditor : PanelContainer
{
    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly Dictionary<Button, (ModelKey Key, string ConditionTextKey)> _conditionTooltips = new();
    private readonly Button _foldout;
    private readonly Button _help;
    private readonly Button _reset;
    private readonly VBoxContainer _body;
    private readonly HFlowContainer _conditions;
    private readonly Button _tezcataraBasicStrike;
    private readonly Button _nonupeipeSwift;
    private readonly Button _tanxInstinct;
    private readonly Button _paelGoopyDefend;
    private readonly Button _paelNoEventPet;
    private readonly Button _paelRemovable;
    private readonly Button _orobasArchaicTooth;
    private readonly Button _orobasTouch;
    private readonly Button _darvPandorasBox;
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _enabled = true;
    private bool _suppressChanged;

    public AncientOptionConditionEditor(IGameIconResolver icons, AnchoredTooltipHost tooltipHost)
    {
        _icons = icons;
        _tooltipHost = tooltipHost;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, 3f, 1, 6f);

        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 5);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 5);
        _foldout = new Button
        {
            ToggleMode = true,
            ButtonPressed = false,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 30),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyButton(_foldout, Ui1ButtonRole.Ghost);
        _help = new Button
        {
            Text = "?",
            CustomMinimumSize = new Vector2(30, 30),
            FocusMode = Control.FocusModeEnum.All
        };
        Ui1Theme.ApplyButton(_help, Ui1ButtonRole.Ghost);
        _help.TooltipText = string.Empty;
        _help.MouseEntered += ShowHelpTooltip;
        _help.MouseExited += () => _tooltipHost.Dismiss(_help);
        _help.TreeExiting += () => _tooltipHost.Dismiss(_help);
        _reset = new Button { CustomMinimumSize = new Vector2(84, 30) };
        Ui1Theme.ApplyButton(_reset, Ui1ButtonRole.Ghost);
        header.AddChild(_foldout);
        header.AddChild(_help);
        header.AddChild(_reset);
        root.AddChild(header);

        _body = new VBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _body.AddThemeConstantOverride("separation", 5);

        _conditions = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _conditions.AddThemeConstantOverride("h_separation", 6);
        _conditions.AddThemeConstantOverride("v_separation", 6);
        _paelGoopyDefend = ConditionButton("PAELS_CLAW", Ui1TextKey.AncientConditionPaelGoopyDefend);
        _paelNoEventPet = ConditionButton("PAELS_LEGION", Ui1TextKey.AncientConditionPaelNoEventPet);
        _paelRemovable = ConditionButton("PAELS_TOOTH", Ui1TextKey.AncientConditionPaelRemovable);
        _orobasArchaicTooth = ConditionButton("ARCHAIC_TOOTH", Ui1TextKey.AncientConditionOrobasArchaicTooth);
        _orobasTouch = ConditionButton("TOUCH_OF_OROBAS", Ui1TextKey.AncientConditionOrobasTouch);
        _tezcataraBasicStrike = ConditionButton("NUTRITIOUS_SOUP", Ui1TextKey.AncientConditionTezcataraBasicStrike);
        _nonupeipeSwift = ConditionButton("BEAUTIFUL_BRACELET", Ui1TextKey.AncientConditionNonupeipeSwift);
        _tanxInstinct = ConditionButton("TRI_BOOMERANG", Ui1TextKey.AncientConditionTanxInstinct);
        _darvPandorasBox = ConditionButton("PANDORAS_BOX", Ui1TextKey.AncientConditionDarvPandorasBox);
        foreach (Button button in Buttons()) _conditions.AddChild(button);
        _body.AddChild(_conditions);
        root.AddChild(_body);
        AddChild(root);

        _foldout.Toggled += expanded =>
        {
            _body.Visible = expanded;
            RefreshText();
        };
        _reset.Pressed += () => SetProfile(AncientOptionConditionProfile.BroadDefault, notify: true);
        foreach (Button button in Buttons())
        {
            button.Toggled += _ => OnConditionChanged();
        }
        SetProfile(AncientOptionConditionProfile.BroadDefault, notify: false);
    }

    public event Action? Changed;

    public AncientOptionConditionProfile CurrentProfile => new(
        TezcataraHasBasicStrike: _tezcataraBasicStrike.ButtonPressed,
        NonupeipeSwiftEnchantableAtLeast4: _nonupeipeSwift.ButtonPressed,
        TanxInstinctEnchantableAtLeast3: _tanxInstinct.ButtonPressed,
        PaelGoopyDefendCardsAtLeast3: _paelGoopyDefend.ButtonPressed,
        PaelAllowLegionNoEventPet: _paelNoEventPet.ButtonPressed,
        PaelRemovableCardsAtLeast5: _paelRemovable.ButtonPressed,
        OrobasArchaicToothConditionMet: _orobasArchaicTooth.ButtonPressed,
        OrobasTouchOfOrobasConditionMet: _orobasTouch.ButtonPressed,
        DarvAllowPandorasBoxRelicSet: _darvPandorasBox.ButtonPressed);

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _tooltipHost.Dismiss();
        _text = text;
        _names = names;
        _reset.Text = text.Get(Ui1TextKey.SearchAncientOptionConditionsReset);
        RefreshText();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _foldout.Disabled = !enabled;
        foreach (Button button in Buttons()) button.Disabled = !enabled;
        RefreshText();
    }

    public void RestoreProfile(AncientOptionConditionProfile profile, bool notify) =>
        SetProfile(profile ?? AncientOptionConditionProfile.BroadDefault, notify);

    public void ResetToBroadDefault(bool notify) =>
        SetProfile(AncientOptionConditionProfile.BroadDefault, notify);

    private Button ConditionButton(string relicEntry, string conditionTextKey)
    {
        var button = new Button
        {
            ToggleMode = true,
            ButtonPressed = true,
            CustomMinimumSize = new Vector2(42, 42),
            FocusMode = Control.FocusModeEnum.All,
            ClipContents = true
        };
        var key = new ModelKey(BaseGameModelKeys.Categories.Relic, relicEntry);
        IconDescriptor descriptor = _icons.Resolve(key, GameContentKind.Relic, IconVariant.Small);
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
        button.TooltipText = string.Empty;
        _conditionTooltips[button] = (key, conditionTextKey);
        button.MouseEntered += () => ShowConditionTooltip(button);
        button.MouseExited += () => _tooltipHost.Dismiss(button);
        button.TreeExiting += () => _tooltipHost.Dismiss(button);
        return button;
    }

    private IEnumerable<Button> Buttons()
    {
        yield return _paelGoopyDefend;
        yield return _paelNoEventPet;
        yield return _paelRemovable;
        yield return _orobasArchaicTooth;
        yield return _orobasTouch;
        yield return _tezcataraBasicStrike;
        yield return _nonupeipeSwift;
        yield return _tanxInstinct;
        yield return _darvPandorasBox;
    }

    private void SetProfile(AncientOptionConditionProfile profile, bool notify)
    {
        bool changed = CurrentProfile != profile;
        _suppressChanged = true;
        _tezcataraBasicStrike.ButtonPressed = profile.TezcataraHasBasicStrike;
        _nonupeipeSwift.ButtonPressed = profile.NonupeipeSwiftEnchantableAtLeast4;
        _tanxInstinct.ButtonPressed = profile.TanxInstinctEnchantableAtLeast3;
        _paelGoopyDefend.ButtonPressed = profile.PaelGoopyDefendCardsAtLeast3;
        _paelNoEventPet.ButtonPressed = profile.PaelAllowLegionNoEventPet;
        _paelRemovable.ButtonPressed = profile.PaelRemovableCardsAtLeast5;
        _orobasArchaicTooth.ButtonPressed = profile.OrobasArchaicToothConditionMet;
        _orobasTouch.ButtonPressed = profile.OrobasTouchOfOrobasConditionMet;
        _darvPandorasBox.ButtonPressed = profile.DarvAllowPandorasBoxRelicSet;
        _suppressChanged = false;
        RefreshText();
        if (changed && notify) Changed?.Invoke();
    }

    private void OnConditionChanged()
    {
        if (_suppressChanged) return;
        RefreshText();
        Changed?.Invoke();
    }

    private void RefreshText()
    {
        if (_text is null) return;
        string state = CurrentProfile.IsBroadDefault
            ? _text.Get(Ui1TextKey.SearchAncientOptionConditionsDefault)
            : _text.Get(Ui1TextKey.SearchAncientOptionConditionsCustom);
        _foldout.Text = $"{(_body.Visible ? "▾" : "▸")}  {_text.Get(Ui1TextKey.SearchAncientOptionConditionsTitle)} · {state}";
        _reset.Disabled = !_enabled || CurrentProfile.IsBroadDefault;
        foreach (Button button in Buttons())
        {
            bool expectedEligible = button.ButtonPressed;
            Ui1Theme.ApplyButton(button, expectedEligible ? Ui1ButtonRole.Secondary : Ui1ButtonRole.Ghost);
            button.Modulate = expectedEligible ? Colors.White : new Color(1f, 1f, 1f, 0.38f);
        }
    }

    private void ShowHelpTooltip()
    {
        if (_text is null) return;
        _tooltipHost.ShowStructuredText(
            _help,
            _text.Get(Ui1TextKey.SearchAncientOptionConditionsTitle),
            string.Empty,
            _text.Get(Ui1TextKey.SearchAncientOptionConditionsHint));
    }

    private void ShowConditionTooltip(Button button)
    {
        if (_text is null || _names is null || !_conditionTooltips.TryGetValue(button, out var binding)) return;
        string optionName = _names.Resolve(binding.Key, GameContentKind.Relic);
        _tooltipHost.ShowStructuredText(
            button,
            optionName,
            _text.Get(Ui1TextKey.SearchAncientOptionConditionTooltipTitle),
            _text.Get(binding.ConditionTextKey));
    }
}
