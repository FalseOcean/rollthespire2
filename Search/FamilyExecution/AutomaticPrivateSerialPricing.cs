using System.Globalization;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// Finite complete orders of registered whole Families; no numerical classification
// lives here. Quote and survival requests are memoized only for this immutable plan.
internal static partial class AutomaticPrivateSerialPricing
{
    internal sealed record PricedOrder(IFamilyInvocation[] Order, bool Private,
        double CanonicalMs, double TerminalRate, double? SetupMs, string[] Evidence);

    internal static IEnumerable<IFamilyInvocation[]> Orders(IFamilyInvocation[] families)
    {
        if (families.Length == 0) { yield return []; yield break; }
        for (int i = 0; i < families.Length; i++)
            foreach (var tail in Orders(families.Where((_, j) => i != j).ToArray()))
                yield return [families[i], ..tail];
    }

    internal static PricedOrder? Price(double roots, IFamilyInvocation[] order, bool privateEdges,
        Func<IFamilyInvocation, FamilyPhysicalQuoteRequest, FamilyPhysicalQuote?> quote,
        Func<IFamilyInvocation, IReadOnlySet<string>, double?> survival, long? cpuScanLimit = null,
        bool firstCompactInput = false, int? rootWindowOverride = null, double? initialSetupMs = null)
    {
        if (!double.IsFinite(roots) || roots < 0) return null;
        if(cpuScanLimit.HasValue && roots>0 && order.Length>0 && order.All(f=>f is FamilyCpuExecution))
            roots=Math.Min(cpuScanLimit.Value,Math.Ceiling(roots/65536)*65536);
        double fraction = 1, ms = 0; double? setup = initialSetupMs ?? (order.All(f=>f is FamilyCpuExecution) ? 0 : 100);
        var passed = new HashSet<string>(StringComparer.Ordinal);
        var evidence = new List<string>();
        int rootWindow = rootWindowOverride ?? (order.All(f=>f is FamilyCpuExecution) ? 65536 : PrivateOrdinalBuffer.Capacity);
        for (int i = 0; i < order.Length; i++)
        {
            bool terminal = i == order.Length - 1;
            double population = roots * fraction;
            double mean = Math.Min(roots, rootWindow) * fraction;
            var geometry = new FamilyPhysicalQuoteRequest(i != 0 || firstCompactInput, privateEdges && !terminal,
                mean, privateEdges && i != 0);
            var q = order[i] is FamilyCpuExecution cpu ? cpu.Quote(geometry,passed) : quote(order[i], geometry);
            double? s = survival(order[i], passed);
            if (q is null || q.WindowCapacity <= 0 || !double.IsFinite(q.NanosecondsPerInput) ||
                q.NanosecondsPerInput < 0 || s is not (>= 0 and <= 1)) return null;
            double windows = PrivateOrdinalAllocationPricing.Windows(roots, fraction, q.WindowCapacity, rootWindow);
            // Runtime transport/submission evidence. Device emission is already in
            // the Family quote; private ordinals pay neither host sort nor upload.
            double numerical = population * q.NanosecondsPerInput / 1e6;
            if (q.PublicTransportClass == "CpuOrderedAbi1")
            {
                // CPU reference already includes hash, partition/join and ordered
                // ABI1. No GPU submit/readback/emission fee may be charged again.
                if (privateEdges) return null;
                ms += numerical + windows * q.FixedWindowMilliseconds;
                fraction *= s.Value;
                // Most CPU Families still leave setup unknown. Consume an explicit
                // owned quote when available; never silently turn Unknown into zero.
                setup = setup.HasValue && q.SetupMilliseconds.HasValue ? setup + q.SetupMilliseconds : null;
                evidence.Add($"{order[i].FamilyId}:{q.Shape}:input={F(population)}:s={F(s.Value)}:ns={F(q.NanosecondsPerInput)}:CPUCanonicalIncludesAbi1:{q.Evidence}");
                passed.UnionWith(order[i].Coverage);
                continue;
            }
            if (i > 0 && order[i-1] is FamilyCpuExecution)
                ms += population * 5 / 1e6 * q.GpuCostRatio; // existing public upload edge, not CPU materialization
            else if (i == 0 && firstCompactInput)
                ms += population * 5 / 1e6 * q.GpuCostRatio;
            ms += numerical + windows * (.15 + q.FixedWindowMilliseconds) * q.GpuCostRatio;
            // No synchronous per-stage logging envelope: operational logging
            // consolidated those writes behind opt-in detail. Empty stages never submit.
            double inputFraction = fraction;
            fraction *= s.Value;
            if (!privateEdges || terminal)
            {
                double output = roots * fraction;
                double reads = PrivateOrdinalAllocationPricing.PayloadReads(roots, inputFraction, s.Value, q.WindowCapacity);
                bool large = q.PublicTransportClass == "LargeResidentAppend32";
                double fixedMs = large ? 4 : .15;
                double survivorNs = q.OutputAlreadyOrdered ? 6 : large ? 38 : 25;
                ms += (reads * fixedMs + output * (survivorNs + (terminal || order[i+1] is FamilyCpuExecution ? 0 : 5)) / 1e6) * q.GpuCostRatio;
            }
            // Ordinary owners are constructed lazily only if reached at least
            // once. The current private lifetime group binds all owners up front.
            // This is an allocation envelope difference, not a numerical surcharge.
            double setupReach = privateEdges ? 1 : PrivateOrdinalAllocationPricing.Nonempty(roots, inputFraction);
            setup = setup.HasValue && q.SetupMilliseconds.HasValue ? setup + setupReach * q.SetupMilliseconds : null;
            evidence.Add($"{order[i].FamilyId}:{q.Shape}:input={F(population)}:s={F(s.Value)}:ns={F(q.NanosecondsPerInput)}:windows={F(windows)}:{q.Evidence}");
            passed.UnionWith(order[i].Coverage);
        }
        return new(order, privateEdges, ms, fraction, setup, evidence.ToArray());
    }

