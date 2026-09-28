using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

internal sealed record ProductionExactSearchResult(
    SeedPredictionRequest? Request,
    SeedPredictionDocument? Document,
    RuntimeContextAuthoritySnapshot Authority,
    SearchQueryEvaluation Evaluation,
    string FailureCode)
{
    public bool IsMatch => Evaluation.Disposition == SearchDisposition.Match && Request is not null && Document is not null;
}

/// <summary>
/// Production validation stage: call the version-bound Predictor for the required
/// domains and validate its result against Query. Filter predicates cannot emit
/// a final Match; SearchQueryEvaluation carries the validated route evidence.
/// </summary>
internal static class ProductionExactSearchEvaluator
{
    internal static ProductionExactSearchResult Evaluate(
        SearchExecutionRequest legacyPlan,
        TrustedRootHashInput input) => EvaluateLegacy(legacyPlan, input);

    /// <summary>Legacy diagnostics only. Production Family Execution calls the Exact overload.</summary>
    internal static ProductionExactSearchResult EvaluateLegacy(
        SearchExecutionRequest legacyPlan,
        TrustedRootHashInput input) => Evaluate(
            new ExactSearchExecutionRequest(
                legacyPlan.CompiledSearch,
                legacyPlan.RunOptions,
                legacyPlan.CanonicalStartSeed,
                legacyPlan.ResolvedScanCount,
                ExactSearchEvaluationProjection.FromLegacyFilter(legacyPlan.Filter),
                legacyPlan.CombatRewardRoutePolicy,
                legacyPlan.SnapshotFingerprint),
            input);

