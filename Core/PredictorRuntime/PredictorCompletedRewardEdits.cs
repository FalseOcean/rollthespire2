using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorRewardEditOwner { Reward, Ancient, Shop, Event, Treasure }
internal sealed record PredictorRewardEditParent(ModelKey? Source, PredictorPlannedRewardChoice Choice);

// Semantic route intent at the owning obtain boundary. Contains neither a
// state/checkpoint or generated card IDs; ordinary source/offer checks bind edits.
internal sealed record PredictorCompletedRewardEdit(ModelKey Source, PredictorRewardEditOwner Owner,
    PredictorPlannedNode Prefix, int InsertionIndex,
    ImmutableArray<PredictorRewardEditParent> RewardParents);

internal sealed partial class PredictorRun
{

    private static void ConfigureCompletedRewardEdits(PredictorRun run, PredictorPlannedNode prefix,
        PredictorRewardEditOwner owner, int insertionIndex) =>
        run._preview.Rewards.CompletedEdit = new(default, owner, prefix, insertionIndex, []);

    private static PredictorCompletedRewardEdit? CompletedRewardEdit(PredictorRun run)
    {
        if (run._preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || !run.HasObtainChild ||
            run._obtainFrames[^1].Destination is PredictorObtainReturn.Rest or PredictorObtainReturn.RestHeal ||
            CurrentRouteObtainSource(run) is not { } source || run._preview.Rewards.CompletedEdit is not { } context) return null;
        var parents = ImmutableArray.CreateBuilder<PredictorRewardEditParent>();
        for (int i = 0; i < run._obtainFrames.Length; i++)
        {
            var frame = run._obtainFrames[i];
            if (frame.Destination is not (PredictorObtainReturn.RewardAlternative or PredictorObtainReturn.Reward)) continue;
            var reward = frame.ParentRewards.FirstOrDefault(candidate => candidate.Id == frame.SourceId);
            if (reward == null) return null;
            int occurrence = frame.ParentRewards.TakeWhile(candidate => candidate.Id != reward.Id)
                .Count(prior => prior.Kind == reward.Kind && prior.Origin == reward.Origin && prior.Key == reward.Key);
            var choice = run._preview.Rewards.Results.TryGetValue(reward.Id, out var result) ? result.Choice :
                new PredictorPlannedRewardChoice(DisplayReward(reward), occurrence);
            if (result == null && frame.Destination == PredictorObtainReturn.RewardAlternative)
            {
                var accepted = run._inputs.LastOrDefault(input => input.Action == "PaelsWingSacrifice" && input.Targets.Contains(reward.Id));
                if (accepted == null) return null;
                choice = choice with { SacrificeRelicId = accepted.ObjectId };
            }
            bool rootRestReward = context.Owner == PredictorRewardEditOwner.Reward && parents.Count == 0 && i > 0 &&
                run._obtainFrames[i - 1].Destination is PredictorObtainReturn.Rest or PredictorObtainReturn.RestHeal;
            parents.Add(new(i == 0 || rootRestReward ? null : run._obtainFrames[i - 1].Relic, choice));
        }
        if (context.Owner == PredictorRewardEditOwner.Reward && parents.Count == 0) return null;
        return context with { Source = source, RewardParents = parents.ToImmutable() };
    }

