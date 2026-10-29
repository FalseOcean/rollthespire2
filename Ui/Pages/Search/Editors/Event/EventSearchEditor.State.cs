using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    internal bool HasConditions(bool party) => party && _partyQueue.Count > 0 || (party ? _partyDrafts : _drafts).Values.Any(d =>
        d.QueueConditions.Count > 0 || d.Results.Values.Any(keys => keys.Any(k => k.HasValue)) ||
        d.CharacterColor >= 0 || d.TrialCase >= 0 || d.TinkerType >= 0 || d.PrototypeTargets.Count > 0 || d.UnknownEventPrefixCount > 0);
    internal Action ClearConditions(bool party)
    {
        var restore = WorkbenchConditionReset.Swap(party ? _partyDrafts : _drafts, _ => new SeatDraft());
        var queue = _partyQueue.ToArray();
        if (party) _partyQueue.Clear();
        return () => { restore(); if (party) { _partyQueue.Clear(); _partyQueue.AddRange(queue); } };
    }

    internal int AuthoredConditionCount() => QueueConditions.Count + AuthoredResultEvents().Count + (_draft.UnknownEventPrefixCount > 0 ? 1 : 0);

    private HashSet<string> AuthoredResultEvents()
    {
        var resultEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach ((EventResultConditionKind kind, ModelKey?[] slots) in _draft.Results)
        {
            if (!slots.Any(key => key.HasValue)) continue;
            string eventId = ResultEventId(kind);
            if (IsResultActive(eventId) && TransformTakenOver?.Invoke(_activeSeat, "E." + eventId) != true) resultEvents.Add(eventId);
        }
        if (IsResultActive("COLORFUL_PHILOSOPHERS") && _draft.CharacterColor >= 0) resultEvents.Add("COLORFUL_PHILOSOPHERS");
        if (IsResultActive("TRIAL") && _draft.TrialCase >= 0 && TransformTakenOver?.Invoke(_activeSeat, "E.TRIAL") != true)
            resultEvents.Add("TRIAL");
        if (IsResultActive("TINKER_TIME") && _draft.TinkerType >= 0) resultEvents.Add("TINKER_TIME");
        return resultEvents;
    }

    internal ObjectPickerPanel? Picker { get; set; }
    internal bool GuideOpen { get => _guideExpanded; set => _guideExpanded = value; }
    internal bool AdvancedOpen { get => _draft.QueueAdvanced; set => SetQueueAdvanced(value); }
    internal IReadOnlyList<ModelKey> TransformationCardCandidates => _draft.TransformCards;
    internal IReadOnlyDictionary<ModelKey, CardPickerCandidateMetadata> TransformationCardPickerMetadata => _draft.CardPickerMetadata;
    internal IReadOnlySet<ModelKey> TransformationMultiplayerOnlyCards => _draft.MultiplayerOnlyCards;

    internal EventEditorStateSnapshot ExportEditorState() => new(
        _draft.Results.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
        _draft.CharacterColor, _draft.TrialCase, _draft.TinkerType, _draft.TinkerEffect,
        _draft.InactiveResultEvents.Order(StringComparer.Ordinal).ToArray());

    internal EventEditorStateSnapshot? ExportPartyEditorState(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out SeatDraft? draft)) return null;
        SeatDraft active = _draft;
        try { _draft = draft; return ExportEditorState(); }
        finally { _draft = active; }
    }

    internal void ImportEditorState(EventEditorStateSnapshot state)
    {
        if (state.CharacterColor is < -1 or > 4 || state.TrialCase is < -1 or > 2 ||
            state.TinkerType is < -1 or > 2 || state.Results.Any(pair =>
                pair.Value.Length != ResultSlotCount(pair.Key)))
            throw new InvalidOperationException("integration.load_shape");
        _draft.Results.Clear();
        foreach (var (kind, slots) in state.Results) _draft.Results[kind] = slots.ToArray();
        _draft.CharacterColor = state.CharacterColor;
        _draft.TrialCase = state.TrialCase;
        _draft.TinkerType = state.TinkerType;
        _draft.TinkerEffect = state.TinkerEffect;
        _draft.InactiveResultEvents.Clear();
        _draft.InactiveResultEvents.UnionWith(state.InactiveResultEvents);
        _draft.ExpandedCondition = -1;
        _draft.SelectedEvent = null;
    }

    internal bool IsResultActive(string eventId) => !_draft.InactiveResultEvents.Contains(eventId);
}
