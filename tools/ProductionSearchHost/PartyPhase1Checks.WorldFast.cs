using Godot;
using MegaCrit.Sts2.Core.Unlocks;
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

internal static partial class PartyPhase1Checks
{
    private static async Task CheckWorldFastContexts(GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        const int roots = 65;
        const ulong positive = 32;
        string seed = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, positive);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Party W: " + message);
            checks++;
        }
        await using var execution = new FamilyExecutionContext(requiresGpu: true, failOnPhysicalRecovery: true);
        foreach (int count in new[] { 2, 3, 4 })
        {
            var unlocks = Enumerable.Range(0, count).Select(_ => UnlockState.all.ToSerializable()).ToArray();
            foreach (var unlock in unlocks) unlock.EncountersSeen.Clear();
            var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, seed,
                BaseGameModelKeys.Characters.All.Take(count).ToArray(), unlocks, 10, "0.111.0");
            var generation = party.World.Beta109Generation!;
            Check(generation.HasExactFixedParty && generation.PartyBossDiscoveryExact &&
                generation.PartyBossDiscoveryOverrides.Count > 0, "unseen bosses produce exact discovery overrides");
            WorldPredictionResult Project(string candidate, WorldAuthoritySnapshot world) =>
                Beta109WorldPredictionProvider.Predict(candidate, 10, 0, party.Players[0].Character.CharacterKey,
                    world, AncientOptionConditionProfile.BroadDefault);
            var observed = Project(seed, party.World);
            Check(observed.Bosses.Any(b => b.Act == 3 && b.Ordinal == 2 && b.SearchIdentityReplayable),
                "A10 source second boss is replayable");
            var queue = observed.EventPoolSequencePrediction!.Acts.Single(a => a.Act == 3);
            var eventTarget = queue.Entries.Single(e => e.Ordinal == 1);
            Check(queue.Precision == PredictionPrecision.Exact && eventTarget.Precision == PredictionPrecision.Exact,
                "Act 3 source effective queue is exact");
            var bossPredicates = observed.Bosses.Select(b => new ActOrdinalModelKeySetFilter(
                b.Act, b.Ordinal, new ModelKeySetFilter([b.BossKey], [], []))).ToArray();
            var query = SearchQuery.Empty with
            {
                Players = Enumerable.Range(0, count).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)).ToArray(),
                LegacyWorld = SearchQuery.Empty.LegacyWorld with { BossOrdinalFilters = bossPredicates },
                EventSequenceConstraints = [new(3, eventTarget.Source, SearchSequenceRangeMode.ExactSlot, 1,
                    new([eventTarget.EventKey], [], []))]
            };
            var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
                10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var compile = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed, roots, 1, 1));
            Check(compile.Success, "query compilation: " + compile.Issue);
            var request = compile.Plan!;
            var cpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, false);
            var gpu = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, true);
            Check(cpu.Count == 1 && gpu.Count == 1 && cpu[0].FamilyId == "W.World" && gpu[0].FamilyId == "W.World",
                "one shared W registration");
            Check(!cpu[0].ConditionPerformance.UsesGpu && gpu[0].ConditionPerformance.UsesGpu,
                "explicit W CPU/GPU realizations");
            try
            {
                var sourceMatches = new List<ulong>();
                int replacedDraws = 0;
                var withoutOverrides = party.World with { Beta109Generation = generation with
                { PartyBossDiscoveryOverrides = new Dictionary<string, ModelKey>() } };
                for (ulong ordinal = 0; ordinal < roots; ordinal++)
                {
                    string candidate = VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, ordinal);
                    var world = Project(candidate, party.World);
                    Check(world.Bosses.All(b => b.SearchIdentityReplayable) &&
                        world.EventPoolSequencePrediction?.Precision == PredictionPrecision.Exact, "source replay authority");
                    var sourceQueue = world.EventPoolSequencePrediction!.Acts.Single(a => a.Act == 3);
                    bool match = bossPredicates.All(p => world.Bosses.Any(b => b.Act == p.Act && b.Ordinal == p.Ordinal &&
                        p.Keys.Any.Contains(b.BossKey))) && sourceQueue.Entries.Any(e => e.Ordinal == 1 &&
                        e.Source == eventTarget.Source && e.EventKey == eventTarget.EventKey);
                    if (match) sourceMatches.Add(ordinal);
                    var natural = Project(candidate, withoutOverrides);
                    replacedDraws += world.Bosses.Where(b => b.Ordinal == 1).Count(b =>
                        natural.Bosses.Single(n => n.Act == b.Act && n.Ordinal == 1).BossKey != b.BossKey);
                }
                Check(replacedDraws > 0, "discovery replacement changes naturally drawn first bosses");
                Check(sourceMatches.Contains(positive) && sourceMatches.Count < roots, "source has positive and negative roots");
                var batch = new SearchBatch(0, roots);
                foreach (var input in new[] { FamilyCandidateSet.Dense(batch), FamilyCandidateSet.FromSortedAbi1(batch,
                    Enumerable.Range(0, (roots + 1) / 2).Select(i => (ulong)i * 2).ToArray()) })
                {
                    var expected = sourceMatches.Where(i => input.IsDense || i % 2 == 0);
                    var window = new FamilyObservationWindow(batch, request);
                    var cpuResult = await cpu[0].InvokeAsync(execution, window, input, default);
                    var gpuResult = await gpu[0].InvokeAsync(execution, window, input, default);
                    Check(cpuResult.EnumerateLogicalOrdinals().SequenceEqual(expected), "CPU complete population/source parity");
                    Check(gpuResult.EnumerateLogicalOrdinals().SequenceEqual(expected), "GPU complete population/source parity");
                    Check(gpuResult.EnumerateLogicalOrdinals().Contains(positive), "known positive retained");
                }
                Check(ProductionExactSearchEvaluator.Evaluate(request,
                    TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed)).IsMatch,
                    "whole-table Exact accepts source positive");
                GD.Print($"PARTY_WORLD_FAST players={count};roots={roots};sourceMatches={sourceMatches.Count};overrides={generation.PartyBossDiscoveryOverrides.Count};replacedDraws={replacedDraws};act3Queue=true;secondBoss=true;denseCompact=true");
            }
            finally
            {
                foreach (var family in gpu) await family.DisposeAsync(execution);
                foreach (var family in cpu) await family.DisposeAsync(execution);
            }
        }
        GD.Print($"PARTY_WORLD_FAST_PASS checks={checks}");
    }
}
