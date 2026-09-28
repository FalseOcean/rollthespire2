using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

/// <summary>
/// Validates Query against authoritative prediction. It never replays an RNG or reconstructs
/// a Predictor. The document's OverallStatus is used only for request/document
/// viability; Query-relevant section/domain status is checked locally before
/// route-related conditions are evaluated against one OpeningRouteId and a Match
/// witness is emitted.
/// </summary>
public static class ProductionQueryValidator
{
    private static readonly ModelKey SeaGlassKey = new(BaseGameModelKeys.Categories.Relic, "SEA_GLASS");

    public static SearchQueryEvaluation Evaluate(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document,
        RuntimeContextAuthoritySnapshot? evaluatedAuthority = null)
    {
        SearchQueryEvaluation cheap = EvaluateCheap(plan, document, evaluatedAuthority);
        if (cheap.Disposition != SearchDisposition.Match || !plan.Evaluation.RequiresNormalCombatRewardDomain)
        {
            return cheap;
        }
        return EvaluateRewards(plan, document, cheap);
    }

    public static SearchQueryEvaluation EvaluateCheap(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document,
        RuntimeContextAuthoritySnapshot? evaluatedAuthority = null)
    {
        RuntimeContextAuthoritySnapshot expectedAuthority = evaluatedAuthority ?? plan.Authority;
        SearchQueryEvaluation context = ValidateDocumentContextAndViability(plan, document, expectedAuthority);
        if (context.Disposition != SearchDisposition.Match)
        {
            return context;
        }

        bool requiresNeowTruth = plan.Evaluation.HasNeowConstraints || plan.Evaluation.RequiresNormalCombatRewardDomain;
        SearchQueryEvaluation neow = requiresNeowTruth
            ? EvaluateNeowAndOpeningRoutes(plan, document, expectedAuthority)
            : SearchQueryEvaluation.Match();
        if (neow.Disposition != SearchDisposition.Match)
        {
            return neow;
        }

        var evidence = new List<SearchMatchEvidence>(neow.Evidence);
        SearchQueryEvaluation world = EvaluateWorld(plan, document);
        if (world.Disposition != SearchDisposition.Match)
        {
            return world;
        }
        evidence.AddRange(world.Evidence);

        SearchQueryEvaluation relics = EvaluateRelicSequences(plan, document);
        if (relics.Disposition != SearchDisposition.Match)
        {
            return relics;
        }
        evidence.AddRange(relics.Evidence);

        SearchQueryEvaluation events = EvaluateEventSequences(plan, document);
        if (events.Disposition != SearchDisposition.Match)
        {
            return events;
        }
        evidence.AddRange(events.Evidence);

        IReadOnlyList<SearchMatchWitness> witnesses = neow.Witnesses.Count > 0
            ? neow.Witnesses.Select(witness => witness with
            {
                MatchedConditions = witness.MatchedConditions
                    .Concat(world.Evidence.Select(item => item.ConditionId ?? item.Code))
                    .Concat(relics.Evidence.Select(item => item.ConditionId ?? item.Code))
                    .Concat(events.Evidence.Select(item => item.ConditionId ?? item.Code))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                EvidenceCodes = witness.EvidenceCodes
                    .Concat(world.Evidence.Select(item => item.EvidenceCode))
                    .Concat(relics.Evidence.Select(item => item.EvidenceCode))
                    .Concat(events.Evidence.Select(item => item.EvidenceCode))
                    .Where(code => !string.IsNullOrWhiteSpace(code.Value))
                    .Distinct()
                    .ToArray()
            }).ToArray()
            : new[]
            {
                new SearchMatchWitness(
                    document.CanonicalSeed,
                    string.Empty,
                    Array.Empty<ModelKey>(),
                    evidence.Select(item => item.ConditionId ?? item.Code).Distinct(StringComparer.Ordinal).ToArray(),
                    string.Empty,
                    null,
                    evidence.Select(item => item.EvidenceCode)
                        .Where(code => !string.IsNullOrWhiteSpace(code.Value))
                        .Distinct()
                        .ToArray())
            };

        return SearchQueryEvaluation.Match(
            evidence.Distinct().ToArray(),
            witnesses.Select(witness => witness.OpeningRouteId)
                .Where(routeId => !string.IsNullOrWhiteSpace(routeId))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            witnesses);
    }

