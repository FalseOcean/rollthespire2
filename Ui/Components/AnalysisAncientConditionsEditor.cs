using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Predictor-local editor for Ancient option appearance assumptions. The visual language mirrors
/// Searcher's compact appearance-condition editor, but this control owns independent Predictor state.
/// </summary>
internal sealed partial class AnalysisAncientConditionsEditor : PanelContainer
{
    public const float SubcardPadding = 6f;
    public const float SubcardRadius = 3f;
    public const int SubcardBorderWidth = 1;
    public const float HeaderMinimumHeight = 32f;
    public const float ConditionIconSize = 42f;
    public const int ConditionGap = 6;

    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly Dictionary<Button, (ModelKey Key, string ConditionTextKey)> _conditionTooltips = new();
    private readonly Button _foldout;
    private readonly VBoxContainer _body;
    private readonly HFlowContainer _conditions;
    private readonly Button _reset;
    private readonly Button _tezcataraBasicStrike;
    private readonly Button _nonupeipeSwift;
    private readonly Button _tanxInstinct;
    private readonly Button _paelGoopyDefend;
    private readonly Button _paelNoEventPet;
    private readonly Button _paelRemovable;
    private readonly Button _orobasArchaicTooth;
    private readonly Button _orobasTouch;
    private readonly Button _darvPandorasBox;
    private IUiTextProvider? _uiText;
    private IGameContentNameResolver? _contentNames;
    private bool _suppressChanged;
    private bool _busy;

    public AnalysisAncientConditionsEditor(
        IGameIconResolver icons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        Name = "AncientConditionsSubcard";
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Input, SubcardRadius, SubcardBorderWidth, SubcardPadding);

        var root = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        root.AddThemeConstantOverride("separation", 6);

