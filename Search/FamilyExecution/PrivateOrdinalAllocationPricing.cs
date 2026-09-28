using System.Globalization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Bounded registration and offline comparative pricing, not an execution scheduler.
// Evidence/units: docs/source_audits/Y4_PRIVATE_ORDINAL_PRICING_20260908.md.
// These are numerical-work estimates, never canonical maximum-throughput references.
internal static class PrivateOrdinalAllocationPricing
{
    private const string Policy = "FamilyPlanner.PrivateOrdinal.WorkShape.20260910.v3";
    // Setup/jit/session variation is shared uncertainty, NOT a private setup surcharge.
    internal const double DecisionUncertaintyMs = 250;
    internal readonly record struct Work(double DenseNs, double CompactNs, int Capacity,
        double PublicPayloadFixedMs = .15, double PublicSurvivorNs = 30);
    internal readonly record struct Range(double Lower, double Upper);

    // Family windows restart at each ABI batch. A downstream population cannot
    // borrow spare capacity from another batch, and one producer's internal
    // window count must not become another Family's dispatch floor.
    internal static double Windows(double roots, double inputFraction, int capacity,
        int rootWindow = PrivateOrdinalBuffer.Capacity)
    {
        if (roots <= 0 || inputFraction <= 0) return 0;
        double full = Math.Floor(roots / rootWindow);
        double tail = roots - full * rootWindow;
        return full * BatchWindows(rootWindow, inputFraction, capacity) +
            BatchWindows(tail, inputFraction, capacity);
    }

