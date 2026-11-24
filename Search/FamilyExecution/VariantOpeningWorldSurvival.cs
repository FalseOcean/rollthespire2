using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;

namespace RolltheSpire2.Search.FamilyExecution;

// A projection of the predicates in the selected numerical W body. It never
// uses whole-query q to claim that an unexecuted predicate has already passed.
internal sealed record VariantOpeningWorldSurvival(
    double? VariantProbability,
    double? WorldProbability,
    double? JointVariantWorldProbability,
    bool WorldGuaranteesVariant,
    string Evidence)
{
    private const string VariantFamily = "V.VariantOpening";
    private const string WorldFamily = "W.World";

    internal FamilySurvivalProjection ResolveVariant(IReadOnlySet<string> passedCoverage,
        double? checkedUnknownPrefixProbability)
    {
        double? variants = passedCoverage.Contains(WorldFamily)
            ? WorldGuaranteesVariant ? 1 : Conditional(JointVariantWorldProbability, WorldProbability)
            : VariantProbability;
        return Projection(VariantFamily, Product(variants, checkedUnknownPrefixProbability),
            "V.ActualVariantAndCheckedUnknownPrefix;" + Evidence);
    }

    internal FamilySurvivalProjection ResolveWorld(IReadOnlySet<string> passedCoverage) =>
        Projection(WorldFamily, passedCoverage.Contains(VariantFamily)
            ? Conditional(JointVariantWorldProbability, VariantProbability) : WorldProbability,
            "W.ActualCompiledPredicates;ConditionalOnPassedV;" + Evidence);

    internal static VariantOpeningWorldSurvival Create(SearchModel request,
        VariantOpeningPreparation prepared, Beta110WorldFastPlan? actualWorldPlan)
    {
        var generation = request.Authority.WorldAuthority?.Beta109Generation;
        bool noVariant = !prepared.HasVariantRestrictions;
        VariantOpeningWorldSurvival Unknown(string why, double? a = null) =>
            new(noVariant ? 1 : a, null, null, noVariant, why);
        if (prepared.AuthorityFingerprint != request.CompiledSearch.Context.WorldSnapshotFingerprint)
            return Unknown("V.PreparedAuthorityMismatch");
        if (generation is null || !generation.ActSelectionAuthorityExact ||
            generation.ActSelectionGroups.GroupBy(group => group.Act).Any(group => group.Count() != 1) ||
            generation.ActSelectionGroups.Any(group => !group.EligibilityAndOrderExact || group.EligibleActsInSourceOrder.Count == 0))
            return actualWorldPlan is null
                ? new(noVariant ? 1 : null, 1, noVariant ? 1 : null, noVariant, "V.VariantPriorUnavailable;NoWFilter")
                : Unknown("V.VariantPriorUnavailable");
        var groups = generation.ActSelectionGroups.OrderBy(group => group.Act).ToArray();
        var priors = groups.Select(group => WorldProbabilityEstimator.ResolveVariantPriors(generation, group)).ToArray();
        if (priors.Any(values => values.Count == 0 || Math.Abs(values.Sum(value => value.Prior) - 1) > 1e-12) ||
            prepared.Acts.Any(row => !groups.Any(group => group.Act == row.Act)))
            return Unknown("V.VariantPriorIncomplete");
        double pA = 1;
        for (int i = 0; i < groups.Length; i++)
            pA *= priors[i].Where(value => prepared.Allows(groups[i].Act, value.Key)).Sum(value => value.Prior);
        if (actualWorldPlan is null)
            return new(pA, 1, pA, pA == 1, "NoWFilter;OriginalVariantWeights;ModeledNotObserved");
        if (!actualWorldPlan.Enabled) return Unknown("W.CompiledPlanUnavailable", pA);
        if (actualWorldPlan.AlwaysRejectCandidate)
            return new(pA, 0, 0, true, "W.ActualAlwaysReject;OriginalVariantWeights");
        var plans = new Dictionary<(int Act, ModelKey Key), WorldFastActPlan>();
        foreach (var group in groups)
        {
            var matchingGroups = actualWorldPlan.ActSelectionGroups.Where(item => item.Act == group.Act).ToArray();
            if (matchingGroups.Length != 1) return Unknown("W.ActSelectionProjectionMissingOrAmbiguous", pA);
            var physical = matchingGroups[0];
            if (physical.EligibleActIds is null || physical.EligibleActIds.Length != group.EligibleActsInSourceOrder.Count ||
                physical.SelectionMode != (group.SelectionMode == Beta109ActSelectionMode.DeterministicFirst ? 1 : 0))
                return Unknown("W.ActSelectionProjectionMismatch", pA);
            for (int i = 0; i < physical.EligibleActIds.Length; i++)
            {
                ushort id = physical.EligibleActIds[i];
                if (!TryAct(actualWorldPlan, id, group.Act, out var act)) return Unknown("W.ActPlanMissing", pA);
                var identity = (group.Act, group.EligibleActsInSourceOrder[i]);
                if (plans.TryGetValue(identity, out var prior) && prior.ActId != act.ActId)
                    return Unknown("W.ActIdentityProjectionAmbiguous", pA);
                plans[identity] = act;
            }
        }
        if (generation.Act1OverrideResolvedKey is { } overrideKey)
        {
            if (!TryAct(actualWorldPlan, actualWorldPlan.Act1OverrideId, 1, out var act))
                return Unknown("W.Act1OverrideProjectionMissing", pA);
            plans[(1, overrideKey)] = act;
        }
        double? pW = 1, pAW = 1;
        bool guarantees = true;
        var notes = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < groups.Length; i++)
        {
            double? world = 0, joint = 0;
            foreach (var (key, weight) in priors[i])
            {
                if (!plans.TryGetValue((groups[i].Act, key), out var act)) return Unknown("W.SelectedActPlanMissing", pA);
                double? accepted = EstimateAct(actualWorldPlan, act, groups[i].Act == groups[^1].Act, out string reason);
                notes.Add(reason);
                world = Sum(world, Scale(accepted, weight));
                bool allowed = prepared.Allows(groups[i].Act, key);
                joint = Sum(joint, allowed ? Scale(accepted, weight) : 0);
                // A direct zero from this actual body's predicates is sufficient
                // even if an unrelated Ancient probability remains unavailable.
                if (!allowed && weight > 0 && accepted != 0) guarantees = false;
            }
            pW = Product(pW, world);
            pAW = Product(pAW, joint);
        }
        if (pW == 0) guarantees = true;
        return new(pA, pW, pAW, guarantees,
            "CompiledWPredicateMixture;OriginalVariantWeights;ConditionalBossEventAndCrossActAssumedIndependent;" +
            "NoQueryProbabilityOrObservedSurvivors;" + string.Join(';', notes.Order(StringComparer.Ordinal)));
    }

    private static bool TryAct(Beta110WorldFastPlan plan, ushort id, int actNumber, out WorldFastActPlan act)
    {
        act = null!;
        if (id >= plan.ActPlanIndexByDenseId.Length) return false;
        int index = plan.ActPlanIndexByDenseId[id];
        if (index < 0 || index >= plan.Acts.Length || plan.Acts[index].Act != actNumber || plan.Acts[index].ActId != id) return false;
        act = plan.Acts[index];
        return true;
    }

    private static double? EstimateAct(Beta110WorldFastPlan plan, WorldFastActPlan act, bool finalAct, out string evidence)
    {
        if (!act.FamilyVariantAllowed) { evidence = "W.ActualVariantReject"; return 0; }
        if (!plan.RequiresSecondBoss && act.Act > plan.MaxRequiredAct) { evidence = "W.NoObservationAtAct"; return 1; }
        if (act.HasEncounterPredicate) { evidence = "W.EncounterQueueProbabilityUnpriced"; return null; }
        double? events = EstimateEvents(act, out string eventEvidence);
        double? bosses = EstimateBosses(plan, act, finalAct, out string bossEvidence);
        var ancient = EstimateAncientIdentity(plan, act);
        evidence = eventEvidence + ";" + bossEvidence + (act.HasAncientIdentityPredicate
            ? ancient.HasValue ? ";W.CompiledAncientIdentityPoolMixture" : ";W.AncientIdentityMixtureUnavailable" : "");
        return Product(Product(events, bosses), ancient);
    }

    // W evaluates identity; the selected A body owns options and Sea Glass.
    // Mirror AssignSharedAncients: a shuffled shared prefix of uniform length
    // goes to each act after the first, leaving the remainder for later acts.
    internal static double? EstimateAncientIdentity(Beta110WorldFastPlan plan, WorldFastActPlan act)
    {
        if (!act.HasAncientIdentityPredicate) return 1;
        int slot = Array.FindIndex(plan.ActSelectionGroups, group => group.Act == act.Act);
        if (slot < 0 || act.Ancients.Length == 0) return null;
        bool Accepts(ushort key) => Matches([key], act.AncientIdentityPredicates) &&
            Matches([key], act.AncientBranchIdentityPredicates);
        int localHits = act.Ancients.Count(Accepts);
        int shared = plan.SharedAncients.Length;
        if (slot == 0 || shared == 0) return localHits / (double)act.Ancients.Length;
        // Two later identities may compete for the very same shared entry.
        // The per-act product used by this projection cannot represent that joint.
        if (plan.Acts.Any(other => other.Act != act.Act && other.Act != plan.ActSelectionGroups[0].Act &&
                other.HasAncientIdentityPredicate)) return null;
        double sharedHitRate = plan.SharedAncients.Count(Accepts) / (double)shared;
        var counts = new double[shared + 1];
        counts[shared] = 1;
        for (int step = 0; step < slot; step++)
        {
            // For r still available, both the new remainder and the assigned
            // length have the same marginal, uniform over [0,r].
            double tail = 0;
            for (int n = shared; n >= 0; n--)
            {
                tail += counts[n] / (n + 1);
                counts[n] = tail;
            }
        }
        double result = 0;
        for (int n = 0; n <= shared; n++)
            result += counts[n] * (localHits + n * sharedHitRate) / (act.Ancients.Length + n);
        return Math.Clamp(result, 0, 1);
    }

    private static double? EstimateEvents(WorldFastActPlan act, out string evidence)
    {
        evidence = "W.EventCompiledOpeningSkipMixture";
        if (act.EventPredicates.Length == 0) return 1;
        var raw = act.EventCandidates;
        var constraints = new List<FiniteSequenceProbabilitySolver.Constraint>();
        foreach (var predicate in act.EventPredicates)
        {
            if (predicate.Keys.AlwaysReject) return 0;
            if (predicate.RangeMode > 1 || predicate.RangeValue > int.MaxValue)
            { evidence = "W.EventPhysicalShapeUnavailable"; return null; }
            // Establish cheap necessary impossibility before invoking a bounded
            // sequence solver. This remains useful when another block is Unknown.
            var candidates = raw.Where(item => item.StaticEligible &&
                (predicate.SourceFilter == byte.MaxValue || predicate.SourceFilter == item.Source)).Select(item => item.EventId).ToHashSet();
            if (predicate.Keys.All.Any(key => !candidates.Contains(key)) ||
                predicate.Keys.Any.Length > 0 && !predicate.Keys.Any.Any(candidates.Contains)) return 0;
            constraints.Add(new(predicate.RangeMode == 0 ? SearchSequenceRangeMode.FirstN : SearchSequenceRangeMode.ExactSlot,
                (int)predicate.RangeValue, Keys(predicate.Keys), predicate.SourceFilter == byte.MaxValue ? null : predicate.SourceFilter,
                ExactSlotRequiresPartitionMatch: true));
        }
        if (raw.Length == 0)
            return Solve([], constraints, out evidence);
        double result = 0;
        for (int skip = 0; skip < raw.Length; skip++)
        {
            var items = raw.Where((item, index) => index != skip && item.StaticEligible)
                .Select(item => new FiniteSequenceProbabilitySolver.Item(Key(item.EventId), item.Source)).ToArray();
            var branch = Solve(items, constraints, out string reason);
            if (branch is null) { evidence = "W.EventPhysicalProbabilityUnavailable:" + reason; return null; }
            result += branch.Value / raw.Length;
        }
        return Math.Clamp(result, 0, 1);
    }

    private static double? Solve(IReadOnlyList<FiniteSequenceProbabilitySolver.Item> pool,
        IReadOnlyList<FiniteSequenceProbabilitySolver.Constraint> constraints, out string evidence)
    {
        if (!FiniteSequenceProbabilitySolver.TrySolve(pool, constraints, out var result, out evidence)) return null;
        return result.Probability;
    }

    private static double? EstimateBosses(Beta110WorldFastPlan plan, WorldFastActPlan act, bool finalAct, out string evidence)
    {
        evidence = "W.BossCompiledOutcomeEnumeration";
        if (!act.HasBossPredicate) return 1;
        if (act.Bosses.Length == 0) { evidence = "W.BossPoolUnavailable"; return null; }
        bool second = plan.RequiresSecondBoss && finalAct;
        // Outside the actual second-boss body ordinal-two predicates do not run.
        ushort[] firstPool = act.FamilyFirstBossOverride == ushort.MaxValue ? act.Bosses : [act.FamilyFirstBossOverride];
        double result = 0;
        foreach (ushort first in firstPool)
        {
            if (!Matches([first], act.BossOrdinalOnePredicates) ||
                !Beta110WorldFastStage.MatchesBossBranches(act, first)) continue;
            bool deferAct = plan.Ascension >= 10 && act.Act == 3 && act.BossActPredicates.Length > 0;
            if (!deferAct && !Matches([first], act.BossActPredicates)) continue;
            if (!second) { result += 1d / firstPool.Length; continue; }
            ushort[] secondPool = act.Bosses.Where(key => key != first).ToArray();
            if (secondPool.Length == 0) { evidence = "W.SecondBossPoolUnavailable"; return null; }
            int accepted = secondPool.Count(other => Matches([other], act.BossOrdinalTwoPredicates) &&
                Matches([first, other], act.BossActPredicates) && Beta110WorldFastStage.MatchesBossBranches(act, first, other));
            result += accepted / (double)secondPool.Length / firstPool.Length;
        }
        return Math.Clamp(result, 0, 1);
    }

    private static bool Matches(ushort[] outcome, WorldFastDenseSetPredicate[] predicates) => predicates.All(predicate =>
        !predicate.AlwaysReject && (predicate.Any.Length == 0 || predicate.Any.Any(outcome.Contains)) &&
        predicate.All.All(outcome.Contains) && !predicate.Ban.Any(outcome.Contains));
    private static ModelKey Key(ushort id) => new("EVENT", "W_DENSE_" + id);
    private static ModelKeySetFilter Keys(WorldFastDenseSetPredicate predicate) =>
        new(predicate.Any.Select(Key).ToArray(), predicate.All.Select(Key).ToArray(), predicate.Ban.Select(Key).ToArray());
    private static double? Scale(double? value, double weight) => weight == 0 ? 0 : value * weight;
    private static double? Sum(double? left, double? right) => left.HasValue && right.HasValue ? left + right : null;
    private static double? Product(double? left, double? right) => left == 0 || right == 0 ? 0 :
        Valid(left) && Valid(right) ? left * right : null;
    private static double? Conditional(double? joint, double? denominator) =>
        Valid(joint) && denominator is > 0 and <= 1 ? Normalize(joint!.Value / denominator.Value) : null;
    private static bool Valid(double? value) => value is >= 0 and <= 1 && double.IsFinite(value.Value);
    private static double? Normalize(double value) => double.IsFinite(value) && value >= -1e-12 && value <= 1 + 1e-12
        ? Math.Clamp(value, 0, 1) : null;
    private static FamilySurvivalProjection Projection(string family, double? probability, string evidence) =>
        Valid(probability) ? FamilySurvivalProjection.Resolved(family, probability!.Value, evidence) :
            FamilySurvivalProjection.Unresolved(family, evidence + ";PhysicalConditionalProbabilityUnknown");
}
