using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Collections;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Diagnostics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>Independent draft transaction. Only the owner of SaveRequested writes the asset.</summary>
internal sealed partial class CardCollectionEditorOverlay : Control
{
    private const int Columns = 3, NormalRowHeight = 100, CompactRowHeight = 164;
    private sealed record CardRow(ModelKey Key, string Name, string? Rarity, string? Type,
        string[] Pools, bool MetadataIssue = false);
    private sealed class RowView
    {
        internal required Button Button;
        internal required Control Content;
        internal required TextureRect Icon;
        internal required Label Title, Detail, Mark;
        internal ModelKey? Key;
    }
    private readonly Dictionary<ModelKey, CardRow> _catalog = new(ModelKeyComparer.Instance);
    private readonly Dictionary<string, string> _poolNames = new(StringComparer.Ordinal);
    private readonly List<ModelKey> _selected = [];
    private readonly HashSet<ModelKey> _selectedKeys = new(ModelKeyComparer.Instance);
    private CardRow[] _sortedCatalog = [], _filtered = [];
    private readonly PanelContainer _panel;
    private readonly VBoxContainer _filterRail, _selectedSide;
    private readonly Label _filtersTitle, _poolLabel, _rarityLabel, _typeLabel;
    private readonly Label _title, _catalogIssue, _issue, _availableTitle, _selectedTitle, _emptyAvailable, _emptySelected;
    private readonly LineEdit _name, _search;
    private readonly VBoxContainer _pool, _rarity, _type;
    private readonly ScrollContainer _filterScroll, _availableScroll, _selectedScroll;
    private readonly Control _availableRows, _selectedRows;
    private readonly List<RowView> _availableViews = [], _selectedViews = [];
    private readonly Button _save, _cancel;
    private readonly List<Control> _focus = [];
    private IGameContentNameResolver _names = null!;
    private IGameIconResolver _icons = null!;
    private Control? _previousFocus;
    private bool _zh, _opening, _refreshing, _revealSelected;
    private string? _id;
    private string[] _poolValues = [], _rarities = [], _types = [];
    private int _poolSelection, _raritySelection, _typeSelection;
    private int _availableRowHeight = NormalRowHeight, _selectedRowHeight = NormalRowHeight;

