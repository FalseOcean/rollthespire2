using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Core.Prediction;
using System.Text.Json;
using RolltheSpire2.Tests.FastSearch;

internal static class ProductionReplayChecks
{
    internal static int Run()
    {
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, 0, 1)
            .WithWorldAuthority(RelicSequenceFixtureFactory.CreateModern());
        var world = authority.WorldAuthority!;
        var g = world.Beta109Generation! with { Profile = RuntimeProfileId.Beta111, WorldGenerationHooksExact = true,
            FixtureValidationStatus = Beta109FixtureValidationStatus.VerifiedRealGame,
            VerifiedRealGameFixtureIds = world.Beta109Generation!.AncientEventContexts.Select(c => "ancient-option:" + c.AncientKey.Entry.ToLowerInvariant() + ":synthetic").Concat(["ancient-identity:synthetic", "boss:synthetic"]).ToArray() };
        authority = authority.WithWorldAuthority(world with { CapturedProfileId = RuntimeProfileId.Beta111, Beta109Generation = g });
        authority = authority.WithEffectAuthority(authority.EffectAuthority! with { EventResultStaticCatalogExact = true,
            MerchantColorlessOrderedPoolExact = true, MerchantColorlessPoolIdentityHooksExact = true,
            MerchantColorlessRarityIdentityHooksExact = true, MerchantInitialInventoryShapeExact = true,
            EventColorfulCharacterPoolsExact = true, UnlockedCharacterCardPoolKeys = BaseGameModelKeys.Characters.All,
            CharacterRewardIdentityPoolExact = true,
            CharacterRewardPool = authority.EffectAuthority!.CharacterRewardPool!.Select(c => c with { CatalogProfileId = RuntimeProfileId.Beta111, EligibilityAuthority = "Synthetic fixture" }).ToArray() });
        var context = new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.111.0", "0.111.0", "beta", "SyntheticReplaySmoke", true, ""), SearchEvaluationAssumptions.BroadDefault);
        string dir = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "production-replay");
        ProductionSearchReplay.Write(Path.Combine(dir, "context.json"), new ProductionSearchReplayContext(1, "SyntheticFixture_NotRuntimeAuthority", context));
        var query = SearchQuery.Empty with { EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard, new("CARD", "CALTROPS"))] };
        ProductionSearchReplay.Write(Path.Combine(dir, "workload.json"), new ProductionSearchReplayWorkload(1, query, new("000000000000", 4096, int.MaxValue, 1)));
        var empty = NeowSearchFilter.Empty;
        ModelKeySetFilter Any(ModelKey key) => new([key], [], []);
        var ancient = g.AncientEventContexts.First(c => c.Act == 2 && c.AncientKey.Entry == "PAEL");
        var relic = RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, 0, "fixture", authority.WorldAuthority!, 10).Lanes.First(l => l.Kind == RelicSequenceKind.Common).Entries[0].RelicKey;
        var samples = new Dictionary<string, SearchQuery>
        {
            ["E"] = query,
            ["S"] = SearchQuery.Empty with { MerchantColorlessConditions = [new(1, MerchantColorlessSlot.Uncommon, new("CARD", "BENCH_COLORLESS_5"))] },
            ["N"] = LegacySearchQueryAdapter.FromFilter(empty with { NeowRelics = Any(BaseGameModelKeys.Relics.GoldenPearl) }),
            ["R"] = LegacySearchQueryAdapter.FromFilter(empty with { RelicSequenceConditions = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.FirstN, 1, Any(relic))] }),
            ["C"] = LegacySearchQueryAdapter.FromFilter(empty with { CombatPotionRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered, [new(CombatPotionSlotRequirement.NoDrop, null)]) }),
            ["W"] = LegacySearchQueryAdapter.FromFilter(empty with { BossFilters = [new(1, Any(g.OrderedActCatalog[0].Bosses[0]))] }),
            ["A"] = LegacySearchQueryAdapter.FromFilter(empty with { AncientBranchConditions = [new(2, ancient.AncientKey, [ancient.Catalog!.Pools.SelectMany(p => p.OrderedOptions).First()], [])] })
        };
        samples["EW"] = samples["W"] with { EventResultConditions = query.EventResultConditions };
        samples["NC"] = samples["N"] with { CombatPotionRewards = samples["C"].CombatPotionRewards };
        samples["Full"] = samples["N"] with { CombatPotionRewards = samples["C"].CombatPotionRewards,
            MerchantColorlessConditions = samples["S"].MerchantColorlessConditions,
            RelicSequenceConstraints = samples["R"].RelicSequenceConstraints,
            AncientBranches = samples["A"].AncientBranches, EventResultConditions = query.EventResultConditions };
        samples["NR"] = LegacySearchQueryAdapter.FromFilter(empty with {
            NeowRoute = new(BaseGameModelKeys.Relics.SmallCapsule),
            StructuredNeowEffects = [new(BaseGameModelKeys.Relics.SmallCapsule, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [relic])] });
        foreach (var sample in samples)
        {
            var workload = new ProductionSearchReplayWorkload(1, sample.Value, new("000000000000", 4096, int.MaxValue, 1));
            ProductionSearchReplay.Write(Path.Combine(dir, sample.Key + ".json"), workload);
            var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(sample.Value, context), workload.Options);
            if (!compiled.Success) throw new Exception("Smoke compile:" + sample.Key + ":" + compiled.Issue);
            string compact = ProductionSearchReplay.SerializeWorkload(compiled.Plan!);
            var logged = JsonSerializer.Deserialize<ProductionSearchReplayWorkload>(compact, ProductionSearchReplay.JsonOptions)!;
            if (compact.Contains('\n') || compact.Contains('\r') ||
                JsonSerializer.Serialize(logged.Query, ProductionSearchReplay.JsonOptions) != JsonSerializer.Serialize(sample.Value, ProductionSearchReplay.JsonOptions) ||
                logged.Options.StartSeed != compiled.Plan!.CanonicalStartSeed || logged.Options.ScanCount != compiled.Plan.ScanCount ||
                logged.Options.TargetMatchCount != workload.Options.TargetMatchCount || logged.Options.WorkerCount != workload.Options.WorkerCount)
                throw new Exception("Logged workload not replayable:" + sample.Key);
            using var expected = new StreamWriter(Path.Combine(dir, sample.Key + ".expected.jsonl"));
            for (ulong i = 0; i < 4096; i++)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(i);
                if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, Beta111Profile.Instance.ComputeRootSeed(seed), seed, out var input, out var issue)) throw new Exception(issue);
                var exact = ProductionExactSearchEvaluator.Evaluate(compiled.Plan!, input);
                if (exact.IsMatch) expected.WriteLine(JsonSerializer.Serialize(new { Seed = seed,
                    MatchEvidence = exact.Evaluation.Evidence, MatchedRouteIds = exact.Evaluation.MatchedRouteIds,
                    Witnesses = exact.Evaluation.Witnesses }, new JsonSerializerOptions(ProductionSearchReplay.JsonOptions) { WriteIndented = false }));
            }
        }
        Console.WriteLine("ProductionReplayRoundTrip=PASS;syntheticOnly=true;directory=" + dir);
        return 0;
    }
}
