using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

internal static class EventOpportunityChecks
{
    private const string Version = "0.113.1";
    private const RngSemanticProfileId Semantics = RngSemanticProfileId.Modern113;
    private static readonly ulong[] Roots = Enumerable.Range(0, 32).Select(i => (ulong)i)
        .Concat([1UL << 63, ulong.MaxValue]).ToArray();

    internal static int Run()
    {
        int checks = 0;
        void Check(bool result, string reason)
        {
            checks++;
            if (!result) throw new InvalidOperationException("EventOpportunityChecks: " + reason);
        }

        CheckNativeIdentityDraws(Check);
        CheckNativeRarityDraws(Check);
        CheckSearchExecution(Check);
        Console.WriteLine($"EventOpportunityChecks passed: {checks}");
        return 0;
    }

    private static void CheckNativeIdentityDraws(Action<bool, string> check)
    {
        ModelId[] dolls = [Relic("DAUGHTER_OF_THE_WIND"), Relic("MR_STRUGGLES"), Relic("BING_BONG")];
        ModelId[] museum = [Relic("OBSIDIAN_VICE"), Relic("THUMBSCREW"), Relic("TORMENTOUS_COLLAR")];
        ModelId[] baths =
        [
            Relic("BLESSED_ANTLER"), Relic("BRILLIANT_SCARF"), Relic("DELICATE_FROND"), Relic("DIAMOND_DIADEM"),
            Relic("FUR_COAT"), Relic("GLITTER"), Relic("JEWELRY_BOX"), Relic("SIGNET_RING"), Relic("BEAUTIFUL_BRACELET")
        ];
        foreach (ulong root in Roots)
        for (int slot = 0; slot < 4; slot++)
        {
            var options = Beta111EventOptionResultProjector.Project(root, slot, "DOLL_ROOM", Semantics);
            ModelKey expectedRandom = Key(NativeRng(root, slot, "DOLL_ROOM").NextItem(dolls)!);
            var expectedPaid = dolls.ToList().StableShuffle(NativeRng(root, slot, "DOLL_ROOM")).Take(2).Select(Key);
            check(options.Single(option => option.OptionKey == "RANDOM").Relics.SequenceEqual([expectedRandom]),
                "Doll Room random draw uses the native authored order");
            check(options.Single(option => option.OptionKey == "TAKE_SOME_TIME").Relics.SequenceEqual(expectedPaid),
                "Doll Room paid display uses native sorted shuffle from the entry checkpoint");
            CheckOffer("MUSEUM_OF_TORTURE", "TAKE", museum);
            CheckOffer("THERMAE_NONUPEIPE", "DRINK_WATERS", baths);

            void CheckOffer(string entry, string option, ModelId[] sourceOrder)
            {
                ModelKey expected = Key(NativeRng(root, slot, entry).NextItem(sourceOrder)!);
                var projected = Beta111EventOptionResultProjector.Project(root, slot, entry, Semantics).Single();
                check(projected.OptionKey == option && projected.Relics.SequenceEqual([expected]),
                    $"{entry} displays its native first draw");
            }
        }

        static ModelId Relic(string entry) => new("RELIC", entry);
        static ModelKey Key(ModelId id) => new(id.Category, id.Entry);
    }

