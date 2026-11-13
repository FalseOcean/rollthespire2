using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class NeowOfferRelationChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new InvalidDataException("NeowOffers." + reason); }
        const string corpus = "tools/ProductionSearchHost/Corpus/";
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(corpus + "owner-nwae-v1/context.json").Context;
        var options = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(corpus + "physical-composition-v1/Arcane27.json").Options
            with { StartSeed = "000000000000", ScanCount = 128, TargetMatchCount = 1, WorkerCount = 1 };
        var pearl = BaseGameModelKeys.Relics.GoldenPearl;
        var arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        var curse = BaseGameModelKeys.Relics.CursedPearl;
        var universe = ModernNeowIdentityRules.PositiveCandidates.Concat(ModernNeowIdentityRules.CurseCandidates)
            .Concat(ModernNeowIdentityRules.BinaryChoices.SelectMany(c => new[] { c.TrueCandidate, c.FalseCandidate })).Distinct().ToArray();
        var allowed = universe.Where(k => k != pearl).ToArray();
        SearchQuery Set(ModelKeySetFilter keys) => SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = keys } };
        var whitelist = SearchQuery.Empty with { NeowOfferWhitelist = new(allowed) };
        var queries = new[] { Set(new([pearl, arcane], [], [])), Set(new([], [pearl, arcane], [])),
            Set(new([], [], [curse])), whitelist };
        PreparedSearch Prepare(SearchQuery q) => SearchPreparationCompiler.Compile(SearchCompiler.Compile(q, context), options).Plan
            ?? throw new InvalidDataException("NeowOffers.Prepare");
        var baseline = Prepare(Set(new([], [], [pearl])));
        var whitePlan = Prepare(whitelist);
        var whiteReplay = NeowReplayPlan.Compile(whitePlan.Model, false);
        var blackReplay = NeowReplayPlan.Compile(baseline.Model, false);
        Check(whiteReplay.TopBan == blackReplay.TopBan && whiteReplay.ExactOnly.Length == 0, "WhitelistCompilesToExistingBanMask");
        Check(NeowFamilyGpuPlan.TryCreate(whiteReplay, out var whiteGpu, out _) &&
            NeowFamilyGpuPlan.TryCreate(blackReplay, out var blackGpu, out _) &&
            whiteGpu!.Meta.SequenceEqual(blackGpu!.Meta) && whiteGpu.Conditions.SequenceEqual(blackGpu.Conditions),
            "EquivalentWhitelistAndBanUseSameGpuParameters");
        string json = JsonSerializer.Serialize(whitelist, ProductionSearchReplay.JsonOptions);
        var restored = JsonSerializer.Deserialize<SearchQuery>(json, ProductionSearchReplay.JsonOptions)!;
        Check(restored.NeowOfferWhitelist!.AllowedKeys.SequenceEqual(allowed) && restored.LegacyNeow.NeowRelics.IsEmpty,
            "PersistenceRetainsAllowedSetRatherThanComplement");
        var emptyWhitelist = SearchQuery.Empty with { NeowOfferWhitelist = new([]) };
        var emptyRestored = JsonSerializer.Deserialize<SearchQuery>(
            JsonSerializer.Serialize(emptyWhitelist, ProductionSearchReplay.JsonOptions), ProductionSearchReplay.JsonOptions)!;
        Check(emptyRestored.NeowOfferWhitelist is { AllowedKeys.Count: 0 } &&
            !NeowOfferWhitelist.Matches(emptyRestored.NeowOfferWhitelist, [pearl, arcane, curse]),
            "EmptyWhitelistRemainsContradictionAfterPersistence");
        Check(SearchCompiler.Compile(whitelist, context).SemanticFingerprint == SearchCompiler.Compile(whitelist with {
            NeowOfferWhitelist = new(allowed.AsEnumerable().Reverse().Concat(allowed).ToArray()) }, context).SemanticFingerprint,
            "AllowedSetCanonicalFingerprint");
        var extra = new ModelKey("RELIC", "AUTHOR_ALLOWED_FUTURE_CONTENT");
        Check(SearchCompiler.Compile(whitelist with { NeowOfferWhitelist = new(allowed.Append(extra).ToArray()) }, context)
            .NormalizedQuery.NeowOfferWhitelist!.AllowedKeys.Contains(extra), "CatalogDoesNotEraseAuthoredIdentity");
        Check(!NeowOfferWhitelist.Matches(new([pearl, arcane]), [pearl, arcane, curse]) &&
            QueryKeySetPredicate.MatchesKeySet([pearl, arcane, curse], new([], [pearl, arcane], [])),
            "AllAndOnlyAllowedAreDifferentRelations");
        foreach (var q in queries)
        {
            var plan = Prepare(q);
            var replay = NeowReplayPlan.Compile(plan.Model, false);
            var probability = SearchSelectivityEstimator.EstimateStage(SearchSelectivityInput.From(plan.Model), SearchSelectivityDomain.Neow);
            bool Accept(IReadOnlyList<ModelKey> offers) => QueryKeySetPredicate.MatchesKeySet(offers, q.LegacyNeow.NeowRelics) &&
                NeowOfferWhitelist.Matches(q.NeowOfferWhitelist, offers);
            ModernNeowIdentityRules.TryEstimateOfferProbability(context.Authority, (a, b, c) => Accept([a, b, c]), out double expected, out _);
            Check(probability.Probability is { } actual && Math.Abs(actual - expected) < 1e-12, "FiniteJointProbability");
            for (ulong ordinal = 0; ordinal < 128; ordinal++)
            {
                string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed);
                var offers = ModernNeowIdentityPredictor.PredictModernCore(input.RootHash, context.Authority, Beta111Profile.Instance, true).RelicKeys;
                bool wanted = Accept(offers);
                Check(NeowFamilyReplay.Matches(input.RootHash, replay) == wanted, "CpuCompleteMenu");
                if (ordinal < 16)
                    Check(ProductionExactSearchEvaluator.Evaluate(plan.Model, input).Evaluation.Disposition ==
                        (wanted ? SearchDisposition.Match : SearchDisposition.NoMatch), "ProductionExactCompleteMenu");
            }
        }
        // Slot-dependent streams and eligibility use one complete menu each.
        foreach (int players in new[] { 2, 4 })
        foreach (int slot in Enumerable.Range(0, players))
        {
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 0, slot, players);
            Check(NeowOfferWhitelist.TryProject(new(allowed), authority, ModelKeySetFilter.Empty, out var projection), "PartyWhitelistProjection");
            for (ulong root = 0; root < 128; root++)
            {
                var offers = ModernNeowIdentityPredictor.PredictModernCore(root, authority, Beta111Profile.Instance, true).RelicKeys;
                Check(QueryKeySetPredicate.MatchesKeySet(offers, projection) == offers.All(allowed.Contains), "PartyFullMenuMembership");
            }
        }
        Console.WriteLine($"NEOW_OFFER_RELATIONS=PASS;checks={checks};relations=Any,All,OnlyAllowed,Ban;cpuExact=true;gpuPacking=true;nativeGpuNotRun=true");
        return 0;
    }
}
