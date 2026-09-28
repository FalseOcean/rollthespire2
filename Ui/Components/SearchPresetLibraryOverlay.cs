using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Retained Search-page Preset Workspace. Browsing and selection never mutate the
/// active Search query. Official / Temporary / My Presets are fixed ownership roots;
/// only explicit source-specific actions commit business operations.
/// </summary>
internal sealed partial class SearchPresetLibraryOverlay : Control
{
    private const float DialogWidth = 1120f;
    private const float DialogHeight = 690f;
    private readonly IGameIconResolver _icons;
    private readonly AnchoredTooltipHost _tooltipHost;
    private readonly PanelContainer _dialog;
    private readonly Label _title;
    private readonly Button _close;
    private readonly Button _createCurrent;
    private readonly VBoxContainer _list;
    private readonly Dictionary<SearchPresetSource, bool> _expanded = new()
    {
        [SearchPresetSource.BuiltIn] = true,
        [SearchPresetSource.Temporary] = true,
        [SearchPresetSource.User] = true
    };

    private readonly HBoxContainer _detailIdentity;
    private readonly HBoxContainer _detailIcons;
    private readonly Label _detailName;
    private readonly Button _editInfo;
    private readonly Label _detailDescription;
    private readonly Label _detailContext;
    private readonly Label _detailSource;
    private readonly Label _compatibilityNotice;
    private readonly Label _probabilityTitle;
    private readonly Label _probabilityValue;
    private readonly VBoxContainer _summary;
    private readonly Button _saveAsUser;
    private readonly Button _updateCurrent;
    private readonly Button _share;
    private readonly Button _delete;
    private readonly Button _cancel;
    private readonly Button _load;

    private IUiTextProvider? _text;
    private IUiTextProvider? _workbenchText;
    private IGameContentNameResolver? _names;
    private IReadOnlyList<SearchPresetDefinition> _entries = Array.Empty<SearchPresetDefinition>();
    private string _selectedId = string.Empty;

    public SearchPresetLibraryOverlay(IGameIconResolver icons, AnchoredTooltipHost tooltipHost)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _tooltipHost = tooltipHost ?? throw new ArgumentNullException(nameof(tooltipHost));
        Visible = false;
        ZIndex = UiZLayers.WorkspaceOverlay;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        SetProcessUnhandledKeyInput(true);

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.68f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        _dialog = new PanelContainer
        {
            CustomMinimumSize = new Vector2(DialogWidth, DialogHeight),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        Ui1Theme.ApplyPanel(_dialog, Ui1SurfaceRole.Drawer, 5f, 2, 16f);
        center.AddChild(_dialog);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);

        var header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _close = new Button { Text = "×", CustomMinimumSize = new Vector2(42f, 34f) };
        Ui1Theme.ApplyButton(_close, Ui1ButtonRole.Ghost);
        _close.Pressed += Cancel;
        _createCurrent = new Button { CustomMinimumSize = new Vector2(156f, 34f), Visible = false };
        Ui1Theme.ApplyButton(_createCurrent, Ui1ButtonRole.Secondary);
        _createCurrent.Pressed += () => CreateCurrentRequested?.Invoke();
        header.AddChild(_title);
        header.AddChild(_createCurrent);
        header.AddChild(_close);
        root.AddChild(header);

