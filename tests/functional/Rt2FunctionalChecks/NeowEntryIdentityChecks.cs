using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static class NeowEntryIdentityChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidDataException("NEntry." + why); }
        foreach (string? mode in new string?[] { null, "", "default", "track" })
            Check(NeowEntryIdentityOptimization.Parse(mode) == NeowEntryIdentityMode.TrackSingleRequired,
                "DefaultTrackAlias:" + (mode ?? "unset"));
        Check(NeowEntryIdentityOptimization.Parse("off") == NeowEntryIdentityMode.Off &&
            NeowEntryIdentityOptimization.Parse("reject") == NeowEntryIdentityMode.RejectUnavailable &&
            NeowEntryIdentityOptimization.Parse("all") ==
                (NeowEntryIdentityMode.TrackSingleRequired | NeowEntryIdentityMode.RejectUnavailable), "ExplicitControls");
        foreach (string value in new[] { "on", "TRACK", "missing", " off " })
        {
            bool failed = false;
            try { _ = NeowEntryIdentityOptimization.Parse(value); }
            catch (InvalidOperationException ex) { failed = ex.Message.Contains(value, StringComparison.Ordinal); }
            Check(failed, "InvalidModeExplicit:" + value);
        }
        const string root = "tools/ProductionSearchHost/Corpus/";
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(root + "owner-nwae-v1/context.json").Context;
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(root + "physical-composition-v1/Arcane27.json");
        var request = SearchPreparationCompiler.Compile(SearchCompiler.Compile(workload.Query, context), workload.Options).Plan
            ?? throw new InvalidDataException("NEntry.FixtureCompile");
        var fixtures = NeowEntryIdentityFixtures.Build(NeowReplayPlan.Compile(request.Model, false));
        CheckProductionEntry();
        int additions = 0, tracked = 0, fallback = 0;
        foreach (var fixture in fixtures)
        {
            var off = NeowEntryIdentityFixtures.Compile(fixture.Replay, "off");
            var normal = NeowEntryIdentityFixtures.Compile(fixture.Replay, null);
            Check(off.EntryIdentityMode == NeowEntryIdentityMode.Off && off.EntryIdentityRevision == "",
                fixture.Name + ".ExplicitOffBaseline");
            Check(normal.EntryIdentityMode == (fixture.Track ? NeowEntryIdentityMode.TrackSingleRequired : NeowEntryIdentityMode.Off),
                fixture.Name + ".NormalLaunchTrackApplicability");
            Check(normal.Meta[68] == off.Meta[68] && normal.Meta[69] == off.Meta[69] &&
                (normal.EntryIdentityMode & NeowEntryIdentityMode.RejectUnavailable) == 0,
                fixture.Name + ".NormalLaunchRejectUnchanged");
            Check(normal.EntryIdentityMetaOffset == (fixture.Track ? off.Meta.Length : -1) &&
                normal.Meta.Length == off.Meta.Length + (fixture.Track ? 20 : 0) &&
                (normal.EntryIdentityTarget != 255) == fixture.Track &&
                (normal.EntryIdentityRevision.Length != 0) == fixture.Track,
                fixture.Name + ".NormalLaunchTrackingOrFullShuffle");
            uint[] capturedMeta = normal.Meta.ToArray();
            foreach (string alias in new[] { "", "default", "track" })
            {
                var aliasPlan = NeowEntryIdentityFixtures.Compile(fixture.Replay, alias);
                Check(aliasPlan.EntryIdentityMode == normal.EntryIdentityMode &&
                    aliasPlan.EntryIdentityTarget == normal.EntryIdentityTarget &&
                    aliasPlan.EntryIdentityMetaOffset == normal.EntryIdentityMetaOffset &&
                    aliasPlan.EntryIdentityRevision == normal.EntryIdentityRevision && aliasPlan.Meta.SequenceEqual(capturedMeta),
                    fixture.Name + ".DefaultPlanAlias:" + alias);
            }
            _ = NeowEntryIdentityFixtures.Compile(fixture.Replay, "off");
            Check(normal.Meta.SequenceEqual(capturedMeta) &&
                normal.EntryIdentityMode == (fixture.Track ? NeowEntryIdentityMode.TrackSingleRequired : NeowEntryIdentityMode.Off),
                fixture.Name + ".CapturedPlanImmutableAfterModeChange");
            foreach (string mode in NeowEntryIdentityFixtures.Modes.Skip(1))
            {
                var plan = NeowEntryIdentityFixtures.Compile(fixture.Replay, mode);
                bool track = (plan.EntryIdentityMode & NeowEntryIdentityMode.TrackSingleRequired) != 0;
                Check(track == (fixture.Track && mode != "reject"), fixture.Name + ".TrackApplicability:" + mode);
                Check(((plan.EntryIdentityMode & NeowEntryIdentityMode.RejectUnavailable) != 0) == (mode != "track"), fixture.Name + ".RejectApplicability:" + mode);
                Check(plan.Meta[68] == (off.Meta[68] & ~plan.Meta[69]), fixture.Name + ".AcceptedMaskOnlyNarrows:" + mode);
                uint added = plan.Meta[69] & ~off.Meta[69];
                if (fixture.AdditionalReject && mode != "track") Check(added != 0, fixture.Name + ".ImpossibleCurseActuallyRejected:" + mode);
                if (mode == "track") Check(added == 0, fixture.Name + ".TrackDoesNotEnableReject");
                if (added != 0) additions++;
                if (track)
                {
                    tracked++;
                    Check(plan.EntryIdentityMetaOffset == off.Meta.Length && plan.Meta.Length == off.Meta.Length + 20 &&
                        plan.EntryIdentityTarget != 255, fixture.Name + ".BoundedTrackingMetadata:" + mode);
                }
                else
                {
                    fallback++;
                    Check(plan.EntryIdentityMetaOffset == -1 && plan.EntryIdentityTarget == 255,
                        fixture.Name + ".FullShuffleRetained:" + mode);
                }
                // A newly rejected first draw must never hide a passing original
                // full-array observation. This deliberately does not reimplement
                // the compiler's pool-union proof as its own test oracle.
                for (ulong seed = 0; seed < 1027; seed++)
                {
                    byte curse = NeowSingleplayerReplay.ObserveCurse(seed, fixture.Replay, out _);
                    int ordinal = Array.IndexOf(fixture.Replay.Authority.EligibleCurseRelicIds, curse);
                    if ((added & (1u << ordinal)) != 0)
                        Check(!NeowSingleplayerReplay.Observe(seed, fixture.Replay).IdentityPass,
                            fixture.Name + ".NoFalseEarlyRejection:" + mode + ":" + seed);
                }
            }
        }
        Check(additions > 0 && tracked > 0 && fallback > 0, "BothOptimizationsAndFallbackExercised");
        Console.WriteLine($"N_ENTRY_IDENTITY_MANAGED=PASS;checks={checks};fixtures={fixtures.Count};newRejectMasks={additions};tracked={tracked};fallback={fallback};oracle=OriginalFullShuffle;nativeGpuNotRun=true");
        return 0;

        void CheckProductionEntry()
        {
            var query = SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.GoldenPearl) };
            var entryRequest = SearchPreparationCompiler.Compile(SearchCompiler.Compile(query, context), workload.Options).Plan
                ?? throw new InvalidDataException("NEntry.ProductionEntryCompile");
            string? previous = Environment.GetEnvironmentVariable(NeowEntryIdentityFixtures.Variable);
            string? direct = Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT");
            try
            {
                Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", "baseline");
                var plans = new List<NeowFamilyGpuPlan>();
                foreach (string? mode in new string?[] { null, "", "default", "off" })
                {
                    Environment.SetEnvironmentVariable(NeowEntryIdentityFixtures.Variable, mode);
                    bool admitted = NeowFamily.TryPrepare(entryRequest.Model,FilterExecutionPreferences.From(entryRequest.RunOptions),out var prepared);
                    Check(admitted && prepared is { GpuPlan:not null, Domain.GpuPreparationFailed:false },
                        "ProductionEntrySoloGpuPlan:" + (mode ?? "unset"));
                    var plan = prepared!.GpuPlan!;
                    plans.Add(plan);
                    Check(plan.EntryIdentityMode == (mode == "off" ? NeowEntryIdentityMode.Off : NeowEntryIdentityMode.TrackSingleRequired) &&
                        (plan.EntryIdentityTarget == Beta110FastRelicCatalog.GoldenPearl) == (mode != "off"),
                        "ProductionEntryDefaultSelection:" + (mode ?? "unset"));
                }
                var off = plans[^1];
                Check(plans.Take(3).All(p => p.Meta[68] == off.Meta[68] && p.Meta[69] == off.Meta[69] &&
                    p.EntryIdentityMetaOffset == off.Meta.Length && p.Meta.Length == off.Meta.Length + 20 &&
                    p.EntryIdentityMode == NeowEntryIdentityMode.TrackSingleRequired),
                    "ProductionEntryCapturedDefaultPlansKeepTrackingAndRejectMasks");
            }
            finally
            {
                Environment.SetEnvironmentVariable(NeowEntryIdentityFixtures.Variable, previous);
                Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", direct);
            }
        }
    }
}
