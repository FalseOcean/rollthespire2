using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Settings;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.InRunConsole;

/// <summary>Live-run tools. Browsing only reads models; explicit action buttons own mutations.</summary>
internal sealed partial class InRunConsolePanel : Panel
{
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly EventThumbnailProvider _eventThumbnails = new();
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("inrun-console");
    private IGameContentNameResolver _names = null!;
    private IUiTextProvider _uiText = null!;
    private bool _zh;
    private int _tab, _page;
    private bool _contentOpen;
    private Panel _rail = null!, _content = null!;
    private VBoxContainer _filters = null!, _cardFilterRail = null!, _rarityFilter = null!;
    private OptionButton _viewMode = null!, _actFilter = null!, _roomFilter = null!;
    private SpinBox _goldAmount = null!;
    private Button _goldAdd = null!, _goldRemove = null!;
    private Label _goldLabel = null!, _contentTitle = null!;
    private readonly CardPickerFilterState _cardFilters = new();
    private CardPickerContext _cardContext = null!;
    private readonly CardPickerFilterIconProvider _filterIcons = new();
    private NativeCharacterPoolIconProvider _characterIcons = null!;
    private SearchCategoryTabIconProvider _categoryIcons = null!;
    private readonly List<(Button Button, Func<bool> Active)> _filterButtons = [];
    private readonly List<string?> _roomTypes = [];
    private readonly List<(Button Button, string? Rarity)> _rarityButtons = [];
    private string? _selectedRarity;
    private readonly List<int?> _acts = [];
    private bool _hasCategory;
    private bool _busy;
    private readonly HashSet<AbstractModel> _selected = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _selectedSlots = [];
    private PotionModel?[] _potionHoldings = [];
    private readonly Dictionary<int, Button> _tabs = [];
    private HFlowContainer _grid = null!;
    private ScrollContainer _scroll = null!;
    private LineEdit _search = null!;
    private CheckButton _holdings = null!, _upgraded = null!;
    private OptionButton _pile = null!, _potionSlot = null!;
    private SpinBox _quantity = null!;
    private VBoxContainer _cardOptions = null!, _potionOptions = null!, _ancientOptions = null!, _quantityOptions = null!;
    private Button _apply = null!, _remove = null!, _replace = null!, _travel = null!, _godmode = null!;
    private Label _status = null!, _selection = null!, _paging = null!;
    private Button _previous = null!, _next = null!;
    internal Button CloseButton { get; private set; } = null!;
    internal bool IsBusy => _busy;
    internal bool ContentOpen => _contentOpen;
    internal Vector2 DesiredSize => new(_contentOpen ? 1204 : 188, 760);
    internal event Action? LayoutChanged;
    internal int SelectedCount => _selected.Count + _selectedSlots.Count;
    internal event Action? CloseRequested;
    private string T(string key) => _uiText.Get(key);
    private string F(string key, params object?[] args) => _uiText.Format(key, args);
    private PileType TargetPile => _pile.Selected == 1 ? PileType.Hand : PileType.Deck;
    private GameContentKind Kind => _tab switch
    { 0 => GameContentKind.Card, 1 => GameContentKind.Relic, 2 => GameContentKind.Potion, 3 => GameContentKind.Ancient, 5 => GameContentKind.Event, _ => GameContentKind.Encounter };

    internal void Initialize(IRunState run, ModRuntimeSnapshot runtime)
    {
        Name = "ConsolePanel";
        Theme = MainPanelTheme.Create();
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        string language = UiLanguagePreferences.LanguageCode;
        _uiText = JsonUiTextProvider.CreateUi13(language);
        _zh = language == "zh";
        _names = RuntimeGameContentNameResolver.Create(language);
        _characterIcons = new NativeCharacterPoolIconProvider(_icons);
        _categoryIcons = new SearchCategoryTabIconProvider(_icons);
        BuildCardContext();
        Build();
        RefreshHoldings();
        InitializeConsoleState(run);
    }

