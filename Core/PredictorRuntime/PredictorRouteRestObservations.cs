using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private sealed record RestObtainReceipt(string NodeId, int InputIndex, long SourceId, PredictorRelic Relic);
    private readonly List<RestObtainReceipt> _routeRestObtains = [];

    private static ImmutableArray<PredictorCombatPendingCause> RouteClonePendingCauses(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        var deck = RouteDeckKnowledge(run);
        var population = deck.Project(PredictorDeckProjectionScope.All);
        // Every member's enchantment identifies the originals. Only those
        // originals contribute their full persistent payload to the copies.
        var eligibilityGaps = population.Gaps.AddRange(population.KnownCandidates.SelectMany(card =>
            deck.TargetGaps(card.Id, PredictorDeckProjectionScope.All,
                PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Enchantment)))
            .Distinct().ToImmutableArray();
        var causes = RouteDeckProjectionCauses(run, population with { Gaps = eligibilityGaps }, dependencies);
        var originals = population.KnownCandidates.Where(card => card.Enchantment?.Entry == "CLONE")
            .Select(card => new PredictorPlannedCard(card.Id, card.Key, card.FloorAdded));
        return PredictorRouteCauseEvidence.Normalize(causes.AddRange(RouteDeckTargetCauses(run, originals,
            dependencies, knownInstanceIds, PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.All)));
    }

    // Hatch's native option suppliers and fixed Byrdpip -> Byrd Swoop obtain
    // read Egg membership/model identities, not unrelated card upgrades or
    // persistent payloads. Clone still owns its complete-card copy contract.
    private static ImmutableArray<PredictorCombatPendingCause> RouteHatchPendingCauses(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
    {
        var eggs = run.Working.Deck.Select(run.Working.Card).Where(card => card.Key.Entry == "BYRDONIS_EGG").ToArray();
        var membership = dependencies.Where(dependency => RouteCardMayVary(run, [dependency], "BYRDONIS_EGG"))
            .Select(dependency => dependency.Cause);
        var targets = eggs.Select(card => new PredictorPlannedCard(card.Id, card.Key, card.FloorAdded)).ToArray();
        var targetCauses = RouteDeckTargetCauses(run, targets, dependencies, knownInstanceIds,
            PredictorDeckProjectionScope.All, PredictorDeckUncertainFact.Definition);
        // A supplier remains an entry-time option instance even after an
        // earlier Hatch transformed all Eggs. Do not demand current membership
        // of that removed source, but keep uncertain source allocation scoped.
        var identities = run._restHatchSuppliers.Any(id => !knownInstanceIds.Contains(id))
            ? CausesFor(dependencies, PredictorCombatDomain.InstanceIds) : [];
        return PredictorRouteCauseEvidence.Normalize(membership.Concat(targetCauses).Concat(identities));
    }

    private void RecordRouteRestObtain(PredictorObtainReturn destination, long sourceId, long obtainedId)
    {
        if (destination != PredictorObtainReturn.Rest) return;
        string node = Working.Position.NodeId;
        if (_routeRestObtains.Count != 0 && _routeRestObtains[0].NodeId != node) _routeRestObtains.Clear();
        _routeRestObtains.Add(new(node, _inputs.Length, sourceId, Working.Relics.Single(relic => relic.Id == obtainedId)));
    }

    private static ImmutableArray<PredictorRouteCardKnowledge> ObserveRouteTargetCards(PredictorRun run,
        List<RouteDependency> dependencies, HashSet<long> knownInstanceIds,
        PredictorDeckProjectionScope scope, PredictorDeckUncertainFact facts)
        => run.Working.Deck.Select(run.Working.Card).Where(card => scope switch
        {
            PredictorDeckProjectionScope.Upgradable => card.UpgradeLevel < card.MaxUpgradeLevel,
            PredictorDeckProjectionScope.Removable => card.Removable,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        }).Select(card =>
        {
            var target = new PredictorPlannedCard(card.Id, card.Key, card.FloorAdded);
            return new PredictorRouteCardKnowledge(card,
                IsKnownRouteDeckTarget(run, target, dependencies, knownInstanceIds, scope, facts) ? [] :
                    RouteDeckTargetCauses(run, [target], dependencies, knownInstanceIds, scope, facts))
            { FullCardKnown = IsKnownRouteDeckTarget(run, target, dependencies, knownInstanceIds) };
        }).ToImmutableArray();

    private static PredictorRestActionKnowledge ObserveRouteRestAction(PredictorRun run, int index,
        PredictorRestAction action, List<RouteDependency> dependencies, HashSet<long> knownInstanceIds)
        => new(index, action, action is PredictorRestAction.Smith or PredictorRestAction.Cook
            ? ObserveRouteTargetCards(run, dependencies, knownInstanceIds,
                action == PredictorRestAction.Smith ? PredictorDeckProjectionScope.Upgradable : PredictorDeckProjectionScope.Removable,
                action == PredictorRestAction.Smith ? RouteLevelEditFacts : PredictorDeckUncertainFact.Definition)
            : [], []);

    private static PredictorRestActionKnowledge RestActionReceipts(PredictorRun run,
        PredictorRestActionKnowledge action, int inputStart)
        => action with { ObtainedRelics = run._routeRestObtains.Where(receipt =>
            receipt.NodeId == run.Working.Position.NodeId && receipt.InputIndex >= inputStart &&
            receipt.InputIndex < run._inputs.Length).Select(receipt => receipt.Relic.Key).ToImmutableArray() };

    // Active native input prefixes need the same per-action boundaries as new
    // plans. Replay only their actual accepted inputs on a detached entry copy.
    private static List<PredictorRestActionKnowledge> ObserveAcceptedRestActions(PredictorRun run, CancellationToken token)
    {
        var actions = new List<PredictorRestActionKnowledge>();
        if (run._nodeReplayInputs.IsEmpty) return actions;
        var replay = run.CreateEntryReplay(PreviewReplayPurpose.RoomObservation);
        replay.RestoreRoutes(run._nodeEntryRoutes, run._preview.Settings.AcceptsPrefixes);
        replay.AdvanceRoute(token);
        PredictorRestActionKnowledge? pending = null;
        int start = 0;
        foreach (var input in run._nodeReplayInputs)
        {
            token.ThrowIfCancellationRequested();
            if (input is SelectRestAction selected)
            {
                pending = ObserveRouteRestAction(replay, actions.Count, selected.Action, [], replay.Working.Deck.ToHashSet());
                start = replay._inputs.Length;
            }
            int before = replay._inputs.Length;
            if (replay.Request is not { } request || replay.Submit(request, input, token) is PredictorInputResult.Invalid or PredictorInputResult.Stale)
                throw new InvalidOperationException("PredictorRestActivePrefixReplayMismatch");
            if (pending != null && replay._inputs.Skip(before).Any(record => record.Action is
                "RestSmith" or "RestCookRemoveTwoGainMaxHp5" or "RestLift" or "RestDig" or "RestClone" or "RestHatch" or "RestKindle" or "RestHeal"))
            {
                actions.Add(RestActionReceipts(replay, pending, start));
                pending = null;
            }
            if (input is CancelSmithSelection or CancelCookSelection) pending = null;
        }
        return actions;
    }
}
