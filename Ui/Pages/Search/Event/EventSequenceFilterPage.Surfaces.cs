using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Event;

internal sealed partial class EventSequenceFilterPage
{
    private readonly List<EventSequenceSearchCondition> _retainedConditions = new();
    private TabContainer _eventTabs = null!;
    private TabContainer _queueTabs = null!;
    private TabContainer _resultTabs = null!;
    private VBoxContainer _stableResults = null!;
    private VBoxContainer _fragileResults = null!;
    private OptionButton _simpleAct = null!;
    private OptionButton _simpleLength = null!;
    private OptionButton _simpleOrder = null!;
    private HFlowContainer _simpleSlots = null!;
    private Label _simpleHelp = null!;
    private Label _stableHelp = null!;
    private Label _fragileHelp = null!;
    private Label _simplePrefix = null!;
    private readonly List<(Label Label, string Key)> _resultTitles = new();
    // Presentation only. Canonical W predicates remain the sole query truth.
    private readonly int[] _simpleLengths = [3, 3, 3];
    private readonly bool[] _simpleOrdered = [true, true, true];

    private static VBoxContainer Column() => new() { SizeFlagsHorizontal = SizeFlags.ExpandFill,
        SizeFlagsVertical = SizeFlags.ExpandFill };

    private HBoxContainer _queueNavigation = null!;
    private HBoxContainer _resultNavigation = null!;
    private readonly List<(Button Button, TabContainer Tabs, int Index)> _navigationButtons = new();

