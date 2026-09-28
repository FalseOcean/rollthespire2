using System.Security.Cryptography;
using System.Text;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.Effects;

/// <summary>
/// Pure classifier for Neow's Bones acquisition-order outcomes. The classifier
/// compares the set of distinct product-relevant outcomes for each original
/// acquisition order; raw player card-instance branches remain in OriginalRoutes
/// but never become one ordinary outcome card apiece.
/// </summary>
public static class BonesOrderImpactAnalyzer
{
    public static BonesOutcomeAnalysis Analyze(
        IReadOnlyList<ModelKey> offeredRelics,
        IReadOnlyList<BonesAcquisitionRouteResult> originalRoutes)
    {
        ArgumentNullException.ThrowIfNull(offeredRelics);
        ArgumentNullException.ThrowIfNull(originalRoutes);

        if (originalRoutes.Count == 0)
        {
            return new BonesOutcomeAnalysis(
                offeredRelics.ToArray(),
                Array.Empty<BonesAcquisitionRouteResult>(),
                Array.Empty<BonesOutcomeGroup>(),
                AcquisitionOrderImpact.Indeterminate,
                OrderComparisonStatus: OrderComparisonStatus.IndeterminateBecauseAuthorityMissing,
                OrderComparisonEvidence: new[] { BonesOrderDifferenceCode.IncompleteProjection });
        }

        IGrouping<string, BonesAcquisitionRouteResult>[] orderSets = originalRoutes
            .GroupBy(route => FormatAcquisitionOrder(route.AcquisitionOrder), StringComparer.Ordinal)
            .OrderBy(group => FirstOrderIndex(originalRoutes, group.Key))
            .ToArray();

        if (orderSets.Length != 2)
        {
            BonesOutcomeGroup[] incomplete = orderSets.Select((set, index) =>
            {
                BonesAcquisitionRouteResult[] routes = set.ToArray();
                return new BonesOutcomeGroup(
                    $"bones.outcome.{index}",
                    AcquisitionOrderImpact.Indeterminate,
                    routes[0],
                    new[] { routes[0].AcquisitionOrder },
                    routes,
                    Array.Empty<BonesOrderDifferenceCode>());
            }).ToArray();
            BonesOrderDifferenceCode[] comparisonEvidence =
            {
                BonesOrderDifferenceCode.IncompleteProjection,
                BonesOrderDifferenceCode.RouteSetDiffers
            };
            return new BonesOutcomeAnalysis(
                offeredRelics.ToArray(),
                originalRoutes.ToArray(),
                incomplete,
                AcquisitionOrderImpact.Indeterminate,
                OrderComparisonStatus: ResolveIndeterminateComparisonStatus(originalRoutes, comparisonEvidence),
                OrderComparisonEvidence: comparisonEvidence);
        }

        BonesAcquisitionRouteResult[] left = orderSets[0].ToArray();
        BonesAcquisitionRouteResult[] right = orderSets[1].ToArray();
        string leftSetFingerprint = BuildOrderOutcomeSetFingerprint(left);
        string rightSetFingerprint = BuildOrderOutcomeSetFingerprint(right);
        bool leftComparable = left.All(IsProductComparable);
        bool rightComparable = right.All(IsProductComparable);
        IReadOnlyList<BonesOrderDifferenceCode> differences = CompareOrderSets(left, right);

        if (leftComparable && rightComparable &&
            string.Equals(leftSetFingerprint, rightSetFingerprint, StringComparison.Ordinal))
        {
            var independent = new BonesOutcomeGroup(
                "bones.outcome.0",
                AcquisitionOrderImpact.ProvenIndependent,
                left[0],
                new[] { left[0].AcquisitionOrder, right[0].AcquisitionOrder },
                left.Concat(right).ToArray(),
                Array.Empty<BonesOrderDifferenceCode>());
            return new BonesOutcomeAnalysis(
                offeredRelics.ToArray(),
                originalRoutes.ToArray(),
                new[] { independent },
                AcquisitionOrderImpact.ProvenIndependent,
                OrderComparisonStatus: OrderComparisonStatus.ProvenIndependent,
                OrderComparisonEvidence: Array.Empty<BonesOrderDifferenceCode>());
        }

        bool boundedForKnownDifference = left.All(HasBoundedProductUncertainty) &&
                                         right.All(HasBoundedProductUncertainty);
        BonesOrderDifferenceCode[] provenDifferences = boundedForKnownDifference
            ? differences.Where(IsProvenProductDifference).Distinct().ToArray()
            : Array.Empty<BonesOrderDifferenceCode>();
        bool provenSensitive = provenDifferences.Length > 0;

        if (provenSensitive)
        {
            BonesOutcomeGroup[] sensitiveGroups =
            {
                new(
                    "bones.outcome.0",
                    AcquisitionOrderImpact.ProvenSensitive,
                    left[0],
                    new[] { left[0].AcquisitionOrder },
                    left,
                    differences),
                new(
                    "bones.outcome.1",
                    AcquisitionOrderImpact.ProvenSensitive,
                    right[0],
                    new[] { right[0].AcquisitionOrder },
                    right,
                    differences)
            };
            return new BonesOutcomeAnalysis(
                offeredRelics.ToArray(),
                originalRoutes.ToArray(),
                sensitiveGroups,
                AcquisitionOrderImpact.ProvenSensitive,
                OrderComparisonStatus: OrderComparisonStatus.ProvenSensitive,
                OrderComparisonEvidence: differences);
        }

        IReadOnlyList<BonesOrderDifferenceCode> incompleteEvidence =
            BuildIncompleteEvidence(left, right, differences);
        BonesOutcomeGroup[] indeterminateGroups =
        {
            new(
                "bones.outcome.0",
                AcquisitionOrderImpact.Indeterminate,
                left[0],
                new[] { left[0].AcquisitionOrder },
                left,
                Array.Empty<BonesOrderDifferenceCode>()),
            new(
                "bones.outcome.1",
                AcquisitionOrderImpact.Indeterminate,
                right[0],
                new[] { right[0].AcquisitionOrder },
                right,
                Array.Empty<BonesOrderDifferenceCode>())
        };
        return new BonesOutcomeAnalysis(
            offeredRelics.ToArray(),
            originalRoutes.ToArray(),
            indeterminateGroups,
            AcquisitionOrderImpact.Indeterminate,
            OrderComparisonStatus: ResolveIndeterminateComparisonStatus(originalRoutes, incompleteEvidence),
            OrderComparisonEvidence: incompleteEvidence);
    }

