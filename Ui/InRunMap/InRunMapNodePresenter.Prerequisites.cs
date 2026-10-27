using System.Collections.Immutable;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed record InRunMapPrerequisiteView(string Identity, string Text,
    ImmutableArray<string> AffectedResults, string? Source = null, string? Summary = null);

// Only actual observation consumers assign impact. A cause's domain bitmask
// describes possible dependencies, not proof that every reward is affected.
internal static class InRunMapPrerequisiteProjection
{
    private const string Prefix = "predictor.map.prerequisite.";
    private static string L(IUiTextProvider text, string key) => text.Get(Prefix + key);

    internal static string Action(PredictorCombatPendingCause cause, IUiTextProvider text)
    {
        string action = EnemyCreationSource(cause) is { } source
            ? text.Format(Prefix + "enemy_creation_count", text.Get("predictor.runtime.enemy." + source),
                text.Get("predictor.map.reward_influences"))
            : text.TryGet(Prefix + "cause." + cause.Code, out var specific) ? specific :
            text.TryGet("predictor.runtime.plan.pending." + cause.Code, out var existing) ? existing :
            L(text, "kind." + cause.ReasonKind);
        // Separate reward opportunities remain distinguishable without exposing
        // their internal keys or implying that the player has selected one.
        if (cause.Choice is { Category: "Reward", RewardKind: { } kind } choice)
            action = text.Format(Prefix + "reward_choice", text.Get("predictor.runtime.reward." + kind),
                choice.Occurrence + 1, action);
        return action;
    }

