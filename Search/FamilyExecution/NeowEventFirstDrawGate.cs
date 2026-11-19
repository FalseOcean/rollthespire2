using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// The event and optional variant use their own first draws from the same local
// root hash. Only ABI1 crosses the dispatch boundary.
internal sealed record NeowEventFirstDrawGate(FamilyFirstDraw Event, FamilyFirstDraw? Variant = null)
{
    internal const string FamilyPrefix = "NE.Neow";
    internal const string CompactSuffix = ".CompactAbi1.20261120.v1";
    internal const string VariantSuffix = ".VFirstDraw.20261120.v1";
    private const string TrackedPositiveSuffix = ".TrackedPositive.20261120.v1";
    internal sealed record OrderedFusion(FilterBodyDescription Body, string EventGroup, string RevisionSuffix, int FamilyCount);
    internal static string Revision(string eventGroup) => $".SharedRoot{eventGroup}FirstDraw.20261119.v1";
    internal static bool AutoPriceEnabled =>
        !string.Equals(Environment.GetEnvironmentVariable("RT2_NE_AUTO_PRICE"), "off", StringComparison.OrdinalIgnoreCase);

    internal static OrderedFusion? TryFusePrefix(SearchModel model, FilterBodyDescription[] families)
    {
        if (!HasEventPair(model, families)) return null;
        return PrepareDensePair(model, families);
    }

    internal static OrderedFusion? TryFuseOrdered(SearchModel model, FilterBodyDescription[] families,
        OrderedFusion? preparedPair, bool compactInput)
    {
        if (!HasEventPair(model, families)) return null;
        var n = families[0]; var e = families[1];
        if (compactInput)
            return FamilyAuxiliaryEvidence.Read<OrderedFusion?>(nameof(NeowEventFirstDrawGate), "OptionalCompactSharedRootPreparation",
                () => Prepare(model, n, e, families.Length > 2, true), null, model.SnapshotFingerprint);

        if (families.Length > 2 && families[2] is { FamilyId: VariantOpeningFamily.Id,
            Coverage: [VariantOpeningFamily.Id], FirstDraw: not null, UsesGpu: true, CanBindPrivateSerial: true,
            RequiredPassedCoverage: null } variant &&
            variant.Preparation.Issues.Count == 0 && variant.Preparation.Remainders.Count == 0 &&
            variant.Preparation.EffectiveObligations.ToArray() is [{ Domain: VariantOpeningFamily.Id, PlayerSlot: null, ThroughOrdinal: null }] &&
            EventPairSurvival(n, e) is double pass)
        {
            var combined = FamilyAuxiliaryEvidence.Read<OrderedFusion?>(nameof(NeowEventFirstDrawGate), "OptionalVariantFirstDrawPreparation",
                () => VariantPaysForFirstDraw(n, e, variant, pass, families.Length > 3)
                    ? Prepare(model, n, e, families.Length > 3, false, variant) : null,
                null, model.SnapshotFingerprint);
            if (combined is not null) return combined;
        }
        return preparedPair ?? PrepareDensePair(model, families);
    }

    private static OrderedFusion? PrepareDensePair(SearchModel model, FilterBodyDescription[] families) =>
        FamilyAuxiliaryEvidence.Read<OrderedFusion?>(nameof(NeowEventFirstDrawGate), "OptionalSharedRootPreparation",
            () => Prepare(model, families[0], families[1], families.Length > 2, false), null, model.SnapshotFingerprint);

