using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Canonical event-result facts are root-local, independent of W occurrence.
/// Shared Trash Grab/Dive facts retain their joint probability; distinct event
/// groups use the existing assumed-independent probability model, not runtime ratios.
/// </summary>
internal sealed class EventResultFamilyPlan
{
    internal int Capacity { get; } = 1 << 20;
    internal EventResultSearchCondition[] Conditions { get; }
    internal Beta111EventResultAuthority Authority { get; }
    internal int PlayerSlot { get; }
    internal bool GpuSupported { get; }
    internal uint[][] Buffers { get; }
    internal FamilySurvivalProjection Survival { get; }
    internal EventResultFamilyPlan(ExactSearchExecutionRequest request)
    {
        Conditions = request.Evaluation.EventResultConditions.Distinct().ToArray();
        Authority = Beta111EventResultAuthority.From(request.Authority);
        PlayerSlot = request.Authority.PlayerSlotIndex;
        GpuSupported = Authority.IsBeta111 && Authority.StaticCatalogAuthorityExact && PlayerSlot >= 0 &&
            Conditions.All(c => Enum.IsDefined(c.Kind)) &&
            (!Conditions.Any(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor) ||
             Authority.ColorfulPoolAuthorityExact && Authority.OwnerCharacterKey.IsValid);
        var meta = EventResultGpuPacking.PackForFamily(Conditions, Authority, PlayerSlot, out var colors);
        Buffers = [meta, colors];
        var model = SearchSelectivityInput.From(request);
        var estimate = EventResultProbabilityEstimator.EstimateBlock(model, Conditions, out string issue);
        Survival = GpuSupported && estimate is { IsPriced: true, Probability: { } p }
            ? FamilySurvivalProjection.Resolved("E.EventResult", p, estimate.EvidenceCode)
            : FamilySurvivalProjection.Unresolved("E.EventResult", "EventResultJointModelUnavailable:" + issue);
    }
    internal bool Matches(ulong root) => GpuSupported
        ? !EventResultNumericPredicate.RejectsEventResultForFamily(root, Conditions, Authority, PlayerSlot)
        : Reference(root);
    internal bool Reference(ulong root)
    {
        var p = Beta111EventResultProjector.Project(root, PlayerSlot, Authority);
        foreach (var c in Conditions)
        {
            var (precision, pass) = c.Kind switch
            {
                EventResultConditionKind.TrashHeapGrabCard => (p.TrashHeapPrecision, p.TrashHeapGrabCard == c.TargetKey),
                EventResultConditionKind.TrashHeapDiveRelic => (p.TrashHeapPrecision, p.TrashHeapDiveRelic == c.TargetKey),
                EventResultConditionKind.FakeMerchantOfferedFakeRelic => (p.FakeMerchantPrecision, p.FakeMerchantInventory.Contains(c.TargetKey)),
                EventResultConditionKind.ColorfulPhilosophersOfferedColor => (p.ColorfulPrecision, p.ColorfulOfferedColors.Contains(c.TargetKey)),
                _ => throw new InvalidOperationException("E.UnknownPredicate")
            };
            if (precision != PredictionPrecision.Exact) throw new InvalidOperationException("E.ReferenceAuthorityUnavailable:" + p.EvidenceCode);
            if (!pass) return false;
        }
        return true;
    }
    internal string ShaderSource()
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string numerical = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..donor.IndexOf("void append_output", StringComparison.Ordinal)];
        return FamilyGpuComputeUtility.LoadEmbeddedShader("EventResultFamily.comp.glsl").Replace("/*__DONOR__*/", numerical, StringComparison.Ordinal);
    }
}
