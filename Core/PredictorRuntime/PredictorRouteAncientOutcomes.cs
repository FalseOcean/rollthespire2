using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private PredictorState? _ancientOutcomeBefore;
    private ModelKey _ancientOutcomeSource;
    private int _ancientOutcomeInputIndex;
    private ImmutableHashSet<long> _ancientKnownBefore = [];
    private ImmutableArray<ImmutableArray<PredictorCard>> _ancientBundles = [];
    private int _ancientBundleChildIndex;

    // Called at the actual acquisition, including an active-node replay. The
    // final node inventory must never be mistaken for its pre-acquisition target.
    private void BeginAncientOutcomeObservation(ModelKey source)
    {
        _preview.Partial?.Synchronize(Working);
        _ancientOutcomeBefore = Working;
        _ancientOutcomeSource = source;
        _ancientOutcomeInputIndex = _inputs.Length;
        _ancientKnownBefore = Working.Deck.Where(id => _preview.Partial != null
            ? _preview.Partial.Deck.ReadTarget(id, PredictorDeckProjectionScope.All) != null
            : !(Context.Live?.PendingCauses.Any(cause => (cause.AffectedDomains & PredictorCombatDomain.Deck) != 0) ?? false))
            .ToImmutableHashSet();
        _ancientBundles = [];
    }

    private void RecordAncientBundleObservation()
    {
        _ancientBundles = _scrollBoxBundles;
        _ancientBundleChildIndex = _nodeReplayInputs.Count(IsRouteObtainChildInput);
    }

    private PredictorAncientBundleChoice? ObserveAncientBundle(PredictorAncientOffer? parent,
        List<RouteDependency> dependencies)
    {
        if (parent == null || _ancientOutcomeBefore?.Position.NodeId != Working.Position.NodeId || _ancientBundles.IsEmpty)
            return null;
        var pending = CausesFor(dependencies, PredictorCombatDomain.RewardsRng);
        var choice = new PredictorObtainChoiceKnowledge(PredictorPhase.ChooseBundle, new("RELIC", "SCROLL_BOXES"),
            [], [], pending.IsEmpty ? _ancientBundles.Select(bundle => bundle.Select(DisplayObtainCard).ToImmutableArray()).ToImmutableArray() : [],
            [], pending);
        var receipt = _inputs.LastOrDefault(input => input.Action == "ChooseScrollBoxBundle");
        return new(choice, _ancientBundleChildIndex, receipt == null ? null : checked((int)receipt.ObjectId), parent with { Id = 0 });
    }

    private PredictorAncientOutcomeKnowledge? ObserveAncientOutcome(List<RouteDependency> dependencies,
        ImmutableArray<PredictorCombatRewardKnowledge> displayedRewards)
    {
        if (_ancientOutcomeBefore is not { } before || before.Position.NodeId != Working.Position.NodeId) return null;
        _preview.Partial?.Synchronize(Working);
        bool Known(PredictorCard card) => _preview.Partial == null ||
            _preview.Partial.Deck.ReadTarget(card.Id, PredictorDeckProjectionScope.All) != null;
        var receipts = _inputs.Skip(_ancientOutcomeInputIndex).ToImmutableArray();
        var shown = displayedRewards.Where(reward => reward.IsKnown && reward.Value?.Status == PredictorRewardStatus.Taken)
            .Select(reward => reward.Value!).ToArray();
        var rewardCards = receipts.Where(input => input.Action == "TakeReward").SelectMany(input => input.Targets)
            .Where(id => Working.Cards.Any(card => card.Id == id) && shown.Any(reward =>
                reward.Kind is (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) &&
                reward.Cards.Any(card => DisplayObtainCard(card) == DisplayObtainCard(Working.Card(id)))))
            .ToHashSet();
        var shownRelics = shown.Where(reward => reward.Kind == PredictorRewardKind.Relic).GroupBy(reward => reward.Key)
            .ToDictionary(group => group.Key, group => group.Count());
        var transformations = receipts.SelectMany(input => input.Transformations).ToArray();
        var changed = ImmutableArray.CreateBuilder<PredictorEventCardChangeKnowledge>();
        foreach (var card in before.Deck.Select(before.Card).Where(card => _ancientKnownBefore.Contains(card.Id)))
        {
            var transformation = transformations.FirstOrDefault(change => change.OriginalId == card.Id);
            if (transformation != null)
            {
                var replacement = Working.Card(transformation.ReplacementId);
                if (Known(replacement)) changed.Add(new("Transform", DisplayObtainCard(card), DisplayObtainCard(replacement)));
            }
            else if (!Working.Deck.Contains(card.Id))
            {
                // An unspecified storage/removal projection keeps the witness
                // member in place; only actual removals reach this branch.
                changed.Add(new("Remove", DisplayObtainCard(card), null));
            }
            else if (Working.Card(card.Id) is { } after && after != card && Known(after))
                changed.Add(new(after.UpgradeLevel > card.UpgradeLevel ? "Upgrade" :
                    after.UpgradeLevel < card.UpgradeLevel ? "Downgrade" : "Enchant", DisplayObtainCard(card), DisplayObtainCard(after)));
        }
        var transformedIds = transformations.Select(change => change.ReplacementId).ToHashSet();
        var added = Working.Deck.Where(id => !before.Deck.Contains(id) && !rewardCards.Contains(id) && !transformedIds.Contains(id))
            .Select(Working.Card).Where(Known).Select(DisplayObtainCard).ToImmutableArray();
        var relicKnown = CausesFor(dependencies, PredictorCombatDomain.Relics).IsEmpty;
        var acquired = relicKnown ? Working.Relics.Where(relic => !before.Relics.Any(old => old.Id == relic.Id) &&
                relic.Key != _ancientOutcomeSource).Select(relic => relic.Key).Where(key =>
                {
                    if (shownRelics.GetValueOrDefault(key) == 0) return true;
                    shownRelics[key]--;
                    return false;
                }).ToImmutableArray() : [];
        var removed = relicKnown ? before.Relics.Where(relic => !Working.Relics.Any(current => current.Id == relic.Id))
            .Select(relic => relic.Key).ToImmutableArray() : [];
        var potions = ImmutableArray.CreateBuilder<PredictorAncientPotionOutcome>();
        if (CausesFor(dependencies, PredictorCombatDomain.CombatPotionRng | PredictorCombatDomain.Relics).IsEmpty)
        {
            bool slotsKnown = CausesFor(dependencies, PredictorCombatDomain.PotionInventory).IsEmpty;
            var attempts = receipts.SelectMany(input => input.PotionAttempts).ToArray();
            foreach (var attempt in attempts)
                potions.Add(new(attempt.Potion.Key, slotsKnown ? attempt.Result : null));
            foreach (var bottle in receipts.SelectMany(input => input.GeneratedPotions).Where(bottle => !attempts.Any(attempt => attempt.Potion.Id == bottle.Id)))
                potions.Add(new(bottle.Key, !slotsKnown ? null : Working.PotionSlots.Any(slot => slot?.Id == bottle.Id)
                    ? PredictorInputResult.Accepted : PredictorSettlementEffects.Has(Working, "SOZU")
                        ? PredictorInputResult.NotAllowed : PredictorInputResult.TooFull));
        }
        return new(_ancientOutcomeSource, added, changed.ToImmutable(), acquired, removed, potions.ToImmutable());
    }

    private static bool CompleteLiveAncientBundle(PredictorRun run, List<RouteDependency> dependencies, CancellationToken token)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || run._preview.Partial == null ||
            run.Phase != PredictorPhase.ChooseBundle || run._ancientOutcomeSource.Entry != "SCROLL_BOXES" ||
            run._ancientOutcomeBefore?.Position.NodeId != run.Working.Position.NodeId || run._scrollBoxBundles.Length != 2 ||
            run._newLeafObtain is not { } completion) return false;
        var before = run.Working;
        var outcomes = run._scrollBoxBundles.Select((bundle, index) =>
        {
            var state = before;
            foreach (var card in bundle) state = PredictorObtainSources.AddExistingCard(run.Context, state, card);
            return (Key: index.ToString(), State: state);
        }).ToImmutableArray();
        if (outcomes.Any(outcome => !Same(outcome.State.Streams, before.Streams)))
            throw new InvalidOperationException("PredictorAncientBundleUnexpectedRandomConsumption");
        // This local comparison executes only the real two acquisition tails.
        // It does not submit a picker input, author a choice or explore routes.
        run.Working = run._preview.Partial.RecordAncientBundleChoice(before, outcomes);
        run._scrollBoxBundles = [];
        run._newLeafObtain = null;
        run.Phase = PredictorPhase.Rewards;
        run._requestId++;
        run.CompleteObtainDestination(completion);
        run.ContinueObtainChildIfComplete(token);
        run.DrainObtainCompletions(token);
        return true;
    }

    private sealed partial class RoutePartialState
    {
        internal PredictorState RecordAncientBundleChoice(PredictorState before,
            ImmutableArray<(string Key, PredictorState State)> outcomes)
        {
            Synchronize(before);
            var witness = outcomes[0].State;
            bool Varies<T>(Func<PredictorState, T> value) => outcomes.Any(outcome => !Same(value(witness), value(outcome.State)));
            var domains = PredictorCombatDomain.Deck;
            if (Varies(state => state.GoldBudget)) domains |= PredictorCombatDomain.Gold;
            if (Varies(state => state.Relics)) domains |= PredictorCombatDomain.Relics;
            if (Varies(state => state.PotionSlots)) domains |= PredictorCombatDomain.PotionInventory;
            if (Varies(state => state.NextInstanceId)) domains |= PredictorCombatDomain.InstanceIds;
            if (Varies(state => state.HoarderCardsToSkip)) domains |= PredictorCombatDomain.CardRewardHookState;
            if (Varies(state => state.CurrentHealthEffects)) domains |= PredictorCombatDomain.Health;
            // Earlier unknown creation hooks cannot be certified by comparing
            // two representatives with the same unknown hook environment.
            var parents = Domain(PredictorCombatDomain.RewardsRng | PredictorCombatDomain.Relics);
            if (!parents.IsEmpty) domains |= PredictorCombatDomain.Gold | PredictorCombatDomain.Relics |
                PredictorCombatDomain.Health | PredictorCombatDomain.InstanceIds | PredictorCombatDomain.CardRewardHookState;
            var cause = PredictorRouteCauseEvidence.CreateExplicit("AncientBundleUnselected", before.Position.NodeId, domains,
                PredictorPendingReasonKind.UserChoice,
                "Scroll Boxes generated both real bundles; no bundle was selected. Only their differing acquisition effects remain unresolved.",
                ["choice.obtain.SCROLL_BOXES.bundle"], parents, occurrenceKey: "ancient-bundle");
            cause = PredictorRouteCauseEvidence.Bind(cause);
            _choices.Add(cause.ChoiceId!, cause);
            Deck = Deck.AddStateChoice(cause.ChoiceId!, before.Position.NodeId, before, witness, outcomes);
            if (!parents.IsEmpty)
                Deck = Deck.MarkChoiceAdditionPending(before.Position.NodeId, cause.ChoiceId!, []);
            var possible = outcomes.SelectMany(outcome => outcome.State.Deck.Where(id => !before.Deck.Contains(id))
                .Select(outcome.State.Card)).Distinct().ToImmutableArray();
            AddDependency(_dependencies, new(cause, possible, MayChangeExistingCards: true)
            {
                ChangedRelicEntries = [], ChangedRelicStateEntries = witness.Relics.Where(relic => outcomes.Any(outcome =>
                    outcome.State.Relics.FirstOrDefault(other => other.Id == relic.Id) != relic)).Select(relic => relic.Key.Entry).Distinct().ToImmutableArray(),
                ChangedCardMembershipEntries = possible.Select(card => card.Key.Entry).Distinct().ToImmutableArray(),
                ChangedCardStateEntries = [], AffectedCardIds = possible.Select(card => card.Id).Distinct().ToImmutableArray()
            });
            // The correlated deck already includes the private additions.
            _observed = witness;
            Facts.RecordResult("ancient.bundle.selection", before.Position.NodeId, "unselected", [cause],
                operation: "two real local bundle acquisition tails; common stream state retained");
            return witness;
        }
    }
}
