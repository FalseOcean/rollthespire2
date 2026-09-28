using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Derived-analysis input. This is not a semantic authority: it carries the immutable
/// CompiledSearch plus the exact/equivalent legacy numerical projection needed by the
/// existing probability estimators. No FastPlan, physical capability or Planner state exists here.
/// </summary>
internal sealed class SearchSelectivityInput
{
    private SearchSelectivityInput(
        CompiledSearch compiled,
        NeowSearchFilter filter,
        ProjectionFidelity projectionFidelity,
        CombatRewardRoutePolicyContract combatRewardRoutePolicy)
    {
        CompiledSearch = compiled;
        Filter = filter;
        ProjectionFidelity = projectionFidelity;
        CombatRewardRoutePolicy = combatRewardRoutePolicy;
    }

    public CompiledSearch CompiledSearch { get; }
    public CompiledSearch Semantics => CompiledSearch;
    public NeowSearchFilter Filter { get; }
    public ProjectionFidelity ProjectionFidelity { get; }
    public CombatRewardRoutePolicyContract CombatRewardRoutePolicy { get; }

    public RuntimeProfileId ProfileId => CompiledSearch.Context.ProfileId;
    public ModelKey CharacterKey => CompiledSearch.Context.CharacterKey;
    public int Ascension => CompiledSearch.Context.Ascension;
    public RuntimeContextAuthoritySnapshot Authority => CompiledSearch.Context.Authority;

    public static SearchSelectivityInput From(CompiledSearch compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        LegacySearchCriteriaProjection projection = LegacySearchCriteriaAdapter.Project(compiled);
        return new SearchSelectivityInput(
            compiled,
            projection.Filter,
            projection.Fidelity,
            BuildRewardPolicy(compiled));
    }

    public static SearchSelectivityInput From(SearchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SearchSelectivityInput(
            request.CompiledSearch,
            request.Filter,
            ProjectionFidelity.Exact,
            request.CombatRewardRoutePolicy);
    }

    public static SearchSelectivityInput From(ExactSearchExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new SearchSelectivityInput(
            request.CompiledSearch,
            request.Evaluation.ToLegacyFilter(),
            ProjectionFidelity.Exact,
            request.CombatRewardRoutePolicy);
    }

    public static implicit operator SearchSelectivityInput(CompiledSearch compiled) => From(compiled);
    public static implicit operator SearchSelectivityInput(SearchExecutionRequest request) => From(request);

    private static CombatRewardRoutePolicyContract BuildRewardPolicy(CompiledSearch compiled) =>
        compiled.ResolvedRouteSemantics.CombatReward.Kind switch
        {
            ResolvedCombatRewardRouteKind.NotApplicable => CombatRewardRoutePolicyContract.None,
            ResolvedCombatRewardRouteKind.PinnedOpeningRoute => new CombatRewardRoutePolicyContract(
                // Derived Probability/Presentation must observe the same product split as
                // Production Search: C Fast is query-literal synthetic while Exact remains
                // authoritative on the real pinned opening route.
                CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed,
                CombatRewardExactRoutePolicy.PinnedRealRoute),
            ResolvedCombatRewardRouteKind.NeutralNonPerturbingContinuation => new CombatRewardRoutePolicyContract(
                CombatRewardFastRoutePolicy.UnpinnedAssumeUnperturbed,
                CombatRewardExactRoutePolicy.UnpinnedVerifyNeutralRealRoute),
            _ => CombatRewardRoutePolicyContract.None
        };
}
