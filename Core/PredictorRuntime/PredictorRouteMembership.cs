using System.Collections.Immutable;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorRun
{
    private static bool? MergeOptionalChange(bool? left, bool? right) =>
        left == true || right == true ? true : left == null || right == null ? null : false;

    private static ImmutableArray<PredictorCombatPendingCause> RouteRelicBagCauses(List<RouteDependency> dependencies,
        bool shared) => PredictorRouteCauseEvidence.Normalize(dependencies.Where(d =>
            (d.Cause.AffectedDomains & (PredictorCombatDomain.Relics | PredictorCombatDomain.RelicRewards)) != 0 &&
            (shared ? d.ChangesSharedRelicBag : d.ChangesPersonalRelicBag) != false).Select(d => d.Cause));
    private static ImmutableArray<string> MergeChangedEntries(ImmutableArray<string> left, ImmutableArray<string> right) =>
        left.IsDefault || right.IsDefault ? default : left.AddRange(right).Distinct(StringComparer.Ordinal).ToImmutableArray();

    private static ImmutableArray<PredictorCombatPendingCause> RouteRelicMembershipCauses(PredictorRun run,
        List<RouteDependency> dependencies, string entry) => PredictorRouteCauseEvidence.Normalize(dependencies
        .Where(d => (d.Cause.AffectedDomains & PredictorCombatDomain.Relics) != 0 && d.Cause.Code != "FirstPlayerTurn" &&
            (d.ChangedRelicEntries.IsDefault || d.ChangedRelicEntries.Contains(entry))).Select(d => d.Cause));

    private static ImmutableArray<PredictorCombatPendingCause> RouteRelicStateCauses(PredictorRun run,
        List<RouteDependency> dependencies, string entry) => !run.Working.Relics.Any(r => !r.Melted && r.Key.Entry == entry)
        ? [] : PredictorRouteCauseEvidence.Normalize(dependencies.Where(d =>
            (d.Cause.AffectedDomains & PredictorCombatDomain.Relics) != 0 && d.Cause.Code != "FirstPlayerTurn" &&
            (d.ChangedRelicStateEntries.IsDefault || d.ChangedRelicStateEntries.Contains(entry))).Select(d => d.Cause));

    private static ImmutableArray<PredictorCombatPendingCause> RouteCardStateCauses(PredictorRun run,
        List<RouteDependency> dependencies, string entry) => !run.Working.Deck.Any(id => run.Working.Card(id).Key.Entry == entry)
        ? [] : PredictorRouteCauseEvidence.Normalize(dependencies.Where(d =>
            (d.Cause.AffectedDomains & PredictorCombatDomain.Deck) != 0 &&
            !(entry == "DOWSING" && d.Cause.Code == "PartialUpgradeTargets") &&
            (d.ChangedCardStateEntries.IsDefault ? d.MayChangeExistingCards &&
                (d.AnyExistingCard || d.PossibleCards.IsDefaultOrEmpty || d.PossibleCards.Any(c => c.Key.Entry == entry)) :
                d.ChangedCardStateEntries.Contains(entry))).Select(d => d.Cause));
}