    public static IReadOnlyList<BonesOrderDifferenceCode> Compare(
        BonesAcquisitionRouteResult left,
        BonesAcquisitionRouteResult right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return CompareOrderSets(new[] { left }, new[] { right });
    }

    public static string BuildOrderNormalizedOutcomeFingerprint(BonesAcquisitionRouteResult route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return FingerprintParts(
            "bones-product-order-normalized-outcome-v3",
            route.RelicScopedResults
                .OrderBy(result => result.SourceRelicKey.Serialized, StringComparer.Ordinal)
                .Select(result => result.SourceRelicKey.Serialized + "=" + ProductFingerprint(result))
                .Concat(new[]
                {
                    route.ProductRelevantObservableFingerprint,
                    route.RelevantContinuationFingerprint,
                    route.ProductRelevantProjectionPrecision.ToString(),
                    route.ProductRelevantProjectionStatus.ToString(),
                    EffectiveRouteStatus(route).ToString(),
                    route.SharedContinuation.FinalCurseKey?.Serialized ?? "curse:none",
                    route.SharedContinuation.FinalCursePrecision.ToString(),
                    AuthorityFingerprint(route),
                    FormatProductDependencyContinuity(route.DependencyContinuity),
                    string.Join(",", route.WarningCodes
                        .Where(code => IsProductWarning(route, code))
                        .OrderBy(code => code))
                }));
    }

