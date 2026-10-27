using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Search.Runtime;

internal static class NeowIdentityCpuChecks
{
    internal static int Run()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var w = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/classic-product-v1/B-SourceCapsules-positive.json");
        var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query, context.Context), w.Options).Plan!;
        var plan = NeowReplayPlan.Compile(request, false);
        var pool = plan.Authority.BonesEligibleRelicIds;
        int checks = CheckEmptyExclusionSelection() + CheckDirectLocalPlans() + CheckMinimalObservationPlans() + CheckRoundedRarityEndpoint(), kept = 0;
        // All possible pair identities: this is a compiled pair shape, not a
        // Small/Large identity special case. Variants also confront Any/Ban/order.
        var random = new Random(20260912);
        for (int a = 0; a < pool.Length; a++)
        for (int b = a + 1; b < pool.Length; b++)
        {
            var p = plan with { BonesAll = Beta110FastRelicCatalog.Bit(pool[a]) | Beta110FastRelicCatalog.Bit(pool[b]),
                BonesAny = a % 3 == 0 ? Beta110FastRelicCatalog.Bit(pool[a]) : 0,
                BonesBan = a % 5 == 0 ? Beta110FastRelicCatalog.Bit(pool[(b + 1) % pool.Length]) : 0,
                First = a % 2 == 0 ? pool[a] : Beta110FastRelicCatalog.InvalidId,
                Second = a % 2 == 0 ? pool[b] : Beta110FastRelicCatalog.InvalidId };
            var fast = NeowIdentityCpuPlan.TryCompile(p) ?? throw new InvalidDataException("pair not admitted");
            for (int i = 0; i < 4096; i++)
            {
                ulong root = (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 32);
                bool expected = NeowFamilyReplay.Matches(root, p), actual = fast.Matches(root);
                if (expected != actual) throw new InvalidDataException($"N identity mismatch {a}/{b}/{root}");
                checks++; if (actual) kept++;
            }
        }
        var numerical = NeowIdentityCpuPlan.TryCompile(plan)!;
        ulong space = VisibleSeedCandidateCodec.SpaceSize(Beta111Profile.Instance);
        foreach (ulong start in new[] { 0UL, space - 8193 })
        for (ulong i = 0; i < 8193; i++)
        {
            ulong root = Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, start + i));
            if (numerical.Matches(root) != NeowFamilyReplay.Matches(root, plan)) throw new InvalidDataException("N tail mismatch");
            checks++;
        }
        ulong positive = Beta111Profile.Instance.ComputeRootSeed(w.Options.StartSeed);
        if (!numerical.Matches(positive) || !NeowFamilyReplay.Matches(positive, plan)) throw new InvalidDataException("positive lost");
        if (NeowIdentityCpuPlan.TryCompile(plan with { TopBan = 1 }) is not null) throw new InvalidDataException("unsupported top filter admitted");
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (ulong i = 0; i < 65536; i++) if (numerical.Matches(i)) kept++;
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        checks += RelicCpuChecks.CheckEnvelope(request, numerical.Matches, root => NeowFamilyReplay.Matches(root, plan));
        var parent = plan with { BonesAny=0, BonesAll=0, BonesBan=0,
            First=Beta110FastRelicCatalog.InvalidId, Second=Beta110FastRelicCatalog.InvalidId };
        var parentGate=NeowCpuPreGate.TryCompile(parent) ?? throw new InvalidDataException("N parent gate missing");
        if(!parentGate.IdentityOnly)throw new InvalidDataException("N pure parent should not replay unobserved pair");
        for(ulong root=0;root<65536;root++)
        { if(parentGate.Matches(root)!=NeowFamilyReplay.Matches(root,parent))throw new InvalidDataException("N parent-only mismatch");checks++; }
        foreach(var item in new[]{
            ("family-cost-closure-v1/N-curse.json","v1/context.json"),
            ("family-cost-closure-v1/N-large.json","v1/context.json"),
            ("family-cost-closure-v1/NN-Leafy.json","owner-nwae-v1/context.json"),
            ("bones-workshape-v1/KP-B1.json","owner-nwae-v1/context.json"),
            ("bones-workshape-v1/Leafy-Arcane.json","owner-nwae-v1/context.json")})
        {
            var ctx=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/"+item.Item2);
            var workload=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/"+item.Item1);
            var r=ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query,ctx.Context),workload.Options).Plan!;
            var p=NeowReplayPlan.Compile(r,false);
            var gate=NeowCpuPreGate.TryCompile(p) ?? throw new InvalidDataException("N gate not admitted:"+item.Item1);
            if (item.Item1.EndsWith("NN-Leafy.json", StringComparison.Ordinal))
            {
                if (!gate.DirectIdentityComplete || gate.IdentityOnly) throw new InvalidDataException("N direct local identity proof missing");
                ulong positiveTop = Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.GoldenPearl);
                foreach (var restricted in new[] { p with { TopAny = positiveTop }, p with { TopAll = positiveTop }, p with { TopBan = positiveTop } })
                {
                    var restrictedGate = NeowCpuPreGate.TryCompile(restricted) ?? throw new InvalidDataException("N positive-top necessary gate missing");
                    if (restrictedGate.DirectIdentityComplete || restrictedGate.IdentityOnly) throw new InvalidDataException("N positive top cannot be decided by curse");
                    int necessaryOnly = 0;
                    for (ulong root = 0; root < 4096; root++)
                    {
                        if (restrictedGate.PassesIdentity(root) && !NeowFamilyReplay.Observe(root, restricted).IdentityPass) necessaryOnly++;
                        if (restrictedGate.Matches(root) != NeowFamilyReplay.Matches(root, restricted)) throw new InvalidDataException("N positive-top full replay parity");
                        checks++;
                    }
                    if (necessaryOnly == 0) throw new InvalidDataException("N positive-top counterexample missing");
                }
                foreach (var remainder in new[] { p with { ExactOnly = ["N3.ExactRemainderFixture"] },
                    p with { EnabledDomains = p.EnabledDomains | Beta110FastDomain.FinalCurse } })
                {
                    var remainderGate = NeowCpuPreGate.TryCompile(remainder) ?? throw new InvalidDataException("N remainder necessary gate missing");
                    if (remainderGate.DirectIdentityComplete) throw new InvalidDataException("N direct shortcut bypassed Exact remainder or FinalCurse");
                    checks++;
                }
                // Keep a real local continuation fault after the proven direct
                // identity; skipping unobserved offers must not hide this cause.
                var broken = p with { Authority = p.Authority with {
                    EffectCatalog = p.Authority.EffectCatalog with { LeafyTransformAuthorityExact = false } } };
                var brokenGate = NeowCpuPreGate.TryCompile(broken) ?? throw new InvalidDataException("N broken local necessary gate missing");
                ulong faultRoot = 0;
                while (faultRoot < 4096 && !brokenGate.PassesIdentity(faultRoot)) faultRoot++;
                if (faultRoot == 4096) throw new InvalidDataException("N direct fault witness missing");
                string Failure(Func<bool> run)
                {
                    try { _ = run(); }
                    catch (InvalidOperationException ex) { return ex.Message; }
                    throw new InvalidDataException("N local continuation fault was hidden");
                }
                if (!brokenGate.DirectIdentityComplete || Failure(() => brokenGate.Matches(faultRoot)) !=
                    Failure(() => NeowFamilyReplay.Matches(faultRoot, broken))) throw new InvalidDataException("N direct local fault cause changed");
                checks++;
            }
            int identityWitnesses=0;
            for(ulong root=0;root<65536;root++)
            {
                bool identity=NeowFamilyReplay.Observe(root,p).IdentityPass;
                if(identity){identityWitnesses++;if(!gate.PassesIdentity(root))throw new InvalidDataException("N necessary identity gate false negative");}
                if(gate.Matches(root)!=NeowFamilyReplay.Matches(root,p))throw new InvalidDataException("N local gate population mismatch");
                checks+=2;
            }
            if(identityWitnesses==0)throw new InvalidDataException("N gate missing positive identity coverage");
            checks+=RelicCpuChecks.CheckEnvelope(r,gate.Matches,root=>NeowFamilyReplay.Matches(root,p));
        }
        Console.WriteLine($"N_IDENTITY_CPU PASS checks={checks + 2};kept={kept};allocatedBytes={bytes};allPairs=true;tail=true;positive=true");
        return 0;
    }

    private static int CheckRoundedRarityEndpoint()
    {
        int checks = 0;
        foreach (var policy in new[] { CardBaseOddsPolicy.LegacyAndBeta109NonCumulative, CardBaseOddsPolicy.Beta110Cumulative })
        foreach (var type in Enum.GetValues<CardBaseOddsType>())
        foreach (int ascension in new[] { 0, 10 })
        {
            foreach (float roll in new[] { MathF.BitDecrement(1f), 1f })
            {
                if (CardBaseOddsPolicyEvaluator.Evaluate(roll, type, ascension, policy) != EffectCardRarity.Common)
                    throw new InvalidDataException("Rounded rarity endpoint must retain the game's Common result");
                checks++;
            }
        }
        foreach (float invalid in new[] { -float.Epsilon, MathF.BitIncrement(1f), float.NegativeInfinity, float.PositiveInfinity })
        {
            bool rejected = false;
            try { _ = CardBaseOddsPolicyEvaluator.Evaluate(invalid, CardBaseOddsType.Regular, 10, CardBaseOddsPolicy.Beta110Cumulative); }
            catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "roll") { rejected = true; }
            if (!rejected) throw new InvalidDataException("Rarity endpoint repair admitted an out-of-domain roll");
            checks++;
        }
        const string seed = "0000001860KC";
        var profile = Beta111Profile.Instance;
        ulong root = profile.ComputeRootSeed(seed);
        if (root != 0xa48b7b3e805f8c17UL) throw new InvalidDataException("LostCoffer rounded-endpoint root identity");
        var rng = new Xoshiro256StarStar(profile.DeriveNamedStreamSeed(root, "rewards"));
        rng.Advance(3); // First card: rarity, card selection, upgrade; next is card two's rarity.
        var raw = rng.Clone();
        if (raw.NextUInt64() != 0xffffffbb9a13cc99UL || rng.Clone().NextFloat() != 1f)
            throw new InvalidDataException("LostCoffer real-seed rounding witness changed");
        if (CardBaseOddsPolicyEvaluator.Roll(rng, CardBaseOddsType.Regular, 10, CardBaseOddsPolicy.Beta110Cumulative) != EffectCardRarity.Common ||
            rng.CallCount != 4 || rng.State != raw.State)
            throw new InvalidDataException("LostCoffer endpoint must consume exactly the original one draw");
        checks += 3;

        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/owner-nwae-v1/context.json").Context;
        var afterlife = new ModelKey("CARD", "AFTERLIFE");
        var projection = Beta111NeowEffectProjector.ProjectFromRootHash(BaseGameModelKeys.Relics.LostCoffer,
            PredictionPrecision.Exact, context.Authority, root);
        if (projection.Precision != PredictionPrecision.Exact || !projection.EffectGroups.SelectMany(g => g.OrderedItems)
            .Any(item => item.Kind == PredictedEffectKind.AddCard && item.TargetKey == afterlife))
            throw new InvalidDataException("LostCoffer real-seed rich projection lost AFTERLIFE");
        var query = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.LostCoffer), StructuredOpeningEffects =
            [new(BaseGameModelKeys.Relics.LostCoffer, NeowStructuredConditionKind.ExactSingle,
                NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [afterlife])] };
        var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, 1, 1, 1));
        var request = compiled.Plan ?? throw new InvalidDataException("LostCoffer endpoint query failed compilation: " + compiled.Issue);
        var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(profile, seed));
        if (!exact.IsMatch) throw new InvalidDataException("LostCoffer real-seed Production Exact failed: " + exact.FailureCode);
        checks += 2;
        Console.WriteLine($"N_ROUNDED_RARITY_ENDPOINT PASS checks={checks};seed={seed};rarityDraw=4;common=true;productionExact=true");
        return checks;
    }

    private static int CheckMinimalObservationPlans()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/owner-nwae-v1/context.json");
        int checks = 0;
        Span<ushort> originalOffers = stackalloc ushort[6];
        Span<ushort> splitOffers = stackalloc ushort[6];
        foreach (string path in new[] { "n-rewards-v1/Lost-potion-rare.json", "n-rewards-v1/Lost-potion-common.json",
            "n-direct-v1/K-same-pool.json", "n-direct-v1/K-cross-pool.json", "n-direct-v1/K-repeated.json" })
        {
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path);
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context), workload.Options).Plan!;
            var p = NeowReplayPlan.Compile(request, false);
            var fast = NeowSingleplayerLocalCpuPlan.TryCompile(p) ?? throw new InvalidDataException("N minimal observation not admitted: " + path);
            var c = p.Authority.EffectCatalog;
            string expectedRevision = p.Selected == Beta110FastRelicCatalog.LostCoffer
                ? "N.Neow.Cpu.LostPotionLocalFirst.20261015.v1" : "N.Neow.Cpu.DirectMinimalObservation.20261015.v1";
            if (fast.Revision != expectedRevision) throw new InvalidDataException("N Lost/K revision scope changed");
            checks++;
            foreach (ulong edge in new[] { ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63 })
            {
                if (fast.Matches(edge) != NeowSingleplayerReplay.Matches(edge, p)) throw new InvalidDataException("N minimal observation overflow: " + path);
                checks++;
            }
            for (ulong root = 0; root < 65536; root++)
            {
                if (fast.Matches(root) != NeowSingleplayerReplay.Matches(root, p)) throw new InvalidDataException("N minimal observation population: " + path);
                checks++;
                if (root >= 4096) continue;
                var original = new Beta110FastRng(root);
                var minimal = new Beta110FastRng(root);
                if (p.Selected == Beta110FastRelicCatalog.LostCoffer)
                {
                    if (!NeowLocalOperators.GenerateNormalCardOffer(c.CharacterRewardPool, p.Authority.Ascension, ref original, originalOffers[..3]))
                        throw new InvalidDataException("N minimal card-burn fixture must always generate");
                    NeowSingleplayerLocalCpuPlan.BurnLostCofferCards(ref minimal);
                    if (original.CaptureCheckpoint() != minimal.CaptureCheckpoint()) throw new InvalidDataException("N Lost three-card burn checkpoint");
                    ushort potion = NeowLocalOperators.RollPotion(c.PotionPool, [], ref original);
                    ushort burnedPotion = NeowLocalOperators.RollPotion(c.PotionPool, [], ref minimal);
                    if (potion != burnedPotion || original.CaptureCheckpoint() != minimal.CaptureCheckpoint()) throw new InvalidDataException("N Lost potion continuation checkpoint");
                }
                else
                {
                    var originalNiche = new Beta110FastRng(root + 17);
                    var splitNiche = new Beta110FastRng(root + 17);
                    if (!NeowLocalOperators.GenerateKaleidoscopeOffers(p.Authority.Ascension, c, ref original, ref originalNiche, originalOffers) ||
                        !NeowLocalOperators.GenerateKaleidoscopeOffers(p.Authority.Ascension, c, ref minimal, ref splitNiche, splitOffers[..3], groups: 1) ||
                        !NeowLocalOperators.GenerateKaleidoscopeOffers(p.Authority.Ascension, c, ref minimal, ref splitNiche, splitOffers[3..], groups: 1))
                        throw new InvalidDataException("N K group fixture must always generate");
                    if (!originalOffers.SequenceEqual(splitOffers) || original.CaptureCheckpoint() != minimal.CaptureCheckpoint() ||
                        originalNiche.CaptureCheckpoint() != splitNiche.CaptureCheckpoint()) throw new InvalidDataException("N K two single-groups changed outputs/checkpoints");
                }
                checks++;
            }
            foreach (var excluded in new[] { p with { ExactOnly = ["MinimalObservation.RemainderFixture"] },
                p with { Bones = true }, p with { RequireBones = true },
                p with { EnabledDomains = p.EnabledDomains | Beta110FastDomain.FinalCurse },
                p with { Authority = p.Authority with { PlayersCount = 2 } },
                p with { Authority = p.Authority with { IdentityAuthorityExact = false } },
                p with { Authority = p.Authority with { EligibleCurseRelicIds = [] } },
                p with { Authority = p.Authority with { AllCharacterCardPoolsUnlocked = false } } })
            {
                if (NeowSingleplayerLocalCpuPlan.TryCompile(excluded) is not null) throw new InvalidDataException("N minimal observation widened route scope");
                checks++;
            }
            var empty = c.CharacterRewardPool with { Common = [], Uncommon = [], Rare = [] };
            var brokenCatalog = p.Selected == Beta110FastRelicCatalog.LostCoffer
                ? c with { CharacterRewardPool = empty }
                : c with { OtherCharacterPools = c.OtherCharacterPools.Select(pool => empty).ToArray() };
            var broken = p with { Authority = p.Authority with { EffectCatalog = brokenCatalog } };
            if (NeowSingleplayerLocalCpuPlan.TryCompile(broken) is not null) throw new InvalidDataException("N minimal observation hid a downstream authority fault");
            ulong faultRoot = 0;
            while (faultRoot < 4096 && !NeowSingleplayerReplay.Observe(faultRoot, broken).IdentityPass) faultRoot++;
            if (faultRoot == 4096) throw new InvalidDataException("N minimal observation fault witness missing");
            try { _ = NeowSingleplayerReplay.Matches(faultRoot, broken); throw new InvalidDataException("N original continuation fault disappeared"); }
            catch (InvalidOperationException ex) when (ex.Message == "NFamilyContinuationAuthorityUnavailable") { checks++; }
            if (p.Selected == Beta110FastRelicCatalog.LostCoffer)
            {
                var exhausted = p with { Authority = p.Authority with { EffectCatalog = c with {
                    CharacterRewardPool = c.CharacterRewardPool with { Common = [1, 1, 1] } } } };
                if (NeowSingleplayerLocalCpuPlan.TryCompile(exhausted) is not null) throw new InvalidDataException("N Lost length-only proof ignored duplicate exhaustion");
                checks++;
                foreach (var potionPool in new[] { c.PotionPool with { Common = [] }, c.PotionPool with { Uncommon = [] },
                    c.PotionPool with { Rare = [] }, c.PotionPool with { Common = [Beta110FastDenseId.Invalid] },
                    c.PotionPool with { Uncommon = [Beta110FastDenseId.Invalid] }, c.PotionPool with { Rare = [Beta110FastDenseId.Invalid] } })
                {
                    var missingProof = p with { Authority = p.Authority with { EffectCatalog = c with { PotionPool = potionPool } } };
                    if (NeowSingleplayerLocalCpuPlan.TryCompile(missingProof) is not null)
                        throw new InvalidDataException("N Lost local-first admitted an empty/invalid potion rarity");
                    checks++;
                }
                foreach (var cardPool in new[] { c.CharacterRewardPool with { Uncommon = [1, 2, 2] },
                    c.CharacterRewardPool with { Rare = [1, 2, Beta110FastDenseId.Invalid] } })
                {
                    var missingProof = p with { Authority = p.Authority with { EffectCatalog = c with { CharacterRewardPool = cardPool } } };
                    if (NeowSingleplayerLocalCpuPlan.TryCompile(missingProof) is not null)
                        throw new InvalidDataException("N Lost local-first admitted exhausted/invalid card rarity");
                    checks++;
                }
            }
            checks += RelicCpuChecks.CheckEnvelope(request, fast.Matches, root => NeowSingleplayerReplay.Matches(root, p));
        }
        Console.WriteLine($"N_MINIMAL_OBSERVATION PASS checks={checks};soloOnly=true;cardBurnCheckpoint=true;crossGroupAndRepeatedTargets=true");
        return checks;
    }

    private static int CheckDirectLocalPlans()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/owner-nwae-v1/context.json");
        int checks = 0;
        foreach (string path in new[] { "physical-composition-v1/Arcane27.json", "n-direct-v1/NewLeaf.json" })
        {
            var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path);
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context), workload.Options).Plan!;
            var p = NeowReplayPlan.Compile(request, false);
            var fast = NeowSingleplayerLocalCpuPlan.TryCompile(p) ?? throw new InvalidDataException("N direct local plan not admitted: " + path);
            for (ulong root = 0; root < 65536; root++)
            {
                if (fast.Matches(root) != NeowSingleplayerReplay.Matches(root, p)) throw new InvalidDataException("N direct local-first parity: " + path);
                checks++;
            }
            foreach (ulong root in new[] { ulong.MaxValue, ulong.MaxValue - 1, 1UL << 63 })
            {
                if (fast.Matches(root) != NeowSingleplayerReplay.Matches(root, p)) throw new InvalidDataException("N direct local-first overflow: " + path);
                checks++;
            }
            foreach (var excluded in new[] { p with { ExactOnly = ["DirectLocal.RemainderFixture"] },
                p with { EnabledDomains = p.EnabledDomains | Beta110FastDomain.FinalCurse },
                p with { Authority = p.Authority with { PlayersCount = 2 } },
                p with { Bones = true }, p with { RequireBones = true },
                p with { StructuredConditions = [..p.StructuredConditions, ..p.StructuredConditions] } })
            {
                if (NeowSingleplayerLocalCpuPlan.TryCompile(excluded) is not null) throw new InvalidDataException("N direct local-first widened scope");
                checks++;
            }
            var catalog = p.Authority.EffectCatalog;
            foreach (ushort[] invalidPool in new ushort[][] { [], [Beta110FastDenseId.Invalid] })
            {
                var invalidCatalog = p.Selected == Beta110FastRelicCatalog.ArcaneScroll
                    ? catalog with { CharacterRewardPool = catalog.CharacterRewardPool with { Rare = invalidPool } }
                    : catalog with { NewLeafTransformPool = invalidPool };
                var invalid = p with { Authority = p.Authority with { EffectCatalog = invalidCatalog } };
                if (NeowSingleplayerLocalCpuPlan.TryCompile(invalid) is not null) throw new InvalidDataException("N direct local-first hides a local fault");
                checks++;
            }
            // An original array with duplicates still has its original weighted
            // index draw. No target encoding deduplicates the pool.
            ushort target = p.StructuredConditions[0].Target0;
            ushort[] duplicatePool = [target, target, (ushort)(target == 0 ? 1 : 0)];
            var duplicateCatalog = p.Selected == Beta110FastRelicCatalog.ArcaneScroll
                ? catalog with { CharacterRewardPool = catalog.CharacterRewardPool with { Rare = duplicatePool } }
                : catalog with { NewLeafTransformPool = duplicatePool };
            var duplicate = p with { Authority = p.Authority with { EffectCatalog = duplicateCatalog } };
            var weighted = NeowSingleplayerLocalCpuPlan.TryCompile(duplicate) ?? throw new InvalidDataException("N direct weighted pool not admitted");
            for (ulong root = 0; root < 4096; root++)
            {
                if (weighted.Matches(root) != NeowSingleplayerReplay.Matches(root, duplicate)) throw new InvalidDataException("N direct weighted pool changed");
                checks++;
            }
            checks += RelicCpuChecks.CheckEnvelope(request, fast.Matches, root => NeowSingleplayerReplay.Matches(root, p));
        }
        Console.WriteLine($"N_DIRECT_LOCAL_FIRST PASS checks={checks};soloOnly=true;reference=OriginalReplay;duplicateWeight=true");
        return checks;
    }

    private static int CheckEmptyExclusionSelection()
    {
        ushort[][] sources = [[], [7], [7, 7], [9, 2, 9, 5, 2, 0, 5, 8], [3, 1, 4, 2, 0]];
        ushort[][] exclusions = [[], [7], [7, 7], [99], [9, 2], [3, 1, 4, 2, 0], [9, 2, 5, 0, 8]];
        ulong[] boundaryRoots = [ulong.MaxValue, 1UL << 63, (1UL << 63) - 1, 0x0123456789abcdefUL];
        int checks = 0;
        foreach (ushort[] source in sources)
        foreach (ushort[] used in exclusions)
        for (int sample = 0; sample < 256 + boundaryRoots.Length; sample++)
        {
            ulong root = sample < 256 ? (ulong)sample : boundaryRoots[sample - 256];
            var expectedRng = new Beta110FastRng(root);
            var actualRng = new Beta110FastRng(root);
            // Exercise selection after an existing stream prefix as well as at initialization.
            for (int prefix = 0; prefix < sample % 5; prefix++)
            {
                _ = expectedRng.NextDouble();
                _ = actualRng.NextDouble();
            }
            ushort expected = OriginalSelectUnused(source, used, ref expectedRng);
            ushort actual = NeowLocalOperators.SelectUnused(source, used, ref actualRng);
            if (actual != expected) throw new InvalidDataException($"N unused selection mismatch root={root};source={string.Join(',', source)};used={string.Join(',', used)}");
            checks++;
            if (actualRng.CaptureCheckpoint() != expectedRng.CaptureCheckpoint())
                throw new InvalidDataException($"N unused selection RNG checkpoint mismatch root={root}");
            checks++;
            if (actualRng.NextInt(17) != expectedRng.NextInt(17) ||
                actualRng.NextDouble() != expectedRng.NextDouble() ||
                actualRng.CaptureCheckpoint() != expectedRng.CaptureCheckpoint())
                throw new InvalidDataException($"N unused selection continuation mismatch root={root}");
            checks++;
        }
        return checks;
    }

    // Retain the original two scans as a test oracle, with no empty-exclusion shortcut.
    private static ushort OriginalSelectUnused(ReadOnlySpan<ushort> source, ReadOnlySpan<ushort> used,
        ref Beta110FastRng rewards)
    {
        int available = 0;
        foreach (ushort candidate in source)
            if (used.IndexOf(candidate) < 0) available++;
        if (available == 0) return Beta110FastDenseId.Invalid;
        int rank = rewards.NextInt(available);
        foreach (ushort candidate in source)
        {
            if (used.IndexOf(candidate) >= 0) continue;
            if (rank-- == 0) return candidate;
        }
        return Beta110FastDenseId.Invalid;
    }
}
