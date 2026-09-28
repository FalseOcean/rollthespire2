using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Controls.Pickers;

/// <summary>
/// Main-thread-only visual ModelKey picker. Legal candidates are supplied by
/// production authority; local search/filter state never enters Search contracts.
/// </summary>
internal sealed partial class RelicPickerPanel : Control
{
    private const float CompactTileSize = 54f;
    private const float CompactGridHorizontalSeparation = 6f;
    private const float CardGridHorizontalSeparation = 9f;
    private const float CardGridVerticalSeparation = 9f;
    private const int CardStandardColumns = 5;
    private const int CardVisibleRows = 2;
    private const float ReservedScrollBarWidth = 18f;
    private const float CardViewportHeight =
        (RelicPickerTile.CardTileHeight * CardVisibleRows) +
        (CardGridVerticalSeparation * (CardVisibleRows - 1));
    private const float FilterButtonSize = 42f;

    private readonly IGameIconResolver _icons;
    private readonly ICharacterPoolIconProvider _characterIcons;
    private readonly ICardPickerFilterIconProvider _cardFilterIcons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly bool _ownsCharacterIcons;
    private readonly PanelContainer _dialog;
    private readonly Label _title;
    private readonly Button _close;
    private readonly LineEdit _search;
    private readonly HFlowContainer _categoryRow;
    private readonly HBoxContainer _cardFilterRow;
    private readonly ScrollContainer _characterFilterScroll;
    private readonly HBoxContainer _characterFilterButtons;
    private readonly HBoxContainer _rarityFilterButtons;
    private readonly HBoxContainer _typeFilterButtons;
    private readonly VSeparator _characterSeparator;
    private readonly VSeparator _raritySeparator;
    private readonly ScrollContainer _scroll;
    private readonly GridContainer _grid;
    private readonly Label _empty;
    private readonly Dictionary<RelicPickerCategory, Button> _categoryButtons = new();
    private readonly List<RelicPickerCandidate> _allCandidates = new();
    private readonly CardPickerFilterState _cardFilters = new();
    private HashSet<ModelKey> _selectedCharacters => _cardFilters.Characters;
    private HashSet<EffectCardRarity> _selectedRarities => _cardFilters.Rarities;
    private HashSet<EffectCardType> _selectedTypes => _cardFilters.Types;

    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private RelicPickerRequest? _request;
    private RelicPickerCategory? _activeCategory;
    private CardPickerContext? _cardContext;
    private bool _allCharactersSelected { get => _cardFilters.AllCharacters; set => _cardFilters.AllCharacters = value; }
    private float _cardTileWidth = RelicPickerTile.CardTileMaxWidth;

    public RelicPickerPanel(IGameIconResolver icons, AnchoredTooltipHost tooltipHost)
        : this(icons, new NativeCharacterPoolIconProvider(icons), new CardPickerFilterIconProvider(), tooltipHost)
    {
        _ownsCharacterIcons = true;
    }

    public RelicPickerPanel(
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterIcons,
        ICardPickerFilterIconProvider cardFilterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        _icons = icons;
        _characterIcons = characterIcons;
        _cardFilterIcons = cardFilterIcons;
        _ownsCharacterIcons = false;
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        Visible = false;
        ZIndex = UiZLayers.PickerModal;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        SetProcessUnhandledKeyInput(true);

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.66f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += OnBackdropInput;
        AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        _dialog = new PanelContainer
        {
            CustomMinimumSize = new Vector2(680, 520),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyPanel(_dialog, Ui1SurfaceRole.Drawer, 5f, 2, 16f);
        center.AddChild(_dialog);

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 10);

        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 8);
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _close = new Button { CustomMinimumSize = new Vector2(76, 36) };
        Ui1Theme.ApplyButton(_close, Ui1ButtonRole.Ghost);
        _close.Pressed += Cancel;
        header.AddChild(_title);
        header.AddChild(_close);
        body.AddChild(header);