    private static IReadOnlyList<BonesOrderDifferenceCode> CompareOrderSets(
        IReadOnlyList<BonesAcquisitionRouteResult> left,
        IReadOnlyList<BonesAcquisitionRouteResult> right)
    {
        var differences = new List<BonesOrderDifferenceCode>();
        if (!SetEquals(left.Select(ProductRelicFingerprint), right.Select(ProductRelicFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.RelicScopedResultsDiffer);
        }
        if (!SetEquals(left.Select(FinalCurseFingerprint), right.Select(FinalCurseFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.FinalCurseDiffers);
            differences.Add(BonesOrderDifferenceCode.SharedContinuationDiffers);
        }
        if (!SetEquals(left.Select(route => route.ProductRelevantObservableFingerprint),
                right.Select(route => route.ProductRelevantObservableFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.ObservableStateDiffers);
        }
        if (!SetEquals(left.Select(RelevantContinuationFingerprint), right.Select(RelevantContinuationFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.RelevantContinuationDiffers);
            differences.Add(BonesOrderDifferenceCode.SharedContinuationDiffers);
        }
        if (!SetEquals(left.Select(ProductPrecisionFingerprint), right.Select(ProductPrecisionFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.PrecisionDiffers);
        }
        if (!SetEquals(left.Select(ProductWarningFingerprint), right.Select(ProductWarningFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.WarningScopeDiffers);
        }
        if (!SetEquals(left.Select(AuthorityFingerprint), right.Select(AuthorityFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.AuthorityOrCompletenessDiffers);
        }
        if (!SetEquals(left.Select(BuildOrderNormalizedOutcomeFingerprint),
                right.Select(BuildOrderNormalizedOutcomeFingerprint)))
        {
            differences.Add(BonesOrderDifferenceCode.RouteSetDiffers);
        }

        return differences.Distinct().ToArray();
    }

    private static IReadOnlyList<BonesOrderDifferenceCode> BuildIncompleteEvidence(
        IReadOnlyList<BonesAcquisitionRouteResult> left,
        IReadOnlyList<BonesAcquisitionRouteResult> right,
        IReadOnlyList<BonesOrderDifferenceCode> differences)
    {
        var evidence = new List<BonesOrderDifferenceCode>
        {
            BonesOrderDifferenceCode.IncompleteProjection
        };
        evidence.AddRange(differences);
        IEnumerable<PredictionWarningCode> warnings = left.Concat(right).SelectMany(route => route.WarningCodes);
        if (warnings.Contains(PredictionWarningCode.EffectNestedObtainIncomplete))
        {
            evidence.Add(BonesOrderDifferenceCode.UnknownNestedHook);
        }
        if (warnings.Contains(PredictionWarningCode.EffectBranchBudgetExceeded))
        {
            evidence.Add(BonesOrderDifferenceCode.BranchBudgetExceeded);
        }
        return evidence.Distinct().ToArray();
    }

    private static string BuildOrderOutcomeSetFingerprint(
        IReadOnlyList<BonesAcquisitionRouteResult> routes) =>
        FingerprintParts(
            "bones-product-order-outcome-set-v1",
            routes.Select(BuildOrderNormalizedOutcomeFingerprint)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal));

    private static bool IsProductComparable(BonesAcquisitionRouteResult route) =>
        EffectiveRouteStatus(route) == RouteProjectionStatus.Exact &&
        HasBoundedProductUncertainty(route);

    private static bool HasBoundedProductUncertainty(BonesAcquisitionRouteResult route)
    {
        BonesDependencyContinuity? continuity = route.DependencyContinuity;
        return continuity is not null &&
               continuity.NicheRng.IsExact &&
               continuity.ShadowDeck.IsExact &&
               continuity.GeneratedCursePool.IsExact &&
               continuity.UnknownHook.IsExact &&
               continuity.NestedObtain.IsExact &&
               continuity.PlayerChoice.IsExact &&
               route.SharedContinuation.FinalCursePrecision == PredictionPrecision.Exact;
    }

    private static bool IsProvenProductDifference(BonesOrderDifferenceCode code) => code is
        BonesOrderDifferenceCode.RelicScopedResultsDiffer or
        BonesOrderDifferenceCode.FinalCurseDiffers or
        BonesOrderDifferenceCode.ObservableStateDiffers;

    private static OrderComparisonStatus ResolveIndeterminateComparisonStatus(
        IEnumerable<BonesAcquisitionRouteResult> routes,
        IEnumerable<BonesOrderDifferenceCode> evidence)
    {
        BonesAcquisitionRouteResult[] routeArray = routes.ToArray();
        if (routeArray.Any(route => EffectiveRouteStatus(route) == RouteProjectionStatus.NotEvaluatedByPolicy))
        {
            return OrderComparisonStatus.IndeterminateBecausePolicyDisabled;
        }
        if (evidence.Contains(BonesOrderDifferenceCode.BranchBudgetExceeded) ||
            routeArray.SelectMany(route => route.WarningCodes)
                .Contains(PredictionWarningCode.EffectBranchBudgetExceeded))
        {
            return OrderComparisonStatus.IndeterminateBecauseBranchBudget;
        }
        return OrderComparisonStatus.IndeterminateBecauseAuthorityMissing;
    }

    private static RouteProjectionStatus EffectiveRouteStatus(BonesAcquisitionRouteResult route)
    {
        if (route.RouteProjectionStatus != RouteProjectionStatus.Unknown)
        {
            return route.RouteProjectionStatus;
        }

        return route.ProductRelevantProjectionStatus switch
        {
            ProductRelevantProjectionStatus.NotEvaluatedByPolicy => RouteProjectionStatus.NotEvaluatedByPolicy,
            ProductRelevantProjectionStatus.Unsupported => RouteProjectionStatus.Unsupported,
            ProductRelevantProjectionStatus.Unknown => RouteProjectionStatus.Unknown,
            ProductRelevantProjectionStatus.Evaluated
                when route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
                     route.SharedContinuation.FinalCursePrecision == PredictionPrecision.Exact =>
                RouteProjectionStatus.Exact,
            ProductRelevantProjectionStatus.Evaluated => RouteProjectionStatus.Partial,
            _ => RouteProjectionStatus.Unknown
        };
    }

    private static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right)
    {
        string[] leftValues = left.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] rightValues = right.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return leftValues.SequenceEqual(rightValues, StringComparer.Ordinal);
    }

    private static int FirstOrderIndex(
        IReadOnlyList<BonesAcquisitionRouteResult> routes,
        string order)
    {
        for (int index = 0; index < routes.Count; index++)
        {
            if (string.Equals(FormatAcquisitionOrder(routes[index].AcquisitionOrder), order, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return int.MaxValue;
    }

    private static string ProductRelicFingerprint(BonesAcquisitionRouteResult route) =>
        FingerprintParts(
            "bones-product-relic-scopes-v3",
            route.RelicScopedResults
                .OrderBy(result => result.SourceRelicKey.Serialized, StringComparer.Ordinal)
                .Select(result => result.SourceRelicKey.Serialized + "=" + ProductFingerprint(result)));

    private static string ProductFingerprint(BonesRelicScopedResult result) =>
        string.IsNullOrWhiteSpace(result.ProductRelevantObservableFingerprint)
            ? result.ObservableFingerprint
            : result.ProductRelevantObservableFingerprint;

    private static string FinalCurseFingerprint(BonesAcquisitionRouteResult route) => string.Join(":",
        route.SharedContinuation.FinalCurseKey?.Serialized ?? "curse:none",
        route.SharedContinuation.FinalCursePrecision,
        route.SharedContinuation.ProductRelevantProjectionStatus);

    private static string RelevantContinuationFingerprint(BonesAcquisitionRouteResult route) => string.Join("|",
        route.RelevantContinuationFingerprint,
        FormatProductDependencyContinuity(route.DependencyContinuity));

    private static string ProductPrecisionFingerprint(BonesAcquisitionRouteResult route) => string.Join(":",
        route.ProductRelevantProjectionPrecision,
        route.ProductRelevantProjectionStatus,
        EffectiveRouteStatus(route),
        route.SharedContinuation.FinalCursePrecision);

    private static string ProductWarningFingerprint(BonesAcquisitionRouteResult route) => string.Join(",",
        route.WarningCodes.Where(code => IsProductWarning(route, code)).OrderBy(code => code));

    private static string AuthorityFingerprint(BonesAcquisitionRouteResult route) => string.Join(":",
        route.SharedContinuation.SourceAuthority,
        route.SharedContinuation.Completeness,
        route.SharedContinuation.PredictionScope);

    private static bool IsProductWarning(BonesAcquisitionRouteResult route, PredictionWarningCode code) =>
        code is not PredictionWarningCode.EffectNotImplemented and not PredictionWarningCode.ComplexRouteNotEvaluated &&
        !(route.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Evaluated &&
          route.ProductRelevantProjectionPrecision == PredictionPrecision.Exact &&
          code == PredictionWarningCode.EffectSnapshotIncomplete);

    private static string FormatProductDependencyContinuity(BonesDependencyContinuity? continuity)
    {
        if (continuity is null)
        {
            return "continuity:not-recorded";
        }
        return string.Join("|",
            FormatDomain("rewards", continuity.RewardsRng),
            FormatDomain("niche", continuity.NicheRng),
            FormatDomain("transformations", continuity.TransformationsRng),
            FormatDomain("shadow", continuity.ShadowDeck),
            FormatDomain("relic-bag", continuity.RelicBag),
            FormatDomain("curse-pool", continuity.GeneratedCursePool),
            FormatDomain("unknown-hook", continuity.UnknownHook),
            FormatDomain("nested-obtain", continuity.NestedObtain),
            FormatDomain("player-choice", continuity.PlayerChoice));
    }

    private static string FormatDomain(string name, BonesContinuationDomainState domain) =>
        string.Join(":",
            name,
            domain.Status,
            string.Join(",", domain.WarningCodes.OrderBy(code => code)),
            string.Join(",", domain.EvidenceCodes.Select(code => code.ToString())
                .OrderBy(code => code, StringComparer.Ordinal)));

    private static string FormatAcquisitionOrder(IReadOnlyList<ModelKey> order) =>
        string.Join(">", order.Select(key => key.Serialized));

    private static string FingerprintParts(string prefix, IEnumerable<string> parts)
    {
        string payload = prefix + "\n" + string.Join("\n", parts);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
