using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

/// <summary>
/// Card and potion predicates over the existing six-battle reward window.
/// </summary>
internal sealed partial class CombatRewardSearchEditor : Control
{
    private readonly SearchEditorLayout _presentation = new();
    private const int MinimumBattleRange = 1, MaximumBattleRange = 6, DefaultBattleRange = 3;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly ObjectPickerPanel _picker;
    private readonly Func<int, int, SearchQuery> _authoredOpening;
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("combat-reward-prototype");
    private readonly Control _body = new();
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyDrafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private CombatRewardSearchUiCatalog? _catalog { get => _draft.Catalog; set => _draft.Catalog = value; }
    private string _problem { get => _draft.Problem; set => _draft.Problem = value; }
    private Label? _notice;
    private int _players = 1;
    private bool _guideExpanded;
    private bool _english;

    private sealed class SeatDraft
    {
        public string Context = string.Empty, Problem = string.Empty;
        public CombatRewardSearchUiCatalog? Catalog;
        public int Removed;
        public double NoticeSeconds;
        public bool Unordered = true;
        public int BattleRange = DefaultBattleRange;
        public string CardRangeProblem = string.Empty, PotionRangeProblem = string.Empty;
        public int Ascension;
        public bool CardSupportKnown, Wheel, Candy;
        public string SupportContext = string.Empty, CandidateContext = string.Empty;
        public Dictionary<string, bool> SupportResults { get; } = new(StringComparer.Ordinal);
        public Dictionary<int, IReadOnlyList<ModelKey>> CandidateResults { get; } = [];
        public ModelKey?[] Slots { get; } = new ModelKey?[MaximumBattleRange];
        public int PotionRange = DefaultBattleRange;
        public int ExclusionBattleCount = DefaultBattleRange;
        public RolltheSpire2.Core.Collections.CardCollectionSnapshot? ExcludedCollection;
        public CombatCardExclusionSearchCondition? Exclusion;
        public bool PotionsUnordered = true;
        public CombatPotionRewardSlotSearchCondition[] Potions { get; } = Enumerable.Range(0, MaximumBattleRange)
            .Select(_ => new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null)).ToArray();
    }

    public CombatRewardSearchEditor(ModRuntimeSnapshot runtime, ObjectPickerPanel picker, Func<int, int, SearchQuery> authoredOpening)
    {
        _runtime = runtime;
        _picker = picker;
        _authoredOpening = authoredOpening;
        AddChild(_body);
    }

    public event Action? GuideRequested;

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true, EditorRefreshContext? sharedContext = null)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _english = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        _players = players;
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;

        string unlockKey = sharedContext?.Key ?? (players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks));
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}";
        if (_draft.Context != context)
        {
            _draft.Context = context;
            _catalog = null;
            _problem = string.Empty;
            try
            {
                if (players > 1 && unlocks is null)
                    _problem = _text.Get("query.combat.context.read_unlocks_required");
                else
                {
                    UnlockState resolved = sharedContext is not null ? UnlockState.FromSerializable(sharedContext.Unlocks) : players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    _catalog = CombatRewardSearchUiCatalog.CaptureForPicker(
                        _runtime, character, ascension, players, seat, resolved, sharedContext?.Authority);
                    if (!_catalog.CardCatalogAvailable) _problem = _text.Get("query.combat.context.catalog_unavailable");

                }
            }
            catch (Exception exception)
            {
                _catalog = null;
                _problem = _text.Get("query.combat.context.catalog_unavailable");
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "EditorCatalogCaptureFailed", "CombatRewardEditorPrototype", "PickerCatalog", "issues.capture_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning);
            }
        }
        RefreshCardSupport(ascension, players, seat);
        if (render) Render();
    }

    public override void _Process(double delta)
    {
        if (!Visible || _draft.NoticeSeconds <= 0) return;
        _draft.NoticeSeconds -= delta;
        if (_draft.NoticeSeconds <= 0) { _draft.Removed = 0; if (GodotObject.IsInstanceValid(_notice)) _notice!.Hide(); }
    }

    private void Render()
    {
        _presentation.Begin();
        Clear(_body);
        _body.Size = Size;
        var guide = FamilyGuide(_body, Size.X - 20);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        void Layout()
        {
            float top = guide.Size.Y + 10;
            scroll.Position = new(0, top);
            scroll.Size = new(Size.X, Math.Max(0, Size.Y - top));
        }
        guide.Resized += Layout;
        Layout();
        _body.AddChild(scroll);
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 1) };
        scroll.AddChild(content);
        RenderAuthoring(content, Size.X - 28);
        _presentation.Finish();
        if (_draft.Removed > 0)
        {
            _notice = Text(_body, _text.Format("query.combat.notice.removed_invalid", _draft.Removed), 0, Size.Y - 28, Size.X - 16, 17, true);
            _notice.ZIndex = 10;
        }
    }

    private VBoxContainer FamilyGuide(Control host, float width) => SearchEditorGuide.Build(
        host, _p, width, _text.Get("query.combat.guide.title"),
        _text.Get("query.combat.guide.summary"),
        [_text.Get("query.combat.guide.usage"), _text.Get("query.combat.guide.legality"),
            _players == 1 ? _text.Get("query.combat.guide.opening") : string.Empty],
        _guideExpanded, () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private void RenderAuthoring(Control content, float width)
    {
        Text(content, _text.Get("query.combat.cards.title"), 16, 0, width - 32, 20);
        RenderRewardControls(content, width, 36, _draft.BattleRange, _draft.Unordered,
            SetBattleRange, SetMode, "CombatCardSpecificMode");
        RenderCards(content, width);
        float potionTop = content.CustomMinimumSize.Y;
        _presentation.Section(content, 0, potionTop);
        RenderPotions(content, width, potionTop);
        _presentation.Section(content, potionTop, content.CustomMinimumSize.Y);
        float exclusionTop = content.CustomMinimumSize.Y;
        RenderCardExclusion(content, width, exclusionTop);
        _presentation.Section(content, exclusionTop, content.CustomMinimumSize.Y);
    }

    private void RenderRewardControls(Control content, float width, float top, int battleRange, bool unordered,
        Action<int> setRange, Action<bool> setMode, string modeName)
    {
        Text(content, _text.Get("query.combat.range"), 16, top + 4, 130, 17, true);
        var decrease = Button(content, "−", 150, top, 38, () => setRange(battleRange - 1), false, 36);
        decrease.AccessibilityName = _text.Get("query.combat.range.decrease");
        decrease.Disabled = battleRange <= MinimumBattleRange;
        var range = Text(content, battleRange.ToString(), 200, top + 4, 34, 20);
        range.HorizontalAlignment = HorizontalAlignment.Center;
        var increase = Button(content, "+", 246, top, 38, () => setRange(battleRange + 1), false, 36);
        increase.AccessibilityName = _text.Get("query.combat.range.increase");
        increase.Disabled = battleRange >= MaximumBattleRange;

        bool ordered = !unordered;
        var specific = ModeButton(content, (ordered ? "✓  " : "○  ") + _text.Get("query.combat.mode.specific"),
            316, top, 180, () => setMode(!unordered), ordered, 36);
        specific.Name = modeName;
        specific.AddThemeFontSizeOverride("font_size", 16);
        specific.ToggleMode = true;
        specific.ButtonPressed = ordered;
        SearchTooltip.Set(specific, _text.Get(ordered
            ? "query.combat.mode.specific_tooltip" : "query.combat.mode.unordered_tooltip"));
        Text(content, _text.Get(ordered ? "query.combat.mode.specific_caption" : "query.combat.mode.within_range"),
            512, top + 4, width - 528, 16, true);
    }

    private void RenderCards(Control content, float width)
    {
        string feasibility = string.IsNullOrWhiteSpace(_problem) ? CardFeasibilityNotice : _problem;
        float labelTop = 88, tileTop = 120;
        if (_draft.CardRangeProblem.Length > 0)
        {
            var rangeNotice = Text(content, _draft.CardRangeProblem, 16, labelTop, width - 32, 16, true);
            rangeNotice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rangeNotice.ClipText = false;
            rangeNotice.Size = new(width - 32, 44);
            labelTop += 56; tileTop += 56;
        }
        if (feasibility.Length > 0)
        {
            var note = Text(content, feasibility, 16, labelTop, width - 32, 16, true);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.ClipText = false;
            note.Size = new(width - 32, 44);
            labelTop += 56; tileTop += 56;
        }
        const float stride = WorkspaceResultTile.TileWidth + 12;
        for (int i = 0; i < _draft.BattleRange; i++)
        {
            float left = 16 + i * stride;
            var label = Text(content, _text.Format(_draft.Unordered ? "query.combat.potions.target" : "query.combat.battle", i + 1),
                left, labelTop, WorkspaceResultTile.TileWidth, 17, true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            SearchTooltip.Set(label, _text.Get("query.combat.cards.slot_tooltip"));
            label.MouseFilter = MouseFilterEnum.Pass;
            AddSlot(content, i, left, tileTop);
        }
        content.CustomMinimumSize = new(width, tileTop + WorkspaceResultTile.CompactHeight +
            (HasImpossibleCardSelection ? 44 : 16));
    }

    private void AddSlot(Control parent, int index, float x, float y)
    {
        if (!_draft.Slots[index].HasValue)
        {
            var add = Button(parent, _text.Get("query.combat.cards.add"), x, y, WorkspaceResultTile.TileWidth,
                () => OpenPicker(index), false, 64);
            add.AddThemeFontSizeOverride("font_size", 16);
            add.Disabled = _catalog?.CardCatalogAvailable != true;
            add.AccessibilityName = _text.Get("picker.any_choose_result");
            SearchTooltip.Set(add, _text.Get("query.combat.cards.slot_tooltip"));
            add.AddThemeStyleboxOverride("normal", _p.EmptySlotBox());
            return;
        }
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, _draft.Slots[index],
            GameContentKind.Card, () => OpenPicker(index), () => { _draft.Slots[index] = null; _draft.CardRangeProblem = string.Empty; Render(); }, compact: true, compactWidth: WorkspaceResultTile.TileWidth)
        { Position = new(x, y) };
        parent.AddChild(tile);
        if (HasImpossibleCardSelection)
        {
            string issue = _catalog?.CardSupportProofAvailable == true
                ? (_english ? "Target conflict" : "目标组合冲突")
                : (_english ? "Model: impossible" : "模型预计不可行");
            Text(parent, issue, x, y + WorkspaceResultTile.CompactHeight + 2, WorkspaceResultTile.TileWidth, 14, true);
        }
    }

    private void OpenPicker(int index)
    {
        if (_catalog is null || !_catalog.CardCatalogAvailable) return;
        var candidates = ResidualCandidates(index);
        CardPickerContext context = _catalog.CreateCardPickerContext($"combat-reward-v13:battle:{index + 1}") with { AllowedCardModelKeys = candidates };
        var draft = _draft;
        _picker.Open(new(candidates, key => { draft.Slots[index] = key; draft.CardRangeProblem = string.Empty; Render(); },
            _names, _text, _icons, _ => "Other", context, _catalog.MultiplayerOnlyCards, _players));
    }

    private void SetMode(bool unordered)
    {
        if (_draft.Unordered == unordered) return;
        _draft.Unordered = unordered;
        Render();
    }

    private void SetBattleRange(int value)
    {
        int next = Math.Clamp(value, MinimumBattleRange, MaximumBattleRange);
        if (next == _draft.BattleRange) return;
        int required = Array.FindLastIndex(_draft.Slots, key => key.HasValue) + 1;
        if (next < required)
        {
            _draft.CardRangeProblem = _text.Format("query.combat.cards.range.targets_preserved", required);
            Render();
            return;
        }
        _draft.BattleRange = next;
        _draft.CardRangeProblem = string.Empty;
        Render();
    }

    private Button ModeButton(Control parent, string title, float x, float y, float width, Action action, bool active, float height = 40)
    {
        var button = Button(parent, title, x, y, width, action, false, height);
        _p.SetModeActive(button, active);
        return button;
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action, bool selected, float height = 40)
    {
        var button = _p.CompactButton(title, height, height < 32 ? 16 : 18, selected);
        button.Position = new(x, y);
        button.CustomMinimumSize = new(0, height);
        button.Size = new(width, height);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private Label Text(Control parent, string value, float x, float y, float width, int size, bool secondary = false)
    {
        var label = _p.Label(value, size, secondary);
        label.Position = new(x, y);
        label.Size = new(width, 28);
        label.ClipText = true;
        parent.AddChild(label);
        return label;
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
