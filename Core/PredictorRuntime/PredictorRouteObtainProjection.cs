using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record LiveObtainDeckEffect(PredictorDeckProjectionScope Scope,
        PredictorDeckUncertainFact Facts, int Count, bool Niche = false, bool AddsCards = false);

    // These existing handlers have only card-target effects. Sources with
    // independent health/death, money choices or stored-card Rewards draws
    // retain their concrete contracts (in particular Pael's Tooth).
    private static LiveObtainDeckEffect? LiveTargetObtainEffect(ModelKey source) => source.Entry switch
    {
        "POMANDER" => new(PredictorDeckProjectionScope.Upgradable, PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other, 1),
        "YUMMY_COOKIE" => new(PredictorDeckProjectionScope.Upgradable, PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other, 4),
        "TRI_BOOMERANG" or "GNARLED_HAMMER" => new(PredictorDeckProjectionScope.EnchantableAttack, PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other, 3),
        "ELECTRIC_SHRYMP" => new(PredictorDeckProjectionScope.EnchantableSkill, PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other, 1),
        "PUNCH_DAGGER" => new(PredictorDeckProjectionScope.EnchantableAttack, PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other, 1),
        "PAELS_GROWTH" => new(PredictorDeckProjectionScope.Enchantable, PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other, 1),
        "KIFUDA" => new(PredictorDeckProjectionScope.Enchantable, PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other, 3),
        "ROYAL_STAMP" => new(PredictorDeckProjectionScope.Enchantable, PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other, 1, Niche: true),
        "ASTROLABE" => new(PredictorDeckProjectionScope.Transformable, PredictorDeckUncertainFact.All, 3, Niche: true, AddsCards: true),
        "NEW_LEAF" => new(PredictorDeckProjectionScope.Transformable, PredictorDeckUncertainFact.All, 1, Niche: true, AddsCards: true),
        "PRECISE_SCISSORS" => new(PredictorDeckProjectionScope.Removable, PredictorDeckUncertainFact.Membership, 1),
        "BIIIG_HUG" => new(PredictorDeckProjectionScope.Removable, PredictorDeckUncertainFact.Membership, 4),
        "EMPTY_CAGE" => new(PredictorDeckProjectionScope.Removable, PredictorDeckUncertainFact.Membership, 2),
        "PRESERVED_FOG" => new(PredictorDeckProjectionScope.Removable, PredictorDeckUncertainFact.Membership, 3),
        _ => null
    };

    private IEnumerable<PredictorPlannedObtainChild> LiveObtainTargets(ModelKey source, PredictorOptionalObtainAddress address)
    {
        string node = _routeNode?.NodeId ?? Working.Position.NodeId;
        var plan = _preview.Settings.Plans.FirstOrDefault(plan => plan.NodeId == node);
        if (plan == null) yield break;
        // Legacy children belong to their particular parent transaction and
        // carry no receipt address. Do not mine another parent's same-model
        // target. Their unchanged intent remains pending in the projection;
        // the fully authored simulator still resolves it through that parent.
        foreach (var optional in plan.ObtainInputs.Where(optional =>
            SameOptionalObtainAddress(optional.Address, address) && optional.Input.ExpectedSource == source))
            yield return optional.Input;
    }

    private PredictorPlannedObtainChild? LiveObtainTarget(ModelKey source, LiveObtainDeckEffect effect, PredictorOptionalObtainAddress address)
    {
        if (_preview.Partial == null || effect.Niche && source.Entry != "ROYAL_STAMP" &&
            !_preview.Partial.StreamKnown(PredictorStream.Niche)) return null;
        var projection = _preview.Partial.Deck.Project(effect.Scope);
        int maximum = projection.KnownCount is { } count ? (int)Math.Min(count, effect.Count) : effect.Count;
        if (source.Entry == "ROYAL_STAMP" && projection.OrderedCandidatesKnown)
            maximum = Math.Min(effect.Count, projection.KnownCandidates.Count(card => card.Type is EffectCardType.Attack or EffectCardType.Skill));
        return LiveObtainTargets(source, address).FirstOrDefault(input =>
            input.Action == (source.Entry == "NEW_LEAF" ? PredictorObtainChildAction.TransformCard : PredictorObtainChildAction.ChooseDeckCards) &&
            (input.DeckCards.Length == maximum || source.Entry is "GNARLED_HAMMER" or "KIFUDA" && input.DeckCards.Length <= maximum) &&
            input.DeckCards.All(target => Working.Deck.Contains(target.InstanceId) &&
                Working.Card(target.InstanceId) is { } card && card.Key == target.Key && card.FloorAdded == target.FloorAdded &&
                (source.Entry != "ROYAL_STAMP" || card.Type is RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack or RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill) &&
                _preview.Partial.Deck.ReadTarget(target.InstanceId, effect.Scope, RouteTargetRequiredFacts(card,
                    effect.AddsCards || effect.Facts == PredictorDeckUncertainFact.Membership
                        ? PredictorDeckUncertainFact.Definition : RouteLevelEditFacts)) != null));
    }

    private void RecordLiveObtainInput(ModelKey source, LiveObtainDeckEffect effect, PredictorOptionalObtainAddress address)
    {
        string node = _routeNode?.NodeId ?? Working.Position.NodeId;
        var projection = _preview.Partial!.Deck.Project(effect.Scope);
        var cards = projection.KnownCandidates.Where(card => source.Entry != "ROYAL_STAMP" ||
            card.Type is RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack or RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill)
            .Select(card => new PredictorRouteCardKnowledge(card, _preview.Partial.ProjectionCauses(projection with
            {
                Gaps = _preview.Partial.Deck.TargetGaps(card.Id, effect.Scope, RouteTargetRequiredFacts(card,
                    effect.AddsCards || effect.Facts == PredictorDeckUncertainFact.Membership
                        ? PredictorDeckUncertainFact.Definition : RouteLevelEditFacts))
            }, count: false))).ToImmutableArray();
        bool optional = source.Entry is "GNARLED_HAMMER" or "KIFUDA";
        int maximum = projection.KnownCount is { } count ? (int)Math.Min(count, effect.Count) : effect.Count;
        if (source.Entry == "ROYAL_STAMP" && projection.OrderedCandidatesKnown) maximum = Math.Min(effect.Count, cards.Length);
        var knowledge = new PredictorObtainChoiceKnowledge(source.Entry == "NEW_LEAF" ? PredictorPhase.TransformCards : PredictorPhase.ObtainDeckCards,
            source, cards, [], [], [], []) { MinimumCards = optional ? 0 : maximum, MaximumCards = maximum };
        _preview.Obtains.OptionalInputs.Add((node, new(address, knowledge)));
    }

    private static bool SameOptionalObtainAddress(PredictorOptionalObtainAddress first, PredictorOptionalObtainAddress second) =>
        first.Destination == second.Destination && first.SourceId == second.SourceId && first.Occurrence == second.Occurrence &&
        first.ParentSourceIds.SequenceEqual(second.ParentSourceIds);

    internal static PredictorPlannedNode ApplyOptionalObtainInput(PredictorPlannedNode plan,
        PredictorOptionalObtainInput optional, PredictorPlannedObtainChild input)
    {
        if (input.ExpectedSource != optional.Choice.Source) throw new ArgumentException("PredictorOptionalObtainSourceMismatch");
        ValidatePlannedObtainChild(input);
        int index = plan.ObtainInputs.FindIndex(prior => prior.Address.Occurrence == optional.Address.Occurrence);
        var updated = new PredictorPlannedOptionalObtain(optional.Address, input);
        return plan with { ObtainInputs = index < 0 ? plan.ObtainInputs.Add(updated) : plan.ObtainInputs.SetItem(index, updated) };
    }

    private void ApplyLiveObtainTarget(ModelKey source, LiveObtainDeckEffect effect, PredictorPlannedObtainChild input)
    {
        var ids = input.DeckCards.Select(target => target.InstanceId).ToImmutableArray();
        if (source.Entry == "NEW_LEAF")
        {
            var transformed = PredictorCardChanges.Transform(Context, Working, ids, PredictorStream.Niche);
            Working = transformed.State;
            Accept(new("NewLeafTransform", ids[0], transformed.Changes.Select(change => change.ReplacementId).ToImmutableArray(), null, null)
                { Transformations = transformed.Changes });
            return;
        }
        if (source.Entry == "ROYAL_STAMP")
            Working = PredictorObtainSources.RoyalStampCandidates(Working,
                Working.Deck.Where(id => PredictorObtainSources.CanEnchant(Context, Working.Card(id)) &&
                    Working.Card(id).Type is RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack or RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Skill).ToImmutableArray()).State;
        var previous = (_obtainDeckSource, _obtainDeckOperation, _obtainDeckPending);
        _obtainDeckSource = source;
        _obtainDeckOperation = source.Entry == "ASTROLABE" ? PredictorObtainDeckOperation.Astrolabe :
            effect.Facts == PredictorDeckUncertainFact.Membership ? PredictorObtainDeckOperation.Remove :
            (effect.Facts & PredictorDeckUncertainFact.Upgrade) != 0 ? PredictorObtainDeckOperation.Upgrade : PredictorObtainDeckOperation.Enchant;
        _obtainDeckPending = new(PredictorObtainReturn.Opening, Working.Relics[^1].Id);
        try { ApplyObtainDeckChoice(ids); }
        finally { (_obtainDeckSource, _obtainDeckOperation, _obtainDeckPending) = previous; }
    }

    private bool IsProjectedLiveObtainTarget(PredictorPlannedObtainChild input) =>
        _preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
        input.Action is PredictorObtainChildAction.ChooseDeckCards or PredictorObtainChildAction.TransformCard &&
        (_preview.Obtains.Projected.Contains((Working.Position.NodeId, input.ExpectedSource)) ||
            _routeNode is { } node && _preview.Obtains.Projected.Contains((node.NodeId, input.ExpectedSource)));

    private sealed partial class RoutePartialState
    {
        internal PredictorState RecordLiveTargetObtain(PredictorState state, ModelKey source, LiveObtainDeckEffect effect)
        {
            Synchronize(state);
            string node = state.Position.NodeId;
            string operation = "live-obtain-target:" + source.Serialized + ":" + state.Relics[^1].Id;
            var projection = Deck.Project(effect.Scope);
            if (projection.KnownCount == 0) return state;
            if (source.Entry == "ROYAL_STAMP" && projection.OrderedCandidatesKnown &&
                !projection.KnownCandidates.Any(card => card.Type is EffectCardType.Attack or EffectCardType.Skill)) return state;
            long? count = projection.KnownCount is { } candidates ? Math.Min(candidates, effect.Count) : null;
            var parents = ProjectionCauses(projection, count: false);
            var domains = PredictorCombatDomain.Deck;
            if ((effect.Facts & PredictorDeckUncertainFact.Membership) != 0 &&
                state.Deck.Any(id => state.Card(id).Key.Entry == "SPOILS_MAP")) domains |= PredictorCombatDomain.Map;
            if (effect.AddsCards) domains |= PredictorCombatDomain.InstanceIds;
            var cause = PredictorRouteCauseEvidence.CreateExplicit("RouteObtainTargetPending", node, domains,
                PredictorPendingReasonKind.UserChoice,
                "The relic was acquired; its card targets remain unspecified or depend on earlier card changes. Independent rewards continue.",
                ["choice.obtain." + source.Entry + ".targets"], parents, occurrenceKey: operation);
            ImmutableArray<EffectCardType> types = effect.Scope switch
            {
                PredictorDeckProjectionScope.EnchantableAttack => [EffectCardType.Attack],
                PredictorDeckProjectionScope.EnchantableSkill => [EffectCardType.Skill],
                _ => source.Entry == "ROYAL_STAMP" ? [EffectCardType.Attack, EffectCardType.Skill] : []
            };
            var possibleCards = state.Deck.Select(state.Card).Where(card => types.IsEmpty || types.Contains(card.Type)).ToImmutableArray();
            var mutationFacts = effect.Facts;
            if ((mutationFacts & PredictorDeckUncertainFact.Upgrade) != 0 && possibleCards.Any(card =>
                PredictorCardChanges.Definition(_context, card.Key).Levels.Select(level => level.Eternal).Distinct().Count() > 1))
                mutationFacts |= PredictorDeckUncertainFact.Removable;
            if (effect.AddsCards)
            {
                // Replacement identities can be ordinary cards absent from the
                // incoming deck. Retain those readers (Hunt, Royalties, etc.)
                // without inventing a particular replacement in the witness.
                var catalog = _context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
                var definitions = catalog.CardDefinitions.ToDictionary(definition => definition.Prototype.Key);
                var originals = possibleCards.Concat(Deck.Choices.SelectMany(choice => choice.Effects).SelectMany(value => value.AddedCards))
                    .Concat(_dependencies.Where(dependency => (dependency.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
                        (dependency.DeckFacts & (PredictorDeckUncertainFact.Membership | PredictorDeckUncertainFact.Definition)) != 0)
                        .SelectMany(dependency => dependency.PossibleCards.IsDefault ? [] : dependency.PossibleCards))
                    .Where(card => card.Removable && !definitions[card.Key].IsQuest).DistinctBy(card => card.Key).ToArray();
                var replacements = originals.SelectMany(original =>
                {
                    var definition = definitions[original.Key];
                    return catalog.TransformationPools.Single(pool => pool.Key == definition.TransformationPool).Cards
                        .Where(key => key != original.Key && (definition.Rarity is "Curse" or "Status" ||
                            definitions[key].Rarity is "Common" or "Uncommon" or "Rare"))
                        .Select(key => definitions[key].Prototype);
                });
                possibleCards = possibleCards.AddRange(replacements).Distinct().ToImmutableArray();
            }
            AddDependency(_dependencies, new(cause, possibleCards, MayChangeExistingCards: true)
            {
                DeckFacts = mutationFacts, AffectedCardTypes = types,
                ChangedCardMembershipEntries = (effect.Facts & PredictorDeckUncertainFact.Membership) == 0 ? [] : default
            });
            if (effect.Niche)
            {
                var rngCause = cause with { AffectedDomains = PredictorCombatDomain.NicheRng };
                AddDependency(_dependencies, new(rngCause));
            }
            if (effect.AddsCards && count != 0)
            {
                bool copies = PredictorSettlementEffects.Has(state, "BING_BONG") || _context.HasRunModifier("HOARDER");
                if (PredictorSettlementEffects.Has(state, "LUCKY_FYSH"))
                {
                    if (count is { } known && !copies)
                        for (long i = 0; i < known; i++) state = PredictorSettlementEffects.TakeGold(state, 15);
                    else AddDependency(_dependencies, new(cause with { AffectedDomains = PredictorCombatDomain.Gold }));
                }
                // The receipt callbacks are real even when card identity is
                // unknown. Their card/health counters are not made-up values.
                if (PredictorSettlementEffects.Has(state, "BOOK_OF_FIVE_RINGS"))
                {
                    if (count is { } known && !copies)
                        state = state with { Relics = state.Relics.Select(relic => relic.Key.Entry == "BOOK_OF_FIVE_RINGS" && !relic.Melted
                            ? relic with { BookOfFiveRingsCardsAdded = checked(relic.BookOfFiveRingsCardsAdded + (int)known) } : relic).ToImmutableArray() };
                    AddDependency(_dependencies, new(cause with { AffectedDomains = PredictorCombatDomain.Health }));
                }
                if (PredictorSettlementEffects.Has(state, "DARKSTONE_PERIAPT"))
                    AddDependency(_dependencies, new(cause with { AffectedDomains = PredictorCombatDomain.Health }));
            }
            Synchronize(state);
            return state;
        }
    }
}