    private static HBoxContainer Row(Node parent)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); parent.AddChild(row); return row;
    }
    private Button Button(Node parent, string text, Action pressed, string? name = null, bool allowWhileBusy = false)
    {
        var button = _p.Button(text); button.CustomMinimumSize = new(0, 40); button.AddThemeFontSizeOverride("font_size", 18);
        if (name is not null) button.Name = name;
        button.Pressed += () => { if (allowWhileBusy || !_busy) pressed(); };
        parent.AddChild(button); return button;
    }
    private void ResetSelection()
    { _selected.Clear(); _selectedSlots.Clear(); _page = 0; ResetAncientOptions(); }

    internal void RefreshHoldings()
    {
        if (_busy) return;
        _selected.Clear(); _selectedSlots.Clear();
        _potionHoldings = ConsoleActions.GetLocalPlayer()?.PotionSlots.ToArray() ?? [];
        BuildCardContext(); RefreshControls(); if (_contentOpen) RenderGrid(true); ResetAncientOptions();
    }

    private void RefreshControls()
    {
        bool objects = _tab < 3;
        _holdings.Visible = objects;
        _cardOptions.Visible = _tab == 0;
        _upgraded.Visible = !_holdings.ButtonPressed;
        _quantityOptions.Visible = objects && !_holdings.ButtonPressed;
        _potionOptions.Visible = _tab == 2 && !_holdings.ButtonPressed;
        _ancientOptions.Visible = _tab == 3;
        _contentTitle.Text = CategoryTitle(_tab);
        _cardFilterRail.Visible = _tab == 0;
        _rarityFilter.Visible = _tab is 1 or 2;
        _actFilter.Visible = _tab == 3;
        _actChoices.Visible = _tab is 4 or 5;
        _roomFilter.Visible = false;
        _roomChoices.Visible = _tab == 4;
        _apply.Visible = !_holdings.ButtonPressed;
        _remove.Visible = objects && _holdings.ButtonPressed;
        _apply.Text = _tab switch { 3 => T("console.enter.selected.ancient"), 4 => T("console.enter.selected.fight"), 5 => T("console.enter.selected.event"), _ => T("console.add.selected.objects") };
        foreach (var (tab, button) in _tabs) _p.SetModeActive(button, _contentOpen && tab == _tab);
        _travel.Text = F("console.travel", ConsoleActions.IsTravelEnabled ? T("console.on") : T("console.off"));
        _godmode.Text = F("console.god", ConsoleActions.IsGodModeEnabled switch { true => T("console.on"), false => T("console.off"), _ => T("console.n.a") });
        _goldLabel.Text = F("console.gold", ConsoleActions.GetLocalPlayer()?.Gold ?? 0);
        int slot = _potionSlot.Selected;
        _potionSlot.Clear();
        for (int i = 0; i < _potionHoldings.Length; i++)
            _potionSlot.AddItem($"{i + 1}: " + (_potionHoldings[i] is { } potion ? NameOf(potion) : T("console.empty")));
        if (_potionSlot.ItemCount > 0) _potionSlot.Select(Math.Clamp(slot, 0, _potionSlot.ItemCount - 1));
        UpdateSelectionStatus();
    }

    private IEnumerable<AbstractModel> Candidates()
    {
        var player = ConsoleActions.GetLocalPlayer();
        if (_holdings.ButtonPressed)
            return _tab switch
            {
                0 when TargetPile == PileType.Hand => player?.PlayerCombatState?.Hand.Cards.Cast<AbstractModel>() ?? [],
                0 => player?.Deck.Cards.Cast<AbstractModel>() ?? [],
                1 => player?.Relics.Cast<AbstractModel>() ?? [],
                _ => []
            };
        return _tab switch
        {
            0 => ModelDb.AllCards.Cast<AbstractModel>(),
            1 => OrderedRelicCatalog().Cast<AbstractModel>(),
            2 => OrderedPotionCatalog().Cast<AbstractModel>(),
            3 => ModelDb.AllAncients.Cast<AbstractModel>(),
            5 => ModelDb.AllEvents.Where(e => e is not AncientEventModel).Cast<AbstractModel>(),
            _ => ModelDb.AllEncounters.Cast<AbstractModel>()
        };
    }

    private string NameOf(AbstractModel model)
        => _names.Resolve(new ModelKey(model.Id.Category, model.Id.Entry), model switch
        { CardModel => GameContentKind.Card, RelicModel => GameContentKind.Relic, PotionModel => GameContentKind.Potion,
            AncientEventModel => GameContentKind.Ancient, EventModel => GameContentKind.Event, _ => GameContentKind.Encounter });

    private void RenderGrid(bool resetScroll = false)
    {
        InvalidateConsoleScrollRestore();
        if (!_contentOpen) return;
        _scroll.SizeFlagsVertical = _tab == 3 ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill;
        _scroll.CustomMinimumSize = new(0, _tab == 3 ? (_viewMode.Selected == 1 ? 80 : 112) : 0);
        int scrollPosition = resetScroll ? 0 : _scroll.ScrollVertical;
        foreach (var child in _grid.GetChildren()) { _grid.RemoveChild(child); child.QueueFree(); }
        try
        {
            if (_tab == 2 && _holdings.ButtonPressed) { RenderPotionHoldings(); return; }
            string query = _search.Text.Trim();
            var models = Candidates().Select((model, index) => (Model: model, Index: index, Name: NameOf(model)))
                .Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Model.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(item => MatchesQuickFilters(item.Model))
                .OrderBy(item => _tab == 1 && _holdings.ButtonPressed && _selectedRarity is null && item.Model is RelicModel relic
                    ? RarityDisplayOrder(relic.Rarity.ToString()) : 0)
                .ToArray();
            const int pageSize = 60;
            int pages = Math.Max(1, (models.Length + pageSize - 1) / pageSize);
            _page = Math.Clamp(_page, 0, pages - 1);
            foreach (var item in models.Skip(_page * pageSize).Take(pageSize))
            {
                var model = item.Model;
                string title = item.Name;
                if (_holdings.ButtonPressed)
                    title = $"#{item.Index + 1} " + title + (model is CardModel { IsUpgraded: true } card ? " +" + card.CurrentUpgradeLevel : "");
                var tile = CreateTile(model, title, _selected.Contains(model));
                tile.Pressed += () =>
                {
                    if (_busy) return;
                    if (!_selected.Remove(model))
                    {
                        if (_tab >= 3) _selected.Clear();
                        _selected.Add(model);
                    }
                    if (_tab == 3) ResetAncientOptions();
                    RenderGrid();
                };
            }
            if (models.Length == 0) _grid.AddChild(_p.Label(T("console.no.matching.objects"), 20, true));
            _previous.Disabled = _busy || _page == 0; _next.Disabled = _busy || _page + 1 == pages;
            _paging.Text = $"{_page + 1}/{pages} · {models.Length}";
            Callable.From(() => { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = scrollPosition; }).CallDeferred();
            UpdateSelectionStatus();
        }
        catch (Exception ex)
        {
            RuntimeLog.WarnException($"inRunConsoleCatalogFailed=true;tab={_tab}", ex);
            _status.Text = F("console.could.not.read.objects", ex.Message);
        }
    }

    private string CardInstanceDetails(CardModel card)
    {
        string details = F("console.upgrade", card.CurrentUpgradeLevel);
        if (card.Enchantment is { } enchantment)
            details += "\n" + enchantment.Title.GetFormattedText() + " ×" + enchantment.Amount;
        else details += "\n" + T("console.no.enchantment");
        if (card.Affliction is { } affliction)
            details += "\n" + affliction.Title.GetFormattedText() + " ×" + affliction.Amount;
        return details;
    }

    private void RenderPotionHoldings()
    {
        for (int i = 0; i < _potionHoldings.Length; i++)
            {
                int slot = i;
                if (_potionHoldings[i] is not { } potion)
                { _grid.AddChild(_p.Label($"#{i + 1} " + T("console.empty.slot"), 18, true)); continue; }
                string title = $"#{i + 1} " + NameOf(potion);
                if (!title.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) && !potion.Id.ToString().Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                if (!MatchesQuickFilters(potion)) continue;
                var tile = CreateTile(potion, title, _selectedSlots.Contains(slot));
                tile.Pressed += () => { if (_busy) return; if (!_selectedSlots.Remove(slot)) _selectedSlots.Add(slot); RenderGrid(); };
            }
        _paging.Text = ""; _previous.Disabled = _next.Disabled = true;
        UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        _selection.Text = F("console.selectedCount", SelectedCount);
        _apply.Disabled = _busy || _selected.Count == 0;
        _remove.Disabled = _busy || SelectedCount == 0;
        _replace.Disabled = _busy || _selected.Count != 1 || _potionSlot.ItemCount == 0 || _potionSlot.Selected < 0;
    }

    private void ApplySelection()
    {
        int quantity = (int)_quantity.Value;
        var selection = _selected.ToArray();
        switch (_tab)
        {
            case 0: Execute(() => ConsoleActions.AddCardsAsync(selection.Cast<CardModel>(), TargetPile, quantity, _upgraded.ButtonPressed)); break;
            case 1: Execute(() => ConsoleActions.AddRelicsAsync(Enumerable.Range(0, quantity).SelectMany(_ => selection.Cast<RelicModel>()))); break;
            case 2: Execute(() => ConsoleActions.AddPotionsAsync(Enumerable.Range(0, quantity).SelectMany(_ => selection.Cast<PotionModel>()))); break;
            case 3 when selection.SingleOrDefault() is AncientEventModel ancient:
                string? option = _selectedAncientOptionKey;
                Execute(() => ConsoleActions.EnterAncientAsync(ancient, option), closeForTransition: true); break;
            case 5 when selection.SingleOrDefault() is EventModel eventModel:
                Execute(() => ConsoleActions.EnterEventAsync(eventModel), closeForTransition: true); break;
            case 4 when selection.SingleOrDefault() is EncounterModel encounter:
                Execute(() => ConsoleActions.FightAsync(encounter), closeForTransition: true); break;
        }
    }

    private void RemoveSelection()
    {
        var selection = _selected.ToArray();
        if (_tab == 0) Execute(() => ConsoleActions.RemoveCardsAsync(selection.Cast<CardModel>(), TargetPile));
        else if (_tab == 1) Execute(() => ConsoleActions.RemoveRelicsAsync(selection.Cast<RelicModel>()));
        else if (_tab == 2)
        {
            var slots = _selectedSlots.Order().Where(slot => slot < _potionHoldings.Length && _potionHoldings[slot] is not null)
                .Select(slot => (Slot: slot, Potion: _potionHoldings[slot]!)).ToArray();
            Execute(async () =>
            {
                var messages = new List<string>();
                bool success = true, queued = false;
                foreach (var item in slots)
                {
                    var result = await ConsoleActions.RemovePotionAsync(item.Slot, item.Potion);
                    messages.Add(result.Message); success &= result.Success; queued |= result.Queued;
                }
                return new ConsoleActionResult(success, string.Join("\n", messages), queued);
            });
        }
    }

    private void ReplacePotion()
    {
        if (_selected.SingleOrDefault() is not PotionModel replacement) return;
        int slot = _potionSlot.Selected;
        if (slot < 0 || slot >= _potionHoldings.Length) return;
        var expected = _potionHoldings[slot];
        Execute(() => ConsoleActions.ReplacePotionAsync(slot, expected, replacement));
    }

    private async void Execute(Func<Task<ConsoleActionResult>> action, bool closeForTransition = false)
    {
        if (_busy) return;
        if (!ConsoleActions.CanUseConsole)
        {
            _status.Text = T("console.singleplayerOnly");
            return;
        }
        _busy = true; UpdateSelectionStatus();
        _status.Text = T("console.applying");
        try
        {
            var result = await action();
            if (!IsInstanceValid(this) || !IsInsideTree()) return;
            _status.Text = F(result.Queued ? "console.queued" : result.Success ? "console.done" : "console.not.completed", result.Message);
            if (closeForTransition && result.Success && !result.Queued) CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            RuntimeLog.WarnException("inRunConsoleUiActionFailed=true", ex);
            if (IsInstanceValid(this) && IsInsideTree()) _status.Text = F("console.action.failed", ex.Message);
        }
        finally
        {
            _busy = false;
            if (IsInstanceValid(this) && IsInsideTree()) RefreshHoldings();
        }
    }
}
