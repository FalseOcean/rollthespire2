using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

public static partial class FamilyExecutionCoordinator
{
    private static FamilyExecutionPlan PlanSimpleSingleplayer(ExactSearchExecutionRequest request,
        IReadOnlyList<IFamilyInvocation> registered, bool gpuAvailable, bool allowFloat64Units)
    {
        IReadOnlyList<IFamilyInvocation> bodies = gpuAvailable ? registered : CpuAllocation(registered);
        IFamilyInvocation? denseEntry = null;
        var fused = new List<IFamilyInvocation>(2);
        if (gpuAvailable && allowFloat64Units)
        {
            // A coupled Dense-only accelerator stays one whole numerical unit.
            // Its local choice never prices the independent Search suffix.
            denseEntry = FamilyAuxiliaryEvidence.Read<IFamilyInvocation?>(nameof(FamilyExecutionCoordinator),
                "DenseEntryUnit", () =>
                    BonesCapsuleAllocation.TryCreateDefaultUnit(request, bodies, out var bones) ? bones :
                    DirectCapsuleAllocation.TryCreateDefaultUnit(request, bodies, out var direct) ? direct : null,
                null, request.SnapshotFingerprint);
        }
        // Reuse the existing whole numerical bodies. Their complete logical
        // Coverage competes in the same greedy walk as the independent units.
        // Restricted/integer registration must never reconstruct a native body.
        if (denseEntry is null && gpuAvailable && allowFloat64Units && bodies.OfType<NeowFamily>().SingleOrDefault() is { GpuPlan: not null } neow)
        {
            var replay = neow.PricingReplayPlan;
            void AddUnit(string stage, Func<IFamilyInvocation?> create)
            {
                if (FamilyAuxiliaryEvidence.Read<IFamilyInvocation?>(nameof(FamilyExecutionCoordinator), stage,
                    create, null, request.SnapshotFingerprint) is { } unit)
                    fused.Add(unit);
            }
            if (bodies.Any(family => family is TransformationAggregateFamily))
                AddUnit("SimpleNeowTransformationUnit", () =>
                    NeowTransformationComposite.TryCreate(request, replay, out var composite) && composite is not null
                        ? new NeowFamily(request, replay, transformations: composite) : null);
            if (bodies.Any(family => family is CapsuleRelicFamily))
                AddUnit("SimpleNeowCapsuleUnit", () =>
                    NeowCapsuleComposite.TryCreate(request, replay, out var composite) && composite is not null
                        ? new NeowFamily(request, replay, composite: composite) : null);
        }
        FamilyExecutionPlan selected = request.RunOptions.ManualFamilyOrder is { Count: > 0 } manual
            ? FamilyPlanner.InManualOrder(denseEntry is null ? [..fused, ..bodies] : [denseEntry, ..fused, ..bodies],
                bodies, manual, denseEntry)
            : FamilyPlanner.PlanSimple(bodies, fused, includeCpuAlternatives: gpuAvailable, denseEntry: denseEntry);
        var order = selected.OrderedFamilies;
        bool privateSerial = order.Count is >= 2 and <= 8 && order.All(family => family.CanBindPrivateSerial &&
            family.Coverage.Count == 1 && family.Coverage[0] == family.FamilyId) &&
            order.Select(family => family.FamilyId).Distinct(StringComparer.Ordinal).Count() == order.Count;
        if (privateSerial)
        {
            // Transport follows the already selected order. Missing sparse
            // prices cannot remove a filter or promote a different whole chain.
            var invocation = new AutomaticPrivateSerialInvocation(request, order,
                enableSmallCandidateExact: !request.RunOptions.SkipExactValidation, estimationStages: selected.Stages);
            var wrapped = FamilyPlanner.InOrder([invocation]);
            selected = wrapped with
            {
                PlanId = selected.PlanId + ".PrivateOrdinal",
                SelectionPolicyId = selected.SelectionPolicyId,
                RankingAuthority = selected.RankingAuthority,
                AllowSmallCandidateExact = true,
                ExpectedFamilyPipelineMsPerRoot = selected.ExpectedFamilyPipelineMsPerRoot,
                EstimatedTerminalSurvival = selected.EstimatedTerminalSurvival,
                DecisionEvidence = [..selected.DecisionEvidence,
                    "PrivateOrdinalTransportForSelectedWholeFamilyOrder;NoCompleteChainPromotionQuote;LazyStageCreation",
                    "EtaUsesFamilyComputationAndSeparateTransportEnvelope;PrivateTransportOverheadNotMeasured"],
                MissingEvidence = selected.MissingEvidence
            };
        }
        else selected = selected with { DecisionEvidence = [..selected.DecisionEvidence,
            "PublicOrderedAbi1Retained;SelectedUnitsNotOneBindableIndependentChain"] };
        RuntimeLog.TryBackgroundInfo($"simpleFamilyPlanning=true;selectionPolicy={selected.SelectionPolicyId};" +
            $"logicalOrder={string.Join('>', order.Select(family => string.Join('+', family.Coverage)))};" +
            $"privateOrdinalTransport={privateSerial};fusedCandidates={fused.Count};permutationEnumeration=false;" +
            "sparseSuffixQuoteRequired=false;targetKRootHorizonRequired=false;exactTailPriceRequired=false");
        return selected;
    }
}
