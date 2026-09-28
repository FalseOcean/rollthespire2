using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search.Event;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class EventEditorPrototype
{
    private string InlineText(string key) => _text.Get("query.event.inline." + key);

    private float RenderQueue(Control content, float width, float top)
    {
        Text(content, _text.Get("query.event.queue.add"), 0, top, width, 20);
        float row = top + 36;
        QueueOptions(content, Enumerable.Range(1, 3).Select(a => _text.Format("query.event.act", a)).ToArray(),
            _draft.QueueAct - 1, 0, row, 112, index => { _draft.QueueAct = index + 1; Render(); });
        QueueOptions(content, [_text.Get("query.event.position.first"), _text.Get("query.event.position.exact")],
            _draft.QueueExact ? 1 : 0, 124, row, 148, index => { _draft.QueueExact = index == 1; Render(); });
        var position = new SpinBox { Name = "EventQueuePosition", MinValue = 1, MaxValue = 12,
            Value = _draft.QueuePosition, Position = new(284, row), Size = new(82, 38) };
        position.AddThemeFontSizeOverride("font_size", 17);
        position.ValueChanged += value => _draft.QueuePosition = (int)value;
        content.AddChild(position);
        QueueOptions(content, [_text.Get("query.event.match.include"), _text.Get("query.event.match.exclude")],
            _draft.QueueExcluded ? 1 : 0, 378, row, 126, index => { _draft.QueueExcluded = index == 1; Render(); });
        Button(content, _text.Get("query.event.queue.choose"), 516, row, 174, OpenQueuePicker, false, 38);
        Text(content, InlineText("hint"), 0, row + 49, width, 15, true);

        float y = row + 92;
        Text(content, _text.Format("query.event.queue.conditions", _draft.QueueConditions.Count), 0, y, width, 18);
        y += 34;
        if (_draft.QueueConditions.Count == 0)
        {
            Text(content, _text.Get("query.event.queue.empty"), 0, y, width, 16, true);
            y += 42;
        }
        for (int index = 0; index < _draft.QueueConditions.Count; index++)
        {
            QueueCondition condition = _draft.QueueConditions[index];
            int captured = index;
            bool supported = !condition.Excluded && EventResultPrototypeWhitelist.Find(condition.Event) is not null;
            bool expanded = supported && _draft.ExpandedCondition == index;
            bool authored = AuthoredResultEvents().Contains(condition.Event.Entry);
            bool managed = TransformTakenOver?.Invoke(_activeSeat, "E." + condition.Event.Entry) == true;
            var panel = new Panel { Name = "EventCondition" + index, Position = new(0, y), Size = new(width, 84) };
            panel.AddThemeStyleboxOverride("panel", _p.Box(_p.Surface, expanded ? _p.Selected : _p.Line, 1));
            content.AddChild(panel);
            panel.AddChild(new EventThumbnailView(_thumbnails.Resolve(condition.Event), new Vector2(56, 56),
                EventThumbnailPresentation.ConditionSquareCrop) { Position = new(14, 13) });
            Text(panel, _names.Resolve(condition.Event, GameContentKind.Event), 84, 12, width - 372, 20);
            string range = _text.Format(condition.Exact ? "query.event.condition.exact" : "query.event.condition.first", condition.Position);
            string match = _text.Get(condition.Excluded ? "query.event.match.exclude" : "query.event.match.include");
            Text(panel, $"{_text.Format("query.event.act", condition.Act)} · {range} · {match}", 84, 43, width - 372, 14, true);
            if (supported)
            {
                string caption = InlineText(managed ? "managed" : authored ? "configured" : "optional");
                Button(panel, (expanded ? "▾ " : "› ") + caption, width - 272, 22, 204,
                    () => { _draft.ExpandedCondition = expanded ? -1 : captured; _draft.SelectedEvent = condition.Event; Render(); },
                    authored || managed, 36);
            }
            else Text(panel, InlineText(condition.Excluded ? "excluded" : "unavailable"), width - 280, 28, 216, 14, true);
            var remove = Button(panel, "×", width - 52, 22, 38, () => RemoveQueueCondition(captured), false, 36);
            remove.TooltipText = InlineText("remove_hint");
            float height = 84;
            if (expanded)
            {
                _draft.SelectedEvent = condition.Event;
                AddLine(panel, 16, 82, width - 32);
                height = RenderBoundResults(panel, condition.Event, width, 100) + 12;
                panel.Size = new(width, height);
            }
            y += height + 12;
        }
        return RenderRetainedResults(content, width, y);
    }

    private float RenderBoundResults(Control host, ModelKey key, float width, float top)
    {
        bool transform = _queuePickerPlayers == 1 && TransformationSources(_activeSeat).Any(s => s.Identity == key);
        if (transform)
        {
            bool managed = TransformTakenOver?.Invoke(_activeSeat, "E." + key.Entry) == true;
            var button = Button(host, InlineText(managed ? "open_transform" : "use_transform"), 16, top, 268,
                () => TransformationRequested?.Invoke("E." + key.Entry), managed, 36);
            button.TooltipText = InlineText("transform_hint");
            if (managed) return top + 40;
            top += 50;
        }
        return RenderResultEditor(host, width, top, inline: true);
    }

    // Old presets may contain E alone. Keep those predicates visible and unchanged
    // until the player explicitly binds them to W or removes them.
    private float RenderRetainedResults(Control content, float width, float top)
    {
        string[] retained = AuthoredResultEvents().Where(id => !_draft.QueueConditions.Any(c => !c.Excluded && c.Event.Entry == id)).ToArray();
        if (retained.Length == 0) return top;
        Text(content, InlineText("retained_title"), 0, top + 8, width, 18);
        Text(content, InlineText("retained_hint"), 0, top + 40, width, 14, true);
        float y = top + 80;
        foreach (string id in retained)
        {
            var key = new ModelKey("EVENT", id);
            Text(content, _names.Resolve(key, GameContentKind.Event), 0, y + 6, width - 520, 17);
            bool open = _draft.ExpandedCondition < 0 && _draft.SelectedEvent == key;
            Button(content, InlineText("view_result"), width - 502, y, 138,
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
            y += 48;
            if (open) y = RenderResultEditor(content, width, y, inline: true) + 16;
        }
        return y;
    }

    internal void FocusEvent(ModelKey key)
    {
        _draft.ExpandedCondition = _draft.QueueConditions.FindIndex(c => !c.Excluded && c.Event == key);
        _draft.SelectedEvent = key;
        Render();
    }

    private void AddQueueCondition(ModelKey key)
    {
        var condition = new QueueCondition(_draft.QueueAct, _draft.QueuePosition, _draft.QueueExact, _draft.QueueExcluded, key);
        int index = _draft.QueueConditions.IndexOf(condition);
        if (index < 0) { index = _draft.QueueConditions.Count; _draft.QueueConditions.Add(condition); }
        _draft.ExpandedCondition = !condition.Excluded && EventResultPrototypeWhitelist.Find(key) is not null ? index : -1;
        _draft.SelectedEvent = _draft.ExpandedCondition >= 0 ? key : null;
        QueueChanged?.Invoke();
        Render();
    }

    private void RemoveQueueCondition(int index)
    {
        QueueCondition removed = _draft.QueueConditions[index];
        _draft.QueueConditions.RemoveAt(index);
        if (!removed.Excluded && !_draft.QueueConditions.Any(c => !c.Excluded && c.Event == removed.Event)) ClearEventResults(removed.Event);
        if (_draft.ExpandedCondition == index) _draft.ExpandedCondition = -1;
        else if (_draft.ExpandedCondition > index) _draft.ExpandedCondition--;
        QueueChanged?.Invoke(); // Also releases a T source when its last positive queue condition is removed.
        Render();
    }

    private void ClearEventResults(ModelKey key)
    {
        foreach (var kind in _draft.Results.Keys.Where(kind => ResultEventId(kind) == key.Entry).ToArray()) _draft.Results.Remove(kind);
        if (key.Entry == "TRIAL") _draft.TrialCase = -1;
        if (key.Entry == "COLORFUL_PHILOSOPHERS") _draft.CharacterColor = -1;
        if (key.Entry == "TINKER_TIME") { _draft.TinkerType = -1; _draft.TinkerEffect = null; }
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

    private void QueueOptions(Control parent, string[] values, int selected, float x, float y, float width, Action<int> changed)
    {
        var control = new OptionButton { FitToLongestItem = false, ClipText = true };
        using var donor = _p.CompactButton("", 38, 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            control.AddThemeStyleboxOverride(state, donor.GetThemeStylebox(state));
        control.AddThemeColorOverride("font_color", _p.Color(_p.Text));
        control.AddThemeFontSizeOverride("font_size", 16);
        foreach (string value in values) control.AddItem(value);
        control.Select(selected); control.Position = new(x, y); control.Size = new(width, 38);
        control.ItemSelected += index => changed((int)index);
        parent.AddChild(control);
    }
}
