using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// One N physical invocation covers the pure opening aggregate as well as N.
// Query ownership and Exact's source-free multiset validation remain unchanged.
internal sealed class NeowTransformationComposite
{
    internal const string Revision = "N.Neow.JointLeafyNewLeaf.TransformFirst.Carry8.CanonicalAbi1Ready.20261001.v1";
    internal TransformationAggregateNumericalPlan Numerical { get; }
    internal TransformationAggregateProbability Probability { get; }
    internal NeowFamilyGpuPlan Gpu { get; }
    private NeowTransformationComposite(TransformationAggregateNumericalPlan numerical, NeowFamilyGpuPlan gpu)
    { Numerical = numerical; Probability = TransformationAggregateProbability.Build(numerical); Gpu = gpu; }

    internal static bool TryCreate(ExactSearchExecutionRequest request, NeowReplayPlan neow,
        out NeowTransformationComposite? composite)
    {
        composite = null;
        var e = request.Evaluation;
        if (request.Authority.PlayersCount != 1 || request.Authority.PlayerSlotIndex != 0 ||
            request.ProfileId != Compatibility.RuntimeProfileId.Beta111 || neow.ExactOnly.Length != 0 ||
            e.NeowRoute?.RouteRelicKey != BaseGameModelKeys.Relics.NeowsBones ||
            e.RequiredBonesCombination.Count != 2 ||
            !e.RequiredBonesCombination.Contains(BaseGameModelKeys.Relics.LeafyPoultice) ||
            !e.RequiredBonesCombination.Contains(BaseGameModelKeys.Relics.NewLeaf) ||
            e.TransformationAggregate is not { Opening: TransformationOpening.BonesLeafyNewLeaf, UsesEvents: false } c ||
            c.OpportunityCount != 3 || c.PickupOrder != TransformationPickupOrder.Any ||
            e.StructuredNeowEffects.Any(x => !x.IsEmpty && x.Scope != NeowStructuredEffectScope.FinalCurse) ||
            !NeowFamilyGpuPlan.TryCreate(neow, out var gpu, out _) || gpu is null)
            return false;
        var numerical = new TransformationAggregateNumericalPlan(request);
        if (!numerical.Closed || numerical.DrawGroups.Length != 2) return false;
        // The same immutable pools/target-instance masks used by the aggregate
        // donor are appended to N's buffers. No RNG or observation payload leaves N.
        var meta = gpu.Meta.ToList(); var data = gpu.Cards.ToList();
        int offset = meta.Count;
        meta.AddRange([(uint)c.Predicate, (uint)c.TargetMultiset.Count, (uint)c.MinimumRareCount, 3u]);
        foreach (var group in numerical.DrawGroups)
        {
            if (group.Prefix != 0 || group.Pools.Length is < 1 or > 2) return false;
            meta.AddRange([(uint)group.Hash, (uint)(group.Hash >> 32), (uint)group.Pools.Length]);
            foreach (var pool in group.Pools)
            {
                meta.Add((uint)data.Count); meta.Add((uint)pool.Length);
                data.AddRange(pool.Select(id => c.Predicate == TransformationAggregatePredicate.RareCountAtLeast
                    ? (numerical.IsRare(id) ? 1u : 0u)
                    : numerical.TargetBits(id) | (c.RequiresRareRemainder && numerical.IsRare(id) ? 0x80000000u : 0u)));
            }
            for (int i = group.Pools.Length; i < 2; i++) meta.AddRange([0u, 0u]);
        }
        composite = new(numerical, gpu with { Meta = meta.ToArray(), Cards = data.ToArray(), JointTransformMetaOffset = offset });
        return true;
    }

