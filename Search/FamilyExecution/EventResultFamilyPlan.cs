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
    internal bool NumericalSupported { get; }
    internal bool GpuSupported { get; }
    internal GpuNumericalVariant NumericalVariant { get; }
    internal bool UsesPreparedGroups { get; }
    internal GpuNumericRequirements GpuNumericRequirements => GpuSupported
        ? GpuIntegerNumerics.Requirements(NumericalVariant) : GpuNumericRequirements.None;
    internal string GpuPreparationFailureDetail { get; } = "";
    internal uint[][] Buffers { get; }
    internal FamilySurvivalProjection Survival { get; }
    internal bool HasMorphic => _morphic.Length > 0;
    internal bool HasNewWhitelist => Conditions.Any(c => c.Kind > EventResultConditionKind.MorphicGroveGroupInitialBasicsContains);
    private readonly MorphicGroveContainsPlan?[] _morphic;
    private readonly EventResultSearchCondition[] _ordinary;
    private readonly EventResultNumericPredicate.Compiled _ordinaryNumeric;
    internal EventResultFamilyPlan(SearchModel request, bool prepareGpu = true,
        GpuNumericalVariant numericalVariant = GpuNumericalVariant.NativeFloat64)
    {
        NumericalVariant = numericalVariant;
        Conditions = request.Evaluation.EventResultConditions.Distinct().ToArray();
        // The measured shape has cheap scalar + shared Trash rejection before
        // inventory generation. Target identities do not select the algorithm.
        bool preparedDefault = Conditions.Any(c => c.Kind is EventResultConditionKind.TrialCase or EventResultConditionKind.TinkerTimeTypeAndRider) &&
            Conditions.Any(c => c.Kind is EventResultConditionKind.TrashHeapGrabCard or EventResultConditionKind.TrashHeapDiveRelic) &&
            Conditions.Any(c => c.Kind is EventResultConditionKind.FakeMerchantOfferedFakeRelic or EventResultConditionKind.ColorfulPhilosophersOfferedColor);
        // Freeze the physical selection with this plan; CPU preparation and
        // subsequent environment changes cannot switch a running GPU body.
        UsesPreparedGroups = prepareGpu && (Environment.GetEnvironmentVariable("RT2_E_PREPARATION")?.Trim().ToLowerInvariant() switch {
            null or "" or "default" => preparedDefault,
            "off" => false,
            "groups" => true,
            var unknown => throw new InvalidOperationException("E.UnknownGpuPreparationMode:" + unknown)
        });
        _ordinary = Conditions.Where(c => !EventResultTransformSemantics.IsTransform(c.Kind)).ToArray();
        var morphic = Conditions.Except(_ordinary).ToArray();
        foreach (var c in morphic) MorphicGrovePredictor.ValidateAuthority(request.Detection, request.Authority, c.MorphicGroveScenario!);
        _morphic = morphic.Select(MorphicGroveContainsPlan.Compile).ToArray();
        Authority = Beta111EventResultAuthority.From(request.Authority);
        _ordinaryNumeric = new(_ordinary, Authority);
        PlayerSlot = request.Authority.PlayerSlotIndex;
        bool numericalSupported = Authority.IsBeta111 && (_ordinary.Length == 0 || Authority.StaticCatalogAuthorityExact) &&
            PlayerSlot >= 0 && PlayerSlot < request.Authority.PlayersCount &&
            (!HasMorphic || _morphic.All(p => p is not null)) &&
            Conditions.All(c => Enum.IsDefined(c.Kind)) &&
            (!Conditions.Any(c => c.Kind == EventResultConditionKind.ColorfulPhilosophersOfferedColor) ||
             Authority.ColorfulPoolAuthorityExact && Authority.OwnerCharacterKey.IsValid);
        NumericalSupported = numericalSupported;
        Buffers = [];
        if (prepareGpu && numericalSupported)
        {
            try
            {
                var meta = EventResultGpuPacking.PackForFamily(Conditions, Authority, PlayerSlot, out var colors);
                Buffers = [meta, colors];
                GpuSupported = true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                GpuPreparationFailureDetail = ex.ToString();
                RolltheSpire2.Bootstrap.RuntimeLog.TryBackgroundWarning("eventResultGpuPreparationFailed=true;selected=CpuEventLocal;exceptionJson=" + RolltheSpire2.Bootstrap.RuntimeLog.SafeJson(ex.ToString()));
            }
        }
        Survival = FamilyAuxiliaryEvidence.Read("E.EventResult", "InitialSurvival", () =>
        {
            var model = SearchSelectivityInput.From(request);
            var estimate = EventResultProbabilityEstimator.EstimateBlock(model, Conditions, out string issue);
            return numericalSupported && estimate is { IsPriced: true, Probability: { } p }
                ? FamilySurvivalProjection.Resolved("E.EventResult", p, estimate.EvidenceCode)
                : FamilySurvivalProjection.Unresolved("E.EventResult", "EventResultJointModelUnavailable:" + issue);
        }, FamilySurvivalProjection.Unresolved("E.EventResult", "AuxiliarySurvivalFailed"), request.SnapshotFingerprint);
    }
    internal bool Matches(ulong root) => MatchesObserved(root, null);
    internal bool MatchesObserved(ulong root, Action<EventResultConditionKind, string>? onUnresolved)
    {
        if (!HasMorphic) return NumericalSupported
            ? !EventResultNumericPredicate.RejectsEventResultForFamily(root, _ordinaryNumeric, PlayerSlot) : ReferenceObserved(root, onUnresolved);
        foreach (var plan in _morphic) if (plan is not null && !plan.Matches(root)) return false;
        return _ordinary.Length == 0 || (NumericalSupported
            ? !EventResultNumericPredicate.RejectsEventResultForFamily(root, _ordinaryNumeric, PlayerSlot) : ReferenceObserved(root, onUnresolved));
    }
    internal bool Reference(ulong root) => ReferenceObserved(root, null);
    private bool ReferenceObserved(ulong root, Action<EventResultConditionKind, string>? onUnresolved)
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
            if (precision != PredictionPrecision.Exact)
            {
                onUnresolved?.Invoke(c.Kind, p.EvidenceCode);
                continue;
            }
            if (!pass) return false;
        }
        return true;
    }
    internal string ShaderSource()
    {
        string donor = FamilyGpuComputeUtility.LoadEmbeddedShader("Beta111GpuEventResult.comp.glsl");
        string numerical = donor[donor.IndexOf("uint64_t s0;", StringComparison.Ordinal)..donor.IndexOf("void append_output", StringComparison.Ordinal)];
        if (UsesPreparedGroups) numerical = "#define RT2_E_PREPARED_GROUPS\n" + numerical;
        string source = FamilyGpuComputeUtility.LoadEmbeddedShader("EventResultFamily.comp.glsl").Replace("/*__DONOR__*/", numerical, StringComparison.Ordinal);
        if (NumericalVariant == GpuNumericalVariant.NativeFloat64) return source;
        if (NumericalVariant != GpuNumericalVariant.IntegerRne) throw new InvalidOperationException("E.UnknownGpuNumericalVariant");
        source = GpuIntegerNumerics.NormalizeLines(source);
        const string native = "uint next_int(uint bound) {\n" +
            "    const double unit = 1.1102230246251565e-16lf;\n" +
            "    double unit_sample = double(next_u64() >> 11u) * unit;\n" +
            "    return uint(unit_sample * double(bound));\n}";
        string integer = GpuIntegerNumerics.NextIntSource + "\nuint next_int(uint bound) {\n" +
            "    uint64_t draw = next_u64();\n" +
            "    return rt2_next_int_rne_raw32(uint(draw), uint(draw >> 32u), bound);\n}";
        source = GpuIntegerNumerics.ReplaceUnique(source, native, integer, "E");
        return GpuIntegerNumerics.RequireNoFloat64(GpuIntegerNumerics.RemoveFloat64Extension(source, "E"), "E");
    }
}
