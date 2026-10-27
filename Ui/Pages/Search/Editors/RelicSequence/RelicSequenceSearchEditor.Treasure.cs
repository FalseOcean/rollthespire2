using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class RelicSequenceSearchEditor
{
    internal TreasureRelicSearchCondition? BuildTreasureCondition()
    {
        if (_draft.TreasureStart == 1 && _draft.TreasureCount == 1 &&
            _draft.TreasureOrder == TreasureRelicOrderMode.Ordered && !_draft.TreasureSlots.Any(k => k.HasValue)) return null;
        return new(_draft.TreasureStart, _draft.TreasureCount, _draft.TreasureOrder, _draft.TreasureSlots.ToArray());
    }

    internal TreasureRelicSearchCondition? BuildPartyTreasureCondition(int slot)
    {
        if (!_partyDrafts.TryGetValue(slot, out var draft)) return null;
        var active = _draft;
        try { _draft = draft; return BuildTreasureCondition(); }
        finally { _draft = active; }
    }

    private void RenderTreasure(Control content, float width, float top)
    {
        var settings = new Control { Name = "TreasureConditionSettings", Position = new(16, top), Size = new(width - 32, 84) };
        content.AddChild(settings);
        // These three windows cover the existing Start/Count contract without
        // asking players to derive an encountered chest from two numeric fields.
        foreach (var (name, key, start, count, left, buttonWidth) in new[]
        {
            ("TreasureWindowFirst", "query.treasure.window.first", 1, 1, 0f, 128f),
            ("TreasureWindowSecond", "query.treasure.window.second", 2, 1, 140f, 128f),
            ("TreasureWindowBoth", "query.treasure.window.both", 1, 2, 280f, 160f)
        })
        {
            var window = Button(settings, _text.Get(key), left, 0, buttonWidth,
                () => SetTreasureWindow(start, count), _draft.TreasureStart == start && _draft.TreasureCount == count, 36);
            window.Name = name;
            window.TooltipText = string.Empty;
            window.AddThemeFontSizeOverride("font_size", 16);
        }
        bool ordered = _draft.TreasureOrder == TreasureRelicOrderMode.Ordered;
        var mode = Button(settings, (ordered ? "✓  " : "○  ") + _text.Get("query.treasure.order.specific_chest"),
            460, 0, 180, () =>
            {
                _draft.TreasureOrder = ordered ? TreasureRelicOrderMode.Unordered : TreasureRelicOrderMode.Ordered;
                Render();
            }, ordered, 36);
        mode.Name = "TreasureOrderMode";
        mode.AddThemeFontSizeOverride("font_size", 16);
        mode.ToggleMode = true;
        mode.ButtonPressed = ordered;
        mode.TooltipText = string.Empty;
        Text(settings, _text.Get(ordered ? "query.treasure.order.specific_caption" : "query.treasure.order.any_caption"),
            0, 48, width - 32, 15, true);

        float y = top + 92;
        const float inset = 16, stride = WorkspaceResultTile.TileWidth + 12;
        float targetTop = y + (ordered ? 32 : 0);
        if (ordered)
        {
            for (int slot = 0; slot < _draft.TreasureCount; slot++)
            {
                var ordinal = Text(content, _text.Format("query.treasure.encounter", _draft.TreasureStart + slot),
                    inset + slot * stride, y, WorkspaceResultTile.TileWidth, 16, true);
                ordinal.Name = "TreasureChestOrdinal" + (slot + 1);
                ordinal.HorizontalAlignment = HorizontalAlignment.Center;
                AddTreasureSlot(content, slot, inset + slot * stride, targetTop, active: true);
            }
        }
        else
        {
            // Like the shop row, unordered targets form a compact list. Keep
            // source indices: changing the mode must never reorder authored slots.
            int visualSlot = 0;
            for (int slot = 0; slot < _draft.TreasureCount; slot++)
                if (_draft.TreasureSlots[slot].HasValue)
                    AddTreasureSlot(content, slot, inset + visualSlot++ * stride, targetTop, active: true);
            int emptySlot = Array.FindIndex(_draft.TreasureSlots, 0, _draft.TreasureCount, key => !key.HasValue);
            if (emptySlot >= 0)
                AddTreasureSlot(content, emptySlot, inset + visualSlot * stride, targetTop, active: true);
        }
        y = targetTop + WorkspaceResultTile.CompactHeight + 24;
        var retained = Enumerable.Range(_draft.TreasureCount, TreasureRelicSearchCondition.MaximumOrdinal - _draft.TreasureCount)
            .Where(slot => _draft.TreasureSlots[slot].HasValue).ToArray();
        if (retained.Length > 0)
        {
            Text(content, _text.Get("query.treasure.retained"), inset, y, width - 32, 15, true).Name = "TreasureRetainedTargets";
            y += 32;
            for (int index = 0; index < retained.Length; index++)
                AddTreasureSlot(content, retained[index], inset + index * stride, y, active: false);
            y += WorkspaceResultTile.CompactHeight + 24;
        }
        var premise = Text(content, _text.Get("query.treasure.premise"), 16, y, width - 32, 15, true);
        premise.ClipText = false;
        premise.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        premise.Size = new(width - 32, 100);
        if (!string.IsNullOrWhiteSpace(_draft.Problem))
            Text(content, _draft.Problem, 16, y + 108, width - 32, 15, true);
        content.CustomMinimumSize = new(width, y + 152);
    }

    private void SetTreasureWindow(int start, int count)
    {
        _draft.TreasureStart = start;
        _draft.TreasureCount = count;
        // Slots remain relative to the window, including dormant saved targets.
        Render();
    }

    private void AddTreasureSlot(Control parent, int slot, float x, float y, bool active)
    {
        if (!_draft.TreasureSlots[slot].HasValue)
        {
            var add = Button(parent, _text.Get("query.treasure.add_target"), x, y, WorkspaceResultTile.TileWidth,
                () => OpenTreasurePicker(slot), false, WorkspaceResultTile.CompactHeight);
            add.Name = "TreasureChooseTarget" + (slot + 1);
            add.AccessibilityName = _text.Get("picker.any_choose_result");
            add.AddThemeFontSizeOverride("font_size", 15);
            add.AddThemeStyleboxOverride("normal", _p.EmptySlotBox());
            add.Disabled = !active || _draft.Catalog is null || !_draft.Catalog.CatalogAvailable;
            return;
        }
        var tile = new WorkspaceResultTile(_p, _icons, _names, _text, _draft.TreasureSlots[slot], GameContentKind.Relic,
            () => OpenTreasurePicker(slot), () => { _draft.TreasureSlots[slot] = null; Render(); },
            isRequested: active, compact: true, compactWidth: WorkspaceResultTile.TileWidth)
        { Name = "TreasureTargetSlot" + (slot + 1), Position = new(x, y) };
        // Shared tiles bind their name as a tooltip; this surface has no Owner
        // request for tooltips. Keep the shop tile interaction without adding one.
        var choose = tile.GetChild<Button>(0);
        SearchTooltip.Set(choose, string.Empty);
        choose.TooltipText = string.Empty;
        choose.Name = "TreasureChooseTarget" + (slot + 1);
        choose.Disabled = !active || _draft.Catalog is null || !_draft.Catalog.CatalogAvailable;
        tile.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == "×")
            .Name = "TreasureClearTarget" + (slot + 1);
        parent.AddChild(tile);
    }

    private void OpenTreasurePicker(int slot)
    {
        if (_draft.Catalog is null || !_draft.Catalog.CatalogAvailable) return;
        ModelKey[] candidates = new[] { RelicSequenceKind.Common, RelicSequenceKind.Uncommon, RelicSequenceKind.Rare }
            .SelectMany(_draft.Catalog.CandidatesFor).Distinct(ModelKeyComparer.Instance).ToArray();
        SeatDraft draft = _draft;
        _picker.Open(new(candidates, key => { draft.TreasureSlots[slot] = key; Render(); }, _names, _text, _icons,
            key => draft.Catalog.Categories.GetValueOrDefault(key).ToString()));
    }
}
