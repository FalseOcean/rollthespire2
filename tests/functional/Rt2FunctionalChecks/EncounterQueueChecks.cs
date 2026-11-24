using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static class EncounterQueueChecks
{
    internal static int Run(int count)
    {
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var originalWorld = captured.Authority.WorldAuthority!;
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidDataException("EncounterQueues:" + name); checks++; }
        CheckContinuationPayload(captured, Check);
        CheckVariantSchema(captured, Check);
        string directory = Path.Combine(Environment.CurrentDirectory, ".build-evidence", "encounter-gpu");
        Directory.CreateDirectory(directory);
        var fixtures = new List<object>();
        var cases = new List<(string Name, SearchQuery Query, WorldAuthoritySnapshot World)>();
        WorldPredictionResult Observe(string seed, WorldAuthoritySnapshot world) => Beta109WorldPredictionProvider.Predict(seed,
            captured.Ascension, 0, captured.CharacterKey, world, AncientOptionConditionProfile.BroadDefault);
        string positive = Beta110SeedCodec.FormatOrdinal(32);
        foreach (int runs in new[] { 1, 0 })
        {
            var world = originalWorld with { EncounterNumberOfRuns = runs };
            if (runs == 0) world = world with { Beta109Generation = world.Beta109Generation! with
            {
                Act1OverrideRaw = "Overgrowth",
                Act1OverrideResolvedKey = world.Beta109Generation.OrderedActCatalog.Single(a => a.Act == 1 && a.ActKey.Entry == "OVERGROWTH").ActKey
            } };
            var source = Observe(positive, world);
            foreach (var sequence in source.EncounterSequences)
            {
                var act = world.Beta109Generation!.OrderedActCatalog.Single(a => a.Act == sequence.Act && a.ActKey == sequence.ActKey);
                Check(sequence.Precision == PredictionPrecision.Exact, "source queue authority");
                foreach (var kind in Enum.GetValues<EncounterQueueKind>())
                {
                    var entries = (kind == EncounterQueueKind.Elite ? sequence.Elite : kind == EncounterQueueKind.Weak
                        ? sequence.Normal.Take(act.WeakEncounterSlots) : sequence.Normal.Skip(act.WeakEncounterSlots)).ToArray();
                    foreach (int n in new[] { 1, Math.Min(3, entries.Length), entries.Length }.Distinct())
                    {
                        var allowed = entries.Take(n).Select(e => e.EncounterKey).Distinct().ToArray();
                        cases.Add(($"Runs{runs}.A{sequence.Act}.{kind}.Prefix{n}", SearchQuery.Empty with
                        { EncounterQueues = [new(sequence.Act, kind, SearchSequenceRangeMode.FirstN, n, allowed)] }, world));
                    }
                    cases.Add(($"Runs{runs}.A{sequence.Act}.{kind}.Last", SearchQuery.Empty with
                    { EncounterQueues = [new(sequence.Act, kind, SearchSequenceRangeMode.ExactSlot, entries.Length, [entries[^1].EncounterKey])] }, world));
                }
            }
            var first = source.EncounterSequences[0];
            cases.Add(($"Runs{runs}.ConjoinedWeakStrongEliteBoss", SearchQuery.Empty with
            {
                EncounterQueues = [new(1, EncounterQueueKind.Weak, SearchSequenceRangeMode.ExactSlot, 1, [first.Normal[0].EncounterKey]),
                    new(1, EncounterQueueKind.Strong, SearchSequenceRangeMode.ExactSlot, 1, [first.Normal[3].EncounterKey]),
                    new(1, EncounterQueueKind.Elite, SearchSequenceRangeMode.FirstN, 2, first.Elite.Take(2).Select(e => e.EncounterKey).ToArray())],
                LegacyWorld = SearchQuery.Empty.LegacyWorld with { BossOrdinalFilters = source.Bosses.Where(b => b.Act == 3)
                    .Select(b => new ActOrdinalModelKeySetFilter(3, b.Ordinal, new([b.BossKey], [], []))).ToArray() }
            }, world));
        }
        var unknownWorld = originalWorld with { EncounterNumberOfRuns = null };
        cases.Add(("DiscoveryMissing", SearchQuery.Empty with { EncounterQueues = [new(1, EncounterQueueKind.Weak,
            SearchSequenceRangeMode.ExactSlot, 1, [new("ENCOUNTER", "NEVER")])] }, unknownWorld));
        var pureMissingWorld = unknownWorld with { Beta109Generation = unknownWorld.Beta109Generation! with
        {
            Act1OverrideRaw = "Overgrowth",
            Act1OverrideResolvedKey = unknownWorld.Beta109Generation.OrderedActCatalog.Single(a => a.Act == 1 && a.ActKey.Entry == "OVERGROWTH").ActKey
        } };
        var missingQuery = cases.Single(c => c.Name == "DiscoveryMissing").Query;
        var missingContext = captured with { Authority = captured.Authority.WithWorldAuthority(pureMissingWorld) };
        var missingCompiled = SearchCompiler.Compile(missingQuery, missingContext);
        var missingModel = new SearchModel(missingCompiled, CompiledSearchEvaluationProjector.Project(missingCompiled).Evaluation,
            CombatRewardRoutePolicyContract.None, missingCompiled.SemanticFingerprint);
        Check(!WorldFamilyReplay.Compile(missingModel).Enabled, "pure unknown discovery has no pass-through filter");
        Check(WorldFamilyReplay.EncounterRemainders(missingModel).Single().Contains("EncounterDiscoveryContextMissing"), "pure unknown discovery cause retained");
        var missingRequest = new PreparedSearch(missingModel, new("000000000000", 129, int.MaxValue, 1, SkipExactValidation: true),
            "000000000000", 129, missingCompiled.SemanticFingerprint);
        var missingPlan = FamilyExecutionCoordinator.Plan(missingRequest, gpuAvailable: false);
        Check(missingPlan.AllConditionsUnfiltered && missingPlan.FilterRemainders.Any(r => r.Contains("EncounterDiscoveryContextMissing")),
            "zero participating conditions disclose raw seeds and actual cause");
        foreach (var (name, query, world) in cases)
        {
            var context = captured with { Authority = captured.Authority.WithWorldAuthority(world) };
            var compiled = SearchCompiler.Compile(query, context);
            var model = new SearchModel(compiled, CompiledSearchEvaluationProjector.Project(compiled).Evaluation,
                CombatRewardRoutePolicyContract.None, compiled.SemanticFingerprint);
            var replay = new WorldFamilyReplay(model);
            var gpu = new WorldFamilyGpuPlan(replay, specializeSolo: true);
            Check(gpu.Revision(false).Contains("EncounterQueues"), "dedicated GPU revision");
            string shader = name + ".glsl";
            File.WriteAllText(Path.Combine(directory, shader), gpu.ShaderSource());
            var expected = new List<uint>();
            for (uint i = 0; i < count; i++)
            {
                string seed = Beta110SeedCodec.FormatOrdinal(i);
                var observed = Observe(seed, world);
                var match = EncounterQueueMatcher.Evaluate(query.EncounterQueues, observed, world);
                bool truth = match.Disposition != SearchDisposition.NoMatch && query.LegacyWorld.BossOrdinalFilters.All(p => observed.Bosses.Any(b =>
                    b.Act == p.Act && b.Ordinal == p.Ordinal && p.Keys.Any.Contains(b.BossKey)));
                bool actual = replay.Matches(Beta111Profile.Instance.ComputeRootSeed(seed));
                Check(actual == truth, name + ":" + seed);
                if (actual) expected.Add(i);
                if (name == "DiscoveryMissing" && observed.EncounterSequences.Single(a => a.Act == 1).Precision != PredictionPrecision.Exact) Check(match.Disposition != SearchDisposition.Match && match.Disposition != SearchDisposition.NoMatch, "missing fact remains unverified");
            }
            var numericalPlans = new List<(string Tag, WorldFamilyGpuPlan Plan)> { ("native", gpu) };
            if (name.Contains(".A1.") && name.EndsWith("Prefix1") || name.EndsWith(".Strong.Last") || name == "DiscoveryMissing")
                numericalPlans.Add(("integer", new WorldFamilyGpuPlan(replay, specializeSolo: true, numericalVariant: GpuNumericalVariant.IntegerRne)));
            foreach (var numerical in numericalPlans)
            {
            string numericalShader = numerical.Tag == "native" ? shader : name + ".integer.glsl";
            File.WriteAllText(Path.Combine(directory, numericalShader), numerical.Plan.ShaderSource());
            foreach (bool compact in new[] { false, true })
            {
                var ordinals = Enumerable.Range(0, count).Where(i => !compact || i % 3 == 0).Select(i => (uint)i).ToArray();
                fixtures.Add(new { name = name + "." + numerical.Tag, shader = numericalShader, buffers = numerical.Plan.Buffers, scratchWords = numerical.Plan.ScratchWords,
                    batchMeta = new uint[] { 0, 0, (uint)count, (uint)ordinals.Length, compact ? 1u : 0u, (uint)count, 0, 0 },
                    ordinals, expected = expected.Where(i => !compact || i % 3 == 0).ToArray() });
            }
            }
        }
        File.WriteAllText(Path.Combine(directory, "fixtures.json"), System.Text.Json.JsonSerializer.Serialize(fixtures));
        File.WriteAllText(Path.Combine(directory, "project.godot"), "config_version=5\n[application]\nconfig/name=\"Encounter GPU parity\"\n[rendering]\nrenderer/rendering_method=\"mobile\"\n");
        File.Copy("tests/functional/Rt2FunctionalChecks/WorldGpuSanity.gd", Path.Combine(directory, "check.gd"), true);
        Console.WriteLine($"ENCOUNTER_QUEUE_PASS checks={checks};cases={cases.Count};gpuFixtures={fixtures.Count}");
        return 0;
    }
    internal static int RunSchema()
    {
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidDataException("EncounterSchema:" + name); checks++; }
        CheckContinuationPayload(context, Check);
        CheckVariantSchema(context, Check);
        Console.WriteLine($"ENCOUNTER_SCHEMA_PASS checks={checks};legacyPayload=true;malformedVariantCategory=true");
        return 0;
    }

    private static void CheckVariantSchema(SearchContext context, Action<bool, string> check)
    {
        var condition = new EncounterQueueSearchCondition(1, EncounterQueueKind.Weak, SearchSequenceRangeMode.FirstN, 1,
            [new("ENCOUNTER", "NIBBITS_WEAK")]) { VariantKey = new("ACT", "OVERGROWTH") };
        check(condition.IsValid && (condition with { VariantKey = null }).IsValid, "valid Act or unscoped variant accepted");
        foreach (string category in new[] { "EVENT", "RELIC", "ENCOUNTER" })
        {
            var malformed = condition with { VariantKey = new(category, "OVERGROWTH") };
            check(!malformed.IsValid, "foreign category cannot be authored as Act variant:" + category);
            bool rejected = false;
            try { SearchCompiler.Compile(SearchQuery.Empty with { EncounterQueues = [malformed] }, context); }
            catch (ArgumentException error) { rejected = error.Message == "EncounterQueue:InvalidShape"; }
            check(rejected, "malformed variant fails admission rather than becoming a seed mismatch:" + category);
        }
    }

    private static void CheckContinuationPayload(SearchContext context, Action<bool, string> check)
    {
        var method = typeof(SearchContinuationIdentity).GetMethod("QueryPayload", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException("SearchContinuationIdentity.QueryPayload");
        JsonObject Payload(SearchQuery query) => ((JsonNode)method.Invoke(null, [query])!).AsObject();
        var legacyEmpty = Payload(SearchQuery.Empty);
        check(!legacyEmpty.ContainsKey("EncounterQueues") && !legacyEmpty.ContainsKey("NeowOfferWhitelist") &&
            !legacyEmpty.ContainsKey("FirstActUnknownMonsterPrefixCount"), "new neutral fields preserve historical v1 resume payload");
        check(legacyEmpty.ContainsKey("StandardMaps") && legacyEmpty.ContainsKey("StandardMapRoutes") &&
            legacyEmpty.ContainsKey("FirstActUnknownEventPrefixCount"), "existing neutral fields retain historical payload shape");
        var oldJson = legacyEmpty.ToJsonString();
        var oldRestored = JsonSerializer.Deserialize<SearchQuery>(oldJson)!;
        check(JsonNode.DeepEquals(Payload(oldRestored), legacyEmpty), "old JSON restores and keeps byte-equivalent query payload");
        var condition = new EncounterQueueSearchCondition(1, EncounterQueueKind.Weak, SearchSequenceRangeMode.FirstN, 1,
            [new("ENCOUNTER", "NIBBITS_WEAK")]);
        var active = SearchQuery.Empty with { EncounterQueues = [condition] };
        check(Payload(active)["EncounterQueues"]!.AsArray().Count == 1, "authored encounter condition remains in resume payload");
        var nested = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = active },
            new(1, ModelKeySetFilter.Empty)] };
        var nestedPayload = Payload(nested);
        check(nestedPayload["Players"]![0]!["Conditions"]!["EncounterQueues"]!.AsArray().Count == 1 &&
            !nestedPayload["Players"]![1]!["Conditions"]!.AsObject().ContainsKey("EncounterQueues"),
            "per-seat neutral omission does not erase authored conditions");
        string Identity(SearchQuery query) => SearchContinuationIdentity.For(SearchCompiler.Compile(query, context));
        check(Identity(oldRestored) == Identity(SearchQuery.Empty), "legacy resume identity survives default restoration");
        check(Identity(active) != Identity(SearchQuery.Empty) && Identity(active) != Identity(active with
            { EncounterQueues = [condition with { RangeValue = 2 }] }), "active queue and changed window remain distinct resume identities");
        check(Payload(SearchQuery.Empty with { FirstActUnknownMonsterPrefixCount = 1 }).ContainsKey("FirstActUnknownMonsterPrefixCount") &&
            Payload(SearchQuery.Empty with { NeowOfferWhitelist = new([new("RELIC", "GOLDEN_PEARL")]) }).ContainsKey("NeowOfferWhitelist"),
            "active new query domains remain in resume payload");
        var blacklist = SearchQuery.Empty with { CombatCardExclusion = new(1, [new("CARD", "STRIKE_IRONCLAD")]) };
        check(!Payload(blacklist)["CombatCardExclusion"]!.AsObject().ContainsKey("Mode") &&
            Payload(blacklist with { CombatCardExclusion = blacklist.CombatCardExclusion! with { Mode = CombatCardCollectionMode.Whitelist } })
                ["CombatCardExclusion"]!.AsObject().ContainsKey("Mode"), "legacy blacklist mode stays absent and authored whitelist stays explicit");
    }

}
