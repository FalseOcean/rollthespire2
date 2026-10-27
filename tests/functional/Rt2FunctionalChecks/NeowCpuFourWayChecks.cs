using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

// Arithmetic checks adapt the independently checked shion resource.17 method
// reference; invocation checks below use the current RT2 matcher and ABI1.
internal static class NeowCpuFourWayChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidOperationException("N four-way: " + why); }
        string? saved = Environment.GetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable);
        try
        {
            var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
            ExactSearchExecutionRequest Request(string path, int workers)
            {
                var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path);
                var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(workload.Query, context),
                    workload.Options with { WorkerCount = workers, ScanCount = 65539, TargetMatchCount = int.MaxValue });
                Check(result.Success, "fixture compile " + result.Issue);
                return result.Plan!;
            }
            NeowFamily Owner(ExactSearchExecutionRequest request)
            {
                Check(NeowFamily.TryCreate(request, out var family, false) && family is NeowFamily, "N scalar owner exists");
                return (NeowFamily)family!;
            }
            FamilyCpuExecution Selected(NeowFamily owner, string? setting)
            {
                Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, setting);
                return FamilyCpuExecution.ConfiguredRealizations(owner).OfType<FamilyCpuExecution>().First();
            }
            var nonApplicable = Owner(Request("v1/cases/N-entry.json", 1));
            Check(NeowCpuFourWayPreGate.TryCompile(nonApplicable.PricingReplayPlan, true) is null,
                "positive-slot-only shape keeps its scalar path");
            // Validate the setting before shape/hardware admission, so a mistyped
            // experiment cannot silently masquerade as its off control arm.
            foreach (string value in new[] { "auto", "1", "enabled", " on " })
            {
                Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, value);
                bool rejected = false;
                try { _ = NeowCpuFourWayPreGate.TryCompile(nonApplicable.PricingReplayPlan); }
                catch (InvalidOperationException error)
                {
                    rejected = error.Message.Contains("InvalidConfiguration", StringComparison.Ordinal) &&
                        error.Message.Contains(NeowCpuFourWayPreGate.EnvironmentVariable + "=" + value, StringComparison.Ordinal);
                }
                Check(rejected, "invalid flag is explicit even for an inapplicable shape: " + value);
            }
            foreach (string? value in new string?[] { null, "off", "OFF", "on", "On", "legacy", "LEGACY" })
            {
                Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, value);
                Check(NeowCpuFourWayPreGate.TryCompile(nonApplicable.PricingReplayPlan) is null,
                    "legal flag retains scalar fallback for an inapplicable shape");
            }
            if (!Avx2.IsSupported)
            {
                var unsupportedOwner = Owner(Request("family-cost-closure-v1/NN-Leafy.json", 1));
                Check(NeowCpuFourWayPreGate.TryCompile(unsupportedOwner.PricingReplayPlan, true) is null,
                    "unsupported hardware keeps trusted scalar implementation");
                foreach (string? value in new string?[] { null, "on", "off", "legacy" })
                {
                    var scalar = Selected(unsupportedOwner, value);
                    Check(!scalar.UsesFourWayPreGate && scalar.Condition(false).PhysicalImplementationRevision.Contains(".RootMode2.", StringComparison.Ordinal),
                        "unsupported hardware retains original byte roots for default and explicit controls");
                }
                Console.WriteLine($"NEOW_CPU_FOUR_WAY_PASS checks={checks};avx2=false;vector=SkippedHardwareUnavailable");
                return 0;
            }

            var settingPlan = Owner(Request("family-cost-closure-v1/NN-Leafy.json", 1)).PricingReplayPlan;
            Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, null);
            var defaultGate = NeowCpuFourWayPreGate.TryCompile(settingPlan);
            Check(defaultGate is { Enabled: true }, "missing flag enables the proven four-way gate for an eligible shape");
            Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, "LEGACY");
            Check(NeowCpuFourWayPreGate.TryCompile(settingPlan) is null, "legacy restores the original byte-root body");
            Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, "On");
            var onSnapshot = NeowCpuFourWayPreGate.TryCompile(settingPlan);
            Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, "OFF");
            var offSnapshot = NeowCpuFourWayPreGate.TryCompile(settingPlan);
            Check(onSnapshot is { Enabled: true } && offSnapshot is { Enabled: false } && defaultGate is { Enabled: true },
                "case-insensitive flag is snapshotted without mutating earlier gate instances");

            var random = new Xoshiro256StarStar(0x51C4D23A907BD6EFUL);
            ulong offset = XxHash64.Hash("NEOW"u8, 0);
            for (int group = 0; group < 256; group++)
            {
                var roots = group == 0 ? Vector256.Create(0UL, ulong.MaxValue, 1UL, ulong.MaxValue - offset) :
                    Vector256.Create(random.NextUInt64(), random.NextUInt64(), random.NextUInt64(), random.NextUInt64());
                var raw = NeowCpuFourWayPreGate.FirstDraws(roots);
                for (int bound = 1; bound <= 64; bound++)
                {
                    var ranks = NeowCpuFourWayPreGate.Ranks(raw, bound);
                    for (int lane = 0; lane < 4; lane++)
                    {
                        var oracle = new Xoshiro256StarStar(unchecked(roots.GetElement(lane) + offset));
                        Check(raw.GetElement(lane) == oracle.NextUInt64(), "four-lane raw first draw");
                        Check(ranks.GetElement(lane) == new Xoshiro256StarStar(unchecked(roots.GetElement(lane) + offset)).NextInt(bound),
                            "four-lane CPU bounded draw");
                    }
                }
            }
            int floorDifferences = 0;
            for (int bound = 1; bound <= 64; bound++)
            for (int edge = 0; edge <= bound; edge++)
            for (int delta = -3; delta <= 3; delta++)
            {
                long mantissa = (long)(((UInt128)(uint)edge * (1UL << 53)) / (uint)bound) + delta;
                if (mantissa < 0 || (ulong)mantissa >= 1UL << 53) continue;
                ulong raw = (ulong)mantissa << 11;
                ulong state = unchecked(BitOperations.RotateRight(raw * Inverse(9), 7) * Inverse(5));
                int expected = Xoshiro256StarStar.FromState(0, state, 0, 0).NextInt(bound);
                Check(Xoshiro256StarStar.FromState(0, state, 0, 0).NextUInt64() == raw, "raw boundary injection");
                var actual = NeowCpuFourWayPreGate.Ranks(Vector256.Create(raw), bound);
                for (int lane = 0; lane < 4; lane++) Check(actual.GetElement(lane) == expected, "binary64 boundary rank");
                if (expected != (int)(((UInt128)(ulong)mantissa * (uint)bound) >> 53)) floorDifferences++;
            }
            Check(floorDifferences > 0, "boundary corpus distinguishes forbidden integer-floor replacement");

            foreach (string path in new[] { "family-cost-closure-v1/NN-Leafy.json", "cpu-main-v1/N-identity-source.json" })
            foreach (int workers in new[] { 1, 3, 7 })
            {
                var request = Request(path, workers);
                var owner = Owner(request);
                var gate = NeowCpuFourWayPreGate.TryCompile(owner.PricingReplayPlan, true);
                Check(gate is not null, "representative N shape has a proven first-draw gate");
                var scalar = Selected(owner, "off");
                var vector = Selected(owner, null);
                var legacy = Selected(owner, "legacy");
                Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, "off");
                Check(scalar.Condition(false).PhysicalImplementationRevision.Contains(".RootMode3.", StringComparison.Ordinal) &&
                    vector.Condition(false).PhysicalImplementationRevision.Contains(NeowCpuFourWayPreGate.Revision, StringComparison.Ordinal) &&
                    legacy.Condition(false).PhysicalImplementationRevision.Contains(".RootMode2.", StringComparison.Ordinal) &&
                    !legacy.UsesFourWayPreGate && vector.UsesFourWayPreGate,
                    "default vector, packed scalar comparison and original byte-root identities");
                foreach (IFamilyInvocation body in new IFamilyInvocation[] { scalar, vector })
                    Check(body.QuotePhysicalWork(new(false, false, 65536)) is not { } quote ||
                        FamilyCanonicalCostChecks.MatchesEmbeddedSingleInvocation(request, body, quote, 65536),
                        "changed packed/vector bodies require their actual matching embedded canonical measurement");
                var original = NeowFamilyReplay.Bind(owner.PricingReplayPlan);
                var execution = new FamilyExecutionContext(false, cpuWorkerCount: workers);
                try
                {
                    foreach (int length in new[] { 1, 2, 3, 4, 5, 7, 17, 1025, 8193, 65539 })
                    foreach (ulong start in new[] { 34UL * 34 * 34 * 34 - 3, Beta110SeedCodec.SpaceSize - (ulong)length })
                    foreach (bool compact in new[] { false, true })
                    {
                        var batch = new SearchBatch(start, length);
                        var input = compact ? FamilyCandidateSet.FromSortedAbi1(batch, Enumerable.Range(0, length)
                            .Where(i => i % 5 is 0 or 3 or 4).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                        var oracle = new FamilyCpuExecution(request, "Oracle", "OriginalProfile", workers);
                        var expected = oracle.Execute(new(batch, request), input, default, original, execution).ExportAbi1().ToArray();
                        var scalarAbi = ((IFamilyInvocation)scalar).InvokeAsync(execution, new(batch, request), input, default).GetAwaiter().GetResult().ExportAbi1().ToArray();
                        var legacyAbi = ((IFamilyInvocation)legacy).InvokeAsync(execution, new(batch, request), input, default).GetAwaiter().GetResult().ExportAbi1().ToArray();
                        long before = vector.FourWayInputs;
                        var vectorAbi = ((IFamilyInvocation)vector).InvokeAsync(execution, new(batch, request), input, default).GetAwaiter().GetResult().ExportAbi1().ToArray();
                        Check(vectorAbi.SequenceEqual(expected) && scalarAbi.SequenceEqual(expected) && legacyAbi.SequenceEqual(expected), "default/packed/legacy full matcher/profile ABI1 parity, carry/end-space/tail");
                        if (compact) Check(vector.FourWayInputs == before, "sparse Compact stays scalar");
                    }
                    Check(vector.FourWayInputs > 0 && vector.FourWayRejected > 0 && vector.FourWayScalarInputs > 0,
                        "snapshot on exercised vector rejection and scalar tails despite later env off");
                    Check(execution.PeakCpuWorkers <= workers, "vector work shares the original worker budget");
                }
                finally { execution.DisposeAsync().GetAwaiter().GetResult(); }
            }

            var faultRequest = Request("family-cost-closure-v1/NN-Leafy.json", 1);
            var faultOwner = Owner(faultRequest);
            var faultGate = NeowCpuFourWayPreGate.TryCompile(faultOwner.PricingReplayPlan, true)!;
            bool FirstPass(ulong global)
            {
                ulong root = CpuPackedSeed12.FromOrdinal(global).Hash();
                int rank = new Xoshiro256StarStar(unchecked(root + offset)).NextInt(faultGate.Bound);
                return (faultGate.AllowedIndexes & (1UL << rank)) != 0;
            }
            // All four vector lanes, scalar tail, and the tail after a full private
            // window retain batch-local/global/root attribution independently.
            foreach (int at in new[] { 0, 1, 2, 3, 4, 65537 })
            {
                ulong start = 1000;
                while (!FirstPass(start + (ulong)at)) start++;
                var batch = new SearchBatch(start, at == 65537 ? 65539 : 5);
                ulong root = CpuPackedSeed12.FromOrdinal(start + (ulong)at).Hash();
                var failed = new FamilyCpuExecution(faultRequest, faultOwner, "InjectedFourWayFault",
                    candidate => candidate == root ? throw new IOException("InjectedFullMatcherFault") : false, 1, _ => null,
                    packedSeedRoots: true, fourWayPreGate: faultGate);
                Exception? caught = null;
                var faultExecution = new FamilyExecutionContext(false);
                try { _ = ((IFamilyInvocation)failed).InvokeAsync(faultExecution, new(batch, faultRequest), FamilyCandidateSet.Dense(batch), default).GetAwaiter().GetResult(); }
                catch (Exception error) { caught = error; }
                finally { faultExecution.DisposeAsync().GetAwaiter().GetResult(); }
                Check(caught is not null && FamilyCpuMatchException.IsRecoverable(caught), "full matcher failure keeps isolation certificate");
                var certificate = FamilyCpuMatchException.Matches(caught!).Single();
                Check(certificate.Batch == batch && certificate.LogicalOrdinal == (ulong)at && certificate.GlobalOrdinal == start + (ulong)at &&
                    certificate.RootHash == root && certificate.InnerException is IOException, "vector lane/tail exact failure attribution");
            }
            var cancellation = new CancellationTokenSource(); bool cancel = true;
            var cancelCpu = new FamilyCpuExecution(faultRequest, faultOwner, "CancelledFourWay",
                root => { if (cancel) cancellation.Cancel(); return NeowFamilyReplay.Matches(root, faultOwner.PricingReplayPlan); },
                1, _ => null, packedSeedRoots: true, fourWayPreGate: faultGate);
            var cancelBatch = new SearchBatch(1000, 4099);
            var cancelExecution = new FamilyExecutionContext(false, cpuWorkerCount: 1);
            try
            {
                bool cancelled = false;
                try { _ = ((IFamilyInvocation)cancelCpu).InvokeAsync(cancelExecution, new(cancelBatch, faultRequest), FamilyCandidateSet.Dense(cancelBatch), cancellation.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && !((IFamilyInvocation)cancelCpu).ExecutionFaulted, "cancellation is not a matcher fault");
                cancel = false;
                _ = ((IFamilyInvocation)cancelCpu).InvokeAsync(cancelExecution, new(cancelBatch, faultRequest), FamilyCandidateSet.Dense(cancelBatch), default).GetAwaiter().GetResult();
                Check(cancelExecution.PeakCpuWorkers == 1, "cancelled vector partition releases its permit for reuse");
            }
            finally { cancellation.Dispose(); cancelExecution.DisposeAsync().GetAwaiter().GetResult(); }
            Console.WriteLine($"NEOW_CPU_FOUR_WAY_PASS checks={checks};avx2=true;fullAbi1=true;roundingBoundaries=true;faultLanesAndTails=true;cancelReuse=true;native=false");
            return 0;
        }
        finally { Environment.SetEnvironmentVariable(NeowCpuFourWayPreGate.EnvironmentVariable, saved); }
    }

    private static ulong Inverse(ulong value)
    {
        ulong inverse = 1;
        for (int i = 0; i < 6; i++) inverse = unchecked(inverse * (2 - value * inverse));
        return inverse;
    }
}
