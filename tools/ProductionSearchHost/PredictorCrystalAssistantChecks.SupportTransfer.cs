using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static bool PackingCoverEnabled=>Environment.GetEnvironmentVariable("RT2_CRYSTAL_PACKING_COVER")=="1";
    private static void CheckSupportTransferPrevalence(PredictorCrystalSnapshot source,string output)
    {
        var watch=Stopwatch.StartNew();var table=new CrystalReachabilityTable(source,includeRerolls:true);
        double tableMs=watch.Elapsed.TotalMilliseconds;
        var bound=new PredictorCrystalOffsetBound(source,table,true);
        long liveBefore=GC.GetTotalMemory(true),before=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
        var rows=table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card)
            .Select(c=>new {carrier=c,contained=bound.ContainedCardSupports(c)}).ToArray();
        double mapMs=watch.Elapsed.TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        long mapAndRowsManagedLiveDelta=GC.GetTotalMemory(true)-liveBefore;GC.KeepAlive(bound);
        ProductionSearchReplay.Write(Path.Combine(output,"support-containment-"+source.Context.Seed+".json"),new {source,tableMs,mapMs,allocated,mapAndRowsManagedLiveDelta,entries=bound.SupportTransferEntries,rows});
        Console.WriteLine($"CRYSTAL_SUPPORT_CONTAINMENT seed={source.Context.Seed};cards={rows.Length};carriers={rows.Count(r=>!r.contained.IsEmpty)};relations={bound.SupportTransferEntries};tableMs={tableMs:F3};mapMs={mapMs:F3};allocatedBytes={allocated};mapAndRowsManagedLiveDelta={mapAndRowsManagedLiveDelta};fullKindsAndOffsets=true");
    }

    private static void CheckSupportTransferRaw(PredictorCrystalSnapshot board,CrystalReachabilityTable table,
        CrystalRewardOption[] options,Func<ImmutableArray<CrystalRewardOption>,bool> rawSupports,string shape)
    {
        var bound=new PredictorCrystalOffsetBound(board,table,true,packingCover:PackingCoverEnabled);
        var different=new HashSet<CrystalRewardOption>();var roots=new HashSet<string>();
        var directOrigins=new HashSet<CrystalLanguageNegativeKind>();
        ImmutableArray<CrystalRewardOption> certified=default;int questions=0,transfers=0,hits=0;
        for(int a=0;a<options.Length;a++) for(int b=a+1;b<options.Length;b++)
        {
            ImmutableArray<CrystalRewardOption> goals=[options[a],options[b]];
            var answer=PredictorCrystalGoalLanguage.Solve(board,table,goals,true,TimeSpan.FromSeconds(5),sharedBound:bound,rootCardSlots:RootCardSlotsEnabled);
            Check(answer.Verdict!=CrystalOffsetVerdict.Incomplete && (answer.Plan!=null)==rawSupports(goals),
                "Support-transfer sweep agrees with complete raw click/take/reroll truth: "+shape);
            questions++;hits+=answer.NegativeCacheHit?1:0;
            if(answer.Verdict!=CrystalOffsetVerdict.Impossible) continue;
            if(!answer.NegativeCacheHit) directOrigins.Add(answer.NegativeKind);
            certified=goals;
            for(int carrierIndex=0;carrierIndex<2;carrierIndex++)
            {
                var carrier=goals[carrierIndex];if(carrier.Kind!=PredictorRewardKind.Card) continue;
                ImmutableArray<CrystalRewardOption> selected=[goals[1-carrierIndex]];
                foreach(var d in bound.TransferSupportDenials(selected,carrier))
                {
                    Check(d!=carrier && !selected.Contains(d) && !bound.CardSupportEmpty(d),
                        "Collateral denial is a different nonempty unselected card");
                    Check(!rawSupports(selected.Add(d)),"Every derived root denial is absent from complete raw truth");
                    different.Add(d);roots.Add(string.Join('|',selected.Add(d).Select(g=>$"{g.Kind}:{g.Key.Serialized}:{g.UpgradeLevel}").Order()));transfers++;
                }
                Check(bound.TransferSupportDenials(goals,carrier).IsEmpty,"Cannot substitute an already selected carrier");
            }
        }
        Check(!certified.IsDefault,"Raw fixture provides a completed negative for transfer policy guards");
        var ordered=new PredictorCrystalOffsetBound(board,table,true);
        ordered.RememberLanguageNegative(certified,CrystalLanguageNegativeKind.OrderedQuery);
        foreach(var c in certified.Where(c=>c.Kind==PredictorRewardKind.Card))
            Check(ordered.TransferSupportDenials(certified.Where(g=>g!=c).ToImmutableArray(),c).IsEmpty,
                "Ordered replay failure never transfers by support containment");
        var card=options.First(c=>c.Kind==PredictorRewardKind.Card);
        var fresh=new PredictorCrystalOffsetBound(board,table,true);
        Check(PredictorCrystalGoalLanguage.Solve(board,table,[card],true,TimeSpan.Zero,sharedBound:fresh).Verdict==CrystalOffsetVerdict.Incomplete &&
            fresh.TransferSupportDenials([],card).IsEmpty,"Timeout supplies no transfer fact");
        Check(PredictorCrystalGoalLanguage.Solve(board,table,[card],true,TimeSpan.FromSeconds(5),stateLimit:0,sharedBound:fresh).Verdict==CrystalOffsetVerdict.Incomplete &&
            fresh.TransferSupportDenials([],card).IsEmpty,"State cutoff supplies no transfer fact");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool cancelled=false;
            try {PredictorCrystalGoalLanguage.Solve(board,table,[card],true,TimeSpan.FromSeconds(5),token:cancel.Token,sharedBound:fresh);}
            catch(OperationCanceledException) {cancelled=true;}
            Check(cancelled && fresh.TransferSupportDenials([],card).IsEmpty,"Cancellation supplies no transfer fact");
        }
        var certifiedCarrier=certified.First(c=>c.Kind==PredictorRewardKind.Card);
        Check(new PredictorCrystalOffsetBound(board,new CrystalReachabilityTable(board,includeRerolls:!table.IncludesRerolls),true)
            .TransferSupportDenials(certified.Where(g=>g!=certifiedCarrier).ToImmutableArray(),certifiedCarrier).IsEmpty,"Different table has no inherited transfer fact");
        CheckLanguageContextIdentity(board,table,certified,(_,_)=>{});
        Console.WriteLine($"CRYSTAL_SUPPORT_TRANSFER_RAW_PASS shape={shape};questions={questions};transfers={transfers};differentCards={different.Count};differentRoots={roots.Count};hits={hits};directOrigins={string.Join(',',directOrigins)};rawEquality=true;orderedPolicyOnly=true;cutoffsUnknown=true");
    }

    private static void CheckSupportTransferSweep(PredictorCrystalSnapshot source,ImmutableArray<CrystalRewardOption> selected,
        CrystalRewardOption? first,Action<ImmutableArray<PredictorCrystalStep>,CrystalSuffixNode> native,string output)
    {
        var tableWatch=Stopwatch.StartNew();var table=new CrystalReachabilityTable(source,includeRerolls:true);
        double tableMs=tableWatch.Elapsed.TotalMilliseconds;
        var candidates=table.Candidates.Where(c=>c.Kind==PredictorRewardKind.Card && !selected.Contains(c)).ToArray();
        if(first!=null) candidates=candidates.OrderBy(c=>c==first?0:1).ToArray();
        var receipts=new List<object>();var comparison=new Dictionary<CrystalRewardOption,CrystalGoalSearchResult>();
        bool reverse=Environment.GetEnvironmentVariable("RT2_CRYSTAL_GOAL_BATCH_FIRST")=="1";
        var modes=PackingCoverEnabled?new[]{(Root:true,Transfer:false,Packing:false),(Root:true,Transfer:false,Packing:true)}:
            RootCardSlotsEnabled?new[]{(Root:false,Transfer:false,Packing:false),(Root:true,Transfer:false,Packing:false),(Root:true,Transfer:true,Packing:false)}:
            new[]{(Root:false,Transfer:false,Packing:false),(Root:false,Transfer:true,Packing:false)};
        foreach(var mode in reverse?modes.AsEnumerable().Reverse():modes.AsEnumerable())
        {
            bool transfer=mode.Transfer;
            long allocatedBefore=GC.GetAllocatedBytesForCurrentThread(),liveBefore=GC.GetTotalMemory(true);
            var total=Stopwatch.StartNew();var bound=new PredictorCrystalOffsetBound(source,table,true,packingCover:mode.Packing);
            double mapMs=0;long mapAllocated=0;int relations=0;
            if(transfer && candidates.Length>0)
            {
                long allocation=GC.GetAllocatedBytesForCurrentThread();var map=Stopwatch.StartNew();
                bound.ContainedCardSupports(candidates[0]);mapMs=map.Elapsed.TotalMilliseconds;
                mapAllocated=GC.GetAllocatedBytesForCurrentThread()-allocation;relations=bound.SupportTransferEntries;
            }
            var differentClosed=new HashSet<CrystalRewardOption>();int nonemptyClosed=0,emptyClosed=0,hits=0,rootRejected=0,unknown=0,positive=0,negative=0;
            long states=0,geometry=0,verified=0;double nativeMs=0,transferMs=0;
            var rows=new List<object>();
            foreach(var candidate in candidates)
            {
                var goals=selected.Add(candidate);
                var budget=total.Elapsed-TimeSpan.FromMilliseconds(nativeMs)<TimeSpan.FromSeconds(20)?TimeSpan.FromSeconds(2):TimeSpan.Zero;
                var answer=PredictorCrystalGoalLanguage.Solve(source,table,goals,true,budget,sharedBound:bound,rootCardSlots:mode.Root);
                if(comparison.TryGetValue(candidate,out var prior))
                {
                    if(prior.Verdict!=CrystalOffsetVerdict.Incomplete && answer.Verdict!=CrystalOffsetVerdict.Incomplete)
                        Check(prior.Verdict==answer.Verdict && JsonSerializer.Serialize(prior.Plan,ProductionSearchReplay.JsonOptions)==
                            JsonSerializer.Serialize(answer.Plan,ProductionSearchReplay.JsonOptions),"Full candidate sweep retains verdict and plan");
                }
                else comparison.Add(candidate,answer);
                if(answer.Verdict!=CrystalOffsetVerdict.Incomplete) comparison[candidate]=answer;
                ImmutableArray<CrystalRewardOption> derived=[];
                if(transfer && answer.Verdict==CrystalOffsetVerdict.Impossible && !answer.NegativeCacheHit)
                {
                    var proof=Stopwatch.StartNew();derived=bound.TransferSupportDenials(selected,candidate);
                    transferMs+=proof.Elapsed.TotalMilliseconds;
                    foreach(var d in derived)
                    {
                        Check(d!=candidate && !selected.Contains(d),"Transfer closes a different unselected candidate");
                        if(differentClosed.Add(d)) {if(bound.CardSupportEmpty(d)) emptyClosed++;else nonemptyClosed++;}
                    }
                }
                if(answer.Plan is {} plan)
                {var replay=Stopwatch.StartNew();native(plan.Steps,ReplayProof(source,plan));nativeMs+=replay.Elapsed.TotalMilliseconds;}
                hits+=answer.NegativeCacheHit?1:0;unknown+=answer.Verdict==CrystalOffsetVerdict.Incomplete?1:0;
                rootRejected+=answer.RootCardSlotsRejected?1:0;
                positive+=answer.Verdict==CrystalOffsetVerdict.Possible?1:0;negative+=answer.Verdict==CrystalOffsetVerdict.Impossible?1:0;
                states+=answer.RewardStates;geometry+=answer.GeometryNodes;verified+=answer.VerifiedWords;
                rows.Add(new {candidate,answer,derived});
                if(!derived.IsEmpty || candidate==first)
                    Console.WriteLine($"CRYSTAL_SUPPORT_TRANSFER_PROOF seed={source.Context.Seed};transfer={transfer};carrier={candidate};origin={answer.NegativeKind};ms={answer.Seconds*1000:F3};differentClosed={derived.Length};derived={string.Join('|',derived)}");
            }
            // Retarget both contexts after the identical whole-candidate sequence.
            // Final-negative repeat benefit already exists in BOTH modes.
            var retargets=new List<object>();
            foreach(var goals in new[]{selected,selected.Add(candidates[0]),selected.Add(candidates[^1]),selected.Add(candidates[0]).Reverse().ToImmutableArray()})
            {
                var result=PredictorCrystalGoalLanguage.Solve(source,table,goals,true,TimeSpan.FromSeconds(2),sharedBound:bound,rootCardSlots:mode.Root);
                if(result.Plan is {} plan) {var replay=Stopwatch.StartNew();native(plan.Steps,ReplayProof(source,plan));nativeMs+=replay.Elapsed.TotalMilliseconds;}
                retargets.Add(new {goals,result});
                states+=result.RewardStates;geometry+=result.GeometryNodes;verified+=result.VerifiedWords;
            }
            double totalMs=total.Elapsed.TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
            long liveDelta=GC.GetTotalMemory(true)-liveBefore;GC.KeepAlive(bound);
            double algorithmMs=totalMs-nativeMs;
            Console.WriteLine($"CRYSTAL_SUPPORT_TRANSFER_SWEEP seed={source.Context.Seed};root={mode.Root};transfer={transfer};packing={mode.Packing};coverNodes={bound.CoverSearchNodes};packingChecks={bound.PackingChecks};packingRejected={bound.PackingRejected};candidates={candidates.Length};positive={positive};negative={negative};unknown={unknown};differentClosed={differentClosed.Count};nonemptyClosed={nonemptyClosed};emptyClosed={emptyClosed};rootRejected={rootRejected};hits={hits};states={states};geometry={geometry};verified={verified};tableMs={tableMs:F3};mapMs={mapMs:F3};transferMs={transferMs:F3};mapLookups={bound.SupportMapLookups};learnCalls={bound.SupportTransferCalls};probes={bound.SupportTransferProbes};algorithmMs={algorithmMs:F3};totalWithNativeMs={totalMs:F3};mapAllocatedBytes={mapAllocated};allocatedBytes={allocated};managedLiveDelta={liveDelta};relations={relations}");
            receipts.Add(new {source,selected,root=mode.Root,transfer,packing=mode.Packing,bound.CoverSearchNodes,bound.PackingChecks,bound.PackingRejected,tableMs,mapMs,transferMs,totalMs,nativeMs,algorithmMs,allocated,mapAllocated,liveDelta,relations,
                candidates=candidates.Length,positive,negative,unknown,differentClosed,nonemptyClosed,emptyClosed,rootRejected,hits,states,geometry,verified,
                mapLookups=bound.SupportMapLookups,learnCalls=bound.SupportTransferCalls,probes=bound.SupportTransferProbes,rows,retargets});
        }
        ProductionSearchReplay.Write(Path.Combine(output,"support-sweep-"+source.Context.Seed+".json"),receipts);
    }
}
