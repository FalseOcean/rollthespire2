using Godot;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class TransformationAggregatePage : MarginContainer
{
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _useNeow = new();
    private readonly Label _neowStatus = Ui1Theme.Label("", Ui1TextRole.Meta);
    private TransformationOpening _currentOpening;
    private TransformationPickupOrder _currentOrder;
    private readonly OptionButton _opening = new(), _order = new(), _predicate = new();
    private readonly CheckBox _morphic = new(), _aroma = new(), _whisper = new(), _symbiote = new(), _trial = new();
    private readonly SpinBox _rare = new() { MinValue = 1, MaxValue = 10, Value = 1, Step = 1 };
    private readonly Label _help = Ui1Theme.Label("", Ui1TextRole.Body);
    private readonly HFlowContainer _targetRow = new();
    private readonly SearchCardResultSlot[] _slots;
    private readonly RelicPickerPanel _picker;
    private IUiTextProvider? _text;
    private IGameContentNameResolver? _names;
    private ModelKey[] _pool = [];
    private ModelKey[] _eventPool = [], _leafyPool = [], _newLeafPool = [];
    private IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> _neowMetadata = new Dictionary<ModelKey, CardPickerCandidateMetadata>();
    private readonly Dictionary<ModelKey, CardPickerCandidateMetadata> _eventMetadata = new();
    private bool _restoring, _running;
    internal event Action? Changed;
    internal event Action<TransformationOpening>? OpeningRequested;
    internal bool UsesNeow => _enabled.ButtonPressed && _useNeow.ButtonPressed && _currentOpening != TransformationOpening.None;
    internal bool UsesMorphic => _enabled.ButtonPressed && _morphic.ButtonPressed;
    internal int EnabledConditionCount => _enabled.ButtonPressed ? 1 : 0;
    private bool Zh => _text?.LanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;

    internal TransformationAggregatePage(IGameIconResolver icons, ICharacterPoolIconProvider characters,
        ICardPickerFilterIconProvider filters, AnchoredTooltipHost tooltip)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(body);
        Populate(_opening, ["No Neow", "Leafy ×2", "New Leaf ×1", "Bones ×3"]);
        Populate(_order, ["Any", "Leafy → New Leaf", "New Leaf → Leafy"]);
        Populate(_predicate, ["Rare count", "Contains multiset"]);
        body.AddThemeConstantOverride("separation", 12);
        body.AddChild(_enabled);
        var n = new HFlowContainer(); n.AddChild(_useNeow); n.AddChild(_opening); n.AddChild(_order); body.AddChild(n);
        _neowStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_neowStatus);
        var events = new HFlowContainer(); events.AddChild(_morphic); events.AddChild(_aroma); events.AddChild(_whisper); events.AddChild(_symbiote); events.AddChild(_trial); body.AddChild(events);
        var p = new HBoxContainer(); p.AddChild(_predicate); p.AddChild(_rare); body.AddChild(p);
        _help.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_help);
        body.AddChild(_targetRow);
        _picker = new(icons, characters, filters, tooltip); AddChild(_picker);
        _picker.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        VisibilityChanged += () => { if (!IsVisibleInTree()) TryCancelTransientSurface(); };
        _slots = Enumerable.Range(0, 10).Select(_ => new SearchCardResultSlot(icons, _picker.Open)).ToArray();
        foreach (var slot in _slots) { _targetRow.AddChild(slot); slot.Changed += Notify; }
        foreach (var box in new[] { _enabled, _useNeow, _morphic, _aroma, _whisper, _symbiote, _trial }) box.Toggled += _ => Notify();
        foreach (var option in new[] { _opening, _order, _predicate }) Ui1Theme.ApplyOptionButton(option);
        _predicate.ItemSelected += _ => Notify();
        _opening.ItemSelected += index =>
        {
            if (_restoring) return;
            _restoring = true; _useNeow.ButtonPressed = index != 0; _restoring = false;
            if (index != 0) OpeningRequested?.Invoke((TransformationOpening)index);
            Notify();
        };
        _rare.ValueChanged += _ => Notify();
    }
    internal TransformationAggregateCondition? BuildDraft() => !_enabled.ButtonPressed ? null : new(
        UsesNeow ? _currentOpening : TransformationOpening.None,
        UsesNeow ? _currentOrder : TransformationPickupOrder.Any,
        _morphic.ButtonPressed, _aroma.ButtonPressed, _whisper.ButtonPressed,
        (TransformationAggregatePredicate)_predicate.Selected, _predicate.Selected == 0 ? (int)_rare.Value : 0,
        _predicate.Selected == 1 ? _slots.Select(s => s.SelectedKey).Where(k => k.HasValue).Select(k => k!.Value).ToArray() : []) { Symbiote = _symbiote.ButtonPressed, TrialNondescript = _trial.ButtonPressed };

    internal void Restore(TransformationAggregateCondition? c)
    {
        _restoring = true;
        _enabled.ButtonPressed = c is not null; _opening.Select((int)(c?.Opening ?? TransformationOpening.None));
        _useNeow.ButtonPressed = c?.UsesNeow == true;
        _order.Select((int)(c?.PickupOrder ?? TransformationPickupOrder.Any));
        _morphic.ButtonPressed = c?.MorphicGrove == true; _aroma.ButtonPressed = c?.AromaOfChaos == true; _whisper.ButtonPressed = c?.WhisperingHollow == true; _symbiote.ButtonPressed = c?.Symbiote == true; _trial.ButtonPressed = c?.TrialNondescript == true;
        _predicate.Select((int)(c?.Predicate ?? TransformationAggregatePredicate.RareCountAtLeast)); _rare.Value = Math.Max(1, c?.MinimumRareCount ?? 1);
        for (int i = 0; i < _slots.Length; i++) _slots[i].Select(c is not null && i < c.TargetMultiset.Count ? c.TargetMultiset[i] : null, notify: false);
        _restoring = false; Refresh();
    }
    internal void ReadNeow(NeowRouteFilterDraft n)
    {
        var previous = (_currentOpening, _currentOrder);
        _currentOpening = n.RouteRelicKey == BaseGameModelKeys.Relics.LeafyPoultice ? TransformationOpening.LeafyPoultice :
            n.RouteRelicKey == BaseGameModelKeys.Relics.NewLeaf ? TransformationOpening.NewLeaf :
            n.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones && n.RequiredBonesRelics.Count == 2 &&
            n.RequiredBonesRelics.Contains(BaseGameModelKeys.Relics.LeafyPoultice) && n.RequiredBonesRelics.Contains(BaseGameModelKeys.Relics.NewLeaf)
                ? TransformationOpening.BonesLeafyNewLeaf : TransformationOpening.None;
        _currentOrder = _currentOpening != TransformationOpening.BonesLeafyNewLeaf || n.BonesOrderMode == BonesRouteOrderMode.AnyOrder
            ? TransformationPickupOrder.Any : n.RequiredBonesRelics[0] == BaseGameModelKeys.Relics.LeafyPoultice
                ? TransformationPickupOrder.LeafyThenNewLeaf : TransformationPickupOrder.NewLeafThenLeafy;
        if (previous != (_currentOpening, _currentOrder)) TryCancelTransientSurface();
        _opening.Select((int)_currentOpening); _order.Select((int)_currentOrder); Refresh();
    }
    internal void BindPool(IEnumerable<MorphicGroveCard> pool)
    {
        _eventMetadata.Clear();
        foreach (var card in pool)
            _eventMetadata[card.CardKey] = new(card.CardKey, [],
                Enum.TryParse<EffectCardRarity>(card.Rarity, out var rarity) ? rarity : default,
                Enum.TryParse<EffectCardType>(card.CardType, out var type) ? type : default);
        _eventPool = _eventMetadata.Keys.ToArray(); Refresh();
    }
    internal void BindNeowPools(IEnumerable<ModelKey> leafy, IEnumerable<ModelKey> newLeaf,
        IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> metadata)
    { _leafyPool = leafy.Distinct().ToArray(); _newLeafPool = newLeaf.Distinct().ToArray(); _neowMetadata = metadata; Refresh(); }
    internal void SetRunning(bool running) { _running = running; if (running) TryCancelTransientSurface(); Refresh(); }
    internal bool TryCancelTransientSurface() { if (!_picker.IsOpen) return false; _picker.Cancel(); return true; }
    internal void ApplyLocalization(IUiTextProvider text, IGameContentNameResolver names)
    {
        _text = text; _names = names; _picker.ApplyLocalization(text, names);
        _enabled.Text = Zh ? "启用变牌组合" : "Enable transformation aggregate";
        _useNeow.Text = Zh ? "纳入涅奥变牌" : "Include Neow transforms";
        string leafy = names.Resolve(BaseGameModelKeys.Relics.LeafyPoultice, GameContentKind.Relic);
        string leaf = names.Resolve(BaseGameModelKeys.Relics.NewLeaf, GameContentKind.Relic);
        string bones = names.Resolve(BaseGameModelKeys.Relics.NeowsBones, GameContentKind.Relic);
        Populate(_opening, [Zh ? "不纳入 / 无可用来源" : "None / unavailable", $"{leafy} ×2", $"{leaf} ×1", $"{bones}: {leafy} + {leaf} ×3"]);
        _opening.TooltipText = Zh ? "快捷设置普通涅奥；其他兼容条件保留。" : "Set the Neow selection; retain compatible conditions.";
        Populate(_order, [Zh ? "拾取顺序不限" : "Either pickup order", $"{leafy} → {leaf}", $"{leaf} → {leafy}"]);
        _order.TooltipText = Zh ? "跟随涅奥页面的拾取顺序" : "Follows pickup order on the Neow page";
        _morphic.Text = Zh ? "变形灵林谷 ×2" : "Morphic Grove ×2";
        _aroma.Text = Zh ? "混沌芳香 ×1" : "Aroma of Chaos ×1";
        _whisper.Text = Zh ? "低语空谷 ×1" : "Whispering Hollow ×1";
        _symbiote.Text = Zh ? "共生体 ×1" : "Symbiote ×1";
        _trial.Text = (Zh ? "审判 · " : "Trial · ") + text.Get("query.event.results.case_3") + " ×2";
        Populate(_predicate, Zh ? ["稀有牌至少", "包含所选牌（来源不限）"] : ["Rare cards: at least", "Contains selected cards, any source"]);
        Refresh();
    }
    private static void Populate(OptionButton option, string[] labels)
    {
        int selected = Math.Max(0, option.Selected); option.Clear(); foreach (string label in labels) option.AddItem(label); option.Select(selected);
    }
    private void Notify() { if (_restoring) return; Refresh(); Changed?.Invoke(); }
    private void Refresh()
    {
        _enabled.Disabled = _running;
        bool active = _enabled.ButtonPressed;
        _pool = (UsesNeow && _currentOpening is TransformationOpening.LeafyPoultice or TransformationOpening.BonesLeafyNewLeaf ? _leafyPool : Array.Empty<ModelKey>())
            .Concat(UsesNeow && _currentOpening is TransformationOpening.NewLeaf or TransformationOpening.BonesLeafyNewLeaf ? _newLeafPool : [])
            .Concat(_morphic.ButtonPressed || _aroma.ButtonPressed || _whisper.ButtonPressed || _symbiote.ButtonPressed || _trial.ButtonPressed ? _eventPool : []).Distinct().ToArray();
        _opening.Disabled = _order.Disabled = _predicate.Disabled = _running || !active;
        _useNeow.Disabled = _running || !active;
        _order.Disabled = true;
        _morphic.Disabled = _aroma.Disabled = _whisper.Disabled = _symbiote.Disabled = _trial.Disabled = _running || !active;
        _order.Visible = _currentOpening == TransformationOpening.BonesLeafyNewLeaf; _rare.Visible = _predicate.Selected == 0; _rare.Editable = !_running && active;
        _neowStatus.Text = _currentOpening == TransformationOpening.None
            ? (Zh ? "当前涅奥没有可纳入的变牌来源；已填目标与稀有张数保留。" : "No supported Neow transform source selected; targets and rare count are retained.")
            : (Zh ? "来源及拾取顺序实时读取涅奥页面；可继续在那里编辑其他条件。" : "Source and pickup order follow Neow; other conditions remain editable there.");
        _targetRow.Visible = _predicate.Selected == 1;
        int count = BuildDraft()?.OpportunityCount ?? 0;
        _help.Text = Zh ? $"共 {count} 次变牌。目标牌可来自任意已选来源，重复目标要求对应张数；稀有数设为 {count} 即全部稀有。\n事件以发生并选择变牌、届时有可变的初始打击／防御为前提；本页不要求事件出现在队列。" :
            $"{count} transformations. Targets may come from any selected source; repeated targets require multiple copies. Set the rare count to {count} for all rare.\nEvents assume the transform choice and legal initial Basics at entry; event queue occurrence is not required here.";
        if (_text is null || _names is null) return;
        var metadata = new Dictionary<ModelKey, CardPickerCandidateMetadata>(_neowMetadata);
        foreach (var (key, value) in _eventMetadata) metadata[key] = value;
        var pickerContext = new CardPickerContext(_pool, metadata, false, "transformation-aggregate");
        foreach (var slot in _slots)
        {
            slot.Configure(Zh ? "目标牌" : "Target card", _pool, GameContentKind.Card, IconVariant.CardPickerLarge, _names,
                Zh ? "不限" : "Any", "", pickerTitle: Zh ? "选择目标牌" : "Select target card", cardPickerContext: pickerContext);
            slot.SetEnabled(active && !_running && _pool.Length > 0);
        }
    }
}
