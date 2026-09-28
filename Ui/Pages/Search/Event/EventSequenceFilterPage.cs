using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal sealed partial class EventSequenceFilterPage : MarginContainer
{
    private readonly EventThumbnailProvider _thumbnails;
    private readonly Label _pageTitle;
    private readonly Button _clear;
    private readonly Label _builderTitle;
    private readonly OptionButton _act;
    private readonly EventAuthoringControlStrip _authoringControls;
    private readonly Button _selectEvent;
    private readonly Label _catalogNotice;
    private readonly Label _addedTitle;
    private readonly Label _empty;
    private readonly VBoxContainer _conditionsHost;
    private readonly EventSearchPickerPanel _picker;
    private readonly RelicPickerPanel _resultPicker;
    private readonly HBoxContainer _trashResultEditor;
    private readonly HBoxContainer _colorfulResultEditor;
    private readonly HBoxContainer _fakeResultEditor;
    private readonly SearchCardResultSlot _trashGrabSlot;
    private readonly NeowModelKeySlot _trashDiveSlot;
    private readonly NeowModelKeySlot _colorfulSlot;
    private readonly NeowModelKeySlot _fakeRelicSlot;
    private readonly List<EventSequenceUiCondition> _conditions = new();
    private EventSequenceSearchUiCatalog _catalog = EventSequenceSearchUiCatalog.Empty(
        RuntimeProfileId.Unsupported,
        "not-bound");
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private bool _suppressChanges;
    private bool _running;

    public EventSequenceFilterPage(
        EventThumbnailProvider thumbnails,
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterPoolIcons,
        ICardPickerFilterIconProvider cardPickerFilterIcons,
        AnchoredTooltipHost tooltipHost)
    {
        _thumbnails = thumbnails;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("margin_left", 2);
        AddThemeConstantOverride("margin_top", 2);
        AddThemeConstantOverride("margin_right", 2);
        AddThemeConstantOverride("margin_bottom", 2);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddThemeConstantOverride("separation", 10);

        var header = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        header.AddThemeConstantOverride("separation", 10);
        _pageTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        _pageTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _clear = new Button { CustomMinimumSize = new Vector2(132f, 34f) };
        Ui1Theme.ApplyButton(_clear, Ui1ButtonRole.Ghost);
        _clear.Pressed += ClearDraft;
        header.AddChild(_pageTitle);
        header.AddChild(_clear);
        root.AddChild(header);

        var builderPanel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyPanel(builderPanel, Ui1SurfaceRole.Card, 4f, 1, 12f);
        var builderColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        builderColumn.AddThemeConstantOverride("separation", 8);
        _builderTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        builderColumn.AddChild(_builderTitle);

        var builderRow = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        builderRow.AddThemeConstantOverride("h_separation", 10);
        builderRow.AddThemeConstantOverride("v_separation", 8);

        _act = Option(124f);
        _authoringControls = new EventAuthoringControlStrip();
        _authoringControls.Changed += RefreshBuilderState;
        _selectEvent = new Button
        {
            CustomMinimumSize = new Vector2(180f, 40f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All
        };
        Ui1Theme.ApplyButton(_selectEvent, Ui1ButtonRole.Primary);
        _selectEvent.Pressed += OpenPicker;

        builderRow.AddChild(_act);
        builderRow.AddChild(_authoringControls);
        builderRow.AddChild(_selectEvent);
        builderColumn.AddChild(builderRow);

        _catalogNotice = Ui1Theme.Label(string.Empty, Ui1TextRole.Warning, true);
        _catalogNotice.Visible = false;
        builderColumn.AddChild(_catalogNotice);
        builderPanel.AddChild(builderColumn);
        root.AddChild(builderPanel);

        _resultPicker = new RelicPickerPanel(icons, characterPoolIcons, cardPickerFilterIcons, tooltipHost);
        _trashGrabSlot = new SearchCardResultSlot(icons, _resultPicker.Open);
        _trashDiveSlot = new NeowModelKeySlot(icons, _resultPicker.Open);
        _colorfulSlot = new NeowModelKeySlot(icons, _resultPicker.Open);
        _fakeRelicSlot = new NeowModelKeySlot(icons, _resultPicker.Open);
        foreach (SearchHorizontalResultSlot slot in new SearchHorizontalResultSlot[]
                 { _trashGrabSlot, _trashDiveSlot, _colorfulSlot, _fakeRelicSlot })
        {
            slot.Changed += () =>
            {
                RebuildConditions();
                RefreshBuilderState();
                Changed?.Invoke();
            };
        }

        _trashResultEditor = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _trashResultEditor.AddThemeConstantOverride("separation", 4);
        _trashGrabSlot.UseInlineLabel(96f);
        _trashDiveSlot.UseInlineLabel(96f);
        _trashGrabSlot.UseCompactInlineWidth(174f);
        _trashDiveSlot.UseCompactInlineWidth(174f);
        _trashResultEditor.AddChild(_trashGrabSlot);
        _trashResultEditor.AddChild(_trashDiveSlot);

        _colorfulResultEditor = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _colorfulSlot.UseInlineLabel(104f);
        _colorfulSlot.UseCompactInlineWidth(174f);
        _colorfulResultEditor.AddChild(_colorfulSlot);

        _fakeResultEditor = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _fakeRelicSlot.UseInlineLabel(112f);
        _fakeRelicSlot.UseCompactInlineWidth(300f);
        _fakeResultEditor.AddChild(_fakeRelicSlot);
        _addedTitle = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
        root.AddChild(_addedTitle);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0f, 150f)
        };
        _conditionsHost = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        _conditionsHost.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_conditionsHost);
        root.AddChild(scroll);

        _empty = Ui1Theme.Label(string.Empty, Ui1TextRole.Muted, true);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _conditionsHost.AddChild(_empty);
        AddChild(root);

        _picker = new EventSearchPickerPanel(_thumbnails, tooltipHost);
        AddChild(_picker);
        _picker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_resultPicker);
        _resultPicker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _act.ItemSelected += _ => ActChanged();
        VisibilityChanged += () =>
        {
            if (!IsVisibleInTree() && _picker.IsOpen) _picker.Cancel();
            if (!IsVisibleInTree() && _resultPicker.IsOpen) _resultPicker.Cancel();
        };
        TreeExiting += () =>
        {
            if (_picker.IsOpen) _picker.Cancel();
            if (_resultPicker.IsOpen) _resultPicker.Cancel();
        };
        RefreshBuilderState();
    }

    public bool TryCancelTransientSurface()
    {
        if (_resultPicker.IsOpen)
        {
            _resultPicker.Cancel();
            return true;
        }
        if (_picker.IsOpen)
        {
            _picker.Cancel();
            return true;
        }
        return false;
    }

    public event Action? Changed;
    public int EnabledConditionCount => _conditions.Count + BuildEventResultConditions().Count;

    public IReadOnlyList<EventSequenceSearchCondition> BuildSearchConditions() =>
        _conditions
            .Select(condition => new EventSequenceSearchCondition(
                condition.Act,
                null,
                condition.RangeMode,
                condition.RangeValue,
                condition.MatchMode == EventSequenceUiMatchMode.Appears
                    ? new ModelKeySetFilter(new[] { condition.EventKey }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                    : new ModelKeySetFilter(Array.Empty<ModelKey>(), Array.Empty<ModelKey>(), new[] { condition.EventKey })))
            .ToArray();

    public string SerializedConditions => string.Join(";", _conditions.Select(condition => condition.Serialize()));

    public IReadOnlyList<EventResultSearchCondition> BuildEventResultConditions()
    {
        var output = new List<EventResultSearchCondition>(4);
        if (_trashGrabSlot.SelectedKey is { IsValid: true } grab)
            output.Add(new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard, grab));
        if (_trashDiveSlot.SelectedKey is { IsValid: true } dive)
            output.Add(new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic, dive));
        if (_colorfulSlot.SelectedKey is { IsValid: true } color)
            output.Add(new EventResultSearchCondition(EventResultConditionKind.ColorfulPhilosophersOfferedColor, color));
        if (_fakeRelicSlot.SelectedKey is { IsValid: true } fake)
            output.Add(new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic, fake));
        return output;
    }

    public void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text;
        _names = names;
        _pageTitle.Text = text.Get(Ui1TextKey.SearchEventPageTitle);
        _clear.Text = text.Get(Ui1TextKey.SearchEventClearConditions);
        _builderTitle.Text = text.Get(Ui1TextKey.SearchEventBuilderTitle);
        _selectEvent.Text = text.Get(Ui1TextKey.SearchEventSelectEvent);
        _catalogNotice.Text = text.Get(Ui1TextKey.SearchEventCatalogUnavailable);
        _empty.Text = text.Get(Ui1TextKey.SearchEventNoConditions);
        _trashGrabSlot.Configure(
            text.Get(Ui1TextKey.SearchEventResultTrashGrab),
            Beta111EventResultCatalog.TrashHeapGrabCards,
            GameContentKind.Card,
            IconVariant.CardPickerLarge,
            names,
            text.Get(Ui1TextKey.SearchEventResultNeutral),
            text.Get(Ui1TextKey.SearchEventResultConditionalTooltip),
            pickerTitle: text.Get(Ui1TextKey.SearchEventResultTrashGrabPicker));
        _trashDiveSlot.Configure(
            text.Get(Ui1TextKey.SearchEventResultTrashDive),
            Beta111EventResultCatalog.TrashHeapDiveRelics,
            GameContentKind.Relic,
            IconVariant.Small,
            names,
            text.Get(Ui1TextKey.SearchEventResultNeutral),
            text.Get(Ui1TextKey.SearchEventResultConditionalTooltip),
            pickerTitle: text.Get(Ui1TextKey.SearchEventResultTrashDivePicker));
        _colorfulSlot.Configure(
            text.Get(Ui1TextKey.SearchEventResultColor),
            Beta111EventResultCatalog.ColorfulCharacterOrder,
            GameContentKind.Character,
            IconVariant.CharacterPortrait,
            names,
            text.Get(Ui1TextKey.SearchEventResultNeutral),
            text.Get(Ui1TextKey.SearchEventResultConditionalTooltip),
            pickerTitle: text.Get(Ui1TextKey.SearchEventResultColorPicker));
        _fakeRelicSlot.Configure(
            text.Get(Ui1TextKey.SearchEventResultFakeRelic),
            Beta111EventResultCatalog.FakeMerchantRelics,
            GameContentKind.Relic,
            IconVariant.Small,
            names,
            text.Get(Ui1TextKey.SearchEventResultNeutral),
            text.Get(Ui1TextKey.SearchEventResultConditionalTooltip),
            pickerTitle: text.Get(Ui1TextKey.SearchEventResultFakePicker));
        _picker.ApplyLocalization(text, names);
        _resultPicker.ApplyLocalization(text, names);
        _authoringControls.ApplyText(
            text.Get(Ui1TextKey.SearchEventRangeFirstN),
            text.Get(Ui1TextKey.SearchEventRangeUnit),
            text.Get(Ui1TextKey.SearchEventModeAppears),
            text.Get(Ui1TextKey.SearchEventModeExcluded),
            text.Get(Ui1TextKey.SearchSequenceAppearsTooltip),
            text.Get(Ui1TextKey.SearchSequenceExcludedTooltip));
        PopulateActSelector();
        RebuildConditions();
        RefreshBuilderState();
    }

    public void BindCatalog(EventSequenceSearchUiCatalog catalog)
    {
        _catalog = catalog;
        if (_picker.IsOpen) _picker.Cancel();
        RebuildConditions();
        RefreshBuilderState();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        _act.Disabled = running;
        _authoringControls.SetDisabled(running);
        _selectEvent.Disabled = running || !_catalog.CatalogAvailable;
        if (running && _picker.IsOpen) _picker.Cancel();
        if (running && _resultPicker.IsOpen) _resultPicker.Cancel();
        _trashGrabSlot.SetEnabled(!running);
        _trashDiveSlot.SetEnabled(!running);
        _colorfulSlot.SetEnabled(!running);
        _fakeRelicSlot.SetEnabled(!running);
        RebuildConditions();
        RefreshBuilderState();
    }

    public void RestoreDraft(IReadOnlyList<EventSequenceSearchCondition>? conditions, bool notify) =>
        RestoreDraft(conditions, Array.Empty<EventResultSearchCondition>(), notify);

    public void RestoreDraft(
        IReadOnlyList<EventSequenceSearchCondition>? conditions,
        IReadOnlyList<EventResultSearchCondition>? resultConditions,
        bool notify)
    {
        if (_running) return;
        _conditions.Clear();
        foreach (EventSequenceSearchCondition condition in conditions ?? Array.Empty<EventSequenceSearchCondition>())
        {
            foreach (ModelKey key in condition.Keys.Any.Concat(condition.Keys.All).Distinct(ModelKeyComparer.Instance))
            {
                if (key.IsValid)
                {
                    _conditions.Add(new EventSequenceUiCondition(
                        Guid.NewGuid(), condition.Act, condition.RangeMode, condition.RangeValue, key, EventSequenceUiMatchMode.Appears));
                }
            }
            foreach (ModelKey key in condition.Keys.Ban.Distinct(ModelKeyComparer.Instance))
            {
                if (key.IsValid)
                {
                    _conditions.Add(new EventSequenceUiCondition(
                        Guid.NewGuid(), condition.Act, condition.RangeMode, condition.RangeValue, key, EventSequenceUiMatchMode.Excluded));
                }
            }
        }
        _trashGrabSlot.Select(null, notify: false);
        _trashDiveSlot.Select(null, notify: false);
        _colorfulSlot.Select(null, notify: false);
        _fakeRelicSlot.Select(null, notify: false);
        foreach (EventResultSearchCondition condition in resultConditions ?? Array.Empty<EventResultSearchCondition>())
        {
            if (!condition.IsValid) continue;
            switch (condition.Kind)
            {
                case EventResultConditionKind.TrashHeapGrabCard:
                    _trashGrabSlot.Select(condition.TargetKey, notify: false);
                    break;
                case EventResultConditionKind.TrashHeapDiveRelic:
                    _trashDiveSlot.Select(condition.TargetKey, notify: false);
                    break;
                case EventResultConditionKind.ColorfulPhilosophersOfferedColor:
                    _colorfulSlot.Select(condition.TargetKey, notify: false);
                    break;
                case EventResultConditionKind.FakeMerchantOfferedFakeRelic:
                    _fakeRelicSlot.Select(condition.TargetKey, notify: false);
                    break;
            }
        }
        SelectAct(1);
        _authoringControls.ResetDefaults(notify: false);
        if (_picker.IsOpen) _picker.Cancel();
        RebuildConditions();
        RefreshBuilderState();
        if (notify) Changed?.Invoke();
    }

    public void ClearDraft()
    {
        if (_running) return;
        bool hadConditions = EnabledConditionCount > 0 || _authoringControls.NeedsReset;
        _conditions.Clear();
        _trashGrabSlot.Select(null, notify: false);
        _trashDiveSlot.Select(null, notify: false);
        _colorfulSlot.Select(null, notify: false);
        _fakeRelicSlot.Select(null, notify: false);
        SelectAct(1);
        _authoringControls.ResetDefaults(notify: false);
        if (_picker.IsOpen) _picker.Cancel();
        RebuildConditions();
        RefreshBuilderState();
        if (hadConditions) Changed?.Invoke();
    }

    private void PopulateActSelector()
    {
        if (_text is null) return;
        _suppressChanges = true;
        int actSelection = SelectedAct();
        _act.Clear();
        _act.AddItem(_text.Get(Ui1TextKey.SearchEventAct1), 1);
        _act.AddItem(_text.Get(Ui1TextKey.SearchEventAct2), 2);
        _act.AddItem(_text.Get(Ui1TextKey.SearchEventAct3), 3);
        SelectAct(actSelection is >= 1 and <= 3 ? actSelection : 1);
        _suppressChanges = false;
    }

    private void ActChanged()
    {
        if (_suppressChanges) return;
        if (_picker.IsOpen) _picker.Cancel();
        RefreshBuilderState();
    }

    private void OpenPicker()
    {
        if (_running || _text is null || _names is null || !_catalog.CatalogAvailable) return;
        int act = SelectedAct();
        IReadOnlyList<EventSearchUiCandidate> candidates = _catalog.CandidatesForAct(act);
        _picker.Open(
            _text.Format(Ui1TextKey.SearchEventPickerTitle, ActText(act)),
            _catalog.ProfileId,
            1,
            candidates,
            candidate =>
            {
                if (candidate is not null) CommitCandidate(candidate);
            });
    }

    private void CommitCandidate(EventSearchUiCandidate candidate)
    {
        int act = SelectedAct();
        if (_running || _catalog.Find(act, candidate.EventKey) is null || _authoringControls.RangeValue is < 1 or > 10)
        {
            return;
        }

        _conditions.Add(new EventSequenceUiCondition(
            Guid.NewGuid(),
            act,
            SearchSequenceRangeMode.FirstN,
            _authoringControls.RangeValue,
            candidate.EventKey,
            _authoringControls.IsExcluded ? EventSequenceUiMatchMode.Excluded : EventSequenceUiMatchMode.Appears));
        RebuildConditions();
        RefreshBuilderState();
        Changed?.Invoke();
    }

    private void RemoveCondition(Guid id)
    {
        if (_running) return;
        if (_conditions.RemoveAll(condition => condition.Id == id) == 0) return;
        RebuildConditions();
        Changed?.Invoke();
    }

    private void RebuildConditions()
    {
        DetachResultEditors();
        foreach (Node child in _conditionsHost.GetChildren())
        {
            if (ReferenceEquals(child, _empty)) continue;
            _conditionsHost.RemoveChild(child);
            child.QueueFree();
        }

        if (_text is null || _names is null)
        {
            _empty.Text = string.Empty;
            _empty.Visible = true;
            return;
        }

        int resultCount = BuildEventResultConditions().Count;
        _addedTitle.Text = _text.Format(Ui1TextKey.SearchEventAddedConditions, _conditions.Count + resultCount);
        _clear.Disabled = _running || EnabledConditionCount == 0;
        if (_conditions.Count == 0 && resultCount == 0)
        {
            _empty.Text = _text.Get(Ui1TextKey.SearchEventNoConditions);
            _empty.Visible = true;
            return;
        }

        _empty.Visible = false;
        var attached = new HashSet<string>(StringComparer.Ordinal);
        foreach (EventSequenceUiCondition condition in _conditions)
        {
            EventSearchUiCandidate? candidate = _catalog.Find(condition.Act, condition.EventKey);
            string eventName = _names.Resolve(condition.EventKey, GameContentKind.Event);
            string tooltip = candidate is null ? eventName : BuildCandidateTooltip(candidate);
            Control? effectEditor = null;
            if (string.Equals(condition.EventKey.Entry, Beta111EventResultCatalog.TrashHeapEventEntry, StringComparison.Ordinal) && attached.Add("trash"))
                effectEditor = _trashResultEditor;
            else if (string.Equals(condition.EventKey.Entry, Beta111EventResultCatalog.ColorfulPhilosophersEventEntry, StringComparison.Ordinal) && attached.Add("colorful"))
                effectEditor = _colorfulResultEditor;
            else if (string.Equals(condition.EventKey.Entry, Beta111EventResultCatalog.FakeMerchantEventEntry, StringComparison.Ordinal) && attached.Add("fake"))
                effectEditor = _fakeResultEditor;
            _conditionsHost.AddChild(new EventSequenceConditionCard(
                _thumbnails.Resolve(condition.EventKey),
                eventName,
                ConditionDetail(condition),
                tooltip,
                _text.Get(Ui1TextKey.SearchEventRemoveCondition),
                _running,
                () => RemoveCondition(condition.Id),
                effectEditor));
        }
        AddResultOnlyEditor("trash", _trashResultEditor, attached.Contains("trash"), _text.Get(Ui1TextKey.SearchEventResultTrashHeap),
            () => ClearResultSlots(_trashGrabSlot, _trashDiveSlot));
        AddResultOnlyEditor("colorful", _colorfulResultEditor, attached.Contains("colorful"), _text.Get(Ui1TextKey.SearchEventResultColorful),
            () => ClearResultSlots(_colorfulSlot));
        AddResultOnlyEditor("fake", _fakeResultEditor, attached.Contains("fake"), _text.Get(Ui1TextKey.SearchEventResultFakeMerchant),
            () => ClearResultSlots(_fakeRelicSlot));
    }

    private string ConditionDetail(EventSequenceUiCondition condition)
    {
        if (_text is null) return condition.Serialize();
        return condition.RangeMode == SearchSequenceRangeMode.FirstN
            ? condition.MatchMode == EventSequenceUiMatchMode.Appears
                ? _text.Format(Ui1TextKey.SearchEventConditionDetailFirstNAppears, ActText(condition.Act), condition.RangeValue)
                : _text.Format(Ui1TextKey.SearchEventConditionDetailFirstNExcluded, ActText(condition.Act), condition.RangeValue)
            : condition.MatchMode == EventSequenceUiMatchMode.Appears
                ? _text.Format(Ui1TextKey.SearchEventConditionDetailExactAppears, ActText(condition.Act), condition.RangeValue)
                : _text.Format(Ui1TextKey.SearchEventConditionDetailExactExcluded, ActText(condition.Act), condition.RangeValue);
    }

    private string BuildCandidateTooltip(EventSearchUiCandidate candidate) =>
        _names?.Resolve(candidate.EventKey, GameContentKind.Event) ?? candidate.EventKey.Entry;

    private string ActText(int act)
    {
        if (_text is null) return "Act " + act;
        return act switch
        {
            1 => _text.Get(Ui1TextKey.SearchEventAct1),
            2 => _text.Get(Ui1TextKey.SearchEventAct2),
            3 => _text.Get(Ui1TextKey.SearchEventAct3),
            _ => "Act " + act
        };
    }

    private void RefreshBuilderState()
    {
        _catalogNotice.Visible = !_catalog.CatalogAvailable;
        _selectEvent.Disabled = _running ||
            !_catalog.CatalogAvailable ||
            _catalog.CandidatesForAct(SelectedAct()).Count == 0 ||
            _authoringControls.RangeValue is < 1 or > 10;
        _clear.Disabled = _running || EnabledConditionCount == 0;
    }

    private int SelectedAct() =>
        _act.ItemCount > 0 && _act.Selected >= 0 ? _act.GetItemId(_act.Selected) : 1;

    private void SelectAct(int act)
    {
        for (int index = 0; index < _act.ItemCount; index++)
        {
            if (_act.GetItemId(index) == act)
            {
                _act.Select(index);
                return;
            }
        }
        if (_act.ItemCount > 0) _act.Select(0);
    }

    private static OptionButton Option(float width)
    {
        var option = new OptionButton
        {
            CustomMinimumSize = new Vector2(width, 38f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin
        };
        Ui1Theme.ApplyOptionButton(option);
        return option;
    }

    private void DetachResultEditors()
    {
        foreach (HBoxContainer editor in new[] { _trashResultEditor, _colorfulResultEditor, _fakeResultEditor })
        {
            editor.GetParent()?.RemoveChild(editor);
            editor.Visible = true;
        }
    }

    private void AddResultOnlyEditor(
        string group,
        HBoxContainer editor,
        bool attached,
        string title,
        Action clear)
    {
        if (attached || !HasResultGroup(group) || _text is null || _names is null) return;
        ModelKey eventKey = group switch
        {
            "trash" => new ModelKey(BaseGameModelKeys.Categories.Event, Beta111EventResultCatalog.TrashHeapEventEntry),
            "colorful" => new ModelKey(BaseGameModelKeys.Categories.Event, Beta111EventResultCatalog.ColorfulPhilosophersEventEntry),
            _ => new ModelKey(BaseGameModelKeys.Categories.Event, Beta111EventResultCatalog.FakeMerchantEventEntry)
        };
        _conditionsHost.AddChild(new EventSequenceConditionCard(
            _thumbnails.Resolve(eventKey),
            title,
            _text.Get(Ui1TextKey.SearchEventResultConditionalHelper),
            title,
            _text.Get(Ui1TextKey.SearchEventRemoveCondition),
            _running,
            () =>
            {
                clear();
                RebuildConditions();
                Changed?.Invoke();
            },
            editor));
    }

    private bool HasResultGroup(string group) => group switch
    {
        "trash" => _trashGrabSlot.SelectedKey.HasValue || _trashDiveSlot.SelectedKey.HasValue,
        "colorful" => _colorfulSlot.SelectedKey.HasValue,
        "fake" => _fakeRelicSlot.SelectedKey.HasValue,
        _ => false
    };

    private static void ClearResultSlots(params SearchHorizontalResultSlot[] slots)
    {
        foreach (SearchHorizontalResultSlot slot in slots) slot.Select(null, notify: false);
    }
}
