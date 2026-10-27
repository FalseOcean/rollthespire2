using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorCardTrigger(long InstanceId, int EffectiveTriggers);
internal sealed record PredictorTemporaryCardTrigger(ModelKey Card, int UpgradeLevel, int EffectiveTriggers);
internal sealed record PredictorCombatCardSourceRef(long? DeckCardId = null, int? TemporarySourceIndex = null);
internal sealed record PredictorCombatFacts(bool AutomaticCardEffects = true)
{
    public ImmutableArray<PredictorCardTrigger> CardTriggers { get; init; } = [];
    public ImmutableArray<PredictorCombatRelicEffect> RelicEffects { get; init; } = [];
    internal bool RelicEffectEnabled(PredictorRelic relic) =>
        RelicEffects.FirstOrDefault(r => r.RelicId == relic.Id)?.EffectEnabled ?? true;
    public ImmutableArray<PredictorTemporaryCardTrigger> TemporaryCardTriggers { get; init; } = [];
    // Relevant effect occurrences only. Required when a temporary source is
    // declared; each Deck/temporary reference repeats EffectiveTriggers times.
    public ImmutableArray<PredictorCombatCardSourceRef> CombatCardSourceOrder { get; init; } = [];
    // Approved prediction premise: no qualifying Lava Lamp damage unless corrected.
    public bool TookDamageThisCombat { get; init; }
    public PredictorCombatPotionFacts? Potions { get; init; }
    public ImmutableArray<PredictorEnemyCreationFact> EnemyCreations { get; init; } = [];
    public PredictorStolenCardFacts? StolenCards { get; init; }
    public bool? FirstPlayerTurnOccurred { get; init; }
    // Local HP after displayed AfterCombatEnd effects, before VictoryEarly heals.
    public PredictorEventHealthFact? VictoryHealth { get; init; }
    public PredictorGremlinGoldFacts? GremlinGold { get; init; }
    public ImmutableArray<PredictorCombatExtraRewardRef> ExtraRewardOrder { get; init; } = [];
}
internal sealed record PredictorStolenCardFact(long CardId, bool ReturnedAsReward);
internal sealed record PredictorStolenCardFacts(ImmutableArray<PredictorStolenCardFact> Cards);
// These are visible source outcomes, not RNG offsets or a combat turn log.
internal enum PredictorEnemyCreationSource { FabricatorBots, FogmogIllusions, ObscuraIllusions, GasBombs, LaidEggs, HatchedEggs, BackupRats, Wrigglers, ReplacementAxebots, SurpriseGremlins }
internal sealed record PredictorEnemyCreationFact(PredictorEnemyCreationSource Source, int Count);

internal static class PredictorCombatEnemyFacts
{
    internal static ImmutableArray<PredictorEnemyCreationSource> Sources(IEnumerable<ModelKey> monsters) => monsters.SelectMany(m => m.Entry switch
    {
        "FABRICATOR" => new[] { PredictorEnemyCreationSource.FabricatorBots },
        "FOGMOG" => [PredictorEnemyCreationSource.FogmogIllusions],
        "THE_OBSCURA" => [PredictorEnemyCreationSource.ObscuraIllusions],
        "LIVING_FOG" => [PredictorEnemyCreationSource.GasBombs],
        "OVICOPTER" => [PredictorEnemyCreationSource.LaidEggs, PredictorEnemyCreationSource.HatchedEggs],
        "TOUGH_EGG" => [PredictorEnemyCreationSource.HatchedEggs],
        "TWO_TAILED_RAT" => [PredictorEnemyCreationSource.BackupRats],
        "PHROG_PARASITE" => [PredictorEnemyCreationSource.Wrigglers],
        "AXEBOT" => [PredictorEnemyCreationSource.ReplacementAxebots],
        "GREMLIN_MERC" => [PredictorEnemyCreationSource.SurpriseGremlins],
        _ => Array.Empty<PredictorEnemyCreationSource>()
    }).Distinct().ToImmutableArray();

    internal static bool Valid(PredictorEncounterDefinition encounter, ImmutableArray<PredictorEnemyCreationFact> facts) =>
        !facts.IsDefault && facts.Length == encounter.EnemyCreationSources.Length &&
        facts.All(f => f != null) && facts.Select(f => f.Source).Distinct().Count() == facts.Length &&
        facts.All(f => f != null && f.Count >= 0 && encounter.EnemyCreationSources.Contains(f.Source));

    internal static PredictorState Apply(PredictorState state, ImmutableArray<PredictorEnemyCreationFact> facts, CancellationToken token)
    {
        if (facts.IsDefault || facts.Any(f => f == null || f.Count < 0 || !Enum.IsDefined(f.Source)))
            throw new ArgumentException("PredictorCombatEnemyFactsInvalid");
        // All these concrete CreateCreature/Hatch paths consume exactly one Niche
        // draw per actual creature/hatch. Initial enemies were already consumed.
        foreach (var fact in facts)
            for (int i = 0; i < fact.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                state = state.Draw(PredictorStream.Niche, 0, 1).State;
            }
        return state;
    }
}
internal sealed record PredictorHealthConsequence(long SourceCardId, int MaxHpDelta,
    int? TemporarySourceIndex = null);

