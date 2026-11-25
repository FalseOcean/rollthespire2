using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Transformation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// Shared capture for count kernels and query probability. Slot order and multiplicity
// matter to the kernel, while the distribution also observes the captured rarity.
internal static partial class ScopedTransformationPools
{
    internal static bool Supports(SearchModel model, TransformationAggregateCondition condition) =>
        condition is { UsesExtendedRequirements: true, IsBones: false } && SupportsPersonalStream(model, condition);

    internal static bool SupportsBonesDistribution(SearchModel model, TransformationAggregateCondition condition) =>
        condition.IsBones && SupportsPersonalStream(model, condition);

    private static bool SupportsPersonalStream(SearchModel model, TransformationAggregateCondition condition) =>
        model.Authority.PlayersCount == 1 && model.Authority.PlayerSlotIndex == 0 &&
        model.Authority.ProfileId == RuntimeProfileId.Beta111 &&
        model.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113 &&
        condition.AncientOpportunityCount is >= 0 and <= TransformationRelicProjector.ProjectionDrawBudget;

    internal static ModelKey[][]? Opening(SearchModel model, ModelKey? opening, NeowEffectAuthoritySnapshot effects)
    {
        if (opening == BaseGameModelKeys.Relics.LargeCapsule)
        {
            // The forced pair adds materials. Nested Whetstone and War Paint
            // upgrade via non_combat_generation, leaving transformations at zero.
            return OpeningTransformationContinuation.HasNativeCapsuleCursor(model.Authority) ? [] : null;
        }
        if (opening.HasValue && NeowEffectCoverageRegistry.TryGet(opening.Value, out var entry) &&
            entry.Family == NeowEffectFamily.DeterministicImmediate) return [];
        if (opening != BaseGameModelKeys.Relics.NewLeaf && opening != BaseGameModelKeys.Relics.LeafyPoultice) return null;
        var catalog = Beta110FastEffectCatalogCompiler.Compile(effects, []);
        ushort[][] pools;
        if (opening == BaseGameModelKeys.Relics.NewLeaf && catalog.NewLeafTransformAuthorityExact)
            pools = [catalog.NewLeafTransformPool];
        else if (opening == BaseGameModelKeys.Relics.LeafyPoultice && catalog.LeafyTransformAuthorityExact)
            pools = [catalog.LeafyStrikeTransformPool, catalog.LeafyDefendTransformPool];
        else return null;
        return pools.Select(pool => pool.Select(id => catalog.DenseKeys[id]).ToArray()).ToArray();
    }

    internal static IReadOnlyList<MorphicGroveCard>[]? Event(EventResultConditionKind kind, MorphicGroveScenario scenario)
    {
        bool trial = kind == EventResultConditionKind.TrialNondescriptInitialBasicsContains;
        bool grove = kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains;
        if (!scenario.Premises.VanillaLocalDependenciesBound || scenario.Premises.EntryFloor < 0 ||
            string.IsNullOrWhiteSpace(scenario.Premises.EventOccurrenceBasis)) return null;
        if ((trial || kind == EventResultConditionKind.SymbioteInitialBasicTransform) &&
            EventResultTransformSemantics.ClosedPool(EventResultTransformSemantics.ForAggregate(kind, scenario)) is null)
            return null;
        int draws = EventResultTransformSemantics.DrawCount(kind);
        if (scenario.Targets.Count < draws || grove && scenario.Targets.Count != draws) return null;
        var pools = new IReadOnlyList<MorphicGroveCard>[draws];
        var basics = MorphicGroveCommitment.InitialBasics;
        for (int i = 0; i < draws; i++)
        {
            // Group resolves each committed instance by ID. Single-target events take the
            // first captured target; Trial's closed contract requires identical ordered pools.
            string id = i == 0 ? basics.FirstInstanceId : basics.SecondInstanceId;
            var target = grove ? scenario.Targets.FirstOrDefault(t => t.InstanceId == id) : scenario.Targets[0];
            if (target is not { Original.CanTransform: true, Original.CardType: not "Quest", OrderedSourceCandidates.Count: > 0 })
                return null;
            pools[i] = target.OrderedSourceCandidates;
        }
        return pools;
    }
}
