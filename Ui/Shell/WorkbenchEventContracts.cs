using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EventEditorPrototype
{
    internal SearchQuery ExportSharedPartyQuery(SearchQuery query) => query with
    {
        EventSequenceConstraints = _partyQueue.Select(c => new EventSequenceSearchCondition(c.Act, null,
            c.Exact ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN, c.Position,
            c.Excluded ? new ModelKeySetFilter([], [], [c.Event]) : new ModelKeySetFilter([c.Event], [], []))).ToArray()
    };

    internal void ImportSharedPartyQuery(SearchQuery query)
    {
        _partyQueue.Clear();
        // Migrate both old per-player drafts and new table-level drafts without dropping a predicate.
        foreach (var c in new[] { query }.Concat(query.Players.Select(p => p.Conditions)).SelectMany(q => q.EventSequenceConstraints))
        {
            foreach (var key in c.Keys.Any.Concat(c.Keys.All)) Add(key, false);
            foreach (var key in c.Keys.Ban) Add(key, true);
            void Add(ModelKey key, bool excluded)
            {
                var row = new QueueCondition(c.Act, c.RangeValue, c.RangeMode == SearchSequenceRangeMode.ExactSlot, excluded, key);
                if (!_partyQueue.Contains(row)) _partyQueue.Add(row);
            }
        }
    }

    internal SearchQuery ExportPartyQuery(int slot, SearchQuery q)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return q;
        var active = _draft;
        try { _draft = draft; return ExportQuery(q) with { EventSequenceConstraints = [] }; }
        finally { _draft = active; }
    }

    internal NeowEditorPrototype? PickerHost { get; set; }
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal bool AdvancedOpen { get => _draft.QueueAdvanced; set => _draft.QueueAdvanced = value; }
    internal IReadOnlyList<ModelKey> TransformationCardCandidates => _draft.TransformCards;

    private void CaptureResultPools(ModelKey character, int ascension, int players,
        Core.Authority.RuntimeContextAuthoritySnapshot? partyAuthority = null,
        MegaCrit.Sts2.Core.Unlocks.UnlockState? unlocks = null)
    {
        _draft.TransformCards = [];
        var authority = partyAuthority;
        if (authority is null)
        {
            if (players != 1) return;
            WorkbenchQueryCompiler.CompileAuthoredQuery(SearchQuery.Empty, AncientOptionConditionProfile.BroadDefault,
                _runtime, character, ascension, out authority);
        }
        var scenario = Beta111MorphicGroveAuthorityCapture.CaptureAuthoredBasics(authority, 2, unlocks);
        _draft.TransformCards = scenario.Targets.SelectMany(t => t.OrderedSourceCandidates ?? [])
            .Select(c => c.CardKey).Distinct().ToArray();
        foreach (var kind in _draft.Results.Keys.ToArray())
        {
            var pool = ResultPool(kind);
            for (int i = 0; i < _draft.Results[kind].Length; i++)
                if (_draft.Results[kind][i] is { } k && !pool.Contains(k)) _draft.Results[kind][i] = null;
        }
    }

    private IReadOnlyList<ModelKey> ResultPool(EventResultConditionKind kind) => kind switch
    {
        EventResultConditionKind.FakeMerchantOfferedFakeRelic => Beta111EventResultCatalog.FakeMerchantRelics,
        EventResultConditionKind.TrashHeapGrabCard => Beta111EventResultCatalog.TrashHeapGrabCards,
        EventResultConditionKind.TrashHeapDiveRelic => Beta111EventResultCatalog.TrashHeapDiveRelics,
        _ when EventResultTransformSemantics.IsTransform(kind) => _draft.TransformCards,
        _ => Beta111EventResultCatalog.ColorfulCharacterOrder
    };

    private void ResultTile(Godot.Control content, float x, float y, EventResultConditionKind kind, int index = 0)
    {
        if (!_draft.Results.TryGetValue(kind, out var slots)) _draft.Results[kind] = slots = new ModelKey?[EventResultTransformSemantics.DrawCount(kind)];
        var key = slots[index];
        var pool = ResultPool(kind);
        var objectKind = kind is EventResultConditionKind.FakeMerchantOfferedFakeRelic or EventResultConditionKind.TrashHeapDiveRelic
            ? GameContentKind.Relic : GameContentKind.Card;
        content.AddChild(new WorkspaceResultTile(_p, _icons, _names, _text, key, objectKind,
            () => { if (pool.Count > 0) PickerHost?.PickExternalObjects(pool, selected => { slots[index] = selected; Render(); }); },
            () => { slots[index] = null; Render(); }) { Position = new(x, y) });
    }

    internal SearchQuery ExportQuery(SearchQuery q)
    {
        if (_draft.PrototypeTargets.Count > 0)
            throw new InvalidOperationException("integration.prototype.e");
        var results = new List<EventResultSearchCondition>();
        foreach (var (kind, slots) in _draft.Results.OrderBy(p => p.Key))
        {
            var keys = slots.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
            if (keys.Length == 0) continue;
            results.Add(new(kind, keys[0]) { MorphicGroveSecondCard = keys.Length > 1 ? keys[1] : null });
        }
        ModelKey[] characters = [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
            BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Necrobinder, BaseGameModelKeys.Characters.Regent];
        if (_draft.CharacterColor >= 0)
            results.Add(new(EventResultConditionKind.ColorfulPhilosophersOfferedColor, characters[_draft.CharacterColor]));
        if (TransformTakenOver?.Invoke(_activeSeat, "E.TRIAL") != true && _draft.TrialCase >= 0 && !results.Any(c => c.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains))
            results.Add(new(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL")) { TrialCase = (TrialCaseTarget)_draft.TrialCase });
        if (_draft.TinkerType >= 0) results.Add(new(EventResultConditionKind.TinkerTimeTypeAndRider, new("CARD", "MAD_SCIENCE")) {
            TinkerCardType = (TinkerCardTypeTarget)_draft.TinkerType,
            TinkerRider = _draft.TinkerEffect is { } effect ? Enum.Parse<TinkerRiderTarget>(effect, true) : null });
        return q with { EventResultConditions = results.ToArray(), EventSequenceConstraints = QueueConditions.Select(c =>
            new EventSequenceSearchCondition(c.Act, null, c.Exact ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN,
                c.Position, c.Excluded ? new ModelKeySetFilter([], [], [c.Event]) : new ModelKeySetFilter([c.Event], [], []))).ToArray() };
    }

    internal void ImportQuery(SearchQuery q, bool importQueue = true)
    {
        if (importQueue) QueueConditions.Clear(); _draft.Results.Clear(); _draft.PrototypeTargets.Clear();
        _draft.ExpandedCondition = -1; _draft.SelectedEvent = null;
        _draft.TrialCase = _draft.TinkerType = _draft.CharacterColor = -1;
        _draft.TinkerEffect = null;
        foreach (var c in importQueue ? q.EventSequenceConstraints : [])
        {
            foreach(var k in c.Keys.Any.Concat(c.Keys.All)) QueueConditions.Add(new(c.Act,c.RangeValue,c.RangeMode == SearchSequenceRangeMode.ExactSlot,false,k));
            foreach(var k in c.Keys.Ban) QueueConditions.Add(new(c.Act,c.RangeValue,c.RangeMode == SearchSequenceRangeMode.ExactSlot,true,k));
        }
        foreach(var c in q.EventResultConditions)
        {
            if (c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor)
            {
                ModelKey[] characters = [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
                    BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Necrobinder, BaseGameModelKeys.Characters.Regent];
                _draft.CharacterColor = Array.IndexOf(characters,c.TargetKey);
            }
            else if (c.Kind == EventResultConditionKind.TrialCase) _draft.TrialCase = (int)c.TrialCase!;
            else if (c.Kind == EventResultConditionKind.TinkerTimeTypeAndRider) {
                _draft.TinkerType = (int)c.TinkerCardType!; _draft.TinkerEffect = c.TinkerRider?.ToString().ToLowerInvariant();
            }
            else _draft.Results[c.Kind] = EventResultTransformSemantics.DrawCount(c.Kind) == 2
                ? [c.TargetKey,c.MorphicGroveSecondCard] : [c.TargetKey];
        }
        if (_draft.Results.ContainsKey(EventResultConditionKind.TrialNondescriptInitialBasicsContains)) _draft.TrialCase = 2;
        _draft.Context = "";
    }
}

internal sealed partial class TransformationEditorPrototype
{
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal TransformationAggregateCondition? ExportCondition(SearchQuery q)
    {
        if (_draft.TakenOver.Count == 0)
        {
            if (_draft.Cards.Count > 0) throw new InvalidOperationException("integration.transform.source_required");
            return q.TransformationAggregate;
        }
        var opening = _draft.TakenOver.Contains("N.LeafyPoultice") ? TransformationOpening.LeafyPoultice :
            _draft.TakenOver.Contains("N.NewLeaf") ? TransformationOpening.NewLeaf :
            _draft.TakenOver.Contains("N.BonesLeafyNewLeaf") ? TransformationOpening.BonesLeafyNewLeaf :
            _draft.TakenOver.Contains("N.BonesLeafyOther") ? TransformationOpening.BonesLeafyOther : TransformationOpening.None;
        var order = opening is not (TransformationOpening.BonesLeafyNewLeaf or TransformationOpening.BonesLeafyOther) || q.OpeningRouteRelicRequirement?.OrderMode != BonesRouteOrderMode.ExactOrder
            ? TransformationPickupOrder.Any : q.OpeningRouteRelicRequirement.RequiredRelicKeys[0] == BaseGameModelKeys.Relics.LeafyPoultice
                ? (opening==TransformationOpening.BonesLeafyOther ? TransformationPickupOrder.LeafyThenCompanion : TransformationPickupOrder.LeafyThenNewLeaf) :
                (opening==TransformationOpening.BonesLeafyOther ? TransformationPickupOrder.CompanionThenLeafy : TransformationPickupOrder.NewLeafThenLeafy);
        var result = new TransformationAggregateCondition(opening, order, _draft.TakenOver.Contains("E.MORPHIC_GROVE"),
            _draft.TakenOver.Contains("E.AROMA_OF_CHAOS"), _draft.TakenOver.Contains("E.WHISPERING_HOLLOW"),
            _draft.Objective switch { 0 => TransformationAggregatePredicate.RareCountAtLeast,
                2 => TransformationAggregatePredicate.ContainsMultisetAndRemainingRare, _ => TransformationAggregatePredicate.ContainsMultiset },
            _draft.Objective == 0 ? _draft.RareCount : 0, _draft.Objective == 0 ? [] : _draft.Cards.ToArray()) { CompanionRelic=opening==TransformationOpening.BonesLeafyOther ? q.OpeningRouteRelicRequirement?.RequiredRelicKeys.FirstOrDefault(k=>k!=BaseGameModelKeys.Relics.LeafyPoultice) : null, Symbiote = _draft.TakenOver.Contains("E.SYMBIOTE"), TrialNondescript = _draft.TakenOver.Contains("E.TRIAL") };
        // ResultCount is display capacity, never a user-created RNG opportunity.
        if ((_draft.Objective == 0 ? result.MinimumRareCount : result.TargetMultiset.Count) is < 1 ||
            (_draft.Objective == 0 ? result.MinimumRareCount : result.TargetMultiset.Count) > result.OpportunityCount)
            throw new InvalidOperationException("integration.transform.capacity");
        return result;
    }
    internal void ImportQuery(SearchQuery q)
    {
        _draft.TakenOver.Clear(); _draft.Cards.Clear();
        if(q.TransformationAggregate is not { } t) return;
        // This existing bounded predicate is also authored by N's joint-three-slot editor.
        // Restore that presentation without duplicating the predicate in T's local state.
        if(t.Opening==TransformationOpening.BonesLeafyNewLeaf && !t.UsesEvents && t.Predicate==TransformationAggregatePredicate.ContainsMultiset) return;
        if(t.UsesNeow) _draft.TakenOver.Add(t.Opening switch { TransformationOpening.LeafyPoultice=>"N.LeafyPoultice",TransformationOpening.NewLeaf=>"N.NewLeaf",TransformationOpening.BonesLeafyOther=>"N.BonesLeafyOther",_=>"N.BonesLeafyNewLeaf" });
        if(t.MorphicGrove) _draft.TakenOver.Add("E.MORPHIC_GROVE");
        if(t.AromaOfChaos) _draft.TakenOver.Add("E.AROMA_OF_CHAOS");
        if(t.WhisperingHollow) _draft.TakenOver.Add("E.WHISPERING_HOLLOW");
        if(t.Symbiote) _draft.TakenOver.Add("E.SYMBIOTE");
        if(t.TrialNondescript) _draft.TakenOver.Add("E.TRIAL");
        _draft.Objective=t.Predicate switch { TransformationAggregatePredicate.ContainsMultiset=>1,
            TransformationAggregatePredicate.ContainsMultisetAndRemainingRare=>2, _=>0 };
        _draft.Cards.AddRange(t.TargetMultiset); _draft.RareCount=t.MinimumRareCount;
        _draft.ResultCount=t.OpportunityCount;
    }
}
