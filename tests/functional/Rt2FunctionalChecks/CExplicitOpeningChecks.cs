using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class CExplicitOpeningChecks
{
    internal static int Run(int count = 32)
    {
        int checks = 0;
        void Check(bool pass, string label)
        {
            checks++;
            if (!pass) throw new InvalidOperationException("C explicit opening: " + label);
        }
        ModelKey bones = BaseGameModelKeys.Relics.NeowsBones, arcane = BaseGameModelKeys.Relics.ArcaneScroll,
            large = BaseGameModelKeys.Relics.LargeCapsule, small = BaseGameModelKeys.Relics.SmallCapsule,
            silken = BaseGameModelKeys.Relics.SilkenTress;
        ModelKey wheel = new("RELIC", "PRAYER_WHEEL"), white = new("RELIC", "WHITE_BEAST_STATUE");
        foreach (int slot in new[] { -1, 0, 3 })
        {
            var context = Fixture(slot);
            var basis = SearchQuery.Empty with { OpeningRoute = new(bones), CombatPotionRewards = new(1,
                CombatRewardSequenceOrderMode.Ordered, [new(CombatPotionSlotRequirement.NoDrop, null)]) };
            var requests = new Dictionary<CombatRewardReplay, ExactSearchExecutionRequest>();
            CombatRewardReplay Replay(SearchQuery q)
            {
                var request = CCompleteMatchingChecks.Request(q, context);
                var replay = new CombatRewardReplay(request);
                requests.Add(replay, request);
                return replay;
            }
            SearchQuery Legacy(ModelKeySetFilter filter) => basis with { LegacyNeow = basis.LegacyNeow with { BonesRelics = filter } };
            SearchQuery Required(params ModelKey[] keys) => basis with {
                OpeningRouteRelicRequirement = new(bones, keys, BonesRouteOrderMode.AnyOrder) };
            var canonical = Replay(Required(arcane));
            var all = Replay(Legacy(new([], [arcane], [])));
            var any = Replay(Legacy(new([arcane], [], [])));
            foreach (var candidate in new[] { all, any, Replay(basis with { LegacyNeow = basis.LegacyNeow with {
                RequiredBonesCombination = [arcane] } }) })
                Equivalent(canonical, candidate, "single child forms");
            Equivalent(canonical, Replay(LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {
                NeowRoute = new(bones), BonesRelics = new([], [arcane], []),
                CombatPotionRewardSequence = basis.CombatPotionRewards })), "public legacy adapter ingress");
            ModelKey rareCard = canonical.Catalog.DenseKeys[canonical.Catalog.CharacterRewardPool.Rare[0]];
            Equivalent(canonical, Replay(basis with { StructuredOpeningEffects = [new(arcane,
                NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.ProductRelevantEffects,
                NeowStructuredOutputKind.Card, [rareCard])] }), "authored child effect implies its source");
            Check(all.Plan.OpeningConsumption.OrderedRelicIds.SequenceEqual(new[] { Beta110FastRelicCatalog.ArcaneScroll }),
                "guaranteed legacy child becomes an authored consumption opcode");
            var missingPool = context with { Authority = context.Authority.WithEffectAuthority(
                context.Authority.EffectAuthority! with { BonesEligibilityExact = false }) };
            var missingReplay = new CombatRewardReplay(CCompleteMatchingChecks.Request(basis, missingPool));
            Check(missingReplay.ConservativelyKeeps && missingReplay.Matches(0) &&
                missingReplay.ConservativeOpeningReason == "BonesShufflePoolAuthorityUnavailable",
                "removing latent identities does not remove the necessary Bones shuffle-count authority check");

            var pair = Replay(Required(arcane, large));
            var pairAll = Replay(Legacy(new([], [large, arcane], [])));
            var pairMixed = Replay(Legacy(new([arcane], [large], [])));
            Equivalent(pair, pairAll, "unordered All pair");
            Equivalent(pair, pairMixed, "All and singleton Any conjunction");
            Check(pair.RouteCount == 2, "unordered guaranteed pair retains both pickup orders");
            var exact = Replay(basis with { OpeningRouteRelicRequirement = new(bones,
                [large, arcane], BonesRouteOrderMode.ExactOrder) });
            var legacyExact = Replay(basis with { LegacyNeow = basis.LegacyNeow with {
                RequiredBonesAcquisitionOrder = [large, arcane] } });
            Equivalent(exact, legacyExact, "explicit acquisition order");
            Check(exact.RouteCount == 1 && exact.Plan.OpeningConsumption.OrderedRelicIds.SequenceEqual(
                new[] { Beta110FastRelicCatalog.LargeCapsule, Beta110FastRelicCatalog.ArcaneScroll }),
                "stable held-set enumeration cannot reorder an authored pickup script");

            // This is independent of the replay helper: Bones shuffles first,
            // Arcane draws one Rare identity, Capsule draws only rarity rolls.
            for (ulong root = 0; root < (ulong)Math.Max(1, count); root++)
            {
                var expected = new Beta110FastRng(unchecked(root + (ulong)context.Authority.PlayerSlotIndex +
                    XxHash64.Hash("rewards"u8, 0)));
                expected.ConsumeUnstableShuffle(context.Authority.EffectAuthority!.BonesEligibleRelics!
                    .Count(k => k != bones));
                var afterShuffle = expected.CaptureCheckpoint();
                expected.NextInt(all.Catalog.CharacterRewardPool.Rare.Length);
                Check(all.Opening(root, 0).Rewards.CaptureCheckpoint() == expected.CaptureCheckpoint(),
                    "All Arcane advances the correct personal Rewards stream");
                expected = new Beta110FastRng(unchecked(root + (ulong)context.Authority.PlayerSlotIndex +
                    XxHash64.Hash("rewards"u8, 0)));
                expected.ConsumeUnstableShuffle(context.Authority.EffectAuthority.BonesEligibleRelics!.Count(k => k != bones));
                expected.NextFloat(); expected.NextFloat(); expected.NextInt(all.Catalog.CharacterRewardPool.Rare.Length);
                Check(exact.Opening(root, 0).Rewards.CaptureCheckpoint() == expected.CaptureCheckpoint(),
                    "ordered Large then Arcane retains exactly its declared consumption");
                Check(Replay(basis).Opening(root, 0).Rewards.CaptureCheckpoint() == afterShuffle,
                    "unspecified Bones has no invented child consumption");
            }

            var silkenAll = Replay(Legacy(new([], [silken], [])));
            Equivalent(Replay(Required(silken)), silkenAll, "held Bones grant");
            Check(silkenAll.Plan.ExplicitContext.SourceRelicKeys.Contains(silken),
                "guaranteed Bones held impact uses the shared acquisition facts");

            var ambiguous = basis with { LegacyNeow = basis.LegacyNeow with {
                CapsuleContainedRelics = new([], [wheel], []) } };
            var ambiguousReplay = Replay(ambiguous);
            Check(ambiguousReplay.Plan.ExplicitContext.AdditionalCardRewardCount == 1 &&
                ambiguousReplay.Plan.OpeningConsumption.OrderedRelicIds.Length == 0 &&
                !ExplicitOpeningRelicFacts.EnumerateGuaranteedRouteGrants(ambiguous).Contains(wheel) &&
                !ExplicitOpeningRelicFacts.EnumerateGuaranteedRouteGrants(ambiguous).Any(k => k == small || k == large),
                "source-ambiguous nested requirement supplies held impact without a Capsule source");
            var ambiguousAny = Replay(basis with { LegacyNeow = basis.LegacyNeow with {
                CapsuleContainedRelics = new([wheel], [], []) } });
            Equivalent(ambiguousReplay, ambiguousAny, "source-ambiguous singleton nested Any");
            var nested = basis with { OpeningRoute = new(small), StructuredOpeningEffects = [new(small,
                NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [wheel])] };
            var nestedReplay = Replay(nested);
            Check(nestedReplay.Plan.OpeningConsumption.OrderedRelicIds.SequenceEqual(new[] { Beta110FastRelicCatalog.SmallCapsule }) &&
                nestedReplay.Plan.ExplicitContext.AdditionalCardRewardCount == 1,
                "explicit nested held relic never becomes a Neow opcode");
            Check(nestedReplay.Plan.ExplicitContext.Fingerprint == ambiguousReplay.Plan.ExplicitContext.Fingerprint,
                "the same guaranteed held result has one impact identity across source-bound forms");
            var duplicate = Replay(nested with { LegacyNeow = nested.LegacyNeow with {
                CapsuleContainedRelics = new([], [wheel], []) } });
            Equivalent(nestedReplay, duplicate, "duplicate acquisition predicates do not stack one held relic");
            foreach (var upgrade in new[] { BaseGameModelKeys.OrdinaryRelics.Whetstone, BaseGameModelKeys.OrdinaryRelics.WarPaint })
            {
                var direct = basis with { OpeningRoute = new(small), LegacyNeow = basis.LegacyNeow with {
                    CapsuleContainedRelics = new([], [upgrade], []) } };
                var guaranteedUpgrade = Replay(direct);
                Equivalent(guaranteedUpgrade, Replay(direct with { LegacyNeow = direct.LegacyNeow with {
                    CapsuleContainedRelics = new([upgrade], [], []) } }), "singleton upgrade Any preserves explicit Niche consumption");
                var legacyCapsule = basis with { LegacyNeow = basis.LegacyNeow with {
                    BonesRelics = new([], [small], []), CapsuleContainedRelics = new([upgrade], [], []) } };
                Equivalent(Replay(legacyCapsule), Replay(legacyCapsule with {
                    OpeningRouteRelicRequirement = new(bones, [small], BonesRouteOrderMode.AnyOrder),
                    LegacyNeow = legacyCapsule.LegacyNeow with { BonesRelics = ModelKeySetFilter.Empty,
                        CapsuleContainedRelics = new([], [upgrade], []) } }), "guaranteed Capsule source preserves explicit Niche consumption");
                _ = Replay(basis with { LegacyNeow = basis.LegacyNeow with {
                    CapsuleContainedRelics = new([], [upgrade], []) } }).Opening(0, 0, out var unboundNiche);
                Check(unboundNiche is null,
                    "an explicit source-ambiguous upgrade never claims a known final shared Niche state");
            }

            var unresolvedRequest = CCompleteMatchingChecks.Request(nested with { StructuredOpeningEffects = [new(small,
                NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "RT2_TEST_UNMAPPED_HELD")])] }, context);
            var unresolved = new CombatRewardReplay(unresolvedRequest);
            var unresolvedModels = new CombatRewardFamilyModels(unresolvedRequest, unresolved, FamilyCpuExecution.Capacity);
            Check(unresolved.ConservativelyKeeps && unresolved.Matches(0) &&
                unresolvedModels.Survival.SurvivalProbability == 1 &&
                unresolvedModels.Survival.Evidence.Contains("PositivePredicatesRemainExact", StringComparison.Ordinal),
                "explicit unsupported obtain retains its obligation and declares the all-pass sieve population");
            Check(CombatRewardGpuPlan.TryCreate(unresolvedRequest, unresolved, out _) is { } unresolvedGpu &&
                unresolvedGpu.Meta[50] == 1,
                "explicit unresolved opening has the same conservative GPU coverage as CPU");

            var alternatives = Replay(Legacy(new([arcane, large], [], [])));
            Check(alternatives.Plan.OpeningConsumption.OrderedRelicIds.Length == 0,
                "multi-Any grants do not choose a child");
            var nestedAlternatives = Replay(basis with { LegacyNeow = basis.LegacyNeow with {
                CapsuleContainedRelics = new([wheel, white], [], []) } });
            Check(nestedAlternatives.Plan.ExplicitContext.IsEmpty && nestedAlternatives.Plan.OpeningConsumption.OrderedRelicIds.Length == 0,
                "multi-Any nested alternatives do not grant all held impacts");
            var future = Replay(basis with { RelicSequenceConstraints = [new(RelicSequenceKind.Rare,
                SearchSequenceRangeMode.ExactSlot, 1, new([wheel], [], []))] });
            Check(future.Plan.ExplicitContext.IsEmpty && future.Plan.OpeningConsumption.OrderedRelicIds.Length == 0,
                "future relic queue does not grant an opening held effect");

            foreach (var replay in new[] { all, pairAll, exact, silkenAll, ambiguousReplay, nestedReplay, alternatives, future })
                CheckPacked(replay);

            void Equivalent(CombatRewardReplay left, CombatRewardReplay right, string label)
            {
                Check(left.Plan.OpeningConsumption.Fingerprint == right.Plan.OpeningConsumption.Fingerprint &&
                    left.Plan.ExplicitContext.Fingerprint == right.Plan.ExplicitContext.Fingerprint && left.RouteCount == right.RouteCount &&
                    left.CapsuleNicheAdvances.SequenceEqual(right.CapsuleNicheAdvances),
                    label + " projection equality");
                for (ulong root = 0; root < (ulong)Math.Max(1, count); root++)
                {
                    for (int route = 0; route < left.RouteCount; route++)
                    {
                        var leftOpening = left.Opening(root, route, out var leftNiche);
                        var rightOpening = right.Opening(root, route, out var rightNiche);
                        Check(leftOpening.EquivalentTo(rightOpening) && leftNiche == rightNiche, label + " CPU opening equality");
                    }
                    Check(left.Matches(root) == right.Matches(root), label + " CPU C predicate equality");
                }
            }
            void CheckPacked(CombatRewardReplay replay)
            {
                var gpu = CombatRewardGpuPlan.TryCreate(requests[replay], replay, out string issue)
                    ?? throw new InvalidOperationException("C explicit packed plan: " + issue);
                Check(gpu.Routes == replay.RouteCount && gpu.Meta[40] == (replay.Plan.OpeningConsumption.ReplayBonesOffer ? 1u : 0u) &&
                    gpu.Meta[41] == replay.Plan.OpeningConsumption.OrderedRelicIds.Length,
                    "GPU packed replay routes and child count agree with CPU");
                for (int i = 0; i < replay.Plan.OpeningConsumption.OrderedRelicIds.Length; i++)
                    Check(gpu.Meta[42 + i] == replay.Plan.OpeningConsumption.OrderedRelicIds[i], "GPU preserves authored child order");
                Check(gpu.Meta[37] == (uint)replay.Plan.ExplicitContext.InfluenceFlags &&
                    gpu.Meta[38] == replay.Plan.ExplicitContext.AdditionalCardRewardCount &&
                    gpu.Meta[39] == (uint)(ushort)replay.Plan.ExplicitContext.FixedGoldAmount,
                    "GPU packed held context agrees with CPU");
            }
        }
        Console.WriteLine($"C_EXPLICIT_OPENING_PASS checks={checks};soloParty=true;cpuOpening=true;gpuPackedPlan=true;nativeRun=false");
        return 0;
    }

    private static SearchContext Fixture(int slot)
    {
        int players = slot < 0 ? 1 : 4, owner = Math.Max(0, slot);
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, owner, players);
        authority = authority.WithEffectAuthority(authority.EffectAuthority! with { CharacterRewardIdentityPoolExact = true,
            CharacterRewardPool = authority.EffectAuthority!.CharacterRewardPool!.Select(c => c with { CatalogProfileId = RuntimeProfileId.Beta111 }).ToArray() });
        var world = Beta109WorldFixtureFactory.Create();
        authority = authority.WithWorldAuthority(world with { CapturedProfileId = RuntimeProfileId.Beta111,
            Beta109Generation = world.Beta109Generation! with { Profile = RuntimeProfileId.Beta111,
                IsMultiplayer = players > 1, PlayerCount = players, PersonalPlayerSlot = owner,
                GameMode = players > 1 ? WorldGameMode.Multiplayer : WorldGameMode.Singleplayer } });
        return new(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.111.0", "0.111.0", "beta", "C-explicit-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
    }
}
