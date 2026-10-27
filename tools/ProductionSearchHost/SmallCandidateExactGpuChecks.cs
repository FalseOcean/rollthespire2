using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;

// Real device/buffer and production transport/lifecycle checks. The controlled
// Families below upload authored ordinals; they do not exercise numerical kernels.
internal static class SmallCandidateExactGpuChecks
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static void Run(ExactSearchExecutionRequest request)
    {
        int checks = 0;
        void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidDataException("SmallCandidateExactGpu:" + reason);
            checks++;
        }
        Exception Reject<T>(Action action, string reason) where T : Exception
        {
            try { action(); }
            catch (Exception error)
            {
                while (error is TargetInvocationException { InnerException: { } inner }) error = inner;
                if (error is T) { checks++; return error; }
                throw new InvalidDataException("SmallCandidateExactGpu:WrongRejection:" + reason, error);
            }
            throw new InvalidDataException("SmallCandidateExactGpu:MissingRejection:" + reason);
        }

        using var rd = RenderingServer.CreateLocalRenderingDevice()
            ?? throw new InvalidOperationException("SmallCandidateExactGpu.LocalDeviceUnavailable");
        var batch = new SearchBatch(7000, 16);
        var payload = new PrivateOrdinalBuffer(rd);
        object? boundaryMeasurement = null;
        try
        {
            Upload(rd, payload, [15]);
            Check(payload.ReadAbi1ForExact(batch, 1).ExportAbi1().Span.SequenceEqual(new ulong[] { 15 }), "OneOrdinal");
            Upload(rd, payload, [15, 1, 7, 0, 12, 2, 9, 3]);
            Check(payload.ReadAbi1ForExact(batch, 8).ExportAbi1().Span.SequenceEqual(new ulong[] { 0, 1, 2, 3, 7, 9, 12, 15 }), "EightSortedOrdinals");
            Upload(rd, payload, [2, 2]);
            Reject<ArgumentException>(() => payload.ReadAbi1ForExact(batch, 2), "DuplicateOrdinal");
            Upload(rd, payload, [16]);
            Reject<ArgumentOutOfRangeException>(() => payload.ReadAbi1ForExact(batch, 1), "OrdinalOutsideBatch");
            Reject<InvalidDataException>(() => payload.ReadAbi1ForExact(batch, 9), "NineOutsideSmallReadback");
            Reject<InvalidDataException>(() => payload.ReadAbi1ForExact(batch, 0), "ZeroUsesEmptyPathWithoutReadback");
            Upload(rd, payload, [4]);
            Reject<InvalidOperationException>(() => Task.Run(() => payload.ReadAbi1ForExact(batch, 1)).GetAwaiter().GetResult(), "PayloadOwnerThread");
            Check(payload.ReadAbi1ForExact(batch, 1).ExportAbi1().Span.SequenceEqual(new ulong[] { 4 }), "PayloadReusableAfterRejection");
            boundaryMeasurement = MeasureReadbackBoundary(rd, payload, batch);
        }
        finally { payload.Dispose(); }
        Reject<ObjectDisposedException>(() => payload.CheckCount(0), "PayloadDisposed");

        var gpuType = typeof(AutomaticPrivateSerialInvocation).GetNestedType("Gpu", BindingFlags.NonPublic)!;
        var execute = gpuType.GetMethod("Execute", Members)!;
        var buffersField = gpuType.GetField("_buffers", Members)!;
        var executorsField = gpuType.GetField("_executors", Members)!;
        IDisposable NewSerial(FixtureFamily prefix, FixtureFamily tail) =>
            (IDisposable)Activator.CreateInstance(gpuType, Members, null,
                [rd, new IFamilyInvocation[] { prefix, tail }, null], null)!;
        FamilyCandidateSet Execute(IDisposable serial, SearchBatch inputBatch, SmallCandidateExactPolicy? policy,
            CancellationToken token = default) =>
            ((ValueTuple<FamilyCandidateSet, double, FamilyCandidateSet[]>)execute.Invoke(serial,
                [FamilyCandidateSet.Dense(inputBatch), "PT", false, policy, token, null])!).Item1;
        bool Uninitialized(IDisposable serial) =>
            ((Array)buffersField.GetValue(serial)!).Cast<object?>().All(item => item is null) &&
            ((Array)executorsField.GetValue(serial)!).Cast<object?>().All(item => item is null);
        PrivateOrdinalBuffer[] Buffers(IDisposable serial) =>
            ((Array)buffersField.GetValue(serial)!).Cast<PrivateOrdinalBuffer?>().OfType<PrivateOrdinalBuffer>().ToArray();
        void CheckOutput(FamilyCandidateSet output, SearchBatch expectedBatch, int survivors)
        {
            Check(output.Batch == expectedBatch && !output.IsDense && output.Count == survivors &&
                output.ExportAbi1().Span.SequenceEqual(Expected(survivors)), "CanonicalBatchLocalOutput:" + survivors);
        }

        var control = new FixtureControl();
        var prefix = new FixtureFamily("P.Prefix", true, control);
        var tail = new FixtureFamily("T.Tail", false, control);
        var serial = NewSerial(prefix, tail);
        var policy = new SmallCandidateExactPolicy();
        var captured = new Dictionary<int, ulong[]>();
        PrivateOrdinalBuffer[] actualBuffers = [];
        try
        {
            Check(Uninitialized(serial) && prefix.BindCount == 0 && tail.BindCount == 0, "ConstructorIsLazy");
            Check(Execute(serial, new SearchBatch(5000, 0), policy).Count == 0 && Uninitialized(serial), "EmptyInputCreatesNothing");
            foreach (int survivors in new[] { 0, 1, 8 })
            {
                control.Survivors = survivors;
                int before = prefix.ExecuteCount;
                var sample = new SearchBatch((ulong)(8000 + survivors * 16), 16);
                var output = Execute(serial, sample, policy);
                CheckOutput(output, sample, survivors);
                captured[survivors] = output.ExportAbi1().ToArray();
                Check(prefix.ExecuteCount == before + 1 && prefix.BindCount == 1 && tail.BindCount == 0 && tail.ExecuteCount == 0,
                    "SmallPopulationDoesNotBindTail:" + survivors);
            }
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                int before = prefix.ExecuteCount;
                Reject<OperationCanceledException>(() => Execute(serial, batch, policy, canceled.Token), "CanceledBeforeDispatch");
                Check(prefix.ExecuteCount == before && tail.BindCount == 0, "CanceledBeforeDispatchHasNoWork");
            }
            using (var canceled = new CancellationTokenSource())
            {
                control.Survivors = 1;
                control.CancelAfterPrefix = canceled;
                int before = prefix.ExecuteCount;
                Reject<OperationCanceledException>(() => Execute(serial, batch, policy, canceled.Token), "CanceledAfterCountReady");
                Check(prefix.ExecuteCount == before + 1 && tail.BindCount == 0, "CanceledTailNotCreated");
            }
            CheckOutput(Execute(serial, batch, policy), batch, 1);
            control.Survivors = 9;
            int prefixBeforeNine = prefix.ExecuteCount;
            CheckOutput(Execute(serial, batch, policy), batch, 9);
            Check(prefix.ExecuteCount == prefixBeforeNine + 1 && prefix.BindCount == 1 && tail.BindCount == 1 && tail.ExecuteCount == 1,
                "NineReachesTerminalOnce");

            control.InvalidPrivateOutput = true;
            Reject<InvalidDataException>(() => Execute(serial, batch, policy), "CountReadyCannotClaimPublicOutput");
            control.InvalidPrivateOutput = false;
            control.WrongTerminalBatch = true;
            Reject<InvalidDataException>(() => Execute(serial, batch, policy), "TerminalBatchIdentity");
            control.WrongTerminalBatch = false;
            CheckOutput(Execute(serial, batch, policy), batch, 9);
            Check(policy.GetSummary().Contains("smallCandidateExactBatches=3;", StringComparison.Ordinal) &&
                policy.GetSummary().Contains("\"unexecutedTail\":\"T.Tail\"", StringComparison.Ordinal), "SummaryRecordsOnlyCompletedHandoffs");
            actualBuffers = Buffers(serial);
            Check(actualBuffers.Length == 1 && prefix.Owners.Count == 1 && tail.Owners.Count == 1, "OnlyReachedResourcesCreated");
        }
        finally { serial.Dispose(); }
        serial.Dispose();
        Check(prefix.Owners.Concat(tail.Owners).All(owner => owner.ReleaseCalls == 1 && owner.Released && owner.BorrowedBuffersAlive),
            "BorrowersReleasedOnceBeforeBuffers");
        foreach (var actual in actualBuffers) Reject<ObjectDisposedException>(() => actual.CheckCount(0), "ReachedBufferDisposed");
        Reject<ObjectDisposedException>(() => Execute(serial, batch, policy), "DisposedSerial");

        // A second allocation provides the explicit null-policy control. Its
        // stages are reused across populations; no per-case device is created.
        var ordinaryControl = new FixtureControl();
        var ordinaryPrefix = new FixtureFamily("P.Prefix", true, ordinaryControl);
        var ordinaryTail = new FixtureFamily("T.Tail", false, ordinaryControl);
        var ordinary = NewSerial(ordinaryPrefix, ordinaryTail);
        PrivateOrdinalBuffer[] ordinaryBuffers = [];
        try
        {
            foreach (int survivors in new[] { 1, 8, 9, 0 })
            {
                ordinaryControl.Survivors = survivors;
                int before = ordinaryPrefix.ExecuteCount;
                int tailBefore = ordinaryTail.ExecuteCount;
                var output = Execute(ordinary, batch, null);
                CheckOutput(output, batch, survivors);
                Check(ordinaryPrefix.ExecuteCount == before + 1 && ordinaryPrefix.BindCount == 1 && ordinaryTail.BindCount == 1 &&
                    ordinaryTail.ExecuteCount == tailBefore + (survivors == 0 ? 0 : 1), "NullPolicyPreservesTail:" + survivors);
                if (captured.TryGetValue(survivors, out var expected))
                    Check(output.ExportAbi1().Span.SequenceEqual(expected), "PolicyAndControlSameOrdinals:" + survivors);
            }
            ordinaryBuffers = Buffers(ordinary);
            ordinaryTail.Owners.Single().ThrowAfterRelease = true;
            var cleanup = Reject<AggregateException>(() => ordinary.Dispose(), "CleanupFailureRetained");
            Check(cleanup.ToString().Contains("InjectedSmallExactCleanupFailure", StringComparison.Ordinal), "CleanupOriginalCause");
        }
        finally { ordinary.Dispose(); }
        Check(ordinaryPrefix.Owners.Concat(ordinaryTail.Owners).All(owner => owner.ReleaseCalls == 1 && owner.Released && owner.BorrowedBuffersAlive),
            "CleanupContinuesAfterBorrowerFailure");
        foreach (var actual in ordinaryBuffers) Reject<ObjectDisposedException>(() => actual.CheckCount(0), "ControlBufferDisposedAfterCleanupFailure");

        // The whole-party wrapper uses the generic production private chain.
        // Reuse authored ordinals to isolate transport, lazy binding and ownership.
        var partyControl = new FixtureControl { AcceptCompactPrefix = true };
        var partyPrefix = new FixtureFamily("P.Prefix", true, partyControl);
        var partyTail = new FixtureFamily("T.Tail", false, partyControl);
        var partyChain = new FamilyPrivateGpuChain(rd, [partyPrefix, partyTail]);
        var partyPolicy = new SmallCandidateExactPolicy();
        var chainType = typeof(FamilyPrivateGpuChain);
        bool PartyUninitialized() => ((Array)chainType.GetField("_executors", Members)!.GetValue(partyChain)!).Cast<object?>().All(value => value is null)
            && ((Array)chainType.GetField("_buffers", Members)!.GetValue(partyChain)!).Cast<object?>().All(value => value is null);
        PrivateOrdinalBuffer[] partyBuffers = [];
        try
        {
            Check(PartyUninitialized() && partyPrefix.BindCount == 0 && partyTail.BindCount == 0, "PartyConstructorIsLazy");
            Check(partyChain.Execute(FamilyCandidateSet.Dense(new(0, 0)), default, partyPolicy).Count == 0 && PartyUninitialized(),
                "PartyEmptyInputCreatesNothing");
            foreach (int survivors in new[] { 0, 1, 8, 9 })
            {
                partyControl.Survivors = survivors;
                int tailBefore = partyTail.ExecuteCount;
                CheckOutput(partyChain.Execute(FamilyCandidateSet.Dense(batch), default, partyPolicy), batch, survivors);
                Check(partyPrefix.BindCount == 1 && partyTail.ExecuteCount == tailBefore + (survivors == 9 ? 1 : 0),
                    "Party0/1/8StopAnd9ReachesTail:" + survivors);
            }
            partyControl.Survivors = 1;
            int ordinaryTailBefore = partyTail.ExecuteCount;
            CheckOutput(partyChain.Execute(FamilyCandidateSet.Dense(batch), default), batch, 1);
            Check(partyTail.ExecuteCount == ordinaryTailBefore + 1, "PartyNullPolicyKeepsSelectedTail");
            partyControl.Survivors = 9;
            partyControl.CompactExpectedInput = Expected(9);
            var compactInput = FamilyCandidateSet.FromSortedAbi1(batch, partyControl.CompactExpectedInput);
            var compactOutput = partyChain.Execute(compactInput, default, partyPolicy);
            Check(compactOutput.ExportAbi1().Span.SequenceEqual(partyControl.CompactExpectedInput) && partyControl.UploadedInputObserved,
                "PartyCompactInputUploadsOriginalBatchLocalOrdinals");
            Check(partyPrefix.BindCount == 2 && partyTail.BindCount == 1, "PartyDenseAndCompactEntryBindOnlyWhenReached");
            int prefixBeforeSmallInput = partyPrefix.ExecuteCount;
            var smallInput = FamilyCandidateSet.FromSortedAbi1(batch, Expected(8));
            Check(partyChain.Execute(smallInput, default, partyPolicy).ExportAbi1().Span.SequenceEqual(smallInput.ExportAbi1().Span)
                && partyPrefix.ExecuteCount == prefixBeforeSmallInput, "PartySmallCompactInputReturnsBeforeAnyChild");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                Reject<OperationCanceledException>(() => partyChain.Execute(compactInput, canceled.Token, partyPolicy), "PartyCanceledBeforeDispatch");
                Check(partyPrefix.ExecuteCount == prefixBeforeSmallInput, "PartyCanceledInputHasNoDispatch");
            }
            using (var canceled = new CancellationTokenSource())
            {
                partyControl.Survivors = 1; partyControl.CancelAfterPrefix = canceled;
                int before = partyTail.ExecuteCount;
                Reject<OperationCanceledException>(() => partyChain.Execute(FamilyCandidateSet.Dense(batch), canceled.Token, partyPolicy), "PartyCanceledAfterCountReady");
                Check(partyTail.ExecuteCount == before, "PartyCanceledTailHasNoDispatch");
            }
            CheckOutput(partyChain.Execute(FamilyCandidateSet.Dense(batch), default, partyPolicy), batch, 1);
            partyBuffers = ((Array)chainType.GetField("_buffers", Members)!.GetValue(partyChain)!).Cast<PrivateOrdinalBuffer?>()
                .Append((PrivateOrdinalBuffer?)chainType.GetField("_incoming", Members)!.GetValue(partyChain)).OfType<PrivateOrdinalBuffer>().ToArray();
        }
        finally { partyChain.Dispose(); }
        partyChain.Dispose();
        Check(partyPrefix.Owners.Concat(partyTail.Owners).All(owner => owner.ReleaseCalls == 1 && owner.Released && owner.BorrowedBuffersAlive),
            "PartyBorrowersReleasedOnceBeforeBuffers");
        foreach (var actual in partyBuffers) Reject<ObjectDisposedException>(() => actual.CheckCount(0), "PartyReachedBufferDisposed");
        Reject<ObjectDisposedException>(() => partyChain.Execute(FamilyCandidateSet.Dense(batch), default, partyPolicy), "PartyDisposedChain");

        Console.WriteLine("smallCandidateExactBoundaryJson=" + JsonSerializer.Serialize(boundaryMeasurement));
        Console.WriteLine($"smallCandidateExactGpu=PASS;checks={checks};populations=0,1,8,9;" +
            "realDeviceAndBuffers=true;productionTransport=true;wholePartyTransport=true;compactUpload=true;lazyTail=true;cancelReuse=true;cleanupFailureRetained=true;" +
            "kernelParity=false;fixture=AuthoredOrdinals;request=" + request.SnapshotFingerprint);
    }

    private static object MeasureReadbackBoundary(RenderingDevice rd, PrivateOrdinalBuffer payload, SearchBatch batch)
    {
        const int warmups = 2, rounds = 32;
        var populations = new List<object>();
        foreach (uint[] authored in new uint[][] { [15], [15, 1, 7, 0, 12, 2, 9, 3] })
        {
            Upload(rd, payload, authored);
            ulong[] expected = authored.Select(ordinal => (ulong)ordinal).Order().ToArray();
            int count = authored.Length;
            double Read(bool small)
            {
                long started = Stopwatch.GetTimestamp();
                FamilyCandidateSet result = small ? payload.ReadAbi1ForExact(batch, count) : payload.ReadForVerification(batch, count);
                double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                // Both sides include production decode/sort/ABI validation. This
                // independent full-population assertion is outside the timer.
                if (result.Batch != batch || result.IsDense || result.Count != count ||
                    !result.ExportAbi1().Span.SequenceEqual(expected))
                    throw new InvalidDataException($"SmallCandidateExactGpu.BoundaryPopulationMismatch:small={small};count={count}");
                return milliseconds;
            }
            for (int warmup = 0; warmup < warmups; warmup++) { Read(true); Read(false); }
            var raw = new BoundaryRound[rounds];
            var smallTimes = new double[rounds * 2];
            var directTimes = new double[rounds * 2];
            for (int round = 0; round < rounds; round++)
            {
                // ABBA balances adjacent driver/host drift without changing the
                // resident source population or running any numerical kernel.
                double a1 = Read(true), b1 = Read(false), b2 = Read(false), a2 = Read(true);
                smallTimes[round * 2] = a1; smallTimes[round * 2 + 1] = a2;
                directTimes[round * 2] = b1; directTimes[round * 2 + 1] = b2;
                raw[round] = new(round, a1, b1, b2, a2);
            }
            double Median(double[] values)
            {
                double[] ordered = values.Order().ToArray();
                return (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;
            }
            populations.Add(new
            {
                Count = count, BatchBase = batch.BatchBase, BatchPopulation = batch.BatchCandidateCount,
                ExpectedAbi1 = expected, RequestedBytes = count * sizeof(uint),
                SamplesPerMethod = smallTimes.Length, FullPopulationAssertions = warmups * 2 + rounds * 4,
                SmallBankSumMs = smallTimes.Sum(), SmallBankMedianMs = Median(smallTimes),
                DirectSumMs = directTimes.Sum(), DirectMedianMs = Median(directTimes), RawAbbaRounds = raw
            });
        }
        return new
        {
            Schema = "SmallCandidateExactBoundary.Abba.v1", Clock = "StopwatchHost", Device = rd.GetDeviceName(),
            Scope = "ActualExactHandoffTransportBoundaryOnly;FixtureLocalRenderingDevice",
            A = "PrivateOrdinalBuffer.ReadAbi1ForExact;32ByteBank;Copy;QueuedDownload;Submit;Sync;DecodeSortAbi1",
            B = "PrivateOrdinalBuffer.ReadForVerification;DirectRead;SameDecodeSortAbi1",
            SourceCapacityBytes = PrivateOrdinalBuffer.Capacity * sizeof(uint), SmallBankCapacityBytes = 8 * sizeof(uint),
            WarmupsPerMethod = warmups, MeasuredAbbaCycles = rounds, DetailLogging = RolltheSpire2.Bootstrap.RuntimeLog.DetailEnabled,
            UploadAndIndependentAssertionOutsideTimer = true, NumericalKernel = false, Calibration = false,
            ProductionSearchE2E = false, GpuKernelTimestamp = false,
            Interpretation = "SmallerBufferDoesNotImplyFaster;CompareBothCompletedBoundaryCalls;NotProductionMemoryPeak",
            Populations = populations
        };
    }

    private sealed record BoundaryRound(int Round, double AFirstMs, double BFirstMs, double BSecondMs, double ASecondMs);

    private static ulong[] Expected(int survivors) =>
        Enumerable.Range(16 - survivors, survivors).Select(value => (ulong)value).ToArray();

    private static void Upload(RenderingDevice device, PrivateOrdinalBuffer buffer, uint[] ordinals)
    {
        buffer.CheckCount(ordinals.Length);
        if (ordinals.Length == 0) return;
        var bytes = new byte[ordinals.Length * sizeof(uint)];
        Buffer.BlockCopy(ordinals, 0, bytes, 0, bytes.Length);
        if (device.BufferUpdate(buffer.Buffer, 0, (uint)bytes.Length, bytes) != Error.Ok)
            throw new InvalidOperationException("SmallCandidateExactGpu.FixtureUploadFailed");
    }

    private sealed class FixtureControl
    {
        internal int Survivors;
        internal CancellationTokenSource? CancelAfterPrefix;
        internal bool InvalidPrivateOutput;
        internal bool WrongTerminalBatch;
        internal bool AcceptCompactPrefix, UploadedInputObserved;
        internal ulong[]? CompactExpectedInput;
    }

    private sealed class FixtureFamily(string id, bool prefix, FixtureControl control) : IFamilyInvocation
    {
        internal int BindCount, ExecuteCount;
        internal readonly List<FixtureOwner> Owners = [];
        public string FamilyId => id;
        public FamilyAnalyticalCostProjection AnalyticalCost => new(id, 16, [], "AuthoredOrdinalFixture");
        public FamilySurvivalProjection Survival => FamilySurvivalProjection.Unresolved(id, "NotNumericalParity");
        public FamilyConditionPerformanceProjection ConditionPerformance => new(id,
            "SmallCandidateExact.ControlledGpuBuffer.v1", "AuthoredOrdinals", 1, "TransportAndLifecycleOnly");
        bool IFamilyInvocation.CanBindPrivateSerial => true;
        GpuNumericRequirements IFamilyInvocation.GpuNumericRequirements => GpuNumericRequirements.None;
        FamilyPrivateGpuExecution IFamilyInvocation.BindPrivateSerial(RenderingDevice device,
            PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
        {
            if (prefix ? input is not null && !control.AcceptCompactPrefix || output is null : input is null || output is not null)
                throw new InvalidDataException("SmallCandidateExactGpu.FixturePortShape");
            var owner = new FixtureOwner(device, input, output);
            Owners.Add(owner); BindCount++;
            return new FamilyPrivateGpuExecution(owner, device.GetDeviceName(), 0, input, output, (batch, count, token) =>
            {
                token.ThrowIfCancellationRequested();
                ExecuteCount++;
                if (prefix)
                {
                    ulong[] authored;
                    if (input is null)
                    {
                        if (count != 16) throw new InvalidDataException("SmallCandidateExactGpu.FixtureDenseCount");
                        authored = Expected(control.Survivors);
                    }
                    else
                    {
                        var incoming = FamilyGpuComputeUtility.FromUInt32Bytes(device.BufferGetData(input.Buffer, 0, checked((uint)count * sizeof(uint))));
                        Array.Sort(incoming);
                        if (!incoming.Select(value => (ulong)value).SequenceEqual(control.CompactExpectedInput ?? []))
                            throw new InvalidDataException("SmallCandidateExactGpu.FixtureCompactUploadMismatch");
                        control.UploadedInputObserved = true;
                        authored = incoming.TakeLast(control.Survivors).Select(value => (ulong)value).ToArray();
                    }
                    Upload(device, output!, authored.AsEnumerable().Reverse().Select(value => (uint)value).ToArray());
                    var cancellation = control.CancelAfterPrefix;
                    control.CancelAfterPrefix = null;
                    cancellation?.Cancel();
                    return new(control.Survivors, control.InvalidPrivateOutput ? FamilyCandidateSet.FromAbi1(batch, authored) : null,
                        0, 0, 0, 0, 0);
                }
                var raw = FamilyGpuComputeUtility.FromUInt32Bytes(device.BufferGetData(input!.Buffer, 0, checked((uint)count * sizeof(uint))));
                if (raw.Length != count) throw new InvalidDataException("SmallCandidateExactGpu.FixtureTerminalReadbackLength");
                Array.Sort(raw);
                var resultBatch = control.WrongTerminalBatch ? new SearchBatch(batch.BatchBase + 1, batch.BatchCandidateCount) : batch;
                var result = FamilyCandidateSet.FromAbi1(resultBatch, raw.Select(value => (ulong)value).ToArray());
                return new(count, result, 0, 0, count * sizeof(uint), 0, 0);
            });
        }
        public ValueTask<FamilyCandidateSet> InvokeAsync(FamilyExecutionContext context, FamilyObservationWindow window,
            FamilyCandidateSet input, CancellationToken token) => throw new NotSupportedException("SmallCandidateExactGpu.PrivateFixtureOnly");
        public FamilyPerformanceObservation CapturePerformanceObservation() => new(ConditionPerformance, "Fixture", 0, 0, 0,
            ExecuteCount, 0, 0, 0, 0, false, "TransportAndLifecycleOnly");
    }

    private sealed class FixtureOwner : IDisposable
    {
        private readonly RenderingDevice _device;
        private readonly PrivateOrdinalBuffer? _input, _output;
        private readonly Rid _scratch;
        private bool _disposed;
        internal int ReleaseCalls;
        internal bool Released, BorrowedBuffersAlive, ThrowAfterRelease;
        internal FixtureOwner(RenderingDevice device, PrivateOrdinalBuffer? input, PrivateOrdinalBuffer? output)
        {
            _device = device; _input = input; _output = output;
            _scratch = FamilyGpuComputeUtility.AllocateStorageBuffer(device, sizeof(uint), "SmallExactFixture.Owner");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; ReleaseCalls++;
            _input?.CheckCount(0); _output?.CheckCount(0);
            BorrowedBuffersAlive = true;
            _device.FreeRid(_scratch); Released = true;
            if (ThrowAfterRelease) throw new InvalidOperationException("InjectedSmallExactCleanupFailure");
        }
    }
}
