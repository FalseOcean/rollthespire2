using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Probability-consumer projection of the Compiled Search semantic spine.
/// This is not a second Query model: every field is either the compiled semantic
/// authority itself or a compatibility execution projection produced from the
/// normalized SearchQuery for reuse by the existing numerical estimators.
/// </summary>
internal sealed record ProbabilitySemanticView(
    CompiledSearch Semantics,
    SearchQuery NormalizedQuery,
    SearchContext Context,
    ResolvedRouteSemantics Routes,
    IReadOnlyList<SemanticRelation> Relations,
    IReadOnlyList<ProbabilitySemanticRelationProjection> DependencyProjections,
    NeowSearchFilter NumericalFilter,
    string SemanticFingerprint,
    string SemanticSource,
    IReadOnlyList<string> NormalizationBlocks,
    IReadOnlyList<string> DerivedDiagnostics)
{
    public QueryNormalizationStatus NormalizationStatus => Semantics.Normalization.Status;
    public ProjectionFidelity NumericalProjectionFidelity { get; init; }
    public bool NumericalProjectionUsable =>
        NumericalProjectionFidelity is ProjectionFidelity.Exact or ProjectionFidelity.EquivalentUnderCurrentRuntime;

    public bool HasRelation(SemanticRelationKind kind) => Relations.Any(relation => relation.Kind == kind);

    public bool HasRelation(
        SemanticRelationKind kind,
        SemanticFactKind sourceKind,
        SemanticFactKind targetKind) => Relations.Any(relation =>
            relation.Kind == kind &&
            relation.Source.Kind == sourceKind &&
            relation.Target.Kind == targetKind);

    public bool HasDependencyProjection(ProbabilitySemanticDependencyKind kind) =>
        DependencyProjections.Any(projection => projection.Kind == kind);

    public IReadOnlyList<SemanticRelation> RelationsOfKind(SemanticRelationKind kind) =>
        Relations.Where(relation => relation.Kind == kind).ToArray();

    public IReadOnlyList<SearchSelectivityDomain> ActiveDomains()
    {
        // NumericalFilter is not the original Production execution Filter. It is
        // the exact/equivalent legacy projection emitted from NormalizedQuery and
        // therefore preserves existing numerical solver inputs while CompiledSearch
        // semantics remains the logical Query identity.
        var output = new List<SearchSelectivityDomain>(4);
        if (HasNeowQuery()) output.Add(SearchSelectivityDomain.Neow);
        if (NormalizedQuery.RelicSequenceConstraints.Any(condition => !condition.IsEmpty) ||
            NormalizedQuery.RelicShopSequenceConditions.Any(condition => !condition.IsEmpty)) output.Add(SearchSelectivityDomain.Relic);
        if (HasWorldQuery()) output.Add(SearchSelectivityDomain.WorldEvent);
        if (NormalizedQuery.HasCombatRewardConstraints) output.Add(SearchSelectivityDomain.CombatReward);
        return output;
    }

    private bool HasNeowQuery()
    {
        LegacyNeowSemanticConstraints legacy = NormalizedQuery.LegacyNeow;
        return NormalizedQuery.OpeningRoute is { IsValid: true } ||
               NormalizedQuery.OpeningRouteRelicRequirement is { IsEmpty: false } ||
               NormalizedQuery.StructuredOpeningEffects.Any(condition => !condition.IsEmpty) ||
               !legacy.NeowRelics.IsEmpty ||
               legacy.RequireNeowsBones ||
               !legacy.BonesRelics.IsEmpty ||
               legacy.RequiredBonesCombination.Count != 0 ||
               legacy.RequiredBonesAcquisitionOrder.Count != 0 ||
               legacy.RequireSmallCapsule ||
               legacy.RequireLargeCapsule ||
               !legacy.CapsuleContainedRelics.IsEmpty ||
               legacy.RequireWhetstone ||
               legacy.RequireWarPaint ||
               legacy.RequiredFinalCurse.HasValue ||
               legacy.BannedFinalCurses.Count != 0 ||
               legacy.EffectOutputConditions.Any(condition => !condition.IsEmpty) ||
               legacy.Preset != NeowSearchPreset.None;
    }

    private bool HasWorldQuery() =>
        NormalizedQuery.VariantBossBranches.Any(branch => branch.IsValid) ||
        NormalizedQuery.AncientBranches.Any(branch => branch.IsValid) ||
        NormalizedQuery.EventSequenceConstraints.Any(condition => !condition.IsEmpty) ||
        NormalizedQuery.LegacyWorld.BossFilters.Any(filter => !filter.IsEmpty) ||
        NormalizedQuery.LegacyWorld.BossOrdinalFilters.Any(filter => !filter.IsEmpty) ||
        NormalizedQuery.LegacyWorld.AncientIdentityFilters.Any(filter => !filter.IsEmpty) ||
        NormalizedQuery.LegacyWorld.AncientOptionFilters.Any(filter => !filter.IsEmpty) ||
        NormalizedQuery.LegacyWorld.AncientSeaGlassTargetFilters.Any(filter => !filter.IsEmpty);
}

