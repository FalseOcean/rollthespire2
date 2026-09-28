using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Effects.Shadow;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;

namespace RolltheSpire2.Core.Effects;

internal sealed partial class NeowEffectProjectionEngine
{
    private sealed record BonesRouteBranchState(
        NeowEffectWorkingState State,
        string RouteId,
        IReadOnlyList<BonesRelicScopedResult> RelicScopedResults,
        PredictionPrecision Precision,
        PredictionPrecision ProductRelevantProjectionPrecision,
        ProductRelevantProjectionStatus ProductRelevantProjectionStatus,
        IReadOnlyList<PredictionWarningCode> WarningCodes,
        BonesDependencyContinuity Continuity,
        IReadOnlyList<PlayerChoiceSelectionTrace> PlayerChoiceSelections);

    private NeowEffectProjection ProjectBones(ModelKey relicKey, NeowEffectWorkingState? state, int recursionDepth)
    {
        if (state is null ||
            !_analysisAuthority.HasExactModernNeowIdentityInputs ||
            !state.Authority.BonesEligibilityExact ||
            !state.Authority.CursePoolExact ||
            state.Authority.BonesEligibleRelics is null ||
            state.Authority.GeneratedCursePool is null)
        {
            return Unknown(relicKey, "bones-eligible-relic-pool-required");
        }

        List<ModelKey> valid = state.Authority.BonesEligibleRelics
            .Where(key => key != BaseGameModelKeys.Relics.NeowsBones)
            .ToList();
        if (valid.Count < 2)
        {
            return Unknown(relicKey, "bones-relic-pool-too-small");
        }

        state.Rng.Rewards.UnstableShuffle(valid);
        ModelKey[] offered = valid.Take(2).ToArray();
        NeowEffectWorkingState postOfferState = state.Clone();
        IReadOnlyList<ModelKey>[] acquisitionOrders =
        {
            new[] { offered[0], offered[1] },
            new[] { offered[1], offered[0] }
        };

        var originalRoutes = new List<BonesAcquisitionRouteResult>();
        for (int orderIndex = 0; orderIndex < acquisitionOrders.Length; orderIndex++)
        {
            IReadOnlyList<BonesAcquisitionRouteResult>? routes = EvaluateBonesAcquisitionOrder(
                relicKey,
                acquisitionOrders[orderIndex],
                postOfferState.Clone(),
                orderIndex,
                recursionDepth,
                out bool branchBudgetExceeded);
            if (routes is null)
            {
                NeowEffectProjection incomplete = branchBudgetExceeded
                    ? NeowEffectProjection.Unknown(
                        relicKey,
                        PredictionWarningCode.EffectBranchBudgetExceeded,
                        Evidence(relicKey, "bones-order-route-budget-exceeded"))
                    : Unknown(relicKey, "bones-acquisition-route-authority-incomplete");
                // The offer was already predicted exactly before route-local
                // continuation became unavailable. Do not discard that fact.
                return incomplete with { BonesOutcome = BonesOrderImpactAnalyzer.Analyze(offered, originalRoutes) };
            }

            originalRoutes.AddRange(routes);
            if (originalRoutes.Count > state.Authority.BranchBudget)
            {
                return NeowEffectProjection.Unknown(
                    relicKey,
                    PredictionWarningCode.EffectBranchBudgetExceeded,
                    Evidence(relicKey, "bones-order-route-budget-exceeded"));
            }
        }

        if (originalRoutes.Count == 0)
        {
            return Unknown(relicKey, "bones-no-complete-acquisition-routes");
        }

        BonesOutcomeAnalysis analyzed = BonesOrderImpactAnalyzer.Analyze(offered, originalRoutes);
        PlayerChoiceImpactAnalysis playerChoiceImpact = BuildPlayerChoiceImpactAnalysis(originalRoutes);
        BonesOutcomeAnalysis outcome = analyzed with { PlayerChoiceImpact = playerChoiceImpact };
        IReadOnlyList<PredictedEffectGroup> objectiveGroups = FlattenBonesRoutes(originalRoutes);

        PredictionPrecision fullPrecision = originalRoutes.All(route => route.Precision == PredictionPrecision.Exact)
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
        ProductRelevantProjectionStatus productStatus = MergeProductStatuses(
            originalRoutes.Select(route => route.ProductRelevantProjectionStatus));
        PredictionPrecision productPrecision = productStatus == ProductRelevantProjectionStatus.Evaluated &&
                                             originalRoutes.All(route => route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact)
            ? PredictionPrecision.Exact
            : productStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy
                ? PredictionPrecision.DescriptionOnly
                : PredictionPrecision.Partial;

        PredictionWarningCode[] warningCodes = originalRoutes
            .SelectMany(route => route.WarningCodes)
            .Distinct()
            .ToArray();
        EvidenceCode evidence = Evidence(relicKey, productPrecision == PredictionPrecision.Exact
            ? "bones-product-relevant-projection-exact"
            : productStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy
                ? "bones-player-choice-impact-not-evaluated-by-policy"
                : "bones-product-relevant-projection-partial");

        return new NeowEffectProjection(
            NeowEffectImplementationStatus.Implemented,
            fullPrecision,
            objectiveGroups,
            warningCodes.Select(code => new PredictionWarning(code, relicKey, evidence)).ToArray(),
            evidence,
            outcome,
            ProductRelevantProjectionPrecision: productPrecision,
            FullEffectSemanticsCompleteness: fullPrecision == PredictionPrecision.Exact
                ? FullEffectSemanticsCompleteness.Complete
                : FullEffectSemanticsCompleteness.Partial,
            ProductRelevantProjectionStatus: productStatus);
    }

