using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search.Event;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    private string InlineText(string key) => _text.Get("query.event.inline." + key);

    private float RenderQueue(Control content, float width, float top)
    {
        float builderBottom = RenderQueueBuilder(content, width, top);
        float y = builderBottom + 16;
        Text(content, _text.Format("query.event.queue.conditions", QueueConditions.Count), 16, y, width - 32, 17);
        y += 36;
        if (QueueConditions.Count == 0)
        {
            Text(content, _text.Get("query.event.queue.empty"), 16, y, width - 32, 16, true);
            y += 36;
        }
        for (int index = 0; index < QueueConditions.Count; index++)
        {
            QueueCondition condition = QueueConditions[index];
            int captured = index;
            bool supported = !condition.Excluded && EventResultPrototypeWhitelist.Find(condition.Event) is not null;
            bool expanded = supported && _draft.ExpandedCondition == index;
            bool authored = StoredResultEvents().Contains(condition.Event.Entry);
            bool managed = TransformTakenOver?.Invoke(_activeSeat, "E." + condition.Event.Entry) == true;
            var row = new Control { Name = "EventCondition" + index, Position = new(0, y), Size = new(width, 80) };
            content.AddChild(row);
            row.AddChild(new EventThumbnailView(_thumbnails.Resolve(condition.Event), new Vector2(56, 56),
                EventThumbnailPresentation.ConditionSquareCrop) { Position = new(16, 12) });
            var eventName = Text(row, _names.Resolve(condition.Event, GameContentKind.Event), 88, 8, width - 384, 18);
            MainPanelTheme.SetColor(eventName, "font_color", _p.Selected);
            eventName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            string range = _text.Format(condition.Exact ? "query.event.condition.exact" : "query.event.condition.first", condition.Position);
            string match = _text.Get(condition.Excluded ? "query.event.match.exclude" : "query.event.match.include");
            Text(row, $"{_text.Format("query.event.act", condition.Act)} · {range} · {match}", 88, 40, width - 384, 14, true);
            if (supported)
            {
                string caption = InlineText(managed ? "managed" : authored ? "configured" : "optional");
                ModeButton(row, (expanded ? "▾ " : "› ") + caption, width - 272, 22, 204,
                    () => { _draft.ExpandedCondition = expanded ? -1 : captured; _draft.SelectedEvent = condition.Event; Render(); },
                    authored || managed, 36);
            }
            var remove = Button(row, "×", width - 52, 22, 36, () => RemoveQueueCondition(captured), false, 36);
            SearchTooltip.Set(remove, InlineText(_queuePickerPlayers > 1 ? "remove_party_hint" : "remove_hint"));
            float height = 80;
            if (!_draft.Catalog.CandidatesForAct(condition.Act).Any(candidate => candidate.EventKey == condition.Event))
            {
                Text(row, InlineText("scope_affected"), 16, height, width - 32, 14, true);
                height += 36;
            }
            if (expanded)
            {
                _draft.SelectedEvent = condition.Event;
                height = RenderBoundResults(row, condition.Event, width, height + 8) + 12;
            }
            AddLine(row, 8, height - 1, width - 16);
            row.Size = new(width, height);
            y += height + 8;
        }
        return RenderRetainedResults(content, width, y);
    }

    private float RenderQueueBuilder(Control content, float width, float top)
    {
        var builder = new Control { Name = "EventConditionBuilder", Position = new(0, top),
            Size = new(width, 72) };
        content.AddChild(builder);
        var act = QueueOptions(builder, Enumerable.Range(1, 3).Select(value => _text.Format("query.event.act", value)).ToArray(),
            _draft.QueueAct - 1, 16, 16, 112,
            index => { CloseQueuePicker(); _draft.QueueAct = index + 1; Render(); });
        act.Name = "EventQueueAct";
        RenderQueuePosition(builder, 140, 16);
        Button(builder, _text.Get("query.event.queue.choose"), 504, 16, 174, OpenQueuePicker, false, 40).Name = "EventQueueChoose";
        var advanced = ModeButton(builder, _text.Get("query.event.queue.advanced"), 690, 16, 128,
            ToggleQueueAdvanced, _draft.QueueAdvanced, 40);
        advanced.Name = "EventQueueAdvanced";
        advanced.AddThemeFontSizeOverride("font_size", 16);
        return top + builder.Size.Y;
    }

    private void RenderQueuePosition(Control host, float left, float top)
    {
        var group = new Control { Name = "EventQueuePosition", Position = new(left, top), Size = new(352, 40) };
        host.AddChild(group);
        if (_draft.QueueAdvanced)
        {
            var mode = QueueOptions(group,
                [_text.Get("query.event.position.first"), _text.Get("query.event.position.exact")],
                _draft.QueueExact ? 1 : 0, 0, 0, 104,
                index => { CloseQueuePicker(); _draft.QueueExact = index == 1; Render(); });
            mode.Name = "EventQueueRangeMode";
            var polarity = QueueOptions(group,
                [_text.Get("query.event.match.include"), _text.Get("query.event.match.exclude")],
                _draft.QueueExcluded ? 1 : 0, 248, 0, 104,
                index => { CloseQueuePicker(); _draft.QueueExcluded = index == 1; Render(); });
            polarity.Name = "EventQueuePolarity";
        }
        else
        {
            var range = Text(group, _text.Get(_draft.QueueExact ? "query.event.position.exact" : "query.event.position.first"),
                0, 0, 104, 16, true);
            range.Name = "EventQueueRangeMode";
            range.Size = new(104, 40);
            range.VerticalAlignment = VerticalAlignment.Center;
            var match = Text(group, _text.Get(_draft.QueueExcluded ? "query.event.match.exclude" : "query.event.match.include"),
                248, 0, 104, 16, true);
            match.Name = "EventQueuePolarity";
            match.Size = new(104, 40);
            match.VerticalAlignment = VerticalAlignment.Center;
        }
        var decrease = Button(group, "−", 116, 0, 36, () => SetQueuePosition(_draft.QueuePosition - 1), false, 40);
        decrease.Name = "EventQueuePositionDecrease";
        decrease.TooltipText = string.Empty;
        decrease.AccessibilityName = _text.Get("query.relic.range.decrease");
        decrease.Disabled = _draft.QueuePosition <= 1;
        var value = Text(group, _draft.QueuePosition.ToString(), 158, 0, 36, 18);
        value.Name = "EventQueuePositionValue";
        value.Size = new(36, 40);
        value.HorizontalAlignment = HorizontalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        var increase = Button(group, "+", 200, 0, 36, () => SetQueuePosition(_draft.QueuePosition + 1), false, 40);
        increase.Name = "EventQueuePositionIncrease";
        increase.TooltipText = string.Empty;
        increase.AccessibilityName = _text.Get("query.relic.range.increase");
        increase.Disabled = _draft.QueuePosition >= 12;
    }

    private void SetQueuePosition(int value)
    {
        int next = Math.Clamp(value, 1, 12);
        if (next == _draft.QueuePosition) return;
        CloseQueuePicker();
        _draft.QueuePosition = next;
        Render();
    }

    private void SetQueueAdvanced(bool advanced)
    {
        CloseQueuePicker();
        _draft.QueueAdvanced = advanced;
    }

    private void ToggleQueueAdvanced()
    {
        SetQueueAdvanced(!_draft.QueueAdvanced);
        Render();
    }

    private float RenderBoundResults(Control host, ModelKey key, float width, float top)
    {
        bool transform = _queuePickerPlayers == 1 && TransformationSources(_activeSeat).Any(s => s.Identity == key);
        if (transform)
        {
            bool managed = TransformTakenOver?.Invoke(_activeSeat, "E." + key.Entry) == true;
            var button = ModeButton(host, InlineText(managed ? "open_transform" : "use_transform"), 16, top, 268,
                () => TransformationRequested?.Invoke("E." + key.Entry), managed, 36);
            SearchTooltip.Set(button, InlineText("transform_hint"));
            if (managed) return top + 40;
            top += 50;
        }
        return RenderResultEditor(host, width, top, inline: true);
    }

    // Result-only legacy queries stay active. Requirements retained after an
    // explicit source edit are stored separately from their participation state.
    private float RenderRetainedResults(Control content, float width, float top)
    {
        string[] retained = StoredResultEvents().Where(id => !QueueConditions.Any(c => !c.Excluded && c.Event.Entry == id)).ToArray();
        if (retained.Length == 0) return top;
        Text(content, InlineText("retained_title"), 16, top + 8, width - 32, 18);
        Text(content, InlineText("retained_hint"), 16, top + 40, width - 32, 14, true);
        float y = top + 80;
        foreach (string id in retained)
        {
            var key = new ModelKey("EVENT", id);
            Text(content, _names.Resolve(key, GameContentKind.Event), 16, y + 6, width - 536, 17);
            bool open = _draft.ExpandedCondition < 0 && _draft.SelectedEvent == key;
            ModeButton(content, InlineText("view_result"), width - 502, y, 138,
                () => { _draft.ExpandedCondition = -1; _draft.SelectedEvent = open ? null : key; Render(); }, open, 36);
            int act = Enumerable.Range(1, 3).OrderBy(a => a == _draft.QueueAct ? 0 : 1)
                .FirstOrDefault(a => _draft.Catalog.CandidatesForAct(a).Any(c => c.EventKey == key));
            var bind = Button(content, act > 0 ? _text.Format("query.event.inline.bind", act, _draft.QueuePosition) : InlineText("unavailable"),
                width - 352, y, 288, () =>
                {
                    if (act == 0) return;
                    _draft.QueueAct = act; _draft.QueueExcluded = false; _draft.QueueExact = false;
                    AddQueueCondition(key);
                }, false, 36);
            bind.Disabled = act == 0;
            Button(content, "×", width - 52, y, 38, () => { ClearEventResults(key); Render(); }, false, 36);
            string status = !IsResultActive(id) ? "retained_inactive"
                : TransformTakenOver?.Invoke(_activeSeat, "E." + id) == true ? "managed" : "retained_active";
            Text(content, InlineText(status),
                16, y + 40, width - 32, 14, true);
            y += 80;
            if (open) y = RenderResultEditor(content, width, y, inline: true) + 16;
        }
        _presentation.Section(content, top, y);
        return y;
    }

    internal void FocusEvent(ModelKey key)
    {
        _draft.ExpandedCondition = QueueConditions.FindIndex(c => !c.Excluded && c.Event == key);
        _draft.SelectedEvent = key;
        Render();
    }

    private void AddQueueCondition(ModelKey key)
    {
        var condition = new QueueCondition(_draft.QueueAct, _draft.QueuePosition,
            _draft.QueueExact, _draft.QueueExcluded, key);
        int index = QueueConditions.IndexOf(condition);
        if (index < 0) { index = QueueConditions.Count; QueueConditions.Add(condition); }
        SetResultActivity(key.Entry, QueueConditions.Any(row => !row.Excluded && row.Event == key));
        _draft.ExpandedCondition = !condition.Excluded && EventResultPrototypeWhitelist.Find(key) is not null ? index : -1;
        _draft.SelectedEvent = _draft.ExpandedCondition >= 0 ? key : null;
        QueueChanged?.Invoke();
        Render();
    }

    private void RemoveQueueCondition(int index)
    {
        QueueCondition removed = QueueConditions[index];
        QueueConditions.RemoveAt(index);
        if (!removed.Excluded && !QueueConditions.Any(c => !c.Excluded && c.Event == removed.Event))
        {
            SetResultActivity(removed.Event.Entry, false);
        }
        foreach (var draft in _queuePickerPlayers > 1 ? _partyDrafts.Values.AsEnumerable() : new[] { _draft })
        {
            if (draft.ExpandedCondition == index) draft.ExpandedCondition = -1;
            else if (draft.ExpandedCondition > index) draft.ExpandedCondition--;
        }
        QueueChanged?.Invoke();
        Render();
    }

    private void SetResultActivity(string eventId, bool active)
    {
        foreach (SeatDraft draft in _queuePickerPlayers > 1
                     ? _partyDrafts.Values.Append(_draft).Distinct()
                     : new[] { _draft })
        {
            if (active) draft.InactiveResultEvents.Remove(eventId);
            else draft.InactiveResultEvents.Add(eventId);
        }
    }

    private HashSet<string> StoredResultEvents()
    {
        var events = _draft.Results.Where(pair => pair.Value.Any(key => key.HasValue))
            .Select(pair => ResultEventId(pair.Key)).ToHashSet(StringComparer.Ordinal);
        if (_draft.CharacterColor >= 0) events.Add("COLORFUL_PHILOSOPHERS");
        if (_draft.TrialCase >= 0) events.Add("TRIAL");
        if (_draft.TinkerType >= 0) events.Add("TINKER_TIME");
        return events;
    }

    private void ClearEventResults(ModelKey key)
    {
        foreach (var kind in _draft.Results.Keys.Where(kind => ResultEventId(kind) == key.Entry).ToArray()) _draft.Results.Remove(kind);
        if (key.Entry == "TRIAL") _draft.TrialCase = -1;
        if (key.Entry == "COLORFUL_PHILOSOPHERS") _draft.CharacterColor = -1;
        if (key.Entry == "TINKER_TIME") { _draft.TinkerType = -1; _draft.TinkerEffect = null; }
        _draft.InactiveResultEvents.Remove(key.Entry);
        _draft.PrototypeTargets.RemoveWhere(k => k.StartsWith(key.Serialized + ":", StringComparison.Ordinal));
        if (_draft.SelectedEvent == key) _draft.SelectedEvent = null;
    }

    private static string ResultEventId(EventResultConditionKind kind) => kind switch
    {
        EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic => "TRASH_HEAP",
        EventResultConditionKind.FakeMerchantOfferedFakeRelic => "FAKE_MERCHANT",
        EventResultConditionKind.MorphicGroveGroupInitialBasicsContains => "MORPHIC_GROVE",
        EventResultConditionKind.SymbioteInitialBasicTransform => "SYMBIOTE",
        EventResultConditionKind.AromaOfChaosInitialBasicTransform => "AROMA_OF_CHAOS",
        EventResultConditionKind.WhisperingHollowInitialBasicTransform => "WHISPERING_HOLLOW",
        EventResultConditionKind.TrialNondescriptInitialBasicsContains => "TRIAL",
        _ => kind.ToString()
    };

    private OptionButton QueueOptions(Control parent, string[] values, int selected, float x, float y, float width, Action<int> changed)
    {
        var control = new OptionButton { FitToLongestItem = false, ClipText = true };
        using var donor = _p.CompactButton("", 40, 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            control.AddThemeStyleboxOverride(state, donor.GetThemeStylebox(state));
        control.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        control.AddThemeFontSizeOverride("font_size", 16);
        foreach (string value in values) control.AddItem(value);
        control.Select(selected); control.Position = new(x, y); control.Size = new(width, 40);
        control.ItemSelected += index => changed((int)index);
        parent.AddChild(control);
        return control;
    }
}
