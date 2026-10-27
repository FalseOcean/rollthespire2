using Godot;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private void RenderRouteCombatPreview(Node area)
    {
        PredictorCombatPreview? preview;
        try { preview = _routeRuntime!.PreviewCurrentCombat(); }
        catch (Exception ex)
        {
            RolltheSpire2.Bootstrap.RuntimeLog.Error("PredictorCombatPreview: " + ex);
            area.AddChild(WLabel(RText("combat_preview_failed") + " · " + ex.Message));
            return;
        }
        if (preview == null) return;
        var block = WColumn(); block.Name = "RouteCombatPreview"; area.AddChild(block);
        block.AddChild(WLabel(RText("combat_preview"), true));
        block.AddChild(WLabel(RText("combat_preview_premise")));
        foreach (var item in preview.Rewards)
        {
            if (item.Value is not { } reward)
            {
                block.AddChild(WLabel(RText("reward." + item.Kind) + " · " + RText("combat_pending")));
                continue;
            }
            string value = reward.Kind switch
            {
                PredictorRewardKind.Gold => RGold(reward),
                PredictorRewardKind.Card or PredictorRewardKind.SpecialCard =>
                    RText("reward." + reward.Kind) + " · " + string.Join(" / ", reward.Cards.Select(RCard)),
                _ => reward.Key.IsValid ? RName(reward.Key) : RText("reward." + reward.Kind)
            };
            block.AddChild(WLabel(value));
        }
        foreach (var cause in preview.PendingCauses)
            block.AddChild(WLabel(RText("combat_pending." + cause.Code) +
                (cause.EnemySource is { } source ? " · " + RText("enemy." + source) : "")));
    }

    private void RenderRouteRewardPolicy(Node area, PredictorRuntimeView view)
    {
        var run = _routeRuntime!;
        if (!run.Rewards.Any(r => r.Origin == PredictorRewardOrigin.Combat)) return;
        var request = run.Request;
        var controls = RFlow(area);
        var automatic = new CheckButton { Text = RText("pickup_defaults"), ButtonPressed = run.AutomaticRewardPickup };
        controls.AddChild(automatic);
        automatic.Toggled += enabled =>
        {
            if (IsInsideTree() && ReferenceEquals(run, _routeRuntime) && run.Request == request)
                Callable.From(() =>
                {
                    if (IsInsideTree() && ReferenceEquals(run, _routeRuntime) && run.Request == request)
                        RouteAction(() => run.AutomaticRewardPickup = enabled);
                }).CallDeferred();
        };
        if (view.CanAdjustCombat)
        {
            var reorder = RButton(controls, RText("pickup_reorder"), () =>
                RouteAction(() => { run.ReorderCurrentCombatRewards(); _routeEditingCombat = false; }));
            reorder.TooltipText = "";
        }
        area.AddChild(WLabel(RText("pickup_order")));
        foreach (var pending in PredictorRewardPickupPolicy.Plan(run).Pending)
        {
            if (pending.Reason is PredictorRewardPickupPendingReason.ActivePhase or PredictorRewardPickupPendingReason.ActiveSelection)
                continue;
            var reward = run.Rewards.FirstOrDefault(r => r.Id == pending.RewardId);
            if (reward == null) continue;
            string name = reward.Key.IsValid ? RName(reward.Key) : RText("reward." + reward.Kind);
            area.AddChild(WLabel(name + " · " + RText("pickup_pending." + pending.Reason)));
        }
    }
}
