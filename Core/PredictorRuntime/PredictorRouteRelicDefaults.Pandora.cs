using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed partial class RoutePartialState
    {
        internal PredictorState RecordLivePandoraObtain(PredictorState state)
        {
            Synchronize(state);
            var strikes = Deck.Project(PredictorDeckProjectionScope.BasicStrike);
            var defends = Deck.Project(PredictorDeckProjectionScope.BasicDefend);
            // No targets means no transformation, receipt callbacks or Niche
            // consumption. Acquiring the box itself is still a real receipt.
            if (strikes.KnownCount == 0 && defends.KnownCount == 0) return state;

            var basics = strikes.KnownCandidates.Concat(defends.KnownCandidates).DistinctBy(card => card.Id).ToArray();
            var readable = basics.Where(card => Deck.ReadTarget(card.Id, PredictorDeckProjectionScope.Basic,
                PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Removable) != null).ToArray();
            var targets = readable.Where(card => card.Removable).ToArray();
            bool countKnown = strikes.OrderedCandidatesKnown && defends.OrderedCandidatesKnown && readable.Length == basics.Length;
            if (countKnown && targets.Length == 0) return state;

            string node = state.Position.NodeId;
            string operation = "live-pandora:" + state.Relics[^1].Id;
            var parents = PredictorRouteCauseEvidence.Normalize(ProjectionCauses(strikes, false)
                .AddRange(ProjectionCauses(defends, false)).AddRange(ProjectionCauses(strikes with { Sources = [],
                    Gaps = basics.SelectMany(card => Deck.TargetGaps(card.Id, PredictorDeckProjectionScope.Basic,
                        PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Removable)).Distinct().ToImmutableArray() }, false)));
            var possibleOriginals = (countKnown ? targets : state.Deck.Select(state.Card)
                .Concat(_dependencies.Where(dependency => (dependency.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0)
                    .SelectMany(dependency => dependency.PossibleCards.IsDefault ? [] : dependency.PossibleCards))
                .Where(card => PredictorCardChanges.Definition(_context, card.Key) is { Rarity: "Basic" } definition &&
                    (definition.IsStrike || definition.IsDefend))).DistinctBy(card => (card.Id, card.Key)).ToArray();
            var catalog = _context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
            var definitions = catalog.CardDefinitions.ToDictionary(definition => definition.Prototype.Key);
            // Match Transform's pool/rarity exclusion, including the actual
            // source character. No representative replacement is generated.
            var possible = possibleOriginals.SelectMany(original =>
            {
                var options = catalog.TransformationPools.Single(pool => pool.Key == definitions[original.Key].TransformationPool).Cards
                    .Where(key => key != original.Key && (definitions[key].Rarity is "Common" or "Uncommon" or "Rare"))
                    .Select(key => definitions[key].Prototype).ToArray();
                if (options.Length == 0) throw new InvalidOperationException("PredictorTransformPoolDepleted");
                return options;
            }).DistinctBy(card => card.Key).ToImmutableArray();

            var identity = PredictorRouteCauseEvidence.CreateExplicit("PandoraTransformIdentityPending", node,
                PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds, PredictorPendingReasonKind.FlowGate,
                "Pandora removes the proven basic targets and receives replacement cards; their random identities remain unspecified.",
                ["deck.pandora.replacements"], parents, occurrenceKey: operation);
            _choices[identity.Identity] = identity;
            // Remove only proven targets. Ordinary synchronization owns the
            // actual fixed-member removal; no fake replacement IDs are allocated.
            var removed = targets.Select(card => card.Id).ToHashSet();
            state = state with { Deck = state.Deck.Where(id => !removed.Contains(id)).ToImmutableArray(),
                Cards = state.Cards.Select(card => removed.Contains(card.Id) ? card with { Removed = true, SpoilsCoord = null } : card).ToImmutableArray() };
            Synchronize(state);
            Deck = Deck.MarkUnknownAddition(node, "PandoraReplacementIdentityNotKnown", identity.Identity,
                possible.Select(card => card.Type).Distinct().ToImmutableArray(), receiptEffectsSettled: countKnown,
                excludesBasicCards: possible.All(card => definitions[card.Key].Rarity != "Basic"));
            _opaqueDeck.Add(identity.Identity);
            if (!countKnown)
            {
                _deckOperations[(node, operation)] = identity;
                Deck = Deck.MarkExistingChangePending(node, "PandoraTargetPopulationNotKnown", PredictorDeckUncertainFact.Membership,
                    instanceIds: possibleOriginals.Where(card => card.Id > 0 && !removed.Contains(card.Id)).Select(card => card.Id).ToImmutableArray(),
                    sourceOperation: operation);
            }
            AddDependency(_dependencies, new(identity, possibleOriginals.Concat(possible).ToImmutableArray(), MayChangeExistingCards: !countKnown)
            {
                DeckFacts = PredictorDeckUncertainFact.Membership,
                ChangedCardMembershipEntries = possibleOriginals.Concat(possible).Select(card => card.Key.Entry).Distinct().ToImmutableArray(),
                ChangedCardStateEntries = [], AffectedCardIds = countKnown ? [] : possibleOriginals.Select(card => card.Id).Where(id => id > 0).ToImmutableArray()
            });

            AddStreamGap(PredictorStream.Niche, node, "PandoraTransformConsumptionPending", [identity], operation);

            // Each original replacement is received once. Every Bing Bong
            // listener adds one suppressed clone; Hoarder adds two. Suppressed
            // clones still invoke Gold/Book/health callbacks, but never recurse.
            int copies = 1 + state.Relics.Count(relic => !relic.Melted && relic.Key.Entry == "BING_BONG") +
                (_context.HasRunModifier("HOARDER") ? 2 : 0);
            int receipts = checked(targets.Length * copies);
            for (int receipt = 0; receipt < receipts; receipt++)
            {
                foreach (var relic in state.Relics.Where(relic => !relic.Melted).ToArray())
                {
                    if (relic.Key.Entry == "LUCKY_FYSH") state = PredictorSettlementEffects.TakeGold(state, 15);
                    if (relic.Key.Entry == "BOOK_OF_FIVE_RINGS")
                    {
                        int index = state.Relics.FindIndex(listener => listener.Id == relic.Id);
                        int added = checked(state.Relics[index].BookOfFiveRingsCardsAdded + 1);
                        state = state with { Relics = state.Relics.SetItem(index, state.Relics[index] with { BookOfFiveRingsCardsAdded = added }) };
                        if (added % 5 == 0) state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 0, 20)) };
                    }
                }
            }
            if (!countKnown)
            {
                var domains = PredictorCombatDomain.None;
                if (PredictorSettlementEffects.Has(state, "LUCKY_FYSH")) domains |= PredictorCombatDomain.Gold;
                if (PredictorSettlementEffects.Has(state, "BOOK_OF_FIVE_RINGS")) domains |= PredictorCombatDomain.Relics | PredictorCombatDomain.Health;
                if (PredictorSettlementEffects.Has(state, "DRAGON_FRUIT") && PredictorSettlementEffects.Has(state, "LUCKY_FYSH")) domains |= PredictorCombatDomain.Health;
                if (domains != PredictorCombatDomain.None) AddDependency(_dependencies, new(identity with { AffectedDomains = domains })
                    { ChangedRelicEntries = [], ChangedRelicStateEntries = ["BOOK_OF_FIVE_RINGS"] });
            }
            // The transform pool normally excludes curses. Keep a genuine
            // identity-sensitive max-HP callback unknown if content admits one.
            if (possible.Any(card => card.Type == EffectCardType.Curse) && PredictorSettlementEffects.Has(state, "DARKSTONE_PERIAPT"))
                AddDependency(_dependencies, new(identity with { AffectedDomains = PredictorCombatDomain.Health }));
            Synchronize(state);
            return state;
        }
    }
}
