using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Search.Semantics;

/// <summary>Bounded Group/InitialBasics Query contract. No event occurrence or deck-history proof.</summary>
internal static class MorphicGroveQuerySemantics
{
    internal static RolltheSpire2.Search.Contracts.EventResultSearchCondition Normalize(
        RolltheSpire2.Search.Contracts.EventResultSearchCondition condition) =>
        EventResultTransformSemantics.IsTransform(condition.Kind) &&
        condition.MorphicGroveSecondCard is { } second && StringComparer.Ordinal.Compare(condition.TargetKey.Serialized, second.Serialized) > 0
            ? condition with { TargetKey = second, MorphicGroveSecondCard = condition.TargetKey } : condition;
    internal static bool IsInitialBasics(MorphicGroveScenario? scenario)
    {
        if (scenario is null || scenario.Targets.Count != 2 || scenario.Targets.Select(t => t.InstanceId).Distinct().Count() != 2 || scenario.Premises.EntryFloor < 0 ||
            string.IsNullOrWhiteSpace(scenario.Premises.EventOccurrenceBasis)) return false;
        var first = scenario.Targets.SingleOrDefault(t => t.InstanceId == MorphicGroveCommitment.InitialBasics.FirstInstanceId);
        var second = scenario.Targets.SingleOrDefault(t => t.InstanceId == MorphicGroveCommitment.InitialBasics.SecondInstanceId);
        if (first is null || second is null || scenario.Targets.Any(t =>
            t.Original.Rarity != "Basic" || !t.Original.CanTransform || t.Original.CardType == "Quest" ||
            t.OrderedSourceCandidates is { Count: 0 })) return false;
        // Missing authority remains Unknown. A supplied heterogeneous/empty pool is not this Product shape.
        return first.OrderedSourceCandidates is not { } a || second.OrderedSourceCandidates is not { } b ||
            a.Count > 0 && a.SequenceEqual(b);
    }

    internal static IReadOnlyList<MorphicGroveCard>? ClosedPool(MorphicGroveScenario? scenario) =>
        IsInitialBasics(scenario) && scenario!.Premises.VanillaLocalDependenciesBound &&
        scenario.Authority.ProfileId == RolltheSpire2.Compatibility.RuntimeProfileId.Beta111 &&
        scenario.Targets.All(t => t.OrderedSourceCandidates is { Count: > 0 })
            ? scenario.Targets[0].OrderedSourceCandidates : null;

    // Pool order, premises and authority are semantic inputs. Hash the actual immutable
    // payload; never identify this condition using just its selected Card identity.
    internal static string Fingerprint(MorphicGroveScenario? scenario) => scenario?.Fingerprint ?? "";
}
