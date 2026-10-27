using System.Collections.Immutable;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Diagnostics;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckLanguageWorkerLifecycle(PredictorCrystalSnapshot source)
    {
        // A tiny detached board keeps the real Advance/retarget/worker lifecycle,
        // without turning this fault check into a language performance workload.
        var hidden=new bool[121];hidden[0]=true;
        var occupancy=Enumerable.Repeat(-1,121).ToArray();occupancy[0]=0;
        var board=source with { Remaining=1,PlacedAllItems=true,Hidden=hidden.ToImmutableArray(),
            Occupancy=occupancy.ToImmutableArray(),Items=[new("GOLD_SMALL",1,1,0,0,1,false)],Revealed=[] };
        ImmutableArray<CrystalRewardOption> selected=[CrystalRewardOption.AnyRelic];
        foreach(string transition in new[]{"retarget","close","cancel"})
        {
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            using var reported=new ManualResetEventSlim();using var stop=new CancellationTokenSource();
            var ledger=new RuntimeIssueLedger(write:(_,_)=>{});
            var expected=new InvalidOperationException("Language lifecycle "+transition,new Exception("Original Solve cause"));
            int solves=0;
            CancellationToken solveToken=default;
            var reach=new PredictorCrystalReachability(board,true,goalLanguageProof:true)
            {
                LanguageResultObserved=(_,_,_)=>
                {
                    Interlocked.Increment(ref solves);entered.Set();
                    if(!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Language lifecycle gate");
                    if(transition=="cancel") solveToken.ThrowIfCancellationRequested();
                    throw expected;
                },
                LanguageFailureObserved=(exception,scope)=>
                {
                    ledger.Record(exception,"CrystalLanguageWorkerFailed",nameof(PredictorCrystalReachability),
                        "LanguageWorker","runtime.issue.crystal_discovery_failed",correlationId:scope);
                    reported.Set();
                }
            };
            try
            {
                reach.Advance(selected,TimeSpan.FromMilliseconds(1),stop.Token);
                Check(entered.Wait(TimeSpan.FromSeconds(5)),"Controlled language worker entered "+transition);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                object owner=typeof(PredictorCrystalReachability).GetField("_activeLanguage",flags)!.GetValue(reach)!;
                var worker=(Task)owner.GetType().GetField("Worker",flags)!.GetValue(owner)!;
                solveToken=((CancellationTokenSource)owner.GetType().GetField("Cancel",flags)!.GetValue(owner)!).Token;
                if(transition=="retarget")
                    reach.Advance([new(PredictorRewardKind.Potion,new("POTION","LIFECYCLE_UNRELATED"))],TimeSpan.Zero);
                else stop.Cancel(); // The page closes/cancels and never polls the old query.
                Check(solveToken.IsCancellationRequested,"Retarget/close cancels the actual linked Solve token "+transition);
                Check(!worker.IsCompleted,"Cancellation and retarget do not wait for the old Solve");
                release.Set();
                Check(SpinWait.SpinUntil(()=>worker.IsCompleted,TimeSpan.FromSeconds(5)),"Old language worker terminates "+transition);
                Check(SpinWait.SpinUntil(()=>owner.GetType().GetField("Cancel",flags)!.GetValue(owner)==null,TimeSpan.FromSeconds(5)),
                    "Completed old language worker releases its linked cancellation source without another Advance "+transition);
                if(transition=="cancel")
                {
                    Check(worker.IsCanceled && ledger.Snapshot().Count==0 && !reported.IsSet,
                        "Ordinary language cancellation is not recorded as a failure");
                    reach.Advance(selected,TimeSpan.Zero);
                }
                else
                {
                    Check(reported.Wait(TimeSpan.FromSeconds(5)),"Abandoned language worker reports without an Advance "+transition);
                    var issue=ledger.Snapshot().Single();
                    Check(issue.ExceptionDetail.Contains(expected.Message) && issue.ExceptionDetail.Contains("Original Solve cause") &&
                        issue.CorrelationId.Contains("seed="+source.Context.Seed) && issue.CorrelationId.Contains("query=") &&
                        issue.CorrelationId.Contains("selected=") && issue.CorrelationId.Contains("target=selected"),
                        "Old worker retains the actual exception chain and original query/target context "+transition);
                    for(int attempt=0;attempt<2;attempt++)
                    {
                        bool original=false;
                        try { reach.Advance(selected,TimeSpan.FromMilliseconds(1)); }
                        catch(InvalidOperationException exception) { original=ReferenceEquals(exception,expected); }
                        Check(original && solves==1 && ledger.Snapshot().Single().OccurrenceCount==1,
                            "Failed language query rethrows its original failure without retrying or duplicating evidence "+transition);
                        Check(owner.GetType().GetField("Worker",flags)!.GetValue(owner)==null &&
                            owner.GetType().GetField("Cancel",flags)!.GetValue(owner)==null,
                            "Failed completed worker is cleaned before its original failure escapes "+transition);
                    }
                }
            }
            finally { release.Set(); }
        }
        int success=0,failures=0;
        PredictorCrystalReachability.StartLanguageWorker(()=>success++,CancellationToken.None,_=>failures++).GetAwaiter().GetResult();
        Check(success==1 && failures==0,"Successful worker result is unchanged");
        var unsolicited=new OperationCanceledException("Provider cancellation without a requested worker cancellation");
        Exception? recordedCancellation=null;bool unsolicitedPreserved=false;
        try { PredictorCrystalReachability.StartLanguageWorker(()=>throw unsolicited,CancellationToken.None,
            exception=>recordedCancellation=exception).GetAwaiter().GetResult(); }
        catch(OperationCanceledException exception) { unsolicitedPreserved=ReferenceEquals(exception,unsolicited); }
        Check(unsolicitedPreserved && ReferenceEquals(recordedCancellation,unsolicited),
            "Provider-only OperationCanceledException remains a recorded real failure when the worker token is not cancelled");
        var originalFailure=new InvalidOperationException("Language failure with broken reporter");
        bool preserved=false;
        try { PredictorCrystalReachability.StartLanguageWorker(()=>throw originalFailure,CancellationToken.None,
            _=>throw new Exception("Broken diagnostics")).GetAwaiter().GetResult(); }
        catch(InvalidOperationException exception) { preserved=ReferenceEquals(exception,originalFailure); }
        Check(preserved,"A failed diagnostic sink cannot replace the language computation failure");
        Console.WriteLine("CRYSTAL_LANGUAGE_WORKER_LIFECYCLE_PASS retarget=true;closedOwner=true;ordinaryCancellation=true;noRetry=true;originalCause=true;successUnchanged=true");
    }

    private static void CheckLanguageNegativeReuse(PredictorCrystalSnapshot board,CrystalReachabilityTable table,
        CrystalRewardOption[] options,Func<ImmutableArray<CrystalRewardOption>,bool> rawSupports,string shape,
        ImmutableArray<CrystalRewardOption> pair=default)
    {
        if(pair.IsDefault) pair=(from a in options from b in options where a!=b && rawSupports([a]) && rawSupports([b]) && !rawSupports([a,b])
            select ImmutableArray.Create(a,b)).First();
        var unrelated=options.First(o=>!pair.Contains(o));
        var shared=new PredictorCrystalOffsetBound(board,table,true);
        var priorLimited=shared.Check(pair,stateLimit:0);
        CrystalGoalSearchResult Query(string label,ImmutableArray<CrystalRewardOption> goals,PredictorCrystalOffsetBound owner,
            TimeSpan? budget=null,int limit=100_000)
        {
            var result=PredictorCrystalGoalLanguage.Solve(board,table,goals,true,budget??TimeSpan.FromSeconds(5),limit,sharedBound:owner);
            if(result.Verdict!=CrystalOffsetVerdict.Incomplete)
                Check((result.Plan!=null)==rawSupports(goals),"Reused language query agrees with full raw oracle: "+label);
            Console.WriteLine($"CRYSTAL_LANGUAGE_NEGATIVE_REUSE shape={shape};query={label};goals={goals.Length};verdict={result.Verdict};origin={result.NegativeKind};hit={result.NegativeCacheHit};ms={result.Seconds*1000:F3};states={result.RewardStates};geometry={result.GeometryNodes};verified={result.VerifiedWords}");
            return result;
        }
        var cold=Query("cold-pair",pair,shared);
        Check(cold.Verdict==CrystalOffsetVerdict.Impossible && !cold.NegativeCacheHit,"Cold completed negative is proved");
        if(cold.NegativeKind==CrystalLanguageNegativeKind.RewardDomain)
            Check(shared.Check(pair,stateLimit:0).Verdict==CrystalOffsetVerdict.Impossible,
                "Completed reward negative supersedes earlier Check receipt, including an Incomplete receipt");
        else Check(shared.Check(pair,stateLimit:0).Verdict==priorLimited.Verdict,"Joint negative does not become relaxed-domain proof");
        var repeated=Query("repeat-pair",pair,shared);
        Check(repeated.NegativeCacheHit && repeated.RewardStates==0 && repeated.GeometryNodes==0 && repeated.VerifiedWords==0,
            "Repeated negative avoids all compile/geometry/replay work");
        bool allOrders=cold.NegativeKind!=CrystalLanguageNegativeKind.OrderedQuery;
        var reverse=Query("permutation",pair.Reverse().ToImmutableArray(),shared);
        var stronger=Query("add-unrelated",pair.Add(unrelated),shared);
        Check(reverse.NegativeCacheHit==allOrders && stronger.NegativeCacheHit==allOrders,
            "Only all-orders negative supports permutation and stronger goal set reuse");
        var removed=Query("remove-essential",[pair[0]],shared);
        Check(removed.Verdict==CrystalOffsetVerdict.Possible && !removed.NegativeCacheHit,"Removing essential goal recovers reachable singleton");
        Check(Query("reselect",pair,shared).NegativeCacheHit,"Removing and reselecting preserves completed contradiction");

        var fresh=new PredictorCrystalOffsetBound(board,table,true);
        Check(Query("timeout",pair,fresh,TimeSpan.Zero).Verdict==CrystalOffsetVerdict.Incomplete,"Time cutoff is unknown");
        Check(Query("state-cutoff",pair,fresh,limit:0).Verdict==CrystalOffsetVerdict.Incomplete,"State cutoff is unknown");
        var partialOwner=new PredictorCrystalOffsetBound(board,table,true);
        var limited=Query("nonzero-cutoff",[pair[0]],partialOwner,limit:1);
        // Some already-revealed boards need only one state to prove this goal.
        // A budget is an upper bound, not a guarantee that work is incomplete.
        Check(limited.Verdict!=CrystalOffsetVerdict.Impossible &&
            partialOwner.KnownLanguageNegative([pair[0]])==CrystalLanguageNegativeKind.None,
            "Bounded reachable query stays unknown or yields a witness, never a cached negative");
        if(limited.Plan is { } limitedPlan) ReplayProof(board,limitedPlan);
        Check(Query("nonzero-cutoff-to-full",[pair[0]],partialOwner).Verdict==CrystalOffsetVerdict.Possible,"Partial compile does not exclude later reachable singleton");
        var full=Query("cutoff-to-full",pair,fresh);
        Check(full.Verdict==CrystalOffsetVerdict.Impossible && !full.NegativeCacheHit && full.RewardStates>0,"Cutoff did not cache a negative");
        using(var cancellation=new CancellationTokenSource())
        {
            cancellation.Cancel();bool cancelled=false;
            try {PredictorCrystalGoalLanguage.Solve(board,table,pair,true,TimeSpan.FromSeconds(5),token:cancellation.Token,sharedBound:fresh);}
            catch(OperationCanceledException) {cancelled=true;}
            Check(cancelled,"Cancellation remains observable even with a completed warm proof");
        }
        Check(Query("after-cancel",pair,fresh).NegativeCacheHit,"Cancellation preserves earlier completed facts");
        CheckLanguageContextIdentity(board,table,pair,(_,_)=>{});

        // Exercise the conservative ordered-query storage policy with a negative
        // independently certified by the complete raw oracle above. This is a
        // cache-routing check, not evidence that this fixture reaches that origin.
        var ordered=new PredictorCrystalOffsetBound(board,table,true);
        ordered.RememberLanguageNegative(pair,CrystalLanguageNegativeKind.OrderedQuery);
        Check(ordered.KnownLanguageNegative(pair)==CrystalLanguageNegativeKind.OrderedQuery &&
            ordered.KnownLanguageNegative(pair.Reverse().ToImmutableArray())==CrystalLanguageNegativeKind.None &&
            ordered.KnownLanguageNegative(pair.Add(unrelated))==CrystalLanguageNegativeKind.None &&
            ordered.KnownLanguageNegative([pair[0]])==CrystalLanguageNegativeKind.None,
            "Ordered query negatives never become all-orders or subset contradictions");
        Console.WriteLine($"CRYSTAL_LANGUAGE_NEGATIVE_REUSE_PASS shape={shape};rawEquality=true;orderedPolicyOnly=true;noProductionDispatch=true");
    }
}
