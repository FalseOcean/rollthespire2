using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.InRunOperations;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private readonly Dictionary<string, OperationPreview?> _rewardPreviewCache = new(StringComparer.Ordinal);
    private long _rewardPreviewCacheRevision = -1;
    internal long RewardPreviewComputationCount { get; private set; }
    internal long RewardPreviewCacheHits { get; private set; }

    private InRunMapDetailChoice RewardChoice(Context context, string label,
        PredictorCombatRewardKnowledge knowledge, PredictorPlannedRewardChoice choice,
        Func<Context, PredictorPlannedRewardChoice, bool> write, string operation,
        InRunMapDetailObject? item = null, bool enabled = true, bool selected = false, bool cancelAlternative = false,
        bool cancelTake = false)
    {
        var display = Choice(context, label, c => cancelAlternative ? Set(c, CancelRewardAlternativePlan(c, knowledge, choice)) :
            cancelTake ? Set(c, CancelRewardTakePlan(c, knowledge, choice)) : write(c, choice), item, enabled, selected);
        // A completed route receipt also owns event/obtain rewards. Its
        // existing replay path binds an operation to that exact opportunity;
        // no room capture or fabricated event input is needed.
        if (enabled && (knowledge.CompletedEdit != null || context.Node.Event is { } ev && EventPlanApplies(context, ev) ||
            context.Node.CombatRequirements != null &&
            context.Node.Room is PredictorRoomKind.Monster or PredictorRoomKind.Elite or PredictorRoomKind.Boss))
            display = display with { PreviewFactory = () => CachedRewardPreview(context, knowledge, choice, operation, cancelAlternative, cancelTake) };
        return display;
    }

    private OperationPreview? CachedRewardPreview(Context context, PredictorCombatRewardKnowledge source,
        PredictorPlannedRewardChoice choice, string operation, bool cancelAlternative, bool cancelTake)
    {
        // Old factories may still be retained by a popup after route edits.
        // Validate before consulting the cache, including after failed previews.
        if (!Valid(context, context.Node.Position.NodeId)) return null;
        if (_rewardPreviewCacheRevision != context.Revision)
        {
            _rewardPreviewCache.Clear();
            _rewardPreviewCacheRevision = context.Revision;
        }
        string key = JsonSerializer.Serialize(new { Node = context.Node.Position.NodeId, operation, cancelAlternative, cancelTake,
            Choice = choice, source.SourceIdentity, source.CompletedEdit });
        if (_rewardPreviewCache.TryGetValue(key, out var cached))
        {
            RewardPreviewCacheHits++;
            return cached;
        }
        RewardPreviewComputationCount++;
        var preview = PreviewReward(context, source, choice, operation, cancelAlternative, cancelTake);
        _rewardPreviewCache[key] = preview;
        return preview;
    }

    private OperationPreview? PreviewReward(Context context, PredictorCombatRewardKnowledge source,
        PredictorPlannedRewardChoice choice, string operation, bool cancelAlternative, bool cancelTake)
    {
        if (!Valid(context, context.Node.Position.NodeId)) return null;
        // Skip belongs to the whole offer; it has no individual card source.
        // A cancelled alternative retains its observed relic identity even
        // though the replacement plan deliberately has no alternative ID.
        ModelKey model = default;
        if (operation is "TAKE" or "CANCEL_TAKE")
            model = RewardOffer(source)?.Cards.ElementAtOrDefault(choice.CardIndex ?? 0)?.Key ?? RewardOffer(source)?.Key ?? default;
        else if (operation is "SACRIFICE" or "REROLL")
        {
            long? relicId = choice.SacrificeRelicId ?? source.AppliedChoice?.SacrificeRelicId ??
                source.Alternatives.FirstOrDefault(alternative => alternative.Key == "SACRIFICE")?.RelicId;
            model = context.Node.CombatRelics.Concat(context.Node.Inventory?.Relics ?? [])
                .FirstOrDefault(relic => relic.IsKnown && !relic.Value.Melted && (operation == "REROLL" ?
                    relic.Value.Key.Entry == "DRIFTWOOD" : relic.Value.Id == relicId))?.Value.Key ?? default;
        }
        try
        {
            var plan = cancelAlternative ? CancelRewardAlternativePlan(context, source, choice) :
                cancelTake ? CancelRewardTakePlan(context, source, choice) : RewardPreviewPlan(context, source, choice);
            var node = _session.PreviewPlan(context.Location.ActIndex, context.Location.Point, plan, context.Revision);
            var result = node?.Rewards.FirstOrDefault(candidate => candidate.Occurrence == choice.Occurrence &&
                RewardOffer(candidate) is { } reward && SameReward(reward, choice.ExpectedReward) &&
                candidate.CompletedEdit?.Source == source.CompletedEdit?.Source);
            bool applied = cancelTake ? result is { DefaultSkipped: true } && RewardOffer(result)?.Status == PredictorRewardStatus.Skipped &&
                    (choice.Reroll ? result.AppliedChoice is { Reroll: true, CardIndex: null, Skip: false } : result.AppliedChoice == null) :
                cancelAlternative ? result is { DefaultSkipped: true, AppliedChoice: null, SacrificeCount: null } &&
                    RewardOffer(result) is { HasRerolled: false, Status: PredictorRewardStatus.Skipped } :
                result?.AppliedChoice is { } receipt && receipt.Reroll == choice.Reroll &&
                receipt.SacrificeRelicId == choice.SacrificeRelicId && receipt.CardIndex == choice.CardIndex && receipt.Skip == choice.Skip;
            var notices = ImmutableArray.CreateBuilder<string>();
            if (applied && cancelAlternative)
                notices.Add(M("preview_cancel_alternative"));
            if (applied && cancelTake) notices.Add(R("cancel") + " · " + R("default_skip"));
            if (applied && operation == "SKIP") notices.Add(R("plan.skip"));
            if (applied && result?.SacrificeCount is { } count)
                notices.Add(_text.Format("predictor.runtime.alternative.sacrifice_count", count));
            if (applied && result?.SacrificeRelic != null) notices.Add(M("preview_sacrifice_scope"));
            // Only observations used by this operation own its pending text.
            // Route-local inventory/HP annotations are not evidence that this
            // reward operation read them, and cannot create an empty Skip box.
            var relevantCauses = RewardPreviewCauses(applied ? result : source,
                includeOffer: !applied || operation is "TAKE" or "REROLL",
                selectedCard: applied && operation == "TAKE" ? choice.CardIndex : null);
            if (applied && choice.SacrificeRelicId != null && node != null)
                relevantCauses = relevantCauses.Concat(node.Rewards.Where(candidate => candidate.CompletedEdit?.RewardParents.Any(parent =>
                    parent.Choice.SacrificeRelicId == choice.SacrificeRelicId &&
                    SameRewardChoice(parent.Choice, choice)) == true).SelectMany(candidate => RewardPreviewCauses(candidate)));
            ImmutableArray<string> pending = PendingLines(relevantCauses).ToImmutableArray();
            if (!applied) pending = pending.Add(M("preview_unavailable"));
            // A plain skip with no transaction or unresolved output has no
            // new consequence to explain. Complex/unknown skips retain their
            // preview, including a previously adopted alternative.
            if (applied && operation == "SKIP" && pending.IsEmpty && result?.RequiredChoices == null &&
                source.RequiredChoices == null && source.AppliedChoice?.Reroll != true &&
                source.AppliedChoice?.SacrificeRelicId == null && source.SacrificeRelic == null)
                return null;
            notices.AddRange(pending);
            return InRunMapOptionPreview.BuildReward(model, operation, applied ? result : null,
                node?.Rewards ?? [], notices.ToImmutable(), pending, cancelledAlternative: applied && cancelAlternative);
        }
        catch (Exception error)
        {
            RuntimeLog.Fault("inRunMapOptionPreviewFailed=true;stage=detached-reward-plan;node=" +
                context.Node.Position.NodeId + ";operation=" + operation + ";occurrence=" + choice.Occurrence +
                ";revision=" + context.Revision, error);
            return new(model, [], [], false, [], [])
            { OptionKey = operation, Notices = [M("preview_unavailable")], NoticesAreLocalized = true,
                Faults = [new("MapRewardPlanPreview", [], error)] };
        }
    }

    private static PredictorPlannedNode RewardPreviewPlan(Context context, PredictorCombatRewardKnowledge source,
        PredictorPlannedRewardChoice choice)
    {
        if (source.CompletedEdit is { } completed)
            return PredictorRun.ApplyCompletedRewardChoice(context.Plan, completed, choice);
        if (context.Node.Event is { } ev && EventPlanApplies(context, ev) && context.Plan.Event != null)
            return EventRewardPlan(context, choice);
        return WithReward(context.Plan, choice);
    }

    // Shared by the real click and its pure projection. A direct event picker
    // is replayed before default close, preserving its accepted option prefix
    // and every other authored reward opportunity.
    private static PredictorPlannedNode EventRewardPlan(Context context, PredictorPlannedRewardChoice choice)
    {
        var plan = context.Plan.Event!;
        var ev = context.Node.Event!;
        {
            int index = plan.Actions.FindIndex(action => SameRewardChoice(action.RewardChoice ?? action.Obtain?.Reward, choice));
            if (index >= 0)
            {
                var action = plan.Actions[index];
                return context.Plan with { Event = plan with { Actions = plan.Actions.SetItem(index,
                    action.RewardChoice != null ? action with { RewardChoice = choice } :
                    action with { Obtain = action.Obtain! with { Reward = choice } }) } };
            }
            return context.Plan with { Event = plan with { Actions = plan.Actions.Insert(Math.Min(ev.AppliedActions, plan.Actions.Length),
                new PredictorPlannedEventAction(PredictorEventActionKind.Reward) { RewardChoice = choice }) } };
        }
    }

    private static IEnumerable<PredictorCombatPendingCause> RewardPreviewCauses(PredictorCombatRewardKnowledge? reward,
        bool includeOffer = true, int? selectedCard = null)
    {
        if (reward == null) yield break;
        if (includeOffer)
            foreach (var cause in selectedCard is { } index && !reward.CardOptions.IsEmpty && index >= 0 && index < reward.CardOptions.Length
                ? reward.CardOptions[index].EnchantmentPendingCauses : reward.PendingCauses) yield return cause;
        if (reward.RequiredChoices is not { } child) yield break;
        foreach (var cause in child.PendingCauses) yield return cause;
        foreach (var nested in child.Rewards)
            foreach (var cause in RewardPreviewCauses(nested)) yield return cause;
    }
}
