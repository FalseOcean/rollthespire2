using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    // Result slots cover both stable single-target outcomes and authored transforms.
    private static int ResultSlotCount(EventResultConditionKind kind) => kind switch
    {
        EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic or
            EventResultConditionKind.FakeMerchantOfferedFakeRelic => 1,
        _ when EventResultTransformSemantics.IsTransform(kind) => EventResultTransformSemantics.DrawCount(kind),
        _ => throw new InvalidOperationException("integration.load_shape")
    };

    internal SearchQuery ExportSharedPartyQuery(SearchQuery query) => query with
    {
        EventSequenceConstraints = _partyQueue.Select(c => new EventSequenceSearchCondition(c.Act, null,
            c.Exact ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN, c.Position,
            c.Excluded ? new ModelKeySetFilter([], [], [c.Event]) : new ModelKeySetFilter([c.Event], [], []))).ToArray()
    };

    internal void ImportSharedPartyQuery(SearchQuery query)
    {
        CloseQueuePicker();
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

    internal SearchQuery ExportQuery(SearchQuery q)
    {
        if (_draft.PrototypeTargets.Count > 0)
            throw new InvalidOperationException("integration.prototype.e");
        var results = new List<EventResultSearchCondition>();
        foreach (var (kind, slots) in _draft.Results.OrderBy(p => p.Key))
        {
            string eventId = ResultEventId(kind);
            if (!IsResultActive(eventId) || TransformTakenOver?.Invoke(_activeSeat, "E." + eventId) == true) continue;
            var keys = slots.Where(k => k.HasValue).Select(k => k!.Value).ToArray();
            if (keys.Length == 0) continue;
            results.Add(new(kind, keys[0]) { MorphicGroveSecondCard = keys.Length > 1 ? keys[1] : null });
        }
        ModelKey[] characters = [BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent,
            BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Necrobinder, BaseGameModelKeys.Characters.Regent];
        if (IsResultActive("COLORFUL_PHILOSOPHERS") && _draft.CharacterColor >= 0)
            results.Add(new(EventResultConditionKind.ColorfulPhilosophersOfferedColor, characters[_draft.CharacterColor]));
        if (IsResultActive("TRIAL") && TransformTakenOver?.Invoke(_activeSeat, "E.TRIAL") != true && _draft.TrialCase >= 0 && !results.Any(c => c.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains))
            results.Add(new(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL")) { TrialCase = (TrialCaseTarget)_draft.TrialCase });
        if (IsResultActive("TINKER_TIME") && _draft.TinkerType >= 0) results.Add(new(EventResultConditionKind.TinkerTimeTypeAndRider, new("CARD", "MAD_SCIENCE")) {
            TinkerCardType = (TinkerCardTypeTarget)_draft.TinkerType,
            TinkerRider = _draft.TinkerEffect is { } effect ? Enum.Parse<TinkerRiderTarget>(effect, true) : null });
        return q with { FirstActUnknownEventPrefixCount = _draft.UnknownEventPrefixCount,
            EventResultConditions = results.ToArray(), EventSequenceConstraints = QueueConditions.Select(c =>
            new EventSequenceSearchCondition(c.Act, null, c.Exact ? SearchSequenceRangeMode.ExactSlot : SearchSequenceRangeMode.FirstN,
                c.Position, c.Excluded ? new ModelKeySetFilter([], [], [c.Event]) : new ModelKeySetFilter([c.Event], [], []))).ToArray() };
    }

    internal void ImportQuery(SearchQuery q, bool importQueue = true)
    {
        _draft.UnknownEventPrefixCount = q.FirstActUnknownEventPrefixCount;
        CloseQueuePicker();
        if (importQueue) QueueConditions.Clear(); _draft.Results.Clear(); _draft.PrototypeTargets.Clear(); _draft.InactiveResultEvents.Clear();
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
            else _draft.Results[c.Kind] = ResultSlotCount(c.Kind) == 2
                ? [c.TargetKey,c.MorphicGroveSecondCard] : [c.TargetKey];
        }
        if (_draft.Results.ContainsKey(EventResultConditionKind.TrialNondescriptInitialBasicsContains)) _draft.TrialCase = 2;
        _draft.Context = "";
    }
}
