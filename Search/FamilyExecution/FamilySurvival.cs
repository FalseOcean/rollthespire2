using System.Globalization;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

/// <summary>
/// Query-resolved analytical Survival for one Family. Runtime survivor counts,
/// physical implementation and Hardware evidence never enter this projection.
/// </summary>
public sealed record FamilySurvivalProjection
{
    private FamilySurvivalProjection(string familyId, double? survivalProbability, string evidence)
    {
        if (string.IsNullOrWhiteSpace(familyId)) throw new ArgumentException("Family id is required.", nameof(familyId));
        if (survivalProbability is double probability &&
            (!double.IsFinite(probability) || probability is < 0d or > 1d))
            throw new ArgumentOutOfRangeException(nameof(survivalProbability));
        FamilyId = familyId.Trim();
        SurvivalProbability = survivalProbability;
        Evidence = string.IsNullOrWhiteSpace(evidence) ? "Unspecified" : evidence.Trim();
    }

    public string FamilyId { get; }
    public double? SurvivalProbability { get; }
    public bool IsResolved => SurvivalProbability.HasValue;
    public string Evidence { get; }

    internal static FamilySurvivalProjection Resolved(string familyId, double probability, string evidence) =>
        new(familyId, probability, evidence);

    internal static FamilySurvivalProjection Unresolved(string familyId, string evidence) =>
        new(familyId, null, evidence);

    internal string FormatSummary() =>
        $"family={FamilyId};survivalProbability=" +
        (SurvivalProbability is double probability
            ? probability.ToString("G17", CultureInfo.InvariantCulture)
            : "unknown") +
        $";probabilityProjection={(IsResolved ? "resolved" : "unresolved")};evidence={Evidence.Replace(';', ',')}";
}

internal static class MerchantShopColorlessSurvival
{
    internal static FamilySurvivalProjection Project(ExactSearchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Beta111MerchantColorlessAuthority authority = Beta111MerchantColorlessAuthority.From(request.Authority);
        if (!authority.HasExactV1Inputs)
            return FamilySurvivalProjection.Unresolved(
                "S.MerchantShopColorless", "Probability.MerchantColorless.AuthorityMissing");

        double survival = 1d;
        var evidence = new List<string>(2);
        foreach (MerchantColorlessSlot lane in Enum.GetValues<MerchantColorlessSlot>())
        {
            MerchantColorlessSlotCondition[] slots = request.Evaluation.MerchantColorlessConditions
                .Where(condition => condition.Slot == lane).ToArray();
            MerchantColorlessSequenceSearchCondition[] sequences = request.Evaluation.MerchantColorlessSequenceConditions
                .Where(condition => condition.Slot == lane).ToArray();
            if (slots.Length == 0 && sequences.All(condition => condition.IsEmpty)) continue;

            IReadOnlyList<ModelKey> pool = lane == MerchantColorlessSlot.Uncommon
                ? authority.UncommonPool
                : authority.RarePool;
            if (!ShopSequenceProbabilityEstimator.TrySolveColorlessConjunction(
                    pool, slots, sequences, out double laneProbability, out string detail))
            {
                return FamilySurvivalProjection.Unresolved(
                    "S.MerchantShopColorless", $"Probability.MerchantColorless.{lane}:{detail}");
            }

            survival *= laneProbability;
            evidence.Add($"{lane}={F(laneProbability)}[{detail}]");
        }

        return FamilySurvivalProjection.Resolved(
            "S.MerchantShopColorless",
            Math.Clamp(survival, 0d, 1d),
            "Probability.Authority.MerchantColorlessFamilyExact;" + string.Join(';', evidence));
    }

    private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
}

internal static class RelicFamilySurvival
{
    internal static FamilySurvivalProjection Project(ExactSearchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SearchSelectivityEstimate estimate = SearchSelectivityEstimator.EstimateStage(
            SearchSelectivityInput.From(request), SearchSelectivityDomain.Relic);
        if (estimate.PricingClass != SearchSelectivityPricingClass.ExactPriced ||
            estimate.Probability is not double probability)
        {
            return FamilySurvivalProjection.Unresolved(
                "R.Relic", estimate.EvidenceCode + ";" + estimate.Notes);
        }

        return FamilySurvivalProjection.Resolved(
            "R.Relic",
            probability,
            estimate.EvidenceCode + ";Method=" + estimate.Method +
            $";OrdinaryConditions={request.Evaluation.RelicSequenceConditions.Count};" +
            $"ShopConditions={request.Evaluation.RelicShopSequenceConditions.Count}");
    }
}
