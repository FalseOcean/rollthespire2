using System.Reflection;
using System.Text.Json;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

// Read-only compiled-plan inventory through the actual Production compiler and
// registration. Reflection is diagnostics-only; no numerical or selection rule.
internal static class FamilyPricingInventory
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(PreparedSearch request, string output)
    {
        var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request);
        object Canonical(FilterBodyDescription family, bool compact, int roots, int inputs)
        {
            var key = FamilyCanonicalCostShape.Describe(family, compact, roots);
            return new { RootSpan = roots, Input = inputs, Compact = compact, Key = key,
                SourceSurvival = family.FamilyId == "N.Neow" ? family.ResolveSurvival(new HashSet<string>()).SurvivalProbability : null,
                Quote = FamilyCanonicalMeasuredCost.TryQuote(family, new(compact, false, inputs), roots),
                LocalBoundary = FamilyCanonicalMeasuredCost.Boundary };
        }
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
            Physical = f.ConditionPerformance,
            CanonicalReferences = new[] { Canonical(f, false, 16777216, 16777216),
                Canonical(f, false, 65536, 65536), Canonical(f, true, 16777216, 65536) },
            ConfiguredCpuReferences = HostFilterBindings.ConfiguredCpuBodies(f).Select(cpu => new {
                Physical = cpu.ConditionPerformance,
                Dense = Canonical(cpu, false, 65536, 65536),
                Compact = Canonical(cpu, true, 16777216, 65536) }).ToArray(),
            Signature = new {
                f.PhysicalRevision, f.NumericRequirements, f.InputModes, f.WindowCapacity, f.CpuChunkCapacity,
                f.CpuWorkers, f.PreferredRootWindowSize, f.RequiredPassedCoverage, f.Cost.PartyChild,
                CanonicalDenseShape = f.Cost.CanonicalWorkShape?.Invoke(false),
                CanonicalCompactShape = f.Cost.CanonicalWorkShape?.Invoke(true),
                f.Cost.PrivateOutputRevision, f.Cost.PrivateBodyRevision, f.Cost.PublicBoundaryToken,
                DenseObservationRevision = f.Cost.ObservationRevision?.Invoke(new(false,false,65536)),
                CompactObservationRevision = f.Cost.ObservationRevision?.Invoke(new(true,false,65536))
            },
            Quotes = (from compact in new[] { false, true }
                from privateOutput in new[] { false, true }
                from population in new[] { 64d, 262144d, 16777216d }
                let geometry = new FamilyPhysicalQuoteRequest(compact, privateOutput, population)
                select new { Geometry = geometry, Quote = f.QuotePhysicalWork(geometry) }).ToArray()
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "family-pricing-inventory.json"), JsonSerializer.Serialize(
            new { Source = "ProductionCompiledFamilyPlans", Device = SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity(),
                SameRunObservedPricing = false, ExecutionPerformed = false, Rows = rows }, ProductionSearchReplay.JsonOptions));
        Console.WriteLine($"familyPricingInventory=true;families={rows.Length};quotes={rows.Sum(r => r.Quotes.Count(q => q.Quote is not null))};execution=false");
    }
}
