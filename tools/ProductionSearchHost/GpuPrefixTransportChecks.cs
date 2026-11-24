using System.Text.Json;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

// Small numerical/transport boundary check. Complete search, cancellation and
// continuation comparisons use the normal production host, not this entry.
internal static class GpuPrefixTransportChecks
{
    internal static async Task RunAsync(PreparedSearch source, string output)
    {
        int checks = 0;
        void Check(bool ok, string reason)
        { checks++; if (!ok) throw new InvalidDataException("GpuPrefixTransportGpu:" + reason); }
        var context = source.Model.CompiledSearch.Context;
        ModelKey target = Beta111MerchantColorlessAuthority.From(context.Authority).UncommonPool[0];
        var query = SearchQuery.Empty with {
            LegacyNeow = LegacyNeowSemanticConstraints.Empty with {
                NeowRelics = new([new("RELIC", "GOLDEN_PEARL")], [], []) },
            MerchantColorlessConditions = [new(1, MerchantColorlessSlot.Uncommon, target)],
            StandardMaps = [new(1, StandardMapMetric.GuaranteedMonster, StandardMapComparison.AtLeast, 8)] };
        PreparedSearch Request(bool skip) => SearchPreparationCompiler.Compile(
            SearchCompiler.Compile(query, context), new("000000000000", 65536, int.MaxValue, 1,
                SkipExactValidation: skip, ManualFamilyOrder: ["N.Neow", "S.MerchantShopColorless", "M.StandardMap"])).Plan
                ?? throw new InvalidDataException("GpuPrefixTransportGpu.Compile");
        string? old = Environment.GetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT");
        var evidence = new List<object>();
        try
        {
            var request = Request(skip: true);
            Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", null);
            var resident = FamilyExecutionCoordinator.Plan(request, true, gpuFloat64Available: true);
            Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", "off");
            var ordinary = FamilyExecutionCoordinator.Plan(request, true, gpuFloat64Available: true);
            var cpu = FamilyExecutionCoordinator.Plan(request, false);
            Check(resident.OrderedFamilies.Count == 2 && resident.OrderedFamilies[0].IsPrivateTransport &&
                resident.OrderedFamilies[1].FamilyId == "M.StandardMap" && ordinary.OrderedFamilies.Count == 3,
                "actual coordinator selects NS private prefix plus CPU M and off restores public NSM");
            var prefix = resident.OrderedFamilies[0];
            Check(resident.OrderedFamilies.Skip(1).SelectMany(f=>f.Coverage).SequenceEqual(["M.StandardMap"]), "private early-handoff metadata retains CPU-tail obligation");
            await using var execution = new FilterRuntimeServices(requiresGpu: true, cpuWorkerCount: 1);
            try
            {
                var large = new SearchBatch(0, 65536);
                var largePrefix = await prefix.ExecuteAsync(execution, FamilyCandidateSet.Dense(large), default);
                Check(largePrefix.Count > 8 && !largePrefix.IsDense, "actual NS prefix reaches CPU boundary above early-Exact threshold");
                await Compare(large, largePrefix, "large");
                FamilyCandidateSet? small = null;
                for (ulong start = 0; start < 16384 && small is null; start += 512)
                {
                    var batch = new SearchBatch(start, 512);
                    var observed = await prefix.ExecuteAsync(execution, FamilyCandidateSet.Dense(batch), default);
                    if (observed.Count is >= 1 and <= 8) small = observed;
                }
                Check(small is not null, "authored real NS kernels supply a small positive boundary witness");
                await Compare(small!.Batch, small, "small-skip-exact");

                var exactRequest = Request(skip: false);
                Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", null);
                var earlyPlan = FamilyExecutionCoordinator.Plan(exactRequest, true, gpuFloat64Available: true);
                var earlyTail = new ObservedTail(HostFilterBindings.Bind(earlyPlan.OrderedFamilies[1], execution));
                var policy = new SmallCandidateExactPolicy();
                try
                {
                    var handedOff = await LinearFamilyPipeline.FilterAsync(execution, small.Batch,
                        [HostFilterBindings.Bind(earlyPlan.OrderedFamilies[0], execution), earlyTail], policy, default);
                    Check(earlyTail.Calls == 0 && handedOff.Count is >= 1 and <= 8,
                        "normal small-candidate policy hands the original request to Exact before CPU tail");
                    var complete = await LinearFamilyPipeline.FilterAsync(execution, small.Batch, HostFilterBindings.BindAll(cpu.OrderedFamilies, execution), default);
                    string[] ExactMatches(FamilyCandidateSet candidates) => candidates.EnumerateLogicalOrdinals().Where(ordinal => {
                        string seed = Beta110SeedCodec.FormatOrdinal(candidates.Batch.GlobalCandidate(ordinal));
                        var result = ProductionExactSearchEvaluator.Evaluate(exactRequest.Model,
                            TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
                        Check(result.Evaluation.Disposition is SearchDisposition.Match or SearchDisposition.NoMatch,
                            "original whole request has a resolved Exact disposition");
                        return result.IsMatch;
                    }).Select(ordinal => Beta110SeedCodec.FormatOrdinal(candidates.Batch.GlobalCandidate(ordinal))).ToArray();
                    Check(ExactMatches(handedOff).SequenceEqual(ExactMatches(complete)),
                        "early Exact still enforces M and agrees with the complete CPU-filter request");
                    evidence.Add(new { arm = "small-exact", batchBase = small.Batch.BatchBase,
                        prefixCandidates = handedOff.Count, cpuTailCalls = earlyTail.Calls, exactParity = true });
                }
                finally { foreach (var family in earlyPlan.OrderedFamilies) await family.DisposeAsync(execution); }

                async Task Compare(SearchBatch batch, FamilyCandidateSet expectedPrefix, string arm)
                {
                    var residentTail = new ObservedTail(HostFilterBindings.Bind(resident.OrderedFamilies[1], execution));
                    var ordinaryTail = new ObservedTail(HostFilterBindings.Bind(ordinary.OrderedFamilies[2], execution));

                    var actual = await LinearFamilyPipeline.FilterAsync(execution, batch,
                        [HostFilterBindings.Bind(resident.OrderedFamilies[0], execution), residentTail], default);
                    var control = await LinearFamilyPipeline.FilterAsync(execution, batch,
                        [HostFilterBindings.Bind(ordinary.OrderedFamilies[0], execution), HostFilterBindings.Bind(ordinary.OrderedFamilies[1], execution), ordinaryTail], default);
                    var reference = await LinearFamilyPipeline.FilterAsync(execution, batch, HostFilterBindings.BindAll(cpu.OrderedFamilies, execution), default);
                    Check(residentTail.Calls == 1 && !residentTail.DenseInput && residentTail.Batch == batch &&
                        residentTail.Input.SequenceEqual(expectedPrefix.EnumerateLogicalOrdinals()),
                        "CPU M consumes the real ordered batch-local prefix exactly once:" + arm);
                    Check(actual.EnumerateLogicalOrdinals().SequenceEqual(control.EnumerateLogicalOrdinals()) &&
                        actual.EnumerateLogicalOrdinals().SequenceEqual(reference.EnumerateLogicalOrdinals()),
                        "resident/public GPU/independent CPU complete filter outputs agree:" + arm);
                    evidence.Add(new { arm, batchBase = batch.BatchBase, rootCount = batch.BatchCandidateCount,
                        prefixCandidates = expectedPrefix.Count, cpuTailCalls = residentTail.Calls,
                        survivors = actual.Count, skipExact = true, orderedAbi1Parity = true });
                }
            }
            finally
            {
                foreach (var family in resident.OrderedFamilies.Concat(ordinary.OrderedFamilies).Concat(cpu.OrderedFamilies))
                    await family.DisposeAsync(execution);
            }
        }
        finally { Environment.SetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT", old); }
        File.WriteAllText(Path.Combine(output, "gpu-prefix-transport-checks.json"),
            JsonSerializer.Serialize(new { checks, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"gpuPrefixTransportGpu=PASS;checks={checks};realKernels=NS;cpuTail=M;skipExactTailConsumed=true;orderedAbi1Parity=true;wholeRequestExactParity=true");
    }

    private sealed class ObservedTail(IFilterExecution inner) : IFilterExecution
    {
        internal int Calls;
        internal bool DenseInput;
        internal SearchBatch? Batch;
        internal ulong[] Input = [];
        public FilterBodyDescription Body => inner.Body;
        public bool ExecutionFaulted => inner.ExecutionFaulted;
        public (string Device,double SetupMs) ResourceInfo => inner.ResourceInfo;
        public ValueTask DisposeAsync(FilterRuntimeServices services) => inner.DisposeAsync(services);
        public ValueTask<FamilyCandidateSet> FilterAsync(FilterRuntimeServices execution,
            FamilyCandidateSet input, CancellationToken token)
        {
            Calls++; DenseInput = input.IsDense; Batch = input.Batch; Input = input.EnumerateLogicalOrdinals().ToArray();
            return inner.FilterAsync(execution, input, token);
        }
    }
}
