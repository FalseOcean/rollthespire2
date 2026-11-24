using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Prediction;
using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;
using MegaCrit.Sts2.Core.Extensions;
using HarmonyLib;
using RolltheSpire2.Infrastructure.Snapshots;
using R = RolltheSpire2.Core.Identity.BaseGameModelKeys.Relics;

internal static class NeowIdentityRulesChecks
{
    // Frozen independently from main@da7f1ae5 ModernNeowIdentityPredictor and
    // sts2_01110/Neow.cs:217-284. Do not rebuild this oracle from prepared rules
    // or numerical metadata: candidate order and skipped draws are observable.
    private static readonly ModelKey[] OriginalPositives =
    [
        R.ArcaneScroll, R.BoomingConch, R.FishingRod, R.GoldenPearl,
        R.Kaleidoscope, R.LeadPaperweight, R.LostCoffer, R.MassiveScroll,
        R.NeowsTorment, R.NewLeaf, R.PhialHolster, R.PreciseScissors,
        R.ScrollBoxes, R.WingedBoots
    ];
    private static readonly ModelKey[] OriginalCurses =
    [
        R.CursedPearl, R.DowsingRod, R.HeftyTablet, R.LargeCapsule,
        R.LeafyPoultice, R.NeowsBones, R.NeowsSacrifice,
        R.PrecariousShears, R.SilkenTress, R.SilverCrucible
    ];
    private static readonly ModelKey[] OriginalBinaryCandidates =
    [R.LavaRock, R.SmallCapsule, R.NutritiousOyster, R.StoneHumidifier, R.NeowsTalisman, R.Pomander];

