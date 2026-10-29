using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class FamilyWorkEstimateWorldPoolChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("FamilyWorkEstimateWorldPool: " + message);
        }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/owner-nwae-v1/context.json").Context;
        var world = context.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        var act1 = generation.OrderedActCatalog.First(a => a.Act == 1);
        ModelKeySetFilter Any(ModelKey key) => new([key], [], []);
        var boss = LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {
            BossOrdinalFilters = [new(1, 1, Any(act1.Bosses[0]))] });
        var ancient = LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {
            AncientIdentityFilters = [new(1, Any(act1.OrderedAncients[0]))] });
        var events = LegacySearchQueryAdapter.FromFilter(NeowSearchFilter.Empty with {
            EventSequenceConditions = [new(1, null, SearchSequenceRangeMode.FirstN, 3,
                Any(act1.OrderedEligibleEvents[0]))] });
        var variantContext = VariantOpeningPreparationChecks.CreateContext();
        var variant = SearchQuery.Empty with { VariantBossBranches = [
            new(1, VariantOpeningPreparationChecks.A, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false),
            new(1, VariantOpeningPreparationChecks.B, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] };

        SearchContext Change(Func<Beta109ActGenerationSnapshot, Beta109ActGenerationSnapshot> change) =>
            context with { Authority = context.Authority.WithWorldAuthority(world with {
                Beta109Generation = generation with {
                    OrderedActCatalog = generation.OrderedActCatalog.Select(change).ToArray() } }) };
        Beta109ActGenerationSnapshot Expand(Beta109ActGenerationSnapshot act)
        {
            // A repeated eligible local identity is a weighted entry. Prepend
            // it to both orders, retaining raw/local/shared alignment.
            var key = act.OrderedRawEvents.First(act.OrderedEligibleEvents.Contains);
            return act with { OrderedRawEvents = [key, ..act.OrderedRawEvents],
                OrderedEligibleEvents = [key, ..act.OrderedEligibleEvents] };
        }
        var expanded = Change(a => a.Act == 1 ? Expand(a) : a);
        var unread = Change(a => a.Act > 1 ? Expand(a) : a);
        var old = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        try
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity("UnmeasuredWorldPoolEstimateDevice", "vulkan");
            foreach (bool gpu in new[] { false, true })
            {
                (WorldFamilyReplay Replay, FamilyPhysicalQuote Quote) Quote(SearchQuery query, SearchContext source)
                {
                    var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, source),
                        new("000000000000", 65536, 16, 1)).Plan!;
                    Check(WorldFamily.TryCreate(request, out var invocation, gpu) && invocation is WorldFamily,
                        "actual compiled W registration " + gpu);
                    var family = (WorldFamily)invocation!;
                    var quote = family.QuotePhysicalWork(new(false, false, 65536))!;
                    Check(FamilyCoarsePricing.IsEstimate(quote), "actual W quote consumes coarse estimate " + gpu);
                    return (family.Replay, quote);
                }
                foreach (var query in new[] { boss, ancient, events })
                {
                    var baseline = Quote(query, context);
                    var larger = Quote(query, expanded);
                    Check(baseline.Replay.Plan.MaxRequiredAct == 1, "only Act 1 progression is consumed");
                    Check(larger.Quote.NanosecondsPerInput > baseline.Quote.NanosecondsPerInput,
                        "consumed event-pool expansion increases the actual quote " + gpu);
                    double delta = larger.Quote.NanosecondsPerInput - baseline.Quote.NanosecondsPerInput;
                    Check(Math.Abs(delta - (gpu ? .025 : 8)) < 1e-9,
                        "one extra shuffle advance is charged once, including event predicates " + gpu);
                    Check(Quote(query, unread).Quote.NanosecondsPerInput == baseline.Quote.NanosecondsPerInput,
                        "later unread event pools do not increase work " + gpu);
                    if (query == boss || query == ancient)
                        Check(baseline.Replay.Plan.Acts.All(a => !a.HasEventPredicate && a.EventCandidates.Length == 0),
                            "identity-only compiled plan has no materialized events");
                }
                var variantWorld = variantContext.Authority.WorldAuthority!;
                var variantExpanded = variantContext with { Authority = variantContext.Authority.WithWorldAuthority(variantWorld with {
                    Beta109Generation = variantWorld.Beta109Generation! with {
                        OrderedActCatalog = variantWorld.Beta109Generation.OrderedActCatalog.Select(Expand).ToArray() } }) };
                VariantOpeningFamily Variant(SearchContext source)
                {
                    var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(variant, source),
                        new("000000000000", 65536, 16, 1)).Plan!;
                    var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpu);
                    Check(families.All(f => f is not WorldFamily) && families.Single() is VariantOpeningFamily,
                        "variant-only registration belongs to V without a W progression stage " + gpu);
                    return (VariantOpeningFamily)families.Single();
                }
                var variantBase = Variant(variantContext);
                Check(variantBase.Plan.CoversVariants &&
                    Variant(variantExpanded).QuotePhysicalWork(new(false, false, 65536))!.NanosecondsPerInput ==
                    variantBase.QuotePhysicalWork(new(false, false, 65536))!.NanosecondsPerInput,
                    "V variant-only path does not acquire unread event progression cost " + gpu);
            }
        }
        finally
        {
            SearchPerformanceProfileFoundation.ObserveGpuIdentity(old.GpuIdentity.Length == 0 ? "Unknown" : old.GpuIdentity,
                old.RenderingBackend.Length == 0 ? "unknown" : old.RenderingBackend);
        }
        Console.WriteLine($"FamilyWorkEstimateWorldPool checks={checks};PASS");
        return checks;
    }
}
