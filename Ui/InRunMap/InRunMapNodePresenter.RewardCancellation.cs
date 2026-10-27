using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    // Cancellation is an explicit UI intent, not an empty reward action.
    // Remove only its authored take or alternative; an independently adopted
    // reroll is retained when cancelling the take. The live default owns Skip.
    // Completed obtain receipts identify the parent path without replaying or
    // replacing those parents, their other children or unrelated node plans.
    private static PredictorPlannedNode CancelRewardAlternativePlan(Context context,
        PredictorCombatRewardKnowledge source, PredictorPlannedRewardChoice choice) =>
        CancelRewardPlan(context, source, choice, cancelTake: false);

    private static PredictorPlannedNode CancelRewardTakePlan(Context context,
        PredictorCombatRewardKnowledge source, PredictorPlannedRewardChoice choice) =>
        CancelRewardPlan(context, source, choice, cancelTake: true);

    private static PredictorPlannedNode CancelRewardPlan(Context context,
        PredictorCombatRewardKnowledge source, PredictorPlannedRewardChoice choice, bool cancelTake)
    {
        var current = context.Plan;
        var edit = source.CompletedEdit;
        var path = (edit?.RewardParents ?? []).Add(new(edit?.Source, choice));
        PredictorRewardEditOwner? owner = edit?.Owner ?? (context.Node.Room is PredictorRoomKind.Monster or PredictorRoomKind.Elite or
            PredictorRoomKind.Boss or PredictorRoomKind.RestSite ? PredictorRewardEditOwner.Reward :
            context.Node.Event != null ? PredictorRewardEditOwner.Event : context.Node.Ancient != null ? PredictorRewardEditOwner.Ancient :
            context.Node.Shop != null ? PredictorRewardEditOwner.Shop : context.Node.Treasure != null ? PredictorRewardEditOwner.Treasure :
            (PredictorRewardEditOwner?)null);
        if (owner == null) throw new InvalidOperationException("MapRewardAlternativeCancelOwnerMissing");

        // The replacement offer binds a follow-up take/skip after a reroll.
        // Removing that follow-up restores the original reroll intent.
        PredictorPlannedRewardChoice? CancelLeaf(PredictorPlannedRewardChoice currentChoice) =>
            cancelTake && (currentChoice.Reroll || currentChoice.SacrificeRelicId != null)
                ? currentChoice with { CardIndex = null, Skip = false, ExpectedRerolledReward = null } : null;

        ImmutableArray<PredictorPlannedObtainChild> RemoveChild(ImmutableArray<PredictorPlannedObtainChild> children, int depth)
        {
            var target = path[depth];
            int index = children.FindIndex(child => child.Action == PredictorObtainChildAction.Reward &&
                (target.Source == null || child.ExpectedSource == target.Source) && SameRewardChoice(child.Reward, target.Choice));
            if (index < 0) throw new InvalidOperationException("MapRewardAlternativeCancelChildMissing");
            if (depth == path.Length - 1) return CancelLeaf(children[index].Reward!) is { } retained
                ? children.SetItem(index, children[index] with { Reward = retained }) : children.RemoveAt(index);
            var child = children[index];
            return children.SetItem(index, child with { Reward = child.Reward! with
                { Children = RemoveChild(child.Reward.Children, depth + 1) } });
        }
        PredictorPlannedRewardChoice RewriteParent(PredictorPlannedRewardChoice parent) =>
            parent with { Children = RemoveChild(parent.Children, 1) };
        bool First(PredictorPlannedRewardChoice? candidate) => SameRewardChoice(candidate, path[0].Choice);

        if (owner == PredictorRewardEditOwner.Reward)
        {
            int index = current.RewardChoices.FindIndex(First);
            if (index < 0) throw new InvalidOperationException("MapRewardAlternativeCancelRootMissing");
            return current with { RewardChoices = path.Length == 1 ? CancelLeaf(current.RewardChoices[index]) is { } retained
                    ? current.RewardChoices.SetItem(index, retained) : current.RewardChoices.RemoveAt(index) :
                current.RewardChoices.SetItem(index, RewriteParent(current.RewardChoices[index])) };
        }
        if (owner == PredictorRewardEditOwner.Ancient && current.Ancient is { } ancient)
            return current with { Ancient = ancient with { Children = RemoveChild(ancient.Children, 0) } };
        if (owner == PredictorRewardEditOwner.Shop && current.Shop is { } shop)
        {
            int index = shop.Actions.FindIndex(action => action.Obtain is { } obtain && First(obtain.Reward) &&
                (path[0].Source == null || obtain.ExpectedSource == path[0].Source));
            if (index < 0) throw new InvalidOperationException("MapRewardAlternativeCancelShopChildMissing");
            var action = shop.Actions[index];
            return current with { Shop = shop with { Actions = path.Length == 1 ? CancelLeaf(action.Obtain!.Reward!) is { } retained
                    ? shop.Actions.SetItem(index, action with { Obtain = action.Obtain! with { Reward = retained } }) : shop.Actions.RemoveAt(index) :
                shop.Actions.SetItem(index, action with { Obtain = action.Obtain! with { Reward = RewriteParent(action.Obtain.Reward!) } }) } };
        }
        if (owner == PredictorRewardEditOwner.Event && current.Event is { } ev)
        {
            int index = ev.Actions.FindIndex(action => First(action.RewardChoice ?? action.Obtain?.Reward) &&
                (path[0].Source == null || action.Obtain?.ExpectedSource == path[0].Source));
            if (index < 0) throw new InvalidOperationException("MapRewardAlternativeCancelEventChildMissing");
            var action = ev.Actions[index];
            if (path.Length == 1)
            {
                var retained = CancelLeaf((action.RewardChoice ?? action.Obtain?.Reward)!);
                return current with { Event = ev with { Actions = retained == null ? ev.Actions.RemoveAt(index) :
                    ev.Actions.SetItem(index, action.RewardChoice != null ? action with { RewardChoice = retained } :
                        action with { Obtain = action.Obtain! with { Reward = retained } }) } };
            }
            var rewritten = action.RewardChoice != null ? action with { RewardChoice = RewriteParent(action.RewardChoice) } :
                action with { Obtain = action.Obtain! with { Reward = RewriteParent(action.Obtain.Reward!) } };
            return current with { Event = ev with { Actions = ev.Actions.SetItem(index, rewritten) } };
        }
        if (owner == PredictorRewardEditOwner.Treasure && current.Treasure is { } treasure)
        {
            int index = treasure.Actions.FindIndex(action => action.Obtain is { } obtain && First(obtain.Reward) &&
                (path[0].Source == null || obtain.ExpectedSource == path[0].Source));
            if (index < 0) throw new InvalidOperationException("MapRewardAlternativeCancelTreasureChildMissing");
            var action = treasure.Actions[index];
            return current with { Treasure = treasure with { Actions = path.Length == 1 ? CancelLeaf(action.Obtain!.Reward!) is { } retained
                    ? treasure.Actions.SetItem(index, action with { Obtain = action.Obtain! with { Reward = retained } }) : treasure.Actions.RemoveAt(index) :
                treasure.Actions.SetItem(index, action with { Obtain = action.Obtain! with { Reward = RewriteParent(action.Obtain.Reward!) } }) } };
        }
        throw new InvalidOperationException("MapRewardAlternativeCancelOwnerMissing");
    }
}