internal enum ProbabilitySemanticDependencyKind : byte
{
    SharedConstraint = 1,
    Conditional = 2,
    SharedParentMixture = 3,
    SharedStateMixture = 4,
    StructuralImplication = 5,
    StructuralConflict = 6,
    RouteConditional = 7
}

internal sealed record ProbabilitySemanticRelationProjection(
    SemanticRelation Relation,
    ProbabilitySemanticDependencyKind Kind,
    SearchSelectivityDependencyClass DependencyClass,
    string ProjectionReason);

internal static class ProbabilitySemanticProjection
{
    public static ProbabilitySemanticView From(SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        CompiledSearch semantics = plan.CompiledSearch;
        ProbabilitySemanticRelationProjection[] projections = semantics.Relations
            .Select(ProjectRelation)
            .Where(projection => projection is not null)
            .Cast<ProbabilitySemanticRelationProjection>()
            .ToArray();

        string[] blocks = BuildNormalizationBlocks(projections);
        string[] diagnostics = BuildDerivedDiagnostics(plan, semantics, projections);

        return new ProbabilitySemanticView(
            semantics,
            semantics.NormalizedQuery,
            semantics.Context,
            semantics.ResolvedRouteSemantics,
            semantics.Relations,
            projections,
            plan.Filter,
            semantics.SemanticFingerprint,
            "CompiledSearch",
            blocks,
            diagnostics)
        {
            NumericalProjectionFidelity = plan.ProjectionFidelity
        };
    }

    private static ProbabilitySemanticRelationProjection? ProjectRelation(SemanticRelation relation)
    {
        return relation.Kind switch
        {
            SemanticRelationKind.SameFact => new(
                relation,
                ProbabilitySemanticDependencyKind.SharedConstraint,
                SearchSelectivityDependencyClass.StructuralDependence,
                "SameFact is normalized as one shared probability constraint; numerical pricing remains Probability-owned."),
            SemanticRelationKind.SequenceMembership => new(
                relation,
                ProbabilitySemanticDependencyKind.SharedConstraint,
                SearchSelectivityDependencyClass.StructuralDependence,
                "SequenceMembership selects finite-sequence/without-replacement normalization where required."),
            SemanticRelationKind.ParentScoped => new(
                relation,
                ProbabilitySemanticDependencyKind.Conditional,
                SearchSelectivityDependencyClass.StructuralDependence,
                "ParentScoped projects to parent-conditioned probability pricing."),
            SemanticRelationKind.SharedParent => new(
                relation,
                ProbabilitySemanticDependencyKind.SharedParentMixture,
                SearchSelectivityDependencyClass.StructuralDependence,
                "SharedParent projects to a shared-parent finite mixture; the parent prior is paid once."),
            SemanticRelationKind.SharedState => new(
                relation,
                ProbabilitySemanticDependencyKind.SharedStateMixture,
                SearchSelectivityDependencyClass.StructuralDependence,
                "SharedState projects to one finite latent-state calculation rather than independent marginals."),
            SemanticRelationKind.StateInfluence => new(
                relation,
                ProbabilitySemanticDependencyKind.Conditional,
                SearchSelectivityDependencyClass.SharedContinuation,
                "StateInfluence projects to conditional pricing; the downstream numerical model remains domain-owned."),
            SemanticRelationKind.RouteScoped => new(
                relation,
                ProbabilitySemanticDependencyKind.RouteConditional,
                SearchSelectivityDependencyClass.RouteDependent,
                "RouteScoped projects to pricing conditioned on ResolvedRouteSemantics."),
            SemanticRelationKind.Implies => new(
                relation,
                ProbabilitySemanticDependencyKind.StructuralImplication,
                SearchSelectivityDependencyClass.StructuralDependence,
                "SearchCompiler normalization owns the logical implication; Probability prices the normalized block."),
            SemanticRelationKind.Conflicts => new(
                relation,
                ProbabilitySemanticDependencyKind.StructuralConflict,
                SearchSelectivityDependencyClass.StructuralDependence,
                "SearchCompiler conflict makes the normalized query semantically impossible."),
            _ => null
        };
    }

