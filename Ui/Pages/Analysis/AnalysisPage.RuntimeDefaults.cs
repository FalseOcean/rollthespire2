using System.Collections.Immutable;
using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private bool RouteCombatEditorIsCurrent(PredictorRun run, PredictorRequest? request, long revision, string context) =>
        GodotObject.IsInstanceValid(this) && IsInsideTree() && IsVisibleInTree() && _routeSubview &&
        context == _routeAnalysisKey && ReferenceEquals(run, _routeRuntime) && run.Revision == revision && run.Request == request;

    private void RenderRouteCombatDefaultsButton(Node parent)
    {
        var run = _routeRuntime;
        var request = run?.Request;
        long revision = run?.Revision ?? 0;
        string context = _routeAnalysisKey;
        RButton(parent, RText("combat_defaults_settings"), () =>
        {
            if (run == null || !RouteCombatEditorIsCurrent(run, request, revision, context)) return;
            RoutePopup(RText("combat_defaults_settings"), (body, popup) =>
            {
                body.AddChild(WLabel(RText("combat_defaults_future")));
                var defaults = run.CombatDefaults;
                var keys = run.Working.Deck.Select(run.Working.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                    .Select(c => c.Key).Concat(defaults.CardRules.Select(r => r.Card)).Distinct()
                    .OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray();
                var counts = new Dictionary<ModelKey, SpinBox>();
                foreach (var key in keys)
                {
                    body.AddChild(WLabel(RName(key)));
                    counts[key] = RNumber(body, "triggers", defaults.CardTriggers(key), Math.Max(10000, defaults.CardTriggers(key)));
                }
                if (keys.Length == 0) body.AddChild(WLabel(RText("combat_defaults_no_cards")));
                var lamp = new ModelKey("RELIC", "LAVA_LAMP");
                var lampEnabled = RCheck(body, "lava_active", defaults.RelicEffectEnabled(lamp));
                var enemySources = run.CombatRequirements.EnemyCreations.Concat(defaults.EnemyRules.Select(r => r.Source))
                    .Distinct().OrderBy(s => s).ToArray();
                var enemyCounts = new Dictionary<PredictorEnemyCreationSource, (SpinBox Count, CheckButton Custom)>();
                if (enemySources.Length > 0) body.AddChild(WLabel(RText("combat_enemy_defaults_hint")));
                foreach (var source in enemySources)
                {
                    int? initial = defaults.EnemyCount(source);
                    int? builtIn = new PredictorCombatDefaults().EnemyCount(source);
                    var number = RNumber(body, "enemy." + source, initial ?? 0, Math.Max(100000, initial ?? 0));
                    var custom = RCheck(body, "combat_enemy_custom_default", defaults.EnemyRules.Any(r => r.Source == source));
                    bool restoring = false;
                    number.ValueChanged += _ => { if (!restoring) custom.ButtonPressed = true; };
                    var pending = WLabel(RText("combat_enemy_default_pending"));
                    pending.Visible = initial == null;
                    body.AddChild(pending);
                    custom.Toggled += enabled =>
                    {
                        if (!enabled)
                        {
                            restoring = true; number.Value = builtIn ?? 0; restoring = false;
                        }
                        pending.Visible = !enabled && builtIn == null;
                    };
                    enemyCounts[source] = (number, custom);
                }
                RButton(body, RText("save_combat_defaults"), () =>
                {
                    if (!RouteCombatEditorIsCurrent(run, request, revision, context) || !ReferenceEquals(run.CombatDefaults, defaults)) return;
                    var updated = defaults with
                    {
                        CardRules = counts.Select(p => new PredictorCombatCardRule(p.Key, (int)p.Value.Value)).ToImmutableArray(),
                        RelicRules = [new(lamp, lampEnabled.ButtonPressed)],
                        EnemyRules = enemyCounts.Where(p => p.Value.Custom.ButtonPressed)
                            .Select(p => new PredictorEnemyCreationFact(p.Key, (int)p.Value.Count.Value)).ToImmutableArray()
                    };
                    updated.Validate();
                    popup.QueueFree();
                    RouteAction(() => run.CombatDefaults = updated);
                }).TooltipText = "";
            });
        }, run != null).TooltipText = "";
    }
}
