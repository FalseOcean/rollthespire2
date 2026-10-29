using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // The registration and event-stage read contracts live beside one another.
    // Concrete effects remain in PredictorEvents/shared obtain and reward code.
    // These inputs are computations, not a duplicate effects/read-write catalog.
    private static EventAppearanceResult EvaluateEventAppearance(PredictorRun run, ModelKey key,
        List<RouteDependency> dependencies)
    {
        var inputs = new EventStateInputs(run, key, dependencies, "appearance");
        bool health = LiveEventAssumesHealth(key), deck = LiveEventAssumesDeck(key);
        if (health) inputs.Read("health.admissionAssumed", true, origin: PredictorKnowledgeOrigin.ApprovedDefault,
            operation: "Owner route convention: event health admission");
        if (deck) inputs.Read("deck.admissionAssumed", true, origin: PredictorKnowledgeOrigin.ApprovedDefault,
            operation: "Owner route convention: ordinary event deck admission");
        bool? Gold(int required) => EventStateInputs.Known(inputs.GoldAtLeast(required));
        bool? Potions(int required) => EventStateInputs.Known(inputs.PotionsAtLeast(required));
        bool? Tradable(int required) => EventStateInputs.Known(inputs.TradableAtLeast(required));
        bool? Floor(bool allowed, string predicate) => EventStateInputs.Known(inputs.Read("position.floor",
            allowed, operation: "IsAllowed: actual floor " + predicate, sourceProperties: ["position.floor"]));
        bool? allowed = key.Entry switch
        {
            "MORPHIC_GROVE" or "CRYSTAL_SPHERE" or "WELCOME_TO_WONGOS" => Gold(100),
            "WHISPERING_HOLLOW" => Gold(44),
            "ENDLESS_CONVEYOR" => Gold(120),
            "TEA_MASTER" => Gold(150),
            "ZEN_WEAVER" => Gold(125),
            "WATERLOGGED_SCRIPTORIUM" => Gold(55),
            "PUNCH_OFF" => Floor(run.Working.Position.Floor >= 6, ">= 6"),
            "SLIPPERY_BRIDGE" => Floor(run.Working.Position.Floor > 6, "> 6"),
            "THE_FUTURE_OF_POTIONS" => Potions(2),
            "STONE_OF_ALL_TIME" => Potions(1),
            "RANWID_THE_ELDER" => EventStateInputs.And(Gold(100), Potions(1), Tradable(1)),
            "RELIC_TRADER" => Tradable(5),
            "FAKE_MERCHANT" => EventStateInputs.Or(Gold(100),
                EventStateInputs.Known(inputs.PotionMember("FOUL_POTION"))),
            "BYRDONIS_NEST" => !EventStateInputs.Or(EventStateInputs.Known(inputs.EventPetMember()),
                EventStateInputs.Known(inputs.CardMember("BYRDONIS_EGG"))),
            "LUMINOUS_CHOIR" => EventStateInputs.And(Gold(149), EventStateInputs.Known(inputs.AvailableRelic())),
            _ => true
        };
        return inputs.Appearance(allowed, health, deck);
    }

    private static bool CanEnterPlannedRouteEvent(PredictorRun run, ModelKey key, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local)
    {
        if (!run.RequireLocalEventPotionRarities(key, local)) return false;
        string? code = !IsModeledRouteEvent(key) ? "EventNotModeled" :
            key.Entry == "WELCOME_TO_WONGOS" && run.Context.AccountWongoPoints == null ? "EventAccountPointsRequired" : null;
        var inputs = new EventStateInputs(run, key, dependencies, "entry");
        EventStateInput<bool>? prerequisite = key.Entry switch
        {
            "THE_FUTURE_OF_POTIONS" => inputs.PotionsAtLeast(2),
            "STONE_OF_ALL_TIME" => inputs.PotionsAtLeast(1),
            "SLIPPERY_BRIDGE" => inputs.DeckAtLeast(PredictorDeckProjectionScope.Removable, 1),
            _ => null
        };
        // The drawing has fixed this event's identity/queue, but an earlier
        // omitted card edit need not furnish a concrete removable target.
        // Bridge's entire local loop consumes no public stream. Its dedicated
        // entry summary runs instead of the native empty-candidate handler.
        run._preview.Event.BridgeEntryPending = run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
            key.Entry == "SLIPPERY_BRIDGE" && prerequisite is { IsKnown: false };
        if (run._preview.Event.BridgeEntryPending && code == null) return true;
        if (prerequisite is { IsKnown: false }) code ??= "EventEntryDependent";
        else if (prerequisite is { Value: false }) code ??= key.Entry switch
        {
            "THE_FUTURE_OF_POTIONS" => "EventEntryTwoPotionsRequired",
            "STONE_OF_ALL_TIME" => "EventEntryPotionRequired",
            _ => "EventEntryRemovableCardRequired"
        };
        if (code == null) return true;
        local.Add(PredictorRouteCauseEvidence.CreateExplicit(code, run.Working.Position.NodeId,
            PredictorCombatDomain.Continuation, code == "EventEntryDependent" ? PredictorPendingReasonKind.DerivedDependency :
                code == "EventAccountPointsRequired" ? PredictorPendingReasonKind.MissingObservation :
                code == "EventNotModeled" ? PredictorPendingReasonKind.ImplementationGap : PredictorPendingReasonKind.KnownUnavailable,
            "Event entry prerequisite read by its concrete handler: " + (prerequisite?.Operation ?? code),
            prerequisite == null ? [code == "EventAccountPointsRequired" ? "account.wongoPoints" : "event.model"] :
                [prerequisite.PropertyKey],
            code == "EventEntryDependent" ? prerequisite!.PendingCauses : [], occurrenceKey: key.Serialized));
        return false;
    }

    private static bool IsModeledRouteEvent(ModelKey key) => key.Entry is
        "AROMA_OF_CHAOS" or "MORPHIC_GROVE" or "WHISPERING_HOLLOW" or "TRASH_HEAP" or "SYMBIOTE" or
        "TINKER_TIME" or "COLORFUL_PHILOSOPHERS" or "TRIAL" or "THIS_OR_THAT" or "UNREST_SITE" or
        "SAPPHIRE_SEED" or "ZEN_WEAVER" or "POTION_COURIER" or "REFLECTIONS" or "WELLSPRING" or
        "SLIPPERY_BRIDGE" or "TEA_MASTER" or "THE_FUTURE_OF_POTIONS" or "STONE_OF_ALL_TIME" or
        "SUNKEN_STATUE" or "FIELD_OF_MAN_SIZED_HOLES" or "ROUND_TEA_PARTY" or "ABYSSAL_BATHS" or
        "DROWNING_BEACON" or "TABLET_OF_TRUTH" or "LUMINOUS_CHOIR" or "LOST_WISP" or "AMALGAMATOR" or
        "COLOSSAL_FLOWER" or "BUGSLAYER" or "SUNKEN_TREASURY" or "RANWID_THE_ELDER" or "WOOD_CARVINGS" or
        "RELIC_TRADER" or "DENSE_VEGETATION" or "SELF_HELP_BOOK" or "SPIRALING_WHIRLPOOL" or
        "DOORS_OF_LIGHT_AND_DARK" or "PUNCH_OFF" or "BATTLEWORN_DUMMY" or "FAKE_MERCHANT" or
        "THE_LANTERN_KEY" or "GRAVE_OF_THE_FORGOTTEN" or "ROOM_FULL_OF_CHEESE" or "WATERLOGGED_SCRIPTORIUM" or
        "THE_LEGENDS_WERE_TRUE" or "BRAIN_LEECH" or "HUNGRY_FOR_MUSHROOMS" or "BYRDONIS_NEST" or "DOLL_ROOM" or
        "INFESTED_AUTOMATON" or "SPIRIT_GRAFTER" or "ENDLESS_CONVEYOR" or "JUNGLE_MAZE_ADVENTURE" or
        "WELCOME_TO_WONGOS" or "WAR_HISTORIAN_REPY" or "CRYSTAL_SPHERE";

    private static ImmutableArray<PredictorEventOptionKnowledge> ObserveRouteEventOptions(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds) => run.EventOptions.Select(option =>
    {
        var inputs = new EventStateInputs(run, run._eventKey!.Value, dependencies, "option");
        if (run._eventPotionInput is { } potionInput)
            inputs.Read("potions.localHypothesis", potionInput.OrderedSlots.IsDefault ?
                "emptySlots=" + potionInput.EmptySlots : string.Join(" | ", potionInput.OrderedSlots.Select(key => key?.Serialized ?? "empty")),
                origin: PredictorKnowledgeOrigin.PlayerInput, operation: "Event-local potion hypothesis; no route inventory identity is asserted");
        var identityDomains = RouteEventOptionIdentityDomains(run, option);
        // A future allocation cursor does not rename an already carried object.
        // Inventory/membership reads below still prove that the bound object is
        // present and is the one selected by this event.
        if ((option.PotionId != null || option.RelicId != null) &&
            new[] { option.PotionId, option.RelicId, option.CardTargetId }
                .All(id => id == null || knownInstanceIds.Contains(id.Value)))
            identityDomains &= ~PredictorCombatDomain.InstanceIds;
        if (run._eventKey?.Entry == "SLIPPERY_BRIDGE" && option.CardTargetId is { } knownBridgeTarget &&
            knownInstanceIds.Contains(knownBridgeTarget))
            identityDomains &= ~PredictorCombatDomain.InstanceIds;
        var identity = CausesFor(dependencies, identityDomains);
        if (run._eventKey?.Entry == "SLIPPERY_BRIDGE")
            identity = PredictorRouteCauseEvidence.Normalize(identity.AddRange(
                RouteBridgeIdentityCauses(run, dependencies, knownInstanceIds)));
        var identityInput = inputs.Read("options." + option.Key + ".identity",
            option.Key + "; goldCost=" + option.GoldCost + "; goldGain=" + option.GoldGain +
            "; card=" + option.CardTargetId + "; potion=" + option.PotionId + "; localPotion=" + option.LocalPotionOrdinal + ":" + option.LocalPotionKey?.Serialized + "; relic=" + option.RelicId +
            "; offeredRelic=" + option.OfferedRelic?.Serialized + "; cardType=" + option.CardType +
            "; damage=" + option.HealthDamage + "; heal=" + option.HealthHeal +
            "; maxHpCost=" + option.MaxHpCost + "; willKill=" + option.WillKill, identity,
            operation: "Event handler: actual generated option identity and bound payload");
        var availabilityReads = RouteEventOptionAvailabilityDomains(run, option);
        var picker = RouteEventPicker(run, run._eventKey!.Value.Entry, option.Key);
        if (picker != null && picker.Value.Candidates.Any(id => IsKnownRouteEventCandidate(run,
            id, run._eventKey!.Value.Entry, option.Key, dependencies, knownInstanceIds)))
            availabilityReads &= ~PredictorCombatDomain.Deck;
        var populationCauses = picker == null ? CausesFor(dependencies, availabilityReads & PredictorCombatDomain.Deck) :
            RouteEventPickerPopulationCauses(run, run._eventKey!.Value.Entry, option.Key, dependencies);
        if (picker != null && (RouteEventOptionAvailabilityDomains(run, option) & PredictorCombatDomain.Deck) != 0)
        {
            inputs.Read("options." + option.Key + ".candidatePopulation", picker.Value.Candidates.Length,
                populationCauses,
                operation: "Event option: actual branch-specific card predicate population");
            inputs.Read("options." + option.Key + ".candidateNonEmpty", picker.Value.Candidates.Length > 0,
                (availabilityReads & PredictorCombatDomain.Deck) == 0 ? [] : populationCauses,
                operation: (availabilityReads & PredictorCombatDomain.Deck) == 0 ?
                    "Event option: guaranteed eligible member proves availability independently of unresolved population" :
                    "Event option: actual branch-specific nonempty candidate test");
        }
        bool enabled = option.Enabled;
        var availabilityCauses = CausesFor(dependencies, availabilityReads &
            ~(PredictorCombatDomain.Gold | PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics | PredictorCombatDomain.Deck));
        if ((availabilityReads & PredictorCombatDomain.Deck) != 0)
            availabilityCauses = availabilityCauses.AddRange(populationCauses);
        if ((availabilityReads & PredictorCombatDomain.Gold) != 0)
        {
            // Only options whose concrete handler enforces an affordable cost
            // are here; spend-more-than-owned source options remain unaffected.
            int cost = option.GoldCost ?? 40; // Conveyor Golden Fysh still checks 40.
            var gold = inputs.GoldAtLeast(cost);
            availabilityCauses = availabilityCauses.AddRange(gold.PendingCauses);
            if (gold.IsKnown) enabled = gold.Value;
        }
        if ((availabilityReads & PredictorCombatDomain.PotionInventory) != 0)
        {
            var potion = option.Key == "THROW_FOUL_POTION" ? inputs.PotionMember("FOUL_POTION") : inputs.PotionsAtLeast(1);
            availabilityCauses = availabilityCauses.AddRange(potion.PendingCauses);
            if (potion.IsKnown) enabled = potion.Value;
        }
        if ((availabilityReads & PredictorCombatDomain.Relics) != 0)
        {
            var tradable = inputs.TradableAtLeast(1);
            availabilityCauses = availabilityCauses.AddRange(tradable.PendingCauses);
            if (tradable.IsKnown) enabled = option.Key == "DONE" ? !tradable.Value : tradable.Value;
        }
        var availability = identity.AddRange(availabilityCauses)
            .Distinct().ToImmutableArray();
        var enabledFact = inputs.Result("options." + option.Key + ".enabled", enabled, availability,
            "Event handler: actual option availability predicate over recorded inputs");
        var knowledge = new PredictorEventOptionKnowledge(option.Key, identity.IsEmpty ? option with { Enabled = true } : null,
            availability.IsEmpty ? enabled : null, identity.AddRange(availability).Distinct().ToImmutableArray())
        {
            IdentityPendingCauses = identity, AvailabilityPendingCauses = availability,
            LostRelic = identity.IsEmpty && option.RelicId is { } lostId
                ? run.Working.Relics.FirstOrDefault(relic => relic.Id == lostId)?.Key : null,
            IdentityReadFactIds = identityInput.FactId is { } identityFact ? [identityFact] : [],
            AvailabilityReadFactIds = enabledFact is { } availabilityFact ? [availabilityFact] : inputs.Facts
        };
        return knowledge with { TraceIdentity = run.EventTrace()?.OptionGuard(run, option, knowledge, availabilityReads, dependencies) };
    }).ToImmutableArray();

    // Only source reads that shape this option belong here. Event-local RNG is
    // separate from Niche, Rewards and Shops; its fixed-pool draws need none of
    // those cursor observations.
    private static PredictorCombatDomain RouteEventOptionIdentityDomains(PredictorRun run, PredictorEventOption option)
    {
        var reads = run._eventKey?.Entry switch
        {
            "THE_FUTURE_OF_POTIONS" => PredictorCombatDomain.PotionInventory,
            "STONE_OF_ALL_TIME" when option.Key == "LIFT" => PredictorCombatDomain.PotionInventory,
            "RANWID_THE_ELDER" when option.Key == "POTION" => PredictorCombatDomain.PotionInventory,
            "RANWID_THE_ELDER" when option.Key == "RELIC" => PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics,
            "RELIC_TRADER" when option.Key != "DONE" => PredictorCombatDomain.Relics | PredictorCombatDomain.RewardsRng,
            "WELCOME_TO_WONGOS" when option.Key == "FEATURED_ITEM" => PredictorCombatDomain.Relics,
            "FAKE_MERCHANT" when option.Key.StartsWith("BUY_", StringComparison.Ordinal) => PredictorCombatDomain.ShopsRng | PredictorCombatDomain.Relics,
            "FAKE_MERCHANT" when option.Key == "THROW_FOUL_POTION" => PredictorCombatDomain.PotionInventory,
            "ENDLESS_CONVEYOR" when option.Key.StartsWith("GRAB_", StringComparison.Ordinal) => PredictorCombatDomain.PotionInventory,
            _ => PredictorCombatDomain.None
        };
        if (option.CardTargetId != null) reads |= PredictorCombatDomain.InstanceIds;
        if (option.PotionId != null || option.RelicId != null) reads |= PredictorCombatDomain.InstanceIds;
        return RefineEventPotionReads(run, reads,
            emptySlotOnly: run._eventKey?.Entry == "ENDLESS_CONVEYOR" && option.Key.StartsWith("GRAB_", StringComparison.Ordinal));
    }

    private static PredictorCombatDomain RouteEventOptionAvailabilityDomains(PredictorRun run, PredictorEventOption option)
    {
        var reads = (run._eventKey?.Entry, option.Key) switch
        {
            ("SYMBIOTE", "APPROACH") or ("STONE_OF_ALL_TIME", "PUSH") or ("WOOD_CARVINGS", "SNAKE") or
            ("SELF_HELP_BOOK", _) or ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT") => PredictorCombatDomain.Deck,
            ("RANWID_THE_ELDER", "POTION") or ("FAKE_MERCHANT", "THROW_FOUL_POTION") => PredictorCombatDomain.PotionInventory,
            ("RANWID_THE_ELDER", "RELIC") or ("RELIC_TRADER", "DONE") => PredictorCombatDomain.Relics,
            ("ENDLESS_CONVEYOR", _) when option.Key.StartsWith("GRAB_", StringComparison.Ordinal) => PredictorCombatDomain.Gold,
            _ => PredictorCombatDomain.None
        };
        // Some source options deliberately allow spending more than the current
        // budget (for example Whispering Hollow and Ranwid). Preserve that rule.
        if (option.Key is "EMOTIONAL_AWARENESS" or "ARACHNID_ACUPUNCTURE" or "BONE_TEA" or "EMBER_TEA" or
            "OFFER_TRIBUTE" or "TENTACLE_QUILL" or "PRICKLY_SPONGE" or "BARGAIN_BIN" or "FEATURED_ITEM" or "MYSTERY_BOX" ||
            option.Key.StartsWith("BUY_", StringComparison.Ordinal)) reads |= PredictorCombatDomain.Gold;
        return reads;
    }

    private static bool LiveEventAssumesHealth(ModelKey key) => key.Entry is "COLOSSAL_FLOWER" or
        "DENSE_VEGETATION" or "JUNGLE_MAZE_ADVENTURE" or "ROUND_TEA_PARTY" or "TRASH_HEAP" or
        "UNREST_SITE" or "THE_LEGENDS_WERE_TRUE";
    private static bool LiveEventAssumesDeck(ModelKey key) => key.Entry is "AMALGAMATOR" or
        "FIELD_OF_MAN_SIZED_HOLES" or "GRAVE_OF_THE_FORGOTTEN" or "MORPHIC_GROVE" or
        "SLIPPERY_BRIDGE" or "SPIRALING_WHIRLPOOL" or "THE_LEGENDS_WERE_TRUE" or "WOOD_CARVINGS";
    // Entry and option dependency maps follow the actual donor handlers in
    // PredictorEvents. Manual pickers may enumerate an incomplete deck when a
    // guaranteed larger subset proves that entry cannot auto-select targets.
    private static PredictorCombatDomain RouteEventActionReadDomains(PredictorRun run, PredictorEventOption option,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds, bool resolvingHealth = false)
    {
        string key = run._eventKey!.Value.Entry, branch = option.Key;
        var reads = PredictorCombatDomain.None;
        bool healthRequired = RouteEventOptionNeedsHealth(key, branch);
        // The donor only queues this selection until health is declared. Its
        // effects and target enumeration happen on ProvideEventHealthFact.
        if (healthRequired && run._eventHealth == null && !resolvingHealth && !run._preview.Event.ProjectHealthIndependently) return reads;
        // Some native options may spend more than the owned amount. Their
        // clamped subtraction cannot use one earlier branch's private budget
        // when the retained alternatives straddle the cost. A proved lower
        // bound makes ordinary fixed subtraction independent of that choice.
        if (run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults && option.GoldCost is > 0 &&
            !CausesFor(dependencies, PredictorCombatDomain.Gold).IsEmpty)
        {
            var minimumGold = run._preview.Partial?.EventGoldBalance(run.Working)?.Bounds.Lower;
            if (minimumGold == null || minimumGold < option.GoldCost) reads |= PredictorCombatDomain.Gold;
        }
        // Healing/max-HP gain does not consult loss/death-prevention listeners.
        // In particular Byrdonis EAT has no Deck or Relics read at all.
        if (healthRequired && (run._eventHealth != null || resolvingHealth) &&
            RouteEventHealthMayLose(key, branch))
            reads |= PredictorCombatDomain.PotionInventory | PredictorCombatDomain.Relics;
        if (key == "ENDLESS_CONVEYOR" && branch.StartsWith("GRAB_", StringComparison.Ordinal))
            reads |= PredictorCombatDomain.PotionInventory;
        if ((key, branch) == ("DENSE_VEGETATION", "REST")) reads |= PredictorCombatDomain.Relics;
        if ((key, branch) is ("REFLECTIONS", _) or ("DOORS_OF_LIGHT_AND_DARK", "LIGHT") or
            ("TABLET_OF_TRUTH", "DECIPHER") or ("ENDLESS_CONVEYOR", "OBSERVE_CHEF") or
            ("ENDLESS_CONVEYOR", "GRAB_SPICY_SNAPPY") or ("WELCOME_TO_WONGOS", "LEAVE") or ("WAR_HISTORIAN_REPY", _))
            reads |= PredictorCombatDomain.Deck;

        var picker = RouteEventPicker(run, key, branch);
        if (picker != null)
        {
            var (candidates, requested) = picker.Value;
            int guaranteed = candidates.Count(id => IsKnownRouteEventCandidate(run, id, key, branch,
                dependencies, knownInstanceIds));
            run.EventTrace()?.PickerGuard(run, candidates, requested, guaranteed, dependencies);
            if (guaranteed <= requested) reads |= PredictorCombatDomain.Deck;
        }
        var relic = RouteEventObtainRelic(run, key, branch);
        if (relic is { } obtain) reads |= ObtainEntryReadDomains(run, obtain, dependencies);
        if ((key, branch) is ("TRIAL", "MERCHANT_GUILTY") or ("THIS_OR_THAT", "ORNATE") or
            ("UNREST_SITE", "KILL") or ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") or
            ("LUMINOUS_CHOIR", "OFFER_TRIBUTE") or ("RANWID_THE_ELDER", _))
        {
            reads |= PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Relics;
            var state = run.Working;
            var front = PredictorRewardGeneration.PullRelic(run.Context, ref state);
            reads |= ObtainEntryReadDomains(run, front, dependencies);
            // Ranwid's two-relic trade and Trial's two pulls can reveal a
            // second auto-obtain in the same call. Require its actual reads.
            if ((key, branch) is ("TRIAL", "MERCHANT_GUILTY") or ("RANWID_THE_ELDER", "RELIC"))
            {
                var second = PredictorRewardGeneration.PullRelic(run.Context, ref state);
                reads |= ObtainEntryReadDomains(run, second, dependencies);
            }
        }
        if ((key, branch) is ("WELCOME_TO_WONGOS", "BARGAIN_BIN"))
            reads |= PredictorCombatDomain.Relics;
        var fixedCard = RouteEventFixedAddedCard(run, key, branch);
        if (fixedCard is { } card && !RouteEventFixedCardReadCauses(run, card, dependencies).IsEmpty)
            reads |= PredictorCombatDomain.Relics;
        return reads;
    }

    private static bool RouteEventHealthMayLose(string key, string branch) => RouteEventOptionNeedsHealth(key, branch) &&
        (key, branch) is not (("MORPHIC_GROVE", "LONER") or ("TRIAL", "NOBLE_GUILTY") or
            ("UNREST_SITE", "REST") or ("SAPPHIRE_SEED", "EAT") or ("STONE_OF_ALL_TIME", "LIFT") or
            ("ROUND_TEA_PARTY", "ENJOY_TEA") or ("ABYSSAL_BATHS", "ABSTAIN") or ("TABLET_OF_TRUTH", "SMASH") or
            ("DENSE_VEGETATION", "REST") or ("SPIRALING_WHIRLPOOL", "DRINK") or
            ("WATERLOGGED_SCRIPTORIUM", "BLOODY_INK") or ("BYRDONIS_NEST", "EAT") or
            ("SPIRIT_GRAFTER", "LET_IT_IN"));

    // These branches call AddCard before any card picker or obtain child. The
    // concrete AddCard helper remains the sole effects implementation.
    private static PredictorCard? RouteEventFixedAddedCard(PredictorRun run, string key, string branch)
    {
        string? entry = (key, branch) switch
        {
            ("BYRDONIS_NEST", "TAKE") => "BYRDONIS_EGG",
            ("THE_LEGENDS_WERE_TRUE", "NAB_THE_MAP") => "SPOILS_MAP",
            ("SPIRIT_GRAFTER", "LET_IT_IN") => "METAMORPHOSIS",
            ("BUGSLAYER", "EXTERMINATION") => "EXTERMINATE",
            ("BUGSLAYER", "SQUASH") => "SQUASH",
            ("CRYSTAL_SPHERE", "PAYMENT_PLAN") => "DEBT",
            ("ZEN_WEAVER", "BREATHING_TECHNIQUES") => "ENLIGHTENMENT",
            ("UNREST_SITE", "REST") => "POOR_SLEEP",
            ("LOST_WISP", "CLAIM") or ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT") => "DECAY",
            ("PUNCH_OFF", "NAB") => "INJURY",
            ("TRIAL", "MERCHANT_INNOCENT") => "SHAME",
            ("TRIAL", "MERCHANT_GUILTY") or ("TRIAL", "NOBLE_INNOCENT") => "REGRET",
            ("TRIAL", "NONDESCRIPT_INNOCENT") or ("TRIAL", "NONDESCRIPT_GUILTY") => "DOUBT",
            _ => null
        };
        return entry == null ? null : PredictorCardChanges.Definition(run.Context, new("CARD", entry)).Prototype;
    }

    private static ImmutableArray<PredictorCombatPendingCause> RouteEventFixedCardReadCauses(PredictorRun run,
        PredictorCard card, List<RouteDependency> dependencies)
    {
        var inputs = new EventStateInputs(run, run._eventKey!.Value, dependencies, "action");
        var causes = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        void Member(string entry) => causes.AddRange(inputs.RelicMember(entry).PendingCauses);
        inputs.Read("cardAdd.prototype", card.Key.Serialized, operation: "AddCard: actual fixed source prototype");
        var creationVariants = new List<PredictorCard> { card };
        foreach (string egg in new[] { "MOLTEN_EGG", "TOXIC_EGG", "FROZEN_EGG" })
        {
            var upgraded = PredictorRewardGeneration.UpgradeOnDeckAddWithEgg(card, egg);
            if (upgraded == card) continue;
            Member(egg);
            creationVariants.Add(upgraded);
        }
        if (creationVariants.Any(variant => PredictorObtainSources.CanEnchant(run.Context, variant) &&
            PredictorObtainSources.GainsBlockForFresnel(run.Context, variant)))
            Member("FRESNEL_LENS");
        Member("BING_BONG");
        var bingState = RouteRelicStateCauses(run, dependencies, "BING_BONG");
        inputs.Read("cardAdd.bingBong.skipNextIdentity", string.Join(" | ", run.Working.Relics
            .Where(r => !r.Melted && r.Key.Entry == "BING_BONG").Select(r => r.BingBongCardsToSkip.Contains(run.Working.NextInstanceId))),
            bingState, operation: "AfterCardAddedToDeck: actual BingBong skip membership for the allocated identity");
        causes.AddRange(bingState);
        Member("BOOK_OF_FIVE_RINGS");
        var bookState = RouteRelicStateCauses(run, dependencies, "BOOK_OF_FIVE_RINGS");
        inputs.Read("cardAdd.book.cardsAdded", string.Join(" | ", run.Working.Relics
            .Where(r => !r.Melted && r.Key.Entry == "BOOK_OF_FIVE_RINGS").Select(r => r.BookOfFiveRingsCardsAdded)),
            bookState, operation: "AfterCardAddedToDeck: actual BookOfFiveRings counters");
        causes.AddRange(bookState);
        if (card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Curse) Member("DARKSTONE_PERIAPT");
        var lucky = inputs.RelicMember("LUCKY_FYSH");
        causes.AddRange(lucky.PendingCauses);
        if (!lucky.IsKnown || lucky.Value)
        {
            Member("BOWLER_HAT");
            Member("ECTOPLASM");
        }
        inputs.Read("cardAdd.modifier.HOARDER", run.Context.HasRunModifier("HOARDER"),
            operation: "AfterCardAddedToDeck: actual stable run modifier");
        if (run.Context.HasRunModifier("HOARDER") || PredictorSettlementEffects.Has(run.Working, "BING_BONG"))
            causes.AddRange(CausesFor(dependencies, PredictorCombatDomain.InstanceIds));
        return PredictorRouteCauseEvidence.Normalize(causes);
    }

    // Callers use this for the actual gate, retaining the domain return only
    // for legacy trace compatibility. Fixed-card hooks preserve local roots.
    private static ImmutableArray<PredictorCombatPendingCause> RouteEventActionReadCauses(PredictorRun run,
        PredictorEventOption option, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        bool resolvingHealth = false)
    {
        string key = run._eventKey!.Value.Entry, branch = option.Key;
        if (RouteEventOptionNeedsHealth(key, branch) && run._eventHealth == null && !resolvingHealth &&
            !run._preview.Event.ProjectHealthIndependently) return [];
        var reads = RouteEventActionReadDomains(run, option, dependencies, knownInstanceIds, resolvingHealth);
        var pickerCauses = RouteEventPicker(run, key, branch) == null ? ImmutableArray<PredictorCombatPendingCause>.Empty :
            RouteEventPickerReadCauses(run, key, branch, dependencies, knownInstanceIds);
        if ((key, branch) == ("SLIPPERY_BRIDGE", "OVERCOME") && option.CardTargetId is { } bridgeTarget)
        {
            var target = run.Working.Card(bridgeTarget);
            // Identity projection does not read a card's payload. Actual
            // removal still owns its persistent Spoils Map coordinates/hook.
            pickerCauses = pickerCauses.AddRange(RouteDeckTargetCauses(run,
                [new(target.Id, target.Key, target.FloorAdded)], dependencies, knownInstanceIds,
                PredictorDeckProjectionScope.Removable, PredictorDeckUncertainFact.Definition));
        }
        // The picker owns its actual qualification and automatic execution
        // reads. Broad Deck causes would reintroduce unrelated property gaps.
        if (RouteEventPicker(run, key, branch) != null) reads &= ~PredictorCombatDomain.Deck;
        if (key == "WAR_HISTORIAN_REPY")
        {
            // Repy consumes the ordered key instances, not their upgrades or
            // enchantments. A boolean membership proof is insufficient: a
            // second key changes the single-player continuation and rewards.
            reads &= ~PredictorCombatDomain.Deck;
            pickerCauses = pickerCauses.AddRange(RouteRepyLanternKeyReadCauses(run, dependencies, knownInstanceIds));
        }
        var card = RouteEventFixedAddedCard(run, key, branch);
        reads = RefineEventPotionReads(run, reads,
            emptySlotOnly: key == "ENDLESS_CONVEYOR" && branch.StartsWith("GRAB_", StringComparison.Ordinal) &&
                !RouteEventHealthMayLose(key, branch));
        // Local potion facts supply event predicates, not a relic obtain's
        // separate read of the persistent inventory simulation.
        if ((key, branch) is ("RANWID_THE_ELDER", _))
        {
            var state = run.Working;
            var front = PredictorRewardGeneration.PullRelic(run.Context, ref state);
            reads |= ObtainEntryReadDomains(run, front, dependencies) & PredictorCombatDomain.PotionInventory;
            if (branch == "RELIC")
            {
                var second = PredictorRewardGeneration.PullRelic(run.Context, ref state);
                reads |= ObtainEntryReadDomains(run, second, dependencies) & PredictorCombatDomain.PotionInventory;
            }
        }
        if (card == null) return PredictorRouteCauseEvidence.Normalize(CausesFor(dependencies, reads).AddRange(pickerCauses));
        bool independentRelicRead = RouteEventObtainRelic(run, key, branch) != null ||
            (key, branch) is ("TRIAL", "MERCHANT_GUILTY") or ("DENSE_VEGETATION", "REST") or
            ("THIS_OR_THAT", "ORNATE") or ("UNREST_SITE", "KILL") or ("ROUND_TEA_PARTY", "CONTINUE_FIGHT") or
            ("LUMINOUS_CHOIR", "OFFER_TRIBUTE") or ("RANWID_THE_ELDER", _) or ("WELCOME_TO_WONGOS", "BARGAIN_BIN") ||
            RouteEventHealthMayLose(key, branch) && (run._eventHealth != null || resolvingHealth);
        return PredictorRouteCauseEvidence.Normalize(CausesFor(dependencies, independentRelicRead ? reads : reads & ~PredictorCombatDomain.Relics)
            .AddRange(pickerCauses).AddRange(RouteEventFixedCardReadCauses(run, card, dependencies)));
    }

    private static ImmutableArray<PredictorCombatPendingCause> RouteRepyLanternKeyReadCauses(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        var targets = run.Working.Deck.Select(run.Working.Card).Where(card => card.Key.Entry == "LANTERN_KEY")
            .Select(card => new PredictorPlannedCard(card.Id, card.Key, card.FloorAdded)).ToImmutableArray();
        var causes = PredictorRouteCauseEvidence.Normalize(dependencies.Where(dependency =>
                RouteCardMayVary(run, [dependency], "LANTERN_KEY")).Select(dependency => dependency.Cause)
            .Concat(RouteDeckTargetCauses(run, targets, dependencies, knownInstanceIds,
                PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.Definition)));
        var inputs = new EventStateInputs(run, run._eventKey!.Value, dependencies, "action");
        return inputs.Read("deck.LANTERN_KEY.instances", string.Join(" | ", targets.Select(target => target.InstanceId)), causes,
            operation: "Repy: ordered Lantern Key identities, removal and remaining-key membership; card upgrades and enchantments are not read",
            sourceProperties: ["deck.All.candidates"]).PendingCauses;
    }

}