    public static ProductionExactSearchResult Evaluate(
        ExactSearchExecutionRequest plan,
        TrustedRootHashInput input)
    {
        RuntimeContextAuthoritySnapshot authority;
        if (plan.Evaluation.RequiresEffectColdPath || plan.Evaluation.RequiresWorldAuthority)
        {
            if (!SearchAuthorityProjector.TryProjectForRootHash(
                    plan,
                    input.RootHash,
                    input.SeedIdentity,
                    out authority,
                    out SearchDisposition projectionDisposition,
                    out string projectionIssue))
            {
                SearchQueryEvaluation projectionFailure = projectionDisposition switch
                {
                    SearchDisposition.Unsupported => SearchQueryEvaluation.Unsupported(projectionIssue),
                    SearchDisposition.NotEvaluatedByPolicy => SearchQueryEvaluation.NotEvaluatedByPolicy(projectionIssue),
                    _ => SearchQueryEvaluation.Unknown(projectionIssue)
                };
                return new ProductionExactSearchResult(null, null, plan.Authority, projectionFailure, projectionIssue);
            }
        }
        else
        {
            authority = SearchAuthorityProjector.PreserveSeedIndependentEffectFoundation(plan.Authority);
        }

        // Canonical Event Result and Merchant Colorless are global/root-local facts.
        // Prove them directly before document/route evaluation so neither Event
        // occurrence nor OpeningRoute precision can reject an otherwise exact result.
        SearchQueryEvaluation canonicalEvaluation = Beta111CanonicalEventShopSearchEvaluator.Evaluate(
            plan,
            input.RootHash,
            authority);
        if (canonicalEvaluation.Disposition != SearchDisposition.Match)
        {
            return new ProductionExactSearchResult(
                null,
                null,
                authority,
                canonicalEvaluation,
                canonicalEvaluation.FailureCode);
        }

        CharacterIdentity character = CharacterIdentity.FromKey(plan.CharacterKey);
        int relicSequencePreviewCount = plan.Evaluation.RelicSequenceConditions.Count == 0
            ? SeedPredictionInputLimits.DefaultRelicSequencePreviewCount
            : Math.Max(
                SeedPredictionInputLimits.DefaultRelicSequencePreviewCount,
                plan.Evaluation.RelicSequenceConditions.Max(condition => condition.RangeValue));

        if (!SeedPredictionRequest.TryCreateFromRootHash(
                input,
                character,
                plan.Ascension,
                authority.PlayersCount,
                authority.PlayerSlotIndex,
                authority,
                plan.Evaluation.AncientOptionConditions,
                plan.Evaluation.CheapPredictionDomains,
                relicSequencePreviewCount,
                plan.IncludeDiagnostics,
                out SeedPredictionRequest? cheapRequest,
                out SeedPredictionRequestError requestError))
        {
            string failure = "CheapPathRequestRejected:" + requestError;
            return new ProductionExactSearchResult(
                null,
                null,
                authority,
                SearchQueryEvaluation.Unknown(failure),
                failure);
        }

        // Canonical-only Event/Shop queries do not run the general Predictor at all.
        // Production truth is already proven by the root-local evaluator above; invoking
        // unrelated Neow/World analysis here would let non-observable state or invariant
        // failures reacquire rejection authority. A minimal document is retained only
        // because SearchCandidate currently carries the shared analysis payload contract.
        if (plan.Evaluation.RequiresCanonicalRootLocalDomain &&
            plan.Evaluation.CheapPredictionDomains == SeedPredictionDomainSelection.None &&
            !plan.Evaluation.RequiresNormalCombatRewardDomain)
        {
            SeedPredictionDocument canonicalDocument = CreateCanonicalOnlyDocument(
                cheapRequest!,
                plan,
                input);
            SearchQueryEvaluation canonicalOnly = AttachCanonicalGlobalWitness(
                canonicalEvaluation,
                canonicalDocument.CanonicalSeed);
            return new ProductionExactSearchResult(
                cheapRequest,
                canonicalDocument,
                authority,
                canonicalOnly,
                canonicalOnly.FailureCode);
        }

        cheapRequest = cheapRequest!.WithComplexBonesDeckInteractions(
            plan.CompiledSearch.RequiresComplexBonesDeckInteractionEvaluation);
        SeedPredictionDocument cheapDocument = RuntimeProfileRegistry.PredictFromRootHash(plan.Detection, cheapRequest, input);
        ThrowIfPredictionFailed(cheapDocument);
        SearchQueryEvaluation cheapEvaluation = ProductionQueryValidator.EvaluateCheap(plan, cheapDocument, authority);
        cheapEvaluation = MergeCanonicalGlobalEvidence(cheapEvaluation, canonicalEvaluation);
        if (cheapEvaluation.Disposition != SearchDisposition.Match || !plan.Evaluation.RequiresNormalCombatRewardDomain)
        {
            return new ProductionExactSearchResult(
                cheapRequest,
                cheapDocument,
                authority,
                cheapEvaluation,
                cheapEvaluation.FailureCode);
        }

        SeedPredictionDomainSelection rewardDomains =
            SeedPredictionDomainSelection.Neow |
            SeedPredictionDomainSelection.NormalCombatRewards;
        NormalCombatRewardProjectionRequest rewardProjectionRequest = BuildRewardProjectionRequest(plan);
        if (!SeedPredictionRequest.TryCreateFromRootHash(
                input,
                character,
                plan.Ascension,
                authority.PlayersCount,
                authority.PlayerSlotIndex,
                authority,
                plan.Evaluation.AncientOptionConditions,
                rewardDomains,
                relicSequencePreviewCount,
                plan.IncludeDiagnostics,
                rewardProjectionRequest,
                out SeedPredictionRequest? rewardRequest,
                out requestError))
        {
            string failure = "RewardPathRequestRejected:" + requestError;
            return new ProductionExactSearchResult(
                cheapRequest,
                cheapDocument,
                authority,
                SearchQueryEvaluation.Unknown(failure),
                failure);
        }

        rewardRequest = rewardRequest!.WithComplexBonesDeckInteractions(
            plan.CompiledSearch.RequiresComplexBonesDeckInteractionEvaluation);
        SeedPredictionDocument rewardDocument = RuntimeProfileRegistry.PredictFromRootHash(plan.Detection, rewardRequest, input);
        ThrowIfPredictionFailed(rewardDocument);
        SearchQueryEvaluation evaluation = ProductionQueryValidator.EvaluateRewards(plan, rewardDocument, cheapEvaluation);
        return new ProductionExactSearchResult(
            rewardRequest,
            rewardDocument,
            authority,
            evaluation,
            evaluation.FailureCode);
    }

    // Error is not a semantic Unknown/NoMatch. Do not let a failed computation
    // mark its candidate complete and advance the session's committed prefix.
    internal static void ThrowIfPredictionFailed(SeedPredictionDocument document)
    {
        if (!document.Warnings.Any(w => w.Code == PredictionWarningCode.AnalysisFailed)) return;
        string evidence = string.Join(" | ", document.Diagnostics
            .Where(d => d.Code == PredictionDiagnosticCodes.Exception).Select(d => d.Value));
        throw new InvalidOperationException($"ProductionExact.AnalysisFailed:{document.PredictorId}:{document.CanonicalSeed}:{evidence}");
    }

