using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private bool _routeShowContinuation;
    private int _routeContinuationLimit = 8;

    private PredictorPlannedNode RoutePlan(string nodeId) =>
        _routeRuntime!.PlannedNodes.FirstOrDefault(p => p.NodeId == nodeId) ?? new(nodeId);

    // Editing future intent never settles a reward or advances the actual run.
    private void EditRoutePlan(PredictorPlannedNode plan, bool remove = false)
    {
        try
        {
            if (remove) _routeRuntime!.RemovePlannedNode(plan.NodeId);
            else _routeRuntime!.SetPlannedNode(plan);
            SaveRouteBoundary();
            _routeMessage = "";
        }
        catch (Exception ex)
        {
            _routeMessage = RText("action_failed");
            RolltheSpire2.Bootstrap.RuntimeLog.Error("PredictorRoutePlan: " + ex);
        }
        RenderWorkbench();
    }

    private Button PlanButton(Node parent, string text, Action action, bool enabled = true)
    {
        var plans = _routeRuntime!.PlannedNodes;
        var request = _routeRuntime.Request;
        var button = RButton(parent, text, () =>
        {
            if (_routeRuntime!.PlannedNodes == plans && _routeRuntime.Request == request) action();
        }, enabled);
        button.TooltipText = "";
        return button;
    }

    private static PredictorPlannedCard PlanCard(PredictorCard card) => new(card.Id, card.Key, card.FloorAdded);
    private static bool SameRewardGroup(PredictorReward a, PredictorReward b) =>
        a.Kind == b.Kind && a.Origin == b.Origin && a.Key == b.Key;

    private static PredictorPlannedNode WithRewardChoice(PredictorPlannedNode plan, PredictorReward reward,
        int occurrence, int? cardIndex = null, bool skip = false, bool moveToEnd = false)
    {
        var choice = new PredictorPlannedRewardChoice(reward, occurrence, cardIndex, skip);
        int index = plan.RewardChoices.FindIndex(c => SameRewardGroup(c.ExpectedReward, reward) && c.Occurrence == occurrence);
        return plan with { RewardChoices = index < 0 ? plan.RewardChoices.Add(choice) :
            moveToEnd ? plan.RewardChoices.RemoveAt(index).Add(choice) : plan.RewardChoices.SetItem(index, choice) };
    }

    private void RenderRouteContinuation()
    {
        var heading = RFlow(_wbCenter);
        var toggle = RButton(heading, RText(_routeShowContinuation ? "plan.hide" : "plan.show"), () =>
        { _routeShowContinuation = !_routeShowContinuation; RenderWorkbench(); });
        toggle.TooltipText = "";
        if (!_routeShowContinuation) return;
        var area = WColumn(); area.Name = "RouteContinuation"; _wbCenter.AddChild(area);
        area.AddChild(WLabel(RText("plan.premise")));
        var run = _routeRuntime!;
        if (run.Routes.IsEmpty) { area.AddChild(WLabel(RText("route_needed"))); return; }
        PredictorRouteContinuation forecast;
        try { forecast = run.PreviewRouteContinuation(run.PlannedNodes, _routeContinuationLimit); }
        catch (Exception ex)
        {
            RolltheSpire2.Bootstrap.RuntimeLog.Error("PredictorRouteContinuation: " + ex);
            area.AddChild(WLabel(RText("plan.failed")));
            return;
        }
        foreach (var node in forecast.Nodes)
        {
            var block = WColumn(); area.AddChild(block);
            block.AddChild(WLabel(string.Format(RText("position"), node.Position.Act, node.Position.Floor) + " · " +
                (node.Model is { } model ? RName(model) : RText("plan.room." + node.Room)), true));
            var plan = RoutePlan(node.Position.NodeId);
            RenderPlannedRewards(block, node, plan);
            if (node.Room == PredictorRoomKind.RestSite) RenderPlannedRest(block, node, plan);
            foreach (var cause in node.PendingCauses.DistinctBy(c => (c.NodeId, c.Code, c.EnemySource)))
                block.AddChild(WLabel(RoutePendingText(cause)));
            if (run.PlannedNodes.Any(p => p.NodeId == node.Position.NodeId))
                PlanButton(block, RText("plan.clear"), () =>
                    EditRoutePlan(new(node.Position.NodeId), remove: true));
        }
        foreach (var cause in forecast.PendingCauses.Where(c =>
            !forecast.Nodes.Any(n => n.PendingCauses.Contains(c))))
            area.AddChild(WLabel(RoutePendingText(cause)));
        if (forecast.ReachedLimit && _routeContinuationLimit < 64)
            PlanButton(area, RText("plan.more"), () =>
            { _routeContinuationLimit = Math.Min(64, _routeContinuationLimit + 8); RenderWorkbench(); });
    }

    private string RoutePendingText(PredictorCombatPendingCause cause)
    {
        string prefix = "";
        if (cause.NodeId?.Split(':') is { Length: >= 5 } parts && int.TryParse(parts[^1], out int floor))
            prefix = string.Format(RText("plan.from_floor"), floor) + " · ";
        bool combatCause = cause.Code is "EnemyCreationCount" or "StolenCardIdentity" or "RecoveredGoldAmount" or
            "StolenGoldLoss" or "VictoryHealth" or "FirstPlayerTurn" or "EventTimeout" or "SealOfGoldDeduction" or "ExtraRewardOrder";
        string text = RText((combatCause ? "combat_pending." : "plan.pending.") + cause.Code);
        return prefix + text + (cause.EnemySource is { } source ? " · " + RText("enemy." + source) : "");
    }

    private void RenderPlannedRewards(Node area, PredictorRouteNodeKnowledge node, PredictorPlannedNode plan)
    {
        for (int index = 0; index < node.Rewards.Length; index++)
        {
            var item = node.Rewards[index];
            if (!item.IsKnown || item.Value is not { } reward)
            { area.AddChild(WLabel(RText("reward." + item.Kind) + " · " + RText("combat_pending"))); continue; }
            string value = reward.Kind switch
            {
                PredictorRewardKind.Gold => RGold(reward),
                PredictorRewardKind.Card or PredictorRewardKind.SpecialCard => string.Join(" / ", reward.Cards.Select(RCard)),
                _ => reward.Key.IsValid ? RName(reward.Key) : RText("reward." + reward.Kind)
            };
            area.AddChild(WLabel(value + (reward.Status == PredictorRewardStatus.Taken ? " · " + RText("taken") :
                reward.Status == PredictorRewardStatus.Skipped ? " · " + RText("skip") : "")));
            if (reward.Status != PredictorRewardStatus.Open) continue;
            int occurrence = item.Occurrence >= 0 ? item.Occurrence :
                node.Rewards.Take(index).Count(r => r.Value is { } prior && SameRewardGroup(prior, reward));
            var row = RFlow(area);
            if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.SpecialCard)
            {
                for (int c = 0; c < reward.Cards.Length; c++)
                {
                    int selected = c;
                    PlanButton(row, RText("plan.take") + " · " + RCard(reward.Cards[c]), () =>
                        EditRoutePlan(WithRewardChoice(plan, reward, occurrence, selected)));
                }
            }
            else if (reward.Kind is PredictorRewardKind.Gold or PredictorRewardKind.Potion or PredictorRewardKind.Relic)
                PlanButton(row, RText("plan.take"), () => EditRoutePlan(WithRewardChoice(plan, reward, occurrence)));
            if (reward.CanSkip && !reward.Mandatory)
                PlanButton(row, RText("plan.skip"), () => EditRoutePlan(WithRewardChoice(plan, reward, occurrence, skip: true)));
        }
    }

    private void RenderPlannedRest(Node area, PredictorRouteNodeKnowledge node, PredictorPlannedNode plan)
    {
        if (!node.RestOfferPendingCauses.IsEmpty) area.AddChild(WLabel(RText("plan.rest_options_pending")));
        for (int i = 0; i < plan.RestActions.Length; i++)
        {
            int stepIndex = i;
            var step = plan.RestActions[i];
            var row = RFlow(area);
            string target = step.SmithCard is { } smith ? " · " + RName(smith.Key) : "";
            row.AddChild(WLabel((i + 1) + ". " + RText("rest." + step.Action) + target));
            PlanButton(row, RText("plan.remove_action"), () => EditRoutePlan(plan with { RestActions = plan.RestActions.RemoveAt(stepIndex) }));
        }
        int pending = node.AppliedRestActions;
        if (pending < plan.RestActions.Length)
        {
            var step = plan.RestActions[pending];
            if (step.Action == PredictorRestAction.Smith)
            {
                var row = RFlow(area);
                foreach (var card in node.Cards.Where(c => c.IsKnown && c.Value.UpgradeLevel < c.Value.MaxUpgradeLevel))
                    PlanButton(row, RCard(card.Value), () => EditRoutePlan(plan with
                    { RestActions = plan.RestActions.SetItem(pending, step with { SmithCard = PlanCard(card.Value) }) }));
            }
            else if (step.Action == PredictorRestAction.Cook)
            {
                var candidates = node.Cards.Where(c => c.IsKnown && c.Value.Removable).Select(c => c.Value).ToArray();
                var selected = new List<PredictorCard>();
                var confirm = PlanButton(area, RText("confirm"), () => EditRoutePlan(plan with
                { RestActions = plan.RestActions.SetItem(pending, step with { CookCards = selected.Select(PlanCard).ToImmutableArray() }) }), false);
                var row = RFlow(area);
                foreach (var card in candidates)
                {
                    var choice = new CheckButton { Text = RCard(card) }; row.AddChild(choice);
                    choice.Toggled += enabled =>
                    { if (enabled) selected.Add(card); else selected.Remove(card); confirm.Disabled = selected.Count != 2; };
                }
            }
            if (node.PendingCauses.Any(c => c.NodeId == node.Position.NodeId && c.Code == "RestMaximumHp"))
            {
                var max = RNumber(area, "maximum_hp", step.MaximumHp ?? 0);
                PlanButton(area, RText("confirm"), () => EditRoutePlan(plan with
                { RestActions = plan.RestActions.SetItem(pending, step with { MaximumHp = (int)max.Value }) }));
            }
        }
        else if (node.Phase == PredictorPhase.RestSite)
        {
            var choices = RFlow(area);
            foreach (var offer in node.RestOffers)
                PlanButton(choices, RText("rest." + offer.Action), () => EditRoutePlan(plan with
                { RestActions = plan.RestActions.Add(new(offer.Action)), LeaveRest = false }), offer.Enabled);
        }
        if (node.RestHeal is { } heal)
            area.AddChild(WLabel(heal.OverrideHealAmount is { } amount
                ? string.Format(RText("plan.heal_fixed"), amount, heal.MaxHpDelta)
                : string.Format(RText("plan.heal"), heal.BaseMaxHpFraction * 100, heal.FlatHealBonus, heal.MaxHpDelta)));
        var leave = PlanButton(area, RText(plan.LeaveRest ? "plan.stay" : "plan.leave_rest"), () =>
            EditRoutePlan(plan with { LeaveRest = !plan.LeaveRest }));
        leave.Disabled = pending < plan.RestActions.Length;
    }

    private PredictorPlannedNode? RememberRouteInput(PredictorRun run, PredictorInput input)
    {
        if (run.Working.Visits.LastOrDefault()?.Room is not (PredictorRoomKind.Monster or PredictorRoomKind.Elite or
            PredictorRoomKind.Boss or PredictorRoomKind.RestSite)) return null;
        var plan = RoutePlan(run.Working.Position.NodeId);
        int restIndex = run.Project().CurrentNodeInputs.Count(i => i.Action is "RestSmith" or "RestHeal" or "RestLift" or
            "RestDig" or "RestCookRemoveTwoGainMaxHp5" or "RestClone" or "RestHatch" or "RestKindle");
        PredictorPlannedNode SetStep(PredictorPlannedRestAction step) => plan with
        { RestActions = restIndex < plan.RestActions.Length ? plan.RestActions.SetItem(restIndex, step) : plan.RestActions.Add(step) };
        var current = restIndex < plan.RestActions.Length ? plan.RestActions[restIndex] : null;
        switch (input)
        {
            case SelectRestAction select: return SetStep(new(select.Action)) with { LeaveRest = false };
            case CancelSmithSelection or CancelCookSelection:
                return restIndex < plan.RestActions.Length ? plan with { RestActions = plan.RestActions.RemoveAt(restIndex) } : null;
            case SelectSmithCard smith:
                return SetStep((current ?? new(PredictorRestAction.Smith)) with { SmithCard = PlanCard(run.Working.Card(smith.CardId)) });
            case SelectCookCards cook:
                return SetStep((current ?? new(PredictorRestAction.Cook)) with
                { CookCards = cook.CardIds.Select(id => PlanCard(run.Working.Card(id))).ToImmutableArray() });
            case ProvideRestMaxHpFact max:
                return SetStep((current ?? new(PredictorRestAction.Heal)) with { MaximumHp = max.MaxHp });
            case LeaveRestSite: return plan with { LeaveRest = true };
            case TakeReward take:
            {
                var reward = run.Rewards.FirstOrDefault(r => r.Id == take.RewardId);
                if (reward == null) return null;
                int occurrence = run.Rewards.TakeWhile(r => r.Id != reward.Id).Count(r => SameRewardGroup(r, reward));
                int? card = take.CardId is { } id ? reward.Cards.FindIndex(c => c.Id == id) :
                    reward.Kind == PredictorRewardKind.SpecialCard ? 0 : null;
                return WithRewardChoice(plan, reward, occurrence, card, moveToEnd: true);
            }
            case SkipCardReward:
            {
                var reward = run.Rewards.FirstOrDefault(r => r.Id == run.Selection.CardRewardId);
                if (reward == null) return null;
                int occurrence = run.Rewards.TakeWhile(r => r.Id != reward.Id).Count(r => SameRewardGroup(r, reward));
                return WithRewardChoice(plan, reward, occurrence, skip: true, moveToEnd: true);
            }
            case LeaveRewards:
                foreach (var reward in run.Rewards.Where(r => r.Status == PredictorRewardStatus.Open && r.CanSkip && !r.Mandatory))
                    plan = WithRewardChoice(plan, reward,
                        run.Rewards.TakeWhile(r => r.Id != reward.Id).Count(r => SameRewardGroup(r, reward)), skip: true);
                return plan;
            default: return null;
        }
    }
}
