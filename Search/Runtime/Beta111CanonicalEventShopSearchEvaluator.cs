using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Runtime;

/// <summary>
/// Production-Exact proof for the two canonical root-local v1 search surfaces.
/// Event Result is conditional event-local identity only; Merchant Colorless is the
/// canonical pristine Shops continuation only. Neither proof owns route/occurrence truth.
/// </summary>
internal static class Beta111CanonicalEventShopSearchEvaluator
{
    public static SearchQueryEvaluation Evaluate(
        ExactSearchExecutionRequest plan,
        ulong rootHash,
        RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(authority);

        var evidence = new List<SearchMatchEvidence>();

        SearchQueryEvaluation aggregate = TransformationAggregateValidator.Evaluate(plan, rootHash, authority);
        if (aggregate.Disposition != SearchDisposition.Match) return aggregate;
        evidence.AddRange(aggregate.Evidence);

        SearchQueryEvaluation eventResult = EvaluateEventResult(plan, rootHash, authority);
        if (eventResult.Disposition != SearchDisposition.Match)
            return eventResult;
        evidence.AddRange(eventResult.Evidence);

        SearchQueryEvaluation merchant = MerchantShopColorlessQueryEvaluator.Evaluate(plan.Evaluation, rootHash, authority);
        if (merchant.Disposition != SearchDisposition.Match)
            return merchant;
        evidence.AddRange(merchant.Evidence);

        return SearchQueryEvaluation.Match(evidence.Distinct().ToArray());
    }

