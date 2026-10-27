using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;

internal static class RelicCpuChecks
{
    internal static int CheckTreasureProjection()
    {
        var profile = Beta111Profile.Instance;
        var world = RelicSequenceFixtureFactory.CreateModern();
        world = world with { CapturedProfileId = profile.ProfileId,
            Beta109Generation = world.Beta109Generation! with { Profile = profile.ProfileId } };
        var initial = RelicSequencePredictor.Predict(profile, RelicSequenceFixtureFactory.ModernSeed, world);
        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidDataException("Treasure projection: " + label);
        }
        Require(initial.TreasureRoomLanes.Count == 3, "shared fixture lanes");
        string Snapshot() => string.Join("|", initial.TreasureRoomLanes.SelectMany(lane =>
            lane.FullEntries.Select(entry => lane.Kind + ":" + entry.Position + ":" + entry.RelicKey.Serialized)));
        string original = Snapshot();
        var removed = initial.TreasureRoomLanes.Select(lane => lane.FullEntries[0].RelicKey).ToArray();
        int checks = 0;
        for (ulong root = 0; root < 128; root++)
        foreach (bool hasAcquisitions in new[] { false, true })
        {
            ModelKey[] acquired = hasAcquisitions ? removed : [];
            var prediction = TreasureRelicPredictor.Predict(profile, root, initial, acquired);
            var nativeRng = new MegaCrit.Sts2.Core.Random.Rng(root, TreasureRelicPredictionResult.RngStream);
            var remaining = initial.TreasureRoomLanes.ToDictionary(lane => lane.RarityCode,
                lane => lane.FullEntries.Select(entry => entry.RelicKey).Where(key => !acquired.Contains(key)).ToList());
            string[] rarities = ["Common", "Uncommon", "Rare"];
            Require(prediction.Status == SeedDomainEvaluationStatus.Evaluated && prediction.Precision == PredictionPrecision.Exact,
                "exact initial shared bags");
            for (int i = 0; i < 2; i++)
            {
                string rarity = MegaCrit.Sts2.Core.Factories.RelicFactory.RollRarity(nativeRng).ToString();
                int lane = Array.IndexOf(rarities, rarity);
                while (lane < rarities.Length && remaining[rarities[lane]].Count == 0) lane++;
                ModelKey expected = lane == rarities.Length ? new("RELIC", "CIRCLET") : remaining[rarities[lane]][0];
                if (lane < rarities.Length) remaining[rarities[lane]].RemoveAt(0);
                var actual = prediction.Entries[i];
                Require(actual.EncounterOrdinal == i + 1 && actual.RolledRarityCode == rarity && actual.RelicKey == expected &&
                    actual.ActualRarityCode == (lane == rarities.Length ? "None" : rarities[lane]), "native rarity/shared-front:" + root);
                checks++;
            }
            Require(prediction.RngCallCount == nativeRng.ToSerializable().counter, "native rarity draw counter");
            var single = TreasureRelicPredictor.Predict(profile, root, initial, acquired, 1);
            Require(single.Entries.Count == 1 && single.Entries[0] == prediction.Entries[0] && single.RngCallCount == 1,
                "one-chest prefix");
            var silver = TreasureRelicPredictor.Predict(profile, root, initial, acquired.Append(new ModelKey("RELIC", "SILVER_CRUCIBLE")).ToArray());
            Require(silver.Entries.Count == 2 && silver.Entries[0].EncounterOrdinal == 1 && silver.Entries[0].IsSuppressed &&
                silver.Entries[0].RelicKey == null && silver.Entries[0].RolledRarityCode.Length == 0 &&
                silver.Entries[1].EncounterOrdinal == 2 && silver.Entries[1].RelicKey == prediction.Entries[0].RelicKey &&
                silver.Entries[1].RolledRarityCode == prediction.Entries[0].RolledRarityCode && silver.RngCallCount == 1,
                "Silver Crucible retains first position and first rarity draw");
            checks += 3;
        }
        for (ulong root = 0; root < 128; root++)
        {
            var preview = TreasureRelicPredictor.PredictInitialOrderPreview(profile, root, initial);
            var firstTwo = TreasureRelicPredictor.Predict(profile, root, initial, []);
            var nativeRng = new MegaCrit.Sts2.Core.Random.Rng(root, TreasureRelicPredictionResult.RngStream);
            var remaining = initial.TreasureRoomLanes.ToDictionary(lane => lane.RarityCode,
                lane => lane.FullEntries.Select(entry => entry.RelicKey).Where(key => key.Entry != "MASSIVE_SCROLL").ToList());
            string[] rarities = ["Common", "Uncommon", "Rare"];
            Require(preview.Status == SeedDomainEvaluationStatus.Evaluated && preview.Entries.Count == 9 &&
                preview.Precision == PredictionPrecision.Exact && preview.Entries.Take(2).SequenceEqual(firstTwo.Entries),
                "nine-item initial reference retains the first-two prefix");
            foreach (var actual in preview.Entries)
            {
                string rarity = MegaCrit.Sts2.Core.Factories.RelicFactory.RollRarity(nativeRng).ToString();
                int lane = Array.IndexOf(rarities, rarity);
                while (lane < rarities.Length && remaining[rarities[lane]].Count == 0) lane++;
                ModelKey expected = lane == rarities.Length ? new("RELIC", "CIRCLET") : remaining[rarities[lane]][0];
                if (lane < rarities.Length) remaining[rarities[lane]].RemoveAt(0);
                Require(actual.RelicKey == expected && actual.RolledRarityCode == rarity &&
                    actual.ActualRarityCode == (lane == rarities.Length ? "None" : rarities[lane]),
                    "initial reference native rarity/shared-front:" + root + ":" + actual.EncounterOrdinal);
                checks++;
            }
            Require(preview.RngCallCount == nativeRng.ToSerializable().counter && preview.RngCallCount == 9,
                "initial reference consumes nine rarity draws");
            checks += 2;
        }
        // Native rarity fallback advances to later bags; it never borrows a
        // personal bag or reshuffles shared bags. One Rare remains, then Circlet.
        var onlyRare = initial with { TreasureRoomLanes = initial.TreasureRoomLanes.Select(lane => lane with {
            FullEntries = lane.Kind == RelicSequenceKind.Rare ? lane.FullEntries.Take(1).ToArray() : [],
            TotalCount = lane.Kind == RelicSequenceKind.Rare ? 1 : 0 }).ToArray() };
        for (ulong root = 0; root < 32; root++)
        {
            var result = TreasureRelicPredictor.Predict(profile, root, onlyRare, []);
            Require(result.Entries[0].RelicKey == onlyRare.TreasureRoomLanes.Single(lane => lane.Kind == RelicSequenceKind.Rare).FullEntries[0].RelicKey &&
                result.Entries[0].ActualRarityCode == "Rare" && result.Entries[0].Source == TreasureRelicSource.SharedBag &&
                result.Entries[1].RelicKey == new ModelKey("RELIC", "CIRCLET") &&
                result.Entries[1].Source == TreasureRelicSource.ExhaustedBagCirclet, "rarity fallback/Circlet");
            checks++;
        }
        var missing = initial with { TreasureRoomLanes = initial.TreasureRoomLanes.Take(2).ToArray() };
        Require(TreasureRelicPredictor.Predict(profile, 0, missing, []).Status == SeedDomainEvaluationStatus.Unknown,
            "missing shared lane is unknown");
        Require(TreasureRelicPredictor.PredictInitialOrderPreview(profile, 0, missing).Status == SeedDomainEvaluationStatus.Unknown,
            "initial reference preserves missing-lane uncertainty");
        var truncated = initial with { TreasureRoomLanes = initial.TreasureRoomLanes.Select(lane => lane with {
            FullEntries = lane.FullEntries.Take(1).ToArray() }).ToArray() };
        Require(TreasureRelicPredictor.Predict(profile, 0, truncated, []).Status == SeedDomainEvaluationStatus.Unknown,
            "truncated lane is unknown");
        var uncertain = initial with { TreasureRoomLanes = initial.TreasureRoomLanes.Select(lane => lane with {
            Precision = PredictionPrecision.Unknown }).ToArray() };
        Require(TreasureRelicPredictor.Predict(profile, 0, uncertain, []).Status == SeedDomainEvaluationStatus.Unknown,
            "unknown shared lanes do not produce guaranteed relics");
        Require(Snapshot() == original, "shared input remains unchanged");
        bool rejectedThird = false;
        try { _ = TreasureRelicPredictor.Predict(profile, 0, initial, [], 3); }
        catch (ArgumentOutOfRangeException) { rejectedThird = true; }
        Require(rejectedThird, "two-chest scope");
        checks += 6;
        Console.WriteLine($"TREASURE_RELIC_PROJECTION_PASS checks={checks};native-rarity;opening-removal;SilverCrucible;fallback;pure-input;separate-initial-nine-reference");
        return checks;
    }

    private static int CheckLargeTargetPlan()
    {
        ushort[] ids = Enumerable.Range(0, 80).Select(i => (ushort)i).ToArray();
        var pool = new RelicFamilyPool(ids, new byte[80], [0], [80], [1], [1], [0], 80);
        var plan = new RelicFamilyPlan(pool, [new(0, 0, 1, 0, 0, 0, 0, 0, 70)],
            ids, [], new byte[4], [1, 0, 0, 0], ids, [0, 80, 80, 80], [80, 0, 0, 0], 0, false);
        if (plan.LocalStateCapacity != 80 || !RelicFamilyGpuExecutor.ShaderSource(false, localStateCapacity: plan.LocalStateCapacity)
            .Contains("const int MAX_LOCAL_STATE = 80;", StringComparison.Ordinal))
            throw new InvalidDataException("Large R query local shader specialization");
        var fast = RelicCpuPlan.TryCompile(plan)!;
        for (ulong root = 0; root < 64; root++)
        {
            ushort[] bag = (ushort[])ids.Clone();
            var rng = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
            rng.UnstableShuffle(bag.AsSpan());
            if (fast.Matches(root) != (bag[0] >= 70))
                throw new InvalidDataException("Large R tracked-rank/full-permutation mismatch:" + root);
        }
        return 65;
    }

    internal static int Run(int count)
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        string[] paths = ["v1/cases/R-common.json", "v1/cases/R-three-common.json",
            "direct-capsule-v1/Small-C-check.json", "direct-capsule-v1/Small-U-check.json", "direct-capsule-v1/Small-R-check.json",
            "direct-capsule-v1/Large-CC-check.json", "direct-capsule-v1/Large-CU-check.json", "direct-capsule-v1/Large-CR-check.json",
            "direct-capsule-v1/Large-UR-check.json", "direct-capsule-v1/Large-RR-check.json",
            "classic-product-v1/B-SourceCapsules.json", "classic-product-v1/B-SourceCapsules-positive.json",
            "classic-product-v1/B-small-source-Toxic.json", "classic-product-v1/B-small-source-Molten.json"];
        var cases = paths.Select(path => (path, workload: ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path))).ToList();
        var source = cases.Single(c => c.path == "classic-product-v1/B-SourceCapsules-positive.json").workload;
        var small = source.Query.StructuredOpeningEffects.Single(c => c.SourceRelicKey == BaseGameModelKeys.Relics.SmallCapsule);
        var large = source.Query.StructuredOpeningEffects.Single(c => c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);
        cases.Add(("authored/grouped-capsules", source with { Query = source.Query with { StructuredOpeningEffects =
            [small with { SourceRelicKey = BaseGameModelKeys.Relics.NeowsBones, Kind = NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                OutputKeys = [..small.OutputKeys, ..large.OutputKeys] }] } }));
        cases.Add(("authored/source-swapped", source with { Query = source.Query with { StructuredOpeningEffects =
            [small with { OutputKeys = [large.OutputKeys[0]] }, large with { OutputKeys = [small.OutputKeys[0], large.OutputKeys[1]] }] } }));
        foreach (bool reverse in new[] { false, true })
            cases.Add(($"authored/source-order-{reverse}", source with { Query = source.Query with { OpeningRouteRelicRequirement =
                source.Query.OpeningRouteRelicRequirement! with { OrderMode = BonesRouteOrderMode.ExactOrder, RequiredRelicKeys = reverse
                    ? [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule]
                    : [BaseGameModelKeys.Relics.LargeCapsule, BaseGameModelKeys.Relics.SmallCapsule] } } }));
        // R01: every already-audited neutral companion, both Capsule sizes,
        // Any and both authored pickup orders. The context is the captured solo
        // context; this does not establish a multiplayer obtain boundary.
        foreach (var companion in planCompanions())
        foreach (bool useLarge in new[] { false, true })
        foreach (int order in new[] { 0, 1, 2 })
        {
            var capsule = useLarge ? BaseGameModelKeys.Relics.LargeCapsule : BaseGameModelKeys.Relics.SmallCapsule;
            var row = useLarge ? large : small;
            cases.Add(($"authored/neutral/{companion.Entry}/{capsule.Entry}/{order}", source with { Query = source.Query with {
                OpeningRouteRelicRequirement = source.Query.OpeningRouteRelicRequirement! with {
                    RequiredRelicKeys = order == 2 ? [companion, capsule] : [capsule, companion],
                    OrderMode = order == 0 ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder },
                StructuredOpeningEffects = [row] } }));
        }
        static ModelKey[] planCompanions() => BaseGameModelKeys.Relics.AllNeow.Where(key =>
            Beta110FastRelicCatalog.TryGetId(key, out byte id) && RelicFullGpuPlan.CapsuleContinuationNeutralCompanion(id)).ToArray();
        var basis = cases[0].workload;
        var directSmall = cases.Single(c => c.path == "direct-capsule-v1/Small-C-check.json").workload;
        cases.Add(("authored/direct-capsule-plus-ordinary", directSmall with { Query = directSmall.Query with
            { RelicSequenceConstraints = basis.Query.RelicSequenceConstraints } }));
        var baseRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(basis.Query, context.Context), basis.Options).Plan!;
        RelicFamilyPlanCompiler.TryCompilePool(baseRequest, out _, out var dense, out var lanes, out _);
        for (int lane = 0; lane < 3; lane++)
        {
            var keys = dense.Where(p => lanes[lane].ContainsKey(p.Value)).Take(2).Select(p => p.Key).ToArray();
            var kind = lane == 0 ? RelicSequenceKind.Common : lane == 1 ? RelicSequenceKind.Uncommon : RelicSequenceKind.Rare;
            foreach (var mode in new[] { SearchSequenceRangeMode.FirstN, SearchSequenceRangeMode.ExactSlot })
            foreach (int depth in new[] { 1, 3, 7 })
            {
                ModelKeySetFilter[] filters = [new(keys, [], []), new([], [], [keys[0]]), new([keys[0]], [], [keys[1]])];
                if (mode == SearchSequenceRangeMode.FirstN && depth > 1) filters = [..filters, new([], keys, [])];
                for (int f = 0; f < filters.Length; f++)
                    cases.Add(($"authored/{kind}/{mode}/{depth}/{f}", basis with { Query = basis.Query with
                    { RelicSequenceConstraints = [new(kind, mode, depth, filters[f])] } }));
            }
        }
        int checks = CheckTreasureProjection() + CheckLargeTargetPlan() + CheckNeutralCapsuleContinuation(source, context);
        foreach (var (path, workload) in cases)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context.Context), workload.Options);
            var request = result.Plan ?? throw new InvalidOperationException(path + ":" + result.Issue);
            Func<ulong, bool> fast, reference;
            if (NeowReplayPlan.HasCapsule(request))
            {
                var route = NeowReplayPlan.Compile(request, true);
                var plan = CapsuleRelicCpuPlan.TryCompile(request, route) ?? throw new InvalidOperationException("not admitted:" + path);
                fast = plan.Matches; reference = new CapsuleRelicReplay(request, route).Matches;
                if (path.StartsWith("direct-capsule-v1/", StringComparison.Ordinal))
                {
                    checks += CheckDirectRarityPrerequisite(request, route, fast, reference, path);
                    checks += CheckDirectSlotAndOverflow(request, route, path);
                }
            }
            else
            {
                if (!RelicFamilyPlanCompiler.TryCompile(request, out var plan, out string issue)) throw new InvalidOperationException(issue);
                fast = (RelicCpuPlan.TryCompile(plan!) ?? throw new InvalidOperationException("ordinary not admitted")).Matches;
                RelicFamily.TryCreate(request, out var family);
                reference = typeof(RelicFamily).GetMethod("MatchesCpuReference", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate<Func<ulong, bool>>(family);
            }
            ulong start = 0;
            foreach (char c in workload.Options.StartSeed) start = checked(start * 34 + (uint)"0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".IndexOf(c));
            var profile = Beta111Profile.Instance;
            var random = new Random(991);
            int accepted = 0;
            for (int i = 0; i < count + 1024; i++)
            {
                ulong root = i < count ? profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, start + (ulong)i))
                    : (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 32);
                bool expected = reference(root), actual = fast(root);
                if (actual != expected) throw new InvalidDataException($"R parity:{path};i={i};root={root};expected={expected};actual={actual}");
                if (actual) accepted++;
                checks++;
            }
            Console.WriteLine($"R_CPU_CASE PASS path={path};population={count};random=1024;kept={accepted}");
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int numericKept = 0;
            for (ulong root = 0; root < 32768; root++) if (fast(root)) numericKept++;
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Console.WriteLine($"R_CPU_NUMERICAL path={path};roots=32768;kept={numericKept};ms={ms};allocatedBytes={allocated}");
            if (path is "v1/cases/R-three-common.json" or "classic-product-v1/B-SourceCapsules-positive.json")
                checks += CheckEnvelope(request, fast, reference);
        }
        foreach (string path in new[] { "family-pricing-matrix-v1/R-shop.json", "bones-capsule-boundary-v1/K-full.json" })
        {
            var w = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path);
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(w.Query, context.Context), w.Options).Plan!;
            bool admitted;
            if (NeowReplayPlan.HasCapsule(request)) admitted = CapsuleRelicCpuPlan.TryCompile(request, NeowReplayPlan.Compile(request, true)) is not null;
            else { RelicFamilyPlanCompiler.TryCompile(request, out var plan, out _); admitted = RelicCpuPlan.TryCompile(plan!) is not null; }
            if (admitted) throw new InvalidDataException("reference alternative unexpectedly admitted:" + path);
            checks++;
        }
        Console.WriteLine($"R_CPU_CHECKS PASS cases={cases.Count};checks={checks}");
        return 0;
    }

    private static int CheckNeutralCapsuleContinuation(ProductionSearchReplayWorkload source, ProductionSearchReplayContext context)
    {
        int checks = 0;
        var large = source.Query.StructuredOpeningEffects.Single(c => c.SourceRelicKey == BaseGameModelKeys.Relics.LargeCapsule);
        foreach (var companion in BaseGameModelKeys.Relics.AllNeow.Where(key => Beta110FastRelicCatalog.TryGetId(key, out byte id) &&
            RelicFullGpuPlan.CapsuleContinuationNeutralCompanion(id)))
        {
            var query = source.Query with { OpeningRouteRelicRequirement = source.Query.OpeningRouteRelicRequirement! with {
                RequiredRelicKeys = [BaseGameModelKeys.Relics.LargeCapsule, companion], OrderMode = BonesRouteOrderMode.AnyOrder },
                StructuredOpeningEffects = [large] };
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context.Context), source.Options).Plan!;
            var route = NeowReplayPlan.Compile(request, true);
            if (CapsuleRelicCpuPlan.TryCompile(request, route) is null) throw new InvalidDataException("R neutral companion not admitted: " + companion);
            ulong root = 0;
            while (root < 65536 && !NeowSingleplayerReplay.Observe(root, route).IdentityPass) root++;
            if (root == 65536) throw new InvalidDataException("R neutral offered-pair witness missing: " + companion);
            var observation = NeowSingleplayerReplay.Observe(root, route);
            Beta110FastRelicCatalog.TryGetId(companion, out byte companionId);
            // Reference executes even the unobserved companion, including its
            // Niche work. Its actual Rewards continuation must stay unchanged.
            foreach (bool companionFirst in new[] { false, true })
            {
                var rewards = observation.RewardsAfterBones;
                var niche = new Beta110FastRng(root);
                var transformations = new Beta110FastRng(root + 1);
                var potions = new Beta110FastRng(root + 2);
                if (!companionFirst) { _ = rewards.NextFloat(); _ = rewards.NextFloat(); }
                var before = rewards.CaptureCheckpoint();
                if (!NeowLocalOperators.ExecuteRelic(companionId, true, route, route.Authority.EffectCatalog, true, true,
                    false, false, ref rewards, ref niche, ref transformations, ref potions, []))
                    throw new InvalidDataException("R neutral companion continuation unavailable: " + companion);
                if (rewards.CaptureCheckpoint() != before) throw new InvalidDataException("R claimed neutral companion consumed Capsule Rewards: " + companion);
                checks++;
            }
            // Bind independently shuffled full-bag outputs at this real offered
            // pair root, so every companion has positive witnesses as well as
            // the complete fixed-population comparisons above.
            foreach (bool smallCapsule in new[] { false, true })
            {
                var capsuleKey = smallCapsule ? BaseGameModelKeys.Relics.SmallCapsule : BaseGameModelKeys.Relics.LargeCapsule;
                var positiveQuery = source.Query with { OpeningRouteRelicRequirement = source.Query.OpeningRouteRelicRequirement! with {
                    RequiredRelicKeys = [capsuleKey, companion], OrderMode = BonesRouteOrderMode.AnyOrder },
                    StructuredOpeningEffects = [source.Query.StructuredOpeningEffects.Single(c => c.SourceRelicKey == capsuleKey)] };
                var seedRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(positiveQuery, context.Context), source.Options).Plan!;
                var seedRoute = NeowReplayPlan.Compile(seedRequest, true);
                ulong positiveRoot = 0;
                while (positiveRoot < 65536 && !NeowSingleplayerReplay.Observe(positiveRoot, seedRoute).IdentityPass) positiveRoot++;
                if (positiveRoot == 65536) throw new InvalidDataException("R neutral positive identity witness missing");
                var actualKeys = IndependentCapsuleOutputs(seedRequest, seedRoute, positiveRoot, smallCapsule ? 1 : 2);
                positiveQuery = positiveQuery with { StructuredOpeningEffects = [positiveQuery.StructuredOpeningEffects[0] with { OutputKeys = actualKeys }] };
                foreach (int pickup in new[] { 0, 1, 2 })
                {
                    var orderedQuery = positiveQuery with { OpeningRouteRelicRequirement = positiveQuery.OpeningRouteRelicRequirement! with {
                        OrderMode = pickup == 0 ? BonesRouteOrderMode.AnyOrder : BonesRouteOrderMode.ExactOrder,
                        RequiredRelicKeys = pickup == 2 ? [companion, capsuleKey] : [capsuleKey, companion] } };
                    var positiveRequest = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(orderedQuery, context.Context), source.Options).Plan!;
                    var positiveRoute = NeowReplayPlan.Compile(positiveRequest, true);
                    var positivePlan = CapsuleRelicCpuPlan.TryCompile(positiveRequest, positiveRoute) ?? throw new InvalidDataException("R neutral positive plan not admitted");
                    if (!new CapsuleRelicReplay(positiveRequest, positiveRoute).Matches(positiveRoot) || !positivePlan.Matches(positiveRoot))
                        throw new InvalidDataException("R neutral positive pickup witness lost: " + companion);
                    checks++;
                }
            }
            if (companion == BaseGameModelKeys.Relics.NewLeaf)
            {
                var bad = route with { Authority = route.Authority with { EffectCatalog = route.Authority.EffectCatalog with {
                    NewLeafTransformAuthorityExact = false } } };
                if (CapsuleRelicCpuPlan.TryCompile(request, bad) is not null) throw new InvalidDataException("R neutral companion hid NewLeaf authority fault");
                checks++;
            }
            // A multiplayer authority cannot borrow the solo neutral proof.
            if (CapsuleRelicCpuPlan.TryCompile(request, route with { Authority = route.Authority with { PlayersCount = 2 } }) is not null)
                throw new InvalidDataException("R neutral solo proof leaked into party");
            checks++;
        }
        Console.WriteLine($"R_NEUTRAL_COMPANION_CONTINUATION PASS checks={checks};soloOnly=true;bothPickupOrders=true");
        return checks;
    }

    // Test-only full permutation oracle: no tracked positions or target-rank
    // code participates in choosing the known positive Capsule outputs.
    private static ModelKey[] IndependentCapsuleOutputs(ExactSearchExecutionRequest request, NeowReplayPlan route, ulong root, int count)
    {
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out var pool, out var dense, out _, out string issue))
            throw new InvalidDataException(issue);
        var keys = dense.ToDictionary(pair => pair.Value, pair => pair.Key);
        var lanes = new ushort[3][];
        var upfront = new Beta110FastRng(unchecked(root + RelicCpuPlan.UpFrontHash));
        for (int bucket = 0; bucket < pool.BucketCount; bucket++)
        {
            ushort[] entries = pool.DenseRelicIds.AsSpan(pool.BucketOffsets[bucket], pool.BucketLengths[bucket]).ToArray();
            upfront.UnstableShuffle(entries.AsSpan());
            if (pool.BucketScopes[bucket] == 1 && pool.BucketKinds[bucket] is >= 1 and <= 3)
                lanes[pool.BucketKinds[bucket] - 1] = entries;
        }
        var rewards = NeowSingleplayerReplay.Observe(root, route).RewardsAfterBones;
        var consumed = new int[3];
        var observed = new ModelKey[count];
        for (int i = 0; i < count; i++)
        {
            float roll = rewards.NextFloat();
            int lane = roll < .5f ? 0 : roll < .83f ? 1 : 2;
            observed[i] = keys[lanes[lane][consumed[lane]++]];
        }
        return observed;
    }

    private static int CheckDirectRarityPrerequisite(ExactSearchExecutionRequest request, NeowReplayPlan route,
        Func<ulong, bool> fast, Func<ulong, bool> reference, string name)
    {
        if (route.Bones || route.Selected is not (Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule))
            throw new InvalidDataException("Direct rarity fixture scope");
        if (!RelicFamilyPlanCompiler.TryCompilePool(request, out _, out var dense, out var lanes, out string issue))
            throw new InvalidDataException(issue);
        int[] required = new int[3];
        foreach (var key in route.Filter.StructuredNeowEffects.Single().OutputKeys)
        {
            ushort id = dense[key];
            int lane = Enumerable.Range(0, 3).Single(i => lanes[i].ContainsKey(id));
            required[lane]++;
        }
        int drawCount = route.Selected == Beta110FastRelicCatalog.SmallCapsule ? 1 : 2;
        int identityKeptButRarityFailed = 0, checkedRoots = 0;
        Span<int> actual = stackalloc int[3];
        for (ulong root = 0; root < 1024; root++)
        {
            actual.Clear();
            // Independent necessary fact from the original actual direct RNG
            // entry. No observation checkpoint or probability model participates.
            var rewards = new Beta110FastRng(unchecked(root + (ulong)route.Authority.PlayerSlotIndex + NeowFamilyReplay.RewardsHash));
            for (int draw = 0; draw < drawCount; draw++)
            {
                float value = rewards.NextFloat();
                actual[value < .5f ? 0 : value < .83f ? 1 : 2]++;
            }
            bool rarityPass = true;
            for (int lane = 0; lane < 3; lane++) rarityPass &= required[lane] <= actual[lane];
            if (!rarityPass)
            {
                if (fast(root) || reference(root)) throw new InvalidDataException("Direct rarity false rejection: " + name);
                if (NeowFamilyReplay.Observe(root, route).IdentityPass) identityKeptButRarityFailed++;
            }
            checkedRoots++;
        }
        if (identityKeptButRarityFailed == 0) throw new InvalidDataException("Direct rarity identity-pass witness missing: " + name);
        Console.WriteLine($"R_DIRECT_RARITY_ORDER_CASE name={name};roots={checkedRoots};identityKeptButRarityFailed={identityKeptButRarityFailed};actualRoot=true");
        return checkedRoots + 1;
    }

    private static int CheckDirectSlotAndOverflow(ExactSearchExecutionRequest request, NeowReplayPlan route, string name)
    {
        int checks = 0;
        foreach (int slot in new[] { 0, 1, 3 })
        foreach (bool repeated in route.Selected == Beta110FastRelicCatalog.LargeCapsule ? new[] { false, true } : new[] { false })
        {
            // Closed numeric donor fixture: reuse the admitted initial bag and
            // identity pools, but exercise real personal stream offsets and the
            // Party observation dispatcher. This is not a party capture claim.
            var personal = route with { Authority = route.Authority with { PlayerSlotIndex = slot, PlayersCount = slot == 0 ? 1 : 4 } };
            if (repeated)
                personal = personal with { Filter = personal.Filter with { StructuredNeowEffects = personal.Filter.StructuredNeowEffects
                    .Select(c => c with { OutputKeys = new[] { c.OutputKeys[0], c.OutputKeys[0] }, AllowDuplicateOutputs = true }).ToArray() } };
            var fast = CapsuleRelicCpuPlan.TryCompile(request, personal) ?? throw new InvalidDataException("Direct slot fixture not admitted: " + name);
            var reference = new CapsuleRelicReplay(request, personal);
            ulong rewardsWrap = unchecked(0UL - (ulong)slot - NeowFamilyReplay.RewardsHash);
            ulong eventWrap = unchecked(0UL - (ulong)slot - XxHash64.Hash("NEOW"u8, 0));
            ulong[] boundary = [0, 1, ulong.MaxValue, ulong.MaxValue - 1,
                unchecked(rewardsWrap - 1), rewardsWrap, unchecked(rewardsWrap + 1),
                unchecked(eventWrap - 1), eventWrap, unchecked(eventWrap + 1)];
            foreach (ulong root in Enumerable.Range(0, 256).Select(i => (ulong)i).Concat(boundary).Distinct())
            {
                bool expected = reference.Matches(root), actual = fast.Matches(root);
                if (actual != expected)
                    throw new InvalidDataException($"Direct slot/overflow parity:{name};slot={slot};repeated={repeated};root={root};expected={expected};actual={actual}");
                if (repeated && actual) throw new InvalidDataException("Unique initial bag cannot supply a repeated Capsule target");
                checks++;
            }
        }
        return checks;
    }

    internal static int CheckEnvelope(ExactSearchExecutionRequest request, Func<ulong, bool> fast, Func<ulong, bool> reference)
    {
        string[] names = ["RT2_CPU_ROOTS", "RT2_CPU_WORKERS", "RT2_CPU_PARTITIONED"];
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        int checks = 0;
        try
        {
            Environment.SetEnvironmentVariable(names[0], "dense");
            Environment.SetEnvironmentVariable(names[1], "4");
            Environment.SetEnvironmentVariable(names[2], "1");
            var profile = Beta111Profile.Instance;
            foreach (ulong first in new[] { 0UL, VisibleSeedCandidateCodec.SpaceSize(profile) - 8193 })
            foreach (bool compact in new[] { false, true })
            {
                var batch = new SearchBatch(first, 8193);
                var input = compact ? FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, 8193).Where(i => i % 2 == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                var expected = input.EnumerateLogicalOrdinals().Where(i => reference(profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, first + i)))).ToArray();
                var cpu = new FamilyCpuExecution(request, "R.Check", "TrackedParity", workers:4, windowCapacity:65536, numericalRoots:true);
                var actual = cpu.Execute(new(batch, request), input, CancellationToken.None, fast);
                if (!actual.ExportAbi1().Span.SequenceEqual(expected)) throw new InvalidDataException("R partition/tail ABI1");
                var zero = FamilyCandidateSet.Dense(new(first, 0));
                if (cpu.Execute(new(zero.Batch, request), zero, CancellationToken.None, fast).Count != 0) throw new InvalidDataException("R zero");
                try { cpu.Execute(new(batch, request), input, new CancellationToken(true), fast); throw new InvalidDataException("R cancel lost"); }
                catch (OperationCanceledException) { }
                try { cpu.Execute(new(batch, request), input, CancellationToken.None, root => { _ = fast(root); throw new InvalidOperationException("injected-R"); }); throw new InvalidDataException("R fault lost"); }
                catch (Exception ex) when (FamilyCpuMatchException.IsRecoverable(ex) &&
                    FamilyCpuMatchException.Matches(ex).All(failure => failure.FamilyId == "R.Check" &&
                        failure.Batch == batch && failure.InnerException is InvalidOperationException { Message: "injected-R" })) { }
                try { cpu.Execute(new(batch, request), input, CancellationToken.None, fast); throw new InvalidDataException("R resumed after fault"); }
                catch (InvalidOperationException) { }
                checks += 5;
            }
        }
        finally { for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]); }
        return checks;
    }
}