    private IReadOnlyList<BonesAcquisitionRouteResult>? EvaluateBonesAcquisitionOrder(
        ModelKey bonesKey,
        IReadOnlyList<ModelKey> acquisitionOrder,
        NeowEffectWorkingState initialState,
        int orderIndex,
        int recursionDepth,
        out bool branchBudgetExceeded)
    {
        branchBudgetExceeded = false;
        var branches = new List<BonesRouteBranchState>
        {
            new(
                initialState,
                $"bones.order.{orderIndex}",
                Array.Empty<BonesRelicScopedResult>(),
                PredictionPrecision.Exact,
                PredictionPrecision.Exact,
                ProductRelevantProjectionStatus.Evaluated,
                Array.Empty<PredictionWarningCode>(),
                BonesDependencyContinuity.CreateInitial(
                    initialState.Authority,
                    Evidence(bonesKey, "bones-continuity-initial")),
                Array.Empty<PlayerChoiceSelectionTrace>())
        };

        for (int acquisitionIndex = 0; acquisitionIndex < acquisitionOrder.Count; acquisitionIndex++)
        {
            ModelKey selected = acquisitionOrder[acquisitionIndex];
            ModelKey? nextRelic = acquisitionIndex + 1 < acquisitionOrder.Count
                ? acquisitionOrder[acquisitionIndex + 1]
                : null;
            var next = new List<BonesRouteBranchState>();
            foreach (BonesRouteBranchState branch in branches)
            {
                IReadOnlyList<BonesRouteBranchState>? applied = ApplyBonesRelic(
                    bonesKey,
                    selected,
                    nextRelic,
                    branch,
                    recursionDepth);
                if (applied is null)
                {
                    return null;
                }
                next.AddRange(applied);
                if (next.Count > initialState.Authority.BranchBudget)
                {
                    branchBudgetExceeded = true;
                    return null;
                }
            }
            branches = next;
        }

        var results = new List<BonesAcquisitionRouteResult>();
        foreach (BonesRouteBranchState branch in branches)
        {
            BonesSharedContinuationResult continuation = BuildBonesSharedContinuation(bonesKey, branch);
            PredictionPrecision routePrecision = branch.Precision == PredictionPrecision.Exact &&
                                               continuation.Precision == PredictionPrecision.Exact
                ? PredictionPrecision.Exact
                : PredictionPrecision.Partial;
            ProductRelevantProjectionStatus productStatus = MergeProductStatuses(new[]
            {
                branch.ProductRelevantProjectionStatus,
                continuation.ProductRelevantProjectionStatus
            });
            PredictionPrecision productPrecision = productStatus == ProductRelevantProjectionStatus.Evaluated &&
                                                 branch.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
                                                 continuation.FinalCursePrecision == PredictionPrecision.Exact
                ? PredictionPrecision.Exact
                : productStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy
                    ? PredictionPrecision.DescriptionOnly
                    : PredictionPrecision.Partial;
            PredictionWarningCode[] warnings = branch.WarningCodes
                .Concat(continuation.WarningCodes)
                .Distinct()
                .ToArray();
            string fullObservable = FingerprintBonesParts(
                "bones-route-full-observable-v2",
                branch.RelicScopedResults
                    .OrderBy(result => result.SourceRelicKey.Serialized, StringComparer.Ordinal)
                    .Select(result => result.ObservableFingerprint)
                    .Concat(new[]
                    {
                        continuation.ObservableStateFingerprint,
                        BuildWorkingStateObservableFingerprint(branch.State)
                    }));
            string productObservable = FingerprintBonesParts(
                "bones-route-product-observable-v2",
                branch.RelicScopedResults
                    .OrderBy(result => result.SourceRelicKey.Serialized, StringComparer.Ordinal)
                    .Select(result => string.IsNullOrWhiteSpace(result.ProductRelevantObservableFingerprint)
                        ? result.ObservableFingerprint
                        : result.ProductRelevantObservableFingerprint)
                    .Concat(new[]
                    {
                        continuation.FinalCurseKey?.Serialized ?? "curse:none",
                        continuation.FinalCursePrecision.ToString(),
                        BuildObservableDeckFingerprint(branch.State.Deck?.Cards ?? Array.Empty<NeowEffectCardSnapshot>())
                    }));

            RouteProjectionStatus routeStatus = ResolveRouteProjectionStatus(
                productStatus,
                productPrecision,
                continuation.FinalCursePrecision);
            EvidenceCode routeEvidence = Evidence(bonesKey, routeStatus switch
            {
                RouteProjectionStatus.Exact => "bones-route-product-projection-exact",
                RouteProjectionStatus.NotEvaluatedByPolicy => "bones-route-continuation-not-evaluated-by-policy",
                RouteProjectionStatus.Unsupported => "bones-route-product-projection-unsupported",
                RouteProjectionStatus.Unknown => "bones-route-product-projection-unknown",
                _ => "bones-route-product-projection-partial"
            });

            results.Add(new BonesAcquisitionRouteResult(
                branch.RouteId,
                acquisitionOrder.ToArray(),
                branch.RelicScopedResults,
                continuation,
                routePrecision,
                warnings,
                fullObservable,
                continuation.RelevantContinuationFingerprint,
                branch.Continuity,
                productPrecision,
                productStatus,
                branch.PlayerChoiceSelections,
                productObservable,
                BuildObservableDeckFingerprint(branch.State.Deck?.Cards ?? Array.Empty<NeowEffectCardSnapshot>()),
                routeStatus,
                routeEvidence,
                BuildOpeningRewardContinuation(
                    branch.RouteId,
                    orderIndex,
                    OpeningRewardRouteKind.BonesAcquisitionOrder,
                    bonesKey,
                    acquisitionOrder,
                    branch.State,
                    branch.Continuity.RewardsRng,
                    branch.Continuity.UnknownHook,
                    branch.Continuity.NestedObtain,
                    new[] { routeEvidence, continuation.EvidenceCode },
                    ExtractNestedRelicKeys(branch.RelicScopedResults.SelectMany(result => result.EffectGroups)),
                    sharedStateExact: branch.Continuity.CanProjectFinalCurse,
                    executedNestedObtainRelics: branch.RelicScopedResults.SelectMany(result =>
                        ExecutedNestedObtainRelics(result.SourceRelicKey, result.EffectGroups)).Distinct().ToArray())));
        }

        return results;
    }


    private static RouteProjectionStatus ResolveRouteProjectionStatus(
        ProductRelevantProjectionStatus productStatus,
        PredictionPrecision productPrecision,
        PredictionPrecision finalCursePrecision) => productStatus switch
    {
        ProductRelevantProjectionStatus.NotEvaluatedByPolicy => RouteProjectionStatus.NotEvaluatedByPolicy,
        ProductRelevantProjectionStatus.Unsupported => RouteProjectionStatus.Unsupported,
        ProductRelevantProjectionStatus.Unknown => RouteProjectionStatus.Unknown,
        ProductRelevantProjectionStatus.Evaluated
            when productPrecision == PredictionPrecision.Exact &&
                 finalCursePrecision == PredictionPrecision.Exact => RouteProjectionStatus.Exact,
        ProductRelevantProjectionStatus.Evaluated => RouteProjectionStatus.Partial,
        _ => RouteProjectionStatus.Unknown
    };

