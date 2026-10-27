using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    // Ephemeron ownership keeps snapshots alive only with their run, even when a
    // selected live model itself points back to that run. Nothing here is serialized.
    private static readonly ConditionalWeakTable<IRunState, ConsoleRunUiState> RunUiStates = new();
    private static readonly bool?[] SessionCategoryCompactIcons = new bool?[6];
    private IRunState? _consoleRun;
    private ConsolePreferencesStore? _consolePreferences;
    private bool _consoleStateInitialized, _consoleStateCaptured;
    private bool _consoleScrollRestorePending;
    private int _consoleScrollRestoreRevision;

    internal void InitializeConsoleState(IRunState run)
    {
        _consoleRun = run;
        _consolePreferences = new ConsolePreferencesStore(OS.GetUserDataDir());
        ApplyCategoryDisplayPreference();
        _consoleStateInitialized = true;
        if (!RunUiStates.TryGetValue(run, out var state) || state.Snapshot is not { } snapshot) return;
        try { RestoreConsoleState(snapshot); }
        catch (Exception ex)
        {
            // A browsing snapshot is auxiliary state and cannot prevent opening the UI.
            RuntimeLog.WarnException("inRunConsoleSessionRestoreFailed=true", ex);
            _selected.Clear(); _selectedSlots.Clear();
            RefreshControls();
        }
    }

    private void RememberDisplayMode()
    {
        if (_restoring) return;
        bool compact = _viewMode.Selected == 1;
        SessionCategoryCompactIcons[_tab] = compact;
        _consolePreferences?.SaveCompactIcons(_tab, compact);
    }

    private void ApplyCategoryDisplayPreference()
    {
        // Session memory also preserves the user's choice when the auxiliary disk
        // write fails or an unknown preference document must remain untouched.
        bool compact = SessionCategoryCompactIcons[_tab] ??
            _consolePreferences?.LoadCompactIcons(_tab) ?? true;
        SessionCategoryCompactIcons[_tab] = compact;
        _viewMode.Select(compact ? 1 : 0);
    }

    internal void CaptureConsoleState()
    {
        if (!_consoleStateInitialized || _consoleStateCaptured || _consoleRun is null) return;
        try
        {
            RunUiStates.GetValue(_consoleRun, static _ => new()).Snapshot = new ConsoleSnapshot(
                _tab, _hasCategory, _contentOpen, _search.Text, _selectedRarity,
                _acts.ElementAtOrDefault(_actFilter.Selected), _roomTypes.ElementAtOrDefault(_roomFilter.Selected),
                _cardFilters.AllCharacters, _cardFilters.Characters.ToArray(), _cardFilters.Rarities.ToArray(),
                _cardFilters.Types.ToArray(), _colorlessOnly, _page, _scroll.ScrollVertical,
                NamedScrollPosition("ConsoleFilters"), NamedScrollPosition("ConsoleCategoryScroll"), NamedScrollPosition("ConsoleAncientOptionScroll"),
                _holdings.ButtonPressed, _pile.Selected, _quantity.Value, _upgraded.ButtonPressed, _goldAmount.Value,
                _selected.ToArray(), _selectedSlots.Where(slot => slot >= 0 && slot < _potionHoldings.Length && _potionHoldings[slot] is not null)
                    .Select(slot => new PotionSlotSelection(slot, _potionHoldings[slot]!)).ToArray(),
                _potionSlot.Selected, _potionHoldings.ElementAtOrDefault(_potionSlot.Selected), _selectedAncientOptionKey);
            _consoleStateCaptured = true;
        }
        catch (Exception ex) { RuntimeLog.WarnException("inRunConsoleSessionCaptureFailed=true", ex); }
    }

    private void RestoreConsoleState(ConsoleSnapshot snapshot)
    {
        if (!_tabs.ContainsKey(snapshot.Tab)) return;
        _restoring = true;
        try
        {
            _tab = snapshot.Tab; _hasCategory = snapshot.HasCategory;
            ApplyCategoryDisplayPreference();
            PrepareCategoryFilters();
            _search.Text = snapshot.Search;
            _selectedRarity = snapshot.Rarity;
            _actFilter.Select(Math.Max(0, _acts.IndexOf(snapshot.Act)));
            _roomFilter.Select(Math.Max(0, _roomTypes.IndexOf(snapshot.Room)));
            _cardFilters.AllCharacters = snapshot.AllCharacters;
            _cardFilters.Characters.UnionWith(snapshot.Characters);
            _cardFilters.Rarities.UnionWith(snapshot.CardRarities);
            _cardFilters.Types.UnionWith(snapshot.CardTypes);
            _colorlessOnly = snapshot.ColorlessOnly;
            _holdings.SetPressedNoSignal(snapshot.Holdings);
            _pile.Select(Math.Clamp(snapshot.Pile, 0, _pile.ItemCount - 1));
            _quantity.Value = snapshot.Quantity;
            _upgraded.SetPressedNoSignal(snapshot.Upgraded);
            _goldAmount.Value = snapshot.GoldAmount;
            _page = snapshot.Page;
            _potionHoldings = ConsoleActions.GetLocalPlayer()?.PotionSlots.ToArray() ?? [];
            // Reference identity is essential: a removed inventory instance must never
            // become a different copy of the same card/relic/potion on reopen.
            var current = new HashSet<AbstractModel>(Candidates(), ReferenceEqualityComparer.Instance);
            _selected.Clear(); _selected.UnionWith(snapshot.Selected.Where(current.Contains));
            _selectedSlots.Clear();
            if (_tab == 2 && snapshot.Holdings)
                foreach (var selection in snapshot.SelectedPotionSlots)
                    if (selection.Slot < _potionHoldings.Length && ReferenceEquals(_potionHoldings[selection.Slot], selection.Potion))
                        _selectedSlots.Add(selection.Slot);
            _contentOpen = snapshot.HasCategory;
            _content.Visible = _contentOpen;
            RefreshControls(); RefreshFilterButtons(); ResetAncientOptions();
            RestoreAncientOptionSelection(snapshot.AncientOptionKey);
            // Render while open even for a collapsed snapshot, so expanding the same
            // category has its existing catalog and selection ready.
            if (_contentOpen) RenderGrid();
            _contentOpen = snapshot.ContentOpen && snapshot.HasCategory;
            _content.Visible = _contentOpen;
            RefreshControls();
            int slot = snapshot.PotionSlot;
            _potionSlot.Select(slot >= 0 && slot < _potionHoldings.Length &&
                ReferenceEquals(_potionHoldings[slot], snapshot.PotionSlotContents) ? slot : -1);
            UpdateSelectionStatus();
            ObserveConsoleScrollInput();
            _consoleScrollRestorePending = true;
            int revision = ++_consoleScrollRestoreRevision;
            if (IsInsideTree()) RestoreScrollAfterLayout(snapshot, revision);
            else Ready += () => RestoreScrollAfterLayout(snapshot, revision);
        }
        finally { _restoring = false; }
    }

    private void InvalidateConsoleScrollRestore()
    {
        if (_restoring || !_consoleScrollRestorePending) return;
        _consoleScrollRestorePending = false;
        _consoleScrollRestoreRevision++;
    }

    private void ObserveConsoleScrollInput()
    {
        // Observe only these scroll surfaces: the launcher event that opened the
        // console cannot cancel restoration, and normal input is never consumed.
        foreach (string name in new[] { "ConsoleCatalogScroll", "ConsoleFilters", "ConsoleCategoryScroll", "ConsoleAncientOptionScroll" })
            if (FindChild(name, recursive: true, owned: false) is ScrollContainer scroll)
            {
                scroll.GuiInput += OnConsoleScrollInput;
                scroll.GetVScrollBar().GuiInput += OnConsoleScrollInput;
            }
    }

    private void OnConsoleScrollInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true } or
            InputEventMouseMotion { ButtonMask: not (MouseButtonMask)0 } or
            InputEventScreenTouch { Pressed: true } or InputEventScreenDrag or InputEventPanGesture)
            InvalidateConsoleScrollRestore();
    }

    private async void RestoreScrollAfterLayout(ConsoleSnapshot snapshot, int revision)
    {
        try
        {
            // Container sizes and scrollbar ranges settle after the panel enters the tree.
            var tree = GetTree();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this) || !IsInsideTree() || !_consoleScrollRestorePending ||
                revision != _consoleScrollRestoreRevision) return;
            _scroll.ScrollVertical = snapshot.Scroll;
            RestoreNamedScroll("ConsoleFilters", snapshot.FilterScroll);
            RestoreNamedScroll("ConsoleCategoryScroll", snapshot.CategoryScroll);
            RestoreNamedScroll("ConsoleAncientOptionScroll", snapshot.AncientScroll);
        }
        catch (Exception ex) { RuntimeLog.WarnException("inRunConsoleScrollRestoreFailed=true", ex); }
        finally
        {
            if (revision == _consoleScrollRestoreRevision) _consoleScrollRestorePending = false;
        }
    }

    private int NamedScrollPosition(string name)
        => FindChild(name, recursive: true, owned: false) is ScrollContainer scroll ? scroll.ScrollVertical : 0;

    private void RestoreNamedScroll(string name, int value)
    {
        if (IsInstanceValid(this) && FindChild(name, recursive: true, owned: false) is ScrollContainer scroll)
            scroll.ScrollVertical = value;
    }

    private sealed class ConsoleRunUiState { internal ConsoleSnapshot? Snapshot; }
    private sealed record PotionSlotSelection(int Slot, PotionModel Potion);
    private sealed record ConsoleSnapshot(
        int Tab, bool HasCategory, bool ContentOpen, string Search, string? Rarity, int? Act, string? Room,
        bool AllCharacters, ModelKey[] Characters, EffectCardRarity[] CardRarities, EffectCardType[] CardTypes,
        bool ColorlessOnly, int Page, int Scroll, int FilterScroll, int CategoryScroll, int AncientScroll,
        bool Holdings, int Pile, double Quantity, bool Upgraded, double GoldAmount,
        AbstractModel[] Selected, PotionSlotSelection[] SelectedPotionSlots, int PotionSlot, PotionModel? PotionSlotContents,
        string? AncientOptionKey);
}
