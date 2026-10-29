using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class VariantOpeningExecutionChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidDataException("VariantOpeningExecution:" + name); }
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var world = captured.Authority.WorldAuthority!;
        var original = world.Beta109Generation!;
        var act3 = original.OrderedActCatalog.First(act => act.Act == 3);
        var alternate = act3 with { ActKey = new("ACT", "V_EXECUTION_ALTERNATE") };
        var generation = original with {
            SelectedActs = [], SelectedActsExact = false,
            Act1OverrideResolvedKey = null, Act1OverrideRaw = "",
            OrderedActCatalog = [..original.OrderedActCatalog, alternate],
            ActSelectionGroups = original.ActSelectionGroups.Select(group => group.Act == 3
                ? group with { EligibleActsInSourceOrder = [act3.ActKey, alternate.ActKey] } : group).ToArray() };
        var context = captured with { Authority = captured.Authority.WithWorldAuthority(world with { Beta109Generation = generation }) };
        ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? selected = null) =>
            ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, selected ?? context),
                new("000000000000", 32771, int.MaxValue, 2)).Plan ?? throw new InvalidDataException("V.Execution.Compile");
        var first = generation.ActSelectionGroups.First().EligibleActsInSourceOrder[0];
        var fixtures = new List<object>();
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "v-gpu");
        Directory.CreateDirectory(directory);
        var cases = new (string Name, SearchQuery Query, SearchContext Context)[] {
            ("Act1", SearchQuery.Empty with { VariantBossBranches = [new(1, first, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] }, context),
            ("LateSelectionAfterSingletonDraw", SearchQuery.Empty with { VariantBossBranches = [new(3, act3.ActKey, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] }, context),
            ("CombinedUnknownFirst", SearchQuery.Empty with { VariantBossBranches = [new(3, act3.ActKey, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)], FirstActUnknownEventPrefixCount = 1 }, context),
            ("CombinedVariantFirst", SearchQuery.Empty with { VariantBossBranches = [new(1, first, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)], FirstActUnknownEventPrefixCount = 3 }, context),
            ("OverrideKeepsSelectionDraw", SearchQuery.Empty with { VariantBossBranches = [new(3, act3.ActKey, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] },
                context with { Authority = context.Authority.WithWorldAuthority(world with { Beta109Generation = generation with { Act1OverrideResolvedKey = first, Act1OverrideRaw = first.Entry } }) }),
            ("DeterministicPredecessor", SearchQuery.Empty with { VariantBossBranches = [new(3, act3.ActKey, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] },
                context with { Authority = context.Authority.WithWorldAuthority(world with { Beta109Generation = generation with {
                    ActSelectionGroups = generation.ActSelectionGroups.Select(group => group.Act == 1
                        ? group with { SelectionMode = Beta109ActSelectionMode.DeterministicFirst } : group).ToArray() } }) }),
            ("UnknownOnly", SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3 },
                context with { Authority = context.Authority.WithWorldAuthority(null) }) };
        foreach (var fixture in cases)
        {
            var request = Request(fixture.Query, fixture.Context);
            Check(VariantOpeningFamily.TryCreate(request, out var cpu, false) && cpu is VariantOpeningFamily, fixture.Name + ":CPUAdmission");
            var family = (VariantOpeningFamily)cpu!;
            bool Oracle(ulong ordinal)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(ordinal);
                bool variants = true;
                if (fixture.Query.VariantBossBranches.Count > 0)
                {
                    var projected = Beta109WorldSnapshotProjector.ProjectForSeed(fixture.Context.Authority.WorldAuthority!.Beta109Generation!, seed);
                    variants = fixture.Query.VariantBossBranches.GroupBy(branch => branch.Act).All(rows =>
                        rows.Any(branch => projected.SelectedActs[branch.Act - 1] == branch.VariantKey));
                }
                return variants && (fixture.Query.FirstActUnknownEventPrefixCount == 0 ||
                    OrdinaryUnknownRoomPredictor.ForAct1Filter(Beta111Profile.Instance.ComputeRootSeed(seed),
                        fixture.Query.FirstActUnknownEventPrefixCount, false).Steps.All(step => step.Room == UnknownRoomCategory.Event));
            }
            foreach (ulong start in new[] { 0UL, 1564445371644820290UL })
            {
                const int size = 1025;
                var expected = new List<uint>();
                for (uint ordinal = 0; ordinal < size; ordinal++)
                {
                    bool oracle = Oracle(start + ordinal);
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + ordinal));
                    Check(family.Plan.Matches(root) == oracle, fixture.Name + ":CoreOracle:" + ordinal);
                    if (oracle) expected.Add(ordinal);
                }
                foreach (var numerical in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
                {
                    var plan = new VariantOpeningGpuPlan(family.Plan, numerical);
                    string shader = fixture.Name + "-" + numerical + ".glsl";
                    File.WriteAllText(Path.Combine(directory, shader), plan.ShaderSource());
                    Check(numerical != GpuNumericalVariant.IntegerRne || !plan.ShaderSource().Contains("double", StringComparison.Ordinal), fixture.Name + ":RneNoFloat64");
                    foreach (bool compact in new[] { false, true })
                    {
                        uint[] ordinals = Enumerable.Range(0, size).Where(i => !compact || i % 3 == 0).Select(i => (uint)i).ToArray();
                        fixtures.Add(new { name = fixture.Name + numerical, shader, buffers = plan.Buffers, scratchWords = 0,
                            batchMeta = new[] { (uint)start, (uint)(start >> 32), (uint)size, (uint)ordinals.Length, compact ? 1u : 0u, (uint)size, 0u, 0u },
                            ordinals, expected = expected.Where(i => !compact || i % 3 == 0).ToArray() });
                    }
                }
                using var token = new CancellationTokenSource();
                var batch = new SearchBatch(start, size);
                var input = FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, size).Where(i => i % 3 == 0).Select(i => (ulong)i).ToArray());
                var execution = new FamilyExecutionContext(false);
                var physical = ((IFamilyInvocation)family).CpuRealizations.Last();
                var actual = physical.InvokeAsync(execution, new(batch, request), input, default).GetAwaiter().GetResult();
                Check(actual.EnumerateLogicalOrdinals().SequenceEqual(expected.Where(i => i % 3 == 0).Select(i => (ulong)i)), fixture.Name + ":OrderedCpuAbi1");
                Check(physical.ResolveSurvival(new HashSet<string>()).SurvivalProbability == family.Survival.SurvivalProbability, fixture.Name + ":CpuOwnerSurvival");
                token.Cancel();
                try { physical.InvokeAsync(execution, new(batch, request), input, token.Token).GetAwaiter().GetResult(); Check(false, "CancellationMissing"); }
                catch (OperationCanceledException) { checks++; }
                physical.DisposeAsync(execution).GetAwaiter().GetResult();
                execution.DisposeAsync().GetAwaiter().GetResult();
            }
            Check(VariantOpeningFamily.TryCreate(request, out var native, true) && native!.CanBindPrivateSerial, fixture.Name + ":NativeResidentPorts");
            Check(VariantOpeningFamily.TryCreate(request, out var integer, true, GpuNumericalVariant.IntegerRne) && !integer!.CanBindPrivateSerial, fixture.Name + ":IntegerPublicOnly");
        }
        Check(!VariantOpeningFamily.TryCreate(Request(SearchQuery.Empty), out _, false), "NoEmptyStage");
        var unadmitted = Request(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 3,
            OpeningRoute = new(new("RELIC", "JUZU_BRACELET")) });
        Check(!VariantOpeningFamily.TryCreate(unadmitted, out _, false), "OpeningInterventionNotAdmittedAsOrdinary");
        var partial = Request(cases[0].Query with { FirstActUnknownEventPrefixCount = 1 },
            context with { Authority = context.Authority.WithWorldAuthority(null) });
        Check(VariantOpeningFamily.TryCreate(partial, out var partialFamily, false) && partialFamily is VariantOpeningFamily {
            Plan.CoversVariants: false, Plan.UnknownPrefixCount: 1, Plan.CheckedPreparation.HasVariantRestrictions: false }, "UnknownContinuesWithoutVariantAuthority");
        File.WriteAllText(Path.Combine(directory, "fixtures.json"), System.Text.Json.JsonSerializer.Serialize(fixtures));
        File.WriteAllText(Path.Combine(directory, "project.godot"), "config_version=5\n[application]\nconfig/name=\"V Family GPU parity\"\n[rendering]\nrenderer/rendering_method=\"mobile\"\n");
        File.Copy("tests/functional/Rt2FunctionalChecks/WorldGpuSanity.gd", Path.Combine(directory, "check.gd"), true);
        var searchDirectory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "variant-opening-main-20261028", "search");
        Directory.CreateDirectory(searchDirectory);
        var searchQuery = SearchQuery.Empty with
        {
            EventSequenceConstraints = [new(1, null, SearchSequenceRangeMode.FirstN, 3,
                new([new("EVENT", "TRASH_HEAP")], [], []))],
            FirstActUnknownEventPrefixCount = 1
        };
        foreach (var (name, order) in new (string, string[]?)[]
            { ("auto", null), ("vw", [VariantOpeningFamily.Id, "W.World"]), ("wv", ["W.World", VariantOpeningFamily.Id]) })
            ProductionSearchReplay.Write(Path.Combine(searchDirectory, name + ".json"),
                new ProductionSearchReplayWorkload(1, searchQuery,
                    new("000000000000", 257, int.MaxValue, 1, ManualFamilyOrder: order)));
        Console.WriteLine($"V_EXECUTION_CORE_PASS checks={checks};GPU fixtures={fixtures.Count}");
        return 0;
    }
}
