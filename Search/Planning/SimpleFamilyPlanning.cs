using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

public static partial class FamilyExecutionCoordinator
{
    private static FamilyExecutionPlan PlanSimpleSingleplayer(PreparedSearch request,
        IReadOnlyList<FilterBodyDescription> registered, bool gpuAvailable, bool allowFloat64Units)
    {
        var model = request.Model;
        var preferences = FilterExecutionPreferences.From(request.RunOptions);
        IReadOnlyList<FilterBodyDescription> bodies = gpuAvailable ? registered : CpuAllocation(registered);
        FilterBodyDescription? denseEntry = null;
        FilterBodyDescription? measuredLocal = null;
        var fused = new List<FilterBodyDescription>(2);
        if (gpuAvailable && allowFloat64Units)
        {
            // A coupled Dense-only accelerator stays one whole numerical unit.
            // Its local choice never prices the independent Search suffix.
            denseEntry = FamilyAuxiliaryEvidence.Read<FilterBodyDescription?>(nameof(FamilyExecutionCoordinator),
                "DenseEntryUnit", () =>
                    BonesCapsuleAllocation.TryCreateDefaultUnit(model, preferences, bodies, out var bones) ? bones :
                    DirectCapsuleAllocation.TryCreateDefaultUnit(model, preferences, bodies, out var direct) ? direct : null,
                null, request.SnapshotFingerprint);
        }
        // Reuse the existing whole numerical bodies. Their complete logical
        // Coverage competes in the same greedy walk as the independent units.
        // Restricted/integer registration must never reconstruct a native body.
        if (denseEntry is null && gpuAvailable && bodies.Any(body => body.FamilyId == "N.Neow" && body.UsesGpu) &&
            bodies.Any(body => body.FamilyId is TransformationAggregateFamily.Id or RelicFamily.Id))
        {
            var replay = FamilyAuxiliaryEvidence.Read<NeowReplayPlan?>(nameof(FamilyExecutionCoordinator),
                "SimpleNeowReplayPreparation", () => NeowReplayPlan.Compile(model, false), null, request.SnapshotFingerprint);
            void AddUnit(string stage, Func<FilterBodyDescription?> create)
            {
                if (FamilyAuxiliaryEvidence.Read<FilterBodyDescription?>(nameof(FamilyExecutionCoordinator), stage,
                    create, null, request.SnapshotFingerprint) is { } unit)
                    fused.Add(unit);
            }
            if (replay is not null && bodies.Any(family => family.FamilyId == TransformationAggregateFamily.Id))
                AddUnit("SimpleNeowTransformationUnit", () =>
                    NeowTransformationComposite.TryCreate(model, replay, out var composite,
                        allowFloat64Units ? NeowGpuNumericalVariant.NativeFloat64 : NeowGpuNumericalVariant.IntegerUnitFloat) && composite is not null
                        ? new NeowFamily(model, replay, transformations: composite).Describe(preferences) : null);
            if (replay is not null && bodies.FirstOrDefault(family => family.FamilyId == "N.Neow") is { } nBody &&
                bodies.FirstOrDefault(family => family.FamilyId == RelicFamily.Id) is { } rBody)
            {
                var local = FamilyAuxiliaryEvidence.Read<FilterBodyDescription?>(nameof(FamilyExecutionCoordinator),
                    "SimpleModernNeowCapsuleLocalUnit", () =>
                        ModernNeowCapsuleComposite.TryCreate(model, replay, nBody, rBody, allowFloat64Units, out var unit) ? unit : null,
                    null, request.SnapshotFingerprint);
                if (local is not null)
                {
                    fused.Add(local);
                    if (bodies.Count == 2 && request.RunOptions.ManualFamilyOrder is not { Count: > 0 } &&
                        ModernNeowCapsuleComposite.PreferMeasuredWholeSearch(model, replay, local,
                            FamilyDeviceProfileFoundation.Initialization.Capture()))
                        measuredLocal = local;
                }
                AddUnit("SimpleModernNeowCapsuleIdentityUnit", () =>
                    ModernCapsuleRelicFamily.TryCreateIdentityComposite(model, replay, nBody, rBody, out var composite)
                        ? composite : null);
            }
            if (allowFloat64Units && replay is not null && bodies.Any(family => family.FamilyId == "R.Relic") && NeowReplayPlan.HasCapsule(model))
                AddUnit("SimpleNeowCapsuleUnit", () =>
                    NeowCapsuleComposite.TryCreate(model, replay, out var composite) && composite is not null
                        ? new NeowFamily(model, replay, composite: composite).Describe(preferences) : null);
        }
        FamilyExecutionPlan selected = request.RunOptions.ManualFamilyOrder is { Count: > 0 } manual
            ? FamilyPlanner.InManualOrder(denseEntry is null ? [..fused, ..bodies] : [denseEntry, ..fused, ..bodies],
                bodies, manual, denseEntry)
            : measuredLocal is not null
                ? FamilyPlanner.PlanSimple([measuredLocal])
            : FamilyPlanner.PlanSimple(bodies, fused, includeCpuAlternatives: gpuAvailable, denseEntry: denseEntry);
        if (measuredLocal is not null)
            selected = selected with { DecisionEvidence = [..selected.DecisionEvidence, "MeasuredWholeSearchModernLocalCapsule;SteadyState;JointQuoteUnavailable"] };
        // This diagnostic control disables only the new partial-prefix transport;
        // the existing complete-order default remains unchanged.
        bool prefixTransportEnabled = !string.Equals(Environment.GetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT"),
            "off", StringComparison.OrdinalIgnoreCase);
        NeowEventFirstDrawGate.OrderedFusion? sharedPrefix = null;
        if (denseEntry is null && gpuAvailable && allowFloat64Units && NeowEventFirstDrawGate.AutoPriceEnabled &&
            request.RunOptions.ManualFamilyOrder is not { Count: > 0 } &&
            FamilyAuxiliaryEvidence.Read<(FamilyExecutionPlan Plan, NeowEventFirstDrawGate.OrderedFusion Pair)?>(
                nameof(FamilyExecutionCoordinator), "SharedRootEntry",
                () => SharedRootEntry(request, selected, bodies, fused, prefixTransportEnabled), null, request.SnapshotFingerprint) is { } shared)
        {
            selected = shared.Plan;
            sharedPrefix = shared.Pair;
        }
        var order = selected.OrderedFamilies;
        int privatePrefixCount = order.TakeWhile(AutomaticPrivateSerialInvocation.CanBindWholeFamily).Count();
        bool completePrivateOrder = privatePrefixCount == order.Count;
        bool privateSerial = BindsPrivatePrefix(order, privatePrefixCount, prefixTransportEnabled);
        if (privateSerial)
        {
            // Transport follows the already selected order. Missing sparse
            // prices cannot remove a filter or promote a different whole chain.
            var invocation = AutomaticPrivateSerialInvocation.Prepare(model, preferences, order.Take(privatePrefixCount).ToArray(),
                enableSmallCandidateExact: !request.RunOptions.SkipExactValidation,
                estimationStages: selected.Stages.Take(privatePrefixCount).ToArray(),
                preparedSharedRoot: sharedPrefix,
                followingCoverage: order.Skip(privatePrefixCount).SelectMany(family => family.Coverage).ToArray());
            var wrapped = FamilyPlanner.InOrder([invocation]);
            selected = selected with
            {
                PlanId = selected.PlanId + (completePrivateOrder ? ".PrivateOrdinal" : ".PrivateOrdinalPrefix"),
                // The boundary returns ordered ABI1. Keep every public tail
                // projection, conditional cost and obligation exactly as selected.
                Stages = [..wrapped.Stages, ..selected.Stages.Skip(privatePrefixCount)],
                DecisionEvidence = [..selected.DecisionEvidence,
                    completePrivateOrder
                        ? "PrivateOrdinalTransportForSelectedWholeFamilyOrder;NoCompleteChainPromotionQuote;LazyStageCreation"
                        : "PrivateOrdinalTransportForSelectedDensePrefix;PublicOrderedAbi1BeforeUnbindableTail;NoReordering;LazyStageCreation",
                    "EtaUsesFamilyComputationAndSeparateTransportEnvelope;PrivateTransportOverheadNotMeasured"]
            };
        }
        else selected = selected with { DecisionEvidence = [..selected.DecisionEvidence,
            !prefixTransportEnabled && !completePrivateOrder && privatePrefixCount is >= 2 and <= 8
                ? "PublicOrderedAbi1Retained;DensePrefixTransportDisabledByDiagnosticControl"
                : "PublicOrderedAbi1Retained;SelectedUnitsNotOneBindableIndependentChain"] };
        RuntimeLog.TryBackgroundInfo($"simpleFamilyPlanning=true;selectionPolicy={selected.SelectionPolicyId};" +
            $"logicalOrder={string.Join('>', order.Select(family => string.Join('+', family.Coverage)))};" +
            $"privateOrdinalTransport={privateSerial};fusedCandidates={fused.Count};permutationEnumeration=false;" +
            $"privateOrdinalTransportScope={(privateSerial ? completePrivateOrder ? "WholeOrder" : "DensePrefix" : "None")};" +
            $"privateOrdinalFamilyCount={(privateSerial ? privatePrefixCount : 0)};publicTailFamilyCount={(privateSerial ? order.Count - privatePrefixCount : order.Count)};" +
            $"gpuPrefixTransportEnabled={prefixTransportEnabled};" +
            "sparseSuffixQuoteRequired=false;targetKRootHorizonRequired=false;exactTailPriceRequired=false");
        return selected;
    }

