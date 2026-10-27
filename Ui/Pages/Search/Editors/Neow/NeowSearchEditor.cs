using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
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

namespace RolltheSpire2.Ui.Pages.Search.Editors;

// Editor-local interaction state; the Query partial exports existing canonical conditions.
// The hidden Result-only experiment is not part of the Production authoring path.
internal sealed partial class NeowSearchEditor : Control
{
    private readonly SearchEditorLayout _presentation = new();
    private readonly ModRuntimeSnapshot _runtime;
    private readonly WorkspacePalette _p = WorkspacePalette.MainPanel;
    private readonly IGameIconResolver _icons = new ReflectionGameIconResolver("neow-prototype");
    private readonly Control _body = new();
    private readonly ObjectPickerPanel _picker;
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
        public bool TransformSeparateInitialized = true, TransformJointInitialized;
        public bool CapsuleSeparateInitialized = true, CapsuleJointInitialized;
        public readonly ModelKey?[] TransformTargets = new ModelKey?[3], CapsuleTargets = new ModelKey?[3];
        public ModelKey? Source, Curse;
        public readonly List<ModelKey> Bones = [];
        public readonly Dictionary<string, ModelKey?> Slots = [];
        public readonly List<Target> Targets = [];
        public readonly HashSet<ModelKey> Bans = [];
        public int Count => (Source.HasValue ? 1 : 0) + Bones.Count + Slots.Count(s =>
                (!JointTransforms || !IsTransformSlot(s.Key)) && (!JointCapsules ||
                !(s.Key.StartsWith(BaseGameModelKeys.Relics.SmallCapsule + "/", StringComparison.Ordinal) ||
                  s.Key.StartsWith(BaseGameModelKeys.Relics.LargeCapsule + "/", StringComparison.Ordinal)))) + Targets.Count + (Curse.HasValue ? 1 : 0)
            + (ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw ? 1 : 0)
            + (JointTransforms ? TransformTargets.Count(k => k.HasValue) : 0)
            + (JointCapsules ? CapsuleTargets.Count(k => k.HasValue) : 0);
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
    private sealed class Target(string kind, ModelKey key)
    {
        public string Kind = kind;
        public ModelKey Key = key;
    }
    public event Action? OpeningChanged;
    internal event Action? OpeningSourceEdited;
    internal Func<int, string, bool>? TransformTakenOver { get; set; }
    internal IReadOnlyList<TransformationSearchSource> TransformationSources(int seat)
    {
        if (!_drafts.TryGetValue(seat, out var draft)) return [];
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
    // T composes its own request from source-owned captured presentation metadata.
    internal bool TryTransformationPickerMetadata(int seat,
        out IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> metadata,
        out IReadOnlySet<ModelKey> multiplayerOnlyCards)
    {
        metadata = new Dictionary<ModelKey, CardPickerCandidateMetadata>(ModelKeyComparer.Instance);
        multiplayerOnlyCards = new HashSet<ModelKey>(ModelKeyComparer.Instance);
        if (!_drafts.TryGetValue(seat, out var draft) || draft.Catalog is null) return false;
        metadata = draft.Catalog.CardPickerMetadata;
        multiplayerOnlyCards = draft.Catalog.MultiplayerOnlyCards;
        return true;
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
        else foreach (var root in roots.Where(IsCapsule).Where(_ => source != BaseGameModelKeys.Relics.NeowsBones || !d.JointCapsules))
            result.AddRange(d.Slots.Where(s => s.Key.StartsWith(root + "/", StringComparison.Ordinal) && s.Value.HasValue).Select(s => s.Value!.Value));
        return result.Distinct().ToArray();

    }
    public event Action? GuideRequested;
    private bool _guideExpanded; // Session UI preference, independent of seat/query state.
    private string NameOf(ModelKey k) => _names.Resolve(k, Kind(k));
    private static GameContentKind Kind(ModelKey k) => k.Category switch
    { "CARD" => GameContentKind.Card, "POTION" => GameContentKind.Potion, _ => GameContentKind.Relic };
    private static ModelKey Relic(string id) => new("RELIC", id);

    public NeowSearchEditor(ModRuntimeSnapshot runtime, ObjectPickerPanel picker)
    { _runtime = runtime; _picker = picker; AddChild(_body); }

    public void Refresh(string language, ModelKey character, int ascension, int players, int seat,
        MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState? unlocks = null, bool render = true, EditorRefreshContext? sharedContext = null)
    {
        _picker.ClosePicker(); _language = language; _text = JsonUiTextProvider.CreateUi13(language); _names = RuntimeGameContentNameResolver.Create(language);
        bool activeContextChanged = _players != players || _activeSeat != seat;
        _players = players;
        _activeSeat = seat;
        var drafts = players > 1 ? _partyCatalogDrafts : _drafts;
        foreach (int removedSeat in drafts.Keys.Where(s => s >= players).ToArray())
        { NotifyRemoved(drafts[removedSeat].Count); drafts.Remove(removedSeat); }
        if (!drafts.TryGetValue(seat, out var draft)) drafts[seat] = draft = new SeatDraft();
        _draft = draft;
        if (_identities.Count == 0) _identities = NeowSearchUiCatalog.AllRuntimeIdentities();
        string unlockKey = sharedContext?.Key ?? (players == 1 ? System.Text.Json.JsonSerializer.Serialize(MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable())
            : unlocks is null ? "unread" : System.Text.Json.JsonSerializer.Serialize(unlocks));
        string context = $"{character}/{ascension}/{players}/{seat}/{unlockKey}";
        bool contextChanged = context != _draft.Context;
        if (contextChanged)
        {
            _draft.Context = context; _problem = "";
            try
            {
                if (players > 1 && unlocks is null)
                {
                    _catalog = null;
                    _problem = _text.Get("query.neow.context.read_unlocks_required");
                }
                else
                {
                    var resolved = sharedContext is not null ? MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(sharedContext.Unlocks) : players == 1 ? MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress()
                        : MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(unlocks!);
                    _catalog = NeowSearchUiCatalog.CaptureForPicker(_runtime, character, ascension, players, seat, resolved, sharedContext?.Authority);
                }
            }
            catch (Exception e) { _catalog = null; _problem = _text.Get("query.neow.context.catalog_unavailable"); Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(e, "EditorCatalogCaptureFailed", "NeowEditorPrototype", "PickerCatalog", "issues.capture_failed", Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning); }
        }
        if (activeContextChanged || contextChanged) OpeningChanged?.Invoke();
        if (render) RenderEditor();
    }
    // Layout/guide redraws are pure presentation. Only authoring edits and
    // changed captured context notify C/T dependents.
    private void RenderAfterEdit()
    {
        OpeningChanged?.Invoke();
        RenderEditor();
    }
    private IReadOnlyList<ModelKey> Sources => _identities.Concat(_source.HasValue ? [_source.Value] : [])
        .Concat(_bones).Distinct(ModelKeyComparer.Instance).ToArray();
    private bool IdentityAllowed(ModelKey key) => _catalog is not null && (_source == BaseGameModelKeys.Relics.NeowsBones && key != BaseGameModelKeys.Relics.NeowsBones
        ? _catalog.BonesNeowRelics.Contains(key) : _catalog.RouteRelics.Contains(key));
    private bool HasBonesPair(ModelKey first, ModelKey second) => _source == BaseGameModelKeys.Relics.NeowsBones
        && _bones.Count == 2 && _bones.Contains(first) && _bones.Contains(second);
    private bool HasTransformPair => HasBonesPair(BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf);
    private bool HasCapsulePair => HasBonesPair(BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule);
    private IReadOnlyList<ModelKey> JointTransformPool => _catalog!.TransformCards.Concat(_catalog.NewLeafTransformCards).Distinct().ToArray();
    private void SetTransformAllocation(bool joint)
    {
        if ((joint && !HasTransformPair) || joint == _draft.JointTransforms || (joint && _players != 1)) return;
        if (TransferAllocation(joint, transforms: true)) _draft.JointTransforms = joint;
    }

    // UI-to-existing-semantic seam only; the workbench Search action remains disconnected.
    internal SearchQuery BuildBonesQuery()
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones) return SearchQuery.Empty;
        var effects = new List<NeowStructuredEffectSearchCondition>();
        bool managedTransforms = TManagesTransform();
        bool jointTransforms = HasTransformPair && _draft.JointTransforms && !managedTransforms;
        bool jointCapsules = HasCapsulePair && _draft.JointCapsules;
        foreach (var child in _bones)
        {
            if ((_draft.JointTransforms || managedTransforms) &&
                (child == BaseGameModelKeys.Relics.LeafyPoultice || child == BaseGameModelKeys.Relics.NewLeaf) ||
                _draft.JointCapsules && IsCapsule(child)) continue;
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
                !_ordered || _bones.Count != 2 ? TransformationPickupOrder.Any : _bones[0] == BaseGameModelKeys.Relics.LeafyPoultice
                    ? TransformationPickupOrder.LeafyThenNewLeaf : TransformationPickupOrder.NewLeafThenLeafy,
                false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0, targetsT) : null;
        return SearchQuery.Empty with
        {
            OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, _bones.ToArray(),
                _ordered && _bones.Count == 2 ? BonesRouteOrderMode.ExactOrder : BonesRouteOrderMode.AnyOrder),
            StructuredOpeningEffects = effects.ToArray(), TransformationAggregate = aggregate
        };
    }

    private void NotifyRemoved(int count)
    { if (count <= 0) return; _removed += count; _noticeSeconds = 5; }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _noticeSeconds <= 0) return;
        _noticeSeconds -= delta;
        if (_noticeSeconds <= 0) { _removed = 0; _allocationNotice = ""; if (GodotObject.IsInstanceValid(_notice)) _notice!.Hide(); }
    }
    private static void Clear(Node node)
    { foreach (var c in node.GetChildren()) { node.RemoveChild(c); c.QueueFree(); } }
    // Click transitions ported verbatim in behavior from NeowRouteSelectionPanel:
    // ToggleBonesMode / OnRelicPressed / SetBonesOrderMode / SwapBonesOrder.
    // These change session-local UI state only, not canonical Query semantics.
    private void ToggleBonesMode()
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones && !IdentityAllowed(BaseGameModelKeys.Relics.NeowsBones)) return;
        _source = _source == BaseGameModelKeys.Relics.NeowsBones ? null : BaseGameModelKeys.Relics.NeowsBones;
        // Enter/exit clears the normal route; retain the internal list and its order mode.
        OpeningSourceEdited?.Invoke();
        RenderAfterEdit();
    }

    private void OnSourceRelicPressed(ModelKey key)
    {
        if (_source != key && !_bones.Contains(key) && !IdentityAllowed(key)) return;
        if (_players > 1) _partyChoices[_activeSeat] = [];
        if (_source == BaseGameModelKeys.Relics.NeowsBones)
        {
            int index = _bones.FindIndex(existing => existing == key);
            if (index >= 0)
            {
                _bones.RemoveAt(index);
            }
            else if (_bones.Count < 2) _bones.Add(key);
        }
        else _source = _source == key ? null : key;
        // Source edits suspend dependent modes; their original targets and order
        // remain available when the same sources are restored.
        OpeningSourceEdited?.Invoke();
        RenderAfterEdit();
    }

    private void SetBonesOrder(bool ordered)
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones || _ordered == ordered) return;
        if (ordered && _bones.Count != 2) return;
        _ordered = ordered;
        RenderAfterEdit();
    }

    private void SwapBonesOrder()
    {
        if (_source != BaseGameModelKeys.Relics.NeowsBones || !_ordered || _bones.Count != 2) return;
        (_bones[0], _bones[1]) = (_bones[1], _bones[0]);
        RenderAfterEdit();
    }

    private IReadOnlyList<ModelKey> ScrollBundlePool => _catalog!.CommonCharacterCards.Concat(_catalog.UncommonCharacterCards).ToArray();

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
        if (source == BaseGameModelKeys.Relics.ScrollBoxes && _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw)
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
    private static bool IsCapsule(ModelKey source) => source == BaseGameModelKeys.Relics.SmallCapsule || source == BaseGameModelKeys.Relics.LargeCapsule;
    private bool IsCapsuleSlot(string id) => id.StartsWith(BaseGameModelKeys.Relics.SmallCapsule + "/", StringComparison.Ordinal)
        || id.StartsWith(BaseGameModelKeys.Relics.LargeCapsule + "/", StringComparison.Ordinal);

    private IReadOnlyList<ModelKey> CapsuleCandidates(int index)
    {
        var occupied = _draft.CapsuleTargets.Where((k, i) => i != index && k.HasValue).Select(k => k!.Value).ToHashSet();
        return _catalog!.OrdinaryRelics.Where(k => !occupied.Contains(k)).ToArray();
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

}