    private IReadOnlyList<BonesRouteBranchState>? ApplyBonesRelic(
        ModelKey bonesKey,
        ModelKey selected,
        ModelKey? nextRelic,
        BonesRouteBranchState branch,
        int recursionDepth)
    {
        NeowEffectCoverageEntry? coverage = NeowEffectCoverageRegistry.TryGet(selected, out NeowEffectCoverageEntry entry)
            ? entry
            : null;
        NeowEffectFamily family = coverage?.Family ?? NeowEffectFamily.FutureRuntime;
        NeowEffectTraits capabilities = coverage?.ProductCapabilities ??
            NeowEffectTraits.None;
        bool producesPlayerChoiceDeckMutation =
            (capabilities & NeowEffectTraits.ProducesPlayerChoiceDeckMutation) != 0;

        if (producesPlayerChoiceDeckMutation)
        {
            // Base relic projection is always evaluated. The complex switch gates
            // only continuation through the player's chosen shadow deck.
            NeowEffectWorkingState objectiveState = branch.State.Clone();
            NeowEffectProjection objectiveProjection = ProjectInternal(
                selected,
                objectiveState,
                recursionDepth + 1);
            string objectiveRouteId = branch.RouteId + "." + selected.Entry.ToLowerInvariant();
            BonesRelicScopedResult objectiveScoped = BuildBonesRelicScopedResult(
                selected,
                objectiveRouteId,
                objectiveProjection,
                forcePartial: false,
                extraWarning: null);
            PredictionPrecision objectiveFullPrecision =
                branch.Precision == PredictionPrecision.Exact && objectiveProjection.Precision == PredictionPrecision.Exact
                    ? PredictionPrecision.Exact
                    : PredictionPrecision.Partial;
            PredictionPrecision objectiveProductPrecision = MergeProductPrecision(
                branch.ProductRelevantProjectionPrecision,
                objectiveProjection.ProductRelevantProjectionPrecision);
            ProductRelevantProjectionStatus objectiveProductStatus = MergeProductStatuses(new[]
            {
                branch.ProductRelevantProjectionStatus,
                objectiveProjection.ProductRelevantProjectionStatus
            });
            PredictionWarningCode[] objectiveWarnings = branch.WarningCodes
                .Concat(objectiveScoped.WarningCodes)
                .Distinct()
                .ToArray();
            BonesDependencyContinuity objectiveContinuity = branch.Continuity.Merge(
                ResolveProjectionDependencyImpact(selected, objectiveProjection));

            // Capability-driven continuation is legal only after the base relic
            // projection itself is product-exact. Missing pool/deck authority or
            // unsupported selection semantics remain fail-closed instead of being
            // reclassified as a policy-disabled continuation.
            if (objectiveProjection.ProductRelevantProjectionStatus != ProductRelevantProjectionStatus.Evaluated ||
                objectiveProjection.ProductRelevantProjectionPrecision != PredictionPrecision.Exact)
            {
                return new[]
                {
                    new BonesRouteBranchState(
                        objectiveState,
                        objectiveRouteId + ".base-projection-incomplete",
                        branch.RelicScopedResults.Concat(new[] { objectiveScoped }).ToArray(),
                        objectiveFullPrecision,
                        objectiveProductPrecision,
                        objectiveProductStatus,
                        objectiveWarnings,
                        objectiveContinuity,
                        branch.PlayerChoiceSelections)
                };
            }

            if (!TryResolveCrossRelicPlayerChoiceContinuationRequirement(
                    nextRelic,
                    objectiveState,
                    recursionDepth,
                    out bool requiresCrossRelicContinuation))
            {
                return null;
            }

            // After an actual W/WP pickup, retain the existing real choice
            // traces even when this is the last relic. Production commitment
            // filtering then has an actual resulting deck/Witness, not merely
            // an offer plus a declared pick. No additional Fast bag replay.
            requiresCrossRelicContinuation |= _enableComplexBonesDeckInteractions && branch.RelicScopedResults
                .SelectMany(r => r.EffectGroups).SelectMany(g => g.OrderedItems)
                .Any(i => i.Kind == PredictedEffectKind.AddRelic &&
                    (i.TargetKey == BaseGameModelKeys.OrdinaryRelics.Whetstone || i.TargetKey == BaseGameModelKeys.OrdinaryRelics.WarPaint));

            if (!requiresCrossRelicContinuation)
            {
                return new[]
                {
                    new BonesRouteBranchState(
                        objectiveState,
                        objectiveRouteId + ".base-projection-only",
                        branch.RelicScopedResults.Concat(new[] { objectiveScoped }).ToArray(),
                        objectiveFullPrecision,
                        objectiveProductPrecision,
                        objectiveProductStatus,
                        objectiveWarnings,
                        objectiveContinuity,
                        branch.PlayerChoiceSelections)
                };
            }

            if (!_enableComplexBonesDeckInteractions)
            {
                PredictionWarningCode policyWarning = PredictionWarningCode.ComplexResultNotEvaluatedByPolicy;
                return new[]
                {
                    new BonesRouteBranchState(
                        objectiveState,
                        objectiveRouteId + ".continuation-not-evaluated-by-policy",
                        branch.RelicScopedResults.Concat(new[] { objectiveScoped }).ToArray(),
                        objectiveFullPrecision,
                        PredictionPrecision.DescriptionOnly,
                        ProductRelevantProjectionStatus.NotEvaluatedByPolicy,
                        objectiveWarnings.Concat(new[] { policyWarning }).Distinct().ToArray(),
                        objectiveContinuity.Merge(
                            EffectDependencyImpact.FiniteChoiceNotEvaluated(
                                _profileId,
                                selected,
                                Evidence(selected, "cross-relic-player-choice-continuation-disabled"))),
                        branch.PlayerChoiceSelections)
                };
            }

            NeowEffectWorkingState finitePreChoiceState = selected == BaseGameModelKeys.Relics.NewLeaf
                // Base NewLeaf projection applies a canonical representative so
                // standalone/complex-off analysis still shows its generated card.
                // Full continuation must enumerate from the original pre-choice
                // state to avoid applying that representative before real routes.
                ? branch.State.Clone()
                : objectiveState.Clone();
            IReadOnlyList<FiniteBonesRouteOutcome>? choiceRoutes = family == NeowEffectFamily.FinitePlayerChoice
                ? EnumerateFiniteBonesRoutes(selected, finitePreChoiceState)
                : EnumerateOfferSelectionBonesRoutes(selected, objectiveState.Clone(), objectiveProjection);
            if (choiceRoutes is null)
            {
                return null;
            }
            if (choiceRoutes.Count == 0)
            {
                return Array.Empty<BonesRouteBranchState>();
            }

            var output = new List<BonesRouteBranchState>();
            foreach (FiniteBonesRouteOutcome finite in choiceRoutes)
            {
                string routeId = objectiveRouteId + "." + finite.RouteId;
                var trace = new PlayerChoiceSelectionTrace(
                    routeId + ".choice-trace",
                    selected,
                    finite.ChoicePolicy,
                    finite.OrderedItems,
                    finite.ChoiceDeckFingerprint,
                    finite.RelevantRngFingerprint,
                    finite.RawLegalSelectionCount,
                    finite.DistinctDeckStateCount,
                    finite.EquivalentSelectionCount,
                    branch.RelicScopedResults.Count);
                output.Add(new BonesRouteBranchState(
                    finite.State,
                    routeId,
                    branch.RelicScopedResults.Concat(new[] { objectiveScoped }).ToArray(),
                    objectiveFullPrecision,
                    objectiveProductPrecision == PredictionPrecision.Exact && finite.Precision == PredictionPrecision.Exact
                        ? PredictionPrecision.Exact
                        : PredictionPrecision.Partial,
                    ProductRelevantProjectionStatus.Evaluated,
                    objectiveWarnings,
                    finite.Precision == PredictionPrecision.Exact
                        ? objectiveContinuity
                        : objectiveContinuity.Merge(
                            EffectDependencyImpact.FiniteChoiceNotEvaluated(
                                _profileId,
                                selected,
                                Evidence(selected, "player-choice-route-partial"))),
                    branch.PlayerChoiceSelections.Concat(new[] { trace }).ToArray()));
            }
            return output;
        }

        NeowEffectWorkingState working = branch.State.Clone();
        bool objectiveOnlyCapsule =
            branch.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy &&
            (selected == BaseGameModelKeys.Relics.SmallCapsule || selected == BaseGameModelKeys.Relics.LargeCapsule);
        NeowEffectProjection nested = selected == BaseGameModelKeys.Relics.SmallCapsule
            ? ProjectCapsule(selected, working, count: 1, includeFixedCards: false, recursionDepth: recursionDepth + 1,
                executeNestedAutomaticEffects: !objectiveOnlyCapsule)
            : selected == BaseGameModelKeys.Relics.LargeCapsule
                ? ProjectCapsule(selected, working, count: 2, includeFixedCards: true, recursionDepth: recursionDepth + 1,
                    executeNestedAutomaticEffects: !objectiveOnlyCapsule)
                : ProjectInternal(selected, working, recursionDepth + 1);
        string nestedRouteId = branch.RouteId + "." + selected.Entry.ToLowerInvariant();
        BonesRelicScopedResult nestedScoped = BuildBonesRelicScopedResult(
            selected,
            nestedRouteId,
            nested,
            forcePartial: false,
            extraWarning: null);
        return new[]
        {
            new BonesRouteBranchState(
                working,
                nestedRouteId,
                branch.RelicScopedResults.Concat(new[] { nestedScoped }).ToArray(),
                branch.Precision == PredictionPrecision.Exact && nested.Precision == PredictionPrecision.Exact
                    ? PredictionPrecision.Exact
                    : PredictionPrecision.Partial,
                MergeProductPrecision(branch.ProductRelevantProjectionPrecision, nested.ProductRelevantProjectionPrecision),
                MergeProductStatuses(new[] { branch.ProductRelevantProjectionStatus, nested.ProductRelevantProjectionStatus }),
                branch.WarningCodes.Concat(nestedScoped.WarningCodes).Distinct().ToArray(),
                branch.Continuity.Merge(ResolveProjectionDependencyImpact(selected, nested)),
                branch.PlayerChoiceSelections)
        };
    }


