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

namespace RolltheSpire2.Ui.Shell;

/// <summary>
/// Session-local 1.3 S authoring surface. Its three shared-horizon Merchant rows map
/// directly to existing typed Shop sequence contracts through WorkbenchEditorContracts.
/// </summary>
internal sealed partial class ShopEditorPrototype : Control
{
    private const int MinimumShopRange = 1, MaximumShopRange = 5, DefaultShopRange = 3;
    private readonly ModRuntimeSnapshot _runtime;
    private readonly NeowEditorPrototype _pickerHost;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
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
        public CombatRewardSequenceOrderMode OrderMode = CombatRewardSequenceOrderMode.Unordered;
        public ModelKey?[] Slots { get; } = new ModelKey?[MaximumShopRange];
    }

    private sealed class SeatDraft
    {
        public string Context = string.Empty;
        public string Problem = string.Empty;
        public ShopColorlessSearchUiCatalog? Cards;
        public RelicSequenceSearchUiCatalog? Relics;
        public int ShopRange = DefaultShopRange;
        public RowDraft Relic { get; } = new();
        public RowDraft Uncommon { get; } = new();
        public RowDraft Rare { get; } = new();
    }

    public ShopEditorPrototype(ModRuntimeSnapshot runtime, NeowEditorPrototype pickerHost)
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
            _draft.Cards = null;
            _draft.Relics = null;
            _draft.Problem = string.Empty;
            try
            {
                if (players > 1 && unlocks is null)
                    _draft.Problem = _text.Get("query.shop.context.read_unlocks_required");
                else
                {
                    UnlockState resolved = players == 1
                        ? SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : UnlockState.FromSerializable(unlocks!);
                    CaptureCatalogs(character, ascension, players, seat, resolved);
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
                RuntimeLog.Warn("shopPrototypeCatalog=" + exception.Message);
            }
            Revalidate();
        }
        if (render) Render();
    }

    private void CaptureCatalogs(ModelKey character, int ascension, int players, int seat, UnlockState unlocks)
    {
        string seed = new(_runtime.Profile.SeedAlphabet[0], _runtime.Profile.SeedLength);
        var captured = ReflectionNeowEffectSnapshotAdapter.Capture(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, _runtime.Profile.ProfileId,
            _runtime.Detection.DisplayVersion, new ReflectionSnapshotProfileRules(true, true), unlocks);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(_runtime.Profile, seed,
            CharacterIdentity.FromKey(character), ascension, players, seat, noRunModifiers: true,
            captured.EffectAuthority, gameVersion: _runtime.Detection.DisplayVersion);
        _draft.Cards = ShopColorlessSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, captured.EffectAuthority);
        _draft.Relics = RelicSequenceSearchUiCatalog.FromAuthority(_runtime.Profile.ProfileId, world);
    }

    private void Revalidate()
    {
        foreach (RowDraft row in Rows())
            for (int i = _draft.ShopRange; i < row.Slots.Length; i++) row.Slots[i] = null;
        if (_draft.Cards is not null)
        {
            RemoveUnavailable(_draft.Uncommon, _draft.Cards.UncommonCandidates);
            RemoveUnavailable(_draft.Rare, _draft.Cards.RareCandidates);
        }
        if (_draft.Relics is not null)
            RemoveUnavailable(_draft.Relic, _draft.Relics.CandidatesFor(RelicSequenceKind.Shop));
    }

    private static void RemoveUnavailable(RowDraft row, IReadOnlyList<ModelKey> candidates)
    {
        for (int i = 0; i < row.Slots.Length; i++)
            if (row.Slots[i] is { } key && !candidates.Contains(key, ModelKeyComparer.Instance)) row.Slots[i] = null;
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
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 520) };
        scroll.AddChild(content);
        RenderMatrix(content, Size.X - 28);
    }

    private VBoxContainer FamilyGuide(Control host, float width) => SearchEditorGuide.Build(
        host, _p, width, _text.Get("query.shop.guide.title"),
        _english ? "Choose a shop range, then add relic or colorless-card targets." : "设置商店范围，再添加遗物或无色牌目标。",
        [_text.Get("query.shop.guide.usage")], _guideExpanded,
        () => { _guideExpanded = !_guideExpanded; Render(); }, () => GuideRequested?.Invoke());

    private void RenderMatrix(Control content, float width)
    {
        const float headerWidth = 210, matrixLeft = 226;
        float matrixWidth = width - matrixLeft - 12;
        float noticeOffset = string.IsNullOrWhiteSpace(_draft.Problem) ? 0 : 34;
        bool showShopOrdinals = new[] { _draft.Relic, _draft.Uncommon, _draft.Rare }
            .Any(row => row.OrderMode == CombatRewardSequenceOrderMode.Ordered);
        float controlsTop = noticeOffset;
        float headerTop = controlsTop + 52;
        float firstRowTop = controlsTop + (showShopOrdinals ? 86 : 58);

        if (!string.IsNullOrWhiteSpace(_draft.Problem))
            Text(content, _draft.Problem, 0, 0, width, 18, true);

        Text(content, _text.Get("query.shop.range"), 0, controlsTop + 7, 104, _english ? 17 : 18, true);
        var decrease = Button(content, "−", 112, controlsTop + 2, 40,
            () => SetShopRange(_draft.ShopRange - 1), false, 36);
        decrease.AccessibilityName = _text.Get("query.shop.range.decrease");
        decrease.Disabled = _draft.ShopRange <= MinimumShopRange;
        var range = Text(content, _draft.ShopRange.ToString(), 160, controlsTop + 7, 36, 20);
        range.HorizontalAlignment = HorizontalAlignment.Center;
        var increase = Button(content, "+", 204, controlsTop + 2, 40,
            () => SetShopRange(_draft.ShopRange + 1), false, 36);
        increase.AccessibilityName = _text.Get("query.shop.range.increase");
        increase.Disabled = _draft.ShopRange >= MaximumShopRange;

        if (showShopOrdinals)
            for (int shop = 0; shop < _draft.ShopRange; shop++)
            {
                float cellWidth = matrixWidth / _draft.ShopRange;
                float tileLeft = matrixLeft + shop * cellWidth + (cellWidth - WorkspaceResultTile.TileWidth) / 2;
                var header = Text(content, _text.Format("query.shop.column.shop", shop + 1),
                    tileLeft, headerTop, WorkspaceResultTile.TileWidth, _english ? 16 : 17, true);
                header.HorizontalAlignment = HorizontalAlignment.Center;
            }

        float rowTop = firstRowTop;
        foreach (var (kind, row) in new[]
        {
            (RowKind.Relic, _draft.Relic), (RowKind.ColorlessUncommon, _draft.Uncommon),
            (RowKind.ColorlessRare, _draft.Rare)
        })
            rowTop += RenderRow(content, kind, row, rowTop, headerWidth, matrixLeft, matrixWidth, width);
        content.CustomMinimumSize = new(width, rowTop + 8);
    }

    private float RenderRow(Control content, RowKind kind, RowDraft row, float y, float headerWidth,
        float matrixLeft, float matrixWidth, float width)
    {
        string rowKey = kind switch
        {
            RowKind.Relic => "query.shop.row.relic",
            RowKind.ColorlessUncommon => "query.shop.row.colorless_uncommon",
            _ => "query.shop.row.colorless_rare"
        };
        bool hasTargets = row.Slots.Take(_draft.ShopRange).Any(key => key.HasValue);
        float rowHeight = hasTargets ? WorkspaceResultTile.TileHeight + 20 : 112;
        Text(content, _text.Get(rowKey), 0, y + 4, headerWidth, _english ? 18 : 20);
        bool ordered = row.OrderMode == CombatRewardSequenceOrderMode.Ordered;
        var mode = Button(content, (ordered ? "✓  " : "○  ") + _text.Get("query.shop.order.specific_shop"),
            0, y + 38, headerWidth - 8, () => ToggleMode(row), ordered, 34);
        mode.AddThemeFontSizeOverride("font_size", _english ? 15 : 16);
        mode.ToggleMode = true;
        mode.ButtonPressed = ordered;
        Text(content, ordered
                ? (_english ? "Match each shop number" : "按商店序号匹配")
                : (_english ? "Any shop within the range" : "范围内不限顺序"),
            0, y + 76, headerWidth, 14, true);
        float emptyTop = y + ((hasTargets ? WorkspaceResultTile.TileHeight : rowHeight - 20) - 56) / 2;

        if (ordered)
        {
            float cellWidth = matrixWidth / _draft.ShopRange;
            for (int slot = 0; slot < _draft.ShopRange; slot++)
                AddSlot(content, kind, row, slot,
                    matrixLeft + slot * cellWidth + (cellWidth - WorkspaceResultTile.TileWidth) / 2,
                    row.Slots[slot].HasValue ? y : emptyTop);
        }
        else
        {
            float compactStart = matrixLeft + 14;
            const float compactStride = WorkspaceResultTile.TileWidth + 10;
            int visualSlot = 0;
            foreach ((ModelKey? key, int sourceSlot) in row.Slots.Take(_draft.ShopRange).Select((key, index) => (key, index)))
                if (key.HasValue)
                    AddSlot(content, kind, row, sourceSlot, compactStart + visualSlot++ * compactStride, y);
            int emptySlot = Array.FindIndex(row.Slots, 0, _draft.ShopRange, key => !key.HasValue);
            if (emptySlot >= 0)
                AddSlot(content, kind, row, emptySlot, compactStart + visualSlot * compactStride, emptyTop);
        }

        Color divider = _p.Color(_p.Line);
        divider.A *= .45f;
        content.AddChild(new ColorRect
        {
            Position = new(0, y + rowHeight - 8),
            Size = new(width - 12, 1),
            Color = divider,
            MouseFilter = MouseFilterEnum.Ignore
        });
        return rowHeight;
    }

    private void AddSlot(Control parent, RowKind kind, RowDraft row, int index, float x, float y)
    {
        if (!row.Slots[index].HasValue)
        {
            var add = Button(parent, _english ? "+ Add target" : "+ 添加目标", x, y, WorkspaceResultTile.TileWidth,
                () => OpenPicker(kind, row, index), false, 56);
            add.AddThemeFontSizeOverride("font_size", 15);
            add.AccessibilityName = _text.Get("picker.any_choose_result");
            add.Disabled = !HasCandidates(kind);
            add.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface, _p.Line, 1));
            return;
        }
        GameContentKind contentKind = kind == RowKind.Relic ? GameContentKind.Relic : GameContentKind.Card;
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, row.Slots[index], contentKind,
            () => OpenPicker(kind, row, index), () => { row.Slots[index] = null; Render(); })
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
            _pickerHost.PickExternalObjects(candidates, key => { row.Slots[index] = key; Render(); });
            return;
        }

        if (_draft.Cards is null) return;
        IReadOnlyList<ModelKey> cardCandidates = kind == RowKind.ColorlessUncommon
            ? _draft.Cards.UncommonCandidates : _draft.Cards.RareCandidates;
        CardPickerContext context = _draft.Cards.CreateCardPickerContext(cardCandidates,
            kind == RowKind.ColorlessUncommon ? "shop-v13:colorless-uncommon" : "shop-v13:colorless-rare");
        _pickerHost.PickExternalCards(cardCandidates, context, new HashSet<ModelKey>(ModelKeyComparer.Instance),
            _players, key => { row.Slots[index] = key; Render(); });
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

    private void SetShopRange(int value)
    {
        int next = Math.Clamp(value, MinimumShopRange, MaximumShopRange);
        if (next == _draft.ShopRange) return;
        if (next < _draft.ShopRange)
            foreach (RowDraft row in Rows())
            {
                if (row.OrderMode == CombatRewardSequenceOrderMode.Ordered)
                {
                    for (int i = next; i < row.Slots.Length; i++) row.Slots[i] = null;
                }
                else
                {
                    ModelKey[] targets = row.Slots.Take(_draft.ShopRange).Where(key => key.HasValue)
                        .Select(key => key!.Value).Take(next).ToArray();
                    Array.Clear(row.Slots);
                    for (int i = 0; i < targets.Length; i++) row.Slots[i] = targets[i];
                }
            }
        _draft.ShopRange = next;
        Revalidate();
        Render();
    }

    private IEnumerable<RowDraft> Rows()
    {
        yield return _draft.Relic;
        yield return _draft.Uncommon;
        yield return _draft.Rare;
    }

    // These projections keep the prototype state shaped exactly like the existing
    // canonical S contracts without connecting the Workbench Search action.
    internal IReadOnlyList<MerchantColorlessSequenceSearchCondition> BuildCardConditions() =>
        new[]
        {
            new MerchantColorlessSequenceSearchCondition(_draft.ShopRange, _draft.Uncommon.OrderMode,
                MerchantColorlessSlot.Uncommon, _draft.Uncommon.Slots),
            new MerchantColorlessSequenceSearchCondition(_draft.ShopRange, _draft.Rare.OrderMode,
                MerchantColorlessSlot.Rare, _draft.Rare.Slots)
        }.Where(condition => !condition.IsEmpty).ToArray();

    internal IReadOnlyList<RelicShopSequenceSearchCondition> BuildRelicConditions() =>
        _draft.Relic.Slots.Any(key => key.HasValue)
            ? [new RelicShopSequenceSearchCondition(_draft.ShopRange, _draft.Relic.OrderMode, _draft.Relic.Slots)]
            : Array.Empty<RelicShopSequenceSearchCondition>();

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