    private static string[] BuildNormalizationBlocks(
        IReadOnlyList<ProbabilitySemanticRelationProjection> projections)
    {
        var blocks = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProbabilitySemanticRelationProjection projection in projections)
        {
            SemanticRelation relation = projection.Relation;
            if (projection.Kind == ProbabilitySemanticDependencyKind.SharedConstraint &&
                ((relation.Source.Kind == SemanticFactKind.CapsuleNestedRelic && relation.Target.Kind == SemanticFactKind.RelicGrabBag) ||
                 (relation.Target.Kind == SemanticFactKind.CapsuleNestedRelic && relation.Source.Kind == SemanticFactKind.RelicGrabBag)))
            {
                blocks.Add(
                    relation.Source.FactId.Contains(".CUR", StringComparison.Ordinal) ||
                    relation.Target.FactId.Contains(".CUR", StringComparison.Ordinal)
                        ? "SharedRelicGrabBag.CUR"
                        : "SharedRelicGrabBag");
            }
            if (projection.Kind == ProbabilitySemanticDependencyKind.SharedParentMixture &&
                (relation.Source.Kind == SemanticFactKind.ActVariant || relation.Source.Kind == SemanticFactKind.LegacyFlatBossConstraint))
            {
                blocks.Add("WorldVariantSharedParent");
            }
            if (projection.Kind == ProbabilitySemanticDependencyKind.Conditional &&
                relation.Source.Kind == SemanticFactKind.AncientIdentity &&
                relation.Target.Kind == SemanticFactKind.AncientOption)
            {
                blocks.Add("AncientParentChild");
            }
            if (projection.Kind == ProbabilitySemanticDependencyKind.SharedStateMixture)
                blocks.Add("AncientSharedAssignment");
            if (projection.Kind == ProbabilitySemanticDependencyKind.RouteConditional &&
                relation.Source.Kind == SemanticFactKind.CombatReward)
                blocks.Add("CombatRewardRoute");
            if (relation.Kind == SemanticRelationKind.StateInfluence && relation.Target.Kind == SemanticFactKind.CombatReward)
                blocks.Add("CombatRewardInfluence");
        }
        return blocks.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string[] BuildDerivedDiagnostics(
        SearchSelectivityInput input,
        CompiledSearch semantics,
        IReadOnlyList<ProbabilitySemanticRelationProjection> projections)
    {
        var diagnostics = new List<string>
        {
            "ProbabilitySemanticSource=CompiledSearch",
            "SemanticFingerprint=" + semantics.SemanticFingerprint,
            "NumericalCompatibilityProjectionFidelity=" + input.ProjectionFidelity,
            "NumericalCompatibilityProjectionUsable=" +
                (input.ProjectionFidelity is ProjectionFidelity.Exact or ProjectionFidelity.EquivalentUnderCurrentRuntime),
            "IndependenceProvenance=ProbabilityOwned;Default=AssumedIndependentUnlessStructuralDependencyRegistered"
        };

        foreach (ProbabilitySemanticRelationProjection projection in projections)
        {
            diagnostics.Add(
                "SemanticRelation=" + projection.Relation.Kind +
                ";ProbabilityProjection=" + projection.Kind +
                ";DependencyClass=" + projection.DependencyClass +
                ";Source=" + projection.Relation.Source.FactId +
                ";Target=" + projection.Relation.Target.FactId);
        }

        return diagnostics.ToArray();
    }
}
