using RolltheSpire2.Bootstrap;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Compatibility;
using NumericRequirements = RolltheSpire2.Search.FamilyExecution.GpuNumericRequirements;
namespace RolltheSpire2.Search.FamilyExecution;

// Immutable N-domain preparation. No selected executor, device, execution fault
// or live cost sample belongs to this object.
internal sealed partial class NeowFamily
{
    internal const string PartyOrderedKRevision = ".PartyOrderedKFirstReject.20261016.v1";
    internal delegate bool GpuPlanFactory(NeowReplayPlan plan, out NeowFamilyGpuPlan? gpu, out string issue);
    internal bool GpuPreparationFailed { get; }
    internal string GpuPreparationFailureDetail { get; } = "";
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
    private readonly NeowGpuOptimizations _gpuOptimizations = NeowGpuOptimizations.Capture();
    private readonly IRuntimeProfile _profile;
    private readonly string _inputIdentity;
    private readonly FilterPricingAuthority _pricingAuthority;
    private readonly bool _sharedNeow, _sharedIdentityOnly, _canonicalScope;
    private readonly NeowReplayPlan _plan;
    private readonly NeowWorkEstimate.Work _workEstimate;
    private readonly Func<ulong,bool> _cpuMatcher;
    private readonly NeowCapsuleSparseRefinement? _sparseRefinement;
    private bool HasCapsuleNicheContinuation => _plan.CapsuleNicheContinuation is not null;
    private readonly NeowCpuPositiveIdentityPlan? _cpuPositiveIdentity;
    private readonly NeowFamilyProjections _models;
    private readonly bool _conditional;
    private readonly NeowFamilyGpuPlan? _gpuPlan;
    private readonly NeowCapsuleComposite? _composite;
    private readonly NeowTransformationComposite? _transformations;
    private readonly double? _transformationJoint, _givenTransformations;
    internal NeowReplayPlan Replay => _plan;
    internal NeowFamilyGpuPlan? GpuPlan => _gpuPlan;
    internal NeowCapsuleComposite? Composite => _composite;
    internal NeowTransformationComposite? Transformations => _transformations;
    internal FilterPricingAuthority PricingAuthority => _pricingAuthority;
    internal bool UsesSmallPrivateOutput => _composite is null && _transformations is null && NeowFamilyGpuExecutor.SupportsSmallOutput(_gpuPlan);
    internal bool UsesCompatibilityGpu => _gpuPlan?.NumericalVariant == NeowGpuNumericalVariant.IntegerUnitFloat;
    private string CpuRevision => "N.Neow.Cpu.LocalReplay.20261015.v3" +
        (_cpuPositiveIdentity is null ? "" : NeowCpuPositiveIdentityPlan.Revision) + NeowIdentityOfferFacts.Revision +
        (_plan.Authority.UsesChildShuffle ? ".Modern113" : "") + _plan.CapsuleNonCombatRevision +
        (HasCapsuleNicheContinuation ? NeowCapsuleNicheContinuation.Revision : "") +
        (_sparseRefinement is null ? "" : NeowCapsuleSparseRefinement.Revision);
    private NeowGpuOptimizations OptimizationsFor(bool compact) => _gpuPlan is null ? default : _gpuOptimizations.Applicable(_gpuPlan, compact,
        _composite is null && _transformations is null && _gpuPlan.DirectNestedMode == 0 && _gpuPlan.JointTransformMetaOffset < 0);
    internal NeowGpuOptimizations WorkEstimateOptimizations(bool compact) => OptimizationsFor(compact);
    internal string OptimizationShape(bool compact) => _gpuPlan is null ? "" : OptimizationsFor(compact).DescribeWork(_gpuPlan,compact);
    internal NeowFamily(SearchModel request, NeowReplayPlan plan, bool preferGpu = true, NeowCapsuleComposite? composite = null,
        NeowTransformationComposite? transformations = null, GpuPlanFactory? gpuPlanFactory = null)
    {
        _profile = RuntimeProfileRegistry.Select(request.Detection); _inputIdentity = request.SnapshotFingerprint;
        _pricingAuthority = FilterPricingAuthority.From(request);
        _canonicalScope = !plan.Authority.UsesChildShuffle && request.Authority.PlayersCount==1 && request.CompiledSearch.Context.Party is null &&
            request.CompiledSearch.NormalizedQuery.StandardMaps.Count==0 && request.CompiledSearch.NormalizedQuery.StandardMapRoutes.Count==0;
        _sharedNeow = Search.Semantics.TransformationAggregateCondition.HasSharedNeow(request.Evaluation);
        _sharedIdentityOnly = Search.Semantics.TransformationAggregateCondition.SharedNeowIdentityOnly(request.Evaluation);
        _plan = plan; _transformations = transformations;
        if(composite is null && transformations is null && plan.CapsuleNicheContinuation is not null)
        {
            string sparseMode=NeowCapsuleSparseRefinement.CaptureSelection();
            bool selected=NeowCapsuleSparseRefinement.ShouldApply(sparseMode,plan);
            if(selected) _sparseRefinement=NeowCapsuleSparseRefinement.Compile(request,plan);
            RuntimeLog.TryBackgroundInfo($"capsuleSparseRefinementPrepared={selected};mode={sparseMode};default=auto;selector=FixedSmallNewLeafOrDualCapsuleFinal;scope=PublicNOnly;ready={_sparseRefinement?.Ready==true};issue={_sparseRefinement?.Issue ?? ""};privateOrCoupledRefinement=false");
        }
        _workEstimate = NeowWorkEstimate.Describe(plan);
        var neowMatcher = NeowFamilyReplay.Bind(plan);
        if(HasCapsuleNicheContinuation && NeowCpuPreGate.TryCompile(plan) is { } identityGate)
        {
            var routeMatcher=neowMatcher;
            neowMatcher=root=>identityGate.PassesIdentity(root) && routeMatcher(root);
        }
        _cpuPositiveIdentity = !HasCapsuleNicheContinuation && composite is null && transformations is null
            ? NeowCpuPositiveIdentityPlan.TryCompile(plan, NeowCpuPositiveIdentityPlan.Capture()) : null;
        _cpuMatcher = transformations is null ? neowMatcher : root => neowMatcher(root) && transformations.Numerical.Matches(root);
        string physicalIssue = "CpuReferenceSelected";
        if (preferGpu && transformations is null)
        {
            try { (gpuPlanFactory ?? NeowFamilyGpuPlan.TryCreate)(plan, out _gpuPlan, out physicalIssue); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _gpuPlan = null; GpuPreparationFailed = true; physicalIssue = "GpuPlanConstructionFailed";
                GpuPreparationFailureDetail = ex.ToString();
                RuntimeLog.TryBackgroundWarning($"nGpuPreparationFailed=true;slot={plan.Authority.PlayerSlotIndex};selected=CpuLocalReplay;scope=N.Neow;exactOnly={string.Join(',', plan.ExactOnly)};exceptionJson=" + RuntimeLog.SafeJson(ex.ToString()));
            }
        }
        if (transformations is not null)
        {
            if (!preferGpu || composite is not null) throw new InvalidOperationException("NJointTransformPhysicalConflict");
            _gpuPlan = transformations.Gpu;
        }
        if (composite is not null && _gpuPlan is null) throw new InvalidOperationException("NrRequiresGpuPhysical");
        if (_gpuPlan is { NumericalVariant: not NeowGpuNumericalVariant.NativeFloat64 } &&
            (composite is not null || transformations is not null || _gpuPlan.JointTransformMetaOffset >= 0))
            throw new InvalidOperationException("N.IntegerUnitFloat.OrdinaryInvocationOnly");
        _composite = composite;
        _models = new(request, plan, false, null, _gpuPlan is null ? CpuFilterExecution.Capacity : NeowFamilyGpuExecutor.Capacity);
        if (Search.Semantics.TransformationAggregateCondition.HasSharedNeow(request.Evaluation))
        {
            var numerical = transformations?.Numerical ?? new TransformationAggregateNumericalPlan(request);
            _transformationJoint = plan.ExactOnly.Length == 0 ? TransformationAggregateProbability.SharedNeowJoint(request, numerical) : null;
            _givenTransformations = TransformationAggregateProbability.Conditional(_transformationJoint,
                TransformationAggregateProbability.Build(numerical).StageSurvival);
        }
        _conditional = NeowReplayPlan.HasCapsule(request);
        if(plan.CapsuleNicheContinuation is { } continuation)
            RuntimeLog.TryBackgroundInfo($"nCapsuleNicheContinuation=true;scope=SoloN;revision={NeowCapsuleNicheContinuation.Revision};routeWitness=OneAdvanceCombination;bagPredicateOwner=R;remainders={string.Join(',',continuation.Remainders)}");
        if (plan.AuthoredUpgrades is { } upgrades)
            RuntimeLog.TryBackgroundInfo($"nAuthoredUpgradeContinuation=true;scope=AuthoredOnly;hiddenWwpReplay=false;constants={string.Join(',', upgrades.Advances)};unknownConstant=-1;unknownRoute={(plan.CapsuleUpgradeUpperBound >= 0 ? "BoundedDrawUnion" : "IdentityOnlyExactRemainder")};choicePolicy=AuthoredTakeOtherwiseSkip");
        if (_gpuOptimizations != default) RuntimeLog.TryBackgroundInfo($"nOptimizationsRequested=true;requested={_gpuOptimizations.Revision};dense={OptimizationsFor(false).Revision};compact={OptimizationsFor(true).Revision};productionDefaults=true;quote=MatchingCurrentBodyReferenceOrEstimatedOwnWork");
        RuntimeLog.TryBackgroundInfo($"nFamilyReady=true;coverage={string.Join(',', Coverage)};physical={(_composite is not null ? "NrTargetRankComposite" : _gpuPlan is null ? "CpuLocalReplay" : "GpuLocalDonor")};physicalIssue={physicalIssue};capsuleBagScratch=false;capsuleSemanticOwner=R;boundedBonesNr={_composite?.UsesBonesCheckpoint == true};exactOnly={string.Join(',', plan.ExactOnly)}");
    }
    public string FamilyId => "N.Neow";
    public IReadOnlyList<string> Coverage => _transformations is not null ? [FamilyId, TransformationAggregateFamily.Id] :
        _composite is null ? [FamilyId] : [FamilyId, "R.Relic"];
    public FamilyAnalyticalCostProjection AnalyticalCost => HasCapsuleNicheContinuation
        ? new(FamilyId,PreferredExecutionWindowSize,[],"SoloCapsuleNicheWorkUnpriced") : _composite is null ? _models.Full : new(FamilyId,
        NeowFamilyGpuExecutor.Capacity, _models.Full.Terms.Concat(_composite.Projection.Full.Terms),
        "CompositeConservativeFullEnvelope_NPlusR;ExpectedTargetRankReachUnpriced");
    public FamilySurvivalProjection Survival => ResolveSurvival(Empty);
    public bool HasConditionalProjections => _conditional || _sharedNeow;
    public FamilySurvivalProjection ResolveSurvival(IReadOnlySet<string> passedCoverage)
    {
        if (HasCapsuleNicheContinuation)
            return FamilySurvivalProjection.Unresolved(FamilyId,"SoloCapsuleNicheJointProbabilityUnpriced");
        if (_transformations is not null) return _transformationJoint is { } joint
            ? FamilySurvivalProjection.Resolved(FamilyId, joint, "NJointAggregate;ExistingSameDrawNAndTModel;IdentityOnce")
            : FamilySurvivalProjection.Unresolved(FamilyId, "NJointAggregate.JointModelUnavailable");
        if (_sharedNeow && passedCoverage.Contains(TransformationAggregateFamily.Id))
            return _sharedIdentityOnly
                ? FamilySurvivalProjection.Resolved(FamilyId, 1, "SharedNIdentityAlreadyPassedByT")
                : _givenTransformations is { } conditional
                    ? FamilySurvivalProjection.Resolved(FamilyId, conditional, "N.GivenT;ExistingSameDrawJointOverTMarginal")
                    : FamilySurvivalProjection.Unresolved(FamilyId, "T+N.AdditionalPredicateJointUnknown");
        if (_composite is null) return _models.Survival(passedCoverage);
        double? n = _models.Survival(Empty).SurvivalProbability;
        double? r = _composite.Projection.Survival(new HashSet<string> { FamilyId }).SurvivalProbability;
        return n.HasValue && r.HasValue ? FamilySurvivalProjection.Resolved(FamilyId, n.Value*r.Value, "CompositeCoverage=N.Neow,R.Relic;RConditionalOnN")
            : FamilySurvivalProjection.Unresolved(FamilyId, "CompositeJointSurvivalUnavailable");
    }
    public FamilyExpectedFilteringCostProjection ExpectedFilteringCost => ResolveExpectedFilteringCost(Empty);
    public FamilyExpectedFilteringCostProjection ResolveExpectedFilteringCost(IReadOnlySet<string> passedCoverage) => _transformations is not null
        ? new(FamilyId, _models.Full.WorkUnitsPerInput, null, _transformationJoint, 1, [], "NJointAggregate.NoPrimitiveLedger;UseOwnedPhysicalQuote") :
        HasCapsuleNicheContinuation
        ? new(FamilyId, 0, null, null, 1, [], "SoloCapsuleNicheWorkAndJointProbabilityUnpriced") : _composite is null
        ? _models.Expected(passedCoverage) : new(FamilyId, _models.Full.WorkUnitsPerInput + _composite.Projection.Full.WorkUnitsPerInput,
            null, ResolveSurvival(passedCoverage).SurvivalProbability, 1, [], "CompositeTargetRankReachLedgerNotYetPriced");
    internal int PreferredExecutionWindowSize => _gpuPlan is null ? CpuFilterExecution.Capacity : NeowFamilyGpuExecutor.Capacity;
    public FamilyConditionPerformanceProjection ConditionPerformance => ResolveConditionPerformance(false);
    public FamilyConditionPerformanceProjection ResolveConditionPerformance(bool compactAbi1Input)
    {
        if (_gpuPlan is null) return CpuFilterExecution.Describe(FamilyId, CpuRevision, 1, CpuFilterExecution.Capacity, false, false, compactAbi1Input);
        string revision = _transformations is not null
            ? NeowTransformationComposite.Revision + (compactAbi1Input ? ".Compact" : _gpuPlan.UsesStagedDense ? ".StagedDense" : ".Dense")
            : _composite is not null
            ? _composite.Revision(compactAbi1Input, _gpuPlan.UsesStagedDense)
            : _gpuPlan.DirectNestedMode != 0 ? _gpuPlan.DirectNestedRevision(compactAbi1Input)
            : _gpuPlan.UsesLeafyPreGate
                ? compactAbi1Input ? NeowFamilyGpuPlan.LeafyCompactRevision :
                    _gpuPlan.UsesStagedDense ? NeowFamilyGpuPlan.LeafyStagedDenseRevision : NeowFamilyGpuPlan.LeafyDenseRevision
                : compactAbi1Input ? NeowFamilyGpuPlan.CompactRevision :
                    _gpuPlan.UsesStagedDense ? NeowFamilyGpuPlan.StagedDenseRevision : NeowFamilyGpuPlan.DenseRevision;
        if (!_gpuPlan.IsMultiplayer) revision += ".PackedPublicOrdinals.20260930.v1";
        if (!_gpuPlan.IsMultiplayer && (_gpuPlan.Meta[82] & 9u) != 0)
            revision += ".ZeroUsedDirectIndex.20260930.v1";
        if (!_gpuPlan.IsMultiplayer && _plan.StructuredConditions.Any(c => c.SourceRelicId == Beta110FastRelicCatalog.Kaleidoscope))
            revision += ".KaleidoscopeFirstGroupReject.20260930.v1";
        if (_gpuPlan.IsMultiplayer && _plan.StructuredConditions.Any(c => c.OrderedKaleidoscope))
            revision += PartyOrderedKRevision;
        if (UsesCompatibilityGpu) revision += ".IntegerUnitFloat.Rne.20261025.v1";
        if (_gpuPlan.UsesChildShuffle) revision += ".Modern113";
        revision += _plan.CapsuleNonCombatRevision;
        revision += OptimizationsFor(compactAbi1Input).Revision;
        revision += _gpuPlan.EntryIdentityRevision;
        revision += NeowIdentityOfferFacts.Revision + GpuRngRotation.Revision;
        if(HasCapsuleNicheContinuation) revision += NeowCapsuleNicheContinuation.Revision;
        if(_sparseRefinement is not null) revision += NeowCapsuleSparseRefinement.Revision;
        return new(FamilyId, revision, "N.Neutral.20260905.v1", 1, UsesCompatibilityGpu
            ? "NoAcceptedWithinPathCurve;GpuNumericalVariant=IntegerUnitFloat;PhysicalQuoteUnavailable=UnmeasuredVariant"
            : "NoAcceptedWithinPathCurve");
    }
    public FamilyPhysicalQuote? QuoteRaw(FamilyPhysicalQuoteRequest request)
    {
        if (HasCapsuleNicheContinuation) return null;
        // Exact finite evidence keeps its original identity. A new ordinary body
        // may instead consume a clearly estimated N-only work reference. Joint
        // owners retain their own existing boundary, never an ordinary N price.
        FamilyPhysicalQuote? quote = null;
        if (quote is null)
        {
            if (Coverage.Count != 1)
                quote = _transformations is null ? NeowPhysicalPricing.Quote(this, request) :
                    NeowTransformationPricing.Joint(_transformations, request);
            else if (_gpuPlan is null)
                quote = NeowWorkEstimate.Cpu(this, _workEstimate, request, 1, capacity: CpuFilterExecution.Capacity);
            else
                quote = (UsesCompatibilityGpu || _plan.Authority.UsesChildShuffle || OptimizationsFor(request.CompactInput).Revision.Length != 0 ? null :
                    NeowPhysicalPricing.Quote(this, request)) ?? NeowWorkEstimate.Gpu(this, _workEstimate, request);
        }
        return request.PrivateOutput && UsesSmallPrivateOutput && quote is not null
            ? quote with { Evidence = quote.Evidence + ";RetainedNumericalEstimate;PrivateSmallHeader8TransportUnmeasured" } : quote;
    }
    internal FilterBodyDescription Describe(FilterExecutionPreferences preferences)
    {
        FamilySurvivalProjection ReadSurvival(IReadOnlySet<string> passed) =>
            FamilyAuxiliaryEvidence.Read(FamilyId, "Preparation.Survival", () => ResolveSurvival(passed),
                FamilySurvivalProjection.Unresolved(FamilyId, "AuxiliarySurvivalUnavailable"), _inputIdentity);
        FamilyExpectedFilteringCostProjection? ReadExpected(IReadOnlySet<string> passed) =>
            FamilyAuxiliaryEvidence.Read<FamilyExpectedFilteringCostProjection?>(FamilyId, "Preparation.ExpectedWork",
                () => ResolveExpectedFilteringCost(passed), null, _inputIdentity);
        var prepared = new PreparedFilter(FamilyId)
        {
            Coverage = Coverage, AnalyticalCost = AnalyticalCost, Survival = ReadSurvival(Empty),
            ExpectedFilteringCost = ReadExpected(Empty), HasConditionalProjections = HasConditionalProjections,
            SurvivalResolver = ReadSurvival, ExpectedCostResolver = ReadExpected,
            Remainders = _plan.FilterRemainders,
            Issues = GpuPreparationFailed ? [new("GpuPlanConstruction","GpuPlanConstructionFailed",GpuPreparationFailureDetail)] : []
        };
        var profile = _profile;
        var cpu = DescribeCpu(prepared,profile,preferences.WorkerCount).ToArray();
        var body = new FilterBodyDescription(prepared,ResolveConditionPerformance(false).PhysicalImplementationRevision,_profile.ProfileId)
        {
            InputIdentity = _inputIdentity, UsesGpu = _gpuPlan is not null,
            PlayersCount = _plan.Authority.PlayersCount, CpuWorkers = _gpuPlan is null ? 1 : preferences.WorkerCount,
            WindowCapacity = _gpuPlan is null ? CpuFilterExecution.Capacity : NeowFamilyGpuExecutor.Capacity,
            PreferredRootWindowSize = _gpuPlan is null ? CpuFilterExecution.Capacity : FamilyPhysicalExecutionDefaults.DefaultCandidateBatchSize,
            NumericRequirements = _gpuPlan is null ? NumericRequirements.None : _gpuPlan.GpuNumericRequirements |
                (_composite is not null || _transformations is not null ? NumericRequirements.Float64 : NumericRequirements.None),
            PerformanceProjection = ResolveConditionPerformance,
            Cost = new()
            {
                CanonicalScope = _canonicalScope && _composite is null && _transformations is null,
                PrivateCanonicalScope = _canonicalScope && Coverage.Count==1,
                PrivatePhysicalRevision = _sparseRefinement is null ? null : privateInput=>
                    ResolveConditionPerformance(privateInput).PhysicalImplementationRevision.Replace(NeowCapsuleSparseRefinement.Revision,"",StringComparison.Ordinal)+NeowCapsuleSparseRefinement.PrivateRevision,
                PrivateOutputRevision = UsesSmallPrivateOutput ? NeowFamilyGpuExecutor.SmallOutputRevision : "",
                PrivateOutputPathSuffix = UsesSmallPrivateOutput ? ".PrivateSmallHeader8" : "",
                ObservationRevision = g=>!g.PrivateOutput ? NeowFamilyGpuExecutor.PublicBoundaryRevision : "",
                PublicBoundaryToken = "MeasuredNeowPublicBoundaryRevision="+NeowFamilyGpuExecutor.PublicBoundaryRevision,
                PublicBoundaryMeasuredMarker = "CurrentNeowPublicInvokeHostTransportMeasured",
                MinimumComparableInput = compact=>_gpuPlan is null ? compact ? CpuFilterExecution.Capacity/4 : CpuFilterExecution.Capacity : compact ? NeowFamilyGpuExecutor.Capacity/4 : NeowFamilyGpuExecutor.Capacity,
                CanonicalWorkShape = _ => FamilyCanonicalCostShape.DescribeNeow(_plan,_gpuPlan),
                OptimizationShape = OptimizationShape,
                Quote = (description,g,passed) => FamilyGpuReadback.Reference(description,
                    FamilyCanonicalMeasuredCost.TryDenseQuote(description,g) ?? QuoteRaw(g))
            },
            CpuAlternatives = cpu,
            BindPrivate = _gpuPlan is not null && !UsesCompatibilityGpu && _composite is null && _transformations is null
                ? PrivateFactory(_gpuPlan,_gpuOptimizations,_sparseRefinement is not null) : null,
            BindPrivateShared = _gpuPlan is not null && !UsesCompatibilityGpu && _composite is null && _transformations is null
                ? SharedPrivateFactory(_gpuPlan,_gpuOptimizations,_sparseRefinement is not null) : null,
            UsesSmallPrivateOutput = UsesSmallPrivateOutput,
            SupportsDenseBatchGrouping = _gpuPlan is { DirectNestedMode:0,JointTransformMetaOffset:<0 } && _composite is null && _transformations is null,
            CreateExecution = CreateRuntimeFactory(_plan,_gpuPlan,_composite,_transformations,_gpuOptimizations,
                preferences.SkipExactValidation,profile,_cpuPositiveIdentity is null ? _cpuMatcher : _cpuPositiveIdentity.Matches,_sparseRefinement)
        };
        return body;
    }
    private static Func<Godot.RenderingDevice,PrivateOrdinalBuffer?,PrivateOrdinalBuffer?,FamilyPrivateGpuExecution>
        PrivateFactory(NeowFamilyGpuPlan plan,NeowGpuOptimizations options,bool sparseRequested) =>
        (rd,input,output)=> {
            if(sparseRequested) RuntimeLog.TryBackgroundInfo("capsuleSparseRefinementNotApplied=true;scope=PrivateN;unionRetained=true;noAdditionalReadback=true");
            return NeowPrivateGpuBinding.Bind(plan,options,rd,input,output);
        };
    private static Func<Godot.RenderingDevice,PrivateOrdinalBuffer?,PrivateOrdinalBuffer?,IPrivateGpuScratchLease,FamilyPrivateGpuExecution>
        SharedPrivateFactory(NeowFamilyGpuPlan plan,NeowGpuOptimizations options,bool sparseRequested) =>
        (rd,input,output,scratch)=> {
            if(sparseRequested) RuntimeLog.TryBackgroundInfo("capsuleSparseRefinementNotApplied=true;scope=PrivateNShared;unionRetained=true;noAdditionalReadback=true");
            return NeowPrivateGpuBinding.Bind(plan,options,rd,input,output,scratch);
        };
    private static Func<FilterBodyDescription,FilterRuntimeServices,IFilterExecution> CreateRuntimeFactory(
        NeowReplayPlan plan,NeowFamilyGpuPlan? gpu,NeowCapsuleComposite? composite,
        NeowTransformationComposite? transformations,NeowGpuOptimizations optimizations,bool skipExact,
        IRuntimeProfile profile,Func<ulong,bool> matcher,NeowCapsuleSparseRefinement? sparse) => (description,services) => gpu is null
            ? NeowCapsuleSparseRefinementRun.WrapCpu(description,profile,matcher,false,false,null,sparse)
            : new NeowFilterExecution(description,plan,gpu,composite,transformations,optimizations,skipExact,profile,sparse);

