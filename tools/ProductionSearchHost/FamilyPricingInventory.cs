using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Read-only compiled-plan inventory through the actual Production compiler and
// registration. Reflection is diagnostics-only; no numerical or selection rule.
internal static class FamilyPricingInventory
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(ExactSearchExecutionRequest request, string output)
    {
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request);
        object? Read(object? source, string path)
        {
            foreach (string part in path.Split('.'))
            {
                if (source is null) return null;
                source = source.GetType().GetProperty(part, Flags)?.GetValue(source)
                    ?? source.GetType().GetField(part, Flags)?.GetValue(source);
            }
            return source is Array array ? array.Length : source;
        }
        var rows = families.Select(f => new
        {
            f.FamilyId, f.Coverage, f.Survival,
            ExpectedWork = f.ExpectedFilteringCost,
            WorkSegments = Read(f.ExpectedFilteringCost, "Segments"),
            Gpu = f.ConditionPerformance.UsesGpu,
            Signature = new[] {
                "PricingSignature", "Plan.PricingSignature", "Replay.PricingSignature",
                "GpuPlan.UsesHotLoop", "GpuPlan.Routes",
                "GpuPlan.DirectNestedMode", "GpuPlan.UsesStagedDense",
                "PricingReplayPlan.StructuredConditions", "Plan.ScratchWords", "Plan.Capacity", "Plan.Conditions",
                "Plan.SingleOptionPlan.Generator", "Plan.SingleOptionPlan.MaxScratchCount", "Plan.OptionPlans", "Replay.RouteCount", "Replay.Plan.MaximumBattleOrdinal",
                "Replay.Plan.PredicateCount", "Replay.Plan.OpeningConsumption.HasReplay",
                "Replay.Plan.OpeningConsumption.OrderedRelicIds", "Replay.Plan.MaxRequiredAct",
                "Replay.Plan.RequiredFarthestStage", "Replay.Plan.RequiresSecondBoss",
                "_gpuPlan.ScratchWords", "_gpuPlan.Capacity", "_gpuPlan.GroupingK",
                "_plan.LastRequiredBucket", "_plan.TrackedInitialPositions", "_plan.Predicates", "_plan.ShopPredicates"
            }.Select(path => new { Path = path, Value = Read(f, path) }).Where(p => p.Value is not null).ToArray(),
            Quotes = (from compact in new[] { false, true }
                from privateOutput in new[] { false, true }
                from population in new[] { 64d, 262144d, 16777216d }
                let geometry = new FamilyPhysicalQuoteRequest(compact, privateOutput, population)
                select new { Geometry = geometry, Quote = f.QuotePhysicalWork(geometry) }).ToArray()
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "family-pricing-inventory.json"), JsonSerializer.Serialize(
            new { Source = "ProductionCompiledFamilyPlans", SameRunObservedPricing = false, Rows = rows }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"familyPricingInventory=true;families={rows.Length};quotes={rows.Sum(r => r.Quotes.Count(q => q.Quote is not null))};execution=false");
    }
}
