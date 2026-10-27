using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Live-map product boundary, not an operation-simulator limitation. Deck
    // target details are optional in a drawing; receipts and independent gold
    // or reward generation remain real operations.
    private static bool HasLiveDeckRandomObtainBoundary(PredictorRun run, ModelKey relic) =>
        run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults && run._preview.Partial != null &&
        (relic.Entry is "WHETSTONE" or "WAR_PAINT" or "SAND_CASTLE" or "FRAGRANT_MUSHROOM" or
            "BEAUTIFUL_BRACELET" or "PANDORAS_BOX" or "PAELS_TOOTH" ||
            LiveTargetObtainEffect(relic) != null);

    private bool ApplyLiveDeckRandomObtainBoundary(ModelKey relic, PredictorObtainReturn destination, long sourceId)
    {
        if (_preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || _preview.Partial == null) return false;
        var address = RegisterLiveObtainAddress(relic, destination, sourceId);
        if (!HasLiveDeckRandomObtainBoundary(this, relic)) return false;
        // Its real health boundary lets the exact parent consume an explicit
        // Health child first. The shared live completion projects it otherwise.
        if (relic.Entry is "FRAGRANT_MUSHROOM" or "PAELS_TOOTH") return false;
        // Called only after actual detached acquisition. Keep the inventory and
        // bag effects, without choosing representative upgrades/transforms.
        Working = Working with { PersonalBag = Working.PersonalBag.Remove(relic), SharedBag = Working.SharedBag.Remove(relic) };
        if (LiveTargetObtainEffect(relic) is { } targetEffect)
        {
            _preview.Obtains.Projected.Add((_routeNode?.NodeId ?? Working.Position.NodeId, relic));
            RecordLiveObtainInput(relic, targetEffect, address);
            if (LiveObtainTarget(relic, targetEffect, address) is { } input)
            {
                ApplyLiveObtainTarget(relic, targetEffect, input);
                _preview.Partial!.Synchronize(Working);
                return true;
            }
            Working = _preview.Partial!.RecordLiveTargetObtain(Working, relic, targetEffect);
            // This handler adds Folly after its removal regardless of which
            // cards were removed. Its real receipt/gold hooks remain active.
            if (relic.Entry == "PRESERVED_FOG")
                Working = PredictorObtainSources.AddCard(Context, Working, new ModelKey("CARD", "FOLLY"));
            return true;
        }
        _preview.Partial!.RecordLiveDeckRandomObtain(Working, relic);
        return true;
    }

    private sealed partial class RoutePartialState
    {
        internal void RecordLiveDeckRandomObtain(PredictorState state, ModelKey relic)
        {
            Synchronize(state);
            string node = state.Position.NodeId;
            string operation = "live-obtain:" + relic.Serialized + ":" + state.Relics[^1].Id;
            var stream = PredictorRouteCauseEvidence.CreateExplicit("RouteRelicDeckRandomnessCutoff", node,
                PredictorCombatDomain.NicheRng, PredictorPendingReasonKind.FlowGate,
                "Approved current-Act path policy: " + relic.Serialized +
                " was acquired; its deck-dependent obtain consumption is not projected. Resume from a new actual capture.",
                ["rng.Niche.state"], occurrenceKey: operation);
            _streamCauses[PredictorStream.Niche] = PredictorRouteCauseEvidence.Normalize(
                _streamCauses.GetValueOrDefault(PredictorStream.Niche, []).Add(stream));
            AddDependency(_dependencies, new(stream));
            var unknown = Facts.RecordUnknown("rng.Niche.state", node, PredictorRouteFactPhase.After,
                [stream], origin: PredictorKnowledgeOrigin.ApprovedDefault, operation: operation);
            RecordStateWrite(node, "rng.Niche.state", unknown);

            var facts = relic.Entry == "BEAUTIFUL_BRACELET" ? PredictorDeckUncertainFact.Enchantment :
                relic.Entry == "PANDORAS_BOX" ? PredictorDeckUncertainFact.All : PredictorDeckUncertainFact.Upgrade;
            ImmutableArray<EffectCardType> types = relic.Entry switch
            { "WHETSTONE" => [EffectCardType.Attack], "WAR_PAINT" => [EffectCardType.Skill], _ => [] };
            if (facts == PredictorDeckUncertainFact.Upgrade)
            {
                facts |= PredictorDeckUncertainFact.Other;
                if (state.Deck.Select(state.Card).Concat(Deck.Choices.SelectMany(c => c.Effects).SelectMany(e => e.AddedCards))
                    .Where(c => types.IsEmpty || types.Contains(c.Type)).Any(c => PredictorCardChanges.Definition(_context, c.Key)
                        .Levels.Select(l => l.Eternal).Distinct().Count() > 1)) facts |= PredictorDeckUncertainFact.Removable;
            }
            var deckCause = PredictorRouteCauseEvidence.CreateExplicit("RouteRelicDeckEffectPending", node,
                PredictorCombatDomain.Deck | (relic.Entry == "PANDORAS_BOX" ? PredictorCombatDomain.InstanceIds : 0),
                PredictorPendingReasonKind.DerivedDependency,
                "The acquired relic's random card changes are intentionally left unresolved by the path policy.",
                ["deck." + (facts == PredictorDeckUncertainFact.Upgrade ? "upgrades" : "state")], [stream], occurrenceKey: operation);
            _opaqueDeck.Add(deckCause.Identity);
            _deckOperations[(node, operation)] = deckCause;
            Deck = Deck.MarkExistingChangePending(node, deckCause.SourceEvidence, facts, cardTypes: types, sourceOperation: operation);
            AddDependency(_dependencies, new(deckCause,
                state.Deck.Select(state.Card).Where(card => types.IsEmpty || types.Contains(card.Type)).ToImmutableArray(),
                MayChangeExistingCards: true)
            { DeckFacts = facts, AffectedCardTypes = types,
                ChangedCardMembershipEntries = relic.Entry == "PANDORAS_BOX" ? default : [],
                ChangedCardStateEntries = state.Deck.Select(state.Card).Where(c => types.IsEmpty || types.Contains(c.Type))
                    .Select(c => c.Key.Entry).Distinct().ToImmutableArray() });
            _observed = state;
        }
    }
}
