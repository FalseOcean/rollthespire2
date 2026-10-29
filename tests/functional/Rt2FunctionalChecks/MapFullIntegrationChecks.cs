using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Tests.FastSearch;

// H-MAP-FULL-INTEGRATION-01 migration seam checks. The old implementation is
// deliberately NOT copied into production or this harness. Supply the preserved
// pre-migration main@4f3080f3 RolltheSpire2.dll, SHA256 below, using --map-full-baseline
// when it is not at the repository-local evidence path. A missing/different DLL
// fails explicitly; current code cannot stand in as its own historical oracle.
internal static class MapFullIntegrationChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string BaselineSha256 = "F3CA0756B265D0740EF73593EDB791BF8E0AB9BC9EAD009DE09584FF45C2A3FD";
    private static readonly ulong[] Ordinals = [0, 37, 8307042, 100000501454];

    internal static int Run(string? baselineAssembly = null)
    {
        string path = Path.GetFullPath(baselineAssembly ?? Path.Combine(".build-evidence",
            "map-full-main-20261028", "baseline", "RolltheSpire2.dll"));
        if (!File.Exists(path)) throw new FileNotFoundException(
            "Map Full migration requires the preserved pre-migration main@4f3080f3 DLL; use --map-full-baseline <path>.", path);
        using (var file = File.OpenRead(path))
            Check(Convert.ToHexString(SHA256.HashData(file)) == BaselineSha256, "wrong pre-migration baseline assembly");
        using var baseline = new HistoricalReplay(path);
        int cases = 0;
        bool grewBothBuffers = false;
        foreach (var context in Contexts())
        {
            var actual = new BoundedActMap(context);
            Check(((Array)Field(actual, "_groups")).Length == 256 &&
                ((Array)Field(actual, "_members")).Length == 512 &&
                ((Array)Field(actual, "_duplicates")).Length == 256, "small default workspace");
            object previous = baseline.Create(context);
            foreach (ulong ordinal in Ordinals)
            {
                string seed = Seed(ordinal);
                ulong root = Beta111Profile.Instance.ComputeRootSeed(seed);
                actual.ReplayRoot(root);
                baseline.Replay(previous, root);
                var reference = Beta111StandardMapGenerator.GenerateStandardMap(seed, context, mode: MapReplayMode.Reference);
                string label = $"{seed}/{context}";
                Check(BoundedGraph(actual) == BoundedGraph(previous), "old full graph/order/fixed parity: " + label);
                Check(BoundedGraph(actual) == ReferenceGraph(reference), "independent full graph/order/fixed parity: " + label);
                Check(actual.EvaluateFinalScalars().Bits == baseline.Scalars(previous) &&
                    actual.EvaluateFinalScalars() == MapBasisScalars.Evaluate(reference), "all 11 Final scalars: " + label);
                Check(actual.RngCalls == baseline.Calls(previous) && actual.RngCalls == reference.MapRngCalls,
                    "RNG call parity: " + label);
                Check(actual.RngState.Equals(baseline.State(previous)), "all four old replay RNG state words: " + label);
                Check(Field(actual, "_endpointReach") is UInt128[] reach && reach.Any(v => v != 0),
                    "default replay actually builds EndpointReach: " + label);
                cases++;
            }
            // ReplayRoot accepts root hashes, not logical ordinals or visible-seed
            // integers. Include values with no claimed canonical-seed preimage.
            foreach (ulong root in new[] { 0UL, 1UL << 63, ulong.MaxValue })
            {
                actual.ReplayRoot(root); baseline.Replay(previous, root);
                Check(BoundedGraph(actual) == BoundedGraph(previous) &&
                    actual.EvaluateFinalScalars().Bits == baseline.Scalars(previous) &&
                    actual.RngCalls == baseline.Calls(previous) && actual.RngState.Equals(baseline.State(previous)),
                    $"literal rootHash boundary parity: {root}/{context}");
            }
            grewBothBuffers |= CheckReuseAndGrowth(actual, context);
        }
        Check(grewBothBuffers, "fixtures must exercise both buffer growth paths");
        CheckCertificateLifecycle();
        int familyCases = CheckFamilyTransportAndExact();
        Console.WriteLine($"MAP_FULL_INTEGRATION_PASS graphCases={cases};familyInputs={familyCases};" +
            "authority=FrozenMain4f3080f3AndReference;fullGraph=RawAndFinalCoordinatesTypesFixedParentSetChildInsertionOrder;" +
            "scalars=11;RngCalls=OldAndReference;RngFourWords=OldOnly;" +
            "includes=SixSoloLawsUnderdocksExistingMultiplayerRootHashBoundariesTinyGrowthCancellationReuseCertificatePruneRepairDenseCompactExact;" +
            "noNewGpuOrPerformanceEvidence=true");
        return 0;
    }

    private static IEnumerable<ActContext> Contexts()
    {
        // Six vanilla SP laws, plus the already supported Underdocks alternative.
        foreach (int act in new[] { 1, 2, 3 })
        foreach (int ascension in new[] { 0, 1 }) yield return StandardMapFamily.Context(act, ascension);
        foreach (bool swarming in new[] { false, true }) yield return new(ActKind.Underdocks, 0, HasSwarmingElites: swarming);
        // Existing numerical entry accepts MP contexts; this does not register MP
        // in M or extend the product's supported-condition declaration.
        foreach (ActKind act in Enum.GetValues<ActKind>())
            yield return new(act, act switch { ActKind.Hive => 1, ActKind.Glory => 2, _ => 0 },
                IsMultiplayer: true, HasSwarmingElites: true);
    }

    private static bool CheckReuseAndGrowth(BoundedActMap reused, ActContext context)
    {
        // Force both numerical buffers through their real resize paths without
        // adding production configuration just for the test.
        foreach (string name in new[] { "_groups", "_members", "_duplicates" })
        {
            var array = (Array)Field(reused, name);
            Set(reused, name, Array.CreateInstance(array.GetType().GetElementType()!, 1));
        }
        bool groupGrowth = false, memberGrowth = false;
        foreach (ulong ordinal in Ordinals.AsEnumerable().Reverse().Concat(Ordinals.Take(1)))
        {
            reused.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(ordinal)));
            var fresh = new BoundedActMap(context);
            fresh.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(ordinal)));
            Check(BoundedGraph(reused) == BoundedGraph(fresh) &&
                reused.EvaluateFinalScalars() == fresh.EvaluateFinalScalars() &&
                reused.RngCalls == fresh.RngCalls && reused.RngState == fresh.RngState,
                $"tiny growth/cross-seed workspace reuse: {ordinal}/{context}");
            groupGrowth |= ((Array)Field(reused, "_groups")).Length > 1;
            memberGrowth |= ((Array)Field(reused, "_members")).Length > 1;
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { reused.ReplayRoot(ulong.MaxValue, cancelled.Token); throw new Exception("cancelled replay returned"); }
        catch (OperationCanceledException) { }
        try { _ = reused.EvaluateFinalScalars(); throw new Exception("cancelled workspace exposed stale Final scalars"); }
        catch (InvalidOperationException) { }
        ulong root = Beta111Profile.Instance.ComputeRootSeed(Seed(37));
        reused.ReplayRoot(root);
        var after = new BoundedActMap(context); after.ReplayRoot(root);
        Check(BoundedGraph(reused) == BoundedGraph(after) && reused.RngState == after.RngState &&
            reused.EvaluateFinalScalars() == after.EvaluateFinalScalars(), "cancel then reuse: " + context);
        return groupGrowth && memberGrowth;
    }

    private static void CheckCertificateLifecycle()
    {
        var map = new BoundedActMap(StandardMapFamily.Context(1, 1));
        map.ReplayRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(37)));
        const int start = 38, a = 44, b = 46, end = 52;
        void Diamond()
        {
            Array.Clear((byte[])Field(map, "_type")); Array.Clear((byte[])Field(map, "_degree"));
            Array.Clear((UInt128[])Field(map, "_parents")); Array.Fill((int[])Field(map, "_grid"), -1);
            var cols = (byte[])Field(map, "_col"); for (int n = 0; n < cols.Length; n++) cols[n] = (byte)(n % 7);
            Set(map, "_active", (UInt128)0); Set(map, "_modifiable", (UInt128)0);
            foreach (int n in new[] { start, a, b, end })
            {
                Invoke(map, "Create", n % 7, n / 7);
                ((byte[])Field(map, "_type"))[n] = (byte)MapPointType.Monster;
            }
            Set(map, "_modifiable", Bit(a) | Bit(b));
            Invoke(map, "AddEdge", start, a); Invoke(map, "AddEdge", start, b);
            Invoke(map, "AddEdge", a, end); Invoke(map, "AddEdge", b, end);
        }
        void Build()
        {
            var state = map.RngState; int calls = map.RngCalls;
            Invoke(map, "BuildEndpointCertificate");
            Check(map.RngState == state && map.RngCalls == calls, "certificate build consumes no RNG");
            CheckCertificateTables(map, conservative: false);
        }
        Diamond(); Build();
        Check((((UInt128[])Field(map, "_endpointStarts"))[start] & Bit(end)) != 0, "diamond endpoint witness");
        Check((bool)Invoke(map, "DiscoverAndPrune", false)!, "fixture must execute an actual prune mutation");
        // Within a prune pass the cached sets deliberately remain supersets.
        // Requiring eager invalidation after every deletion would change the design.
        CheckCertificateTables(map, conservative: true); Build();
        Diamond(); Build();
        Array.Clear((int[])Field(map, "_targets")); ((int[])Field(map, "_targets"))[(int)MapPointType.RestSite] = 1;
        Check((bool)Invoke(map, "Repair", (int)MapPointType.RestSite)!, "fixture must execute actual type-changing repair");
        Check((((UInt128[])Field(map, "_endpointStarts"))[start] & Bit(end)) != 0, "pre-rebuild repair certificate is stale");
        Build();
        Check(((UInt128[])Field(map, "_endpointStarts"))[start] == 0, "repair boundary rebuild removes obsolete endpoint");
        Console.WriteLine("MAP_FULL_CERTIFICATE_PASS actualPruneAndRepair=true;deleteCache=ConservativeSuperset;repairBoundary=ExactRebuild;adjacency=DenseOrderedChildren");
    }

    private static void CheckCertificateTables(BoundedActMap map, bool conservative)
    {
        UInt128 active = (UInt128)Field(map, "_active");
        var types = (byte[])Field(map, "_type"); var degree = (byte[])Field(map, "_degree");
        var children = (byte[])Field(map, "_children");
        var pairs = (UInt128[])Field(map, "_endpointPairs");
        var starts = (UInt128[])Field(map, "_endpointStarts"); var reach = (UInt128[])Field(map, "_endpointReach");
        IEnumerable<int> Children(int node) => Enumerable.Range(0, degree[node]).Select(i => (int)children[node * 7 + i]);
        bool Active(int node) => (active & Bit(node)) != 0;
        // Small synthetic graph: enumerate matching pair continuations directly,
        // independently of the bottom-up production table construction.
        UInt128 Ends(int a, int b)
        {
            if (a == b) return Bit(a);
            if (!Active(a) || !Active(b) || types[a] != types[b]) return 0;
            UInt128 result = 0;
            foreach (int x in Children(a)) foreach (int y in Children(b)) result |= Ends(x, y);
            return result;
        }
        UInt128 Reach(int node)
        {
            UInt128 result = Bit(node); foreach (int child in Children(node)) result |= Reach(child); return result;
        }
        void EqualOrSuperset(UInt128 actual, UInt128 expected, string label) =>
            Check(conservative ? (actual & expected) == expected : actual == expected, "endpoint " + label);
        for (int node = 0; node < degree.Length; node++)
        {
            if (!Active(node)) continue;
            EqualOrSuperset(reach[node], Reach(node), "reachability");
            UInt128 expected = 0; int[] next = Children(node).ToArray();
            for (int i = 0; i < next.Length; i++) for (int j = i + 1; j < next.Length; j++) expected |= Ends(next[i], next[j]);
            EqualOrSuperset(starts[node], expected, "start endpoints");
            for (int other = node / 7 * 7; other < node / 7 * 7 + 7; other++)
                if (other != node && Active(other)) EqualOrSuperset(pairs[node * 7 + other % 7], Ends(node, other), "pair endpoints");
        }
    }

    private static int CheckFamilyTransportAndExact()
    {
        int inputs = 0;
        foreach (int ascension in new[] { 0, 1 })
        foreach (int act in new[] { 1, 2, 3 })
        {
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, ascension, 0, 1);
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, BaseGameModelKeys.Characters.Ironclad,
                ascension, authority, new("0.111.0", "0.111.0", "beta", "MapFullIntegration", true, ""), AncientOptionConditionProfile.BroadDefault);
            var batch = new SearchBatch((1UL << 32) - 5, 17);
            var signatures = Enumerable.Range(0, batch.BatchCandidateCount).Select(i => MapBasisScalars.Evaluate(
                Beta111StandardMapGenerator.GenerateStandardMap(Seed(batch.GlobalCandidate((ulong)i)),
                    StandardMapFamily.Context(act, ascension), mode: MapReplayMode.Reference))).ToArray();
            StandardMapMetric metric = Enum.GetValues<StandardMapMetric>().First(m =>
                signatures.Select(s => StandardMapFamily.Value(s, m)).Distinct().Count() > 1);
            int threshold = signatures.Min(s => StandardMapFamily.Value(s, metric)) + 1;
            var compiled = SearchCompiler.Compile(SearchQuery.Empty with
                { StandardMaps = [new(act, metric, StandardMapComparison.AtLeast, threshold)] }, context);
            var admission = ExactSearchExecutionRequestFactory.Compile(compiled, new(Seed(batch.BatchBase), batch.BatchCandidateCount, 1, 1));
            var request = admission.Plan ?? throw new Exception("map-only fixture admission: " + admission.Issue);
            var family = new StandardMapFamily(request);
            var execution = new FamilyExecutionContext(requiresGpu: false);
            try
            {
                var truth = signatures.Select(s => StandardMapFamily.Value(s, metric) >= threshold).ToArray();
                for (int i = 0; i < truth.Length; i++)
                {
                    string seed = Seed(batch.GlobalCandidate((ulong)i));
                    Check((StandardMapExactEvaluator.Evaluate(request, seed).Disposition == SearchDisposition.Match) == truth[i], "independent StandardMapExactEvaluator parity");
                    Check(ProductionExactSearchEvaluator.Evaluate(request,
                        TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch == truth[i], "whole Production Exact parity");
                }
                foreach (bool compact in new[] { false, true })
                {
                    var input = compact ? FamilyCandidateSet.FromAbi1(batch, [0, 3, 7, 16]) : FamilyCandidateSet.Dense(batch);
                    ulong[] expected = input.EnumerateLogicalOrdinals().Where(i => truth[(int)i]).ToArray();
                    var actual = family.InvokeAsync(execution, new(batch, request), input, default).GetAwaiter().GetResult();
                    Check(actual.Batch == batch && actual.ExportAbi1().Span.SequenceEqual(expected), "M dense/compact ordered unrenumbered ABI1 -> Exact");
                    inputs += input.Count;
                }
                // Inspect the actual leased product workspaces rather than only
                // proving that a separately instantiated numerical replay works.
                var work = (System.Collections.IEnumerable)Field(family, "_work");
                Check(work.Cast<BoundedActMap[]>().Any(maps =>
                    typeof(BoundedActMap).GetField("_endpointReach", Members)!.GetValue(maps[act - 1]) is UInt128[]),
                    "actual M Family invocation uses new default EndpointReach replay");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { family.MatchesRoot(0, cancelled.Token); throw new Exception("M cancellation ignored"); }
                catch (OperationCanceledException) { }
                Check(family.MatchesRoot(Beta111Profile.Instance.ComputeRootSeed(Seed(batch.BatchBase))) == truth[0], "M workspace reuse after cancellation");
            }
            finally
            {
                family.DisposeAsync(execution).GetAwaiter().GetResult(); execution.DisposeAsync().GetAwaiter().GetResult();
            }
        }
        return inputs;
    }

    private static string ReferenceGraph(GeneratedStandardMap map) => string.Join('\n', map.AllNodes()
        .OrderBy(n => n.RawRow).ThenBy(n => n.RawCol).Select(n =>
            Node(n.RawRow * 7 + n.RawCol, n.FinalCol, (int)n.PointType, n.CanBeModified,
                n.Parents.Select(p => p.RawRow * 7 + p.RawCol).Order(), n.Children.Select(c => c.RawRow * 7 + c.RawCol))));

    private static string BoundedGraph(object map)
    {
        UInt128 active = (UInt128)Field(map, "_active"), modifiable = (UInt128)Field(map, "_modifiable");
        var types = (byte[])Field(map, "_type"); var cols = (byte[])Field(map, "_col");
        var degree = (byte[])Field(map, "_degree"); var children = (byte[])Field(map, "_children");
        var parents = (UInt128[])Field(map, "_parents");
        return string.Join('\n', Enumerable.Range(0, types.Length).Where(n => (active & Bit(n)) != 0).Select(n =>
            Node(n, cols[n], types[n], (modifiable & Bit(n)) != 0,
                Enumerable.Range(0, types.Length).Where(p => (parents[n] & Bit(p)) != 0),
                Enumerable.Range(0, degree[n]).Select(lane => (int)children[n * 7 + lane]))));
    }

    private static string Node(int id, int col, int type, bool modifiable, IEnumerable<int> parents, IEnumerable<int> children) =>
        $"{id}:{id / 7},{col}:{type}:{modifiable}:" + string.Join(',', parents) + "->" + string.Join(',', children);
    private static UInt128 Bit(int node) => (UInt128)1 << node;
    private static string Seed(ulong ordinal) => VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
    private static object Field(object target, string name) => target.GetType().GetField(name, Members)?.GetValue(target)
        ?? throw new MissingFieldException(target.GetType().FullName, name);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Members)!.Invoke(target, args);
    private static void Check(bool pass, string reason) { if (!pass) throw new Exception("Map Full integration: " + reason); }

    private sealed class HistoricalReplay : IDisposable
    {
        private readonly AssemblyLoadContext _load = new("MapFullPreMigration", isCollectible: true);
        private readonly Type _map, _context, _act;
        internal HistoricalReplay(string path)
        {
            // All dependencies are shared with the functional runner. Only the
            // old product assembly has a separate identity; exchange primitives.
            _load.Resolving += (_, name) => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
            var assembly = _load.LoadFromAssemblyPath(path);
            _map = assembly.GetType(typeof(BoundedActMap).FullName!, throwOnError: true)!;
            _context = assembly.GetType(typeof(ActContext).FullName!, throwOnError: true)!;
            _act = assembly.GetType(typeof(ActKind).FullName!, throwOnError: true)!;
        }
        internal object Create(ActContext c)
        {
            object context = Activator.CreateInstance(_context, Members, null,
                [Enum.ToObject(_act, (int)c.Act), c.ActIndex, c.IsMultiplayer, c.HasSwarmingElites, c.HasSecondBoss, c.ReplaceTreasureWithElites], null)!;
            return Activator.CreateInstance(_map, Members, null, [context], null)!;
        }
        internal void Replay(object map, ulong root) => Invoke(map, "ReplayRoot", root, CancellationToken.None, false);
        internal ulong Scalars(object map)
        {
            object value = Invoke(map, "EvaluateFinalScalars")!;
            return (ulong)value.GetType().GetProperty("Bits", Members)!.GetValue(value)!;
        }
        internal int Calls(object map) => (int)_map.GetProperty("RngCalls", Members)!.GetValue(map)!;
        internal object State(object map) => _map.GetProperty("RngState", Members)!.GetValue(map)!;
        public void Dispose() => _load.Unload();
    }
}
