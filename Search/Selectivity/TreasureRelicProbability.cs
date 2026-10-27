using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Selectivity;

// Initial shared bags are uniform permutations. Only the first two ordinary
// treasure encounters are modeled, with the existing upward rarity fallback.
internal static class TreasureRelicProbability
{
    internal const string ComponentId = "R.InitialTreasure";
    internal static SearchSelectivityEstimate Estimate(SearchSelectivityInput input)
    {
        var condition = input.CompiledSearch.NormalizedQuery.TreasureRelics;
        SearchSelectivityEstimate Unknown(string issue) => SearchSelectivityEstimate.Unpriced(
            "Probability.Treasure." + issue, "The initial shared relic bags or supported treasure context are unavailable.");
        if (condition is null || condition.IsEmpty) return Result(1);
        if (!condition.IsValid) return Unknown("InvalidCondition");
        if (input.ProfileId != RuntimeProfileId.Beta111) return Unknown("ProfileUnsupported");
        if (input.CompiledSearch.Context.Party is not null || input.Authority.PlayersCount != 1)
            return Unknown("PartyAllocationUnmodeled");
        var acquired = ExplicitOpeningRelicFacts.EnumerateGuaranteed(input.CompiledSearch.NormalizedQuery).ToHashSet();
        var required = condition.Slots.Take(condition.Count).ToArray();
        var usedOpening = new HashSet<ModelKey>();
        for (int slot = 0; slot < required.Length; slot++)
            if (required[slot] is { } target && acquired.Contains(target) && usedOpening.Add(target)) required[slot] = null;
        // Exact accepts each actual opening acquisition as one goal witness,
        // even when unrelated future treasure observations are unavailable.
        if (required.All(key => key is null)) return Result(1);
        var generation = input.Authority.WorldAuthority?.Beta109Generation;
        if (generation is null || generation.SharedRelicBuckets.Count == 0 ||
            generation.SharedRelicBuckets.Any(bucket => !bucket.OrderExact)) return Unknown("SharedBagAuthorityMissing");
        string[] rarities = ["Common", "Uncommon", "Rare"];
        var bags = new ModelKey[3][];
        var seen = new HashSet<ModelKey>();
        for (int lane = 0; lane < 3; lane++)
        {
            var candidates = generation.SharedRelicBuckets.Where(bucket =>
                bucket.BucketId.Split(':').Last().Equals(rarities[lane], StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1) return Unknown("SharedLaneMissingOrAmbiguous:" + rarities[lane]);
            bags[lane] = candidates[0].OrderedRelics.Where(key => !acquired.Contains(key) && key.Entry != "MASSIVE_SCROLL").ToArray();
            if (bags[lane].Any(key => !key.IsValid || !seen.Add(key))) return Unknown("SharedIdentityInvalidOrDuplicated");
        }
        ModelKey[] targets = required.Where(key => key.HasValue).Select(key => key!.Value).Distinct().ToArray();
        int[] targetLanes = targets.Select(key => Array.FindIndex(bags, bag => bag.Contains(key))).ToArray();
        int circlet = Array.FindIndex(targets, key => key.Entry == "CIRCLET");
        bool suppression = acquired.Any(key => key.Entry == "SILVER_CRUCIBLE");
        int end = condition.StartOrdinal + condition.Count - 1;
        var outputs = new int[end];
        var taken = new int[3];
        double Visit(int ordinal, int used)
        {
            if (ordinal == end)
            {
                int windowStart = condition.StartOrdinal - 1;
                if (condition.OrderMode == TreasureRelicOrderMode.Ordered)
                    return Enumerable.Range(0, condition.Count).All(slot => required[slot] is not { } target ||
                        outputs[windowStart + slot] == Array.IndexOf(targets, target)) ? 1 : 0;
                int assigned = 0;
                foreach (var target in required.Where(key => key.HasValue))
                {
                    int wanted = Array.IndexOf(targets, target!.Value);
                    int index = Enumerable.Range(windowStart, condition.Count).FirstOrDefault(i =>
                        (assigned & (1 << i)) == 0 && outputs[i] == wanted, -1);
                    if (index < 0) return 0;
                    assigned |= 1 << index;
                }
                return 1;
            }
            if (suppression && ordinal == 0) { outputs[ordinal] = -2; return Visit(ordinal + 1, used); }
            double total = 0;
            double[] mass = [.5, .33, .17];
            for (int rolled = 0; rolled < 3; rolled++)
            {
                int lane = rolled;
                while (lane < 3 && taken[lane] == bags[lane].Length) lane++;
                if (lane == 3) { outputs[ordinal] = circlet; total += mass[rolled] * Visit(ordinal + 1, used); continue; }
                int remaining = bags[lane].Length - taken[lane];
                var available = Enumerable.Range(0, targets.Length).Where(index => targetLanes[index] == lane && (used & (1 << index)) == 0).ToArray();
                taken[lane]++;
                foreach (int index in available)
                { outputs[ordinal] = index; total += mass[rolled] / remaining * Visit(ordinal + 1, used | (1 << index)); }
                int others = remaining - available.Length;
                if (others > 0) { outputs[ordinal] = -1; total += mass[rolled] * others / remaining * Visit(ordinal + 1, used); }
                taken[lane]--;
            }
            return total;
        }
        return Result(Visit(0, 0));
    }

    private static SearchSelectivityEstimate Result(double p) => SearchSelectivityEstimate.Exact(p,
        SearchSelectivityMethod.WithoutReplacement, SearchSelectivityCoverage.ExactRequestedConjunction,
        SearchSelectivityDependencyClass.AssumedIndependent, "Probability.Treasure.InitialSharedBag",
        "First two ordinary encounters; rarity mass .5/.33/.17, upward empty-lane fallback, shared front without replacement.",
        ["UnspecifiedOpeningOutcomesHaveNoInfluence=OwnerApproved", "OnlyExplicitAcquisitionsRemoved=true",
            "TreasureAndOtherConditions=OwnerApprovedIndependentApproximation", "ExactReplayAndHeldStateUnchanged=true"]);

    internal static JointSelectivityComponent EstimateComponent(SearchSelectivityInput input)
    {
        var estimate = Estimate(input);
        return new(ComponentId, "TreasureRelics", "Initial shared bags", estimate.Probability, estimate.EvidenceCode,
            JointSelectivityCombinationMethod.IndependentProduct, SearchSelectivityDependencyClass.AssumedIndependent,
            estimate.IsPriced, estimate.Probability > 0, estimate.Notes, estimate.Assumptions);
    }

    internal static JointSelectivityResult Compose(SearchSelectivityInput input, Func<SearchSelectivityInput, JointSelectivityResult> other)
    {
        var query = input.CompiledSearch.NormalizedQuery;
        if (input.CompiledSearch.Context.Party is not null)
            return JointSelectivityResult.Unpriced("Probability.Treasure.PartyAllocationUnmodeled", "Party treasure allocation is outside the current supported authoring surface.", "No personal allocation is invented.");
        var remainder = SearchCompiler.Compile(query with { TreasureRelics = null }, input.CompiledSearch.Context);
        return FirstActUnknownEventProbability.Combine(other(SearchSelectivityInput.From(remainder)), EstimateComponent(input), "Treasure");
    }
}