    public static SearchQueryEvaluation EvaluateRewards(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument rewardDocument,
        SearchQueryEvaluation cheapEvaluation)
    {
        if (!plan.Evaluation.RequiresNormalCombatRewardDomain)
        {
            return cheapEvaluation;
        }
        if (cheapEvaluation.Disposition != SearchDisposition.Match)
        {
            return cheapEvaluation;
        }

        PredictionSection? section = rewardDocument.Sections.FirstOrDefault(item =>
            item.Kind == PredictionSectionKind.NormalCombatRewardSequence);
        NormalCombatRewardSequencePredictionResult? sequence = section?.NormalCombatRewardSequencePrediction;
        if (section is null || sequence is null)
        {
            return SearchQueryEvaluation.Unknown("NormalCombatRewardSectionMissing");
        }
        if (section.DomainStatus == SeedDomainEvaluationStatus.Unsupported ||
            sequence.Status == SeedDomainEvaluationStatus.Unsupported)
        {
            return SearchQueryEvaluation.Unsupported("NormalCombatRewardDomainUnsupported:" + section.IssueCode);
        }
        if (section.DomainStatus != SeedDomainEvaluationStatus.Evaluated ||
            sequence.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            return SearchQueryEvaluation.Unknown("NormalCombatRewardDomainUnknown:" + section.IssueCode);
        }

        if (sequence.NoEligibleSearchRewardRoute)
        {
            return SearchQueryEvaluation.NoMatch(
                plan.CombatRewardRoutePolicy.ExactPolicy == CombatRewardExactRoutePolicy.PinnedRealRoute
                    ? "NoEligiblePinnedRewardRoute"
                    : "NoEligibleUnpinnedRewardRoute");
        }

        var allowed = cheapEvaluation.Witnesses
            .Where(witness => !string.IsNullOrWhiteSpace(witness.OpeningRouteId))
            .GroupBy(witness => witness.OpeningRouteId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        SearchMatchWitness[] globalWitnesses = cheapEvaluation.Witnesses
            .Where(witness => string.IsNullOrWhiteSpace(witness.OpeningRouteId))
            .ToArray();

        var matchedEvidence = new List<SearchMatchEvidence>(cheapEvaluation.Evidence);
        var matchedWitnesses = new List<SearchMatchWitness>();
        bool encounteredUnknown = false;
        foreach (NormalCombatRewardRoutePredictionResult routeGroup in sequence.Routes)
        {
            if (routeGroup.Status == SeedDomainEvaluationStatus.Unsupported)
            {
                continue;
            }
            if (routeGroup.Status != SeedDomainEvaluationStatus.Evaluated ||
                routeGroup.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported)
            {
                encounteredUnknown = true;
                continue;
            }

            OpeningRewardRouteDescriptor[] eligibleDescriptors = routeGroup.EquivalentRoutes
                .Where(descriptor => IsRewardDescriptorEligible(plan, routeGroup, descriptor))
                .Where(descriptor => allowed.Count == 0 || allowed.ContainsKey(descriptor.RouteId) || globalWitnesses.Length > 0)
                .ToArray();
            if (eligibleDescriptors.Length == 0)
            {
                continue;
            }

            RewardRouteEvaluation rewardEvaluation = EvaluateRewardConditions(
                plan.Evaluation,
                routeGroup,
                plan.CompiledSearch.NormalizedQuery.LegacyCombatRewardConstraints);
            if (rewardEvaluation.Disposition == SearchDisposition.Unknown)
            {
                encounteredUnknown = true;
                continue;
            }
            if (rewardEvaluation.Disposition != SearchDisposition.Match)
            {
                continue;
            }

            foreach (OpeningRewardRouteDescriptor descriptor in eligibleDescriptors)
            {
                IReadOnlyList<SearchMatchWitness?> baseWitnesses = allowed.TryGetValue(descriptor.RouteId, out SearchMatchWitness[]? values)
                    ? values.Cast<SearchMatchWitness?>().ToArray()
                    : globalWitnesses.Length > 0
                        ? globalWitnesses.Cast<SearchMatchWitness?>().ToArray()
                        : new SearchMatchWitness?[] { null };
                var routeEvidence = new SearchMatchEvidence(
                    "NormalCombatRewardRouteMatched",
                    descriptor.RootRelicKey,
                    descriptor.RouteId,
                    descriptor.AcquisitionOrder,
                    routeGroup.EvidenceCode,
                    Act: sequence.Act,
                    StreamDomain: sequence.RngStream,
                    Authority: routeGroup.Authority,
                    AuthorityFingerprint: sequence.AuthorityFingerprint,
                    Ordinal: rewardEvaluation.MatchedBattleOrdinal,
                    ConditionId: "normal-combat-reward");
                matchedEvidence.Add(routeEvidence);
                foreach (SearchMatchWitness? baseWitness in baseWitnesses)
                {
                    matchedWitnesses.Add(new SearchMatchWitness(
                        rewardDocument.CanonicalSeed,
                        descriptor.RouteId,
                        descriptor.AcquisitionOrder,
                        (baseWitness?.MatchedConditions ?? Array.Empty<string>())
                            .Concat(rewardEvaluation.MatchedConditionIds)
                            .Append("normal-combat-reward")
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        routeGroup.ContinuationFingerprint,
                        rewardEvaluation.MatchedBattleOrdinal,
                        (baseWitness?.EvidenceCodes ?? Array.Empty<EvidenceCode>())
                            .Concat(rewardEvaluation.EvidenceCodes)
                            .Append(routeGroup.EvidenceCode)
                            .Where(code => !string.IsNullOrWhiteSpace(code.Value))
                            .Distinct()
                            .ToArray())
                    {
                        RewardRouteGroupId = routeGroup.RouteGroupId,
                        ChoicePolicyId = baseWitness?.ChoicePolicyId ?? string.Empty,
                        OutcomeId = baseWitness?.OutcomeId ?? string.Empty
                    });
                }
            }
        }

        if (matchedWitnesses.Count == 0)
        {
            return encounteredUnknown
                ? SearchQueryEvaluation.Unknown("NoReliableNormalCombatRewardRouteMatched")
                : SearchQueryEvaluation.NoMatch("NoNormalCombatRewardRouteMatched");
        }

        return SearchQueryEvaluation.Match(
            matchedEvidence.Distinct().ToArray(),
            matchedWitnesses.Select(item => item.OpeningRouteId).Distinct(StringComparer.Ordinal).ToArray(),
            matchedWitnesses
                .GroupBy(item => string.Join("|", item.OpeningRouteId, item.RewardRouteGroupId, item.MatchedBattleOrdinal, item.ChoicePolicyId, item.OutcomeId), StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray());
    }

    private static bool IsRewardDescriptorEligible(
        ExactSearchExecutionRequest plan,
        NormalCombatRewardRoutePredictionResult routeGroup,
        OpeningRewardRouteDescriptor descriptor)
    {
        switch (plan.CombatRewardRoutePolicy.ExactPolicy)
        {
            case CombatRewardExactRoutePolicy.PinnedRealRoute:
                if (plan.Evaluation.NeowRoute is not { IsValid: true } pinned ||
                    descriptor.RootRelicKey != pinned.RouteRelicKey)
                {
                    return false;
                }
                IReadOnlyList<ModelKey> requiredOrder = plan.Evaluation.RequiredBonesAcquisitionOrder;
                return requiredOrder.Count == 0 || descriptor.AcquisitionOrder.SequenceEqual(requiredOrder);

            case CombatRewardExactRoutePolicy.UnpinnedVerifyNeutralRealRoute:
                return OpeningRewardNeutralityClassifier.Classify(routeGroup.RepresentativeContinuation) ==
                       OpeningRewardNeutrality.ProvablyNeutral;

            default:
                return false;
        }
    }

    /// <summary>
    /// Validates request/document identity and the preserved document-level
    /// viability contract. This deliberately does not aggregate or reject
    /// unrelated section/domain status; each consumed domain owns that check.
    /// </summary>
    private static SearchQueryEvaluation ValidateDocumentContextAndViability(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document,
        RuntimeContextAuthoritySnapshot expectedAuthority)
    {
        if (document.ProfileId != plan.ProfileId ||
            document.Context.Character.CharacterKey != plan.CharacterKey ||
            document.Context.Ascension != plan.Ascension)
        {
            return SearchQueryEvaluation.Unknown("DocumentContextMismatch");
        }
        if (!string.Equals(document.Context.UnlockSnapshotFingerprint, plan.Authority.UnlockSnapshotFingerprint, StringComparison.Ordinal) ||
            !string.Equals(document.Context.CatalogFingerprint, expectedAuthority.CatalogFingerprint, StringComparison.Ordinal))
        {
            return SearchQueryEvaluation.Unknown("DocumentSnapshotFingerprintMismatch");
        }
        if (document.OverallStatus == SeedPredictionOverallStatus.Unsupported)
        {
            return SearchQueryEvaluation.Unsupported("DocumentUnsupported");
        }
        if (document.OverallStatus is SeedPredictionOverallStatus.Unknown or SeedPredictionOverallStatus.InvalidRequest)
        {
            return SearchQueryEvaluation.Unknown("DocumentNotReliable:" + document.OverallStatus);
        }
        return SearchQueryEvaluation.Match();
    }

    private static SearchQueryEvaluation EvaluateNeowAndOpeningRoutes(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document,
        RuntimeContextAuthoritySnapshot evaluatedAuthority)
    {
        NeowChoiceResult[] choices = document.Sections
            .Where(section => section.Kind == PredictionSectionKind.NeowIdentity)
            .SelectMany(section => section.NeowChoices)
            .OrderBy(choice => choice.SlotIndex)
            .ToArray();
        if (choices.Length == 0 || choices.Any(choice => choice.IdentityPrecision != PredictionPrecision.Exact))
        {
            return SearchQueryEvaluation.Unknown("NeowIdentityNotExact");
        }

        ModelKey[] choiceKeys = choices.Select(choice => choice.RelicKey).ToArray();
        if (!QueryKeySetPredicate.MatchesKeySet(choiceKeys, plan.Evaluation.NeowRelics))
        {
            return SearchQueryEvaluation.NoMatch("NeowIdentityFilterRejectedColdPath");
        }

        IReadOnlyList<NeowChoiceResult> routeChoices = choices;
        var globalEvidence = new List<SearchMatchEvidence>();
        if (!plan.Evaluation.NeowRelics.IsEmpty)
        {
            globalEvidence.Add(new SearchMatchEvidence(
                "NeowIdentityFilterMatched",
                EvidenceCode: "r3.search.neow-identity",
                ConditionId: "legacy-neow-identities"));
        }
        if (plan.Evaluation.NeowRoute is { } routeCondition)
        {
            NeowChoiceResult? selected = choices.FirstOrDefault(choice => choice.RelicKey == routeCondition.RouteRelicKey);
            if (selected is null)
            {
                return SearchQueryEvaluation.NoMatch("SelectedNeowRouteRelicMissing");
            }
            routeChoices = new[] { selected };
            globalEvidence.Add(new SearchMatchEvidence(
                "NeowRouteRelicMatched",
                selected.RelicKey,
                EvidenceCode: selected.EvidenceCode,
                ConditionId: "neow-route-relic"));
        }
        else if (globalEvidence.Count == 0)
        {
            globalEvidence.Add(new SearchMatchEvidence(
                "NeowUnconstrained",
                EvidenceCode: "r3.search.neow-unconstrained",
                ConditionId: "neow-unconstrained"));
        }

        bool routeScopeRequiredBeforeRewards = HasRouteScopedNeowConstraints(plan.Evaluation);
        if (!routeScopeRequiredBeforeRewards && !plan.Evaluation.RequiresNormalCombatRewardDomain)
        {
            return SearchQueryEvaluation.Match(globalEvidence);
        }

        var criteria = plan.Evaluation;
        var incompleteBones = RequiresBonesRoute(criteria)
            ? routeChoices.FirstOrDefault(c => c.RelicKey == BaseGameModelKeys.Relics.NeowsBones &&
                c.BonesOutcome is not null && c.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.Unknown)
            : null;
        if (incompleteBones?.BonesOutcome is { } knownBones)
        {
            // Only identity observations can be decided from the retained offer.
            // Do not invent continuation or a successful nested-result witness.
            bool identityOnly = !criteria.RequiresNormalCombatRewardDomain &&
                criteria.StructuredNeowEffects.Count == 0 && criteria.EffectOutputConditions.Count == 0 &&
                criteria.CapsuleContainedRelics.IsEmpty && !criteria.RequireWhetstone && !criteria.RequireWarPaint &&
                !criteria.RequireSmallCapsule && !criteria.RequireLargeCapsule &&
                !criteria.RequiredFinalCurse.HasValue && criteria.BannedFinalCurses.Count == 0 &&
                criteria.Preset == NeowSearchPreset.None;
            if (identityOnly)
            {
                var offered = knownBones.OfferedRelics;
                if (!QueryKeySetPredicate.MatchesKeySet(offered, criteria.BonesRelics) ||
                    criteria.RequiredBonesCombination.Any(k => !offered.Contains(k)))
                    return SearchQueryEvaluation.NoMatch("BonesRelicFilterRejected");
                var order = criteria.RequiredBonesAcquisitionOrder;
                if (order.Count > 0 && (order.Count != 2 || order.Distinct().Count() != 2 || order.Any(k => !offered.Contains(k))))
                    return SearchQueryEvaluation.NoMatch("BonesAcquisitionOrderMismatch");
                globalEvidence.Add(new SearchMatchEvidence("BonesIdentityMatched", incompleteBones.RelicKey,
                    AcquisitionOrder: order, EvidenceCode: incompleteBones.EffectEvidenceCode, ConditionId: "bones-identities"));
                return SearchQueryEvaluation.Match(globalEvidence);
            }
        }

        List<OpeningRouteContext> routeContexts = BuildOpeningRouteContexts(routeChoices);
        if (routeContexts.Count == 0)
        {
            return plan.Evaluation.RequiresNormalCombatRewardDomain || HasRouteScopedNeowConstraints(plan.Evaluation)
                ? SearchQueryEvaluation.Unknown("OpeningRewardRoutesUnavailable")
                : SearchQueryEvaluation.Match(globalEvidence);
        }

        bool requiresBones = RequiresBonesRoute(plan.Evaluation);
        var routeMatches = new List<OpeningRouteEvaluation>();
        bool unknownSeen = incompleteBones is not null;
        bool notEvaluatedSeen = false;
        foreach (OpeningRouteContext route in routeContexts)
        {
            if (requiresBones && route.BonesRoute is null)
            {
                continue;
            }
            OpeningRouteEvaluation evaluation = EvaluateOpeningRoute(
                plan.Evaluation,
                route,
                evaluatedAuthority.EffectAuthority);
            if (evaluation.Disposition == SearchDisposition.Match)
            {
                routeMatches.Add(evaluation);
            }
            else if (evaluation.Disposition == SearchDisposition.Unknown)
            {
                unknownSeen = true;
            }
            else if (evaluation.Disposition == SearchDisposition.NotEvaluatedByPolicy)
            {
                notEvaluatedSeen = true;
            }
        }

        bool routeScopeRequired = HasRouteScopedNeowConstraints(plan.Evaluation) || plan.Evaluation.RequiresNormalCombatRewardDomain;
        if (routeMatches.Count == 0)
        {
            if (!routeScopeRequired)
            {
                return SearchQueryEvaluation.Match(globalEvidence);
            }
            if (notEvaluatedSeen)
            {
                return SearchQueryEvaluation.NotEvaluatedByPolicy("ComplexResultNotEvaluatedByPolicy");
            }
            return unknownSeen
                ? SearchQueryEvaluation.Unknown("NoReliableOpeningRouteMatched")
                : SearchQueryEvaluation.NoMatch("NoOpeningRouteMatched");
        }

        var witnesses = routeMatches.Select(match => new SearchMatchWitness(
            document.CanonicalSeed,
            match.Route.RouteId,
            match.Route.AcquisitionOrder,
            match.Evidence.Select(item => item.ConditionId ?? item.Code).Distinct(StringComparer.Ordinal).ToArray(),
            match.Route.ContinuationFingerprint,
            null,
            match.Evidence.Select(item => item.EvidenceCode)
                .Where(code => !string.IsNullOrWhiteSpace(code.Value))
                .Distinct()
                .ToArray())
        {
            ChoicePolicyId = match.Route.Outcome?.ChoicePolicy.PolicyId ??
                match.Route.BonesRoute?.PlayerChoiceSelections?.LastOrDefault()?.ChoicePolicy.PolicyId ?? string.Empty,
            OutcomeId = match.Route.Outcome?.OutcomeId ?? string.Empty
        }).ToArray();

        return SearchQueryEvaluation.Match(
            globalEvidence.Concat(routeMatches.SelectMany(match => match.Evidence)).Distinct().ToArray(),
            witnesses.Select(item => item.OpeningRouteId).Distinct(StringComparer.Ordinal).ToArray(),
            witnesses);
    }

    private static List<OpeningRouteContext> BuildOpeningRouteContexts(IReadOnlyList<NeowChoiceResult> choices)
    {
        var contexts = new List<OpeningRouteContext>();
        foreach (NeowChoiceResult choice in choices)
        {
            if (choice.RelicKey == BaseGameModelKeys.Relics.NeowsBones && choice.BonesOutcome is { } bones)
            {
                Dictionary<string, DistinctPlayerChoiceOutcome[]> outcomes = (bones.PlayerChoiceImpact?.RouteSets ?? Array.Empty<PlayerChoiceRouteSet>())
                    .SelectMany(set => set.DistinctOutcomes)
                    .GroupBy(outcome => outcome.SourceRouteId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.OrderBy(outcome => outcome.OutcomeId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
                foreach (BonesAcquisitionRouteResult route in bones.OriginalRoutes)
                {
                    OpeningRewardContinuation? continuation = route.OpeningRewardContinuation;
                    string routeId = continuation is null
                        ? $"choice.{choice.SlotIndex}.{route.RouteId}"
                        : $"choice.{choice.SlotIndex}.{continuation.Route.RouteId}";
                    DistinctPlayerChoiceOutcome[] routeOutcomes = outcomes.TryGetValue(route.RouteId, out DistinctPlayerChoiceOutcome[]? values)
                        ? values
                        : Array.Empty<DistinctPlayerChoiceOutcome>();
                    if (routeOutcomes.Length == 0)
                    {
                        contexts.Add(new OpeningRouteContext(
                            routeId,
                            choice,
                            route.AcquisitionOrder,
                            route,
                            null,
                            continuation?.ContinuationFingerprint ?? string.Empty,
                            continuation?.Precision ?? PredictionPrecision.Unknown));
                        continue;
                    }
                    foreach (DistinctPlayerChoiceOutcome outcome in routeOutcomes)
                    {
                        contexts.Add(new OpeningRouteContext(
                            routeId,
                            choice,
                            route.AcquisitionOrder,
                            route,
                            outcome,
                            continuation?.ContinuationFingerprint ?? string.Empty,
                            continuation?.Precision ?? PredictionPrecision.Unknown));
                    }
                }
                continue;
            }

            IReadOnlyList<OpeningRewardContinuation> continuations = choice.OpeningRewardContinuations?.Routes ??
                Array.Empty<OpeningRewardContinuation>();
            if (continuations.Count == 0)
            {
                // Structured Neow filtering depends on the production effect
                // projection, not on the separate opening-reward continuation
                // capability. Keep a direct selected-relic route so exact effect
                // outputs remain searchable even when no reward continuation is
                // published. Reward filters still fail closed later because this
                // route has no continuation fingerprint.
                contexts.Add(new OpeningRouteContext(
                    $"choice.{choice.SlotIndex}.selected",
                    choice,
                    new[] { choice.RelicKey },
                    null,
                    null,
                    string.Empty,
                    PredictionPrecision.Unknown));
                continue;
            }
            foreach (OpeningRewardContinuation continuation in continuations)
            {
                contexts.Add(new OpeningRouteContext(
                    $"choice.{choice.SlotIndex}.{continuation.Route.RouteId}",
                    choice,
                    continuation.Route.AcquisitionOrder,
                    null,
                    null,
                    continuation.ContinuationFingerprint,
                    continuation.Precision));
            }
        }
        return contexts;
    }

    private static OpeningRouteEvaluation EvaluateOpeningRoute(
        ExactSearchEvaluationProjection filter,
        OpeningRouteContext route,
        NeowEffectAuthoritySnapshot? effectAuthority)
    {
        var evidence = new List<SearchMatchEvidence>();
        foreach (var source in filter.StructuredNeowEffects.Select(c => c.SourceRelicKey).Distinct())
            if (!NeowChoiceCommitment.HasCompatibleRequirements(filter.StructuredNeowEffects, source))
                return OpeningRouteEvaluation.NoMatch(route, "OptionalChoiceCommitmentConflict");
        foreach (var trace in route.BonesRoute?.PlayerChoiceSelections ?? Array.Empty<PlayerChoiceSelectionTrace>())
            if (!NeowChoiceCommitment.Matches(filter.StructuredNeowEffects, trace.ChoiceRelicKey,
                    trace.ChoicePolicy, trace.ChoiceEffects))
                return OpeningRouteEvaluation.NoMatch(route, "OptionalChoiceCommitmentMismatch");
        if (route.Outcome is { } outcome && !NeowChoiceCommitment.Matches(filter.StructuredNeowEffects,
                outcome.ChoiceRelicKey, outcome.ChoicePolicy, outcome.ChoiceEffectGroups.SelectMany(g => g.OrderedItems).ToArray()))
            return OpeningRouteEvaluation.NoMatch(route, "OptionalChoiceCommitmentMismatch");
        // OpeningRouteContext.OpeningRewardPrecision belongs only to the normal-combat-
        // reward continuation observation domain. Route-scoped Neow observables (Bones
        // identity/order, Capsule outputs, structured effects, Final Curse, etc.) prove
        // their own authority below and must not be rejected because an unrelated reward
        // continuation is unknown. Reward queries retain the fail-closed guard.
        if (filter.RequiresNormalCombatRewardDomain &&
            route.OpeningRewardPrecision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported)
        {
            return OpeningRouteEvaluation.Unknown(route, "OpeningRewardContinuationPrecisionUnknown");
        }

        if (filter.RequireNeowsBones && route.BonesRoute is null)
        {
            return OpeningRouteEvaluation.NoMatch(route, "NeowsBonesRouteRequired");
        }
        if (!filter.BonesRelics.IsEmpty || filter.RequiredBonesCombination.Count > 0)
        {
            if (route.BonesRoute is null || route.Choice.BonesOutcome is null)
            {
                return OpeningRouteEvaluation.NoMatch(route, "BonesRelicsRequireBonesRoute");
            }
            ModelKey[] offered = route.Choice.BonesOutcome.OfferedRelics.ToArray();
            if (!QueryKeySetPredicate.MatchesKeySet(offered, filter.BonesRelics) ||
                filter.RequiredBonesCombination.Any(key => !offered.Contains(key)))
            {
                return OpeningRouteEvaluation.NoMatch(route, "BonesRelicFilterRejected");
            }
            evidence.Add(new SearchMatchEvidence(
                "BonesRelicsMatched",
                BaseGameModelKeys.Relics.NeowsBones,
                route.RouteId,
                route.AcquisitionOrder,
                route.BonesRoute.RouteEvidenceCode,
                ConditionId: "bones-relics"));
        }
        if (filter.RequiredBonesAcquisitionOrder.Count > 0)
        {
            if (route.BonesRoute is null || !route.AcquisitionOrder.SequenceEqual(filter.RequiredBonesAcquisitionOrder))
            {
                return OpeningRouteEvaluation.NoMatch(route, "BonesAcquisitionOrderMismatch");
            }
            evidence.Add(new SearchMatchEvidence(
                "BonesAcquisitionOrderMatched",
                BaseGameModelKeys.Relics.NeowsBones,
                route.RouteId,
                route.AcquisitionOrder,
                route.BonesRoute.RouteEvidenceCode,
                ConditionId: "bones-order"));
        }

        bool hasSmall = RouteContainsRelic(route, BaseGameModelKeys.Relics.SmallCapsule);
        bool hasLarge = RouteContainsRelic(route, BaseGameModelKeys.Relics.LargeCapsule);
        if (filter.RequireSmallCapsule && !hasSmall)
        {
            return OpeningRouteEvaluation.NoMatch(route, "SmallCapsuleMissingFromRoute");
        }
        if (filter.RequireLargeCapsule && !hasLarge)
        {
            return OpeningRouteEvaluation.NoMatch(route, "LargeCapsuleMissingFromRoute");
        }

        PredictedEffect[] nestedRelicEffects = EnumerateRouteEffects(route)
            .Where(effect => effect.Kind == PredictedEffectKind.AddRelic &&
                             effect.Relation == PredictedEffectRelation.NestedRelic)
            .ToArray();
        ModelKey[] containedRelics = nestedRelicEffects
            .Where(effect => effect.TargetKey.HasValue && effect.Precision == PredictionPrecision.Exact)
            .Select(effect => effect.TargetKey!.Value)
            .ToArray();
        bool unresolvedNestedRelic = nestedRelicEffects.Any(effect =>
            !effect.TargetKey.HasValue || effect.Precision != PredictionPrecision.Exact);
        if (!QueryKeySetPredicate.MatchesKeySet(containedRelics, filter.CapsuleContainedRelics))
        {
            return unresolvedNestedRelic
                ? OpeningRouteEvaluation.Unknown(route, "CapsuleContainedRelicNotExact")
                : OpeningRouteEvaluation.NoMatch(route, "CapsuleContainedRelicFilterRejected");
        }
        if (filter.CapsuleContainedRelics.Ban.Count > 0 && unresolvedNestedRelic)
        {
            return OpeningRouteEvaluation.Unknown(route, "CapsuleContainedRelicBanNotExact");
        }
        if (filter.RequireWhetstone && !containedRelics.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone))
        {
            return unresolvedNestedRelic
                ? OpeningRouteEvaluation.Unknown(route, "WhetstonePresenceNotExact")
                : OpeningRouteEvaluation.NoMatch(route, "WhetstoneMissingFromCapsule");
        }
        if (filter.RequireWarPaint && !containedRelics.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint))
        {
            return unresolvedNestedRelic
                ? OpeningRouteEvaluation.Unknown(route, "WarPaintPresenceNotExact")
                : OpeningRouteEvaluation.NoMatch(route, "WarPaintMissingFromCapsule");
        }
        if (!filter.CapsuleContainedRelics.IsEmpty || filter.RequireWhetstone || filter.RequireWarPaint)
        {
            evidence.Add(new SearchMatchEvidence(
                "CapsuleObjectiveOutputMatched",
                route.Choice.RelicKey,
                route.RouteId,
                route.AcquisitionOrder,
                route.BonesRoute?.RouteEvidenceCode ?? route.Choice.EffectEvidenceCode,
                ConditionId: "capsule-output"));
        }

        foreach ((NeowStructuredEffectSearchCondition condition, int index) in filter.StructuredNeowEffects
                     .Concat(NeowChoiceCommitment.CombinedConditions(filter.StructuredNeowEffects)).Select((item, index) => (item, index)))
        {
            StructuredNeowConditionEvaluation structured = EvaluateStructuredNeowCondition(
                route, condition, effectAuthority);
            if (structured.Disposition == SearchDisposition.NotEvaluatedByPolicy)
            {
                return OpeningRouteEvaluation.NotEvaluatedByPolicy(route, structured.FailureCode);
            }
            if (structured.Disposition == SearchDisposition.Unknown)
            {
                return OpeningRouteEvaluation.Unknown(route, structured.FailureCode);
            }
            if (structured.Disposition != SearchDisposition.Match)
            {
                return OpeningRouteEvaluation.NoMatch(route, structured.FailureCode);
            }
            evidence.Add(new SearchMatchEvidence(
                "NeowStructuredEffectMatched",
                condition.SourceRelicKey,
                route.RouteId,
                route.AcquisitionOrder,
                structured.EvidenceCode,
                ChoicePolicyId: route.Outcome?.ChoicePolicy.PolicyId,
                OutcomeId: route.Outcome?.OutcomeId,
                ConditionId: "neow-structured-effect-" + index));
        }

        foreach ((NeowEffectOutputSearchCondition condition, int index) in filter.EffectOutputConditions.Select((item, index) => (item, index)))
        {
            PredictedEffect[] sourceEffects = EnumerateSourceEffects(route, condition.SourceRelicKey).ToArray();
            if (sourceEffects.Length == 0)
            {
                return OpeningRouteEvaluation.NoMatch(route, "EffectOutputSourceMissing:" + condition.SourceRelicKey.Serialized);
            }
            ModelKey[] exactOutputs = sourceEffects
                .Where(effect => effect.TargetKey.HasValue && effect.Precision == PredictionPrecision.Exact)
                .Select(effect => effect.TargetKey!.Value)
                .ToArray();
            bool unresolved = sourceEffects.Any(effect => effect.Precision != PredictionPrecision.Exact);
            if (!QueryKeySetPredicate.MatchesKeySet(exactOutputs, condition.OutputKeys))
            {
                return unresolved
                    ? OpeningRouteEvaluation.Unknown(route, "EffectOutputNotExact:" + condition.SourceRelicKey.Serialized)
                    : OpeningRouteEvaluation.NoMatch(route, "EffectOutputFilterRejected:" + condition.SourceRelicKey.Serialized);
            }
            if (condition.OutputKeys.Ban.Count > 0 && unresolved)
            {
                return OpeningRouteEvaluation.Unknown(route, "EffectOutputBanNotExact:" + condition.SourceRelicKey.Serialized);
            }
            evidence.Add(new SearchMatchEvidence(
                "NeowEffectOutputMatched",
                condition.SourceRelicKey,
                route.RouteId,
                route.AcquisitionOrder,
                sourceEffects.FirstOrDefault()?.EvidenceCode ?? default,
                ConditionId: "effect-output-" + index));
        }

        if (filter.RequiredFinalCurse.HasValue || filter.BannedFinalCurses.Count > 0)
        {
            if (route.BonesRoute is null)
            {
                return OpeningRouteEvaluation.NoMatch(route, "FinalCurseRequiresBonesRoute");
            }
            ProductRelevantProjectionStatus projectionStatus = route.Outcome?.ProjectionStatus ??
                route.BonesRoute.ProductRelevantProjectionStatus;
            if (projectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy)
            {
                return OpeningRouteEvaluation.NotEvaluatedByPolicy(route, "ComplexResultNotEvaluatedByPolicy");
            }
            PredictionPrecision finalCursePrecision = route.Outcome?.FinalCursePrecision ??
                route.BonesRoute.SharedContinuation.FinalCursePrecision;
            if (finalCursePrecision != PredictionPrecision.Exact)
            {
                return OpeningRouteEvaluation.Unknown(route, "FinalCurseNotExact");
            }
            ModelKey? finalCurse = route.Outcome?.FinalCurseKey ??
                route.BonesRoute.SharedContinuation.FinalCurseKey;
            if (filter.RequiredFinalCurse.HasValue && finalCurse != filter.RequiredFinalCurse)
            {
                return OpeningRouteEvaluation.NoMatch(route, "RequiredFinalCurseMismatch");
            }
            if (finalCurse.HasValue && filter.BannedFinalCurses.Contains(finalCurse.Value))
            {
                return OpeningRouteEvaluation.NoMatch(route, "BannedFinalCurse");
            }
            evidence.Add(new SearchMatchEvidence(
                "FinalCurseMatched",
                finalCurse,
                route.RouteId,
                route.AcquisitionOrder,
                route.BonesRoute.SharedContinuation.EvidenceCode,
                ChoicePolicyId: route.Outcome?.ChoicePolicy.PolicyId,
                OutcomeId: route.Outcome?.OutcomeId,
                ConditionId: "final-curse"));
        }

        if (filter.Preset == NeowSearchPreset.BonesCapsuleAutomaticUpgradeAfterDeckChange)
        {
            if (route.BonesRoute is null)
            {
                return OpeningRouteEvaluation.NoMatch(route, "ValidationPresetRequiresBonesRoute");
            }
            if (route.BonesRoute.ProductRelevantProjectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy)
            {
                return OpeningRouteEvaluation.NotEvaluatedByPolicy(route, "ComplexResultNotEvaluatedByPolicy");
            }
            if (route.Outcome is null || route.Outcome.AutomaticEffectGroups.Count == 0)
            {
                return OpeningRouteEvaluation.NoMatch(route, "DistinctOutcomeAutomaticUpgradeMissing");
            }
            if (route.Outcome.ProjectionStatus == ProductRelevantProjectionStatus.NotEvaluatedByPolicy)
            {
                return OpeningRouteEvaluation.NotEvaluatedByPolicy(route, "ComplexResultNotEvaluatedByPolicy");
            }
            if (route.Outcome.ProductRelevantProjectionPrecision != PredictionPrecision.Exact)
            {
                return OpeningRouteEvaluation.Unknown(route, "ValidationPresetOutcomeNotExact");
            }
            evidence.Add(new SearchMatchEvidence(
                "DistinctPlayerChoiceOutcomeMatched",
                route.Outcome.ChoiceRelicKey,
                route.RouteId,
                route.AcquisitionOrder,
                route.Outcome.EvidenceCodes.FirstOrDefault(),
                route.Outcome.ChoicePolicy.PolicyId,
                route.Outcome.OutcomeId,
                route.Outcome.AutomaticEffectGroups
                    .SelectMany(group => group.OrderedItems)
                    .Where(item => item.TargetKey.HasValue)
                    .Select(item => item.TargetKey!.Value)
                    .Distinct()
                    .ToArray(),
                ConditionId: "complex-route-preset"));
        }

        foreach (ModelKey source in route.AcquisitionOrder.Append(route.Choice.RelicKey).Distinct()
                     .Where(NeowChoiceCommitment.IsOptionalCardOffer))
        {
            ModelKey[] committed = NeowChoiceCommitment.Cards(filter.StructuredNeowEffects, source);
            evidence.Add(new SearchMatchEvidence("OptionalChoiceCommitment", source, route.RouteId, route.AcquisitionOrder,
                ChoicePolicyId: committed.Length == 0 ? "QueryCommitment.Skip" : "QueryCommitment.TakeAuthored.SkipRemainder",
                OrderedOptionKeys: committed, ConditionId: "optional-card-commitment"));
        }
        if (evidence.Count == 0)
        {
            evidence.Add(new SearchMatchEvidence(
                "OpeningRouteAvailable",
                route.Choice.RelicKey,
                route.RouteId,
                route.AcquisitionOrder,
                route.BonesRoute?.RouteEvidenceCode ?? route.Choice.EffectEvidenceCode,
                ConditionId: "opening-route"));
        }
        return OpeningRouteEvaluation.Match(route, evidence);
    }

    private static bool IsBossIdentityUsableForSearch(BossPredictionResult boss) =>
        boss.IdentityPrecision == PredictionPrecision.Exact ||
        boss.SearchIdentityReplayable;

    private static SearchQueryEvaluation EvaluateWorld(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document)
    {
        if (!plan.Evaluation.RequiresWorldDomain)
        {
            return SearchQueryEvaluation.Match();
        }

        PredictionSection? bossSection = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.BossIdentity);
        PredictionSection? ancientSection = document.Sections.FirstOrDefault(section => section.Kind == PredictionSectionKind.AncientIdentityAndOptions);
        var evidence = new List<SearchMatchEvidence>();

        foreach (ActModelKeySetFilter filter in plan.Evaluation.BossFilters)
        {
            SearchQueryEvaluation status = ValidateSection(bossSection, "Boss");
            if (status.Disposition != SearchDisposition.Match) return status;
            BossPredictionResult[] bosses = bossSection!.Bosses.Where(item => item.Act == filter.Act).ToArray();
            if (bosses.Length == 0 || bosses.Any(item => !IsBossIdentityUsableForSearch(item)))
                return SearchQueryEvaluation.Unknown("BossIdentityNotReplayable:Act" + filter.Act);
            if (!QueryKeySetPredicate.MatchesKeySet(bosses.Select(item => item.BossKey), filter.Keys))
                return SearchQueryEvaluation.NoMatch("BossFilterRejected:Act" + filter.Act);
            evidence.AddRange(bosses.Select(boss => new SearchMatchEvidence(
                boss.IdentityPrecision == PredictionPrecision.Exact
                    ? "BossIdentityMatched"
                    : "BossIdentityMatchedCompatible",
                boss.BossKey, $"boss:act{boss.Act}:ordinal{boss.Ordinal}",
                EvidenceCode: boss.EvidenceCode, ProfileId: document.ProfileId, Act: boss.Act,
                StreamDomain: boss.RngStream, Authority: boss.Authority,
                AuthorityFingerprint: boss.AuthorityFingerprint, Ordinal: boss.Ordinal,
                ConditionId: $"boss-act-{filter.Act}")));
        }

        foreach (ActOrdinalModelKeySetFilter filter in plan.Evaluation.BossOrdinalFilters)
        {
            SearchQueryEvaluation status = ValidateSection(bossSection, "Boss");
            if (status.Disposition != SearchDisposition.Match) return status;
            BossPredictionResult? boss = bossSection!.Bosses.FirstOrDefault(item => item.Act == filter.Act && item.Ordinal == filter.Ordinal);
            if (boss is null || !IsBossIdentityUsableForSearch(boss))
                return SearchQueryEvaluation.Unknown($"BossOrdinalNotReplayable:Act{filter.Act}:Ordinal{filter.Ordinal}");
            if (!QueryKeySetPredicate.MatchesKeySet(new[] { boss.BossKey }, filter.Keys))
                return SearchQueryEvaluation.NoMatch($"BossOrdinalFilterRejected:Act{filter.Act}:Ordinal{filter.Ordinal}");
            evidence.Add(new SearchMatchEvidence(
                boss.IdentityPrecision == PredictionPrecision.Exact
                    ? "BossOrdinalIdentityMatched"
                    : "BossOrdinalIdentityMatchedCompatible",
                boss.BossKey, $"boss:act{boss.Act}:ordinal{boss.Ordinal}",
                EvidenceCode: boss.EvidenceCode, ProfileId: document.ProfileId, Act: boss.Act,
                StreamDomain: boss.RngStream, Authority: boss.Authority,
                AuthorityFingerprint: boss.AuthorityFingerprint, Ordinal: boss.Ordinal,
                ConditionId: $"boss-act-{filter.Act}-ordinal-{filter.Ordinal}"));
        }

        foreach (IGrouping<int, AncientSearchBranchCondition> actBranches in
                 plan.Evaluation.AncientBranchConditions.GroupBy(condition => condition.Act).OrderBy(group => group.Key))
        {
            SearchQueryEvaluation status = ValidateSection(ancientSection, "Ancient");
            if (status.Disposition != SearchDisposition.Match) return status;

            AncientPredictionResult? ancient = ancientSection!.Ancients.FirstOrDefault(item => item.Act == actBranches.Key);
            if (ancient is null || ancient.IdentityPrecision != PredictionPrecision.Exact)
            {
                return SearchQueryEvaluation.Unknown("AncientBranchIdentityNotExact:Act" + actBranches.Key);
            }

            var rows = actBranches.Where(item => item.AncientKey == ancient.AncientKey).ToArray();
            // Rows are alternatives even when they bind the same Ancient. Keep
            // each row's option/SeaGlass conjunction intact, and preserve Unknown
            // when no row matches but at least one cannot yet be evaluated.
            SearchDisposition RowDisposition(AncientSearchBranchCondition row)
            {
                if (row.OptionAny.Count > 0)
                {
                    if (!AncientOptionsEvaluated(ancient.OptionsEvaluationStatus) ||
                        ancient.Options.Any(o => o.OptionPrecision != PredictionPrecision.Exact)) return SearchDisposition.Unknown;
                    if (!ancient.Options.Any(o => o.IsVisible && row.OptionAny.Contains(o.OptionKey, ModelKeyComparer.Instance)))
                        return SearchDisposition.NoMatch;
                }
                if (row.SeaGlassTargetAny.Count > 0)
                {
                    if (!AncientOptionsEvaluated(ancient.OptionsEvaluationStatus)) return SearchDisposition.Unknown;
                    var sea = ancient.Options.FirstOrDefault(o => o.IsVisible && o.OptionKey == SeaGlassKey);
                    if (sea is null) return SearchDisposition.NoMatch;
                    if (sea.CharacterTarget is not { Precision: PredictionPrecision.Exact, CharacterKey: { } target })
                        return SearchDisposition.Unknown;
                    if (!row.SeaGlassTargetAny.Contains(target, ModelKeyComparer.Instance)) return SearchDisposition.NoMatch;
                }
                return SearchDisposition.Match;
            }
            AncientSearchBranchCondition? branch = rows.FirstOrDefault(row => RowDisposition(row) == SearchDisposition.Match) ??
                rows.FirstOrDefault(row => RowDisposition(row) == SearchDisposition.Unknown) ?? rows.FirstOrDefault();
            if (branch is null)
            {
                return SearchQueryEvaluation.NoMatch("AncientBranchIdentityRejected:Act" + actBranches.Key);
            }

            string conditionId = $"ancient-act-{branch.Act}-row-{branch.AncientKey.Entry}";
            evidence.Add(new SearchMatchEvidence(
                "AncientBranchIdentityMatched", ancient.AncientKey, $"ancient:act{ancient.Act}",
                EvidenceCode: ancient.IdentityEvidenceCode, ProfileId: document.ProfileId, Act: ancient.Act,
                StreamDomain: ancient.IdentityRngStream, Authority: ancient.Authority,
                AuthorityFingerprint: ancient.AuthorityFingerprint,
                ConditionId: conditionId));

            if (branch.OptionAny.Count > 0)
            {
                if (!AncientOptionsEvaluated(ancient.OptionsEvaluationStatus) ||
                    ancient.Options.Any(option => option.OptionPrecision != PredictionPrecision.Exact))
                {
                    return SearchQueryEvaluation.Unknown("AncientBranchOptionsNotExact:Act" + branch.Act);
                }

                AncientOptionPredictionResult[] visibleOptions = ancient.Options
                    .Where(option => option.IsVisible)
                    .ToArray();
                AncientOptionPredictionResult? matchedOption = visibleOptions.FirstOrDefault(option =>
                    branch.OptionAny.Contains(option.OptionKey, ModelKeyComparer.Instance));
                if (matchedOption is null)
                {
                    return SearchQueryEvaluation.NoMatch("AncientBranchOptionAnyRejected:Act" + branch.Act);
                }

                ModelKey[] orderedOptions = visibleOptions.Select(option => option.OptionKey).ToArray();
                evidence.Add(new SearchMatchEvidence(
                    "AncientBranchOptionAnyMatched", matchedOption.OptionKey, $"ancient-options:act{ancient.Act}",
                    EvidenceCode: matchedOption.EvidenceCode, ProfileId: document.ProfileId, Act: ancient.Act,
                    StreamDomain: ancient.OptionRngStream, Authority: ancient.Authority,
                    AuthorityFingerprint: ancient.AuthorityFingerprint, OrderedOptionKeys: orderedOptions,
                    Ordinal: matchedOption.Ordinal,
                    ConditionId: conditionId + "-options"));
            }

            if (branch.SeaGlassTargetAny.Count > 0)
            {
                if (!AncientOptionsEvaluated(ancient.OptionsEvaluationStatus))
                {
                    return SearchQueryEvaluation.Unknown("AncientBranchSeaGlassOptionsNotEvaluated:Act" + branch.Act);
                }
                AncientOptionPredictionResult? seaGlass = ancient.Options.FirstOrDefault(option =>
                    option.IsVisible && option.OptionKey == SeaGlassKey);
                if (seaGlass is null)
                {
                    return SearchQueryEvaluation.NoMatch("AncientBranchSeaGlassOptionMissing:Act" + branch.Act);
                }
                AncientOptionCharacterTargetProjection? target = seaGlass.CharacterTarget;
                if (target is null || target.Precision != PredictionPrecision.Exact || !target.CharacterKey.HasValue)
                {
                    return SearchQueryEvaluation.Unknown("AncientBranchSeaGlassTargetNotExact:Act" + branch.Act);
                }
                if (!branch.SeaGlassTargetAny.Contains(target.CharacterKey.Value, ModelKeyComparer.Instance))
                {
                    return SearchQueryEvaluation.NoMatch("AncientBranchSeaGlassTargetRejected:Act" + branch.Act);
                }
                evidence.Add(new SearchMatchEvidence(
                    "AncientBranchSeaGlassTargetMatched", target.CharacterKey, $"ancient-seaglass:act{branch.Act}",
                    EvidenceCode: target.EvidenceCode, Act: branch.Act,
                    ConditionId: conditionId + "-sea-glass-target"));
            }
        }

        foreach (ActModelKeySetFilter filter in plan.Evaluation.AncientIdentityFilters)
        {
            SearchQueryEvaluation status = ValidateSection(ancientSection, "Ancient");
            if (status.Disposition != SearchDisposition.Match) return status;
            AncientPredictionResult? ancient = ancientSection!.Ancients.FirstOrDefault(item => item.Act == filter.Act);
            if (ancient is null || ancient.IdentityPrecision != PredictionPrecision.Exact)
                return SearchQueryEvaluation.Unknown("AncientIdentityNotExact:Act" + filter.Act);
            if (!QueryKeySetPredicate.MatchesKeySet(new[] { ancient.AncientKey }, filter.Keys))
                return SearchQueryEvaluation.NoMatch("AncientIdentityFilterRejected:Act" + filter.Act);
            evidence.Add(new SearchMatchEvidence(
                "AncientIdentityMatched", ancient.AncientKey, $"ancient:act{ancient.Act}",
                EvidenceCode: ancient.IdentityEvidenceCode, ProfileId: document.ProfileId, Act: ancient.Act,
                StreamDomain: ancient.IdentityRngStream, Authority: ancient.Authority,
                AuthorityFingerprint: ancient.AuthorityFingerprint,
                ConditionId: $"ancient-act-{filter.Act}"));
        }

        foreach (ActModelKeySetFilter filter in plan.Evaluation.AncientOptionFilters)
        {
            if (filter.Act == 1)
            {
                var neow = document.Sections.Where(s => s.Kind == PredictionSectionKind.NeowIdentity)
                    .SelectMany(s => s.NeowChoices).ToArray();
                if (neow.Length == 0 || neow.Any(c => c.IdentityPrecision != PredictionPrecision.Exact))
                    return SearchQueryEvaluation.Unknown("Act1NeowOptionsNotExact");
                var offered = neow.Select(c => c.RelicKey).ToArray();
                if (!QueryKeySetPredicate.MatchesKeySet(offered, filter.Keys))
                    return SearchQueryEvaluation.NoMatch("Act1NeowOptionFilterRejected");
                evidence.Add(new SearchMatchEvidence("Act1NeowOptionsMatched", Act: 1,
                    OrderedOptionKeys: offered, ConditionId: "ancient-options-act-1"));
                continue;
            }
            SearchQueryEvaluation status = ValidateSection(ancientSection, "Ancient");
            if (status.Disposition != SearchDisposition.Match) return status;
            AncientPredictionResult? ancient = ancientSection!.Ancients.FirstOrDefault(item => item.Act == filter.Act);
            if (ancient is null || !AncientOptionsEvaluated(ancient.OptionsEvaluationStatus) ||
                ancient.Options.Any(option => option.OptionPrecision != PredictionPrecision.Exact))
                return SearchQueryEvaluation.Unknown("AncientOptionsNotExact:Act" + filter.Act);
            ModelKey[] options = ancient.Options.Where(option => option.IsVisible).Select(option => option.OptionKey).ToArray();
            if (!QueryKeySetPredicate.MatchesKeySet(options, filter.Keys))
                return SearchQueryEvaluation.NoMatch("AncientOptionFilterRejected:Act" + filter.Act);
            evidence.Add(new SearchMatchEvidence(
                "AncientOptionsMatched", ancient.AncientKey, $"ancient-options:act{ancient.Act}",
                EvidenceCode: ancient.OptionEvidenceCode, ProfileId: document.ProfileId, Act: ancient.Act,
                StreamDomain: ancient.OptionRngStream, Authority: ancient.Authority,
                AuthorityFingerprint: ancient.AuthorityFingerprint, OrderedOptionKeys: options,
                ConditionId: $"ancient-options-act-{filter.Act}"));
        }

        foreach (ActModelKeySetFilter filter in plan.Evaluation.AncientSeaGlassTargetFilters)
        {
            if (filter.Act == 1)
            {
                // Act 1 belongs to Neow, whose offer catalog cannot contain Sea
                // Glass. A target-only refinement is vacuous when it is absent.
                evidence.Add(new SearchMatchEvidence("SeaGlassTargetNotApplicable", Act: 1,
                    ConditionId: "sea-glass-target-act-1"));
                continue;
            }
            SearchQueryEvaluation status = ValidateSection(ancientSection, "Ancient");
            if (status.Disposition != SearchDisposition.Match) return status;
            AncientPredictionResult? ancient = ancientSection!.Ancients.FirstOrDefault(item => item.Act == filter.Act);
            if (ancient is null || !AncientOptionsEvaluated(ancient.OptionsEvaluationStatus))
                return SearchQueryEvaluation.Unknown("AncientOptionsNotEvaluatedForSeaGlass:Act" + filter.Act);
            AncientOptionPredictionResult? seaGlass = ancient.Options.FirstOrDefault(option => option.IsVisible && option.OptionKey == SeaGlassKey);
            if (seaGlass is null)
            {
                // Target is a refinement only. Requiring SeaGlass itself is an
                // independent AncientOption filter.
                evidence.Add(new SearchMatchEvidence(
                    "SeaGlassTargetNotApplicable", ancient.AncientKey, $"ancient-seaglass:act{filter.Act}",
                    EvidenceCode: ancient.OptionEvidenceCode, Act: filter.Act,
                    ConditionId: $"sea-glass-target-act-{filter.Act}"));
                continue;
            }
            AncientOptionCharacterTargetProjection? target = seaGlass.CharacterTarget;
            if (target is null || target.Precision != PredictionPrecision.Exact || !target.CharacterKey.HasValue)
                return SearchQueryEvaluation.Unknown("SeaGlassTargetNotExact:Act" + filter.Act);
            if (!QueryKeySetPredicate.MatchesKeySet(new[] { target.CharacterKey.Value }, filter.Keys))
                return SearchQueryEvaluation.NoMatch("SeaGlassTargetFilterRejected:Act" + filter.Act);
            evidence.Add(new SearchMatchEvidence(
                "SeaGlassTargetMatched", target.CharacterKey, $"ancient-seaglass:act{filter.Act}",
                EvidenceCode: target.EvidenceCode, Act: filter.Act,
                ConditionId: $"sea-glass-target-act-{filter.Act}"));
        }

        return SearchQueryEvaluation.Match(evidence);
    }

    private static SearchQueryEvaluation EvaluateRelicSequences(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document)
    {
        if (!plan.Evaluation.RequiresRelicSequenceDomain)
        {
            return SearchQueryEvaluation.Match();
        }
        PredictionSection? section = document.Sections.FirstOrDefault(item => item.Kind == PredictionSectionKind.RelicSequences);
        SearchQueryEvaluation status = ValidateSection(section, "RelicSequence");
        if (status.Disposition != SearchDisposition.Match) return status;
        RelicSequencePredictionResult? prediction = section!.RelicSequencePrediction;
        if (prediction is null || prediction.Status != SeedDomainEvaluationStatus.Evaluated)
            return SearchQueryEvaluation.Unknown("RelicSequencePredictionMissingOrUnknown");

        var evidence = new List<SearchMatchEvidence>();
        foreach ((RelicSequenceSearchCondition condition, int index) in plan.Evaluation.RelicSequenceConditions.Select((item, index) => (item, index)))
        {
            RelicSequenceLaneResult? lane = prediction.Lanes.FirstOrDefault(item => item.Kind == condition.Lane);
            if (lane is null || lane.Precision != PredictionPrecision.Exact)
                return SearchQueryEvaluation.Unknown("RelicSequenceLaneNotExact:" + condition.Lane);
            RelicSequenceEntryResult[] range = condition.RangeMode == SearchSequenceRangeMode.FirstN
                ? lane.Entries.Where(item => item.Position <= condition.RangeValue).ToArray()
                : lane.Entries.Where(item => item.Position == condition.RangeValue).ToArray();
            if (condition.RangeMode == SearchSequenceRangeMode.ExactSlot && range.Length != 1)
                return SearchQueryEvaluation.Unknown($"RelicSequenceSlotUnavailable:{condition.Lane}:{condition.RangeValue}");
            if (range.Any(item => item.Precision != PredictionPrecision.Exact))
                return SearchQueryEvaluation.Unknown($"RelicSequenceRangeNotExact:{condition.Lane}:{condition.RangeValue}");
            if (!QueryKeySetPredicate.MatchesKeySet(range.Select(item => item.RelicKey), condition.Keys))
                return SearchQueryEvaluation.NoMatch($"RelicSequenceConditionRejected:{condition.Lane}:{index}");
            evidence.Add(new SearchMatchEvidence(
                "RelicSequenceConditionMatched",
                range.FirstOrDefault()?.RelicKey,
                $"relic-sequence:{condition.Lane}",
                EvidenceCode: lane.EvidenceCode,
                Ordinal: condition.RangeMode == SearchSequenceRangeMode.ExactSlot ? condition.RangeValue : null,
                ConditionId: "relic-sequence-" + index));
        }
        int shopSequenceIndex = 0;
        RelicSequenceLaneResult? shopLane = prediction.Lanes.FirstOrDefault(item => item.Kind == RelicSequenceKind.Shop);
        foreach (RelicShopSequenceSearchCondition condition in plan.Evaluation.RelicShopSequenceConditions)
        {
            if (shopLane is null || shopLane.Precision != PredictionPrecision.Exact)
                return SearchQueryEvaluation.Unknown("RelicShopSequenceLaneNotExact");
            RelicSequenceEntryResult[] range = shopLane.Entries
                .Where(item => item.Position <= condition.Count)
                .OrderBy(item => item.Position)
                .ToArray();
            if (range.Length < condition.Count || range.Any(item => item.Precision != PredictionPrecision.Exact))
                return SearchQueryEvaluation.Unknown($"RelicShopSequenceRangeNotExact:{condition.Count}");
            if (!MatchesShopSequence(condition, range.Select(item => item.RelicKey).ToArray()))
                return SearchQueryEvaluation.NoMatch($"RelicShopSequenceRejected:{shopSequenceIndex}");
            evidence.Add(new SearchMatchEvidence(
                "RelicShopSequenceMatched",
                null,
                "relic-sequence:shop",
                EvidenceCode: shopLane.EvidenceCode,
                ConditionId: "relic-shop-sequence-" + shopSequenceIndex));
            shopSequenceIndex++;
        }
        return SearchQueryEvaluation.Match(evidence);
    }

    private static bool MatchesShopSequence(RelicShopSequenceSearchCondition condition, IReadOnlyList<ModelKey> actual) =>
        ShopSequenceSemantics.Matches(actual, condition.Count, condition.OrderMode, condition.Slots);

    private static SearchQueryEvaluation EvaluateEventSequences(
        ExactSearchExecutionRequest plan,
        SeedPredictionDocument document)
    {
        if (!plan.Evaluation.RequiresEventSequenceDomain)
        {
            return SearchQueryEvaluation.Match();
        }
        PredictionSection? section = document.Sections.FirstOrDefault(item => item.Kind == PredictionSectionKind.EventPoolSequences);
        SearchQueryEvaluation status = ValidateSection(section, "EventSequence");
        if (status.Disposition != SearchDisposition.Match) return status;
        EventPoolSequencePredictionResult? prediction = section!.EventPoolSequencePrediction;
        if (prediction is null || prediction.Status != SeedDomainEvaluationStatus.Evaluated)
            return SearchQueryEvaluation.Unknown("EventSequencePredictionMissingOrUnknown");
        if (prediction.RawEventOrderPrecision != PredictionPrecision.Exact ||
            prediction.StaticActCleaningPrecision != PredictionPrecision.Exact ||
            prediction.StaticCandidateOrdinalPrecision != PredictionPrecision.Exact)
        {
            return SearchQueryEvaluation.Unknown("EventStaticCandidateOrdinalNotExact");
        }

        var evidence = new List<SearchMatchEvidence>();
        foreach ((EventSequenceSearchCondition condition, int index) in plan.Evaluation.EventSequenceConditions.Select((item, index) => (item, index)))
        {
            EventPoolActSequenceResult? act = prediction.Acts.FirstOrDefault(item => item.Act == condition.Act);
            if (act is null || act.Precision != PredictionPrecision.Exact)
                return SearchQueryEvaluation.Unknown("EventSequenceActNotExact:Act" + condition.Act);
            // Always use the production effective queue. RawEntries is diagnostic only.
            IEnumerable<EventPoolSequenceEntryResult> absoluteRange = condition.RangeMode == SearchSequenceRangeMode.FirstN
                ? act.Entries.Where(item => item.Ordinal <= condition.RangeValue)
                : act.Entries.Where(item => item.Ordinal == condition.RangeValue);
            EventPoolSequenceEntryResult[] range = absoluteRange
                .Where(item => !condition.Source.HasValue || item.Source == condition.Source.Value)
                .ToArray();
            if (condition.RangeMode == SearchSequenceRangeMode.ExactSlot && range.Length == 0)
            {
                // A source mismatch is a definite NoMatch, while a missing absolute
                // slot means the effective queue is too short and is also definite.
                return SearchQueryEvaluation.NoMatch($"EventSequenceSlotRejected:Act{condition.Act}:{condition.RangeValue}");
            }
            if (range.Any(item => item.Precision != PredictionPrecision.Exact))
                return SearchQueryEvaluation.Unknown($"EventSequenceRangeNotExact:Act{condition.Act}:{condition.RangeValue}");
            if (!QueryKeySetPredicate.MatchesKeySet(range.Select(item => item.EventKey), condition.Keys))
                return SearchQueryEvaluation.NoMatch($"EventSequenceConditionRejected:Act{condition.Act}:{index}");
            evidence.Add(new SearchMatchEvidence(
                "EffectiveEventSequenceConditionMatched",
                range.FirstOrDefault()?.EventKey,
                $"event-effective:act{condition.Act}",
                EvidenceCode: act.EvidenceCode,
                Act: condition.Act,
                StreamDomain: act.RngStream,
                Ordinal: condition.RangeMode == SearchSequenceRangeMode.ExactSlot ? condition.RangeValue : null,
                ConditionId: "event-sequence-" + index));
        }
        return SearchQueryEvaluation.Match(evidence);
    }

    private static RewardRouteEvaluation EvaluateRewardConditions(
        ExactSearchEvaluationProjection filter,
        NormalCombatRewardRoutePredictionResult route,
        IReadOnlyList<NormalCombatRewardSearchCondition> legacyConditions)
    {
        var matchedIds = new List<string>();
        var evidenceCodes = new List<EvidenceCode>();
        int? representativeBattle = null;

        RewardRouteEvaluation legacy = EvaluateLegacyRewardConditions(legacyConditions, route);
        if (legacy.Disposition != SearchDisposition.Match)
            return legacy;
        matchedIds.AddRange(legacy.MatchedConditionIds);
        evidenceCodes.AddRange(legacy.EvidenceCodes);
        representativeBattle ??= legacy.MatchedBattleOrdinal;

        if (filter.CombatCardRewardSequence is { IsEmpty: false } cards)
        {
            RewardRouteEvaluation cardResult = EvaluateCardRewardSequence(cards, route);
            if (cardResult.Disposition != SearchDisposition.Match)
                return cardResult;
            matchedIds.AddRange(cardResult.MatchedConditionIds);
            evidenceCodes.AddRange(cardResult.EvidenceCodes);
            representativeBattle ??= cardResult.MatchedBattleOrdinal;
        }

        if (filter.CombatPotionRewardSequence is { IsEmpty: false } potions)
        {
            RewardRouteEvaluation potionResult = EvaluatePotionRewardSequence(potions, route);
            if (potionResult.Disposition != SearchDisposition.Match)
                return potionResult;
            matchedIds.AddRange(potionResult.MatchedConditionIds);
            evidenceCodes.AddRange(potionResult.EvidenceCodes);
            representativeBattle ??= potionResult.MatchedBattleOrdinal;
        }

        return RewardRouteEvaluation.Match(
            matchedIds.Distinct(StringComparer.Ordinal).ToArray(),
            representativeBattle,
            evidenceCodes.Where(code => !string.IsNullOrWhiteSpace(code.Value)).Distinct().ToArray());
    }

    private static RewardRouteEvaluation EvaluateLegacyRewardConditions(
        IReadOnlyList<NormalCombatRewardSearchCondition> conditions,
        NormalCombatRewardRoutePredictionResult route)
    {
        if (conditions.Count == 0)
            return RewardRouteEvaluation.Match(Array.Empty<string>(), null, Array.Empty<EvidenceCode>());

        var matchedIds = new List<string>();
        var evidenceCodes = new List<EvidenceCode>();
        int? representativeBattle = null;
        foreach ((NormalCombatRewardSearchCondition condition, int index) in conditions.Select((item, index) => (item, index)))
        {
            NormalCombatRewardBattleResult[] battles = condition.BattleOrdinal == 0
                ? route.Battles.Where(item => item.BattleOrdinal is >= 1 and <= 3).ToArray()
                : route.Battles.Where(item => item.BattleOrdinal == condition.BattleOrdinal).ToArray();
            if (battles.Length == 0)
                return RewardRouteEvaluation.NoMatch("RewardBattleMissing:" + condition.BattleOrdinal);

            NormalCombatRewardBattleResult? matched = null;
            bool unknownSeen = false;
            foreach (NormalCombatRewardBattleResult battle in battles)
            {
                BattleConditionEvaluation battleEvaluation = EvaluateBattleCondition(condition, battle);
                if (battleEvaluation.Disposition == SearchDisposition.Match)
                {
                    matched = battle;
                    break;
                }
                if (battleEvaluation.Disposition == SearchDisposition.Unknown)
                    unknownSeen = true;
            }
            if (matched is null)
            {
                return unknownSeen
                    ? RewardRouteEvaluation.Unknown("RewardConditionNotReliablyEvaluated:" + index)
                    : RewardRouteEvaluation.NoMatch("RewardConditionRejected:" + index);
            }
            representativeBattle ??= matched.BattleOrdinal;
            matchedIds.Add("normal-combat-reward-condition-" + index);
            evidenceCodes.Add(matched.EvidenceCode);
        }
        return RewardRouteEvaluation.Match(matchedIds, representativeBattle, evidenceCodes);
    }

    private static RewardRouteEvaluation EvaluateCardRewardSequence(
        CombatCardRewardSequenceSearchCondition sequence,
        NormalCombatRewardRoutePredictionResult route)
    {
        NormalCombatRewardBattleResult[] battles = FirstBattles(route, sequence.Count);
        if (battles.Length != sequence.Count)
            return RewardRouteEvaluation.NoMatch("CombatCardRewardSequenceBattleMissing");

        BattleConditionEvaluation Evaluate(ModelKey target, NormalCombatRewardBattleResult battle) =>
            EvaluateBattleCondition(
                new NormalCombatRewardSearchCondition(
                    battle.BattleOrdinal,
                    new ModelKeySetFilter(new[] { target }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>()),
                    NormalCombatPotionRequirement.Any,
                    ModelKeySetFilter.Empty,
                    null,
                    null),
                battle);

        if (sequence.OrderMode == CombatRewardSequenceOrderMode.Ordered)
        {
            var evidence = new List<EvidenceCode>();
            for (int slot = 0; slot < sequence.Count; slot++)
            {
                ModelKey? target = sequence.Slots[slot];
                if (!target.HasValue) continue;
                BattleConditionEvaluation result = Evaluate(target.Value, battles[slot]);
                if (result.Disposition == SearchDisposition.NoMatch)
                    return RewardRouteEvaluation.NoMatch("CombatCardRewardSequenceRejected");
                if (result.Disposition == SearchDisposition.Unknown)
                    return RewardRouteEvaluation.Unknown("CombatCardRewardSequenceNotReliablyEvaluated");
                evidence.Add(battles[slot].EvidenceCode);
            }
            return RewardRouteEvaluation.Match(
                Enumerable.Range(0, sequence.Count)
                    .Where(index => sequence.Slots[index].HasValue)
                    .Select(index => "combat-card-reward-slot-" + (index + 1))
                    .ToArray(),
                battles.FirstOrDefault()?.BattleOrdinal,
                evidence);
        }

        ModelKey[] constraints = sequence.Slots.Where(key => key.HasValue).Select(key => key!.Value).ToArray();
        if (!TryFindUnorderedAssignment(
                constraints.Length,
                battles.Length,
                (constraint, battle) => Evaluate(constraints[constraint], battles[battle]),
                out int[]? assignment,
                out bool unknownPossible) || assignment is null)
        {
            return unknownPossible
                ? RewardRouteEvaluation.Unknown("CombatCardRewardSequenceNotReliablyEvaluated")
                : RewardRouteEvaluation.NoMatch("CombatCardRewardSequenceRejected");
        }
        return RewardRouteEvaluation.Match(
            Enumerable.Range(0, constraints.Length).Select(index => "combat-card-reward-target-" + (index + 1)).ToArray(),
            constraints.Length > 0 ? battles[assignment[0]].BattleOrdinal : battles[0].BattleOrdinal,
            assignment.Select(index => battles[index].EvidenceCode).ToArray());
    }

    private static RewardRouteEvaluation EvaluatePotionRewardSequence(
        CombatPotionRewardSequenceSearchCondition sequence,
        NormalCombatRewardRoutePredictionResult route)
    {
        NormalCombatRewardBattleResult[] battles = FirstBattles(route, sequence.Count);
        if (battles.Length != sequence.Count)
            return RewardRouteEvaluation.NoMatch("CombatPotionRewardSequenceBattleMissing");

        BattleConditionEvaluation Evaluate(CombatPotionRewardSlotSearchCondition slot, NormalCombatRewardBattleResult battle)
        {
            if (slot.IsNeutral)
                return BattleConditionEvaluation.Match();
            NormalCombatPotionRequirement requirement = slot.Requirement == CombatPotionSlotRequirement.NoDrop
                ? NormalCombatPotionRequirement.MustNotDrop
                : NormalCombatPotionRequirement.MustDrop;
            ModelKeySetFilter potionKeys = slot.Requirement == CombatPotionSlotRequirement.DropSpecific && slot.PotionKey.HasValue
                ? new ModelKeySetFilter(new[] { slot.PotionKey.Value }, Array.Empty<ModelKey>(), Array.Empty<ModelKey>())
                : ModelKeySetFilter.Empty;
            return EvaluateBattleCondition(
                new NormalCombatRewardSearchCondition(
                    battle.BattleOrdinal,
                    ModelKeySetFilter.Empty,
                    requirement,
                    potionKeys,
                    null,
                    null),
                battle);
        }

        if (sequence.OrderMode == CombatRewardSequenceOrderMode.Ordered)
        {
            var evidence = new List<EvidenceCode>();
            for (int slotIndex = 0; slotIndex < sequence.Count; slotIndex++)
            {
                CombatPotionRewardSlotSearchCondition slot = sequence.Slots[slotIndex];
                if (slot.IsNeutral) continue;
                BattleConditionEvaluation result = Evaluate(slot, battles[slotIndex]);
                if (result.Disposition == SearchDisposition.NoMatch)
                    return RewardRouteEvaluation.NoMatch("CombatPotionRewardSequenceRejected");
                if (result.Disposition == SearchDisposition.Unknown)
                    return RewardRouteEvaluation.Unknown("CombatPotionRewardSequenceNotReliablyEvaluated");
                evidence.Add(battles[slotIndex].EvidenceCode);
            }
            return RewardRouteEvaluation.Match(
                Enumerable.Range(0, sequence.Count)
                    .Where(index => !sequence.Slots[index].IsNeutral)
                    .Select(index => "combat-potion-reward-slot-" + (index + 1))
                    .ToArray(),
                battles.FirstOrDefault()?.BattleOrdinal,
                evidence);
        }

        CombatPotionRewardSlotSearchCondition[] constraints = sequence.Slots.Where(slot => !slot.IsNeutral).ToArray();
        if (!TryFindUnorderedAssignment(
                constraints.Length,
                battles.Length,
                (constraint, battle) => Evaluate(constraints[constraint], battles[battle]),
                out int[]? assignment,
                out bool unknownPossible) || assignment is null)
        {
            return unknownPossible
                ? RewardRouteEvaluation.Unknown("CombatPotionRewardSequenceNotReliablyEvaluated")
                : RewardRouteEvaluation.NoMatch("CombatPotionRewardSequenceRejected");
        }
        return RewardRouteEvaluation.Match(
            Enumerable.Range(0, constraints.Length).Select(index => "combat-potion-reward-target-" + (index + 1)).ToArray(),
            constraints.Length > 0 ? battles[assignment[0]].BattleOrdinal : battles[0].BattleOrdinal,
            assignment.Select(index => battles[index].EvidenceCode).ToArray());
    }

    private static bool TryFindUnorderedAssignment(
        int constraintCount,
        int battleCount,
        Func<int, int, BattleConditionEvaluation> evaluate,
        out int[]? assignment,
        out bool unknownPossible)
    {
        assignment = null;
        unknownPossible = false;
        if (constraintCount == 0)
        {
            assignment = Array.Empty<int>();
            return true;
        }
        if (constraintCount > battleCount) return false;

        var chosen = new int[constraintCount];
        var used = new bool[battleCount];
        int[]? foundAssignment = null;
        bool unknown = false;
        bool Search(int constraint)
        {
            if (constraint == constraintCount)
            {
                foundAssignment = chosen.ToArray();
                return true;
            }
            for (int battle = 0; battle < battleCount; battle++)
            {
                if (used[battle]) continue;
                BattleConditionEvaluation result = evaluate(constraint, battle);
                if (result.Disposition == SearchDisposition.NoMatch) continue;
                if (result.Disposition == SearchDisposition.Unknown)
                {
                    unknown = true;
                    continue;
                }
                used[battle] = true;
                chosen[constraint] = battle;
                if (Search(constraint + 1)) return true;
                used[battle] = false;
            }
            return false;
        }
        bool matched = Search(0);
        assignment = foundAssignment;
        unknownPossible = unknown;
        return matched;
    }

    private static NormalCombatRewardBattleResult[] FirstBattles(
        NormalCombatRewardRoutePredictionResult route,
        int count) =>
        route.Battles
            .Where(item => item.BattleOrdinal >= 1 && item.BattleOrdinal <= count)
            .OrderBy(item => item.BattleOrdinal)
            .Take(count)
            .ToArray();

    private static int[]? FindSequencePermutation(
        int count,
        CombatRewardSequenceOrderMode orderMode,
        Func<int, int, BattleConditionEvaluation> evaluate,
        out bool unknownPossible)
    {
        unknownPossible = false;
        IEnumerable<int[]> permutations = orderMode == CombatRewardSequenceOrderMode.Ordered
            ? new[] { Enumerable.Range(0, count).ToArray() }
            : EnumerateRewardPermutations(count);

        foreach (int[] permutation in permutations)
        {
            bool anyUnknown = false;
            bool rejected = false;
            for (int slot = 0; slot < count; slot++)
            {
                BattleConditionEvaluation result = evaluate(slot, permutation[slot]);
                if (result.Disposition == SearchDisposition.NoMatch)
                {
                    rejected = true;
                    break;
                }
                if (result.Disposition == SearchDisposition.Unknown)
                    anyUnknown = true;
            }
            if (!rejected && !anyUnknown)
                return permutation;
            if (!rejected && anyUnknown)
                unknownPossible = true;
        }
        return null;
    }

    private static IEnumerable<int[]> EnumerateRewardPermutations(int count)
    {
        if (count <= 1)
        {
            yield return new[] { 0 };
            yield break;
        }
        if (count == 2)
        {
            yield return new[] { 0, 1 };
            yield return new[] { 1, 0 };
            yield break;
        }
        yield return new[] { 0, 1, 2 };
        yield return new[] { 0, 2, 1 };
        yield return new[] { 1, 0, 2 };
        yield return new[] { 1, 2, 0 };
        yield return new[] { 2, 0, 1 };
        yield return new[] { 2, 1, 0 };
    }

    private static BattleConditionEvaluation EvaluateBattleCondition(
        NormalCombatRewardSearchCondition condition,
        NormalCombatRewardBattleResult battle)
    {
        if (battle.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported)
        {
            return BattleConditionEvaluation.Unknown("BattlePrecisionUnknown");
        }
        IReadOnlyList<NormalCombatRewardCardResult> rewardCards = battle.CardRewards.Count > 0
            ? battle.CardRewards.SelectMany(reward => reward.Cards).ToArray()
            : battle.Cards;
        if (rewardCards.Any(card => card.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported))
        {
            return BattleConditionEvaluation.Unknown("RewardCardIdentityUnknown");
        }
        if (!QueryKeySetPredicate.MatchesKeySet(rewardCards.Select(card => card.CardKey), condition.Cards))
        {
            return BattleConditionEvaluation.NoMatch("RewardCardsRejected");
        }

        if (condition.PotionRequirement == NormalCombatPotionRequirement.MustDrop && !battle.Potion.Generated)
        {
            return BattleConditionEvaluation.NoMatch("PotionDropRequired");
        }
        if (condition.PotionRequirement == NormalCombatPotionRequirement.MustNotDrop && battle.Potion.Generated)
        {
            return BattleConditionEvaluation.NoMatch("PotionDropBanned");
        }
        if (!condition.Potions.IsEmpty)
        {
            if (!battle.Potion.Generated)
            {
                return BattleConditionEvaluation.NoMatch("PotionIdentityRequiresDrop");
            }
            if (battle.Potion.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported ||
                !battle.Potion.PotionKey.HasValue)
            {
                return BattleConditionEvaluation.Unknown("PotionIdentityUnknown");
            }
            if (!QueryKeySetPredicate.MatchesKeySet(new[] { battle.Potion.PotionKey.Value }, condition.Potions))
            {
                return BattleConditionEvaluation.NoMatch("PotionIdentityRejected");
            }
        }

        if (condition.MinimumGold.HasValue || condition.MaximumGold.HasValue)
        {
            if (!battle.Gold.HasValue || battle.GoldStatus != NormalCombatGoldProjectionStatus.ConditionalDefaultMonsterFullKill)
            {
                return BattleConditionEvaluation.Unknown("GoldNotAvailableUnderSupportedBaseline");
            }
            int supportedGold = battle.GoldRewards.Count > 0
                ? battle.GoldRewards.Sum(entry => entry.Amount)
                : battle.Gold.Value;
            if (condition.MinimumGold.HasValue && supportedGold < condition.MinimumGold.Value)
            {
                return BattleConditionEvaluation.NoMatch("GoldBelowMinimum");
            }
            if (condition.MaximumGold.HasValue && supportedGold > condition.MaximumGold.Value)
            {
                return BattleConditionEvaluation.NoMatch("GoldAboveMaximum");
            }
        }
        return BattleConditionEvaluation.Match();
    }

    private static SearchQueryEvaluation ValidateSection(PredictionSection? section, string domain)
    {
        if (section is null)
        {
            return SearchQueryEvaluation.Unknown(domain + "SectionMissing");
        }
        if (section.DomainStatus == SeedDomainEvaluationStatus.Unsupported)
        {
            return SearchQueryEvaluation.Unsupported(domain + "DomainUnsupported:" + section.IssueCode);
        }
        if (section.DomainStatus != SeedDomainEvaluationStatus.Evaluated)
        {
            return SearchQueryEvaluation.Unknown(domain + "DomainUnknown:" + section.IssueCode);
        }
        return SearchQueryEvaluation.Match();
    }

    private static bool AncientOptionsEvaluated(AncientOptionsEvaluationStatus status) =>
        status is AncientOptionsEvaluationStatus.EvaluatedNonEmpty or
            AncientOptionsEvaluationStatus.EvaluatedKnownEmpty or
            AncientOptionsEvaluationStatus.EvaluatedLocked;

    private static StructuredNeowConditionEvaluation EvaluateStructuredNeowCondition(
        OpeningRouteContext route,
        NeowStructuredEffectSearchCondition condition,
        NeowEffectAuthoritySnapshot? authority)
    {
        if (!RouteContainsRelic(route, condition.SourceRelicKey))
        {
            return StructuredNeowConditionEvaluation.NoMatch("StructuredEffectSourceNotOnRoute");
        }

        if (condition.Scope == NeowStructuredEffectScope.BonesOfferedRelics)
        {
            if (route.Choice.BonesOutcome is null)
            {
                return StructuredNeowConditionEvaluation.NoMatch("BonesOfferMissing");
            }
            ModelKey[] offered = route.Choice.BonesOutcome.OfferedRelics.ToArray();
            if (offered.Length != 2)
            {
                return StructuredNeowConditionEvaluation.Unknown("BonesOfferCountNotExact");
            }
            return UnorderedKeysContain(offered, condition.OutputKeys)
                ? StructuredNeowConditionEvaluation.Match(route.BonesRoute?.RouteEvidenceCode ?? route.Choice.EffectEvidenceCode)
                : StructuredNeowConditionEvaluation.NoMatch("BonesOfferedRelicTargetsMismatch");
        }

        if (condition.Scope == NeowStructuredEffectScope.FinalCurse)
        {
            if (route.BonesRoute is null)
            {
                return StructuredNeowConditionEvaluation.NoMatch("FinalCurseRequiresBonesRoute");
            }
            ProductRelevantProjectionStatus status = route.Outcome?.ProjectionStatus ??
                route.BonesRoute.ProductRelevantProjectionStatus;
            if (status == ProductRelevantProjectionStatus.NotEvaluatedByPolicy)
            {
                return StructuredNeowConditionEvaluation.NotEvaluated("ComplexResultNotEvaluatedByPolicy");
            }
            PredictionPrecision precision = route.Outcome?.FinalCursePrecision ??
                route.BonesRoute.SharedContinuation.FinalCursePrecision;
            if (precision != PredictionPrecision.Exact)
            {
                return StructuredNeowConditionEvaluation.Unknown("FinalCurseNotExact");
            }
            ModelKey? actual = route.Outcome?.FinalCurseKey ?? route.BonesRoute.SharedContinuation.FinalCurseKey;
            return actual.HasValue && actual.Value == condition.OutputKeys[0]
                ? StructuredNeowConditionEvaluation.Match(route.BonesRoute.SharedContinuation.EvidenceCode)
                : StructuredNeowConditionEvaluation.NoMatch("FinalCurseStructuredMismatch");
        }

        if (condition.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset)
        {
            if (route.BonesRoute is null || condition.Scope != NeowStructuredEffectScope.NestedRelics ||
                condition.OutputKind != NeowStructuredOutputKind.Relic || condition.OutputKeys.Count is < 1 or > 3)
                return StructuredNeowConditionEvaluation.NoMatch("GroupedCapsuleRequiresBonesSmallLargeRoute");
            SourceGroupProjection small = GetSourceGroups(route, BaseGameModelKeys.Relics.SmallCapsule);
            SourceGroupProjection large = GetSourceGroups(route, BaseGameModelKeys.Relics.LargeCapsule);
            if (!small.IsPresent || !large.IsPresent)
                return StructuredNeowConditionEvaluation.NoMatch("GroupedCapsuleSourceMissing");
            if (small.Status == ProductRelevantProjectionStatus.NotEvaluatedByPolicy ||
                large.Status == ProductRelevantProjectionStatus.NotEvaluatedByPolicy)
                return StructuredNeowConditionEvaluation.NotEvaluated("GroupedCapsuleNotEvaluatedByPolicy");
            if (small.Precision != PredictionPrecision.Exact || large.Precision != PredictionPrecision.Exact)
                return StructuredNeowConditionEvaluation.Unknown("GroupedCapsuleOutputNotExact");
            PredictedEffect[] effects = RelevantEffects(small.Groups.Concat(large.Groups), condition).ToArray();
            if (effects.Any(effect => effect.Precision != PredictionPrecision.Exact || !effect.TargetKey.HasValue))
                return StructuredNeowConditionEvaluation.Unknown("GroupedCapsuleOutputNotExact");
            ModelKey[] actual = effects
                .SelectMany(effect => Enumerable.Repeat(effect.TargetKey!.Value, Math.Max(1, effect.Multiplicity)))
                .ToArray();
            if (actual.Length != 3)
                return StructuredNeowConditionEvaluation.Unknown("GroupedCapsuleOutputCountNotExact");
            return UnorderedKeysContain(actual, condition.OutputKeys)
                ? StructuredNeowConditionEvaluation.Match(effects.FirstOrDefault()?.EvidenceCode ?? default)
                : StructuredNeowConditionEvaluation.NoMatch("GroupedCapsuleMultisetMismatch");
        }

        SourceGroupProjection source = GetSourceGroups(route, condition.SourceRelicKey);
        if (!source.IsPresent)
        {
            return StructuredNeowConditionEvaluation.NoMatch("StructuredEffectSourceMissing");
        }
        if (source.Status == ProductRelevantProjectionStatus.NotEvaluatedByPolicy)
        {
            return StructuredNeowConditionEvaluation.NotEvaluated("StructuredEffectNotEvaluatedByPolicy");
        }
        if (source.Precision is PredictionPrecision.Unknown or PredictionPrecision.Unsupported)
        {
            return StructuredNeowConditionEvaluation.Unknown("StructuredEffectSourcePrecisionUnknown");
        }

        PredictedEffectGroup[] groups = FilterGroupsForScope(source.Groups, condition.Scope).ToArray();
        if (groups.Length == 0)
        {
            return StructuredNeowConditionEvaluation.NoMatch("StructuredEffectGroupMissing");
        }
        bool hasComparableOutput = RelevantEffects(groups, condition).Any();
        bool capabilityOnly = groups
            .SelectMany(group => group.OrderedItems)
            .Any(effect => effect.IsProductRelevant && effect.Kind == PredictedEffectKind.DescriptionOnly);
        if (!hasComparableOutput && capabilityOnly)
        {
            return StructuredNeowConditionEvaluation.Unknown("StructuredOutputCapabilityOnly");
        }

        return condition.Kind switch
        {
            NeowStructuredConditionKind.ExactSingle => EvaluateExactSingle(groups, condition),
            NeowStructuredConditionKind.ExactUnorderedPair => EvaluateExactUnorderedPair(groups, condition),
            NeowStructuredConditionKind.IndependentOfferGroupTargets => EvaluateIndependentOfferTargets(groups, condition),
            NeowStructuredConditionKind.StructuredCardComposition => EvaluateStructuredCardComposition(groups, condition, authority),
            NeowStructuredConditionKind.SpecialOffer => EvaluateSpecialOffer(groups, condition, authority),
            _ => StructuredNeowConditionEvaluation.Unknown("UnsupportedStructuredNeowCondition")
        };
    }

    private static StructuredNeowConditionEvaluation EvaluateExactSingle(
        IReadOnlyList<PredictedEffectGroup> groups,
        NeowStructuredEffectSearchCondition condition)
    {
        ModelKey target = condition.OutputKeys[0];
        bool unresolved = false;
        foreach (PredictedEffect effect in RelevantEffects(groups, condition))
        {
            if (effect.Precision != PredictionPrecision.Exact || !effect.TargetKey.HasValue)
            {
                unresolved = true;
                continue;
            }
            if (effect.TargetKey.Value == target)
            {
                return StructuredNeowConditionEvaluation.Match(effect.EvidenceCode);
            }
        }
        return unresolved
            ? StructuredNeowConditionEvaluation.Unknown("ExactSingleOutputNotExact")
            : StructuredNeowConditionEvaluation.NoMatch("ExactSingleOutputMismatch");
    }

    private static StructuredNeowConditionEvaluation EvaluateExactUnorderedPair(
        IReadOnlyList<PredictedEffectGroup> groups,
        NeowStructuredEffectSearchCondition condition)
    {
        PredictedEffect[] effects = RelevantEffects(groups, condition).ToArray();
        if (effects.Any(effect => effect.Precision != PredictionPrecision.Exact || !effect.TargetKey.HasValue))
        {
            return StructuredNeowConditionEvaluation.Unknown("UnorderedPairOutputNotExact");
        }
        ModelKey[] actual = effects
            .SelectMany(effect => Enumerable.Repeat(effect.TargetKey!.Value, Math.Max(1, effect.Multiplicity)))
            .ToArray();
        if (actual.Length != 2)
        {
            return StructuredNeowConditionEvaluation.Unknown("UnorderedPairOutputCountNotExact");
        }
        return UnorderedKeysContain(actual, condition.OutputKeys)
            ? StructuredNeowConditionEvaluation.Match(effects.FirstOrDefault()?.EvidenceCode ?? default)
            : StructuredNeowConditionEvaluation.NoMatch("UnorderedOutputTargetsMismatch");
    }

    private static StructuredNeowConditionEvaluation EvaluateIndependentOfferTargets(
        IReadOnlyList<PredictedEffectGroup> groups,
        NeowStructuredEffectSearchCondition condition)
    {
        PredictedEffectGroup[] orderedGroups = groups.OrderBy(group => group.GroupOrder).ToArray();
        bool unresolved = false;

        if (condition.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.ExactOrder)
        {
            ModelKey?[] slots = condition.KaleidoscopePositionalSlots.Count == 2
                ? condition.KaleidoscopePositionalSlots.ToArray()
                : condition.OutputKeys.Take(2).Select(key => (ModelKey?)key)
                    .Concat(Enumerable.Repeat<ModelKey?>(null, Math.Max(0, 2 - condition.OutputKeys.Count)))
                    .Take(2).ToArray();
            if (orderedGroups.Length < 2)
                return StructuredNeowConditionEvaluation.Unknown("KaleidoscopeExactOrderGroupCountNotExact");
            for (int index = 0; index < 2; index++)
            {
                ModelKey? target = slots[index];
                if (!target.HasValue) continue;
                GroupTargets actual = GetExactGroupTargets(orderedGroups[index], condition);
                unresolved |= actual.Unresolved;
                if (!actual.Keys.Contains(target.GetValueOrDefault()))
                {
                    return unresolved
                        ? StructuredNeowConditionEvaluation.Unknown("KaleidoscopeExactOrderGroupsNotExact")
                        : StructuredNeowConditionEvaluation.NoMatch("KaleidoscopeExactOrderTargetsMismatch");
                }
            }
            return unresolved
                ? StructuredNeowConditionEvaluation.Unknown("KaleidoscopeExactOrderGroupsNotExact")
                : StructuredNeowConditionEvaluation.Match(
                    orderedGroups[0].OrderedItems.FirstOrDefault()?.EvidenceCode ?? default);
        }

        ModelKey first = condition.OutputKeys[0];
        if (condition.OutputKeys.Count == 1)
        {
            foreach (PredictedEffectGroup group in orderedGroups)
            {
                GroupTargets targets = GetExactGroupTargets(group, condition);
                unresolved |= targets.Unresolved;
                if (targets.Keys.Contains(first))
                {
                    return StructuredNeowConditionEvaluation.Match(
                        group.OrderedItems.FirstOrDefault()?.EvidenceCode ?? default);
                }
            }
            return unresolved
                ? StructuredNeowConditionEvaluation.Unknown("IndependentOfferGroupsNotExact")
                : StructuredNeowConditionEvaluation.NoMatch("IndependentOfferGroupTargetMissing");
        }

        ModelKey second = condition.OutputKeys[1];
        for (int i = 0; i < orderedGroups.Length; i++)
        {
            GroupTargets left = GetExactGroupTargets(orderedGroups[i], condition);
            unresolved |= left.Unresolved;
            if (!left.Keys.Contains(first)) continue;
            for (int j = 0; j < orderedGroups.Length; j++)
            {
                if (i == j) continue;
                GroupTargets right = GetExactGroupTargets(orderedGroups[j], condition);
                unresolved |= right.Unresolved;
                if (right.Keys.Contains(second))
                {
                    return StructuredNeowConditionEvaluation.Match(
                        orderedGroups[i].OrderedItems.FirstOrDefault()?.EvidenceCode ?? default);
                }
            }
        }
        return unresolved
            ? StructuredNeowConditionEvaluation.Unknown("IndependentOfferGroupsNotExact")
            : StructuredNeowConditionEvaluation.NoMatch("IndependentOfferGroupTargetsMismatch");
    }

    private static StructuredNeowConditionEvaluation EvaluateStructuredCardComposition(
        IReadOnlyList<PredictedEffectGroup> groups,
        NeowStructuredEffectSearchCondition condition,
        NeowEffectAuthoritySnapshot? authority)
    {
        if (authority?.HasExactCharacterRewardPool != true || authority.CharacterRewardPool is null)
        {
            return StructuredNeowConditionEvaluation.Unknown("ScrollBoxesCardRarityAuthorityMissing");
        }
        var rarityByKey = authority.CharacterRewardPool
            .GroupBy(card => card.CardKey, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.First().Rarity, ModelKeyComparer.Instance);
        EffectCardRarity[] targetRarities = condition.OutputKeys
            .Select(key => rarityByKey.TryGetValue(key, out EffectCardRarity rarity) ? rarity : (EffectCardRarity)(-1))
            .ToArray();
        int targetCommon = targetRarities.Count(rarity => rarity == EffectCardRarity.Common);
        int targetUncommon = targetRarities.Count(rarity => rarity == EffectCardRarity.Uncommon);
        if (targetRarities.Any(rarity => rarity != EffectCardRarity.Common && rarity != EffectCardRarity.Uncommon) ||
            targetCommon > 2 ||
            targetUncommon > 1)
        {
            return StructuredNeowConditionEvaluation.NoMatch("ScrollBoxesCompositionRarityMismatch");
        }

        bool unresolved = false;
        foreach (PredictedEffectGroup group in groups)
        {
            GroupTargets targets = GetExactGroupTargets(group, condition);
            unresolved |= targets.Unresolved;
            if (targets.Unresolved)
            {
                continue;
            }

            EffectCardRarity[] actualRarities = targets.Keys
                .Select(key => rarityByKey.TryGetValue(key, out EffectCardRarity rarity) ? rarity : (EffectCardRarity)(-1))
                .ToArray();
            if (actualRarities.Any(rarity => rarity != EffectCardRarity.Common && rarity != EffectCardRarity.Uncommon))
            {
                unresolved = true;
                continue;
            }
            bool productionShapeExact =
                actualRarities.Count(rarity => rarity == EffectCardRarity.Common) == 2 &&
                actualRarities.Count(rarity => rarity == EffectCardRarity.Uncommon) == 1;
            if (productionShapeExact && UnorderedKeysContain(targets.Keys, condition.OutputKeys))
            {
                return StructuredNeowConditionEvaluation.Match(group.OrderedItems.FirstOrDefault()?.EvidenceCode ?? default);
            }
        }
        return unresolved
            ? StructuredNeowConditionEvaluation.Unknown("ScrollBoxesBundleNotExact")
            : StructuredNeowConditionEvaluation.NoMatch("ScrollBoxesBundleCompositionMismatch");
    }

    private static StructuredNeowConditionEvaluation EvaluateSpecialOffer(
        IReadOnlyList<PredictedEffectGroup> groups,
        NeowStructuredEffectSearchCondition condition,
        NeowEffectAuthoritySnapshot? authority)
    {
        if (condition.SpecialOffer != NeowSpecialOfferKind.ScrollBoxesTripleClaw)
        {
            return StructuredNeowConditionEvaluation.Unknown("UnsupportedNeowSpecialOffer");
        }
        ModelKey claw = authority?.ClawKey ?? BaseGameModelKeys.Cards.Claw;
        bool unresolved = false;
        foreach (PredictedEffectGroup group in groups)
        {
            PredictedEffect[] cards = group.OrderedItems
                .Where(effect => effect.Kind == PredictedEffectKind.AddCard && effect.IsProductRelevant)
                .ToArray();
            unresolved |= cards.Any(effect => effect.Precision != PredictionPrecision.Exact || !effect.TargetKey.HasValue);
            if (cards.Length == 1 && cards[0].Precision == PredictionPrecision.Exact &&
                cards[0].TargetKey == claw && cards[0].Multiplicity == 3)
            {
                return StructuredNeowConditionEvaluation.Match(cards[0].EvidenceCode);
            }
        }
        return unresolved
            ? StructuredNeowConditionEvaluation.Unknown("ScrollBoxesSpecialOfferNotExact")
            : StructuredNeowConditionEvaluation.NoMatch("ScrollBoxesTripleClawMissing");
    }

    private static IEnumerable<PredictedEffectGroup> FilterGroupsForScope(
        IEnumerable<PredictedEffectGroup> groups,
        NeowStructuredEffectScope scope) =>
        groups.Where(group => group.IsProductRelevant).Where(group => scope switch
        {
            NeowStructuredEffectScope.SelectableOfferGroups =>
                group.SelectionPolicy is EffectSelectionPolicy.ChooseExactlyOne or
                    EffectSelectionPolicy.ChooseOneOrSkip or
                    EffectSelectionPolicy.ChooseExactlyN or
                    EffectSelectionPolicy.ChooseAny or
                    EffectSelectionPolicy.OptionalClaim,
            _ => true
        });

    private static IEnumerable<PredictedEffect> RelevantEffects(
        IEnumerable<PredictedEffectGroup> groups,
        NeowStructuredEffectSearchCondition condition) =>
        groups.OrderBy(group => group.GroupOrder)
            .SelectMany(group => group.OrderedItems.OrderBy(effect => effect.ItemOrder))
            .Where(effect => effect.IsProductRelevant)
            .Where(effect => condition.Scope switch
            {
                NeowStructuredEffectScope.NestedRelics =>
                    effect.Kind == PredictedEffectKind.AddRelic && effect.Relation == PredictedEffectRelation.NestedRelic,
                NeowStructuredEffectScope.TransformResults => effect.Kind == PredictedEffectKind.TransformCard,
                NeowStructuredEffectScope.GeneratedPotions =>
                    effect.Kind is PredictedEffectKind.AddPotion or PredictedEffectKind.AttemptAddPotion,
                _ => EffectMatchesOutputKind(effect, condition.OutputKind)
            });

    private static bool EffectMatchesOutputKind(PredictedEffect effect, NeowStructuredOutputKind outputKind) => outputKind switch
    {
        NeowStructuredOutputKind.Relic => effect.Kind == PredictedEffectKind.AddRelic,
        NeowStructuredOutputKind.Card => effect.Kind is PredictedEffectKind.AddCard or PredictedEffectKind.TransformCard,
        NeowStructuredOutputKind.Potion => effect.Kind is PredictedEffectKind.AddPotion or PredictedEffectKind.AttemptAddPotion,
        NeowStructuredOutputKind.Curse => effect.Kind == PredictedEffectKind.AddCard,
        _ => false
    };

    private static GroupTargets GetExactGroupTargets(
        PredictedEffectGroup group,
        NeowStructuredEffectSearchCondition condition)
    {
        PredictedEffect[] relevant = RelevantEffects(new[] { group }, condition).ToArray();
        bool unresolved = relevant.Any(effect => effect.Precision != PredictionPrecision.Exact || !effect.TargetKey.HasValue);
        ModelKey[] keys = relevant
            .Where(effect => effect.Precision == PredictionPrecision.Exact && effect.TargetKey.HasValue)
            .SelectMany(effect => Enumerable.Repeat(effect.TargetKey!.Value, Math.Max(1, effect.Multiplicity)))
            .ToArray();
        return new GroupTargets(keys, unresolved);
    }

    private static SourceGroupProjection GetSourceGroups(OpeningRouteContext route, ModelKey sourceRelicKey)
    {
        // A DistinctPlayerChoiceOutcome is the route-specific product result for
        // its choice relic. Never combine it with the pre-choice scoped offer,
        // otherwise one condition could match the offer while another matches a
        // different concrete choice outcome on the same nominal acquisition order.
        // Optional traces were already restricted to the authored multiset (and
        // positional picks) in EvaluateOpeningRoute. Keep their original offer
        // groups here: the choice summary collapses two K groups into one.
        if (route.Outcome?.ChoiceRelicKey == sourceRelicKey && !NeowChoiceCommitment.IsOptionalCardOffer(sourceRelicKey))
        {
            return new SourceGroupProjection(
                true,
                route.Outcome.ChoiceEffectGroups,
                route.Outcome.ProductRelevantProjectionPrecision,
                route.Outcome.ProjectionStatus);
        }

        if (route.BonesRoute is null && route.Choice.RelicKey == sourceRelicKey)
        {
            return new SourceGroupProjection(
                true,
                route.Choice.EffectGroups,
                route.Choice.ProductRelevantProjectionPrecision,
                route.Choice.ProductRelevantProjectionStatus);
        }

        if (route.BonesRoute is not null)
        {
            BonesRelicScopedResult? scoped = route.BonesRoute.RelicScopedResults
                .FirstOrDefault(item => item.SourceRelicKey == sourceRelicKey);
            if (scoped is not null)
            {
                return new SourceGroupProjection(
                    true,
                    scoped.EffectGroups,
                    scoped.ProductRelevantProjectionPrecision,
                    scoped.ProductRelevantProjectionStatus);
            }
        }

        return new SourceGroupProjection(
            false,
            Array.Empty<PredictedEffectGroup>(),
            PredictionPrecision.Unsupported,
            ProductRelevantProjectionStatus.Unsupported);
    }

    /// <summary>
    /// Multiset containment for optional multi-output refinements. Repeated
    /// targets retain multiplicity, so selecting the same LeafyPoultice result
    /// twice still requires two generated copies.
    /// </summary>
    private static bool UnorderedKeysContain(
        IReadOnlyList<ModelKey> actual,
        IReadOnlyList<ModelKey> required)
    {
        if (required.Count > actual.Count)
        {
            return false;
        }

        var remaining = actual
            .GroupBy(key => key, ModelKeyComparer.Instance)
            .ToDictionary(group => group.Key, group => group.Count(), ModelKeyComparer.Instance);
        foreach (ModelKey key in required)
        {
            if (!remaining.TryGetValue(key, out int count) || count <= 0)
            {
                return false;
            }
            remaining[key] = count - 1;
        }
        return true;
    }

    private sealed record GroupTargets(IReadOnlyList<ModelKey> Keys, bool Unresolved);
    private sealed record SourceGroupProjection(
        bool IsPresent,
        IReadOnlyList<PredictedEffectGroup> Groups,
        PredictionPrecision Precision,
        ProductRelevantProjectionStatus Status);
    private sealed record StructuredNeowConditionEvaluation(
        SearchDisposition Disposition,
        string FailureCode,
        EvidenceCode EvidenceCode)
    {
        public static StructuredNeowConditionEvaluation Match(EvidenceCode evidence) =>
            new(SearchDisposition.Match, string.Empty, evidence);
        public static StructuredNeowConditionEvaluation NoMatch(string code) =>
            new(SearchDisposition.NoMatch, code, default);
        public static StructuredNeowConditionEvaluation Unknown(string code) =>
            new(SearchDisposition.Unknown, code, default);
        public static StructuredNeowConditionEvaluation NotEvaluated(string code) =>
            new(SearchDisposition.NotEvaluatedByPolicy, code, default);
    }

    private static bool RequiresBonesRoute(ExactSearchEvaluationProjection filter) =>
        (filter.NeowRoute?.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones &&
         filter.StructuredNeowEffects.Count > 0) ||
        filter.RequireNeowsBones ||
        !filter.BonesRelics.IsEmpty ||
        filter.RequiredBonesCombination.Count > 0 ||
        filter.RequiredBonesAcquisitionOrder.Count > 0 ||
        filter.RequiredFinalCurse.HasValue ||
        filter.BannedFinalCurses.Count > 0 ||
        filter.Preset != NeowSearchPreset.None;

    private static bool HasRouteScopedNeowConstraints(ExactSearchEvaluationProjection filter) =>
        RequiresBonesRoute(filter) ||
        filter.RequireSmallCapsule ||
        filter.RequireLargeCapsule ||
        !filter.CapsuleContainedRelics.IsEmpty ||
        filter.RequireWhetstone ||
        filter.RequireWarPaint ||
        filter.EffectOutputConditions.Any(condition => !condition.IsEmpty) ||
        filter.StructuredNeowEffects.Any(condition => !condition.IsEmpty);

    private static bool RouteContainsRelic(OpeningRouteContext route, ModelKey key) =>
        route.Choice.RelicKey == key || route.AcquisitionOrder.Contains(key);

    private static IEnumerable<PredictedEffect> EnumerateRouteEffects(OpeningRouteContext route)
    {
        if (route.BonesRoute is null)
        {
            return EnumerateEffects(route.Choice.EffectGroups);
        }
        return route.BonesRoute.RelicScopedResults
            .SelectMany(scope => EnumerateEffects(scope.EffectGroups));
    }

    private static IEnumerable<PredictedEffect> EnumerateSourceEffects(OpeningRouteContext route, ModelKey sourceRelicKey)
    {
        IEnumerable<PredictedEffect> objective = route.BonesRoute is null
            ? route.Choice.RelicKey == sourceRelicKey
                ? EnumerateEffects(route.Choice.EffectGroups)
                : Array.Empty<PredictedEffect>()
            : route.BonesRoute.RelicScopedResults
                .Where(scope => scope.SourceRelicKey == sourceRelicKey)
                .SelectMany(scope => EnumerateEffects(scope.EffectGroups));
        IEnumerable<PredictedEffect> choiceOutcome = route.Outcome?.ChoiceRelicKey == sourceRelicKey
            ? EnumerateEffects(route.Outcome.ChoiceEffectGroups)
            : Array.Empty<PredictedEffect>();
        return objective.Concat(choiceOutcome);
    }

    private static IEnumerable<PredictedEffect> EnumerateEffects(IEnumerable<PredictedEffectGroup> groups) =>
        groups.OrderBy(group => group.GroupOrder)
            .SelectMany(group => group.OrderedItems.OrderBy(effect => effect.ItemOrder));

    private sealed record OpeningRouteContext(
        string RouteId,
        NeowChoiceResult Choice,
        IReadOnlyList<ModelKey> AcquisitionOrder,
        BonesAcquisitionRouteResult? BonesRoute,
        DistinctPlayerChoiceOutcome? Outcome,
        string ContinuationFingerprint,
        PredictionPrecision OpeningRewardPrecision);

    private sealed record OpeningRouteEvaluation(
        SearchDisposition Disposition,
        OpeningRouteContext Route,
        IReadOnlyList<SearchMatchEvidence> Evidence,
        string FailureCode)
    {
        public static OpeningRouteEvaluation Match(OpeningRouteContext route, IReadOnlyList<SearchMatchEvidence> evidence) =>
            new(SearchDisposition.Match, route, evidence, string.Empty);
        public static OpeningRouteEvaluation NoMatch(OpeningRouteContext route, string code) =>
            new(SearchDisposition.NoMatch, route, Array.Empty<SearchMatchEvidence>(), code);
        public static OpeningRouteEvaluation Unknown(OpeningRouteContext route, string code) =>
            new(SearchDisposition.Unknown, route, Array.Empty<SearchMatchEvidence>(), code);
        public static OpeningRouteEvaluation NotEvaluatedByPolicy(OpeningRouteContext route, string code) =>
            new(SearchDisposition.NotEvaluatedByPolicy, route, Array.Empty<SearchMatchEvidence>(), code);
    }

    private sealed record RewardRouteEvaluation(
        SearchDisposition Disposition,
        IReadOnlyList<string> MatchedConditionIds,
        int? MatchedBattleOrdinal,
        IReadOnlyList<EvidenceCode> EvidenceCodes,
        string FailureCode)
    {
        public static RewardRouteEvaluation Match(IReadOnlyList<string> ids, int? ordinal, IReadOnlyList<EvidenceCode> evidence) =>
            new(SearchDisposition.Match, ids, ordinal, evidence, string.Empty);
        public static RewardRouteEvaluation NoMatch(string code) =>
            new(SearchDisposition.NoMatch, Array.Empty<string>(), null, Array.Empty<EvidenceCode>(), code);
        public static RewardRouteEvaluation Unknown(string code) =>
            new(SearchDisposition.Unknown, Array.Empty<string>(), null, Array.Empty<EvidenceCode>(), code);
    }

    private sealed record BattleConditionEvaluation(SearchDisposition Disposition, string FailureCode)
    {
        public static BattleConditionEvaluation Match() => new(SearchDisposition.Match, string.Empty);
        public static BattleConditionEvaluation NoMatch(string code) => new(SearchDisposition.NoMatch, code);
        public static BattleConditionEvaluation Unknown(string code) => new(SearchDisposition.Unknown, code);
    }
}
