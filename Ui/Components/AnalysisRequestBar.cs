using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Single-line Predictor context toolbar. Seed text is committed reactively only
/// when the current runtime profile recognizes a complete legal seed. Intermediate
/// edit states remain local and do not invoke prediction.
/// </summary>
internal sealed partial class AnalysisRequestBar : PanelContainer
{
    private const int CharacterOverflowThreshold = 7;
    private readonly IRuntimeProfile _seedProfile;
    private readonly Label _seedLabel;
    private readonly LineEdit _seedInput;
    private readonly Button _copySeed;
    private readonly Button _randomSeed;
    private readonly Label _characterLabel;
    private readonly CharacterPoolIconSelector _characterPicker;
    private readonly Label _ascensionLabel;
    private readonly AscensionStepper _ascension;
    private readonly Label _unlockLabel;
    private readonly UnlockSummary _unlockSummary;
    private readonly Label _partySummary;
    private IUiTextProvider? _uiText;
    private bool? _allCharacterCardPoolsUnlocked;
    private string _committedSeed;
    private int _playersCount = SeedPredictionInputLimits.MinimumPlayers;
    private int _playerSlotIndex;
    private bool _suppressSeedSignals;

    public AnalysisRequestBar(
        string initialSeed,
        IRuntimeProfile seedProfile,
        ICharacterPoolIconProvider characterPoolIcons,
        IReadOnlyList<ModelKey> characterKeys)
    {
        _seedProfile = seedProfile ?? throw new ArgumentNullException(nameof(seedProfile));
        _committedSeed = initialSeed.Trim();
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 4f, 1, 9f);

        var toolbar = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        toolbar.AddThemeConstantOverride("separation", 9);

        _seedLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _seedInput = new LineEdit
        {
            Text = initialSeed,
            CustomMinimumSize = new Vector2(270, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyLineEdit(_seedInput);
        _copySeed = new Button
        {
            CustomMinimumSize = new Vector2(76, 38),
            Disabled = string.IsNullOrWhiteSpace(initialSeed)
        };
        Ui1Theme.ApplyButton(_copySeed, Ui1ButtonRole.Secondary);
        _randomSeed = new Button { CustomMinimumSize = new Vector2(76, 38) };
        Ui1Theme.ApplyButton(_randomSeed, Ui1ButtonRole.Secondary);

        _characterLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _characterPicker = new CharacterPoolIconSelector(characterKeys, characterPoolIcons);
        Control characterHost;
        if (_characterPicker.RequiresHorizontalOverflow(CharacterOverflowThreshold))
        {
            var characterScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(
                    _characterPicker.PreferredViewportWidth(CharacterOverflowThreshold),
                    48),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            characterScroll.AddChild(_characterPicker);
            characterHost = characterScroll;
        }
        else
        {
            characterHost = _characterPicker;
        }

        _ascensionLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _ascension = new AscensionStepper();
        _ascension.Configure(
            SeedPredictionInputLimits.MinimumAscension,
            SeedPredictionInputLimits.MaximumAscension,
            SeedPredictionInputLimits.MaximumAscension);
        _unlockLabel = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _unlockSummary = new UnlockSummary();
        _partySummary = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted);
        _partySummary.Visible = false;

        toolbar.AddChild(_seedLabel);
        toolbar.AddChild(_seedInput);
        toolbar.AddChild(_copySeed);
        toolbar.AddChild(_randomSeed);
        toolbar.AddChild(NewGroupGap());
        toolbar.AddChild(_characterLabel);
        toolbar.AddChild(characterHost);
        toolbar.AddChild(NewGroupGap());
        toolbar.AddChild(_ascensionLabel);
        toolbar.AddChild(_ascension);
        toolbar.AddChild(NewGroupGap());
        toolbar.AddChild(_unlockLabel);
        toolbar.AddChild(_unlockSummary);
        toolbar.AddChild(_partySummary);
        toolbar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        AddChild(toolbar);

        _seedInput.TextChanged += HandleSeedTextChanged;
        _copySeed.Pressed += () => CopySeedRequested?.Invoke(_seedInput.Text?.Trim() ?? string.Empty);
        _randomSeed.Pressed += () => RandomSeedRequested?.Invoke();
        _characterPicker.SelectionChanged += _ => ContextChanged?.Invoke(CurrentReactiveDraft);
        _ascension.ValueChanged += _ => ContextChanged?.Invoke(CurrentReactiveDraft);
        _characterPicker.Select(BaseGameModelKeys.Characters.Silent, notify: false);
    }

    public event Action<AnalysisRequestDraft>? SubmitRequested;
    public event Action<AnalysisRequestDraft>? ContextChanged;
    public event Action<string>? CopySeedRequested;
    public event Action? RandomSeedRequested;

    public AnalysisRequestDraft CurrentDraft => BuildDraft(_committedSeed);
    public AnalysisRequestDraft CurrentReactiveDraft => BuildDraft(_committedSeed);
    public string CommittedSeed => _committedSeed;
    public bool? AllCharacterCardPoolsUnlocked => _allCharacterCardPoolsUnlocked;

