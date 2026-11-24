using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class CombatRewardSearchEditor
{
    private void RenderPotions(Control content, float width, float top)
    {
        Text(content, _text.Get("query.combat.potions.title"), 16, top, width - 32, 18, true);
        RenderRewardControls(content, width, top + 36, _draft.PotionRange, _draft.PotionsUnordered, SetPotionRange,
            unordered => { if (_draft.PotionsUnordered == unordered) return; _draft.PotionsUnordered = unordered; Render(); },
            "CombatPotionSpecificMode");

        const int columns = 3;
        const float columnGap = 16;
        float cellWidth = (width - 32 - columnGap * (columns - 1)) / columns;
        float rowTop = top + 88;
        if (_draft.PotionRangeProblem.Length > 0)
        {
            var notice = Text(content, _draft.PotionRangeProblem, 16, rowTop, width - 32, 16, true);
            notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            notice.ClipText = false;
            notice.Size = new(width - 32, 44);
            rowTop += 56;
        }
        for (int i = 0; i < _draft.PotionRange; i++)
        {
            if (i > 0 && i % columns == 0)
                rowTop += _draft.Potions.Skip(i - columns).Take(columns).Any(p => p.PotionKey.HasValue)
                    ? 88 + WorkspaceResultTile.CompactHeight + 16 : 88;
            int slot = i;
            float left = 16 + (i % columns) * (cellWidth + columnGap);
            var condition = _draft.Potions[i];
            Text(content, _text.Format(_draft.PotionsUnordered ? "query.combat.potions.target" : "query.combat.battle", i + 1),
                left, rowTop, cellWidth, 17, true);
            var mode = new OptionButton
            {
                Name = "CombatPotionRequirement" + i, Position = new(left, rowTop + 32), Size = new(cellWidth, 40),
                FitToLongestItem = false, ClipText = true
            };
            mode.AddThemeFontSizeOverride("font_size", 17);
            mode.AddThemeColorOverride("font_color", _p.Color(_p.Text));
            using var donor = _p.CompactButton("", 40, 17, selected: !condition.IsNeutral);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
                mode.AddThemeStyleboxOverride(state, donor.GetThemeStylebox(state));
            foreach (var (requirement, key) in new[] {
                (CombatPotionSlotRequirement.Neutral, "neutral"), (CombatPotionSlotRequirement.NoDrop, "none"),
                (CombatPotionSlotRequirement.DropAny, "any"), (CombatPotionSlotRequirement.DropSpecific, "specific") })
            {
                mode.AddItem(_text.Get("query.combat.potions." + key), (int)requirement);
                SearchTooltip.SetItem(mode, mode.ItemCount - 1, _text.Get("query.combat.potions." + key + "_tooltip"));
            }
            mode.Select(mode.GetItemIndex((int)condition.Requirement));
            SearchTooltip.Set(mode, _text.Get("query.combat.potions.requirement_tooltip"));
            content.AddChild(mode);
            mode.ItemSelected += selected =>
            {
                var requirement = (CombatPotionSlotRequirement)mode.GetItemId((int)selected);
                if (requirement == CombatPotionSlotRequirement.DropSpecific)
                {
                    mode.Select(mode.GetItemIndex((int)_draft.Potions[slot].Requirement));
                    OpenPotionPicker(slot);
                }
                else { _draft.Potions[slot] = new(requirement, null); _draft.PotionRangeProblem = string.Empty; Render(); }
            };
            if (condition.PotionKey is { } potion)
            {
                var target = new WorkspaceResultTile(_p, _icons, _names, _text, potion,
                    GameContentKind.Potion, () => OpenPotionPicker(slot),
                    () => { _draft.Potions[slot] = new(CombatPotionSlotRequirement.Neutral, null); _draft.PotionRangeProblem = string.Empty; Render(); }, compact: true)
                { Position = new(left, rowTop + 88) };
                content.AddChild(target);
            }
        }
        int lastRow = ((_draft.PotionRange - 1) / columns) * columns;
        bool hasSpecific = _draft.Potions.Skip(lastRow).Take(_draft.PotionRange - lastRow).Any(p => p.PotionKey.HasValue);
        content.CustomMinimumSize = new(width, rowTop + (hasSpecific ? 88 + WorkspaceResultTile.CompactHeight + 16 : 88));
    }

    private void OpenPotionPicker(int slot)
    {
        if (_catalog?.PotionCatalogAvailable != true) return;
        var draft = _draft; // Keep the owner if a seat changes while the modal is open.
        _picker.Open(new(_catalog.PotionCandidates, key =>
        { draft.Potions[slot] = new(CombatPotionSlotRequirement.DropSpecific, key); draft.PotionRangeProblem = string.Empty; Render(); },
            _names, _text, _icons, key => _catalog.PotionCategories.GetValueOrDefault(key).ToString()));
    }

    private void SetPotionRange(int value)
    {
        int next = Math.Clamp(value, MinimumBattleRange, MaximumBattleRange);
        if (next == _draft.PotionRange) return;
        int required = Array.FindLastIndex(_draft.Potions, condition => !condition.IsNeutral) + 1;
        if (next < required)
        {
            _draft.PotionRangeProblem = _text.Format("query.combat.potions.range.targets_preserved", required);
            Render();
            return;
        }
        _draft.PotionRangeProblem = string.Empty;
        _draft.PotionRange = next;
        Render();
    }

    internal CombatPotionRewardSequenceSearchCondition? ExportPotionCondition() => PotionCondition(_draft);
    internal CombatPotionRewardSequenceSearchCondition? ExportPartyPotionCondition(int slot) =>
        _partyDrafts.TryGetValue(slot, out var draft) ? PotionCondition(draft) : null;
    private static CombatPotionRewardSequenceSearchCondition? PotionCondition(SeatDraft draft) =>
        draft.Potions.Take(draft.PotionRange).All(p => p.IsNeutral) ? null : new(draft.PotionRange,
            draft.PotionsUnordered ? CombatRewardSequenceOrderMode.Unordered : CombatRewardSequenceOrderMode.Ordered,
            draft.Potions.Take(draft.PotionRange).ToArray());
}
