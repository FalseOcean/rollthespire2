using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // This capability splits the real mandatory receipt from its final curse.
    // It never permits an exact consumer to execute an unknown-cursor witness.
    private bool CanProjectLiveBonesCurse() =>
        _preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults && _preview.Partial != null;

    private sealed partial class RoutePartialState
    {
        internal PredictorState RecordLiveBonesCurse(PredictorState state, long sourceId)
        {
            Synchronize(state);
            var catalog = _context.Catalog ?? throw new InvalidOperationException("PredictorCatalogMissing");
            var keys = catalog.UnlockedCurseCards;
            if (keys.IsDefaultOrEmpty) throw new InvalidOperationException("PredictorBonesCursePoolMissing");
            var possible = keys.Select(key => PredictorCardChanges.Definition(_context, key).Prototype).ToImmutableArray();
            string node = state.Position.NodeId;
            var parents = StreamCauses(PredictorStream.Niche);
            var cause = PredictorRouteCauseEvidence.CreateExplicit("BonesCurseIdentityPending", node,
                PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds, PredictorPendingReasonKind.FlowGate,
                "Both Bones relics were acquired. Its mandatory final card is received without inventing its unknown Niche identity.",
                ["deck.bones.curse"], parents, occurrenceKey: "bones-curse:" + sourceId);
            _choices[cause.Identity] = cause;
            _opaqueDeck.Add(cause.Identity);
            bool hooksKnown = Domain(PredictorCombatDomain.Relics).IsEmpty;
            int receipts = 1 + state.Relics.Count(relic => !relic.Melted && relic.Key.Entry == "BING_BONG") +
                (_context.HasRunModifier("HOARDER") ? 2 : 0);
            Deck = Deck.MarkUnknownAddition(node, "BonesCurseIdentityNotKnown", cause.Identity,
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
            _streamOffsets[PredictorStream.Niche] += 1;
            Facts.RecordResult("bones.curse.Niche.consumed", node, 1,
                operation: "BonesCurse: one NextItem draw; identity remains unknown");
            Facts.RecordResult("bones.curse.receivedCount", node, receipts,
                hooksKnown ? [] : Domain(PredictorCombatDomain.Relics),
                operation: "BonesCurse: mandatory original plus nonrecursive Bing Bong/Hoarder copies");
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
            Facts.RecordResult("bones.curse.maxHpDelta", node, state.CurrentHealthEffects.Skip(healthStart).Sum(effect => effect.MaxHpDelta),
                healthCauses, operation: "BonesCurse: actual receipt health callbacks");
            Synchronize(state);
            return state;
        }
    }
}
