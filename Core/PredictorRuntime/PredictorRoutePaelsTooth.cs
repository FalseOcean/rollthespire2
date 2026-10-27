using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record LivePaelsToothBoundary(int? RemainingCount, int CombatsFinished);
    private sealed record LivePaelsToothStorage(PredictorPaelsToothStorageKnowledge Knowledge,
        ImmutableArray<PredictorCard> PossibleCards, bool PopulationKnown, int CombatsFinished = 0,
        ImmutableDictionary<string, LivePaelsToothBoundary>? BeforeNodeCounts = null);

    private PredictorDeckProjection LivePaelsToothPopulation()
    {
        _preview.Partial!.Synchronize(Working);
        return _preview.Partial.Deck.Project(PredictorDeckProjectionScope.PaelsTooth);
    }

    internal static int? PaelsToothStorageCount(PredictorDeckProjection population) => population.KnownCount is { } count
        ? (int)Math.Min(5, count) : population.Count?.Bounds.Lower >= 5 || population.KnownCandidates.Length >= 5 ? 5 : null;

    private static bool CompleteLivePaelsTooth(PredictorRun run, List<RouteDependency> dependencies, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._preview.Partial == null ||
            run.Phase != PredictorPhase.ObtainDeckCards || run._obtainDeckOperation != PredictorObtainDeckOperation.PaelsTooth ||
            run._obtainDeckPending is not { } pending) return false;
        var source = run._obtainDeckSource;
        var address = run.RegisterLiveObtainAddress(source, pending.Destination, pending.SourceId);
        var population = run.LivePaelsToothPopulation();
        int? count = PaelsToothStorageCount(population);
        var cards = population.KnownCandidates.Select(card => new PredictorRouteCardKnowledge(card,
            run._preview.Partial.ProjectionCauses(population with { Sources = [],
                Gaps = run._preview.Partial.Deck.TargetGaps(card.Id, PredictorDeckProjectionScope.PaelsTooth) }, false))).ToImmutableArray();
        var choice = new PredictorObtainChoiceKnowledge(PredictorPhase.ObtainDeckCards, source, cards, [], [], [], [])
            { MinimumCards = count ?? 5, MaximumCards = count ?? 5 };
        run._preview.Obtains.OptionalInputs.Add((run.Working.Position.NodeId, new(address, choice)));
        var authored = run.LiveObtainTargets(source, address).FirstOrDefault();
        if (authored != null && count is { } required && authored.Action == PredictorObtainChildAction.ChooseDeckCards &&
            authored.DeckCards.Length == required && authored.DeckCards.All(target =>
                run.Working.Deck.Contains(target.InstanceId) && run.Working.Card(target.InstanceId) is { } card &&
                card.Key == target.Key && card.FloorAdded == target.FloorAdded &&
                run._preview.Partial.Deck.ReadTarget(target.InstanceId, PredictorDeckProjectionScope.PaelsTooth) != null))
        {
            run._obtainDeckCount = required;
            if (run.Submit(run.Request!, new ChooseObtainDeckCards(authored.DeckCards.Select(target => target.InstanceId).ToImmutableArray()), token)
                != PredictorInputResult.Accepted) throw new InvalidOperationException("PredictorPaelsToothOptionalInputRejected");
            return true;
        }
        if (authored != null)
            AddDependency(dependencies, new(PredictorRouteCauseEvidence.CreateExplicit("PaelsToothPlannedTargetsPending",
                run.Working.Position.NodeId, PredictorCombatDomain.None, PredictorPendingReasonKind.PlanBinding,
                "The saved storage targets do not bind to this receipt's proven eligible population; the original intent is retained.",
                ["choice.obtain.PAELS_TOOTH.targets"], occurrenceKey: address.Occurrence.ToString())));
        var parents = run._preview.Partial.ProjectionCauses(population, false);
        var cause = PredictorRouteCauseEvidence.CreateExplicit("PaelsToothStorageTargetsPending", run.Working.Position.NodeId,
            PredictorCombatDomain.Deck, PredictorPendingReasonKind.UserChoice,
            "Pael's Tooth was acquired. Its eligible storage count is retained separately from the unspecified removed card identities.",
            ["choice.obtain.PAELS_TOOTH.targets"], parents, occurrenceKey: "paels-tooth:" + run._obtainDeckRelicId);
        var knowledge = new PredictorPaelsToothStorageKnowledge(run._obtainDeckRelicId, address,
            run.Working.Position.NodeId, count, count, [cause]);
        var possible = population.OrderedCandidatesKnown ? population.KnownCandidates : run.Working.Deck.Select(run.Working.Card).ToImmutableArray();
        run._preview.PaelsToothStorage = run._preview.PaelsToothStorage.SetItem(run._obtainDeckRelicId,
            new(knowledge, possible, population.OrderedCandidatesKnown));
        AddDependency(dependencies, new(cause, possible, MayChangeExistingCards: true)
        {
            DeckFacts = PredictorDeckUncertainFact.Membership,
            AffectedCardIds = population.OrderedCandidatesKnown ? possible.Select(card => card.Id).ToImmutableArray() : default,
            ChangedCardMembershipEntries = population.OrderedCandidatesKnown ? possible.Select(card => card.Key.Entry).Distinct().ToImmutableArray() : default
        });
        if (possible.Any(card => card.Key.Entry == "SPOILS_MAP"))
            AddDependency(dependencies, new(cause with { AffectedDomains = PredictorCombatDomain.Map }));
        run._obtainDeckPending = null;
        run._obtainDeckSource = default;
        run._obtainDeckCandidateOrder = [];
        run.Phase = PredictorPhase.Rewards;
        run._requestId++;
        run.CompleteObtainDestination(pending);
        run.ContinueObtainChildIfComplete(token);
        run.DrainObtainCompletions(token);
        return true;
    }

    private bool ProjectLivePaelsToothReturn(ref PredictorState state, PredictorRelic relic)
    {
        if (!_preview.PaelsToothStorage.TryGetValue(relic.Id, out var storage)) return false;
        int? remaining = storage.Knowledge.RemainingCount;
        string node = state.Position.NodeId;
        var before = storage.BeforeNodeCounts ?? ImmutableDictionary<string, LivePaelsToothBoundary>.Empty;
        if (!before.ContainsKey(node)) before = before.Add(node, new(remaining, storage.CombatsFinished));
        if (remaining == 0) return true;
        if (remaining is { } known) state = state.Draw(PredictorStream.Rewards, 0, known).State;
        else
        {
            var cursor = PredictorRouteCauseEvidence.CreateExplicit("PaelsToothReturnCountPending", node,
                PredictorCombatDomain.RewardsRng | PredictorCombatDomain.CardRewards | PredictorCombatDomain.GoldRewards |
                    PredictorCombatDomain.PotionRewards | PredictorCombatDomain.RelicRewards | PredictorCombatDomain.Odds,
                PredictorPendingReasonKind.DerivedDependency,
                "The real stored population may already be empty; its Rewards draw cannot be advanced from an unknown count.",
                ["rng.Rewards.state", "relic." + relic.Id + ".storedCount"], storage.Knowledge.PendingCauses,
                occurrenceKey: "paels-return-count:" + relic.Id + ":" + storage.CombatsFinished);
            _preview.PaelsToothCombatCauses = _preview.PaelsToothCombatCauses.Add(cursor);
        }
        var domains = PredictorCombatDomain.Deck | PredictorCombatDomain.InstanceIds;
        bool copies = PredictorSettlementEffects.Has(state, "BING_BONG") || Context.HasRunModifier("HOARDER");
        bool guaranteedSingle = remaining is > 0 && !copies;
        if (PredictorSettlementEffects.Has(state, "LUCKY_FYSH"))
        {
            if (guaranteedSingle) state = PredictorSettlementEffects.TakeGold(state, 15);
            else domains |= PredictorCombatDomain.Gold;
        }
        if (PredictorSettlementEffects.Has(state, "BING_BONG")) domains |= PredictorCombatDomain.Relics;
        if (PredictorSettlementEffects.Has(state, "BOOK_OF_FIVE_RINGS"))
        {
            if (guaranteedSingle)
            {
                state = state with { Relics = state.Relics.Select(listener => !listener.Melted && listener.Key.Entry == "BOOK_OF_FIVE_RINGS"
                    ? listener with { BookOfFiveRingsCardsAdded = checked(listener.BookOfFiveRingsCardsAdded + 1) } : listener).ToImmutableArray() };
                foreach (var listener in state.Relics.Where(listener => !listener.Melted && listener.Key.Entry == "BOOK_OF_FIVE_RINGS" &&
                    listener.BookOfFiveRingsCardsAdded % 5 == 0))
                    state = state with { CurrentHealthEffects = state.CurrentHealthEffects.Add(new(listener.Key, listener.Id, 0, 20)) };
            }
            else domains |= PredictorCombatDomain.Relics;
            domains |= PredictorCombatDomain.Health;
        }
        if (PredictorSettlementEffects.Has(state, "DARKSTONE_PERIAPT") &&
            (!storage.PopulationKnown || storage.PossibleCards.Any(card => card.Type == RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Curse)))
            domains |= PredictorCombatDomain.Health;
        var returned = PredictorRouteCauseEvidence.CreateExplicit("PaelsToothReturnedCardPending", node, domains,
            PredictorPendingReasonKind.DerivedDependency,
            "The actual return upgrades and adds one of the stored cards; its identity and real card-add hooks remain unresolved.",
            ["deck.paelsTooth.returnedCard"], storage.Knowledge.PendingCauses, occurrenceKey: "paels-return:" + relic.Id + ":" + storage.CombatsFinished);
        _preview.PaelsToothCombatCauses = _preview.PaelsToothCombatCauses.Add(returned);
        int finished = storage.CombatsFinished + 1;
        _preview.PaelsToothStorage = _preview.PaelsToothStorage.SetItem(relic.Id, storage with
        {
            Knowledge = storage.Knowledge with { RemainingCount = remaining is { } count ? count - 1 : finished >= 5 ? 0 : null },
            CombatsFinished = finished, BeforeNodeCounts = before
        });
        return true;
    }

    private void CopyLivePaelsToothFrom(PredictorRun source, bool replayNodeEntry = false)
    {
        _preview.PaelsToothStorage = source._preview.PaelsToothStorage;
        if (replayNodeEntry)
            _preview.PaelsToothStorage = _preview.PaelsToothStorage.ToImmutableDictionary(pair => pair.Key, pair =>
            {
                var storage = pair.Value;
                if (storage.BeforeNodeCounts != null && storage.BeforeNodeCounts.TryGetValue(source.Working.Position.NodeId, out var before))
                    return storage with { Knowledge = storage.Knowledge with { RemainingCount = before.RemainingCount },
                        CombatsFinished = before.CombatsFinished };
                return storage;
            });
    }
}
