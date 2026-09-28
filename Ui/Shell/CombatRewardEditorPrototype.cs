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

namespace RolltheSpire2.Ui.Shell;

/// <summary>
/// Card and potion predicates over the existing six-battle reward window.
/// </summary>
internal sealed partial class CombatRewardEditorPrototype : Control
{
    private const int MinimumBattleRange = 1, MaximumBattleRange = 6, DefaultBattleRange = 3;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly NeowEditorPrototype _pickerHost;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
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
        public bool Unordered;
        public int BattleRange = DefaultBattleRange;
        public int Ascension;
        public bool CardSupportKnown, Wheel, Candy;
        public string SupportContext = string.Empty, CandidateContext = string.Empty;
        public Dictionary<string, bool> SupportResults { get; } = new(StringComparer.Ordinal);
        public Dictionary<int, IReadOnlyList<ModelKey>> CandidateResults { get; } = [];
        public ModelKey?[] Slots { get; } = new ModelKey?[MaximumBattleRange];
        public int PotionRange = DefaultBattleRange;
        public bool PotionsUnordered;
        public CombatPotionRewardSlotSearchCondition[] Potions { get; } = Enumerable.Range(0, MaximumBattleRange)
            .Select(_ => new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null)).ToArray();
    }

    public CombatRewardEditorPrototype(ModRuntimeSnapshot runtime, NeowEditorPrototype pickerHost)
    {
        _runtime = runtime;
        _pickerHost = pickerHost;
        AddChild(_body);
    }

    public event Action? GuideRequested;

    public void Refresh(string language, IUiTextProvider text, ModelKey character, int ascension,
        int players, int seat, SerializableUnlockState? unlocks = null, bool render = true)
    {
        _text = text;
        _names = RuntimeGameContentNameResolver.Create(language);
        _english = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        _players = players;
        var drafts = players > 1 ? _partyDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(index => index >= players).ToArray()) drafts.Remove(removedSeat);
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;

        string unlockKey = players == 1
            ? System.Text.Json.JsonSerializer.Serialize(SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks);
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
                    UnlockState resolved = players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    _catalog = CombatRewardSearchUiCatalog.CaptureForPicker(
                        _runtime, character, ascension, players, seat, resolved);
                    if (!_catalog.CardCatalogAvailable) _problem = _text.Get("query.combat.context.catalog_unavailable");

                }
            }
            catch (Exception exception)
            {
                _catalog = null;
                _problem = _text.Get("query.combat.context.catalog_unavailable");
                RuntimeLog.Warn("combatRewardPrototypeCatalog=" + exception.Message);
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
        if (_draft.Removed > 0)
        {
            _notice = Text(_body, _text.Format("query.combat.notice.removed_invalid", _draft.Removed), 0, Size.Y - 28, Size.X - 16, 17, true);
            _notice.ZIndex = 10;
        }
    }

    private VBoxContainer FamilyGuide(Control host, float width) => SearchEditorGuide.Build(
        host, _p, width, _text.Get("query.combat.guide.title"),
        _english ? "Set card and potion targets independently for the first 1–6 combats." : "分别设置前 1–6 场战斗的卡牌与药水目标。",
        [_text.Get("query.combat.guide.usage"), _text.Get("query.combat.guide.legality")],
        _guideExpanded, () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private void RenderAuthoring(Control content, float width)
    {
        Text(content, _english ? "Card rewards" : "卡牌奖励", 0, 0, width, 20);
        RenderRewardControls(content, width, 36, _draft.BattleRange, _draft.Unordered, SetBattleRange, SetMode);
        if (!string.IsNullOrWhiteSpace(_problem))
        {
            Text(content, _problem, 0, 84, width, 18, true);
            content.CustomMinimumSize = new(width, 128);
            return;
        }

        RenderCards(content, width);
        float potionTop = content.CustomMinimumSize.Y + 18;
        content.AddChild(new ColorRect
        {
            Position = new(0, potionTop - 12), Size = new(width, 1), Color = _p.Color(_p.Line),
            MouseFilter = MouseFilterEnum.Ignore
        });
        RenderPotions(content, width, potionTop);
    }

    private void RenderRewardControls(Control content, float width, float top, int battleRange, bool unordered,
        Action<int> setRange, Action<bool> setMode)
    {
        Text(content, _text.Get("query.combat.range"), 0, top + 5, 122, 17, true);
        var decrease = Button(content, "−", 126, top, 38, () => setRange(battleRange - 1), false, 36);
        decrease.AccessibilityName = _text.Get("query.combat.range.decrease");
        decrease.Disabled = battleRange <= MinimumBattleRange;
        var range = Text(content, battleRange.ToString(), 168, top + 5, 34, 20);
        range.HorizontalAlignment = HorizontalAlignment.Center;
        var increase = Button(content, "+", 206, top, 38, () => setRange(battleRange + 1), false, 36);
        increase.AccessibilityName = _text.Get("query.combat.range.increase");
        increase.Disabled = battleRange >= MaximumBattleRange;

        const float modeLeft = 276, gap = 8;
        float modeWidth = (width - modeLeft - gap) / 2;
        var specific = Button(content, _english ? "Specific combats" : "指定战斗", modeLeft, top, modeWidth,
            () => setMode(false), !unordered, 36);
        var any = Button(content, _english ? "Any combat in range" : "范围内不限顺序", modeLeft + modeWidth + gap, top,
            modeWidth, () => setMode(true), unordered, 36);
        specific.AddThemeFontSizeOverride("font_size", 16);
        any.AddThemeFontSizeOverride("font_size", 16);
    }

    private void RenderCards(Control content, float width)
    {
        string feasibility = CardFeasibilityNotice;
        float labelTop = 84, tileTop = 116;
        if (feasibility.Length > 0)
        {
            var note = Text(content, feasibility, 0, 82, width, 16, true);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.ClipText = false;
            note.Size = new(width, 44);
            labelTop += 48; tileTop += 48;
        }
        float cellWidth = width / Math.Max(3, _draft.BattleRange);
        bool hasCards = _draft.Slots.Take(_draft.BattleRange).Any(key => key.HasValue);
        for (int i = 0; i < _draft.BattleRange; i++)
        {
            float left = i * cellWidth + (cellWidth - WorkspaceResultTile.TileWidth) / 2;
            var label = Text(content, _text.Format(_draft.Unordered ? "query.combat.potions.target" : "query.combat.battle", i + 1),
                left, labelTop, WorkspaceResultTile.TileWidth, 17, true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            AddSlot(content, i, left, tileTop);
        }
        content.CustomMinimumSize = new(width, tileTop + (hasCards ? WorkspaceResultTile.TileHeight : 64) +
            (HasImpossibleCardSelection ? 28 : 8));
    }

    private void AddSlot(Control parent, int index, float x, float y)
    {
        if (!_draft.Slots[index].HasValue)
        {
            var add = Button(parent, _english ? "+ Add card" : "+ 选择卡牌", x, y, WorkspaceResultTile.TileWidth,
                () => OpenPicker(index), false, 64);
            add.AddThemeFontSizeOverride("font_size", 16);
            add.AccessibilityName = _text.Get("picker.any_choose_result");
            add.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface, _p.Line, 1));
            return;
        }
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, _draft.Slots[index],
            GameContentKind.Card, () => OpenPicker(index), () => { _draft.Slots[index] = null; Render(); })
        { Position = new(x, y) };
        parent.AddChild(tile);
        if (HasImpossibleCardSelection)
        {
            string issue = _catalog?.CardSupportProofAvailable == true
                ? (_english ? "Target conflict" : "目标组合冲突")
                : (_english ? "Model: impossible" : "模型预计不可行");
            Text(parent, issue, x, y + WorkspaceResultTile.TileHeight + 2, WorkspaceResultTile.TileWidth, 14, true);
        }
    }

    private void OpenPicker(int index)
    {
        if (_catalog is null || !_catalog.CardCatalogAvailable) return;
        var candidates = ResidualCandidates(index);
        CardPickerContext context = _catalog.CreateCardPickerContext($"combat-reward-v13:battle:{index + 1}") with { AllowedCardModelKeys = candidates };
        var draft = _draft;
        _pickerHost.PickExternalCards(candidates, context, _catalog.MultiplayerOnlyCards, _players,
            key => { draft.Slots[index] = key; Render(); });
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
        int removed = 0;
        if (next < _draft.BattleRange)
            for (int i = next; i < _draft.Slots.Length; i++)
                if (_draft.Slots[i].HasValue) { _draft.Slots[i] = null; removed++; }
        _draft.BattleRange = next;
        if (removed > 0) { _draft.Removed += removed; _draft.NoticeSeconds = 5; }
        Render();
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