        _search = new LineEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClearButtonEnabled = true
        };
        Ui1Theme.ApplyLineEdit(_search);
        _search.TextChanged += _ => RebuildGrid();
        body.AddChild(_search);

        _cardFilterRow = new HBoxContainer
        {
            Visible = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 46f)
        };
        _cardFilterRow.AddThemeConstantOverride("separation", 8);

        _characterFilterScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(250f, 46f)
        };
        _characterFilterButtons = FilterGroup();
        _characterFilterScroll.AddChild(_characterFilterButtons);
        _characterSeparator = new VSeparator();
        Ui1Theme.ApplySeparator(_characterSeparator);
        _rarityFilterButtons = FilterGroup();
        _raritySeparator = new VSeparator();
        Ui1Theme.ApplySeparator(_raritySeparator);
        _typeFilterButtons = FilterGroup();
        _cardFilterRow.AddChild(_characterFilterScroll);
        _cardFilterRow.AddChild(_characterSeparator);
        _cardFilterRow.AddChild(_rarityFilterButtons);
        _cardFilterRow.AddChild(_raritySeparator);
        _cardFilterRow.AddChild(_typeFilterButtons);
        body.AddChild(_cardFilterRow);

        _categoryRow = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };
        _categoryRow.AddThemeConstantOverride("h_separation", 6);
        _categoryRow.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(_categoryRow);

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            CustomMinimumSize = new Vector2(0, 330)
        };
        _grid = new GridContainer
        {
            Columns = 10,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _grid.AddThemeConstantOverride("h_separation", (int)CompactGridHorizontalSeparation);
        _grid.AddThemeConstantOverride("v_separation", 8);
        _scroll.AddChild(_grid);
        _scroll.Resized += QueueGridColumnRefresh;
        body.AddChild(_scroll);

        _empty = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Visible = false;
        body.AddChild(_empty);

        _dialog.AddChild(body);
        Resized += UpdateResponsiveLayout;
    }

    public bool IsOpen => Visible;

    /// <summary>
    /// Raised after a user-driven picker close (selection, close button, backdrop, or Escape).
    /// The picker is already hidden and any selected value has already been committed when
    /// this event fires. Tree teardown intentionally does not raise it.
    /// </summary>
    public event Action? Closed;

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text;
        _names = names;
        _close.Text = text.Get(Ui1TextKey.SearchNeowPickerClose);
        _search.PlaceholderText = text.Get(Ui1TextKey.SearchNeowPickerSearchPlaceholder);
        _empty.Text = text.Get(Ui1TextKey.SearchNeowPickerNoResults);
        if (Visible) RebuildAll();
    }

    public void Open(RelicPickerRequest request)
    {
        _tooltipHost.Dismiss();
        if (_text is null || _names is null || request.CandidateModelKeys.Count == 0) return;

        _request = request;
        _cardContext = request.ContentKind == GameContentKind.Card ? request.CardContext : null;
        _cardTileWidth = RelicPickerTile.CardTileMaxWidth;
        ApplyResultViewportLayout();
        _title.Text = request.Title;
        _search.Text = string.Empty;
        _activeCategory = null;
        _allCharactersSelected = true;
        _selectedCharacters.Clear();
        _selectedRarities.Clear();
        _selectedTypes.Clear();
        Visible = true;
        if (_cardContext is not null)
        {
            RuntimeLog.Detail(
                $"cardPickerOpened=true;source={_cardContext.SourceId};" +
                $"requestCandidates={request.CandidateModelKeys.Count};" +
                $"allowedCandidates={_cardContext.AllowedCardModelKeys.Count};" +
                $"metadata={_cardContext.CandidateMetadata.Count};" +
                $"characterFilters={_cardContext.AvailableCharacterKeys.Count};" +
                $"rarityFilters={_cardContext.AvailableRarities.Count};" +
                $"typeFilters={_cardContext.AvailableTypes.Count};");
        }
        RebuildAll();
        UpdateResponsiveLayout();
        _search.GrabFocus();
    }

    public void Cancel()
    {
        if (!Visible && _request is null) return;
        CloseCore();
        Closed?.Invoke();
    }

    private void CloseCore()
    {
        _tooltipHost.Dismiss();
        Visible = false;
        _request = null;
        _cardContext = null;
        _allCandidates.Clear();
        _activeCategory = null;
        _selectedCharacters.Clear();
        _selectedRarities.Clear();
        _selectedTypes.Clear();
        ClearChildren(_grid);
        ClearChildren(_categoryRow);
        ClearChildren(_characterFilterButtons);
        ClearChildren(_rarityFilterButtons);
        ClearChildren(_typeFilterButtons);
        _categoryButtons.Clear();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape) return;
        Cancel();
        GetViewport().SetInputAsHandled();
    }

    private void RebuildAll()
    {
        if (_request is null || _names is null) return;
        _allCandidates.Clear();
        var disabled = new HashSet<ModelKey>(_request.DisabledModelKeys, ModelKeyComparer.Instance);
        IEnumerable<ModelKey> candidateKeys = _request.CandidateModelKeys;
        if (_request.ContentKind == GameContentKind.Card && _cardContext is not null)
        {
            // AllowedCardModelKeys is the hard upper bound. Picker filters may
            // only reduce this intersection; they can never add metadata-only
            // or catalog-wide cards back into the result grid.
            candidateKeys = candidateKeys.Where(_cardContext.IsAllowed);
        }

        int presentationFallbackCount = 0;
        var presentationFallbackSamples = new List<string>(3);
        foreach (ModelKey key in candidateKeys.Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance))
        {
            _request.Categories.TryGetValue(key, out RelicPickerCategory category);
            string name;
            try
            {
                name = _names.Resolve(key, _request.ContentKind);
            }
            catch (Exception ex)
            {
                name = key.Serialized;
                presentationFallbackCount++;
                if (presentationFallbackSamples.Count < 3)
                {
                    presentationFallbackSamples.Add($"{key.Serialized}:name:{ex.GetType().Name}");
                }
            }

            IconVariant variant = _request.ContentKind == GameContentKind.Card
                ? IconVariant.CardPickerLarge
                : _request.IconVariant;
            IconDescriptor icon;
            try
            {
                icon = _icons.Resolve(key, _request.ContentKind, variant);
            }
            catch (Exception ex)
            {
                icon = IconDescriptor.Missing(
                    key,
                    _request.ContentKind,
                    variant,
                    $"picker-presentation-icon-resolve-failed:{ex.GetType().Name}");
                presentationFallbackCount++;
                if (presentationFallbackSamples.Count < 3)
                {
                    presentationFallbackSamples.Add($"{key.Serialized}:icon:{ex.GetType().Name}");
                }
            }

            bool selected = _request.SelectedModelKey == key;
            bool isDisabled = disabled.Contains(key) && !selected;
            _allCandidates.Add(new RelicPickerCandidate(
                key,
                name,
                icon.Texture,
                category,
                selected,
                isDisabled,
                isDisabled ? _text!.Get(Ui1TextKey.SearchNeowPickerAlreadyUsed) : string.Empty));
        }
        if (presentationFallbackCount > 0)
        {
            RuntimeLog.Warn(
                $"pickerCandidatePresentationFallback=true;kind={_request.ContentKind};" +
                $"count={presentationFallbackCount};samples={string.Join(",", presentationFallbackSamples)};");
        }
        _allCandidates.Sort((left, right) => string.Compare(left.LocalizedName, right.LocalizedName, StringComparison.CurrentCulture));
        RebuildCategories();
        RebuildCardFilters();
        RebuildGrid();
    }

    private void RebuildCategories()
    {
        ClearChildren(_categoryRow);
        _categoryButtons.Clear();
        if (_request?.ContentKind == GameContentKind.Card)
        {
            _categoryRow.Visible = false;
            _activeCategory = null;
            return;
        }

        RelicPickerCategory[] preferredOrder = _request?.ContentKind switch
        {
            GameContentKind.Relic => new[] { RelicPickerCategory.Common, RelicPickerCategory.Uncommon, RelicPickerCategory.Rare, RelicPickerCategory.Shop },
            GameContentKind.Potion => new[] { RelicPickerCategory.Common, RelicPickerCategory.Uncommon, RelicPickerCategory.Rare },
            _ => new[] { RelicPickerCategory.Common, RelicPickerCategory.Uncommon, RelicPickerCategory.Rare, RelicPickerCategory.Shop }
        };
        HashSet<RelicPickerCategory> present = _allCandidates.Select(candidate => candidate.Category).ToHashSet();
        RelicPickerCategory[] categories = preferredOrder.Where(present.Contains).ToArray();
        _categoryRow.Visible = categories.Length > 1;
        if (!_categoryRow.Visible)
        {
            _activeCategory = null;
            return;
        }

        AddCategoryButton(null, _text!.Get(Ui1TextKey.SearchNeowPickerAll));
        foreach (RelicPickerCategory category in categories) AddCategoryButton(category, CategoryText(category));
        RefreshCategoryButtons();
    }

    private void RebuildCardFilters()
    {
        ClearChildren(_characterFilterButtons);
        ClearChildren(_rarityFilterButtons);
        ClearChildren(_typeFilterButtons);
        if (_request?.ContentKind != GameContentKind.Card || _cardContext is null)
        {
            _cardFilterRow.Visible = false;
            return;
        }

        IReadOnlyList<ModelKey> characters = _cardContext.AvailableCharacterKeys;
        IReadOnlyList<EffectCardRarity> rarities = _cardContext.AvailableRarities;
        IReadOnlyList<EffectCardType> types = _cardContext.AvailableTypes;
        bool showCharacters = _cardContext.AllowCharacterFilter && characters.Count > 1;
        bool showRarities = rarities.Count > 1;
        bool showTypes = types.Count > 1;
        _cardFilterRow.Visible = showCharacters || showRarities || showTypes;
        _characterFilterScroll.Visible = showCharacters;
        _characterSeparator.Visible = showCharacters && (showRarities || showTypes);
        _rarityFilterButtons.Visible = showRarities;
        _raritySeparator.Visible = showRarities && showTypes;
        _typeFilterButtons.Visible = showTypes;

        if (showCharacters)
        {
            AddAllCharactersButton();
            foreach (ModelKey character in characters)
            {
                string name = _names!.Resolve(character, GameContentKind.Character);
                AddFilterButton(
                    _characterFilterButtons,
                    _characterIcons.Resolve(character).Texture,
                    name,
                    () => ToggleCharacter(character),
                    () => _selectedCharacters.Contains(character));
            }
        }

        if (showRarities)
        {
            foreach (EffectCardRarity rarity in rarities)
            {
                AddFilterButton(
                    _rarityFilterButtons,
                    _cardFilterIcons.ResolveRarity(rarity),
                    RarityText(rarity),
                    () => ToggleRarity(rarity),
                    () => _selectedRarities.Contains(rarity));
            }
        }

        if (showTypes)
        {
            foreach (EffectCardType type in types)
            {
                AddFilterButton(
                    _typeFilterButtons,
                    _cardFilterIcons.ResolveType(type),
                    TypeText(type),
                    () => ToggleType(type),
                    () => _selectedTypes.Contains(type));
            }
        }
    }

    private void AddAllCharactersButton()
    {
        AddFilterButton(
            _characterFilterButtons,
            _characterIcons.ResolveColorlessPoolVisual(),
            _text!.Get(Ui1TextKey.SearchCardPickerAllCharacters),
            () =>
            {
                _allCharactersSelected = true;
                _selectedCharacters.Clear();
                RebuildCardFilters();
                RebuildGrid();
            },
            () => _allCharactersSelected);
    }

    private void ToggleCharacter(ModelKey character)
    {
        _cardFilters.ToggleCharacter(character);
        RebuildCardFilters();
        RebuildGrid();
    }

    private void ToggleRarity(EffectCardRarity rarity)
    {
        _cardFilters.ToggleRarity(rarity);
        RebuildCardFilters();
        RebuildGrid();
    }

    private void ToggleType(EffectCardType type)
    {
        _cardFilters.ToggleType(type);
        RebuildCardFilters();
        RebuildGrid();
    }

    private void AddFilterButton(
        Container parent,
        Texture2D? texture,
        string tooltip,
        Action pressed,
        Func<bool> selected)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(FilterButtonSize, FilterButtonSize),
            FocusMode = FocusModeEnum.All,
            ClipContents = true,
            TooltipText = string.Empty
        };
        Ui1Theme.ApplyButton(button, selected() ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        button.Pressed += pressed;
        button.MouseEntered += () => _tooltipHost.ShowText(button, tooltip);
        button.MouseExited += () => _tooltipHost.Dismiss(button);
        button.TreeExiting += () => _tooltipHost.Dismiss(button);

        Texture2D? usableTexture = TryGetUsableTexture(texture);
        var icon = new TextureRect
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        if (usableTexture is not null)
        {
            try
            {
                icon.Texture = usableTexture;
                icon.Visible = true;
            }
            catch (ObjectDisposedException)
            {
                // Presentation-only visual failure. Keep the filter button and
                // fall back to '?' rather than aborting the picker modal.
                usableTexture = null;
            }
        }
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = 6f;
        icon.OffsetTop = 6f;
        icon.OffsetRight = -6f;
        icon.OffsetBottom = -6f;
        button.AddChild(icon);
        if (usableTexture is null)
        {
            var missing = Ui1Theme.Label("?", Ui1TextRole.Muted);
            missing.MouseFilter = MouseFilterEnum.Ignore;
            missing.HorizontalAlignment = HorizontalAlignment.Center;
            missing.VerticalAlignment = VerticalAlignment.Center;
            missing.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            button.AddChild(missing);
        }
        parent.AddChild(button);
    }

    private static Texture2D? TryGetUsableTexture(Texture2D? texture)
    {
        if (texture is null)
        {
            return null;
        }

        try
        {
            return GodotObject.IsInstanceValid(texture) ? texture : null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private void AddCategoryButton(RelicPickerCategory? category, string label)
    {
        var button = new Button { Text = label, CustomMinimumSize = new Vector2(78, 34) };
        button.Pressed += () =>
        {
            _activeCategory = category;
            RefreshCategoryButtons();
            RebuildGrid();
        };
        _categoryButtons[category ?? RelicPickerCategory.All] = button;
        _categoryRow.AddChild(button);
    }

    private void RefreshCategoryButtons()
    {
        foreach ((RelicPickerCategory category, Button button) in _categoryButtons)
        {
            bool selected = category == RelicPickerCategory.All ? !_activeCategory.HasValue : category == _activeCategory;
            Ui1Theme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Ghost);
        }
    }

    private string CategoryText(RelicPickerCategory category) => category switch
    {
        RelicPickerCategory.Common => _text!.Get(Ui1TextKey.SearchNeowPickerCommon),
        RelicPickerCategory.Uncommon => _text!.Get(Ui1TextKey.SearchNeowPickerUncommon),
        RelicPickerCategory.Rare => _text!.Get(Ui1TextKey.SearchNeowPickerRare),
        RelicPickerCategory.Shop => _text!.Get(Ui1TextKey.SearchNeowPickerShop),
        _ => category.ToString()
    };

    private void RebuildGrid()
    {
        _tooltipHost.Dismiss();
        ClearChildren(_grid);
        if (_request is null) return;
        string query = _search.Text.Trim();
        RelicPickerCandidate[] filtered = _allCandidates
            .Where(candidate => !_activeCategory.HasValue || candidate.Category == _activeCategory.Value)
            .Where(candidate => query.Length == 0 || candidate.LocalizedName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Where(CardFilterMatches)
            .ToArray();

        foreach (RelicPickerCandidate candidate in filtered)
        {
            var tile = new RelicPickerTile(
                candidate,
                _request.ContentKind,
                _tooltipHost,
                _cardTileWidth);
            tile.Pressed += () => Commit(candidate.ModelKey);
            _grid.AddChild(tile);
        }
        _empty.Visible = filtered.Length == 0;
        _scroll.Visible = filtered.Length > 0;
        if (filtered.Length == 0 && _request.ContentKind == GameContentKind.Card && _cardContext is not null)
        {
            RuntimeLog.Warn(
                $"cardPickerEmptyAfterFilters=true;source={_cardContext.SourceId};" +
                $"allCandidates={_allCandidates.Count};allowedCandidates={_cardContext.AllowedCardModelKeys.Count};" +
                $"metadata={_cardContext.CandidateMetadata.Count};" +
                $"searchLength={query.Length};allCharacters={_allCharactersSelected.ToString().ToLowerInvariant()};" +
                $"selectedCharacters={_selectedCharacters.Count};selectedRarities={_selectedRarities.Count};selectedTypes={_selectedTypes.Count};");
        }
        QueueGridColumnRefresh();
    }

    private bool CardFilterMatches(RelicPickerCandidate candidate)
    {
        if (_request?.ContentKind != GameContentKind.Card || _cardContext is null) return true;
        return _cardFilters.Matches(_cardContext, candidate.ModelKey);
    }

    private string RarityText(EffectCardRarity rarity) => rarity switch
    {
        EffectCardRarity.Common => _text!.Get(Ui1TextKey.SearchCardPickerCommon),
        EffectCardRarity.Uncommon => _text!.Get(Ui1TextKey.SearchCardPickerUncommon),
        EffectCardRarity.Rare => _text!.Get(Ui1TextKey.SearchCardPickerRare),
        _ => rarity.ToString()
    };

    private string TypeText(EffectCardType type) => type switch
    {
        EffectCardType.Attack => _text!.Get(Ui1TextKey.SearchCardPickerAttack),
        EffectCardType.Skill => _text!.Get(Ui1TextKey.SearchCardPickerSkill),
        EffectCardType.Power => _text!.Get(Ui1TextKey.SearchCardPickerPower),
        _ => type.ToString()
    };

    private void Commit(ModelKey? key)
    {
        RelicPickerRequest? request = _request;
        if (request?.ContentKind == GameContentKind.Card &&
            key.HasValue &&
            _cardContext is not null &&
            !_cardContext.IsAllowed(key.Value))
        {
            // Defense in depth for restored state or future UI regressions.
            // A card outside AllowedCardModelKeys is never returned.
            return;
        }

        CloseCore();
        request?.OnSelected(key);
        Closed?.Invoke();
    }

    private void UpdateResponsiveLayout()
    {
        float availableWidth = Math.Max(320f, Size.X - 28f);
        float availableHeight = Math.Max(320f, Size.Y - 28f);
        _dialog.CustomMinimumSize = new Vector2(
            Math.Clamp(availableWidth * 0.88f, 620f, 1040f),
            Math.Clamp(availableHeight * 0.88f, 480f, 760f));
        QueueGridColumnRefresh();
    }

    private void QueueGridColumnRefresh()
    {
        if (!IsInsideTree()) return;
        Callable.From(UpdateGridColumnsFromViewport).CallDeferred();
    }

    private void UpdateGridColumnsFromViewport()
    {
        if (!IsInsideTree() || _request is null) return;
        float renderedWidth = _scroll.Size.X;
        if (renderedWidth <= 1f) renderedWidth = Math.Max(_dialog.Size.X, _dialog.CustomMinimumSize.X) - 32f;
        float usableWidth = Math.Max(CompactTileSize, renderedWidth - ReservedScrollBarWidth);

        if (_request.ContentKind == GameContentKind.Card)
        {
            float minimumFiveColumnWidth =
                (RelicPickerTile.CardTileMinWidth * CardStandardColumns) +
                (CardGridHorizontalSeparation * (CardStandardColumns - 1));
            int columns = usableWidth >= minimumFiveColumnWidth
                ? CardStandardColumns
                : Math.Clamp(
                    (int)Math.Floor((usableWidth + CardGridHorizontalSeparation) /
                                    (RelicPickerTile.CardTileMinWidth + CardGridHorizontalSeparation)),
                    2,
                    CardStandardColumns);
            float nextTileWidth = Math.Clamp(
                (usableWidth - (CardGridHorizontalSeparation * (columns - 1))) / columns,
                RelicPickerTile.CardTileMinWidth,
                RelicPickerTile.CardTileMaxWidth);
            bool widthChanged = Math.Abs(nextTileWidth - _cardTileWidth) > 0.5f;
            _grid.Columns = columns;
            if (widthChanged)
            {
                _cardTileWidth = nextTileWidth;
                RebuildGrid();
            }
            return;
        }

        int compactColumns = (int)Math.Floor(
            (usableWidth + CompactGridHorizontalSeparation) /
            (CompactTileSize + CompactGridHorizontalSeparation));
        _grid.Columns = Math.Clamp(compactColumns, 4, 18);
    }

    private void ApplyResultViewportLayout()
    {
        bool cardMode = _request?.ContentKind == GameContentKind.Card;
        _scroll.SizeFlagsVertical = cardMode ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill;
        _scroll.HorizontalScrollMode = cardMode
            ? ScrollContainer.ScrollMode.Disabled
            : ScrollContainer.ScrollMode.Auto;
        _scroll.CustomMinimumSize = new Vector2(0f, cardMode ? CardViewportHeight : 330f);
        _grid.SizeFlagsHorizontal = cardMode ? SizeFlags.ShrinkCenter : SizeFlags.ExpandFill;
        _grid.AddThemeConstantOverride(
            "h_separation",
            (int)(cardMode ? CardGridHorizontalSeparation : CompactGridHorizontalSeparation));
        _grid.AddThemeConstantOverride("v_separation", cardMode ? (int)CardGridVerticalSeparation : 8);
    }

    private void OnBackdropInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }


    public override void _ExitTree()
    {
        CloseCore();
        if (_ownsCharacterIcons)
        {
            _characterIcons.Dispose();
        }
    }

    private static HBoxContainer FilterGroup()
    {
        var group = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        group.AddThemeConstantOverride("separation", 5);
        return group;
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
