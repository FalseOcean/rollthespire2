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
        if (denseEntry is null && gpuAvailable && allowFloat64Units && bodies.Any(body => body.FamilyId == "N.Neow" && body.UsesGpu))
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
                    NeowTransformationComposite.TryCreate(model, replay, out var composite) && composite is not null
                        ? new NeowFamily(model, replay, transformations: composite).Describe(preferences) : null);
            if (replay is not null && bodies.Any(family => family.FamilyId == "R.Relic") && NeowReplayPlan.HasCapsule(model))
                AddUnit("SimpleNeowCapsuleUnit", () =>
                    NeowCapsuleComposite.TryCreate(model, replay, out var composite) && composite is not null
                        ? new NeowFamily(model, replay, composite: composite).Describe(preferences) : null);
        }
        FamilyExecutionPlan selected = request.RunOptions.ManualFamilyOrder is { Count: > 0 } manual
            ? FamilyPlanner.InManualOrder(denseEntry is null ? [..fused, ..bodies] : [denseEntry, ..fused, ..bodies],
                bodies, manual, denseEntry)
            : FamilyPlanner.PlanSimple(bodies, fused, includeCpuAlternatives: gpuAvailable, denseEntry: denseEntry);
        var order = selected.OrderedFamilies;
        int privatePrefixCount = order.TakeWhile(family => family.CanBindPrivateSerial &&
            family.Coverage.Count == 1 && family.Coverage[0] == family.FamilyId).Count();
        bool completePrivateOrder = privatePrefixCount == order.Count;
        // This diagnostic control disables only the new partial-prefix transport;
        // the existing complete-order default remains unchanged.
        bool prefixTransportEnabled = !string.Equals(Environment.GetEnvironmentVariable("RT2_GPU_PREFIX_TRANSPORT"),
            "off", StringComparison.OrdinalIgnoreCase);
        bool privateSerial = privatePrefixCount is >= 2 and <= 8 &&
            (completePrivateOrder || prefixTransportEnabled) &&
            order.Take(privatePrefixCount).Select(family => family.FamilyId).Distinct(StringComparer.Ordinal).Count() == privatePrefixCount;
        if (privateSerial)
        {
            // Transport follows the already selected order. Missing sparse
            // prices cannot remove a filter or promote a different whole chain.
            var invocation = AutomaticPrivateSerialInvocation.Prepare(model, preferences, order.Take(privatePrefixCount).ToArray(),
                enableSmallCandidateExact: !request.RunOptions.SkipExactValidation,
                estimationStages: selected.Stages.Take(privatePrefixCount).ToArray(),
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
}