    internal static int Run()
    {
        int checks = 0, offers = 0, numericalOffers = 0, capsuleOffers = 0;
        void Check(bool ok, string why)
        {
            checks++;
            if (!ok) throw new InvalidDataException("NeowIdentityRules." + why);
        }
        Check(ModernNeowIdentityRules.PositiveCandidates.SequenceEqual(OriginalPositives), "OriginalPositiveOrder");
        Check(ModernNeowIdentityRules.CurseCandidates.SequenceEqual(OriginalCurses), "OriginalCurseOrder");
        CheckImmutable(ModernNeowIdentityRules.PositiveCandidates, Check, "PositiveCandidates");
        CheckImmutable(ModernNeowIdentityRules.CurseCandidates, Check, "CurseCandidates");
        CheckImmutable(ModernNeowIdentityRules.BinaryChoices, Check, "BinaryChoices");
        CheckCaptured113Catalog(Check);
        CheckLiveGetterStability(Check);

        foreach (int players in Enumerable.Range(1, 4))
        foreach (int slot in Enumerable.Range(0, players))
        foreach (bool? unlocked in new bool?[] { false, true, null })
        foreach (bool? scroll in new bool?[] { false, true, null })
        {
            var authority = Authority(players, slot, unlocked, scroll);
            string scope = $"p{players}s{slot}/unlocked={unlocked}/scroll={scroll}";
            var prepared = ModernNeowIdentityRules.Prepare(players, unlocked, scroll);
            ModelKey[] eligibleCurses = OriginalCurses.Where(k => Allowed(k, players, unlocked, scroll) != false).ToArray();
            Check(prepared.EligibleCurses.SequenceEqual(eligibleCurses), scope + ".EligibleCurseOrder");
            Check(prepared.UnknownEligibilityAffectedPool == (!unlocked.HasValue || !scroll.HasValue), scope + ".UnknownPrecision");
            CheckImmutable(prepared.EligibleCurses, Check, scope + ".EligibleCurses");
            CheckImmutable(prepared.CursePools, Check, scope + ".CursePools");
            Check(prepared.CursePools.Count() == OriginalCurses.Length, scope + ".IncludesIneligibleCurseRows");
            foreach (var curse in OriginalCurses)
            {
                var row = prepared.ForCurse(curse);
                Check(row.Curse == curse, scope + ".RowIdentity");
                Check(row.FixedCandidates.SequenceEqual(OriginalPositives.Where(k =>
                    !Conflict(curse, k) && Allowed(k, players, unlocked, scroll) != false)), scope + ".FixedOrder:" + curse);
                Check(row.AppendChoices.SelectMany(c => new[] { c.TrueCandidate, c.FalseCandidate }).SequenceEqual(
                    curse == R.LargeCapsule ? OriginalBinaryCandidates.Skip(2) : OriginalBinaryCandidates), scope + ".AppendOrder:" + curse);
                CheckImmutable(row.FixedCandidates, Check, scope + ".FixedCandidates");
                CheckImmutable(row.AppendChoices, Check, scope + ".AppendChoices");
            }
            string preparedBefore = string.Join("|", prepared.CursePools.Select(row =>
                row.Curse + ":" + string.Join(",", row.FixedCandidates) + ":" + string.Join(",", row.AppendChoices)));

            var catalog = Beta110FastEffectCatalogCompiler.Compile(authority.EffectAuthority, []);
            var context = new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 0, authority,
                new("0.111.0", "0.111.0", "beta", "NeowIdentityRules.Fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
            var numerical = NeowNumericCompilation.BuildAuthority(context, catalog, needsBones: true);
            bool known = unlocked.HasValue && scroll.HasValue;
            Check(numerical.IdentityAuthorityExact == known && numerical.CurseIdentityAuthorityExact == known,
                scope + ".NumericalPrecisionAdmission");
            Check(ModernNeowIdentityRules.TryGetEligibleCursePool(authority, out var exactCurses) == known,
                scope + ".ExactPoolPrecisionAdmission");
            if (known) Check(exactCurses.SequenceEqual(eligibleCurses), scope + ".ExactPoolOrder");
            Check(ModernNeowIdentityRules.TryEstimatePositiveOfferProbability(authority, R.GoldenPearl, out _, out _) == known,
                scope + ".ProbabilityPrecisionAdmission");
            if (known && slot == 0)
            {
                var marginals = OriginalMarginals(players, unlocked, scroll);
                var visited = marginals.Keys.ToDictionary(k => k, _ => 0d);
                double massSum = 0;
                Check(ModernNeowIdentityRules.TryVisitOfferSpace(authority, (a, b, c, mass) =>
                {
                    massSum += mass;
                    visited[a] += mass; visited[b] += mass; visited[c] += mass;
                }), scope + ".OfferSpaceExact");
                Check(Math.Abs(massSum - 1) < 1e-10, scope + ".OfferSpaceTotalMass");
                foreach (var (key, expected) in marginals)
                    Check(Math.Abs(visited[key] - expected) < 1e-10, scope + ".IndependentOfferMarginal:" + key);
            }
            Check(ReferenceEquals(numerical.Identity, NeowIdentityOfferFacts.For(players, unlocked == true, scroll == true)), scope + ".FrozenNumericSelection");
            var changedEligibility = numerical with { AllCharacterCardPoolsUnlocked = !numerical.AllCharacterCardPoolsUnlocked };
            Check(!ReferenceEquals(numerical.Identity, changedEligibility.Identity), scope + ".RecordCloneEligibilityRefresh");
            var changedPlayers = numerical with { PlayersCount = players == 1 ? 2 : 1 };
            Check(!ReferenceEquals(numerical.Identity, changedPlayers.Identity), scope + ".RecordClonePlayerRefresh");
            var plan = new NeowReplayPlan(numerical, NeowSearchFilter.Empty, Beta110FastDomain.None,
                [], [], [], 0, 0, 0, 0, 0, 0, 255, 255, 255, true, false, false, []);
            var seenCurses = new HashSet<ModelKey>();
            for (int i = 0; i < 256; i++)
            {
                ulong root = i < 4 ? new[] { 0UL, 1UL, ulong.MaxValue, ulong.MaxValue - 1 }[i]
                    : unchecked((ulong)i * 0x9e3779b97f4a7c15UL);
                var original = OriginalOffer(root, players, slot, unlocked, scroll);
                var actual = ModernNeowIdentityPredictor.PredictModernCore(root, authority, Beta111Profile.Instance, exactAuthority: known);
                string at = scope + "/root=" + root;
                Check(actual.RelicKeys.SequenceEqual(original.Keys), at + ".OrderedCompleteOffer");
                Check(actual.RngCalls == original.Calls, at + ".EventDrawCount");
                Check(actual.ExactAuthority == known && actual.UnknownEligibilityAffectedPool == !known, at + ".PredictionPrecision");
                seenCurses.Add(original.Keys[2]);
                offers++;
                if (original.Keys[2] == R.LargeCapsule)
                {
                    capsuleOffers++;
                    Check(original.BinaryDraws == 2 && !original.Pool.Contains(R.LavaRock) && !original.Pool.Contains(R.SmallCapsule),
                        at + ".LargeCapsuleSkipsFirstBinaryDraw");
                }
                if (!known) continue;

                var observed = NeowFamilyReplay.Observe(root, plan);
                Check(new[] { Beta110FastRelicCatalog.KeyOf(observed.First), Beta110FastRelicCatalog.KeyOf(observed.Second),
                    Beta110FastRelicCatalog.KeyOf(observed.Curse) }.SequenceEqual(original.Keys), at + ".NumericalOrderedOffer");
                byte drawnCurse = NeowFamilyReplay.ObserveCurse(root, plan, out var eventRng);
                var curseRng = new Xoshiro256StarStar(unchecked(root + (ulong)slot + XxHash64.Hash("NEOW"u8, 0)));
                _ = curseRng.NextInt(eligibleCurses.Length);
                Check(Beta110FastRelicCatalog.KeyOf(drawnCurse) == original.Keys[2] && SameState(eventRng, curseRng),
                    at + ".CurseDrawCheckpoint");
                ModelKey[] bonesPool = authority.EffectAuthority!.BonesEligibleRelics!.Where(k => k != R.NeowsBones).ToArray();
                var rewards = new Xoshiro256StarStar(unchecked(root + (ulong)slot + XxHash64.Hash("rewards"u8, 0)));
                rewards.UnstableShuffle(bonesPool);
                Check(Beta110FastRelicCatalog.KeyOf(observed.BonesFirst) == bonesPool[0] &&
                    Beta110FastRelicCatalog.KeyOf(observed.BonesSecond) == bonesPool[1] && SameState(observed.RewardsAfterBones, rewards),
                    at + ".BonesOrderAndFullRewardsCheckpoint");
                numericalOffers++;
            }
            Check(seenCurses.SetEquals(eligibleCurses), scope + ".EveryEligibleCurseExercised");
            _ = ModernNeowIdentityRules.Prepare(players == 1 ? 4 : 1, unlocked != true, scroll != true);
            Check(preparedBefore == string.Join("|", prepared.CursePools.Select(row =>
                row.Curse + ":" + string.Join(",", row.FixedCandidates) + ":" + string.Join(",", row.AppendChoices))),
                scope + ".PreparationAndPredictionDoNotMutateFrozenFacts");
        }
        CheckCoOffers(Check);
        CheckBonesConflictScope(Check);
        Check(capsuleOffers > 0, "LargeCapsuleExercised");
        Console.WriteLine($"NEOW_IDENTITY_RULES=PASS;checks={checks};coreOffers={offers};numericalOffers={numericalOffers};largeCapsuleOffers={capsuleOffers};oracle=da7f1ae5CompleteOffer;players=1..4;eligibility=true,false,unknown");
        return 0;
    }

    private sealed class CatalogFixture
    {
        public bool Reverse { get; init; }
        public IEnumerable<object> PositiveOptions => new[] { "ONE", "TWO", "TWO" }
            .Select(entry => (object)new { Relic = new { Id = new { Category = "RELIC", Entry = entry } } });
    }

    private static void ReorderOffers(CatalogFixture __instance, ref IEnumerable<object> __result)
    {
        if (__instance.Reverse) __result = __result.Reverse();
    }

    private static void CheckLiveGetterStability(Action<bool, string> check)
    {
        var getter = AccessTools.PropertyGetter(typeof(CatalogFixture), nameof(CatalogFixture.PositiveOptions));
        var harmony = new Harmony("rt2.tests.neow.read-ordered-rows");
        try
        {
            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(NeowIdentityRulesChecks), nameof(ReorderOffers)));
            foreach (bool reverse in new[] { false, true })
            {
                var unresolved = new HashSet<ModelKey>();
                check(ReflectionNeowEffectSnapshotAdapter.TryReadNeowOptionRelics(new CatalogFixture { Reverse = reverse },
                    nameof(CatalogFixture.PositiveOptions), true, unresolved, out var actual), "ReadablePatchedContent");
                check(actual.Select(k => k.Entry).SequenceEqual(reverse ? ["TWO", "TWO", "ONE"] : ["ONE", "TWO", "TWO"]),
                    "ReadActualOrderAndMultiplicity");
                check(unresolved.SetEquals(actual), "UnknownEligibilityPreservesIdentities");
            }
        }
        finally { harmony.Unpatch(getter, HarmonyPatchType.Postfix, harmony.Id); }
    }

    private static void CheckCaptured113Catalog(Action<bool, string> check)
    {
        var baseline = Authority(1, 0, true, true);
        var extra = new ModelKey("RELIC", "CUSTOM_OPTION");
        var catalog = new NeowOfferCatalog([R.LavaRock, extra, R.NewLeaf, R.GoldenPearl],
            [R.LeafyPoultice, R.LargeCapsule], null);
        RuntimeContextAuthoritySnapshot Capture(NeowOfferCatalog offers, string version = "0.113.1") => new(baseline.Character, 0, 0, 1, true, false, null, null, null,
            version, baseline.ProfileId, true, null, "sts2", baseline.SourceAuthority, baseline.Completeness,
            baseline.ResolutionStatus, baseline.EffectAuthority, capturedNeowOffers: offers);
        var changed = Capture(catalog);
        check(changed.HasExactModernNeowIdentityInputs && changed.IsBeta111NeowIdentityAuthorityExact, "ChangedCatalogAuthority");
        var uncertain = Capture(catalog with { UnresolvedEligibility = [extra] });
        check(!uncertain.HasExactModernNeowIdentityInputs &&
            SeedModelAssumptions.TryCreate(uncertain, out var model, out var reasons, SeedPredictionDomainSelection.Neow) &&
            model.NeowPreparation.ForCurse(R.LargeCapsule).FixedCandidates.Contains(extra) &&
            reasons[SeedPredictionDomainSelection.Neow].Contains("UnresolvedEligibilityIncluded"),
            "UnmodeledEligibilityKeepsActualPoolAndAssumption");
        var shortPool = Capture(catalog with { Positives = [], Curses = [R.LargeCapsule],
            BinaryChoices = [
                new(R.SmallCapsule, R.LavaRock, R.LargeCapsule),
                new(R.MassiveScroll, R.MassiveScroll), // Neither side is eligible in solo.
                new(extra, R.MassiveScroll)
            ] });
        var menuSizes = new HashSet<int>();
        for (ulong seed = 0; seed < 32; seed++)
        {
            var game = new MegaCrit.Sts2.Core.Random.Rng(seed, "NEOW");
            game.NextInt(1);
            game.NextBool();
            bool extraPresent = game.NextBool();
            var menu = ModernNeowIdentityPredictor.PredictModernCore(seed, shortPool, Beta111Profile.Instance, true);
            check(menu.RelicKeys.SequenceEqual(extraPresent ? [extra, R.LargeCapsule] : [R.LargeCapsule]),
                "ReadBinaryAlternativesAndRespectEligibility." + seed);
            menuSizes.Add(menu.RelicKeys.Count);
        }
        check(menuSizes.SetEquals([1, 2]), "NativeTakeDoesNotRequireTwoPositives");
        var nativeCatalog = new NeowOfferCatalog(ModernNeowIdentityRules.PositiveCandidates,
            ModernNeowIdentityRules.CurseCandidates, [.. BaseGameModelKeys.Relics.AllNeow]);
        var implicitDefault = new RuntimeContextAuthoritySnapshot(baseline.Character, 0, 0, 1, true, true, true, 16, 16,
            "0.113.1", baseline.ProfileId, true, null, "sts2", baseline.SourceAuthority, baseline.Completeness,
            baseline.ResolutionStatus, baseline.EffectAuthority);
        RuntimeContextAuthoritySnapshot Explicit(NeowOfferCatalog input) => new(baseline.Character, 0, 0, 1, true, true,
            true, 16, 16, "0.113.1", baseline.ProfileId, true, null, "sts2", baseline.SourceAuthority, baseline.Completeness,
            baseline.ResolutionStatus, baseline.EffectAuthority, capturedNeowOffers: input);
        check(Explicit(nativeCatalog).CapturedNeowOffers == null &&
            Explicit(nativeCatalog).CatalogFingerprint == implicitDefault.CatalogFingerprint, "DefaultCaptureKeepsHistoricalFingerprint");
        check(Explicit(nativeCatalog with { UnresolvedEligibility = [R.SmallCapsule] }).CatalogFingerprint !=
            implicitDefault.CatalogFingerprint && Capture(catalog).CatalogFingerprint != uncertain.CatalogFingerprint,
            "ChangedEligibilityHasDistinctFingerprint");
        var overlap = catalog with
        {
            Positives = [R.LavaRock], Curses = [R.LargeCapsule],
            BinaryChoices = [
                new(R.LavaRock, R.SmallCapsule, R.LargeCapsule),
                new(R.SmallCapsule, R.SmallCapsule),
                new(R.MassiveScroll, R.MassiveScroll)
            ]
        };
        check(Capture(overlap).CatalogFingerprint != Capture(overlap with { BinaryChoices = ModernNeowIdentityRules.BinaryChoices })
            .CatalogFingerprint, "ChangedBinaryContentHasDistinctFingerprint");
        var options = ProductionSearchReplay.JsonOptions;
        var staged = Capture(overlap with { UnresolvedEligibility = [extra] });
        var restored = JsonSerializer.Deserialize<RuntimeContextAuthoritySnapshot>(
            JsonSerializer.Serialize(staged, options), options)!;
        check(restored.CapturedNeowOffers!.BinaryChoices.SequenceEqual(overlap.BinaryChoices) &&
            restored.CapturedNeowOffers.UnresolvedEligibility.SequenceEqual([extra]) &&
            restored.CatalogFingerprint == staged.CatalogFingerprint, "MutableOfferContentRoundTripsInReplay");
        var historical = JsonSerializer.SerializeToNode(overlap, options)!.AsObject();
        historical.Remove(nameof(NeowOfferCatalog.BinaryChoices));
        historical.Remove(nameof(NeowOfferCatalog.UnresolvedEligibility));
        var earlier = historical.Deserialize<NeowOfferCatalog>(options)!;
        check(earlier.BinaryChoices.SequenceEqual(ModernNeowIdentityRules.BinaryChoices) &&
            earlier.UnresolvedEligibility.IsEmpty, "OlderSavedCatalogUsesHistoricalBinaryDefaults");
        historical[nameof(NeowOfferCatalog.BinaryChoices)] = new JsonArray();
        check(historical.Deserialize<NeowOfferCatalog>(options)!.BinaryChoices.IsEmpty, "ExplicitEmptyBinaryListPreserved");
        var oldShape = RolltheSpire2.Core.Authority.Runtime.RuntimePredictionInputIdentity.AuthorityPayload(changed)!["CapturedNeowOffers"]!;
        var newShape = RolltheSpire2.Core.Authority.Runtime.RuntimePredictionInputIdentity.AuthorityPayload(staged)!["CapturedNeowOffers"]!;
        check(oldShape["BinaryChoices"] == null && oldShape["UnresolvedEligibility"] == null &&
            newShape["BinaryChoices"] is JsonArray { Count: 3 } && newShape["UnresolvedEligibility"] is JsonArray { Count: 1 },
            "ContinuationPreservesLegacyInputsAndDistinguishesChangedBinaryContent");
        var oldRules = JsonSerializer.Deserialize<RolltheSpire2.Core.World.Snapshots.Beta109AncientRuleObservation>(
            """{"PaelRelics":[],"SharedAncientsWithUnknownGenerator":[],"PatchedAncientGenerators":["PAEL"],"StandardOptionWrapper":false}""", options)!;
        check(!JsonSerializer.Serialize(oldRules, options).Contains("StandardOptionWrapper"), "OlderAncientPatchFlagsConsumed");
        foreach (var version in new[] { "0.111.1", "0.113.1" })
        {
            var captured = Capture(overlap, version);
            var picker = RolltheSpire2.Ui.Pages.Search.Neow.NeowSearchUiCatalog.FromAuthority(captured);
            check(captured.HasExactModernNeowIdentityInputs && picker.RouteRelics.ToHashSet().SetEquals(
                [R.LavaRock, R.SmallCapsule, R.LargeCapsule]), version + ".PickerUsesCapturedOfferBranches");
            check(ModernNeowIdentityRules.TryEstimateOfferProbability(captured,
                (a, b, c) => new[] { a, b, c }.ToHashSet().SetEquals(picker.RouteRelics), out var probability, out _) &&
                Math.Abs(probability - 1) < 1e-12, version + ".ModelAllowsFormerBinarySiblingsTogether");
            var offers = ModernNeowIdentityPredictor.PredictModernCore(17, captured, Beta111Profile.Instance, true);
            check(offers.RelicKeys.ToHashSet().SetEquals(picker.RouteRelics), version + ".OrderedInputsReachPrediction");
            var altered = new SearchContext(captured.ProfileId, captured.Character.CharacterKey, 0, captured,
                new(version, version, "stable", "ChangedBinary", true, ""), SearchEvaluationAssumptions.BroadDefault);
            var fast = NeowNumericCompilation.BuildAuthority(altered,
                Beta110FastEffectCatalogCompiler.Compile(captured.EffectAuthority, []), false);
            check(!fast.IdentityAuthorityExact && !fast.CurseIdentityAuthorityExact, version + ".FixedFastRetires");
        }
        var context = new SearchContext(changed.ProfileId, changed.Character.CharacterKey, 0, changed,
            new("0.113.1", "0.113.1", "beta", "ChangedNeowCatalog", true, ""), SearchEvaluationAssumptions.BroadDefault);
        var numeric = NeowNumericCompilation.BuildAuthority(context, Beta110FastEffectCatalogCompiler.Compile(changed.EffectAuthority, []), false);
        check(!numeric.IdentityAuthorityExact && !numeric.CurseIdentityAuthorityExact, "StaticFastRetiresForChangedOrder");
        for (ulong root = 0; root < 64; root++)
        {
            var native = new MegaCrit.Sts2.Core.Random.Rng(root, "NEOW");
            var curse = catalog.Curses[native.NextInt(2)];
            var positives = catalog.Positives.Where(k => curse != R.LeafyPoultice || k != R.NewLeaf).ToList();
            if (curse != R.LargeCapsule) positives.Add(native.NextBool() ? R.LavaRock : R.SmallCapsule);
            positives.Add(native.NextBool() ? R.NutritiousOyster : R.StoneHumidifier);
            positives.Add(native.NextBool() ? R.NeowsTalisman : R.Pomander);
            var expected = positives.UnstableShuffle(native).Take(2).Append(curse);
            var actual = ModernNeowIdentityPredictor.PredictModernCore(root, changed, Beta111Profile.Instance, true);
            check(actual.RelicKeys.SequenceEqual(expected) && native.ToSerializable().counter == actual.RngCalls,
                "Native113ChangedNeowOrderAndDraws:" + root);
        }
    }

    private static RuntimeContextAuthoritySnapshot Authority(int players, int slot, bool? unlocked, bool? scroll)
    {
        var fixture = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 0, slot, players);
        return new(fixture.Character, 0, slot, players, true, true, unlocked,
            scroll.HasValue ? scroll.Value ? 16 : 0 : null, scroll.HasValue ? 16 : null,
            fixture.GameVersion, fixture.ProfileId, true, null, fixture.SourceAssembly, fixture.SourceAuthority,
            fixture.Completeness, fixture.ResolutionStatus, fixture.EffectAuthority,
            predictionGameMode: fixture.PredictionGameMode, predictionGameModeAuthority: fixture.PredictionGameModeAuthority);
    }

    private static (ModelKey[] Keys, ModelKey[] Pool, int Calls, int BinaryDraws) OriginalOffer(
        ulong root, int players, int slot, bool? unlocked, bool? scroll)
    {
        var rng = new Xoshiro256StarStar(unchecked(root + (ulong)slot + XxHash64.Hash("NEOW"u8, 0)));
        ModelKey[] curses = OriginalCurses.Where(k => Allowed(k, players, unlocked, scroll) != false).ToArray();
        ModelKey curse = curses[rng.NextInt(curses.Length)];
        var positives = OriginalPositives.Where(k => !Conflict(curse, k)).ToList();
        int binaryDraws = 2;
        if (curse != R.LargeCapsule)
        {
            positives.Add(rng.NextBool() ? R.LavaRock : R.SmallCapsule);
            binaryDraws++;
        }
        positives.Add(rng.NextBool() ? R.NutritiousOyster : R.StoneHumidifier);
        positives.Add(rng.NextBool() ? R.NeowsTalisman : R.Pomander);
        positives.RemoveAll(k => Allowed(k, players, unlocked, scroll) == false);
        rng.UnstableShuffle(positives);
        return ([positives[0], positives[1], curse], positives.ToArray(), rng.CallCount, binaryDraws);
    }

    private static bool? Allowed(ModelKey key, int players, bool? unlocked, bool? scroll) =>
        key == R.MassiveScroll ? players > 1 :
        key == R.Kaleidoscope ? unlocked : key == R.ScrollBoxes ? scroll :
        key == R.WingedBoots || key == R.SilverCrucible ? players == 1 : true;

    private static Dictionary<ModelKey, double> OriginalMarginals(int players, bool? unlocked, bool? scroll)
    {
        var result = OriginalCurses.Concat(OriginalPositives).Concat(OriginalBinaryCandidates).ToDictionary(k => k, _ => 0d);
        ModelKey[] curses = OriginalCurses.Where(k => Allowed(k, players, unlocked, scroll) == true).ToArray();
        foreach (ModelKey curse in curses)
        foreach (ModelKey? capsule in curse == R.LargeCapsule
            ? new ModelKey?[] { null } : new ModelKey?[] { R.LavaRock, R.SmallCapsule })
        foreach (ModelKey oyster in new[] { R.NutritiousOyster, R.StoneHumidifier })
        foreach (ModelKey talisman in new[] { R.NeowsTalisman, R.Pomander })
        {
            var positives = OriginalPositives.Where(k => !Conflict(curse, k)).ToList();
            if (capsule.HasValue) positives.Add(capsule.Value);
            positives.Add(oyster); positives.Add(talisman);
            positives.RemoveAll(k => Allowed(k, players, unlocked, scroll) == false);
            double variantMass = 1d / (curses.Length * (capsule.HasValue ? 8 : 4));
            result[curse] += variantMass;
            foreach (ModelKey positive in positives) result[positive] += variantMass * 2 / positives.Count;
        }
        return result;
    }

    private static bool Conflict(ModelKey curse, ModelKey positive) =>
        curse == R.CursedPearl && positive == R.GoldenPearl ||
        curse == R.HeftyTablet && positive == R.ArcaneScroll ||
        curse == R.LargeCapsule && (positive == R.LavaRock || positive == R.SmallCapsule) ||
        curse == R.LeafyPoultice && positive == R.NewLeaf ||
        curse == R.PrecariousShears && positive == R.PreciseScissors ||
        curse == R.NeowsSacrifice && (positive == R.PhialHolster || positive == R.LostCoffer);

    private static bool SameState(Beta110FastRng actual, Xoshiro256StarStar expected)
    {
        var a = actual.CaptureCheckpoint();
        var e = expected.State;
        return a.S0 == e.S0 && a.S1 == e.S1 && a.S2 == e.S2 && a.S3 == e.S3;
    }

    private static void CheckImmutable<T>(IEnumerable<T> values, Action<bool, string> check, string scope)
    {
        check(values is not T[], scope + ".NoExposedArray");
        if (values is ICollection<T> collection) check(collection.IsReadOnly, scope + ".ReadOnlyCollection");
        if (values is IList<T> list && list.Count > 0)
        {
            bool rejected = false;
            try { list[0] = list[0]; }
            catch (NotSupportedException) { rejected = true; }
            check(rejected, scope + ".MutationRejected");
        }
    }

    private static void CheckCoOffers(Action<bool, string> check)
    {
        var candidates = OriginalCurses.Concat(OriginalPositives).Concat(OriginalBinaryCandidates).ToArray();
        check(ModernNeowIdentityRules.CanCoOffer([]), "EmptyCoOffer");
        foreach (ModelKey a in candidates)
        {
            check(ModernNeowIdentityRules.CanCoOffer([a, a, a, a]), "DuplicatesDoNotConsumeOfferSlots:" + a);
            foreach (ModelKey b in candidates)
            foreach (ModelKey c in candidates)
            {
                ModelKey[] selected = new[] { a, b, c }.Distinct().ToArray();
                ModelKey[] curses = selected.Where(OriginalCurses.Contains).ToArray();
                ModelKey[] positives = selected.Except(curses).ToArray();
                bool expected = curses.Length <= 1 && positives.Length <= 2 &&
                    !Pair(positives, R.LavaRock, R.SmallCapsule) &&
                    !Pair(positives, R.NutritiousOyster, R.StoneHumidifier) &&
                    !Pair(positives, R.NeowsTalisman, R.Pomander) &&
                    (curses.Length == 0 || positives.All(p => !Conflict(curses[0], p)));
                check(ModernNeowIdentityRules.CanCoOffer(selected) == expected, $"CoOffer:{a}/{b}/{c}");
            }
        }
        check(!ModernNeowIdentityRules.CanCoOffer([R.ArcaneScroll, R.GoldenPearl, R.NewLeaf, R.CursedPearl]), "TooManyDistinctOffers");
        check(!ModernNeowIdentityRules.CanCoOffer([new ModelKey(BaseGameModelKeys.Categories.Relic, "NOT_A_NEOW_OFFER")]), "UnknownIdentityRejected");
        static bool Pair(ModelKey[] values, ModelKey a, ModelKey b) => values.Contains(a) && values.Contains(b);
    }

    private static void CheckBonesConflictScope(Action<bool, string> check)
    {
        foreach (int players in Enumerable.Range(1, 4))
        {
            var authority = Authority(players, players - 1, true, true);
            ModelKey[] pool = [R.NeowsSacrifice, R.PhialHolster, R.LostCoffer];
            authority = authority.WithEffectAuthority(authority.EffectAuthority! with { BonesEligibleRelics = pool });
            var context = new SearchContext(RuntimeProfileId.Beta111, authority.Character.CharacterKey, 0, authority,
                new("0.111.0", "0.111.0", "beta", "BonesConflictScope.Fixture", true, ""), SearchEvaluationAssumptions.BroadDefault);
            var numeric = NeowNumericCompilation.BuildAuthority(context,
                Beta110FastEffectCatalogCompiler.Compile(authority.EffectAuthority, []), true);
            check(numeric.BonesEligibleRelicIds.Select(Beta110FastRelicCatalog.KeyOf).SequenceEqual(pool), "BonesPoolPreservesConflictingTopLevelIdentities");
            var plan = new NeowReplayPlan(numeric, NeowSearchFilter.Empty, Beta110FastDomain.None,
                [], [], [], 0, 0, 0, 0, 0, 0, 255, 255, 255, true, false, false, []);
            bool witnessed = false;
            for (ulong root = 0; root < 64; root++)
            {
                var observed = NeowFamilyReplay.Observe(root, plan);
                ModelKey[] pair = [Beta110FastRelicCatalog.KeyOf(observed.BonesFirst), Beta110FastRelicCatalog.KeyOf(observed.BonesSecond)];
                witnessed |= pair.Contains(R.NeowsSacrifice) && (pair.Contains(R.PhialHolster) || pair.Contains(R.LostCoffer));
            }
            check(witnessed, "BonesPickupCanContainSacrificeAndTopLevelConflict:players=" + players);
        }
    }
}
