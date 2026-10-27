using RolltheSpire2.Core.Authority;
using System.Collections;
using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Profiles;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class PredictorClosureChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        void Fault(Action action, string label)
        {
            try { action(); throw new Exception("Failure swallowed: " + label); }
            catch (InjectedPredictionFailure) { checks++; }
        }
        string corpus = Path.Combine(Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(corpus,"n-direct-v1/context-defect-a10.json")).Context;
        var authority = context.Authority;
        var effects = authority.EffectAuthority!;
        var world = authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        const string seed = "N9U2V9VPUSR7";
        var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
        ulong root = input.RootHash;

        // Direct prediction has no Search plan/Family registration and must not use
        // a captured bag from some other root when immutable source pools exist.
        var bare = authority.WithEffectAuthority(effects with { OrderedRelicBag = null, RelicBagExact = false });
        foreach (var relic in new[] { BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.NeowsBones })
        {
            var direct = new NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,bare,true).Project(relic);
            var preparedBag = NeowRewardGenerator.BuildOrderedRelicBag(Beta111Profile.Instance,root,effects.SharedRelicPoolSource!,effects.CharacterRelicPoolSource!);
            var prepared = bare.WithEffectAuthority(effects with { OrderedRelicBag=preparedBag,RelicBagExact=true });
            var reference = new NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,prepared,true).Project(relic);
            Check(JsonSerializer.Serialize(direct)==JsonSerializer.Serialize(reference),"Direct/prepared prediction parity: "+relic);
            Check(direct.ProductRelevantProjectionStatus==ProductRelevantProjectionStatus.Evaluated,"Direct prediction evaluated: "+relic);
        }
        Check(bare.EffectAuthority!.OrderedRelicBag is null,"Prediction does not mutate captured authority");
        var unavailable=authority.WithEffectAuthority(effects with {
            AuthoritySource=NeowEffectAuthoritySource.RuntimeSnapshot,RelicBagSourcePoolsExact=false
        });
        var unavailableCapsule=new NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,unavailable,true).Project(BaseGameModelKeys.Relics.LargeCapsule);
        Check(unavailableCapsule.ProductRelevantProjectionPrecision!=PredictionPrecision.Exact,"Unbound captured bag does not grant root-local authority");

        var brokenWorld = world with { Beta109Generation=generation with {
            CanonicalSeed="different", SelectedActsExact=false,
            ActSelectionGroups=new ThrowingList<Beta109ActSelectionGroupSnapshot>() } };
        Fault(()=>WorldPredictionEngine.PredictFromRootHash(Beta111Profile.Instance,root,seed,context.Ascension,0,context.CharacterKey,brokenWorld,effects),"World");
        Fault(()=>RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,root,seed,brokenWorld),"Relic");
        Fault(()=>NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,root,seed,0,context.Ascension,[],effects,brokenWorld),"Combat starting-act preparation");
        var brokenEffects = authority.WithEffectAuthority(effects with {OrderedDeck=new ThrowingList<NeowEffectCardSnapshot>()});
        Fault(()=>new NeowEffectProjectionEngine(Beta111Profile.Instance,"test",root,brokenEffects,true).Project(BaseGameModelKeys.Relics.WingedBoots),"Neow");

        var projected = Beta109WorldSnapshotProjector.ProjectForRootHash(generation,root,seed);
        var vakuu = projected.AncientEventContexts.First(c=>c.AncientKey.Entry=="VAKUU");
        try
        {
            Beta109AncientOptionProvider.Predict(projected with {
                AncientEventContexts=projected.AncientEventContexts.Select(c=>c==vakuu?c with {EventRngRoot=c.EventRngRoot^1}:c).ToArray()
            },vakuu.Act,vakuu.AncientKey,vakuu.PlayerSlot,vakuu.CharacterKey,true,context.EvaluationAssumptions.AncientEligibilityAssumptions);
            throw new Exception("Corrupt exact root swallowed");
        }
        catch(InvalidOperationException ex) when(ex.Message.StartsWith("EventRngRootMismatch:")) { checks++; }
        var badVakuu = vakuu with { Catalog=vakuu.Catalog! with { Pools=vakuu.Catalog!.Pools.Select(p=>p with {OrderedOptions=new ThrowingList<ModelKey>()}).ToArray() } };
        Fault(()=>Beta109AncientOptionProvider.Predict(projected with {
            AncientEventContexts=projected.AncientEventContexts.Select(c=>c==vakuu?badVakuu:c).ToArray()
        },vakuu.Act,vakuu.AncientKey,vakuu.PlayerSlot,vakuu.CharacterKey,true,context.EvaluationAssumptions.AncientEligibilityAssumptions),"Ancient generator");

        // Missing authority remains an explicit modeled Unknown, not an exception.
        Check(WorldPredictionEngine.PredictFromRootHash(Beta111Profile.Instance,root,seed,10,0,context.CharacterKey,null,effects).BossStatus==SeedDomainEvaluationStatus.Unknown,"World missing authority");
        Check(RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance,root,seed,null).Status==SeedDomainEvaluationStatus.Unknown,"Relic missing authority");
        var treasureWorld = RelicSequenceFixtureFactory.CreateModern();
        var treasure = RelicSequencePredictor.PredictFromRootHash(Beta109Profile.Instance,
            Beta109Profile.Instance.ComputeRootSeed(seed), seed, treasureWorld);
        Check(treasure.Status == SeedDomainEvaluationStatus.Evaluated && treasure.TreasureRoomLanes.Count == 3,
            "Treasure room projects three shared relic lanes");
        foreach (var lane in treasure.TreasureRoomLanes)
        {
            var source = treasureWorld.Beta109Generation!.SharedRelicBuckets.First(bucket =>
                bucket.BucketId.EndsWith(":" + lane.RarityCode, StringComparison.Ordinal));
            Check(lane.PullDirection == RelicSequencePullDirection.Front &&
                  lane.FullEntries.Select(entry => entry.RelicKey).OrderBy(key => key.Serialized)
                      .SequenceEqual(source.OrderedRelics.OrderBy(key => key.Serialized)),
                "Treasure lane uses the shared pool, not the player pool: " + lane.RarityCode);
        }

        var faultWorkload=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(corpus,"wwp-commitment-v1/authored-wp-positive.json"));
        var faultPlan=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(faultWorkload.Query,context),faultWorkload.Options with {ScanCount=8,WorkerCount=1}).Plan!;
        var pairQuery=faultWorkload.Query with {StructuredOpeningEffects=[]};
        var pairContext=context with {Authority=unavailable};
        var pairPlan=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(pairQuery,pairContext),faultWorkload.Options).Plan!;
        var pairResult=ProductionExactSearchEvaluator.Evaluate(pairPlan,input);
        Check(pairResult.IsMatch,"Missing Capsule bag does not block known Bones pair identity: "+pairResult.FailureCode);
        foreach(var ordered in new[] {pairQuery.OpeningRouteRelicRequirement!.RequiredRelicKeys,
            pairQuery.OpeningRouteRelicRequirement!.RequiredRelicKeys.Reverse().ToArray()})
        {
            var pinned=pairQuery with {OpeningRouteRelicRequirement=pairQuery.OpeningRouteRelicRequirement! with {
                RequiredRelicKeys=ordered,OrderMode=BonesRouteOrderMode.ExactOrder}};
            var pinnedPlan=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(pinned,pairContext),faultWorkload.Options).Plan!;
            Check(ProductionExactSearchEvaluator.Evaluate(pinnedPlan,input).IsMatch,"Both identity-only acquisition orders remain legal");
        }
        var wrongPair=pairQuery with {OpeningRouteRelicRequirement=pairQuery.OpeningRouteRelicRequirement! with {
            RequiredRelicKeys=[BaseGameModelKeys.Relics.Kaleidoscope,BaseGameModelKeys.Relics.SmallCapsule]}};
        var wrongPairPlan=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(wrongPair,pairContext),faultWorkload.Options).Plan!;
        Check(ProductionExactSearchEvaluator.Evaluate(wrongPairPlan,input).Evaluation.Disposition==SearchDisposition.NoMatch,"Known wrong pair is NoMatch");
        var capsulePlan=faultPlan with {CompiledSearch=SearchCompiler.Compile(faultWorkload.Query,pairContext)};
        Check(ProductionExactSearchEvaluator.Evaluate(capsulePlan,input).Evaluation.Disposition==SearchDisposition.Unknown,"Missing Capsule authority is Unknown, not a fabricated Match/NoMatch");
        var noFilter=FamilyPlanner.InOrder([]);
        var faultSession=new ProductionSearchSession(faultPlan,noFilter,FamilySearchEtaProjectorV1.Project(faultPlan,noFilter),8,16,null,"",
            exactEvaluator:(p,i)=>{
                WorldPredictionEngine.PredictFromRootHash(Beta111Profile.Instance,i.RootHash,i.SeedIdentity,10,0,context.CharacterKey,brokenWorld,effects);
                throw new Exception("Predictor failure swallowed");
            });
        Check(faultSession.Completion.Wait(15000),"Predictor fault session completes");
        Check(RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec.TryParseOrdinal(faultPlan.CanonicalStartSeed,out ulong faultStart,out _,out _),"Fault cursor input");
        Check(faultSession.GetProgress().State==SearchRunState.Faulted && faultSession.GetSafeNextOrdinal()==faultStart &&
            !faultSession.TryReadCandidate(out _),"Real Predictor fault preserves safe cursor and publishes no match");
        faultSession.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Profiles must not activate Neow for an independent World/Relic horizon.
        foreach (var profile in new[] {RuntimeProfileId.Beta110,RuntimeProfileId.Beta111})
        {
            string version=profile==RuntimeProfileId.Beta110?"0.110.1":"0.111.0";
            var independent=new RuntimeContextAuthoritySnapshot(authority.Character,authority.Ascension,0,1,true,false,
                authority.AllCharacterCardPoolsUnlocked,authority.UnlockedCommonCards,authority.UnlockedUncommonCards,
                version,profile,true,null,null,authority.SourceAuthority,authority.Completeness,authority.ResolutionStatus,
                null,world with {CapturedProfileId=profile,Beta109Generation=generation with {Profile=profile}},
                authority.PredictionGameMode,authority.PredictionGameModeAuthority);
            Check(SeedPredictionRequest.TryCreate(seed,independent.Character,10,1,0,independent,
                context.EvaluationAssumptions.AncientEligibilityAssumptions,SeedPredictionDomainSelection.World|SeedPredictionDomainSelection.RelicSequence,
                false,out var request,out _),"Independent request");
            var document=RuntimeProfileRegistry.Predict(new(version,version,"test","test",true,"test"),request!);
            Check(document.Sections.Any(s=>s.RelicSequencePrediction?.Status==SeedDomainEvaluationStatus.Evaluated),"No Neow admission for R: "+profile);
            Check(document.Sections.Any(s=>s.Kind==PredictionSectionKind.BossIdentity && s.DomainStatus==SeedDomainEvaluationStatus.Evaluated),"No Neow admission for W: "+profile);
            Check(!document.Sections.Any(s=>s.NeowChoices.Count>0),"Unrequested Neow not activated");
        }

        // Existing committed-route corpus: invoke Exact directly with no Filter.
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(corpus,"wwp-commitment-v1/manifest.json")));
        foreach(var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
        {
            string dir=Path.Combine(corpus,"wwp-commitment-v1");
            var c=ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.GetFullPath(Path.Combine(dir,entry.GetProperty("Context").GetString()!))).Context;
            var workload=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(Path.Combine(dir,entry.GetProperty("Path").GetString()!));
            var plan=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query,c),workload.Options).Plan!;
            Check(RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec.TryParseOrdinal(plan.CanonicalStartSeed,out ulong start,out _,out _),"Corpus root interval");
            int accepted=0;
            for(ulong i=0;i<(ulong)plan.ScanCount;i++)
            {
                string candidate=RolltheSpire2.Search.FamilyExecution.Beta110SeedCodec.FormatOrdinal(start+i);
                var result=ProductionExactSearchEvaluator.Evaluate(plan,TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,candidate));
                if(result.IsMatch) {accepted++;Check(result.Evaluation.Witnesses.Count>0,"Committed witness");}
            }
            Check(accepted==entry.GetProperty("ExpectedMatches").GetInt32(),"Committed Exact count: "+entry.GetProperty("Path").GetString());
        }
        Console.WriteLine($"PredictorClosure checks={checks};PASS");return 0;
    }
    private sealed class InjectedPredictionFailure : Exception { }
    private sealed class ThrowingList<T> : IReadOnlyList<T>
    {
        public int Count=>1;
        public T this[int index]=>throw new InjectedPredictionFailure();
        public IEnumerator<T> GetEnumerator()=>throw new InjectedPredictionFailure();
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