    // Modeled occupancy, never same-run population. For a one-window batch this
    // is exactly P(X>0), X~Binomial(n,p). Window splitting pays E[ceil(X/cap)].
    // Large split populations use a continuity-corrected normal tail; the first
    // (empty/nonempty) boundary remains exact even for extremely rare stages.
    private static double BatchWindows(double n, double p, int cap)
    {
        if (n <= 0 || p <= 0) return 0;
        if (p >= 1) return Math.Ceiling(n / cap);
        if (cap == 1) return n * p;
        double result = Nonempty(n, p);
        if (cap >= n) return result;
        double mean = n * p, sd = Math.Sqrt(mean * (1 - p));
        int first = Math.Max(1, (int)Math.Floor((mean - 9 * sd) / cap));
        int last = (int)Math.Min(Math.Floor((n - 1) / cap), Math.Ceiling((mean + 9 * sd) / cap));
        result += first - 1;
        for (int k = first; k <= last; k++)
        {
            double z = (k * (double)cap + .5 - mean) / sd;
            // Standard normal upper tail (absolute error below 8e-8).
            double t = 1 / (1 + .2316419 * Math.Abs(z));
            double tail = .3989422804014327 * Math.Exp(-z * z / 2) * t *
                (.319381530 + t * (-.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
            result += z >= 0 ? tail : 1 - tail;
        }
        return result;
    }

    internal static double Nonempty(double n, double p)
    {
        if (n <= 0 || p <= 0) return 0;
        if (p >= 1) return 1;
        double log = p < 1e-5 ? -p * (1 + p * (.5 + p / 3)) : Math.Log(1 - p);
        double x = -n * log;
        return x < 1e-5 ? x * (1 - x * (.5 - x / 6)) : 1 - Math.Exp(-x);
    }

    internal static double PayloadReads(double roots, double inputFraction, double survival, int capacity)
    {
        double Batch(double n)
        {
            if (n <= 0 || inputFraction <= 0 || survival <= 0) return 0;
            double mean = n * inputFraction;
            if (n <= capacity || mean + 9 * Math.Sqrt(mean * (1 - inputFraction)) <= capacity)
                return Nonempty(n, inputFraction * survival);
            double windows = BatchWindows(n, inputFraction, capacity);
            return windows * Nonempty(mean / windows, survival);
        }
        double full = Math.Floor(roots / PrivateOrdinalBuffer.Capacity);
        return full * Batch(PrivateOrdinalBuffer.Capacity) + Batch(roots - full * PrivateOrdinalBuffer.Capacity);
    }

    // Unknown producer survival is a bound, not an observed ratio or invented probability.
    // Common final ABI1/Exact work cancels between equivalent complete allocations.
    internal static Range Price(double roots, double? survival, Work first, Work second, bool resident)
    {
        double Cost(double s)
        {
            // Same offline submission/header/payload classes as the complete
            // bounded chain quote. Terminal output remains deliberately excluded:
            // unknown C sieve survival is not replaced by Exact acceptance.
            PrivateSerialChainPricing.Node[] nodes = [
                new('1', "PairProducer", first.DenseNs, first.CompactNs, first.Capacity, s,
                    PublicPayloadFixedMs: first.PublicPayloadFixedMs, PublicSurvivorNs: first.PublicSurvivorNs),
                new('2', "PairConsumer", second.DenseNs, second.CompactNs, second.Capacity, null,
                    PublicPayloadFixedMs: second.PublicPayloadFixedMs, PublicSurvivorNs: second.PublicSurvivorNs)];
            return PrivateSerialChainPricing.Price(roots, nodes, resident);
        }
        return survival is double s ? new(Cost(s), Cost(s)) : new(Cost(0), Cost(1));
    }

    internal static int Choose(Range forward, Range reverse, Range resident, int baseline)
    {
        Range[] quotes = [forward, reverse, resident];
        // Strict dominance avoids turning an unknown producer into a point estimate.
        for (int i = 0; i < quotes.Length; i++)
            if (Enumerable.Range(0, quotes.Length).Where(j => j != i)
                .All(j => quotes[i].Upper + DecisionUncertaintyMs < quotes[j].Lower)) return i;
        // A private/ordinary tie prefers ordinary; improve the ordinary order only on evidence.
        int other = 1 - baseline;
        return quotes[other].Upper + DecisionUncertaintyMs < quotes[baseline].Lower ? other : baseline;
    }

    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        FamilyExecutionPlan baseline, out FamilyExecutionPlan? selected)
    {
        selected = null;
        if (registered.Count != 2) return false; // missing topology, not mispricing
        IFamilyInvocation first, second, composite;
        Work firstWork, secondWork;
        string coverage;
        bool pricedShape;
        double? firstSetup = null, secondSetup = null;
        if (registered.OfType<NeowFamily>().SingleOrDefault() is { GpuPlan: { } n } nf &&
            registered.OfType<CombatRewardFamily>().SingleOrDefault() is { GpuPlan: { } c } cf)
        {
            first = nf; second = cf; coverage = "NC";
            composite = new NcPrivateInvocation(request, c, n, "private-nc");
            pricedShape = NeowPhysicalPricing.TryResolve(nf, out firstWork, out _);
            firstWork = firstWork with { PublicPayloadFixedMs = 4, PublicSurvivorNs = 43 };
            secondWork = CombatRewardPhysicalPricing.LegacyNcWork(c);
            firstSetup = n.DirectNestedMode == 0 ? 2400 : 120;
            if (c.UsesHotLoop) secondSetup = 200; // measured public C HotLoop constructor, not GenericStreaming extrapolation

        }
        else if (registered.OfType<EventResultFamily>().SingleOrDefault() is { } e &&
                 registered.OfType<WorldFamily>().SingleOrDefault() is { } w &&
                 e.Plan.GpuSupported && WorldFamilyGpuPlan.Supports(w.Replay))
        {
            var wp = new WorldFamilyGpuPlan(w.Replay);
            first = e; second = w; coverage = "EW";
            composite = new EwPrivateInvocation(request, e.Plan, wp, "private-ordinal");
            firstWork = EventResultPhysicalPricing.LegacyEwWork;
            secondWork = WorldPhysicalPricing.LegacyEwWork(wp);
            pricedShape = !wp.VariantOnly;
        }
        else return false;

        bool compatible = FamilyPhysicalQuote.HasReferenceBackend();
        if (!compatible || !pricedShape)
        {
            selected = baseline with { DecisionEvidence = baseline.DecisionEvidence.Concat(new[] {
                "RegisteredAllocations=" + coverage + ":Forward,Reverse,PrivateForward",
                "AllocationPriceUnavailable:" + (compatible ? "PhysicalWorkShape" : "CompatibleOfflineDeviceEvidence") }).ToArray() };
            return true; // preserve ordinary execution; no Search admission gate
        }

        double roots = request.ScanCount;
        var joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        if (joint.JointlyPriced && joint.Probability is > 0 and <= 1)
            roots = Math.Min(roots, Math.Max(1, request.TargetMatchCount) / joint.Probability.Value);
        double? sFirst = first.ResolveSurvival(new HashSet<string>(StringComparer.Ordinal)).SurvivalProbability;
        double? sSecond = second.ResolveSurvival(new HashSet<string>(StringComparer.Ordinal)).SurvivalProbability;
        double gpuRatio = GpuCostCalibration.Capture().GlobalRatio;
        Range Scale(Range r) => new(r.Lower * gpuRatio, r.Upper * gpuRatio);
        Range forward = Scale(Price(roots, sFirst, firstWork, secondWork, false));
        Range reverse = Scale(Price(roots, sSecond, secondWork, firstWork, false));
        Range resident = Scale(Price(roots, sFirst, firstWork, secondWork, true));
        int previous = baseline.Stages[0].Family.FamilyId == first.FamilyId ? 0 : 1;
        int choice = Choose(forward, reverse, resident, previous);
        FamilyExecutionPlan plan = choice == 2 ? FamilyPlanner.InOrder([composite]) :
            FamilyPlanner.InOrder(choice == 0 ? [first, second] : [second, first]);
        if (!plan.Stages.SelectMany(s => s.Coverage).ToHashSet(StringComparer.Ordinal)
            .SetEquals(baseline.Stages.SelectMany(s => s.Coverage)))
            throw new InvalidOperationException("PrivateOrdinalAllocationCoverageMismatch");
        string RangeText(Range r) => F(r.Lower) + ".." + F(r.Upper);
        string[] evidence = ["RegisteredAllocations=" + coverage + ":Forward,Reverse,PrivateForward",
            "OfflineEvidence=Y2,Y4.DeviceWorkShape.v1;Edges=GlobalComposition.20260910.v1;NQuote=" + NeowPhysicalPricing.Revision, "ExpectedRoots=" + F(roots),
            "ModeledProducerSurvival=" + (sFirst.HasValue ? F(sFirst.Value) : "UnknownBound0..1"),
            "ExpectedIntermediate=" + (sFirst.HasValue ? F(roots * sFirst.Value) : "UnknownBound0..Roots"),
            "ForwardComparativeMs=" + RangeText(forward), "ReverseComparativeMs=" + RangeText(reverse),
            "PrivateComparativeMs=" + RangeText(resident), "DecisionUncertaintyMs=" + F(DecisionUncertaintyMs),
            "SelectedAllocation=" + (choice == 2 ? "PrivateForward" : choice == 0 ? "Forward" : "Reverse"),
            "ComparativePricesExcludeCommonFinalAbi1ExactSetup;CompleteQuoteAvailabilitySeparate;NoObservedSurvival"];
        selected = plan with { SelectionPolicyId = Policy,
            DecisionEvidence = evidence, ExpectedFamilyPipelineMsPerRoot = null };
        // Only close the point ETA when the actually selected public order has
        // resolved conditional stage populations and measured setup. Rare NC's
        // unknown terminal sieve remains Unknown even though private wins the
        // comparative bound; do not use its observed tiny terminal population.
        if (coverage == "NC" && choice != 2 && firstSetup.HasValue && secondSetup.HasValue &&
            plan.Stages.All(stage => stage.Survival is >= 0 and <= 1))
        {
            var orderedWork = choice == 0 ? new[] { firstWork, secondWork } : new[] { secondWork, firstWork };
            var nodes = plan.Stages.Select((stage, i) => new PrivateSerialChainPricing.Node(
                stage.Family.FamilyId[0], "NC.SelectedPublicWork", orderedWork[i].DenseNs, orderedWork[i].CompactNs,
                orderedWork[i].Capacity, stage.Survival, PublicPayloadFixedMs: orderedWork[i].PublicPayloadFixedMs,
                PublicSurvivorNs: orderedWork[i].PublicSurvivorNs)).ToArray();
            selected = selected with {
                EstimateCanonicalMilliseconds = count => gpuRatio * PrivateSerialChainPricing.Price(count, nodes, false, true),
                EstimatedSetupMilliseconds = 100 + firstSetup.Value + secondSetup.Value,
                EstimatedTerminalSurvival = nodes.Aggregate(1d, (p, node) => p * node.Survival!.Value),
                CompleteQuoteEvidence = "NC.PublicHotLoop.Complete.20260910.v1;ConditionalStageProjections;OfflineCoarse" };
        }
        return true;
    }

    private static string F(double x) => x.ToString("G17", CultureInfo.InvariantCulture);
}
