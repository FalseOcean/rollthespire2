using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Relic;
using RolltheSpire2.Ui.Pages.Search.Shop;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

/// <summary>
/// Session-local S authoring surface. Its three independently ranged Merchant rows map
/// directly to existing typed Shop sequence contracts through its Query partial.
/// </summary>
internal sealed partial class ShopSearchEditor : Control
{
    private readonly SearchEditorLayout _presentation = new();
    private const int MinimumShopRange = 1, MaximumShopRange = 5, DefaultShopRange = 3;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly ObjectPickerPanel _picker;
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("shop-prototype");
    private readonly Control _body = new();
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyDrafts = [];
    private SeatDraft _draft = new();
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private bool _english;
    private int _players = 1;
    private bool _guideExpanded;

    private enum RowKind { Relic, ColorlessUncommon, ColorlessRare }

    private sealed class RowDraft
    {
        public int ShopRange = DefaultShopRange;
        public string RangeProblem = string.Empty;
        public CombatRewardSequenceOrderMode OrderMode = CombatRewardSequenceOrderMode.Unordered;
        public ModelKey?[] Slots { get; } = new ModelKey?[MaximumShopRange];
    }

    private sealed class SeatDraft
    {
        public string Context = string.Empty;
        public string Problem = string.Empty;
        public ShopColorlessSearchUiCatalog? Cards;
        public RelicSequenceSearchUiCatalog? Relics;
        public RowDraft Relic { get; } = new();
        public RowDraft Uncommon { get; } = new();
        public RowDraft Rare { get; } = new();
    }

