using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;
using Environment = System.Environment;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Runtime;

// Bounded real-executor parity, deliberately outside timing and calibration.
// Inputs keep the supplied authority and eligibility premises. No synthetic pools.
internal static class FamilyPreparationChecks
{
    private sealed record Fixture(string Name, SearchQuery Query);
    private sealed record Sample(FamilyCandidateSet Input, FamilyCandidateSet Expected);
    private sealed record TFixture(string Name, PreparedSearch Request, ulong Witness);

    internal static void RunTEntry(PreparedSearch request, string output, bool productionDefaultOnly = false) =>
        Capture("RT2_T_BONES_EXPERIMENT", "", () => Capture("RT2_T_FULL_TARGET_EXPERIMENT", "", () =>
            Capture("RT2_T_GPU_PLAN_EXPERIMENT", "off", () => { RunTEntryCore(request, output, productionDefaultOnly); return true; })));

    private static void RunTEntryCore(PreparedSearch request, string output, bool productionDefaultOnly)
    {
        var profile = RuntimeProfileRegistry.Select(request.Model.Detection);
        if (profile.ProfileId != RuntimeProfileId.Beta111 || request.Model.Authority.PlayersCount != 1 ||
            request.Model.CompiledSearch.Context.Party is not null)
            throw new InvalidOperationException("TEntry.RequiresSingleplayerBeta111Context");
        var fixtures = BuildTEntryFixtures(request, profile);
        if (productionDefaultOnly)
        {
            fixtures = fixtures.Where(f => f.Name is "NewLeaf.Rare1" or "ClosedBones.GOLDEN_PEARL.Any" or "ExistingBonesNewLeaf.Rare1").ToList();
            if (fixtures.Count != 3) throw new InvalidDataException("TEntry.DefaultFixturesMissing");
        }
        var rows = new List<object>();
        long comparedInputs = 0;
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("TEntry.NoGpu");
        foreach (var fixture in fixtures)
        {
            var reference = new TransformationAggregateNumericalPlan(fixture.Request.Model);
            var samples = TEntrySamples(profile, reference, fixture.Witness);
            foreach (var numeric in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
            {
                var offFamily = Capture("RT2_T_ENTRY_IDENTITY", "off", () => PrepareT(fixture.Request, numeric));
                var onFamily = Capture("RT2_T_ENTRY_IDENTITY", productionDefaultOnly ? null : "track", () => PrepareT(fixture.Request, numeric));
                var off = offFamily.GpuPlan; var on = onFamily.GpuPlan;
                var opening = reference.Condition.Opening;
                bool newLeaf = opening == TransformationOpening.NewLeaf;
                bool closedBones = opening == TransformationOpening.BonesLeafyOther;
                bool unchanged = opening == TransformationOpening.BonesLeafyNewLeaf;
                if (!reference.Closed || !off.Supported || !on.Supported || off.EntryIdentityTracked ||
                    on.NewLeafTracked != newLeaf || on.ClosedBonesTracked != closedBones ||
                    off.BonesOptimized != on.BonesOptimized || off.FullTarget != on.FullTarget || off.PrefixReuse || on.PrefixReuse ||
                    unchanged && (!on.BonesOptimized || on.EntryIdentityTracked))
                    throw new InvalidDataException("TEntry.PlanScope:" + fixture.Name + ":" + string.Join(";",offFamily.Body.Preparation.Issues) + ":" + string.Join(";",onFamily.Body.Preparation.Issues));
                string offSource = Capture("RT2_T_ENTRY_IDENTITY", "off", off.ShaderSource);
                string onSource = Capture("RT2_T_ENTRY_IDENTITY", "track", on.ShaderSource);
                if (Capture("RT2_T_ENTRY_IDENTITY", "track", off.ShaderSource) != offSource ||
                    Capture("RT2_T_ENTRY_IDENTITY", "off", on.ShaderSource) != onSource ||
                    (offSource == onSource) != unchanged ||
                    (off.Revision(false) == on.Revision(false)) != unchanged)
                    throw new InvalidDataException("TEntry.PlanFreezeOrRevision:" + fixture.Name);
                using var offGpu = Capture("RT2_T_ENTRY_IDENTITY", "track", () => new TransformationAggregateGpuExecutor(rd, off));
                using var onGpu = Capture("RT2_T_ENTRY_IDENTITY", "off", () => new TransformationAggregateGpuExecutor(rd, on));
                foreach (var sample in samples)
                {
                    var original = offGpu.Execute(sample.Input, default, out _);
                    var actual = onGpu.Execute(sample.Input, default, out _);
                    string scope = "TEntry:" + fixture.Name + ":" + numeric + ":" + (sample.Input.IsDense ? "Dense" : "Compact");
                    Equal(original, sample.Expected, scope + ":OffVsOriginalCpu");
                    Equal(actual, original, scope + ":TrackVsSameBuildOff");
                    Equal(actual, sample.Expected, scope + ":TrackVsOriginalCpu");
                    comparedInputs += sample.Input.Count;
                    rows.Add(new { fixture.Name, numeric = numeric.ToString(), dense = sample.Input.IsDense,
                        sample.Input.Batch, inputs = sample.Input.Count, survivors = actual.Count, abi1Sha256 = Digest(actual),
                        on.NewLeafTracked, on.ClosedBonesTracked, on.BonesOptimized, on.FullTarget,
                        reference.Condition, fixture.Witness, revisionOff = off.Revision(!sample.Input.IsDense),
                        revisionTrack = on.Revision(!sample.Input.IsDense) });
                }
                Console.WriteLine($"T_ENTRY_GPU=PASS;fixture={fixture.Name};numeric={numeric};samples={samples.Count};newLeaf={on.NewLeafTracked};closedBones={on.ClosedBonesTracked};existingBones={on.BonesOptimized};fullTarget={on.FullTarget}");
            }
        }
        File.WriteAllText(Path.Combine(output, "transformation-entry-checks.json"), JsonSerializer.Serialize(new {
            passed = true, productionDefaultOnly, device = rd.GetDeviceName(), fixtures = fixtures.Count, comparedInputs, rows,
            oracle = "Original full-shuffle NeowSingleplayerReplay + existing local transform operators + canonical aggregate predicate; cross-checked against MatchesCollected",
            source = "Supplied replay authority/eligibility, original captured transform pools; named targets from a real identity/transform witness",
            limits = new[] { "Opening-only T atoms; event contributors not expanded", "No synthetic duplicate Bones pool or absent-identity authority fixtures", "Non-Closed companion/order combinations remain outside this entry optimization", "Original BonesLeafyNewLeaf defaults exercised with Rare1 generic and complete named FullTarget" },
            timingIsPerformanceSample = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"T_ENTRY_GPU_SUMMARY=PASS;fixtures={fixtures.Count};rows={rows.Count};comparedInputs={comparedInputs}");
    }

    private static TransformationAggregatePreparation PrepareT(PreparedSearch request,GpuNumericalVariant numeric) =>
        TransformationAggregateFamily.TryPrepare(request.Model,FilterExecutionPreferences.From(request.RunOptions),out var prepared,true,numeric) && prepared is not null
            ? prepared : throw new InvalidDataException("TEntry.PreparationUnavailable");

    private static List<TFixture> BuildTEntryFixtures(PreparedSearch source, IRuntimeProfile profile)
    {
        PreparedSearch Compile(TransformationAggregateCondition condition)
        {
            var result = SearchPreparationCompiler.Compile(SearchCompiler.Compile(SearchQuery.Empty with {
                TransformationAggregate = condition
            }, source.Model.CompiledSearch.Context), new("000000000000", 1027, int.MaxValue, 1));
            if (!result.Success) throw new InvalidDataException("TEntry.FixtureCompile:" + result.Issue);
            return result.Plan!;
        }
        ulong Root(ulong ordinal) => profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, ordinal));
        var fixtures = new List<TFixture>();
        void Add(string name, TransformationAggregateCondition condition, bool named = false, bool remainingRare = false)
        {
            var request = Compile(condition);
            var plan = new TransformationAggregateNumericalPlan(request.Model);
            if (!plan.Closed) throw new InvalidDataException("TEntry.RequiredFixtureNotClosed:" + name);
            // Bound the witness search independently of GPU, in a real high
            // global range. It remains a high local ordinal in Compact below.
            ulong witness = (1UL << 40) + (1 << 20), limit = witness + 262144;
            while (witness < limit && !TEntryReference(plan, Root(witness))) witness++;
            if (witness == limit) throw new InvalidDataException("TEntry.NoBoundedPositiveWitness:" + name);
            if (named)
            {
                var outputs = TEntryOutputs(plan, Root(witness));
                request = Compile(condition with { Predicate = remainingRare
                    ? TransformationAggregatePredicate.ContainsMultisetAndRemainingRare : TransformationAggregatePredicate.ContainsMultiset,
                    MinimumRareCount = 0, TargetMultiset = outputs.Select(o => o.Card!.Value).ToArray() });
                plan = new(request.Model);
                if (!TEntryReference(plan, Root(witness))) throw new InvalidDataException("TEntry.NamedWitnessLost:" + name);
            }
            var exact = ProductionExactSearchEvaluator.Evaluate(request.Model,
                TrustedRootHashInput.FromCanonicalSeed(profile, VisibleSeedCandidateCodec.FormatOrdinal(profile, witness)));
            if (!exact.IsMatch) throw new InvalidDataException("TEntry.PositiveExactWitness:" + name + ":" + exact.Evaluation.FailureCode);
            fixtures.Add(new(name, request, witness));
        }
        TransformationAggregateCondition Rare(TransformationOpening opening, TransformationPickupOrder order = TransformationPickupOrder.Any,
            ModelKey? companion = null) => new(opening, order, false, false, false,
                TransformationAggregatePredicate.RareCountAtLeast, 1, []) { CompanionRelic = companion };
        Add("NewLeaf.Rare1", Rare(TransformationOpening.NewLeaf));
        Add("NewLeaf.Named", Rare(TransformationOpening.NewLeaf), named: true);
        Add("NewLeaf.NamedRemainingRare", Rare(TransformationOpening.NewLeaf), named: true, remainingRare: true);
        foreach (var companion in new[] { BaseGameModelKeys.Relics.ArcaneScroll, BaseGameModelKeys.Relics.HeftyTablet,
            BaseGameModelKeys.Relics.Kaleidoscope, BaseGameModelKeys.Relics.NeowsTalisman })
            Add("ClosedBones." + companion.Entry + ".CompanionFirst", Rare(TransformationOpening.BonesLeafyOther,
                TransformationPickupOrder.CompanionThenLeafy, companion));
        Add("ClosedBones.GOLDEN_PEARL.Any", Rare(TransformationOpening.BonesLeafyOther,
            TransformationPickupOrder.Any, BaseGameModelKeys.Relics.GoldenPearl));
        Add("ClosedBones.LARGE_CAPSULE.LeafyFirst", Rare(TransformationOpening.BonesLeafyOther,
            TransformationPickupOrder.LeafyThenCompanion, BaseGameModelKeys.Relics.LargeCapsule));
        Add("ExistingBonesNewLeaf.Rare1", Rare(TransformationOpening.BonesLeafyNewLeaf));
        Add("ExistingBonesNewLeaf.FullNamed", Rare(TransformationOpening.BonesLeafyNewLeaf), named: true);
        return fixtures;
    }

    private static TransformationAggregateValidator.Output[] TEntryOutputs(TransformationAggregateNumericalPlan plan, ulong root)
    {
        var n = plan.Neow ?? throw new InvalidDataException("TEntry.NeowDonorMissing");
        var catalog = n.Authority.EffectCatalog;
        var ids = new List<ushort>();
        if (plan.Condition.Opening != TransformationOpening.NewLeaf)
        {
            var rng = new Beta110FastRng(unchecked(root + (ulong)n.Authority.PlayerSlotIndex + XxHash64.Hash("transformations"u8, 0)));
            Span<ushort> pair = stackalloc ushort[2]; NeowLocalOperators.DrawLeafyTransforms(catalog, ref rng, pair);
            ids.Add(pair[0]); ids.Add(pair[1]);
        }
        if (plan.Condition.Opening is TransformationOpening.NewLeaf or TransformationOpening.BonesLeafyNewLeaf)
        {
            var rng = new Beta110FastRng(unchecked(root + XxHash64.Hash("niche"u8, 0)));
            ids.Add(NeowLocalOperators.DrawNewLeafTransform(catalog, ref rng));
        }
        return ids.Select(id => new TransformationAggregateValidator.Output(catalog.DenseKeys[id],
            catalog.CardRarityByDenseId[id] == (byte)EffectCardRarity.Rare)).ToArray();
    }

    private static bool TEntryReference(TransformationAggregateNumericalPlan plan, ulong root)
    {
        bool expected = NeowSingleplayerReplay.Matches(root, plan.Neow!) &&
            TransformationAggregateValidator.EvaluateOutputs(plan.Condition, TEntryOutputs(plan, root)) == SearchDisposition.Match;
        if (expected != plan.MatchesCollected(root)) throw new InvalidDataException("TEntry.OriginalCpuCollectorMismatch");
        return expected;
    }

    private static List<Sample> TEntrySamples(IRuntimeProfile profile, TransformationAggregateNumericalPlan plan, ulong witness)
    {
        const ulong baseOrdinal = 1UL << 40;
        const int span = (1 << 24) - 3;
        var compactBatch = new SearchBatch(baseOrdinal, span);
        var ordinals = Enumerable.Range(0, 507).Select(i => (ulong)i * 32749 + 3)
            .Concat(new ulong[] { 0, 1, 65535, 65536, 1048575, 1048576, span - 1UL, witness - baseOrdinal })
            .Distinct().Order().ToArray();
        var inputs = new[] { FamilyCandidateSet.Dense(new SearchBatch(0, 1027)),
            FamilyCandidateSet.Dense(new SearchBatch(witness, 1027)), FamilyCandidateSet.FromSortedAbi1(compactBatch, ordinals) };
        var samples = inputs.Select(input => new Sample(input, FamilyCandidateSet.FromSortedAbi1(input.Batch,
            input.EnumerateLogicalOrdinals().Where(i => TEntryReference(plan, profile.ComputeRootSeed(
                VisibleSeedCandidateCodec.FormatOrdinal(profile, input.Batch.GlobalCandidate(i))))).ToArray()))).ToList();
        if (samples[1].Expected.Count == 0 || samples[2].Expected.Count == 0 || !samples.Any(s => s.Expected.Count < s.Input.Count))
            throw new InvalidDataException("TEntry.PositiveNegativePopulation");
        return samples;
    }

    internal static void RunE(PreparedSearch request, string output, bool productionDefaultOnly = false)
    {
        var profile = RuntimeProfileRegistry.Select(request.Model.Detection);
        if (profile.ProfileId != RuntimeProfileId.Beta111 || request.Model.CompiledSearch.Context.Party is not null)
            throw new InvalidOperationException("EPreparation.RequiresSingleplayerBeta111Context");
        var limits = new List<string>();
        var fixtures = BuildFixtures(request, limits);
        if (productionDefaultOnly)
        {
            fixtures = fixtures.Where(f => f.Name is "E.TrialTinkerMixed" or "E.TrashJointFake").ToList();
            if (fixtures.Count != 2) throw new InvalidDataException("EPreparation.DefaultFixturesMissing");
        }
        var rows = new List<object>();
        long comparedInputs = 0;
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("EPreparation.NoGpu");
        foreach (var fixture in fixtures)
        {
            var compiled = SearchCompiler.Compile(fixture.Query, request.Model.CompiledSearch.Context);
            var result = SearchPreparationCompiler.Compile(compiled,
                new("000000000000", 1027, int.MaxValue, 1));
            if (!result.Success) throw new InvalidOperationException("EPreparation.FixtureCompile:" + fixture.Name + ":" + result.Issue);
            var fixtureRequest = result.Plan!;
            foreach (var numeric in new[] { GpuNumericalVariant.NativeFloat64, GpuNumericalVariant.IntegerRne })
            {
                var off = Capture("RT2_E_PREPARATION", "off", () => new EventResultFamilyPlan(fixtureRequest.Model, numericalVariant: numeric));
                var on = Capture("RT2_E_PREPARATION", productionDefaultOnly ? null : "groups", () => new EventResultFamilyPlan(fixtureRequest.Model, numericalVariant: numeric));
                bool expectedPrepared = !productionDefaultOnly || fixture.Name == "E.TrialTinkerMixed";
                if (!off.GpuSupported || !on.GpuSupported || off.UsesPreparedGroups || on.UsesPreparedGroups != expectedPrepared)
                    throw new InvalidDataException("EPreparation.NotAdmitted:" + fixture.Name + ":" + off.GpuPreparationFailureDetail + ":" + on.GpuPreparationFailureDetail);
                var samples = Samples(profile, off.Reference, fixture.Name);
                string offSource = Capture("RT2_E_PREPARATION", "off", off.ShaderSource);
                string onSource = Capture("RT2_E_PREPARATION", "groups", on.ShaderSource);
                if (Capture("RT2_E_PREPARATION", "groups", off.ShaderSource) != offSource ||
                    Capture("RT2_E_PREPARATION", "off", on.ShaderSource) != onSource)
                    throw new InvalidDataException("EPreparation.PlanNotFrozen");
                using var offGpu = Capture("RT2_E_PREPARATION", "groups", () => new EventResultFamilyGpuExecutor(rd, off));
                using var onGpu = Capture("RT2_E_PREPARATION", "off", () => new EventResultFamilyGpuExecutor(rd, on));
                if ((offSource != onSource) != expectedPrepared) throw new InvalidDataException("EPreparation.SelectedShader:" + fixture.Name);
                foreach (var sample in samples)
                {
                    var original = offGpu.Execute(sample.Input, default, out _);
                    var actual = onGpu.Execute(sample.Input, default, out _);
                    string scope = fixture.Name + ":" + numeric + ":" + (sample.Input.IsDense ? "Dense" : "Compact");
                    Equal(original, sample.Expected, scope + ":OffVsOriginalCpu");
                    Equal(actual, original, scope + ":OnVsSameBuildOff");
                    Equal(actual, sample.Expected, scope + ":OnVsOriginalCpu");
                    comparedInputs += sample.Input.Count;
                    rows.Add(new { fixture.Name, numeric = numeric.ToString(), dense = sample.Input.IsDense,
                        sample.Input.Batch, inputs = sample.Input.Count, survivors = actual.Count,
                        abi1Sha256 = Digest(actual), shaderOffSha256 = Digest(offSource), shaderOnSha256 = Digest(onSource) });
                }
                Console.WriteLine($"E_PREPARATION_GPU=PASS;fixture={fixture.Name};numeric={numeric};samples={samples.Count};originalCpu=true;sameBuildOff=true;frozenPlan=true");
            }
        }
        File.WriteAllText(Path.Combine(output, "event-preparation-checks.json"), JsonSerializer.Serialize(new {
            passed = true, productionDefaultOnly, device = rd.GetDeviceName(), fixtures = fixtures.Count, comparedInputs, rows, limits,
            oracle = "Original E projectors, plus same-build off GPU; complete ordered ABI1",
            timingIsPerformanceSample = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"E_PREPARATION_GPU_SUMMARY=PASS;fixtures={fixtures.Count};rows={rows.Count};comparedInputs={comparedInputs};limits={string.Join('|', limits)}");
    }

    private static List<Fixture> BuildFixtures(PreparedSearch request, List<string> limits)
    {
        var fixtures = new List<Fixture>();
        var eventAuthority = Beta111EventResultAuthority.From(request.Model.Authority);
        if (!eventAuthority.StaticCatalogAuthorityExact || !eventAuthority.ColorfulPoolAuthorityExact)
            throw new InvalidDataException("EPreparation.RequiresExactEventCatalogAndColorfulPools");
        var colorKey = Beta111EventResultCatalog.ColorfulCharacterOrder.FirstOrDefault(k =>
            k != eventAuthority.OwnerCharacterKey && eventAuthority.UnlockedCharacterCardPoolKeys.Contains(k));
        if (!colorKey.IsValid) throw new InvalidDataException("EPreparation.NoEligibleColorfulTarget");
        var grab = new EventResultSearchCondition(EventResultConditionKind.TrashHeapGrabCard, Beta111EventResultCatalog.TrashHeapGrabCards[0]);
        var dive = new EventResultSearchCondition(EventResultConditionKind.TrashHeapDiveRelic, Beta111EventResultCatalog.TrashHeapDiveRelics[0]);
        var fake = new EventResultSearchCondition(EventResultConditionKind.FakeMerchantOfferedFakeRelic, Beta111EventResultCatalog.FakeMerchantRelics[0]);
        var color = new EventResultSearchCondition(EventResultConditionKind.ColorfulPhilosophersOfferedColor, colorKey);
        var trial = new EventResultSearchCondition(EventResultConditionKind.TrialCase, new("EVENT", "TRIAL")) { TrialCase = TrialCaseTarget.Nondescript };
        var tinker = new EventResultSearchCondition(EventResultConditionKind.TinkerTimeTypeAndRider, new("CARD", "MAD_SCIENCE")) {
            TinkerCardType = TinkerCardTypeTarget.Attack, TinkerRider = TinkerRiderTarget.Sapping
        };
        void E(string name, params EventResultSearchCondition[] conditions) => fixtures.Add(new(name,
            SearchQuery.Empty with { EventResultConditions = conditions }));
        E("E.TrashFake", grab, fake);
        E("E.TrashJointFake", grab, dive, fake);
        E("E.FakeColor", fake, color);
        // Deliberately authored after expensive groups: the prepared order moves
        // Trial/Tinker first without changing their original joint conditions.
        E("E.TrialTinkerMixed", fake, color, grab, tinker, trial);
        var morphic = request.Model.Evaluation.EventResultConditions.FirstOrDefault(c =>
            c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains && c.MorphicGroveScenario is not null && c.IsValid);
        if (morphic is not null) E("E.MorphicMixed", fake, morphic, trial, tinker);
        else limits.Add("MorphicMixed not run: supplied replay has no authored Morphic Grove scenario; no entry pools invented");
        return fixtures;
    }

    private static List<Sample> Samples(IRuntimeProfile profile, Func<ulong, bool> cpu, string fixture)
    {
        bool Matches(ulong ordinal) => cpu(profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, ordinal)));
        var dense = FamilyCandidateSet.Dense(new SearchBatch(0, 1027));
        const int span = (1 << 24) - 3;
        var sparseBatch = new SearchBatch(VisibleSeedCandidateCodec.SpaceSize(profile) - span, span);
        // Explicit high local ordinals, 16-bit/20-bit edges, and final candidate;
        // the input count stays bounded although the logical batch spans 16 Mi.
        var ordinals = Enumerable.Range(0, 508).Select(i => (ulong)i * 32749 + 3)
            .Concat(new ulong[] { 0, 1, 65535, 65536, 1048575, 1048576, span - 1UL }).Distinct().Order().ToArray();
        var inputs = new List<FamilyCandidateSet> { dense, FamilyCandidateSet.FromSortedAbi1(sparseBatch, ordinals) };
        Sample Evaluate(FamilyCandidateSet input) => new(input, FamilyCandidateSet.FromSortedAbi1(input.Batch,
            input.EnumerateLogicalOrdinals().Where(i => Matches(input.Batch.GlobalCandidate(i))).ToArray()));
        var samples = inputs.Select(Evaluate).ToList();
        if (!samples.Any(s => s.Expected.Count > 0))
        {
            ulong witness = 1027;
            while (witness < 16384 && !Matches(witness)) witness++;
            if (witness == 16384) throw new InvalidDataException("EPreparation.NoPositiveWitnessInBoundedScan:" + fixture);
            samples.Add(Evaluate(FamilyCandidateSet.Dense(new SearchBatch(witness, 515))));
        }
        if (!samples.Any(s => s.Expected.Count < s.Input.Count))
            throw new InvalidDataException("EPreparation.NoRejectionWitness:" + fixture);
        return samples;
    }

    private static T Capture<T>(string control, string? mode, Func<T> create)
    {
        string? previous = Environment.GetEnvironmentVariable(control);
        try { Environment.SetEnvironmentVariable(control, mode); return create(); }
        finally { Environment.SetEnvironmentVariable(control, previous); }
    }
    private static string Digest(string source) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
    private static string Digest(FamilyCandidateSet set) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(set.ExportAbi1().Span)));
    private static void Equal(FamilyCandidateSet actual, FamilyCandidateSet expected, string scope)
    {
        if (actual.Batch != expected.Batch || !actual.ExportAbi1().Span.SequenceEqual(expected.ExportAbi1().Span))
            throw new InvalidDataException("AEPreparation.Abi1Mismatch:" + scope + ":actual=" + actual.Count + ":expected=" + expected.Count);
    }
}