    private static bool BindsPrivatePrefix(IReadOnlyList<FilterBodyDescription> order, int length, bool allowPartial) =>
        length is >= 2 and <= 8 && (length == order.Count || allowPartial) &&
        order.Take(length).Select(family => family.FamilyId).Distinct(StringComparer.Ordinal).Count() == length;

    private static (FamilyExecutionPlan Plan, NeowEventFirstDrawGate.OrderedFusion Pair)? SharedRootEntry(
        PreparedSearch request, FamilyExecutionPlan ordinary, IReadOnlyList<FilterBodyDescription> bodies,
        IReadOnlyList<FilterBodyDescription> alternatives, bool allowPartial)
    {
        var order = ordinary.OrderedFamilies;
        int ordinaryPrefix = order.TakeWhile(AutomaticPrivateSerialInvocation.CanBindWholeFamily).Count();
        if (order.Count < 3 || !BindsPrivatePrefix(order, ordinaryPrefix, allowPartial) ||
            order.FirstOrDefault(f => f.FamilyId == "N.Neow" && AutomaticPrivateSerialInvocation.CanBindWholeFamily(f)) is not { } n ||
            order.FirstOrDefault(f => f.FamilyId == EventResultFamily.Id && AutomaticPrivateSerialInvocation.CanBindWholeFamily(f)) is not { } e ||
            ReferenceEquals(order[0], n) && ReferenceEquals(order[1], e) ||
            n.Survival.SurvivalProbability is not (> 0 and < 1) ||
            e.Survival.SurvivalProbability is not (> 0 and < 1)) return null;
        var remaining = order.Where(f => !ReferenceEquals(f, n) && !ReferenceEquals(f, e)).ToArray();
        if (NeowEventFirstDrawGate.TryFusePrefix(request.Model, [n, e, remaining[0]]) is not { } fusion ||
            fusion.Body.Survival.SurvivalProbability is not double nePass) return null;
        var group = fusion.Body;
        int window = ResolveExecutionWindowSize(request, order);
        double roots = Math.Min(window, request.ScanCount);
        if (!request.RunOptions.SkipExactValidation && roots * nePass <= SmallCandidateExactPolicy.MaximumCandidates) return null;

        var costs = GpuCostCalibration.Capture();
        var dense = new FamilyPhysicalQuoteRequest(false, true, roots);
        var first = order[0];
        if (FamilyPhysicalQuote.Computation(first, dense, costs) is not { } firstWork ||
            FamilyPhysicalQuote.Computation(group, dense, costs) is not { } neWork) return null;
        bool neowFirst = ReferenceEquals(first, n);
        // Preserve the existing N-first lookahead. For a competing entry keep the
        // selected independent suffix; the pair has no standalone execution factory.
        var planned = neowFirst
            ? FamilyPlanner.PlanSimple(bodies, alternatives, includeCpuAlternatives: true, denseEntry: group)
            : FamilyPlanner.InOrder([group, ..remaining], resolveInputReferences: true);
        var suffix = planned.Stages.Skip(1).ToArray();
        var next = remaining[0];
        if (suffix.Length == 0 || !ReferenceEquals(next, suffix[0].Family)) return null;
        var logical = new[] { n, e }.Concat(suffix.Select(stage => stage.Family)).ToArray();
        int sharedPrefix = logical.TakeWhile(AutomaticPrivateSerialInvocation.CanBindWholeFamily).Count();
        if (sharedPrefix < 3 || !BindsPrivatePrefix(logical, sharedPrefix, allowPartial) ||
            ResolveExecutionWindowSize(request, logical) != window) return null;
        string evidence;
        if (neowFirst)
        {
            // Give the ordinary order a free later E; compare only the common next operation.
            double nPass = n.Survival.SurvivalProbability.Value;
            var ordinaryNext = new FamilyPhysicalQuoteRequest(true, ordinaryPrefix > 2, roots * nPass, PrivateInput: true);
            var sharedNext = new FamilyPhysicalQuoteRequest(true, sharedPrefix > 3, roots * nePass, PrivateInput: true);
            if (FamilyPhysicalQuote.Computation(next, ordinaryNext, costs, n.Coverage.ToHashSet(StringComparer.Ordinal)) is not { } before ||
                FamilyPhysicalQuote.Computation(next, sharedNext, costs, group.Coverage.ToHashSet(StringComparer.Ordinal)) is not { } after) return null;
            double originalNs = firstWork.NanosecondsPerInput + nPass * before.NanosecondsPerInput;
            double combinedNs = neWork.NanosecondsPerInput + nePass * after.NanosecondsPerInput;
            if (!(combinedNs < originalNs)) return null;
            evidence = FormattableString.Invariant(
                $"SharedRootFirstDrawBefore={next.FamilyId};originalPrefixNsPerRoot={originalNs:G6};sharedPrefixNsPerRoot={combinedNs:G6};ordinaryNextNsPerInput={before.NanosecondsPerInput:G6};sharedNextNsPerInput={after.NanosecondsPerInput:G6};ComputationOnly;OrdinaryLaterEventWorkOmitted;SuffixGreedy");
        }
        else
        {
            double? firstPass = ordinary.Stages[0].Survival;
            if (GreedyUnitOrder.Score(firstWork.NanosecondsPerInput, firstPass) is not double firstScore ||
                GreedyUnitOrder.Score(neWork.NanosecondsPerInput, nePass) is not double sharedScore ||
                !(sharedScore < firstScore) ||
                !request.RunOptions.SkipExactValidation && roots * firstPass <= SmallCandidateExactPolicy.MaximumCandidates)
                return null;
            evidence = FormattableString.Invariant(
                $"SharedRootDenseEntryBefore={first.FamilyId};ordinaryNsPerRejection={firstScore:G6};sharedNsPerRejection={sharedScore:G6};ComputationOnly;RetainedSuffixOrder");
        }
        return (planned with {
            Stages = [..FamilyPlanner.InOrder([n, e]).Stages, ..suffix],
            AllowSmallCandidateExact = ordinary.AllowSmallCandidateExact,
            RankingAuthority = ordinary.RankingAuthority,
            EstimatedTerminalSurvival = planned.Stages.All(stage => stage.Survival.HasValue)
                ? planned.Stages.Aggregate(1d, (fraction, stage) => fraction * stage.Survival!.Value) : null,
            DecisionEvidence = [..(neowFirst ? planned.DecisionEvidence : ordinary.DecisionEvidence), evidence]
        }, fusion);
    }
}