    private bool TryResolveCrossRelicPlayerChoiceContinuationRequirement(
        ModelKey? nextRelic,
        NeowEffectWorkingState postObjectiveState,
        int recursionDepth,
        out bool required)
    {
        required = false;
        if (!nextRelic.HasValue ||
            (nextRelic.Value != BaseGameModelKeys.Relics.SmallCapsule &&
             nextRelic.Value != BaseGameModelKeys.Relics.LargeCapsule))
        {
            return true;
        }

        NeowEffectWorkingState probeState = postObjectiveState.Clone();
        NeowEffectProjection capsuleProbe = nextRelic.Value == BaseGameModelKeys.Relics.SmallCapsule
            ? ProjectCapsule(
                nextRelic.Value,
                probeState,
                count: 1,
                includeFixedCards: false,
                recursionDepth: recursionDepth + 1,
                executeNestedAutomaticEffects: false)
            : ProjectCapsule(
                nextRelic.Value,
                probeState,
                count: 2,
                includeFixedCards: true,
                recursionDepth: recursionDepth + 1,
                executeNestedAutomaticEffects: false);

        if (capsuleProbe.ProductRelevantProjectionStatus != ProductRelevantProjectionStatus.Evaluated)
        {
            return false;
        }

        PredictedEffect[] nestedRelics = capsuleProbe.EffectGroups
            .SelectMany(group => group.OrderedItems)
            .Where(item => item.Kind == PredictedEffectKind.AddRelic &&
                           item.Relation == PredictedEffectRelation.NestedRelic &&
                           item.TargetKey.HasValue)
            .ToArray();
        if (nestedRelics.Length == 0)
        {
            return false;
        }

        required = nestedRelics.Any(item =>
            item.TargetKey == BaseGameModelKeys.OrdinaryRelics.Whetstone ||
            item.TargetKey == BaseGameModelKeys.OrdinaryRelics.WarPaint);
        return true;
    }

    private NeowEffectProjection BuildFiniteChoiceProjection(
        ModelKey selected,
        FiniteBonesRouteOutcome route,
        string routeId)
    {
        EffectSelectionPolicy policy = selected == BaseGameModelKeys.Relics.PrecariousShears
            ? EffectSelectionPolicy.ChooseExactlyN
            : EffectSelectionPolicy.ChooseExactlyOne;
        int? required = selected == BaseGameModelKeys.Relics.PrecariousShears ? 2 : 1;
        string stableSourceGroupId = selected.Serialized + ".finite-choice";
        PredictedEffectGroup group = new(
            routeId + ".choice",
            0,
            policy,
            EffectPredictionDomain.Neow,
            EffectPredictionScope.FinitePlayerChoiceRoutes,
            selected.Entry + " product player-choice route",
            route.OrderedItems
                .OrderBy(item => item.ItemOrder)
                .Select(item => item with
                {
                    SourceRelicKey = selected,
                    SourceEffectGroupId = stableSourceGroupId,
                    Phase = PredictedEffectPhase.RelicImmediateEffect,
                    IsPlayerChoiceEffect = true
                })
                .ToArray(),
            RequiredSelectionCount: required,
            SelectionSetId: selected.Serialized + ".finite-choice-set",
            BundleId: routeId,
            BundleOrder: 0,
            SourceRelicKey: selected,
            SourceEffectGroupId: stableSourceGroupId,
            Phase: PredictedEffectPhase.RelicImmediateEffect,
            IsProductRelevant: true,
            IsPlayerChoiceGroup: true);
        if (route.Precision != PredictionPrecision.Exact)
        {
            return NeowEffectProjection.Partial(
                selected,
                new[] { group },
                PredictionWarningCode.ComplexRouteNotEvaluated,
                Evidence(selected, "bones-finite-route-partial"));
        }

        NeowEffectProjection exactProduct = NeowEffectProjection.Exact(
            selected,
            new[] { group },
            Evidence(selected, "bones-finite-route-product-exact"));
        return exactProduct with
        {
            Precision = PredictionPrecision.Partial,
            ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
            ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
            FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.Partial
        };
    }

    private BonesRelicScopedResult BuildBonesRelicScopedResult(
        ModelKey sourceRelic,
        string routeId,
        NeowEffectProjection projection,
        bool forcePartial,
        PredictionWarningCode? extraWarning)
    {
        string acquisitionGroupId = routeId + ".acquire";
        string acquisitionSourceGroupId = "bones.acquire." + sourceRelic.Serialized;
        PredictedEffect acquisition = new(
            PredictedEffectKind.AddRelic,
            sourceRelic,
            null,
            PredictionPrecision.Exact,
            Array.Empty<PredictionWarningCode>(),
            Evidence(sourceRelic, "bones-forced-obtain"),
            ItemOrder: 0,
            Multiplicity: 1,
            SourceStep: "NeowsBones ordered acquisition",
            OfferItemId: routeId + ".relic",
            SourceRelicKey: sourceRelic,
            SourceEffectGroupId: acquisitionSourceGroupId,
            Phase: PredictedEffectPhase.RelicAcquisition,
            IsProductRelevant: true);
        var groups = new List<PredictedEffectGroup>
        {
            new(
                acquisitionGroupId,
                0,
                EffectSelectionPolicy.ForcedObtain,
                EffectPredictionDomain.Neow,
                EffectPredictionScope.NestedObtain,
                "NeowsBones ordered acquisition",
                new[] { acquisition },
                BundleId: routeId,
                BundleOrder: 0,
                SourceRelicKey: sourceRelic,
                SourceEffectGroupId: acquisitionSourceGroupId,
                Phase: PredictedEffectPhase.RelicAcquisition,
                IsProductRelevant: true)
        };

        int groupOrder = 1;
        foreach (PredictedEffectGroup raw in projection.EffectGroups
                     .Where(group => !group.IsPlayerChoiceGroup)
                     .OrderBy(group => group.GroupOrder))
        {
            string sourceGroupId = raw.SourceEffectGroupId ?? raw.GroupId;
            PredictionWarningCode[] groupExtraWarnings = extraWarning.HasValue
                ? new[] { extraWarning.Value }
                : Array.Empty<PredictionWarningCode>();
            PredictedEffect[] items = raw.OrderedItems
                .Where(item => !item.IsPlayerChoiceEffect)
                .OrderBy(item => item.ItemOrder)
                .Select((item, index) => item with
                {
                    ItemOrder = index,
                    Precision = forcePartial ? PredictionPrecision.Partial : item.Precision,
                    WarningCodes = item.WarningCodes.Concat(groupExtraWarnings).Distinct().ToArray(),
                    SourceRelicKey = item.SourceRelicKey ?? sourceRelic,
                    SourceEffectGroupId = sourceGroupId,
                    Phase = raw.Scope == EffectPredictionScope.NestedObtain
                        ? PredictedEffectPhase.NestedObtain
                        : item.Phase
                })
                .ToArray();
            if (items.Length == 0)
            {
                continue;
            }
            groups.Add(raw with
            {
                GroupId = routeId + "." + raw.GroupId,
                GroupOrder = groupOrder++,
                OrderedItems = items,
                SourceRelicKey = sourceRelic,
                SourceEffectGroupId = sourceGroupId,
                Phase = raw.Scope == EffectPredictionScope.NestedObtain
                    ? PredictedEffectPhase.NestedObtain
                    : raw.Phase,
                IsPlayerChoiceGroup = false
            });
        }

        PredictionPrecision precision = forcePartial ? PredictionPrecision.Partial : projection.Precision;
        PredictionPrecision productPrecision = forcePartial
            ? PredictionPrecision.Partial
            : projection.ProductRelevantProjectionPrecision;
        PredictionWarningCode[] warnings = projection.Warnings
            .Select(warning => warning.Code)
            .Concat(extraWarning.HasValue ? new[] { extraWarning.Value } : Array.Empty<PredictionWarningCode>())
            .Concat(groups.SelectMany(group => group.OrderedItems).SelectMany(item => item.WarningCodes))
            .Distinct()
            .ToArray();
        string fullFingerprint = BuildRelicScopedFingerprint(
            sourceRelic,
            groups,
            precision,
            warnings,
            projection.EvidenceCode,
            productOnly: false);
        PredictionWarningCode[] productWarnings = projection.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
                                                 productPrecision == PredictionPrecision.Exact
            ? warnings.Where(code => IsProductRelevantWarning(code) &&
                                     code != PredictionWarningCode.EffectSnapshotIncomplete).ToArray()
            : warnings.Where(IsProductRelevantWarning).ToArray();
        string productFingerprint = BuildRelicScopedFingerprint(
            sourceRelic,
            groups,
            productPrecision,
            productWarnings,
            projection.EvidenceCode,
            productOnly: true);
        return new BonesRelicScopedResult(
            sourceRelic,
            groups,
            precision,
            warnings,
            projection.EvidenceCode,
            fullFingerprint,
            productPrecision,
            projection.FullEffectSemanticsCompleteness,
            projection.ProductRelevantProjectionStatus,
            productFingerprint);
    }