        var content = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 14);

        var listPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(390f, 0f),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(listPanel, Ui1SurfaceRole.Input, 4f, 1, 10f);
        var listScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _list.AddThemeConstantOverride("separation", 5);
        listScroll.AddChild(_list);
        listPanel.AddChild(listScroll);
        content.AddChild(listPanel);

        var detailPanel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        Ui1Theme.ApplyPanel(detailPanel, Ui1SurfaceRole.Card, 4f, 1, 14f);
        var detailScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        var detail = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        detail.AddThemeConstantOverride("separation", 10);

        _detailIdentity = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detailIdentity.AddThemeConstantOverride("separation", 10);
        _detailIcons = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _detailIcons.AddThemeConstantOverride("separation", 5);
        var identityText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detailName = Ui1Theme.Label(string.Empty, Ui1TextRole.CardTitle, true);
        _detailContext = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _detailSource = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        identityText.AddChild(_detailName);
        identityText.AddChild(_detailContext);
        identityText.AddChild(_detailSource);
        _editInfo = new Button { CustomMinimumSize = new Vector2(108f, 32f) };
        Ui1Theme.ApplyButton(_editInfo, Ui1ButtonRole.Ghost);
        _editInfo.Pressed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedId)) EditMetadataRequested?.Invoke(_selectedId);
        };
        _detailIdentity.AddChild(_detailIcons);
        _detailIdentity.AddChild(identityText);
        _detailIdentity.AddChild(_editInfo);
        detail.AddChild(_detailIdentity);

        _detailDescription = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        _detailDescription.Visible = false;
        detail.AddChild(_detailDescription);

        _compatibilityNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _compatibilityNotice.Visible = false;
        detail.AddChild(_compatibilityNotice);

        _probabilityTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        _probabilityValue = Ui1Theme.Label(string.Empty, Ui1TextRole.Body, true);
        detail.AddChild(_probabilityTitle);
        detail.AddChild(_probabilityValue);

        var summaryTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta);
        summaryTitle.Name = "PresetSummaryTitle";
        _summary = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _summary.AddThemeConstantOverride("separation", 8);
        detail.AddChild(summaryTitle);
        detail.AddChild(_summary);
        detailScroll.AddChild(detail);
        detailPanel.AddChild(detailScroll);
        content.AddChild(detailPanel);
        root.AddChild(content);

        var footer = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _delete = new Button { CustomMinimumSize = new Vector2(104f, 34f) };
        _share = new Button { CustomMinimumSize = new Vector2(104f, 34f), Disabled = true };
        _saveAsUser = new Button { CustomMinimumSize = new Vector2(154f, 34f) };
        _updateCurrent = new Button { CustomMinimumSize = new Vector2(156f, 34f), Visible = false };
        _cancel = new Button { CustomMinimumSize = new Vector2(92f, 34f) };
        _load = new Button { CustomMinimumSize = new Vector2(104f, 34f) };
        Ui1Theme.ApplyButton(_delete, Ui1ButtonRole.Danger);
        Ui1Theme.ApplyButton(_share, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(_saveAsUser, Ui1ButtonRole.Secondary);
        Ui1Theme.ApplyButton(_updateCurrent, Ui1ButtonRole.Secondary);
        Ui1Theme.ApplyButton(_cancel, Ui1ButtonRole.Ghost);
        Ui1Theme.ApplyButton(_load, Ui1ButtonRole.Primary);
        _delete.Pressed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedId)) DeleteRequested?.Invoke(_selectedId);
        };
        _saveAsUser.Pressed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedId)) SaveAsUserRequested?.Invoke(_selectedId);
        };
        _updateCurrent.Pressed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedId)) UpdateCurrentRequested?.Invoke(_selectedId);
        };
        _cancel.Pressed += Cancel;
        _load.Pressed += () =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedId)) LoadRequested?.Invoke(_selectedId);
        };
        footer.AddChild(_delete);
        footer.AddChild(_share);
        footer.AddChild(_saveAsUser);
        footer.AddChild(_updateCurrent);
        footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        footer.AddChild(_cancel);
        footer.AddChild(_load);
        root.AddChild(footer);
        _dialog.AddChild(root);
    }

    public event Action<string>? LoadRequested;
    public event Action<string>? SaveAsUserRequested;
    public event Action<string>? EditMetadataRequested;
    public event Action<string>? DeleteRequested;
    public event Action? CreateCurrentRequested;
    public event Action<string>? UpdateCurrentRequested;
    public event Action? Cancelled;
    public bool IsOpen => Visible;
    public bool WorkbenchMode { get; set; }

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _workbenchText = JsonUiTextProvider.CreateUi13(text.LanguageCode);
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _title.Text = text.Get(Ui1TextKey.SearchPresetLibraryTitle);
        _editInfo.Text = text.Get(Ui1TextKey.SearchPresetEditInfo);
        _delete.Text = text.Get(Ui1TextKey.SearchPresetDelete);
        _share.Text = text.Get(Ui1TextKey.SearchPresetShare);
        _share.TooltipText = text.Get(Ui1TextKey.SearchPresetShareUnavailable);
        _saveAsUser.Text = text.Get(Ui1TextKey.SearchPresetSaveAsMine);
        _createCurrent.Text = Local("保存当前条件", "Save current conditions");
        _updateCurrent.Text = Local("用当前条件更新", "Update from current");
        _updateCurrent.TooltipText = Local("以当前工作台的完整条件替换此预设；保存前需确认。", "Replace this preset with the current workbench conditions after confirmation.");
        _cancel.Text = text.Get(Ui1TextKey.SearchPresetCancel);
        _load.Text = text.Get(Ui1TextKey.SearchPresetLoadConfirm);
        _probabilityTitle.Text = text.Get(Ui1TextKey.SearchPresetSavedProbability);
        if (FindChild("PresetSummaryTitle", true, false) is Label summaryTitle)
            summaryTitle.Text = text.Get(Ui1TextKey.SearchPresetConditionSummary);
        RebuildList();
        RefreshDetail();
    }

    public void Open(IReadOnlyList<SearchPresetDefinition> entries)
    {
        _entries = entries ?? Array.Empty<SearchPresetDefinition>();
        _createCurrent.Visible = WorkbenchMode;
        EnsureVisibleSelection();
        RebuildList();
        RefreshDetail();
        Visible = true;
        GrabFocus();
    }

    public void RefreshEntries(IReadOnlyList<SearchPresetDefinition> entries)
    {
        _entries = entries ?? Array.Empty<SearchPresetDefinition>();
        EnsureVisibleSelection();
        RebuildList();
        RefreshDetail();
    }

    public void ShowIssue(string message)
    {
        _compatibilityNotice.Text = message ?? string.Empty;
        _compatibilityNotice.TooltipText = string.Empty;
        _compatibilityNotice.Visible = !string.IsNullOrWhiteSpace(message);
    }

    public void Cancel()
    {
        if (!Visible) return;
        _tooltipHost.Dismiss();
        Visible = false;
        Cancelled?.Invoke();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void EnsureVisibleSelection()
    {
        bool selectedVisible = _entries.Any(IsVisibleAndSelected);
        if (selectedVisible) return;
        _selectedId = VisibleEntries().FirstOrDefault()?.Id ?? string.Empty;
    }

    private bool IsVisibleAndSelected(SearchPresetDefinition preset) =>
        string.Equals(preset.Id, _selectedId, StringComparison.Ordinal) && IsSourceVisible(preset.Source);

    private IEnumerable<SearchPresetDefinition> VisibleEntries() =>
        _entries.Where(entry => IsSourceVisible(entry.Source));

    private static bool IsSourceVisible(SearchPresetSource source) =>
        source is SearchPresetSource.BuiltIn or SearchPresetSource.Temporary or SearchPresetSource.User;

    private void RebuildList()
    {
        foreach (Node child in _list.GetChildren()) child.QueueFree();
        IUiTextProvider? text = _text;
        if (text is null) return;
        AddSection(SearchPresetSource.BuiltIn, text.Get(Ui1TextKey.SearchPresetOfficialSection), text);
        AddSection(SearchPresetSource.Temporary, text.Get(Ui1TextKey.SearchPresetTemporarySection), text);
        AddSection(SearchPresetSource.User, text.Get(Ui1TextKey.SearchPresetUserSection), text);
    }

    private void AddSection(SearchPresetSource source, string title, IUiTextProvider text)
    {
        var rootButton = new Button
        {
            ToggleMode = true,
            ButtonPressed = _expanded[source],
            Text = (_expanded[source] ? "▾ " : "▸ ") + title,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0f, 34f)
        };
        Ui1Theme.ApplyButton(rootButton, Ui1ButtonRole.Ghost);
        rootButton.Pressed += () =>
        {
            _expanded[source] = rootButton.ButtonPressed;
            RebuildList();
        };
        _list.AddChild(rootButton);
        if (!_expanded[source]) return;

        SearchPresetDefinition[] items = _entries
            .Where(entry => entry.Source == source)
            .OrderByDescending(entry => source == SearchPresetSource.Temporary ? entry.CreatedAtUtc : DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (items.Length == 0)
        {
            _list.AddChild(Ui1Theme.Label(text.Get(Ui1TextKey.SearchPresetNone), Ui1TextRole.Muted, true));
            return;
        }

        foreach (SearchPresetDefinition preset in items)
        {
            string label = source == SearchPresetSource.Temporary
                ? FormatTemporaryIdentity(preset)
                : preset.Title;
            var button = new PresetListItemButton(_icons, _tooltipHost)
            {
                ButtonPressed = string.Equals(_selectedId, preset.Id, StringComparison.Ordinal)
            };
            button.Bind(label, FormatListContext(preset), preset.VisualIcons, _names);
            button.Pressed += () =>
            {
                _selectedId = preset.Id;
                RebuildList();
                RefreshDetail();
            };
            _list.AddChild(button);
        }
    }

    private void RefreshDetail()
    {
        foreach (Node child in _summary.GetChildren()) child.QueueFree();
        foreach (Node child in _detailIcons.GetChildren()) child.QueueFree();
        SearchPresetDefinition? preset = _entries.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedId, StringComparison.Ordinal));
        bool valid = preset is not null;
        SearchPresetLoadResolution? loadResolution = valid && !preset!.IsWorkbench
            ? SearchPresetCompatibilityResolver.Resolve(preset!, RuntimeAuthorityEnvironment.Current.Authority)
            : null;
        SearchPresetWorkbenchLoadResolution? workbenchResolution = valid && preset!.IsWorkbench && WorkbenchMode
            ? SearchPresetCompatibilityResolver.ResolveWorkbench(preset, RuntimeAuthorityEnvironment.Current.Authority)
            : null;
        bool canLoad = workbenchResolution?.CanLoad == true || loadResolution?.CanLoad == true &&
            (!WorkbenchMode || loadResolution.Kind == SearchPresetLoadResolutionKind.Full);
        _load.Disabled = !canLoad;
        _delete.Visible = valid && preset!.Source == SearchPresetSource.User;
        _share.Visible = !WorkbenchMode && valid && preset!.Source == SearchPresetSource.User;
        _editInfo.Visible = valid && preset!.Source == SearchPresetSource.User;
        _saveAsUser.Visible = valid && (WorkbenchMode || preset!.Source == SearchPresetSource.Temporary);
        _saveAsUser.Disabled = WorkbenchMode && !canLoad;
        _saveAsUser.Text = WorkbenchMode ? Local("另存为我的预设", "Save as my preset") : _text?.Get(Ui1TextKey.SearchPresetSaveAsMine) ?? string.Empty;
        _updateCurrent.Visible = WorkbenchMode && valid && preset!.Source == SearchPresetSource.User;
        // An unknown payload must remain intact until it can be resolved by a compatible version.
        _updateCurrent.Disabled = !canLoad;

        if (!valid || _text is null || _names is null)
        {
            _detailName.Text = _text?.Get(Ui1TextKey.SearchPresetSelectPrompt) ?? string.Empty;
            _detailDescription.Visible = false;
            _detailContext.Text = string.Empty;
            _detailSource.Text = string.Empty;
            _compatibilityNotice.Text = string.Empty;
            _compatibilityNotice.Visible = false;
            _probabilityValue.Text = string.Empty;
            return;
        }

        SearchPresetDefinition value = preset!;
        foreach (SearchPresetVisualIconRef icon in value.VisualIcons.Take(3))
            AddDetailIcon(icon);
        _detailName.Text = value.Source == SearchPresetSource.Temporary
            ? FormatTemporaryIdentity(value)
            : value.Title;
        _detailDescription.Text = value.Description;
        _detailDescription.Visible = !string.IsNullOrWhiteSpace(value.Description);
        _detailContext.Text = FormatContext(value);
        _detailSource.Text = value.Source switch
        {
            SearchPresetSource.BuiltIn => _text.Get(Ui1TextKey.SearchPresetBuiltIn),
            SearchPresetSource.Temporary => _text.Get(Ui1TextKey.SearchPresetTemporary),
            _ => _text.Get(Ui1TextKey.SearchPresetUser)
        };
        if (value.IsWorkbench || WorkbenchMode && !canLoad)
        {
            string explanation = value.IsWorkbench && !WorkbenchMode
                ? Local("此预设包含完整工作台条件，请在新工作台的“预设”中打开。", "This preset contains workbench conditions. Open it from Presets in the new workbench.")
                : !canLoad
                    ? Local("无法完整应用此预设。原始内容已保留；请使用兼容版本，或在所需内容可用后重试。", "This preset cannot be applied in full. Its original content is preserved; use a compatible version or retry when the required content is available.")
                    : Local("应用将切换到此预设的模式并替换该模式的全部条件；多人预设同时恢复整桌人物与解锁设置。不会自动开始筛种。", "Apply switches to this preset's mode and replaces that mode's conditions, including the full party roster and unlock settings. Search does not start automatically.");
            _compatibilityNotice.Text = explanation;
            _compatibilityNotice.TooltipText = workbenchResolution?.Issue ?? loadResolution?.Issue ?? string.Empty;
            _compatibilityNotice.Visible = true;
        }
        else BindCompatibilityNotice(value, loadResolution!);
        _probabilityValue.Text = FormatProbability(value.SavedProbability);
        BuildSemanticSummary(value);
    }

    private void BindCompatibilityNotice(
        SearchPresetDefinition preset,
        SearchPresetLoadResolution resolution)
    {
        if (_text is null) return;
        SearchPresetCompatibilityAssessment assessment = SearchPresetCompatibilityResolver.Assess(
            preset,
            RuntimeAuthorityEnvironment.Current);

        string text = string.Empty;
        if (!resolution.CanLoad)
        {
            text = _text.Get(Ui1TextKey.SearchPresetCompatibilityUnavailable);
        }
        else if (resolution.Kind == SearchPresetLoadResolutionKind.Partial)
        {
            text = _text.Format(
                Ui1TextKey.SearchPresetCompatibilityPartial,
                resolution.LoadedConditionCount,
                resolution.AuthoredConditionCount);
        }
        else if (assessment.SemanticCompatibility == SearchPresetSemanticCompatibilityKind.DifferentRuntimeSemantics ||
                 assessment.CurrentEnvironmentStatus == SemanticEnvironmentStatus.SemanticMismatch)
        {
            text = _text.Get(Ui1TextKey.SearchPresetCompatibilitySemanticDifferent);
        }
        else if (assessment.SemanticCompatibility is SearchPresetSemanticCompatibilityKind.Unknown or SearchPresetSemanticCompatibilityKind.SchemaIncomparable)
        {
            text = _text.Get(Ui1TextKey.SearchPresetCompatibilitySemanticUnknown);
        }
        else if (assessment.CurrentUnlockMayBeNarrower)
        {
            text = _text.Get(Ui1TextKey.SearchPresetCompatibilityUnlockReduced);
        }
        else if (assessment.SemanticCompatibility == SearchPresetSemanticCompatibilityKind.SameRuntimeSemantics &&
                 !assessment.SameGameVersion)
        {
            text = _text.Get(Ui1TextKey.SearchPresetCompatibilityCrossVersionMatch);
        }

        _compatibilityNotice.Text = text;
        _compatibilityNotice.Visible = !string.IsNullOrWhiteSpace(text);
    }

    private void AddDetailIcon(SearchPresetVisualIconRef icon)
    {
        if (!TryResolveRelicIcon(icon, out ModelKey key, out IconDescriptor? descriptor)) return;
        var host = new Control { CustomMinimumSize = new Vector2(58f, 58f), MouseFilter = MouseFilterEnum.Stop };
        var texture = new TextureRect
        {
            Texture = descriptor!.Texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        texture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        host.AddChild(texture);
        if (_names is not null)
        {
            host.MouseEntered += () => _tooltipHost.ShowFor(host, key, GameContentKind.Relic, _names.Resolve(key, GameContentKind.Relic));
            host.MouseExited += () => _tooltipHost.Dismiss(host);
        }
        _detailIcons.AddChild(host);
    }

    private string FormatTemporaryIdentity(SearchPresetDefinition preset)
    {
        if (_text is null || _names is null) return preset.CreatedAtUtc.ToLocalTime().ToString("HH:mm");
        string character = _names.Resolve(preset.CharacterKey, GameContentKind.Character);
        return _text.Format(
            Ui1TextKey.SearchPresetTemporaryIdentity,
            preset.CreatedAtUtc.ToLocalTime().ToString("HH:mm"),
            character,
            preset.Ascension,
            preset.ConditionCount);
    }

    private string FormatContext(SearchPresetDefinition preset)
    {
        if (_text is null || _names is null) return string.Empty;
        string unlock = preset.Provenance.UnlockKind switch
        {
            SearchPresetUnlockKinds.Full => _text.Get(Ui1TextKey.SearchPresetUnlockFull),
            SearchPresetUnlockKinds.Partial => _text.Get(Ui1TextKey.SearchPresetUnlockPartial),
            _ => _text.Get(Ui1TextKey.SearchPresetUnlockUnknown)
        };
        SemanticEnvironmentStatus environmentStatus = RuntimeAuthorityInterpreter.InterpretHistoricalFingerprint(
            preset.Provenance.GameVersion,
            preset.Provenance.FingerprintSchemaVersion,
            preset.Provenance.SemanticFingerprint,
            out string matchedBaseline);
        string environment = environmentStatus switch
        {
            SemanticEnvironmentStatus.VerifiedVanillaMatch => _text.Get(Ui1TextKey.SearchPresetEnvironmentVerifiedVanilla),
            SemanticEnvironmentStatus.KnownBaselineMatchButGameVersionUnverified =>
                _text.Format(Ui1TextKey.SearchPresetEnvironmentKnownSemanticMatch, matchedBaseline),
            SemanticEnvironmentStatus.SemanticMismatch => _text.Get(Ui1TextKey.SearchPresetEnvironmentSemanticMismatch),
            SemanticEnvironmentStatus.Unverified => _text.Get(Ui1TextKey.SearchPresetEnvironmentUnverified),
            _ => _text.Get(Ui1TextKey.SearchPresetEnvironmentUnknown)
        };
        string version = string.IsNullOrWhiteSpace(preset.Provenance.GameVersion)
            ? _text.Get(Ui1TextKey.SearchPresetVersionUnknown)
            : preset.Provenance.GameVersion;
        return $"{FormatListContext(preset)}\n{unlock} · {environment} · {version}";
    }

    private string Local(string chinese, string english) => _text?.LanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ? chinese : english;

    private string FormatListContext(SearchPresetDefinition preset)
    {
        if (_names is null) return string.Empty;
        string roster = preset.Workbench is { Players.Count: > 0 } workbench
            ? string.Join(" / ", workbench.Players.Select(p => $"P{p.Slot + 1} {_names.Resolve(p.Character, GameContentKind.Character)}"))
            : _names.Resolve(preset.CharacterKey, GameContentKind.Character);
        string mode = preset.IsMultiplayer
            ? Local($"多人 · {preset.Workbench!.Players.Count} 人", $"Party · {preset.Workbench!.Players.Count} players")
            : preset.IsWorkbench && preset.Workbench is null
                ? Local("模式待解析", "Mode unavailable")
                : Local("单人", "Solo");
        return $"{mode} · A{preset.Ascension} · {Local($"{preset.ConditionCount} 项条件", $"{preset.ConditionCount} conditions")}\n{roster}";
    }

    private string FormatProbability(SearchPresetProbabilitySnapshot snapshot)
    {
        if (_text is null) return string.Empty;
        if (snapshot.IsImpossible) return "0";
        return snapshot.IsComplete && snapshot.TotalProbability is > 0d
            ? "≈ " + CompactNumberFormatter.FormatRarity(snapshot.TotalProbability.Value)
            : _text.Get(Ui1TextKey.SearchPresetProbabilityUnavailable);
    }

    private void BuildSemanticSummary(SearchPresetDefinition preset)
    {
        if (_text is null || _names is null) return;
        if (preset.Workbench is { } workbench)
        {
            BuildWorkbenchSummary(workbench);
            return;
        }
        if (preset.Draft is null)
        {
            _summary.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchPresetQueryUnavailable), Ui1TextRole.Warning, true));
            return;
        }

        var sections = new List<(string Title, List<string> Lines)>();
        void Add(string title, IEnumerable<string> lines)
        {
            string[] values = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Distinct(StringComparer.Ordinal).ToArray();
            if (values.Length > 0) sections.Add((title, values.ToList()));
        }
        SearchDraft draft = preset.Draft;

        var neow = new List<string>();
        if (draft.NeowRouteDraft.RouteRelicKey is { IsValid: true } route)
            neow.Add(_names.Resolve(route, GameContentKind.Relic));
        if (draft.NeowRouteDraft.RequiredBonesRelics.Count > 0)
            neow.Add(string.Join(draft.NeowRouteDraft.BonesOrderMode == RolltheSpire2.Search.Semantics.BonesRouteOrderMode.AnyOrder ? " + " : " → ",
                draft.NeowRouteDraft.RequiredBonesRelics.Select(key => _names.Resolve(key, GameContentKind.Relic))));
        foreach (var effect in draft.NeowRouteDraft.EffectConditions)
        {
            var kind = effect.OutputKind == NeowStructuredOutputKind.Relic ? GameContentKind.Relic :
                effect.OutputKind == NeowStructuredOutputKind.Potion ? GameContentKind.Potion : GameContentKind.Card;
            string label = effect.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset
                ? _text.Get("ui1.preset.summary.combined") : _names.Resolve(effect.SourceRelicKey, GameContentKind.Relic);
            string targets = string.Join(" + ", effect.OutputKeys.Select(k => _names.Resolve(k, kind)));
            if (effect.KaleidoscopePositionalSlots.Count > 0)
                targets += " · " + FormatSlots(effect.KaleidoscopePositionalSlots, kind);
            if (effect.Scope == NeowStructuredEffectScope.FinalCurse) label = _text.Get(Ui1TextKey.SearchFinalCurse);
            neow.Add(label + " · " + targets);
        }
        if (draft.RequireSmallCapsule) neow.Add(_text.Get(Ui1TextKey.SearchSmallCapsule));
        if (draft.RequireLargeCapsule) neow.Add(_text.Get(Ui1TextKey.SearchLargeCapsule));
        if (draft.RequireWhetstone) neow.Add(_text.Get(Ui1TextKey.SearchWhetstone));
        if (draft.RequireWarPaint) neow.Add(_text.Get(Ui1TextKey.SearchWarPaint));
        if (ModelKey.TryParseExact(draft.RequiredFinalCurse, out ModelKey finalCurse))
            neow.Add(_text.Get(Ui1TextKey.SearchFinalCurse) + " · " + _names.Resolve(finalCurse, GameContentKind.Card));
        if (draft.RequireBones && neow.Count == 0) neow.Add(_text.Get(Ui1TextKey.SearchCategoryNeow));
        Add(_text.Get(Ui1TextKey.SearchCategoryNeow), neow);

        Add(_text.Get(Ui1TextKey.SearchCategoryBossAncient), draft.AncientMatrixDraft.Rows
            .Where(row => row.IsActive)
            .Select(row =>
            {
                var parts = new List<string> { _names.Resolve(row.AncientKey, GameContentKind.Ancient) };
                parts.AddRange(row.SelectedOptionKeys.Select(key => _names.Resolve(key, GameContentKind.Relic)));
                return string.Join(" · ", parts);
            }));

        Add(_text.Get(Ui1TextKey.SearchCategoryBossAncient), draft.BossMapDraft.Rows
            .Where(row => row.IsVariantActive || row.FirstBossAny.Count > 0 || row.SecondBossAny.Count > 0)
            .Select(row =>
            {
                string[] names = row.FirstBossAny.Concat(row.SecondBossAny)
                    .Distinct()
                    .Select(key => _names.Resolve(key, GameContentKind.Encounter))
                    .ToArray();
                return names.Length > 0 ? string.Join(" · ", names) : _text.Format(Ui1TextKey.SearchPresetSummaryAct, row.Act);
            }));

        Add(_text.Get(Ui1TextKey.SearchCategoryRelicsShop), draft.RelicSequenceDraft
            .Where(condition => !condition.IsEmpty)
            .Select(FormatRelicCondition));

        Add(_text.Get(Ui1TextKey.SearchCategoryEvents), draft.EventSequenceDraft
            .Where(condition => !condition.IsEmpty)
            .Select(FormatEventCondition));
        if ((draft.MorphicGroveContainsCard ?? draft.MorphicGroveSecondCard) is { } morphicCard)
            Add(_text.Get(Ui1TextKey.SearchCategoryEvents), new[] {
                _text.Get(Ui1TextKey.SearchEventMorphicContains) + ": " + _names!.Resolve(morphicCard, GameContentKind.Card) +
                (draft.MorphicGroveContainsCard.HasValue && draft.MorphicGroveSecondCard is { } second ? " + " + _names.Resolve(second, GameContentKind.Card) : "") });
        if (draft.TransformationAggregate is { } aggregate)
            Add(_text.LanguageCode.StartsWith("zh") ? "变牌组合" : "Transformation aggregate", new[] {
                aggregate.Opening + " · " + aggregate.PickupOrder + " · " +
                string.Join(" + ", new[] { aggregate.MorphicGrove ? "Morphic ×2" : null, aggregate.AromaOfChaos ? "Aroma ×1" : null, aggregate.WhisperingHollow ? "Whisper ×1" : null }.Where(x => x is not null)),
                aggregate.Predicate == RolltheSpire2.Search.Semantics.TransformationAggregatePredicate.RareCountAtLeast
                    ? (_text.LanguageCode.StartsWith("zh") ? "稀有牌至少 " : "Rare cards ≥ ") + aggregate.MinimumRareCount
                    : string.Join(" + ", aggregate.TargetMultiset.Select(k => _names.Resolve(k, GameContentKind.Card))) });

        Add(_text.Get(Ui1TextKey.SearchCategoryEvents), draft.EventResultDraft.Where(c => c.IsValid).Select(c => {
            var (key, kind) = c.Kind switch {
                EventResultConditionKind.TrashHeapGrabCard => ("trash_grab", GameContentKind.Card),
                EventResultConditionKind.MorphicGroveGroupInitialBasicsContains => ("morphic_contains", GameContentKind.Card),
                EventResultConditionKind.TrashHeapDiveRelic => ("trash_dive", GameContentKind.Relic),
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => ("color", GameContentKind.Character),
                _ => ("fake_relic", GameContentKind.Relic)
            };
            return _text.Get("ui1.search.event_result." + key) + " · " + _names.Resolve(c.TargetKey, kind) +
                (c.MorphicGroveSecondCard is { } second ? " + " + _names.Resolve(second, GameContentKind.Card) : "");
        }));
        var shop = new List<string>();
        shop.AddRange(draft.MerchantColorlessSequenceDraft.Where(c => !c.IsEmpty).Select(c =>
            _text.Get("ui1.search.shop_colorless.title") + " · " + _text.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Card)));
        shop.AddRange(draft.RelicShopSequenceDraft.Where(c => !c.IsEmpty).Select(c =>
            _text.Get(Ui1TextKey.SearchRelicLaneShop) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Relic)));
        if (draft.MerchantColorlessSequenceDraft.Count == 0)
            shop.AddRange(draft.MerchantColorlessDraft.Where(c => c.IsValid).Select(c =>
                $"#{c.MerchantOrdinal} · " + _text.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + _names.Resolve(c.TargetCardKey, GameContentKind.Card)));
        Add(_text.Get("ui1.search.shop_colorless.title"), shop);

        if (draft.CombatRewardDraft.HasAnyValue)
        {
            var reward = new List<string>();
            if (draft.CombatRewardDraft.Cards.HasAnyValue)
                reward.Add(_text.Format(Ui1TextKey.SearchPresetSummaryRewardCards, draft.CombatRewardDraft.Cards.Count, FormatOrderMode(draft.CombatRewardDraft.Cards.OrderMode)) + " · " + FormatSlots(draft.CombatRewardDraft.Cards.Slots.Take(draft.CombatRewardDraft.Cards.Count), GameContentKind.Card));
            if (draft.CombatRewardDraft.Potions.HasAnyValue)
                reward.Add(_text.Format(Ui1TextKey.SearchPresetSummaryRewardPotions, draft.CombatRewardDraft.Potions.Count, FormatOrderMode(draft.CombatRewardDraft.Potions.OrderMode)) + " · " +
                    string.Join(" / ", draft.CombatRewardDraft.Potions.Slots.Take(draft.CombatRewardDraft.Potions.Count).Select(s =>
                        s.PotionKey.HasValue ? _names.Resolve(s.PotionKey.Value, GameContentKind.Potion) :
                        s.Requirement == CombatPotionSlotRequirement.NoDrop ? _text.Get(Ui1TextKey.SearchCombatRewardPotionNoDrop) :
                        s.Requirement == CombatPotionSlotRequirement.DropAny ? _text.Get(Ui1TextKey.SearchCombatRewardPotionDrop) : "—")));
            Add(_text.Get(Ui1TextKey.AnalysisSubsectionCombatReward), reward);
        }

        foreach ((string title, List<string> lines) in sections)
        {
            var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(card, Ui1SurfaceRole.Input, 3f, 1, 8f);
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", 3);
            column.AddChild(Ui1Theme.Label(title, Ui1TextRole.Meta));
            foreach (string line in lines) column.AddChild(Ui1Theme.Label(line, Ui1TextRole.Body, true));
            card.AddChild(column);
            _summary.AddChild(card);
        }
        if (sections.Count == 0)
            _summary.AddChild(Ui1Theme.Label(_text.Get(Ui1TextKey.SearchPresetSummaryEmpty), Ui1TextRole.Muted, true));
    }

    private void BuildWorkbenchSummary(WorkbenchSearchDraft draft)
    {
        if (draft.Players.Count == 0)
            AddQuerySummary(draft.Query, Local("单人条件", "Solo conditions"));
        else
        {
            AddQuerySummary(draft.Query, Local("整桌共享条件", "Shared party conditions"));
            foreach (WorkbenchPlayerDraft player in draft.Players)
            {
                string source = player.UnlockSource switch
                {
                    "AssumedFullyUnlocked" => Local("假定全解锁", "Assumed fully unlocked"),
                    "CapturedLobbySlot" => Local("读取的大厅解锁", "Captured lobby unlocks"),
                    _ => player.UnlockSource
                };
                string title = $"P{player.Slot + 1} · {_names!.Resolve(player.Character, GameContentKind.Character)} · {source}";
                PlayerOfferQuery? personal = draft.Query.Players.FirstOrDefault(p => p.Slot == player.Slot);
                if (personal is null)
                {
                    AddSummaryCard(title, [Local("此席位的条件无法解析", "This slot's conditions are unavailable")]);
                    continue;
                }
                var offers = new List<string>();
                if (!personal.Offers.IsEmpty) offers.Add("N · " + FormatKeyFilter(personal.Offers, GameContentKind.Relic));
                if (personal.SelectedOption is { } plan)
                    offers.Add("N · " + _names.Resolve(plan.Option, GameContentKind.Relic));
                offers.AddRange(personal.Results.Select(FormatStructuredEffect));
                AddQuerySummary(personal.Conditions, title, offers);
            }
        }
    }

    private void AddQuerySummary(SearchQuery query, string title, IEnumerable<string>? extra = null)
    {
        var lines = new List<string>(extra ?? []);
        if (query.OpeningRoute is { } route) lines.Add("N · " + _names!.Resolve(route.RouteRelicKey, GameContentKind.Relic));
        if (query.OpeningRouteRelicRequirement is { } bones)
            lines.Add("N · " + string.Join(bones.OrderMode == BonesRouteOrderMode.ExactOrder ? " → " : " + ", bones.RequiredRelicKeys.Select(k => _names!.Resolve(k, GameContentKind.Relic))));
        lines.AddRange(query.StructuredOpeningEffects.Select(FormatStructuredEffect));
        if (!query.LegacyNeow.NeowRelics.IsEmpty) lines.Add("N · " + FormatKeyFilter(query.LegacyNeow.NeowRelics, GameContentKind.Relic));
        if (query.LegacyNeow.RequiredFinalCurse is { } curse) lines.Add("N · " + _text!.Get(Ui1TextKey.SearchFinalCurse) + " · " + _names!.Resolve(curse, GameContentKind.Card));
        lines.AddRange(query.RelicSequenceConstraints.Select(c => "R · " + FormatRelicCondition(c)));
        lines.AddRange(query.EventSequenceConstraints.Select(c => "E · " + FormatEventCondition(c)));
        lines.AddRange(query.EventResultConditions.Select(c => "E · " + FormatEventResult(c)));
        lines.AddRange(query.AncientBranches.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {_names!.Resolve(c.AncientKey, GameContentKind.Ancient)}" +
            (c.OptionAny.Count > 0 ? " · " + string.Join(" / ", c.OptionAny.Select(k => _names.Resolve(k, GameContentKind.Relic))) : "") +
            (c.SeaGlassTargetAny.Count > 0 ? " · " + string.Join(" / ", c.SeaGlassTargetAny.Select(k => _names.Resolve(k, GameContentKind.Character))) : "")));
        lines.AddRange(query.LegacyWorld.AncientIdentityFilters.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Ancient)}"));
        lines.AddRange(query.LegacyWorld.AncientOptionFilters.Select(c => $"A · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Relic)}"));
        lines.AddRange(query.VariantBossBranches.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {_names!.Resolve(c.VariantKey, GameContentKind.Act)}" +
            (!c.FirstBoss.IsEmpty ? " · " + FormatKeyFilter(c.FirstBoss, GameContentKind.Encounter) : "") +
            (!c.SecondBoss.IsEmpty ? " · #2 " + FormatKeyFilter(c.SecondBoss, GameContentKind.Encounter) : "")));
        lines.AddRange(query.LegacyWorld.BossFilters.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · {FormatKeyFilter(c.Keys, GameContentKind.Encounter)}"));
        lines.AddRange(query.LegacyWorld.BossOrdinalFilters.Select(c => $"W · {_text!.Format(Ui1TextKey.SearchPresetSummaryAct, c.Act)} · #{c.Ordinal} · {FormatKeyFilter(c.Keys, GameContentKind.Encounter)}"));
        lines.AddRange(query.StandardMaps.Select(FormatMapCondition));
        lines.AddRange(query.RelicShopSequenceConditions.Select(c => "S · " + _text!.Get(Ui1TextKey.SearchRelicLaneShop) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Relic)));
        lines.AddRange(query.MerchantColorlessSequenceConditions.Select(c => "S · " + _text!.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + FormatOrderMode(c.OrderMode) + " · " + FormatSlots(c.Slots.Take(c.Count), GameContentKind.Card)));
        lines.AddRange(query.MerchantColorlessConditions.Select(c => $"S · #{c.MerchantOrdinal} · " + _text!.Get(c.Slot == MerchantColorlessSlot.Uncommon ? Ui1TextKey.SearchShopUncommon : Ui1TextKey.SearchShopRare) + " · " + _names!.Resolve(c.TargetCardKey, GameContentKind.Card)));
        if (query.CombatCardRewards is { } cards)
            lines.Add("C · " + _text!.Format(Ui1TextKey.SearchPresetSummaryRewardCards, cards.Count, FormatOrderMode(cards.OrderMode)) + " · " + FormatSlots(cards.Slots.Take(cards.Count), GameContentKind.Card));
        if (query.CombatPotionRewards is { } potions)
            lines.Add("C · " + _text!.Format(Ui1TextKey.SearchPresetSummaryRewardPotions, potions.Count, FormatOrderMode(potions.OrderMode)) + " · " +
                string.Join(" / ", potions.Slots.Take(potions.Count).Select(s => s.PotionKey is { } key ? _names!.Resolve(key, GameContentKind.Potion) :
                    s.Requirement == CombatPotionSlotRequirement.NoDrop ? _text.Get(Ui1TextKey.SearchCombatRewardPotionNoDrop) :
                    s.Requirement == CombatPotionSlotRequirement.DropAny ? _text.Get(Ui1TextKey.SearchCombatRewardPotionDrop) : "—")));
        if (query.TransformationAggregate is { } transform)
            lines.Add("T · " + (transform.Predicate == TransformationAggregatePredicate.RareCountAtLeast
                ? Local("稀有牌至少 ", "Rare cards ≥ ") + transform.MinimumRareCount
                : string.Join(" + ", transform.TargetMultiset.Select(k => _names!.Resolve(k, GameContentKind.Card)))));
        AddSummaryCard(title, lines.Count > 0 ? lines : [_text!.Get(Ui1TextKey.SearchPresetSummaryEmpty)]);
    }

    private string FormatStructuredEffect(NeowStructuredEffectSearchCondition effect)
    {
        GameContentKind kind = effect.OutputKind == NeowStructuredOutputKind.Relic ? GameContentKind.Relic :
            effect.OutputKind == NeowStructuredOutputKind.Potion ? GameContentKind.Potion : GameContentKind.Card;
        string targets = effect.KaleidoscopePositionalSlots.Count > 0
            ? FormatSlots(effect.KaleidoscopePositionalSlots, kind)
            : string.Join(" + ", effect.OutputKeys.Select(k => _names!.Resolve(k, kind)));
        if (effect.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw)
            targets = _names!.Resolve(BaseGameModelKeys.Cards.Claw, GameContentKind.Card) + " ×3";
        return $"N · {_names!.Resolve(effect.SourceRelicKey, GameContentKind.Relic)} · {targets}";
    }

    private string FormatEventResult(EventResultSearchCondition condition)
    {
        string eventId = condition.Kind switch
        {
            EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic => "TRASH_HEAP",
            EventResultConditionKind.ColorfulPhilosophersOfferedColor => "COLORFUL_PHILOSOPHERS",
            EventResultConditionKind.FakeMerchantOfferedFakeRelic => "FAKE_MERCHANT",
            EventResultConditionKind.MorphicGroveGroupInitialBasicsContains => "MORPHIC_GROVE",
            EventResultConditionKind.SymbioteInitialBasicTransform => "SYMBIOTE",
            EventResultConditionKind.AromaOfChaosInitialBasicTransform => "AROMA_OF_CHAOS",
            EventResultConditionKind.WhisperingHollowInitialBasicTransform => "WHISPERING_HOLLOW",
            EventResultConditionKind.TrialCase or EventResultConditionKind.TrialNondescriptInitialBasicsContains => "TRIAL",
            _ => "TINKER_TIME"
        };
        string target;
        if (condition.TrialCase is { } trial)
            target = _workbenchText!.Get($"query.event.results.case_{(int)trial + 1}");
        else if (condition.TinkerCardType is { } cardType)
            target = _workbenchText!.Get($"query.event.results.card_type_{(int)cardType + 1}") +
                (condition.TinkerRider is { } rider ? " · " + _workbenchText.Get("query.event.results.rider." + rider.ToString().ToLowerInvariant()) : "");
        else
        {
            GameContentKind kind = condition.Kind switch
            {
                EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.FakeMerchantOfferedFakeRelic => GameContentKind.Relic,
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => GameContentKind.Character,
                _ => GameContentKind.Card
            };
            target = _names!.Resolve(condition.TargetKey, kind) + (condition.MorphicGroveSecondCard is { } second ? " + " + _names.Resolve(second, GameContentKind.Card) : "");
        }
        return _names!.Resolve(new ModelKey("EVENT", eventId), GameContentKind.Event) + " · " + target;
    }

    private string FormatMapCondition(StandardMapSearchCondition condition)
    {
        string metric = condition.Metric switch
        {
            StandardMapMetric.GuaranteedMonster => "monster", StandardMapMetric.GuaranteedElite => "elite",
            StandardMapMetric.GuaranteedRest => "rest", StandardMapMetric.GuaranteedUnknown => "unknown",
            StandardMapMetric.ReachableMaxMonster => "max_monster", StandardMapMetric.ReachableMaxElite => "max_elite",
            StandardMapMetric.ReachableMaxRest => "max_rest", StandardMapMetric.ReachableMaxUnknown => "max_unknown", _ => "prefix"
        };
        string scope = _workbenchText!.Get(condition.Scope == 0 ? "query.map.scope.total" : $"query.map.scope.act{condition.Scope}");
        return $"M · {scope} · {_workbenchText.Get(condition.RouteObjective ? "query.map.route" : "query.map.property")} · {_workbenchText.Get("query.map.property." + metric)} {(condition.Comparison == StandardMapComparison.AtLeast ? "≥" : "≤")} {condition.Value}";
    }

    private void AddSummaryCard(string title, IEnumerable<string> lines)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Ui1Theme.ApplyPanel(card, Ui1SurfaceRole.Input, 3f, 1, 8f);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(Ui1Theme.Label(title, Ui1TextRole.Meta, true));
        foreach (string line in lines) column.AddChild(Ui1Theme.Label(line, Ui1TextRole.Body, true));
        card.AddChild(column);
        _summary.AddChild(card);
    }

    private bool TryResolveRelicIcon(
        SearchPresetVisualIconRef icon,
        out ModelKey key,
        out IconDescriptor? descriptor)
    {
        key = default;
        descriptor = null;
        if (!string.Equals(icon.Kind, SearchPresetVisualIconRef.RelicKind, StringComparison.Ordinal) ||
            !icon.TryGetModelKey(out key))
            return false;
        descriptor = _icons.Resolve(key, GameContentKind.Relic, IconVariant.Small);
        return descriptor.Texture is not null && !descriptor.IsMissing;
    }

    private string FormatSlots(IEnumerable<ModelKey?> slots, GameContentKind kind) =>
        string.Join(" / ", slots.Select(k => k.HasValue ? _names!.Resolve(k.Value, kind) : "—"));

    private string FormatRelicCondition(RelicSequenceSearchCondition condition)
    {
        if (_text is null || _names is null) return string.Empty;
        string lane = condition.Lane switch
        {
            RelicSequenceKind.Common => _text.Get(Ui1TextKey.SearchRelicLaneCommon),
            RelicSequenceKind.Uncommon => _text.Get(Ui1TextKey.SearchRelicLaneUncommon),
            RelicSequenceKind.Rare => _text.Get(Ui1TextKey.SearchRelicLaneRare),
            RelicSequenceKind.Shop => _text.Get(Ui1TextKey.SearchRelicLaneShop),
            _ => string.Empty
        };
        return condition.RangeMode == SearchSequenceRangeMode.FirstN
            ? _text.Format(Ui1TextKey.SearchRelicConditionFirstN, lane, condition.RangeValue, FormatKeyFilter(condition.Keys, GameContentKind.Relic))
            : _text.Format(Ui1TextKey.SearchRelicConditionExactSlot, lane, condition.RangeValue, FormatKeyFilter(condition.Keys, GameContentKind.Relic));
    }

    private string FormatEventCondition(EventSequenceSearchCondition condition)
    {
        if (_text is null || _names is null) return string.Empty;
        string act = _text.Format(Ui1TextKey.SearchPresetSummaryAct, condition.Act);
        string targets = FormatKeyFilter(condition.Keys, GameContentKind.Event);
        return condition.RangeMode == SearchSequenceRangeMode.FirstN
            ? _text.Format(Ui1TextKey.SearchEventConditionFirstN, act, condition.RangeValue, targets)
            : _text.Format(Ui1TextKey.SearchEventConditionExactSlot, act, condition.RangeValue, targets);
    }

    private string FormatKeyFilter(ModelKeySetFilter filter, GameContentKind kind)
    {
        if (_text is null || _names is null) return string.Empty;
        var parts = new List<string>();
        AddMode(_text.Get(Ui1TextKey.SearchAny), filter.Any);
        AddMode(_text.Get(Ui1TextKey.SearchAll), filter.All);
        AddMode(_text.Get(Ui1TextKey.SearchBan), filter.Ban);
        return string.Join(" · ", parts);

        void AddMode(string label, IReadOnlyList<ModelKey> keys)
        {
            string[] names = keys.Where(key => key.IsValid)
                .Distinct()
                .Select(key => _names.Resolve(key, kind))
                .ToArray();
            if (names.Length > 0) parts.Add(label + ": " + string.Join(", ", names));
        }
    }

    private string FormatOrderMode(CombatRewardSequenceOrderMode mode)
    {
        if (_text is null) return string.Empty;
        return mode == CombatRewardSequenceOrderMode.Ordered
            ? _text.Get(Ui1TextKey.SearchCombatRewardOrdered)
            : _text.Get(Ui1TextKey.SearchCombatRewardUnordered);
    }

    private sealed partial class PresetListItemButton : Button
    {
        private readonly IGameIconResolver _icons;
        private readonly AnchoredTooltipHost _tooltipHost;
        private readonly HBoxContainer _row;
        private readonly Label _label;
        private readonly Label _context;
        private readonly List<(Control Host, ModelKey Key)> _hoverIcons = new();

        public PresetListItemButton(IGameIconResolver icons, AnchoredTooltipHost tooltipHost)
        {
            _icons = icons;
            _tooltipHost = tooltipHost;
            ToggleMode = true;
            Text = string.Empty;
            CustomMinimumSize = new Vector2(0f, 88f);
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Ui1Theme.ApplyButton(this, Ui1ButtonRole.Secondary);

            _row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            _row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _row.OffsetLeft = 10f;
            _row.OffsetRight = -10f;
            _row.AddThemeConstantOverride("separation", 5);
            _label = Ui1Theme.Label(string.Empty, Ui1TextRole.Body);
            _label.MouseFilter = MouseFilterEnum.Ignore;
            _label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.ClipText = true;
            _label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _context = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted);
            _context.MouseFilter = MouseFilterEnum.Ignore;
            _context.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _context.ClipText = true;
            _context.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            AddChild(_row);
        }

        public void Bind(
            string label,
            string context,
            IReadOnlyList<SearchPresetVisualIconRef> icons,
            IGameContentNameResolver? names)
        {
            foreach (Node child in _row.GetChildren()) child.QueueFree();
            _hoverIcons.Clear();
            foreach (SearchPresetVisualIconRef icon in icons.Take(3))
            {
                if (!string.Equals(icon.Kind, SearchPresetVisualIconRef.RelicKind, StringComparison.Ordinal) ||
                    !icon.TryGetModelKey(out ModelKey key))
                    continue;
                IconDescriptor descriptor = _icons.Resolve(key, GameContentKind.Relic, IconVariant.Small);
                if (descriptor.Texture is null || descriptor.IsMissing) continue;
                var host = new Control
                {
                    CustomMinimumSize = new Vector2(30f, 30f),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                var texture = new TextureRect
                {
                    Texture = descriptor.Texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                texture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                host.AddChild(texture);
                _row.AddChild(host);
                _hoverIcons.Add((host, key));
            }
            _label.Text = label ?? string.Empty;
            _context.Text = context ?? string.Empty;
            var identity = new VBoxContainer
            {
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            identity.AddThemeConstantOverride("separation", 2);
            identity.AddChild(_label);
            identity.AddChild(_context);
            _row.AddChild(identity);
            TooltipText = (label ?? string.Empty) + "\n" + context;
            if (names is not null && _hoverIcons.Count > 0)
            {
                MouseEntered += () =>
                {
                    (Control _, ModelKey key) = _hoverIcons[0];
                    _tooltipHost.ShowFor(this, key, GameContentKind.Relic, names.Resolve(key, GameContentKind.Relic));
                };
                MouseExited += () => _tooltipHost.Dismiss(this);
            }
        }
    }
}
