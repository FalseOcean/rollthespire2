using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    // Current route intent contains no cached simulation output. Abandoned
    // branch intent is retained only by the temporary redraw undo opportunity.
    internal ImmutableArray<PredictorPlannedNode> PlannedNodes { get; private set; } = [];

    internal void SetPlannedNode(PredictorPlannedNode plan)
    {
        RequireRouteSimulation();
        ValidatePlannedNodes([plan]);
        int index = PlannedNodes.FindIndex(p => p.NodeId == plan.NodeId);
        if (index >= 0 && PlannedNodes[index] == plan) return;
        PlannedNodes = index < 0 ? PlannedNodes.Add(plan) : PlannedNodes.SetItem(index, plan);
        CompleteRouteRedrawUndo();
    }

    internal void RestorePlannedNodes(ImmutableArray<PredictorPlannedNode> plans)
    {
        RequireRouteSimulation();
        ValidatePlannedNodes(plans);
        plans = PlansForRoutes(plans);
        if (PlannedNodes.SequenceEqual(plans)) return;
        PlannedNodes = plans;
        CompleteRouteRedrawUndo();
    }

    internal void RemovePlannedNode(string nodeId)
    {
        RequireRouteSimulation();
        if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("PredictorPlannedNodeIdInvalid");
        if (!PlannedNodes.Any(p => p.NodeId == nodeId)) return;
        PlannedNodes = PlannedNodes.RemoveAll(p => p.NodeId == nodeId);
        CompleteRouteRedrawUndo();
    }

    // Validate the authored source/target shape, never today's route, inventory
    // or catalog membership. A future or presently unbound identity remains
    // saved intent and is resolved by the projection at its actual boundary.
    internal static void ValidatePlannedNodes(ImmutableArray<PredictorPlannedNode> plans)
    {
        RequirePlan(!plans.IsDefault && plans.All(p => p != null && !string.IsNullOrWhiteSpace(p.NodeId)));
        RequirePlan(plans.Select(p => p.NodeId).Distinct(StringComparer.Ordinal).Count() == plans.Length);
        foreach (var plan in plans)
        {
            RequirePlan(!plan.ObtainInputs.IsDefault && plan.ObtainInputs.All(optional => optional != null &&
                optional.Address != null && Enum.IsDefined(optional.Address.Destination) && optional.Address.SourceId >= 0 &&
                optional.Address.Occurrence >= 0 && !optional.Address.ParentSourceIds.IsDefault &&
                optional.Address.ParentSourceIds.All(id => id >= 0) && optional.Input != null &&
                optional.Input.Action is PredictorObtainChildAction.ChooseDeckCards or PredictorObtainChildAction.TransformCard));
            RequirePlan(plan.ObtainInputs.Select(optional => optional.Address.Occurrence).Distinct().Count() == plan.ObtainInputs.Length);
            foreach (var optional in plan.ObtainInputs) ValidatePlannedObtainChild(optional.Input);
            RequirePlan(!plan.RestActions.IsDefault && !plan.RewardChoices.IsDefault);
            RequirePlan(!plan.EventOccurrenceExclusions.IsDefault &&
                plan.EventOccurrenceExclusions.All(e => e != null && e.QueueEvent.IsValid &&
                    e.QueueEvent.Category == "EVENT" && e.Cursor >= 0) &&
                plan.EventOccurrenceExclusions.Distinct().Count() == plan.EventOccurrenceExclusions.Length);
            ValidatePlannedPotionActions(plan.PotionActions);
            foreach (var action in plan.RestActions)
            {
                RequirePlan(action != null && Enum.IsDefined(action.Action) && !action.CookCards.IsDefault);
                RequirePlan(action.SmithCard == null || action.Action == PredictorRestAction.Smith);
                RequirePlan(action.Action == PredictorRestAction.Cook || action.CookCards.IsEmpty);
                RequirePlan(action.MaximumHp == null || action.Action == PredictorRestAction.Heal && action.MaximumHp > 0);
                if (action.SmithCard is { } smith) ValidatePlannedCard(smith);
                foreach (var card in action.CookCards) ValidatePlannedCard(card);
                RequirePlan(action.CookCards.IsEmpty || action.CookCards.Length == 2 &&
                    action.CookCards.Select(c => c.InstanceId).Distinct().Count() == 2);
            }
            foreach (var choice in plan.RewardChoices)
            {
                RequirePlan(choice != null && choice.ExpectedReward != null && choice.Occurrence >= 0);
                var reward = choice.ExpectedReward;
                RequirePlan(Enum.IsDefined(reward.Kind) && Enum.IsDefined(reward.Origin) && Enum.IsDefined(reward.Status) &&
                    !reward.Cards.IsDefault && reward.Id >= 0 && reward.ContentInstanceId is not < 1 &&
                    reward.GoldMinimum >= 0 && reward.GoldMaximum >= reward.GoldMinimum && reward.GoldAmount >= 0 &&
                    reward.HasValidGoldDisposition);
                ValidateRewardEnchantmentMask(reward, choice.UnresolvedEnchantmentIndices);
                RequirePlan(reward.Kind != PredictorRewardKind.Relic || reward.Key.IsValid && reward.Key.Category == "RELIC");
                RequirePlan(reward.Kind != PredictorRewardKind.Potion || reward.Key.IsValid && reward.Key.Category == "POTION");
                ValidatePlannedRewardAlternatives(choice);
                var selectedOffer = choice.ExpectedRerolledReward ?? reward;
                RequirePlan(choice.CardIndex == null || !choice.Skip && (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) &&
                    choice.CardIndex >= 0 && choice.CardIndex < selectedOffer.Cards.Length);
                foreach (var card in reward.Cards)
                    RequirePlan(card != null && card.Key.IsValid && card.Key.Category == "CARD" && card.Id >= 0 &&
                        Enum.IsDefined(card.Type) && card.UpgradeLevel >= 0 && card.MaxUpgradeLevel >= card.UpgradeLevel &&
                        card.FloorAdded >= 0);
            }
            if (plan.Combat is { } combat) ValidatePlannedCombat(combat);
            if (plan.Shop is { } shop) ValidatePlannedShop(shop);
            if (plan.Ancient is { } ancient) ValidatePlannedAncient(ancient);
            if (plan.Event is { } plannedEvent) ValidatePlannedEvent(plannedEvent);
            if (plan.Treasure is { } treasure) ValidatePlannedTreasure(treasure);
        }
    }

    private static void ValidatePlannedCard(PredictorPlannedCard card) => RequirePlan(card != null &&
        card.InstanceId > 0 && card.Key.IsValid && card.Key.Category == "CARD" && card.FloorAdded >= 0);

    private static void ValidatePlannedCombat(PredictorCombatNodeOverrides combat)
    {
        RequirePlan(!combat.CardTriggers.IsDefault && !combat.RelicEffects.IsDefault &&
            !combat.TemporaryCardTriggers.IsDefault && !combat.CombatCardSourceOrder.IsDefault &&
            !combat.EnemyCreations.IsDefault && !combat.ExtraRewardOrder.IsDefault);
        RequirePlan(combat.CardTriggers.All(t => t != null && t.InstanceId > 0 && t.EffectiveTriggers >= 0) &&
            combat.CardTriggers.Select(t => t.InstanceId).Distinct().Count() == combat.CardTriggers.Length);
        RequirePlan(combat.RelicEffects.All(r => r != null && r.RelicId > 0) &&
            combat.RelicEffects.Select(r => r.RelicId).Distinct().Count() == combat.RelicEffects.Length);
        RequirePlan(combat.TemporaryCardTriggers.All(t => t != null && t.Card.IsValid && t.Card.Category == "CARD" &&
            t.UpgradeLevel >= 0 && t.EffectiveTriggers >= 0));
        RequirePlan(combat.CombatCardSourceOrder.All(r => r != null &&
            r.DeckCardId.HasValue != r.TemporarySourceIndex.HasValue && r.DeckCardId is not < 1 &&
            (r.TemporarySourceIndex == null || r.TemporarySourceIndex >= 0 && r.TemporarySourceIndex < combat.TemporaryCardTriggers.Length)));
        RequirePlan(combat.EnemyCreations.All(e => e != null && Enum.IsDefined(e.Source) && e.Count >= 0) &&
            combat.EnemyCreations.Select(e => e.Source).Distinct().Count() == combat.EnemyCreations.Length);
        RequirePlan(combat.ExtraRewardOrder.All(r => r != null && Enum.IsDefined(r.Source) && r.Index >= 0) &&
            combat.ExtraRewardOrder.Distinct().Count() == combat.ExtraRewardOrder.Length);
        if (combat.StolenCards is { } stolen)
            RequirePlan(!stolen.Cards.IsDefault && stolen.Cards.All(c => c != null && c.CardId > 0) &&
                stolen.Cards.Select(c => c.CardId).Distinct().Count() == stolen.Cards.Length);
        if (combat.VictoryHealth is { } health)
            RequirePlan(health.MaxHp > 0 && health.CurrentHp > 0 && health.CurrentHp <= health.MaxHp);
        if (combat.GremlinGold is { } gremlin)
            RequirePlan(gremlin.RecoveredGold is not < 0 && gremlin.StolenGoldLoss is not < 0 &&
                !(gremlin.FatGremlinEscaped && gremlin.RecoveredGold != null) &&
                !(gremlin.RecoveredGold > 0 && !gremlin.GoldWasStolen) &&
                !(gremlin.RecoverAllStolenGold && (gremlin.FatGremlinEscaped || !gremlin.GoldWasStolen ||
                    gremlin.RecoveredGold != null || gremlin.StolenGoldLoss != null)) &&
                (gremlin.StolenGoldLoss == null || gremlin.GoldWasStolen));
        if (combat.Potions is { } potions)
        {
            RequirePlan(!potions.Changes.IsDefault && potions.NoAdditionalPotionChange == potions.Changes.IsEmpty);
            foreach (var change in potions.Changes)
            {
                RequirePlan(change != null && Enum.IsDefined(change.Action) && change.BeforeOccurrence >= 0 &&
                    change.BeforeCardId is not < 1 && change.RelicId is not < 1 &&
                    !(change.BeforeCardId != null && change.BeforeTemporarySourceIndex != null) &&
                    (change.BeforeTemporarySourceIndex == null || change.BeforeTemporarySourceIndex >= 0 &&
                        change.BeforeTemporarySourceIndex < combat.TemporaryCardTriggers.Length) &&
                    (change.BeforeCardId != null || change.BeforeTemporarySourceIndex != null || change.BeforeOccurrence == 0));
                bool selectsPotion = change.Action is PredictorCombatPotionAction.Use or PredictorCombatPotionAction.Discard or
                    PredictorCombatPotionAction.FairyTriggered;
                RequirePlan(selectsPotion == (change.Potion != null) &&
                    (change.Action == PredictorCombatPotionAction.LizardTailTriggered) == (change.RelicId != null));
                if (change.Potion is { } reference)
                    RequirePlan(reference.ExistingId.HasValue != reference.GeneratedOrdinal.HasValue &&
                        reference.ExistingId is not < 1 && reference.GeneratedOrdinal is not < 0);
            }
        }
    }

    private static void RequirePlan([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new InvalidDataException("PredictorPlannedNodesMalformed");
    }
}