    internal static FamilyExecutionPlan SelectPriced(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, FamilyExecutionPlan baseline)
    {
        if (Environment.GetEnvironmentVariable("RT2_N_TRANSFORM_AGGREGATE_EXPERIMENT") == "baseline" ||
            registered.Count is < 2 or > 7 || registered.OfType<NeowFamily>().SingleOrDefault() is not { } owner ||
            !registered.Any(f => f is TransformationAggregateFamily)) return baseline;
        var neow = owner.PricingReplayPlan;
        if (!TryCreate(request, neow, out var composite) || composite is null)
            return baseline;
        IFamilyInvocation[] fused = [new NeowFamily(request, neow, transformations: composite),
            ..registered.Where(f => f is not NeowFamily and not TransformationAggregateFamily)];
        // Direct Exact or another selected allocation may have different coverage.
        if (!fused.SelectMany(f => f.Coverage).ToHashSet(StringComparer.Ordinal)
            .SetEquals(baseline.Stages.SelectMany(s => s.Coverage))) return baseline;
        var query = Search.Selectivity.JointSelectivityEstimator.EstimateQuery(Search.Selectivity.SearchSelectivityInput.From(request));
        double roots = query.JointlyPriced && query.Probability is > 0 and <= 1
            ? Math.Min(request.ScanCount, Math.Max(1, request.TargetMatchCount) / query.Probability.Value) : request.ScanCount;
        var calibration = GpuCostCalibration.Capture();
        FamilyPhysicalQuote? Quote(IFamilyInvocation f, FamilyPhysicalQuoteRequest g) =>
            f.QuotePhysicalWork(g) is { } q ? calibration.Local(f, q, g) : null;
        static double? Survival(IFamilyInvocation f, IReadOnlySet<string> passed) => f.ResolveSurvival(passed).SurvivalProbability;
        var basePublic = AutomaticPrivateSerialPricing.Price(roots, baseline.OrderedFamilies.ToArray(), false, Quote, Survival);
        double? baseMs = baseline.EstimateCanonicalMilliseconds?.Invoke(roots) ?? basePublic?.CanonicalMs;
        double? baseSetup = baseline.EstimatedSetupMilliseconds ?? basePublic?.SetupMs;
        double? baseRate = baseline.EstimatedTerminalSurvival ?? basePublic?.TerminalRate;
        if (baseMs is null || baseSetup is null || baseRate is null) return baseline;
        var exact = Search.Predictability.SearchPredictabilityVerificationStore.TryGetExactTiming(FamilyExactTimingDomains.Resolve(request));
        double Wall(double canonical) => exact is { Usable: true }
            ? Math.Max(canonical, roots * baseRate.Value * exact.AverageExactMsPerAttempt / Math.Max(1, request.WorkerCount)) : canonical;
        var choices = AutomaticPrivateSerialPricing.Orders(fused)
            .Select(order => AutomaticPrivateSerialPricing.Price(roots, order, false, Quote, Survival))
            .Where(p => p is not null && p.SetupMs.HasValue && Math.Abs(p.TerminalRate - baseRate.Value) <= Math.Max(1e-15, baseRate.Value * 1e-8))
            .Select(p => p!).OrderBy(p => Wall(p.CanonicalMs) + p.SetupMs).ThenBy(p => p.CanonicalMs).ToArray();
        if (choices.Length == 0) return baseline;
        var best = choices[0];
        // Identical Exact tail cancels. Include differential cold setup and a 10%
        // numerical uncertainty margin; unknown never promotes a physical by fiat.
        bool promote = Wall(best.CanonicalMs * 1.1) + best.SetupMs < Wall(baseMs.Value) + baseSetup;
        RuntimeLog.TryBackgroundInfo($"nJointTransformComparison=true;promote={promote};roots={roots};baselineMs={baseMs};baselineSetupMs={baseSetup};jointMs={best.CanonicalMs};jointSetupMs={best.SetupMs};terminalRate={baseRate};NTOrderPreservedByCost=true");
        if (!promote) return baseline;
        return FamilyPlanner.InOrder(best.Order) with {
            SelectionPolicyId = "FamilyPlanner.NT.EquivalentCoverage.20261001.v1",
            RankingAuthority = FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            ExpectedFamilyPipelineMsPerRoot = null,
            EstimatedTerminalSurvival = best.TerminalRate, EstimatedSetupMilliseconds = best.SetupMs,
            EstimateCanonicalMilliseconds = n => AutomaticPrivateSerialPricing.Price(n, best.Order, false, Quote, Survival)?.CanonicalMs,
            BoundedPricingOrderCount = choices.Length + baseline.BoundedPricingOrderCount,
            DecisionEvidence = ["NAndTEqualCandidates;OrdinaryOrdersAndCpuAlternativesRetained;SameTerminalMass;DifferentialSetupIncluded", ..best.Evidence],
            CompleteQuoteEvidence = "NT.OwnedWork.20261001.v1;RuntimeEdges;NoForcedOrder"
        };
    }
}
