using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class FamilyWorkEstimateReplayChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException("ReplayWork: " + message); }
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext source)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, source),
                new("000000000000", 65536, 16, 4));
            return result.Plan ?? throw new InvalidOperationException("ReplayWork.Compile: " + result.Issue);
        }
        SearchContext Effects(NeowEffectAuthoritySnapshot effects) => captured with {
            Authority = captured.Authority.WithEffectAuthority(effects) };
        var effects = captured.Authority.EffectAuthority!;
        var cards = effects.CharacterRewardPool!;
        var shop = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool!;
        var unread = Effects(effects with { MerchantColorlessOrderedPool = [.. shop, shop[0]],
            SnapshotFingerprint = effects.SnapshotFingerprint + ":replay-unread" });
        var common = cards.First(c => c.Rarity == EffectCardRarity.Common);
        var moreCommon = Effects(effects with { CharacterRewardPool = [.. cards, common with {
            PoolOrder = cards.Max(c => c.PoolOrder) + 1 }], SnapshotFingerprint = effects.SnapshotFingerprint + ":replay-common" });
        var old = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("UnmeasuredReplayWorkDevice", "vulkan");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            SearchContext Party(SearchContext source) => (SearchContext)typeof(CombinationRegressionChecks)
                .GetMethod("PartyContext", flags)!.Invoke(null, [source, 2, null, null])!;
            // The retained solo corpus has no multiplayer-only members. Reuse
            // its controlled Party fixture with explicit pool membership facts.
            var massiveEffects = effects with { CharacterRewardPool = [.. cards, .. cards
                .Where(c => c.Rarity == EffectCardRarity.Common).Take(3).Select((c, i) => c with {
                    IsMultiplayerOnly = true, PoolOrder = cards.Max(card => card.PoolOrder) + 1 + i })],
                SnapshotFingerprint = effects.SnapshotFingerprint + ":controlled-multiplayer-membership" };
            var party = Party(Effects(massiveEffects));
            var catalog = Beta110FastEffectCatalogCompiler.Compile(party.Party!.Players[0].EffectAuthority, []);
            ushort target = catalog.MultiplayerRewardPool.Common.Concat(catalog.MultiplayerRewardPool.Uncommon)
                .Concat(catalog.MultiplayerRewardPool.Rare).First();
            var massive = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.MassiveScroll),
                StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.MassiveScroll, NeowStructuredConditionKind.ExactSingle,
                    NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [catalog.KeyOf(target)])] };
            var table = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = massive },
                new(1, ModelKeySetFilter.Empty)] };
            var extra = common with { CardKey = new("CARD", "RT2_COST_MASSIVE_EXTRA"), InstanceId = "RT2_COST_MASSIVE_EXTRA",
                IsMultiplayerOnly = true, PoolOrder = cards.Max(c => c.PoolOrder) + 1 };
            var moreMassive = Party(Effects(massiveEffects with { CharacterRewardPool = [.. massiveEffects.CharacterRewardPool!, extra],
                SnapshotFingerprint = effects.SnapshotFingerprint + ":replay-massive" }));
            FamilyPhysicalQuote PartyQuote(SearchContext source)
            {
                var request = Request(table, source);
                var personal = ExactSearchExecutionRequestFactory.ForPlayer(request, request.CompiledSearch.PlayerSearches[0]);
                var generated = NeowWorkEstimate.Describe(NeowReplayPlan.Compile(personal, false));
                Check(generated.Draws > 0 && generated.PoolVisits > 0,
                    "actual MassiveScroll personal plan has both generation and repeated-selection work");
                var unit = FamilyExecutionCoordinator.Plan(request, gpuAvailable: false).ExecutionOrderUnits
                    .OfType<NeowFamily.PartyInitial>().Single();
                var quote = unit.QuotePhysicalWork(new(false, false, 65536))!;
                Check(FamilyCoarsePricing.IsEstimate(quote) && quote.Evidence.Contains("N.PartyInitial.WorkEstimate", StringComparison.Ordinal),
                    "actual PartyInitial CPU consumes the source-work estimate");
                return quote;
            }
            var partyPrice = PartyQuote(party);
            Check(PartyQuote(moreMassive).NanosecondsPerInput > partyPrice.NanosecondsPerInput,
                "MassiveScroll three-card generation responds to expanded read multiplayer pool through wrapper");
            Check(PartyQuote(Party(Effects(massiveEffects with { MerchantColorlessOrderedPool = [.. shop, shop[0]],
                SnapshotFingerprint = effects.SnapshotFingerprint + ":massive-unread" }))).NanosecondsPerInput == partyPrice.NanosecondsPerInput,
                "MassiveScroll wrapper has no unread merchant-pool fee");

            var capsule = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/v1/cases/NR-small-capsule.json").Query;
            SearchQuery WithCompanion(ModelKey companion, bool ordered = false) => capsule with {
                OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones), OpeningRouteRelicRequirement = new(
                    BaseGameModelKeys.Relics.NeowsBones, [BaseGameModelKeys.Relics.SmallCapsule, companion],
                    ordered ? BonesRouteOrderMode.ExactOrder : BonesRouteOrderMode.AnyOrder) };
            (FamilyCpuExecution Cpu, FamilyPhysicalQuote Quote) RelicQuote(SearchQuery query, SearchContext source, bool compiled)
            {
                var request = Request(query, source);
                var route = NeowReplayPlan.Compile(request, true);
                Check((CapsuleRelicCpuPlan.TryCompile(request, route) is not null) == compiled,
                    "expected actual Capsule CPU realization");
                var owner = FamilyExecutionCoordinator.CreateRegisteredFamilies(request).Single(f => f is CapsuleRelicFamily);
                var cpu = (FamilyCpuExecution)FamilyCpuExecution.ConfiguredRealizations(owner).Single();
                var quote = cpu.Quote(new(false, false, 65536), new HashSet<string>())!;
                Check(FamilyCoarsePricing.IsEstimate(quote), "actual configured Capsule CPU quote is estimated");
                Check(cpu.Quote(new(false, false, 65536), new HashSet<string> { "N.Neow" })!.NanosecondsPerInput == quote.NanosecondsPerInput,
                    "R continuation does not acquire or price another N filtering obligation");
                return (cpu, quote);
            }
            var scroll = WithCompanion(BaseGameModelKeys.Relics.ScrollBoxes);
            var scrollPrice = RelicQuote(scroll, captured, false).Quote;
            Check(RelicQuote(scroll, moreCommon, false).Quote.NanosecondsPerInput > scrollPrice.NanosecondsPerInput,
                "reference Capsule CPU accounts for unobserved ScrollBoxes generation/scan work");
            Check(RelicQuote(scroll, unread, false).Quote.NanosecondsPerInput == scrollPrice.NanosecondsPerInput,
                "R reference continuation ignores unread merchant pool");
            Check(RelicQuote(WithCompanion(BaseGameModelKeys.Relics.ScrollBoxes, true), captured, false).Quote.NanosecondsPerInput <
                scrollPrice.NanosecondsPerInput, "one authored route does not pay both companion replays");
            Check(scrollPrice.Evidence.Contains("NoAdditionalNFilterCharge", StringComparison.Ordinal),
                "reference continuation evidence retains its R-local boundary");

            var rare = cards.First(c => c.Rarity == EffectCardRarity.Rare);
            var moreRare = Effects(effects with { CharacterRewardPool = [.. cards, rare with {
                PoolOrder = cards.Max(c => c.PoolOrder) + 1 }], SnapshotFingerprint = effects.SnapshotFingerprint + ":replay-rare" });
            var arcane = WithCompanion(BaseGameModelKeys.Relics.ArcaneScroll);
            Check(RelicQuote(arcane, moreRare, false).Quote.NanosecondsPerInput == RelicQuote(arcane, captured, false).Quote.NanosecondsPerInput,
                "reference Arcane direct index has fixed generation but no linear rare-pool charge");
            var neutral = WithCompanion(BaseGameModelKeys.Relics.NewLeaf);
            var neutralPrice = RelicQuote(neutral, captured, true).Quote;
            Check(RelicQuote(neutral, moreCommon, true).Quote.NanosecondsPerInput == neutralPrice.NanosecondsPerInput &&
                neutralPrice.Evidence.Contains("CompanionReplayDraws=0", StringComparison.Ordinal),
                "compiled neutral companion skips reference-only generation without duplicate work charge");
        }
        finally { SearchPerformanceProfileFoundation.ObserveGpuIdentity(old.GpuIdentity, old.RenderingBackend); }
        Console.WriteLine($"FamilyWorkEstimateReplay checks={checks};PASS");
        return checks;
    }
}
