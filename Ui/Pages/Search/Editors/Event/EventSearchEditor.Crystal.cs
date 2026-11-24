using Godot;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class EventSearchEditor
{
    private static readonly CrystalSphereSearchRelics[] CrystalRelicChoices =
        [CrystalSphereSearchRelics.Driftwood, CrystalSphereSearchRelics.PrismaticGem, CrystalSphereSearchRelics.DingyRug];

    private static CrystalSphereSearchRelics ToggleCrystalRelic(CrystalSphereSearchRelics current, CrystalSphereSearchRelics selected)
    {
        if (current.HasFlag(selected)) return current & ~selected;
        if (selected == CrystalSphereSearchRelics.Driftwood) current &= ~CrystalSphereSearchRelics.PrismaticGem;
        if (selected == CrystalSphereSearchRelics.PrismaticGem) current &= ~CrystalSphereSearchRelics.Driftwood;
        return current | selected;
    }

    private float RenderCrystalResults(Control host, ModelKey eventKey, float x, float y, float width)
    {
        y = OpportunityNote(host, "crystal_premise", x, y, width);
        var authored = OpportunitiesFor(eventKey).ToArray();
        int divinations = _draft.CrystalDivinations;
        OpportunityButtons(host, x, y, Math.Min(width, 512), CrystalChoices,
            count => EventOpportunityPresentation.CrystalChoice(_text, count), count => count == divinations,
            count =>
            {
                _draft.CrystalDivinations = count;
                if (count == divinations || authored.Select(condition => condition.CrystalDivinations).Distinct().Count() > 1) return;
                foreach (var condition in authored)
                    _draft.OpportunityResults[_draft.OpportunityResults.IndexOf(condition)] = condition with { CrystalDivinations = count };
            });
        y += 48;
        var current = authored.Where(condition => condition.CrystalDivinations == divinations).ToArray();
        if (current.Length > 1) return RenderOpportunitySummaries(host, current, x, y, width);
        var existing = current.FirstOrDefault();
        var cards = existing?.CrystalCards ?? CrystalSphereSeedCards.None;
        var named = existing?.CrystalTargetCards?.ToArray() ?? [];
        var relics = existing?.CrystalRelics ?? _draft.CrystalRelics;
        EffectRelicRarity? relic = existing?.RelicRarity;
        void Update(CrystalSphereSeedCards targetCards, EffectRelicRarity? targetRelic,
            IReadOnlyList<ModelKey> targetNames, CrystalSphereSearchRelics premises)
        {
            _draft.CrystalRelics = premises;
            if (existing is not null) _draft.OpportunityResults.Remove(existing);
            if (targetCards != CrystalSphereSeedCards.None || targetRelic is not null || targetNames.Count > 0)
                _draft.OpportunityResults.Add(new(EventResultConditionKind.CrystalSphereRewards, eventKey)
                {
                    CrystalDivinations = divinations,
                    CrystalCards = targetCards == CrystalSphereSeedCards.None ? null : targetCards,
                    RelicRarity = targetRelic, CrystalTargetCards = targetNames.Count == 0 ? null : targetNames.ToArray(),
                    CrystalRelics = premises == CrystalSphereSearchRelics.None ? null : premises
                });
        }
        Text(host, _text.Get(OpportunityPrefix + "crystal_relics"), x, y, width, 16, true);
        OpportunityButtons(host, x, y + 28, Math.Min(width, 560), CrystalRelicChoices,
            flag => EventOpportunityPresentation.CrystalRelic(_names, flag), flag => relics.HasFlag(flag),
            flag => Update(cards, relic, named, ToggleCrystalRelic(relics, flag)));
        y += 70;
        var pool = CrystalSphereSearchSemantics.Pool(_draft.CrystalEffects, _queuePickerPlayers, relics, _draft.CrystalCharacter);
        var choices = pool?.Where(card => card.Rarity is EffectCardRarity.Uncommon or EffectCardRarity.Rare)
            .Select(card => card.CardKey).ToArray() ?? [];
        Text(host, _text.Get(OpportunityPrefix + "crystal_named_cards"), x, y, width, 16, true);
        y += 30;
        var represented = new HashSet<int>();
        foreach (var rarity in new[] { EffectCardRarity.Uncommon, EffectCardRarity.Rare })
        {
            float slotX = x + (rarity == EffectCardRarity.Uncommon ? 0 : WorkspaceResultTile.CompactWidth + 16);
            Text(host, EventOpportunityPresentation.Rarity(_text, rarity), slotX, y,
                WorkspaceResultTile.CompactWidth, 14, true);
            int position = Array.FindIndex(named, key => _draft.CardPickerMetadata.TryGetValue(key, out var metadata) && metadata.Rarity == rarity);
            if (position >= 0) represented.Add(position);
            ModelKey? key = position >= 0 ? named[position] : null;
            var rarityChoices = pool?.Where(card => card.Rarity == rarity).Select(card => card.CardKey).ToArray() ?? [];
            Action? choose = rarityChoices.Length == 0 ? null : () =>
                {
                    var context = new CardPickerContext(rarityChoices, _draft.CardPickerMetadata, true, "crystal-sphere");
                    Picker?.Open(new(rarityChoices, selected =>
                    {
                        var changed = named.ToList();
                        if (position < 0) changed.Add(selected); else changed[position] = selected;
                        Update(cards, relic, changed, relics); Render();
                    }, _names, _text, _icons, _ => "Other", context, _draft.MultiplayerOnlyCards, _queuePickerPlayers));
                };
            var tile = new WorkspaceResultTile(_p, _icons, _names, _text, key, GameContentKind.Card, choose,
                () => { Update(cards, relic, named.Where((_, i) => i != position).ToArray(), relics); Render(); },
                allowClear: key.HasValue, compact: true, candidateSurface: true)
            {
                Name = "CrystalTarget_" + rarity,
                Position = new(slotX, y + 24)
            };
            host.AddChild(tile);
        }
        y += WorkspaceResultTile.CompactHeight + 40;
        // Historical authored goals stay visible and individually removable;
        // the new two-slot editor never silently weakens a loaded request.
        foreach (int position in Enumerable.Range(0, named.Length).Where(i => !represented.Contains(i)))
        {
            Text(host, _text.Format(OpportunityPrefix + "crystal_retained_target", _names.Resolve(named[position], GameContentKind.Card)),
                x, y + 3, width - 42, 14, true);
            Button(host, "×", x + width - 36, y, 36, () =>
            {
                Update(cards, relic, named.Where((_, i) => i != position).ToArray(), relics); Render();
            }, false, 32).Name = "CrystalRetainedTarget_" + position;
            y += 38;
        }
        if (pool is null) y = OpportunityNote(host, named.Length == 0 ? "crystal_picker_unavailable" : "crystal_pool_unavailable", x, y, width);
        else if (named.Any(key => !choices.Contains(key))) y = OpportunityNote(host, "crystal_outside_pool", x, y, width);
        if (cards != CrystalSphereSeedCards.None)
        {
            string tiers = string.Join(" / ", EventOpportunityPresentation.CardRarities.Where(rarity => cards.HasFlag(rarity))
                .Select(rarity => EventOpportunityPresentation.Rarity(_text, rarity)));
            Text(host, _text.Format(OpportunityPrefix + "crystal_retained_tiers", tiers), x, y + 3, width - 42, 14, true);
            Button(host, "×", x + width - 36, y, 36,
                () => { Update(CrystalSphereSeedCards.None, relic, named, relics); Render(); }, false, 32).Name = "CrystalRetainedTiers";
            y += 38;
        }
        if (divinations == 3)
        {
            if (relic is null) return y;
            y = OpportunityNote(host, "crystal_minimum_relic", x, y, width);
        }
        Text(host, _text.Get(OpportunityPrefix + "rolls"), x, y, width, 16, true);
        IReadOnlyList<EffectRelicRarity> relicChoices = divinations == 3 ? [relic!.Value] : EventOpportunityPresentation.RelicRarities;
        OpportunityButtons(host, x, y + 28, Math.Min(width, 384), relicChoices,
            rarity => EventOpportunityPresentation.Rarity(_text, rarity),
            rarity => relic == rarity, rarity => Update(cards, relic == rarity ? null : rarity, named, relics));
        return y + 70;
    }
}