    internal static PredictorPlannedNode ApplyCompletedRewardChoice(PredictorPlannedNode current,
        PredictorCompletedRewardEdit edit, PredictorPlannedRewardChoice choice)
    {
        if (current.NodeId != edit.Prefix.NodeId || !edit.Source.IsValid || edit.InsertionIndex < 0 ||
            edit.RewardParents.IsDefault) throw new ArgumentException("PredictorCompletedRewardEditInvalid");
        // The receipt supplies an automatic owner path only when none has been
        // authored yet. Existing intent, including later sibling edits, belongs
        // to current rather than to the earlier observation's prefix.
        var prefix = current with
        {
            Ancient = current.Ancient ?? edit.Prefix.Ancient,
            Shop = current.Shop ?? edit.Prefix.Shop,
            Event = current.Event ?? edit.Prefix.Event,
            Treasure = current.Treasure ?? edit.Prefix.Treasure
        };
        var parents = CurrentCompletedRewardParents(prefix, edit);
        var child = new PredictorPlannedObtainChild(PredictorObtainChildAction.Reward, edit.Source) { Reward = choice };
        PredictorPlannedRewardChoice? root = null;
        for (int i = parents.Length - 1; i >= 0; i--)
        {
            var parent = parents[i];
            var updated = parent.Choice with { Children = UpsertCompletedRewardChild(parent.Choice.Children, child, parent.Choice.Children.Length) };
            if (parent.Source is { } source) child = new(PredictorObtainChildAction.Reward, source) { Reward = updated };
            else root = updated;
        }
        if (root != null)
        {
            if (edit.Owner == PredictorRewardEditOwner.Event && prefix.Event is { } eventPlan)
            {
                int index = eventPlan.Actions.FindIndex(action => SameCompletedReward(action.RewardChoice, root));
                if (index < 0) throw new ArgumentException("PredictorCompletedRewardEventParentMissing");
                return current with { Event = eventPlan with { Actions = eventPlan.Actions.SetItem(index,
                    eventPlan.Actions[index] with { RewardChoice = root }) } };
            }
            int rewardIndex = prefix.RewardChoices.FindIndex(prior => SameCompletedReward(prior, root));
            return current with { RewardChoices = rewardIndex < 0 ? prefix.RewardChoices.Add(root) :
                prefix.RewardChoices.SetItem(rewardIndex, root) };
        }
        switch (edit.Owner)
        {
            case PredictorRewardEditOwner.Ancient when prefix.Ancient is { } ancient:
                return current with { Ancient = ancient with
                    { Children = UpsertCompletedRewardChild(ancient.Children, child, edit.InsertionIndex) } };
            case PredictorRewardEditOwner.Shop when prefix.Shop is { } shop:
            {
                var actions = shop.Actions;
                int index = actions.FindIndex(action => SameCompletedRewardChild(action.Obtain, child));
                var action = new PredictorPlannedShopAction(PredictorShopActionKind.Obtain) { Obtain = child };
                return current with { Shop = shop with { Actions = index < 0 ? actions.Insert(
                    CompletedRewardInsertionIndex(actions.Select(item => item.Obtain).ToImmutableArray(), child, edit.InsertionIndex), action) : actions.SetItem(index, action) } };
            }
            case PredictorRewardEditOwner.Event when prefix.Event is { } eventPlan:
            {
                var actions = eventPlan.Actions;
                int index = actions.FindIndex(action => SameCompletedRewardChild(action.Obtain, child));
                var action = new PredictorPlannedEventAction(PredictorEventActionKind.Obtain) { Obtain = child };
                return current with { Event = eventPlan with { Actions = index < 0 ? actions.Insert(
                    CompletedRewardInsertionIndex(actions.Select(item => item.Obtain).ToImmutableArray(), child, edit.InsertionIndex), action) : actions.SetItem(index, action) } };
            }
            case PredictorRewardEditOwner.Treasure when prefix.Treasure is { } treasure:
            {
                var actions = treasure.Actions;
                int index = actions.FindIndex(action => SameCompletedRewardChild(action.Obtain, child));
                var action = new PredictorPlannedTreasureAction(PredictorTreasureActionKind.Obtain) { Obtain = child };
                return current with { Treasure = treasure with { Actions = index < 0 ? actions.Insert(
                    CompletedRewardInsertionIndex(actions.Select(item => item.Obtain).ToImmutableArray(), child, edit.InsertionIndex), action) : actions.SetItem(index, action) } };
            }
            default: throw new ArgumentException("PredictorCompletedRewardOwnerMissing");
        }
    }