    private static bool HasEventPair(SearchModel model, FilterBodyDescription[] families)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("RT2_NE_SHARED_HASH"), "off", StringComparison.OrdinalIgnoreCase) ||
            families is not [{ FamilyId: "N.Neow", Coverage: ["N.Neow"] } n,
                { FamilyId: EventResultFamily.Id, Coverage: [EventResultFamily.Id], FirstDraw: not null } e, ..] ||
            n.PlayersCount != 1 || !n.UsesGpu || !e.UsesGpu ||
            n.NumericRequirements != (GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64) ||
            e.NumericRequirements != (GpuNumericRequirements.Int64 | GpuNumericRequirements.Float64) ||
            model.Evaluation.EventResultConditions is not [var condition]) return false;
        return condition.Kind is EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.TrashHeapGrabCard ||
            !string.Equals(Environment.GetEnvironmentVariable("RT2_NE_EVENT_FIRST_DRAW"), "off", StringComparison.OrdinalIgnoreCase);
    }

    private static double? EventPairSurvival(FilterBodyDescription neow, FilterBodyDescription events) =>
        neow.Survival.SurvivalProbability is double n && events.Survival.SurvivalProbability is double e ? n * e : null;

    private static bool VariantPaysForFirstDraw(FilterBodyDescription neow, FilterBodyDescription events,
        FilterBodyDescription variant, double pass, bool hasFollowingStage)
    {
        if (pass is <= 0 or > 1) return false;
        var geometry = new FamilyPhysicalQuoteRequest(true, hasFollowingStage, PrivateOrdinalBuffer.Capacity * pass, PrivateInput: true);
        var costs = GpuCostCalibration.Capture();
        var passed = neow.Coverage.Concat(events.Coverage).ToHashSet(StringComparer.Ordinal);
        // Compare calculation under the existing first-draw and V reference prices.
        // Conservatively hold the pair's device output unchanged.
        return FamilyPhysicalQuote.Computation(variant, geometry, costs, passed) is { } next &&
            FirstDrawIncrement(pass, costs.GlobalRatio) < pass * next.NanosecondsPerInput;
    }

    private static OrderedFusion? Prepare(SearchModel model, FilterBodyDescription n, FilterBodyDescription e,
        bool hasFollowingStage, bool compactInput, FilterBodyDescription? variant = null)
    {
        var replay = NeowReplayPlan.Compile(model, false);
        if (!replay.AuditedModern113IdentityReplay || replay.FilterRemainders.Length != 0 ||
            !NeowSingleplayerGpuPlan.TryCreate(replay, out var gpu, out _) || gpu is null) return null;
        bool trackedPositive = !gpu.TryCompileTerminalCurseGate(out _);
        if (trackedPositive && (n.Preparation.HasConditionalProjections ||
            n.RequiredPassedCoverage is not null || e.RequiredPassedCoverage is not null ||
            !gpu.CanShareTrackedPositive)) return null;
        var draw = e.FirstDraw!;
        var combined = gpu with { EventFirstDraw = new(draw, variant?.FirstDraw) };
        var options = NeowGpuOptimizations.Capture();
        string familyId = FamilyPrefix + draw.Group;
        string[] coverage = variant is null ? [..n.Coverage, ..e.Coverage] : [..n.Coverage, ..e.Coverage, ..variant.Coverage];
        var obligations = n.Preparation.EffectiveObligations.Concat(e.Preparation.EffectiveObligations);
        if (variant is not null) obligations = obligations.Concat(variant.Preparation.EffectiveObligations);
        double? pass = EventPairSurvival(n, e);
        if (variant is not null) pass *= variant.ResolveSurvival(n.Coverage.Concat(e.Coverage).ToHashSet(StringComparer.Ordinal)).SurvivalProbability;
        var prep = new PreparedFilter(familyId)
        {
            Coverage = coverage,
            Obligations = obligations.ToArray(),
            Remainders = [..n.Preparation.Remainders, ..e.Preparation.Remainders],
            Survival = pass is double rate
                ? FamilySurvivalProjection.Resolved(familyId, rate, variant is null ? "ExistingIndependentEventStreamEstimate" : "EventAndConditionalVariantEstimate")
                : FamilySurvivalProjection.Unresolved(familyId, "MissingComponentSurvival")
        };
        string revisionSuffix = Revision(draw.Group) + (compactInput ? CompactSuffix : variant is not null ? VariantSuffix : "") +
            (trackedPositive ? TrackedPositiveSuffix : "");
        string revision = n.PhysicalRevision + revisionSuffix;
        var body = new FilterBodyDescription(prep, revision, n.ProfileId)
        {
            InputIdentity = n.InputIdentity, UsesGpu = true, InputModes = compactInput ? FilterInputModes.CompactAbi1 : FilterInputModes.Dense,
            PlayersCount = 1, CpuWorkers = n.CpuWorkers, WindowCapacity = n.WindowCapacity,
            NumericRequirements = n.NumericRequirements | e.NumericRequirements,
            Cost = new() {
                Quote = (_, geometry, passed) => compactInput || gpu.UseDirectFirstDraw || trackedPositive
                    ? Quote(familyId, n, variant is null ? null : e, compactInput, geometry, passed) : null
            },
            PerformanceProjection = _ => new(prep.FamilyId, revision,
                "SharedRoot.NeowThen" + draw.Group, 1, variant is null ? "CombinedDispatch" : "CombinedDispatchWithVariant", true),
            BindPrivate = (rd, input, output) => NeowPrivateGpuBinding.Bind(combined, options, rd, input, output),
            BindPrivateShared = compactInput ? null :
                (rd, input, output, scratch) => NeowPrivateGpuBinding.Bind(combined, options, rd, input, output, scratch),
            SupportsDenseBatchGrouping = !compactInput && hasFollowingStage && n.SupportsDenseBatchGrouping
        };
        return new(body, draw.Group, revisionSuffix, variant is null ? 2 : 3);
    }

    private static double Active32(double pass) => 1 - Math.Pow(1 - pass, 32);

    private static double FirstDrawIncrement(double pass, double gpuCostRatio) =>
        Active32(pass) * NeowWorkEstimate.DirectGpuDrawNanoseconds * gpuCostRatio;

    private static FamilyPhysicalQuote? Quote(string familyId, FilterBodyDescription neow, FilterBodyDescription? eventsBeforeVariant,
        bool compactInput, FamilyPhysicalQuoteRequest geometry, IReadOnlySet<string> passedCoverage)
    {
        if (!FamilyPhysicalQuote.AdmittedRequest(geometry) || geometry.CompactInput != compactInput ||
            geometry.PrivateInput != compactInput || neow.ResolveSurvival(passedCoverage).SurvivalProbability is not double neowPass ||
            FamilyPhysicalQuote.Computation(neow, geometry, geometry.CostSnapshot, passedCoverage) is not { NanosecondsPerInput: > 0 } entry)
            return null;
        double? variantPass = eventsBeforeVariant is null ? null :
            neowPass * eventsBeforeVariant.ResolveSurvival(passedCoverage).SurvivalProbability;
        if (eventsBeforeVariant is not null && !variantPass.HasValue) return null;
        // The first-draw arithmetic is the only added numerical cost. The N
        // quote uses this invocation's input geometry and actual output head.
        double eventNs = FirstDrawIncrement(neowPass, entry.GpuCostRatio);
        double variantNs = variantPass is double pass ? FirstDrawIncrement(pass, geometry.CostSnapshot?.GlobalRatio ?? 1) : 0;
        var quote = FamilyCoarsePricing.Gpu(familyId, variantPass.HasValue ? "NEV.SharedRootFirstDraw" : "NE.SharedRootFirstDraw",
            entry.NanosecondsPerInput + eventNs + variantNs,
            entry.WindowCapacity, geometry,
            FormattableString.Invariant($"OneRootDecodeAndHash;OneExtraIndependentFirstDraw;Active32={Active32(neowPass):G9};EventArithmeticNs={eventNs:G9};") +
            (variantPass.HasValue ? FormattableString.Invariant($"VariantActive32={Active32(variantPass.Value):G9};VariantArithmeticNs={variantNs:G9};") : "") +
            $"SameRoleNReference;CompactInput={geometry.CompactInput};PrivateInput={geometry.PrivateInput};PrivateOutput={geometry.PrivateOutput};" +
            "DeviceEmissionOnce;NoNewMeasurement;NReference=" + entry.Evidence,
            entry.SetupMilliseconds, entry.OutputElementBytes, entry.OutputAlreadyOrdered, entry.PublicTransportClass);
        return quote is null ? null : quote with { LocalCostSource = "ComponentComposition", GpuCostRatio = entry.GpuCostRatio,
            LocalExecutorCostRatio = entry.LocalExecutorCostRatio };
    }

    internal string Apply(string source)
    {
        const string marker = "/*__RT2_NE_SHARED_EVENT__*/";
        if (!source.Contains(marker, StringComparison.Ordinal))
            throw new InvalidOperationException("NE.SharedRootShaderSeamChanged");
        string helper = "#define RT2_NE_SHARED_EVENT\n";
        if (Variant is { } variant)
            helper += FirstDrawSource("shared_variant_matches", variant, variant.AcceptedRanks.GlslExpression("m"));
        string accepts = Event.AcceptedRanks.GlslExpression("m");
        if (Variant is not null) accepts = $"({accepts}) && shared_variant_matches(root)";
        helper += FirstDrawSource("shared_event_matches", Event, accepts);
        return source.Replace(marker, helper, StringComparison.Ordinal);
    }

    private static string FirstDrawSource(string name, FamilyFirstDraw draw, string accepts)
    {
        // splitmix_next adds one increment. Start one increment ahead to read
        // xoshiro's initial s1, the sole word used by its first star-star draw.
        ulong mixOffset = unchecked(draw.StreamOffset + 0x9e3779b97f4a7c15UL);
        string offset = FormattableString.Invariant($"make_u64(0x{(uint)mixOffset:x8}u,0x{(uint)(mixOffset >> 32):x8}u)");
        return $$"""
            bool {{name}}(uint64_t root) {
                uint64_t state = root + {{offset}};
                uint64_t s1 = splitmix_next(state);
                uint64_t m = (rotl64(s1 * uint64_t(5u), 7u) * uint64_t(9u)) >> 11u;
                return {{accepts}};
            }

            """;
    }
}