    private static PredictorEnemyCreationSource? EnemyCreationSource(PredictorCombatPendingCause cause)
    {
        if (cause.Code != "EnemyCreationCount") return null;
        if (cause.EnemySource is { } source && Enum.IsDefined(source)) return source;
        const string prefix = "combat.enemyCreation.", suffix = ".count";
        foreach (string key in cause.RequiredPropertyKeys)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(suffix, StringComparison.Ordinal) &&
                Enum.TryParse<PredictorEnemyCreationSource>(key[prefix.Length..^suffix.Length], out var parsed) &&
                Enum.IsDefined(parsed)) return parsed;
        return null;
    }

    // These observations still belong to the forecast and full diagnostics.
    // Drawing exposes actual results and input controls without repeatedly
    // announcing its deliberately unprojected health/potion bookkeeping.
    private static IEnumerable<PredictorCombatPendingCause> VisibleCauses(IEnumerable<PredictorCombatPendingCause> causes) =>
        PredictorRouteCauseEvidence.OriginalCauses(causes).Where(cause => cause.Code is not (
            "EventHealthOutcomePending" or "ObtainHealthOutcomePending" or "MapPotionInventoryNotProjected" or
            "VictoryHealth" or "RestMaximumHp" or "EventHealthChoice" or "PlannedEventHealthPending" or
            "PlannedEventHealthDependent" or "PlannedObtainHealthPending" or "PlannedObtainHealthDependent" or
            "CombatPotionDropConsumptionPending" or "EventTabletDecipherOutcomePending"));

    internal static ImmutableArray<string> Pending(IEnumerable<PredictorCombatPendingCause> causes,
        IUiTextProvider text) => VisibleCauses(causes)
        .Select(cause => Action(cause, text)).ToImmutableArray();

    internal static ImmutableArray<InRunMapPrerequisiteView> Project(PredictorRouteNodeKnowledge node,
        Func<string?, string> sourceLabel, IUiTextProvider text,
        PredictorEventOptionResultKnowledge? selectedBranch = null)
    {
        var causes = new Dictionary<string, PredictorCombatPendingCause>(StringComparer.Ordinal);
        var results = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var missing = new List<InRunMapPrerequisiteView>();

        void Observe(IEnumerable<PredictorCombatPendingCause> pending, string result)
        {
            foreach (var cause in VisibleCauses(pending))
            {
                causes[cause.Identity] = cause;
                if (!results.TryGetValue(cause.Identity, out var affected)) results[cause.Identity] = affected = [];
                if (!affected.Contains(result, StringComparer.Ordinal)) affected.Add(result);
            }
        }
        void Rewards(ImmutableArray<PredictorCombatRewardKnowledge> rewards)
        {
            foreach (var reward in rewards)
            {
                string label = text.Get("predictor.runtime.reward." + reward.Kind);
                if (!reward.IsKnown)
                {
                    var roots = PredictorRouteCauseEvidence.OriginalCauses(reward.PendingCauses);
                    var enchantmentRoots = PredictorRouteCauseEvidence.OriginalCauses(reward.CardOptions
                            .SelectMany(option => option.EnchantmentPendingCauses))
                        .Select(cause => cause.Identity).ToHashSet(StringComparer.Ordinal);
                    string affected = !reward.CardOptions.IsEmpty && !roots.IsEmpty &&
                        roots.All(cause => enchantmentRoots.Contains(cause.Identity))
                        ? L(text, "card_enchantments") : label;
                    Observe(reward.PendingCauses, affected);
                    if (reward.PendingCauses.IsEmpty)
                        missing.Add(new("unresolved-result:" + missing.Count,
                            text.Format(Prefix + "line", sourceLabel(node.Position.NodeId),
                                L(text, "unexplained_result"), label), [label]));
                }
                // An unavailable base offer already explains why operations on
                // that offer are pending. Keep genuinely different operation
                // causes, without repeating the same source as a second scope.
                var offerCauses = reward.IsKnown ? new HashSet<string>(StringComparer.Ordinal) :
                    PredictorRouteCauseEvidence.OriginalCauses(reward.PendingCauses)
                        .Select(cause => cause.Identity).ToHashSet(StringComparer.Ordinal);
                Observe(PredictorRouteCauseEvidence.OriginalCauses(reward.Alternatives.SelectMany(alternative => alternative.PendingCauses))
                        .Where(cause => !offerCauses.Contains(cause.Identity)),
                    text.Format(Prefix + "reward_operations", label));
            }
        }

        Rewards(node.Rewards);
        if (node.Event is { } ev)
        {
            Rewards(ev.Rewards);
            if (selectedBranch is { } selected)
            {
                // Entry previews execute one option independently. Their child
                // input may already have been supplied by later applied actions.
                // Current event knowledge owns what is still unresolved; only
                // its surviving roots can qualify a historical preview result.
                var currentRoots = PredictorRouteCauseEvidence.OriginalCauses(ev.PendingCauses)
                    .Select(cause => cause.Identity).ToHashSet(StringComparer.Ordinal);
                ImmutableArray<PredictorCombatPendingCause> Residual(IEnumerable<PredictorCombatPendingCause> pending) =>
                    PredictorRouteCauseEvidence.OriginalCauses(pending).Where(cause => currentRoots.Contains(cause.Identity)).ToImmutableArray();
                if (ev.Rewards.IsEmpty && ev.Combat == null)
                    Rewards(selected.Rewards.Where(reward => reward.IsKnown ||
                        !Residual(reward.PendingCauses).IsEmpty).Select(reward => reward with
                        { PendingCauses = Residual(reward.PendingCauses),
                            Alternatives = reward.Alternatives.Select(alternative => alternative with
                            { PendingCauses = Residual(alternative.PendingCauses) }).ToImmutableArray() }).ToImmutableArray());
                Observe(Residual(selected.GeneratedCards.Where(card => !card.IsKnown).SelectMany(card => card.PendingCauses)),
                    L(text, "card_offers"));
                Observe(Residual(selected.RelicPendingCauses), L(text, "selected_branch_outcome"));
                // A remaining non-reward failure belongs to the current input
                // boundary. This producer's EventHealthOutcomePending records
                // deferred health commands (PropagateRouteEventHealthPending),
                // not missing arbitrary operation inputs. Drop only that scope
                // annotation here unless health is currently required; actual
                // health-dependent rewards/options are observed separately.
                if (!ev.Advanced && ev.Combat == null && selected.NeededInput == ev.NeededInput)
                    Observe(Residual(selected.LocalPendingCauses.Where(cause =>
                        cause.Code != "EventHealthOutcomePending" || ev.HealthRequired)),
                        L(text, "selected_branch_outcome"));
            }
            if (ev.Combat is { } combat) Rewards(combat.Rewards);
            Observe(ev.OfferedCardPendingCauses, L(text, "card_offers"));
            Observe(ev.GoldPendingCauses, L(text, "gold_balance"));
            Observe(ev.OccurrencePendingCauses, L(text, "event_occurrence"));
            // This is an actual admission consumer, not merely an unknown
            // future balance. Expand its original source while naming the
            // event occurrence that cannot yet be established.
            Observe(ev.PendingCauses.Where(cause => cause.Code == "EventGoldAppearancePending"),
                L(text, "event_occurrence"));
            foreach (var option in ev.Options)
            {
                if (option.Enabled != true) Observe(option.AvailabilityPendingCauses, L(text, "branch_availability"));
                if (option.Value == null) Observe(option.IdentityPendingCauses, L(text, "branch_options"));
            }
            // Local pending input is itself actionable even before it generates
            // rewards. Inherited inventory/RNG annotations are not local input.
            Observe(ev.PendingCauses.Where(cause => !ev.Advanced && cause.NodeId == node.Position.NodeId &&
                cause.ReasonKind is PredictorPendingReasonKind.UserChoice or PredictorPendingReasonKind.PlanBinding),
                L(text, "node_continuation"));
        }
        if (node.Shop is { } shop)
        {
            Observe(shop.GoldPendingCauses, L(text, "gold_balance"));
            var goldRoots = PredictorRouteCauseEvidence.OriginalCauses(shop.GoldPendingCauses)
                .Select(cause => cause.Identity).ToHashSet(StringComparer.Ordinal);
            Observe(PredictorRouteCauseEvidence.OriginalCauses(shop.Offers
                    .Where(offer => offer.Affordable == null && offer.Value?.Stocked != false)
                    .SelectMany(offer => offer.AffordabilityPendingCauses))
                .Where(cause => goldRoots.Contains(cause.Identity)), L(text, "shop_affordability"));
        }
        if (node.RestOffers.Length > 0 || !node.RestOfferPendingCauses.IsEmpty)
            Observe(node.RestOfferPendingCauses, L(text, "rest_options"));
        // Other room editors keep their own local fields. Actual unresolved
        // local inputs still receive a readable summary when no reward exists.
        if (node.Event == null)
            Observe(node.PendingCauses.Where(cause => cause.NodeId == node.Position.NodeId &&
                (cause.AffectedDomains & PredictorCombatDomain.Continuation) != 0 &&
                cause.ReasonKind is PredictorPendingReasonKind.UserChoice or PredictorPendingReasonKind.PlanBinding),
                L(text, "node_continuation"));

        // Missing optional Niche precision is not a node-wide task. A real
        // dependent reward above still exposes its original source and field.

        return causes.Values.Select(cause => new InRunMapPrerequisiteView(cause.Identity,
            text.Format(Prefix + "line", sourceLabel(cause.NodeId), Action(cause, text),
                string.Join(L(text, "separator"), results[cause.Identity])), results[cause.Identity].ToImmutableArray(),
            cause.NodeId != null && cause.NodeId != node.Position.NodeId ? sourceLabel(cause.NodeId) : null,
            EnemyCreationSource(cause) != null ? text.Format(Prefix + "input_summary",
                sourceLabel(cause.NodeId), Action(cause, text)) : null))
            .Concat(missing).ToImmutableArray();
    }
}