    private EffectDependencyImpact ResolveProjectionDependencyImpact(
        ModelKey relicKey,
        NeowEffectProjection projection)
    {
        if (projection.DependencyImpact is not null)
        {
            return projection.DependencyImpact;
        }

        EvidenceCode evidence = Evidence(relicKey, "bones-continuation-no-product-domain-invalidation");
        if (projection.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
            projection.ProductRelevantProjectionPrecision == PredictionPrecision.Exact)
        {
            // Product-relevant classification is the explicit authority that any
            // unmodeled full/future semantics do not touch the opening projection.
            return EffectDependencyImpact.None(evidence);
        }

        PredictionWarningCode warning = projection.Warnings.Count > 0
            ? projection.Warnings[0].Code
            : PredictionWarningCode.EffectAuthorityIncomplete;
        return EffectDependencyImpact.ConservativeUnknownImmediateHook(
            warning,
            Evidence(relicKey, "bones-continuation-unclassified-product-impact"));
    }

    private BonesSharedContinuationResult BuildBonesSharedContinuation(
        ModelKey bonesKey,
        BonesRouteBranchState branch)
    {
        bool cursePoolPresent = branch.State.Authority.GeneratedCursePool is { Count: > 0 };
        bool policySkipped = branch.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy ||
                             branch.Continuity.ContinuationWarningCodes.Contains(
                                 PredictionWarningCode.ComplexResultNotEvaluatedByPolicy);
        bool canProjectCurse = !policySkipped && branch.Continuity.CanProjectFinalCurse && cursePoolPresent;
        var warnings = new List<PredictionWarningCode>();
        ModelKey? curse = null;
        PredictedEffectGroup group;
        PredictionPrecision finalCursePrecision;
        ProductRelevantProjectionStatus productStatus;

        if (canProjectCurse)
        {
            IReadOnlyList<ModelKey> curses = branch.State.Authority.GeneratedCursePool!;
            curse = curses[branch.State.Rng.Niche.NextInt(curses.Count)];
            AddSyntheticCardToDeck(branch.State.Deck, curse.Value, branch.RouteId + ".bones-curse", EffectCardType.Curse);
            string groupId = branch.RouteId + ".shared-continuation";
            EvidenceCode curseEvidence = Evidence(bonesKey, "bones-final-curse-product-continuity-exact");
            finalCursePrecision = PredictionPrecision.Exact;
            productStatus = ProductRelevantProjectionStatus.Evaluated;
            group = new PredictedEffectGroup(
                groupId,
                0,
                EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionDomain.Neow,
                EffectPredictionScope.FinitePlayerChoiceRoutes,
                "NeowsBones final route-specific curse",
                new[]
                {
                    new PredictedEffect(
                        PredictedEffectKind.AddCard,
                        curse,
                        null,
                        PredictionPrecision.Exact,
                        Array.Empty<PredictionWarningCode>(),
                        curseEvidence,
                        ItemOrder: 0,
                        Multiplicity: 1,
                        SourceStep: "NeowsBones shared continuation",
                        OfferItemId: branch.RouteId + ".curse",
                        SourceRelicKey: bonesKey,
                        SourceEffectGroupId: groupId,
                        Phase: PredictedEffectPhase.SharedContinuation,
                        IsProductRelevant: true)
                },
                SourceRelicKey: bonesKey,
                SourceEffectGroupId: groupId,
                Phase: PredictedEffectPhase.SharedContinuation,
                RngEvidenceCode: "niche:route-specific-final-curse",
                IsProductRelevant: true);
        }
        else
        {
            if (policySkipped)
            {
                warnings.Add(PredictionWarningCode.ComplexResultNotEvaluatedByPolicy);
                finalCursePrecision = PredictionPrecision.DescriptionOnly;
                productStatus = ProductRelevantProjectionStatus.NotEvaluatedByPolicy;
            }
            else if (!cursePoolPresent)
            {
                warnings.Add(PredictionWarningCode.EffectPoolEmpty);
                finalCursePrecision = PredictionPrecision.Unknown;
                productStatus = ProductRelevantProjectionStatus.Unknown;
            }
            else
            {
                warnings.AddRange(branch.Continuity.ContinuationWarningCodes);
                if (warnings.Count == 0)
                {
                    warnings.Add(PredictionWarningCode.EffectAuthorityIncomplete);
                }
                finalCursePrecision = PredictionPrecision.Unknown;
                productStatus = ProductRelevantProjectionStatus.Unknown;
            }

            PredictionWarningCode[] distinctWarnings = warnings.Distinct().ToArray();
            string groupId = branch.RouteId + ".shared-continuation-not-evaluated";
            EvidenceCode evidence = Evidence(bonesKey, policySkipped
                ? "bones-final-curse-not-evaluated-by-product-policy"
                : "bones-final-curse-product-dependency-incomplete");
            group = new PredictedEffectGroup(
                groupId,
                0,
                EffectSelectionPolicy.NoPlayerChoice,
                EffectPredictionDomain.Neow,
                EffectPredictionScope.FinitePlayerChoiceRoutes,
                policySkipped
                    ? "Complex result not evaluated by product policy"
                    : "NeowsBones continuation not safely projectable",
                new[]
                {
                    new PredictedEffect(
                        PredictedEffectKind.DescriptionOnly,
                        null,
                        null,
                        finalCursePrecision,
                        distinctWarnings,
                        evidence,
                        ItemOrder: 0,
                        SourceStep: "NeowsBones shared continuation",
                        SourceRelicKey: bonesKey,
                        SourceEffectGroupId: groupId,
                        Phase: PredictedEffectPhase.SharedContinuation,
                        IsProductRelevant: true)
                },
                SourceRelicKey: bonesKey,
                SourceEffectGroupId: groupId,
                Phase: PredictedEffectPhase.SharedContinuation,
                IsProductRelevant: true);
        }

        PredictionWarningCode[] warningArray = warnings.Distinct().ToArray();
        string continuityFingerprint = BuildBonesContinuityFingerprint(branch.Continuity);
        string observable = FingerprintBonesParts(
            "bones-shared-product-observable-v3",
            new[]
            {
                curse?.Serialized ?? (policySkipped ? "curse:not-evaluated-by-policy" : "curse:indeterminate"),
                finalCursePrecision.ToString(),
                productStatus.ToString(),
                BuildWorkingStateObservableFingerprint(branch.State),
                continuityFingerprint,
                string.Join(",", warningArray.Where(IsProductRelevantWarning).OrderBy(code => code))
            });
        string relevantContinuation = canProjectCurse
            ? FingerprintBonesParts("bones-terminal-product-continuation-v3", new[]
            {
                branch.State.Authority.SourceAuthority.ToString(),
                branch.State.Authority.Completeness.ToString(),
                continuityFingerprint
            })
            : FingerprintBonesParts("bones-incomplete-product-continuation-v3", new[]
            {
                BuildWorkingStateContinuationFingerprint(branch.State),
                continuityFingerprint,
                productStatus.ToString()
            });
        EvidenceCode sharedEvidence = Evidence(bonesKey, canProjectCurse
            ? "bones-shared-product-continuation-exact"
            : policySkipped
                ? "bones-shared-product-continuation-not-evaluated-by-policy"
                : "bones-shared-product-continuation-incomplete");

        return new BonesSharedContinuationResult(
            new[] { group },
            curse,
            canProjectCurse ? PredictionPrecision.Exact : finalCursePrecision,
            warningArray,
            branch.State.Authority.SourceAuthority,
            branch.State.Authority.Completeness,
            EffectPredictionScope.FinitePlayerChoiceRoutes,
            observable,
            relevantContinuation,
            sharedEvidence,
            finalCursePrecision,
            branch.Continuity,
            productStatus);
    }