    internal static bool TryCreate(SearchModel request,FilterExecutionPreferences preferences,
        out FilterBodyDescription? family,bool preferGpu=true,GpuPlanFactory? gpuPlanFactory=null)
    {
        bool admitted=TryPrepare(request,preferences,out var prepared,preferGpu,gpuPlanFactory);
        family=prepared?.Body;return admitted;
    }
    internal static bool TryPrepare(SearchModel request, FilterExecutionPreferences preferences, out NeowPreparedBody? prepared, bool preferGpu = true,
        GpuPlanFactory? gpuPlanFactory = null)
    {
        prepared = null;
        if (!request.Evaluation.HasNeowConstraints) return false;
        NeowReplayPlan plan;
        try
        {
            plan = NeowReplayPlan.Compile(request, false);
        }
        catch (InvalidOperationException ex) when (ex.Message is "NeowFamilyReplayAuthorityUnavailable" or "NeowFamilyUnmappedIdentity" ||
            ex.Message.StartsWith("DonorContinuationAuthorityUnavailable:", StringComparison.Ordinal))
        {
            RuntimeLog.TryBackgroundWarning("nFamilyUnavailable=true;phase=CpuReplayCompilation;replacement=None;remainingQueryOwner=ProductionExact;replacementCompleteness=NotClaimed;reason=" + ex.Message + ";exceptionJson=" + RuntimeLog.SafeJson(ex.ToString()));
            return false;
        }
        string continuationIssue = plan.StructuredConditions.Length == 0 && !plan.HasFinalCurseFastProjection ? "" : plan.ContinuationIssue();
        if (continuationIssue.Length > 0)
        {
            if (RuntimeLog.SessionId.Length > 0) Infrastructure.Diagnostics.RuntimeIssueLedger.Current.RecordDegradation("NeowFilterUnavailable", nameof(NeowFamily),
                "Filter.Construction", "issues.filter_incomplete", $"cause={continuationIssue};predicatesRetained=true;finalValidationSkipped={preferences.SkipExactValidation};matchClaimed=false");
            return false;
        }
        var domain = new NeowFamily(request,plan,preferGpu,gpuPlanFactory:gpuPlanFactory);
        prepared = new(domain,domain.Describe(preferences));
        return true;
    }
}

internal sealed record NeowPreparedBody(NeowFamily Domain,FilterBodyDescription Body)
{
    internal NeowReplayPlan Replay => Domain.Replay;
    internal NeowFamilyGpuPlan? GpuPlan => Domain.GpuPlan;
}
