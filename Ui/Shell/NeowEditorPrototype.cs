using Godot;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Shell;

// Editor-local interaction state; WorkbenchEditorContracts exports existing canonical conditions.
// The hidden Result-only experiment is not part of the Production authoring path.
internal sealed partial class NeowEditorPrototype : Control
{
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.Canonical;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("neow-prototype");
    private readonly Control _body = new(), _overlay = new() { Visible = false };
    private IGameContentNameResolver _names = RuntimeGameContentNameResolver.Create("zh");
    private IUiTextProvider _text = JsonUiTextProvider.CreateUi13("zh");
    private sealed class SeatDraft
    {
        public NeowSearchUiCatalog? Catalog;
        public string Context = "", Problem = "";
        public bool Ordered;
        public KaleidoscopeGroupOrderMode KaleidoscopeOrder = KaleidoscopeGroupOrderMode.AnyOrder;
        public NeowSpecialOfferKind ScrollOffer = NeowSpecialOfferKind.None;
        public bool JointTransforms, JointCapsules;
        public readonly ModelKey?[] TransformTargets = new ModelKey?[3], CapsuleTargets = new ModelKey?[3];
        public ModelKey? Source, Curse;
        public readonly List<ModelKey> Bones = [];
        public readonly Dictionary<string, ModelKey?> Slots = [];
        public readonly List<Target> Targets = [];
        public readonly HashSet<ModelKey> Bans = [];
        public int Count => (Source.HasValue ? 1 : 0) + Bones.Count + Slots.Count + Targets.Count + (Curse.HasValue ? 1 : 0)
            + (ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw ? 1 : 0)
            + TransformTargets.Count(k => k.HasValue) + CapsuleTargets.Count(k => k.HasValue);
    }
    private readonly Dictionary<int, SeatDraft> _drafts = [];
    private readonly Dictionary<int, SeatDraft> _partyCatalogDrafts = [];
    private SeatDraft _draft = new();
    private NeowSearchUiCatalog? _catalog { get => _draft.Catalog; set => _draft.Catalog = value; }
    private string _language = "zh";
    private string _problem { get => _draft.Problem; set => _draft.Problem = value; }
    private bool _ordered { get => _draft.Ordered; set => _draft.Ordered = value; }
    private ModelKey? _source { get => _draft.Source; set => _draft.Source = value; }
    private List<ModelKey> _bones => _draft.Bones;
    private ModelKey? _bonesFinalCurse { get => _draft.Curse; set => _draft.Curse = value; }
    private Dictionary<string, ModelKey?> _slots => _draft.Slots;
    private List<Target> _targets => _draft.Targets;
    private HashSet<ModelKey> _bans => _draft.Bans;
    private IReadOnlyList<ModelKey> _identities = [];
    private int _players = 1, _removed, _activeSeat;
    private double _noticeSeconds;
    private Label? _notice;
    private readonly Dictionary<BaseButton, bool> _blocked = [];
    private sealed class Target(string kind, ModelKey key)
    {
        public string Kind = kind;
        public ModelKey Key = key;
    }
    public event Action? OpeningChanged;
    internal Func<int, string, bool>? TransformTakenOver { get; set; }
    internal IReadOnlyList<TransformationPrototypeSource> TransformationSources(int seat)
    {
        if (!_drafts.TryGetValue(seat, out var draft) || draft.Catalog is null) return [];
        ModelKey leafy = BaseGameModelKeys.Relics.LeafyPoultice;
        ModelKey leaf = BaseGameModelKeys.Relics.NewLeaf;
        ModelKey bones = BaseGameModelKeys.Relics.NeowsBones;
        if (draft.Source == leafy) return [new("N.LeafyPoultice", leafy, 2)];
        if (draft.Source == leaf) return [new("N.NewLeaf", leaf, 1)];
        if (draft.Source != bones || draft.Bones.Count != 2) return [];
        if (draft.Bones.Contains(leafy) && draft.Bones.Contains(leaf))
            return [new("N.BonesLeafyNewLeaf", bones, 3)];
        if (draft.Bones.Contains(leafy))
            return [new("N.BonesLeafyOther", bones, 2, draft.Bones.First(key => key != leafy))];
        return [];
    }
    internal IReadOnlyList<ModelKey> TransformationCardCandidates(int seat)
    {
        if (!_drafts.TryGetValue(seat, out var draft) || draft.Catalog is null) return [];
        return draft.Catalog.InitialBasicTransformCards.Concat(draft.Catalog.NewLeafTransformCards)
            .Distinct(ModelKeyComparer.Instance).ToArray();
    }
    internal void PickTransformationCards(int seat, IReadOnlyList<ModelKey> candidates, Action<ModelKey> selected)
    {
        if (!_drafts.TryGetValue(seat, out var draft) || draft.Catalog is null || candidates.Count == 0) return;
        var context = draft.Catalog.CreateCardPickerContext(candidates, true, "transformation-prototype");
        PickExternalCards(candidates, context, draft.Catalog.MultiplayerOnlyCards, _players, selected);
    }
    internal void ClearTakenOverTransformTargets(int seat)
    {
        if (!_drafts.TryGetValue(seat, out var draft)) return;
        foreach (string key in draft.Slots.Keys.Where(key =>
                     key.StartsWith($"{BaseGameModelKeys.Relics.LeafyPoultice}/", StringComparison.Ordinal) ||
                     key.StartsWith($"{BaseGameModelKeys.Relics.NewLeaf}/", StringComparison.Ordinal)).ToArray())
            draft.Slots.Remove(key);
        Array.Clear(draft.TransformTargets);
    }
    private bool TManagesTransform() => _players == 1 && TransformationSources(_activeSeat).Any(source =>
        TransformTakenOver?.Invoke(_activeSeat, source.Id) == true);
    internal IReadOnlyList<ModelKey> ExplicitOpeningRelics(int seat)
    {
        if (!(_players > 1 ? _partyCatalogDrafts : _drafts).TryGetValue(seat, out var d) || d.Source is not { } source) return Array.Empty<ModelKey>();
        var roots = source == BaseGameModelKeys.Relics.NeowsBones ? d.Bones.ToArray() : new[] { source };
        var result = new List<ModelKey>(roots);
        bool joint = source == BaseGameModelKeys.Relics.NeowsBones && d.JointCapsules
            && roots.Contains(BaseGameModelKeys.Relics.SmallCapsule) && roots.Contains(BaseGameModelKeys.Relics.LargeCapsule);
        if (joint) result.AddRange(d.CapsuleTargets.Where(k => k.HasValue).Select(k => k!.Value));
        else foreach (var root in roots.Where(IsCapsule))
            result.AddRange(d.Slots.Where(s => s.Key.StartsWith(root + "/", StringComparison.Ordinal) && s.Value.HasValue).Select(s => s.Value!.Value));
        return result.Distinct().ToArray();

    }
    public event Action<bool>? ModalChanged;
    public event Action? GuideRequested;
    private bool _guideExpanded; // Session UI preference, independent of seat/query state.
    private string NameOf(ModelKey k) => _names.Resolve(k, Kind(k));
    private static GameContentKind Kind(ModelKey k) => k.Category switch
    { "CARD" => GameContentKind.Card, "POTION" => GameContentKind.Potion, _ => GameContentKind.Relic };
    private static ModelKey Relic(string id) => new("RELIC", id);

    public NeowEditorPrototype(ModRuntimeSnapshot runtime)
    { _runtime = runtime; AddChild(_body); AddChild(_overlay); }
    public void AttachOverlay(Control shell)
    { _overlay.Reparent(shell); _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }

