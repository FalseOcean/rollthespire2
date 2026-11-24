using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private PredictorState ReceiveRewardCard(PredictorReward reward, PredictorCard card,
        PredictorCombatRewardKnowledge? offeredKnowledge = null)
    {
        var before = Working;
        var knowledge = offeredKnowledge ?? _preview.Partial?.RewardKnowledge(this, reward);
        static PredictorState Receive(PredictorContext context, PredictorState state, PredictorCard offered)
        {
            if (!state.Cards.Any(existing => existing.Id == offered.Id)) state = state with { Cards = state.Cards.Add(offered) };
            else state = state.WithCard(offered);
            return PredictorObtainSources.AddExistingCard(context, state, offered);
        }
        var after = Receive(Context, before, card);
        _preview.Partial?.ReceiveRewardCard(before, after, reward, card, knowledge, Receive);
        return after;
    }

    private sealed partial class RoutePartialState
    {
        private readonly HashSet<long> _knownRewardCardIds = [];

        internal void ObserveRewardSourceIds(PredictorReward reward)
        {
            if (!Domain(PredictorCombatDomain.InstanceIds).IsEmpty) return;
            bool payloadUnknown = !Domain(PredictorCombatDomain.NicheRng).IsEmpty;
            for (int index = 0; index < reward.Cards.Length; index++)
                if (!payloadUnknown || reward.CardRewardNicheScope?.UnresolvedCardInstanceIndices.Contains(index) != true)
                    _knownRewardCardIds.Add(reward.Cards[index].Id);
        }

        internal PredictorCombatRewardKnowledge? RewardKnowledge(PredictorRun run, PredictorReward reward) =>
            ObserveRouteRewards(run, _dependencies).ElementAtOrDefault(run.Rewards.IndexOf(reward));

        internal PredictorCombatRewardKnowledge? EventOfferedCardKnowledge(PredictorRun run) =>
            ObserveRouteEventOfferedCards(run, _dependencies);

        internal ImmutableArray<int> UncertainLateRewardSourceIds(PredictorReward before, IReadOnlyList<PredictorCard> after) =>
            Domain(PredictorCombatDomain.InstanceIds).IsEmpty ? [] : Enumerable.Range(0, before.Cards.Length)
                .Where(index => before.Cards[index].Id != after[index].Id).ToImmutableArray();

        internal void LateRewardInstanceAllocation(PredictorReward reward, string listener)
        {
            var parents = Domain(PredictorCombatDomain.NicheRng);
            if (parents.IsEmpty) return;
            AddDependency(_dependencies, new(PredictorRouteCauseEvidence.CreateExplicit(
                "CardRewardListenerInstanceAllocationPending", _observed.Position.NodeId, PredictorCombatDomain.InstanceIds,
                PredictorPendingReasonKind.DerivedDependency,
                "A late card reward listener clones only changed options; its unresolved enchantment eligibility changes the instance allocation count.",
                ["reward.card.instanceId", "state.nextInstanceId"], parents,
                occurrenceKey: PredictorRouteCauseEvidence.Key("late-reward-listener", reward.Id.ToString(), listener))));
        }

        internal void ReceiveRewardCard(PredictorState before, PredictorState after, PredictorReward reward,
            PredictorCard card, PredictorCombatRewardKnowledge? knowledge,
            Func<PredictorContext, PredictorState, PredictorCard, PredictorState> receive)
        {
            int index = reward.Cards.IndexOf(card);
            var option = knowledge?.CardOptions.ElementAtOrDefault(index);
            var payloadCauses = option?.EnchantmentPendingCauses ?? [];
            var idCauses = Domain(PredictorCombatDomain.InstanceIds);
            bool sourceIdUncertain = (!payloadCauses.IsEmpty || !idCauses.IsEmpty) &&
                reward.CardRewardNicheScope?.UnresolvedCardInstanceIndices.Contains(index) == true ||
                !idCauses.IsEmpty && !_knownRewardCardIds.Contains(card.Id);
            if (payloadCauses.IsEmpty && !sourceIdUncertain && (idCauses.IsEmpty || before.NextInstanceId == after.NextInstanceId)) return;
            Synchronize(before);
            var variants = payloadCauses.IsEmpty ? ImmutableArray.Create(PredictorCardEnchantment.From(card)) :
                reward.CardRewardNicheScope?.EnchantmentAlternatives.ElementAtOrDefault(index) ?? [];
            string operation = PredictorRouteCauseEvidence.Key("card-receipt", reward.Id.ToString(), index.ToString());
            var parents = PredictorRouteCauseEvidence.Normalize(payloadCauses.AddRange(idCauses));
            // New producers record every possible payload at the listener
            // boundary. A legacy/incomplete detached capture keeps the actual
            // receipt count and settled hooks, but cannot certify its payloads.
            bool finite = !variants.IsDefaultOrEmpty;
            var outcomes = finite ? variants.Select((value, ordinal) =>
                (Key: "enchantment:" + ordinal, State: receive(_context, before, value.Apply(card)))).ToImmutableArray() : [];
            bool allocationVaries = !finite || !idCauses.IsEmpty || outcomes.Any(outcome => outcome.State.NextInstanceId != after.NextInstanceId);
            var domains = PredictorCombatDomain.Deck | (allocationVaries ? PredictorCombatDomain.InstanceIds : PredictorCombatDomain.None);
            var cause = PredictorRouteCauseEvidence.Bind(PredictorRouteCauseEvidence.CreateExplicit(
                "CardRewardEnchantmentPending", before.Position.NodeId, domains,
                finite ? PredictorPendingReasonKind.DerivedDependency : PredictorPendingReasonKind.ImplementationGap,
                finite ? "The selected card was received; only its unresolved enchantment and dependent acquisition effects remain conditional." :
                    "The selected card was received, but this captured reward lacks the listener payload alternatives needed to prove its card effects.",
                ["choice.reward.card.enchantment"], parents, occurrenceKey: operation,
                choice: new(PredictorRouteCauseEvidence.Key(before.Position.NodeId, operation),
                    "RewardPayload", card.Key.Serialized, index)));
            _choices.Add(cause.ChoiceId!, cause);
            if (finite)
                Deck = Deck.AddStateChoice(cause.ChoiceId!, before.Position.NodeId, before, after, outcomes,
                    preserveCommonAdditionIds: !allocationVaries && !sourceIdUncertain);
            else
                Deck = Deck.MarkUnknownAddition(before.Position.NodeId, "CardRewardPayloadAlternativesMissing", cause.ChoiceId,
                    [card.Type], receiptEffectsSettled: true, knownCount: after.Deck.Length - before.Deck.Length);
            var possible = finite ? outcomes.SelectMany(outcome => outcome.State.Deck.Where(id => !before.Deck.Contains(id))
                .Select(outcome.State.Card)).Distinct().ToImmutableArray() : [];
            AddDependency(_dependencies, new(cause, possible)
            {
                ChangedCardMembershipEntries = finite ? possible.Select(value => value.Key.Entry).Distinct().ToImmutableArray() : [card.Key.Entry],
                ChangedCardStateEntries = [],
                AffectedCardIds = possible.Select(value => value.Id).Distinct().ToImmutableArray(),
                DeckFacts = PredictorDeckUncertainFact.None
            });
            // The local finite choice, rather than Synchronize's witness import,
            // owns additions and their payloads. Receipt Gold/Book/health hooks
            // are common to these outcomes and stay on the actual state.
            _observed = after;
            Facts.RecordUnknown("reward.receipt." + reward.Id + ".enchantment", before.Position.NodeId,
                PredictorRouteFactPhase.Results, [cause], operation: "actual card receipt / local payload alternatives");
        }
    }
}
