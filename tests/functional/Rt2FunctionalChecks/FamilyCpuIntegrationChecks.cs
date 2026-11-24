using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.FamilyExecution;

internal static partial class FamilyCpuIntegrationChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool ok,string name) { checks++; if(!ok)throw new InvalidOperationException(name); }
        var context=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        foreach(string path in new[]{"cpu-main-v1/C-three-potions.json","v1/cases/S-three-shops.json","family-pricing-matrix-v1/R-three-common.json",
            "direct-capsule-v1/Small-C-check.json","direct-capsule-v1/Large-UR-check.json","classic-product-v1/B-SourceCapsules.json"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/"+path);
            var request=SearchPreparationCompiler.Compile(SearchCompiler.Compile(w.Query,context.Context),
                w.Options with {ScanCount=1<<24,TargetMatchCount=100000,WorkerCount=16}).Plan!;
            var p=FamilyExecutionCoordinator.Plan(request,gpuAvailable:false);
            Check(p.OrderedFamilies.All(f=>!f.ConditionPerformance.UsesGpu),"no GPU owner in CPU-only allocation");
            foreach(var f in p.OrderedFamilies)
            {
                Check(!f.CanBindPrivateSerial,"CPU does not bind private GPU port");
                var q=f.QuotePhysicalWork(new(false,false,65536));
                if(q is null)continue;
                Check(q.PublicTransportClass=="CpuOrderedAbi1" && q.OutputAlreadyOrdered && q.OutputElementBytes==8,"CPU canonical boundary");
                var unit = FamilyPlanner.Plan([f]).Stages.Single();
                Check(Math.Abs(unit.PerInputTimeMs!.Value- q.NanosecondsPerInput/1e6)<1e-9,"CPU single unit consumes its complete public quote once");
                Check(q.GpuCostRatio == 1 && (q.Evidence.Contains("ActualStageInput", StringComparison.Ordinal) ||
                    FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(request, f, q, 65536)),
                    "CPU attribution belongs to its historical actual-input anchor or exact embedded canonical body/input measurement");
                Check(f.QuotePhysicalWork(new(false,true,65536)) is null,"no CPU private quote");
                Check(FamilyCoarsePricing.IsEstimate(f.QuotePhysicalWork(new(false,false,1))),
                    "sub-1K CPU input has an explicit conservative estimate rather than a fictitious measured knot");
                var snapshot=new GpuCostSnapshot([],_=>null);
                Check(snapshot.Local(f,q,new(false,false,65536))==q,"GPU calibration never scales CPU");
            }
            Check(p.OrderedFamilies.Count>0,"Unknown baseline does not collapse into Direct Exact");
            if(path.Contains("Large-UR"))
            {
                var r=p.OrderedFamilies.Single(f=>f.FamilyId=="R.Relic");
                Check(r.CpuWorkers==request.WorkerCount,"configured R stage follows the user worker count under N");
                var own = r.QuotePhysicalWork(new(true,false,6553.6),new HashSet<string>{"N.Neow"});
                var otherPrefix = r.QuotePhysicalWork(new(true,false,6553.6),new HashSet<string>{"N.Neow","W.World"});
                Check(FamilyCoarsePricing.IsEstimate(own),"unmeasured P16 Capsule execution has an honest local work estimate");
                Check(FamilyCoarsePricing.IsEstimate(otherPrefix) && own!.NanosecondsPerInput == otherPrefix!.NanosecondsPerInput,
                    "unrelated passed coverage does not reprice unconditional Capsule work");
                Check(FamilyCoarsePricing.IsEstimate(r.QuotePhysicalWork(new(true,false,3000),new HashSet<string>{"N.Neow"})),
                    "different Compact occupancy uses an estimate without acquiring anchor measurement identity");
            }
            if(path.Contains("S-three"))
                Check(p.Stages.Single().PerInputTimeMs is >0,"READY public CPU allocation priced");
            if(path.Contains("R-three") || path.Contains("C-three"))
                Check(p.OrderedFamilies.All(cpu=>cpu.CpuWorkers==request.WorkerCount),"unpriced CPU configuration remains selected and executable");
            Console.WriteLine("CPU_INTEGRATION_CASE "+path+" "+p.FormatSummary());
        }
        var sw=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/S-three-shops.json");
        var rw=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/family-pricing-matrix-v1/R-three-common.json");
        var mixed=SearchPreparationCompiler.Compile(SearchCompiler.Compile(sw.Query with {RelicSequenceConstraints=rw.Query.RelicSequenceConstraints},context.Context),sw.Options).Plan!;
        MerchantShopColorlessFamily.TryCreate(mixed.Model, FilterExecutionPreferences.From(mixed.RunOptions), out var sg); RelicFamily.TryCreate(mixed.Model, FilterExecutionPreferences.From(mixed.RunOptions), out var rg);
        var sc=sg!.CpuAlternatives.First();
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(mixed,[sc,rg!])==FamilyExecutionCoordinator.ResolveExecutionWindowSize(mixed,[sg,rg!]),"CPU internal window never shrinks GPU root batch");
        var batch=new SearchBatch(12345,2*65536+3); var input=FamilyCandidateSet.Dense(batch);
        var reference=Cpu(mixed,"Check","ProfileRoot",r=>(r&7)==0);
        var numeric=Cpu(mixed,"Check","ByteRoot",r=>(r&7)==0,workers:4,windowCapacity:65536,numericalRoots:true);
        var execution=new FilterRuntimeServices(false);
        var expected=reference.FilterAsync(execution,input,CancellationToken.None).GetAwaiter().GetResult();
        var actual=numeric.FilterAsync(execution,input,CancellationToken.None).GetAwaiter().GetResult();
        execution.DisposeAsync().GetAwaiter().GetResult();
        Check(actual.ExportAbi1().Span.SequenceEqual(expected.ExportAbi1().Span),"two full CPU windows plus tail preserve global batch ordinals");
        CheckFailureIsolation(mixed, Check);
        CheckResumeManifest(Check);
        Console.WriteLine("CPU_INTEGRATION PASS checks="+checks);
        return 0;
    }

    private static CpuFilterExecution Cpu(PreparedSearch request, string id, string revision, Func<ulong, bool> matcher,
        int workers = 1, int windowCapacity = 8192, bool numericalRoots = false,
        Func<FamilyPhysicalQuoteRequest, FamilyPhysicalQuote?>? quote = null)
    {
        var body = new FilterBodyDescription(new PreparedFilter(id), revision, request.Model.ProfileId)
        { CpuWorkers = workers, WindowCapacity = windowCapacity,
            PerformanceProjection = compact => CpuFilterExecution.Describe(id, revision, workers, windowCapacity, numericalRoots, false, compact),
            Cost = new() { Quote = (_, geometry, _) => quote?.Invoke(geometry), CalibrationQuote = quote } };
        return new(body, RolltheSpire2.Compatibility.RuntimeProfileRegistry.Select(request.Model.Detection), matcher, numericalRoots);
    }

    private static void CheckFailureIsolation(PreparedSearch request, Action<bool, string> check)
    {
        var calibration = Cpu(request, "Calibration", "AuxiliaryQuoteFixture",
            _ => true, 2, 65536, quote: _ => throw new IOException("InjectedCpuCalibrationQuoteFailure"));
        var calibrationServices = new FilterRuntimeServices(false);
        var calibrationBatch = new SearchBatch(12345, 65536);
        for (int i = 0; i < 2; i++)
            check(calibration.FilterAsync(calibrationServices, FamilyCandidateSet.Dense(calibrationBatch), default).GetAwaiter().GetResult().Count == 65536,
                "CPU calibration quote failure preserves completed numerical output");
        check(!calibration.ExecutionFaulted, "Auxiliary calibration does not fault the CPU invocation");
        calibrationServices.DisposeAsync().GetAwaiter().GetResult();

        bool injectFailure = false;
        int calls = 0, active = 0;
        var prefix = new BoundaryProbe("Prefix", input => FamilyCandidateSet.FromSortedAbi1(input.Batch,
            input.EnumerateLogicalOrdinals().Where(ordinal => ordinal % 2 == 0).ToArray()));
        var broken = Cpu(request, "Broken", "PureMatcherFaultFixture", _ =>
        {
            Interlocked.Increment(ref calls); Interlocked.Increment(ref active);
            try { if (injectFailure) throw new IOException("InjectedPureCpuMatcherFailure"); return true; }
            finally { Interlocked.Decrement(ref active); }
        }, 2, 65536, quote: _ => null);
        var tail = new BoundaryProbe("Tail");
        IFilterExecution[] pipeline = [prefix, broken, tail];
        var execution = new FilterRuntimeServices(false) { CorrelationId = request.SnapshotFingerprint };
        try
        {
            var earlier = new SearchBatch(1000, 67);
            LinearFamilyPipeline.FilterAsync(execution, earlier, pipeline, default).GetAwaiter().GetResult();
            injectFailure = true;
            var failed = new SearchBatch(5000, 4096);
            var restored = LinearFamilyPipeline.FilterAsync(execution, failed, pipeline, default).GetAwaiter().GetResult();
            check(active == 0 && restored.Batch == failed && restored.Count == 2048 &&
                restored.ExportAbi1().Span.SequenceEqual(Enumerable.Range(0, 2048).Select(i => (ulong)i * 2).ToArray()),
                "CPU partitions join before restoring the exact immutable pre-family ABI1 input");
            var record = execution.CaptureFilterIsolations(failed).Single();
            check(record.UnfilteredCoverage.SequenceEqual(new[] { "Broken", "Tail" }) && tail.Calls == 1 &&
                record.Error.ToString().Contains("InjectedPureCpuMatcherFailure") &&
                record.FailedGlobalOrdinals.All(ordinal => ordinal >= failed.BatchBase && ordinal < failed.BatchBase + 4096),
                "Matcher isolation preserves cause and remaining obligations without executing a dependent tail");
            var warning = RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Snapshot().Single(issue =>
                issue.Code == "FamilyFilterIsolated" && issue.Source == "Broken" && issue.CorrelationId == request.SnapshotFingerprint);
            check(warning.Severity == RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueSeverity.Warning && warning.HasException &&
                warning.MessageKey == "issues.filter_incomplete" && warning.ExceptionDetail.Contains("InjectedPureCpuMatcherFailure"),
                "Actual filter computation failure keeps its warning and original cause with Exact enabled");
            int callsAfterFailure = calls;
            var later = new SearchBatch(10000, 67);
            LinearFamilyPipeline.FilterAsync(execution, later, pipeline, default).GetAwaiter().GetResult();
            check(calls == callsAfterFailure && execution.CaptureFilterIsolations(earlier).Count == 0 &&
                execution.CaptureFilterIsolations(later).Single() is { FinalValidationSkipped: false, FailureBatch: var origin } && origin == failed,
                "Isolated CPU filters are not retried; fixed session policy and batch-scoped causes cannot relabel earlier results");
            var badIdentity = new BoundaryProbe("BadIdentity", input => FamilyCandidateSet.Dense(new(input.Batch.BatchBase + 1, input.Count)));
            bool rejected = false;
            try { LinearFamilyPipeline.FilterAsync(execution, later, [badIdentity], default).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Untrusted output batch identity is never treated as a recoverable matcher failure");
        }
        finally { execution.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private sealed class BoundaryProbe(string id, Func<FamilyCandidateSet, FamilyCandidateSet>? filter = null) : IFilterExecution
    {
        internal int Calls;
        private FilterBodyDescription? _body;
        public FilterBodyDescription Body => _body ??= new(new PreparedFilter(id)
        { AnalyticalCost = new(id, 64, [], "CpuBoundaryFixture"), Survival = FamilySurvivalProjection.Unresolved(id, "CpuBoundaryFixture") },
            id, RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
        { PerformanceProjection = _ => new(id, id, "CpuBoundaryFixture", 1, "Fixture", false), CreateExecution = (_, _) => this };
        public static implicit operator FilterBodyDescription(BoundaryProbe probe) => probe.Body;
        public string FamilyId => id;
        public bool ExecutionFaulted => false;
        public ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices services, FamilyCandidateSet input, CancellationToken token)
        { Calls++; return ValueTask.FromResult(filter is null ? input : filter(input)); }
        public ValueTask DisposeAsync(FilterRuntimeServices services) => ValueTask.CompletedTask;
    }
}
