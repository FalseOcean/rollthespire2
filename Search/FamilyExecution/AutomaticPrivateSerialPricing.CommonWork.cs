using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class AutomaticPrivateSerialPricing
{
    // No cross-order cancellation: the same immutable Family instances receive
    // the same modeled populations and passed Coverage in these two allocations.
    internal static bool TryCompareCommonWork(double roots, IFamilyInvocation[] order,
        Func<IFamilyInvocation, FamilyPhysicalQuoteRequest, FamilyPhysicalQuote?> quote,
        Func<IFamilyInvocation, IReadOnlySet<string>, double?> survival,
        out double advantageMs, out string[] evidence, GpuCostSnapshot? costSnapshot = null)
    {
        advantageMs = 0; evidence = [];
        double fraction = 1;
        var passed = new HashSet<string>();
        var pairs = new Dictionary<(IFamilyInvocation, bool), FamilyPhysicalQuote>();
        var cancelled = new List<string>();
        for (int i = 0; i < order.Length; i++)
        {
            var f = order[i]; bool terminal = i == order.Length - 1;
            double mean = Math.Min(roots, PrivateOrdinalBuffer.Capacity) * fraction;
            var pub = new FamilyPhysicalQuoteRequest(i > 0, false, mean);
            var priv = new FamilyPhysicalQuoteRequest(i > 0, !terminal, mean, i > 0);
            var a = quote(f, pub); var b = quote(f, priv);
            if (a is null || b is null)
            {
                a = f.QuoteCommonPrivateWorkCancellation(pub);
                b = f.QuoteCommonPrivateWorkCancellation(priv);
                if (a is null || b is null || a.Shape != b.Shape || a.WindowCapacity != b.WindowCapacity ||
                    a.NanosecondsPerInput != 0 || b.NanosecondsPerInput != 0 ||
                    a.OutputElementBytes != b.OutputElementBytes || a.OutputAlreadyOrdered != b.OutputAlreadyOrdered ||
                    a.PublicTransportClass != b.PublicTransportClass) return false;
                cancelled.Add(f.FamilyId + ":" + a.Shape);
                if (costSnapshot is not null) { a = costSnapshot.Local(f, a, pub); b = costSnapshot.Local(f, b, priv); }
            }
            if (a.SetupMilliseconds is null || b.SetupMilliseconds is null) return false;
            pairs[(f, false)] = a; pairs[(f, true)] = b;
            double? s = survival(f, passed);
            if (s is not (>= 0 and <= 1)) return false;
            fraction *= s.Value; passed.UnionWith(f.Coverage);
        }
        if (cancelled.Count == 0) return false;
        var publicBound = Price(roots, order, false, (f,g) => pairs[(f,false)], survival);
        var privateBound = Price(roots, order, true, (f,g) => pairs[(f,true)], survival);
        if (publicBound is null || privateBound is null) return false;
        advantageMs = publicBound.CanonicalMs - privateBound.CanonicalMs -
            Math.Max(0, privateBound.SetupMs!.Value - publicBound.SetupMs!.Value);
        evidence = ["SameOrderCommonWorkCancellation;AbsoluteEtaUnavailable;NoObservedPopulation",
            "Cancelled=" + string.Join('|', cancelled), "BoundaryAdvantageAfterEagerSetupBoundMs=" + F(advantageMs)];
        return double.IsFinite(advantageMs);
    }

    private static bool TrySelectCommonWork(ExactSearchExecutionRequest request, IReadOnlyList<IFamilyInvocation> registered,
        double roots, Func<IFamilyInvocation, FamilyPhysicalQuoteRequest, FamilyPhysicalQuote?> quote,
        Func<IFamilyInvocation, IReadOnlySet<string>, double?> survival, out FamilyExecutionPlan? selected)
    {
        selected = null;
        var order = FamilyPlanner.Plan(registered).OrderedFamilies.ToArray();
        if (!TryCompareCommonWork(roots, order, quote, survival, out double saving, out var evidence, GpuCostCalibration.Capture()) ||
            saving <= PrivateSerialChainPricing.PromotionUncertaintyMs) return false;
        selected = FamilyPlanner.InOrder([new AutomaticPrivateSerialInvocation(request, order)]) with {
            SelectionPolicyId = "FamilyPlanner.PrivateCommonWork.20260913.v1",
            RankingAuthority = FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            BoundedPricingOrderCount = 2, DecisionEvidence = evidence,
            ExpectedFamilyPipelineMsPerRoot = null,
            CompleteQuoteEvidence = "Unavailable:CommonUnknownCancelledOnly" };
        Bootstrap.RuntimeLog.TryBackgroundInfo("privateCommonWorkSelection=true;order=" +
            string.Concat(order.Select(f=>f.FamilyId[0])) + ";" + string.Join(';', evidence));
        return true;
    }
}