    public ShopSearchEditor(ModRuntimeSnapshot runtime, ObjectPickerPanel picker)
    {
        _runtime = runtime;
        _picker = picker;
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
            _draft.Cards = null;
            _draft.Relics = null;
            _draft.Problem = string.Empty;
            try
            {
                if (players > 1 && unlocks is null)
                    _draft.Problem = _text.Get("query.shop.context.read_unlocks_required");
                else
                {
                    UnlockState resolved = sharedContext is not null ? UnlockState.FromSerializable(sharedContext.Unlocks) : players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    if (sharedContext is not null)
                    {
                        _draft.Cards = ShopColorlessSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, sharedContext.Authority.EffectAuthority);
                        _draft.Relics = RelicSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, sharedContext.Authority.WorldAuthority);
                    }
                    else CaptureCatalogs(character, ascension, players, seat, resolved);
                    if (_draft.Cards is null || !_draft.Cards.CatalogAvailable ||
                        _draft.Relics is null || !_draft.Relics.CatalogAvailable)
                        _draft.Problem = _text.Get("query.shop.context.catalog_unavailable");
                }
            }
            catch (Exception exception)
            {
                _draft.Cards = null;
                _draft.Relics = null;
                _draft.Problem = _text.Get("query.shop.context.catalog_unavailable");
                Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(exception, "EditorCatalogCaptureFailed", "ShopEditorPrototype", "PickerCatalog", "issues.capture_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning);
            }
        }
        if (render) Render();
    }

    private void CaptureCatalogs(ModelKey character, int ascension, int players, int seat, UnlockState unlocks)
    {
        string seed = RolltheSpire2.Compatibility.ProfileSeedGenerator.CreateProbeSeed(_runtime.Profile);
        var captured = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
            captured.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
        _draft.Cards = ShopColorlessSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, captured.EffectAuthority);
        _draft.Relics = RelicSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, world);
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
        RenderMatrix(content, Size.X - 28);
        _presentation.Finish();
    }

    private VBoxContainer FamilyGuide(Control host, float width) => SearchEditorGuide.Build(
        host, _p, width, _text.Get("query.shop.guide.title"),
        _text.Get("query.shop.guide.summary"),
        [_text.Get("query.shop.guide.usage")], _guideExpanded,
        () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private void RenderMatrix(Control content, float width)
    {
        float rowTop = 0;
        if (!string.IsNullOrWhiteSpace(_draft.Problem))
        {
            Text(content, _draft.Problem, 16, 0, width - 32, 18, true);
            rowTop = 40;
        }
        foreach (var (kind, row) in new[]
        {
            (RowKind.Relic, _draft.Relic), (RowKind.ColorlessUncommon, _draft.Uncommon),
            (RowKind.ColorlessRare, _draft.Rare)
        })
        {
            float rowStart = rowTop;
            rowTop += RenderRow(content, kind, row, rowTop, width);
            _presentation.Section(content, rowStart, rowTop);
        }
        content.CustomMinimumSize = new(width, rowTop);
    }

    private float RenderRow(Control content, RowKind kind, RowDraft row, float y, float width)
    {
        string rowKey = kind switch
        {
            RowKind.Relic => "query.shop.row.relic",
            RowKind.ColorlessUncommon => "query.shop.row.colorless_uncommon",
            _ => "query.shop.row.colorless_rare"
        };
        const float inset = 16, gap = 12;
        Text(content, _text.Get(rowKey), inset, y + 4, 196, _english ? 18 : 20);
        Text(content, _text.Get("query.shop.range"), 212, y + 4, 110, 17, true);
        var decrease = Button(content, "−", 326, y, 38,
            () => SetShopRange(row, row.ShopRange - 1), false, 36);
        decrease.AccessibilityName = _text.Get("query.shop.range.decrease");
        decrease.Disabled = row.ShopRange <= MinimumShopRange;
        var range = Text(content, row.ShopRange.ToString(), 372, y + 4, 34, 20);
        range.HorizontalAlignment = HorizontalAlignment.Center;
        var increase = Button(content, "+", 418, y, 38,
            () => SetShopRange(row, row.ShopRange + 1), false, 36);
        increase.AccessibilityName = _text.Get("query.shop.range.increase");
        increase.Disabled = row.ShopRange >= MaximumShopRange;

        bool ordered = row.OrderMode == CombatRewardSequenceOrderMode.Ordered;
        var mode = ModeButton(content, (ordered ? "✓  " : "○  ") + _text.Get("query.shop.order.specific_shop"),
            476, y, 180, () => ToggleMode(row), ordered, 36);
        mode.AddThemeFontSizeOverride("font_size", 16);
        mode.ToggleMode = true;
        mode.ButtonPressed = ordered;
        SearchTooltip.Set(mode, _text.Get(ordered ? "query.shop.order.specific_hint" : "query.shop.order.any_hint"));

        float labelTop = y + 52;
        if (row.RangeProblem.Length > 0)
        {
            var notice = Text(content, row.RangeProblem, inset, labelTop, width - inset * 2, 16, true);
            notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            notice.ClipText = false;
            notice.Size = new(width - inset * 2, 44);
            labelTop += 56;
        }
        float targetTop = labelTop + (ordered ? 32 : 0);
        const float stride = WorkspaceResultTile.TileWidth + gap;
        if (ordered)
        {
            for (int slot = 0; slot < row.ShopRange; slot++)
            {
                float left = inset + slot * stride;
                var ordinal = Text(content, _text.Format("query.shop.column.shop", slot + 1),
                    left, labelTop, WorkspaceResultTile.TileWidth, 16, true);
                ordinal.HorizontalAlignment = HorizontalAlignment.Center;
                AddSlot(content, kind, row, slot, left, targetTop);
            }
        }
        else
        {
            int visualSlot = 0;
            foreach ((ModelKey? key, int sourceSlot) in row.Slots.Take(row.ShopRange).Select((key, index) => (key, index)))
                if (key.HasValue)
                    AddSlot(content, kind, row, sourceSlot, inset + visualSlot++ * stride, targetTop);
            int emptySlot = Array.FindIndex(row.Slots, 0, row.ShopRange, key => !key.HasValue);
            if (emptySlot >= 0)
                AddSlot(content, kind, row, emptySlot, inset + visualSlot * stride, targetTop);
        }
        return targetTop - y + WorkspaceResultTile.CompactHeight + 8;
    }

    private void AddSlot(Control parent, RowKind kind, RowDraft row, int index, float x, float y)
    {
        if (!row.Slots[index].HasValue)
        {
            var add = Button(parent, _english ? "+ Add target" : "+ 添加目标", x, y, WorkspaceResultTile.TileWidth,
                () => OpenPicker(kind, row, index), false, WorkspaceResultTile.CompactHeight);
            add.AddThemeFontSizeOverride("font_size", 15);
            add.AccessibilityName = _text.Get("picker.any_choose_result");
            add.Disabled = !HasCandidates(kind);
            add.AddThemeStyleboxOverride("normal", _p.EmptySlotBox());
            return;
        }
        GameContentKind contentKind = kind == RowKind.Relic ? GameContentKind.Relic : GameContentKind.Card;
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, row.Slots[index], contentKind,
            () => OpenPicker(kind, row, index), () => { row.Slots[index] = null; row.RangeProblem = string.Empty; Render(); },
            compact: true, compactWidth: WorkspaceResultTile.TileWidth)
        { Position = new(x, y) };
        parent.AddChild(tile);
    }

    private void OpenPicker(RowKind kind, RowDraft row, int index)
    {
        if (!HasCandidates(kind)) return;
        if (kind == RowKind.Relic)
        {
            if (_draft.Relics is null || !_draft.Relics.CatalogAvailable) return;
            var selectedElsewhere = row.Slots.Where((key, slot) => slot != index && key.HasValue)
                .Select(key => key!.Value).ToHashSet(ModelKeyComparer.Instance);
            ModelKey[] candidates = _draft.Relics.CandidatesFor(RelicSequenceKind.Shop)
                .Where(key => !selectedElsewhere.Contains(key)).ToArray();
            _picker.Open(new(candidates, key => { row.Slots[index] = key; row.RangeProblem = string.Empty; Render(); },
                _names, _text, _icons, key => _draft.Relics.Categories.GetValueOrDefault(key).ToString()));
            return;
        }

        if (_draft.Cards is null) return;
        IReadOnlyList<ModelKey> cardCandidates = kind == RowKind.ColorlessUncommon
            ? _draft.Cards.UncommonCandidates : _draft.Cards.RareCandidates;
        CardPickerContext context = _draft.Cards.CreateCardPickerContext(cardCandidates,
            kind == RowKind.ColorlessUncommon ? "shop-v13:colorless-uncommon" : "shop-v13:colorless-rare");
        _picker.Open(new(cardCandidates, key => { row.Slots[index] = key; row.RangeProblem = string.Empty; Render(); },
            _names, _text, _icons, _ => "Other", context, new HashSet<ModelKey>(ModelKeyComparer.Instance), _players));
    }

    private bool HasCandidates(RowKind kind) => kind switch
    {
        RowKind.Relic => _draft.Relics?.CandidatesFor(RelicSequenceKind.Shop).Count > 0,
        RowKind.ColorlessUncommon => _draft.Cards?.UncommonCandidates.Count > 0,
        RowKind.ColorlessRare => _draft.Cards?.RareCandidates.Count > 0,
        _ => false
    };

    private void ToggleMode(RowDraft row)
    {
        row.OrderMode = row.OrderMode == CombatRewardSequenceOrderMode.Ordered
            ? CombatRewardSequenceOrderMode.Unordered
            : CombatRewardSequenceOrderMode.Ordered;
        Render();
    }

    private void SetShopRange(RowDraft row, int value)
    {
        int next = Math.Clamp(value, MinimumShopRange, MaximumShopRange);
        if (next == row.ShopRange) return;
        int required = Array.FindLastIndex(row.Slots, key => key.HasValue) + 1;
        if (next < required)
        {
            row.RangeProblem = _text.Format("query.shop.range.targets_preserved", required);
            Render();
            return;
        }
        row.ShopRange = next;
        row.RangeProblem = string.Empty;
        Render();
    }

    // Project each independently ranged block into the existing canonical S contract.
    internal IReadOnlyList<MerchantColorlessSequenceSearchCondition> BuildCardConditions() =>
        new[]
        {
            new MerchantColorlessSequenceSearchCondition(_draft.Uncommon.ShopRange, _draft.Uncommon.OrderMode,
                MerchantColorlessSlot.Uncommon, _draft.Uncommon.Slots.Take(_draft.Uncommon.ShopRange).ToArray()),
            new MerchantColorlessSequenceSearchCondition(_draft.Rare.ShopRange, _draft.Rare.OrderMode,
                MerchantColorlessSlot.Rare, _draft.Rare.Slots.Take(_draft.Rare.ShopRange).ToArray())
        }.Where(condition => !condition.IsEmpty).ToArray();

    internal IReadOnlyList<RelicShopSequenceSearchCondition> BuildRelicConditions() =>
        _draft.Relic.Slots.Any(key => key.HasValue)
            ? [new RelicShopSequenceSearchCondition(_draft.Relic.ShopRange, _draft.Relic.OrderMode,
                _draft.Relic.Slots.Take(_draft.Relic.ShopRange).ToArray())]
            : Array.Empty<RelicShopSequenceSearchCondition>();

    private Button ModeButton(Control parent, string title, float x, float y, float width, Action action, bool active, float height = 40)
    {
        var button = Button(parent, title, x, y, width, action, false, height);
        _p.SetModeActive(button, active);
        return button;
    }

    private Button Button(Control parent, string title, float x, float y, float width, Action action,
        bool selected, float height = 40)
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
