using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class CombatRewardEditorPrototype
{
    private void RenderPotions(Control content, float width, float top)
    {
        Text(content, _text.Get("query.combat.potions.title"), 0, top, width, 20);
        RenderRewardControls(content, width, top + 36, _draft.PotionRange, _draft.PotionsUnordered, SetPotionRange,
            unordered => { if (_draft.PotionsUnordered == unordered) return; _draft.PotionsUnordered = unordered; Render(); });

        const int columns = 3;
        const float columnGap = 16;
        float cellWidth = (width - columnGap * (columns - 1)) / columns;
        float rowTop = top + 84;
        for (int i = 0; i < _draft.PotionRange; i++)
        {
            if (i > 0 && i % columns == 0)
                rowTop += _draft.Potions.Skip(i - columns).Take(columns).Any(p => p.PotionKey.HasValue) ? 130 : 82;
            int slot = i;
            float left = (i % columns) * (cellWidth + columnGap);
            var condition = _draft.Potions[i];
            Text(content, _text.Format(_draft.PotionsUnordered ? "query.combat.potions.target" : "query.combat.battle", i + 1),
                left, rowTop, cellWidth, 17, true);
            var mode = new OptionButton
            {
                Name = "CombatPotionRequirement" + i, Position = new(left, rowTop + 30), Size = new(cellWidth, 40),
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
                mode.AddItem(_text.Get("query.combat.potions." + key), (int)requirement);
            mode.Select(mode.GetItemIndex((int)condition.Requirement));
            content.AddChild(mode);
            mode.ItemSelected += selected =>
            {
                var requirement = (CombatPotionSlotRequirement)mode.GetItemId((int)selected);
                if (requirement == CombatPotionSlotRequirement.DropSpecific)
                {
                    mode.Select(mode.GetItemIndex((int)_draft.Potions[slot].Requirement));
                    OpenPotionPicker(slot);
                }
                else { _draft.Potions[slot] = new(requirement, null); Render(); }
            };
            if (condition.PotionKey is { } potion)
            {
                string name = _names.Resolve(potion, GameContentKind.Potion);
                var target = Button(content, name, left, rowTop + 78, cellWidth, () => OpenPotionPicker(slot), true, 36);
                target.AddThemeFontSizeOverride("font_size", 17);
                target.ClipText = true;
                target.TooltipText = name;
            }
        }
        int lastRow = ((_draft.PotionRange - 1) / columns) * columns;
        bool hasSpecific = _draft.Potions.Skip(lastRow).Take(_draft.PotionRange - lastRow).Any(p => p.PotionKey.HasValue);
        content.CustomMinimumSize = new(width, rowTop + (hasSpecific ? 126 : 82));
    }

    private void OpenPotionPicker(int slot)
    {
        if (_catalog?.PotionCatalogAvailable != true) return;
        var draft = _draft; // Keep the owner if a seat changes while the modal is open.
        _pickerHost.PickExternalObjects(_catalog.PotionCandidates, key =>
        { draft.Potions[slot] = new(CombatPotionSlotRequirement.DropSpecific, key); Render(); });
    }

    private void SetPotionRange(int value)
    {
        int next = Math.Clamp(value, MinimumBattleRange, MaximumBattleRange);
        for (int i = next; i < MaximumBattleRange; i++) _draft.Potions[i] = new(CombatPotionSlotRequirement.Neutral, null);
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