    private static void CheckNativeRarityDraws(Action<bool, string> check)
    {
        foreach (var opportunity in EventOpportunityCatalog.RarityBranches)
        foreach (ulong root in Roots)
        for (int slot = 0; slot < SeedPredictionInputLimits.MaximumPlayers; slot++)
        {
            var expected = NativeRarityDraws(root, slot, opportunity);
            var actual = new EffectRelicRarity[opportunity.RollCount];
            bool applicable = EventRelicRarityProjector.Project(root, slot, opportunity, actual);
            check(expected is null ? !applicable : applicable && expected.SequenceEqual(actual),
                $"{opportunity.Branch} native rolled rarity at slot {slot}");

            string entry = opportunity.EventKey.Entry;
            var options = Beta111EventOptionResultProjector.Project(root, slot, entry, Semantics);
            var rarity = options.SingleOrDefault(option => option.RarityBranch == opportunity.Branch);
            check(expected is null ? rarity is null : rarity is not null && rarity.RelicRarityRolls.SequenceEqual(expected),
                $"{opportunity.Branch} seed information preserves native draws and applicability at slot {slot}");
            var previous = Beta111EventOptionResultProjector.Project(root, slot, entry, RngSemanticProfileId.Modern110);
            check(previous.All(option => option.RelicRarityRolls.Count == 0),
                $"{opportunity.Branch} seed information omits the unsupported legacy rarity model");
            if (opportunity.Branch == EventRelicRarityBranch.OfferTribute)
                check(options.Count == 1 && rarity is { OptionKey: "OFFER_TRIBUTE" } &&
                    rarity.GoldAmount == previous.Single().GoldAmount,
                    "Offer Tribute shows its existing cost and rarity in a single option");
            if (opportunity.Branch == EventRelicRarityBranch.Ornate)
                check(options.Count == 2 &&
                    options.Single(option => option.OptionKey == "PLAIN").GoldAmount == previous.Single().GoldAmount,
                    "The Ornate relic roll preserves the separate Plain gold option");
        }
    }

    private static EffectRelicRarity[]? NativeRarityDraws(ulong root, int slot, EventRelicRarityOpportunity opportunity)
    {
        var rng = NativeRng(root, slot, opportunity.EventKey.Entry, opportunity.Branch == EventRelicRarityBranch.Setting3);
        switch (opportunity.Branch)
        {
            case EventRelicRarityBranch.OfferTribute:
                _ = rng.NextInt(0, 50);
                break;
            case EventRelicRarityBranch.Ornate:
                _ = rng.NextInt(41, 69);
                break;
            case EventRelicRarityBranch.GivePotion:
            case EventRelicRarityBranch.GiveGold:
            case EventRelicRarityBranch.GiveRelic:
                _ = rng.NextItem(Enumerable.Range(0, 1 + slot).ToArray());
                _ = rng.NextItem(Enumerable.Range(0, 5 + slot).ToArray());
                break;
            case EventRelicRarityBranch.TradeOffers:
                Enumerable.Range(0, 5 + slot).Reverse().ToList().StableShuffle(rng);
                break;
            case EventRelicRarityBranch.MerchantGuilty:
                if (rng.NextInt(3) != 0) return null;
                break;
        }
        return Enumerable.Range(0, opportunity.RollCount)
            .Select(_ => Enum.Parse<EffectRelicRarity>(RelicFactory.RollRarity(rng).ToString())).ToArray();
    }

