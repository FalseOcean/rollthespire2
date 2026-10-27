using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Core.Seed;
using System.Runtime.CompilerServices;

internal static class FamilyAEChecks
{
 internal static int Run(int count)
 {
  var world=RelicSequenceFixtureFactory.CreateModern();
  var g=world.Beta109Generation!;
  // Synthetic accepted-authority fixture; not Owner/game runtime evidence.
  g=g with {Profile=RuntimeProfileId.Beta111,WorldGenerationHooksExact=true,
   FixtureValidationStatus=Beta109FixtureValidationStatus.VerifiedRealGame,
   VerifiedRealGameFixtureIds=g.AncientEventContexts.Select(c=>"ancient-option:"+c.AncientKey.Entry.ToLowerInvariant()+":synthetic").Concat(new[]{"ancient-identity:synthetic","boss:synthetic"}).ToArray()};
  world=world with {CapturedProfileId=RuntimeProfileId.Beta111,Beta109Generation=g};
  var authority=Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect,10,0,1).WithWorldAuthority(world);
  authority=authority.WithEffectAuthority(authority.EffectAuthority! with {EventResultStaticCatalogExact=true,EventColorfulCharacterPoolsExact=true,UnlockedCharacterCardPoolKeys=BaseGameModelKeys.Characters.All});
  var context=new SearchContext(RuntimeProfileId.Beta111,authority.Character.CharacterKey,10,authority,
    new("0.111.0","0.111.0","beta","AE-synthetic",true,""),SearchEvaluationAssumptions.BroadDefault);
  ExactSearchExecutionRequest Request(NeowSearchFilter f){var c=SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(f),context);return new(c,new("000000000000",10000,30,1),"000000000000",10000,CompiledSearchEvaluationProjector.Project(c).Evaluation,CombatRewardRoutePolicyContract.None,c.SemanticFingerprint);}
  var empty=NeowSearchFilter.Empty;
  // Same-offer All must use the source distribution, not a product of marginals.
  foreach(var ancient in g.AncientEventContexts.Where(c=>c.AncientKey.Entry is "NONUPEIPE" or "TANX")) {
   var row=new AncientSearchBranchCondition(ancient.Act,ancient.AncientKey,[],[]);
   var input=SearchSelectivityInput.From(Request(empty with {AncientBranchConditions=[row]}));
   var method=typeof(AncientOptionProbabilityEstimator).GetMethod(ancient.AncientKey.Entry=="TANX"?"BuildTanxPool":"BuildNonupeipePool",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
   var pool=(IReadOnlyList<ModelKey>)method.Invoke(null,[input,ancient.Catalog!])!;
   if(pool.Count<3 || pool.Distinct().Count()!=pool.Count) throw new Exception("All fixture requires distinct shuffle pool");
   foreach(int requiredCount in new[]{1,2,3}) {
    var targets=pool.Take(requiredCount).ToArray();
    double expected=1;for(int i=0;i<requiredCount;i++)expected*=(3d-i)/(pool.Count-i);
    var actual=AncientOptionProbabilityEstimator.EstimateConditionalAll(input,row,targets);
    if(actual.Probability is not {} p || Math.Abs(p-expected)>1e-12)throw new Exception("All shuffle hypergeometric");
    var allQuery=SearchQuery.Empty with {AncientBranches=[row],LegacyWorld=LegacyWorldSemanticConstraints.Empty with {AncientOptionFilters=[new(row.Act,new([],targets,[]))]}};
    var compiled=SearchCompiler.Compile(allQuery,context);
    var full=AncientProbabilityEstimator.Estimate(SearchSelectivityInput.From(compiled));
    if(full?.JointlyPriced!=true)throw new Exception("Workbench All query probability unclosed");
    var rq=new ExactSearchExecutionRequest(compiled,new("000000000000",64,30,1),"000000000000",64,CompiledSearchEvaluationProjector.Project(compiled).Evaluation,CombatRewardRoutePolicyContract.None,compiled.SemanticFingerprint);
    if(Math.Abs(new AncientOptionFamilyPlan(rq).Survival.SurvivalProbability!.Value-expected)>1e-12)throw new Exception("All physical survival");
   }
  }
  foreach(var ancient in g.AncientEventContexts.Where(c=>c.AncientKey.Entry is "PAEL" or "TEZCATARA" or "VAKUU")) {
   var row=new AncientSearchBranchCondition(ancient.Act,ancient.AncientKey,[],[]);
   var input=SearchSelectivityInput.From(Request(empty with {AncientBranchConditions=[row]}));
   IReadOnlyList<IReadOnlyList<ModelKey>> pools=ancient.AncientKey.Entry=="VAKUU"
    ? new[]{ancient.Catalog!.Pool("vakuu.pool1"),ancient.Catalog.Pool("vakuu.pool2"),ancient.Catalog.Pool("vakuu.pool3")}
    : (IReadOnlyList<IReadOnlyList<ModelKey>>)typeof(AncientOptionProbabilityEstimator).GetMethod(ancient.AncientKey.Entry=="PAEL"?"BuildPaelPools":"BuildTezcataraPools",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[input,ancient.Catalog!])!;
   foreach(var targets in new[]{new[]{pools[0][0],pools[0][1]},new[]{pools[0][0],pools[1][0]},new[]{pools[0][0],pools[1][0],pools[2][0]}}) {
    int hits=0,total=0;
    foreach(var a in pools[0])foreach(var b in pools[1])foreach(var c in pools[2]) {total++;if(targets.All(k=>new[]{a,b,c}.Contains(k)))hits++;}
    var actual=AncientOptionProbabilityEstimator.EstimateConditionalAll(input,row,targets);
    if(actual.Probability is not {} p || Math.Abs(p-(double)hits/total)>1e-12)throw new Exception("All separate-pool exhaustive");
   }
  }
  foreach(var ancient in g.AncientEventContexts.Where(c=>c.AncientKey.Entry=="OROBAS")) {
   var sea=new ModelKey("RELIC","SEA_GLASS"); var other=ancient.Catalog!.Pool("orobas.pool2")[0];
   var row=new AncientSearchBranchCondition(ancient.Act,ancient.AncientKey,[sea],[BaseGameModelKeys.Characters.Silent]);
   var input=SearchSelectivityInput.From(Request(empty with {AncientBranchConditions=[row]}));
   double seaP=AncientOptionProbabilityEstimator.EstimateConditional(input,row).Probability!.Value;
   var all=AncientOptionProbabilityEstimator.EstimateConditionalAll(input,row,[sea,other]);
   if(all.Probability is not {} p || Math.Abs(p-seaP/ancient.Catalog.Pool("orobas.pool2").Count)>1e-12)
    throw new Exception("Sea Glass refinement counted once with All");
   ulong testRoot=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(17));
   var eventContext=ancient with {EventRngRoot=Beta109WorldRng.DeriveEventLocalSeed(testRoot,ancient.PlayerSlot,ancient.IsShared,ancient.EventIdEntry)};
   var snapshot=g with {RunSeedRoot=testRoot,AncientEventContexts=[eventContext]};
   var broad=Beta109AncientOptionProvider.PredictEventLocal(snapshot,ancient.Act,ancient.AncientKey,0,
    authority.Character.CharacterKey,AncientOptionConditionProfile.BroadDefault);
   var locked=Beta109AncientOptionProvider.PredictEventLocal(snapshot,ancient.Act,ancient.AncientKey,0,
    authority.Character.CharacterKey,AncientOptionConditionProfile.BroadDefault with {
     OrobasTouchOfOrobasConditionMet=false,OrobasArchaicToothConditionMet=false});
   if(locked.Options.Count!=3 || locked.Options[2].OptionKey.IsValid || !locked.Options[2].IsLocked ||
      locked.Options[2].IsVisible || locked.Options[2].IsSelectable ||
      !locked.Options.Take(2).Select(o=>o.OptionKey).SequenceEqual(broad.Options.Take(2).Select(o=>o.OptionKey)) ||
      locked.RngCallCount!=broad.RngCallCount)
    throw new Exception("Orobas locked third option did not preserve the two relics and singleton RNG draw");
  }
  // UI interaction limits do not erase valid imported backend requirements.
  var modeRows=g.AncientEventContexts.Where(c=>c.Act==2).Select(c=>c.AncientKey).Distinct().Take(2)
   .Select(k=>new AncientSearchBranchCondition(2,k,[],[])).ToArray();
  if(modeRows.Length==2) {
   var allAcrossParents=SearchCompiler.Compile(SearchQuery.Empty with {AncientBranches=modeRows, LegacyWorld=LegacyWorldSemanticConstraints.Empty with {
    AncientOptionFilters=[new(2,new([], [new ModelKey("RELIC","TEST")],[]))]}},context);
   if(allAcrossParents.NormalizedQuery.AncientBranches.Count!=2 || allAcrossParents.NormalizedQuery.LegacyWorld.AncientOptionFilters.Single().Keys.All.Count!=1)
    throw new Exception("Multi-parent All intent was lost");
  }
  // Mixture equals the weighted sum of two independently evaluated variant pools.
  var group2=g.ActSelectionGroups.Single(x=>x.Act==2);
  var baseAct=g.OrderedActCatalog.First(x=>x.ActKey==group2.EligibleActsInSourceOrder[0]);
  var variant=baseAct with {ActKey=new ModelKey("ACT","PROBABILITY_TEST_VARIANT"),OrderedAncients=[new ModelKey("EVENT","OTHER_TEST_ANCIENT")]};
  var mixedGeneration=g with {SelectedActs=[],SelectedActsExact=false,
   OrderedActCatalog=g.OrderedActCatalog.Append(variant).ToArray(),ActSelectionGroups=g.ActSelectionGroups.Select(x=>x.Act==2?
    x with {EligibleActsInSourceOrder=[baseAct.ActKey,variant.ActKey],SelectionMode=Beta109ActSelectionMode.RandomNextItem}:x).ToArray()};
  var mixtureQuery=SearchQuery.Empty with {AncientBranches=[new(2,baseAct.OrderedAncients[0],[],[])]};
  double Price(Beta109WorldGenerationSnapshot generation) {
   var a=authority.WithWorldAuthority(world with {Beta109Generation=generation});
   var c=context with {Authority=a};
   var result=AncientProbabilityEstimator.Estimate(SearchSelectivityInput.From(SearchCompiler.Compile(mixtureQuery,c)))!;
   return result.Probability ?? throw new Exception(result.EvidenceCode+":"+result.Notes);
  }
  var probabilityLeft=mixedGeneration with {ActSelectionGroups=mixedGeneration.ActSelectionGroups.Select(x=>x.Act==2?x with {EligibleActsInSourceOrder=[baseAct.ActKey]}:x).ToArray()};
  var probabilityRight=mixedGeneration with {ActSelectionGroups=mixedGeneration.ActSelectionGroups.Select(x=>x.Act==2?x with {EligibleActsInSourceOrder=[variant.ActKey]}:x).ToArray()};
  if(Math.Abs(Price(mixedGeneration)-(Price(probabilityLeft)+Price(probabilityRight))/2)>1e-12)throw new Exception("Ancient variant mixture");
  var mixtureAuthority=authority.WithWorldAuthority(world with {Beta109Generation=mixedGeneration});
  var mixtureContext=context with {Authority=mixtureAuthority};
  var variantQuery=SearchQuery.Empty with {VariantBossBranches=[new(2,baseAct.ActKey,ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)]};
  double VariantP(SearchQuery query) => JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.Compile(query,mixtureContext))).Probability!.Value;
  if(Math.Abs(VariantP(variantQuery)-.5)>1e-12)throw new Exception("Variant-only query prior");
  if(Math.Abs(VariantP(variantQuery with {AncientBranches=mixtureQuery.AncientBranches})-.5*Price(probabilityLeft))>1e-12)
   throw new Exception("Variant/Ancient dependency was independently multiplied");
  double WorldRowP(SearchQuery query) => RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder
   .Build(SearchCompiler.Compile(query,mixtureContext)).Rows
   .Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.WorldEvent).Probability
   ?? throw new Exception("Priced variant query lost its W dashboard probability");
  if(Math.Abs(WorldRowP(variantQuery)-.5)>1e-12)throw new Exception("W dashboard omits Variant-only prior");
  var variantBossQuery=variantQuery with {VariantBossBranches=[variantQuery.VariantBossBranches[0] with {
   FirstBoss=new([baseAct.Bosses[0]],[],[])}]};
  if(Math.Abs(WorldRowP(variantBossQuery)-.5/baseAct.Bosses.Distinct().Count())>1e-12)
   throw new Exception("W dashboard omits Variant-scoped Boss predicate or prior");
  if(Math.Abs(WorldRowP(variantQuery with {AncientBranches=mixtureQuery.AncientBranches})-.5*Price(probabilityLeft))>1e-12)
   throw new Exception("W dashboard independently multiplies Variant and Ancient marginals");
  var unknownVariantContext=mixtureContext with {Authority=mixtureAuthority.WithWorldAuthority(world with {
   Beta109Generation=mixedGeneration with {ActSelectionAuthorityExact=false}})};
  var unknownVariantView=RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder.Build(
   SearchCompiler.Compile(variantQuery,unknownVariantContext));
  if(unknownVariantView.TotalProbability.HasValue || unknownVariantView.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.WorldEvent).Probability.HasValue)
   throw new Exception("Missing Variant prior must remain unknown in W dashboard and total");
  // Two authoring pages constrain one opening: use one joint offer, never two marginals.
  var bonesQuery=SearchQuery.Empty with {OpeningRoute=new(BaseGameModelKeys.Relics.NeowsBones),
   OpeningRouteRelicRequirement=new(BaseGameModelKeys.Relics.NeowsBones,[BaseGameModelKeys.Relics.LeafyPoultice,BaseGameModelKeys.Relics.NewLeaf])};
  CompiledSearch CompileN(SearchQuery query) => SearchCompiler.Compile(query,context);
  double PriceN(SearchQuery query) => JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(CompileN(query))).Probability ?? throw new Exception("Opening probability missing");
  var conflict=bonesQuery with {LegacyNeow=LegacyNeowSemanticConstraints.Empty with {NeowRelics=new([],[BaseGameModelKeys.Relics.LeafyPoultice],[])}};
  if(!SearchFeasibilityAnalyzer.Analyze(CompileN(conflict)).IsImpossible || PriceN(conflict)!=0)throw new Exception("Conflicting opening pages not rejected");
  var merged=bonesQuery with {LegacyNeow=LegacyNeowSemanticConstraints.Empty with {NeowRelics=new([],[BaseGameModelKeys.Relics.NutritiousOyster],[])}};
  var mergedExecution=RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(CompileN(merged),new("000000000000",100,1,1)).Plan!;
  var mergedReplay=NeowReplayPlan.Compile(mergedExecution,false);
  double expectedOffer=NeowFamilyProjections.IdentityProbability(mergedReplay,mergedReplay.Filter);
  if(PriceN(merged)<=0 || PriceN(merged)>=PriceN(bonesQuery) || Math.Abs(PriceN(merged)-expectedOffer)>1e-12)throw new Exception("Opening conjunction differs from finite replay model");
  var curse=authority.EffectAuthority!.GeneratedCursePool!.First();
  var curseCondition=new NeowStructuredEffectSearchCondition(BaseGameModelKeys.Relics.NeowsBones,NeowStructuredConditionKind.ExactSingle,
   NeowStructuredEffectScope.FinalCurse,NeowStructuredOutputKind.Curse,[curse]);
  if(Math.Abs(PriceN(merged with {StructuredOpeningEffects=[curseCondition]})/PriceN(bonesQuery with {StructuredOpeningEffects=[curseCondition]})-
    PriceN(merged)/PriceN(bonesQuery))>1e-12)throw new Exception("Structured Bones drops extra offer predicate");
  var commonKey=RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,0,"fixture",world,10).Lanes.First(l=>l.Kind==RelicSequenceKind.Common).Entries[0].RelicKey;
  var dashboardQuery=variantQuery with {OpeningRoute=new(BaseGameModelKeys.Relics.NeowsBones),
   RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.FirstN,5,new([commonKey],[],[]))],
   StandardMaps=[new(1,StandardMapMetric.ReachableMaxElite,StandardMapComparison.AtLeast,1,true)]};
  var dashboardCompiled=SearchCompiler.Compile(dashboardQuery,mixtureContext);
  var dashboardJoint=JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(dashboardCompiled));
  var dashboard=RolltheSpire2.Presentation.Ui1.SearchProbabilityPresentationBuilder.Build(dashboardCompiled,dashboardJoint);
  if(dashboard.Rows.Where(r=>r.Kind is RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.Neow or RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.Relic).Any(r=>r.Probability is null))
   throw new Exception("Variant mixture loses known dashboard factors");
  if(dashboard.Rows.Single(r=>r.Kind==RolltheSpire2.Presentation.Ui1.SearchProbabilityRowKind.WorldEvent).Probability is not {} dashboardWorld || Math.Abs(dashboardWorld-.5)>1e-12)
   throw new Exception("W dashboard must retain its own Variant prior when other families are present");
  var dashboardRequest=RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(dashboardQuery with {VariantBossBranches=[]},context),new("000000000000",100,1,1)).Plan!;
  var dashboardPlan=FamilyExecutionCoordinator.Plan(dashboardRequest,true);
  var dashboardCoverage=FamilyExecutionCoordinator.CreateRegisteredFamilies(dashboardRequest,true)
   .SelectMany(f=>f.Coverage).ToHashSet(StringComparer.Ordinal);
  if(dashboardPlan.SelectionPolicyId!=FamilyPlanner.SelectionPolicyId||
   dashboardPlan.RankingAuthority!=FamilyPlannerRankingAuthority.GreedyUnitEliminationCost||
   !dashboardCoverage.Contains("M.StandardMap")||
   !dashboardPlan.Stages.SelectMany(stage=>stage.Coverage).ToHashSet(StringComparer.Ordinal).SetEquals(dashboardCoverage))
   throw new Exception("Ordinary map planning must use greedy ordering without dropping registered filtering obligations");
  Console.WriteLine("OPENING_JOINT_AND_MIXTURE_ATTRIBUTION_PASS;MAP_GREEDY_COVERAGE_PRESERVED");
  var eventInput=SearchSelectivityInput.From(Request(empty));
  var fakeTargets=Beta111EventResultCatalog.FakeMerchantRelics.Take(2).ToArray();
  var fakeJoint=EventResultProbabilityEstimator.EstimateBlock(eventInput,fakeTargets.Select(k=>new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,k)).ToArray(),out _);
  if(Math.Abs(fakeJoint!.Probability!.Value-6d/9*5d/8)>1e-12)throw new Exception("Same fake offer joint");
  var probabilityGrab=new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]);
  var probabilityDive=new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics[0]);
  var duplicate=EventResultProbabilityEstimator.EstimateBlock(eventInput,[probabilityGrab,probabilityGrab,probabilityDive],out _);
  if(Math.Abs(duplicate!.Probability!.Value-.1)>1e-12)throw new Exception("Duplicate Grab pays shared draw twice");
  Console.WriteLine("ANCIENT_ALL_PROBABILITY_PASS shuffle+separate-pool exhaustive;SeaGlass refinement;query+stage closed");
  var cases=new List<(string Name,string Family,NeowSearchFilter Filter)>();
  void E(string n,params EventResultSearchCondition[] p)=>cases.Add((n,"E",empty with {EventResultConditions=p}));
  foreach(var (kind,keys) in new[]{(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards),
    (EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics),
    (EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics),
    (EventResultConditionKind.ColorfulPhilosophersOfferedColor,Beta111EventResultCatalog.ColorfulCharacterOrder)})
   foreach(var key in keys)E(kind+"_"+key.Entry,new EventResultSearchCondition(kind,key));
  E("EZero",new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,new("CARD","NEVER")));
  E("TrashJoint",new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]),new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics[0]));
  E("TrashContradiction",new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]),new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics[4]));
  E("FakeAll",new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0]),new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[1]));
  E("MixedE",new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]),new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0]));
  var colors=Beta111EventResultCatalog.ColorfulCharacterOrder.Where(c=>c!=authority.Character.CharacterKey).ToArray();
  for(int mask=1;mask<16;mask++)
  {
   var targets=colors.Where((c,i)=>(mask&(1<<i))!=0).Select(c=>new EventResultSearchCondition(EventResultConditionKind.ColorfulPhilosophersOfferedColor,c)).ToArray();
   E("ColorMask"+mask,targets);
   E("ColorFake"+mask,targets.Concat(new[]{new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0])}).ToArray());
  }
  void A(string n,params AncientSearchBranchCondition[] p)=>cases.Add((n,"A",empty with {AncientBranchConditions=p}));
  foreach(var c in g.AncientEventContexts.Where(c=>c.Act>=2)){
   var options=c.Catalog!.Pools.SelectMany(p=>p.OrderedOptions).Distinct().ToArray();
   foreach(var option in options)A(c.AncientKey.Entry+"_"+option.Entry,new AncientSearchBranchCondition(c.Act,c.AncientKey,[option],[]));
   A(c.AncientKey.Entry+"Union",new AncientSearchBranchCondition(c.Act,c.AncientKey,options,[]));
  }
  foreach(var source in g.AncientEventContexts.Where(c=>c.Act>=2)) {
   ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(0));
   var snapshot=g with {RunSeedRoot=root,AncientEventContexts=[source with {EventRngRoot=Beta109WorldRng.DeriveEventLocalSeed(root,source.PlayerSlot,source.IsShared,source.EventIdEntry)}]};
   var offers=Beta109AncientOptionProvider.PredictEventLocal(snapshot,source.Act,source.AncientKey,0,authority.Character.CharacterKey,empty.AncientOptionConditions)
    .Options.Where(o=>o.IsVisible && o.OptionKey.IsValid).Select(o=>o.OptionKey).Distinct().ToArray();
   for(int take=1;take<=offers.Length;take++) cases.Add((source.AncientKey.Entry+"All"+take,"A",empty with {
    AncientBranchConditions=[new(source.Act,source.AncientKey,[],[])],AncientOptionFilters=[new(source.Act,new([],offers.Take(take).ToArray(),[]))]}));
   cases.Add((source.AncientKey.Entry+"AllImpossible","A",empty with {
    AncientBranchConditions=[new(source.Act,source.AncientKey,[],[])],AncientOptionFilters=[new(source.Act,new([],[new("RELIC","NEVER")],[]))]}));
  }
  var a2=g.AncientEventContexts.First(c=>c.Act==2);var a3=g.AncientEventContexts.First(c=>c.Act==3);
  var o2=a2.Catalog!.Pools.SelectMany(p=>p.OrderedOptions).First();var o3=a3.Catalog!.Pools.SelectMany(p=>p.OrderedOptions).First();
  A("AZero",new AncientSearchBranchCondition(2,a2.AncientKey,[new("RELIC","NEVER")],[]));
  A("TwoActs",new AncientSearchBranchCondition(2,a2.AncientKey,[o2],[]),new AncientSearchBranchCondition(3,a3.AncientKey,[o3],[]));
  A("AlternativeRows",new AncientSearchBranchCondition(2,a2.AncientKey,[o2],[]),new AncientSearchBranchCondition(2,new("EVENT","OROBAS"),[new("RELIC","SEA_GLASS")],[]));
  A("NeutralAlternative",new AncientSearchBranchCondition(2,a2.AncientKey,[new("RELIC","NEVER")],[]),new AncientSearchBranchCondition(2,new("EVENT","OROBAS"),[],[]));
  cases.Add(("MultiParentAll","A",empty with {AncientBranchConditions=[new(2,a2.AncientKey,[],[]),new(2,new("EVENT","OROBAS"),[],[])],
   AncientOptionFilters=[new(2,new([],[o2],[]))]}));
  A("SeaGlass",new AncientSearchBranchCondition(2,new("EVENT","OROBAS"),[],[BaseGameModelKeys.Characters.Silent]));
  cases.Add(("LegacyAnyAllBan","A",empty with {AncientOptionFilters=[new(2,new([o2],[],[new("RELIC","DUSTY_TOME")]))]}));
  cases.Add(("LegacySea","A",empty with {AncientSeaGlassTargetFilters=[new(2,new([BaseGameModelKeys.Characters.Silent],[],[]))]}));
  int checks=CheckAuthoredShape(context)+CheckDonorAvailability(context)+CheckOrdinaryCompilation(context)+CheckEventGpuGeometry();var fixtures=new List<object>();
  checks+=CheckAncientReferenceBindings(context);
  checks+=CheckAncientLegacyNumerical(context);
  // A contradictory Variant alternative must not invalidate its sibling OR row.
  var badBoss=baseAct.Bosses[0];
  var contradictoryBosses=new ModelKeySetFilter[] {
   new([badBoss],[],[badBoss]), new([],[badBoss],[badBoss]),
   new([],[badBoss,new("MONSTER","OTHER_BOSS")],[]), new([new("MONSTER","OTHER_BOSS")],[badBoss],[])
  };
  foreach(var contradiction in contradictoryBosses) {
   var bad=new VariantScopedBossBranch(2,baseAct.ActKey,contradiction,ModelKeySetFilter.Empty,false);
   var good=new VariantScopedBossBranch(2,variant.ActKey,ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false);
   var orQuery=SearchCompiler.Compile(SearchQuery.Empty with {VariantBossBranches=[bad,good]},mixtureContext);
   if(orQuery.Normalization.Status!=QueryNormalizationStatus.Legal ||
      orQuery.NormalizedQuery.VariantBossBranches.Count!=1 || orQuery.NormalizedQuery.VariantBossBranches[0]!=good ||
      SearchFeasibilityAnalyzer.Analyze(orQuery).IsImpossible)
    throw new Exception("One contradictory Variant OR row rejected its legal sibling");
   if(SearchCompiler.Compile(SearchQuery.Empty with {VariantBossBranches=[bad,bad with {VariantKey=variant.ActKey}]},mixtureContext)
      .Normalization.Status!=QueryNormalizationStatus.Impossible)
    throw new Exception("All contradictory Variant alternatives were accepted");
   if(SearchCompiler.Compile(SearchQuery.Empty with {VariantBossBranches=[good,bad with {Act=3}]},mixtureContext)
      .Normalization.Status!=QueryNormalizationStatus.Impossible)
    throw new Exception("A different Act contradiction was erased by a legal Act");
   if(SearchCompiler.Compile(SearchQuery.Empty with {VariantBossBranches=[good with {SecondBoss=contradiction}]},mixtureContext)
      .Normalization.Status!=QueryNormalizationStatus.Legal)
    throw new Exception("Inactive Boss2 restriction rejected a valid Variant row");
   checks+=4;
  }
  // Sea Glass uses the captured other-character population, including mod identities.
  var seaContext=context;
  double Sea(params ModelKey[] targets) => AncientOptionPhysicalPricing.ResolveSurvival(Request(empty with {
   AncientBranchConditions=[new(2,new("EVENT","OROBAS"),[new("RELIC","SEA_GLASS")],targets)]
  })).SurvivalProbability ?? throw new InvalidOperationException("SeaGlass survival unavailable");
  double seaOne=Sea(BaseGameModelKeys.Characters.Silent);
  double seaTwo=Sea(BaseGameModelKeys.Characters.Silent,BaseGameModelKeys.Characters.Ironclad);
  if(seaOne<=0 || Math.Abs(seaTwo-2*seaOne)>1e-12) throw new InvalidOperationException("SeaGlass target union probability");
  var modWorld=world with {Beta109Generation=g with {NoUnknownHooksOrModifiers=false}};
  var modAuthority=authority.WithWorldAuthority(modWorld).WithEffectAuthority(authority.EffectAuthority! with {
   SourceAuthority=SourceAuthority.ModdedRuntimeBestEffort});
  context=context with {Authority=modAuthority};
  if(Math.Abs(Sea(BaseGameModelKeys.Characters.Silent)-seaOne)>1e-12)
   throw new InvalidOperationException("SeaGlass provenance changed numerical probability");
  context=seaContext;
  checks+=3;
  // Model policy: normalize known shared facts, then multiply distinct groups.
  var grab = new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0]);
  var dive = new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic, Beta111EventResultCatalog.TrashHeapDiveRelics[0]);
  var fake = new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic, Beta111EventResultCatalog.FakeMerchantRelics[0]);
  void Probability(double expected, params EventResultSearchCondition[] conditions)
  {
   var probability = new EventResultFamilyPlan(Request(empty with {EventResultConditions=conditions})).Survival.SurvivalProbability;
   if (!probability.HasValue || Math.Abs(probability.Value-expected)>1e-12) throw new Exception("E modeled selectivity");
   checks++;
  }
  Probability(.1, grab);
  Probability(.1, grab, dive);
  Probability(.1 * 6d/9d, grab, fake);
  Probability(.1 * 6d/9d, grab, dive, fake);
  Probability(.1 * 6d/9d, grab, grab, fake); // Duplicate conditions do not introduce a factor.
  Probability(0, grab, fake, new(EventResultConditionKind.TrashHeapDiveRelic, Beta111EventResultCatalog.TrashHeapDiveRelics[4]));
  var inventoryJoint=new EventResultFamilyPlan(Request(empty with {EventResultConditions=[fake,
      new(EventResultConditionKind.FakeMerchantOfferedFakeRelic, Beta111EventResultCatalog.FakeMerchantRelics[1])]})).Survival.SurvivalProbability;
  if (inventoryJoint is not {} jointMass || Math.Abs(jointMass-6d/9*5d/8)>1e-12)
   throw new Exception("Shared inventory joint must use without-replacement intersection");
  checks++;
  string dir=Path.Combine(Environment.CurrentDirectory,".build-evidence","ae-gpu");Directory.CreateDirectory(dir);
  foreach(var (name,family,filter) in cases){
   var request=Request(filter);
   var a=family=="A"?new AncientOptionFamilyPlan(request):null;
   var e=family=="E"?new EventResultFamilyPlan(request):null;
   var cpuOnly=e is null?null:new EventResultFamilyPlan(request,prepareGpu:false);
   var cpuOnlyColor=cpuOnly is null?null:EventResultCpuPlan.TryCompile(request,cpuOnly);
   if(cpuOnly is not null && (cpuOnly.GpuSupported || cpuOnly.Buffers.Length!=0 || cpuOnly.NumericalSupported!=e!.NumericalSupported))
    throw new Exception("E CPU numerical admission must not prepare GPU: "+name);
   Func<ulong,bool> match=a is not null?a.Matches:e!.Matches;
   Func<ulong,bool> reference=a is not null?a.Reference:e!.Reference;
   var compiledColor=e is null?null:EventResultCpuPlan.TryCompile(request,e);
   if(compiledColor is not null)checks+=RelicCpuChecks.CheckEnvelope(request,compiledColor.Matches,e!.Reference);
   bool gpu=a?.GpuSupported??e!.GpuSupported;
   if(family=="A" && name.Contains("All") && !gpu)throw new Exception("A All GPU not admitted: "+name);
   Console.WriteLine($"AE_CASE name={name};gpu={gpu}");
   var registration=FamilyExecutionCoordinator.Plan(request);
   // Private transport can expose only its first body's FamilyId while retaining
   // every constituent obligation in Coverage.
   if(!registration.Stages.SelectMany(stage=>stage.Coverage).Contains(family=="A"?"A.AncientOption":"E.EventResult"))
    throw new Exception("Registration coverage "+name);
   string shader=name+".glsl";if(gpu)File.WriteAllText(Path.Combine(dir,shader),a?.ShaderSource()??e!.ShaderSource());
   foreach(ulong start in new[]{0UL,1564445371644820290UL}){
    int size=Math.Clamp(count,67,513);var expected=new List<uint>();
    for(uint i=0;i<size;i++){
     ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start+i));
     bool truth=reference(root),actual=match(root);
     if(cpuOnly is not null && cpuOnly.Matches(root)!=truth)throw new Exception("CPU-only E Reference parity "+name);
     if(cpuOnlyColor is not null && cpuOnlyColor.Matches(root)!=truth)throw new Exception("CPU-only compiled E Reference parity "+name);
     if(compiledColor is not null && compiledColor.Matches(root)!=truth)throw new Exception("Compiled color parity "+name);
     if(truth!=actual)throw new Exception($"Canonical parity {name} {start+i} truth={truth} actual={actual}");
     if(e is not null && (Beta111CanonicalEventShopSearchEvaluator.Evaluate(request,root,authority).Disposition==SearchDisposition.Match)!=actual)throw new Exception("E Exact parity "+name);
     if(actual)expected.Add(i);checks++;
    }
    if(name=="NeutralAlternative"&&expected.Count!=size)throw new Exception("A implicit actual-identity tightening");
    if(gpu)foreach(bool compact in new[]{false,true}){
     var ordinals=Enumerable.Range(0,size).Where(i=>!compact||i%3==0).Select(i=>(uint)i).ToArray();
     fixtures.Add(new{name,family,shader,buffers=a?.Buffers??e!.Buffers,scratchWords=a?.ScratchWords??0,
       batchMeta=new[]{(uint)start,(uint)(start>>32),(uint)size,(uint)ordinals.Length,compact?1u:0u},ordinals,expected=expected.Where(i=>!compact||i%3==0).ToArray()});
    }
   }
  }
  // Reuse the same family plans in either order; no upstream replay object is passed.
  var af=empty with {AncientBranchConditions=[new(2,a2.AncientKey,[o2],[])]};
  var ef=empty with {EventResultConditions=[new(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0])]};
  var n=empty with {NeowRoute=new(BaseGameModelKeys.Relics.ArcaneScroll)};
  var rTarget=RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,0,"fixture",world,10).Lanes.First(l=>l.Kind==RelicSequenceKind.Common).Entries[0].RelicKey;
  var r=empty with {RelicSequenceConditions=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.FirstN,2,new([rTarget],[],[]))]};
  var w=empty with {EventSequenceConditions=[new(1,null,SearchSequenceRangeMode.FirstN,2,new([g.OrderedActCatalog[0].OrderedRawEvents[0]],[],[]))]};
  foreach(var (left,right,label) in new[]{(w,ef,"W+E"),(w,af,"W+A"),(af,ef,"A+E"),(n,af,"N+A"),(n,ef,"N+E"),(r,af,"R+A"),(r,ef,"R+E")}){
   var mixed=left with {AncientBranchConditions=right.AncientBranchConditions.Count>0?right.AncientBranchConditions:left.AncientBranchConditions,EventResultConditions=right.EventResultConditions};
   var request=Request(mixed);var registeredCoverage=FamilyExecutionCoordinator.Plan(request).Stages
    .SelectMany(stage=>stage.Coverage).ToHashSet(StringComparer.Ordinal);
   string[] ids=label.Split('+').Select(c=>c switch{"A"=>"A.AncientOption","E"=>"E.EventResult","N"=>"N.Neow","R"=>"R.Relic",_=>"W.World"}).ToArray();
   if(ids.Any(id=>!registeredCoverage.Contains(id)))throw new Exception("Mixed registration coverage "+label);
   var a=request.Evaluation.AncientBranchConditions.Count>0?new AncientOptionFamilyPlan(request):null;
   var e=request.Evaluation.EventResultConditions.Count>0?new EventResultFamilyPlan(request):null;
   var np=label.StartsWith("N")?NeowReplayPlan.Compile(request,false):null;
   var wp=label.StartsWith("W")?new WorldFamilyReplay(request):null;
   bool L(ulong root)=>label.StartsWith("N")?NeowFamilyReplay.Matches(root,np!):label.StartsWith("W")?wp!.Matches(root):label.StartsWith("R")?
    RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,root,"fixture",world,10).Lanes.First(l=>l.Kind==RelicSequenceKind.Common).Entries.Take(2).Any(x=>x.RelicKey==rTarget):a!.Matches(root);
   bool R(ulong root)=>label.EndsWith("A")?a!.Matches(root):e!.Matches(root);
   var batch=new SearchBatch(1564445371644820290UL,257);var source=FamilyCandidateSet.FromAbi1(batch,[0,3,9,12,49,103,201,256]);
   ulong Root(ulong i)=>Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(batch.GlobalCandidate(i)));
   FamilyCandidateSet Filter(FamilyCandidateSet input,Func<ulong,bool> f)=>FamilyCandidateSet.FromAbi1(batch,input.EnumerateLogicalOrdinals().Where(i=>f(Root(i))).ToArray());
   if(!Filter(Filter(source,L),R).ExportAbi1().Span.SequenceEqual(Filter(Filter(source,R),L).ExportAbi1().Span))throw new Exception("Order dependence "+label);
   Console.WriteLine("AE_INDEPENDENCE_PASS "+label);checks++;
  }
  // Shared assignment uses the same event-local donor, with a nonzero context slot
  // to catch accidental local seed derivation. Restore the live fixture afterwards.
  var savedContext=context;
  var shared=g with {AncientEventContexts=g.AncientEventContexts.Select(c=>c.AncientKey==a2.AncientKey?c with {IsShared=true,PlayerSlot=2}:c).ToArray()};
  context=context with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=shared})};
  var sr=Request(af);var sp=new AncientOptionFamilyPlan(sr);
  var se=new List<uint>();for(uint i=0;i<257;i++){ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));if(sp.Matches(root)!=sp.Reference(root))throw new Exception("Shared A parity");if(sp.Reference(root))se.Add(i);checks++;}
  if(!sp.GpuSupported)throw new Exception("Shared A GPU applicability");
  File.WriteAllText(Path.Combine(dir,"SharedA.glsl"),sp.ShaderSource());
  fixtures.Add(new{name="SharedA",family="A",shader="SharedA.glsl",buffers=sp.Buffers,scratchWords=sp.ScratchWords,batchMeta=new uint[]{0,0,257,257,0},ordinals=Enumerable.Range(0,257).ToArray(),expected=se});
  context=savedContext;
  // Reproduce the live capture shape: no pre-materialized World SelectedActs.
  // Compare the same canonical numerical core against a fully projected snapshot.
  var raw=g with {SelectedActs=[],SelectedActsExact=false,SelectedActProvenance=SelectedActProvenance.Missing};
  foreach(var ctx in g.AncientEventContexts.Where(c=>c.Act>=2))for(ulong i=0;i<67;i++){
   ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
   var local=ctx with {EventRngRoot=Beta109WorldRng.DeriveEventLocalSeed(root,ctx.PlayerSlot,ctx.IsShared,ctx.EventIdEntry)};
   var full=g with {RunSeedRoot=root,AncientEventContexts=[local]};
   var captured=raw with {RunSeedRoot=root,AncientEventContexts=[local]};
   var truth=Beta109AncientOptionProvider.Predict(full,ctx.Act,ctx.AncientKey,0,authority.Character.CharacterKey,true);
   var actual=Beta109AncientOptionProvider.PredictEventLocal(captured,ctx.Act,ctx.AncientKey,0,authority.Character.CharacterKey,AncientOptionConditionProfile.BroadDefault);
   if(actual.Options.Any(o=>o.OptionPrecision!=PredictionPrecision.Exact)||!truth.Options.Select(o=>(o.OptionKey,o.CharacterTarget?.CharacterKey)).SequenceEqual(actual.Options.Select(o=>(o.OptionKey,o.CharacterTarget?.CharacterKey)))||truth.RngCallCount!=actual.RngCallCount)throw new Exception("Raw capture canonical parity "+ctx.AncientKey);
   if(Beta109AncientOptionProvider.Predict(captured,ctx.Act,ctx.AncientKey,0,authority.Character.CharacterKey,true).Precision==PredictionPrecision.Exact)throw new Exception("Public World precision changed");
   checks++;
  }
  context=savedContext with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=raw with {
   AncientEventContexts=raw.AncientEventContexts.Select(c=>c with {Catalog=c.Catalog! with {MutableInstanceProjectionExact=false}}).ToArray()}})};
  var rawFilter=cases.Single(c=>c.Name=="PAELUnion").Filter;
  var rawPlan=new AncientOptionFamilyPlan(Request(rawFilter));
  if(!rawPlan.GpuSupported)throw new Exception("Raw capture GPU admission");
  var rawExpected=new List<uint>();for(uint i=0;i<257;i++){ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));if(rawPlan.Matches(root)!=rawPlan.Reference(root))throw new Exception("Live raw reference parity");if(rawPlan.Reference(root))rawExpected.Add(i);checks++;}
  File.WriteAllText(Path.Combine(dir,"RawCaptureA.glsl"),rawPlan.ShaderSource());
  fixtures.Add(new{name="RawCaptureA",family="A",shader="RawCaptureA.glsl",buffers=rawPlan.Buffers,scratchWords=rawPlan.ScratchWords,batchMeta=new uint[]{0,0,257,257,0},ordinals=Enumerable.Range(0,257).ToArray(),expected=rawExpected});
  // Missing event authority is an unresolved observation, not a thrown failure.
  // The optional sieve retains it for Exact without claiming a model Match.
  var broken=raw with {AncientEventContexts=raw.AncientEventContexts.Select(c=>c with {EventContextExact=false}).ToArray()};
  context=savedContext with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=broken})};
  var brokenPlan=new AncientOptionFamilyPlan(Request(rawFilter));
  var beforeProbe=RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Revision;
  if(brokenPlan.GpuSupported || brokenPlan.ReferenceDisposition(1)!=SearchDisposition.Unknown || !brokenPlan.Reference(1))
   throw new Exception("Missing event authority was rejected or claimed Match");
  if(RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Revision!=beforeProbe)
   throw new Exception("A reference probe created an execution notice");
  checks+=2;
  var missingActContexts=raw with {AncientEventContexts=raw.AncientEventContexts.Where(c=>c.Act!=2).ToArray()};
  context=savedContext with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=missingActContexts})};
  var missingActPlan=new AncientOptionFamilyPlan(Request(empty with {AncientOptionFilters=[new(2,new([o2],[],[]))]}));
  if(missingActPlan.GpuSupported || missingActPlan.ReferenceDisposition(1)!=SearchDisposition.Unknown || !missingActPlan.Matches(1))
   throw new Exception("Missing unscoped A contexts falsely proved an empty offer set");
  checks++;
  // An unresolved first Ancient does not block a later independently replayed row.
  var first=raw.AncientEventContexts.First(c=>c.Act==2);
  var next=raw.AncientEventContexts.First(c=>c.Act==2 && c.AncientKey!=first.AncientKey && c.Catalog!.Pools.Count>0);
  var alternativeGeneration=raw with {AncientEventContexts=raw.AncientEventContexts.Select(c=>
   c.Act==first.Act && c.AncientKey==first.AncientKey?c with {EventContextExact=false}:c).ToArray()};
  context=savedContext with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=alternativeGeneration})};
  var alternativesPlan=new AncientOptionFamilyPlan(Request(empty with {AncientBranchConditions=[
   new(first.Act,first.AncientKey,[new("RELIC","FIRST_UNRESOLVED")],[]),
   new(next.Act,next.AncientKey,next.Catalog!.Pools.SelectMany(p=>p.OrderedOptions).Distinct().ToArray(),[])]}));
  if(alternativesPlan.GpuSupported || alternativesPlan.ReferenceDisposition(1)!=SearchDisposition.Match || !alternativesPlan.Matches(1))
   throw new Exception("Unresolved A alternative blocked an independently modeled survivor");
  checks++;
  // Sea Glass option identity can be exact while its character target is Unknown.
  var unknownSeaGeneration=raw with {AncientEventContexts=raw.AncientEventContexts.Select(c=>
   c.AncientKey.Entry=="OROBAS"?c with {UnlockedCharacterSourceOrderExact=false}:c).ToArray()};
  context=savedContext with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=unknownSeaGeneration})};
  var seaFilter=empty with {AncientBranchConditions=[new(2,new("EVENT","OROBAS"),
   [new("RELIC","SEA_GLASS")],[BaseGameModelKeys.Characters.Silent])]};
  var unknownSeaPlan=new AncientOptionFamilyPlan(Request(seaFilter));
  var rejectSeaPlan=new AncientOptionFamilyPlan(Request(seaFilter with {
   AncientOptionFilters=[new(2,new([new("RELIC","NEVER")],[],[]))]}));
  var sameAncientOr=new AncientOptionFamilyPlan(Request(seaFilter with {AncientBranchConditions=[
   seaFilter.AncientBranchConditions[0],new(2,new("EVENT","OROBAS"),[new("RELIC","SEA_GLASS")],[])]}));
  int unresolvedSea=0,rejectedSea=0;
  long noticeRevision=-1;
  for(ulong i=0;i<128;i++) {
   ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
   var disposition=unknownSeaPlan.ReferenceDisposition(root);
   if(disposition==SearchDisposition.Unknown) {
    unresolvedSea++;
    if(!unknownSeaPlan.Matches(root) || sameAncientOr.ReferenceDisposition(root)!=SearchDisposition.Match ||
       rejectSeaPlan.ReferenceDisposition(root)!=SearchDisposition.NoMatch || rejectSeaPlan.Matches(root))
     throw new Exception("A Sea Glass Unknown was rejected, claimed Match, or hid a known contradiction");
    var notice=RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Snapshot()
     .First(x=>x.Code=="AncientOptionFilterIncomplete" && x.Stage.Contains("OROBAS"));
    if(!notice.ExceptionDetail.Contains("MissingUnlockedCharacterSourceOrderAuthority",StringComparison.Ordinal))
     throw new Exception("A unresolved notice lost the original target cause");
    long revision=RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Revision;
    if(noticeRevision!=-1 && noticeRevision!=revision)throw new Exception("A unresolved row flooded the issue ledger");
    noticeRevision=revision;
   } else {
    rejectedSea++;
    if(disposition!=SearchDisposition.NoMatch || unknownSeaPlan.Matches(root))throw new Exception("Absent Sea Glass retained without cause");
   }
   checks++;
  }
  if(unknownSeaPlan.GpuSupported || unresolvedSea==0 || rejectedSea==0)throw new Exception("Unknown Sea Glass GPU admission or outcome coverage");
  checks++;
  // A genuine exception from the event-local numerical provider stays a failure.
  var faultGeneration=raw with {AncientEventContexts=raw.AncientEventContexts.Select(c=>c.AncientKey.Entry=="OROBAS"?
   c with {Catalog=c.Catalog! with {Pools=c.Catalog!.Pools.Select(p=>p.PoolId=="orobas.pool2"?
    p with {OrderedOptions=[]}:p).ToArray()}}:c).ToArray()};
  context=savedContext with {Authority=authority.WithWorldAuthority(world with {Beta109Generation=faultGeneration})};
  var faultPlan=new AncientOptionFamilyPlan(Request(seaFilter));
  try { faultPlan.Matches(1);throw new Exception("A numerical provider fault became survivor retention"); }
  catch(InvalidOperationException ex) when(ex.Message.StartsWith("Beta109WorldCandidatePoolEmpty:",StringComparison.Ordinal)) {checks++;}
  Console.WriteLine($"A_UNRESOLVED_SIEVE_BOUNDARY_PASS seaUnknown={unresolvedSea};seaRejected={rejectedSea};noUnknownMatch=true;faultPreserved=true");
  context=savedContext;
  var mixedWE=Request(w with {EventResultConditions=ef.EventResultConditions});
  var weRegistered=FamilyExecutionCoordinator.CreateRegisteredFamilies(mixedWE);
  var unknownFamilies=weRegistered.Select(f=>f.FamilyId=="W.World"?(IFamilyInvocation)new UnknownSurvivalProbe(f):f).ToArray();
  var greedy=FamilyPlanner.Plan(unknownFamilies);
  if(!greedy.OrderedFamilies.Select(f=>f.FamilyId).Order().SequenceEqual(unknownFamilies.Select(f=>f.FamilyId).Order()) ||
     greedy.Stages.Single(stage=>stage.Family.FamilyId=="W.World").Survival.HasValue)
   throw new Exception("Unknown terminal W model removed a filtering obligation or invented survival");
  checks++;
  Console.WriteLine("AE_RAW_CAPTURE_AND_GREEDY_WORLD_EVENT_PASS");
  // Union fixtures request every possible option and therefore cannot reject.
  // Vakuu draws one of three identities from pool 1; a single target exercises
  // both CPU predicate outcomes under the same optional-packing failure.
  checks+=CheckGpuPreparation(Request(cases.Single(c=>c.Name=="VAKUU_VAKUU_1A").Filter));
   int exactMatches=0,exactRejects=0;
  foreach(var c in cases.Where(c=>c.Name is "PAELUnion" or "VAKUUUnion" or "AZero" or "TrashJoint" or "MixedE" or "MultiParentAll")){
    var request=Request(c.Filter);var ap=c.Family=="A"?new AncientOptionFamilyPlan(request):null;var ep=c.Family=="E"?new EventResultFamilyPlan(request):null;
   for(ulong i=0;i<67;i++){
    string seed=Beta110SeedCodec.FormatOrdinal(i);ulong root=Beta111Profile.Instance.ComputeRootSeed(seed);
    if(!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance,root,seed,out var bound,out _))throw new Exception("Exact seed binding");
    var exact=ProductionExactSearchEvaluator.Evaluate(request,bound);
    bool pass=ap?.Matches(root)??ep!.Matches(root);
    if(exact.IsMatch){exactMatches++;if(!pass)throw new Exception("Exact false negative "+c.Name);}
    else if(exact.Evaluation.Disposition==SearchDisposition.NoMatch)exactRejects++;
    else throw new Exception("Fixture Exact authority "+c.Name+":"+exact.FailureCode);
    checks++;
   }
  }
   if(exactMatches==0||exactRejects==0)throw new Exception("Exact witness coverage");
  Console.WriteLine($"AE_EXACT_PASS accepted={exactMatches};rejected={exactRejects};falseNegative=0");
  // Exercise the explicitly selected CPU realization / ABI1 / cancellation.
  var cpuRequest=Request(cases.Single(c=>c.Name=="LegacyAnyAllBan").Filter);
  AncientOptionFamily.TryCreate(cpuRequest,out var ownerA);
  var cpu=ownerA!.CpuRealizations.Single();
  if(FamilyExecutionCoordinator.ResolveExecutionWindowSize(cpuRequest,[cpu!])!=65536)throw new Exception("A CPU-only batch geometry");
  EventResultFamily.TryCreate(Request(ef),out var gpuE);
  if(FamilyExecutionCoordinator.ResolveExecutionWindowSize(cpuRequest,[cpu!,gpuE!])!=(1<<24))throw new Exception("A CPU must not underfill E GPU");
  var execution=new FamilyExecutionContext(false);var cb=new SearchBatch(0,67);var ci=FamilyCandidateSet.FromAbi1(cb,[0,3,15,44,66]);
  var cp=new AncientOptionFamilyPlan(cpuRequest);
  var co=cpu!.InvokeAsync(execution,new(cb,cpuRequest),ci,default).GetAwaiter().GetResult();
  if(!co.EnumerateLogicalOrdinals().SequenceEqual(ci.EnumerateLogicalOrdinals().Where(i=>cp.Reference(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i))))))throw new Exception("A CPU ABI1");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{cpu.InvokeAsync(execution,new(cb,cpuRequest),ci,cancel.Token).GetAwaiter().GetResult();throw new Exception("CPU cancellation");}catch(OperationCanceledException){checks++;}}
  cpu.DisposeAsync(execution).GetAwaiter().GetResult();execution.DisposeAsync().GetAwaiter().GetResult();
  File.WriteAllText(Path.Combine(dir,"fixtures.json"),System.Text.Json.JsonSerializer.Serialize(fixtures));
  Console.WriteLine($"AE_CPU_SUMMARY cases={cases.Count};checks={checks};gpuFixtures={fixtures.Count};failures=0");
  return 0;
 }
 private static int CheckEventGpuGeometry()
 {
  var trash=new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]);
  var fake=new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0]);
  var color=new EventResultSearchCondition(EventResultConditionKind.ColorfulPhilosophersOfferedColor,Beta111EventResultCatalog.ColorfulCharacterOrder[0]);
  EventResultGpuPacking.ValidateFixedPoolGeometry([trash,fake,color],10,5,9,5);
  foreach(var (condition,grab,dive,merchant,colors,issue) in new[]{
   (trash,12,6,9,5,"TrashHeap"),(trash,10,6,9,5,"TrashHeap"),
   (fake,10,5,10,5,"FakeMerchant"),(color,10,5,9,6,"ColorfulPhilosophers")})
  {
   try { EventResultGpuPacking.ValidateFixedPoolGeometry([condition],grab,dive,merchant,colors);throw new Exception("E changed shader geometry admitted"); }
   catch(NotSupportedException ex) when(ex.Message.StartsWith("E.GpuFixedPoolGeometryUnsupported:"+issue,StringComparison.Ordinal)) { }
  }
  EventResultGpuPacking.ValidateFixedPoolGeometry([fake],12,6,9,6);
  EventResultGpuPacking.ValidateFixedPoolGeometry([trash],10,5,10,6);
  Console.WriteLine("E_GPU_FIXED_POOL_GEOMETRY_PASS;current=true;changedGeometryRejected=true;unrelatedObservablesUnaffected=true");
  return 7;
 }
 private static int CheckOrdinaryCompilation(SearchContext basis)
 {
  int checks=0;
  void Check(bool ok,string message){checks++;if(!ok)throw new InvalidDataException("E ordinary compilation: "+message);}
  var grab=new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard,Beta111EventResultCatalog.TrashHeapGrabCards[0]);
  var dive=new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic,Beta111EventResultCatalog.TrashHeapDiveRelics[0]);
  var fake=new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic,Beta111EventResultCatalog.FakeMerchantRelics[0]);
  var color=new EventResultSearchCondition(EventResultConditionKind.ColorfulPhilosophersOfferedColor,
   Beta111EventResultCatalog.ColorfulCharacterOrder.First(k=>k!=basis.Authority.Character.CharacterKey));
  var trial=new EventResultSearchCondition(EventResultConditionKind.TrialCase,new("EVENT","TRIAL")){TrialCase=TrialCaseTarget.Nondescript};
  var tinker=new EventResultSearchCondition(EventResultConditionKind.TinkerTimeTypeAndRider,new("CARD","MAD_SCIENCE"))
   {TinkerCardType=TinkerCardTypeTarget.Attack,TinkerRider=TinkerRiderTarget.Sapping};
  var cases=new List<EventResultSearchCondition[]>();
  foreach(var (kind,keys) in new[]{(grab.Kind,Beta111EventResultCatalog.TrashHeapGrabCards),
   (dive.Kind,Beta111EventResultCatalog.TrashHeapDiveRelics),(fake.Kind,Beta111EventResultCatalog.FakeMerchantRelics),
   (color.Kind,Beta111EventResultCatalog.ColorfulCharacterOrder)})
   foreach(var key in keys)cases.Add([new(kind,key)]);
  foreach(var condition in new[]{grab,dive,fake,color})
  {
   cases.Add([condition with {TargetKey=new(condition.TargetKey.Category,"NEVER_IN_EVENT_CATALOG")}]);
   cases.Add([condition,condition]);
  }
  cases.Add([grab,dive]);cases.Add([grab,dive with {TargetKey=Beta111EventResultCatalog.TrashHeapDiveRelics[4]}]);
  cases.Add(Beta111EventResultCatalog.FakeMerchantRelics.Take(6).Select(k=>new EventResultSearchCondition(fake.Kind,k)).ToArray());
  cases.Add([color,fake]);cases.Add([color,fake,fake]);cases.Add([trial,tinker,grab,fake,color]);
  cases.Add([color,fake,grab,tinker,trial]);cases.Add([trial,trial,fake]);cases.Add([tinker,tinker,color]);
  foreach(var value in Enum.GetValues<TrialCaseTarget>())cases.Add([trial with {TrialCase=value}]);
  foreach(var type in Enum.GetValues<TinkerCardTypeTarget>())
  {
   cases.Add([tinker with {TinkerCardType=type,TinkerRider=null}]);
   foreach(var rider in Enum.GetValues<TinkerRiderTarget>().Where(r=>(int)r/3==(int)type))
    cases.Add([tinker with {TinkerCardType=type,TinkerRider=rider}]);
  }
  var authorities=new List<RuntimeContextAuthoritySnapshot>{basis.Authority};
  var effects=basis.Authority.EffectAuthority!;
  var candidates=Beta111EventResultCatalog.ColorfulCharacterOrder.Where(k=>k!=basis.Authority.Character.CharacterKey).ToArray();
  for(int length=0;length<=candidates.Length;length++)
   authorities.Add(basis.Authority.WithEffectAuthority(effects with {
    // Reverse, duplicate and include the owner: compilation must still use catalog order.
    UnlockedCharacterCardPoolKeys=candidates.Take(length).Reverse().Concat(candidates.Take(length))
     .Append(basis.Authority.Character.CharacterKey).ToArray()}));
  authorities.Add(basis.Authority.WithEffectAuthority(effects with {EventColorfulCharacterPoolsExact=false}));
  authorities.Add(basis.Authority.WithEffectAuthority(effects with {EventResultStaticCatalogExact=false}));
  Check(!Beta111EventResultAuthority.From(authorities[^2]).ColorfulPoolAuthorityExact,"missing Colorful authority fixture");
  Check(!Beta111EventResultAuthority.From(authorities[^1]).StaticCatalogAuthorityExact,"missing static authority fixture");
  ulong[] roots=Enumerable.Range(0,64).Select(i=>Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal((ulong)i)))
   .Concat(new[]{0UL,ulong.MaxValue,1UL<<63,(1UL<<63)-1}).ToArray();
  foreach(var authority in authorities)
  foreach(var conditions in cases)
  {
   var context=basis with {Authority=authority};
   var compiled=SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {EventResultConditions=conditions}),context);
   var request=new ExactSearchExecutionRequest(compiled,new("000000000000",10000,30,1),"000000000000",10000,
    CompiledSearchEvaluationProjector.Project(compiled).Evaluation,CombatRewardRoutePolicyContract.None,compiled.SemanticFingerprint);
   var plan=new EventResultFamilyPlan(request,prepareGpu:false);
   var metadata=new EventResultNumericPredicate.Compiled(conditions,plan.Authority);
   var compiledColor=EventResultCpuPlan.TryCompile(request,plan);
   foreach(ulong root in roots)
   {
    bool oldReject=OriginalEventNumericPredicate.RejectsEventResultForFamily(root,conditions,plan.Authority,0);
    Check(EventResultNumericPredicate.RejectsEventResultForFamily(root,metadata,0)==oldReject,"old numerical oracle");
    bool actual=plan.Matches(root),core=plan.Reference(root);
    Check(actual==core,"Core projector/Trial/Tinker reference");
    if(plan.NumericalSupported)Check(actual==!oldReject,"request plan vs original numerical");
    if(compiledColor is not null)Check(compiledColor.Matches(root)==core,"compiled color remaining");
    var disposition=Beta111CanonicalEventShopSearchEvaluator.Evaluate(request,root,authority).Disposition;
    // An unresolved earlier observable can end Exact before a later independent
    // mismatch. Only its definitive decisions constrain this optional sieve.
    if(disposition==SearchDisposition.Match)Check(actual,"canonical Match retained");
    else if(disposition==SearchDisposition.NoMatch)Check(!actual,"canonical NoMatch rejected");
    if(plan.Authority.StaticCatalogAuthorityExact && plan.Authority.ColorfulPoolAuthorityExact)
     Check((disposition==SearchDisposition.Match)==actual,"complete canonical evaluator");
   }
  }
  var noAuthority=new EventResultNumericPredicate.Compiled([fake,color],null);
  foreach(ulong root in roots)
   Check(EventResultNumericPredicate.RejectsEventResultForFamily(root,noAuthority,0)==
    OriginalEventNumericPredicate.RejectsEventResultForFamily(root,[fake,color],null,0),"null authority retains");
  Console.WriteLine($"E_ORDINARY_COMPILE_ORACLE_PASS checks={checks};cases={cases.Count};authorities={authorities.Count};roots={roots.Length};oldNumerical=true;restrictedPools=true;missingTargets=true;duplicates=true");
  return checks;
 }

 // Frozen b041630 numerical matcher: independent of the compiled metadata and
 // unchanged shuffle, fresh-root and missing-authority behavior for this oracle.
 private static class OriginalEventNumericPredicate
 {
     private static readonly ulong TrashHeapHash = XxHash64.Hash("TRASH_HEAP"u8, 0UL);
     private static readonly ulong FakeMerchantHash = XxHash64.Hash("FAKE_MERCHANT"u8, 0UL);
     private static readonly ulong ColorfulHash = XxHash64.Hash("COLORFUL_PHILOSOPHERS"u8, 0UL);
     private static readonly ulong ShopsHash = XxHash64.Hash("shops"u8, 0UL);
     private static readonly ulong TrialHash = XxHash64.Hash("TRIAL"u8, 0UL);
     private static readonly ulong TinkerTimeHash = XxHash64.Hash("TINKER_TIME"u8, 0UL);

     internal static bool RejectsEventResultForFamily(ulong rootHash, IReadOnlyList<EventResultSearchCondition> conditions,
         Beta111EventResultAuthority? authority, int playerSlot)
     {
         if (authority is null || !authority.IsBeta111 || !authority.StaticCatalogAuthorityExact)
             return false;

         bool needsTrashGrab = false;
         bool needsTrashDive = false;
         bool needsFake = false;
         bool needsColorful = false;
         foreach (EventResultSearchCondition condition in conditions)
         {
             switch (condition.Kind)
             {
                 case EventResultConditionKind.TrialCase:
                 {
                     var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, false, TrialHash));
                     if (rng.NextInt(3) != (int)condition.TrialCase!) return true;
                     break;
                 }
                 case EventResultConditionKind.TinkerTimeTypeAndRider:
                 {
                     var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, false, TinkerTimeHash));
                     Span<int> types = stackalloc int[] { 0, 1, 2 };
                     rng.UnstableShuffle(types);
                     int type = (int)condition.TinkerCardType!;
                     if (types[0] != type && types[1] != type) return true;
                     if (condition.TinkerRider is { } rider)
                     {
                         Span<int> riders = stackalloc int[] { type * 3, type * 3 + 1, type * 3 + 2 };
                         rng.UnstableShuffle(riders);
                         if (riders[0] != (int)rider && riders[1] != (int)rider) return true;
                     }
                     break;
                 }
                 case EventResultConditionKind.TrashHeapGrabCard: needsTrashGrab = true; break;
                 case EventResultConditionKind.TrashHeapDiveRelic: needsTrashDive = true; break;
                 case EventResultConditionKind.FakeMerchantOfferedFakeRelic: needsFake = true; break;
                 case EventResultConditionKind.ColorfulPhilosophersOfferedColor: needsColorful = true; break;
             }
         }

         ModelKey grab = default;
         ModelKey dive = default;
         Span<int> fakeOrder = stackalloc int[9];
         Span<int> colorfulOrder = stackalloc int[5];
         int colorfulCount = 0;

         if (needsTrashGrab)
         {
             var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: false, TrashHeapHash));
             grab = Beta111EventResultCatalog.TrashHeapGrabCards[rng.NextInt(Beta111EventResultCatalog.TrashHeapGrabCards.Count)];
         }
         if (needsTrashDive)
         {
             var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: false, TrashHeapHash));
             dive = Beta111EventResultCatalog.TrashHeapDiveRelics[rng.NextInt(Beta111EventResultCatalog.TrashHeapDiveRelics.Count)];
         }
         if (needsTrashGrab || needsTrashDive)
         {
             // Grab and Dive keep their existing fresh-root relationship. Once
             // that conjunction rejects, the independent Fake/Colorful groups
             // need no projection: their static bounded catalogs and bound
             // authority inputs introduce no required downstream operation fault.
             foreach (EventResultSearchCondition condition in conditions)
             {
                 if (condition.Kind == EventResultConditionKind.TrashHeapGrabCard && grab != condition.TargetKey)
                     return true;
                 if (condition.Kind == EventResultConditionKind.TrashHeapDiveRelic && dive != condition.TargetKey)
                     return true;
             }
         }
         if (needsFake)
         {
             for (int i = 0; i < fakeOrder.Length; i++) fakeOrder[i] = i;
             var rng = new Beta110FastRng(EventSeed(rootHash, playerSlot, shared: true, FakeMerchantHash));
             rng.UnstableShuffle(fakeOrder);
         }
         if (needsColorful && authority.ColorfulPoolAuthorityExact)
         {
             for (int i = 0; i < Beta111EventResultCatalog.ColorfulCharacterOrder.Count; i++)
             {
                 ModelKey candidate = Beta111EventResultCatalog.ColorfulCharacterOrder[i];
                 if (candidate == authority.OwnerCharacterKey || !Contains(authority.UnlockedCharacterCardPoolKeys, candidate))
                     continue;
                 colorfulOrder[colorfulCount++] = i;
             }
             var rng = ColorfulRng(rootHash, playerSlot);
             int targetCount = Math.Min(3, colorfulCount);
             while (colorfulCount > targetCount)
             {
                 int remove = rng.NextInt(colorfulCount);
                 for (int i = remove; i < colorfulCount - 1; i++) colorfulOrder[i] = colorfulOrder[i + 1];
                 colorfulCount--;
             }
         }

         foreach (EventResultSearchCondition condition in conditions)
         {
             switch (condition.Kind)
             {
                 case EventResultConditionKind.FakeMerchantOfferedFakeRelic:
                 {
                     int targetIndex = IndexOf(Beta111EventResultCatalog.FakeMerchantRelics, condition.TargetKey);
                     if (targetIndex < 0) return true;
                     bool found = false;
                     for (int i = 0; i < 6; i++) found |= fakeOrder[i] == targetIndex;
                     if (!found) return true;
                     break;
                 }
                 case EventResultConditionKind.ColorfulPhilosophersOfferedColor:
                 {
                     // Missing owner/unlock authority cannot produce a Fast rejection.
                     if (!authority.ColorfulPoolAuthorityExact) break;
                     int targetIndex = IndexOf(Beta111EventResultCatalog.ColorfulCharacterOrder, condition.TargetKey);
                     if (targetIndex < 0) return true;
                     bool found = false;
                     for (int i = 0; i < colorfulCount; i++) found |= colorfulOrder[i] == targetIndex;
                     if (!found) return true;
                     break;
                 }
             }
         }
         return false;
     }

     [MethodImpl(MethodImplOptions.AggressiveInlining)]
     internal static Beta110FastRng ColorfulRng(ulong rootHash, int playerSlot) =>
         new(EventSeed(rootHash, playerSlot, shared: false, ColorfulHash));

     [MethodImpl(MethodImplOptions.AggressiveInlining)]
     private static ulong EventSeed(ulong rootHash, int playerSlotIndex, bool shared, ulong eventHash)
     {
         unchecked
         {
             long signed = (long)rootHash + (shared ? 0L : playerSlotIndex);
             return (ulong)signed + eventHash;
         }
     }

     private static bool Contains(IReadOnlyList<ModelKey> values, ModelKey target)
     {
         for (int i = 0; i < values.Count; i++)
             if (values[i] == target) return true;
         return false;
     }

     private static int IndexOf(IReadOnlyList<ModelKey> values, ModelKey target)
     {
         for (int i = 0; i < values.Count; i++)
             if (values[i] == target) return i;
         return -1;
     }
 }

 private static int CheckAuthoredShape(SearchContext context)
 {
  var relic=new ModelKey("RELIC","TEST");var card=new ModelKey("CARD","TEST");
  SearchQuery[] malformed=[
   SearchQuery.Empty with {LegacyNeow=LegacyNeowSemanticConstraints.Empty with {NeowRelics=new([default(ModelKey)],[],[])}},
   SearchQuery.Empty with {VariantBossBranches=[new(0,new("ACT","TEST"),ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)]},
   SearchQuery.Empty with {AncientBranches=[new(2,relic,[],[])]},
   SearchQuery.Empty with {StructuredOpeningEffects=[new(default,NeowStructuredConditionKind.ExactSingle,NeowStructuredEffectScope.ProductRelevantEffects,NeowStructuredOutputKind.Card,[card])]},
   SearchQuery.Empty with {CombatCardRewards=new(2,CombatRewardSequenceOrderMode.Ordered,[card])},
   SearchQuery.Empty with {CombatPotionRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[new(CombatPotionSlotRequirement.Neutral,new("POTION","TEST"))])},
   SearchQuery.Empty with {RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.FirstN,0,new([relic],[],[]))]},
   SearchQuery.Empty with {EventResultConditions=[new((EventResultConditionKind)999,card)]}
  ];
  foreach(var query in malformed) {
   bool rejected=false;try{SearchCompiler.Compile(query,context);}catch(ArgumentException){rejected=true;}
   if(!rejected)throw new Exception("Malformed authored requirements were normalized instead of reported");
  }
  var impossible=SearchCompiler.Compile(SearchQuery.Empty with {RelicSequenceConstraints=[new(RelicSequenceKind.Common,SearchSequenceRangeMode.FirstN,1,new([],[relic],[relic]))]},context);
  if(impossible.Status!=QueryNormalizationStatus.Impossible)throw new Exception("Legal contradictory predicates must retain semantic Impossible");
  var repeated=SearchCompiler.Compile(SearchQuery.Empty with {CombatCardRewards=new(2,CombatRewardSequenceOrderMode.Unordered,[card,card])},context);
  if(repeated.NormalizedQuery.CombatCardRewards!.Slots.Count(k=>k==card)!=2)throw new Exception("Unordered reward multiplicity was lost");
  var typed=SearchCompiler.Compile(SearchQuery.Empty with {VariantBossBranches=[new(2,new("ACT","UNKNOWN_VARIANT"),ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)]},context with {Authority=context.Authority.WithWorldAuthority(null)});
  var projected=CompiledSearchEvaluationProjector.Project(typed);
  if(projected.Fidelity!=ProjectionFidelity.Exact||projected.Evaluation.VariantBossBranches.Count!=1||!projected.Evaluation.RequiresWorldDomain)
   throw new Exception("Missing Variant observations must preserve typed final obligations");
  var typedRequest=RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(typed,new("000000000000",1,1,1)).Plan;
  if(typedRequest is null||typedRequest.Evaluation.VariantBossBranches.Count!=1||!typedRequest.Evaluation.RequiresWorldDomain||
   typedRequest.CompiledSearch.NormalizedQuery.VariantBossBranches.Single().VariantKey!=new ModelKey("ACT","UNKNOWN_VARIANT"))
   throw new Exception("Formal Exact request must retain typed Variant obligations without World observations");
  var seaOnly=SearchCompiler.Compile(SearchQuery.Empty with {AncientBranches=[new(2,new("EVENT","OROBAS"),[],[BaseGameModelKeys.Characters.Silent])]},context);
  if(seaOnly.Status!=QueryNormalizationStatus.Legal||seaOnly.NormalizedQuery.AncientBranches.Single().SeaGlassTargetAny.Count!=1)
   throw new Exception("SeaGlass target without another option restriction is a valid authored observation");
  var manyParents=SearchCompiler.Compile(SearchQuery.Empty with {AncientBranches=Enumerable.Range(0,5).Select(i=>
   new AncientSearchBranchCondition(2,new("EVENT","UNKNOWN_PARENT_"+i),[],[])).ToArray()},context);
  if(manyParents.NormalizedQuery.AncientBranches.Count!=5||manyParents.Status!=QueryNormalizationStatus.Legal)
   throw new Exception("More than four meaningful parent alternatives must remain authored");
  Console.WriteLine("AUTHORED_SHAPE_PASS;invalidInputDistinctFromImpossible=true;typedVariantPreserved=true");
  return malformed.Length+6;
 }
 private static int CheckDonorAvailability(SearchContext context)
 {
  var effect=context.Authority.EffectAuthority!;
  var bones=BaseGameModelKeys.Relics.NeowsBones;var kale=BaseGameModelKeys.Relics.Kaleidoscope;var arcane=BaseGameModelKeys.Relics.ArcaneScroll;
  var opening=SearchQuery.Empty with {OpeningRoute=new(bones),OpeningRouteRelicRequirement=new(bones,[kale,arcane]),
   LegacyNeow=LegacyNeowSemanticConstraints.Empty with {RequiredFinalCurse=effect.GeneratedCursePool![0]}};
  ExactSearchExecutionRequest Bind(SearchQuery query,SearchContext c)=>RolltheSpire2.Search.Compilation.ExactSearchExecutionRequestFactory.Compile(
   SearchCompiler.Compile(query,c),new("000000000000",32,32,1)).Plan??throw new Exception("Missing-observation request was rejected");
  var complete=Bind(opening,context);
  if(!NeowFamily.TryCreate(complete,out var valid,false)||valid is null)throw new Exception("Complete N donor path was disabled");
  var missing=context with {Authority=context.Authority.WithEffectAuthority(effect with {OtherCharacterPools=[],OtherCharacterPoolsExact=false})};
  var incomplete=Bind(opening,missing);
  if(!NeowReplayPlan.Compile(incomplete,false).ContinuationIssue().StartsWith("DonorContinuationAuthorityUnavailable:")||
   NeowFamily.TryCreate(incomplete,out _,false)||incomplete.CompiledSearch.NormalizedQuery.OpeningRouteRelicRequirement!.RequiredRelicKeys.Count!=2)
   throw new Exception("Missing N companion donor facts were not retained outside filtering");
  var card=effect.CharacterRewardPool![0].CardKey;
  var reward=opening with {LegacyNeow=LegacyNeowSemanticConstraints.Empty,CombatCardRewards=new(1,CombatRewardSequenceOrderMode.Ordered,[card])};
  if(!CombatRewardFamily.TryCreate(Bind(reward,context),out _,false)||CombatRewardFamily.TryCreate(Bind(reward,missing),out _,false))
   throw new Exception("C donor availability changed complete path or admitted absent companion pool");
  var noWorld=context with {Authority=context.Authority.WithWorldAuthority(null)};
  var variant=SearchQuery.Empty with {VariantBossBranches=[new(2,new("ACT","UNKNOWN_VARIANT"),ModelKeySetFilter.Empty,ModelKeySetFilter.Empty,false)]};
  if(WorldFamily.TryCreate(Bind(variant,noWorld),out _,false))throw new Exception("Missing W observations fabricated a filter");
  var events=SearchQuery.Empty with {EventResultConditions=[new(EventResultConditionKind.TrashHeapGrabCard,new("CARD","TEST"))]};
  var eventUnknown=Bind(events,context with {Authority=context.Authority.WithEffectAuthority(effect with {EventResultStaticCatalogExact=false})});
  if(!new EventResultFamilyPlan(eventUnknown,prepareGpu:false).Reference(1))throw new Exception("Unknown E observation rejected a seed");
  Console.WriteLine("FILTER_DONOR_AVAILABILITY_PASS;completeNCPreserved=true;missingPoolRetainsQuery=true;unknownEWRetained=true");
  return 6;
 }
 private static int CheckGpuPreparation(ExactSearchExecutionRequest request)
 {
  int calls=0;
  uint[][] Fail(IReadOnlyList<Beta110GpuAncientOptionPreGateGate> gates,int scratch,int capacity)
  { calls++;throw new InvalidOperationException("InjectedAncientGpuPackingFailure"); }
  var cpu=new AncientOptionFamilyPlan(request,prepareGpu:false,gpuPacker:Fail);
  if(calls!=0||cpu.GpuSupported||cpu.Buffers.Length!=0)throw new Exception("A CPU plan invoked GPU packing");
  var failed=new AncientOptionFamilyPlan(request,gpuPacker:Fail);
  if(calls!=1||failed.GpuSupported||failed.GpuPreparationIssue!="GpuPlanConstructionFailed"||
   !failed.GpuPreparationFailureDetail.Contains("InjectedAncientGpuPackingFailure",StringComparison.Ordinal)||cpu.GpuPreparationFailureDetail.Length!=0)
   throw new Exception("A optional GPU fault lost the CPU plan");
  int passes=0,rejects=0;
  for(ulong i=0;i<128;i++) {
   ulong root=Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(i));
   bool expected=cpu.Reference(root);if(expected)passes++;else rejects++;
   if(cpu.Matches(root)!=expected||failed.Matches(root)!=expected)throw new Exception("A packing failure changed CPU option predicates");
  }
  if(passes==0||rejects==0)throw new Exception("A preparation fixture requires both predicate outcomes");
  Console.WriteLine("A_GPU_PREPARATION_ISOLATION_PASS;cpuIgnoresPacking=true;failedGpuRetainsPredicates=true");
  return 130;
 }
 private static int CheckAncientLegacyNumerical(SearchContext basis)
 {
  var world=basis.Authority.WorldAuthority!;var generation=world.Beta109Generation!;
  var missing=new ModelKey("RELIC","A_LEGACY_MISSING");
  var roots=Enumerable.Range(0,32).Select(i=>Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal((ulong)i)))
   .Concat(new[]{0UL,ulong.MaxValue,1UL<<63}).ToArray();
  int checks=0,solo=0,party=0,numerical=0,reference=0;
  void Probe(string name,Beta109WorldGenerationSnapshot g,NeowSearchFilter filter,int players,int slot,bool expectedNumerical)
  {
   var authority=Beta110FastSearchFixtureAuthority.CreateBeta111(basis.CharacterKey,10,slot,players)
    .WithWorldAuthority(world with {Beta109Generation=g});
   var context=basis with {Authority=authority};
   var query=LegacySearchQueryAdapter.FromFilter(filter);
   if(players>1)
   {
    // This fixture proves an owner child numerical contract, not whole-table
    // admission. The public boundary must still reject an unwrapped MP query.
    bool envelopeRejected=false;
    try { SearchCompiler.Compile(query,context); }
    catch(ArgumentException ex) when(ex.Message=="Party.WholeTableEnvelopeRequired") {envelopeRejected=true;}
    if(!envelopeRejected)throw new Exception("A child probe bypassed the public party envelope boundary");
   }
   // PartyInitialQuery uses this same private compiler for each owner context.
   var compiled=players==1?SearchCompiler.Compile(query,context):SearchCompiler.CompilePlayer(query,context);
   var request=new ExactSearchExecutionRequest(compiled,new("000000000000",67,int.MaxValue,1),"000000000000",67,
    CompiledSearchEvaluationProjector.Project(compiled).Evaluation,CombatRewardRoutePolicyContract.None,compiled.SemanticFingerprint);
   var plan=new AncientOptionFamilyPlan(request,prepareGpu:false);
   int act=filter.AncientOptionFilters[0].Act;
   if(plan.UsesNumericalAct(act)!=expectedNumerical)throw new Exception("A strict legacy selector "+name);
   if(expectedNumerical)numerical++;else reference++;
   foreach(ulong root in roots)
   {
    var expected=UncachedAncientReference(request,root);
    if(plan.Matches(root)!=(expected!=SearchDisposition.NoMatch))
     throw new Exception($"A legacy numerical/old provider {name}/{players}/{slot}/{root}");
    checks++;
   }
   if(players==1)solo++;else party++;
  }
  foreach(var source in generation.AncientEventContexts.Where(c=>c.Act>=2))
  {
   var options=source.Catalog!.Pools.SelectMany(p=>p.OrderedOptions).Where(k=>k.IsValid).Distinct().ToArray();
   var target=options[0];
   var filters=new ModelKeySetFilter[] {new([target],[],[]),new([],[target],[]),new([],[],[target]),
    new([missing],[],[]),new([],[missing],[]),new([],[],[missing])};
   foreach(var keys in filters)
   foreach(bool broad in new[]{true,false})
   {
    var filter=NeowSearchFilter.Empty with {AncientBranchConditions=[new(source.Act,source.AncientKey,[],[])],
     AncientOptionFilters=[new(source.Act,keys)],AncientOptionConditions=broad?RolltheSpire2.Core.World.AncientOptionConditionProfile.BroadDefault:
      new(false,false,false,false,false,false,false,false,false)};
    Probe(source.AncientKey.Entry+"/sets/"+broad,generation,filter,1,0,true);
   }
   var any=NeowSearchFilter.Empty with {AncientBranchConditions=[new(source.Act,source.AncientKey,[],[])],
    AncientOptionFilters=[new(source.Act,new([target],[],[]))]};
   Probe(source.AncientKey.Entry+"/sameAncientOR",generation,any with {AncientBranchConditions=[
    new(source.Act,source.AncientKey,[missing],[]),new(source.Act,source.AncientKey,options,[])]},1,0,true);
   Beta109WorldGenerationSnapshot Replace(params Beta109AncientEventContextSnapshot[] rows)=>generation with {
    AncientEventContexts=rows.Concat(generation.AncientEventContexts.Where(c=>c.Act!=source.Act||c.AncientKey!=source.AncientKey)).ToArray()};
   Probe(source.AncientKey.Entry+"/characterMismatch",Replace(source with {CharacterKey=BaseGameModelKeys.Characters.Silent}),any,1,0,false);
   Probe(source.AncientKey.Entry+"/catalogMismatch",Replace(source with {Catalog=source.Catalog with {EventIdEntry="A_WRONG_EVENT_ENTRY"}}),any,1,0,false);
   var proceed=new ModelKey("RELIC","A_WRAPPER_PROCEED");
   var denied=source with {HookDecision=Beta109HookDecision.Deny,DynamicFactsExact=true,
    Catalog=source.Catalog with {Pools=source.Catalog.Pools.Append(new Beta109NamedOptionPoolSnapshot("wrapper.proceed",[proceed],true)
     {SourceOrdinal=999,FilterResultExact=true}).ToArray()}};
   Probe(source.AncientKey.Entry+"/hookDenied",Replace(denied),any with {AncientOptionFilters=[new(source.Act,new([proceed],[],[]))]},1,0,true);
   foreach(int players in new[]{2,3,4})
   for(int slot=0;slot<players;slot++)
   foreach(bool shared in new[]{false,true})
   {
    // Fixed synthetic party pools isolate context priority and seed ownership;
    // they are not represented as captured multiplayer eligibility evidence.
    var personal=source with {PlayerSlot=slot,IsShared=false};
    var fallback=source with {PlayerSlot=7,IsShared=true};
    var g=Replace(shared?[fallback]:[fallback,personal]) with {
     GameMode=WorldGameMode.Multiplayer,IsMultiplayer=true,PlayerCount=players,PersonalPlayerSlot=slot,
     PartyBossDiscoveryExact=true,
     LobbyPlayers=Enumerable.Range(0,players).Select(i=>new Beta109LobbyPlayerSnapshot(i,basis.CharacterKey,false,true)).ToArray(),
     PartyRelicBuckets=Enumerable.Repeat(generation.PlayerRelicBuckets,players).ToArray()};
    Probe(source.AncientKey.Entry+(shared?"/shared":"/personalBeforeShared"),g,any,players,slot,true);
   }
  }
  Console.WriteLine($"A_LEGACY_NUMERICAL_ORACLE_PASS checks={checks};soloCases={solo};partyChildCases={party};numerical={numerical};reference={reference};oldProvider=true");
  return checks;
 }
 private static int CheckAncientReferenceBindings(SearchContext context)
 {
  var world=context.Authority.WorldAuthority!;var generation=world.Beta109Generation!;
  var pael=generation.AncientEventContexts.First(c=>c.AncientKey.Entry=="PAEL");
  var orobas=generation.AncientEventContexts.First(c=>c.AncientKey.Entry=="OROBAS");
  var third=generation.AncientEventContexts.First(c=>c.Act==3);
  var paelCatalog=pael.Catalog??throw new Exception("A binding fixture requires Pael catalog");
  var first=paelCatalog.Pool("pael.pool1");var growth=paelCatalog.Pool("pael.pool2.growth")[0];
  var sea=new ModelKey("RELIC","SEA_GLASS");var empty=NeowSearchFilter.Empty;
  NeowSearchFilter Pael(ModelKeySetFilter keys)=>empty with {
   AncientBranchConditions=[new(pael.Act,pael.AncientKey,[],[])],AncientOptionFilters=[new(pael.Act,keys)]};
  var any=Pael(new([first[0]],[],[]));
  var seaFilter=empty with {AncientBranchConditions=[new(orobas.Act,orobas.AncientKey,[],[])],
   AncientOptionFilters=[new(orobas.Act,new([sea],[],[]))],
   AncientSeaGlassTargetFilters=[new(orobas.Act,new([BaseGameModelKeys.Characters.Silent],[],[]))]};
  var shared=pael with {IsShared=true,PlayerSlot=7,Catalog=paelCatalog with {
   Pools=paelCatalog.Pools.Select(p=>p.PoolId=="pael.pool1"?p with {OrderedOptions=[first[1]]}:p).ToArray()}};
  var withoutPael=generation.AncientEventContexts.Where(c=>c.Act!=pael.Act||c.AncientKey!=pael.AncientKey).ToArray();
  var personalFirst=generation with {AncientEventContexts=new[]{shared,pael}.Concat(withoutPael).ToArray()};
  var sharedOnly=generation with {AncientEventContexts=new[]{shared}.Concat(withoutPael).ToArray()};
  var missing=generation with {AncientEventContexts=withoutPael};
  var cases=new List<(string Name,NeowSearchFilter Filter,Beta109WorldGenerationSnapshot Generation)> {
   ("LegacyAny",any,generation),
   ("LegacyAll",Pael(new([],[first[0],growth],[])),generation),
   ("LegacyBan",Pael(new([],[],[first[0]])),generation),
   ("LegacyAnyAllBan",Pael(new([first[0],first[1]],[growth],[first[1]])),generation),
   ("LegacySea",seaFilter,generation),
   ("LegacySeaUnknown",seaFilter,generation with {AncientEventContexts=generation.AncientEventContexts.Select(c=>
    c.AncientKey==orobas.AncientKey?c with {UnlockedCharacterSourceOrderExact=false}:c).ToArray()}),
   ("PersonalBeforeShared",any,personalFirst),
   ("SharedFallback",any,sharedOnly),
   ("MissingContext",any,missing),
   ("MissingAct",empty with {AncientOptionFilters=[new(pael.Act,new([first[0]],[],[]))]},
    generation with {AncientEventContexts=generation.AncientEventContexts.Where(c=>c.Act!=pael.Act).ToArray()}),
   ("UnconditionalMissingContext",empty with {AncientBranchConditions=[new(pael.Act,pael.AncientKey,[],[]),
    new(orobas.Act,orobas.AncientKey,[sea],[])]},missing),
   ("MultipleActs",any with {AncientBranchConditions=[new(pael.Act,pael.AncientKey,[],[]),new(third.Act,third.AncientKey,[],[])],
    AncientOptionFilters=[new(pael.Act,new([first[0]],[],[])),new(third.Act,new([third.Catalog!.Pools.SelectMany(p=>p.OrderedOptions).First()],[],[]))]},generation)
  };
  var roots=Enumerable.Range(0,64).Select(i=>Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal((ulong)i)))
   .Concat(new[]{0UL,ulong.MaxValue,1UL<<63}).ToArray();
  int checks=0,personalMatches=0,seaUnknown=0;
  foreach(var test in cases)
  {
   var testContext=context with {Authority=context.Authority.WithWorldAuthority(world with {Beta109Generation=test.Generation})};
   var compiled=SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(test.Filter),testContext);
   var request=new ExactSearchExecutionRequest(compiled,new("000000000000",67,int.MaxValue,1),"000000000000",67,
    CompiledSearchEvaluationProjector.Project(compiled).Evaluation,CombatRewardRoutePolicyContract.None,compiled.SemanticFingerprint);
   var current=new AncientOptionFamilyPlan(request,prepareGpu:false);
   foreach(ulong root in roots)
   {
    var before=RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Revision;
    var expected=UncachedAncientReference(request,root);var actual=current.ReferenceDisposition(root);
    if(actual!=expected)throw new Exception($"A cached/old provider oracle {test.Name}/{root}: {actual}!={expected}");
    if(RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Revision!=before)
     throw new Exception("A reference binding probes created an execution notice");
    if((test.Name is "MissingContext" or "MissingAct") && actual!=SearchDisposition.Unknown)
     throw new Exception("A missing reference context falsely resolved");
    if(test.Name=="UnconditionalMissingContext"&&actual!=SearchDisposition.Match)
     throw new Exception("A unconditional scope was blocked by its missing context");
    if(test.Name=="SharedFallback"&&actual!=SearchDisposition.NoMatch)
     throw new Exception("A shared fallback did not use its different immutable pool");
    if(test.Name=="PersonalBeforeShared"&&actual==SearchDisposition.Match)personalMatches++;
    if(test.Name=="LegacySeaUnknown"&&actual==SearchDisposition.Unknown)seaUnknown++;
    // This special case exercises the unconditional reference shortcut itself;
    // the remaining cases compare the selected numerical/reference CPU path.
    if(test.Name!="UnconditionalMissingContext"&&current.Matches(root)!=(expected!=SearchDisposition.NoMatch))
     throw new Exception("A cached execution retained/rejected differently from the old provider oracle");
    checks+=2;
   }
  }
  if(personalMatches==0||seaUnknown==0)throw new Exception("A binding fixtures lack personal-precedence/Unknown witnesses");
  Console.WriteLine($"A_REFERENCE_BINDING_ORACLE_PASS checks={checks};cases={cases.Count};rootsPerCase={roots.Length};personalMatches={personalMatches};seaUnknown={seaUnknown};oldProviderLogic=true");
  return checks;
 }
 // Frozen HEAD ReferenceRow/ReferenceDisposition oracle: reselect all static
 // inputs per root, independently of A3's binding, then use the original provider.
 private static SearchDisposition UncachedAncientReference(ExactSearchExecutionRequest request,ulong root)
 {
  var e=request.Evaluation;var generation=request.Authority.WorldAuthority!.Beta109Generation!;
  var filter=NeowSearchFilter.Empty with {AncientBranchConditions=e.AncientBranchConditions,
   AncientOptionFilters=e.AncientOptionFilters,AncientSeaGlassTargetFilters=e.AncientSeaGlassTargetFilters,
   AncientOptionConditions=e.AncientOptionConditions};
  var acts=e.AncientBranchConditions.Where(b=>b.OptionAny.Count>0||b.SeaGlassTargetAny.Count>0).Select(b=>b.Act)
   .Concat(e.AncientOptionFilters.Where(f=>!f.IsEmpty).Select(f=>f.Act))
   .Concat(e.AncientSeaGlassTargetFilters.Where(f=>!f.IsEmpty).Select(f=>f.Act)).Distinct().Order();
  bool unresolved=false;
  foreach(int act in acts)
  {
   var branches=e.AncientBranchConditions.Where(b=>b.Act==act).ToArray();
   var keys=branches.Length>0?branches.Select(b=>b.AncientKey).Distinct().ToArray():
    generation.AncientEventContexts.Where(c=>c.Act==act).Select(c=>c.AncientKey).Distinct().ToArray();
   if(keys.Length==0){unresolved=true;continue;}
   bool matched=false,rowUnresolved=false;
   foreach(var key in keys)
   {
    var disposition=Row(act,key);
    if(disposition==SearchDisposition.Match){matched=true;break;}
    rowUnresolved|=disposition!=SearchDisposition.NoMatch;
   }
   if(!matched&&!rowUnresolved)return SearchDisposition.NoMatch;
   unresolved|=!matched;
  }
  return unresolved?SearchDisposition.Unknown:SearchDisposition.Match;
  SearchDisposition Row(int act,ModelKey key)
  {
   var branches=filter.AncientBranchConditions.Where(b=>b.Act==act&&b.AncientKey==key).ToArray();
   if(branches.Any(b=>b is {OptionAny.Count:0,SeaGlassTargetAny.Count:0})&&
    !filter.AncientOptionFilters.Any(f=>f.Act==act&&!f.IsEmpty)&&!filter.AncientSeaGlassTargetFilters.Any(f=>f.Act==act&&!f.IsEmpty))
    return SearchDisposition.Match;
   var context=generation.AncientEventContexts.FirstOrDefault(c=>c.Act==act&&c.AncientKey==key&&c.PlayerSlot==request.Authority.PlayerSlotIndex)
    ??generation.AncientEventContexts.FirstOrDefault(c=>c.Act==act&&c.AncientKey==key&&c.IsShared);
   if(context is null)return SearchDisposition.Unknown;
   var snapshot=generation with {RunSeedRoot=root,AncientEventContexts=[context with {
    EventRngRoot=Beta109WorldRng.DeriveEventLocalSeed(root,context.PlayerSlot,context.IsShared,context.EventIdEntry)}]};
   var prediction=Beta109AncientOptionProvider.PredictEventLocal(snapshot,act,key,request.Authority.PlayerSlotIndex,
    request.CharacterKey,filter.AncientOptionConditions);
   if(prediction.EvaluationStatus is not (AncientOptionsEvaluationStatus.EvaluatedNonEmpty or AncientOptionsEvaluationStatus.EvaluatedKnownEmpty or AncientOptionsEvaluationStatus.EvaluatedLocked)||
    prediction.Options.Any(o=>o.OptionPrecision!=PredictionPrecision.Exact))return SearchDisposition.Unknown;
   var options=prediction.Options.Where(o=>o.IsVisible).ToArray();var sea=options.FirstOrDefault(o=>o.OptionKey.Entry=="SEA_GLASS");
   bool branchMatched=branches.Length==0,branchUnresolved=false;
   foreach(var branch in branches)
   {
    if(branch.OptionAny.Count>0&&!options.Any(o=>branch.OptionAny.Contains(o.OptionKey)))continue;
    if(branch.SeaGlassTargetAny.Count>0)
    {
     if(sea is null)continue;
     if(sea.CharacterTarget is not {Precision:PredictionPrecision.Exact,CharacterKey:{} target}){branchUnresolved=true;continue;}
     if(!branch.SeaGlassTargetAny.Contains(target))continue;
    }
    branchMatched=true;break;
   }
   if(!branchMatched&&!branchUnresolved)return SearchDisposition.NoMatch;
   if(!filter.AncientOptionFilters.Where(f=>f.Act==act).All(f=>Set(options.Select(o=>o.OptionKey),f.Keys)))return SearchDisposition.NoMatch;
   if(sea is not null&&filter.AncientSeaGlassTargetFilters.Any(f=>f.Act==act&&!f.IsEmpty))
   {
    if(sea.CharacterTarget is not {Precision:PredictionPrecision.Exact,CharacterKey:{} target})return SearchDisposition.Unknown;
    if(!filter.AncientSeaGlassTargetFilters.Where(f=>f.Act==act).All(f=>Set([target],f.Keys)))return SearchDisposition.NoMatch;
   }
   return branchMatched?SearchDisposition.Match:SearchDisposition.Unknown;
  }
  static bool Set(IEnumerable<ModelKey> values,ModelKeySetFilter filter)
  {
   var observed=values.ToArray();return(filter.Any.Count==0||filter.Any.Any(observed.Contains))&&
    filter.All.All(observed.Contains)&&!filter.Ban.Any(observed.Contains);
  }
 }
 private sealed class UnknownSurvivalProbe(IFamilyInvocation inner) : IFamilyInvocation
 {
  public string FamilyId=>inner.FamilyId;
  public FamilyAnalyticalCostProjection AnalyticalCost=>inner.AnalyticalCost;
  public FamilySurvivalProjection Survival=>FamilySurvivalProjection.Unresolved(FamilyId,"ExplicitUnknownTerminalFixture");
  public FamilyConditionPerformanceProjection ConditionPerformance=>inner.ConditionPerformance;
  public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compact)=>inner.ResolveConditionPerformance(compact);
  public FamilyPerformanceObservation CapturePerformanceObservation()=>inner.CapturePerformanceObservation();
  public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context,FamilyObservationWindow window,FamilyCandidateSet input,CancellationToken token)=>inner.InvokeAsync(context,window,input,token);
  public ValueTask DisposeAsync(FamilyExecutionContext context)=>inner.DisposeAsync(context);
 }
}
