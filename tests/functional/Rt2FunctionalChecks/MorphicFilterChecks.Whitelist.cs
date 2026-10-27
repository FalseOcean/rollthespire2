using System.Text.Json;
using Godot;
using Environment = System.Environment;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Random;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class MorphicFilterChecks
{
    private static void EventWhitelistClosure(SearchContext context, MorphicGroveScenario donor, Action<bool,string> check)
    {
        var kinds = new[] { EventResultConditionKind.SymbioteInitialBasicTransform, EventResultConditionKind.AromaOfChaosInitialBasicTransform,
            EventResultConditionKind.WhisperingHollowInitialBasicTransform, EventResultConditionKind.TrialNondescriptInitialBasicsContains };
        var pool = donor.Targets[0].OrderedSourceCandidates!;
        var a = pool[0].CardKey; var b = pool[1].CardKey;
        EventResultSearchCondition Transform(EventResultConditionKind k, ModelKey? second = null) => new(k,a) {
            MorphicGroveSecondCard=second,
            MorphicGroveScenario=new(donor.Authority,donor.Premises,donor.Targets.Take(EventResultTransformSemantics.DrawCount(k)).ToArray()) };
        var cases = kinds.Select(k => new[] {Transform(k)}).ToList();
        ModelKey duplicate = a;
        for(ulong i=0;i<4099;i++) {
            ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
            var rng=new Rng(unchecked(root+StringHelper.GetDeterministicHashCode("TRIAL")));
            if(rng.NextInt(3)!=2) continue;
            var first=pool[rng.NextInt(pool.Count)].CardKey; var second=pool[rng.NextInt(pool.Count)].CardKey;
            if(first==second) {duplicate=first;break;}
        }
        cases.Add([Transform(kinds[3], duplicate) with {TargetKey=duplicate}]); cases.Add([Transform(kinds[3], b)]);
        foreach(var trial in Enum.GetValues<TrialCaseTarget>()) cases.Add([new(EventResultConditionKind.TrialCase,new("EVENT","TRIAL")) {TrialCase=trial}]);
        foreach(var type in Enum.GetValues<TinkerCardTypeTarget>())
        {
            cases.Add([new(EventResultConditionKind.TinkerTimeTypeAndRider,new("CARD","MAD_SCIENCE")) {TinkerCardType=type}]);
            foreach(var rider in Enum.GetValues<TinkerRiderTarget>().Where(r => (int)r/3==(int)type))
                cases.Add([new(EventResultConditionKind.TinkerTimeTypeAndRider,new("CARD","MAD_SCIENCE")) {TinkerCardType=type,TinkerRider=rider}]);
        }
        cases.Add([Transform(kinds[3]),new(EventResultConditionKind.TrialCase,new("EVENT","TRIAL")) {TrialCase=TrialCaseTarget.Nondescript}]);
        cases.Add([Transform(kinds[3]),new(EventResultConditionKind.TrialCase,new("EVENT","TRIAL")) {TrialCase=TrialCaseTarget.Merchant}]);
        cases.Add([Transform(kinds[0]),new(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0])]);
        cases.Add([new(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0])]);
        cases.Add([new(EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics[0])]);
        cases.Add([new(EventResultConditionKind.ColorfulPhilosophersOfferedColor,new("CHARACTER","SILENT"))]);
        var opts = new SearchRunOptions("000000000000", 4099, int.MaxValue, 1);
        ExactSearchExecutionRequest Request(SearchQuery q) => ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q,context),opts).Plan!;
        RenderingDevice? rd = Environment.GetEnvironmentVariable("RT2_EVENT_WHITELIST_GPU") == "1" ? RenderingServer.CreateLocalRenderingDevice() : null;
        if (Environment.GetEnvironmentVariable("RT2_EVENT_WHITELIST_GPU") == "1") check(rd is not null,"Vulkan available");
        int totalHits=0; var fingerprints=new HashSet<string>();
        try
        {
            for(int ci=0;ci<cases.Count;ci++)
            {
                var query=SearchQuery.Empty with {EventResultConditions=cases[ci]};
                var request=Request(query); var plan=new EventResultFamilyPlan(request);
                check(plan.GpuSupported,"Whitelist GPU closure");
                check(fingerprints.Add(request.SnapshotFingerprint),"Distinct typed event fingerprint");
                var json=JsonSerializer.Serialize(query,ProductionSearchReplay.JsonOptions);
                check(Request(JsonSerializer.Deserialize<SearchQuery>(json,ProductionSearchReplay.JsonOptions)!).SnapshotFingerprint==request.SnapshotFingerprint,"Whitelist JSON roundtrip");
                var accepted=new List<ulong>();
                for(ulong i=0;i<(ulong)opts.ScanCount;i++)
                {
                    var root=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,Beta110SeedCodec.FormatOrdinal(i));
                    bool expected=cases[ci].All(c=>SourceTruth(root.RootHash,c,plan));
                    var exact=ProductionExactSearchEvaluator.Evaluate(request,root);
                    check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch,"Whitelist Exact resolved");
                    check(plan.Matches(root.RootHash)==expected && plan.Reference(root.RootHash)==expected && exact.IsMatch==expected,"Vanilla RNG vs CPU vs Exact");
                    if(expected) accepted.Add(i);
                }
                var batch=new SearchBatch(0,(int)opts.ScanCount); var dense=FamilyCandidateSet.Dense(batch);
                EventResultFamily.TryCreate(request,out var family);
                var cpu=family!.CpuRealizations.Last();
                var execution=new FamilyExecutionContext(false);
                check(cpu.InvokeAsync(execution,new(batch,request),dense,default).GetAwaiter().GetResult().EnumerateLogicalOrdinals().SequenceEqual(accepted),"E CPU ABI1");
                using(var cancelled=new CancellationTokenSource()) {
                    cancelled.Cancel();
                    try {cpu.InvokeAsync(execution,new(batch,request),dense,cancelled.Token).GetAwaiter().GetResult();throw new Exception("Missing cancellation");}
                    catch(OperationCanceledException) {check(true,"E cancellation");}
                }
                cpu.DisposeAsync(execution).GetAwaiter().GetResult(); execution.DisposeAsync().GetAwaiter().GetResult();
                if(rd is not null)
                {
                    using var gpu=new EventResultFamilyGpuExecutor(rd,plan);
                    check(gpu.Execute(dense,default,out _).EnumerateLogicalOrdinals().SequenceEqual(accepted),"E Vulkan dense full population");
                    var sparse=FamilyCandidateSet.FromSortedAbi1(batch,Enumerable.Range(0,(int)opts.ScanCount).Where(i=>i%3==0).Select(i=>(ulong)i).ToArray());
                    check(gpu.Execute(sparse,default,out _).EnumerateLogicalOrdinals().SequenceEqual(accepted.Where(i=>i%3==0)),"E Vulkan compact full population");
                    check(gpu.Execute(FamilyCandidateSet.FromSortedAbi1(batch,[]),default,out _).Count==0,"E Vulkan zero");
                }
                totalHits+=accepted.Count;
                if(ci==4) check(accepted.Count>0,"Positive repeated-card Trial witness");
                if(ci==21) check(Math.Abs(plan.Survival.SurvivalProbability!.Value-new EventResultFamilyPlan(Request(query with {EventResultConditions=[cases[ci][0]]})).Survival.SurvivalProbability!.Value)<1e-12,"Trial case counted once");
                if(ci==22) check(plan.Survival.SurvivalProbability==0,"Trial incompatible case has zero reach");
                Console.WriteLine($"EVENT_WHITELIST case={ci};kind={string.Join('+',cases[ci].Select(c=>c.Kind))};hits={accepted.Count};gpu={rd is not null}");
            }
        }
        finally {rd?.Free();}
        foreach(var k in kinds.Take(3)) {
            var c=Transform(k); var unknown=c with {MorphicGroveScenario=new(donor.Authority,donor.Premises with {VanillaLocalDependenciesBound=false},c.MorphicGroveScenario!.Targets)};
            var rq=Request(SearchQuery.Empty with {EventResultConditions=[unknown]});
            check(new EventResultFamilyPlan(rq).Matches(12),"Unknown cannot Fast reject");
            check(ProductionExactSearchEvaluator.Evaluate(rq,TrustedRootHashInput.RootOnly(12)).Evaluation.Disposition==SearchDisposition.Unknown,"Unknown cannot become Exact Match");
        }
        check(!(cases[12][0] with {TinkerCardType=TinkerCardTypeTarget.Attack,TinkerRider=TinkerRiderTarget.Curious}).IsValid,"Cross-type rider invalid");
        foreach(var k in kinds.Skip(1).Take(2)) {
            var c=Transform(k);
            var t=new TransformationAggregateCondition(TransformationOpening.None,TransformationPickupOrder.Any,false,
                k==EventResultConditionKind.AromaOfChaosInitialBasicTransform,k==EventResultConditionKind.WhisperingHollowInitialBasicTransform,
                TransformationAggregatePredicate.RareCountAtLeast,1,[]) {EventScenario=c.MorphicGroveScenario};
            bool rejected=false;
            try {Request(SearchQuery.Empty with {EventResultConditions=[c],TransformationAggregate=t});}
            catch(ArgumentException e) {rejected=e.Message=="TransformationAggregate.EventResultOwnershipConflict";}
            check(rejected,"T/E cannot double-own a transform source");
        }
        Console.WriteLine($"EVENT_WHITELIST_PASS cases={cases.Count};roots={opts.ScanCount};accepted={totalHits};sourceRng=vanilla;GPU={rd is not null}");
    }

    private static bool SourceTruth(ulong root, EventResultSearchCondition c, EventResultFamilyPlan plan)
    {
        if(EventResultTransformSemantics.IsTransform(c.Kind)) {
            var entry=EventResultTransformSemantics.Entry(c.Kind);
            var rng=new Rng(unchecked(root+StringHelper.GetDeterministicHashCode(entry)));
            if(entry=="WHISPERING_HOLLOW") rng.NextInt(-9,10);
            if(entry=="TRIAL" && rng.NextInt(3)!=2) return false;
            var outputs=c.MorphicGroveScenario!.Targets.Select(t=>t.OrderedSourceCandidates![rng.NextInt(t.OrderedSourceCandidates.Count)].CardKey).ToList();
            if(!outputs.Remove(c.TargetKey)) return false;
            return c.MorphicGroveSecondCard is not { } b || outputs.Remove(b);
        }
        if(c.Kind==EventResultConditionKind.TrialCase) return new Rng(unchecked(root+StringHelper.GetDeterministicHashCode("TRIAL"))).NextInt(3)==(int)c.TrialCase!;
        if(c.Kind==EventResultConditionKind.TinkerTimeTypeAndRider) {
            var rng=new Rng(unchecked(root+StringHelper.GetDeterministicHashCode("TINKER_TIME")));
            if(!new[]{0,1,2}.TakeRandom(2,rng).Contains((int)c.TinkerCardType!)) return false;
            int type=(int)c.TinkerCardType!;
            return c.TinkerRider is null || new[]{type*3,type*3+1,type*3+2}.TakeRandom(2,rng).Contains((int)c.TinkerRider);
        }
        var p=Beta111EventResultProjector.Project(root,0,plan.Authority);
        return c.Kind switch {
            EventResultConditionKind.TrashHeapGrabCard=>p.TrashHeapGrabCard==c.TargetKey,
            EventResultConditionKind.TrashHeapDiveRelic=>p.TrashHeapDiveRelic==c.TargetKey,
            EventResultConditionKind.FakeMerchantOfferedFakeRelic=>p.FakeMerchantInventory.Contains(c.TargetKey),
            EventResultConditionKind.ColorfulPhilosophersOfferedColor=>p.ColorfulOfferedColors.Contains(c.TargetKey), _=>throw new Exception("Unexpected fixture") };
    }
}
