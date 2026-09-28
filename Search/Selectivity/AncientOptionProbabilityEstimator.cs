using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Parent-conditioned Ancient option probability for the current Search UI's
/// row-scoped OptionAny semantics. This service consumes only immutable Runtime
/// Authority/catalog DTOs plus the compiled user-declared eligibility profile.
/// It never predicts Ancient identity and never accesses live game state.
/// </summary>
internal static partial class AncientOptionProbabilityEstimator
{
    // Public marginals and All queries use the same source offer distribution
    // as arbitrary legacy/modern conjunctions; targets are never independent.
    internal static SearchSelectivityEstimate EstimateConditionalAll(SearchSelectivityInput plan,
        AncientSearchBranchCondition branch, IReadOnlyList<ModelKey> required) =>
        EstimateConditionalConjunction(plan, branch.Act, branch.AncientKey, [branch],
            required.Count == 0 ? [] : [new ModelKeySetFilter([], required, [])], []);
    /// <summary>
    /// Physical-only survival authority for the disposable Ancient Option necessary PreGate.
    /// This deliberately excludes Ancient Identity probability: P3-A replays the targeted
    /// event-local option RNG directly from LogicalOrdinal/root hash plus immutable context.
    /// Different Ancient event-local contexts are combined by the frozen product policy and
    /// are diagnosed as AssumedIndependent; no source-proven independence claim is made.
    /// </summary>
    public static SearchSelectivityEstimate EstimateNecessaryPreGatePhysicalSurvival(
        SearchSelectivityInput plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        SearchQuery? query = plan.CompiledSearch?.NormalizedQuery;
        AncientSearchBranchCondition[] branches = (query?.AncientBranches ?? Array.Empty<AncientSearchBranchCondition>())
            .Where(branch => branch.IsValid && (branch.OptionAny.Count != 0 || branch.SeaGlassTargetAny.Count != 0))
            .OrderBy(branch => branch.Act)
            .ThenBy(branch => branch.AncientKey.Serialized, StringComparer.Ordinal)
            .ToArray();
        if (branches.Length == 0)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOptionPreGate.NoTargetedOptionPredicate",
                "Ancient Option PreGate physical survival requires at least one targeted branch-local option predicate.");
        }
        if (branches.Any(branch => branch.SeaGlassTargetAny.Count != 0))
        {
            // SeaGlass target identity is a distinct Orobas projection with additional target
            // authority. Do not silently price it as option-only 1.0 or multiply an unmodeled
            // joint predicate. This batch intentionally adds analytic authority only for the
            // branch-local Ancient Option necessary predicate.
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOptionPreGate.SeaGlassJointPhysicalModelUnavailable",
                "Option-only analytic PreGate survival does not yet model SeaGlass target predicates; keep PhysicalGateSurvival Unknown rather than fabricate a value.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.AuthorityPoolMembership,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.UnknownDependence,
                new[] { "AncientOptionOnlyAuthority=true", "SeaGlassPhysicalPredicateExcluded=true" });
        }

        double product = 1d;
        var evidence = new List<string>(branches.Length);
        var assumptions = new List<string>
        {
            "Ancient Identity occurrence probability is intentionally excluded from PreGate physical survival.",
            "Each targeted option predicate is replayed against its own immutable event-local RNG context.",
            "Cross-Ancient event-local contexts use the frozen product policy DependencyClass=AssumedIndependent; this is a product-model choice, not a source-proven independence claim."
        };
        foreach (AncientSearchBranchCondition branch in branches)
        {
            SearchSelectivityEstimate branchEstimate = EstimateConditional(plan, branch);
            if (!branchEstimate.IsPriced || !branchEstimate.Probability.HasValue)
            {
                return SearchSelectivityEstimate.Unpriced(
                    "Probability.AncientOptionPreGate.BranchUnpriced:" + branch.Act + ":" + branch.AncientKey.Entry,
                    "A targeted Ancient Option necessary predicate lacks exact option-only probability authority: " + branchEstimate.EvidenceCode,
                    branchEstimate.Confidence,
                    branchEstimate.Method,
                    SearchSelectivityCoverage.PartialRequestedConjunction,
                    SearchSelectivityDependencyClass.AssumedIndependent,
                    assumptions.Concat(branchEstimate.Assumptions).ToArray());
            }

            double p = Math.Clamp(branchEstimate.Probability.Value, 0d, 1d);
            product *= p;
            evidence.Add($"Act{branch.Act}:{branch.AncientKey.Entry}={p:G17}[{branchEstimate.EvidenceCode}]");
        }

        return SearchSelectivityEstimate.Exact(
            Math.Clamp(product, 0d, 1d),
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            branches.Length > 1 ? SearchSelectivityDependencyClass.AssumedIndependent :
                SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.AnalyticPhysical.AncientOptionNecessaryPreGate",
            "Option-only physical PreGate survival; branch product=" + string.Join("*", evidence) + ".",
            assumptions,
            independentOfDomains: new[]
            {
                SearchSelectivityDomain.Neow,
                SearchSelectivityDomain.Relic,
                SearchSelectivityDomain.WorldEvent
            });
    }
    public static SearchSelectivityEstimate EstimateConditional(SearchSelectivityInput plan,
        AncientSearchBranchCondition branch) =>
        EstimateConditionalConjunction(plan, branch.Act, branch.AncientKey, [branch], [], []);
    private static IReadOnlyList<IReadOnlyList<ModelKey>> BuildPaelPools(
        SearchSelectivityInput plan,
        Beta109AncientOptionCatalogSnapshot catalog)
    {
        var pool2 = catalog.Pool("pael.pool2.base").ToList();
        if (SemanticAssumptions(plan).PaelGoopyDefendCardsAtLeast3)
            pool2.AddRange(catalog.Pool("pael.pool2.goopy"));
        if (SemanticAssumptions(plan).PaelRemovableCardsAtLeast5)
            pool2.AddRange(catalog.Pool("pael.pool2.removable"));
        pool2.AddRange(pool2.ToArray());
        pool2.AddRange(catalog.Pool("pael.pool2.growth"));

        var pool3 = catalog.Pool("pael.pool3.base").ToList();
        if (SemanticAssumptions(plan).PaelAllowLegionNoEventPet)
            pool3.AddRange(catalog.Pool("pael.pool3.no-event-pet"));

        return new IReadOnlyList<ModelKey>[]
        {
            catalog.Pool("pael.pool1"),
            pool2,
            pool3
        };
    }

    private static IReadOnlyList<IReadOnlyList<ModelKey>> BuildTezcataraPools(
        SearchSelectivityInput plan,
        Beta109AncientOptionCatalogSnapshot catalog)
    {
        var pool1 = catalog.Pool("tezcatara.pool1.base").ToList();
        if (SemanticAssumptions(plan).TezcataraHasBasicStrike)
            pool1.AddRange(catalog.Pool("tezcatara.pool1.basic-strike"));
        return new IReadOnlyList<ModelKey>[]
        {
            pool1,
            catalog.Pool("tezcatara.pool2"),
            catalog.Pool("tezcatara.pool3")
        };
    }

    private static IReadOnlyList<ModelKey> BuildNonupeipePool(
        SearchSelectivityInput plan,
        Beta109AncientOptionCatalogSnapshot catalog)
    {
        var pool = catalog.Pool("nonupeipe.pool.base").ToList();
        if (SemanticAssumptions(plan).NonupeipeSwiftEnchantableAtLeast4)
            pool.AddRange(catalog.Pool("nonupeipe.pool.swift"));
        return pool;
    }

    private static IReadOnlyList<ModelKey> BuildTanxPool(
        SearchSelectivityInput plan,
        Beta109AncientOptionCatalogSnapshot catalog)
    {
        var pool = catalog.Pool("tanx.pool.base").ToList();
        if (SemanticAssumptions(plan).TanxInstinctEnchantableAtLeast3)
            pool.AddRange(catalog.Pool("tanx.pool.instinct"));
        return pool;
    }

    private static Beta109AncientEventContextSnapshot? FindContext(
        SearchSelectivityInput plan,
        AncientSearchBranchCondition branch)
    {
        Beta109WorldGenerationSnapshot? generation = plan.Authority.WorldAuthority?.Beta109Generation;
        return generation?.AncientEventContexts.FirstOrDefault(context =>
            context.Act == branch.Act &&
            context.AncientKey == branch.AncientKey &&
            context.PlayerSlot == plan.Authority.PlayerSlotIndex) ?? generation?.AncientEventContexts.FirstOrDefault(context =>
            context.Act == branch.Act && context.AncientKey == branch.AncientKey && context.IsShared);
    }

    private static bool CatalogUsable(
        Beta109AncientEventContextSnapshot context,
        Beta109AncientOptionCatalogSnapshot catalog) =>
        context.EventContextExact &&
        context.ModifierFactsExact &&
        catalog.CatalogExact &&
        catalog.Pools.All(pool =>
            pool.SourceOrdinal >= 0 &&
            pool.OrderExact &&
            pool.FilterResultExact);

    private static AncientOptionConditionProfile SemanticAssumptions(SearchSelectivityInput plan) =>
        ProbabilitySemanticProjection.From(plan)
            .Context
            .EvaluationAssumptions
            .AncientEligibilityAssumptions;

    private static string NormalizeEntry(string entry) =>
        new(entry.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
