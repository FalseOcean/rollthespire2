using System.Diagnostics;
using System.Security.Cryptography;
using Godot;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;

internal static partial class PartyPhase1Checks
{
    // Standalone ordered Kaleidoscope check; full-body CPU population and Exact witnesses.
    private static void CheckNeowOrderedKKernelOptimizations(string contextsRoot, string output, int capacity = 32771)
    {
        if (capacity is < 64 or > NeowFamilyGpuExecutor.Capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        Directory.CreateDirectory(output);
        using var rd = RenderingServer.CreateLocalRenderingDevice() ?? throw new InvalidOperationException("MPN3.DeviceUnavailable");
        using var modules = new FamilyGpuComputeUtility.ShaderReuse(rd);
        using var reuse = modules.Activate();
        var receipts = new List<object>();
        var kalei = BaseGameModelKeys.Relics.Kaleidoscope;
        var arcane = BaseGameModelKeys.Relics.ArcaneScroll;
        var leaf = BaseGameModelKeys.Relics.NewLeaf;
        var bones = BaseGameModelKeys.Relics.NeowsBones;
        foreach (int players in new[] { 2, 3, 4 })
        {
            var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(Path.Combine(contextsRoot, players + "p", "context.json"));
            var context = captured.Context;
            if (context.Party?.Players.Count != players) throw new InvalidDataException("MPN3.CapturedRoster");
            ProductionSearchReplay.Write(Path.Combine(output, players + "p-context.json"), captured);
            for (int slot = 0; slot < players; slot++)
            foreach (string shape in players == 2 ? (slot == 0 ? new[] { "Direct", "WildcardFirst" } : new[] { "Direct", "LaterArrival", "ReverseBones" }) : new[] { "Direct" })
            {
                bool reverse = shape == "ReverseBones", arrival = shape == "LaterArrival", wildcard = shape == "WildcardFirst";
                string name = $"MPN3-{players}p-slot{slot}-{shape}";
                NeowStructuredEffectSearchCondition? prefixObservation = null;
                ExactSearchExecutionRequest Build(ModelKey? first, ModelKey? second)
                {
                    var local = SearchQuery.Empty with { OpeningRoute = new(reverse ? bones : kalei),
                        OpeningRouteRelicRequirement = reverse ? new(bones, [arcane, kalei], BonesRouteOrderMode.AnyOrder) : null,
                        StructuredOpeningEffects = second is { } secondKey ? [new(kalei, NeowStructuredConditionKind.IndependentOfferGroupTargets,
                            NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
                            first is { } firstKey ? [firstKey, secondKey] : [secondKey], AllowDuplicateOutputs: true,
                            KaleidoscopeGroupOrder: KaleidoscopeGroupOrderMode.ExactOrder) { KaleidoscopePositionalSlots = [first, secondKey] }] : [] };
                    var query = SearchQuery.Empty with { Players = Enumerable.Range(0, players).Select(i => new PlayerOfferQuery(i, ModelKeySetFilter.Empty)
                        { Conditions = i == slot ? local : arrival && i == 0 ? SearchQuery.Empty with { OpeningRoute = new(leaf),
                            StructuredOpeningEffects = prefixObservation is { } observedPrefix ? [observedPrefix] : [] } : SearchQuery.Empty }).ToArray() };
                    return ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new("000000000000", capacity, 30, 4)).Plan
                        ?? throw new InvalidOperationException("MPN3.QueryCompile:" + name);
                }
                NeowReplayPlan Personal(ExactSearchExecutionRequest r) => NeowReplayPlan.Compile(
                    ExactSearchExecutionRequestFactory.ForPlayer(r, r.CompiledSearch.PlayerSearches[slot]), false);
                var preliminary = Personal(Build(null, null));
                ulong positive = 0; ModelKey? firstTarget = null, secondTarget = null;
                for (; positive < 262144; positive++)
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(positive));
                    var observed = NeowPartyReplay.Observe(root, preliminary);
                    if (!observed.IdentityPass || arrival && !RolltheSpire2.Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(
                        root, context.Party.Players[0], Beta111Profile.Instance, true).RelicKeys.Contains(leaf)) continue;
                    bool reverseArcaneFirst = reverse && observed.BonesFirst == Beta110FastRelicCatalog.Kaleidoscope;
                    var wanted = MpN3Offers(root, preliminary, reverseArcaneFirst, arrival ? 1 : 0);
                    var rejected = arrival || reverse ? MpN3Offers(root, preliminary, reverse && !reverseArcaneFirst, 0) : [];
                    ushort firstId = wanted.Take(3).FirstOrDefault(id => !rejected.Take(3).Contains(id), Beta110FastDenseId.Invalid);
                    if (firstId == Beta110FastDenseId.Invalid) continue;
                    firstTarget = wildcard ? null : preliminary.Authority.EffectCatalog.DenseKeys[firstId];
                    secondTarget = preliminary.Authority.EffectCatalog.DenseKeys[wanted[3]];
                    break;
                }
                if (secondTarget is null) throw new InvalidDataException("MPN3.NoBoundedPositive:" + name);
                if (arrival)
                {
                    var originalRequest = Build(firstTarget, secondTarget);
                    var originalExact = ProductionExactSearchEvaluator.Evaluate(originalRequest, TrustedRootHashInput.FromCanonicalSeed(
                        Beta111Profile.Instance, Beta110SeedCodec.FormatOrdinal(positive)));
                    string originalDetail = MpNExactFailureDetails(name + "-RouteOnlyPrefix", originalRequest, originalExact, output,
                        Beta110SeedCodec.FormatOrdinal(positive));
                    if (originalExact.Evaluation.Disposition != SearchDisposition.Unknown ||
                        originalExact.Document?.Sections.SelectMany(s => s.NeowChoices).Any(c =>
                            c.EffectEvidenceCode.Value == "party.earlier-player-shared-opening-unresolved") != true)
                        throw new InvalidDataException("MPN3.OriginalPrefixUnknownPremise:" + originalDetail);
                    GD.Print($"MP_N3_ORIGINAL_PREFIX_UNKNOWN name={name};positive={positive};originalQueryRetained=true;matchClaimed=false");
                    // A route-only predecessor currently has no requested effect
                    // observation in Exact. Author the actual transform here so the
                    // whole-table positive has a proven shared continuation.
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(positive));
                    var prefixRng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, 0);
                    var projection = new NeowEffectProjectionEngine(Beta111Profile.Instance, "mpn3-prefix", root, context.Party.Players[0]).Project(leaf, prefixRng);
                    var transform = projection.EffectGroups.SelectMany(g => g.OrderedItems).Single(e => e.Kind == PredictedEffectKind.TransformCard);
                    if (prefixRng.Niche.CallCount != 1 || transform.TargetKey is not { } target)
                        throw new InvalidDataException("MPN3.PrefixObservationPremise");
                    prefixObservation = new(leaf, NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.TransformResults,
                        NeowStructuredOutputKind.Card, [target]);
                }
                var request = Build(firstTarget, secondTarget);
                var plan = Personal(request);
                if (arrival)
                {
                    // Test a conservative union containing an earlier failing arrival.
                    // The real authored prefix is P1 NewLeaf (one Niche draw); Exact
                    // below verifies that actual whole-table witness separately.
                    var root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(positive));
                    if (NeowPartyReplay.Matches(root, plan) || !NeowPartyReplay.Matches(root, plan with { SharedNicheDraws = 1 }))
                        throw new InvalidDataException("MPN3.ArrivalPremise");
                    plan = plan with { SharedArrivals = [(0, 0), (1, 0)] };
                }
                if (reverse)
                {
                    var root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(positive));
                    var observed = NeowPartyReplay.Observe(root, plan);
                    if (NeowPartyReplay.Route(root, plan, observed.BonesFirst, observed.BonesSecond, observed.RewardsAfterBones) ||
                        !NeowPartyReplay.Route(root, plan, observed.BonesSecond, observed.BonesFirst, observed.RewardsAfterBones))
                        throw new InvalidDataException("MPN3.ReversePremise");
                }
                var exact = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance,
                    Beta110SeedCodec.FormatOrdinal(positive)));
                if (!exact.IsMatch) throw new InvalidDataException("MPN3.WholeTablePositive:" + MpNExactFailureDetails(name, request, exact, output,
                    Beta110SeedCodec.FormatOrdinal(positive)));
                ProductionSearchReplay.Write(Path.Combine(output, name + ".json"), new ProductionSearchReplayWorkload(
                    ProductionSearchReplay.SchemaVersion, request.CompiledSearch.Query,
                    request.RunOptions with { StartSeed = Beta110SeedCodec.FormatOrdinal(positive), ScanCount = 65536 }));
                MpN3CheckBody(rd, plan, positive, name, capacity, receipts, output, conservativeArrivalBoundary: arrival);
                if (players == 2 && slot == 0 && shape == "Direct")
                {
                    var catalog = plan.Authority.EffectCatalog;
                    foreach (var invalid in new[] { catalog with { OtherCharacterCardAuthorityExact = false },
                        catalog with { OtherCharacterPools = catalog.OtherCharacterPools.Take(2).ToArray() },
                        catalog with { OtherCharacterPools = catalog.OtherCharacterPools.Select((p, i) => i == 0 ? new Beta110FastCardPool([], [], []) : p).ToArray() } })
                        if (NeowFamilyGpuPlan.TryCreate(plan with { Authority = plan.Authority with { EffectCatalog = invalid } }, out _, out _))
                            throw new InvalidDataException("MPN3.InvalidPoolAdmitted");
                }
                GD.Print($"MP_N3_KERNEL_PASS name={name};positive={positive};wholeTableExact=true;DenseCompact=true;firstWildcard={wildcard};laterArrival={arrival};arrivalUnionBoundary={arrival};reverseOnly={reverse}");
            }
        }
        ProductionSearchReplay.Write(Path.Combine(output, "mp-n3-kernel-results.json"), receipts);
        GD.Print($"MP_N3_KERNEL_SUMMARY rows={receipts.Count};output={output};calibration=false");
    }

    private static string MpNExactFailureDetails(string label, ExactSearchExecutionRequest request, ProductionExactSearchResult exact,
        string? output = null, string? canonicalSeed = null)
    {
        string seed = canonicalSeed ?? exact.Document?.CanonicalSeed ?? request.CanonicalStartSeed;
        string detail = $"{label}:seed={seed}:disposition={exact.Evaluation.Disposition}:failure={exact.FailureCode}:childSlot={exact.Document?.Context.PlayerSlotIndex}:routes=" +
            System.Text.Json.JsonSerializer.Serialize(exact.Evaluation.RouteRejections, ProductionSearchReplay.JsonOptions);
        GD.Print("PARTY_N_EXACT_REJECT " + detail);
        try
        {
            if (output is null)
            {
                var args = OS.GetCmdlineUserArgs(); int index = Array.IndexOf(args, "--output");
                if (index >= 0 && index + 1 < args.Length) output = Path.GetFullPath(args[index + 1]);
            }
            if (output is not null)
            {
                string path = Path.Combine(output, label + "-exact-failure.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { label, seed,
                    binaryMvid = typeof(Beta111Profile).Assembly.ManifestModule.ModuleVersionId,
                    originalOptions = request.RunOptions, exact.FailureCode, exact.Evaluation, exact.Document }, ProductionSearchReplay.JsonOptions));
                ProductionSearchReplay.Write(Path.Combine(output, label + "-failed-workload.json"), new ProductionSearchReplayWorkload(
                    ProductionSearchReplay.SchemaVersion, request.CompiledSearch.Query,
                    request.RunOptions with { StartSeed = seed, ScanCount = 1, TargetMatchCount = 1 }));
                ProductionSearchReplay.Write(Path.Combine(output, label + "-failed-context.json"), new ProductionSearchReplayContext(
                    ProductionSearchReplay.SchemaVersion, "N host Exact failure; original query and authority retained", request.CompiledSearch.Context));
                detail += ":diagnostic=" + path;
            }
        }
        catch (Exception ex) { detail += ":diagnosticExportFailed=" + ex.GetType().Name + ":" + ex.Message; }
        return detail;
    }

    private static ushort[] MpN3Offers(ulong root, NeowReplayPlan plan, bool arcaneFirst, int nicheDraws)
    {
        var rewards = NeowPartyReplay.Observe(root, plan).RewardsAfterBones;
        var niche = new Beta110FastRng(unchecked(root + XxHash64.Hash("niche"u8, 0)));
        for (int i = 0; i < nicheDraws; i++) niche.NextInt(2);
        if (arcaneFirst && NeowLocalOperators.SelectUnused(plan.Authority.EffectCatalog.CharacterRewardPool.Rare, [], ref rewards) == Beta110FastDenseId.Invalid)
            throw new InvalidDataException("MPN3.ArcanePool");
        ushort[] offers = new ushort[6];
        if (!NeowLocalOperators.GenerateKaleidoscopeOffers(plan.Authority.Ascension, plan.Authority.EffectCatalog, ref rewards, ref niche, offers))
            throw new InvalidDataException("MPN3.OfferAuthority");
        return offers;
    }

    private static void MpN3CheckBody(RenderingDevice rd, NeowReplayPlan plan, ulong positive, string name, int capacity, List<object> receipts,
        string output,
        bool conservativeArrivalBoundary = false)
    {
        if (plan.StructuredConditions.Length != 1 || !plan.StructuredConditions[0].OrderedKaleidoscope ||
            !NeowFamilyGpuPlan.TryCreate(plan, out var packed, out string issue) || packed is null)
            throw new InvalidDataException("MPN3.OrderedPacking:" + name);
        string source = NeowFamilyGpuExecutor.ShaderSource(authoredUpgrades: packed.HasAuthoredUpgrades, localResults: packed.HasLocalResults, multiplayer: true);
        if (!NeowFamilyGpuPlan.TryCreateIntegerUnitFloat(plan, out var compatiblePacked, out _) || compatiblePacked is null)
            throw new InvalidDataException("MPN3.CompatibilityPacking:" + name);
        string compatibleSource = MpNIntegerUnitFloatSource(compatiblePacked);
        File.WriteAllText(Path.Combine(output, name + "-integer-unit-float.glsl"), compatibleSource);
        using var disabled = new MpNKernel(rd, packed, source.Replace("#version 450", "#version 450\n#define RT2_MP_N_ORDERED_K_FIRST_REJECT 0", StringComparison.Ordinal), capacity);
        using var enabled = new MpNKernel(rd, packed, source.Replace("#version 450", "#version 450\n#define RT2_MP_N_ORDERED_K_FIRST_REJECT 1", StringComparison.Ordinal), capacity);
        using var compatible = new MpNKernel(rd, compatiblePacked, compatibleSource, capacity);
        var kernels = new[] { disabled, enabled, compatible };
        ulong start = positive / (ulong)capacity * (ulong)capacity;
        var batch = new SearchBatch(start, capacity);
        foreach (bool compact in new[] { false, true })
        {
            uint[] input = Enumerable.Range(0, capacity).Where(i => !compact || i % 3 == 0 || (ulong)i == positive - start).Select(i => (uint)i).ToArray();
            uint[] expected = input.Where(i => NeowPartyReplay.Matches(Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i)), plan)).ToArray();
            if (!expected.Contains((uint)(positive - start))) throw new InvalidDataException("MPN3.PositiveLost:" + name);
            int firstGroupMisses = 0;
            if (!plan.Bones && plan.SharedArrivals.Length == 0 && plan.StructuredConditions[0].KaleidoscopeFirstTarget != Beta110FastDenseId.Invalid)
            {
                foreach (uint i in input)
                {
                    ulong root = Beta111Profile.Instance.ComputeRootSeed(Beta110SeedCodec.FormatOrdinal(start + i));
                    if (NeowPartyReplay.Observe(root, plan).IdentityPass && !MpN3Offers(root, plan, false, 0).Take(3).Contains(plan.StructuredConditions[0].KaleidoscopeFirstTarget)) firstGroupMisses++;
                }
                if (firstGroupMisses == 0) throw new InvalidDataException("MPN3.UnreachedGate:" + name);
            }
            foreach (var kernel in kernels) _ = kernel.Run(batch, input, input.Length, compact, out _);
            var samples = new[] { new List<double>(), new List<double>(), new List<double>() };
            for (int round = 0; round < 3; round++)
            foreach (int index in round % 2 == 0 ? new[] { 0, 1, 2 } : new[] { 2, 1, 0 })
            {
                uint[] actual = kernels[index].Run(batch, input, input.Length, compact, out double ms);
                if (!actual.SequenceEqual(expected)) throw new InvalidDataException($"MPN3.PopulationMismatch:{name}:{compact}:{index}");
                samples[index].Add(ms);
            }
            string digest = Convert.ToHexString(SHA256.HashData(FamilyGpuComputeUtility.ToBytes(expected)));
            for (int index = 0; index < kernels.Length; index++) receipts.Add(new { name, positive, start, compact, input = input.Length,
                survivors = expected.Length, digest, firstGroupMisses, orderedFirstReject = index == 0 ? 0 : 1,
                numericalVariant = index == 2 ? "IntegerUnitFloat" : "NativeFloat64", dispatchSyncMs = samples[index],
                medianDispatchSyncMs = samples[index].Order().ElementAt(1), measuredStage = 3, conservativeArrivalBoundary,
                compatibleSourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(compatibleSource))),
                wholeTableExactForAuthoredQuery = true,
                metric = "FullMatcherDispatchSyncOnly;UploadReadbackSetupExcluded;NotCanonicalPeakOrProductionE2E" });
        }
    }

    private static string MpNIntegerUnitFloatSource(NeowFamilyGpuPlan packed, int stage = 3)
    {
        if (packed.NumericalVariant != NeowGpuNumericalVariant.IntegerUnitFloat ||
            packed.GpuNumericRequirements != GpuNumericRequirements.Int64 || !packed.IsMultiplayer)
            throw new InvalidDataException("PartyN.CompatibilitySourcePlan");
        string source = NeowFamilyGpuExecutor.ShaderSource(stage, leafyPreGate: packed.UsesLeafyPreGate,
            authoredUpgrades: packed.HasAuthoredUpgrades, localResults: packed.HasLocalResults, multiplayer: true,
            numericalVariant: packed.NumericalVariant);
        if (System.Text.RegularExpressions.Regex.IsMatch(source, @"\bdouble\b|\bfloat64\b|[0-9]lf\b") ||
            source.Contains("arithmetic_types_float64", StringComparison.Ordinal) ||
            !source.Contains("rt2_next_float_from_words_u32", StringComparison.Ordinal) ||
            !source.Contains("next_int_current_shortcut_from_mantissa", StringComparison.Ordinal))
            throw new InvalidDataException("PartyN.CompatibilitySourceNumerics");
        return source;
    }

    private sealed class MpNKernel : IDisposable
    {
        private readonly RenderingDevice _rd;
        private readonly List<Rid> _owned = [];
        private readonly Rid[] _buffers;
        private readonly Rid _pipeline, _uniforms;
        private readonly int _capacity;
        internal MpNKernel(RenderingDevice rd, NeowFamilyGpuPlan plan, string source, int capacity)
        {
            _rd = rd; _capacity = capacity;
            Rid Add(Rid rid) { _owned.Add(rid); return rid; }
            Rid Buffer(uint[] words) => Add(FamilyGpuComputeUtility.CreateStorageBuffer(rd, FamilyGpuComputeUtility.ToBytesNonEmpty(words), "MPN.Check"));
            try
            {
                var shader = Add(FamilyGpuComputeUtility.CompileShader(rd, source, "MPN.Check"));
                _pipeline = Add(FamilyGpuComputeUtility.CreateComputePipeline(rd, shader, "MPN.Check"));
                _buffers = [Buffer(new uint[8]), Buffer(plan.Meta), Buffer(plan.PoolMeta), Buffer(plan.Cards), Buffer(plan.Strike), Buffer(plan.Defend),
                    Buffer(new uint[8]), Buffer(new uint[capacity]), Buffer(plan.Bones), Buffer(plan.Conditions), Buffer(new uint[capacity])];
                _uniforms = Add(FamilyGpuComputeUtility.CreateUniformSet(rd, shader, _buffers, "MPN.Check"));
            }
            catch { Dispose(); throw; }
        }
        internal uint[] Run(SearchBatch batch, uint[] input, int count, bool compact, out double ms)
        {
            FamilyGpuComputeUtility.Update(_rd, _buffers[0], [(uint)batch.BatchBase, (uint)(batch.BatchBase >> 32),
                (uint)batch.BatchCandidateCount, (uint)count, compact ? 1u : 0u, (uint)_capacity, 0u, 0u], "MPN.Batch");
            FamilyGpuComputeUtility.Update(_rd, _buffers[6], [0x4e464d52, 1, 0, 0, 0, 0, 0, 0], "MPN.Header");
            FamilyGpuComputeUtility.Update(_rd, _buffers[10], input, "MPN.Input");
            long list = _rd.ComputeListBegin(); _rd.ComputeListBindComputePipeline(list, _pipeline);
            _rd.ComputeListBindUniformSet(list, _uniforms, 0);
            _rd.ComputeListDispatch(list, (uint)((count + 511) / 512), 1, 1); _rd.ComputeListEnd();
            long started = Stopwatch.GetTimestamp(); _rd.Submit(); _rd.Sync(); ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            byte[] header = _rd.BufferGetData(_buffers[6]);
            uint survivors = BitConverter.ToUInt32(header, 8);
            if (BitConverter.ToUInt32(header, 12) != 0 || BitConverter.ToUInt32(header, 16) != count || survivors > _capacity)
                throw new InvalidDataException("MPN.HeaderOrCheckpointFailure:" + string.Join(',', Enumerable.Range(0, 8).Select(i => BitConverter.ToUInt32(header, i * 4))));
            var result = new uint[survivors];
            if (survivors != 0) System.Buffer.BlockCopy(_rd.BufferGetData(_buffers[7], 0, survivors * 4), 0, result, 0, (int)survivors * 4);
            Array.Sort(result); return result;
        }
        public void Dispose() => FamilyGpuComputeUtility.FreeAll(_rd, _owned);
    }
}
