using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // This capability splits the real mandatory receipt from its final curse.
    // It never permits an exact consumer to execute an unknown-cursor witness.
    private bool CanProjectLiveCurses() =>
        _preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults && _preview.Partial != null;

    private sealed partial class RoutePartialState
    {
        internal PredictorState RecordLiveCurseReceipts(PredictorRun run, long sourceId, bool cape)
        {
            var state = run.Working;
            Synchronize(state);
            var catalog = _context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
            var keys = catalog.UnlockedCurseCards;
            if (keys.IsDefaultOrEmpty) throw new InvalidOperationException("PredictorBonesCursePoolMissing");
            var possible = keys.Select(key => PredictorCardChanges.Definition(_context, key).Prototype).ToImmutableArray();
            string node = state.Position.NodeId;
            int draws = cape ? 2 : 1;
            if (keys.Length < draws) throw new InvalidOperationException("PredictorCursePoolDepleted");
            var stream = _context.NonCombatStream(PredictorStream.Niche);
            var parents = StreamCauses(stream);
            string source = cape ? "cape" : "bones";
            string code = cape ? "CapeCurseIdentitiesPending" : "BonesCurseIdentityPending";
            var hookCauses = RouteLiveObtainRelicCauses(run, new("RELIC", cape ? "DISTINGUISHED_CAPE" : "NEOWS_BONES"), _dependencies);
            bool hooksKnown = hookCauses.IsEmpty;
            var cause = PredictorRouteCauseEvidence.CreateExplicit(code, node,
                PredictorCombatDomain.Deck | (hooksKnown ? PredictorCombatDomain.None : PredictorCombatDomain.InstanceIds),
                PredictorPendingReasonKind.FlowGate,
                cape ? "The Cape adds two distinct curses sampled without replacement before its three Apparitions." :
                    "Both Bones relics were acquired. The final curse is received with an unknown identity.",
                ["deck." + source + ".curse"], parents, occurrenceKey: source + "-curse:" + sourceId);
            _choices[cause.Identity] = cause;
            _opaqueDeck.Add(cause.Identity);
            int receipts = draws * (1 + state.Relics.Count(relic => !relic.Melted && relic.Key.Entry == "BING_BONG") +
                (_context.HasRunModifier("HOARDER") ? 2 : 0));
            // Cape entry already checked receipt listeners. Every original and
            // each copy occupies one id, independent of which curse was drawn.
            state = state with { NextInstanceId = checked(state.NextInstanceId + (hooksKnown ? receipts : draws)) };
            Deck = Deck.MarkUnknownAddition(node, code, cause.Identity,
                possible.Select(card => card.Type).Distinct().ToImmutableArray(), receiptEffectsSettled: hooksKnown,
                excludesBasicCards: possible.All(card => PredictorCardChanges.Definition(_context, card.Key).Rarity != "Basic"),
                knownCount: hooksKnown ? receipts : null);
            AddDependency(_dependencies, new(cause, possible)
            {
                DeckFacts = PredictorDeckUncertainFact.Membership,
                ChangedCardMembershipEntries = possible.Select(card => card.Key.Entry).Distinct().ToImmutableArray(),
                ChangedCardStateEntries = []
            });
            // NextItem consumes one draw regardless of which card it returns.
            // Record that real consumption without reading or advancing the
            // private numerical stream's unknown representative state.
            _streamOffsets[stream] += draws;
            Facts.RecordResult(source + ".curse." + stream + ".consumed", node, draws,
                operation: "One NextItem draw per mandatory curse");
            Facts.RecordResult(source + ".curse.receivedCount", node, receipts,
                hookCauses,
                operation: "Mandatory curse receipts including nonrecursive Bing Bong/Hoarder copies");
            bool allCurses = possible.All(card => card.Type == EffectCardType.Curse);
            int healthStart = state.CurrentHealthEffects.Length;
            if (hooksKnown)
            {
                for (int receipt = 0; receipt < receipts; receipt++)
                    foreach (var relic in state.Relics.Where(relic => !relic.Melted).ToArray())
                    {
                        if (relic.Key.Entry == "LUCKY_FYSH") state = PredictorSettlementEffects.TakeGold(state, 15);
                        if (relic.Key.Entry == "DARKSTONE_PERIAPT" && allCurses)
                            state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 6, 6)) };
                        if (relic.Key.Entry == "BOOK_OF_FIVE_RINGS")
                        {
                            int index = state.Relics.FindIndex(item => item.Id == relic.Id);
                            int added = checked(state.Relics[index].BookOfFiveRingsCardsAdded + 1);
                            state = state with { Relics = state.Relics.SetItem(index, state.Relics[index] with { BookOfFiveRingsCardsAdded = added }) };
                            if (added % 5 == 0) state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(relic.Key, relic.Id, 0, 20)) };
                        }
                    }
                if (!allCurses && possible.Any(card => card.Type == EffectCardType.Curse) && PredictorSettlementEffects.Has(state, "DARKSTONE_PERIAPT"))
                    AddDependency(_dependencies, new(cause with { AffectedDomains = PredictorCombatDomain.Health }));
            }
            else AddDependency(_dependencies, new(cause with { AffectedDomains = PredictorCombatDomain.Gold |
                PredictorCombatDomain.Health | PredictorCombatDomain.Relics })
            { ChangedRelicEntries = [], ChangedRelicStateEntries = ["BOOK_OF_FIVE_RINGS", "BING_BONG"] });
            var healthCauses = hooksKnown && (allCurses || !PredictorSettlementEffects.Has(state, "DARKSTONE_PERIAPT"))
                ? ImmutableArray<PredictorCombatPendingCause>.Empty : [cause];
            Facts.RecordResult(source + ".curse.maxHpDelta", node, state.CurrentHealthEffects.Skip(healthStart).Sum(effect => effect.MaxHpDelta),
                healthCauses, operation: "Actual curse receipt health callbacks");
            Synchronize(state);
            return state;
        }
    }
}
