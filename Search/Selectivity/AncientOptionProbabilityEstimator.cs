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
internal static class AncientOptionProbabilityEstimator
{

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
    public static SearchSelectivityEstimate EstimateConditional(
        SearchSelectivityInput plan,
        AncientSearchBranchCondition branch)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!branch.IsValid)
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.InvalidBranch",
                "Ancient option probability requires a valid parent Ancient branch.");

        ModelKey[] targets = branch.OptionAny
            .Where(key => key.IsValid)
            .Distinct(ModelKeyComparer.Instance)
            .ToArray();
        if (targets.Length == 0)
        {
            return SearchSelectivityEstimate.Exact(
                1d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedPredicate,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.AncientOption.NoChildConstraint",
                "The selected Ancient branch has no child option restriction; conditional option acceptance is 1.",
                conditionedOnDomains: new[] { SearchSelectivityDomain.WorldEvent });
        }

        Beta109AncientEventContextSnapshot? context = FindContext(plan, branch);
        Beta109AncientOptionCatalogSnapshot? catalog = context?.Catalog;
        if (context is null || catalog is null ||
            !CatalogUsable(context, catalog) && !plan.Authority.UsesBestEffortModel)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.AuthorityMissing",
                "Exact Ancient option probability requires the immutable runtime event context and exact filtered option catalog.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.AuthorityPoolMembership,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent,
                new[] { "ParentScopedOptionAny=true" });
        }

        string ancient = NormalizeEntry(branch.AncientKey.Entry);
        SearchSelectivityEstimate estimate = ancient switch
        {
            "PAEL" => EstimateSeparateDrawPools(
                BuildPaelPools(plan, catalog), targets, "Probability.Authority.PaelParentScopedAny"),
            "TEZCATARA" => EstimateSeparateDrawPools(
                BuildTezcataraPools(plan, catalog), targets, "Probability.Authority.TezcataraParentScopedAny"),
            "VAKUU" => EstimateSeparateDrawPools(
                new[]
                {
                    catalog.Pool("vakuu.pool1"),
                    catalog.Pool("vakuu.pool2"),
                    catalog.Pool("vakuu.pool3")
                },
                targets,
                "Probability.Authority.VakuuParentScopedAny"),
            "NONUPEIPE" => EstimateShuffleTake(
                BuildNonupeipePool(plan, catalog), 3, targets, "Probability.Authority.NonupeipeParentScopedAny"),
            "TANX" => EstimateShuffleTake(
                BuildTanxPool(plan, catalog), 3, targets, "Probability.Authority.TanxParentScopedAny"),
            "OROBAS" => EstimateOrobas(plan, context, catalog, branch, targets),
            "DARV" => EstimateDarv(plan, catalog, targets),
            _ => SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.GeneratorUnsupported:" + ancient,
                "No exact parent-conditioned probability strategy is registered for this Ancient option generator.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.Unknown,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent)
        };

        if (!estimate.IsPriced || !estimate.Probability.HasValue)
            return estimate;

        return estimate with
        {
            ConditionedOnDomains = new[] { SearchSelectivityDomain.WorldEvent },
            Notes = estimate.Notes + " Result is P(OptionAny | this parent Ancient identity), not an unconditional option marginal."
        };
    }

    private static SearchSelectivityEstimate EstimateSeparateDrawPools(
        IReadOnlyList<IReadOnlyList<ModelKey>> pools,
        IReadOnlyCollection<ModelKey> targets,
        string evidenceCode)
    {
        if (pools.Count == 0 || pools.Any(pool => pool.Count == 0))
        {
            return SearchSelectivityEstimate.Unpriced(
                evidenceCode + ".PoolMissing",
                "One or more exact Ancient option draw pools are empty/missing.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.AuthorityPoolMembership,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        var targetSet = new HashSet<ModelKey>(targets, ModelKeyComparer.Instance);
        double miss = 1d;
        var terms = new List<string>(pools.Count);
        foreach (IReadOnlyList<ModelKey> pool in pools)
        {
            int hitMass = pool.Count(targetSet.Contains);
            double hit = hitMass / (double)pool.Count;
            miss *= 1d - hit;
            terms.Add($"{hitMass}/{pool.Count}");
        }
        double probability = Math.Clamp(1d - miss, 0d, 1d);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.AssumedIndependent,
            evidenceCode,
            "Parent-scoped OptionAny is the union over the generator's separate authority-derived draws; per-draw selected mass=" + string.Join(",", terms) + ".",
            new[]
            {
                "OptionAny is scoped to one parent Ancient branch.",
                "Repeated ModelKey entries in a weighted pool count as physical probability mass.",
                "Separate event-local draws use the current AssumedIndependentUnlessStructuralDependencyRegistered probability policy."
            });
    }

    private static SearchSelectivityEstimate EstimateShuffleTake(
        IReadOnlyList<ModelKey> pool,
        int requestedTake,
        IReadOnlyCollection<ModelKey> targets,
        string evidenceCode)
    {
        if (pool.Count == 0)
        {
            return SearchSelectivityEstimate.Unpriced(
                evidenceCode + ".PoolMissing",
                "The exact Ancient option shuffle pool is empty/missing.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        var targetSet = new HashSet<ModelKey>(targets, ModelKeyComparer.Instance);
        int hitMass = pool.Count(targetSet.Contains);
        int take = Math.Min(requestedTake, pool.Count);
        if (hitMass == 0)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                evidenceCode + ".TargetAbsent",
                "No selected OptionAny identity exists in the immutable eligible shuffle pool.");
        }

        double miss = 1d;
        int nonTarget = pool.Count - hitMass;
        for (int draw = 0; draw < take; draw++)
        {
            if (nonTarget - draw <= 0)
            {
                miss = 0d;
                break;
            }
            miss *= (nonTarget - draw) / (double)(pool.Count - draw);
        }
        double probability = Math.Clamp(1d - miss, 0d, 1d);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.WithoutReplacement,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            evidenceCode,
            $"OptionAny requires at least one selected physical entry among the first {take} entries of a uniform shuffle: pool={pool.Count}; selectedMass={hitMass}.",
            new[]
            {
                "The probability is computed as one minus drawing only non-target physical entries without replacement.",
                "No independent per-slot multiplication is used."
            });
    }

    private static SearchSelectivityEstimate EstimateOrobas(
        SearchSelectivityInput plan,
        Beta109AncientEventContextSnapshot context,
        Beta109AncientOptionCatalogSnapshot catalog,
        AncientSearchBranchCondition branch,
        IReadOnlyCollection<ModelKey> targets)
    {
        IReadOnlyList<ModelKey> firstTrue = catalog.Pool("orobas.pool1.true");
        IReadOnlyList<ModelKey> firstFalse = catalog.Pool("orobas.pool1.false");
        IReadOnlyList<ModelKey> second = catalog.Pool("orobas.pool2");
        var third = new List<ModelKey>();
        if (SemanticAssumptions(plan).OrobasTouchOfOrobasConditionMet)
            third.AddRange(catalog.Pool("orobas.pool3.touch"));
        if (SemanticAssumptions(plan).OrobasArchaicToothConditionMet)
            third.AddRange(catalog.Pool("orobas.pool3.tooth"));
        if (firstTrue.Count == 0 || firstFalse.Count == 0 || second.Count == 0 || third.Count == 0)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.OrobasPoolMissing",
                "Orobas option probability requires non-empty authority pools for both special branches, pool2, and the user-enabled pool3.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        // Production source uses NextFloat() < 0.3333333f. Retain the source
        // threshold as the probability-model mass instead of silently rewriting it.
        double specialMass = (double)0.3333333f;
        var targetSet = new HashSet<ModelKey>(targets, ModelKeyComparer.Instance);
        double p1 = specialMass * HitMass(firstTrue, targetSet) + (1d - specialMass) * HitMass(firstFalse, targetSet);
        double p2 = HitMass(second, targetSet);
        double p3 = HitMass(third, targetSet);
        double optionAny = Math.Clamp(1d - (1d - p1) * (1d - p2) * (1d - p3), 0d, 1d);

        if (branch.SeaGlassTargetAny.Count == 0)
        {
            return SearchSelectivityEstimate.Exact(
                optionAny,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.AssumedIndependent,
                "Probability.Authority.OrobasParentScopedAny",
                $"Orobas OptionAny is the union of the source-audited weighted pool1 branch and the separate pool2/pool3 draws; p1={p1:G17};p2={p2:G17};p3={p3:G17}.",
                new[]
                {
                    "SpecialBranchThreshold=(double)0.3333333f from Production source.",
                    "Pool3 eligibility is supplied by the compiled user AncientOptionConditionProfile.",
                    "Separate event-local draws use AssumedIndependent unless a registered structural relation says otherwise."
                });
        }

        if (!context.UnlockedCharacterSourceOrderExact && !plan.Authority.UsesBestEffortModel)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.OrobasSeaGlassCharacterOrderMissing",
                "Sea Glass target probability requires exact runtime unlocked-character source-order authority.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        ModelKey[] characterCandidates = context.UnlockedCharacters
            .Where(key => key.IsValid &&
                          key.Category == BaseGameModelKeys.Categories.Character &&
                          key != context.CharacterKey)
            .ToArray();
        if (characterCandidates.Length == 0)
        {
            return SearchSelectivityEstimate.Exact(
                0d,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.ExactRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence,
                "Probability.Authority.OrobasSeaGlassNoCharacterCandidate",
                "Sea Glass cannot produce an exact other-character target because the runtime candidate set is empty.");
        }

        var seaTargets = new HashSet<ModelKey>(
            branch.SeaGlassTargetAny.Where(key => key.IsValid),
            ModelKeyComparer.Instance);
        int characterHit = characterCandidates.Count(seaTargets.Contains);
        double characterP = characterHit / (double)characterCandidates.Length;
        double seaGlassP = specialMass * IdentityMass(firstTrue, "SEA_GLASS") +
                           (1d - specialMass) * IdentityMass(firstFalse, "SEA_GLASS");

        // The compiler requires SEA_GLASS in OptionAny whenever a SeaGlass target
        // filter is present. Therefore the target predicate dominates OptionAny:
        // SeaGlass must be the visible first option and its preselected character
        // must belong to SeaGlassTargetAny.
        double probability = Math.Clamp(seaGlassP * characterP, 0d, 1d);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.AssumedIndependent,
            "Probability.Authority.OrobasSeaGlassParentScopedTarget",
            $"Orobas Sea Glass target: P(SeaGlass)={seaGlassP:G17}; runtime other-character target mass={characterHit}/{characterCandidates.Length}.",
            new[]
            {
                "AncientBranchSeaGlassTargetRequiresSeaGlassOption is enforced by the Production compiler.",
                "Character target is selected before the Orobas option branch, but current Probability Design treats these separate draws as AssumedIndependent.",
                "Runtime unlocked-character candidates exclude the current character."
            });
    }

    private static SearchSelectivityEstimate EstimateDarv(
        SearchSelectivityInput plan,
        Beta109AncientOptionCatalogSnapshot catalog,
        IReadOnlyCollection<ModelKey> targets)
    {
        Beta109NamedOptionPoolSnapshot[] validSets = catalog.PoolsWithPrefix("darv.valid.")
            .Where(pool => SemanticAssumptions(plan).DarvAllowPandorasBoxRelicSet ||
                           !string.Equals(pool.PoolId, "darv.valid.pandoras-box", StringComparison.Ordinal))
            .Where(pool => pool.OrderedOptions.Count != 0)
            .ToArray();
        if (validSets.Length == 0)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.DarvValidSetMissing",
                "Darv option probability requires at least one exact runtime valid-set pool.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.WithoutReplacement,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.StructuralDependence);
        }

        IReadOnlyList<ModelKey> dustyPool = catalog.Pool("darv.dusty-tome");
        if (dustyPool.Count != 1)
        {
            return SearchSelectivityEstimate.Unpriced(
                "Probability.AncientOption.DarvDustyTomeCatalogMissing",
                "Darv Dusty Tome branch requires exactly one authority catalog identity.",
                SearchSelectivityConfidence.Low,
                SearchSelectivityMethod.ConditionalChain,
                SearchSelectivityCoverage.PartialRequestedConjunction,
                SearchSelectivityDependencyClass.RouteDependent);
        }

        var targetSet = new HashSet<ModelKey>(targets, ModelKeyComparer.Instance);
        double[] hitProbabilities = validSets
            .Select(pool => pool.OrderedOptions.Count(targetSet.Contains) / (double)pool.OrderedOptions.Count)
            .ToArray();

        // H = number of selected valid-set representatives that match OptionAny.
        // The selected representatives are then uniformly shuffled; only H and the
        // number of valid sets are needed to price whether the first k contains a hit.
        double[] hMass = new double[validSets.Length + 1];
        hMass[0] = 1d;
        int processed = 0;
        foreach (double hit in hitProbabilities)
        {
            var next = new double[hMass.Length];
            for (int h = 0; h <= processed; h++)
            {
                next[h] += hMass[h] * (1d - hit);
                next[h + 1] += hMass[h] * hit;
            }
            hMass = next;
            processed++;
        }

        double SourceTakeHit(int requestedTake)
        {
            int take = Math.Min(requestedTake, validSets.Length);
            if (take <= 0) return 0d;
            double miss = 0d;
            for (int h = 0; h < hMass.Length; h++)
            {
                int nonTarget = validSets.Length - h;
                double conditionalMiss = nonTarget < take
                    ? 0d
                    : CombinationRatio(nonTarget, validSets.Length, take);
                miss += hMass[h] * conditionalMiss;
            }
            return Math.Clamp(1d - miss, 0d, 1d);
        }

        double dustyBranch = targetSet.Contains(dustyPool[0]) ? 1d : SourceTakeHit(2);
        double ordinaryBranch = SourceTakeHit(3);
        double probability = Math.Clamp(0.5d * dustyBranch + 0.5d * ordinaryBranch, 0d, 1d);
        return SearchSelectivityEstimate.Exact(
            probability,
            SearchSelectivityMethod.ConditionalChain,
            SearchSelectivityCoverage.ExactRequestedConjunction,
            SearchSelectivityDependencyClass.StructuralDependence,
            "Probability.Authority.DarvActScopedOptionAny",
            $"Darv Act-scoped valid-set representatives are selected, uniformly shuffled, then DustyTome branch takes 2+Dusty or ordinary branch takes 3; validSets={validSets.Length};P={probability:G17}.",
            new[]
            {
                "Darv valid-set catalog is taken from the selected Act's immutable Ancient event context.",
                "Pandora's Box set eligibility follows AncientOptionConditionProfile.",
                "Dusty Tome branch probability is 1/2 from Production NextBool.",
                "Dusty Tome mutable reward projection is not needed to price visible option identity."
            });
    }

    private static double HitMass(IReadOnlyList<ModelKey> pool, HashSet<ModelKey> targets) =>
        pool.Count(targets.Contains) / (double)pool.Count;

    private static double IdentityMass(IReadOnlyList<ModelKey> pool, string entry) =>
        pool.Count(key => string.Equals(key.Entry, entry, StringComparison.Ordinal)) / (double)pool.Count;

    private static double CombinationRatio(int numeratorN, int denominatorN, int k)
    {
        if (k <= 0) return 1d;
        if (numeratorN < k || denominatorN < k) return 0d;
        double ratio = 1d;
        for (int i = 0; i < k; i++)
            ratio *= (numeratorN - i) / (double)(denominatorN - i);
        return ratio;
    }

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
            (context.PlayerSlot == plan.Authority.PlayerSlotIndex || context.IsShared));
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
