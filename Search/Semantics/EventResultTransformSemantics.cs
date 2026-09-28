using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

// Closed E whitelist only. Reuses the captured Basic/pool donor; neither occurrence
// nor final deck state is inferred. This is not an arbitrary event program.
internal static class EventResultTransformSemantics
{
    internal static bool IsTransform(EventResultConditionKind kind) => kind is
        EventResultConditionKind.MorphicGroveGroupInitialBasicsContains or
        EventResultConditionKind.SymbioteInitialBasicTransform or
        EventResultConditionKind.AromaOfChaosInitialBasicTransform or
        EventResultConditionKind.WhisperingHollowInitialBasicTransform or
        EventResultConditionKind.TrialNondescriptInitialBasicsContains;
    internal static int DrawCount(EventResultConditionKind kind) => kind is
        EventResultConditionKind.MorphicGroveGroupInitialBasicsContains or EventResultConditionKind.TrialNondescriptInitialBasicsContains ? 2 : 1;
    internal static string Entry(EventResultConditionKind kind) => kind switch {
        EventResultConditionKind.MorphicGroveGroupInitialBasicsContains => "MORPHIC_GROVE",
        EventResultConditionKind.SymbioteInitialBasicTransform => "SYMBIOTE",
        EventResultConditionKind.AromaOfChaosInitialBasicTransform => "AROMA_OF_CHAOS",
        EventResultConditionKind.WhisperingHollowInitialBasicTransform => "WHISPERING_HOLLOW",
        EventResultConditionKind.TrialNondescriptInitialBasicsContains => "TRIAL",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    internal static bool IsInitialBasics(EventResultSearchCondition c)
    {
        if (!IsTransform(c.Kind)) return false;
        if (DrawCount(c.Kind) == 2) return MorphicGroveQuerySemantics.IsInitialBasics(c.MorphicGroveScenario);
        var s = c.MorphicGroveScenario;
        return s is not null && s.Targets.Count == 1 && s.Targets[0].InstanceId == "basic:0" &&
            s.Premises.EntryFloor is null or >= 0 && !string.IsNullOrWhiteSpace(s.Premises.EventOccurrenceBasis) &&
            s.Targets[0].Original is { Rarity: "Basic", CanTransform: true } card && card.CardType != "Quest" &&
            s.Targets[0].OrderedSourceCandidates is not { Count: 0 };
    }
    internal static IReadOnlyList<MorphicGroveCard>? ClosedPool(EventResultSearchCondition c) =>
        IsInitialBasics(c) && c.MorphicGroveScenario!.Premises.VanillaLocalDependenciesBound &&
        c.MorphicGroveScenario.Authority.ProfileId == Compatibility.RuntimeProfileId.Beta111 &&
        c.MorphicGroveScenario.Authority.PlayerSlotIndex >= 0 &&
        c.MorphicGroveScenario.Authority.PlayerSlotIndex < c.MorphicGroveScenario.Authority.PlayersCount &&
        c.MorphicGroveScenario.Targets.All(t => t.OrderedSourceCandidates is { Count: > 0 })
            ? c.MorphicGroveScenario.Targets[0].OrderedSourceCandidates : null;

    internal static IReadOnlyList<MorphicGroveCard>? Project(ulong root, EventResultSearchCondition c)
    {
        if (ClosedPool(c) is not { } pool) return null;
        var rng = Core.World.Beta109.Beta109WorldRng.CreateEventLocal(root,
            c.MorphicGroveScenario!.Authority.PlayerSlotIndex,
            c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, Entry(c.Kind));
        if (c.Kind == EventResultConditionKind.WhisperingHollowInitialBasicTransform) rng.NextInt(19, "CalculateVars:gold-minus-nine");
        if (c.Kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains && rng.NextInt(3, "Accept:case") != 2)
            return [];
        var first = pool[rng.NextInt(pool.Count, "transform:0")];
        return DrawCount(c.Kind) == 1 ? [first] : [first, pool[rng.NextInt(pool.Count, "transform:1")]];
    }
    internal static MorphicGrovePredicateResult Evaluate(ulong root, EventResultSearchCondition c)
    {
        var outputs = Project(root, c);
        if (outputs is null) return MorphicGrovePredicateResult.Unknown;
        if (outputs.Count == 0) return MorphicGrovePredicateResult.NoMatch;
        bool match = c.MorphicGroveSecondCard is { } b
            ? outputs.Count == 2 && (outputs[0].CardKey == c.TargetKey && outputs[1].CardKey == b ||
                outputs[1].CardKey == c.TargetKey && outputs[0].CardKey == b)
            : outputs.Any(o => o.CardKey == c.TargetKey);
        return match ? MorphicGrovePredicateResult.Match : MorphicGrovePredicateResult.NoMatch;
    }
}
