using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunMap;

internal static partial class UiSurfaceChecks
{
    private static (InRunMapNodeDetailData Source, InRunMapNodeDetailData Dependent,
        InRunMapNodeDetailData Resolved) CheckInRunMapMissingCountRecompute(
        InRunMapPredictionSession production, MapPosition first, MapPosition suffix,
        IGameContentNameResolver names, IUiTextProvider text, Func<string> signature,
        Action<bool, string> check)
    {
        string nativeBefore = signature(), plansBefore = JsonSerializer.Serialize(production.Plans);
        var captured = production.Snapshot!;
        var source = captured.Run;
        var initial = source.Working;
        int act = initial.Position.Act;
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        InRunMapPredictionSession Fixture(bool wing, bool secondFabricator)
        {
            // Only initial input is controlled. Both node observations and all
            // their causes/rewards come from the production detached replay.
            var configured = initial with
            {
                Relics = wing ? [new(initial.NextInstanceId, new("RELIC", "WING_CHARM"), initial.Position.Floor)] : [],
                NextInstanceId = initial.NextInstanceId + (wing ? 1 : 0),
                Maps = initial.Maps.Select(map => map.Act != act ? map : map with
                { Map = map.Map with { Points = map.Map.Points.Select(point =>
                    point.Position == first || point.Position == suffix ? point with
                    { Type = MapPointType.Monster } : point).ToArray() } }).ToImmutableArray(),
                Acts = initial.Acts.Select(value => value.Act != act ? value : value with
                { NormalEncounters = [new("ENCOUNTER", "FABRICATOR_NORMAL"),
                    new("ENCOUNTER", secondFabricator ? "FABRICATOR_NORMAL" : "FUZZY_WURM_CRAWLER_WEAK")],
                    NormalCursor = 0 }).ToImmutableArray()
            };
            var session = new InRunMapPredictionSession();
            typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot), members)!
                .SetValue(session, captured with { Run = new PredictorRun(source.Context, configured)
                { CombatDefaults = new(), AutomaticRewardPickup = source.AutomaticRewardPickup } });
            typeof(InRunMapPredictionSession).GetField("_drafts", members)!
                .SetValue(session, DiagnosticField(production, "_drafts"));
            session.Recompute();
            check(session.Nodes.Count == 2 && session.Nodes[(act - 1, first)].Model?.Entry == "FABRICATOR_NORMAL" &&
                session.Nodes[(act - 1, suffix)].Room == PredictorRoomKind.Monster,
                "missing-count fixture projects two actual connected combats from controlled initial input");
            return session;
        }
        var session = Fixture(wing: true, secondFabricator: true);
        var presenter = new InRunMapNodePresenter(session, names, text);
        var firstNode = session.Nodes[(act - 1, first)];
        var secondNode = session.Nodes[(act - 1, suffix)];
        string Label(string? id)
        {
            var node = session.Nodes.Values.Single(value => value.Position.NodeId == id);
            return text.Format("predictor.map.prerequisite.source", node.Position.Floor,
                names.Resolve(node.Model!.Value, GameContentKind.Encounter));
        }
        var root = PredictorRouteCauseEvidence.OriginalCauses(firstNode.PendingCauses)
            .Single(cause => cause.Code == "EnemyCreationCount" && cause.NodeId == firstNode.Position.NodeId);
        var independent = PredictorRouteCauseEvidence.OriginalCauses(secondNode.PendingCauses)
            .Single(cause => cause.Code == "EnemyCreationCount" && cause.NodeId == secondNode.Position.NodeId);
        var ownRows = InRunMapPrerequisiteProjection.Project(firstNode, Label, text);
        var downstreamRows = InRunMapPrerequisiteProjection.Project(secondNode, Label, text);
        string enemyLabel = text.Get("predictor.runtime.enemy.FabricatorBots");
        check(ownRows.Single(row => row.Identity == root.Identity).Summary is { } ownSummary &&
            ownSummary.Contains(Label(firstNode.Position.NodeId), StringComparison.Ordinal) &&
            ownSummary.Contains(enemyLabel, StringComparison.Ordinal),
            "source combat summary names its actual encounter and the missing generation quantity");
        check(downstreamRows.Single(row => row.Identity == root.Identity).Summary is { } downstreamSummary &&
            downstreamSummary.Contains(Label(firstNode.Position.NodeId), StringComparison.Ordinal) &&
            downstreamSummary.Contains(enemyLabel, StringComparison.Ordinal) &&
            downstreamRows.Any(row => row.Identity == independent.Identity),
            "actual downstream Wing Charm dependency names its source while retaining its own independent input");
        check(firstNode.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown) &&
            secondNode.Rewards.Any(reward => reward.Kind == PredictorRewardKind.Gold && reward.IsKnown),
            "missing generation counts leave independent real Rewards results known at both nodes");
        var beforeSource = presenter.Detail((act - 1, first))!;
        var beforeDependent = presenter.Detail((act - 1, suffix))!;
        var partialCard = firstNode.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card &&
            !reward.CardOptions.IsEmpty && !reward.PendingCauses.IsEmpty);
        var partialSection = beforeSource.Sections.Single(section =>
            section.Objects.Select(item => item.CardSnapshot).SequenceEqual(partialCard.CardOptions.Select(option => option.Value)));
        check(partialSection.Choices.Count == 0 && partialSection.Objects.All(item => item.CardSnapshot?.Id == 0) &&
            partialCard.CardOptions.Where(option => !option.EnchantmentPendingCauses.IsEmpty)
                .All(option => option.Value.Enchantment == null && option.Value.EnchantmentAmount == 0) &&
            ownRows.Single(row => row.Identity == root.Identity).AffectedResults.Contains(
                text.Get("predictor.map.prerequisite.card_enchantments")) &&
            !ownRows.Single(row => row.Identity == root.Identity).AffectedResults.Contains(text.Get("predictor.runtime.reward.Card")),
            "actual partial card reward shows known identities/levels read-only without guessed enchantments and scopes its cause to enchantments");
        check(beforeSource.PrerequisiteSummary?.Contains(enemyLabel, StringComparison.Ordinal) == true &&
            beforeDependent.PrerequisiteSummary?.Contains(enemyLabel, StringComparison.Ordinal) == true,
            "resident presenter summaries expose the actionable missing quantity at source and dependent nodes");
        var input = beforeSource.Premises!.Numbers.Single(field => field.Label == enemyLabel);
        long revision = session.Revision;
        check(presenter.ApplyField(firstNode.Position.NodeId, input.Id, new(Number: 2)) && session.Revision > revision &&
            session.Plans.Single(plan => plan.NodeId == firstNode.Position.NodeId).Combat!.EnemyCreations
                .Single(fact => fact.Source == PredictorEnemyCreationSource.FabricatorBots).Count == 2,
            "existing combat number entry authors the actual count and recomputes the route");
        check(!presenter.ApplyField(firstNode.Position.NodeId, input.Id, new(Number: 3)),
            "count entry from the old revision expires after recomputation");
        var afterFirst = session.Nodes[(act - 1, first)];
        var afterSecond = session.Nodes[(act - 1, suffix)];
        check(InRunMapPrerequisiteProjection.Project(afterFirst, Label, text).All(row => row.Identity != root.Identity) &&
            InRunMapPrerequisiteProjection.Project(afterSecond, Label, text).All(row => row.Identity != root.Identity) &&
            InRunMapPrerequisiteProjection.Project(afterSecond, Label, text).Any(row => row.Identity == independent.Identity),
            "entering source count removes only its cause across the actual chain and keeps the independent count pending");

        var unrelated = Fixture(wing: false, secondFabricator: false);
        var unrelatedNode = unrelated.Nodes[(act - 1, suffix)];
        check(PredictorRouteCauseEvidence.OriginalCauses(unrelatedNode.PendingCauses)
                .Any(cause => cause.Code == "EnemyCreationCount") &&
            InRunMapPrerequisiteProjection.Project(unrelatedNode, _ => "unexpected source", text)
                .All(row => !row.Text.Contains(enemyLabel, StringComparison.Ordinal) && row.Summary == null),
            "inherited unknown Niche does not warn an unrelated downstream combat with no actual count consumer");
        check(signature() == nativeBefore && JsonSerializer.Serialize(production.Plans) == plansBefore &&
            JsonSerializer.Serialize(source.Working) == JsonSerializer.Serialize(initial),
            "controlled count-entry chain preserves native run/RNG, production plans and initial captured state");
        return (beforeSource, beforeDependent, presenter.Detail((act - 1, suffix))!);
    }
}
