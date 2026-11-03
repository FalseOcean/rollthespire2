using System.Diagnostics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Diagnostics;

namespace RolltheSpire2.Search.FamilyExecution;

// Execution-local counters; observation facts and numerical evaluation stay
// immutable. An optional refinement fault retains the established union and
// reports its original cause; it never becomes a mask-zero observation.
internal sealed class NeowCapsuleSparseRefinementRun(NeowCapsuleSparseRefinement plan,string identity)
{
    private long _inputs,_primary,_primaryUnknown,_observations,_rejected,_nonzero,_unknown,_zero,_ticks;
    private int _faulted;
    internal bool Refine(ulong root)
    {
        Interlocked.Increment(ref _inputs);
        long started=Stopwatch.GetTimestamp();
        try {
            if(Volatile.Read(ref _faulted)!=0) { Interlocked.Increment(ref _unknown); return true; }
            var result=plan.Evaluate(root);
            Interlocked.Add(ref _observations,result.Observations);
            switch(result.Reason) {
                case "PrimaryPass": Interlocked.Increment(ref _primary); break;
                case "PrimaryUnknown": Interlocked.Increment(ref _primaryUnknown); break;
                case "ZeroPass": Interlocked.Increment(ref _zero); break;
                case "NonzeroRetained": Interlocked.Increment(ref _nonzero); break;
                case "Rejected": Interlocked.Increment(ref _rejected); break;
                default: Interlocked.Increment(ref _unknown); break;
            }
            return result.Keep;
        }
        catch(Exception error) when(error is not OutOfMemoryException and not OperationCanceledException) {
            Interlocked.Increment(ref _unknown);
            if(Interlocked.Exchange(ref _faulted,1)==0) {
                RuntimeLog.TryBackgroundWarning($"capsuleSparseRefinementFault=true;stage=N.SparseRefine;root={root:x16};identity={identity};unionRetained=true;refinementDisabled=true;exceptionJson="+RuntimeLog.SafeJson(error.ToString()));
                try { RuntimeIssueLedger.Current.Record(error,"CapsuleSparseRefinementFault",nameof(NeowCapsuleSparseRefinementRun),
                    "N.SparseRefine","issues.filter_incomplete",RuntimeIssueSeverity.Warning,identity); }
                catch(Exception diagnosticError) { RuntimeLog.TryBackgroundWarning("capsuleSparseRefinementDiagnosticFault=true;originalExceptionJson="+
                    RuntimeLog.SafeJson(error.ToString())+";diagnosticExceptionJson="+RuntimeLog.SafeJson(diagnosticError.ToString())); }
            }
            return true;
        }
        finally { Interlocked.Add(ref _ticks,Stopwatch.GetTimestamp()-started); }
    }
    internal FamilyCandidateSet Filter(FilterRuntimeServices services,FamilyCandidateSet input,IRuntimeProfile profile,
        CancellationToken token,out double milliseconds)
    {
        milliseconds=0;
        if(input.Count==0) return input;
        var watch=Stopwatch.StartNew();
        services.EnterCpu(token);
        try {
            var output=new List<ulong>(input.Count);
            foreach(ulong ordinal in input.EnumerateLogicalOrdinals()) {
                token.ThrowIfCancellationRequested();
                ulong root=profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile,input.Batch.GlobalCandidate(ordinal)));
                if(Refine(root)) output.Add(ordinal);
            }
            return output.Count==input.Count ? input : FamilyCandidateSet.FromSortedAbi1(input.Batch,output.ToArray());
        }
        finally { services.ExitCpu(); milliseconds=watch.Elapsed.TotalMilliseconds; }
    }
    internal void Report() => RuntimeLog.TryBackgroundInfo($"capsuleSparseRefinementSummary=true;revision={NeowCapsuleSparseRefinement.Revision};scope=PublicN;input={_inputs};primaryPass={_primary};primaryUnknown={_primaryUnknown};actualChecks={_observations};rejected={_rejected};nonzeroRetained={_nonzero};unknownRetained={_unknown};zeroPass={_zero};cpuRefineMs={_ticks*1000d/Stopwatch.Frequency};ready={plan.Ready};issue={plan.Issue};faulted={_faulted!=0};privateOrCoupledRefinement=false;originalUnionParityPreserved=true");

    internal static IFilterExecution WrapCpu(FilterBodyDescription body,IRuntimeProfile profile,Func<ulong,bool> union,
        bool numericalRoots,bool packedRoots,NeowCpuFourWayPreGate? gate,NeowCapsuleSparseRefinement? plan)
    {
        if(plan is null) return new CpuFilterExecution(body,profile,union,numericalRoots,packedRoots,gate);
        var run=new NeowCapsuleSparseRefinementRun(plan,body.InputIdentity);
        return new CpuFilterExecution(body,profile,root=>union(root)&&run.Refine(root),numericalRoots,packedRoots,gate,run.Report);
    }
}