    private static void CheckSearchExecution(Action<bool, string> check)
    {
        var rarityRows = EventOpportunityCatalog.RarityBranches.SelectMany(opportunity =>
            new[] { EventRelicRaritySelection.First, EventRelicRaritySelection.Second, EventRelicRaritySelection.Third,
                    EventRelicRaritySelection.Any, EventRelicRaritySelection.All }
                .Select(selection => new EventResultSearchCondition(EventResultConditionKind.EventRelicRarityRoll, opportunity.EventKey)
                {
                    RelicRarityBranch = opportunity.Branch,
                    RelicRaritySelection = selection,
                    RelicRarity = EffectRelicRarity.Uncommon
                })
                .Where(row => row.IsValid)
                .Select(row => (Opportunity: opportunity, Row: row)));
        foreach (var (opportunity, row) in rarityRows)
        {
            var request = Prepare(Version, row);
            var filter = new EventResultFamilyPlan(request.Model, prepareGpu: true);
            check(filter.Conditions.SequenceEqual([row]) && !filter.GpuSupported,
                "New CPU rarity predicate retained and excluded from unsupported GPU packing");
            foreach (ulong root in Roots)
            {
                var draws = NativeRarityDraws(root, 0, opportunity);
                var rarity = row.RelicRarity!.Value;
                bool expected = draws is not null && (row.RelicRaritySelection switch
                {
                    EventRelicRaritySelection.First => draws[0] == rarity,
                    EventRelicRaritySelection.Second => draws[1] == rarity,
                    EventRelicRaritySelection.Third => draws[2] == rarity,
                    EventRelicRaritySelection.Any => draws.Any(draw => draw == rarity),
                    EventRelicRaritySelection.All => draws.All(draw => draw == rarity),
                    _ => throw new ArgumentOutOfRangeException(nameof(row))
                });
                CheckDisposition(root, request, filter, expected, check);
            }
        }

        foreach (var (kind, eventEntry, option, target) in new[]
        {
            (EventResultConditionKind.DollRoomRandomRelic, "DOLL_ROOM", "RANDOM", EventOpportunityCatalog.Dolls[2]),
            (EventResultConditionKind.DollRoomTakeSomeTimeRelic, "DOLL_ROOM", "TAKE_SOME_TIME", EventOpportunityCatalog.Dolls[2]),
            (EventResultConditionKind.MuseumOfTortureOfferedRelic, "MUSEUM_OF_TORTURE", "TAKE", EventOpportunityCatalog.MuseumRelics[1]),
            (EventResultConditionKind.ThermaeNonupeipeDrinkRelic, "THERMAE_NONUPEIPE", "DRINK_WATERS", EventOpportunityCatalog.ThermaeRelics[8])
        })
        {
            var request = Prepare(Version, new EventResultSearchCondition(kind, target));
            var filter = new EventResultFamilyPlan(request.Model);
            foreach (ulong root in Roots)
            {
                var expected = Beta111EventOptionResultProjector.Project(root, 0, eventEntry, Semantics)
                    .Single(row => row.OptionKey == option).Relics.Contains(target);
                CheckDisposition(root, request, filter, expected, check);
            }
        }

        var previousRarity = Prepare("0.111.0", rarityRows.First().Row);
        check(new EventResultFamilyPlan(previousRarity.Model).Conditions.Length == 0,
            "Old reward-stream rarity is left outside the event-local CPU predicate");
        var previousExact = ProductionExactSearchEvaluator.Evaluate(previousRarity.Model, TrustedRootHashInput.RootOnly(0)).Evaluation;
        check(previousExact.Disposition == SearchDisposition.Unsupported &&
            previousExact.FailureCode.Contains(EventOpportunitySemantics.ProfileUnavailable(EventResultConditionKind.EventRelicRarityRoll)),
            "Old reward-stream rarity remains unresolved in Exact");

        var crystal = new EventResultSearchCondition(EventResultConditionKind.CrystalSphereRewards, new("EVENT", "CRYSTAL_SPHERE"))
            { CrystalDivinations = 6, RelicRarity = EffectRelicRarity.Common };
        var trial = new EventResultSearchCondition(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL"))
            { TrialCase = TrialCaseTarget.Merchant };
        var withCrystal = Prepare(Version, trial, crystal);
        var oldFilter = new EventResultFamilyPlan(withCrystal.Model);
        check(oldFilter.Conditions.SequenceEqual([trial]) && oldFilter.GpuSupported,
            "Exact-only Crystal Sphere condition leaves existing GPU event filtering available");
        var crystalOnly = Prepare(Version, crystal);
        check(!EventResultFamily.TryCreate(crystalOnly.Model, FilterExecutionPreferences.From(crystalOnly.RunOptions), out _),
            "Crystal-only query does not count an empty CPU family as a participating filter");
        var threeClicks = crystal with { CrystalDivinations = 3 };
        foreach (string version in new[] { "0.111.0", Version })
        {
            var impossible = Compile(version, threeClicks);
            check(impossible.NormalizedQuery.EventResultConditions.SequenceEqual([threeClicks]),
                "The impossible combination retains its authored relic goal and payment branch");
            var feasibility = SearchFeasibilityAnalyzer.Analyze(impossible);
            check(feasibility.Proof?.ReasonCode == SearchImpossibilityReasonCode.CrystalRelicRequiresFourMoves,
                "Workbench feasibility identifies the four-corner proof before starting a scan");
            foreach (bool skipExact in new[] { false, true })
            {
                var rejected = SearchPreparationCompiler.Compile(impossible,
                    new("000000000000", 64, 1, 1, SkipExactValidation: skipExact));
                check(!rejected.Success && rejected.Plan is null && rejected.Disposition == SearchDisposition.NoMatch &&
                    rejected.Issue.StartsWith("SearchImpossible:CrystalRelicRequiresFourMoves:", StringComparison.Ordinal),
                    "Search preparation reports a conventional impossibility before creating its plan");
            }
        }
        var impossibleRelic = CrystalSphereSeedSearch.Evaluate(0, 0, Semantics, 3, new(CrystalSphereSeedCards.None, EffectRelicRarity.Common));
        check(impossibleRelic.Verdict == CrystalSphereSeedVerdict.NoMatch, "The per-seed search shares the same geometric result");
        var both = Prepare(Version, crystal, crystal with { RelicRarity = EffectRelicRarity.Rare });
        foreach (ulong root in Roots.Take(3))
        {
            var expected = CrystalSphereSeedSearch.Evaluate(root, 0, Semantics, 6,
                CrystalSphereSeedTargets.WithRelicRarities(CrystalSphereSeedCards.None,
                    [EffectRelicRarity.Common, EffectRelicRarity.Rare]));
            var observed = ProductionExactSearchEvaluator.Evaluate(both.Model, TrustedRootHashInput.RootOnly(root)).Evaluation;
            check(observed.Disposition == (expected.Verdict switch
            {
                CrystalSphereSeedVerdict.Match => SearchDisposition.Match,
                CrystalSphereSeedVerdict.NoMatch => SearchDisposition.NoMatch,
                _ => SearchDisposition.Unknown
            }), "Two rarities for one six-divination relic share one witness search");
            if (observed.Disposition == SearchDisposition.Match)
                check(observed.Evidence.Count(e => e.OutcomeId is not null) == 2 &&
                    observed.Evidence.Select(e => e.OutcomeId).Distinct().Count() == 1,
                    "Both imported rarity rows use the same revealed-cell sequence");
        }
    }

    private static void CheckDisposition(ulong root, PreparedSearch request, EventResultFamilyPlan filter,
        bool expected, Action<bool, string> check)
    {
        var observation = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.RootOnly(root)).Evaluation;
        check(observation.Disposition == (expected ? SearchDisposition.Match : SearchDisposition.NoMatch),
            $"Exact predicate follows the native result for {filter.Conditions[0].Kind}");
        check(filter.Matches(root) == expected && filter.Reference(root) == expected,
            $"CPU filter and its reference follow Exact for {filter.Conditions[0].Kind}");
    }

    private static PreparedSearch Prepare(string version, params EventResultSearchCondition[] conditions)
    {
        var preparation = SearchPreparationCompiler.Compile(Compile(version, conditions), new("000000000000", 64, 1, 1));
        return preparation.Plan ?? throw new InvalidOperationException("Event query preparation failed: " + preparation.Issue);
    }

    private static CompiledSearch Compile(string version, params EventResultSearchCondition[] conditions)
    {
        var hero = BaseGameModelKeys.Characters.Ironclad;
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(hero, 10, 0, 1, version);
        var context = new SearchContext(RuntimeProfileId.Beta111, hero, 10, authority,
            new(version, version, "beta", "event-opportunity-checks", true, ""), SearchEvaluationAssumptions.BroadDefault);
        return SearchCompiler.Compile(SearchQuery.Empty with { EventResultConditions = conditions }, context);
    }

    private static Rng NativeRng(ulong root, int slot, string entry, bool shared = false) =>
        new(unchecked(root + (shared ? 0UL : (ulong)slot) + StringHelper.GetDeterministicHashCode(entry)));
}
