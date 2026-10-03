using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Predictability;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

internal static partial class AutomaticPrivateSerialPricing
{
    // Extend existing complete-allocation comparison with Family-owned public CPU
    // realizations. Private GPU allocations retain their existing selection path.
    internal static FamilyExecutionPlan SelectPhysicalAlternatives(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> families, FamilyExecutionPlan baseline, bool gpuAvailable)
    {
        if (families.Count is 0 or > 7 || families.Any(f=>f.Coverage.Count!=1)) return baseline;
        var alternatives = families.ToDictionary(f=>f, f=>f.CpuRealizations.ToArray());
        if (alternatives.Values.All(a=>a.Length==0)) return baseline;
        foreach(var (family,cpus) in alternatives)
        {
            double nFraction = families.FirstOrDefault(f=>f.FamilyId=="N.Neow")?.Survival.SurvivalProbability ?? 1;
            Bootstrap.RuntimeLog.TryBackgroundInfo("familyCpuRealizations=true;family="+family.FamilyId+";alternativesJson="+
                Bootstrap.RuntimeLog.SafeJson(cpus.Select(cpu=>new {revision=cpu.ConditionPerformance.PhysicalImplementationRevision,
                    dense=cpu.QuotePhysicalWork(new(false,false,65536)),
                    compactAfterN=cpu is FamilyCpuExecution c && c.RequiredPassedCoverage is not null ? c.Quote(new(true,false,65536*nFraction),new HashSet<string>{"N.Neow"}) : null,
                    status="ApplicabilitySeparateFromPrice;NoCompleteAllocationImplied"})));
        }
        var probability = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request));
        double roots = probability.JointlyPriced && probability.Probability is > 0 and <= 1
            ? Math.Min(request.ScanCount, Math.Max(1,request.TargetMatchCount)/probability.Probability.Value) : request.ScanCount;
        // Identical unpriced stages may cancel when comparing only a known CPU
        // realization at the same position. This never invents a complete quote.
        var stageOrder = baseline.OrderedFamilies.ToArray();
        var stagePassed = new HashSet<string>(); double stageFraction=1; bool stageChanged=false;
        int stageWindow = stageOrder.All(f=>f is FamilyCpuExecution) ? 65536 : PrivateOrdinalBuffer.Capacity;
        for(int i=0;i<stageOrder.Length;i++)
        {
            var current=stageOrder[i];
            if(current is FamilyCpuExecution cpu)
            {
                var g=new FamilyPhysicalQuoteRequest(i>0,false,Math.Min(request.ScanCount,stageWindow)*stageFraction);
                var currentQuote=cpu.Quote(g,stagePassed);
                var owner=families.FirstOrDefault(f=>f.FamilyId==current.FamilyId);
                if(currentQuote is not null && owner is not null)
                {
                    var bestStage=alternatives[owner].OfType<FamilyCpuExecution>().Select(c=>(Cpu:c,Quote:c.Quote(g,stagePassed)))
                        .Where(x=>x.Quote is not null).OrderBy(x=>x.Quote!.NanosecondsPerInput).FirstOrDefault();
                    if(bestStage.Quote is not null && bestStage.Quote.NanosecondsPerInput<currentQuote.NanosecondsPerInput)
                    {
                        stageOrder[i]=bestStage.Cpu; stageChanged=true;
                        Bootstrap.RuntimeLog.TryBackgroundInfo("familyCpuStageSelection=true;family="+current.FamilyId+
                            ";before="+currentQuote.Shape+";after="+bestStage.Quote.Shape+";reason=SamePositionSameSurvivalUnknownCommonStagesCancel;wholeAllocationPriceMayRemainUnknown=true");
                    }
                }
            }
            stageFraction*=current.ResolveSurvival(stagePassed).SurvivalProbability ?? double.NaN;
            stagePassed.UnionWith(current.Coverage);
        }
        if(stageChanged)baseline=FamilyPlanner.InOrder(stageOrder) with { DecisionEvidence=["FamilyOwnedCpuStageQuotes;UnknownCommonStagesCancel;NoOrderOrSurvivalChange"] };
        var snapshot = GpuCostCalibration.Capture();
        var quotes = new Dictionary<(IFamilyInvocation,FamilyPhysicalQuoteRequest),FamilyPhysicalQuote?>();
        FamilyPhysicalQuote? Quote(IFamilyInvocation f, FamilyPhysicalQuoteRequest g)
        {
            if (!quotes.TryGetValue((f,g),out var q))
                quotes[(f,g)] = q = f.QuotePhysicalWork(g) is {} raw ? snapshot.Local(f,raw,g) : null;
            return q;
        }
        var survivals = new Dictionary<(IFamilyInvocation,string),double?>();
        double? Survival(IFamilyInvocation f,IReadOnlySet<string> passed)
        {
            var key=(f,string.Join(',',passed.Order(StringComparer.Ordinal)));
            if(!survivals.TryGetValue(key,out var value)) survivals[key]=value=f.ResolveSurvival(passed).SurvivalProbability;
            return value;
        }
        var exact = SearchPredictabilityVerificationStore.TryGetExactTiming(FamilyExactTimingDomains.Resolve(request));
        var priced = new List<PricedOrder>();
        foreach(var order in Orders(families.ToArray()))
        {
            var chosen = new IFamilyInvocation[order.Length];
            void Choose(int at, double fraction, HashSet<string> passed)
            {
                if(at==order.Length)
                {
                    if (chosen.Any(f=>f is FamilyCpuExecution) && Price(roots,chosen,false,Quote,Survival,cpuScanLimit:request.ScanCount) is {} p)
                        priced.Add(p with { Order=chosen.ToArray() });
                    return;
                }
                var options = gpuAvailable ? new[]{order[at]}.Concat(alternatives[order[at]]) : alternatives[order[at]];
                foreach(var f in options)
                {
                    // Prune unpriced CPU shapes before expanding whole orders.
                    // Check both actual root geometries; final Price binds one.
                    if(f is FamilyCpuExecution cpu &&
                        cpu.Quote(new(at>0,false,Math.Min(request.ScanCount,65536)*fraction),passed) is null &&
                        cpu.Quote(new(at>0,false,Math.Min(request.ScanCount,PrivateOrdinalBuffer.Capacity)*fraction),passed) is null) continue;
                    var s = Survival(f,passed);
                    if(s is not (>=0 and <=1)) continue;
                    chosen[at]=f;
                    var next = new HashSet<string>(passed); next.UnionWith(f.Coverage);
                    Choose(at+1,fraction*s.Value,next);
                }
            }
            Choose(0,1,new());
        }
        FamilyExecutionPlan BindCpuEnvelope(FamilyExecutionPlan plan)
        {
            if(plan.OrderedFamilies.Count==0 || !plan.OrderedFamilies.All(f=>f is FamilyCpuExecution))return plan;
            var kept=plan.OrderedFamilies.ToArray();
            var q=Price(roots,kept,false,Quote,Survival,cpuScanLimit:request.ScanCount);
            double? terminal=plan.Stages.All(s=>s.Survival.HasValue)?plan.Stages.Aggregate(1d,(v,s)=>v*s.Survival!.Value):null;
            return plan with {EstimateCanonicalMilliseconds=n=>Price(n,kept,false,Quote,Survival,cpuScanLimit:request.ScanCount)?.CanonicalMs,
                EstimatedSetupMilliseconds=q?.SetupMs,EstimatedTerminalSurvival=q?.TerminalRate??terminal,
                ExpectedFamilyPipelineMsPerRoot=null,CompleteQuoteEvidence="CpuOwnedActualBatchGeometry;UnknownNotInfinity"};
        }
        if (priced.Count == 0) return BindCpuEnvelope(baseline);
        double? baselineMs = baseline.EstimateCanonicalMilliseconds?.Invoke(roots);
        var basePublic = Price(roots,baseline.OrderedFamilies.ToArray(),false,Quote,Survival,cpuScanLimit:request.ScanCount);
        baselineMs ??= basePublic?.CanonicalMs;
        double? baseRate = baseline.EstimatedTerminalSurvival ?? basePublic?.TerminalRate;
        if (baseRate is null)
        {
            double fraction=1; var passed=new HashSet<string>();
            foreach(var f in baseline.OrderedFamilies)
            { var s=Survival(f,passed); if(s is null){fraction=double.NaN;break;} fraction*=s.Value; passed.UnionWith(f.Coverage); }
            if(double.IsFinite(fraction))baseRate=fraction;
        }
        // A known direct Exact alternative has no Filter cost and offers all roots.
        if(exact is {Usable:true}) priced.Add(new([],false,0,1,0,["DirectExact;LocalMsPerAttempt;NoCpuOrGpuRatio"]));
        double Wall(double ms,double rate) => exact is {Usable:true}
            ? Math.Max(ms,roots*rate*exact.AverageExactMsPerAttempt/Math.Max(1,request.WorkerCount)) : ms;
        bool Comparable(double rate) => exact is {Usable:true} || baseRate is double b && Math.Abs(b-rate)<=Math.Max(1e-15,b*1e-8);
        double? baselineSetup = baseline.EstimatedSetupMilliseconds ?? basePublic?.SetupMs;
        double SetupDelta(PricedOrder p)
        {
            if (p.SetupMs is not double a || baselineSetup is not double b) return 0;
            // These alternatives change at least one CPU/GPU realization. Avoiding
            // its known module setup is a saving even if another Family keeps a GPU.
            // The common owner cost cancels; unknown envelopes remain incomparable.
            return a-b;
        }
        var best = priced.Where(p=>Comparable(p.TerminalRate)).OrderBy(p=>Wall(p.CanonicalMs,p.TerminalRate)+SetupDelta(p)).ThenBy(p=>p.CanonicalMs).FirstOrDefault();
        // Unknown is not infinity: a priced Direct Exact must never discard an
        // effective unpriced Filter merely because its quote is absent.
        bool coarseComparison = best is not null && best.Evidence.Concat(basePublic?.Evidence ?? [])
            .Any(e => e.Contains("ConservativeCoarse", StringComparison.Ordinal));
        bool select = best is not null && baselineMs.HasValue &&
            Wall(best.CanonicalMs,best.TerminalRate)+SetupDelta(best)<Wall(baselineMs.Value,baseRate??best.TerminalRate)*(coarseComparison?.8:1);
        Bootstrap.RuntimeLog.TryBackgroundInfo("planningPhysicalAlternatives=true;gpuAvailable="+gpuAvailable+
            ";baselineCanonicalMs="+baselineMs+";selectedNew="+select+";alternativesJson="+
            Bootstrap.RuntimeLog.SafeJson(priced.Select(p=>new {revisions=p.Order.Select(f=>f.ConditionPerformance.PhysicalImplementationRevision),p.CanonicalMs,p.TerminalRate,wallMs=Wall(p.CanonicalMs,p.TerminalRate),p.Evidence})));
        if (!select) return BindCpuEnvelope(baseline);
        var selected=best!;
        return (selected.Order.Length==0 ? FamilyPlanner.Plan([]) : FamilyPlanner.InOrder(selected.Order)) with
        {
            SelectionPolicyId="FamilyPlanner.FamilyOwnedPhysicalAlternatives.20260912.v1",
            RankingAuthority=FamilyPlannerRankingAuthority.OfflineAllocationWorkShape,
            EstimatedTerminalSurvival=selected.TerminalRate, EstimatedSetupMilliseconds=selected.SetupMs,
            EstimateCanonicalMilliseconds=n=>Price(n,selected.Order,false,Quote,Survival,cpuScanLimit:request.ScanCount)?.CanonicalMs,
            ExpectedFamilyPipelineMsPerRoot=null, BoundedPricingOrderCount=priced.Count,
            DecisionEvidence=["FamilyOwnedCpuGpuReferenceQuotes;ActualStageInput;NoObservedRepricing;UnpricedExcluded",..selected.Evidence],
            CompleteQuoteEvidence="FamilyOwnedCpuCanonicalOrGpuNumerical;ExactLocalIndependent"
        };
    }
}