    private static ImmutableArray<PredictorRewardEditParent> CurrentCompletedRewardParents(
        PredictorPlannedNode current, PredictorCompletedRewardEdit edit)
    {
        var children = edit.Owner switch
        {
            PredictorRewardEditOwner.Ancient => current.Ancient?.Children ?? [],
            PredictorRewardEditOwner.Shop => current.Shop?.Actions.Where(action => action.Obtain != null)
                .Select(action => action.Obtain!).ToImmutableArray() ?? [],
            PredictorRewardEditOwner.Event => current.Event?.Actions.Where(action => action.Obtain != null)
                .Select(action => action.Obtain!).ToImmutableArray() ?? [],
            PredictorRewardEditOwner.Treasure => current.Treasure?.Actions.Where(action => action.Obtain != null)
                .Select(action => action.Obtain!).ToImmutableArray() ?? [],
            _ => ImmutableArray<PredictorPlannedObtainChild>.Empty
        };
        var parents = ImmutableArray.CreateBuilder<PredictorRewardEditParent>();
        foreach (var recorded in edit.RewardParents)
        {
            var actual = recorded.Source == null
                ? (edit.Owner == PredictorRewardEditOwner.Event
                    ? current.Event?.Actions.Select(action => action.RewardChoice)
                        .FirstOrDefault(choice => SameCompletedReward(choice, recorded.Choice))
                    : current.RewardChoices.FirstOrDefault(choice => SameCompletedReward(choice, recorded.Choice)))
                : children.FirstOrDefault(child => child.ExpectedSource == recorded.Source &&
                    SameCompletedReward(child.Reward, recorded.Choice))?.Reward;
            var parent = recorded with { Choice = actual ?? recorded.Choice };
            parents.Add(parent);
            children = parent.Choice.Children;
        }
        return parents.ToImmutable();
    }

    private static bool SameCompletedReward(PredictorPlannedRewardChoice? first, PredictorPlannedRewardChoice second) =>
        first != null && first.Occurrence == second.Occurrence && first.ExpectedReward.Kind == second.ExpectedReward.Kind &&
        first.ExpectedReward.Origin == second.ExpectedReward.Origin && first.ExpectedReward.Key == second.ExpectedReward.Key;

    private static bool SameCompletedRewardChild(PredictorPlannedObtainChild? first, PredictorPlannedObtainChild second) =>
        first?.Action == PredictorObtainChildAction.Reward && first.ExpectedSource == second.ExpectedSource &&
        second.Reward != null && SameCompletedReward(first.Reward, second.Reward);

    private static ImmutableArray<PredictorPlannedObtainChild> UpsertCompletedRewardChild(
        ImmutableArray<PredictorPlannedObtainChild> children, PredictorPlannedObtainChild child, int insertionIndex)
    {
        int index = children.FindIndex(prior => SameCompletedRewardChild(prior, child));
        return index < 0 ? children.Insert(CompletedRewardInsertionIndex(children.Select(item =>
            (PredictorPlannedObtainChild?)item).ToImmutableArray(), child, insertionIndex), child) : children.SetItem(index, child);
    }

    private static int CompletedRewardInsertionIndex(ImmutableArray<PredictorPlannedObtainChild?> children,
        PredictorPlannedObtainChild child, int insertionIndex)
    {
        int index = Math.Min(insertionIndex, children.Length);
        // An older receipt points before sibling edits authored since its
        // observation. Append after those actual reward operations so their
        // reroll RNG order and already accepted replacement offers stay valid.
        while (index < children.Length && children[index] is { Action: PredictorObtainChildAction.Reward } sibling &&
            sibling.ExpectedSource == child.ExpectedSource) index++;
        return index;
    }

    private static bool ApplyPlannedRewardChildren(PredictorRun run, PredictorReward source,
        PredictorPlannedRewardChoice choice, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        foreach (var child in choice.Children)
        {
            if (!ApplyRouteObtainChild(run, child, dependencies, knownInstanceIds, local, token)) return false;
            if (run._preview.Rewards.Results.TryGetValue(source.Id, out var result))
                run._preview.Rewards.Results[source.Id] = result with { AppliedChildren = result.AppliedChildren + 1 };
        }
        return true;
    }
}
