using System.Diagnostics;
using RolltheSpire2.Search.Semantics;
using System.Text.Json;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

internal static class CpuCostClosureChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
        CpuLocalPeak Peak(string shape,int workers,double ratio)
        {
            string revision=FamilyCpuReferenceCost.Revision(shape)+$".RootMode2.P{workers}.B65536.Dense.CanonicalAbi1Ready";
            long inputs=16777216;
            return new(CpuCostCalibration.DeviceKey(),CpuCostCalibration.Key(shape,revision,workers),shape,revision,workers,
                inputs,inputs*FamilyCpuReferenceCost.ReferenceNs(shape)!.Value*ratio/1e6,DateTimeOffset.UtcNow);
        }
        var p1=Peak("N.BonesIdentityTwoSource.P1",1,2);
        var p1b=Peak("S.ThreeUncommonSlots.P1",1,4);
        var p8=Peak("R.ShopOrdered3.P8",8,.5);
        var empty=new CpuCostSnapshot([]);
        Check(empty.Resolve(p1.Key,1)==(1d,"ReferenceDefault"),"Reference default needs no local benchmark");
        var snap=new CpuCostSnapshot([p1,p1b,p8]);
        Check(snap.Resolve(p1.Key,1)==(2d,"Condition"),"same condition wins");
        Check(snap.Resolve("unseen",1)==(3d,"Global"),"P1 median fallback");
        Check(snap.Resolve("unseen",8)==(.5,"Global"),"P8 independent fallback");
        Check(snap.Resolve("unseen",16)==(1d,"ReferenceDefault"),"P1/P8 cannot manufacture P16 evidence");
        Check(new CpuCostSnapshot([p1 with {Revision="old"}]).Resolve("unseen",1).Ratio==1,"old revision cannot contaminate global");
        const string oldSlotRevision="S.MerchantShopColorless.Cpu.Slot.20260912.v1.RootMode2.P1.B65536.Dense.CanonicalAbi1Ready";
        var oldSlot=p1b with {Revision=oldSlotRevision,Key=CpuCostCalibration.Key(p1b.Shape,oldSlotRevision,1),
            CanonicalMs=p1b.Inputs*55d/1e6};
        var staleSnapshot=new CpuCostSnapshot([oldSlot]);
        Check(!CpuCostCalibration.Valid(oldSlot),"real old strict slot revision with correct key is stale after reanchoring");
        Check(staleSnapshot.Resolve(oldSlot.Key,1)==(1d,"ReferenceDefault") &&
            staleSnapshot.Resolve("unseen",1)==(1d,"ReferenceDefault"),"stale software revision contributes neither condition nor hardware global ratio");
        Check(new CpuCostSnapshot([p1 with {CanonicalMs=1}]).Resolve("unseen",1).Ratio==1,"insufficient owned work rejected");
        Check(new CpuCostSnapshot([p1,p1 with {CanonicalMs=p1.CanonicalMs*2}]).Resolve(p1.Key,1).Ratio==2,"best eligible cost retained");
        var raw=new FamilyPhysicalQuote("N.Neow",p1.Shape,29,65536,null,"fixture",PublicTransportClass:"CpuOrderedAbi1");
        var local=snap.Local(raw,p1.Revision,1,false);
        Check(local.NanosecondsPerInput==58 && local.GpuCostRatio==1,"CPU ratio only scales CPU owned quote");
        var gpu=new GpuCostSnapshot([new("g","GPU","g",1000,1000,1000,DateTimeOffset.UtcNow)],_=>4000);
        Check(gpu.GlobalRatio==4 && snap.Resolve(p1.Key,1).Ratio==2,"CPU and GPU independent simulated devices");
        var compact=Peak("R.DirectLargeUR.P8",8,1);
        string cr=compact.Revision.Replace(".Dense.",".CompactAbi1.");
        compact=compact with {Revision=cr,Key=CpuCostCalibration.Key(compact.Shape,cr,8,true),CompactInput=true,
            Inputs=655360,RootBatches=100,ModeledInput=6553.6,CanonicalMs=69.46816};
        Check(CpuCostCalibration.Valid(compact),"compact actual-input boundary with expected occupancy");
        Check(!CpuCostCalibration.Valid(compact with {RootBatches=200}),"incompatible compact population rejected as evidence only");

        var context=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        checks += CheckGenericScalarQuotes(context.Context);
        checks += CheckRelicQuoteIdentities(context.Context);
        checks += CheckPartyRelicQuoteIdentities();
        ExactSearchExecutionRequest Request(string id,int budget,long count=1<<24)
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/cpu-main-v1/{id}.json");
            return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,context.Context),w.Options with {WorkerCount=budget,ScanCount=count,TargetMatchCount=30}).Plan!;
        }
        foreach(int budget in new[]{1,4,8,16})
        {
            var r=Request("R-shop-three",budget);
            RelicFamily.TryCreate(r,out var f);
            var configs=f!.CpuRealizations.Cast<FamilyCpuExecution>().Select(c=>c.Workers).ToArray();
            Check(configs.All(n=>n<=budget) && configs.Contains(1),"budget bounds physical candidates");
            Check(configs.SequenceEqual(Enumerable.Range(1,budget)),"all integer worker counts available independently of price");
        }
        foreach(var id in new[]{"N-identity-source","S-three-slots","R-three-common","R-shop-three","C-three-potions"})
        {
            var r=Request(id,1);var plan=FamilyExecutionCoordinator.Plan(r,false);
            Check(plan.OrderedFamilies.All(f=>f is FamilyCpuExecution),"CPU Only preserved");
            if (id.StartsWith("R-", StringComparison.Ordinal) && plan.OrderedFamilies.OfType<FamilyCpuExecution>().Any(c =>
                !FamilyCpuReferenceCost.RelicIdentitySupported(c.Condition(false).PhysicalImplementationRevision, c.WindowCapacity, c.Workers, false)))
            {
                Check(plan.OrderedFamilies.Count > 0 && plan.OrderedFamilies.All(f =>
                    f.QuotePhysicalWork(new(false, false, 65536)) is not { } quote ||
                    FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, f, quote, 65536)),
                    "New R CPU bodies stay unpriced unless their actual identity and input have an embedded canonical measurement");
                continue;
            }
            if(id=="N-identity-source")continue; // source-conditioned R has no complete price
            var eta=FamilySearchEtaProjectorV1.Project(r,plan,liveExactMsPerAttempt:.01);
            Console.WriteLine(id+" "+eta.FormatSummary());
            Check(eta.Status==FamilySearchEtaStatus.Ready && eta.TargetSearchMeanMs is >0,"Reference CPU produces estimate "+id);
            Check(eta.TargetExactTailMs>=0,"Exact retains independent local timing");
        }
        var shortRequest=Request("S-three-slots",16,4096);
        var shortPlan=FamilyExecutionCoordinator.Plan(shortRequest,false);
        Check(shortPlan.Stages.Single().PerInputTimeMs is >0,"measured actual short input priced");
        var longRequest=Request("S-three-slots",16);
        var longPlan=FamilyExecutionCoordinator.Plan(longRequest,false);
        Check(longPlan.Stages.Single().PerInputTimeMs == shortPlan.Stages.Single().PerInputTimeMs,"unit scheduling reference is independent of requested scan horizon");
        var tiny=Request("R-shop-three",16,128);
        var tinyPlan=FamilyExecutionCoordinator.Plan(tiny,false);
        Check(tinyPlan.OrderedFamilies.Count > 0 && tinyPlan.OrderedFamilies.All(family => family.QuotePhysicalWork(new(false, false, 128)) is null),
            "an actual unmeasured tiny invocation stays unpriced while its full-window ranking reference remains usable");
        var tinyEta = FamilySearchEtaProjectorV1.Project(tiny,tinyPlan,liveExactMsPerAttempt:.01);
        Check(tinyEta.Status==FamilySearchEtaStatus.Ready && tinyEta.TargetStages.Any(stage =>
            stage.CostEvidence.Contains("ReferenceGeometryApproximation", StringComparison.Ordinal)),
            "tiny input uses an explicitly approximate estimate from its existing same-body reference");
        var rprice=Request("R-three-common",1);
        var rplan=FamilyExecutionCoordinator.Plan(rprice,false);
        var exactReference = FamilySearchEtaProjectorV1.Project(longRequest, longPlan, liveExactMsPerAttempt: .02);
        var exactDouble = FamilySearchEtaProjectorV1.Project(longRequest, longPlan, liveExactMsPerAttempt: .04);
        Check(exactReference.TargetExactTailMs is > 0 &&
            exactDouble.TargetExactTailMs == 2 * exactReference.TargetExactTailMs,
            "Exact local cost scales only with its independent observed per-attempt cost");
        var noQuote = tinyPlan;
        var relicUi=FamilySearchEtaProjectorV1.Project(rprice,rplan,liveExactMsPerAttempt:.01);
        bool oldRelicIdentity=rplan.OrderedFamilies.OfType<FamilyCpuExecution>().All(c =>
            FamilyCpuReferenceCost.RelicIdentitySupported(c.Condition(false).PhysicalImplementationRevision,c.WindowCapacity,c.Workers,false));
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Build(relicUi).Status == (oldRelicIdentity
            ? RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.Ready : RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.Unavailable),
            "R estimate presentation follows actual body pricing without blocking execution");
        // Keep the generic Ready/live presentation checks on an independently priced fixture.
        var readyUi=FamilySearchEtaProjectorV1.Project(longRequest,longPlan,liveExactMsPerAttempt:.01);
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Build(readyUi).Status==RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.Ready,"reference estimate UI is Ready");
        var waitingUi=readyUi with {Status=FamilySearchEtaStatus.Calibrating,FirstResultSearchMeanMs=null,FirstResultSearchP99Ms=null,TargetSearchMeanMs=null};
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Build(waitingUi).Status==RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.Calibrating,"local evidence presentation state");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Build(FamilySearchEtaProjectorV1.Project(tiny,noQuote,liveExactMsPerAttempt:.01)).Status==RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.Ready,"explicit reference estimate is presented without claiming a measured tiny quote");
        var rolling=new RolltheSpire2.Presentation.Ui1.SearchScanningSpeed();
        Check(rolling.Observe(0,0) is null && rolling.Observe(3,100) is null && rolling.Observe(3.5,200) is null,"cold/warm speed waits for actual window");
        Check(Math.Abs(rolling.Observe(4.1,320)!.Value-200)<.001,"rolling speed excludes startup");
        var progress=new SearchProgressSnapshot(SearchRunState.Completed,1000,1,1000,1,10,100,SearchDisposition.Match,"",false)
            {ObservedScanningRoots=500,ObservedScanningSeconds=.1};
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress,null)==5000,"short terminal speed excludes startup");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {State=SearchRunState.Running},null) is null,"running waits for rolling window");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {ObservedScanningRoots=0},null) is null,"no invented speed without observation");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {ObservedScanningRoots=0},12345) is null,"terminal cannot borrow stale rolling speed");
        Check(RolltheSpire2.Presentation.Ui1.SearchScanningSpeed.Terminal(progress with {State=SearchRunState.Faulted},null)==5000,"faulted session may show completed scanning evidence");
        var etaFrozen = RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Build(readyUi) with {
            AcceptedResultSurvivalRate = 1e-9, TargetCount = 30, FirstResultMeanMs = 2000 };
        var running = progress with { State = SearchRunState.Running, TargetMatchCount = 30, MatchCount = 7 };
        var emptyEta = RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Build(
            readyUi with { AcceptedResultProbabilityEvidence = "Joint.NoPredicates", AcceptedResultProbability = 1 });
        Check(emptyEta.Status == RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.NoConditions &&
            emptyEta.FirstResultMeanMs is null && emptyEta.TargetMeanMs is null, "empty query hides numeric ETA");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(emptyEta, running, 1e9) == emptyEta,
            "live speed cannot restore an empty-query ETA");
        var live = RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen, running, 1e9);
        Check(live.IsLive && Math.Abs(live.FirstResultMeanMs!.Value-1000)<.001 && live.RemainingTargetCount==23 &&
            Math.Abs(live.TargetMeanMs!.Value-23000)<.001,"live uses etaFrozen probability and accepted target progress");
        Check(etaFrozen.FirstResultMeanMs==2000 && !etaFrozen.IsLive,"presentation never mutates predicted snapshot");
        foreach (double? invalid in new double?[]{null,0,-1,double.NaN,double.PositiveInfinity})
            Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen,running,invalid)==etaFrozen,"no stable speed retains predicted");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen,running,.25e9).FirstResultMeanMs>etaFrozen.FirstResultMeanMs,
            "backpressure/slower completed-root wall-rate lengthens UI ETA");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen,running with {ScannedCount=long.MaxValue},1e9)==live,
            "scanned roots never subtract from rare-event expected work");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen,running with {MatchCount=30},1e9).TargetMeanMs==0,
            "completed target has zero remaining work");
        foreach(var terminal in new[]{SearchRunState.Completed,SearchRunState.Cancelled,SearchRunState.Faulted})
            Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen,running with {State=terminal},1e9)==etaFrozen,
                "terminal restores explicitly predicted view");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen with {AcceptedResultSurvivalRate=null},running,1e9).IsLive==false,
            "speed cannot invent missing probability");
        var unavailableUi=etaFrozen with {Status=RolltheSpire2.Presentation.Ui1.SearchEtaQuickViewStatus.Unavailable,FirstResultMeanMs=null};
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(unavailableUi,running,1e9).IsLive,
            "UI can project live despite missing offline price; Planner remains unpriced");
        Check(RolltheSpire2.Presentation.Ui1.SearchEtaPresentationBuilder.Live(etaFrozen with {AcceptedResultSurvivalRate=1e-20},running,1e9).FirstResultP99Ms is >0,
            "rare probability rounding does not lose p99");

        string captured=".rewrite-build-evidence/production-replay/runtime-catalog/contexts";
        if(Directory.Exists(captured))
        foreach(string path in Directory.GetFiles(captured,"*-a10.json"))
        {
            var character=ProductionSearchReplay.Read<ProductionSearchReplayContext>(path);
            foreach(string id in new[]{"N-identity-source","S-three-slots"})
            {
                var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/cpu-main-v1/{id}.json");
                var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,character.Context),w.Options with {WorkerCount=16}).Plan!;
                IFamilyInvocation? f;
                if(id.StartsWith("N"))NeowFamily.TryCreate(r,out f);else MerchantShopColorlessFamily.TryCreate(r,out f);
                var cpu=f!.CpuRealizations.First();var q=cpu.QuotePhysicalWork(new(false,false,65536));
                Check(q is not null,"equal vanilla A10 N/S work does not require Ironclad name: "+r.CharacterKey);
                Console.WriteLine($"CPU_ROLE_QUOTE character={r.CharacterKey};shape={q!.Shape};ns={q.NanosecondsPerInput}");
                using var cancel=new CancellationTokenSource();
                var ec=new FamilyExecutionContext(false);var b=new SearchBatch(123456,1024);
                var output=cpu.InvokeAsync(ec,new(b,r),FamilyCandidateSet.Dense(b),cancel.Token).GetAwaiter().GetResult();
                Func<ulong,bool> reference=id.StartsWith("N")?root=>NeowFamilyReplay.Matches(root,NeowReplayPlan.Compile(r,false)):
                    root=>RolltheSpire2.Search.Semantics.MerchantShopColorlessQueryEvaluator.Evaluate(r.Evaluation,root,r.Authority).Disposition!=SearchDisposition.NoMatch;
                var profile=RolltheSpire2.Compatibility.RuntimeProfileRegistry.Select(r.Detection);
                var expected=Enumerable.Range(0,1024).Where(i=>reference(profile.ComputeRootSeed(
                    RolltheSpire2.Search.FamilyExecution.VisibleSeedCandidateCodec.FormatOrdinal(profile,123456UL+(ulong)i)))).Select(i=>(ulong)i).ToArray();
                Check(output.ExportAbi1().Span.SequenceEqual(expected),"captured character reference parity");
                ec.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        foreach(string id in new[]{"R-T1","R-T2","R-T3","R-T4","R-T6","R-T3-ban","R-T3-depth8","R-mixed","R-Rare1"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var ctx=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,ctx.Context),w.Options).Plan!;
            RelicFamily.TryCreate(r,out var owner);
            var cpu=owner!.CpuRealizations.First();
            var quote=cpu.QuotePhysicalWork(new(false,false,65536));
            var physical=(FamilyCpuExecution)cpu;
            if (!FamilyCpuReferenceCost.RelicIdentitySupported(physical.Condition(false).PhysicalImplementationRevision,
                physical.WindowCapacity,physical.Workers,false))
            { Check(quote is null || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, quote, 65536),
                "new ordinary R body needs its own embedded canonical measurement rather than a historical anchor " + id); continue; }
            Check(quote is not null,"ordinary R model coverage "+id);
            Check((id=="R-T3" ? quote!.Shape=="R.CommonFirst3All3.P1" : quote!.Shape.StartsWith("R.OrdinaryWork.")) ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, quote!, 65536),"measured specialization wins before model");
            Check(quote!.OutputAlreadyOrdered && quote.PublicTransportClass=="CpuOrderedAbi1","CPU canonical envelope has no duplicate transport");
            Check(cpu.QuotePhysicalWork(new(true,false,65536)) is null,"unmeasured compact CPU stays Unknown");
            Check(cpu.QuotePhysicalWork(new(false,false,64)) is not { } tinyQuote ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64),"tiny input needs a separate finite canonical source");
        }

        foreach(string id in new[]{"Shop-O1","Shop-O3","Shop-O5","Shop-U3","Shop-U5","Shop-last3"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var ctx=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,ctx.Context),w.Options).Plan!;
            RelicFamily.TryCreate(r,out var owner);var cpu=owner!.CpuRealizations.First();
            var q=cpu.QuotePhysicalWork(new(false,false,65536));
            var physical=(FamilyCpuExecution)cpu;
            if (!FamilyCpuReferenceCost.RelicIdentitySupported(physical.Condition(false).PhysicalImplementationRevision,
                physical.WindowCapacity,physical.Workers,false))
            { Check(q is null || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, q, 65536),
                "new Shop R body needs its own embedded canonical measurement rather than a historical anchor " + id); continue; }
            Check(q is not null,"Shop work coverage "+id);
            Check((id=="Shop-O3" ? q!.Shape=="R.ShopOrdered3.P1" : q!.Shape=="R.ShopPrefixWork.20260913.v1.P1") ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, q!, 65536),"Shop measured override preserved");
            Check((cpu.QuotePhysicalWork(new(false,false,64)) is not { } tinyQuote ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64)) && cpu.QuotePhysicalWork(new(true,false,65536)) is null,"Shop short/compact geometry cannot extrapolate historical anchors");
        }

        foreach(string id in new[]{"N-curse","N-positive","N-large","N-mixed","C-H1","C-H2","C-H3","C-any3","C-last3"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var ctx=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,ctx.Context),w.Options).Plan!;
            IFamilyInvocation? owner;if(id.StartsWith("N"))NeowFamily.TryCreate(r,out owner);else CombatRewardFamily.TryCreate(r,out owner);
            var cpu=owner!.CpuRealizations.First();
            if (owner is CombatRewardFamily changed && changed.Replay.Plan.NeedsCompleteMatchingReference)
            {
                Check(cpu.QuotePhysicalWork(new(false,false,65536)) is null,
                    "changed complete C matcher does not inherit old existential CPU measurement " + id);
                continue;
            }
            Check(cpu.QuotePhysicalWork(new(false,false,65536)) is not null,"N/C numerical pricing coverage "+id);
            Check(cpu.QuotePhysicalWork(new(true,false,65536)) is null &&
                (cpu.QuotePhysicalWork(new(false,false,64)) is not { } tinyQuote || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64)),"N/C static boundaries or separately validated finite canonical input");
        }
        foreach(string id in new[]{"NN-KaleidoscopeBoth","NN-Leafy","NN-Arcane-first-rank","NN-NewLeaf","NN-Lost-card-common","NN-Phial-two-rare","NN-Lead-rare","NN-ScrollSpecial"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var ctx=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/"+(id=="NN-ScrollSpecial"?"n-direct-v1/context-defect-a10.json":"owner-nwae-v1/context.json"));
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,ctx.Context),w.Options).Plan!;
            NeowFamily.TryCreate(r,out var owner);var cpu = owner!.CpuRealizations.First();
            var q = cpu.QuotePhysicalWork(new(false,false,65536));
            Check(id=="NN-ScrollSpecial" ? q is null || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, q, 65536) : q is not null,"N local validated operators / failed holdout boundary "+id);
        }
        string a0=".rewrite-build-evidence/production-replay/runtime-catalog/contexts/character-00-a00.json";
        if(File.Exists(a0))
        foreach(string id in new[]{"N-curse","N-positive","N-mixed"})
        {
            var ctx=ProductionSearchReplay.Read<ProductionSearchReplayContext>(a0);
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,ctx.Context),w.Options).Plan!;
            NeowFamily.TryCreate(r,out var owner);
            Check(NeowPhysicalPricing.TryResolve((NeowFamily)owner!,out var work,out var shape),"A0 identity uses unchanged physical class");
            Check(work.DenseNs==(id=="N-curse"?.28:1.10),"GPU eight-class coefficients unchanged");
            Check(owner!.CpuRealizations.First().QuotePhysicalWork(new(false,false,65536)) is not null,"A0 CPU identity workload equivalent");
        }

        foreach(string id in new[]{"C-specific1","C-specific2","C-specific3","C-late3","C-drop1","C-drop2","C-drop3","C-nodrop1","C-nodrop2","C-nodrop3"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/cpu-numerical-closure-v1/{id}.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,context.Context),w.Options with {WorkerCount=16}).Plan!;
            CombatRewardFamily.TryCreate(r,out var owner);var variants=owner!.CpuRealizations.ToArray();var cpu=variants[0];
            Check(cpu.ConditionPerformance.PhysicalImplementationRevision.Contains("PotionPrefix"),"C prefix has independent numerical revision");
            Check(variants.Length>=2,"C old numerical/worker alternatives preserved");
            var shortQuote = cpu.QuotePhysicalWork(new(false,false,1024));
            Check(shortQuote is not null && (shortQuote.Shape.Contains("PotionPrefix") ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, shortQuote, 1024)),"C prefix owns measured short quote");
            Check((cpu.QuotePhysicalWork(new(false,false,64)) is not { } tinyQuote ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64)) && cpu.QuotePhysicalWork(new(true,false,65536)) is null,"C prefix keeps static short/compact bounds outside canonical measured geometry");
        }

        foreach(string id in new[]{"E-Color","E-FakeColor","E-Color2","E-Color3","E-ColorGrab"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/cpu-numerical-closure-v1/{id}.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,context.Context),w.Options).Plan!;
            EventResultFamily.TryCreate(r,out var owner);var cpu=owner!.CpuRealizations.First();
            Check(cpu.ConditionPerformance.PhysicalImplementationRevision.Contains("CompiledColor"),"E compiled revision separate");
            var shortQuote = cpu.QuotePhysicalWork(new(false,false,1024));
            Check(shortQuote is not null && (shortQuote.Shape.Contains("CompiledColor") ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, shortQuote, 1024)),"E compiled work quote");
            Check((cpu.QuotePhysicalWork(new(false,false,64)) is not { } tinyQuote ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64)) && cpu.QuotePhysicalWork(new(true,false,65536)) is null,"E keeps static short/compact bounds outside canonical measured geometry");
        }

        string store=Path.GetFullPath(".build-evidence/cpu-cost-closure/isolated-"+Guid.NewGuid()+".json");
        Environment.SetEnvironmentVariable("RT2_CPU_COST_EVIDENCE_PATH",store);
        CpuCostCalibration.InitializeOnMainThread();
        NeowFamily.TryCreate(Request("N-identity-source",1),out var nOwner);
        var frozenInvocation=nOwner!.CpuRealizations.First();
        var frozen=CpuCostCalibration.Capture();CpuCostCalibration.Submit([p1,p1b,p8]);
        Check(frozen.Resolve(p1.Key,1).Ratio==1 && CpuCostCalibration.Capture().Resolve(p1.Key,1).Ratio==2,"next Search only consumes new evidence");
        var frozenQuote = frozenInvocation.QuotePhysicalWork(new(false, false, 65536))!;
        var nextInvocation = nOwner.CpuRealizations.First();
        var nextQuote = nextInvocation.QuotePhysicalWork(new(false, false, 65536))!;
        Check(FamilyCanonicalMeasuredCost.IsCanonicalQuote(frozenQuote)
            ? FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(Request("N-identity-source", 1), frozenInvocation, frozenQuote, 65536) &&
              FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(Request("N-identity-source", 1), nextInvocation, nextQuote, 65536) &&
              frozenQuote.NanosecondsPerInput == nextQuote.NanosecondsPerInput
            : frozenQuote.NanosecondsPerInput == 29 && nextQuote.NanosecondsPerInput == 58,
            "actual invocation freezes its historical local ratio; absolute canonical measurements never consume peak ratios");
        CpuCostCalibration.TryFlushPendingOnMainThread();
        using var doc=JsonDocument.Parse(File.ReadAllText(store));
        Check(doc.RootElement.GetProperty("Peaks").GetArrayLength()==3 && !File.ReadAllText(store).Contains("Ratio"),"persist raw measurements, never ratio truth");
        CpuCostCalibration.Submit([p1 with {CanonicalMs=p1.CanonicalMs*2}]);
        Check(CpuCostCalibration.Capture().Resolve(p1.Key,1).Ratio==2,"slow run cannot degrade device peak");
        var shortGeometryFailures = new List<string>();
        foreach (string id in new[]{"S-slot1","S-slot3","S-slot5","S-two","S-five","S-O3","S-U3",
            "E-grab","E-Dive","E-Fake","E-Color-other","E-FakeColor","E-FakeTwo","E-IndependentPair","E-SharedTrash"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,context.Context),w.Options).Plan!;
            IFamilyInvocation? owner;
            if(id.StartsWith("S")) MerchantShopColorlessFamily.TryCreate(r,out owner); else EventResultFamily.TryCreate(r,out owner);
            var cpu=owner!.CpuRealizations.First();
            var q=cpu.QuotePhysicalWork(new(false,false,65536));
            bool sequence=id is "S-O3" or "S-U3";
            Check(q is not null,"S/E bounded CPU quote including numerical sequence "+id);
            if(sequence) Check(q!.Shape=="S.SequenceCalls.20260913.v1.P1" || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, q, 65536),"new sequence revision never borrows old slot/reference price");
            if(q is not null) Check(q.OutputAlreadyOrdered && q.PublicTransportClass=="CpuOrderedAbi1","no CPU transport double charge");
            var tinyQuote = cpu.QuotePhysicalWork(new(false, false, 64));
            var compactQuote = cpu.QuotePhysicalWork(new(true, false, 65536));
            string tinyReason = "QuoteUnavailable";
            bool tinyValid = tinyQuote is null || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64, out tinyReason);
            if (!tinyValid || compactQuote is not null)
            {
                object Describe(FamilyPhysicalQuote? quote, string geometry) => quote is null ? new { Geometry = geometry, Available = false } :
                    new { Geometry = geometry, Available = true, ShapeKind = quote.Shape.Split('.')[0], quote.NanosecondsPerInput,
                        quote.FixedWindowMilliseconds, quote.WindowCapacity, quote.OutputElementBytes, quote.OutputAlreadyOrdered,
                        quote.PublicTransportClass, quote.GpuCostRatio,
                        Source = string.Join(';', quote.Evidence.Split(';').Where(part => part.StartsWith("TrainingRowsSha256=", StringComparison.Ordinal) ||
                            part.StartsWith("CanonicalAssetSha256=", StringComparison.Ordinal) || part.StartsWith("AnchorN=", StringComparison.Ordinal))) };
                Console.WriteLine("CPU_SHORT_GEOMETRY " + JsonSerializer.Serialize(new { Case = id, cpu.FamilyId,
                    Body = cpu.ConditionPerformance.PhysicalImplementationRevision, TinyHelperReason = tinyReason,
                    Tiny = Describe(tinyQuote, "Dense64"), Compact = Describe(compactQuote, "Compact65536") }));
                shortGeometryFailures.Add(id + ":" + tinyReason + ":compactAvailable=" + (compactQuote is not null));
            }
        }
        Check(shortGeometryFailures.Count == 0, "S/E actual short/compact source mismatch: " + string.Join('|', shortGeometryFailures));
        foreach(string id in new[]{"A-TEZCATARA-2","A-PAEL-2","A-DARV-2","A-TANX-3","A-VAKUU-3","A-OROBAS-2",
            "W-variant","W-boss-2","W-boss-3","W-K4"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,context.Context),w.Options).Plan!;
            IFamilyInvocation? owner;
            if(id.StartsWith("A")) AncientOptionFamily.TryCreate(r,out owner); else WorldFamily.TryCreate(r,out owner);
            var cpu=owner!.CpuRealizations.First(); var quote=cpu.QuotePhysicalWork(new(false,false,65536));
            Check(id=="W-K4" ? quote is null || FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, quote, 65536) : quote is not null,"A/W generator and progression boundaries "+id);
            Check((cpu.QuotePhysicalWork(new(false,false,64)) is not { } tinyQuote ||
                FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(r, cpu, tinyQuote, 64)) && cpu.QuotePhysicalWork(new(true,false,65536)) is null,"A/W static tiny/compact boundaries or separately validated canonical input");
        }
        // Quote contract only: identify the measured backend without creating a
        // device. Numerical parity is covered by the native physical matrix.
        // This retained R-envelope fixture fixes N's historical comparison body;
        // current optimized N prices require their own measured device/shape.
        var previousBackend = RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().RenderingBackend;
        var previousNeowGpuOptions = Environment.GetEnvironmentVariable("RT2_SHION_GPU_TRIAL");
        try
        {
            Environment.SetEnvironmentVariable("RT2_SHION_GPU_TRIAL", "off");
            RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveRenderingBackend("d3d12");
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/family-cost-closure-v1/NRC-small-compact.json");
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context), workload.Options).Plan!;
            RelicFamily.TryCreate(request, out var relic);
            foreach (bool privateOutput in new[] { false, true })
            foreach (bool privateInput in new[] { false, true })
            foreach (double n in new[] { 64d, 512, 4096, 4438, 65536, 262143 })
            {
                var quote = relic!.QuotePhysicalWork(new(true, privateOutput, n, privateInput));
                Check(quote is not null && quote.Shape.Contains("CompactSmall"), "bounded small compact R quote");
                Check(quote!.NanosecondsPerInput * n >= 250000 - 1e-8, "underfill floor retained");
                var localQuote = new GpuCostSnapshot([]).Local(relic, quote, new(true, privateOutput, n, privateInput));
                Check(Math.Abs(localQuote.NanosecondsPerInput / quote.NanosecondsPerInput - 1) < 1e-12, "GPU calibration preserves effective small-input price");
            }
            Check(relic!.QuotePhysicalWork(new(true, true, 63, true)) is null, "unmeasured sub-64 stays Unknown");
            Check(relic.QuotePhysicalWork(new(false, true, 4438)) is null, "dense small input not silently admitted");
            Check(relic.QuotePhysicalWork(new(true, true, 262144, true))!.Shape.EndsWith(".Compact"), "full-input model unchanged");
            NeowFamily.TryCreate(request, out var neow);
            CombatRewardFamily.TryCreate(request, out var combat);
            var families = new[] { neow!, relic, combat! };
            var greedy = FamilyPlanner.Plan(families);
            Check(greedy.OrderedFamilies.First().FamilyId == "N.Neow", "Measured single-unit elimination cost selects N first");
            Check(greedy.Stages.SelectMany(stage => stage.Coverage).ToHashSet().SetEquals(families.SelectMany(family => family.Coverage)),
                "Small-compact reference support never removes an unpriced downstream obligation");
            RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveRenderingBackend("vulkan");
            Check(relic.QuotePhysicalWork(new(true, true, 4438, true)) is null, "D3D12 evidence does not admit another backend");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RT2_SHION_GPU_TRIAL", previousNeowGpuOptions);
            RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveRenderingBackend(previousBackend);
        }
        Console.WriteLine("CPU_COST_CLOSURE PASS checks="+checks);return 0;
    }
    private static int CheckGenericScalarQuotes(SearchContext context)
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException(why); }
        const string device = "NVIDIA GeForce RTX 4060 Laptop GPU";
        var geometry = new FamilyPhysicalQuoteRequest(true, true, 1677721.6, true);
        var nc = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/owner-rare-nc-v1/NC_RARE_01.json");
        ExactSearchExecutionRequest Compile(SearchQuery query) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), nc.Options).Plan!;
        var request = Compile(nc.Query);
        var replay = new CombatRewardReplay(request);
        CombatRewardGpuPlan Gpu(ExactSearchExecutionRequest r, CombatRewardReplay c) =>
            CombatRewardGpuPlan.TryCreate(r, c, out string issue) ?? throw new InvalidOperationException("C scalar quote fixture: " + issue);
        var gpu = Gpu(request, replay);
        FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest r, CombatRewardReplay c, CombatRewardGpuPlan p,
            FamilyPhysicalQuoteRequest g, string identity = device, string backend = "d3d12") =>
            CombatRewardPhysicalPricing.QuoteGenericScalarModel(r, c, p, g, identity, backend);
        Check(!replay.Plan.NeedsCompleteMatchingReference || Quote(request, replay, gpu, geometry) is null,
            "complete solo matcher never borrows the old existential scalar price");
        // Retained reference boundary fixture, explicitly not a newly compiled
        // complete-matching body. Keep testing the original price without
        // claiming that it measures the changed matcher.
        var referencePlan = replay.Plan with { NeedsCompleteMatchingReference = false,
            CardAssignmentWindow = 0, CardAssignmentTargets = [], PotionAssignmentWindow = 0,
            PotionAssignmentRequirements = [], PotionAssignmentTargets = [],
            Predicates = replay.Plan.Predicates.Select(p => p with { AnyBattleWindow = 0 }).ToArray() };
        replay = new CombatRewardReplay(request, catalog: replay.Catalog, plan: referencePlan);
        gpu = Gpu(request, replay);
        var quote = Quote(request, replay, gpu, geometry);
        Check(quote is not null && Math.Abs(quote.NanosecondsPerInput - 6.7) < 1e-12 &&
            quote.SetupMilliseconds is null && quote.Evidence.Contains("ConservativeCoarse") && quote.Evidence.Contains("NotCalibratedPeak"),
            "NC generic scalar quote reuses full-generation envelope, with unknown setup and no peak claim");
        var model = new CombatRewardFamilyModels(request, replay, CombatRewardGpuExecutor.Capacity);
        Check(model.Survival.IsResolved && model.ResolveSurvival(new HashSet<string> { "N.Neow" }).SurvivalProbability == model.Survival.SurvivalProbability,
            "NC scheduling uses the fixed declared probability independently of retained legacy price evidence");
        foreach (double population in new[] { 1048576d, 4194304d, 16777216d })
        foreach (bool privateOutput in new[] { false, true })
            Check(Quote(request, replay, gpu, new(true, privateOutput, population, true)) is not null,
                "C compact private input coverage retains measured public/private output boundaries");
        foreach (var g in new[] { new FamilyPhysicalQuoteRequest(false, true, 1677721.6),
            new(true, true, 1677721.6, false), new(true, true, 1048575, true) })
            Check(Quote(request, replay, gpu, g) is null, "C scalar quote excludes Dense, nonprivate and underfilled input");
        Check(Quote(request, replay, gpu, geometry, "NVIDIA GeForce RTX 4090") is null &&
            Quote(request, replay, gpu, geometry, device, "vulkan") is null,
            "C local scalar envelope does not extrapolate device/backend");

        // The held-out shapes share work, not target identities or output rates.
        var nc03 = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/owner-rare-nc-v1/NC_RARE_03.json");
        foreach (var fixture in new (string Name, SearchQuery Query, bool Quoted)[] {
            ("NC03 specific-potion outside scope", nc03.Query, false),
            ("scalar-only", nc.Query with { CombatCardRewards = null }, true),
            ("common-target", nc.Query with { CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Unordered,
                [new("CARD", "ANGER"), new("CARD", "ARMAMENTS"), new("CARD", "ANGER")]) }, true) })
        {
            var r = Compile(fixture.Query); var c = new CombatRewardReplay(r);
            Check((Quote(r, c, Gpu(r, c), geometry) is not null) == (fixture.Quoted && !c.Plan.NeedsCompleteMatchingReference),
                "C structural holdout quote boundary: " + fixture.Name);
            if (!fixture.Quoted) Check(c.Plan.Predicates.Any(p => p.HasPotionIdentityPredicate),
                "NC03 preserves its authored DropSpecific requirements outside the scalar envelope");
        }
        void Outside(string label, Beta110CombatRewardFastPlan p, Beta110FastEffectCatalog? catalog = null)
        {
            var c = new CombatRewardReplay(request, catalog: catalog ?? replay.Catalog, plan: p);
            Check(Quote(request, c, Gpu(request, c), geometry) is null, "C scalar envelope excludes " + label);
        }
        foreach (int horizon in new[] { 1, 2 }) Outside("H" + horizon, replay.Plan with { MaximumBattleOrdinal = horizon });
        var prefixOne = replay.Plan with { OpeningConsumption = new(false, [Beta110FastRelicCatalog.ArcaneScroll], "quote-test-prefix1") };
        Check(CombatRewardSieveProbability.FixedAuthoredPrefixDrawCount(request, prefixOne) == 1,
            "C negative fixture has a proven one-draw prefix");
        Outside("prefix1", prefixOne);
        ushort potion = replay.Catalog.CombatRewardPotionPool.Common[0];
        Outside("specific potion", replay.Plan with { Predicates = replay.Plan.Predicates.Select(p => p.HasCardPredicate ? p :
            p with { PotionAny = [potion] }).ToArray() });
        Outside("gold", replay.Plan with { Predicates = replay.Plan.Predicates.Select(p => p.HasCardPredicate ? p :
            p with { HasMinimumGold = true, MinimumGold = 0 }).ToArray() });
        Outside("fixed battle", replay.Plan with { Predicates = replay.Plan.Predicates.Select(p => p with { BattleOrdinal = 1 }).ToArray() });
        Outside("compound row", replay.Plan with { Predicates = replay.Plan.Predicates.Select(p => p.HasCardPredicate ?
            p with { PotionRequirement = NormalCombatPotionRequirement.MustDrop } : p).ToArray() });
        Outside("extra rewards", replay.Plan with { ExplicitContext = replay.Plan.ExplicitContext with { AdditionalCardRewardCount = 1 } });
        Outside("Candy", replay.Plan with { ExplicitContext = replay.Plan.ExplicitContext with {
            InfluenceFlags = Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard } });
        Outside("unverified card authority", replay.Plan with { CardPoolAuthorityExact = false });
        Outside("unverified potion authority", replay.Plan with { PotionPoolAuthorityExact = false });
        var cardPool = replay.Catalog.CombatRewardCardPool;
        Outside("actual card pool shape", replay.Plan, replay.Catalog with {
            CombatRewardCardPool = cardPool with { Common = cardPool.Common[..^1] } });
        var potionPool = replay.Catalog.CombatRewardPotionPool;
        Outside("actual potion pool shape", replay.Plan, replay.Catalog with {
            CombatRewardPotionPool = potionPool with { Rare = [] } });
        var partyAuthority = RolltheSpire2.Tests.FastSearch.Beta110FastSearchFixtureAuthority.CreateBeta111(request.CharacterKey, 10, 0, 2);
        var partyRequest = request with { CompiledSearch = request.CompiledSearch with {
            Context = request.CompiledSearch.Context with { Authority = partyAuthority } } };
        Check(Quote(partyRequest, replay, gpu, geometry) is null, "C scalar quote does not extrapolate multiplayer");
        return checks;
    }

    private static int CheckRelicQuoteIdentities(SearchContext context)
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("RelicQuoteIdentity:" + why); }
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/cpu-main-v1/R-three-common.json");
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context), workload.Options).Plan!;
        FamilyPhysicalQuote? Quote(string shape, int workers, bool compact, string? revision, int capacity = 65536) =>
            FamilyCpuReferenceCost.Quote(request, new(compact,false,compact ? 65536d/10 : 65536), "R.Relic", shape, workers,
                "ExistingAnchorRegression", compact: compact, cpuPhysicalRevision: revision, cpuWindowCapacity: capacity);
        foreach (var (shape, workers, compact) in new[] {
            ("R.CommonFirst3All3",1,false), ("R.ShopOrdered3",1,false), ("R.ShopOrdered3",8,false),
            ("R.DirectSmallC",1,true), ("R.DirectLargeUR",1,true), ("R.DirectLargeUR",8,true) })
        {
            string revision = FamilyCpuReferenceCost.Revision(shape) +
                $".RootMode2.P{workers}.B65536.{(compact ? "CompactAbi1" : "Dense")}.CanonicalAbi1Ready";
            var quote = Quote(shape,workers,compact,revision);
            Check(quote is not null && quote.NanosecondsPerInput == FamilyCpuReferenceCost.ReferenceNs(shape + ".P" + workers) &&
                quote.Evidence.Contains("CpuPhysicalRevision=" + revision, StringComparison.Ordinal), "Existing measured anchor retained " + shape + workers);
            Check(Quote(shape,workers,compact,null) is null && Quote(shape,workers,compact,revision,8192) is null &&
                Quote(shape,workers,compact,revision.Replace(".B65536.",".B8192.",StringComparison.Ordinal)) is null,
                "Missing identity or mismatched actual/declared window remains unknown " + shape + workers);
            Check(Quote(shape,workers,compact,revision.Replace(".RootMode2.",".RootMode0.",StringComparison.Ordinal)) is null &&
                Quote(shape,workers,compact,revision + ".ChangedBody") is null && Quote(shape,workers + 1,compact,revision) is null,
                "Root mode, worker configuration and complete body identity must match " + shape + workers);
        }
        foreach (var (shape, body) in new[] {
            ("R.CommonFirst3All3","R.Relic.Cpu.TrackedPositions.20261016.v3"),
            ("R.ShopOrdered3","R.Relic.Cpu.ShopBackPrefix.20261016.v2"),
            ("R.DirectLargeUR","R.Relic.Cpu.CapsuleTargetsWithOrdinary.20261016.v3"),
            ("R.DirectLargeUR","R.Relic.Cpu.BonesCapsuleTargetsWithOrdinary.20261016.v4") })
        {
            bool compact = shape.StartsWith("R.Direct",StringComparison.Ordinal);
            string revision = body + $".RootMode2.P1.B65536.{(compact ? "CompactAbi1" : "Dense")}.CanonicalAbi1Ready";
            Check(!FamilyCpuReferenceCost.RelicIdentitySupported(revision,65536,1,compact) && Quote(shape,1,compact,revision) is null,
                "New numerical body cannot borrow measured or model identity " + body);
        }
        Check(FamilyCpuReferenceCost.Quote(request,new(false,false,65536),"S.MerchantShopColorless","S.ThreeUncommonSlots",1,"fixture") is not null,
            "Other families do not acquire a new identity requirement");
        return checks;
    }

    private static int CheckPartyRelicQuoteIdentities()
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("PartyRelicQuoteIdentity:" + why); }
        string? oldCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        var oldDevice = RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER", "Intel64 Family 6 Model 183 TestFixture");
            RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveGpuIdentity("RTX 4060 Laptop GPU", "d3d12");
            string[] bodies = ["R.Relic.Cpu.TrackedPositions.20261013.v2", "R.Relic.Cpu.ShopBackPrefix.20260912.v1",
                "R.Relic.Cpu.Reference.20260912.v1", "R.Relic.Cpu.CapsuleTargets.20261013.v2",
                "R.Relic.Cpu.CapsuleAndSequenceReplay.20260905.v1"];
            string Identity(string body, string mode = "Dense") => body + $".RootMode2.P1.B65536.{mode}.CanonicalAbi1Ready";
            (double Ms, double SetupMs, string Evidence)? Quote(IReadOnlyList<(string PhysicalRevision, int Capacity)>? stages,
                int count = 2, int workers = 1, bool gpu = false, string family = "R.Relic") =>
                PartyMeasuredCost.Price(family, 8192, count, gpu, gpu, 8192, family == "R.Relic" ? 206 : 35,
                    .00001, workers, cpuStages: stages);
            foreach (string body in bodies)
            foreach (string mode in new[] { "Dense", "CompactAbi1" })
            {
                string identity = Identity(body, mode);
                var quote = Quote([(identity,65536),(identity,65536)]);
                Check(quote.HasValue && Math.Abs(quote.Value.Ms - (.001 + 8192 * 840d / 1e6)) < 1e-12 &&
                    quote.Value.Evidence.Contains("IdentityPinnedNotRevalidated",StringComparison.Ordinal) &&
                    quote.Value.Evidence.Contains(identity,StringComparison.Ordinal), "Existing P1 envelope retained with provenance " + identity);
            }
            string old = Identity(bodies[0]);
            foreach (string body in new[] { "R.Relic.Cpu.TrackedPositions.20261016.v3", "R.Relic.Cpu.ShopBackPrefix.20261016.v2",
                "R.Relic.Cpu.CapsuleTargetsWithOrdinary.20261016.v3", "R.Relic.Cpu.BonesCapsuleTargetsWithOrdinary.20261016.v4" })
                Check(Quote([(old,65536),(Identity(body),65536)]) is null, "One changed body invalidates the complete wrapper quote " + body);
            Check(Quote(null) is null && Quote([(old,65536),("",65536)]) is null, "Missing per-child identity stays unknown");
            Check(Quote([(old,65536),(old,8192)]) is null, "Every actual child window must match");
            Check(Quote([(old,65536),(old.Replace(".P1.",".P4.",StringComparison.Ordinal),65536)]) is null &&
                Quote([(old,65536),(old,65536)], workers: 4) is null, "Every child identity and requested workers retain P1 scope");
            Check(Quote([(old,65536)], count: 2) is null && Quote([(old,65536),(old,65536)], count: 1) is null,
                "Identity evidence must cover exactly the complete stage population");
            Check(Quote([(old,65536),(old + ".Changed",65536)]) is null, "No generic revision prefix matching");
            var gpu = Quote(null,gpu:true);
            Check(gpu.HasValue && gpu == Quote([("NewCpuBody",1)],gpu:true), "GPU R does not inherit CPU identity guards");
            var shop = Quote(null,family:"S.MerchantShopColorless");
            Check(shop.HasValue && shop == Quote([("NewCpuBody",1)],family:"S.MerchantShopColorless"), "Other CPU families preserve existing quoting");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROCESSOR_IDENTIFIER",oldCpu);
            RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveGpuIdentity(
                oldDevice.GpuIdentity.Length == 0 ? "Unknown" : oldDevice.GpuIdentity,
                oldDevice.RenderingBackend.Length == 0 ? "unknown" : oldDevice.RenderingBackend);
        }
        return checks;
    }

    internal static int Measure(string[] args)
    {
        string Arg(string key,string fallback) { int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : fallback; }
        var context=ProductionSearchReplay.Read<ProductionSearchReplayContext>(Arg("--context","tools/ProductionSearchHost/Corpus/v1/context.json"));
        var rows=new List<object>();
        var deviceIdentity=SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        int workerBudget=int.Parse(Arg("--worker-budget","16"));
        int stride=int.Parse(Arg("--input-stride","1"));
        if(workerBudget is <1 or >32 || stride<1) throw new ArgumentOutOfRangeException("CPU measurement budget/stride");
        bool passedNeowInput=args.Contains("--passed-neow-input");
        bool prepareOwnerQuotes=args.Contains("--prepare-owner-quotes");
        if(passedNeowInput && stride!=1) throw new ArgumentException("N-passed input uses actual N ABI1, not a synthetic stride");
        var configurations=Arg("--worker-configs","").Split(',',StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        int sampleMs=int.Parse(Arg("--sample-ms","250"));
        int repeats=int.Parse(Arg("--repeats","3"));
        int warmupBatches=int.Parse(Arg("--warmup-batches","64"));
        if(configurations.Any(w=>w<1 || w>workerBudget) || sampleMs is <1 or >60000 || repeats<1 || warmupBatches<0)
            throw new ArgumentOutOfRangeException("CPU measurement configuration/duration");
        bool primed=false;
        var paths=Arg("--workloads","").Split('|',StringSplitOptions.RemoveEmptyEntries);
        if(paths.Length==0) paths=new[]{"N-identity-source","S-three-slots","R-three-common","R-shop-three"}.Select(id=>$"tools/ProductionSearchHost/Corpus/cpu-main-v1/{id}.json").ToArray();
        foreach(var path in paths)
        {
            string id=Path.GetFileNameWithoutExtension(path);
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(path);
            var compiled=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,context.Context),w.Options with {WorkerCount=workerBudget});
            var r=compiled.Plan ?? throw new InvalidOperationException($"CostCorpusCompile:{id}:{compiled.Issue}");
            var cpuOnlyOwners=FamilyExecutionCoordinator.CreateRegisteredFamilies(r,gpuAvailable:false);
            // Optional owner metadata preparation only. Every invocation below
            // remains FamilyCpuExecution; no GPU executor/device is created.
            var owners=prepareOwnerQuotes ? FamilyExecutionCoordinator.CreateRegisteredFamilies(r,gpuAvailable:true) : cpuOnlyOwners;
            // The first P1 body is the same numerical body selected by CPU-only
            // allocation. It checks the complete N-owned obligations of this request.
            var neow=passedNeowInput ? cpuOnlyOwners.Single(f=>f.FamilyId=="N.Neow").CpuRealizations
                .OfType<FamilyCpuExecution>().First(f=>f.Workers==1) : null;
            IReadOnlySet<string> passedCoverage=new HashSet<string>{"N.Neow"};
            foreach(var owner in owners.Where(f=>Arg("--families","NSRCWAE").Contains(f.FamilyId[0]) &&
                (!passedNeowInput || f.FamilyId=="R.Relic")))
            foreach(var f in owner.CpuRealizations.Cast<FamilyCpuExecution>().Where(f=>configurations.Count==0 || configurations.Contains(f.Workers)))
            foreach(int n in Arg("--populations",passedNeowInput?"65536":"65536,1024,4096,16384,32768,65536").Split(',').Select(int.Parse))
            {
                if(passedNeowInput && (n!=65536 || f.CalibrationMeanInput is null ||
                    f.RequiredPassedCoverage is null || !f.RequiredPassedCoverage.SetEquals(passedCoverage)))
                    throw new InvalidOperationException("N-passed measurement requires the existing full-window direct Capsule pricing shape");
                var contextExecution=new FamilyExecutionContext(false);
                FamilyCandidateSet Input(SearchBatch b) => neow is not null ?
                    ((IFamilyInvocation)neow).InvokeAsync(contextExecution,new(b,r),FamilyCandidateSet.Dense(b),CancellationToken.None).GetAwaiter().GetResult() :
                    stride==1 ? FamilyCandidateSet.Dense(b) :
                    FamilyCandidateSet.FromSortedAbi1(b,Enumerable.Range(0,b.BatchCandidateCount).Where(i=>i%stride==0).Select(i=>(ulong)i).ToArray());
                bool compact=passedNeowInput || stride>1;
                var parityBatch=new SearchBatch(987654321,n);
                var parityInput=Input(parityBatch);
                int inputCount=parityInput.Count;
                if(passedNeowInput) f.BindCalibrationCoverage(passedCoverage);
                // n is the actual root span, including synthetic/real Compact
                // inputs. Its retained population must never replace RootWindow.
                var canonicalKey=FamilyCanonicalCostShape.Describe(r,f,compact,n);
                double? sourceSurvival=owner.FamilyId=="N.Neow"
                    ? owner.ResolveSurvival(new HashSet<string>()).SurvivalProbability : null;
                FamilyPhysicalQuote? Quote() => passedNeowInput ? f.Quote(new(true,false,f.CalibrationMeanInput!.Value),passedCoverage) :
                    ((IFamilyInvocation)f).QuotePhysicalWork(new(compact,false,inputCount));
                if(args.Contains("--inventory-only"))
                {
                    rows.Add(new{id,family=owner.FamilyId,revision=((IFamilyInvocation)f).ResolveConditionPerformance(compact).PhysicalImplementationRevision,n,inputCount,stride,workerBudget,workers=f.Workers,work=owner.ExpectedFilteringCost?.Segments,survival=owner.Survival,quote=Quote(),passedNeowInput,PrepareOwnerQuotes=prepareOwnerQuotes,ExecutionClass="CPU",neowRevision=neow?.Condition(false).PhysicalImplementationRevision,ModeledInput=f.CalibrationMeanInput,CanonicalKey=canonicalKey,SourceSurvival=sourceSurvival,DeviceIdentity=deviceIdentity});
                    contextExecution.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    continue;
                }
                // Compare the complete ordered population, never a timing-dependent first-N result.
                string body=((IFamilyInvocation)f).ConditionPerformance.PhysicalImplementationRevision.Split(".RootMode",StringSplitOptions.None)[0];
                var serialOwner=prepareOwnerQuotes ? cpuOnlyOwners.SingleOrDefault(x=>x.FamilyId==owner.FamilyId) : owner;
                var serial=serialOwner?.CpuRealizations.Cast<FamilyCpuExecution>().SingleOrDefault(x=>x.Workers==1 && ((IFamilyInvocation)x).ConditionPerformance.PhysicalImplementationRevision.StartsWith(body+".RootMode",StringComparison.Ordinal))
                    ?? throw new InvalidOperationException($"CpuOwnerQuoteParityBodyMissing:{id}:{owner.FamilyId}:{body}");
                if(passedNeowInput) serial.BindCalibrationCoverage(passedCoverage);
                // Prepared owners must also agree across preparation modes over
                // a full root window, even when timing a shorter geometry.
                var checkBatch=prepareOwnerQuotes ? new SearchBatch(987654321,65536) : parityBatch;
                var checkInput=prepareOwnerQuotes ? Input(checkBatch) : parityInput;
                var expected=((IFamilyInvocation)serial).InvokeAsync(contextExecution,new(checkBatch,r),checkInput,CancellationToken.None).GetAwaiter().GetResult();
                var actual=((IFamilyInvocation)f).InvokeAsync(contextExecution,new(checkBatch,r),checkInput,CancellationToken.None).GetAwaiter().GetResult();
                if(!actual.ExportAbi1().Span.SequenceEqual(expected.ExportAbi1().Span))
                    throw new InvalidOperationException($"CpuWorkerOrderedParity:{id}:P{f.Workers}:stride={stride}");
                if(!primed)
                {
                    var prelude=Stopwatch.StartNew();
                    while(prelude.ElapsedMilliseconds<int.Parse(Arg("--warmup-ms","0")))
                    {
                        var b=new SearchBatch(123456,n);
                        ((IFamilyInvocation)f).InvokeAsync(contextExecution,new(b,r),Input(b),CancellationToken.None).GetAwaiter().GetResult();
                    }
                    primed=true;
                }
                for(int warm=0;warm<warmupBatches;warm++)
                {
                    var b=new SearchBatch(123456UL+(ulong)warm*(ulong)n,n);
                    ((IFamilyInvocation)f).InvokeAsync(contextExecution,new(b,r),Input(b),CancellationToken.None).GetAwaiter().GetResult();
                }
                for(int repeat=0;repeat<repeats;repeat++)
                {
                    var before=f.Observation(); var timer=Stopwatch.StartNew(); int batches=0; long actualInputs=0;
                    long outerInvokeTicks=0;
                    do
                    {
                        var b=new SearchBatch(123456UL+((ulong)batches+(ulong)repeat*10000UL)*(ulong)n,n);
                        var input=Input(b); // N work stays outside R's canonical timing boundary.
                        var observationWindow=new FamilyObservationWindow(b,r);
                        long invokeStarted=Stopwatch.GetTimestamp();
                        ((IFamilyInvocation)f).InvokeAsync(contextExecution,observationWindow,input,CancellationToken.None).GetAwaiter().GetResult();
                        outerInvokeTicks+=Stopwatch.GetTimestamp()-invokeStarted;
                        actualInputs+=input.Count; batches++;
                    }while(timer.ElapsedMilliseconds<sampleMs);
                    var after=f.Observation();
                    double ms=after.PerformanceBoundaryMs-before.PerformanceBoundaryMs;
                    double outerInvokeMs=outerInvokeTicks*1000d/Stopwatch.Frequency;
                    if(passedNeowInput && actualInputs==0)
                        throw new InvalidOperationException("N prerequisite produced no actual R inputs; no compact cost anchor exists for this sample");
                    rows.Add(new{id,family=owner.FamilyId,revision=((IFamilyInvocation)f).ResolveConditionPerformance(compact).PhysicalImplementationRevision,n,inputCount,stride,workerBudget,workers=f.Workers,repeat,batches,inputs=actualInputs,ms,ns=ms*1e6/actualInputs,outerInvokeMs,OuterInvokeBoundary=FamilyCanonicalMeasuredCost.Boundary,work=owner.ExpectedFilteringCost?.Segments,quote=Quote(),passedNeowInput,PrepareOwnerQuotes=prepareOwnerQuotes,ExecutionClass="CPU",rootBatches=batches,ModeledInput=f.CalibrationMeanInput,actualInputs,neowRevision=neow?.Condition(false).PhysicalImplementationRevision,CanonicalKey=canonicalKey,SourceSurvival=sourceSurvival,DeviceIdentity=deviceIdentity});
                }
                contextExecution.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        string output=Arg("--output",".build-evidence/cpu-cost-closure/short-input.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("CPU_SHORT_MEASURE PASS rows="+rows.Count);return 0;
    }
}
