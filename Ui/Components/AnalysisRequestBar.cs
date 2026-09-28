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
    private readonly ICharacterPoolIconProvider _characterIcons;
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
    private OptionButton? _railCharacter;
    private ModelKey[] _railKeys = [];
    private Label? _railTitle;
    private Button? _railSolo, _railMulti;
    private OptionButton? _partyPlayer;
    private Button? _partyConfig;
    internal event Action<int>? PartyPlayerSelected;
    internal event Action<bool>? PartyModeSelected;
    internal event Action? PartyConfigurationRequested;
    internal void ConfigureParty(int count)
    {
        if (_partyPlayer is null) return;
        _partyPlayer.Clear(); _partyPlayer.Visible = count > 1;
        if (_partyConfig is not null) _partyConfig.Visible = count > 1;
        for (int slot = 0; slot < count; slot++) _partyPlayer.AddItem($"P{slot + 1}");
        _partyPlayer.Select(Math.Clamp(_playerSlotIndex, 0, Math.Max(0, count - 1)));
    }

    internal void SetPartyOpeningChoices(IReadOnlyDictionary<int, string> names)
    {
        if (_partyPlayer is null) return;
        for (int slot = 0; slot < _partyPlayer.ItemCount; slot++)
            _partyPlayer.SetItemText(slot, names.TryGetValue(slot, out var name) ? $"P{slot + 1} · {name}" : $"P{slot + 1}");
    }

    internal void SetCurrentPartyOpeningChoice(string name)
    {
        if (_partyPlayer is null || _playersCount <= 1) return;
        _partyPlayer.SetItemText(_playerSlotIndex, $"P{_playerSlotIndex + 1} · {name}");
        for (int slot = _playerSlotIndex + 1; slot < _partyPlayer.ItemCount; slot++)
            _partyPlayer.SetItemText(slot, $"P{slot + 1}");
    }

    internal void UseRail(IReadOnlyList<ModelKey> keys)
    {
        var old = GetChild<Control>(0); old.Hide();
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12); AddChild(column);
        _railTitle = new Label(); column.AddChild(_railTitle);
        var mode = new HBoxContainer(); column.AddChild(mode);
        var palette = WorkspacePalette.Canonical;
        _railSolo = palette.Button(""); _railMulti = palette.Button("");
        _railSolo.AddThemeFontSizeOverride("font_size", 18); _railMulti.AddThemeFontSizeOverride("font_size", 18);
        _railSolo.SizeFlagsHorizontal = _railMulti.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _railSolo.Pressed += () => PartyModeSelected?.Invoke(false);
        _railMulti.Pressed += () => PartyModeSelected?.Invoke(true);
        palette.SetActive(_railSolo, true);
        mode.AddChild(_railSolo); mode.AddChild(_railMulti);
        _partyConfig = palette.Button(""); _partyConfig.Name = "PredictorPartyConfiguration";
        _partyConfig.Visible = false;
        _partyConfig.Pressed += () => PartyConfigurationRequested?.Invoke();
        column.AddChild(_partyConfig);
        _partyPlayer = new OptionButton { Visible = false, FitToLongestItem = false, CustomMinimumSize = new Vector2(0, 40) };
        _partyPlayer.ItemSelected += index => PartyPlayerSelected?.Invoke((int)index);
        column.AddChild(_partyPlayer);
        void Move(Control c, Node target) { c.GetParent().RemoveChild(c); target.AddChild(c); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; }
        Move(_seedLabel, column); Move(_seedInput, column);
        _seedInput.CustomMinimumSize = new Vector2(0, 40); _seedInput.ExpandToTextLength = false;
        var actions = new HBoxContainer(); column.AddChild(actions);
        Move(_copySeed, actions); Move(_randomSeed, actions);
        Move(_characterLabel, column);
        _railKeys = keys.ToArray(); _railCharacter = new OptionButton { CustomMinimumSize = new Vector2(0, 40), FitToLongestItem = false };
        _railCharacter.ExpandIcon = true;
        _railCharacter.AddThemeConstantOverride("icon_max_width", 28);
        column.AddChild(_railCharacter);
        _railCharacter.ItemSelected += i => _characterPicker.Select(_railKeys[(int)i], notify: true);
        Move(_ascensionLabel, column); Move(_ascension, column);
        Move(_unlockLabel, column); Move(_unlockSummary, column); Move(_partySummary, column);
    }

    public AnalysisRequestBar(
        string initialSeed,
        IRuntimeProfile seedProfile,
        ICharacterPoolIconProvider characterPoolIcons,
        IReadOnlyList<ModelKey> characterKeys)
    {
        _seedProfile = seedProfile ?? throw new ArgumentNullException(nameof(seedProfile));
        _characterIcons = characterPoolIcons;
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
        _partySummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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
    public event Action? InvalidSeedEdited;
    public event Action? CommittedSeedRestored;

    public AnalysisRequestDraft CurrentDraft => BuildDraft(_committedSeed);
    public AnalysisRequestDraft CurrentReactiveDraft => BuildDraft(_committedSeed);
    public string CommittedSeed => _committedSeed;
    internal bool HasValidVisibleSeed => _seedProfile.TryCanonicalizeSeed(_seedInput.Text, out var canonical, out _) && canonical == _committedSeed;
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
        if (_railCharacter is not null)
        {
            _railTitle!.Text = uiText.Get("predictor.context");
            _railSolo!.Text = uiText.Get("predictor.solo"); _railMulti!.Text = uiText.Get("predictor.multiplayer");
            _partyConfig!.Text = uiText.Get("predictor.party.configure");
            _railCharacter.Clear();
            foreach (var key in _railKeys) _railCharacter.AddIconItem(_characterIcons.Resolve(key).Texture, contentNames.Resolve(key, GameContentKind.Character));
            _railCharacter.Select(Math.Max(0, Array.IndexOf(_railKeys, _characterPicker.SelectedKey)));
        }
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
        if (_partyPlayer is { ItemCount: > 0 }) _partyPlayer.Select(Math.Min(_playerSlotIndex, _partyPlayer.ItemCount - 1));
        _characterPicker.Select(_characterPicker.ResolveAvailableSelection(characterKey), notify: false);
        if (_railCharacter is { ItemCount: > 0 })
            _railCharacter.Select(Math.Max(0, Array.IndexOf(_railKeys, _characterPicker.SelectedKey)));
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
            InvalidSeedEdited?.Invoke();
            return;
        }
        if (string.Equals(canonical, _committedSeed, StringComparison.Ordinal))
        {
            CommittedSeedRestored?.Invoke();
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
        if (_railSolo is not null)
        {
            var palette = WorkspacePalette.Canonical;
            palette.SetActive(_railSolo, _playersCount == 1);
            palette.SetActive(_railMulti!, _playersCount > 1);
        }
        if (_playersCount <= 1)
        {
            _partySummary.Visible = false;
            _partySummary.Text = string.Empty;
            return;
        }
        _partySummary.Visible = true;
        _partySummary.Text = _uiText is null
            ? $"P{_playerSlotIndex + 1}/{_playersCount}"
            : _uiText.Format(Ui1TextKey.AnalysisPlayerContext, _playerSlotIndex + 1, _playersCount)
                + "\n" + _uiText.Get("predictor.party.order");
    }

    private static Control NewGroupGap() => new()
    {
        CustomMinimumSize = new Vector2(10f, 0f),
        MouseFilter = Control.MouseFilterEnum.Ignore
    };
}
