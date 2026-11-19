using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

/// <summary>
/// At least one of the two ordinary slots offers TargetRelicKey in this inclusive
/// pristine-shop window. Separate rows are conjunctive. The source is the initial
/// personal relic bag, before pickups, with no intervening special merchant or refill.
/// </summary>
public sealed record MerchantRelicCondition(int FirstMerchant, int LastMerchant, ModelKey TargetRelicKey)
{
    internal const string Domain = "MerchantRelic";
    internal const string UnsupportedCode = Domain + ".SinglePlayerModern113Required";
}

internal static class MerchantRelicQueryEvaluator
{
    internal static SearchQueryEvaluation Evaluate(IReadOnlyList<MerchantRelicCondition> conditions,
        RuntimeContextAuthoritySnapshot authority, ulong rootHash)
    {
        if (conditions.Count == 0) return SearchQueryEvaluation.Match();
        var bags = RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, rootHash,
            "root-hash:" + rootHash, authority.WorldAuthority);
        var projection = NormalMerchantRelicProjector.Project(rootHash, authority, bags,
            merchantCount: conditions.Max(c => c.LastMerchant));
        return EvaluateProjection(conditions, projection, authority);
    }

    internal static SearchQueryEvaluation EvaluateProjection(IReadOnlyList<MerchantRelicCondition> conditions,
        NormalMerchantRelicProjection projection, RuntimeContextAuthoritySnapshot authority)
    {
        var evidence = new List<SearchMatchEvidence>();
        bool unresolved = false;
        for (int index = 0; index < conditions.Count; index++)
        {
            var row = conditions[index];
            bool missing = false;
            bool matched = false;
            for (int ordinal = row.FirstMerchant; ordinal <= row.LastMerchant; ordinal++)
            {
                var shop = projection.Merchants.FirstOrDefault(m => m.MerchantOrdinal == ordinal);
                if (shop is null)
                {
                    missing = true;
                    continue;
                }
                if (shop.Left.RelicKey == row.TargetRelicKey || shop.Middle.RelicKey == row.TargetRelicKey)
                {
                    evidence.Add(new("MerchantRelic.Offered", row.TargetRelicKey,
                        OutcomeId: shop.Left.RelicKey == row.TargetRelicKey ? "Left" : "Middle",
                        ProfileId: authority.ProfileId, StreamDomain: "Shops+RelicGrabBag",
                        AuthorityFingerprint: authority.WorldSnapshotFingerprint,
                        Ordinal: ordinal, ConditionId: "merchant-relic-" + index));
                    matched = true;
                    break;
                }
                missing |= !shop.Left.RelicKey.HasValue || !shop.Middle.RelicKey.HasValue;
            }
            if (matched) continue;
            if (!missing) return SearchQueryEvaluation.NoMatch("MerchantRelic.Rejected:" + index);
            unresolved = true;
        }
        return unresolved ? SearchQueryEvaluation.Unknown("MerchantRelic.Incomplete:" + projection.IssueCode)
            : SearchQueryEvaluation.Match(evidence);
    }
}
