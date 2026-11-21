using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

public enum TransformationOpening { None, LeafyPoultice, NewLeaf, BonesLeafyNewLeaf, BonesLeafyOther }
public enum TransformationPickupOrder { Any, LeafyThenNewLeaf, NewLeafThenLeafy, LeafyThenCompanion, CompanionThenLeafy }
public enum TransformationAggregatePredicate { RareCountAtLeast, ContainsMultiset, ContainsMultisetAndRemainingRare, ContainsMultisetAndRareCount }
public enum TransformationAncientSource { None, Astrolabe, PandorasBox }

/// <summary>One predicate over the selected sources of the same generated outcome.</summary>
public sealed record TransformationResultRequirement(IReadOnlyList<string> Sources,
    TransformationAggregatePredicate Predicate, int MinimumRareCount, IReadOnlyList<ModelKey> TargetMultiset);

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
    public TransformationAncientSource AncientSource { get; init; }
    public int AncientAct { get; init; }
    public int? PandoraCountOverride { get; init; }
    public int PandoraAutomaticCount { get; init; }
    public IReadOnlyList<TransformationResultRequirement> ResultRequirements { get; init; } = [];
    public bool UsesAncient => AncientSource != TransformationAncientSource.None;
    internal ModelKey RequiredAncientKey => new("RELIC", AncientSource == TransformationAncientSource.Astrolabe ? "ASTROLABE" : "PANDORAS_BOX");
    public bool UsesExtendedRequirements => UsesAncient || ResultRequirements.Count > 0 || Predicate == TransformationAggregatePredicate.ContainsMultisetAndRareCount;
    public int AncientOpportunityCount => AncientSource switch {
        TransformationAncientSource.Astrolabe => 3,
        TransformationAncientSource.PandorasBox => PandoraCountOverride ?? PandoraAutomaticCount, _ => 0 };
    internal bool RequiresRareRemainder => Predicate == TransformationAggregatePredicate.ContainsMultisetAndRemainingRare;
    internal bool IsBones => Opening is TransformationOpening.BonesLeafyNewLeaf or TransformationOpening.BonesLeafyOther;
    internal ModelKey BonesCompanion => Opening == TransformationOpening.BonesLeafyOther ? CompanionRelic!.Value : BaseGameModelKeys.Relics.NewLeaf;
    internal bool LeafyFirst => PickupOrder is TransformationPickupOrder.LeafyThenNewLeaf or TransformationPickupOrder.LeafyThenCompanion;
    public bool UsesNeow => Opening != TransformationOpening.None;
    public bool UsesEvents => MorphicGrove || AromaOfChaos || WhisperingHollow || Symbiote || TrialNondescript;
    public int OpportunityCount => (int)Math.Min(int.MaxValue, (long)(Opening switch {
        TransformationOpening.LeafyPoultice => 2, TransformationOpening.NewLeaf => 1,
        TransformationOpening.BonesLeafyNewLeaf => 3, TransformationOpening.BonesLeafyOther => 2, _ => 0 }) +
        (MorphicGrove ? 2 : 0) + (AromaOfChaos ? 1 : 0) + (WhisperingHollow ? 1 : 0) + (Symbiote ? 1 : 0) + (TrialNondescript ? 2 : 0) + AncientOpportunityCount);

    public IReadOnlyDictionary<string, int> SourceCounts()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (UsesNeow) result[Opening switch {
            TransformationOpening.LeafyPoultice => "N.LeafyPoultice", TransformationOpening.NewLeaf => "N.NewLeaf",
            TransformationOpening.BonesLeafyOther => "N.BonesLeafyOther", _ => "N.BonesLeafyNewLeaf" }] =
            Opening switch { TransformationOpening.NewLeaf => 1, TransformationOpening.BonesLeafyNewLeaf => 3, _ => 2 };
        if (MorphicGrove) result["E.MORPHIC_GROVE"] = 2;
        if (AromaOfChaos) result["E.AROMA_OF_CHAOS"] = 1;
        if (WhisperingHollow) result["E.WHISPERING_HOLLOW"] = 1;
        if (Symbiote) result["E.SYMBIOTE"] = 1;
        if (TrialNondescript) result["E.TRIAL"] = 2;
        if (UsesAncient) result[AncientSource == TransformationAncientSource.Astrolabe ? "A.ASTROLABE" : "A.PANDORAS_BOX"] = AncientOpportunityCount;
        return result;
    }

    internal TransformationAggregateCondition Normalize()
    {
        if (!Enum.IsDefined(Opening) || !Enum.IsDefined(PickupOrder) || !Enum.IsDefined(Predicate) ||
            !IsBones && PickupOrder != TransformationPickupOrder.Any ||
            !Enum.IsDefined(AncientSource) || OpportunityCount <= 0 || TargetMultiset is null || ResultRequirements is null ||
            (UsesAncient ? AncientAct is not (2 or 3) : AncientAct != 0) || PandoraCountOverride is < 0 || PandoraAutomaticCount < 0 ||
            AncientSource != TransformationAncientSource.PandorasBox && (PandoraCountOverride.HasValue || PandoraAutomaticCount != 0))
            throw new ArgumentException("TransformationAggregate.InvalidContributors");
        if (Opening == TransformationOpening.BonesLeafyOther
            ? CompanionRelic is not { IsValid: true } k || k.Category != "RELIC" || k == BaseGameModelKeys.Relics.LeafyPoultice || k == BaseGameModelKeys.Relics.NeowsBones || k == BaseGameModelKeys.Relics.NewLeaf || PickupOrder is TransformationPickupOrder.LeafyThenNewLeaf or TransformationPickupOrder.NewLeafThenLeafy
            : CompanionRelic.HasValue || PickupOrder is TransformationPickupOrder.LeafyThenCompanion or TransformationPickupOrder.CompanionThenLeafy)
            throw new ArgumentException("TransformationAggregate.InvalidBonesCompanion");
        var counts = SourceCounts();
        var requirements = ResultRequirements.Select(r => {
            if (r is null || r.Sources is null || r.Sources.Count == 0 || r.Sources.Any(s => s is null || !counts.ContainsKey(s)))
                throw new ArgumentException("TransformationAggregate.InvalidRequirementSources");
            var sources = r.Sources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            ValidatePredicate(r.Predicate, r.MinimumRareCount, r.TargetMultiset, sources.Sum(s => (long)counts[s]));
            return r with { Sources = Array.AsReadOnly(sources), TargetMultiset = SortedTargets(r.TargetMultiset) };
        }).ToArray();
        if (requirements.Length == 0) ValidatePredicate(Predicate, MinimumRareCount, TargetMultiset, OpportunityCount);
        if (UsesEvents && !ValidEventPremises() &&
            !EventResultTransformSemantics.IsIncompleteCapture(EventScenario, MorphicGrove || TrialNondescript ? 2 : 1))
            throw new ArgumentException("TransformationAggregate.InitialBasicEventPremisesRequired");
        return this with { TargetMultiset = SortedTargets(TargetMultiset), ResultRequirements = Array.AsReadOnly(requirements),
            EventScenario = UsesEvents ? EventScenario : null };
    }

    private static IReadOnlyList<ModelKey> SortedTargets(IReadOnlyList<ModelKey> targets) =>
        Array.AsReadOnly(targets.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToArray());

    private static void ValidatePredicate(TransformationAggregatePredicate predicate, int rare, IReadOnlyList<ModelKey> targets, long capacity)
    {
        if (!Enum.IsDefined(predicate) || targets is null || targets.Any(k => !k.IsValid || k.Category != "CARD") ||
            (predicate == TransformationAggregatePredicate.RareCountAtLeast ? rare < 1 || rare > capacity || targets.Count != 0 :
                targets.Count < 1 || targets.Count > capacity ||
                (predicate == TransformationAggregatePredicate.ContainsMultisetAndRareCount ? rare < 1 || rare > capacity : rare != 0)))
            throw new ArgumentException("TransformationAggregate.InvalidPredicate");
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
        string.Join(',', TargetMultiset.Select(k => k.Serialized)), EventScenario?.Fingerprint ?? "none") +
        (UsesExtendedRequirements ? "|scoped.v1|" + string.Join('|', AncientSource, AncientAct, PandoraCountOverride, PandoraAutomaticCount,
            System.Text.Json.JsonSerializer.Serialize(ResultRequirements)) : "");

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
    }

    internal static bool HasUnresolvedRewardContinuation(SearchQuery query) =>
        query.TransformationAggregate is { UsesNeow: true } && query.HasCombatRewardConstraints && !HasBoundOpeningContinuation(query);

    internal static bool HasUnresolvedRelicContinuation(SearchQuery query) =>
        query.TransformationAggregate is { UsesNeow: true } && HasRelicSequenceConditions(query) && !HasBoundOpeningContinuation(query);

    internal static bool HasBoundDirectRelicContinuation(SearchQuery query) =>
        query.TransformationAggregate is { Opening: TransformationOpening.LeafyPoultice or TransformationOpening.NewLeaf } &&
        HasRelicSequenceConditions(query) && HasBoundOpeningContinuation(query);

    internal static bool HasFixedBonesRelicContinuation(SearchQuery query) =>
        query.TransformationAggregate is { Opening: TransformationOpening.BonesLeafyNewLeaf } && HasBoundOpeningContinuation(query);

    private static bool HasRelicSequenceConditions(SearchQuery query) =>
        query.RelicSequenceConstraints.Any(c => !c.IsEmpty) || query.RelicShopSequenceConditions.Any(c => !c.IsEmpty);

    private static bool HasBoundOpeningContinuation(SearchQuery query)
    {
        if (query.TransformationAggregate is not { UsesNeow: true } aggregate ||
            query.OpeningRoute?.RouteRelicKey != aggregate.RequiredOpeningKey)
            return false;

        // These Ancient pickups preserve the observed personal relic bags and
        // UpFront. C already replays the versioned Bones offer and actual route;
        // Leafy and New Leaf change no Rewards draws or held reward effects.
        // Conditional event contributors read their own initial-Basic premises
        // and event streams. The aggregation predicate cannot change a pickup.
        return aggregate.Opening switch
        {
            TransformationOpening.LeafyPoultice or TransformationOpening.NewLeaf => true,
            TransformationOpening.BonesLeafyNewLeaf =>
                query.OpeningRouteRelicRequirement is { RequiredRelicKeys.Count: 2 } pair &&
                pair.RequiredRelicKeys.Contains(BaseGameModelKeys.Relics.LeafyPoultice) &&
                pair.RequiredRelicKeys.Contains(BaseGameModelKeys.Relics.NewLeaf),
            _ => false
        };
    }

    internal ModelKey RequiredOpeningKey => Opening switch {
        TransformationOpening.LeafyPoultice => BaseGameModelKeys.Relics.LeafyPoultice,
        TransformationOpening.NewLeaf => BaseGameModelKeys.Relics.NewLeaf,
        _ => BaseGameModelKeys.Relics.NeowsBones };

    internal static bool HasSharedNeow(ExactSearchEvaluationProjection e) =>
        e.TransformationAggregate is { } aggregate && (aggregate.UsesNeow || aggregate.UsesAncient) && e.NeowRoute is not null;

    // Identity/pickup commitment is already necessary for T. More authored N
    // predicates can inspect these same outputs; their joint must not be multiplied.
    internal static bool SharedNeowIdentityOnly(ExactSearchEvaluationProjection e) => HasSharedNeow(e) &&
        (!e.RequireNeowsBones || e.TransformationAggregate!.IsBones) &&
        (e.TransformationAggregate!.IsBones ||
            e.RequiredBonesCombination.Count == 0 && e.RequiredBonesAcquisitionOrder.Count == 0) &&
        e.NeowOfferWhitelist is null && e.NeowRelics.IsEmpty && e.BonesRelics.IsEmpty && !e.RequireSmallCapsule && !e.RequireLargeCapsule &&
        e.CapsuleContainedRelics.IsEmpty && !e.RequireWhetstone && !e.RequireWarPaint &&
        e.RequiredFinalCurse is null && e.BannedFinalCurses.Count == 0 && e.Preset == NeowSearchPreset.None &&
        e.StructuredNeowEffects.Count == 0 && e.EffectOutputConditions.Count == 0;

    private static bool HasLegacyNeow(LegacyNeowSemanticConstraints n) => !n.NeowRelics.IsEmpty ||
        n.RequireNeowsBones || !n.BonesRelics.IsEmpty || n.RequiredBonesCombination.Count != 0 ||
        n.RequireSmallCapsule || n.RequireLargeCapsule || !n.CapsuleContainedRelics.IsEmpty || n.RequireWhetstone ||
        n.RequireWarPaint || n.RequiredFinalCurse.HasValue || n.BannedFinalCurses.Count != 0 ||
        n.Preset != NeowSearchPreset.None || n.RequiredBonesAcquisitionOrder.Count != 0 || n.EffectOutputConditions.Count != 0;
}
