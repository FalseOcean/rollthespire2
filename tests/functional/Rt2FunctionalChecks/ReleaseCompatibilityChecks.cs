using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class ReleaseCompatibilityChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string issue){checks++;if(!value)throw new InvalidOperationException(issue);}
        var options=ProductionSearchReplay.JsonOptions;
        string corpus="tools/ProductionSearchHost/Corpus/";
        string raw=File.ReadAllText(corpus+"v1/context.json");
        var current=JsonSerializer.Deserialize<ProductionSearchReplayContext>(raw,options)!.Context;
        // Metadata-only simulation of unchanged captured mechanics, NOT future game evidence.
        var newer=JsonSerializer.Deserialize<ProductionSearchReplayContext>(raw.Replace("0.111.0","0.112.0"),options)!.Context;
        var validation=Beta109AcceptedRuntimeValidationAuthority.Resolve("0.112.0");
        var world=newer.Authority.WorldAuthority!;
        newer=newer with {Authority=newer.Authority.WithWorldAuthority(world with {
            Beta109Generation=world.Beta109Generation! with {FixtureValidationStatus=validation.Status,
                VerifiedRealGameFixtureIds=validation.FixtureIds}})};
        Check(newer.Authority.VersionResolution.AllowsProductionSearch && !newer.Authority.VersionResolution.RuntimeAccepted,
            "Unvalidated version remains usable, never falsely accepted");
        Check(validation.EvidenceCode.Contains("unverified") && validation.Status==Beta109FixtureValidationStatus.AssumedCompatibleLatestKnown,
            "World reference reuse provenance");
        Check(newer.Authority.WorldAuthority!.Beta109Generation!.HasVerifiedBossVersionFixture,
            "reference generator fixture is available without claiming new-version validation");
        var modNode=JsonNode.Parse(raw)!;
        modNode["Context"]!["Authority"]!["SourceModId"]="cosmetic-metadata-fixture";
        var cosmetic=modNode.Deserialize<ProductionSearchReplayContext>(options)!.Context;
        ExactSearchExecutionRequest Compile(ProductionSearchReplayWorkload w,SearchContext c) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query,c),w.Options with {ScanCount=8193,WorkerCount=1}).Plan
                ?? throw new InvalidOperationException("Fixture compile rejected");
        foreach(string id in new[]{"N-entry","R-common","W-K4","A-pael","S-three-shops","C-three-potions"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(corpus+"v1/cases/"+id+".json");
            var baseline=Compile(w,current);
            foreach(var c in new[]{newer,cosmetic})
            {
                var p=Compile(w,c);
                for(ulong i=0;i<67;i++)
                {
                    var input=TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,Beta110SeedCodec.FormatOrdinal(i));
                    var a=ProductionExactSearchEvaluator.Evaluate(baseline,input);
                    var b=ProductionExactSearchEvaluator.Evaluate(p,input);
                    Check(a.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch,id+" baseline truth available");
                    Check(a.Evaluation.Disposition==b.Evaluation.Disposition,id+" metadata-only Exact disposition");
                    Check(JsonSerializer.Serialize(a.Evaluation.Witnesses,options)==JsonSerializer.Serialize(b.Evaluation.Witnesses,options),id+" witness");
                }
            }
        }
        var partialNode=JsonNode.Parse(raw)!;
        var auth=partialNode["Context"]!["Authority"]!;
        auth["AllCharacterCardPoolsUnlocked"]=false;
        var pool=(JsonArray)auth["EffectAuthority"]!["MerchantColorlessOrderedPool"]!;
        var selected=pool.Where(x=>x!["Rarity"]!.GetValue<string>()=="Uncommon").Take(4)
            .Concat(pool.Where(x=>x!["Rarity"]!.GetValue<string>()=="Rare").Take(3)).Select(x=>x!.DeepClone()).ToArray();
        auth["EffectAuthority"]!["MerchantColorlessOrderedPool"]=new JsonArray(selected);
        var partial=partialNode.Deserialize<ProductionSearchReplayContext>(options)!.Context;
        var sw=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(corpus+"cpu-numerical-closure-v1/S-O3.json");
        Check(ModelKey.TryParseExact(selected[0]["CardKey"]!.GetValue<string>(),out var target),"partial target identity");
        sw=sw with {Query=sw.Query with {MerchantColorlessSequenceConditions=[new(3,CombatRewardSequenceOrderMode.Ordered,
            MerchantColorlessSlot.Uncommon,[target,target,target])]}};
        var sp=Compile(sw,partial);var fast=MerchantShopColorlessCpuPlan.TryCompile(sp)!;
        Check(fast is not null,"partial pool execution binding");
        checks+=RelicCpuChecks.CheckEnvelope(sp,fast!.Matches,root=>MerchantShopColorlessQueryEvaluator.Evaluate(sp.Evaluation,root,sp.Authority).Disposition==SearchDisposition.Match);
        MerchantShopColorlessFamily.TryCreate(sp,out var owner);
        Check(owner!.CpuRealizations.First().QuotePhysicalWork(new(false,false,65536)) is null,"partial geometry price Unknown, execution legal");
        Check(!partial.Authority.IsKaleidoscopeAllowed && partial.Authority.HasExactModernNeowIdentityInputs,"partial unlock is eligibility fact, not whole Search gate");
        foreach(bool? catalog in new bool?[]{null,false})
        {
            var n=JsonNode.Parse(raw)!;n["Context"]!["Authority"]!["VanillaNeowCatalogExact"]=catalog.HasValue?JsonValue.Create(catalog.Value):null;
            var changed=n.Deserialize<ProductionSearchReplayContext>(options)!.Context;
            Check(SeedPredictionRequest.TryCreate("000000000000",changed.Authority.Character,changed.Ascension,1,0,changed.Authority,true,out var prediction,out _),"boundary request");
            var doc=RuntimeProfileRegistry.Predict(changed.Detection,prediction!);
            var neow=doc.Sections.Single(s=>s.Kind==PredictionSectionKind.NeowIdentity);
            var expectedStatus=catalog.HasValue?SeedDomainEvaluationStatus.Unsupported:SeedDomainEvaluationStatus.Unknown;
            Check(neow.DomainStatus==expectedStatus && neow.NeowChoices.Count==0 &&
                neow.IssueCode==(catalog.HasValue?"NonVanillaCatalogUnsupported":"SnapshotAuthorityIncomplete"),
                "missing vs changed Neow authority stays explicit and cannot invent offers");
            Check(doc.OverallStatus==SeedPredictionOverallStatus.CompletedWithWarnings &&
                doc.Sections.Any(s=>s.Kind==PredictionSectionKind.BossIdentity) &&
                doc.Sections.Any(s=>s.Kind==PredictionSectionKind.RelicSequences),
                "local Neow prerequisite does not erase requested independent domains");
            var rewards=doc.Sections.Single(s=>s.Kind==PredictionSectionKind.NormalCombatRewardSequence);
            Check(rewards.DomainStatus==expectedStatus && rewards.IssueCode=="NeowPrerequisite:"+neow.IssueCode &&
                rewards.NormalCombatRewardSequencePrediction!.Routes.Count==0,
                "dependent rewards retain Neow prerequisite instead of inventing continuation");
            // An unrelated S query does not request that unmodeled Neow mechanic.
            var independent=Compile(sw,changed);
            Check(ProductionExactSearchEvaluator.Evaluate(independent,TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,"000000000000"))
                .Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch,"unrelated S remains executable");
        }
        string output=".build-evidence/release-closure";Directory.CreateDirectory(output);
        var source=ProductionSearchReplay.Read<ProductionSearchReplayContext>(corpus+"v1/context.json");
        ProductionSearchReplay.Write(Path.Combine(output,"newer-context.json"),source with {Context=newer,Provenance="Metadata-only version simulation from current0.111;not future game evidence"});
        ProductionSearchReplay.Write(Path.Combine(output,"partial-context.json"),source with {Context=partial,Provenance="Synthetic narrowed current pools;not real partial-unlock capture"});
        ProductionSearchReplay.Write(Path.Combine(output,"partial-workload.json"),sw with {Options=sw.Options with {ScanCount=8193,TargetMatchCount=int.MaxValue,WorkerCount=1}});
        Console.WriteLine($"RELEASE_COMPATIBILITY PASS checks={checks};newVersion=MetadataSimulation;partialPool=Synthetic;cosmetic=MetadataOnly;futureMechanicsNotClaimed=true");
        return 0;
    }
}
