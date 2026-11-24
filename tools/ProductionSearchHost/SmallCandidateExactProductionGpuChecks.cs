using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;

// One actual N-positive window from the authored scan. Real existing numerical
// bodies are compared before/after the new transport boundary, then the complete
// original request is checked for every handed-off candidate.
internal static class SmallCandidateExactProductionGpuChecks
{
    internal static async Task RunAsync(PreparedSearch request)
    {
        if (!VisibleSeedCandidateCodec.TryParseOrdinal(Beta111Profile.Instance, request.CanonicalStartSeed,
            out ulong startOrdinal, out _, out var issue)) throw new InvalidDataException(issue);
        FilterBodyDescription[] CreateFamilies()
        {
            var families = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true).ToArray();
            var n = families.SingleOrDefault(family => family.FamilyId == "N.Neow");
            if (n is null || families.Length < 2 || families.Any(family => !family.CanBindPrivateSerial))
                throw new InvalidOperationException("SmallCandidateExactProduction.RequiresNativeNAndPrivateBindableOriginalSuffixes");
            // Test the actual authored request. An unrelated R condition is not
            // required merely to instantiate the historical four-family fixture.
            return [n, ..families.Where(family => !ReferenceEquals(family, n))];
        }
        await using var context = new FilterRuntimeServices(requiresGpu: true, cpuWorkerCount: request.WorkerCount);
        var publicFamilies = CreateFamilies();
        var early = AutomaticPrivateSerialInvocation.Prepare(request.Model,FilterExecutionPreferences.From(request.RunOptions), CreateFamilies(), enableSmallCandidateExact: true);
        var complete = AutomaticPrivateSerialInvocation.Prepare(request.Model,FilterExecutionPreferences.From(request.RunOptions), CreateFamilies(), verify: true);
        try
        {
            FamilyCandidateSet? prefix = null;
            int testedWindows = 0;
            for (long offset = 0; offset < request.ScanCount && testedWindows < 64; offset += 1 << 24)
            {
                var probe = new SearchBatch(checked(startOrdinal + (ulong)offset), (int)Math.Min(1 << 24, request.ScanCount - offset));
                var observed = await publicFamilies[0].ExecuteAsync(context, FamilyCandidateSet.Dense(probe), CancellationToken.None);
                testedWindows++;
                if (observed.Count is >= 1 and <= 8) { prefix = observed; break; }
            }
            if (prefix is null) throw new InvalidDataException("SmallCandidateExactProduction.NoActualSmallPositiveInFirst64AuthoredWindows");
            var batch = prefix.Batch;

            ulong positiveOrdinal = batch.GlobalCandidate(prefix.ExportAbi1().Span[0]);
            string positive = Beta110SeedCodec.FormatOrdinal(positiveOrdinal);
            if (!NeowFamilyReplay.Matches(Beta111Profile.Instance.ComputeRootSeed(positive), NeowReplayPlan.Compile(request.Model,true)))
                throw new InvalidDataException("SmallCandidateExactProduction.SelectedPositiveCpuMismatch");
            var handedOff = await early.ExecuteAsync(context, FamilyCandidateSet.Dense(batch), CancellationToken.None);
            if (!prefix.ExportAbi1().Span.SequenceEqual(handedOff.ExportAbi1().Span))
                throw new InvalidDataException("SmallCandidateExactProduction.PrivatePrefixPayloadMismatch");
            var full = await complete.ExecuteAsync(context, FamilyCandidateSet.Dense(batch), CancellationToken.None);
            var publicFull = await LinearFamilyPipeline.FilterAsync(context, batch, HostFilterBindings.BindAll(publicFamilies,context), CancellationToken.None);
            if (!full.ExportAbi1().Span.SequenceEqual(publicFull.ExportAbi1().Span))
                throw new InvalidDataException("SmallCandidateExactProduction.FullPublicPrivateMismatch");
            string[] ExactMatches(FamilyCandidateSet candidates) => candidates.EnumerateLogicalOrdinals().Where(ordinal =>
            {
                string seed = Beta110SeedCodec.FormatOrdinal(batch.GlobalCandidate(ordinal));
                var exact = ProductionExactSearchEvaluator.Evaluate(request.Model, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, seed));
                if (exact.Evaluation.Disposition is SearchDisposition.Unknown or SearchDisposition.Unsupported)
                    throw new InvalidDataException("SmallCandidateExactProduction.ExactUnresolved:" + exact.FailureCode);
                return exact.IsMatch;
            }).Select(ordinal => Beta110SeedCodec.FormatOrdinal(batch.GlobalCandidate(ordinal))).ToArray();
            var earlyMatches = ExactMatches(handedOff);
            var fullMatches = ExactMatches(full);
            if (!earlyMatches.SequenceEqual(fullMatches))
                throw new InvalidDataException("SmallCandidateExactProduction.OriginalQueryMatchSetChanged");
            Console.WriteLine($"smallCandidateExactProduction=PASS;knownPrefixSeed={positive};rootWindow={batch.BatchBase};" +
                $"rootCount={batch.BatchCandidateCount};prefixCandidates={prefix.Count};fullFilterCandidates={full.Count};" +
                $"exactMatches={earlyMatches.Length};kernelParity=true;fullOriginalExact=true;families={string.Join(',', publicFamilies.Select(family => family.FamilyId))};originalRegisteredFamiliesOmitted=0;positiveWindowSource=AuthoredScan;testedWindows={testedWindows}");
        }
        finally
        {
            await early.DisposeAsync(context);
            await complete.DisposeAsync(context);
            foreach (var family in publicFamilies) await family.DisposeAsync(context);
        }
    }
}