    public CardCollectionEditorOverlay()
    {
        Name = "CardCollectionEditorOverlay";
        Visible = false; MouseFilter = MouseFilterEnum.Stop; TopLevel = true;
        ZIndex = UiZLayers.TransactionModal; Theme = MainPanelTheme.Create();
        SetProcessInput(true);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, .68f), MouseFilter = MouseFilterEnum.Stop };
        AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        MainPanelTheme.ApplyPanel(_panel, Ui1SurfaceRole.Drawer, 5, 2, 16); AddChild(_panel);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8); _panel.AddChild(column);
        _title = MainPanelTheme.Label("", Ui1TextRole.SectionTitle, true); column.AddChild(_title);
        _name = new LineEdit { Name = "CollectionName", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        MainPanelTheme.ApplyLineEdit(_name); column.AddChild(_name);
        _catalogIssue = MainPanelTheme.Label("", Ui1TextRole.Warning, true); column.AddChild(_catalogIssue);
        var lists = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        lists.AddThemeConstantOverride("separation", 12); column.AddChild(lists);
        _filterScroll = new ScrollContainer { Name = "CollectionFilterScroll", CustomMinimumSize = new(160, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _filterRail = new VBoxContainer { Name = "CollectionFilters", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _filterRail.AddThemeConstantOverride("separation", 6);
        _filterScroll.AddChild(_filterRail);
        var available = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _selectedSide = new VBoxContainer { CustomMinimumSize = new(180, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        lists.AddChild(_filterScroll); lists.AddChild(available); lists.AddChild(_selectedSide);
        _filtersTitle = MainPanelTheme.Label("", Ui1TextRole.Meta, true); _filterRail.AddChild(_filtersTitle);
        _availableTitle = MainPanelTheme.Label("", Ui1TextRole.Meta, true); available.AddChild(_availableTitle);
        _selectedTitle = MainPanelTheme.Label("", Ui1TextRole.Meta, true); _selectedSide.AddChild(_selectedTitle);
        _search = new LineEdit { Name = "CollectionCardSearch", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        MainPanelTheme.ApplyLineEdit(_search); _filterRail.AddChild(_search);
        _pool = Filter("CollectionPool"); _rarity = Filter("CollectionRarity"); _type = Filter("CollectionType");
        _poolLabel = MainPanelTheme.Label("", Ui1TextRole.Meta); _filterRail.AddChild(_poolLabel); _filterRail.AddChild(_pool);
        _rarityLabel = MainPanelTheme.Label("", Ui1TextRole.Meta); _filterRail.AddChild(_rarityLabel); _filterRail.AddChild(_rarity);
        _typeLabel = MainPanelTheme.Label("", Ui1TextRole.Meta); _filterRail.AddChild(_typeLabel); _filterRail.AddChild(_type);
        (_availableScroll, _availableRows) = BuildList(available, "CollectionAvailableScroll");
        (_selectedScroll, _selectedRows) = BuildList(_selectedSide, "CollectionSelectedScroll");
        _emptyAvailable = MainPanelTheme.Label("", Ui1TextRole.Meta, true); _availableRows.AddChild(_emptyAvailable);
        _emptySelected = MainPanelTheme.Label("", Ui1TextRole.Meta, true); _selectedRows.AddChild(_emptySelected);
        _issue = MainPanelTheme.Label("", Ui1TextRole.Warning, true); column.AddChild(_issue);
        var actions = new HBoxContainer(); actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _cancel = MakeButton("", Cancel); _save = MakeButton("", RequestSave, Ui1ButtonRole.Primary);
        _cancel.CustomMinimumSize = _save.CustomMinimumSize = new Vector2(100, 36);
        actions.AddChild(_cancel); actions.AddChild(_save); column.AddChild(actions);
        _search.TextChanged += _ => FiltersChanged();
        _availableScroll.GetVScrollBar().ValueChanged += _ => RenderViewport(false);
        _selectedScroll.GetVScrollBar().ValueChanged += _ => RenderViewport(true);
        _availableRows.Resized += () => RenderViewport(false); _selectedRows.Resized += () => RenderViewport(true);
        _availableScroll.Resized += () => RenderViewport(false); _selectedScroll.Resized += () => RenderViewport(true);
        Resized += ResizePanel;
    }

    public event Action<string?, string, IReadOnlyList<ModelKey>>? SaveRequested;
    public event Action? Cancelled;
    public bool IsOpen => Visible;
    public override void _Ready() => ResizePanel();
    public override void _Process(double delta)
    {
        if (!Visible) return;
        ResizePanel();
        if (!_revealSelected) return;
        int target = Math.Max(0, (int)(_selected.Count * _selectedRowHeight - _selectedScroll.Size.Y));
        _selectedScroll.ScrollVertical = target;
        _revealSelected = _selectedScroll.ScrollVertical + 1 < target;
        RenderViewport(true);
    }

    public void Open(string language, CardCollectionSnapshot? source, bool copy = false)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        _previousFocus = GetViewport().GuiGetFocusOwner(); _opening = true; _zh = language == "zh";
        _id = copy ? null : source?.Id; _selected.Clear(); _selectedKeys.Clear();
        if (source is not null) foreach (var key in source.Cards) if (_selectedKeys.Add(key)) _selected.Add(key);
        _name.Text = (source?.Name ?? "") + (copy && source is not null ? (_zh ? " 副本" : " copy") : "");
        _name.PlaceholderText = _zh ? "牌集名称" : "Collection name";
        _title.Text = _zh ? (source is null ? "新建牌集" : copy ? "复制牌集" : "编辑牌集")
            : source is null ? "New card collection" : copy ? "Copy card collection" : "Edit card collection";
        _search.Text = ""; _search.PlaceholderText = _zh ? "卡名或 ID" : "Name or ID";
        _filtersTitle.Text = _zh ? "筛选" : "Filters";
        _poolLabel.Text = _zh ? "角色／牌池" : "Character / pool";
        _rarityLabel.Text = _zh ? "稀有度" : "Rarity"; _typeLabel.Text = _zh ? "类型" : "Type";
        _cancel.Text = _zh ? "取消" : "Cancel"; _save.Text = _zh ? "保存" : "Save";
        _emptyAvailable.Text = _zh ? "没有匹配的卡牌。" : "No matching cards.";
        _emptySelected.Text = _zh ? "点击左侧卡牌加入。" : "Click cards on the left to add.";
        _names = RuntimeGameContentNameResolver.Create(language); _icons = new ReflectionGameIconResolver("card-collection-editor");
        CaptureCatalog(); PopulateFilters();
        _filterScroll.ScrollVertical = _availableScroll.ScrollVertical = _selectedScroll.ScrollVertical = 0;
        _opening = false; ShowIssue(""); Visible = true; MoveToFront(); ResizePanel();
        FiltersChanged(); RefreshMembers(); _name.GrabFocus();
    }

    public void ShowIssue(string message) { _issue.Text = message; _issue.Visible = !string.IsNullOrWhiteSpace(message); }
    public void CloseCommitted() => Close();
    public void Cancel() { if (!Visible) return; Close(); Cancelled?.Invoke(); }
    private void Close()
    {
        Visible = false; _revealSelected = false; _selected.Clear(); _selectedKeys.Clear(); _focus.Clear();
        foreach (var view in _availableViews.Concat(_selectedViews)) view.Button.Visible = false;
        if (_previousFocus is { } prior && GodotObject.IsInstanceValid(prior) && prior.IsVisibleInTree()) prior.GrabFocus();
        _previousFocus = null;
    }
    private void RequestSave()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        { ShowIssue(_zh ? "请填写牌集名称。" : "Enter a collection name."); _name.GrabFocus(); return; }
        ShowIssue(""); SaveRequested?.Invoke(_id, _name.Text.Trim(), Array.AsReadOnly(_selected.ToArray()));
    }

    private void CaptureCatalog()
    {
        _catalog.Clear(); _poolNames.Clear(); int issues = 0;
        var memberships = new Dictionary<ModelKey, HashSet<string>>(ModelKeyComparer.Instance);
        void Membership(IEnumerable<CardModel> cards, string filter)
        {
            foreach (var card in cards)
            {
                var key = new ModelKey(card.Id.Category, card.Id.Entry);
                if (!memberships.TryGetValue(key, out var set)) memberships.Add(key, set = new(StringComparer.Ordinal));
                set.Add(filter);
            }
        }
        // Reverse membership avoids CardModel.Pool's throwing/ambiguous lookup.
        // Keep every identity for old members; only the approved pool/rarity/type
        // intersection participates in the new-card view.
        try
        {
            foreach (var character in ModelDb.AllCharacters)
            {
                try
                {
                    var key = new ModelKey(character.Id.Category, character.Id.Entry);
                    string filter = "character:" + key.Serialized;
                    _poolNames[filter] = _names.Resolve(key, GameContentKind.Character);
                    Membership(character.CardPool.AllCards.Concat(character.StartingDeck), filter);
                }
                catch (Exception exception) { issues++; RecordIssue(exception, "CharacterMembership", character.Id.ToString()); }
            }
        }
        catch (Exception exception) { issues++; RecordIssue(exception, "Characters", ""); }
        try
        {
            foreach (var pool in ModelDb.AllCardPools)
            {
                try
                {
                    if (pool is not MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool) continue;
                    var key = new ModelKey(pool.Id.Category, pool.Id.Entry);
                    string filter = "pool:" + key.Serialized; _poolNames[filter] = _zh ? "无色" : "Colorless";
                    Membership(pool.AllCards, filter);
                }
                catch (Exception exception) { issues++; RecordIssue(exception, "PoolMembership", pool.Id.ToString()); }
            }
        }
        catch (Exception exception) { issues++; RecordIssue(exception, "Pools", ""); }
        try
        {
            foreach (CardModel card in ModelDb.AllCards)
            {
                ModelKey key;
                try { key = new ModelKey(card.Id.Category, card.Id.Entry); }
                catch (Exception exception) { issues++; RecordIssue(exception, "Identity", ""); continue; }
                if (!key.IsValid || key.Category != BaseGameModelKeys.Categories.Card)
                { issues++; RecordIssue(new InvalidDataException("Catalog card has invalid identity: " + key.Serialized), "Identity", key.Serialized); continue; }
                string name = key.Serialized; string? rarity = null, type = null; bool metadataIssue = false;
                try { name = _names.Resolve(key, GameContentKind.Card); }
                catch (Exception exception) { issues++; metadataIssue = true; RecordIssue(exception, "Name", key.Serialized); }
                try { rarity = card.Rarity.ToString(); }
                catch (Exception exception) { issues++; metadataIssue = true; RecordIssue(exception, "Rarity", key.Serialized); }
                try { type = card.Type.ToString(); }
                catch (Exception exception) { issues++; metadataIssue = true; RecordIssue(exception, "Type", key.Serialized); }
                _catalog.TryAdd(key, new(key, name, rarity, type,
                    memberships.TryGetValue(key, out var pools) ? pools.ToArray() : ["unknown"], metadataIssue));
            }
        }
        catch (Exception exception) { issues++; RecordIssue(exception, "Catalog", ""); }
        _sortedCatalog = _catalog.Values.Where(IsCandidate).OrderBy(c => c.Name, StringComparer.CurrentCulture).ThenBy(c => c.Key.Serialized, StringComparer.Ordinal).ToArray();
        _catalogIssue.Text = _zh ? $"卡牌目录有 {issues} 项读取问题；已选身份保留，部分元数据可能缺失。"
            : $"Card catalog has {issues} read issue(s); selected identities are retained. Some metadata may be unavailable.";
        _catalogIssue.Visible = issues > 0;
        // Views survive close/open, but cached presentation belongs to this language/catalog.
        foreach (var view in _availableViews.Concat(_selectedViews)) view.Key = null;
    }
    private bool IsCandidate(CardRow card) => (card.Rarity is "Common" or "Uncommon" or "Rare") &&
        (card.Type is "Attack" or "Skill" or "Power") && card.Pools.Any(_poolNames.ContainsKey);
    private void PopulateFilters()
    {
        _rarities = ["Common", "Uncommon", "Rare"];
        _types = ["Attack", "Skill", "Power"];
        _poolValues = _poolNames.Keys.Prepend("").ToArray();
        _poolSelection = _raritySelection = _typeSelection = 0;
        string all = _zh ? "全部" : "All";
        PopulateFilter(_pool, _poolValues.Select(value => (value, value.Length == 0 ? all : _poolNames[value])),
            index => { _poolSelection = index; FiltersChanged(); });
        PopulateFilter(_rarity, _rarities.Prepend("").Select(value => (value, value.Length == 0 ? all : MetadataName(value))),
            index => { _raritySelection = index; FiltersChanged(); });
        PopulateFilter(_type, _types.Prepend("").Select(value => (value, value.Length == 0 ? all : MetadataName(value))),
            index => { _typeSelection = index; FiltersChanged(); });
    }
    private static void PopulateFilter(VBoxContainer filter, IEnumerable<(string Value, string Label)> choices, Action<int> selected)
    {
        foreach (Node child in filter.GetChildren()) { filter.RemoveChild(child); child.QueueFree(); }
        var group = new ButtonGroup { AllowUnpress = false };
        var buttons = new List<Button>();
        foreach (var (value, label) in choices)
        {
            int index = buttons.Count;
            var button = new Button { Text = label, Name = filter.Name + "Choice" + index, ToggleMode = true, ButtonGroup = group,
                Alignment = HorizontalAlignment.Left, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new(0, 28), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            button.SetMeta("filter_value", value);
            button.SetPressedNoSignal(index == 0);
            StyleFilterButton(button, index == 0);
            button.Pressed += () =>
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    buttons[i].SetPressedNoSignal(i == index);
                    StyleFilterButton(buttons[i], i == index);
                }
                selected(index);
            };
            buttons.Add(button); filter.AddChild(button);
        }
    }
    private static void StyleFilterButton(Button button, bool selected)
    {
        MainPanelTheme.ApplyButton(button, selected ? Ui1ButtonRole.NavigationSelected : Ui1ButtonRole.Secondary);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            if (button.GetThemeStylebox(state) is StyleBoxFlat box)
            {
                box.ContentMarginTop = box.ContentMarginBottom = 3;
                box.ContentMarginLeft = box.ContentMarginRight = 10;
            }
    }
    private void FiltersChanged()
    {
        if (_opening) return;
        string query = _search.Text.Trim(), pool = _poolValues[_poolSelection];
        string? rarity = _raritySelection > 0 ? _rarities[_raritySelection - 1] : null;
        string? type = _typeSelection > 0 ? _types[_typeSelection - 1] : null;
        _filtered = _sortedCatalog.Where(c =>
            (query.Length == 0 || c.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || c.Key.Serialized.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (pool.Length == 0 || c.Pools.Contains(pool, StringComparer.Ordinal)) &&
            (rarity is null || c.Rarity is null || c.Rarity == rarity) && (type is null || c.Type is null || c.Type == type)).ToArray();
        _availableScroll.ScrollVertical = 0;
        _availableRows.CustomMinimumSize = new(0, Math.Max(1, (_filtered.Length + Columns - 1) / Columns) * _availableRowHeight);
        _availableTitle.Text = (_zh ? "卡牌目录" : "Card catalog") + $" · {_filtered.Length}";
        RenderViewport(false);
    }
    private void RefreshMembers()
    {
        _selectedTitle.Text = (_zh ? "已加入" : "Added cards") + $" · {_selected.Count}";
        _selectedRows.CustomMinimumSize = new(0, Math.Max(1, _selected.Count) * _selectedRowHeight);
        foreach (var view in _availableViews.Where(v => v.Button.Visible && v.Key.HasValue)) UpdateState(view, false);
        RenderViewport(true); RefreshFocus();
    }
    private void ChangeMembership(RowView view, bool selectedSide)
    {
        if (view.Key is not { } key) return;
        if (selectedSide) { if (!_selectedKeys.Remove(key)) return; _selected.Remove(key); }
        else
        {
            if (!_catalog.TryGetValue(key, out var card) || !IsCandidate(card) || !_selectedKeys.Add(key)) return;
            _selected.Add(key); _revealSelected = true;
        }
        RefreshMembers();
    }

    private void RenderViewport(bool selectedSide)
    {
        if (_opening || !Visible || _refreshing) return;
        _refreshing = true;
        try
        {
            var scroll = selectedSide ? _selectedScroll : _availableScroll;
            var rows = selectedSide ? _selectedRows : _availableRows;
            var views = selectedSide ? _selectedViews : _availableViews;
            int columns = selectedSide ? 1 : Columns;
            float width = Math.Max(0, rows.Size.X), cellWidth = Math.Max(0, (width - (columns - 1) * 6) / columns);
            bool compact = cellWidth < 190;
            int height = compact ? CompactRowHeight : NormalRowHeight;
            if (selectedSide) _selectedRowHeight = height; else _availableRowHeight = height;
            int count = selectedSide ? _selected.Count : _filtered.Length;
            rows.CustomMinimumSize = new(0, Math.Max(1, (count + columns - 1) / columns) * height);
            int firstRow = Math.Max(0, scroll.ScrollVertical / height - 1);
            int lastRow = Math.Min((count + columns - 1) / columns, (int)Math.Ceiling((scroll.ScrollVertical + scroll.Size.Y) / height) + 1);
            int visibleCount = Math.Max(0, (lastRow - firstRow) * columns);
            while (views.Count < visibleCount) views.Add(CreateRow(rows, selectedSide));
            for (int i = 0; i < views.Count; i++)
            {
                int index = firstRow * columns + i; var view = views[i];
                view.Button.Visible = i < visibleCount && index < count;
                if (!view.Button.Visible) continue;
                view.Button.Position = new((index % columns) * (cellWidth + 6), (index / columns) * height);
                view.Button.Size = new(cellWidth, height - 4); view.Button.SetMeta("catalog_index", index);
                LayoutRow(view, cellWidth, height - 4, compact);
                var key = selectedSide ? _selected[index] : _filtered[index].Key;
                CardRow card = _catalog.TryGetValue(key, out var found) ? found : new(key, key.Serialized, null, null, []);
                BindRow(view, card, selectedSide);
            }
            var empty = selectedSide ? _emptySelected : _emptyAvailable;
            empty.Visible = count == 0; empty.Size = new(width, 52);
            RefreshFocus();
        }
        finally { _refreshing = false; }
    }
    private RowView CreateRow(Control parent, bool selectedSide)
    {
        var button = new Button { ClipContents = true, FocusMode = FocusModeEnum.All };
        MainPanelTheme.ApplyButton(button, Ui1ButtonRole.Secondary); parent.AddChild(button);
        var content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        button.AddChild(content); content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var icon = new TextureRect { MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered }; content.AddChild(icon);
        var title = MainPanelTheme.Label("", Ui1TextRole.Body);
        title.MouseFilter = MouseFilterEnum.Ignore; title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.MaxLinesVisible = 2; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; content.AddChild(title);
        var detail = MainPanelTheme.Label("", Ui1TextRole.Meta);
        detail.MouseFilter = MouseFilterEnum.Ignore; detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; content.AddChild(detail);
        var mark = MainPanelTheme.Label("", Ui1TextRole.Body); mark.MouseFilter = MouseFilterEnum.Ignore; content.AddChild(mark);
        var view = new RowView { Button = button, Content = content, Icon = icon, Title = title, Detail = detail, Mark = mark };
        button.Pressed += () => ChangeMembership(view, selectedSide); return view;
    }
    private static void LayoutRow(RowView view, float width, float height, bool compact)
    {
        view.Icon.Position = new(8, compact ? 6 : 12); view.Icon.Size = new(52, 72);
        view.Title.AddThemeFontSizeOverride("font_size", compact ? 16 : 18);
        view.Detail.AddThemeFontSizeOverride("font_size", compact ? 13 : 14);
        view.Title.Position = compact ? new(8, 82) : new(68, 8);
        view.Title.Size = new(Math.Max(0, compact ? width - 16 : width - 94), compact ? 46 : 50);
        view.Detail.Position = compact ? new(8, 132) : new(68, 64);
        view.Detail.Size = new(Math.Max(0, compact ? width - 16 : width - 94), 22);
        view.Mark.Position = new(Math.Max(0, width - 24), compact ? 16 : (height - 32) / 2);
        view.Mark.Size = new(18, 32);
    }
    private void BindRow(RowView view, CardRow card, bool selectedSide)
    {
        if (view.Key != card.Key)
        {
            view.Key = card.Key; view.Button.SetMeta("card_key", card.Key.Serialized); view.Title.Text = card.Name;
            bool missing = !_catalog.ContainsKey(card.Key);
            view.Detail.Text = missing ? (_zh ? "目录缺失 · 点击移除" : "Missing · click to remove") : card.MetadataIssue
                ? (_zh ? "元数据不完整" : "Incomplete metadata") : MetadataName(card.Rarity) + " · " + MetadataName(card.Type);
            view.Icon.Texture = null;
            if (!missing) try { view.Icon.Texture = _icons.Resolve(card.Key, GameContentKind.Card, IconVariant.Small).Texture; }
                catch (Exception exception) { RecordIssue(exception, "Icon", card.Key.Serialized); }
        }
        UpdateState(view, selectedSide);
    }
    private void UpdateState(RowView view, bool selectedSide)
    {
        bool added = view.Key is { } key && _selectedKeys.Contains(key);
        view.Button.Disabled = !selectedSide && added;
        view.Content.Modulate = new Color(1, 1, 1, !selectedSide && added ? .42f : 1);
        view.Mark.Text = selectedSide ? "−" : added ? "✓" : "+";
    }
    private void RefreshFocus()
    {
        _focus.Clear(); _focus.AddRange([_name, _search]);
        _focus.AddRange(new[] { _pool, _rarity, _type }.SelectMany(filter => filter.GetChildren().OfType<Button>()));
        _focus.AddRange(_availableViews.Concat(_selectedViews).Where(v => v.Button.Visible && !v.Button.Disabled).Select(v => (Control)v.Button));
        _focus.Add(_filterScroll.GetVScrollBar()); _focus.Add(_availableScroll.GetVScrollBar());
        _focus.Add(_selectedScroll.GetVScrollBar()); _focus.AddRange([_cancel, _save]);
    }
    private static VBoxContainer Filter(string name)
    {
        var filter = new VBoxContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        filter.AddThemeConstantOverride("separation", 2); return filter;
    }
    private static (ScrollContainer Scroll, Control Rows) BuildList(VBoxContainer parent, string name)
    {
        var scroll = new ScrollContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll); var rows = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }; scroll.AddChild(rows); return (scroll, rows);
    }
    private static Button MakeButton(string text, Action? pressed, Ui1ButtonRole role = Ui1ButtonRole.Secondary)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(36, 32) };
        MainPanelTheme.ApplyButton(button, role); if (pressed is not null) button.Pressed += pressed; return button;
    }
    private void ResizePanel()
    {
        if (IsInsideTree())
        {
            // This transaction uses screen space, independent of the Shell's
            // fixed-canvas scale, so narrow windows keep readable card names.
            var viewport = GetViewport().GetVisibleRect();
            SetAnchorsPreset(LayoutPreset.TopLeft); Position = viewport.Position; Size = viewport.Size;
        }
        Vector2 available = Size - new Vector2(24, 24);
        float panelWidth = Math.Min(1440, Math.Max(0, available.X));
        _filterScroll.CustomMinimumSize = new(panelWidth < 950 ? 140 : 180, 0);
        _selectedSide.CustomMinimumSize = new(panelWidth < 950 ? 148 : 240, 0);
        _panel.Size = new(panelWidth, Math.Min(780, Math.Max(0, available.Y)));
        _panel.Position = (Size - _panel.Size) / 2;
    }
    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.Escape) { Cancel(); GetViewport().SetInputAsHandled(); }
        else if (key.Keycode == Key.Tab)
        {
            Control[] focus = _focus.Where(control => GodotObject.IsInstanceValid(control) && control.IsVisibleInTree() &&
                control is not BaseButton { Disabled: true }).ToArray();
            if (focus.Length == 0) return;
            int index = Array.IndexOf(focus, GetViewport().GuiGetFocusOwner());
            focus[(index + (key.ShiftPressed ? focus.Length - 1 : 1) + focus.Length) % focus.Length].GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }
    private string MetadataName(string? value) => !_zh ? value ?? "Unknown" : value switch
    {
        "Basic" => "基础", "Common" => "普通", "Uncommon" => "罕见", "Rare" => "稀有", "Special" => "特殊",
        "Ancient" => "先古", "Event" => "事件", "Curse" => "诅咒", "Status" => "状态",
        "Attack" => "攻击", "Skill" => "技能", "Power" => "能力", "Quest" => "任务", null => "未知", _ => value
    };
    private static void RecordIssue(Exception exception, string stage, string identity)
    {
        try { RuntimeIssueLedger.Current.Record(exception, "CardCollectionCatalogReadFailed", nameof(CardCollectionEditorOverlay),
            stage, "issues.capture_failed", RuntimeIssueSeverity.Warning, identity); }
        catch { /* Presentation diagnostics do not acquire authority over the draft. */ }
    }
}
