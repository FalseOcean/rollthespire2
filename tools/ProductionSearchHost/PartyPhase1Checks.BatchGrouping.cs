using System.Reflection;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

internal static partial class PartyPhase1Checks
{
    private static async Task CheckNeowDenseBatchPairs(ExactSearchExecutionRequest request,
        FamilyExecutionContext execution, Action<bool, string> check)
    {
        foreach (bool fused in new[] { false, true })
        {
            var grouped = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: fused);
            var single = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: fused);
            var cpu = new NeowFamily.PartyInitial(request, preferGpu: false, optimize: false);
            var grouping = (IDenseFamilyBatchGrouping)grouped;
            check(grouping.CanGroupDenseBatches, "real 2p native N admits requested private/fused pair role");
            try
            {
                var pairs = new[]
                {
                    (new SearchBatch(7, 32771), new SearchBatch(1234567, 32789)),
                    (new SearchBatch(98765, 32783), new SearchBatch((1UL << 32) + 33, 32771))
                };
                for (int trial = 0; trial < pairs.Length; trial++)
                {
                    var (first, second) = pairs[trial];
                    if (trial > 0)
                    {
                        // The same wrapper returns to Dense after using its distinct
                        // compact-first binding. Neither root may reuse stale meta.
                        var compactBatch = new SearchBatch(4242, 4099);
                        var compact = FamilyCandidateSet.FromSortedAbi1(compactBatch,
                            Enumerable.Range(0, compactBatch.BatchCandidateCount).Where(i => i % 3 == 1).Select(i => (ulong)i).ToArray());
                        var compactWindow = new FamilyObservationWindow(compactBatch, request);
                        var expectedCompact = await cpu.InvokeAsync(execution, compactWindow, compact, default);
                        var actualCompact = await grouped.InvokeAsync(execution, compactWindow, compact, default);
                        var singleCompact = await single.InvokeAsync(execution, compactWindow, compact, default);
                        CheckPairAbi1(expectedCompact, actualCompact, check, "Compact transition whole-table CPU");
                        CheckPairAbi1(singleCompact, actualCompact, check, "Compact transition independent single GPU");
                        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                        bool cancelled = false;
                        try { await grouping.PrepareDensePairAsync(execution, new(first, request), new(second, request), cancellation.Token); }
                        catch (OperationCanceledException) { cancelled = true; }
                        check(cancelled && grouping.CanGroupDenseBatches, "pre-cancelled native pair leaves wrapper reusable");
                    }
                    var prepared = await grouping.PrepareDensePairAsync(execution, new(first, request), new(second, request), default);
                    check(prepared.First.Batch == first && prepared.Second.Batch == second, "native prepared windows retain separate root identities");
                    int[]? prefixes = null;
                    if (!fused)
                    {
                        prefixes = await ReadPrivatePairPrefixCounts(grouped, execution);
                        check(prefixes.All(count => count > SmallCandidateExactPolicy.MaximumCandidates),
                            "both native private first-N outputs exceed 8 before suffix continuation");
                        if (trial == 0)
                            check(PairChainExecutors(grouped, right: false).Skip(1).All(item => item is null) &&
                                PairChainExecutors(grouped, right: true).Skip(1).All(item => item is null),
                                "pair Prepare binds only N; both private suffixes remain lazy");
                    }
                    int slot = 0;
                    foreach (var (batch, handle) in new[] { (first, prepared.First), (second, prepared.Second) })
                    {
                        var input = FamilyCandidateSet.Dense(batch);
                        var window = new FamilyObservationWindow(batch, request);
                        var expected = await cpu.InvokeAsync(execution, window, input, default);
                        var independent = await single.InvokeAsync(execution, window, input, default);
                        var actual = await handle.InvokeAsync(grouped, window, input, default);
                        check(expected.Count > 8, "large 2p identity fixture has nontrivial whole-table survivors");
                        CheckPairAbi1(expected, actual, check, "native paired whole-table CPU");
                        CheckPairAbi1(independent, actual, check, "native paired independent single GPU");
                        if (!fused)
                            check(PairChainExecutors(grouped, right: slot != 0).All(item => item is not null),
                                "positive private pair member executes every original suffix stage");
                        GD.Print($"PARTY_N_DENSE_PAIR fused={fused};trial={trial};slot={slot};base={batch.BatchBase};roots={batch.BatchCandidateCount};prefix={(prefixes is null ? -1 : prefixes[slot])};survivors={actual.Count};fullAbi1CpuParity=true;singleGpuParity=true;tail=true");
                        slot++;
                    }
                }
            }
            finally
            {
                await grouped.DisposeAsync(execution);
                await single.DisposeAsync(execution);
                await cpu.DisposeAsync(execution);
            }
        }
        await CheckPrivatePartySmallPolicyUnchanged(request, execution, check);
        GD.Print("PARTY_N_DENSE_PAIR_PASS players=2;privateAndFused=true;differentBases=true;oddTails=true;reuse=true;compactThenDense=true;preCancelReuse=true;policyUnchanged=true");
    }
    private static void CheckPairAbi1(FamilyCandidateSet expected, FamilyCandidateSet actual, Action<bool, string> check, string scope)
    {
        check(expected.Batch == actual.Batch && !actual.IsDense &&
            expected.EnumerateLogicalOrdinals().SequenceEqual(actual.EnumerateLogicalOrdinals()), scope + " complete ordered ABI1 and batch identity");
    }
    private static T PairPrivateField<T>(object owner, string field) =>
        (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static FamilyPrivateGpuExecution?[] PairChainExecutors(NeowFamily.PartyInitial family, bool right) =>
        PairPrivateField<FamilyPrivateGpuExecution?[]>(PairPrivateField<FamilyPrivateGpuChain>(family,
            right ? "_privatePairChain" : "_chain"), "_executors");
    private static async Task<int[]> ReadPrivatePairPrefixCounts(NeowFamily.PartyInitial family, FamilyExecutionContext execution) =>
        await execution.ExecuteGpuAsync(rd =>
        {
            return new[] { "_chain", "_privatePairChain" }.Select(field =>
            {
                var chain = PairPrivateField<FamilyPrivateGpuChain>(family, field);
                Rid header = PairPrivateField<Rid>(chain.DensePrefixExecutor, "_header");
                ReadOnlySpan<uint> values = FamilyGpuComputeUtility.UInt32Span(rd.BufferGetData(header, 0, 32));
                if (values.Length != 8 || values[0] != 0x4e464d52 || values[1] != 1 || values[3] != 0 || values[2] > values[4])
                    throw new InvalidDataException("PartyNativePair.PrefixHeaderInvalid");
                return checked((int)values[2]);
            }).ToArray();
        }, CancellationToken.None);
    private static async Task CheckPrivatePartySmallPolicyUnchanged(ExactSearchExecutionRequest request,
        FamilyExecutionContext execution, Action<bool, string> check)
    {
        var grouped = new NeowFamily.PartyInitial(request, preferGpu: true, fuse: false);
        var cpu = new NeowFamily.PartyInitial(request, preferGpu: false, optimize: false);
        var priorPolicy = execution.SmallCandidateExact;
        var policy = new SmallCandidateExactPolicy();
        execution.SmallCandidateExact = policy;
        try
        {
            var stages = PairPrivateField<List<(ExactSearchExecutionRequest Request, IFamilyInvocation Family)>>(grouped, "_gpuStages");
            var firstPlan = ((NeowFamily)stages[0].Family).PricingReplayPlan;
            int PrefixCount(ulong first) => Enumerable.Range(0, 9).Count(offset => NeowFamilyReplay.Matches(
                Beta111Profile.Instance.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(Beta111Profile.Instance, first + (ulong)offset)), firstPlan));
            // Every candidate block below includes the request's known positive
            // ordinal 128, so at least one root needs the real private suffix.
            ulong firstBase = Enumerable.Range(120, 9).Select(value => (ulong)value)
                .First(first => PrefixCount(first) is > 0 and <= SmallCandidateExactPolicy.MaximumCandidates);
            var firstBatch = new SearchBatch(firstBase, 9);
            var secondBatch = new SearchBatch(512, 9);
            var prepared = await ((IDenseFamilyBatchGrouping)grouped).PrepareDensePairAsync(execution,
                new(firstBatch, request), new(secondBatch, request), default);
            int[] prefixes = await ReadPrivatePairPrefixCounts(grouped, execution);
            check(prefixes[0] is > 0 and <= SmallCandidateExactPolicy.MaximumCandidates &&
                PairChainExecutors(grouped, right: false).Skip(1).All(item => item is null),
                "actual private N prefix is small while untouched suffix remains unbound");
            foreach (var (batch, handle) in new[] { (firstBatch, prepared.First), (secondBatch, prepared.Second) })
            {
                var input = FamilyCandidateSet.Dense(batch);
                var expected = await cpu.InvokeAsync(execution, new(batch, request), input, default);
                var actual = await handle.InvokeAsync(grouped, new(batch, request), input, default);
                CheckPairAbi1(expected, actual, check, "small-prefix private Party policy");
                if (batch == firstBatch)
                    check(actual.EnumerateLogicalOrdinals().Contains(128UL - firstBase) &&
                        PairChainExecutors(grouped, right: false).All(item => item is not null),
                        "context policy does not enable previously disabled PartyInitial early handoff; known positive executes entire suffix");
            }
            check(policy.GetSummary().Contains("smallCandidateExactBatches=0", StringComparison.Ordinal),
                "PartyInitial private does not claim small-Exact handoffs from a context policy it never enabled");
            GD.Print($"PARTY_N_DENSE_PAIR_POLICY firstBase={firstBase};roots=9;firstPrefix={prefixes[0]};contextPolicyPresent=true;suffixExecuted=true;fullAbi1CpuParity=true");
        }
        finally
        {
            execution.SmallCandidateExact = priorPolicy;
            await grouped.DisposeAsync(execution);
            await cpu.DisposeAsync(execution);
        }
    }
}