    internal static bool TrySelect(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, out FamilyExecutionPlan? selected)
    {
        selected = null;
        if (registered.Count is < 2 or > 7 || registered.Any(f => !f.CanBindPrivateSerial ||
                f.Coverage.Count != 1 || f.Coverage[0] != f.FamilyId) ||
            registered.Select(f => f.FamilyId).Distinct().Count() != registered.Count) return false;
        double roots = request.ScanCount;
        var joint = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        bool knownHitProbability = joint.JointlyPriced && joint.Probability is > 0 and <= 1;
        if (knownHitProbability && joint.Probability is double hitProbability)
            roots = Math.Min(roots, Math.Max(1, request.TargetMatchCount) / hitProbability);
        var costSnapshot = GpuCostCalibration.Capture();
        var quotes = new Dictionary<(IFamilyInvocation, FamilyPhysicalQuoteRequest), FamilyPhysicalQuote?>();
        var survivals = new Dictionary<(IFamilyInvocation, string), double?>();
        FamilyPhysicalQuote? Quote(IFamilyInvocation f, FamilyPhysicalQuoteRequest g)
        {
            if (!quotes.TryGetValue((f, g), out var q)) quotes[(f, g)] = q = f.QuotePhysicalWork(g) is { } reference ? costSnapshot.Local(f, reference, g) : null;
            return q;
        }
        double? Survival(IFamilyInvocation f, IReadOnlySet<string> p)
        {
            var key = (f, string.Join(',', p.Order(StringComparer.Ordinal)));
            if (!survivals.TryGetValue(key, out var s)) survivals[key] = s = f.ResolveSurvival(p).SurvivalProbability;
            return s;
        }
        var priced = new List<PricedOrder>();
        foreach (var order in Orders(registered.ToArray()))
            foreach (bool privateEdges in new[] { false, true })
                if (Price(roots, order, privateEdges, Quote, Survival) is { } p) priced.Add(p);
        if (!priced.Any(p => p.Private) || !priced.Any(p => !p.Private))
            return TrySelectCommonWork(request, registered, roots, Quote, Survival, out selected);
        var exact = SearchPredictabilityVerificationStore.TryGetExactTiming(FamilyExactTimingDomains.Resolve(request));
        // An unknown common Exact tail does not invent a price. Different modeled
        // terminal rates require usable Exact timing before complete comparison.
        double lo = priced.Min(p => p.TerminalRate), hi = priced.Max(p => p.TerminalRate);
        bool commonTerminal = hi - lo <= Math.Max(1e-15, hi * 1e-8);
        if (exact is not { Usable: true } && !commonTerminal) return false;
        double Wall(PricedOrder p) => exact is { Usable: true }
            ? Math.Max(p.CanonicalMs, roots * (commonTerminal ? priced[0].TerminalRate : p.TerminalRate) *
                exact.AverageExactMsPerAttempt / Math.Max(1, request.WorkerCount))
            : p.CanonicalMs;
        // Multiplication order roundoff must not choose an expensive order when
        // the same Exact tail dominates all candidates. Prefer less Family work
        // inside a true wall-time tie, then retain deterministic enumeration order.
        var bestPrivate = priced.Where(p => p.Private).OrderBy(Wall).ThenBy(p => p.CanonicalMs).First();
        var bestPublic = priced.Where(p => !p.Private).OrderBy(Wall).ThenBy(p => p.CanonicalMs).First();
        double differentialSetup = bestPrivate.SetupMs is double ps && bestPublic.SetupMs is double os
            ? Math.Max(0, ps - os) : 0;
        // Shared startup never sets the frontier uncertainty margin. A real,
        // non-shared eager constructor can veto promotion over a bounded horizon;
        // this vanishes naturally when both allocations will reach every owner.
        bool coarse = bestPrivate.Evidence.Concat(bestPublic.Evidence).Any(e => e.Contains("ConservativeCoarse", StringComparison.Ordinal));
        // Compatible private is canonical inside the uncertainty interval. Public
        // must establish a real advantage, including private-only eager setup.
        bool promote = Wall(bestPrivate) + differentialSetup - Wall(bestPublic) <=
            Math.Max(PrivateSerialChainPricing.PromotionUncertaintyMs, Wall(bestPublic) * (coarse ? .2 : .1));
        var chosen = promote ? bestPrivate : bestPublic;
        Bootstrap.RuntimeLog.TryBackgroundInfo("planningAlternatives=true;scope=AdmissibleSelection;alternativesJson=" + Bootstrap.RuntimeLog.SafeJson(priced.Select(p=>new {order=string.Concat(p.Order.Select(f=>f.FamilyId[0])),privateEdges=p.Private,canonicalMs=p.CanonicalMs,wallMs=Wall(p),setupMs=p.SetupMs,terminalRate=p.TerminalRate,evidence=p.Evidence})));
        string orderText = string.Concat(chosen.Order.Select(f => f.FamilyId[0]));
        string[] evidence = ["FamilyOwnedQuotes;ModeledPassedCoverage;NoObservedPopulation",
            knownHitProbability ? "RootHorizon=ModeledKExactClippedToScan" : "RootHorizon=BoundedScanOnly;QueryHitProbabilityUnknown;TargetKTimeUnresolved",
            $"PricedPublic={priced.Count(p => !p.Private)};PricedPrivate={priced.Count(p => p.Private)};ExpectedRoots={F(roots)}",
            $"PrivateOrder={string.Concat(bestPrivate.Order.Select(f => f.FamilyId[0]))};PrivateCanonicalMs={F(bestPrivate.CanonicalMs)};PublicOrder={string.Concat(bestPublic.Order.Select(f => f.FamilyId[0]))};PublicCanonicalMs={F(bestPublic.CanonicalMs)}",
            $"ExactTail={(exact is { Usable: true } ? "StoredTimingParallelPressure" : "UnknownCommonTailCancels")};SharedStartupExcludedFromRanking;DifferentialEagerSetupMs={F(differentialSetup)};SelectedPrivate={promote}",
            ..chosen.Evidence];
        Bootstrap.RuntimeLog.TryBackgroundInfo($"automaticPrivateSerialPricing=true;order={orderText};selectedPrivate={promote};" + string.Join(';', evidence));
        selected = FamilyPlanner.InOrder(promote ? [new AutomaticPrivateSerialInvocation(request, chosen.Order)] : chosen.Order) with
        {
            SelectionPolicyId = "FamilyPlanner.AutomaticPrivateSerial.FamilyOwned.20260910.v1",
            RankingAuthority = FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            BoundedPricingOrderCount = priced.Count, ExpectedFamilyPipelineMsPerRoot = null,
            DecisionEvidence = evidence, EstimatedTerminalSurvival = chosen.TerminalRate,
            EstimatedSetupMilliseconds = chosen.SetupMs,
            EstimateCanonicalMilliseconds = n => Price(n, chosen.Order, promote, Quote, Survival)?.CanonicalMs,
            CompleteQuoteEvidence = "FamilyOwnedNumerical;RuntimeOrdinalEdges.ExpectedOccupancy.20260913.v1;NoDefaultDiagnosticFee;ModeledPopulation;OfflineMatrix.20260910.v1"
        };
        return true;
    }

    private static string F(double x) => x.ToString("G17", CultureInfo.InvariantCulture);
}
