using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private static InRunMapDetailObject[] CombatRewardInfluences(PredictorRouteNodeKnowledge node) =>
        node.CombatRequirements == null ? [] : node.CombatRelics.Where(observation => observation.IsKnown && !observation.Value.Melted)
            .Select(observation => observation.Value).Where(relic => relic.Key.Entry switch
            {
                "PRAYER_WHEEL" => node.Room == PredictorRoomKind.Monster,
                "WHITE_STAR" or "BLACK_STAR" => node.Room == PredictorRoomKind.Elite,
                "LAVA_ROCK" => node.Room == PredictorRoomKind.Boss && node.Position.Act == 1 && !relic.LavaRockTriggered,
                "WONGOS_MYSTERY_TICKET" => relic.CombatsFinished >= 5 && !relic.GaveRelic,
                "DRIFTWOOD" or "PAELS_WING" or "WHITE_BEAST_STATUE" or "SOZU" or "BOWLER_HAT" or "ECTOPLASM" or
                "AMETHYST_AUBERGINE" or "WING_CHARM" or "FRESNEL_LENS" or "GLITTER" or "LAVA_LAMP" or
                "MOLTEN_EGG" or "TOXIC_EGG" or "FROZEN_EGG" or "SILKEN_TRESS" or "SILVER_CRUCIBLE" => true,
                _ => false
            }).Select(relic => Object(relic.Key)).Distinct().ToArray();

    private void CombatEditor(Context context, EditorParts parts, bool detailed)
    {
        var node = context.Node;
        var requirements = node.CombatRequirements!;
        var defaults = _session.Snapshot?.Run.CombatDefaults ?? new PredictorCombatDefaults();
        var combat = context.Plan.Combat ?? new();
        var cards = node.CombatCards.Where(c => c.IsKnown && c.Value.Id > 0 &&
            PredictorCombatConsequences.IsTriggeredSource(c.Value)).Select(c => c.Value).ToArray();
        foreach (var card in cards)
        {
            int count = combat.CardTriggers.FirstOrDefault(t => t.InstanceId == card.Id)?.EffectiveTriggers ?? defaults.CardTriggers(card.Key);
            string name = _names.Resolve(card.Key, GameContentKind.Card) + (card.UpgradeLevel > 0 ? "+" + card.UpgradeLevel : "");
            parts.Numbers.Add(new(Field(context, (c, v) =>
            {
                if (!Integer(v.Number, out int amount)) return false;
                var current = c.Plan.Combat ?? new();
                var triggers = current.CardTriggers.Where(t => t.InstanceId != card.Id).Append(new PredictorCardTrigger(card.Id, amount)).ToImmutableArray();
                return Set(c, c.Plan with { Combat = current with { CardTriggers = triggers } });
            }), name + " · " + R("triggers"), count, 0, int.MaxValue));
        }
        foreach (var relic in node.CombatRelics.Where(r => r.IsKnown && r.Value.Id > 0 && PredictorCombatDefaults.IsConfigurableRelic(r.Value.Key)).Select(r => r.Value))
        {
            bool enabled = combat.RelicEffects.FirstOrDefault(r => r.RelicId == relic.Id)?.EffectEnabled ?? defaults.RelicEffectEnabled(relic.Key);
            parts.Toggles.Add(new(Field(context, (c, v) =>
            {
                if (v.Toggle is not { } active) return false;
                var current = c.Plan.Combat ?? new();
                return Set(c, c.Plan with { Combat = current with { RelicEffects = current.RelicEffects.Where(r => r.RelicId != relic.Id)
                    .Append(new PredictorCombatRelicEffect(relic.Id, active)).ToImmutableArray() } });
            }), R("lava_active"), enabled));
        }
        foreach (var source in requirements.EnemyCreations)
        {
            var specified = combat.EnemyCreations.FirstOrDefault(f => f.Source == source);
            int? count = specified?.Count ?? defaults.EnemyCount(source);
            string label = R("enemy." + source);
            // Zero in an unsupplied field is an editing placeholder only. The
            // supplied toggle lets the player explicitly declare zero summons.
            parts.Numbers.Add(new(Field(context, (c, v) =>
            {
                if (!Integer(v.Number, out int amount)) return false;
                var current = c.Plan.Combat ?? new();
                return Set(c, c.Plan with { Combat = current with { EnemyCreations = current.EnemyCreations.Where(f => f.Source != source)
                    .Append(new PredictorEnemyCreationFact(source, amount)).ToImmutableArray() } });
            }), label, count ?? 0, 0, int.MaxValue));
            parts.Toggles.Add(new(Field(context, (c, v) =>
            {
                if (v.Toggle is not { } supplied) return false;
                var current = c.Plan.Combat ?? new();
                var facts = current.EnemyCreations.Where(f => f.Source != source).ToImmutableArray();
                if (supplied) facts = facts.Add(new(source, count ?? 0));
                return Set(c, c.Plan with { Combat = current with { EnemyCreations = facts } });
            }), label + " · " + R("combat_amount_confirmed"), specified != null));
            if (detailed && count == null) parts.Sections.Add(new(label,
                M("optional_niche_input"), [], []));
        }
        if (requirements.FirstPlayerTurn)
            parts.Toggles.Add(new(Field(context, (c, v) => v.Toggle is { } occurred &&
                Set(c, c.Plan with { Combat = (c.Plan.Combat ?? new()) with { FirstPlayerTurnOccurred = occurred } })),
                R("first_turn"), combat.FirstPlayerTurnOccurred ?? true));
        if (requirements.VictoryHealth)
            HealthFields(context, parts, "combat", (c, health) => Set(c,
                c.Plan with { Combat = (c.Plan.Combat ?? new()) with { VictoryHealth = health } }), combat.VictoryHealth);
        if (detailed && (node.CombatCards.Any(c => !c.IsKnown) || node.CombatRelics.Any(r => !r.IsKnown) ||
            requirements.StolenCards || requirements.GremlinGold || requirements.EventTimeout))
            parts.Sections.Add(new(R("extra_combat_facts"), R("combat_local_facts_pending"), [], []));
        if (context.Plan.Combat != null || parts.Numbers.Count != 0 || parts.Toggles.Count != 0 || parts.Selections.Count != 0)
            parts.Sections.Insert(0, new("", "", [],
                [Choice(context, R("combat_reset_defaults"), c =>
                {
                    foreach (string key in _health.Keys.Where(k => k.StartsWith(c.Node.Position.NodeId + "|combat|", StringComparison.Ordinal)).ToArray()) _health.Remove(key);
                    return Set(c, c.Plan with { Combat = null });
                })]));
    }
}
