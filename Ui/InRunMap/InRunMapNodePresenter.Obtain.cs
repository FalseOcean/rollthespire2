using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private static int OptionalObtainInputIndex(Context context, PredictorOptionalObtainInput optional) =>
        context.Plan.ObtainInputs.FindIndex(input => input.Input.ExpectedSource == optional.Choice.Source &&
            input.Address.Destination == optional.Address.Destination && input.Address.SourceId == optional.Address.SourceId &&
            input.Address.Occurrence == optional.Address.Occurrence && input.Address.ParentSourceIds.SequenceEqual(optional.Address.ParentSourceIds));

    private static PredictorObtainChoiceKnowledge? RequiredChild(PredictorRouteNodeKnowledge node) =>
        RewardObtainParent(node.Rewards)?.RequiredChoices ??
        RequiredRoomChild(node);
    private static PredictorCombatRewardKnowledge? RewardObtainParent(ImmutableArray<PredictorCombatRewardKnowledge> rewards)
    {
        var parent = rewards.FirstOrDefault(reward => reward.RequiredChoices != null);
        return parent?.RequiredChoices is { } child ? RewardObtainParent(child.Rewards) ?? parent : null;
    }
    private static PredictorObtainChoiceKnowledge? RequiredRoomChild(PredictorRouteNodeKnowledge node) =>
        node.Ancient?.RequiredChoices.FirstOrDefault() ?? node.Shop?.RequiredChoices.FirstOrDefault() ??
        (node.Event?.NeededInput == PredictorEventNeededInput.Obtain ? node.Event.RequiredChoices.FirstOrDefault() : null) ??
        node.Treasure?.RequiredChoices.FirstOrDefault();

    private bool AddObtain(Context c, PredictorPlannedObtainChild child)
    {
        if (RewardObtainParent(c.Node.Rewards) is { } parent) return AddRewardObtain(c, parent, child);
        if (c.Node.Ancient is { } ancient && c.Plan.Ancient is { } plan)
            return Set(c, c.Plan with { Ancient = plan with { Children = plan.Children.Take(ancient.AppliedChildren).Append(child).ToImmutableArray() } });
        if (c.Node.Shop != null) return AddShop(c, new(PredictorShopActionKind.Obtain) { Obtain = child });
        if (c.Node.Event != null) return AddEvent(c, new(PredictorEventActionKind.Obtain) { Obtain = child });
        if (c.Node.Treasure is { } treasure)
        {
            var prefix = (c.Plan.Treasure?.Actions ?? []).Take(treasure.AppliedActions).ToImmutableArray();
            // A child of the private default pickup still needs an authored
            // parent pickup when its first explicit choice is saved.
            if (c.Plan.Treasure == null && treasure.RelicTaken && treasure.OfferedRelic is { } relic && treasure.RelicPendingCauses.IsEmpty)
                prefix = [new(PredictorTreasureActionKind.Open), new(PredictorTreasureActionKind.TakeRelic) { ExpectedRelic = relic }];
            if (prefix.IsEmpty) return false;
            return Set(c, c.Plan with { Treasure = new() { Actions = prefix.Add(new(PredictorTreasureActionKind.Obtain) { Obtain = child }) } });
        }
        return false;
    }
    private bool AddRewardObtain(Context c, PredictorCombatRewardKnowledge parent, PredictorPlannedObtainChild child)
    {
        var choice = RewriteRewardObtain(c.Node.Rewards, parent, plan => plan with
        { Children = plan.Children.Take(parent.AppliedChildren).Append(child).ToImmutableArray() });
        return choice != null && RewardWriter(c) is { } write && write(c, choice);
    }
    private static PredictorPlannedRewardChoice? RewriteRewardObtain(ImmutableArray<PredictorCombatRewardKnowledge> rewards,
        PredictorCombatRewardKnowledge target, Func<PredictorPlannedRewardChoice, PredictorPlannedRewardChoice> update)
    {
        foreach (var parent in rewards)
        {
            if (parent.AppliedChoice is not { } plan) continue;
            if (ReferenceEquals(parent, target)) return update(plan);
            if (parent.RequiredChoices is not { } child || RewriteRewardObtain(child.Rewards, target, update) is not { } nested) continue;
            int index = plan.Children.FindIndex(step => SameRewardChoice(step.Reward, nested));
            if (index >= 0) return plan with
            { Children = plan.Children.Take(index).Append(plan.Children[index] with { Reward = nested }).ToImmutableArray() };
        }
        return null;
    }
    private void RewardObtainSections(Context c, EditorParts parts, ImmutableArray<PredictorCombatRewardKnowledge> rewards,
        bool mainSurface = false)
    {
        foreach (var parent in rewards.Where(reward => reward.RequiredChoices != null))
        {
            if (mainSurface) MainObtainEditor(c, parts, parent.RequiredChoices!, parent);
            else ObtainEditor(c, parts, parent.RequiredChoices!, parent);
            RewardObtainSections(c, parts, parent.RequiredChoices!.Rewards, mainSurface);
        }
    }

    // Main details consume sections, so field-based children need an embedded
    // editor. Keep the input with its actual parent instead of duplicating it
    // in premises just to make its fields reachable.
    private void MainObtainEditor(Context c, EditorParts parts, PredictorObtainChoiceKnowledge child,
        PredictorCombatRewardKnowledge? rewardParent = null)
    {
        var childParts = new EditorParts();
        ObtainEditor(c, childParts, child, rewardParent);
        if (childParts.Numbers.Count == 0 && childParts.Toggles.Count == 0 && childParts.Selections.Count == 0)
            parts.Sections.AddRange(childParts.Sections);
        else
            parts.Sections.Add(new("", "", [], [])
            { EmbeddedEditor = new(c.Node.Position.NodeId, _names.Resolve(child.Source, Kind(child.Source)),
                childParts.Sections, childParts.Numbers, childParts.Toggles, childParts.Selections, M("close_editor")) });
    }

    private void ObtainEditor(Context c, EditorParts parts, PredictorObtainChoiceKnowledge child,
        PredictorCombatRewardKnowledge? rewardParent = null, PredictorOptionalObtainInput? optional = null)
    {
        string role = "obtain-" + child.Source + "-" + child.Phase + "-" +
            (optional?.Address.Occurrence ?? rewardParent?.AppliedChildren ?? RewardObtainParent(c.Node.Rewards)?.AppliedChildren ??
                c.Node.Ancient?.AppliedChildren ?? c.Node.Shop?.AppliedActions ?? c.Node.Event?.AppliedActions ?? c.Node.Treasure?.AppliedActions ?? 0);
        bool Save(Context context, PredictorPlannedObtainChild input) => optional == null
            ? AddObtain(context, input)
            : Set(context, PredictorRun.ApplyOptionalObtainInput(context.Plan, optional, input));
        if (!child.PendingCauses.IsEmpty)
            parts.Sections.Add(new("", string.Join("\n", PendingLines(child.PendingCauses)), [], []));
        switch (child.Phase)
        {
            case PredictorPhase.ObtainChooseCard:
            {
                var choices = child.Cards.Select((card, index) => Choice(c, R("plan.take"), context => AddObtain(context,
                    new(PredictorObtainChildAction.ChooseCard, child.Source) { ExpectedCards = child.Cards, CardIndices = [index] }), Object(card),
                    enabled: child.PendingCauses.IsEmpty && !child.Cards.IsEmpty)).ToList();
                choices.Add(Choice(c, R("plan.skip"), context => AddObtain(context,
                    new(PredictorObtainChildAction.ChooseCard, child.Source) { ExpectedCards = child.Cards }), enabled: child.PendingCauses.IsEmpty && !child.Cards.IsEmpty));
                parts.Sections.Add(new(_text.Format("predictor.runtime.select_cards", 0, 1), "", [], choices) { LargeCardResults = true });
                break;
            }
            case PredictorPhase.ObtainChooseManyCards:
                CardSelection(c, parts, role, child.Cards, child.MinimumCards, child.MaximumCards, (context, indices) => AddObtain(context,
                    new(PredictorObtainChildAction.ChooseManyCards, child.Source) { ExpectedCards = child.Cards, CardIndices = indices }), child.PendingCauses.IsEmpty && !child.Cards.IsEmpty,
                    largeCardResults: true);
                break;
            case PredictorPhase.ObtainDeckCards:
            case PredictorPhase.TransformCards:
            case PredictorPhase.RewardRemoveCards:
            {
                var targets = child.DeckCards.Where(card => card.IsKnown && card.Value.Id > 0).ToImmutableArray();
                var cards = targets.Select(card => card.Value).ToImmutableArray();
                bool one = child.Phase is PredictorPhase.TransformCards or PredictorPhase.RewardRemoveCards;
                var action = child.Phase == PredictorPhase.TransformCards ? PredictorObtainChildAction.TransformCard :
                    child.Phase == PredictorPhase.RewardRemoveCards ? PredictorObtainChildAction.RemoveRewardCards : PredictorObtainChildAction.ChooseDeckCards;
                int savedIndex = optional == null ? -1 : OptionalObtainInputIndex(c, optional);
                var saved = savedIndex < 0 ? null : c.Plan.ObtainInputs[savedIndex].Input;
                var selected = optional == null ? default : cards.Select((card, index) => (card, index))
                    .Where(item => saved?.DeckCards.Any(target => target.InstanceId == item.card.Id &&
                        target.Key == item.card.Key && target.FloorAdded == item.card.FloorAdded) == true)
                    .Select(item => item.index).ToImmutableArray();
                CardSelection(c, parts, role, cards, one ? 1 : child.MinimumCards, one ? 1 : child.MaximumCards,
                    (context, indices) => Save(context, new(action, child.Source) { DeckCards = indices.Select(i => Target(cards[i])).ToImmutableArray() }),
                    selectedIndices: selected, display: card => TargetObject(targets.First(target => target.Value.Id == card.Id)));
                break;
            }
            case PredictorPhase.ChooseBundle:
                for (int index = 0; index < child.Bundles.Length; index++)
                {
                    int selected = index;
                    parts.Sections.Add(new(_text.Format("predictor.map.bundle", index + 1), "", child.Bundles[index].Select(Object).ToArray(),
                        [Choice(c, R("plan.take"), context => AddObtain(context, new(PredictorObtainChildAction.ChooseBundle, child.Source) {
                            ExpectedBundles = child.Bundles, BundleIndex = selected }), enabled: child.PendingCauses.IsEmpty && child.Bundles.Length == 2)]) { LargeCardResults = true });
                }
                break;
            case PredictorPhase.ObtainHealth:
                HealthFields(c, parts, role, (context, health) => AddObtain(context,
                    new(PredictorObtainChildAction.Health, child.Source) { Health = health }));
                break;
            case PredictorPhase.Rewards:
                if (rewardParent != null)
                    parts.Sections.AddRange(RewardSections(c, child.Rewards, (context, reward) => AddRewardObtain(context, rewardParent,
                        new(PredictorObtainChildAction.Reward, child.Source) { Reward = reward })));
                // Complete rewards are already in the hover and editor reward
                // section; finishing is a separate explicit child action.
                parts.Sections.Add(new("", "", [], [Choice(c, R("leave_rewards"), context => rewardParent == null ? AddObtain(context,
                    new(PredictorObtainChildAction.LeaveRewards, child.Source)) : AddRewardObtain(context, rewardParent,
                    new(PredictorObtainChildAction.LeaveRewards, child.Source)), enabled: !child.Rewards.Any(reward => reward.RequiredChoices != null) && child.Rewards.All(r => r.IsKnown &&
                        r.Value is { } reward && (reward.Status != PredictorRewardStatus.Open || reward.CanSkip && !reward.Mandatory)))]));
                break;
            case PredictorPhase.ReplacePotion:
                parts.Sections.Add(new(R("replace_potion"), "", [], [Choice(c, R("cancel"), context => AddObtain(context,
                    new(PredictorObtainChildAction.CancelPotionReplacement, child.Source)))]));
                break;
        }
    }
}
