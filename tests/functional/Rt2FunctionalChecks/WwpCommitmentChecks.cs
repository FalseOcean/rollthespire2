using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class WwpCommitmentChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool pass, string name) { checks++; if (!pass) throw new Exception(name); }
        var template = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/bones-k-sameroute-v1/counterexample.json");
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/n-direct-v1/context-defect-a10.json");
        var w = BaseGameModelKeys.OrdinaryRelics.Whetstone; var wp = BaseGameModelKeys.OrdinaryRelics.WarPaint;
        var k = BaseGameModelKeys.Relics.Kaleidoscope; var large = BaseGameModelKeys.Relics.LargeCapsule;
        string output = ".build-evidence/wwp-commitment"; Directory.CreateDirectory(output);
        ExactSearchExecutionRequest Compile(SearchQuery query, SearchContext ctx, string seed = "N9U2V9VPUSR7") =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, ctx), new(seed, 1, int.MaxValue, 1)).Plan
                ?? throw new Exception("WWP compile");
        SearchQuery Query(ModelKey[] relics, ModelKey[] cards) => template.Query with {
            StructuredOpeningEffects = (cards.Length == 0 ? Array.Empty<NeowStructuredEffectSearchCondition>() :
                new[] { template.Query.StructuredOpeningEffects[0] with { OutputKeys = cards } })
                .Append(template.Query.StructuredOpeningEffects[1] with { OutputKeys = relics }).ToArray() };
        foreach (string path in new[] { "v1/context.json", "n-direct-v1/context-defect-a10.json", "n-rewards-v1/context-necro-a0.json" })
        {
            var ctx = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/" + path).Context;
            var a = ctx.Authority.EffectAuthority!;
            var pool = a.OtherCharacterPools!.SelectMany(p => p.Cards).ToArray();
            ModelKey attack = pool.First(c => c.CanUpgrade && c.CardType == EffectCardType.Attack).CardKey;
            ModelKey skill = pool.First(c => c.CanUpgrade && c.CardType == EffectCardType.Skill).CardKey;
            int ac = a.OrderedDeck!.Count(c => c.CanUpgrade && c.CardType == EffectCardType.Attack);
            int sc = a.OrderedDeck!.Count(c => c.CanUpgrade && c.CardType == EffectCardType.Skill);
            foreach (ModelKey[] relics in new[] { new[] { w }, new[] { wp }, new[] { w, wp } })
            foreach (ModelKey[] picks in new[] { Array.Empty<ModelKey>(), new[] { attack }, new[] { attack, skill } })
            {
                var p = NeowReplayPlan.Compile(Compile(Query(relics, picks), ctx), false);
                int Expected(int da, int ds) => (relics.Contains(w) ? NeowAuthoredUpgradeContinuation.Draws(ac + da) : 0) +
                    (relics.Contains(wp) ? NeowAuthoredUpgradeContinuation.Draws(sc + ds) : 0);
                Check(p.AuthoredUpgrades!.Advance(3, 255) == Expected(0, 0), "Large first constant");
                Check(p.AuthoredUpgrades.Advance(3, 14) == Expected(picks.Length > 0 ? 1 : 0, picks.Length == 2 ? 1 : 0), "K first committed delta");
                Check(NeowFamilyGpuPlan.TryCreate(p, out var gpu, out _) && gpu!.HasAuthoredUpgrades &&
                    gpu.Conditions[160 + 33 + 14] == p.AuthoredUpgrades.Advance(3, 14), "GPU constant packing");
            }
        }
        Check(NeowAuthoredUpgradeContinuation.Draws(0) == 0 && NeowAuthoredUpgradeContinuation.Draws(1) == 0 &&
            NeowAuthoredUpgradeContinuation.Draws(2) == 1 && NeowAuthoredUpgradeContinuation.Draws(6) == 5, "shuffle edge arithmetic");
        foreach (ulong seed in new ulong[] { 0, 19, ulong.MaxValue })
        for (int count = 0; count <= 12; count++)
        {
            var reference = new RolltheSpire2.Core.Rng.Xoshiro256StarStar(seed);
            reference.UnstableShuffle(Enumerable.Range(0, count).ToList());
            var fast = new Beta110FastRng(seed);
            for (int i = 0; i < NeowAuthoredUpgradeContinuation.Draws(count); i++) fast.NextInt(2);
            var state = fast.CaptureCheckpoint();
            Check(reference.State == (state.S0, state.S1, state.S2, state.S3), "Full RNG state after count quotient");
        }
        var hidden = Compile(template.Query, context.Context);
        Check(NeowReplayPlan.Compile(hidden, false).AuthoredUpgrades is null, "Hidden War Paint stays out of scope");
        var one = Query([wp, new ModelKey("RELIC", "ANCHOR")], [new("CARD", "ANGER")]);
        var explicitPlan = Compile(one, context.Context);
        var root = Beta111Profile.Instance.ComputeRootSeed("N9U2V9VPUSR7");
        var replay = NeowReplayPlan.Compile(explicitPlan, false);
        var observation = NeowFamilyReplay.Observe(root, replay);
        Check(!NeowFamilyReplay.Route(root, replay with { AuthoredUpgrades = null }, 3, 14, observation.RewardsAfterBones), "Known old Fast route B rejects");
        Check(NeowFamilyReplay.Route(root, replay, 3, 14, observation.RewardsAfterBones), "Authored WP now advances before K");
        ProductionExactSearchResult Exact(ExactSearchExecutionRequest req, string seed)
        {
            ulong hash = Beta111Profile.Instance.ComputeRootSeed(seed);
            if (!TrustedRootHashInput.TryBindCanonicalSeed(Beta111Profile.Instance, hash, seed, out var input, out _)) throw new Exception("bind");
            return ProductionExactSearchEvaluator.Evaluate(req, input);
        }
        var exact = Exact(explicitPlan, "N9U2V9VPUSR7");
        Check(exact.IsMatch && exact.Evaluation.Witnesses.Count > 0, "Explicit WP Production Exact/Witness");
        Check(exact.Evaluation.Witnesses.All(x => !string.IsNullOrEmpty(x.ChoicePolicyId)), "Late optional pick has real committed trace in Witness");
        Check(!BonesCapsuleBoundaryGpu.HasExcludedMatchedPickup(exact, explicitPlan), "Authored W/WP mismatch cannot use old waiver");
        Check(BonesCapsuleBoundaryGpu.HasExcludedMatchedPickup(exact, hidden), "Only hidden W/WP retains diagnostic exclusion");
        ProductionSearchReplay.Write(Path.Combine(output, "authored-wp-positive.json"), template with { Query = one, Options = template.Options with { ScanCount = 67 } });
        var skip = new PlayerChoicePolicyDescriptor("skip", PlayerChoicePolicyKind.SkipOfferedCard, [], 0);
        Check(NeowChoiceCommitment.Matches([], k, skip, []), "Unauthored Skip");
        Check(!NeowChoiceCommitment.Matches(one.StructuredOpeningEffects, k, skip, []), "Authored cannot Skip");
        var takeExtra = new PlayerChoicePolicyDescriptor("extra", PlayerChoicePolicyKind.ChooseOfferedCardCombination,
            [new("CARD", "ANGER"), new("CARD", "STRIKE_IRONCLAD")], 2);
        Check(!NeowChoiceCommitment.Matches(one.StructuredOpeningEffects, k, takeExtra, []), "Unspecified extra take rejected");
        // Discover real K-before-Capsule traces, then author precisely their picked
        // cards and final curse; keep the original game deck/shuffle engine as oracle.
        var broad = Compile(Query([wp], []), context.Context);
        var identity = NeowReplayPlan.Compile(broad, false);
        int positives = 0;
        for (ulong ordinal = 0; ordinal < (1UL << 22) && positives < 3; ordinal++)
        {
            string seed = Beta110SeedCodec.FormatOrdinal(ordinal);
            ulong hash = Beta111Profile.Instance.ComputeRootSeed(seed);
            if (!NeowFamilyReplay.Observe(hash, identity).IdentityPass) continue;
            var result = Exact(broad, seed);
            var bones = result.Document?.Sections.SelectMany(s => s.NeowChoices).FirstOrDefault(c => c.BonesOutcome is not null)?.BonesOutcome;
            if (bones is null) continue;
            foreach (var route in bones.OriginalRoutes.Where(r => r.AcquisitionOrder.FirstOrDefault() == k))
            {
                var trace = route.PlayerChoiceSelections?.FirstOrDefault(t => t.ChoiceRelicKey == k);
                if (trace is null || trace.ChoicePolicy.SourceCardKeys.Count != positives || route.SharedContinuation.FinalCurseKey is not ModelKey curse) continue;
                if (!route.RelicScopedResults.Where(r => r.SourceRelicKey == large).SelectMany(r => r.EffectGroups)
                    .SelectMany(g => g.OrderedItems).Any(i => i.TargetKey == wp)) continue;
                var query = Query([wp], trace.ChoicePolicy.SourceCardKeys.ToArray());
                query = query with { OpeningRouteRelicRequirement = query.OpeningRouteRelicRequirement! with { OrderMode = BonesRouteOrderMode.ExactOrder },
                    LegacyNeow = query.LegacyNeow with { RequiredFinalCurse = curse } };
                // Existing canonical requirement already lists K,Large.
                var req = Compile(query, context.Context, seed);
                var truth = Exact(req, seed);
                Check(truth.IsMatch && truth.Evaluation.Witnesses.Count > 0, "Committed K-first Exact/curse " + positives);
                Check(NeowFamilyReplay.Matches(hash, NeowReplayPlan.Compile(req, false)), "Committed K-first Fast " + positives);
                ProductionSearchReplay.Write(Path.Combine(output, "k-first-" + positives + ".json"), new ProductionSearchReplayWorkload(1, query, req.RunOptions with { ScanCount = 67 }));
                positives++; break;
            }
        }
        Check(positives == 3, "Real zero/one/two-pick K-first witnesses found");
        foreach (bool kFirst in new[] { true, false })
        {
            var query = Query([w], []) with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                kFirst ? [k, large] : [large, k], BonesRouteOrderMode.ExactOrder) };
            var req = Compile(query, context.Context); var n = NeowReplayPlan.Compile(req, false);
            bool found = false;
            for (ulong ordinal = 0; ordinal < (1UL << 22) && !found; ordinal++)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(ordinal); ulong hash = Beta111Profile.Instance.ComputeRootSeed(seed);
                if (!NeowFamilyReplay.Observe(hash, n).IdentityPass) continue;
                var result = Exact(req, seed);
                if (!result.IsMatch) continue;
                var bones = result.Document!.Sections.SelectMany(s => s.NeowChoices).First(c => c.BonesOutcome is not null).BonesOutcome!;
                foreach (var route in bones.OriginalRoutes.Where(r => r.AcquisitionOrder[0] == (kFirst ? k : large)))
                {
                    if (route.PlayerChoiceSelections?.Any(t => t.ChoicePolicy.SourceCardKeys.Count != 0) == true) continue;
                    var relics = route.RelicScopedResults.Where(r => r.SourceRelicKey == large).SelectMany(r => r.EffectGroups).SelectMany(g => g.OrderedItems)
                        .Where(i => i.Kind == PredictedEffectKind.AddRelic).Select(i => i.TargetKey).ToArray();
                    if (!relics.Contains(w) || relics.Contains(wp) || route.SharedContinuation.FinalCurseKey is not ModelKey curse) continue;
                    var full = query with { LegacyNeow = query.LegacyNeow with { RequiredFinalCurse = curse } };
                    var fullReq = Compile(full, context.Context, seed); var truth = Exact(fullReq, seed);
                    Check(truth.IsMatch && truth.Evaluation.Witnesses.Count > 0 && NeowFamilyReplay.Matches(hash, NeowReplayPlan.Compile(fullReq, false)), "W only pinned chronology");
                    ProductionSearchReplay.Write(Path.Combine(output, "w-only-" + (kFirst ? "k-first" : "large-first") + ".json"), new ProductionSearchReplayWorkload(1, full, fullReq.RunOptions with { ScanCount = 67 }));
                    found = true; break;
                }
            }
            Check(found, "W only positive " + kFirst);
        }
        {
            const string seed = "000000078PC3"; // discovered in real Production, both W/WP in one Large
            var query = Query([w, wp], []);
            var req = Compile(query, context.Context, seed); var result = Exact(req, seed);
            Check(result.IsMatch, "Both W/WP legally reachable Production result");
            var route = result.Document!.Sections.SelectMany(s => s.NeowChoices).First(c => c.BonesOutcome is not null).BonesOutcome!.OriginalRoutes
                .First(r => r.AcquisitionOrder[0] == k && r.PlayerChoiceSelections!.All(t => t.ChoicePolicy.SourceCardKeys.Count == 0));
            var full = query with { LegacyNeow = query.LegacyNeow with { RequiredFinalCurse = route.SharedContinuation.FinalCurseKey } };
            var fullReq = Compile(full, context.Context, seed);
            Check(Exact(fullReq, seed).IsMatch && NeowFamilyReplay.Matches(Beta111Profile.Instance.ComputeRootSeed(seed), NeowReplayPlan.Compile(fullReq, false)), "Both W/WP exact curse chronology");
            ProductionSearchReplay.Write(Path.Combine(output, "both-final-curse.json"), new ProductionSearchReplayWorkload(1, full, fullReq.RunOptions with { ScanCount = 67 }));
        }
        foreach (var neighbor in new[] { BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.LeafyPoultice,
                     BaseGameModelKeys.Relics.ScrollBoxes, BaseGameModelKeys.Relics.LeadPaperweight, BaseGameModelKeys.Relics.NeowsTorment })
        {
            bool fury = neighbor == BaseGameModelKeys.Relics.NeowsTorment;
            ModelKey requiredUpgrade = fury ? w : wp;
            var query = Query([requiredUpgrade], []) with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [neighbor, large], BonesRouteOrderMode.ExactOrder) };
            var req = Compile(query, context.Context);
            var n = NeowReplayPlan.Compile(req, false);
            Beta110FastRelicCatalog.TryGetId(neighbor, out byte id);
            Check(n.AuthoredUpgrades!.Advance(3, id) == (fury ? 4 : NeowChoiceCommitment.IsOptionalCardOffer(neighbor) ? 5 : -1),
                "Neighbor empty commitment/missing mandatory result: " + neighbor);
            bool found = false;
            for (ulong ordinal = 0; ordinal < (1UL << 22) && !found; ordinal++)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(ordinal); ulong hash = Beta111Profile.Instance.ComputeRootSeed(seed);
                if (!NeowFamilyReplay.Observe(hash, n).IdentityPass) continue;
                var result = Exact(req, seed);
                var bones = result.Document?.Sections.SelectMany(s => s.NeowChoices).FirstOrDefault(c => c.BonesOutcome is not null)?.BonesOutcome;
                if (bones is null) continue;
                foreach (var route in bones.OriginalRoutes.Where(r => r.AcquisitionOrder.FirstOrDefault() == neighbor))
                {
                    if (!route.RelicScopedResults.Where(r => r.SourceRelicKey == large).SelectMany(r => r.EffectGroups)
                        .SelectMany(g => g.OrderedItems).Any(i => i.TargetKey == requiredUpgrade)) continue;
                    var trace = route.PlayerChoiceSelections?.FirstOrDefault(t => t.ChoiceRelicKey == neighbor);
                    ModelKey[] targets = trace is not null ? trace.ChoicePolicy.SourceCardKeys.ToArray() :
                        route.RelicScopedResults.Where(r => r.SourceRelicKey == neighbor).SelectMany(r => r.EffectGroups)
                            .SelectMany(g => g.OrderedItems).Where(i => i.TargetKey.HasValue && i.Kind is PredictedEffectKind.AddCard or PredictedEffectKind.TransformCard)
                            .Select(i => i.TargetKey!.Value).ToArray();
                    int expectedCount = neighbor == BaseGameModelKeys.Relics.LeafyPoultice ? 2 : neighbor == BaseGameModelKeys.Relics.ScrollBoxes ? 3 : 1;
                    if (targets.Length != expectedCount || route.SharedContinuation.FinalCurseKey is not ModelKey curse) continue;
                    var condition = new NeowStructuredEffectSearchCondition(neighbor,
                        expectedCount == 1 ? NeowStructuredConditionKind.ExactSingle : expectedCount == 2 ? NeowStructuredConditionKind.ExactUnorderedPair : NeowStructuredConditionKind.StructuredCardComposition,
                        neighbor == BaseGameModelKeys.Relics.LeafyPoultice ? NeowStructuredEffectScope.TransformResults :
                        neighbor == BaseGameModelKeys.Relics.ArcaneScroll || fury ? NeowStructuredEffectScope.ProductRelevantEffects : NeowStructuredEffectScope.SelectableOfferGroups,
                        NeowStructuredOutputKind.Card, targets);
                    var actualUpgrades = route.RelicScopedResults.Where(r => r.SourceRelicKey == large).SelectMany(r => r.EffectGroups)
                        .SelectMany(g => g.OrderedItems).Where(i => i.Kind == PredictedEffectKind.AddRelic && (i.TargetKey == w || i.TargetKey == wp))
                        .Select(i => i.TargetKey!.Value).Distinct().ToArray();
                    var full = query with { StructuredOpeningEffects = fury
                            ? [query.StructuredOpeningEffects[0] with { OutputKeys = actualUpgrades }]
                            : [condition, query.StructuredOpeningEffects[0] with { OutputKeys = actualUpgrades }],
                        LegacyNeow = query.LegacyNeow with { RequiredFinalCurse = curse } };
                    var fullReq = Compile(full, context.Context, seed);
                    var fullN = NeowReplayPlan.Compile(fullReq, false);
                    Check(fullN.AuthoredUpgrades!.Advance(3, id) >= 0, "Neighbor closed constant " + neighbor);
                    var truth = Exact(fullReq, seed);
                    Check(truth.IsMatch && truth.Evaluation.Witnesses.Count > 0, "Neighbor Exact/Witness " + neighbor + ":" + truth.FailureCode + ":" + truth.Evaluation.FailureCode);
                    Check(NeowFamilyReplay.Matches(hash, fullN), "Neighbor Fast " + neighbor + ":" + seed + ":" + string.Join(',', targets) + ":advance=" + fullN.AuthoredUpgrades.Advance(3,id) + ":relics=" + string.Join(',',actualUpgrades));
                    ProductionSearchReplay.Write(Path.Combine(output, "neighbor-" + neighbor.Entry + ".json"), new ProductionSearchReplayWorkload(1, full, fullReq.RunOptions with { ScanCount = 67 }));
                    found = true; break;
                }
            }
            Check(found, "Neighbor positive found " + neighbor);
        }
        foreach (string path in new[] { "v1/context.json", "n-rewards-v1/context-necro-a0.json" })
        {
            var ctx = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/" + path).Context;
            var query = Query([wp], []) with { OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones, [k, large], BonesRouteOrderMode.ExactOrder) };
            var req = Compile(query, ctx); var n = NeowReplayPlan.Compile(req, false); bool found = false;
            for (ulong ordinal = 0; ordinal < (1UL << 22) && !found; ordinal++)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(ordinal); ulong hash = Beta111Profile.Instance.ComputeRootSeed(seed);
                if (!NeowFamilyReplay.Observe(hash, n).IdentityPass) continue;
                var result = Exact(req, seed); if (!result.IsMatch) continue;
                var bones = result.Document!.Sections.SelectMany(s => s.NeowChoices).First(c => c.BonesOutcome is not null).BonesOutcome!;
                foreach (var route in bones.OriginalRoutes.Where(r => r.AcquisitionOrder[0] == k &&
                             r.PlayerChoiceSelections?.All(t => t.ChoicePolicy.SourceCardKeys.Count == 0) == true))
                {
                    var relics = route.RelicScopedResults.Where(r => r.SourceRelicKey == large).SelectMany(r => r.EffectGroups).SelectMany(g => g.OrderedItems)
                        .Where(i => i.Kind == PredictedEffectKind.AddRelic).Select(i => i.TargetKey).ToArray();
                    if (!relics.Contains(wp) || relics.Contains(w) || route.SharedContinuation.FinalCurseKey is not ModelKey curse) continue;
                    var full = query with { LegacyNeow = query.LegacyNeow with { RequiredFinalCurse = curse } };
                    var fullReq = Compile(full, ctx, seed); var truth = Exact(fullReq, seed);
                    Check(truth.IsMatch && truth.Evaluation.Witnesses.Count > 0 && NeowFamilyReplay.Matches(hash, NeowReplayPlan.Compile(fullReq, false)), "Character chronology " + ctx.CharacterKey);
                    ProductionSearchReplay.Write(Path.Combine(output, "context-" + ctx.CharacterKey.Entry + ".json"), new ProductionSearchReplayWorkload(1, full, fullReq.RunOptions with { ScanCount = 67 }));
                    found = true; break;
                }
            }
            Check(found, "Character positive " + ctx.CharacterKey);
        }
        Console.WriteLine($"WwpCommitment checks={checks};positiveKFirst={positives};PASS");
        return 0;
    }
}