        _foldout = new Button
        {
            Name = "ConditionsFoldout",
            ToggleMode = true,
            ButtonPressed = false,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0f, HeaderMinimumHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyButton(_foldout, Ui1ButtonRole.Ghost);
        root.AddChild(_foldout);

        _body = new VBoxContainer
        {
            Name = "ConditionsExpandedBody",
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _body.AddThemeConstantOverride("separation", 6);

        var controlsRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        controlsRow.AddThemeConstantOverride("separation", 8);
        _conditions = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _conditions.AddThemeConstantOverride("h_separation", ConditionGap);
        _conditions.AddThemeConstantOverride("v_separation", ConditionGap);

        _paelGoopyDefend = ConditionButton("PAELS_CLAW", Ui1TextKey.AncientConditionPaelGoopyDefend);
        _paelNoEventPet = ConditionButton("PAELS_LEGION", Ui1TextKey.AncientConditionPaelNoEventPet);
        _paelRemovable = ConditionButton("PAELS_TOOTH", Ui1TextKey.AncientConditionPaelRemovable);
        _orobasArchaicTooth = ConditionButton("ARCHAIC_TOOTH", Ui1TextKey.AncientConditionOrobasArchaicTooth);
        _orobasTouch = ConditionButton("TOUCH_OF_OROBAS", Ui1TextKey.AncientConditionOrobasTouch);
        _tezcataraBasicStrike = ConditionButton("NUTRITIOUS_SOUP", Ui1TextKey.AncientConditionTezcataraBasicStrike);
        _nonupeipeSwift = ConditionButton("BEAUTIFUL_BRACELET", Ui1TextKey.AncientConditionNonupeipeSwift);
        _tanxInstinct = ConditionButton("TRI_BOOMERANG", Ui1TextKey.AncientConditionTanxInstinct);
        _darvPandorasBox = ConditionButton("PANDORAS_BOX", Ui1TextKey.AncientConditionDarvPandorasBox);
        foreach (Button button in Buttons())
        {
            _conditions.AddChild(button);
            button.Toggled += _ => OnConditionChanged();
        }

        _reset = new Button
        {
            Name = "ConditionsReset",
            CustomMinimumSize = new Vector2(84f, 30f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyButton(_reset, Ui1ButtonRole.Ghost);
        controlsRow.AddChild(_conditions);
        controlsRow.AddChild(_reset);
        _body.AddChild(controlsRow);
        root.AddChild(_body);
        AddChild(root);

        _foldout.Toggled += expanded =>
        {
            _body.Visible = expanded;
            RefreshText();
        };
        _reset.Pressed += () => SetConditions(AncientOptionConditionProfile.BroadDefault, notify: true);
        SetConditions(AncientOptionConditionProfile.BroadDefault, notify: false);
    }

    public event Action<AncientOptionConditionProfile>? ConditionsChanged;

    public AncientOptionConditionProfile CurrentConditions => new(
        _tezcataraBasicStrike.ButtonPressed,
        _nonupeipeSwift.ButtonPressed,
        _tanxInstinct.ButtonPressed,
        _paelGoopyDefend.ButtonPressed,
        _paelNoEventPet.ButtonPressed,
        _paelRemovable.ButtonPressed,
        _orobasArchaicTooth.ButtonPressed,
        _orobasTouch.ButtonPressed,
        _darvPandorasBox.ButtonPressed);

    public void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames)
    {
        _tooltipHost.Dismiss();
        _uiText = uiText ?? throw new ArgumentNullException(nameof(uiText));
        _contentNames = contentNames ?? throw new ArgumentNullException(nameof(contentNames));
        _reset.Text = uiText.Get(Ui1TextKey.SearchAncientOptionConditionsReset);
        _foldout.TooltipText = uiText.Get(Ui1TextKey.SearchAncientOptionConditionsHint);
        RefreshText();
    }

    public void SetConditions(AncientOptionConditionProfile conditions, bool notify)
    {
        bool changed = CurrentConditions != conditions;
        _suppressChanged = true;
        try
        {
            _tezcataraBasicStrike.ButtonPressed = conditions.TezcataraHasBasicStrike;
            _nonupeipeSwift.ButtonPressed = conditions.NonupeipeSwiftEnchantableAtLeast4;
            _tanxInstinct.ButtonPressed = conditions.TanxInstinctEnchantableAtLeast3;
            _paelGoopyDefend.ButtonPressed = conditions.PaelGoopyDefendCardsAtLeast3;
            _paelNoEventPet.ButtonPressed = conditions.PaelAllowLegionNoEventPet;
            _paelRemovable.ButtonPressed = conditions.PaelRemovableCardsAtLeast5;
            _orobasArchaicTooth.ButtonPressed = conditions.OrobasArchaicToothConditionMet;
            _orobasTouch.ButtonPressed = conditions.OrobasTouchOfOrobasConditionMet;
            _darvPandorasBox.ButtonPressed = conditions.DarvAllowPandorasBoxRelicSet;
        }
        finally
        {
            _suppressChanged = false;
        }
        RefreshText();
        if (changed && notify)
        {
            ConditionsChanged?.Invoke(CurrentConditions);
        }
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _foldout.Disabled = busy;
        foreach (Button button in Buttons())
        {
            button.Disabled = busy;
        }
        RefreshText();
    }

    private Button ConditionButton(string relicEntry, string conditionTextKey)
    {
        var button = new Button
        {
            ToggleMode = true,
            ButtonPressed = true,
            CustomMinimumSize = new Vector2(ConditionIconSize, ConditionIconSize),
            FocusMode = Control.FocusModeEnum.All,
            ClipContents = true,
            TooltipText = string.Empty
        };
        var key = new ModelKey(BaseGameModelKeys.Categories.Relic, relicEntry);
        IconDescriptor descriptor = _icons.Resolve(key, GameContentKind.Relic, IconVariant.Small);
        if (descriptor.Texture is not null && !descriptor.IsMissing)
        {
            var texture = new TextureRect
            {
                Texture = descriptor.Texture,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            texture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            texture.OffsetLeft = 3f;
            texture.OffsetTop = 3f;
            texture.OffsetRight = -3f;
            texture.OffsetBottom = -3f;
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

    private void OnConditionChanged()
    {
        if (_suppressChanged)
        {
            return;
        }
        RefreshText();
        ConditionsChanged?.Invoke(CurrentConditions);
    }

    private void RefreshText()
    {
        if (_uiText is null)
        {
            return;
        }

        string state = CurrentConditions.IsBroadDefault
            ? _uiText.Get(Ui1TextKey.SearchAncientOptionConditionsDefault)
            : _uiText.Get(Ui1TextKey.SearchAncientOptionConditionsCustom);
        _foldout.Text = $"{(_body.Visible ? "▾" : "▸")}  {_uiText.Get(Ui1TextKey.SearchAncientOptionConditionsTitle)} · {state}";
        _reset.Disabled = _busy || CurrentConditions.IsBroadDefault;

        foreach (Button button in Buttons())
        {
            bool expectedEligible = button.ButtonPressed;
            Ui1Theme.ApplyButton(button, expectedEligible ? Ui1ButtonRole.Secondary : Ui1ButtonRole.Ghost);
            button.Modulate = expectedEligible ? Colors.White : new Color(1f, 1f, 1f, 0.38f);
        }
    }

    private void ShowConditionTooltip(Button button)
    {
        if (_uiText is null || _contentNames is null || !_conditionTooltips.TryGetValue(button, out var binding))
        {
            return;
        }

        string optionName = _contentNames.Resolve(binding.Key, GameContentKind.Relic);
        _tooltipHost.ShowStructuredText(
            button,
            optionName,
            _uiText.Get(Ui1TextKey.SearchAncientOptionConditionTooltipTitle),
            _uiText.Get(binding.ConditionTextKey));
    }
}
