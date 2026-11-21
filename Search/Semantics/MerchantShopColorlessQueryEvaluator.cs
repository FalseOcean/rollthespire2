using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// Topology-independent evaluation of the Merchant / Shop Colorless observation
/// window. Family S and Production Exact replay this evaluator independently.
/// </summary>
internal static class MerchantShopColorlessQueryEvaluator
{
    public static SearchQueryEvaluation Evaluate(
        ExactSearchEvaluationProjection evaluation,
        ulong rootHash,
        RuntimeContextAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(authority);
        if (!evaluation.RequiresMerchantColorlessDomain)
            return SearchQueryEvaluation.Match();

        Beta111MerchantColorlessAuthority merchantAuthority = Beta111MerchantColorlessAuthority.From(authority);
        int maxOrdinal = evaluation.MerchantColorlessConditions.Count == 0
            ? 0
            : evaluation.MerchantColorlessConditions.Max(condition => condition.MerchantOrdinal);
        if (evaluation.MerchantColorlessSequenceConditions.Count > 0)
            maxOrdinal = Math.Max(maxOrdinal, evaluation.MerchantColorlessSequenceConditions.Max(condition => condition.Count));

        Beta111ShopColorlessProjection projection = Beta111NormalMerchantColorlessSequenceProjector.Project(
            rootHash,
            merchantAuthority,
            maxOrdinal);
        if (projection.Precision == PredictionPrecision.Unsupported)
            return SearchQueryEvaluation.Unsupported(projection.EvidenceCode);
        if (projection.Merchants.Count == 0)
            return projection.Precision == PredictionPrecision.Exact
                ? SearchQueryEvaluation.NoMatch(projection.EvidenceCode)
                : SearchQueryEvaluation.Unknown(projection.EvidenceCode);

        var evidence = new List<SearchMatchEvidence>();
        bool unresolved = false;
        foreach ((MerchantColorlessSlotCondition condition, int index) in
                 evaluation.MerchantColorlessConditions.Select((condition, index) => (condition, index)))
        {
            Beta111NormalMerchantProjection? merchant = projection.Merchants
                .FirstOrDefault(item => item.MerchantOrdinal == condition.MerchantOrdinal);
            if (merchant is null)
            {
                unresolved = true;
                continue;
            }

            ModelKey actual = condition.Slot == MerchantColorlessSlot.Uncommon
                ? merchant.UncommonColorless
                : merchant.RareColorless;
            if (actual != condition.TargetCardKey)
                return SearchQueryEvaluation.NoMatch($"MerchantColorlessConditionRejected:{condition.MerchantOrdinal}:{condition.Slot}:{index}");

            evidence.Add(new SearchMatchEvidence(
                "MerchantColorlessConditionMatched",
                condition.TargetCardKey,
                "merchant-colorless:canonical",
                EvidenceCode: new EvidenceCode(projection.EvidenceCode),
                ProfileId: authority.ProfileId,
                StreamDomain: "Shops",
                Authority: SourceAuthority.OfficialRuntimeExact,
                AuthorityFingerprint: merchantAuthority.AuthorityFingerprint,
                Ordinal: condition.MerchantOrdinal,
                ConditionId: "merchant-colorless-" + index));
        }

        int sequenceIndex = 0;
        foreach (MerchantColorlessSequenceSearchCondition condition in evaluation.MerchantColorlessSequenceConditions)
        {
            ModelKey[] actual = projection.Merchants
                .Take(condition.Count)
                .Select(item => condition.Slot == MerchantColorlessSlot.Uncommon ? item.UncommonColorless : item.RareColorless)
                .ToArray();
            if (actual.Length < condition.Count)
            {
                if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered &&
                    actual.Where((key, index) => condition.Slots[index] is { } target && key != target).Any())
                    return SearchQueryEvaluation.NoMatch($"MerchantColorlessSequenceRejected:{condition.Slot}:{sequenceIndex}");
                unresolved = true;
                sequenceIndex++;
                continue;
            }
            if (!ShopSequenceSemantics.Matches(actual, condition.Count, condition.OrderMode, condition.Slots))
                return SearchQueryEvaluation.NoMatch($"MerchantColorlessSequenceRejected:{condition.Slot}:{sequenceIndex}");

            evidence.Add(new SearchMatchEvidence(
                "MerchantColorlessSequenceMatched",
                null,
                "merchant-colorless:canonical-sequence",
                EvidenceCode: new EvidenceCode(projection.EvidenceCode),
                ProfileId: authority.ProfileId,
                StreamDomain: "Shops",
                Authority: SourceAuthority.OfficialRuntimeExact,
                AuthorityFingerprint: merchantAuthority.AuthorityFingerprint,
                Ordinal: condition.Count,
                ConditionId: "merchant-colorless-sequence-" + sequenceIndex));
            sequenceIndex++;
        }

        return unresolved ? SearchQueryEvaluation.Unknown(projection.EvidenceCode) : SearchQueryEvaluation.Match(evidence);
    }
}
