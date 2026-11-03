using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;

internal static class FamilyCombatRewardChecks
{
    // This pure presentation fixture checks reward structure and localized
    // suffixes without loading native models or maintaining a name catalog.
    private sealed record RewardFixtureNames(string LanguageCode) : IGameContentNameResolver
    {
        public string Resolve(ModelKey key, GameContentKind kind) => key.Serialized;
    }

    private delegate ushort UnusedPicker(ReadOnlySpan<ushort> source, ReadOnlySpan<ushort> used,
        ref Beta110FastRng rewards);

    private static void CheckOpeningSelection()
    {
        var select = typeof(CombatRewardOpeningReplay).GetMethod("SelectUnused",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .CreateDelegate<UnusedPicker>();
        ushort[][] sources = [[], [5], [5, 5, 3, 5, 7, 3],
            Enumerable.Range(0, 64).Select(i => (ushort)i).ToArray()];
        ushort[][] exclusions = [[], [5], [5, 5, 3], [99],
            Enumerable.Range(0, 64).Select(i => (ushort)i).ToArray()];
        foreach (ushort[] source in sources)
        foreach (ushort[] used in exclusions)
        {
            // Materialize the old eligible occurrence sequence independently of
            // the numerical helper, retaining repeated source entries in order.
            ushort[] eligible = source.Where(candidate => !used.Contains(candidate)).ToArray();
            for (ulong root = 0; root < 512; root++)
            {
                var expected = new Beta110FastRng(root);
                var actual = new Beta110FastRng(root);
                ushort value = eligible.Length == 0 ? Beta110FastDenseId.Invalid :
                    eligible[expected.NextInt(eligible.Length)];
                Check(select(source, used, ref actual) == value, "Opening selection occurrence rank");
                Check(actual.CaptureCheckpoint() == expected.CaptureCheckpoint(), "Opening selection RNG checkpoint");
                Check(actual.NextInt(1009) == expected.NextInt(1009) &&
                    actual.NextFloat() == expected.NextFloat(), "Opening selection RNG continuation");
            }
        }
    }

    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; }
    private static void Throws<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { _checks++; return; } throw new Exception(message); }

    internal static int CheckSilkenTressOpeningOrder()
    {
        const string seed = "N9U2V9VPUSR7";
        var profile = Beta111Profile.Instance;
        ulong root = profile.ComputeRootSeed(seed);
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            Path.Combine(Environment.CurrentDirectory, "tools/ProductionSearchHost/Corpus/n-direct-v1/context-defect-a10.json")).Context;
        var captured = context.Authority;
        var tress = BaseGameModelKeys.Relics.SilkenTress;
        var bonesKey = BaseGameModelKeys.Relics.NeowsBones;
        foreach (bool complex in new[] { false, true })
        foreach (var companion in new[] { BaseGameModelKeys.Relics.LostCoffer,
                     BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.HeftyTablet })
        {
            // Fix the offer pair; use the existing captured pools for actual card
            // generation. Test both orders without searching for a lucky seed.
            var effects = captured.EffectAuthority! with { BonesEligibleRelics = [tress, companion] };
            var authority = captured.WithEffectAuthority(effects);
            var projection = new NeowEffectProjectionEngine(profile, "tress-order", root, authority, complex).Project(bonesKey);
            var bones = projection.BonesOutcome!;
            bool consumes = companion != BaseGameModelKeys.Relics.HeftyTablet;
            Check(bones.OriginalRoutes.Count == 2, "Both pickup orders survive");
            Check(bones.OutcomeGroups.Count == (consumes ? 2 : 1), "Only actual CardReward consumers split the order picker");
            Check(bones.OverallImpact == (consumes ? AcquisitionOrderImpact.ProvenSensitive : AcquisitionOrderImpact.ProvenIndependent),
                "Order comparison includes Tress state without a complex-mode gate");
            var first = bones.OriginalRoutes.Single(r => r.AcquisitionOrder[0] == tress);
            var last = bones.OriginalRoutes.Single(r => r.AcquisitionOrder[1] == tress);
            var firstContinuation = first.OpeningRewardContinuation!;
            var lastContinuation = last.OpeningRewardContinuation!;
            Check(firstContinuation.SilkenTressConsumedDuringOpening == consumes &&
                  !lastContinuation.SilkenTressConsumedDuringOpening, "Opening consumption follows pickup order");
            Check(firstContinuation.RewardsRngState == lastContinuation.RewardsRngState &&
                  first.SharedContinuation.FinalCurseKey == last.SharedContinuation.FinalCurseKey,
                "Deterministic Glam does not change RNG or final curse");
            var firstGroups = first.RelicScopedResults.Single(r => r.SourceRelicKey == companion).EffectGroups
                .Where(g => g.OrderedItems.Any(i => i.Kind == PredictedEffectKind.AddCard)).OrderBy(g => g.GroupOrder).ToArray();
            Check(firstGroups[0].OrderedItems.Where(i => i.Kind == PredictedEffectKind.AddCard).All(i => i.HasGlam == consumes),
                "Tress enchants the first actual card reward");
            Check(firstGroups.Skip(1).SelectMany(g => g.OrderedItems).All(i => !i.HasGlam), "Second Kaleidoscope group is not enchanted");
            Check(last.RelicScopedResults.SelectMany(r => r.EffectGroups).SelectMany(g => g.OrderedItems).All(i => !i.HasGlam),
                "Reward before Tress is not enchanted retroactively");
            foreach (string language in new[] { "zh", "en" })
            {
                var text = JsonUiTextProvider.CreatePredictorUi13(language);
                var names = new RewardFixtureNames(language);
                var view = BonesOutcomePresentationBuilder.Build(bones, text, names, false)!;
                Check(view.OutcomeGroups.Count == bones.OutcomeGroups.Count, "Order picker retains the separate outcomes");
                var shown = view.OutcomeGroups.Single(g => g.EquivalentRouteIds.Contains(first.RouteId));
                string suffix = text.Get(Ui1TextKey.NormalCombatRewardCardGlamSuffix);
                Check(shown.RelicResults.SelectMany(r => r.EffectGroups).SelectMany(g => g.OrderedItems)
                        .Count(i => i.ShowInNormalMode && i.CompactDisplayText.EndsWith(suffix, StringComparison.Ordinal)) == (consumes ? 3 : 0),
                    "Opening result text marks the three enchanted cards in " + language);
            }
            var choice = new NeowChoiceResult(0, bonesKey, PredictionPrecision.Exact, projection.Capability, projection.Precision,
                projection.EffectGroups, projection.Warnings, "fixture", projection.EvidenceCode,
                new(true, null, "sts2", SourceAuthority.AuditedStaticExact, IdentityResolutionStatus.Exact),
                BonesOutcome: bones, OpeningRewardContinuations: projection.OpeningRewardContinuations);
            var prediction = NormalCombatRewardSequencePredictor.PredictFromRootHash(profile, root, seed, 0,
                context.Ascension, [choice], effects, authority.WorldAuthority);
            Check(prediction.Routes.Count == (consumes ? 2 : 1), "C keeps consumed and unconsumed continuations separate");
            foreach (var route in prediction.Routes)
            {
                Check(route.Battles.Count >= 2, "Combat rewards remain available");
                bool expectGlam = !route.RepresentativeContinuation.SilkenTressConsumedDuringOpening;
                Check(route.Battles[0].Cards.All(c => c.Enchantments.Contains(NormalCombatCardEnchantment.Glam) == expectGlam),
                    "First battle displays Glam only if still available");
                Check(route.Battles.Skip(1).SelectMany(b => b.Cards).All(c => !c.Enchantments.Contains(NormalCombatCardEnchantment.Glam)),
                    "Tress never enchants a second combat reward");
            }
        }
        Console.WriteLine($"SilkenTressOpeningOrder checks={_checks};PASS");
        return 0;
    }

    internal static int Run(int count, bool exportGpu = false, bool openingPrefixOnly = false)
    {
        CheckOpeningSelection();
        CheckPreparedCardSelection();
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, 10, 0, 1);
        authority = authority.WithEffectAuthority(authority.EffectAuthority! with { CharacterRewardIdentityPoolExact = true,
            CharacterRewardPool = authority.EffectAuthority!.CharacterRewardPool!.Select(c => c with {
                CatalogProfileId = RuntimeProfileId.Beta111, EligibilityAuthority = "C0 fixture pool membership" }).ToArray() });
        var world = Beta109WorldFixtureFactory.Create();
        authority = authority.WithWorldAuthority(world with { CapturedProfileId = RuntimeProfileId.Beta111,
            Beta109Generation = world.Beta109Generation! with { Profile = RuntimeProfileId.Beta111 } });
        var context = new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 10, authority,
            new("0.111.0", "0.111.0", "beta", "C0-fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
        PreparedSearch QueryAtAscension(NeowSearchFilter filter, int ascension)
        {
            var queryContext = ascension == 10 ? context : context with { Ascension = ascension,
                Authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Defect, ascension, 0, 1)
                    .WithEffectAuthority(authority.EffectAuthority!).WithWorldAuthority(authority.WorldAuthority!) };
            var compiled = SearchCompiler.Compile(LegacySearchQueryAdapter.FromFilter(filter), queryContext);
            return new(new SearchModel(compiled, CompiledSearchEvaluationProjector.Project(compiled).Evaluation,
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint),
                new("000000000000", 10000, 30, 1), "000000000000", 10000, compiled.SemanticFingerprint);
        }
        PreparedSearch Query(NeowSearchFilter filter) => QueryAtAscension(filter, 10);
        CheckRealNeutralWitness(Query);
        var openingPrefixFixtures = CheckOpeningPrefixHotLoop(Query, filter => QueryAtAscension(filter, 0));
        if (openingPrefixOnly)
        {
            if (exportGpu)
            {
                ExportOpeningPrefixGpu(openingPrefixFixtures.Where(f => !f.Name.StartsWith("ProductionDefault.",
                    StringComparison.Ordinal) && !f.Name.StartsWith("Followup.", StringComparison.Ordinal)).ToList());
                ExportOpeningPrefixGpu(openingPrefixFixtures.Where(f => f.Name.StartsWith("ProductionDefault.",
                    StringComparison.Ordinal)).ToList(), ".build-evidence/c-default-gpu", 515);
                ExportOpeningPrefixGpu(openingPrefixFixtures.Where(f => f.Name.StartsWith("Followup.",
                    StringComparison.Ordinal)).ToList(), ".build-evidence/c-followup-gpu", 515);
            }
            Console.WriteLine($"C opening-prefix checks={_checks};PASS");
            return 0;
        }
        var terminalCardFixtures = CheckTerminalCardGate(Query);
        var empty = ModelKeySetFilter.Empty;
        var gold = new NormalCombatRewardSearchCondition(3, empty, NormalCombatPotionRequirement.Any, empty, 12, null);
        var basis = NeowSearchFilter.Empty with { NormalCombatRewardConditions = [gold] };
        CheckPartyOpeningContinuations(Query, basis);
        CheckPartyDistinctAssignments(Query);
        CheckProbabilityAssignmentWindows(Query);
        CheckCapsuleProbabilityConjunctions(Query);
        CheckDeclaredRewardProbabilityPolicy(Query);
        CheckLargeFiniteConjunctions();
        var neutral = new CombatRewardReplay((Query(basis)).Model);
        var cOnly = FamilyExecutionCoordinator.Plan(Query(basis));
        Check(cOnly.OrderedFamilies.Count == 1 && cOnly.OrderedFamilies[0].FamilyId == CombatRewardFamily.Id, "C-only registration");
        Check(cOnly.Stages[0].Coverage.SequenceEqual(new[] { "C.CombatReward" }) &&
            Math.Abs(cOnly.Stages[0].Survival!.Value - 4d / 9d) < 1e-10, "C Coverage / ordered gold sieve probability");
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(Query(basis), cOnly.OrderedFamilies) == FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, "C gold GPU window");
        Check(!CombatRewardFamily.TryCreate((Query(NeowSearchFilter.Empty)).Model,FilterExecutionPreferences.From((Query(NeowSearchFilter.Empty)).RunOptions), out _), "Empty C must not register");
        var gpuRequest = Query(NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(1,
            CombatRewardSequenceOrderMode.Ordered, [new(CombatPotionSlotRequirement.NoDrop, null)]) });
        var gpuOnly = FamilyExecutionCoordinator.Plan(gpuRequest);
        Check(gpuOnly.OrderedFamilies.Count == 1 && gpuOnly.OrderedFamilies[0].ConditionPerformance.UsesGpu,
            "C-only GPU registration without N");
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(gpuRequest, gpuOnly.OrderedFamilies) ==
            FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, "C GPU preserves normal root batch");
        FilterBodyDescription Model(NeowSearchFilter filter)
        {
            var request = Query(filter);
            Check(CombatRewardFamily.TryCreate(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var value), "C model registration");
            return value!;
        }
        // Six unordered duplicate requirements need 64 subset states, not the old byte.
        var probabilityTarget = authority.EffectAuthority!.CharacterRewardPool!.First(c=>c.Rarity==EffectCardRarity.Common).CardKey;
        var horizonFingerprints=new HashSet<string>();
        foreach(int h in new[]{4,5,6})
        {
            Check(horizonFingerprints.Add(new CombatRewardReplay((Query(NeowSearchFilter.Empty with {CombatCardRewardSequence=new(h,CombatRewardSequenceOrderMode.Unordered,Enumerable.Repeat<ModelKey?>(null,h-1).Append(probabilityTarget).ToArray())})).Model).Plan.Fingerprint),"Different unordered horizons have different physical fingerprints");
            double? P(CombatRewardSequenceOrderMode order) => CombatRewardProbabilityEstimator.Estimate(
                SearchSelectivityInput.From(Query(NeowSearchFilter.Empty with {CombatCardRewardSequence=new(h,order,
                    Enumerable.Repeat((ModelKey?)probabilityTarget,h).ToArray())}))).Probability;
            double? ordered=P(CombatRewardSequenceOrderMode.Ordered), unordered=P(CombatRewardSequenceOrderMode.Unordered);
            Check(ordered is >0 && unordered is >0 && Math.Abs(ordered.Value-unordered.Value)<1e-15,
                "Six-battle duplicate multiset equals one copy in every battle; no assignment-mask truncation");
        }
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        var dropFilters = new[] {
            NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered, [noDrop]) },
            NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(2, CombatRewardSequenceOrderMode.Ordered, [noDrop, noDrop]) },
            NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(3, CombatRewardSequenceOrderMode.Ordered, [noDrop, noDrop, noDrop]) },
            NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(3, CombatRewardSequenceOrderMode.Ordered,
                [new(CombatPotionSlotRequirement.Neutral, null), new(CombatPotionSlotRequirement.Neutral, null), noDrop]) }
        };
        double[] dropProbabilities = [0.6, 0.3, 0.12, 0.564];
        foreach (var specified in new ModelKey[][] { [], [BaseGameModelKeys.Relics.SilkenTress], [new("RELIC", "WHITE_BEAST_STATUE")] })
        {
            var request = Query(dropFilters[0] with { NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                RequiredBonesCombination = specified });
            var estimate = RolltheSpire2.Search.Selectivity.CombatRewardProbabilityEstimator.Estimate(
                RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(request));
            double expectedBonesProbability = specified.Any(k => k.Entry == "WHITE_BEAST_STATUE") ? 0 : .6;
            Check(estimate.IsPriced && estimate.Probability is double p && Math.Abs(p - expectedBonesProbability) < 1e-6,
                "Unspecified Bones grants ignored; explicitly authored WBS still affects probability");
        }
        double previousWork = 0;
        for (int i = 0; i < dropFilters.Length; i++)
        {
            var model = Model(dropFilters[i]);
            Check(model.Survival.IsResolved && Math.Abs(model.Survival.SurvivalProbability!.Value - dropProbabilities[i]) < 1e-6,
                "C potion pity analytical vector " + i);
            Check(model.ExpectedFilteringCost!.ExpectedFilteringWorkUnitsPerInput == model.AnalyticalCost.WorkUnitsPerInput,
                "C cost full envelope, no invented reach discount");
            if (i < 3) Check(model.AnalyticalCost.WorkUnitsPerInput > previousWork, "C horizon increases full work");
            previousWork = model.AnalyticalCost.WorkUnitsPerInput;
            Check(model.ResolveSurvival(new HashSet<string> { "N.Neow", "E.EventResult", "W.World" }).SurvivalProbability ==
                model.Survival.SurvivalProbability, "Neutral C uses its owned model after unrelated Coverage; no known dependency");
        }
        var duplicatePotion = dropFilters[1] with { CombatPotionRewardSequence = new(2,
            CombatRewardSequenceOrderMode.Unordered, [noDrop, noDrop]) };
        Check(Model(duplicatePotion).Survival.SurvivalProbability == CombatRewardProbabilityEstimator.EstimateOptimizedConditions(
            SearchSelectivityInput.From(Query(duplicatePotion))).Probability, "C complete unordered potion matcher uses its declared probability");
        var jointReward = dropFilters[0] with { CombatCardRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered,
            [new ModelKey("CARD", "BENCH_CHARACTER_0")]) };
        Check(Model(jointReward).Survival.SurvivalProbability == CombatRewardProbabilityEstimator.EstimateOptimizedConditions(
            SearchSelectivityInput.From(Query(jointReward))).Probability, "C complete card/potion matcher uses its declared probability");
        var fixedCard = Model(NeowSearchFilter.Empty with { CombatCardRewardSequence = new(1,
            CombatRewardSequenceOrderMode.Ordered, [new ModelKey("CARD", "BENCH_CHARACTER_0")]) });
        Check(fixedCard.Survival.SurvivalProbability is > 0 and < 1, "C ordered card donor available");
        var repeatedNeutralPrefix = NeowSearchFilter.Empty with
        {
            NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
            RequiredBonesCombination = [BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.SilkenTress],
            CombatCardRewardSequence = new(2, CombatRewardSequenceOrderMode.Unordered,
                [new ModelKey("CARD", "BENCH_CHARACTER_0"), new ModelKey("CARD", "BENCH_CHARACTER_0")])
        };
        var oneExistential = repeatedNeutralPrefix with { CombatCardRewardSequence = new(3,
            CombatRewardSequenceOrderMode.Unordered, [new ModelKey("CARD", "BENCH_CHARACTER_0"), null, null]) };
        var repeatedModel = Model(repeatedNeutralPrefix);
        Check(repeatedModel.Survival.IsResolved && repeatedModel.Survival.SurvivalProbability == CombatRewardProbabilityEstimator.EstimateOptimizedConditions(
            SearchSelectivityInput.From(Query(repeatedNeutralPrefix))).Probability &&
            repeatedModel.Survival.SurvivalProbability < Model(oneExistential).Survival.SurvivalProbability,
            "C complete repeated targets retain distinct-battle multiplicity instead of a window union");
        Check(repeatedModel.ResolveSurvival(new HashSet<string> { "N.Neow", "R.Relic" }).SurvivalProbability == repeatedModel.Survival.SurvivalProbability,
            "Rewards-neutral authored C preserves its marginal after separate modeled Coverage");
        var queryRepeated = CombatRewardProbabilityEstimator.Estimate(SearchSelectivityInput.From(Query(repeatedNeutralPrefix)));
        Check(queryRepeated.Probability is > 0 && queryRepeated.Probability == repeatedModel.Survival.SurvivalProbability,
            "Complete positive C uses the same distinct-battle probability for query and unit survival");
        foreach (var passed in new[] { new HashSet<string> { "W.World" }, new HashSet<string> { "A.AncientOption", "E.EventResult" } })
            Check(repeatedModel.ResolveSurvival(passed).SurvivalProbability == repeatedModel.Survival.SurvivalProbability,
                "Unrelated passed W/A/E observations do not invalidate a covered physical C marginal");
        var repeatedReplay = new CombatRewardReplay((Query(repeatedNeutralPrefix)).Model);
        var oneReplay = new CombatRewardReplay((Query(oneExistential)).Model);
        for (ulong root = 0; root < 1024; root++)
        {
            Check(repeatedReplay.Opening(root, 0).EquivalentTo(repeatedReplay.Opening(root, 1)), "Neutral prefix pickups have identical C arrival");
            Check(!repeatedReplay.Matches(root) || oneReplay.Matches(root), "Complete repeated matching is a subset of the weaker one-target window union");
        }
        Check(Query(repeatedNeutralPrefix).Model.CompiledSearch.NormalizedQuery.CombatCardRewards!.Count == 2,
            "Probability-only matcher projection preserves authored Exact Query");
        var oneDrawPrefix = repeatedNeutralPrefix with
        {
            RequiredBonesCombination = [BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.ArcaneScroll],
            CombatCardRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered, [new ModelKey("CARD", "BENCH_CHARACTER_0")])
        };
        var oneDrawReplay = new CombatRewardReplay((Query(oneDrawPrefix)).Model);
        Check(Model(oneDrawPrefix).Survival.IsResolved, "Single forced-Rare prefix has a fixed-draw C marginal");
        for (ulong root = 0; root < 1024; root++)
            Check(oneDrawReplay.Opening(root, 0).EquivalentTo(oneDrawReplay.Opening(root, 1)), "One-draw prefix commutes with zero-draw pickup");
        Check(gpuOnly.Stages[0].ExpectedFilteringWorkUnitsPerInput is > 0,
            "Existing Family planner consumes C logical cost");

        ModelKey bones = BaseGameModelKeys.Relics.NeowsBones, arcane = BaseGameModelKeys.Relics.ArcaneScroll,
            scroll = BaseGameModelKeys.Relics.ScrollBoxes, large = BaseGameModelKeys.Relics.LargeCapsule,
            small = BaseGameModelKeys.Relics.SmallCapsule;
        var anyFilter = basis with { NeowRoute = new(bones), RequiredBonesCombination = [scroll, arcane] };
        var any = new CombatRewardReplay((Query(anyFilter)).Model);
        var anyModel = Model(anyFilter);
        Check(anyModel.Survival.IsResolved && anyModel.ExpectedFilteringCost!.ExpectedFilteringWorkUnitsPerInput > 0,
            "C authored prefix uses the fixed declared probability model and retains full cost");
        Check(anyModel.ResolveSurvival(new HashSet<string> { "E.EventResult" }).SurvivalProbability == anyModel.Survival.SurvivalProbability,
            "Unrelated Coverage does not modify the fixed C probability model");
        var ab = new CombatRewardReplay((Query(anyFilter with { RequiredBonesAcquisitionOrder = [scroll, arcane] })).Model);
        var ba = new CombatRewardReplay((Query(anyFilter with { RequiredBonesAcquisitionOrder = [arcane, scroll] })).Model);
        var partial = new CombatRewardReplay((Query(basis with { NeowRoute = new(bones), RequiredBonesCombination = [large] })).Model);
        var direct = new CombatRewardReplay((Query(basis with { NeowRoute = new(large) })).Model);
        Check(any.RouteCount == 2 && ab.RouteCount == 1 && ba.RouteCount == 1 && partial.RouteCount == 2,
            "Fixed authored pairs retain their order; a partial pair owns two actual opening alternatives");
        CheckUnavailableObservedFixture(direct, "Solo direct Capsule");
        CheckUnavailableObservedFixture(partial, "Solo partial Bones Capsule");
        int differing = 0, branchOnly = 0, retained = 0;
        for (ulong ordinal = 0; ordinal < (ulong)Math.Max(1024, count); ordinal++)
        {
            ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            var rng = new Beta110FastRng(unchecked(root + XxHash64.Hash("rewards"u8, 0)));
            Check(neutral.Opening(root, 0).Rewards.CaptureCheckpoint() == rng.CaptureCheckpoint(), "Neutral root RNG");
            Check(direct.Matches(root) && partial.Matches(root),
                "Missing actual Capsule/companion observations cannot become a synthetic prefix rejection");
            var a = ab.Opening(root, 0); var b = ba.Opening(root, 0);
            if (!a.EquivalentTo(b)) differing++;
            bool pa = ab.Matches(root), pb = ba.Matches(root), actual = any.Matches(root);
            if (pa != pb) branchOnly++;
            if (actual) retained++;
            Check(actual == (pa || pb), "AnyOrder must OR both authored predecessors");
            var donor = Beta110CombatRewardFastStage.Evaluate(a, ab.Plan, ab.Catalog, 10);
            Check(donor.Status == (pa ? Beta110CombatRewardRouteProjectionStatus.ExactProjection : Beta110CombatRewardRouteProjectionStatus.Rejected),
                "Donor numeric parity");
        }
        Check(differing > 0 && branchOnly > 0 && retained > 0, "Need real order-dependent acceptance witnesses");

        // Force only the two authored grants in the independent Core fixture;
        // compare both acquisition continuations, irrespective of offer order.
        var bonesAuthority = authority.WithEffectAuthority(authority.EffectAuthority! with { BonesEligibleRelics = [scroll, arcane] });
        var bonesRequest = Query(anyFilter);
        bonesRequest = bonesRequest with { Model = bonesRequest.Model with { CompiledSearch = bonesRequest.Model.CompiledSearch with {
            Context = context with { Authority = bonesAuthority } } } };
        var bonesReplay = new CombatRewardReplay(bonesRequest.Model);
        for (ulong ordinal = 0; ordinal < 64; ordinal++)
        {
            ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            var projection = Beta111NeowEffectProjector.ProjectFromRootHash(bones, PredictionPrecision.Exact, bonesAuthority, root);
            var routes = projection.OpeningRewardContinuations!.Routes;
            Check(routes.Count == 2, "Core Bones fixture requires both pickup orders");
            for (int route = 0; route < 2; route++)
            {
                var ids = bonesReplay.Plan.OpeningConsumption.OrderedRelicIds;
                byte first = ids[route == 0 ? 0 : 1];
                ModelKey firstKey = first == Beta110FastRelicCatalog.ScrollBoxes ? scroll : arcane;
                var core = routes.Single(r => r.Route.AcquisitionOrder.First(k => k != bones) == firstKey).RewardsRngState!;
                var checkpoint = bonesReplay.Opening(root, route).Rewards.CaptureCheckpoint();
                Check(checkpoint.S0 == core.S0 && checkpoint.S1 == core.S1 && checkpoint.S2 == core.S2 && checkpoint.S3 == core.S3,
                    "Core Bones prefix continuation parity");
            }
        }

        ModelKey pw = new("RELIC", "PRAYER_WHEEL");
        var grouped = new NeowStructuredEffectSearchCondition(bones, NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [pw]);
        var groupedFilter = basis with { NeowRoute = new(bones), RequiredBonesCombination = [small, large], StructuredNeowEffects = [grouped] };
        foreach (var capsuleFilter in new[] {
            dropFilters[0] with { NeowRoute = new(small) },
            dropFilters[0] with { NeowRoute = new(large) },
            dropFilters[0] with { NeowRoute = new(bones), RequiredBonesCombination = [small, large],
                StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "ANCHOR")] }] },
            dropFilters[0] with { NeowRoute = new(bones), RequiredBonesCombination = [small, large],
                StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "WHITE_BEAST_STATUE")] }] } })
        {
            var product = RolltheSpire2.Search.Selectivity.CombatRewardProbabilityEstimator.Estimate(
                RolltheSpire2.Search.Selectivity.SearchSelectivityInput.From(Query(capsuleFilter)));
            bool authoredWbs = capsuleFilter.StructuredNeowEffects.Any(c => c.OutputKeys.Any(k => k.Entry == "WHITE_BEAST_STATUE"));
            Check(product.IsPriced && product.Probability is double mass && Math.Abs(mass - (authoredWbs ? 0 : .6)) < 1e-6,
                "Capsule empty slots do not penalize P(C); authored grouped WBS still applies");
        }
        var groupedC = new CombatRewardReplay((Query(groupedFilter)).Model);
        Check(groupedC.Plan.ExplicitContext.AdditionalCardRewardCount == 1, "Grouped PW impact missing");
        var noPw = new CombatRewardReplay((Query(groupedFilter with { StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "ANCHOR")] }] })).Model);
        Check(noPw.Plan.ExplicitContext.IsEmpty, "Grouped empty slots cannot guess PW");
        var noImpactFilter = groupedFilter with { StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "ANCHOR")] }] };
        double noImpactWork = Model(noImpactFilter).AnalyticalCost.WorkUnitsPerInput;
        Check(Model(groupedFilter).AnalyticalCost.WorkUnitsPerInput > noImpactWork, "C PW extra groups increase full cost");
        Check(Model(groupedFilter with { StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "WHITE_BEAST_STATUE")] }] })
            .AnalyticalCost.WorkUnitsPerInput < noImpactWork, "C WBS omits potion decision draws");
        Check(Model(groupedFilter with { StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "LASTING_CANDY")] }] })
            .AnalyticalCost.WorkUnitsPerInput > noImpactWork, "C Candy adds second-battle power work");
        foreach (var targets in new ModelKey[][] { [pw], [new("RELIC", "WHITE_BEAST_STATUE")],
            [new("RELIC", "LASTING_CANDY")], [pw, new("RELIC", "WHITE_BEAST_STATUE"), new("RELIC", "LASTING_CANDY")] })
        {
            var impactFilter = groupedFilter with { StructuredNeowEffects = [grouped with { OutputKeys = targets }] };
            var impactRequest = Query(impactFilter);
            var impactReplay = new CombatRewardReplay(impactRequest.Model);
            CheckPreparedCardStage(impactReplay, "Authored impacts: " + string.Join(',', targets.Select(t => t.Entry)));
            Check(CombatRewardSieveProbability.FixedAuthoredPrefixDrawCount(
                impactRequest.Model, impactReplay.Plan) == 3, "Known reward impacts do not change Capsule prefix consumption");
            var survival = Model(impactFilter).ResolveSurvival(new HashSet<string> { "N.Neow", "R.Relic" });
            var declared = CombatRewardProbabilityEstimator.EstimateOptimizedConditions(SearchSelectivityInput.From(impactRequest));
            Check(declared.Probability is double probability && Math.Abs(probability - 4d / 9d) < 1e-10,
                "Declared card/potion impacts preserve their authored gold marginal");
            Check(impactReplay.ConservativelyKeeps && survival.SurvivalProbability == 1,
                "Missing real bag keeps the actual physical envelope distinct from the declared gold marginal");
        }
        var wbsNoDrop = groupedFilter with { NormalCombatRewardConditions = [],
            StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", "WHITE_BEAST_STATUE")] }],
            CombatPotionRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered, [noDrop]) };
        Check(CombatRewardProbabilityEstimator.EstimateOptimizedConditions(SearchSelectivityInput.From(Query(wbsNoDrop)))
            .Probability == 0, "Authored WBS makes no-drop impossible in its declared probability model");
        Check(Model(wbsNoDrop).ResolveSurvival(new HashSet<string> { "N.Neow", "R.Relic" })
            .SurvivalProbability == 1, "Declared impossibility does not invent missing bag/route observation authority");
        Check(!CombatRewardSieveProbability.HasModeledRewardImpact(
            groupedC.Plan.ExplicitContext with { InfluenceFlags = Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation }),
            "Unknown continuation must not acquire a modeled impact quote");
        var dedup = new CombatRewardReplay((Query(groupedFilter with { StructuredNeowEffects = [grouped,
            grouped with { SourceRelicKey = large, Kind = NeowStructuredConditionKind.ExactSingle }] })).Model);
        Check(dedup.Plan.ExplicitContext.AdditionalCardRewardCount == 1 && dedup.Plan.ExplicitContext.SourceRelicKeys.Length == 1,
            "Grouped/source-specific impact dedup");
        var future = new CombatRewardReplay((Query(basis with { RelicSequenceConditions = [new(RelicSequenceKind.Rare,
            SearchSequenceRangeMode.ExactSlot, 1, new([pw], [], []))] })).Model);
        Check(future.Plan.ExplicitContext.IsEmpty, "Future relic sequence target is not owned");
        foreach (string entry in OpeningCombatRewardImpactAdapterRegistry.SupportedRelicEntries)
        {
            var impact = new CombatRewardReplay((Query(groupedFilter with { StructuredNeowEffects = [grouped with { OutputKeys = [new("RELIC", entry)] }] })).Model);
            Check(impact.Plan.ExplicitContext.SourceRelicKeys.Single().Entry == entry, "Registered explicit impact " + entry);
            _ = impact.Matches(123456);
        }

        // Independent Core predictor, not a second copy of the Fast numeric loop.
        int coreCases = 0;
        for (ulong ordinal = 0; ordinal < 16; ordinal++)
        foreach (ModelKey source in new[] { BaseGameModelKeys.Relics.LavaRock, arcane, large })
        {
            ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            var projection = Beta111NeowEffectProjector.ProjectFromRootHash(source, PredictionPrecision.Exact, authority, root);
            var choice = new NeowChoiceResult(0, source, PredictionPrecision.Exact, projection.Capability, projection.Precision,
                projection.EffectGroups, projection.Warnings, "fixture", projection.EvidenceCode,
                new(true, null, "sts2", SourceAuthority.AuditedStaticExact, IdentityResolutionStatus.Exact),
                OpeningRewardContinuations: projection.OpeningRewardContinuations);
            var prediction = NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, root,
                Beta110SeedCodec.FormatOrdinal(ordinal), 0, 10, [choice], authority.EffectAuthority, authority.WorldAuthority);
            var battles = prediction.Routes.Single().Battles;
            var prefix = source == BaseGameModelKeys.Relics.LavaRock ? NeowSearchFilter.Empty : NeowSearchFilter.Empty with { NeowRoute = new(source) };
            // Large's actual nested impacts are intentionally excluded from C's
            // empty slots. Use it for RNG-prefix parity, not strict world recall.
            if (source == large)
            {
                Check(direct.ConservativelyKeeps && direct.Matches(root),
                    "Seed-bound legacy Core Capsule fixture does not supply the current runtime bag authority");
                continue;
            }
            var sixPrediction = NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, root,
                Beta110SeedCodec.FormatOrdinal(ordinal), 0, 10, [choice], authority.EffectAuthority, authority.WorldAuthority,
                NormalCombatRewardProjectionRequest.RichAnalysis with { BattleCount = 6 });
            var six = sixPrediction.Routes.Single().Battles;
            Check(six.Count == 6, "C6 full Predictor horizon");
            Check(six.Take(3).SelectMany(b => b.Cards.Select(c => c.CardKey)).SequenceEqual(
                battles.SelectMany(b => b.Cards.Select(c => c.CardKey))), "C1-3 prefix unchanged");
            foreach (int horizon in new[] {4,5,6})
            foreach (var order6 in Enum.GetValues<CombatRewardSequenceOrderMode>())
            {
                var targets = six.Take(horizon).Select(b => (ModelKey?)b.Cards[0].CardKey).ToArray();
                if (order6 == CombatRewardSequenceOrderMode.Unordered) Array.Reverse(targets);
                var q6 = prefix with { CombatCardRewardSequence = new(horizon, order6, targets) };
                var rq6 = Query(q6); var replay6 = new CombatRewardReplay(rq6.Model);

                Check(replay6.Plan.MaximumBattleOrdinal == horizon, "C6 Fast actually covers requested horizon");
                Check(replay6.Matches(root), "C6 source positive + unordered assignment " + source + horizon + order6);
                // A late-only target must not silently reduce to the first three battles.
                targets = new ModelKey?[horizon]; targets[^1] = six[horizon-1].Cards[0].CardKey;
                Check(new CombatRewardReplay((Query(prefix with { CombatCardRewardSequence = new(horizon,
                    CombatRewardSequenceOrderMode.Ordered, targets) })).Model).Matches(root), "C6 late target");
                targets[^1] = authority.EffectAuthority.CharacterRewardPool!.First(c =>
                    !six[horizon-1].Cards.Any(actual => actual.CardKey == c.CardKey)).CardKey;
                Check(!new CombatRewardReplay((Query(prefix with { CombatCardRewardSequence = new(horizon,
                    CombatRewardSequenceOrderMode.Ordered, targets) })).Model).Matches(root), "C6 late rejection");
            }
            Check(battles.Count == 3, "Core predictor failed: " + source + ":" + prediction.IssueCode + ":" +
                string.Join('|', prediction.Routes.Select(r => r.IssueCode)));
            foreach (CombatRewardSequenceOrderMode order in Enum.GetValues<CombatRewardSequenceOrderMode>())
            foreach (int slot in new[] { 0, 1, 2 })
            {
                ModelKey target = battles[slot].Cards[0].CardKey;
                ModelKey?[] slots = [null, null, null]; slots[slot] = target;
                var typed = prefix with { CombatCardRewardSequence = new(3, order, slots),
                    CombatPotionRewardSequence = new(3, order, battles.Select(b => new CombatPotionRewardSlotSearchCondition(
                        b.Potion.Generated ? CombatPotionSlotRequirement.DropSpecific : CombatPotionSlotRequirement.NoDrop, b.Potion.PotionKey)).ToArray()) };
                var request = Query(typed);
                Check(new CombatRewardReplay(request.Model).Matches(root), "Core Card/Potion positive " + source + order + slot);
                coreCases++;
                // Neutral slots and DropAny exercise the same already-advanced stream.
                var potions = typed.CombatPotionRewardSequence!.Slots.Select(p => p.Requirement == CombatPotionSlotRequirement.DropSpecific
                    ? new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.DropAny, null) : p).ToArray();
                potions[(slot + 1) % 3] = new(CombatPotionSlotRequirement.Neutral, null);
                Check(new CombatRewardReplay((Query(typed with { CombatPotionRewardSequence = new(3, order, potions) })).Model).Matches(root), "DropAny/neutral");
                // Three copies require three different battle witnesses.
                if (order == CombatRewardSequenceOrderMode.Unordered)
                    Check(new CombatRewardReplay((Query(prefix with { CombatCardRewardSequence = new(3, order, [target, target, target]) })).Model).Matches(root) ==
                        battles.All(b => (b.CardRewards.Count > 0 ? b.CardRewards.SelectMany(r => r.Cards) : b.Cards).Any(c => c.CardKey == target)),
                        "Complete duplicate matching agrees with all three Core battle witnesses");
            }
            foreach (int length in new[] { 1, 2 })
            {
                var typed = prefix with { CombatCardRewardSequence = new(length, CombatRewardSequenceOrderMode.Ordered,
                    battles.Take(length).Select(b => (ModelKey?)b.Cards[0].CardKey).ToArray()) };
                Check(new CombatRewardReplay((Query(typed)).Model).Matches(root), "One/two combat progression");
            }
            Check(new CombatRewardReplay((Query(prefix with {
                CombatCardRewardSequence = new(3, CombatRewardSequenceOrderMode.Unordered,
                    battles.Reverse().Select(b => (ModelKey?)b.Cards[0].CardKey).ToArray()),
                CombatPotionRewardSequence = new(3, CombatRewardSequenceOrderMode.Unordered,
                    battles.Select(b => new CombatPotionRewardSlotSearchCondition(b.Potion.Generated ?
                        CombatPotionSlotRequirement.DropSpecific : CombatPotionSlotRequirement.NoDrop, b.Potion.PotionKey)).ToArray())
            })).Model).Matches(root), "Card/Potion permutations remain independent");
            var incorrect = prefix with { NormalCombatRewardConditions = [new(3, empty, NormalCombatPotionRequirement.Any, empty,
                battles[2].Gold + 1, battles[2].Gold + 1)] };
            Check(!new CombatRewardReplay((Query(incorrect)).Model).Matches(root), "Core negative third battle gold");
        }

        var requestC = Query(basis);
        var mixed = FamilyExecutionCoordinator.Plan(Query(basis with { NeowRoute = new(arcane) }));
        Check(mixed.Stages.Any(s => s.Coverage.Contains("N.Neow")) && mixed.Stages.Any(s => s.Coverage.Contains("C.CombatReward")), "N+C registration");
        Check(FamilyExecutionCoordinator.ResolveExecutionWindowSize(requestC, mixed.OrderedFamilies) ==
            FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize, "C must not underfill mixed GPU batch");
        Check(!CombatRewardFamily.TryCreate((Query(NeowSearchFilter.Empty with {
            CombatCardRewardSequence = new(3, CombatRewardSequenceOrderMode.Ordered, [null, null, null]),
            CombatPotionRewardSequence = new(3, CombatRewardSequenceOrderMode.Ordered,
                Enumerable.Repeat(new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null), 3).ToArray()) })).Model,FilterExecutionPreferences.From((Query(NeowSearchFilter.Empty with {
            CombatCardRewardSequence = new(3, CombatRewardSequenceOrderMode.Ordered, [null, null, null]),
            CombatPotionRewardSequence = new(3, CombatRewardSequenceOrderMode.Ordered,
                Enumerable.Repeat(new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null), 3).ToArray()) })).RunOptions), out _), "All-empty typed slots");
        CombatRewardFamily.TryCreate(requestC.Model,FilterExecutionPreferences.From(requestC.RunOptions), out var family);
        family = family!.CpuAlternatives.First();
        var batch = new SearchBatch(39303, 257);
        var input = FamilyCandidateSet.FromSortedAbi1(batch, [0, 2, 9, 55, 128, 256]);
        var execution = new FilterRuntimeServices(false);
        var familyExecution = family.CreateExecution!(family, execution);
        var output = familyExecution.FilterAsync(execution, input, default).GetAwaiter().GetResult();
        var expected = input.EnumerateLogicalOrdinals().Where(i => neutral.Matches(
            Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(batch.GlobalCandidate(i)))));
        Check(output.EnumerateLogicalOrdinals().SequenceEqual(expected), "Compact nonzero batch ABI1");
        var denseBatch = new SearchBatch(39303, 8201);
        var denseInput = FamilyCandidateSet.Dense(denseBatch);
        var denseOutput = familyExecution.FilterAsync(execution, denseInput, default).GetAwaiter().GetResult();
        Check(denseOutput.EnumerateLogicalOrdinals().SequenceEqual(denseInput.EnumerateLogicalOrdinals().Where(i => neutral.Matches(
            Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(denseBatch.GlobalCandidate(i)))))), "Dense CPU chunk boundary ABI1");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Throws<OperationCanceledException>(() => familyExecution.FilterAsync(execution, input, cancelled.Token).GetAwaiter().GetResult(), "Cancelled C must throw");
        var injected = new InvalidOperationException("injected");
        var cpu = new CpuFilterExecution(family with { PhysicalRevision = "test" }, Beta111Profile.Instance,
            _ => throw injected, numericalRoots: false);
        bool retainedFault = false;
        try { cpu.FilterAsync(execution, input, default).GetAwaiter().GetResult(); }
        catch (FamilyCpuMatchException error)
        {
            retainedFault = ReferenceEquals(error.InnerException, injected) && error.FamilyId == "C.CombatReward" &&
                error.Batch == batch && error.LogicalOrdinal == 0 && error.GlobalOrdinal == batch.BatchBase;
        }
        Check(retainedFault, "Matcher failure retains original cause and candidate identity without catch-all Keep");
        Throws<InvalidOperationException>(() => cpu.FilterAsync(execution, input, default).GetAwaiter().GetResult(), "Faulted invocation cannot Keep");
        Throws<InvalidOperationException>(() => Beta110CombatRewardFastStage.EvaluateForFamily(neutral.Opening(1, 0),
            neutral.Plan, neutral.Catalog with { CombatRewardCardPool = new([], [], []) }, 10), "Empty numeric pool must fail");
        var unverified = neutral.Plan with { CardPoolAuthorityExact = false, PotionPoolAuthorityExact = false };
        Check(Beta110CombatRewardFastStage.EvaluateForFamily(neutral.Opening(1, 0), unverified, neutral.Catalog, 10).Status !=
            Beta110CombatRewardRouteProjectionStatus.ConservativeKeep, "Verification flags cannot Gate computation");
        familyExecution.DisposeAsync(execution).GetAwaiter().GetResult();
        cpu.DisposeAsync(execution).GetAwaiter().GetResult();
        execution.DisposeAsync().GetAwaiter().GetResult();
        if (exportGpu) ExportGpu(Query, terminalCardFixtures);
        foreach(var replay in new[]{neutral,any,ab,ba,partial,direct,groupedC,noPw})
        {
            CheckPreparedCardStage(replay, "Existing opening/route fixture");
            if (replay.ConservativelyKeeps) continue; // retention/cause asserted by CheckPreparedCardStage
            if(replay.Plan.Predicates.Any(p=>p.IsAnyBattle || p.HasCardPredicate)) continue;
            for(ulong root=0;root<1024;root++)
            for(int route=0;route<replay.RouteCount;route++)
            {
                var opening=replay.Opening(root,route);
                var full=Beta110CombatRewardFastStage.EvaluateForFamily(opening,replay.Plan,replay.Catalog,10);
                var prefix=Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening,replay.Plan,replay.Catalog,10);
                Check(prefix.Status==full.Status,"C prefix preserves authored arrival/modifiers/every route");
            }
        }
        var captured=ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        CheckWholeQueryParentProbability(captured.Context);
        foreach(string id in new[]{"C-specific1","C-specific2","C-specific3","C-late3","C-drop3","C-nodrop3"})
        {
            var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/cpu-numerical-closure-v1/{id}.json");
            var r=SearchPreparationCompiler.Compile(SearchCompiler.Compile(w.Query,captured.Context),w.Options).Plan!;
            var replay=new CombatRewardReplay(r.Model);
            CheckPreparedCardStage(replay, id);
            Check(replay.CanUsePotionPrefix,"C fixed potion prefix admitted");
            for(ulong root=0;root<65536;root++) Check(replay.MatchesPotionPrefix(root)==replay.Matches(root),"C prefix population parity");
            _checks+=RelicCpuChecks.CheckEnvelope(r,replay.MatchesPotionPrefix,replay.Matches);
        }
        Console.WriteLine($"C0 PASS checks={_checks};coreCardPotionCases={coreCases};orderDifferentRoots={differing};branchOnlyMatches={branchOnly};anyOrderOutput={retained};ABI1=PASS;failFast=PASS");
        return 0;
    }

    private static void CheckPreparedCardSelection()
    {
        // Independent expected results: filter the original ordered entries,
        // retaining duplicates. This does not reuse the index/rank algorithm.
        ushort[][] sources = [[], [7], [7, 7], [9, 2, 9, 5, 2, 0, 5, 8],
            [3, 1, 4, 2, 0], [10, 11, 10, 12, 13, 11, 14, 15]];
        ushort[] alphabet = [0, 2, 5, 8, 9, 99];
        List<ushort[]> selections = [[], [7], [7, 7], [3, 1, 4, 2], [3, 1, 4, 2, 0],
            [9, 2, 5, 8, 0, 1, 4, 6, 9], [10, 11, 12, 13, 14, 15]];
        void Enumerate(ushort[] selected, int index)
        {
            if (index == selected.Length) { selections.Add(selected.ToArray()); return; }
            foreach (ushort id in alphabet) { selected[index] = id; Enumerate(selected, index + 1); }
        }
        for (int length = 1; length <= 4; length++) Enumerate(new ushort[length], 0);

        void Compare(CombatRewardCardSelection.Pool indexed, ushort[] source, ushort[] selected, string label)
        {
            ushort[] expected = source.Where(id => !selected.Contains(id)).ToArray();
            Check(indexed.CountAvailable(selected) == expected.Length, "Indexed card count: " + label);
            for (int rank = 0; rank < expected.Length; rank++)
                Check(indexed.SelectAvailable(selected, rank) == expected[rank], "Indexed ordered card rank: " + label + "/" + rank);
            if (expected.Length == 0)
                Check(indexed.SelectAvailable(selected, 0) == Beta110FastDenseId.Invalid, "Indexed exhausted card pool: " + label);
        }
        foreach (var source in sources)
        {
            var snapshot = source.ToArray();
            var indexed = new CombatRewardCardSelection.Pool(source);
            foreach (var selected in selections) Compare(indexed, source, selected, "source=" + string.Join(',', source));
            Check(source.SequenceEqual(snapshot), "Prepared selector preserves source order and repeated entries");
        }
        var rarities = new Beta110FastCardPool([9, 2, 9, 5, 0], [2, 8, 8, 6], [5, 1, 5, 4]);
        var prepared = new CombatRewardCardSelection.CardPool(rarities);
        ushort[][] lanes = [rarities.Common, rarities.Uncommon, rarities.Rare];
        foreach (var selected in selections)
        {
            int expectedTotal = lanes.Sum(source => source.Count(id => !selected.Contains(id)));
            Check(prepared.CountAvailable(selected) == expectedTotal, "Indexed exclusions span rarity lanes");
            for (byte rarity = 1; rarity <= 3; rarity++)
                Compare(prepared.Rarity(rarity), lanes[rarity - 1], selected, "rarity=" + rarity);
        }
        Console.WriteLine($"C_CARD_INDEX_PARITY sources={sources.Length};selectionCases={selections.Count};maxSelected={selections.Max(s => s.Length)};allLegalRanks=true;duplicates=true;crossRarity=true");
    }

    private static void CheckPreparedCardStage(CombatRewardReplay replay, string label)
    {
        if (replay.ConservativelyKeeps)
        {
            CheckUnavailableObservedFixture(replay, label);
            return;
        }
        var prepared = new CombatRewardCardSelection(replay.Catalog);
        for (ulong root = 0; root < 64; root++)
        for (int route = 0; route < replay.RouteCount; route++)
        {
            var opening = replay.Opening(root, route);
            var original = Beta110CombatRewardFastStage.EvaluateForFamily(opening, replay.Plan, replay.Catalog, 10);
            var indexed = Beta110CombatRewardFastStage.EvaluateForFamily(opening, replay.Plan, replay.Catalog, 10, prepared);
            Check(indexed == original, "Prepared/null card Stage oracle: " + label + ";route=" + route);
            if (replay.CanUsePotionPrefix)
            {
                var originalPrefix = Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening, replay.Plan, replay.Catalog, 10);
                var indexedPrefix = Beta110CombatRewardFastStage.EvaluatePotionPrefixForFamily(opening, replay.Plan, replay.Catalog, 10, prepared);
                Check(indexedPrefix == originalPrefix, "Prepared/null potion-prefix Stage oracle: " + label + ";route=" + route);
            }
        }
    }

    private static void CheckUnavailableObservedFixture(CombatRewardReplay replay, string label)
    {
        Check(replay.ConservativelyKeeps && replay.ConservativeOpeningReason ==
            "ObservedOpeningBag:UnsupportedOrIncompleteRelicRuntimeAuthority", "Retained fixture has its actual missing-bag cause: " + label);
        Check(!replay.UsesObservedOpening, "Unavailable actual opening is never claimed as numeric parity: " + label);
        for (ulong root = 0; root < 16; root++)
            Check(replay.Matches(root) && replay.MatchesReference(root), "Unobserved fixture retains roots for Exact: " + label);
        Throws<InvalidOperationException>(() => replay.Opening(0, 0), "Unavailable opening cannot manufacture a checkpoint: " + label);
    }

    private static PreparedSearch ForPartySlot(PreparedSearch request, int slot, ModelKey? uncommonReplacement = null, IReadOnlyList<NeowEffectRelicSnapshot>? exactBagPool = null)
    {
        var effects = request.Model.Authority.EffectAuthority!;
        effects = effects with
        {
            CharacterRewardPool = effects.CharacterRewardPool!.Select((c, i) => c with
                { IsMultiplayerOnly = i is 0 or 1 or 2 or 10 or 11 or 16 }).ToArray(),
            PotionPool = effects.PotionPool!.Select((p, i) => p with { IsMultiplayerOnly = i == 0 }).ToArray()
        };
        var shared = effects.SharedRelicPoolSource!.Select(r => r.RelicKey.Entry switch
        {
            "BENCH_UNCOMMON_RELIC" => r with { RelicKey = uncommonReplacement ?? new("RELIC", "PRAYER_WHEEL") },
            "BENCH_RARE_RELIC" => r with { RelicKey = new("RELIC", "LASTING_CANDY") },
            _ => r
        }).Append(new NeowEffectRelicSnapshot(new("RELIC", "WHITE_BEAST_STATUE"), 4, EffectRelicRarity.Rare,
            NestedRelicEffectKind.NoTrackedImmediateEffect)).ToArray();
        effects = effects with { SharedRelicPoolSource = exactBagPool ?? shared,
            CharacterRelicPoolSource = exactBagPool is null ? effects.CharacterRelicPoolSource : [] };
        shared = effects.SharedRelicPoolSource!.ToArray();
        Beta109RelicBucketSnapshot[] Buckets(IEnumerable<NeowEffectRelicSnapshot> relics)
        {
            var buckets = relics.GroupBy(r => string.IsNullOrEmpty(r.RarityCode) ? r.Rarity.ToString() : r.RarityCode)
                .Select(g => new Beta109RelicBucketSnapshot(g.Key, g.Select(r => r.RelicKey).ToArray(), true)
                { OrderedEntries = g.Select(r => new Beta109RelicBucketEntrySnapshot(r.RelicKey, r.IsAllowedInShops, true)).ToArray() }).ToArray();
            // The controlled pool has no Shop relics. Represent that known-empty
            // lane explicitly for probability consumers of all four initial lanes.
            return new[] { "Common", "Uncommon", "Rare", "Shop" }.Select(name => buckets.FirstOrDefault(bucket => bucket.BucketId == name)
                ?? new Beta109RelicBucketSnapshot(name, [], true) { OrderedEntries = [] }).ToArray();
        }
        var playerBuckets = Buckets(shared.Concat(effects.CharacterRelicPoolSource!).Where(r =>
            r.Rarity is EffectRelicRarity.Common or EffectRelicRarity.Uncommon or EffectRelicRarity.Rare or EffectRelicRarity.Shop).DistinctBy(r => r.RelicKey));
        var world = request.Model.Authority.WorldAuthority!;
        world = world with { Beta109Generation = world.Beta109Generation! with
        {
            GameMode = WorldGameMode.Multiplayer, IsMultiplayer = true, PlayerCount = 4,
            // This fixture constructs every bucket's shop-eligibility entry
            // explicitly below; the generic world fixture does not assert it.
            RelicShopEligibilityAuthorityExact = true,
            PersonalPlayerSlot = slot, PartyBossDiscoveryExact = true,
            LobbyPlayers = Enumerable.Range(0, 4).Select(i => new Beta109LobbyPlayerSnapshot(i, request.Model.CharacterKey, false, true)).ToArray(),
            SharedRelicBuckets = Buckets(shared), PlayerRelicBuckets = playerBuckets,
            PartyRelicBuckets = Enumerable.Repeat<IReadOnlyList<Beta109RelicBucketSnapshot>>(playerBuckets, 4).ToArray()
        } };
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(request.Model.CharacterKey, request.Model.Ascension, slot, 4)
            .WithEffectAuthority(effects)
            .WithWorldAuthority(world);
        return request with { Model = request.Model with { CompiledSearch = request.Model.CompiledSearch with {
            Context = request.Model.CompiledSearch.Context with { Authority = authority } } } };
    }

    private static void CheckRealNeutralWitness(Func<NeowSearchFilter, PreparedSearch> query)
    {
        var impossible = NeowSearchFilter.Empty with { NormalCombatRewardConditions =
            [new(1, ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, 0)] };
        var source = query(impossible);
        // The other fixtures deliberately exercise the numeric donor with no
        // product route policy. This group uses the real prepared no-N contract.
        SearchModel Product(SearchModel model) => model with { CombatRewardRoutePolicy = new(
            CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed, CombatRewardExactRoutePolicy.UnpinnedVerifyNeutralRealRoute) };
        int soloRejected = 0, soloRetained = 0, partyRejected = 0, partyRetained = 0;
        foreach (int slot in new[] { -1, 0, 1, 3 })
        {
            var model = Product((slot < 0 ? source : ForPartySlot(source, slot)).Model);
            var replay = new CombatRewardReplay(model);
            var proof = replay.NeutralWitness ?? throw new Exception("Real no-N policy needs a neutral witness proof");
            Check(proof.Available && proof.NeutralMask != 0, "Captured direct producer/identity foundation is available");
            Check(proof.RequiresAllOffers == (slot >= 0), "Party no-N preserves per-choice unresolved scope");
            Check(!proof.AllowsRejection(Beta110FastRelicCatalog.ArcaneScroll, Beta110FastRelicCatalog.SmallCapsule,
                Beta110FastRelicCatalog.NeowsBones), "No pristine synthetic route substitutes for a real neutral offer");
            Check(proof.AllowsRejection(Beta110FastRelicCatalog.GoldenPearl, Beta110FastRelicCatalog.NutritiousOyster,
                Beta110FastRelicCatalog.CursedPearl), "Independent known neutral direct opening permits rejection");
            Check(proof.AllowsRejection(Beta110FastRelicCatalog.GoldenPearl, Beta110FastRelicCatalog.ArcaneScroll,
                Beta110FastRelicCatalog.CursedPearl) == (slot < 0), "One neutral offer cannot erase a party choice Unknown");
            foreach (ulong ordinal in new[] { 0UL, 16UL, 128UL })
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                for (byte id = 0; id < 30; id++)
                {
                    if ((proof.NeutralMask & (1u << id)) == 0) continue;
                    var projected = new NeowEffectProjectionEngine(Beta111Profile.Instance, "neutral-witness-source",
                        root, model.Authority).Project(Beta110FastRelicCatalog.KeyOf(id));
                    Check(projected.OpeningRewardContinuations?.Routes is { Count: 1 } routes &&
                        OpeningRewardNeutralityClassifier.Classify(routes[0]) == OpeningRewardNeutrality.ProvablyNeutral,
                        "Every admitted direct producer really publishes an exact neutral continuation");
                }
            }
            for (ulong ordinal = 0; ordinal < 129; ordinal++)
            {
                ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
                var real = ModernNeowIdentityPredictor.PredictModernCore(root, model.Authority, Beta111Profile.Instance,
                    model.Authority.IsBeta111NeowIdentityAuthorityExact);
                Check(real.ExactAuthority && !real.UnknownEligibilityAffectedPool, "Core real offer identity is exact");
                bool Trusted(ModelKey key) => CombatRewardNeutralWitness.HasUnconditionalNeutralProducer(model.ProfileId, key);
                bool expected = slot < 0 ? real.RelicKeys.Any(Trusted) : real.RelicKeys.All(Trusted);
                Check(proof.AllowsRejection(root) == expected, "Numeric witness binds the actual Core offered identities and slot");
                Check(replay.Matches(root) == !expected, "Only a proved real opening can reject the impossible reward condition");
                if (slot < 0) { if (expected) soloRejected++; else soloRetained++; }
                else { if (expected) partyRejected++; else partyRetained++; }
            }
            var gpu = CombatRewardGpuPlan.TryCreate(model, replay, out var issue) ?? throw new Exception(issue);
            Check(gpu.UsesNeutralWitness && gpu.Meta[51] == (slot < 0 ? 1u : 2u) &&
                gpu.Meta[52] == proof.NeutralMask && gpu.Revision(false).Contains(CombatRewardNeutralWitness.Revision),
                "GPU binds the same immutable witness mask, quantifier and physical revision");
            Check(gpu.ShaderSource().Contains("bool c_numeric_keep_root(uint64_t root)") &&
                !CombatRewardNeutralWitness.HasUnconditionalNeutralProducer(model.ProfileId, BaseGameModelKeys.Relics.NewLeaf),
                "Only the guarded body injects proof code; deck-dependent neutral producers are not assumed available");
            var missingAuthority = model.Authority.WithEffectAuthority(model.Authority.EffectAuthority! with {
                AuthoritySource = NeowEffectAuthoritySource.Missing });
            var missing = new CombatRewardNeutralWitness(model with { CompiledSearch = model.CompiledSearch with {
                Context = model.CompiledSearch.Context with { Authority = missingAuthority } } }, replay.Catalog);
            Check(!missing.Available && missing.Issue == "RealNeutralOpeningEffectFoundationUnavailable" &&
                !missing.AllowsRejection(0), "Missing producer foundation retains with its actual cause");
            var uncertainAuthority = model.Authority.WithEffectAuthority(model.Authority.EffectAuthority! with {
                SourceAuthority = SourceAuthority.ModdedRuntimeBestEffort });
            var uncertain = new CombatRewardNeutralWitness(model with { CompiledSearch = model.CompiledSearch with {
                Context = model.CompiledSearch.Context with { Authority = uncertainAuthority } } }, replay.Catalog);
            Check(!uncertain.Available && uncertain.Issue == "RealNeutralOpeningEffectAuthorityNotExact",
                "Model admission does not substitute for neutral-classifier exact continuation authority");
        }
        Check(soloRejected > 0 && soloRetained > 0 && partyRejected > 0 && partyRetained > 0,
            "Real witness guard preserves useful rejection and genuinely unverified roots");
        var pinned = query(impossible with { NeowRoute = new(BaseGameModelKeys.Relics.GoldenPearl) }).Model;
        pinned = pinned with { CombatRewardRoutePolicy = new(CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed,
            CombatRewardExactRoutePolicy.PinnedRealRoute) };
        Check(!new CombatRewardReplay(pinned).UsesNeutralWitness, "Pinned numerical replay is unchanged by the no-N guard");
        var pinnedReplay = new CombatRewardReplay(pinned);
        var pinnedGpu = CombatRewardGpuPlan.TryCreate(pinned, pinnedReplay, out var pinnedIssue) ?? throw new Exception(pinnedIssue);
        Check(!pinnedGpu.UsesNeutralWitness && pinnedGpu.Meta[51] == 0 && pinnedGpu.Meta[52] == 0 &&
            !pinnedGpu.ShaderSource().Contains("c_numeric_keep_root"), "Pinned shader has no reachable or disabled witness wrapper");
    }

    private static void CheckPartyDistinctAssignments(Func<NeowSearchFilter, PreparedSearch> query)
    {
        ModelKey card = new("CARD", "BENCH_CHARACTER_0");
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        var neutral = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null);
        foreach (int window in new[] { 1, 2, 6 })
        {
            // Requiring the same identity in every slot has exactly one distinct
            // battle assignment: the ordered query is an independent oracle.
            var cards = NeowSearchFilter.Empty with { CombatCardRewardSequence = new(window,
                CombatRewardSequenceOrderMode.Unordered, Enumerable.Repeat((ModelKey?)card, window).ToArray()),
                CombatPotionRewardSequence = new(6, CombatRewardSequenceOrderMode.Ordered,
                    [neutral, neutral, neutral, neutral, neutral, noDrop]) };
            var unordered = new CombatRewardReplay((ForPartySlot(query(cards), 3)).Model);
            var ordered = new CombatRewardReplay((ForPartySlot(query(cards with { CombatCardRewardSequence =
                cards.CombatCardRewardSequence! with { OrderMode = CombatRewardSequenceOrderMode.Ordered } }), 3)).Model);
            Check(unordered.Plan.CardAssignmentWindow == window && unordered.Plan.CardAssignmentTargets.Length == window,
                "MP card assignment preserves its own window and duplicate targets");
            var potions = NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(window,
                CombatRewardSequenceOrderMode.Unordered, Enumerable.Repeat(noDrop, window).ToArray()),
                CombatCardRewardSequence = new(6, CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]) };
            var potionUnordered = new CombatRewardReplay((ForPartySlot(query(potions), 3)).Model);
            var potionOrdered = new CombatRewardReplay((ForPartySlot(query(potions with { CombatPotionRewardSequence =
                potions.CombatPotionRewardSequence! with { OrderMode = CombatRewardSequenceOrderMode.Ordered } }), 3)).Model);
            for (ulong root = 0; root < 64; root++)
            {
                Check(unordered.Matches(root) == ordered.Matches(root), "MP duplicate cards require distinct battles inside the card window");
                Check(potionUnordered.Matches(root) == potionOrdered.Matches(root), "MP duplicate potions require distinct battles inside the potion window");
            }
        }
    }

    private static void CheckProbabilityAssignmentWindows(Func<NeowSearchFilter, PreparedSearch> query)
    {
        int before = _checks;
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        var drop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.DropAny, null);
        var neutral = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.Neutral, null);
        foreach (int slot in new[] { -1, 0, 3 })
        {
            PreparedSearch Request(NeowSearchFilter filter) => slot < 0 ? query(filter) : ForPartySlot(query(filter), slot);
            double P(NeowSearchFilter filter, string label)
            {
                var result = CombatRewardProbabilityEstimator.Estimate(SearchSelectivityInput.From(Request(filter)));
                Check(result.IsPriced && result.Probability is double p && double.IsFinite(p),
                    $"C assignment probability slot={slot};{label};{result.EvidenceCode}");
                double value = result.Probability!.Value;
                Console.WriteLine(FormattableString.Invariant($"C_DP_VECTOR slot={slot};case={label};p={value:G17}"));
                return value;
            }
            void Near(double actual, double expected, string label, double tolerance = 1e-12) =>
                Check(Math.Abs(actual - expected) < tolerance,
                    $"C assignment oracle slot={slot};{label};actual={actual:G17};expected={expected:G17}");
            var pool = Request(NeowSearchFilter.Empty).Model.Authority.EffectAuthority!.CharacterRewardPool!;
            ModelKey a = pool[0].CardKey, b = pool[1].CardKey;
            NeowSearchFilter Cards(int window, CombatRewardSequenceOrderMode order, params ModelKey?[] targets) =>
                NeowSearchFilter.Empty with { CombatCardRewardSequence = new(window, order, targets) };

            // Identical requirements fill every battle. There is only one possible
            // assignment event, so the ordered probability is an independent oracle.
            foreach (int horizon in new[] { 1, 2, 3, 6 })
            {
                var repeated = Cards(horizon, CombatRewardSequenceOrderMode.Unordered,
                    Enumerable.Repeat((ModelKey?)a, horizon).ToArray());
                double unordered = P(repeated, $"C{horizon}-repeat-unordered");
                double ordered = P(repeated with { CombatCardRewardSequence = repeated.CombatCardRewardSequence! with
                    { OrderMode = CombatRewardSequenceOrderMode.Ordered } }, $"C{horizon}-repeat-ordered");
                Near(unordered, ordered, "same identity still needs every distinct battle", 1e-15);
                Check(unordered > 0, "Repeated common card remains possible in the captured solo/party pool");
            }

            // [A,B] over two battles is AB union BA, with the overlapping reward
            // histories subtracted once. Legacy CardAll describes the intersection
            // directly and does not use the unordered assignment representation.
            double ab = P(Cards(2, CombatRewardSequenceOrderMode.Ordered, a, b), "C2-AB");
            double ba = P(Cards(2, CombatRewardSequenceOrderMode.Ordered, b, a), "C2-BA");
            var all = new ModelKeySetFilter([], [a, b], []);
            double intersection = P(NeowSearchFilter.Empty with { NormalCombatRewardConditions =
                [new(1, all, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, null),
                 new(2, all, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, null, null)] }, "C2-both-each-battle");
            double pair = P(Cards(2, CombatRewardSequenceOrderMode.Unordered, a, b), "C2-AB-unordered");
            Near(pair, ab + ba - intersection, "unordered two-target union includes overlapping card groups exactly once");
            Check(intersection > 0, "The union oracle exercises actual overlapping multi-card rewards");

            // Drop/no-drop are disjoint and exhaustive per battle. Sum the masses
            // of ordered histories that satisfy the multiset, without a subset DP.
            for (int horizon = 1; horizon <= 4; horizon++)
            {
                var histories = new double[1 << horizon];
                for (int history = 0; history < histories.Length; history++)
                {
                    var slots = Enumerable.Range(0, horizon).Select(battle =>
                        (history & (1 << battle)) == 0 ? noDrop : drop).ToArray();
                    histories[history] = P(NeowSearchFilter.Empty with { CombatPotionRewardSequence =
                        new(horizon, CombatRewardSequenceOrderMode.Ordered, slots) }, $"P{horizon}-history-{history}");
                }
                Near(histories.Sum(), 1, "ordered potion histories partition the complete probability space");
                var patterns = new List<(int NoDrop, int Drop)> { (1, 0), (horizon, 0) };
                if (horizon >= 2) patterns.AddRange([(2, 0), (1, 1)]);
                foreach (var pattern in patterns.Distinct())
                {
                    var slots = Enumerable.Repeat(noDrop, pattern.NoDrop).Concat(Enumerable.Repeat(drop, pattern.Drop))
                        .Concat(Enumerable.Repeat(neutral, horizon - pattern.NoDrop - pattern.Drop)).ToArray();
                    double expected = histories.Where((_, history) =>
                        System.Numerics.BitOperations.PopCount((uint)history) >= pattern.Drop &&
                        horizon - System.Numerics.BitOperations.PopCount((uint)history) >= pattern.NoDrop).Sum();
                    double actual = P(NeowSearchFilter.Empty with { CombatPotionRewardSequence =
                        new(horizon, CombatRewardSequenceOrderMode.Unordered, slots) },
                        $"P{horizon}-unordered-N{pattern.NoDrop}-D{pattern.Drop}");
                    Near(actual, expected, "unordered repeated and mixed potion requirements match disjoint ordered histories");
                }
            }

            foreach (var (cardWindow, potionWindow) in new[] { (2, 4), (4, 2), (3, 2) })
            {
                var cardTargets = new ModelKey?[cardWindow];
                cardTargets[0] = a;
                cardTargets[1] = cardWindow == 3 ? b : a;
                var cards = Cards(cardWindow, CombatRewardSequenceOrderMode.Unordered, cardTargets);
                var potionSlots = Enumerable.Repeat(neutral, potionWindow).ToArray();
                potionSlots[0] = noDrop;
                potionSlots[1] = noDrop;
                var potions = NeowSearchFilter.Empty with { CombatPotionRewardSequence =
                    new(potionWindow, CombatRewardSequenceOrderMode.Unordered, potionSlots) };
                double cardMass = P(cards, $"C{cardWindow}-P{potionWindow}-card-part");
                double potionMass = P(potions, $"C{cardWindow}-P{potionWindow}-potion-part");
                double joint = P(cards with { CombatPotionRewardSequence = potions.CombatPotionRewardSequence },
                    $"C{cardWindow}-P{potionWindow}-joint");
                Near(joint, cardMass * potionMass, "card and potion obligations use their own windows before and after the shorter domain ends");
                Check(joint > 0, "Independent-window fixture retains positive successful histories");
            }

            var candidates = pool.Select(card => (card.CardKey, card.Rarity, card.CardType)).ToArray();
            ModelKey rareAttack = pool.First(card => card.Rarity == EffectCardRarity.Rare && card.CardType == EffectCardType.Attack).CardKey;
            ModelKey rarePower = pool.First(card => card.Rarity == EffectCardRarity.Rare && card.CardType == EffectCardType.Power).CardKey;
            foreach (var (name, target, wheel, candy, possible) in new[] {
                ("neutral-rare-attack", rareAttack, false, false, false),
                ("Wheel-rare-attack", rareAttack, true, false, true),
                ("Candy-rare-power", rarePower, false, true, true),
                ("Candy-rare-attack", rareAttack, false, true, false) })
            {
                var filter = Cards(2, CombatRewardSequenceOrderMode.Ordered, null, target);
                if (wheel || candy) filter = filter with { NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                    RequiredBonesCombination = [new ModelKey("RELIC", wheel ? "PRAYER_WHEEL" : "LASTING_CANDY")] };
                double probability = P(filter, name);
                Check((probability > 0) == possible && CombatRewardProbabilityEstimator.CanAuthorCards(10,
                    candidates, [null, target], false, wheel, candy) == possible,
                    "Explicit modifiers preserve zero/positive probability and independent authoring reachability");
                if (wheel || candy)
                {
                    var repeated = filter with { CombatCardRewardSequence = new(2, CombatRewardSequenceOrderMode.Unordered, [a, a]) };
                    double unordered = P(repeated, name + "-repeat-unordered");
                    double ordered = P(repeated with { CombatCardRewardSequence = repeated.CombatCardRewardSequence! with
                        { OrderMode = CombatRewardSequenceOrderMode.Ordered } }, name + "-repeat-ordered");
                    Near(unordered, ordered, "modified reward groups still pay at most one repeated requirement per battle", 1e-15);
                }
            }
        }
        Console.WriteLine($"C_PROBABILITY_ASSIGNMENT_WINDOWS_PASS checks={_checks - before};soloParty=true;" +
            "oracles=ordered-union-and-disjoint-potion-histories-and-domain-factorization;notNativeParity=true");
    }

    private static void CheckWholeQueryParentProbability(SearchContext context)
    {
        ModelKey small = BaseGameModelKeys.Relics.SmallCapsule, large = BaseGameModelKeys.Relics.LargeCapsule,
            bones = BaseGameModelKeys.Relics.NeowsBones, anchor = new("RELIC", "ANCHOR");
        var bases = new[] {
            SearchQuery.Empty with { OpeningRoute = new(small) },
            SearchQuery.Empty with { OpeningRoute = new(bones), OpeningRouteRelicRequirement = new(bones, [small, large]) },
            SearchQuery.Empty with { OpeningRoute = new(large), StructuredOpeningEffects = [new(large,
                NeowStructuredConditionKind.ExactUnorderedPair, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [anchor])],
                RelicSequenceConstraints = [new(RelicSequenceKind.Common, SearchSequenceRangeMode.ExactSlot, 1, new([], [anchor], []))] }
        };
        foreach (var parent in bases)
        {
            var full = parent with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
                [new(CombatPotionSlotRequirement.NoDrop, null)]) };
            var parentMass = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.Compile(parent, context)));
            var fullMass = JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.Compile(full, context)));
            Check(parentMass.JointlyPriced && parentMass.Probability is > 0 && fullMass.JointlyPriced &&
                fullMass.Probability is double actual && Math.Abs(actual - parentMass.Probability!.Value * .6) < 1e-12,
                "Full query pays the N/R same-bag parent once and composes the declared neutral C conditional: " +
                parent.OpeningRoute!.RouteRelicKey + ";" + fullMass.EvidenceCode);
        }
    }

    private static void CheckDeclaredRewardProbabilityPolicy(Func<NeowSearchFilter, PreparedSearch> query)
    {
        int before = _checks;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
        var basis = NeowSearchFilter.Empty with { CombatPotionRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered, [noDrop]) };
        ModelKey bones = BaseGameModelKeys.Relics.NeowsBones, small = BaseGameModelKeys.Relics.SmallCapsule,
            large = BaseGameModelKeys.Relics.LargeCapsule, anchor = new("RELIC", "ANCHOR"),
            white = new("RELIC", "WHITE_BEAST_STATUE"), wheel = new("RELIC", "PRAYER_WHEEL"), candy = new("RELIC", "LASTING_CANDY");
        NeowStructuredEffectSearchCondition Nested(ModelKey source, params ModelKey[] targets) => new(source,
            source == small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, targets);
        foreach (int slot in new[] { -1, 0, 3 })
        {
            PreparedSearch Request(NeowSearchFilter filter) => slot < 0 ? query(filter) : ForPartySlot(query(filter), slot);
            SearchSelectivityEstimate Estimate(NeowSearchFilter filter) => CombatRewardProbabilityEstimator.Estimate(SearchSelectivityInput.From(Request(filter)));
            void Probability(NeowSearchFilter filter, double expected, string label)
            {
                var result = Estimate(filter);
                Check(result.IsPriced && result.Probability is double value && Math.Abs(value - expected) < 1e-12,
                    $"Declared C profile slot={slot}: {label};p={result.Probability:G17};cause={result.EvidenceCode}");
            }
            Probability(basis, .6, "neutral first-battle no-drop vector");
            foreach (var opening in new[] { bones, small, large })
            {
                var unspecified = basis with { NeowRoute = new(opening) };
                Probability(unspecified, .6, "unspecified opening effects remain neutral: " + opening.Entry);
                var queue = unspecified with { RelicSequenceConditions = [new(RelicSequenceKind.Rare, SearchSequenceRangeMode.ExactSlot, 1, new([white], [], []))] };
                var result = Estimate(queue);
                Check(result.Probability == Estimate(unspecified).Probability && !result.ConditionedOnDomains.Contains(SearchSelectivityDomain.Relic),
                    "ordinary initial R observations cannot infer an unlisted held C effect");
            }
            foreach (var (opening, capsule, acquisition) in new[] {
                (bones, false, "Bones"), (small, true, "Capsule"), (bones, true, "Bones nested Capsule") })
            foreach (var (keys, expected, label) in new[] {
                (new ModelKeySetFilter([], [white], []), 0d, "All"),
                (new ModelKeySetFilter([white], [], []), 0d, "singleton Any"),
                (new ModelKeySetFilter([white, anchor], [], []), .6d, "multi Any") })
            {
                var filter = basis with { NeowRoute = new(opening),
                    CapsuleContainedRelics = capsule ? keys : ModelKeySetFilter.Empty,
                    BonesRelics = capsule ? ModelKeySetFilter.Empty : keys };
                Probability(filter, expected, acquisition + " " + label + " preserves guaranteed-acquisition semantics");
                var request = Request(filter);
                var guaranteed = ExplicitOpeningRelicFacts.EnumerateGuaranteed(request.Model.CompiledSearch.NormalizedQuery);
                Check(guaranteed.Contains(white) == (expected == 0) &&
                    request.Model.CompiledSearch.Relations.Any(relation => relation.Kind == SemanticRelationKind.StateInfluence &&
                        relation.Source.Kind == SemanticFactKind.RewardInfluenceRelic && relation.Source.Key == white &&
                        relation.Target.Kind == SemanticFactKind.CombatReward) == (expected == 0),
                    "canonical state influence and declared product acquisition facts agree on guaranteed effects");
            }
            Probability(basis with { NeowRoute = new(small), StructuredNeowEffects = [Nested(small, white)] }, 0,
                "explicit nested White Beast applies without a latent mixture");
            Probability(basis with { NeowRoute = new(bones), RequiredBonesCombination = [white] }, 0,
                "explicit Bones White Beast applies while unspecified companion remains neutral");

            var pool = Request(NeowSearchFilter.Empty).Model.Authority.EffectAuthority!.CharacterRewardPool!;
            ModelKey rarePower = pool.First(card => card.Rarity == EffectCardRarity.Rare && card.CardType == EffectCardType.Power).CardKey;
            ModelKey rareAttack = pool.First(card => card.Rarity == EffectCardRarity.Rare && card.CardType == EffectCardType.Attack).CardKey;
            NeowSearchFilter Card(ModelKey target) => NeowSearchFilter.Empty with { CombatCardRewardSequence = new(2,
                CombatRewardSequenceOrderMode.Ordered, [null, target]) };
            Probability(Card(rareAttack), 0, "chronological A7+ neutral pity excludes B2 Rare");
            var wheelCard = Card(rareAttack) with { NeowRoute = new(small), StructuredNeowEffects = [Nested(small, wheel)] };
            var candyPower = Card(rarePower) with { NeowRoute = new(small), StructuredNeowEffects = [Nested(small, candy)] };
            Check(Estimate(wheelCard).Probability is > 0 && Estimate(candyPower).Probability is > 0,
                "explicit Wheel extra group and Candy Power card retain distinct reward generation effects");
            Probability(Card(rareAttack) with { NeowRoute = new(small), StructuredNeowEffects = [Nested(small, candy)] }, 0,
                "Candy cannot generate a Rare Attack as its extra Power");
            foreach (string unsupported in new[] { "WING_CHARM", "PRISMATIC_GEM" })
            {
                var key = new ModelKey("RELIC", unsupported);
                foreach (var explicitUnknown in new[] {
                    Card(rarePower) with { NeowRoute = new(small), StructuredNeowEffects = [Nested(small, key)] },
                    Card(rarePower) with { NeowRoute = new(bones), CapsuleContainedRelics = new([], [key], []) },
                    Card(rarePower) with { NeowRoute = new(bones), CapsuleContainedRelics = new([key], [], []) } })
                {
                    var unknown = Estimate(explicitUnknown);
                    Check(!unknown.IsPriced && unknown.Probability is null && unknown.EvidenceCode.Contains("UnsupportedExplicitHeldInfluence", StringComparison.Ordinal),
                        "explicit unmodeled card impact remains Unknown for solo and party: " + unsupported);
                }
            }
            if (slot >= 0)
            {
                var capsuleRequest = Request(basis with { NeowRoute = new(small) });
                Check(CombatRewardFamily.TryPrepareParty(capsuleRequest.Model,FilterExecutionPreferences.From(capsuleRequest.RunOptions), null, null, out var physical), "actual party Capsule C remains registered");
                var family = physical!;
                var declaredCapsule = CombatRewardProbabilityEstimator.EstimateOptimizedConditions(SearchSelectivityInput.From(capsuleRequest));
                Check(family.Body.Survival.SurvivalProbability == declaredCapsule.Probability &&
                    family.Body.ResolveSurvival(new HashSet<string> { "W.World", "A.AncientOption", "E.EventResult" }).SurvivalProbability == declaredCapsule.Probability,
                    "Scheduling retains its declared approximation separately from actual held replay");
                Check(family.Replay.UsesObservedOpening, "Physical party Capsule owns its actual held replay");
                bool observedHeldInfluence = false;
                for (ulong root = 0; root < 128; root++)
                {
                    var actual = family.Replay.Opening(root, 0);
                    if (!actual.ExactProjectionAuthority)
                        Check(family.Replay.Matches(root), "Unknown actual Capsule effects are retained independently of scheduling probability");
                    else observedHeldInfluence |= actual.InfluenceFlags != Beta110CombatRewardInfluenceFlags.None;
                }
                Check(observedHeldInfluence, "Unlisted real Capsule held influences participate in physical filtering");
                var mp = pool.First(card => card.IsMultiplayerOnly && card.Rarity == EffectCardRarity.Common);
                Check(Estimate(NeowSearchFilter.Empty with { CombatCardRewardSequence = new(1, CombatRewardSequenceOrderMode.Ordered, [mp.CardKey]) }).Probability is > 0,
                    "product C includes the actual captured multiplayer-only card pool");
            }
        }
        Console.WriteLine($"C_DECLARED_PROFILE_POLICY_PASS checks={_checks - before};elapsedMs={watch.Elapsed.TotalMilliseconds:F3};soloParty=true;actualSieveSeparate=true;notSearchThroughput=true");
    }

    private static void CheckCapsuleProbabilityConjunctions(Func<NeowSearchFilter, PreparedSearch> query)
    {
        ModelKey large = BaseGameModelKeys.Relics.LargeCapsule, small = BaseGameModelKeys.Relics.SmallCapsule,
            bones = BaseGameModelKeys.Relics.NeowsBones, a = new("RELIC", "PRAYER_WHEEL"), b = new("RELIC", "LASTING_CANDY");
        NeowStructuredEffectSearchCondition Row(ModelKey source, params ModelKey[] targets) => new(source,
            source == small ? NeowStructuredConditionKind.ExactSingle : NeowStructuredConditionKind.ExactUnorderedPair,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, targets);
        SearchSelectivityInput Input(params NeowStructuredEffectSearchCondition[] rows) => SearchSelectivityInput.From(
            ForPartySlot(query(NeowSearchFilter.Empty with { NeowRoute = new(large), StructuredNeowEffects = rows }), 3));
        var pair = CapsuleRelicProbabilityEstimator.Estimate(Input(Row(large, a, b)), false);
        var split = CapsuleRelicProbabilityEstimator.Estimate(Input(Row(large, a), Row(large, b), Row(large, a)), false);
        Check(pair.IsPriced && pair.Probability > 0 && split.Probability == pair.Probability,
            $"Direct Capsule repeated same-source rows conjoin without paying the parent or target twice; " +
            $"pair={pair.Probability:G17}/{pair.EvidenceCode}/{pair.Notes}; split={split.Probability:G17}/{split.EvidenceCode}/{split.Notes}");
        var duplicateInput = Input(Row(large, a, a) with { AllowDuplicateOutputs = true });
        Check(duplicateInput.CompiledSearch.NormalizedQuery.StructuredOpeningEffects.Single().OutputKeys.Count == 2,
            "Capsule multiplicity fixture explicitly opts into duplicate-output semantics");
        var impossible = CapsuleRelicProbabilityEstimator.Estimate(duplicateInput, false);
        Check(impossible.Probability == 0,
            $"Capsule duplicate target keeps multiplicity in the without-replacement bag; " +
            $"p={impossible.Probability:G17};code={impossible.EvidenceCode};notes={impossible.Notes}");
        var grouped = new NeowStructuredEffectSearchCondition(bones, NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
            NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [a]);
        var input = Input(Row(large, a));
        Check(CapsuleRelicProbabilityEstimator.TryEstimateBonesNestedCapsules(input, [Row(large, a)], false,
            [small, large], out double source, out _, out _) && source > 0, "Bones source probability available");
        Check(CapsuleRelicProbabilityEstimator.TryEstimateBonesNestedCapsules(input, [grouped, Row(large, a), Row(large, a)], false,
            [small, large], out double combined, out _, out _) && Math.Abs(source - combined) < 1e-12,
            "Grouped plus source Capsule conjunction retains source scope and removes redundant grouped fact");
        var broad = Enumerable.Range(0, 24).Select(i => new ModelKey("RELIC", "ABSENT_CAPSULE_" + i)).Append(a).ToArray();
        SearchSelectivityInput Legacy(ModelKeySetFilter keys) => SearchSelectivityInput.From(ForPartySlot(query(
            NeowSearchFilter.Empty with { NeowRoute = new(large), CapsuleContainedRelics = keys }), 3));
        Check(CapsuleBagProbabilityEstimator.TryEstimateConjunction(Input(Row(large, a)), [large], false,
            out double direct, out _, out _) && direct > 0, "Shared Capsule extraction probability available");
        Check(CapsuleBagProbabilityEstimator.TryEstimateConjunction(Legacy(new(broad, [], [])), [large], false,
            out double any, out _, out _) && Math.Abs(any - direct) < 1e-12,
            "Broad legacy Capsule Any is an aggregate event, not a distinguished-identity budget");
        Check(CapsuleBagProbabilityEstimator.TryEstimateConjunction(Legacy(new([], [], broad)), [large], false,
            out double ban, out _, out _) && Math.Abs(any + ban - 1) < 1e-12,
            "Broad legacy Capsule Ban complements Any on the same extraction event");
        foreach (string heldOnly in new[] { "WING_CHARM", "PRISMATIC_GEM" })
        {
            ModelKey key = new("RELIC", heldOnly);
            SearchSelectivityInput Renamed(ModelKey target) => SearchSelectivityInput.From(ForPartySlot(query(
                NeowSearchFilter.Empty with { NeowRoute = new(large), StructuredNeowEffects = [Row(large, target)] }), 3, target));
            Check(CapsuleBagProbabilityEstimator.TryEstimateConjunction(Renamed(key), [large], false, out double actual, out _, out _) &&
                CapsuleBagProbabilityEstimator.TryEstimateConjunction(Renamed(new("RELIC", "ANCHOR")), [large], false, out double neutral, out _, out _) &&
                actual > 0 && Math.Abs(actual - neutral) < 1e-12,
                "Pure N/R bag extraction is unchanged by an identity-renamed unsupported held C effect: " + heldOnly);
        }
        ModelKey circlet = BaseGameModelKeys.OrdinaryRelics.Circlet;
        NeowEffectRelicSnapshot[] exhaustedPool = [new(b, 0, EffectRelicRarity.Rare, NestedRelicEffectKind.NoTrackedImmediateEffect)];
        SearchSelectivityInput Exhausted(NeowSearchFilter filter) => SearchSelectivityInput.From(ForPartySlot(query(filter), 3, exactBagPool: exhaustedPool));
        double ExhaustedMass(NeowSearchFilter filter, IReadOnlyList<ModelKey> sources, bool withQueue = false)
        {
            Check(CapsuleBagProbabilityEstimator.TryEstimateConjunction(Exhausted(filter), sources, withQueue,
                out double mass, out _, out string issue), "Artificial exhausted-lane bag is explicit authority: " + issue);
            return mass;
        }
        var fallback = NeowSearchFilter.Empty with { NeowRoute = new(large), StructuredNeowEffects = [Row(large, b, circlet)] };
        Check(ExhaustedMass(fallback, [large]) == 1, "Empty Common/Uncommon fall forward to the sole Rare then Circlet");
        Check(ExhaustedMass(fallback with { StructuredNeowEffects = [Row(large, b, b) with { AllowDuplicateOutputs = true }] }, [large]) == 0,
            "A real relic is not returned twice from an exhausted without-replacement lane");
        var ordered = fallback with { NeowRoute = new(bones), RequiredBonesAcquisitionOrder = [small, large],
            StructuredNeowEffects = [Row(small, b), Row(large, circlet, circlet) with { AllowDuplicateOutputs = true }],
            RelicSequenceConditions = [new(RelicSequenceKind.Rare, SearchSequenceRangeMode.ExactSlot, 1, new([], [b], []))] };
        Check(ExhaustedMass(ordered, [small, large], true) == 1 &&
            ExhaustedMass(ordered with { RequiredBonesAcquisitionOrder = [large, small] }, [small, large], true) == 0,
            "Two Capsules share their consumed bag and explicit source order, including repeatable Circlet fallback");

    }

    private static void CheckLargeFiniteConjunctions()
    {
        var keys = Enumerable.Range(0, 80).Select(i => new ModelKey("RELIC", "FINITE_" + i)).ToArray();
        var pool = keys.Select((k, i) => new FiniteSequenceProbabilitySolver.Item(k, i % 2)).ToArray();
        double P(params FiniteSequenceProbabilitySolver.Constraint[] conditions)
        {
            Check(FiniteSequenceProbabilitySolver.TrySolve(pool, conditions, out var result, out var issue),
                "Equivalent-class finite sequence solve: " + issue);
            return result.Probability;
        }
        var any = new FiniteSequenceProbabilitySolver.Constraint(SearchSequenceRangeMode.FirstN, 2,
            new(keys.Take(70).ToArray(), [], []));
        Check(Math.Abs(P(any) - (1 - 10d * 9 / (80 * 79))) < 1e-12, "Large Any matches hypergeometric complement");
        Check(Math.Abs(P(any with { Keys = new([], [], keys.Take(70).ToArray()) }) - 10d * 9 / (80 * 79)) < 1e-12,
            "Large Ban preserves without-replacement counts");
        Check(Math.Abs(P(any with { Keys = new([], keys.Skip(78).ToArray(), keys.Take(70).ToArray()) }) - 2d / (80 * 79)) < 1e-12,
            "Equivalent-class All requires every member, not one member of the class");
        Check(Math.Abs(P(any with { Partition = 1 }) - (1 - 45d * 44 / (80 * 79))) < 1e-12,
            "Equivalent classes retain source partition observations");
        var exact = new FiniteSequenceProbabilitySolver.Constraint(SearchSequenceRangeMode.ExactSlot, 2,
            new(keys.Skip(10).ToArray(), [], []));
        double brute = 0;
        for (int first = 0; first < 80; first++) for (int second = 0; second < 80; second++)
            if (first != second && (first < 70 || second < 70) && second >= 10) brute += 1d / (80 * 79);
        Check(Math.Abs(P(any, exact) - brute) < 1e-12, "Large overlapping FirstN and ExactSlot conjunction matches finite enumeration");
    }

    private static void CheckPartyOpeningContinuations(Func<NeowSearchFilter, PreparedSearch> query,
        NeowSearchFilter basis)
    {
        int actualHeldInfluences = 0, reliableCapsuleRoots = 0;
        for (int slot = 0; slot < 4; slot++)
        {
            var unselected = ForPartySlot(query(basis), slot);
            Check(!CombatRewardFamily.TryCreate(unselected.Model,FilterExecutionPreferences.From(unselected.RunOptions), out _), "Multiplayer cannot enter the solo C factory");
            Check(CombatRewardFamily.TryPrepareParty(unselected.Model,FilterExecutionPreferences.From(unselected.RunOptions), null, null, out var neutralFamily), "Unselected party opening uses neutral C");
            var neutralReplay = neutralFamily!.Replay;
            Check(!neutralReplay.ConservativelyKeeps && !neutralReplay.Plan.OpeningConsumption.HasReplay,
                "Neutral C is an effective sieve without invented opening RNG");
            foreach (var key in new[] { BaseGameModelKeys.Relics.SmallCapsule,
                BaseGameModelKeys.Relics.LargeCapsule })
            {
                var capsuleRequest = ForPartySlot(query(basis with { NeowRoute = new(key) }), slot);
                Check(CombatRewardFamily.TryPrepareParty(capsuleRequest.Model,FilterExecutionPreferences.From(capsuleRequest.RunOptions),
                    0, 0, out var capsuleFamily), "Actual Capsule party continuation registers");
                var capsule = capsuleFamily!.Replay;
                Check(!capsule.ConservativelyKeeps && capsule.UsesObservedOpening,
                    "Complete party bag owns the actual Capsule held observation");
                for (ulong root = 0; root < 32; root++)
                {
                    var opening = capsule.Opening(root, 0);
                    if (!opening.ExactProjectionAuthority)
                    {
                        Check(capsule.Matches(root), "Unresolved actual held effect retains its root without an invented prefix");
                        continue;
                    }
                    var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-c-actual-held", root,
                        capsuleRequest.Model.Authority) { AuthoredCapsuleEffects =
                        PartyInitialQuery.CapsuleEffectPremise(capsuleRequest.Model.CompiledSearch.NormalizedQuery) }.Project(key);
                    var snapshot = projection.OpeningRewardContinuations!.Routes.Single().RewardsRngState!;
                    var checkpoint = opening.Rewards.CaptureCheckpoint();
                    Check(snapshot is not null && checkpoint.S0 == snapshot.S0 && checkpoint.S1 == snapshot.S1 &&
                        checkpoint.S2 == snapshot.S2 && checkpoint.S3 == snapshot.S3,
                        "Reliable actual Capsule Rewards prefix agrees with independent Core projection");
                    ModelKey[] held = projection.EffectGroups.SelectMany(g => g.OrderedItems)
                        .Where(e => e.Kind == PredictedEffectKind.AddRelic && e.Relation == PredictedEffectRelation.NestedRelic &&
                            e.TargetKey.HasValue).Select(e => e.TargetKey!.Value).Distinct().ToArray();
                    var observedQuery = ForPartySlot(query(basis with { NeowRoute = new(key),
                        CapsuleContainedRelics = new([], held, []) }), slot);
                    var expectedContext = Beta110CombatRewardExplicitContextProjector.Project(observedQuery.Model.CompiledSearch);
                    Check(opening.InfluenceFlags == expectedContext.InfluenceFlags &&
                        opening.AdditionalCardRewardCount == expectedContext.AdditionalCardRewardCount &&
                        opening.FixedGoldAmount == expectedContext.FixedGoldAmount,
                        "Unlisted held effects agree with actual Core identities, not an authored neutral placeholder");
                    reliableCapsuleRoots++;
                    if (opening.InfluenceFlags != Beta110CombatRewardInfluenceFlags.None) actualHeldInfluences++;
                }
            }
            Check(CombatRewardFamily.TryPrepareParty((ForPartySlot(query(basis with { NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones) }), slot)).Model,FilterExecutionPreferences.From((ForPartySlot(query(basis with { NeowRoute = new(BaseGameModelKeys.Relics.NeowsBones) }), slot)).RunOptions),
                0, 0, out var unresolvedBones) && !unresolvedBones!.Replay.ConservativelyKeeps &&
                unresolvedBones!.Replay.UsesObservedOpening && !unresolvedBones.Replay.UsesQueryLiteralPartyOpening &&
                unresolvedBones.Replay.ObservedOpening!.DynamicBones && unresolvedBones.Replay.RouteCount == 2,
                "Unspecified Bones observes its real pair and both acquisition orders");

            foreach (var key in new[] { BaseGameModelKeys.Relics.CursedPearl, BaseGameModelKeys.Relics.ArcaneScroll,
                BaseGameModelKeys.Relics.ScrollBoxes, BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.MassiveScroll,
                BaseGameModelKeys.Relics.NewLeaf })
            {
                var request = ForPartySlot(query(basis with { NeowRoute = new(key) }), slot);
                bool kaleidoscope = key == BaseGameModelKeys.Relics.Kaleidoscope;
                Check(CombatRewardFamily.TryPrepareParty(request.Model,FilterExecutionPreferences.From(request.RunOptions), 11, null, out var family), "Direct authored party C registers");
                Check(CombatRewardFamily.TryPrepareParty(request.Model,FilterExecutionPreferences.From(request.RunOptions), null, null, out var unknownFamily),
                    "Fixed Rewards continuation supports unknown shared arrival");
                var replay = family!.Replay;
                var effects = request.Model.Authority.EffectAuthority!;
                Check(replay.Catalog.CombatRewardCardPool.TotalCount == effects.CharacterRewardPool!.Count(c =>
                    c.EligibleForPostCombatRewardByPoolMembership && c.IsUnlockedInCapturedPool &&
                    c.Rarity is EffectCardRarity.Common or EffectCardRarity.Uncommon or EffectCardRarity.Rare),
                    "Party combat keeps multiplayer-only cards in captured pool order");
                Check(replay.Catalog.CombatRewardPotionPool.TotalCount == effects.PotionPool!.Count,
                    "Party combat keeps multiplayer-only potions");
                for (ulong root = 0; root < 32; root++)
                {
                    var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, slot);
                    for (int draw = 0; draw < 11; draw++) rng.Niche.NextDouble();
                    var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-c-parity", root,
                        request.Model.Authority).Project(key, rng);
                    var continuation = projection.OpeningRewardContinuations!.Routes.Single();
                    var snapshot = continuation.RewardsRngState!;
                    var checkpoint = replay.Opening(root, 0, out var niche).Rewards.CaptureCheckpoint();
                    Check(snapshot is not null && checkpoint.S0 == snapshot.S0 && checkpoint.S1 == snapshot.S1 &&
                        checkpoint.S2 == snapshot.S2 && checkpoint.S3 == snapshot.S3, "Party C personal Rewards / shared Niche Core parity");
                    if (kaleidoscope || key == BaseGameModelKeys.Relics.NewLeaf)
                    {
                        var expectedNiche = continuation.NicheState!;
                        Check(niche is { } n && n.S0 == expectedNiche.S0 && n.S1 == expectedNiche.S1 && n.S2 == expectedNiche.S2 && n.S3 == expectedNiche.S3,
                            "Known party shared arrival is advanced without reset");
                    }
                    var unknown = unknownFamily!.Replay.Opening(root, 0, out var unknownNiche);
                    Check(unknownNiche is null && unknown.Rewards.CaptureCheckpoint() == checkpoint,
                        "Unknown Niche stays unknown while fixed Rewards draws match Core");
                }
            }
        }
        Check(reliableCapsuleRoots > 0 && actualHeldInfluences > 0,
            "Actual party Capsule fixtures exercise reliable non-neutral held observations");
        var bonesKey = BaseGameModelKeys.Relics.NeowsBones;
        var leafKey = BaseGameModelKeys.Relics.NewLeaf;
        var kaleidoKey = BaseGameModelKeys.Relics.Kaleidoscope;
        var bonesRequest = ForPartySlot(query(basis with { NeowRoute = new(bonesKey), RequiredBonesCombination = [leafKey, kaleidoKey] }), 3);
        var bonesAuthority = bonesRequest.Model.Authority.WithEffectAuthority(bonesRequest.Model.Authority.EffectAuthority! with { BonesEligibleRelics = [leafKey, kaleidoKey] });
        bonesRequest = bonesRequest with { Model = bonesRequest.Model with { CompiledSearch = bonesRequest.Model.CompiledSearch with {
            Context = bonesRequest.Model.CompiledSearch.Context with { Authority = bonesAuthority } } } };
        Check(CombatRewardFamily.TryPrepareParty(bonesRequest.Model,FilterExecutionPreferences.From(bonesRequest.RunOptions), 11, 0, out var bonesFamily), "Complete authored Bones party route registers");
        var bonesReplay = bonesFamily!.Replay;
        Check(bonesReplay.RouteCount == 2 && !bonesReplay.ConservativelyKeeps, "Any-order Bones is two effective ordered continuations");
        var actualRequest = ForPartySlot(query(basis with { NeowRoute = new(bonesKey) }), 3);
        actualRequest = actualRequest with { Model = actualRequest.Model with { CompiledSearch = actualRequest.Model.CompiledSearch with {
            Context = actualRequest.Model.CompiledSearch.Context with { Authority = bonesAuthority } } } };
        Check(CombatRewardFamily.TryPrepareParty(actualRequest.Model,FilterExecutionPreferences.From(actualRequest.RunOptions), null, null, out var actualFamily), "Actual-pair Bones admits unknown shared prefix");
        var actualReplay = actualFamily!.Replay;
        bool retained = false, rejected = false;
        for (ulong root = 0; root < 128; root++)
        {
            bool pass = actualReplay.Matches(root);
            retained |= pass; rejected |= !pass;
            var expected = new Beta110FastRng(unchecked(root + 3UL + XxHash64.Hash("rewards"u8, 0)));
            expected.ConsumeUnstableShuffle(2);
            for (int draw = 0; draw < 18; draw++) expected.NextDouble(); // actual Kaleidoscope companion
            Check(actualReplay.Opening(root, 0).Rewards.CaptureCheckpoint() == expected.CaptureCheckpoint(),
                "Unbound Bones observes the actual Kaleidoscope companion's fixed Rewards consumption");
        }
        Check(retained && rejected, "Observed actual-pair Bones performs an effective C sieve");
        for (ulong root = 0; root < 32; root++)
        for (int order = 0; order < 2; order++)
        {
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 3);
            for (int draw = 0; draw < 11; draw++) rng.Niche.NextDouble();
            rng.Rewards.NextInt(2); // Two-item offered-pair shuffle.
            foreach (byte id in order == 0 ? bonesReplay.Plan.OpeningConsumption.OrderedRelicIds : bonesReplay.Plan.OpeningConsumption.OrderedRelicIds.AsEnumerable().Reverse())
                new NeowEffectProjectionEngine(Beta111Profile.Instance, "party-c-bones-order", root, bonesAuthority)
                    .Project(Beta110FastRelicCatalog.KeyOf(id), rng);
            rng.Niche.NextInt(bonesAuthority.EffectAuthority!.GeneratedCursePool!.Count);
            var actual = bonesReplay.Opening(root, order, out var niche);
            var rewards = RewardsRngStateSnapshot.Capture(rng.Rewards);
            var expectedNiche = RewardsRngStateSnapshot.Capture(rng.Niche);
            var checkpoint = actual.Rewards.CaptureCheckpoint();
            Check(checkpoint.S0 == rewards.S0 && checkpoint.S1 == rewards.S1 && checkpoint.S2 == rewards.S2 && checkpoint.S3 == rewards.S3,
                "Bones ordered NewLeaf/Kaleidoscope Rewards continuation matches Core operators");
            Check(niche is { } n && n.S0 == expectedNiche.S0 && n.S1 == expectedNiche.S1 && n.S2 == expectedNiche.S2 && n.S3 == expectedNiche.S3,
                "Bones carries NewLeaf before Kaleidoscope and generates its curse after obtains");
        }
    }

    private sealed record TerminalCardFixture(string Name, CombatRewardReplay Replay, CombatRewardGpuPlan Plan,
        uint[]? Ordinals = null, bool ExpectedFault = false, ulong Start = 0);

    private sealed record OpeningPrefixFixture(string Name, CombatRewardReplay Replay, CombatRewardGpuPlan Plan,
        PreparedSearch Request,
        ulong Start = 0, bool ExpectedFault = false, bool RequireMixedResults = true);

    private static List<OpeningPrefixFixture> CheckOpeningPrefixHotLoop(Func<NeowSearchFilter, PreparedSearch> query,
        Func<NeowSearchFilter, PreparedSearch> queryA0)
    {
        var fixtures = new List<OpeningPrefixFixture>();
        ModelKey card = new("CARD", "BENCH_CHARACTER_0"), rare = new("CARD", "BENCH_CHARACTER_16");
        ModelKey bones = BaseGameModelKeys.Relics.NeowsBones, scroll = BaseGameModelKeys.Relics.ScrollBoxes,
            arcane = BaseGameModelKeys.Relics.ArcaneScroll, small = BaseGameModelKeys.Relics.SmallCapsule,
            large = BaseGameModelKeys.Relics.LargeCapsule;
        var basis = NeowSearchFilter.Empty with { CombatCardRewardSequence = new(6,
            CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]) };
        OpeningPrefixFixture Add(string name, NeowSearchFilter filter, bool eligible = true, bool party = false, ulong start = 0, bool a0 = false)
        {
            var request = (a0 ? queryA0 : query)(filter);
            if (party) request = ForPartySlot(request, 3);
            var replay = new CombatRewardReplay(request.Model, party ? 11 : 0);
            Check(!replay.ConservativelyKeeps, "Prefix fixture executes its authored model: " + name);
            var baseline = CombatRewardGpuPlan.TryCreate(request.Model, replay, out string issue, openingPrefixHotLoop: false,
                rareOnlyIdentityElision: false, assignmentEarlyRejection: false, unobservedFixedGold: false)
                ?? throw new Exception("Prefix baseline: " + name + ":" + issue);
            var trial = CombatRewardGpuPlan.TryCreate(request.Model, replay, out issue, openingPrefixHotLoop: true,
                rareOnlyIdentityElision: false, assignmentEarlyRejection: false, unobservedFixedGold: false)
                ?? throw new Exception("Prefix trial: " + name + ":" + issue);
            Check(!baseline.UsesHotLoop && !baseline.UsesOpeningPrefixHotLoop, "Prefix baseline retained: " + name);
            Check(trial.UsesOpeningPrefixHotLoop == eligible && trial.UsesHotLoop == eligible,
                "Prefix structural eligibility: " + name);
            // Hot flags/tables and their packed offsets are physical metadata.
            // Compare semantic fields and original pool/predicate order instead.
            Check(baseline.Meta.Take(29).SequenceEqual(trial.Meta.Take(29)) &&
                baseline.Meta.Skip(37).Take(20).SequenceEqual(trial.Meta.Skip(37).Take(20)) &&
                baseline.Meta[58] == trial.Meta[58] && baseline.Meta[59] == trial.Meta[59] &&
                baseline.Pools.SequenceEqual(trial.Pools) && baseline.Ids.SequenceEqual(trial.Ids) &&
                baseline.Predicates.SequenceEqual(trial.Predicates) && baseline.Targets.SequenceEqual(trial.Targets),
                "Prefix semantic metadata: " + name);
            if (eligible)
            {
                Check(trial.Revision(false) != baseline.Revision(false), "Prefix physical revision: " + name);
                for (ulong root = 0; root < 16; root++)
                for (int route = 0; route < replay.RouteCount; route++)
                {
                    var opening = replay.Opening(root, route);
                    Check(opening.PotionOdds == .4f && opening.CardRarityOffset == -.05f,
                        "Prefix retains initial combat probability states: " + name);
                }
            }
            fixtures.Add(new(name + ".Baseline", replay, baseline, request, Start: start));
            var fixture = new OpeningPrefixFixture(name + ".Trial", replay, trial, request, Start: start);
            fixtures.Add(fixture);
            return fixture;
        }
        // These are prefix-hot numerical fixtures. Observed Capsule/Bones now
        // needs actual bag authority, which this solo world fixture does not
        // provide. Assert its real retained scope separately, not hot parity.
        foreach (var (name, filter) in new[] {
                     ("DirectSmall", basis with { NeowRoute = new(small) }),
                     ("DirectLarge", basis with { NeowRoute = new(large) }),
                     ("BonesNoChild", basis with { NeowRoute = new(bones) }),
                     ("BonesSingleCapsule", basis with { NeowRoute = new(bones), RequiredBonesCombination = new[] { small } }) })
        {
            var request = query(filter);
            var replay = new CombatRewardReplay(request.Model);
            Check(replay.ConservativelyKeeps && replay.ConservativeOpeningReason ==
                "ObservedOpeningBag:UnsupportedOrIncompleteRelicRuntimeAuthority", "Missing real bag retains its actual cause: " + name);
            Check(CombatRewardFamily.TryPrepare(request.Model, FilterExecutionPreferences.From(request.RunOptions), out var prepared) &&
                prepared!.Body.Preparation.Remainders.Contains("C.CombatReward:" + replay.ConservativeOpeningReason),
                "Missing observed opening is disclosed without claiming numerical coverage: " + name);
            for (ulong root = 0; root < 16; root++)
                Check(replay.Matches(root) && replay.MatchesReference(root), "Unavailable opening cannot reject a root: " + name);
        }
        foreach (var relic in new[] { BaseGameModelKeys.Relics.GoldenPearl, arcane, scroll,
                     BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.HeftyTablet,
                     BaseGameModelKeys.Relics.LeadPaperweight, BaseGameModelKeys.Relics.LostCoffer })
            Add("Direct" + relic.Entry, basis with { NeowRoute = new(relic) });
        var pair = basis with { NeowRoute = new(bones), RequiredBonesCombination = [scroll, arcane] };
        var union = Add("BonesUnion", pair);
        Check(union.Replay.RouteCount == 2, "Prefix retains both authored predecessors");
        Add("BonesAB", pair with { RequiredBonesAcquisitionOrder = [scroll, arcane] });
        Add("BonesBA", pair with { RequiredBonesAcquisitionOrder = [arcane, scroll] });
        // The existing party fixture supplies complete bucket/eligibility data.
        // Check its actual observed prefix; the legacy GPU exporter has no
        // observed-opening buffers and must not call this Capsule hot parity.
        var partyCapsuleRequest = ForPartySlot(query(basis with { NeowRoute = new(small) }), 3);
        var partyCapsuleReplay = new CombatRewardReplay(partyCapsuleRequest.Model, 11);
        var partyCapsulePlan = CombatRewardGpuPlan.TryCreate(partyCapsuleRequest.Model, partyCapsuleReplay,
            out var partyCapsuleIssue) ?? throw new Exception(partyCapsuleIssue);
        Check(partyCapsuleReplay.UsesObservedOpening && !partyCapsuleReplay.ConservativelyKeeps &&
            partyCapsulePlan.UsesObservedOpening && !partyCapsulePlan.UsesOpeningPrefixHotLoop,
            "Complete party Capsule uses actual observed opening, not the query-literal prefix hot loop");
        for (ulong root = 0; root < 16; root++)
        {
            var expected = new Beta110FastRng(unchecked(root + 3UL + XxHash64.Hash("rewards"u8, 0)));
            expected.NextFloat();
            Check(partyCapsuleReplay.Opening(root, 0).Rewards.CaptureCheckpoint() == expected.CaptureCheckpoint(),
                "Actual party SmallCapsule retains its personal one-draw Rewards prefix");
        }
        Add("SixUnordered", basis with { NeowRoute = new(arcane), CombatCardRewardSequence = new(6,
            CombatRewardSequenceOrderMode.Unordered, [card, card, null, null, rare, null]) });
        Add("SixRare", basis with { NeowRoute = new(BaseGameModelKeys.Relics.GoldenPearl), CombatCardRewardSequence = new(6,
            CombatRewardSequenceOrderMode.Unordered, [rare, null, null, null, null, null]) });
        Add("SixRareA0", basis with { NeowRoute = new(BaseGameModelKeys.Relics.GoldenPearl), CombatCardRewardSequence = new(6,
            CombatRewardSequenceOrderMode.Unordered, [rare, null, null, null, null, null]) }, a0: true);
        Add("SixRareDuplicateA0", basis with { NeowRoute = new(arcane), CombatCardRewardSequence = new(6,
            CombatRewardSequenceOrderMode.Unordered, [rare, rare, null, null, null, null]) }, a0: true);
        Add("MixedAssignmentWindows", basis with { NeowRoute = new(arcane), CombatCardRewardSequence = new(2,
            CombatRewardSequenceOrderMode.Unordered, [card, card]), CombatPotionRewardSequence = new(6,
            CombatRewardSequenceOrderMode.Unordered, [new(CombatPotionSlotRequirement.NoDrop, null),
                new(CombatPotionSlotRequirement.NoDrop, null), new(CombatPotionSlotRequirement.Neutral, null),
                new(CombatPotionSlotRequirement.Neutral, null), new(CombatPotionSlotRequirement.Neutral, null),
                new(CombatPotionSlotRequirement.Neutral, null)]) });
        Add("OrderedPotionIdentity", basis with { NeowRoute = new(arcane), CombatPotionRewardSequence = new(3,
            CombatRewardSequenceOrderMode.Ordered, [new(CombatPotionSlotRequirement.Neutral, null),
                new(CombatPotionSlotRequirement.DropSpecific, new("POTION", "BENCH_COMMON_POTION")),
                new(CombatPotionSlotRequirement.NoDrop, null)]) });
        Add("UnorderedPotionIdentity", basis with { NeowRoute = new(arcane), CombatPotionRewardSequence = new(3,
            CombatRewardSequenceOrderMode.Unordered, [new(CombatPotionSlotRequirement.Neutral, null),
                new(CombatPotionSlotRequirement.DropSpecific, new("POTION", "BENCH_COMMON_POTION")),
                new(CombatPotionSlotRequirement.NoDrop, null)]) }, eligible: false);
        Add("GoldGuard", basis with { NeowRoute = new(arcane), NormalCombatRewardConditions =
            [new(3, ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 8, 11)] }, eligible: false);
        foreach (string entry in new[] { "PRAYER_WHEEL", "LASTING_CANDY", "WHITE_BEAST_STATUE" })
        {
            // This checks the suffix compiler's state guard, not a fictitious
            // Capsule obtain. Real Capsule state is covered by the replay host.
            var declared = query(basis with { NeowRoute = new(small), StructuredNeowEffects =
                [new(small, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                    NeowStructuredOutputKind.Relic, [new("RELIC", entry)])] });
            var numeric = fixtures.Single(f => f.Name == "DirectARCANE_SCROLL.Trial").Replay;
            var context = Beta110CombatRewardExplicitContextProjector.Project(declared.Model.CompiledSearch);
            var guarded = Beta110GpuCombatRewardHotLoopCompiler.Compile(10, numeric.Plan with { ExplicitContext = context },
                numeric.Catalog, Beta110GpuCombatRewardRoutePolicy.UnpinnedAssumeUnperturbed, allowOpeningPrefix: true);
            Check(!guarded.Enabled, "Suffix refuses actual structural held state: " + entry);
        }

        // Explicitly find both rare Defect ScrollBoxes branches. Each ordinary
        // bundle consumes its test + three selections; a special bundle only its test.
        var scrollFixture = fixtures.Single(f => f.Name == "DirectSCROLL_BOXES.Trial");
        var seen = new HashSet<int>();
        for (ulong ordinal = 0; ordinal < 65536 && seen.Count < 3; ordinal++)
        {
            ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal));
            var rng = new Beta110FastRng(unchecked(root + XxHash64.Hash("rewards"u8, 0)));
            int specials = 0;
            for (int bundle = 0; bundle < 2; bundle++)
                if (rng.NextInt(100) < 1) specials++;
                else for (int draw = 0; draw < 3; draw++) rng.NextDouble();
            if (!seen.Add(specials)) continue;
            Check(scrollFixture.Replay.Opening(root, 0).Rewards.CaptureCheckpoint() == rng.CaptureCheckpoint(),
                "ScrollBoxes special branch checkpoint: " + specials);
            fixtures.Add(scrollFixture with { Name = "ScrollBoxesSpecial" + specials, Start = ordinal });
            Console.WriteLine($"C prefix ScrollBoxes witness specials={specials};ordinal={ordinal};seed={Beta110SeedCodec.FormatOrdinal(ordinal)}");
            if (specials == 0) continue;
            // Require this exact GPU lane to distinguish the real special branch
            // from the tempting but incorrect eight-draw ordinary-only prefix.
            var wrongOpening = scrollFixture.Replay.Opening(root, 0);
            wrongOpening.Rewards = new Beta110FastRng(unchecked(root + XxHash64.Hash("rewards"u8, 0)));
            for (int draw = 0; draw < 8; draw++) wrongOpening.Rewards.NextDouble();
            NeowSearchFilter? distinguishing = null;
            foreach (var candidate in query(NeowSearchFilter.Empty).Model.Authority.EffectAuthority!.CharacterRewardPool!)
            {
                var filter = basis with { NeowRoute = new(scroll), CombatCardRewardSequence = new(6,
                    CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, candidate.CardKey]) };
                var replay = new CombatRewardReplay((query(filter)).Model);
                if (replay.Matches(root) && Beta110CombatRewardFastStage.EvaluateForFamily(wrongOpening,
                    replay.Plan, replay.Catalog, 10).Status == Beta110CombatRewardRouteProjectionStatus.Rejected)
                { distinguishing = filter; break; }
            }
            Check(distinguishing is not null, "ScrollBoxes special branch has a discriminating card: " + specials);
            Add("ScrollBoxesSpecial" + specials + "ExactPositive", distinguishing!, start: ordinal);
        }
        Check(seen.SetEquals(new[] { 0, 1, 2 }), "ScrollBoxes ordinary, single-special and double-special witnesses");

        CheckTargetOptimizationGuards(fixtures.Single(f => f.Name == "SixRare.Trial"),
            fixtures.Single(f => f.Name == "SixUnordered.Trial"));
        foreach (var fixture in fixtures.Where(f => f.Name is "SixRare.Trial" or "SixRareA0.Trial" or
                     "SixRareDuplicateA0.Trial" or "SixUnordered.Trial" or "MixedAssignmentWindows.Trial").ToArray())
        foreach (var (suffix, rareOnly, assignment) in new[] { ("RareOnly", true, false), ("Assignment", false, true), ("All", true, true) })
        {
            var hot = CombatRewardTargetOptimizations.Apply(fixture.Plan.HotLoop, fixture.Replay.Plan,
                fixture.Replay.Catalog, rareOnly, assignment);
            fixtures.Add(fixture with { Name = fixture.Name + "." + suffix, Plan = fixture.Plan with { HotLoop = hot } });
        }

        // Fault injection at the prefix source pool proves neither the optimized
        // suffix nor rejection masks a failed authored opening consumption.
        foreach (var fixture in fixtures.Where(f => f.Name.StartsWith("DirectARCANE_SCROLL.", StringComparison.Ordinal)).ToArray())
        {
            var pools = fixture.Plan.Pools.ToArray(); pools[5] = 0;
            fixtures.Add(fixture with { Name = fixture.Name + ".PrefixPoolFault", Plan = fixture.Plan with { Pools = pools }, ExpectedFault = true });
        }

        CheckFollowupGpuOptimizations(query, fixtures);

        // A successful empty set cannot validate the state handoff. Anchor each
        // normal fixture on a real CPU positive and require rejections nearby.
        foreach (var fixture in fixtures.Where(f => !f.ExpectedFault).ToArray())
        {
            ulong? positive = null;
            for (ulong offset = 0; offset < 65536; offset++)
            {
                ulong ordinal = fixture.Start + offset;
                if (fixture.Replay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal))))
                { positive = ordinal; break; }
            }
            Check(positive.HasValue, "Prefix fixture has a real positive: " + fixture.Name);
            // Special-branch anchors must stay at their original ordinal. Add a
            // separate positive anchor rather than replacing the branch witness.
            if (fixture.Name is "ScrollBoxesSpecial0" or "ScrollBoxesSpecial1" or "ScrollBoxesSpecial2")
            {
                int at = fixtures.IndexOf(fixture);
                fixtures[at] = fixture with { RequireMixedResults = false };
                fixtures.Add(fixture with { Name = fixture.Name + ".Positive", Start = positive!.Value });
            }
            else
            {
                int at = fixtures.IndexOf(fixture);
                fixtures[at] = fixture with { Start = positive!.Value };
            }
        }
        CheckCombatRewardProductionDefaults(fixtures);
        return fixtures;
    }

    private static void CheckCombatRewardProductionDefaults(List<OpeningPrefixFixture> fixtures)
    {
        string[] variables = ["RT2_C_PREFIX_EXPERIMENT", "RT2_C_TARGET_EXPERIMENT", "RT2_C_DENSE_EXPERIMENT"];
        string?[] saved = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        var rare = fixtures.Single(f => f.Name == "SixRareDuplicateA0.Trial");
        var assignment = fixtures.Single(f => f.Name == "SixUnordered.Trial");
        var gold = fixtures.Single(f => f.Name == "GoldGuard.Trial");
        var eligible = fixtures.Single(f => f.Name == "DirectARCANE_SCROLL.Trial");
        var cases = new[] {
            (Fixture: eligible, Hot: true, Rare: false, Assignment: false),
            (Fixture: rare, Hot: true, Rare: true, Assignment: true),
            (Fixture: fixtures.Single(f => f.Name == "SixRare.Trial"), Hot: true, Rare: true, Assignment: false),
            (Fixture: assignment, Hot: true, Rare: false, Assignment: true),
            (Fixture: gold, Hot: false, Rare: false, Assignment: false),
            (Fixture: fixtures.Single(f => f.Name == "UnorderedPotionIdentity.Trial"), Hot: false, Rare: false, Assignment: false)
        };
        CombatRewardGpuPlan Create(OpeningPrefixFixture fixture, PreparedSearch? request = null) =>
            CombatRewardGpuPlan.TryCreate((request ?? fixture.Request).Model, fixture.Replay, out string issue)
                ?? throw new Exception("C production default plan: " + fixture.Name + ":" + issue);
        void Set(string? prefix, string? target, string? dense)
        {
            Environment.SetEnvironmentVariable(variables[0], prefix);
            Environment.SetEnvironmentVariable(variables[1], target);
            Environment.SetEnvironmentVariable(variables[2], dense);
        }
        try
        {
            foreach (string? mode in new string?[] { null, "", "default", "off" })
            {
                Set(mode, mode, mode);
                bool enabled = mode != "off";
                foreach (var value in cases)
                {
                    var plan = Create(value.Fixture);
                    string label = value.Fixture.Name + ";mode=" + (mode ?? "unset");
                    Check(plan.UsesHotLoop == (enabled && value.Hot) &&
                        plan.UsesOpeningPrefixHotLoop == (enabled && value.Hot), "Production prefix eligibility: " + label);
                    Check(plan.HotLoop.RareOnlyIdentityElisionEnabled == (enabled && value.Rare) &&
                        plan.HotLoop.AssignmentEarlyRejectionEnabled == (enabled && value.Assignment),
                        "Production target eligibility: " + label);
                    Check(plan.UsesDenseCarry8 == enabled, "Production Dense selection: " + label);
                    // Export only a small set of real default-created plans. The
                    // existing GPU runner sends Dense and Compact to the same
                    // shader with their actual 512/64-candidate dispatch sizes.
                    if ((mode is null && (value.Fixture == rare || value.Fixture == assignment || value.Fixture == gold)) ||
                        (mode == "off" && value.Fixture == rare))
                        fixtures.Add(value.Fixture with { Name = "ProductionDefault." + (mode ?? "unset") + "." + value.Fixture.Name,
                            Plan = plan });
                }
                foreach (var (scanCount, target) in new[] { (1L, 1), (8_589_934_592L, int.MaxValue) })
                {
                    var request = rare.Request with { ResolvedScanCount = scanCount,
                        RunOptions = rare.Request.RunOptions with { ScanCount = scanCount, TargetMatchCount = target } };
                    var plan = Create(rare, request);
                    Check(plan.UsesDenseCarry8 == enabled && plan.UsesOpeningPrefixHotLoop == enabled &&
                        plan.HotLoop.RareOnlyIdentityElisionEnabled == enabled &&
                        plan.HotLoop.AssignmentEarlyRejectionEnabled == enabled,
                        "Production optimization selection is independent of scan count and target: " + scanCount);
                }
            }
            foreach (var (targetMode, rareOnly, earlyAssignment) in new[] {
                         ("rare-only", true, false), ("assignment", false, true), ("all", true, true) })
            {
                Set("hot", targetMode, "carry8");
                var plan = Create(rare);
                Check(plan.UsesOpeningPrefixHotLoop && plan.UsesDenseCarry8 &&
                    plan.HotLoop.RareOnlyIdentityElisionEnabled == rareOnly &&
                    plan.HotLoop.AssignmentEarlyRejectionEnabled == earlyAssignment,
                    "Explicit optimization controls remain selectable: " + targetMode);
            }
            Set("hot", "off", "off");
            var prefixOnly = Create(rare);
            Check(prefixOnly.UsesOpeningPrefixHotLoop && !prefixOnly.UsesDenseCarry8 &&
                !prefixOnly.HotLoop.RareOnlyIdentityElisionEnabled && !prefixOnly.HotLoop.AssignmentEarlyRejectionEnabled,
                "Explicit off controls keep the prepared prefix independently selectable");
        }
        finally
        {
            for (int i = 0; i < variables.Length; i++) Environment.SetEnvironmentVariable(variables[i], saved[i]);
        }
    }

    private static void CheckTargetOptimizationGuards(OpeningPrefixFixture rareFixture, OpeningPrefixFixture assignmentFixture)
    {
        var replay = rareFixture.Replay;
        Beta110GpuCombatRewardHotLoopPlan Apply(Beta110FastEffectCatalog catalog, Beta110CombatRewardFastPlan? plan = null)
        {
            var reward = plan ?? replay.Plan;
            var hot = Beta110GpuCombatRewardHotLoopCompiler.Compile(10, reward, catalog,
                Beta110GpuCombatRewardRoutePolicy.UnpinnedAssumeUnperturbed, allowOpeningPrefix: true);
            return CombatRewardTargetOptimizations.Apply(hot, reward, catalog, rareOnly: true, assignment: true);
        }
        Check(Apply(replay.Catalog).RareOnlyIdentityElisionEnabled, "Rare-only target enables identity elision");
        var assignment = assignmentFixture.Replay;
        Check(CombatRewardTargetOptimizations.Apply(assignmentFixture.Plan.HotLoop, assignment.Plan,
            assignment.Catalog, true, true).AssignmentEarlyRejectionEnabled, "Distinct-battle assignment enables capacity gate");
        Check(!CombatRewardTargetOptimizations.Apply(assignmentFixture.Plan.HotLoop, assignment.Plan,
            assignment.Catalog, true, true).RareOnlyIdentityElisionEnabled, "Mixed common/rare targets retain identities");
        var predicate = replay.Plan.Predicates[0];
        var nonrareBan = replay.Plan with { Predicates = [predicate with { CardBan = [replay.Catalog.CombatRewardCardPool.Common[0]] }] };
        Check(!Apply(replay.Catalog, nonrareBan).RareOnlyIdentityElisionEnabled, "Nonrare ban retains observable identities");
        var pool = replay.Catalog.CombatRewardCardPool;
        foreach (var invalid in new[] {
            pool with { Common = [] },
            pool with { Common = pool.Common.Append(pool.Common[0]).ToArray() },
            pool with { Common = pool.Common.Append(pool.Rare[0]).ToArray() },
            pool with { Common = pool.Common.Append(Beta110FastDenseId.Invalid).ToArray() } })
        {
            var hot = Apply(replay.Catalog with { CombatRewardCardPool = invalid }, assignment.Plan);
            Check(!hot.RareOnlyIdentityElisionEnabled && !hot.AssignmentEarlyRejectionEnabled,
                "Incomplete/duplicate/overlapping/invalid card pools retain full work and faults");
        }
        var incompletePotions = assignment.Catalog with { CombatRewardPotionPool = assignment.Catalog.CombatRewardPotionPool with { Rare = [] } };
        var noPotionGate = CombatRewardTargetOptimizations.Apply(assignmentFixture.Plan.HotLoop,
            assignment.Plan, incompletePotions, true, true);
        Check(!noPotionGate.AssignmentEarlyRejectionEnabled, "Missing potion rarity retains later numeric faults");

        // Independently enumerate adding at most one assignment target per future
        // battle, including an empty battle. This covers count=6's high mask bit.
        for (int count = 0; count <= 6; count++)
        for (int remaining = 0; remaining <= 6; remaining++)
        {
            ulong mask = CombatRewardTargetOptimizations.FeasibleAssignmentStates(count, remaining);
            int full = (1 << count) - 1;
            for (int subset = 0; subset <= full; subset++)
            {
                var reachable = new HashSet<int> { subset };
                for (int battle = 0; battle < remaining; battle++)
                {
                    var next = new HashSet<int>(reachable);
                    foreach (int state in reachable)
                    for (int target = 0; target < count; target++) next.Add(state | (1 << target));
                    reachable = next;
                }
                Check(((mask & (1UL << subset)) != 0) == reachable.Contains(full),
                    "Assignment remaining-battle feasibility matches explicit one-target transitions");
            }
        }
        Check(CombatRewardTargetOptimizations.FeasibleAssignmentStates(6, 0) == 1UL << 63,
            "Six-target complete state preserves bit 63");
        Throws<ArgumentOutOfRangeException>(() => CombatRewardTargetOptimizations.FeasibleAssignmentStates(7, 0), "Reject oversized assignment mask");
    }

    private static void CheckFollowupGpuOptimizations(Func<NeowSearchFilter, PreparedSearch> query,
        List<OpeningPrefixFixture> fixtures)
    {
        var authority = query(NeowSearchFilter.Empty).Model.Authority.EffectAuthority!;
        ModelKey Key(EffectCardRarity rarity) => authority.CharacterRewardPool!.First(c => c.Rarity == rarity).CardKey;
        ModelKey common = Key(EffectCardRarity.Common);
        ModelKey arcane = BaseGameModelKeys.Relics.ArcaneScroll, capsule = BaseGameModelKeys.Relics.SmallCapsule;
        NeowSearchFilter Cards(params ModelKey?[] slots) => NeowSearchFilter.Empty with {
            NeowRoute = new(arcane), CombatCardRewardSequence = new(6, CombatRewardSequenceOrderMode.Unordered, slots) };
        CombatRewardGpuPlan Create(PreparedSearch request, CombatRewardReplay replay, bool gold) =>
            CombatRewardGpuPlan.TryCreate(request.Model, replay, out string issue, openingPrefixHotLoop: true,
                rareOnlyIdentityElision: false, assignmentEarlyRejection: false, unobservedFixedGold: gold)
                ?? throw new Exception("Followup GPU fixture: " + issue);
        OpeningPrefixFixture Add(string name, NeowSearchFilter filter, Beta110CombatRewardExplicitContext explicitContext)
        {
            var request = query(filter);
            var prefix = new CombatRewardReplay(request.Model);
            // An explicitly controlled reward state tests the suffix optimizer.
            // It is not a claim that this query generated a Capsule/Aubergine.
            var replay = new CombatRewardReplay(request.Model, catalog: prefix.Catalog,
                plan: prefix.Plan with { ExplicitContext = explicitContext });
            var baseline = Create(request, replay, false);
            var trial = Create(request, replay, true);
            Check(trial.UsesHotLoop && trial.HotLoop.UnobservedFixedGoldEnabled,
                "Followup fixed-gold admission: " + name);
            Check(!baseline.HotLoop.UnobservedFixedGoldEnabled,
                "Followup off preserves baseline: " + name);
            Check(!baseline.UsesHotLoop && replay.Plan.ExplicitContext.FixedGoldAmount > 0,
                "Fixed-gold source is authored and blocked only in baseline");
            Check(trial.Revision(false) != baseline.Revision(false), "Followup physical identity: " + name);
            Check(baseline.Pools.SequenceEqual(trial.Pools) && baseline.Ids.SequenceEqual(trial.Ids) &&
                baseline.Predicates.SequenceEqual(trial.Predicates) && baseline.Targets.SequenceEqual(trial.Targets),
                "Followup retains original pools/predicates: " + name);
            fixtures.Add(new("Followup." + name + ".Off", replay, baseline, request));
            var fixture = new OpeningPrefixFixture("Followup." + name + ".On", replay, trial, request);
            fixtures.Add(fixture);
            return fixture;
        }
        var fixedFilter = Cards(common, null, null, null, null, null) with { NeowRoute = new(capsule), StructuredNeowEffects =
            [new(capsule, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "AMETHYST_AUBERGINE")])] };
        var fixedContext = Beta110CombatRewardExplicitContextProjector.Project(query(fixedFilter).Model.CompiledSearch);
        var suffixFilter = Cards(common, null, null, null, null, null);
        var fixedFixture = Add("FixedGoldNumericalSuffix", suffixFilter, fixedContext);
        foreach (var flag in new[] { Beta110CombatRewardInfluenceFlags.ForcePotionReward, Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward,
                     Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard, Beta110CombatRewardInfluenceFlags.UnknownRewardImpact,
                     Beta110CombatRewardInfluenceFlags.UnknownRewardsContinuation })
        {
            var plan = fixedFixture.Replay.Plan with { ExplicitContext = fixedFixture.Replay.Plan.ExplicitContext with {
                InfluenceFlags = fixedFixture.Replay.Plan.ExplicitContext.InfluenceFlags | flag } };
            var changed = new CombatRewardReplay(fixedFixture.Request.Model, catalog: fixedFixture.Replay.Catalog, plan: plan);
            Check(!Create(fixedFixture.Request, changed, true).UsesHotLoop, "Fixed gold retains structural/unknown guard: " + flag);
        }
        var extraRewardPlan = fixedFixture.Replay.Plan with { ExplicitContext = fixedFixture.Replay.Plan.ExplicitContext with {
            AdditionalCardRewardCount = 1 } };
        Check(!Create(fixedFixture.Request, new CombatRewardReplay(fixedFixture.Request.Model,
            catalog: fixedFixture.Replay.Catalog, plan: extraRewardPlan), true).UsesHotLoop,
            "Fixed gold retains additional-card-count guard without relying on its flag");
        var goldRequest = query(suffixFilter with { NormalCombatRewardConditions =
            [new(1, ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 0, null)] });
        var goldPrefix = new CombatRewardReplay(goldRequest.Model);
        var goldReplay = new CombatRewardReplay(goldRequest.Model, catalog: goldPrefix.Catalog,
            plan: goldPrefix.Plan with { ExplicitContext = fixedContext });
        Check(!Create(goldRequest, goldReplay, true).UsesHotLoop,
            "Observed gold retains generic evaluator");

        string[] variables = ["RT2_C_FIXED_GOLD_HOT"];
        var saved = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string? mode in new string?[] { null, "", "default", "off", "on" })
            {
                foreach (string variable in variables) Environment.SetEnvironmentVariable(variable, mode);
                var goldPlan = CombatRewardGpuPlan.TryCreate(fixedFixture.Request.Model, fixedFixture.Replay, out _, openingPrefixHotLoop: true,
                    rareOnlyIdentityElision: false, assignmentEarlyRejection: false)!;
                Check(goldPlan.HotLoop.UnobservedFixedGoldEnabled == (mode != "off"), "Followup immutable capture: " + mode);
                foreach (string variable in variables) Environment.SetEnvironmentVariable(variable, "off");
                Check(goldPlan.HotLoop.UnobservedFixedGoldEnabled == (mode != "off"), "Followup plans retain captured selection");
            }
        }
        finally { for (int i = 0; i < variables.Length; i++) Environment.SetEnvironmentVariable(variables[i], saved[i]); }
    }

    private static void ExportOpeningPrefixGpu(List<OpeningPrefixFixture> cases,
        string exportDirectory = ".build-evidence/c-prefix-gpu", int size = 259)
    {
        if (Environment.GetEnvironmentVariable("RT2_C_GPU_EXPORT_ROOT") is { Length: > 0 } exportRoot)
            exportDirectory = Path.Combine(exportRoot, Path.GetFileName(exportDirectory));
        string directory = Path.GetFullPath(exportDirectory); Directory.CreateDirectory(directory);
        var fixtures = new List<object>();
        int shaderIndex = 0;
        foreach (var fixture in cases)
        foreach (bool forceGeneric in fixture.Plan.UsesOpeningPrefixHotLoop ? new[] { false, true } : new[] { false })
        foreach (var numerical in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
        {
            Check(!fixture.Replay.UsesObservedOpening && !fixture.Replay.ConservativelyKeeps,
                "Prefix GPU export owns only available numerical inputs its runner actually binds: " + fixture.Name);
            var plan = fixture.Plan with { NumericalVariant = numerical };
            string shader = $"shader{shaderIndex++}.glsl";
            File.WriteAllText(Path.Combine(directory, shader), plan.ShaderSource(forceGeneric));
            foreach (bool compact in new[] { false, true })
            {
                uint[] ordinals = Enumerable.Range(0, size).Where(i => !compact || i % 3 == 0).Select(i => (uint)i).ToArray();
                uint[] expected = fixture.ExpectedFault ? [] : ordinals.Where(i => fixture.Replay.MatchesReference(
                    Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(fixture.Start + i)))).ToArray();
                if (!fixture.ExpectedFault && fixture.RequireMixedResults)
                    Check(expected.Length > 0 && expected.Length < ordinals.Length,
                        "Prefix GPU fixture includes matches and rejections: " + fixture.Name);
                fixtures.Add(new { name = fixture.Name + "." + numerical + (forceGeneric ? ".ForcedGeneric" : ".Selected"), shader,
                    batchMeta = new[] { (uint)fixture.Start, (uint)(fixture.Start >> 32), (uint)size, (uint)ordinals.Length,
                        compact ? 1u : 0u, (uint)size, 0u, (uint)plan.Routes }, plan.Meta, plan.Pools, plan.Ids,
                    plan.Predicates, plan.Targets, plan.HotLoop, ordinals, expected, expectedFault = fixture.ExpectedFault });
            }
        }
        File.WriteAllText(Path.Combine(directory, "fixtures.json"), System.Text.Json.JsonSerializer.Serialize(fixtures));
        File.WriteAllText(Path.Combine(directory, "project.godot"), "config_version=5\n[application]\nconfig/name=\"C prefix GPU parity\"\n[rendering]\nrenderer/rendering_method=\"mobile\"\n");
        File.Copy("tests/functional/Rt2FunctionalChecks/CombatRewardGpuSanity.gd", Path.Combine(directory, "check.gd"), true);
        Console.WriteLine($"C prefix GPU fixtures exported: {fixtures.Count}; shaders={shaderIndex}; directory={directory}");
    }

    private static List<TerminalCardFixture> CheckTerminalCardGate(Func<NeowSearchFilter, PreparedSearch> query)
    {
        var fixtures = new List<TerminalCardFixture>();
        var authority = query(NeowSearchFilter.Empty).Model.Authority;
        ModelKey[] keys = authority.EffectAuthority!.CharacterRewardPool!.Select(c => c.CardKey).Distinct().Take(3).ToArray();
        var filter = NeowSearchFilter.Empty with { NeowRoute = new(BaseGameModelKeys.Relics.SilkenTress),
            CombatCardRewardSequence = new(3, CombatRewardSequenceOrderMode.Unordered, keys.Select(k => (ModelKey?)k).ToArray()) };
        var request = query(filter);
        var original = new CombatRewardReplay(request.Model);
        ushort[] ids = keys.Select(k => original.Catalog.TryGetDenseId(k, out var id) ? id : throw new Exception("Terminal target missing")).ToArray();
        Check(ids.Distinct().Count() == 3, "Terminal fixture has three different targets");
        var controlled = original.Catalog with { CombatRewardCardPool = new(ids, [], []) };

        CombatRewardGpuPlan Add(string name, CombatRewardReplay replay, bool admitted, uint[]? ordinals = null, bool fault = false)
        {
            var plan = CombatRewardGpuPlan.TryCreate(request.Model, replay, out string issue, openingPrefixHotLoop: false) ?? throw new Exception("Terminal fixture: " + issue);
            Check(plan.UsesTerminalCardGate == admitted, "Terminal gate admission: " + name);
            fixtures.Add(new(name, replay, plan, ordinals, fault));
            return plan;
        }
        CombatRewardReplay With(Beta110CombatRewardFastPlan? plan = null, Beta110FastEffectCatalog? catalog = null)
        {
            var selected = plan ?? original.Plan;
            if (plan is not null && plan.MaximumBattleOrdinal < original.Plan.MaximumBattleOrdinal)
                selected = selected with { CardAssignmentWindow = 0, CardAssignmentTargets = [],
                    PotionAssignmentWindow = 0, PotionAssignmentRequirements = [], PotionAssignmentTargets = [],
                    Predicates = selected.Predicates.Select(p => p with { AnyBattleWindow = 0 }).ToArray() };
            return new(request.Model, catalog: catalog ?? controlled, plan: selected);
        }

        Add("TerminalOriginalPool", original, true);
        // Every normal reward contains all three identities. The first case must
        // retain a root whose last card fills its last missing target; the second
        // must account for predicates already satisfied in the preceding battle.
        foreach (int horizon in new[] { 1, 2 })
        {
            // This diagnostic isolates the retained necessary terminal gate;
            // real typed requests keep their full distinct-battle obligations.
            var replay = With(original.Plan with { MaximumBattleOrdinal = horizon,
                CardAssignmentWindow = 0, CardAssignmentTargets = [],
                PotionAssignmentWindow = 0, PotionAssignmentRequirements = [], PotionAssignmentTargets = [],
                Predicates = original.Plan.Predicates.Select(p => p with { AnyBattleWindow = 0 }).ToArray() });
            for (ulong ordinal = 0; ordinal < 64; ordinal++)
                Check(replay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal))),
                    horizon == 1 ? "Terminal final card completes targets" : "Terminal prior battle already matched targets");
            Add(horizon == 1 ? "TerminalLastCardCompletes" : "TerminalPriorBattleMatched", replay, true);
        }
        var repeated = original.Plan with { Predicates = [original.Plan.Predicates[0], original.Plan.Predicates[0], original.Plan.Predicates[0]] };
        Add("TerminalRepeatedSingleTarget", With(repeated), false);
        Add("TerminalRepeatedTwoTargets", With(original.Plan with { Predicates =
            [original.Plan.Predicates[0], original.Plan.Predicates[0], original.Plan.Predicates[1]] }), true);
        Add("TerminalComposite", With(original.Plan with { Predicates = original.Plan.Predicates.Select((p, i) =>
            i == 0 ? p with { PotionRequirement = NormalCombatPotionRequirement.MustDrop } : p).ToArray() }), false);
        foreach (bool candy in new[] { false, true })
        {
            ushort fourth = original.Catalog.CombatRewardCardPool.Common.Concat(original.Catalog.CombatRewardCardPool.Uncommon)
                .Concat(original.Catalog.CombatRewardCardPool.Rare).First(id => !ids.Contains(id));
            var context = original.Plan.ExplicitContext with {
                AdditionalCardRewardCount = candy ? (byte)0 : (byte)1,
                InfluenceFlags = candy ? Beta110CombatRewardInfluenceFlags.LastingCandyPowerCard : Beta110CombatRewardInfluenceFlags.PrayerWheelExtraReward };
            // The compiled body can remain available; runtime state must suppress
            // its three-card capacity assumption for both kinds of extra output.
            var runtimePlan = original.Plan with { ExplicitContext = context, MaximumBattleOrdinal = candy ? 2 : 1,
                Predicates = [.. original.Plan.Predicates, original.Plan.Predicates[0] with { CardAny = [], CardAll = [fourth] }] };
            var runtimeCatalog = candy ? controlled with { CombatRewardPowerPool = new([fourth], [], []) } :
                controlled with { CombatRewardCardPool = new([.. ids, fourth], [], []) };
            var replay = With(runtimePlan, runtimeCatalog);
            var plan = CombatRewardGpuPlan.TryCreate(request.Model, replay, out string issue) ?? throw new Exception(issue);
            Check(plan.UsesTerminalCardGate, "Terminal body remains present for runtime modifier bypass");
            Check(Enumerable.Range(0, 64).Any(i => replay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal((ulong)i)))),
                "Extra output supplies target beyond the ordinary terminal group");
            fixtures.Add(new(candy ? "TerminalCandyRuntimeBypass" : "TerminalWheelRuntimeBypass", replay, plan));
        }
        var partyRequest = ForPartySlot(request, 1);
        var partyReplay = new CombatRewardReplay(partyRequest.Model);
        var partyPlan = CombatRewardGpuPlan.TryCreate(partyRequest.Model, partyReplay, out string partyIssue) ?? throw new Exception(partyIssue);
        Check(!partyPlan.UsesTerminalCardGate, "Terminal gate excludes multiplayer");

        void Fault(string name, Beta110FastEffectCatalog catalog, bool admitted)
        {
            var replay = With(catalog: catalog);
            uint? faultOrdinal = null;
            for (uint ordinal = 0; ordinal < 128 && faultOrdinal is null; ordinal++)
            {
                try { _ = replay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(ordinal))); }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("C.CombatReward.NumericFailure:", StringComparison.Ordinal))
                { faultOrdinal = ordinal; }
            }
            Check(faultOrdinal.HasValue, "Terminal fixture exposes original numeric fault: " + name);
            Add(name, replay, admitted, [faultOrdinal!.Value], true);
        }
        Fault("TerminalDuplicatePoolFault", controlled with { CombatRewardCardPool = new([ids[0], ids[0], ids[0]], [], []) }, false);
        // This fault happens at potion generation, before the terminal card seam.
        // Terminal eligibility does not require hypothetical future potion work.
        Fault("TerminalEmptyPotionFault", controlled with { CombatRewardPotionPool = new([], [], [], []) }, true);
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var nc = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/owner-rare-nc-v1/NC_RARE_01.json");
        var ncRequest = SearchPreparationCompiler.Compile(SearchCompiler.Compile(nc.Query, captured.Context), nc.Options).Plan!;
        var ncReplay = new CombatRewardReplay(ncRequest.Model);
        // This suite tests the retained generic terminal gate. Production's
        // opening-prefix hot body deliberately uses its own rejection path.
        var ncPlan = CombatRewardGpuPlan.TryCreate(ncRequest.Model, ncReplay, out string ncIssue,
            openingPrefixHotLoop: false) ?? throw new Exception(ncIssue);
        Console.WriteLine("C_TERMINAL_OWNER_DETAIL " + System.Text.Json.JsonSerializer.Serialize(new
        {
            scope = "GenericTerminalGateFixture",
            ncPlan.UsesTerminalCardGate,
            ncPlan.UsesOpeningPrefixHotLoop,
            hotLoop = ncPlan.HotLoop.Enabled,
            ncReplay.ConservativelyKeeps,
            ncReplay.Plan.CardPoolAuthorityExact,
            predicateCount = ncReplay.Plan.Predicates.Length,
            allExistential = ncReplay.Plan.Predicates.All(p => p.IsAnyBattle),
            predicates = ncReplay.Plan.Predicates,
            compiledOpening = ncRequest.Model.CompiledSearch.Query.OpeningRoute
        }));
        Check(ncPlan.UsesTerminalCardGate && ncReplay.Plan.Predicates.Length == 6 && ncReplay.Plan.Predicates.All(p => p.IsAnyBattle),
            "Actual NC rare workload retains six existential predicates and uses terminal card gate");
        fixtures.Add(new("TerminalOwnerNcRare01", ncReplay, ncPlan, Start: 1564438048595379352UL));
        return fixtures;
    }

    private static void ExportGpu(Func<NeowSearchFilter,PreparedSearch> query, List<TerminalCardFixture> terminalFixtures)
    {
        string directory=Path.GetFullPath(".build-evidence/c1-gpu");Directory.CreateDirectory(directory);
        var fixtures=new List<object>();
        ModelKey card=new("CARD","BENCH_CHARACTER_0"),rare=new("CARD","BENCH_CHARACTER_16"),potion=new("POTION","BENCH_COMMON_POTION");
        ModelKey bones=BaseGameModelKeys.Relics.NeowsBones,large=BaseGameModelKeys.Relics.LargeCapsule,
            small=BaseGameModelKeys.Relics.SmallCapsule,arcane=BaseGameModelKeys.Relics.ArcaneScroll,scroll=BaseGameModelKeys.Relics.ScrollBoxes;
        var neutral=NeowSearchFilter.Empty with {CombatCardRewardSequence=new(3,CombatRewardSequenceOrderMode.Ordered,[card,null,rare])};
        var cases=new List<(string Name,NeowSearchFilter Filter)> {("Neutral",neutral),
            ("Slot2",neutral with {CombatCardRewardSequence=new(2,CombatRewardSequenceOrderMode.Ordered,[null,card])}),
            ("Slot3",neutral with {CombatCardRewardSequence=new(3,CombatRewardSequenceOrderMode.Ordered,[null,null,card])}),
            ("Duplicate",neutral with {CombatCardRewardSequence=new(3,CombatRewardSequenceOrderMode.Unordered,[card,card,card])})};
        foreach(var order in Enum.GetValues<CombatRewardSequenceOrderMode>())
        foreach(var requirement in new[]{CombatPotionSlotRequirement.NoDrop,CombatPotionSlotRequirement.DropAny,CombatPotionSlotRequirement.DropSpecific})
            cases.Add(($"Potion{order}{requirement}",neutral with {CombatPotionRewardSequence=new(3,order,
                [new(CombatPotionSlotRequirement.Neutral,null),new(requirement,requirement==CombatPotionSlotRequirement.DropSpecific?potion:null),new(CombatPotionSlotRequirement.NoDrop,null)])}));
        foreach(var relic in new[]{arcane,large,small,scroll,BaseGameModelKeys.Relics.Kaleidoscope,BaseGameModelKeys.Relics.HeftyTablet,
            BaseGameModelKeys.Relics.LeadPaperweight,BaseGameModelKeys.Relics.LostCoffer})cases.Add(("Direct"+relic.Entry,neutral with {NeowRoute=new(relic)}));
        var pair=neutral with {NeowRoute=new(bones),RequiredBonesCombination=[scroll,arcane]};
        cases.Add(("BonesAny",pair));cases.Add(("BonesAB",pair with {RequiredBonesAcquisitionOrder=[scroll,arcane]}));
        cases.Add(("BonesBA",pair with {RequiredBonesAcquisitionOrder=[arcane,scroll]}));
        cases.Add(("Partial",neutral with {NeowRoute=new(bones),RequiredBonesCombination=[large]}));
        foreach(string impact in OpeningCombatRewardImpactAdapterRegistry.SupportedRelicEntries)
            cases.Add(("Grouped"+impact,neutral with {NeowRoute=new(bones),RequiredBonesCombination=[small,large],StructuredNeowEffects=[new(bones,
                NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,NeowStructuredEffectScope.NestedRelics,NeowStructuredOutputKind.Relic,[new("RELIC",impact)])]}));
        cases.Add(("CandyAndWheel",neutral with {NeowRoute=new(bones),RequiredBonesCombination=[small,large],StructuredNeowEffects=[new(bones,
            NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,NeowStructuredEffectScope.NestedRelics,NeowStructuredOutputKind.Relic,
            [new("RELIC","LASTING_CANDY"),new("RELIC","PRAYER_WHEEL")])]}));
        foreach (int horizon in new[] {4,5,6})
        foreach (var order in Enum.GetValues<CombatRewardSequenceOrderMode>())
        {
            ModelKey?[] slots = Enumerable.Range(0,horizon).Select(i => i >= 2 ? (ModelKey?)rare : null).ToArray();
            var q = neutral with { CombatCardRewardSequence = new(horizon,order,slots) };
            cases.Add(($"H{horizon}.{order}",q));
            cases.Add(($"H{horizon}.{order}.Arcane",q with {NeowRoute=new(arcane)}));
            cases.Add(($"H{horizon}.{order}.CandyWheel",q with {NeowRoute=new(bones),RequiredBonesCombination=[small,large],
                StructuredNeowEffects=[new(bones,NeowStructuredConditionKind.ExactGroupedCapsuleMultiset,
                    NeowStructuredEffectScope.NestedRelics,NeowStructuredOutputKind.Relic,
                    [new("RELIC","LASTING_CANDY"),new("RELIC","PRAYER_WHEEL")])]}));
        }
        cases.Add(("Zero",neutral with {CombatCardRewardSequence=new(1,CombatRewardSequenceOrderMode.Ordered,[new("CARD","NEVER_IN_POOL")])}));
        cases.Add(("HighSurvival",NeowSearchFilter.Empty with {CombatPotionRewardSequence=new(1,CombatRewardSequenceOrderMode.Ordered,[new(CombatPotionSlotRequirement.NoDrop,null)])}));
        for (int slot = 0; slot < 4; slot++)
        {
            cases.Add(($"PartySlot{slot}Kaleidoscope", neutral with { NeowRoute = new(BaseGameModelKeys.Relics.Kaleidoscope) }));
            cases.Add(($"PartySlot{slot}MassiveScroll", neutral with { NeowRoute = new(BaseGameModelKeys.Relics.MassiveScroll) }));
        }
        cases.Add(("PartySlot3Neutral", neutral));
        foreach (int window in new[] { 1, 2, 6 })
        {
            cases.Add(($"PartySlot3CardWindow{window}", NeowSearchFilter.Empty with {
                CombatCardRewardSequence = new(window, CombatRewardSequenceOrderMode.Unordered,
                    Enumerable.Repeat((ModelKey?)card, window).ToArray()),
                CombatPotionRewardSequence = new(6, CombatRewardSequenceOrderMode.Ordered,
                    [new(CombatPotionSlotRequirement.Neutral, null), new(CombatPotionSlotRequirement.Neutral, null),
                     new(CombatPotionSlotRequirement.Neutral, null), new(CombatPotionSlotRequirement.Neutral, null),
                     new(CombatPotionSlotRequirement.Neutral, null), new(CombatPotionSlotRequirement.NoDrop, null)]) }));
            cases.Add(($"PartySlot3PotionWindow{window}", neutral with {
                CombatCardRewardSequence = new(6, CombatRewardSequenceOrderMode.Ordered, [null, null, null, null, null, card]),
                CombatPotionRewardSequence = new(window, CombatRewardSequenceOrderMode.Unordered,
                    Enumerable.Repeat(new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null), window).ToArray()) }));
        }
        cases.Add(("PartySlot3SmallCapsule", neutral with { NeowRoute = new(small) }));
        cases.Add(("PartySlot3LargeCapsule", neutral with { NeowRoute = new(large) }));
        cases.Add(("PartySlot3CapsulePrayerWheel", neutral with { NeowRoute = new(small), StructuredNeowEffects =
            [new(small, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "PRAYER_WHEEL")])] }));
        cases.Add(("PartySlot3BonesActual", neutral with { NeowRoute = new(bones) }));
        cases.Add(("PartySlot3BonesPartial", neutral with { NeowRoute = new(bones), RequiredBonesCombination = [arcane] }));
        cases.Add(("ExplicitBonesAllArcane", neutral with { NeowRoute = new(bones), BonesRelics = new([], [arcane], []) }));
        cases.Add(("ExplicitBonesAnyArcane", neutral with { NeowRoute = new(bones), BonesRelics = new([arcane], [], []) }));
        cases.Add(("PartySlot3ExplicitBonesAllArcane", neutral with { NeowRoute = new(bones), BonesRelics = new([], [arcane], []) }));
        cases.Add(("PartySlot3ExplicitBonesAnyArcane", neutral with { NeowRoute = new(bones), BonesRelics = new([arcane], [], []) }));
        foreach (string name in new[] { "ExplicitUnmappedNested", "PartySlot3ExplicitUnmappedNested" })
            cases.Add((name, neutral with { NeowRoute = new(small), StructuredNeowEffects =
                [new(small, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                    NeowStructuredOutputKind.Relic, [new("RELIC", "RT2_TEST_UNMAPPED_HELD")])] }));
        foreach (var upgrade in new[] { BaseGameModelKeys.OrdinaryRelics.Whetstone, BaseGameModelKeys.OrdinaryRelics.WarPaint })
        foreach (string prefix in new[] { "", "PartySlot3" })
        {
            var explicitUpgrade = neutral with { NeowRoute = new(small), CapsuleContainedRelics = new([], [upgrade], []) };
            cases.Add(($"{prefix}ExplicitUpgradeAll{upgrade.Entry}", explicitUpgrade));
            cases.Add(($"{prefix}ExplicitUpgradeAny{upgrade.Entry}", explicitUpgrade with { CapsuleContainedRelics = new([upgrade], [], []) }));
            var bonesUpgrade = explicitUpgrade with { NeowRoute = new(bones), BonesRelics = new([], [small], []) };
            cases.Add(($"{prefix}ExplicitUpgradeBonesAll{upgrade.Entry}", bonesUpgrade));
            cases.Add(($"{prefix}ExplicitUpgradeBonesAny{upgrade.Entry}", bonesUpgrade with { CapsuleContainedRelics = new([upgrade], [], []) }));
        }
        cases.Add(("PartySlot3BonesCapsule", neutral with { NeowRoute = new(bones), RequiredBonesCombination = [small, large] }));
        var sharedPair = neutral with { NeowRoute = new(bones), RequiredBonesCombination = [BaseGameModelKeys.Relics.NewLeaf, BaseGameModelKeys.Relics.Kaleidoscope] };
        cases.Add(("PartySlot3BonesNewLeafKaleidoscope", sharedPair));
        cases.Add(("PartySlot3BonesNewLeafKaleidoscopeAB", sharedPair with { RequiredBonesAcquisitionOrder = sharedPair.RequiredBonesCombination }));
        cases.Add(("PartySlot3BonesNewLeafKaleidoscopeBA", sharedPair with { RequiredBonesAcquisitionOrder = sharedPair.RequiredBonesCombination.Reverse().ToArray() }));
        cases.Add(("PartySlot3BonesUnknownNiche", sharedPair));
        cases.Add(("PartySlot3CapsuleUnknownRewardHook", neutral with { NeowRoute = new(small), StructuredNeowEffects =
            [new(small, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "CALLING_BELL")])] }));
        var goldRange = NeowSearchFilter.Empty with { NormalCombatRewardConditions =
            [new(3, ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 9, 12)] };
        cases.Add(("GoldRange", goldRange));
        cases.Add(("RealNeutralWitness", goldRange));
        cases.Add(("PartySlot1RealNeutralWitness", goldRange));
        cases.Add(("PartySlot3GoldRange", goldRange));
        cases.Add(("PartySlot3CapsuleGold", goldRange with { NeowRoute = new(large) }));
        cases.Add(("PartySlot3AuthoredFixedGold", goldRange with { NeowRoute = new(small), NormalCombatRewardConditions =
            [new(0, ModelKeySetFilter.Empty, NormalCombatPotionRequirement.Any, ModelKeySetFilter.Empty, 20, null)], StructuredNeowEffects =
            [new(small, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "AMETHYST_AUBERGINE")])] }));
        int shaderIndex=0;
        var observedNotExported = new List<object>();
        foreach(var (name,filter) in cases)
        {
            bool party = name.StartsWith("PartySlot", StringComparison.Ordinal);
            var request=query(filter);
            if (party) request = ForPartySlot(request, name[9] - '0');
            if (name.EndsWith("RealNeutralWitness", StringComparison.Ordinal))
                request = request with { Model = request.Model with { CombatRewardRoutePolicy = new(
                    CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed, CombatRewardExactRoutePolicy.UnpinnedVerifyNeutralRealRoute) } };
            var replay=new CombatRewardReplay(request.Model, name.EndsWith("UnknownNiche", StringComparison.Ordinal) ? null : party ? 11 : 0);
            if (CombatRewardObservedOpening.Needed(replay.Plan.OpeningConsumption))
            {
                // This legacy sanity runner binds [0] to bones/observed data.
                // Complete real Observed GPU/full-query recall belongs to the
                // ProductionSearchHost contexts; exporting these toys would
                // test fabricated inputs or an all-retained missing-bag guard.
                Check(replay.ConservativelyKeeps || replay.UsesObservedOpening,
                    "Observed fixture declares either its actual input or its missing observation: " + name);
                if (replay.ConservativelyKeeps)
                    Check(replay.ConservativeOpeningReason.Length > 0 && replay.Matches(0),
                        "Unexported fixture retains its real unavailable cause: " + name);
                observedNotExported.Add(new { Name = name, replay.ConservativelyKeeps, replay.ConservativeOpeningReason,
                    replay.UsesObservedOpening, Scope = "NotExportedOrTestedByLegacyGpuRunner",
                    Reason = replay.ConservativelyKeeps ? replay.ConservativeOpeningReason : "RunnerDoesNotBindActualObservedOpeningInputs",
                    ActualObservedGpuCoverageOwner = "ProductionSearchHost.CombatRecallSearchChecks" });
                continue;
            }
            var plan=CombatRewardGpuPlan.TryCreate(request.Model,replay,out string issue)??throw new Exception("GPU fixture admission "+name+":"+issue);
            Check(!plan.UsesHotLoop || !replay.Plan.OpeningConsumption.HasReplay || plan.UsesOpeningPrefixHotLoop,
                "Hot-loop authored prefix retains its prepared opening continuation");
            foreach(bool generic in plan.UsesHotLoop?new[]{false,true}:new[]{false})
            foreach (var numerical in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
            {
                string shader=$"shader{shaderIndex++}.glsl";File.WriteAllText(Path.Combine(directory,shader),(plan with { NumericalVariant = numerical }).ShaderSource(generic));
                foreach(ulong start in new[]{0UL,1564445371644820290UL})
                foreach(bool compact in new[]{false,true})
                {
                    int size=4099;uint[] ordinals=Enumerable.Range(0,size).Where(i=>!compact||i%3==0).Select(i=>(uint)i).ToArray();
                    var expected=ordinals.Where(i=>replay.MatchesReference(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start+i)))).ToArray();
                    fixtures.Add(new {name=name+"."+numerical+(generic?".ForcedGeneric":".Selected"),shader,batchMeta=new[]{(uint)start,(uint)(start>>32),(uint)size,(uint)ordinals.Length,compact?1u:0u,(uint)size,0u,(uint)plan.Routes},
                        plan.Meta,plan.Pools,plan.Ids,plan.Predicates,plan.Targets,plan.HotLoop,forceGeneric=generic,ordinals,expected});
                }
            }
        }
        foreach (var fixture in terminalFixtures)
        foreach (bool reference in new[] { false, true })
        {
            var plan = reference ? fixture.Plan with { TerminalCardGateSource = "" } : fixture.Plan;
            string shader = $"shader{shaderIndex++}.glsl";
            File.WriteAllText(Path.Combine(directory, shader), plan.ShaderSource());
            const int size = 259;
            uint[] ordinals = fixture.Ordinals ?? Enumerable.Range(0, size).Where(i => i % 3 == 0).Select(i => (uint)i).ToArray();
            uint[] expected = fixture.ExpectedFault ? [] : ordinals.Where(i => fixture.Replay.MatchesReference(
                Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(fixture.Start + i)))).ToArray();
            fixtures.Add(new { name = fixture.Name + (reference ? ".Reference" : ".Gate"), shader,
                batchMeta = new[] { (uint)fixture.Start, (uint)(fixture.Start >> 32), (uint)size, (uint)ordinals.Length, 1u, (uint)size, 0u, (uint)plan.Routes },
                plan.Meta, plan.Pools, plan.Ids, plan.Predicates, plan.Targets, plan.HotLoop, ordinals, expected,
                expectedFault = fixture.ExpectedFault });
        }
        File.WriteAllText(Path.Combine(directory,"fixtures.json"),System.Text.Json.JsonSerializer.Serialize(fixtures));
        File.WriteAllText(Path.Combine(directory,"observed-not-exported.json"),System.Text.Json.JsonSerializer.Serialize(observedNotExported));
        File.WriteAllText(Path.Combine(directory,"project.godot"),"config_version=5\n[application]\nconfig/name=\"C1 Family GPU parity\"\n[rendering]\nrenderer/rendering_method=\"mobile\"\n");
        File.Copy("tests/functional/Rt2FunctionalChecks/CombatRewardGpuSanity.gd",Path.Combine(directory,"check.gd"),true);
        Console.WriteLine($"C1 GPU fixtures exported: {fixtures.Count}; shaders={shaderIndex}; observedNotExportedOrClaimed={observedNotExported.Count}; directory={directory}");
    }
}