internal static class PredictorCombatConsequences
{
    internal static bool IsTriggeredSource(PredictorCard c) => c.Key.Entry is "THE_HUNT" or "ROYALTIES" or "HAND_OF_GREED" or
        "FEED" or "GENETIC_ALGORITHM" or "THE_SCYTHE" or "ALCHEMIZE" or "BRIGHTEST_FLAME" or "FORBIDDEN_GRIMOIRE" || c.Enchantment?.Entry == "GOOPY";

    internal static (PredictorState State, ImmutableArray<PredictorRewardPlan> Extras,
        ImmutableArray<PredictorCardTrigger> Triggers, ImmutableArray<PredictorPotion> Potions,
        ImmutableArray<PredictorHealthConsequence> Health, ImmutableArray<PredictorPotionAttempt> PotionAttempts) Apply(PredictorContext context, PredictorState state,
        PredictorCombatKind room, PredictorCombatFacts facts, CancellationToken cancellationToken = default)
    {
        // Entry is after currently implemented entrance processing and before the
        // unsimulated fight. Reward takes below cannot enter this frozen snapshot.
        var sources = state.Deck.Select(state.Card).Where(IsTriggeredSource).ToArray();
        if (facts.RelicEffects.IsDefault || facts.RelicEffects.Any(r => r == null ||
                !state.Relics.Any(held => held.Id == r.RelicId && PredictorCombatDefaults.IsConfigurableRelic(held.Key))) ||
            facts.RelicEffects.Select(r => r.RelicId).Distinct().Count() != facts.RelicEffects.Length)
            throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.RelicFactsInvalid,
                "PredictorCombatRelicFactsInvalid");
        var triggers = facts.AutomaticCardEffects ? sources.Select(c => new PredictorCardTrigger(c.Id, 1)).ToImmutableArray() : facts.CardTriggers;
        if (triggers.IsDefault || (facts.AutomaticCardEffects && !facts.CardTriggers.IsEmpty) ||
            triggers.Length != sources.Length || triggers.Any(t => t == null) || triggers.Select(t => t.InstanceId).Distinct().Count() != triggers.Length ||
            triggers.Any(t => t.EffectiveTriggers < 0 || !sources.Any(c => c.Id == t.InstanceId)))
            throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.FactsMustReplaceAllRelevantInstances,
                "PredictorCombatFactsMustReplaceAllRelevantInstances");
        var temporary = facts.TemporaryCardTriggers;
        if (temporary.IsDefault || facts.CombatCardSourceOrder.IsDefault ||
            facts.AutomaticCardEffects && !temporary.IsEmpty ||
            temporary.Any(t => t == null || t.Card.Category != "CARD" || t.UpgradeLevel < 0 ||
                t.EffectiveTriggers < 0 || !IsTemporarySource(t.Card) ||
                !(context.Catalog?.CardDefinitions.Any(d => d.Prototype.Key == t.Card &&
                    t.UpgradeLevel <= d.Prototype.MaxUpgradeLevel) ?? false)))
            throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.TemporaryCardFactsInvalid,
                "PredictorTemporaryCombatCardFactsInvalid");
        ImmutableArray<PredictorCombatCardSourceRef> sourceOrder;
        if (temporary.IsEmpty)
        {
            if (!facts.CombatCardSourceOrder.IsEmpty) throw new ArgumentException("PredictorCombatCardSourceOrderWithoutTemporary");
            sourceOrder = triggers.SelectMany(t => Enumerable.Repeat(new PredictorCombatCardSourceRef(DeckCardId: t.InstanceId),
                t.EffectiveTriggers)).ToImmutableArray();
        }
        else
        {
            sourceOrder = facts.CombatCardSourceOrder;
            if (sourceOrder.Length != triggers.Sum(t => t.EffectiveTriggers) + temporary.Sum(t => t.EffectiveTriggers) ||
                sourceOrder.Any(r => r == null || r.DeckCardId.HasValue == r.TemporarySourceIndex.HasValue ||
                    r.DeckCardId is { } deckId && !triggers.Any(t => t.InstanceId == deckId) ||
                    r.TemporarySourceIndex is { } i && (i < 0 || i >= temporary.Length)) ||
                triggers.Any(t => sourceOrder.Count(r => r.DeckCardId == t.InstanceId) != t.EffectiveTriggers) ||
                Enumerable.Range(0, temporary.Length).Any(i =>
                    sourceOrder.Count(r => r.TemporarySourceIndex == i) != temporary[i].EffectiveTriggers))
                throw new PredictorCombatPlanBindingException(PredictorCombatPlanBindingReason.CardSourceOrderInvalid,
                    "PredictorCombatCardSourceOrderInvalid");
        }
        var extras = ImmutableArray.CreateBuilder<PredictorRewardPlan>();
        var potions = ImmutableArray.CreateBuilder<PredictorPotion>();
        var potionAttempts = ImmutableArray.CreateBuilder<PredictorPotionAttempt>();
        var potionFacts = facts.Potions ?? PredictorCombatPotionFacts.NoAdditional;
        PredictorCombatPotions.Validate(potionFacts, triggers, temporary);
        var health = ImmutableArray.CreateBuilder<PredictorHealthConsequence>();
        int royalties = 0;
        var deckOccurrences = triggers.ToDictionary(t => t.InstanceId, _ => 0);
        var temporaryOccurrences = new int[temporary.Length];
        foreach (var source in sourceOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? temporaryIndex = source.TemporarySourceIndex;
            var card = source.DeckCardId is { } id ? state.Card(id) : null;
            var cardKey = card?.Key ?? temporary[temporaryIndex!.Value].Card;
            int upgrade = card?.UpgradeLevel ?? temporary[temporaryIndex!.Value].UpgradeLevel;
            int occurrence = source.DeckCardId is { } deckId
                ? deckOccurrences[deckId]++ : temporaryOccurrences[temporaryIndex!.Value]++;
            state = PredictorCombatPotions.Apply(context, state,
                potionFacts.Changes.Where(p => source.DeckCardId is { } sourceId
                    ? p.BeforeCardId == sourceId && p.BeforeOccurrence == occurrence
                    : p.BeforeTemporarySourceIndex == temporaryIndex && p.BeforeOccurrence == occurrence),
                potions, potionAttempts);
            switch (cardKey.Entry)
            {
                case "THE_HUNT":
                    extras.Add(new(PredictorRewardKind.Card, Odds: room switch { PredictorCombatKind.Elite => PredictorCardOdds.Elite,
                        PredictorCombatKind.Boss => PredictorCardOdds.Boss, _ => PredictorCardOdds.Regular }, EncounterSource: true));
                    break;
                case "ROYALTIES": royalties = checked(royalties + (upgrade > 0 ? 40 : 30)); break;
                case "HAND_OF_GREED": state = PredictorSettlementEffects.TakeGold(state, upgrade > 0 ? 25 : 20); break;
                case "FEED": health.Add(new(card?.Id ?? 0, upgrade > 0 ? 4 : 3, temporaryIndex)); break;
                case "BRIGHTEST_FLAME": health.Add(new(card?.Id ?? 0, -2, temporaryIndex)); break;
                case "GENETIC_ALGORITHM":
                    if (card == null) throw new InvalidOperationException("PredictorTemporaryGrowthSourceInvalid");
                    card = card with { IncreasedBlock = checked(card.IncreasedBlock + (upgrade > 0 ? 4 : 3)) };
                    state = state.WithCard(card); break;
                case "THE_SCYTHE":
                    if (card == null) throw new InvalidOperationException("PredictorTemporaryGrowthSourceInvalid");
                    card = card with { IncreasedDamage = checked(card.IncreasedDamage + (upgrade > 0 ? 7 : 5)) };
                    state = state.WithCard(card); break;
                case "ALCHEMIZE":
                    var key = PredictorRewardGeneration.GeneratePotions(context, ref state, 1, PredictorStream.CombatPotionGeneration, inCombat: true)[0];
                    var result = PredictorPotionEffects.Procure(state, key, card?.Id ?? 0, "AutomaticOrDeclaredCardTrigger");
                    state = result.State; potions.Add(result.Attempt.Potion);
                    potionAttempts.Add(result.Attempt with { TemporarySourceIndex = temporaryIndex });
                    break;
                case "FORBIDDEN_GRIMOIRE":
                    extras.Add(new(PredictorRewardKind.RemoveCard));
                    break;
            }
            if (card?.Enchantment?.Entry == "GOOPY")
            {
                card = card with { EnchantmentAmount = checked(card.EnchantmentAmount + 1) };
                state = state.WithCard(card);
            }
        }
        // RoyaltiesPower is one counter for this owner, appended at AfterCombatEnd.
        // It is an ORIGINAL prefilled reward, so its Populate still draws once.
        if (royalties > 0) extras.Add(new(PredictorRewardKind.Gold, royalties, royalties, PrefilledGold: true));
        state = PredictorCombatPotions.Apply(context, state, potionFacts.Changes.Where(p =>
            p.BeforeCardId == null && p.BeforeTemporarySourceIndex == null), potions, potionAttempts);
        return (state, extras.ToImmutable(), triggers, potions.ToImmutable(), health.ToImmutable(), potionAttempts.ToImmutable());
    }

    internal static bool IsTemporarySource(ModelKey key) => key.Entry is "THE_HUNT" or "ROYALTIES" or
        "HAND_OF_GREED" or "FEED" or "ALCHEMIZE" or "BRIGHTEST_FLAME" or "FORBIDDEN_GRIMOIRE";
}