    private static PlayerChoiceImpactAnalysis BuildPlayerChoiceImpactAnalysis(
        IReadOnlyList<BonesAcquisitionRouteResult> routes)
    {
        var routeSets = new List<PlayerChoiceRouteSet>();
        foreach (IGrouping<string, BonesAcquisitionRouteResult> orderGroup in routes.GroupBy(
                     route => FormatAcquisitionOrder(route.AcquisitionOrder),
                     StringComparer.Ordinal))
        {
            foreach (ModelKey choiceRelic in orderGroup
                         .SelectMany(route => route.PlayerChoiceSelections ?? Array.Empty<PlayerChoiceSelectionTrace>())
                         .Select(trace => trace.ChoiceRelicKey)
                         .Distinct())
            {
                var candidates = new List<(BonesAcquisitionRouteResult Route, PlayerChoiceSelectionTrace Trace,
                    IReadOnlyList<PredictedEffectGroup> AutomaticGroups)>();
                foreach (BonesAcquisitionRouteResult route in orderGroup)
                {
                    foreach (PlayerChoiceSelectionTrace trace in route.PlayerChoiceSelections ?? Array.Empty<PlayerChoiceSelectionTrace>())
                    {
                        if (trace.ChoiceRelicKey != choiceRelic)
                        {
                            continue;
                        }

                        PredictedEffectGroup[] automaticGroups = route.RelicScopedResults
                            .Skip(trace.AcquisitionStepIndex + 1)
                            .SelectMany(result => result.EffectGroups)
                            .Where(group => group.IsProductRelevant && group.OrderedItems.Any(item =>
                                item.Kind is PredictedEffectKind.AutomaticEffect or PredictedEffectKind.UpgradeCard))
                            .ToArray();
                        candidates.Add((route, trace, automaticGroups));
                    }
                }

                if (candidates.Count == 0)
                {
                    continue;
                }

                // A player-choice card exists only when the choice changes a later
                // product-relevant result: an automatic target/result or the final
                // curse. The choice's own deck mutation is not sufficient.
                int distinctDownstreamImpacts = candidates
                    .Select(candidate => FingerprintDownstreamPlayerChoiceImpact(
                        candidate.Route,
                        candidate.AutomaticGroups))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                if (distinctDownstreamImpacts <= 1)
                {
                    continue;
                }

                IGrouping<string, (BonesAcquisitionRouteResult Route, PlayerChoiceSelectionTrace Trace,
                    IReadOnlyList<PredictedEffectGroup> AutomaticGroups)>[] outcomes = candidates
                    .GroupBy(candidate => FingerprintPlayerChoiceOutcome(
                        candidate.Route,
                        candidate.Trace,
                        candidate.AutomaticGroups),
                        StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToArray();
                if (outcomes.Length <= 1)
                {
                    continue;
                }

                var distinct = new List<DistinctPlayerChoiceOutcome>();
                for (int index = 0; index < outcomes.Length; index++)
                {
                    var representative = outcomes[index].First();
                    PlayerChoiceSelectionTrace[] uniqueTraces = outcomes[index]
                        .Select(item => item.Trace)
                        .GroupBy(trace => trace.TraceId, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .ToArray();
                    int equivalentCount = uniqueTraces.Sum(trace => trace.EquivalentSelectionCount);
                    int rawLegalCount = uniqueTraces.Sum(trace => trace.EquivalentSelectionCount);
                    int distinctDeckStates = uniqueTraces
                        .Select(trace => string.Join("|",
                            trace.ChoicePolicy.StableKey,
                            trace.ChoiceDeckFingerprint,
                            trace.RelevantRngFingerprint))
                        .Distinct(StringComparer.Ordinal)
                        .Count();
                    PredictionWarningCode[] warnings = outcomes[index]
                        .SelectMany(item => item.Route.WarningCodes)
                        .Where(code => IsProductRelevantRouteWarning(representative.Route, code))
                        .Distinct()
                        .ToArray();
                    EvidenceCode[] evidence = representative.Trace.ChoiceEffects
                        .Select(item => item.EvidenceCode)
                        .Concat(representative.AutomaticGroups
                            .SelectMany(group => group.OrderedItems)
                            .Select(item => item.EvidenceCode))
                        .Distinct()
                        .ToArray();
                    EffectSelectionPolicy choiceSelectionPolicy = representative.Trace.ChoicePolicy.Kind switch
                    {
                        PlayerChoicePolicyKind.SkipOfferedCard => EffectSelectionPolicy.ChooseOneOrSkip,
                        PlayerChoicePolicyKind.ChooseOfferedCardCombination => EffectSelectionPolicy.ChooseAny,
                        _ when representative.Trace.ChoicePolicy.SelectionCount == 2 => EffectSelectionPolicy.ChooseExactlyN,
                        _ => EffectSelectionPolicy.ChooseExactlyOne
                    };
                    PredictedEffectGroup choiceGroup = new(
                        representative.Route.RouteId + ".player-choice-summary",
                        0,
                        choiceSelectionPolicy,
                        EffectPredictionDomain.Neow,
                        EffectPredictionScope.FinitePlayerChoiceRoutes,
                        representative.Trace.ChoicePolicy.PolicyId,
                        representative.Trace.ChoiceEffects,
                        RequiredSelectionCount: representative.Trace.ChoicePolicy.SelectionCount,
                        SourceRelicKey: choiceRelic,
                        IsProductRelevant: true,
                        IsPlayerChoiceGroup: true);
                    distinct.Add(new DistinctPlayerChoiceOutcome(
                        $"{representative.Route.RouteId}.distinct-outcome.{index}",
                        representative.Route.RouteId,
                        choiceRelic,
                        representative.Trace.ChoicePolicy,
                        new[] { choiceGroup },
                        representative.AutomaticGroups,
                        representative.Route.FinalShadowDeckFingerprint,
                        representative.Route.SharedContinuation.FinalCurseKey,
                        representative.Route.SharedContinuation.FinalCursePrecision,
                        representative.Route.ProductRelevantProjectionPrecision,
                        representative.Route.ProductRelevantProjectionStatus,
                        warnings,
                        evidence,
                        representative.Route.SharedContinuation.SourceAuthority,
                        representative.Route.SharedContinuation.Completeness,
                        representative.Route.SharedContinuation.PredictionScope,
                        rawLegalCount,
                        distinctDeckStates,
                        equivalentCount));
                }

                PlayerChoiceSelectionTrace[] routeSetTraces = candidates
                    .Select(candidate => candidate.Trace)
                    .GroupBy(trace => trace.TraceId, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray();
                int routeSetRawLegalCount = routeSetTraces.Sum(trace => trace.EquivalentSelectionCount);
                int routeSetDistinctDeckStates = routeSetTraces
                    .Select(trace => string.Join("|",
                        trace.ChoicePolicy.StableKey,
                        trace.ChoiceDeckFingerprint,
                        trace.RelevantRngFingerprint))
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                routeSets.Add(new PlayerChoiceRouteSet(
                    $"player-choice-impact.{routeSets.Count}",
                    candidates[0].Route.AcquisitionOrder,
                    choiceRelic,
                    orderGroup.Key,
                    routeSetRawLegalCount,
                    routeSetDistinctDeckStates,
                    distinct));
            }
        }

        return routeSets.Count == 0
            ? PlayerChoiceImpactAnalysis.Empty
            : new PlayerChoiceImpactAnalysis(routeSets);
    }

    private static string FingerprintDownstreamPlayerChoiceImpact(
        BonesAcquisitionRouteResult route,
        IReadOnlyList<PredictedEffectGroup> automaticGroups) =>
        FingerprintBonesParts(
            "bones-downstream-player-choice-impact-v1",
            new[]
            {
                FingerprintEffectGroups(automaticGroups, productOnly: true),
                route.SharedContinuation.FinalCurseKey?.Serialized ?? "curse:none",
                route.SharedContinuation.FinalCursePrecision.ToString(),
                route.ProductRelevantProjectionPrecision.ToString(),
                route.ProductRelevantProjectionStatus.ToString(),
                route.SharedContinuation.SourceAuthority.ToString(),
                route.SharedContinuation.Completeness.ToString(),
                route.SharedContinuation.PredictionScope.ToString(),
                string.Join(",", route.WarningCodes.Where(code => IsProductRelevantRouteWarning(route, code)).OrderBy(code => code))
            });

    private static string FingerprintPlayerChoiceOutcome(
        BonesAcquisitionRouteResult route,
        PlayerChoiceSelectionTrace trace,
        IReadOnlyList<PredictedEffectGroup> automaticGroups) =>
        FingerprintBonesParts(
            "bones-distinct-player-choice-outcome-v2",
            new[]
            {
                trace.ChoicePolicy.StableKey,
                route.FinalShadowDeckFingerprint,
                FingerprintEffectGroups(automaticGroups, productOnly: true),
                route.SharedContinuation.FinalCurseKey?.Serialized ?? "curse:none",
                route.SharedContinuation.FinalCursePrecision.ToString(),
                route.ProductRelevantProjectionPrecision.ToString(),
                route.ProductRelevantProjectionStatus.ToString(),
                route.SharedContinuation.SourceAuthority.ToString(),
                route.SharedContinuation.Completeness.ToString(),
                route.SharedContinuation.PredictionScope.ToString(),
                string.Join(",", route.WarningCodes.Where(code => IsProductRelevantRouteWarning(route, code)).OrderBy(code => code))
            });

    private static IReadOnlyList<PredictedEffectGroup> FlattenBonesRoutes(
        IReadOnlyList<BonesAcquisitionRouteResult> routes)
    {
        var output = new List<PredictedEffectGroup>();
        int groupOrder = 0;
        for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
        {
            BonesAcquisitionRouteResult route = routes[routeIndex];
            foreach (BonesRelicScopedResult scoped in route.RelicScopedResults)
            {
                foreach (PredictedEffectGroup group in scoped.EffectGroups
                             .Where(group => !group.IsPlayerChoiceGroup)
                             .OrderBy(group => group.GroupOrder))
                {
                    output.Add(group with
                    {
                        GroupId = route.RouteId + "." + group.GroupId,
                        GroupOrder = groupOrder++,
                        SelectionSetId = "neows-bones.acquisition-order",
                        BundleId = route.RouteId,
                        BundleOrder = routeIndex
                    });
                }
            }
            foreach (PredictedEffectGroup group in route.SharedContinuation.EffectGroups.OrderBy(group => group.GroupOrder))
            {
                output.Add(group with
                {
                    GroupId = route.RouteId + "." + group.GroupId,
                    GroupOrder = groupOrder++,
                    SelectionSetId = "neows-bones.acquisition-order",
                    BundleId = route.RouteId,
                    BundleOrder = routeIndex
                });
            }
        }
        return output;
    }

    private static string BuildRelicScopedFingerprint(
        ModelKey sourceRelic,
        IReadOnlyList<PredictedEffectGroup> groups,
        PredictionPrecision precision,
        IReadOnlyList<PredictionWarningCode> warnings,
        EvidenceCode projectionEvidence,
        bool productOnly)
    {
        var selectionIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var bundleIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var offerIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = new List<string>
        {
            sourceRelic.Serialized,
            precision.ToString(),
            productOnly ? "product" : projectionEvidence.ToString(),
            string.Join(",", warnings.OrderBy(code => code))
        };

        foreach (PredictedEffectGroup group in groups
                     .Where(group => !productOnly || (group.IsProductRelevant && !group.IsPlayerChoiceGroup))
                     .OrderBy(group => group.GroupOrder))
        {
            string selectionSet = NormalizeOpaqueId(group.SelectionSetId, selectionIds, "selection");
            string bundle = NormalizeOpaqueId(group.BundleId, bundleIds, "bundle");
            parts.Add(string.Join(":",
                group.SourceRelicKey?.Serialized ?? sourceRelic.Serialized,
                group.SourceEffectGroupId ?? group.GroupId,
                group.GroupOrder,
                group.SelectionPolicy,
                group.RequiredSelectionCount?.ToString() ?? "-",
                selectionSet,
                bundle,
                group.BundleOrder?.ToString() ?? "-",
                group.Scope,
                group.Phase,
                group.RngEvidenceCode ?? "-"));

            foreach (PredictedEffect item in group.OrderedItems
                         .Where(item => !productOnly || (item.IsProductRelevant && !item.IsPlayerChoiceEffect))
                         .OrderBy(item => item.ItemOrder))
            {
                parts.Add(string.Join(":",
                    item.SourceRelicKey?.Serialized ?? sourceRelic.Serialized,
                    item.SourceEffectGroupId ?? group.SourceEffectGroupId ?? group.GroupId,
                    item.ItemOrder,
                    item.Kind,
                    item.TargetKey?.Serialized ?? "-",
                    item.SourceKey?.Serialized ?? "-",
                    item.Amount?.ToString() ?? "-",
                    item.Multiplicity,
                    NormalizeOpaqueId(item.OfferItemId, offerIds, "offer"),
                    item.Precision,
                    item.Phase,
                    item.Relation,
                    productOnly ? "product" : item.EvidenceCode.ToString(),
                    string.Join(",", item.WarningCodes.Where(code => !productOnly || IsProductRelevantWarning(code)).OrderBy(code => code))));
            }
        }

        return FingerprintBonesParts(productOnly ? "bones-relic-product-v3" : "bones-relic-full-v3", parts);
    }

    private static string FingerprintEffectGroups(
        IReadOnlyList<PredictedEffectGroup> groups,
        bool productOnly) =>
        FingerprintBonesParts(
            productOnly ? "effect-groups-product-v1" : "effect-groups-full-v1",
            groups
                .Where(group => !productOnly || group.IsProductRelevant)
                .OrderBy(group => group.GroupOrder)
                .SelectMany(group => group.OrderedItems
                    .Where(item => !productOnly || item.IsProductRelevant)
                    .OrderBy(item => item.ItemOrder)
                    .Select(item => string.Join(":",
                        group.SourceRelicKey?.Serialized ?? "-",
                        group.SourceEffectGroupId ?? group.GroupId,
                        item.Kind,
                        item.TargetKey?.Serialized ?? "-",
                        item.SourceKey?.Serialized ?? "-",
                        item.Amount?.ToString() ?? "-",
                        item.Multiplicity,
                        item.Relation,
                        item.Precision))));

    private static string NormalizeOpaqueId(
        string? value,
        IDictionary<string, string> map,
        string prefix)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }
        if (!map.TryGetValue(value, out string? normalized))
        {
            normalized = prefix + map.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            map.Add(value, normalized);
        }
        return normalized;
    }

    private static string BuildBonesContinuityFingerprint(BonesDependencyContinuity continuity) =>
        FingerprintBonesParts(
            "bones-dependency-continuity-v1",
            new[]
            {
                FormatContinuityDomain("rewards", continuity.RewardsRng),
                FormatContinuityDomain("niche", continuity.NicheRng),
                FormatContinuityDomain("transformations", continuity.TransformationsRng),
                FormatContinuityDomain("shadow", continuity.ShadowDeck),
                FormatContinuityDomain("relic-bag", continuity.RelicBag),
                FormatContinuityDomain("curse-pool", continuity.GeneratedCursePool),
                FormatContinuityDomain("unknown-hook", continuity.UnknownHook),
                FormatContinuityDomain("nested-obtain", continuity.NestedObtain),
                FormatContinuityDomain("player-choice", continuity.PlayerChoice)
            });

    private static string FormatContinuityDomain(
        string name,
        BonesContinuationDomainState domain) =>
        string.Join(":",
            name,
            domain.Status,
            string.Join(",", domain.WarningCodes.OrderBy(code => code)),
            string.Join(",", domain.EvidenceCodes.Select(code => code.ToString()).OrderBy(code => code, StringComparer.Ordinal)));

    private static string BuildWorkingStateObservableFingerprint(NeowEffectWorkingState state) =>
        FingerprintBonesParts(
            "bones-working-observable-v1",
            new[]
            {
                state.Deck is null ? "deck:missing" : BuildObservableDeckFingerprint(state.Deck.Cards),
                state.RelicBag is null
                    ? "bag:missing"
                    : string.Join("|", state.RelicBag
                        .OrderBy(relic => relic.BagOrder)
                        .Select(relic => relic.RelicKey.Serialized + ":" + relic.BagOrder))
            });

    private static string BuildWorkingStateContinuationFingerprint(NeowEffectWorkingState state) =>
        FingerprintBonesParts(
            "bones-working-continuation-v1",
            new[]
            {
                RngFingerprint("rewards", state.Rng.Rewards),
                RngFingerprint("transformations", state.Rng.Transformations),
                RngFingerprint("niche", state.Rng.Niche),
                RngFingerprint("combat-potion", state.Rng.CombatPotionGeneration),
                BuildWorkingStateObservableFingerprint(state)
            });

    private static string RngFingerprint(string name, RolltheSpire2.Core.Rng.Xoshiro256StarStar rng)
    {
        (ulong s0, ulong s1, ulong s2, ulong s3) = rng.State;
        return string.Join(":",
            name,
            s0.ToString("X16"),
            s1.ToString("X16"),
            s2.ToString("X16"),
            s3.ToString("X16"),
            rng.CallCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static PredictionPrecision MergeProductPrecision(PredictionPrecision left, PredictionPrecision right)
    {
        if (left == PredictionPrecision.Unknown || right == PredictionPrecision.Unknown)
        {
            return PredictionPrecision.Unknown;
        }
        if (left == PredictionPrecision.Unsupported || right == PredictionPrecision.Unsupported)
        {
            return PredictionPrecision.Unsupported;
        }
        if (left == PredictionPrecision.DescriptionOnly || right == PredictionPrecision.DescriptionOnly)
        {
            return PredictionPrecision.DescriptionOnly;
        }
        return left == PredictionPrecision.Exact && right == PredictionPrecision.Exact
            ? PredictionPrecision.Exact
            : PredictionPrecision.Partial;
    }

    private static ProductRelevantProjectionStatus MergeProductStatuses(
        IEnumerable<ProductRelevantProjectionStatus> statuses)
    {
        ProductRelevantProjectionStatus[] values = statuses.ToArray();
        if (values.Contains(ProductRelevantProjectionStatus.Unknown))
        {
            return ProductRelevantProjectionStatus.Unknown;
        }
        if (values.Contains(ProductRelevantProjectionStatus.Unsupported))
        {
            return ProductRelevantProjectionStatus.Unsupported;
        }
        if (values.Contains(ProductRelevantProjectionStatus.NotEvaluatedByPolicy))
        {
            return ProductRelevantProjectionStatus.NotEvaluatedByPolicy;
        }
        return ProductRelevantProjectionStatus.Evaluated;
    }

    private static bool IsProductRelevantWarning(PredictionWarningCode code) => code is not
        PredictionWarningCode.EffectNotImplemented and not
        PredictionWarningCode.ComplexRouteNotEvaluated;

    private static bool IsProductRelevantRouteWarning(
        BonesAcquisitionRouteResult route,
        PredictionWarningCode code) =>
        IsProductRelevantWarning(code) &&
        !(route.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
          route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
          code == PredictionWarningCode.EffectSnapshotIncomplete);

    private static string FormatAcquisitionOrder(IReadOnlyList<ModelKey> order) =>
        string.Join(">", order.Select(key => key.Serialized));

    private static string FingerprintBonesParts(string prefix, IEnumerable<string> parts)
    {
        string payload = prefix + "\n" + string.Join("\n", parts);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
