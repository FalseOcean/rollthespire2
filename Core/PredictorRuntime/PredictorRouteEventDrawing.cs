using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{

    private bool TryBeginDrawingBridgeSummary(PredictorPosition position, ModelKey key)
    {
        bool summarize = _preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
            key.Entry == "SLIPPERY_BRIDGE" && _preview.Event.BridgeEntryPending;
        _preview.Event.BridgeEntryPending = false;
        _preview.Event.BridgeEntrySummarized = summarize;
        if (!summarize) return false;
        // FinishEventOccurrence still owns queue bookkeeping and room/travel
        // hooks. No creature/card/RNG witness is substituted for the missing
        // population and no native bridge target is generated here.
        _eventKey = key;
        _eventBranch = null;
        _eventRng = null;
        _eventHealth = null;
        _eventRewardContinuation = false;
        _eventCardsRequired = 0;
        _eventCardCandidates = [];
        _preview.Event.ProjectHealthIndependently = false;
        _preview.Event.PendingHealthCommands = [];
        _preview.Event.UnknownHealingAmount = false;
        ResetEventChoiceState(bathsDamage: 3, resetAssignedGold: true);
        Working = Working with { Position = position };
        _inputs = [];
        Rewards = [];
        EventOptions = [];
        Phase = PredictorPhase.EventChoice;
        _requestId++;
        if (_preview.Partial is { } partial)
            partial.Facts.RecordResult("event.default.bridgeEntrySummary", position.NodeId, key.Serialized,
                CausesFor(partial.EventTraceDependencies, PredictorCombatDomain.Deck),
                origin: PredictorKnowledgeOrigin.ApprovedDefault,
                operation: "The selected queue occurrence is retained; missing bridge targets affect only its zero-public-consumption card/health loop.");
        return true;
    }

    private static bool DrawingEventBusinessChoiceRequired(PredictorRun run,
        ImmutableArray<PredictorEventOptionKnowledge> options)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults)
            return PredictorRouteEventPolicy.For(run._eventKey).BranchRequired;
        if (DrawingEventConfirmation(run) != null) return false;
        if (PredictorRouteEventPolicy.For(run._eventKey).BranchRequired) return true;
        var outcomes = run._preview.Event.OptionOutcomes;
        if (outcomes.Length < 2) return false;
        var first = outcomes[0].State;
        // Equal consumption is a fact about actual handler executions, not an
        // assumption that an unselected option consumes no random numbers.
        return outcomes.Any(outcome => first.Streams.Any(stream => stream.Stream != PredictorStream.Niche &&
                outcome.State.Streams.Single(value => value.Stream == stream.Stream) != stream));
    }

    private static string? DrawingEventConfirmation(PredictorRun run)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            run.Phase != PredictorPhase.EventChoice || run.EventNeedsHealthFact ||
            !run._nodeReplayInputs.Any(input => input is ChooseEventOption) ||
            run.EventOptions.Length != 1 || !run.EventOptions[0].Enabled) return null;
        string option = run.EventOptions[0].Key;
        return (run._eventKey?.Entry, option) is
            ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") or
            ("PUNCH_OFF", "FIGHT") or ("THE_LANTERN_KEY", "FIGHT") or
            ("DENSE_VEGETATION", "FIGHT") or ("RELIC_TRADER", "DONE") or
            ("WAR_HISTORIAN_REPY", "SECOND_UNLOCK_CAGE" or "SECOND_UNLOCK_CHEST") ? option : null;
    }

    private static void ContinueDrawingEventConfirmation(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (local.Count != 0 || DrawingEventConfirmation(run) is not { } key) return;
        var option = ObserveRouteEventOptions(run, dependencies, knownInstanceIds).Single();
        if (!option.IsKnown || !RouteEventActionReadCauses(run, run.EventOptions[0], dependencies, knownInstanceIds).IsEmpty)
            return;
        run._preview.Partial?.Facts.RecordResult("event.default.confirmation", run.Working.Position.NodeId, key,
            origin: PredictorKnowledgeOrigin.ApprovedDefault,
            operation: "The adopted business branch has one deterministic confirmation; execute its real successor without a player input.");
        // Submit would invent a saved player action. The handler still records
        // its actual effects, while the trace above owns the approved default.
        if (run.TrySubmitEvent(new ChooseEventOption(key), token) != PredictorInputResult.Accepted)
            throw new InvalidOperationException("PredictorDrawingEventConfirmationRejected");
        ProjectRouteEventHealthIndependentResult(run, dependencies, knownInstanceIds, local, token);
        PropagateRouteEventHealthPending(run, dependencies);
    }

    private static bool ContinueDrawingLowValueEvent(PredictorRun run,
        List<RouteDependency> dependencies, List<PredictorCombatPendingCause> local)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            run.Phase != PredictorPhase.EventChoice || run._eventKey is not { } key ||
            local.Count != 1 || local[0].Code.StartsWith("Planned", StringComparison.Ordinal) ||
            key.Entry is not ("AROMA_OF_CHAOS" or "SYMBIOTE" or "AMALGAMATOR" or
                "ABYSSAL_BATHS" or "REFLECTIONS" or "SAPPHIRE_SEED" or "SELF_HELP_BOOK" or
                "SPIRALING_WHIRLPOOL" or "TABLET_OF_TRUTH" or "WOOD_CARVINGS" or
                "FIELD_OF_MAN_SIZED_HOLES" or "DOORS_OF_LIGHT_AND_DARK" or "SLIPPERY_BRIDGE")) return false;
        if (DrawingEventBusinessChoiceRequired(run, [])) return false;
        // These native event families have zero public-stream consumption in
        // every branch, including their repeat pages. Preserve the entry draws
        // already executed, discard only their event-local continuation, and
        // retain the real unselected deck/health effect instead of picking Exit.
        var existing = run.Working.Deck.Select(run.Working.Card).ToImmutableArray();
        var cards = existing;
        bool addsCards = key.Entry is "AROMA_OF_CHAOS" or "SYMBIOTE" or "AMALGAMATOR" or
            "REFLECTIONS" or "WOOD_CARVINGS" or "FIELD_OF_MAN_SIZED_HOLES";
        bool membership = addsCards || key.Entry is "DOORS_OF_LIGHT_AND_DARK" or "SLIPPERY_BRIDGE";
        if (key.Entry is "AROMA_OF_CHAOS" or "SYMBIOTE")
        {
            var pools = existing.Select(card => PredictorCardChanges.Definition(run.Context, card.Key).TransformationPool).ToHashSet();
            var keys = run.Context.Catalog!.TransformationPools.Where(pool => pools.Contains(pool.Key))
                .SelectMany(pool => pool.Cards).ToHashSet();
            cards = cards.AddRange(run.Context.Catalog.CardDefinitions.Where(definition => keys.Contains(definition.Prototype.Key))
                .Select(definition => definition.Prototype)).Distinct().ToImmutableArray();
        }
        string[] fixedAdditions = key.Entry switch
        {
            "AMALGAMATOR" => ["ULTIMATE_STRIKE", "ULTIMATE_DEFEND"],
            "WOOD_CARVINGS" => ["PECK", "TORIC_TOUGHNESS"],
            "FIELD_OF_MAN_SIZED_HOLES" => ["NORMALITY"],
            "REFLECTIONS" => ["BAD_LUCK"],
            _ => []
        };
        cards = cards.AddRange(fixedAdditions.Select(entry => PredictorCardChanges.Definition(run.Context, new("CARD", entry)).Prototype));
        var domains = key.Entry == "ABYSSAL_BATHS" ? PredictorCombatDomain.None : PredictorCombatDomain.Deck;
        if (key.Entry is "ABYSSAL_BATHS" or "SAPPHIRE_SEED" or
            "SPIRALING_WHIRLPOOL" or "TABLET_OF_TRUTH" or "SLIPPERY_BRIDGE") domains |= PredictorCombatDomain.Health;
        if (addsCards) domains |= PredictorCombatDomain.InstanceIds;
        if (addsCards && (PredictorSettlementEffects.Has(run.Working, "BING_BONG") ||
            PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS"))) domains |= PredictorCombatDomain.Relics;
        if (addsCards && (PredictorSettlementEffects.Has(run.Working, "DARKSTONE_PERIAPT") ||
            PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS"))) domains |= PredictorCombatDomain.Health;
        if (membership && existing.Any(card => card.Key.Entry == "SPOILS_MAP")) domains |= PredictorCombatDomain.Gold;
        var cause = local[0] with
        {
            AffectedDomains = domains,
            SourceEvidence = "Drawing omits this event's card/health choices. Its native branches and repeat pages consume no public RNG; only their actual state dependents remain unresolved."
        };
        if (run._preview.Event.BridgeEntrySummarized)
            cause = cause with { ReasonKind = PredictorPendingReasonKind.DerivedDependency,
                RequiredPropertyKeys = ["deck.Removable.candidates"],
                Parents = PredictorRouteCauseEvidence.Normalize(cause.Parents.AddRange(CausesFor(dependencies, PredictorCombatDomain.Deck))) };
        var facts = key.Entry switch
        {
            "ABYSSAL_BATHS" => PredictorDeckUncertainFact.None,
            "SELF_HELP_BOOK" or "SPIRALING_WHIRLPOOL" => PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other,
            "TABLET_OF_TRUTH" => PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other,
            _ => membership ? PredictorDeckUncertainFact.All : PredictorDeckUncertainFact.Upgrade |
                PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other
        };
        var effect = new PendingRouteEventCardEffect(cards, facts == PredictorDeckUncertainFact.None ? [] : existing,
            domains, facts, membership, addsCards, 0, 0) { MayEditExistingCards = facts != PredictorDeckUncertainFact.None };
        run._preview.Event.PendingCardEffect = effect;
        run._preview.Partial?.PendingEventCards(run, cause, effect);
        AddDependency(dependencies, EventCardEffectDependency(cause, effect));
        run._preview.Partial?.Facts.RecordResult("event.default.lowValueChoicesOmitted", run.Working.Position.NodeId,
            key.Serialized, [cause], origin: PredictorKnowledgeOrigin.ApprovedDefault,
            operation: "Zero public consumption; no business option or target has been selected.");
        run.FinishEvent();
        local.Clear();
        return true;
    }
}
