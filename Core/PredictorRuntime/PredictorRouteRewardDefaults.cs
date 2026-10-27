using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{

    private static void CaptureCompletedCardRewardEdits(PredictorRun run, List<RouteDependency> dependencies)
    {
        if (CompletedRewardEdit(run) is not { } edit) return;
        var rewards = ObserveRouteRewards(run, dependencies);
        for (int index = 0; index < run.Rewards.Length; index++)
        {
            var reward = run.Rewards[index];
            if (reward.Kind is not (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)) continue;
            run._preview.Rewards.DefaultCardSkipKnowledge[reward.Id] = (run.Working.Position.NodeId,
                rewards[index] with { CompletedEdit = edit });
        }
    }

    private static bool IsLiveDefaultCardSkip(PredictorRun run, PredictorReward reward) =>
        run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
        reward.Kind is (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) && reward.CanSkip && !reward.Mandatory &&
        !(run.HasObtainChild && run._obtainFrames[^1].Mandatory);

    private static bool HasAcceptedCardSkip(PredictorRun run, PredictorReward reward) =>
        run._inputs.LastOrDefault(input => input.ObjectId == reward.Id && input.Action is
            "EnterCardSelection" or "SkipCardReward" or "RerollCardReward" or "TakeReward")?.Action == "SkipCardReward";

    private static void ExitLiveDefaultRerollSelection(PredictorRun run, CancellationToken token)
    {
        if (run.Phase != PredictorPhase.Rewards || run.Selection.CardRewardId is not { } selectedId ||
            run.Rewards.FirstOrDefault(reward => reward.Id == selectedId) is not
                { HasRerolled: true, Status: PredictorRewardStatus.Open } selected ||
            !IsLiveDefaultCardSkip(run, selected) ||
            !run._preview.Rewards.Results.TryGetValue(selectedId, out var accepted) ||
            accepted.NodeId != run.Working.Position.NodeId || accepted.Source.Id != selectedId ||
            accepted.Choice is not { Reroll: true, CardIndex: null, Skip: false,
                SacrificeRelicId: null, Children.IsEmpty: true }) return;
        // The reroll is complete; its open selection screen must not prevent
        // independent default gold/potion/relic pickups. Navigation leaves the
        // replacement offer open. The existing final default Skip still owns
        // its disposition after pickup hooks have had their normal turn.
        if (run.Submit(run.Request!, new ExitCardSelection(), token) != PredictorInputResult.Accepted)
            throw new InvalidOperationException("PredictorDrawingRerollNavigationRejected");
    }

    private static void ApplyLiveDefaultCardSkips(PredictorRun run, List<RouteDependency> dependencies,
        HashSet<long> skipped, CancellationToken token)
    {
        if (run.Phase != PredictorPhase.Rewards) return;
        foreach (var reward in run.Rewards.Where(reward => reward.Status == PredictorRewardStatus.Open))
        {
            if (HasAcceptedCardSkip(run, reward)) { skipped.Add(reward.Id); continue; }
            if (!IsLiveDefaultCardSkip(run, reward) || skipped.Contains(reward.Id)) continue;
            // Reward population already happened through the source pipeline.
            // This policy chooses no card, reroll or sacrifice and owns no RNG.
            skipped.Add(reward.Id);
            if (!run._preview.Rewards.DefaultCardSkips.Add(reward.Id)) continue;
            int index = run.Rewards.IndexOf(reward);
            var knowledge = ObserveRouteRewards(run, dependencies)[index];
            run._preview.Rewards.DefaultCardSkipKnowledge[reward.Id] = (run.Working.Position.NodeId,
                knowledge with { CompletedEdit = CompletedRewardEdit(run) });
            int occurrence = run.Rewards.Take(index).Count(prior => prior.Kind == reward.Kind &&
                prior.Origin == reward.Origin && prior.Key == reward.Key);
            string choice = PredictorRouteCauseEvidence.RewardChoice(run.Working.Position.NodeId, reward, occurrence).Identity;
            run._preview.Partial?.Facts.RecordResult("reward.disposition." + choice, run.Working.Position.NodeId,
                PredictorRewardStatus.Skipped, origin: PredictorKnowledgeOrigin.ApprovedDefault,
                operation: "LiveMapDefaults: skip optional card reward",
                derivation: "Generated reward remains available for display; no card receipt, reroll or sacrifice is performed.");
        }
        if (run.Selection.CardRewardId is { } selected && skipped.Contains(selected))
            run.Submit(run.Request!, new SkipCardReward(), token);
    }

    private static ImmutableArray<PredictorCombatRewardKnowledge> CompletedLiveDefaultCardRewards(PredictorRun run,
        List<RouteDependency> dependencies)
    {
        // CompleteNode moves child dispositions into its record and clears
        // CompletedChildRewards. Room DTOs observed after leaving still need
        // those completed sources and their semantic edit paths.
        var completed = run.CompletedChildRewards;
        if (run._checkpoints[^1].CompletedNode is { } node && node.NodeId == run.Working.Position.NodeId)
            completed = completed.AddRange(node.Rewards);
        return completed.DistinctBy(reward => reward.Id).Where(reward => !run.Rewards.Any(current => current.Id == reward.Id) &&
                (run._preview.Rewards.DefaultCardSkipKnowledge.TryGetValue(reward.Id, out var card) &&
                    card.NodeId == run.Working.Position.NodeId ||
                run._preview.Rewards.PotionDisplayKnowledge.TryGetValue(reward.Id, out var potion) &&
                    potion.NodeId == run.Working.Position.NodeId))
            .Select(reward =>
            {
                if (run._preview.Rewards.PotionDisplayKnowledge.TryGetValue(reward.Id, out var potion))
                    return WithRouteRewardResult(run, reward, potion.Knowledge, dependencies);
                var knowledge = run._preview.Rewards.DefaultCardSkipKnowledge[reward.Id].Knowledge;
                return WithRouteRewardResult(run, reward, knowledge with
                    { Value = knowledge.IsKnown ? DisplayReward(reward) : null }, dependencies);
            })
            .ToImmutableArray();
    }

    private static PredictorCombatRewardKnowledge WithRouteRewardDisposition(PredictorRun run,
        PredictorReward reward, PredictorCombatRewardKnowledge knowledge)
    {
        bool defaultSkipped = run._preview.Rewards.DefaultCardSkips.Contains(reward.Id);
        return knowledge with
        {
            DefaultSkipped = defaultSkipped,
            CompletedEdit = knowledge.CompletedEdit ??
                (run._preview.Rewards.DefaultCardSkipKnowledge.TryGetValue(reward.Id, out var source) ? source.Knowledge.CompletedEdit : null),
            DispositionOrigin = defaultSkipped ? PredictorKnowledgeOrigin.ApprovedDefault :
                knowledge.AppliedChoice != null || HasAcceptedCardSkip(run, reward) ||
                    reward.Kind is (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) &&
                    run._inputs.Any(input => input.ObjectId == reward.Id && input.Action == "TakeReward")
                    ? PredictorKnowledgeOrigin.PlayerInput :
                reward.Kind is (PredictorRewardKind.Card or PredictorRewardKind.SpecialCard) && reward.Status != PredictorRewardStatus.Open
                    ? PredictorKnowledgeOrigin.Captured : null
        };
    }

    // Call after authored child actions. Only an optional reward leaf made
    // entirely of default card skips or optional potion display can close;
    // mandatory potion receipts and other child choices stay open.
    private static bool ContinueLiveDefaultCardRewardChildren(PredictorRun run,
        List<RouteDependency> dependencies, CancellationToken token)
    {
        bool completed = CompleteLiveObtainHealth(run, dependencies, token);
        completed |= CompleteLivePaelsTooth(run, dependencies, token);
        while (run.HasObtainChild && run._preview.Settings.Policy == PredictorRoutePreviewPolicy.LiveMapDefaults &&
            !run._obtainFrames[^1].Mandatory)
        {
            var skipped = new HashSet<long>();
            if (!ApplyRouteRewardChoices(run, new(run.Working.Position.NodeId), dependencies, [], skipped, [], token)) break;
            if (!run.HasObtainChild) { completed = true; break; }
            if (run.Rewards.Any(reward => reward.Status == PredictorRewardStatus.Open && !skipped.Contains(reward.Id) &&
                !IsLiveDefaultPotionOmission(run, reward, dependencies))) break;
            foreach (var reward in run.Rewards.Where(reward => reward.Status == PredictorRewardStatus.Open))
                RegisterLiveDefaultPotionOmission(run, reward, dependencies);
            if (run.Submit(run.Request!, new LeaveRewards(), token) != PredictorInputResult.Accepted) break;
            CompleteLiveObtainHealth(run, dependencies, token);
            CompleteLivePaelsTooth(run, dependencies, token);
            completed = true;
        }
        return completed;
    }
}
