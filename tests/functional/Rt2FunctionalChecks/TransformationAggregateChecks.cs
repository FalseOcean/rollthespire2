using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;

internal static class TransformationAggregateChecks
{
    internal static int Run(string? openingFilter = null)
    {
        if (openingFilter is not (null or "bones" or "bones-with-controls" or "direct"))
            throw new ArgumentException("Expected bones, bones-with-controls or direct.", nameof(openingFilter));
        int checks = 0;
        string output = Environment.GetEnvironmentVariable("RT2_TRANSFORMATION_FIXTURE_DIR") ?? ".build-evidence/transformation-aggregate";
        var jointFixtures = new List<object>();
        int? jointOffset = null;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        var json = ProductionSearchReplay.JsonOptions;
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        using var captured = JsonDocument.Parse(File.ReadAllText("docs/source_audits/AUTHORED_MORPHIC_20260913.evidence.json"));
        var targets = captured.RootElement.GetProperty("Evidence")[0].GetProperty("Targets").Deserialize<MorphicGroveTarget[]>(json)!
            .Select((t,i) => t with { InstanceId = "basic:" + i }).ToArray();
        var scenario = new MorphicGroveScenario(context.Authority, new("Authored Morphic Group / Aroma LetGo / Whisper Hug at initial-Basic entry; occurrence not proven", true, false, null), targets);
        var condition = new TransformationAggregateCondition(TransformationOpening.None, TransformationPickupOrder.Any,
            true, true, true, TransformationAggregatePredicate.RareCountAtLeast, 2, []) { EventScenario = scenario };
        var options = new SearchRunOptions("000000000000", 16387, int.MaxValue, 1);
        ExactSearchExecutionRequest Request(SearchQuery q)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, context), options);
            if (!result.Success) throw new InvalidOperationException(result.Issue);
            return result.Plan!;
        }
        SearchQuery Query(TransformationAggregateCondition c) => SearchQuery.Empty with { TransformationAggregate = c };
        void CheckJointNeow(ExactSearchExecutionRequest request, string name)
        {
            if(request.Evaluation.NeowRoute is null) {
                Check(!NeowTransformationComposite.TryCreate(request,NeowReplayPlan.Compile(request,false),out _),"Unbound N identity outside trial");
                request=Request(request.CompiledSearch.NormalizedQuery with { OpeningRoute=new(BaseGameModelKeys.Relics.NeowsBones),
                    OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.LeafyPoultice,BaseGameModelKeys.Relics.NewLeaf]) });
            }
            var neow = NeowReplayPlan.Compile(request, false);
            Check(NeowTransformationComposite.TryCreate(request, neow, out var joint) && joint is not null, "N joint admitted " + name);
            var nMatch = NeowFamilyReplay.Bind(neow);
            ulong[] expected = Enumerable.Range(0, (int)options.ScanCount).Select(i => (ulong)i).Where(i => {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
                bool match = nMatch(root) && joint!.Numerical.Matches(root);
                Check(match == ProductionExactSearchEvaluator.Evaluate(request,
                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(i))).IsMatch,
                    "N joint complete CPU/Exact population " + name);
                return match;
            }).ToArray();
            Check(jointOffset is null || jointOffset == joint!.Gpu.JointTransformMetaOffset, "N joint fixture shared metadata layout");
            jointOffset = joint!.Gpu.JointTransformMetaOffset;
            foreach (int mode in new[] { 0, 1, 2 })
            {
                uint[] meta = joint.Gpu.Meta.ToArray(); if (mode == 1) meta[66] = 0;
                ulong[] ordinals = Enumerable.Range(0, (int)options.ScanCount).Select(i => (ulong)i)
                    .Where(i => mode != 2 || i % 4 == 0 || Array.BinarySearch(expected, i) >= 0).ToArray();
                jointFixtures.Add(new { name = name + "." + mode, compact = mode == 2, start = 0UL,
                    count = mode == 2 ? ordinals.Length : (int)options.ScanCount, ordinals,
                    expected = mode == 2 ? expected.Where(i => Array.BinarySearch(ordinals, i) >= 0).ToArray() : expected,
                    gpu = joint.Gpu with { Meta = meta } });
            }
        }
        Check(!SearchCompiler.Compile(SearchQuery.Empty, context).SemanticFingerprint.Contains("transformationAggregate="), "Ordinary fingerprint format unchanged");
        var cases = new List<TransformationAggregateCondition> { condition, condition with { MinimumRareCount = 4 } };
        var higherFullTargets = new HashSet<TransformationAggregateCondition>();
        foreach (var opening in new[] { TransformationOpening.LeafyPoultice, TransformationOpening.NewLeaf, TransformationOpening.BonesLeafyNewLeaf })
            cases.Add(condition with { Opening = opening, MinimumRareCount = 2 });
        cases.Add(condition with { Opening = TransformationOpening.BonesLeafyNewLeaf, PickupOrder = TransformationPickupOrder.LeafyThenNewLeaf });
        cases.Add(condition with { Opening = TransformationOpening.BonesLeafyNewLeaf, PickupOrder = TransformationPickupOrder.NewLeafThenLeafy });

        // Targets are sourced from three independent authoritative event draws,
        // not preassigned per-source predicates. Freeze a genuinely duplicate case.
        ModelKey[] Raw(ulong root) => Beta111MorphicGroveProjector.Project(root, MorphicGroveCommitment.InitialBasics, scenario.Premises, targets)
            .Transforms.Select(t => t.RawReplacement!.CardKey).Concat(new[] {
                Beta111SingleBasicTransformProjector.Project(root, 0, SingleBasicTransformEvent.AromaOfChaos, scenario.Premises, targets[0]).RawReplacement!.CardKey,
                Beta111SingleBasicTransformProjector.Project(root, 0, SingleBasicTransformEvent.WhisperingHollow, scenario.Premises, targets[0]).RawReplacement!.CardKey }).ToArray();
        var raw0 = Raw(Beta111Profile.Instance.ComputeRootSeed(options.StartSeed));
        cases.Add(condition with { Predicate = TransformationAggregatePredicate.ContainsMultiset, MinimumRareCount = 0, TargetMultiset = [raw0[0], raw0[2], raw0[3]] });
        ulong duplicateRoot = 0; ModelKey duplicate = default, other = default;
        for (ulong i = 0; i < 8192; i++)
        {
            var root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i)); var raw = Raw(root);
            var pair = raw.GroupBy(k => k).FirstOrDefault(g => g.Count() >= 2);
            if (pair is null || raw.All(k => k == pair.Key)) continue;
            duplicateRoot = i; duplicate = pair.Key; other = raw.First(k => k != pair.Key); break;
        }
        Check(duplicate.IsValid, "Duplicate witness exists");
        cases.Add(condition with { Predicate = TransformationAggregatePredicate.ContainsMultiset, MinimumRareCount = 0, TargetMultiset = [duplicate, duplicate, other] });
        // Frozen real Bones witness: two N outcomes plus one E output. Neither
        // leaf alone can decide the source-agnostic three-card predicate.
        var bonesRoot = Beta111Profile.Instance.ComputeRootSeed("0000000001FA");
        var bonesProjection = new RolltheSpire2.Core.Effects.NeowEffectProjectionEngine(Beta111Profile.Instance, "test", bonesRoot, context.Authority)
            .Project(BaseGameModelKeys.Relics.NeowsBones);
        var nCards = bonesProjection.BonesOutcome!.OriginalRoutes[0].RelicScopedResults.SelectMany(r => r.EffectGroups)
            .SelectMany(g => g.OrderedItems).Where(i => i.Kind == RolltheSpire2.Core.Effects.PredictedEffectKind.TransformCard).Select(i => i.TargetKey!.Value).ToArray();
        foreach (var order in new[]{TransformationPickupOrder.Any,TransformationPickupOrder.LeafyThenNewLeaf,TransformationPickupOrder.NewLeafThenLeafy})
        foreach (int count in new[] { 1, 2, 3 })
        {
            var joint = new TransformationAggregateCondition(TransformationOpening.BonesLeafyNewLeaf, order,
                false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0, nCards.Take(count).Reverse().ToArray());
            var pair = order == TransformationPickupOrder.NewLeafThenLeafy
                ? new[] { BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.LeafyPoultice }
                : new[] { BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf };
            var request = Request(Query(joint) with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, pair,
                    order == TransformationPickupOrder.Any ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder) });
            var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, "0000000001FA"));
            Check(exact.IsMatch && new TransformationAggregateNumericalPlan(request).Matches(bonesRoot), "Bones-only partial/full multiset Exact/numerical witness");
            if (order != TransformationPickupOrder.Any)
                Check(exact.Evaluation.Evidence.Where(e => e.Code == "TransformationAggregateMatched").All(e => e.AcquisitionOrder!.SequenceEqual(pair)), "Joint evidence binds requested pickup route");
            if (order == TransformationPickupOrder.Any) CheckJointNeow(request, "JointN.M" + count);
            else Check(!NeowTransformationComposite.TryCreate(request, NeowReplayPlan.Compile(request, false), out _), "N joint pinned outside bounded trial");
            if (count == 3) cases.Add(joint);
        }
        var repeatedBones = new TransformationAggregateCondition(TransformationOpening.BonesLeafyNewLeaf, TransformationPickupOrder.Any,
            false, false, false, TransformationAggregatePredicate.ContainsMultiset, 0, [nCards[0], nCards[0], nCards[1]]);
        Check(TransformationAggregateValidator.EvaluateOutputs(repeatedBones, [new(nCards[0], false), new(nCards[0], false), new(nCards[1], false)]) == SearchDisposition.Match,
            "Bones joint AAB counts repeated instances");
        CheckJointNeow(Request(Query(repeatedBones)), "JointN.AAB");
        CheckJointNeow(Request(Query(repeatedBones with { Predicate=TransformationAggregatePredicate.RareCountAtLeast,
            MinimumRareCount=1, TargetMultiset=[] })), "JointN.Rare1");
        var fullBones = repeatedBones with { TargetMultiset=nCards };
        CheckJointNeow(Request(Query(fullBones with { Predicate=TransformationAggregatePredicate.ContainsMultisetAndRemainingRare })), "JointN.MixedFull");
        var finalCurse = bonesProjection.BonesOutcome!.OriginalRoutes[0].SharedContinuation.FinalCurseKey!.Value;
        var curseRequest = Request(Query(fullBones) with { OpeningRoute=new(BaseGameModelKeys.Relics.NeowsBones),
            OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.LeafyPoultice,BaseGameModelKeys.Relics.NewLeaf]),
            StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.NeowsBones,
            NeowStructuredConditionKind.ExactSingle,NeowStructuredEffectScope.FinalCurse,NeowStructuredOutputKind.Curse,[finalCurse])] });
        CheckJointNeow(curseRequest, "JointN.FinalCurse");
        var curseN = new NeowFamily(curseRequest, NeowReplayPlan.Compile(curseRequest, false));
        var curseT = new TransformationAggregateFamily(curseRequest);
        double curseMass = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(curseRequest)).Probability!.Value;
        Check(Math.Abs(curseN.Survival.SurvivalProbability!.Value * curseT.ResolveSurvival(new HashSet<string>{"N.Neow"}).SurvivalProbability!.Value - curseMass) < 1e-14,
            "N then T counts shared identity and final curse once");
        Check(Math.Abs(curseT.Survival.SurvivalProbability!.Value * curseN.ResolveSurvival(new HashSet<string>{TransformationAggregateFamily.Id}).SurvivalProbability!.Value - curseMass) < 1e-14,
            "T then N has the same terminal mass");
        Check(NeowTransformationComposite.TryCreate(curseRequest, curseN.PricingReplayPlan, out var curseComposite), "Joint allocation remains admitted");
        var jointOwner = new NeowFamily(curseRequest, curseN.PricingReplayPlan, transformations: curseComposite);
        Check(jointOwner.Coverage.ToHashSet().SetEquals(["N.Neow", TransformationAggregateFamily.Id]) &&
            Math.Abs(jointOwner.Survival.SurvivalProbability!.Value - curseMass) < 1e-14, "Joint coverage and mass equal serial alternatives");
        Check(curseT.GpuPlan.BonesOptimized == (Environment.GetEnvironmentVariable("RT2_T_BONES_EXPERIMENT") != "baseline"),
            "Admitted Bones optimization defaults on without a forced order");
        var previousBackend = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity().RenderingBackend;
        try
        {
            SearchPerformanceProfileFoundation.ObserveRenderingBackend("d3d12");
            IFamilyInvocation[] nt = [curseN, curseT];
            Check(jointOwner.QuotePhysicalWork(new(false,false,1<<24)) is {NanosecondsPerInput: >0}, "Joint physical owns its dense quote");
            Check(jointOwner.QuotePhysicalWork(new(true,false,100)) is null, "Unmeasured joint compact remains unpriced");
            Check(curseT.QuotePhysicalWork(new(true,false,100)) is {FixedWindowMilliseconds: >0}, "Sparse compact T work charged by window");
            Check(curseT.QuotePhysicalWork(new(true,false,1<<20)) is null, "Sparse T quote does not extrapolate to full compact");
            var selectedUnits = FamilyPlanner.PlanSimple(nt, [jointOwner]);
            Check(selectedUnits.Stages.SelectMany(stage => stage.Coverage).ToHashSet().SetEquals(["N.Neow", TransformationAggregateFamily.Id]),
                "The fused numerical body competes as one greedy unit without losing serial obligations");
            SearchPerformanceProfileFoundation.ObserveRenderingBackend("vulkan");
            Check(jointOwner.QuotePhysicalWork(new(false,false,1<<24)) is null && curseT.QuotePhysicalWork(new(false,false,1<<24)) is null,
                "D3D12 reference quotes do not claim Vulkan measurements");

        }
        finally { SearchPerformanceProfileFoundation.ObserveRenderingBackend(string.IsNullOrEmpty(previousBackend) ? "unknown" : previousBackend); }
        var namedSeven = condition with { Opening=TransformationOpening.BonesLeafyNewLeaf,
            Predicate=TransformationAggregatePredicate.ContainsMultiset,MinimumRareCount=0,TargetMultiset=nCards.Concat(Raw(bonesRoot)).ToArray() };
        cases.Add(namedSeven);
        cases.Add(namedSeven with { Predicate=TransformationAggregatePredicate.ContainsMultisetAndRemainingRare });
        cases.Add(condition with { Opening = TransformationOpening.BonesLeafyNewLeaf,
            Predicate = TransformationAggregatePredicate.ContainsMultiset, MinimumRareCount = 0, TargetMultiset = [nCards[0], nCards[2], Raw(bonesRoot)[2]] });
        foreach (var opening in new[] { TransformationOpening.LeafyPoultice, TransformationOpening.NewLeaf, TransformationOpening.BonesLeafyNewLeaf })
            cases.Add(condition with { Opening = opening, MorphicGrove = false, AromaOfChaos = false, WhisperingHollow = false, EventScenario = null, MinimumRareCount = 1 });
        var singleScenario = new MorphicGroveScenario(context.Authority, scenario.Premises, [targets[0]]);
        cases.Add(condition with { MorphicGrove = false, AromaOfChaos = true, WhisperingHollow = false, MinimumRareCount = 1, EventScenario = singleScenario });
        cases.Add(condition with { MorphicGrove = false, AromaOfChaos = false, WhisperingHollow = true, MinimumRareCount = 1, EventScenario = singleScenario });

        // Saturated target multisets may draw any required identity from Leafy;
        // do not assign a fixed pair to it. Freeze real distinct/duplicate witnesses.
        var leafyProbe = new TransformationAggregateNumericalPlan(Request(Query(condition with { Opening=TransformationOpening.LeafyPoultice })));
        var fullKinds = new HashSet<bool>();
        bool leafyDouble=false;
        for(ulong i=0;i<8192 && (fullKinds.Count<2 || !leafyDouble);i++)
        {
            ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
            if(!NeowFamilyReplay.Observe(root,leafyProbe.Neow!).IdentityPass)continue;
            var projected=new RolltheSpire2.Core.Effects.NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,context.Authority)
                .Project(BaseGameModelKeys.Relics.LeafyPoultice);
            var cards=projected.EffectGroups.SelectMany(g=>g.OrderedItems)
                .Where(x=>x.Kind==RolltheSpire2.Core.Effects.PredictedEffectKind.TransformCard).Select(x=>x.TargetKey!.Value).Concat(Raw(root)).ToArray();
            bool duplicateFull=cards.Distinct().Count()<cards.Length;
            if (!leafyDouble && cards[0]==cards[1])
            {
                leafyDouble=true;
                cases.Add(condition with {Opening=TransformationOpening.LeafyPoultice,MorphicGrove=false,AromaOfChaos=false,
                    WhisperingHollow=false,EventScenario=null,Predicate=TransformationAggregatePredicate.ContainsMultiset,
                    MinimumRareCount=0,TargetMultiset=cards[..2]});
            }
            if(!fullKinds.Add(duplicateFull))continue;
            cases.Add(condition with {Opening=TransformationOpening.LeafyPoultice,Predicate=TransformationAggregatePredicate.ContainsMultiset,
                MinimumRareCount=0,TargetMultiset=cards});
        }
        Check(fullKinds.Count==2 && leafyDouble,"Full target distinct and duplicate positive witnesses");

        var symbiote = condition with { MorphicGrove=false,AromaOfChaos=false,WhisperingHollow=false,Symbiote=true,MinimumRareCount=1,EventScenario=singleScenario };
        var trial = symbiote with {Symbiote=false,TrialNondescript=true,EventScenario=scenario};
        cases.Add(symbiote); cases.Add(trial);
        cases.Add(condition with {Symbiote=true,TrialNondescript=true});
        cases.Add(condition with {Opening=TransformationOpening.BonesLeafyNewLeaf,Symbiote=true,TrialNondescript=true,MinimumRareCount=1});
        // Positive full-target witness exercises all added draws and Carry8's trial gate.
        for(ulong ordinal=0;ordinal<16387;ordinal++) {
            ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            if(!NeowFamilyReplay.Observe(root,leafyProbe.Neow!).IdentityPass)continue;
            var t=EventResultTransformSemantics.Project(root,new(EventResultConditionKind.TrialNondescriptInitialBasicsContains,default){MorphicGroveScenario=scenario});
            if(t is not {Count:2})continue;
            var sym=EventResultTransformSemantics.Project(root,new(EventResultConditionKind.SymbioteInitialBasicTransform,default){MorphicGroveScenario=singleScenario})!;
            var n=new RolltheSpire2.Core.Effects.NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,context.Authority).Project(BaseGameModelKeys.Relics.LeafyPoultice);
            var cards=n.EffectGroups.SelectMany(g=>g.OrderedItems).Where(x=>x.Kind==RolltheSpire2.Core.Effects.PredictedEffectKind.TransformCard).Select(x=>x.TargetKey!.Value)
                .Concat(Raw(root)).Concat(sym.Select(x=>x.CardKey)).Concat(t.Select(x=>x.CardKey)).ToArray();
            cases.Add(condition with {Opening=TransformationOpening.LeafyPoultice,Symbiote=true,TrialNondescript=true,Predicate=TransformationAggregatePredicate.ContainsMultiset,MinimumRareCount=0,TargetMultiset=cards});
            break;
        }
        // Seven is a benchmark size. Exercise the current larger contributor
        // combinations with authoritative positive witnesses and dynamic packing.
        var bonesProbe=new TransformationAggregateNumericalPlan(Request(Query(condition with {Opening=TransformationOpening.BonesLeafyNewLeaf})));
        for(ulong ordinal=0;ordinal<262144 && higherFullTargets.Count==0;ordinal++) {
            ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            if(!NeowFamilyReplay.Observe(root,bonesProbe.Neow!).IdentityPass)continue;
            var t=EventResultTransformSemantics.Project(root,new(EventResultConditionKind.TrialNondescriptInitialBasicsContains,default){MorphicGroveScenario=scenario});
            if(t is not {Count:2})continue;
            var sym=EventResultTransformSemantics.Project(root,new(EventResultConditionKind.SymbioteInitialBasicTransform,default){MorphicGroveScenario=singleScenario})!;
            var n=new RolltheSpire2.Core.Effects.NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,context.Authority).Project(BaseGameModelKeys.Relics.NeowsBones);
            var opening=n.BonesOutcome!.OriginalRoutes[0].RelicScopedResults.SelectMany(r=>r.EffectGroups).SelectMany(g=>g.OrderedItems)
                .Where(x=>x.Kind==RolltheSpire2.Core.Effects.PredictedEffectKind.TransformCard).Select(x=>x.TargetKey!.Value).ToArray();
            foreach(var flags in new[]{(Sym:true,Trial:false),(Sym:false,Trial:true),(Sym:true,Trial:true)}) {
                var cards=opening.Concat(Raw(root)).Concat(flags.Sym?sym.Select(x=>x.CardKey):[]).Concat(flags.Trial?t.Select(x=>x.CardKey):[]).ToArray();
                var c=condition with {Opening=TransformationOpening.BonesLeafyNewLeaf,Symbiote=flags.Sym,TrialNondescript=flags.Trial,
                    Predicate=TransformationAggregatePredicate.ContainsMultiset,MinimumRareCount=0,TargetMultiset=cards};
                var request=Request(Query(c)); var gpu=new TransformationAggregateGpuPlan(new(request));
                Check(c.OpportunityCount==cards.Length && cards.Length is >=8 and <=10 && gpu.Supported,"Larger joint result count and GPU packing admitted");
                Check(Environment.GetEnvironmentVariable("RT2_T_BONES_EXPERIMENT")=="baseline" || gpu.FullTarget,"Carry8 full-target admission is based on actual opportunity count");
                Check(ProductionExactSearchEvaluator.Evaluate(request,TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,Beta110SeedCodec.FormatOrdinal(ordinal))).IsMatch,
                    "Larger full-target authoritative positive witness");
                higherFullTargets.Add(c); cases.Add(c);
                Console.WriteLine($"TRANSFORMATION_CAPACITY opportunities={cards.Length};groups={gpu.Buffers[0][5]};witness={Beta110SeedCodec.FormatOrdinal(ordinal)};fullTarget={gpu.FullTarget}");
            }
        }
        Check(higherFullTargets.Count==3,"Eight, nine and ten-result positive cases found");
        foreach(var companion in new[]{BaseGameModelKeys.Relics.ArcaneScroll,BaseGameModelKeys.Relics.Kaleidoscope,BaseGameModelKeys.Relics.HeftyTablet})
        foreach(var order in new[]{TransformationPickupOrder.Any,TransformationPickupOrder.LeafyThenCompanion,TransformationPickupOrder.CompanionThenLeafy})
            cases.Add(condition with {Opening=TransformationOpening.BonesLeafyOther,CompanionRelic=companion,PickupOrder=order,
                MorphicGrove=false,AromaOfChaos=false,WhisperingHollow=false,EventScenario=null,MinimumRareCount=1});
        var uncertainCompanion=condition with {Opening=TransformationOpening.BonesLeafyOther,CompanionRelic=BaseGameModelKeys.Relics.LargeCapsule,
            MorphicGrove=false,AromaOfChaos=false,WhisperingHollow=false,EventScenario=null,MinimumRareCount=1};
        var uncertainPlan=new TransformationAggregateNumericalPlan(Request(Query(uncertainCompanion)));
        Check(!uncertainPlan.Closed && !new TransformationAggregateGpuPlan(uncertainPlan).Supported &&
            uncertainPlan.ProbabilityClosed && TransformationAggregateProbability.Build(uncertainPlan).QueryHitProbability.HasValue,
            "Unspecified companion uses approved marginal probability without widening runtime/GPU continuation proof");
        for (ulong root = 0; root < 32; root++)
            Check(uncertainPlan.Matches(root) == uncertainPlan.MatchesCollected(root),
                "Probability closure leaves the non-closed runtime collector path intact");
        Check(new TransformationAggregateGpuPlan(new(Request(Query(uncertainCompanion with {PickupOrder=TransformationPickupOrder.LeafyThenCompanion})))).Supported,
            "Leafy-first needs no predecessor companion projection");
        var trialProbability=TransformationAggregateProbability.Build(new(Request(Query(trial))));
        double rareMass=targets[0].OrderedSourceCandidates!.Count(c=>c.Rarity=="Rare")/(double)targets[0].OrderedSourceCandidates!.Count;
        Check(Math.Abs(trialProbability.QueryHitProbability!.Value-(1-Math.Pow(1-rareMass,2))/3)<1e-12,"Trial case probability paid once");
        // A tiny captured pool allows exhaustive probability checks independent
        // of the aggregate DP, including repeated identities and Rare targets.
        var commonCard = targets[0].OrderedSourceCandidates!.First(c => c.Rarity != "Rare");
        var rareCards = targets[0].OrderedSourceCandidates!.Where(c => c.Rarity == "Rare").DistinctBy(c => c.CardKey).Take(2).ToArray();
        MorphicGroveCard[] tinyPool = [commonCard, commonCard, rareCards[0], rareCards[1]];
        // Both initial Basics use the same ordered source pool, as required by
        // the authored product shape. Independent draws identify both endpoints.
        var endpointScenario = new MorphicGroveScenario(context.Authority, scenario.Premises,
            targets.Select(target => target with { OrderedSourceCandidates = tinyPool }).ToArray());
        var endpoint = new TransformationAggregateNumericalPlan(Request(Query(condition with {
            AromaOfChaos=false, WhisperingHollow=false, MinimumRareCount=2, EventScenario=endpointScenario })));
        Check(endpoint.UsesStreaming, "Closed endpoint uses streaming");
        bool firstFailure = false, finalFailure = false, bothSatisfy = false;
        for (ulong root = 0; root < 256; root++)
        {
            var draws = new Beta110FastRng(unchecked(root + XxHash64.Hash("MORPHIC_GROVE"u8, 0)));
            bool firstRare = tinyPool[draws.NextInt(tinyPool.Length)].Rarity == "Rare";
            bool secondRare = tinyPool[draws.NextInt(tinyPool.Length)].Rarity == "Rare";
            firstFailure |= !firstRare;
            finalFailure |= firstRare && !secondRare;
            bothSatisfy |= firstRare && secondRare;
            Check(endpoint.Matches(root) == endpoint.MatchesCollected(root) &&
                endpoint.Matches(root) == (firstRare && secondRare),
                "Streaming endpoints retain duplicate source occurrence weights");
        }
        Check(firstFailure && finalFailure && bothSatisfy, "First-draw failure, final-draw failure and success all exercised");
        var tinyScenario = new MorphicGroveScenario(context.Authority, scenario.Premises,
            targets.Select(t => t with { OrderedSourceCandidates = tinyPool }).ToArray());
        var mixed = trial with { Symbiote=true, Predicate=TransformationAggregatePredicate.ContainsMultisetAndRemainingRare,
            MinimumRareCount=0, TargetMultiset=[commonCard.CardKey], EventScenario=tinyScenario };
        foreach (ModelKey[] requested in new[] { new[] { commonCard.CardKey }, new[] { rareCards[0].CardKey },
            new[] { commonCard.CardKey,commonCard.CardKey }, new[] { commonCard.CardKey,rareCards[0].CardKey,rareCards[1].CardKey } })
        {
            var c = mixed with { TargetMultiset=requested }; int hits=0;
            foreach(var a in tinyPool) foreach(var b in tinyPool) foreach(var d in tinyPool)
            {
                var left = new List<MorphicGroveCard> { a,b,d }; bool contains=true;
                foreach(var key in requested) { int at=left.FindIndex(x=>x.CardKey==key); if(at<0){contains=false;break;} left.RemoveAt(at); }
                bool expected = contains && left.All(x=>x.Rarity=="Rare");
                if(expected) hits++;
                Check((TransformationAggregateValidator.EvaluateOutputs(c,[new(a.CardKey,a.Rarity=="Rare"),new(b.CardKey,b.Rarity=="Rare"),new(d.CardKey,d.Rarity=="Rare")])==SearchDisposition.Match)==expected,
                    "Mixed predicate uses distinct target instances and requires every remaining result Rare");
            }
            var prob=TransformationAggregateProbability.Build(new(Request(Query(c))));
            Check(Math.Abs(prob.QueryHitProbability!.Value-hits/64d/3)<1e-12,"Mixed probability equals exhaustive 4^3 pool enumeration, Trial once");
            cases.Add(c);
        }
        Check(TransformationAggregateValidator.EvaluateOutputs(mixed,[new(null,true),new(commonCard.CardKey,false),new(rareCards[0].CardKey,true)])==SearchDisposition.Match,
            "Unknown Rare identity need not consume the known non-Rare target's slot");
        Check(TransformationAggregateValidator.EvaluateOutputs(mixed,[new(null,false),new(rareCards[0].CardKey,true),new(rareCards[1].CardKey,true)])==SearchDisposition.Unknown,
            "Unknown identity can satisfy target only provisionally");
        Check(TransformationAggregateValidator.EvaluateOutputs(mixed,[new(commonCard.CardKey,false),new(commonCard.CardKey,false),new(null,null)])==SearchDisposition.NoMatch,
            "Extra known non-Rare copy fails even when another output is unknown");
        Check(TransformationAggregateValidator.EvaluateOutputs(mixed,[new(commonCard.CardKey,false),new(rareCards[0].CardKey,null),new(rareCards[1].CardKey,true)])==SearchDisposition.Unknown,
            "Unproven remainder rarity cannot become Exact");
        // Real pools exercise each new event alone and the opening donor on GPU.
        cases.Add(symbiote with {Predicate=mixed.Predicate,MinimumRareCount=0,TargetMultiset=[commonCard.CardKey]});
        cases.Add(trial with {Predicate=mixed.Predicate,MinimumRareCount=0,TargetMultiset=[rareCards[0].CardKey]});
        var mixedLeafy = condition with {Opening=TransformationOpening.LeafyPoultice,MorphicGrove=false,AromaOfChaos=false,
            WhisperingHollow=false,EventScenario=null,Predicate=mixed.Predicate,MinimumRareCount=0,TargetMultiset=[commonCard.CardKey]};
        cases.Add(mixedLeafy);
        // A bound N predicate and T inspect these same two draws, so their
        // probability is a joint event rather than two multiplied marginals.
        var mixedLeafyPlan = new TransformationAggregateNumericalPlan(Request(Query(mixedLeafy)));
        var bound = new NeowStructuredEffectSearchCondition(BaseGameModelKeys.Relics.LeafyPoultice,NeowStructuredConditionKind.ExactSingle,
            NeowStructuredEffectScope.TransformResults,NeowStructuredOutputKind.Card,[rareCards[0].CardKey]);
        var pools = mixedLeafyPlan.DrawGroups.Single().Pools;
        int ci=mixedLeafyPlan.LocalId(commonCard.CardKey), ri=mixedLeafyPlan.LocalId(rareCards[0].CardKey);
        double mixedJoint=pools[0].Sum(a=>pools[1].Count(b=>a==ci&&b==ri||a==ri&&b==ci))/(double)(pools[0].Length*pools[1].Length);
        double actualJoint=TransformationAggregateProbability.WithNeowConditions(mixedLeafyPlan,[bound])!.Value;
        Check(Math.Abs(actualJoint-mixedJoint*TransformationAggregateProbability.Build(mixedLeafyPlan).IdentityProbability)<1e-12,"Mixed T/N same-draw joint mass");
        var gpuFixtures = new List<object>();
        var fingerprints = new HashSet<string>();
        Directory.CreateDirectory(output);
        int checkedCases = 0;
        for (int index = 0; index < cases.Count; index++)
        {
            var c = cases[index];
            if (openingFilter == "direct")
            {
                if (c is not { Opening: TransformationOpening.LeafyPoultice or TransformationOpening.NewLeaf, UsesEvents: false }) continue;
            }
            else if (openingFilter is not null && !c.IsBones &&
                !(openingFilter == "bones-with-controls" && c is {
                    Opening: TransformationOpening.LeafyPoultice or TransformationOpening.NewLeaf,
                    UsesEvents: false, Predicate: TransformationAggregatePredicate.RareCountAtLeast, MinimumRareCount: 1 })) continue;
            checkedCases++;
            var query = Query(c); var request = Request(query);
            Check(fingerprints.Add(request.SnapshotFingerprint), "Contributor/order/predicate fingerprint unique");
            var restored = JsonSerializer.Deserialize<SearchQuery>(JsonSerializer.Serialize(query, json), json)!;
            Check(Request(restored).SnapshotFingerprint == request.SnapshotFingerprint, "JSON cursor identity");
            var numerical = new TransformationAggregateNumericalPlan(request); var expected = new List<ulong>();
            int accepted = 0;
            for (ulong i = 0; i < (ulong)options.ScanCount; i++)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(i); var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                var exact = ProductionExactSearchEvaluator.Evaluate(request, root);
                Check(exact.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch,
                    $"Exact must resolve case={index} ordinal={i}: {exact.Evaluation.FailureCode}");
                bool fast = numerical.Matches(root.RootHash);
                Check(fast == numerical.MatchesCollected(root.RootHash), "Streaming / collector complete population parity");
                Check(fast == exact.IsMatch, $"Fast/Exact parity case={index} ordinal={i} fast={fast} exact={exact.IsMatch}");
                if (fast) expected.Add(i);
                if (exact.IsMatch) { accepted++; Check(exact.Document is not null && exact.Evaluation.Evidence.Any(e => e.Code == "TransformationAggregateMatched"), "Positive aggregate Witness"); }
            }
            Check(accepted > 0 || higherFullTargets.Contains(c) || c.Opening==TransformationOpening.BonesLeafyOther || c is {Opening:TransformationOpening.BonesLeafyNewLeaf,TrialNondescript:true}, "Representative positive interval case=" + index);
            var execution = new FamilyExecutionContext(false);
            IFamilyInvocation owner = new TransformationAggregateFamily(request);
            Check(owner.Coverage.SequenceEqual(new[] { TransformationAggregateFamily.Id }), "Indivisible single Coverage");
            Check(owner.Survival.IsResolved, "T owned modeled survival");
            var probability = TransformationAggregateProbability.Build(numerical);
            Check(((TransformationAggregateFamily)owner).GpuPlan.Supported, "Closed bounded T GPU admitted");
            Check(probability.QueryHitProbability == probability.StageSurvival && probability.LocalRareDistributions.All(d => Math.Abs(d.Sum()-1)<1e-12), "Closed T probability/distributions");
            Check(JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(request)).Probability is { } p && Math.Abs(p-probability.QueryHitProbability!.Value)<1e-12, "T-only Query probability");
            owner = owner.CpuRealizations.Single();
            var batch = new SearchBatch(0, (int)options.ScanCount); var input = FamilyCandidateSet.Dense(batch);
            var actual = owner.InvokeAsync(execution, new(batch, request), input, default).GetAwaiter().GetResult();
            Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected), "Real CPU ordered ABI1/tail");
            var zero = FamilyCandidateSet.FromAbi1(batch, []);
            Check(owner.InvokeAsync(execution, new(batch, request), zero, default).Result.Count == 0, "Zero input");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { owner.InvokeAsync(execution, new(batch, request), input, canceled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation missing"); }
            catch (OperationCanceledException) { checks++; }
            var sparse = FamilyCandidateSet.FromAbi1(batch, expected.Where((_,i) => i % 2 == 0).ToArray());
            Check(owner.InvokeAsync(execution, new(batch, request), sparse, default).Result.Count == sparse.Count, "Resume sparse survivors");
            owner.DisposeAsync(execution).GetAwaiter().GetResult(); execution.DisposeAsync().GetAwaiter().GetResult();
            var gpuPlan = new TransformationAggregateGpuPlan(numerical);
            string shader = $"T-{index}.glsl";
            File.WriteAllText(output+"/"+shader,gpuPlan.ShaderSource());
            if(accepted==0 && (higherFullTargets.Contains(c) || c.Opening==TransformationOpening.BonesLeafyOther)) {
                ulong witness=0;
                for(ulong i=(ulong)options.ScanCount;i<262144;i++) {
                    var root=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,Beta110SeedCodec.FormatOrdinal(i));
                    if(!numerical.Matches(root.RootHash))continue;
                    Check(ProductionExactSearchEvaluator.Evaluate(request,root).IsMatch,"Additional positive agrees with Exact"); witness=i;break;
                }
                Check(witness!=0,"Additional positive found outside fixed main interval");
                gpuFixtures.Add(new {name=$"T-{index}-positive",family="T",shader,buffers=gpuPlan.Buffers,fullTarget=gpuPlan.FullTarget,
                    batchMeta=new uint[]{(uint)witness,(uint)(witness>>32),1,1,0},ordinals=new uint[]{0},expected=new ulong[]{0}});
            }
            foreach(bool compact in new[]{false,true}) {
                uint[] ordinals=Enumerable.Range(0,(int)options.ScanCount).Where(i=>!compact||i%3==0).Select(i=>(uint)i).ToArray();
                gpuFixtures.Add(new {name=$"T-{index}",family="T",shader,buffers=gpuPlan.Buffers,fullTarget=gpuPlan.FullTarget,
                    batchMeta=new uint[]{0,0,(uint)options.ScanCount,(uint)ordinals.Length,compact?1u:0u},ordinals,
                    expected=expected.Where(i=>!compact||i%3==0).ToArray()});
            }
            File.WriteAllText($"{output}/case-{index}.json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, query, options), json));
            File.WriteAllText($"{output}/case-{index}.expected.json", JsonSerializer.Serialize(expected.Select(i => Beta110SeedCodec.FormatOrdinal(i)), json));
            Console.WriteLine($"TRANSFORMATION_PARITY case={index};opening={c.Opening};order={c.PickupOrder};predicate={c.Predicate};roots={options.ScanCount};accepted={accepted};falseNegative=0");
        }
        if (openingFilter is not null)
        {
            Check(checkedCases > 0, "Opening filter must select existing parity cases");
            Console.WriteLine($"TRANSFORMATION_PARITY_FILTER opening={openingFilter};cases={checkedCases};totalCases={cases.Count};rootsPerCase={options.ScanCount}");
        }
        Check(TransformationAggregateValidator.EvaluateOutputs(cases[8], [new(duplicate,false),new(other,false),new(other,false),new(other,false)]) == SearchDisposition.NoMatch, "AAB is not AB set membership");
        void Reject(SearchQuery q, string label) { try { Request(q); throw new Exception("Missing rejection:" + label); } catch (ArgumentException) { checks++; } }
        Reject(Query(condition with { Opening = TransformationOpening.NewLeaf }) with { OpeningRoute = new(BaseGameModelKeys.Relics.LeafyPoultice) }, "Conflicting opening commitments rejected");
        foreach (var opening in new[]{TransformationOpening.LeafyPoultice,TransformationOpening.NewLeaf,TransformationOpening.BonesLeafyNewLeaf})
        {
            var c = condition with { Opening=opening };
            var q = Query(c) with { OpeningRoute=new(c.RequiredOpeningKey),
                OpeningRouteRelicRequirement=opening==TransformationOpening.BonesLeafyNewLeaf
                    ?new(c.RequiredOpeningKey,[BaseGameModelKeys.Relics.LeafyPoultice,BaseGameModelKeys.Relics.NewLeaf]):null };
            var req=Request(q); var t=new TransformationAggregateFamily(req);
            Check(TransformationAggregateCondition.SharedNeowIdentityOnly(req.Evaluation), "Shared opening identity recognized");
            var p=JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(req)).Probability;
            Check(p is not null && Math.Abs(p.Value-t.Probability.QueryHitProbability!.Value)<1e-12, "Shared opening not multiplied twice");
            Check(NeowFamily.TryCreate(req,out var n) && n is not null,"N remains registered beside T");
            Check(n!.ResolveSurvival(new HashSet<string>{TransformationAggregateFamily.Id}).SurvivalProbability==1,"N identity discharged after T");
            Check(Math.Abs(t.ResolveSurvival(new HashSet<string>{"N.Neow"}).SurvivalProbability!.Value*t.Probability.IdentityProbability-p!.Value)<1e-12,"T survival conditions on N");
            var extraOffers = q with { LegacyNeow = q.LegacyNeow with {
                NeowRelics = new([], [BaseGameModelKeys.Relics.Pomander], []) } };
            var extraOffersRequest = Request(extraOffers);
            var extraOffersMass = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(extraOffersRequest));
            var offerOnly = Request(extraOffers with { TransformationAggregate = null });
            var offerOnlyMass = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(offerOnly));
            Check(extraOffersMass.JointlyPriced && offerOnlyMass.JointlyPriced &&
                Math.Abs(extraOffersMass.Probability!.Value - p!.Value * offerOnlyMass.Probability!.Value / t.Probability.IdentityProbability) < 1e-12,
                "Extra authored N offers follow default independence and do not repay the shared T parent");
            var pureRequest=Request(Query(c));
            for(ulong i=0;i<1027;i++)
            {
                var root=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,Beta110SeedCodec.FormatOrdinal(i));
                Check(ProductionExactSearchEvaluator.Evaluate(req,root).IsMatch==ProductionExactSearchEvaluator.Evaluate(pureRequest,root).IsMatch,"Shared N/T Exact parity");
            }
            File.WriteAllText($"{output}/shared-{opening}.json",JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1,q,options),json));
            if(opening==TransformationOpening.BonesLeafyNewLeaf)
            {
                var additional=q with { StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.NewLeaf,NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.TransformResults,NeowStructuredOutputKind.Card,[nCards[2]])] };
                var extraRequest=Request(additional); var extraT=new TransformationAggregateFamily(extraRequest);
                Check(extraRequest.Evaluation.StructuredNeowEffects.Count==1,"Additional N condition retained beside T");
                var sharedProbability=JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(extraRequest)).Probability;
                Check(sharedProbability is >0 && sharedProbability<=p,"Overlapping N/T uses a same-draw joint, bounded by T marginal");
                var redundant = q with { StructuredOpeningEffects=[new(BaseGameModelKeys.Relics.NewLeaf,NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.TransformResults,NeowStructuredOutputKind.Card,[nCards[2]]),
                    new(BaseGameModelKeys.Relics.NewLeaf,NeowStructuredConditionKind.ExactSingle,NeowStructuredEffectScope.TransformResults,NeowStructuredOutputKind.Card,[nCards[2]])] };
                Check(Math.Abs(JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(Request(redundant))).Probability!.Value-sharedProbability!.Value)<1e-12,"Repeated N fact does not repay shared output");
                var curses=extraRequest.Authority.EffectAuthority!.GeneratedCursePool!;
                var withCurse=additional with {StructuredOpeningEffects=[..additional.StructuredOpeningEffects,
                    new(BaseGameModelKeys.Relics.NeowsBones,NeowStructuredConditionKind.ExactSingle,
                        NeowStructuredEffectScope.FinalCurse,NeowStructuredOutputKind.Curse,[curses[0]])]};
                double curseJoint=JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(Request(withCurse))).Probability!.Value;
                Check(Math.Abs(curseJoint-sharedProbability!.Value*curses.Count(k=>k==curses[0])/curses.Count)<1e-12,"T/N final curse is conditional, identity paid once");
                var extraN = new NeowFamily(extraRequest, NeowReplayPlan.Compile(extraRequest, false));
                Check(Math.Abs(extraT.ResolveSurvival(new HashSet<string>{"N.Neow"}).SurvivalProbability!.Value * extraN.Survival.SurvivalProbability!.Value - sharedProbability!.Value)<1e-12,
                    "N-first same-draw target intersection uses existing joint model");
                Check(Math.Abs(extraN.ResolveSurvival(new HashSet<string>{TransformationAggregateFamily.Id}).SurvivalProbability!.Value * extraT.Survival.SurvivalProbability!.Value - sharedProbability!.Value)<1e-12,
                    "T-first same-draw target intersection equals N-first");
                Check(ProductionExactSearchEvaluator.Evaluate(extraRequest,TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,"0000000001FA")).IsMatch,
                    "Additional N and T share a positive authoritative witness");
                File.WriteAllText(output+"/shared-extra.json",JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1,additional,options),json));
                var reversed=q with {OpeningRouteRelicRequirement=new(c.RequiredOpeningKey,[BaseGameModelKeys.Relics.NewLeaf,BaseGameModelKeys.Relics.LeafyPoultice],BonesRouteOrderMode.ExactOrder)};
                Reject(reversed,"Conflicting pickup order rejected");
                _=Request(reversed with {TransformationAggregate=c with{PickupOrder=TransformationPickupOrder.NewLeafThenLeafy}});
                checks++;
            }
        }
        Reject(Query(symbiote) with {EventResultConditions=[new(EventResultConditionKind.SymbioteInitialBasicTransform,raw0[0]){MorphicGroveScenario=singleScenario}]},"Symbiote owned once");
        Reject(Query(trial) with {EventResultConditions=[new(EventResultConditionKind.TrialCase,new("EVENT","TRIAL")){TrialCase=TrialCaseTarget.Nondescript}]},"Trial case owned once");
        Reject(Query(condition) with { EventResultConditions = [new(EventResultConditionKind.MorphicGroveGroupInitialBasicsContains, raw0[0]) { MorphicGroveScenario = scenario }] }, "E duplicate ownership");
        Reject(Query(condition with { TargetMultiset = [raw0[0]] }), "No hidden mixed predicates");
        var unresolvedRelic = Query(condition with { Opening = TransformationOpening.NewLeaf }) with {
            RelicSequenceConstraints = [new(RelicSequenceKind.Shop, SearchSequenceRangeMode.FirstN, 1, new([new("RELIC","ANCHOR")], [], []))] };
        var unresolvedRelicRequest = Request(unresolvedRelic);
        Check(TransformationAggregateCondition.HasUnresolvedRelicContinuation(unresolvedRelicRequest.CompiledSearch.NormalizedQuery),
            "Unimplemented T relic continuation remains an authored obligation");
        Check(!FamilyExecutionCoordinator.CreateRegisteredFamilies(unresolvedRelicRequest, false).Any(f=>f.Coverage.Contains("R.Relic")),
            "Initial relic bag cannot reject an unresolved T continuation");
        var unclosed = condition with { EventScenario = new(context.Authority, scenario.Premises with { VanillaLocalDependenciesBound = false }, targets) };
        var unknownRequest = Request(Query(unclosed));
        var unknownNumerical = new TransformationAggregateNumericalPlan(unknownRequest);
        Check(!unknownNumerical.UsesStreaming && unknownNumerical.Matches(bonesRoot), "Unclosed donor conservatively retained outside streaming");
        Check(TransformationAggregateValidator.Evaluate(unknownRequest, bonesRoot, context.Authority).Disposition == SearchDisposition.Unknown, "Unknown remains Unknown");
        Check(TransformationAggregateProbability.Build(new(unknownRequest)).QueryHitProbability is null && !new TransformationAggregateFamily(unknownRequest).GpuPlan.Supported, "Unclosed authority has no invented probability or GPU admission");
        Reject(Query(condition with { EventScenario = new(context.Authority, scenario.Premises, [targets[0] with { OrderedSourceCandidates = [] }, targets[1]]) }), "Corrupt empty pool rejected");
        var faultRequest = Request(Query(condition));
        string? previousGpuControl = Environment.GetEnvironmentVariable("RT2_T_BONES_EXPERIMENT");
        try
        {
            Environment.SetEnvironmentVariable("RT2_T_BONES_EXPERIMENT", "InjectedInvalidGpuControl");
            var cpuOnly = new TransformationAggregateFamily(faultRequest, preferGpu: false);
            var preparationFailed = new TransformationAggregateFamily(faultRequest);
            Check(!cpuOnly.GpuPlan.Supported && cpuOnly.GpuPreparationFailureDetail.Length == 0,
                "CPU T preparation does not parse optional GPU controls");
            Check(!preparationFailed.GpuPlan.Supported && preparationFailed.GpuPreparationFailureDetail.Contains("UnknownTBonesControl"),
                "Optional T preparation failure preserves CPU and original cause");
            for (ulong ordinal = 0; ordinal < 32; ordinal++)
                Check(cpuOnly.Plan.Matches(ordinal) == preparationFailed.Plan.Matches(ordinal), "T preparation failure preserves every CPU predicate");
            var cpuSelection = FamilyPlanner.InOrder(((IFamilyInvocation)preparationFailed).CpuRealizations.ToArray());
            Check(FamilyExecutionCoordinator.SelectedPreparationFallbacks([preparationFailed], cpuSelection).Count == 1,
                "Selected T CPU replacement retains GPU failure evidence");
        }
        finally { Environment.SetEnvironmentVariable("RT2_T_BONES_EXPERIMENT", previousGpuControl); }
        var ownerForFault = new TransformationAggregateFamily(faultRequest);
        IFamilyInvocation selectedGpu = new TransformationAggregateFamily(faultRequest);
        var noDevice = new FamilyExecutionContext(false);
        var gpuFaultBatch = new SearchBatch(0, 3);
        try { selectedGpu.InvokeAsync(noDevice,new(gpuFaultBatch,faultRequest),FamilyCandidateSet.Dense(gpuFaultBatch),default).GetAwaiter().GetResult(); throw new Exception("Selected GPU silently recovered"); }
        catch (InvalidOperationException ex) when(ex.Message=="FamilyGpuOwnerUnavailable") { checks++; }
        try { selectedGpu.InvokeAsync(noDevice,new(gpuFaultBatch,faultRequest),FamilyCandidateSet.Dense(gpuFaultBatch),default).GetAwaiter().GetResult(); throw new Exception("Faulted GPU owner reused"); }
        catch (InvalidOperationException ex) when(ex.Message=="T.InvocationFaulted") { checks++; }
        noDevice.DisposeAsync().GetAwaiter().GetResult();
        var orderProbabilities = cases.Skip(4).Take(3).Select(c=>TransformationAggregateProbability.Build(new(Request(Query(c)))).QueryHitProbability).ToArray();
        Check(orderProbabilities.Distinct().Count()==1, "Any/pinned closed Bones routes do not double probability");
        var knownCpu = ((IFamilyInvocation)ownerForFault).CpuRealizations.Single();
        var knownSetup = knownCpu.QuotePhysicalWork(new(false, false, 4096));
        Check(knownSetup?.SetupMilliseconds==0, "Known CPU setup does not acquire a GPU owner fee");
        var faultCpu = new FamilyCpuExecution(faultRequest, ownerForFault, "InjectedFault", _ => throw new InvalidOperationException("InjectedNumericalFault"), 1, _ => null);
        var faultBatch = new SearchBatch(0, 3); var faultInput = FamilyCandidateSet.Dense(faultBatch); var faultContext = new FamilyExecutionContext(false);
        try { ((IFamilyInvocation)faultCpu).InvokeAsync(faultContext, new(faultBatch, faultRequest), faultInput, default).GetAwaiter().GetResult(); throw new Exception("Fault did not propagate"); }
        catch (FamilyCpuMatchException error) when (error.InnerException is InvalidOperationException { Message: "InjectedNumericalFault" } &&
            error.FamilyId == TransformationAggregateFamily.Id && error.Batch == faultBatch && error.GlobalOrdinal == 0) { checks++; }
        try { ((IFamilyInvocation)faultCpu).InvokeAsync(faultContext, new(faultBatch, faultRequest), faultInput, default).GetAwaiter().GetResult(); throw new Exception("Faulted owner reused"); }
        catch (InvalidOperationException) { checks++; }
        faultContext.DisposeAsync().GetAwaiter().GetResult();
        var permuted = cases[8] with { TargetMultiset = cases[8].TargetMultiset.Reverse().ToArray() };
        Check(Request(Query(permuted)).SnapshotFingerprint == Request(Query(cases[8])).SnapshotFingerprint, "Multiset canonical order");
        var combined = Query(condition) with {
            EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 5, new([new("EVENT","MORPHIC_GROVE")], [], []))],
            EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
        var combinedRequest = Request(combined);
        var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(combinedRequest);
        Check(registered.SelectMany(f => f.Coverage).Order().SequenceEqual(new[] { "E.EventResult", "T.TransformationAggregate", "W.World" }.Order()), "T + independent W + unrelated E");
        Check(JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(combinedRequest)).Probability is > 0, "Aggregate composes owned probability with W/E");
        var expectedCombined = new List<string>();
        for (ulong i = 0; i < (ulong)options.ScanCount; i++)
        {
            var root = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(i));
            var result = ProductionExactSearchEvaluator.Evaluate(combinedRequest, root);
            Check(result.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch, "Combined Exact closed");
            if (result.IsMatch) expectedCombined.Add(root.CanonicalSeed);
        }
        File.WriteAllText(output+"/combined.expected.json", JsonSerializer.Serialize(expectedCombined, json));
        File.WriteAllText(output+"/combined.json", JsonSerializer.Serialize(new ProductionSearchReplayWorkload(1, combined, options), json));
        File.WriteAllText(output+"/fixtures.json",JsonSerializer.Serialize(gpuFixtures));
        if (jointOffset is { } offset)
        {
            string directory = Path.Combine(output, "neow-joint"); Directory.CreateDirectory(directory);
            string[] names = ["stage0.glsl","stage1.glsl","stage2.glsl","shader.glsl","nr.glsl",
                "leafy-stage0.glsl","leafy-stage1.glsl","leafy-stage2.glsl","leafy-shader.glsl",
                "bones-nr-stage0.glsl","bones-nr-stage1.glsl","bones-nr-stage2.glsl","bones-nr.glsl",
                "bones-arcane-nr-stage0.glsl","bones-arcane-nr-stage1.glsl","bones-arcane-nr-stage2.glsl","bones-arcane-nr.glsl"];
            foreach (string file in names)
            {
                int stage = file.Contains("stage0") ? 0 : file.Contains("stage1") ? 1 : file.Contains("stage2") ? 2 : 3;
                File.WriteAllText(Path.Combine(directory, file), NeowFamilyGpuExecutor.ShaderSource(stage, jointTransformMetaOffset: offset));
            }
            File.WriteAllText(Path.Combine(directory, "fixtures.json"), JsonSerializer.Serialize(jointFixtures));
            Console.WriteLine($"N_JOINT_FIXTURES count={jointFixtures.Count};offset={offset}");
        }
        Console.WriteLine($"TRANSFORMATION_AGGREGATE_PASS checks={checks};duplicateWitnessOrdinal={duplicateRoot}");
        return 0;
    }
}
