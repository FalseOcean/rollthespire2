using RolltheSpire2.Ui.Shell;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private void BuildWorkbenchSections()
    {
        Text(_text.Get("layout.context"), 15, ColumnInset, 0, true, heading: true).Size = new(RailContentWidth, 24);
        Text(_text.Get("layout.categories"), 15, ColumnInset, DomainTop - 30, true, heading: true).Size = new(RailContentWidth, 24);
        Text(_text.Get("layout.management"), 15, ColumnInset, PresetActionsTop - 34, true, heading: true).Size = new(RailContentWidth, 24);
        Line(ColumnInset, DomainTop - 38, RailContentWidth, 1);
        Line(ColumnInset, PresetActionsTop - 42, RailContentWidth, 1);
    }

    public void Refresh(string language, IUiTextProvider text)
    {
        using var observation = _editorInputs.Observe(_runtime);
        CloseModal();
        _language = language; _text = text;
        bool firstRefresh = !_restored;
        bool restoredDraft = false;
        if (firstRefresh)
        {
            _restored = true;
            // Restore context before drawing its mode, seat and character controls.
            try
            {
                if (_persistence.LoadWorkbench() is { } saved)
                {
                    RestoreDraft(saved, render: false);
                    restoredDraft = true;
                }
            }
            catch (Exception ex) { _loadFailed = true; _restoreFailure = _lastIssue = ex.Message; _persistence.RecordPresentationFailure(ex, "WorkbenchRestore"); }
        }
        SyncSavedResults();
        foreach (var child in GetChildren())
        { if (child != _objectPicker && child != _searchTooltipLayer && child != _configOverlay && child != _neowEditor && child != _combatEditor && child != _shopEditor && child != _relicEditor && child != _ancientEditor && child != _eventEditor && child != _actInformationEditor && child != _transformationEditor) { RemoveChild(child); child.QueueFree(); } }
        _domainButtons.Clear();
        _domainCountLabels.Clear();
        _seatButtons.Clear();
        _currentUnlockStatus = null;
        _configButton = null;
        var names = RuntimeGameContentNameResolver.Create(language);
        const float presetWidth = (RailContentWidth - 8) / 2;
        var back = _p.CompactButton(_text.Get("preset.title")); back.Name = "OpenPresets";
        Place(back, ColumnInset, PresetActionsTop, presetWidth, AuthoringHeaderHeight);
        back.Pressed += OpenPresets;
        var savePreset = _p.CompactButton(_language == "zh" ? "保存预设" : "Save"); savePreset.Name = "SavePreset";
        SearchTooltip.Set(savePreset, _language == "zh" ? "保存当前条件为预设" : "Save current conditions as a preset");
        Place(savePreset, ColumnInset + presetWidth + 8, PresetActionsTop, presetWidth, AuthoringHeaderHeight);
        savePreset.Pressed += () => OpenPresetSave(Components.SearchPresetSaveIntentKind.CreateCurrentQuery, "");

        BuildWorkbenchSections();
        Line(LeftRailWidth + RailGap / 2, WorkspaceLayout.DividerTop, 1, WorkspaceLayout.DividerHeight);
        Line(RightRailLeft - RailGap / 2, WorkspaceLayout.DividerTop, 1, WorkspaceLayout.DividerHeight);
        SearchContext(names);
        _domainScroll = new ScrollContainer
        {
            Name = "SearchCategoryScroll", FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        Place(_domainScroll, ColumnInset, DomainTop, RailContentWidth, PresetActionsTop - 48 - DomainTop);

        (string id, string key)[] domains =
        [
            ("neow", "query.domain.neow"), ("ancient", "query.domain.ancients"),
            ("shop", "query.domain.shop"), ("combat", "query.domain.combat_rewards"),
            ("events", "query.domain.events"), ("boss", "query.domain.boss_variant"),
            ("map", "query.domain.map"),
            ("relics", "query.domain.relics")
        ];
        var domainContent = new Control
        {
            Name = "SearchCategoryNavigation", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new(0, domains.Length * DomainRowHeight + (!_multiplayer ? DomainRowHeight + 8 : 0)),
            MouseFilter = MouseFilterEnum.Pass
        };
        _domainScroll.AddChild(domainContent);
        for (int i = 0; i < domains.Length; i++)
            Domain(domainContent, domains[i].id, _text.Get(domains[i].key), i * DomainRowHeight);
        if (!_multiplayer)
        {
            float compositeTop = domains.Length * DomainRowHeight + 8;
            var separator = new ColorRect { Color = _p.Color(_p.Line), MouseFilter = MouseFilterEnum.Ignore };
            domainContent.AddChild(separator);
            separator.SetAnchorsPreset(LayoutPreset.TopWide);
            separator.OffsetTop = compositeTop - 6; separator.OffsetBottom = compositeTop - 5;
            Domain(domainContent, "transform", _text.Get("query.domain.transform"), compositeTop);
        }
        if (firstRefresh) RestoreUiPreferences();

        _currentDomain = Text(string.Empty, 14, CenterLeft + 12, CenterTop + 6, true);
        _currentDomain.Size = new Vector2(CenterWidth - 208, 28);
        _clearCategory = new ClearConditionsButton(_text.Get("workflow.conditions.clear_category"));
        _clearCategory.Name = "ClearCategoryConditions";
        _clearCategory.Pressed += ClearCategoryConditions;
        Place(_clearCategory, CenterLeft + CenterWidth - 184, CenterTop + 2, 176, 32);
        _neowEditor.Position = new(CenterLeft + ColumnInset, CenterTop + WorkspaceLayout.HeaderHeight);
        _neowEditor.Size = new(CenterWidth - ColumnInset * 2, DockTop - CenterTop - WorkspaceLayout.HeaderHeight - ColumnInset);
        _combatEditor.Position = _neowEditor.Position;
        _combatEditor.Size = _neowEditor.Size;
        _shopEditor.Position = _neowEditor.Position;
        _shopEditor.Size = _neowEditor.Size;
        _relicEditor.Position = _neowEditor.Position;
        _relicEditor.Size = _neowEditor.Size;
        _ancientEditor.Position = _neowEditor.Position;
        _ancientEditor.Size = _neowEditor.Size;
        _eventEditor.Position = _neowEditor.Position;
        _eventEditor.Size = _neowEditor.Size;
        _actInformationEditor.Position = _neowEditor.Position;
        _actInformationEditor.Size = _neowEditor.Size;
        _transformationEditor.Position = _neowEditor.Position;
        _transformationEditor.Size = _neowEditor.Size;
        // Restore already bound every semantic context after importing the draft.
        // Preferences and final sizes above only select which controls to present.
        if (restoredDraft) RenderEditorDomain(_selectedDomain);
        else RefreshEditorContexts();
        ApplyDomainSelection(_selectedDomain);
        UpdateDomainCounts();

        BuildConditionActions();
        BuildProductionDock();
    }

    private void Domain(Control parent, string id, string title, float y)
    {
        var button = _p.CompactButton(title, DomainRowHeight - 4, _language == "zh" ? 18 : 16);
        SearchCategoryKey category = id switch
        {
            "neow" => SearchCategoryKey.Neow,
            "ancient" => SearchCategoryKey.Ancient,
            "shop" => SearchCategoryKey.Shop,
            "combat" => SearchCategoryKey.CombatReward,
            "events" => SearchCategoryKey.Event,
            "boss" => SearchCategoryKey.BossIdentity,
            "map" => SearchCategoryKey.BossAndMap,
            "relics" => SearchCategoryKey.Relic,
            "transform" => SearchCategoryKey.Transformation,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown workbench domain")
        };
        SearchCategoryTabIconAsset icon = _domainIcons.Resolve(category);
        button.Name = "SearchCategory_" + id;
        button.Alignment = HorizontalAlignment.Left;
        button.ClipContents = true;
        button.CustomMinimumSize = new Vector2(0, DomainRowHeight - 4);
        parent.AddChild(button);
        button.SetAnchorsPreset(LayoutPreset.TopWide);
        button.OffsetTop = y; button.OffsetBottom = y + DomainRowHeight - 4;
        var countLabel = _p.Label(string.Empty, 15, true);
        countLabel.HorizontalAlignment = HorizontalAlignment.Right;
        countLabel.VerticalAlignment = VerticalAlignment.Center;
        countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        countLabel.Visible = false;
        countLabel.AddThemeColorOverride("font_color", _p.Color(_p.Selected));
        button.AddChild(countLabel);
        countLabel.SetAnchorsPreset(LayoutPreset.TopRight);
        countLabel.OffsetLeft = -40; countLabel.OffsetRight = -6;
        countLabel.OffsetTop = 2; countLabel.OffsetBottom = DomainRowHeight - 6;
        _domainCountLabels.Add(id, countLabel);
        if (!icon.IsMissing)
        {
            var iconHost = new CenterContainer
            {
                Position = new Vector2(12, Math.Max(2, (DomainRowHeight - 28) / 2)),
                Size = new Vector2(24, 24),
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            button.AddChild(iconHost);
            iconHost.AddChild(new TextureRect
            {
                Texture = icon.Texture,
                CustomMinimumSize = new Vector2(20, 20),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            });
        }
        button.Disabled = _multiplayer && id == "transform";
        if (button.Disabled) SearchTooltip.Set(button, _language == "zh" ? "多人不启用 T 复合变牌筛选" : "T aggregate filtering is outside multiplayer scope");
        button.Pressed += () => { _showResults = false; _runtimeSurface?.Hide(); SelectDomain(id); UpdateResultNavigation(); };
        _domainButtons.Add(id, button);
    }

    private void SelectDomain(string id)
    {
        if (!_multiplayer && id == "transform") RefreshTransformationContext();
        ApplyDomainSelection(id);
    }

    private void ApplyDomainSelection(string id)
    {
        if (_multiplayer && id == "transform") id = "neow";
        RenderEditorDomain(id);
        if (id is "boss" or "map") _actInformationEditor.SelectPage(id == "map");
        _selectedDomain = id;
        if (_domainScroll is not null && _domainButtons.TryGetValue(id, out var selectedButton))
            _domainScroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, selectedButton);
        foreach (var (key, button) in _domainButtons)
        {
            var style = _p.Box(_p.Canvas, _p.Selected);
            style.BgColor = new Color(0, 0, 0, 0);
            style.BorderColor = _p.Color(_p.Selected);
            style.BorderWidthLeft = key == id ? 3 : 0;
            style.ContentMarginLeft = 44;
            style.ContentMarginRight = 44;
            style.ContentMarginTop = style.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride("normal", style);
            // Disabling during Search must preserve the space reserved for the child icon.
            button.AddThemeStyleboxOverride("disabled", (StyleBox)style.Duplicate());
            var hover = _p.Box(_p.Hover, _p.Selected);
            hover.BgColor = new Color(hover.BgColor, .25f);
            hover.BorderColor = _p.Color(_p.Selected);
            hover.BorderWidthLeft = key == id ? 3 : 0;
            hover.ContentMarginLeft = 44;
            hover.ContentMarginRight = 44;
            hover.ContentMarginTop = hover.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride("hover", hover);
            var pressed = _p.Box(_p.Line, _p.Selected);
            pressed.BgColor = new Color(pressed.BgColor, .35f);
            pressed.BorderColor = _p.Color(_p.Selected);
            pressed.BorderWidthLeft = key == id ? 3 : 0;
            pressed.ContentMarginLeft = 44;
            pressed.ContentMarginRight = 44;
            pressed.ContentMarginTop = pressed.ContentMarginBottom = 2;
            button.AddThemeStyleboxOverride("pressed", pressed);
            MainPanelTheme.SetColor(button, "font_color", key == id ? _p.Text : _p.Secondary);
        }
        if (_currentDomain is not null)
        {
            _currentDomain.Text = !_multiplayer ? (_language == "zh" ? "单人条件" : "Single-player conditions") : id switch
            {
                "boss" or "map" => _language == "zh" ? "全队共用条件" : "Shared party conditions",
                "ancient" => _language == "zh" ? $"身份全队共用 · P{_seat + 1} 的选项" : $"Shared identities · P{_seat + 1}'s options",
                "events" => _language == "zh" ? $"出现条件全队共用 · P{_seat + 1} 的结果" : $"Shared event queue · P{_seat + 1}’s results",
                _ => _language == "zh" ? $"正在编辑 P{_seat + 1} 的条件" : $"Editing P{_seat + 1}'s conditions"
            };
            _currentDomain.Visible = !_showResults;
        }
        _neowEditor.Visible = id == "neow";
        _combatEditor.Visible = id == "combat";
        _shopEditor.Visible = id == "shop";
        _relicEditor.Visible = id == "relics";
        _ancientEditor.Visible = id == "ancient";
        _eventEditor.Visible = id == "events";
        _actInformationEditor.Visible = id is "boss" or "map";
        _transformationEditor.Visible = id == "transform";
    }

    private Label Text(string text, int fontSize, float x, float y, bool secondary = false, bool heading = false)
    {
        var label = _p.Label(text, fontSize, secondary, heading);
        Place(label, x, y);
        return label;
    }
    private void Line(float x, float y, float width, float height)
        => Place(new ColorRect { Color = _p.Color(_p.Line), MouseFilter = MouseFilterEnum.Ignore }, x, y, width, height);
    private void Place(Control control, float x, float y, float width = 0, float height = 0)
    {
        AddChild(control); control.Position = new Vector2(x, y);
        if (width > 0 || height > 0) control.Size = new Vector2(width, height);
    }
}