    private static SearchQueryEvaluation EvaluateEventResult(
        ExactSearchExecutionRequest plan,
        ulong rootHash,
        RuntimeContextAuthoritySnapshot authority)
    {
        if (!plan.Evaluation.RequiresEventResultDomain)
            return SearchQueryEvaluation.Match();

        Beta111EventResultAuthority eventAuthority = Beta111EventResultAuthority.From(authority);
        Beta111EventResultProjection? projection = null;

        var evidence = new List<SearchMatchEvidence>();
        foreach ((EventResultSearchCondition condition, int index) in
                 plan.Evaluation.EventResultConditions.Select((condition, index) => (condition, index)))
        {
            if (condition.Kind > EventResultConditionKind.MorphicGroveGroupInitialBasicsContains)
            {
                if (!condition.IsValid) return SearchQueryEvaluation.Unsupported("EventResult.InvalidWhitelistCondition");
                if (!eventAuthority.IsBeta111) return SearchQueryEvaluation.Unsupported("EventResult.Beta111Required");
                bool match;
                if (EventResultTransformSemantics.IsTransform(condition.Kind))
                {
                    MorphicGrovePredictor.ValidateAuthority(plan.Detection, authority, condition.MorphicGroveScenario!);
                    var result = EventResultTransformSemantics.Evaluate(rootHash, condition);
                    if (result == MorphicGrovePredicateResult.Unknown) return SearchQueryEvaluation.Unknown("EventResult.InitialBasicPoolUnknown");
                    match = result == MorphicGrovePredicateResult.Match;
                }
                else match = condition.Kind switch {
                    EventResultConditionKind.TrialCase => Beta111TrialTinkerProjector.TrialCase(rootHash, authority.PlayerSlotIndex) == (int)condition.TrialCase!,
                    EventResultConditionKind.TinkerTimeTypeAndRider => Beta111TrialTinkerProjector.TinkerContains(rootHash, authority.PlayerSlotIndex,
                        (int)condition.TinkerCardType!, (int?)condition.TinkerRider),
                    _ => throw new InvalidOperationException("EventResult.UnrecognizedWhitelistKind") };
                if (!match) return SearchQueryEvaluation.NoMatch("EventResult.WhitelistRejected:" + condition.Kind);
                evidence.Add(new SearchMatchEvidence("EventResultConditionMatched", condition.TargetKey,
                    "event-result:" + condition.Kind + ":case=" + condition.TrialCase + ":type=" + condition.TinkerCardType + ":rider=" + condition.TinkerRider,
                    ProfileId: authority.ProfileId, StreamDomain: "EventLocal", Authority: SourceAuthority.OfficialRuntimeExact,
                    AuthorityFingerprint: condition.MorphicGroveScenario?.Fingerprint ?? eventAuthority.AuthorityFingerprint,
                    ConditionId: "event-result-" + index));
                continue;
            }
            if (condition.Kind == EventResultConditionKind.MorphicGroveGroupInitialBasicsContains)
            {
                if (!condition.IsValid) return SearchQueryEvaluation.Unsupported("MorphicGrove.InvalidInitialBasicsScenario");
                var root = TrustedRootHashInput.RootOnly(rootHash);
                if (!SeedPredictionRequest.TryCreateFromRootHash(root, authority.Character, plan.Ascension,
                    authority.PlayersCount, authority.PlayerSlotIndex, authority, plan.Evaluation.AncientOptionConditions, SeedPredictionDomainSelection.None,
                    SeedPredictionInputLimits.DefaultRelicSequencePreviewCount, plan.IncludeDiagnostics,
                    out var request, out var error))
                    return SearchQueryEvaluation.Unknown("MorphicGrove.RequestRejected:" + error);
                var prediction = RuntimeProfileRegistry.PredictMorphicGrove(plan.Detection, request!,
                    condition.MorphicGroveScenario!, MorphicGroveCommitment.InitialBasics);
                if (prediction.Projection is null)
                    return prediction.Precision == PredictionPrecision.Unsupported
                        ? SearchQueryEvaluation.Unsupported(prediction.Evidence) : SearchQueryEvaluation.Unknown(prediction.Evidence);
                var result = condition.MorphicGroveSecondCard is { } second
                    ? Beta111MorphicGroveProjector.ContainsPair(prediction.Projection, condition.TargetKey, second, finalDeckCard: false)
                    : Beta111MorphicGroveProjector.Contains(prediction.Projection, condition.TargetKey, finalDeckCard: false);
                if (result == MorphicGrovePredicateResult.Unknown) return SearchQueryEvaluation.Unknown(prediction.Evidence);
                if (result == MorphicGrovePredicateResult.NoMatch) return SearchQueryEvaluation.NoMatch("MorphicGrove.ContainsRejected");
                evidence.Add(new SearchMatchEvidence("EventResultConditionMatched", condition.TargetKey,
                    "event-result:morphic-grove:group:initial-basics:raw-contains" +
                    (condition.MorphicGroveSecondCard is { } paired ? ":pair:" + paired.Serialized : ""),
                    EvidenceCode: new EvidenceCode(prediction.Evidence), ProfileId: authority.ProfileId,
                    StreamDomain: "EventLocal", Authority: SourceAuthority.OfficialRuntimeExact,
                    AuthorityFingerprint: MorphicGroveQuerySemantics.Fingerprint(condition.MorphicGroveScenario),
                    ConditionId: "event-result-" + index));
                continue;
            }
            projection ??= Beta111EventResultProjector.Project(rootHash, authority.PlayerSlotIndex, eventAuthority);
            if (projection.TrashHeapPrecision == PredictionPrecision.Unsupported || projection.FakeMerchantPrecision == PredictionPrecision.Unsupported)
                return SearchQueryEvaluation.Unsupported(projection.EvidenceCode);
            bool matched;
            ModelKey? relatedKey = condition.TargetKey;
            PredictionPrecision precision;
            string source;
            switch (condition.Kind)
            {
                case EventResultConditionKind.TrashHeapGrabCard:
                    precision = projection.TrashHeapPrecision;
                    matched = projection.TrashHeapGrabCard == condition.TargetKey;
                    source = "event-result:trash-heap:grab";
                    break;
                case EventResultConditionKind.TrashHeapDiveRelic:
                    precision = projection.TrashHeapPrecision;
                    matched = projection.TrashHeapDiveRelic == condition.TargetKey;
                    source = "event-result:trash-heap:dive";
                    break;
                case EventResultConditionKind.ColorfulPhilosophersOfferedColor:
                    precision = projection.ColorfulPrecision;
                    matched = projection.ColorfulOfferedColors.Contains(condition.TargetKey);
                    source = "event-result:colorful-philosophers:offers";
                    break;
                case EventResultConditionKind.FakeMerchantOfferedFakeRelic:
                    precision = projection.FakeMerchantPrecision;
                    matched = projection.FakeMerchantInventory.Contains(condition.TargetKey);
                    source = "event-result:fake-merchant:inventory";
                    break;
                default:
                    return SearchQueryEvaluation.Unsupported("EventResultConditionKindUnsupported");
            }

            if (precision == PredictionPrecision.Unsupported)
                return SearchQueryEvaluation.Unsupported($"EventResultObservableUnsupported:{condition.Kind}");
            if (precision != PredictionPrecision.Exact)
                return SearchQueryEvaluation.Unknown($"EventResultObservableNotExact:{condition.Kind}");
            if (!matched)
                return SearchQueryEvaluation.NoMatch($"EventResultConditionRejected:{condition.Kind}:{index}");

            evidence.Add(new SearchMatchEvidence(
                "EventResultConditionMatched",
                relatedKey,
                source,
                EvidenceCode: new EvidenceCode(projection.EvidenceCode),
                ProfileId: authority.ProfileId,
                StreamDomain: "EventLocal",
                Authority: SourceAuthority.OfficialRuntimeExact,
                AuthorityFingerprint: eventAuthority.AuthorityFingerprint,
                ConditionId: "event-result-" + index));
        }

        return SearchQueryEvaluation.Match(evidence);
    }

}
