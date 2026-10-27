using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record PendingRouteEventCardEffect(ImmutableArray<PredictorCard> Cards,
        ImmutableArray<PredictorCard> ExistingCards, PredictorCombatDomain Domains,
        PredictorDeckUncertainFact AffectedFacts, bool ChangesMembership, bool AddsCards,
        int FixedHealthDamage, int DiscardEventLocalRngDraws,
        int FixedGoldCost = 0, string? FixedAddedCard = null)
    {
        internal RouteDeckRead TargetRead { get; init; } = new(PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.All);
        internal bool MayEditExistingCards { get; init; }
    }

    private static bool TryContinueRouteEventDeckAction(PredictorRun run, PredictorEventOption option,
        ImmutableArray<PredictorCombatPendingCause> readCauses, List<RouteDependency> dependencies, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._preview.Partial == null ||
            run._eventKey is not { } key || readCauses.IsEmpty ||
            readCauses.Any(c => (c.AffectedDomains & ~PredictorCombatDomain.Deck) != 0))
            return false;
        if (RouteEventPicker(run, key.Entry, option.Key) is { } picker &&
            PendingRouteEventCardsEffect(run, out _, key.Entry, option.Key,
                picker.Candidates.Select(run.Working.Card).ToImmutableArray()))
        {
            // Execute the real fixed costs, fixed receipts and hooks. Only the
            // source's auto-target selection is deferred; its witness is not a
            // proven target set when the qualification attributes are unknown.
            var before = run.Working;
            var targets = picker.Candidates.Select(before.Card).ToImmutableArray();
            run._preview.Event.DeferCardTarget = true;
            try
            {
                if (run.Submit(run.Request!, new ChooseEventOption(option.Key), token) != PredictorInputResult.Accepted)
                    throw new InvalidOperationException("PredictorConditionalEventOptionRejected");
            }
            finally { run._preview.Event.DeferCardTarget = false; }
            var cause = PredictorRouteCauseEvidence.CreateExplicit("EventCardsChoice", before.Position.NodeId,
                PredictorCombatDomain.Continuation, PredictorPendingReasonKind.UserChoice,
                "The chosen event operation has an unresolved target; earlier choices also affect its eligible population.",
                ["deck." + RouteEventDeckRead(key.Entry, option.Key).Scope + ".candidates"], readCauses,
                occurrenceKey: key.Serialized + ":" + option.Key);
            if (run.Phase == PredictorPhase.EventCards)
            {
                var pending = new List<PredictorCombatPendingCause> { cause };
                if (!ContinuePendingRouteEvent(run, dependencies, pending, token))
                    throw new InvalidOperationException("PredictorConditionalEventLeafNotCompleted");
                return true;
            }
            if (run.Phase != PredictorPhase.Complete)
                throw new InvalidOperationException("PredictorConditionalEventLeafChangedStructure");
            // A zero-candidate native branch may finish before opening a picker.
            // Its fixed tail already ran. Retain the possible card edit from
            // other incoming alternatives, without repeating that tail.
            if (!PendingRouteEventCardsEffect(run, out var effect, key.Entry, option.Key, targets))
                throw new InvalidOperationException("PredictorConditionalEventEffectMissing");
            effect = effect with { FixedAddedCard = null, FixedGoldCost = 0, FixedHealthDamage = 0 };
            cause = cause with { AffectedDomains = effect.Domains };
            run._preview.Event.PendingCardEffect = effect;
            run._preview.Partial.PendingEventCards(run, cause, effect, before);
            AddDependency(dependencies, EventCardEffectDependency(cause, effect));
            return true;
        }
        if ((key.Entry, option.Key) is not (("DOORS_OF_LIGHT_AND_DARK", "LIGHT") or ("REFLECTIONS", "TOUCH_A_MIRROR")))
            return false;
        // Both native handlers finish at this leaf and only change upgrades.
        // LIGHT shuffles; Reflections draws sequentially without replacement.
        // Neither advances a public stream. We deliberately do not substitute
        // either draw law or a representative target set for the missing one.
        var cards = run.Working.Deck.Select(run.Working.Card).Concat(run._preview.Partial.Deck.Choices
            .SelectMany(c => c.Effects).SelectMany(e => e.AddedCards)).Distinct().ToImmutableArray();
        var facts = PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other;
        if (cards.Any(c => PredictorCardChanges.Definition(run.Context, c.Key).Levels
            .Select(level => level.Eternal).Distinct().Count() > 1)) facts |= PredictorDeckUncertainFact.Removable;
        var randomCause = PredictorRouteCauseEvidence.CreateExplicit("EventLocalDeckEffectPending", run.Working.Position.NodeId,
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.DerivedDependency,
            "This terminal event operation changes only card upgrades using its own discarded random stream; its targets depend on earlier deck properties.",
            ["deck.Upgradable.candidates", "deck.Upgraded.candidates"], readCauses,
            occurrenceKey: key.Serialized + ":" + option.Key);
        var randomEffect = new PendingRouteEventCardEffect(cards, cards, PredictorCombatDomain.Deck, facts,
            false, false, 0, 0)
        { TargetRead = new(PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.Upgrade), MayEditExistingCards = true };
        run._preview.Event.PendingCardEffect = randomEffect;
        run._preview.Partial.PendingEventCards(run, randomCause, randomEffect);
        AddDependency(dependencies, new(randomCause, cards, MayChangeExistingCards: true)
        { DeckFacts = facts, ChangedCardMembershipEntries = [],
            ChangedCardStateEntries = cards.Select(c => c.Key.Entry).Distinct().ToImmutableArray() });
        run.EventTrace()?.PartialLocalDeckOperation(randomCause);
        run._eventBranch = option.Key;
        run.Accept(new("EventOption:" + option.Key, 0, [], null, null));
        run.FinishEvent();
        return true;
    }

    private static bool ContinueDefaultFakeMerchant(PredictorRun run, PredictorPlannedEvent? plan,
        List<PredictorCombatPendingCause> local)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults ||
            run._eventKey?.Entry != "FAKE_MERCHANT" || run.Phase != PredictorPhase.EventChoice ||
            plan is { Actions.Length: > 0 } || local.Count != 1 || local[0].Code != "EventOptionChoice") return false;
        // Stock generation has already consumed its normal entry RNG. The
        // approved no-purchase/no-combat continuation has the same state effect
        // as LEAVE, without manufacturing a saved/player-selected action. Keep
        // the observed options so explicit shopping or Foul combat can replace it.
        run.EventTrace()?.DefaultFakeMerchant();
        run.FinishEvent();
        local.Clear();
        return true;
    }

    private static void ApplyRouteEventDefaultCardTarget(PredictorRun run, List<RouteDependency> dependencies,
        HashSet<long> knownInstanceIds, List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || local.Count != 0 ||
            run.Phase != PredictorPhase.EventCards || run._eventKey?.Entry != "ENDLESS_CONVEYOR" ||
            run._conveyorDish != "JELLY_LIVER" || run.EventCardsRequired != 1 ||
            !CausesFor(dependencies, PredictorCombatDomain.Relics).IsEmpty) return;
        var target = run.EventCardCandidates.Select(run.Working.Card).FirstOrDefault(card =>
            PredictorCardChanges.Definition(run.Context, card.Key).Rarity == "Basic" &&
            IsKnownRouteDeckTarget(run, new(card.Id, card.Key, card.FloorAdded), dependencies, knownInstanceIds,
                PredictorDeckProjectionScope.Transformable, PredictorDeckUncertainFact.Definition));
        if (target == null) return;
        run.EventTrace()?.DefaultCardTarget(target);
        if (run.Submit(run.Request!, new SelectEventCards([target.Id]), token) != PredictorInputResult.Accepted)
            throw new InvalidOperationException("PredictorConveyorDefaultBasicTargetRejected");
    }

    private static void ApplyRouteEventDefaultRewardPickup(PredictorRun run, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (local.Count == 0)
        {
            CompleteLiveObtainHealth(run, dependencies, token);
            CompleteLivePaelsTooth(run, dependencies, token);
        }
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || local.Count != 0 ||
            run.Phase != PredictorPhase.Rewards) return;
        ContinueLiveDefaultCardRewardChildren(run, dependencies, token);
        if (run.Phase != PredictorPhase.Rewards || run.HasObtainChild) return;
        var skipped = new HashSet<long>();
        if (!ApplyRouteRewardChoices(run, new(run.Working.Position.NodeId), dependencies, [], skipped, local, token) ||
            run.Phase != PredictorPhase.Rewards || run.HasObtainChild ||
            run.Rewards.Any(reward => reward.Status == PredictorRewardStatus.Open && !skipped.Contains(reward.Id) &&
                !IsLiveDefaultPotionOmission(run, reward, dependencies))) return;
        foreach (var reward in run.Rewards.Where(reward => reward.Status == PredictorRewardStatus.Open))
            RegisterLiveDefaultPotionOmission(run, reward, dependencies);
        if (!CausesFor(dependencies, PredictorCombatDomain.Continuation).IsEmpty) return;
        run.Submit(run.Request!, new LeaveRewards(), token);
    }

    private static bool ContinuePendingRouteEvent(PredictorRun run, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        run._preview.Event.PendingCardEffect = null;
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._eventKey == null ||
            local.Count != 1) return false;
        var cause = local[0];
        if (run.Phase == PredictorPhase.EventCards && cause.Code == "EventCardsChoice" &&
            !run.EventOfferedCards.IsEmpty)
        {
            // These are already generated opportunities, not forced direct
            // additions. Drawing's unselected receipt is Skip: no card-add
            // hooks, Lucky Fysh gold, or speculative deck membership.
            run._preview.Partial?.Facts.RecordResult("event.default.offeredCardsSkipped", run.Working.Position.NodeId,
                string.Join(" | ", run.EventOfferedCards.Select(card => card.Key.Serialized)),
                origin: PredictorKnowledgeOrigin.ApprovedDefault,
                operation: "Generated event candidates remain visible; no concrete card receipt was selected.");
            run.FinishEvent();
            local.Clear();
            return true;
        }
        if (ContinueDrawingLowValueEvent(run, dependencies, local)) return true;
        if (ContinueOptionalRouteEventTail(run, dependencies, local)) return true;
        if (run.Phase == PredictorPhase.EventCards && cause.Code is ("EventCardsChoice" or "PlannedEventDeckTargetPending"))
        {
            if (!PendingRouteEventCardsEffect(run, out var effect) || effect.FixedHealthDamage > 0 &&
                !run._preview.Event.ProjectHealthIndependently && run._eventHealth == null) return false;
            var targetBoundary = run.Working;
            if (effect.FixedGoldCost > 0) run.SpendEventGold(effect.FixedGoldCost);
            if (effect.FixedAddedCard is { } added)
            {
                var prototype = PredictorCardChanges.Definition(run.Context, new("CARD", added)).Prototype;
                if (RouteEventFixedCardReadCauses(run, prototype, dependencies).IsEmpty) run.AddTrialCurse(added);
                else effect = effect with { Cards = effect.Cards.Add(prototype), AddsCards = true,
                    Domains = effect.Domains | PredictorCombatDomain.InstanceIds | PredictorCombatDomain.Relics | PredictorCombatDomain.Health };
            }
            local[0] = cause with { AffectedDomains = effect.Domains };
            run._preview.Event.PendingCardEffect = effect;
            run._preview.Partial?.PendingEventCards(run, local[0], effect, targetBoundary);
            AddDependency(dependencies, EventCardEffectDependency(local[0], effect));
            for (int i = 0; i < effect.DiscardEventLocalRngDraws; i++) run._eventRng!.NextInt(100);
            if (effect.FixedHealthDamage > 0) run.ApplyEventHealth(-effect.FixedHealthDamage, 0);
            PropagateRouteEventHealthPending(run, dependencies);
            // This is private witness completion of an unresolved effect. No
            // target is selected and no Skip/default-target input is invented.
            // Source event-local transformation RNG is discarded at this leaf;
            // Conveyor's dependent next dish remains locally pending instead.
            if (run.Phase != PredictorPhase.Complete) run.FinishEvent();
            return true;
        }
        if (run.Phase != PredictorPhase.Rewards || run.HasObtainChild || cause.Code != "EventRewardsChoice" ||
            run._eventCombatEntry != null || !run._eventRewardContinuation ||
            run._eventKey.Value.Entry is "ENDLESS_CONVEYOR" or "WAR_HISTORIAN_REPY" ||
            run.Rewards.Any(r => r.Status == PredictorRewardStatus.Open && r.Mandatory)) return false;
        foreach (var reward in run.Rewards.Where(r => r.Status == PredictorRewardStatus.Open))
        {
            if (IsLiveDefaultPotionOmission(run, reward, dependencies))
            {
                RegisterLiveDefaultPotionOmission(run, reward, dependencies);
                continue;
            }
            if (IsLiveDefaultCardSkip(run, reward)) continue;
            AddPendingReward(run, reward, dependencies);
            if (reward.Kind is (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) &&
                (PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS") || PredictorSettlementEffects.Has(run.Working, "BING_BONG")))
                AddDependency(dependencies, new(RewardChoiceCause(run, reward, "Pending" + reward.Kind + "Choice",
                    PredictorCombatDomain.Relics, dependencies)));
        }
        if (!CausesFor(dependencies, PredictorCombatDomain.Continuation).IsEmpty) return false;
        local.Clear();
        // Leave the published reward disposition Open. The witness only omits
        // unresolved receipts with their real downstream effect dependencies.
        run.FinishEvent();
        return true;
    }

    private static RouteDependency EventCardEffectDependency(PredictorCombatPendingCause cause,
        PendingRouteEventCardEffect effect) => new(cause, effect.Cards,
            effect.MayEditExistingCards || !effect.ExistingCards.IsEmpty)
    {
        DeckFacts = effect.AffectedFacts,
        ChangedCardMembershipEntries = effect.ChangesMembership
            ? effect.Cards.IsEmpty ? default : effect.Cards.Select(c => c.Key.Entry).Distinct().ToImmutableArray() : [],
        ChangedCardStateEntries = effect.Cards.IsEmpty ? default : effect.Cards.Select(c => c.Key.Entry).Distinct().ToImmutableArray(),
        ChangedRelicEntries = (effect.Domains & PredictorCombatDomain.Relics) != 0 ? default : [],
        ChangedRelicStateEntries = (effect.Domains & PredictorCombatDomain.Relics) != 0 ? default : []
    };

    private static bool ContinueOptionalRouteEventTail(PredictorRun run, List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local)
    {
        string key = run._eventKey!.Value.Entry;
        // These event-local loops never touch a public stream. Their optional
        // repeated deck/HP edits remain unresolved instead of inventing Exit,
        // Give Up, a bridge target, a Tinker rider or numerical health.
        if (run.Phase != PredictorPhase.EventChoice ||
            key is not ("ABYSSAL_BATHS" or "TABLET_OF_TRUTH" or "SLIPPERY_BRIDGE")) return false;
        if (local[0].Code.StartsWith("Planned", StringComparison.Ordinal)) return false;
        var domains = PredictorCombatDomain.Health;
        PendingRouteEventCardEffect effect = new([], [], domains, PredictorDeckUncertainFact.None, false, false, 0, 0);
        if (key != "ABYSSAL_BATHS")
        {
            var cards = run.Working.Deck.Select(run.Working.Card).ToImmutableArray();
            domains |= PredictorCombatDomain.Deck;
            if (key == "SLIPPERY_BRIDGE" && cards.Any(c => c.Key.Entry == "SPOILS_MAP")) domains |= PredictorCombatDomain.Gold;
            var facts = key == "SLIPPERY_BRIDGE" ? PredictorDeckUncertainFact.Membership :
                PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other | PredictorDeckUncertainFact.Removable;
            effect = new(cards, cards, domains, facts, key == "SLIPPERY_BRIDGE", false, 0, 0);
        }
        var cause = local[0] with { AffectedDomains = domains,
            SourceEvidence = "Unselected event-local repeat choices change only the recorded deck/health domains; entry and public stream consumption are retained." };
        local[0] = cause;
        run._preview.Event.PendingCardEffect = effect;
        run._preview.Partial?.PendingEventCards(run, cause, effect);
        AddDependency(dependencies, new(cause, effect.Cards, !effect.ExistingCards.IsEmpty)
        { DeckFacts = effect.AffectedFacts });
        if (run.Phase != PredictorPhase.Complete) run.FinishEvent();
        return true;
    }

    private static bool PendingRouteEventCardsEffect(PredictorRun run, out PendingRouteEventCardEffect effect,
        string? sourceKey = null, string? sourceBranch = null, ImmutableArray<PredictorCard> sourceTargets = default)
    {
        effect = null!;
        if (sourceKey == null && (run.Phase != PredictorPhase.EventCards || run._eventKey == null)) return false;
        string key = sourceKey ?? run._eventKey!.Value.Entry, branch = sourceBranch ?? run._eventBranch!;
        var existing = !sourceTargets.IsDefault ? sourceTargets : run.EventOfferedCards.IsEmpty
            ? run.EventCardCandidates.Select(run.Working.Card).ToImmutableArray() : ImmutableArray<PredictorCard>.Empty;
        var cards = existing.IsEmpty ? run.EventOfferedCards : existing;
        // Only handlers whose selected-card path reaches FinishEvent are leafs.
        // In particular Conveyor keeps its event-local RNG and opens another dish.
        bool addsCards = !run.EventOfferedCards.IsEmpty &&
            (key, branch) is ("ROOM_FULL_OF_CHEESE", "GORGE") or ("BRAIN_LEECH", "SHARE_KNOWLEDGE");
        bool transforms = (key, branch) is ("AROMA_OF_CHAOS", "LET_GO") or ("SYMBIOTE", "KILL_WITH_FIRE") or
            ("MORPHIC_GROVE", "GROUP") or ("TRIAL", "NONDESCRIPT_INNOCENT") or ("WHISPERING_HOLLOW", "HUG");
        bool removes = (key, branch) is ("DOORS_OF_LIGHT_AND_DARK", "DARK") or
            ("ZEN_WEAVER", "EMOTIONAL_AWARENESS" or "ARACHNID_ACUPUNCTURE") or ("WELLSPRING", "BATHE") or
            ("FIELD_OF_MAN_SIZED_HOLES", "RESIST") or ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") or
            ("AMALGAMATOR", "COMBINE_STRIKES" or "COMBINE_DEFENDS");
        bool fixedTransform = (key, branch) is ("WOOD_CARVINGS", "BIRD" or "TORUS");
        bool upgrade = (key, branch) is ("AROMA_OF_CHAOS", "MAINTAIN_CONTROL") or ("SAPPHIRE_SEED", "EAT") or
            ("TRIAL", "MERCHANT_INNOCENT") or ("SPIRIT_GRAFTER", "REJECTION");
        bool enchant = (key, branch) is ("SYMBIOTE", "APPROACH") or ("SAPPHIRE_SEED", "PLANT") or ("STONE_OF_ALL_TIME", "PUSH") or
            ("FIELD_OF_MAN_SIZED_HOLES", "ENTER_YOUR_HOLE") or ("WOOD_CARVINGS", "SNAKE") or
            ("SELF_HELP_BOOK", "READ_THE_BACK" or "READ_PASSAGE" or "READ_ENTIRE_BOOK") or
            ("SPIRALING_WHIRLPOOL", "OBSERVE") or ("GRAVE_OF_THE_FORGOTTEN", "CONFRONT") or
            ("WATERLOGGED_SCRIPTORIUM", "TENTACLE_QUILL") or ("WATERLOGGED_SCRIPTORIUM", "PRICKLY_SPONGE");
        if (!addsCards && !transforms && !fixedTransform && !removes && !upgrade && !enchant) return false;
        var facts = upgrade ? PredictorDeckUncertainFact.Upgrade | PredictorDeckUncertainFact.Other :
            enchant ? PredictorDeckUncertainFact.Enchantment | PredictorDeckUncertainFact.Other :
            removes ? PredictorDeckUncertainFact.Membership : PredictorDeckUncertainFact.All;
        if (upgrade && existing.Any(card => PredictorCardChanges.Definition(run.Context, card.Key).Levels
            .Select(level => level.Eternal).Distinct().Count() > 1)) facts |= PredictorDeckUncertainFact.Removable;
        var domains = PredictorCombatDomain.Deck;
        if (removes && existing.Any(card => card.Key.Entry == "SPOILS_MAP")) domains |= PredictorCombatDomain.Gold;
        if (fixedTransform)
        {
            var replacement = PredictorCardChanges.Definition(run.Context,
                new("CARD", branch == "BIRD" ? "PECK" : "TORIC_TOUGHNESS")).Prototype;
            cards = cards.Add(replacement);
            addsCards = true;
        }
        if (transforms)
        {
            var pools = cards.Select(c => PredictorCardChanges.Definition(run.Context, c.Key).TransformationPool).ToHashSet();
            var keys = run.Context.Catalog!.TransformationPools.Where(p => pools.Contains(p.Key)).SelectMany(p => p.Cards).ToHashSet();
            cards = cards.AddRange(run.Context.Catalog.CardDefinitions.Where(d => keys.Contains(d.Prototype.Key))
                .Select(d => d.Prototype)).Distinct().ToImmutableArray();
            addsCards = true;
        }
        if (addsCards)
        {
            domains |= PredictorCombatDomain.InstanceIds;
            // Owner accepts not projecting Lucky Fysh's omitted card receipts.
            // Keep the known budget for route decisions; this is local to an
            // unselected branch, not a global card/relic approximation.
            if (run._preview.EventTrace?.Hypothetical != true && PredictorSettlementEffects.Has(run.Working, "LUCKY_FYSH"))
                domains |= PredictorCombatDomain.Gold;
            if (PredictorSettlementEffects.Has(run.Working, "DARKSTONE_PERIAPT") || PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS"))
                domains |= PredictorCombatDomain.Health;
            if (PredictorSettlementEffects.Has(run.Working, "BING_BONG") || PredictorSettlementEffects.Has(run.Working, "BOOK_OF_FIVE_RINGS"))
                domains |= PredictorCombatDomain.Relics;
        }
        // Trial curses, Grave's Decay, Scriptorium costs and Stone's initial HP
        // loss already ran before opening the target choice. These are the only
        // remaining fixed tail effects in the supported handlers.
        int damage = key == "WHISPERING_HOLLOW" ? 9 : (key, branch) == ("SPIRIT_GRAFTER", "REJECTION") ? 10 : 0;
        string? fixedAdded = (key, branch) switch
        {
            ("WELLSPRING", "BATHE") => "GUILTY",
            ("FIELD_OF_MAN_SIZED_HOLES", "RESIST") => "NORMALITY",
            ("LUMINOUS_CHOIR", "REACH_INTO_THE_FLESH") => "SPORE_MIND",
            ("AMALGAMATOR", "COMBINE_STRIKES") => "ULTIMATE_STRIKE",
            ("AMALGAMATOR", "COMBINE_DEFENDS") => "ULTIMATE_DEFEND",
            _ => null
        };
        effect = new(cards, existing, domains, facts, removes || transforms || fixedTransform || addsCards, addsCards, damage,
            (key, branch) == ("STONE_OF_ALL_TIME", "PUSH") ? 1 : 0,
            key == "ZEN_WEAVER" ? run._eventGoldAmount : 0, fixedAdded)
        { TargetRead = RouteEventDeckRead(key, branch),
            MayEditExistingCards = removes || transforms || fixedTransform || upgrade || enchant };
        return true;
    }
}
