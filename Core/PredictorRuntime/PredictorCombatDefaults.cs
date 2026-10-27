using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorCombatCardRule(ModelKey Card, int EffectiveTriggers);
internal sealed record PredictorCombatRelicRule(ModelKey Relic, bool EffectEnabled);
internal sealed record PredictorCombatRelicEffect(long RelicId, bool EffectEnabled);

// Saved preferences name models. Resolved facts name the instances that existed
// at this combat boundary; changing a preference never edits inventory.
internal sealed record PredictorCombatDefaults
{
    public ImmutableArray<PredictorCombatCardRule> CardRules { get; init; } = [];
    public ImmutableArray<PredictorCombatRelicRule> RelicRules { get; init; } = [];
    public ImmutableArray<PredictorEnemyCreationFact> EnemyRules { get; init; } = [];

    internal void Validate()
    {
        if (CardRules.IsDefault || RelicRules.IsDefault || EnemyRules.IsDefault ||
            CardRules.Any(r => r == null || !r.Card.IsValid || r.Card.Category != "CARD" || r.EffectiveTriggers < 0) ||
            RelicRules.Any(r => r == null || !IsConfigurableRelic(r.Relic)) ||
            EnemyRules.Any(r => r == null || !Enum.IsDefined(r.Source) || r.Count < 0) ||
            CardRules.Select(r => r.Card).Distinct().Count() != CardRules.Length ||
            RelicRules.Select(r => r.Relic).Distinct().Count() != RelicRules.Length ||
            EnemyRules.Select(r => r.Source).Distinct().Count() != EnemyRules.Length)
            throw new ArgumentException("PredictorCombatDefaultsInvalid");
    }

    internal int CardTriggers(ModelKey card) => CardRules.FirstOrDefault(r => r.Card == card)?.EffectiveTriggers ?? 1;
    internal bool RelicEffectEnabled(ModelKey relic) => RelicRules.FirstOrDefault(r => r.Relic == relic)?.EffectEnabled ?? true;
    internal static bool IsConfigurableRelic(ModelKey relic) => relic.Category == "RELIC" && relic.Entry == "LAVA_LAMP";

    // Owner-approved prediction premises, not observations or simulated enemy
    // actions. Fabricator's two different summon moves have no approved count.
    internal int? EnemyCount(PredictorEnemyCreationSource source) =>
        EnemyRules.FirstOrDefault(r => r.Source == source)?.Count ?? (source switch
        {
            PredictorEnemyCreationSource.FabricatorBots => (int?)null,
            PredictorEnemyCreationSource.FogmogIllusions or PredictorEnemyCreationSource.ObscuraIllusions or
                PredictorEnemyCreationSource.GasBombs or PredictorEnemyCreationSource.BackupRats => 1,
            PredictorEnemyCreationSource.LaidEggs or PredictorEnemyCreationSource.HatchedEggs => 3,
            PredictorEnemyCreationSource.Wrigglers => 4,
            PredictorEnemyCreationSource.ReplacementAxebots or PredictorEnemyCreationSource.SurpriseGremlins => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        });
}

// Partial instance overrides belong to one node. Missing enemy counts use the
// declared prediction defaults, while an unset Fabricator count stays missing.
internal sealed record PredictorCombatNodeOverrides
{
    public ImmutableArray<PredictorCardTrigger> CardTriggers { get; init; } = [];
    public ImmutableArray<PredictorCombatRelicEffect> RelicEffects { get; init; } = [];
    public ImmutableArray<PredictorTemporaryCardTrigger> TemporaryCardTriggers { get; init; } = [];
    public ImmutableArray<PredictorCombatCardSourceRef> CombatCardSourceOrder { get; init; } = [];
    public PredictorCombatPotionFacts? Potions { get; init; }
    public ImmutableArray<PredictorEnemyCreationFact> EnemyCreations { get; init; } = [];
    public PredictorStolenCardFacts? StolenCards { get; init; }
    public bool? FirstPlayerTurnOccurred { get; init; }
    public PredictorEventHealthFact? VictoryHealth { get; init; }
    public PredictorGremlinGoldFacts? GremlinGold { get; init; }
    public ImmutableArray<PredictorCombatExtraRewardRef> ExtraRewardOrder { get; init; } = [];
}

internal sealed record PredictorCombatDefaultResolution(PredictorCombatFacts Facts, PredictorCombatRequirements MissingFacts)
{
    public bool NeedsExtraRewardOrder { get; init; }
    internal ResolveCombat? Input => MissingFacts.NeedsDeclaration || NeedsExtraRewardOrder ? null : new(Facts);
}

