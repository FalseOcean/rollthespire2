using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.World.Beta109;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static class FamilyCpuOnlyChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException(reason); checks++; }
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json");
        var workload = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/v1/cases/S-three-shops.json");
        ExactSearchExecutionRequest Request(SearchQuery q)
        {
            var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, context.Context), workload.Options);
            return result.Plan ?? throw new InvalidOperationException(result.Issue + ";conditions=" + string.Join(',', q.MerchantColorlessConditions));
        }
        var request = Request(workload.Query);
        var profile = Beta111Profile.Instance;
        string? rootsBefore = Environment.GetEnvironmentVariable("RT2_CPU_ROOTS");
        string? sBefore = Environment.GetEnvironmentVariable("RT2_S_CPU");
        string? workersBefore = Environment.GetEnvironmentVariable("RT2_CPU_WORKERS");
        string? partitionsBefore = Environment.GetEnvironmentVariable("RT2_CPU_PARTITIONED");
        try
        {
            Environment.SetEnvironmentVariable("RT2_CPU_WORKERS", "1");
            Environment.SetEnvironmentVariable("RT2_CPU_PARTITIONED", "0");
            CheckFloat64Planning(context, workload, Check);
            ulong space = VisibleSeedCandidateCodec.SpaceSize(profile);
            foreach (ulong first in new[] { 0UL, 31UL, 45435421UL, (1UL << 32) - 5, space - 8193 })
            foreach (bool compact in new[] { false, true })
            {
                var batch = new SearchBatch(first, 8193);
                var input = compact ? FamilyCandidateSet.FromAbi1(batch, Enumerable.Range(0, 8193).Where(i => (i & 1) == 0).Select(i => (ulong)i).ToArray()) : FamilyCandidateSet.Dense(batch);
                var expectedRoots = input.EnumerateLogicalOrdinals().Select(i => profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile, batch.GlobalCandidate(i)))).ToArray();
                ulong[]? baseline = null;
                foreach (string mode in new[] { "baseline", "dense" })
                {
                    Environment.SetEnvironmentVariable("RT2_CPU_ROOTS", mode);
                    var cpu = new FamilyCpuExecution(request, "CPU.Check", "Test", workers: int.Parse(Environment.GetEnvironmentVariable("RT2_CPU_WORKERS") ?? "1"), numericalRoots: Environment.GetEnvironmentVariable("RT2_CPU_ROOTS") != "baseline");
                    int visited = 0;
                    var output = cpu.Execute(new(batch, request), input, CancellationToken.None, root =>
                    { Check(root == expectedRoots[visited++], "root identity: " + mode); return (root & 7) == 0; }).ExportAbi1().ToArray();
                    baseline ??= output;
                    Check(output.SequenceEqual(baseline), "CPU ordered population");
                    var empty = FamilyCandidateSet.Dense(new(first, 0));
                    Check(cpu.Execute(new(empty.Batch, request), empty, CancellationToken.None, _ => true).Count == 0, "zero");
                    bool canceled = false;
                    try { cpu.Execute(new(batch, request), input, new CancellationToken(true), _ => true); }
                    catch (OperationCanceledException) { canceled = true; }
                    Check(canceled, "cancel");
                    foreach (int workers in new[] { 1, 4, 8 })
                    {
                        Environment.SetEnvironmentVariable("RT2_CPU_WORKERS", workers.ToString());
                        Environment.SetEnvironmentVariable("RT2_CPU_PARTITIONED", "1");
                        var parallel = new FamilyCpuExecution(request, "CPU.Check", "Test", workers: int.Parse(Environment.GetEnvironmentVariable("RT2_CPU_WORKERS") ?? "1"), numericalRoots: Environment.GetEnvironmentVariable("RT2_CPU_ROOTS") != "baseline");
                        var actual = parallel.Execute(new(batch, request), input, CancellationToken.None, root => (root & 7) == 0);
                        Check(actual.ExportAbi1().Span.SequenceEqual(baseline), "partitioned ABI1");
                        bool faulted = false;
                        try { parallel.Execute(new(batch, request), input, CancellationToken.None, _ => throw new InvalidOperationException("injected")); }
                        catch (Exception ex) when (FamilyCpuMatchException.IsRecoverable(ex))
                        {
                            faulted = FamilyCpuMatchException.Matches(ex).All(failure =>
                                failure.FamilyId == "CPU.Check" && failure.Batch == batch &&
                                failure.InnerException is InvalidOperationException { Message: "injected" });
                        }
                        Check(faulted, "partition failure propagates");
                        bool disabled = false;
                        try { parallel.Execute(new(batch, request), input, CancellationToken.None, _ => true); }
                        catch (InvalidOperationException) { disabled = true; }
                        Check(disabled, "faulted invocation cannot resume");
                    }
                    Environment.SetEnvironmentVariable("RT2_CPU_WORKERS", "1");
                    Environment.SetEnvironmentVariable("RT2_CPU_PARTITIONED", "0");
                }
            }
            var authority = Beta111MerchantColorlessAuthority.From(request.Authority);
            var cases = new List<IReadOnlyList<MerchantColorlessSlotCondition>> { request.Evaluation.MerchantColorlessConditions };
            for (int ordinal = 1; ordinal <= 5; ordinal++)
            {
                var u = new MerchantColorlessSlotCondition(ordinal, MerchantColorlessSlot.Uncommon, authority.UncommonPool[ordinal % authority.UncommonPool.Count]);
                var r = new MerchantColorlessSlotCondition(ordinal, MerchantColorlessSlot.Rare, authority.RarePool[ordinal % authority.RarePool.Count]);
                cases.Add([u]); cases.Add([r]); cases.Add([u, r]); cases.Add([u, u]);
                cases.Add([u, u with { TargetCardKey = authority.UncommonPool[(ordinal + 1) % authority.UncommonPool.Count] }]);
            }
            var random = new Random(20260911);
            foreach (var conditions in cases)
            {
                var q = workload.Query with { MerchantColorlessConditions = conditions, MerchantColorlessSequenceConditions = [] };
                var compiled = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(q, context.Context), workload.Options);
                if (compiled.Plan is null)
                {
                    Check(compiled.Issue.Contains("MerchantColorlessSlotConflict", StringComparison.Ordinal) &&
                        conditions.GroupBy(c => (c.MerchantOrdinal, c.Slot)).Any(g => g.Select(c => c.TargetCardKey).Distinct().Count() > 1),
                        "S contradictory slots rejected by compiler");
                    continue;
                }
                var r = compiled.Plan;
                var fast = MerchantShopColorlessCpuPlan.TryCompile(r);
                Check(fast != null, "S slot compile");
                for (int i = 0; i < 2048; i++)
                {
                    ulong root = (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 32);
                    bool expected = MerchantShopColorlessQueryEvaluator.Evaluate(r.Evaluation, root, r.Authority).Disposition != SearchDisposition.NoMatch;
                    Check(fast!.Matches(root) == expected, "S exact predicate parity");
                }
            }
            var sequence = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/family-pricing-matrix-v1/S-unordered.json");
            Check(MerchantShopColorlessCpuPlan.TryCompile(Request(sequence.Query)) is not null,
                "S sequence compiles numerical realization before execution");
            int sequenceCases = 0;
            for (int horizon = 1; horizon <= 5; horizon++)
            foreach (var lane in new[]{MerchantColorlessSlot.Uncommon,MerchantColorlessSlot.Rare})
            foreach (var order in new[]{CombatRewardSequenceOrderMode.Ordered,CombatRewardSequenceOrderMode.Unordered})
            {
                ulong witness = (ulong)(10000 + horizon);
                var projected = Beta111NormalMerchantColorlessSequenceProjector.Project(witness,authority,horizon);
                ModelKey?[] targets = projected.Merchants.Select(m => (ModelKey?)(lane==MerchantColorlessSlot.Uncommon ? m.UncommonColorless : m.RareColorless)).ToArray();
                foreach (bool partial in new[]{false,true})
                {
                    var authored = targets.ToArray(); if(partial) authored[horizon-1]=null;
                    if(authored.All(t=>t is null)) continue; // compiler drops empty Product conditions
                    var c = new MerchantColorlessSequenceSearchCondition(horizon,order,lane,authored);
                    var q = sequence.Query with { MerchantColorlessConditions = [], MerchantColorlessSequenceConditions=[c] };
                    var r = Request(q); var fast=MerchantShopColorlessCpuPlan.TryCompile(r)!;
                    Check(fast.Matches(witness),"S positive sequence witness");
                    for(int i=0;i<512;i++)
                    {
                        ulong root=(ulong)random.NextInt64();
                        Check(fast.Matches(root)==(MerchantShopColorlessQueryEvaluator.Evaluate(r.Evaluation,root,r.Authority).Disposition!=SearchDisposition.NoMatch),"S ordered/unordered wildcard parity");
                    }
                    sequenceCases++;
                }
            }
            var mixedProjection=Beta111NormalMerchantColorlessSequenceProjector.Project(42,authority,5);
            var mixedQueries=new List<SearchQuery>();
            foreach(bool duplicates in new[]{false,true})
            {
                ModelKey?[] u=mixedProjection.Merchants.Select(m=>(ModelKey?)m.UncommonColorless).ToArray();
                if(duplicates){u[1]=u[0];for(int i=2;i<5;i++)u[i]=null;}
                mixedQueries.Add(sequence.Query with {
                    MerchantColorlessConditions=[new(1,MerchantColorlessSlot.Rare,mixedProjection.Merchants[0].RareColorless)],
                    MerchantColorlessSequenceConditions=[new(5,CombatRewardSequenceOrderMode.Unordered,MerchantColorlessSlot.Uncommon,u),
                        new(3,CombatRewardSequenceOrderMode.Ordered,MerchantColorlessSlot.Rare,[null,null,mixedProjection.Merchants[2].RareColorless])]});
            }
            foreach(var q in mixedQueries)
            {
                var r=Request(q);var fast=MerchantShopColorlessCpuPlan.TryCompile(r)!;
                for(ulong root=0;root<4096;root++)
                    Check(fast.Matches(root)==(MerchantShopColorlessQueryEvaluator.Evaluate(r.Evaluation,root,r.Authority).Disposition!=SearchDisposition.NoMatch),"S mixed slot/sequence/lanes/multiplicity parity");
            }
            CheckDuplicateMerchantPools(context.Context, workload.Options, Check);
            foreach(string id in new[]{"S-O5","S-U5","S-U3","S-high-output"})
            {
                var w=ProductionSearchReplay.Read<ProductionSearchReplayWorkload>($"tools/ProductionSearchHost/Corpus/family-cost-closure-v1/{id}.json");
                var r=Request(w.Query);var fast=MerchantShopColorlessCpuPlan.TryCompile(r)!;
                foreach(bool compact in new[]{false,true})
                {
                    var batch=new SearchBatch(0,8193);
                    var input=compact?FamilyCandidateSet.FromAbi1(batch,Enumerable.Range(0,8193).Where(i=>i%3==0).Select(i=>(ulong)i).ToArray()):FamilyCandidateSet.Dense(batch);
                    var cpu=new FamilyCpuExecution(r,"S.MerchantShopColorless","SequenceCheck",numericalRoots:true);
                    var output=cpu.Execute(new(batch,r),input,CancellationToken.None,fast.Matches);
                    var expected=input.EnumerateLogicalOrdinals().Where(i=>MerchantShopColorlessQueryEvaluator.Evaluate(r.Evaluation,profile.ComputeRootSeed(VisibleSeedCandidateCodec.FormatOrdinal(profile,i)),r.Authority).Disposition!=SearchDisposition.NoMatch).ToArray();
                    Check(output.ExportAbi1().Span.SequenceEqual(expected),"S sequence dense/compact tail ABI1 reference parity");
                    var zero=new SearchBatch(0,0);
                    Check(cpu.Execute(new(zero,r),FamilyCandidateSet.Dense(zero),CancellationToken.None,fast.Matches).Count==0,"S sequence zero");
                    using var cancel=new CancellationTokenSource();cancel.Cancel();
                    bool cancelled=false;try{cpu.Execute(new(batch,r),input,cancel.Token,fast.Matches);}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled,"S sequence cancellation");
                    Check(cpu.Execute(new(batch,r),input,CancellationToken.None,fast.Matches).ExportAbi1().Span.SequenceEqual(expected),"S sequence cancel reuse");
                }
            }
            Console.WriteLine($"S_SEQUENCE_PARITY PASS cases={sequenceCases};multiplicity=true;wildcards=true;denseCompactTail=true;cancelReuse=true");
            Console.WriteLine($"CPU_ONLY_CHECKS PASS checks={checks};rootModes=2;denseCompact=true;tail=8193;SCaseCount={cases.Count};renderingDeviceCreated=false");
            return 0;
        }
        finally
        {
            Environment.SetEnvironmentVariable("RT2_CPU_ROOTS", rootsBefore);
            Environment.SetEnvironmentVariable("RT2_S_CPU", sBefore);
            Environment.SetEnvironmentVariable("RT2_CPU_WORKERS", workersBefore);
            Environment.SetEnvironmentVariable("RT2_CPU_PARTITIONED", partitionsBefore);
        }
    }

    // Planning only: no session, RenderingDevice, shader or native dispatch.
    private static void CheckFloat64Planning(ProductionSearchReplayContext captured,
        ProductionSearchReplayWorkload donor, Action<bool, string> check)
    {
        var state = new FamilyGpuInitialization();
        check(state.Capture().CanAttemptFloat64 && state.Capture().Float64Support == "Unverified",
            "unknown backend retains an attempt without claiming FP64 support");
        state.ObserveEnvironment("adapter", true, "MeTaL");
        var metal = state.Capture();
        check(metal.RenderingBackend == "MeTaL" && !metal.CanAttemptFloat64 &&
            metal.Float64Support == "KnownUnsupported" && metal.Float64Reason == "MetalMslDoubleUnsupported" &&
            metal.GpuEligible && metal.State == FamilyGpuInitializationState.Unverified && metal.LastFailure is null,
            "exact Metal driver excludes only FP64 and preserves device creation evidence");
        long attempt = state.BeginCreation(); state.CreationSucceeded(attempt); state.OwnerStopped();
        check(!state.Capture().CanAttemptFloat64 && state.Capture().State == FamilyGpuInitializationState.Available,
            "successful device creation does not make Metal FP64 supported");
        foreach (string backend in new[] { "d3d12", "vulkan", "Unknown", "", "metal-via-wrapper" })
        {
            state.ObserveEnvironment("adapter", true, backend);
            check(state.Capture().RenderingBackend == backend && state.Capture().CanAttemptFloat64 &&
                state.Capture().Float64Support == "Unverified", "non-Metal driver is unverified: " + backend);
        }

        string[] controls = ["RT2_PRIVATE_SERIAL_EXPERIMENT", "RT2_BONES_CAPSULE_BOUNDARY",
            "RT2_DIRECT_CAPSULE_EXPERIMENT", "RT2_RSW_EXPERIMENT", "RT2_NWAE_EXPERIMENT",
            "RT2_NC_EXPERIMENT", "RT2_EW_EXPERIMENT", "RT2_PRIVATE_ORDINAL_SELECTION",
            "RT2_N_DIRECT_EXPERIMENT", "RT2_N_BONES_EXPERIMENT", "RT2_T_FULL_TARGET_EXPERIMENT",
            "RT2_T_BONES_EXPERIMENT", "RT2_N_TRANSFORM_AGGREGATE_EXPERIMENT"];
        var originalControls = controls.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (string name in controls) Environment.SetEnvironmentVariable(name, null);
            // Do not read a user's selection file during the check.
            Environment.SetEnvironmentVariable("RT2_PRIVATE_ORDINAL_SELECTION",
                Path.Combine(Path.GetTempPath(), "rt2-fp64-planning-absent-" + Guid.NewGuid().ToString("N")));
            ExactSearchExecutionRequest Request(SearchQuery query, SearchContext? context = null, bool skipExact = false)
            {
                var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context ?? captured.Context),
                    donor.Options with { ScanCount = 16, WorkerCount = 1, SkipExactValidation = skipExact });
                return result.Plan ?? throw new InvalidDataException("FP64 planning fixture: " + result.Issue);
            }
            void Declined(Action action, string reason)
            {
                try { action(); }
                catch (InvalidOperationException error) when (error.Message.StartsWith(reason, StringComparison.Ordinal))
                { check(true, reason); return; }
                check(false, "FP64 request was not explicitly declined: " + reason);
            }
            bool HasFloat64Gpu(FamilyExecutionPlan plan) => plan.OrderedFamilies.Any(UsesFloat64);
            bool UsesFloat64(IFamilyInvocation family) =>
                (family.GpuNumericRequirements & (GpuNumericRequirements.Float64 | GpuNumericRequirements.Unspecified)) != 0;
            void SamePlan(FamilyExecutionPlan original, FamilyExecutionPlan actual, string reason)
            {
                check(original.PlanId == actual.PlanId && original.FinalValidationSkipped == actual.FinalValidationSkipped &&
                    original.Stages.Select(s => s.ConditionPerformance.PhysicalImplementationRevision)
                        .SequenceEqual(actual.Stages.Select(s => s.ConditionPerformance.PhysicalImplementationRevision)) &&
                    original.Stages.Select(s => string.Join(',', s.Coverage))
                        .SequenceEqual(actual.Stages.Select(s => string.Join(',', s.Coverage))) &&
                    original.FilterRemainders.SequenceEqual(actual.FilterRemainders), reason);
            }
            void CheckSkippedExact(ExactSearchExecutionRequest request)
            {
                var original = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: true);
                if (HasFloat64Gpu(original))
                    Declined(() => FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false),
                        "GpuFloat64UnavailableWithExactSkipped");
                else SamePlan(original, FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false),
                    "SkipExact retains a selected allocation that was already CPU/integer-only");
            }
            void CheckExplicitControl(ExactSearchExecutionRequest request, bool allocationSelection = true)
            {
                FamilyExecutionPlan? original = null;
                Exception? originalFailure = null;
                try { original = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: true); }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException or InvalidDataException or System.Text.Json.JsonException)
                { originalFailure = error; }
                if (originalFailure is not null)
                {
                    // Invalid controls keep their exact pre-existing rejection;
                    // they are not counted as successful capability fallback.
                    try { _ = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false); }
                    catch (Exception error) when (error.GetType() == originalFailure.GetType() && error.Message == originalFailure.Message)
                    { check(true, "invalid physical selection retains original type/reason"); return; }
                    check(false, "invalid physical selection changed its rejection or succeeded");
                    return;
                }
                check(!allocationSelection || original!.DecisionEvidence.Contains("ExplicitPhysicalSelection=true", StringComparer.Ordinal),
                    "physical-control fixture actually selected its requested allocation;controls=" +
                    string.Join(',', controls.Where(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                        .Select(name => name + "=" + Environment.GetEnvironmentVariable(name))) +
                    ";actualPlan=" + original!.PlanId + ";evidence=" + string.Join('|', original.DecisionEvidence));
                if (HasFloat64Gpu(original!))
                    Declined(() => FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false),
                        "GpuFloat64PhysicalSelectionUnavailable");
                else SamePlan(original!, FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false),
                    "integer/CPU physical selection remains unchanged");
            }
            var cases = new List<SearchQuery>();
            foreach (string path in new[] { "v1/cases/N-entry.json", "v1/cases/NC-silken.json",
                "v1/cases/EW-high-output.json", "v1/cases/A-pael.json", "v1/cases/W-K4.json",
                "direct-capsule-v1/Large-UR-check.json", "classic-product-v1/B-SourceCapsules.json",
                "bones-k-sameroute-v1/scoped-both-route.json", "family-pricing-matrix-v1/NAEW-owner.json",
                "composition-holdout-v3/RSW-middle.json", "composition-holdout-v3/ECW-middle.json" })
                cases.Add(ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path).Query);
            cases.Add(SearchQuery.Empty with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones),
                OpeningRouteRelicRequirement = new(BaseGameModelKeys.Relics.NeowsBones,
                    [BaseGameModelKeys.Relics.LeafyPoultice, BaseGameModelKeys.Relics.NewLeaf]),
                TransformationAggregate = new(TransformationOpening.BonesLeafyNewLeaf, TransformationPickupOrder.Any,
                    false, false, false, TransformationAggregatePredicate.RareCountAtLeast, 1, []) });
            check(FamilyExecutionCoordinator.CreateRegisteredFamilies(Request(cases[0]), gpuAvailable: true, gpuFloat64Available: true)
                .Any(f => f is NeowFamily && f.ConditionPerformance.UsesGpu),
                "unverified non-Metal allowance retains the existing N GPU candidate");
            var compatibleNeow = FamilyExecutionCoordinator.CreateRegisteredFamilies(Request(cases[0]),
                gpuAvailable: true, gpuFloat64Available: false).OfType<NeowFamily>().Single();
            check(compatibleNeow.GpuPlan?.NumericalVariant == NeowGpuNumericalVariant.IntegerUnitFloat &&
                ((IFamilyInvocation)compatibleNeow).GpuNumericRequirements == GpuNumericRequirements.Int64 &&
                !((IFamilyInvocation)compatibleNeow).CanBindPrivateSerial &&
                compatibleNeow.QuotePhysicalWork(new(false, false, 65536)) is null &&
                compatibleNeow.ConditionPerformance.PhysicalImplementationRevision.Contains("IntegerUnitFloat", StringComparison.Ordinal),
                "compatible N has an actual integer body, independent revision, no native price or private binding");
            foreach (var query in cases)
            {
                var request = Request(query);
                string originalQuery = System.Text.Json.JsonSerializer.Serialize(request.CompiledSearch.NormalizedQuery, ProductionSearchReplay.JsonOptions);
                var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true, gpuFloat64Available: false);
                check(registered.Any(f => f is CapsuleRelicFamily || f.FamilyId is not ("R.Relic" or "S.MerchantShopColorless")),
                    "affected planning case actually retains FP64-family query work");
                check(registered.All(f => !UsesFloat64(f)),
                    "restricted registration uses actual integer numerical bodies");
                var selected = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false);
                check(selected.OrderedFamilies.All(f => !UsesFloat64(f)),
                    "default/priced/fused alternatives cannot recreate an FP64 GPU stage");
                check(selected.DecisionEvidence.Any(e => e.Contains("GpuFloat64Unavailable", StringComparison.Ordinal)) &&
                    !selected.FinalValidationSkipped && originalQuery == System.Text.Json.JsonSerializer.Serialize(
                        request.CompiledSearch.NormalizedQuery, ProductionSearchReplay.JsonOptions),
                    "capability fallback discloses cause and preserves complete authored query for Exact");
                // In particular, the existing BonesK fixture exercises a fused
                // same-route filter whose output can be stricter than separate
                // N/R filters. Coverage names alone cannot certify equivalence
                // after Exact is skipped (BonesCapsuleBoundaryGpu.Verify).
                CheckSkippedExact(Request(query, skipExact: true));
            }
            foreach (string path in new[] { "v1/cases/S-three-shops.json", "family-pricing-matrix-v1/R-three-common.json" })
            {
                var query = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>("tools/ProductionSearchHost/Corpus/" + path).Query;
                var request = Request(query, skipExact: true);
                var registered = FamilyExecutionCoordinator.CreateRegisteredFamilies(request, gpuAvailable: true, gpuFloat64Available: false);
                check(registered.Count == 1 && registered[0].ConditionPerformance.UsesGpu &&
                    registered[0] is RelicFamily or MerchantShopColorlessFamily,
                    "integer-only R/S remain GPU candidates even with Exact skipped");
                var selected = FamilyExecutionCoordinator.Plan(request, gpuAvailable: true, gpuFloat64Available: false);
                check(selected.FinalValidationSkipped, "integer-only request retains authored SkipExact choice");
            }
            // The BonesK corpus targets Anger in another character's pool and
            // was authored for Defect. In the generic Ironclad context that
            // target is unavailable, so the bounded selector correctly declines.
            var bonesContext = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
                "tools/ProductionSearchHost/Corpus/n-direct-v1/context-defect-a10.json").Context;
            foreach (var control in new[] { ("RT2_PRIVATE_SERIAL_EXPERIMENT", "private-NC", 1),
                ("RT2_BONES_CAPSULE_BOUNDARY", "local", 7), ("RT2_DIRECT_CAPSULE_EXPERIMENT", "fused", 5),
                ("RT2_RSW_EXPERIMENT", "private-RSW", 9), ("RT2_NWAE_EXPERIMENT", "private-NEAW", 8),
                ("RT2_NC_EXPERIMENT", "private-nc", 1), ("RT2_EW_EXPERIMENT", "private-ordinal", 2),
                ("RT2_PRIVATE_SERIAL_EXPERIMENT", "private-N", 0) })
            {
                Environment.SetEnvironmentVariable(control.Item1, control.Item2);
                try { CheckExplicitControl(Request(cases[control.Item3],
                    control.Item1 == "RT2_BONES_CAPSULE_BOUNDARY" ? bonesContext : null)); }
                finally { Environment.SetEnvironmentVariable(control.Item1, null); }
            }
            var directQuery = SearchQuery.Empty with { OpeningRoute = cases[8].OpeningRoute,
                StructuredOpeningEffects = cases[8].StructuredOpeningEffects };
            var bonesQuery = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/pricing-hole-v1/N-KP-B5.json").Query;
            var fullTargetQuery = cases[^1] with { TransformationAggregate = cases[^1].TransformationAggregate! with { MinimumRareCount = 3 } };
            foreach (var control in new[] { ("RT2_N_DIRECT_EXPERIMENT", "baseline", directQuery),
                ("RT2_N_DIRECT_EXPERIMENT", "arcane-direct", directQuery),
                ("RT2_N_BONES_EXPERIMENT", "baseline", bonesQuery),
                ("RT2_N_BONES_EXPERIMENT", "early-accept", bonesQuery),
                ("RT2_N_BONES_EXPERIMENT", "rare-local", bonesQuery),
                ("RT2_T_FULL_TARGET_EXPERIMENT", "baseline", fullTargetQuery),
                ("RT2_T_FULL_TARGET_EXPERIMENT", "full1m", fullTargetQuery),
                ("RT2_T_BONES_EXPERIMENT", "baseline", fullTargetQuery),
                ("RT2_N_TRANSFORM_AGGREGATE_EXPERIMENT", "baseline", fullTargetQuery) })
            {
                Environment.SetEnvironmentVariable(control.Item1, control.Item2);
                // Family-local variants do not claim the whole-allocation
                // explicitSelection marker. Preserve their actual CPU/integer
                // baseline, or refuse a selected unavailable FP64 variant.
                try { CheckExplicitControl(Request(control.Item3), allocationSelection: false); }
                finally { Environment.SetEnvironmentVariable(control.Item1, null); }
            }
            foreach (var control in new[] { ("RT2_N_DIRECT_EXPERIMENT", "default", directQuery),
                ("RT2_N_BONES_EXPERIMENT", "pair-local", bonesQuery),
                ("RT2_T_BONES_EXPERIMENT", "optimized", fullTargetQuery),
                ("RT2_T_FULL_TARGET_EXPERIMENT", "ignored", fullTargetQuery),
                ("RT2_N_TRANSFORM_AGGREGATE_EXPERIMENT", "ignored", fullTargetQuery) })
            {
                Environment.SetEnvironmentVariable(control.Item1, control.Item2);
                try
                {
                    var selected = FamilyExecutionCoordinator.Plan(Request(control.Item3), gpuAvailable: true, gpuFloat64Available: false);
                    check(!HasFloat64Gpu(selected) && !selected.FinalValidationSkipped,
                        "default/ignored family-local knob does not force an unavailable FP64 trial");
                }
                finally { Environment.SetEnvironmentVariable(control.Item1, null); }
            }
            // Deliberately malformed selection: the existing JSON rejection must
            // survive unchanged, rather than becoming a capability success.
            Environment.SetEnvironmentVariable("RT2_PRIVATE_ORDINAL_SELECTION", Path.GetFullPath(
                "tools/ProductionSearchHost/Corpus/v1/cases/N-entry.json"));
            try { CheckExplicitControl(Request(cases[0])); }
            finally { Environment.SetEnvironmentVariable("RT2_PRIVATE_ORDINAL_SELECTION",
                Path.Combine(Path.GetTempPath(), "rt2-fp64-planning-absent-" + Guid.NewGuid().ToString("N"))); }
            var ordinaryR = ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(
                "tools/ProductionSearchHost/Corpus/family-pricing-matrix-v1/R-three-common.json");
            var integerPair = Request(donor.Query with { RelicSequenceConstraints = ordinaryR.Query.RelicSequenceConstraints }, skipExact: true);
            Environment.SetEnvironmentVariable("RT2_PRIVATE_SERIAL_EXPERIMENT", "private-RS");
            try
            {
                var privateIntegerPlan = FamilyExecutionCoordinator.Plan(integerPair, gpuAvailable: true, gpuFloat64Available: false);
                check(privateIntegerPlan.Stages.Count == 1 && privateIntegerPlan.Stages[0].ConditionPerformance.UsesGpu &&
                    privateIntegerPlan.Stages[0].Coverage.ToHashSet().SetEquals(["R.Relic", "S.MerchantShopColorless"]),
                    "ordinary R/S explicit private allocation remains available with Exact skipped");
            }
            finally { Environment.SetEnvironmentVariable("RT2_PRIVATE_SERIAL_EXPERIMENT", null); }
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var partyContext = (SearchContext)typeof(CombinationRegressionChecks).GetMethod("PartyContext", flags)!
                .Invoke(null, [captured.Context, 2, null, null])!;
            var partyQuery = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = cases[0] },
                new(1, ModelKeySetFilter.Empty) { Conditions = donor.Query }] };
            var partyRequest = Request(partyQuery, partyContext);
            var partyRegistered = FamilyExecutionCoordinator.CreateRegisteredFamilies(partyRequest, gpuAvailable: true, gpuFloat64Available: false);
            check(partyRegistered.OfType<NeowFamily.PartyInitial>().Any() &&
                partyRegistered.OfType<NeowFamily.PartyInitial>().All(f => f.ConditionPerformance.UsesGpu &&
                    ((IFamilyInvocation)f).GpuNumericRequirements == GpuNumericRequirements.Int64 &&
                    f.ConditionPerformance.PhysicalImplementationRevision.Contains(".Public.", StringComparison.Ordinal) &&
                    f.ConditionPerformance.PhysicalImplementationRevision.Contains("IntegerUnitFloat", StringComparison.Ordinal)) &&
                partyRegistered.OfType<PartyFamily>().Where(f => f.FamilyId == "S.MerchantShopColorless")
                    .SelectMany(f => f.PersonalStages).Any(s => s.Family.ConditionPerformance.UsesGpu),
                "party N selects public integer GPU while independent integer S remains eligible");
            var partyPlan = FamilyExecutionCoordinator.Plan(partyRequest, gpuAvailable: true, gpuFloat64Available: false);
            check(!HasFloat64Gpu(partyPlan) && partyPlan.OrderedFamilies.Any(f => f is NeowFamily.PartyInitial &&
                f.ConditionPerformance.UsesGpu),
                "party private/fused paths inherit compatible child registration");
            // Compatible children in PartyFamily must retain the cause of
            // their physical selection even when there is no N table wrapper.
            var partyAncient = SearchQuery.Empty with { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = cases[3] },
                new(1, ModelKeySetFilter.Empty) { Conditions = donor.Query }] };
            var partyAncientPlan = FamilyExecutionCoordinator.Plan(Request(partyAncient, partyContext),
                gpuAvailable: true, gpuFloat64Available: false);
            check(partyAncientPlan.DecisionEvidence.Any(e => e.Contains("GpuFloat64Unavailable", StringComparison.Ordinal)),
                "configured party child retains numeric compatibility disclosure without N");
            CheckSkippedExact(Request(partyAncient, partyContext, skipExact: true));
            var effects = captured.Context.Authority.EffectAuthority ?? throw new InvalidDataException("FP64 event fixture missing effects");
            var missingEventAuthority = captured.Context with { Authority = captured.Context.Authority.WithEffectAuthority(
                effects with { EventResultStaticCatalogExact = false }) };
            var eventQuery = SearchQuery.Empty with { EventResultConditions = [new(EventResultConditionKind.TrashHeapGrabCard,
                RolltheSpire2.Core.Events.Beta111EventResultCatalog.TrashHeapGrabCards[0])] };
            var cpuOnlyEvent = Request(eventQuery, missingEventAuthority, skipExact: true);
            var eventRegistration = FamilyExecutionCoordinator.CreateRegisteredFamilies(cpuOnlyEvent, gpuAvailable: true, gpuFloat64Available: true);
            check(eventRegistration.OfType<EventResultFamily>().Any(f => !f.Plan.NumericalSupported && !f.Plan.GpuSupported) &&
                eventRegistration.All(f => !f.ConditionPerformance.UsesGpu),
                "missing event catalog creates an intrinsically CPU-only conservative E filter");
            var originalEvent = FamilyExecutionCoordinator.Plan(cpuOnlyEvent, gpuAvailable: true, gpuFloat64Available: true);
            var metalEvent = FamilyExecutionCoordinator.Plan(cpuOnlyEvent, gpuAvailable: true, gpuFloat64Available: false);
            SamePlan(originalEvent, metalEvent, "Metal does not reject or broaden existing CPU-only event SkipExact plan");
            check(metalEvent.FinalValidationSkipped && !metalEvent.DecisionEvidence.Any(e => e.StartsWith("GpuFloat64Unavailable;", StringComparison.Ordinal)),
                "intrinsic CPU event filtering is not reported as a new Metal degradation");
            Console.WriteLine("FP64_PLANNING_CHECKS PASS nativeExecution=false;backendBoundary=Metal;otherDrivers=Unverified");
        }
        finally { foreach (var (name, value) in originalControls) Environment.SetEnvironmentVariable(name, value); }
    }

    private static void CheckDuplicateMerchantPools(SearchContext donor, SearchRunOptions options, Action<bool, string> check)
    {
        var effects = donor.Authority.EffectAuthority ?? throw new InvalidDataException("S duplicate pool donor missing");
        var source = effects.MerchantColorlessOrderedPool ?? effects.ColorlessRewardPool
            ?? throw new InvalidDataException("S duplicate pool source missing");
        var u = source.Where(c => c.Rarity == EffectCardRarity.Uncommon).DistinctBy(c => c.CardKey).Take(3).ToArray();
        var r = source.Where(c => c.Rarity == EffectCardRarity.Rare).DistinctBy(c => c.CardKey).Take(3).ToArray();
        check(u.Length == 3 && r.Length == 3, "S nonempty duplicate pool donor identities");
        var random = new Random(20261013);
        int cases = 0;
        foreach (var shape in new[] { (U: true, R: false), (U: false, R: true), (U: true, R: true) })
        {
            // Copy actual donor identities and close the existing merchant input
            // contract. Pool order/duplicate entries remain authored draw weights.
            var poolU = (shape.U ? new[] { u[0], u[1], u[0], u[2], u[0], u[1] } : u)
                .Select((card, index) => card with { PoolOrder = index }).ToArray();
            var poolR = (shape.R ? new[] { r[0], r[0], r[1], r[2], r[1] } : r)
                .Select((card, index) => card with { PoolOrder = index }).ToArray();
            var context = donor with { Authority = donor.Authority.WithEffectAuthority(effects with {
                MerchantColorlessOrderedPool = poolU.Concat(poolR).ToArray(),
                MerchantColorlessOrderedPoolExact = true, MerchantColorlessPoolIdentityHooksExact = true,
                MerchantColorlessRarityIdentityHooksExact = true, MerchantInitialInventoryShapeExact = true }) };
            var authority = Beta111MerchantColorlessAuthority.From(context.Authority);
            check(authority.HasExactV1Inputs && authority.UncommonPool.Count == poolU.Length &&
                authority.RarePool.Count == poolR.Length, "S duplicate pools retain exact input contract and draw counts");

            ulong witness = 0;
            Beta111NormalMerchantProjection[] projected = [];
            for (; witness < 4096; witness++)
            {
                var shops = Beta109WorldRng.CreateNamed(unchecked(witness + (ulong)authority.PlayerSlotIndex), "shops");
                projected = Enumerable.Range(1, 5).Select(ordinal => Beta111NormalMerchantShopsContinuation.AdvanceOne(
                    shops, ordinal, authority.UncommonPool, authority.RarePool)).ToArray();
                bool LaterDuplicate(bool uncommon)
                {
                    var trace = shops.Trace.First(t => t.SourceStage == (uncommon
                        ? "merchant-1:colorless-uncommon" : "merchant-1:colorless-rare"));
                    var pool = uncommon ? authority.UncommonPool : authority.RarePool;
                    int index = trace.IntResult!.Value;
                    return Enumerable.Range(0, index).Any(i => pool[i] == pool[index]);
                }
                if ((!shape.U || LaterDuplicate(true)) && (!shape.R || LaterDuplicate(false)) &&
                    projected.Count(m => m.UncommonColorless == projected[0].UncommonColorless) >= 2 &&
                    projected.Count(m => m.RareColorless == projected[0].RareColorless) >= 2) break;
            }
            check(witness < 4096, "S positive witness draws a non-first duplicate source index and repeated identities");

            void Verify(SearchQuery query)
            {
                var result = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), options);
                var request = result.Plan ?? throw new InvalidDataException("S duplicate pool factory: " + result.Issue);
                var fast = MerchantShopColorlessCpuPlan.TryCompile(request);
                check(fast is not null, "S duplicate pool numerical realization admitted");
                check(MerchantShopColorlessQueryEvaluator.Evaluate(request.Evaluation, witness, request.Authority).Disposition == SearchDisposition.Match &&
                    fast!.Matches(witness), "S duplicate source positive oracle witness");
                foreach (ulong root in new[] { 0UL, 1UL, ulong.MaxValue, 1UL << 63 }.Concat(
                    Enumerable.Range(0, 512).Select(_ => (ulong)random.NextInt64() ^ ((ulong)random.NextInt64() << 32))))
                {
                    var expected = MerchantShopColorlessQueryEvaluator.Evaluate(request.Evaluation, root, request.Authority).Disposition;
                    check(expected is SearchDisposition.Match or SearchDisposition.NoMatch, "S duplicate oracle remains exact");
                    check(fast!.Matches(root) == (expected == SearchDisposition.Match), "S duplicate source ModelKey oracle parity");
                }
                cases++;
            }

            for (int horizon = 1; horizon <= 5; horizon++)
            foreach (var lane in new[] { MerchantColorlessSlot.Uncommon, MerchantColorlessSlot.Rare })
            {
                bool uncommon = lane == MerchantColorlessSlot.Uncommon;
                ModelKey?[] targets = projected.Take(horizon).Select(m => (ModelKey?)(uncommon ? m.UncommonColorless : m.RareColorless)).ToArray();
                ModelKey?[] wildcard = new ModelKey?[horizon]; wildcard[0] = targets[0];
                Verify(SearchQuery.Empty with { MerchantColorlessSequenceConditions = [
                    new(horizon, CombatRewardSequenceOrderMode.Unordered, lane, targets),
                    new(horizon, CombatRewardSequenceOrderMode.Unordered, lane, wildcard)] });
            }
            // Same-lane horizons and multiplicity must share observations, while
            // fixed and ordered slots still accept every duplicate draw index.
            foreach (var lane in new[] { MerchantColorlessSlot.Uncommon, MerchantColorlessSlot.Rare })
            {
                bool uncommon = lane == MerchantColorlessSlot.Uncommon;
                ModelKey first = uncommon ? projected[0].UncommonColorless : projected[0].RareColorless;
                ModelKey second = uncommon ? projected[1].UncommonColorless : projected[1].RareColorless;
                var other = uncommon ? MerchantColorlessSlot.Rare : MerchantColorlessSlot.Uncommon;
                Verify(SearchQuery.Empty with {
                    MerchantColorlessConditions = [new(1, lane, first), new(1, lane, first)],
                    MerchantColorlessSequenceConditions = [
                        new(5, CombatRewardSequenceOrderMode.Unordered, lane, [first, first, null, null, null]),
                        new(2, CombatRewardSequenceOrderMode.Unordered, lane, [first, second]),
                        new(3, CombatRewardSequenceOrderMode.Ordered, other, [null, null,
                            uncommon ? projected[2].RareColorless : projected[2].UncommonColorless])] });
            }
        }
        Console.WriteLine($"S_DUPLICATE_POOL_PARITY PASS cases={cases};randomRootsPerCase=512;positiveLaterDuplicate=true;oracle=MerchantQueryEvaluator");
    }
}
