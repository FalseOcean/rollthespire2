using System.Globalization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Offline work-shape evidence, not a semantic graph or an online scheduler.
// See NWAE_PRIVATE_CHAIN_PRICING_20260909.md. Unknown evidence preserves the plan.
internal static class PrivateSerialChainPricing
{
    internal const double PromotionUncertaintyMs = 100;
    // Complete-quote recurring envelope, measured offline before the v3 holdout:
    // repeat spread after removing setup was at most 77 ms, rounded up to 100.
    // The old 250 ms includes shared setup/JIT variation and remains appropriate
    // only for the older incomplete comparative quotes. Absolute and relative
    // allowances describe the same uncertainty, so do not add them together.
    internal static bool Promotes(double publicRecurringMs, double privateRecurringMs) =>
        double.IsFinite(publicRecurringMs) && double.IsFinite(privateRecurringMs) &&
        publicRecurringMs - privateRecurringMs > Math.Max(100, publicRecurringMs * .1);

    internal static bool IsSingleOptionWork(AncientOptionFamilyPlan plan) =>
        AncientOptionPhysicalPricing.IsSingleOptionWork(plan);

    internal readonly record struct Node(char Id, string Shape, double DenseNs, double CompactNs,
        int Capacity, double? Survival, double SetupMs = 0,
        double PublicPayloadFixedMs = .15, double PublicSurvivorNs = 30);
    internal static IEnumerable<Node[]> Orders(Node[] nodes)
    {
        if (nodes.Length == 0) { yield return []; yield break; }
        for (int i = 0; i < nodes.Length; i++)
            foreach (var tail in Orders(nodes.Where((_, j) => j != i).ToArray()))
                yield return [nodes[i], ..tail];
    }
    // Offline D3D12 boundary classes measured in physical-composition-v1. Device
    // emission/reconstruction is in numerical rates. Header/count is charged once
    // per submission window for either transport; public payload is additional.
    // The former synchronous console/file envelope was removed by operational
    // logging consolidation; default execution no longer incurs that fee.
    internal static double Price(double roots, Node[] order, bool privateEdges,
        bool includeTerminal = false,
        double initialFraction = 1, bool firstIsCompact = false)
    {
        if (roots <= 0) return 0;
        double fraction = initialFraction, ms = 0;
        for (int i = 0; i < order.Length; i++)
        {
            var node = order[i]; double input = roots * fraction;
            double windows = PrivateOrdinalAllocationPricing.Windows(roots, fraction, node.Capacity);
            double meanWindow = windows > 0 ? input / windows : 0;
            double submissionMs = meanWindow < (1 << 18) ? .25 : .05;
            ms += input * (i == 0 && !firstIsCompact ? node.DenseNs : node.CompactNs) / 1e6;
            ms += windows * submissionMs; // Submit/sync floor, no output transport.
            ms += windows * .15; // Header/count edge, including private output.
            bool terminal = i == order.Length - 1;
            if (terminal && !includeTerminal) break;
            if (node.Survival is not double survival) return includeTerminal ? double.NaN : ms;
            double inputFraction = fraction;
            fraction *= survival;
            if (!privateEdges || terminal)
            {
                double output = roots * fraction;
                double nonemptyReads = PrivateOrdinalAllocationPricing.PayloadReads(roots, inputFraction, survival, node.Capacity);
                ms += nonemptyReads * node.PublicPayloadFixedMs;
                // The last edge does not pack/upload to another Family.
                ms += output * (node.PublicSurvivorNs - (terminal ? 5 : 0)) / 1e6;
            }
        }
        return ms;
    }
    internal static double Setup(Node[] nodes) => 100 + nodes.Sum(n => n.SetupMs);
    internal static double CompletePrice(double roots, Node[] order, bool privateEdges) =>
        Setup(order) + Price(roots, order, privateEdges, includeTerminal: true);
    internal static Node[] Rank(double roots, Node[] gates, Node terminal)
    {
        var candidates = Orders(gates).Select(o => o.Append(terminal).ToArray()).ToArray();
        double best = candidates.Min(o => Price(roots, o, true));
        // Deterministic registration-order preference inside the accepted 10% uncertainty.
        return candidates.First(o => Price(roots, o, true) <= best * 1.1);
    }
    // The existing World-domain probability includes parent-scoped Ancient options.
    // For the admitted distinct-act A conjunction, remove that already-priced A
    // factor once to obtain W-owned identity/progression survival. This is modeled
    // projection, never a measured survivor ratio or a new probability estimator.
    internal static double? ProjectWorldSurvival(ExactSearchExecutionRequest request, double conditionalAncientSurvival)
        => WorldPhysicalPricing.ProjectOwnedSurvival(request, conditionalAncientSurvival);
    internal static bool TrySelect(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        FamilyExecutionPlan baseline, out FamilyExecutionPlan? selected)
    {
        selected = null;
        if (registered.Count is < 2 or > 4 || registered.Any(f => f.FamilyId is not
            ("N.Neow" or "A.AncientOption" or "E.EventResult" or "W.World"))) return false;
        var w = registered.OfType<WorldFamily>().SingleOrDefault();
        if (w is null || !WorldFamilyGpuPlan.Supports(w.Replay)) return false;
        var n = registered.OfType<NeowFamily>().SingleOrDefault();
        var a = registered.OfType<AncientOptionFamily>().SingleOrDefault();
        var e = registered.OfType<EventResultFamily>().SingleOrDefault();
        if (n is null && a is null && e is null) return false;
        if (n is not null && n.GpuPlan is null || a is not null && !a.Plan.GpuSupported ||
            e is not null && !e.Plan.GpuSupported) return false;
        if (!FamilyPhysicalQuote.HasReferenceBackend()) return false;
        var wp = new WorldFamilyGpuPlan(w.Replay);
        var nodes = new List<Node>();
        foreach (var family in registered)
        {
            var dense = family.QuotePhysicalWork(new(false, true, PrivateOrdinalBuffer.Capacity));
            var compact = family.QuotePhysicalWork(new(true, true, PrivateOrdinalBuffer.Capacity));
            double? survival = family.ResolveSurvival(new HashSet<string>(StringComparer.Ordinal)).SurvivalProbability;
            if (dense is null || compact is null || dense.SetupMilliseconds is not double setup ||
                survival is not (>= 0 and <= 1)) return false;
            // Transport classes are runtime edge evidence, not numerical rates.
            bool largeAppend = dense.PublicTransportClass == "LargeResidentAppend32";
            nodes.Add(new(family.FamilyId[0], dense.Shape, dense.NanosecondsPerInput,
                compact.NanosecondsPerInput, dense.WindowCapacity, survival, setup,
                largeAppend ? 4 : .15, largeAppend ? 43 : 30));
        }
        var terminal = nodes.Single(node => node.Id == 'W');
        nodes.Remove(terminal);
        if (terminal.Survival is not double wSurvival) return false;
        double gpuRatio = GpuCostCalibration.Capture().GlobalRatio;
        double roots = request.ScanCount;
        var joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        if (joint.JointlyPriced && joint.Probability is > 0 and <= 1)
            roots = Math.Min(roots, Math.Max(1, request.TargetMatchCount) / joint.Probability.Value);
        var all = nodes.Append(terminal).ToArray();
        // Setup belongs to the actual binding, not one Dense quote reused for
        // every order. Keep the existing recurring/edge price model untouched.
        double BoundSetup(Node[] order, bool privateEdges)
        {
            double sum=100;
            for(int i=0;i<order.Length;i++)
            {
                var family=registered.Single(f=>f.FamilyId[0]==order[i].Id);
                var quote=family.QuotePhysicalWork(new(i!=0,privateEdges && i!=order.Length-1,
                    PrivateOrdinalBuffer.Capacity,privateEdges && i!=0));
                if(quote?.SetupMilliseconds is not double setup) return double.PositiveInfinity;
                sum+=setup;
            }
            return sum;
        }
        double BoundComplete(Node[] order,bool privateEdges) => BoundSetup(order,privateEdges) +
            gpuRatio * Price(roots,order,privateEdges,includeTerminal:true);
        var orders = Orders(all).ToArray();
        int orderCount = orders.Length * 2;
        var bestPrivate = orders.MinBy(o => BoundComplete(o, true))!;
        // Preserve the simple terminal-W order inside a 10% complete-time tie.
        var preferred = Rank(roots, nodes.ToArray(), terminal);
        var ranked = BoundComplete(preferred, true) <= BoundComplete(bestPrivate, true) * 1.1
            ? preferred : bestPrivate;
        var publicOrder = orders.MinBy(o => BoundComplete(o, false))!;
        double privateMs = BoundComplete(ranked, true);
        double publicMs = BoundComplete(publicOrder, false);
        if(!double.IsFinite(privateMs) || !double.IsFinite(publicMs)) return false;
        // Compare recurring work for the modeled k-result horizon. Common startup
        // must not enlarge the uncertainty margin on a steady-state decision.
        double privateRecurringMs = privateMs - BoundSetup(ranked,true);
        double publicRecurringMs = publicMs - BoundSetup(publicOrder,false);
        bool promote = Promotes(publicRecurringMs, privateRecurringMs);
        string order = new((promote ? ranked : publicOrder).Select(x => x.Id).ToArray());
        string F(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        string[] evidence = ["OfflineWorkShape=GlobalComposition.20260910.v1;NQuote=" + NeowPhysicalPricing.Revision,
            "ExpectedRoots=" + F(roots), "PrivateOrder=" + new string(ranked.Select(x => x.Id).ToArray()),
            "PrivateCompleteMs=" + F(privateMs), "BestPublicCompleteMs=" + F(publicMs),
            "Objective=SteadyStateWallToKExact;StartupExcludedFromDecision;TieTolerance=10Percent;CompleteRecurringDecisionFloorMs=100;UncertaintyCombination=Maximum",
            "PrivateRecurringMs=" + F(privateRecurringMs), "PublicRecurringMs=" + F(publicRecurringMs), "WorldOwnedSurvival=" + F(wSurvival),
            "Nodes=" + string.Join('|', all.Select(x => $"{x.Id}:{x.Shape}:denseNs={F(x.DenseNs)}:compactNs={F(x.CompactNs)}:s={x.Survival}")),
            "IncludesSetupHeadersPublicPayload;ExpectedNonemptyWindows;ExactTailPricedSeparately;NoObservedSurvival"];
        Bootstrap.RuntimeLog.TryBackgroundInfo($"privateSerialPricing=true;order={order};selectedPrivate={promote};" + string.Join(';', evidence));
        FamilyExecutionPlan result;
        if (promote)
        {
            var invocation = new NwaePrivateInvocation(request, n?.GpuPlan, a?.Plan, e?.Plan, wp, order, false);
            if (!invocation.Coverage.ToHashSet().SetEquals(registered.SelectMany(f => f.Coverage)))
                throw new InvalidOperationException("PrivateSerialPricing.CoverageMismatch");
            result = FamilyPlanner.InOrder([invocation]);
        }
        else result = FamilyPlanner.InOrder(publicOrder.Select(node => registered.Single(f => f.FamilyId[0] == node.Id)).ToArray());
        Node[] selectedNodes = (promote ? ranked : publicOrder).ToArray();
        selected = result with {
            SelectionPolicyId = "FamilyPlanner.PrivateSerial.WorkShape.20260910.v3",
            RankingAuthority = FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            DecisionEvidence = evidence, ExpectedFamilyPipelineMsPerRoot = null, BoundedPricingOrderCount = orderCount,
            EstimateCanonicalMilliseconds = count => gpuRatio * Price(count, selectedNodes, promote, true),
            EstimatedSetupMilliseconds = BoundSetup(selectedNodes,promote),
            EstimatedTerminalSurvival = selectedNodes.Aggregate(1d, (p, node) => p * node.Survival!.Value),
            CompleteQuoteEvidence = "GlobalComposition.20260910.v1;ExpectedOccupancy.20260913.v1;NoDefaultDiagnosticFee;RTX4060Laptop.D3D12;OfflineCoarse" };
        return true;
    }
}
