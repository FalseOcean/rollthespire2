using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Only the authority coordinates used by retained numerical reference prices.
// No query, scan schedule, runtime object or observation is retained here.
internal sealed record FilterPricingAuthority(RuntimeProfileId ProfileId, bool CanUseCurrentModel,
    int PlayersCount, int Ascension, bool? AllCharacterCardPoolsUnlocked, ModelKey CharacterKey)
{
    internal static FilterPricingAuthority From(SearchModel model) => new(model.ProfileId,
        model.Authority.CanUseCurrentModel, model.Authority.PlayersCount, model.Ascension,
        model.Authority.AllCharacterCardPoolsUnlocked, model.CharacterKey) {
            NoRunModifiers = model.Authority.NoRunModifiers,
            UsesBestEffortModel = model.Authority.UsesBestEffortModel
        };
    internal bool? NoRunModifiers { get; init; }
    internal bool UsesBestEffortModel { get; init; }

    internal bool AdmitsCpuReference(bool equivalentIdentityWork) => ProfileId == RuntimeProfileId.Beta111 &&
        CanUseCurrentModel && PlayersCount == 1 && Ascension == 10 &&
        (equivalentIdentityWork || CharacterKey == BaseGameModelKeys.Characters.Ironclad) &&
        AllCharacterCardPoolsUnlocked == true;
}

internal sealed record CombatRewardPricingInput(FilterPricingAuthority Authority, int? FixedAuthoredPrefixDrawCount)
{
    internal RuntimeProfileId ProfileId => Authority.ProfileId;
    internal int Ascension => Authority.Ascension;
    internal static CombatRewardPricingInput From(SearchModel model, CombatRewardReplay replay) =>
        new(FilterPricingAuthority.From(model), CombatRewardSieveProbability.FixedAuthoredPrefixDrawCount(model, replay.Plan));
}

internal sealed record AncientOptionPricingInput(FilterPricingAuthority Authority, ExactSearchEvaluationProjection Evaluation)
{
    internal static AncientOptionPricingInput From(SearchModel model) => new(FilterPricingAuthority.From(model),
        ExactSearchEvaluationProjection.Empty with {
            AncientBranchConditions = model.Evaluation.AncientBranchConditions,
            AncientOptionFilters = model.Evaluation.AncientOptionFilters,
            AncientSeaGlassTargetFilters = model.Evaluation.AncientSeaGlassTargetFilters,
            AncientOptionConditions = model.Evaluation.AncientOptionConditions
        });
    internal RuntimeProfileId ProfileId => Authority.ProfileId;
}