internal sealed partial class InRunMapNodePresenter
{
    private IReadOnlyList<string> PrerequisiteLines(PredictorRouteNodeKnowledge node) => PrerequisiteViews(node).Select(row => row.Text).ToArray();
    private IReadOnlyList<InRunMapPrerequisiteView> PrerequisiteViews(PredictorRouteNodeKnowledge node)
    {
        PredictorEventOptionResultKnowledge? selected = null;
        if (node.Event is { } ev && PredictorMapNodeIdentity.TryParse(node.Position.NodeId, out var identity) &&
            Current((identity.Act - 1, identity.Point)) is { } context && EventPlanApplies(context, ev))
        {
            var option = context.Plan.Event!.Actions.Take(ev.AppliedActions)
                .FirstOrDefault(action => action.Action == PredictorEventActionKind.Option)?.ExpectedOption;
            var initial = option == null ? null : ev.EntryOptions.FirstOrDefault(value => value.Value == option);
            if (initial?.ResultPreview != null)
            {
                var path = SelectedEventBranchPath(context, ev, initial).Select(value => value.ResultPreview!).ToArray();
                selected = path[^1] with
                {
                    Rewards = path.SelectMany(value => value.Rewards).ToImmutableArray(),
                    GeneratedCards = path.SelectMany(value => value.GeneratedCards).ToImmutableArray(),
                    RelicPendingCauses = PredictorRouteCauseEvidence.Normalize(path.SelectMany(value => value.RelicPendingCauses)),
                    LocalPendingCauses = PredictorRouteCauseEvidence.Normalize(path.SelectMany(value => value.LocalPendingCauses))
                };
            }
        }
        return InRunMapPrerequisiteProjection.Project(node, Source, _text, selected);
    }

    private string Source(string? nodeId)
    {
        var source = _session.Nodes.Values.FirstOrDefault(value => value.Position.NodeId == nodeId);
        return source == null ? M("prerequisite.previous_node") :
            _text.Format("predictor.map.prerequisite.source", source.Position.Floor, Title(source));
    }
}