    private static TabContainer Tabs()
    {
        var tabs = new TabContainer { TabsVisible = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        tabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        return tabs;
    }

    private HBoxContainer Navigation(TabContainer tabs)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            var button = new Button { CustomMinimumSize = new Vector2(76, 38) };
            button.Pressed += () => tabs.CurrentTab = index;
            row.AddChild(button); _navigationButtons.Add((button, tabs, index));
        }
        return row;
    }

    private void RefreshEventNavigation()
    {
        foreach (var (button, tabs, index) in _navigationButtons)
        {
            button.Text = tabs.GetTabTitle(index);
            Ui1Theme.ApplyButton(button, tabs.CurrentTab == index ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
        }
        _queueNavigation.Visible = _eventTabs.CurrentTab == 0;
        _resultNavigation.Visible = _eventTabs.CurrentTab == 1;
    }

    private VBoxContainer BuildEventSurfaces(VBoxContainer root)
    {
        _eventTabs = Tabs();
        var navigation = new HFlowContainer();
        navigation.AddThemeConstantOverride("h_separation", 20);
        navigation.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(navigation); root.AddChild(_eventTabs);
        _queueTabs = Tabs(); _resultTabs = Tabs();
        _eventTabs.AddChild(_queueTabs); _eventTabs.AddChild(_resultTabs);
        var simple = Column(); var advanced = Column();
        _queueTabs.AddChild(simple); _queueTabs.AddChild(advanced);
        _stableResults = Column(); _fragileResults = Column();
        foreach (var column in new[] { _stableResults, _fragileResults })
        {
            var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(column); _resultTabs.AddChild(scroll);
            column.AddThemeConstantOverride("separation", 12);
        }
        navigation.AddChild(Navigation(_eventTabs));
        _queueNavigation = Navigation(_queueTabs); _resultNavigation = Navigation(_resultTabs);
        navigation.AddChild(_queueNavigation); navigation.AddChild(_resultNavigation);
        _resultNavigation.Hide();
        _stableHelp = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _fragileHelp = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true);
        _stableResults.AddChild(_stableHelp); _fragileResults.AddChild(_fragileHelp);
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 10);
        _simpleAct = Option(130); _simpleLength = Option(80); _simpleOrder = Option(145);
        _simplePrefix = Ui1Theme.Label(string.Empty, Ui1TextRole.Body);
        for (int i = 1; i <= 10; i++) _simpleLength.AddItem(i.ToString(), i);
        row.AddChild(_simpleAct); row.AddChild(_simplePrefix); row.AddChild(_simpleLength); row.AddChild(_simpleOrder);
        simple.AddChild(row);
        _simpleHelp = Ui1Theme.Label(string.Empty, Ui1TextRole.Meta, true); simple.AddChild(_simpleHelp);
        _simpleSlots = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _simpleSlots.AddThemeConstantOverride("h_separation", 8); _simpleSlots.AddThemeConstantOverride("v_separation", 8);
        simple.AddChild(_simpleSlots);
        _simpleAct.ItemSelected += _ => { TryCancelTransientSurface(); RefreshSimpleQueue(); };
        _simpleLength.ItemSelected += _ => EditSimpleOptions();
        _simpleOrder.ItemSelected += _ => EditSimpleOptions();
        _queueTabs.TabChanged += _ => { TryCancelTransientSurface(); RefreshSimpleQueue(); RefreshEventNavigation(); };
        _eventTabs.TabChanged += _ => { TryCancelTransientSurface(); RefreshEventNavigation(); };
        _resultTabs.TabChanged += _ => { TryCancelTransientSurface(); RefreshEventNavigation(); };
        return advanced;
    }

    private void MountResultEditors()
    {
        void Add(VBoxContainer parent, HBoxContainer editor, string key)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Card, 4f, 1, 12f);
            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var title = Ui1Theme.Label(string.Empty, Ui1TextRole.SectionTitle);
            _resultTitles.Add((title, key)); column.AddChild(title); column.AddChild(editor);
            panel.AddChild(column); parent.AddChild(panel);
        }
        Add(_stableResults, _trashResultEditor, Ui1TextKey.SearchEventResultTrashHeap);
        Add(_stableResults, _colorfulResultEditor, Ui1TextKey.SearchEventResultColorful);
        Add(_stableResults, _fakeResultEditor, Ui1TextKey.SearchEventResultFakeMerchant);
        Add(_fragileResults, _morphicResultEditor, "ui1.search.event.morphic_title");
    }

    private void LocalizeEventSurfaces()
    {
        if (_text is null) return;
        _eventTabs.SetTabTitle(0, _text.Get("ui1.search.event.queue"));
        _eventTabs.SetTabTitle(1, _text.Get("ui1.search.event.results"));
        _queueTabs.SetTabTitle(0, _text.Get("ui1.search.event.simple"));
        _queueTabs.SetTabTitle(1, _text.Get("ui1.search.event.advanced"));
        _resultTabs.SetTabTitle(0, _text.Get("ui1.search.event.stable"));
        _resultTabs.SetTabTitle(1, _text.Get("ui1.search.event.fragile"));
        RefreshEventNavigation();
        _stableHelp.Text = _text.Get("ui1.search.event.stable_help");
        _fragileHelp.Text = _text.Get("ui1.search.event.fragile_help");
        _simplePrefix.Text = _text.Get("ui1.search.event.first_events");
        int act = Math.Max(0, _simpleAct.Selected);
        _simpleAct.Clear();
        for (int i = 1; i <= 3; i++) _simpleAct.AddItem(ActText(i), i);
        _simpleAct.Select(act);
        _simpleOrder.Clear();
        _simpleOrder.AddItem(_text.Get("ui1.search.event.ordered"));
        _simpleOrder.AddItem(_text.Get("ui1.search.event.unordered"));
        foreach (var (label, key) in _resultTitles) label.Text = _text.Get(key);
    }

    private void ResetSimplePresentation()
    {
        Array.Fill(_simpleLengths, 3); Array.Fill(_simpleOrdered, true);
        int firstAct = _conditions.Select(c => c.Act).Concat(_retainedConditions.Select(c => c.Act))
            .Where(a => a is >= 1 and <= 3).DefaultIfEmpty(1).Min();
        _simpleAct.Select(firstAct - 1);
        // Restored advanced predicates are never silently flattened to slot semantics.
        _queueTabs.CurrentTab = Enumerable.Range(1, 3).All(a => TryReadSimple(a, out _, out _, out _)) ? 0 : 1;
    }

    private bool TryReadSimple(int act, out ModelKey?[] slots, out int length, out bool ordered)
    {
        length = _simpleLengths[act - 1]; ordered = _simpleOrdered[act - 1];
        slots = [];
        if (_retainedConditions.Any(c => c.Act == act)) return false;
        var rows = _conditions.Where(c => c.Act == act).ToArray();
        if (rows.Any(c => c.MatchMode != EventSequenceUiMatchMode.Appears || c.RangeValue is < 1 or > 10)) return false;
        if (rows.Length > 0)
        {
            ordered = rows.All(c => c.RangeMode == SearchSequenceRangeMode.ExactSlot);
            if (!ordered && (rows.Any(c => c.RangeMode != SearchSequenceRangeMode.FirstN) || rows.Select(c => c.RangeValue).Distinct().Count() != 1)) return false;
            length = ordered ? Math.Max(length, rows.Max(c => c.RangeValue)) : rows[0].RangeValue;
            if (rows.Length > length || rows.Select(c => c.EventKey).Distinct().Count() != rows.Length ||
                ordered && rows.Select(c => c.RangeValue).Distinct().Count() != rows.Length) return false;
        }
        slots = new ModelKey?[length];
        for (int i = 0; i < rows.Length; i++) slots[ordered ? rows[i].RangeValue - 1 : i] = rows[i].EventKey;
        return true;
    }

    private void RefreshSimpleQueue()
    {
        if (_simpleSlots is null || _text is null || _names is null) return;
        int act = Math.Max(0, _simpleAct.Selected) + 1;
        bool editable = TryReadSimple(act, out var slots, out int length, out bool ordered);
        _simpleAct.Disabled = _running;
        _simpleLength.Disabled = _simpleOrder.Disabled = _running || !editable;
        _simpleLength.Select(length - 1); _simpleOrder.Select(ordered ? 0 : 1);
        _simpleHelp.Text = _text.Get(!editable ? "ui1.search.event.advanced_preserved" : ordered ? "ui1.search.event.ordered_help" : "ui1.search.event.unordered_help");
        foreach (Node child in _simpleSlots.GetChildren()) { _simpleSlots.RemoveChild(child); child.QueueFree(); }
        if (!editable) return;
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = i;
            const float tileWidth = 124;
            var tile = new Control { CustomMinimumSize = new Vector2(tileWidth, 132) };
            string name = slots[i] is { } key ? _names.Resolve(key, GameContentKind.Event) : _text.Get("ui1.search.event.any");
            var button = new Button { TooltipText = $"{i + 1} · {name}", Disabled = _running || !_catalog.CatalogAvailable };
            Ui1Theme.ApplyButton(button, Ui1ButtonRole.Secondary);
            tile.AddChild(button); button.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            button.AddChild(content); content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.OffsetLeft = 8; content.OffsetTop = 20; content.OffsetRight = -8; content.OffsetBottom = -6;
            if (slots[i] is { } eventKey)
                content.AddChild(new EventThumbnailView(_thumbnails.Resolve(eventKey), new Vector2(68, 68),
                    EventThumbnailPresentation.ConditionSquareCrop) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
            else
            {
                var any = Ui1Theme.Label("+", Ui1TextRole.Accent);
                any.CustomMinimumSize = new Vector2(68, 68); any.HorizontalAlignment = HorizontalAlignment.Center;
                any.VerticalAlignment = VerticalAlignment.Center; any.MouseFilter = MouseFilterEnum.Ignore;
                content.AddChild(any);
            }
            var label = Ui1Theme.Label(name, Ui1TextRole.Meta, true);
            label.CustomMinimumSize = new Vector2(0, 34);
            label.HorizontalAlignment = HorizontalAlignment.Center; label.MaxLinesVisible = 2;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.MouseFilter = MouseFilterEnum.Ignore;
            content.AddChild(label);
            var ordinal = Ui1Theme.Label((i + 1).ToString(), Ui1TextRole.Meta);
            ordinal.Position = new Vector2(8, 5); ordinal.MouseFilter = MouseFilterEnum.Ignore; tile.AddChild(ordinal);
            button.Pressed += () => _picker.Open(_text.Get(Ui1TextKey.SearchEventSelectEvent), _catalog.ProfileId, 1,
                _catalog.CandidatesForAct(act).Where(c => !slots.Where((_, j) => j != slot).Contains(c.EventKey)).ToArray(),
                candidate => { if (candidate is not null) SetSimpleSlot(act, slot, candidate.EventKey); });
            var clear = new Button { Text = "×", Position = new Vector2(tileWidth - 28, 3), CustomMinimumSize = new Vector2(24, 24), Disabled = _running || !slots[i].HasValue,
                TooltipText = _text.Get("ui1.search.event.any") };
            Ui1Theme.ApplyButton(clear, Ui1ButtonRole.Ghost);
            foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
                Ui1Theme.SetMargins((StyleBoxFlat)clear.GetThemeStylebox(state), 2, 2, 2, 2);
            clear.Visible = slots[i].HasValue;
            clear.Pressed += () => SetSimpleSlot(act, slot, null);
            tile.AddChild(clear); _simpleSlots.AddChild(tile);
        }
    }

    private void EditSimpleOptions()
    {
        int act = Math.Max(0, _simpleAct.Selected) + 1;
        if (_running || !TryReadSimple(act, out var slots, out _, out _)) return;
        int length = _simpleLength.Selected + 1;
        Array.Resize(ref slots, length);
        CommitSimple(act, slots, _simpleOrder.Selected == 0);
    }

    private void SetSimpleSlot(int act, int slot, ModelKey? key)
    {
        if (_running || !TryReadSimple(act, out var slots, out _, out bool ordered) || slot >= slots.Length) return;
        slots[slot] = key;
        CommitSimple(act, slots, ordered);
    }

    private void CommitSimple(int act, ModelKey?[] slots, bool ordered)
    {
        _conditions.RemoveAll(c => c.Act == act);
        _simpleLengths[act - 1] = slots.Length; _simpleOrdered[act - 1] = ordered;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is { } key)
                _conditions.Add(new(Guid.NewGuid(), act, ordered ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN,
                    ordered ? i + 1 : slots.Length, key, EventSequenceUiMatchMode.Appears));
        RebuildConditions(); RefreshBuilderState(); Changed?.Invoke();
    }
}