    private static SeedPredictionDocument CreateCanonicalOnlyDocument(
        SeedPredictionRequest request,
        ExactSearchExecutionRequest plan,
        TrustedRootHashInput input) => new()
    {
        Context = SeedPredictionDocument.CreateContext(request, plan.Detection.DisplayVersion, plan.ProfileId),
        PredictorId = "Beta111CanonicalEventShopSearchEvaluator",
        OriginalSeed = request.OriginalSeed,
        CanonicalSeed = input.CanonicalSeed,
        Sections = Array.Empty<PredictionSection>(),
        Warnings = Array.Empty<PredictionWarning>(),
        Diagnostics = Array.Empty<PredictionDiagnostic>(),
        OverallStatus = SeedPredictionOverallStatus.Completed,
        ProductRelevantProjectionPrecision = PredictionPrecision.Exact,
        ProductRelevantProjectionStatus = ProductRelevantProjectionStatus.Evaluated,
        FullEffectSemanticsCompleteness = FullEffectSemanticsCompleteness.NotEvaluated
    };

    private static NormalCombatRewardProjectionRequest BuildRewardProjectionRequest(ExactSearchExecutionRequest plan) =>
        plan.CombatRewardRoutePolicy.ExactPolicy switch
        {
            CombatRewardExactRoutePolicy.PinnedRealRoute when plan.Evaluation.NeowRoute is { IsValid: true } pinned =>
                new NormalCombatRewardProjectionRequest(
                    NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.PinnedRealRoute,
                    pinned.RouteRelicKey,
                    plan.Evaluation.RequiredBonesAcquisitionOrder),
            CombatRewardExactRoutePolicy.UnpinnedVerifyNeutralRealRoute =>
                new NormalCombatRewardProjectionRequest(
                    NormalCombatRewardProjectionScope.SearchExact,
                    NormalCombatRewardRouteSelectionMode.ProvablyNeutralRealRoutes),
            // SearchExact + AllRealRoutes is deliberately rejected by the
            // predictor in P10-RP1, so a missing/unknown policy fails closed.
            _ => new NormalCombatRewardProjectionRequest(
                NormalCombatRewardProjectionScope.SearchExact,
                NormalCombatRewardRouteSelectionMode.AllRealRoutes)
        };

    private static SearchQueryEvaluation AttachCanonicalGlobalWitness(
        SearchQueryEvaluation canonicalEvaluation,
        string canonicalSeed)
    {
        if (canonicalEvaluation.Disposition != SearchDisposition.Match)
            return canonicalEvaluation;

        string[] conditionIds = canonicalEvaluation.Evidence
            .Select(item => item.ConditionId ?? item.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        EvidenceCode[] evidenceCodes = canonicalEvaluation.Evidence
            .Select(item => item.EvidenceCode)
            .Where(code => !string.IsNullOrWhiteSpace(code.Value))
            .Distinct()
            .ToArray();
        var witness = new SearchMatchWitness(
            canonicalSeed,
            string.Empty,
            Array.Empty<ModelKey>(),
            conditionIds,
            string.Empty,
            null,
            evidenceCodes);
        return SearchQueryEvaluation.Match(
            canonicalEvaluation.Evidence,
            Array.Empty<string>(),
            new[] { witness });
    }

    private static SearchQueryEvaluation MergeCanonicalGlobalEvidence(
        SearchQueryEvaluation documentEvaluation,
        SearchQueryEvaluation canonicalEvaluation)
    {
        if (documentEvaluation.Disposition != SearchDisposition.Match ||
            canonicalEvaluation.Disposition != SearchDisposition.Match ||
            canonicalEvaluation.Evidence.Count == 0)
        {
            return documentEvaluation;
        }

        SearchMatchEvidence[] evidence = documentEvaluation.Evidence
            .Concat(canonicalEvaluation.Evidence)
            .Distinct()
            .ToArray();
        string[] conditionIds = canonicalEvaluation.Evidence
            .Select(item => item.ConditionId ?? item.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        EvidenceCode[] evidenceCodes = canonicalEvaluation.Evidence
            .Select(item => item.EvidenceCode)
            .Where(code => !string.IsNullOrWhiteSpace(code.Value))
            .Distinct()
            .ToArray();
        SearchMatchWitness[] witnesses = documentEvaluation.Witnesses
            .Select(witness => witness with
            {
                MatchedConditions = witness.MatchedConditions
                    .Concat(conditionIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                EvidenceCodes = witness.EvidenceCodes
                    .Concat(evidenceCodes)
                    .Distinct()
                    .ToArray()
            })
            .ToArray();

        return SearchQueryEvaluation.Match(evidence, documentEvaluation.MatchedRouteIds, witnesses);
    }
}