    public void Refresh(string language, ModelKey character, int ascension, int players, int seat,
        MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState? unlocks = null, bool render = true)
    {
        ClosePicker(); _language = language; _text = JsonUiTextProvider.CreateUi13(language); _names = RuntimeGameContentNameResolver.Create(language);
        _players = players;
        _activeSeat = seat;
        var drafts = players > 1 ? _partyCatalogDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(s => s >= players).ToArray())
        { NotifyRemoved(drafts[removedSeat].Count); drafts.Remove(removedSeat); }
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;
        if (_identities.Count == 0) _identities = NeowSearchUiCatalog.AllRuntimeIdentities();
        string unlockKey = players == 1 ? System.Text.Json.JsonSerializer.Serialize(MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks);
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}";
        if (context != _draft.Context)
        {
            _draft.Context = context; _problem = "";
            try
            {
                if (players > 1 && unlocks is null)
                {
                    _catalog = null;
                    _problem = _text.Get("query.neow.context.read_unlocks_required");
                    var modeAllowed = NeowSearchUiCatalog.CaptureModeAllowedIdentities(character, ascension, players);
                    int before = _draft.Count;
                    if (_source is { } identity && !modeAllowed.Contains(identity)) _source = null;
                    _bones.RemoveAll(k => !modeAllowed.Contains(k));
                    if (_bones.Count < 2) _ordered = false;
                    foreach (string id in _slots.Keys.ToArray())
                        if (ModelKey.TryParseExact(id.Split('/')[0], out var owner) && !modeAllowed.Contains(owner)) _slots.Remove(id);
                    NotifyRemoved(before - _draft.Count);
                }
                else
                {
                    var resolved = players == 1 ? MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(unlocks!);
                    _catalog = NeowSearchUiCatalog.CaptureForPicker(_runtime, character, ascension, players, seat, resolved);
                    Revalidate();
                }
            }
            catch (Exception e) { _catalog = null; _problem = _text.Get("query.neow.context.catalog_unavailable"); RuntimeLog.Warn("neowPrototypeCatalog=" + e.Message); }
        }
        if (render) RenderEditor();
    }
    private IReadOnlyList<ModelKey> Sources => _identities;
    private bool IdentityAllowed(ModelKey key) => _catalog is not null && (_source == BaseGameModelKeys.Relics.NeowsBones && key != BaseGameModelKeys.Relics.NeowsBones
        ? _catalog.BonesNeowRelics.Contains(key) : _catalog.RouteRelics.Contains(key));
    private bool HasBonesPair(ModelKey first, ModelKey second) => _source == BaseGameModelKeys.Relics.NeowsBones
        && _bones.Count == 2 && _bones.Contains(first) && _bones.Contains(second);
    private bool HasTransformPair => HasBonesPair(BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf);
    private bool HasCapsulePair => HasBonesPair(BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule);
    private IReadOnlyList<ModelKey> JointTransformPool => _catalog!.TransformCards.Concat(_catalog.NewLeafTransformCards).Distinct().ToArray();
    private void SetTransformAllocation(bool joint)
    {
        if (!HasTransformPair || joint == _draft.JointTransforms || (joint && _players != 1)) return;
        foreach (string id in _slots.Keys.Where(id => id.StartsWith($"{BaseGameModelKeys.Relics.LeafyPoultice}/", StringComparison.Ordinal)
            || id.StartsWith($"{BaseGameModelKeys.Relics.NewLeaf}/", StringComparison.Ordinal)).ToArray()) _slots.Remove(id);
        Array.Clear(_draft.TransformTargets);
        _draft.JointTransforms = joint;
    }

    // UI-to-existing-semantic seam only; the workbench Search action remains disconnected.
    internal SearchQuery BuildBonesQuery()
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones) return SearchQuery.Empty;
        var effects = new List<NeowStructuredEffectSearchCondition>();
        bool jointTransforms = HasTransformPair && _draft.JointTransforms;
        bool jointCapsules = HasCapsulePair && _draft.JointCapsules;
        foreach (var child in _bones)
        {
            if (jointTransforms || jointCapsules) continue;
            foreach (var c in NeowEffectCardRegistry.Get(child).Components)
            {
                var targets = Enumerable.Range(0, c.SlotCount).Select(i => _slots.GetValueOrDefault($"{child}/{c.ComponentId}/{i}"))
                    .Where(k => k.HasValue).Select(k => k!.Value).ToArray();
                if (targets.Length > 0) effects.Add(new(child, c.ConditionKind, c.Scope, c.OutputKind, targets, AllowDuplicateOutputs: c.AllowDuplicateOutputs));
            }
        }
        if (jointCapsules && _draft.CapsuleTargets.Any(k => k.HasValue))
            effects.Add(new(BaseGameModelKeys.Relics.NeowsBones, NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
                _draft.CapsuleTargets.Where(k => k.HasValue).Select(k => k!.Value).ToArray(), AllowDuplicateOutputs: false));
        if (_bonesFinalCurse is { } curse)
            effects.Add(new(BaseGameModelKeys.Relics.NeowsBones, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.FinalCurse, NeowStructuredOutputKind.Curse, [curse]));
        var targetsT = _draft.TransformTargets.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
        TransformationAggregateCondition? aggregate = jointTransforms && targetsT.Length > 0
            ? new(TransformationOpening.BonesLeafyNewLeaf,
                !_ordered ? TransformationPickupOrder.Any : _bones[0] == BaseGameModelKeys.Relics.LeafyPoultice
                    ? TransformationPickupOrder.LeafyThenNewLeaf : TransformationPickupOrder.NewLeafThenLeafy,
                false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0, targetsT) : null;
        return SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, _bones.ToArray(),
                _ordered ? BonesRouteOrderMode.ExactOrder : BonesRouteOrderMode.AnyOrder),
            StructuredOpeningEffects = effects.ToArray(), TransformationAggregate = aggregate
        };
    }

    private void Revalidate()
    {
        int before = _draft.Count;
        if (_source is { } source && !_catalog!.RouteRelics.Contains(source)) _source = null;
        _bones.RemoveAll(k => !_catalog!.BonesNeowRelics.Contains(k));
        if (_bones.Count < 2) _ordered = false;
        if (_bonesFinalCurse is { } curse && !_catalog!.Curses.Contains(curse)) _bonesFinalCurse = null;
        if (_catalog!.CharacterKey != BaseGameModelKeys.Characters.Defect || !_catalog.RouteRelics.Contains(BaseGameModelKeys.Relics.ScrollBoxes))
            _draft.ScrollOffer = NeowSpecialOfferKind.None;
        if (!_catalog.RouteRelics.Contains(BaseGameModelKeys.Relics.Kaleidoscope)) _draft.KaleidoscopeOrder = KaleidoscopeGroupOrderMode.AnyOrder;
        var validSlots = new Dictionary<string, IReadOnlyList<ModelKey>>();
        foreach (var key in _catalog!.RouteRelics)
        {
            if (key == BaseGameModelKeys.Relics.ScrollBoxes)
                for (int i = 0; i < 3; i++) validSlots[$"{key}/bundle/{i}"] = ScrollBundlePool;
            foreach (var component in NeowEffectCardRegistry.Get(key).Components)
                for (int i = 0; i < component.SlotCount; i++) validSlots[$"{key}/{component.ComponentId}/{i}"] = _catalog.Candidates(component.CandidatePool);
        }
        foreach (var (id, value) in _slots.ToArray())
            if (value is { } key && (!validSlots.TryGetValue(id, out var candidates) || !candidates.Contains(key))) _slots.Remove(id);
        _targets.RemoveAll(t => !Pool(t.Kind).Contains(t.Key));
        if (!HasTransformPair || _players != 1) { _draft.JointTransforms = false; Array.Clear(_draft.TransformTargets); }
        else for (int i = 0; i < 3; i++) if (_draft.TransformTargets[i] is { } target && !JointTransformPool.Contains(target)) _draft.TransformTargets[i] = null;
        if (!HasCapsulePair) { _draft.JointCapsules = false; Array.Clear(_draft.CapsuleTargets); }
        else for (int i = 0; i < 3; i++) if (_draft.CapsuleTargets[i] is { } target && !_catalog.OrdinaryRelics.Contains(target)) _draft.CapsuleTargets[i] = null;
        NotifyRemoved(before - _draft.Count);
    }

    private void NotifyRemoved(int count)
    { if (count <= 0) return; _removed += count; _noticeSeconds = 5; }
    public override void _Process(double delta)
    {
        if (_noticeSeconds <= 0) return;
        _noticeSeconds -= delta;
        if (_noticeSeconds <= 0) { _removed = 0; if (GodotObject.IsInstanceValid(_notice)) _notice!.Hide(); }
    }
    private static void Clear(Node node)
    { foreach (var c in node.GetChildren()) { node.RemoveChild(c); c.QueueFree(); } }
    private void RenderEditor()
    {
        NormalizeCapsuleTargets();
        OpeningChanged?.Invoke();
        Clear(_body); _body.Size = Size;
        if (_removed > 0)
        {
            _notice = Text(_body, _text.Format("query.neow.notice.removed_invalid", _removed), 4, Size.Y - 32, Size.X - 8, 17);
            _notice.ZIndex = 10;
        }
        var guide = FamilyGuide(_body, Size.X - 20);
        Source(_body, Size.X - 20, guide);
    }

    private VBoxContainer FamilyGuide(Control host, float width) =>
        SearchEditorGuide.Build(host, _p, width, _text.Get("query.neow.guide.title"),
            _text.Get("query.neow.guide.summary"),
            [_text.Get("query.neow.guide.usage"), _text.Get("query.neow.guide.pickup_order")],
            _guideExpanded, () => { _guideExpanded = !_guideExpanded; RenderEditor(); }, () => GuideRequested?.Invoke());

    // Retained research surface; not reachable from the current N editor.
    private void RenderResultOnlyPrototype()
    {
        var scroll = new ScrollContainer { Position = new(0, 58), Size = new(Size.X, Size.Y - 58), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body.AddChild(scroll);
        var content = new Control { CustomMinimumSize = new(Size.X - 20, 1) }; scroll.AddChild(content);
        float width = Size.X - 28;
        if (_catalog is null) { Text(content, _problem, 0, 12, width); return; }
        float bottom = Results(content, width);
        content.CustomMinimumSize = new(width, bottom + 20);
    }

    // Click transitions ported verbatim in behavior from NeowRouteSelectionPanel:
    // ToggleBonesMode / OnRelicPressed / SetBonesOrderMode / SwapBonesOrder.
    // These change session-local UI state only, not canonical Query semantics.
    private void ToggleBonesMode()
    {
        if (!IdentityAllowed(BaseGameModelKeys.Relics.NeowsBones)) return;
        _source = _source == BaseGameModelKeys.Relics.NeowsBones ? null : BaseGameModelKeys.Relics.NeowsBones;
        // Enter/exit clears the normal route; retain the internal list and its order mode.
        RenderEditor();
    }

    private void OnSourceRelicPressed(ModelKey key)
    {
        if (!IdentityAllowed(key)) return;
        if (_players > 1) _partyChoices[_activeSeat] = [];
        if (_source == BaseGameModelKeys.Relics.NeowsBones)
        {
            int index = _bones.FindIndex(existing => existing == key);
            if (index >= 0)
            {
                _bones.RemoveAt(index);
                if (_bones.Count < 2) _ordered = false;
            }
            else if (_bones.Count < 2) _bones.Add(key);
        }
        else _source = _source == key ? null : key;
        if (!HasTransformPair) { _draft.JointTransforms = false; Array.Clear(_draft.TransformTargets); }
        if (!HasCapsulePair) { _draft.JointCapsules = false; Array.Clear(_draft.CapsuleTargets); }
        RenderEditor();
    }

    private void SetBonesOrder(bool ordered)
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones || _ordered == ordered) return;
        if (ordered && _bones.Count != 2) return;
        _ordered = ordered;
        RenderEditor();
    }

    private void SwapBonesOrder()
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones || !_ordered || _bones.Count != 2) return;
        (_bones[0], _bones[1]) = (_bones[1], _bones[0]);
        RenderEditor();
    }

    private void Source(Control host, float width, Control guide)
    {
        const float identityWidth = 260, gap = 20;
        const int identityColumns = 5;
        float resultsWidth = width - identityWidth - gap - 12;
        Control Pane(float x, float paneWidth)
        {
            var scroll = new ScrollContainer { Name = x == 0 ? "NeowSources" : "NeowResults",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            void LayoutPane()
            {
                float top = guide.Size.Y + 8;
                scroll.Position = new(x, top);
                scroll.Size = new(paneWidth + 12, Math.Max(0, Size.Y - top));
            }
            guide.Resized += LayoutPane;
            LayoutPane();
            host.AddChild(scroll);
            var content = new Control { CustomMinimumSize = new(paneWidth, 1) };
            scroll.AddChild(content); return content;
        }
        var identity = Pane(0, identityWidth);
        var results = Pane(identityWidth + gap, resultsWidth);
        var bonesKey = BaseGameModelKeys.Relics.NeowsBones;
        Text(identity, _text.Get("query.neow.bones.title"), 0, 0, identityWidth, 18, true);
        var bonesButton = Button(identity, "", 0, 26, identityWidth - 8,
            ToggleBonesMode, _source == bonesKey, 44);
        bonesButton.Disabled = !IdentityAllowed(bonesKey);
        bonesButton.Modulate = bonesButton.Disabled ? new Color(1, 1, 1, .52f) : Colors.White;
        Picture(bonesButton, bonesKey, 8, 3, 38);
        Text(bonesButton, NameOf(bonesKey), 54, 8, identityWidth - 70, 18);
        float y = 80;
        if (_source == bonesKey)
        {
            Text(identity, _text.Format("query.neow.bones.inner_relics", _bones.Count), 0, y, identityWidth, 17, true);
            y += 28;
        }
        bool HasRandomResult(ModelKey key) => NeowEffectCardRegistry.Get(key).HasEffectCard || key == Relic("MASSIVE_SCROLL");
        var ordinary = Sources.Where(k => k != bonesKey).ToArray();
        foreach (bool random in new[] { true, false })
        {
            Text(identity, _text.Get(random ? "query.neow.source.random_results" : "common.other"), 0, y, identityWidth, 18, true);
            y += 26;
            var keys = ordinary.Where(k => HasRandomResult(k) == random).ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                bool selectedIdentity = _source == bonesKey ? _bones.Contains(key) : _source == key;
                var b = Button(identity, "", i % identityColumns * 52, y + i / identityColumns * 52, 48,
                    () => OnSourceRelicPressed(key), selectedIdentity, 48);
                b.Disabled = !IdentityAllowed(key) || (_source == bonesKey && _bones.Count >= 2 && !selectedIdentity);
                b.Modulate = b.Disabled ? new Color(1f, 1f, 1f, 0.52f) : Colors.White;
                Picture(b, key, 4, 4, 40); b.TooltipText = NameOf(key); b.AccessibilityName = NameOf(key);
            }
            y += (int)Math.Ceiling(keys.Length / (double)identityColumns) * 52 + (random ? 10 : 0);
        }
        identity.CustomMinimumSize = new(identityWidth, y);
        if (_catalog is null) { Text(results, _problem, 0, 0, resultsWidth, 18, true); return; }
        float end = 0;
        if (_source == bonesKey)
        {
            Picture(results, bonesKey, 0, 4, 32);
            Text(results, NameOf(bonesKey), 40, 6, resultsWidth - 370, 18);
            var order = Button(results, _text.Get(_ordered ? "query.neow.pickup_order.ordered" : "query.neow.pickup_order.any"),
                resultsWidth - 248, 0, 196, () => SetBonesOrder(!_ordered), _ordered);
            order.Disabled = _bones.Count != 2;
            var swap = Button(results, "⇄", resultsWidth - 40, 0, 40, SwapBonesOrder);
            swap.TooltipText = swap.AccessibilityName = _text.Get("common.swap");
            swap.Disabled = !_ordered || _bones.Count != 2;

            const float siblingGap = 16, childTop = 52;
            float childWidth = (resultsWidth - siblingGap) / 2;
            bool pairMode = HasTransformPair || HasCapsulePair;
            bool joint = HasTransformPair ? _draft.JointTransforms : HasCapsulePair && _draft.JointCapsules;
            bool alignOrderRows = !joint && _bones.Count == 2 && _bones.Any(child =>
                NeowEffectCardRegistry.Get(child).Components.Any(component => component.SupportsOrderSelection));
            float outputTop = childTop + (pairMode ? 96 : 44);
            if (pairMode)
            {
                Text(results, _text.Get("query.neow.results.allocation"), 0, childTop + 48, 96, 17, true);
                float modeWidth = (resultsWidth - 116) / 2;
                void SelectAllocation(bool value)
                {
                    if (HasTransformPair) SetTransformAllocation(value);
                    else _draft.JointCapsules = value; // Existing capsule donor retains separate/combined history.
                    RenderEditor();
                }
                Button(results, _text.Get("query.neow.results.separate"), 104, childTop + 40, modeWidth, () => SelectAllocation(false), !joint);
                var combined = Button(results, _text.Get("query.neow.results.combined"), 116 + modeWidth, childTop + 40, modeWidth, () => SelectAllocation(true), joint);
                combined.Disabled = HasTransformPair && _players != 1;
            }
            end = childTop + 40;
            for (int i = 0; i < 2; i++)
            {
                float x = i * (childWidth + siblingGap);
                if (i >= _bones.Count)
                {
                    Text(results, _language == "zh" ? $"内部遗物 {i + 1}" : $"Inner relic {i + 1}", x, childTop, childWidth, 15, true);
                    Text(results, _language == "zh" ? "不限 · 从左侧选择" : "Any · choose on the left", x, childTop + 25, childWidth, 16, true);
                    end = Math.Max(end, childTop + 66);
                    continue;
                }
                var child = _bones[i];
                if (!joint) ResultSourceHeader(results, child, x, childTop, childWidth, 32, (_ordered ? $"{i + 1} · " : ""));
                else
                {
                    Picture(results, child, x, childTop, 32);
                    Text(results, (_ordered ? $"{i + 1} · " : "") + NameOf(child), x + 40, childTop + 3, childWidth - 40, 18);
                }
                if (!joint) end = Math.Max(end, Outputs(results, child, x, outputTop, childWidth, alignOrderRows));
            }
            if (joint)
            {
                bool transforms = HasTransformPair;
                var targets = transforms ? _draft.TransformTargets : _draft.CapsuleTargets;
                Text(results, _text.Get(transforms ? "query.neow.results.combined_cards" : "query.neow.results.combined_relics"), 0, outputTop, resultsWidth, 18, true);
                if (transforms && TManagesTransform())
                {
                    Text(results, _text.Get("query.transform.managed"), 0, outputTop + 34, resultsWidth, 16, true);
                    end = outputTop + 76;
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int slot = i;
                        ResultObject(results, targets[i], transforms ? NeowStructuredOutputKind.Card : NeowStructuredOutputKind.Relic,
                            i * (ResultObjectWidth + 12), outputTop + 32,
                            () => Pick(transforms ? JointTransformPool : CapsuleCandidates(slot), k => { targets[slot] = k; RenderEditor(); }),
                            () => { targets[slot] = null; RenderEditor(); });
                    }
                    end = outputTop + 32 + ResultObjectHeight;
                }
            }
            end += 12;
            Text(results, _text.Get("query.neow.results.final_curse"), 0, end, resultsWidth, 18, true);
            ObjectSlot(results, _bonesFinalCurse, _text.Get("common.add_any"), 0, end + 32, Math.Min(resultsWidth, 320),
                () => Pick(_catalog!.Curses, k => { _bonesFinalCurse = k; RenderEditor(); }),
                () => { _bonesFinalCurse = null; RenderEditor(); });
            end += 92;
        }
        else if (_source is { } source)
        {
            ResultSourceHeader(results, source, 0, end, resultsWidth, 40);
            end = Outputs(results, source, 0, end + 52, resultsWidth) + 24;
        }
        else
        {
            var hint = _p.Label(_text.Get("query.neow.results.empty"), 17, true);
            hint.Position = new(16, 24); hint.Size = new(resultsWidth - 32, 80);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; results.AddChild(hint);
            end = 120;
        }
        results.CustomMinimumSize = new(resultsWidth, Math.Max(1, end));
    }

    private IReadOnlyList<ModelKey> ScrollBundlePool => _catalog!.CommonCharacterCards.Concat(_catalog.UncommonCharacterCards).ToArray();

    private void ResultSourceHeader(Control parent, ModelKey source, float x, float y, float width, float iconSize, string prefix = "")
    {
        var header = Button(parent, "", x, y, width, () =>
        {
            foreach (string id in _slots.Keys.Where(id => id.StartsWith(source + "/", StringComparison.Ordinal)).ToArray()) _slots.Remove(id);
            if (source == BaseGameModelKeys.Relics.ScrollBoxes) _draft.ScrollOffer = NeowSpecialOfferKind.None;
            RenderEditor();
        }, height: iconSize);
        header.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        header.TooltipText = _text.Get("query.neow.results.clear_nested");
        header.AccessibilityName = NameOf(source) + " · " + header.TooltipText;
        Picture(header, source, 0, 0, iconSize);
        Text(header, prefix + NameOf(source), iconSize + 12, 3, width - iconSize - 12, iconSize == 40 ? 22 : 18);
    }

    private float Outputs(Control parent, ModelKey source, float x, float y, float width,
        bool alignOrderRow = false)
    {
        var components = NeowEffectCardRegistry.Get(source).Components;
        // A sibling with an order selector uses this row. Reserve the same space
        // before the other sibling's content so their labels and result slots align.
        if (alignOrderRow && !components.Any(component => component.SupportsOrderSelection)) y += 48;
        // Exact Kaleidoscope groups have a caption above each slot. Its sibling
        // needs the same caption space without acquiring group/order semantics.
        float siblingCaptionHeight = alignOrderRow && _draft.KaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder ? 26 : 0;
        if (source is { } && (source == BaseGameModelKeys.Relics.LeafyPoultice || source == BaseGameModelKeys.Relics.NewLeaf)
            && TManagesTransform())
        {
            Text(parent, _text.Get("query.event.results.transform_targets"), x, y, width, 18, true);
            Text(parent, _text.Get("query.transform.managed"), x, y + 32, width, 16, true);
            return y + 78;
        }
        int columns = Math.Max(1, (int)((width + 12) / (ResultObjectWidth + 12)));
        if (source == BaseGameModelKeys.Relics.ScrollBoxes)
        {
            if (_catalog!.CharacterKey == BaseGameModelKeys.Characters.Defect)
            {
                bool triple = _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw;
                float modeWidth = (width - 12) / 2;
                Button(parent, _text.Get("query.neow.scroll.normal_bundle"), x, y, modeWidth,
                    () => { _draft.ScrollOffer = NeowSpecialOfferKind.None; RenderEditor(); }, !triple);
                Button(parent, _text.Get("query.neow.scroll.triple_claw"), x + modeWidth + 12, y, modeWidth,
                    () => { _draft.ScrollOffer = NeowSpecialOfferKind.ScrollBoxesTripleClaw; RenderEditor(); }, triple);
                y += 48;
                if (triple)
                {
                    var claw = new ModelKey("CARD", "CLAW");
                    for (int i = 0; i < 3; i++)
                    {
                        var view = new Panel { Position = new(x + i % columns * (ResultObjectWidth + 12), y + i / columns * (ResultObjectHeight + 12)),
                            Size = new(ResultObjectWidth, ResultObjectHeight) };
                        parent.AddChild(view);
                        view.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface));
                        ResultContents(view, claw, NeowStructuredOutputKind.Card);
                    }
                    return y + (int)Math.Ceiling(3d / columns) * (ResultObjectHeight + 12);
                }
            }
            Text(parent, _text.Get("object.cards"), x, y, width, 18, true); y += 32 + siblingCaptionHeight;
            for (int i = 0; i < 3; i++)
                OutputSlot(parent, source, "bundle", i, ScrollBundlePool,
                    x + i % columns * (ResultObjectWidth + 12), y + i / columns * (ResultObjectHeight + 12), ResultObjectWidth);
            y += (int)Math.Ceiling(3d / columns) * (ResultObjectHeight + 12);
            return y;
        }
        if (components.Count == 0) return y;
        foreach (var c in components)
        {
            bool ordered = c.SupportsOrderSelection && _draft.KaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder;
            if (c.SupportsOrderSelection)
            {
                Button(parent, _text.Get(ordered ? "query.neow.generation_order.exact" : "query.neow.generation_order.any"), x, y, width - 52,
                    () => { _draft.KaleidoscopeOrder = ordered ? KaleidoscopeGroupOrderMode.AnyOrder : KaleidoscopeGroupOrderMode.ExactOrder; RenderEditor(); }, ordered);
                var swap = Button(parent, "⇄", x + width - 40, y, 40,
                    () => { SwapKaleidoscopeTargets(); RenderEditor(); });
                swap.TooltipText = swap.AccessibilityName = _text.Get("common.swap");
                swap.Disabled = !ordered;
                y += 48;
            }
            string title = c.OutputKind.ToString() switch { "Relic" => _text.Get("object.relics"), "Potion" => _text.Get("object.potions"), _ => _text.Get("object.cards") };
            Text(parent, title, x, y, width, 18, true); y += 32;
            bool objectView = c.OutputKind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Relic or NeowStructuredOutputKind.Potion;
            int slotColumns = objectView ? columns : width >= 440 ? 2 : 1;
            float slotWidth = objectView ? ResultObjectWidth : (width - (slotColumns - 1) * 12) / slotColumns;
            float captionHeight = ordered ? 26 : siblingCaptionHeight;
            float rowHeight = (objectView ? ResultObjectHeight + 12 : 68) + captionHeight;
            for (int i = 0; i < c.SlotCount; i++)
            {
                float top = y + i / slotColumns * rowHeight;
                if (ordered) Text(parent, _text.Format("query.neow.results.group", i + 1), x + i % slotColumns * (slotWidth + 12), top, slotWidth, 16, true);
                OutputSlot(parent, source, c.ComponentId, i, _catalog!.Candidates(c.CandidatePool),
                    x + i % slotColumns * (slotWidth + 12), top + captionHeight, slotWidth);
            }
            y += (int)Math.Ceiling(c.SlotCount / (double)slotColumns) * rowHeight;
        }
        return y;
    }
    private void SwapKaleidoscopeTargets()
    {
        if (_draft.KaleidoscopeOrder != KaleidoscopeGroupOrderMode.ExactOrder) return;
        string prefix = $"{BaseGameModelKeys.Relics.Kaleidoscope}/independent-offers/";
        _slots.TryGetValue(prefix + "0", out var first); _slots.TryGetValue(prefix + "1", out var second);
        _slots.Remove(prefix + "0"); _slots.Remove(prefix + "1");
        if (second.HasValue) _slots[prefix + "0"] = second;
        if (first.HasValue) _slots[prefix + "1"] = first;
    }

    // Preserve the existing typed advanced condition shape; still no Search execution/persistence wiring.
    internal NeowStructuredEffectSearchCondition? AdvancedSourceCondition(ModelKey source)
    {
        if (_catalog is null || !_catalog.RouteRelics.Contains(source)) return null;
        if (source == BaseGameModelKeys.Relics.ScrollBoxes && _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw
            && _catalog.CharacterKey == BaseGameModelKeys.Characters.Defect)
            return new(source, NeowStructuredConditionKind.SpecialOffer, NeowStructuredEffectScope.SelectableOfferGroups,
                NeowStructuredOutputKind.Card, Array.Empty<ModelKey>(), NeowSpecialOfferKind.ScrollBoxesTripleClaw);
        if (source != BaseGameModelKeys.Relics.Kaleidoscope) return null;
        var values = Enumerable.Range(0, 2).Select(i => _slots.GetValueOrDefault($"{source}/independent-offers/{i}")).ToArray();
        var keys = values.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
        if (keys.Length == 0) return null;
        return new(source, NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups,
            NeowStructuredOutputKind.Card, keys, AllowDuplicateOutputs: true, KaleidoscopeGroupOrder: _draft.KaleidoscopeOrder)
        { KaleidoscopePositionalSlots = _draft.KaleidoscopeOrder == KaleidoscopeGroupOrderMode.ExactOrder ? values : Array.Empty<ModelKey?>() };
    }
    private float OutputSlot(Control p, ModelKey source, string component, int index, IReadOnlyList<ModelKey> pool, float x, float y, float width)
    {
        string id = $"{source}/{component}/{index}"; _slots.TryGetValue(id, out var chosen);
        var kind = NeowEffectCardRegistry.Get(source).Components.FirstOrDefault(c => c.ComponentId == component)?.OutputKind
            ?? NeowStructuredOutputKind.Card;
        if (kind is NeowStructuredOutputKind.Card or NeowStructuredOutputKind.Relic or NeowStructuredOutputKind.Potion)
        {
            ResultObject(p, chosen, kind, x, y,
                () => Pick(SlotCandidates(source, component, index, pool), k => { _slots[id] = k; RenderEditor(); }),
                () => { _slots.Remove(id); RenderEditor(); });
            return y + ResultObjectHeight + 12;
        }
        ObjectSlot(p, chosen, _text.Get("common.add_any"), x, y, Math.Min(width, 600),
            () => Pick(SlotCandidates(source, component, index, pool), k => { _slots[id] = k; RenderEditor(); }), () => { _slots.Remove(id); RenderEditor(); });
        return y + 68;
    }

    private static bool IsCapsule(ModelKey source) => source == BaseGameModelKeys.Relics.SmallCapsule || source == BaseGameModelKeys.Relics.LargeCapsule;
    private bool IsCapsuleSlot(string id) => id.StartsWith(BaseGameModelKeys.Relics.SmallCapsule + "/", StringComparison.Ordinal)
        || id.StartsWith(BaseGameModelKeys.Relics.LargeCapsule + "/", StringComparison.Ordinal);

    private IReadOnlyList<ModelKey> CapsuleCandidates(int index)
    {
        var occupied = _draft.CapsuleTargets.Where((k, i) => i != index && k.HasValue).Select(k => k!.Value).ToHashSet();
        return _catalog!.OrdinaryRelics.Where(k => !occupied.Contains(k)).ToArray();
    }

    private void NormalizeCapsuleTargets()
    {
        int removed = 0;
        var seen = new HashSet<ModelKey>();
        for (int i = 0; i < _draft.CapsuleTargets.Length; i++)
            if (_draft.CapsuleTargets[i] is { } key && !seen.Add(key)) { _draft.CapsuleTargets[i] = null; removed++; }
        seen.Clear();
        // Only combine histories when both capsule identities are actually required.
        foreach (var source in new[] { BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.SmallCapsule })
        {
            if (!HasCapsulePair) seen.Clear();
            foreach (string id in _slots.Keys.Where(id => id.StartsWith(source + "/", StringComparison.Ordinal)).OrderBy(id => id, StringComparer.Ordinal).ToArray())
                if (_slots[id] is { } key && !seen.Add(key)) { _slots.Remove(id); removed++; }
        }
        if (removed > 0) NotifyRemoved(removed);
    }

    private IReadOnlyList<ModelKey> SlotCandidates(ModelKey source, string component, int index, IReadOnlyList<ModelKey> pool)
    {
        if (IsCapsule(source))
        {
            string capsuleSlot = $"{source}/{component}/{index}";
            var occupiedCapsules = _slots.Where(s => s.Key != capsuleSlot && s.Value.HasValue &&
                (HasCapsulePair ? IsCapsuleSlot(s.Key) : s.Key.StartsWith(source + "/", StringComparison.Ordinal)))
                .Select(s => s.Value!.Value).ToHashSet();
            return pool.Where(k => !occupiedCapsules.Contains(k)).ToArray();
        }
        // Reuse the audited component's duplicate policy from the old effect-card donor.
        // ScrollBoxes normal bundle is separate from its SpecialOffer/TripleClaw branch.
        bool excludeSiblings = source == BaseGameModelKeys.Relics.ScrollBoxes && component == "bundle";
        var definition = NeowEffectCardRegistry.Get(source).Components.FirstOrDefault(c => c.ComponentId == component);
        excludeSiblings |= definition is { SlotCount: > 1, AllowDuplicateOutputs: false };
        if (!excludeSiblings) return pool;
        string prefix = $"{source}/{component}/", current = prefix + index;
        var occupied = _slots.Where(s => s.Key.StartsWith(prefix, StringComparison.Ordinal) && s.Key != current && s.Value.HasValue)
            .Select(s => s.Value!.Value).ToHashSet(ModelKeyComparer.Instance);
        if (source == BaseGameModelKeys.Relics.ScrollBoxes && component == "bundle")
        {
            int common = occupied.Count(k => _catalog!.CommonCharacterCards.Contains(k));
            int uncommon = occupied.Count(k => _catalog!.UncommonCharacterCards.Contains(k));
            return pool.Where(k => !occupied.Contains(k) &&
                ((_catalog!.CommonCharacterCards.Contains(k) && common < 2) ||
                 (_catalog.UncommonCharacterCards.Contains(k) && uncommon < 1))).ToArray();
        }
        return pool.Where(k => !occupied.Contains(k)).ToArray();
    }

    private float Results(Control content, float width)
    {
        float y = 0;
        const float slotWidth = 124, slotHeight = 76, gap = 12;
        int columns = Math.Max(1, (int)((width + gap) / (slotWidth + gap)));
        foreach (string kind in new[] { "CARD", "RELIC", "POTION" })
        {
            string title = kind switch { "CARD" => _text.Get("object.cards"), "RELIC" => _text.Get("object.relics"), _ => _text.Get("object.potions") };
            Text(content, title, 0, y, width, 20); y += 30;
            var selected = _targets.Where(t => t.Kind == kind).ToArray();
            for (int i = 0; i <= selected.Length; i++)
            {
                float x = i % columns * (slotWidth + gap), top = y + i / columns * (slotHeight + gap);
                if (i == selected.Length)
                {
                    var empty = Button(content, "+", x, top, slotWidth,
                        () => Pick(Pool(kind), key => { _targets.Add(new Target(kind, key)); RenderEditor(); }), height: slotHeight);
                    empty.AccessibilityName = _text.Format("picker.choose_kind", title);
                    continue;
                }
                var target = selected[i];
                var slot = Button(content, "", x, top, slotWidth,
                    () => Pick(Pool(kind), key => { target.Key = key; RenderEditor(); }), height: slotHeight);
                Picture(slot, target.Key, 12, 2, 46);
                Text(slot, NameOf(target.Key), 8, 48, slotWidth - 16, 16);
                slot.AccessibilityName = NameOf(target.Key);
                Button(content, "×", x + slotWidth - 36, top + 2, 32,
                    () => { _targets.Remove(target); RenderEditor(); }, height: 30);
            }
            y += (selected.Length / columns + 1) * (slotHeight + gap) + 8;
        }

        // Deliberately a UI illustration, never an Exact/Query feasibility assertion.
        var preview = PreviewSources();
        Text(content, _text.Format("query.neow.result_only.possible_sources", preview.Count), 0, y, 300, 21);
        Text(content, _text.Get("query.neow.result_only.disclaimer"), 320, y + 2, width - 320, 16, true);
        y += 40;
        if (_bans.Count > 0)
        {
            Text(content, _text.Format("query.neow.result_only.excluded_sources", _bans.Count), 0, y + 7, 240, 17, true);
            Button(content, _text.Get("query.neow.result_only.reset_restrictions"), 254, y, 204,
                () => { _bans.Clear(); RenderEditor(); });
            y += 52;
        }
        var sources = Sources.Where(preview.Contains).ToArray();
        const int sourceColumns = 10;
        float stride = width / sourceColumns;
        for (int i = 0; i < sources.Length; i++)
        {
            var key = sources[i];
            var b = Button(content, "", i % sourceColumns * stride, y + i / sourceColumns * 64, stride - 10,
                () => { _bans.Add(key); RenderEditor(); }, height: 54);
            Picture(b, key, (stride - 52) / 2, 5, 42);
            b.AccessibilityName = _text.Format("query.neow.result_only.exclude_accessibility", NameOf(key));
            b.TooltipText = _text.Format("query.neow.result_only.exclude_tooltip", NameOf(key));
        }
        if (sources.Length == 0)
        { Text(content, _text.Get("query.neow.result_only.no_sources"), 0, y, width, 17, true); y += 36; }
        return y + (int)Math.Ceiling(sources.Length / (double)sourceColumns) * 64;
    }

    // Capacity/pool sketch for visual contraction only. No seed, RNG, offer correlation,
    // sampling uniqueness, hooks or multiplayer continuation is modeled here.
    // A source may participate as either the direct entry or a Bones sibling.
    private HashSet<ModelKey> PreviewSources()
    {
        var allowed = Sources.Where(k => _catalog!.RouteRelics.Contains(k) && !_bans.Contains(k)).ToArray();
        if (_targets.Count == 0) return allowed.ToHashSet();
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var ordinary = allowed.Where(k => k != bones).ToArray();
        var capacities = ordinary.ToDictionary(k => k, k =>
        {
            if (k == BaseGameModelKeys.Relics.MassiveScroll) return new IReadOnlyList<ModelKey>[] { _catalog!.MassiveScrollCards };
            if (k == BaseGameModelKeys.Relics.ScrollBoxes)
                return new IReadOnlyList<ModelKey>[] { _catalog!.CommonCharacterCards, _catalog.CommonCharacterCards, _catalog.UncommonCharacterCards };
            return NeowEffectCardRegistry.Get(k).Components.SelectMany(c =>
                Enumerable.Repeat(_catalog!.Candidates(c.CandidatePool), c.SlotCount)).ToArray();
        });
        bool Fits(IReadOnlyList<ModelKey>[] slots)
        {
            if (_targets.Count > slots.Length) return false;
            var used = new bool[slots.Length];
            bool Assign(int target)
            {
                if (target == _targets.Count) return true;
                for (int i = 0; i < slots.Length; i++)
                    if (!used[i] && slots[i].Contains(_targets[target].Key))
                    {
                        used[i] = true;
                        if (Assign(target + 1)) return true;
                        used[i] = false;
                    }
                return false;
            }
            return Assign(0);
        }
        var visible = ordinary.Where(k => Fits(capacities[k])).ToHashSet();
        if (allowed.Contains(bones))
            for (int i = 0; i < ordinary.Length; i++)
                for (int j = i + 1; j < ordinary.Length; j++)
                    if (Fits(capacities[ordinary[i]].Concat(capacities[ordinary[j]]).ToArray()))
                    { visible.Add(bones); visible.Add(ordinary[i]); visible.Add(ordinary[j]); }
        return visible;
    }
    private IReadOnlyList<ModelKey> Pool(string kind)
    {
        // The result-only prototype browses the union of direct outputs of currently
        // eligible N sources, not a global object encyclopedia or a feasibility solver.
        var keys = new HashSet<ModelKey>();
        foreach (var source in _catalog!.RouteRelics)
        {
            foreach (var component in NeowEffectCardRegistry.Get(source).Components)
                keys.UnionWith(_catalog.Candidates(component.CandidatePool).Where(k => k.Category == kind));
            if (kind == "CARD" && source == BaseGameModelKeys.Relics.ScrollBoxes)
            { keys.UnionWith(_catalog.CommonCharacterCards); keys.UnionWith(_catalog.UncommonCharacterCards); }
            if (kind == "CARD" && source == BaseGameModelKeys.Relics.MassiveScroll) keys.UnionWith(_catalog.MassiveScrollCards);
        }
        return keys.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray();
    }

    internal void PickExternalCards(IReadOnlyList<ModelKey> pool, CardPickerContext context,
        IReadOnlySet<ModelKey> multiplayerOnlyCards, int players, Action<ModelKey> selected)
        => Pick(pool, selected, context, multiplayerOnlyCards, players);

    internal void PickExternalObjects(IReadOnlyList<ModelKey> pool, Action<ModelKey> selected)
        => Pick(pool, selected);

    private void Pick(IReadOnlyList<ModelKey> pool, Action<ModelKey> selected,
        CardPickerContext? externalCardContext = null,
        IReadOnlySet<ModelKey>? externalMultiplayerOnlyCards = null,
        int? externalPlayers = null)
    {
        _blocked.Clear();
        foreach (var node in _overlay.GetParent().FindChildren("*", "BaseButton", true, false))
            if (node is BaseButton button && !_overlay.IsAncestorOf(button))
            { _blocked[button] = button.Disabled; button.Disabled = true; }
        Clear(_overlay); _overlay.Show(); _overlay.MoveToFront(); ModalChanged?.Invoke(true);
        var shade = new ColorRect { Color = new Color(0, 0, 0, .68f), MouseFilter = MouseFilterEnum.Stop };
        _overlay.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var modal = new Panel { Position = new(160, 96), Size = new(1280, 708) };
        modal.AddThemeStyleboxOverride("panel", _p.Box(_p.Canvas, _p.Line, 1)); _overlay.AddChild(modal);
        Text(modal, _text.Get("picker.title"), 24, 22, 220, 24);
        Button(modal, "×", 1208, 12, 48, () => ClosePicker());
        bool cards = externalCardContext is not null || pool.Count > 0 && pool.All(key => key.Category == "CARD");
        var cardContext = cards ? externalCardContext ?? _catalog!.CreateCardPickerContext(pool, true, "neow-prototype") : null;
        var filters = new CardPickerFilterState();
        var colorlessCards = pool.Where(key => cardContext?.IsColorlessPoolMember(key) == true)
            .ToHashSet(ModelKeyComparer.Instance);
        var curseCards = pool.Where(key => _catalog!.Curses.Contains(key, ModelKeyComparer.Instance) ||
            cardContext is not null && cardContext.TryGet(key, out CardPickerCandidateMetadata metadata) && metadata.Rarity == EffectCardRarity.Curse)
            .ToHashSet(ModelKeyComparer.Instance);
        var ancientCards = pool.Where(key => cardContext is not null && cardContext.TryGet(key, out CardPickerCandidateMetadata metadata) &&
            metadata.Rarity == EffectCardRarity.Ancient).ToHashSet(ModelKeyComparer.Instance);
        var sourceSets = new Dictionary<string, HashSet<ModelKey>>(StringComparer.Ordinal);
        if (cardContext is not null)
            foreach (ModelKey character in cardContext.AvailableCharacterKeys)
                sourceSets["character:" + character.Serialized] = pool.Where(key =>
                    cardContext.TryGet(key, out CardPickerCandidateMetadata metadata) && metadata.CharacterKeys.Contains(character, ModelKeyComparer.Instance))
                    .ToHashSet(ModelKeyComparer.Instance);
        if (colorlessCards.Count > 0) sourceSets["colorless"] = colorlessCards;
        if (curseCards.Count > 0) sourceSets["curse"] = curseCards;
        if (ancientCards.Count > 0) sourceSets["ancient"] = ancientCards;
        if (cards)
        {
            ModelKey[] classified = sourceSets.Values.SelectMany(keys => keys).Distinct(ModelKeyComparer.Instance).ToArray();
            HashSet<ModelKey> other = pool.Where(key => !classified.Contains(key, ModelKeyComparer.Instance)).ToHashSet(ModelKeyComparer.Instance);
            if (other.Count > 0) sourceSets["other"] = other;
        }
        IReadOnlySet<ModelKey> multiplayerOnlyCards = externalMultiplayerOnlyCards ?? _catalog!.MultiplayerOnlyCards;
        int pickerPlayers = externalPlayers ?? _players;
        string? selectedSource = null;
        bool includeMultiplayer = pickerPlayers > 1;
        string? selectedRarity = null;
        string ObjectRarity(ModelKey key)
        {
            try
            {
                if (key.Category == "RELIC") return ModelDb.GetById<RelicModel>(new ModelId(key.Category, key.Entry)).Rarity.ToString();
                if (key.Category == "POTION") return ModelDb.GetById<PotionModel>(new ModelId(key.Category, key.Entry)).Rarity.ToString();
            }
            catch { }
            return key.Category == "RELIC" ? _catalog!.RelicRarity(key).ToString() : _catalog!.PotionRarity(key).ToString();
        }
        string RarityName(string rarity) => rarity switch
        { "Common" => _text.Get("rarity.common"), "Uncommon" => _text.Get("rarity.uncommon"), "Rare" => _text.Get("rarity.rare"),
          "Ancient" => _text.Get("rarity.ancient"), "Event" => _text.Get("rarity.event"),
          "Shop" => _text.Get("rarity.shop"), _ => _text.Get("common.other") };
        const float filterRailWidth = 220, filterRailGap = 24;
        float browserLeft = 24 + filterRailWidth + filterRailGap;
        float browserWidth = 1256 - browserLeft;
        var search = new LineEdit { Position = new(browserLeft, 14), Size = new(browserWidth - 64, 44), PlaceholderText = _text.Get("picker.search_name") };
        search.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface)); search.AddThemeStyleboxOverride("focus", _p.FocusRing()); modal.AddChild(search);
        float gridTop = 76;
        const int cardLeftOverhang = 16;
        int leftInset = cards ? cardLeftOverhang : 0;
        var scroll = new ScrollContainer { Position = new(browserLeft - leftInset, gridTop), Size = new(browserWidth + leftInset, 676 - gridTop), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; modal.AddChild(scroll);
        // Keep frame centers/grid density unchanged; give protruding star costs room
        // inside the scroll clip, rather than offsetting the native card itself.
        var gridInset = new MarginContainer();
        gridInset.AddThemeConstantOverride("margin_left", leftInset);
        scroll.AddChild(gridInset);
        var grid = new GridContainer { Columns = cards ? 5 : 6 }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 12); gridInset.AddChild(grid);
        var nativeTiles = new List<(Button Tile, ModelKey Key)>();
        var nativeViews = new Dictionary<Button, Control>();
        void RefreshNativeCards()
        {
            if (!cards || !GodotObject.IsInstanceValid(scroll) || !scroll.IsInsideTree() || !_overlay.Visible) return;
            var viewport = scroll.GetGlobalRect().Grow(80);
            foreach (var (tile, key) in nativeTiles)
            {
                bool visible = tile.GetGlobalRect().Intersects(viewport);
                if (visible && !nativeViews.ContainsKey(tile)) nativeViews[tile] = AddNativePickerCard(tile, key);
                else if (!visible && nativeViews.Remove(tile, out var view)) { tile.RemoveChild(view); view.QueueFree(); }
            }
        }
        scroll.GetVScrollBar().ValueChanged += _ => RefreshNativeCards();
        void Filter(string text)
        {
            nativeTiles.Clear(); nativeViews.Clear();
            Clear(grid);
            grid.Columns = cards ? 5 : 6;
            foreach (var key in pool.Distinct().Where(k => NameOf(k).Contains(text, StringComparison.OrdinalIgnoreCase))
                         .Where(k => cardContext is null || filters.Matches(cardContext, k))
                         .Where(k => selectedSource is null || sourceSets[selectedSource].Contains(k))
                         .Where(k => !cards || includeMultiplayer || !multiplayerOnlyCards.Contains(k))
                         .Where(k => selectedRarity is null || ObjectRarity(k) == selectedRarity))
            {
                float tileWidth = cards ? FullCardTileWidth : 146;
                var b = _p.Button(""); b.CustomMinimumSize = new(tileWidth, cards ? FullCardTileHeight : 140); grid.AddChild(b);
                if (cards)
                {
                    b.AccessibilityName = NameOf(key);
                    b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                    b.AddThemeStyleboxOverride("hover", _p.Box(_p.Hover));
                    nativeTiles.Add((b, key));
                }
                else
                {
                    string objectName = NameOf(key);
                    var label = Text(b, objectName, 6, 6, tileWidth - 12, 16);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                    Picture(b, key, 28, 36, 90);
                }
                b.Pressed += () =>
                {
                    if (!pool.Contains(key) || (cardContext is not null && !cardContext.IsAllowed(key))) return;
                    ClosePicker(); selected(key);
                };
            }
            Callable.From(RefreshNativeCards).CallDeferred();
        }
        {
            var railScroll = new ScrollContainer { Position = new(24, 76), Size = new(filterRailWidth, cards ? 550 : 600), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            modal.AddChild(railScroll);
            var rail = new VBoxContainer { CustomMinimumSize = new(filterRailWidth - 16, 0) };
            rail.AddThemeConstantOverride("separation", 4); railScroll.AddChild(rail);
            var characterIcons = new NativeCharacterPoolIconProvider(_icons);
            var filterIcons = new CardPickerFilterIconProvider();
            var selectedStates = new List<(Button Button, Func<bool> Selected)>();
            void StyleChoice(Button button, bool active)
            {
                foreach (var (state, fill) in new[] { ("normal", _p.Canvas), ("hover", _p.Hover), ("pressed", _p.Line), ("disabled", _p.Canvas) })
                {
                    var box = _p.Box(fill);
                    box.BorderColor = _p.Color(_p.Selected);
                    box.BorderWidthLeft = active ? 3 : 0;
                    button.AddThemeStyleboxOverride(state, box);
                }
            }
            void RefreshFilters()
            {
                foreach (var item in selectedStates) StyleChoice(item.Button, item.Selected());
                Filter(search.Text);
            }
            void Heading(string title)
            {
                if (rail.GetChildCount() > 0) rail.AddChild(new Control { CustomMinimumSize = new(0, 12) });
                rail.AddChild(_p.Label(title, 18, true));
            }
            void Choice(string title, Texture2D? texture, Action toggle, Func<bool> active, bool disabled = false)
            {
                var button = _p.Button("", selected: active());
                button.CustomMinimumSize = new(filterRailWidth - 16, 32); button.Disabled = disabled;
                StyleChoice(button, active());
                rail.AddChild(button);
                if (texture is not null)
                {
                    // Scale explicitly: assigning TextureRect.Size before its expand mode
                    // let the texture's native minimum size enlarge the rail icons.
                    var dimensions = texture.GetSize();
                    button.AddChild(new Sprite2D { Texture = texture, Position = new(18, 16),
                        Scale = Vector2.One * (24f / Math.Max(1f, Math.Max(dimensions.X, dimensions.Y))) });
                }
                Text(button, title, texture is null ? 10 : 40, 2, texture is null ? 182 : 152, 16);
                selectedStates.Add((button, active));
                button.Pressed += () => { toggle(); RefreshFilters(); };
            }
            if (cardContext is not null)
            {
            Heading(_text.Get("picker.card.filter.pool"));
            var sources = new List<(string Id, string Title, Texture2D? Texture)>();
            foreach (ModelKey character in cardContext.AvailableCharacterKeys)
            {
                string id = "character:" + character.Serialized;
                if (sourceSets.ContainsKey(id))
                    sources.Add((id, _names.Resolve(character, GameContentKind.Character), characterIcons.Resolve(character).Texture));
            }
            if (sourceSets.ContainsKey("colorless"))
                sources.Add(("colorless", _text.Get("picker.card.filter.colorless"), characterIcons.ResolveColorlessPoolVisual()));
            if (sourceSets.ContainsKey("curse"))
                sources.Add(("curse", _text.Get("picker.card.filter.curse"), null));
            if (sourceSets.ContainsKey("ancient"))
                sources.Add(("ancient", _text.Get("picker.card.filter.ancient"), null));
            if (sourceSets.ContainsKey("other"))
                sources.Add(("other", _text.Get("common.other"), null));
            if (sources.Count > 1)
                Choice(_text.Get("common.all"), null, () => { selectedSource = null; filters.SelectAllCharacters(); }, () => selectedSource is null);
            foreach (var source in sources)
                Choice(source.Title, source.Texture,
                    () => { selectedSource = selectedSource == source.Id ? null : source.Id; filters.SelectAllCharacters(); },
                    () => sources.Count == 1 || selectedSource == source.Id,
                    sources.Count == 1);
            Heading(_text.Get("picker.card.filter.rarity"));
            foreach (var rarity in cardContext.AvailableRarities)
                Choice(rarity switch { EffectCardRarity.Common => _text.Get("rarity.common"), EffectCardRarity.Uncommon => _text.Get("rarity.uncommon"), _ => _text.Get("rarity.rare") },
                    filterIcons.ResolveRarity(rarity), () => filters.ToggleRarity(rarity),
                    () => filters.Rarities.Contains(rarity) || cardContext.AvailableRarities.Count == 1,
                    cardContext.AvailableRarities.Count <= 1);
            Heading(_text.Get("picker.card.filter.type"));
            foreach (var type in cardContext.AvailableTypes)
                Choice(type switch { EffectCardType.Attack => _text.Get("card.type.attack"), EffectCardType.Skill => _text.Get("card.type.skill"), _ => _text.Get("card.type.power") },
                    filterIcons.ResolveType(type), () => filters.ToggleType(type),
                    () => filters.Types.Contains(type) || cardContext.AvailableTypes.Count == 1,
                    cardContext.AvailableTypes.Count <= 1);
            if (pickerPlayers > 1)
            {
            var multiplayer = _p.Button("");
            multiplayer.ToggleMode = true;
            multiplayer.Position = new(24, 640); multiplayer.Size = new(filterRailWidth, 36);
            multiplayer.CustomMinimumSize = new(0, 36);
            multiplayer.ButtonPressed = includeMultiplayer;
            multiplayer.Disabled = !pool.Any(multiplayerOnlyCards.Contains);
            multiplayer.AccessibilityName = _text.Get("picker.card.filter.multiplayer_only");
            multiplayer.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            multiplayer.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            modal.AddChild(multiplayer);
            var box = new Panel { Position = new(6, 8), Size = new(20, 20), MouseFilter = MouseFilterEnum.Ignore };
            var border = _p.Box(_p.Canvas, _p.Text, 2);
            border.CornerRadiusTopLeft = border.CornerRadiusTopRight = border.CornerRadiusBottomLeft = border.CornerRadiusBottomRight = 3;
            box.AddThemeStyleboxOverride("panel", border);
            multiplayer.AddChild(box);
            var mark = Text(box, "✓", 0, -4, 20, 19);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            var caption = Text(multiplayer, multiplayer.AccessibilityName, 36, 4, filterRailWidth - 36, 16);
            void UpdateCheck(bool enabled)
            {
                mark.Visible = enabled;
                box.Modulate = multiplayer.Disabled ? new Color(1, 1, 1, .45f) : Colors.White;
                caption.AddThemeColorOverride("font_color", _p.Color(multiplayer.Disabled ? _p.Disabled : enabled ? _p.Selected : _p.Text));
            }
            UpdateCheck(includeMultiplayer);
            multiplayer.Toggled += enabled => { includeMultiplayer = enabled; UpdateCheck(enabled); Filter(search.Text); };
            }
            }
            else
            {
                Heading(_text.Get("picker.object.filter.rarity"));
                Choice(_text.Get("common.all"), null, () => selectedRarity = null, () => selectedRarity is null);
                HashSet<string> presentRarities = pool.Select(ObjectRarity).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string rarity in new[] { "Common", "Uncommon", "Rare", "Ancient", "Event" }.Where(presentRarities.Contains))
                    Choice(RarityName(rarity), null, () => selectedRarity = selectedRarity == rarity ? null : rarity,
                        () => selectedRarity == rarity);
            }
        }
        search.TextChanged += Filter; Filter(""); search.GrabFocus();
    }
    public bool ClosePicker()
    {
        if (!_overlay.Visible) return false;
        _overlay.Hide();
        foreach (var (button, disabled) in _blocked)
            if (GodotObject.IsInstanceValid(button)) button.Disabled = disabled;
        _blocked.Clear(); ModalChanged?.Invoke(false); return true;
    }
    private Button Button(Control parent, string text, float x, float y, float width, Action click, bool selected = false, float height = 40)
    {
        var b = _p.CompactButton(text, height, height < 32 ? 16 : 18, selected);
        b.Position = new(x, y); b.Size = new(width, height);
        parent.AddChild(b); b.Pressed += click; return b;
    }
    private Label Text(Control p, string text, float x, float y, float width, int size = 20, bool secondary = false)
    { var l = _p.Label(text, size, secondary); l.Position = new(x, y); l.Size = new(width, 28); l.ClipText = true; p.AddChild(l); return l; }
    private void ObjectSlot(Control p, ModelKey? key, string empty, float x, float y, float width, Action pick, Action clear)
    {
        var b = Button(p, key is null ? empty : "", x, y, width - (key.HasValue ? 48 : 0), pick, height: key.HasValue ? 56 : 40);
        if (key is not { } k) return;
        Picture(b, k, 8, 4, 48); Text(b, NameOf(k), 64, 14, width - 120, 18);
        Button(p, "×", x + width - 40, y + 8, 36, clear);
    }
    private OptionButton Options(Control p, string[] values, int selected, float x, float y, float width, Action<int> change)
    {
        var o = new OptionButton { Position = new(x, y), Size = new(width, 40), FitToLongestItem = false, ClipText = true };
        foreach (string value in values) o.AddItem(value); o.Select(selected);
        o.AddThemeFontSizeOverride("font_size", 17); o.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        o.AddThemeStyleboxOverride("normal", _p.Box(_p.Surface)); o.AddThemeStyleboxOverride("hover", _p.Box(_p.Hover));
        o.AddThemeStyleboxOverride("pressed", _p.Box(_p.Line)); o.AddThemeStyleboxOverride("focus", _p.FocusRing());
        p.AddChild(o); o.ItemSelected += i => change((int)i); return o;
    }
    private void Picture(Control p, ModelKey key, float x, float y, float size)
    {
        GameContentKind kind = Kind(key);
        var texture = _icons.Resolve(key, kind, kind == GameContentKind.Card ? IconVariant.CardPickerLarge : IconVariant.RelicLarge).Texture;
        if (texture is null) return;
        Vector2 dimensions = texture.GetSize();
        p.AddChild(new Sprite2D { Texture = texture, Position = new(x + size / 2, y + size / 2),
            Scale = Vector2.One * (size / Math.Max(1f, Math.Max(dimensions.X, dimensions.Y))) });
    }
}