internal static class PredictorCombatDefaultResolver
{
    internal static PredictorCombatDefaultResolution Resolve(PredictorState state, PredictorCombatRequirements requirements,
        PredictorCombatDefaults defaults, PredictorCombatNodeOverrides? node = null, PredictorContext? context = null)
    {
        defaults.Validate();
        node ??= new();
        var cards = state.Deck.Select(state.Card).Where(PredictorCombatConsequences.IsTriggeredSource).ToArray();
        var relics = state.Relics.Where(r => !r.Melted).ToArray();
        if (node.CardTriggers.IsDefault || node.RelicEffects.IsDefault || node.EnemyCreations.IsDefault || node.TemporaryCardTriggers.IsDefault ||
            node.TemporaryCardTriggers.Any(t => t == null) ||
            node.CardTriggers.Any(t => t == null || t.EffectiveTriggers < 0 || !cards.Any(c => c.Id == t.InstanceId)) ||
            node.CardTriggers.Select(t => t.InstanceId).Distinct().Count() != node.CardTriggers.Length ||
            node.RelicEffects.Any(r => r == null || !relics.Any(held => held.Id == r.RelicId && PredictorCombatDefaults.IsConfigurableRelic(held.Key))) ||
            node.RelicEffects.Select(r => r.RelicId).Distinct().Count() != node.RelicEffects.Length ||
            node.EnemyCreations.Any(f => f == null || f.Count < 0 || !requirements.EnemyCreations.Contains(f.Source)) ||
            node.EnemyCreations.Select(f => f.Source).Distinct().Count() != node.EnemyCreations.Length)
            throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.NodeOverridesInvalid,
                "PredictorCombatNodeOverridesInvalid");
        var effects = relics.Where(r => PredictorCombatDefaults.IsConfigurableRelic(r.Key)).Select(r => node.RelicEffects.FirstOrDefault(n => n.RelicId == r.Id) ??
            new PredictorCombatRelicEffect(r.Id, defaults.RelicEffectEnabled(r.Key))).ToImmutableArray();
        bool Enabled(PredictorRelic relic) => effects.Single(e => e.RelicId == relic.Id).EffectEnabled;
        var lamps = relics.Where(r => r.Key.Entry == "LAVA_LAMP").ToArray();
        var enemies = ImmutableArray.CreateBuilder<PredictorEnemyCreationFact>();
        foreach (var source in requirements.EnemyCreations)
        {
            var specified = node.EnemyCreations.FirstOrDefault(f => f.Source == source);
            if (specified != null) enemies.Add(specified);
            else if (defaults.EnemyCount(source) is { } count) enemies.Add(new(source, count));
        }
        var facts = new PredictorCombatFacts(AutomaticCardEffects: false)
        {
            CardTriggers = cards.Select(c => node.CardTriggers.FirstOrDefault(t => t.InstanceId == c.Id) ??
                new PredictorCardTrigger(c.Id, defaults.CardTriggers(c.Key))).ToImmutableArray(),
            RelicEffects = effects,
            TemporaryCardTriggers = node.TemporaryCardTriggers,
            CombatCardSourceOrder = node.CombatCardSourceOrder,
            // The public control describes the reward effect. Preserve the legacy
            // damage fact for settlement; per-instance gates handle mixed lamps.
            TookDamageThisCombat = lamps.Length > 0 && lamps.All(r => !Enabled(r)),
            Potions = node.Potions ?? PredictorCombatPotionFacts.NoAdditional,
            EnemyCreations = enemies.ToImmutable(),
            StolenCards = node.StolenCards,
            FirstPlayerTurnOccurred = node.FirstPlayerTurnOccurred ?? true,
            VictoryHealth = node.VictoryHealth,
            GremlinGold = node.GremlinGold ?? (requirements.GremlinGold
                ? new PredictorGremlinGoldFacts(false, true, null) { RecoverAllStolenGold = true } : null),
            ExtraRewardOrder = node.ExtraRewardOrder
        };
        var missing = requirements with
        {
            EnemyCreations = requirements.EnemyCreations.Where(s => !facts.EnemyCreations.Any(f => f.Source == s)).ToImmutableArray(),
            StolenCards = requirements.StolenCards && node.StolenCards == null,
            GremlinGold = requirements.GremlinGold && (facts.GremlinGold == null ||
                facts.GremlinGold.RecoverAllStolenGold && !PredictorCombatRewardFacts.CanBalanceRecoveredGold(context, state) ||
                facts.GremlinGold.GoldWasStolen && !PredictorCombatRewardFacts.HasHeistReward(facts.GremlinGold) && facts.GremlinGold.StolenGoldLoss == null),
            FirstPlayerTurn = false,
            VictoryHealth = requirements.VictoryHealth && node.VictoryHealth == null
        };
        int sources = facts.CardTriggers.Any(t => t.EffectiveTriggers > 0 && state.Card(t.InstanceId).Key.Entry == "THE_HUNT") ||
            facts.TemporaryCardTriggers.Any(t => t.EffectiveTriggers > 0 && t.Card.Entry == "THE_HUNT") ? 1 : 0;
        if (facts.StolenCards?.Cards.Any(c => c.ReturnedAsReward) == true) sources++;
        if (PredictorCombatRewardFacts.HasHeistReward(facts.GremlinGold)) sources++;
        return new(facts, missing) { NeedsExtraRewardOrder = sources > 1 && facts.ExtraRewardOrder.IsEmpty };
    }
}
