using System.Text.Json;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Bounded numerical parity on the existing host, not a timing/calibration suite.
// Original NeowSingleplayerReplay retains the complete positive array + shuffle.
internal static class NeowEntryIdentityChecks
{
    internal static void Run(PreparedSearch request, string output, bool productionDefaultOnly = false)
    {
        var fixtures = NeowEntryIdentityFixtures.Build(NeowReplayPlan.Compile(request.Model, false));
        if (productionDefaultOnly)
        {
            var names = new HashSet<string> {
                "Single." + Beta110FastRelicCatalog.GoldenPearl,
                "Single." + Beta110FastRelicCatalog.NutritiousOyster,
                "MixedAnySatisfiedByCurse", "PositiveBanFallback", "UnavailableAnyStillHasCurse", "LockedKaleidoscope"
            };
            fixtures = fixtures.Where(f => names.Contains(f.Name)).ToArray();
            if (fixtures.Count != names.Count) throw new InvalidDataException("NEntry.DefaultFixturesMissing");
        }
        var profile = RuntimeProfileRegistry.Select(request.Model.Detection);
        var records = new List<object>();
        long cpuInputs = 0, comparisons = 0;
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("NEntry.NoGpu");
        foreach (var fixture in fixtures)
        {
            bool Cpu(ulong global) => NeowSingleplayerReplay.Matches(profile.ComputeRootSeed(
                VisibleSeedCandidateCodec.FormatOrdinal(profile, global)), fixture.Replay);
            ulong positive = 0;
            if (fixture.Positive)
            {
                while (positive < 1 << 20 && !Cpu(positive)) positive++;
                if (positive == 1 << 20) throw new InvalidDataException("NEntry.PositiveWitnessMissing:" + fixture.Name);
            }
            var batches = new List<SearchBatch> { new(0, 1027) };
            if (fixture.Positive && positive >= 1027) batches.Add(new(positive, 515));
            // Distinguish ordinal encoding carry and the valid seed-space tail
            // without repeating these codec checks for every fixed target.
            if (fixture.Name == "LargeCapsuleSkipsFirstFlip")
            {
                batches.Add(new(34UL * 34 * 34 * 34 - 3, 17));
                batches.Add(new(VisibleSeedCandidateCodec.SpaceSize(profile) - 515, 515));
            }
            var samples = new List<(FamilyCandidateSet Input, FamilyCandidateSet Expected)>();
            foreach (var batch in batches)
            foreach (bool compact in new[] { false, true })
            {
                var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, batch.BatchCandidateCount)
                    .Where(i => i % 3 == 0 || i == batch.BatchCandidateCount - 1)
                    .Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                var expected = FamilyCandidateSet.FromSortedAbi1(batch, input.EnumerateLogicalOrdinals()
                    .Where(i => Cpu(batch.GlobalCandidate(i))).ToArray());
                samples.Add((input, expected)); cpuInputs += input.Count;
            }
            if (fixture.Positive && !samples.Any(s => s.Expected.Count > 0) ||
                !fixture.Positive && samples.Any(s => s.Expected.Count != 0))
                throw new InvalidDataException("NEntry.PositiveNegativeFixture:" + fixture.Name);

            foreach (var numeric in new[] { NeowGpuNumericalVariant.NativeFloat64, NeowGpuNumericalVariant.IntegerUnitFloat })
            {
                // Integer next_int/position tracking is shared by the variants.
                // Full identity coverage is native; the second variant checks
                // every distinct branch and the route continuation seam.
                if (!productionDefaultOnly && numeric == NeowGpuNumericalVariant.IntegerUnitFloat && !SecondaryNumeric(fixture.Name)) continue;
                var offPlan = NeowEntryIdentityFixtures.Compile(fixture.Replay, "off") with { NumericalVariant = numeric };
                using var off = new NeowFamilyGpuExecutor(rd, offPlan, 201);
                var offResults = samples.Select(s => off.Execute(s.Input, default, out _)).ToArray();
                for (int i = 0; i < samples.Count; i++) Equal(offResults[i], samples[i].Expected, fixture.Name + ".OriginalGpuCpu:" + numeric);
                string?[] modes = productionDefaultOnly ? [null] :
                    numeric == NeowGpuNumericalVariant.NativeFloat64 && IndependentArms(fixture.Name)
                        ? ["reject", "track", "all"] : ["all"];
                foreach (string? mode in modes)
                {
                    var plan = NeowEntryIdentityFixtures.Compile(fixture.Replay, mode) with { NumericalVariant = numeric };
                    bool track = (plan.EntryIdentityMode & NeowEntryIdentityMode.TrackSingleRequired) != 0;
                    if (track != (fixture.Track && mode != "reject")) throw new InvalidDataException("NEntry.TrackApplicability:" + fixture.Name + ":" + mode);
                    if (mode == "track" && plan.Meta[69] != offPlan.Meta[69]) throw new InvalidDataException("NEntry.TrackEnabledReject");
                    if (productionDefaultOnly && ((plan.EntryIdentityMode & NeowEntryIdentityMode.RejectUnavailable) != 0 ||
                        plan.Meta[68] != offPlan.Meta[68] || plan.Meta[69] != offPlan.Meta[69]))
                        throw new InvalidDataException("NEntry.DefaultEnabledReject:" + fixture.Name);
                    using var on = new NeowFamilyGpuExecutor(rd, plan, 202);
                    if ((plan.EntryIdentityRevision.Length != 0) !=
                        (on.OptimizationRevisionFor(false) != off.OptimizationRevisionFor(false)))
                        throw new InvalidDataException("NEntry.PhysicalRevision:" + fixture.Name + ":" + mode);
                    for (int i = 0; i < samples.Count; i++)
                    {
                        var actual = on.Execute(samples[i].Input, default, out _);
                        Equal(actual, offResults[i], fixture.Name + ".SameBuildFullAbi1:" + numeric + ":" + mode);
                        Equal(actual, samples[i].Expected, fixture.Name + ".OriginalFullShuffleCpu:" + numeric + ":" + mode);
                        comparisons += samples[i].Input.Count;
                    }
                    records.Add(new { fixture.Name, numeric = numeric.ToString(), mode = mode ?? "default", track,
                        offRejectMask = offPlan.Meta[69], rejectMask = plan.Meta[69],
                        denseRevision = on.OptimizationRevisionFor(false), compactRevision = on.OptimizationRevisionFor(true),
                        positive = fixture.Positive ? (ulong?)positive : null,
                        samples = samples.Select(s => new { start = s.Input.Batch.BatchBase,
                            count = s.Input.Batch.BatchCandidateCount, inputs = s.Input.Count, survivors = s.Expected.Count }) });
                    Console.WriteLine($"N_ENTRY_GPU=PASS;fixture={fixture.Name};numeric={numeric};mode={mode ?? "default"};track={track};samples={samples.Count}");
                }
            }
        }
        File.WriteAllText(Path.Combine(output, "neow-entry-identity-checks.json"), JsonSerializer.Serialize(new {
            passed = true, productionDefaultOnly, device = rd.GetDeviceName(), fixtures = fixtures.Count, cpuInputs, comparisons, records,
            oracle = "Unchanged NeowSingleplayerReplay full offer shuffle and same-build off GPU full ABI1",
            timingIsPerformanceSample = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"N_ENTRY_GPU_SUMMARY=PASS;fixtures={fixtures.Count};arms={records.Count};comparedInputs={comparisons};cpuInputs={cpuInputs}");
    }

    private static bool IndependentArms(string name) => name == "Single." + Beta110FastRelicCatalog.GoldenPearl ||
        name is "MixedAnySatisfiedByCurse" or "PositiveBanFallback" or "LargeCapsuleSkipsFirstFlip";
    private static bool SecondaryNumeric(string name) => IndependentArms(name) ||
        name == "Single." + Beta110FastRelicCatalog.SmallCapsule || name == "Single." + Beta110FastRelicCatalog.Pomander ||
        name is "LockedKaleidoscope" or
        "ScrollBoxesDisallowed" or "UnavailableAnyStillHasCurse" or "ArcaneRouteProjection" or
        "CapsuleAuthoredUpgradeFinalCurse";
    private static void Equal(FamilyCandidateSet actual, FamilyCandidateSet expected, string scope)
    {
        if (!actual.ExportAbi1().Span.SequenceEqual(expected.ExportAbi1().Span))
            throw new InvalidDataException("NEntry." + scope);
    }
}
