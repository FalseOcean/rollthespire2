using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

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
    internal bool HasMorphic => _morphic.Length > 0;
    internal bool HasNewWhitelist => Conditions.Any(c => c.Kind > EventResultConditionKind.MorphicGroveGroupInitialBasicsContains);
    private readonly MorphicGroveContainsPlan?[] _morphic;
    private readonly EventResultSearchCondition[] _ordinary;
    internal EventResultFamilyPlan(ExactSearchExecutionRequest request)
    {
        Conditions = request.Evaluation.EventResultConditions.Distinct().ToArray();
        _ordinary = Conditions.Where(c => !EventResultTransformSemantics.IsTransform(c.Kind)).ToArray();
        var morphic = Conditions.Except(_ordinary).ToArray();
        foreach (var c in morphic) MorphicGrovePredictor.ValidateAuthority(request.Detection, request.Authority, c.MorphicGroveScenario!);
        _morphic = morphic.Select(MorphicGroveContainsPlan.Compile).ToArray();
        Authority = Beta111EventResultAuthority.From(request.Authority);
        PlayerSlot = request.Authority.PlayerSlotIndex;
        GpuSupported = Authority.IsBeta111 && (_ordinary.Length == 0 || Authority.StaticCatalogAuthorityExact) &&
            PlayerSlot >= 0 && PlayerSlot < request.Authority.PlayersCount &&
            (!HasMorphic || _morphic.All(p => p is not null)) &&
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
    internal bool Matches(ulong root)
    {
        if (!HasMorphic) return GpuSupported
            ? !EventResultNumericPredicate.RejectsEventResultForFamily(root, Conditions, Authority, PlayerSlot) : Reference(root);
        foreach (var plan in _morphic) if (plan is not null && !plan.Matches(root)) return false;
        return _ordinary.Length == 0 || !EventResultNumericPredicate.RejectsEventResultForFamily(root, _ordinary, Authority, PlayerSlot);
    }
    internal bool Reference(ulong root)
    {
        Beta111EventResultProjection? p = null;
        foreach (var c in Conditions)
        {
            if (EventResultTransformSemantics.IsTransform(c.Kind) && c.Kind != EventResultConditionKind.MorphicGroveGroupInitialBasicsContains)
            {
                if (EventResultTransformSemantics.Evaluate(root, c) == MorphicGrovePredicateResult.NoMatch) return false;
                continue;
            }
            if (c.Kind == EventResultConditionKind.TrialCase)
            {
                if (Beta111TrialTinkerProjector.TrialCase(root, PlayerSlot) != (int)c.TrialCase!) return false;
                continue;
            }
            if (c.Kind == EventResultConditionKind.TinkerTimeTypeAndRider)
            {
                if (!Beta111TrialTinkerProjector.TinkerContains(root, PlayerSlot, (int)c.TinkerCardType!, (int?)c.TinkerRider)) return false;
                continue;
            }
            if (c.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains)
            {
                var scenario = c.MorphicGroveScenario!;
                var projection = Beta111MorphicGroveProjector.Project(root, MorphicGroveCommitment.InitialBasics,
                    scenario.Premises, scenario.Targets);
                var match = c.MorphicGroveSecondCard is { } second
                    ? Beta111MorphicGroveProjector.ContainsPair(projection, c.TargetKey, second, false)
                    : Beta111MorphicGroveProjector.Contains(projection, c.TargetKey, false);
                if (match == MorphicGrovePredicateResult.NoMatch) return false;
                continue;
            }
            p ??= Beta111EventResultProjector.Project(root, PlayerSlot, Authority);
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
