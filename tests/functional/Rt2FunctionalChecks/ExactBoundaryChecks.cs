using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Core.Prediction.Profiles;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using System.Text.Json;

internal static class ExactBoundaryChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException(name); }
        var packed = Enumerable.Range(0, 65536).Select(i => unchecked((uint)(i * 2654435761L))).Concat(new uint[] { 0, 0, uint.MaxValue }).ToArray();
        var sortedReference = packed.Order().ToArray();
        NeowFamilyGpuExecutor.SortLargePackedOrdinals(packed);
        Check(packed.SequenceEqual(sortedReference), "Packed N boundary preserves every ordinal, duplicate and out-of-range value");
        string corpus = Path.Combine(Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(corpus,"v1/context.json")).Context;
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus,"classic-product-v1/B-SourceCapsules-positive.json"));
        var plan = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context),
            workload.Options with { ScanCount=8, TargetMatchCount=100, WorkerCount=4 }).Plan!;
        TrustedRootHashInput Input(string seed) => TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,seed);
        var positive = ProductionExactSearchEvaluator.Evaluate(plan,Input(plan.CanonicalStartSeed));
        Check(positive.IsMatch && positive.Evaluation.Witnesses.Count>0,"Real Exact positive fixture");
        CheckPreparedOpeningReuse(context, plan.CanonicalStartSeed, corpus, Check);
        var bonesChoice = positive.Document!.Sections.SelectMany(s => s.NeowChoices).First(c => c.BonesOutcome is not null);
        CheckMissingBonesOutcome(context, bonesChoice, Check);
        var actualOpening = bonesChoice.BonesOutcome!.OriginalRoutes.Last();
        var authoredOpening = SearchQuery.Empty with { OpeningRoute = new(bonesChoice.RelicKey),
            OpeningRouteRelicRequirement = new(bonesChoice.RelicKey, actualOpening.AcquisitionOrder, BonesRouteOrderMode.ExactOrder) };
        var selection = RolltheSpire2.Ui.Controllers.AnalysisPageController.ResolveSearchOpening(positive.Document, authoredOpening);
        Check(selection.Available && selection.ChoiceSlot == bonesChoice.SlotIndex &&
            selection.Route == (actualOpening.OpeningRewardContinuation?.Route.RouteId ?? actualOpening.RouteId),
            "Unverified opening navigation selects the actual requested Bones order");
        var missingOpening = authoredOpening with { OpeningRoute = new(new ModelKey("RELIC", "MISSING_OPENING")) };
        Check(!RolltheSpire2.Ui.Controllers.AnalysisPageController.ResolveSearchOpening(positive.Document, missingOpening).Available,
            "Absent authored opening is reported unavailable, never manufactured");
        var injected = new Beta111PredictionProfile((_,_,_,_) => throw new InvalidOperationException("InjectedAnalyzerFailure"));
        var failed = injected.Predict(positive.Request!,plan.Detection);
        var quietPlan = plan with { RunOptions = plan.RunOptions with { IncludeDiagnostics=false } };
        var quietPositive = ProductionExactSearchEvaluator.Evaluate(quietPlan,Input(plan.CanonicalStartSeed));
        var quietFailed = injected.Predict(quietPositive.Request!,plan.Detection);
        Check(!quietPositive.Request!.IncludeDiagnostics && quietFailed.Diagnostics.Any(d=>d.Value.Contains("InjectedAnalyzerFailure")),"Failure evidence survives diagnostics off");
        var semanticUnknown = SeedPredictionDocumentFactory.Create(positive.Request!,plan.Detection,plan.ProfileId,"UnknownFixture",plan.CanonicalStartSeed,[],
            [new PredictionWarning(PredictionWarningCode.SnapshotAuthorityIncomplete)],[],SeedPredictionOverallStatus.Unknown);
        ProductionExactSearchEvaluator.ThrowIfPredictionFailed(semanticUnknown);checks++;
        Check(failed.Warnings.Any(w=>w.Code==PredictionWarningCode.AnalysisFailed),"Typed analyzer failure document");
        try { ProductionExactSearchEvaluator.ThrowIfPredictionFailed(failed); throw new Exception("Missing fatal"); }
        catch(InvalidOperationException ex) { Check(ex.Message.Contains("InjectedAnalyzerFailure") && ex.Message.Contains("ExactBoundaryChecks"),"Original exception and stack retained"); }
        var cancelled = new Beta111PredictionProfile((_,_,_,_) => throw new OperationCanceledException("InjectedCancellation"));
        try { cancelled.Predict(positive.Request!,plan.Detection); throw new Exception("Cancellation swallowed"); }
        catch(OperationCanceledException) { checks++; }
        var preparedChoices = positive.Document.Sections.SelectMany(s => s.NeowChoices).ToArray();
        var reusedFailure = injected.PredictFromRootHash(positive.Request!, plan.Detection, Input(plan.CanonicalStartSeed), preparedChoices);
        Check(reusedFailure.Warnings.Any(w => w.Code == PredictionWarningCode.AnalysisFailed) &&
            reusedFailure.Diagnostics.Any(d => d.Value.Contains("InjectedAnalyzerFailure")),
            "Prepared openings retain the original identity failure and evidence");
        try { cancelled.PredictFromRootHash(positive.Request!, plan.Detection, Input(plan.CanonicalStartSeed), preparedChoices); throw new Exception("Prepared opening swallowed cancellation"); }
        catch(OperationCanceledException) { checks++; }

        ulong start=0; foreach(char c in plan.CanonicalStartSeed) start=checked(start*34+(uint)"0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c));
        CheckCandidatePublication(plan, positive, start, Check);
        var unknown=positive with {Evaluation=SearchQueryEvaluation.Unknown("SemanticDetailUnknown")};
        var negativeWorkload=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus,"classic-product-v1/B-small-first-positive.json"));
        var negativePlan=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(negativeWorkload.Query,context),negativeWorkload.Options).Plan!;
        var noMatch=ProductionExactSearchEvaluator.Evaluate(negativePlan,Input(negativePlan.CanonicalStartSeed));
        Check(noMatch.Evaluation.Disposition==SearchDisposition.NoMatch,"Real source-constrained NoMatch");
        Check(noMatch.Evaluation.RouteRejections.Count > 0 && noMatch.Evaluation.RouteRejections.All(r => r.Reason.Length > 0),
            "Rejected openings preserve actual condition reasons");
        CheckDomainAndRouteUncertainty(context, corpus, Check);
        CheckPartyUnverifiedAuthority(context, Check);
        CheckPartyIndependentMismatch(context, Check);
        var bypassPlan = plan with { RunOptions = plan.RunOptions with { SkipExactValidation = true, TargetMatchCount = 3, WorkerCount = 1 } };
        // This low-level session fixture bypasses Coordinator's plan metadata.
        // Its authored predicates have no selected filtering stage and no Exact.
        var bypassExecution = FamilyPlanner.InOrder([]) with { AllConditionsUnfiltered = true, FinalValidationSkipped = true };
        var bypass = new ProductionSearchSession(bypassPlan, bypassExecution,
            FamilySearchEtaProjectorV1.Project(bypassPlan, bypassExecution), 8, 8, null, "",
            exactEvaluator: (_, _) => throw new Exception("Bypass must not invoke Exact"));
        Check(bypass.Completion.Wait(10000), "Candidate mode terminates at its target");
        var unverified = new List<SearchCandidate>();
        while (bypass.TryReadCandidate(out var item)) unverified.Add(item!);
        Check(unverified.Count == 3 && unverified.All(c => c.IsUnverified && c.IsUnfiltered && c.Document is null && c.Witnesses.Count == 0 && c.MatchEvidence.Count == 0),
            "Raw unfiltered outputs have no manufactured proof or prediction document");
        Check(bypass.GetProgress().MatchCount == 0 && bypass.GetProgress().UnverifiedCandidateCount == 3 && bypass.GetProgress().State == SearchRunState.Completed,
            "Unverified count stays separate from Exact matches");
        Check(bypass.GetSafeNextOrdinal() == start + 3, "Unverified output advances only the consumed prefix");
        Check(bypass.GetDiagnosticSummary().DispositionCounts.GetValueOrDefault(nameof(SearchDisposition.Match)) == 0,
            "Candidate mode never records a Match");
        bypass.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var diagnosticExecution = FamilyPlanner.InOrder([]);
        var diagnosticSession = new ProductionSearchSession(plan, diagnosticExecution,
            FamilySearchEtaProjectorV1.Project(plan, diagnosticExecution), 8, 32, null, "",
            exactEvaluator: (_, input) => positive with { Evaluation = SearchQueryEvaluation.NoMatch("seed:" + input.CanonicalSeed) });
        Check(diagnosticSession.Completion.Wait(10000), "Concurrent rejection diagnostics complete");
        var diagnostic = diagnosticSession.GetDiagnosticSummary();
        Check(diagnostic.FirstFailureCode == "seed:" + diagnostic.FirstFailureSeed,
            "First failure code and seed describe the same worker observation");
        diagnosticSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach(string mode in new[]{"unknown","nomatch","fatal","cancel","cancel-fatal"})
        {
            using var later=new ManualResetEventSlim(); using var entered=new ManualResetEventSlim(); using var release=new ManualResetEventSlim();
            ProductionExactSearchResult Evaluate(ExactSearchExecutionRequest p,TrustedRootHashInput input)
            {
                ulong ordinal=0; foreach(char c in input.CanonicalSeed) ordinal=checked(ordinal*34+(uint)"0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c));
                int index=checked((int)(ordinal-start));
                if(mode=="unknown") return unknown;
                if(mode=="nomatch") return noMatch;
                if(index==0) return positive;
                if(index==1)
                {
                    entered.Set();
                    if(!release.Wait(10000)) throw new TimeoutException("Release");
                    if(mode=="fatal" && !later.Wait(10000)) throw new TimeoutException("Later worker");
                    if(mode=="cancel") throw new OperationCanceledException();
                    ProductionExactSearchEvaluator.ThrowIfPredictionFailed(failed);
                }
                later.Set(); return unknown;
            }
            var execution=FamilyPlanner.InOrder([]);
            var session=new ProductionSearchSession(plan,execution,FamilySearchEtaProjectorV1.Project(plan,execution),8,32,null,"",exactEvaluator:Evaluate);
            var emitted=new List<SearchCandidate>();
            if(mode is not ("unknown" or "nomatch"))
            {
                Check(entered.Wait(10000),"Concurrent Exact entered");
                Check(SpinWait.SpinUntil(()=>
                {
                    while(session.TryReadCandidate(out var item))
                    {
                        emitted.Add(item!);
                        if(!item!.IsUnverified) return true;
                    }
                    return false;
                },10000),"Positive emitted before failure");
                Check(SpinWait.SpinUntil(()=>session.GetProgress().ScannedCount>=2,10000),"Later ordinal completed while earlier Exact blocked");
                if(mode.StartsWith("cancel")) session.Cancel();
                release.Set();
            }
            Check(session.Completion.Wait(15000),"Session terminates");
            var progress=session.GetProgress();
            bool fatal=mode.Contains("fatal");
            if(mode=="unknown") Check(progress.LastDisposition==SearchDisposition.Unknown,"Semantic Unknown not relabeled NoMatch");
            Check(progress.State==(mode.StartsWith("cancel",StringComparison.Ordinal)?SearchRunState.Cancelled:SearchRunState.Completed),"State "+mode);
            ulong frontier=session.GetSafeNextOrdinal();
            if(mode=="cancel") Check(frontier==start+1,"Actual cancellation leaves the unfinished Exact prefix resumable");
            else if(mode=="cancel-fatal") Check(frontier>=start+2 && frontier<=start+8,
                "Concurrent real failure is accounted as pending; cancellation may leave later publications unfinished");
            else Check(frontier==start+8,"Completed scanning retains the full in-memory frontier "+mode);
            var continuation=session.CaptureContinuation()!;
            Check(continuation.PendingCandidates.Count==(fatal?1:0) && progress.PendingCandidateCount==(fatal?1:0),
                "Only actual candidate faults remain pending "+mode);
            if(fatal)
            {
                var pending=continuation.PendingCandidates.Single();
                Check(pending.Ordinal==start+1 && pending.Seed==Beta110SeedCodec.FormatOrdinal(start+1) &&
                    pending.Stage=="CandidateCheck" && pending.Attempts==1 &&
                    pending.ExceptionText.Contains("InjectedAnalyzerFailure",StringComparison.Ordinal) &&
                    pending.QueryFingerprint==plan.CompiledSearch.SemanticFingerprint && pending.SnapshotFingerprint==plan.SnapshotFingerprint,
                    "Real analyzer failure retains its original cause and seed/context as pending "+mode);
                Check(string.IsNullOrEmpty(progress.FailureCode),"Recoverable candidate fault is not a session execution fault "+mode);
                Check(session.GetDiagnosticSummary().DispositionCounts.Values.Sum()==progress.ScannedCount-1,
                    "Pending failure is not recorded as Match, NoMatch or ordinary Unknown "+mode);
            }
            while(session.TryReadCandidate(out var candidate)) emitted.Add(candidate!);
            foreach(var candidate in emitted)
            {
                if(candidate.ValidationDisposition==SearchDisposition.Unknown)
                    Check(candidate.IsUnverified && candidate.Document is null && candidate.Witnesses.Count==0 &&
                        candidate.MatchEvidence.Count==0 && candidate.MatchedRouteIds.Count==0 && candidate.ValidationIssue=="SemanticDetailUnknown",
                        "Checked Unknown delivery retains explicit state and reason without a proof payload");
                else Check(!candidate.IsUnverified && candidate.ValidationDisposition==SearchDisposition.Match && candidate.Witnesses.Count>0,
                    "Confirmed delivery retains its real Match witness");
            }
            int confirmed=emitted.Count(c=>!c.IsUnverified),unverifiedCount=emitted.Count(c=>c.IsUnverified);
            Check(confirmed==(mode is "unknown" or "nomatch"?0:1),"Already emitted confirmed result preserved");
            Check(progress.MatchCount==confirmed && progress.UnverifiedCandidateCount==unverifiedCount &&
                progress.DeliveredCandidateCount==emitted.Count,"Separate confirmed/unverified/delivered counts "+mode);
            if(mode=="unknown") Check(emitted.Count==8 && unverifiedCount==8,"Checked Unknown seeds are delivered unconfirmed");
            if(mode=="nomatch") Check(emitted.Count==0,"Proven NoMatch seeds remain excluded");
            if(mode=="fatal") Check(unverifiedCount==6 && emitted.Count==7,
                "Later successful checks remain published while the earlier genuine failure stays outside delivery/target counts");
            Check(continuation.NextOrdinal==(emitted.Count==0?frontier:start),
                "Durable frontier remains behind unacknowledged publications "+mode);
            foreach(var candidate in emitted) session.AcknowledgeCandidate(candidate.Seed);
            var received=session.CaptureContinuation()!;
            Check(received.NextOrdinal==frontier && received.PendingCandidates.Count==(fatal?1:0),
                "ACK advances the durable frontier without deleting a different seed's pending failure "+mode);
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // Capacity probes pad immutable catalogs only; no GPU execution and no
        // claims about new content semantics. The actual selected act is retained.
        var aw=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus,"classic-product-v1/A-positive-tail.json"));
        var gen=context.Authority.WorldAuthority!.Beta109Generation!;
        var impossibleBranch=new VariantScopedBossBranch(1,gen.OrderedActCatalog[0].ActKey,
            new ModelKeySetFilter([gen.OrderedActCatalog[0].Bosses[0]],[],[gen.OrderedActCatalog[0].Bosses[0]]),ModelKeySetFilter.Empty,false);
        var variantProbe=SearchCompiler.Compile(SearchQuery.Empty with {VariantBossBranches=[impossibleBranch,impossibleBranch with {FirstBoss=ModelKeySetFilter.Empty}]},context);
        Console.WriteLine($"VariantRobustnessProbe={variantProbe.Status};canonicalQuery=true;editorUnchanged=true");
        ExactSearchExecutionRequest? oversizedWorld=null;
        foreach(string dimension in new[]{"acts","groups","ancients"})
        foreach(int delta in new[]{-1,0,1})
        {
            int limit=dimension=="acts"?64:dimension=="groups"?8:512;
            var g=dimension switch {
                "acts" => gen with {OrderedActCatalog=Enumerable.Range(0,limit+delta).Select(i=>i<gen.OrderedActCatalog.Count?gen.OrderedActCatalog[i]:gen.OrderedActCatalog[0] with {ActKey=new ModelKey("ACT","CAPACITY_"+i)}).ToArray()},
                "groups" => gen with {ActSelectionGroups=Enumerable.Range(0,limit+delta).Select(i=>gen.ActSelectionGroups[i%gen.ActSelectionGroups.Count]).ToArray()},
                _ => gen with {SharedAncients=Enumerable.Range(0,limit+delta).Select(i=>gen.SharedAncients[i%gen.SharedAncients.Count]).ToArray()}
            };
            var authority=context.Authority.WithWorldAuthority(context.Authority.WorldAuthority with {Beta109Generation=g});
            var c=context with {Authority=authority};
            var request=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(aw.Query,c),aw.Options with {ScanCount=1}).Plan!;
            Check(request is not null,"Capacity Query still admitted");
            Check(WorldFamily.TryCreate(request!,out var w)==(delta<=0),"W capacity boundary "+dimension+delta);
            if(delta>0)
            {
                Check(w is null,"No false W coverage");
                Check(request!.CompiledSearch.NormalizedQuery.AncientBranches.Count==aw.Query.AncientBranches.Count,"Exact World predicates retained");
                var actual=ProductionExactSearchEvaluator.Evaluate(request,Input(request.CanonicalStartSeed));
                Check(actual.Evaluation.Disposition != SearchDisposition.Unknown,"Oversized authority reaches Exact");
                if(dimension=="acts")
                {
                    var baseline=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(aw.Query,context),aw.Options with {ScanCount=1}).Plan!;
                    var baselineResult=ProductionExactSearchEvaluator.Evaluate(baseline,Input(baseline.CanonicalStartSeed));
                    Check(actual.IsMatch && baselineResult.IsMatch && System.Text.Json.JsonSerializer.Serialize(actual.Evaluation.Witnesses)==
                        System.Text.Json.JsonSerializer.Serialize(baselineResult.Evaluation.Witnesses),"Mixed oversized catalog preserves positive Exact/Witness");
                    var wq=LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {
                        BossOrdinalFilters=[new(1,1,new ModelKeySetFilter([gen.OrderedActCatalog[0].Bosses[0]],[],[]))] });
                    oversizedWorld=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(wq,c),aw.Options with {ScanCount=67,TargetMatchCount=1000,WorkerCount=4}).Plan!;
                    var registered=FamilyExecutionCoordinator.Plan(request);
                    Check(!registered.Stages.Any(s=>s.Coverage.Contains("W.World")) && registered.Stages.Count>0,"Mixed allocation keeps other Families, no fabricated W coverage");
                    var corrupt=c with {Authority=authority.WithWorldAuthority(authority.WorldAuthority! with {Beta109Generation=g with {RelicInitializationExact=false}})};
                    var bad=request with {CompiledSearch=SearchCompiler.Compile(aw.Query,corrupt)};
                    Check(!WorldFamily.TryCreate(bad,out var unavailableRelicWorld) && unavailableRelicWorld is null,
                        "Missing actual World initialization facts retire W coverage");
                    var incompleteRelicResult=ProductionExactSearchEvaluator.Evaluate(bad,Input(bad.CanonicalStartSeed));
                    Check(incompleteRelicResult.Evaluation.Disposition==SearchDisposition.Unknown && incompleteRelicResult.Evaluation.Witnesses.Count==0,
                        "Missing World initialization stays unresolved in final model");
                    var empty=c with {Authority=authority.WithWorldAuthority(authority.WorldAuthority! with {Beta109Generation=g with {ActSelectionGroups=[]}})};
                    var missingSelection=request with {CompiledSearch=SearchCompiler.Compile(aw.Query,empty)};
                    Check(!WorldFamily.TryCreate(missingSelection,out var unavailableSelectionWorld) && unavailableSelectionWorld is null,
                        "Missing actual Act-selection facts retire W coverage");
                    var incompleteSelectionResult=ProductionExactSearchEvaluator.Evaluate(missingSelection,Input(missingSelection.CanonicalStartSeed));
                    Check(incompleteSelectionResult.Evaluation.Disposition==SearchDisposition.Unknown && incompleteSelectionResult.Evaluation.Witnesses.Count==0,
                        "Missing Act-selection observation stays unresolved in final model");
                }
            }
        }
        var only=oversizedWorld!;
        var selected=FamilyExecutionCoordinator.Plan(only);
        Check(selected.Stages.Count==0,"Oversized W-only legally selects direct Exact");
        var expected=new List<string>();
        ulong wStart=0;foreach(char c in only.CanonicalStartSeed)wStart=checked(wStart*34+(uint)"0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c));
        for(ulong i=0;i<67;i++) {
            var exact=ProductionExactSearchEvaluator.Evaluate(only,Input(RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec.FormatOrdinal(wStart+i)));
            if(exact.IsMatch) expected.Add(System.Text.Json.JsonSerializer.Serialize(exact.Evaluation.Witnesses));
        }
        var worldSession=FamilyExecutionCoordinator.Start(only,queueCapacity:128);
        Check(worldSession.Completion.Wait(15000),"W Exact-only completes");
        var found=new List<string>();while(worldSession.TryReadCandidate(out var candidate))found.Add(System.Text.Json.JsonSerializer.Serialize(candidate!.Witnesses));
        Check(expected.Count>0 && expected.Order().SequenceEqual(found.Order()),"Oversized W complete positive Exact/Witness multiset");
        Check(worldSession.GetProgress().State==SearchRunState.Completed && worldSession.GetSafeNextOrdinal()==wStart+67,"W Exact-only safe tail");
        worldSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        using(var stopped=new ManualResetEventSlim())
        using(var resume=new ManualResetEventSlim())
        {
            var serial=only with {RunOptions=only.RunOptions with {WorkerCount=1}};
            var cancelSession=new ProductionSearchSession(serial,selected,FamilySearchEtaProjectorV1.Project(serial,selected),8,128,null,"",
                exactEvaluator:(p,i)=>{
                    if(i.CanonicalSeed==RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec.FormatOrdinal(wStart+8)) {
                        stopped.Set();if(!resume.Wait(10000))throw new TimeoutException();throw new OperationCanceledException();
                    }
                    return ProductionExactSearchEvaluator.Evaluate(p,i);
                });
            Check(stopped.Wait(10000),"W cancellation at next batch");cancelSession.Cancel();resume.Set();
            Check(cancelSession.Completion.Wait(10000) && cancelSession.GetProgress().State==SearchRunState.Cancelled && cancelSession.GetSafeNextOrdinal()==wStart+8,"W cancellation preserves committed prefix");
            var combined=new List<string>();while(cancelSession.TryReadCandidate(out var candidate))combined.Add(System.Text.Json.JsonSerializer.Serialize(candidate!.Witnesses));
            cancelSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var remaining=ExactSearchExecutionRequestFactory.Compile(only.CompiledSearch,only.RunOptions with {
                StartSeed=RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec.FormatOrdinal(wStart+8),ScanCount=59}).Plan!;
            var resumed=FamilyExecutionCoordinator.Start(remaining,queueCapacity:128);
            Check(resumed.Completion.Wait(10000),"W resumed completes");
            while(resumed.TryReadCandidate(out var candidate))combined.Add(System.Text.Json.JsonSerializer.Serialize(candidate!.Witnesses));
            Check(expected.Order().SequenceEqual(combined.Order()) && resumed.GetSafeNextOrdinal()==wStart+67,"W cancel/resume Exact/Witness and cursor parity");
            resumed.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        var faultPlan=FamilyPlanner.InOrder([new SelectedWorldFault()]);
        var faultSession=new ProductionSearchSession(only,faultPlan,FamilySearchEtaProjectorV1.Project(only,faultPlan),8,32,null,"");
        Check(faultSession.Completion.Wait(15000) && faultSession.GetProgress().State==SearchRunState.Faulted &&
            faultSession.GetProgress().FailureCode.Contains("InjectedSelectedWFailure") && faultSession.GetSafeNextOrdinal()==wStart,"Selected W fault never becomes inapplicability");
        faultSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Console.WriteLine($"ExactBoundary checks={checks};PASS");return 0;
    }

    private static void CheckPreparedOpeningReuse(SearchContext context, string bonesSeed, string corpus, Action<bool, string> check)
    {
        string otherSeed = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            Path.Combine(corpus, "v1/cases/C-three-potions.json")).Options.StartSeed;
        check(otherSeed != bonesSeed, "Opening reuse A/B fixtures use different roots");
        var beta110 = Beta110FastSearchFixtureAuthority.Create(context.CharacterKey, context.Ascension, 0, 1);
        var beta110Detection = new GameVersionDetection(beta110.GameVersion, beta110.GameVersion, "beta", "reuse-fixture", true, "");
        foreach (var (source, detection) in new[] { (context.Authority, context.Detection), (beta110, beta110Detection) })
        foreach (bool complex in new[] { false, true })
        foreach (bool missingEffects in new[] { false, true })
        {
            var authority = missingEffects ? source.WithEffectAuthority(null) : source;
            var profile = RuntimeProfileRegistry.Select(detection);
            var fixtures = new[] { bonesSeed, otherSeed }.Select(seed =>
            {
                var input = TrustedRootHashInput.FromCanonicalSeed(profile, seed);
                SeedPredictionRequest Request(SeedPredictionDomainSelection domains)
                {
                    check(SeedPredictionRequest.TryCreateFromRootHash(input, authority.Character, authority.Ascension,
                        authority.PlayersCount, authority.PlayerSlotIndex, authority, AncientOptionConditionProfile.BroadDefault,
                        domains, SeedPredictionInputLimits.DefaultRelicSequencePreviewCount, true,
                        NormalCombatRewardProjectionRequest.RichAnalysis, out var request, out _), "Opening reuse fixture request");
                    return request!.WithComplexBonesDeckInteractions(complex);
                }
                var cheapRequest = Request(SeedPredictionDomainSelection.Neow);
                var rewardRequest = Request(SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards);
                var cheapDocument = RuntimeProfileRegistry.PredictFromRootHash(detection, cheapRequest, input);
                var choices = cheapDocument.Sections.SelectMany(s => s.NeowChoices).ToArray();
                // Include internal shared RNG snapshots as well as the entire wire payload.
                // They are intentionally absent from ordinary JSON serialization.
                string SourceSignature() => JsonSerializer.Serialize(new
                {
                    cheapRequest, rewardRequest, cheapDocument, choices,
                    continuationRng = choices.Select(c => new
                    {
                        c.SlotIndex,
                        direct = c.OpeningRewardContinuations?.Routes.Select(r => new
                            { r.RewardsRngState, r.NicheState, r.CombatPotionGenerationState }),
                        bones = c.BonesOutcome?.OriginalRoutes.Select(r => new
                            { rewards = r.OpeningRewardContinuation?.RewardsRngState, niche = r.OpeningRewardContinuation?.NicheState,
                                potions = r.OpeningRewardContinuation?.CombatPotionGenerationState })
                    })
                });
                string sourceJson = SourceSignature();
                var original = RuntimeProfileRegistry.PredictFromRootHash(detection, rewardRequest, input);
                check(SourceSignature() == sourceJson, "Original reward projection preserves opening inputs and all continuation RNG states");
                var rewards = original.Sections.Single(s => s.Kind == PredictionSectionKind.NormalCombatRewardSequence).NormalCombatRewardSequencePrediction!;
                if (missingEffects) check(rewards.Status == SeedDomainEvaluationStatus.Unknown, "Missing reward authority remains Unknown before reuse");
                if (!missingEffects && source.ProfileId == RuntimeProfileId.Beta111 && seed == bonesSeed)
                {
                    check(choices.Any(c => c.BonesOutcome is not null), "Opening reuse exercises actual Bones outcomes in both complexity modes");
                    check(choices.Any(c => c.OpeningRewardContinuations?.Routes.Any(r => r.Route.RouteKind == OpeningRewardRouteKind.DirectNeowChoice) == true),
                        "Opening reuse also exercises ordinary direct Neow continuations");
                    check(rewards.Routes.Any(r => r.Battles.Count == 3), "Opening reuse fixture retains full three-battle observations");
                }
                return (input, rewardRequest, choices, originalJson: JsonSerializer.Serialize(original), sourceJson,
                    sourceSignature: (Func<string>)SourceSignature);
            }).ToArray();
            foreach (int index in new[] { 0, 1, 0 })
            {
                var fixture = fixtures[index];
                var reused = RuntimeProfileRegistry.PredictFromRootHash(detection, fixture.rewardRequest, fixture.input, fixture.choices);
                check(JsonSerializer.Serialize(reused) == fixture.originalJson,
                    $"{source.ProfileId} full same-request reward document parity A/B/A; complex={complex};missingEffects={missingEffects};root={index}");
                foreach (var observed in fixtures)
                    check(observed.sourceSignature() == observed.sourceJson,
                        "Reused rewards preserve both roots' cheap documents, choices, requests and every continuation RNG state");
            }
        }
    }

    private static void CheckMissingBonesOutcome(SearchContext context, NeowChoiceResult knownBones, Action<bool, string> check)
    {
        // Typed reduction of the saved two-seat 0000000002TN repro: P2's exact
        // Bones identity survives, but the unknown P1 shared cursor prevents its
        // outcome projection. This exercises the validator boundary, not native
        // multiplayer pool/RNG parity, and needs no ignored captured context.
        const string seed = "0000000002TN";
        const string cause = "party.earlier-player-shared-opening-unresolved";
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        var phial = BaseGameModelKeys.Relics.PhialHolster;
        var kalei = BaseGameModelKeys.Relics.Kaleidoscope;
        check(SeedPredictionRequest.TryCreate(seed, context.Authority.Character, context.Ascension,
            context.Authority.PlayersCount, context.Authority.PlayerSlotIndex, context.Authority, false, out var request, out _),
            "Missing Bones typed fixture request");
        ExactSearchExecutionRequest Compile(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(query, context), new(seed, 1, int.MaxValue, 1)).Plan
            ?? throw new InvalidOperationException("MissingBonesFixture.Compile");
        var query = SearchQuery.Empty with { OpeningRoute = new(bones),
            OpeningRouteRelicRequirement = new(bones, [kalei, phial], BonesRouteOrderMode.AnyOrder),
            StructuredOpeningEffects = [
                new(phial, NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.GeneratedPotions,
                    NeowStructuredOutputKind.Potion, [new("POTION", "CUNNING_POTION")]),
                new(kalei, NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups,
                    NeowStructuredOutputKind.Card, [new("CARD", "WROUGHT_IN_WAR")])] };
        var plan = Compile(query);
        var sibling = SeedPredictionDocumentFactory.Choice(1, phial, request!, PredictionPrecision.Exact, "fixture.exact-offer",
            NeowEffectProjection.ProductExactNoVisibleEffects(phial, "fixture.independent-effect"));
        SeedPredictionDocument Document(params NeowChoiceResult[] choices) => SeedPredictionDocumentFactory.Create(
            request!, context.Detection, context.ProfileId, "MissingBonesTypedBoundary", seed, choices, [], [], SeedPredictionOverallStatus.CompletedWithWarnings);
        foreach (var projection in new[] { NeowEffectProjection.UnknownAuthority(bones, cause),
            NeowEffectProjection.Unsupported(bones, "fixture.bones-projection-unsupported") })
        {
            var missing = SeedPredictionDocumentFactory.Choice(3, bones, request!, PredictionPrecision.Exact, "fixture.exact-offer", projection);
            check(missing.BonesOutcome is null && missing.OpeningRewardContinuations is null,
                "Actual missing-authority producer retains no invented Bones outcome or continuation");
            var document = Document(missing, sibling);
            var result = ProductionQueryValidator.EvaluateCheap(plan, document);
            check(result.Disposition == SearchDisposition.Unknown && result.FailureCode == projection.EvidenceCode.Value &&
                result.Witnesses.Count == 0 && result.RouteRejections.Any(r => r.Disposition == SearchDisposition.Unknown && r.Reason == projection.EvidenceCode.Value),
                "Missing Unknown/Unsupported Bones preserves its original cause without a false rejection");
            var banSibling = Compile(query with { LegacyNeow = query.LegacyNeow with { NeowRelics = new([], [], [phial]) } });
            check(ProductionQueryValidator.EvaluateCheap(banSibling, document).Disposition == SearchDisposition.NoMatch,
                "Known forbidden offer still rejects independently of a missing Bones observation");
            check(ProductionQueryValidator.EvaluateCheap(Compile(SearchQuery.Empty with { OpeningRoute = new(phial) }), document).Disposition == SearchDisposition.Match,
                "Unselected unknown Bones does not taint a different selected identity");
            check(ProductionQueryValidator.EvaluateCheap(Compile(SearchQuery.Empty with { OpeningRoute = new(bones) }), document).Disposition == SearchDisposition.Match,
                "Selected Bones identity alone does not require its missing outcome");
            // The normal compiler can prove this impossible before Exact. Keep
            // the validator's existing optional-choice contradiction decisive too.
            var conflict = plan with { Evaluation = plan.Evaluation with { StructuredNeowEffects = [
                new(kalei, NeowStructuredConditionKind.IndependentOfferGroupTargets, NeowStructuredEffectScope.SelectableOfferGroups,
                    NeowStructuredOutputKind.Card, [new("CARD", "ANGER"), new("CARD", "ARMAMENTS"), new("CARD", "BASH")])] } };
            var contradiction = ProductionQueryValidator.EvaluateCheap(conflict, document);
            check(contradiction.Disposition == SearchDisposition.NoMatch && contradiction.RouteRejections.Any(r => r.Reason == "OptionalChoiceCommitmentConflict"),
                "Known impossible optional-card commitment remains NoMatch with a missing Bones outcome");
        }
        check(ProductionQueryValidator.EvaluateCheap(plan, Document(sibling)).Disposition == SearchDisposition.NoMatch,
            "Actually absent selected Bones remains a proved identity mismatch");
        var missingRelic = new[] { kalei, phial, BaseGameModelKeys.Relics.SmallCapsule }.First(k => !knownBones.BonesOutcome!.OfferedRelics.Contains(k));
        var wrongPair = Compile(SearchQuery.Empty with { OpeningRoute = new(bones), OpeningRouteRelicRequirement = new(bones, [missingRelic]) });
        check(ProductionQueryValidator.EvaluateCheap(wrongPair, Document(knownBones)).Disposition == SearchDisposition.NoMatch,
            "Known actual Bones offer mismatch remains NoMatch");
    }

    private static void CheckDomainAndRouteUncertainty(SearchContext context, string corpus, Action<bool,string> check)
    {
        var workload=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus,"v1/cases/C-three-potions.json"));
        ExactSearchExecutionRequest Compile(SearchQuery query) => ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(query,context),workload.Options with {ScanCount=1,WorkerCount=1}).Plan!;
        var input=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,workload.Options.StartSeed);
        // An unpinned C request permits only proven-neutral openings; the first
        // seed need not contain any. Obtain real route facts from the existing
        // rich information projection, then pin the actual offered opening.
        check(SeedPredictionRequest.TryCreate(input.CanonicalSeed,context.Authority.Character,context.Ascension,
            context.Authority.PlayersCount,context.Authority.PlayerSlotIndex,context.Authority,true,out var request,out _),
            "Full captured reward-route fixture request");
        var document=RuntimeProfileRegistry.Predict(context.Detection,request!);
        var section=document.Sections.Single(s=>s.Kind==PredictionSectionKind.NormalCombatRewardSequence);
        var sequence=section.NormalCombatRewardSequencePrediction!;
        // Successful reward projections are deliberately Partial: default Monster
        // rewards and no intervening consumers are declared model assumptions.
        // The validator accepts these evaluated facts; this precedence check does
        // not assert unconditional real-game parity or upgrade their precision.
        check(sequence.Routes.Any(r=>r.Status==SeedDomainEvaluationStatus.Evaluated && r.Precision==PredictionPrecision.Partial && r.Battles.Count>0),
            "Rich conditional reward fixture must have evaluated route facts: "+sequence.Status+":"+sequence.IssueCode+":"+
            string.Join("|",sequence.Routes.Select(r=>r.RouteGroupId+":"+r.Status+":"+r.IssueCode)));
        var route=sequence.Routes.First(r=>r.Status==SeedDomainEvaluationStatus.Evaluated && r.Precision==PredictionPrecision.Partial && r.Battles.Count>0);
        var opening=route.RepresentativeContinuation.Route;
        var pinnedQuery=workload.Query with {OpeningRoute=new(opening.RootRelicKey),OpeningRouteRelicRequirement=
            opening.RootRelicKey==BaseGameModelKeys.Relics.NeowsBones
                ? new(opening.RootRelicKey,opening.AcquisitionOrder,BonesRouteOrderMode.ExactOrder) : null};
        var sourcePlan=Compile(pinnedQuery);
        var potion=route.Battles[0].Potion;
        var slot=new CombatPotionRewardSlotSearchCondition(potion.Generated?CombatPotionSlotRequirement.DropSpecific:CombatPotionSlotRequirement.NoDrop,
            potion.Generated?potion.PotionKey:null);
        var matchedPlan=Compile(pinnedQuery with {CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[slot])});
        var cheap=ProductionQueryValidator.EvaluateCheap(matchedPlan,document);
        check(cheap.Disposition==SearchDisposition.Match && cheap.Witnesses.Any(w=>route.EquivalentRoutes.Any(d=>d.RouteId==w.OpeningRouteId)),
            "Actual offered opening supplies a compatible cheap witness for the reward-route fixture");
        SeedPredictionDocument Document(params NormalCombatRewardRoutePredictionResult[] routes) => SeedPredictionDocumentFactory.Create(
            request!,sourcePlan.Detection,sourcePlan.ProfileId,"RewardRouteUncertaintyFixture",input.CanonicalSeed,
            [],[],[],SeedPredictionOverallStatus.Completed,normalCombatRewardPrediction:sequence with {Routes=routes});
        var unsupported=route with {RouteGroupId="unsupported-first",Status=SeedDomainEvaluationStatus.Unsupported,IssueCode="UnsupportedFixture"};
        var unresolved=ProductionQueryValidator.EvaluateRewards(matchedPlan,Document(unsupported),cheap);
        check(unresolved.Disposition==SearchDisposition.Unsupported && unresolved.FailureCode=="NoSupportedNormalCombatRewardRouteMatched" &&
            unresolved.RouteRejections.Single().Reason=="RewardRouteUnsupported:UnsupportedFixture",
            "Unsupported reward alternative remains unresolved with machine reason");
        var matched=ProductionQueryValidator.EvaluateRewards(matchedPlan,Document(unsupported,route),cheap);
        check(matched.Disposition==SearchDisposition.Match && matched.Witnesses.Count>0 &&
            matched.Witnesses.All(w=>w.RewardRouteGroupId==route.RouteGroupId),
            "Unsupported first reward alternative cannot block later common Match witness");
        var opposite=new CombatPotionRewardSlotSearchCondition(potion.Generated?CombatPotionSlotRequirement.NoDrop:CombatPotionSlotRequirement.DropAny,null);
        var rejectedPlan=Compile(pinnedQuery with {CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[opposite])});
        check(ProductionQueryValidator.EvaluateRewards(rejectedPlan,Document(route),cheap).Disposition==SearchDisposition.NoMatch,
            "All known reward alternatives still prove NoMatch");
        check(ProductionQueryValidator.EvaluateRewards(rejectedPlan,Document(unsupported,route),cheap).Disposition==SearchDisposition.Unsupported,
            "Known failed reward route cannot disprove an unsupported alternative");

        var emptyPolicy=sequence with {Routes=[],NoEligibleSearchRewardRoute=true};
        var policyDocument=SeedPredictionDocumentFactory.Create(request!,sourcePlan.Detection,sourcePlan.ProfileId,"EmptyPolicyFixture",
            input.CanonicalSeed,[],[],[],SeedPredictionOverallStatus.Completed,normalCombatRewardPrediction:emptyPolicy);
        var unpinnedPlan=Compile(workload.Query);
        var policyResult=ProductionQueryValidator.EvaluateRewards(unpinnedPlan,policyDocument,SearchQueryEvaluation.Match());
        check(policyResult.Disposition==SearchDisposition.Unknown && policyResult.FailureCode=="NoEligibleUnpinnedRewardRoute" && policyResult.Witnesses.Count==0,
            "Empty neutral policy cannot disprove rewards after all actual openings");
        check(ProductionQueryValidator.EvaluateRewards(matchedPlan,policyDocument,cheap).Disposition==SearchDisposition.NoMatch,
            "Pinned empty eligibility retains its concrete route mismatch");

        var unknownCards=route with {Battles=route.Battles.Select(b=>b with {
            Cards=b.Cards.Select(c=>c with {Precision=PredictionPrecision.Unknown}).ToArray(),
            CardRewards=b.CardRewards.Select(r=>r with {Cards=r.Cards.Select(c=>c with {Precision=PredictionPrecision.Unknown}).ToArray()}).ToArray()
        }).ToArray()};
        check(ProductionQueryValidator.EvaluateRewards(matchedPlan,Document(unknownCards),cheap).Disposition==SearchDisposition.Match,
            "Known potion obligation does not consume unrelated card precision");

        // An existential legacy row binds every field to one battle. Separate
        // card and potion/gold successes must not be stitched into that row.
        var card=route.Battles[0].Cards[0] with {Precision=PredictionPrecision.Exact};
        var otherCard=card with {CardKey=context.Authority.EffectAuthority!.CharacterRewardPool!.First(c=>c.CardKey!=card.CardKey).CardKey};
        var targetPotion=route.Battles[0].Potion with {
            Generated=true,PotionKey=context.Authority.EffectAuthority!.PotionPool![0].PotionKey,Precision=PredictionPrecision.Exact};
        var cardBattle=route.Battles[0] with {BattleOrdinal=1,Cards=[card],CardRewards=[],
            Gold=13,GoldRewards=[],GoldStatus=NormalCombatGoldProjectionStatus.ConditionalDefaultMonsterFullKill,
            Potion=targetPotion with {Generated=false,PotionKey=null}};
        var potionBattle=cardBattle with {BattleOrdinal=2,Cards=[otherCard],Gold=14,Potion=targetPotion};
        var splitRoute=route with {Battles=[cardBattle,potionBattle]};
        var jointRow=new NormalCombatRewardSearchCondition(0,new([], [card.CardKey], []),
            NormalCombatPotionRequirement.MustDrop,new([targetPotion.PotionKey!.Value],[],[]),14,14);
        SearchQueryEvaluation RewardRow(NormalCombatRewardSearchCondition row,NormalCombatRewardRoutePredictionResult facts) =>
            ProductionQueryValidator.EvaluateRewards(Compile(pinnedQuery with {CombatCardRewards=null,CombatPotionRewards=null,
                LegacyCombatRewardConstraints=[row]}),Document(facts),cheap);
        check(RewardRow(jointRow with {PotionRequirement=NormalCombatPotionRequirement.Any,Potions=ModelKeySetFilter.Empty,
                MinimumGold=null,MaximumGold=null},splitRoute).Disposition==SearchDisposition.Match &&
            RewardRow(jointRow with {Cards=ModelKeySetFilter.Empty},splitRoute).Disposition==SearchDisposition.Match,
            "Split fixture separately satisfies the card and potion/gold obligations");
        check(RewardRow(jointRow,splitRoute).Disposition==SearchDisposition.NoMatch,
            "One reward row cannot combine card and potion/gold across battles");
        check(RewardRow(jointRow,route with {Battles=[cardBattle with {Gold=14,Potion=targetPotion}]}).Disposition==SearchDisposition.Match,
            "One battle satisfying every reward field supplies a real row witness");
        check(ProductionExactSearchEvaluator.CombineIndependentEvaluations(SearchQueryEvaluation.Unknown("UnobservedCanonical"),
            SearchQueryEvaluation.NoMatch("KnownWorldMismatch")).Disposition==SearchDisposition.NoMatch,
            "Independent known mismatch decides conjunction after unknown canonical obligation");
        var unresolvedCanonical=ProductionExactSearchEvaluator.CombineIndependentEvaluations(SearchQueryEvaluation.Match(),
            SearchQueryEvaluation.Unsupported("UnsupportedCanonical"));
        check(unresolvedCanonical.Disposition==SearchDisposition.Unsupported && unresolvedCanonical.Witnesses.Count==0,
            "Independent Match cannot manufacture an unsupported canonical witness");

        var allOffers=document.Sections.Single(s=>s.Kind==PredictionSectionKind.NeowIdentity).NeowChoices;
        var identityPlan=Compile(SearchQuery.Empty with {OpeningRoute=new(opening.RootRelicKey)});
        var identityResult=ProductionExactSearchEvaluator.Evaluate(identityPlan,input);
        var identities=identityResult.Document!.Sections.Single(s=>s.Kind==PredictionSectionKind.NeowIdentity).NeowChoices;
        check(identityResult.IsMatch && identities.Select(c=>c.RelicKey).SequenceEqual(allOffers.Select(c=>c.RelicKey)) &&
            identities.All(c=>c.OpeningRewardContinuations is null && c.EffectGroups.Count==0),
            "Identity-only Exact retains all offers without projecting unrequested effects or continuation");
        var pinnedResult=ProductionExactSearchEvaluator.Evaluate(matchedPlan,input);
        var projectedChoices=pinnedResult.Document!.Sections.Single(s=>s.Kind==PredictionSectionKind.NeowIdentity).NeowChoices;
        check(projectedChoices.Where(c=>c.RelicKey!=opening.RootRelicKey).All(c=>c.OpeningRewardContinuations is null) &&
            projectedChoices.Single(c=>c.RelicKey==opening.RootRelicKey).OpeningRewardContinuations is not null,
            "Pinned reward Exact observes selected opening continuation while retaining sibling identities");

        check(SearchAuthorityProjector.TryProjectForRootHash(sourcePlan,input.RootHash,input.CanonicalSeed,
            out var projectedAuthority,out _,out _),"Typed Variant fixture authority projected for actual root");
        var generation=projectedAuthority.WorldAuthority!.Beta109Generation!;
        var firstBoss=document.Sections.Single(s=>s.Kind==PredictionSectionKind.BossIdentity).Bosses.First(b=>b.Ordinal==1);
        var worldPredicate=LegacyWorldSemanticConstraints.Empty with {BossFilters=[new(firstBoss.Act,new([firstBoss.BossKey],[],[]))]};
        var knownRewardWithUnknownWorld=SeedPredictionDocumentFactory.Create(request!,sourcePlan.Detection,sourcePlan.ProfileId,"IndependentRewardFixture",
            input.CanonicalSeed,allOffers,[],[],SeedPredictionOverallStatus.Completed,
            worldPrediction:WorldPredictionResult.Unknown("MissingWorldFixture"),normalCombatRewardPrediction:sequence with {Routes=[route]});
        var matchedWithWorld=Compile(pinnedQuery with {CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[slot]),LegacyWorld=worldPredicate});
        var rejectedWithWorld=Compile(pinnedQuery with {CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[opposite]),LegacyWorld=worldPredicate});
        var partialCheap=ProductionQueryValidator.EvaluateCheap(matchedWithWorld,knownRewardWithUnknownWorld,null,out var actualOpeningWitness);
        check(partialCheap.Disposition==SearchDisposition.Unknown && partialCheap.Witnesses.Count==0 &&
            actualOpeningWitness.Disposition==SearchDisposition.Match && actualOpeningWitness.Witnesses.Count>0 &&
            actualOpeningWitness.Witnesses.All(w=>!string.IsNullOrWhiteSpace(w.OpeningRouteId)),
            "Cheap unknown keeps actual N witness internally without attaching it to unresolved query");
        check(ProductionQueryValidator.Evaluate(rejectedWithWorld,knownRewardWithUnknownWorld).Disposition==SearchDisposition.NoMatch,
            "Known C mismatch on actual N witness decides query despite independent World unknown");
        var stillUnknown=ProductionQueryValidator.Evaluate(matchedWithWorld,knownRewardWithUnknownWorld);
        check(stillUnknown.Disposition==SearchDisposition.Unknown && stillUnknown.Witnesses.Count==0 &&
            stillUnknown.FailureCode==partialCheap.FailureCode,"C Match cannot clear independent World unknown or supply a final witness");
        var unknownN=SeedPredictionDocumentFactory.Create(request!,sourcePlan.Detection,sourcePlan.ProfileId,"UnknownOpeningFixture",
            input.CanonicalSeed,allOffers,[],[],SeedPredictionOverallStatus.Completed,normalCombatRewardPrediction:sequence with {Routes=[route]},
            neowStatus:SeedDomainEvaluationStatus.Unknown,neowIssueCode:"MissingNFixture");
        var noBorrowedRoute=ProductionQueryValidator.Evaluate(rejectedPlan,unknownN);
        check(noBorrowedRoute.Disposition==SearchDisposition.Unknown && noBorrowedRoute.FailureCode=="NeowDomainUnknown:MissingNFixture" &&
            noBorrowedRoute.Witnesses.Count==0,"Unknown N cannot borrow a reward-route payload to establish C mismatch");

        var incompleteEventWorld=context.Authority.WorldAuthority! with {Beta109Generation=context.Authority.WorldAuthority.Beta109Generation! with {
            EventAuthority=context.Authority.WorldAuthority.Beta109Generation.EventAuthority with {SharedRawOrderExact=false}
        }};
        var incompleteEventContext=context with {Authority=context.Authority.WithWorldAuthority(incompleteEventWorld)};
        ExactSearchExecutionRequest MissingWorldPlan(SearchQuery q)=>ExactSearchExecutionRequestFactory.Compile(
            SearchCompiler.Compile(q,incompleteEventContext),workload.Options with {ScanCount=1,WorkerCount=1}).Plan!;
        var e2eMismatch=ProductionExactSearchEvaluator.Evaluate(MissingWorldPlan(pinnedQuery with {
            CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[opposite]),LegacyWorld=worldPredicate}),input);
        check(e2eMismatch.Evaluation.Disposition==SearchDisposition.NoMatch && e2eMismatch.Evaluation.FailureCode=="NoNormalCombatRewardRouteMatched",
            "Production Exact evaluates known real C after unknown required World event-prefix observation");
        var e2eUnresolved=ProductionExactSearchEvaluator.Evaluate(MissingWorldPlan(pinnedQuery with {
            CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[slot]),LegacyWorld=worldPredicate}),input);
        check(e2eUnresolved.Evaluation.Disposition==SearchDisposition.Unknown && e2eUnresolved.Evaluation.Witnesses.Count==0 &&
            e2eUnresolved.Evaluation.FailureCode.Contains("MissingSharedEventCatalog",StringComparison.Ordinal),
            "Production C Match preserves actual unresolved World cause without confirmed proof");
        var variant=generation.SelectedActs[firstBoss.Act-1];
        var acceptedBoss=new ModelKeySetFilter([firstBoss.BossKey],[],[]);
        var rejectedBoss=new ModelKeySetFilter([],[],[firstBoss.BossKey]);
        SearchQueryEvaluation Branches(params VariantScopedBossBranch[] rows) => ProductionQueryValidator.EvaluateCheap(
            sourcePlan with {Evaluation=ExactSearchEvaluationProjection.Empty with {VariantBossBranches=rows}},document,projectedAuthority);
        check(Branches(new VariantScopedBossBranch(firstBoss.Act,variant,rejectedBoss,ModelKeySetFilter.Empty,false),
            new VariantScopedBossBranch(firstBoss.Act,variant,ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)).Disposition==SearchDisposition.Match,
            "Typed Variant OR keeps wildcard sibling after known child mismatch");
        check(Branches(new VariantScopedBossBranch(firstBoss.Act,variant,rejectedBoss,ModelKeySetFilter.Empty,false)).Disposition==SearchDisposition.NoMatch,
            "Typed Variant child predicate retains proven mismatch");
        check(Branches(new VariantScopedBossBranch(firstBoss.Act,variant,acceptedBoss,rejectedBoss,true),
            new VariantScopedBossBranch(firstBoss.Act,variant,rejectedBoss,acceptedBoss,true)).Disposition!=SearchDisposition.Match,
            "Typed Variant Boss1/Boss2 cannot combine children from different rows");
        var finalBossFacts = document.Sections.Single(s => s.Kind == PredictionSectionKind.BossIdentity).Bosses
            .Where(b => b.Act == 3).OrderBy(b => b.Ordinal).ToArray();
        check(finalBossFacts.Length == 2 && finalBossFacts[0].BossKey != finalBossFacts[1].BossKey,
            "Captured A10 fixture supplies two distinct final Boss identities");
        var orderedFinal = new VariantScopedBossBranch(3, generation.SelectedActs[2],
            new([finalBossFacts[0].BossKey], [], []), new([finalBossFacts[1].BossKey], [], []), true);
        check(Branches(orderedFinal).Disposition == SearchDisposition.Match &&
            Branches(orderedFinal with { FirstBoss = orderedFinal.SecondBoss, SecondBoss = orderedFinal.FirstBoss }).Disposition == SearchDisposition.NoMatch,
            "Production Exact distinguishes the two final Boss positions instead of matching an unordered pair");
        var presenceFinal = orderedFinal with { FirstBoss = ModelKeySetFilter.Empty, SecondBoss = ModelKeySetFilter.Empty };
        check(Branches(presenceFinal).Disposition == SearchDisposition.Match,
            "Presence-only second Boss declaration accepts a real second Boss observation");
        var missingSecondDocument = new SeedPredictionDocument
        {
            Context = document.Context, PredictorId = document.PredictorId, OriginalSeed = document.OriginalSeed,
            CanonicalSeed = document.CanonicalSeed, Warnings = document.Warnings, Diagnostics = document.Diagnostics,
            OverallStatus = document.OverallStatus, Sections = document.Sections.Select(s => s.Kind == PredictionSectionKind.BossIdentity
                ? s with { Bosses = s.Bosses.Where(b => b.Act != 3 || b.Ordinal != 2).ToArray() } : s).ToArray()
        };
        var missingSecond = ProductionQueryValidator.EvaluateCheap(sourcePlan with
            { Evaluation = ExactSearchEvaluationProjection.Empty with { VariantBossBranches = [presenceFinal] } }, missingSecondDocument, projectedAuthority);
        check(missingSecond.Disposition == SearchDisposition.Unknown && missingSecond.FailureCode == "VariantBossNotReplayable:Act3:Ordinal2",
            "Solo presence-only second Boss retains the missing observation cause instead of fabricating Match");
        var presenceFinalQuery = SearchQuery.Empty with { VariantBossBranches = [presenceFinal] };
        check(SearchFeasibilityAnalyzer.Analyze(SearchCompiler.Compile(presenceFinalQuery, context with { Ascension = 9 })).IsImpossible &&
            !SearchFeasibilityAnalyzer.Analyze(SearchCompiler.Compile(presenceFinalQuery, context)).IsImpossible,
            "Exact catalog proves second Boss presence impossible below A10 while preserving the A10 declaration");
        var missingWorld=projectedAuthority.WithWorldAuthority(null);
        var unresolvedVariant=ProductionQueryValidator.EvaluateCheap(sourcePlan with {
            Evaluation=ExactSearchEvaluationProjection.Empty with {VariantBossBranches=[new(firstBoss.Act,variant,ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)]}},document,missingWorld);
        check(unresolvedVariant.Disposition==SearchDisposition.Unknown && unresolvedVariant.Witnesses.Count==0,
            "Missing Variant parent observation remains unresolved without child catalog guesses");

        var relics=RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,input.RootHash,input.CanonicalSeed,projectedAuthority.WorldAuthority);
        var shopUnknown=projectedAuthority.WorldAuthority with {Beta109Generation=generation with {
            RelicShopEligibilityAuthorityExact=false,
            SharedRelicBuckets=generation.SharedRelicBuckets.Select(b=>b with {OrderedEntries=b.OrderedEntries.Select(e=>e with {ShopEligibilityExact=false}).ToArray()}).ToArray(),
            PlayerRelicBuckets=generation.PlayerRelicBuckets.Select(b=>b with {OrderedEntries=b.OrderedEntries.Select(e=>e with {ShopEligibilityExact=false}).ToArray()}).ToArray()
        }};
        var partialRelics=RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,input.RootHash,input.CanonicalSeed,shopUnknown);
        check(relics.Status==SeedDomainEvaluationStatus.Evaluated && partialRelics.Status==SeedDomainEvaluationStatus.Evaluated &&
            partialRelics.RngCallCount==relics.RngCallCount && partialRelics.Lanes.Where(l=>l.Kind!=RelicSequenceKind.Shop).All(l=>
                l.Precision==PredictionPrecision.Exact && l.Entries.SequenceEqual(relics.Lanes.Single(r=>r.Kind==l.Kind).Entries)),
            "Unknown shop eligibility preserves actual shuffle consumption and exact ordinary relic lanes");
        check(partialRelics.Lanes.Single(l=>l.Kind==RelicSequenceKind.Shop) is {Precision:PredictionPrecision.Unknown,Entries.Count:0,
            IssueCode:"MissingModernRelicShopEligibilityAuthority"},"Unknown Shop lane keeps its concrete missing prerequisite");

        var missingSnapshots=context.Authority.WithEffectAuthority(null).WithWorldAuthority(null);
        var missingPlan=sourcePlan with {CompiledSearch=sourcePlan.CompiledSearch with {Context=sourcePlan.CompiledSearch.Context with {Authority=missingSnapshots}}};
        check(SearchAuthorityProjector.TryProjectForRootHash(missingPlan,input.RootHash,input.CanonicalSeed,out var preservedMissing,out _,out _) &&
            preservedMissing.EffectAuthority is null && preservedMissing.WorldAuthority is null,
            "Missing observed snapshots are preserved without discarding independent obligations");
        var missingObservation=ProductionExactSearchEvaluator.Evaluate(missingPlan,input);
        check(missingObservation.Evaluation.Disposition==SearchDisposition.Unknown && missingObservation.Evaluation.Witnesses.Count==0,
            "Missing required opening snapshot propagates semantic Unknown rather than projection failure or fake Match");

        var partyObservation=new PartySeedInformation(input.CanonicalSeed,input.RootHash,"0.111.0",context.Ascension,"fixture",generation.SelectedActs,[],
            [new(0,context.CharacterKey,[opening.RootRelicKey])]) {BossStatus=SeedDomainEvaluationStatus.Unknown,BossIssueCode="MissingPartyWorldFixture"};
        var partyOffers=SearchQuery.Empty with {Players=[new(0,new([opening.RootRelicKey],[],[]))]};
        check(ProductionExactSearchEvaluator.EvaluatePartyObservations(partyOffers,partyObservation).Disposition==SearchDisposition.Match,
            "Party initial N obligation does not consume unresolved World envelope");
        var partyWorld=partyOffers with {LegacyWorld=LegacyWorldSemanticConstraints.Empty with {
            BossFilters=[new(firstBoss.Act,new([firstBoss.BossKey],[],[]))]}};
        check(ProductionExactSearchEvaluator.EvaluatePartyObservations(partyWorld,partyObservation).Disposition==SearchDisposition.Unknown,
            "Required party World observation retains returned Unknown instead of a thrown fault");
        var secondBossPresence=partyOffers with {VariantBossBranches=[new(3,generation.SelectedActs[2],
            ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,true)]};
        check(ProductionExactSearchEvaluator.EvaluatePartyObservations(secondBossPresence,partyObservation).Disposition==SearchDisposition.Unknown,
            "IncludesSecondBoss requires a Boss observation even when both child identity filters are empty");
        check(ProductionExactSearchEvaluator.EvaluatePartyObservations(partyWorld with {Players=[new(0,new([],[],[opening.RootRelicKey]))]},
            partyObservation).Disposition==SearchDisposition.NoMatch,"Known party offer mismatch decides conjunction after unknown World");
        check(ProductionExactSearchEvaluator.EvaluatePartyObservations(partyOffers,partyObservation with {
            Players=[partyObservation.Players[0] with {IdentityPrecision=PredictionPrecision.Unknown,IssueCode="Party.NeowAuthorityIncomplete"}]
        }).Disposition==SearchDisposition.Unknown,"Unknown party offer identity cannot prove its predicate");

        var residualQuery=SearchQuery.Empty with {
            OpeningRoute=new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.LeafyPoultice,BaseGameModelKeys.Relics.NewLeaf]),
            TransformationAggregate=new(TransformationOpening.BonesLeafyNewLeaf,TransformationPickupOrder.Any,false,false,false,
                TransformationAggregatePredicate.RareCountAtLeast,1,[]),
            RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.ExactSlot,1,
                new([relics.Lanes.Single(l=>l.Kind==RelicSequenceKind.Common).Entries.First().RelicKey],[],[]))]
        };
        var residualResult=ProductionExactSearchEvaluator.Evaluate(Compile(residualQuery),
            TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,"0000000001FA"));
        check(residualResult.Evaluation.Disposition==SearchDisposition.Unknown && residualResult.Evaluation.FailureCode==
            "TransformationAggregate.NeowDependentRelicContinuationNotSupported" && residualResult.Evaluation.Witnesses.Count==0,
            "Unsupported T/R continuation stays authored and unresolved without checking an unrelated initial bag");

        // Keep real World facts and one real N predicate; change only the observed
        // Neow domain to incomplete. No route-specific negations are combined.
        var boss=document.Sections.Single(s=>s.Kind==PredictionSectionKind.BossIdentity).Bosses.First();
        var offer=document.Sections.Single(s=>s.Kind==PredictionSectionKind.NeowIdentity).NeowChoices.First().RelicKey;
        SearchQuery Query(bool rejectBoss) => LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {
            NeowRelics=new([offer],[],[]),BossFilters=[new(boss.Act,rejectBoss?new([],[],[boss.BossKey]):new([boss.BossKey],[],[]))]});
        var unknownDocument=SeedPredictionDocumentFactory.Create(request!,context.Detection,context.Authority.ProfileId,"DomainUncertaintyFixture",
            input.CanonicalSeed,[],[],[],SeedPredictionOverallStatus.CompletedWithWarnings,
            neowStatus:SeedDomainEvaluationStatus.Unknown,neowIssueCode:"SnapshotAuthorityIncomplete");
        // Copy the independent immutable section payloads; no simulated state changes.
        var independent= new SeedPredictionDocument {
            Context=unknownDocument.Context,PredictorId=unknownDocument.PredictorId,OriginalSeed=unknownDocument.OriginalSeed,
            CanonicalSeed=unknownDocument.CanonicalSeed,Warnings=unknownDocument.Warnings,Diagnostics=unknownDocument.Diagnostics,
            OverallStatus=unknownDocument.OverallStatus,Sections=unknownDocument.Sections.Concat(document.Sections.Where(s=>s.Kind!=PredictionSectionKind.NeowIdentity)).ToArray()};
        var mismatch=ProductionQueryValidator.EvaluateCheap(Compile(Query(true)),independent);
        check(mismatch.Disposition==SearchDisposition.NoMatch && mismatch.FailureCode=="BossFilterRejected:Act"+boss.Act,
            "Independent proven World mismatch decides conjunction after unknown Neow");
        var unknown=ProductionQueryValidator.EvaluateCheap(Compile(Query(false)),independent);
        check(unknown.Disposition==SearchDisposition.Unknown && unknown.FailureCode=="NeowDomainUnknown:SnapshotAuthorityIncomplete" && unknown.Witnesses.Count==0,
            "Independent World Match cannot manufacture a missing Neow witness");
    }

    internal static int RunPartyIndependentMismatch()
    {
        int checks = 0;
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        CheckPartyIndependentMismatch(captured, (pass, reason) =>
        {
            if (!pass) throw new InvalidOperationException(reason);
            checks++;
        });
        Console.WriteLine($"PartyExactIndependent checks={checks};PASS");
        return 0;
    }

    private static void CheckPartyIndependentMismatch(SearchContext captured, Action<bool, string> check)
    {
        const string seed = "000000000KL6";
        var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
        // A typed partial-authority fixture for reachable authored MP requests,
        // not a claim that a native capture has encountered these missing facts.
        SearchContext Context(bool missingWorld, bool missingRewards)
        {
            var source = captured.Authority;
            var world = source.WorldAuthority!;
            var generation = world.Beta109Generation!;
            var players = Enumerable.Range(0, 2).Select(slot => new RuntimeContextAuthoritySnapshot(
                source.Character, source.Ascension, slot, 2, source.NoRunModifiers, source.VanillaNeowCatalogExact,
                source.AllCharacterCardPoolsUnlocked, source.UnlockedCommonCards, source.UnlockedUncommonCards,
                source.GameVersion, source.ProfileId, source.IsVanilla, source.SourceModId, source.SourceAssembly,
                source.SourceAuthority, source.Completeness, source.ResolutionStatus,
                missingRewards && slot == 0 ? source.EffectAuthority! with { CharacterRewardPool = null,
                    CharacterRewardPoolExact = false, CharacterRewardIdentityPoolExact = false } : source.EffectAuthority,
                world with { Beta109Generation = generation with {
                    GameMode = WorldGameMode.Multiplayer, IsMultiplayer = true, PlayerCount = 2, PersonalPlayerSlot = slot,
                    PartyBossDiscoveryExact = true, RelicInitializationExact = !missingWorld && generation.RelicInitializationExact,
                    LobbyPlayers = Enumerable.Range(0, 2).Select(i => new Beta109LobbyPlayerSnapshot(i, source.Character.CharacterKey, false, true)).ToArray(),
                    PartyRelicBuckets = Enumerable.Repeat(generation.PlayerRelicBuckets, 2).ToArray()
                } }, WorldGameMode.Multiplayer, PredictionGameModeAuthority.ExplicitRequest)).ToArray();
            return captured with { Authority = players[0], Party = new OrderedPartyAuthority(players,
                ["CapturedSoloPools.FixedSlotFixture", "CapturedSoloPools.FixedSlotFixture"], players[0].WorldAuthority!) };
        }
        SearchQuery Table(SearchQuery first, SearchQuery last) => SearchQuery.Empty with { Players =
            [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty) { Conditions = last }] };
        ExactSearchExecutionRequest Plan(SearchContext context, SearchQuery query)
        {
            var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, 1, 1, 1));
            check(compiled.Success, "Independent party repro compiles the actual authored request: " + compiled.Issue);
            return compiled.Plan!;
        }
        ProductionExactSearchResult Exact(SearchContext context, SearchQuery query) =>
            ProductionExactSearchEvaluator.Evaluate(Plan(context, query), input);
        var healthy = Context(false, false);
        var owner = healthy.Party!.Players[1];
        int actualTrial = Beta111TrialTinkerProjector.TrialCase(input.RootHash, 1);
        SearchQuery Event(bool match) => SearchQuery.Empty with { EventResultConditions =
            [new(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL")) {
                TrialCase = (TrialCaseTarget)(match ? actualTrial : (actualTrial + 1) % 3) }] };
        var merchantAuthority = Beta111MerchantColorlessAuthority.From(owner);
        var shop = Beta111NormalMerchantColorlessSequenceProjector.Project(input.RootHash, merchantAuthority, 1);
        check(shop.Precision == PredictionPrecision.Exact && merchantAuthority.UncommonPool.Count > 1,
            "Independent Shop repro has actual exact copied pool authority");
        var actualShop = shop.Merchants.Single().UncommonColorless;
        SearchQuery Shop(bool match) => SearchQuery.Empty with { MerchantColorlessConditions =
            [new(1, MerchantColorlessSlot.Uncommon, match ? actualShop : merchantAuthority.UncommonPool.First(k => k != actualShop))] };
        foreach (var canonical in new Func<bool, SearchQuery>[] { Event, Shop })
        {
            check(Exact(healthy, Table(SearchQuery.Empty, canonical(true))).IsMatch,
                "Matching canonical-only party request remains confirmed");
            var single = Plan(healthy, Table(SearchQuery.Empty, canonical(false)));
            var child = ExactSearchExecutionRequestFactory.ForPlayer(single, single.CompiledSearch.PlayerSearches[1]);
            check(ProductionExactSearchEvaluator.Evaluate(child, input).Evaluation.Disposition == SearchDisposition.NoMatch,
                "Actual standalone P2 Exact proves the independent contradiction");
        }
        var incompleteWorld = Context(true, false);
        var initial = PartySeedInformation.Project(seed, healthy.Party!);
        var worldPredicate = SearchQuery.Empty with { LegacyWorld = LegacyWorldSemanticConstraints.Empty with {
            BossFilters = [new(1, new([initial.Bosses.First(b => b.Act == 1).BossKey], [], []))] } };
        var missingRelics = SearchQuery.Empty with { RelicSequenceConstraints =
            [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1,
                new([BaseGameModelKeys.OrdinaryRelics.Whetstone], [], []))] };
        var missingCards = SearchQuery.Empty with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
            [captured.Authority.EffectAuthority!.CharacterRewardPool!.First().CardKey]) };
        foreach (var (name, context, first, shared) in new[] {
            ("shared World", incompleteWorld, SearchQuery.Empty, worldPredicate),
            ("unselected P1 relic", incompleteWorld, missingRelics, SearchQuery.Empty),
            ("selected-continuation P1 reward", Context(false, true), missingCards, SearchQuery.Empty) })
        {
            SearchQuery Query(SearchQuery last) => Table(first, last) with { LegacyWorld = shared.LegacyWorld };
            // Keep the family projection entry point identical in the baseline;
            // a shared-W-only request uses the separate initial-information path.
            var unresolved = Exact(context, Query(Event(true)));
            check(unresolved.Evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported,
                name + " baseline is unresolved through the real evaluator: " + unresolved.Evaluation.FailureCode);
            string before = JsonSerializer.Serialize(context.Party!.Players);
            foreach (var canonical in new Func<bool, SearchQuery>[] { Event, Shop })
            {
                var matching = Exact(context, Query(canonical(true)));
                check(matching.Evaluation.Disposition == unresolved.Evaluation.Disposition &&
                    matching.FailureCode == unresolved.FailureCode && matching.Evaluation.Witnesses.Count == 0,
                    name + ": local Match retains the original unresolved cause and cannot manufacture a joint witness");
                var mismatch = Exact(context, Query(canonical(false)));
                check(mismatch.Evaluation.Disposition == SearchDisposition.NoMatch && !mismatch.IsMatch &&
                    ReferenceEquals(mismatch.Authority, context.Authority),
                    name + ": independent P2 contradiction rejects the whole request after unresolved predecessor");
                var scope = mismatch.Evaluation.Evidence.Single(e => e.Code == "Party.ChildEvaluationScope");
                check(scope.Ordinal == 2 && scope.RelatedKey == owner.Character.CharacterKey,
                    name + ": rejection provenance identifies the actual P2 observation");
            }
            check(JsonSerializer.Serialize(context.Party.Players) == before,
                name + ": independent checks preserve copied authority inputs");
        }
    }

    private static void CheckPartyUnverifiedAuthority(SearchContext captured, Action<bool, string> check)
    {
        const string seed = "000000000KL6"; // The real four-player P4 failure at ordinal 22650.
        var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
        var characters = BaseGameModelKeys.Characters.All.Take(4).ToArray();
        var world = RelicSequenceFixtureFactory.CreateModern();
        // Always-available typed boundary fixture. Its missing C pool is explicit;
        // it is not represented as a native captured multiplayer reward model.
        var players = characters.Select((character, slot) =>
        {
            var owner = Beta110FastSearchFixtureAuthority.CreateBeta111(character, 10, slot, 4);
            var effects = owner.EffectAuthority! with { CharacterRewardPool = null,
                CharacterRewardPoolExact = false, CharacterRewardIdentityPoolExact = false };
            return owner.WithEffectAuthority(effects).WithWorldAuthority(world with {
                CapturedProfileId = RuntimeProfileId.Beta111,
                Beta109Generation = world.Beta109Generation! with { Profile = RuntimeProfileId.Beta111,
                    CharacterKey = character, GameMode = WorldGameMode.Multiplayer, IsMultiplayer = true,
                    PlayerCount = 4, PersonalPlayerSlot = slot, PartyBossDiscoveryExact = true,
                    LobbyPlayers = characters.Select((key, index) => new Beta109LobbyPlayerSnapshot(index, key, false, true)).ToArray(),
                    PartyRelicBuckets = Enumerable.Repeat(world.Beta109Generation.PlayerRelicBuckets, 4).ToArray() }
            });
        }).ToArray();
        var party = new OrderedPartyAuthority(players, Enumerable.Repeat("ControlledMissingCForBoundary", 4).ToArray(), players[0].WorldAuthority!);
        var context = captured with { Authority = players[0], CharacterKey = characters[0], Party = party };
        foreach (int activeSlot in new[] { 0, 3 })
        {
            var query = SearchQuery.Empty with { Players = Enumerable.Range(0, 4).Select(slot =>
                new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) { Conditions = slot == activeSlot
                    ? SearchQuery.Empty with { CombatCardRewards = new(6, CombatRewardSequenceOrderMode.Unordered,
                        [new ModelKey("CARD", "BETA111_TRANSFORM_19"), null, null, null, null, null]) }
                    : SearchQuery.Empty }).ToArray() };
            var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, 1, int.MaxValue, 1));
            check(compiled.Success, "Controlled mixed-character party request preserves a missing C observation");
            var request = compiled.Plan!;
            var unknown = ProductionExactSearchEvaluator.Evaluate(request, input);
            check((unknown.Evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported) && !unknown.IsMatch,
                "Missing personal C remains an actual unresolved model outcome");
            check(ReferenceEquals(unknown.Authority, request.Authority), "P1 and P4 non-Match return the parent authority");
            if (activeSlot == 3)
            {
                var scope = unknown.Evaluation.Evidence.Single(e => e.Code == "Party.ChildEvaluationScope");
                check(scope.Ordinal == 4 && scope.RelatedKey == characters[3] && scope.AuthorityFingerprint!.Length > 0,
                    "Whole-table unresolved result preserves its actual child slot/character provenance");
                CheckPartyUnknownPublication(request, unknown.Evaluation, check);
            }
        }
        // An explicit native-capture regression can be supplied by the existing
        // Host exporter. The committed check never depends on an ignored path.
        string? nativeContext = Environment.GetEnvironmentVariable("RT2_PARTY_EXACT_REPRO_CONTEXT");
        string? nativeWorkload = Environment.GetEnvironmentVariable("RT2_PARTY_EXACT_REPRO_WORKLOAD");
        if (nativeContext is null && nativeWorkload is null) return;
        if (string.IsNullOrWhiteSpace(nativeContext) || string.IsNullOrWhiteSpace(nativeWorkload))
            throw new ArgumentException("Native party Exact repro needs both context and workload paths");
        var realContext = ProductionSearchReplay.Read<ProductionSearchReplayContext>(nativeContext).Context;
        var realWorkload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(nativeWorkload);
        check(realContext.Party is { Players.Count: 4 } realParty && realParty.Players[3].Character.CharacterKey == BaseGameModelKeys.Characters.Regent,
            "Native regression retains the actual four-player Regent P4 owner");
        var realCompiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(realWorkload.Query, realContext),
            realWorkload.Options with { StartSeed = seed, ScanCount = 1, TargetMatchCount = int.MaxValue });
        check(realCompiled.Success, "Native four-player ordinal 22650 request remains admissible");
        var real = realCompiled.Plan!;
        var result = ProductionExactSearchEvaluator.Evaluate(real, input);
        check(result.Evaluation.Disposition == SearchDisposition.Unknown && result.Evaluation.FailureCode == "NoEligibleUnpinnedRewardRoute" &&
            result.FailureCode == "NoEligibleUnpinnedRewardRoute" && ReferenceEquals(result.Authority, real.Authority),
            "Native ordinal 22650 preserves the original Unknown cause in the parent envelope");
        var provenance = result.Evaluation.Evidence.Single(e => e.Code == "Party.ChildEvaluationScope");
        check(provenance.Ordinal == 4 && provenance.RelatedKey == BaseGameModelKeys.Characters.Regent && result.Evaluation.Witnesses.Count == 0,
            "Native unresolved child has scoped provenance without a fabricated Match witness");
        CheckPartyUnknownPublication(real, result.Evaluation, check);
    }

    private static void CheckPartyUnknownPublication(ExactSearchExecutionRequest request, SearchQueryEvaluation evaluation,
        Action<bool, string> check)
    {
        var execution = FamilyPlanner.InOrder([]); // Exercise the one actual terminal root; filtering is not under test.
        var session = new ProductionSearchSession(request, execution, FamilySearchEtaProjectorV1.Project(request, execution), 1, 4, null, "");
        try
        {
            check(session.Completion.Wait(15000), "Party unresolved terminal publication completes");
            var progress = session.GetProgress();
            var outputs = new List<SearchCandidate>();
            while (session.TryReadCandidate(out var candidate)) outputs.Add(candidate!);
            check(progress.State == SearchRunState.Completed && progress.MatchCount == 0 && progress.UnverifiedCandidateCount == 1 &&
                progress.PendingCandidateCount == 0 && outputs.Count == 1 && string.IsNullOrEmpty(progress.FailureCode),
                "A real unresolved child is delivered once unverified, without a session fault or fake pending work");
            var output = outputs.Single();
            check(output.CharacterKey == request.CharacterKey && ReferenceEquals(output.Authority, request.Authority) &&
                output.PredictionRequest.Character.CharacterKey == request.CharacterKey &&
                ReferenceEquals(output.PredictionRequest.Authority, request.Authority) && output.PredictionRequest.PlayerSlotIndex == 0,
                "Party candidate metadata and its unobserved request agree on the parent identity");
            string issue = evaluation.FailureCode;
            if (evaluation.RouteRejections.Count > 0)
                issue += "\n" + string.Join("\n", evaluation.RouteRejections.Select(r =>
                    $"route={r.RouteId};disposition={r.Disposition};reason={r.Reason}"));
            foreach (var scope in evaluation.Evidence.Where(e => e.Code == "Party.ChildEvaluationScope").Take(4))
                issue += $"\nsource=P{scope.Ordinal};character={scope.RelatedKey}";
            check(output.ValidationIssue.Contains("source=P4;character=CHARACTER:REGENT", StringComparison.Ordinal),
                "Persisted unresolved output names its actual P4 Regent child source");
            check(output.ValidationDisposition == evaluation.Disposition && output.ValidationIssue == issue && output.Document is null &&
                output.MatchEvidence.Count == 0 && output.Witnesses.Count == 0,
                "Unverified publication retains the original cause and has no proof payload");
            const ulong ordinal = 22650;
            check(session.GetSafeNextOrdinal() == ordinal + 1 && session.CaptureContinuation()!.PendingCandidates.Count == 0,
                "Completed unresolved root advances the trustworthy scan frontier without pending loss");
            check(session.CaptureContinuation()!.NextOrdinal == ordinal, "Unacknowledged unresolved output remains durable work");
            session.AcknowledgeCandidate(output.Seed);
            check(session.CaptureContinuation()!.NextOrdinal == ordinal + 1,
                "Acknowledged unresolved output advances the durable cursor once");
        }
        finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void CheckCandidatePublication(
        ExactSearchExecutionRequest fixture,
        ProductionExactSearchResult positive,
        ulong start,
        Action<bool, string> check)
    {
        // Reuse one already verified Exact result. Only two synthetic publications
        // run per case; no seed search or native/GPU execution is needed.
        var request = fixture with { RunOptions = fixture.RunOptions with
            { ScanCount = 2, TargetMatchCount = 2, WorkerCount = 2 } };
        var execution = FamilyPlanner.InOrder([]);
        var firstReserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask BeforePublish(int reservation, CancellationToken token)
        {
            if (reservation == 1)
            {
                firstReserved.TrySetResult();
                await releaseFirst.Task.WaitAsync(token).ConfigureAwait(false);
            }
            else
                await firstReserved.Task.WaitAsync(token).ConfigureAwait(false);
        }
        var reordered = new ProductionSearchSession(request, execution,
            FamilySearchEtaProjectorV1.Project(request, execution), 2, 1, null, "",
            exactEvaluator: (_, _) => positive, beforeCandidatePublish: BeforePublish);
        try
        {
            check(firstReserved.Task.Wait(10000), "First match reservation is paused");
            SearchCandidate? publishedFirst = null;
            check(SpinWait.SpinUntil(() => reordered.TryReadCandidate(out publishedFirst), 10000),
                "Later reservation publishes while the first is paused");
            check(SpinWait.SpinUntil(() => reordered.GetProgress().MatchCount >= 1, 10000),
                "Published candidate becomes visible in progress");
            check(reordered.GetProgress().MatchCount == 1,
                "Reserved but unpublished candidate is not counted");
            check(!reordered.Completion.IsCompleted && reordered.GetProgress().State == SearchRunState.Running,
                "Last reservation cannot finish or cancel an earlier publication");
            check(reordered.GetSafeNextOrdinal() == start,
                "In-flight Exact batch does not advance the safe cursor");
            releaseFirst.TrySetResult();
            check(reordered.Completion.Wait(10000), "All reserved candidates finish publishing");
            var emitted = new List<SearchCandidate> { publishedFirst! };
            while (reordered.TryReadCandidate(out var candidate)) emitted.Add(candidate!);
            var progress = reordered.GetProgress();
            check(progress.State == SearchRunState.Completed && progress.MatchCount == 2 && emitted.Count == 2,
                "Automatic completion counts exactly the published target");
            check(emitted.Select(candidate => candidate.Seed).Distinct().Count() == 2,
                "Reordered publications retain both distinct candidates");
            check(reordered.GetSafeNextOrdinal() == start + 2,
                "Successful reordered publications commit the complete prefix");
        }
        finally
        {
            releaseFirst.TrySetResult();
            reordered.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // Leave a capacity-one result queue full. The second reservation must
        // remain cancellable and must not inflate the count or safe cursor.
        request = request with { RunOptions = request.RunOptions with { WorkerCount = 1 } };
        var secondReserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask ObserveReservation(int reservation, CancellationToken _)
        {
            if (reservation == 2) secondReserved.TrySetResult();
            return ValueTask.CompletedTask;
        }
        var cancelled = new ProductionSearchSession(request, execution,
            FamilySearchEtaProjectorV1.Project(request, execution), 2, 1, null, "",
            exactEvaluator: (_, _) => positive, beforeCandidatePublish: ObserveReservation);
        try
        {
            check(secondReserved.Task.Wait(10000), "Full result queue has a second reserved match");
            check(cancelled.GetProgress().MatchCount == 1 && !cancelled.Completion.IsCompleted,
                "Full result queue only counts the first published match");
            cancelled.Cancel();
            check(cancelled.Completion.Wait(10000), "Cancellation terminates with a full result queue");
            int emitted = 0;
            while (cancelled.TryReadCandidate(out _)) emitted++;
            var progress = cancelled.GetProgress();
            check(progress.State == SearchRunState.Cancelled && progress.MatchCount == 1 && emitted == 1,
                "Cancellation preserves the published count and buffered candidate");
            check(cancelled.GetSafeNextOrdinal() == start + 1,
                "Unpublished cancelled candidate remains resumable");
            var continuation = cancelled.CaptureContinuation()!;
            check(continuation.NextOrdinal == start && continuation.PendingCandidates.Count == 0 &&
                continuation.UnreceivedOrdinals!.SequenceEqual(new[] { start }),
                "Cancellation abandons the blocked publication while retaining the actual buffered result for delivery");
        }
        finally
        {
            cancelled.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class SelectedWorldFault : IFamilyInvocation
    {
        public string FamilyId=>"W.World";
        public FamilyAnalyticalCostProjection AnalyticalCost=>new(FamilyId,8,[],"Injected");
        public FamilySurvivalProjection Survival=>FamilySurvivalProjection.Resolved(FamilyId,.5,"Injected");
        public FamilyConditionPerformanceProjection ConditionPerformance=>new(FamilyId,"InjectedW","Injected",1,"Injected",usesGpu:false);
        public FamilyPerformanceObservation CapturePerformanceObservation()=>new(ConditionPerformance,"",0,0,0,0,0,0,0,0,false,"Injected");
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context,FamilyObservationWindow window,FamilyCandidateSet input,CancellationToken token)
            =>ValueTask.FromException<FamilyCandidateSet>(new InvalidOperationException("InjectedSelectedWFailure"));
    }
}
