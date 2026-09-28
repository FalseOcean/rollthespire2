using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

public enum TransformationOpening { None, LeafyPoultice, NewLeaf, BonesLeafyNewLeaf, BonesLeafyOther }
public enum TransformationPickupOrder { Any, LeafyThenNewLeaf, NewLeafThenLeafy, LeafyThenCompanion, CompanionThenLeafy }
public enum TransformationAggregatePredicate { RareCountAtLeast, ContainsMultiset }

/// <summary>One indivisible Query obligation. Event inputs are captured conditional
/// initial-Basic premises, never an assertion that an event occurs on a route.</summary>
public sealed record TransformationAggregateCondition(
    TransformationOpening Opening,
    TransformationPickupOrder PickupOrder,
    bool MorphicGrove,
    bool AromaOfChaos,
    bool WhisperingHollow,
    TransformationAggregatePredicate Predicate,
    int MinimumRareCount,
    IReadOnlyList<ModelKey> TargetMultiset)
{
    // The existing immutable initial-Basic capture is also the source pool for
    // selected one/two-card events. No mutable game state is stored.
    public MorphicGroveScenario? EventScenario { get; init; }
    public bool Symbiote { get; init; }
    public bool TrialNondescript { get; init; }
    public ModelKey? CompanionRelic { get; init; }
    internal bool IsBones => Opening is TransformationOpening.BonesLeafyNewLeaf or TransformationOpening.BonesLeafyOther;
    internal ModelKey BonesCompanion => Opening == TransformationOpening.BonesLeafyOther ? CompanionRelic!.Value : BaseGameModelKeys.Relics.NewLeaf;
    internal bool LeafyFirst => PickupOrder is TransformationPickupOrder.LeafyThenNewLeaf or TransformationPickupOrder.LeafyThenCompanion;
    public bool UsesNeow => Opening != TransformationOpening.None;
    public bool UsesEvents => MorphicGrove || AromaOfChaos || WhisperingHollow || Symbiote || TrialNondescript;
    public int OpportunityCount => (Opening switch {
        TransformationOpening.LeafyPoultice => 2, TransformationOpening.NewLeaf => 1,
        TransformationOpening.BonesLeafyNewLeaf => 3, TransformationOpening.BonesLeafyOther => 2, _ => 0 }) +
        (MorphicGrove ? 2 : 0) + (AromaOfChaos ? 1 : 0) + (WhisperingHollow ? 1 : 0) + (Symbiote ? 1 : 0) + (TrialNondescript ? 2 : 0);

    internal TransformationAggregateCondition Normalize()
    {
        if (!Enum.IsDefined(Opening) || !Enum.IsDefined(PickupOrder) || !Enum.IsDefined(Predicate) ||
            !IsBones && PickupOrder != TransformationPickupOrder.Any ||
            OpportunityCount == 0 || TargetMultiset is null)
            throw new ArgumentException("TransformationAggregate.InvalidContributors");
        if (Opening == TransformationOpening.BonesLeafyOther
            ? CompanionRelic is not { IsValid: true } k || k.Category != "RELIC" || k == BaseGameModelKeys.Relics.LeafyPoultice || k == BaseGameModelKeys.Relics.NeowsBones || k == BaseGameModelKeys.Relics.NewLeaf || PickupOrder is TransformationPickupOrder.LeafyThenNewLeaf or TransformationPickupOrder.NewLeafThenLeafy
            : CompanionRelic.HasValue || PickupOrder is TransformationPickupOrder.LeafyThenCompanion or TransformationPickupOrder.CompanionThenLeafy)
            throw new ArgumentException("TransformationAggregate.InvalidBonesCompanion");
        if (Predicate == TransformationAggregatePredicate.RareCountAtLeast
            ? MinimumRareCount < 1 || MinimumRareCount > OpportunityCount || TargetMultiset.Count != 0
            : MinimumRareCount != 0 || TargetMultiset.Count < 1 || TargetMultiset.Count > OpportunityCount ||
              TargetMultiset.Any(k => !k.IsValid || k.Category != "CARD"))
            throw new ArgumentException("TransformationAggregate.InvalidPredicate");
        if (UsesEvents && !ValidEventPremises())
            throw new ArgumentException("TransformationAggregate.InitialBasicEventPremisesRequired");
        return this with { TargetMultiset = Array.AsReadOnly(TargetMultiset.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray()),
            EventScenario = UsesEvents ? EventScenario : null };
    }

    private bool ValidEventPremises()
    {
        if (MorphicGrove || TrialNondescript) return MorphicGroveQuerySemantics.IsInitialBasics(EventScenario);
        return EventScenario is { } s && s.Targets.Count is 1 or 2 &&
            !string.IsNullOrWhiteSpace(s.Premises.EventOccurrenceBasis) && s.Premises.EntryFloor is not < 0 &&
            s.Targets[0].InstanceId == "basic:0" && s.Targets[0].Original.Rarity == "Basic" &&
            s.Targets[0].Original.CanTransform && s.Targets[0].Original.CardType != "Quest" &&
            s.Targets[0].OrderedSourceCandidates is not { Count: 0 };
    }

    internal string SemanticIdentity => string.Join("|", Opening, PickupOrder, MorphicGrove, AromaOfChaos,
        WhisperingHollow, Symbiote, TrialNondescript, CompanionRelic, Predicate, MinimumRareCount,
        string.Join(',', TargetMultiset.Select(k => k.Serialized)), EventScenario?.Fingerprint ?? "none");

    internal static void ValidateExclusiveOwnership(SearchQuery query)
    {
        if (query.TransformationAggregate is not { } condition) return;
        _ = condition.Normalize();
        if (condition.UsesNeow && query.OpeningRoute is { } opening)
        {
            if (opening.RouteRelicKey != condition.RequiredOpeningKey)
                throw new ArgumentException("TransformationAggregate.NeowCommitmentMismatch");
            if (condition.IsBones)
            {
                var pair = query.OpeningRouteRelicRequirement;
                if (pair is null || pair.RequiredRelicKeys.Count != 2 ||
                    !pair.RequiredRelicKeys.Contains(BaseGameModelKeys.Relics.LeafyPoultice) || !pair.RequiredRelicKeys.Contains(condition.BonesCompanion))
                    throw new ArgumentException("TransformationAggregate.NeowPairMismatch");
                var order = pair.OrderMode == BonesRouteOrderMode.AnyOrder ? TransformationPickupOrder.Any :
                    pair.RequiredRelicKeys[0] == BaseGameModelKeys.Relics.LeafyPoultice ? (condition.Opening==TransformationOpening.BonesLeafyOther ? TransformationPickupOrder.LeafyThenCompanion : TransformationPickupOrder.LeafyThenNewLeaf) :
                    (condition.Opening==TransformationOpening.BonesLeafyOther ? TransformationPickupOrder.CompanionThenLeafy : TransformationPickupOrder.NewLeafThenLeafy);
                if (order != condition.PickupOrder) throw new ArgumentException("TransformationAggregate.NeowOrderMismatch");
            }
        }
        else if (condition.UsesNeow && (query.OpeningRouteRelicRequirement is not null || query.StructuredOpeningEffects.Count != 0 || HasLegacyNeow(query.LegacyNeow)))
            throw new ArgumentException("TransformationAggregate.ExplicitNeowCommitmentRequired");
        if (condition.MorphicGrove && query.EventResultConditions.Any(c =>
            c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains))
            throw new ArgumentException("TransformationAggregate.MorphicGroveOwnershipConflict");
        if (condition.AromaOfChaos && query.EventResultConditions.Any(c => c.Kind == EventResultConditionKind.AromaOfChaosInitialBasicTransform) ||
            condition.WhisperingHollow && query.EventResultConditions.Any(c => c.Kind == EventResultConditionKind.WhisperingHollowInitialBasicTransform) ||
            condition.Symbiote && query.EventResultConditions.Any(c => c.Kind == EventResultConditionKind.SymbioteInitialBasicTransform) ||
            condition.TrialNondescript && query.EventResultConditions.Any(c => c.Kind is EventResultConditionKind.TrialNondescriptInitialBasicsContains or EventResultConditionKind.TrialCase))
            throw new ArgumentException("TransformationAggregate.EventResultOwnershipConflict");
        // Fixed Bones Leafy+NewLeaf has the same Rewards continuation in bound and
        // aggregate authoring: Bones offer shuffle, then no child Rewards consumption.
        // Other aggregate openings/continuations retain their existing boundary.
        if (condition.UsesNeow && (query.HasCombatRewardConstraints && !HasFixedBonesRewardContinuation(query) ||
            query.RelicSequenceConstraints.Any(c => !c.IsEmpty) || query.RelicShopSequenceConditions.Any(c => !c.IsEmpty)))
            throw new ArgumentException("TransformationAggregate.NeowDependentRewardRelicContinuationNotSupportedInV1");
    }

    internal static bool HasFixedBonesRewardContinuation(SearchQuery query) =>
        query.TransformationAggregate is { Opening: TransformationOpening.BonesLeafyNewLeaf, UsesEvents: false,
            Predicate: TransformationAggregatePredicate.ContainsMultiset } &&
        query.OpeningRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
        query.OpeningRouteRelicRequirement is { RequiredRelicKeys.Count: 2 } pair &&
        pair.RequiredRelicKeys.Contains(BaseGameModelKeys.Relics.LeafyPoultice) &&
        pair.RequiredRelicKeys.Contains(BaseGameModelKeys.Relics.NewLeaf);

    internal ModelKey RequiredOpeningKey => Opening switch {
        TransformationOpening.LeafyPoultice => BaseGameModelKeys.Relics.LeafyPoultice,
        TransformationOpening.NewLeaf => BaseGameModelKeys.Relics.NewLeaf,
        _ => BaseGameModelKeys.Relics.NeowsBones };

    internal static bool HasSharedNeow(ExactSearchEvaluationProjection e) =>
        e.TransformationAggregate is { UsesNeow: true } && e.NeowRoute is not null;

    // Identity/pickup commitment is already necessary for T. More authored N
    // predicates can inspect these same outputs; their joint must not be multiplied.
    internal static bool SharedNeowIdentityOnly(ExactSearchEvaluationProjection e) => HasSharedNeow(e) &&
        (!e.RequireNeowsBones || e.TransformationAggregate!.IsBones) &&
        (e.TransformationAggregate!.IsBones ||
            e.RequiredBonesCombination.Count == 0 && e.RequiredBonesAcquisitionOrder.Count == 0) &&
        e.NeowRelics.IsEmpty && e.BonesRelics.IsEmpty && !e.RequireSmallCapsule && !e.RequireLargeCapsule &&
        e.CapsuleContainedRelics.IsEmpty && !e.RequireWhetstone && !e.RequireWarPaint &&
        e.RequiredFinalCurse is null && e.BannedFinalCurses.Count == 0 && e.Preset == NeowSearchPreset.None &&
        e.StructuredNeowEffects.Count == 0 && e.EffectOutputConditions.Count == 0;

    private static bool HasLegacyNeow(LegacyNeowSemanticConstraints n) => !n.NeowRelics.IsEmpty ||
        n.RequireNeowsBones || !n.BonesRelics.IsEmpty || n.RequiredBonesCombination.Count != 0 ||
        n.RequireSmallCapsule || n.RequireLargeCapsule || !n.CapsuleContainedRelics.IsEmpty || n.RequireWhetstone ||
        n.RequireWarPaint || n.RequiredFinalCurse.HasValue || n.BannedFinalCurses.Count != 0 ||
        n.Preset != NeowSearchPreset.None || n.RequiredBonesAcquisitionOrder.Count != 0 || n.EffectOutputConditions.Count != 0;
}
