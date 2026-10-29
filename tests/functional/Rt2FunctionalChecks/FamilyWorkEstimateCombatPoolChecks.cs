using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class FamilyWorkEstimateCombatPoolChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException("CombatPoolWork: " + message); }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var query = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
            "tools/ProductionSearchHost/Corpus/pricing-hole-v1/C-two-targets.json").Query;
        var effects = context.Authority.EffectAuthority!;
        var cards = effects.CharacterRewardPool!;
        var extra = cards.First(card => card.EligibleForPostCombatRewardByPoolMembership && !card.IsMultiplayerOnly &&
            card.IsUnlockedInCapturedPool && card.Rarity == RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common) with {
                CardKey = new("CARD", "RT2_COST_EXTRA_COMMON"), InstanceId = "RT2_COST_EXTRA_COMMON",
                PoolOrder = cards.Max(card => card.PoolOrder) + 1 };
        var expanded = context with { Authority = context.Authority.WithEffectAuthority(effects with {
            CharacterRewardPool = [.. cards, extra], SnapshotFingerprint = effects.SnapshotFingerprint + ":C-read-expanded" }) };
        var shop = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool!;
        var unread = context with { Authority = context.Authority.WithEffectAuthority(effects with {
            MerchantColorlessOrderedPool = [.. shop, shop[0]], SnapshotFingerprint = effects.SnapshotFingerprint + ":C-unread-expanded" }) };

        (CombatRewardFamily Family, FamilyPhysicalQuote Quote) Quote(SearchQuery q, SearchContext c, bool gpu = true)
        {
            var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, c),
                new("000000000000", 65536, 16, 1)).Plan!;
            if (!CombatRewardFamily.TryCreate(request, out var invocation, gpu, GpuNumericalVariant.IntegerRne) ||
                invocation is not CombatRewardFamily family)
                throw new InvalidOperationException("CombatPoolWork.FamilyCompilation");
            var quote = invocation.QuotePhysicalWork(new(false, false, 65536))
                ?? throw new InvalidOperationException("CombatPoolWork.MissingQuote");
            Check(FamilyCoarsePricing.IsEstimate(quote) || !gpu && FamilyCanonicalMeasuredCost.IsCanonicalQuote(quote),
                "actual C entry retains honest estimate or existing CPU measurement priority");
            return (family, quote);
        }

        var old = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("UnmeasuredCombatPoolDevice", "vulkan");
            var genericQuery = query with { OpeningRoute = new(BaseGameModelKeys.Relics.ArcaneScroll) };
            var baseline = Quote(genericQuery, context);
            var larger = Quote(genericQuery, expanded);
            Check(baseline.Family.GpuPlan is { UsesHotLoop: false, NumericalVariant: GpuNumericalVariant.IntegerRne },
                "authored Arcane prefix executes Generic Integer body");
            Check(larger.Quote.NanosecondsPerInput > baseline.Quote.NanosecondsPerInput,
                "expanded read combat pool increases actual Generic quote");
            Check(Quote(genericQuery, unread).Quote.NanosecondsPerInput == baseline.Quote.NanosecondsPerInput,
                "unread merchant pool has no Generic scan fee");
            // A legal three-card fallback pool exposes a negative extra-group
            // slope if the old 80-card anchor is incorrectly subtracted again.
            var tinyCards = new[] {
                cards.First(card => card.CardKey == new ModelKey("CARD", "ANGER")),
                cards.First(card => card.CardKey == new ModelKey("CARD", "ARMAMENTS")),
                cards.First(card => card.CardType == EffectCardType.Power && !card.IsMultiplayerOnly &&
                    card.IsUnlockedInCapturedPool && card.EligibleForPostCombatRewardByPoolMembership) };
            var tiny = context with { Authority = context.Authority.WithEffectAuthority(effects with {
                CharacterRewardPool = tinyCards, SnapshotFingerprint = effects.SnapshotFingerprint + ":C-tiny-pool" }) };
            var small = BaseGameModelKeys.Relics.SmallCapsule;
            var tinyQuery = query with { OpeningRoute = new(small) };
            var wheelQuery = tinyQuery with { StructuredOpeningEffects = [new(small,
                NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, [new("RELIC", "PRAYER_WHEEL")])] };
            var tinyBase = Quote(tinyQuery, tiny);
            var tinyExtra = Quote(wheelQuery, tiny);
            Check(tinyBase.Family.GpuPlan is { UsesHotLoop: false } && tinyExtra.Family.GpuPlan is { UsesHotLoop: false } &&
                !tinyBase.Family.Replay.ConservativelyKeeps && !tinyExtra.Family.Replay.ConservativelyKeeps &&
                tinyExtra.Family.Replay.Catalog.CombatRewardCardPool.TotalCount == 3 &&
                tinyExtra.Family.Replay.Plan.ExplicitContext.AdditionalCardRewardCount == 1,
                "tiny supported actual Generic family executes one additional card group");
            Check(tinyBase.Quote.NanosecondsPerInput > 0 &&
                tinyExtra.Quote.NanosecondsPerInput > tinyBase.Quote.NanosecondsPerInput,
                "extra generation increases work even below the former 30-entry negative-slope boundary");
            var extraWork = CombatRewardWorkEstimate.DescribePoolWork(tinyExtra.Family.Replay, tinyExtra.Family.GpuPlan!);
            Check(extraWork.AnchorScanEntries == CombatRewardWorkEstimate.DescribePoolWork(
                tinyBase.Family.Replay, tinyBase.Family.GpuPlan!).AnchorScanEntries,
                "extra group cannot subtract another ordinary base-group anchor");
            var tinyExpanded = tiny with { Authority = tiny.Authority.WithEffectAuthority(tiny.Authority.EffectAuthority! with {
                CharacterRewardPool = [.. tinyCards, extra], SnapshotFingerprint = effects.SnapshotFingerprint + ":C-tiny-expanded" }) };
            Check(Quote(wheelQuery, tinyExpanded).Quote.NanosecondsPerInput > tinyExtra.Quote.NanosecondsPerInput,
                "small read pools retain size response without a clamp");

            var direct = Quote(query, context);
            var directExpanded = Quote(query, expanded);
            Check(direct.Family.GpuPlan is { UsesHotLoop: true, HotLoop.DirectCardOrdinalSelectionEligible: true } &&
                directExpanded.Family.GpuPlan is { UsesHotLoop: true, HotLoop.DirectCardOrdinalSelectionEligible: true },
                "unique read-pool expansion retains direct HotLoop selection");
            Check(direct.Quote.NanosecondsPerInput == directExpanded.Quote.NanosecondsPerInput,
                "direct-index HotLoop receives no linear pool fee");
            Check(Quote(query, context, false).Quote.NanosecondsPerInput == Quote(query, expanded, false).Quote.NanosecondsPerInput,
                "CPU reward rank indexes receive no per-root linear pool fee");
        }
        finally
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(old.GpuIdentity.Length == 0 ? "Unknown" : old.GpuIdentity,
                old.RenderingBackend.Length == 0 ? "unknown" : old.RenderingBackend);
        }
        Console.WriteLine($"FamilyWorkEstimateCombatPool checks={checks};PASS");
        return checks;
    }
}