    public void ApplyLocalization(
        IUiTextProvider uiText,
        IGameContentNameResolver contentNames,
        string missingIconTooltip)
    {
        _uiText = uiText;
        _seedLabel.Text = uiText.Get(Ui1TextKey.Seed);
        _seedInput.PlaceholderText = uiText.Get(UiTextKey.SeedPlaceholder);
        _copySeed.Text = uiText.Get(Ui1TextKey.CopySeed);
        _randomSeed.Text = uiText.Get(Ui1TextKey.RandomSeed);
        _characterLabel.Text = uiText.Get(Ui1TextKey.Character);
        _ascensionLabel.Text = uiText.Get(Ui1TextKey.Ascension);
        _unlockLabel.Text = uiText.Get(Ui1TextKey.UnlockContext);
        UpdateUnlockSummary();
        UpdatePartySummary();
        _characterPicker.Build(_characterPicker.SelectedKey, contentNames, missingIconTooltip);
    }

    public void SetUnlockState(bool? allCharacterCardPoolsUnlocked)
    {
        _allCharacterCardPoolsUnlocked = allCharacterCardPoolsUnlocked;
        UpdateUnlockSummary();
    }

    public void SetSeedText(string seed, bool commit)
    {
        string value = seed ?? string.Empty;
        if (commit && _seedProfile.TryCanonicalizeSeed(value, out string canonical, out _))
        {
            value = canonical;
            _committedSeed = canonical;
        }
        else if (commit)
        {
            _committedSeed = value.Trim();
        }
        SetSeedInputWithoutSignal(value);
        _copySeed.Disabled = string.IsNullOrWhiteSpace(value);
    }

    public void CommitSeed(string seed)
    {
        string value = seed?.Trim() ?? string.Empty;
        if (_seedProfile.TryCanonicalizeSeed(value, out string canonical, out _))
        {
            value = canonical;
        }
        _committedSeed = value;
        SetSeedInputWithoutSignal(value);
        _copySeed.Disabled = string.IsNullOrWhiteSpace(value);
    }

    public void SetContext(
        ModelKey characterKey,
        int ascension,
        int playersCount,
        int playerSlotIndex,
        bool notify)
    {
        _playersCount = Math.Clamp(playersCount, SeedPredictionInputLimits.MinimumPlayers, SeedPredictionInputLimits.MaximumPlayers);
        _playerSlotIndex = Math.Clamp(playerSlotIndex, 0, Math.Max(0, _playersCount - 1));
        _characterPicker.Select(_characterPicker.ResolveAvailableSelection(characterKey), notify: false);
        _ascension.SetValue(ascension, notify: false);
        UpdatePartySummary();
        if (notify)
        {
            ContextChanged?.Invoke(CurrentReactiveDraft);
        }
    }

    public void SetBusy(bool busy)
    {
        _copySeed.Disabled = string.IsNullOrWhiteSpace(_seedInput.Text);
        _randomSeed.Disabled = busy;
        _seedInput.Editable = !busy;
    }

    private void HandleSeedTextChanged(string text)
    {
        if (_suppressSeedSignals)
        {
            return;
        }
        _copySeed.Disabled = string.IsNullOrWhiteSpace(text);
        if (!_seedProfile.TryCanonicalizeSeed(text, out string canonical, out _))
        {
            return;
        }
        if (string.Equals(canonical, _committedSeed, StringComparison.Ordinal))
        {
            return;
        }

        _committedSeed = canonical;
        if (!string.Equals(text, canonical, StringComparison.Ordinal))
        {
            SetSeedInputWithoutSignal(canonical);
        }
        SubmitRequested?.Invoke(BuildDraft(canonical));
    }

    private void SetSeedInputWithoutSignal(string value)
    {
        _suppressSeedSignals = true;
        try
        {
            _seedInput.Text = value;
            _seedInput.CaretColumn = value.Length;
        }
        finally
        {
            _suppressSeedSignals = false;
        }
    }

    private AnalysisRequestDraft BuildDraft(string seed) => new(
        seed,
        _characterPicker.SelectedKey,
        _ascension.Value,
        _playersCount,
        _playerSlotIndex);

    private void UpdateUnlockSummary()
    {
        if (_uiText is null)
        {
            return;
        }

        string text = _allCharacterCardPoolsUnlocked switch
        {
            true => _uiText.Get(Ui1TextKey.UnlockFull),
            false => _uiText.Get(Ui1TextKey.UnlockPartial),
            null => _uiText.Get(Ui1TextKey.UnlockUnknown)
        };
        _unlockSummary.Bind(text, _uiText.Get(Ui1TextKey.UnlockTooltip), _allCharacterCardPoolsUnlocked);
    }

    private void UpdatePartySummary()
    {
        if (_playersCount <= 1)
        {
            _partySummary.Visible = false;
            _partySummary.Text = string.Empty;
            return;
        }
        _partySummary.Visible = true;
        _partySummary.Text = _uiText is null
            ? $"P{_playerSlotIndex + 1}/{_playersCount}"
            : _uiText.Format(Ui1TextKey.AnalysisPlayerContext, _playerSlotIndex + 1, _playersCount);
    }

    private static Control NewGroupGap() => new()
    {
        CustomMinimumSize = new Vector2(10f, 0f),
        MouseFilter = Control.MouseFilterEnum.Ignore
    };
}
